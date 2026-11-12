using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Shared look for every menu: dark glass panels with rounded corners, amber accent, OS fonts
    /// (Bahnschrift for headings, Segoe UI for text; built-in font as a fallback) and a small set of
    /// widgets — buttons with hover animation, sliders, option switches, arrow selectors, tabs, scroll lists.
    /// Everything is built from code (uGUI), so scenes need no prefabs.
    /// </summary>
    public static class UIKit
    {
        // ------------------------------------------------------------------ theme

        public static readonly Color Accent = new Color(1f, 0.74f, 0.26f);
        public static readonly Color AccentDim = new Color(1f, 0.74f, 0.26f, 0.35f);
        public static readonly Color Bg = new Color(0.045f, 0.05f, 0.06f, 0.94f);
        public static readonly Color PanelC = new Color(0.085f, 0.095f, 0.11f, 0.985f);
        public static readonly Color Card = new Color(1f, 1f, 1f, 0.055f);
        public static readonly Color CardHover = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color TextC = new Color(0.93f, 0.94f, 0.95f);
        public static readonly Color TextDim = new Color(0.93f, 0.94f, 0.95f, 0.55f);
        public static readonly Color Good = new Color(0.45f, 0.9f, 0.5f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.36f);

        static Font font, head;

        public static Font Font
        {
            get
            {
                if (font) return font;
                font = TryOs("Segoe UI", "Arial");
                return font;
            }
        }

        public static Font Head
        {
            get
            {
                if (head) return head;
                head = TryOs("Bahnschrift SemiBold", "Bahnschrift", "Segoe UI Semibold", "Segoe UI", "Arial");
                return head;
            }
        }

        static Font TryOs(params string[] names)
        {
            try
            {
                var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
                foreach (var n in names)
                    if (installed.Contains(n)) { var f = Font.CreateDynamicFontFromOSFont(n, 32); if (f) return f; }
            }
            catch (Exception) { }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ------------------------------------------------------------------ sprites

        static readonly Dictionary<int, Sprite> rounded = new Dictionary<int, Sprite>();

        /// <summary>White rounded rectangle, 9-sliced (radius in pixels at 1080p).</summary>
        public static Sprite Rounded(int radius)
        {
            radius = Mathf.Clamp(radius, 1, 32);
            if (rounded.TryGetValue(radius, out var s) && s) return s;
            int size = radius * 2 + 4;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius), cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            var b = radius + 1;
            s = Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
            rounded[radius] = s;
            return s;
        }

        static Texture2D vgrad;

        /// <summary>Vertical gradient (alpha 1 at the bottom → 0 at the top), for shading.</summary>
        public static Texture2D VGradient
        {
            get
            {
                if (vgrad) return vgrad;
                vgrad = new Texture2D(2, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < 64; y++) { var c = new Color(1f, 1f, 1f, 1f - y / 63f); vgrad.SetPixel(0, y, c); vgrad.SetPixel(1, y, c); }
                vgrad.Apply();
                return vgrad;
            }
        }

        // ------------------------------------------------------------------ basics

        public static RectTransform Rect(string name, Transform parent)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Rect anchored at a point (pivot = anchor unless given).</summary>
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var rt = Rect(name, parent);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>Stretches over the parent with insets (left, bottom, right, top).</summary>
        public static RectTransform Fill(RectTransform rt, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        /// <summary>Stretches over a fraction of the parent.</summary>
        public static RectTransform Span(RectTransform rt, float x0, float y0, float x1, float y1)
        {
            rt.anchorMin = new Vector2(x0, y0); rt.anchorMax = new Vector2(x1, y1);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color c, int radius = 10)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.sprite = Rounded(radius);
            img.type = Image.Type.Sliced;
            img.color = c;
            return img;
        }

        public static Image Solid(Transform parent, string name, Color c)
        {
            var img = Rect(name, parent).gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string s, int size, TextAnchor align, Color? c = null, bool heading = false, FontStyle style = FontStyle.Normal)
        {
            var t = Rect("Text", parent).gameObject.AddComponent<Text>();
            t.font = heading ? Head : Font;
            t.fontSize = size;
            t.text = s;
            t.alignment = align;
            t.color = c ?? TextC;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Canvas MakeCanvas(GameObject go, int order)
        {
            var canvas = go.GetComponent<Canvas>();
            if (!canvas) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.GetComponent<CanvasScaler>();
            if (!scaler) scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (!go.GetComponent<GraphicRaycaster>()) go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // ------------------------------------------------------------------ buttons

        public enum Style { Normal, Primary, Ghost, Danger }

        public static Button Button(Transform parent, string label, Action onClick, Style style = Style.Normal, int fontSize = 26, bool leftAlign = false)
        {
            var bg = Panel(parent, label, Color.white, 8);
            var b = bg.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.targetGraphic = bg;
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            var fx = bg.gameObject.AddComponent<UIButtonFx>();
            fx.bg = bg;
            fx.style = style;
            var accent = Solid(bg.transform, "Accent", Accent);
            var art = accent.rectTransform;
            art.anchorMin = new Vector2(0f, 0.18f); art.anchorMax = new Vector2(0f, 0.82f);
            art.pivot = new Vector2(0f, 0.5f);
            art.sizeDelta = new Vector2(4f, 0f); art.anchoredPosition = Vector2.zero;
            fx.accent = accent;
            var t = Label(bg.transform, label, fontSize, leftAlign ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, null, true);
            Fill(t.rectTransform, leftAlign ? 26f : 8f, 0f, 8f, 0f);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            fx.label = t;
            fx.Refresh(true);
            b.onClick.AddListener(() =>
            {
                ProcAudio.Play(Sfx.Switch, 0.45f);
                onClick?.Invoke();
            });
            return b;
        }

        public static Text LabelOf(Button b) { var fx = b.GetComponent<UIButtonFx>(); return fx && fx.label ? fx.label : b.GetComponentInChildren<Text>(); }

        // ------------------------------------------------------------------ rows (for vertical lists)

        public static RectTransform Row(Transform list, float height = 64f)
        {
            var rt = Rect("Row", list);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = le.minHeight = height;
            le.flexibleWidth = 1f;
            return rt;
        }

        public static Text RowLabel(RectTransform row, string label, string hint = null)
        {
            var bg = Panel(row, "RowBg", Card, 8);
            Fill(bg.rectTransform);
            bg.raycastTarget = false;
            var t = Label(row, label, 24, TextAnchor.MiddleLeft);
            Span(t.rectTransform, 0f, 0f, 0.46f, 1f);
            t.rectTransform.offsetMin = new Vector2(22f, 0f);
            if (!string.IsNullOrEmpty(hint))
            {
                t.alignment = TextAnchor.UpperLeft;
                t.rectTransform.offsetMax = new Vector2(0f, -10f);
                var h = Label(row, hint, 17, TextAnchor.LowerLeft, TextDim);
                Span(h.rectTransform, 0f, 0f, 0.5f, 1f);
                h.rectTransform.offsetMin = new Vector2(22f, 9f);
            }
            return t;
        }

        public static void Header(Transform list, string title)
        {
            var row = Row(list, 52f);
            var t = Label(row, title.ToUpperInvariant(), 22, TextAnchor.LowerLeft, Accent, true);
            Fill(t.rectTransform, 6f, 8f, 0f, 0f);
            var line = Solid(row, "Line", AccentDim);
            line.rectTransform.anchorMin = new Vector2(0f, 0f); line.rectTransform.anchorMax = new Vector2(1f, 0f);
            line.rectTransform.sizeDelta = new Vector2(0f, 2f); line.rectTransform.anchoredPosition = new Vector2(0f, 1f);
        }

        /// <summary>Slider row: label, bar, value text.</summary>
        public static Slider SliderRow(Transform list, string label, float min, float max, float value, Func<float, string> fmt, Action<float> onChange, string hint = null, bool whole = false)
        {
            var row = Row(list, hint != null ? 74f : 64f);
            RowLabel(row, label, hint);
            var holder = Rect("Slider", row);
            Span(holder, 0.5f, 0f, 0.86f, 1f);
            var s = holder.gameObject.AddComponent<Slider>();
            var track = Panel(holder, "Track", new Color(1f, 1f, 1f, 0.12f), 4);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f); track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 8f);
            var fillArea = Rect("FillArea", holder);
            fillArea.anchorMin = new Vector2(0f, 0.5f); fillArea.anchorMax = new Vector2(1f, 0.5f); fillArea.sizeDelta = new Vector2(0f, 8f);
            var fill = Panel(fillArea, "Fill", Accent, 4);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0f, 1f); fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = Rect("HandleArea", holder);
            handleArea.anchorMin = new Vector2(0f, 0.5f); handleArea.anchorMax = new Vector2(1f, 0.5f);
            handleArea.sizeDelta = new Vector2(-20f, 22f); handleArea.anchoredPosition = Vector2.zero;
            var handle = Panel(handleArea, "Handle", Color.white, 11);
            handle.rectTransform.sizeDelta = new Vector2(22f, 0f);
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole;
            s.value = value;
            var nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
            var cols = s.colors; cols.highlightedColor = Accent; cols.pressedColor = Accent; s.colors = cols;
            var v = Label(row, fmt(value), 24, TextAnchor.MiddleRight, Accent, true);
            Span(v.rectTransform, 0.86f, 0f, 1f, 1f);
            v.rectTransform.offsetMax = new Vector2(-20f, 0f);
            s.onValueChanged.AddListener(x => { v.text = fmt(x); onChange(x); });
            return s;
        }

        /// <summary>Segmented switch: a row of option buttons, one highlighted.</summary>
        public static Action<int> ChoiceRow(Transform list, string label, string[] options, int index, Action<int> onChange, string hint = null)
        {
            var row = Row(list, hint != null ? 74f : 64f);
            RowLabel(row, label, hint);
            var holder = Rect("Choices", row);
            Span(holder, 0.5f, 0.15f, 1f, 0.85f);
            holder.offsetMax = new Vector2(-16f, 0f);
            var bg = Panel(holder, "Bg", new Color(0f, 0f, 0f, 0.35f), 8);
            Fill(bg.rectTransform);
            var buttons = new List<UIButtonFx>();
            Action<int> select = null;
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                var b = Button(holder, options[i], () => { select(idx); onChange(idx); }, Style.Ghost, 21);
                Span((RectTransform)b.transform, i / (float)options.Length, 0f, (i + 1) / (float)options.Length, 1f);
                ((RectTransform)b.transform).offsetMin = new Vector2(3f, 3f);
                ((RectTransform)b.transform).offsetMax = new Vector2(-3f, -3f);
                buttons.Add(b.GetComponent<UIButtonFx>());
            }
            select = k => { for (int i = 0; i < buttons.Count; i++) buttons[i].Selected = i == k; };
            select(index);
            return select;
        }

        /// <summary>◄ value ► selector. getText(index) → label; optional swatch color per index.</summary>
        public static Action SelectorRow(Transform list, string label, int count, Func<int> get, Action<int> set, Func<int, string> name, Func<int, Color?> swatch = null)
        {
            var row = Row(list, 58f);
            RowLabel(row, label);
            var holder = Rect("Selector", row);
            Span(holder, 0.42f, 0.12f, 1f, 0.88f);
            holder.offsetMax = new Vector2(-12f, 0f);
            var bg = Panel(holder, "Bg", new Color(0f, 0f, 0f, 0.3f), 8);
            Fill(bg.rectTransform);
            Text value = Label(holder, "", 22, TextAnchor.MiddleCenter);
            Fill(value.rectTransform, 56f, 0f, 56f, 0f);
            Image sw = null;
            if (swatch != null)
            {
                sw = Panel(holder, "Swatch", Color.white, 6);
                var srt = sw.rectTransform;
                srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
                srt.pivot = new Vector2(0f, 0.5f);
                srt.sizeDelta = new Vector2(26f, 26f);
                srt.anchoredPosition = new Vector2(62f, 0f);
                value.rectTransform.offsetMin = new Vector2(96f, 0f);
                value.alignment = TextAnchor.MiddleLeft;
            }
            Action refresh = () =>
            {
                int i = get();
                value.text = name(i);
                if (sw)
                {
                    var c = swatch(i);
                    sw.gameObject.SetActive(c.HasValue);
                    if (c.HasValue) sw.color = c.Value;
                }
            };
            var l = Button(holder, "‹", () => { set((get() + count - 1) % count); refresh(); }, Style.Ghost, 34);
            var lr = (RectTransform)l.transform; lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(0f, 1f); lr.pivot = new Vector2(0f, 0.5f); lr.sizeDelta = new Vector2(52f, 0f); lr.anchoredPosition = Vector2.zero;
            var r = Button(holder, "›", () => { set((get() + 1) % count); refresh(); }, Style.Ghost, 34);
            var rr = (RectTransform)r.transform; rr.anchorMin = new Vector2(1f, 0f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(1f, 0.5f); rr.sizeDelta = new Vector2(52f, 0f); rr.anchoredPosition = Vector2.zero;
            refresh();
            return refresh;
        }

        public static InputField Input(Transform parent, string value, string placeholder, int fontSize = 24)
        {
            var img = Panel(parent, "Input", new Color(0f, 0f, 0f, 0.38f), 8);
            img.raycastTarget = true;
            var f = img.gameObject.AddComponent<InputField>();
            var text = Label(img.transform, "", fontSize, TextAnchor.MiddleLeft);
            Fill(text.rectTransform, 16f, 0f, 16f, 0f);
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            var ph = Label(img.transform, placeholder, fontSize, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.3f));
            Fill(ph.rectTransform, 16f, 0f, 16f, 0f);
            ph.fontStyle = FontStyle.Italic;
            f.textComponent = text;
            f.placeholder = ph;
            f.targetGraphic = img;
            var cols = f.colors; cols.normalColor = Color.white; cols.highlightedColor = new Color(1.3f, 1.3f, 1.3f); cols.selectedColor = new Color(1.5f, 1.4f, 1.2f); f.colors = cols;
            f.caretColor = Accent;
            f.selectionColor = AccentDim;
            f.text = value;
            var nav = f.navigation; nav.mode = Navigation.Mode.None; f.navigation = nav;
            return f;
        }

        /// <summary>Horizontal 0..1 bar (stat bars on the loadout cards).</summary>
        public static Image Bar(Transform parent, float value, Color c)
        {
            var bg = Panel(parent, "Bar", new Color(1f, 1f, 1f, 0.08f), 3);
            bg.raycastTarget = false;
            var fill = Panel(bg.transform, "Fill", c, 3);
            fill.raycastTarget = false;
            var rt = fill.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return fill;
        }

        // ------------------------------------------------------------------ containers

        /// <summary>Vertical scroll list filling <paramref name="parent"/>; returns the content (add rows to it).</summary>
        public static RectTransform ScrollList(RectTransform parent, float spacing = 8f, float padding = 4f)
        {
            var view = Rect("Scroll", parent);
            Fill(view);
            var sr = view.gameObject.AddComponent<ScrollRect>();
            var vp = Rect("Viewport", view);
            Fill(vp, 0f, 0f, 14f, 0f);
            vp.gameObject.AddComponent<RectMask2D>();
            var hit = vp.gameObject.AddComponent<Image>(); hit.color = new Color(0f, 0f, 0f, 0f); // receives scroll events
            var content = Rect("Content", vp);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var vl = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vl.spacing = spacing;
            vl.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding + 8);
            vl.childControlWidth = true; vl.childControlHeight = true;
            vl.childForceExpandWidth = true; vl.childForceExpandHeight = false;
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp; sr.content = content;
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            // scrollbar
            var bar = Rect("Scrollbar", view);
            bar.anchorMin = new Vector2(1f, 0f); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(6f, 0f); bar.anchoredPosition = Vector2.zero;
            var barBg = bar.gameObject.AddComponent<Image>(); barBg.sprite = Rounded(3); barBg.type = Image.Type.Sliced; barBg.color = new Color(1f, 1f, 1f, 0.05f);
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            var slide = Rect("Sliding", bar); Fill(slide);
            var handle = Panel(slide, "Handle", new Color(1f, 1f, 1f, 0.3f), 3);
            Fill(handle.rectTransform);
            sb.handleRect = handle.rectTransform; sb.targetGraphic = handle;
            sr.verticalScrollbar = sb;
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        /// <summary>Full-screen modal: dimmed background, centered rounded window with a title. Returns the window body.</summary>
        public static RectTransform Modal(Transform parent, string title, Vector2 size, out RectTransform root)
        {
            root = Rect("Modal_" + title, parent);
            Fill(root);
            var dim = root.gameObject.AddComponent<Image>();
            dim.color = new Color(0.01f, 0.012f, 0.016f, 0.9f);
            var win = Panel(root, "Window", PanelC, 14).rectTransform;
            win.anchorMin = win.anchorMax = win.pivot = new Vector2(0.5f, 0.5f);
            win.sizeDelta = size;
            var top = Solid(win, "TopLine", Accent).rectTransform;
            top.anchorMin = new Vector2(0f, 1f); top.anchorMax = new Vector2(1f, 1f); top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(-28f, 3f); top.anchoredPosition = Vector2.zero;
            var t = Label(win, title.ToUpperInvariant(), 38, TextAnchor.MiddleLeft, TextC, true);
            t.rectTransform.anchorMin = new Vector2(0f, 1f); t.rectTransform.anchorMax = new Vector2(1f, 1f); t.rectTransform.pivot = new Vector2(0.5f, 1f);
            t.rectTransform.sizeDelta = new Vector2(-72f, 80f); t.rectTransform.anchoredPosition = new Vector2(0f, -8f);
            var body = Rect("Body", win);
            Fill(body, 36f, 28f, 36f, 96f);
            return body;
        }

        /// <summary>Tab bar across the top of <paramref name="area"/>; returns the page containers.</summary>
        public static RectTransform[] Tabs(RectTransform area, string[] names, int initial = 0, Action<int> onTab = null)
        {
            var bar = Rect("TabBar", area);
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(0f, 54f); bar.anchoredPosition = Vector2.zero;
            var pages = new RectTransform[names.Length];
            var fxs = new UIButtonFx[names.Length];
            Action<int> show = null;
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = Button(bar, names[i], () => { show(idx); onTab?.Invoke(idx); }, Style.Ghost, 23);
                Span((RectTransform)b.transform, i / (float)names.Length, 0f, (i + 1) / (float)names.Length, 1f);
                ((RectTransform)b.transform).offsetMin = new Vector2(i == 0 ? 0f : 4f, 0f);
                ((RectTransform)b.transform).offsetMax = new Vector2(i == names.Length - 1 ? 0f : -4f, 0f);
                fxs[i] = b.GetComponent<UIButtonFx>();
                var page = Rect("Page_" + names[i], area);
                Fill(page, 0f, 0f, 0f, 66f);
                pages[i] = page;
            }
            show = k => { for (int i = 0; i < pages.Length; i++) { pages[i].gameObject.SetActive(i == k); fxs[i].Selected = i == k; } };
            show(initial);
            return pages;
        }
    }

    /// <summary>Hover / press / selected animation for <see cref="UIKit.Button"/>.</summary>
    public class UIButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public Image bg, accent;
        public Text label;
        public UIKit.Style style;
        bool hover, down, selected;
        float h; // 0..1 animated hover
        Vector2? labelBase;
        Selectable sel;

        public bool Selected { get => selected; set { selected = value; Refresh(false); } }

        void Awake() => sel = GetComponent<Selectable>();
        public void OnPointerEnter(PointerEventData e) { hover = true; if (Interactable) ProcAudio.Play(Sfx.Click, 0.12f, 0.1f); }
        public void OnPointerExit(PointerEventData e) { hover = false; down = false; }
        public void OnPointerDown(PointerEventData e) => down = true;
        public void OnPointerUp(PointerEventData e) => down = false;
        void OnDisable() { hover = down = false; h = 0f; Refresh(true); }

        bool Interactable => !sel || sel.interactable;

        void Update()
        {
            float target = Interactable && (hover || selected) ? 1f : 0f;
            if (Mathf.Abs(h - target) > 0.001f || down)
            {
                h = Mathf.MoveTowards(h, target, Time.unscaledDeltaTime * 9f);
                Refresh(false);
            }
        }

        public void Refresh(bool instant)
        {
            if (instant) h = selected ? 1f : 0f;
            if (!bg) return;
            bool on = Interactable;
            Color baseC, hoverC;
            switch (style)
            {
                case UIKit.Style.Primary: baseC = new Color(1f, 0.74f, 0.26f, 0.88f); hoverC = new Color(1f, 0.82f, 0.4f, 1f); break;
                case UIKit.Style.Ghost: baseC = new Color(1f, 1f, 1f, 0f); hoverC = new Color(1f, 1f, 1f, 0.1f); break;
                case UIKit.Style.Danger: baseC = new Color(0.8f, 0.25f, 0.2f, 0.25f); hoverC = new Color(0.9f, 0.3f, 0.25f, 0.55f); break;
                default: baseC = new Color(1f, 1f, 1f, 0.06f); hoverC = new Color(1f, 1f, 1f, 0.13f); break;
            }
            if (selected && style == UIKit.Style.Ghost) hoverC = new Color(1f, 0.74f, 0.26f, 0.22f);
            Color c = Color.Lerp(baseC, hoverC, h);
            if (down && on) c *= 0.85f;
            if (!on) c.a *= 0.4f;
            bg.color = c;
            if (accent)
            {
                accent.enabled = style != UIKit.Style.Primary;
                var rt = accent.rectTransform;
                rt.sizeDelta = new Vector2(Mathf.Lerp(0f, 4f, h), 0f);
                accent.color = new Color(UIKit.Accent.r, UIKit.Accent.g, UIKit.Accent.b, h);
            }
            if (label)
            {
                Color tc = style == UIKit.Style.Primary ? new Color(0.08f, 0.07f, 0.05f) : Color.Lerp(UIKit.TextC * new Color(1, 1, 1, 0.85f), selected ? UIKit.Accent : Color.white, h);
                if (!on) tc.a *= 0.35f;
                label.color = tc;
                if (label.alignment == TextAnchor.MiddleLeft)
                {
                    if (!labelBase.HasValue) labelBase = label.rectTransform.anchoredPosition;
                    label.rectTransform.anchoredPosition = labelBase.Value + new Vector2(h * 6f, 0f);
                }
            }
        }
    }
}
