using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// HUD of the objective modes: the task line and the point badges under the match clock, the objectives on screen
    /// (a lettered circle over each one with the distance; the important ones stay pinned to the screen edge when you
    /// look away), event banners ("ТОЧКА B ЗАХВАЧЕНА", "РАУНД 3"), the plant / defuse prompt and its progress bar.
    /// Colours from the viewer's side: ours blue, theirs red, nobody's grey.
    /// </summary>
    [DefaultExecutionOrder(130)]
    public class ObjectiveHUD : MonoBehaviour
    {
        static ObjectiveHUD inst;

        Font font;
        Sprite disc, white;
        Text task, bannerTitle, bannerSub, prompt, barLabel;
        Image bannerBack, barBack, barFill;
        CanvasGroup bannerGroup, barGroup;
        RectTransform badgeRow, iconRoot;
        float bannerAt = -99f;
        readonly List<ObjectiveIcon> buf = new List<ObjectiveIcon>();

        class View
        {
            public RectTransform rt;
            public Image back, fillBack, fill;
            public Text letter, dist;
        }
        readonly List<View> badges = new List<View>(), icons = new List<View>();

        public static void Ensure()
        {
            if (inst) return;
            inst = new GameObject("ObjectiveHUD").AddComponent<ObjectiveHUD>();
        }

        public static void Banner(string title, string sub, Color color)
        {
            Ensure();
            inst.bannerTitle.text = title;
            inst.bannerTitle.color = Color.Lerp(color, Color.white, 0.25f);
            inst.bannerSub.text = sub ?? "";
            inst.bannerBack.color = new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 0.6f);
            inst.bannerAt = Time.unscaledTime;
        }

        void Awake()
        {
            inst = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var tex = Texture2D.whiteTexture;
            white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            disc = MakeDisc();
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 17;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            Build();
        }

        void OnDestroy() { if (inst == this) inst = null; }

        static Sprite MakeDisc()
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(N * 0.5f, N * 0.5f)) / (N * 0.5f);
                    float g = d > 0.82f ? 0.08f : 1f; // dark rim
                    px[y * N + x] = new Color(g, g, g, Mathf.Clamp01((1f - d) * 12f));
                }
            t.SetPixels(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        RectTransform Box(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        Text Txt(RectTransform rt, int size, TextAnchor a, bool bold = false)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = Color.white; t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            if (bold) t.fontStyle = FontStyle.Bold;
            var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = new Color(0f, 0f, 0f, 0.75f); o.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        Image Img(RectTransform rt, Sprite s, Color c)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.sprite = s; i.color = c; i.raycastTarget = false;
            return i;
        }

        void Build()
        {
            var top = new Vector2(0.5f, 1f);
            // under MatchHUD's clock (-18) and score line (-68)
            task = Txt(Box("Task", transform, top, top, new Vector2(0f, -100f), new Vector2(900f, 28f)), 21, TextAnchor.UpperCenter);
            badgeRow = Box("Badges", transform, top, top, new Vector2(0f, -132f), new Vector2(400f, 56f));
            iconRoot = Box("Icons", transform, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);

            var banner = Box("Banner", transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -205f), new Vector2(1100f, 112f));
            bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f; bannerGroup.blocksRaycasts = false;
            bannerBack = Img(banner, white, new Color(0f, 0f, 0f, 0.55f));
            bannerTitle = Txt(Box("Title", banner, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(1100f, 60f)), 50, TextAnchor.UpperCenter, true);
            bannerSub = Txt(Box("Sub", banner, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(1100f, 34f)), 24, TextAnchor.LowerCenter);

            var bar = Box("Channel", transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 360f), new Vector2(440f, 70f));
            barGroup = bar.gameObject.AddComponent<CanvasGroup>();
            barGroup.alpha = 0f; barGroup.blocksRaycasts = false;
            prompt = Txt(Box("Prompt", transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 440f), new Vector2(900f, 34f)), 26, TextAnchor.MiddleCenter, true);
            barLabel = Txt(Box("Label", bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(440f, 30f)), 22, TextAnchor.UpperCenter, true);
            var bb = Box("Back", bar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(440f, 18f));
            barBack = Img(bb, white, new Color(0f, 0f, 0f, 0.6f));
            var bf = Box("Fill", bb, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(3f, 0f), new Vector2(0f, 12f));
            barFill = Img(bf, white, new Color(1f, 0.75f, 0.25f, 0.95f));
        }

        View MakeView(Transform parent, float size)
        {
            var v = new View();
            v.rt = Box("Obj", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            v.back = Img(v.rt, disc, Color.white);
            v.letter = Txt(Box("L", v.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 1f), new Vector2(size, size)), Mathf.RoundToInt(size * 0.55f), TextAnchor.MiddleCenter, true);
            var fb = Box("PB", v.rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -3f), new Vector2(size * 1.05f, 6f));
            v.fillBack = Img(fb, white, new Color(0f, 0f, 0f, 0.6f));
            var f = Box("F", fb, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1f, 0f), new Vector2(0f, 4f));
            v.fill = Img(f, white, Color.white);
            v.dist = Txt(Box("D", v.rt, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(120f, 20f)), 15, TextAnchor.UpperCenter);
            return v;
        }

        static View Pooled(List<View> pool, int i, System.Func<View> make)
        {
            while (pool.Count <= i) pool.Add(make());
            var v = pool[i];
            if (!v.rt.gameObject.activeSelf) v.rt.gameObject.SetActive(true);
            return v;
        }

        static void HideFrom(List<View> pool, int n)
        {
            for (int i = n; i < pool.Count; i++) if (pool[i].rt.gameObject.activeSelf) pool[i].rt.gameObject.SetActive(false);
        }

        void Paint(View v, ObjectiveIcon ic, int my, float pulse, float size)
        {
            Color c = ObjectiveMode.IconColor(ic, my);
            float a = ic.pulse ? pulse : 1f;
            v.back.color = new Color(c.r, c.g, c.b, 0.88f * a);
            v.letter.text = ic.label;
            bool prog = ic.progress > 0.001f;
            v.fillBack.enabled = v.fill.enabled = prog;
            if (prog)
            {
                Color pc = ObjectiveMode.RelColor(ic.progressTeam, my);
                v.fill.color = pc;
                v.fill.rectTransform.sizeDelta = new Vector2((size * 1.05f - 2f) * Mathf.Clamp01(ic.progress), 4f);
            }
        }

        void LateUpdate()
        {
            var mode = ObjectiveMode.Current;
            var mm = MatchManager.Instance;
            var cam = Camera.main;
            bool on = mode && mm;
            int my = ObjectiveMode.MyTeam;
            float pulse = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 5f));

            // banner: in, hold, out
            float age = Time.unscaledTime - bannerAt;
            bannerGroup.alpha = age < 0.25f ? age / 0.25f : age < 3.2f ? 1f : Mathf.Clamp01(1f - (age - 3.2f) / 0.6f);
            bannerGroup.transform.localScale = Vector3.one * (age < 0.25f ? Mathf.Lerp(1.15f, 1f, age / 0.25f) : 1f);

            buf.Clear();
            if (on && !mm.Ended) mode.Icons(buf, my);
            task.text = on && !mm.Ended ? mode.TaskLine(my) : "";

            // badges under the clock: the zones (letters with a radius)
            int nb = 0;
            int zones = 0;
            foreach (var ic in buf) if (ic.radius > 0f && ic.important) zones++;
            float gap = 58f, x0 = -(zones - 1) * gap * 0.5f;
            foreach (var ic in buf)
            {
                if (!(ic.radius > 0f && ic.important)) continue;
                var v = Pooled(badges, nb, () => MakeView(badgeRow, 40f));
                v.rt.anchoredPosition = new Vector2(x0 + nb * gap, 0f);
                Paint(v, ic, my, pulse, 40f);
                v.dist.text = "";
                nb++;
            }
            HideFrom(badges, nb);

            // over the objectives in the world
            int ni = 0;
            var ph = PlayerHealth.Local;
            if (cam && !(ph && ph.IsDead && mm && mm.Ended))
            {
                float w = Screen.width, h = Screen.height, k0 = Mathf.Min(w / 1920f, h / 1080f);
                // pinned icons keep clear of the clock and badges at the top and the weapon panel at the bottom
                float mx = 60f * k0, mTop = 180f * k0, mBottom = 130f * k0;
                foreach (var ic in buf)
                {
                    // standing in the zone: the badges up top say it all
                    if (ic.radius > 0f && ObjectiveMode.Inside(cam.transform.position - Vector3.up * 1.5f, ic.pos, ic.radius)) continue;
                    Vector3 wp = ic.pos + Vector3.up * (ic.radius > 0f ? 2.4f : 0.9f);
                    Vector3 sp = cam.WorldToScreenPoint(wp);
                    bool behind = sp.z < 0f;
                    bool offScreen = behind || sp.x < mx || sp.x > w - mx || sp.y < mBottom || sp.y > h - mTop;
                    if (offScreen && !ic.important) continue;
                    if (behind) { sp.x = w - sp.x; sp.y = h - sp.y; }
                    if (offScreen)
                    {
                        // pin to the screen edge, in the direction of the objective
                        Vector2 c = new Vector2(w * 0.5f, (mBottom + h - mTop) * 0.5f);
                        float hx = w * 0.5f - mx, hy = (h - mTop - mBottom) * 0.5f;
                        Vector2 d = new Vector2(sp.x, sp.y) - c;
                        if (behind && d.sqrMagnitude < 1f) d = Vector2.down;
                        float k = Mathf.Min(hx / Mathf.Max(1e-3f, Mathf.Abs(d.x)), hy / Mathf.Max(1e-3f, Mathf.Abs(d.y)));
                        Vector2 e = c + d * k;
                        sp.x = e.x; sp.y = e.y;
                    }
                    var v = Pooled(icons, ni, () => MakeView(iconRoot, 34f));
                    v.rt.position = new Vector3(sp.x, sp.y, 0f);
                    Paint(v, ic, my, pulse, 34f);
                    float dist = Vector3.Distance(cam.transform.position, ic.pos);
                    v.dist.text = Mathf.RoundToInt(dist) + " м";
                    // close up the icon fades so it does not cover the fight
                    float fade = ic.radius > 0f ? Mathf.Clamp01((dist - ic.radius * 0.6f) / 6f) * 0.75f + 0.25f : 1f;
                    v.back.color = new Color(v.back.color.r, v.back.color.g, v.back.color.b, v.back.color.a * fade);
                    v.letter.color = new Color(1f, 1f, 1f, fade);
                    v.dist.color = new Color(1f, 1f, 1f, 0.85f * fade);
                    ni++;
                }
            }
            HideFrom(icons, ni);

            // plant / defuse
            string p = on && !mm.Ended ? mode.Prompt : null;
            float ch = on ? mode.Channel : -1f;
            prompt.text = ch < 0f && !string.IsNullOrEmpty(p) ? p : "";
            prompt.color = new Color(1f, 0.85f, 0.4f, 0.75f + 0.25f * pulse);
            barGroup.alpha = Mathf.MoveTowards(barGroup.alpha, ch >= 0f ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (ch >= 0f)
            {
                barLabel.text = mode.ChannelLabel;
                barFill.rectTransform.sizeDelta = new Vector2((440f - 6f) * Mathf.Clamp01(ch), 12f);
            }
        }
    }
}
