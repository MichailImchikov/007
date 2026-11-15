using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Team games: the names of the local player's teammates (bots and other players) over their heads, in the ally colour,
    /// seen through walls; enemies get no name at all. A fallen teammate a medic can still bring back reads "+ name +" in
    /// blinking green. Screen-space labels on an overlay canvas under the HUD; free-for-all modes show none (the network players'
    /// world nameplates — NetProxy — are hidden in team games, this draws the teammates' instead).
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class TeamNameplates : MonoBehaviour
    {
        static TeamNameplates inst;
        readonly List<Text> pool = new List<Text>();
        Font font;
        Canvas canvas;

        static readonly Color Down = new Color(0.45f, 1f, 0.5f);

        public static void Ensure()
        {
            if (inst) return;
            inst = new GameObject("TeamNameplates").AddComponent<TeamNameplates>();
        }

        void Awake()
        {
            inst = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 8; // under the HUD (20) and the class HUD (15)
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
        }

        void OnDestroy() { if (inst == this) inst = null; }

        Text Get(int i)
        {
            while (pool.Count <= i)
            {
                var go = new GameObject("Plate", typeof(RectTransform));
                go.transform.SetParent(transform, false);
                var t = go.AddComponent<Text>();
                t.font = font; t.alignment = TextAnchor.LowerCenter; t.raycastTarget = false; t.supportRichText = true;
                t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
                t.fontStyle = FontStyle.Bold;
                var rt = t.rectTransform; rt.sizeDelta = new Vector2(300f, 40f); rt.pivot = new Vector2(0.5f, 0f);
                var o = go.AddComponent<Outline>(); o.effectColor = new Color(0f, 0f, 0f, 0.8f); o.effectDistance = new Vector2(1.5f, -1.5f);
                pool.Add(t);
            }
            return pool[i];
        }

        void LateUpdate()
        {
            int n = 0;
            var ph = PlayerHealth.Local;
            var cam = Camera.main;
            var me = ph ? ph.GetComponent<Combatant>() : null;
            if (Teams.Active && cam && me && me.team >= 0 && !DroneMode.Active && !SkyArena.Active)
            {
                foreach (var c in Combatant.All)
                {
                    if (!c || c == me || !Teams.Friendly(me, c) || string.IsNullOrEmpty(c.displayName)) continue;
                    Vector3 head = c.alive ? c.Head : c.transform.position + Vector3.up * 0.4f;
                    Vector3 sp = cam.WorldToScreenPoint(head + Vector3.up * 0.32f);
                    if (sp.z < 0.3f) continue;
                    float dist = sp.z;
                    float a = 1f - Mathf.InverseLerp(70f, 110f, dist);
                    if (a <= 0.01f) continue;
                    var t = Get(n++);
                    t.gameObject.SetActive(true);
                    t.rectTransform.position = new Vector3(sp.x, sp.y, 0f);
                    t.fontSize = Mathf.RoundToInt(Mathf.Lerp(22f, 14f, Mathf.InverseLerp(4f, 50f, dist)));
                    if (c.alive)
                    {
                        Color col = Teams.Colors[0];
                        col.a = a;
                        t.color = col;
                        t.text = c.displayName;
                    }
                    else
                    {
                        // fallen: a medic can still bring them back while the body lies there
                        Color col = Down;
                        col.a = a * (0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f));
                        t.color = col;
                        t.text = "+ " + c.displayName + " +";
                    }
                }
            }
            for (int i = n; i < pool.Count; i++) if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
        }
    }
}
