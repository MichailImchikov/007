using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// The sniper's recon: for a few seconds the enemies within range show through walls as red silhouettes (the
    /// FPS_ReconXRay shader draws only where they are hidden) for the local player's camera, with a short pulse at the
    /// start. Like the drone's thermal view it draws the body's primitive parts with Graphics.RenderMesh (they follow the
    /// pose even under a skinned model), no weapons. Purely local: positions are already replicated.
    /// </summary>
    public class ReconVision : MonoBehaviour
    {
        static ReconVision inst;
        Material mat;
        float until, total, range;
        Combatant viewer;
        readonly Dictionary<Combatant, MeshFilter[]> cache = new Dictionary<Combatant, MeshFilter[]>();
        float cacheT;

        /// <summary>Seconds of recon left (0 = off).</summary>
        public static float Left => inst ? Mathf.Max(0f, inst.until - Time.time) : 0f;
        public static float Fraction => inst && inst.total > 0f ? Left / inst.total : 0f;
        /// <summary>Reach of the running recon (metres from the viewer), and who sees it.</summary>
        public static float Range => inst ? inst.range : 0f;
        public static Combatant Viewer => inst ? inst.viewer : null;

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
            inst.cache.Clear();
            ProcAudio.Play(Sfx.Switch, 0.6f);
        }

        public static void Stop() { if (inst) inst.until = 0f; }

        void Awake()
        {
            var sh = Resources.Load<Shader>("FPS_ReconXRay");
            if (!sh) sh = Shader.Find("Hidden/FPS/ReconXRay");
            if (sh) mat = new Material(sh) { name = "ReconXRay" };
            else Debug.LogWarning("[ReconVision] FPS_ReconXRay shader not found");
        }

        void OnDestroy() { if (inst == this) inst = null; if (mat) Destroy(mat); }

        void LateUpdate()
        {
            float left = until - Time.time;
            if (left <= 0f || !mat || !viewer) return;
            var cam = Camera.main;
            if (!cam) return;
            cacheT -= Time.deltaTime;
            if (cacheT <= 0f) { cacheT = 1f; cache.Clear(); }
            // a pulse when it starts, a fade over the last second
            float age = total - left;
            mat.SetFloat("_Pulse", Mathf.Clamp01(1f - age * 2f));
            Color c = new Color(1f, 0.16f, 0.12f, 0.55f * Mathf.Clamp01(left));
            mat.SetColor("_Color", c);
            Vector3 eye = cam.transform.position;
            var rp = new RenderParams(mat) { camera = cam, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
            foreach (var e in Combatant.All)
            {
                if (!e || !e.alive || e == viewer || !viewer.IsEnemyOf(e)) continue;
                if ((e.Chest - eye).sqrMagnitude > range * range) continue;
                if (!cache.TryGetValue(e, out var mfs)) cache[e] = mfs = Collect(e);
                for (int i = 0; i < mfs.Length; i++)
                {
                    var mf = mfs[i];
                    if (!mf || !mf.gameObject.activeInHierarchy) continue;
                    var mesh = mf.sharedMesh;
                    if (!mesh) continue;
                    Matrix4x4 m = mf.transform.localToWorldMatrix;
                    for (int s = 0; s < mesh.subMeshCount; s++) Graphics.RenderMesh(rp, mesh, s, m);
                }
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
