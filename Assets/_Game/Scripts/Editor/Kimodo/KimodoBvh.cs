using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FPS.EditorTools.Anim;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FPS.EditorTools.Kimodo
{
    /// <summary>A parsed BVH file (hierarchy + motion).</summary>
    public class BvhData
    {
        public class Joint
        {
            public string name;
            public int parent = -1;
            public Vector3 offset;          // BVH units / axes
            public string[] channels = new string[0];
            public int channelStart;
        }

        public readonly List<Joint> joints = new List<Joint>();
        public float frameTime = 1f / 30f;
        public float[][] frames = new float[0][];
        public int FrameCount => frames.Length;
        public float Fps => 1f / Mathf.Max(1e-5f, frameTime);

        public int Index(string name) => joints.FindIndex(j => j.name == name);

        public static BvhData Load(string path) => Parse(File.ReadAllText(path));

        public static BvhData Parse(string text)
        {
            var d = new BvhData();
            var tokens = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int i = 0;
            var stack = new Stack<int>();
            int channelCount = 0, last = -1;
            bool endSite = false;
            float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
            while (i < tokens.Length)
            {
                string t = tokens[i];
                if (t == "ROOT" || t == "JOINT")
                {
                    var j = new Joint { name = tokens[i + 1], parent = stack.Count > 0 ? stack.Peek() : -1 };
                    d.joints.Add(j);
                    last = d.joints.Count - 1;
                    i += 2;
                }
                else if (t == "End") { endSite = true; i += 2; }
                else if (t == "{") { if (!endSite) stack.Push(last); i++; }
                else if (t == "}") { if (endSite) endSite = false; else stack.Pop(); i++; }
                else if (t == "OFFSET")
                {
                    if (!endSite) d.joints[last].offset = new Vector3(F(tokens[i + 1]), F(tokens[i + 2]), F(tokens[i + 3]));
                    i += 4;
                }
                else if (t == "CHANNELS")
                {
                    int n = int.Parse(tokens[i + 1]);
                    var ch = new string[n];
                    for (int k = 0; k < n; k++) ch[k] = tokens[i + 2 + k];
                    d.joints[last].channels = ch;
                    d.joints[last].channelStart = channelCount;
                    channelCount += n;
                    i += 2 + n;
                }
                else if (t == "MOTION")
                {
                    int frames = int.Parse(tokens[i + 2]);           // "Frames:" N
                    d.frameTime = F(tokens[i + 5]);                  // "Frame Time:" t
                    i += 6;
                    d.frames = new float[frames][];
                    for (int f = 0; f < frames; f++)
                    {
                        var v = new float[channelCount];
                        for (int c = 0; c < channelCount; c++) v[c] = F(tokens[i++]);
                        d.frames[f] = v;
                    }
                    break;
                }
                else i++;
            }
            return d;
        }
    }

    /// <summary>
    /// Kimodo SOMA (somaskel77) motion → Unity humanoid muscles. Builds the SOMA skeleton as a temporary
    /// GameObject hierarchy (BVH: right-handed, Y up, cm, character facing +Z, left side +X → Unity: mirror X,
    /// meters), makes a humanoid Avatar for it from the standard T-pose and reads every frame back through a
    /// HumanPoseHandler. The result retargets to any humanoid (the Military soldiers) like a normal clip.
    /// </summary>
    public class SomaRetargeter : IDisposable
    {
        // SOMA joint → Unity humanoid bone
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "Hips", "Hips" }, { "Spine1", "Spine" }, { "Spine2", "Chest" }, { "Chest", "UpperChest" },
            { "Neck1", "Neck" }, { "Head", "Head" },
            { "LeftShoulder", "LeftShoulder" }, { "LeftArm", "LeftUpperArm" }, { "LeftForeArm", "LeftLowerArm" }, { "LeftHand", "LeftHand" },
            { "RightShoulder", "RightShoulder" }, { "RightArm", "RightUpperArm" }, { "RightForeArm", "RightLowerArm" }, { "RightHand", "RightHand" },
            { "LeftLeg", "LeftUpperLeg" }, { "LeftShin", "LeftLowerLeg" }, { "LeftFoot", "LeftFoot" }, { "LeftToeBase", "LeftToes" },
            { "RightLeg", "RightUpperLeg" }, { "RightShin", "RightLowerLeg" }, { "RightFoot", "RightFoot" }, { "RightToeBase", "RightToes" },
            { "LeftHandThumb1", "Left Thumb Proximal" }, { "LeftHandThumb2", "Left Thumb Intermediate" }, { "LeftHandThumb3", "Left Thumb Distal" },
            { "LeftHandIndex2", "Left Index Proximal" }, { "LeftHandIndex3", "Left Index Intermediate" }, { "LeftHandIndex4", "Left Index Distal" },
            { "LeftHandMiddle2", "Left Middle Proximal" }, { "LeftHandMiddle3", "Left Middle Intermediate" }, { "LeftHandMiddle4", "Left Middle Distal" },
            { "LeftHandRing2", "Left Ring Proximal" }, { "LeftHandRing3", "Left Ring Intermediate" }, { "LeftHandRing4", "Left Ring Distal" },
            { "LeftHandPinky2", "Left Little Proximal" }, { "LeftHandPinky3", "Left Little Intermediate" }, { "LeftHandPinky4", "Left Little Distal" },
            { "RightHandThumb1", "Right Thumb Proximal" }, { "RightHandThumb2", "Right Thumb Intermediate" }, { "RightHandThumb3", "Right Thumb Distal" },
            { "RightHandIndex2", "Right Index Proximal" }, { "RightHandIndex3", "Right Index Intermediate" }, { "RightHandIndex4", "Right Index Distal" },
            { "RightHandMiddle2", "Right Middle Proximal" }, { "RightHandMiddle3", "Right Middle Intermediate" }, { "RightHandMiddle4", "Right Middle Distal" },
            { "RightHandRing2", "Right Ring Proximal" }, { "RightHandRing3", "Right Ring Intermediate" }, { "RightHandRing4", "Right Ring Distal" },
            { "RightHandPinky2", "Right Little Proximal" }, { "RightHandPinky3", "Right Little Intermediate" }, { "RightHandPinky4", "Right Little Distal" },
        };

        const float Scale = 0.01f; // cm → m

        readonly BvhData bvh;
        readonly GameObject root;
        readonly Transform[] bones;
        readonly Avatar avatar;
        readonly HumanPoseHandler handler;

        public Avatar Avatar => avatar;

        public SomaRetargeter(BvhData skeleton)
        {
            bvh = skeleton;
            root = new GameObject("SOMA_Retarget") { hideFlags = HideFlags.HideAndDontSave };
            bones = new Transform[bvh.joints.Count];
            for (int i = 0; i < bvh.joints.Count; i++)
            {
                var j = bvh.joints[i];
                var t = new GameObject(j.name).transform;
                t.SetParent(j.parent >= 0 ? bones[j.parent] : root.transform, false);
                t.localPosition = ToUnity(j.offset) * Scale;
                t.localRotation = Quaternion.identity;
                bones[i] = t;
            }

            var human = new List<HumanBone>();
            foreach (var kv in Map)
            {
                if (bvh.Index(kv.Key) < 0) continue;
                var hb = new HumanBone { boneName = kv.Key, humanName = kv.Value };
                hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            var skel = new List<SkeletonBone>
            {
                new SkeletonBone { name = root.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one }
            };
            foreach (var t in bones)
                skel.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = Vector3.one });
            var desc = new HumanDescription
            {
                human = human.ToArray(), skeleton = skel.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            avatar = AvatarBuilder.BuildHumanAvatar(root, desc);
            avatar.name = "SOMA77";
            if (!avatar.isValid || !avatar.isHuman) throw new Exception("SOMA avatar is not a valid humanoid");
            handler = new HumanPoseHandler(avatar, root.transform);
        }

        public void Dispose()
        {
            handler?.Dispose();
            if (avatar) Object.DestroyImmediate(avatar);
            if (root) Object.DestroyImmediate(root);
        }

        /// <summary>BVH (right-handed, left = +X) → Unity (left-handed, left = −X): mirror X.</summary>
        static Vector3 ToUnity(Vector3 v) => new Vector3(-v.x, v.y, v.z);

        static Quaternion ToUnity(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        static Quaternion Axis(char axis, float degrees)
        {
            float h = degrees * Mathf.Deg2Rad * 0.5f, s = Mathf.Sin(h), c = Mathf.Cos(h);
            switch (axis)
            {
                case 'X': return new Quaternion(s, 0f, 0f, c);
                case 'Y': return new Quaternion(0f, s, 0f, c);
                default: return new Quaternion(0f, 0f, s, c);
            }
        }

        /// <summary>Poses the SOMA skeleton with one BVH frame.</summary>
        public void ApplyFrame(float[] v)
        {
            for (int i = 0; i < bvh.joints.Count; i++)
            {
                var j = bvh.joints[i];
                Vector3 pos = j.offset;
                bool hasPos = false;
                Quaternion q = Quaternion.identity; // right-handed, Hamilton products in channel order
                for (int c = 0; c < j.channels.Length; c++)
                {
                    float val = v[j.channelStart + c];
                    switch (j.channels[c])
                    {
                        case "Xposition": pos.x = val; hasPos = true; break;
                        case "Yposition": pos.y = val; hasPos = true; break;
                        case "Zposition": pos.z = val; hasPos = true; break;
                        case "Xrotation": q = q * Axis('X', val); break;
                        case "Yrotation": q = q * Axis('Y', val); break;
                        case "Zrotation": q = q * Axis('Z', val); break;
                    }
                }
                if (hasPos) bones[i].localPosition = ToUnity(pos) * Scale;
                bones[i].localRotation = ToUnity(q);
            }
            SpreadTwists();
        }

        // Kimodo splits the twist of the spine (and of a leg) between neighbouring joints in opposite directions:
        // Spine2 +110°, Chest −110°, lying even ±180°. The pose is right, but the humanoid muscles read two huge twists
        // whose signs flip from frame to frame, and the interpolation between such keys spun the chest through a half
        // turn within one frame (the prone twitch). The chain's total twist (around each bone) is shared out again by
        // weight, joint by joint: a joint turns around its own bone and its child turns back by the same amount, so the
        // child keeps its world rotation and position (it sits on that bone). Only the spread of the twist changes.
        struct TwistChain { public string[] joints; public float[] weights; public string endChild; }
        static readonly TwistChain[] Chains =
        {
            new TwistChain { joints = new[] { "Spine1", "Spine2", "Chest" }, weights = new[] { 1f / 3f, 1f / 3f }, endChild = "Neck1" },
            new TwistChain { joints = new[] { "LeftLeg", "LeftShin" }, weights = new[] { 0.7f }, endChild = "LeftFoot" },
            new TwistChain { joints = new[] { "RightLeg", "RightShin" }, weights = new[] { 0.7f }, endChild = "RightFoot" },
        };

        /// <summary>Twist of a local rotation around an axis of its own frame, degrees in (-180, 180].</summary>
        static float TwistAngle(Quaternion q, Vector3 axis, out Quaternion swing)
        {
            if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            float along = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            var twist = new Quaternion(axis.x * along, axis.y * along, axis.z * along, q.w);
            float n = Mathf.Sqrt(along * along + q.w * q.w);
            twist = n > 1e-8f ? new Quaternion(twist.x / n, twist.y / n, twist.z / n, twist.w / n) : Quaternion.identity;
            swing = q * Quaternion.Inverse(twist);
            return 2f * Mathf.Atan2(along, q.w) * Mathf.Rad2Deg;
        }

        void SpreadTwists()
        {
            foreach (var c in Chains)
            {
                // the chain: each joint's bone runs to the next one; the end joint's bone runs to endChild
                int n = c.joints.Length;
                var t = new Transform[n + 1];
                bool ok = true;
                for (int i = 0; i < n; i++) { int k = bvh.Index(c.joints[i]); if (k < 0) { ok = false; break; } t[i] = bones[k]; }
                int ec = bvh.Index(c.endChild);
                if (!ok || ec < 0) continue;
                t[n] = bones[ec];
                for (int i = 0; i < n; i++) if (t[i + 1].parent != t[i]) ok = false;
                if (!ok) continue;

                var axes = new Vector3[n];
                float total = 0f;
                for (int i = 0; i < n; i++)
                {
                    axes[i] = t[i + 1].localPosition.normalized;
                    total += TwistAngle(t[i].localRotation, axes[i], out _);
                }
                total = Mathf.DeltaAngle(0f, total); // ±180° flips cancel
                // each joint but the last gets its share; the difference goes down to the next joint (the last keeps the rest)
                for (int i = 0; i < n - 1; i++)
                {
                    float delta = Mathf.DeltaAngle(TwistAngle(t[i].localRotation, axes[i], out _), total * c.weights[i]);
                    if (Mathf.Abs(delta) < 0.01f) continue;
                    Quaternion turn = Quaternion.AngleAxis(delta, axes[i]);
                    t[i].localRotation = t[i].localRotation * turn;
                    t[i + 1].localRotation = Quaternion.Inverse(turn) * t[i + 1].localRotation;
                }
            }
        }

        public PoseFrame Capture()
        {
            var p = new HumanPose();
            handler.GetHumanPose(ref p);
            return PoseFrame.From(p);
        }

        /// <summary>All frames of the BVH as a muscle-space animation.</summary>
        public HumanAnim ToHumanAnim(BvhData motion)
        {
            var a = new HumanAnim { fps = motion.Fps, loop = false };
            foreach (var f in motion.frames)
            {
                ApplyFrame(f);
                a.frames.Add(Capture());
            }
            return a;
        }

        public Vector3 BonePosition(string name) { int i = bvh.Index(name); return i >= 0 ? bones[i].position : Vector3.zero; }
    }
}
