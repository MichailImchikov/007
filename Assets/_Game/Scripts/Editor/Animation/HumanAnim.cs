using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools.Anim
{
    /// <summary>One humanoid pose in muscle space (what a humanoid clip stores per frame).</summary>
    public class PoseFrame
    {
        public float[] m = new float[HumanTrait.MuscleCount];
        public Vector3 rootT;
        public Quaternion rootQ = Quaternion.identity;

        public PoseFrame Clone()
        {
            var f = new PoseFrame { rootT = rootT, rootQ = rootQ };
            Array.Copy(m, f.m, m.Length);
            return f;
        }

        public static PoseFrame Lerp(PoseFrame a, PoseFrame b, float t)
        {
            var f = new PoseFrame { rootT = Vector3.LerpUnclamped(a.rootT, b.rootT, t), rootQ = Quaternion.Slerp(a.rootQ, b.rootQ, t) };
            for (int i = 0; i < f.m.Length; i++) f.m[i] = Mathf.LerpUnclamped(a.m[i], b.m[i], t);
            return f;
        }

        public HumanPose ToHumanPose() => new HumanPose { bodyPosition = rootT, bodyRotation = rootQ, muscles = (float[])m.Clone() };

        public static PoseFrame From(HumanPose p)
        {
            var f = new PoseFrame { rootT = p.bodyPosition, rootQ = p.bodyRotation };
            Array.Copy(p.muscles, f.m, Mathf.Min(p.muscles.Length, f.m.Length));
            return f;
        }
    }

    /// <summary>
    /// Muscle-space humanoid animation sampled at a fixed frame rate: read from a clip, edited with simple operators
    /// (retime, reverse, amplify, offset, blend, masks...) and written back as a humanoid .anim.
    /// </summary>
    public class HumanAnim
    {
        public float fps = 30f;
        public bool loop;
        public List<PoseFrame> frames = new List<PoseFrame>();

        public float Length => frames.Count > 1 ? (frames.Count - 1) / fps : 0f;
        public int Count => frames.Count;

        // ------------------------------------------------------------------ muscle names / groups

        static Dictionary<string, int> index;
        public static int M(string muscleName)
        {
            if (index == null)
            {
                index = new Dictionary<string, int>();
                for (int i = 0; i < HumanTrait.MuscleCount; i++) index[HumanTrait.MuscleName[i]] = i;
            }
            return index.TryGetValue(muscleName, out int v) ? v : throw new ArgumentException("no muscle " + muscleName);
        }

        public static string CurveName(string muscle)
        {
            foreach (var side in new[] { "Left", "Right" })
                foreach (var f in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    string p = side + " " + f + " ";
                    if (muscle.StartsWith(p)) return side + "Hand." + f + "." + muscle.Substring(p.Length);
                }
            return muscle;
        }

        public static bool[] Mask(Func<string, bool> pred)
        {
            var mask = new bool[HumanTrait.MuscleCount];
            for (int i = 0; i < mask.Length; i++) mask[i] = pred(HumanTrait.MuscleName[i]);
            return mask;
        }

        public static readonly Func<string, bool> Legs = n => n.Contains("Leg") || n.Contains("Foot") || n.Contains("Toes");
        public static readonly Func<string, bool> Spine = n => n.StartsWith("Spine") || n.StartsWith("Chest") || n.StartsWith("UpperChest");
        public static readonly Func<string, bool> NeckHead = n => n.StartsWith("Neck") || n.StartsWith("Head");
        public static readonly Func<string, bool> Arms = n => (n.Contains("Shoulder") || n.Contains("Arm") || n.Contains("Hand")) && !Fingers(n);
        public static readonly Func<string, bool> Fingers = n => n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little");
        public static readonly Func<string, bool> All = n => true;

        // ------------------------------------------------------------------ io

        public static HumanAnim FromClip(AnimationClip clip, float fps = 30f)
        {
            var a = new HumanAnim { fps = fps, loop = AnimationUtility.GetAnimationClipSettings(clip).loopTime };
            var mc = new AnimationCurve[HumanTrait.MuscleCount];
            var rt = new AnimationCurve[3];
            var rq = new AnimationCurve[4];
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Animator)) continue;
                var c = AnimationUtility.GetEditorCurve(clip, b);
                string p = b.propertyName;
                switch (p)
                {
                    case "RootT.x": rt[0] = c; continue;
                    case "RootT.y": rt[1] = c; continue;
                    case "RootT.z": rt[2] = c; continue;
                    case "RootQ.x": rq[0] = c; continue;
                    case "RootQ.y": rq[1] = c; continue;
                    case "RootQ.z": rq[2] = c; continue;
                    case "RootQ.w": rq[3] = c; continue;
                }
                for (int i = 0; i < HumanTrait.MuscleCount; i++)
                    if (CurveName(HumanTrait.MuscleName[i]) == p) { mc[i] = c; break; }
            }
            int n = Mathf.Max(2, Mathf.RoundToInt(clip.length * fps) + 1);
            for (int k = 0; k < n; k++)
            {
                float t = Mathf.Min(k / fps, clip.length);
                var f = new PoseFrame();
                for (int i = 0; i < f.m.Length; i++) f.m[i] = mc[i] != null ? mc[i].Evaluate(t) : 0f;
                f.rootT = new Vector3(rt[0]?.Evaluate(t) ?? 0f, rt[1]?.Evaluate(t) ?? 1f, rt[2]?.Evaluate(t) ?? 0f);
                var q = new Quaternion(rq[0]?.Evaluate(t) ?? 0f, rq[1]?.Evaluate(t) ?? 0f, rq[2]?.Evaluate(t) ?? 0f, rq[3]?.Evaluate(t) ?? 1f);
                f.rootQ = Normalize(q);
                a.frames.Add(f);
            }
            return a;
        }

        static Quaternion Normalize(Quaternion q)
        {
            float d = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return d < 1e-6f ? Quaternion.identity : new Quaternion(q.x / d, q.y / d, q.z / d, q.w / d);
        }

        /// <summary>Writes a humanoid clip (muscles + body position / rotation, everything baked into the pose).</summary>
        public AnimationClip ToClip(string name, bool? loopOverride = null, float cycleOffset = 0f)
        {
            var clip = new AnimationClip { name = name, frameRate = fps };
            var mc = new AnimationCurve[HumanTrait.MuscleCount];
            for (int i = 0; i < mc.Length; i++) mc[i] = new AnimationCurve();
            var rt = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var rq = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            Quaternion prev = Quaternion.identity;
            for (int k = 0; k < frames.Count; k++)
            {
                float t = k / fps;
                var f = frames[k];
                for (int i = 0; i < mc.Length; i++) mc[i].AddKey(new Keyframe(t, f.m[i]));
                rt[0].AddKey(t, f.rootT.x); rt[1].AddKey(t, f.rootT.y); rt[2].AddKey(t, f.rootT.z);
                Quaternion q = f.rootQ;
                if (k > 0 && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); // continuous quaternion curves
                prev = q;
                rq[0].AddKey(t, q.x); rq[1].AddKey(t, q.y); rq[2].AddKey(t, q.z); rq[3].AddKey(t, q.w);
            }
            void Set(string prop, AnimationCurve c)
            {
                for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prop), c);
            }
            Set("RootT.x", rt[0]); Set("RootT.y", rt[1]); Set("RootT.z", rt[2]);
            Set("RootQ.x", rq[0]); Set("RootQ.y", rq[1]); Set("RootQ.z", rq[2]); Set("RootQ.w", rq[3]);
            for (int i = 0; i < mc.Length; i++) Set(CurveName(HumanTrait.MuscleName[i]), mc[i]);

            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = loopOverride ?? loop;
            s.loopBlend = false;
            s.cycleOffset = cycleOffset;
            s.loopBlendOrientation = true;     // bake: the pose is exactly the authored one, no root motion
            s.loopBlendPositionY = true;
            s.loopBlendPositionXZ = true;
            s.keepOriginalOrientation = true;
            s.keepOriginalPositionY = true;
            s.keepOriginalPositionXZ = true;
            s.heightFromFeet = false;
            s.mirror = false;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            return clip;
        }

        // ------------------------------------------------------------------ sampling

        /// <summary>Pose at time t (seconds), interpolated; wraps for loops.</summary>
        public PoseFrame Sample(float t)
        {
            if (frames.Count == 0) return new PoseFrame();
            if (frames.Count == 1) return frames[0].Clone();
            float len = Length;
            if (loop && len > 0f) { t %= len; if (t < 0f) t += len; }
            else t = Mathf.Clamp(t, 0f, len);
            float x = t * fps;
            int i = Mathf.Clamp(Mathf.FloorToInt(x), 0, frames.Count - 2);
            return PoseFrame.Lerp(frames[i], frames[i + 1], x - i);
        }

        public static HumanAnim FromPose(PoseFrame pose, float length = 1f, float fps = 30f, bool loop = true)
        {
            var a = new HumanAnim { fps = fps, loop = loop };
            int n = Mathf.Max(2, Mathf.RoundToInt(length * fps) + 1);
            for (int i = 0; i < n; i++) a.frames.Add(pose.Clone());
            return a;
        }

        /// <summary>Builds an animation by evaluating a pose function at every frame (t in seconds).</summary>
        public static HumanAnim Build(float length, Func<float, PoseFrame> poseAt, float fps = 30f, bool loop = false)
        {
            var a = new HumanAnim { fps = fps, loop = loop };
            int n = Mathf.Max(2, Mathf.RoundToInt(length * fps) + 1);
            for (int i = 0; i < n; i++) a.frames.Add(poseAt(Mathf.Min(i / fps, length)));
            return a;
        }

        // ------------------------------------------------------------------ operators (all return a new animation)

        public HumanAnim Clone()
        {
            var a = new HumanAnim { fps = fps, loop = loop };
            foreach (var f in frames) a.frames.Add(f.Clone());
            return a;
        }

        /// <summary>Plays speed times faster (loops stay loops).</summary>
        public HumanAnim Retime(float speed)
        {
            float len = Length / Mathf.Max(0.01f, speed);
            return Build(len, t => Sample(Mathf.Min(t * speed, Length - 1e-5f)), fps, loop);
        }

        public HumanAnim Reverse() => Build(Length, t => Sample(Length - t), fps, loop);

        /// <summary>Loop starts at phase (0..1).</summary>
        public HumanAnim Shift(float phase) => Build(Length, t => Sample(t + phase * Length), fps, loop);

        public HumanAnim Slice(float from, float to, bool asLoop)
        {
            var src = this;
            var a = Build(to - from, t => src.SampleClamped(from + t), fps, asLoop);
            return a;
        }

        PoseFrame SampleClamped(float t)
        {
            bool l = loop; loop = false;
            var f = Sample(t);
            loop = l;
            return f;
        }

        public float[] Mean()
        {
            var mean = new float[HumanTrait.MuscleCount];
            foreach (var f in frames) for (int i = 0; i < mean.Length; i++) mean[i] += f.m[i];
            for (int i = 0; i < mean.Length; i++) mean[i] /= Mathf.Max(1, frames.Count);
            return mean;
        }

        public Vector3 MeanRootT()
        {
            Vector3 s = Vector3.zero;
            foreach (var f in frames) s += f.rootT;
            return s / Mathf.Max(1, frames.Count);
        }

        /// <summary>Exaggerates (k &gt; 1) or calms (k &lt; 1) the motion of the masked muscles around their mean.</summary>
        public HumanAnim Amplify(bool[] mask, float k, float bodyBounce = 1f)
        {
            var mean = Mean();
            Vector3 rmean = MeanRootT();
            var a = Clone();
            foreach (var f in a.frames)
            {
                for (int i = 0; i < f.m.Length; i++) if (mask[i]) f.m[i] = Mathf.Clamp(mean[i] + (f.m[i] - mean[i]) * k, -1.6f, 1.6f);
                f.rootT.y = rmean.y + (f.rootT.y - rmean.y) * bodyBounce;
            }
            return a;
        }

        /// <summary>Adds a constant offset to masked muscles (and optionally to the body height).</summary>
        public HumanAnim Offset(bool[] mask, float[] delta, float bodyDy = 0f)
        {
            var a = Clone();
            foreach (var f in a.frames)
            {
                for (int i = 0; i < f.m.Length; i++) if (mask[i]) f.m[i] += delta[i];
                f.rootT.y += bodyDy;
            }
            return a;
        }

        /// <summary>Per-muscle offset that moves this animation's mean stance onto another's (stance transfer: crouch a walk).</summary>
        public HumanAnim TransferStance(HumanAnim stance, bool[] mask, float weight = 1f)
        {
            var from = Mean(); var to = stance.Mean();
            var d = new float[from.Length];
            for (int i = 0; i < d.Length; i++) d[i] = (to[i] - from[i]) * weight;
            float dy = (stance.MeanRootT().y - MeanRootT().y) * weight;
            return Offset(mask, d, dy);
        }

        /// <summary>Takes the masked muscles from other (time-synchronized by normalized time).</summary>
        public HumanAnim Merge(HumanAnim other, bool[] mask, float weight = 1f)
        {
            var a = Clone();
            for (int k = 0; k < a.frames.Count; k++)
            {
                float n = a.frames.Count > 1 ? k / (float)(a.frames.Count - 1) : 0f;
                var o = other.Sample(n * other.Length);
                var f = a.frames[k];
                for (int i = 0; i < f.m.Length; i++) if (mask[i]) f.m[i] = Mathf.Lerp(f.m[i], o.m[i], weight);
            }
            return a;
        }

        /// <summary>Sets masked muscles to fixed values.</summary>
        public HumanAnim Set(params (string muscle, float value)[] values)
        {
            var a = Clone();
            foreach (var f in a.frames) foreach (var v in values) f.m[M(v.muscle)] = v.value;
            return a;
        }

        /// <summary>Adds per-frame offsets from a function (time, frame) → modifies in place on a copy.</summary>
        public HumanAnim Modify(Action<float, PoseFrame> fn)
        {
            var a = Clone();
            for (int k = 0; k < a.frames.Count; k++) fn(k / fps, a.frames[k]);
            return a;
        }

        /// <summary>Makes a loop seamless: the end blends into the start over the last fraction.</summary>
        public HumanAnim SealLoop(float fraction = 0.15f)
        {
            var a = Clone();
            int n = a.frames.Count;
            int w = Mathf.Max(1, Mathf.RoundToInt(n * fraction));
            for (int k = 0; k < w; k++)
            {
                int idx = n - w + k;
                float t = (k + 1f) / w;
                a.frames[idx] = PoseFrame.Lerp(a.frames[idx], frames[0], AnimSmooth(t));
            }
            a.loop = true;
            return a;
        }

        static float AnimSmooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    }
}
