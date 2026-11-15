using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// "Модули" window of the loadout menu: the weapon's attachment slots, each with its compatible attachments as chips
    /// (plus "none" unless the slot is required). Hover shows the attachment's description and stat changes; a click
    /// saves the choice (PlayerLoadout) — applied at the start of a match or on the next respawn.
    /// </summary>
    public static class AttachmentsPanel
    {
        public static MenuPanel Open(Transform canvas, string weaponName, bool inMatch, Action onChanged = null, Action onClose = null)
        {
            var data = WeaponDataDB.Get(weaponName);
            if (!data) return null;
            var panel = MenuPanel.Create(canvas, "Модули · " + weaponName, new Vector2(1320f, 820f), out var body);
            panel.onClose = onClose;

            // ---- slots (left)
            var left = UIKit.Rect("Slots", body);
            UIKit.Span(left, 0f, 0f, 0.62f, 1f);
            left.offsetMin = new Vector2(0f, 80f);

            // ---- info card (right): the hovered / last chosen attachment
            var right = UIKit.Panel(body, "Info", UIKit.Card, 12).rectTransform;
            UIKit.Span(right, 0.64f, 0f, 1f, 1f);
            right.offsetMin = new Vector2(0f, 80f);
            var title = UIKit.Label(right, "", 34, TextAnchor.UpperLeft, UIKit.TextC, true);
            title.rectTransform.SetTop(22f, 46f, 28f);
            var kind = UIKit.Label(right, "", 19, TextAnchor.UpperLeft, UIKit.Accent, true);
            kind.rectTransform.SetTop(72f, 28f, 28f);
            var desc = UIKit.Label(right, "", 20, TextAnchor.UpperLeft, UIKit.TextDim);
            desc.rectTransform.SetTop(110f, 110f, 28f);
            var effects = UIKit.Label(right, "", 20, TextAnchor.UpperLeft, UIKit.TextC);
            effects.rectTransform.SetTop(232f, 320f, 28f);
            effects.lineSpacing = 1.15f;

            var chips = new List<(int slot, AttachmentData a, UIButtonFx fx)>();
            WeaponData.Slot focus = data.slots.Length > 0 ? data.slots[0] : null;

            void Show(WeaponData.Slot s, AttachmentData a)
            {
                if (s == null) return;
                kind.text = WeaponDataDB.SlotName(s.slot).ToUpperInvariant();
                if (a)
                {
                    title.text = a.Name;
                    desc.text = a.description;
                    effects.text = WeaponDataDB.Effects(a);
                }
                else
                {
                    title.text = WeaponDataDB.NoneName(data, s);
                    desc.text = s.slot == AttachmentSlot.Optic ? "Механический прицел оружия: целик и мушка."
                              : s.slot == AttachmentSlot.Magazine ? "Штатный магазин: быстрее перезарядка, легче оружие."
                              : "Слот свободен: оружие легче и быстрее вскидывается.";
                    effects.text = "";
                }
            }

            void ShowFocus()
            {
                if (focus != null) Show(focus, WeaponDataDB.Chosen(data, focus));
            }

            void Refresh()
            {
                var sel = WeaponDataDB.Selection(data);
                foreach (var c in chips) c.fx.Selected = c.slot < sel.Length && sel[c.slot] == c.a;
            }

            const float rowH = 100f, gap = 12f;
            for (int i = 0; i < data.slots.Length; i++)
            {
                var s = data.slots[i];
                if (s == null) continue;
                int si = i;
                var row = UIKit.Rect("Slot_" + s.slot, left);
                row.SetTop(i * (rowH + gap), rowH);
                var head = UIKit.Label(row, WeaponDataDB.SlotName(s.slot).ToUpperInvariant(), 19, TextAnchor.LowerLeft, UIKit.Accent, true);
                head.rectTransform.SetTop(0f, 28f, 4f);
                var holder = UIKit.Rect("Chips", row);
                UIKit.Fill(holder, 0f, 0f, 0f, 34f);
                var options = new List<AttachmentData>();
                if (!s.required) options.Add(null);
                foreach (var a in s.allowed) if (a) options.Add(a);
                for (int k = 0; k < options.Count; k++)
                {
                    var a = options[k];
                    var b = UIKit.Button(holder, a ? a.Name : WeaponDataDB.NoneName(data, s), () =>
                    {
                        PlayerLoadout.SetAttachment(data.weaponName, s.slot.ToString(), a ? a.id : "");
                        focus = s;
                        Refresh();
                        Show(s, a);
                        onChanged?.Invoke();
                    }, UIKit.Style.Normal, 18);
                    var rt = (RectTransform)b.transform;
                    UIKit.Span(rt, k / (float)options.Count, 0f, (k + 1) / (float)options.Count, 1f);
                    rt.offsetMin = new Vector2(k == 0 ? 0f : 4f, 0f);
                    rt.offsetMax = new Vector2(k == options.Count - 1 ? 0f : -4f, 0f);
                    var lbl = UIKit.LabelOf(b);
                    lbl.horizontalOverflow = HorizontalWrapMode.Wrap;
                    lbl.lineSpacing = 0.9f;
                    lbl.resizeTextForBestFit = true; // long names shrink instead of breaking mid-word
                    lbl.resizeTextMinSize = 13;
                    lbl.resizeTextMaxSize = 18;
                    chips.Add((si, a, b.GetComponent<UIButtonFx>()));
                    var hover = b.gameObject.AddComponent<UIHover>();
                    hover.enter = () => Show(s, a);
                    hover.exit = ShowFocus;
                }
            }
            Refresh();
            ShowFocus();

            // ---- footer
            var reset = UIKit.Button(body, "Сбросить", () =>
            {
                PlayerLoadout.ResetAttachments(data.weaponName);
                Refresh();
                ShowFocus();
                onChanged?.Invoke();
            }, UIKit.Style.Ghost, 24);
            var rrt = (RectTransform)reset.transform;
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = Vector2.zero;
            rrt.sizeDelta = new Vector2(220f, 60f);
            rrt.anchoredPosition = Vector2.zero;
            string note = inMatch ? "Модули ставятся при следующем возрождении." : "Выбор сохраняется для этого оружия во всех матчах.";
            var nt = UIKit.Label(body, note, 20, TextAnchor.MiddleLeft, UIKit.TextDim);
            nt.rectTransform.anchorMin = Vector2.zero; nt.rectTransform.anchorMax = new Vector2(0.7f, 0f); nt.rectTransform.pivot = Vector2.zero;
            nt.rectTransform.sizeDelta = new Vector2(-240f, 60f); nt.rectTransform.anchoredPosition = new Vector2(240f, 0f);
            var done = UIKit.Button(body, "Готово", () => panel.Close(), UIKit.Style.Primary, 26);
            var drt = (RectTransform)done.transform;
            drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(1f, 0f);
            drt.sizeDelta = new Vector2(300f, 60f);
            drt.anchoredPosition = Vector2.zero;
            return panel;
        }
    }
}
