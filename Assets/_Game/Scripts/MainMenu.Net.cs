using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>Main menu: LAN multiplayer windows — "Создать игру" (host) and "Найти игру по сети" (join).</summary>
    public partial class MainMenu
    {
        static readonly (string scene, string title)[] NetMapsAll =
        {
            ("Map_Port", "ПОРТ"), ("Map_Town", "ГОРОДОК"), ("Map_Warehouse", "СКЛАД"), ("Map_Plant", "ЗАВОД"), ("Map_Village", "ДЕРЕВНЯ"),
            ("Map_Flooded", "ЗАТОПЛЕННАЯ ДЕРЕВНЯ"), ("Map_Disco", "ДИСКОТЕКА"), ("Map_DesertBase", "ПУСТЫННАЯ БАЗА"), ("Map_Factory", "ФАБРИКА"),
            ("Map_Downtown", "ДАУНТАУН"), ("Map_Bazaar", "БАЗАР"), ("Map_Suburb", "ПРИГОРОД"), ("Map_Station", "ВОКЗАЛ"),
            ("Map_Depot", "СКЛАД БОЕПРИПАСОВ"), ("Map_Construction", "СТРОЙКА"),
        };
        static (string scene, string title)[] netMapsInBuild;

        /// <summary>The host's map choices: those in the build (a map made from a removed asset pack is left out).</summary>
        static (string scene, string title)[] NetMaps
        {
            get
            {
                if (netMapsInBuild == null)
                {
                    var l = new List<(string scene, string title)>();
                    foreach (var m in NetMapsAll) if (Application.CanStreamedLevelBeLoaded(m.scene)) l.Add(m);
                    netMapsInBuild = l.ToArray();
                }
                return netMapsInBuild;
            }
        }
        static int netMap;
        static int netMode;
        static readonly string[] NetModeNames = { "DEATHMATCH", "ВОЙНА ДРОНОВ", "КОМАНДНЫЙ БОЙ", "ЗАХВАТ ТОЧЕК", "ЗАКЛАДКА БОМБЫ", "ШТУРМ" };
        static readonly GameMode[] NetModes = { GameMode.Deathmatch, GameMode.Drones, GameMode.TeamDeathmatch, GameMode.Domination, GameMode.Bomb, GameMode.Assault };
        static string lastAddress = "", lastCode = "";
        /// <summary>Where the game is: 0 — the local network (direct, LAN discovery), 1 — the internet (Unity Relay, code / list).</summary>
        static int netWhere;
        static readonly string[] WhereNames = { "Локальная сеть", "Интернет" };

        MenuPanel joinPanel;
        Text joinStatus, joinCap, joinEntryLabel;
        InputField joinAddress;
        readonly List<Button> gameButtons = new List<Button>();
        readonly List<UIButtonFx> whereButtons = new List<UIButtonFx>();
        float listT;

        InputField NickRow(RectTransform list)
        {
            var row = UIKit.Row(list, 64f);
            UIKit.RowLabel(row, "Ваш ник");
            var f = UIKit.Input(row, CharacterLook.Local.nick, "Игрок");
            UIKit.Span(f.Rt(), 0.42f, 0.13f, 1f, 0.87f);
            f.Rt().offsetMax = new Vector2(-12f, 0f);
            f.characterLimit = CharacterLook.MaxNick;
            f.onEndEdit.AddListener(s => { CharacterLook.Local.nick = CharacterLook.SanitizeNick(s); CharacterLook.SaveLocal(); f.text = CharacterLook.Local.nick; RefreshProfile(); });
            return f;
        }

        // ------------------------------------------------------------------ host

        void OpenHostPanel()
        {
            var panel = MenuPanel.Create(canvasRoot, "Создать игру по сети", new Vector2(1100f, 820f), out var body);
            var holder = UIKit.Rect("List", body);
            UIKit.Fill(holder, 0f, 150f, 0f, 0f);
            var list = UIKit.ScrollList(holder, 6f);
            UIKit.Header(list, "Игрок");
            var nick = NickRow(list);
            UIKit.Header(list, "Матч");
            Text info = null;
            UIKit.SelectorRow(list, "Сеть", WhereNames.Length, () => netWhere, i => { netWhere = i; if (info) info.text = HostInfo(); }, i => WhereNames[i]);
            UIKit.SelectorRow(list, "Режим", NetModeNames.Length, () => netMode, i => netMode = i, i => NetModeNames[i]);
            netMap = Mathf.Clamp(netMap, 0, Mathf.Max(0, NetMaps.Length - 1));
            UIKit.SelectorRow(list, "Карта", NetMaps.Length, () => netMap, i => netMap = i, i => NetMaps[i].title);
            UIKit.SelectorRow(list, "Боты", 14, () => MatchSettings.botCount, i => MatchSettings.botCount = i, i => i.ToString());
            UIKit.SelectorRow(list, "Сложность ботов", 3, () => MatchSettings.difficulty, i => MatchSettings.difficulty = i, i => DiffNames[i]);
            UIKit.SelectorRow(list, "Лимит убийств", 20, () => MatchSettings.killLimit / 5 - 1, i => MatchSettings.killLimit = (i + 1) * 5, i => ((i + 1) * 5).ToString());
            UIKit.SelectorRow(list, "Время", 6, () => Mathf.Clamp(Mathf.RoundToInt(MatchSettings.timeLimit / 300f) - 1, 0, 5), i => MatchSettings.timeLimit = (i + 1) * 300f, i => ((i + 1) * 5) + " мин");

            info = UIKit.Label(body, HostInfo(), 20, TextAnchor.MiddleLeft, UIKit.TextDim);
            info.rectTransform.anchorMin = Vector2.zero; info.rectTransform.anchorMax = new Vector2(1f, 0f); info.rectTransform.pivot = Vector2.zero;
            info.rectTransform.sizeDelta = new Vector2(0f, 70f); info.rectTransform.anchoredPosition = new Vector2(0f, 74f);

            var start = UIKit.Button(body, "Запустить", () => { SaveNick(nick); panel.Close(); StartHosting(); }, UIKit.Style.Primary, 26);
            var srt = (RectTransform)start.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 0f);
            srt.sizeDelta = new Vector2(300f, 60f); srt.anchoredPosition = Vector2.zero;
        }

        /// <summary>The line under the host settings: the LAN address, or how internet players find the game.</summary>
        static string HostInfo()
        {
            if (netWhere == 1)
                return "Игра через интернет (Unity Relay): открытые порты и «белый» IP не нужны. Она появится в «Найти игру» → Интернет,\n" +
                       "а код для друзей покажется при запуске и в игре (Tab — таблица, Esc — пауза), он же копируется в буфер.";
            var ips = LanDiscovery.LocalAddresses();
            return (ips.Count > 0 ? "Ваш IP в локальной сети: <b><color=#FFBD42>" + string.Join(",  ", ips) + "</color></b>" : "IP-адрес не найден — проверьте подключение к сети")
                   + "   ·   порт " + NetGame.GamePort + "\nДругие игроки увидят игру в «Найти игру по сети» или введут этот IP.";
        }

        // ------------------------------------------------------------------ join

        void OpenJoinPanel()
        {
            joinPanel = MenuPanel.Create(canvasRoot, "Найти игру по сети", new Vector2(1100f, 860f), out var body);
            joinPanel.onClose = () => { if (NetGame.I && NetGame.I.Discovery) NetGame.I.Discovery.StopSearch(); joinPanel = null; };
            joinPanel.onUpdate = () =>
            {
                listT -= Time.unscaledDeltaTime;
                if (listT <= 0f) { listT = 0.5f; RefreshGames(); }
            };

            var top = UIKit.Rect("Top", body);
            top.SetTop(0f, 64f);
            UIKit.Label(top, "Ваш ник", 24, TextAnchor.MiddleLeft).rectTransform.SetTop(0f, 64f);
            var nick = UIKit.Input(top, CharacterLook.Local.nick, "Игрок");
            UIKit.Span(nick.Rt(), 0.42f, 0.1f, 1f, 0.9f);
            nick.characterLimit = CharacterLook.MaxNick;
            nick.onEndEdit.AddListener(s => { CharacterLook.Local.nick = CharacterLook.SanitizeNick(s); CharacterLook.SaveLocal(); nick.text = CharacterLook.Local.nick; RefreshProfile(); });

            // local network / internet
            var whereRow = UIKit.Rect("Where", body);
            whereRow.SetTop(76f, 56f);
            whereButtons.Clear();
            for (int i = 0; i < WhereNames.Length; i++)
            {
                int w = i;
                var b = UIKit.Button(whereRow, WhereNames[i], () => SetWhere(w), UIKit.Style.Normal, 23);
                UIKit.Span((RectTransform)b.transform, i == 0 ? 0f : 0.505f, 0f, i == 0 ? 0.495f : 1f, 1f);
                whereButtons.Add(b.GetComponent<UIButtonFx>());
            }

            joinCap = UIKit.Label(body, "", 20, TextAnchor.LowerLeft, UIKit.Accent, true);
            joinCap.rectTransform.SetTop(146f, 34f);
            gameButtons.Clear();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = UIKit.Button(body, "", () => { SaveNick(nick); JoinFound(idx); }, UIKit.Style.Normal, 23, true);
                ((RectTransform)b.transform).SetTop(190f + i * 70f, 62f);
                gameButtons.Add(b);
            }

            var ipRow = UIKit.Rect("IP", body);
            ipRow.SetTop(486f, 62f);
            joinEntryLabel = UIKit.Label(ipRow, "", 24, TextAnchor.MiddleLeft);
            joinEntryLabel.rectTransform.SetTop(0f, 62f);
            joinAddress = UIKit.Input(ipRow, "", "");
            UIKit.Span(joinAddress.Rt(), 0.42f, 0.05f, 0.78f, 0.95f);
            var go = UIKit.Button(ipRow, "Войти", () => { SaveNick(nick); EnterTyped(joinAddress.text); }, UIKit.Style.Primary, 24);
            UIKit.Span((RectTransform)go.transform, 0.8f, 0.05f, 1f, 0.95f);

            joinStatus = UIKit.Label(body, "Поиск игр...", 21, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));
            joinStatus.rectTransform.SetTop(572f, 40f);
            SetWhere(netWhere);
        }

        /// <summary>Join window: switch between LAN games (address) and internet games (code).</summary>
        void SetWhere(int w)
        {
            netWhere = Mathf.Clamp(w, 0, 1);
            for (int i = 0; i < whereButtons.Count; i++) if (whereButtons[i]) whereButtons[i].Selected = i == netWhere;
            bool online = netWhere == 1;
            if (joinCap) joinCap.text = online ? "ИГРЫ В ИНТЕРНЕТЕ" : "ИГРЫ В ЛОКАЛЬНОЙ СЕТИ";
            if (joinEntryLabel) joinEntryLabel.text = online ? "Код игры" : "IP-адрес хоста";
            if (joinAddress)
            {
                joinAddress.text = online ? lastCode : lastAddress;
                if (joinAddress.placeholder is Text ph) ph.text = online ? "например K7Q2XM" : "например 192.168.0.12";
            }
            var disc = NetGame.Ensure().Discovery;
            if (online) disc.StopSearch(); else disc.StartSearch();
            if (joinStatus) joinStatus.text = online ? "Поиск игр в интернете..." : "Поиск игр...";
            listT = 0f;
            RefreshGames();
        }

        void RefreshGames()
        {
            if (netWhere == 1) { RefreshOnline(); return; }
            var games = NetGame.I && NetGame.I.Discovery ? NetGame.I.Discovery.Found : null;
            for (int i = 0; i < gameButtons.Count; i++)
            {
                var b = gameButtons[i];
                if (!b) continue;
                var label = UIKit.LabelOf(b);
                if (games != null && i < games.Count)
                {
                    var g = games[i];
                    label.text = $"<b>{g.host}</b>   ·   {MapTitle(g.map)}   ·   игроков {g.players}/{g.max}   ·   <color=#FFFFFF80>{g.address}</color>";
                    b.interactable = true;
                }
                else
                {
                    label.text = i == 0 && (games == null || games.Count == 0) ? "— игр пока не найдено —" : "";
                    b.interactable = false;
                }
            }
        }

        void RefreshOnline()
        {
            NetOnline.Refresh(); // (asks the service at most every few seconds)
            var games = NetOnline.Found;
            for (int i = 0; i < gameButtons.Count; i++)
            {
                var b = gameButtons[i];
                if (!b) continue;
                var label = UIKit.LabelOf(b);
                if (i < games.Count)
                {
                    var g = games[i];
                    string who = string.IsNullOrEmpty(g.host) ? g.name : g.host;
                    label.text = $"<b>{who}</b>   ·   {MapTitle(g.map)}   ·   {ObjectiveMode.ModeTitle(g.mode)}   ·   игроков {g.players}/{g.max}";
                    b.interactable = !NetOnline.Working;
                }
                else
                {
                    label.text = i != 0 || games.Count > 0 ? ""
                               : NetOnline.ListError != null ? "— интернет-игры недоступны: " + NetOnline.ListError + " —"
                               : NetOnline.Searching ? "— поиск... —" : "— открытых игр нет: создайте свою или войдите по коду —";
                    b.interactable = false;
                }
            }
        }

        static string MapTitle(string scene)
        {
            foreach (var m in NetMapsAll) if (m.scene == scene) return m.title;
            return scene;
        }

        void SaveNick(InputField f)
        {
            if (!f) return;
            CharacterLook.Local.nick = CharacterLook.SanitizeNick(f.text);
            CharacterLook.SaveLocal();
            RefreshProfile();
        }

        void StartHosting()
        {
            MenuPanel.CloseAll();
            MatchSettings.mode = NetModes[Mathf.Clamp(netMode, 0, NetModes.Length - 1)];
            if (netWhere == 1)
            {
                // internet: sign in, open the session (relay + code), then the map loads; progress in the status line
                NetOnline.Host(NetMaps[netMap].scene, NetMaps[netMap].title);
                return;
            }
            if (!NetGame.Ensure().StartHost(NetMaps[netMap].scene))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (statusLine) statusLine.text = NetGame.Status;
            }
        }

        void JoinFound(int i)
        {
            if (netWhere == 1)
            {
                if (i < NetOnline.Found.Count) NetOnline.JoinById(NetOnline.Found[i].id);
                if (joinStatus) joinStatus.text = NetGame.Status;
                return;
            }
            var games = NetGame.I ? NetGame.I.Discovery.Found : null;
            if (games == null || i >= games.Count) return;
            Connect(games[i].address + ":" + games[i].port);
        }

        /// <summary>The "Войти" button: the typed IP (LAN) or join code (internet).</summary>
        void EnterTyped(string text)
        {
            if (netWhere == 1)
            {
                lastCode = (text ?? "").Trim().ToUpperInvariant();
                NetOnline.JoinByCode(lastCode);
                if (joinStatus) joinStatus.text = NetGame.Status;
                return;
            }
            Connect(text);
        }

        void Connect(string address)
        {
            lastAddress = address;
            NetGame.Ensure().Join(address);
            if (joinStatus) joinStatus.text = NetGame.Status;
        }
    }
}
