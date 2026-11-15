using System;
using UnityEngine;

namespace FPS.Animation
{
    // =====================================================================================================
    // Basic value types of the animation framework (our replacement for KINEMATION's Core/Types).
    // =====================================================================================================

    /// <summary>Position + rotation pair, the basic currency of the layers.</summary>
    [Serializable]
    public struct LocRot
    {
        public Vector3 position;
        public Quaternion rotation;

        public static LocRot Identity => new LocRot(Vector3.zero, Quaternion.identity);

        public LocRot(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public LocRot(Vector3 position, Vector3 euler)
        {
            this.position = position;
            rotation = Quaternion.Euler(euler);
        }

        public LocRot(Transform t, bool world = true)
        {
            if (world) { position = t.position; rotation = t.rotation; }
            else { position = t.localPosition; rotation = t.localRotation; }
        }

        /// <summary>The rotation, never the invalid all-zero quaternion of a default / serialized struct.</summary>
        public Quaternion Rot => AnimMath.Valid(rotation);

        /// <summary>Local (in space) → world.</summary>
        public LocRot FromSpace(Transform space)
            => space ? new LocRot(space.TransformPoint(position), space.rotation * Rot) : this;

        /// <summary>World → local (in space).</summary>
        public LocRot ToSpace(Transform space)
            => space ? new LocRot(space.InverseTransformPoint(position), Quaternion.Inverse(space.rotation) * Rot) : this;

        public void ApplyWorld(Transform t) => t.SetPositionAndRotation(position, Rot);
        public void ApplyLocal(Transform t) { t.localPosition = position; t.localRotation = Rot; }

        public static LocRot Lerp(LocRot a, LocRot b, float t)
            => new LocRot(Vector3.Lerp(a.position, b.position, t), Quaternion.Slerp(a.Rot, b.Rot, t));

        /// <summary>Frame-rate independent exponential approach.</summary>
        public static LocRot Damp(LocRot a, LocRot b, float speed, float dt) => Lerp(a, b, AnimMath.DampAlpha(speed, dt));

        public override string ToString() => $"({position:F3}, {Rot.eulerAngles:F1})";
    }

    // ----------------------------------------------------------------------------------------------- springs

    /// <summary>
    /// Damped spring. stiffness pulls toward the target (1/s²), damping is the ratio (1 = critically damped,
    /// below 1 overshoots). Integrated in sub-steps, so the result does not depend on the frame rate.
    /// </summary>
    [Serializable]
    public struct SpringSettings
    {
        [Min(0f)] public float stiffness;
        [Range(0f, 2f)] public float damping;
        [Tooltip("Time scale of the simulation (1 = real time)")] public float speed;
        [Tooltip("Clamp of the target value (0 = no clamp)")] public float maxValue;

        public SpringSettings(float stiffness, float damping, float speed = 1f, float maxValue = 0f)
        {
            this.stiffness = stiffness;
            this.damping = damping;
            this.speed = speed;
            this.maxValue = maxValue;
        }
    }

    public struct SpringState
    {
        public float value, velocity;
        public void Reset() { value = 0f; velocity = 0f; }
    }

    [Serializable]
    public struct VectorSpring
    {
        public SpringSettings x, y, z;
        [Tooltip("Multiplier of the target per axis")] public Vector3 scale;

        public VectorSpring(float stiffness, float damping, float speed = 1f)
        {
            x = y = z = new SpringSettings(stiffness, damping, speed);
            scale = Vector3.one;
        }

        public VectorSpring(float stiffness, float damping, Vector3 scale)
        {
            x = y = z = new SpringSettings(stiffness, damping, 1f);
            this.scale = scale;
        }
    }

    public struct VectorSpringState
    {
        public SpringState x, y, z;
        public Vector3 Value => new Vector3(x.value, y.value, z.value);
        public void Reset() { x.Reset(); y.Reset(); z.Reset(); }
    }

    [Serializable]
    public struct LocRotSpring
    {
        public VectorSpring loc;
        public VectorSpring rot;

        public LocRotSpring(float stiffness, float damping)
        {
            loc = rot = new VectorSpring(stiffness, damping);
        }
    }

    // ----------------------------------------------------------------------------------------------- curves

    /// <summary>Three animation curves evaluated together.</summary>
    [Serializable]
    public struct VectorCurve
    {
        public AnimationCurve x, y, z;

        public VectorCurve(params Keyframe[] keys)
        {
            x = new AnimationCurve(keys);
            y = new AnimationCurve(keys);
            z = new AnimationCurve(keys);
        }

        public VectorCurve(AnimationCurve x, AnimationCurve y, AnimationCurve z)
        {
            this.x = x; this.y = y; this.z = z;
        }

        public bool IsValid => x != null && y != null && z != null && x.length > 0 && y.length > 0 && z.length > 0;
        public Vector3 Evaluate(float t) => IsValid ? new Vector3(x.Evaluate(t), y.Evaluate(t), z.Evaluate(t)) : Vector3.zero;
        public float LastTime => Mathf.Max(Last(x), Mathf.Max(Last(y), Last(z)));
        static float Last(AnimationCurve c) => c != null && c.length > 0 ? c[c.length - 1].time : 0f;
    }

    public enum EaseFunc { Linear, Sine, Cubic, Custom }

    [Serializable]
    public struct EaseMode
    {
        public EaseFunc func;
        public AnimationCurve curve;

        public EaseMode(EaseFunc func) { this.func = func; curve = null; }

