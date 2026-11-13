using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

namespace FPS.EditorTools
{
    /// <summary>
    /// Map_Disco — an open-air night club (original layout). Three dance rooms under the night sky, each with its own
    /// track and light show: the main hall (stage + LED wall, VIP balcony with four cabins, bar under it), the Red hall
    /// (west) and the Blue laser hall (east). South, under a roof: the lobby with the cloakroom, the VIP gallery
    /// (gold, cage, aquarium and karaoke cabins) and the backstage (storage, toilets, staff room).
    /// Dance floors are packed with dancing mannequins (DiscoClub/DiscoDancer); nicknames are hidden; the screaming
    /// head in the sky (DiscoHead) fires telegraphed rockets every 30 s. Tools > FPS > Maps > Disco.
    /// </summary>
    public static partial class RangeBuilder
    {
        [MenuItem("Tools/FPS/Maps/Disco")] static void M7() => BuildDisco();

        const string DiscoAudio = "Assets/_Game/Audio/Disco/";

        static Random discoRnd;
        static Material dGlass, dChrome, dDark, dNeon;

        static void BuildDisco()
        {
            var light = new LightPreset
            {
                name = "Disco", sunIntensity = 0.5f, sunColor = new Color(0.55f, 0.62f, 1f), sunEuler = new Vector3(52f, -35f, 0f),
                ambSky = new Color(0.34f, 0.27f, 0.48f), ambEquator = new Color(0.27f, 0.21f, 0.34f), ambGround = new Color(0.13f, 0.1f, 0.15f),
                fogColor = new Color(0.035f, 0.02f, 0.06f), fogStart = 30f, fogEnd = 170f,
                skyTint = new Color(0.25f, 0.18f, 0.4f), atmosphere = 0.35f, skyExposure = 0.05f,
                exposure = 1.05f, contrast = 14f, saturation = 16f, bloom = 1.4f,
                probeCenter = new Vector3(0f, 3f, 4f), probeSize = new Vector3(84f, 20f, 60f)
            };
            (float, float, float, float)[] spawns =
            {
                (-37f, 0f, 0f, 45f), (37f, 0f, 25f, -135f), (37f, 0f, 0f, -45f), (-37f, 0f, 25f, 135f),
                (-11.5f, 0f, 12f, 90f), (11.5f, 0f, 12f, -90f), (0f, 0f, 18.4f, 180f), (12.6f, 3f, -1f, -90f),
                (-17f, 0f, 25f, -135f), (17f, 0f, 25f, 135f),
                (0f, 0f, -20f, 0f), (-11.5f, 0f, -22.5f, 45f), (10f, 0f, -15f, -45f),
                (-30f, 0f, -15f, 90f), (-23f, 0f, -22f, 0f), (20f, 0f, -15f, -90f), (32f, 0f, -18f, 180f),
            };
            BuildDM("Map_Disco", "Дискотека", light, DiscoLayout, spawns);
        }

