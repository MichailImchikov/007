using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    public enum Sfx
    {
        Shot, Dry, MagOut, MagIn, BoltPull, BoltRelease, Switch,
        Hit, HitCrit, Kill, ImpactConcrete, ImpactMetal, Shell, MagDrop,
        Step, Land, Vault, Slide, TargetDown, TargetUp,
        ShotShotgun, ShotSniper, ShotPistol, PumpBack, PumpForward, ShellIn,
        Explosion, GrenadePin, GrenadeBounce, Throw, KnifeSwing, KnifeHit, KnifeClang, Stab, Equip, Prone, Hurt, Whiz,
        WoodBreak, SandBreak, Fart, Cluck, Parry, RocketLaunch, HookFire, HookHit, Reel, Warp, Slip, AbilityPickup, AbilityReady, Trap,
        Scream, Boing, Hammer, ErrorTone, Click, ShrinkDown, GrowUp
    }

    /// <summary>
    /// Synthesizes every sound procedurally at startup (no audio assets needed)
    /// and plays them from small pools of 2D / 3D AudioSources.
    /// Replace with real clips later by swapping Pick() results.
    /// </summary>
    public static class ProcAudio
    {
        const int SR = 44100;
        static Dictionary<Sfx, AudioClip[]> clips;
        static AudioSource[] src2D, src3D;
        static int i2, i3;
        static GameObject host;

        public static float MasterVolume = 0.9f;

        public static void Init()
        {
            if (clips == null || !ClipsAlive()) Generate();
            if (host) return;
            host = new GameObject("ProcAudio");
            src2D = new AudioSource[16];
            for (int i = 0; i < src2D.Length; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0f;
                src2D[i] = s;
            }
            src3D = new AudioSource[24];
            for (int i = 0; i < src3D.Length; i++)
            {
                var go = new GameObject("Src3D");
                go.transform.SetParent(host.transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 1f; s.minDistance = 3f; s.maxDistance = 90f;
                s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = 0f;
                src3D[i] = s;
            }
        }

        static bool ClipsAlive()
        {
            foreach (var kv in clips) if (kv.Value == null || kv.Value.Length == 0 || kv.Value[0] == null) return false;
            return true;
        }

        public static void Play(Sfx s, float volume = 1f, float pitchVar = 0.04f)
        {
            Init();
            i2 = (i2 + 1) % src2D.Length;
            var a = src2D[i2];
            a.clip = Pick(s);
            a.volume = volume * MasterVolume;
            a.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            a.Play();
        }

        public static void PlayAt(Sfx s, Vector3 pos, float volume = 1f, float pitchVar = 0.08f)
        {
            Init();
            i3 = (i3 + 1) % src3D.Length;
            var a = src3D[i3];
            a.transform.position = pos;
            a.clip = Pick(s);
            a.volume = volume * MasterVolume;
            a.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            a.Play();
        }

        static AudioClip Pick(Sfx s)
        {
            var arr = clips[s];
            return arr[Random.Range(0, arr.Length)];
        }

        // ------------------------------------------------------------------ generation

        static void Generate()
        {
            clips = new Dictionary<Sfx, AudioClip[]>
            {
                [Sfx.Shot] = new[] { Clip("shot0", Shot(11), 1f, 1.7f), Clip("shot1", Shot(23), 1f, 1.7f), Clip("shot2", Shot(37), 1f, 1.7f) },
                [Sfx.Dry] = new[] { Clip("dry", Dry(3)) },
                [Sfx.MagOut] = new[] { Clip("magout", MagOut(5)) },
                [Sfx.MagIn] = new[] { Clip("magin", MagIn(7)) },
                [Sfx.BoltPull] = new[] { Clip("boltpull", BoltPull(9)) },
                [Sfx.BoltRelease] = new[] { Clip("boltrel", BoltRelease(13)) },
                [Sfx.Switch] = new[] { Clip("switch", Switch(17)) },
                [Sfx.Hit] = new[] { Clip("hit", HitTick(2100f, 19)) },
                [Sfx.HitCrit] = new[] { Clip("hitcrit", HitCrit(21)) },
                [Sfx.Kill] = new[] { Clip("kill", Kill(29)) },
                [Sfx.ImpactConcrete] = new[] { Clip("imp_c0", ImpactConcrete(31)), Clip("imp_c1", ImpactConcrete(41)), Clip("imp_c2", ImpactConcrete(43)) },
                [Sfx.ImpactMetal] = new[] { Clip("imp_m0", ImpactMetal(47, 1f)), Clip("imp_m1", ImpactMetal(53, 1.06f)), Clip("imp_m2", ImpactMetal(59, 0.94f)) },
                [Sfx.Shell] = new[] { Clip("shell0", Shell(61)), Clip("shell1", Shell(67)), Clip("shell2", Shell(71)) },
                [Sfx.MagDrop] = new[] { Clip("magdrop", MagDrop(73)) },
                [Sfx.Step] = new[] { Clip("step0", Step(79)), Clip("step1", Step(83)), Clip("step2", Step(89)), Clip("step3", Step(97)) },
                [Sfx.Land] = new[] { Clip("land", Land(101)) },
                [Sfx.Vault] = new[] { Clip("vault", Vault(103)) },
                [Sfx.Slide] = new[] { Clip("slide", Slide(107)) },
                [Sfx.TargetDown] = new[] { Clip("tdown", TargetDown(109)) },
                [Sfx.TargetUp] = new[] { Clip("tup", TargetUp(113)) },
                [Sfx.ShotShotgun] = new[] { Clip("sg0", ShotShotgun(127), 1f, 1.8f), Clip("sg1", ShotShotgun(131), 1f, 1.8f) },
                [Sfx.ShotSniper] = new[] { Clip("sn0", ShotSniper(137), 1f, 1.9f), Clip("sn1", ShotSniper(139), 1f, 1.9f) },
                [Sfx.ShotPistol] = new[] { Clip("ps0", ShotPistol(149), 1f, 1.6f), Clip("ps1", ShotPistol(151), 1f, 1.6f), Clip("ps2", ShotPistol(157), 1f, 1.6f) },
                [Sfx.PumpBack] = new[] { Clip("pumpb", PumpBack(163)) },
                [Sfx.PumpForward] = new[] { Clip("pumpf", PumpForward(167)) },
                [Sfx.ShellIn] = new[] { Clip("shellin0", ShellIn(173)), Clip("shellin1", ShellIn(179)) },
                [Sfx.Explosion] = new[] { Clip("expl0", Explosion(181), 1f, 1.5f), Clip("expl1", Explosion(191), 1f, 1.5f) },
                [Sfx.GrenadePin] = new[] { Clip("pin", GrenadePin(193)) },
                [Sfx.GrenadeBounce] = new[] { Clip("gb0", GrenadeBounce(197)), Clip("gb1", GrenadeBounce(199)) },
                [Sfx.Throw] = new[] { Clip("throw", Whoosh(211, 0.32f, 0.15f)) },
                [Sfx.KnifeSwing] = new[] { Clip("kswing0", Whoosh(223, 0.22f, 0.4f)), Clip("kswing1", Whoosh(227, 0.2f, 0.45f)) },
                [Sfx.KnifeHit] = new[] { Clip("khit0", KnifeHit(229)), Clip("khit1", KnifeHit(233)) },
                [Sfx.KnifeClang] = new[] { Clip("kclang", KnifeClang(239)) },
                [Sfx.Stab] = new[] { Clip("stab", Stab(241)) },
                [Sfx.Equip] = new[] { Clip("equip", Equip(251)) },
                [Sfx.Prone] = new[] { Clip("prone", ProneDrop(257)) },
                [Sfx.Hurt] = new[] { Clip("hurt0", Hurt(263)), Clip("hurt1", Hurt(269)) },
                [Sfx.Whiz] = new[] { Clip("whiz0", Whiz(271)), Clip("whiz1", Whiz(277)) },
                [Sfx.WoodBreak] = new[] { Clip("wbreak0", WoodBreak(281)), Clip("wbreak1", WoodBreak(283)) },
                [Sfx.SandBreak] = new[] { Clip("sbreak", SandBreak(293)) },
                [Sfx.Fart] = new[] { Clip("fart0", Fart(307, 1f), 0.95f, 1.4f), Clip("fart1", Fart(311, 0.8f), 0.95f, 1.4f), Clip("fart2", Fart(313, 1.25f), 0.95f, 1.4f) },
                [Sfx.Cluck] = new[] { Clip("cluck0", Cluck(317, 1f)), Clip("cluck1", Cluck(331, 1.15f)), Clip("cluck2", Cluck(337, 0.9f)) },
                [Sfx.Parry] = new[] { Clip("parry0", Parry(347, 1f)), Clip("parry1", Parry(349, 1.12f)), Clip("parry2", Parry(353, 0.9f)) },
                [Sfx.RocketLaunch] = new[] { Clip("rocket", RocketLaunch(359), 1f, 1.3f) },
                [Sfx.HookFire] = new[] { Clip("hookfire", HookFire(367)) },
                [Sfx.HookHit] = new[] { Clip("hookhit", HookHit(373)) },
                [Sfx.Reel] = new[] { Clip("reel", Reel(379)) },
                [Sfx.Warp] = new[] { Clip("warp", Warp(383)) },
                [Sfx.Slip] = new[] { Clip("slip", Slip(389)) },
                [Sfx.AbilityPickup] = new[] { Clip("apick", Chime(660f, 990f)) },
                [Sfx.AbilityReady] = new[] { Clip("aready", Chime(880f, 1320f)) },
                [Sfx.Trap] = new[] { Clip("trap", TrapHorn()) },
                [Sfx.Scream] = new[] { Clip("scream", Scream(419), 1f, 2.5f) },
                [Sfx.Boing] = new[] { Clip("boing", Boing()) },
                [Sfx.Hammer] = new[] { Clip("hammer0", Hammer(421)), Clip("hammer1", Hammer(431)) },
                [Sfx.ErrorTone] = new[] { Clip("errtone", ErrorTone()) },
                [Sfx.Click] = new[] { Clip("click", UiClick(433)) },
                [Sfx.ShrinkDown] = new[] { Clip("shrink", PitchSweep(900f, 180f)) },
                [Sfx.GrowUp] = new[] { Clip("grow", PitchSweep(180f, 900f)) },
            };
        }

        static float[] Buf(float sec) => new float[Mathf.CeilToInt(sec * SR)];

        static AudioClip Clip(string name, float[] d, float peak = 0.9f, float drive = 0f)
        {
            Normalize(d, 1f);
            if (drive > 0f)
            {
                float k = (float)System.Math.Tanh(drive);
                for (int i = 0; i < d.Length; i++) d[i] = (float)System.Math.Tanh(d[i] * drive) / k;
            }
            Normalize(d, peak);
            int fadeIn = Mathf.Min(d.Length, 12);
            for (int i = 0; i < fadeIn; i++) d[i] *= i / (float)fadeIn;
            int fade = Mathf.Min(d.Length, SR / 200);
            for (int i = 0; i < fade; i++) d[d.Length - 1 - i] *= i / (float)fade;
            var c = AudioClip.Create(name, d.Length, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 0f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            if (m <= 1e-6f) return;
            float g = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= g;
        }

        /// <summary>Filtered noise burst. lp = one-pole lowpass coefficient (1 = none), hp = highpass coefficient.</summary>
        static void Noise(float[] d, System.Random r, float start, float decay, float amp, float lp = 1f, float hp = 0f, float attack = 0f)
        {
            int s0 = Mathf.Clamp((int)(start * SR), 0, d.Length);
            float y = 0f, low = 0f;
            float comp = 1f / Mathf.Sqrt(lp / (2f - lp));
            for (int i = s0; i < d.Length; i++)
            {
                float t = (i - s0) / (float)SR;
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                y += lp * (n - y);
                float o = y * comp;
                if (hp > 0f) { low += hp * (o - low); o -= low; }
                float env = Mathf.Exp(-t * decay);
                if (attack > 0f) env *= Mathf.Clamp01(t / attack);
                d[i] += o * env * amp;
            }
        }

        /// <summary>Filtered noise with a sin² envelope over [start, start+len] (whooshes, scrapes).</summary>
        static void NoiseSwell(float[] d, System.Random r, float start, float len, float amp, float lp, float hp = 0f)
        {
            int s0 = Mathf.Clamp((int)(start * SR), 0, d.Length);
            int s1 = Mathf.Clamp((int)((start + len) * SR), 0, d.Length);
            float y = 0f, low = 0f;
            float comp = 1f / Mathf.Sqrt(lp / (2f - lp));
            for (int i = s0; i < s1; i++)
            {
                float u = (i - s0) / (float)(s1 - s0);
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                y += lp * (n - y);
                float o = y * comp;
                if (hp > 0f) { low += hp * (o - low); o -= low; }
                float e = Mathf.Sin(u * Mathf.PI);
                d[i] += o * e * e * amp;
            }
        }

        static void Ring(float[] d, float start, float freq, float decay, float amp)
        {
            int s0 = Mathf.Clamp((int)(start * SR), 0, d.Length);
            for (int i = s0; i < d.Length; i++)
            {
                float t = (i - s0) / (float)SR;
                d[i] += Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-t * decay) * amp;
            }
        }

        static void Sweep(float[] d, float start, float f0, float f1, float rate, float decay, float amp)
        {
            int s0 = Mathf.Clamp((int)(start * SR), 0, d.Length);
            float phase = 0f;
            for (int i = s0; i < d.Length; i++)
            {
                float t = (i - s0) / (float)SR;
                float f = f1 + (f0 - f1) * Mathf.Exp(-t * rate);
                phase += 2f * Mathf.PI * f / SR;
                d[i] += Mathf.Sin(phase) * Mathf.Exp(-t * decay) * amp;
            }
        }

        static void Echo(float[] d, float delay, float gain)
        {
            int n = (int)(delay * SR);
            for (int i = d.Length - 1; i >= n; i--) d[i] += d[i - n] * gain;
        }

        // ------------------------------------------------------------------ recipes

        static float[] Shot(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(1.15f);
            Noise(d, r, 0f, 85f, 1.0f, 1f, 0.25f);          // supersonic crack
            Noise(d, r, 0f, 20f, 0.85f, 0.2f);              // muzzle blast body
            Sweep(d, 0f, 160f, 46f, 26f, 9f, 1.15f);        // chest thump
            Noise(d, r, 0.003f, 3.2f, 0.2f, 0.035f);        // low rumble tail
            Noise(d, r, 0.028f, 300f, 0.18f, 0.7f, 0.3f);   // action cycling click
            Echo(d, 0.057f, 0.22f);
            Echo(d, 0.131f, 0.13f);
            Echo(d, 0.236f, 0.07f);
            return d;
        }

        static float[] Dry(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.09f);
            Noise(d, r, 0f, 420f, 1f, 0.9f, 0.4f);
            Ring(d, 0f, 3300f, 110f, 0.35f);
            Ring(d, 0.012f, 2200f, 160f, 0.2f);
            return d;
        }

        static float[] MagOut(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.3f);
            Noise(d, r, 0f, 320f, 1f, 0.85f, 0.35f);
            Ring(d, 0f, 2700f, 90f, 0.25f);
            NoiseSwell(d, r, 0.02f, 0.17f, 0.35f, 0.3f, 0.15f);
            Noise(d, r, 0.19f, 260f, 0.5f, 0.8f, 0.3f);
            return d;
        }

        static float[] MagIn(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.35f);
            NoiseSwell(d, r, 0f, 0.06f, 0.25f, 0.3f, 0.15f);
            Sweep(d, 0.055f, 260f, 150f, 30f, 34f, 0.9f);
            Noise(d, r, 0.055f, 160f, 1f, 0.6f, 0.2f);
            Ring(d, 0.058f, 2450f, 55f, 0.3f);
            Ring(d, 0.06f, 3900f, 80f, 0.15f);
            Noise(d, r, 0.1f, 260f, 0.35f, 0.8f, 0.3f);
            return d;
        }

        static float[] BoltPull(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.22f);
            Noise(d, r, 0f, 260f, 0.6f, 0.8f, 0.3f);
            NoiseSwell(d, r, 0.01f, 0.13f, 0.45f, 0.45f, 0.2f);
            Noise(d, r, 0.14f, 280f, 0.8f, 0.85f, 0.35f);
            Ring(d, 0.14f, 3100f, 70f, 0.2f);
            return d;
        }

        static float[] BoltRelease(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.4f);
            Noise(d, r, 0f, 170f, 1f, 0.75f, 0.25f);
            Sweep(d, 0f, 220f, 120f, 40f, 38f, 0.8f);
            Ring(d, 0f, 1750f, 24f, 0.32f);
            Ring(d, 0f, 2930f, 30f, 0.22f);
            Ring(d, 0f, 4400f, 45f, 0.12f);
            return d;
        }

        static float[] Switch(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.06f);
            Noise(d, r, 0f, 500f, 1f, 0.9f, 0.5f);
            Ring(d, 0f, 4100f, 140f, 0.3f);
            return d;
        }

        static float[] HitTick(float f, int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.1f);
            Ring(d, 0f, f, 48f, 1f);
            Ring(d, 0f, f * 2f, 70f, 0.3f);
            Noise(d, r, 0f, 600f, 0.25f, 1f, 0.5f);
            return d;
        }

        static float[] HitCrit(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.22f);
            Ring(d, 0f, 2900f, 26f, 1f);
            Ring(d, 0f, 1450f, 30f, 0.45f);
            Ring(d, 0f, 5800f, 50f, 0.25f);
            Noise(d, r, 0f, 600f, 0.3f, 1f, 0.5f);
            return d;
        }

        static float[] Kill(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.45f);
            Ring(d, 0f, 1600f, 22f, 0.8f);
            Ring(d, 0.075f, 2400f, 14f, 0.9f);
            Ring(d, 0.075f, 4800f, 30f, 0.2f);
            Sweep(d, 0f, 140f, 60f, 20f, 18f, 0.6f);
            Noise(d, r, 0f, 500f, 0.25f, 1f, 0.5f);
            return d;
        }

        static float[] ImpactConcrete(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.25f);
            Noise(d, r, 0f, 230f, 1f, 0.95f, 0.3f);
            Noise(d, r, 0f, 38f, 0.7f, 0.22f);
            Noise(d, r, 0.02f, 25f, 0.15f, 0.5f, 0.2f); // debris patter
            return d;
        }

        static float[] ImpactMetal(int seed, float tune)
        {
            var r = new System.Random(seed);
            var d = Buf(0.7f);
            Noise(d, r, 0f, 260f, 0.9f, 0.9f, 0.3f);
            Ring(d, 0f, 610f * tune, 7f, 0.22f);
            Ring(d, 0f, 1180f * tune, 9f, 0.35f);
            Ring(d, 0f, 1720f * tune, 11f, 0.3f);
            Ring(d, 0f, 2630f * tune, 15f, 0.22f);
            Ring(d, 0f, 3910f * tune, 22f, 0.16f);
            return d;
        }

        static float[] Shell(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.35f);
            float[] times = { 0f, 0.075f + (float)r.NextDouble() * 0.03f, 0.14f + (float)r.NextDouble() * 0.04f };
            float amp = 1f;
            foreach (float t in times)
            {
                Ring(d, t, 4200f + (float)r.NextDouble() * 300f, 32f, 0.5f * amp);
                Ring(d, t, 5900f + (float)r.NextDouble() * 400f, 40f, 0.35f * amp);
                Ring(d, t, 7300f, 55f, 0.2f * amp);
                Noise(d, r, t, 500f, 0.3f * amp, 1f, 0.5f);
                amp *= 0.5f;
            }
            return d;
        }

        static float[] MagDrop(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.4f);
            Sweep(d, 0f, 240f, 150f, 30f, 30f, 0.9f);
            Noise(d, r, 0f, 120f, 0.8f, 0.4f, 0.1f);
            Noise(d, r, 0.09f, 140f, 0.35f, 0.5f, 0.1f);
            Ring(d, 0f, 1900f, 40f, 0.12f);
            return d;
        }

        static float[] Step(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.16f);
            Noise(d, r, 0f, 40f, 1f, 0.07f);
            Noise(d, r, 0.005f, 90f, 0.35f, 0.5f, 0.2f);
            Noise(d, r, 0.03f, 70f, 0.12f, 0.6f, 0.3f);
            return d;
        }

        static float[] Land(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.3f);
            Noise(d, r, 0f, 20f, 1f, 0.05f);
            Sweep(d, 0f, 110f, 55f, 20f, 18f, 0.7f);
            Noise(d, r, 0f, 60f, 0.3f, 0.45f, 0.2f);
            return d;
        }

        static float[] Vault(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.55f);
            NoiseSwell(d, r, 0f, 0.48f, 0.55f, 0.12f, 0.03f);   // body whoosh
            Noise(d, r, 0.09f, 75f, 0.9f, 0.12f);                // hand plant
            Noise(d, r, 0.095f, 160f, 0.35f, 0.6f, 0.2f);
            NoiseSwell(d, r, 0.1f, 0.25f, 0.2f, 0.5f, 0.2f);     // cloth
            return d;
        }

        static float[] Slide(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.8f);
            NoiseSwell(d, r, 0f, 0.75f, 0.8f, 0.18f, 0.04f);
            Noise(d, r, 0f, 30f, 0.5f, 0.08f);
            return d;
        }

        static float[] TargetDown(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.9f);
            NoiseSwell(d, r, 0f, 0.25f, 0.15f, 0.3f, 0.1f);
            Noise(d, r, 0.28f, 30f, 1f, 0.08f);
            Noise(d, r, 0.28f, 200f, 0.6f, 0.8f, 0.3f);
            Ring(d, 0.28f, 420f, 9f, 0.25f);
            Ring(d, 0.28f, 830f, 12f, 0.18f);
            Ring(d, 0.28f, 1370f, 16f, 0.12f);
            return d;
        }

        static float[] TargetUp(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.45f);
            Sweep(d, 0f, 380f, 900f, 6f, 6f, 0.25f);
            NoiseSwell(d, r, 0f, 0.3f, 0.25f, 0.4f, 0.15f);
            Noise(d, r, 0.3f, 250f, 0.7f, 0.85f, 0.3f);
            Ring(d, 0.3f, 1500f, 30f, 0.2f);
            return d;
        }

        static float[] ShotShotgun(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(1.4f);
            Noise(d, r, 0f, 60f, 1.0f, 1f, 0.2f);
            Noise(d, r, 0f, 11f, 1.1f, 0.14f);
            Sweep(d, 0f, 120f, 36f, 18f, 6.5f, 1.4f);
            Noise(d, r, 0.004f, 2.2f, 0.3f, 0.03f);
            Echo(d, 0.07f, 0.25f); Echo(d, 0.16f, 0.15f); Echo(d, 0.3f, 0.08f);
            return d;
        }

        static float[] ShotSniper(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(1.9f);
            Noise(d, r, 0f, 110f, 1.2f, 1f, 0.3f);
            Noise(d, r, 0f, 15f, 0.9f, 0.25f);
            Sweep(d, 0f, 145f, 40f, 22f, 6f, 1.3f);
            Noise(d, r, 0.004f, 1.6f, 0.32f, 0.03f);
            Echo(d, 0.09f, 0.3f); Echo(d, 0.21f, 0.2f); Echo(d, 0.38f, 0.12f); Echo(d, 0.62f, 0.06f);
            return d;
        }

        static float[] ShotPistol(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.75f);
            Noise(d, r, 0f, 120f, 1.0f, 1f, 0.3f);
            Noise(d, r, 0f, 30f, 0.7f, 0.3f);
            Sweep(d, 0f, 210f, 70f, 30f, 15f, 0.8f);
            Noise(d, r, 0.003f, 5f, 0.12f, 0.05f);
            Noise(d, r, 0.02f, 350f, 0.2f, 0.8f, 0.3f);
            Echo(d, 0.05f, 0.2f); Echo(d, 0.12f, 0.1f);
            return d;
        }

        static float[] PumpBack(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.2f);
            Noise(d, r, 0f, 220f, 0.8f, 0.7f, 0.25f);
            NoiseSwell(d, r, 0.01f, 0.1f, 0.5f, 0.35f, 0.15f);
            Noise(d, r, 0.1f, 200f, 0.9f, 0.6f, 0.2f);
            Sweep(d, 0.1f, 180f, 120f, 30f, 40f, 0.4f);
            return d;
        }

        static float[] PumpForward(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.3f);
            NoiseSwell(d, r, 0f, 0.07f, 0.4f, 0.35f, 0.15f);
            Noise(d, r, 0.07f, 160f, 1f, 0.7f, 0.25f);
            Sweep(d, 0.07f, 200f, 110f, 35f, 35f, 0.7f);
            Ring(d, 0.07f, 1400f, 30f, 0.2f);
            return d;
        }

        static float[] ShellIn(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.22f);
            NoiseSwell(d, r, 0f, 0.05f, 0.25f, 0.4f, 0.2f);
            Sweep(d, 0.045f, 300f, 170f, 40f, 40f, 0.8f);
            Noise(d, r, 0.045f, 220f, 0.8f, 0.7f, 0.25f);
            Ring(d, 0.05f, 2600f, 60f, 0.15f);
            return d;
        }

        static float[] Explosion(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(2.8f);
            Noise(d, r, 0f, 9f, 1.0f, 0.2f);
            Sweep(d, 0f, 85f, 26f, 4f, 2.1f, 1.5f);
            Noise(d, r, 0f, 1.5f, 0.9f, 0.025f);
            Noise(d, r, 0.02f, 3f, 0.18f, 1f, 0.4f);
            Echo(d, 0.2f, 0.25f); Echo(d, 0.47f, 0.15f);
            return d;
        }

        static float[] GrenadePin(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.3f);
            Noise(d, r, 0f, 300f, 0.6f, 0.9f, 0.4f);
            Ring(d, 0f, 3600f, 40f, 0.4f);
            Ring(d, 0.12f, 2900f, 35f, 0.35f);
            Noise(d, r, 0.12f, 300f, 0.4f, 0.9f, 0.4f);
            return d;
        }

        static float[] GrenadeBounce(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.25f);
            Sweep(d, 0f, 320f, 200f, 40f, 40f, 0.9f);
            Noise(d, r, 0f, 180f, 0.6f, 0.6f, 0.2f);
            Ring(d, 0f, 1500f, 30f, 0.2f);
            return d;
        }

        static float[] Whoosh(int seed, float len, float lp)
        {
            var r = new System.Random(seed);
            var d = Buf(len + 0.05f);
            NoiseSwell(d, r, 0f, len, 1f, lp, 0.05f);
            return d;
        }

        static float[] KnifeHit(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.3f);
            Noise(d, r, 0f, 40f, 1f, 0.1f);
            Sweep(d, 0f, 150f, 80f, 30f, 30f, 0.8f);
            Noise(d, r, 0.01f, 25f, 0.35f, 0.4f, 0.2f);
            return d;
        }

        static float[] KnifeClang(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.5f);
            Noise(d, r, 0f, 200f, 0.8f, 1f, 0.4f);
            Ring(d, 0f, 2200f, 18f, 0.4f);
            Ring(d, 0f, 3300f, 22f, 0.3f);
            Ring(d, 0f, 5100f, 30f, 0.2f);
            return d;
        }

        static float[] Stab(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.6f);
            Noise(d, r, 0f, 30f, 1f, 0.08f);
            Sweep(d, 0f, 130f, 60f, 25f, 20f, 1f);
            NoiseSwell(d, r, 0.02f, 0.3f, 0.45f, 0.25f, 0.1f);
            Noise(d, r, 0.15f, 30f, 0.25f, 0.3f, 0.1f);
            return d;
        }

        static float[] Equip(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.35f);
            NoiseSwell(d, r, 0f, 0.22f, 0.5f, 0.25f, 0.1f);
            Noise(d, r, 0.2f, 260f, 0.8f, 0.8f, 0.3f);
            Ring(d, 0.2f, 2800f, 70f, 0.2f);
            return d;
        }

        static float[] Hurt(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.35f);
            Noise(d, r, 0f, 25f, 1f, 0.06f);
            Sweep(d, 0f, 120f, 50f, 20f, 16f, 0.9f);
            Noise(d, r, 0f, 80f, 0.3f, 0.5f, 0.2f);
            return d;
        }

        static float[] Whiz(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.18f);
            Noise(d, r, 0f, 40f, 0.6f, 0.6f, 0.3f, 0.02f);
            Sweep(d, 0f, 3200f, 1400f, 25f, 30f, 0.4f);
            Noise(d, r, 0f, 400f, 0.8f, 1f, 0.4f);
            return d;
        }

        static float[] ProneDrop(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.6f);
            NoiseSwell(d, r, 0f, 0.3f, 0.4f, 0.2f, 0.05f);
            Noise(d, r, 0.25f, 18f, 1f, 0.05f);
            Noise(d, r, 0.27f, 40f, 0.3f, 0.3f, 0.1f);
            return d;
        }

        // ------------------------------------------------------------------ abilities / destruction

        static float[] WoodBreak(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.9f);
            Noise(d, r, 0f, 30f, 1f, 0.5f, 0.15f);
            for (int i = 0; i < 7; i++) Noise(d, r, (float)r.NextDouble() * 0.25f, 60f + 40f * (float)r.NextDouble(), 0.6f, 0.7f, 0.25f);
            Sweep(d, 0f, 210f, 90f, 18f, 14f, 0.8f);
            Noise(d, r, 0.05f, 6f, 0.15f, 0.1f);
            return d;
        }

        static float[] SandBreak(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(1.1f);
            Noise(d, r, 0f, 9f, 1f, 0.08f);
            Sweep(d, 0f, 120f, 50f, 15f, 10f, 0.9f);
            NoiseSwell(d, r, 0.05f, 0.9f, 0.5f, 0.3f, 0.1f);
            return d;
        }

        /// <summary>Low buzzy flapping tone with a wobbling pitch.</summary>
        static float[] Fart(int seed, float pitch)
        {
            var r = new System.Random(seed);
            float len = 1.3f;
            var d = Buf(len);
            float phase = 0f, y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float u = t / len;
                float f = (95f + 35f * Mathf.Sin(t * 13f) + 20f * Mathf.Sin(t * 37f + 1f)) * pitch * Mathf.Lerp(1.1f, 0.7f, u);
                phase += f / SR;
                float saw = (phase % 1f) * 2f - 1f;
                float flap = 0.6f + 0.4f * Mathf.Sin(t * 70f * pitch);
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                y += 0.2f * (n - y);
                float env = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((len - t) / 0.25f);
                d[i] = (saw * 0.8f * flap + y * 0.6f) * env;
            }
            return d;
        }

        static float[] Cluck(int seed, float pitch)
        {
            var r = new System.Random(seed);
            var d = Buf(0.42f);
            for (int k = 0; k < 3; k++)
            {
                float st = k * 0.12f;
                Sweep(d, st, 900f * pitch, 520f * pitch, 25f, 30f, 0.8f);
                Sweep(d, st, 1800f * pitch, 1100f * pitch, 25f, 40f, 0.3f);
                Noise(d, r, st, 60f, 0.3f, 0.6f, 0.3f);
            }
            return d;
        }

        static float[] Parry(int seed, float tune)
        {
            var r = new System.Random(seed);
            var d = Buf(0.9f);
            Noise(d, r, 0f, 250f, 1f, 1f, 0.5f);
            Ring(d, 0f, 2650f * tune, 7f, 0.5f);
            Ring(d, 0f, 3970f * tune, 9f, 0.35f);
            Ring(d, 0f, 5210f * tune, 12f, 0.25f);
            Ring(d, 0f, 1320f * tune, 6f, 0.25f);
            Sweep(d, 0.01f, 5200f, 2600f, 20f, 30f, 0.2f); // ricochet whine
            return d;
        }

        static float[] RocketLaunch(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(1.4f);
            Noise(d, r, 0f, 12f, 1f, 0.15f);
            Sweep(d, 0f, 140f, 40f, 8f, 6f, 1.2f);
            NoiseSwell(d, r, 0.02f, 1.3f, 0.7f, 0.5f, 0.2f);
            Echo(d, 0.12f, 0.2f);
            return d;
        }

        static float[] HookFire(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.5f);
            Noise(d, r, 0f, 90f, 1f, 0.6f, 0.2f);
            Sweep(d, 0f, 400f, 120f, 30f, 25f, 0.6f);
            NoiseSwell(d, r, 0.03f, 0.4f, 0.5f, 0.7f, 0.3f);
            return d;
        }

        static float[] HookHit(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.4f);
            Noise(d, r, 0f, 200f, 1f, 0.9f, 0.3f);
            Ring(d, 0f, 1700f, 25f, 0.4f);
            Ring(d, 0f, 2450f, 30f, 0.3f);
            return d;
        }

        static float[] Reel(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.7f);
            for (int i = 0; i < 14; i++) Noise(d, r, i * 0.045f, 300f, 0.5f, 0.8f, 0.4f);
            NoiseSwell(d, r, 0f, 0.7f, 0.4f, 0.4f, 0.1f);
            return d;
        }

        static float[] Warp(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.8f);
            Sweep(d, 0f, 200f, 1600f, 5f, 4f, 0.7f);
            Sweep(d, 0f, 300f, 2400f, 5f, 4f, 0.35f);
            NoiseSwell(d, r, 0f, 0.7f, 0.5f, 0.3f, 0.1f);
            return d;
        }

        static float[] Slip(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(1f);
            Sweep(d, 0f, 300f, 1500f, 7f, 6f, 0.6f);   // comedic slide-whistle up
            NoiseSwell(d, r, 0f, 0.3f, 0.4f, 0.4f, 0.1f);
            Noise(d, r, 0.55f, 16f, 1f, 0.06f);         // thud
            Sweep(d, 0.55f, 120f, 50f, 20f, 14f, 0.9f);
            return d;
        }

        static float[] Chime(float f0, float f1)
        {
            var d = Buf(0.6f);
            Ring(d, 0f, f0, 9f, 0.6f);
            Ring(d, 0.09f, f1, 8f, 0.6f);
            Ring(d, 0.09f, f1 * 2f, 14f, 0.15f);
            return d;
        }

        static float[] TrapHorn()
        {
            var d = Buf(1.2f);
            Sweep(d, 0f, 420f, 300f, 2f, 3f, 0.6f);
            Sweep(d, 0f, 630f, 450f, 2f, 3f, 0.3f);
            Sweep(d, 0.35f, 300f, 200f, 2f, 3f, 0.6f);
            Sweep(d, 0.35f, 450f, 300f, 2f, 3f, 0.3f);
            return d;
        }

        static float[] Scream(int seed)
        {
            var r = new System.Random(seed);
            float len = 1.7f;
            var d = Buf(len);
            float p1 = 0f, p2 = 0f, p3 = 0f, y = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float vib = Mathf.Sin(t * 38f) * 60f + Mathf.Sin(t * 9f) * 40f;
                float f = Mathf.Lerp(700f, 1150f, Mathf.Clamp01(t / 0.25f)) + vib - t * 120f;
                p1 += f / SR; p2 += f * 1.51f / SR; p3 += f * 2.03f / SR;
                float saw = (p1 % 1f) * 2f - 1f + ((p2 % 1f) * 2f - 1f) * 0.6f + ((p3 % 1f) * 2f - 1f) * 0.35f;
                float n = (float)(r.NextDouble() * 2.0 - 1.0);
                y += 0.5f * (n - y);
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((len - t) / 0.3f);
                d[i] = (saw * 0.7f + y * 0.5f) * env;
            }
            return d;
        }

        static float[] Boing()
        {
            var d = Buf(0.9f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float f = 140f + 260f * t + Mathf.Sin(t * 60f) * 70f * Mathf.Exp(-t * 2f);
                phase += f / SR;
                d[i] = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 3f);
            }
            return d;
        }

        static float[] Hammer(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.5f);
            Noise(d, r, 0f, 90f, 1f, 0.5f, 0.1f);
            Sweep(d, 0f, 260f, 140f, 30f, 25f, 0.9f);
            Noise(d, r, 0.12f, 120f, 0.5f, 0.6f, 0.2f);
            Sweep(d, 0.12f, 240f, 130f, 30f, 30f, 0.6f);
            return d;
        }

        static float[] ErrorTone()
        {
            var d = Buf(1.1f);
            Ring(d, 0f, 523f, 3.5f, 0.5f);
            Ring(d, 0f, 659f, 3.5f, 0.35f);
            Ring(d, 0.16f, 392f, 3f, 0.5f);
            Ring(d, 0.16f, 494f, 3f, 0.35f);
            return d;
        }

        static float[] UiClick(int seed)
        {
            var r = new System.Random(seed);
            var d = Buf(0.06f);
            Noise(d, r, 0f, 500f, 1f, 0.9f, 0.4f);
            Ring(d, 0f, 2400f, 120f, 0.4f);
            return d;
        }

        static float[] PitchSweep(float f0, float f1)
        {
            var d = Buf(0.6f);
            float phase = 0f;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)SR;
                float f = Mathf.Lerp(f0, f1, t / 0.6f);
                phase += f / SR;
                d[i] = Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Sin(Mathf.Clamp01(t / 0.6f) * Mathf.PI) * (0.7f + 0.3f * Mathf.Sin(t * 90f));
            }
            return d;
        }
    }
}
