using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// The sniper's recon: for a few seconds the enemies within range show through walls as red silhouettes (the
    /// FPS_ReconXRay shader draws only where they are hidden) for the local player's camera. Not a live feed: a ping once
    /// a second (<see cref="PingInterval"/>) takes a snapshot of where every enemy stands and how (each body part's pose),
    /// and the silhouettes stay there, dimming a little, until the next ping. The minimap marks the same pinged spots.
    /// Drawn for the player's camera and, while aiming through a magnified optic, for its eyepiece camera too, so they
    /// show magnified in the scope's picture where they really are (the eyepiece glass masks the main view's copies,
    /// see OpticLens). Like the drone's thermal view it draws the body's primitive parts with Graphics.RenderMesh (they
    /// follow the pose even under a skinned model), no weapons. Purely local: positions are already replicated.
    /// </summary>
    [DefaultExecutionOrder(310)] // after OpticSight (300) turned the eyepiece camera on or off for this frame
    public class ReconVision : MonoBehaviour
    {
        /// <summary>Seconds between two snapshots of the enemies.</summary>
        public const float PingInterval = 1f;

        static ReconVision inst;
        Material mat;
        float until, total, range;
        Combatant viewer;

        struct Part { public Mesh mesh; public Matrix4x4 matrix; }
        readonly List<Part> parts = new List<Part>();
        readonly Dictionary<Combatant, Vector3> pinged = new Dictionary<Combatant, Vector3>();
        float nextPing, lastPing;

        /// <summary>Seconds of recon left (0 = off).</summary>
        public static float Left => inst ? Mathf.Max(0f, inst.until - Time.time) : 0f;
        public static float Fraction => inst && inst.total > 0f ? Left / inst.total : 0f;
        /// <summary>Reach of the running recon (metres from the viewer), and who sees it.</summary>
        public static float Range => inst ? inst.range : 0f;
        public static Combatant Viewer => inst ? inst.viewer : null;

        /// <summary>Where the last ping saw this enemy (the minimap marks it there until the next ping).</summary>
        public static bool TryGetPing(Combatant c, out Vector3 pos)
        {
            pos = default;
            return inst && c && Left > 0f && inst.pinged.TryGetValue(c, out pos);
        }

        public static void Show(float seconds, float maxRange, Combatant who)
        {
            if (!inst)
            {
                var go = new GameObject("ReconVision");
                inst = go.AddComponent<ReconVision>();
            }
            inst.until = Time.time + seconds;
            inst.total = seconds;
            inst.range = maxRange;
            inst.viewer = who;
            inst.nextPing = 0f; // the first ping at once
            ProcAudio.Play(Sfx.Switch, 0.6f);
        }

        public static void Stop()
        {
            if (!inst) return;
            inst.until = 0f;
            inst.Clear();
        }

        void Awake()
        {
            var sh = Resources.Load<Shader>("FPS_ReconXRay");
            if (!sh) sh = Shader.Find("Hidden/FPS/ReconXRay");
            if (sh) mat = new Material(sh) { name = "ReconXRay" };
            else Debug.LogWarning("[ReconVision] FPS_ReconXRay shader not found");
        }

        void OnDestroy() { if (inst == this) inst = null; if (mat) Destroy(mat); }

        void Clear()
        {
            parts.Clear();
            pinged.Clear();
        }

        void LateUpdate()
        {
            float left = until - Time.time;
            if (left <= 0f || !mat || !viewer)
            {
                if (parts.Count > 0 || pinged.Count > 0) Clear();
                return;
            }
            var cam = Camera.main;
            if (!cam) return;
            if (Time.time >= nextPing) Ping(cam.transform.position);

            // a pulse with every ping; the snapshot dims as it ages, the whole effect fades over its last second
            float age = Time.time - lastPing;
            mat.SetFloat("_Pulse", Mathf.Clamp01(1f - age * 2.5f));
            float fresh = Mathf.Lerp(1f, 0.6f, Mathf.Clamp01(age / PingInterval));
            mat.SetColor("_Color", new Color(1f, 0.16f, 0.12f, 0.55f * fresh * Mathf.Clamp01(left)));
            Draw(cam);
            // looking through a magnified optic: the eyepiece camera draws them in the scope's picture
            var pip = OpticSight.ActivePip;
            if (pip) Draw(pip);
        }

        /// <summary>Snapshot of the enemies in range: their body parts where they are right now.</summary>
        void Ping(Vector3 eye)
        {
            lastPing = Time.time;
            nextPing = Time.time + PingInterval;
            Clear();
            foreach (var e in Combatant.All)
            {
                if (!e || !e.alive || e == viewer || !viewer.IsEnemyOf(e)) continue;
                if ((e.Chest - eye).sqrMagnitude > range * range) continue;
                pinged[e] = e.transform.position;
                foreach (var mf in Collect(e))
                {
                    if (!mf || !mf.gameObject.activeInHierarchy) continue;
                    var mesh = mf.sharedMesh;
                    if (!mesh) continue;
                    parts.Add(new Part { mesh = mesh, matrix = mf.transform.localToWorldMatrix });
                }
            }
        }

        void Draw(Camera c)
        {
            if (parts.Count == 0) return;
            var rp = new RenderParams(mat) { camera = c, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
            foreach (var p in parts)
            {
                if (!p.mesh) continue;
                for (int s = 0; s < p.mesh.subMeshCount; s++) Graphics.RenderMesh(rp, p.mesh, s, p.matrix);
            }
        }

        static MeshFilter[] Collect(Combatant c)
        {
            var rig = c.GetComponentInChildren<HumanoidRig>();
            Transform root = rig ? rig.transform : c.transform;
            var list = new List<MeshFilter>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                if (rig && rig.weaponSocket && mf.transform.IsChildOf(rig.weaponSocket)) continue;
                list.Add(mf);
            }
            return list.ToArray();
        }
    }
}
