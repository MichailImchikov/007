using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Aiming down sights (KINEMATION's AdsLayer): puts the weapon's aim point (sight) on the aim target (a point on
    /// the eye line) so the sight is exactly in the screen center. Also: point aiming (canted), per-weapon view offset
    /// and a crouch pose. Absolute aim (sight locked on the eye line) blends with an additive mode.
    /// The ADS weight can be driven by the layer's own timer (SetAds) or set from gameplay (SetAdsWeight).
    /// </summary>
    public class AdsLayer : AnimLayer
    {
        [Header("Sights aligner")]
        public EaseMode adsEaseMode = new EaseMode(EaseFunc.Sine);
        public EaseMode pointAimEaseMode = new EaseMode(EaseFunc.Sine);
        [Tooltip("0 = absolute (sight on the eye line), 1 = additive (base pose + sight offset)")]
        [Range(0f, 1f)] public float aimLayerAlphaLoc;
        [Range(0f, 1f)] public float aimLayerAlphaRot;
        [Tooltip("Point on the eye line where the sight goes when aiming")] public Transform aimTarget;

        [Tooltip("How far the sight travels to the eye when aiming (1 = all the way). The rotation always aligns fully. " +
                 "Below 1 the view has to come to the sight (true first person: the head goes onto the stock)")]
        [Range(0f, 1f)] public float positionScale = 1f;

        [Header("Crouch pose")]
        public LocRot crouchPose = LocRot.Identity;
        [Tooltip("Animator float / curve 0..1 that enables the crouch pose")] public string crouchPoseCurve;

        protected bool bAds, bPointAim;
        protected float adsProgress, pointAimProgress;
        protected float adsWeight, pointAimWeight;
        protected LocRot interpAimPoint = LocRot.Identity, viewOffsetCache = LocRot.Identity, viewOffset = LocRot.Identity;
        float externalAds = -1f;

        public float AdsWeight => adsWeight;
        public float PointAimWeight => pointAimWeight;

        public void SetAds(bool aiming)
        {
            if (aiming && !bAds) interpAimPoint = GetAdsOffset();
            bAds = aiming;
        }

        public void SetPointAim(bool aiming) => bPointAim = aiming;

        /// <summary>Drive the ADS weight directly (0..1, already eased). Negative = back to the layer's own timer.</summary>
        public void SetAdsWeight(float w)
        {
            if (w >= 0f && externalAds < 0f && w > 0.001f) interpAimPoint = GetAdsOffset();
            externalAds = w;
        }

        /// <summary>Snap the sight interpolation (after a weapon / sight change).</summary>
        public void ResetSight() => interpAimPoint = GetAdsOffset();

        public override void OnPoseSampled()
        {
            viewOffsetCache = viewOffset;
            ResetSight();
        }

        public override void OnAnimUpdate()
        {
            var basePose = new LocRot(MasterPivot);
            ApplyCrouchPose();
            ApplyPointAiming();
            ApplyAiming();
            BlendMaster(basePose, smoothLayerAlpha);
        }

        protected LocRot GetAdsOffset()
        {
            var aim = AimPoint;
            var pivot = PivotPoint;
            if (!aim) return LocRot.Identity;
            Quaternion pr = pivot ? pivot.rotation : MasterPivot.rotation;
            Vector3 pp = pivot ? pivot.position : MasterPivot.position;
            Quaternion inv = Quaternion.Inverse(pr);
            return new LocRot(-(inv * (aim.position - pp)), inv * aim.rotation);
        }

        protected void UpdateAimWeights(float adsRate, float pointAimRate)
        {
            float dt = DeltaTime;
            adsProgress = Mathf.Clamp01(adsProgress + dt * (bAds ? adsRate : -adsRate));
            pointAimProgress = Mathf.Clamp01(pointAimProgress + dt * (bPointAim ? pointAimRate : -pointAimRate));
            adsWeight = externalAds >= 0f ? Mathf.Clamp01(externalAds) : adsEaseMode.Apply(adsProgress);
            pointAimWeight = pointAimEaseMode.Apply(pointAimProgress);
        }

        protected virtual void ApplyAiming()
        {
            var ads = Weapon.ads;
            var master = MasterPivot;

            var defaultPose = new LocRot(master);
            ApplyHandsOffset();
            var handsPose = new LocRot(master);
            defaultPose.ApplyWorld(master);

            UpdateAimWeights(Mathf.Max(0.01f, ads.aimSpeed), Mathf.Max(0.01f, ads.pointAimSpeed));
            interpAimPoint = LocRot.Damp(interpAimPoint, GetAdsOffset(), ads.changeSightSpeed, DeltaTime);

            // additive: base pose + the sight offset
            AnimMath.MoveInSpace(master, master, ads.additiveAim.position, 1f);
            master.rotation *= ads.additiveAim.Rot;
            AnimMath.MoveInSpace(master, master, interpAimPoint.position, 1f);
            var additive = new LocRot(master);

            // absolute: the sight exactly on the aim target
            handsPose.ApplyWorld(master);
            ApplyAbsAim(interpAimPoint.position, interpAimPoint.Rot);

            master.position = Vector3.Lerp(master.position, additive.position, aimLayerAlphaLoc);
            master.rotation = Quaternion.Slerp(master.rotation, additive.rotation, aimLayerAlphaRot);

            float aimWeight = Mathf.Clamp01(adsWeight - pointAimWeight);
            // the sight always turns fully parallel to the view; it travels to the eye by positionScale of the way
            // (a shouldered rifle only rises a little, the eye comes down to the sight instead)
            var aimed = new LocRot(master);
            master.SetPositionAndRotation(Vector3.Lerp(handsPose.position, aimed.position, aimWeight * positionScale),
                                          Quaternion.Slerp(handsPose.Rot, aimed.Rot, aimWeight));
        }

        protected void ApplyCrouchPose()
        {
            if (string.IsNullOrEmpty(crouchPoseCurve)) return;
            float a = GetCurveValue(crouchPoseCurve) * (1f - adsWeight);
            if (a <= 0f) return;
            AnimMath.MoveInSpace(RootBone, MasterPivot, crouchPose.position, a);
            AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, crouchPose.Rot, a);
        }

        protected virtual void ApplyPointAiming()
        {
            if (pointAimWeight <= 0f) return;
            var o = Weapon.ads.pointAimOffset;
            AnimMath.MoveInSpace(RootBone, MasterPivot, o.position, pointAimWeight);
            AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, o.Rot, pointAimWeight);
        }

        /// <summary>Per-weapon view offset, blended with the overlay pose change (weapon switch).</summary>
        protected virtual void ApplyHandsOffset()
        {
            float progress = Graph != null ? Graph.PoseProgress : 1f;
            viewOffset = LocRot.Lerp(viewOffsetCache, Weapon.viewOffset, progress);
            if (progress >= 0.999f) viewOffsetCache = viewOffset;
            AnimMath.MoveInSpace(RootBone, MasterPivot, viewOffset.position, 1f);
            AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, viewOffset.Rot, 1f);
        }

        /// <summary>Master so that the aim point sits on the aim target, oriented with the root (camera) frame.</summary>
        protected virtual void ApplyAbsAim(Vector3 loc, Quaternion rot)
        {
            var master = MasterPivot;
            Vector3 targetPos = aimTarget ? aimTarget.position : RootBone.position + RootBone.forward * Weapon.aimDistance;
            master.rotation = RootBone.rotation * Quaternion.Inverse(rot);
            master.position = targetPos + master.rotation * loc;
        }
    }
}