        static Material Glass(string name, Color c)
        {
            var m = Lit(name, c, 0.1f, 0.95f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static AudioClip DiscoTrack(string file)
        {
            string path = DiscoAudio + file;
            if (AssetImporter.GetAtPath(path) is AudioImporter imp)
            {
                var s = imp.defaultSampleSettings;
                if (s.loadType != AudioClipLoadType.Streaming || s.compressionFormat != AudioCompressionFormat.Vorbis)
                {
                    s.loadType = AudioClipLoadType.Streaming;
                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = 0.6f;
                    imp.defaultSampleSettings = s;
                    imp.loadInBackground = true;
                    imp.SaveAndReimport();
                }
            }
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (!clip) Debug.LogWarning("[Disco] track not found: " + path);
            return clip;
        }

        static void DiscoLayout(Transform env)
        {
            discoRnd = new Random(1313);
            var floorM = NoiseMat("Disco_Floor", new Color(0.05f, 0.05f, 0.06f), new Color(0.11f, 0.1f, 0.12f), 5f, 3, new Vector2(20f, 14f), 0.75f);
            var wallM = NoiseMat("Disco_Wall", new Color(0.07f, 0.06f, 0.08f), new Color(0.13f, 0.11f, 0.14f), 3f, 4, new Vector2(10f, 2f), 0.2f);
            var ceilM = Lit("Disco_Ceiling", new Color(0.05f, 0.045f, 0.06f), 0.1f, 0.2f);
            dDark = Lit("Disco_DarkMetal", new Color(0.06f, 0.06f, 0.07f), 0.8f, 0.45f);
            dChrome = Lit("Disco_Chrome", new Color(0.85f, 0.86f, 0.9f), 1f, 0.95f);
            dGlass = Glass("Disco_Glass", new Color(0.55f, 0.65f, 0.8f, 0.22f));
            dNeon = Lit("Disco_Neon", new Color(1f, 0.2f, 0.6f), 0f, 0.5f, true);

            var root = new GameObject("Disco").transform;
            root.SetParent(env, false);
            Box("Ground", root, new Vector3(0f, -0.5f, 0f), new Vector3(82f, 1f, 58f), floorM);
            Room(root, -40f, -28f, 40f, 28f, 0f, 7f, 0.6f, wallM);

            // ---- inner walls (z = -6 splits the open-air dance rooms from the covered south band)
            Wall(root, new Vector3(-40f, 0f, -6f), new Vector3(40f, 0f, -6f), 0f, 6f, 0.4f, wallM,
                 Door(9f, 3f, 2.8f), Door(20f, 2.4f), Door(34f, 3f, 2.8f), Door(52.6f, 1.8f), Door(60f, 3f, 2.8f), Door(74f, 2.4f));
            Wall(root, new Vector3(-14f, 0f, -28f), new Vector3(-14f, 0f, 28f), 0f, 6f, 0.4f, wallM,
                 Door(4f, 1.8f), Door(14f, 2.4f), Door(32f, 3f, 2.8f), Door(50f, 2.2f));
            Wall(root, new Vector3(14f, 0f, -28f), new Vector3(14f, 0f, 28f), 0f, 6f, 0.4f, wallM,
                 Door(6f, 1.8f), Door(16f, 2.4f), Door(34f, 3f, 2.8f), Door(50f, 2.2f));
            Slab(root, -40f, -28f, 40f, -6.2f, 4.6f, 0.3f, ceilM);

            var tileM = Lit("Disco_Tile", new Color(0.08f, 0.08f, 0.1f), 0.2f, 0.9f, true);
            var club = root.gameObject.AddComponent<DiscoClub>();
            var spots = new List<Transform>();

            var main = MainHall(root, tileM, spots);
            SideHall(root, tileM, spots, false);
            SideHall(root, tileM, spots, true);
            Balcony(root, main, spots);
            Lobby(root);
            VipGallery(root);
            Backstage(root);

            club.idleClips = CharacterAnimatorBuilder.DanceClips();
            club.dancerSpots = spots.ToArray();
            club.maxDancers = 80;
            club.areaMin = new Vector2(-39f, -27f);
            club.areaMax = new Vector2(39f, 27f);

            var headGO = new GameObject("SkyHead");
            headGO.transform.SetParent(root, false);
            headGO.transform.position = new Vector3(0f, 34f, 12f);
            var head = headGO.AddComponent<DiscoHead>();
            head.voiceRoom = main;
        }

        // ================================================================== dance rooms

        static DiscoRoom NewRoom(Transform p, string name, string title, AudioClip track, Vector3 center, float radius, params Color[] palette)
        {
            var go = new GameObject(name);
            go.transform.SetParent(p, false);
            var r = go.AddComponent<DiscoRoom>();
            r.title = title; r.track = track; r.center = center; r.radius = radius; r.palette = palette;
            return r;
        }

        /// <summary>LED tiles 2×2 m (cols × rows from minX/minZ), neon frame around, dancer spots on top.</summary>
        static void DanceFloor(DiscoRoom room, Transform p, float minX, float minZ, int cols, int rows, Material tileM, List<Transform> spots, Vector3 lookAt, List<Renderer> neon)
        {
            var tiles = new List<Renderer>();
            var floor = new GameObject("DanceFloor").transform;
            floor.SetParent(p, false);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var t = Prim(PrimitiveType.Cube, "Tile", floor, new Vector3(minX + 1f + c * 2f, 0.05f, minZ + 1f + r * 2f), new Vector3(1.94f, 0.1f, 1.94f), tileM, null, true, false);
                    tiles.Add(t.GetComponent<Renderer>());
                }
            room.tiles = tiles.ToArray();
            room.tileCols = cols;
            float maxX = minX + cols * 2f, maxZ = minZ + rows * 2f;
            neon.Add(Prim(PrimitiveType.Cube, "FloorNeon", floor, new Vector3((minX + maxX) * 0.5f, 0.06f, minZ - 0.08f), new Vector3(maxX - minX + 0.3f, 0.12f, 0.12f), dNeon, null, false, false).GetComponent<Renderer>());
            neon.Add(Prim(PrimitiveType.Cube, "FloorNeon", floor, new Vector3((minX + maxX) * 0.5f, 0.06f, maxZ + 0.08f), new Vector3(maxX - minX + 0.3f, 0.12f, 0.12f), dNeon, null, false, false).GetComponent<Renderer>());
            neon.Add(Prim(PrimitiveType.Cube, "FloorNeon", floor, new Vector3(minX - 0.08f, 0.06f, (minZ + maxZ) * 0.5f), new Vector3(0.12f, 0.12f, maxZ - minZ), dNeon, null, false, false).GetComponent<Renderer>());
            neon.Add(Prim(PrimitiveType.Cube, "FloorNeon", floor, new Vector3(maxX + 0.08f, 0.06f, (minZ + maxZ) * 0.5f), new Vector3(0.12f, 0.12f, maxZ - minZ), dNeon, null, false, false).GetComponent<Renderer>());

            // the crowd: a loose grid with jitter and gaps (lanes to run through)
            var crowd = new GameObject("DancerSpots").transform;
            crowd.SetParent(room.transform, false);
            const float step = 1.9f;
            for (float z = minZ + 0.9f; z <= maxZ - 0.7f; z += step)
                for (float x = minX + 0.9f; x <= maxX - 0.7f; x += step)
                {
                    if (discoRnd.NextDouble() < 0.33) continue;
                    var pos = new Vector3(x + (float)(discoRnd.NextDouble() - 0.5) * 0.6f, 0.1f, z + (float)(discoRnd.NextDouble() - 0.5) * 0.6f);
                    Spot(crowd, pos, lookAt, spots);
                }
        }

        static void Spot(Transform parent, Vector3 pos, Vector3 lookAt, List<Transform> spots)
        {
            var s = new GameObject("Spot").transform;
            s.SetParent(parent, false);
            s.position = pos;
            Vector3 d = lookAt - pos; d.y = 0f;
            s.rotation = Quaternion.LookRotation(d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward);
            spots.Add(s);
        }

        /// <summary>Moving head hanging from a truss: yoke + the rotating head (its +Z is the beam). Returns the head.</summary>
        static Transform MovingHead(Transform p, Vector3 pos, Vector3 aimAt)
        {
            var anchor = new GameObject("MovingHead").transform;
            anchor.SetParent(p, false);
            anchor.position = pos;
            Vector3 d = aimAt - pos; d.y = 0f;
            anchor.rotation = Quaternion.LookRotation(d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward);
            Prim(PrimitiveType.Cube, "Yoke", anchor, new Vector3(0f, 0.12f, 0f), new Vector3(0.45f, 0.08f, 0.2f), dDark, null, false, false);
            var head = new GameObject("Head").transform;
            head.SetParent(anchor, false);
            head.localPosition = new Vector3(0f, -0.1f, 0f);
            head.localRotation = Quaternion.Euler(60f, 0f, 0f);
            Prim(PrimitiveType.Cylinder, "Body", head, new Vector3(0f, 0f, -0.05f), new Vector3(0.32f, 0.22f, 0.32f), dDark, Quaternion.Euler(90f, 0f, 0f), false, false);
            Prim(PrimitiveType.Cylinder, "Lens", head, new Vector3(0f, 0f, 0.17f), new Vector3(0.24f, 0.01f, 0.24f), Emissive("Disco_Lens", new Color(1f, 0.95f, 0.9f), 3f), Quaternion.Euler(90f, 0f, 0f), false, false);
            return head;
        }

        /// <summary>Rectangular truss ring at height y with posts at the corners (posts are cover).</summary>
        static void Truss(Transform p, float minX, float minZ, float maxX, float maxZ, float y, bool crossX)
        {
            var t = new GameObject("Truss").transform;
            t.SetParent(p, false);
            Beam(t, new Vector3(minX, y, minZ), new Vector3(maxX, y, minZ), 0.3f, 0.3f, dDark, false, "TrussBar");
            Beam(t, new Vector3(minX, y, maxZ), new Vector3(maxX, y, maxZ), 0.3f, 0.3f, dDark, false, "TrussBar");
            Beam(t, new Vector3(minX, y, minZ), new Vector3(minX, y, maxZ), 0.3f, 0.3f, dDark, false, "TrussBar");
            Beam(t, new Vector3(maxX, y, minZ), new Vector3(maxX, y, maxZ), 0.3f, 0.3f, dDark, false, "TrussBar");
            if (crossX) Beam(t, new Vector3(minX, y, (minZ + maxZ) * 0.5f), new Vector3(maxX, y, (minZ + maxZ) * 0.5f), 0.25f, 0.25f, dDark, false, "TrussBar");
            else Beam(t, new Vector3((minX + maxX) * 0.5f, y, minZ), new Vector3((minX + maxX) * 0.5f, y, maxZ), 0.25f, 0.25f, dDark, false, "TrussBar");
            foreach (var c in new[] { new Vector2(minX, minZ), new Vector2(maxX, minZ), new Vector2(minX, maxZ), new Vector2(maxX, maxZ) })
                Box("TrussPost", t, new Vector3(c.x, y * 0.5f, c.y), new Vector3(0.35f, y, 0.35f), dDark);
        }

        static Transform MirrorBall(Transform p, Vector3 pos)
        {
            var ball = Prim(PrimitiveType.Sphere, "MirrorBall", p, pos, Vector3.one * 1.3f, dChrome, null, false, false).transform;
            Prim(PrimitiveType.Cylinder, "BallWire", p, pos + Vector3.up * 0.95f, new Vector3(0.03f, 0.35f, 0.03f), dDark, null, false, true);
            // facets: a few hundred tiny mirror chips would be expensive — a ring of chrome bands sells it
            for (int i = 0; i < 6; i++)
                Prim(PrimitiveType.Cylinder, "Band", ball, Vector3.zero, new Vector3(1.02f, 0.01f, 1.02f), dChrome, Quaternion.Euler(0f, i * 30f, 90f), false, false);
            return ball;
        }

        static Light Wash(Transform p, Vector3 pos, float range)
        {
            var go = new GameObject("Wash");
            go.transform.SetParent(p, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = range; l.intensity = 3f; l.color = Color.magenta; l.shadows = LightShadows.None;
            return l;
        }

        static Renderer LedWall(Transform p, Vector3 center, Vector3 facing, float w, float h)
        {
            Prim(PrimitiveType.Cube, "LedFrame", p, center - facing * 0.08f, FrameSize(facing, w + 0.4f, h + 0.4f), dDark, null, false, true);
            var q = Prim(PrimitiveType.Quad, "LedWall", p, center, new Vector3(w, h, 1f), dDark, Quaternion.LookRotation(-facing), false, false);
            q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return q.GetComponent<Renderer>();
        }

        static Vector3 FrameSize(Vector3 facing, float w, float h) => Mathf.Abs(facing.x) > 0.5f ? new Vector3(0.12f, h, w) : new Vector3(w, h, 0.12f);

        static void Speaker(Transform p, Vector3 pos, float yaw, float h)
        {
            var s = Block("Speaker", p, pos.x, pos.z, new Vector3(1.4f, h, 1.2f), dDark, pos.y, yaw);
            s.AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 3; i++)
            {
                float y = pos.y + h * (0.22f + i * 0.3f);
                Prim(PrimitiveType.Cylinder, "Woofer", p, new Vector3(pos.x, y, pos.z) + rot * new Vector3(0f, 0f, 0.61f), new Vector3(0.9f - i * 0.15f, 0.02f, 0.9f - i * 0.15f),
                     Lit("Disco_Cone", new Color(0.02f, 0.02f, 0.025f), 0f, 0.3f), rot * Quaternion.Euler(90f, 0f, 0f), false, true);
            }
        }

        static void DjDesk(Transform p, Vector3 pos, float yaw, List<Renderer> neon)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Block("DjDesk", p, pos.x, pos.z, new Vector3(4.2f, 1.1f, 1.1f), dDark, pos.y, yaw);
            neon.Add(Prim(PrimitiveType.Cube, "DeskNeon", p, pos + Vector3.up * 0.55f + rot * new Vector3(0f, 0f, 0.56f), new Vector3(4.0f, 0.08f, 0.04f), dNeon, rot, false, false).GetComponent<Renderer>());
            foreach (float x in new[] { -1.3f, 1.3f })
                Prim(PrimitiveType.Cylinder, "Deck", p, pos + Vector3.up * 1.12f + rot * new Vector3(x, 0f, 0f), new Vector3(0.6f, 0.02f, 0.6f), dChrome, rot, false, true);
            Prim(PrimitiveType.Cube, "Mixer", p, pos + Vector3.up * 1.14f, new Vector3(0.7f, 0.06f, 0.5f), Emissive("Disco_MixerLeds", new Color(0.2f, 1f, 0.5f), 1.5f), rot, false, true);
        }

