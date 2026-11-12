using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>Full-screen effects of enemy area abilities: screamer face, blue screen of death, minesweeper 4×4.</summary>
    public partial class AbilityHUD
    {
        Image screamImg;
        RectTransform screamRt;
        CanvasGroup bsodGroup;
        Text bsodPercent;

        // minesweeper
        const int MS = 4, Mines = 3;
        CanvasGroup msGroup;
        RectTransform[] cellRt = new RectTransform[MS * MS];
        Image[] cellImg = new Image[MS * MS];
        Text[] cellTxt = new Text[MS * MS];
        Text msStatus;
        readonly bool[] mine = new bool[MS * MS], open = new bool[MS * MS], flag = new bool[MS * MS];
        bool msActive, msGenerated, msDone, msWin;
        float msEndT;
        AbilityUser msUser;

        void BuildExtra()
        {
            // ---- screamer
            screamRt = Full("Screamer");
            var tex = ScreamTexture(256);
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

            // ---- minesweeper
            var ms = Rect("Minesweeper", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(460f, 560f));
            msGroup = ms.gameObject.AddComponent<CanvasGroup>();
            Img(ms, new Color(0.12f, 0.13f, 0.15f, 0.95f));
            var title = Txt(Rect("Title", ms, new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(440f, 40f)), 30, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.3f), true);
            title.text = "САПЁР";
            var sub = Txt(Rect("Sub", ms, new Vector2(0.5f, 1f), new Vector2(0f, -72f), new Vector2(440f, 30f)), 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
            sub.text = "Решите, чтобы снова двигаться. Мин: " + Mines;
            msStatus = Txt(Rect("Status", ms, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(440f, 30f)), 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.75f));
            const float cs = 92f, gap = 6f;
            for (int y = 0; y < MS; y++)
                for (int x = 0; x < MS; x++)
                {
                    int i = y * MS + x;
                    var rt = Rect("Cell" + i, ms, new Vector2(0.5f, 0.5f), new Vector2((x - 1.5f) * (cs + gap), (1.5f - y) * (cs + gap) - 10f), new Vector2(cs, cs));
                    cellRt[i] = rt;
                    cellImg[i] = Img(rt, Color.gray);
                    cellTxt[i] = Txt(Rect("T", rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cs, cs)), 50, TextAnchor.MiddleCenter, Color.white, true);
                }
            msGroup.alpha = 0f;
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

            UpdateMinesweeper(dt);
        }

        // ------------------------------------------------------------------ minesweeper

        public void OpenMinesweeper(AbilityUser u)
        {
            msUser = u;
            msActive = true;
            msGenerated = false;
            msDone = false;
            for (int i = 0; i < mine.Length; i++) { mine[i] = open[i] = flag[i] = false; }
            msStatus.text = "ЛКМ — открыть клетку,  ПКМ — флажок";
            RefreshCells();
            msGroup.alpha = 1f;
        }

        public void CloseMinesweeper()
        {
            msActive = false;
            msGroup.alpha = 0f;
        }

        void UpdateMinesweeper(float dt)
        {
            if (!msActive) return;
            if (!msUser || !msUser.Minesweeping) { CloseMinesweeper(); return; }
            if (msDone)
            {
                msEndT -= Time.unscaledDeltaTime;
                if (msEndT <= 0f)
                {
                    CloseMinesweeper();
                    msUser.OnMineResult(msWin);
                }
                return;
            }
            var m = Mouse.current;
            if (m == null || PauseMenu.IsOpen) return;
            bool l = m.leftButton.wasPressedThisFrame, r = m.rightButton.wasPressedThisFrame;
            if (!l && !r) return;
            Vector2 pos = m.position.ReadValue();
            for (int i = 0; i < cellRt.Length; i++)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(cellRt[i], pos, null)) continue;
                if (open[i]) break;
                if (r) { flag[i] = !flag[i]; ProcAudio.Play(Sfx.Click, 0.5f); }
                else if (!flag[i]) Reveal(i);
                RefreshCells();
                break;
            }
        }

        void Reveal(int i)
        {
            if (!msGenerated) Generate(i);
            ProcAudio.Play(Sfx.Click, 0.6f);
            if (mine[i])
            {
                for (int k = 0; k < mine.Length; k++) if (mine[k]) open[k] = true;
                msDone = true; msWin = false; msEndT = 0.7f;
                msStatus.text = "<color=#ff5040>БУМ!</color>";
                msStatus.supportRichText = true;
                ProcAudio.Play(Sfx.ErrorTone, 0.8f);
                return;
            }
            Flood(i);
            int closed = 0;
            for (int k = 0; k < open.Length; k++) if (!open[k]) closed++;
            if (closed == Mines)
            {
                msDone = true; msWin = true; msEndT = 0.45f;
                msStatus.text = "<color=#60ff70>Готово!</color>";
                msStatus.supportRichText = true;
            }
        }

        void Generate(int safe)
        {
            msGenerated = true;
            int sx = safe % MS, sy = safe / MS;
            int placed = 0, guard = 0;
            while (placed < Mines && guard++ < 500)
            {
                int k = Random.Range(0, MS * MS);
                int kx = k % MS, ky = k / MS;
                if (mine[k] || (Mathf.Abs(kx - sx) <= 1 && Mathf.Abs(ky - sy) <= 1)) continue; // first click opens an area
                mine[k] = true;
                placed++;
            }
        }

        int Adjacent(int i)
        {
            int x = i % MS, y = i / MS, n = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < MS && ny < MS && mine[ny * MS + nx]) n++;
                }
            return n;
        }

        void Flood(int i)
        {
            if (open[i] || mine[i]) return;
            open[i] = true;
            flag[i] = false;
            if (Adjacent(i) > 0) return;
            int x = i % MS, y = i / MS;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx >= 0 && ny >= 0 && nx < MS && ny < MS) Flood(ny * MS + nx);
                }
        }

        static readonly Color[] NumColors =
        {
            Color.white, new Color(0.35f, 0.55f, 1f), new Color(0.3f, 0.85f, 0.35f), new Color(1f, 0.35f, 0.3f),
            new Color(0.6f, 0.4f, 1f), new Color(1f, 0.6f, 0.2f), Color.cyan, Color.white, Color.gray
        };

        void RefreshCells()
        {
            for (int i = 0; i < cellImg.Length; i++)
            {
                if (open[i])
                {
                    if (mine[i]) { cellImg[i].color = new Color(0.75f, 0.12f, 0.1f); cellTxt[i].text = "●"; cellTxt[i].color = Color.black; }
                    else
                    {
                        int n = Adjacent(i);
                        cellImg[i].color = new Color(0.32f, 0.34f, 0.38f);
                        cellTxt[i].text = n > 0 ? n.ToString() : "";
                        cellTxt[i].color = NumColors[n];
                    }
                }
                else
                {
                    cellImg[i].color = new Color(0.68f, 0.7f, 0.74f);
                    cellTxt[i].text = flag[i] ? "F" : "";
                    cellTxt[i].color = new Color(0.9f, 0.15f, 0.1f);
                }
            }
        }

        // ------------------------------------------------------------------ textures

        /// <summary>Procedural screaming face: pale skin, hollow eyes, gaping mouth with teeth.</summary>
        static Texture2D ScreamTexture(int N)
        {
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            float Ell(float x, float y, float cx, float cy, float rx, float ry) { float dx = (x - cx) / rx, dy = (y - cy) / ry; return dx * dx + dy * dy; }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N, v = y / (float)N;
                    float n = Mathf.PerlinNoise(u * 9f, v * 9f);
                    Color c = new Color(0.02f, 0.02f, 0.02f) * (0.5f + n);
                    float face = Ell(u, v, 0.5f, 0.5f, 0.42f, 0.5f);
                    if (face < 1f)
                    {
                        float shade = Mathf.Lerp(0.95f, 0.45f, face);
                        c = new Color(0.78f, 0.8f, 0.72f) * shade * (0.8f + 0.3f * n);
                        // veins / dirt
                        if (Mathf.PerlinNoise(u * 30f, v * 30f) > 0.72f) c *= 0.7f;
                        float eL = Ell(u, v, 0.33f, 0.64f, 0.1f, 0.13f), eR = Ell(u, v, 0.67f, 0.64f, 0.1f, 0.13f);
                        if (eL < 1.6f || eR < 1.6f) c *= 0.55f; // dark sockets
                        if (eL < 1f || eR < 1f)
                        {
                            c = Color.black;
                            if (Ell(u, v, 0.33f, 0.62f, 0.022f, 0.026f) < 1f || Ell(u, v, 0.67f, 0.62f, 0.022f, 0.026f) < 1f) c = new Color(1f, 0.85f, 0.7f);
                        }
                        float mouth = Ell(u, v, 0.5f, 0.25f, 0.17f, 0.2f);
                        if (mouth < 1.25f) c *= 0.5f;
                        if (mouth < 1f)
                        {
                            c = new Color(0.12f, 0f, 0f) * (1f - mouth * 0.7f);
                            // jagged teeth on top and bottom
                            float tooth = Mathf.Abs(Mathf.Repeat(u * 28f, 1f) - 0.5f) * 2f;
                            if (v > 0.25f + 0.2f * 0.82f - tooth * 0.06f) c = new Color(0.85f, 0.82f, 0.65f);
                            if (v < 0.25f - 0.2f * 0.8f + tooth * 0.06f) c = new Color(0.8f, 0.77f, 0.6f);
                        }
                        // blood streaks under the eyes
                        if ((Mathf.Abs(u - 0.33f) < 0.012f || Mathf.Abs(u - 0.67f) < 0.012f) && v < 0.55f && v > 0.32f + Mathf.PerlinNoise(u * 50f, 1f) * 0.1f)
                            c = new Color(0.45f, 0f, 0f);
                    }
                    px[y * N + x] = c;
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

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
