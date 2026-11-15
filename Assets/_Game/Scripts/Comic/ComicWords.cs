using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Comic sound words floating in the world (PAW! over a streaking gunner's rifle, SVOOSH! under a slide...):
    /// pooled world-space canvases that face the camera, pop in, float up and fade. Kept readable at range.
    /// </summary>
    public class ComicWords : MonoBehaviour
    {
        static ComicWords instance;

        class Item
        {
            public RectTransform rt;
            public Canvas canvas;
            public Text text;
            public CanvasGroup group;
            public Vector3 pos, drift;
            public float t, life, size, rot;
            public bool live;
        }

        const int PoolSize = 40;
        readonly List<Item> pool = new List<Item>();
        Font font;

        static ComicWords Get()
        {
            if (instance) return instance;
            var go = new GameObject("ComicWords");
            instance = go.AddComponent<ComicWords>();
            return instance;
        }

        /// <summary>Pops a word at a world point (size 1 ≈ 0.45 m tall up close).</summary>
        public static void Spawn(Vector3 pos, string word, Color fill, float size = 1f, Vector3 drift = default)
        {
            Get().Pop(pos, word, fill, size, drift);
        }

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        Item Make()
        {
            var go = new GameObject("Word", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400f, 160f);
            var g = go.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false;
            var tgo = new GameObject("Text", typeof(RectTransform));
            var trt = tgo.GetComponent<RectTransform>();
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.AddComponent<Text>();
            t.font = font;
            t.fontSize = 84;
            t.fontStyle = FontStyle.BoldAndItalic;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            // a thick ink stroke that follows the letters: each Outline copies everything before it four times, so two
            // stacked ones give 25 copies reaching 7 px diagonally and sideways; then a drop shadow under it all
            var ink = new Color(0.04f, 0.02f, 0.05f, 1f);
            for (int i = 0; i < 2; i++)
            {
                var o = tgo.AddComponent<Outline>();
                o.effectColor = ink;
                o.effectDistance = new Vector2(3.5f, 3.5f);
            }
            var sh = tgo.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(5f, -6f);
            go.SetActive(false);
            var item = new Item { rt = rt, canvas = canvas, text = t, group = g };
            pool.Add(item);
            return item;
        }

        void Pop(Vector3 pos, string word, Color fill, float size, Vector3 drift)
        {
            Item it = null;
            foreach (var p in pool) if (!p.live) { it = p; break; }
            if (it == null)
            {
                if (pool.Count < PoolSize) it = Make();
                else { it = pool[0]; foreach (var p in pool) if (p.t > it.t) it = p; } // reuse the oldest
            }
            it.live = true;
            it.t = 0f;
            it.life = 0.75f;
            it.pos = pos;
            it.drift = drift == default ? Vector3.up * 0.6f : drift;
            it.size = size;
            it.rot = Random.Range(-16f, 16f);
            it.text.text = word;
            it.text.color = fill;
            it.rt.gameObject.SetActive(true);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (!cam) return;
            float dt = Time.deltaTime;
            Vector3 cp = cam.transform.position;
            foreach (var it in pool)
            {
                if (!it.live) continue;
                it.t += dt;
                if (it.t >= it.life) { it.live = false; it.rt.gameObject.SetActive(false); continue; }
                it.pos += it.drift * dt;
                float s = it.t < 0.08f ? Mathf.Lerp(0.3f, 1.25f, it.t / 0.08f) : it.t < 0.15f ? Mathf.Lerp(1.25f, 1f, (it.t - 0.08f) / 0.07f) : 1f;
                float dist = Vector3.Distance(cp, it.pos);
                // world size grows a little with distance so the word stays readable across a room
                float world = 0.0028f * it.size * s * Mathf.Lerp(1f, 2.2f, Mathf.InverseLerp(4f, 40f, dist));
                it.rt.position = it.pos;
                it.rt.rotation = Quaternion.LookRotation(it.pos - cp, cam.transform.up) * Quaternion.Euler(0f, 0f, it.rot);
                it.rt.localScale = Vector3.one * world;
                it.group.alpha = 1f - Mathf.Clamp01((it.t - (it.life - 0.2f)) / 0.2f);
            }
        }
    }
}
