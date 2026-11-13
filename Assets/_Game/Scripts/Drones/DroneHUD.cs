using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Drone war HUD. On foot: the drone ability slot, red screen edges when an enemy drone is near
    /// (stronger on the side it is on) and the revealed operator areas. While flying: an FPV OSD
    /// (speed, altitude, battery, link, heading, horizon, ammo, thermal, warnings), video noise that
    /// grows as the link weakens, and the same danger edges for the operator's body.
    /// </summary>
    [DefaultExecutionOrder(210)]
    public class DroneHUD : MonoBehaviour
    {
        public const float DangerRange = 38f;

        static DroneHUD instance;

        Font font;
        RectTransform root;
        CanvasGroup footGroup, osdGroup, blackGroup;
        RawImage edgeL, edgeR, edgeT, edgeB, noise, scan, vignette;
        Text dangerText, noticeText, revealedText;
        // foot slot
        Text slotTitle, slotKey, slotState;
        Image slotFill, slotFrame;
        // OSD
        Text tMode, tTimer, tHeading, tLink, tHome, tSpeed, tAlt, tBattery, tAmmo, tThermal, tWarn, tHints, tWarhead, tVSpeed;
        Image batteryFill, linkFill, horizonL, horizonR;
        RectTransform horizon, crossRoot, hitRoot;
        readonly Image[] hitLines = new Image[4];
        float hitT = 9f; bool hitKill;
        float noticeT = 9f, revealedT = 9f, alarmT;
        AudioSource alarmSrc;
        DroneOperator boundOp;

        class Marker { public RectTransform rt; public RawImage ring; public Text label; public Image dot; }
        readonly List<Marker> markers = new List<Marker>();

        public static void Hitmarker(bool kill)
        {
            if (!instance) return;
            instance.hitT = 0f;
            instance.hitKill = kill;
        }

        void Awake()
        {
            instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 22;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            root = (RectTransform)transform;
            alarmSrc = gameObject.AddComponent<AudioSource>();
            alarmSrc.playOnAwake = false;
            alarmSrc.spatialBlend = 0f;
            Build();
            DroneMode.Revealed += OnRevealed;
        }

        void OnDestroy()
        {
            DroneMode.Revealed -= OnRevealed;
            if (boundOp) boundOp.Notice -= OnNotice;
            if (instance == this) instance = null;
        }

        // ------------------------------------------------------------------ building

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

        RectTransform Full(string name, Transform parent)
        {
            var rt = R(name, parent, Vector2.zero, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        Text T(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fs, TextAnchor align, Color c, bool bold = true)
        {
            var rt = R("Text", parent, anchor, pos, size);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = fs; t.alignment = align; t.color = c; t.raycastTarget = false;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0f, 0f, 0f, 0.85f); o.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        Image I(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var rt = R("Img", parent, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c; img.raycastTarget = false;
            return img;
        }

        RawImage Raw(Transform parent, Texture tex, Color c)
        {
            var rt = Full("Raw", parent);
            var r = rt.gameObject.AddComponent<RawImage>();
            r.texture = tex; r.color = c; r.raycastTarget = false;
            return r;
        }

        void Build()
        {
            var white = new Color(0.95f, 0.97f, 1f, 0.95f);
            var amber = new Color(1f, 0.78f, 0.3f, 1f);

            // ---- OSD (drawn first so the danger edges sit on top of the video)
            var osd = Full("OSD", root);
            osdGroup = osd.gameObject.AddComponent<CanvasGroup>();
            osdGroup.alpha = 0f; osdGroup.blocksRaycasts = false;
            vignette = Raw(osd, RadialTex(), new Color(0f, 0f, 0f, 0.55f));
            scan = Raw(osd, ScanTex(), new Color(1f, 1f, 1f, 0.06f));
            noise = Raw(osd, NoiseTex(), new Color(1f, 1f, 1f, 0.05f));

            horizon = R("Horizon", osd, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
            horizonL = I(horizon, new Vector2(0.5f, 0.5f), new Vector2(-150f, 0f), new Vector2(200f, 2.5f), new Color(1f, 1f, 1f, 0.55f));
            horizonR = I(horizon, new Vector2(0.5f, 0.5f), new Vector2(150f, 0f), new Vector2(200f, 2.5f), new Color(1f, 1f, 1f, 0.55f));

            crossRoot = R("Cross", osd, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
            I(crossRoot, new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(3f, 3f), white);
            I(crossRoot, new Vector2(0.5f, 0.5f), new Vector2(-16f, 0f), new Vector2(14f, 2f), white);
            I(crossRoot, new Vector2(0.5f, 0.5f), new Vector2(16f, 0f), new Vector2(14f, 2f), white);
            I(crossRoot, new Vector2(0.5f, 0.5f), new Vector2(0f, -16f), new Vector2(2f, 14f), white);
            hitRoot = R("Hit", osd, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60f, 60f));
            for (int i = 0; i < 4; i++)
            {
                hitLines[i] = I(hitRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 3f), Color.white);
                float a = 45f + 90f * i;
                hitLines[i].rectTransform.anchoredPosition = Quaternion.Euler(0f, 0f, a) * new Vector3(15f, 0f, 0f);
                hitLines[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, a);
            }

            tMode = T(osd, new Vector2(0f, 1f), new Vector2(190f, -46f), new Vector2(300f, 36f), 28, TextAnchor.MiddleLeft, white);
            tTimer = T(osd, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(300f, 34f), 28, TextAnchor.MiddleCenter, white);
            tHeading = T(osd, new Vector2(0.5f, 1f), new Vector2(0f, -76f), new Vector2(300f, 30f), 24, TextAnchor.MiddleCenter, white, false);
            tLink = T(osd, new Vector2(1f, 1f), new Vector2(-190f, -46f), new Vector2(300f, 34f), 26, TextAnchor.MiddleRight, white);
            linkFill = I(osd, new Vector2(1f, 1f), new Vector2(-120f, -74f), new Vector2(160f, 6f), white);
            linkFill.rectTransform.pivot = new Vector2(1f, 0.5f); linkFill.rectTransform.anchoredPosition = new Vector2(-40f, -74f);
            tHome = T(osd, new Vector2(1f, 1f), new Vector2(-190f, -102f), new Vector2(340f, 30f), 22, TextAnchor.MiddleRight, white, false);

            tSpeed = T(osd, new Vector2(0.5f, 0.5f), new Vector2(-330f, 0f), new Vector2(220f, 40f), 34, TextAnchor.MiddleRight, white);
            tAlt = T(osd, new Vector2(0.5f, 0.5f), new Vector2(330f, 0f), new Vector2(220f, 40f), 34, TextAnchor.MiddleLeft, white);
            tVSpeed = T(osd, new Vector2(0.5f, 0.5f), new Vector2(330f, -36f), new Vector2(220f, 30f), 22, TextAnchor.MiddleLeft, white, false);

            tBattery = T(osd, new Vector2(0f, 0f), new Vector2(190f, 120f), new Vector2(300f, 36f), 28, TextAnchor.MiddleLeft, white);
            batteryFill = I(osd, new Vector2(0f, 0f), new Vector2(40f, 94f), new Vector2(200f, 8f), white);
            batteryFill.rectTransform.pivot = new Vector2(0f, 0.5f); batteryFill.rectTransform.anchoredPosition = new Vector2(40f, 94f);
            tAmmo = T(osd, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(400f, 34f), 28, TextAnchor.MiddleCenter, white);
            tThermal = T(osd, new Vector2(0.5f, 0f), new Vector2(0f, 114f), new Vector2(520f, 30f), 24, TextAnchor.MiddleCenter, white);
            tWarhead = T(osd, new Vector2(1f, 0f), new Vector2(-190f, 120f), new Vector2(340f, 34f), 26, TextAnchor.MiddleRight, amber);
            tWarn = T(osd, new Vector2(0.5f, 0.5f), new Vector2(0f, 190f), new Vector2(900f, 40f), 32, TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.25f));
            tHints = T(osd, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(1800f, 26f), 18, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.55f), false);

            // ---- revealed operator markers (both views)
            var marks = Full("Reveals", root);
            for (int i = 0; i < 6; i++)
            {
                var m = new Marker();
                m.rt = R("Mark", marks, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));
                var rr = R("Ring", m.rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100f, 100f));
                m.ring = rr.gameObject.AddComponent<RawImage>(); m.ring.texture = RingTex(); m.ring.raycastTarget = false;
                m.dot = I(m.rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f), Color.white);
                m.dot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                m.label = T(m.rt, new Vector2(0.5f, 0.5f), new Vector2(0f, 34f), new Vector2(500f, 30f), 22, TextAnchor.MiddleCenter, Color.white);
                m.rt.gameObject.SetActive(false);
                markers.Add(m);
            }

            // ---- danger edges
            var edgeTex = EdgeTex();
            edgeL = Raw(root, edgeTex, new Color(1f, 0.05f, 0.03f, 0f));
            edgeL.rectTransform.anchorMax = new Vector2(0.24f, 1f);
            edgeR = Raw(root, edgeTex, new Color(1f, 0.05f, 0.03f, 0f));
            edgeR.rectTransform.anchorMin = new Vector2(0.76f, 0f); edgeR.uvRect = new Rect(1f, 0f, -1f, 1f);
            edgeT = Raw(root, edgeTex, new Color(1f, 0.05f, 0.03f, 0f));
            edgeT.rectTransform.anchorMin = new Vector2(0f, 0.86f); edgeT.rectTransform.localRotation = Quaternion.identity;
            edgeT.uvRect = new Rect(0f, 0f, 1f, 1f);
            edgeB = Raw(root, edgeTex, new Color(1f, 0.05f, 0.03f, 0f));
            edgeB.rectTransform.anchorMax = new Vector2(1f, 0.14f);
            edgeT.texture = EdgeTexV(true); edgeB.texture = EdgeTexV(false);
            dangerText = T(root, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(700f, 40f), 30, TextAnchor.MiddleCenter, new Color(1f, 0.3f, 0.25f, 0f));

            // ---- on-foot drone slot
            var foot = Full("Foot", root);
            footGroup = foot.gameObject.AddComponent<CanvasGroup>();
            footGroup.blocksRaycasts = false;
            slotFrame = I(foot, new Vector2(1f, 0f), new Vector2(-170f, 250f), new Vector2(250f, 74f), new Color(0.05f, 0.06f, 0.07f, 0.72f));
            var srt = slotFrame.rectTransform;
            slotFill = I(srt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(250f, 5f), amber);
            slotFill.rectTransform.pivot = Vector2.zero; slotFill.rectTransform.anchoredPosition = Vector2.zero;
            slotKey = T(srt, new Vector2(0f, 0.5f), new Vector2(34f, 2f), new Vector2(52f, 44f), 26, TextAnchor.MiddleCenter, amber);
            slotTitle = T(srt, new Vector2(0f, 0.5f), new Vector2(150f, 14f), new Vector2(170f, 30f), 24, TextAnchor.MiddleLeft, white);
            slotState = T(srt, new Vector2(0f, 0.5f), new Vector2(150f, -14f), new Vector2(170f, 26f), 19, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f), false);
            slotTitle.text = "FPV-ДРОН";

            noticeText = T(root, new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(900f, 36f), 28, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0f));
            revealedText = T(root, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1000f, 36f), 28, TextAnchor.MiddleCenter, new Color(1f, 0.75f, 0.3f, 0f));

            // ---- black flash between views
            var blk = Raw(root, Texture2D.whiteTexture, Color.black);
            blackGroup = blk.gameObject.AddComponent<CanvasGroup>();
            blackGroup.alpha = 0f; blackGroup.blocksRaycasts = false;
        }

        // ------------------------------------------------------------------ textures

        static Texture2D EdgeTex()
        {
            var t = new Texture2D(64, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 64; x++)
            {
                float a = Mathf.Pow(1f - x / 63f, 2.2f);
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }

        static Texture2D EdgeTexV(bool top)
        {
            var t = new Texture2D(4, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
            {
                float u = y / 63f;
                float a = Mathf.Pow(top ? u : 1f - u, 2.4f) * 0.6f;
                for (int x = 0; x < 4; x++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }

        static Texture2D NoiseTex()
        {
            var t = new Texture2D(256, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var rnd = new System.Random(77);
            var px = new Color32[256 * 256];
            for (int y = 0; y < 256; y++)
            {
                // analog breakup: whole lines brighter / darker
                float line = (float)rnd.NextDouble() < 0.06 ? 0.6f : 0f;
                for (int x = 0; x < 256; x++)
                {
                    byte v = (byte)Mathf.Clamp((float)rnd.NextDouble() * 255f + line * 120f, 0f, 255f);
                    px[y * 256 + x] = new Color32(v, v, v, (byte)(120 + rnd.Next(135)));
                }
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static Texture2D ScanTex()
        {
            var t = new Texture2D(1, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            t.SetPixel(0, 0, new Color(0f, 0f, 0f, 1f)); t.SetPixel(0, 1, new Color(0f, 0f, 0f, 0f));
            t.SetPixel(0, 2, new Color(1f, 1f, 1f, 0.4f)); t.SetPixel(0, 3, new Color(0f, 0f, 0f, 0f));
            t.Apply();
            return t;
        }

        static Texture2D RadialTex()
        {
            const int S = 64;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x / (S - 1f) - 0.5f) * 2f, dy = (y / (S - 1f) - 0.5f) * 2f;
                    float d = Mathf.Sqrt(dx * dx * 0.8f + dy * dy);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((d - 0.7f) / 0.6f)));
                }
            t.Apply();
            return t;
        }

        static Texture2D ringTex;
        static Texture2D RingTex()
        {
            if (ringTex) return ringTex;
            const int S = 128;
            ringTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x / (S - 1f) - 0.5f, dy = y / (S - 1f) - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float ring = Mathf.Exp(-Mathf.Pow((d - 0.93f) / 0.035f, 2f));
                    float fill = d < 0.95f ? 0.16f * d : 0f;
                    ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(ring + fill)));
                }
            ringTex.Apply();
            return ringTex;
        }

        // ------------------------------------------------------------------ events

        void OnNotice(string s)
        {
            noticeText.text = s;
            noticeT = 0f;
        }

        void OnRevealed(DroneMode.RevealMark m)
        {
            var me = PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null;
            if (m.owner == me) revealedText.text = "ВАШ ДРОН СБИТ — ПОЛОЖЕНИЕ РАСКРЫТО";
            else if (m.by == me) revealedText.text = "ДРОН СБИТ — ОПЕРАТОР «" + m.owner.displayName + "» ОТМЕЧЕН";
            else revealedText.text = "ОПЕРАТОР ДРОНА «" + m.owner.displayName + "» ОБНАРУЖЕН";
            revealedT = 0f;
            ProcAudio.Play(Sfx.AbilityReady, 0.45f);
        }

        // ------------------------------------------------------------------ update

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var op = DroneOperator.Local;
            if (op != boundOp)
            {
                if (boundOp) boundOp.Notice -= OnNotice;
                boundOp = op;
                if (op) op.Notice += OnNotice;
            }
            var ph = PlayerHealth.Local;
            var drone = op ? op.Drone : null;
            bool flying = drone && drone.IsLocalView && !drone.Dead;
            Camera cam = flying ? drone.Cam : (ph && ph.playerCam ? ph.playerCam.cam : Camera.main);

            osdGroup.alpha = flying ? 1f : 0f;
            footGroup.alpha = !flying && ph && !ph.IsDead ? 1f : 0f;
            float since = op ? Time.unscaledTime - op.ViewSwitchTime : 9f;
            blackGroup.alpha = Mathf.Clamp01(1f - since / 0.35f);

            UpdateFoot(op);
            UpdateDanger(ph, cam, flying);
            UpdateMarkers(cam);
            if (flying) UpdateOSD(drone, op, dt);
            else alarmT = 0f;

            noticeT += dt;
            noticeText.color = new Color(1f, 1f, 1f, Mathf.Clamp01(2.6f - noticeT));
            revealedT += dt;
            var rc = revealedText.color; rc.a = Mathf.Clamp01(3.5f - revealedT) * (0.75f + 0.25f * Mathf.Sin(revealedT * 12f));
            revealedText.color = rc;
        }

        void UpdateFoot(DroneOperator op)
        {
            if (!op) { footGroup.alpha = 0f; return; }
            slotKey.text = GameSettings.KeyName(BindAction.Ability);
            if (op.Drone) { slotState.text = "в полёте"; slotFill.rectTransform.sizeDelta = new Vector2(250f, 5f); }
            else if (op.CooldownLeft > 0f)
            {
                slotState.text = "готов через " + Mathf.CeilToInt(op.CooldownLeft) + " с";
                slotFill.rectTransform.sizeDelta = new Vector2(250f * (1f - op.CooldownLeft / DroneOperator.Cooldown), 5f);
                slotFill.color = new Color(1f, 0.78f, 0.3f, 0.55f);
                slotTitle.color = new Color(1f, 1f, 1f, 0.5f);
            }
            else
            {
                slotState.text = "готов к запуску";
                slotFill.rectTransform.sizeDelta = new Vector2(250f, 5f);
                slotFill.color = new Color(1f, 0.78f, 0.3f, 1f);
                slotTitle.color = Color.white;
            }
        }

        void UpdateDanger(PlayerHealth ph, Camera cam, bool flying)
        {
            float lvl = 0f, side = 0f;
            var me = ph ? ph.GetComponent<Combatant>() : null;
            if (me && me.alive)
            {
                Vector3 body = me.Chest;
                // the side is judged from where the body faces (the player camera), also while flying
                Transform view = ph.playerCam && ph.playerCam.cam ? ph.playerCam.cam.transform : ph.transform;
                foreach (var d in Drone.All)
                {
                    if (!d || d.Dead || !d.Owner || d.Owner == me || !me.IsEnemyOf(d.Owner)) continue;
                    Vector3 to = d.transform.position - body;
                    float dist = to.magnitude;
                    if (dist > DangerRange) continue;
                    float l = Mathf.Pow(1f - dist / DangerRange, 0.75f);
                    if (l > lvl) { lvl = l; side = dist > 0.1f ? Vector3.Dot(view.right, to / dist) : 0f; }
                }
            }
            float pulse = lvl > 0f ? 0.72f + 0.28f * Mathf.Sin(Time.unscaledTime * (5f + lvl * 11f)) : 0f;
            float a = lvl * pulse;
            edgeL.color = new Color(1f, 0.05f, 0.03f, a * (0.45f + 0.55f * Mathf.Clamp01(1f - side * 1.4f)));
            edgeR.color = new Color(1f, 0.05f, 0.03f, a * (0.45f + 0.55f * Mathf.Clamp01(1f + side * 1.4f)));
            edgeT.color = edgeB.color = new Color(1f, 0.05f, 0.03f, a * 0.5f);
            dangerText.text = flying ? "ВРАЖЕСКИЙ ДРОН У ОПЕРАТОРА" : "ВРАЖЕСКИЙ ДРОН РЯДОМ";
            dangerText.color = new Color(1f, 0.3f, 0.25f, lvl > 0f ? Mathf.Clamp01(0.4f + lvl) * pulse : 0f);
        }

        void UpdateMarkers(Camera cam)
        {
            int used = 0;
            var me = PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null;
            if (cam)
            {
                Vector2 scale = new Vector2(root.rect.width / Mathf.Max(1, Screen.width), root.rect.height / Mathf.Max(1, Screen.height));
                foreach (var r in DroneMode.Reveals)
                {
                    if (used >= markers.Count) break;
                    if (!r.owner || r.owner == me) continue;
                    var m = markers[used++];
                    m.rt.gameObject.SetActive(true);
                    Vector3 sp = cam.WorldToScreenPoint(r.center + Vector3.up * 1f);
                    bool behind = sp.z < 0.1f;
                    Vector2 p = new Vector2(sp.x, sp.y);
                    if (behind) p = new Vector2(Screen.width - p.x, 0f);
                    p.x = Mathf.Clamp(p.x, 60f, Screen.width - 60f);
                    p.y = Mathf.Clamp(p.y, 60f, Screen.height - 60f);
                    m.rt.anchoredPosition = new Vector2((p.x - Screen.width * 0.5f) * scale.x, (p.y - Screen.height * 0.5f) * scale.y);
                    float dist = Vector3.Distance(cam.transform.position, r.center);
                    float px = behind ? 0f : r.radius / Mathf.Max(1f, sp.z) * (Screen.height * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * scale.y;
                    float size = Mathf.Clamp(px * 2f, 50f, 1100f);
                    m.ring.rectTransform.sizeDelta = new Vector2(size, size * 0.55f);
                    float life = Mathf.Clamp01((r.until - Time.time) / 1.5f) * Mathf.Clamp01((Time.time - r.born) * 4f);
                    float blink = 0.75f + 0.25f * Mathf.Sin(Time.time * 7f);
                    var col = new Color(1f, 0.45f, 0.15f);
                    m.ring.color = new Color(col.r, col.g, col.b, 0.85f * life * blink);
                    m.dot.color = new Color(col.r, col.g, col.b, life);
                    m.label.text = "ОПЕРАТОР · " + r.owner.displayName + " · " + Mathf.RoundToInt(dist) + " м";
                    m.label.color = new Color(1f, 0.85f, 0.6f, life);
                    m.label.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Min(size * 0.3f, 200f) + 26f);
                }
            }
            for (int i = used; i < markers.Count; i++) markers[i].rt.gameObject.SetActive(false);
        }

        static string Compass(float deg)
        {
            string[] n = { "С", "СВ", "В", "ЮВ", "Ю", "ЮЗ", "З", "СЗ" };
            return n[Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / 45f) % 8];
        }

        void UpdateOSD(Drone d, DroneOperator op, float dt)
        {
            var cam = d.Cam;
            Vector3 v = d.Velocity;
            bool blink = Mathf.Repeat(Time.unscaledTime * 2f, 1f) < 0.6f;

            string mode = "FPV";
            tMode.text = mode + (d.Sport ? "  УСКОРЕНИЕ" : "");
            int sec = Mathf.FloorToInt(d.Age);
            tTimer.text = (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
            tHeading.text = Compass(d.Heading) + " " + Mathf.RoundToInt(Mathf.Repeat(d.Heading, 360f)).ToString("000") + "°";

            int rssi = Mathf.RoundToInt(d.Signal * 100f);
            tLink.text = "";
            linkFill.enabled = false;
            var owner = d.Owner;
            if (owner)
            {
                Vector3 h = owner.transform.position - d.transform.position;
                float hd = new Vector2(h.x, h.z).magnitude;
                float rel = Mathf.DeltaAngle(d.Heading, Mathf.Atan2(h.x, h.z) * Mathf.Rad2Deg);
                string arrow = Mathf.Abs(rel) < 22f ? "впереди" : Mathf.Abs(rel) > 158f ? "сзади" : rel > 0f ? "справа" : "слева";
                tHome.text = "ДОМ " + Mathf.RoundToInt(hd) + " м " + arrow;
            }

            tSpeed.text = Mathf.RoundToInt(v.magnitude * 3.6f) + " км/ч";
            float agl = 99f;
            if (Physics.Raycast(d.transform.position, Vector3.down, out RaycastHit gh, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) agl = gh.distance;
            agl = Mathf.Min(agl, d.transform.position.y - DroneMode.WaterLevel);
            tAlt.text = "ВЫС " + agl.ToString("0.0") + " м";
            tVSpeed.text = (v.y >= 0f ? "ВВЕРХ " : "ВНИЗ ") + Mathf.Abs(v.y).ToString("0.0") + " м/с";

            tBattery.text = d.Voltage.ToString("0.0") + "В  " + Mathf.RoundToInt(d.Battery * 100f) + "%";
            batteryFill.rectTransform.sizeDelta = new Vector2(200f * d.Battery, 8f);
            batteryFill.color = d.Battery > 0.3f ? new Color(0.6f, 1f, 0.6f, 0.9f) : new Color(1f, 0.35f, 0.25f, blink ? 1f : 0.4f);
            tAmmo.text = d.DropCd > 0f ? "ГРАНАТА: " + d.DropCd.ToString("0.0") + " с" : "ГРАНАТА ГОТОВА  [ЛКМ]";
            string thKey = GameSettings.KeyName(BindAction.Ability);
            if (d.Thermal) tThermal.text = "ТЕПЛОВИЗОР  " + d.ThermalLeft.ToString("0.0") + " с   [" + thKey + "] выкл";
            else if (d.ThermalCd > 0f) tThermal.text = "тепловизор: перезарядка " + Mathf.CeilToInt(d.ThermalCd) + " с";
            else tThermal.text = "[" + thKey + "] тепловизор готов";
            tThermal.color = d.Thermal ? new Color(1f, 0.85f, 0.5f) : new Color(1f, 1f, 1f, d.ThermalCd > 0f ? 0.5f : 0.9f);
            tWarhead.text = d.Armed ? "БЧ ВЗВЕДЕНА  [" + GameSettings.KeyName(BindAction.Aim) + "]" : "МОТОРЫ ВЫКЛ";

            // warnings, most urgent first
            string warn = "";
            bool beep = false;
            if (d.LinkLost) warn = "СВЯЗЬ ПОТЕРЯНА — FAILSAFE";
            else if (d.OutOfBounds) warn = "ГРАНИЦА КАРТЫ";
            else if (d.Battery < 0.15f) { warn = "БАТАРЕЯ РАЗРЯЖЕНА"; beep = true; }
            else if (Time.time - d.OperatorHitT < 2.5f) warn = "ОПЕРАТОР ПОД ОГНЁМ";
            else if (agl < 2.5f && v.y < -1.5f) warn = "ЗЕМЛЯ";
            tWarn.text = warn;
            tWarn.enabled = warn.Length > 0 && blink;
            alarmT -= dt;
            if (beep && alarmT <= 0f) { alarmT = 0.9f; alarmSrc.PlayOneShot(DroneAudio.Alarm, 0.25f * ProcAudio.MasterVolume); }

            // horizon from two far points on the level plane
            Vector3 f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 0.01f)
            {
                f.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, f);
                Vector3 c0 = cam.transform.position + f * 500f;
                Vector3 a = cam.WorldToScreenPoint(c0 - right * 100f), b = cam.WorldToScreenPoint(c0 + right * 100f), c = cam.WorldToScreenPoint(c0);
                if (c.z > 0f)
                {
                    horizon.gameObject.SetActive(true);
                    float sy = root.rect.height / Mathf.Max(1, Screen.height);
                    horizon.anchoredPosition = new Vector2((c.x - Screen.width * 0.5f) * sy, (c.y - Screen.height * 0.5f) * sy);
                    horizon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
                }
                else horizon.gameObject.SetActive(false);
            }

            // video: noise grows as the link weakens or the airframe is hit
            float n = Mathf.Clamp01((0.5f - d.Signal) / 0.5f);
            float na = 0.05f + n * n * 0.85f + d.HitFlash * 0.45f + (d.LinkLost ? 0.9f : 0f);
            noise.color = new Color(1f, 1f, 1f, Mathf.Clamp01(na));
            noise.uvRect = new Rect(Random.value, Random.value, Screen.width / 256f * 0.5f, Screen.height / 256f * 0.5f);
            scan.uvRect = new Rect(0f, Time.unscaledTime * 0.3f, 1f, Screen.height / 3f);
            vignette.color = new Color(0f, 0f, 0f, d.Thermal ? 0.6f : 0.3f);

            hitT += dt;
            float ha = Mathf.Clamp01(1f - hitT / 0.3f);
            for (int i = 0; i < 4; i++) hitLines[i].color = hitKill ? new Color(1f, 0.25f, 0.2f, ha) : new Color(1f, 1f, 1f, ha);
            hitRoot.localScale = Vector3.one * (1f + (1f - ha) * 0.3f);

            string K(BindAction ba) => GameSettings.KeyName(ba);
            tHints.text = $"{K(BindAction.MoveForward)}{K(BindAction.MoveLeft)}{K(BindAction.MoveBack)}{K(BindAction.MoveRight)} — движение   Мышь — взгляд   Пробел — вверх   Ctrl — вниз   {K(BindAction.Sprint)} — ускорение   " +
                          $"ЛКМ — сброс гранаты   {K(BindAction.Aim)} — подрыв   {K(BindAction.Ability)} — тепловизор   {K(BindAction.Grenade)} — сброс связи";
        }
    }
}
