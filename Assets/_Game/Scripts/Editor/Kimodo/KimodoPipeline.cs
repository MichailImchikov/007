using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FPS.EditorTools.Anim;
using UnityEditor;
using UnityEngine;
using HB = UnityEngine.HumanBodyBones;

namespace FPS.EditorTools.Kimodo
{
    /// <summary>
    /// Kimodo → game clips: talks to the bridge server (Tools/Kimodo/kimodo_bridge.py), retargets the SOMA motion
    /// to the humanoid, cleans it up for the game (in place, loop, phase, speed, jump phases) and writes
    /// Assets/_Game/Animation/Kimodo/&lt;slot&gt;.anim. The soldier controller builder prefers these clips.
    /// </summary>
    public static class KimodoPipeline
    {
        public const string ClipDir = "Assets/_Game/Animation/Kimodo";
        public const string LibraryPath = ClipDir + "/KimodoLibrary.asset";
        const string ProbePrefab = "Assets/Military/Prefab/URP/SK_Military_CHR1_URP.prefab";

        public static string RawDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/Kimodo/Output"));

        static readonly HttpClient http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

        // ------------------------------------------------------------------ status (polled by the window / MCP)

        public static bool Busy { get; private set; }
        public static string Status { get; private set; } = "idle";
        public static readonly List<string> Log = new List<string>();

        static void Say(string s)
        {
            Status = s;
            Log.Add(DateTime.Now.ToString("HH:mm:ss ") + s);
            Debug.Log("[Kimodo] " + s);
        }

        public static KimodoLibrary Library()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Animation")) AssetDatabase.CreateFolder("Assets/_Game", "Animation");
            if (!AssetDatabase.IsValidFolder(ClipDir)) AssetDatabase.CreateFolder("Assets/_Game/Animation", "Kimodo");
            var lib = AssetDatabase.LoadAssetAtPath<KimodoLibrary>(LibraryPath);
            if (!lib)
            {
                lib = ScriptableObject.CreateInstance<KimodoLibrary>();
                lib.ResetToDefaults();
                AssetDatabase.CreateAsset(lib, LibraryPath);
                AssetDatabase.SaveAssets();
            }
            return lib;
        }

        // ------------------------------------------------------------------ http

        [Serializable] public class Health { public string status; public string name; public float fps; public string device; public string error; }
        [Serializable] public class OutFile { public string bvh; public string npz; }
        [Serializable] public class GenResponse { public OutFile[] files; public float fps; public int frames; public float seconds; }

        public static async Task<Health> CheckHealth(string url)
        {
            try
            {
                var s = await http.GetStringAsync(url.TrimEnd('/') + "/health").ConfigureAwait(false);
                return JsonUtility.FromJson<Health>(s);
            }
            catch (Exception e) { return new Health { status = "offline", error = e.GetBaseException().Message }; }
        }

