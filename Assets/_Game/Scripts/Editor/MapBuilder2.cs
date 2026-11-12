using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

namespace FPS.EditorTools
{
    /// <summary>
    /// Two more Deathmatch maps (Plant — industrial boiler house at dusk; Village — forest village with a church
    /// on a raised yard) and a detail kit used to dress all maps: signs, pipes, cables, clutter, trees, roofs.
    /// Original layouts.
    /// </summary>
    public static partial class RangeBuilder
    {
        [MenuItem("Tools/FPS/Maps/Plant")] static void M4() => BuildPlant();
        [MenuItem("Tools/FPS/Maps/Village")] static void M5() => BuildVillage();

        // ================================================================== detail kit

        static GameObject Deco(string name, Transform p, PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default, bool collider = false)
            => Prim(t, name, p, pos, scale, m, Quaternion.Euler(euler), collider, true);

        /// <summary>Cylinder between two points (pipes, rails, wires, logs).</summary>
        static GameObject Pipe(Transform p, Vector3 a, Vector3 b, float d, Material m, bool collider = false, string name = "Pipe")
        {
            Vector3 dir = b - a;
            var go = Prim(PrimitiveType.Cylinder, name, p, (a + b) * 0.5f, new Vector3(d, dir.magnitude * 0.5f, d), m,
                          Quaternion.FromToRotation(Vector3.up, dir.normalized), collider, true);
            return go;
        }

        /// <summary>Thin box between two points (wires, beams, rails).</summary>
        static GameObject Beam(Transform p, Vector3 a, Vector3 b, float w, float h, Material m, bool collider = false, string name = "Beam")
        {
            Vector3 dir = b - a;
            return Prim(PrimitiveType.Cube, name, p, (a + b) * 0.5f, new Vector3(w, h, dir.magnitude), m, Quaternion.LookRotation(dir.normalized), collider, true);
        }

        static Font signFont;

        /// <summary>3D text sign (TextMesh) facing +Z of the given yaw, on a dark plate.</summary>
        static void Sign(Transform p, Vector3 pos, float yaw, string text, Color textColor, float height, Material plate, float plateWidth = 0f)
        {
            if (!signFont) signFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("Sign_" + text).transform;
            root.SetParent(p, false);
            root.position = pos;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            float w = plateWidth > 0f ? plateWidth : height * 0.62f * text.Length + height * 0.6f;
            if (plate) Prim(PrimitiveType.Cube, "Plate", root, Vector3.zero, new Vector3(w, height * 1.5f, 0.04f), plate, null, false, true);
            var tgo = new GameObject("Text");
            tgo.transform.SetParent(root, false);
            tgo.transform.localPosition = new Vector3(0f, 0f, 0.026f);
            tgo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TextMesh reads along -Z: face the viewer
            var tm = tgo.AddComponent<TextMesh>();
            tm.font = signFont;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = height / 6.4f * 1.1f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = textColor;
            tm.fontStyle = FontStyle.Bold;
            var mr = tgo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = signFont.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void TireStack(Transform p, Vector3 pos, int n, Material rubber)
        {
            for (int i = 0; i < n; i++)
                Deco("Tire", p, PrimitiveType.Cylinder, pos + new Vector3(i % 2 * 0.06f, 0.12f + i * 0.24f, 0f), new Vector3(0.9f, 0.12f, 0.9f), rubber, new Vector3(0f, i * 23f, 0f), i == 0);
        }

        static void Cone(Transform p, Vector3 pos, Material orange, Material white)
        {
            Deco("Cone", p, PrimitiveType.Cylinder, pos + Vector3.up * 0.3f, new Vector3(0.22f, 0.3f, 0.22f), orange);
            Deco("ConeStripe", p, PrimitiveType.Cylinder, pos + Vector3.up * 0.38f, new Vector3(0.2f, 0.05f, 0.2f), white);
            Deco("ConeBase", p, PrimitiveType.Cube, pos + Vector3.up * 0.02f, new Vector3(0.4f, 0.04f, 0.4f), orange);
        }

        static void CableReel(Transform p, Vector3 pos, float yaw, Material wood, Material cable)
        {
            var r = Quaternion.Euler(0f, yaw, 0f);
            foreach (float s in new[] { -0.45f, 0.45f })
                Prim(PrimitiveType.Cylinder, "ReelSide", p, pos + r * new Vector3(s, 0.75f, 0f), new Vector3(1.5f, 0.04f, 1.5f), wood, r * Quaternion.Euler(0f, 0f, 90f), s < 0f, true);
            Prim(PrimitiveType.Cylinder, "ReelCable", p, pos + Vector3.up * 0.75f, new Vector3(1.1f, 0.44f, 1.1f), cable, r * Quaternion.Euler(0f, 0f, 90f), true, true);
        }

        static void Puddle(Transform p, Vector3 pos, float size, float yaw, Material water)
            => Deco("Puddle", p, PrimitiveType.Cylinder, pos + Vector3.up * 0.006f, new Vector3(size, 0.004f, size * 0.6f), water, new Vector3(0f, yaw, 0f));

        static void Extinguisher(Transform p, Vector3 pos, float yaw, Material red, Material dark)
        {
            var r = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "ExtBoard", p, pos + Vector3.up * 1.2f, new Vector3(0.5f, 0.7f, 0.03f), red, r, false, true);
            Prim(PrimitiveType.Cylinder, "Extinguisher", p, pos + r * new Vector3(0f, 1.15f, 0.1f), new Vector3(0.16f, 0.22f, 0.16f), red, r, false, true);
            Prim(PrimitiveType.Cube, "ExtHead", p, pos + r * new Vector3(0f, 1.42f, 0.1f), new Vector3(0.06f, 0.06f, 0.1f), dark, r, false, true);
        }

        static void ElectricBox(Transform p, Vector3 pos, float yaw, Material grey, Material yellow)
        {
            var r = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "ElecBox", p, pos + r * new Vector3(0f, 1.4f, 0.12f), new Vector3(0.8f, 1f, 0.25f), grey, r, false, true);
            Prim(PrimitiveType.Cube, "ElecSign", p, pos + r * new Vector3(0f, 1.6f, 0.252f), new Vector3(0.18f, 0.16f, 0.01f), yellow, r * Quaternion.Euler(0f, 0f, 45f), false, true);
            Prim(PrimitiveType.Cube, "Conduit", p, pos + r * new Vector3(0.25f, 0.45f, 0.06f), new Vector3(0.06f, 0.9f, 0.06f), grey, r, false, true);
        }

