using System;
using UnityEngine;

namespace FPS.Animation
{
    /// <summary>Curve-based camera shake (KINEMATION's CameraShakeInfo): a random target per axis scaled by the curve.</summary>
    [Serializable]
    public struct CameraShakeInfo
    {
        public VectorCurve shakeCurve;
        [Tooltip("(min A, max A, min B, max B): the target is random in A or in B")] public Vector4 pitch;
        public Vector4 yaw;
        public Vector4 roll;
        public float smoothSpeed;
        public float playRate;

        public CameraShakeInfo(Keyframe[] frames, float playRate, float smooth)
        {
            shakeCurve = new VectorCurve(frames);
            pitch = yaw = roll = Vector4.zero;
            smoothSpeed = smooth;
            this.playRate = playRate;
        }
    }

    [CreateAssetMenu(fileName = "NewCameraShake", menuName = "FPS/Animation/Camera Shake")]
    public class CameraShakeProfile : ScriptableObject
    {
        public CameraShakeInfo shake = new CameraShakeInfo(new[] { new Keyframe(0f, 0f), new Keyframe(0.04f, 1f), new Keyframe(0.22f, 0f) }, 1f, 22f);
    }
}
