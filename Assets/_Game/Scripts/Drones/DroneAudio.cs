using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Procedural FPV drone sound: four slightly detuned motors (blade-pass tone with harmonics and
    /// whine), beating between them, and broadband prop wash noise. One seamless 2 s loop; the
    /// AudioSource pitch follows the throttle.
    /// </summary>
    public static class DroneAudio
    {
        static AudioClip buzz, alarm;

        public static AudioClip Buzz => buzz ? buzz : buzz = MakeBuzz();
        /// <summary>Short warning beep (low battery / weak link) for the OSD.</summary>
        public static AudioClip Alarm => alarm ? alarm : alarm = MakeAlarm();

        static AudioClip MakeBuzz()
        {
            const int sr = 44100;
            const float len = 2f;
            int n = (int)(sr * len);
            var d = new float[n];
            // integer frequencies over a 2 s loop -> every component wraps seamlessly
            float[] f0 = { 148.5f, 152f, 156.5f, 161f };
            var rnd = new System.Random(4242);
            float lp = 0f, lp2 = 0f, bp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float s = 0f;
                for (int m = 0; m < 4; m++)
                {
                    float ph = 2f * Mathf.PI * f0[m] * t;
                    // blade pass (2 blades) + harmonics; the motor's electrical whine sits much higher
                    s += Mathf.Sin(ph) * 0.5f + Mathf.Sin(ph * 2f + 0.3f) * 0.32f + Mathf.Sin(ph * 3f + 1.1f) * 0.18f + Mathf.Sin(ph * 5f) * 0.07f;
                    s += Mathf.Sin(ph * 14f + m) * 0.035f;
                }
                s *= 0.25f;
                // saw-ish edge for the angry FPV buzz
                float saw = 0f;
                for (int m = 0; m < 4; m++) saw += (Mathf.Repeat(f0[m] * 2f * t, 1f) - 0.5f);
                s += saw * 0.08f;
                // wash noise: low-passed white noise, gently modulated by the beating
                float w = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += (w - lp) * 0.08f;
                lp2 += (lp - lp2) * 0.2f;
                bp += (w - bp) * 0.45f;
                float beat = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 3.5f * t);
                s += lp2 * 0.9f * beat + (w - bp) * 0.05f;
                d[i] = s;
            }
            // crossfade the noise tail into the head so the loop is click-free
            int fade = sr / 20;
            for (int i = 0; i < fade; i++)
            {
                float a = i / (float)fade;
                d[i] = Mathf.Lerp(d[n - fade + i], d[i], a);
            }
            Normalize(d, 0.8f);
            var clip = AudioClip.Create("drone_buzz", n - fade, 1, sr, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(d, 0, trimmed, 0, n - fade);
            clip.SetData(trimmed, 0);
            return clip;
        }

        static AudioClip MakeAlarm()
        {
            const int sr = 44100;
            int n = sr / 6;
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr;
                float env = Mathf.Clamp01(t * 200f) * Mathf.Clamp01((n - i) / (sr * 0.02f));
                d[i] = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 2200f * t)) * 0.3f * env;
            }
            var clip = AudioClip.Create("drone_alarm", n, 1, sr, false);
            clip.SetData(d, 0);
            return clip;
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 0f;
            foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v));
            if (m < 1e-5f) return;
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }
}
