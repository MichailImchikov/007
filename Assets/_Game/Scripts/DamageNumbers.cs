using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Floating damage numbers: pop in with overshoot, arc upwards in world space,
    /// then fade and shrink. White = body, gold = headshot, red = kill.
    /// </summary>
    public class DamageNumbers : MonoBehaviour
    {
        static DamageNumbers instance;

        class Popup
        {
            public RectTransform rt;
            public Text text;
            public Outline outline;
            public Vector3 world, vel;
            public float age, life, scale;
            public Color color;
            public bool active;
        }

        readonly List<Popup> pool = new List<Popup>();
        Canvas canvas;
        Font font;
        Camera cam;

        public static void Ensure()
        {
            if (instance) return;
            instance = new GameObject("DamageNumbers").AddComponent<DamageNumbers>();
        }

        public static void Spawn(Vector3 pos, float amount, bool crit, bool kill)
        {
            Ensure();
            instance.SpawnInternal(pos, amount, crit, kill);
        }

        void Awake()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        Popup Create()
        {
            var go = new GameObject("DamageNumber", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(300f, 90f);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>();
            sh.effectDistance = new Vector2(2f, -3f);
            sh.effectColor = new Color(0f, 0f, 0f, 0.45f);
            var o = go.AddComponent<Outline>();
            o.effectDistance = new Vector2(1.6f, -1.6f);
            o.effectColor = new Color(0f, 0f, 0f, 0.9f);
            var p = new Popup { rt = rt, text = t, outline = o };
            pool.Add(p);
            return p;
        }

        void SpawnInternal(Vector3 pos, float amount, bool crit, bool kill)
        {
            if (!cam) cam = Camera.main;
            Popup p = null;
            foreach (var x in pool) if (!x.active) { p = x; break; }
            if (p == null) p = Create();

            p.text.text = Mathf.RoundToInt(amount).ToString();
            p.text.fontSize = kill ? 46 : crit ? 42 : 32;
            p.color = kill ? new Color(1f, 0.28f, 0.22f) : crit ? new Color(1f, 0.8f, 0.2f) : Color.white;
            p.scale = kill ? 1.2f : crit ? 1.1f : 1f;

            Vector3 right = cam ? cam.transform.right : Vector3.right;
            p.world = pos + Random.insideUnitSphere * 0.06f;
            p.vel = right * Random.Range(-0.9f, 0.9f) + Vector3.up * Random.Range(1.7f, 2.3f);
            p.age = 0f;
            p.life = kill ? 1.1f : crit ? 0.95f : 0.8f;
            p.active = true;
            p.rt.gameObject.SetActive(true);
            p.rt.SetAsLastSibling();
        }

        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            if (!cam) return;
            float dt = Time.deltaTime;
            float sf = Mathf.Max(0.01f, canvas.scaleFactor);

            foreach (var p in pool)
            {
                if (!p.active) continue;
                p.age += dt;
                if (p.age >= p.life)
                {
                    p.active = false;
                    p.rt.gameObject.SetActive(false);
                    continue;
                }
                p.vel += Vector3.down * 3.4f * dt;
                p.vel *= 1f - 1.2f * dt;
                p.world += p.vel * dt;

                Vector3 sp = cam.WorldToScreenPoint(p.world);
                if (sp.z < 0.05f) { p.text.color = Color.clear; continue; }
                p.rt.anchoredPosition = new Vector2(sp.x, sp.y) / sf;

                float u = p.age / p.life;
                float s = p.age < 0.07f
                    ? Mathf.Lerp(0.35f, 1.65f, p.age / 0.07f)
                    : 1f + 0.65f * Mathf.Exp(-(p.age - 0.07f) * 15f);
                float tail = Mathf.Clamp01((u - 0.62f) / 0.38f);
                s *= Mathf.Lerp(1f, 0.65f, tail);
                p.rt.localScale = Vector3.one * (s * p.scale);

                Color c = Color.Lerp(Color.white, p.color, Mathf.Clamp01(p.age / 0.12f));
                c.a = 1f - tail * tail;
                p.text.color = c;
                var oc = p.outline.effectColor; oc.a = 0.9f * c.a; p.outline.effectColor = oc;
            }
        }
    }
}
