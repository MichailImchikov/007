using UnityEngine;

namespace FPS
{
    /// <summary>
    /// The procedural screaming face (pale skin, hollow eyes, gaping mouth with teeth) shared by the Screamer ability
    /// and the talking head above the disco. <c>mouth</c> 0 = lips almost shut, 1 = the full scream.
    /// </summary>
    public static class ScreamFace
    {
        public static Texture2D Make(int N, float mouth, bool transparentBackground)
        {
            mouth = Mathf.Clamp01(mouth);
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ScreamFace" };
            var px = new Color32[N * N];
            float Ell(float x, float y, float cx, float cy, float rx, float ry) { float dx = (x - cx) / rx, dy = (y - cy) / ry; return dx * dx + dy * dy; }
            // the jaw drops as the mouth opens: at 1 it is exactly the ability's face
            float my = Mathf.Lerp(0.31f, 0.25f, mouth), mry = Mathf.Lerp(0.025f, 0.2f, mouth), mrx = Mathf.Lerp(0.12f, 0.17f, mouth);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float n = Mathf.PerlinNoise(u * 9f, v * 9f);
                    Color c = new Color(0.02f, 0.02f, 0.02f) * (0.5f + n);
                    float face = Ell(u, v, 0.5f, 0.5f, 0.42f, 0.5f);
                    if (transparentBackground) c.a = 0f;
                    if (face < 1f)
                    {
                        float shade = Mathf.Lerp(0.95f, 0.45f, face);
                        c = new Color(0.78f, 0.8f, 0.72f) * shade * (0.8f + 0.3f * n);
                        c.a = 1f;
                        // veins / dirt
                        if (Mathf.PerlinNoise(u * 30f, v * 30f) > 0.72f) c *= 0.7f;
                        float eL = Ell(u, v, 0.33f, 0.64f, 0.1f, 0.13f), eR = Ell(u, v, 0.67f, 0.64f, 0.1f, 0.13f);
                        if (eL < 1.6f || eR < 1.6f) c *= 0.55f; // dark sockets
                        if (eL < 1f || eR < 1f)
                        {
                            c = Color.black;
                            if (Ell(u, v, 0.33f, 0.62f, 0.022f, 0.026f) < 1f || Ell(u, v, 0.67f, 0.62f, 0.022f, 0.026f) < 1f) c = new Color(1f, 0.85f, 0.7f);
                        }
                        float m = Ell(u, v, 0.5f, my, mrx, mry);
                        if (m < 1.25f) c *= 0.5f;
                        if (m < 1f)
                        {
                            c = new Color(0.12f, 0f, 0f) * (1f - m * 0.7f);
                            // jagged teeth on top and bottom
                            float tooth = Mathf.Abs(Mathf.Repeat(u * 28f, 1f) - 0.5f) * 2f;
                            float th = 0.06f * Mathf.Lerp(0.35f, 1f, mouth);
                            if (v > my + mry * 0.82f - tooth * th) c = new Color(0.85f, 0.82f, 0.65f);
                            if (v < my - mry * 0.8f + tooth * th) c = new Color(0.8f, 0.77f, 0.6f);
                        }
                        // blood streaks under the eyes
                        if ((Mathf.Abs(u - 0.33f) < 0.012f || Mathf.Abs(u - 0.67f) < 0.012f) && v < 0.55f && v > 0.32f + Mathf.PerlinNoise(u * 50f, 1f) * 0.1f)
                            c = new Color(0.45f, 0f, 0f);
                        c.a = 1f;
                    }
                    px[y * N + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
