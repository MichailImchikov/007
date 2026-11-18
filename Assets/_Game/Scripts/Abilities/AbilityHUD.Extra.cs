using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>Full-screen effects of enemy area abilities: screamer face, blue screen of death (tic-tac-toe has its own TicTacToeHUD).</summary>
    public partial class AbilityHUD
    {
        Image screamImg;
        RectTransform screamRt;
        CanvasGroup bsodGroup;
        Text bsodPercent;

        void BuildExtra()
        {
            // ---- screamer
            screamRt = Full("Screamer");
            var tex = ScreamFace.Make(256, 1f, false);
            screamImg = Img(screamRt, new Color(1f, 1f, 1f, 0f), Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f)));
            screamImg.preserveAspect = false;

            // ---- blue screen (Windows 10 style parody)
            var b = Full("BSOD");
            bsodGroup = b.gameObject.AddComponent<CanvasGroup>();
            Img(b, new Color(0f, 0.47f, 0.84f, 1f));
            TopLeft(Txt(TL(b, "Sad", 230f, -150f, 600f, 260f), 220, TextAnchor.UpperLeft, Color.white), false);
            var main = Txt(TL(b, "Main", 236f, -440f, 1350f, 140f), 38, TextAnchor.UpperLeft, Color.white);
            main.horizontalOverflow = HorizontalWrapMode.Wrap;
            main.text = "На вашем ПК возникла проблема, и его необходимо перезагрузить. Мы лишь собираем некоторые сведения об ошибке, а затем будет автоматически выполнена перезагрузка.";
            bsodPercent = Txt(TL(b, "Percent", 236f, -600f, 900f, 50f), 38, TextAnchor.UpperLeft, Color.white);
            var qrTex = QrTexture(25);
            var qr = TL(b, "QR", 236f, -700f, 150f, 150f);
            Img(qr, Color.white, Sprite.Create(qrTex, new Rect(0, 0, qrTex.width, qrTex.height), new Vector2(0.5f, 0.5f)));
            var side = Txt(TL(b, "Side", 420f, -700f, 1200f, 160f), 22, TextAnchor.UpperLeft, Color.white);
            side.horizontalOverflow = HorizontalWrapMode.Wrap;
            side.text = "Дополнительные сведения об этой ошибке и возможных исправлениях вы не найдёте нигде.\n\nЕсли вы позвоните в службу поддержки, назовите им этот код:\nКод остановки: CRITICAL_ABILITY_HIT_BY_ENEMY";
            foreach (var sh in b.GetComponentsInChildren<Shadow>()) sh.enabled = false;
            bsodGroup.alpha = 0f;

        }

        RectTransform TL(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        static void TopLeft(Text t, bool bold) { t.text = ":("; if (bold) t.fontStyle = FontStyle.Bold; }

        void UpdateExtra(float dt)
        {
            // ---- screamer: zooms in and shakes
            float s = user ? user.ScreamT : 0f;
            if (s > 0f)
            {
                float age = 1.7f - s;
                float a = Mathf.Clamp01(age / 0.05f) * Mathf.Clamp01(s / 0.25f);
                screamImg.color = new Color(1f, 1f, 1f, a);
                float z = Mathf.Lerp(0.7f, 1.25f, Mathf.Clamp01(age / 0.25f)) + Mathf.Sin(age * 60f) * 0.02f;
                screamRt.localScale = Vector3.one * z;
                screamRt.anchoredPosition = Random.insideUnitCircle * 25f;
            }
            else if (screamImg.color.a > 0f) screamImg.color = new Color(1f, 1f, 1f, 0f);

            // ---- blue screen
            float bl = user ? user.BSODLeft : 0f;
            bsodGroup.alpha = bl > 0f ? 1f : 0f;
            if (bl > 0f)
            {
                float p = 1f - bl / Mathf.Max(0.01f, user.BSODDuration);
                bsodPercent.text = Mathf.FloorToInt(Mathf.Pow(p, 0.6f) * 100f) + "% завершено";
            }

        }

        // ------------------------------------------------------------------ textures

        static Texture2D QrTexture(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var r = new System.Random(7);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool finder = (x < 7 && y < 7) || (x >= n - 7 && y < 7) || (x < 7 && y >= n - 7);
                    bool on;
                    if (finder)
                    {
                        int fx = x < 7 ? x : x - (n - 7), fy = y < 7 ? y : y - (n - 7);
                        on = fx == 0 || fy == 0 || fx == 6 || fy == 6 || (fx >= 2 && fx <= 4 && fy >= 2 && fy <= 4);
                    }
                    else on = r.NextDouble() < 0.5;
                    t.SetPixel(x, y, on ? new Color(0f, 0.47f, 0.84f) : Color.white);
                }
            t.Apply();
            return t;
        }
    }
}
