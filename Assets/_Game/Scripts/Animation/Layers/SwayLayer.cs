using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Weapon sway (KINEMATION's SwayLayer): the weapon lags behind the look motion on springs (position + rotation),
    /// tilts with the movement input (move sway) and optionally aims inside a dead zone (free aim).
    /// Improvement: real damped springs (frame-rate independent), clamped targets.
    /// </summary>
    public class SwayLayer : AnimLayer
    {
        [Header("Look sway")]
        [Tooltip("Look rate (deg/s) → sway target factor")] public float lookRateScale = 0.01f;
        [Tooltip("Max look-sway rotation (degrees)")] public float maxRot = 8f;
        [Tooltip("Max look-sway offset (m)")] public float maxLoc = 0.03f;
        [Tooltip("Multiplier while aiming down sights (CharData.aim)")] public float aimScale = 0.28f;

        [Header("Free aim (dead zone)")]
        public bool freeAim;
        public bool useCircleMethod = true;

        protected VectorSpringState locState, rotState, moveLocState, moveRotState;
        protected Vector3 swayLoc, swayRot, targetMoveLoc, targetMoveRot, smoothMoveLoc, smoothMoveRot;
        protected Vector2 deadZoneTarget, deadZone;
        protected float smoothFreeAim;

        public Vector3 LookSwayRotation => swayRot;

        public override void OnPoseSampled()
        {
            locState.Reset(); rotState.Reset();
            targetMoveLoc = targetMoveRot = Vector3.zero;
        }

        public override void OnAnimUpdate()
        {
            if (DeltaTime <= 0f) return;
            var basePose = new LocRot(MasterPivot);
            ApplySway();
            ApplyMoveSway();
            ApplyFreeAim();
            BlendMaster(basePose, smoothLayerAlpha);
        }

        protected virtual void ApplySway()
        {
            float dt = DeltaTime;
            var s = Weapon.sway;
            Vector2 rate = CharData.deltaAimInput / dt * lookRateScale * Mathf.Lerp(1f, aimScale, CharData.aim);
            Vector3 targetLoc = Vector3.ClampMagnitude(Vector3.Scale(new Vector3(rate.x, rate.y, 0f), s.loc.scale), maxLoc);
            Vector3 targetRot = Vector3.ClampMagnitude(Vector3.Scale(new Vector3(rate.y, rate.x, rate.x), s.rot.scale), maxRot);
            var locSpring = s.loc; locSpring.scale = Vector3.one;
            var rotSpring = s.rot; rotSpring.scale = Vector3.one;
            swayLoc = AnimMath.SpringStep(ref locState, targetLoc, locSpring, dt);
            swayRot = AnimMath.SpringStep(ref rotState, targetRot, rotSpring, dt);
            AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, Quaternion.Euler(swayRot), 1f);
            AnimMath.MoveInSpace(RootBone, MasterPivot, swayLoc, 1f);
        }

        protected virtual void ApplyMoveSway()
        {
            float dt = DeltaTime;
            var d = Weapon.moveSway;
            Vector2 move = CharData.moveInput * Mathf.Lerp(1f, 0.35f, CharData.aim);
            var rotT = new Vector3(move.y * d.maxMoveRotSway.x, move.x * d.maxMoveRotSway.y, move.x * d.maxMoveRotSway.z);
            var locT = new Vector3(move.x * d.maxMoveLocSway.x, move.y * d.maxMoveLocSway.y, move.y * d.maxMoveLocSway.z);
            targetMoveRot = new Vector3(AnimMath.Damp(targetMoveRot.x, rotT.x, d.rotSpeed.x, dt), AnimMath.Damp(targetMoveRot.y, rotT.y, d.rotSpeed.y, dt), AnimMath.Damp(targetMoveRot.z, rotT.z, d.rotSpeed.z, dt));
            targetMoveLoc = new Vector3(AnimMath.Damp(targetMoveLoc.x, locT.x, d.locSpeed.x, dt), AnimMath.Damp(targetMoveLoc.y, locT.y, d.locSpeed.y, dt), AnimMath.Damp(targetMoveLoc.z, locT.z, d.locSpeed.z, dt));
            smoothMoveRot = AnimMath.SpringStep(ref moveRotState, targetMoveRot, d.moveRotSway, dt);
            smoothMoveLoc = AnimMath.SpringStep(ref moveLocState, targetMoveLoc, d.moveLocSway, dt);
            AnimMath.MoveInSpace(RootBone, MasterPivot, smoothMoveLoc, 1f);
            AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, Quaternion.Euler(smoothMoveRot), 1f);
        }

        /// <summary>Dead-zone aiming: the gun moves inside a cone before the view turns (rotation of the weapon only).</summary>
        protected virtual void ApplyFreeAim()
        {
            float dt = DeltaTime;
            var f = Weapon.freeAim;
            if (freeAim)
            {
                deadZoneTarget.x += CharData.deltaAimInput.y * f.scalar;
                deadZoneTarget.y += CharData.deltaAimInput.x * f.scalar;
            }
            else deadZoneTarget = Vector2.zero;
            deadZoneTarget.x = Mathf.Clamp(deadZoneTarget.x, -f.maxValue, f.maxValue);
            float maxY = useCircleMethod ? Mathf.Sqrt(Mathf.Max(0f, f.maxValue * f.maxValue - deadZoneTarget.x * deadZoneTarget.x)) : f.maxValue;
            deadZoneTarget.y = Mathf.Clamp(deadZoneTarget.y, -maxY, maxY);
            deadZone = AnimMath.Damp(deadZone, deadZoneTarget, f.speed, dt);
            smoothFreeAim = AnimMath.Damp(smoothFreeAim, freeAim ? 1f : 0f, 10f, dt);
            if (smoothFreeAim < 0.001f) return;
            var q = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(-deadZone.x, deadZone.y, 0f), smoothFreeAim);
            AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, q, 1f);
        }
    }
}
