using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPS
{
    /// <summary>
    /// Image of the local pilot's FPV camera: an analog video look while flying and the thermal sight
    /// (white-hot, cold world, thin fog). In thermal, characters and enemy drones glow; within
    /// <see cref="XRayRange"/> they also show through walls (warm tint), farther away only in plain sight.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class DroneThermal : MonoBehaviour
    {
        public const float XRayRange = 45f, VisibleRange = 230f;

        Volume fpvVol, thermVol;
        Material matFull, matVisible;
        float thermW, fpvW;
        bool fogSaved;
        float fogDensity, fogEnd;
        readonly Dictionary<Combatant, MeshFilter[]> cache = new Dictionary<Combatant, MeshFilter[]>();
        float cacheT;

        void Awake()
        {
            fpvVol = MakeVolume("FPV Video", 150, p =>
            {
                var ld = p.Add<LensDistortion>(true); ld.intensity.Override(-0.28f); ld.scale.Override(0.94f);
                var ca = p.Add<ChromaticAberration>(true); ca.intensity.Override(0.35f);
                var vg = p.Add<Vignette>(true); vg.intensity.Override(0.22f); vg.smoothness.Override(0.5f);
                var fg = p.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Medium4); fg.intensity.Override(0.35f); fg.response.Override(0.8f);
                var adj = p.Add<ColorAdjustments>(true); adj.saturation.Override(-8f); adj.contrast.Override(8f); adj.postExposure.Override(0.45f);
            });
            thermVol = MakeVolume("Thermal", 160, p =>
            {
                var adj = p.Add<ColorAdjustments>(true);
                adj.saturation.Override(-100f); adj.contrast.Override(30f); adj.postExposure.Override(0.35f); adj.colorFilter.Override(new Color(0.78f, 0.82f, 0.86f));
                var bl = p.Add<Bloom>(true); bl.intensity.Override(2.2f); bl.threshold.Override(1.05f); bl.scatter.Override(0.6f);
                var fg = p.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Thin2); fg.intensity.Override(0.75f);
            });

            var sh = Resources.Load<Shader>("FPS_ThermalXRay");
            if (!sh) sh = Shader.Find("Hidden/FPS/ThermalXRay");
            if (sh)
            {
                matFull = new Material(sh) { name = "ThermalFull" };
                matVisible = new Material(sh) { name = "ThermalVisible" };
                foreach (var m in new[] { matFull, matVisible }) { m.SetColor("_Hot", new Color(3f, 3f, 2.9f)); m.SetColor("_Edge", new Color(1.2f, 1.2f, 1.15f)); }
                matVisible.SetColor("_Hidden", Color.black); // additive black = the through-wall pass adds nothing
            }
            else Debug.LogWarning("[DroneThermal] FPS_ThermalXRay shader not found — thermal highlights disabled");
        }

        Volume MakeVolume(string name, int priority, System.Action<VolumeProfile> fill)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var v = go.AddComponent<Volume>();
            v.isGlobal = true;
            v.priority = priority;
            v.weight = 0f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            fill(prof);
            v.sharedProfile = prof;
            return v;
        }

        void OnDestroy() => RestoreFog();

        void LateUpdate()
        {
            var op = DroneOperator.Local;
            var d = op ? op.Drone : null;
            bool flying = d && d.IsLocalView && !d.Dead;
            bool thermal = flying && d.Thermal;
            float dt = Time.unscaledDeltaTime;
            fpvW = flying ? 1f : 0f;
            thermW = Mathf.MoveTowards(thermW, thermal ? 1f : 0f, dt * 6f);
            fpvVol.weight = fpvW;
            thermVol.weight = thermW;

            if (thermW > 0.5f) ThinFog(); else RestoreFog();
            if (!thermal || !matFull) return;

            cacheT -= dt;
            if (cacheT <= 0f) { cacheT = 1f; cache.Clear(); }

            Camera cam = d.Cam;
            Vector3 eye = cam.transform.position;
            var owner = d.Owner;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || c == owner) continue;
                float dist = Vector3.Distance(eye, c.Chest);
                if (dist > VisibleRange) continue;
                var mat = dist <= XRayRange ? matFull : matVisible;
                if (!cache.TryGetValue(c, out var mfs)) cache[c] = mfs = Collect(c);
                Draw(cam, mat, mfs);
            }
            foreach (var other in Drone.All)
            {
                if (!other || other == d || other.Dead) continue;
                float dist = Vector3.Distance(eye, other.transform.position);
                if (dist > VisibleRange) continue;
                Draw(cam, dist <= XRayRange ? matFull : matVisible, other.meshes);
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
                // the body only (primitive parts follow the pose even when a skinned model is shown); no guns
                if (rig && rig.weaponSocket && mf.transform.IsChildOf(rig.weaponSocket)) continue;
                list.Add(mf);
            }
            return list.ToArray();
        }

        static void Draw(Camera cam, Material mat, IList<MeshFilter> mfs)
        {
            var rp = new RenderParams(mat) { camera = cam, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = 0 };
            for (int i = 0; i < mfs.Count; i++)
            {
                var mf = mfs[i];
                if (!mf || !mf.gameObject.activeInHierarchy) continue;
                var mesh = mf.sharedMesh;
                if (!mesh) continue;
                Matrix4x4 m = mf.transform.localToWorldMatrix;
                for (int s = 0; s < mesh.subMeshCount; s++) Graphics.RenderMesh(rp, mesh, s, m);
            }
        }

        void ThinFog()
        {
            if (fogSaved || !RenderSettings.fog) return;
            fogSaved = true;
            fogDensity = RenderSettings.fogDensity;
            fogEnd = RenderSettings.fogEndDistance;
            RenderSettings.fogDensity = fogDensity * 0.25f;
            RenderSettings.fogEndDistance = fogEnd * 3f;
        }

        void RestoreFog()
        {
            if (!fogSaved) return;
            fogSaved = false;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogEndDistance = fogEnd;
        }
    }
}