        /// <summary>Gable roof over a rectangle; ridge along X (or Z). Two tilted slabs + gable triangles approximated by boxes.</summary>
        static void GableRoof(Transform p, float minX, float minZ, float maxX, float maxZ, float baseY, float rise, Material roof, Material gable, bool ridgeAlongX, float overhang = 0.45f)
        {
            float w = maxX - minX, d = maxZ - minZ;
            float half = (ridgeAlongX ? d : w) * 0.5f;
            float ang = Mathf.Atan2(rise, half) * Mathf.Rad2Deg;
            float slope = Mathf.Sqrt(half * half + rise * rise) + overhang;
            Vector3 c = new Vector3((minX + maxX) * 0.5f, baseY, (minZ + maxZ) * 0.5f);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 off, size; Quaternion rot;
                if (ridgeAlongX)
                {
                    off = new Vector3(0f, rise * 0.5f, s * (half - overhang) * 0.5f);
                    size = new Vector3(w + overhang * 2f, 0.16f, slope);
                    rot = Quaternion.Euler(s * ang, 0f, 0f);
                }
                else
                {
                    off = new Vector3(s * (half - overhang) * 0.5f, rise * 0.5f, 0f);
                    size = new Vector3(slope, 0.16f, d + overhang * 2f);
                    rot = Quaternion.Euler(0f, 0f, -s * ang);
                }
                Prim(PrimitiveType.Cube, "Roof", p, c + off, size, roof, rot, true, true);
            }
            // gable ends: stacked shrinking boxes
            int steps = 5;
            for (int i = 0; i < steps; i++)
            {
                float t0 = i / (float)steps;
                float hgt = rise / steps;
                float span = (ridgeAlongX ? d : w) * (1f - t0 - 0.5f / steps);
                foreach (int e in new[] { -1, 1 })
                {
                    Vector3 pos = ridgeAlongX ? new Vector3(e > 0 ? maxX : minX, baseY + hgt * (i + 0.5f), c.z) : new Vector3(c.x, baseY + hgt * (i + 0.5f), e > 0 ? maxZ : minZ);
                    Vector3 sz = ridgeAlongX ? new Vector3(0.3f, hgt, span) : new Vector3(span, hgt, 0.3f);
                    Prim(PrimitiveType.Cube, "Gable", p, pos, sz, gable, null, false, true);
                }
            }
        }

        static void Tree(Transform p, Vector3 pos, float h, Material bark, Material leaves, Random r, bool collider, bool pine)
        {
            var t = new GameObject(pine ? "Pine" : "Tree").transform;
            t.SetParent(p, false);
            t.position = pos;
            t.rotation = Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f);
            float trunk = h * (pine ? 0.035f : 0.05f);
            Prim(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0f, h * 0.5f, 0f), new Vector3(trunk, h * 0.5f, trunk), bark, null, collider, true);
            if (pine)
            {
                int layers = 5;
                for (int i = 0; i < layers; i++)
                {
                    float k = i / (float)layers;
                    float rad = Mathf.Lerp(h * 0.28f, h * 0.06f, k);
                    Prim(PrimitiveType.Cylinder, "Needles", t, new Vector3(0f, h * (0.3f + 0.15f * i), 0f), new Vector3(rad * 2f, h * 0.07f, rad * 2f), leaves,
                         Quaternion.Euler(0f, i * 31f, 0f), false, true);
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    var o = new Vector3((float)(r.NextDouble() - 0.5) * h * 0.25f, h * (0.72f + (float)r.NextDouble() * 0.18f), (float)(r.NextDouble() - 0.5) * h * 0.25f);
                    float s = h * (0.3f + (float)r.NextDouble() * 0.15f);
                    Prim(PrimitiveType.Sphere, "Crown", t, o, new Vector3(s, s * 0.8f, s), leaves, null, false, true);
                }
            }
        }

        static void Bush(Transform p, Vector3 pos, float s, Material leaves)
        {
            Deco("Bush", p, PrimitiveType.Sphere, pos + Vector3.up * s * 0.35f, new Vector3(s, s * 0.7f, s * 0.9f), leaves);
            Deco("Bush", p, PrimitiveType.Sphere, pos + new Vector3(s * 0.35f, s * 0.3f, s * 0.2f), new Vector3(s * 0.7f, s * 0.55f, s * 0.7f), leaves);
        }

        /// <summary>Wooden fence: posts + two rails; only the top rail has a collider (bots path around, bullets mostly pass).</summary>
        static void Fence(Transform p, Vector3 a, Vector3 b, Material wood, float h = 1.15f)
        {
            Vector3 d = b - a; d.y = 0f;
            float L = d.magnitude; if (L < 0.2f) return;
            d /= L;
            var rot = Quaternion.LookRotation(d);
            Prim(PrimitiveType.Cube, "FenceRail", p, (a + b) * 0.5f + Vector3.up * h * 0.85f, new Vector3(0.06f, 0.1f, L), wood, rot, true, true);
            Prim(PrimitiveType.Cube, "FenceRail", p, (a + b) * 0.5f + Vector3.up * h * 0.4f, new Vector3(0.06f, 0.1f, L), wood, rot, false, true);
            int n = Mathf.Max(2, Mathf.CeilToInt(L / 0.35f));
            for (int i = 0; i <= n; i++)
            {
                float hh = h * (i % 2 == 0 ? 1f : 0.92f);
                Prim(PrimitiveType.Cube, "Picket", p, a + d * (L * i / n) + Vector3.up * hh * 0.5f, new Vector3(0.09f, hh, 0.04f), wood, rot, false, true);
            }
        }

        static void Haystack(Transform p, Vector3 pos, float s, Material hay)
        {
            Prim(PrimitiveType.Cylinder, "Haystack", p, pos + Vector3.up * s * 0.6f, new Vector3(s * 1.6f, s * 0.6f, s * 1.6f), hay, null, true, true);
            Deco("HayTop", p, PrimitiveType.Sphere, pos + Vector3.up * s * 1.2f, new Vector3(s * 1.6f, s * 1.2f, s * 1.6f), hay);
        }

        static void HayBale(Transform p, Vector3 pos, float yaw, Material hay)
            => Prim(PrimitiveType.Cylinder, "HayBale", p, pos + Vector3.up * 0.6f, new Vector3(1.2f, 0.65f, 1.2f), hay, Quaternion.Euler(0f, yaw, 90f), true, true);

        static void LogPile(Transform p, Vector3 pos, float yaw, Material bark, Material cut)
        {
            var r = Quaternion.Euler(0f, yaw, 0f);
            int[] rows = { 4, 3, 2 };
            for (int row = 0; row < rows.Length; row++)
                for (int i = 0; i < rows[row]; i++)
                {
                    Vector3 o = new Vector3((i - (rows[row] - 1) * 0.5f) * 0.36f, 0.18f + row * 0.31f, 0f);
                    Prim(PrimitiveType.Cylinder, "Log", p, pos + r * o, new Vector3(0.34f, 1.4f, 0.34f), i % 2 == 0 ? bark : cut, r * Quaternion.Euler(90f, 0f, 0f), row == 0 && i == 0, true);
                }
            Prim(PrimitiveType.Cube, "LogPileCollider", p, pos + Vector3.up * 0.5f, new Vector3(1.5f, 1f, 2.8f), bark, r, true, true).GetComponent<Renderer>().enabled = false;
        }

        static void PowerPole(Transform p, Vector3 pos, float yaw, Material wood, Material dark)
        {
            Prim(PrimitiveType.Cylinder, "Pole", p, pos + Vector3.up * 4f, new Vector3(0.24f, 4f, 0.24f), wood, null, true, true);
            Prim(PrimitiveType.Cube, "Crossbar", p, pos + Vector3.up * 7.4f, new Vector3(1.8f, 0.12f, 0.12f), wood, Quaternion.Euler(0f, yaw, 0f), false, true);
            foreach (float x in new[] { -0.75f, 0f, 0.75f })
                Prim(PrimitiveType.Cylinder, "Insulator", p, pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(x, 7.55f, 0f), new Vector3(0.07f, 0.08f, 0.07f), dark, null, false, true);
        }

        static void Wires(Transform p, Vector3 a, Vector3 b, float yaw, Material dark)
        {
            var r = Quaternion.Euler(0f, yaw, 0f);
            foreach (float x in new[] { -0.75f, 0f, 0.75f })
            {
                Vector3 s = a + r * new Vector3(x, 7.6f, 0f), e = b + r * new Vector3(x, 7.6f, 0f);
                Vector3 mid = (s + e) * 0.5f + Vector3.down * 0.45f; // sag
                Beam(p, s, mid, 0.025f, 0.025f, dark, false, "Wire");
                Beam(p, mid, e, 0.025f, 0.025f, dark, false, "Wire");
            }
        }

        static Material NoiseMat(string name, Color a, Color b, float scale, int seed, Vector2 tiling, float smooth)
            => TexLit(name, NoiseTexture(name + "Tex", a, b, scale, seed), tiling, smooth);

        /// <summary>Horizontal log / plank stripes (izba walls, barn boards).</summary>
        static Material StripeMat(string name, Color a, Color b, int stripes, Vector2 tiling, float smooth)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = name + "Tex", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
            {
                float band = (y * stripes / (float)N) % 1f;
                float shade = Mathf.Sin(band * Mathf.PI); // round log profile
                for (int x = 0; x < N; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.08f, y * 0.6f) * 0.25f;
                    px[y * N + x] = Color.Lerp(b, a, Mathf.Clamp01(shade * 0.85f + n));
                }
            }
            t.SetPixels(px); t.Apply(true);
            return TexLit(name, t, tiling, smooth);
        }

        static GameObject Bound(Transform p, Vector3 center, Vector3 size)
        {
            var go = Prim(PrimitiveType.Cube, "Boundary", p, center, size, null, null, true, true);
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(go.GetComponent<MeshFilter>());
            return go;
        }

        // ================================================================== PLANT (boiler house at dusk)

        static void BuildPlant()
        {
            var light = new LightPreset
            {
                name = "Plant", sunIntensity = 1.25f, sunColor = new Color(1f, 0.62f, 0.38f), sunEuler = new Vector3(14f, 250f, 0f),
                ambSky = new Color(0.36f, 0.38f, 0.5f), ambEquator = new Color(0.42f, 0.33f, 0.3f), ambGround = new Color(0.14f, 0.12f, 0.12f),
                fogColor = new Color(0.55f, 0.42f, 0.38f), fogStart = 45f, fogEnd = 240f,
                skyTint = new Color(0.62f, 0.42f, 0.38f), atmosphere = 2.2f, skyExposure = 1.05f,
                exposure = 0.45f, contrast = 20f, saturation = 6f, bloom = 1.1f,
                probeCenter = new Vector3(0f, 5f, 0f), probeSize = new Vector3(95f, 40f, 85f)
            };
            (float, float, float, float)[] spawns =
            {
                (-40f, 0f, -35f, 45f), (38f, 0f, 33f, -135f), (-38f, 0f, 30f, 135f), (36f, 0f, -33f, -45f),
                (0f, 0f, -24f, 0f), (-11f, 0f, 1.5f, 0f), (13f, 0f, 1.5f, 0f), (0f, 4.5f, 23.5f, 180f),
                (-36f, 0f, -2f, 90f), (24f, 0f, 30f, -90f), (-8f, 0f, -36f, 0f), (12f, 0f, -14f, -30f),
            };
            BuildDM("Map_Plant", "Завод", light, PlantLayout, spawns);
        }

        static void PlantLayout(Transform env)
        {
            var ground = NoiseMat("Plant_Ground", new Color(0.2f, 0.2f, 0.21f), new Color(0.32f, 0.31f, 0.3f), 5f, 7, new Vector2(20f, 20f), 0.2f);
            var concrete = NoiseMat("Plant_Concrete", new Color(0.46f, 0.45f, 0.43f), new Color(0.56f, 0.55f, 0.52f), 6f, 8, new Vector2(10f, 3f), 0.12f);
            var brick = StripeMat("Plant_Brick", new Color(0.58f, 0.28f, 0.2f), new Color(0.38f, 0.17f, 0.13f), 24, new Vector2(6f, 3f), 0.1f);
            var steel = Lit("Plant_Steel", new Color(0.24f, 0.26f, 0.28f), 0.8f, 0.45f);
            var rust = Lit("Plant_Rust", new Color(0.45f, 0.22f, 0.12f), 0.4f, 0.25f);
            var boilerM = Lit("Plant_Boiler", new Color(0.52f, 0.55f, 0.56f), 0.6f, 0.4f);
            var pipeM = Lit("Plant_Pipe", new Color(0.55f, 0.58f, 0.5f), 0.5f, 0.4f);
            var pipeRed = Lit("Plant_PipeRed", new Color(0.62f, 0.14f, 0.1f), 0.4f, 0.4f);
            var pipeYel = Lit("Plant_PipeYellow", new Color(0.85f, 0.65f, 0.1f), 0.4f, 0.4f);
            var tankM = Lit("Plant_Tank", new Color(0.78f, 0.78f, 0.74f), 0.4f, 0.4f);
            var grate = Lit("Plant_Grate", new Color(0.18f, 0.19f, 0.2f), 0.85f, 0.35f);
            var wagon = Lit("Plant_Wagon", new Color(0.32f, 0.18f, 0.12f), 0.3f, 0.3f);
            var wagon2 = Lit("Plant_Wagon2", new Color(0.18f, 0.26f, 0.22f), 0.3f, 0.3f);
            var wood = Lit("Plant_Wood", new Color(0.45f, 0.33f, 0.22f), 0f, 0.25f);
            var crateM = Lit("Plant_Crate", new Color(0.5f, 0.38f, 0.24f), 0f, 0.25f);
            var plaster = Lit("Plant_Office", new Color(0.7f, 0.68f, 0.6f), 0f, 0.12f);
            var stone = Lit("Plant_Stone", new Color(0.5f, 0.49f, 0.46f), 0f, 0.1f);
            var signPlate = Lit("Plant_SignPlate", new Color(0.12f, 0.2f, 0.32f), 0f, 0.3f);
            var red = Lit("Plant_Red", new Color(0.75f, 0.1f, 0.08f), 0f, 0.4f);
            var white = Lit("Plant_White", new Color(0.88f, 0.88f, 0.86f), 0f, 0.3f);
            var hazard = Lit("Plant_Hazard", new Color(0.95f, 0.75f, 0.1f), 0f, 0.3f);
            var rubber = Lit("Plant_Rubber", new Color(0.06f, 0.06f, 0.06f), 0f, 0.35f);
            var water = Lit("Plant_Water", new Color(0.05f, 0.06f, 0.07f), 0.1f, 0.95f);
            var sodium = Emissive("Plant_Sodium", new Color(1f, 0.7f, 0.35f), 4f);
            var glow = Emissive("Plant_Glow", new Color(1f, 0.45f, 0.15f), 3f);
            var bags = Lit("Port_Sandbag", new Color(0.55f, 0.5f, 0.38f), 0f, 0.1f);

            var root = new GameObject("Plant").transform;
            root.SetParent(env, false);
            Box("Ground", root, new Vector3(0f, -0.5f, 0f), new Vector3(130f, 1f, 120f), ground);
            Room(root, -44f, -38f, 44f, 38f, 0f, 4.5f, 0.6f, concrete);
            // wall coping and barbed wire
            foreach (var (a, b) in new[] { (new Vector3(-44.3f, 0, -38f), new Vector3(44.3f, 0, -38f)), (new Vector3(-44.3f, 0, 38f), new Vector3(44.3f, 0, 38f)) })
            {
                Prim(PrimitiveType.Cube, "Coping", root, (a + b) * 0.5f + Vector3.up * 4.55f, new Vector3(88.6f, 0.12f, 0.8f), stone, null, false, true);
                Pipe(root, a + Vector3.up * 4.85f, b + Vector3.up * 4.85f, 0.03f, steel, false, "Wire");
            }
            for (float x = -40f; x <= 40f; x += 8f) { Prim(PrimitiveType.Cube, "Pilaster", root, new Vector3(x, 2.25f, -37.6f), new Vector3(0.6f, 4.5f, 0.3f), concrete, null, false, true); Prim(PrimitiveType.Cube, "Pilaster", root, new Vector3(x, 2.25f, 37.6f), new Vector3(0.6f, 4.5f, 0.3f), concrete, null, false, true); }

            // ---- boiler hall
            const float HH = 12f;
            Room(root, -22f, 0f, 22f, 26f, 0f, HH, 0.5f, brick,
                 south: new[] { Door(12f, 5f, 5f), Door(32f, 5f, 5f), Window(22f, 3f, 7f, 10f), Window(4f, 2.4f, 7f, 10f), Window(40f, 2.4f, 7f, 10f) },
                 north: new[] { Window(8f, 3f, 7f, 10f), Window(22f, 3f, 7f, 10f), Window(36f, 3f, 7f, 10f) },
                 west: new[] { Door(4f, 1.6f, 2.5f), Window(14f, 3f, 7f, 10f), Door(23.5f, 1.6f, 6.9f) },
                 east: new[] { Door(4f, 1.6f, 2.5f), Window(14f, 3f, 7f, 10f), Door(23.5f, 1.6f, 6.9f) });
            Slab(root, -22.5f, -0.5f, 22.5f, 26.5f, HH + 0.4f, 0.4f, steel);
            for (int i = -2; i <= 2; i++) Block("Truss", root, i * 9f, 13f, new Vector3(0.4f, 0.9f, 25.5f), steel, HH - 0.9f);
            Sign(root, new Vector3(0f, 6.2f, -0.28f), 180f, "КОТЕЛЬНАЯ № 2", Color.white, 0.8f, signPlate);
            Sign(root, new Vector3(-12f, 5.8f, -0.28f), 180f, "ЦЕХ 1", Color.white, 0.45f, signPlate);
            Sign(root, new Vector3(12f, 5.8f, -0.28f), 180f, "ЦЕХ 2", Color.white, 0.45f, signPlate);

            // boilers with domes, gauges and stacks
            foreach (float bx in new[] { -12f, 0f, 12f })
            {
                Block("BoilerBase", root, bx, 15f, new Vector3(5.6f, 0.6f, 5.6f), concrete);
                Prim(PrimitiveType.Cylinder, "Boiler", root, new Vector3(bx, 4.1f, 15f), new Vector3(4.8f, 3.5f, 4.8f), boilerM, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
                Deco("BoilerDome", root, PrimitiveType.Sphere, new Vector3(bx, 7.6f, 15f), new Vector3(4.8f, 1.8f, 4.8f), boilerM);
                for (int k = 0; k < 3; k++) Deco("BoilerBand", root, PrimitiveType.Cylinder, new Vector3(bx, 1.6f + k * 2.4f, 15f), new Vector3(4.9f, 0.08f, 4.9f), steel);
                Deco("Firebox", root, PrimitiveType.Cube, new Vector3(bx, 1.4f, 12.55f), new Vector3(1.4f, 1.1f, 0.15f), rust);
                Deco("FireGlow", root, PrimitiveType.Cube, new Vector3(bx, 1.3f, 12.47f), new Vector3(0.9f, 0.5f, 0.02f), glow);
                Deco("Gauge", root, PrimitiveType.Cylinder, new Vector3(bx + 1.2f, 3.2f, 12.6f), new Vector3(0.35f, 0.03f, 0.35f), white, new Vector3(90f, 0f, 0f));
                Pipe(root, new Vector3(bx, 8.3f, 15f), new Vector3(bx, HH, 15f), 0.7f, steel);
                Pipe(root, new Vector3(bx + 1.6f, 6.5f, 15f), new Vector3(bx + 1.6f, 9f, 15f), 0.35f, pipeRed);
                var lgo = new GameObject("FireLight"); lgo.transform.SetParent(root, false); lgo.transform.position = new Vector3(bx, 1.4f, 12f);
                var fl = lgo.AddComponent<Light>(); fl.type = LightType.Point; fl.color = new Color(1f, 0.5f, 0.2f); fl.intensity = 2.2f; fl.range = 6f; fl.shadows = LightShadows.None;
            }
            Pipe(root, new Vector3(-17f, 9f, 15f), new Vector3(17f, 9f, 15f), 0.5f, pipeRed, false, "Header");
            Pipe(root, new Vector3(-21.7f, 3.4f, 1f), new Vector3(-21.7f, 3.4f, 25f), 0.3f, pipeYel);
            Pipe(root, new Vector3(21.7f, 3.4f, 1f), new Vector3(21.7f, 3.4f, 25f), 0.3f, pipeYel);
            Pipe(root, new Vector3(-21.6f, 6.4f, 1f), new Vector3(21.6f, 6.4f, 1f), 0.25f, pipeM);
            // low pipes to vault over
            Pipe(root, new Vector3(-9f, 0.45f, 5.5f), new Vector3(9f, 0.45f, 5.5f), 0.7f, pipeM, true, "LowPipe");
            foreach (float x in new[] { -8f, 0f, 8f }) Block("PipeSaddle", root, x, 5.5f, new Vector3(0.3f, 0.2f, 0.9f), steel);

            // catwalk level (4.5 m): north strip, side strips, a bridge in front of the boilers
            const float CW = 4.5f;
            Slab(root, -21.75f, 21.5f, 21.75f, 25.75f, CW, 0.25f, grate);
            Slab(root, -21.75f, 7.2f, -17.5f, 21.5f, CW, 0.25f, grate);
            Slab(root, 17.5f, 7.2f, 21.75f, 21.5f, CW, 0.25f, grate);
            Slab(root, -17.5f, 7.4f, 17.5f, 9.2f, CW, 0.25f, grate);
            Railing(root, new Vector3(-17.5f, 0, 21.5f), new Vector3(-8.2f, 0, 21.5f), CW, steel);
            Railing(root, new Vector3(8.2f, 0, 21.5f), new Vector3(17.5f, 0, 21.5f), CW, steel);
            Railing(root, new Vector3(-17.5f, 0, 9.2f), new Vector3(-17.5f, 0, 21.5f), CW, steel);
            Railing(root, new Vector3(17.5f, 0, 9.2f), new Vector3(17.5f, 0, 21.5f), CW, steel);
            Railing(root, new Vector3(-16.5f, 0, 7.4f), new Vector3(16.5f, 0, 7.4f), CW, steel);
            Railing(root, new Vector3(-16.5f, 0, 9.2f), new Vector3(16.5f, 0, 9.2f), CW, steel);
            foreach (float x in new[] { -19.6f, 19.6f })
            {
                Stairs(root, new Vector3(x, 0f, 0.3f), Vector3.forward, 1.6f, CW, steel);
                for (float z = 9f; z <= 21f; z += 6f) Block("CatwalkPost", root, x + (x < 0 ? -1.9f : 1.9f), z, new Vector3(0.2f, CW - 0.25f, 0.2f), steel);
            }
            for (float x = -15f; x <= 15f; x += 7.5f) Block("BridgePost", root, x, 8.3f, new Vector3(0.2f, CW - 0.25f, 0.2f), steel);
            // control room under the north catwalk
            Room(root, -8f, 21.6f, 8f, 25.7f, 0f, CW - 0.25f, 0.2f, plaster,
                 south: new[] { Door(4f, 1.2f, 2.3f), Window(9f, 3f, 1.1f, 2.6f), Window(13f, 2f, 1.1f, 2.6f) });
            Block("ControlDesk", root, 0f, 24.6f, new Vector3(6f, 0.95f, 1f), steel);
            Deco("Screens", root, PrimitiveType.Cube, new Vector3(0f, 1.35f, 24.9f), new Vector3(5f, 0.5f, 0.06f), Emissive("Plant_Screen", new Color(0.3f, 0.9f, 0.6f), 1.5f));
            Lamp(root, new Vector3(0f, 0f, 23.6f), 3.9f, new Color(0.8f, 1f, 0.9f), 1.6f, 8f, null, sodium);
            // catwalk cover
            Crate(root, new Vector3(-14f, CW, 23.4f), 1.1f, 12f, crateM);
            Crate(root, new Vector3(12f, CW, 22.8f), 1.1f, -18f, crateM);
            Block("CatwalkTank", root, -19.6f, 14f, new Vector3(1.4f, 1.2f, 2.2f), tankM, CW);
            Block("CatwalkTank", root, 19.6f, 16f, new Vector3(1.4f, 1.2f, 2.2f), tankM, CW);
            // floor cover
            Crate(root, new Vector3(-17f, 0f, 3f), 1.2f, 8f, crateM); Crate(root, new Vector3(-15.8f, 0f, 3.2f), 1.2f, -6f, crateM); Crate(root, new Vector3(-16.4f, 1.2f, 3.1f), 1f, 30f, crateM);
            Crate(root, new Vector3(15f, 0f, 3.5f), 1.2f, 20f, crateM);
            PalletStack(root, new Vector3(-6f, 0f, 2.5f), 2, wood, crateM);
            PalletStack(root, new Vector3(6.5f, 0f, 11f), 1, wood, crateM);
            Block("Pump", root, -5f, 11f, new Vector3(1.6f, 1.2f, 1.1f), pipeYel).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            Prim(PrimitiveType.Cylinder, "PumpMotor", root, new Vector3(-3.8f, 0.6f, 11f), new Vector3(0.8f, 0.5f, 0.8f), steel, Quaternion.Euler(0f, 0f, 90f), true, true);
            Block("Cabinet", root, -21.2f, 18f, new Vector3(0.7f, 2f, 1.6f), steel);
            Block("Cabinet", root, 21.2f, 11f, new Vector3(0.7f, 2f, 1.6f), steel);
            for (int i = 0; i < 3; i++) ElectricBox(root, new Vector3(-3f + i * 3f, 0f, 0.26f), 0f, steel, hazard);
            Extinguisher(root, new Vector3(-21.74f, 0f, 9f), 90f, red, steel);
            Extinguisher(root, new Vector3(21.74f, 0f, 19f), -90f, red, steel);
            foreach (float x in new[] { -10f, 10f })
                for (int i = 0; i < 6; i++) Deco("Hazard", root, PrimitiveType.Cube, new Vector3(x - 2.5f + i * 1f, 0.006f, 0.6f), new Vector3(0.5f, 0.01f, 1.2f), i % 2 == 0 ? hazard : rubber);
            for (int ix = -1; ix <= 1; ix++)
                for (int iz = 0; iz < 2; iz++)
                    Lamp(root, new Vector3(ix * 13f, 0f, 5f + iz * 13f), HH - 0.6f, new Color(1f, 0.82f, 0.6f), 5f, 18f, null, sodium);

            // ---- chimney (landmark) and smaller stack
            Prim(PrimitiveType.Cylinder, "Chimney", root, new Vector3(33f, 22f, 30f), new Vector3(4.4f, 22f, 4.4f), brick, null, true, true);
            for (int i = 0; i < 4; i++) Deco("ChimneyBand", root, PrimitiveType.Cylinder, new Vector3(33f, 36f + i * 2f, 30f), new Vector3(4.5f, 1f, 4.5f), i % 2 == 0 ? red : white);
            Deco("ChimneyLight", root, PrimitiveType.Sphere, new Vector3(33f, 44.3f, 32.3f), Vector3.one * 0.5f, Emissive("Plant_Beacon", new Color(1f, 0.1f, 0.05f), 8f));
            Pipe(root, new Vector3(22f, 8f, 23f), new Vector3(31f, 8f, 28.5f), 1.2f, steel, false, "Flue");
            Prim(PrimitiveType.Cylinder, "Stack", root, new Vector3(-34f, 9f, 32f), new Vector3(2f, 9f, 2f), steel, null, true, true);

            // ---- tank farm (south-west): horizontal tanks on saddles
            foreach (float tx in new[] { -34f, -26.5f, -19f })
            {
                Prim(PrimitiveType.Cylinder, "Tank", root, new Vector3(tx, 2.25f, -16f), new Vector3(3.3f, 4.6f, 3.3f), tankM, Quaternion.Euler(90f, 0f, 0f), true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
                foreach (float z in new[] { -19.5f, -12.5f }) Block("Saddle", root, tx, z, new Vector3(2.6f, 0.7f, 0.5f), concrete);
                Deco("TankCapN", root, PrimitiveType.Sphere, new Vector3(tx, 2.25f, -11.4f), new Vector3(3.3f, 3.3f, 0.8f), tankM);
                Deco("TankCapS", root, PrimitiveType.Sphere, new Vector3(tx, 2.25f, -20.6f), new Vector3(3.3f, 3.3f, 0.8f), tankM);
                Pipe(root, new Vector3(tx, 3.9f, -16f), new Vector3(tx, 5.2f, -16f), 0.3f, steel);
            }
            Pipe(root, new Vector3(-36f, 5.2f, -16f), new Vector3(-17f, 5.2f, -16f), 0.28f, pipeYel, false, "TankLine");
            Fence(root, new Vector3(-38f, 0, -9.5f), new Vector3(-24f, 0, -9.5f), steel);
            Sign(root, new Vector3(-27f, 1.6f, -9.42f), 0f, "ОГНЕОПАСНО", new Color(0.9f, 0.15f, 0.1f), 0.32f, white);
            // vertical tanks (south-east)
            foreach (var (x, z, r) in new[] { (22f, -19f, 3.6f), (32f, -25f, 3f) })
            {
                Prim(PrimitiveType.Cylinder, "Silo", root, new Vector3(x, 4.5f, z), new Vector3(r * 2f, 4.5f, r * 2f), tankM, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
                Deco("SiloTop", root, PrimitiveType.Cylinder, new Vector3(x, 9.05f, z), new Vector3(r * 2f + 0.2f, 0.05f, r * 2f + 0.2f), steel);
                Deco("SiloLadder", root, PrimitiveType.Cube, new Vector3(x - r - 0.05f, 4.5f, z), new Vector3(0.08f, 9f, 0.45f), steel);
                for (int i = 0; i < 3; i++) Deco("SiloRing", root, PrimitiveType.Cylinder, new Vector3(x, 2f + i * 3f, z), new Vector3(r * 2f + 0.06f, 0.06f, r * 2f + 0.06f), steel);
            }
            Sandbags(root, new Vector3(16f, 0f, -14f), 30f, 3f, bags);
            Sandbags(root, new Vector3(-12f, 0f, -22f), 0f, 3.2f, bags);

            // ---- railway siding along the south wall
            Pipe(root, new Vector3(-43.5f, 0.08f, -30.7f), new Vector3(43.5f, 0.08f, -30.7f), 0.1f, steel, false, "Rail");
            Pipe(root, new Vector3(-43.5f, 0.08f, -32.2f), new Vector3(43.5f, 0.08f, -32.2f), 0.1f, steel, false, "Rail");
            for (float x = -43f; x <= 43f; x += 1.1f) Deco("Sleeper", root, PrimitiveType.Cube, new Vector3(x, 0.03f, -31.45f), new Vector3(0.25f, 0.06f, 2.4f), wood);
            Wagon(root, new Vector3(-10f, 0f, -31.45f), wagon, steel, false);
            Wagon(root, new Vector3(16f, 0f, -31.45f), wagon2, steel, false);
            Wagon(root, new Vector3(-31f, 0f, -31.45f), tankM, steel, true);

            // ---- office (east) and annex with roof access (west)
            TwoStorey(root, 28f, -12f, 40f, 8f, plaster, stone, wood, false);
            Sign(root, new Vector3(27.75f, 3f, -2f), -90f, "УПРАВЛЕНИЕ", Color.white, 0.4f, signPlate);
            RoofHouse(root, -42f, 2f, -30f, 16f, plaster, stone, wood, false);
            Sign(root, new Vector3(-36f, 2.9f, 1.75f), 180f, "СКЛАД ГСМ", Color.white, 0.35f, signPlate);
            // pipe rack between the hall and the office
            foreach (float x in new[] { 23.5f, 27f })
            {
                Block("RackPost", root, x, 3f, new Vector3(0.3f, 6.2f, 0.3f), steel);
                Block("RackPost", root, x, 6f, new Vector3(0.3f, 6.2f, 0.3f), steel);
            }
            foreach (var (y, z, m) in new[] { (6.3f, 3.6f, pipeM), (6.3f, 4.5f, pipeRed), (6.3f, 5.4f, pipeYel) })
                Pipe(root, new Vector3(22.2f, y, z), new Vector3(27.8f, y, z), 0.45f, m);
            // yard clutter
            CableReel(root, new Vector3(5f, 0f, -12f), 20f, wood, rubber);
            CableReel(root, new Vector3(-4f, 0f, -28f), -40f, wood, rubber);
            TireStack(root, new Vector3(9f, 0f, -26f), 4, rubber);
            TireStack(root, new Vector3(9.9f, 0f, -26.4f), 3, rubber);
            Barrel(root, new Vector3(-8f, 0f, -8f), rust); Barrel(root, new Vector3(-7.3f, 0f, -8.5f), rust); Barrel(root, new Vector3(-7.7f, 0f, -7.4f), pipeRed);
            Barrel(root, new Vector3(25f, 0f, 12f), rust); Barrel(root, new Vector3(25.7f, 0f, 12.4f), rust);
            Crate(root, new Vector3(2f, 0f, -18f), 1.2f, 15f, crateM); Crate(root, new Vector3(3.2f, 0f, -18.3f), 1.2f, -8f, crateM);
            Crate(root, new Vector3(-24f, 0f, -2f), 1.2f, 0f, crateM); Crate(root, new Vector3(-24f, 1.2f, -2f), 1f, 25f, crateM);
            Crate(root, new Vector3(36f, 0f, 15f), 1.2f, 10f, crateM);
            Container(root, -10f, 32f, 0f, Lit("Cont_Red", new Color(0.62f, 0.14f, 0.1f), 0.4f, 0.35f), steel);
            Container(root, 10f, 31.5f, 0f, Lit("Cont_Blue", new Color(0.1f, 0.28f, 0.55f), 0.4f, 0.35f), steel, 2);
            Container(root, 38.5f, 22f, 90f, Lit("Cont_Grey", new Color(0.5f, 0.52f, 0.55f), 0.4f, 0.35f), steel);
            Block("Barrier", root, -2f, -6f, new Vector3(2.4f, 0.9f, 0.6f), concrete).AddComponent<SurfaceTag>().kind = SurfaceKind.Concrete;
            Block("Barrier", root, 4f, -4f, new Vector3(2.4f, 0.9f, 0.6f), concrete, 0f, 25f).AddComponent<SurfaceTag>().kind = SurfaceKind.Concrete;
            Block("Barrier", root, -20f, -27f, new Vector3(2.4f, 0.9f, 0.6f), concrete, 0f, 80f).AddComponent<SurfaceTag>().kind = SurfaceKind.Concrete;
            for (int i = 0; i < 5; i++) Puddle(root, new Vector3(-30f + i * 14f, 0f, -6f + (i % 2) * 9f), 2.5f + i % 3, i * 40f, water);
            Cone(root, new Vector3(1f, 0f, -9f), hazard, white); Cone(root, new Vector3(2f, 0f, -9.4f), hazard, white); Cone(root, new Vector3(-14f, 0f, -36f), hazard, white);
            // yard lights along the walls
            for (int i = 0; i < 5; i++)
            {
                Lamp(root, new Vector3(-36f + i * 18f, 0f, -36.6f), 7f, new Color(1f, 0.72f, 0.4f), 2.4f, 16f, steel, sodium);
                Lamp(root, new Vector3(-43.2f, 0f, -28f + i * 14f), 6.5f, new Color(1f, 0.72f, 0.4f), 1.8f, 13f, steel, sodium);
            }
            Lamp(root, new Vector3(0f, 0f, -0.9f), 6f, new Color(1f, 0.75f, 0.45f), 3f, 14f, null, sodium);
        }

        static void Wagon(Transform p, Vector3 center, Material body, Material steel, bool tank)
        {
            var r = new GameObject(tank ? "TankWagon" : "Wagon").transform;
            r.SetParent(p, false);
            r.position = center;
            Prim(PrimitiveType.Cube, "Chassis", r, new Vector3(0f, 0.95f, 0f), new Vector3(12f, 0.3f, 2.4f), steel, null, true, true);
            foreach (float x in new[] { -4.6f, -3.4f, 3.4f, 4.6f })
                foreach (float z in new[] { -0.75f, 0.75f })
                    Prim(PrimitiveType.Cylinder, "Wheel", r, new Vector3(x, 0.45f, z), new Vector3(0.9f, 0.06f, 0.9f), steel, Quaternion.Euler(90f, 0f, 0f), false, true);
            Prim(PrimitiveType.Cube, "Bogie", r, new Vector3(-4f, 0.5f, 0f), new Vector3(2.4f, 0.6f, 1.6f), steel, null, true, true);
            Prim(PrimitiveType.Cube, "Bogie", r, new Vector3(4f, 0.5f, 0f), new Vector3(2.4f, 0.6f, 1.6f), steel, null, true, true);
            if (tank)
            {
                Prim(PrimitiveType.Cylinder, "TankBody", r, new Vector3(0f, 2.4f, 0f), new Vector3(2.7f, 5.4f, 2.7f), body, Quaternion.Euler(0f, 0f, 90f), true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
                Prim(PrimitiveType.Cylinder, "Dome", r, new Vector3(0f, 3.85f, 0f), new Vector3(0.9f, 0.2f, 0.9f), steel, null, false, true);
            }
            else
            {
                Prim(PrimitiveType.Cube, "Box", r, new Vector3(0f, 2.65f, 0f), new Vector3(11.8f, 3.1f, 2.8f), body, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
                for (int i = -5; i <= 5; i++) Prim(PrimitiveType.Cube, "Rib", r, new Vector3(i * 1.07f, 2.65f, 1.42f), new Vector3(0.08f, 3f, 0.05f), body, null, false, true);
                Prim(PrimitiveType.Cube, "Door", r, new Vector3(0f, 2.5f, 1.43f), new Vector3(2.4f, 2.6f, 0.06f), steel, null, false, true);
            }
        }

        // ================================================================== VILLAGE (forest village, church on a raised yard)

        static void BuildVillage()
        {
            var light = new LightPreset
            {
                name = "Village", sunIntensity = 1.6f, sunColor = new Color(1f, 0.9f, 0.76f), sunEuler = new Vector3(38f, 150f, 0f),
                ambSky = new Color(0.55f, 0.62f, 0.72f), ambEquator = new Color(0.45f, 0.5f, 0.44f), ambGround = new Color(0.22f, 0.24f, 0.18f),
                fogColor = new Color(0.66f, 0.72f, 0.74f), fogStart = 55f, fogEnd = 270f,
                skyTint = new Color(0.5f, 0.55f, 0.6f), atmosphere = 1.1f, skyExposure = 1.25f,
                exposure = 0.3f, contrast = 14f, saturation = 4f, bloom = 0.7f,
                probeCenter = new Vector3(0f, 5f, 0f), probeSize = new Vector3(110f, 40f, 110f)
            };
            (float, float, float, float)[] spawns =
            {
                (-30f, 0f, -46f, 30f), (44f, 0f, 44f, -135f), (-44f, 0f, 40f, 135f), (44f, 0f, -44f, -45f),
                (0f, 0f, -44f, 0f), (-46f, 0f, 0f, 90f), (46f, 0f, 2f, -90f), (0f, 1.6f, 31f, 180f),
                (-22f, 0f, -6f, 90f), (22f, 0f, 6f, -90f), (-14f, 0f, 25f, 160f), (18f, 0f, -16f, -20f),
            };
            BuildDM("Map_Village", "Деревня", light, VillageLayout, spawns);
        }

        static void VillageLayout(Transform env)
        {
            var grass = NoiseMat("Vil_Grass", new Color(0.24f, 0.33f, 0.14f), new Color(0.38f, 0.45f, 0.2f), 4f, 21, new Vector2(24f, 24f), 0.05f);
            var dirt = NoiseMat("Vil_Dirt", new Color(0.36f, 0.28f, 0.19f), new Color(0.5f, 0.41f, 0.29f), 5f, 22, new Vector2(16f, 2f), 0.08f);
            var logs = StripeMat("Vil_Logs", new Color(0.55f, 0.38f, 0.22f), new Color(0.28f, 0.18f, 0.1f), 10, new Vector2(3f, 1f), 0.15f);
            var boards = StripeMat("Vil_Boards", new Color(0.46f, 0.4f, 0.32f), new Color(0.26f, 0.23f, 0.19f), 1, new Vector2(1f, 1f), 0.1f);
            var roofM = Lit("Vil_Roof", new Color(0.32f, 0.3f, 0.28f), 0.3f, 0.3f);
            var roofG = Lit("Vil_RoofGreen", new Color(0.2f, 0.36f, 0.3f), 0.3f, 0.3f);
            var roofR = Lit("Vil_RoofRed", new Color(0.5f, 0.2f, 0.16f), 0.2f, 0.3f);
            var whiteM = Lit("Vil_Whitewash", new Color(0.9f, 0.89f, 0.85f), 0f, 0.1f);
            var stone = NoiseMat("Vil_Stone", new Color(0.42f, 0.41f, 0.39f), new Color(0.58f, 0.57f, 0.54f), 7f, 23, new Vector2(8f, 2f), 0.1f);
            var wood = Lit("Vil_Wood", new Color(0.42f, 0.3f, 0.19f), 0f, 0.25f);
            var woodDark = Lit("Vil_WoodDark", new Color(0.24f, 0.17f, 0.11f), 0f, 0.25f);
            var cut = Lit("Vil_LogCut", new Color(0.78f, 0.62f, 0.42f), 0f, 0.2f);
            var bark = Lit("Vil_Bark", new Color(0.26f, 0.2f, 0.15f), 0f, 0.15f);
            var birch = Lit("Vil_Birch", new Color(0.86f, 0.85f, 0.8f), 0f, 0.2f);
            var leaves = Lit("Vil_Leaves", new Color(0.22f, 0.38f, 0.14f), 0f, 0.1f);
            var leaves2 = Lit("Vil_Leaves2", new Color(0.32f, 0.44f, 0.16f), 0f, 0.1f);
            var pineM = Lit("Vil_Pine", new Color(0.1f, 0.22f, 0.12f), 0f, 0.1f);
            var hay = Lit("Vil_Hay", new Color(0.78f, 0.66f, 0.36f), 0f, 0.05f);
            var water = Lit("Vil_Water", new Color(0.08f, 0.14f, 0.16f), 0.2f, 0.95f);
            var gold = Lit("Vil_Gold", new Color(0.95f, 0.75f, 0.3f), 1f, 0.8f);
            var tractorM = Lit("Vil_Tractor", new Color(0.2f, 0.42f, 0.25f), 0.3f, 0.4f);
            var dark = Lit("Vil_Dark", new Color(0.08f, 0.08f, 0.08f), 0.4f, 0.3f);
            var glass = Lit("Vil_WindowGlass", new Color(0.1f, 0.14f, 0.17f), 0.2f, 0.9f);
            var blue = Lit("Vil_Blue", new Color(0.25f, 0.42f, 0.65f), 0f, 0.3f);
            var crateM = Lit("Vil_Crate", new Color(0.5f, 0.38f, 0.24f), 0f, 0.25f);
            var bags = Lit("Port_Sandbag", new Color(0.55f, 0.5f, 0.38f), 0f, 0.1f);
            var rnd = new Random(77);

            var root = new GameObject("Village").transform;
            root.SetParent(env, false);
            Box("Ground", root, new Vector3(0f, -0.5f, 0f), new Vector3(200f, 1f, 200f), grass);
            // play area boundary (invisible) + forest beyond a fence
            Bound(root, new Vector3(0f, 4f, 52f), new Vector3(106f, 8f, 1f));
            Bound(root, new Vector3(0f, 4f, -52f), new Vector3(106f, 8f, 1f));
            Bound(root, new Vector3(52f, 4f, 0f), new Vector3(1f, 8f, 106f));
            Bound(root, new Vector3(-52f, 4f, 0f), new Vector3(1f, 8f, 106f));
            Fence(root, new Vector3(-51f, 0, 51f), new Vector3(51f, 0, 51f), woodDark);
            Fence(root, new Vector3(-51f, 0, -51f), new Vector3(51f, 0, -51f), woodDark);
            Fence(root, new Vector3(51f, 0, -51f), new Vector3(51f, 0, 51f), woodDark);
            Fence(root, new Vector3(-51f, 0, -51f), new Vector3(-51f, 0, 51f), woodDark);
            for (int i = 0; i < 150; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float rad = 56f + (float)rnd.NextDouble() * 30f;
                var pos = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                if (Mathf.Abs(pos.x) < 54f && Mathf.Abs(pos.z) < 54f) continue;
                bool pine = rnd.NextDouble() < 0.6;
                Tree(root, pos, 9f + (float)rnd.NextDouble() * 7f, pine ? bark : (rnd.NextDouble() < 0.4 ? birch : bark), pine ? pineM : (rnd.NextDouble() < 0.5 ? leaves : leaves2), rnd, false, pine);
            }

            // ---- roads
            Prim(PrimitiveType.Cube, "Road", root, new Vector3(0f, 0.01f, 0f), new Vector3(104f, 0.02f, 7f), dirt, null, false, true);
            Prim(PrimitiveType.Cube, "Road", root, new Vector3(0f, 0.012f, 14f), new Vector3(5f, 0.02f, 21f), dirt, null, false, true);
            Prim(PrimitiveType.Cube, "Road", root, new Vector3(-26f, 0.012f, -22f), new Vector3(4f, 0.02f, 37f), dirt, null, false, true);
            for (int i = 0; i < 26; i++) Deco("Rut", root, PrimitiveType.Cube, new Vector3(-50f + i * 4f, 0.025f, (i % 2 == 0 ? 1.1f : -1.1f)), new Vector3(3.6f, 0.01f, 0.35f), Lit("Vil_Rut", new Color(0.28f, 0.22f, 0.15f), 0f, 0.4f));

            // ---- church on a raised yard (north)
            const float PY = 1.6f;
            Block("ChurchYard", root, 0f, 38.5f, new Vector3(26f, PY, 21f), stone);
            float sl = StairLength(PY);
            Stairs(root, new Vector3(0f, 0f, 28f - sl), Vector3.forward, 5f, PY, stone);
            Stairs(root, new Vector3(13f + sl, 0f, 36f), Vector3.left, 2.4f, PY, stone);
            Stairs(root, new Vector3(-13f - sl, 0f, 44f), Vector3.right, 2.4f, PY, stone);
            Room(root, -5.5f, 32f, 5.5f, 45f, PY, 6.5f, 0.5f, whiteM,
                 south: new[] { Door(5.5f, 2.4f, 3.2f) },
                 west: new[] { Window(3f, 1.2f, 2.2f, 4.4f), Window(7f, 1.2f, 2.2f, 4.4f), Window(10.5f, 1.2f, 2.2f, 4.4f), Door(12f, 1.4f, 2.4f) },
                 east: new[] { Window(3f, 1.2f, 2.2f, 4.4f), Window(7f, 1.2f, 2.2f, 4.4f), Window(10.5f, 1.2f, 2.2f, 4.4f) });
            Slab(root, -5.8f, 31.7f, 5.8f, 45.3f, PY + 6.5f + 0.25f, 0.25f, whiteM);
            GableRoof(root, -5.8f, 31.7f, 5.8f, 45.3f, PY + 6.75f, 3f, roofG, whiteM, false);
            Block("Tower", root, 0f, 46.9f, new Vector3(5f, 13f, 4f), whiteM, PY);
            Block("TowerTop", root, 0f, 46.9f, new Vector3(3.6f, 3.4f, 3.2f), whiteM, PY + 13f);
            for (int s = 0; s < 4; s++) Deco("BellOpening", root, PrimitiveType.Cube, new Vector3(0f, PY + 14.8f, 46.9f) + Quaternion.Euler(0f, s * 90f, 0f) * new Vector3(0f, 0f, 1.62f), new Vector3(1.3f, 1.8f, 0.05f), dark, new Vector3(0f, s * 90f, 0f));
            Deco("Dome", root, PrimitiveType.Sphere, new Vector3(0f, PY + 17.4f, 46.9f), new Vector3(3f, 3.6f, 3f), roofG);
            Deco("Spire", root, PrimitiveType.Cylinder, new Vector3(0f, PY + 20f, 46.9f), new Vector3(0.25f, 1.4f, 0.25f), gold);
            Deco("Cross", root, PrimitiveType.Cube, new Vector3(0f, PY + 21.2f, 46.9f), new Vector3(1.1f, 0.12f, 0.12f), gold);
            Deco("SmallDome", root, PrimitiveType.Sphere, new Vector3(0f, PY + 10.6f, 38.5f), new Vector3(2.2f, 2.6f, 2.2f), gold);
            for (int r = 0; r < 5; r++)
                foreach (float x in new[] { -2.6f, 2.6f })
                    Block("Pew", root, x, 35f + r * 1.6f, new Vector3(3.2f, 0.5f, 0.45f), woodDark, PY);
            Block("Altar", root, 0f, 43.6f, new Vector3(2.4f, 1.1f, 1f), gold, PY);
            Railing(root, new Vector3(-13f, 0, 28.1f), new Vector3(-2.6f, 0, 28.1f), PY, woodDark, 0.9f);
            Railing(root, new Vector3(2.6f, 0, 28.1f), new Vector3(13f, 0, 28.1f), PY, woodDark, 0.9f);
            // graveyard crosses and lime trees on the yard
            for (int i = 0; i < 6; i++)
            {
                var gp = new Vector3(-11f + (i % 3) * 1.6f, PY, 39f + (i / 3) * 2.2f);
                Deco("GraveCross", root, PrimitiveType.Cube, gp + Vector3.up * 0.55f, new Vector3(0.08f, 1.1f, 0.08f), woodDark);
                Deco("GraveCross", root, PrimitiveType.Cube, gp + Vector3.up * 0.8f, new Vector3(0.5f, 0.08f, 0.08f), woodDark);
            }
            Tree(root, new Vector3(10f, PY, 42f), 9f, bark, leaves, rnd, true, false);
            Tree(root, new Vector3(-10f, PY, 31f), 8f, birch, leaves2, rnd, true, false);

            // ---- log houses (izbas)
            Izba(root, -34f, -24f, -26f, -17f, logs, roofM, woodDark, whiteM, glass, blue, true);
            Izba(root, -35f, 10f, -27f, 17f, logs, roofR, woodDark, whiteM, glass, blue, true);
            Izba(root, -18f, -34f, -10f, -27f, logs, roofG, woodDark, whiteM, glass, blue, false);
            Izba(root, 12f, -34f, 20f, -27f, logs, roofM, woodDark, whiteM, glass, blue, false);
            Izba(root, 27f, -20f, 35f, -13f, logs, roofR, woodDark, whiteM, glass, blue, false);
            Izba(root, 27f, 12f, 35f, 19f, logs, roofM, woodDark, whiteM, glass, blue, false);
            Izba(root, -18f, 12f, -10f, 19f, logs, roofG, woodDark, whiteM, glass, blue, true);
            // yards around houses
            Fence(root, new Vector3(-38f, 0, -27f), new Vector3(-38f, 0, -12f), wood);
            Fence(root, new Vector3(-38f, 0, -12f), new Vector3(-31f, 0, -12f), wood);
            Fence(root, new Vector3(-28f, 0, -12f), new Vector3(-22f, 0, -12f), wood);
            Fence(root, new Vector3(-38f, 0, 7f), new Vector3(-22f, 0, 7f), wood);
            Fence(root, new Vector3(9f, 0, -37f), new Vector3(9f, 0, -24f), wood);
            Fence(root, new Vector3(9f, 0, -24f), new Vector3(14f, 0, -24f), wood);
            Fence(root, new Vector3(18f, 0, -24f), new Vector3(23f, 0, -24f), wood);
            Fence(root, new Vector3(38f, 0, 9f), new Vector3(38f, 0, 23f), wood);
            Fence(root, new Vector3(24f, 0, 23f), new Vector3(38f, 0, 23f), wood);
            Fence(root, new Vector3(-21f, 0, 22f), new Vector3(-7f, 0, 22f), wood);

            // ---- barns
            Barn(root, -44f, 26f, -32f, 36f, boards, roofM, woodDark, hay);
            Barn(root, 32f, 28f, 44f, 38f, boards, roofR, woodDark, hay);
            Barn(root, 30f, -44f, 42f, -34f, boards, roofM, woodDark, hay);

            // ---- field (south-east) with haystacks; pond (south-west)
            for (int i = 0; i < 7; i++) Haystack(root, new Vector3(4f + (i % 4) * 6.5f + (float)rnd.NextDouble() * 2f, 0f, -44f + (i / 4) * 7f + (float)rnd.NextDouble() * 2f), 1.4f + (float)rnd.NextDouble() * 0.4f, hay);
            for (int i = 0; i < 4; i++) HayBale(root, new Vector3(40f - i * 3.5f, 0f, -8f - (i % 2) * 2.5f), i * 35f, hay);
            Deco("Pond", root, PrimitiveType.Cylinder, new Vector3(-40f, 0.015f, -41f), new Vector3(14f, 0.01f, 9f), water);
            Deco("PondBank", root, PrimitiveType.Cylinder, new Vector3(-40f, 0.008f, -41f), new Vector3(15.5f, 0.01f, 10.5f), dirt);
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f;
                Deco("Reed", root, PrimitiveType.Cylinder, new Vector3(-40f + Mathf.Cos(a) * 7.3f, 0.5f, -41f + Mathf.Sin(a) * 4.8f), new Vector3(0.06f, 0.5f, 0.06f), leaves2);
            }
            Block("Jetty", root, -40f, -35.2f, new Vector3(1.6f, 0.15f, 4f), wood, 0.1f);

            // ---- trees and bushes inside (cover)
            Vector3[] trees = { new Vector3(-44f, 0, -8f), new Vector3(-45f, 0, 12f), new Vector3(-20f, 0, 34f), new Vector3(20f, 0, 34f), new Vector3(44f, 0, -2f), new Vector3(43f, 0, 10f),
                                new Vector3(-6f, 0, -22f), new Vector3(24f, 0, -2f), new Vector3(-24f, 0, 2f), new Vector3(6f, 0, 22f), new Vector3(-46f, 0, -30f), new Vector3(46f, 0, -24f) };
            for (int i = 0; i < trees.Length; i++)
            {
                bool pine = i % 3 == 0;
                Tree(root, trees[i], 8f + (float)rnd.NextDouble() * 5f, pine ? bark : (i % 2 == 0 ? birch : bark), pine ? pineM : leaves, rnd, true, pine);
            }
            for (int i = 0; i < 16; i++) Bush(root, new Vector3(-48f + (float)rnd.NextDouble() * 96f, 0f, -48f + (float)rnd.NextDouble() * 96f), 1.2f + (float)rnd.NextDouble(), i % 2 == 0 ? leaves : leaves2);

            // ---- props
            // well
            Prim(PrimitiveType.Cylinder, "Well", root, new Vector3(-6f, 0.5f, -8f), new Vector3(1.8f, 0.5f, 1.8f), stone, null, true, true);
            foreach (float x in new[] { -0.8f, 0.8f }) Deco("WellPost", root, PrimitiveType.Cube, new Vector3(-6f + x, 1.4f, -8f), new Vector3(0.14f, 1.8f, 0.14f), woodDark);
            GableRoof(root, -7.1f, -9f, -4.9f, -7f, 2.2f, 0.6f, roofM, woodDark, true, 0.2f);
            Pipe(root, new Vector3(-6.8f, 1.9f, -8f), new Vector3(-5.2f, 1.9f, -8f), 0.18f, wood, false, "Windlass");
            LogPile(root, new Vector3(-22f, 0f, -16f), 0f, bark, cut);
            LogPile(root, new Vector3(23f, 0f, 9f), 90f, bark, cut);
            LogPile(root, new Vector3(-8f, 0f, 25f), 90f, bark, cut);
            Cart(root, new Vector3(-12f, 0f, 4f), 75f, wood);
            Cart(root, new Vector3(16f, 0f, -6f), -100f, wood);
            Tractor(root, new Vector3(28f, 0f, 30f), -30f, tractorM, dark);
            Crate(root, new Vector3(4f, 0f, -6f), 1f, 12f, crateM); Crate(root, new Vector3(5f, 0f, -6.4f), 1f, -10f, crateM);
            Crate(root, new Vector3(-30f, 0f, 3.5f), 1.1f, 20f, crateM);
            Crate(root, new Vector3(30f, 0f, -6f), 1.1f, 0f, crateM); Crate(root, new Vector3(30f, 1.1f, -6f), 0.9f, 30f, crateM);
            Sandbags(root, new Vector3(0f, 0f, -14f), 0f, 3f, bags);
            Sandbags(root, new Vector3(-40f, 0f, 0f), 90f, 2.6f, bags);
            Sandbags(root, new Vector3(40f, 0f, 0f), 90f, 2.6f, bags);
            // power line along the road
            for (int i = 0; i < 6; i++)
            {
                var pp = new Vector3(-45f + i * 18f, 0f, -4.6f);
                PowerPole(root, pp, 0f, woodDark, dark);
                if (i > 0) Wires(root, new Vector3(-45f + (i - 1) * 18f, 0f, -4.6f), pp, 0f, dark);
            }
            Sign(root, new Vector3(-47f, 2.2f, 4.4f), 90f, "ЗАРЕЧЬЕ", Color.white, 0.5f, blue);
            Block("SignPost", root, -47f, 4.4f, new Vector3(0.12f, 1.8f, 0.12f), woodDark);
            Lamp(root, new Vector3(0f, 0f, 3.8f), 5f, new Color(1f, 0.9f, 0.7f), 1.2f, 12f, woodDark, Emissive("Vil_Bulb", new Color(1f, 0.85f, 0.6f), 3f));
        }

        /// <summary>Log house: walls with door / windows, ceiling, gable roof, porch, stove, furniture, window frames.</summary>
        static void Izba(Transform p, float minX, float minZ, float maxX, float maxZ, Material logs, Material roof, Material wood, Material white, Material glass, Material trim, bool doorEast)
        {
            float h = 3f, t = 0.4f;
            float w = maxX - minX, d = maxZ - minZ;
            var south = new[] { Window(w * 0.3f, 1.1f, 1f, 2f), Window(w * 0.72f, 1.1f, 1f, 2f) };
            var north = new[] { Window(w * 0.5f, 1.1f, 1f, 2f), Door(w * 0.15f, 1.1f, 2.1f) };
            var door = new[] { Door(d * 0.5f, 1.3f, 2.2f) };
            var win = new[] { Window(d * 0.5f, 1f, 1f, 2f) };
            Room(p, minX, minZ, maxX, maxZ, 0f, h, t, logs, south, north, doorEast ? win : door, doorEast ? door : win);
            Slab(p, minX - 0.2f, minZ - 0.2f, maxX + 0.2f, maxZ + 0.2f, h + 0.2f, 0.2f, wood);
            GableRoof(p, minX - 0.2f, minZ - 0.2f, maxX + 0.2f, maxZ + 0.2f, h + 0.2f, 2f, roof, logs, true);
            // chimney
            Block("Chimney", p, minX + w * 0.7f, minZ + d * 0.6f, new Vector3(0.6f, 2.4f, 0.6f), white, h + 0.6f);
            // carved window frames (trim) on the long walls
            foreach (var (along, z, outward) in new[] { (w * 0.3f, minZ, -1f), (w * 0.72f, minZ, -1f), (w * 0.5f, maxZ, 1f) })
            {
                float x = minX + along;
                Vector3 c = new Vector3(x, 1.5f, z + outward * (t * 0.5f + 0.03f));
                Prim(PrimitiveType.Cube, "Frame", p, c + Vector3.up * 0.58f, new Vector3(1.4f, 0.14f, 0.06f), trim, null, false, true);
                Prim(PrimitiveType.Cube, "Frame", p, c - Vector3.up * 0.55f, new Vector3(1.3f, 0.1f, 0.12f), trim, null, false, true);
                Prim(PrimitiveType.Cube, "Shutter", p, c + new Vector3(-0.78f, 0f, 0f), new Vector3(0.45f, 1.05f, 0.05f), trim, null, false, true);
                Prim(PrimitiveType.Cube, "Shutter", p, c + new Vector3(0.78f, 0f, 0f), new Vector3(0.45f, 1.05f, 0.05f), trim, null, false, true);
            }
            // porch at the door
            float px = doorEast ? maxX + 0.9f : minX - 0.9f;
            float pz = (minZ + maxZ) * 0.5f;
            Block("Porch", p, px, pz, new Vector3(1.6f, 0.25f, 2.2f), wood);
            foreach (float z in new[] { -0.95f, 0.95f }) Block("PorchPost", p, px + (doorEast ? 0.65f : -0.65f), pz + z, new Vector3(0.12f, 2.4f, 0.12f), wood, 0.25f);
            Prim(PrimitiveType.Cube, "PorchRoof", p, new Vector3(px, 2.75f, pz), new Vector3(1.9f, 0.08f, 2.5f), wood, Quaternion.Euler(0f, 0f, doorEast ? -14f : 14f), false, true);
            // interior: stove, table, bench, bed
            Block("Stove", p, doorEast ? minX + 1.1f : maxX - 1.1f, maxZ - 1.1f, new Vector3(1.6f, 1.6f, 1.6f), white).AddComponent<SurfaceTag>().kind = SurfaceKind.Concrete;
            Block("Table", p, minX + w * 0.5f, minZ + 1.6f, new Vector3(1.6f, 0.78f, 0.9f), wood);
            Block("Bench", p, minX + w * 0.5f, minZ + 0.75f, new Vector3(2.2f, 0.45f, 0.4f), wood);
            Block("Bed", p, doorEast ? minX + 2.9f : maxX - 2.9f, maxZ - 0.75f, new Vector3(2f, 0.55f, 0.9f), wood);
        }

        /// <summary>Plank barn, open at both gable ends, hay inside and a hay loft you can see but not reach.</summary>
        static void Barn(Transform p, float minX, float minZ, float maxX, float maxZ, Material boards, Material roof, Material wood, Material hay)
        {
            float h = 4.6f;
            float w = maxX - minX, d = maxZ - minZ;
            Room(p, minX, minZ, maxX, maxZ, 0f, h, 0.25f, boards,
                 new[] { Door(w * 0.5f, 3.6f, 3.6f) }, new[] { Door(w * 0.5f, 3.6f, 3.6f) },
                 new[] { Window(d * 0.3f, 1f, 2.4f, 3.2f), Door(d * 0.7f, 1.4f, 2.4f) }, new[] { Window(d * 0.6f, 1f, 2.4f, 3.2f) });
            GableRoof(p, minX - 0.15f, minZ - 0.15f, maxX + 0.15f, maxZ + 0.15f, h, 2.6f, roof, boards, false, 0.6f);
            for (float z = minZ + 2f; z < maxZ - 1f; z += 3f) Block("Beam", p, (minX + maxX) * 0.5f, z, new Vector3(w - 0.3f, 0.2f, 0.2f), wood, h - 0.2f);
            HayBale(p, new Vector3(minX + 1.6f, 0f, minZ + 2.2f), 0f, hay);
            HayBale(p, new Vector3(minX + 1.6f, 1.2f, minZ + 2.6f), 10f, hay);
            HayBale(p, new Vector3(maxX - 1.8f, 0f, maxZ - 2.5f), 80f, hay);
            HayBale(p, new Vector3(maxX - 1.8f, 0f, minZ + 3.4f), 85f, hay);
            Block("Trough", p, maxX - 0.8f, (minZ + maxZ) * 0.5f, new Vector3(0.7f, 0.6f, 2.6f), wood);
        }

        static void Tractor(Transform p, Vector3 pos, float yaw, Material body, Material dark)
        {
            var r = new GameObject("Tractor").transform;
            r.SetParent(p, false);
            r.position = pos; r.rotation = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "Hood", r, new Vector3(0f, 1.05f, 0.9f), new Vector3(1f, 0.9f, 1.8f), body, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            Prim(PrimitiveType.Cube, "Cab", r, new Vector3(0f, 1.7f, -0.6f), new Vector3(1.4f, 1.6f, 1.4f), body, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            Prim(PrimitiveType.Cube, "CabGlass", r, new Vector3(0f, 2f, 0.12f), new Vector3(1.2f, 0.8f, 0.04f), dark, null, false, true);
            foreach (float x in new[] { -0.85f, 0.85f })
            {
                Prim(PrimitiveType.Cylinder, "RearWheel", r, new Vector3(x, 0.8f, -0.7f), new Vector3(1.6f, 0.2f, 1.6f), dark, Quaternion.Euler(0f, 0f, 90f), true, true);
                Prim(PrimitiveType.Cylinder, "FrontWheel", r, new Vector3(x * 0.85f, 0.45f, 1.4f), new Vector3(0.9f, 0.13f, 0.9f), dark, Quaternion.Euler(0f, 0f, 90f), false, true);
            }
            Prim(PrimitiveType.Cylinder, "Exhaust", r, new Vector3(0.35f, 2f, 1.4f), new Vector3(0.1f, 0.5f, 0.1f), dark, null, false, true);
        }

        // ================================================================== detail passes for the original maps

        static void PortDetails(Transform root)
        {
            var dark = Lit("Port_DarkMetal", new Color(0.14f, 0.15f, 0.16f), 0.7f, 0.4f);
            var yellow = Lit("Port_Paint", new Color(0.95f, 0.75f, 0.15f), 0f, 0.3f);
            var white = Lit("Port_White", new Color(0.88f, 0.88f, 0.86f), 0f, 0.3f);
            var orange = Lit("Port_Orange", new Color(1f, 0.42f, 0.1f), 0f, 0.35f);
            var rubber = Lit("Port_Rubber", new Color(0.06f, 0.06f, 0.06f), 0f, 0.35f);
            var wood = Lit("Port_Wood", new Color(0.55f, 0.4f, 0.25f), 0f, 0.25f);
            var water = Lit("Port_Water", new Color(0.05f, 0.12f, 0.16f), 0.3f, 0.92f);
            var puddle = Lit("Port_Puddle", new Color(0.06f, 0.07f, 0.08f), 0.1f, 0.95f);
            var hull = Lit("Port_Hull", new Color(0.18f, 0.22f, 0.28f), 0.5f, 0.4f);
            var hullRed = Lit("Port_HullRed", new Color(0.5f, 0.12f, 0.1f), 0.4f, 0.4f);
            var plate = Lit("Port_SignPlate", new Color(0.1f, 0.25f, 0.45f), 0f, 0.3f);
            var concrete = Lit("Port_Concrete", new Color(0.58f, 0.57f, 0.55f), 0f, 0.12f);

            // sea beyond the north wall with a moored ship
            Deco("Sea", root, PrimitiveType.Cube, new Vector3(0f, -0.6f, 95f), new Vector3(300f, 0.1f, 80f), water);
            Deco("QuayEdge", root, PrimitiveType.Cube, new Vector3(0f, -0.05f, 54.9f), new Vector3(110f, 0.12f, 0.3f), Lit("Port_Paint", new Color(0.95f, 0.75f, 0.15f), 0f, 0.3f));
            var ship = new GameObject("Ship").transform; ship.SetParent(root, false); ship.position = new Vector3(-6f, 0f, 66f);
            Prim(PrimitiveType.Cube, "Hull", ship, new Vector3(0f, 2f, 0f), new Vector3(80f, 8f, 14f), hull, null, false, true);
            Prim(PrimitiveType.Cube, "HullBottom", ship, new Vector3(0f, -1.6f, 0f), new Vector3(80.2f, 1.6f, 14.2f), hullRed, null, false, true);
            Prim(PrimitiveType.Cube, "Bridge", ship, new Vector3(30f, 10f, 0f), new Vector3(9f, 8f, 12f), white, null, false, true);
            for (int i = 0; i < 4; i++) Prim(PrimitiveType.Cube, "BridgeWindows", ship, new Vector3(25.45f, 8f + i * 1.8f, 0f), new Vector3(0.1f, 0.6f, 10f), dark, null, false, true);
            for (int i = 0; i < 5; i++) Prim(PrimitiveType.Cube, "DeckContainers", ship, new Vector3(-28f + i * 11f, 7.3f, 0f), new Vector3(10f, 2.6f, 12f), i % 2 == 0 ? hullRed : yellow, null, false, true);
            Prim(PrimitiveType.Cylinder, "Funnel", ship, new Vector3(33f, 16f, 0f), new Vector3(3f, 3f, 3f), hullRed, null, false, true);
            // bollards along the north wall (inside) and life buoys
            for (float x = -30f; x <= 30f; x += 7.5f)
            {
                Deco("Bollard", root, PrimitiveType.Cylinder, new Vector3(x, 0.35f, 32.6f), new Vector3(0.45f, 0.35f, 0.45f), dark, default, true);
                Deco("BollardCap", root, PrimitiveType.Cylinder, new Vector3(x, 0.72f, 32.6f), new Vector3(0.6f, 0.05f, 0.6f), dark);
            }
            for (float x = -24f; x <= 24f; x += 16f)
            {
                Deco("LifeBuoy", root, PrimitiveType.Cylinder, new Vector3(x, 1.7f, 33.68f), new Vector3(0.75f, 0.06f, 0.75f), orange, new Vector3(90f, 0f, 0f));
                Deco("LifeBuoyHole", root, PrimitiveType.Cylinder, new Vector3(x, 1.7f, 33.66f), new Vector3(0.38f, 0.07f, 0.38f), concrete, new Vector3(90f, 0f, 0f));
            }
            // container numbers + zone signs
            Sign(root, new Vector3(0f, 4.2f, -6.18f), 180f, "ТЕРМИНАЛ 3", Color.white, 0.6f, plate);
            Sign(root, new Vector3(-33.7f, 2.6f, 0f), 90f, "СЕКТОР А", Color.white, 0.5f, plate);
            Sign(root, new Vector3(33.7f, 2.6f, -14f), -90f, "СЕКТОР Б", Color.white, 0.5f, plate);
            Sign(root, new Vector3(0f, 2.5f, -33.7f), 0f, "КПП ЗАКРЫТ", new Color(1f, 0.85f, 0.2f), 0.35f, dark);
            // guard booth in the south-west corner
            Room(root, -33.4f, -25f, -30.4f, -21f, 0f, 2.6f, 0.15f, white, north: new[] { Door(1.5f, 1f, 2.2f) }, east: new[] { Window(2f, 2.4f, 1f, 2.1f) });
            Slab(root, -33.6f, -25.2f, -30.2f, -20.8f, 2.8f, 0.2f, dark);
            Deco("BoothStripe", root, PrimitiveType.Cube, new Vector3(-31.9f, 2.55f, -20.9f), new Vector3(3.2f, 0.18f, 0.05f), yellow);
            // clutter
            CableReel(root, new Vector3(12f, 0f, -20f), 30f, wood, rubber);
            CableReel(root, new Vector3(-9f, 0f, 22f), -20f, wood, rubber);
            TireStack(root, new Vector3(22f, 0f, -10f), 4, rubber); TireStack(root, new Vector3(22.8f, 0f, -10.5f), 2, rubber);
            TireStack(root, new Vector3(-22f, 0f, -30f), 3, rubber);
            foreach (var c in new[] { new Vector3(-2f, 0f, -12.2f), new Vector3(2f, 0f, -12.2f), new Vector3(-11f, 0f, -3f), new Vector3(11f, 0f, 12.5f), new Vector3(20f, 0f, -26f) }) Cone(root, c, orange, white);
            for (int i = 0; i < 7; i++) Puddle(root, new Vector3(-24f + i * 8f, 0f, -18f + (i % 3) * 14f), 2f + i % 3, i * 50f, puddle);
            // pallets with sacks
            foreach (var pp in new[] { new Vector3(-12f, 0f, 8f), new Vector3(6f, 0f, -24f) })
            {
                Prim(PrimitiveType.Cube, "Pallet", root, pp + Vector3.up * 0.07f, new Vector3(1.2f, 0.14f, 1f), wood, null, true, true);
                for (int i = 0; i < 4; i++) Deco("Sack", root, PrimitiveType.Capsule, pp + new Vector3((i % 2 - 0.5f) * 0.55f, 0.32f + (i / 2) * 0.26f, 0f), new Vector3(0.5f, 0.45f, 0.4f), Lit("Port_Sack", new Color(0.7f, 0.64f, 0.5f), 0f, 0.1f), new Vector3(0f, 0f, 90f));
            }
            // crane details: hook, cables, ladder
            Pipe(root, new Vector3(2f, 13.8f, 25f), new Vector3(2f, 6f, 25f), 0.05f, dark, false, "CraneCable");
            Deco("CraneHook", root, PrimitiveType.Cube, new Vector3(2f, 5.8f, 25f), new Vector3(0.5f, 0.6f, 0.3f), yellow);
            Deco("CraneLadder", root, PrimitiveType.Cube, new Vector3(-7.5f, 7.5f, 21f), new Vector3(0.05f, 15f, 0.5f), dark);
        }

        static void TownDetails(Transform root)
        {
            var terracotta = Lit("Town_Terracotta", new Color(0.62f, 0.32f, 0.2f), 0f, 0.2f);
            var wood = Lit("Town_Wood", new Color(0.36f, 0.23f, 0.13f), 0f, 0.3f);
            var stone = Lit("Town_Stone", new Color(0.62f, 0.55f, 0.46f), 0f, 0.1f);
            var clothR = Lit("Town_ClothRed", new Color(0.62f, 0.18f, 0.14f), 0f, 0.1f);
            var clothC = Lit("Town_ClothCream", new Color(0.9f, 0.85f, 0.72f), 0f, 0.1f);
            var blue = Lit("Town_Blue", new Color(0.18f, 0.38f, 0.58f), 0f, 0.3f);
            var green = Lit("Town_Green", new Color(0.25f, 0.45f, 0.3f), 0f, 0.3f);
            var metal = Lit("Town_Metal", new Color(0.35f, 0.36f, 0.38f), 0.7f, 0.4f);
            var tank = Lit("Town_Tank", new Color(0.82f, 0.82f, 0.8f), 0.3f, 0.4f);
            var car = Lit("Town_Car", new Color(0.55f, 0.18f, 0.12f), 0.3f, 0.35f);
            var rust = Lit("Town_Rust", new Color(0.4f, 0.22f, 0.12f), 0.3f, 0.2f);
            var rubber = Lit("Town_Rubber", new Color(0.07f, 0.07f, 0.07f), 0f, 0.35f);
            var glass = Lit("Town_Glass", new Color(0.12f, 0.14f, 0.16f), 0.2f, 0.9f);
            var rubble = Lit("Town_Rubble", new Color(0.7f, 0.62f, 0.5f), 0f, 0.1f);
            var leaves = Lit("Town_Palm", new Color(0.24f, 0.36f, 0.16f), 0f, 0.2f);

            // rooftop water tanks, antennas and dishes
            foreach (var (x, y, z) in new[] { (-14f, 7.1f, -30f), (13f, 7.1f, 30f), (-13f, 3.85f, 31f), (13f, 3.85f, -26f), (-37f, 3.85f, -34f), (37f, 3.85f, 34f) })
            {
                Deco("RoofTank", root, PrimitiveType.Cylinder, new Vector3(x, y + 0.9f, z), new Vector3(1.2f, 0.6f, 1.2f), tank, default, true);
                foreach (float dx in new[] { -0.4f, 0.4f }) Deco("TankLeg", root, PrimitiveType.Cube, new Vector3(x + dx, y + 0.15f, z), new Vector3(0.06f, 0.3f, 0.9f), metal);
                Deco("Antenna", root, PrimitiveType.Cylinder, new Vector3(x + 1.6f, y + 1.5f, z + 1f), new Vector3(0.04f, 1.5f, 0.04f), metal);
                Deco("AntennaBar", root, PrimitiveType.Cube, new Vector3(x + 1.6f, y + 2.6f, z + 1f), new Vector3(0.9f, 0.03f, 0.03f), metal);
                Deco("Dish", root, PrimitiveType.Cylinder, new Vector3(x - 1.4f, y + 0.6f, z - 1.2f), new Vector3(0.8f, 0.04f, 0.8f), tank, new Vector3(60f, 30f, 0f));
            }
            // laundry lines across the side alleys
            foreach (float z in new[] { -22f, 22f })
            {
                Vector3 a = new Vector3(-20.2f, 5.6f, z), b = new Vector3(-31f, 5.6f, z);
                foreach (var e in new[] { a, b, new Vector3(20.2f, 5.6f, -z), new Vector3(31f, 5.6f, -z) })
                    Deco("LinePole", root, PrimitiveType.Cylinder, new Vector3(e.x + (e.x > 0 ? 0.15f : -0.15f), 2.85f, e.z), new Vector3(0.1f, 2.85f, 0.1f), wood, default, true);
                Beam(root, a, b, 0.015f, 0.015f, metal, false, "Line");
                for (int i = 1; i < 7; i++) Deco("Laundry", root, PrimitiveType.Cube, Vector3.Lerp(a, b, i / 7f) + Vector3.down * 0.35f, new Vector3(0.7f, 0.6f, 0.02f), i % 3 == 0 ? clothR : i % 3 == 1 ? clothC : blue);
                Vector3 c = new Vector3(20.2f, 5.6f, -z), d = new Vector3(31f, 5.6f, -z);
                Beam(root, c, d, 0.015f, 0.015f, metal, false, "Line");
                for (int i = 1; i < 7; i++) Deco("Laundry", root, PrimitiveType.Cube, Vector3.Lerp(c, d, i / 7f) + Vector3.down * 0.35f, new Vector3(0.7f, 0.6f, 0.02f), i % 2 == 0 ? clothC : green);
            }
            // clay pots / amphoras by the doors and the plaza
            Vector3[] pots = { new Vector3(-6.4f, 0, -23f), new Vector3(-6.4f, 0, -21.6f), new Vector3(6.4f, 0, 23f), new Vector3(-14.4f, 0, -7f), new Vector3(14.4f, 0, 8f), new Vector3(-2.5f, 0, 2.8f), new Vector3(2.8f, 0, -2.6f), new Vector3(-30.5f, 0, -15f) };
            foreach (var pp in pots)
            {
                Deco("Pot", root, PrimitiveType.Sphere, pp + Vector3.up * 0.35f, new Vector3(0.55f, 0.7f, 0.55f), terracotta);
                Deco("PotNeck", root, PrimitiveType.Cylinder, pp + Vector3.up * 0.78f, new Vector3(0.24f, 0.08f, 0.24f), terracotta);
            }
            // wrecked car on the main street (cover)
            var carRoot = new GameObject("WreckedCar").transform; carRoot.SetParent(root, false);
            carRoot.position = new Vector3(2.2f, 0f, -10f); carRoot.rotation = Quaternion.Euler(0f, 18f, 4f);
            Prim(PrimitiveType.Cube, "Body", carRoot, new Vector3(0f, 0.65f, 0f), new Vector3(1.8f, 0.7f, 4.3f), car, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            Prim(PrimitiveType.Cube, "Cabin", carRoot, new Vector3(0f, 1.25f, -0.2f), new Vector3(1.6f, 0.55f, 2.2f), car, null, true, true).AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;
            Prim(PrimitiveType.Cube, "Windows", carRoot, new Vector3(0f, 1.27f, -0.2f), new Vector3(1.62f, 0.4f, 2f), glass, null, false, true);
            foreach (float x in new[] { -0.85f, 0.85f })
                foreach (float z in new[] { -1.4f, 1.4f })
                    Prim(PrimitiveType.Cylinder, "Wheel", carRoot, new Vector3(x, 0.32f, z), new Vector3(0.62f, 0.1f, 0.62f), x < 0 && z > 0 ? rust : rubber, Quaternion.Euler(0f, 0f, 90f), false, true);
            // rubble piles at the archways
            var rr = new Random(5);
            foreach (var c in new[] { new Vector3(-4.6f, 0, -27f), new Vector3(4.8f, 0, 29f), new Vector3(-24f, 0, -2f) })
                for (int i = 0; i < 6; i++)
                    Deco("Rubble", root, PrimitiveType.Cube, c + new Vector3((float)(rr.NextDouble() - 0.5) * 1.6f, 0.12f, (float)(rr.NextDouble() - 0.5) * 1.6f),
                         Vector3.one * (0.2f + (float)rr.NextDouble() * 0.3f), rubble, new Vector3((float)rr.NextDouble() * 40f, (float)rr.NextDouble() * 360f, 0f));
            // curb stones along the main street and wall lanterns
            for (float z = -40f; z <= 40f; z += 2f)
            {
                if (Mathf.Abs(Mathf.Abs(z) - 28f) < 1.5f) continue;
                Deco("Curb", root, PrimitiveType.Cube, new Vector3(-6.6f, 0.07f, z), new Vector3(0.3f, 0.14f, 1.9f), stone);
                Deco("Curb", root, PrimitiveType.Cube, new Vector3(6.6f, 0.07f, z), new Vector3(0.3f, 0.14f, 1.9f), stone);
            }
            var lantern = Emissive("Town_Lantern", new Color(1f, 0.8f, 0.45f), 2.5f);
            foreach (var (x, z) in new[] { (-6.75f, -30f), (-6.75f, 24f), (6.75f, -24f), (6.75f, 32f), (-14.8f, -8f), (14.8f, 8f) })
            {
                float s = Mathf.Sign(x);
                Deco("LanternArm", root, PrimitiveType.Cube, new Vector3(x - s * 0.25f, 3.2f, z), new Vector3(0.5f, 0.05f, 0.05f), metal);
                Deco("Lantern", root, PrimitiveType.Cube, new Vector3(x - s * 0.45f, 2.95f, z), new Vector3(0.22f, 0.32f, 0.22f), lantern);
            }
            // shop signs and stall rugs
            Sign(root, new Vector3(-6.76f, 3.6f, -19f), 90f, "ЧАЙХАНА", new Color(1f, 0.85f, 0.4f), 0.35f, blue);
            Sign(root, new Vector3(6.76f, 3.6f, 20f), -90f, "БАЗАР", new Color(1f, 0.9f, 0.6f), 0.35f, clothR);
            Deco("Rug", root, PrimitiveType.Cube, new Vector3(-9f, 0.01f, -8.6f), new Vector3(2.4f, 0.01f, 1.4f), clothR);
            Deco("Rug", root, PrimitiveType.Cube, new Vector3(9f, 0.01f, 8.6f), new Vector3(2.4f, 0.01f, 1.4f), blue);
            for (int i = 0; i < 6; i++) Bush(root, new Vector3(-40f + i * 16f, 0f, (i % 2 == 0 ? -43.5f : 43.5f)), 1.2f, leaves);
        }

        static void WarehouseDetails(Transform root)
        {
            var steel = Lit("WH_Steel", new Color(0.2f, 0.21f, 0.23f), 0.8f, 0.45f);
            var paint = Lit("WH_Paint", new Color(0.95f, 0.78f, 0.15f), 0f, 0.3f);
            var black = Lit("WH_Black", new Color(0.05f, 0.05f, 0.05f), 0f, 0.3f);
            var red = Lit("WH_Red", new Color(0.75f, 0.1f, 0.08f), 0f, 0.4f);
            var grey = Lit("WH_Grey", new Color(0.55f, 0.56f, 0.57f), 0.5f, 0.35f);
            var duct = Lit("WH_Duct", new Color(0.7f, 0.72f, 0.74f), 0.8f, 0.5f);
            var pipeG = Lit("WH_PipeGreen", new Color(0.2f, 0.45f, 0.3f), 0.4f, 0.4f);
            var plate = Lit("WH_SignPlate", new Color(0.12f, 0.14f, 0.18f), 0f, 0.3f);
            var green = Emissive("WH_Exit", new Color(0.2f, 1f, 0.4f), 2.5f);
            var puddle = Lit("WH_Puddle", new Color(0.08f, 0.08f, 0.09f), 0.1f, 0.95f);

            // outer perimeter: the side doors used to open onto endless ground
            var fenceM = Lit("Port_Concrete", new Color(0.58f, 0.57f, 0.55f), 0f, 0.12f);
            Room(root, -46f, -50f, 46f, 34f, 0f, 3.5f, 0.5f, fenceM);
            for (float x = -42f; x <= 42f; x += 12f) Lamp(root, new Vector3(x, 0f, 33.4f), 6f, new Color(1f, 0.85f, 0.65f), 1.2f, 12f, steel, Emissive("WH_Bulb", new Color(1f, 0.93f, 0.8f), 4f));
            Sign(root, new Vector3(45.7f, 2.2f, 0f), -90f, "ТЕРРИТОРИЯ СКЛАДА", Color.white, 0.4f, plate);

            // yellow band along the walls + ventilation ducts under the roof
            foreach (var (a, b) in new[] { (new Vector3(-34.7f, 0, -23.7f), new Vector3(34.7f, 0, -23.7f)), (new Vector3(-34.7f, 0, 23.7f), new Vector3(34.7f, 0, 23.7f)) })
                Prim(PrimitiveType.Cube, "WallBand", root, (a + b) * 0.5f + Vector3.up * 1.1f, new Vector3(69.4f, 0.25f, 0.04f), paint, null, false, true);
            Pipe(root, new Vector3(-33f, 8.8f, -10f), new Vector3(33f, 8.8f, -10f), 0.9f, duct, false, "Duct");
            Pipe(root, new Vector3(-33f, 8.8f, 8f), new Vector3(33f, 8.8f, 8f), 0.9f, duct, false, "Duct");
            for (float x = -30f; x <= 30f; x += 10f)
            {
                Deco("DuctHanger", root, PrimitiveType.Cube, new Vector3(x, 9.4f, -10f), new Vector3(0.05f, 1.2f, 0.05f), steel);
                Deco("DuctHanger", root, PrimitiveType.Cube, new Vector3(x, 9.4f, 8f), new Vector3(0.05f, 1.2f, 0.05f), steel);
                Deco("Vent", root, PrimitiveType.Cube, new Vector3(x + 5f, 8.3f, -10f), new Vector3(0.6f, 0.1f, 0.6f), grey);
            }
            // overhead crane rail along the hall
            Beam(root, new Vector3(-34f, 7.6f, -2f), new Vector3(34f, 7.6f, -2f), 0.4f, 0.5f, paint, false, "CraneRail");
            Deco("CraneTrolley", root, PrimitiveType.Cube, new Vector3(-8f, 7.2f, -2f), new Vector3(1.4f, 0.6f, 1f), paint);
            Pipe(root, new Vector3(-8f, 6.9f, -2f), new Vector3(-8f, 3.2f, -2f), 0.04f, black, false, "Chain");
            Deco("CraneHook", root, PrimitiveType.Cube, new Vector3(-8f, 3f, -2f), new Vector3(0.4f, 0.5f, 0.25f), black);
            // wall pipes
            Pipe(root, new Vector3(-34.6f, 3f, -22f), new Vector3(-34.6f, 3f, 14f), 0.3f, pipeG);
            Pipe(root, new Vector3(34.6f, 3.4f, -22f), new Vector3(34.6f, 3.4f, 14f), 0.3f, red);
            Pipe(root, new Vector3(34.6f, 0.2f, 14f), new Vector3(34.6f, 3.4f, 14f), 0.3f, red);
            // hazard stripes at the loading doors
            foreach (float x in new[] { -17f, 17f })
                for (int i = 0; i < 6; i++)
                    Deco("Hazard", root, PrimitiveType.Cube, new Vector3(x - 2.5f + i, 0.007f, -22.6f), new Vector3(0.5f, 0.01f, 1.6f), i % 2 == 0 ? paint : black, new Vector3(0f, 30f, 0f));
            // zone signs and exits
            Sign(root, new Vector3(-17f, 6.4f, -23.7f), 0f, "ДОК 1", Color.white, 0.6f, plate);
            Sign(root, new Vector3(17f, 6.4f, -23.7f), 0f, "ДОК 2", Color.white, 0.6f, plate);
            Sign(root, new Vector3(-34.7f, 5.5f, -9f), 90f, "ЗОНА А", new Color(1f, 0.85f, 0.2f), 0.6f, plate);
            Sign(root, new Vector3(34.7f, 5.5f, -9f), -90f, "ЗОНА Б", new Color(1f, 0.85f, 0.2f), 0.6f, plate);
            foreach (var (pos, yaw) in new[] { (new Vector3(0f, 2.9f, -23.68f), 0f), (new Vector3(-34.68f, 2.9f, -14f), 90f), (new Vector3(34.68f, 2.9f, 14f), -90f) })
                Prim(PrimitiveType.Cube, "ExitSign", root, pos, new Vector3(0.6f, 0.22f, 0.05f), green, Quaternion.Euler(0f, yaw, 0f), false, true);
            // fire points, electrical boxes, puddles
            Extinguisher(root, new Vector3(-10f, 0f, -23.72f), 0f, red, black);
            Extinguisher(root, new Vector3(10f, 0f, -23.72f), 0f, red, black);
            Extinguisher(root, new Vector3(-34.72f, 0f, 6f), 90f, red, black);
            ElectricBox(root, new Vector3(34.72f, 0f, -2f), -90f, grey, paint);
            ElectricBox(root, new Vector3(-34.72f, 0f, -18f), 90f, grey, paint);
            for (int i = 0; i < 5; i++) Puddle(root, new Vector3(-24f + i * 12f, 0f, -20f + (i % 2) * 22f), 1.6f + i % 2, i * 33f, puddle);
            // roll-up shutters above the loading doors
            foreach (float x in new[] { -17f, 17f })
            {
                Deco("ShutterBox", root, PrimitiveType.Cube, new Vector3(x, 5.1f, -24.05f), new Vector3(5.4f, 0.5f, 0.5f), grey);
                Deco("ShutterRail", root, PrimitiveType.Cube, new Vector3(x - 2.6f, 2.4f, -24.05f), new Vector3(0.12f, 4.8f, 0.2f), grey);
                Deco("ShutterRail", root, PrimitiveType.Cube, new Vector3(x + 2.6f, 2.4f, -24.05f), new Vector3(0.12f, 4.8f, 0.2f), grey);
            }
            // trash bins and a pallet jack
            foreach (var pp in new[] { new Vector3(-33f, 0f, 22.6f), new Vector3(33f, 0f, -22.6f) })
                Deco("Bin", root, PrimitiveType.Cube, pp + Vector3.up * 0.55f, new Vector3(1.2f, 1.1f, 0.9f), pipeG, default, true);
            Deco("PalletJack", root, PrimitiveType.Cube, new Vector3(-4f, 0.06f, -14f), new Vector3(0.6f, 0.08f, 1.2f), red, new Vector3(0f, 25f, 0f));
            Deco("JackHandle", root, PrimitiveType.Cube, new Vector3(-4.3f, 0.6f, -14.6f), new Vector3(0.05f, 1.1f, 0.05f), black, new Vector3(-20f, 25f, 0f));
        }
    }
}
