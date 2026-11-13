using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Character editor: call sign + appearance (build, skin, headwear, face gear, clothes, vest, backpack, colors)
    /// with a live, rotatable 3D preview. Saved to PlayerPrefs; other players see it in network games.
    /// </summary>
    public static class CharacterPanel
    {
        public static MenuPanel Open(Transform canvas, Action onClose = null)
        {
            var look = CharacterLook.Local;
            var backup = look.Clone();
            var preview = LookPreview.Ensure();
            var panel = MenuPanel.Create(canvas, "Персонаж", new Vector2(1500f, 900f), out var body);
            panel.onClose = () => { CharacterLook.SaveLocal(); preview.Apply(); onClose?.Invoke(); };

            // ---- preview (left, 3:4)
            var frame = UIKit.Panel(body, "PreviewFrame", Color.white, 14);
            var frt = frame.rectTransform;
            frt.anchorMin = frt.anchorMax = frt.pivot = new Vector2(0f, 1f);
            frt.sizeDelta = new Vector2(540f, 720f);
            frt.anchoredPosition = Vector2.zero;
            frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            preview.Show(frt);
            var nickPlate = UIKit.Label(body, look.nick, 30, TextAnchor.MiddleCenter, UIKit.TextC, true);
            nickPlate.rectTransform.anchorMin = nickPlate.rectTransform.anchorMax = nickPlate.rectTransform.pivot = new Vector2(0f, 1f);
            nickPlate.rectTransform.sizeDelta = new Vector2(540f, 44f);
            nickPlate.rectTransform.anchoredPosition = new Vector2(0f, -728f);
            var dragHint = UIKit.Label(frt, "Потяните мышью, чтобы повернуть", 17, TextAnchor.LowerCenter, new Color(1f, 1f, 1f, 0.4f));
            UIKit.Fill(dragHint.rectTransform, 0f, 14f, 0f, 0f);

            // ---- options (right)
            var right = UIKit.Rect("Options", body);
            UIKit.Fill(right, 580f, 84f, 0f, 0f);
            var list = UIKit.ScrollList(right);
            var refreshers = new List<Action>();

            void Changed(bool structural = false)
            {
                if (structural) preview.Rebuild(); else preview.Apply();
            }

            UIKit.Header(list, "Позывной");
            var nickRow = UIKit.Row(list, 64f);
            UIKit.RowLabel(nickRow, "Ник в игре");
            var nick = UIKit.Input(nickRow, look.nick, "Игрок");
            UIKit.Span(nick.Rt(), 0.42f, 0.13f, 1f, 0.87f);
            nick.Rt().offsetMax = new Vector2(-12f, 0f);
            nick.characterLimit = CharacterLook.MaxNick;
            nick.onValueChanged.AddListener(s => nickPlate.text = CharacterLook.SanitizeNick(s));
            nick.onEndEdit.AddListener(s => { look.nick = CharacterLook.SanitizeNick(s); nick.text = look.nick; });

            var tones = CharacterLook.SkinTones;
            var colors = CharacterLook.Colors;
            string ColorName(int i) => CharacterLook.ColorNames[i];
            Color? Col(int i) => colors[i];

            UIKit.Header(list, "Модель");
            refreshers.Add(UIKit.SelectorRow(list, "Боец", CharacterLook.ModelCount, () => look.model, i => { look.model = (byte)i; Changed(); }, CharacterLook.ModelName));
            var modelHint = UIKit.Label(list, "Цвета и снаряжение ниже меняют только классического бойца.", 17, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.45f));
            modelHint.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;

            UIKit.Header(list, "Внешность");
            refreshers.Add(UIKit.SelectorRow(list, "Телосложение", CharacterLook.BuildNames.Length, () => look.build, i => { look.build = (byte)i; Changed(); }, i => CharacterLook.BuildNames[i]));
            refreshers.Add(UIKit.SelectorRow(list, "Тон кожи", tones.Length, () => look.skin, i => { look.skin = (byte)i; Changed(); }, i => CharacterLook.SkinNames[i], i => tones[i]));
            refreshers.Add(UIKit.SelectorRow(list, "Головной убор", CharacterLook.HeadwearNames.Length, () => look.headwear, i => { look.headwear = (byte)i; Changed(); }, i => CharacterLook.HeadwearNames[i]));
            refreshers.Add(UIKit.SelectorRow(list, "Цвет головного убора", colors.Length, () => look.helmet, i => { look.helmet = (byte)i; Changed(); }, ColorName, Col));
            refreshers.Add(UIKit.SelectorRow(list, "Лицо", CharacterLook.FaceNames.Length, () => look.face, i => { look.face = (byte)i; Changed(); }, i => CharacterLook.FaceNames[i]));

            UIKit.Header(list, "Одежда");
            refreshers.Add(UIKit.SelectorRow(list, "Куртка", colors.Length, () => look.shirt, i => { look.shirt = (byte)i; Changed(); }, ColorName, Col));
            refreshers.Add(UIKit.SelectorRow(list, "Брюки", colors.Length, () => look.pants, i => { look.pants = (byte)i; Changed(); }, ColorName, Col));

            UIKit.Header(list, "Снаряжение");
            refreshers.Add(UIKit.SelectorRow(list, "Жилет", CharacterLook.VestNames.Length, () => look.vestType, i => { look.vestType = (byte)i; Changed(); }, i => CharacterLook.VestNames[i]));
            refreshers.Add(UIKit.SelectorRow(list, "Цвет жилета", colors.Length, () => look.vest, i => { look.vest = (byte)i; Changed(); }, ColorName, Col));
            refreshers.Add(UIKit.SelectorRow(list, "Подсумки, очки, маска", colors.Length, () => look.gear, i => { look.gear = (byte)i; Changed(); }, ColorName, Col));
            refreshers.Add(UIKit.SelectorRow(list, "Рюкзак", CharacterLook.BackpackNames.Length, () => look.backpack, i => { look.backpack = (byte)i; Changed(); }, i => CharacterLook.BackpackNames[i]));

            // ---- footer
            var random = UIKit.Button(body, "Случайный облик", () =>
            {
                var r = CharacterLook.Random(new System.Random(UnityEngine.Random.Range(0, int.MaxValue)), look.nick);
                look.SetBytes(r.GetBytes());
                foreach (var f in refreshers) f();
                Changed();
            }, UIKit.Style.Normal, 22);
            Place(random, new Vector2(580f, 0f), new Vector2(280f, 60f), new Vector2(0f, 0f));
            var undo = UIKit.Button(body, "Отменить изменения", () =>
            {
                look.SetBytes(backup.GetBytes());
                look.nick = backup.nick; nick.text = look.nick; nickPlate.text = look.nick;
                foreach (var f in refreshers) f();
                Changed();
            }, UIKit.Style.Ghost, 22);
            Place(undo, new Vector2(880f, 0f), new Vector2(280f, 60f), new Vector2(0f, 0f));
            var done = UIKit.Button(body, "Сохранить", () =>
            {
                look.nick = CharacterLook.SanitizeNick(nick.text);
                panel.Close();
            }, UIKit.Style.Primary, 26);
            Place(done, Vector2.zero, new Vector2(300f, 60f), new Vector2(1f, 0f));
            return panel;
        }

        static void Place(Button b, Vector2 pos, Vector2 size, Vector2 anchor)
        {
            var rt = (RectTransform)b.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
        }
    }
}