        static DiscoRoom MainHall(Transform root, Material tileM, List<Transform> spots)
        {
            var room = NewRoom(root, "Room_Main", "Главный зал", DiscoTrack("Disco_HellOnEarth.mp3"), new Vector3(0f, 0f, 11f), 14f,
                               new Color(1f, 0.1f, 0.55f), new Color(0.1f, 0.85f, 1f), new Color(1f, 0.75f, 0.1f), new Color(0.6f, 0.15f, 1f));
            var p = room.transform;
            var neon = new List<Renderer>();
            Vector3 dj = new Vector3(0f, 1.2f, 22.6f);
            DanceFloor(room, p, -8f, 2f, 8, 7, tileM, spots, dj, neon);

            // stage + DJ + speakers + LED wall
            Box("Stage", p, new Vector3(0f, 0.6f, 23.85f), new Vector3(20f, 1.2f, 7.7f), Lit("Disco_Stage", new Color(0.08f, 0.07f, 0.09f), 0.3f, 0.6f));
            Stairs(p, new Vector3(-8.6f, 0f, 18.2f), Vector3.forward, 2f, 1.2f, dDark);
            Stairs(p, new Vector3(8.6f, 0f, 18.2f), Vector3.forward, 2f, 1.2f, dDark);
            neon.Add(Prim(PrimitiveType.Cube, "StageNeon", p, new Vector3(0f, 1.18f, 19.98f), new Vector3(20f, 0.08f, 0.06f), dNeon, null, false, false).GetComponent<Renderer>());
            DjDesk(p, dj, 180f, neon);
            Speaker(p, new Vector3(-8.6f, 1.2f, 25.5f), 180f, 3.6f);
            Speaker(p, new Vector3(8.6f, 1.2f, 25.5f), 180f, 3.6f);
            Speaker(p, new Vector3(-12.2f, 0f, 19f), 160f, 2.4f);
            Speaker(p, new Vector3(12.2f, 0f, 19f), 200f, 2.4f);
            room.ledWall = LedWall(p, new Vector3(0f, 4.3f, 27.62f), Vector3.back, 16f, 5.4f);

            // truss with moving heads, mirror ball, lasers on the desk
            Truss(p, -9.5f, 1f, 9.5f, 17f, 7.5f, true);
            var heads = new List<Transform>();
            Vector3 floorC = new Vector3(0f, 0f, 9f);
            foreach (var hp in new[] { new Vector3(-9.5f, 7.2f, 1f), new Vector3(9.5f, 7.2f, 1f), new Vector3(-9.5f, 7.2f, 17f), new Vector3(9.5f, 7.2f, 17f),
                                       new Vector3(-9.5f, 7.2f, 9f), new Vector3(9.5f, 7.2f, 9f), new Vector3(-4f, 7.2f, 9f), new Vector3(4f, 7.2f, 9f) })
                heads.Add(MovingHead(p, hp, floorC));
            room.movingHeads = heads.ToArray();
            room.mirrorBall = MirrorBall(p, new Vector3(0f, 6.4f, 9f));
            room.lasers = new[] { Laser(p, new Vector3(-2.4f, 2.45f, 21.9f), 180f, -5f), Laser(p, new Vector3(2.4f, 2.45f, 21.9f), 180f, -5f) };

            // neon along the side walls
            neon.Add(Prim(PrimitiveType.Cube, "WallNeon", p, new Vector3(-13.76f, 5.4f, 13f), new Vector3(0.06f, 0.1f, 26f), dNeon, null, false, false).GetComponent<Renderer>());
            neon.Add(Prim(PrimitiveType.Cube, "WallNeon", p, new Vector3(13.76f, 5.4f, 13f), new Vector3(0.06f, 0.1f, 26f), dNeon, null, false, false).GetComponent<Renderer>());
            room.neon = neon.ToArray();
            room.washes = new[] { Wash(p, new Vector3(-12f, 5f, 4f), 16f), Wash(p, new Vector3(12f, 5f, 4f), 16f), Wash(p, new Vector3(-11f, 5f, 24f), 14f), Wash(p, new Vector3(11f, 5f, 24f), 14f) };

            // high tables around the floor (cover)
            foreach (var tp in new[] { new Vector2(-11.5f, 7f), new Vector2(11.5f, 7f), new Vector2(-11.5f, 15f), new Vector2(11.5f, 15f) })
                HighTable(p, new Vector3(tp.x, 0f, tp.y));
            Sign(p, new Vector3(0f, 6.6f, 27.55f), 180f, "ГЛАВНЫЙ ЗАЛ", new Color(1f, 0.4f, 0.8f), 0.55f, null);
            return room;
        }

