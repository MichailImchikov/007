using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Class and team HUD: the class ability slot (above the pickup ability slot: glyph, name, key, cooldown sweep,
    /// active-time bar; the engineer's rockets), the medic's "[3] Поднять" prompt with its progress, short class messages,
    /// the team score in a team game, and while lying dead in a team game the wait for a medic ("Space — respawn now").
    /// </summary>
    [DefaultExecutionOrder(121)]
    public class ClassHUD : MonoBehaviour
    {
        static ClassHUD inst;
        Font font;
        Sprite white;
        CanvasGroup slotGroup, promptGroup, msgGroup, scoreGroup, reviveGroup;
        Image iconBg, cdFill, durFill, promptFill, scoreA, scoreB;
        Text glyph, nameText, infoText, promptText, msgText, scoreText, reviveText;
        float msgT = 99f;

        public static void Ensure()
        {
            if (inst) return;
            inst = new GameObject("ClassHUD").AddComponent<ClassHUD>();
        }

        void Awake()
        {
            inst = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var tex = Texture2D.whiteTexture;
            white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            Build();
            ClassAbility.Message += OnMessage;
        }

        void OnDestroy()
        {
            ClassAbility.Message -= OnMessage;
            if (inst == this) inst = null;
        }

        RectTransform R(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
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

        Text Txt(RectTransform rt, int size, TextAnchor a, Color c, bool bold = false)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = c; t.raycastTarget = false; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            if (bold) t.fontStyle = FontStyle.Bold;
            var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.75f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        Image Filled(RectTransform rt, Color c, Image.FillMethod m)
        {
            var i = Img(rt, c);
            i.type = Image.Type.Filled; i.fillMethod = m;
            return i;
        }

        void Build()
        {
            // ---- class slot (above the pickup ability slot)
            var slot = R("ClassSlot", transform, new Vector2(0.5f, 0f), new Vector2(0f, 212f), new Vector2(400f, 74f));
            slotGroup = slot.gameObject.AddComponent<CanvasGroup>();
            Img(slot, new Color(0f, 0f, 0f, 0.45f));
            var ic = R("Icon", slot, new Vector2(0f, 0.5f), new Vector2(39f, 0f), new Vector2(62f, 62f));
            iconBg = Img(ic, Color.gray);
            glyph = Txt(R("Glyph", ic, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f)), 40, TextAnchor.MiddleCenter, Color.white, true);
            cdFill = Filled(R("Cooldown", ic, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f)), new Color(0f, 0f, 0f, 0.7f), Image.FillMethod.Radial360);
            cdFill.fillOrigin = (int)Image.Origin360.Top; cdFill.fillClockwise = false;
            nameText = Txt(R("Name", slot, new Vector2(0f, 0.5f), new Vector2(240f, 13f), new Vector2(300f, 32f)), 24, TextAnchor.MiddleLeft, Color.white, true);
            infoText = Txt(R("Info", slot, new Vector2(0f, 0.5f), new Vector2(240f, -16f), new Vector2(300f, 24f)), 18, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.75f));
            var db = R("DurBack", slot, new Vector2(0.5f, 0f), new Vector2(0f, -5f), new Vector2(400f, 5f));
            Img(db, new Color(0f, 0f, 0f, 0.5f));
            durFill = Filled(R("DurFill", db, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 5f)), Color.white, Image.FillMethod.Horizontal);

            // ---- revive prompt (under the crosshair)
            var pr = R("RevivePrompt", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(520f, 52f));
            promptGroup = pr.gameObject.AddComponent<CanvasGroup>();
            Img(pr, new Color(0f, 0f, 0f, 0.5f));
            promptText = Txt(R("Text", pr, new Vector2(0.5f, 0.5f), new Vector2(0f, 5f), new Vector2(520f, 40f)), 24, TextAnchor.MiddleCenter, Color.white, true);
            var pb = R("Progress", pr, new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(500f, 5f));
            promptFill = Filled(pb, new Color(0.35f, 1f, 0.5f), Image.FillMethod.Horizontal);
            promptGroup.alpha = 0f;

            // ---- class message
            var msg = R("ClassMessage", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 170f), new Vector2(1000f, 50f));
            msgGroup = msg.gameObject.AddComponent<CanvasGroup>();
            msgText = Txt(R("Text", msg, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 50f)), 30, TextAnchor.MiddleCenter, Color.white, true);
            msgGroup.alpha = 0f;

            // ---- team score (top centre)
            var sc = R("TeamScore", transform, new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(440f, 46f));
            scoreGroup = sc.gameObject.AddComponent<CanvasGroup>();
            Img(sc, new Color(0f, 0f, 0f, 0.42f));
            scoreA = Img(R("A", sc, new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(8f, 46f)), Teams.Colors[0]);
            scoreB = Img(R("B", sc, new Vector2(1f, 0.5f), new Vector2(-4f, 0f), new Vector2(8f, 46f)), Teams.Colors[1]);
            scoreText = Txt(R("Text", sc, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 46f)), 26, TextAnchor.MiddleCenter, Color.white, true);
            scoreGroup.alpha = 0f;

            // ---- waiting for a medic
            var rv = R("ReviveWait", transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(900f, 80f));
            reviveGroup = rv.gameObject.AddComponent<CanvasGroup>();
            reviveText = Txt(R("Text", rv, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 80f)), 26, TextAnchor.MiddleCenter, Color.white, true);
            reviveGroup.alpha = 0f;
        }

        void OnMessage(string s)
        {
            msgText.text = s;
            var ca = ClassAbility.Local;
            msgText.color = ca ? ca.Info.color : Color.white;
            msgT = 0f;
        }

        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            msgT += dt;
            msgGroup.alpha = msgT < 0.12f ? msgT / 0.12f : 1f - Mathf.Clamp01((msgT - 1.8f) / 0.5f);

            var ph = PlayerHealth.Local;
            var ca = ClassAbility.Local;
            bool hide = !ph || !ca || DroneMode.Active || SkyArena.Active;

            // ---- team score (the match line of MatchHUD shows it; this panel stays for a HUD without that line)
            bool teams = Teams.Active && !MatchManager.Instance;
            scoreGroup.alpha = teams ? 1f : 0f;
            if (teams)
            {
                var me = ph ? ph.GetComponent<Combatant>() : null;
                int my = me && me.team >= 0 ? me.team : 0;
                scoreText.text = "<color=#" + Hex(Teams.Colors[0]) + ">" + Teams.Names[0].ToUpperInvariant() + "  " + Teams.Score(0) + "</color>   :   <color=#" +
                                 Hex(Teams.Colors[1]) + ">" + Teams.Score(1) + "  " + Teams.Names[1].ToUpperInvariant() + "</color>   <size=16>до " + Teams.KillLimit + "</size>";
                scoreA.color = my == 0 ? Teams.Colors[0] : Teams.Colors[0] * 0.6f;
                scoreB.color = my == 1 ? Teams.Colors[1] : Teams.Colors[1] * 0.6f;
            }

            // ---- waiting for a medic
            bool waiting = ph && ph.AwaitingRevive;
            reviveGroup.alpha = waiting ? 1f : 0f;
            if (waiting)
                reviveText.text = "Медик может поднять вас ещё " + Mathf.CeilToInt(ph.RespawnIn) + " с\n<size=20><color=#BBBBBB>[Пробел] — возродиться сейчас</color></size>";

            if (hide || ph.IsDead)
            {
                slotGroup.alpha = 0f;
                promptGroup.alpha = 0f;
                return;
            }

            // ---- class slot
            var info = ca.Info;
            slotGroup.alpha = 1f;
            iconBg.color = info.color * 0.75f;
            glyph.text = info.glyph;
            string key = GameSettings.KeyName(BindAction.ClassAbility);
            nameText.text = info.abilityName;
            if (ca.Class == PlayerClass.Engineer)
            {
                int n = ca.Rockets;
                infoText.text = "<b>[" + key + "]</b> " + (ca.LauncherOut ? "убрать" : "достать") + " · гранат: " + n;
                cdFill.fillAmount = n > 0 ? 0f : 1f;
            }
            else if (ca.Cooldown > 0f)
            {
                infoText.text = "<b>[" + key + "]</b> — через " + Mathf.CeilToInt(ca.Cooldown) + " с";
                cdFill.fillAmount = ca.Cooldown / Mathf.Max(0.01f, ca.CooldownTotal);
            }
            else
            {
                infoText.text = "<b>[" + key + "]</b> — готово";
                cdFill.fillAmount = 0f;
            }
            durFill.fillAmount = ca.ActiveFraction;
            durFill.color = info.color;

            // ---- revive prompt
            var t = ca.PromptTarget;
            promptGroup.alpha = t ? 1f : 0f;
            if (t)
            {
                promptText.text = "<b>[" + key + "]</b> держать — поднять: " + t.displayName;
                promptFill.fillAmount = ca.ReviveProgress;
            }
        }
    }
}
