using UnityEngine;

namespace FPS.Animation
{
    public enum ReadyPose { LowReady, HighReady }

    /// <summary>
    /// Locomotion layer (KINEMATION's LocomotionLayer): ready poses (low / high ready), sprint pose with a lean into
    /// turns, curve-based weapon motion from animator curves (IK_T_X..Z / IK_R_X..Z) and the additive weapon bone.
    /// The layer weight is the sprint weight: overlay poses fade out (graph weight) while sprinting if requested.
    /// </summary>
    public class LocomotionLayer : AnimLayer
    {
        [Header("Ready poses")]
        public LocRot highReadyPose = new LocRot(new Vector3(0f, 0.04f, -0.06f), Quaternion.Euler(-28f, 10f, -10f));
        public LocRot lowReadyPose = new LocRot(new Vector3(0f, -0.06f, -0.08f), Quaternion.Euler(32f, -18f, 12f));
        public ReadyPose readyPoseType;
        public float readyInterpSpeed = 8f;

        [Header("Curve-based animation (animator curves)")]
        public float rotationScale = 1f;
        public float translationScale = 1f;
        public float smoothSpeed = 10f;

        [Header("Sprint")]
        [Tooltip("Animator float / curve weighting the sprint pose")] public string sprintPoseWeight;
        public AnimationCurve sprintBlendCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        public LocRot sprintPose = new LocRot(new Vector3(-0.05f, -0.055f, -0.05f), Quaternion.Euler(16f, -42f, 22f));
        [Tooltip("Fade overlay poses out with the sprint weight (sprint arms from the base animation)")] public bool sprintFadesOverlay;

        float readyAlpha, smoothReady, smoothSprintLean;
        LocRot smoothLoco = LocRot.Identity, curveAnimation = LocRot.Identity;
        static readonly int RotX = Animator.StringToHash("IK_R_X"), RotY = Animator.StringToHash("IK_R_Y"), RotZ = Animator.StringToHash("IK_R_Z");
        static readonly int LocX = Animator.StringToHash("IK_T_X"), LocY = Animator.StringToHash("IK_T_Y"), LocZ = Animator.StringToHash("IK_T_Z");

        public void SetReadyWeight(float w) => readyAlpha = Mathf.Clamp01(w);

        public override void OnPreAnimUpdate()
        {
            base.OnPreAnimUpdate();
            smoothLayerAlpha *= 1f - GetCurveValue(AnimCurves.Overlay);
            if (Graph != null && sprintFadesOverlay) Graph.SetGraphWeight(1f - smoothLayerAlpha);
            if (Graph != null) Rig.weaponBoneWeight = GetCurveValue(AnimCurves.WeaponBone);
        }

        public override void OnAnimUpdate()
        {
            ApplyReadyPose();
            ApplyLocomotion();
        }

        void ApplyReadyPose()
        {
            float a = readyAlpha * (1f - smoothLayerAlpha);
            smoothReady = AnimMath.Damp(smoothReady, a, readyInterpSpeed, DeltaTime);
            if (smoothReady <= 0.0001f) return;
            var pose = readyPoseType == ReadyPose.HighReady ? highReadyPose : lowReadyPose;
            AnimMath.MoveInSpace(RootBone, MasterPivot, pose.position, smoothReady);
            AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, pose.Rot, smoothReady);
        }

        void UpdateCurveAnimation()
        {
            if (Graph == null) return;
            var c = Graph;
            curveAnimation.rotation = Quaternion.Euler(new Vector3(c.GetFloat(RotX), c.GetFloat(RotY), c.GetFloat(RotZ)) * rotationScale);
            curveAnimation.position = new Vector3(c.GetFloat(LocX), c.GetFloat(LocY), c.GetFloat(LocZ)) * translationScale;
        }

        void ApplyLocomotion()
        {
            var master = MasterPivot;
            float dt = DeltaTime;
            smoothSprintLean = Mathf.Clamp(AnimMath.Damp(smoothSprintLean, 4f * CharData.deltaAimInput.x, 3f, dt), -15f, 15f);

            float alpha = sprintBlendCurve.Evaluate(smoothLayerAlpha);
            float locoAlpha = (1f - alpha) * layerAlpha;
            if (alpha > 0f && Pelvis)
                AnimMath.RotateInSpace(RootBone.rotation, Pelvis, Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, smoothSprintLean, -smoothSprintLean), alpha), 1f);

            UpdateCurveAnimation();
            AnimMath.RotateInSpace(RootBone.rotation, master, curveAnimation.Rot, locoAlpha);
            AnimMath.MoveInSpace(RootBone, master, curveAnimation.position, locoAlpha);

            if (Rig.weaponBoneAdditive)
            {
                smoothLoco = LocRot.Damp(smoothLoco, new LocRot(Rig.weaponBoneAdditive, false), smoothSpeed, dt);
                AnimMath.MoveInSpace(RootBone, master, smoothLoco.position, locoAlpha);
                AnimMath.RotateInSpace(RootBone.rotation, master, smoothLoco.Rot, locoAlpha);
            }

            if (!string.IsNullOrEmpty(sprintPoseWeight)) alpha *= GetCurveValue(sprintPoseWeight);
            if (alpha <= 0f) return;
            AnimMath.MoveInSpace(RootBone, master, sprintPose.position, alpha);
            AnimMath.RotateInSpace(master.rotation, master, sprintPose.Rot, alpha);
        }
    }
}
