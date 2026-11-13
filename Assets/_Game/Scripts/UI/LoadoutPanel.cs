using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Loadout window. Three slots (tabs on top): primary — split into categories, secondary (pistols) and melee.
    /// Left: a grid of weapon cards of the current category; right: details and stat bars (hover previews).
    /// From the main menu it applies to the next match; during a match — on the next respawn.
    /// </summary>
    public static class LoadoutPanel
    {
        static readonly string[] GunStats = { "Урон", "Скорострельность", "Дальность", "Контроль", "Мобильность" };
        static readonly string[] MeleeStats = { "Урон", "Скорость", "Дальность" };
        static readonly WeaponCategory[] PrimaryCats = { WeaponCategory.Assault, WeaponCategory.Marksman, WeaponCategory.SMG, WeaponCategory.Shotgun };

        enum Slot { Primary, Secondary, Melee }

        public static MenuPanel Open(Transform canvas, bool inMatch, Action onClose = null)
        {
            var panel = MenuPanel.Create(canvas, "Снаряжение", new Vector2(1440f, 860f), out var body);
            panel.onClose = onClose;

            Slot slot = Slot.Primary;
            WeaponCategory cat = PlayerLoadout.Info(PlayerLoadout.Primary).category;

            // ---- slot tabs (top), each shows the current choice
            var tabBar = UIKit.Rect("SlotTabs", body);
            tabBar.SetTop(0f, 74f);
            var slotFx = new UIButtonFx[3];
            var slotSub = new Text[3];
            string[] slotNames = { "ОСНОВНОЕ", "ВТОРИЧНОЕ", "БЛИЖНИЙ БОЙ" };

            // ---- category chips + card grid (left)
            var left = UIKit.Rect("Left", body);
            UIKit.Span(left, 0f, 0f, 0.46f, 1f);
            left.offsetMin = new Vector2(0f, 84f);
            left.offsetMax = new Vector2(0f, -90f);
            var chips = UIKit.Rect("Categories", left);
            chips.SetTop(0f, 46f);
            var grid = UIKit.Rect("Grid", left);
            UIKit.Fill(grid, 0f, 0f, 0f, 60f);

            // ---- details (right)
            var right = UIKit.Panel(body, "Details", UIKit.Card, 12).rectTransform;
            UIKit.Span(right, 0.48f, 0f, 1f, 1f);
            right.offsetMin = new Vector2(0f, 84f);
            right.offsetMax = new Vector2(0f, -90f);
            var title = UIKit.Label(right, "", 50, TextAnchor.UpperLeft, UIKit.TextC, true);
            title.rectTransform.SetTop(24f, 64f, 32f);
            var kind = UIKit.Label(right, "", 22, TextAnchor.UpperLeft, UIKit.Accent, true);
            kind.rectTransform.SetTop(88f, 30f, 34f);
            var desc = UIKit.Label(right, "", 21, TextAnchor.UpperLeft, UIKit.TextDim);
            desc.rectTransform.SetTop(126f, 60f, 34f);
            var statNames = new Text[GunStats.Length];
            var bars = new Image[GunStats.Length];
            for (int i = 0; i < GunStats.Length; i++)
            {
                float y = 206f + i * 46f;
                statNames[i] = UIKit.Label(right, GunStats[i], 21, TextAnchor.MiddleLeft);
                statNames[i].rectTransform.SetTop(y, 30f, 34f);
                var holder = UIKit.Rect("BarHolder", right);
                holder.SetTop(y + 10f, 10f, 34f);
                holder.offsetMin = new Vector2(280f, holder.offsetMin.y);
                bars[i] = UIKit.Bar(holder, 0.5f, UIKit.Accent);
                UIKit.Fill(bars[i].transform.parent as RectTransform);
            }
            var mag = UIKit.Label(right, "", 21, TextAnchor.UpperLeft, UIKit.TextDim);
            mag.rectTransform.SetTop(442f, 30f, 34f);
            var kit = UIKit.Label(right, "", 19, TextAnchor.UpperLeft, UIKit.TextC);
            kit.rectTransform.SetTop(486f, 90f, 34f);

            var cards = new List<(UIButtonFx fx, string name)>();
            var chipFx = new List<UIButtonFx>();

            string Current() => slot == Slot.Primary ? PlayerLoadout.Primary : slot == Slot.Secondary ? PlayerLoadout.Secondary : PlayerLoadout.Melee;

            void SetBars(int count, float[] v, string[] names)
            {
                for (int i = 0; i < bars.Length; i++)
                {
                    bool on = i < count;
                    statNames[i].gameObject.SetActive(on);
                    bars[i].transform.parent.parent.gameObject.SetActive(on);
                    if (!on) continue;
                    statNames[i].text = names[i];
                    bars[i].rectTransform.anchorMax = new Vector2(Mathf.Clamp01(v[i]), 1f);
                }
            }

            void ShowGun(WeaponInfo w)
            {
                title.text = w.name;
                kind.text = w.kind.ToUpperInvariant();
                desc.text = w.desc;
                SetBars(5, new[] { w.damage, w.fireRate, w.range, w.control, w.mobility }, GunStats);
                mag.text = "Магазин: " + w.mag + " " + Rounds(w.mag);
            }

            void ShowMelee(MeleeDef m)
            {
                title.text = m.name;
                kind.text = "ХОЛОДНОЕ ОРУЖИЕ";
                desc.text = m.desc;
                SetBars(3, new[] { m.uiDamage, m.uiSpeed, m.uiRange }, MeleeStats);
                mag.text = "Удар: " + Mathf.RoundToInt(m.damage) + " · в спину: " + Mathf.RoundToInt(m.damage * m.backstab) + "  (" + GameSettings.KeyName(BindAction.Melee) + ")";
            }

            void ShowName(string n)
            {
                if (slot == Slot.Melee) ShowMelee(MeleeCatalog.Get(n)); else ShowGun(PlayerLoadout.Info(n));
            }

            void RefreshSelection()
            {
                string cur = Current();
                foreach (var c in cards) c.fx.Selected = c.name == cur;
                for (int i = 0; i < 3; i++) slotFx[i].Selected = i == (int)slot;
                slotSub[0].text = PlayerLoadout.Primary;
                slotSub[1].text = PlayerLoadout.Secondary;
                slotSub[2].text = PlayerLoadout.Melee;
                for (int i = 0; i < chipFx.Count; i++) chipFx[i].Selected = PrimaryCats[i] == cat;
                kit.text = "<color=#FFBD42>КОМПЛЕКТ</color>   " + PlayerLoadout.Primary + " · " + PlayerLoadout.Secondary + " · " + PlayerLoadout.Melee +
                           "\n<color=#FFBD42>СНАРЯЖЕНИЕ</color>   3 осколочные гранаты (" + GameSettings.KeyName(BindAction.Grenade) + ") · способность с карты (" + GameSettings.KeyName(BindAction.Ability) + ")";
            }

            void Card(string name, string sub, int index, int total, Action pick)
            {
                const int cols = 2;
                const float h = 84f, gap = 10f;
                int rows = (total + cols - 1) / cols;
                float rowH = Mathf.Min(h, (grid.rect.height > 1f ? grid.rect.height : 600f) / Mathf.Max(1, rows) - gap);
                var b = UIKit.Button(grid, "", () => { pick(); ShowName(name); RefreshSelection(); }, UIKit.Style.Normal, 26, true);
                var rt = (RectTransform)b.transform;
                int c = index % cols, r = index / cols;
                rt.anchorMin = new Vector2(c / (float)cols, 1f); rt.anchorMax = new Vector2((c + 1) / (float)cols, 1f); rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(c == 0 ? 0f : gap * 0.5f, -(r * (rowH + gap)) - rowH);
                rt.offsetMax = new Vector2(c == cols - 1 ? 0f : -gap * 0.5f, -(r * (rowH + gap)));
                var lbl = UIKit.LabelOf(b);
                lbl.text = name;
                lbl.alignment = TextAnchor.UpperLeft;
                lbl.rectTransform.offsetMax = new Vector2(-8f, -12f);
                var sl = UIKit.Label(rt, sub, 17, TextAnchor.LowerLeft, UIKit.TextDim);
                UIKit.Fill(sl.rectTransform, 26f, 10f, 8f, 0f);
                cards.Add((b.GetComponent<UIButtonFx>(), name));
                var hover = b.gameObject.AddComponent<UIHover>();
                hover.enter = () => ShowName(name);
                hover.exit = () => ShowName(Current());
            }

            void Rebuild()
            {
                for (int i = grid.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(grid.GetChild(i).gameObject);
                for (int i = chips.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(chips.GetChild(i).gameObject);
                cards.Clear();
                chipFx.Clear();

                if (slot == Slot.Primary)
                {
                    for (int i = 0; i < PrimaryCats.Length; i++)
                    {
                        var k = PrimaryCats[i];
                        var cb = UIKit.Button(chips, WeaponCatalog.CategoryNames[(int)k], () => { cat = k; Rebuild(); }, UIKit.Style.Ghost, 19);
                        UIKit.Span((RectTransform)cb.transform, i / (float)PrimaryCats.Length, 0f, (i + 1) / (float)PrimaryCats.Length, 1f);
                        ((RectTransform)cb.transform).offsetMin = new Vector2(i == 0 ? 0f : 3f, 0f);
                        ((RectTransform)cb.transform).offsetMax = new Vector2(i == PrimaryCats.Length - 1 ? 0f : -3f, 0f);
                        chipFx.Add(cb.GetComponent<UIButtonFx>());
                    }
                }
                else
                {
                    var head = UIKit.Label(chips, slot == Slot.Secondary ? "ПИСТОЛЕТЫ" : "ХОЛОДНОЕ ОРУЖИЕ", 20, TextAnchor.MiddleLeft, UIKit.Accent, true);
                    UIKit.Fill(head.rectTransform);
                }

                if (slot == Slot.Melee)
                {
                    var all = MeleeCatalog.All;
                    for (int i = 0; i < all.Length; i++)
                    {
                        var m = all[i];
                        Card(m.name, "Урон " + Mathf.RoundToInt(m.damage), i, all.Length, () => PlayerLoadout.Melee = m.name);
                    }
                }
                else
                {
                    var list = new List<WeaponInfo>();
                    foreach (var w in PlayerLoadout.Weapons)
                        if (slot == Slot.Secondary ? w.secondary : !w.secondary && w.category == cat) list.Add(w);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var w = list[i];
                        Card(w.name, w.kind, i, list.Count, () => { if (slot == Slot.Primary) PlayerLoadout.Primary = w.name; else PlayerLoadout.Secondary = w.name; });
                    }
                }
                ShowName(Current());
                RefreshSelection();
            }

            for (int i = 0; i < 3; i++)
            {
                var s = (Slot)i;
                var b = UIKit.Button(tabBar, "", () =>
                {
                    slot = s;
                    if (s == Slot.Primary) cat = PlayerLoadout.Info(PlayerLoadout.Primary).category;
                    Rebuild();
                }, UIKit.Style.Ghost, 21, true);
                var rt = (RectTransform)b.transform;
                UIKit.Span(rt, i / 3f, 0f, (i + 1) / 3f, 1f);
                rt.offsetMin = new Vector2(i == 0 ? 0f : 5f, 0f);
                rt.offsetMax = new Vector2(i == 2 ? 0f : -5f, 0f);
                var lbl = UIKit.LabelOf(b);
                lbl.text = slotNames[i];
                lbl.fontSize = 18;
                lbl.color = UIKit.TextDim;
                lbl.alignment = TextAnchor.UpperLeft;
                lbl.rectTransform.offsetMax = new Vector2(-8f, -10f);
                slotSub[i] = UIKit.Label(rt, "", 26, TextAnchor.LowerLeft, UIKit.TextC, true);
                UIKit.Fill(slotSub[i].rectTransform, 26f, 8f, 8f, 0f);
                slotFx[i] = b.GetComponent<UIButtonFx>();
            }
            Rebuild();

            // ---- footer
            string note = inMatch ? "Новое снаряжение выдаётся при следующем возрождении." : "Выбор сохраняется и действует во всех матчах.";
            var nt = UIKit.Label(body, note, 20, TextAnchor.MiddleLeft, UIKit.TextDim);
            nt.rectTransform.anchorMin = Vector2.zero; nt.rectTransform.anchorMax = new Vector2(0.7f, 0f); nt.rectTransform.pivot = new Vector2(0f, 0f);
            nt.rectTransform.sizeDelta = new Vector2(0f, 60f); nt.rectTransform.anchoredPosition = Vector2.zero;
            var done = UIKit.Button(body, "Готово", () => panel.Close(), UIKit.Style.Primary, 26);
            var drt = (RectTransform)done.transform;
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1f, 0f);
            drt.sizeDelta = new Vector2(300f, 60f); drt.anchoredPosition = Vector2.zero;
            return panel;
        }

        static string Rounds(int n)
        {
            int a = n % 100, b = n % 10;
            if (a >= 11 && a <= 14) return "патронов";
            return b == 1 ? "патрон" : b >= 2 && b <= 4 ? "патрона" : "патронов";
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
