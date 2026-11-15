using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Foot placement (KINEMATION's LegIK, extended): each foot is traced down to the ground and its goal moved so the
    /// animated foot height is kept above the real ground (stairs, slopes, crates); the foot turns with the ground
    /// normal. Improvement: the pelvis drops when a foot has to reach lower ground, own colliders are ignored, and the
    /// layer fades out in the air.
    /// </summary>
    public class LegIKLayer : AnimLayer
    {
        [Header("Trace")]
        [Tooltip("How far above the foot the trace starts")] public float traceHeight = 0.5f;
        [Tooltip("How far below the animated foot the ground may be")] public float traceDepth = 0.45f;
        public LayerMask layerMask = Physics.DefaultRaycastLayers;
        [Tooltip("Height of the ankle above the sole in the animation's rest stance")] public float footHeight = 0.13f;

        [Header("Smoothing")]
        public float footInterpSpeed = 18f;
        public float pelvisInterpSpeed = 10f;
        [Tooltip("Max pelvis drop (m)")] public float maxPelvisDrop = 0.45f;
        [Tooltip("Max foot raise (m)")] public float maxFootRaise = 0.5f;
        [Tooltip("Foot rotation to the ground normal (0..1)")] [Range(0f, 1f)] public float alignToNormal = 0.85f;

        [Tooltip("Colliders under this transform are ignored (the character's own capsule / hit boxes)")]
        public Transform ignoreRoot;

        LocRot smoothR = LocRot.Identity, smoothL = LocRot.Identity;
        float smoothPelvis, groundWeight;
        Transform charRoot;
        static readonly RaycastHit[] hits = new RaycastHit[12];

        public override void OnAnimStart()
        {
            charRoot = ignoreRoot ? ignoreRoot : Animator ? Animator.transform.parent ? Animator.transform.parent : Animator.transform : transform;
        }

        public void SetIgnoreRoot(Transform root) { ignoreRoot = root; charRoot = root; }

        public override void OnAnimUpdate()
        {
            float dt = DeltaTime;
            groundWeight = AnimMath.Damp(groundWeight, CharData.grounded ? 1f : 0f, 12f, dt);
            float alpha = smoothLayerAlpha * groundWeight;
            if (alpha <= 0.001f) { smoothPelvis = AnimMath.Damp(smoothPelvis, 0f, pelvisInterpSpeed, dt); return; }

            Transform root = Animator ? Animator.transform : transform;
            Vector3 up = root.up;
            var tr = Trace(RightFootGoal, root, up, out float dr);
            var tl = Trace(LeftFootGoal, root, up, out float dl);
            smoothR = LocRot.Damp(smoothR, tr, footInterpSpeed, dt);
            smoothL = LocRot.Damp(smoothL, tl, footInterpSpeed, dt);

            // the pelvis goes down so the lower foot can reach its ground
            float drop = Mathf.Clamp(Mathf.Min(0f, Mathf.Min(dr, dl)), -maxPelvisDrop, 0f);
            smoothPelvis = AnimMath.Damp(smoothPelvis, drop, pelvisInterpSpeed, dt);
            if (Pelvis && Mathf.Abs(smoothPelvis) > 0.0005f) Pelvis.position += up * smoothPelvis * alpha;

            Apply(RightFootGoal, smoothR, alpha);
            Apply(LeftFootGoal, smoothL, alpha);
        }

        static void Apply(Transform goal, LocRot delta, float alpha)
        {
            goal.position += delta.position * alpha;
            goal.rotation = Quaternion.Slerp(Quaternion.identity, delta.Rot, alpha) * goal.rotation;
        }

        /// <summary>World delta (position, rotation) that moves the foot goal onto the ground. dy: vertical change.</summary>
        LocRot Trace(Transform foot, Transform root, Vector3 up, out float dy)
        {
            dy = 0f;
            // the animated foot height above the character's ground plane (lifted feet stay lifted)
            float animOffset = Vector3.Dot(foot.position - root.position, up);
            Vector3 origin = foot.position + up * (traceHeight - Mathf.Max(0f, animOffset - footHeight));
            float length = traceHeight + traceDepth;
            int n = Physics.RaycastNonAlloc(origin, -up, hits, length, layerMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; int bi = -1;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (!c || (charRoot && c.transform.IsChildOf(charRoot))) continue;
                if (hits[i].distance < best) { best = hits[i].distance; bi = i; }
            }
            if (bi < 0) return LocRot.Identity;
            var hit = hits[bi];
            float groundOffset = Vector3.Dot(hit.point - root.position, up);     // ground height vs the capsule's ground
            dy = Mathf.Clamp(groundOffset, -maxPelvisDrop, maxFootRaise);
            Quaternion align = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(up, hit.normal), alignToNormal);
            return new LocRot(up * dy, align);
        }
    }
}
