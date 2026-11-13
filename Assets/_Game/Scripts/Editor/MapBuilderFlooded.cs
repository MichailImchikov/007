using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// "Затопленная деревня": Deathmatch on the Flooded Grounds Asset Store environment. The package's scene is
    /// copied to Assets/Scenes/Map_Flooded.unity, its own FPS controller / UI are removed, the playable village core
    /// is fenced with invisible walls, and the usual match stack (player, bots, MatchManager) is added.
    /// Spawn points are picked automatically from the baked NavMesh; the NavMesh itself is saved with the scene so
    /// the match does not have to bake ~300 m of terrain and 2000 mesh colliders on load.
    /// </summary>
    public static partial class RangeBuilder
    {
        const string FloodedSource = "Assets/Flooded_Grounds/Scenes/Scene_A.unity";
        const string FloodedScene = "Assets/Scenes/Map_Flooded.unity";

        // playable rectangle (world XZ) — the village between the package's own blocker fences
        static readonly Rect FloodedArea = Rect.MinMaxRect(400f, 352f, 708f, 605f);

        [MenuItem("Tools/FPS/Maps/Flooded Village")] static void M6() => BuildFlooded();

        static void BuildFlooded()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(FloodedSource)) { Debug.LogWarning("[FPS] Flooded Grounds pack not found — skipping Map_Flooded"); return; }
            Prepare();
            FloodedMaterials.FixAll();

            AssetDatabase.DeleteAsset(FloodedScene);
            AssetDatabase.CopyAsset(FloodedSource, FloodedScene);
            var scene = EditorSceneManager.OpenScene(FloodedScene, OpenSceneMode.Single);
            Lightmapping.lightingDataAsset = null; // the pack's Unity 5 bake does not apply

            // ---- strip the package's demo controller and UI (old Input Manager, legacy post-processing)
            foreach (var n in new[] { "FpsController", "FPSDisplay", "EventSystem" })
            {
                var go = GameObject.Find(n);
                if (go) Object.DestroyImmediate(go);
            }
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) Object.DestroyImmediate(cam.gameObject);

            FloodedTerrain();

            // blocker helpers: invisible from the start (the package hid them at runtime)
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.sharedMaterial && r.sharedMaterial.name.StartsWith("HLP_")) r.enabled = false;

            FloodedLighting();

            var game = new GameObject("_Deathmatch").transform;
            FloodedBounds(game);

            // ---- navigation: only the play area
            var nav = new GameObject("Navigation");
            nav.transform.SetParent(game, false);
            var surface = nav.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.center = new Vector3(FloodedArea.center.x, 30f, FloodedArea.center.y);
            surface.size = new Vector3(FloodedArea.width, 60f, FloodedArea.height);
            surface.BuildNavMesh();

            var spawns = FloodedSpawns(16);
            var spawnRoot = new GameObject("SpawnPoints").transform;
            spawnRoot.SetParent(game, false);
            foreach (var s in spawns)
            {
                var sp = new GameObject("Spawn").transform;
                sp.SetParent(spawnRoot, false);
                sp.position = s.pos;
                sp.rotation = Quaternion.Euler(0f, s.yaw, 0f);
                sp.gameObject.AddComponent<SpawnPoint>();
            }

            // keep the bake with the scene (MatchManager skips the runtime bake when data is present)
            string navPath = "Assets/Scenes/Map_Flooded_NavMesh.asset";
            AssetDatabase.DeleteAsset(navPath);
            AssetDatabase.CreateAsset(surface.navMeshData, navPath);

            BuildFXObject();
            var player = BuildPlayer();
            player.transform.position = spawns[0].pos + Vector3.up * 0.05f;
            player.transform.rotation = Quaternion.Euler(0f, spawns[0].yaw, 0f);

            var mmGO = new GameObject("MatchManager");
            var mm = mmGO.AddComponent<MatchManager>();
            mm.mapName = "Затопленная деревня";
            mm.navSurface = surface;
            mm.botTemplate = BuildBotTemplate(spawns[1].pos);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, FloodedScene);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPS] Map_Flooded built: {spawns.Count} spawns, nav area {FloodedArea.width:0}x{FloodedArea.height:0} m");
        }

        /// <summary>
        /// Tree Creator prefabs only render with the Built-in tree shaders (URP terrain refuses them: "no valid mesh
        /// renderer"). Plain mesh prefabs are made from each tree's generated mesh + optimized materials (now URP/Lit),
        /// with a trunk collider, and the map uses its own copy of the terrain data pointing at them.
        /// </summary>
        static void FloodedTerrain()
        {
            var terrain = Terrain.activeTerrain;
            if (!terrain) return;
            string treeDir = Root + "/FloodedTrees";
            EnsureFolder(treeDir);

            string dataPath = "Assets/Scenes/Map_Flooded_Terrain.asset";
            AssetDatabase.DeleteAsset(dataPath);
            AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(terrain.terrainData), dataPath);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);

            var protos = data.treePrototypes;
            for (int i = 0; i < protos.Length; i++)
            {
                var src = protos[i].prefab;
                if (!src || !src.GetComponent<Tree>()) continue;
                Mesh mesh = null;
                Material bark = null, leaf = null;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(src)))
                {
                    if (o is Mesh m) mesh = m;
                    else if (o is Material mat) { if (mat.name.Contains("Leaf")) leaf = mat; else bark = mat; }
                }
                if (!mesh) { Debug.LogWarning("[FPS] tree without mesh: " + src.name); continue; }

                var go = new GameObject(src.name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mesh.subMeshCount > 1 ? new[] { bark, leaf } : new[] { bark ? bark : leaf };
                if (!src.name.Contains("Bush"))
                {
                    var b = mesh.bounds;
                    var cap = go.AddComponent<CapsuleCollider>();
                    cap.radius = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * 0.035f, 0.15f, 0.45f);
                    cap.height = Mathf.Min(b.size.y * 0.6f, 8f);
                    cap.center = new Vector3(0f, cap.height * 0.5f, 0f);
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{treeDir}/{src.name}.prefab");
                Object.DestroyImmediate(go);
                protos[i].prefab = prefab;
            }
            data.treePrototypes = protos;
            data.RefreshPrototypes();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            terrain.terrainData = data;
            var tc = terrain.GetComponent<TerrainCollider>();
            if (tc) tc.terrainData = data;
            terrain.treeDistance = Mathf.Max(terrain.treeDistance, 350f);
            terrain.treeBillboardDistance = 400f; // no billboards for plain-mesh trees
        }

        static void FloodedLighting()
        {
            var sun = RenderSettings.sun;
            if (!sun) foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            if (sun)
            {
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.6f;
                sun.intensity = 1.9f;
                sun.color = new Color(0.97f, 0.96f, 0.92f);
                sun.lightmapBakeType = LightmapBakeType.Realtime;
                RenderSettings.sun = sun;
            }
            // decorative point lights were baked-only bounce lights: they cost a lot as realtime lights
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type != LightType.Directional && l.lightmapBakeType == LightmapBakeType.Baked) l.enabled = false;

            // no baked environment lighting → explicit overcast ambient
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.84f, 0.9f);
            RenderSettings.ambientEquatorColor = new Color(0.62f, 0.65f, 0.64f);
            RenderSettings.ambientGroundColor = new Color(0.36f, 0.36f, 0.33f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 15f;
            RenderSettings.fogEndDistance = 330f;
            RenderSettings.fogColor = new Color(0.49f, 0.55f, 0.59f);

            var probeGO = new GameObject("Reflection Probe");
            probeGO.transform.position = new Vector3(FloodedArea.center.x, 22f, FloodedArea.center.y);
            var probe = probeGO.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(FloodedArea.width + 20f, 60f, FloodedArea.height + 20f);
            probe.resolution = 128;

            var volGO = new GameObject("Global Volume");
            var vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            SaveAsset(profile, Root + "/Volume_Flooded.asset");
            var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.5f); bloom.threshold.Override(1.1f); bloom.scatter.Override(0.6f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.26f); vig.smoothness.Override(0.45f);
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(0.85f); ca.contrast.Override(12f); ca.saturation.Override(-6f);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true); smh.shadows.Override(new Vector4(0.95f, 1f, 1.05f, 0f));
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            vol.sharedProfile = profile;
        }

        /// <summary>Invisible walls around the play area (60 m high so nobody climbs out over roofs).</summary>
        static void FloodedBounds(Transform parent)
        {
            var root = new GameObject("Bounds").transform;
            root.SetParent(parent, false);
            var a = FloodedArea;
            const float h = 80f, t = 2f;
            void W(string n, Vector3 c, Vector3 s)
            {
                var go = new GameObject(n);
                go.transform.SetParent(root, false);
                go.transform.position = c;
                go.AddComponent<BoxCollider>().size = s;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
            }
            W("North", new Vector3(a.center.x, h * 0.5f, a.yMax + t * 0.5f), new Vector3(a.width + 2 * t, h, t));
            W("South", new Vector3(a.center.x, h * 0.5f, a.yMin - t * 0.5f), new Vector3(a.width + 2 * t, h, t));
            W("East", new Vector3(a.xMax + t * 0.5f, h * 0.5f, a.center.y), new Vector3(t, h, a.height));
            W("West", new Vector3(a.xMin - t * 0.5f, h * 0.5f, a.center.y), new Vector3(t, h, a.height));
        }

        /// <summary>
        /// Spawn candidates on a 4 m grid: on the NavMesh, at ground level (no roofs), above the water line,
        /// with headroom, connected to the main NavMesh region; then farthest-point selection.
        /// </summary>
        static List<(Vector3 pos, float yaw)> FloodedSpawns(int count)
        {
            Physics.SyncTransforms();
            var terrain = Terrain.activeTerrain;
            var cands = new List<Vector3>();
            var a = FloodedArea;
            for (float x = a.xMin + 10f; x <= a.xMax - 10f; x += 4f)
                for (float z = a.yMin + 10f; z <= a.yMax - 10f; z += 4f)
                {
                    float ty = terrain ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f;
                    if (!NavMesh.SamplePosition(new Vector3(x, ty + 0.5f, z), out var hit, 1.5f, NavMesh.AllAreas)) continue;
                    Vector3 p = hit.position;
                    if (p.y < 16.15f) continue;                       // under the flood water
                    if (terrain && p.y - ty > 1.2f) continue;        // roofs, piers, props
                    if (Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 1.7f, 0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue; // same test as MapCheck
                    cands.Add(p);
                }
            if (cands.Count == 0) throw new System.Exception("No spawn candidates on the Flooded NavMesh");

            // keep the largest connected region: path from the candidate nearest the centre
            Vector3 centre = new Vector3(a.center.x, 17f, a.center.y);
            cands.Sort((p, q) => (p - centre).sqrMagnitude.CompareTo((q - centre).sqrMagnitude));
            var path = new NavMeshPath();
            var ok = new List<Vector3>();
            foreach (var c in cands)
                if (NavMesh.CalculatePath(cands[0], c, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) ok.Add(c);

            // farthest-point sampling, seeded by the candidate nearest to the south-west corner
            var picked = new List<Vector3>();
            Vector3 sw = new Vector3(a.xMin, 0f, a.yMin);
            Vector3 first = ok[0];
            foreach (var c in ok) if ((c - sw).sqrMagnitude < (first - sw).sqrMagnitude) first = c;
            picked.Add(first);
            while (picked.Count < count && picked.Count < ok.Count)
            {
                Vector3 best = ok[0]; float bestD = -1f;
                foreach (var c in ok)
                {
                    float d = float.MaxValue;
                    foreach (var p in picked) d = Mathf.Min(d, (c - p).sqrMagnitude);
                    if (d > bestD) { bestD = d; best = c; }
                }
                picked.Add(best);
            }

            var res = new List<(Vector3, float)>();
            foreach (var p in picked)
            {
                // face the longest open line of sight among 8 directions (prefers looking along streets, not into walls)
                float bestYaw = 0f, bestLen = -1f;
                for (int i = 0; i < 8; i++)
                {
                    float yaw = i * 45f;
                    Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    float len = Physics.Raycast(p + Vector3.up * 1.6f, dir, out var h, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? h.distance : 60f;
                    Vector3 toC = centre - p; toC.y = 0f;
                    len += Vector3.Dot(dir, toC.normalized) * 5f;
                    if (len > bestLen) { bestLen = len; bestYaw = yaw; }
                }
                res.Add((p, bestYaw));
            }
            return res;
        }
    }
}
