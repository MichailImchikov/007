using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Main menu: navigation on the left (play, LAN game, character, loadout, settings), the player's soldier
    /// in a showroom on the right, and modal windows for map selection and the other screens.
    /// </summary>
    public partial class MainMenu : MonoBehaviour
    {
        public struct MapEntry { public string scene, title, tag, desc; public Color accent; public GameMode mode; }

        public static readonly MapEntry[] Maps =
        {
            new MapEntry { scene = "Map_Port", title = "ПОРТ", tag = "Контейнерный терминал · средняя дистанция", accent = new Color(0.25f, 0.6f, 0.95f),
                           desc = "Лабиринт контейнеров, линия на крышах вдоль востока, кран и склад в центре. Фланги через западный лабиринт." },
            new MapEntry { scene = "Map_Town", title = "ГОРОДОК", tag = "Пустынный город · три линии", accent = new Color(0.95f, 0.65f, 0.25f),
                           desc = "Главная улица, рынок на площади и переулки. Двухэтажные дома с окнами на улицу и выходы на крыши." },
            new MapEntry { scene = "Map_Warehouse", title = "СКЛАД", tag = "Промышленный склад · два уровня", accent = new Color(0.55f, 0.85f, 0.4f),
                           desc = "Ряды стеллажей, антресоль по периметру, офис и погрузочная зона с фурами." },
            new MapEntry { scene = "Map_Plant", title = "ЗАВОД", tag = "Котельная на закате · два яруса", accent = new Color(0.9f, 0.35f, 0.3f),
                           desc = "Цех с котлами и трубопроводами, мостки на двух ярусах, двор с цистернами и железнодорожной веткой." },
            new MapEntry { scene = "Map_Village", title = "ДЕРЕВНЯ", tag = "Лесная деревня · открытые линии", accent = new Color(0.45f, 0.75f, 0.55f),
                           desc = "Избы, сараи и церковь на холме. Длинные линии через поле, заборы, стога и ближний бой в домах." },
            new MapEntry { scene = "Map_Flooded", title = "ЗАТОПЛЕННАЯ ДЕРЕВНЯ", tag = "Туманная низина · большая карта", accent = new Color(0.4f, 0.62f, 0.68f),
                           desc = "Особняк с садом, церковь, теплицы и ангар вокруг затопленной дороги. Дальние линии в тумане и бой в домах." },
            new MapEntry { scene = "Map_Flooded", title = "ВОЙНА ДРОНОВ", tag = "Затопленная деревня · нож, пистолет и FPV-дрон", accent = new Color(0.95f, 0.45f, 0.2f), mode = GameMode.Drones,
                           desc = "Только нож и пистолет. [V] — запуск FPV-дрона (перезарядка 30 с после потери): реалистичная физика, пулемёт, подрыв при касании, тепловизор. Сбитый дрон выдаёт район оператора." },
            new MapEntry { scene = "ShootingRange", title = "ПОЛИГОН", tag = "Тренировка", accent = new Color(0.8f, 0.8f, 0.85f),
                           desc = "Мишени, полоса препятствий, тренировочные боты для добиваний, стенд со всеми способностями." },
        };

        static readonly string[] DiffNames = { "Легко", "Нормально", "Сложно" };
        static int selectedMap;

        RectTransform canvasRoot;
        Text nickText, loadoutText, statusLine, controlsText;
        LookPreview preview;

        void Start()
        {
            Time.timeScale = 1f;
            GameInput.Blocked = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ProcAudio.Init();
            GameSettings.EnsureLoaded();
            PauseMenu.EnsureEventSystem();
            Build();
        }

        // ------------------------------------------------------------------ layout

        void Build()
        {
            UIKit.MakeCanvas(gameObject, 0);
            canvasRoot = (RectTransform)transform;

            var bg = UIKit.Rect("BG", transform);
            UIKit.Fill(bg);
            bg.gameObject.AddComponent<RawImage>().texture = BackgroundTexture();

            // diagonal accent stripes behind the soldier
            for (int i = 0; i < 3; i++)
            {
                var s = UIKit.Solid(transform, "Stripe", new Color(1f, 0.74f, 0.26f, 0.035f + i * 0.012f));
                var rt = s.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.72f, 0.5f);
                rt.sizeDelta = new Vector2(70f - i * 18f, 2200f);
                rt.anchoredPosition = new Vector2(-120f + i * 110f, 0f);
                rt.localRotation = Quaternion.Euler(0f, 0f, -24f);
            }

            // ---- logo
            var logo = UIKit.Label(transform, "007", 170, TextAnchor.MiddleLeft, UIKit.Accent, true, FontStyle.Bold);
            Pin(logo.rectTransform, new Vector2(0f, 1f), new Vector2(130f, -60f), new Vector2(700f, 190f));
            var sub = UIKit.Label(transform, "ТАКТИЧЕСКИЙ ШУТЕР", 28, TextAnchor.MiddleLeft, UIKit.TextDim, true);
            Pin(sub.rectTransform, new Vector2(0f, 1f), new Vector2(138f, -238f), new Vector2(700f, 40f));
            var line = UIKit.Solid(transform, "LogoLine", UIKit.Accent);
            Pin(line.rectTransform, new Vector2(0f, 1f), new Vector2(138f, -288f), new Vector2(90f, 4f));

            // ---- navigation
            float y = -340f;
            NavButton("ИГРАТЬ", ref y, OpenPlay, UIKit.Style.Primary, 72f);
            y -= 14f;
            NavCaption("СЕТЕВАЯ ИГРА", ref y);
            NavButton("Создать игру по сети", ref y, OpenHostPanel);
            NavButton("Найти игру по сети", ref y, OpenJoinPanel);
            y -= 14f;
            NavCaption("ПРОФИЛЬ", ref y);
            NavButton("Персонаж", ref y, () => CharacterPanel.Open(canvasRoot, RefreshProfile));
            NavButton("Снаряжение", ref y, () => LoadoutPanel.Open(canvasRoot, false, RefreshProfile));
            NavButton("Настройки", ref y, () => SettingsPanel.Open(canvasRoot, RefreshProfile));
            y -= 14f;
            NavButton("Выход", ref y, Quit, UIKit.Style.Ghost);

            // ---- soldier showroom (right)
            preview = LookPreview.Ensure();
            var card = UIKit.Panel(transform, "Showroom", Color.white, 18);
            var crt = card.rectTransform;
            Pin(crt, new Vector2(1f, 0.5f), new Vector2(-150f, 40f), new Vector2(600f, 800f), new Vector2(1f, 0.5f));
            card.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            preview.Show(crt);
            var shade = UIKit.Rect("Shade", crt);
            shade.anchorMin = Vector2.zero; shade.anchorMax = new Vector2(1f, 0f); shade.pivot = new Vector2(0.5f, 0f);
            shade.sizeDelta = new Vector2(0f, 220f); shade.anchoredPosition = Vector2.zero;
            var sh = shade.gameObject.AddComponent<RawImage>(); sh.texture = UIKit.VGradient; sh.color = new Color(0.03f, 0.035f, 0.045f, 0.95f); sh.raycastTarget = false;
            nickText = UIKit.Label(crt, "", 40, TextAnchor.LowerLeft, UIKit.TextC, true);
            UIKit.Fill(nickText.rectTransform, 34f, 66f, 20f, 0f);
            loadoutText = UIKit.Label(crt, "", 21, TextAnchor.LowerLeft, UIKit.Accent, true);
            UIKit.Fill(loadoutText.rectTransform, 36f, 30f, 20f, 0f);
            var edit = UIKit.Button(crt, "Изменить", () => CharacterPanel.Open(canvasRoot, RefreshProfile), UIKit.Style.Normal, 20);
            var ert = (RectTransform)edit.transform;
            ert.anchorMin = ert.anchorMax = ert.pivot = new Vector2(1f, 1f);
            ert.sizeDelta = new Vector2(160f, 46f); ert.anchoredPosition = new Vector2(-20f, -20f);

            // ---- bottom lines
            statusLine = UIKit.Label(transform, "", 22, TextAnchor.MiddleLeft, new Color(1f, 0.6f, 0.4f));
            Pin(statusLine.rectTransform, new Vector2(0f, 0f), new Vector2(138f, 86f), new Vector2(1200f, 34f));
            if (!string.IsNullOrEmpty(NetGame.Status) && !Net.Active) statusLine.text = NetGame.Status;
            controlsText = UIKit.Label(transform, "", 18, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.35f));
            Pin(controlsText.rectTransform, new Vector2(0f, 0f), new Vector2(138f, 40f), new Vector2(1700f, 30f));

            RefreshProfile();
            NetGame.StatusChanged += OnNetStatus;
        }

        static void Pin(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        void NavButton(string label, ref float y, System.Action a, UIKit.Style style = UIKit.Style.Normal, float h = 58f)
        {
            var b = UIKit.Button(transform, label, a, style, style == UIKit.Style.Primary ? 32 : 25, true);
            Pin((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(130f, y), new Vector2(520f, h));
            y -= h + 8f;
        }

        void NavCaption(string label, ref float y)
        {
            var t = UIKit.Label(transform, label, 17, TextAnchor.LowerLeft, UIKit.TextDim, true);
            Pin(t.rectTransform, new Vector2(0f, 1f), new Vector2(134f, y), new Vector2(520f, 26f));
            y -= 32f;
        }

        void RefreshProfile()
        {
            if (nickText) nickText.text = CharacterLook.Local.nick;
            if (loadoutText) loadoutText.text = PlayerLoadout.Primary + "  ·  " + PlayerLoadout.Secondary + "  ·  " + PlayerLoadout.Melee.ToUpperInvariant() + "  ·  ГРАНАТЫ";
            if (controlsText)
            {
                string K(BindAction a) => GameSettings.KeyName(a);
                controlsText.text = $"{K(BindAction.MoveForward)}{K(BindAction.MoveLeft)}{K(BindAction.MoveBack)}{K(BindAction.MoveRight)} — движение   {K(BindAction.Sprint)} — бег ({(GameSettings.SprintToggle ? "нажатие" : "зажатие")})   " +
                                    $"{K(BindAction.Jump)} — прыжок   {K(BindAction.CrouchToggle)}/{K(BindAction.CrouchHold)} — присед   {K(BindAction.Prone)} — лечь   {K(BindAction.Grenade)} — граната   " +
                                    $"{K(BindAction.Melee)} — нож   {K(BindAction.Ability)} — способность   {K(BindAction.Scoreboard)} — счёт   Esc — пауза";
            }
        }

        static Texture2D BackgroundTexture()
        {
            const int W = 64, H = 36;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = x / (W - 1f), v = y / (H - 1f);
                    Color c = Color.Lerp(new Color(0.025f, 0.028f, 0.035f), new Color(0.085f, 0.095f, 0.115f), v * 0.7f + u * 0.3f);
                    // warm glow behind the showroom, vignette at the edges
                    float glow = Mathf.Exp(-((u - 0.78f) * (u - 0.78f) * 9f + (v - 0.55f) * (v - 0.55f) * 6f));
                    c += new Color(0.09f, 0.06f, 0.02f) * glow;
                    float vig = Mathf.Clamp01(1.25f - Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 1.3f);
                    c *= Mathf.Lerp(0.55f, 1f, vig);
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>Kept for <see cref="HoverInfo"/>.</summary>
        public void ShowDesc(string s) { }

        void OnNetStatus()
        {
            if (joinStatus) joinStatus.text = NetGame.Status;
            if (statusLine) statusLine.text = Net.Active ? "" : NetGame.Status;
        }

        void OnDestroy() => NetGame.StatusChanged -= OnNetStatus;

        // ------------------------------------------------------------------ play (map select)

        void OpenPlay()
        {
            var panel = MenuPanel.Create(canvasRoot, "Одиночная игра", new Vector2(1560f, 860f), out var body);
            var cards = new List<UIButtonFx>();
            Text desc = null, title = null;
            Image accentBar = null;

            var grid = UIKit.Rect("Maps", body);
            UIKit.Span(grid, 0f, 0f, 0.6f, 1f);
            grid.offsetMin = new Vector2(0f, 84f);
            for (int i = 0; i < Maps.Length; i++)
            {
                int idx = i;
                var m = Maps[i];
                int col = i % 2, row = i / 2;
                var b = UIKit.Button(grid, "", () => { selectedMap = idx; Select(); }, UIKit.Style.Normal, 34, true);
                var rt = (RectTransform)b.transform;
                // rows share the grid height, so more maps shrink the cards instead of overflowing
                int rows = (Maps.Length + 1) / 2;
                float top = 1f - row / (float)rows, bottom = 1f - (row + 1) / (float)rows;
                rt.anchorMin = new Vector2(col * 0.5f, bottom); rt.anchorMax = new Vector2(col * 0.5f + 0.5f, top); rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(col == 0 ? 0f : 8f, row == rows - 1 ? 0f : 7f);
                rt.offsetMax = new Vector2(col == 0 ? -8f : 0f, row == 0 ? 0f : -7f);
                var lbl = UIKit.LabelOf(b);
                lbl.text = m.title;
                lbl.alignment = TextAnchor.UpperLeft;
                lbl.rectTransform.offsetMax = new Vector2(-8f, -22f);
                var tag = UIKit.Label(rt, m.tag, 18, TextAnchor.UpperLeft, UIKit.TextDim);
                UIKit.Fill(tag.rectTransform, 27f, 0f, 14f, 72f);
                var sw = UIKit.Solid(rt, "MapAccent", m.accent);
                sw.rectTransform.anchorMin = new Vector2(0f, 0f); sw.rectTransform.anchorMax = new Vector2(1f, 0f); sw.rectTransform.pivot = new Vector2(0.5f, 0f);
                sw.rectTransform.offsetMin = new Vector2(16f, 16f); sw.rectTransform.offsetMax = new Vector2(-16f, 22f);
                var mode = UIKit.Label(rt, m.scene == "ShootingRange" ? "ТРЕНИРОВКА" : m.mode == GameMode.Drones ? "ДРОНЫ" : "DEATHMATCH", 16, TextAnchor.LowerRight, m.accent, true);
                UIKit.Fill(mode.rectTransform, 0f, 30f, 18f, 0f);
                cards.Add(b.GetComponent<UIButtonFx>());
            }

            // ---- right: description + match settings
            var right = UIKit.Panel(body, "Side", UIKit.Card, 12).rectTransform;
            UIKit.Span(right, 0.615f, 0f, 1f, 1f);
            right.offsetMin = new Vector2(0f, 84f);
            accentBar = UIKit.Solid(right, "Accent", Color.white);
            accentBar.rectTransform.SetTop(0f, 4f, 14f);
            title = UIKit.Label(right, "", 46, TextAnchor.UpperLeft, UIKit.TextC, true);
            title.rectTransform.SetTop(24f, 60f, 28f);
            desc = UIKit.Label(right, "", 20, TextAnchor.UpperLeft, UIKit.TextDim);
            desc.rectTransform.SetTop(86f, 110f, 28f);
            var listHolder = UIKit.Rect("Settings", right);
            UIKit.Fill(listHolder, 14f, 10f, 14f, 206f);
            var list = UIKit.ScrollList(listHolder, 6f);
            UIKit.Header(list, "Настройки матча");
            UIKit.SelectorRow(list, "Боты", 13, () => MatchSettings.botCount - 1, i => MatchSettings.botCount = i + 1, i => (i + 1).ToString());
            UIKit.SelectorRow(list, "Сложность", 3, () => MatchSettings.difficulty, i => MatchSettings.difficulty = i, i => DiffNames[i]);
            UIKit.SelectorRow(list, "Лимит убийств", 20, () => MatchSettings.killLimit / 5 - 1, i => MatchSettings.killLimit = (i + 1) * 5, i => ((i + 1) * 5).ToString());
            UIKit.SelectorRow(list, "Время", 6, () => Mathf.Clamp(Mathf.RoundToInt(MatchSettings.timeLimit / 300f) - 1, 0, 5), i => MatchSettings.timeLimit = (i + 1) * 300f, i => ((i + 1) * 5) + " мин");
            var loadRow = UIKit.Row(list, 58f);
            UIKit.RowLabel(loadRow, "Оружие");
            Button lb = null;
            lb = UIKit.Button(loadRow, PlayerLoadout.Primary + "  ›", () => LoadoutPanel.Open(canvasRoot, false, () => { if (lb) UIKit.LabelOf(lb).text = PlayerLoadout.Primary + "  ›"; RefreshProfile(); }), UIKit.Style.Normal, 22);
            UIKit.Span((RectTransform)lb.transform, 0.42f, 0.12f, 1f, 0.88f);
            ((RectTransform)lb.transform).offsetMax = new Vector2(-12f, 0f);

            var go = UIKit.Button(body, "В БОЙ", () => { MatchSettings.mode = Maps[selectedMap].mode; Play(Maps[selectedMap].scene); }, UIKit.Style.Primary, 30);
            var grt = (RectTransform)go.transform;
            grt.anchorMin = grt.anchorMax = grt.pivot = new Vector2(1f, 0f);
            grt.sizeDelta = new Vector2(340f, 64f); grt.anchoredPosition = Vector2.zero;
            var hint = UIKit.Label(body, "Оружие и облик можно поменять и во время матча (Esc).", 19, TextAnchor.MiddleLeft, UIKit.TextDim);
            hint.rectTransform.anchorMin = Vector2.zero; hint.rectTransform.anchorMax = new Vector2(0.7f, 0f); hint.rectTransform.pivot = Vector2.zero;
            hint.rectTransform.sizeDelta = new Vector2(0f, 64f); hint.rectTransform.anchoredPosition = Vector2.zero;

            void Select()
            {
                selectedMap = Mathf.Clamp(selectedMap, 0, Maps.Length - 1);
                var m = Maps[selectedMap];
                for (int i = 0; i < cards.Count; i++) cards[i].Selected = i == selectedMap;
                title.text = m.title;
                title.fontSize = m.title.Length > 14 ? 34 : 46; // long names stay on one line above the description
                desc.text = m.desc;
                accentBar.color = m.accent;
            }
            Select();
        }

        void Play(string scene)
        {
            if (!Application.CanStreamedLevelBeLoaded(scene))
            {
                if (statusLine) statusLine.text = "Карта «" + scene + "» не собрана: Tools > FPS > Build All Maps";
                return;
            }
            MenuPanel.CloseAll();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            SceneManager.LoadScene(scene);
        }

        void Quit()
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
    }
}
