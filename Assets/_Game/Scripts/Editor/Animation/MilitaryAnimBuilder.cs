using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FPS.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using HB = UnityEngine.HumanBodyBones;

namespace FPS.EditorTools.Anim
{
    /// <summary>
    /// Builds the soldier animation set from the Military pack:
    ///  • generates the missing clips in muscle space (jog / sprint / crouch / prone in all directions, crouch idle,
    ///    jump phases, slide, vault, mantle, forward death, weapon hold poses) from the 13 pack clips + authored poses;
    ///  • the locomotion Animator Controller (8-way directional blend trees per stance, air, slide, vault, death);
    ///  • avatar masks, overlay pose sequences, Resources/CharacterAnimSet.asset;
    ///  • Resources/CharacterModels.asset with the Military soldiers.
    /// Every clip is in place (movement stays the motor's), loops are phase-aligned (left foot lands at phase 0)
    /// and retimed to the game's speeds so the feet don't slide.
    /// </summary>
    public static class MilitaryAnimBuilder
    {
        const string Src = "Assets/Military/Animations/";
        const string Root = "Assets/_Game/Animation";
        const string GenDir = Root + "/Generated";
        const string ControllerPath = Root + "/Soldier.controller";
        const string SetPath = "Assets/Resources/CharacterAnimSet.asset";
        const string ProbePrefab = "Assets/Military/Prefab/URP/SK_Military_CHR1_URP.prefab";

        // game speeds (PlayerMotor): walk 4.6 (our jog ring), sprint 7.4, crouch 2.5, prone 1.15
        const float WalkRing = 1.45f, Jog = 4.6f, Sprint = 7.4f, CrouchSpeed = 2.5f, ProneSpeed = 1.15f;

