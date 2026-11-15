using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// "Фабрика": a small night-shift factory dressed with the HQ Warehouse Kit props (materials converted to URP, box
    /// colliders added — the kit ships none). Layout:
    ///  - production hall (44 x 24 m, 9 m high): two conveyor lines with crossings, vat tanks, generators and a pipe rig;
    ///    a catwalk along the north wall (stairs at both ends) with a glazed control booth over the floor;
    ///  - storage room (west): three shelving rows with a cross aisle, pallets by the doors;
    ///  - locker room and office (east), through a partition door;
    ///  - fenced yard (south), reached through three open garage doors: water towers, gas tanks, stacked containers and
    ///    drums, lit by stadium lights.
    /// </summary>
    public static partial class RangeBuilder
    {
        const string HQRoot = "Assets/HQ PBR/HQ Warehouse Kit";
        const float FacMZ = 3.6f; // catwalk level

        [MenuItem("Tools/FPS/Maps/Factory")] static void MFactory() => BuildFactory();

        static void BuildFactory()
        {
            if (!AssetDatabase.IsValidFolder(HQRoot)) { Debug.LogWarning("[FPS] HQ Warehouse Kit not found — skipping Map_Factory"); return; }
            FactoryMaterials();
            var light = new LightPreset
            {
                // night shift: a neutral moonlit ambient (a blue one tinted the whole map), the lamps do the work
                name = "Factory", sunIntensity = 0.55f, sunColor = new Color(0.78f, 0.84f, 1f), sunEuler = new Vector3(40f, -40f, 0f),
                ambSky = new Color(0.36f, 0.38f, 0.44f), ambEquator = new Color(0.32f, 0.32f, 0.33f), ambGround = new Color(0.18f, 0.18f, 0.18f),
                fogColor = new Color(0.15f, 0.16f, 0.2f), fogStart = 30f, fogEnd = 160f,
                skyTint = new Color(0.25f, 0.28f, 0.38f), atmosphere = 0.5f, skyExposure = 0.3f,
                exposure = 0.9f, contrast = 14f, saturation = -12f, bloom = 1.2f,
                probeCenter = new Vector3(-2f, 4f, -7f), probeSize = new Vector3(80f, 24f, 50f)
            };
            (float, float, float, float)[] spawns =
            {
                (-33f, 0f, -25f, 45f), (29f, 0f, -25f, -45f), (-14f, 0f, -25.5f, 0f), (18f, 0f, -25.5f, 0f),
                (-34f, 0f, 6.5f, 135f), (-28.2f, 0f, -2.5f, 90f), (28f, 0f, -10.5f, 0f), (23.5f, 0f, 2.5f, 135f),
                (-20.5f, 0f, -10.5f, 45f), (20.5f, 0f, -10.5f, -45f), (0f, 0f, -10.5f, 0f), (-15f, FacMZ, 10.5f, 180f),
                (12.5f, FacMZ, 9.4f, 180f), (-17f, 0f, 1.5f, 90f), (17f, 0f, 1.5f, -90f), (0f, 0f, 9.5f, 180f),
            };
            BuildDM("Map_Factory", "Фабрика", light, FactoryLayout, spawns);
            Debug.Log("[FPS] Map_Factory built");
        }

        /// <summary>The kit's Built-in Standard materials → URP/Lit (once; the light-cone FX material is left alone and hidden).</summary>
        static void FactoryMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            int n = 0;
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { HQRoot }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g));
                if (!m || m.name.StartsWith("fx_") || m.name.StartsWith("skybox")) continue;
                if (ImportedMaterialFixer.Convert(m, lit)) { EditorUtility.SetDirty(m); n++; }
            }
            if (n > 0) { AssetDatabase.SaveAssets(); Debug.Log($"[FPS] HQ Warehouse Kit: {n} materials converted to URP/Lit"); }
        }

        static void FactoryLayout(Transform env)
        {
            var floorM = Lit("FAC_Floor", Color.white, 0f, 0.3f);
            var ftex = FactoryConcrete();
            if (ftex) { floorM.SetTexture("_BaseMap", ftex); floorM.SetTextureScale("_BaseMap", new Vector2(30f, 37f)); }
            EditorUtility.SetDirty(floorM);
            var wallM = Lit("FAC_Wall", new Color(0.5f, 0.55f, 0.54f), 0f, 0.25f);
            var steel = Lit("FAC_Steel", new Color(0.24f, 0.25f, 0.27f), 0.3f, 0.3f); // matte: a shiny roof mirrored the blue night sky
            var grate = Lit("FAC_Grate", new Color(0.33f, 0.34f, 0.36f), 0.7f, 0.35f);
            var trim = Lit("FAC_Trim", new Color(0.88f, 0.66f, 0.1f), 0.2f, 0.4f);
            var office = Lit("FAC_Office", new Color(0.76f, 0.76f, 0.72f), 0f, 0.2f);
            var fence = Lit("FAC_Fence", new Color(0.48f, 0.48f, 0.46f), 0f, 0.12f);
            var contBlue = Lit("FAC_ContainerBlue", new Color(0.18f, 0.42f, 0.58f), 0.4f, 0.35f);
            var contRed = Lit("FAC_ContainerRed", new Color(0.66f, 0.22f, 0.13f), 0.4f, 0.35f);
            var dark = Lit("FAC_Dark", new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.3f);
            var crateM = Lit("FAC_Crate", new Color(0.62f, 0.47f, 0.3f), 0f, 0.2f);
            var paint = Lit("FAC_Paint", new Color(0.95f, 0.78f, 0.15f), 0f, 0.3f);
            var bulb = Emissive("FAC_Bulb", new Color(1f, 0.82f, 0.55f), 5f);
            Color warm = new Color(1f, 0.8f, 0.55f), cold = new Color(0.8f, 0.9f, 1f);

            var root = new GameObject("Factory").transform;
            root.SetParent(env, false);
            Box("Ground", root, new Vector3(-1f, -0.5f, -7f), new Vector3(120f, 1f, 90f), floorM);
            var area = Rect.MinMaxRect(-36.6f, -27.6f, 32.6f, 12.6f);
            PackBounds(root, area, -1f, 30f);
            // the ground runs on past the walls for the view: no NavMesh out there (bots, pickup spawns)
            void NoNav(float x0, float z0, float x1, float z1)
            {
                var go = new GameObject("NoNav");
                go.transform.SetParent(root, false);
                go.transform.position = new Vector3((x0 + x1) * 0.5f, 5f, (z0 + z1) * 0.5f);
                var v = go.AddComponent<Unity.AI.Navigation.NavMeshModifierVolume>();
                v.center = Vector3.zero;
                v.size = new Vector3(x1 - x0, 14f, z1 - z0);
                v.area = 1; // Not Walkable
            }
            NoNav(-61f, -52f, area.xMin, 38f);
            NoNav(area.xMax, -52f, 59f, 38f);
            NoNav(area.xMin, -52f, area.xMax, area.yMin);
            NoNav(area.xMin, area.yMax, area.xMax, 38f);

            // ================================================================ production hall
            const float H = 9f;
            Room(root, -22f, -12f, 22f, 12f, 0f, H, 0.4f, wallM,
                 south: new[] { Door(10f, 3.2f, 3.5f), Door(22f, 3.2f, 3.5f), Door(34f, 3.2f, 3.5f) },
                 north: new[] { Window(8f, 5f, 5.6f, 7.6f), Window(22f, 5f, 5.6f, 7.6f), Window(36f, 5f, 5.6f, 7.6f) },
                 west: new[] { Door(6f, 1.8f, 2.6f), Door(16f, 1.8f, 2.6f) },
                 east: new[] { Door(5f, 1.6f, 2.5f), Door(13f, 1.6f, 2.5f) });
            foreach (var (z0, z1) in new[] { (-12.2f, -6.5f), (-4.5f, 1.5f), (3.5f, 12.2f) })
                Slab(root, -22.2f, z0, 22.2f, z1, H + 0.3f, 0.3f, steel);
            for (int i = -2; i <= 2; i++) Block("RoofTruss", root, i * 8f, 0f, new Vector3(0.35f, 0.7f, 24f), steel, H - 0.7f);
            foreach (float x in new[] { -12f, 0f, 12f }) Prop("garage_door_a_01_open_set", root, new Vector3(x, 0f, -12f), 0f, SurfaceKind.Metal, false);

            // painted lanes
            foreach (float z in new[] { -3.5f, -9.4f })
                Prim(PrimitiveType.Cube, "Lane", root, new Vector3(0f, 0.005f, z), new Vector3(43f, 0.01f, 0.12f), paint, null, false, true);

            // conveyor lines (waist-high cover) with crossings in the middle and at the ends
            FactoryLine(root, -17.5f, -0.5f, 4);
            FactoryLine(root, 5.6f, -0.5f, 4);
            FactoryLine(root, -14f, -6.5f, 3);
            FactoryLine(root, 5.1f, -6.5f, 3);
            Prop("electric_generator_a_01", root, new Vector3(0f, 0f, -0.5f), 90f);
            Prop("electric_generator_a_01", root, new Vector3(-6f, 0f, 5f), 0f);
            Prop("electric_generator_a_01", root, new Vector3(6f, 0f, 5f), 180f);

            // vat tanks and pipe rigs on both sides
            foreach (float s in new[] { -1f, 1f })
            {
                Prop("steel_vat_tank_a_01", root, new Vector3(s * 9f, 0f, 4.4f), 0f);
                Prop("steel_vat_tank_a_01", root, new Vector3(s * 11.3f, 0f, 4.4f), 30f);
                Prop("metal_pipe_demo", root, new Vector3(s * 14f, 0f, 4.7f), s > 0 ? 180f : 0f);
            }

            // by the garage doors (the openings stay clear)
            Prop("pallet_wrapped_a_01", root, new Vector3(-17f, 0f, -10.2f), 10f, SurfaceKind.Wood);
            Prop("pallet_wrapped_b_01", root, new Vector3(-15.5f, 0f, -10.4f), -5f, SurfaceKind.Wood);
            Prop("shipping_crate_a_01", root, new Vector3(-6f, 0f, -10.2f), 0f, SurfaceKind.Wood);
            Prop("shipping_crate_b_01", root, new Vector3(-6f, 1f, -10.2f), 15f, SurfaceKind.Wood);
            Prop("wood_pallet_stack_b_01", root, new Vector3(5.5f, 0f, -10f), 0f, SurfaceKind.Wood);
            Prop("pallet_wrapped_b_01", root, new Vector3(16.5f, 0f, -10.2f), 0f, SurfaceKind.Wood);
            Prop("shipping_crate_c_01", root, new Vector3(18f, 0f, -10.4f), 30f, SurfaceKind.Wood);
            Crate(root, new Vector3(-8.5f, 0f, -3.6f), 1.1f, 20f, crateM);
            Crate(root, new Vector3(9f, 0f, -3.4f), 1.2f, -10f, crateM);

            // ---- catwalk along the north wall, stairs at both ends
            Slab(root, -21.8f, 7.5f, 21.8f, 11.8f, FacMZ, 0.3f, grate);
            foreach (float x in new[] { -16f, -8f, 0f, 8f, 16f }) Block("Column", root, x, 7.7f, new Vector3(0.35f, FacMZ - 0.3f, 0.35f), steel);
            float sl = StairLength(FacMZ);
            float[] stairX = { -19.8f, 19.8f };
            const float stairW = 1.8f;
            float cursor = -21.8f;
            foreach (float sx in stairX)
            {
                if (sx - stairW * 0.5f - cursor > 0.3f) Railing(root, new Vector3(cursor, 0f, 7.55f), new Vector3(sx - stairW * 0.5f, 0f, 7.55f), FacMZ, trim);
                cursor = sx + stairW * 0.5f;
            }
            if (21.8f - cursor > 0.3f) Railing(root, new Vector3(cursor, 0f, 7.55f), new Vector3(21.8f, 0f, 7.55f), FacMZ, trim);
            foreach (float sx in stairX) Stairs(root, new Vector3(sx, 0f, 7.5f - sl), Vector3.forward, stairW - 0.2f, FacMZ, steel);
            // glazed control booth over the floor (doors on both sides)
            Room(root, -4f, 8.4f, 4f, 11.7f, FacMZ, 2.5f, 0.15f, office,
                 south: new[] { Window(2.2f, 3f, 1f, 2.2f), Window(5.8f, 3f, 1f, 2.2f) },
                 west: new[] { Door(1.7f, 1f, 2.2f) }, east: new[] { Door(1.7f, 1f, 2.2f) });
            Slab(root, -4.1f, 8.3f, 4.1f, 11.8f, FacMZ + 2.65f, 0.15f, steel);
            Prop("table_aluminum_a_01", root, new Vector3(-1.5f, FacMZ, 9.4f), 90f);
            Prop("table_aluminum_a_01", root, new Vector3(1.5f, FacMZ, 9.4f), 90f);
            Prop("chair_aluminum_a_01", root, new Vector3(-1.4f, FacMZ, 10.3f), 180f, SurfaceKind.Metal, false);
            FactoryShelf(root, new Vector3(0f, FacMZ, 11.2f), 0f, 1);
            Lamp(root, new Vector3(0f, FacMZ, 10f), 2.4f, cold, 1.6f, 7f, null, bulb);
            // cover on the catwalk
            Prop("shipping_crate_a_01", root, new Vector3(-13f, FacMZ, 9.9f), 10f, SurfaceKind.Wood);
            Prop("shipping_crate_b_01", root, new Vector3(-13f, FacMZ + 1f, 9.9f), 25f, SurfaceKind.Wood);
            Prop("pallet_wrapped_a_01", root, new Vector3(9.5f, FacMZ, 10.3f), 0f, SurfaceKind.Wood);
            Prop("electric_generator_a_01", root, new Vector3(16f, FacMZ, 10.4f), 90f);
            Prop("plastic_drum_a_01", root, new Vector3(-8f, FacMZ, 11.1f), 0f);
            // under the catwalk: tool shelves, lockers, drums
            foreach (float x in new[] { -13f, -11f, -9f }) FactoryShelf(root, new Vector3(x, 0f, 11.25f), 0f, (int)(x * 7f));
            for (int i = 0; i < 4; i++) Prop("locker_hiding_a_01", root, new Vector3(3.6f + i * 0.6f, 0f, 11.35f), 180f);
            Prop("plastic_drum_pile_01", root, new Vector3(12f, 0f, 10.2f), 90f);
            Prop("compressed_air_tank_a_01", root, new Vector3(-5f, 0f, 11.4f), 0f);
            Prop("compressed_air_tank_a_01", root, new Vector3(-4.5f, 0f, 11.4f), 0f);

            // hall lights
            foreach (float x in new[] { -15f, -5f, 5f, 15f })
                foreach (float z in new[] { -7f, 2f })
                    Lamp(root, new Vector3(x, 0f, z), 8.4f, warm, 6.5f, 22f, null, bulb);
            Lamp(root, new Vector3(-12f, 0f, 9.6f), 3.2f, warm, 1.5f, 8f, null, bulb);
            Lamp(root, new Vector3(12f, 0f, 9.6f), 3.2f, warm, 1.5f, 8f, null, bulb);

            // ================================================================ storage room (west)
            const float SH = 5f;
            Wall(root, new Vector3(-36.15f, 0f, -12f), new Vector3(-22.2f, 0f, -12f), 0f, SH, 0.3f, wallM, Door(7f, 1.8f, 2.6f));
            Wall(root, new Vector3(-36.15f, 0f, 8f), new Vector3(-22.2f, 0f, 8f), 0f, SH, 0.3f, wallM);
            Wall(root, new Vector3(-36f, 0f, -11.85f), new Vector3(-36f, 0f, 7.85f), 0f, SH, 0.3f, wallM,
                 Window(6f, 3f, 2.9f, 4.3f), Window(14f, 3f, 2.9f, 4.3f));
            Slab(root, -36.15f, -12.15f, -22.2f, 8.15f, SH + 0.3f, 0.3f, steel);
            foreach (float x in new[] { -33.5f, -30f, -26.5f })
                foreach (float z in new[] { -9.5f, -7.5f, -5.5f, 0.5f, 2.5f, 4.5f })
                    FactoryShelf(root, new Vector3(x, 0f, z), 90f, (int)(x * 13f + z * 5f));
            Prop("wood_pallet_stack_a_01", root, new Vector3(-24.2f, 0f, -9.6f), 0f, SurfaceKind.Wood);
            Prop("pallet_wrapped_a_01", root, new Vector3(-24.1f, 0f, 6.6f), 90f, SurfaceKind.Wood);
            Prop("cargo_tarp_a_01", root, new Vector3(-34.4f, 0f, -2.5f), 90f, SurfaceKind.Wood);
            Prop("step_ladder_a_01", root, new Vector3(-28.2f, 0f, 6.9f), 0f, SurfaceKind.Metal, false);
            Lamp(root, new Vector3(-29f, 0f, -6f), 4.7f, cold, 2.2f, 11f, null, bulb);
            Lamp(root, new Vector3(-29f, 0f, 3f), 4.7f, cold, 2.2f, 11f, null, bulb);

            // ================================================================ locker room and office (east)
            const float LH = 3.4f;
            Wall(root, new Vector3(22.2f, 0f, -12f), new Vector3(32.15f, 0f, -12f), 0f, LH, 0.3f, wallM, Door(5f, 1.6f, 2.5f));
            Wall(root, new Vector3(22.2f, 0f, 4f), new Vector3(32.15f, 0f, 4f), 0f, LH, 0.3f, wallM, Window(5f, 2.4f, 2.6f, 3.2f));
            Wall(root, new Vector3(32f, 0f, -11.85f), new Vector3(32f, 0f, 3.85f), 0f, LH, 0.3f, wallM);
            Wall(root, new Vector3(22.2f, 0f, -4f), new Vector3(31.85f, 0f, -4f), 0f, LH, 0.15f, office, Door(3f, 1.2f, 2.3f));
            Slab(root, 22.2f, -12.15f, 32.15f, 4.15f, LH + 0.3f, 0.3f, steel);
            for (int i = 0; i < 9; i++) Prop("locker_hiding_a_01", root, new Vector3(31.4f, 0f, -9.4f + i * 0.6f), -90f);
            for (int i = 0; i < 6; i++) Prop("locker_hiding_a_01", root, new Vector3(26.4f + i * 0.6f, 0f, -4.55f), 180f);
            Block("Bench", root, 26.5f, -8.5f, new Vector3(3f, 0.45f, 0.4f), steel);
            Prop("table_aluminum_a_01", root, new Vector3(28.6f, 0f, -0.2f), 0f);
            Prop("table_aluminum_a_01", root, new Vector3(28.6f, 0f, 1.8f), 0f);
            Prop("chair_aluminum_a_01", root, new Vector3(27.8f, 0f, 0.4f), 90f, SurfaceKind.Metal, false);
            Prop("chair_aluminum_a_01", root, new Vector3(29.4f, 0f, 1.4f), -90f, SurfaceKind.Metal, false);
            FactoryShelf(root, new Vector3(31.4f, 0f, 1f), 90f, 77);
            Prop("plastic_bin_a_01", root, new Vector3(23f, 0f, -3.2f), 0f);
            Lamp(root, new Vector3(27f, 0f, -8f), 3.1f, cold, 1.8f, 9f, null, bulb);
            Lamp(root, new Vector3(27f, 0f, 0f), 3.1f, cold, 1.8f, 9f, null, bulb);

            // ================================================================ yard (south)
            Wall(root, new Vector3(-36.15f, 0f, -27f), new Vector3(32.15f, 0f, -27f), 0f, 3f, 0.3f, fence);
            Wall(root, new Vector3(-36f, 0f, -26.85f), new Vector3(-36f, 0f, -12.15f), 0f, 3f, 0.3f, fence);
            Wall(root, new Vector3(32f, 0f, -26.85f), new Vector3(32f, 0f, -12.15f), 0f, 3f, 0.3f, fence);
            Prop("water_reservoir_a_01_set", root, new Vector3(-31.5f, 0f, -22.5f), 0f);
            Prop("water_reservoir_a_01_set", root, new Vector3(27.5f, 0f, -22.5f), 180f);
            Prop("propane_gas_tank_a_01", root, new Vector3(-21f, 0f, -20f), 0f);
            Prop("propane_gas_tank_a_01", root, new Vector3(-21f, 0f, -21.4f), 0f);
            Prop("propane_gas_tank_a_01", root, new Vector3(17f, 0f, -21f), 90f);
            Container(root, 4f, -20.5f, 90f, contBlue, dark);
            Container(root, -8f, -23.6f, 0f, contRed, dark, 2);
            Prop("plastic_drum_pile_01", root, new Vector3(12f, 0f, -24.6f), 0f);
            Prop("plastic_drum_pile_01", root, new Vector3(-27f, 0f, -15.4f), 90f);
            Prop("wood_pallet_stack_c_01", root, new Vector3(-15f, 0f, -15.2f), 15f, SurfaceKind.Wood);
            Prop("wood_pallet_stack_a_01", root, new Vector3(9f, 0f, -15.6f), -10f, SurfaceKind.Wood);
            Prop("cargo_tarp_b_01", root, new Vector3(23.5f, 0f, -15.6f), 0f, SurfaceKind.Wood);
            Prop("trashbag_a_01", root, new Vector3(-26.4f, 0f, -12.9f), 20f, SurfaceKind.Wood, false);
            Prop("trashbag_a_01", root, new Vector3(25.2f, 0f, -12.8f), -30f, SurfaceKind.Wood, false);
            Crate(root, new Vector3(-2f, 0f, -16f), 1.1f, 15f, crateM);
            Crate(root, new Vector3(-0.8f, 0f, -16.4f), 0.9f, -20f, crateM);
            Crate(root, new Vector3(20.5f, 0f, -24.8f), 1.2f, 5f, crateM);
            foreach (var (x, z, yaw) in new[] { (-34.6f, -25.6f, 45f), (30.6f, -25.6f, -45f), (0f, -26.2f, 0f) })
            {
                var stadium = Prop("stadium_light_a_01", root, new Vector3(x, 0f, z), yaw, SurfaceKind.Metal, false);
                if (stadium) foreach (var l in stadium.GetComponentsInChildren<Light>(true)) { l.intensity = 6f; l.range = 40f; l.color = new Color(1f, 0.93f, 0.82f); }
            }
            foreach (float x in new[] { -29f, -12f, 0f, 12f, 27.2f })
                Lamp(root, new Vector3(x, 0f, -12.45f), 3.9f, warm, 4f, 14f, null, bulb);
        }

        /// <summary>
        /// The kit's concrete floor texture is an atlas with a blue painted band and a yellow line at its edges: the plain
        /// concrete between them is cut out (a GPU blit — the source is not CPU-readable) and kept as a tiling texture.
        /// </summary>
        static Texture2D FactoryConcrete()
        {
            string path = Root + "/Textures/FAC_Concrete.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) return existing;
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(HQRoot + "/Materials/concrete_floor_a_01/concrete_floor_a_01_diff.tif");
            if (!src) return null;
            const int W = 1024, Hpx = 624; // rows 156..780 of the 1024² source: seam to the start of the blue band
            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(W, Hpx, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt, new Vector2(1f, 0.61f), new Vector2(0f, 0.238f));
            RenderTexture.active = rt;
            var tex = new Texture2D(W, Hpx, TextureFormat.RGBA32, true, false)
            { name = "FAC_Concrete", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            tex.ReadPixels(new Rect(0, 0, W, Hpx), 0, 0);
            tex.Apply(true);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return SaveAsset(tex, path);
        }

        static readonly string[] FactoryBoxes = { "cardboard_box_a_01", "cardboard_box_b_01", "cardboard_box_c_01", "cardboard_box_simple_a_01",
                                                   "cardboard_box_simple_b_01", "cardboard_box_simple_c_01", "parcel_box_a_01" };

        /// <summary>
        /// Metal shelving filled with boxes: the shelf's box collider makes it solid cover, so it also has to look solid
        /// (an empty open rack would show targets that cannot be hit). A row of boxes on every board, a few gaps.
        /// </summary>
        static void FactoryShelf(Transform root, Vector3 at, float yaw, int seed)
        {
            var shelf = Prop("shelving_metal_a_01", root, at, yaw);
            if (!shelf) return;
            var rnd = new System.Random(seed);
            foreach (float y in new[] { 0.27f, 0.79f, 1.29f, 1.81f })
                for (int i = 0; i < 3; i++)
                {
                    if (rnd.NextDouble() < 0.14) continue;
                    Vector3 local = new Vector3(-0.62f + i * 0.62f + (float)(rnd.NextDouble() * 0.06 - 0.03), y, (float)(rnd.NextDouble() * 0.08 - 0.04));
                    Prop(FactoryBoxes[rnd.Next(FactoryBoxes.Length)], root, shelf.transform.TransformPoint(local),
                         yaw + (float)(rnd.NextDouble() * 16 - 8), SurfaceKind.Wood, false);
                }
        }

        /// <summary>Conveyor line along +X from <paramref name="x0"/>, centred on <paramref name="z"/>.</summary>
        static void FactoryLine(Transform root, float x0, float z, int segments)
        {
            for (int i = 0; i < segments; i++)
                Prop(i % 2 == 0 ? "conveyor_belt_a_01" : "conveyor_belt_b_01", root, new Vector3(x0 + 1.485f + i * 2.97f, 0f, z), 90f);
        }

        /// <summary>
        /// An HQ kit prop placed by its footprint: the bounds centre lands on (at.x, at.z) and the bottom on at.y, whatever
        /// the model's pivot. A box collider wraps its meshes (the kit ships none) unless <paramref name="solid"/> is off;
        /// the kit's fake light cones are hidden.
        /// </summary>
        static GameObject Prop(string name, Transform parent, Vector3 at, float yaw, SurfaceKind surface = SurfaceKind.Metal, bool solid = true)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HQRoot + "/Prefabs/" + name + ".prefab");
            if (!prefab) { Debug.LogWarning("[FPS] HQ prop not found: " + name); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            var rs = go.GetComponentsInChildren<Renderer>(true);
            Bounds wb = default;
            bool any = false;
            foreach (var r in rs)
            {
                if (r.sharedMaterial && r.sharedMaterial.name.StartsWith("fx_")) { r.enabled = false; continue; }
                if (!any) { wb = r.bounds; any = true; }
                else wb.Encapsulate(r.bounds);
            }
            if (!any) return go;
            go.transform.position += new Vector3(at.x - wb.center.x, at.y - wb.min.y, at.z - wb.center.z);
            if (solid)
            {
                // a box in the prop's own space around its meshes
                var toLocal = go.transform.worldToLocalMatrix;
                Bounds lb = default;
                bool init = false;
                foreach (var r in rs)
                {
                    var mf = r.enabled ? r.GetComponent<MeshFilter>() : null;
                    if (!mf || !mf.sharedMesh) continue;
                    var mb = mf.sharedMesh.bounds;
                    var m = toLocal * r.transform.localToWorldMatrix;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                        var p = m.MultiplyPoint3x4(c);
                        if (!init) { lb = new Bounds(p, Vector3.zero); init = true; }
                        else lb.Encapsulate(p);
                    }
                }
                if (init) { var bc = go.AddComponent<BoxCollider>(); bc.center = lb.center; bc.size = lb.size; }
            }
            go.AddComponent<SurfaceTag>().kind = surface;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            return go;
        }
    }
}