        public float Apply(float t)
        {
            t = Mathf.Clamp01(t);
            switch (func)
            {
                case EaseFunc.Sine: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;
                case EaseFunc.Cubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) * 0.5f;
                case EaseFunc.Custom: return curve != null && curve.length > 0 ? curve.Evaluate(t) : t;
                default: return t;
            }
        }
    }

    /// <summary>Blend in / out time in seconds.</summary>
    [Serializable]
    public struct BlendTime
    {
        [Min(0f)] public float blendIn;
        [Min(0f)] public float blendOut;
        public BlendTime(float blendIn, float blendOut) { this.blendIn = blendIn; this.blendOut = blendOut; }
    }

    /// <summary>A named float curve played with a slot animation (normalized time 0..1) — layers read it as a weight.</summary>
    [Serializable]
    public struct AnimCurveBinding
    {
        public string name;
        public AnimationCurve curve;
        public AnimCurveBinding(string name, AnimationCurve curve) { this.name = name; this.curve = curve; }
    }

    /// <summary>Well-known curve names the built-in layers listen to.</summary>
    public static class AnimCurves
    {
        /// <summary>1 = the left hand leaves the weapon (LeftHandIKLayer / grip IK off).</summary>
        public const string MaskLeftHand = "MaskLeftHand";
        /// <summary>1 = the look layer (spine aim) is off.</summary>
        public const string MaskLookLayer = "MaskLookLayer";
        /// <summary>Weapon follows the right (+1) or left (−1) hand instead of the weapon bone.</summary>
        public const string WeaponBone = "WeaponBone";
        /// <summary>1 = the overlay pose is replaced (locomotion layer fades out).</summary>
        public const string Overlay = "Overlay";
        /// <summary>1 = the right hand leaves the weapon.</summary>
        public const string MaskRightHand = "MaskRightHand";
    }

    // ----------------------------------------------------------------------------------------------- input

    /// <summary>Per-frame input of the layers, written by the controller (KINEMATION's CharAnimData + extras).</summary>
    public struct CharAnimData
    {
        /// <summary>Look delta this frame in degrees (x = yaw right, y = pitch up).</summary>
        public Vector2 deltaAimInput;
        /// <summary>Accumulated aim in degrees (x = yaw offset of the upper body, y = pitch, + = up).</summary>
        public Vector2 totalAimInput;
        /// <summary>Movement input −1..1 (x = strafe, y = forward).</summary>
        public Vector2 moveInput;
        /// <summary>Velocity in character space (m/s).</summary>
        public Vector3 velocity;
        /// <summary>−1..1 lean (Q / E), continuous.</summary>
        public float lean;
        /// <summary>The recoil animation output (applied by RecoilLayer).</summary>
        public LocRot recoilAnim;
        public bool grounded;
        /// <summary>0..1 aiming down sights.</summary>
        public float aim;
        /// <summary>0..1 sprinting.</summary>
        public float sprint;

        public void AddAimInput(Vector2 delta)
        {
            deltaAimInput = delta;
            totalAimInput += delta;
            totalAimInput.x = Mathf.Clamp(totalAimInput.x, -90f, 90f);
            totalAimInput.y = Mathf.Clamp(totalAimInput.y, -90f, 90f);
        }

        public void SetAimInput(Vector2 aim)
        {
            deltaAimInput = aim - totalAimInput;
            totalAimInput = new Vector2(Mathf.Clamp(aim.x, -90f, 90f), Mathf.Clamp(aim.y, -90f, 90f));
        }
    }

    // ----------------------------------------------------------------------------------------------- weapon data

    [Serializable]
    public struct FreeAimData
    {
        [Tooltip("Degrees of dead-zone rotation per degree of look input")] public float scalar;
        public float maxValue;
        public float speed;
    }

    [Serializable]
    public struct MoveSwayData
    {
        [Tooltip("Offset at full move input (x = strafe, y/z = forward)")] public Vector3 maxMoveLocSway;
        [Tooltip("Rotation at full move input (degrees)")] public Vector3 maxMoveRotSway;
        public VectorSpring moveLocSway;
        public VectorSpring moveRotSway;
        public Vector3 locSpeed;
        public Vector3 rotSpeed;
    }

    /// <summary>Wall collision: the gun is traced along its length and pulled back / lowered when blocked.</summary>
    [Serializable]
    public struct GunBlockData
    {
        public float weaponLength;
        public float startOffset;
        public float threshold;
        public LocRot restPose;

        public GunBlockData(LocRot restPose)
        {
            this.restPose = restPose;
            weaponLength = 0.6f;
            startOffset = 0.1f;
            threshold = 0.2f;
        }
    }

    /// <summary>Aiming: speeds and offsets.</summary>
    [Serializable]
    public struct AdsData
    {
        [Tooltip("1 / seconds to aim in")] public float aimSpeed;
        [Tooltip("Speed of the sight change interpolation")] public float changeSightSpeed;
        [Tooltip("1 / seconds of point aiming")] public float pointAimSpeed;
        [Tooltip("Point aim (canted) pose in root space")] public LocRot pointAimOffset;
        [Tooltip("Additive aim pose correction (pivot space)")] public LocRot additiveAim;

        public AdsData(float speed)
        {
            aimSpeed = changeSightSpeed = pointAimSpeed = speed;
            pointAimOffset = LocRot.Identity;
            additiveAim = LocRot.Identity;
        }
    }

    /// <summary>Weapon references the layers need (pivot of the procedural rotations, sight, support-hand grip).</summary>
    [Serializable]
    public struct WeaponTransforms
    {
        public Transform pivotPoint;
        public Transform aimPoint;
        public Transform leftHandTarget;
        public Transform rightHandTarget;
    }
}