        static AnimationClip C(string f) => AssetDatabase.LoadAllAssetsAtPath(Src + f).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));

        [MenuItem("Tools/FPS/Build Military Animations")]
        public static void BuildAll()
        {
            Folders();
            var clips = GenerateClips();
            // clips generated with NVIDIA Kimodo (Tools > FPS > Kimodo) replace the procedural ones slot by slot, for the
            // presets that are enabled (a disabled preset falls back to the procedural clip, e.g. the slide)
            var kimodoSlots = new HashSet<string>();
            foreach (var p in Kimodo.KimodoPipeline.Library().presets)
            {
                if (!p.enabled) continue;
                if (p.process == Kimodo.KimodoProcess.JumpSplit) { kimodoSlots.Add("Jump_Start"); kimodoSlots.Add("Fall"); kimodoSlots.Add("Land"); }
                else kimodoSlots.Add(p.slot);
            }
            int fromKimodo = 0;
            foreach (var key in clips.Keys.ToList())
            {
                if (!kimodoSlots.Contains(key)) continue;
                var kc = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Kimodo.KimodoPipeline.ClipDir}/{key}.anim");
                if (kc) { clips[key] = kc; fromKimodo++; }
            }
            foreach (var extra in new[] { "Knife" })
            {
                if (!kimodoSlots.Contains(extra)) continue;
                var kc = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Kimodo.KimodoPipeline.ClipDir}/{extra}.anim");
                if (kc) clips[extra] = kc;
            }
            Debug.Log($"[MilitaryAnimBuilder] {fromKimodo} of {clips.Count} slots use Kimodo clips");
            var masks = BuildMasks();
            var controller = BuildController(clips);
            // states / transitions / blend trees are sub-assets: all of them must be written, not only the controller
            EditorUtility.SetDirty(controller);
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)) if (o) EditorUtility.SetDirty(o);
            BuildSet(controller, clips, masks);
            BuildCharacterModels();
            AssetDatabase.SaveAssets();
            Debug.Log("[MilitaryAnimBuilder] done: " + clips.Count + " clips, " + ControllerPath);
        }

        static void Folders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Animation")) AssetDatabase.CreateFolder("Assets/_Game", "Animation");
            if (!AssetDatabase.IsValidFolder(GenDir)) AssetDatabase.CreateFolder(Root, "Generated");
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        }

        // =================================================================================================== clips

        static AnimationClip Save(HumanAnim a, string name, bool loop)
        {
            var clip = a.ToClip(name, loop);
            string path = $"{GenDir}/{name}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                // keep the asset (and its GUID): copy curves + settings into it
                EditorUtility.CopySerialized(clip, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(clip);
                return existing;
            }
            AssetDatabase.CreateAsset(clip, path);
            return clip;
        }

        /// <summary>Loop starts when the left foot lands at its farthest point toward the travel direction.</summary>
        static HumanAnim AlignPhase(HumanRigProbe p, HumanAnim a, Vector3 dir)
        {
            int n = a.Count;
            var ys = new float[n]; var ds = new float[n];
            for (int i = 0; i < n; i++)
            {
                p.Apply(a.frames[i]);
                var l = p.P(HB.LeftFoot);
                ys[i] = l.y; ds[i] = Vector3.Dot(l, dir);
            }
            float minY = ys.Min();
            int best = 0; float bd = float.MinValue;
            for (int i = 0; i < n; i++) if (ys[i] < minY + 0.03f && ds[i] > bd) { bd = ds[i]; best = i; }
            return best == 0 ? a : a.Shift(best / (float)(n - 1));
        }

        /// <summary>Retimes so the stance speed along dir equals speed.</summary>
        static HumanAnim TuneSpeed(HumanRigProbe p, HumanAnim a, Vector3 dir, float speed)
        {
            for (int iter = 0; iter < 3; iter++)
            {
                float v = Vector3.Dot(p.StanceVelocity(a), dir.normalized);
                if (v < 0.05f) return a;
                float k = speed / v;
                if (Mathf.Abs(k - 1f) < 0.01f) break;
                a = a.Retime(k);
            }
            return a;
        }

        /// <summary>Muscle sign so that +value bends the spine forward (head moves +z).</summary>
        static float ForwardSign(HumanRigProbe p, PoseFrame basePose, string muscle)
        {
            var f = basePose.Clone();
            p.Apply(f); float z0 = p.P(HB.Head).z;
            f.m[HumanAnim.M(muscle)] += 0.3f;
            p.Apply(f); float z1 = p.P(HB.Head).z;
            return z1 > z0 ? 1f : -1f;
        }

        static HumanAnim Lean(HumanRigProbe p, HumanAnim a, float amount)
        {
            float s = ForwardSign(p, a.frames[0], "Spine Front-Back");
            return a.Modify((t, f) =>
            {
                f.m[HumanAnim.M("Spine Front-Back")] += amount * s;
                f.m[HumanAnim.M("Chest Front-Back")] += amount * 0.7f * s;
            });
        }

        static HumanAnim Breathing(HumanAnim a, float amp = 0.02f, float rate = 0.35f)
        {
            float len = a.Length;
            return a.Modify((t, f) =>
            {
                float w = Mathf.Sin(t / len * Mathf.PI * 2f * Mathf.Max(1f, Mathf.Round(len * rate)));
                f.m[HumanAnim.M("Chest Front-Back")] += w * amp;
                f.m[HumanAnim.M("UpperChest Front-Back")] += w * amp * 0.8f;
                f.m[HumanAnim.M("Neck Nod Down-Up")] -= w * amp * 0.5f;
            });
        }

        static Dictionary<string, AnimationClip> GenerateClips()
        {
            var outClips = new Dictionary<string, AnimationClip>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProbePrefab);
            using (var p = new HumanRigProbe(prefab))
            {
                HumanAnim L(string f) { var a = HumanAnim.FromClip(C(f)); a.loop = true; return a; }
                var legs = HumanAnim.Mask(HumanAnim.Legs);
                var legsSpine = HumanAnim.Mask(n => HumanAnim.Legs(n) || HumanAnim.Spine(n));

                var idle = L("Idle.fbx");
                p.CalibrateSole(idle.frames[0]);
                var walkF = AlignPhase(p, L("Walk_F.FBX"), Vector3.forward);
                var walkB = AlignPhase(p, L("Walk_B.FBX"), Vector3.back);
                var strL = AlignPhase(p, L("Strafe_L.FBX"), Vector3.left);
                var strR = AlignPhase(p, L("Strafe_R.FBX"), Vector3.right);
                var run = AlignPhase(p, L("Run.FBX"), Vector3.forward);
                var crouch = AlignPhase(p, L("Crouch.FBX"), Vector3.forward);

                void Out(string name, HumanAnim a, bool loop) => outClips[name] = Save(a, name, loop);

                // ---- stand: idle + walk ring (pack clips, phase aligned, looped)
                Out("Idle", Breathing(idle, 0.012f), true);
                Out("Walk_F", walkF, true);
                Out("Walk_B", walkB, true);
                Out("Walk_L", strL, true);
                Out("Walk_R", strR, true);

                // ---- jog ring at the game's run speed: longer strides + retime
                Out("Jog_F", TuneSpeed(p, p.GroundFix(Lean(p, run.Amplify(legs, 1.12f, 1.1f), 0.04f)), Vector3.forward, Jog), true);
                Out("Jog_B", TuneSpeed(p, p.GroundFix(walkB.Amplify(legs, 1.45f, 2.2f)), Vector3.back, Jog), true);
                Out("Jog_L", TuneSpeed(p, p.GroundFix(strL.Amplify(legs, 1.35f, 2f)), Vector3.left, Jog), true);
                Out("Jog_R", TuneSpeed(p, p.GroundFix(strR.Amplify(legs, 1.35f, 2f)), Vector3.right, Jog), true);

                // ---- sprint: bigger strides, forward lean
                Out("Sprint_F", TuneSpeed(p, p.GroundFix(Lean(p, run.Amplify(legs, 1.3f, 1.25f), 0.16f)), Vector3.forward, Sprint), true);

                // ---- crouch: the pack's crouch walk + the walk / strafe clips moved onto the crouch stance
                var crouchMean = HumanAnim.FromPose(Mean(crouch), 1f);
                Out("Crouch_Idle", Breathing(p.GroundFix(HumanAnim.FromPose(Mean(crouch), 2.4f)), 0.014f), true);
                Out("Crouch_F", TuneSpeed(p, p.GroundFix(crouch.Amplify(legs, 1.1f)), Vector3.forward, CrouchSpeed), true);
                Out("Crouch_B", TuneSpeed(p, p.GroundFix(walkB.TransferStance(crouchMean, legsSpine).Amplify(legs, 1.1f)), Vector3.back, CrouchSpeed), true);
                Out("Crouch_L", TuneSpeed(p, p.GroundFix(strL.TransferStance(crouchMean, legsSpine)), Vector3.left, CrouchSpeed), true);
                Out("Crouch_R", TuneSpeed(p, p.GroundFix(strR.TransferStance(crouchMean, legsSpine)), Vector3.right, CrouchSpeed), true);

                // ---- prone (authored)
                var proneIdle = HumanAnim.Build(2.4f, t => PronePose(p, idle.frames[0], 0f, 0f, Mathf.Sin(t / 2.4f * Mathf.PI * 2f)), 30f, true);
                Out("Prone_Idle", proneIdle, true);
                var crawl = HumanAnim.Build(1.4f, t => PronePose(p, idle.frames[0], t / 1.4f, 1f, 0f), 30f, true);
                crawl = TuneSpeedProne(p, crawl, ProneSpeed);
                Out("Prone_F", crawl, true);
                Out("Prone_B", crawl.Reverse(), true);
                var shimmy = HumanAnim.Build(1.4f, t => PronePose(p, idle.frames[0], t / 1.4f, 0.6f, 0f), 30f, true);
                Out("Prone_L", shimmy, true);
                Out("Prone_R", shimmy.Reverse(), true);

                // ---- air: phases of the pack's standing jump
                var jump = L("Jump.FBX"); jump.loop = false;
                Out("Jump_Start", jump.Slice(0.26f, 0.42f, false), false);
                var apex = jump.Slice(0.42f, 0.52f, false);
                var fall = HumanAnim.Build(0.6f, t => apex.Sample(Mathf.PingPong(t / 0.3f, 1f) * apex.Length), 30f, true);
                Out("Fall", fall, true);
                Out("Land", jump.Slice(0.6f, 1f, false), false);

                // ---- slide / vault / mantle (authored on the crouch stance)
                var crouchPose = Mean(crouch);
                Out("Slide", HumanAnim.Build(1f, t => SlidePose(p, crouchPose, Mathf.Sin(t * Mathf.PI * 2f)), 30f, true), true);
                Out("Vault", HumanAnim.Build(1f, t => VaultPose(p, crouchPose, idle.frames[0], t, false), 30f, false), false);
                Out("Mantle", HumanAnim.Build(1f, t => VaultPose(p, crouchPose, idle.frames[0], t, true), 30f, false), false);

                // ---- deaths: the pack's (falls backwards) + a forward fall
                var death = L("Death.fbx"); death.loop = false;
                Out("Death_B", death, false);
                Out("Death_F", HumanAnim.Build(1.5f, t => DeathForwardPose(p, idle.frames[0], t / 1.5f), 30f, false), false);

                // ---- weapon hold (static overlay poses) + upper body one-shots
                var aim = L("Recoil_Rifle.fbx");
                Out("Hold_Rifle", HumanAnim.FromPose(aim.frames[0], 0.5f), true);
                Out("Hold_Pistol", HumanAnim.FromPose(PistolPose(p, aim.frames[0]), 0.5f), true);
                var reload = L("Reloadl_Rifle.fbx"); reload.loop = false;
                Out("Reload_Rifle", reload, false);
                var punch = L("Punch.fbx"); punch.loop = false;
                Out("Punch", punch, false);
                Out("Throw", HumanAnim.Build(0.9f, t => ThrowPose(p, aim.frames[0], t / 0.9f), 30f, false), false);
            }
            AssetDatabase.SaveAssets();
            return outClips;
        }

        static PoseFrame Mean(HumanAnim a)
        {
            var f = new PoseFrame();
            var mean = a.Mean();
            Array.Copy(mean, f.m, mean.Length);
            f.rootT = a.MeanRootT();
            Quaternion q = a.frames[0].rootQ;
            f.rootQ = q;
            return f;
        }

        static HumanAnim TuneSpeedProne(HumanRigProbe p, HumanAnim a, float speed)
        {
            // the crawl moves by the hands / knees: measure the knee's backward speed while it is on the ground
            var kz = new List<float>();
            int n = 60;
            float len = a.Length, dt = len / n;
            var ks = new List<Vector3>();
            for (int i = 0; i <= n; i++) { p.Apply(a.Sample(len * i / n)); ks.Add(p.P(HB.LeftLowerLeg)); }
            float minY = ks.Min(k => k.y);
            for (int i = 0; i < n; i++) if (ks[i].y < minY + 0.03f) kz.Add(-(ks[i + 1].z - ks[i].z) / dt);
            if (kz.Count == 0) return a;
            kz.Sort();
            float v = kz[kz.Count / 2];
            return v > 0.05f ? a.Retime(Mathf.Clamp(speed / v, 0.4f, 2.5f)) : a;
        }

        // =================================================================================================== authored poses
        // Character space: x right, y up, z forward. Directions are bone → child.

        static readonly Vector3 Up = Vector3.up, Fwd = Vector3.forward, Down = Vector3.down, Back = Vector3.back;

        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        /// <summary>Lying on the stomach, propped on the elbows. crawl: 0..1 cycle phase, amount: crawl amplitude, breath -1..1.</summary>
        static PoseFrame PronePose(HumanRigProbe p, PoseFrame reference, float phase, float amount, float breath)
        {
            p.Apply(reference);
            float s = Mathf.Sin(phase * Mathf.PI * 2f) * amount;     // + = left side works
            float c = Mathf.Cos(phase * Mathf.PI * 2f) * amount;
            float sl = Mathf.Max(0f, s), sr = Mathf.Max(0f, -s);
            var hips = p.B(HB.Hips);
            hips.position = p.Root.TransformPoint(V(0.03f * s, 0.15f + 0.01f * breath, -0.42f + 0.03f * c));
            p.Aim(HB.Hips, HB.Spine, V(0.08f * s, 0.05f, 1f), V(0f, -1f, 0.05f));
            p.Aim(HB.Spine, HB.Chest, V(-0.04f * s, 0.1f, 1f), V(0f, -1f, 0.1f));
            p.Aim(HB.Chest, HB.UpperChest, V(-0.06f * s, 0.24f + 0.02f * breath, 1f), V(0f, -1f, 0.24f));
            if (p.B(HB.UpperChest)) p.Aim(HB.UpperChest, HB.Neck, V(-0.06f * s, 0.34f, 1f), V(0f, -1f, 0.34f));
            p.Aim(HB.Neck, HB.Head, V(0f, 0.6f, 0.8f), V(0f, -0.8f, 0.6f));
            AimHead(p, V(0f, 0.92f, 0.38f), V(0f, -0.38f, 0.92f));
            // legs: straight back, slightly spread; the crawling knee comes up and out to the side
            Leg(p, true, V(-0.14f - 0.55f * sl, -0.04f, -1f + 0.55f * sl), V(-0.2f * sl, -1f, 0.25f * sl), V(-0.1f + 0.5f * sl, -0.02f, -1f), V(0f, -1f, 0f));
            Leg(p, false, V(0.14f + 0.55f * sr, -0.04f, -1f + 0.55f * sr), V(0.2f * sr, -1f, 0.25f * sr), V(0.1f - 0.5f * sr, -0.02f, -1f), V(0f, -1f, 0f));
            // arms: elbows on the ground in front of the shoulders, the crawling side reaches forward
            Arm(p, true, V(-0.28f, -0.72f, 0.55f + 0.35f * sr), V(0f, 0.55f, 0.85f), V(0.32f, 0.2f, 1f), V(0f, 1f, 0f));
            Arm(p, false, V(0.28f, -0.72f, 0.55f + 0.35f * sl), V(0f, 0.55f, 0.85f), V(-0.32f, 0.2f, 1f), V(0f, 1f, 0f));
            return p.Capture();
        }

        /// <summary>Sliding on the left hip: left leg forward, right leg folded under, torso leaning back.</summary>
        static PoseFrame SlidePose(HumanRigProbe p, PoseFrame crouchPose, float wobble)
        {
            p.Apply(crouchPose);
            var hips = p.B(HB.Hips);
            // sitting on the ground: the extended leg's heel and the folded leg's knee touch the floor
            hips.position = p.Root.TransformPoint(V(-0.03f, 0.26f + 0.006f * wobble, -0.05f));
            p.Aim(HB.Hips, HB.Spine, V(0.05f, 0.9f, -0.42f), V(0f, 0.42f, 0.9f));
            p.Aim(HB.Spine, HB.Chest, V(0.05f, 0.93f, -0.3f), V(0f, 0.3f, 0.93f));
            p.Aim(HB.Chest, HB.UpperChest, V(0.03f, 0.97f, -0.15f), V(0f, 0.15f, 0.97f));
            if (p.B(HB.UpperChest)) p.Aim(HB.UpperChest, HB.Neck, V(0f, 0.99f, 0.05f), Fwd);
            p.Aim(HB.Neck, HB.Head, V(0f, 0.92f, 0.35f), V(0f, -0.35f, 0.92f));
            AimHead(p, V(0f, 0.98f, 0.15f), V(0f, -0.15f, 0.98f));
            Leg(p, true, V(-0.12f, -0.38f, 1f), V(0f, 1f, 0.38f), V(-0.05f, -0.1f, 1f), V(0f, 1f, 0.1f));        // extended forward
            Leg(p, false, V(0.22f, -0.62f, 0.76f), V(0f, 0.76f, 0.62f), V(-0.1f, 0.02f, -1f), V(0f, -1f, 0f));   // folded under
            return p.Capture();
        }

        /// <summary>Speed vault (side vault over a low wall) or mantle (climb onto a ledge), t 0..1.</summary>
        static PoseFrame VaultPose(HumanRigProbe p, PoseFrame crouchPose, PoseFrame stand, float t, bool mantle)
        {
            p.Apply(PoseFrame.Lerp(crouchPose, stand, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, t))));
            var hips = p.B(HB.Hips);
            float s = Mathf.Sin(t * Mathf.PI);
            if (!mantle)
            {
                // legs tuck and swing over to the right, the body leans back-left
                float tuck = Mathf.Sin(Mathf.Clamp01((t - 0.1f) / 0.75f) * Mathf.PI);
                hips.position = p.Root.TransformPoint(V(0.05f * tuck, Mathf.Lerp(0.6f, 0.92f, Mathf.SmoothStep(0f, 1f, t)) + 0.12f * s, 0f));
                p.Aim(HB.Hips, HB.Spine, V(-0.25f * tuck, 0.95f, -0.15f * tuck), V(0.2f * tuck, 0.15f, 1f));
                p.Aim(HB.Spine, HB.Chest, V(-0.2f * tuck, 0.97f, 0.1f), Fwd);
                Leg(p, true, V(0.35f * tuck, -1f + 0.85f * tuck, 0.55f * tuck), V(0.4f * tuck, 0.2f * tuck, 1f), V(0.25f * tuck, -1f, -0.1f * tuck), Fwd);
                Leg(p, false, V(0.45f * tuck, -1f + 0.75f * tuck, 0.4f * tuck), V(0.4f * tuck, 0.2f * tuck, 1f), V(0.3f * tuck, -1f, -0.35f * tuck), Fwd);
            }
            else
            {
                // pull up: knees come up in front, the body rises and leans forward over the ledge
                float pull = Mathf.Sin(Mathf.Clamp01(t * 1.15f) * Mathf.PI);
                hips.position = p.Root.TransformPoint(V(0f, Mathf.Lerp(0.55f, 0.93f, Mathf.SmoothStep(0f, 1f, t)), -0.15f * (1f - t)));
                p.Aim(HB.Hips, HB.Spine, V(0f, 0.85f, 0.5f * pull), V(0f, -0.5f * pull, 0.85f));
                p.Aim(HB.Spine, HB.Chest, V(0f, 0.8f, 0.6f * pull), V(0f, -0.6f * pull, 0.8f));
                Leg(p, true, V(-0.05f, -1f + 0.9f * pull, 0.9f * pull), V(0f, 0.6f * pull, 1f), V(-0.05f, -1f, -0.4f * pull), Fwd);
                Leg(p, false, V(0.08f, -1f + 0.5f * pull, 0.5f * pull), V(0f, 0.4f * pull, 1f), V(0.05f, -1f, -0.5f * pull), Fwd);
            }
            return p.Capture();
        }

        /// <summary>Forward death: knees buckle, the body falls on its chest. t 0..1.</summary>
        static PoseFrame DeathForwardPose(HumanRigProbe p, PoseFrame stand, float t)
        {
            p.Apply(stand);
            float buckle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f));
            float fall = Mathf.Clamp01((t - 0.2f) / 0.55f);
            fall = fall * fall;
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.75f) / 0.25f));
            var hips = p.B(HB.Hips);
            float pitch = Mathf.Lerp(0f, 88f, fall);
            Quaternion body = Quaternion.Euler(pitch, 8f * fall, 0f);
            float hy = Mathf.Lerp(Mathf.Lerp(0.93f, 0.55f, buckle), 0.14f, fall);
            hips.position = p.Root.TransformPoint(V(0.02f * fall, hy, 0.25f * fall + 0.35f * fall));
            p.Aim(HB.Hips, HB.Spine, body * V(0f, 1f, 0.05f), body * V(0f, -0.05f, 1f));
            p.Aim(HB.Spine, HB.Chest, body * V(0.03f, 1f, 0.08f), body * Fwd);
            p.Aim(HB.Chest, HB.UpperChest, body * V(0.05f, 1f, 0.05f), body * Fwd);
            p.Aim(HB.Neck, HB.Head, body * V(0.2f * settle, 1f, -0.2f * settle), body * Fwd);
            AimHead(p, body * V(0.6f * settle, 0.8f, -0.1f), body * V(0f, -0.1f, 1f));
            Vector3 thigh = Vector3.Slerp(V(0f, -1f, 0.6f * buckle), body * V(0f, -1f, 0f), fall);
            Vector3 shin = Vector3.Slerp(V(0f, -1f, -0.7f * buckle), body * V(0f, -1f, 0f), fall);
            Leg(p, true, thigh + V(-0.15f, 0f, 0f), Fwd, shin + V(-0.1f, 0f, 0f), Fwd);
            Leg(p, false, thigh + V(0.1f, 0f, 0.15f * buckle), Fwd, shin + V(0.08f, 0f, 0f), Fwd);
            Arm(p, true, Vector3.Slerp(V(-0.3f, -1f, 0.2f), V(-1f, -0.2f, 0.5f), fall), Fwd, Vector3.Slerp(V(-0.1f, -1f, 0.3f), V(-0.3f, 0f, 1f), fall), Up);
            Arm(p, false, Vector3.Slerp(V(0.3f, -1f, 0.2f), V(1f, -0.3f, 0.2f), fall), Fwd, Vector3.Slerp(V(0.1f, -1f, 0.3f), V(0.4f, -0.2f, 1f), fall), Up);
            return p.Capture();
        }

        /// <summary>Pistol: both arms forward at chest height (fingers / elbows; IK puts the hands on the grip).</summary>
        static PoseFrame PistolPose(HumanRigProbe p, PoseFrame rifle)
        {
            p.Apply(rifle);
            Arm(p, false, V(0.25f, -0.35f, 1f), V(0f, 1f, 0.3f), V(-0.2f, 0.05f, 1f), Up);
            Arm(p, true, V(-0.35f, -0.4f, 1f), V(0f, 1f, 0.3f), V(0.3f, 0.05f, 1f), Up);
            var f = p.Capture();
            // keep the rifle pose's body / spine, only arms change
            var r = rifle.Clone();
            var arms = HumanAnim.Mask(HumanAnim.Arms);
            for (int i = 0; i < r.m.Length; i++) if (arms[i]) r.m[i] = f.m[i];
            return r;
        }

        /// <summary>Overhand grenade throw (right arm), t 0..1.</summary>
        static PoseFrame ThrowPose(HumanRigProbe p, PoseFrame rifle, float t)
        {
            p.Apply(rifle);
            float wind = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.35f));
            float release = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.25f));
            float back = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.7f) / 0.3f));
            Vector3 ua = Vector3.Slerp(Vector3.Slerp(V(0.4f, -0.6f, 0.6f), V(0.7f, 0.5f, -0.5f), wind), V(0.2f, 0.4f, 1f), release);
            Vector3 fa = Vector3.Slerp(Vector3.Slerp(V(-0.1f, 0.2f, 1f), V(0.1f, 1f, 0.2f), wind), V(0f, 0.1f, 1f), release);
            ua = Vector3.Slerp(ua, V(0.4f, -0.6f, 0.6f), back);
            fa = Vector3.Slerp(fa, V(-0.1f, 0.2f, 1f), back);
            Arm(p, false, ua, V(0f, 0f, 1f), fa, Up);
            return p.Capture();
        }

        static void Leg(HumanRigProbe p, bool left, Vector3 thigh, Vector3 knee, Vector3 shin, Vector3 shinFront)
        {
            p.Aim(left ? HB.LeftUpperLeg : HB.RightUpperLeg, left ? HB.LeftLowerLeg : HB.RightLowerLeg, thigh, knee);
            p.Aim(left ? HB.LeftLowerLeg : HB.RightLowerLeg, left ? HB.LeftFoot : HB.RightFoot, shin, shinFront);
        }

        static void Arm(HumanRigProbe p, bool left, Vector3 upper, Vector3 upperFront, Vector3 fore, Vector3 foreFront)
        {
            p.Aim(left ? HB.LeftUpperArm : HB.RightUpperArm, left ? HB.LeftLowerArm : HB.RightLowerArm, upper, upperFront);
            p.Aim(left ? HB.LeftLowerArm : HB.RightLowerArm, left ? HB.LeftHand : HB.RightHand, fore, foreFront);
        }

        /// <summary>Head: rest direction is up (neck → top of the head), front is forward.</summary>
        static void AimHead(HumanRigProbe p, Vector3 dir, Vector3 front)
        {
            var head = p.B(HB.Head);
            if (!head) return;
            // rest frame of the head (upright, facing forward) → target frame (top along dir, face along front)
            Quaternion rest = Quaternion.LookRotation(Fwd, Up);
            Vector3 d = p.Root.TransformDirection(dir.normalized);
            Vector3 f = p.Root.TransformDirection(Vector3.ProjectOnPlane(front, dir).normalized);
            Quaternion target = Quaternion.LookRotation(f, d);
            head.rotation = target * Quaternion.Inverse(rest) * p.RestRotation(HB.Head);
        }

        /// <summary>Renders contact sheets of the authored poses / generated clips (side + front views) into dir.</summary>
        public static void RenderPreviews(string dir, string only = null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProbePrefab);
            using (var p = new HumanRigProbe(prefab))
            {
                var idle = HumanAnim.FromClip(C("Idle.fbx"));
                var crouch = HumanAnim.FromClip(C("Crouch.FBX")); crouch.loop = true;
                var aim = HumanAnim.FromClip(C("Recoil_Rifle.fbx"));
                var crouchPose = Mean(crouch);
                void Sheet(string name, HumanAnim a, int n, Vector3 view, float size = 1.1f, Vector3? center = null)
                {
                    if (only != null && !name.Contains(only)) return;
                    PosePreview.Sheet(p, PosePreview.Frames(a, n), Path.Combine(dir, name + ".png"), view, size, center);
                }
                var side = Vector3.left;            // camera looks along -x: sees the right side, forward = screen left
                var front = Vector3.back;
                var top = new Vector3(0.6f, -0.8f, -0.6f);
                Sheet("prone_idle_side", HumanAnim.Build(2.4f, t => PronePose(p, idle.frames[0], 0f, 0f, 0f)), 1, side, 1.1f, new Vector3(0f, 0.4f, 0f));
                Sheet("prone_crawl_top", HumanAnim.Build(1.4f, t => PronePose(p, idle.frames[0], t / 1.4f, 1f, 0f), 30f, true), 6, top, 1.2f, new Vector3(0f, 0.3f, 0f));
                Sheet("prone_crawl_side", HumanAnim.Build(1.4f, t => PronePose(p, idle.frames[0], t / 1.4f, 1f, 0f), 30f, true), 4, side, 1.1f, new Vector3(0f, 0.4f, 0f));
                Sheet("slide_side", HumanAnim.Build(1f, t => SlidePose(p, crouchPose, 0f)), 1, side, 1.0f, new Vector3(0f, 0.6f, 0f));
                Sheet("slide_front", HumanAnim.Build(1f, t => SlidePose(p, crouchPose, 0f)), 1, front, 1.0f, new Vector3(0f, 0.6f, 0f));
                Sheet("vault_front", HumanAnim.Build(1f, t => VaultPose(p, crouchPose, idle.frames[0], t, false)), 6, front, 1.1f);
                Sheet("mantle_side", HumanAnim.Build(1f, t => VaultPose(p, crouchPose, idle.frames[0], t, true)), 6, side, 1.1f);
                Sheet("death_f_side", HumanAnim.Build(1.5f, t => DeathForwardPose(p, idle.frames[0], t / 1.5f)), 6, side, 1.1f);
                Sheet("pistol_side", HumanAnim.FromPose(PistolPose(p, aim.frames[0]), 0.2f), 1, side);
                Sheet("pistol_top", HumanAnim.FromPose(PistolPose(p, aim.frames[0]), 0.2f), 1, top, 1.1f);
                Sheet("rifle_hold_side", HumanAnim.FromPose(aim.frames[0], 0.2f), 1, side);
                Sheet("throw_side", HumanAnim.Build(0.9f, t => ThrowPose(p, aim.frames[0], t / 0.9f)), 5, side);
                Sheet("crouch_idle_side", HumanAnim.FromPose(crouchPose, 0.2f), 1, side);
                foreach (var n in new[] { "Jog_F", "Jog_B", "Jog_L", "Sprint_F", "Crouch_B", "Crouch_L", "Jump_Start", "Fall", "Land" })
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{GenDir}/{n}.anim");
                    if (!clip) continue;
                    var a = HumanAnim.FromClip(clip);
                    Sheet(n.ToLowerInvariant() + (n.EndsWith("_L") ? "_front" : "_side"), a, 6, n.EndsWith("_L") ? front : side);
                }
            }
        }

        // =================================================================================================== masks

        public class Masks { public AvatarMask arms, upper, fingers; }

        static AvatarMask Mask(string name, params AvatarMaskBodyPart[] parts)
        {
            string path = $"{Root}/{name}.mask";
            var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (!m) { m = new AvatarMask(); AssetDatabase.CreateAsset(m, path); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) m.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var p in parts) m.SetHumanoidBodyPartActive(p, true);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Masks BuildMasks() => new Masks
        {
            arms = Mask("Mask_Arms", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK),
            upper = Mask("Mask_UpperBody", AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK),
            fingers = Mask("Mask_Fingers", AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers),
        };

        // =================================================================================================== controller

        static BlendTree Tree(AnimatorController ac, string name, BlendTreeType type, string px, string py = null)
        {
            var t = new BlendTree { name = name, blendType = type, blendParameter = px, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            if (py != null) t.blendParameterY = py;
            AssetDatabase.AddObjectToAsset(t, ac);
            return t;
        }

        static void Ring(BlendTree t, Dictionary<string, AnimationClip> c, string prefix, float s)
        {
            t.AddChild(c[prefix + "_F"], new Vector2(0f, s));
            t.AddChild(c[prefix + "_B"], new Vector2(0f, -s));
            t.AddChild(c[prefix + "_L"], new Vector2(-s, 0f));
            t.AddChild(c[prefix + "_R"], new Vector2(s, 0f));
        }

        static AnimatorController BuildController(Dictionary<string, AnimationClip> c)
        {
            // the existing asset is wiped and rebuilt in place instead of deleted: its GUID (and every reference to it)
            // stays, and AssetDatabase.DeleteAsset can be refused when run from tools without UI, which left a stale
            // file under a fresh in-memory controller that was never written to disk
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!ac) ac = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            else
            {
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                    if (o && o != ac) { AssetDatabase.RemoveObjectFromAsset(o); UnityEngine.Object.DestroyImmediate(o, true); }
                ac.parameters = new AnimatorControllerParameter[0];
                ac.layers = new AnimatorControllerLayer[0];
                ac.AddLayer("Base Layer");
            }
            foreach (var p in new[] { "VelX", "VelZ", "Speed", "Crouch", "Prone", "VertSpeed", "VaultT", "AnimSpeed" })
                ac.AddParameter(p, AnimatorControllerParameterType.Float);
            foreach (var p in new[] { "Grounded", "Slide", "Vault", "Mantle", "Dead", "DeadForward" })
                ac.AddParameter(p, AnimatorControllerParameterType.Bool);
            var ps = ac.parameters;
            foreach (var p in ps) { if (p.name == "AnimSpeed") p.defaultFloat = 1f; if (p.name == "Grounded") p.defaultBool = true; }
            ac.parameters = ps;

            var stand = Tree(ac, "Stand", BlendTreeType.FreeformDirectional2D, "VelX", "VelZ");
            stand.AddChild(c["Idle"], Vector2.zero);
            Ring(stand, c, "Walk", WalkRing);
            Ring(stand, c, "Jog", Jog);
            stand.AddChild(c["Sprint_F"], new Vector2(0f, Sprint));

            var crouch = Tree(ac, "Crouch", BlendTreeType.FreeformDirectional2D, "VelX", "VelZ");
            crouch.AddChild(c["Crouch_Idle"], Vector2.zero);
            Ring(crouch, c, "Crouch", CrouchSpeed);

            var prone = Tree(ac, "Prone", BlendTreeType.FreeformDirectional2D, "VelX", "VelZ");
            prone.AddChild(c["Prone_Idle"], Vector2.zero);
            Ring(prone, c, "Prone", ProneSpeed);

            var standCrouch = Tree(ac, "StandCrouch", BlendTreeType.Simple1D, "Crouch");
            standCrouch.AddChild(stand, 0f);
            standCrouch.AddChild(crouch, 1f);
            var ground = Tree(ac, "Ground", BlendTreeType.Simple1D, "Prone");
            ground.AddChild(standCrouch, 0f);
            ground.AddChild(prone, 1f);

            var sm = ac.layers[0].stateMachine;
            AnimatorState S(string name, Motion m, Vector2 pos)
            {
                var s = sm.AddState(name, pos);
                s.motion = m;
                s.writeDefaultValues = true;
                return s;
            }
            var move = S("Locomotion", ground, new Vector2(300, 0));
            move.speedParameterActive = true;
            move.speedParameter = "AnimSpeed";
            sm.defaultState = move;
            var jump = S("Jump", c["Jump_Start"], new Vector2(600, -120));
            var fall = S("Fall", c["Fall"], new Vector2(800, -60));
            var land = S("Land", c["Land"], new Vector2(600, 80));
            var slide = S("Slide", c["Slide"], new Vector2(300, 200));
            var vault = S("Vault", c["Vault"], new Vector2(0, -150));
            vault.timeParameterActive = true; vault.timeParameter = "VaultT";
            var mantle = S("Mantle", c["Mantle"], new Vector2(0, -60));
            mantle.timeParameterActive = true; mantle.timeParameter = "VaultT";
            var deadB = S("Death_B", c["Death_B"], new Vector2(0, 150));
            var deadF = S("Death_F", c["Death_F"], new Vector2(0, 240));

            AnimatorStateTransition T(AnimatorState from, AnimatorState to, float dur, bool exit = false, float exitTime = 0.9f)
            {
                var t = from.AddTransition(to);
                t.hasExitTime = exit; t.exitTime = exitTime; t.duration = dur; t.hasFixedDuration = true;
                return t;
            }
            AnimatorStateTransition A(AnimatorState to, float dur)
            {
                var t = sm.AddAnyStateTransition(to);
                t.hasExitTime = false; t.duration = dur; t.hasFixedDuration = true; t.canTransitionToSelf = false;
                return t;
            }

            // air
            var t1 = T(move, jump, 0.08f); t1.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded"); t1.AddCondition(AnimatorConditionMode.Greater, 1.5f, "VertSpeed");
            var t2 = T(move, fall, 0.25f); t2.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded"); t2.AddCondition(AnimatorConditionMode.Less, 1.5f, "VertSpeed");
            T(jump, fall, 0.15f, true, 0.9f);
            var t3 = T(jump, land, 0.08f); t3.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            var t4 = T(fall, land, 0.06f); t4.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            T(land, move, 0.25f, true, 0.55f);
            var t5 = T(land, move, 0.18f); t5.AddCondition(AnimatorConditionMode.Greater, 2.5f, "Speed");
            var t5b = T(land, fall, 0.2f); t5b.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
            // slide
            var t6 = T(move, slide, 0.15f); t6.AddCondition(AnimatorConditionMode.If, 0, "Slide");
            var t7 = T(slide, move, 0.25f); t7.AddCondition(AnimatorConditionMode.IfNot, 0, "Slide");
            // vault / mantle (scrubbed by VaultT)
            var t8 = A(vault, 0.1f); t8.AddCondition(AnimatorConditionMode.If, 0, "Vault"); t8.AddCondition(AnimatorConditionMode.IfNot, 0, "Mantle"); t8.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var t9 = A(mantle, 0.1f); t9.AddCondition(AnimatorConditionMode.If, 0, "Mantle"); t9.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var t10 = T(vault, move, 0.15f); t10.AddCondition(AnimatorConditionMode.IfNot, 0, "Vault");
            var t11 = T(mantle, move, 0.15f); t11.AddCondition(AnimatorConditionMode.IfNot, 0, "Mantle");
            // death / respawn
            var d1 = A(deadB, 0.12f); d1.AddCondition(AnimatorConditionMode.If, 0, "Dead"); d1.AddCondition(AnimatorConditionMode.IfNot, 0, "DeadForward");
            var d2 = A(deadF, 0.12f); d2.AddCondition(AnimatorConditionMode.If, 0, "Dead"); d2.AddCondition(AnimatorConditionMode.If, 0, "DeadForward");
            var r1 = T(deadB, move, 0f); r1.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            var r2 = T(deadF, move, 0f); r2.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");

            EditorUtility.SetDirty(ac);
            return ac;
        }

        // =================================================================================================== set / models

        static AnimSequence Seq(string name, AnimationClip clip, AvatarMask mask, float blendIn)
        {
            string path = $"{Root}/{name}.asset";
            var s = AssetDatabase.LoadAssetAtPath<AnimSequence>(path);
            if (!s) { s = ScriptableObject.CreateInstance<AnimSequence>(); AssetDatabase.CreateAsset(s, path); }
            s.clip = clip; s.mask = mask; s.blendTime = new BlendTime(blendIn, 0.2f); s.poseTime = 0f; s.loop = false;
            EditorUtility.SetDirty(s);
            return s;
        }

        static void BuildSet(AnimatorController controller, Dictionary<string, AnimationClip> c, Masks masks)
        {
            var set = AssetDatabase.LoadAssetAtPath<CharacterAnimSet>(SetPath);
            if (!set) { set = ScriptableObject.CreateInstance<CharacterAnimSet>(); AssetDatabase.CreateAsset(set, SetPath); }
            set.controller = controller;
            set.armsMask = masks.arms; set.upperBodyMask = masks.upper; set.fingersMask = masks.fingers;
            set.rifleHold = Seq("Seq_HoldRifle", c["Hold_Rifle"], masks.arms, 0.2f);
            set.pistolHold = Seq("Seq_HoldPistol", c["Hold_Pistol"], masks.arms, 0.2f);
            set.unarmedHold = null;
            set.reloadRifle = c["Reload_Rifle"];
            set.punch = c["Punch"];
            set.throwGrenade = c["Throw"];
            set.knifeSlash = c.TryGetValue("Knife", out var knife) ? knife : null;
            set.speeds = new Vector4(WalkRing, Jog, Sprint, CrouchSpeed);
            set.proneSpeed = ProneSpeed;

            // eye height of the idle pose (scale 1): head bone + eye offset
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProbePrefab);
            using (var p = new HumanRigProbe(prefab))
            {
                p.Apply(HumanAnim.FromClip(C("Idle.fbx")).frames[0]);
                set.restEyeHeight = p.P(HB.Head).y + set.eyeOffset.y;
            }
            EditorUtility.SetDirty(set);
        }

        /// <summary>Resources/CharacterModels.asset: the Military soldiers (URP prefabs).</summary>
        [MenuItem("Tools/FPS/Build Character Models (Military)")]
        public static void BuildCharacterModels()
        {
            string[] names = { "Штурмовик", "Разведчик", "Полиция", "Спецназ", "Медик", "Морпех", "Снайпер", "Боец", "Патруль", "Охрана", "Десант" };
            var entries = new List<CharacterModelDB.Entry>();
            for (int i = 1; i <= 11; i++)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Military/Prefab/URP/SK_Military_CHR{i}_URP.prefab");
                if (go) entries.Add(new CharacterModelDB.Entry { name = $"{names[(i - 1) % names.Length]} {i}", prefab = go });
            }
            const string dbPath = "Assets/Resources/CharacterModels.asset";
            var db = AssetDatabase.LoadAssetAtPath<CharacterModelDB>(dbPath);
            if (!db) { db = ScriptableObject.CreateInstance<CharacterModelDB>(); AssetDatabase.CreateAsset(db, dbPath); }
            db.entries = entries.ToArray();
            db.tpController = null;
            db.fpController = null;
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MilitaryAnimBuilder] {entries.Count} character models");
        }
    }
}
