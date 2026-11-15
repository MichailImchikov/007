using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Procedural comic-book art for the streak effects (made once, no texture assets): the starburst behind a kill word,
    /// the halftone vignette, speed lines, the inked panel border, villain eyes, an evil grin and soft aura wisps.
    /// </summary>
    public static class ComicArt
    {
        static Texture2D burst, halftone, speedLines, border, eye, grin, wisp, glow, sparkle, streak;

        public static Texture2D Burst => burst ? burst : burst = MakeBurst(256);
        public static Texture2D Halftone => halftone ? halftone : halftone = MakeHalftone(512);
        public static Texture2D SpeedLines => speedLines ? speedLines : speedLines = MakeSpeedLines(512);
        public static Texture2D Border => border ? border : border = MakeBorder(512);
        public static Texture2D Eye => eye ? eye : eye = MakeEye(128);
        public static Texture2D Grin => grin ? grin : grin = MakeGrin(256, 128);
        public static Texture2D Wisp => wisp ? wisp : wisp = MakeWisp(64);
        public static Texture2D Glow => glow ? glow : glow = MakeGlow(64);
        public static Texture2D Sparkle => sparkle ? sparkle : sparkle = MakeSparkle(64);
        public static Texture2D Streak => streak ? streak : streak = MakeStreak(8, 64);

        static Texture2D New(int w, int h) =>
            new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };

        static float AA(float d, float w = 1.2f) => Mathf.Clamp01(0.5f - d / w);

        /// <summary>Jagged starburst: white fill (tinted by the image color), thick black ink outline, halftone shading.</summary>
        static Texture2D MakeBurst(int s)
        {
            var t = New(s, s);
            var px = new Color32[s * s];
            const int spikes = 13;
            var rnd = new System.Random(7);
            var radii = new float[spikes * 2];
            for (int i = 0; i < radii.Length; i++) radii[i] = i % 2 == 0 ? 0.93f - (float)rnd.NextDouble() * 0.12f : 0.58f + (float)rnd.NextDouble() * 0.08f;
            float half = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f;
                    float f = a * radii.Length;
                    int i0 = Mathf.FloorToInt(f) % radii.Length, i1 = (i0 + 1) % radii.Length;
                    float edge = Mathf.Lerp(radii[i0], radii[i1], f - Mathf.Floor(f));
                    float dEdge = (r - edge) * half;                         // pixels outside the star edge
                    float fill = AA(dEdge + 7f);                              // inside the ink
                    float ink = AA(dEdge) - fill;                             // the 7 px outline band
                    // halftone on the lower right: comic shading
                    float shade = Mathf.Clamp01((dx - dy) * 0.6f + 0.1f);
                    float cell = 9f;
                    float cx = (x % cell) - cell * 0.5f, cy = (y % cell) - cell * 0.5f;
                    float dot = AA(Mathf.Sqrt(cx * cx + cy * cy) - cell * 0.45f * shade);
                    float v = 1f - dot * 0.28f;
                    byte c = (byte)(255 * v);
                    byte inkV = 18;
                    float alpha = Mathf.Clamp01(fill + ink);
                    byte col = ink > fill ? inkV : c;
                    px[y * s + x] = new Color32(col, col, col, (byte)(alpha * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>Halftone dots growing toward the edges (alpha), for the screen vignette.</summary>
        static Texture2D MakeHalftone(int s)
        {
            var t = New(s, s);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[s * s];
            float cell = 14f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
                    float edge = Mathf.Clamp01((Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) * 0.6f + Mathf.Sqrt(u * u + v * v) * 0.4f - 0.66f) / 0.4f);
                    // dots on a 45° grid
                    float rx = (x + y) * 0.7071f, ry = (x - y) * 0.7071f;
                    float cx = Mathf.Repeat(rx, cell) - cell * 0.5f, cy = Mathf.Repeat(ry, cell) - cell * 0.5f;
                    float rad = cell * 0.52f * edge * edge;
                    float a = AA(Mathf.Sqrt(cx * cx + cy * cy) - rad);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>Radial action lines from the edges toward the center (alpha), for speed.</summary>
        static Texture2D MakeSpeedLines(int s)
        {
            var t = New(s, s);
            var px = new Color32[s * s];
            const int n = 70;
            var rnd = new System.Random(11);
            var start = new float[n];
            var width = new float[n];
            for (int i = 0; i < n; i++) { start[i] = 0.45f + (float)rnd.NextDouble() * 0.35f; width[i] = 0.25f + (float)rnd.NextDouble() * 0.6f; }
            float half = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = (Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) + 0.5f) * n;
                    int i = Mathf.FloorToInt(a) % n;
                    float off = Mathf.Abs(a - Mathf.Floor(a) - 0.5f) * 2f;          // 0 at the line's center
                    float wedge = width[i] * Mathf.Clamp01((r - start[i]) / 0.25f);   // lines thicken outward
                    float line = Mathf.Clamp01((wedge - off) * 4f) * Mathf.Clamp01((r - start[i]) * 6f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(line * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>Inked comic panel border: a thick, slightly wobbly black frame (alpha).</summary>
        static Texture2D MakeBorder(int s)
        {
            var t = New(s, s);
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                    float d = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float wob = 0.022f + 0.006f * Mathf.Sin((u + v) * 37f) + 0.004f * Mathf.Sin(u * 91f - v * 53f);
                    float a = Mathf.Clamp01((wob - d) * s * 0.8f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>
        /// A slanted, angry glowing eye (additive): a white-hot slit going orange at its rim inside a red glow; the top edge
        /// drops toward +u (the nose side of the left eye; the right eye is mirrored).
        /// </summary>
        static Texture2D MakeEye(int s)
        {
            var t = New(s, s);
            var px = new Color32[s * s];
            float half = s * 0.5f;
            Color glowCol = new Color(1f, 0.16f, 0.06f), rimCol = new Color(1f, 0.52f, 0.2f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
                    // an almond slit cut by a brow sloping down toward the nose: a frown
                    float ell = (u * u) / 0.78f + (v + 0.02f) * (v + 0.02f) / 0.085f;
                    float brow = v - (0.16f - u * 0.38f);
                    float core = Mathf.Clamp01((1f - ell) * 3.2f) * Mathf.Clamp01(-brow * 9f);
                    float hot = Mathf.Clamp01((1f - ell) * 1.6f - 0.3f) * Mathf.Clamp01(-brow * 6f);
                    float glowA = Mathf.Exp(-(u * u * 1.5f + v * v * 7f)) * 0.6f;
                    Color c = Color.Lerp(glowCol, Color.Lerp(rimCol, Color.white, hot), core);
                    float a = Mathf.Clamp01(Mathf.Max(core, glowA));
                    px[y * s + x] = new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>A soft round glow (alpha), white: the halo around a glowing eye.</summary>
        static Texture2D MakeGlow(int s)
        {
            var t = New(s, s);
            var px = new Color32[s * s];
            float half = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
                    float a = Mathf.Clamp01(Mathf.Exp(-(u * u + v * v) * 4.5f) - 0.012f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>A four-point comic sparkle (alpha), white: the sparks thrown off by the aura.</summary>
        static Texture2D MakeSparkle(int s)
        {
            var t = New(s, s);
            var px = new Color32[s * s];
            float half = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
                    float au = Mathf.Abs(u), av = Mathf.Abs(v);
                    float star = Mathf.Exp(-au * 13f) * Mathf.Exp(-av * 2.4f) + Mathf.Exp(-av * 13f) * Mathf.Exp(-au * 2.4f);
                    float core = Mathf.Exp(-(u * u + v * v) * 20f);
                    float a = Mathf.Clamp01(star * 0.95f + core) * Mathf.Clamp01((1f - Mathf.Max(au, av)) * 5f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>A soft line across v (alpha), the same all along u: a light trail.</summary>
        static Texture2D MakeStreak(int w, int h)
        {
            var t = New(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;
                byte a = (byte)(Mathf.Clamp01(Mathf.Exp(-v * v * 3.5f) - 0.03f) * 255);
                for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, a);
            }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>An evil grin: a wide crescent mouth with jagged white teeth, black ink outline.</summary>
        static Texture2D MakeGrin(int w, int h)
        {
            var t = New(w, h);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h * 2f - 1f;
                    // mouth: between two arcs (a wide smile curving up at the ends)
                    float top = 0.25f + 0.55f * u * u;
                    float bottom = -0.55f + 0.9f * u * u;
                    float inside = Mathf.Min(top - v, v - bottom) * h * 0.5f;      // px inside the mouth
                    float mouth = Mathf.Clamp01(inside / 1.2f + 0.5f) * Mathf.Clamp01((1f - Mathf.Abs(u)) * 12f);
                    float outline = Mathf.Clamp01((inside + 5f) / 1.2f) * Mathf.Clamp01((1.05f - Mathf.Abs(u)) * 12f) - mouth;
                    // teeth: a zigzag splitting the mouth (upper and lower fangs), dark gaps between them
                    float mid = (top + bottom) * 0.5f;
                    float tooth = Mathf.Abs(Mathf.Repeat(u * 7f, 1f) - 0.5f) * 2f;    // 0..1 saw
                    float zig = mid + (tooth - 0.5f) * (top - bottom) * 0.5f;
                    float gap = Mathf.Clamp01(1f - Mathf.Abs(v - zig) * h * 0.5f / 1.6f);
                    float teeth = mouth * (1f - gap);
                    float a = Mathf.Clamp01(mouth + outline);
                    byte c = (byte)(teeth > 0.5f ? 245 : 20);
                    if (outline > 0.5f) c = 10;
                    px[y * w + x] = new Color32(c, c, c, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }

        /// <summary>A soft flame-like wisp for the aura particles (additive).</summary>
        static Texture2D MakeWisp(int s)
        {
            var t = New(s, s);
            var px = new Color32[s * s];
            float half = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float u = (x + 0.5f - half) / half, v = (y + 0.5f - half) / half;
                    float a = Mathf.Exp(-(u * u * 3.5f + (v + 0.2f) * (v + 0.2f) * 1.6f)) * Mathf.Clamp01(1.1f - v);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            t.SetPixels32(px); t.Apply(true); return t;
        }
    }
}
