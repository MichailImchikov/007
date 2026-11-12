using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Loadout window: pick the primary weapon (cards on the left, stats on the right).
    /// From the main menu it applies to the next match; during a match — on the next respawn.
    /// </summary>
    public static class LoadoutPanel
    {
        static readonly string[] StatNames = { "Урон", "Скорострельность", "Дальность", "Контроль", "Мобильность" };

        public static MenuPanel Open(Transform canvas, bool inMatch, Action onClose = null)
        {
            var panel = MenuPanel.Create(canvas, "Снаряжение", new Vector2(1380f, 820f), out var body);
            panel.onClose = onClose;

            // ---- weapon cards (left)
            var left = UIKit.Rect("Cards", body);
            UIKit.Span(left, 0f, 0f, 0.38f, 1f);
            left.offsetMin = new Vector2(0f, 84f);
            UIKit.Label(left, "ОСНОВНОЕ ОРУЖИЕ", 20, TextAnchor.UpperLeft, UIKit.Accent, true).rectTransform.SetTop(0f, 30f);
            var cards = new List<UIButtonFx>();
            var primaries = new List<WeaponInfo>();
            foreach (var w in PlayerLoadout.Weapons) if (!w.secondary) primaries.Add(w);

            // ---- details (right)
            var right = UIKit.Panel(body, "Details", UIKit.Card, 12).rectTransform;
            UIKit.Span(right, 0.4f, 0f, 1f, 1f);
            right.offsetMin = new Vector2(0f, 84f);
            var title = UIKit.Label(right, "", 54, TextAnchor.UpperLeft, UIKit.TextC, true);
            title.rectTransform.SetTop(28f, 70f, 32f);
            var kind = UIKit.Label(right, "", 22, TextAnchor.UpperLeft, UIKit.Accent, true);
            kind.rectTransform.SetTop(96f, 30f, 34f);
            var desc = UIKit.Label(right, "", 21, TextAnchor.UpperLeft, UIKit.TextDim);
            desc.rectTransform.SetTop(136f, 60f, 34f);
            var bars = new Image[StatNames.Length];
            for (int i = 0; i < StatNames.Length; i++)
            {
                float y = 222f + i * 50f;
                var n = UIKit.Label(right, StatNames[i], 21, TextAnchor.MiddleLeft);
                n.rectTransform.SetTop(y, 30f, 34f);
                var holder = UIKit.Rect("BarHolder", right);
                holder.SetTop(y + 10f, 10f, 34f);
                holder.offsetMin = new Vector2(280f, holder.offsetMin.y);
                bars[i] = UIKit.Bar(holder, 0.5f, UIKit.Accent);
                UIKit.Fill(bars[i].transform.parent as RectTransform);
            }
            var mag = UIKit.Label(right, "", 21, TextAnchor.UpperLeft, UIKit.TextDim);
            mag.rectTransform.SetTop(476f, 30f, 34f);

            // secondary + equipment strip
            var pistol = PlayerLoadout.Info(PlayerLoadout.Secondary);
            var extra = UIKit.Label(right,
                "<color=#FFBD42>ВТОРИЧНОЕ</color>   " + pistol.name + " · " + pistol.kind + "\n" +
                "<color=#FFBD42>СНАРЯЖЕНИЕ</color>   Нож (" + GameSettings.KeyName(BindAction.Melee) + ") · 3 осколочные гранаты (" + GameSettings.KeyName(BindAction.Grenade) + ") · способность с карты (" + GameSettings.KeyName(BindAction.Ability) + ")",
                20, TextAnchor.UpperLeft, UIKit.TextC);
            extra.rectTransform.SetTop(520f, 70f, 34f);

            void Show(WeaponInfo w)
            {
                title.text = w.name;
                kind.text = w.kind.ToUpperInvariant();
                desc.text = w.desc;
                float[] v = { w.damage, w.fireRate, w.range, w.control, w.mobility };
                for (int i = 0; i < bars.Length; i++) bars[i].rectTransform.anchorMax = new Vector2(Mathf.Clamp01(v[i]), 1f);
                mag.text = "Магазин: " + w.mag + " патронов";
                for (int i = 0; i < cards.Count; i++) cards[i].Selected = primaries[i].name == PlayerLoadout.Primary;
            }

            for (int i = 0; i < primaries.Count; i++)
            {
                var w = primaries[i];
                var b = UIKit.Button(left, "", () => { PlayerLoadout.Primary = w.name; Show(w); }, UIKit.Style.Normal, 30, true);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(0f, 104f);
                rt.anchoredPosition = new Vector2(0f, -44f - i * 116f);
                var lbl = UIKit.LabelOf(b);
                lbl.text = w.name;
                lbl.alignment = TextAnchor.UpperLeft;
                lbl.rectTransform.offsetMax = new Vector2(-8f, -18f);
                var sub = UIKit.Label(rt, w.kind, 19, TextAnchor.LowerLeft, UIKit.TextDim);
                UIKit.Fill(sub.rectTransform, 26f, 18f, 8f, 0f);
                cards.Add(b.GetComponent<UIButtonFx>());
                // hover previews the weapon, leaving restores the chosen one
                var hover = b.gameObject.AddComponent<UIHover>();
                hover.enter = () => Show(w);
                hover.exit = () => Show(PlayerLoadout.Info(PlayerLoadout.Primary));
            }
            Show(PlayerLoadout.Info(PlayerLoadout.Primary));

            // ---- footer
            string note = inMatch ? "Новое оружие выдаётся при следующем возрождении." : "Выбор сохраняется и действует во всех матчах.";
            var nt = UIKit.Label(body, note, 20, TextAnchor.MiddleLeft, UIKit.TextDim);
            nt.rectTransform.anchorMin = Vector2.zero; nt.rectTransform.anchorMax = new Vector2(0.7f, 0f); nt.rectTransform.pivot = new Vector2(0f, 0f);
            nt.rectTransform.sizeDelta = new Vector2(0f, 60f); nt.rectTransform.anchoredPosition = Vector2.zero;
            var done = UIKit.Button(body, "Готово", () => panel.Close(), UIKit.Style.Primary, 26);
            var drt = (RectTransform)done.transform;
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1f, 0f);
            drt.sizeDelta = new Vector2(300f, 60f); drt.anchoredPosition = Vector2.zero;
            return panel;
        }
    }

    /// <summary>Pointer enter / exit callbacks.</summary>
    public class UIHover : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        public Action enter, exit;
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) => enter?.Invoke();
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) => exit?.Invoke();
    }

    public static class RectExt
    {
        public static RectTransform Rt(this Component c) => (RectTransform)c.transform;

        /// <summary>Stretch horizontally (with side insets) at a fixed distance from the parent's top.</summary>
        public static void SetTop(this RectTransform rt, float fromTop, float height, float inset = 0f)
        {
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(inset, -fromTop - height);
            rt.offsetMax = new Vector2(-inset, -fromTop);
        }
    }
}
