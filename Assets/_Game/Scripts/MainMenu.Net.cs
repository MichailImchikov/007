using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>Main menu: LAN multiplayer windows — "Создать игру" (host) and "Найти игру по сети" (join).</summary>
    public partial class MainMenu
    {
        static readonly (string scene, string title)[] NetMaps =
        {
            ("Map_Port", "ПОРТ"), ("Map_Town", "ГОРОДОК"), ("Map_Warehouse", "СКЛАД"), ("Map_Plant", "ЗАВОД"), ("Map_Village", "ДЕРЕВНЯ"),
        };
        static int netMap;
        static string lastAddress = "";

        MenuPanel joinPanel;
        Text joinStatus;
        InputField joinAddress;
        readonly List<Button> gameButtons = new List<Button>();
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
            UIKit.SelectorRow(list, "Карта", NetMaps.Length, () => netMap, i => netMap = i, i => NetMaps[i].title);
            UIKit.SelectorRow(list, "Боты", 14, () => MatchSettings.botCount, i => MatchSettings.botCount = i, i => i.ToString());
            UIKit.SelectorRow(list, "Сложность ботов", 3, () => MatchSettings.difficulty, i => MatchSettings.difficulty = i, i => DiffNames[i]);
            UIKit.SelectorRow(list, "Лимит убийств", 20, () => MatchSettings.killLimit / 5 - 1, i => MatchSettings.killLimit = (i + 1) * 5, i => ((i + 1) * 5).ToString());
            UIKit.SelectorRow(list, "Время", 6, () => Mathf.Clamp(Mathf.RoundToInt(MatchSettings.timeLimit / 300f) - 1, 0, 5), i => MatchSettings.timeLimit = (i + 1) * 300f, i => ((i + 1) * 5) + " мин");

            var ips = LanDiscovery.LocalAddresses();
            var info = UIKit.Label(body, (ips.Count > 0 ? "Ваш IP в локальной сети: <b><color=#FFBD42>" + string.Join(",  ", ips) + "</color></b>" : "IP-адрес не найден — проверьте подключение к сети")
                                         + "   ·   порт " + NetGame.GamePort + "\nДругие игроки увидят игру в «Найти игру по сети» или введут этот IP.",
                                   20, TextAnchor.MiddleLeft, UIKit.TextDim);
            info.rectTransform.anchorMin = Vector2.zero; info.rectTransform.anchorMax = new Vector2(1f, 0f); info.rectTransform.pivot = Vector2.zero;
            info.rectTransform.sizeDelta = new Vector2(0f, 70f); info.rectTransform.anchoredPosition = new Vector2(0f, 74f);

            var start = UIKit.Button(body, "Запустить", () => { SaveNick(nick); panel.Close(); StartHosting(); }, UIKit.Style.Primary, 26);
            var srt = (RectTransform)start.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 0f);
            srt.sizeDelta = new Vector2(300f, 60f); srt.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------------ join

        void OpenJoinPanel()
        {
            joinPanel = MenuPanel.Create(canvasRoot, "Найти игру по сети", new Vector2(1100f, 820f), out var body);
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

            var cap = UIKit.Label(body, "ИГРЫ В ЛОКАЛЬНОЙ СЕТИ", 20, TextAnchor.LowerLeft, UIKit.Accent, true);
            cap.rectTransform.SetTop(84f, 34f);
            gameButtons.Clear();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = UIKit.Button(body, "", () => { SaveNick(nick); JoinFound(idx); }, UIKit.Style.Normal, 23, true);
                ((RectTransform)b.transform).SetTop(128f + i * 70f, 62f);
                gameButtons.Add(b);
            }

            var ipRow = UIKit.Rect("IP", body);
            ipRow.SetTop(424f, 62f);
            UIKit.Label(ipRow, "IP-адрес хоста", 24, TextAnchor.MiddleLeft).rectTransform.SetTop(0f, 62f);
            joinAddress = UIKit.Input(ipRow, lastAddress, "например 192.168.0.12");
            UIKit.Span(joinAddress.Rt(), 0.42f, 0.05f, 0.78f, 0.95f);
            var go = UIKit.Button(ipRow, "Войти", () => { SaveNick(nick); Connect(joinAddress.text); }, UIKit.Style.Primary, 24);
            UIKit.Span((RectTransform)go.transform, 0.8f, 0.05f, 1f, 0.95f);

            joinStatus = UIKit.Label(body, "Поиск игр...", 21, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.5f));
            joinStatus.rectTransform.SetTop(510f, 40f);
            NetGame.Ensure().Discovery.StartSearch();
            listT = 0f;
            RefreshGames();
        }

        void RefreshGames()
        {
            var games = NetGame.I && NetGame.I.Discovery ? NetGame.I.Discovery.Found : null;
            for (int i = 0; i < gameButtons.Count; i++)
            {
                var b = gameButtons[i];
                if (!b) continue;
                var label = UIKit.LabelOf(b);
                if (games != null && i < games.Count)
                {
                    var g = games[i];
                    string map = g.map;
                    foreach (var m in NetMaps) if (m.scene == g.map) map = m.title;
                    label.text = $"<b>{g.host}</b>   ·   {map}   ·   игроков {g.players}/{g.max}   ·   <color=#FFFFFF80>{g.address}</color>";
                    b.interactable = true;
                }
                else
                {
                    label.text = i == 0 && (games == null || games.Count == 0) ? "— игр пока не найдено —" : "";
                    b.interactable = false;
                }
            }
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
            if (!NetGame.Ensure().StartHost(NetMaps[netMap].scene))
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (statusLine) statusLine.text = NetGame.Status;
            }
        }

        void JoinFound(int i)
        {
            var games = NetGame.I ? NetGame.I.Discovery.Found : null;
            if (games == null || i >= games.Count) return;
            Connect(games[i].address + ":" + games[i].port);
        }

        void Connect(string address)
        {
            lastAddress = address;
            NetGame.Ensure().Join(address);
            if (joinStatus) joinStatus.text = NetGame.Status;
        }
    }
}
