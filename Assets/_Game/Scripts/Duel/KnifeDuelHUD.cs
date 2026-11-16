using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Screen side of the knife duel: cinematic letterbox, the tug-of-war slider (the marker runs toward whoever
    /// strikes more often; reaching your end wins), the pulsing [F] prompt, flashes, the result title, a red edge
    /// while losing — and small floating sliders over duels of other fighters.
    /// </summary>
    [DefaultExecutionOrder(950)]
    public class KnifeDuelHUD : MonoBehaviour
    {
        public static KnifeDuelHUD Instance { get; private set; }

        static readonly Color MineCol = new Color(0.25f, 0.78f, 1f);
        static readonly Color TheirCol = new Color(1f, 0.28f, 0.22f);
        const float BarW = 860f, BarH = 22f;

        Font font;
        Sprite white, soft;
        CanvasGroup group;
        RectTransform topBar, bottomBar, marker, fill, keyRt, barRt;
        Image fillImg, flashImg, edgeImg, markerGlow, leftCap, rightCap;
        Text title, sub, leftName, rightName, keyText, prompt, result;
        float flash, keyPulse, show, edge;
        int lastPresses;
        KnifeDuel lastDuel;
        int lastPhase = -1;

        // floating bars over other duels
        const int Floats = 4;
        readonly RectTransform[] floatRt = new RectTransform[Floats];
        readonly RectTransform[] floatMarker = new RectTransform[Floats];
        readonly RectTransform[] floatFill = new RectTransform[Floats];
        readonly Image[] floatFillImg = new Image[Floats];

        public static KnifeDuelHUD Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("KnifeDuelHUD").AddComponent<KnifeDuelHUD>();
            return Instance;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var tex = Texture2D.whiteTexture;
            white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            soft = MakeEdgeSprite();
            Build();
        }

        static Sprite MakeEdgeSprite()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) * 0.6f + Mathf.Sqrt(dx * dx + dy * dy) * 0.4f;
                    float a = Mathf.Clamp01((r - 0.55f) / 0.45f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                }
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
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

        RectTransform Stretch(string name, Transform parent)
        {
            var rt = Rect(name, parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            return rt;
        }

        Image Img(RectTransform rt, Color c, Sprite s = null)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.sprite = s ? s : white;
            i.color = c;
            i.raycastTarget = false;
            return i;
        }

        Text Txt(RectTransform rt, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Bold)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.color = c; t.alignment = anchor; t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(2f, -2f);
            return t;
        }

        void Build()
        {
            // floating bars live outside the main group (they show while the local player is not fighting)
            for (int i = 0; i < Floats; i++)
            {
                var r = Rect("FloatBar" + i, transform, Vector2.zero, Vector2.zero, new Vector2(150f, 12f));
                r.anchorMin = r.anchorMax = Vector2.zero;
                Img(r, new Color(0f, 0f, 0f, 0.6f));
                var f = Rect("Fill", r, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 8f));
                floatFillImg[i] = Img(f, Color.white);
                var m = Rect("Marker", r, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 16f));
                Img(m, Color.white);
                var lbl = Rect("Label", r, new Vector2(0.5f, 1f), new Vector2(0f, 12f), new Vector2(150f, 18f));
                Txt(lbl, 15, new Color(1f, 0.9f, 0.6f)).text = "ДУЭЛЬ НА НОЖАХ";
                floatRt[i] = r; floatMarker[i] = m; floatFill[i] = f;
                r.gameObject.SetActive(false);
            }

            var root = Stretch("Duel", transform);
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            edgeImg = Img(Stretch("Edge", root), new Color(0.8f, 0f, 0f, 0f), soft);
            flashImg = Img(Stretch("Flash", root), new Color(1f, 1f, 1f, 0f));

            topBar = Rect("Top", root, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4000f, 0f));
            topBar.pivot = new Vector2(0.5f, 1f);
            Img(topBar, Color.black);
            bottomBar = Rect("Bottom", root, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(4000f, 0f));
            bottomBar.pivot = new Vector2(0.5f, 0f);
            Img(bottomBar, Color.black);

            title = Txt(Rect("Title", root, new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(1200f, 60f)), 46, new Color(1f, 0.86f, 0.45f));
            title.text = "ДУЭЛЬ НА НОЖАХ";
            sub = Txt(Rect("Sub", root, new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(1200f, 30f)), 20, new Color(1f, 1f, 1f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Normal);
            sub.text = "Клинки сцепились — бей чаще соперника";

            // slider
            barRt = Rect("Bar", root, new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(BarW + 8f, BarH + 8f));
            Img(barRt, new Color(1f, 1f, 1f, 0.22f));
            var inner = Rect("Inner", barRt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(BarW, BarH));
            Img(inner, new Color(0.04f, 0.04f, 0.06f, 0.9f));
            leftCap = Img(Rect("LeftCap", barRt, new Vector2(0f, 0.5f), new Vector2(-6f, 0f), new Vector2(12f, BarH + 22f)), MineCol);
            rightCap = Img(Rect("RightCap", barRt, new Vector2(1f, 0.5f), new Vector2(6f, 0f), new Vector2(12f, BarH + 22f)), TheirCol);
            fill = Rect("Fill", inner, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, BarH - 6f));
            fillImg = Img(fill, MineCol);
            Img(Rect("Center", inner, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2f, BarH + 10f)), new Color(1f, 1f, 1f, 0.5f));
            marker = Rect("Marker", inner, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(8f, BarH + 26f));
            markerGlow = Img(Rect("Glow", marker, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(40f, BarH + 40f)), new Color(1f, 1f, 1f, 0.25f), soft);
            Img(marker, Color.white);
            leftName = Txt(Rect("LeftName", barRt, new Vector2(0f, 0.5f), new Vector2(0f, 34f), new Vector2(400f, 30f)), 24, MineCol, TextAnchor.MiddleLeft);
            leftName.rectTransform.pivot = new Vector2(0f, 0.5f);
            rightName = Txt(Rect("RightName", barRt, new Vector2(1f, 0.5f), new Vector2(0f, 34f), new Vector2(400f, 30f)), 24, TheirCol, TextAnchor.MiddleRight);
            rightName.rectTransform.pivot = new Vector2(1f, 0.5f);

            keyRt = Rect("Key", root, new Vector2(0.5f, 0f), new Vector2(0f, 104f), new Vector2(64f, 64f));
            Img(keyRt, new Color(1f, 1f, 1f, 0.92f));
            Img(Rect("KeyInner", keyRt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f)), new Color(0.08f, 0.08f, 0.1f, 1f));
            keyText = Txt(Rect("KeyText", keyRt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(64f, 64f)), 34, Color.white);
            keyText.text = "F";
            prompt = Txt(Rect("Prompt", root, new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(900f, 30f)), 22, new Color(1f, 1f, 1f, 0.85f));
            prompt.text = "ЖМИ  F  БЫСТРЕЕ!";

            result = Txt(Rect("Result", root, new Vector2(0.5f, 0.5f), new Vector2(0f, 180f), new Vector2(1400f, 120f)), 96, Color.white);
            result.text = "";
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            KnifeDuel mine = null;
            foreach (var d in KnifeDuel.Active) if (d && d.HasLocal) { mine = d; break; }
            UpdateFloats(mine);

            show = Mathf.MoveTowards(show, mine ? 1f : 0f, dt * (mine ? 3f : 2.2f));
            group.alpha = Mathf.Clamp01(show * 1.4f);
            float lb = Anim.Smooth(show) * 118f;
            topBar.sizeDelta = new Vector2(4000f, lb);
            bottomBar.sizeDelta = new Vector2(4000f, lb);
            flash = Mathf.MoveTowards(flash, 0f, dt * 2.6f);
            keyPulse = Mathf.MoveTowards(keyPulse, 0f, dt * 7f);

            if (mine != lastDuel) { lastDuel = mine; lastPhase = -1; lastPresses = 0; if (mine) flash = 0.85f; }
            if (!mine)
            {
                flashImg.color = new Color(1f, 1f, 1f, flash * 0.6f);
                edge = Mathf.MoveTowards(edge, 0f, dt * 2f);
                edgeImg.color = new Color(0.7f, 0f, 0f, edge);
                return;
            }

            var me = mine.LocalSide;
            bool meA = me == mine.A;
            var them = meA ? mine.B : mine.A;
            leftName.text = me ? me.displayName.ToUpperInvariant() : "";
            rightName.text = them ? them.displayName.ToUpperInvariant() : "";

            float lead = meA ? mine.Progress : -mine.Progress; // +1 = I win
            // the marker runs toward whoever strikes more often; my end is the left one
            float x = -lead * (BarW * 0.5f - 4f);
            marker.anchoredPosition = new Vector2(x, 0f);
            fill.anchoredPosition = new Vector2(x * 0.5f, 0f);
            fill.sizeDelta = new Vector2(Mathf.Abs(x), BarH - 6f);
            Color lc = lead >= 0f ? MineCol : TheirCol;
            fillImg.color = Color.Lerp(lc * 0.7f, lc, Mathf.Abs(lead));
            markerGlow.color = new Color(lc.r, lc.g, lc.b, 0.35f + 0.4f * Mathf.Abs(lead));
            float pulseL = Mathf.Max(0f, lead), pulseR = Mathf.Max(0f, -lead);
            leftCap.color = Color.Lerp(MineCol * 0.6f, Color.white, pulseL * pulseL * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 18f)));
            rightCap.color = Color.Lerp(TheirCol * 0.6f, Color.white, pulseR * pulseR * (0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 18f)));

            int presses = me ? mine.Presses(me) : 0;
            if (presses != lastPresses) { lastPresses = presses; keyPulse = 1f; }
            float pulse = 1f + 0.25f * keyPulse + 0.05f * Mathf.Sin(Time.unscaledTime * 12f);
            keyRt.localScale = Vector3.one * pulse;
            float hitShake = them ? mine.StrikePulse(them) : 0f;
            barRt.anchoredPosition = new Vector2(Random.Range(-1f, 1f) * 5f * hitShake, 170f + Random.Range(-1f, 1f) * 3f * hitShake);

            int phase = mine.Phase;
            bool fighting = phase <= 1;
            keyRt.gameObject.SetActive(fighting);
            prompt.gameObject.SetActive(fighting);
            if (phase != lastPhase)
            {
                if (phase == 2) flash = 0.5f;
                lastPhase = phase;
            }
            if (phase == 2 && mine.PhaseTime > 0.5f && mine.PhaseTime < 0.6f) flash = Mathf.Max(flash, 0.35f);

            if (phase >= 2)
            {
                bool won = mine.Winner == me;
                result.text = won ? "ПОБЕДА" : "ПОРАЖЕНИЕ";
                result.color = won ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.25f, 0.2f);
                float k = Anim.Smooth(mine.PhaseTime / 0.3f);
                result.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, k);
                var c = result.color; c.a = k; result.color = c;
                title.text = won ? "ДОБИВАНИЕ" : "ВЫ ПРОИГРАЛИ ДУЭЛЬ";
                sub.text = "";
            }
            else
            {
                result.text = "";
                title.text = "ДУЭЛЬ НА НОЖАХ";
                sub.text = phase == 0 ? "Клинки сцепились!" : "Бей чаще соперника — доведи нож до своего края";
                title.rectTransform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(Time.unscaledTime * 6f));
            }

            float wantEdge = phase <= 1 ? Mathf.Clamp01(-lead) * 0.7f : 0f;
            edge = Mathf.MoveTowards(edge, wantEdge * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 9f)), dt * 3f);
            edgeImg.color = new Color(0.75f, 0f, 0f, edge);
            flashImg.color = new Color(1f, 1f, 1f, flash * 0.6f);
        }

        void UpdateFloats(KnifeDuel mine)
        {
            int used = 0;
            var cam = Camera.main;
            if (cam)
            {
                foreach (var d in KnifeDuel.Active)
                {
                    if (!d || d == mine || d.HasLocal || used >= Floats || d.Phase >= 3) continue;
                    Vector3 p = d.ClashPoint + Vector3.up * 0.75f;
                    Vector3 sp = cam.WorldToScreenPoint(p);
                    if (sp.z <= 0.1f || sp.z > 45f) continue;
                    var r = floatRt[used];
                    r.gameObject.SetActive(true);
                    var canvasRt = (RectTransform)transform;
                    float s = canvasRt.rect.width / Mathf.Max(1f, Screen.width);
                    r.anchoredPosition = new Vector2(sp.x * s, sp.y * s);
                    float k = Mathf.Clamp(14f / sp.z, 0.5f, 1.2f);
                    r.localScale = Vector3.one * k;
                    float x = -d.Progress * 73f;
                    floatMarker[used].anchoredPosition = new Vector2(x, 0f);
                    floatFill[used].anchoredPosition = new Vector2(x * 0.5f, 0f);
                    floatFill[used].sizeDelta = new Vector2(Mathf.Abs(x), 8f);
                    floatFillImg[used].color = d.Progress >= 0f ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.5f, 0.3f);
                    used++;
                }
            }
            for (int i = used; i < Floats; i++) if (floatRt[i].gameObject.activeSelf) floatRt[i].gameObject.SetActive(false);
        }
    }
}
