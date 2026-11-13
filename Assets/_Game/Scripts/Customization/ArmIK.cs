using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Two-bone arm IK on a humanoid skeleton with explicit bones (upper arm, forearm, hand), so twist bones
    /// between the forearm and the hand (genAssault: ForeArm > ForeArmRoll > Hand) don't break it.
    /// The hand is placed on a grip anchor: the anchor's position is where the palm goes, its forward is the
    /// finger direction and its up the palm normal (see HandGrips).
    /// </summary>
    public class ArmIK
    {
        public Transform upper, lower, hand;
        public bool left;
        Vector3 fingerLocal, palmNormalLocal, palmLocal;
        public float Reach { get; private set; }

        public static ArmIK Make(Animator an, bool left)
        {
            if (!an || !an.isHuman) return null;
            var a = new ArmIK
            {
                left = left,
                upper = an.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
                lower = an.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
                hand = an.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand),
            };
            if (!a.upper || !a.lower || !a.hand) return null;
            Transform B(HumanBodyBones l, HumanBodyBones r) => an.GetBoneTransform(left ? l : r);
            var mid = B(HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal);
            var index = B(HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal);
            var little = B(HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal) ?? B(HumanBodyBones.LeftRingProximal, HumanBodyBones.RightRingProximal);
            var h = a.hand;
            Vector3 knuckles = mid ? mid.position : h.position + (h.position - a.lower.position).normalized * 0.09f;
            a.fingerLocal = h.InverseTransformDirection(knuckles - h.position).normalized;
            Vector3 across = index && little ? index.position - little.position : Vector3.Cross(h.position - a.lower.position, Vector3.up);
            Vector3 t = h.InverseTransformDirection(across).normalized;
            Vector3 n = Vector3.Cross(a.fingerLocal, t).normalized;
            a.palmNormalLocal = left ? -n : n;
            // palm center: a bit past the middle of the metacarpals, pushed out of the palm surface
            Vector3 palmW = Vector3.Lerp(h.position, knuckles, 0.55f) + h.TransformDirection(a.palmNormalLocal) * 0.022f;
            a.palmLocal = h.InverseTransformPoint(palmW);
            a.Reach = Vector3.Distance(a.upper.position, a.lower.position) + Vector3.Distance(a.lower.position, a.hand.position);
            return a;
        }

        /// <summary>Hand rotation that maps the hand's finger / palm axes onto the grip anchor's forward / up.</summary>
        public Quaternion HandRotation(Transform grip)
            => Quaternion.LookRotation(grip.forward, grip.up) * Quaternion.Inverse(Quaternion.LookRotation(fingerLocal, palmNormalLocal));

        /// <summary>Wrist position that puts the palm center on the grip with the given hand rotation.</summary>
        public Vector3 WristFor(Vector3 palmPos, Quaternion handRot)
        {
            Vector3 offW = hand.TransformVector(palmLocal);                  // current world offset wrist -> palm
            Vector3 rotated = handRot * (Quaternion.Inverse(hand.rotation) * offW);
            return palmPos - rotated;
        }

        /// <summary>Puts the palm on the grip (weight 0..1). hintDir: where the elbow should point (world).</summary>
        public void Solve(Transform grip, Vector3 hintDir, float weight, float maxStretch = 0.999f)
        {
            if (!grip || weight <= 0.001f) return;
            if ((grip.position - upper.position).sqrMagnitude > Reach * Reach * 2.56f) return; // not this body's gun / way out of reach
            Quaternion rot = HandRotation(grip);
            Vector3 wrist = WristFor(grip.position, rot);
            Solve(wrist, rot, hintDir, weight, maxStretch);
        }

        public void Solve(Vector3 wrist, Quaternion rot, Vector3 hintDir, float weight, float maxStretch = 0.999f)
        {
            Vector3 pa = upper.position, pb = lower.position, pc = hand.position;
            float lab = (pb - pa).magnitude, lbc = (pc - pb).magnitude;
            Vector3 t = Vector3.Lerp(pc, wrist, weight);
            Vector3 at = t - pa;
            float d = Mathf.Clamp(at.magnitude, 1e-4f, (lab + lbc) * maxStretch);
            Vector3 dir = at.sqrMagnitude > 1e-8f ? at.normalized : (pc - pa).normalized;

            Vector3 bend = Vector3.ProjectOnPlane(hintDir, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(pb - pa, dir);
            bend.Normalize();
            float cosA = (lab * lab + d * d - lbc * lbc) / (2f * lab * d);
            float angA = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
            Vector3 elbow = pa + (dir * Mathf.Cos(angA) + bend * Mathf.Sin(angA)) * lab;

            upper.rotation = Quaternion.FromToRotation(pb - pa, elbow - pa) * upper.rotation;
            pb = lower.position; pc = hand.position;
            lower.rotation = Quaternion.FromToRotation(pc - pb, (pa + dir * d) - pb) * lower.rotation;
            hand.rotation = Quaternion.Slerp(hand.rotation, rot, weight);
        }
    }

    /// <summary>
    /// Hand grip anchors on a gun model: "GripR" (pistol grip / trigger hand) and "GripL" (support hand, moves with
    /// the reload animation's left hand). forward = finger direction, up = palm normal, in gun space (+Z = muzzle).
    /// </summary>
    public static class HandGrips
    {
        public const string Right = "GripR", Left = "GripL";

        public static void Ensure(WeaponController w)
        {
            if (!w || !w.model || w.model.Find(Right)) return;
            bool pistol = w.slot == WeaponSlot.Secondary;
            var model = w.model;

            // right palm: the procedural glove sits where the trigger hand holds the grip
            var glove = Find(model, "RightGlove");
            Vector3 rPos = glove ? model.InverseTransformPoint(glove.position) : new Vector3(0.004f, -0.048f, -0.042f);
            var r = new GameObject(Right).transform;
            r.SetParent(model, false);
            r.localPosition = rPos + new Vector3(0.012f, 0.004f, -0.008f);
            r.localRotation = pistol
                ? Quaternion.LookRotation(new Vector3(-0.2f, -0.75f, 0.6f), new Vector3(-1f, 0f, 0.15f))
                : Quaternion.LookRotation(new Vector3(-0.25f, -0.6f, 0.75f), new Vector3(-1f, 0f, 0.1f));

            // left palm: the procedural left hand grip (under the left arm, so reloads move it)
            Transform lParent = w.leftHand ? w.leftHand : model;
            Vector3 lPosModel = w.leftHandGrip ? model.InverseTransformPoint(w.leftHandGrip.position) : new Vector3(0f, -0.02f, 0.3f);
            var l = new GameObject(Left).transform;
            l.SetParent(lParent, false);
            l.position = model.TransformPoint(lPosModel + (pistol ? new Vector3(-0.01f, -0.005f, 0f) : new Vector3(-0.012f, -0.022f, 0f)));
            l.rotation = model.rotation * (pistol
                ? Quaternion.LookRotation(new Vector3(0.25f, -0.5f, 0.8f), new Vector3(1f, 0.3f, 0f))
                : Quaternion.LookRotation(new Vector3(0.5f, 0.25f, 0.8f), new Vector3(0.3f, 1f, 0f)));
        }

        public static Transform Find(Transform root, string name)
        {
            if (!root) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }
    }
}
