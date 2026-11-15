using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Minimap in the bottom-left corner (under the health bar). The plan is drawn once per map from the bots' NavMesh:
    /// the walkable floor is light (higher floors lighter), walls, closed buildings and drops stay dark, with a bright rim
    /// along the edges and a dark line on ledges — a radar picture that reads the same on night maps. Everything above
    /// the ground floor + <see cref="CutAbove"/> is cut off (roofs). The window turns with the view (or keeps north up,
    /// in the settings). On it: the player's arrow and view cone, teammates (blue dots), fallen teammates (green "+",
    /// a medic can still raise them), enemies only while their gunfire gives them away (red, fading ~2 s after a shot)
    /// or inside the sniper's recon, drone-war reveal rings, and whatever the game mode adds through
    /// <see cref="CollectMarkers"/> (capture points, bomb sites, the bomb).
    /// </summary>
    [DefaultExecutionOrder(125)]
    public class MiniMap : MonoBehaviour
    {
        public struct Marker
        {
            public Vector3 pos;
            /// <summary>Dot / ring colour.</summary>
            public Color color;
            /// <summary>Dot diameter in reference pixels (0 = no dot).</summary>
            public float size;
            /// <summary>Ring radius in metres (0 = none), e.g. a capture zone.</summary>
            public float ring;
            /// <summary>Text over the dot ("A", "+", "B").</summary>
            public string label;
            /// <summary>Text colour (alpha 0 = white).</summary>
            public Color labelColor;
            /// <summary>Blinks.</summary>
            public bool pulse;
            /// <summary>Stays on the rim of the window when out of view (objectives).</summary>
            public bool edge;
        }

        /// <summary>Game modes add their objectives (capture points, bomb sites, the bomb) every frame.</summary>
        public static event Action<List<Marker>> CollectMarkers;

        public static MiniMap Instance { get; private set; }

        const float ViewSize = 230f;   // window, reference pixels (1920x1080)
        const float CutAbove = 4.4f;   // the plan keeps floors up to this height over the ground floor
        const float Shot = 2.2f;       // an enemy stays on the map this long after a shot

        static readonly Color Bg = new Color(0.05f, 0.06f, 0.07f, 0.74f);
        static readonly Color FloorLow = new Color(0.33f, 0.37f, 0.40f, 0.93f);
        static readonly Color FloorHigh = new Color(0.63f, 0.67f, 0.70f, 0.96f);
        static readonly Color Rim = new Color(0.87f, 0.9f, 0.92f, 0.98f);
        static readonly Color Ledge = new Color(0.17f, 0.2f, 0.23f, 0.95f);
        static readonly Color Me = new Color(1f, 0.86f, 0.3f);
        static readonly Color Down = new Color(0.45f, 1f, 0.5f);

        Texture2D plan;
        Rect world;                    // plan area (x, z), square
        float pxPerM = 3f;             // reference pixels per metre in the window
        float floorY;
        RectTransform rotator, mapRT, ringLayer, dotLayer, labelLayer, northMark, coneRT, arrowRT;
        RawImage mapImage;
        CanvasGroup group;
        readonly List<Image> rings = new List<Image>(), dots = new List<Image>();
        readonly List<Text> labels = new List<Text>();
        readonly List<Marker> markers = new List<Marker>();
        Sprite circle, ringSprite, white;
        Font font;
        bool ready;

        public static void Ensure()
        {
            if (Instance) return;
            Instance = new GameObject("MiniMap").AddComponent<MiniMap>();
        }

        /// <summary>Ground floor height the plan was cut at.</summary>
        public static float FloorY => Instance ? Instance.floorY : 0f;

        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var tex = Texture2D.whiteTexture;
            white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            circle = MakeDisc(false);
            ringSprite = MakeDisc(true);
            Build();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (plan) Destroy(plan);
        }

        IEnumerator Start()
        {
            // a couple of frames for the match to place everybody, then the plan
            yield return null;
            yield return null;
            float t0 = Time.realtimeSinceStartup;
            try { BuildPlan(); }
            catch (Exception e) { Debug.LogException(e); }
            Debug.Log($"[MiniMap] plan {(plan ? plan.width : 0)}px over {world.width:0} m, floor {floorY:0.0} m, {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
            ready = true;
        }

        // ------------------------------------------------------------------ UI

        void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 19; // just under the match HUD (20)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // bottom-left, under the health bar (which sits 250 px up)
            var frame = Box("Frame", transform, Vector2.zero, new Vector2(26f + ViewSize * 0.5f, 12f + ViewSize * 0.5f), new Vector2(ViewSize + 6f, ViewSize + 6f));
            group = frame.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            Img(frame, new Color(0f, 0f, 0f, 0.5f));
            var view = Box("View", frame, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewSize, ViewSize));
            view.gameObject.AddComponent<RectMask2D>();
            Img(view, Bg);
            rotator = Box("Rotator", view, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            mapRT = Box("Plan", rotator, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));
            mapImage = mapRT.gameObject.AddComponent<RawImage>();
            mapImage.raycastTarget = false;
            mapImage.enabled = false;

            // the player: view cone and arrow in the middle
            coneRT = Box("Cone", view, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120f, 70f));
            coneRT.pivot = new Vector2(0.5f, 0f);
            coneRT.anchoredPosition = Vector2.zero;
            Img(coneRT, new Color(1f, 0.92f, 0.6f, 0.2f)).sprite = MakeCone();
            ringLayer = Box("Rings", view, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dotLayer = Box("Dots", view, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            arrowRT = Box("Me", view, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15f, 19f));
            Img(arrowRT, Me).sprite = MakeArrow();
            labelLayer = Box("Labels", view, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            // north on the rim (С = север)
            northMark = Box("N", view, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f));
            var nt = northMark.gameObject.AddComponent<Text>();
            nt.font = font; nt.fontSize = 15; nt.fontStyle = FontStyle.Bold; nt.alignment = TextAnchor.MiddleCenter; nt.text = "С";
            nt.color = new Color(1f, 1f, 1f, 0.8f); nt.raycastTarget = false;
            var no = northMark.gameObject.AddComponent<Outline>(); no.effectColor = new Color(0f, 0f, 0f, 0.8f);
        }

        static RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        Image Img(RectTransform rt, Color c)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.sprite = white; i.color = c; i.raycastTarget = false;
            return i;
        }

        static Sprite MakeDisc(bool ring)
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                    Color c;
                    if (ring)
                    {
                        // thin line with a faint fill: a zone
                        float line = Mathf.Clamp01(1f - Mathf.Abs(d - 0.93f) * 22f);
                        c = new Color(1f, 1f, 1f, Mathf.Max(line, d < 0.93f ? 0.16f : 0f));
                    }
                    else
                    {
                        // dot with a dark rim
                        float g = d > 0.7f ? 0.05f : 1f;
                        c = new Color(g, g, g, Mathf.Clamp01((1f - d) * 10f));
                    }
                    px[y * N + x] = c;
                }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeArrow()
        {
            const int W = 30, H = 38;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // a chevron: pointed at the top, notched at the back; dark outline round a light body
                    float u = Mathf.Abs((x + 0.5f) / W - 0.5f), v = (y + 0.5f) / H;
                    float outer = Mathf.Min((1f - v) * 0.5f - u, v - 0.02f - u * 0.7f);
                    float inner = Mathf.Min((1f - v) * 0.5f - u - 0.09f, v - 0.17f - u * 0.7f);
                    px[y * W + x] = inner > 0f ? Color.white : outer > 0f ? new Color(0.1f, 0.08f, 0.02f) : new Color(0f, 0f, 0f, 0f);
                }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
        }

        static Sprite MakeCone()
        {
            const int W = 96, H = 56;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = (x + 0.5f) - W * 0.5f, dy = y + 0.5f;
                    float ang = Mathf.Abs(Mathf.Atan2(dx, dy)) * Mathf.Rad2Deg; // 0 = straight up
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / H;
                    float a = ang < 44f ? Mathf.Clamp01((44f - ang) / 6f) * Mathf.Clamp01((1f - r) * 2.2f) : 0f;
                    px[y * W + x] = new Color(1f, 1f, 1f, a);
                }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0f));
        }

        // ------------------------------------------------------------------ plan

        void BuildPlan()
        {
            var tri = NavMesh.CalculateTriangulation();
            var v = tri.vertices;
            var idx = tri.indices;
            var sps = FindObjectsByType<SpawnPoint>();

            // the ground floor: the lower spawns (most fights happen there), else the lower part of the NavMesh
            var hs = new List<float>();
            foreach (var s in sps) hs.Add(s.transform.position.y);
            if (hs.Count == 0 && v != null) for (int i = 0; i < v.Length; i += 7) hs.Add(v[i].y);
            hs.Sort();
            floorY = hs.Count > 0 ? hs[hs.Count / 5] : 0f;
            float top = floorY + CutAbove;

            // area: the NavMesh under the cut, inside the map's "Bounds" walls when it has them
            Rect r = default;
            bool any = false;
            if (v != null && idx != null)
                for (int i = 0; i < idx.Length; i++)
                {
                    var p = v[idx[i]];
                    if (p.y > top + 1f) continue;
                    if (!any) { r = new Rect(p.x, p.z, 0f, 0f); any = true; }
                    else r = Rect.MinMaxRect(Mathf.Min(r.xMin, p.x), Mathf.Min(r.yMin, p.z), Mathf.Max(r.xMax, p.x), Mathf.Max(r.yMax, p.z));
                }
            Rect walls = BoundsRect();
            if (walls.width > 1f)
            {
                if (!any) { r = walls; any = true; }
                else
                {
                    var c = Rect.MinMaxRect(Mathf.Max(r.xMin, walls.xMin - 3f), Mathf.Max(r.yMin, walls.yMin - 3f), Mathf.Min(r.xMax, walls.xMax + 3f), Mathf.Min(r.yMax, walls.yMax + 3f));
                    if (c.width > 10f && c.height > 10f) r = c;
                }
            }
            if (!any)
            {
                if (sps.Length == 0) r = new Rect(-50f, -50f, 100f, 100f);
                else
                {
                    r = new Rect(sps[0].transform.position.x, sps[0].transform.position.z, 0f, 0f);
                    foreach (var s in sps)
                    {
                        var p = s.transform.position;
                        r = Rect.MinMaxRect(Mathf.Min(r.xMin, p.x), Mathf.Min(r.yMin, p.z), Mathf.Max(r.xMax, p.x), Mathf.Max(r.yMax, p.z));
                    }
                }
            }
            float span = Mathf.Max(r.width, r.height) + 12f;
            world = new Rect(r.center.x - span * 0.5f, r.center.y - span * 0.5f, span, span);
            // bigger maps show more ground in the window
            pxPerM = ViewSize / Mathf.Clamp(span * 0.42f, 56f, 96f);
            mapRT.sizeDelta = new Vector2(span * pxPerM, span * pxPerM);

            if (v == null || idx == null || idx.Length < 3) return; // no NavMesh: markers on a blank radar
            int n = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(span / 0.14f)), 256, 1024);
            var keep = Reachable(v, idx, sps, top);
            plan = Rasterize(v, idx, keep, world, n, floorY, top);
            mapImage.texture = plan;
            mapImage.enabled = true;
        }

        /// <summary>The play area from the map's "Bounds" walls (their centres), or an empty rect.</summary>
        static Rect BoundsRect()
        {
            var b = GameObject.Find("Bounds");
            if (!b) return default;
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            int n = 0;
            foreach (var bc in b.GetComponentsInChildren<BoxCollider>())
            {
                if (bc.name == "Floor" || bc.name == "Ceiling") continue;
                var c = bc.bounds.center;
                x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); z0 = Mathf.Min(z0, c.z); z1 = Mathf.Max(z1, c.z);
                n++;
            }
            return n >= 2 && x1 > x0 && z1 > z0 ? Rect.MinMaxRect(x0, z0, x1, z1) : default;
        }

        /// <summary>
        /// Which NavMesh triangles a soldier can actually get to. The bake also covers islands nobody reaches — container
        /// tops, shelves, low roofs — that would read as floor on the plan. Triangles sharing a corner are grouped
        /// (corners welded on a 5 cm grid); a group then counts when the NavMesh finds a full path to it from a spawn. The
        /// path check also rejoins pieces the grouping splits at tile borders, where corners don't line up.
        /// </summary>
        static bool[] Reachable(Vector3[] v, int[] idx, SpawnPoint[] sps, float top)
        {
            int T = idx.Length / 3;
            var keep = new bool[T];
            var weld = new Dictionary<Vector3Int, int>();
            var parent = new int[v.Length];
            var vid = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(v[i].x * 20f), Mathf.RoundToInt(v[i].y * 5f), Mathf.RoundToInt(v[i].z * 20f));
                if (!weld.TryGetValue(k, out int id)) { id = i; weld[k] = i; }
                vid[i] = id;
                parent[i] = i;
            }
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int t = 0; t < T; t++)
            {
                int a = Find(vid[idx[t * 3]]), b = Find(vid[idx[t * 3 + 1]]), c = Find(vid[idx[t * 3 + 2]]);
                parent[b] = a;
                parent[Find(c)] = Find(a);
            }
            // per group: area and its biggest triangle (the point the path goes to)
            var area = new Dictionary<int, float>();
            var best = new Dictionary<int, int>();
            var bestA = new Dictionary<int, float>();
            var root = new int[T];
            for (int t = 0; t < T; t++)
            {
                Vector3 A = v[idx[t * 3]], B = v[idx[t * 3 + 1]], C = v[idx[t * 3 + 2]];
                int r = root[t] = Find(vid[idx[t * 3]]);
                if ((A.y + B.y + C.y) / 3f > top) continue;
                float ar = Mathf.Abs((B.x - A.x) * (C.z - A.z) - (B.z - A.z) * (C.x - A.x)) * 0.5f;
                area.TryGetValue(r, out float sum);
                area[r] = sum + ar;
                if (!bestA.TryGetValue(r, out float ba) || ar > ba) { bestA[r] = ar; best[r] = t; }
            }
            // from the first spawn that sits on the NavMesh
            Vector3 from = Vector3.zero;
            bool ok = false;
            foreach (var s in sps)
                if (NavMesh.SamplePosition(s.transform.position, out var hit, 2f, NavMesh.AllAreas)) { from = hit.position; ok = true; break; }
            var good = new HashSet<int>();
            if (!ok)
            {
                foreach (var r in area.Keys) good.Add(r); // nothing to measure from: draw it all
            }
            else
            {
                var groups = new List<int>(area.Keys);
                groups.Sort((x, y) => area[y].CompareTo(area[x]));
                var path = new NavMeshPath();
                int tests = 0;
                foreach (var r in groups)
                {
                    if (area[r] < 0.5f || tests++ > 800) break;
                    int t = best[r];
                    Vector3 to = (v[idx[t * 3]] + v[idx[t * 3 + 1]] + v[idx[t * 3 + 2]]) / 3f;
                    if (NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                        good.Add(r);
                }
            }
            for (int t = 0; t < T; t++) keep[t] = good.Contains(root[t]);
            return keep;
        }

        /// <summary>
        /// Software raster of the NavMesh from above: per pixel the highest walkable surface under <paramref name="top"/>,
        /// shaded by height, a bright rim along the walkable edge, a dark line where the floor steps (ledges, catwalks).
        /// </summary>
        static Texture2D Rasterize(Vector3[] v, int[] idx, bool[] keep, Rect area, int N, float floor, float top)
        {
            var H = new float[N * N];
            for (int i = 0; i < H.Length; i++) H[i] = float.NegativeInfinity;
            float inv = N / area.width, x0 = area.xMin, z0 = area.yMin;

            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                if (!keep[t / 3]) continue;
                Vector3 A = v[idx[t]], B = v[idx[t + 1]], C = v[idx[t + 2]];
                if ((A.y + B.y + C.y) * (1f / 3f) > top) continue;
                float ax = (A.x - x0) * inv, ay = (A.z - z0) * inv;
                float bx = (B.x - x0) * inv, by = (B.z - z0) * inv;
                float cx = (C.x - x0) * inv, cy = (C.z - z0) * inv;
                float area2 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (Mathf.Abs(area2) < 1e-7f) continue;
                float ia = 1f / area2;
                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ax, Mathf.Min(bx, cx))));
                int maxX = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(ax, Mathf.Max(bx, cx))));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ay, Mathf.Min(by, cy))));
                int maxY = Mathf.Min(N - 1, Mathf.CeilToInt(Mathf.Max(ay, Mathf.Max(by, cy))));
                for (int y = minY; y <= maxY; y++)
                {
                    float py = y + 0.5f;
                    int row = y * N;
                    for (int x = minX; x <= maxX; x++)
                    {
                        float px = x + 0.5f;
                        float wa = ((cx - bx) * (py - by) - (cy - by) * (px - bx)) * ia;
                        float wb = ((ax - cx) * (py - cy) - (ay - cy) * (px - cx)) * ia;
                        float wc = 1f - wa - wb;
                        if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                        float h = wa * A.y + wb * B.y + wc * C.y;
                        if (h <= top + 0.3f && h > H[row + x]) H[row + x] = h;
                    }
                }
            }

            // "near the edge": the empty mask grown by a rim width (separable max filter)
            int R = Mathf.Clamp(Mathf.RoundToInt(0.24f * inv), 1, 3);
            var empty = new bool[N * N];
            for (int i = 0; i < H.Length; i++) empty[i] = float.IsNegativeInfinity(H[i]);
            var tmp = new bool[N * N];
            var near = new bool[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool e = false;
                    for (int k = -R; k <= R && !e; k++) { int xx = x + k; e = xx < 0 || xx >= N || empty[y * N + xx]; }
                    tmp[y * N + x] = e;
                }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    bool e = false;
                    for (int k = -R; k <= R && !e; k++) { int yy = y + k; e = yy < 0 || yy >= N || tmp[yy * N + x]; }
                    near[y * N + x] = e;
                }

            var px32 = new Color32[N * N];
            Color32 none = new Color(Rim.r, Rim.g, Rim.b, 0f);
            Color32 rim = Rim, ledge = Ledge;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x;
                    float h = H[i];
                    if (float.IsNegativeInfinity(h)) { px32[i] = none; continue; }
                    if (near[i]) { px32[i] = rim; continue; }
                    // a step down to a neighbour: ledge line on the higher side
                    bool step = (x + 1 < N && !empty[i + 1] && h - H[i + 1] > 0.55f)
                             || (x > 0 && !empty[i - 1] && h - H[i - 1] > 0.55f)
                             || (y + 1 < N && !empty[i + N] && h - H[i + N] > 0.55f)
                             || (y > 0 && !empty[i - N] && h - H[i - N] > 0.55f);
                    if (step) { px32[i] = ledge; continue; }
                    px32[i] = Color.Lerp(FloorLow, FloorHigh, Mathf.InverseLerp(floor - 1.5f, floor + CutAbove, h));
                }

            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "MiniMapPlan", filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px32);
            tex.Apply(true, true);
            return tex;
        }

        // ------------------------------------------------------------------ per frame

        Vector2 PlanPos(Vector3 w) => new Vector2((w.x - world.center.x) * pxPerM, (w.z - world.center.y) * pxPerM);

        static Vector2 Rot(Vector2 p, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(p.x * c - p.y * s, p.x * s + p.y * c);
        }

        void LateUpdate()
        {
            var ph = PlayerHealth.Local;
            var cam = Camera.main;
            bool show = ready && cam && ph && GameSettings.MiniMapOn && !SkyArena.Active;
            group.alpha = Mathf.MoveTowards(group.alpha, show ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            if (!ready || !cam || !ph || group.alpha <= 0f) return;

            // centred on the view (the drone's camera when flying one), turned so forward is up
            Vector3 eye = cam.transform.position;
            float yaw = cam.transform.eulerAngles.y;
            float turn = GameSettings.MiniMapRotate ? yaw : 0f;
            Vector2 me = PlanPos(eye);
            rotator.localEulerAngles = new Vector3(0f, 0f, turn);
            mapRT.anchoredPosition = -me;
            coneRT.localEulerAngles = arrowRT.localEulerAngles = new Vector3(0f, 0f, turn - yaw);
            float half = ViewSize * 0.5f;
            northMark.anchoredPosition = Rot(new Vector2(0f, half - 12f), turn);
            if (northMark.gameObject.activeSelf != GameSettings.MiniMapRotate) northMark.gameObject.SetActive(GameSettings.MiniMapRotate);

            markers.Clear();
            var self = ph.GetComponent<Combatant>();
            bool teams = Teams.Active && self && self.team >= 0;
            float recon = self && ReconVision.Left > 0f && ReconVision.Viewer == self ? ReconVision.Range : 0f;
            if (self && (self.transform.position - eye).sqrMagnitude > 16f)
                markers.Add(new Marker { pos = self.transform.position, color = Me, size = 10f });
            foreach (var c in Combatant.All)
            {
                if (!c || c == self) continue;
                if (teams && Teams.Friendly(self, c))
                {
                    if (c.alive) markers.Add(new Marker { pos = c.transform.position, color = Teams.Colors[0], size = 11f });
                    else markers.Add(new Marker { pos = c.transform.position, label = "+", labelColor = Down, pulse = true });
                    continue;
                }
                if (!c.alive) continue;
                float since = Time.time - c.lastShotTime;
                bool spotted = recon > 0f && (c.transform.position - self.transform.position).sqrMagnitude < recon * recon;
                if (since >= Shot && !spotted) continue;
                var col = Teams.Colors[1];
                if (!spotted) col.a = 1f - (since / Shot) * (since / Shot);
                markers.Add(new Marker { pos = c.transform.position, color = col, size = 11f });
            }
            if (DroneMode.Active)
                foreach (var rv in DroneMode.Reveals)
                    if (rv != null && rv.owner != self && Time.time < rv.until)
                        markers.Add(new Marker { pos = rv.center, color = Teams.Colors[1], ring = rv.radius, label = "?", labelColor = Teams.Colors[1], pulse = true });
            try { CollectMarkers?.Invoke(markers); }
            catch (Exception e) { Debug.LogException(e); }

            int nr = 0, nd = 0, nl = 0;
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 8f);
            float lim = half - 11f;
            foreach (var m in markers)
            {
                Vector2 at = Rot(PlanPos(m.pos) - me, turn);
                Vector2 p = at;
                float far = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
                if (far > lim && m.edge) p *= lim / far;
                float a = m.pulse ? pulse : 1f;
                if (m.ring > 0f)
                {
                    var img = Pooled(rings, ringLayer, nr++, ringSprite);
                    img.rectTransform.anchoredPosition = at;
                    img.rectTransform.sizeDelta = Vector2.one * (m.ring * 2f * pxPerM);
                    var col = m.color; col.a *= 0.85f * a;
                    img.color = col;
                }
                if (m.size > 0f)
                {
                    var img = Pooled(dots, dotLayer, nd++, circle);
                    img.rectTransform.anchoredPosition = p;
                    img.rectTransform.sizeDelta = Vector2.one * m.size;
                    var col = m.color; col.a *= a;
                    img.color = col;
                }
                if (!string.IsNullOrEmpty(m.label))
                {
                    var t = Label(nl++);
                    t.rectTransform.anchoredPosition = p;
                    t.text = m.label;
                    var col = m.labelColor.a > 0f ? m.labelColor : Color.white;
                    col.a *= a;
                    t.color = col;
                    t.fontSize = m.size >= 16f ? Mathf.RoundToInt(m.size * 0.62f) : 17;
                }
            }
            Hide(rings, nr); Hide(dots, nd);
            for (int i = nl; i < labels.Count; i++) if (labels[i].enabled) labels[i].enabled = false;
        }

        static void Hide(List<Image> pool, int from)
        {
            for (int i = from; i < pool.Count; i++) if (pool[i].enabled) pool[i].enabled = false;
        }

        static Image Pooled(List<Image> pool, RectTransform parent, int i, Sprite sprite)
        {
            while (pool.Count <= i)
            {
                var rt = Box("M", parent, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = sprite; img.raycastTarget = false;
                pool.Add(img);
            }
            pool[i].enabled = true;
            return pool[i];
        }

        Text Label(int i)
        {
            while (labels.Count <= i)
            {
                var rt = Box("L", labelLayer, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, 24f));
                var t = rt.gameObject.AddComponent<Text>();
                t.font = font; t.fontStyle = FontStyle.Bold; t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
                var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0f, 0f, 0f, 0.9f); o.effectDistance = new Vector2(1.2f, -1.2f);
                labels.Add(t);
            }
            labels[i].enabled = true;
            return labels[i];
        }
    }
}
