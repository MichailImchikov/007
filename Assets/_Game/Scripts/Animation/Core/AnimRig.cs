using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Two-bone limb with an IK goal and an elbow / knee hint object (KINEMATION's DynamicBone).
    /// The chain bones are explicit, so twist bones between the forearm and the hand don't matter.
    /// </summary>
    [Serializable]
    public class IKLimb
    {
        public Transform upper, lower, tip;
        [Tooltip("IK goal object (layers move it, the solver puts the tip on it)")] public Transform goal;
        [Tooltip("Elbow / knee hint object")] public Transform hint;
        [NonSerialized] public float positionWeight = 1f, rotationWeight = 1f, hintWeight = 1f;

        public bool Valid => upper && lower && tip && goal;
        public float Length => Valid ? Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, tip.position) : 0f;

        /// <summary>Goal = current tip pose.</summary>
        public void RetargetGoal() { if (goal && tip) goal.SetPositionAndRotation(tip.position, tip.rotation); }

        /// <summary>Hint = current mid bone pose.</summary>
        public void RetargetHint() { if (hint && lower) hint.SetPositionAndRotation(lower.position, lower.rotation); }

        public void Solve(float weight)
        {
            if (!Valid || weight <= 0f) return;
            AnimMath.SolveTwoBoneIK(upper, lower, tip, goal.position, goal.rotation, hint ? hint.position : Vector3.zero,
                weight * positionWeight, weight * rotationWeight, weight * hintWeight, hint);
        }
    }

    /// <summary>
    /// Distributes the forearm roll the IK put on the hand over the forearm and its twist bone, so the wrist does not
    /// collapse ("candy wrapper") when the hand turns on a grip.
    /// </summary>
    [Serializable]
    public class TwistCorrector
    {
        public Transform lower, hand, twist;
        [Range(0f, 1f)] public float lowerShare = 0.45f;
        [Range(0f, 1f)] public float twistShare = 0.5f;
        Quaternion restRelative = Quaternion.identity, twistRest = Quaternion.identity;
        Vector3 axisLocal = Vector3.right;
        bool ready;

        public void Init()
        {
            ready = lower && hand;
            if (!ready) return;
            restRelative = Quaternion.Inverse(lower.rotation) * hand.rotation;
            axisLocal = lower.InverseTransformDirection(hand.position - lower.position).normalized;
            if (twist) twistRest = twist.localRotation;
        }

        /// <summary>Call after the IK, before rendering.</summary>
        public void Apply(float weight)
        {
            if (!ready || weight <= 0f) return;
            Quaternion rel = Quaternion.Inverse(lower.rotation) * hand.rotation * Quaternion.Inverse(restRelative);
            float ang = AnimMath.TwistAngle(rel, axisLocal) * weight;
            if (Mathf.Abs(ang) < 0.5f) { if (twist) twist.localRotation = twistRest; return; }
            Quaternion handWorld = hand.rotation;
            lower.rotation = lower.rotation * Quaternion.AngleAxis(ang * lowerShare, axisLocal);
            hand.rotation = handWorld;
            if (twist) twist.localRotation = twistRest * Quaternion.AngleAxis(ang * (1f - lowerShare) * twistShare, twist.InverseTransformDirection(lower.TransformDirection(axisLocal)));
        }
    }

    /// <summary>
    /// Puts a palm on a grip anchor. The anchor's position is where the palm center goes, its forward is the finger
    /// direction and its up the palm normal (HandGrips convention). Calibrated from the finger bones of the avatar.
    /// </summary>
    [Serializable]
    public class PalmSolver
    {
        public Transform hand;
        public bool left;
        Vector3 fingerLocal = Vector3.forward, palmNormalLocal = Vector3.up, palmLocal;
        public bool Ready { get; private set; }

        public static PalmSolver Make(Animator an, bool left)
        {
            if (!an || !an.isHuman) return null;
            Transform B(HumanBodyBones l, HumanBodyBones r) => an.GetBoneTransform(left ? l : r);
            var p = new PalmSolver { left = left, hand = B(HumanBodyBones.LeftHand, HumanBodyBones.RightHand) };
            var lower = B(HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm);
            if (!p.hand || !lower) return null;
            var mid = B(HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal);
            var index = B(HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal);
            var little = B(HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal) ?? B(HumanBodyBones.LeftRingProximal, HumanBodyBones.RightRingProximal);
            Transform h = p.hand;
            Vector3 knuckles = mid ? mid.position : h.position + (h.position - lower.position).normalized * 0.09f;
            p.fingerLocal = h.InverseTransformDirection(knuckles - h.position).normalized;
            Vector3 across = index && little ? index.position - little.position : Vector3.Cross(h.position - lower.position, Vector3.up);
            Vector3 t = h.InverseTransformDirection(across).normalized;
            Vector3 n = Vector3.Cross(p.fingerLocal, t).normalized;
            p.palmNormalLocal = left ? -n : n;
            Vector3 palmW = Vector3.Lerp(h.position, knuckles, 0.55f) + h.TransformDirection(p.palmNormalLocal) * 0.022f;
            p.palmLocal = h.InverseTransformPoint(palmW);
            p.Ready = true;
            return p;
        }

        /// <summary>Hand rotation mapping the finger / palm axes onto the grip's forward / up.</summary>
        public Quaternion HandRotation(Quaternion grip)
            => grip * Quaternion.Inverse(Quaternion.LookRotation(fingerLocal, palmNormalLocal));

        /// <summary>Wrist position that puts the palm center on palmPos with the given hand rotation.</summary>
        public Vector3 WristFor(Vector3 palmPos, Quaternion handRot)
        {
            Vector3 offLocal = Vector3.Scale(palmLocal, hand.lossyScale);
            return palmPos - handRot * offLocal;
        }

        /// <summary>World wrist pose for a grip anchor.</summary>
        public LocRot WristPose(Transform grip)
        {
            Quaternion rot = HandRotation(grip.rotation);
            return new LocRot(WristFor(grip.position, rot), rot);
        }

        public LocRot WristPose(Vector3 gripPos, Quaternion gripRot)
        {
            Quaternion rot = HandRotation(gripRot);
            return new LocRot(WristFor(gripPos, rot), rot);
        }
    }

    /// <summary>
    /// Skeleton data the layers work on (KINEMATION's DynamicRigData). Built from a humanoid Animator by Setup().
    /// Hierarchy of the helper objects:
    ///   rootBone (aim frame: the camera in first person, the eyes in third person)
    ///     ├─ WeaponBone        weapon hangs here (final pose after the layers)
    ///     ├─ WeaponBoneAdditive
    ///     └─ MasterIK          master pivot the layers move; hand goals / elbow hints are its children
    ///   RightFootIK / LeftFootIK (under the character root)
    /// </summary>
    [Serializable]
    public class AnimRig
    {
        public Animator animator;
        public Transform pelvis, spineRoot, chest, upperChest, neck, head;
        public Transform rootBone;
        public Transform weaponBone, weaponBoneAdditive;
        public Transform weaponBoneRight, weaponBoneLeft;
        public Transform masterIK;
        public IKLimb rightHand = new IKLimb(), leftHand = new IKLimb(), rightFoot = new IKLimb(), leftFoot = new IKLimb();
        public TwistCorrector rightTwist = new TwistCorrector(), leftTwist = new TwistCorrector();
        [NonSerialized] public PalmSolver rightPalm, leftPalm;

        /// <summary>Weapon follows the right hand (&gt;0) or the left hand (&lt;0) instead of the weapon bone (animations).</summary>
        [NonSerialized] public float weaponBoneWeight;
        /// <summary>Weapon bone pose in root bone space (the base hold pose).</summary>
        [NonSerialized] public LocRot weaponTransform = LocRot.Identity;

        /// <summary>Spine bones from the hips up (look layer default chain).</summary>
        public List<Transform> SpineChain()
        {
            var l = new List<Transform>();
            foreach (var b in new[] { spineRoot, chest, upperChest, neck, head }) if (b && !l.Contains(b)) l.Add(b);
            return l;
        }

        public bool Valid => animator && pelvis && rootBone && weaponBone && masterIK && rightHand.Valid && leftHand.Valid;

        public Quaternion PelvisMS => Quaternion.Inverse(rootBone.rotation) * pelvis.rotation;

        /// <summary>Weapon bone from the base pose (root bone space).</summary>
        public void RetargetWeaponBone()
        {
            weaponBone.position = rootBone.TransformPoint(weaponTransform.position);
            weaponBone.rotation = rootBone.rotation * weaponTransform.Rot;
        }

        public void RetargetHandBones()
        {
            if (weaponBoneRight) weaponBoneRight.SetPositionAndRotation(weaponBone.position, weaponBone.rotation);
            if (weaponBoneLeft) weaponBoneLeft.SetPositionAndRotation(weaponBone.position, weaponBone.rotation);
        }

        /// <summary>Master follows the hand-attached weapon bone copies by weaponBoneWeight (weapon animated in a hand).</summary>
        public void UpdateWeaponParent()
        {
            if (Mathf.Approximately(weaponBoneWeight, 0f)) return;
            var master = new LocRot(masterIK);
            LocRot target = weaponBoneWeight > 0f ? (weaponBoneRight ? new LocRot(weaponBoneRight) : master)
                                                  : (weaponBoneLeft ? new LocRot(weaponBoneLeft) : master);
            LocRot.Lerp(master, target, Mathf.Abs(weaponBoneWeight)).ApplyWorld(masterIK);
        }

        static Transform Make(string name, Transform parent)
        {
            var t = parent.Find(name);
            if (t) return t;
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        /// <summary>
        /// Finds the bones of a humanoid and creates the helper objects. owner: where the root bone / foot goals live.
        /// </summary>
        public bool Setup(Animator an, Transform owner)
        {
            animator = an;
            if (!an || !an.isHuman) return false;
            Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
            pelvis = B(HumanBodyBones.Hips);
            spineRoot = B(HumanBodyBones.Spine);
            chest = B(HumanBodyBones.Chest);
            upperChest = B(HumanBodyBones.UpperChest);
            neck = B(HumanBodyBones.Neck);
            head = B(HumanBodyBones.Head);

            if (!rootBone) rootBone = Make("RootBone", owner);
            if (!weaponBone) weaponBone = Make("WeaponBone", rootBone);
            if (!weaponBoneAdditive) weaponBoneAdditive = Make("WeaponBoneAdditive", rootBone);
            if (!masterIK) masterIK = Make("MasterIK", rootBone);

            void Limb(IKLimb l, HumanBodyBones u, HumanBodyBones m, HumanBodyBones t, string goalName, string hintName, Transform goalParent)
            {
                l.upper = B(u); l.lower = B(m); l.tip = B(t);
                if (!l.goal) l.goal = Make(goalName, goalParent);
                if (!l.hint) l.hint = Make(hintName, goalParent);
            }
            Limb(rightHand, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, "RightHandIK", "RightElbowIK", masterIK);
            Limb(leftHand, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, "LeftHandIK", "LeftElbowIK", masterIK);
            Limb(rightFoot, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, "RightFootIK", "RightKneeIK", owner);
            Limb(leftFoot, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, "LeftFootIK", "LeftKneeIK", owner);

            if (rightHand.tip && !weaponBoneRight) weaponBoneRight = Make("WeaponBoneRight", rightHand.tip);
            if (leftHand.tip && !weaponBoneLeft) weaponBoneLeft = Make("WeaponBoneLeft", leftHand.tip);

            rightTwist.lower = rightHand.lower; rightTwist.hand = rightHand.tip; rightTwist.twist = FindTwist(rightHand.lower);
            leftTwist.lower = leftHand.lower; leftTwist.hand = leftHand.tip; leftTwist.twist = FindTwist(leftHand.lower);
            rightTwist.Init(); leftTwist.Init();
            rightPalm = PalmSolver.Make(an, false);
            leftPalm = PalmSolver.Make(an, true);
            return Valid;
        }

        /// <summary>A twist bone directly under the forearm ("lowerarm_twist_01_l", "ForeArmRoll"...).</summary>
        static Transform FindTwist(Transform lower)
        {
            if (!lower) return null;
            foreach (Transform c in lower)
            {
                string n = c.name.ToLowerInvariant();
                if (n.Contains("twist") || n.Contains("roll")) return c;
            }
            return null;
        }
    }
}