        static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);
        static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        /// <summary>Request JSON: prompt, duration, root path (Kimodo space: x = character's left, z forward), heading forward.</summary>
        static string RequestJson(KimodoPreset p, KimodoLibrary lib, string outDir, int samples)
        {
            const float fps = 30f;
            var sb = new StringBuilder();
            sb.Append("{\"name\":").Append(Q(p.slot));
            sb.Append(",\"prompts\":[").Append(Q(p.prompt)).Append("]");
            sb.Append(",\"durations\":[").Append(F(Mathf.Clamp(p.duration, 0.5f, 10f))).Append("]");
            sb.Append(",\"num_samples\":").Append(samples);
            sb.Append(",\"seed\":").Append(p.seed);
            sb.Append(",\"diffusion_steps\":").Append(lib.diffusionSteps);
            sb.Append(",\"cfg\":[").Append(F(lib.cfg.x)).Append(",").Append(F(lib.cfg.y)).Append("]");
            sb.Append(",\"postprocess\":true");
            sb.Append(",\"out_dir\":").Append(Q(outDir.Replace('\\', '/')));
            if (p.constrainPath)
            {
                int n = Mathf.Max(2, Mathf.RoundToInt(Mathf.Clamp(p.duration, 0.5f, 10f) * fps));
                var idx = new StringBuilder(); var pts = new StringBuilder(); var hd = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    float t = i / fps;
                    if (i > 0) { idx.Append(','); pts.Append(','); hd.Append(','); }
                    idx.Append(i);
                    // Unity x (right) → Kimodo x (left): mirrored
                    pts.Append('[').Append(F(-p.velocity.x * t)).Append(',').Append(F(p.velocity.y * t)).Append(']');
                    hd.Append("[1,0]");
                }
                sb.Append(",\"constraints\":[{\"type\":\"root2d\",\"frame_indices\":[").Append(idx).Append("],\"smooth_root_2d\":[")
                  .Append(pts).Append("],\"global_root_heading\":[").Append(hd).Append("]}]");
            }
            sb.Append('}');
            return sb.ToString();
        }

        static async Task<GenResponse> Generate(KimodoPreset p, KimodoLibrary lib, int samples)
        {
            Directory.CreateDirectory(RawDir);
            var body = new StringContent(RequestJson(p, lib, RawDir, samples), Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(lib.serverUrl.TrimEnd('/') + "/generate", body).ConfigureAwait(false);
            string s = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) throw new Exception($"server {(int)resp.StatusCode}: {s}");
            return JsonUtility.FromJson<GenResponse>(s);
        }

        // ------------------------------------------------------------------ queue

        // slots still to do, kept across domain reloads (a script compile mid-run would otherwise drop the queue silently)
        const string QueueKey = "FPS.Kimodo.PendingSlots";

        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            string q = SessionState.GetString(QueueKey, "");
            if (string.IsNullOrEmpty(q)) return;
            EditorApplication.delayCall += () =>
            {
                if (Busy) return;
                Debug.Log("[Kimodo] resuming the queue after a domain reload: " + q.Replace("|", ", "));
                Run(q.Split('|'));
            };
        }

        /// <summary>Generates and imports the given slots (all enabled presets when null), one after another.
        /// The remaining slots survive a domain reload and the queue resumes by itself.</summary>
        public static async void Run(IEnumerable<string> slots = null)
        {
            if (Busy) { Debug.LogWarning("[Kimodo] already running"); return; }
            Busy = true;
            Log.Clear();
            try
            {
                var lib = Library();
                var list = lib.presets.Where(p => slots == null ? p.enabled : slots.Contains(p.slot)).ToList();
                SessionState.SetString(QueueKey, string.Join("|", list.Select(p => p.slot)));
                var h = await CheckHealth(lib.serverUrl);
                if (h.status != "ok") { Say($"server not ready: {h.status} {h.error}"); return; }
                for (int i = 0; i < list.Count; i++)
                {
                    var p = list[i];
                    if (!string.IsNullOrEmpty(p.reverseOf))
                        Say($"[{i + 1}/{list.Count}] {p.slot}: " + (ImportReversed(p) ? $"{p.reverseOf} played backward" : $"no {p.reverseOf} clip to reverse yet, skipped"));
                    else
                    {
                        Say($"[{i + 1}/{list.Count}] generating {p.slot}: \"{p.prompt}\"");
                        var r = await Generate(p, lib, 1);
                        Say($"[{i + 1}/{list.Count}] {p.slot} generated in {r.seconds:F1}s ({r.frames} frames), importing");
                        Import(p, r.files[0].bvh);
                    }
                    SessionState.SetString(QueueKey, string.Join("|", list.Skip(i + 1).Select(x => x.slot)));
                }
                AssetDatabase.SaveAssets();
                Say($"done: {list.Count} clip(s)");
            }
            catch (Exception e) { Say("error: " + e.GetBaseException().Message); Debug.LogException(e); }
            finally
            {
                // reached on completion / error only: a domain reload abandons the task and keeps the queue
                SessionState.EraseString(QueueKey);
                Busy = false;
            }
        }

        /// <summary>Re-processes the last generated motion of the given slots (Tools/Kimodo/Output/&lt;slot&gt;.bvh) without
        /// generating again: for tuning the processing or the preset's speed / loop settings.</summary>
        public static void Reimport(params string[] slots)
        {
            var lib = Library();
            foreach (var s in slots)
            {
                var p = lib.presets.Find(x => x.slot == s);
                if (p == null) { Debug.LogWarning("[Kimodo] no preset " + s); continue; }
                if (!string.IsNullOrEmpty(p.reverseOf)) { if (!ImportReversed(p)) Debug.LogWarning($"[Kimodo] {s}: no {p.reverseOf} clip to reverse"); continue; }
                string bvh = Path.Combine(RawDir, s + ".bvh");
                if (!File.Exists(bvh)) { Debug.LogWarning("[Kimodo] no generated motion for " + s + " in " + RawDir); continue; }
                Import(p, bvh);
            }
            AssetDatabase.SaveAssets();
        }

        static bool ImportReversed(KimodoPreset p)
        {
            var src = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{ClipDir}/{p.reverseOf}.anim");
            if (!src) return false;
            Save(HumanAnim.FromClip(src).Reverse(), p.slot, true);
            return true;
        }

        // ------------------------------------------------------------------ import / processing

        /// <summary>BVH → humanoid clip(s) for a preset (on the main thread).</summary>
        public static void Import(KimodoPreset p, string bvhPath)
        {
            var bvh = BvhData.Load(bvhPath);
            HumanAnim raw;
            using (var rt = new SomaRetargeter(bvh)) raw = rt.ToHumanAnim(bvh);
            raw = Resample(raw, 30f);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProbePrefab);
            using (var probe = new HumanRigProbe(prefab))
            {
                var idleClip = AssetDatabase.LoadAllAssetsAtPath("Assets/Military/Animations/Idle.fbx").OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
                probe.CalibrateSole(HumanAnim.FromClip(idleClip).frames[0]);
                switch (p.process)
                {
                    case KimodoProcess.LoopMove:
                    {
                        var a = RemoveYawDrift(RemoveTravel(TrimInvalid(raw), false));
                        a = BestLoop(a, p.loopLength.x, p.loopLength.y);
                        Vector3 dir = new Vector3(p.velocity.x, 0f, p.velocity.y);
                        if (dir.sqrMagnitude > 1e-4f) a = AlignPhase(probe, a, dir.normalized);
                        if (p.crouchHips > 0f) a = DeepCrouch(probe, a, p.crouchHips, p.crouchStride, p.crouchLift, p.torsoPitch);
                        float target = dir.magnitude * p.playbackSpeed;
                        a = p.velocity.magnitude > 0.9f ? TuneSpeed(probe, a, dir.normalized, target) : a.Retime(p.playbackSpeed);
                        if (p.torsoPitch >= 0f) a = UprightTorso(probe, a, p.torsoPitch);
                        if (p.levelHead) a = LevelHead(probe, a);
                        Save(a, p.slot, true);
                        break;
                    }
                    case KimodoProcess.LoopIdle:
                    {
                        var a = BestLoop(RemoveYawDrift(RemoveTravel(TrimInvalid(raw), false)), p.loopLength.x, p.loopLength.y);
                        if (p.crouchHips > 0f) a = DeepCrouch(probe, a, p.crouchHips, 1f, p.crouchLift, p.torsoPitch);
                        if (p.torsoPitch >= 0f) a = UprightTorso(probe, a, p.torsoPitch);
                        if (p.levelHead) a = LevelHead(probe, a);
                        if (p.straightLegs) a = StraightLegs(probe, a);
                        Save(a, p.slot, true);
                        break;
                    }
                    case KimodoProcess.OneShot:
                        Save(FaceForward(RemoveTravel(raw, false, centerOnStart: true)).Retime(p.playbackSpeed), p.slot, false);
                        break;
                    case KimodoProcess.OneShotNoLift:
                        // vault / mantle: only the part where the body leaves its run-up height (VaultT maps onto it), the
                        // horizontal travel pinned and the lift removed: the motor carries the character over the obstacle
                        Save(NoLift(PinHorizontal(FaceForward(TrimToAction(raw)))).Retime(p.playbackSpeed), p.slot, false);
                        break;
                    case KimodoProcess.JumpSplit:
                        SplitJump(probe, FaceForward(RemoveTravel(raw, false, centerOnStart: true)));
                        break;
                }
            }
        }

        static void Save(HumanAnim a, string slot, bool loop)
        {
            var clip = a.ToClip(slot, loop);
            string path = $"{ClipDir}/{slot}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);
                existing.name = slot;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(clip);
            }
            else AssetDatabase.CreateAsset(clip, path);
        }

        static HumanAnim Resample(HumanAnim a, float fps)
        {
            if (Mathf.Abs(a.fps - fps) < 0.01f) return a;
            var src = a;
            return HumanAnim.Build(a.Length, t => src.Sample(t), fps, a.loop);
        }

        /// <summary>Removes the horizontal travel (linear trend) of the body; keeps the sway. Centered on the mean or the start.</summary>
        public static HumanAnim RemoveTravel(HumanAnim a, bool removeY, bool centerOnStart = false)
        {
            int n = a.Count;
            if (n < 2) return a;
            Vector3 p0 = a.frames[0].rootT, p1 = a.frames[n - 1].rootT;
            float T = a.Length;
            var o = a.Clone();
            Vector3 mean = Vector3.zero;
            for (int k = 0; k < n; k++)
            {
                float t = k / a.fps;
                Vector3 trend = p0 + (p1 - p0) * (t / T);
                var r = o.frames[k].rootT;
                r.x -= trend.x - p0.x; r.z -= trend.z - p0.z;
                if (removeY) r.y -= trend.y - p0.y;
                o.frames[k].rootT = r;
                mean += r;
            }
            mean /= n;
            Vector3 center = centerOnStart ? new Vector3(o.frames[0].rootT.x, 0f, o.frames[0].rootT.z) : new Vector3(mean.x, 0f, mean.z);
            foreach (var f in o.frames) f.rootT -= center;
            return o;
        }

        /// <summary>Heading of the body. Lying face down the body's forward axis points at the ground, so its up axis (toward
        /// the head) takes over as the body tips forward; upright poses are unaffected.</summary>
        static float Yaw(Quaternion q)
        {
            Vector3 f = q * Vector3.forward, u = q * Vector3.up;
            Vector3 h = f + u * Mathf.Max(0f, -f.y);
            return Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg;
        }

        /// <summary>Body faces +z on average, without a slow turn over the clip.</summary>
        public static HumanAnim RemoveYawDrift(HumanAnim a)
        {
            int n = a.Count;
            var yaw = new float[n];
            float prev = 0f;
            for (int k = 0; k < n; k++)
            {
                float y = Yaw(a.frames[k].rootQ);
                if (k > 0) y = prev + Mathf.DeltaAngle(prev, y);
                yaw[k] = prev = y;
            }
            float slope = n > 1 ? (yaw[n - 1] - yaw[0]) / (n - 1) : 0f;
            float meanResidual = 0f;
            for (int k = 0; k < n; k++) meanResidual += yaw[k] - slope * k;
            meanResidual /= n;
            var o = a.Clone();
            for (int k = 0; k < n; k++)
            {
                float corr = -(slope * k + meanResidual);
                var q = Quaternion.AngleAxis(corr, Vector3.up);
                o.frames[k].rootQ = q * o.frames[k].rootQ;
                o.frames[k].rootT = q * o.frames[k].rootT;
            }
            return o;
        }

        /// <summary>One-shots: the first frame faces +z.</summary>
        static HumanAnim FaceForward(HumanAnim a)
        {
            var q = Quaternion.AngleAxis(-Yaw(a.frames[0].rootQ), Vector3.up);
            var o = a.Clone();
            foreach (var f in o.frames) { f.rootQ = q * f.rootQ; f.rootT = q * f.rootT; }
            return o;
        }

        /// <summary>The body never rises above its starting height (the motor does the lifting); dips stay.</summary>
        static HumanAnim NoLift(HumanAnim a)
        {
            float y0 = a.frames[0].rootT.y;
            var o = a.Clone();
            foreach (var f in o.frames) f.rootT.y = Mathf.Min(f.rootT.y, y0);
            return o;
        }

        /// <summary>Loop sources: keeps the longest stretch where the body height stays near the clip's median, dropping
        /// generation glitches (at speeds past what the model holds, the body can take off mid-clip).</summary>
        public static HumanAnim TrimInvalid(HumanAnim a, float tolerance = 0.3f)
        {
            int n = a.Count;
            if (n < 4) return a;
            var ys = a.frames.Select(f => f.rootT.y).OrderBy(y => y).ToList();
            float med = ys[n / 2];
            int bestStart = 0, bestLen = 0, start = 0;
            for (int i = 0; i <= n; i++)
            {
                bool ok = i < n && Mathf.Abs(a.frames[i].rootT.y - med) < tolerance;
                if (ok) continue;
                if (i - start > bestLen) { bestLen = i - start; bestStart = start; }
                start = i + 1;
            }
            if (bestLen >= n || bestLen < 4) return a;
            Debug.LogWarning($"[Kimodo] dropped {n - bestLen} glitch frame(s): body height off its median by more than {tolerance}");
            var o = new HumanAnim { fps = a.fps, loop = a.loop };
            for (int k = bestStart; k < bestStart + bestLen; k++) o.frames.Add(a.frames[k].Clone());
            return o;
        }

        /// <summary>Obstacle one-shots (vault / mantle): the stretch around the highest body point where the body is above
        /// its starting height, plus a little before / after; the approach run, waiting and run-out are dropped.</summary>
        public static HumanAnim TrimToAction(HumanAnim a, float rise = 0.12f, float pre = 0.2f, float post = 0.4f)
        {
            int n = a.Count;
            if (n < 4) return a;
            float baseY = a.frames.Take(Mathf.Max(1, n / 6)).Average(f => f.rootT.y);
            int peak = 0;
            for (int i = 1; i < n; i++) if (a.frames[i].rootT.y > a.frames[peak].rootT.y) peak = i;
            if (a.frames[peak].rootT.y < baseY + rise) return a; // never rises: keep everything
            int first = peak, last = peak;
            while (first > 0 && a.frames[first - 1].rootT.y > baseY + rise) first--;
            while (last < n - 1 && a.frames[last + 1].rootT.y > baseY + rise) last++;
            int from = Mathf.Max(0, first - Mathf.RoundToInt(pre * a.fps));
            int to = Mathf.Min(n - 1, last + Mathf.RoundToInt(post * a.fps));
            return a.Slice(from / a.fps, to / a.fps, false);
        }

        /// <summary>No horizontal body travel at all: the motor moves the character, the body stays over the capsule.</summary>
        public static HumanAnim PinHorizontal(HumanAnim a)
        {
            var o = a.Clone();
            foreach (var f in o.frames) { f.rootT.x = 0f; f.rootT.z = 0f; }
            return o;
        }

        /// <summary>Mean forward lean (degrees, + = forward) of the chest over the clip, against the rest pose's upright chest.</summary>
        public static float MeanTorsoPitch(HumanRigProbe p, HumanAnim a)
        {
            var bone = p.B(HB.UpperChest) ? HB.UpperChest : HB.Chest;
            var restInv = Quaternion.Inverse(p.RestRotation(bone));
            float s = 0f; int n = 0;
            for (int k = 0; k < a.Count; k += 2)
            {
                p.Apply(a.frames[k]);
                Vector3 up = p.B(bone).rotation * restInv * Vector3.up; // the character faces +z
                s += Mathf.Atan2(up.z, up.y) * Mathf.Rad2Deg; n++;
            }
            return n > 0 ? s / n : 0f;
        }

        /// <summary>Straightens (or bends) the spine so the chest leans forward by the target on average; the motion stays.
        /// The pelvis and legs stay exactly as generated: Unity's body rotation is an average of the hips and the torso,
        /// so after the spine changes every frame's body rotation / position is corrected until the hips match again
        /// (without this, straightening the back swung the legs backward).</summary>
        public static HumanAnim UprightTorso(HumanRigProbe p, HumanAnim a, float targetPitch)
        {
            var ms = new[] { "Spine Front-Back", "Chest Front-Back", "UpperChest Front-Back" }
                .Select(n => Array.IndexOf(HumanTrait.MuscleName, n)).Where(m => m >= 0).ToArray();
            int n = a.Count;
            var hipsRot = new Quaternion[n];
            var hipsPos = new Vector3[n];
            for (int k = 0; k < n; k++) { p.Apply(a.frames[k]); hipsRot[k] = p.B(HB.Hips).rotation; hipsPos[k] = p.B(HB.Hips).position; }
            float scale = p.an && p.an.humanScale > 0.01f ? p.an.humanScale : 1f;

            HumanAnim Bend(float d)
            {
                var o = a.Clone();
                for (int k = 0; k < n; k++)
                {
                    var f = o.frames[k];
                    foreach (int m in ms) f.m[m] = Mathf.Clamp(f.m[m] + d, -1f, 1f);
                    for (int it = 0; it < 2; it++)
                    {
                        p.Apply(f);
                        f.rootQ = hipsRot[k] * Quaternion.Inverse(p.B(HB.Hips).rotation) * f.rootQ;
                        p.Apply(f);
                        f.rootT += (hipsPos[k] - p.B(HB.Hips).position) / scale;
                    }
                }
                return o;
            }

            float p0 = MeanTorsoPitch(p, a);
            float gain = (MeanTorsoPitch(p, Bend(0.1f)) - p0) / 0.1f;
            if (Mathf.Abs(gain) < 5f) return a;
            float total = 0f, pitch = p0;
            var r = a;
            for (int it = 0; it < 3 && Mathf.Abs(targetPitch - pitch) > 1.5f; it++)
            {
                total = Mathf.Clamp(total + (targetPitch - pitch) / gain, -1.5f, 1.5f);
                r = Bend(total);
                pitch = MeanTorsoPitch(p, r);
            }
            Debug.Log($"[Kimodo] torso lean {p0:F0}° → {pitch:F0}° (target {targetPitch:F0}°)");
            return r;
        }

        /// <summary>Mean pitch (degrees, + = up) of the face over the clip, measured against the rest pose's level face.</summary>
        public static float MeanFacePitch(HumanRigProbe p, HumanAnim a)
        {
            var restInv = Quaternion.Inverse(p.RestRotation(HB.Head));
            float s = 0f; int n = 0;
            for (int k = 0; k < a.Count; k += 2)
            {
                p.Apply(a.frames[k]);
                Vector3 face = p.B(HB.Head).rotation * restInv * Vector3.forward;
                s += Mathf.Asin(Mathf.Clamp(face.y, -1f, 1f)) * Mathf.Rad2Deg; n++;
            }
            return n > 0 ? s / n : 0f;
        }

        /// <summary>Offsets the neck + head nod muscles so the face looks level on average (the per-frame head motion stays).
        /// The muscle → angle gain is measured on the rig, so it does not depend on the avatar's muscle limits.</summary>
        public static HumanAnim LevelHead(HumanRigProbe p, HumanAnim a, float targetPitch = 0f)
        {
            int neck = HumanAnim.M("Neck Nod Down-Up"), head = HumanAnim.M("Head Nod Down-Up");
            HumanAnim Nod(HumanAnim x, float d) => x.Modify((t, f) =>
            {
                f.m[neck] = Mathf.Clamp(f.m[neck] + d, -1f, 1f);
                f.m[head] = Mathf.Clamp(f.m[head] + d, -1f, 1f);
            });
            float p0 = MeanFacePitch(p, a);
            float gain = (MeanFacePitch(p, Nod(a, 0.1f)) - p0) / 0.1f; // degrees per unit on both muscles
            if (Mathf.Abs(gain) < 5f) return a;
            var r = a;
            float pitch = p0;
            for (int it = 0; it < 2 && Mathf.Abs(targetPitch - pitch) > 1f; it++)
            {
                r = Nod(r, Mathf.Clamp((targetPitch - pitch) / gain, -0.8f, 0.8f));
                pitch = MeanFacePitch(p, r);
            }
            return r;
        }

        /// <summary>
        /// Lying idle: the legs go straight back along the ground, a little apart, toes pointed (Kimodo's prone lifts the
        /// lower legs and keeps the feet in the air). Along the body's own axis (chest → hips), so a body lying a little
        /// askew still gets straight legs; the upper body is left as generated.
        /// </summary>
        public static HumanAnim StraightLegs(HumanRigProbe p, HumanAnim a)
        {
            var r = new HumanAnim { fps = a.fps, loop = a.loop };
            foreach (var f in a.frames)
            {
                p.Apply(f);
                Vector3 back = Vector3.ProjectOnPlane(p.P(HumanBodyBones.Hips) - p.P(HumanBodyBones.Chest), Vector3.up);
                back = back.sqrMagnitude > 1e-4f ? back.normalized : Vector3.back;
                Vector3 left = Vector3.Cross(Vector3.up, back), down = Vector3.down;
                for (int s = 0; s < 2; s++)
                {
                    bool l = s == 0;
                    Vector3 side = l ? left : -left;
                    var upper = l ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
                    var lower = l ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg;
                    var foot = l ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
                    var toes = l ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
                    // knees and shins face the ground; the foot continues the shin, its top on the ground
                    p.Aim(upper, lower, back + side * 0.14f + down * 0.04f, down);
                    p.Aim(lower, foot, back + side * 0.1f - down * 0.12f, down); // shin a little up: the knee bends slightly
                    p.Aim(foot, toes, back + down * 0.3f, down - back * 0.3f);
                }
                r.frames.Add(p.Capture());
            }
            return r;
        }

        /// <summary>
        /// Deepens a crouch to the game's crouch camera: the chest leans forward (torsoPitch), the hips go down to
        /// hipsHeight (their bob kept), and each leg is solved again with two-bone IK onto where its foot was: the steps
        /// scaled by stride around the hips, the swing foot kept low (soft cap at maxLift), the knee bending forward and a
        /// little outward and staying off the floor, the foot keeping its own rotation. Without it the runtime alignment
        /// pulled Kimodo's shallow crouch down by ~0.45 m and the leg IK folded the trailing leg up behind into the seat.
        /// </summary>
        public static HumanAnim DeepCrouch(HumanRigProbe p, HumanAnim a, float hipsHeight, float stride, float maxLift, float torsoPitch)
        {
            if (torsoPitch >= 0f) a = UprightTorso(p, a, torsoPitch);
            int n = a.Count;
            float scale = p.an && p.an.humanScale > 0.01f ? p.an.humanScale : 1f;
            float hipsMean = 0f;
            for (int k = 0; k < n; k++) { p.Apply(a.frames[k]); hipsMean += p.P(HB.Hips).y; }
            hipsMean /= Mathf.Max(1, n);
            float drop = Mathf.Max(0f, hipsMean - hipsHeight);
            const float KneeFloor = 0.07f;   // the knee's joint stays this high (a knee on the ground is ~5 cm up)
            var legs = new[] { (HB.LeftUpperLeg, HB.LeftLowerLeg, HB.LeftFoot, -1f), (HB.RightUpperLeg, HB.RightLowerLeg, HB.RightFoot, 1f) };
            var o = new HumanAnim { fps = a.fps, loop = a.loop };
            var target = new Vector3[2];
            var footRot = new Quaternion[2];
            for (int k = 0; k < n; k++)
            {
                var f = a.frames[k].Clone();
                p.Apply(f);
                Vector3 hips0 = p.P(HB.Hips);
                for (int s = 0; s < 2; s++)
                {
                    var (_, _, ft, _) = legs[s];
                    Vector3 F = p.P(ft);
                    float lift = F.y - p.SoleHeight;
                    float low = lift > 0f ? maxLift * (1f - Mathf.Exp(-lift / maxLift)) : lift;
                    target[s] = new Vector3(F.x, p.SoleHeight + low, hips0.z + (F.z - hips0.z) * stride);
                    footRot[s] = p.B(ft).rotation;
                }
                f.rootT.y -= drop / scale;
                p.Apply(f);
                for (int s = 0; s < 2; s++)
                {
                    var (up, lo, ft, side) = legs[s];
                    Vector3 hip = p.P(up), knee = p.P(lo), ankle = p.P(ft);
                    float l1 = Vector3.Distance(hip, knee), l2 = Vector3.Distance(knee, ankle);
                    Vector3 d = target[s] - hip;
                    float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 0.02f, l1 + l2 - 0.005f);
                    Vector3 dir = d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.down;
                    float along = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
                    float rad = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
                    Vector3 center = hip + dir * along;
                    // the knee goes forward and a little outward; if that puts it into the floor (a long step behind under
                    // low hips), it swings out to the side around the hip-foot line until it clears
                    Vector3 front = Vector3.ProjectOnPlane(new Vector3(side * 0.3f, 0f, 1f), dir).normalized;
                    Vector3 outward = Vector3.ProjectOnPlane(new Vector3(side, 0f, 0f), dir).normalized;
                    Vector3 bend = front;
                    for (int it = 0; it <= 8 && (center + bend * rad).y < KneeFloor; it++)
                        bend = Vector3.Slerp(front, outward, it / 8f).normalized;
                    Vector3 kneeNew = center + bend * rad;
                    Vector3 ankleNew = hip + dir * dist;
                    p.Aim(up, lo, kneeNew - hip, bend);
                    p.Aim(lo, ft, ankleNew - kneeNew, bend);
                    p.B(ft).rotation = footRot[s];
                }
                o.frames.Add(p.Capture());
            }
            Debug.Log($"[Kimodo] deep crouch: hips {hipsMean:F2} → {hipsMean - drop:F2} m, steps ×{stride:F2}, swing ≤ {maxLift:F2} m");
            return o;
        }

        static readonly bool[] LoopMask = HumanAnim.Mask(n => HumanAnim.Legs(n) || HumanAnim.Spine(n) || HumanAnim.Arms(n));

        static float Distance(PoseFrame x, PoseFrame y)
        {
            float d = 0f;
            for (int i = 0; i < x.m.Length; i++) if (LoopMask[i]) { float e = x.m[i] - y.m[i]; d += e * e; }
            d += (x.rootT.y - y.rootT.y) * (x.rootT.y - y.rootT.y) * 40f;
            d += (1f - Mathf.Abs(Quaternion.Dot(x.rootQ, y.rootQ))) * 200f;
            return d;
        }

        /// <summary>The window [a, b] whose ends match best (pose + velocity), skipping the start / end transients.</summary>
        public static HumanAnim BestLoop(HumanAnim a, float minLen, float maxLen, float skipStart = 0.5f, float skipEnd = 0.25f)
        {
            int n = a.Count;
            float fps = a.fps;
            int s0 = Mathf.Min(n - 2, Mathf.RoundToInt(skipStart * fps)), e1 = n - 1 - Mathf.RoundToInt(skipEnd * fps);
            int minF = Mathf.Max(4, Mathf.RoundToInt(minLen * fps)), maxF = Mathf.RoundToInt(maxLen * fps);
            // motion energy (prefix sums): a window must move at least about a third as much as the clip on average,
            // so a still stretch (the model sometimes waits before starting) is never picked as a "perfect" loop
            var cum = new float[n];
            for (int k = 1; k < n; k++)
            {
                float e = 0f; var x = a.frames[k - 1].m; var y = a.frames[k].m;
                for (int q = 0; q < x.Length; q++) if (LoopMask[q]) { float dq = y[q] - x[q]; e += dq * dq; }
                cum[k] = cum[k - 1] + e;
            }
            float meanE = n > 1 ? cum[n - 1] / (n - 1) : 0f;
            float best = float.MaxValue; int ba = 0, bb = n - 1;
            for (int i = s0; i < e1; i++)
                for (int j = i + minF; j <= Mathf.Min(e1, i + maxF); j++)
                {
                    if ((cum[j] - cum[i]) / (j - i) < 0.35f * meanE) continue;
                    float d = Distance(a.frames[i], a.frames[j]);
                    if (i + 1 < n && j + 1 < n) d += 0.5f * Distance(a.frames[i + 1], a.frames[j + 1]);
                    d *= 1f + 0.15f * (j - i) / (float)maxF; // prefer shorter cycles a little
                    if (d < best) { best = d; ba = i; bb = j; }
                }
            var o = new HumanAnim { fps = fps, loop = true };
            for (int k = ba; k <= bb; k++) o.frames.Add(a.frames[k].Clone());
            o.frames[o.Count - 1] = o.frames[0].Clone(); // exact seam
            return o.SealLoop(0.12f);
        }

        static HumanAnim AlignPhase(HumanRigProbe p, HumanAnim a, Vector3 dir)
        {
            int n = a.Count;
            var ys = new float[n]; var ds = new float[n];
            for (int i = 0; i < n; i++) { p.Apply(a.frames[i]); var l = p.P(HB.LeftFoot); ys[i] = l.y; ds[i] = Vector3.Dot(l, dir); }
            float minY = ys.Min();
            int best = 0; float bd = float.MinValue;
            for (int i = 0; i < n; i++) if (ys[i] < minY + 0.03f && ds[i] > bd) { bd = ds[i]; best = i; }
            return best == 0 ? a : a.Shift(best / (float)(n - 1));
        }

        /// <summary>Retimes a loop so the stance foot moves at the game speed (no foot sliding). The total retime is kept
        /// within [0.5, 2]: past that the cadence looks wrong, so the clip keeps some sliding and should be regenerated
        /// closer to the target speed.</summary>
        static HumanAnim TuneSpeed(HumanRigProbe p, HumanAnim a, Vector3 dir, float speed)
        {
            float total = 1f;
            for (int iter = 0; iter < 3; iter++)
            {
                float v = Vector3.Dot(p.StanceVelocity(a), dir);
                if (v < 0.05f) { Debug.LogWarning("[Kimodo] TuneSpeed: no stance-foot motion found, clip kept as generated"); return a; }
                float k = speed / v;
                if (Mathf.Abs(k - 1f) < 0.01f) break;
                float next = Mathf.Clamp(total * k, 0.5f, 2f);
                if (Mathf.Abs(next - total) < 1e-3f)
                {
                    Debug.LogWarning($"[Kimodo] TuneSpeed: cadence clamped at x{total:F2} (needs x{total * k:F2}); regenerate closer to {speed:F1} m/s");
                    break;
                }
                a = a.Retime(next / total);
                total = next;
            }
            return a;
        }

        /// <summary>Takeoff / landing from the feet: Jump_Start (push-off), Fall (air loop), Land.</summary>
        static void SplitJump(HumanRigProbe p, HumanAnim a)
        {
            int n = a.Count;
            var low = new float[n];
            for (int i = 0; i < n; i++)
            {
                p.Apply(a.frames[i]);
                low[i] = Mathf.Min(p.P(HB.LeftFoot).y, p.P(HB.RightFoot).y);
            }
            float sole = low.Take(Mathf.Max(1, n / 6)).Min();
            int takeoff = -1, landing = -1;
            for (int i = 1; i < n; i++) if (low[i] > sole + 0.06f) { takeoff = i; break; }
            if (takeoff > 0) for (int i = takeoff + 3; i < n; i++) if (low[i] < sole + 0.025f) { landing = i; break; }
            if (takeoff < 0 || landing < 0) { Debug.LogWarning("[Kimodo] jump: no takeoff / landing found, saved whole clip as Jump_Start"); Save(NoLift(a), "Jump_Start", false); return; }
            float fps = a.fps;
            var lifted = NoLift(a);
            Save(lifted.Slice(Mathf.Max(0, takeoff - 5) / fps, Mathf.Min(landing - 1, takeoff + 6) / fps, false), "Jump_Start", false);
            float airA = Mathf.Min(landing - 2, takeoff + 6) / fps, airB = Mathf.Max(airA + 0.1f, (landing - 3) / fps);
            var air = lifted.Slice(airA, airB, false);
            Save(HumanAnim.Build(Mathf.Max(0.4f, air.Length * 2f), t => air.Sample(Mathf.PingPong(t / Mathf.Max(0.05f, air.Length), 1f) * air.Length), fps, true), "Fall", true);
            Save(lifted.Slice(Mathf.Max(0, landing - 2) / fps, Mathf.Min(n - 1, landing + Mathf.RoundToInt(0.7f * fps)) / fps, false), "Land", false);
        }
    }
}