        static Transform Laser(Transform p, Vector3 pos, float yaw, float pitch)
        {
            var t = Prim(PrimitiveType.Cube, "LaserUnit", p, pos, new Vector3(0.35f, 0.2f, 0.3f), dDark, Quaternion.Euler(pitch, yaw, 0f), false, false).transform;
            var emitter = new GameObject("Emitter").transform;
            emitter.SetParent(p, false);
            emitter.position = pos + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 0.18f);
            emitter.rotation = Quaternion.Euler(pitch, yaw, 0f);
            return emitter;
        }

        static void HighTable(Transform p, Vector3 pos)
        {
            Prim(PrimitiveType.Cylinder, "TableLeg", p, pos + Vector3.up * 0.55f, new Vector3(0.12f, 0.55f, 0.12f), dChrome, null, true, true);
            Prim(PrimitiveType.Cylinder, "TableTop", p, pos + Vector3.up * 1.1f, new Vector3(0.8f, 0.03f, 0.8f), dDark, null, true, true);
        }

        /// <summary>The Red hall (west) or the Blue laser hall (east): mirrored layouts with different palettes and decor.</summary>
        static void SideHall(Transform root, Material tileM, List<Transform> spots, bool east)
        {
            float s = east ? 1f : -1f;
            var room = east
                ? NewRoom(root, "Room_Blue", "Синий зал", DiscoTrack("Disco_CultistBase.mp3"), new Vector3(27f, 0f, 11f), 13f,
                          new Color(0.1f, 0.35f, 1f), new Color(0.1f, 1f, 0.95f), new Color(0.55f, 0.2f, 1f), new Color(0.9f, 0.95f, 1f))
                : NewRoom(root, "Room_Red", "Красный зал", DiscoTrack("Disco_BloodHarvesting.mp3"), new Vector3(-27f, 0f, 11f), 13f,
                          new Color(1f, 0.08f, 0.05f), new Color(1f, 0.45f, 0.05f), new Color(1f, 0.1f, 0.5f), new Color(1f, 0.85f, 0.6f));
            var p = room.transform;
            var neon = new List<Renderer>();
            Vector3 dj = new Vector3(s * 36.6f, 0.9f, 11f);
            DanceFloor(room, p, east ? 21f : -33f, 5f, 6, 6, tileM, spots, dj, neon);

            // DJ platform against the outer wall, stairs on its north side
            Box("DjPlatform", p, new Vector3(s * 37.55f, 0.45f, 11f), new Vector3(4.2f, 0.9f, 8f), Lit("Disco_Stage", new Color(0.08f, 0.07f, 0.09f), 0.3f, 0.6f));
            Stairs(p, new Vector3(s * 37.5f, 0f, 16.5f), Vector3.back, 2f, 0.9f, dDark);
            DjDesk(p, dj, east ? -90f : 90f, neon);
            Speaker(p, new Vector3(s * 37.6f, 0.9f, 7.8f), east ? -90f : 90f, 2.8f);
            Speaker(p, new Vector3(s * 37.6f, 0.9f, 14.2f), east ? -90f : 90f, 2.8f);
            room.ledWall = LedWall(p, new Vector3(s * 39.62f, 3.9f, 11f), new Vector3(-s, 0f, 0f), 9f, 4f);

            Truss(p, east ? 20f : -34f, 4f, east ? 34f : -20f, 18f, 6.8f, false);
            var heads = new List<Transform>();
            Vector3 c = new Vector3(s * 27f, 0f, 11f);
            foreach (var hp in new[] { new Vector3(s * 20f, 6.5f, 4f), new Vector3(s * 34f, 6.5f, 4f), new Vector3(s * 20f, 6.5f, 18f), new Vector3(s * 34f, 6.5f, 18f),
                                       new Vector3(s * 27f, 6.5f, 4f), new Vector3(s * 27f, 6.5f, 18f) })
                heads.Add(MovingHead(p, hp, c));
            room.movingHeads = heads.ToArray();
            room.mirrorBall = east ? null : MirrorBall(p, new Vector3(s * 27f, 5.8f, 11f));

            var lasers = new List<Transform>
            {
                Laser(p, new Vector3(s * 36.1f, 2.1f, 9.4f), east ? -90f : 90f, -4f),
                Laser(p, new Vector3(s * 36.1f, 2.1f, 12.6f), east ? -90f : 90f, -4f),
            };
            if (east)
            {
                // laser hall: extra emitters high in the corners sweep the whole room
                lasers.Add(Laser(p, new Vector3(15.2f, 5.6f, 0f), 45f, 12f));
                lasers.Add(Laser(p, new Vector3(15.2f, 5.6f, 26f), 135f, 12f));
                lasers.Add(Laser(p, new Vector3(39.2f, 5.6f, 26f), -135f, 12f));
            }
            room.lasers = lasers.ToArray();

            neon.Add(Prim(PrimitiveType.Cube, "WallNeon", p, new Vector3(s * 27f, 5.2f, 27.65f), new Vector3(24f, 0.1f, 0.06f), dNeon, null, false, false).GetComponent<Renderer>());
            neon.Add(Prim(PrimitiveType.Cube, "WallNeon", p, new Vector3(s * 27f, 5.2f, -5.75f), new Vector3(24f, 0.1f, 0.06f), dNeon, null, false, false).GetComponent<Renderer>());
            room.neon = neon.ToArray();
            room.washes = new[] { Wash(p, new Vector3(s * 18f, 4.5f, 0f), 15f), Wash(p, new Vector3(s * 36f, 4.5f, 24f), 15f), Wash(p, new Vector3(s * 18f, 4.5f, 24f), 15f) };

            // a side bar on the north wall, lounge cubes and high tables (cover)
            var barM = Lit(east ? "Disco_BarBlue" : "Disco_BarRed", east ? new Color(0.05f, 0.08f, 0.16f) : new Color(0.16f, 0.04f, 0.04f), 0.2f, 0.8f);
            Block("SideBar", p, s * 27f, 25.6f, new Vector3(9f, 1.1f, 0.8f), barM);
            neon.Add(Prim(PrimitiveType.Cube, "BarNeon", p, new Vector3(s * 27f, 0.1f, 25.18f), new Vector3(9f, 0.06f, 0.06f), dNeon, null, false, false).GetComponent<Renderer>());
            room.neon = neon.ToArray();
            foreach (var tp in new[] { new Vector2(s * 18f, 8f), new Vector2(s * 18f, 14f), new Vector2(s * 24f, 22f), new Vector2(s * 31f, 22f) })
                HighTable(p, new Vector3(tp.x, 0f, tp.y));
            var cube = Lit(east ? "Disco_CubeBlue" : "Disco_CubeRed", east ? new Color(0.15f, 0.3f, 0.9f) : new Color(0.85f, 0.12f, 0.1f), 0f, 0.6f);
            Block("LoungeCube", p, s * 36f, 1.5f, new Vector3(1.2f, 0.9f, 1.2f), cube);
            Block("LoungeCube", p, s * 33.5f, 0.5f, new Vector3(1.2f, 0.9f, 1.2f), cube, 0f, 20f);
            Block("LoungeCube", p, s * 36f, 21.5f, new Vector3(1.2f, 0.9f, 1.2f), cube, 0f, -15f);
            for (int i = 0; i < 3; i++)
                Spot(p, new Vector3(s * (24f + i * 2.4f), 0f, 24.4f), new Vector3(s * (24f + i * 2.4f), 0f, 26f), spots);
            Sign(p, new Vector3(s * 27f, 6.1f, 27.55f), 180f, east ? "СИНИЙ ЗАЛ" : "КРАСНЫЙ ЗАЛ", east ? new Color(0.4f, 0.7f, 1f) : new Color(1f, 0.35f, 0.25f), 0.5f, null);
        }

