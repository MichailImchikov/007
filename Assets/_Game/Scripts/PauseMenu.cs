using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>Esc menu: resume / loadout / settings / restart / main menu / quit.</summary>
    public class PauseMenu : MonoBehaviour
    {
        public static PauseMenu Instance { get; private set; }
        public static bool IsOpen => Instance && Instance.open;

        bool open;
        CanvasGroup group;
        RectTransform root;
        Text loadoutNote;

        public static PauseMenu Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("PauseMenu").AddComponent<PauseMenu>();
            return Instance;
        }

        public static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>()) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        void Awake()
        {
            Instance = this;
            EnsureEventSystem();
            UIKit.MakeCanvas(gameObject, 50);

            root = UIKit.Rect("Panel", transform);
            UIKit.Fill(root);
            var bg = root.gameObject.AddComponent<Image>(); bg.color = new Color(0.015f, 0.02f, 0.028f, 0.8f);
            group = root.gameObject.AddComponent<CanvasGroup>();

            // left column
            var col = UIKit.Panel(root, "Column", new Color(0.06f, 0.065f, 0.075f, 0.94f), 0).rectTransform;
            col.anchorMin = new Vector2(0f, 0f); col.anchorMax = new Vector2(0f, 1f); col.pivot = new Vector2(0f, 0.5f);
            col.sizeDelta = new Vector2(620f, 0f); col.anchoredPosition = Vector2.zero;
            var edge = UIKit.Solid(col, "Edge", UIKit.Accent).rectTransform;
            edge.anchorMin = new Vector2(1f, 0f); edge.anchorMax = Vector2.one; edge.pivot = new Vector2(1f, 0.5f);
            edge.sizeDelta = new Vector2(3f, 0f); edge.anchoredPosition = Vector2.zero;

            var title = UIKit.Label(col, "ПАУЗА", 76, TextAnchor.LowerLeft, UIKit.TextC, true, FontStyle.Bold);
            title.rectTransform.SetTop(110f, 96f, 90f);
            string mapName = MatchManager.Instance ? MatchManager.Instance.mapName : SceneManager.GetActiveScene().name == "ShootingRange" ? "Полигон" : "";
            var sub = UIKit.Label(col, (mapName + (Net.Active ? "   ·   сетевая игра" : MatchManager.Instance ? "   ·   deathmatch" : "")).ToUpperInvariant(), 20, TextAnchor.UpperLeft, UIKit.Accent, true);
            sub.rectTransform.SetTop(212f, 30f, 94f);

            float y = 300f;
            Btn(col, "Продолжить", ref y, Resume, UIKit.Style.Primary);
            y += 14f;
            Btn(col, "Снаряжение", ref y, () => LoadoutPanel.Open(transform, MatchManager.Instance, RefreshNote));
            Btn(col, "Настройки", ref y, () => SettingsPanel.Open(transform));
            y += 14f;
            var restart = Btn(col, "Начать заново", ref y, () =>
            {
                Time.timeScale = 1f; GameInput.Blocked = false;
                if (Net.IsHost) { NetGame.HostLoadMap(SceneManager.GetActiveScene().name); return; }
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            });
            if (Net.IsClient) restart.gameObject.SetActive(false); // only the host restarts a network match
            Btn(col, Net.Active ? "Покинуть игру" : "Главное меню", ref y, () => { Time.timeScale = 1f; GameInput.Blocked = false; NetGame.Leave(); SceneManager.LoadScene("MainMenu"); });
            Btn(col, "Выход из игры", ref y, () =>
            {
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }, UIKit.Style.Ghost);

            loadoutNote = UIKit.Label(col, "", 19, TextAnchor.UpperLeft, UIKit.TextDim);
            loadoutNote.rectTransform.SetTop(y + 20f, 80f, 94f);
            SetOpen(false);
        }

        Button Btn(RectTransform parent, string label, ref float y, System.Action a, UIKit.Style style = UIKit.Style.Normal)
        {
            var b = UIKit.Button(parent, label, a, style, style == UIKit.Style.Primary ? 30 : 25, true);
            ((RectTransform)b.transform).SetTop(y, 62f, 90f);
            y += 70f;
            return b;
        }

        void RefreshNote()
        {
            if (!loadoutNote) return;
            var lo = PlayerHealth.Local ? PlayerHealth.Local.loadout : null;
            loadoutNote.text = lo && lo.ChoicePending
                ? "<color=#FFBD42>" + PlayerLoadout.Primary + " · " + PlayerLoadout.Secondary + " · " + PlayerLoadout.Melee + "</color> — при следующем возрождении."
                : "Оружие: " + PlayerLoadout.Primary + " · " + PlayerLoadout.Secondary + " · " + PlayerLoadout.Melee;
        }

        /// <summary>Legacy helper (old menus): a styled button at a position.</summary>
        public static Button MakeButton(Transform parent, string label, Vector2 pos, UnityEngine.Events.UnityAction onClick, float width = 420f, Font f = null)
        {
            var b = UIKit.Button(parent, label, () => onClick?.Invoke());
            var rt = (RectTransform)b.transform;
            rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(width, 64f);
            return b;
        }

        void SetOpen(bool o)
        {
            open = o;
            group.alpha = o ? 1f : 0f;
            group.blocksRaycasts = o;
            group.interactable = o;
            if (!o) MenuPanel.CloseAll();
            else RefreshNote();
            bool matchOver = MatchManager.Instance && MatchManager.Instance.Ended;
            if (!matchOver && !Net.Active) Time.timeScale = o ? 0f : 1f; // a network match never pauses
            Cursor.lockState = o ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = o;
        }

        public void Toggle() => SetOpen(!open);
        void Resume() => SetOpen(false);

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !MenuPanel.EscUsedThisFrame && !MenuPanel.AnyOpen)
            {
                if (MatchManager.Instance && MatchManager.Instance.Ended) { Time.timeScale = 1f; GameInput.Blocked = false; NetGame.Leave(); SceneManager.LoadScene("MainMenu"); return; }
                Toggle();
            }
        }

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; if (open) Time.timeScale = 1f; }
        }
    }
}
