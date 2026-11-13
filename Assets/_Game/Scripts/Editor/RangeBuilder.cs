using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPS.EditorTools
{
    /// <summary>
    /// Tools > FPS > Build Shooting Range — generates the whole test scene:
    /// lighting + post-processing, shooting range with targets, vault/mantle obstacle course,
    /// physics props and the player with a procedural rifle + arms viewmodel.
    /// </summary>
    [InitializeOnLoad]
    public static partial class RangeBuilder
    {
        // Build the range automatically the first time these scripts compile (if it doesn't exist yet).
        static RangeBuilder()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) return;
                if (SessionState.GetBool("FPS.AutoBuildTried", false)) return;
                SessionState.SetBool("FPS.AutoBuildTried", true);
                for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                    if (EditorSceneManager.GetSceneAt(i).isDirty) return; // never discard user's unsaved work
                Build();
            };
        }

        const string Root = "Assets/_Game/Generated";
        const string ScenePath = "Assets/Scenes/ShootingRange.unity";

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        [MenuItem("Tools/FPS/Build Shooting Range", priority = 0)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            mats.Clear();
            EnsureFolder("Assets/_Game");
            EnsureFolder(Root);
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Textures");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildLighting(LightPreset.Range);
            var env = new GameObject("Environment").transform;
            BuildGround(env);
            BuildRange(env);
            BuildCourse(env);
            BuildProps(env);
            var targets = new GameObject("Targets").transform;
            BuildTargets(targets);
            BuildFXObject();
            var player = BuildPlayer();
            BuildArmory(env);
            BuildYard(env, new GameObject("Bots").transform);

            EditorSceneManager.SaveScene(scene, ScenePath);
            SetBuildScenes();
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = player;
            Debug.Log("[FPS] Shooting range built: " + ScenePath + " — press Play.");
        }

        // ------------------------------------------------------------------ helpers

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static readonly string[] SceneOrder = { "MainMenu", "ShootingRange", "Map_Port", "Map_Town", "Map_Warehouse", "Map_Plant", "Map_Village", "Map_Flooded", "Map_Disco" };

        static void SetBuildScenes()
        {
            var list = new List<EditorBuildSettingsScene>();
            foreach (var n in SceneOrder)
            {
                string path = "Assets/Scenes/" + n + ".unity";
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) list.Add(new EditorBuildSettingsScene(path, true));
            }
            foreach (var s in EditorBuildSettings.scenes)
                if (!list.Exists(x => x.path == s.path)) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }

        /// <summary>
        /// Creates or updates an asset in place. Existing materials keep their GUID (so other scenes
        /// referencing them stay intact); existing textures are reused.
        /// </summary>
        static T SaveAsset<T>(T obj, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing)
            {
                if (obj is Material nm && existing is Material em)
                {
                    em.shader = nm.shader;
                    em.CopyPropertiesFromMaterial(nm);
                    em.shaderKeywords = nm.shaderKeywords;
                    em.renderQueue = nm.renderQueue;
                    em.globalIlluminationFlags = nm.globalIlluminationFlags;
                    EditorUtility.SetDirty(em);
                    return em as T;
                }
                if (obj is Texture2D && existing is Texture2D) return existing as T;
                AssetDatabase.DeleteAsset(path);
            }
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        static Material Lit(string name, Color c, float metal = 0f, float smooth = 0.35f, bool emissive = false)
        {
            if (mats.TryGetValue(name, out var cached)) return cached;
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", smooth);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            m = SaveAsset(m, $"{Root}/Materials/{name}.mat");
            mats[name] = m;
            return m;
        }

        static Material Unlit(string name, Color c)
        {
            if (mats.TryGetValue(name, out var cached)) return cached;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
            m.SetColor("_BaseColor", c);
            m = SaveAsset(m, $"{Root}/Materials/{name}.mat");
            mats[name] = m;
            return m;
        }

        static Material Particle(string name, Texture2D tex, bool additive, Color tint, bool doubleSided = false)
        {
            var texAsset = SaveAsset(tex, $"{Root}/Textures/{name}.asset");
            var m = FX.MakeParticleMat(name, texAsset, additive, tint, doubleSided);
            return SaveAsset(m, $"{Root}/Materials/{name}.mat");
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material m,
                               Quaternion? rot = null, bool collider = true, bool isStatic = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            if (isStatic) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material m, bool isStatic = true)
            => Prim(PrimitiveType.Cube, name, parent, center, size, m, null, true, isStatic);

        /// <summary>Box resting on the ground: footprint center (x,z), size, sitting at baseY.</summary>
        static GameObject Block(string name, Transform parent, float x, float z, Vector3 size, Material m, float baseY = 0f, float yaw = 0f)
        {
            var go = Box(name, parent, new Vector3(x, baseY + size.y * 0.5f, z), size, m);
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        // ------------------------------------------------------------------ lighting & post

        struct LightPreset
        {
            public string name;
            public float sunIntensity; public Color sunColor; public Vector3 sunEuler;
            public Color ambSky, ambEquator, ambGround;
            public Color fogColor; public float fogStart, fogEnd;
            public Color skyTint; public float atmosphere, skyExposure;
            public float exposure, contrast, saturation, bloom;
            public Vector3 probeCenter, probeSize;

            public static LightPreset Range => new LightPreset
            {
                name = "Range", sunIntensity = 1.9f, sunColor = new Color(1f, 0.94f, 0.85f), sunEuler = new Vector3(47f, -38f, 0f),
                ambSky = new Color(0.56f, 0.64f, 0.76f), ambEquator = new Color(0.46f, 0.47f, 0.48f), ambGround = new Color(0.24f, 0.22f, 0.2f),
                fogColor = new Color(0.68f, 0.75f, 0.84f), fogStart = 60f, fogEnd = 260f,
                skyTint = new Color(0.5f, 0.5f, 0.5f), atmosphere = 1f, skyExposure = 1.3f,
                exposure = 0.35f, contrast = 14f, saturation = 8f, bloom = 0.75f,
                probeCenter = new Vector3(0f, 3f, 20f), probeSize = new Vector3(120f, 40f, 140f)
            };
        }

        static void BuildLighting(LightPreset p)
        {
            var sunGO = new GameObject("Sun");
            var sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = p.sunIntensity;
            sun.color = p.sunColor;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sunGO.transform.rotation = Quaternion.Euler(p.sunEuler);
            RenderSettings.sun = sun;

            var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "Sky_" + p.name };
            sky.SetColor("_SkyTint", p.skyTint);
            sky.SetFloat("_AtmosphereThickness", p.atmosphere);
            sky.SetFloat("_Exposure", p.skyExposure);
            sky.SetFloat("_SunSize", 0.035f);
            sky = SaveAsset(sky, $"{Root}/Materials/Sky_{p.name}.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambSky;
            RenderSettings.ambientEquatorColor = p.ambEquator;
            RenderSettings.ambientGroundColor = p.ambGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = p.fogStart;
            RenderSettings.fogEndDistance = p.fogEnd;
            RenderSettings.fogColor = p.fogColor;

            var probeGO = new GameObject("Reflection Probe");
            probeGO.transform.position = p.probeCenter;
            var probe = probeGO.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = p.probeSize;
            probe.resolution = 128;
            probe.intensity = 1f;

            var volGO = new GameObject("Global Volume");
            var vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            SaveAsset(profile, Root + "/Volume_" + p.name + ".asset");

            var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(p.bloom); bloom.threshold.Override(1.05f); bloom.scatter.Override(0.62f);
            var vig = profile.Add<Vignette>(true); vig.intensity.Override(0.24f); vig.smoothness.Override(0.45f);
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.Override(p.exposure); ca.contrast.Override(p.contrast); ca.saturation.Override(p.saturation);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true); smh.shadows.Override(new Vector4(0.96f, 0.99f, 1.06f, 0f));
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            vol.sharedProfile = profile;
        }

        // ------------------------------------------------------------------ environment

        static Texture2D GridTexture()
        {
            const int N = 512;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Grid", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.02f, y * 0.02f) * 0.05f + Mathf.PerlinNoise(x * 0.15f, y * 0.15f) * 0.025f;
                    float v = 0.42f + n;
                    int mx = x % 128, my = y % 128;
                    bool minor = mx < 2 || my < 2;
                    bool major = x < 4 || y < 4;
                    if (minor) v -= 0.06f;
                    if (major) v -= 0.1f;
                    px[y * N + x] = new Color(v, v * 1.01f, v * 1.03f, 1f);
                }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        static void BuildGround(Transform env)
        {
            var grid = SaveAsset(GridTexture(), Root + "/Textures/Grid.asset");
            var m = Lit("Ground", Color.white, 0f, 0.18f);
            m.SetTexture("_BaseMap", grid);
            m.SetTextureScale("_BaseMap", new Vector2(30f, 35f));
            Box("Ground", env, new Vector3(0f, -0.5f, 25f), new Vector3(120f, 1f, 140f), m);
        }

        static void BuildRange(Transform env)
        {
            var wall = Lit("RangeWall", new Color(0.55f, 0.56f, 0.58f), 0f, 0.15f);
            var dark = Lit("RangeDark", new Color(0.2f, 0.21f, 0.23f), 0f, 0.2f);
            var wood = Lit("Wood", new Color(0.5f, 0.36f, 0.24f), 0f, 0.25f);
            var stripe = Lit("Stripe", new Color(1f, 0.75f, 0.15f), 0f, 0.3f, true);
            stripe.SetColor("_EmissionColor", new Color(0.4f, 0.28f, 0.05f));

            var range = new GameObject("Range").transform;
            range.SetParent(env, false);

            // side walls and back berm
            Block("WallLeft", range, -10f, 34f, new Vector3(0.6f, 3.2f, 58f), wall);
            Block("WallRight", range, 10f, 34f, new Vector3(0.6f, 3.2f, 58f), wall);
            Block("BackBerm", range, 0f, 64f, new Vector3(21f, 7f, 2f), dark);
            Block("BermSlope", range, 0f, 62.4f, new Vector3(20.4f, 3f, 1.4f), dark);

            // roof beams for some framing
            for (int i = 0; i < 4; i++)
                Box("Beam" + i, range, new Vector3(0f, 3.35f, 8f + i * 3.5f), new Vector3(20.6f, 0.3f, 0.35f), dark);

            // firing line counter (vaultable) with a gap in the middle
            var counter = Block("CounterL", range, -5.5f, 4.5f, new Vector3(8f, 1.0f, 0.6f), wood);
            counter.AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            var counter2 = Block("CounterR", range, 5.5f, 4.5f, new Vector3(8f, 1.0f, 0.6f), wood);
            counter2.AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;

            // distance stripes on the floor
            foreach (float z in new[] { 10f, 20f, 30f, 45f, 60f })
                Prim(PrimitiveType.Cube, "Stripe" + z, range, new Vector3(0f, 0.005f, z), new Vector3(19f, 0.01f, 0.12f), stripe, null, false, true);

            // lane separators (low)
            foreach (float x in new[] { -3.5f, 3.5f })
                Block("Lane" + x, range, x, 32f, new Vector3(0.12f, 0.15f, 50f), dark);
        }

        static void BuildCourse(Transform env)
        {
            var orange = Lit("Obstacle", new Color(0.95f, 0.45f, 0.12f), 0f, 0.3f);
            var blue = Lit("Platform", new Color(0.25f, 0.42f, 0.7f), 0f, 0.3f);
            var concrete = Lit("Concrete", new Color(0.68f, 0.67f, 0.64f), 0f, 0.12f);
            var crate = Lit("Crate", new Color(0.62f, 0.47f, 0.3f), 0f, 0.25f);

            var course = new GameObject("ObstacleCourse").transform;
            course.SetParent(env, false);
            float x = -19f;

            // walls of increasing height — vault over (Space while moving toward them)
            Block("Wall_0.6", course, x, -2f, new Vector3(4f, 0.6f, 0.3f), orange);
            Block("Wall_0.95", course, x, 3f, new Vector3(4f, 0.95f, 0.3f), orange);
            Block("Wall_1.2", course, x, 8f, new Vector3(4f, 1.2f, 0.4f), orange);
            Block("Crate_1.0", course, x, 13f, new Vector3(1.8f, 1.0f, 1.0f), crate).AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            // thick platform — mantle on top
            Block("Platform_1.3", course, x, 20f, new Vector3(4f, 1.3f, 4f), blue);
            Block("Wall_1.4", course, x, 27f, new Vector3(4f, 1.4f, 0.3f), orange);

            // low tunnel — crouch or slide under it
            Block("TunnelPillarL", course, x - 2.2f, 33f, new Vector3(0.4f, 1.35f, 3f), concrete);
            Block("TunnelPillarR", course, x + 2.2f, 33f, new Vector3(0.4f, 1.35f, 3f), concrete);
            Block("TunnelRoof", course, x, 33f, new Vector3(4.8f, 0.3f, 3f), concrete, 1.35f);

            // staircase of crates — chain mantles
            Block("Step_0.5", course, x, 39f, new Vector3(3f, 0.5f, 1.2f), crate);
            Block("Step_1.0", course, x, 40.2f, new Vector3(3f, 1.0f, 1.2f), crate);
            Block("Step_1.5", course, x, 41.4f, new Vector3(3f, 1.5f, 1.2f), crate);
            Block("Step_2.0", course, x, 43.2f, new Vector3(3f, 2.0f, 2.4f), blue);
            // too tall to vault
            Block("Wall_2.6", course, x, 48f, new Vector3(4f, 2.6f, 0.4f), concrete);

            // cover pieces in the open area to the right of the course
            Block("Cover_A", course, -13f, 0f, new Vector3(2.5f, 1.1f, 0.4f), concrete, 0f, 15f);
            Block("Cover_B", course, -14f, 10f, new Vector3(2.5f, 0.9f, 0.6f), concrete, 0f, -10f);
            Block("Cover_C", course, -13.5f, 22f, new Vector3(1.4f, 1.2f, 1.4f), crate);
            Block("Ledge", course, -13.5f, 30f, new Vector3(3f, 1.45f, 3f), blue);
        }

        static void BuildProps(Transform env)
        {
            var crate = Lit("Crate", new Color(0.62f, 0.47f, 0.3f), 0f, 0.25f);
            var barrel = Lit("Barrel", new Color(0.62f, 0.12f, 0.1f), 0.6f, 0.45f);
            var props = new GameObject("PhysicsProps").transform;
            props.SetParent(env, false);

            int n = 0;
            for (int row = 0; row < 3; row++)
                for (int i = 0; i < 3 - row; i++)
                {
                    var c = Box("PropCrate" + n++, props, new Vector3(13f + i * 0.62f + row * 0.31f, 0.3f + row * 0.6f, 2f), Vector3.one * 0.6f, crate, false);
                    var rb = c.AddComponent<Rigidbody>(); rb.mass = 8f;
                    c.AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
                }
            for (int i = 0; i < 4; i++)
            {
                var b = Prim(PrimitiveType.Cylinder, "Barrel" + i, props, new Vector3(14f + (i % 2) * 0.8f, 0.45f, 6f + (i / 2) * 0.8f), new Vector3(0.6f, 0.45f, 0.6f), barrel);
                Object.DestroyImmediate(b.GetComponent<Collider>());
                var cc = b.AddComponent<CapsuleCollider>(); cc.height = 2f; cc.radius = 0.5f;
                var rb = b.AddComponent<Rigidbody>(); rb.mass = 12f;
                b.AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            }
        }

        // ------------------------------------------------------------------ targets

        static void BuildTargets(Transform parent)
        {
            Vector3[] statics =
            {
                new Vector3(-6f, 0, 10f), new Vector3(0f, 0, 10f), new Vector3(6f, 0, 10f),
                new Vector3(-7f, 0, 20f), new Vector3(-2f, 0, 20f), new Vector3(3f, 0, 20f), new Vector3(7.5f, 0, 20f),
                new Vector3(-6.5f, 0, 30f), new Vector3(6.5f, 0, 30f),
                new Vector3(-4f, 0, 45f), new Vector3(4f, 0, 45f),
                new Vector3(0f, 0, 60f),
            };
            foreach (var p in statics) MakeTarget(parent, p, 0f, 0f);
            MakeTarget(parent, new Vector3(0f, 0f, 30f), 4.5f, 2.2f);
            MakeTarget(parent, new Vector3(0f, 0f, 52f), 6f, 2.8f);
            MakeTarget(parent, new Vector3(-1f, 0f, 15f), 2.5f, 3.5f);

            MakeDummy(parent, new Vector3(7.5f, 0f, 7f));
            // targets near the obstacle course for shooting on the move
            MakeTarget(parent, new Vector3(-27f, 0f, 12f), 0f, 0f, 90f);
            MakeTarget(parent, new Vector3(-27f, 0f, 24f), 0f, 0f, 90f).AddComponent<MovingTarget>().axis = Vector3.forward;
            var stand = Lit("Platform", new Color(0.25f, 0.42f, 0.7f), 0f, 0.3f);
            Block("TargetStand", parent, -27f, 36f, new Vector3(1.5f, 1.2f, 1.5f), stand);
            MakeTarget(parent, new Vector3(-27f, 1.2f, 36f), 0f, 0f, 90f);
        }

        static GameObject MakeTarget(Transform parent, Vector3 pos, float moveDist, float moveSpeed, float yaw = 180f)
        {
            var metal = Lit("TargetMetal", new Color(0.18f, 0.19f, 0.2f), 0.8f, 0.45f);
            var plate = Lit("TargetPlate", new Color(0.92f, 0.9f, 0.86f), 0.3f, 0.45f, true);
            var head = Lit("TargetHead", new Color(1f, 0.42f, 0.14f), 0.3f, 0.45f, true);
            var ring = Lit("TargetRing", new Color(0.85f, 0.15f, 0.12f), 0f, 0.4f);

            var root = new GameObject(moveDist > 0f ? "MovingTarget" : "Target");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f); // front (+Z) faces the shooter
            root.AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;

            Prim(PrimitiveType.Cube, "Base", root.transform, new Vector3(0f, 0.06f, 0f), new Vector3(0.75f, 0.12f, 0.45f), metal);
            Prim(PrimitiveType.Cube, "Rail", root.transform, new Vector3(0f, 0.13f, 0f), new Vector3(0.5f, 0.03f, 0.08f), metal, null, false);

            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(0f, 0.14f, 0f);

            Prim(PrimitiveType.Cube, "Post", hinge, new Vector3(0f, 0.2f, 0f), new Vector3(0.06f, 0.4f, 0.04f), metal);
            var body = Prim(PrimitiveType.Cube, "Torso", hinge, new Vector3(0f, 0.78f, 0f), new Vector3(0.5f, 0.76f, 0.035f), plate);
            body.AddComponent<Hitbox>().multiplier = 1f;
            var shoulders = Prim(PrimitiveType.Cube, "Shoulders", hinge, new Vector3(0f, 1.1f, 0f), new Vector3(0.64f, 0.14f, 0.035f), plate);
            shoulders.AddComponent<Hitbox>().multiplier = 1f;
            Prim(PrimitiveType.Cube, "Neck", hinge, new Vector3(0f, 1.2f, 0f), new Vector3(0.1f, 0.08f, 0.03f), metal, null, false);
            var headGO = Prim(PrimitiveType.Cylinder, "Head", hinge, new Vector3(0f, 1.36f, 0f), new Vector3(0.25f, 0.018f, 0.25f), head, Quaternion.Euler(90f, 0f, 0f));
            var hb = headGO.AddComponent<Hitbox>(); hb.multiplier = 2.2f; hb.critical = true;
            Prim(PrimitiveType.Cylinder, "Bullseye", hinge, new Vector3(0f, 0.86f, 0.02f), new Vector3(0.2f, 0.004f, 0.2f), ring, Quaternion.Euler(90f, 0f, 0f), false);
            Prim(PrimitiveType.Cylinder, "BullseyeInner", hinge, new Vector3(0f, 0.86f, 0.023f), new Vector3(0.12f, 0.004f, 0.12f), plate, Quaternion.Euler(90f, 0f, 0f), false);
            Prim(PrimitiveType.Cylinder, "BullseyeDot", hinge, new Vector3(0f, 0.86f, 0.026f), new Vector3(0.05f, 0.004f, 0.05f), ring, Quaternion.Euler(90f, 0f, 0f), false);

            var t = root.AddComponent<Target>();
            t.hinge = hinge;
            t.flashRenderers = new[] { body.GetComponent<Renderer>(), shoulders.GetComponent<Renderer>(), headGO.GetComponent<Renderer>() };
            t.maxHealth = 100f;
            AddHealthBar(root.transform, t, 1.82f);

            if (moveDist > 0f)
            {
                var mt = root.AddComponent<MovingTarget>();
                mt.axis = Vector3.right;
                mt.distance = moveDist;
                mt.speed = moveSpeed;
            }
            return root;
        }

        static void MakeDummy(Transform parent, Vector3 pos)
        {
            var metal = Lit("TargetMetal", new Color(0.18f, 0.19f, 0.2f), 0.8f, 0.45f);
            var skin = Lit("Dummy", new Color(0.36f, 0.52f, 0.62f), 0.1f, 0.4f, true);
            var headM = Lit("DummyHead", new Color(0.9f, 0.86f, 0.78f), 0.1f, 0.4f, true);

            var root = new GameObject("TrainingDummy");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, 200f, 0f);
            Prim(PrimitiveType.Cylinder, "Base", root.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.7f, 0.05f, 0.7f), metal);
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(root.transform, false);
            hinge.localPosition = new Vector3(0f, 0.1f, 0f);
            Prim(PrimitiveType.Cylinder, "Pole", hinge, new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.3f, 0.08f), metal);
            var body = Prim(PrimitiveType.Capsule, "Body", hinge, new Vector3(0f, 1.0f, 0f), new Vector3(0.55f, 0.45f, 0.38f), skin);
            body.AddComponent<Hitbox>().multiplier = 1f;
            var head = Prim(PrimitiveType.Sphere, "Head", hinge, new Vector3(0f, 1.62f, 0f), Vector3.one * 0.3f, headM);
            var hb = head.AddComponent<Hitbox>(); hb.multiplier = 2.2f; hb.critical = true;
            var t = root.AddComponent<Target>();
            t.hinge = hinge;
            t.canFall = false;
            t.maxHealth = 1000f;
            t.flashRenderers = new[] { body.GetComponent<Renderer>(), head.GetComponent<Renderer>() };
            root.AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            AddHealthBar(root.transform, t, 2.0f);
        }

        static void AddHealthBar(Transform root, Target t, float height)
        {
            var bg = Unlit("HealthBg", new Color(0.05f, 0.05f, 0.05f));
            var fill = Unlit("HealthFill", new Color(0.95f, 0.3f, 0.2f));
            var bar = new GameObject("HealthBar").transform;
            bar.SetParent(root, false);
            bar.localPosition = new Vector3(0f, height, 0f);
            Prim(PrimitiveType.Quad, "Bg", bar, new Vector3(0f, 0f, 0.002f), new Vector3(0.66f, 0.075f, 1f), bg, null, false);
            var pivot = new GameObject("FillPivot").transform;
            pivot.SetParent(bar, false);
            pivot.localPosition = new Vector3(-0.31f, 0f, 0f);
            Prim(PrimitiveType.Quad, "Fill", pivot, new Vector3(0.31f, 0f, 0f), new Vector3(0.62f, 0.045f, 1f), fill, null, false);
            foreach (var r in bar.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            t.healthBarRoot = bar;
            t.healthBarFill = pivot;
        }

        // ------------------------------------------------------------------ FX

        static void BuildFXObject()
        {
            var go = new GameObject("FX");
            var fx = go.AddComponent<FX>();
            fx.sparkMaterial = Particle("FX_Spark", FX.SoftCircle(64, 2.2f), true, new Color(3.5f, 3.5f, 3.5f, 1f));
            fx.dustMaterial = Particle("FX_Dust", FX.SmokeTex(64, 5), false, Color.white);
            fx.tracerMaterial = Particle("FX_Tracer", FX.TracerTex(), true, new Color(3f, 3f, 3f, 1f));
            fx.decalMaterial = Particle("FX_Decal", FX.BulletHoleTex(64, 3), false, Color.white);
            fx.brassMaterial = Lit("Brass", new Color(0.86f, 0.64f, 0.27f), 1f, 0.72f);
        }

        // ------------------------------------------------------------------ player, weapons & viewmodels

        class VMMats
        {
            public Material gun, poly, tan, od, sleeve, cuff, glove, brass, steel, wood, reticle, glass, flashStar, flashSide, shellRed;
        }
        static VMMats vm;

        static void LoadVM()
        {
            vm = new VMMats
            {
                gun = Lit("VM_Gunmetal", new Color(0.12f, 0.125f, 0.135f), 0.8f, 0.55f),
                poly = Lit("VM_Polymer", new Color(0.05f, 0.05f, 0.055f), 0f, 0.38f),
                tan = Lit("VM_Tan", new Color(0.56f, 0.47f, 0.35f), 0f, 0.3f),
                od = Lit("VM_OD", new Color(0.27f, 0.3f, 0.21f), 0f, 0.3f),
                sleeve = Lit("VM_Sleeve", new Color(0.24f, 0.27f, 0.2f), 0f, 0.12f),
                cuff = Lit("VM_Cuff", new Color(0.16f, 0.18f, 0.14f), 0f, 0.1f),
                glove = Lit("VM_Glove", new Color(0.07f, 0.07f, 0.075f), 0f, 0.28f),
                brass = Lit("Brass", new Color(0.86f, 0.64f, 0.27f), 1f, 0.72f),
                steel = Lit("VM_Steel", new Color(0.72f, 0.74f, 0.77f), 1f, 0.82f),
                wood = Lit("VM_Wood", new Color(0.42f, 0.26f, 0.14f), 0f, 0.4f),
                shellRed = Lit("VM_ShellRed", new Color(0.7f, 0.1f, 0.08f), 0.2f, 0.5f),
                reticle = Unlit("VM_Reticle", new Color(4f, 0.12f, 0.09f)), // dimmer: less bloom halo around the dot
                glass = Particle("VM_Glass", FX.SoftCircle(8, 0f), false, new Color(0.45f, 0.75f, 1f, 0.1f)),
                flashStar = Particle("VM_FlashStar", FX.MuzzleStarTex(128), true, new Color(4f, 3.2f, 2.4f, 1f), true),
                flashSide = Particle("VM_FlashSide", FX.SoftCircle(64, 1.4f), true, new Color(5f, 2.4f, 0.8f, 1f), true),
            };
        }

        static GameObject BuildPlayer()
        {
            LoadVM();
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.05f, -3f);
            var cc = player.AddComponent<CharacterController>();
            cc.radius = 0.35f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 50f;
            cc.skinWidth = 0.04f;
            cc.minMoveDistance = 0f;
            var motor = player.AddComponent<PlayerMotor>();

            var camRoot = new GameObject("CameraRoot").transform;
            camRoot.SetParent(player.transform, false);
            camRoot.localPosition = new Vector3(0f, 1.68f, 0f);
            var recoil = new GameObject("Recoil").transform;
            recoil.SetParent(camRoot, false);
            var fxPivot = new GameObject("CameraFX").transform;
            fxPivot.SetParent(recoil, false);

            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(fxPivot, false);
            var cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 500f;
            cam.fieldOfView = 64f;
            camGO.AddComponent<AudioListener>();
            var acd = camGO.AddComponent<UniversalAdditionalCameraData>();
            acd.renderPostProcessing = true;
            acd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            acd.antialiasingQuality = AntialiasingQuality.High;

            var pc = player.AddComponent<PlayerCamera>();
            pc.motor = motor;
            pc.pitchPivot = camRoot;
            pc.recoilPivot = recoil;
            pc.fxPivot = fxPivot;
            pc.cam = cam;
            motor.cameraRoot = camRoot;

            // ---- weapons
            var weaponsRoot = new GameObject("Weapons").transform;
            weaponsRoot.SetParent(camGO.transform, false);
            var list = new List<WeaponController>();
            foreach (var def in WeaponCatalog.All)
            {
                System.Action<Transform, WeaponController> build = def.archetype switch
                {
                    WeaponArchetype.SMG => BuildSMG,
                    WeaponArchetype.Shotgun => BuildShotgun,
                    WeaponArchetype.DMR => BuildDMR,
                    WeaponArchetype.Pistol => BuildPistol,
                    _ => BuildAR,
                };
                var d = def;
                list.Add(MakeWeapon(d.name, weaponsRoot, motor, pc, cam, build, w => d.Apply(w)));
            }
            foreach (var w in list) w.gameObject.SetActive(w.weaponName == "M4A1");

            // ---- melee & grenade viewmodels
            var knifeArm = BuildKnifeArm(camGO.transform);
            var execArm = BuildExecLeftArm(camGO.transform);
            Transform grenadeInHand;
            var grenadeArm = BuildGrenadeArm(camGO.transform, out grenadeInHand);

            var loadout = player.AddComponent<Loadout>();
            loadout.motor = motor; loadout.playerCam = pc; loadout.cam = cam;
            loadout.weapons = list.ToArray();
            loadout.primaryName = "M4A1"; loadout.secondaryName = "P-17";
            loadout.grenadeArm = grenadeArm; loadout.grenadeInHand = grenadeInHand;

            var melee = player.AddComponent<MeleeController>();
            melee.motor = motor; melee.playerCam = pc; melee.cam = cam; melee.loadout = loadout;
            melee.knifeArm = knifeArm; melee.execLeftArm = execArm;
            loadout.melee = melee;

            // ---- full body (shadow only in first person, ready for multiplayer replication)
            var rig = HumanoidRig.Build(player.transform, PlayerPalette(), false);
            var anim = rig.gameObject.AddComponent<CharacterAnimator>();
            anim.rig = rig;
            var body = player.AddComponent<PlayerBody>();
            body.motor = motor; body.playerCam = pc; body.loadout = loadout; body.melee = melee; body.anim = anim; body.rig = rig;

            var combatant = player.AddComponent<Combatant>();
            combatant.displayName = "Вы";
            combatant.isPlayer = true;
            var health = player.AddComponent<PlayerHealth>();
            health.motor = motor; health.playerCam = pc; health.loadout = loadout;
            return player;
        }

        static WeaponController MakeWeapon(string name, Transform parent, PlayerMotor motor, PlayerCamera pc, Camera cam,
                                           System.Action<Transform, WeaponController> build, System.Action<WeaponController> setup)
        {
            var holder = new GameObject("W_" + name).transform;
            holder.SetParent(parent, false);
            var w = holder.gameObject.AddComponent<WeaponController>();
            w.weaponName = name;
            w.motor = motor; w.playerCam = pc; w.cam = cam;
            setup(w);
            var model = new GameObject(name + "_Model").transform;
            model.SetParent(holder, false);
            w.model = model;
            build(model, w);
            holder.localPosition = w.hipPosition;
            return w;
        }

        static Transform Part(string name, Transform parent, Vector3 pos, Vector3 size, Material m, Vector3? euler = null, PrimitiveType type = PrimitiveType.Cube)
        {
            var go = Prim(type, name, parent, pos, size, m, Quaternion.Euler(euler ?? Vector3.zero), false);
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        static Transform Cyl(string name, Transform parent, Vector3 pos, float diameter, float length, Material m)
            => Part(name, parent, pos, new Vector3(diameter, length * 0.5f, diameter), m, new Vector3(90f, 0f, 0f), PrimitiveType.Cylinder);

        static void Limb(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material m)
        {
            Vector3 d = b - a;
            var t = Part(name, parent, (a + b) * 0.5f, new Vector3(radius * 2f, d.magnitude * 0.5f + radius * 0.5f, radius * 2f), m, null, PrimitiveType.Capsule);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        }

        static Transform Anchor(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static void Muzzle(Transform model, WeaponController w, Vector3 pos, float scale)
        {
            var muzzle = Anchor("Muzzle", model, pos);
            var flash = Anchor("MuzzleFlash", muzzle, Vector3.zero);
            Part("FlashFront", flash, new Vector3(0f, 0f, 0.015f), new Vector3(0.15f, 0.15f, 1f) * scale, vm.flashStar, null, PrimitiveType.Quad);
            Part("FlashSideA", flash, new Vector3(0f, 0f, 0.11f * scale), new Vector3(0.26f * scale, 0.075f * scale, 1f), vm.flashSide, new Vector3(0f, 90f, 0f), PrimitiveType.Quad);
            Part("FlashSideB", flash, new Vector3(0f, 0f, 0.11f * scale), new Vector3(0.075f * scale, 0.26f * scale, 1f), vm.flashSide, new Vector3(90f, 0f, 0f), PrimitiveType.Quad);
            var lightGO = new GameObject("MuzzleLight");
            lightGO.transform.SetParent(muzzle, false);
            lightGO.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 6f;
            light.color = new Color(1f, 0.68f, 0.38f);
            light.intensity = 4f;
            light.shadows = LightShadows.None;
            w.muzzle = muzzle;
            w.muzzleFlash = flash.gameObject;
            w.muzzleLight = light;
        }

        static void Arms(Transform model, WeaponController w, Vector3 rGrip, Vector3 lGrip, bool pistol)
        {
            var rArm = Anchor("RightArm", model, Vector3.zero);
            Part("RightGlove", rArm, rGrip, new Vector3(0.052f, 0.066f, 0.085f), vm.glove, new Vector3(18f, 0f, 0f));
            Part("RightFingers", rArm, rGrip + new Vector3(-0.008f, 0.003f, 0.042f), new Vector3(0.05f, 0.05f, 0.03f), vm.glove, new Vector3(18f, 0f, 0f));
            Part("RightThumb", rArm, rGrip + new Vector3(-0.034f, 0.048f, 0.012f), new Vector3(0.016f, 0.018f, 0.055f), vm.glove, new Vector3(0f, 10f, 0f));
            Vector3 wrist = rGrip + new Vector3(0.008f, -0.027f, -0.048f);
            Vector3 cuffEnd = wrist + new Vector3(0.018f, -0.03f, -0.06f);
            Vector3 elbow = wrist + new Vector3(0.088f, -0.155f, -0.33f);
            Limb("RightCuff", rArm, wrist, cuffEnd, 0.04f, vm.cuff);
            Limb("RightForearm", rArm, cuffEnd, elbow, 0.043f, vm.sleeve);
            Limb("RightUpperArm", rArm, elbow, elbow + new Vector3(0.12f, -0.15f, -0.2f), 0.055f, vm.sleeve);

            var lArm = Anchor("LeftArm", model, Vector3.zero);
            Vector3 lw, le;
            if (!pistol)
            {
                Part("LeftGlove", lArm, lGrip, new Vector3(0.074f, 0.045f, 0.1f), vm.glove);
                Part("LeftFingers", lArm, lGrip + new Vector3(0.03f, 0.028f, 0.01f), new Vector3(0.022f, 0.04f, 0.085f), vm.glove);
                Part("LeftThumb", lArm, lGrip + new Vector3(-0.028f, 0.036f, -0.015f), new Vector3(0.016f, 0.02f, 0.055f), vm.glove);
                lw = lGrip + new Vector3(-0.017f, -0.019f, -0.05f);
                le = lw + new Vector3(-0.145f, -0.155f, -0.27f);
            }
            else
            {
                Part("LeftGlove", lArm, lGrip, new Vector3(0.05f, 0.07f, 0.08f), vm.glove, new Vector3(18f, 0f, -15f));
                Part("LeftThumb", lArm, lGrip + new Vector3(0.006f, 0.045f, 0.03f), new Vector3(0.015f, 0.018f, 0.05f), vm.glove);
                lw = lGrip + new Vector3(-0.02f, -0.03f, -0.045f);
                le = lw + new Vector3(-0.14f, -0.13f, -0.28f);
            }
            Vector3 lc = lw + new Vector3(-0.03f, -0.03f, -0.06f);
            Limb("LeftCuff", lArm, lw, lc, 0.038f, vm.cuff);
            Limb("LeftForearm", lArm, lc, le, 0.04f, vm.sleeve);
            Limb("LeftUpperArm", lArm, le, le + new Vector3(-0.15f, -0.15f, -0.32f), 0.055f, vm.sleeve);
            w.leftHand = lArm;
            w.leftHandGrip = Anchor("LeftGrip", lArm, lGrip);
        }

        static void HoloSight(Transform model, WeaponController w, float y, float z)
        {
            Part("SightBase", model, new Vector3(0f, y - 0.026f, z - 0.01f), new Vector3(0.042f, 0.012f, 0.08f), vm.poly);
            Part("SightPostL", model, new Vector3(-0.022f, y + 0.002f, z), new Vector3(0.006f, 0.046f, 0.055f), vm.poly);
            Part("SightPostR", model, new Vector3(0.022f, y + 0.002f, z), new Vector3(0.006f, 0.046f, 0.055f), vm.poly);
            Part("SightHood", model, new Vector3(0f, y + 0.028f, z), new Vector3(0.05f, 0.007f, 0.055f), vm.poly);
            Part("SightButtons", model, new Vector3(0.026f, y - 0.018f, z - 0.03f), new Vector3(0.006f, 0.008f, 0.02f), vm.gun);
            Part("SightGlass", model, new Vector3(0f, y + 0.002f, z + 0.026f), new Vector3(0.038f, 0.044f, 1f), vm.glass, null, PrimitiveType.Quad);
            w.reticle = Part("Reticle", model, new Vector3(0f, y, z + 0.02f), Vector3.one * 0.0009f, vm.reticle, null, PrimitiveType.Sphere);
            w.sightPoint = Anchor("SightPoint", model, new Vector3(0f, y, z + 0.02f));
        }

        static Transform Magazine(Transform model, Vector3 pivot, Vector3 bodySize, float tilt, Material body, Material plate)
        {
            var mag = Anchor("Magazine", model, pivot);
            Part("MagBody", mag, new Vector3(0f, -bodySize.y * 0.5f, 0.006f), bodySize, body, new Vector3(-tilt, 0f, 0f));
            Part("MagBase", mag, new Vector3(0f, -bodySize.y - 0.002f, 0.006f + Mathf.Sin(tilt * Mathf.Deg2Rad) * bodySize.y), new Vector3(bodySize.x + 0.004f, 0.012f, bodySize.z + 0.006f), plate, new Vector3(-tilt, 0f, 0f));
            Part("MagRound", mag, new Vector3(0f, 0.004f, 0f), new Vector3(0.012f, 0.008f, 0.04f), vm.brass);
            return mag;
        }

        // ---------------- M4A1 (assault rifle, holo sight)
        static void BuildAR(Transform model, WeaponController w)
        {
            Part("Receiver", model, new Vector3(0f, 0.03f, 0.06f), new Vector3(0.055f, 0.075f, 0.3f), vm.gun);
            Part("LowerReceiver", model, new Vector3(0f, -0.005f, 0.04f), new Vector3(0.05f, 0.03f, 0.22f), vm.gun);
            Part("TopRail", model, new Vector3(0f, 0.0775f, 0.07f), new Vector3(0.03f, 0.02f, 0.3f), vm.poly);
            for (int i = 0; i < 9; i++)
                Part("RailTooth" + i, model, new Vector3(0f, 0.0885f, -0.06f + i * 0.033f), new Vector3(0.032f, 0.004f, 0.012f), vm.poly);
            Part("Handguard", model, new Vector3(0f, 0.035f, 0.36f), new Vector3(0.062f, 0.07f, 0.3f), vm.tan);
            Part("HandguardRail", model, new Vector3(0f, 0.075f, 0.36f), new Vector3(0.028f, 0.012f, 0.3f), vm.poly);
            for (int i = 0; i < 4; i++)
            {
                Part("VentL" + i, model, new Vector3(-0.0315f, 0.035f, 0.26f + i * 0.065f), new Vector3(0.002f, 0.03f, 0.035f), vm.poly);
                Part("VentR" + i, model, new Vector3(0.0315f, 0.035f, 0.26f + i * 0.065f), new Vector3(0.002f, 0.03f, 0.035f), vm.poly);
            }
            Cyl("Barrel", model, new Vector3(0f, 0.04f, 0.6f), 0.022f, 0.18f, vm.gun);
            Cyl("GasBlock", model, new Vector3(0f, 0.045f, 0.535f), 0.03f, 0.03f, vm.gun);
            Cyl("MuzzleBrake", model, new Vector3(0f, 0.04f, 0.72f), 0.034f, 0.07f, vm.poly);
            Cyl("BufferTube", model, new Vector3(0f, 0.045f, -0.18f), 0.03f, 0.18f, vm.gun);
            Part("Stock", model, new Vector3(0f, 0.025f, -0.3f), new Vector3(0.045f, 0.085f, 0.16f), vm.tan);
            Part("ButtPad", model, new Vector3(0f, 0.005f, -0.385f), new Vector3(0.05f, 0.13f, 0.025f), vm.poly);
            Part("PistolGrip", model, new Vector3(0f, -0.055f, -0.035f), new Vector3(0.035f, 0.1f, 0.045f), vm.poly, new Vector3(18f, 0f, 0f));
            Part("TriggerGuard", model, new Vector3(0f, -0.03f, 0.03f), new Vector3(0.008f, 0.006f, 0.075f), vm.gun);
            Part("MagWell", model, new Vector3(0f, -0.02f, 0.1f), new Vector3(0.042f, 0.045f, 0.082f), vm.gun);
            w.magazine = Magazine(model, new Vector3(0f, -0.02f, 0.1f), new Vector3(0.03f, 0.15f, 0.068f), 10f, vm.poly, vm.tan);
            w.chargingHandle = Part("ChargingHandle", model, new Vector3(0f, 0.07f, -0.075f), new Vector3(0.05f, 0.012f, 0.022f), vm.poly);
            w.bolt = Part("Bolt", model, new Vector3(0.029f, 0.04f, 0.07f), new Vector3(0.004f, 0.026f, 0.055f), vm.brass);
            w.ejectPort = Anchor("EjectPort", model, new Vector3(0.045f, 0.045f, 0.07f));
            HoloSight(model, w, 0.122f, 0.06f);
            Muzzle(model, w, new Vector3(0f, 0.04f, 0.76f), 1f);
            Arms(model, w, new Vector3(0.004f, -0.048f, -0.042f), new Vector3(-0.008f, -0.006f, 0.36f), false);
        }

        // ---------------- SMG-9 (suppressed SMG, reflex sight, vertical grip)
        static void BuildSMG(Transform model, WeaponController w)
        {
            Part("Receiver", model, new Vector3(0f, 0.025f, 0.06f), new Vector3(0.05f, 0.075f, 0.26f), vm.gun);
            Part("TopRail", model, new Vector3(0f, 0.07f, 0.06f), new Vector3(0.026f, 0.016f, 0.24f), vm.poly);
            Part("LowerFrame", model, new Vector3(0f, -0.018f, 0.12f), new Vector3(0.046f, 0.02f, 0.2f), vm.poly);
            Cyl("Suppressor", model, new Vector3(0f, 0.035f, 0.32f), 0.042f, 0.2f, vm.poly);
            Cyl("SuppressorCap", model, new Vector3(0f, 0.035f, 0.425f), 0.036f, 0.012f, vm.gun);
            Part("PistolGrip", model, new Vector3(0f, -0.055f, -0.03f), new Vector3(0.034f, 0.1f, 0.045f), vm.poly, new Vector3(18f, 0f, 0f));
            Part("TriggerGuard", model, new Vector3(0f, -0.024f, 0.02f), new Vector3(0.008f, 0.006f, 0.065f), vm.gun);
            Part("Foregrip", model, new Vector3(0f, -0.06f, 0.2f), new Vector3(0.028f, 0.075f, 0.032f), vm.poly);
            Part("StockRodL", model, new Vector3(-0.016f, 0.03f, -0.17f), new Vector3(0.006f, 0.006f, 0.2f), vm.gun);
            Part("StockRodR", model, new Vector3(0.016f, 0.03f, -0.17f), new Vector3(0.006f, 0.006f, 0.2f), vm.gun);
            Part("ButtPad", model, new Vector3(0f, 0.012f, -0.275f), new Vector3(0.042f, 0.09f, 0.02f), vm.poly);
            w.magazine = Magazine(model, new Vector3(0f, -0.012f, 0.085f), new Vector3(0.026f, 0.17f, 0.05f), 0f, vm.poly, vm.gun);
            w.chargingHandle = Part("ChargingHandle", model, new Vector3(-0.03f, 0.04f, 0.13f), new Vector3(0.012f, 0.012f, 0.02f), vm.gun);
            w.bolt = Part("Bolt", model, new Vector3(0.026f, 0.035f, 0.06f), new Vector3(0.004f, 0.022f, 0.04f), vm.brass);
            w.ejectPort = Anchor("EjectPort", model, new Vector3(0.04f, 0.04f, 0.06f));
            // reflex sight
            Part("ReflexBase", model, new Vector3(0f, 0.084f, 0.05f), new Vector3(0.03f, 0.01f, 0.05f), vm.poly);
            Part("ReflexPostL", model, new Vector3(-0.016f, 0.104f, 0.065f), new Vector3(0.004f, 0.034f, 0.012f), vm.poly);
            Part("ReflexPostR", model, new Vector3(0.016f, 0.104f, 0.065f), new Vector3(0.004f, 0.034f, 0.012f), vm.poly);
            Part("ReflexTop", model, new Vector3(0f, 0.122f, 0.065f), new Vector3(0.036f, 0.004f, 0.012f), vm.poly);
            Part("ReflexGlass", model, new Vector3(0f, 0.104f, 0.07f), new Vector3(0.028f, 0.032f, 1f), vm.glass, null, PrimitiveType.Quad);
            w.reticle = Part("Reticle", model, new Vector3(0f, 0.104f, 0.066f), Vector3.one * 0.0009f, vm.reticle, null, PrimitiveType.Sphere);
            w.sightPoint = Anchor("SightPoint", model, new Vector3(0f, 0.104f, 0.066f));
            Muzzle(model, w, new Vector3(0f, 0.035f, 0.44f), 0.45f);
            Arms(model, w, new Vector3(0.004f, -0.05f, -0.035f), new Vector3(-0.004f, -0.05f, 0.2f), false);
        }

        // ---------------- M870 (pump shotgun, bead sight, shell-by-shell)
        static void BuildShotgun(Transform model, WeaponController w)
        {
            Part("Receiver", model, new Vector3(0f, 0.03f, 0.04f), new Vector3(0.056f, 0.075f, 0.22f), vm.gun);
            Cyl("Barrel", model, new Vector3(0f, 0.056f, 0.46f), 0.026f, 0.62f, vm.gun);
            Part("Rib", model, new Vector3(0f, 0.071f, 0.46f), new Vector3(0.008f, 0.004f, 0.6f), vm.gun);
            Cyl("MagTube", model, new Vector3(0f, 0.018f, 0.38f), 0.024f, 0.5f, vm.gun);
            Cyl("MagCap", model, new Vector3(0f, 0.018f, 0.635f), 0.03f, 0.03f, vm.gun);
            var pump = Part("Pump", model, new Vector3(0f, 0.016f, 0.36f), new Vector3(0.062f, 0.052f, 0.18f), vm.wood);
            for (int i = 0; i < 5; i++)
                Part("PumpRib" + i, pump, new Vector3(-0.51f, 0f, -0.35f + i * 0.17f), new Vector3(0.05f, 0.85f, 0.06f), vm.poly);
            w.chargingHandle = pump;
            Part("Stock", model, new Vector3(0f, -0.005f, -0.24f), new Vector3(0.045f, 0.095f, 0.3f), vm.wood, new Vector3(6f, 0f, 0f));
            Part("Wrist", model, new Vector3(0f, -0.035f, -0.065f), new Vector3(0.036f, 0.075f, 0.06f), vm.wood, new Vector3(28f, 0f, 0f));
            Part("ButtPad", model, new Vector3(0f, -0.022f, -0.4f), new Vector3(0.05f, 0.12f, 0.025f), vm.poly, new Vector3(6f, 0f, 0f));
            Part("TriggerGuard", model, new Vector3(0f, -0.022f, 0.03f), new Vector3(0.008f, 0.006f, 0.07f), vm.gun);
            Part("Bead", model, new Vector3(0f, 0.077f, 0.765f), Vector3.one * 0.007f, vm.brass, null, PrimitiveType.Sphere);
            Part("RearNotchL", model, new Vector3(-0.006f, 0.078f, 0.0f), new Vector3(0.004f, 0.01f, 0.01f), vm.gun);
            Part("RearNotchR", model, new Vector3(0.006f, 0.078f, 0.0f), new Vector3(0.004f, 0.01f, 0.01f), vm.gun);
            w.sightPoint = Anchor("SightPoint", model, new Vector3(0f, 0.077f, 0.0f));
            w.ejectPort = Anchor("EjectPort", model, new Vector3(0.04f, 0.045f, 0.06f));
            w.loadPort = Anchor("LoadPort", model, new Vector3(0f, -0.01f, 0.07f));
            Muzzle(model, w, new Vector3(0f, 0.056f, 0.78f), 1.3f);
            Vector3 lGrip = new Vector3(-0.006f, -0.012f, 0.36f);
            Arms(model, w, new Vector3(0.004f, -0.055f, -0.06f), lGrip, false);
            var shell = Anchor("ShellInHand", w.leftHand, lGrip + new Vector3(0.0f, 0.03f, 0.05f));
            Part("ShellBody", shell, Vector3.zero, new Vector3(0.022f, 0.026f, 0.022f), vm.shellRed, new Vector3(90f, 0f, 0f), PrimitiveType.Cylinder);
            Part("ShellBase", shell, new Vector3(0f, 0f, -0.03f), new Vector3(0.024f, 0.006f, 0.024f), vm.brass, new Vector3(90f, 0f, 0f), PrimitiveType.Cylinder);
            w.magazine = shell;
        }

        // ---------------- DMR-7 (marksman rifle, 6x scope)
        static void BuildDMR(Transform model, WeaponController w)
        {
            Part("Receiver", model, new Vector3(0f, 0.03f, 0.06f), new Vector3(0.058f, 0.08f, 0.32f), vm.od);
            Part("LowerReceiver", model, new Vector3(0f, -0.006f, 0.04f), new Vector3(0.052f, 0.032f, 0.24f), vm.od);
            Part("TopRail", model, new Vector3(0f, 0.079f, 0.08f), new Vector3(0.03f, 0.02f, 0.4f), vm.poly);
            Part("Handguard", model, new Vector3(0f, 0.035f, 0.4f), new Vector3(0.064f, 0.072f, 0.36f), vm.od);
            Cyl("Barrel", model, new Vector3(0f, 0.04f, 0.72f), 0.024f, 0.3f, vm.gun);
            Cyl("MuzzleBrake", model, new Vector3(0f, 0.04f, 0.9f), 0.036f, 0.08f, vm.poly);
            Part("Stock", model, new Vector3(0f, 0.02f, -0.29f), new Vector3(0.048f, 0.1f, 0.26f), vm.od);
            Part("CheekRiser", model, new Vector3(0f, 0.08f, -0.29f), new Vector3(0.04f, 0.03f, 0.16f), vm.poly);
            Part("ButtPad", model, new Vector3(0f, 0.005f, -0.43f), new Vector3(0.052f, 0.14f, 0.025f), vm.poly);
            Part("PistolGrip", model, new Vector3(0f, -0.058f, -0.035f), new Vector3(0.036f, 0.1f, 0.046f), vm.poly, new Vector3(18f, 0f, 0f));
            Part("TriggerGuard", model, new Vector3(0f, -0.032f, 0.03f), new Vector3(0.008f, 0.006f, 0.075f), vm.gun);
            Part("MagWell", model, new Vector3(0f, -0.022f, 0.1f), new Vector3(0.044f, 0.045f, 0.084f), vm.od);
            w.magazine = Magazine(model, new Vector3(0f, -0.022f, 0.1f), new Vector3(0.032f, 0.1f, 0.072f), 4f, vm.poly, vm.poly);
            w.chargingHandle = Part("ChargingHandle", model, new Vector3(0f, 0.072f, -0.085f), new Vector3(0.05f, 0.012f, 0.022f), vm.poly);
            w.bolt = Part("Bolt", model, new Vector3(0.03f, 0.04f, 0.07f), new Vector3(0.004f, 0.028f, 0.06f), vm.brass);
            w.ejectPort = Anchor("EjectPort", model, new Vector3(0.046f, 0.046f, 0.07f));
            // scope
            Part("RingRear", model, new Vector3(0f, 0.1f, 0.0f), new Vector3(0.032f, 0.03f, 0.02f), vm.gun);
            Part("RingFront", model, new Vector3(0f, 0.1f, 0.13f), new Vector3(0.032f, 0.03f, 0.02f), vm.gun);
            Cyl("ScopeTube", model, new Vector3(0f, 0.125f, 0.065f), 0.036f, 0.3f, vm.poly);
            Cyl("ScopeObjective", model, new Vector3(0f, 0.125f, 0.24f), 0.056f, 0.07f, vm.poly);
            Cyl("ScopeEyepiece", model, new Vector3(0f, 0.125f, -0.1f), 0.046f, 0.06f, vm.poly);
            Part("ScopeTurretTop", model, new Vector3(0f, 0.15f, 0.065f), new Vector3(0.024f, 0.02f, 0.024f), vm.gun, null, PrimitiveType.Cylinder);
            Part("ScopeTurretSide", model, new Vector3(0.025f, 0.125f, 0.065f), new Vector3(0.024f, 0.012f, 0.024f), vm.gun, new Vector3(0f, 0f, 90f), PrimitiveType.Cylinder);
            Part("ScopeLens", model, new Vector3(0f, 0.125f, 0.277f), new Vector3(0.05f, 0.05f, 1f), vm.glass, null, PrimitiveType.Quad);
            w.sightPoint = Anchor("SightPoint", model, new Vector3(0f, 0.125f, -0.13f));
            Muzzle(model, w, new Vector3(0f, 0.04f, 0.945f), 1.1f);
            Arms(model, w, new Vector3(0.004f, -0.05f, -0.042f), new Vector3(-0.008f, -0.006f, 0.42f), false);
        }

        // ---------------- P-17 (pistol)
        static void BuildPistol(Transform model, WeaponController w)
        {
            var slide = Part("Slide", model, new Vector3(0f, 0.036f, 0.035f), new Vector3(0.03f, 0.034f, 0.19f), vm.gun);
            Part("SlideSerrations", slide, new Vector3(0f, 0f, -0.38f), new Vector3(1.04f, 0.8f, 0.15f), vm.poly);
            Part("EjectionPort", slide, new Vector3(0.5f, 0.15f, 0.05f), new Vector3(0.05f, 0.45f, 0.25f), vm.brass);
            Part("RearSightL", slide, new Vector3(-0.2f, 0.62f, -0.45f), new Vector3(0.14f, 0.25f, 0.06f), vm.poly);
            Part("RearSightR", slide, new Vector3(0.2f, 0.62f, -0.45f), new Vector3(0.14f, 0.25f, 0.06f), vm.poly);
            Part("FrontSight", slide, new Vector3(0f, 0.62f, 0.45f), new Vector3(0.12f, 0.25f, 0.04f), vm.poly);
            Part("FrontDot", slide, new Vector3(0f, 0.7f, 0.465f), new Vector3(0.09f, 0.09f, 0.01f), vm.reticle, null, PrimitiveType.Sphere);
            w.chargingHandle = slide;
            Part("Frame", model, new Vector3(0f, 0.008f, 0.025f), new Vector3(0.028f, 0.025f, 0.16f), vm.poly);
            Part("Grip", model, new Vector3(0f, -0.05f, -0.035f), new Vector3(0.031f, 0.11f, 0.05f), vm.poly, new Vector3(15f, 0f, 0f));
            Part("TriggerGuard", model, new Vector3(0f, -0.016f, 0.04f), new Vector3(0.008f, 0.006f, 0.055f), vm.poly);
            Part("Trigger", model, new Vector3(0f, -0.008f, 0.035f), new Vector3(0.005f, 0.018f, 0.005f), vm.gun);
            var mag = Anchor("Magazine", model, new Vector3(0f, -0.01f, -0.03f));
            Part("MagBody", mag, new Vector3(0f, -0.05f, -0.008f), new Vector3(0.024f, 0.1f, 0.04f), vm.gun, new Vector3(15f, 0f, 0f));
            Part("MagBase", mag, new Vector3(0f, -0.103f, -0.022f), new Vector3(0.033f, 0.012f, 0.052f), vm.poly, new Vector3(15f, 0f, 0f));
            w.magazine = mag;
            w.sightPoint = Anchor("SightPoint", model, new Vector3(0f, 0.06f, -0.05f));
            w.ejectPort = Anchor("EjectPort", model, new Vector3(0.03f, 0.045f, 0.04f));
            Muzzle(model, w, new Vector3(0f, 0.036f, 0.135f), 0.8f);
            Arms(model, w, new Vector3(0.0f, -0.045f, -0.04f), new Vector3(-0.024f, -0.06f, -0.022f), true);
        }

        // ---------------- knife / grenade / execution arms (children of the camera)
        static Transform BuildKnifeArm(Transform cam)
        {
            var ka = Anchor("KnifeArm", cam, new Vector3(0.3f, -0.5f, 0.1f));
            Part("Glove", ka, Vector3.zero, new Vector3(0.055f, 0.07f, 0.09f), vm.glove);
            Part("Fingers", ka, new Vector3(-0.004f, 0.0f, 0.045f), new Vector3(0.05f, 0.06f, 0.03f), vm.glove);
            Part("Handle", ka, new Vector3(0f, 0.004f, 0.06f), new Vector3(0.022f, 0.026f, 0.12f), vm.poly);
            Part("Guard", ka, new Vector3(0f, 0.004f, 0.12f), new Vector3(0.03f, 0.045f, 0.008f), vm.gun);
            Part("Blade", ka, new Vector3(0f, 0.01f, 0.2f), new Vector3(0.005f, 0.032f, 0.15f), vm.steel);
            Part("BladeTip", ka, new Vector3(0f, 0.004f, 0.284f), new Vector3(0.005f, 0.02f, 0.03f), vm.steel, new Vector3(25f, 0f, 0f));
            Part("Spine", ka, new Vector3(0f, 0.027f, 0.19f), new Vector3(0.006f, 0.004f, 0.13f), vm.gun);
            Limb("Cuff", ka, new Vector3(0.006f, -0.025f, -0.045f), new Vector3(0.02f, -0.055f, -0.1f), 0.04f, vm.cuff);
            Limb("Forearm", ka, new Vector3(0.02f, -0.055f, -0.1f), new Vector3(0.07f, -0.17f, -0.42f), 0.043f, vm.sleeve);
            ka.gameObject.SetActive(false);
            return ka;
        }

        static Transform BuildExecLeftArm(Transform cam)
        {
            var la = Anchor("ExecLeftArm", cam, new Vector3(-0.3f, -0.5f, 0.1f));
            Part("Glove", la, Vector3.zero, new Vector3(0.08f, 0.05f, 0.1f), vm.glove);
            Part("Fingers", la, new Vector3(0.0f, 0.015f, 0.065f), new Vector3(0.075f, 0.03f, 0.05f), vm.glove, new Vector3(30f, 0f, 0f));
            Part("Thumb", la, new Vector3(0.04f, 0.02f, 0.01f), new Vector3(0.018f, 0.02f, 0.05f), vm.glove);
            Limb("Cuff", la, new Vector3(-0.01f, -0.015f, -0.05f), new Vector3(-0.03f, -0.035f, -0.1f), 0.04f, vm.cuff);
            Limb("Forearm", la, new Vector3(-0.03f, -0.035f, -0.1f), new Vector3(-0.1f, -0.14f, -0.4f), 0.043f, vm.sleeve);
            la.gameObject.SetActive(false);
            return la;
        }

        static Transform BuildGrenadeArm(Transform cam, out Transform grenadeInHand)
        {
            var ga = Anchor("GrenadeArm", cam, new Vector3(0.28f, -0.5f, 0.2f));
            Part("Glove", ga, Vector3.zero, new Vector3(0.055f, 0.07f, 0.09f), vm.glove);
            var g = Anchor("GrenadeInHand", ga, new Vector3(-0.01f, 0.045f, 0.035f));
            Part("Body", g, Vector3.zero, new Vector3(0.075f, 0.09f, 0.075f), vm.od, null, PrimitiveType.Sphere);
            Part("Top", g, new Vector3(0f, 0.048f, 0f), new Vector3(0.028f, 0.012f, 0.028f), vm.steel, null, PrimitiveType.Cylinder);
            Part("Lever", g, new Vector3(0.022f, 0.02f, 0f), new Vector3(0.008f, 0.06f, 0.014f), vm.steel);
            Part("Ring", g, new Vector3(-0.02f, 0.05f, 0f), new Vector3(0.025f, 0.003f, 0.025f), vm.steel, new Vector3(0f, 0f, 90f), PrimitiveType.Cylinder);
            Part("Thumb", ga, new Vector3(-0.034f, 0.03f, 0.02f), new Vector3(0.016f, 0.018f, 0.05f), vm.glove);
            Limb("Cuff", ga, new Vector3(0.006f, -0.025f, -0.045f), new Vector3(0.02f, -0.055f, -0.1f), 0.04f, vm.cuff);
            Limb("Forearm", ga, new Vector3(0.02f, -0.055f, -0.1f), new Vector3(0.07f, -0.17f, -0.42f), 0.043f, vm.sleeve);
            ga.gameObject.SetActive(false);
            grenadeInHand = g;
            return ga;
        }

        // ------------------------------------------------------------------ characters

        static HumanoidRig.Palette PlayerPalette() => new HumanoidRig.Palette
        {
            skin = Lit("Char_Skin", new Color(0.78f, 0.6f, 0.48f), 0f, 0.35f, true),
            shirt = Lit("Char_PlayerShirt", new Color(0.46f, 0.43f, 0.33f), 0f, 0.15f, true),
            pants = Lit("Char_PlayerPants", new Color(0.36f, 0.34f, 0.27f), 0f, 0.15f, true),
            vest = Lit("Char_PlayerVest", new Color(0.26f, 0.29f, 0.2f), 0f, 0.2f, true),
            boots = Lit("Char_Boots", new Color(0.2f, 0.15f, 0.1f), 0f, 0.3f, true),
            gloves = Lit("Char_Gloves", new Color(0.07f, 0.07f, 0.075f), 0f, 0.28f, true),
            helmet = Lit("Char_PlayerHelmet", new Color(0.28f, 0.31f, 0.22f), 0.1f, 0.35f, true),
            gear = Lit("Char_Gear", new Color(0.14f, 0.14f, 0.13f), 0f, 0.25f, true),
        };

        static HumanoidRig.Palette BotPalette() => new HumanoidRig.Palette
        {
            skin = Lit("Char_Skin", new Color(0.78f, 0.6f, 0.48f), 0f, 0.35f, true),
            shirt = Lit("Char_BotShirt", new Color(0.22f, 0.25f, 0.31f), 0f, 0.15f, true),
            pants = Lit("Char_BotPants", new Color(0.17f, 0.19f, 0.23f), 0f, 0.15f, true),
            vest = Lit("Char_BotVest", new Color(0.09f, 0.09f, 0.1f), 0f, 0.25f, true),
            boots = Lit("Char_Boots", new Color(0.2f, 0.15f, 0.1f), 0f, 0.3f, true),
            gloves = Lit("Char_Gloves", new Color(0.07f, 0.07f, 0.075f), 0f, 0.28f, true),
            helmet = Lit("Char_BotHelmet", new Color(0.1f, 0.1f, 0.11f), 0.2f, 0.4f, true),
            gear = Lit("Char_BotGear", new Color(0.55f, 0.13f, 0.1f), 0f, 0.3f, true),
        };

        static void MakeBot(Transform parent, Vector3 pos, float yaw, Vector3[] waypoints)
        {
            var root = new GameObject("TrainingBot");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var rig = HumanoidRig.Build(root.transform, BotPalette(), true);
            var anim = root.AddComponent<CharacterAnimator>();
            anim.rig = rig;
            var bot = root.AddComponent<TrainingBot>();
            bot.rig = rig;
            bot.anim = anim;
            bot.guardYaw = yaw;
            bot.waypoints = waypoints;
            var gun = Anchor("Rifle3P", rig.weaponSocket, Vector3.zero);
            var dark = Lit("VM_Polymer", new Color(0.05f, 0.05f, 0.055f), 0f, 0.38f);
            var metal = Lit("VM_Gunmetal", new Color(0.12f, 0.125f, 0.135f), 0.8f, 0.55f);
            Prim(PrimitiveType.Cube, "Body", gun, new Vector3(0f, 0.02f, 0.05f), new Vector3(0.05f, 0.08f, 0.4f), metal, null, false);
            Prim(PrimitiveType.Cube, "Stock", gun, new Vector3(0f, 0.0f, -0.25f), new Vector3(0.045f, 0.09f, 0.2f), dark, null, false);
            Prim(PrimitiveType.Cube, "Mag", gun, new Vector3(0f, -0.08f, 0.1f), new Vector3(0.03f, 0.14f, 0.06f), dark, Quaternion.Euler(-10f, 0f, 0f), false);
            Prim(PrimitiveType.Cylinder, "Barrel", gun, new Vector3(0f, 0.03f, 0.38f), new Vector3(0.025f, 0.12f, 0.025f), metal, Quaternion.Euler(90f, 0f, 0f), false);
            Prim(PrimitiveType.Cube, "Sight", gun, new Vector3(0f, 0.08f, 0.05f), new Vector3(0.035f, 0.035f, 0.06f), dark, null, false);
            bot.heldWeapon = gun;
        }

        static void BuildYard(Transform env, Transform bots)
        {
            var concrete = Lit("Concrete", new Color(0.68f, 0.67f, 0.64f), 0f, 0.12f);
            var crate = Lit("Crate", new Color(0.62f, 0.47f, 0.3f), 0f, 0.25f);
            var yard = new GameObject("TrainingYard").transform;
            yard.SetParent(env, false);
            Block("YardWallN", yard, 21f, 40f, new Vector3(10f, 2.6f, 0.4f), concrete);
            Block("YardWallE", yard, 31f, 25f, new Vector3(0.4f, 2.6f, 26f), concrete);
            Block("GuardWall", yard, 20f, 37.6f, new Vector3(4f, 2.2f, 0.4f), concrete);
            Block("GuardWall2", yard, 28.6f, 12f, new Vector3(0.4f, 2.2f, 4f), concrete);
            Block("YardCrate1", yard, 19f, 22f, new Vector3(1.2f, 1.1f, 1.2f), crate).AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            Block("YardCrate2", yard, 25f, 24f, new Vector3(2.2f, 0.9f, 0.6f), crate).AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            Block("YardCover", yard, 14f, 18f, new Vector3(0.4f, 1.3f, 2.5f), concrete);

            MakeBot(bots, new Vector3(16f, 0f, 12f), 0f, new[] { new Vector3(16f, 0f, 12f), new Vector3(16f, 0f, 30f) });
            MakeBot(bots, new Vector3(22f, 0f, 30f), 90f, new[] { new Vector3(22f, 0f, 30f), new Vector3(28f, 0f, 30f), new Vector3(28f, 0f, 18f), new Vector3(22f, 0f, 18f) });
            MakeBot(bots, new Vector3(20f, 0f, 36.6f), 0f, null);
            MakeBot(bots, new Vector3(27.6f, 0f, 12f), 90f, null);
            MakeBot(bots, new Vector3(23f, 0f, 9f), 60f, null);
        }

        static void BuildArmory(Transform env)
        {
            var wood = Lit("Wood", new Color(0.5f, 0.36f, 0.24f), 0f, 0.25f);
            var metal = Lit("TargetMetal", new Color(0.18f, 0.19f, 0.2f), 0.8f, 0.45f);
            var crate = Lit("AmmoCrate", new Color(0.3f, 0.34f, 0.22f), 0f, 0.3f);
            var armory = new GameObject("Armory").transform;
            armory.SetParent(env, false);
            Block("ArmoryTable", armory, -4.2f, -3.2f, new Vector3(0.9f, 0.85f, 4.4f), wood).AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            Block("ArmoryRack", armory, -4.9f, -3.2f, new Vector3(0.15f, 2f, 4.6f), metal);
            var names = new List<string>();
            foreach (var d in WeaponCatalog.All) if (!d.Secondary) names.Add(d.name);
            for (int i = 0; i < names.Count; i++)
            {
                var p = new GameObject("Pickup_" + names[i]);
                p.transform.SetParent(armory, false);
                p.transform.position = new Vector3(-4.2f, 0.9f, -3.2f + (i - (names.Count - 1) * 0.5f) * Mathf.Min(1.05f, 4.2f / names.Count));
                p.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                var wp = p.AddComponent<WeaponPickup>();
                wp.kind = WeaponPickup.Kind.Weapon;
                wp.weaponName = names[i];
            }
            var ammo = Block("AmmoCrate", armory, -4.2f, -0.4f, new Vector3(0.8f, 0.45f, 0.6f), crate, 0.85f);
            ammo.AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            var ap = ammo.AddComponent<WeaponPickup>();
            ap.kind = WeaponPickup.Kind.Ammo;
            ap.spin = false;
        }
    }
}
