using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FPS
{
    /// <summary>Rebindable gameplay actions (order = order in the settings list).</summary>
    public enum BindAction
    {
        MoveForward, MoveBack, MoveLeft, MoveRight, Jump, Sprint, CrouchHold, CrouchToggle, Prone, LeanLeft, LeanRight,
        Fire, Aim, Reload, FireMode, Primary, Secondary, Melee, Grenade, Pickup, Ability, Scoreboard, Hints,
    }

    /// <summary>
    /// Player settings (PlayerPrefs): mouse, FOV, sound, screen, hold/press modes and key bindings.
    /// A binding is an Input System <see cref="Key"/> value, or <see cref="MouseBase"/> + mouse button index.
    /// </summary>
    public static class GameSettings
    {
        public const int MouseBase = 1000; // 1000 left, 1001 right, 1002 middle, 1003 back, 1004 forward
        const string P = "fps.set.";

        // ------------------------------------------------------------------ values

        /// <summary>Mouse sensitivity multiplier (1 = default 0.085 deg / pixel).</summary>
        public static float Sensitivity = 1f;
        /// <summary>Extra multiplier while aiming down sights.</summary>
        public static float AdsSensitivity = 1f;
        public static bool InvertY;
        /// <summary>true: Shift switches sprint on (until you stop running); false: hold Shift.</summary>
        public static bool SprintToggle;
        /// <summary>true: right mouse button switches aiming on/off; false: hold.</summary>
        public static bool AimToggle;
        /// <summary>true: crouch key (Ctrl) switches crouch; false: hold.</summary>
        public static bool CrouchHoldIsToggle;
        public static float Fov = 64f;
        public static float Volume = 0.9f;
        public static bool Fullscreen = true;
        public static bool VSync = true;
        public static bool ShowFps;
        public static int CrosshairColor; // index in CrosshairColors

        public static readonly Color[] CrosshairColors =
        {
            new Color(1f, 1f, 1f, 0.9f), new Color(0.35f, 1f, 0.45f, 0.95f), new Color(0.3f, 0.95f, 1f, 0.95f),
            new Color(1f, 0.9f, 0.25f, 0.95f), new Color(1f, 0.35f, 0.9f, 0.95f), new Color(1f, 0.3f, 0.25f, 0.95f),
        };
        public static readonly string[] CrosshairColorNames = { "Белый", "Зелёный", "Голубой", "Жёлтый", "Розовый", "Красный" };

        static readonly int[] Defaults =
        {
            (int)Key.W, (int)Key.S, (int)Key.A, (int)Key.D, (int)Key.Space, (int)Key.LeftShift, (int)Key.LeftCtrl, (int)Key.C, (int)Key.Z,
            (int)Key.Q, (int)Key.E, MouseBase + 0, MouseBase + 1, (int)Key.R, (int)Key.B, (int)Key.Digit1, (int)Key.Digit2,
            (int)Key.F, (int)Key.G, (int)Key.X, (int)Key.V, (int)Key.Tab, (int)Key.H,
        };

        public static readonly string[] ActionNames =
        {
            "Вперёд", "Назад", "Влево", "Вправо", "Прыжок", "Бег", "Присесть (зажатие)", "Присесть (переключение)", "Лечь",
            "Выглянуть влево", "Выглянуть вправо", "Стрельба", "Прицеливание", "Перезарядка", "Режим огня", "Основное оружие", "Пистолет",
            "Нож / фонарик (держать)", "Граната", "Подобрать", "Способность", "Таблица счёта", "Подсказки",
        };

        static readonly int[] binds = (int[])Defaults.Clone();
        static bool loaded;

        public static event Action Changed;

        // ------------------------------------------------------------------ persistence

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() { loaded = false; EnsureLoaded(); }

        public static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            Sensitivity = PlayerPrefs.GetFloat(P + "sens", 1f);
            AdsSensitivity = PlayerPrefs.GetFloat(P + "adsSens", 1f);
            InvertY = PlayerPrefs.GetInt(P + "invertY", 0) == 1;
            SprintToggle = PlayerPrefs.GetInt(P + "sprintToggle", 0) == 1;
            AimToggle = PlayerPrefs.GetInt(P + "aimToggle", 0) == 1;
            CrouchHoldIsToggle = PlayerPrefs.GetInt(P + "crouchToggle", 0) == 1;
            Fov = PlayerPrefs.GetFloat(P + "fov", 64f);
            Volume = PlayerPrefs.GetFloat(P + "volume", 0.9f);
            Fullscreen = PlayerPrefs.GetInt(P + "fullscreen", Screen.fullScreen ? 1 : 0) == 1;
            VSync = PlayerPrefs.GetInt(P + "vsync", 1) == 1;
            ShowFps = PlayerPrefs.GetInt(P + "fps", 0) == 1;
            CrosshairColor = Mathf.Clamp(PlayerPrefs.GetInt(P + "xhair", 0), 0, CrosshairColors.Length - 1);
            int n = Enum.GetValues(typeof(BindAction)).Length;
            for (int i = 0; i < n; i++) binds[i] = PlayerPrefs.GetInt("fps.bind." + (BindAction)i, Defaults[i]);
            Apply(false);
        }

        public static void Save()
        {
            PlayerPrefs.SetFloat(P + "sens", Sensitivity);
            PlayerPrefs.SetFloat(P + "adsSens", AdsSensitivity);
            PlayerPrefs.SetInt(P + "invertY", InvertY ? 1 : 0);
            PlayerPrefs.SetInt(P + "sprintToggle", SprintToggle ? 1 : 0);
            PlayerPrefs.SetInt(P + "aimToggle", AimToggle ? 1 : 0);
            PlayerPrefs.SetInt(P + "crouchToggle", CrouchHoldIsToggle ? 1 : 0);
            PlayerPrefs.SetFloat(P + "fov", Fov);
            PlayerPrefs.SetFloat(P + "volume", Volume);
            PlayerPrefs.SetInt(P + "fullscreen", Fullscreen ? 1 : 0);
            PlayerPrefs.SetInt(P + "vsync", VSync ? 1 : 0);
            PlayerPrefs.SetInt(P + "fps", ShowFps ? 1 : 0);
            PlayerPrefs.SetInt(P + "xhair", CrosshairColor);
            for (int i = 0; i < binds.Length; i++) PlayerPrefs.SetInt("fps.bind." + (BindAction)i, binds[i]);
            PlayerPrefs.Save();
        }

        /// <summary>Pushes the values into the engine (and saves when asked).</summary>
        public static void Apply(bool save = true)
        {
            GameInput.MouseSensitivity = 0.085f * Mathf.Clamp(Sensitivity, 0.05f, 20f);
            AudioListener.volume = Mathf.Clamp01(Volume / 0.9f); // 90% on the slider = the mix as designed
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            if (!Application.isEditor && Screen.fullScreen != Fullscreen)
                Screen.fullScreenMode = Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (save) Save();
            Changed?.Invoke();
        }

        public static void ResetBinds()
        {
            for (int i = 0; i < binds.Length; i++) binds[i] = Defaults[i];
            Apply();
        }

        public static void ResetAll()
        {
            Sensitivity = 1f; AdsSensitivity = 1f; InvertY = false; SprintToggle = false; AimToggle = false; CrouchHoldIsToggle = false;
            Fov = 64f; Volume = 0.9f; VSync = true; ShowFps = false; CrosshairColor = 0;
            ResetBinds();
        }

        // ------------------------------------------------------------------ bindings

        public static int Get(BindAction a) { EnsureLoaded(); return binds[(int)a]; }

        /// <summary>Assigns a key; if another action used it, that action gets this one's old key (swap).</summary>
        public static void Set(BindAction a, int code)
        {
            EnsureLoaded();
            int old = binds[(int)a];
            for (int i = 0; i < binds.Length; i++)
                if (i != (int)a && binds[i] == code) binds[i] = old;
            binds[(int)a] = code;
            Apply();
        }

        static ButtonControl Control(int code)
        {
            if (code >= MouseBase)
            {
                var m = Mouse.current;
                if (m == null) return null;
                switch (code - MouseBase)
                {
                    case 0: return m.leftButton;
                    case 1: return m.rightButton;
                    case 2: return m.middleButton;
                    case 3: return m.backButton;
                    case 4: return m.forwardButton;
                }
                return null;
            }
            var k = Keyboard.current;
            if (k == null || code <= 0) return null;
            try { return k[(Key)code]; } catch (Exception) { return null; }
        }

        public static bool Held(BindAction a) { var c = Control(Get(a)); return c != null && c.isPressed; }
        public static bool Pressed(BindAction a) { var c = Control(Get(a)); return c != null && c.wasPressedThisFrame; }
        public static bool Released(BindAction a) { var c = Control(Get(a)); return c != null && c.wasReleasedThisFrame; }

        /// <summary>Key / button pressed this frame (for rebinding), or -1. Escape is reported as 0 (cancel).</summary>
        public static int PollAnyPressed()
        {
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.escapeKey.wasPressedThisFrame) return 0;
                foreach (var key in k.allKeys)
                    if (key != null && key.wasPressedThisFrame && key.keyCode != Key.None) return (int)key.keyCode;
            }
            var m = Mouse.current;
            if (m != null)
            {
                if (m.leftButton.wasPressedThisFrame) return MouseBase + 0;
                if (m.rightButton.wasPressedThisFrame) return MouseBase + 1;
                if (m.middleButton.wasPressedThisFrame) return MouseBase + 2;
                if (m.backButton.wasPressedThisFrame) return MouseBase + 3;
                if (m.forwardButton.wasPressedThisFrame) return MouseBase + 4;
            }
            return -1;
        }

        public static string KeyName(BindAction a) => CodeName(Get(a));

        public static string CodeName(int code)
        {
            if (code >= MouseBase)
            {
                switch (code - MouseBase)
                {
                    case 0: return "ЛКМ";
                    case 1: return "ПКМ";
                    case 2: return "СКМ";
                    case 3: return "Мышь 4";
                    case 4: return "Мышь 5";
                }
                return "Мышь";
            }
            var key = (Key)code;
            switch (key)
            {
                case Key.None: return "—";
                case Key.Space: return "Пробел";
                case Key.LeftShift: return "L-Shift";
                case Key.RightShift: return "R-Shift";
                case Key.LeftCtrl: return "L-Ctrl";
                case Key.RightCtrl: return "R-Ctrl";
                case Key.LeftAlt: return "L-Alt";
                case Key.RightAlt: return "R-Alt";
                case Key.Tab: return "Tab";
                case Key.Enter: return "Enter";
                case Key.Backspace: return "Backspace";
                case Key.CapsLock: return "Caps Lock";
                case Key.Backquote: return "`";
                case Key.Minus: return "-";
                case Key.Equals: return "=";
                case Key.LeftBracket: return "[";
                case Key.RightBracket: return "]";
                case Key.Semicolon: return ";";
                case Key.Quote: return "'";
                case Key.Comma: return ",";
                case Key.Period: return ".";
                case Key.Slash: return "/";
                case Key.Backslash: return "\\";
                case Key.UpArrow: return "↑";
                case Key.DownArrow: return "↓";
                case Key.LeftArrow: return "←";
                case Key.RightArrow: return "→";
            }
            string s = key.ToString();
            if (s.StartsWith("Digit")) return s.Substring(5);
            if (s.StartsWith("Numpad")) return "Num " + s.Substring(6);
            return s;
        }
    }
}
