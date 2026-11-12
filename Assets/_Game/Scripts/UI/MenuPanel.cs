using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// A modal menu window (settings, loadout, character). Closes on Esc (top-most first) and tells the
    /// main / pause menu that the Esc press of this frame was used.
    /// </summary>
    public class MenuPanel : MonoBehaviour
    {
        static readonly List<MenuPanel> open = new List<MenuPanel>();
        public static bool AnyOpen { get { open.RemoveAll(p => !p); return open.Count > 0; } }
        /// <summary>Frame in which a panel consumed Esc (the pause menu must not react to the same press).</summary>
        public static int EscFrame = -1;
        public static bool EscUsedThisFrame => EscFrame == Time.frameCount;

        public Action onClose;
        public Action onUpdate;
        /// <summary>Return true to swallow Esc (e.g. cancel key rebinding instead of closing).</summary>
        public Func<bool> onEscape;

        void OnEnable() { if (!open.Contains(this)) open.Add(this); }
        void OnDisable() => open.Remove(this);

        public static void CloseAll()
        {
            foreach (var p in open.ToArray()) if (p) p.Close();
        }

        void Update()
        {
            onUpdate?.Invoke();
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && !EscUsedThisFrame && open.Count > 0 && open[open.Count - 1] == this)
            {
                EscFrame = Time.frameCount;
                if (onEscape != null && onEscape()) return;
                Close();
            }
        }

        public void Close()
        {
            var cb = onClose;
            onClose = null;
            open.Remove(this);
            Destroy(gameObject);
            cb?.Invoke();
        }

        public static MenuPanel Create(Transform canvas, string title, Vector2 size, out RectTransform body)
        {
            body = UIKit.Modal(canvas, title, size, out var root);
            root.SetAsLastSibling();
            var p = root.gameObject.AddComponent<MenuPanel>();
            // close button (×) in the corner
            var x = UIKit.Button(root.GetChild(0), "×", () => p.Close(), UIKit.Style.Ghost, 46);
            var xr = (RectTransform)x.transform;
            xr.anchorMin = xr.anchorMax = xr.pivot = new Vector2(1f, 1f);
            xr.sizeDelta = new Vector2(64f, 64f);
            xr.anchoredPosition = new Vector2(-14f, -14f);
            return p;
        }
    }
}