        // ================================================================== VIP balcony (main hall, south)

        struct VipTheme { public string name; public Color neon, velvet; }

        static void Balcony(Transform root, DiscoRoom main, List<Transform> spots)
        {
            var p = new GameObject("VipBalcony").transform;
            p.SetParent(root, false);
            var deck = Lit("Disco_Deck", new Color(0.1f, 0.09f, 0.11f), 0.5f, 0.5f);
            Slab(p, -13.8f, -5.8f, 13.8f, 0f, 3f, 0.3f, deck);
            foreach (float x in new[] { -11.2f, -5.6f, 0f, 5.6f, 11.2f })
                Box("Column", p, new Vector3(x, 1.35f, -0.25f), new Vector3(0.4f, 2.7f, 0.4f), dDark);
            Stairs(p, new Vector3(-12.6f, 0f, 4.5f), Vector3.back, 2.2f, 3f, dDark);
            Stairs(p, new Vector3(12.6f, 0f, 4.5f), Vector3.back, 2.2f, 3f, dDark);

            // glass balustrade along the front (bullets stop on it — it is low)
            Box("GlassRail", p, new Vector3(0f, 3.5f, -0.1f), new Vector3(22.8f, 1f, 0.06f), dGlass, false);
            Beam(p, new Vector3(-11.4f, 4.02f, -0.1f), new Vector3(11.4f, 4.02f, -0.1f), 0.07f, 0.05f, dChrome, true, "RailTop");

            VipTheme[] themes =
            {
                new VipTheme { name = "РУБИН", neon = new Color(1f, 0.1f, 0.25f), velvet = new Color(0.45f, 0.03f, 0.08f) },
                new VipTheme { name = "ИЗУМРУД", neon = new Color(0.1f, 1f, 0.45f), velvet = new Color(0.03f, 0.3f, 0.15f) },
                new VipTheme { name = "САПФИР", neon = new Color(0.15f, 0.45f, 1f), velvet = new Color(0.04f, 0.1f, 0.4f) },
                new VipTheme { name = "АМЕТИСТ", neon = new Color(0.7f, 0.2f, 1f), velvet = new Color(0.25f, 0.06f, 0.4f) },
            };
            float[] cx = { -8.4f, -2.8f, 2.8f, 8.4f };
            foreach (float x in new[] { -11.2f, -5.6f, 0f, 5.6f, 11.2f })
                Box("CabinGlass", p, new Vector3(x, 4.35f, -4f), new Vector3(0.12f, 2.7f, 3.8f), Glass("Disco_GlassSmoke", new Color(0.2f, 0.1f, 0.25f, 0.45f)), false);
            for (int i = 0; i < 4; i++)
            {
                var th = themes[i];
                var neonM = Emissive("Disco_Vip_" + i, th.neon, 3f);
                var velvet = Lit("Disco_Velvet_" + i, th.velvet, 0f, 0.25f);
                // canopy: shelter from the head's rockets
                Box("Canopy", p, new Vector3(cx[i], 5.85f, -3.9f), new Vector3(5.5f, 0.15f, 4.2f), Lit("Disco_Canopy_" + i, th.velvet * 0.6f, 0.6f, 0.6f), false);
                Prim(PrimitiveType.Cube, "CanopyNeon", p, new Vector3(cx[i], 5.75f, -1.82f), new Vector3(5.4f, 0.06f, 0.06f), neonM, null, false, true);
                Sofa(p, new Vector3(cx[i], 3f, -5.3f), 4.8f, 0f, velvet);
                Sofa(p, new Vector3(cx[i] - 2.25f, 3f, -3.6f), 1.8f, 90f, velvet);
                Sofa(p, new Vector3(cx[i] + 2.25f, 3f, -3.6f), 1.8f, -90f, velvet);
                Block("VipTable", p, cx[i], -3.6f, new Vector3(1.6f, 0.45f, 0.8f), dChrome, 3f);
                Bottles(p, new Vector3(cx[i], 3.45f, -3.6f), th.neon);
                Sign(p, new Vector3(cx[i], 5.45f, -1.78f), 0f, "VIP · " + th.name, th.neon, 0.22f, null);
                var lamp = new GameObject("VipLamp");
                lamp.transform.SetParent(p, false);
                lamp.transform.position = new Vector3(cx[i], 5.4f, -3.8f);
                var l = lamp.AddComponent<Light>();
                l.type = LightType.Point; l.color = th.neon; l.range = 5.5f; l.intensity = 2.2f; l.shadows = LightShadows.None;
            }

            // under the balcony: the bar (east half) and a lounge (west half)
            var barWood = Lit("Disco_BarWood", new Color(0.12f, 0.06f, 0.04f), 0f, 0.7f);
            Block("Bar", p, 6.6f, -3.1f, new Vector3(8.6f, 1.1f, 0.7f), barWood);
            Box("BarTop", p, new Vector3(6.6f, 1.13f, -3.1f), new Vector3(8.8f, 0.06f, 0.9f), Lit("Disco_BarTop", new Color(0.02f, 0.02f, 0.025f), 0.3f, 0.95f));
            Prim(PrimitiveType.Cube, "BarNeon", p, new Vector3(6.6f, 0.1f, -2.72f), new Vector3(8.6f, 0.05f, 0.05f), Emissive("Disco_BarNeon", new Color(1f, 0.6f, 0.1f), 3f), null, false, true);
            Block("BarShelf", p, 6.6f, -5.55f, new Vector3(8.6f, 2.4f, 0.4f), dDark);
            for (int row = 0; row < 3; row++)
                for (int k = 0; k < 18; k++)
                {
                    float bx = 2.6f + k * 0.47f;
                    var col = Color.HSVToRGB((float)discoRnd.NextDouble(), 0.7f, 1f);
                    Prim(PrimitiveType.Cylinder, "Bottle", p, new Vector3(bx, 0.95f + row * 0.62f, -5.25f), new Vector3(0.1f, 0.14f, 0.1f),
                         Emissive("Disco_Bottle_" + (k % 5), Color.HSVToRGB((k % 5) / 5f, 0.6f, 0.9f), 1.2f), null, false, true);
                }
            for (int k = 0; k < 7; k++) Stool(p, new Vector3(3f + k * 1.2f, 0f, -2.2f));
            var lounge = Lit("Disco_Lounge", new Color(0.18f, 0.05f, 0.2f), 0f, 0.3f);
            Sofa(p, new Vector3(-11f, 0f, -5.3f), 3f, 0f, lounge);
            Sofa(p, new Vector3(-2.6f, 0f, -5.3f), 2.4f, 0f, lounge);
            Block("LoungeTable", p, -11f, -3.8f, new Vector3(1.6f, 0.4f, 0.8f), dChrome);
            foreach (float x in new[] { -8f, 0f, 7f })
            {
                var lg = new GameObject("UnderLamp");
                lg.transform.SetParent(p, false);
                lg.transform.position = new Vector3(x, 2.5f, -3f);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.3f); l.range = 6f; l.intensity = 2f; l.shadows = LightShadows.None;
            }
            // a few mannequins at the bar
            for (int k = 0; k < 4; k++) Spot(main.transform.Find("DancerSpots"), new Vector3(3.6f + k * 1.8f, 0f, -1.6f), new Vector3(3.6f + k * 1.8f, 0f, -4f), spots);
        }

        static void Sofa(Transform p, Vector3 pos, float length, float yaw, Material m)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Prim(PrimitiveType.Cube, "SofaSeat", p, pos + Vector3.up * 0.22f, new Vector3(length, 0.45f, 0.8f), m, rot, true, true);
            Prim(PrimitiveType.Cube, "SofaBack", p, pos + Vector3.up * 0.7f + rot * new Vector3(0f, 0f, -0.32f), new Vector3(length, 0.6f, 0.18f), m, rot, true, true);
        }

        static void Stool(Transform p, Vector3 pos)
        {
            Prim(PrimitiveType.Cylinder, "StoolLeg", p, pos + Vector3.up * 0.38f, new Vector3(0.06f, 0.38f, 0.06f), dChrome, null, false, true);
            Prim(PrimitiveType.Cylinder, "StoolSeat", p, pos + Vector3.up * 0.78f, new Vector3(0.42f, 0.04f, 0.42f), dDark, null, true, true);
        }

        static void Bottles(Transform p, Vector3 pos, Color c)
        {
            var glow = Emissive("Disco_Champagne", new Color(0.9f, 0.8f, 0.4f), 1.4f);
            Prim(PrimitiveType.Cylinder, "Bucket", p, pos + Vector3.up * 0.12f, new Vector3(0.3f, 0.12f, 0.3f), dChrome, null, false, true);
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Cylinder, "Bottle", p, pos + new Vector3(-0.06f + i * 0.06f, 0.3f, 0.02f * i), new Vector3(0.07f, 0.16f, 0.07f), glow, Quaternion.Euler(12f - i * 12f, 0f, 8f), false, true);
        }

        // ================================================================== covered south band

        static Light DLamp(Transform p, Vector3 pos, Color c, float range, float intensity)
        {
            var go = new GameObject("Lamp");
            go.transform.SetParent(p, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.range = range; l.intensity = intensity; l.shadows = LightShadows.None;
            return l;
        }

        static void Lobby(Transform root)
        {
            var p = new GameObject("Lobby").transform;
            p.SetParent(root, false);
            var red = Lit("Disco_Carpet", new Color(0.22f, 0.03f, 0.05f), 0f, 0.1f);
            Box("Carpet", p, new Vector3(0f, 0.01f, -17f), new Vector3(10f, 0.02f, 18f), red, true).GetComponent<Collider>().enabled = false;
            // cloakroom
            Block("Cloakroom", p, 0f, -25.4f, new Vector3(10f, 1.1f, 0.7f), Lit("Disco_BarWood", new Color(0.12f, 0.06f, 0.04f), 0f, 0.7f));
            Beam(p, new Vector3(-4.5f, 2.1f, -27.2f), new Vector3(4.5f, 2.1f, -27.2f), 0.04f, 0.04f, dChrome, false, "CoatRail");
            for (int i = 0; i < 16; i++)
            {
                var col = Color.HSVToRGB((float)discoRnd.NextDouble(), 0.4f, 0.25f + (float)discoRnd.NextDouble() * 0.3f);
                Prim(PrimitiveType.Cube, "Coat", p, new Vector3(-4.2f + i * 0.55f, 1.55f, -27.2f), new Vector3(0.45f, 1.0f, 0.12f), Lit("Disco_Coat_" + (i % 4), col, 0f, 0.15f), null, false, true);
            }
            foreach (var c in new[] { new Vector2(-6f, -18f), new Vector2(6f, -18f), new Vector2(-6f, -11f), new Vector2(6f, -11f) })
            {
                Box("Column", p, new Vector3(c.x, 2.3f, c.y), new Vector3(0.7f, 4.6f, 0.7f), dChrome);
                Prim(PrimitiveType.Cube, "ColumnNeon", p, new Vector3(c.x, 2.3f, c.y), new Vector3(0.74f, 0.05f, 0.74f), Emissive("Disco_LobbyNeon", new Color(1f, 0.25f, 0.7f), 3f), null, false, true);
            }
            var leather = Lit("Disco_Leather", new Color(0.04f, 0.04f, 0.05f), 0f, 0.55f);
            Sofa(p, new Vector3(-11f, 0f, -19f), 4f, 90f, leather);
            Sofa(p, new Vector3(11f, 0f, -19f), 4f, -90f, leather);
            Block("RoundTable", p, -9.8f, -14f, new Vector3(1.1f, 0.75f, 1.1f), dChrome);
            Block("RoundTable", p, 9.8f, -24f, new Vector3(1.1f, 0.75f, 1.1f), dChrome);
            Block("TicketBooth", p, -9.5f, -25.5f, new Vector3(3f, 2.4f, 2.2f), dDark);
            Box("BoothGlass", p, new Vector3(-9.5f, 1.7f, -24.38f), new Vector3(2.6f, 0.9f, 0.05f), dGlass, false);
            // fake entrance doors with the exit sign
            Prim(PrimitiveType.Cube, "EntranceDoors", p, new Vector3(9.5f, 1.4f, -27.68f), new Vector3(3f, 2.8f, 0.06f), dChrome, null, false, true);
            Prim(PrimitiveType.Cube, "ExitSign", p, new Vector3(9.5f, 3.1f, -27.66f), new Vector3(0.9f, 0.3f, 0.04f), Emissive("Disco_Exit", new Color(0.1f, 1f, 0.3f), 2.5f), null, false, true);
            Sign(p, new Vector3(0f, 3.5f, -27.62f), 0f, "ДИСКОТЕКА", new Color(1f, 0.3f, 0.75f), 0.7f, null);
            DLamp(p, new Vector3(0f, 4f, -11f), new Color(1f, 0.4f, 0.75f), 12f, 2.4f);
            DLamp(p, new Vector3(0f, 4f, -22f), new Color(1f, 0.55f, 0.35f), 12f, 2.4f);
            DLamp(p, new Vector3(-10f, 4f, -18f), new Color(0.5f, 0.3f, 1f), 9f, 2f);
            DLamp(p, new Vector3(10f, 4f, -18f), new Color(0.5f, 0.3f, 1f), 9f, 2f);
        }

        static void VipGallery(Transform root)
        {
            var p = new GameObject("VipGallery").transform;
            p.SetParent(root, false);
            var wallM = Lit("Disco_VipWall", new Color(0.08f, 0.06f, 0.07f), 0f, 0.3f);
            // partitions between the four cabins
            foreach (float x in new[] { -33.5f, -27f, -20.5f })
                Box("CabinWall", p, new Vector3(x, 2.3f, -24.2f), new Vector3(0.25f, 4.6f, 7.6f), wallM);

            // 0 — GOLD: solid front, wide doorway, gold columns
            {
                float x0 = -40f, cx = -36.75f;
                var gold = Lit("Disco_Gold", new Color(1f, 0.75f, 0.3f), 1f, 0.85f);
                Wall(p, new Vector3(x0 + 0.3f, 0f, -20.4f), new Vector3(x0 + 6.5f, 0f, -20.4f), 0f, 4.6f, 0.25f, wallM, Door(3.1f, 2.4f, 2.8f));
                Box("GoldColumn", p, new Vector3(cx - 1.55f, 1.4f, -20.15f), new Vector3(0.3f, 2.8f, 0.3f), gold);
                Box("GoldColumn", p, new Vector3(cx + 1.55f, 1.4f, -20.15f), new Vector3(0.3f, 2.8f, 0.3f), gold);
                Cabin(p, cx, Lit("Disco_VelvetRed", new Color(0.45f, 0.03f, 0.05f), 0f, 0.25f), new Color(1f, 0.75f, 0.3f), "ЗОЛОТО");
                Prim(PrimitiveType.Sphere, "Chandelier", p, new Vector3(cx, 3.8f, -24.5f), new Vector3(1f, 0.6f, 1f), Emissive("Disco_Chandelier", new Color(1f, 0.85f, 0.55f), 2.5f), null, false, true);
            }
            // 1 — CAGE: a front of bars (you can shoot through the gaps) and a dance pole
            {
                float x0 = -33.5f, cx = -30.25f;
                for (float x = x0 + 0.35f; x < x0 + 6.3f; x += 0.32f)
                {
                    if (Mathf.Abs(x - cx) < 0.75f) continue;
                    Prim(PrimitiveType.Cylinder, "Bar", p, new Vector3(x, 2.3f, -20.4f), new Vector3(0.06f, 2.3f, 0.06f), dChrome, null, true, true);
                }
                Beam(p, new Vector3(x0 + 0.2f, 4.5f, -20.4f), new Vector3(x0 + 6.3f, 4.5f, -20.4f), 0.12f, 0.12f, dChrome, true, "CageTop");
                Cabin(p, cx, Lit("Disco_Leather", new Color(0.04f, 0.04f, 0.05f), 0f, 0.55f), new Color(1f, 0.1f, 0.3f), "КЛЕТКА");
                Prim(PrimitiveType.Cylinder, "Podium", p, new Vector3(cx, 0.15f, -23.6f), new Vector3(1.4f, 0.15f, 1.4f), dChrome, null, true, true);
                Prim(PrimitiveType.Cylinder, "Pole", p, new Vector3(cx, 2.3f, -23.6f), new Vector3(0.07f, 2.3f, 0.07f), dChrome, null, true, true);
            }
            // 2 — AQUARIUM: glass front, glowing aquarium along the back wall
            {
                float x0 = -27f, cx = -23.75f;
                var glassBlue = Glass("Disco_GlassBlue", new Color(0.2f, 0.6f, 1f, 0.3f));
                Box("GlassFront", p, new Vector3(x0 + 1.7f, 1.6f, -20.4f), new Vector3(3.1f, 3.2f, 0.08f), glassBlue, false);
                Box("GlassFront", p, new Vector3(x0 + 5.6f, 1.6f, -20.4f), new Vector3(1.7f, 3.2f, 0.08f), glassBlue, false);
                Box("GlassHeader", p, new Vector3(x0 + 3.25f, 3.9f, -20.4f), new Vector3(6.2f, 1.4f, 0.25f), wallM);
                Box("Aquarium", p, new Vector3(cx, 2.1f, -27.45f), new Vector3(5.6f, 2.6f, 0.5f), Emissive("Disco_Aquarium", new Color(0.05f, 0.45f, 0.9f), 1.8f));
                for (int i = 0; i < 9; i++)
                    Prim(PrimitiveType.Sphere, "Fish", p, new Vector3(cx - 2.4f + (float)discoRnd.NextDouble() * 4.8f, 1.1f + (float)discoRnd.NextDouble() * 2f, -27.18f),
                         new Vector3(0.28f, 0.14f, 0.05f), Emissive("Disco_Fish", new Color(1f, 0.5f, 0.1f), 2f), null, false, true);
                Cabin(p, cx, Lit("Disco_VelvetTeal", new Color(0.03f, 0.2f, 0.25f), 0f, 0.25f), new Color(0.1f, 0.7f, 1f), "АКВАРИУМ");
            }
            // 3 — KARAOKE: wall with a door and a window, a big screen and a mic stand on a small stage
            {
                float x0 = -20.5f, cx = -17.25f;
                Wall(p, new Vector3(x0 + 0.15f, 0f, -20.4f), new Vector3(x0 + 6.5f, 0f, -20.4f), 0f, 4.6f, 0.25f, wallM, Door(1.4f, 1.4f), Window(4.2f, 2.4f, 1.1f, 2.3f));
                Box("KaraokeScreen", p, new Vector3(cx, 2.4f, -27.6f), new Vector3(4.2f, 2.2f, 0.1f), Emissive("Disco_Karaoke", new Color(1f, 0.3f, 0.8f), 2f));
                Box("KaraokeStage", p, new Vector3(cx, 0.15f, -26.6f), new Vector3(4f, 0.3f, 1.6f), dDark);
                Prim(PrimitiveType.Cylinder, "MicStand", p, new Vector3(cx, 1.05f, -26.4f), new Vector3(0.03f, 0.75f, 0.03f), dChrome, null, false, true);
                Prim(PrimitiveType.Sphere, "Mic", p, new Vector3(cx, 1.85f, -26.35f), Vector3.one * 0.08f, dDark, null, false, true);
                Cabin(p, cx, Lit("Disco_VelvetPink", new Color(0.4f, 0.06f, 0.25f), 0f, 0.25f), new Color(1f, 0.3f, 0.8f), "КАРАОКЕ", true);
            }

            // the lounge corridor in front of the cabins
            foreach (float x in new[] { -34f, -27f, -20f })
                Box("Pillar", p, new Vector3(x, 2.3f, -13f), new Vector3(0.7f, 4.6f, 0.7f), wallM);
            var leather = Lit("Disco_Leather", new Color(0.04f, 0.04f, 0.05f), 0f, 0.55f);
            Sofa(p, new Vector3(-36f, 0f, -7.0f), 3.5f, 180f, leather);
            Sofa(p, new Vector3(-25f, 0f, -7.0f), 3.5f, 180f, leather);
            Block("Hookah", p, -37f, -13f, new Vector3(0.9f, 0.6f, 0.9f), dChrome);
            Prim(PrimitiveType.Cube, "GalleryNeon", p, new Vector3(-27f, 4.35f, -20.25f), new Vector3(25.5f, 0.06f, 0.06f), Emissive("Disco_GalleryNeon", new Color(0.7f, 0.2f, 1f), 3f), null, false, true);
            Sign(p, new Vector3(-27f, 3.9f, -6.45f), 180f, "VIP-ГАЛЕРЕЯ", new Color(0.8f, 0.5f, 1f), 0.4f, null);
            DLamp(p, new Vector3(-33f, 4f, -13f), new Color(0.6f, 0.25f, 1f), 10f, 2.2f);
            DLamp(p, new Vector3(-21f, 4f, -13f), new Color(1f, 0.3f, 0.6f), 10f, 2.2f);
        }

        /// <summary>Inside of a gallery cabin (x center, back wall at z = -27.7): U-sofa, table, lamp and a name sign over the door.</summary>
        static void Cabin(Transform p, float cx, Material velvet, Color neon, string name, bool karaoke = false)
        {
            if (!karaoke) Sofa(p, new Vector3(cx, 0f, -27.1f), 5.4f, 0f, velvet);
            Sofa(p, new Vector3(cx - 2.75f, 0f, -24.6f), 3.2f, 90f, velvet);
            Sofa(p, new Vector3(cx + 2.75f, 0f, -24.6f), 3.2f, -90f, velvet);
            Block("CabinTable", p, cx, -24.4f, new Vector3(1.6f, 0.45f, 0.9f), dChrome);
            Bottles(p, new Vector3(cx, 0.45f, -24.4f), neon);
            DLamp(p, new Vector3(cx, 3.9f, -24.5f), neon, 6.5f, 2.6f);
            Sign(p, new Vector3(cx, 3.95f, -20.25f), 0f, "VIP · " + name, neon, 0.26f, null);
        }

        static void Backstage(Transform root)
        {
            var p = new GameObject("Backstage").transform;
            p.SetParent(root, false);
            var wallM = Lit("Disco_StaffWall", new Color(0.22f, 0.22f, 0.24f), 0f, 0.2f);
            var wood = Lit("Disco_CrateWood", new Color(0.4f, 0.3f, 0.2f), 0f, 0.25f);
            var tile = Lit("Disco_WCTile", new Color(0.55f, 0.58f, 0.6f), 0f, 0.6f);

            // staff room in the south-west corner
            Wall(p, new Vector3(14.2f, 0f, -20f), new Vector3(24f, 0f, -20f), 0f, 4.6f, 0.2f, wallM, Door(7f, 1.4f));
            Wall(p, new Vector3(24f, 0f, -28f), new Vector3(24f, 0f, -20f), 0f, 4.6f, 0.2f, wallM, Door(4f, 1.4f), Window(6.5f, 1.4f, 1.1f, 2.1f));
            Block("StaffTable", p, 18.5f, -24.5f, new Vector3(2.2f, 0.8f, 1.2f), wood);
            for (int i = 0; i < 5; i++) Block("Locker", p, 15.5f + i * 0.8f, -27.4f, new Vector3(0.75f, 2f, 0.55f), Lit("Disco_Locker", new Color(0.2f, 0.3f, 0.35f), 0.6f, 0.4f));
            DLamp(p, new Vector3(19f, 4f, -24f), new Color(0.85f, 0.9f, 1f), 8f, 1.6f);

            // toilets: open stalls along the south wall, sinks on the partition
            Box("WCWall", p, new Vector3(34f, 0.01f, -26f), new Vector3(12f, 0.02f, 4f), tile, true).GetComponent<Collider>().enabled = false;
            foreach (float x in new[] { 28f, 31f, 34f, 37f })
                Box("Stall", p, new Vector3(x, 1.1f, -26.2f), new Vector3(0.1f, 2.2f, 3.4f), tile);
            for (int i = 0; i < 4; i++)
            {
                float x = 29.5f + i * 3f;
                Prim(PrimitiveType.Cube, "Toilet", p, new Vector3(x, 0.25f, -27.3f), new Vector3(0.45f, 0.5f, 0.6f), Lit("Disco_Porcelain", new Color(0.9f, 0.9f, 0.92f), 0f, 0.9f), null, true, true);
            }
            Block("Sinks", p, 32.5f, -23f, new Vector3(6f, 0.9f, 0.6f), Lit("Disco_Porcelain", new Color(0.9f, 0.9f, 0.92f), 0f, 0.9f));
            Prim(PrimitiveType.Cube, "Mirror", p, new Vector3(32.5f, 1.8f, -23.33f), new Vector3(5.6f, 1f, 0.02f), dChrome, null, false, true);
            DLamp(p, new Vector3(33f, 4f, -25f), new Color(0.8f, 0.95f, 1f), 9f, 1.8f);

            // storage: flight cases, speaker stacks and crates (breakable)
            var caseM = Lit("Disco_FlightCase", new Color(0.05f, 0.05f, 0.06f), 0.3f, 0.5f);
            Block("FlightCase", p, 21f, -11f, new Vector3(2.2f, 1.2f, 1.1f), caseM, 0f, 8f);
            Block("FlightCase", p, 21f, -11f, new Vector3(1.6f, 0.9f, 1f), caseM, 1.2f, -5f);
            Block("FlightCase", p, 27.5f, -15.5f, new Vector3(1.4f, 1.4f, 1.4f), caseM, 0f, 20f);
            Speaker(p, new Vector3(33f, 0f, -9f), 180f, 2.2f);
            Speaker(p, new Vector3(34.6f, 0f, -9f), 180f, 2.2f);
            Crate(p, new Vector3(37.5f, 0f, -14f), 1.2f, 10f, wood);
            Crate(p, new Vector3(37.4f, 1.2f, -14.1f), 1f, 30f, wood);
            Crate(p, new Vector3(36.2f, 0f, -16f), 1.1f, -12f, wood);
            Crate(p, new Vector3(25f, 0f, -9.5f), 1.1f, 5f, wood);
            Crate(p, new Vector3(30f, 0f, -19f), 1.2f, -20f, wood);
            Barrel(p, new Vector3(16f, 0f, -8f), Lit("Disco_Barrel", new Color(0.1f, 0.12f, 0.35f), 0.6f, 0.45f));
            Barrel(p, new Vector3(16.8f, 0f, -8.6f), Lit("Disco_Barrel", new Color(0.1f, 0.12f, 0.35f), 0.6f, 0.45f));
            Sign(p, new Vector3(27f, 3.6f, -6.45f), 180f, "ТОЛЬКО ДЛЯ ПЕРСОНАЛА", new Color(0.9f, 0.9f, 0.9f), 0.3f, null);
            DLamp(p, new Vector3(22f, 4f, -12f), new Color(0.75f, 0.85f, 1f), 10f, 1.6f);
            DLamp(p, new Vector3(33f, 4f, -14f), new Color(0.75f, 0.85f, 1f), 10f, 1.6f);
        }
    }
}
