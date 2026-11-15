using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Comic-book screen effects for the local player, growing with the kill streak and with momentum (moving fast:
    /// sprinting, sliding, jumping; built quickly, lost slowly):
    ///  - every kill pops a sound word in a starburst ("BAM!", "POW!"...), bigger and hotter with the streak;
    ///  - a big streak makes the whole picture more saturated (_FPS_ComicBoost, applied by FPS/ToonPost), nothing
    ///    covers the edges of the screen;
    ///  - speed lines while moving fast, steady (they drift and breathe, they do not jump around);
    ///  - the streak counter.
    /// Lives under the HUD (HUD creates it).
    /// </summary>
    public class ComicFX : MonoBehaviour
    {
        public static ComicFX Instance { get; private set; }

        /// <summary>0..1: the whole comic effect (streak + momentum + a kill's flash).</summary>
        public float Intensity { get; private set; }
        /// <summary>0..1: built by moving fast.</summary>
        public float Momentum { get; private set; }
        /// <summary>0..1: how much the streak saturates the picture (from two kills up, full at about seven).</summary>
        public float Saturation { get; private set; }

        static readonly int Boost = Shader.PropertyToID("_FPS_ComicBoost");
        static readonly string[] KillWords = { "BAM!", "POW!", "WHAM!", "BOOM!", "KRAK!", "ZAP!", "BLAM!", "SMASH!", "KA-POW!", "KA-BOOM!" };

        class Word
        {
            public RectTransform rt;
            public CanvasGroup group;
            public float t, life, scale, rot, wobble;
        }

        Font font;
        RawImage speed;
        Text streakText;
        CanvasGroup streakGroup;
        RectTransform wordLayer;
        PlayerMotor motor;
        float killFlash, streakPulse;
        int wordIndex;
        readonly List<Word> words = new List<Word>();

        public static ComicFX Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("ComicFX");
            return go.AddComponent<ComicFX>();
        }

        void Awake()
        {
            Instance = this;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 15; // under the HUD (20): crosshair and ammo stay readable
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            speed = Full("SpeedLines", ComicArt.SpeedLines, new Color(1f, 1f, 1f, 0f));
            speed.rectTransform.localScale = Vector3.one * 1.25f;
            wordLayer = (RectTransform)Full("Words", null, Color.clear).transform;

            // streak counter: top center, comic lettering
            var st = new GameObject("Streak", typeof(RectTransform)).GetComponent<RectTransform>();
            st.SetParent(transform, false);
            st.anchorMin = st.anchorMax = new Vector2(0.5f, 1f);
            st.pivot = new Vector2(0.5f, 1f);
            st.anchoredPosition = new Vector2(0f, -96f);
            st.sizeDelta = new Vector2(700f, 90f);
            streakGroup = st.gameObject.AddComponent<CanvasGroup>();
            streakGroup.alpha = 0f;
            streakText = Lettering(st, "", 64, new Color(1f, 0.86f, 0.2f));
        }

        void OnEnable()
        {
            KillStreak.LocalKill += OnKill;
            KillStreak.LocalReset += OnReset;
        }

        void OnDisable()
        {
            KillStreak.LocalKill -= OnKill;
            KillStreak.LocalReset -= OnReset;
            Shader.SetGlobalFloat(Boost, 0f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        RawImage Full(string name, Texture tex, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var ri = go.AddComponent<RawImage>();
            ri.texture = tex;
            ri.color = c;
            ri.raycastTarget = false;
            if (!tex) ri.enabled = c.a > 0f;
            return ri;
        }

        /// <summary>Comic lettering: bold, thick black ink outline and a drop shadow.</summary>
        Text Lettering(RectTransform parent, string s, int size, Color fill)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = s;
            t.fontSize = size;
            t.fontStyle = FontStyle.BoldAndItalic;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = fill;
            t.raycastTarget = false;
            // a thick ink stroke that follows the letters: each Outline copies everything before it four times, so two
            // stacked ones give 25 copies reaching 6 px diagonally and sideways (four would give 625 and a solid box)
            var ink = new Color(0.04f, 0.02f, 0.05f, 1f);
            for (int i = 0; i < 2; i++)
            {
                var o = go.AddComponent<Outline>();
                o.effectColor = ink;
                o.effectDistance = new Vector2(3f, 3f);
            }
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.55f);
            sh.effectDistance = new Vector2(9f, -9f);
            return t;
        }

        void OnKill(int streak)
        {
            killFlash = 1f;
            streakPulse = 1f;
            int n = streak >= 6 ? 3 : streak >= 3 ? 2 : 1;
            for (int i = 0; i < n; i++) SpawnWord(streak, i);
        }

        void OnReset()
        {
            killFlash = 0f;
        }

        void SpawnWord(int streak, int k)
        {
            string s = KillWords[wordIndex++ % KillWords.Length];
            if (streak >= 5 && k == 0) s = streak >= 8 ? "KA-BOOM!" : "KA-POW!";
            float heat = KillStreak.Level(streak);
            var go = new GameObject("Word", typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(wordLayer, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            // up and to the sides, clear of the crosshair: the sides alternate word by word, and a word goes a slot lower
            // for each fresh word already on its side, so a multi-kill does not pile its words on one spot
            float side = wordIndex % 2 == 0 ? 1f : -1f;
            int slot = 0;
            foreach (var w in words)
                if (w.rt && Mathf.Sign(w.rt.anchoredPosition.x) == side && w.t < w.life * 0.7f) slot++;
            slot %= 3;
            rt.anchoredPosition = new Vector2(side * Random.Range(450f, 620f), Random.Range(280f, 340f) - slot * 230f);
            float size = 0.62f + 0.07f * Mathf.Min(streak, 8);
            rt.sizeDelta = new Vector2(460f, 320f) * size;
            var g = go.AddComponent<CanvasGroup>();
            g.blocksRaycasts = false;

            // the burst: yellow → orange → red → purple as the streak grows
            var star = new GameObject("Burst", typeof(RectTransform)).GetComponent<RectTransform>();
            star.SetParent(rt, false);
            star.anchorMin = Vector2.zero; star.anchorMax = Vector2.one;
            star.offsetMin = star.offsetMax = Vector2.zero;
            star.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            var ri = star.gameObject.AddComponent<RawImage>();
            ri.texture = ComicArt.Burst;
            ri.raycastTarget = false;
            Color c0 = new Color(1f, 0.88f, 0.18f), c1 = new Color(1f, 0.52f, 0.08f), c2 = new Color(0.95f, 0.16f, 0.12f), c3 = new Color(0.62f, 0.2f, 0.95f);
            ri.color = heat < 0.45f ? Color.Lerp(c0, c1, heat / 0.45f) : heat < 0.8f ? Color.Lerp(c1, c2, (heat - 0.45f) / 0.35f) : Color.Lerp(c2, c3, (heat - 0.8f) / 0.2f);

            Color fill = heat < 0.45f ? new Color(0.95f, 0.15f, 0.1f) : new Color(1f, 0.97f, 0.85f);
            Lettering(rt, s, Mathf.RoundToInt(86 * size), fill);

            words.Add(new Word { rt = rt, group = g, t = 0f, life = 0.95f + 0.1f * Mathf.Min(streak, 5), scale = size, rot = Random.Range(-14f, 14f), wobble = Random.Range(0f, 10f) });
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (!motor) motor = FindAnyObjectByType<PlayerMotor>();

            // momentum: fast moves build it up quickly, it drains slowly when you stop
            float speedN = 0f;
            if (motor)
            {
                speedN = Mathf.InverseLerp(3.2f, 9.5f, motor.HorizontalSpeed);
                if (motor.IsSliding) speedN = Mathf.Max(speedN, 0.9f);
                else if (!motor.IsGrounded) speedN = Mathf.Max(speedN, 0.5f);
            }
            Momentum = Mathf.MoveTowards(Momentum, speedN, dt * (speedN > Momentum ? 1.2f : 0.3f));
            killFlash = Mathf.MoveTowards(killFlash, 0f, dt * 1.5f);
            streakPulse = Mathf.MoveTowards(streakPulse, 0f, dt * 2.5f);
            float streak = KillStreak.Level(KillStreak.Local);
            float target = Mathf.Clamp01(streak * 0.8f + Momentum * 0.45f + killFlash * 0.35f);
            Intensity = Mathf.Lerp(Intensity, target, 1f - Mathf.Exp(-4f * dt));
            // a big streak saturates the picture (FPS/ToonPost): nothing from one kill, then up to full at about seven
            float sat = Mathf.SmoothStep(0f, 1f, (streak - 0.25f) / 0.65f);
            Saturation = Mathf.Lerp(Saturation, sat, 1f - Mathf.Exp(-3f * dt));
            Shader.SetGlobalFloat(Boost, Saturation);

            // speed lines: steady, a slight sway and a gentle breath (a new random angle every few frames made them
            // jump and drop out)
            float time = Time.unscaledTime;
            float lines = Momentum * (0.2f + 0.2f * streak);
            speed.color = new Color(1f, 1f, 1f, lines);
            speed.enabled = lines > 0.005f;
            if (speed.enabled)
            {
                speed.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 3f * Mathf.Sin(time * 0.9f));
                speed.rectTransform.localScale = Vector3.one * (1.25f + 0.03f * Mathf.Sin(time * 8f));
            }

            int n = KillStreak.Local;
            streakGroup.alpha = Mathf.MoveTowards(streakGroup.alpha, n >= 2 ? 1f : 0f, dt * 4f);
            if (n >= 2) streakText.text = "СЕРИЯ ×" + n;
            streakText.rectTransform.localScale = Vector3.one * (1f + streakPulse * 0.45f);
            streakText.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 3f) * 2.5f * Intensity);

            for (int i = words.Count - 1; i >= 0; i--)
            {
                var w = words[i];
                w.t += dt;
                if (w.t >= w.life || !w.rt) { if (w.rt) Destroy(w.rt.gameObject); words.RemoveAt(i); continue; }
                // pop in, settle, hold with a little shake, then blow up and fade
                float s;
                if (w.t < 0.1f) s = Mathf.Lerp(0.25f, 1.2f, w.t / 0.1f);
                else if (w.t < 0.18f) s = Mathf.Lerp(1.2f, 1f, (w.t - 0.1f) / 0.08f);
                else s = 1f + Mathf.Max(0f, w.t - (w.life - 0.22f)) * 1.4f;
                w.rt.localScale = Vector3.one * s;
                w.rt.localRotation = Quaternion.Euler(0f, 0f, w.rot + Mathf.Sin((w.t + w.wobble) * 30f) * 2f * Mathf.Clamp01(1f - w.t * 3f));
                w.group.alpha = 1f - Mathf.Clamp01((w.t - (w.life - 0.22f)) / 0.22f);
            }
        }
    }
}
