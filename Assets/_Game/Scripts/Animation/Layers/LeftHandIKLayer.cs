using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Support hand on the weapon (KINEMATION's LeftHandIKLayer) for setups without grip IK: the left hand goal is
    /// moved to the weapon's left-hand target relative to the master pivot. A mask curve (MaskLeftHand) lets
    /// animations take the hand off the weapon. With grip IK (default) the core already does this.
    /// </summary>
    public class LeftHandIKLayer : AnimLayer
    {
        [Tooltip("Curve that frees the left hand (1 = animated hand)")] public string maskCurveName = AnimCurves.MaskLeftHand;

        LocRot defaultLeftHand = LocRot.Identity, cache = LocRot.Identity, final = LocRot.Identity;

        public override void OnPoseSampled()
        {
            cache = final;
            if (!PivotPoint) return;
            defaultLeftHand = new LocRot(Quaternion.Inverse(PivotPoint.rotation) * (Rig.leftHand.tip.position - PivotPoint.position),
                                         Quaternion.Inverse(PivotPoint.rotation) * Rig.leftHand.tip.rotation);
        }

        public override void OnAnimUpdate()
        {
            if (Weapon.gripIK) return;
            var master = MasterPivot;
            LocRot target;
            var t = Transforms.leftHandTarget;
            if (t && Rig.leftPalm != null && Rig.leftPalm.Ready)
            {
                // palm on the target, expressed relative to the master
                LocRot wrist = Rig.leftPalm.WristPose(t);
                Quaternion inv = Quaternion.Inverse(master.rotation);
                target = new LocRot(inv * (wrist.position - master.position), inv * wrist.Rot);
            }
            else target = new LocRot(defaultLeftHand.position - PivotOffset, defaultLeftHand.Rot);

            float alpha = (1f - GetCurveValue(maskCurveName)) * smoothLayerAlpha;
            float progress = Graph != null ? Graph.PoseProgress : 1f;
            final = LocRot.Lerp(cache, target, progress);
            if (alpha <= 0f) return;
            var world = new LocRot(master.position + master.rotation * final.position, master.rotation * final.Rot);
            LocRot.Lerp(new LocRot(LeftHandGoal), world, alpha).ApplyWorld(LeftHandGoal);
        }
    }
}
