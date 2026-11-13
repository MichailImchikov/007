using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Ability slot (bottom center): icon, name, cooldown sweep, active-duration bar; pickup prompt;
    /// center messages; full-screen overlays for blindness, gas clouds, ghost / invisibility / chicken tints.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public partial class AbilityHUD : MonoBehaviour
    {
        public static AbilityHUD Instance { get; private set; }

        AbilityUser user;
        Font font;
        CanvasGroup slotGroup, msgGroup, promptGroup;
        Image iconBg, cdFill, durBack, durFill, blindImg, tintImg, smokeImg;
        Text glyph, nameText, infoText, msgTitle, msgSub, promptText;
        float msgT = 99f;
        Sprite white;

        public static AbilityHUD Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("AbilityHUD").AddComponent<AbilityHUD>();
            return Instance;
        }

        public void Bind(AbilityUser u)
        {
            if (user) user.Message -= OnMessage;
            user = u;
            user.Message += OnMessage;
        }

        void OnDestroy()
        {
            if (user) user.Message -= OnMessage;
            if (Instance == this) Instance = null;
        }

        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 14;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var tex = Texture2D.whiteTexture;
            white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            Build();
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

        RectTransform Full(string name)
        {
            var rt = Rect(name, transform, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
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

        Text Txt(RectTransform rt, int size, TextAnchor a, Color c, bool bold = false)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = c; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            if (bold) t.fontStyle = FontStyle.Bold;
            var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.75f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        static Sprite VignetteSprite()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float a = Mathf.Clamp01((Mathf.Sqrt(dx * dx + dy * dy) - 0.5f) / 0.7f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        }

        void Build()
        {
            tintImg = Img(Full("AbilityTint"), new Color(1f, 1f, 1f, 0f), VignetteSprite());
            smokeImg = Img(Full("GasFog"), new Color(0.42f, 0.46f, 0.2f, 0f));
            blindImg = Img(Full("Blind"), new Color(0.93f, 1f, 0.82f, 0f));

            // ---- slot
            var slot = Rect("AbilitySlot", transform, new Vector2(0.5f, 0f), new Vector2(0f, 120f), new Vector2(400f, 84f));
            slotGroup = slot.gameObject.AddComponent<CanvasGroup>();
            Img(slot, new Color(0f, 0f, 0f, 0.45f));
            var ic = Rect("Icon", slot, new Vector2(0f, 0.5f), new Vector2(44f, 0f), new Vector2(70f, 70f));
            iconBg = Img(ic, Color.gray);
            glyph = Txt(Rect("Glyph", ic, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, 70f)), 46, TextAnchor.MiddleCenter, Color.white, true);
            var cd = Rect("Cooldown", ic, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(70f, 70f));
            cdFill = Img(cd, new Color(0f, 0f, 0f, 0.7f));
            cdFill.type = Image.Type.Filled;
            cdFill.fillMethod = Image.FillMethod.Radial360;
            cdFill.fillOrigin = (int)Image.Origin360.Top;
            cdFill.fillClockwise = false;
            var nr = Rect("Name", slot, new Vector2(0f, 0.5f), new Vector2(250f, 14f), new Vector2(300f, 34f));
            nameText = Txt(nr, 26, TextAnchor.MiddleLeft, Color.white, true);
            var ir = Rect("Info", slot, new Vector2(0f, 0.5f), new Vector2(250f, -18f), new Vector2(300f, 26f));
            infoText = Txt(ir, 19, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.75f));
            var db = Rect("DurBack", slot, new Vector2(0.5f, 0f), new Vector2(0f, -6f), new Vector2(400f, 6f));
            durBack = Img(db, new Color(0f, 0f, 0f, 0.5f));
            var df = Rect("DurFill", db, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 6f));
            durFill = Img(df, Color.white);
            durFill.type = Image.Type.Filled;
            durFill.fillMethod = Image.FillMethod.Horizontal;

            // ---- prompt
            var pr = Rect("AbilityPrompt", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(760f, 44f));
            promptGroup = pr.gameObject.AddComponent<CanvasGroup>();
            Img(pr, new Color(0f, 0f, 0f, 0.45f));
            promptText = Txt(Rect("Text", pr, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 44f)), 24, TextAnchor.MiddleCenter, Color.white, true);
            promptText.supportRichText = true;
            promptGroup.alpha = 0f;

            // ---- center message
            var msg = Rect("AbilityMessage", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 230f), new Vector2(1200f, 110f));
            msgGroup = msg.gameObject.AddComponent<CanvasGroup>();
            msgTitle = Txt(Rect("Title", msg, new Vector2(0.5f, 0.5f), new Vector2(0f, 22f), new Vector2(1200f, 60f)), 46, TextAnchor.MiddleCenter, Color.white, true);
            msgSub = Txt(Rect("Sub", msg, new Vector2(0.5f, 0.5f), new Vector2(0f, -26f), new Vector2(1200f, 36f)), 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.9f));
            msgGroup.alpha = 0f;

            BuildExtra();
        }

        void OnMessage(string title, string sub, Color c)
        {
            msgTitle.text = title;
            msgTitle.color = c;
            msgSub.text = sub;
            msgT = 0f;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            msgT += dt;
            msgGroup.alpha = msgT < 0.15f ? msgT / 0.15f : 1f - Mathf.Clamp01((msgT - 2.8f) / 0.6f);
            UpdateExtra(dt);
            if (!user) { slotGroup.alpha = 0f; return; }

            // ---- slot
            var type = user.Type;
            if (type == AbilityType.None)
            {
                slotGroup.alpha = 0.55f;
                iconBg.color = new Color(0.25f, 0.25f, 0.27f, 0.8f);
                glyph.text = "—";
                nameText.text = "Нет способности";
                infoText.text = "Ищите светящиеся предметы на карте";
                cdFill.fillAmount = 0f;
            }
            else
            {
                var info = AbilityDB.Get(user.Shown);
                slotGroup.alpha = 1f;
                bool ready = user.Cooldown <= 0f;
                iconBg.color = ready ? info.color : info.color * new Color(0.55f, 0.55f, 0.55f, 1f);
                glyph.text = info.glyph;
                nameText.text = info.name;
                nameText.color = Color.Lerp(Color.white, info.color, 0.35f);
                string uses = user.UsesLeft > 0 ? $"   x{user.UsesLeft}" : "";
                infoText.text = user.Building ? $"строим: осталось {user.BuildLeft}   <b>[V]</b> — выйти"
                              : ready ? "<b>[V]</b> — готово" + uses : $"перезарядка {user.Cooldown:0.0} с" + uses;
                infoText.supportRichText = true;
                cdFill.fillAmount = ready ? 0f : Mathf.Clamp01(user.Cooldown / Mathf.Max(0.01f, user.CooldownTotal));
                // a ready ability pulses gently
                float pulse = ready ? 1f + Mathf.Sin(Time.unscaledTime * 5f) * 0.04f : 1f;
                iconBg.rectTransform.localScale = Vector3.one * pulse;
            }
            float act = user.ActiveFraction;
            durBack.enabled = durFill.enabled = act >= 0f;
            if (act >= 0f) { durFill.fillAmount = act; durFill.color = user.ActiveColor; }

            // ---- pickup prompt
            var p = user.NearbyPickup;
            if (p && user.Type != AbilityType.None)
            {
                promptText.text = $"<color=#ffd75a>[X]</color> Взять «{p.Label}» вместо «{AbilityDB.Get(user.Shown).name}»";
                promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, 1f, dt * 8f);
            }
            else promptGroup.alpha = Mathf.MoveTowards(promptGroup.alpha, 0f, dt * 8f);

            // ---- overlays
            blindImg.color = new Color(0.93f, 1f, 0.82f, Mathf.SmoothStep(0f, 1f, user.BlindAmount) * 0.98f);
            float gas = 0f;
            if (PlayerHealth.Local && PlayerHealth.Local.playerCam) gas = SmokeCloud.Density(PlayerHealth.Local.playerCam.cam.transform.position);
            smokeImg.color = new Color(0.42f, 0.46f, 0.2f, Mathf.MoveTowards(smokeImg.color.a, gas * 0.85f, dt * 2f));

            Color tint = new Color(1f, 1f, 1f, 0f);
            if (user.IsChicken) tint = new Color(1f, 0.85f, 0.3f, 0.45f);
            else if (user.Confused) tint = new Color(1f, 0.55f, 0.1f, 0.35f + Mathf.Sin(Time.unscaledTime * 8f) * 0.1f);
            else if (user.Shrunk) tint = new Color(0.45f, 0.95f, 0.4f, 0.25f);
            else if (user.Building) tint = new Color(0.85f, 0.6f, 0.3f, 0.2f);
            else if (user.Phasing) tint = new Color(0.65f, 0.35f, 1f, 0.6f + Mathf.Sin(Time.unscaledTime * 6f) * 0.1f);
            else if (user.Invisible) tint = new Color(0.35f, 1f, 0.95f, 0.38f);
            else if (user.Parrying) tint = new Color(0.6f, 0.82f, 1f, 0.25f);
            tintImg.color = Color.Lerp(tintImg.color, tint, 1f - Mathf.Exp(-8f * dt));
        }
    }
}
