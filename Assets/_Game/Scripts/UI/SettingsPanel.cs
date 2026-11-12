using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Settings window: controls (mouse, hold / press modes for sprint, aim and crouch, crosshair),
    /// key bindings (click a key, press a new one; Esc cancels) and screen / sound.
    /// </summary>
    public static class SettingsPanel
    {
        static readonly string[] HoldPress = { "Зажатие", "Нажатие" };
        static readonly string[] OffOn = { "Выкл", "Вкл" };

        public static MenuPanel Open(Transform canvas, Action onClose = null)
        {
            GameSettings.EnsureLoaded();
            var panel = MenuPanel.Create(canvas, "Настройки", new Vector2(1240f, 880f), out var body);
            panel.onClose = () => { GameSettings.Save(); onClose?.Invoke(); };

            var area = UIKit.Rect("Area", body);
            UIKit.Fill(area, 0f, 84f, 0f, 0f);
            var pages = UIKit.Tabs(area, new[] { "Управление", "Клавиши", "Экран и звук" });

            BuildControls(UIKit.ScrollList(pages[0]));
            var rebinding = BuildKeys(UIKit.ScrollList(pages[1]), panel);
            BuildScreen(UIKit.ScrollList(pages[2]));

            // footer
            var reset = UIKit.Button(body, "Сбросить всё", () =>
            {
                GameSettings.ResetAll();
                var c = panel.transform.parent;
                panel.onClose = null;
                panel.Close();
                Open(c, onClose); // rebuild with the default values
            }, UIKit.Style.Danger, 22);
            Place(reset, new Vector2(0f, 0f), new Vector2(260f, 60f));
            var done = UIKit.Button(body, "Готово", () => panel.Close(), UIKit.Style.Primary, 26);
            Place(done, new Vector2(1f, 0f), new Vector2(300f, 60f));
            panel.onEscape = () => rebinding();
            return panel;
        }

        static void Place(Button b, Vector2 anchor, Vector2 size)
        {
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
        }

        static void Changed() => GameSettings.Apply(false);

        static void BuildControls(RectTransform list)
        {
            UIKit.Header(list, "Мышь");
            UIKit.SliderRow(list, "Чувствительность", 0.1f, 5f, GameSettings.Sensitivity, v => v.ToString("0.00"),
                            v => { GameSettings.Sensitivity = v; Changed(); });
            UIKit.SliderRow(list, "Чувствительность в прицеле", 0.2f, 2f, GameSettings.AdsSensitivity, v => "×" + v.ToString("0.00"),
                            v => { GameSettings.AdsSensitivity = v; Changed(); }, "Множитель при прицеливании и в оптике");
            UIKit.ChoiceRow(list, "Инверсия по вертикали", OffOn, GameSettings.InvertY ? 1 : 0, i => { GameSettings.InvertY = i == 1; Changed(); });

            UIKit.Header(list, "Режимы клавиш");
            UIKit.ChoiceRow(list, "Бег  (" + GameSettings.KeyName(BindAction.Sprint) + ")", HoldPress, GameSettings.SprintToggle ? 1 : 0,
                            i => { GameSettings.SprintToggle = i == 1; Changed(); }, "Нажатие: бег включается и держится, пока вы бежите вперёд");
            UIKit.ChoiceRow(list, "Прицеливание  (" + GameSettings.KeyName(BindAction.Aim) + ")", HoldPress, GameSettings.AimToggle ? 1 : 0,
                            i => { GameSettings.AimToggle = i == 1; Changed(); });
            UIKit.ChoiceRow(list, "Присед  (" + GameSettings.KeyName(BindAction.CrouchHold) + ")", HoldPress, GameSettings.CrouchHoldIsToggle ? 1 : 0,
                            i => { GameSettings.CrouchHoldIsToggle = i == 1; Changed(); }, GameSettings.KeyName(BindAction.CrouchToggle) + " — всегда переключение");

            UIKit.Header(list, "Прицел");
            UIKit.SelectorRow(list, "Цвет перекрестия", GameSettings.CrosshairColors.Length, () => GameSettings.CrosshairColor,
                              i => { GameSettings.CrosshairColor = i; Changed(); }, i => GameSettings.CrosshairColorNames[i], i => GameSettings.CrosshairColors[i]);
        }

        /// <summary>Key list; returns "Esc handler" (true while a key is being captured — Esc cancels it).</summary>
        static Func<bool> BuildKeys(RectTransform list, MenuPanel panel)
        {
            var labels = new Dictionary<BindAction, Text>();
            var fxs = new Dictionary<BindAction, UIButtonFx>();
            BindAction? waiting = null;
            int startFrame = 0;

            void RefreshAll()
            {
                foreach (var kv in labels) kv.Value.text = GameSettings.KeyName(kv.Key);
                foreach (var kv in fxs) kv.Value.Selected = waiting == kv.Key;
                if (waiting.HasValue) labels[waiting.Value].text = "Нажмите клавишу…";
            }

            string[] groups = { "Движение", "Бой", "Прочее" };
            BindAction[] firstOf = { BindAction.MoveForward, BindAction.Fire, BindAction.Scoreboard };
            foreach (BindAction a in Enum.GetValues(typeof(BindAction)))
            {
                int g = Array.IndexOf(firstOf, a);
                if (g >= 0) UIKit.Header(list, groups[g]);
                var row = UIKit.Row(list, 56f);
                UIKit.RowLabel(row, GameSettings.ActionNames[(int)a]);
                var act = a;
                var b = UIKit.Button(row, "", () =>
                {
                    waiting = act;
                    startFrame = Time.frameCount;
                    RefreshAll();
                }, UIKit.Style.Normal, 22);
                var rt = (RectTransform)b.transform;
                UIKit.Span(rt, 0.62f, 0.12f, 1f, 0.88f);
                rt.offsetMax = new Vector2(-12f, 0f);
                labels[a] = UIKit.LabelOf(b);
                fxs[a] = b.GetComponent<UIButtonFx>();
            }

            var resetRow = UIKit.Row(list, 70f);
            var reset = UIKit.Button(resetRow, "Сбросить клавиши по умолчанию", () => { waiting = null; GameSettings.ResetBinds(); RefreshAll(); }, UIKit.Style.Normal, 22);
            UIKit.Span((RectTransform)reset.transform, 0.25f, 0.1f, 0.75f, 0.9f);
            var note = UIKit.Row(list, 40f);
            var nt = UIKit.Label(note, "Если клавиша уже занята, действия поменяются клавишами. Esc — отмена.", 18, TextAnchor.MiddleCenter, UIKit.TextDim);
            UIKit.Fill(nt.rectTransform);
            RefreshAll();

            panel.onUpdate += () =>
            {
                if (!waiting.HasValue || Time.frameCount <= startFrame + 1) return;
                int code = GameSettings.PollAnyPressed();
                if (code < 0) return;
                if (code == 0) return; // Esc — handled by onEscape (cancel)
                GameSettings.Set(waiting.Value, code);
                waiting = null;
                ProcAudio.Play(Sfx.Switch, 0.4f);
                RefreshAll();
            };
            return () =>
            {
                if (!waiting.HasValue) return false;
                waiting = null;
                RefreshAll();
                return true;
            };
        }

        static void BuildScreen(RectTransform list)
        {
            UIKit.Header(list, "Изображение");
            UIKit.SliderRow(list, "Поле зрения (FOV)", 55f, 90f, GameSettings.Fov, v => Mathf.RoundToInt(v) + "°",
                            v => { GameSettings.Fov = Mathf.Round(v); Changed(); }, "Вертикальный угол обзора камеры", true);
            UIKit.ChoiceRow(list, "Полноэкранный режим", OffOn, GameSettings.Fullscreen ? 1 : 0, i => { GameSettings.Fullscreen = i == 1; Changed(); });
            UIKit.ChoiceRow(list, "Вертикальная синхронизация", OffOn, GameSettings.VSync ? 1 : 0, i => { GameSettings.VSync = i == 1; Changed(); });
            UIKit.ChoiceRow(list, "Счётчик FPS", OffOn, GameSettings.ShowFps ? 1 : 0, i => { GameSettings.ShowFps = i == 1; Changed(); });
            UIKit.Header(list, "Звук");
            UIKit.SliderRow(list, "Общая громкость", 0f, 1f, GameSettings.Volume, v => Mathf.RoundToInt(v * 100f) + "%",
                            v => { GameSettings.Volume = v; Changed(); });
        }
    }
}
