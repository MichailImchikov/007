using System;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace FPS.EditorTools
{
    /// <summary>
    /// Deathmatch maps + main menu scene. Original layouts built on classic level-design patterns:
    /// three-lane town, container yard with a raised power lane, two-level warehouse.
    /// Tools > FPS > Build All Maps.
    /// </summary>
    public static partial class RangeBuilder
    {
        [MenuItem("Tools/FPS/Build All Maps (Menu + Deathmatch)", priority = 10)]
        public static void BuildAllMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildAll();
        }

        public static void BuildAll()
        {
            BuildMainMenuScene();
            BuildPort();
            BuildTown();
            BuildWarehouse();
            BuildPlant();
            BuildVillage();
            BuildFlooded();
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) Build();
            SetBuildScenes();
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            Debug.Log("[FPS] All maps built. Open MainMenu and press Play.");
        }

        [MenuItem("Tools/FPS/Maps/Main Menu")] static void M0() => BuildMainMenuScene();
        [MenuItem("Tools/FPS/Maps/Port")] static void M1() => BuildPort();
        [MenuItem("Tools/FPS/Maps/Town")] static void M2() => BuildTown();
        [MenuItem("Tools/FPS/Maps/Warehouse")] static void M3() => BuildWarehouse();

        // ------------------------------------------------------------------ scene scaffolding

        static void Prepare()
        {
            mats.Clear();
            EnsureFolder("Assets/_Game");
            EnsureFolder(Root);
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Textures");
        }

        static void BuildMainMenuScene()
        {
            Prepare();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.035f, 0.045f);
            camGO.AddComponent<AudioListener>();
            new GameObject("MainMenu").AddComponent<MainMenu>();
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/MainMenu.unity");
            SetBuildScenes();
        }

        static void BuildDM(string sceneName, string title, LightPreset light, Action<Transform> map, (float x, float y, float z, float yaw)[] spawns)
        {
            Prepare();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildLighting(light);
            var env = new GameObject("Environment").transform;
            map(env);

            var spawnRoot = new GameObject("SpawnPoints").transform;
            foreach (var s in spawns)
            {
                var sp = new GameObject("Spawn").transform;
                sp.SetParent(spawnRoot, false);
                sp.position = new Vector3(s.x, s.y, s.z);
                sp.rotation = Quaternion.Euler(0f, s.yaw, 0f);
                sp.gameObject.AddComponent<SpawnPoint>();
            }

            BuildFXObject();
            var player = BuildPlayer();
            player.transform.position = new Vector3(spawns[0].x, spawns[0].y + 0.05f, spawns[0].z);
            player.transform.rotation = Quaternion.Euler(0f, spawns[0].yaw, 0f);

            var nav = new GameObject("Navigation");
            var surface = nav.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            var mmGO = new GameObject("MatchManager");
            var mm = mmGO.AddComponent<MatchManager>();
            mm.mapName = title;
            mm.navSurface = surface;
            mm.botTemplate = BuildBotTemplate(new Vector3(spawns[1].x, spawns[1].y, spawns[1].z));

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/" + sceneName + ".unity");
            SetBuildScenes();
            AssetDatabase.SaveAssets();
        }

        static GameObject BuildBotTemplate(Vector3 pos)
        {
            var root = new GameObject("BotTemplate");
            root.transform.position = pos;
            var rig = HumanoidRig.Build(root.transform, BotPalette(), true);
            var anim = root.AddComponent<CharacterAnimator>();
            anim.rig = rig;
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.35f;
            agent.height = 1.8f;
            agent.baseOffset = 0f;
            var c = root.AddComponent<Combatant>();
            c.displayName = "Бот";
            var bot = root.AddComponent<CombatBot>();
            bot.rig = rig; bot.anim = anim; bot.combatant = c; bot.agent = agent;
            root.SetActive(false);
            return root;
        }

        // ------------------------------------------------------------------ geometry kit

        static Texture2D NoiseTexture(string name, Color a, Color b, float scale, int seed)
        {
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color[N * N];
            float o = seed * 17.3f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    // tileable noise via torus mapping
                    float nx = Mathf.Cos(u * Mathf.PI * 2f) * scale, ny = Mathf.Sin(u * Mathf.PI * 2f) * scale;
                    float n = Mathf.PerlinNoise(nx + o + v * scale * 6.28f, ny + o) * 0.6f + Mathf.PerlinNoise(nx * 3f - o, ny * 3f + v * scale * 18.8f) * 0.4f;
                    px[y * N + x] = Color.Lerp(a, b, n);
                }
            t.SetPixels(px); t.Apply(true);
            return t;
        }

        static Material TexLit(string name, Texture2D tex, Vector2 tiling, float smooth)
        {
            var texAsset = SaveAsset(tex, $"{Root}/Textures/{name}.asset");
            var m = Lit(name, Color.white, 0f, smooth);
            m.SetTexture("_BaseMap", texAsset);
            m.SetTextureScale("_BaseMap", tiling);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Wall from a to b (ground footprint) with rectangular openings (along, width, bottom, top).</summary>
        static void Wall(Transform p, Vector3 a, Vector3 b, float baseY, float h, float thick, Material m, params Vector4[] openings)
        {
            Vector3 d = b - a; d.y = 0f;
            float L = d.magnitude;
            if (L < 0.01f) return;
            d /= L;
            var rot = Quaternion.LookRotation(Vector3.Cross(d, Vector3.up));
            var list = new List<Vector4>(openings);
            list.Sort((x, y) => x.x.CompareTo(y.x));
            float cursor = 0f;
            void Piece(float s0, float s1, float y0, float y1)
            {
                if (s1 - s0 < 0.01f || y1 - y0 < 0.01f) return;
                var go = Prim(PrimitiveType.Cube, "Wall", p, a + d * ((s0 + s1) * 0.5f) + Vector3.up * (baseY + (y0 + y1) * 0.5f),
                              new Vector3(s1 - s0, y1 - y0, thick), m, rot, true, true);
            }
            foreach (var o in list)
            {
                float s0 = Mathf.Clamp(o.x - o.y * 0.5f, 0f, L), s1 = Mathf.Clamp(o.x + o.y * 0.5f, 0f, L);
                Piece(cursor, s0, 0f, h);
                Piece(s0, s1, 0f, o.z);
                Piece(s0, s1, o.w, h);
                cursor = s1;
            }
            Piece(cursor, L, 0f, h);
        }

        static Vector4 Door(float along, float width = 1.4f, float height = 2.4f) => new Vector4(along, width, 0f, height);
        static Vector4 Window(float along, float width = 1.4f, float sill = 1.1f, float top = 2.2f) => new Vector4(along, width, sill, top);

        /// <summary>Four walls around a rectangle (walls along X span the full width).</summary>
        static void Room(Transform p, float minX, float minZ, float maxX, float maxZ, float baseY, float h, float t, Material m,
                         Vector4[] south = null, Vector4[] north = null, Vector4[] west = null, Vector4[] east = null)
        {
            float ht = t * 0.5f;
            Wall(p, new Vector3(minX - ht, 0, minZ), new Vector3(maxX + ht, 0, minZ), baseY, h, t, m, Shift(south, ht));
            Wall(p, new Vector3(minX - ht, 0, maxZ), new Vector3(maxX + ht, 0, maxZ), baseY, h, t, m, Shift(north, ht));
            Wall(p, new Vector3(minX, 0, minZ + ht), new Vector3(minX, 0, maxZ - ht), baseY, h, t, m, Shift(west, -ht));
            Wall(p, new Vector3(maxX, 0, minZ + ht), new Vector3(maxX, 0, maxZ - ht), baseY, h, t, m, Shift(east, -ht));
        }

        static Vector4[] Shift(Vector4[] o, float by)
        {
            if (o == null) return new Vector4[0];
            var r = new Vector4[o.Length];
            for (int i = 0; i < o.Length; i++) r[i] = new Vector4(o[i].x + by, o[i].y, o[i].z, o[i].w);
            return r;
        }

        static void Slab(Transform p, float minX, float minZ, float maxX, float maxZ, float topY, float thick, Material m)
            => Prim(PrimitiveType.Cube, "Slab", p, new Vector3((minX + maxX) * 0.5f, topY - thick * 0.5f, (minZ + maxZ) * 0.5f),
                    new Vector3(maxX - minX, thick, maxZ - minZ), m, null, true, true);

        /// <summary>Solid stairs: bottom front edge center, flat direction, width, rise.</summary>
        static void Stairs(Transform p, Vector3 start, Vector3 dir, float width, float rise, Material m, float stepH = 0.2f, float stepD = 0.3f)
        {
            dir.y = 0f; dir.Normalize();
            int n = Mathf.CeilToInt(rise / stepH);
            float sh = rise / n;
            var rot = Quaternion.LookRotation(dir);
            for (int i = 0; i < n; i++)
            {
                float top = sh * (i + 1);
                Vector3 c = start + dir * (stepD * (i + 0.5f)) + Vector3.up * (start.y + top * 0.5f - start.y);
                c.y = start.y + top * 0.5f;
                Prim(PrimitiveType.Cube, "Step", p, c, new Vector3(width, top, stepD), m, rot, true, true);
            }
        }

        static float StairLength(float rise, float stepH = 0.2f, float stepD = 0.3f) => Mathf.CeilToInt(rise / stepH) * stepD;

        static void Railing(Transform p, Vector3 a, Vector3 b, float baseY, Material m, float h = 1.05f)
        {
            Vector3 d = b - a; d.y = 0f;
            float L = d.magnitude; d /= L;
            var rot = Quaternion.LookRotation(Vector3.Cross(d, Vector3.up));
            Prim(PrimitiveType.Cube, "RailTop", p, (a + b) * 0.5f + Vector3.up * (baseY + h), new Vector3(L, 0.06f, 0.06f), m, rot, true, true);
            Prim(PrimitiveType.Cube, "RailMid", p, (a + b) * 0.5f + Vector3.up * (baseY + h * 0.5f), new Vector3(L, 0.05f, 0.05f), m, rot, false, true);
            int posts = Mathf.Max(2, Mathf.CeilToInt(L / 1.5f) + 1);
            for (int i = 0; i < posts; i++)
                Prim(PrimitiveType.Cube, "RailPost", p, a + d * (L * i / (posts - 1)) + Vector3.up * (baseY + h * 0.5f), new Vector3(0.06f, h, 0.06f), m, rot, false, true);
        }

        static GameObject Container(Transform p, float x, float z, float yaw, Material body, Material dark, int stack = 1, float baseY = 0f)
        {
            GameObject first = null;
            for (int s = 0; s < stack; s++)
            {
                var root = new GameObject("Container").transform;
                root.SetParent(p, false);
                root.position = new Vector3(x, baseY + s * 2.6f, z);
                root.rotation = Quaternion.Euler(0f, yaw + (s == 1 ? 1.5f : 0f), 0f);
                Prim(PrimitiveType.Cube, "Body", root, new Vector3(0f, 1.3f, 0f), new Vector3(6.06f, 2.6f, 2.44f), body, null, true, true);
                for (int i = 0; i < 9; i++)
                {
                    float px = -2.7f + i * 0.675f;
                    Prim(PrimitiveType.Cube, "Rib", root, new Vector3(px, 1.3f, 1.235f), new Vector3(0.18f, 2.3f, 0.04f), body, null, false, true);
                    Prim(PrimitiveType.Cube, "Rib", root, new Vector3(px, 1.3f, -1.235f), new Vector3(0.18f, 2.3f, 0.04f), body, null, false, true);
                }
                Prim(PrimitiveType.Cube, "Frame", root, new Vector3(3.04f, 1.3f, 0f), new Vector3(0.06f, 2.6f, 2.5f), dark, null, false, true);
                Prim(PrimitiveType.Cube, "DoorBar", root, new Vector3(3.08f, 1.3f, 0.4f), new Vector3(0.04f, 2.4f, 0.05f), dark, null, false, true);
                Prim(PrimitiveType.Cube, "DoorBar", root, new Vector3(3.08f, 1.3f, -0.4f), new Vector3(0.04f, 2.4f, 0.05f), dark, null, false, true);
                Prim(PrimitiveType.Cube, "Frame", root, new Vector3(-3.04f, 1.3f, 0f), new Vector3(0.06f, 2.6f, 2.5f), dark, null, false, true);
                if (!first) first = root.gameObject;
                root.gameObject.AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            }
            return first;
        }

        static void Crate(Transform p, Vector3 pos, float size, float yaw, Material m, bool physics = false)
        {
            // not static: destructible cover wobbles, tumbles and breaks at runtime
            var go = Prim(PrimitiveType.Cube, "Crate", p, pos + Vector3.up * size * 0.5f, Vector3.one * size, m, Quaternion.Euler(0f, yaw, 0f), true, false);
            go.AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            var d = go.AddComponent<Destructible>();
            d.kind = Destructible.Kind.Wood;
            d.maxHealth = Mathf.Lerp(90f, 150f, Mathf.InverseLerp(0.8f, 1.3f, size));
            if (physics) { var rb = go.AddComponent<Rigidbody>(); rb.mass = 10f * size; }
        }

        static void Barrel(Transform p, Vector3 pos, Material m, bool physics = true)
        {
            var b = Prim(PrimitiveType.Cylinder, "Barrel", p, pos + Vector3.up * 0.45f, new Vector3(0.6f, 0.45f, 0.6f), m, null, false, false);
            var cc = b.AddComponent<CapsuleCollider>(); cc.height = 2f; cc.radius = 0.5f;
            b.AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            if (physics) { var rb = b.AddComponent<Rigidbody>(); rb.mass = 14f; }
        }

        static void Sandbags(Transform p, Vector3 center, float yaw, float length, Material m, int layers = 3)
        {
            var root = new GameObject("Sandbags").transform;
            root.SetParent(p, false);
            root.position = center;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            var rnd = new Random((int)(center.x * 13 + center.z * 7));
            int per = Mathf.Max(1, Mathf.RoundToInt(length / 0.62f));
            var bags = new List<Transform>();
            for (int l = 0; l < layers; l++)
                for (int i = 0; i < per; i++)
                {
                    float x = -length * 0.5f + (i + 0.5f + (l % 2) * 0.5f) * (length / per);
                    if (x > length * 0.5f) continue;
                    bags.Add(Prim(PrimitiveType.Cube, "Bag", root, new Vector3(x, 0.09f + l * 0.17f, 0f), new Vector3(0.58f, 0.17f, 0.36f), m,
                         Quaternion.Euler(0f, (float)(rnd.NextDouble() * 8 - 4), (float)(rnd.NextDouble() * 4 - 2)), false, false).transform);
                }
            var colGO = Prim(PrimitiveType.Cube, "SandbagCollider", root, new Vector3(0f, layers * 0.085f, 0f), new Vector3(length, layers * 0.17f, 0.36f), m, null, true, false);
            colGO.GetComponent<Renderer>().enabled = false;
            var d = colGO.AddComponent<Destructible>();
            d.kind = Destructible.Kind.Sand;
            d.maxHealth = 110f * length;
            d.pieces = bags.ToArray();
        }

        static void Lamp(Transform p, Vector3 pos, float height, Color color, float intensity, float range, Material pole, Material bulb)
        {
            if (pole) Prim(PrimitiveType.Cylinder, "LampPole", p, pos + Vector3.up * height * 0.5f, new Vector3(0.12f, height * 0.5f, 0.12f), pole, null, true, true);
            Prim(PrimitiveType.Cube, "LampHead", p, pos + Vector3.up * height, new Vector3(0.6f, 0.12f, 0.3f), bulb, null, false, true);
            var lgo = new GameObject("LampLight");
            lgo.transform.SetParent(p, false);
            lgo.transform.position = pos + Vector3.up * (height - 0.2f);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point; l.color = color; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        }

        static Material Emissive(string name, Color c, float power)
        {
            var m = Lit(name, c, 0f, 0.5f, true);
            m.SetColor("_EmissionColor", c * power);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ================================================================== PORT (container yard)

        static void BuildPort()
        {
            var light = new LightPreset
            {
                name = "Port", sunIntensity = 1.7f, sunColor = new Color(1f, 0.9f, 0.78f), sunEuler = new Vector3(32f, 125f, 0f),
                ambSky = new Color(0.5f, 0.6f, 0.75f), ambEquator = new Color(0.45f, 0.47f, 0.5f), ambGround = new Color(0.22f, 0.21f, 0.2f),
                fogColor = new Color(0.62f, 0.7f, 0.8f), fogStart = 35f, fogEnd = 190f,
                skyTint = new Color(0.45f, 0.5f, 0.6f), atmosphere = 1.15f, skyExposure = 1.2f,
                exposure = 0.3f, contrast = 16f, saturation = 6f, bloom = 0.7f,
                probeCenter = new Vector3(0f, 4f, 0f), probeSize = new Vector3(90f, 30f, 90f)
            };
            (float, float, float, float)[] spawns =
            {
                (-31f, 0f, -31f, 45f), (31f, 0f, 31f, -135f), (-31f, 0f, 31f, 135f), (31f, 0f, -31f, -45f),
                (-31f, 0f, 0f, 90f), (31f, 0f, 0f, -90f), (0f, 0f, -31f, 0f), (0f, 0f, 31f, 180f),
                (-9f, 0f, 9f, 0f), (11f, 0f, 1f, -90f), (-5f, 0f, -24f, 0f), (20f, 0f, 22f, -135f),
            };
            BuildDM("Map_Port", "Порт", light, PortLayout, spawns);
        }

        static void PortLayout(Transform env)
        {
            var asphalt = TexLit("Port_Asphalt", NoiseTexture("Port_AsphaltTex", new Color(0.17f, 0.17f, 0.18f), new Color(0.27f, 0.27f, 0.28f), 4f, 1), new Vector2(18f, 18f), 0.15f);
            var concrete = Lit("Port_Concrete", new Color(0.58f, 0.57f, 0.55f), 0f, 0.12f);
            var dark = Lit("Port_DarkMetal", new Color(0.14f, 0.15f, 0.16f), 0.7f, 0.4f);
            var paint = Lit("Port_Paint", new Color(0.95f, 0.75f, 0.15f), 0f, 0.3f);
            var crane = Lit("Port_Crane", new Color(0.95f, 0.55f, 0.1f), 0.5f, 0.45f);
            var wood = Lit("Port_Wood", new Color(0.55f, 0.4f, 0.25f), 0f, 0.25f);
            var barrel = Lit("Port_Barrel", new Color(0.15f, 0.3f, 0.55f), 0.6f, 0.45f);
            Material[] cols =
            {
                Lit("Cont_Red", new Color(0.62f, 0.14f, 0.1f), 0.4f, 0.35f), Lit("Cont_Blue", new Color(0.1f, 0.28f, 0.55f), 0.4f, 0.35f),
                Lit("Cont_Green", new Color(0.18f, 0.4f, 0.24f), 0.4f, 0.35f), Lit("Cont_Orange", new Color(0.85f, 0.42f, 0.1f), 0.4f, 0.35f),
                Lit("Cont_Grey", new Color(0.5f, 0.52f, 0.55f), 0.4f, 0.35f), Lit("Cont_Yellow", new Color(0.85f, 0.68f, 0.12f), 0.4f, 0.35f),
            };
            int ci = 0;
            Material C() => cols[(ci++ * 7 + 3) % cols.Length];

            var root = new GameObject("Port").transform;
            root.SetParent(env, false);
            Box("Ground", root, new Vector3(0f, -0.5f, 0f), new Vector3(110f, 1f, 110f), asphalt);
            // painted lane lines
            for (int i = -2; i <= 2; i++)
                Prim(PrimitiveType.Cube, "Line", root, new Vector3(i * 10f + 5f, 0.005f, 0f), new Vector3(0.15f, 0.01f, 64f), paint, null, false, true);

            // perimeter fence + water edge decoration on the north
            Room(root, -34f, -34f, 34f, 34f, 0f, 3.5f, 0.5f, concrete);

            // ---- central shed
            var shed = Lit("Port_Shed", new Color(0.42f, 0.47f, 0.52f), 0.3f, 0.3f);
            Room(root, -8f, -6f, 8f, 6f, 0f, 5f, 0.3f, shed,
                 south: new[] { Door(8f, 4f, 3.6f) }, north: new[] { Door(8f, 4f, 3.6f) },
                 west: new[] { Door(4f, 1.6f, 2.4f), Window(8.5f, 1.6f, 1.2f, 2.3f) }, east: new[] { Door(8f, 1.6f, 2.4f), Window(3.5f, 1.6f, 1.2f, 2.3f) });
            Slab(root, -8.2f, -6.2f, 8.2f, 6.2f, 5.25f, 0.25f, dark);
            Crate(root, new Vector3(-5f, 0f, -3f), 1.2f, 10f, wood);
            Crate(root, new Vector3(-5f, 1.2f, -3f), 1f, 30f, wood);
            Crate(root, new Vector3(4.5f, 0f, 3f), 1.2f, -5f, wood);
            Crate(root, new Vector3(5.7f, 0f, 3f), 1.2f, 3f, wood);
            Block("ShedShelf", root, 0f, 4.8f, new Vector3(6f, 1.6f, 0.9f), dark);
            Lamp(root, new Vector3(0f, 0f, 0f), 4.6f, new Color(1f, 0.9f, 0.75f), 2.5f, 12f, null, Emissive("Port_Bulb", new Color(1f, 0.9f, 0.7f), 3f));

            // ---- raised power lane on the east: a row of containers with stairs on both ends
            for (int i = 0; i < 8; i++) Container(root, 27f, -21f + i * 6f, 90f, C(), dark);
            float sl = StairLength(2.6f);
            Stairs(root, new Vector3(27f, 0f, -24.05f - sl), Vector3.forward, 2.4f, 2.6f, concrete);
            Stairs(root, new Vector3(27f, 0f, 24.05f + sl), Vector3.back, 2.4f, 2.6f, concrete);
            Crate(root, new Vector3(27f, 2.6f, -12f), 1.1f, 15f, wood);
            Crate(root, new Vector3(26.6f, 2.6f, 0f), 1.1f, -10f, wood);
            Crate(root, new Vector3(27.2f, 2.6f, 12f), 1.1f, 40f, wood);
            Sandbags(root, new Vector3(26f, 2.6f, -5f), 90f, 2.4f, Lit("Port_Sandbag", new Color(0.55f, 0.5f, 0.38f), 0f, 0.1f), 3);
            Sandbags(root, new Vector3(26f, 2.6f, 6f), 90f, 2.4f, Lit("Port_Sandbag", new Color(0.55f, 0.5f, 0.38f), 0f, 0.1f), 3);

            // ---- west container maze
            Container(root, -26f, -22f, 90f, C(), dark, 2);
            Container(root, -26f, -12f, 90f, C(), dark, 1);
            Container(root, -26f, 0f, 90f, C(), dark, 2);
            Container(root, -26f, 13f, 90f, C(), dark, 1);
            Container(root, -26f, 24f, 90f, C(), dark, 2);
            Container(root, -19f, -17f, 0f, C(), dark, 1);
            Container(root, -19f, -5f, 0f, C(), dark, 1);
            Container(root, -19f, 8f, 0f, C(), dark, 2);
            Container(root, -19f, 20f, 0f, C(), dark, 1);
            Container(root, -14f, -25f, 90f, C(), dark, 1);
            Container(root, -14f, 1f, 90f, C(), dark, 1);
            Container(root, -14f, 27f, 90f, C(), dark, 1);

            // ---- north: crane and stacks
            foreach (float cx in new[] { -7f, 7f })
            {
                Block("CraneLeg", root, cx, 21f, new Vector3(0.9f, 15f, 0.9f), crane);
                Block("CraneLeg", root, cx, 29f, new Vector3(0.9f, 15f, 0.9f), crane);
                Block("CraneBrace", root, cx, 25f, new Vector3(0.5f, 0.6f, 8.5f), crane, 8f);
            }
            Block("CraneBeam", root, 0f, 21f, new Vector3(16f, 1.4f, 1.4f), crane, 15f);
            Block("CraneBeam", root, 0f, 29f, new Vector3(16f, 1.4f, 1.4f), crane, 15f);
            Block("CraneCab", root, 2f, 25f, new Vector3(3f, 2.2f, 8f), dark, 13.8f);
            Container(root, 0f, 18f, 0f, C(), dark, 2);
            Container(root, 11f, 24f, 0f, C(), dark, 1);
            Container(root, -3f, 28f, 0f, C(), dark, 1);
            Container(root, 15f, 15f, 90f, C(), dark, 1);

            // ---- south yard
            Container(root, 0f, -20f, 0f, C(), dark, 1);
            Container(root, 9f, -26f, 90f, C(), dark, 2);
            Container(root, -7f, -29f, 0f, C(), dark, 1);
            Container(root, 15f, -15f, 0f, C(), dark, 1);
            Container(root, 17f, -4f, 90f, C(), dark, 1);
            Container(root, 17f, 7f, 90f, C(), dark, 2);

            // ---- cover clutter
            var jersey = Lit("Port_Jersey", new Color(0.7f, 0.69f, 0.66f), 0f, 0.1f);
            Vector3[] barriers = { new Vector3(0f, 0f, -11f), new Vector3(-10f, 0f, -1f), new Vector3(10f, 0f, 11f), new Vector3(-8f, 0f, 14f), new Vector3(22f, 0f, -24f), new Vector3(-22f, 0f, 30f), new Vector3(5f, 0f, -31f) };
            float[] byaw = { 0f, 90f, 0f, 20f, 70f, 0f, 0f };
            for (int i = 0; i < barriers.Length; i++) Block("Barrier", root, barriers[i].x, barriers[i].z, new Vector3(2.4f, 0.9f, 0.6f), jersey, 0f, byaw[i]).AddComponent<SurfaceTag>().kind = SurfaceKind.Concrete;
            Barrel(root, new Vector3(-12f, 0f, -12f), barrel); Barrel(root, new Vector3(-11.3f, 0f, -12.4f), barrel); Barrel(root, new Vector3(-11.7f, 0f, -11.3f), barrel);
            Barrel(root, new Vector3(21f, 0f, 14f), barrel); Barrel(root, new Vector3(21.7f, 0f, 14.5f), barrel);
            Barrel(root, new Vector3(5f, 0f, 9f), barrel);
            Crate(root, new Vector3(-6f, 0f, -14f), 1.2f, 12f, wood); Crate(root, new Vector3(-4.8f, 0f, -14.2f), 1.2f, -8f, wood);
            Crate(root, new Vector3(9f, 0f, -9f), 1.2f, 45f, wood);
            Crate(root, new Vector3(-30f, 0f, -6f), 1.2f, 0f, wood); Crate(root, new Vector3(-30f, 1.2f, -6f), 1f, 20f, wood);
            Crate(root, new Vector3(31f, 0f, 9f), 1.2f, 10f, wood);
            // forklift
            Block("Forklift", root, 4f, -13f, new Vector3(1.3f, 1.4f, 2.4f), Lit("Port_Forklift", new Color(0.9f, 0.7f, 0.1f), 0.2f, 0.4f), 0f, 15f);
            Block("ForkliftMast", root, 4.35f, -11.7f, new Vector3(1.2f, 2.6f, 0.15f), dark, 0f, 15f);
            for (int i = 0; i < 6; i++)
                Lamp(root, new Vector3(-30f + i * 12f, 0f, -33f), 8f, new Color(1f, 0.85f, 0.65f), 1.2f, 14f, dark, Emissive("Port_Bulb", new Color(1f, 0.9f, 0.7f), 3f));
            PortDetails(root);
        }

        // ================================================================== TOWN (desert, three lanes)

        static void BuildTown()
        {
            var light = new LightPreset
            {
                name = "Town", sunIntensity = 2.15f, sunColor = new Color(1f, 0.88f, 0.72f), sunEuler = new Vector3(52f, 35f, 0f),
                ambSky = new Color(0.62f, 0.66f, 0.72f), ambEquator = new Color(0.62f, 0.55f, 0.45f), ambGround = new Color(0.4f, 0.32f, 0.22f),
                fogColor = new Color(0.86f, 0.78f, 0.64f), fogStart = 45f, fogEnd = 220f,
                skyTint = new Color(0.6f, 0.55f, 0.48f), atmosphere = 1.5f, skyExposure = 1.3f,
                exposure = 0.25f, contrast = 18f, saturation = 4f, bloom = 0.8f,
                probeCenter = new Vector3(0f, 5f, 0f), probeSize = new Vector3(110f, 40f, 110f)
            };
            (float, float, float, float)[] spawns =
            {
                (0f, 0f, -41f, 0f), (0f, 0f, 41f, 180f), (-24f, 0f, -41f, 0f), (24f, 0f, 41f, 180f),
                (24f, 0f, -41f, 0f), (-24f, 0f, 41f, 180f), (-37f, 0f, -17f, 90f), (37f, 0f, 17f, -90f),
                (-37f, 0f, 17f, 90f), (37f, 0f, -17f, -90f), (-24f, 0f, 0f, 90f), (24f, 0f, 0f, -90f),
            };
            BuildDM("Map_Town", "Городок", light, TownLayout, spawns);
        }

        static void TownLayout(Transform env)
        {
            var sand = TexLit("Town_Sand", NoiseTexture("Town_SandTex", new Color(0.7f, 0.58f, 0.42f), new Color(0.82f, 0.7f, 0.52f), 3f, 2), new Vector2(14f, 14f), 0.08f);
            var plaster = Lit("Town_Plaster", new Color(0.88f, 0.8f, 0.66f), 0f, 0.12f);
            var plaster2 = Lit("Town_Plaster2", new Color(0.78f, 0.66f, 0.5f), 0f, 0.12f);
            var plaster3 = Lit("Town_Plaster3", new Color(0.82f, 0.62f, 0.48f), 0f, 0.12f);
            var stone = Lit("Town_Stone", new Color(0.62f, 0.55f, 0.46f), 0f, 0.1f);
            var wood = Lit("Town_Wood", new Color(0.36f, 0.23f, 0.13f), 0f, 0.3f);
            var terracotta = Lit("Town_Terracotta", new Color(0.62f, 0.32f, 0.2f), 0f, 0.2f);
            var blue = Lit("Town_Blue", new Color(0.18f, 0.38f, 0.58f), 0f, 0.3f);
            var clothR = Lit("Town_ClothRed", new Color(0.62f, 0.18f, 0.14f), 0f, 0.1f);
            var clothC = Lit("Town_ClothCream", new Color(0.9f, 0.85f, 0.72f), 0f, 0.1f);
            var bags = Lit("Town_Sandbag", new Color(0.66f, 0.58f, 0.42f), 0f, 0.1f);
            var crateM = Lit("Town_Crate", new Color(0.55f, 0.4f, 0.25f), 0f, 0.25f);
            var palm = Lit("Town_Palm", new Color(0.24f, 0.36f, 0.16f), 0f, 0.2f);
            var root = new GameObject("Town").transform;
            root.SetParent(env, false);
            Box("Ground", root, new Vector3(0f, -0.5f, 0f), new Vector3(140f, 1f, 140f), sand);
            Room(root, -45f, -45f, 45f, 45f, 0f, 6f, 1.2f, stone);
            // battlements
            for (int i = -10; i <= 10; i++)
            {
                Block("Merlon", root, i * 4.3f, -45f, new Vector3(1.6f, 1f, 1.2f), stone, 6f);
                Block("Merlon", root, i * 4.3f, 45f, new Vector3(1.6f, 1f, 1.2f), stone, 6f);
            }

            // ---- two-storey houses with interior stairs and windows over the street (west B1, east B4)
            TwoStorey(root, -20f, -38f, -7f, -16f, plaster, stone, wood, true);
            TwoStorey(root, 7f, 18f, 20f, 38f, plaster3, stone, wood, false);
            // ---- one-storey houses with roof access (outside stairs)
            RoofHouse(root, -20f, 16f, -7f, 38f, plaster2, stone, wood, true);
            RoofHouse(root, 7f, -38f, 20f, -18f, plaster2, stone, wood, false);
            // ---- small plaza-side houses (enterable)
            SmallHouse(root, -20f, -12f, -15f, -3f, plaster3, stone, true);
            SmallHouse(root, -20f, 3f, -15f, 12f, plaster, stone, true);
            SmallHouse(root, 15f, -14f, 20f, -2f, plaster, stone, false);
            SmallHouse(root, 15f, 2f, 20f, 14f, plaster2, stone, false);
            // ---- outer blocks with courtyards
            RoofHouse(root, -43f, -40f, -31f, -27f, plaster, stone, wood, false);
            SmallHouse(root, -43f, -6f, -32f, 6f, plaster2, stone, false);
            RoofHouse(root, -43f, 27f, -31f, 40f, plaster3, stone, wood, false);
            RoofHouse(root, 31f, -40f, 43f, -27f, plaster3, stone, wood, true);
            SmallHouse(root, 32f, -6f, 43f, 6f, plaster, stone, true);
            RoofHouse(root, 31f, 27f, 43f, 40f, plaster2, stone, wood, true);
            // courtyard low walls
            Wall(root, new Vector3(-31f, 0, -24f), new Vector3(-31f, 0, -9f), 0f, 1.2f, 0.5f, stone, Door(7f, 2f, 1.2f));
            Wall(root, new Vector3(31f, 0, 9f), new Vector3(31f, 0, 24f), 0f, 1.2f, 0.5f, stone, Door(8f, 2f, 1.2f));
            Wall(root, new Vector3(-31f, 0, 9f), new Vector3(-31f, 0, 24f), 0f, 1.2f, 0.5f, stone, Door(6f, 2f, 1.2f));
            Wall(root, new Vector3(31f, 0, -24f), new Vector3(31f, 0, -9f), 0f, 1.2f, 0.5f, stone, Door(9f, 2f, 1.2f));

            // ---- plaza: well, market stalls, crates
            Prim(PrimitiveType.Cylinder, "Well", root, new Vector3(0f, 0.45f, 0f), new Vector3(2.8f, 0.45f, 2.8f), stone, null, true, true);
            foreach (var p in new[] { new Vector3(-1.2f, 0, -1.2f), new Vector3(1.2f, 0, 1.2f) })
                Block("WellPost", root, p.x, p.z, new Vector3(0.2f, 2.6f, 0.2f), wood, 0.9f);
            Block("WellRoof", root, 0f, 0f, new Vector3(3.2f, 0.2f, 3.2f), terracotta, 3.4f);
            Stall(root, new Vector3(-9f, 0f, -7f), 0f, wood, clothR);
            Stall(root, new Vector3(9f, 0f, -7f), 0f, wood, clothC);
            Stall(root, new Vector3(-9f, 0f, 7f), 180f, wood, clothC);
            Stall(root, new Vector3(9f, 0f, 7f), 180f, wood, clothR);
            Crate(root, new Vector3(-4f, 0f, 9f), 1f, 12f, crateM);
            Crate(root, new Vector3(4.5f, 0f, -9.5f), 1f, -20f, crateM);
            Crate(root, new Vector3(5.2f, 0f, -8.6f), 0.8f, 30f, crateM);

            // ---- main street: arches, carts, sandbags
            foreach (float z in new[] { -28f, 28f })
            {
                Block("ArchPillar", root, -6f, z, new Vector3(1.2f, 5f, 1.2f), stone);
                Block("ArchPillar", root, 6f, z, new Vector3(1.2f, 5f, 1.2f), stone);
                Block("ArchTop", root, 0f, z, new Vector3(13.2f, 1.4f, 1.4f), stone, 5f);
                Block("ArchRoof", root, 0f, z, new Vector3(13.6f, 0.3f, 1.8f), terracotta, 6.4f);
            }
            Cart(root, new Vector3(-2.5f, 0f, -20f), 15f, wood);
            Cart(root, new Vector3(3f, 0f, 22f), -160f, wood);
            Sandbags(root, new Vector3(0f, 0f, -33f), 0f, 3.2f, bags);
            Sandbags(root, new Vector3(0f, 0f, 33f), 0f, 3.2f, bags);
            Sandbags(root, new Vector3(-24f, 0f, -20f), 90f, 2.6f, bags);
            Sandbags(root, new Vector3(24f, 0f, 20f), 90f, 2.6f, bags);
            Sandbags(root, new Vector3(-24f, 0f, 30f), 0f, 3f, bags);
            Sandbags(root, new Vector3(24f, 0f, -30f), 0f, 3f, bags);
            Crate(root, new Vector3(-23f, 0f, 8f), 1.1f, 5f, crateM); Crate(root, new Vector3(-23f, 1.1f, 8f), 0.9f, 25f, crateM);
            Crate(root, new Vector3(23f, 0f, -8f), 1.1f, -10f, crateM);
            Crate(root, new Vector3(-36f, 0f, 20f), 1.1f, 0f, crateM);
            Crate(root, new Vector3(36f, 0f, -20f), 1.1f, 0f, crateM);
            Block("Trough", root, -37f, -14f, new Vector3(0.9f, 0.7f, 2.6f), stone);
            Block("Trough", root, 37f, 14f, new Vector3(0.9f, 0.7f, 2.6f), stone);
            Palm(root, new Vector3(-12f, 0f, -12f), wood, palm);
            Palm(root, new Vector3(12f, 0f, 12f), wood, palm);
            Palm(root, new Vector3(-37f, 0f, 22f), wood, palm);
            Palm(root, new Vector3(37f, 0f, -22f), wood, palm);
            // awnings over doors
            Prim(PrimitiveType.Cube, "Awning", root, new Vector3(-6.2f, 2.8f, -27f), new Vector3(1.4f, 0.08f, 3f), blue, Quaternion.Euler(0f, 0f, -12f), false, true);
            Prim(PrimitiveType.Cube, "Awning", root, new Vector3(6.2f, 2.8f, 27f), new Vector3(1.4f, 0.08f, 3f), blue, Quaternion.Euler(0f, 0f, 12f), false, true);
            TownDetails(root);
        }

        static void TwoStorey(Transform p, float minX, float minZ, float maxX, float maxZ, Material wall, Material stone, Material wood, bool westSide)
        {
            float t = 0.4f, f = 3.4f, h = f * 2f + 1f;
            float w = maxX - minX, d = maxZ - minZ;
            // street side gets door + windows on both floors; lane side gets a door; cross-alley ends get windows
            var streetOpen = new[] { Door(d * 0.3f, 1.6f), Door(d * 0.75f, 1.6f), Window(d * 0.5f, 1.5f), Window(d * 0.15f, 1.2f, f + 1f, f + 2.3f), Window(d * 0.45f, 1.6f, f + 1f, f + 2.3f), Window(d * 0.8f, 1.2f, f + 1f, f + 2.3f) };
            var laneOpen = new[] { Door(d * 0.5f, 1.4f), Window(d * 0.2f, 1.2f), Window(d * 0.3f, 1.2f, f + 1f, f + 2.3f), Window(d * 0.7f, 1.2f, f + 1f, f + 2.3f) };
            var endOpen = new[] { Window(w * 0.5f, 1.4f), Window(w * 0.3f, 1.2f, f + 1f, f + 2.3f), Window(w * 0.7f, 1.2f, f + 1f, f + 2.3f) };
            if (westSide) Room(p, minX, minZ, maxX, maxZ, 0f, h, t, wall, endOpen, endOpen, laneOpen, streetOpen);
            else Room(p, minX, minZ, maxX, maxZ, 0f, h, t, wall, endOpen, endOpen, streetOpen, laneOpen);
            // upper floor with stair hole, interior stairs along the south wall
            float sl = StairLength(f);
            float holeMinX = westSide ? minX + 0.4f : maxX - 0.4f - sl;
            float holeMaxX = holeMinX + sl;
            Slab(p, minX, minZ + 1.7f, maxX, maxZ, f, 0.25f, stone);
            Slab(p, minX, minZ, holeMinX, minZ + 1.7f, f, 0.25f, stone);
            Slab(p, holeMaxX, minZ, maxX, minZ + 1.7f, f, 0.25f, stone);
            Vector3 start = westSide ? new Vector3(minX + 0.4f, 0f, minZ + 0.95f) : new Vector3(maxX - 0.4f, 0f, minZ + 0.95f);
            Stairs(p, start, westSide ? Vector3.right : Vector3.left, 1.4f, f, stone);
            Slab(p, minX - 0.2f, minZ - 0.2f, maxX + 0.2f, maxZ + 0.2f, h - 1f + 0.3f, 0.3f, stone);
            // furniture as interior cover
            Block("Table", p, (minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f, new Vector3(2f, 0.8f, 1f), wood);
            Block("Shelf", p, (minX + maxX) * 0.5f, maxZ - 0.8f, new Vector3(3f, 1.6f, 0.6f), wood);
            Block("UpperTable", p, (minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f + 3f, new Vector3(2f, 0.8f, 1f), wood, f);
        }

        static void RoofHouse(Transform p, float minX, float minZ, float maxX, float maxZ, Material wall, Material stone, Material wood, bool stairsWest)
        {
            float t = 0.4f, h = 3.6f;
            float w = maxX - minX, d = maxZ - minZ;
            Room(p, minX, minZ, maxX, maxZ, 0f, h, t, wall,
                 new[] { Door(w * 0.35f, 1.4f), Window(w * 0.7f, 1.3f) }, new[] { Window(w * 0.5f, 1.4f) },
                 new[] { Door(d * 0.6f, 1.4f), Window(d * 0.25f, 1.2f) }, new[] { Window(d * 0.4f, 1.3f), Door(d * 0.8f, 1.4f) });
            Slab(p, minX - 0.2f, minZ - 0.2f, maxX + 0.2f, maxZ + 0.2f, h + 0.25f, 0.25f, stone);
            // parapet with a gap where the stairs arrive
            float y = h + 0.25f;
            float sl = StairLength(y);
            float px = stairsWest ? minX - 0.2f : maxX + 0.2f;
            Wall(p, new Vector3(minX - 0.2f, 0, minZ - 0.1f), new Vector3(maxX + 0.2f, 0, minZ - 0.1f), y, 0.9f, 0.2f, wall);
            Wall(p, new Vector3(minX - 0.2f, 0, maxZ + 0.1f), new Vector3(maxX + 0.2f, 0, maxZ + 0.1f), y, 0.9f, 0.2f, wall);
            Wall(p, new Vector3(px, 0, minZ), new Vector3(px, 0, maxZ), y, 0.9f, 0.2f, wall, new Vector4(0.7f, 1.4f, 0f, 5f));
            Wall(p, new Vector3(stairsWest ? maxX + 0.1f : minX - 0.1f, 0, minZ), new Vector3(stairsWest ? maxX + 0.1f : minX - 0.1f, 0, maxZ), y, 0.9f, 0.2f, wall);
            // outside stairs climbing along the side wall towards the south corner
            float sx = stairsWest ? minX - 0.95f : maxX + 0.95f;
            Stairs(p, new Vector3(sx, 0f, minZ + sl + 0.1f), Vector3.back, 1.2f, y, stone);
            Block("Crate", p, (minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f, new Vector3(1f, 0.9f, 1f), wood, y);
        }

        static void SmallHouse(Transform p, float minX, float minZ, float maxX, float maxZ, Material wall, Material stone, bool openEast)
        {
            float w = maxX - minX, d = maxZ - minZ;
            var a = new[] { Door(d * 0.5f, 1.4f) };
            var win = new[] { Window(d * 0.5f, 1.2f) };
            Room(p, minX, minZ, maxX, maxZ, 0f, 3.2f, 0.35f, wall,
                 new[] { Window(w * 0.5f, 1.2f) }, new[] { Door(w * 0.5f, 1.3f) }, openEast ? win : a, openEast ? a : win);
            Slab(p, minX - 0.2f, minZ - 0.2f, maxX + 0.2f, maxZ + 0.2f, 3.45f, 0.25f, stone);
        }

        static void Stall(Transform p, Vector3 pos, float yaw, Material wood, Material cloth)
        {
            var r = new GameObject("Stall").transform;
            r.SetParent(p, false);
            r.position = pos; r.rotation = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "Counter", r, new Vector3(0f, 0.45f, 0f), new Vector3(2.6f, 0.9f, 1f), wood, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            foreach (var x in new[] { -1.25f, 1.25f })
                foreach (var z in new[] { -0.45f, 0.45f })
                    Prim(PrimitiveType.Cube, "Post", r, new Vector3(x, 1.2f, z), new Vector3(0.1f, 2.4f, 0.1f), wood, null, true, true);
            Prim(PrimitiveType.Cube, "Canopy", r, new Vector3(0f, 2.45f, 0.1f), new Vector3(2.9f, 0.06f, 1.6f), cloth, Quaternion.Euler(10f, 0f, 0f), false, true);
            Prim(PrimitiveType.Cube, "Goods", r, new Vector3(-0.6f, 1.0f, 0f), new Vector3(0.5f, 0.2f, 0.4f), cloth, null, false, true);
            Prim(PrimitiveType.Cube, "Goods", r, new Vector3(0.5f, 1.05f, 0.1f), new Vector3(0.6f, 0.3f, 0.5f), wood, null, false, true);
        }

        static void Cart(Transform p, Vector3 pos, float yaw, Material wood)
        {
            var r = new GameObject("Cart").transform;
            r.SetParent(p, false);
            r.position = pos; r.rotation = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "Bed", r, new Vector3(0f, 0.9f, 0f), new Vector3(1.6f, 0.5f, 2.6f), wood, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            foreach (var x in new[] { -0.9f, 0.9f })
                Prim(PrimitiveType.Cylinder, "Wheel", r, new Vector3(x, 0.55f, 0.3f), new Vector3(1.1f, 0.05f, 1.1f), wood, Quaternion.Euler(0f, 0f, 90f), false, true);
            Prim(PrimitiveType.Cube, "Shaft", r, new Vector3(0f, 0.7f, 2f), new Vector3(0.1f, 0.1f, 1.8f), wood, Quaternion.Euler(-10f, 0f, 0f), false, true);
        }

        static void Palm(Transform p, Vector3 pos, Material trunk, Material leaves)
        {
            var r = new GameObject("Palm").transform;
            r.SetParent(p, false);
            r.position = pos;
            Prim(PrimitiveType.Cylinder, "Trunk", r, new Vector3(0f, 2.5f, 0f), new Vector3(0.35f, 2.5f, 0.35f), trunk, Quaternion.Euler(4f, 0f, 3f), true, true);
            for (int i = 0; i < 7; i++)
                Prim(PrimitiveType.Cube, "Leaf", r, new Vector3(0f, 5f, 0f) + Quaternion.Euler(0f, i * 51f, 0f) * new Vector3(0f, 0f, 1.3f),
                     new Vector3(0.6f, 0.05f, 2.6f), leaves, Quaternion.Euler(18f, i * 51f, 0f), false, true);
        }

        // ================================================================== WAREHOUSE (two levels)

        static void BuildWarehouse()
        {
            var light = new LightPreset
            {
                name = "Warehouse", sunIntensity = 1.5f, sunColor = new Color(1f, 0.92f, 0.82f), sunEuler = new Vector3(62f, -60f, 0f),
                ambSky = new Color(0.42f, 0.46f, 0.52f), ambEquator = new Color(0.36f, 0.36f, 0.37f), ambGround = new Color(0.2f, 0.19f, 0.18f),
                fogColor = new Color(0.32f, 0.34f, 0.38f), fogStart = 25f, fogEnd = 140f,
                skyTint = new Color(0.45f, 0.48f, 0.55f), atmosphere = 1.2f, skyExposure = 1f,
                exposure = 0.55f, contrast = 18f, saturation = 2f, bloom = 1f,
                probeCenter = new Vector3(0f, 4f, -4f), probeSize = new Vector3(80f, 25f, 70f)
            };
            (float, float, float, float)[] spawns =
            {
                (-32f, 0f, -20f, 45f), (33.5f, 0f, 4f, -135f), (32f, 0f, -20f, -45f), (-33.5f, 0f, 4f, 135f),
                (0f, 0f, -21f, 0f), (-15f, 0f, -9f, 90f), (15f, 0f, 1f, -90f), (-20f, 4.3f, 20f, 180f),
                (20f, 4.3f, 20f, 180f), (-21f, 0f, -30f, 0f), (21f, 0f, -30f, 0f), (12f, 0f, 11f, 180f),
            };
            BuildDM("Map_Warehouse", "Склад", light, WarehouseLayout, spawns);
        }

        static void WarehouseLayout(Transform env)
        {
            var floor = TexLit("WH_Floor", NoiseTexture("WH_FloorTex", new Color(0.33f, 0.33f, 0.34f), new Color(0.44f, 0.44f, 0.45f), 6f, 3), new Vector2(16f, 16f), 0.35f);
            var wallM = Lit("WH_Wall", new Color(0.4f, 0.45f, 0.5f), 0.4f, 0.3f);
            var steel = Lit("WH_Steel", new Color(0.2f, 0.21f, 0.23f), 0.8f, 0.45f);
            var rackM = Lit("WH_Rack", new Color(0.8f, 0.32f, 0.1f), 0.5f, 0.4f);
            var board = Lit("WH_Board", new Color(0.55f, 0.56f, 0.58f), 0.6f, 0.3f);
            var box = Lit("WH_Box", new Color(0.66f, 0.5f, 0.32f), 0f, 0.2f);
            var box2 = Lit("WH_Box2", new Color(0.58f, 0.44f, 0.28f), 0f, 0.2f);
            var wrap = Lit("WH_Wrap", new Color(0.85f, 0.88f, 0.9f), 0f, 0.7f);
            var paint = Lit("WH_Paint", new Color(0.95f, 0.78f, 0.15f), 0f, 0.3f);
            var pallet = Lit("WH_Pallet", new Color(0.6f, 0.47f, 0.3f), 0f, 0.2f);
            var truck = Lit("WH_Truck", new Color(0.85f, 0.85f, 0.88f), 0.3f, 0.4f);
            var truckCab = Lit("WH_TruckCab", new Color(0.15f, 0.3f, 0.6f), 0.4f, 0.5f);
            var office = Lit("WH_Office", new Color(0.78f, 0.78f, 0.75f), 0f, 0.2f);
            var bulb = Emissive("WH_Bulb", new Color(1f, 0.93f, 0.8f), 4f);
            var concrete = Lit("Port_Concrete", new Color(0.58f, 0.57f, 0.55f), 0f, 0.12f);

            var root = new GameObject("Warehouse").transform;
            root.SetParent(env, false);
            Box("Ground", root, new Vector3(0f, -0.5f, -6f), new Vector3(110f, 1f, 110f), floor);

            // ---- hall walls (two loading doors in the south wall, side doors east/west)
            const float H = 10f;
            Room(root, -35f, -24f, 35f, 24f, 0f, H, 0.5f, wallM,
                 south: new[] { Door(18f, 5f, 4.8f), Door(52f, 5f, 4.8f), Door(35f, 1.6f, 2.5f) },
                 north: new[] { Window(15f, 4f, 6f, 8f), Window(35f, 4f, 6f, 8f), Window(55f, 4f, 6f, 8f) },
                 west: new[] { Door(10f, 1.6f, 2.5f), Window(30f, 4f, 6f, 8f) },
                 east: new[] { Door(38f, 1.6f, 2.5f), Window(18f, 4f, 6f, 8f) });
            // roof with skylight strips
            for (int i = 0; i < 5; i++)
                Slab(root, -35.5f, -24.5f + i * 10f, 35.5f, -24.5f + i * 10f + 7.5f, H + 0.4f, 0.4f, steel);
            for (int i = -3; i <= 3; i++) Block("RoofTruss", root, i * 10f, 0f, new Vector3(0.4f, 0.8f, 48f), steel, H - 0.8f);

            // painted walkways
            foreach (float z in new[] { -9f, 1f, 11f })
                Prim(PrimitiveType.Cube, "Lane", root, new Vector3(0f, 0.005f, z), new Vector3(68f, 0.01f, 0.12f), paint, null, false, true);
            Prim(PrimitiveType.Cube, "Lane", root, new Vector3(-6.2f, 0.005f, -4f), new Vector3(0.12f, 0.01f, 38f), paint, null, false, true);
            Prim(PrimitiveType.Cube, "Lane", root, new Vector3(6.2f, 0.005f, -4f), new Vector3(0.12f, 0.01f, 38f), paint, null, false, true);

            // ---- shelving rows with a central cross aisle
            int seed = 11;
            foreach (float z in new[] { -14f, -4f, 6f })
            {
                Rack(root, -28f, -7f, z, rackM, board, box, box2, wrap, ref seed);
                Rack(root, 7f, 28f, z, rackM, board, box, box2, wrap, ref seed);
            }

            // ---- mezzanine along the north wall
            const float MZ = 4f;
            Slab(root, -33f, 16f, 33f, 23.75f, MZ, 0.3f, steel);
            for (float x = -30f; x <= 30f; x += 10f) Block("Column", root, x, 16.3f, new Vector3(0.4f, MZ - 0.3f, 0.4f), steel);
            float sl = StairLength(MZ);
            float[] stairX = { -31.5f, 0f, 31.5f };
            float gapW = 1.8f;
            // railing with gaps at the stairs
            float cursor = -33f;
            foreach (float sx in stairX)
            {
                if (sx - gapW * 0.5f - cursor > 0.3f) Railing(root, new Vector3(cursor, 0, 16.05f), new Vector3(sx - gapW * 0.5f, 0, 16.05f), MZ, steel);
                cursor = sx + gapW * 0.5f;
            }
            if (33f - cursor > 0.3f) Railing(root, new Vector3(cursor, 0, 16.05f), new Vector3(33f, 0, 16.05f), MZ, steel);
            foreach (float sx in stairX) Stairs(root, new Vector3(sx, 0f, 16f - sl), Vector3.forward, gapW - 0.2f, MZ, steel);
            // cover on the mezzanine
            Crate(root, new Vector3(-24f, MZ, 18.5f), 1.2f, 10f, box);
            Crate(root, new Vector3(-12f, MZ, 17.5f), 1f, -15f, box2);
            Crate(root, new Vector3(10f, MZ, 18f), 1.2f, 5f, box);
            Crate(root, new Vector3(24f, MZ, 17.6f), 1f, 30f, box2);
            Block("MezzWall", root, -6f, 20f, new Vector3(0.3f, 1.2f, 3f), steel, MZ);
            Block("MezzWall", root, 6f, 21f, new Vector3(0.3f, 1.2f, 3f), steel, MZ);

            // ---- office under the mezzanine (west) and storage cage (east)
            Room(root, -30f, 16.5f, -16f, 23.75f, 0f, MZ - 0.3f, 0.2f, office,
                 south: new[] { Door(3f, 1.2f, 2.3f), Window(7f, 2.4f, 1.1f, 2.4f), Window(11f, 2.4f, 1.1f, 2.4f) },
                 east: new[] { Door(3.5f, 1.2f, 2.3f) });
            Block("Desk", root, -24f, 21f, new Vector3(2f, 0.8f, 1f), steel);
            Block("Desk", root, -19f, 21f, new Vector3(2f, 0.8f, 1f), steel);
            Block("Cabinet", root, -28.8f, 20f, new Vector3(0.6f, 1.8f, 2f), board);
            for (float x = 14f; x <= 28f; x += 2f) Block("CagePost", root, x, 16.5f, new Vector3(0.08f, MZ - 0.3f, 0.08f), steel);
            Crate(root, new Vector3(18f, 0f, 20f), 1.3f, 0f, box); Crate(root, new Vector3(18f, 1.3f, 20f), 1.1f, 15f, box2);
            Crate(root, new Vector3(24f, 0f, 21f), 1.3f, -10f, box);

            // ---- central aisle cover: forklifts and pallet stacks
            Forklift(root, new Vector3(0f, 0f, -9f), 30f, steel);
            Forklift(root, new Vector3(-2f, 0f, 9f), -120f, steel);
            PalletStack(root, new Vector3(2.5f, 0f, 1f), 3, pallet, wrap);
            PalletStack(root, new Vector3(-3f, 0f, -2f), 2, pallet, box);
            PalletStack(root, new Vector3(-3.5f, 0f, -18f), 2, pallet, wrap);
            PalletStack(root, new Vector3(-31f, 0f, -2f), 3, pallet, box);
            PalletStack(root, new Vector3(31f, 0f, -9f), 2, pallet, wrap);
            Barrel(root, new Vector3(-31f, 0f, 3f), Lit("WH_Barrel", new Color(0.6f, 0.15f, 0.1f), 0.6f, 0.45f));
            Barrel(root, new Vector3(-30.3f, 0f, 3.5f), Lit("WH_Barrel", new Color(0.6f, 0.15f, 0.1f), 0.6f, 0.45f));

            // ---- loading dock: a trailer backed through the west door, one parked outside
            Trailer(root, new Vector3(-17f, 0f, -24f), truck, truckCab, steel, true);
            Trailer(root, new Vector3(17f, 0f, -33f), truck, truckCab, steel, false);
            // outside yard with a fence
            Wall(root, new Vector3(-35.2f, 0, -42f), new Vector3(35.2f, 0, -42f), 0f, 3f, 0.4f, concrete);
            Wall(root, new Vector3(-35f, 0, -41.8f), new Vector3(-35f, 0, -24.3f), 0f, 3f, 0.4f, concrete);
            Wall(root, new Vector3(35f, 0, -41.8f), new Vector3(35f, 0, -24.3f), 0f, 3f, 0.4f, concrete);
            Block("DockLip", root, 0f, -25f, new Vector3(70f, 0.15f, 1f), steel);

            // ---- interior lights
            for (int ix = 0; ix < 4; ix++)
                for (int iz = 0; iz < 3; iz++)
                    Lamp(root, new Vector3(-24f + ix * 16f, 0f, -16f + iz * 14f), 8.6f, new Color(1f, 0.92f, 0.8f), 6f, 20f, null, bulb);
            Lamp(root, new Vector3(-23f, 0f, 20f), 3.4f, new Color(1f, 0.95f, 0.9f), 2f, 9f, null, bulb);
            WarehouseDetails(root);
        }

        static void Rack(Transform p, float x0, float x1, float z, Material rack, Material board, Material b1, Material b2, Material wrap, ref int seed)
        {
            var rnd = new Random(seed++);
            float len = x1 - x0;
            int bays = Mathf.Max(1, Mathf.RoundToInt(len / 2.7f));
            float bay = len / bays;
            for (int i = 0; i <= bays; i++)
            {
                float x = x0 + i * bay;
                Prim(PrimitiveType.Cube, "RackPost", p, new Vector3(x, 2.25f, z - 0.55f), new Vector3(0.1f, 4.5f, 0.1f), rack, null, true, true);
                Prim(PrimitiveType.Cube, "RackPost", p, new Vector3(x, 2.25f, z + 0.55f), new Vector3(0.1f, 4.5f, 0.1f), rack, null, true, true);
            }
            foreach (float y in new[] { 0.12f, 1.65f, 3.15f })
            {
                Prim(PrimitiveType.Cube, "Shelf", p, new Vector3((x0 + x1) * 0.5f, y, z), new Vector3(len, 0.06f, 1.2f), board, null, true, true);
                Prim(PrimitiveType.Cube, "Beam", p, new Vector3((x0 + x1) * 0.5f, y + 0.05f, z - 0.6f), new Vector3(len, 0.12f, 0.06f), rack, null, false, true);
                Prim(PrimitiveType.Cube, "Beam", p, new Vector3((x0 + x1) * 0.5f, y + 0.05f, z + 0.6f), new Vector3(len, 0.12f, 0.06f), rack, null, false, true);
                for (int i = 0; i < bays; i++)
                {
                    float bx = x0 + (i + 0.5f) * bay;
                    double r = rnd.NextDouble();
                    if (r < 0.18) continue; // empty bay — shoot-through gap
                    var m = r < 0.5 ? b1 : r < 0.8 ? b2 : wrap;
                    float hgt = (float)(0.8 + rnd.NextDouble() * 0.6);
                    float wdt = bay * (float)(0.6 + rnd.NextDouble() * 0.3);
                    var go = Prim(PrimitiveType.Cube, "Goods", p, new Vector3(bx + (float)(rnd.NextDouble() - 0.5) * 0.4f, y + 0.03f + hgt * 0.5f, z),
                                  new Vector3(wdt, hgt, 1.05f), m, Quaternion.Euler(0f, (float)(rnd.NextDouble() * 4 - 2), 0f), true, true);
                    go.AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
                }
            }
        }

        static void Forklift(Transform p, Vector3 pos, float yaw, Material steel)
        {
            var r = new GameObject("Forklift").transform;
            r.SetParent(p, false);
            r.position = pos; r.rotation = Quaternion.Euler(0f, yaw, 0f);
            var body = Lit("WH_Forklift", new Color(0.92f, 0.68f, 0.08f), 0.2f, 0.45f);
            Prim(PrimitiveType.Cube, "Body", r, new Vector3(0f, 0.75f, 0f), new Vector3(1.2f, 1.1f, 2.2f), body, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            Prim(PrimitiveType.Cube, "Counterweight", r, new Vector3(0f, 0.9f, -1.1f), new Vector3(1.2f, 1f, 0.4f), steel, null, true, true);
            Prim(PrimitiveType.Cube, "Roof", r, new Vector3(0f, 2.15f, -0.1f), new Vector3(1.1f, 0.08f, 1.3f), steel, null, false, true);
            foreach (var x in new[] { -0.5f, 0.5f })
            {
                Prim(PrimitiveType.Cube, "Pillar", r, new Vector3(x, 1.7f, 0.5f), new Vector3(0.08f, 1f, 0.08f), steel, null, false, true);
                Prim(PrimitiveType.Cube, "Mast", r, new Vector3(x, 1.4f, 1.2f), new Vector3(0.12f, 2.8f, 0.12f), steel, null, true, true);
                Prim(PrimitiveType.Cube, "Fork", r, new Vector3(x * 0.6f, 0.1f, 1.8f), new Vector3(0.12f, 0.05f, 1.1f), steel, null, false, true);
            }
        }

        static void PalletStack(Transform p, Vector3 pos, int levels, Material pallet, Material load)
        {
            for (int i = 0; i < levels; i++)
            {
                Prim(PrimitiveType.Cube, "Pallet", p, pos + Vector3.up * (i * 1.15f + 0.07f), new Vector3(1.2f, 0.14f, 1f), pallet, null, true, true);
                Prim(PrimitiveType.Cube, "Load", p, pos + Vector3.up * (i * 1.15f + 0.6f), new Vector3(1.1f, 0.92f, 0.95f), load, Quaternion.Euler(0f, i * 7f, 0f), true, true)
                    .AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            }
        }

        static void Trailer(Transform p, Vector3 rearCenter, Material body, Material cab, Material steel, bool intoDoor)
        {
            var r = new GameObject("Trailer").transform;
            r.SetParent(p, false);
            r.position = rearCenter;
            float dir = intoDoor ? -1f : 1f; // trailer extends away from the rear
            Prim(PrimitiveType.Cube, "Box", r, new Vector3(0f, 2.3f, dir * 6f), new Vector3(2.5f, 2.9f, 12f), body, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            Prim(PrimitiveType.Cube, "Chassis", r, new Vector3(0f, 0.75f, dir * 6f), new Vector3(1.8f, 0.3f, 11.5f), steel, null, false, true);
            foreach (float z in new[] { 1.5f, 2.8f, 10.2f })
                foreach (float x in new[] { -1.05f, 1.05f })
                    Prim(PrimitiveType.Cylinder, "Wheel", r, new Vector3(x, 0.5f, dir * z), new Vector3(1f, 0.15f, 1f), steel, Quaternion.Euler(0f, 0f, 90f), true, true);
            Prim(PrimitiveType.Cube, "Cab", r, new Vector3(0f, 1.9f, dir * 13.6f), new Vector3(2.5f, 2.8f, 2.8f), cab, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            Prim(PrimitiveType.Cube, "Windshield", r, new Vector3(0f, 2.5f, dir * 15.02f), new Vector3(2.2f, 1f, 0.05f), steel, null, false, true);
        }
    }
}
