using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Math of the animation framework: frame-rate independent damping, bone-space offsets, springs and the
    /// two-bone IK solver (adapted from Unity's Animation Rigging TwoBoneIK, working on explicit bones so twist
    /// bones between the forearm and the hand are fine).
    /// </summary>
    public static class AnimMath
    {
        const float SqrEpsilon = 1e-8f;

        // ------------------------------------------------------------------ damping

        /// <summary>Interpolation alpha of an exponential approach (speed = 1/s). speed &lt;= 0 → 1 (instant).</summary>
        public static float DampAlpha(float speed, float dt) => speed <= 0f ? 1f : 1f - Mathf.Exp(-speed * dt);

        public static float Damp(float a, float b, float speed, float dt) => Mathf.Lerp(a, b, DampAlpha(speed, dt));
        public static Vector2 Damp(Vector2 a, Vector2 b, float speed, float dt) => Vector2.Lerp(a, b, DampAlpha(speed, dt));
        public static Vector3 Damp(Vector3 a, Vector3 b, float speed, float dt) => Vector3.Lerp(a, b, DampAlpha(speed, dt));
        public static Quaternion Damp(Quaternion a, Quaternion b, float speed, float dt) => Quaternion.Slerp(Valid(a), Valid(b), DampAlpha(speed, dt));

        public static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Never the all-zero quaternion (default structs, unset serialized fields).</summary>
        public static Quaternion Valid(Quaternion q)
        {
            float d = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (d < 1e-10f) return Quaternion.identity;
            if (Mathf.Abs(d - 1f) > 1e-3f) { float r = 1f / Mathf.Sqrt(d); return new Quaternion(q.x * r, q.y * r, q.z * r, q.w * r); }
            return q;
        }

        // ------------------------------------------------------------------ bone-space helpers

        /// <summary>Rotates bone by rotation expressed in space (world rotation of the space), blended by alpha.</summary>
        public static void RotateInSpace(Quaternion space, Transform bone, Quaternion rotation, float alpha)
        {
            if (alpha <= 0f) return;
            Quaternion boneRot = bone.rotation;
            Quaternion outRot = space * Valid(rotation) * (Quaternion.Inverse(space) * boneRot);
            bone.rotation = alpha >= 1f ? outRot : Quaternion.Slerp(boneRot, outRot, alpha);
        }

        /// <summary>Same as RotateInSpace, returns the result.</summary>
        public static Quaternion RotateInSpace(Quaternion space, Quaternion original, Quaternion offset, float alpha)
        {
            Quaternion outRot = space * Valid(offset) * (Quaternion.Inverse(space) * original);
            return Quaternion.Slerp(original, outRot, alpha);
        }

        /// <summary>Moves bone by offset (meters) along the axes of space (rotation only, scale ignored), blended by alpha.</summary>
        public static void MoveInSpace(Transform space, Transform bone, Vector3 offset, float alpha)
        {
            if (alpha <= 0f || offset.sqrMagnitude < 1e-14f) return;
            bone.position += space.rotation * offset * alpha;
        }

        public static void MoveInSpace(Quaternion space, Transform bone, Vector3 offset, float alpha)
        {
            if (alpha <= 0f || offset.sqrMagnitude < 1e-14f) return;
            bone.position += space * offset * alpha;
        }

        /// <summary>Applies a LocRot offset to a bone in the given space (move then rotate).</summary>
        public static void OffsetInSpace(Transform space, Transform bone, LocRot offset, float alpha)
        {
            MoveInSpace(space, bone, offset.position, alpha);
            RotateInSpace(space.rotation, bone, offset.Rot, alpha);
        }

        // ------------------------------------------------------------------ springs

        public static float SpringStep(ref SpringState s, float target, in SpringSettings p, float dt)
        {
            if (p.maxValue > 0f) target = Mathf.Clamp(target, -p.maxValue, p.maxValue);
            float t = dt * (p.speed > 0f ? p.speed : 1f);
            if (t <= 0f) return s.value;
            if (p.stiffness <= 0f) { s.value = target; s.velocity = 0f; return target; }
            float k = p.stiffness, c = 2f * Mathf.Sqrt(k) * p.damping;
            int n = Mathf.Clamp(Mathf.CeilToInt(t * 240f), 1, 40);
            float h = t / n;
            for (int i = 0; i < n; i++)
            {
                float a = (target - s.value) * k - s.velocity * c;
                s.velocity += a * h;
                s.value += s.velocity * h;
            }
            return s.value;
        }

        public static Vector3 SpringStep(ref VectorSpringState s, Vector3 target, in VectorSpring p, float dt)
        {
            Vector3 sc = p.scale == Vector3.zero ? Vector3.one : p.scale;
            return new Vector3(SpringStep(ref s.x, target.x * sc.x, p.x, dt),
                               SpringStep(ref s.y, target.y * sc.y, p.y, dt),
                               SpringStep(ref s.z, target.z * sc.z, p.z, dt));
        }

        /// <summary>Kicks the spring (adds velocity) — impulse-style motion (landing, shots).</summary>
        public static void SpringKick(ref VectorSpringState s, Vector3 velocity)
        {
            s.x.velocity += velocity.x; s.y.velocity += velocity.y; s.z.velocity += velocity.z;
        }

        // ------------------------------------------------------------------ IK

        /// <summary>
        /// Two-bone IK on explicit bones (root → mid → tip). The bend plane is the one already in the pose (minimal
        /// change), then turned toward the hint. tip gets targetRot by rotWeight.
        /// </summary>
        public static void SolveTwoBoneIK(Transform root, Transform mid, Transform tip, Vector3 targetPos, Quaternion targetRot,
                                          Vector3 hintPos, float posWeight, float rotWeight, float hintWeight, bool useHint,
                                          float maxReach = 0.9995f)
        {
            if (!root || !mid || !tip) return;
            if (posWeight <= 0f && rotWeight <= 0f) return;
            Vector3 aPosition = root.position, bPosition = mid.position, cPosition = tip.position;
            Vector3 tPosition = Vector3.Lerp(cPosition, targetPos, posWeight);
            Quaternion tRotation = Quaternion.Slerp(tip.rotation, Valid(targetRot), rotWeight);
            bool hasHint = useHint && hintWeight > 0f;

            Vector3 ab = bPosition - aPosition, bc = cPosition - bPosition, ac = cPosition - aPosition, at = tPosition - aPosition;
            float abLen = ab.magnitude, bcLen = bc.magnitude, acLen = ac.magnitude, atLen = at.magnitude;
            // keep the target inside the reach: a fully straight limb flips its bend direction
            float reach = (abLen + bcLen) * maxReach;
            if (atLen > reach) { at = at / atLen * reach; atLen = reach; tPosition = aPosition + at; }

            float oldAbc = TriangleAngle(acLen, abLen, bcLen);
            float newAbc = TriangleAngle(atLen, abLen, bcLen);

            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < SqrEpsilon)
            {
                axis = hasHint ? Vector3.Cross(hintPos - aPosition, bc) : Vector3.zero;
                if (axis.sqrMagnitude < SqrEpsilon) axis = Vector3.Cross(at, bc);
                if (axis.sqrMagnitude < SqrEpsilon) axis = Vector3.up;
            }
            axis.Normalize();

            float a = 0.5f * (oldAbc - newAbc);
            float sin = Mathf.Sin(a), cos = Mathf.Cos(a);
            var deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);
            mid.rotation = deltaR * mid.rotation;

            cPosition = tip.position;
            ac = cPosition - aPosition;
            root.rotation = FromToRotation(ac, at) * root.rotation;

            if (hasHint)
            {
                float acSqr = ac.sqrMagnitude;
                if (acSqr > 0f)
                {
                    bPosition = mid.position;
                    cPosition = tip.position;
                    ab = bPosition - aPosition;
                    ac = cPosition - aPosition;
                    Vector3 acNorm = ac / Mathf.Sqrt(acSqr);
                    Vector3 ah = hintPos - aPosition;
                    Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);
                    float maxR = abLen + bcLen;
                    if (abProj.sqrMagnitude > maxR * maxR * 0.001f && ahProj.sqrMagnitude > 0f)
                    {
                        Quaternion hintR = FromToRotation(abProj, ahProj);
                        hintR.x *= hintWeight; hintR.y *= hintWeight; hintR.z *= hintWeight;
                        root.rotation = Valid(hintR) * root.rotation;
                    }
                }
            }

            tip.rotation = tRotation;
        }

        static float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (aLen1 * aLen2) / 2f, -1f, 1f);
            return Mathf.Acos(c);
        }

        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            float theta = Vector3.Dot(from.normalized, to.normalized);
            if (theta >= 1f) return Quaternion.identity;
            if (theta <= -1f)
            {
                Vector3 axis = Vector3.Cross(from, Vector3.right);
                if (axis.sqrMagnitude == 0f) axis = Vector3.Cross(from, Vector3.up);
                return Quaternion.AngleAxis(180f, axis);
            }
            return Quaternion.AngleAxis(Mathf.Acos(theta) * Mathf.Rad2Deg, Vector3.Cross(from, to).normalized);
        }

        /// <summary>Twist of q around axis (swing-twist decomposition), returned as a rotation about axis.</summary>
        public static Quaternion Twist(Quaternion q, Vector3 axis)
        {
            Vector3 r = new Vector3(q.x, q.y, q.z);
            Vector3 p = Vector3.Project(r, axis);
            var twist = new Quaternion(p.x, p.y, p.z, q.w);
            float m = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (m < 1e-6f) return Quaternion.identity;
            twist.x /= m; twist.y /= m; twist.z /= m; twist.w /= m;
            return twist;
        }

        /// <summary>Signed angle (degrees) of the twist of q around axis.</summary>
        public static float TwistAngle(Quaternion q, Vector3 axis)
        {
            var t = Twist(q, axis);
            t.ToAngleAxis(out float ang, out Vector3 ax);
            if (ang > 180f) ang -= 360f;
            return Vector3.Dot(ax, axis) < 0f ? -ang : ang;
        }
    }
}
