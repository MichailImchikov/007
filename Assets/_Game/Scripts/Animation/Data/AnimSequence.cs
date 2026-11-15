using System.Collections.Generic;
using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// A clip with its playback settings: overlay pose (static or looping hold pose) or a one-shot slot animation
    /// (reload, inspect, throw...). Curves (normalized time 0..1) drive layer masks while it plays (see AnimCurves).
    /// </summary>
    [CreateAssetMenu(fileName = "NewAnimSequence", menuName = "FPS/Animation/Anim Sequence")]
    public class AnimSequence : ScriptableObject
    {
        public AnimationClip clip;
        [Tooltip("Body part mask (empty = upper body)")] public AvatarMask mask;
        public BlendTime blendTime = new BlendTime(0.15f, 0.2f);
        [Tooltip("Playback speed")] public float playRate = 1f;
        [Tooltip("Overlay pose: sampled at this time (seconds)")] public float poseTime;
        [Tooltip("Overlay pose: keep playing (looping hold animation)")] public bool loop;
        [Tooltip("Applied to the spine root while the animation plays (degrees)")] public Vector3 spineEuler;
        public AnimCurveBinding[] curves = new AnimCurveBinding[0];

        public Quaternion spineRotation => Quaternion.Euler(spineEuler);

        public static AnimSequence Create(AnimationClip clip, float blendIn = 0.15f, float blendOut = 0.2f, AvatarMask mask = null)
        {
            var s = CreateInstance<AnimSequence>();
            s.name = clip ? clip.name : "AnimSequence";
            s.clip = clip;
            s.mask = mask;
            s.blendTime = new BlendTime(blendIn, blendOut);
            return s;
        }

        public AnimSequence WithCurve(string curveName, AnimationCurve curve)
        {
            var l = new List<AnimCurveBinding>(curves ?? new AnimCurveBinding[0]) { new AnimCurveBinding(curveName, curve) };
            curves = l.ToArray();
            return this;
        }
    }
}
