using System;
using UnityEngine;

namespace FPS.Animation
{
    public enum FireMode { Semi, Burst, Auto }

    [Serializable]
    public struct RecoilCurves
    {
        [Tooltip("0..1 progress to the random target, per axis (semi auto / first shot)")] public VectorCurve semiRotCurve;
        public VectorCurve semiLocCurve;
        [Tooltip("Loop of sustained full-auto fire (length = one shot interval is enough)")] public VectorCurve autoRotCurve;
        public VectorCurve autoLocCurve;

        public RecoilCurves(params Keyframe[] keys)
        {
            semiRotCurve = new VectorCurve(keys);
            semiLocCurve = new VectorCurve(keys);
            autoRotCurve = new VectorCurve(keys);
            autoLocCurve = new VectorCurve(keys);
        }
    }

    /// <summary>
    /// Procedural recoil of the weapon (visual kick of the arms / gun, the camera recoil is separate).
    /// Same model as KINEMATION's RecoilAnimData: random rotation / translation targets per shot reached along curves,
    /// smoothing for full auto, accumulating noise and a push-back layer.
    /// </summary>
    [CreateAssetMenu(fileName = "NewRecoilProfile", menuName = "FPS/Animation/Recoil Profile")]
    public class RecoilProfile : ScriptableObject
    {
        [Header("Rotation targets (degrees)")]
        [Tooltip("min..max, negative = muzzle up")] public Vector2 pitch = new Vector2(-6f, -9f);
        [Tooltip("(left min, left max, right min, right max)")] public Vector4 yaw = new Vector4(-1.5f, -0.4f, 0.4f, 1.5f);
        [Tooltip("(left min, left max, right min, right max)")] public Vector4 roll = new Vector4(-4f, -1.5f, 1.5f, 4f);

        [Header("Translation targets (meters)")]
        [Tooltip("Into the shoulder (negative = back)")] public Vector2 kickback = new Vector2(-0.035f, -0.05f);
        public Vector2 kickUp = new Vector2(0.002f, 0.007f);
        public Vector2 kickRight = new Vector2(-0.004f, 0.004f);

        [Header("Aiming multipliers")]
        public Vector3 aimRot = new Vector3(0.45f, 0.4f, 0.35f);
        public Vector3 aimLoc = new Vector3(0.4f, 0.4f, 0.55f);

        [Header("Full auto smoothing")]
        public Vector3 smoothRot = new Vector3(18f, 18f, 18f);
        public Vector3 smoothLoc = new Vector3(20f, 20f, 20f);
        public Vector3 extraRot = Vector3.one;
        public Vector3 extraLoc = Vector3.one;

        [Header("Noise layer")]
        public Vector2 noiseX = new Vector2(-0.0015f, 0.0015f);
        public Vector2 noiseY = new Vector2(0.0005f, 0.002f);
        public Vector2 noiseAccel = new Vector2(12f, 12f);
        public Vector2 noiseDamp = new Vector2(6f, 6f);
        public float noiseScalar = 0.5f;

        [Header("Push-back layer")]
        public float pushAmount;
        public float pushAccel = 10f;
        public float pushDamp = 8f;

        [Header("Misc")]
        [Tooltip("Alternate the roll direction between shots")] public bool smoothRoll = true;
        public float playRate = 1f;

        [Header("Curves")]
        public RecoilCurves recoilCurves = new RecoilCurves(new Keyframe(0f, 0f), new Keyframe(0.035f, 1f), new Keyframe(0.25f, 0f));

        /// <summary>Recoil profile built from a few gameplay numbers (used when a weapon has no profile asset).</summary>
        public static RecoilProfile CreateRuntime(string name, float kick, float climb, bool pistol)
        {
            var d = CreateInstance<RecoilProfile>();
            d.name = "Recoil_" + name;
            float k = Mathf.Clamp(kick, 0.5f, 2.2f);
            float up = Mathf.Clamp(climb, 0.3f, 5f);
            d.pitch = new Vector2(-4f - up * 1.6f, -6f - up * 2.2f) * k;
            d.yaw = new Vector4(-1.6f, -0.4f, 0.4f, 1.6f) * k;
            d.roll = new Vector4(-4f, -1.5f, 1.5f, 4f) * k;
            d.kickback = new Vector2(-0.035f, -0.05f) * k * (pistol ? 0.7f : 1f);
            d.kickUp = new Vector2(0.002f, 0.007f) * k;
            d.kickRight = new Vector2(-0.004f, 0.004f) * k;
            float len = Mathf.Clamp(0.12f + 0.06f * k + 0.03f * up, 0.16f, 0.42f);
            Keyframe[] rot = { new Keyframe(0f, 0f), new Keyframe(0.035f, 1f), new Keyframe(len, 0f) };
            Keyframe[] loc = { new Keyframe(0f, 0f), new Keyframe(0.03f, 1f), new Keyframe(len * 0.85f, 0f) };
            d.recoilCurves = new RecoilCurves
            {
                semiRotCurve = new VectorCurve(rot), semiLocCurve = new VectorCurve(loc),
                autoRotCurve = new VectorCurve(rot), autoLocCurve = new VectorCurve(loc),
            };
            return d;
        }
    }
}
