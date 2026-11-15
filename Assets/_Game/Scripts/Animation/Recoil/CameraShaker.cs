using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Curve-based camera shake (KINEMATION's FPSCamera). Several shakes can overlap (the old one fades into the new
    /// one through the smoothing). Call Apply(transform) after the camera got its final rotation this frame.
    /// </summary>
    public class CameraShaker
    {
        CameraShakeInfo shake;
        Vector3 target, output;
        float playBack;
        bool active;

        public Vector3 Output => output;

        static float RandomTarget(Vector4 t) => Random.value < 0.5f ? Random.Range(t.x, t.y) : Random.Range(t.z, t.w);

        public void Play(CameraShakeInfo info, float scale = 1f)
        {
            shake = info;
            target = new Vector3(RandomTarget(info.pitch), RandomTarget(info.yaw), RandomTarget(info.roll)) * scale;
            playBack = 0f;
            active = info.shakeCurve.IsValid;
        }

        public void Play(CameraShakeProfile profile, float scale = 1f) { if (profile) Play(profile.shake, scale); }

        public Quaternion Update(float dt)
        {
            Vector3 c = Vector3.zero;
            if (active)
            {
                playBack = Mathf.Min(playBack + shake.playRate * dt, shake.shakeCurve.LastTime);
                c = shake.shakeCurve.Evaluate(playBack);
                if (playBack >= shake.shakeCurve.LastTime) active = false;
            }
            float s = shake.smoothSpeed > 0f ? shake.smoothSpeed : 20f;
            output = AnimMath.Damp(output, Vector3.Scale(c, target), s, dt);
            return Quaternion.Euler(output);
        }

        /// <summary>Updates and adds the shake to t's rotation.</summary>
        public void Apply(Transform t, float dt) => t.rotation *= Update(dt);
    }
}
