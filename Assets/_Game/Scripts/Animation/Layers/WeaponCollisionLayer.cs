using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Weapon vs walls (KINEMATION's WeaponCollision): a ray along the gun; when blocked, the gun slides back toward the
    /// shoulder, and once it can't, it goes to a raised / lowered rest pose. Improvement: a sphere cast (thin rails and
    /// door frames are caught), own colliders ignored, the result is reported (gameplay can block shooting).
    /// </summary>
    public class WeaponCollisionLayer : AnimLayer
    {
        public LayerMask layerMask = Physics.DefaultRaycastLayers;
        public float radius = 0.025f;
        public float smoothSpeed = 10f;

        protected Vector3 start, end;
        protected LocRot smoothPose = LocRot.Identity, offsetPose = LocRot.Identity;
        static readonly RaycastHit[] hits = new RaycastHit[8];
        Transform ignoreRoot;

        /// <summary>0..1 how much the weapon is blocked right now.</summary>
        public float Blocked { get; private set; }

        public void SetIgnoreRoot(Transform root) => ignoreRoot = root;

        protected void Trace()
        {
            var b = Weapon.block;
            Transform master = MasterPivot;
            start = master.position - master.forward * b.startOffset;
            end = start + master.forward * b.weaponLength;
            int n = Physics.SphereCastNonAlloc(start, radius, master.forward, hits, b.weaponLength, layerMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (!c || (ignoreRoot && c.transform.IsChildOf(ignoreRoot))) continue;
                // loose light things (casings, magazines, cans) do not stop a gun: they get pushed aside
                var rb = c.attachedRigidbody;
                if (rb && !rb.isKinematic && rb.mass < 3f) continue;
                if (hits[i].distance > 0f && hits[i].distance < best) best = hits[i].distance;
            }
            if (best == float.MaxValue) { offsetPose = LocRot.Identity; Blocked = 0f; return; }
            float distance = b.weaponLength - best;
            if (distance > b.threshold) { offsetPose = b.restPose; Blocked = 1f; }
            else
            {
                offsetPose = new LocRot(new Vector3(0f, 0f, -distance), Quaternion.Euler(0f, 0f, 15f * (distance / Mathf.Max(0.001f, b.threshold))));
                Blocked = distance / Mathf.Max(0.001f, b.threshold);
            }
        }

        public override void OnAnimUpdate()
        {
            Trace();
            smoothPose = LocRot.Damp(smoothPose, offsetPose, smoothSpeed, DeltaTime);
            AnimMath.MoveInSpace(MasterPivot, MasterPivot, smoothPose.position, smoothLayerAlpha);
            AnimMath.RotateInSpace(MasterPivot.rotation, MasterPivot, smoothPose.Rot, smoothLayerAlpha);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(start, end);
        }
    }
}
