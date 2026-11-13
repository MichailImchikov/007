using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Player health bar, damage vignette and direction indicators, death screen;
    /// in Deathmatch also timer / score, kill feed, Tab scoreboard and end-of-match results.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class MatchHUD : MonoBehaviour
    {
        public static MatchHUD Instance { get; private set; }

        Canvas canvas;
        Font font;
        Image healthFill, healthBack, vignette, deathOverlay, protectImg;
        Text healthText, timerText, scoreText, deathTitle, deathSub, endTitle, endSub, boardText, boardTitle, boardKills, boardDeaths;
        RectTransform feedRoot, board, endPanel;
        CanvasGroup deathGroup, boardGroup, endGroup;
        float vignetteAlpha, healthShown = 1f, deathAlpha;

        class Indicator { public RectTransform rt; public Image img; public Vector3 src; public float life; }
        readonly List<Indicator> indicators = new List<Indicator>();
        class FeedLine { public Text text; public float born; }
        readonly List<FeedLine> feed = new List<FeedLine>();
        PlayerHealth hooked;
        MatchManager hookedMatch;

        public static MatchHUD Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("MatchHUD").AddComponent<MatchHUD>();
            return Instance;
        }

        void Awake()
        {
            Instance = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            Build();
        }

        RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        Image Img(RectTransform rt, Color c, Texture2D tex = null)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.color = c; i.raycastTarget = false;
            if (tex) i.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return i;
        }

        Text Txt(RectTransform rt, int size, TextAnchor a, Color c, bool bold = false)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font; t.fontSize = size; t.alignment = a; t.color = c; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            if (bold) t.fontStyle = FontStyle.Bold;
            var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.7f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        static Texture2D VignetteTex()
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx * 0.8f + dy * dy * 0.8f);
                    float a = Mathf.Clamp01((r - 0.45f) / 0.6f);
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        static Texture2D ArrowTex()
        {
            const int W = 64, H = 32;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H;
                    float edge = 1f - Mathf.Abs(u);            // arc-like wedge
                    float a = Mathf.Clamp01((edge * 0.9f - (1f - v)) * 4f) * Mathf.Clamp01(v * 3f);
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        void Build()
        {
            var full = Vector2.zero; var one = Vector2.one; var c = new Vector2(0.5f, 0.5f);
            vignette = Img(Rect("DamageVignette", transform, full, one, c, Vector2.zero, Vector2.zero), new Color(0.8f, 0.05f, 0.02f, 0f), VignetteTex());

            // health
            var hp = Rect("Health", transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40f, 250f), new Vector2(320f, 18f));
            healthBack = Img(hp, new Color(0f, 0f, 0f, 0.5f));
            var fill = Rect("Fill", hp, Vector2.zero, new Vector2(1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
            fill.offsetMin = new Vector2(3f, 3f); fill.offsetMax = new Vector2(-3f, -3f);
            healthFill = Img(fill, new Color(0.9f, 0.95f, 0.9f, 0.95f));
            healthText = Txt(Rect("HP", hp, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(0f, 6f), new Vector2(200f, 40f)), 34, TextAnchor.LowerLeft, Color.white, true);
            protectImg = Img(Rect("Protect", hp, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 8f), new Vector2(120f, 24f)), new Color(0.3f, 0.7f, 1f, 0f));

            // match info
            timerText = Txt(Rect("Timer", transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(300f, 50f)), 40, TextAnchor.UpperCenter, Color.white, true);
            scoreText = Txt(Rect("Score", transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(700f, 30f)), 20, TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.85f));
            feedRoot = Rect("KillFeed", transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -80f), new Vector2(600f, 300f));

            // death screen
            var death = Rect("Death", transform, full, one, c, Vector2.zero, Vector2.zero);
            deathGroup = death.gameObject.AddComponent<CanvasGroup>(); deathGroup.alpha = 0f; deathGroup.blocksRaycasts = false;
            deathOverlay = Img(death, new Color(0.15f, 0f, 0f, 0.45f));
            deathTitle = Txt(Rect("Title", death, c, c, c, new Vector2(0f, 80f), new Vector2(900f, 90f)), 72, TextAnchor.MiddleCenter, new Color(1f, 0.3f, 0.25f), true);
            deathTitle.text = "ВЫ УБИТЫ";
            deathSub = Txt(Rect("Sub", death, c, c, c, new Vector2(0f, 10f), new Vector2(900f, 60f)), 28, TextAnchor.MiddleCenter, Color.white);

            // scoreboard
            board = Rect("Scoreboard", transform, c, c, c, Vector2.zero, new Vector2(760f, 620f));
            boardGroup = board.gameObject.AddComponent<CanvasGroup>(); boardGroup.alpha = 0f; boardGroup.blocksRaycasts = false;
            Img(board, new Color(0.03f, 0.04f, 0.05f, 0.82f));
            boardTitle = Txt(Rect("Title", board, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(700f, 50f)), 34, TextAnchor.UpperCenter, Color.white, true);
            boardText = Txt(Rect("Names", board, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -90f), new Vector2(400f, 520f)), 26, TextAnchor.UpperLeft, Color.white);
            boardKills = Txt(Rect("Kills", board, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(520f, -90f), new Vector2(140f, 520f)), 26, TextAnchor.UpperCenter, Color.white);
            boardDeaths = Txt(Rect("Deaths", board, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(650f, -90f), new Vector2(140f, 520f)), 26, TextAnchor.UpperCenter, Color.white);
            boardText.lineSpacing = boardKills.lineSpacing = boardDeaths.lineSpacing = 1.25f;

            // end of match
            endPanel = Rect("End", transform, full, one, c, Vector2.zero, Vector2.zero);
            endGroup = endPanel.gameObject.AddComponent<CanvasGroup>(); endGroup.alpha = 0f; endGroup.blocksRaycasts = false;
            Img(endPanel, new Color(0f, 0f, 0f, 0.55f));
            endTitle = Txt(Rect("Title", endPanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1000f, 90f)), 70, TextAnchor.UpperCenter, new Color(1f, 0.85f, 0.3f), true);
            endSub = Txt(Rect("Sub", endPanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1000f, 40f)), 26, TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.8f));

            for (int i = 0; i < 6; i++)
            {
                var rt = Rect("DamageArrow", transform, c, c, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(150f, 60f));
                var img = Img(rt, new Color(1f, 0.15f, 0.1f, 0f), ArrowTex());
                indicators.Add(new Indicator { rt = rt, img = img, life = 0f });
            }
        }

        void Hook()
        {
            var ph = PlayerHealth.Local;
            if (ph && ph != hooked) { hooked = ph; ph.Damaged += OnDamaged; }
            var mm = MatchManager.Instance;
            if (mm && mm != hookedMatch) { hookedMatch = mm; mm.Killed += OnKill; }
        }

        void OnDestroy()
        {
            if (hooked) hooked.Damaged -= OnDamaged;
            if (hookedMatch) hookedMatch.Killed -= OnKill;
        }

        void OnDamaged(Vector3 src, float amount)
        {
            vignetteAlpha = Mathf.Min(0.85f, vignetteAlpha + 0.25f + amount / 100f);
            Indicator free = indicators.OrderBy(i => i.life).First();
            free.src = src;
            free.life = 1.4f;
        }

        /// <summary>A plain line in the kill feed (players joining / leaving a network match).</summary>
        public void Notice(string text)
        {
            if (!feedRoot) return;
            AddFeedLine("<color=#9fd3ff>" + text + "</color>", false);
        }

        void OnKill(KillEvent e)
        {
            string Col(Combatant c) => c ? (c.isPlayer ? "<color=#ffd84a>" + c.displayName + "</color>" : c.human ? "<color=#9fd3ff>" + c.displayName + "</color>" : c.displayName) : "?";
            string line;
            if (!e.killer && e.weapon == DiscoClub.RocketWeapon) line = "<color=#ff4a3a>ГОЛОВА</color>  <color=#aaaaaa>[ракета]</color>  " + Col(e.victim);
            else if (!e.killer || e.killer == e.victim) line = Col(e.victim) + "  <color=#aaaaaa>[" + e.weapon + "]</color>  самоубийство";
            else line = Col(e.killer) + "  <color=#aaaaaa>[" + e.weapon + "]</color>" + (e.headshot ? " <color=#ff5040>◎</color>" : "") + "  " + Col(e.victim);
            bool mine = (e.killer && e.killer.isPlayer) || (e.victim && e.victim.isPlayer);
            AddFeedLine(line, mine);
            if (e.killer && e.killer.isPlayer && e.killer != e.victim) ProcAudio.Play(Sfx.Kill, 0.35f, 0f);
        }

        void AddFeedLine(string line, bool mine)
        {
            var rt = Rect("Feed", feedRoot, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(600f, 32f));
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = mine ? new Color(0.35f, 0.25f, 0.05f, 0.55f) : new Color(0f, 0f, 0f, 0.4f);
            bg.raycastTarget = false;
            var trt = Rect("T", rt, Vector2.zero, Vector2.one, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), Vector2.zero);
            var t = Txt(trt, 22, TextAnchor.MiddleRight, Color.white);
            t.text = line;
            feed.Insert(0, new FeedLine { text = t, born = Time.unscaledTime });
        }

        string FormatTime(float s) { int m = Mathf.FloorToInt(s / 60f); int sec = Mathf.FloorToInt(s % 60f); return $"{m:00}:{sec:00}"; }

        void Update()
        {
            Hook();
            float dt = Time.unscaledDeltaTime;
            var ph = PlayerHealth.Local;
            var mm = MatchManager.Instance;

            // health
            if (ph)
            {
                float h01 = ph.Health / ph.maxHealth;
                healthShown = Mathf.MoveTowards(healthShown, h01, dt * 1.5f);
                healthFill.rectTransform.anchorMax = new Vector2(Mathf.Max(0.001f, h01), 1f);
                healthFill.color = Color.Lerp(new Color(1f, 0.25f, 0.2f), new Color(0.9f, 0.95f, 0.9f), Mathf.InverseLerp(0.25f, 0.6f, h01));
                healthText.text = Mathf.CeilToInt(ph.Health).ToString();
                protectImg.color = new Color(0.3f, 0.7f, 1f, ph.Protected ? 0.6f : 0f);
                float low = h01 < 0.35f && !ph.IsDead ? 0.25f + Mathf.Sin(Time.unscaledTime * 5f) * 0.08f : 0f;
                vignetteAlpha = Mathf.MoveTowards(vignetteAlpha, low, dt * 0.9f);
                vignette.color = new Color(0.8f, 0.05f, 0.02f, vignetteAlpha);

                deathAlpha = Mathf.MoveTowards(deathAlpha, ph.IsDead ? 1f : 0f, dt * 3f);
                deathGroup.alpha = deathAlpha;
                if (ph.IsDead)
                {
                    var k = ph.LastKiller;
                    string by = !k ? (ph.LastKillerWeapon == DiscoClub.RocketWeapon ? "Вас накрыла ракета Головы" : "") : k.isPlayer ? "Вы подорвались сами" : $"Убийца: <b>{k.displayName}</b>  [{ph.LastKillerWeapon}]";
                    string resp = mm && mm.Ended ? "" : $"\nВозрождение через {Mathf.CeilToInt(ph.RespawnIn)}";
                    string kit = mm && mm.Ended ? "" : ph.loadout && ph.loadout.ChoicePending
                        ? $"\n<size=20><color=#FFBD42>Следующая жизнь: {PlayerLoadout.Primary} · {PlayerLoadout.Secondary} · {PlayerLoadout.Melee}</color></size>"
                        : "\n<size=20><color=#FFFFFF90>Esc → Снаряжение — сменить оружие</color></size>";
                    deathSub.text = by + resp + kit;
                }
            }

            // damage direction arrows
            if (ph)
            {
                Transform cam = ph.playerCam.cam.transform;
                foreach (var ind in indicators)
                {
                    if (ind.life <= 0f) { ind.img.color = new Color(1f, 0.15f, 0.1f, 0f); continue; }
                    ind.life -= dt;
                    Vector3 to = ind.src - cam.position; to.y = 0f;
                    Vector3 f = cam.forward; f.y = 0f;
                    float ang = Vector3.SignedAngle(f, to, Vector3.up);
                    ind.rt.localRotation = Quaternion.Euler(0f, 0f, -ang);
                    ind.rt.anchoredPosition = Quaternion.Euler(0f, 0f, -ang) * new Vector3(0f, 170f, 0f);
                    ind.img.color = new Color(1f, 0.15f, 0.1f, Mathf.Clamp01(ind.life) * 0.85f);
                }
            }

            // kill feed
            for (int i = feed.Count - 1; i >= 0; i--)
            {
                var f = feed[i];
                float age = Time.unscaledTime - f.born;
                var rt = (RectTransform)f.text.transform.parent;
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(0f, -i * 36f), dt * 12f);
                float a = Mathf.Clamp01((7f - age) / 1f);
                f.text.color = new Color(1f, 1f, 1f, a);
                rt.GetComponent<Image>().color = new Color(rt.GetComponent<Image>().color.r, rt.GetComponent<Image>().color.g, rt.GetComponent<Image>().color.b, 0.5f * a);
                if (age > 8f || i > 5) { Destroy(rt.gameObject); feed.RemoveAt(i); }
            }

            // match info
            bool dm = mm;
            timerText.enabled = scoreText.enabled = dm;
            if (dm)
            {
                timerText.text = FormatTime(mm.TimeLeft);
                timerText.color = mm.TimeLeft < 30f ? new Color(1f, 0.4f, 0.3f) : Color.white;
                var me = ph ? ph.GetComponent<Combatant>() : null;
                var lead = mm.Leader;
                scoreText.text = $"{mm.mapName}  ·  Вы: <b>{(me ? me.kills : 0)}</b>  ·  Лидер: {(lead ? lead.displayName : "-")} ({(lead ? lead.kills : 0)})  ·  до {MatchSettings.killLimit}";
            }

            // scoreboard (Tab) and end screen
            bool tab = GameInput.ScoreboardHeld;
            bool ended = dm && mm.Ended;
            boardGroup.alpha = Mathf.MoveTowards(boardGroup.alpha, (tab || ended) && dm ? 1f : 0f, dt * 10f);
            endGroup.alpha = Mathf.MoveTowards(endGroup.alpha, ended ? 1f : 0f, dt * 2f);
            if (dm && (boardGroup.alpha > 0f))
            {
                boardTitle.text = "DEATHMATCH — " + mm.mapName;
                var rows = Combatant.All.Where(x => x).OrderByDescending(x => x.kills).ThenBy(x => x.deaths).ToList();
                var sn = new System.Text.StringBuilder("<color=#999999>Игрок</color>\n");
                var sk = new System.Text.StringBuilder("<color=#999999>Убийства</color>\n");
                var sd = new System.Text.StringBuilder("<color=#999999>Смерти</color>\n");
                int n = 1;
                foreach (var r in rows)
                {
                    string pre = r.isPlayer ? "<color=#ffd84a><b>" : !r.alive ? "<color=#888888>" : "";
                    string post = r.isPlayer ? "</b></color>" : !r.alive ? "</color>" : "";
                    sn.Append(pre).Append(n).Append(".  ").Append(r.displayName).Append(post).Append('\n');
                    sk.Append(pre).Append(r.kills).Append(post).Append('\n');
                    sd.Append(pre).Append(r.deaths).Append(post).Append('\n');
                    n++;
                }
                boardText.text = sn.ToString();
                boardKills.text = sk.ToString();
                boardDeaths.text = sd.ToString();
            }
            if (ended)
            {
                var w = mm.Winner;
                endTitle.text = w && w.isPlayer ? "ПОБЕДА!" : "МАТЧ ОКОНЧЕН";
                endSub.text = (w ? "Победитель: " + w.displayName + " — " + w.kills + " убийств.   " : "") +
                              $"Enter — сыграть снова · Esc — меню · авто-рестарт через {Mathf.CeilToInt(25f - (Time.unscaledTime - mm.EndTime))}";
            }
        }
    }
}
