using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Shoulders the weapon on the body: moves the master pivot (the weapon and both hand goals) so the top of the butt
    /// rests in the shoulder pocket. Used in true first person, where the weapon is otherwise placed in camera space and
    /// floated at eye height above the shoulder. Runs after the body alignment / leg IK and before the pose, aim, sway
    /// and recoil layers, which then work from the shouldered pose (aiming still brings the sight to the eye).
    /// </summary>
    public class StockMountLayer : AnimLayer
    {
        [Tooltip("Shoulder joint the butt rests against (the right upper arm bone)")]
        public Transform shoulder;
        [Tooltip("Where the top of the butt rests, relative to the shoulder joint, in the character's frame (x right, " +
                 "y up, z forward): the top of the shoulder, a little inside and in front of the joint")]
        public Vector3 pocketOffset = new Vector3(-0.03f, 0.05f, 0.04f);
        [Tooltip("Top of the butt in the weapon's space (set on equip)")]
        public Vector3 buttTop;
        [Tooltip("False for weapons without a stock (pistols): the layer does nothing")]
        public bool hasStock;
        [Tooltip("Smoothing of the correction (1/s), 0 = none: the gun follows the shoulder without its jitter")]
        public float smoothSpeed = 14f;

        Vector3 smoothLocal;
        bool started;

        public override void OnAnimUpdate()
        {
            if (!hasStock || !shoulder || smoothLayerAlpha <= 0.001f) { started = false; return; }
            Transform master = MasterPivot;
            Transform ch = Animator.transform;
            Vector3 pocket = shoulder.position + ch.rotation * pocketOffset;
            // where the butt ends up: the weapon bone is the master minus the pivot offset at the end of the frame
            Vector3 butt = master.position + master.rotation * (buttTop - PivotOffset);
            Vector3 delta = Quaternion.Inverse(ch.rotation) * (pocket - butt);
            smoothLocal = !started || smoothSpeed <= 0f ? delta : AnimMath.Damp(smoothLocal, delta, smoothSpeed, DeltaTime);
            started = true;
            master.position += ch.rotation * smoothLocal * smoothLayerAlpha;
        }

        /// <summary>
        /// Top of the butt of a weapon, in its own space (z forward, y up): a "Socket_Butt" object when the model has
        /// one, otherwise the rear-most point of its visible meshes at the bore height minus 2 cm (stocks are roughly in
        /// line with the bore). False when nothing reaches well behind the trigger hand (no stock: pistols).
        /// </summary>
        public static bool FindButtTop(Transform weapon, Transform model, Transform muzzle, Transform grip, out Vector3 top)
        {
            top = Vector3.zero;
            if (!weapon || !model) return false;
            var socket = FindDeep(model, "Socket_Butt");
            if (socket) { top = weapon.InverseTransformPoint(socket.position); return true; }

            float minZ = float.MaxValue;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || r.forceRenderingOff) continue;
                Bounds b;
                if (r is SkinnedMeshRenderer smr) b = smr.localBounds;
                else
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (!mf || !mf.sharedMesh) continue;
                    b = mf.sharedMesh.bounds;
                }
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float z = weapon.InverseTransformPoint(r.transform.TransformPoint(c)).z;
                    if (z < minZ) minZ = z;
                }
            }
            float gripZ = grip ? weapon.InverseTransformPoint(grip.position).z : 0f;
            if (minZ == float.MaxValue || minZ > gripZ - 0.1f) return false;
            float bore = muzzle ? weapon.InverseTransformPoint(muzzle.position).y : 0f;
            top = new Vector3(0f, bore - 0.02f, minZ);
            return true;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f) return f;
            }
            return null;
        }
    }
}
