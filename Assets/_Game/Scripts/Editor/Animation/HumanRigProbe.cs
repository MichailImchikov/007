using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPS.EditorTools.Anim
{
    /// <summary>
    /// A hidden instance of a humanoid used to evaluate and author muscle-space poses: apply a pose (forward
    /// kinematics), measure feet / speeds, pose bones by direction in character space and capture the muscles back.
    /// Character space: x right, y up, z forward, origin between the feet.
    /// </summary>
    public class HumanRigProbe : IDisposable
    {
        public readonly GameObject go;
        public readonly Animator an;
        readonly HumanPoseHandler hph;
        readonly Dictionary<HumanBodyBones, Quaternion> restRot = new Dictionary<HumanBodyBones, Quaternion>();
        public Transform Root => go.transform;
        public float SoleHeight { get; private set; } = 0.13f;

        public HumanRigProbe(GameObject prefab)
        {
            go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            an = go.GetComponent<Animator>() ?? go.GetComponentInChildren<Animator>();
            hph = new HumanPoseHandler(an.avatar, an.transform);
            for (var b = HumanBodyBones.Hips; b < HumanBodyBones.LastBone; b++)
            {
                var t = an.GetBoneTransform(b);
                if (t) restRot[b] = t.rotation;
            }
        }

        public void Dispose()
        {
            hph?.Dispose();
            if (go) Object.DestroyImmediate(go);
        }

        public Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
        public Quaternion RestRotation(HumanBodyBones b) => restRot.TryGetValue(b, out var q) ? q : Quaternion.identity;
        public Vector3 P(HumanBodyBones b) { var t = B(b); return t ? Root.InverseTransformPoint(t.position) : Vector3.zero; }

        // ------------------------------------------------------------------ poses

        public void Apply(PoseFrame f)
        {
            var p = f.ToHumanPose();
            hph.SetHumanPose(ref p);
        }

        public PoseFrame Capture()
        {
            var p = new HumanPose();
            hph.GetHumanPose(ref p);
            return PoseFrame.From(p);
        }

        public PoseFrame Sample(AnimationClip clip, float t)
        {
            clip.SampleAnimation(go, t);
            return Capture();
        }

        /// <summary>Ankle height in the rest stance (measured from a standing clip).</summary>
        public void CalibrateSole(PoseFrame standing)
        {
            Apply(standing);
            SoleHeight = Mathf.Min(P(HumanBodyBones.LeftFoot).y, P(HumanBodyBones.RightFoot).y);
        }

        // ------------------------------------------------------------------ authoring helpers (character space)

        /// <summary>
        /// Points bone → child along dir (character space) with the bone's front facing front.
        /// Uses the rest pose: the bone's anatomical front is the character's forward (or up for feet / toes).
        /// </summary>
        public void Aim(HumanBodyBones bone, HumanBodyBones child, Vector3 dir, Vector3 front)
        {
            var t = B(bone); var c = B(child);
            if (!t || !c || !restRot.ContainsKey(bone)) return;
            // rest direction / front from the rest rotations (independent of the current pose)
            Quaternion rr = restRot[bone];
            Vector3 restDirLocal = Quaternion.Inverse(t.rotation) * (c.position - t.position);
            Vector3 restDir = rr * restDirLocal;
            Vector3 restFront = Mathf.Abs(Vector3.Dot(restDir.normalized, Vector3.forward)) > 0.7f ? Vector3.up : Vector3.forward;
            Quaternion qRest = Quaternion.LookRotation(restDir, Vector3.ProjectOnPlane(restFront, restDir));
            Vector3 d = Root.TransformDirection(dir.normalized);
            Vector3 f = Root.TransformDirection(front.normalized);
            if (Mathf.Abs(Vector3.Dot(d, f)) > 0.999f) f = Root.up;
            Quaternion qNew = Quaternion.LookRotation(d, Vector3.ProjectOnPlane(f, d));
            t.rotation = qNew * Quaternion.Inverse(qRest) * rr;
        }

        /// <summary>Rotates a bone (world) around a character-space axis through the bone.</summary>
        public void Rotate(HumanBodyBones bone, Vector3 axis, float degrees)
        {
            var t = B(bone);
            if (!t) return;
            t.rotation = Quaternion.AngleAxis(degrees, Root.TransformDirection(axis)) * t.rotation;
        }

        /// <summary>Hips position / rotation in character space.</summary>
        public void SetHips(Vector3 pos, Quaternion rot)
        {
            var h = B(HumanBodyBones.Hips);
            h.SetPositionAndRotation(Root.TransformPoint(pos), Root.rotation * rot * Quaternion.Inverse(Quaternion.identity) * RestHipsRelative());
        }

        Quaternion RestHipsRelative() => Quaternion.Inverse(Root.rotation) * restRot[HumanBodyBones.Hips];

        /// <summary>Puts every bone back to its rest rotation (keeps positions of the hips).</summary>
        public void Rest()
        {
            foreach (var kv in restRot) { var t = B(kv.Key); if (t) t.rotation = Root.rotation * kv.Value; }
        }

        // ------------------------------------------------------------------ measurement

        /// <summary>Median velocity (character space, m/s) of the character implied by the planted foot.</summary>
        public Vector3 StanceVelocity(HumanAnim a, int samples = 60)
        {
            var lf = new List<Vector3>(); var rf = new List<Vector3>();
            float len = a.Length;
            for (int i = 0; i <= samples; i++)
            {
                Apply(a.Sample(len * i / samples));
                lf.Add(P(HumanBodyBones.LeftFoot)); rf.Add(P(HumanBodyBones.RightFoot));
            }
            float dt = len / samples;
            var vx = new List<float>(); var vz = new List<float>();
            foreach (var arr in new[] { lf, rf })
            {
                float minY = float.MaxValue;
                foreach (var p in arr) minY = Mathf.Min(minY, p.y);
                for (int i = 0; i < samples; i++)
                    if (arr[i].y < minY + 0.025f && arr[i + 1].y < minY + 0.025f)
                    {
                        Vector3 v = (arr[i + 1] - arr[i]) / dt;
                        vx.Add(-v.x); vz.Add(-v.z);
                    }
            }
            if (vx.Count == 0) return Vector3.zero;
            vx.Sort(); vz.Sort();
            return new Vector3(vx[vx.Count / 2], 0f, vz[vz.Count / 2]);
        }

        /// <summary>Moves the body each frame so the lowest foot rests at the sole height (fixes floating / sinking feet).</summary>
        public HumanAnim GroundFix(HumanAnim a, float blend = 1f, float maxCorrection = 0.25f)
        {
            var o = a.Clone();
            for (int k = 0; k < o.frames.Count; k++)
            {
                Apply(o.frames[k]);
                float low = Mathf.Min(P(HumanBodyBones.LeftFoot).y, P(HumanBodyBones.RightFoot).y);
                float d = Mathf.Clamp(SoleHeight - low, -maxCorrection, maxCorrection) * blend;
                var hips = B(HumanBodyBones.Hips);
                hips.position += Root.up * d;
                o.frames[k] = Capture();
            }
            return o;
        }

        /// <summary>Body height of a pose in character space (hips y).</summary>
        public float HipsHeight(PoseFrame f) { Apply(f); return P(HumanBodyBones.Hips).y; }
    }
}
