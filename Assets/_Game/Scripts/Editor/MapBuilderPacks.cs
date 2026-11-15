using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// Deathmatch / team maps made from the six LowPolyFPS asset-pack demo scenes (Tools > FPS > Maps > Packs). Same
    /// pipeline as Map_Flooded, generalised: the demo scene is copied to Assets/Scenes, its camera rigs, post-processing
    /// and sun are removed, meshes in the play area get colliders where they have none, the padded core is fenced
    /// ("Bounds": four walls and a catch floor), a NavMesh limited to it is baked and saved next to the scene, spawn
    /// points are picked on that NavMesh, and the match stack (FX, player, MatchManager, bot template) is added.
    /// </summary>
    public static partial class RangeBuilder
    {
        /// <summary>One pack map: source scene, names, approximate playable core and look.</summary>
        class PackMap
        {
            public string source, scene, title;
            public Rect core;                      // playable rectangle (world XZ), approximate
            public float pad = 6f;                 // the walls stand this far outside the core
            public LightPreset light;              // probe centre / size are filled in from the scan
            public float shadowStrength = 0.85f;
            public int spawns = 16;
            public float floorY = float.NaN;       // catch-floor top; surfaces below it are ignored (rooftop map). NaN: 1 m under the lowest ground
            public float ceilY = float.NaN;        // NavMesh (so bots and spawns) stays below it: an indoor map whose roof is not part of the play
            public System.Action<Scene> fixups;    // optional per-map tweaks, run after the demo rig is stripped
        }

        /// <summary>Heights of the upward-facing surfaces found by the column scan.</summary>
        struct PackLevels { public float min, max, coreLow; }

#pragma warning disable CS0618 // NavigationStatic is obsolete (NavMeshSurface ignores it), kept as on the other maps
        const StaticEditorFlags PackStatic = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                             StaticEditorFlags.OccludeeStatic | StaticEditorFlags.NavigationStatic;
#pragma warning restore CS0618

        // no collider for these even when the mesh has none (lower-case name parts)
        static readonly string[] PackSoftNames = { "grass", "flower", "decal", "wire", "leaf", "leaves", "mud", "puddle", "shadow", "rope", "curtain", "cobweb", "godray", "lightshaft", "fx_" };
        // demo helpers removed once they are empty (lower-case name parts)
        static readonly string[] PackHelperNames = { "camera", "postprocess", "staticlightingsky", "sky and fog", "volume", "eventsystem", "directional light" };

        [MenuItem("Tools/FPS/Maps/Packs/Build All Pack Maps", priority = 100)]
        static void MPackAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildPackDowntown();
            BuildPackBazaar();
            BuildPackSuburb();
            BuildPackDepot();
            BuildPackStation();
            BuildPackConstruction();
            BuildPackDesertBase();
            SetBuildScenes();
            Debug.Log("[FPS] Pack maps done.");
        }

        [MenuItem("Tools/FPS/Maps/Packs/Downtown")] static void MPack1() => BuildPackDowntown();
        [MenuItem("Tools/FPS/Maps/Packs/Bazaar")] static void MPack2() => BuildPackBazaar();
        [MenuItem("Tools/FPS/Maps/Packs/Suburb")] static void MPack3() => BuildPackSuburb();
        [MenuItem("Tools/FPS/Maps/Packs/Depot")] static void MPack4() => BuildPackDepot();
        [MenuItem("Tools/FPS/Maps/Packs/Station")] static void MPack5() => BuildPackStation();
        [MenuItem("Tools/FPS/Maps/Packs/Construction")] static void MPack6() => BuildPackConstruction();

        // ================================================================== maps

        /// <summary>City street between tall buildings; low warm sun, neutral-cool ambient.</summary>
        static void BuildPackDowntown() => BuildFromPack(new PackMap
        {
            source = "Assets/LowPolyFPS/Scenes/LowPolyFPS_Map1.unity", scene = "Map_Downtown", title = "Даунтаун",
            core = Rect.MinMaxRect(-43f, -19f, 28f, 61f),
            light = new LightPreset
            {
                name = "Downtown", sunIntensity = 1.5f, sunColor = new Color(1f, 0.83f, 0.66f), sunEuler = new Vector3(27f, -114f, 0f),
                ambSky = new Color(0.47f, 0.52f, 0.62f), ambEquator = new Color(0.45f, 0.44f, 0.45f), ambGround = new Color(0.22f, 0.21f, 0.2f),
                fogColor = new Color(0.58f, 0.57f, 0.6f), fogStart = 40f, fogEnd = 260f,
                skyTint = new Color(0.55f, 0.5f, 0.5f), atmosphere = 1.8f, skyExposure = 1.05f,
                exposure = 0.5f, contrast = 16f, saturation = 0f, bloom = 0.9f,
            },
        });

        /// <summary>Middle-eastern market village; hot high sun. The hills around stay as scenery outside the fence.</summary>
        static void BuildPackBazaar() => BuildFromPack(new PackMap
        {
            source = "Assets/LowPolyFPS2/Scenes/LowPolyFPS2_Demo.unity", scene = "Map_Bazaar", title = "Базар",
            core = Rect.MinMaxRect(1f, 2f, 61f, 63f),
            light = new LightPreset
            {
                name = "Bazaar", sunIntensity = 2.1f, sunColor = new Color(1f, 0.88f, 0.7f), sunEuler = new Vector3(55f, -121f, 0f),
                ambSky = new Color(0.64f, 0.66f, 0.7f), ambEquator = new Color(0.66f, 0.57f, 0.45f), ambGround = new Color(0.44f, 0.35f, 0.24f),
                fogColor = new Color(0.87f, 0.78f, 0.63f), fogStart = 70f, fogEnd = 340f,
                skyTint = new Color(0.62f, 0.55f, 0.46f), atmosphere = 1.6f, skyExposure = 1.3f,
                exposure = 0.22f, contrast = 18f, saturation = 6f, bloom = 0.8f,
            },
        });

        /// <summary>Street, parking, office building and park; mild day.</summary>
        static void BuildPackSuburb() => BuildFromPack(new PackMap
        {
            source = "Assets/LowPolyFPS3/Scenes/LowPolyFPS3_Demo.unity", scene = "Map_Suburb", title = "Пригород",
            core = Rect.MinMaxRect(-32f, -18f, 32f, 47f),
            light = new LightPreset
            {
                name = "Suburb", sunIntensity = 1.85f, sunColor = new Color(1f, 0.95f, 0.87f), sunEuler = new Vector3(46f, -113f, 0f),
                ambSky = new Color(0.56f, 0.64f, 0.78f), ambEquator = new Color(0.48f, 0.5f, 0.5f), ambGround = new Color(0.25f, 0.25f, 0.21f),
                fogColor = new Color(0.7f, 0.77f, 0.86f), fogStart = 60f, fogEnd = 300f,
                skyTint = new Color(0.48f, 0.52f, 0.6f), atmosphere = 1f, skyExposure = 1.3f,
                exposure = 0.32f, contrast = 14f, saturation = 8f, bloom = 0.7f,
            },
        });

        /// <summary>
        /// Small roofed warehouse / range with containers; dim ambient, the pack's spot lights do the work. The flat roof
        /// (~9.3 m) is out of play: uncapped, the spawn search took it as the largest open region and the whole fight
        /// happened up there.
        /// </summary>
        static void BuildPackDepot() => BuildFromPack(new PackMap
        {
            source = "Assets/LowPolyFPS4/Scenes/LowPolyFPS4_Demo.unity", scene = "Map_Depot", title = "Склад боеприпасов",
            core = Rect.MinMaxRect(-14f, -22f, 14f, 22f), spawns = 12, ceilY = 8f,
            light = new LightPreset
            {
                name = "Depot", sunIntensity = 1.3f, sunColor = new Color(1f, 0.93f, 0.84f), sunEuler = new Vector3(60f, -112f, 0f),
                ambSky = new Color(0.4f, 0.42f, 0.46f), ambEquator = new Color(0.34f, 0.33f, 0.32f), ambGround = new Color(0.2f, 0.19f, 0.17f),
                fogColor = new Color(0.25f, 0.25f, 0.27f), fogStart = 25f, fogEnd = 150f,
                skyTint = new Color(0.45f, 0.48f, 0.55f), atmosphere = 1.2f, skyExposure = 1f,
                exposure = 0.65f, contrast = 16f, saturation = 0f, bloom = 1f,
            },
        });

        /// <summary>Train station: tracks, trains, platforms; overcast (weak sun, soft shadows, bright flat ambient).</summary>
        static void BuildPackStation() => BuildFromPack(new PackMap
        {
            source = "Assets/LowPolyFPS5/Scenes/LowPolyFPS5_Demo.unity", scene = "Map_Station", title = "Вокзал",
            core = Rect.MinMaxRect(-20f, -50f, 45f, 50f), shadowStrength = 0.45f,
            light = new LightPreset
            {
                name = "Station", sunIntensity = 0.95f, sunColor = new Color(0.9f, 0.93f, 0.98f), sunEuler = new Vector3(62f, -132f, 0f),
                ambSky = new Color(0.66f, 0.7f, 0.76f), ambEquator = new Color(0.56f, 0.58f, 0.6f), ambGround = new Color(0.3f, 0.3f, 0.31f),
                fogColor = new Color(0.62f, 0.65f, 0.7f), fogStart = 30f, fogEnd = 230f,
                skyTint = new Color(0.5f, 0.52f, 0.55f), atmosphere = 2.2f, skyExposure = 1f,
                exposure = 0.5f, contrast = 12f, saturation = -12f, bloom = 0.6f,
            },
        });

        /// <summary>
        /// Construction site on a tower roof: its floors are at y 50.0 / 53.8 / 57.6 above the demo city. There is no fall
        /// damage, so the walls hug the building (pad 1 m) and the catch floor sits ~1 m under the lowest roof floor
        /// (a faller can mantle back); the city and the tower's inside below stay out of the NavMesh.
        /// </summary>
        static void BuildPackConstruction() => BuildFromPack(new PackMap
        {
            source = "Assets/LowPolyFPS6/Scenes/LowPolyFPS6_Demo.unity", scene = "Map_Construction", title = "Стройка",
            core = Rect.MinMaxRect(-19f, -20f, 19f, 19f), pad = 1f, spawns = 14, floorY = 49.2f,
            light = new LightPreset
            {
                name = "Construction", sunIntensity = 2.2f, sunColor = new Color(1f, 0.96f, 0.88f), sunEuler = new Vector3(58f, -125f, 0f),
                ambSky = new Color(0.6f, 0.68f, 0.82f), ambEquator = new Color(0.52f, 0.53f, 0.52f), ambGround = new Color(0.3f, 0.28f, 0.25f),
                fogColor = new Color(0.72f, 0.8f, 0.9f), fogStart = 80f, fogEnd = 360f,
                skyTint = new Color(0.45f, 0.52f, 0.66f), atmosphere = 0.9f, skyExposure = 1.35f,
                exposure = 0.22f, contrast = 16f, saturation = 10f, bloom = 0.7f,
            },
        });

        // ================================================================== pipeline

        static void BuildFromPack(PackMap m)
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(m.source)) { Debug.LogWarning($"[FPS] {m.source} not found — skipping {m.scene}"); return; }
            Prepare();
            EnsureFolder("Assets/Scenes");
            string path = "Assets/Scenes/" + m.scene + ".unity";

            AssetDatabase.DeleteAsset(path);
            if (!AssetDatabase.CopyAsset(m.source, path))
            {
                // DeleteAsset can fail silently (e.g. through the MCP bridge): overwrite the old copy in place
                var src = EditorSceneManager.OpenScene(m.source, OpenSceneMode.Single);
                if (!EditorSceneManager.SaveScene(src, path)) { Debug.LogError($"[FPS] could not copy {m.source} to {path}"); return; }
            }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Lightmapping.lightingDataAsset = null; // the demo's bake does not apply

            try { PackBuild(m, scene, path); }
            catch (System.Exception e) { Debug.LogException(e); PackAbort(path, m.scene + ": build failed"); }
        }

        static void PackBuild(PackMap m, Scene scene, string path)
        {
            PackStripDemo(scene);
            m.fixups?.Invoke(scene);

            var play = new Rect(m.core.xMin - m.pad, m.core.yMin - m.pad, m.core.width + 2f * m.pad, m.core.height + 2f * m.pad);
            int exact = PackExactColliders(scene, play);
            int added = PackColliders(scene, play);
            PackMarkStatic(scene);
            bool fixedFloor = !float.IsNaN(m.floorY);
            var lv = PackScan(play, m.core, fixedFloor ? m.floorY : float.NegativeInfinity);
            if (lv.min > lv.max) { PackAbort(path, m.scene + ": no walkable surfaces in the play area"); return; }

            var lp = m.light;
            lp.probeCenter = new Vector3(play.center.x, lv.coreLow + 6f, play.center.y);
            lp.probeSize = new Vector3(play.width + 20f, 40f, play.height + 20f);
            BuildLighting(lp);
            RenderSettings.sun.shadowStrength = m.shadowStrength;

            // ---- fence: walls above every surface, catch floor 1 m under the lowest ground (or at the map's floorY)
            float floorTop = fixedFloor ? m.floorY : lv.min - 1f;
            float wallTop = Mathf.Max(lv.coreLow + 40f, Mathf.Min(lv.max, lv.coreLow + 150f) + 3f);
            var game = new GameObject("_Deathmatch").transform;
            PackBounds(game, play, floorTop, wallTop);

            // ---- navigation: only the fenced area, baked before any character exists
            var nav = new GameObject("Navigation");
            nav.transform.SetParent(game, false);
            var surface = nav.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Volume;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            float navBottom = floorTop + 0.5f, navTop = float.IsNaN(m.ceilY) ? Mathf.Min(lv.max, lv.coreLow + 150f) + 3f : m.ceilY;
            surface.center = new Vector3(play.center.x, (navBottom + navTop) * 0.5f, play.center.y);
            surface.size = new Vector3(play.width, navTop - navBottom, play.height);
            var cover = PackAll<Destructible>(scene);
            foreach (var d in cover) { var dc = d.GetComponent<Collider>(); if (dc) dc.enabled = false; }
            surface.BuildNavMesh();
            foreach (var d in cover) { var dc = d.GetComponent<Collider>(); if (dc) dc.enabled = true; }
            if (!surface.navMeshData) { PackAbort(path, m.scene + ": the NavMesh bake produced nothing"); return; }

            var spawns = PackSpawns(m.core, m.spawns);
            if (spawns.Count < 2) { PackAbort(path, m.scene + ": fewer than 2 spawn points found on the NavMesh"); return; }
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
            string navPath = "Assets/Scenes/" + m.scene + "_NavMesh.asset";
            AssetDatabase.DeleteAsset(navPath);
            AssetDatabase.CreateAsset(surface.navMeshData, navPath);

            BuildFXObject();
            var player = BuildPlayer();
            player.transform.position = spawns[0].pos + Vector3.up * 0.05f;
            player.transform.rotation = Quaternion.Euler(0f, spawns[0].yaw, 0f);

            var mm = new GameObject("MatchManager").AddComponent<MatchManager>();
            mm.mapName = m.title;
            mm.navSurface = surface;
            mm.botTemplate = BuildBotTemplate(spawns[1].pos);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FPS] {m.scene} built: {spawns.Count} spawns, {exact} box colliders made exact, {added} colliders added, fence {play.width:0}x{play.height:0} m up to {wallTop:0} m, " +
                      $"ground {lv.coreLow:0.0} m, catch floor {floorTop:0.0} m");
        }

        /// <summary>Failed build: report, leave an empty scene and delete the half-made copy so it never reaches the build list.</summary>
        static void PackAbort(string path, string why)
        {
            Debug.LogError($"[FPS] {why} — {path} removed, map not built");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(path);
        }

        static List<T> PackAll<T>(Scene scene) where T : Component
        {
            var res = new List<T>();
            foreach (var root in scene.GetRootGameObjects()) res.AddRange(root.GetComponentsInChildren<T>(true));
            return res;
        }

        // ================================================================== demo rig

        /// <summary>
        /// Removes the demo rig: cameras (with their bare rig children), audio listeners, URP / legacy post-processing
        /// volumes, uGUI event systems and the directional sun; other lights become realtime without shadows (baked-only
        /// ones are switched off). Then missing scripts and the empty helper objects left behind are removed.
        /// </summary>
        static void PackStripDemo(Scene scene)
        {
            foreach (var cam in PackAll<Camera>(scene)) if (cam) PackRemoveCamera(cam);
            foreach (var al in PackAll<AudioListener>(scene)) if (al) PackRemoveComponent(al);
            foreach (var v in PackAll<Volume>(scene)) if (v) PackRemoveComponent(v);
            // legacy post-processing (PPv2) and event systems by type name: no assembly reference needed
            foreach (var mb in PackAll<MonoBehaviour>(scene))
            {
                if (!mb) continue;
                string tn = mb.GetType().Name;
                if (tn == "PostProcessVolume" || tn == "PostProcessLayer" || tn == "EventSystem" || tn.EndsWith("InputModule")) PackRemoveComponent(mb);
            }
            foreach (var l in PackAll<Light>(scene))
            {
                if (!l) continue;
                if (l.type == LightType.Directional) PackRemoveComponent(l); // BuildLighting adds the map's own sun
                else if (l.lightmapBakeType == LightmapBakeType.Baked) l.enabled = false;
                else { l.lightmapBakeType = LightmapBakeType.Realtime; l.shadows = LightShadows.None; }
            }
            PackStripMissingScripts(scene);
            PackRemoveEmptyHelpers(scene);
        }

        static void PackRemoveCamera(Camera cam)
        {
            var go = cam.gameObject;
            // real scene content hanging under a camera survives; bare children (pivots, listeners) belong to the rig
            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                for (int i = go.transform.childCount - 1; i >= 0; i--)
                {
                    var ch = go.transform.GetChild(i);
                    if (!PackIsBare(ch.gameObject, null)) ch.SetParent(go.transform.parent, true);
                }
            if (PackIsBare(go, cam)) { PackRemove(go); return; }
            cam.enabled = false;
            var al = go.GetComponent<AudioListener>();
            if (al) al.enabled = false;
        }

        /// <summary>Removes a demo-only component: the whole object when nothing else lives on it, else just disables it.</summary>
        static void PackRemoveComponent(Component c)
        {
            if (PackIsBare(c.gameObject, c)) PackRemove(c.gameObject);
            else if (c is Behaviour b) b.enabled = false;
        }

        /// <summary>True when the object and its children carry nothing visible or solid (except <paramref name="except"/>).</summary>
        static bool PackIsBare(GameObject go, Component except)
        {
            foreach (var c in go.GetComponentsInChildren<Component>(true))
                if (c && c != except && (c is Renderer || c is Collider || c is Light || c is ParticleSystem || c is Terrain || c is AudioSource || c is Camera))
                    return false;
            return true;
        }

        static void PackRemove(GameObject go)
        {
            // objects inside a prefab instance cannot be destroyed: switch them off instead
            if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go)) go.SetActive(false);
            else Object.DestroyImmediate(go);
        }

        static int PackStripMissingScripts(Scene scene)
        {
            int removed = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) == 0) continue;
                    // components of a prefab instance can only be removed once it is unpacked
                    if (PrefabUtility.IsPartOfPrefabInstance(go))
                    {
                        var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
                        if (outer) PrefabUtility.UnpackPrefabInstance(outer, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    }
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
                }
            return removed;
        }

        /// <summary>Deletes leaf objects with only a Transform whose names mark them as demo helpers (camera rigs, PostProcessing, StaticLightingSky…).</summary>
        static void PackRemoveEmptyHelpers(Scene scene)
        {
            var all = new List<Transform>();
            foreach (var root in scene.GetRootGameObjects()) all.AddRange(root.GetComponentsInChildren<Transform>(true));
            for (int i = all.Count - 1; i >= 0; i--) // children first: a rig root empties once its cameras are gone
            {
                var t = all[i];
                if (!t || t.childCount > 0 || t.GetComponents<Component>().Length > 1 || PrefabUtility.IsPartOfPrefabInstance(t)) continue;
                string n = t.name.ToLowerInvariant();
                foreach (var h in PackHelperNames)
                    if (n.Contains(h)) { Object.DestroyImmediate(t.gameObject); break; }
            }
        }

        // ================================================================== colliders & static flags

        /// <summary>
        /// The packs put box colliders on walls and buildings that have doorways, arches and gaps: the box fills the opening,
        /// so a door you can see is a wall you cannot walk through, and the NavMesh splits into sealed rooms (spawns inside
        /// them could not get out). Inside the play area a box collider on a big static mesh (over 1.8 m) is switched off and
        /// an exact (non-convex) mesh collider of the same mesh takes its place; small solid props keep their boxes, moving
        /// or simulated objects keep theirs. A box on a parent with several meshes gives each mesh its own collider.
        /// </summary>
        static int PackExactColliders(Scene scene, Rect area)
        {
            int n = 0;
            foreach (var box in PackAll<BoxCollider>(scene))
            {
                if (!box || !box.enabled || box.isTrigger) continue;
                var go = box.gameObject;
                if (go.GetComponentInParent<Rigidbody>(true) || go.GetComponentInParent<Animator>(true) || go.GetComponent<Destructible>()) continue;
                var b = box.bounds;
                if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) < 1.8f) continue;
                if (!area.Overlaps(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z))) continue;
                var meshes = new List<MeshFilter>();
                var own = go.GetComponent<MeshFilter>();
                if (own && own.sharedMesh) meshes.Add(own);
                else
                    foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                        if (mf.sharedMesh && mf.GetComponent<MeshRenderer>() && !mf.GetComponent<Collider>() && b.Intersects(mf.GetComponent<MeshRenderer>().bounds))
                            meshes.Add(mf);
                if (meshes.Count == 0) continue;
                // a box on a prefab instance cannot be destroyed: switched off (an override) instead
                box.enabled = false;
                foreach (var mf in meshes) mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                n++;
            }
            return n;
        }

        /// <summary>Adds a MeshCollider to visible solid meshes in the play area that have no collider. Returns the count.</summary>
        static int PackColliders(Scene scene, Rect area)
        {
            Physics.SyncTransforms();
            int added = 0;
            foreach (var mr in PackAll<MeshRenderer>(scene))
            {
                if (!mr.enabled || !mr.gameObject.activeInHierarchy || mr.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var mf = mr.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh || mr.GetComponentInParent<ParticleSystem>(true)) continue;
                var b = mr.bounds;
                if (!area.Overlaps(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z)) || PackHasCollider(mr) || PackSoft(mr.name, b)) continue;
                mr.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                added++;
            }
            return added;
        }

        static bool PackHasCollider(Renderer r)
        {
            if (r.GetComponentInParent<Collider>(true)) return true;
            foreach (Transform c in r.transform)
                if (c.GetComponent<Collider>() && !c.GetComponent<Renderer>()) return true; // separate collision child
            var lod = r.GetComponentInParent<LODGroup>(true);
            if (lod)
            {
                var lods = lod.GetLODs();
                if (lods.Length > 0 && System.Array.IndexOf(lods[0].renderers, r) < 0) return true; // lower LODs share LOD0's collider
                if (lod.GetComponentInChildren<Collider>(true)) return true;
            }
            return false;
        }

        /// <summary>Foliage, decals, wires and tiny clutter stay walk- and shoot-through.</summary>
        static bool PackSoft(string name, Bounds b)
        {
            string n = name.ToLowerInvariant();
            foreach (var s in PackSoftNames)
                if (n.Contains(s) && !(s == "flower" && n.Contains("bed"))) return true;
            Vector3 e = b.size;
            float lo = Mathf.Min(e.x, Mathf.Min(e.y, e.z)), hi = Mathf.Max(e.x, Mathf.Max(e.y, e.z)), mid = e.x + e.y + e.z - lo - hi;
            if (hi < 0.15f || mid < 0.15f) return true; // tiny clutter, wires and rods
            // a flat piece lying on a surface is a decal or a rug; one with nothing under it (plate, walkway) is solid
            return e.y < 0.15f && Physics.Raycast(new Vector3(b.center.x, b.max.y + 0.05f, b.center.z), Vector3.down, e.y + 0.45f,
                                                  Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Static flags on the pack geometry (moving or simulated objects excluded).</summary>
        static void PackMarkStatic(Scene scene)
        {
            foreach (var mr in PackAll<MeshRenderer>(scene))
            {
                var go = mr.gameObject;
                if (!go.GetComponent<MeshFilter>() || go.GetComponentInParent<ParticleSystem>(true) || go.GetComponentInParent<Rigidbody>(true) || go.GetComponentInParent<Animator>(true)) continue;
                GameObjectUtility.SetStaticEditorFlags(go, GameObjectUtility.GetStaticEditorFlags(go) | PackStatic);
            }
        }

        // ================================================================== scan, fence

        /// <summary>Vertical extent of the solid colliders over the area (start and end of the column rays).</summary>
        static bool PackVerticalRange(Rect area, out float top, out float bottom)
        {
            Physics.SyncTransforms();
            top = float.MinValue; bottom = float.MaxValue;
            foreach (var c in Object.FindObjectsByType<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                var b = c.bounds;
                if (!area.Overlaps(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z))) continue;
                top = Mathf.Max(top, b.max.y);
                bottom = Mathf.Min(bottom, b.min.y);
            }
            top += 1f; bottom -= 1f;
            return top > bottom;
        }

        /// <summary>
        /// Upward-facing surfaces under (x, z), top to bottom. Repeated rays rather than RaycastAll, which reports one hit
        /// per collider and would miss the floors of a building that is a single mesh collider.
        /// </summary>
        static List<Vector3> PackColumn(float x, float z, float top, float bottom)
        {
            var res = new List<Vector3>();
            var o = new Vector3(x, top, z);
            for (int i = 0; i < 32 && o.y > bottom; i++)
            {
                if (!Physics.Raycast(o, Vector3.down, out var h, o.y - bottom, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) break;
                if (h.normal.y >= 0.6f) res.Add(h.point);
                o.y = h.point.y - 0.05f;
            }
            return res;
        }

        /// <summary>Surface heights (at or above minY) over the fenced area, and the ground of the core (20th percentile).</summary>
        static PackLevels PackScan(Rect play, Rect core, float minY)
        {
            var lv = new PackLevels { min = float.MaxValue, max = float.MinValue };
            if (!PackVerticalRange(play, out float top, out float bottom)) return lv;
            var coreHits = new List<float>();
            for (float x = play.xMin + 1f; x < play.xMax; x += 2f)
                for (float z = play.yMin + 1f; z < play.yMax; z += 2f)
                    foreach (var p in PackColumn(x, z, top, Mathf.Max(bottom, minY)))
                    {
                        lv.min = Mathf.Min(lv.min, p.y);
                        lv.max = Mathf.Max(lv.max, p.y);
                        if (core.Contains(new Vector2(x, z))) coreHits.Add(p.y);
                    }
            coreHits.Sort();
            lv.coreLow = coreHits.Count > 0 ? coreHits[coreHits.Count / 5] : lv.min;
            return lv;
        }

        /// <summary>"Bounds" (the name DroneMode looks for): four invisible walls around the area and a catch floor under it.</summary>
        static void PackBounds(Transform parent, Rect a, float floorTop, float wallTop)
        {
            var root = new GameObject("Bounds").transform;
            root.SetParent(parent, false);
            const float t = 2f;
            float bottom = floorTop - 2f, h = wallTop - bottom, cy = (wallTop + bottom) * 0.5f;
            GameObject W(string n, Vector3 c, Vector3 s)
            {
                var go = new GameObject(n);
                go.transform.SetParent(root, false);
                go.transform.position = c;
                go.AddComponent<BoxCollider>().size = s;
                return go;
            }
            W("North", new Vector3(a.center.x, cy, a.yMax + t * 0.5f), new Vector3(a.width + 2f * t, h, t));
            W("South", new Vector3(a.center.x, cy, a.yMin - t * 0.5f), new Vector3(a.width + 2f * t, h, t));
            W("East", new Vector3(a.xMax + t * 0.5f, cy, a.center.y), new Vector3(t, h, a.height));
            W("West", new Vector3(a.xMin - t * 0.5f, cy, a.center.y), new Vector3(t, h, a.height));
            // catch floor (no kill volume): kept out of every NavMesh bake, MapCheck's included
            var floor = W("Floor", new Vector3(a.center.x, floorTop - 1f, a.center.y), new Vector3(a.width + 2f * t, 2f, a.height + 2f * t));
            floor.AddComponent<NavMeshModifier>().ignoreFromBuild = true;
        }

        // ================================================================== spawns

        /// <summary>
        /// Spawn points on the baked NavMesh inside <paramref name="area"/> (generalised FloodedSpawns): grid columns at
        /// every walkable level, standing room with 2.2 m headroom, the largest NavMesh-connected region only (roofs,
        /// train tops and closed rooms drop out), farthest-point selection, each facing its longest open sightline.
        /// </summary>
        static List<(Vector3 pos, float yaw)> PackSpawns(Rect area, int count = 16)
        {
            var res = new List<(Vector3, float)>();
            if (!PackVerticalRange(area, out float top, out float bottom)) return res;

            float step = Mathf.Clamp(Mathf.Sqrt(area.width * area.height) / 25f, 2f, 4f);
            var cands = new List<Vector3>();
            for (float x = area.xMin + step * 0.5f; x < area.xMax; x += step)
                for (float z = area.yMin + step * 0.5f; z < area.yMax; z += step)
                    foreach (var hp in PackColumn(x, z, top, bottom))
                    {
                        if (!NavMesh.SamplePosition(hp, out var nh, 0.8f, NavMesh.AllAreas)) continue;
                        Vector3 p = nh.position;
                        if (Mathf.Abs(p.y - hp.y) > 0.5f || !area.Contains(new Vector2(p.x, p.z)) || !PackClear(p)) continue; // snapped to another level / outside / blocked
                        cands.Add(p);
                    }
            if (cands.Count == 0) return res;

            // largest connected region, grown from the candidates nearest the centre at ground level
            var heights = new List<float>();
            foreach (var c in cands) heights.Add(c.y);
            heights.Sort();
            var centre = new Vector3(area.center.x, heights[heights.Count / 5], area.center.y);
            cands.Sort((p, q) => (p - centre).sqrMagnitude.CompareTo((q - centre).sqrMagnitude));
            var region = new int[cands.Count];
            for (int i = 0; i < region.Length; i++) region[i] = -1;
            var path = new NavMeshPath();
            int best = -1, bestCount = 0, left = cands.Count;
            for (int i = 0, id = 0; i < cands.Count && left > bestCount; i++)
            {
                if (region[i] >= 0) continue;
                region[i] = id;
                left--;
                int n = 1;
                for (int j = i + 1; j < cands.Count; j++)
                    if (region[j] < 0 && NavMesh.CalculatePath(cands[i], cands[j], NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    { region[j] = id; n++; left--; }
                if (n > bestCount) { bestCount = n; best = id; }
                id++;
            }
            var ok = new List<Vector3>();
            for (int i = 0; i < cands.Count; i++) if (region[i] == best) ok.Add(cands[i]);

            // farthest-point sampling, seeded by the point farthest from the centre
            float Flat(Vector3 v) => v.x * v.x + v.z * v.z;
            var picked = new List<Vector3>();
            Vector3 first = ok[0];
            foreach (var c in ok) if (Flat(c - centre) > Flat(first - centre)) first = c;
            picked.Add(first);
            while (picked.Count < count && picked.Count < ok.Count)
            {
                Vector3 next = ok[0]; float nextD = -1f;
                foreach (var c in ok)
                {
                    float d = float.MaxValue;
                    foreach (var p in picked) d = Mathf.Min(d, (c - p).sqrMagnitude);
                    if (d > nextD) { nextD = d; next = c; }
                }
                picked.Add(next);
            }

            foreach (var p in picked) res.Add((p, PackFacing(p, centre)));
            return res;
        }

        /// <summary>Room for a standing soldier: MapCheck's body test plus elbow room up to 2.2 m.</summary>
        static bool PackClear(Vector3 p) =>
            !Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
            !Physics.CheckCapsule(p + Vector3.up * 0.8f, p + Vector3.up * 1.75f, 0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        /// <summary>Yaw of the longest open line of sight among 8 directions, nudged towards the centre.</summary>
        static float PackFacing(Vector3 p, Vector3 centre)
        {
            Vector3 toC = centre - p; toC.y = 0f;
            float bestYaw = 0f, bestLen = -1f;
            for (int i = 0; i < 8; i++)
            {
                float yaw = i * 45f;
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                float len = Physics.Raycast(p + Vector3.up * 1.6f, dir, out var h, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? h.distance : 60f;
                if (toC.sqrMagnitude > 0.01f) len += Vector3.Dot(dir, toC.normalized) * 5f;
                if (len > bestLen) { bestLen = len; bestYaw = yaw; }
            }
            return bestYaw;
        }
    }
}
