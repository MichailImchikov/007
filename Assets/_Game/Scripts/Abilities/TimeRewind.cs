using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// "Перемотка" ability: the whole field winds back 5 seconds like a tape. Every combatant (position, look,
    /// health, ammo, alive) and every loose physics body is recorded 20 times a second; on activation everybody is
    /// frozen and the recording plays backwards on screen (VHS overlay, chromatic smear, tape squeal), then the game
    /// resumes from the state of 5 seconds ago. The fallen get back up, grenades thrown meanwhile are un-thrown.
    /// Kills already counted stay on the scoreboard. Online every machine rewinds the fighters it simulates
    /// (AbilityNetEvent.Rewind); everybody else's rewind arrives through the snapshots.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class TimeRewind : MonoBehaviour
    {
        public static TimeRewind Instance { get; private set; }
        public static bool Playing => Instance && Instance.playing;
        /// <summary>0..1 while the tape winds back (screen effects).</summary>
        public static float Amount => Instance ? Instance.fx : 0f;

        public const float Span = 5f;
        const float Step = 0.05f, PlayTime = 1.7f;
        const int N = 120; // ≥ (Span + margin) / Step
        const int MaxBodies = 160;

        struct Frame
        {
            public float time, yaw, pitch, health;
            public Vector3 pos;
            public bool alive;
            public int ammo;
            public int[] mags, reserves;
        }

        class Track
        {
            public Combatant c;
            public PlayerHealth ph;
            public CombatBot bot;
            public readonly Frame[] buf = new Frame[N];
            public int head = -1, count;
            public bool skip, wasLockedMotor;
            public void Push(Frame f) { head = (head + 1) % N; buf[head] = f; if (count < N) count++; }
        }

        struct BodyFrame { public float time; public Vector3 pos, vel, ang; public Quaternion rot; }

        class BodyTrack
        {
            public Rigidbody rb;
            public readonly BodyFrame[] buf = new BodyFrame[N];
            public int head = -1, count;
            public float born;
            public bool wasKinematic;
            public void Push(BodyFrame f) { head = (head + 1) % N; buf[head] = f; if (count < N) count++; }
        }

        readonly Dictionary<Combatant, Track> tracks = new Dictionary<Combatant, Track>();
        readonly Dictionary<Rigidbody, BodyTrack> bodies = new Dictionary<Rigidbody, BodyTrack>();
        readonly List<Combatant> deadKeys = new List<Combatant>();
        readonly List<Rigidbody> deadBodies = new List<Rigidbody>();
        float nextRec, nextScan;
        bool playing;
        float playT, tFrom, tTo, fx;
        Combatant caster;

        public static TimeRewind Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("TimeRewind").AddComponent<TimeRewind>();
            return Instance;
        }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (playing) Finish(true);
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ recording

        void LateUpdate()
        {
            if (playing) return;
            float now = Time.time;
            if (now >= nextScan) { nextScan = now + 1f; ScanBodies(); }
            if (now < nextRec) return;
            nextRec = now + Step;

            foreach (var c in Combatant.All)
            {
                if (!c || c.remote) continue;
                if (!tracks.TryGetValue(c, out var tr))
                {
                    tr = new Track { c = c, ph = c.GetComponent<PlayerHealth>(), bot = c.GetComponent<CombatBot>() };
                    if (!tr.ph && !tr.bot) continue; // training dummies etc. stay where they are
                    tracks[c] = tr;
                }
                tr.Push(Capture(tr, now));
            }
            deadKeys.Clear();
            foreach (var kv in tracks) if (!kv.Key) deadKeys.Add(kv.Key);
            foreach (var k in deadKeys) tracks.Remove(k);

            deadBodies.Clear();
            foreach (var kv in bodies)
            {
                var rb = kv.Key;
                if (!rb) { deadBodies.Add(rb); continue; }
                if (rb.isKinematic) continue;
                kv.Value.Push(new BodyFrame { time = now, pos = rb.position, rot = rb.rotation, vel = rb.linearVelocity, ang = rb.angularVelocity });
            }
            foreach (var k in deadBodies) bodies.Remove(k);
        }

        Frame Capture(Track tr, float now)
        {
            var c = tr.c;
            var f = new Frame { time = now, pos = c.transform.position, yaw = c.transform.eulerAngles.y, alive = c.alive };
            if (tr.ph)
            {
                f.pitch = tr.ph.playerCam ? tr.ph.playerCam.Pitch : 0f;
                if (tr.ph.playerCam) f.yaw = tr.ph.playerCam.Yaw;
                f.health = tr.ph.Health;
                var lo = tr.ph.loadout;
                if (lo && lo.weapons != null)
                {
                    f.mags = new int[lo.weapons.Length];
                    f.reserves = new int[lo.weapons.Length];
                    for (int i = 0; i < lo.weapons.Length; i++)
                        if (lo.weapons[i]) { f.mags[i] = lo.weapons[i].Ammo; f.reserves[i] = lo.weapons[i].Reserve; }
                }
            }
            else if (tr.bot)
            {
                f.health = tr.bot.Health;
                f.ammo = tr.bot.RewindAmmo;
            }
            return f;
        }

        void ScanBodies()
        {
            if (bodies.Count >= MaxBodies) return;
            foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                if (bodies.Count >= MaxBodies) break;
                if (!rb || rb.isKinematic || bodies.ContainsKey(rb) || rb.GetComponentInParent<Combatant>()) continue;
                bodies[rb] = new BodyTrack { rb = rb, born = Time.time };
            }
        }

        static Frame Sample(Track tr, float time)
        {
            // newest → oldest
            Frame newer = tr.buf[tr.head];
            if (time >= newer.time) return newer;
            for (int k = 1; k < tr.count; k++)
            {
                var older = tr.buf[(tr.head - k + N) % N];
                if (older.time <= time)
                {
                    float u = Mathf.InverseLerp(older.time, newer.time, time);
                    var f = u < 0.5f ? older : newer;
                    f.pos = Vector3.Lerp(older.pos, newer.pos, u);
                    f.yaw = Mathf.LerpAngle(older.yaw, newer.yaw, u);
                    f.pitch = Mathf.Lerp(older.pitch, newer.pitch, u);
                    f.health = Mathf.Lerp(older.health, newer.health, u);
                    f.time = time;
                    return f;
                }
                newer = older;
            }
            return newer; // the oldest we have
        }

        static BodyFrame Sample(BodyTrack tr, float time)
        {
            BodyFrame newer = tr.buf[tr.head];
            if (time >= newer.time) return newer;
            for (int k = 1; k < tr.count; k++)
            {
                var older = tr.buf[(tr.head - k + N) % N];
                if (older.time <= time)
                {
                    float u = Mathf.InverseLerp(older.time, newer.time, time);
                    return new BodyFrame
                    {
                        time = time, pos = Vector3.Lerp(older.pos, newer.pos, u), rot = Quaternion.Slerp(older.rot, newer.rot, u),
                        vel = Vector3.Lerp(older.vel, newer.vel, u), ang = Vector3.Lerp(older.ang, newer.ang, u)
                    };
                }
                newer = older;
            }
            return newer;
        }

        // ------------------------------------------------------------------ activation

        /// <summary>Ability entry point. False (with a reason for the HUD) when it can't run now.</summary>
        public static bool Begin(Combatant who, out string why)
        {
            why = null;
            var r = Ensure();
            if (r.playing) { why = "Плёнка уже мотается"; return false; }
            if (SkyArena.Active) { why = "Не на арене"; return false; }
            float oldest = float.MaxValue;
            foreach (var tr in r.tracks.Values) if (tr.count > 0) oldest = Mathf.Min(oldest, tr.buf[(tr.head - tr.count + 1 + N) % N].time);
            if (oldest == float.MaxValue || Time.time - oldest < 1f) { why = "Ещё нечего перематывать"; return false; }
            r.Play(who, Mathf.Max(oldest, Time.time - Span));
            return true;
        }

        /// <summary>
        /// Online: another machine's fighter rewound time. This machine winds back what it simulates itself (its
        /// player; the host also its bots) over the same span, with the same tape effect.
        /// </summary>
        public static void BeginRemote(Combatant who)
        {
            var r = Ensure();
            if (r.playing) return;
            float oldest = float.MaxValue;
            foreach (var tr in r.tracks.Values) if (tr.count > 0) oldest = Mathf.Min(oldest, tr.buf[(tr.head - tr.count + 1 + N) % N].time);
            if (oldest == float.MaxValue) oldest = Time.time;
            r.Play(who, Mathf.Max(oldest, Time.time - Span));
        }

        void Play(Combatant who, float target)
        {
            caster = who;
            playing = true;
            playT = 0f;
            tFrom = Time.time;
            tTo = target;
            // fights are part of the past now
            for (int i = KnifeDuel.Active.Count - 1; i >= 0; i--) if (KnifeDuel.Active[i]) KnifeDuel.Active[i].Abort();

            foreach (var tr in tracks.Values)
            {
                if (!tr.c || tr.count == 0) { if (tr != null) tr.skip = true; continue; }
                var then = Sample(tr, tTo);
                // alive now but dead back then (respawned meanwhile): stays where it is
                tr.skip = !then.alive && tr.c.alive;
                if (tr.skip) continue;
                if (tr.ph)
                {
                    if (tr.ph.IsDead) tr.ph.Revive(null); // watch it unwind on your feet
                    tr.wasLockedMotor = tr.ph.motor.Locked;
                    tr.ph.motor.Locked = true;
                    tr.ph.playerCam.Locked = true;
                    if (tr.ph.loadout && tr.ph.loadout.Active) tr.ph.loadout.Active.CancelReload();
                }
                else if (tr.bot)
                {
                    if (tr.bot.IsAlive) tr.bot.ExternalControl = true;
                    tr.bot.CancelMelee();
                }
                if (tr.c.abilities) tr.c.abilities.ClearStatus();
            }
            foreach (var bt in bodies.Values)
            {
                if (!bt.rb || bt.count == 0) continue;
                bt.wasKinematic = bt.rb.isKinematic;
                bt.rb.isKinematic = true;
            }

            RewindFX.Ensure().Begin();
            Shockwave.Spawn(who ? who.transform.position + Vector3.up * 0.1f : Vector3.zero, 70f, AbilityDB.Get(AbilityType.Rewind).color);
        }

        void Update()
        {
            if (!playing) { fx = Mathf.MoveTowards(fx, 0f, Time.unscaledDeltaTime * 3f); return; }
            float dt = Time.unscaledDeltaTime;
            playT += dt / PlayTime;
            fx = Mathf.MoveTowards(fx, 1f, dt * 6f);
            // the tape speeds up, then brakes into the moment
            float u = Mathf.Clamp01(playT);
            float e = u < 0.5f ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) / 2f;
            float head = Mathf.Lerp(tFrom, tTo, e);
            Apply(head, false);
            if (playT >= 1f) Finish(false);
        }

        void Apply(float time, bool final)
        {
            foreach (var tr in tracks.Values)
            {
                if (tr.skip || !tr.c || tr.count == 0) continue;
                var f = Sample(tr, time);
                if (tr.ph)
                {
                    var t = tr.ph.transform;
                    t.position = f.pos;
                    tr.ph.playerCam.SetLook(f.yaw, f.pitch);
                    t.rotation = Quaternion.Euler(0f, f.yaw, 0f);
                }
                else if (tr.bot)
                {
                    // the fallen rise when the tape passes their death
                    if (f.alive && !tr.bot.IsAlive)
                    {
                        tr.bot.Revive(null);
                        if (tr.bot.IsAlive) tr.bot.ExternalControl = true;
                        FX.Ensure().Puff(f.pos + Vector3.up, Vector3.up, 10, 1f, new Vector2(0.5f, 2f), new Vector2(0.5f, 1f), new Vector2(0.4f, 0.8f),
                                         new Color(0.4f, 0.9f, 1f, 0.45f), new Color(0.6f, 0.95f, 1f, 0.3f));
                    }
                    if (!tr.bot.IsAlive) continue;
                    tr.bot.transform.SetPositionAndRotation(f.pos, Quaternion.Euler(0f, f.yaw, 0f));
                }
            }
            foreach (var bt in bodies.Values)
            {
                if (!bt.rb || bt.count == 0) continue;
                var f = Sample(bt, time);
                bt.rb.transform.SetPositionAndRotation(f.pos, f.rot);
            }
        }

        void Finish(bool aborted)
        {
            playing = false;
            if (!aborted) Apply(tTo, true);
            foreach (var tr in tracks.Values)
            {
                if (tr.skip || !tr.c || tr.count == 0) continue;
                var f = Sample(tr, tTo);
                if (tr.ph)
                {
                    tr.ph.motor.Locked = false;
                    tr.ph.playerCam.Locked = false;
                    if (!aborted)
                    {
                        if (tr.ph.IsDead) tr.ph.Revive(null);
                        tr.ph.SetHealthRewind(f.health);
                        var lo = tr.ph.loadout;
                        if (lo && lo.weapons != null && f.mags != null)
                            for (int i = 0; i < lo.weapons.Length && i < f.mags.Length; i++)
                                if (lo.weapons[i]) lo.weapons[i].SetAmmoRewind(f.mags[i], f.reserves[i]);
                    }
                }
                else if (tr.bot)
                {
                    if (!aborted && f.alive && !tr.bot.IsAlive) tr.bot.Revive(null);
                    if (tr.bot.IsAlive)
                    {
                        tr.bot.ExternalControl = false;
                        if (!aborted) { tr.bot.SetHealthRewind(f.health); tr.bot.RewindAmmo = f.ammo; }
                    }
                }
            }
            foreach (var bt in new List<BodyTrack>(bodies.Values))
            {
                if (!bt.rb) continue;
                if (bt.count > 0) bt.rb.isKinematic = bt.wasKinematic;
                // thrown after the moment we went back to: it never happened
                if (!aborted && bt.born > tTo + 0.2f && (bt.rb.GetComponent<Grenade>() || bt.rb.GetComponent<RocketProjectile>()))
                {
                    Destroy(bt.rb.gameObject);
                    continue;
                }
                if (!aborted && bt.count > 0 && !bt.rb.isKinematic)
                {
                    var f = Sample(bt, tTo);
                    bt.rb.linearVelocity = f.vel;
                    bt.rb.angularVelocity = f.ang;
                }
            }
            // the recording after the target moment is gone: start over from here
            foreach (var tr in tracks.Values) { tr.head = -1; tr.count = 0; }
            foreach (var bt in bodies.Values) { bt.head = -1; bt.count = 0; bt.born = Time.time; }
            nextRec = 0f;
            if (RewindFX.Instance) RewindFX.Instance.End();
        }
    }

    /// <summary>Screen side of the rewind: VHS tint, rolling scanlines, tracking noise, ◄◄ and a running timecode, tape sound.</summary>
    [DefaultExecutionOrder(960)]
    public class RewindFX : MonoBehaviour
    {
        public static RewindFX Instance { get; private set; }

        CanvasGroup group;
        RawImage lines, noise, band;
        Image tint;
        Text rew, code;
        RectTransform bandRt;
        Volume volume;
        ChromaticAberration ca;
        LensDistortion ld;
        ColorAdjustments col;
        AudioSource audioSrc;
        float t, w;
        bool on;
        UniversalAdditionalCameraData camData;
        bool camPostWas;

        public static RewindFX Ensure()
        {
            if (Instance) return Instance;
            Instance = new GameObject("RewindFX").AddComponent<RewindFX>();
            return Instance;
        }

        void Awake()
        {
            Instance = this;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            tint = Stretch("Tint").gameObject.AddComponent<Image>();
            tint.color = new Color(0.15f, 0.45f, 0.75f, 0.22f);
            tint.raycastTarget = false;

            lines = Stretch("Lines").gameObject.AddComponent<RawImage>();
            lines.texture = LinesTex();
            lines.color = new Color(1f, 1f, 1f, 0.55f);
            lines.raycastTarget = false;

            noise = Stretch("Noise").gameObject.AddComponent<RawImage>();
            noise.texture = NoiseTex(256, 256, 0.5f);
            noise.color = new Color(1f, 1f, 1f, 0.12f);
            noise.raycastTarget = false;

            bandRt = Stretch("Band");
            bandRt.anchorMin = new Vector2(0f, 0.5f); bandRt.anchorMax = new Vector2(1f, 0.5f);
            bandRt.sizeDelta = new Vector2(0f, 70f);
            band = bandRt.gameObject.AddComponent<RawImage>();
            band.texture = NoiseTex(512, 16, 1f);
            band.color = new Color(1f, 1f, 1f, 0.5f);
            band.raycastTarget = false;

            rew = Label(font, "Rew", new Vector2(0f, 1f), new Vector2(70f, -60f), 72, TextAnchor.UpperLeft);
            rew.text = "◄◄  ПЕРЕМОТКА";
            code = Label(font, "Code", new Vector2(1f, 1f), new Vector2(-70f, -66f), 46, TextAnchor.UpperRight);

            var vgo = new GameObject("RewindVolume");
            vgo.transform.SetParent(transform, false);
            volume = vgo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 60;
            volume.weight = 0f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            ca = prof.Add<ChromaticAberration>(true); ca.intensity.Override(1f);
            ld = prof.Add<LensDistortion>(true); ld.intensity.Override(-0.25f); ld.scale.Override(1f);
            col = prof.Add<ColorAdjustments>(true); col.saturation.Override(-55f); col.contrast.Override(18f); col.hueShift.Override(-12f);
            var fg = prof.Add<FilmGrain>(true); fg.type.Override(FilmGrainLookup.Medium3); fg.intensity.Override(0.8f);
            volume.sharedProfile = prof;

            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.spatialBlend = 0f;
            audioSrc.playOnAwake = false;
            audioSrc.clip = TapeClip();
            audioSrc.volume = 0.75f;
        }

        RectTransform Stretch(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        Text Label(Font font, string name, Vector2 anchor, Vector2 pos, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(900f, 100f);
            var tx = go.AddComponent<Text>();
            tx.font = font; tx.fontSize = size; tx.fontStyle = FontStyle.Bold; tx.alignment = align;
            tx.color = new Color(0.92f, 0.97f, 1f);
            tx.horizontalOverflow = HorizontalWrapMode.Overflow;
            tx.raycastTarget = false;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0.1f, 0.3f, 0.9f, 0.9f);
            sh.effectDistance = new Vector2(4f, -3f);
            return tx;
        }

        static Texture2D LinesTex()
        {
            var tx = new Texture2D(1, 4, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            tx.SetPixels32(new[] { new Color32(0, 0, 0, 150), new Color32(0, 0, 0, 40), new Color32(255, 255, 255, 10), new Color32(0, 0, 0, 40) });
            tx.Apply();
            return tx;
        }

        static Texture2D NoiseTex(int w, int h, float density)
        {
            var tx = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)Random.Range(120, 256);
                byte a = Random.value < density ? (byte)Random.Range(60, 256) : (byte)0;
                px[i] = new Color32(v, v, v, a);
            }
            tx.SetPixels32(px); tx.Apply();
            return tx;
        }

        /// <summary>Tape winding back: warbling squeal sweeping up, motor hum and hiss.</summary>
        static AudioClip TapeClip()
        {
            const int SR = 44100;
            float len = 1.9f;
            int n = (int)(SR * len);
            var data = new float[n];
            float ph1 = 0f, ph2 = 0f, ph3 = 0f, lp = 0f;
            var rnd = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float u = t / len;
                float env = Mathf.Clamp01(t / 0.06f) * Mathf.Clamp01((len - t) / 0.25f);
                float speed = u < 0.5f ? 2f * u * u : 1f - Mathf.Pow(-2f * u + 2f, 2f) / 2f;
                float sweep = Mathf.Lerp(380f, 2400f, Mathf.Sin(speed * Mathf.PI));
                float wob = 1f + 0.08f * Mathf.Sin(t * 37f) + 0.04f * Mathf.Sin(t * 91f);
                ph1 += 2f * Mathf.PI * sweep * wob / SR;
                ph2 += 2f * Mathf.PI * sweep * 1.51f * wob / SR;
                ph3 += 2f * Mathf.PI * (60f + 140f * speed) / SR;
                float noise = (float)(rnd.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.25f;
                float chirp = Mathf.Sin(ph1) * 0.32f + Mathf.Sin(ph2) * 0.12f;
                float flutter = 0.6f + 0.4f * Mathf.Sin(t * 2f * Mathf.PI * (14f + 26f * speed));
                data[i] = env * (chirp * flutter + lp * 0.22f + Mathf.Sin(ph3) * 0.12f);
            }
            var clip = AudioClip.Create("TapeRewind", n, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void Begin()
        {
            on = true;
            t = 0f;
            if (audioSrc) { audioSrc.Stop(); audioSrc.Play(); }
            var cam = Camera.main;
            camData = cam ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            if (camData) { camPostWas = camData.renderPostProcessing; camData.renderPostProcessing = true; }
        }

        public void End()
        {
            on = false;
            ProcAudio.Play(Sfx.Click, 0.9f, 0f);
            ProcAudio.Play(Sfx.Warp, 0.6f, 0f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            w = Mathf.MoveTowards(w, on ? 1f : 0f, dt * (on ? 7f : 3.5f));
            group.alpha = w;
            if (volume)
            {
                volume.weight = w;
                if (ld != null) ld.intensity.value = -0.18f - 0.12f * Mathf.Sin(t * 9f);
                if (ca != null) ca.intensity.value = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(t * 13f));
            }
            if (w <= 0f)
            {
                if (camData) { camData.renderPostProcessing = camPostWas; camData = null; }
                return;
            }
            // rolling scanlines and a tracking band crawling up the screen
            float h = Screen.height / 3f;
            lines.uvRect = new Rect(0f, -t * 3f, 1f, h);
            noise.uvRect = new Rect(Random.value, Random.value, Screen.width / 256f, Screen.height / 256f);
            float y = Mathf.Repeat(t * 0.9f, 1.3f) - 0.15f;
            bandRt.anchorMin = new Vector2(0f, y); bandRt.anchorMax = new Vector2(1f, y);
            bandRt.sizeDelta = new Vector2(0f, 50f + 40f * Mathf.Sin(t * 5f));
            band.uvRect = new Rect(Random.value, 0f, 2f, 1f);
            rew.enabled = Mathf.Repeat(t * 2.2f, 1f) < 0.7f || !on;
            var r = TimeRewind.Instance;
            float left = r && TimeRewind.Playing ? TimeRewind.Span * (1f - Mathf.Clamp01(t / 1.7f)) : 0f;
            code.text = "-00:0" + (TimeRewind.Span - left).ToString("0.00").Replace(',', '.');
            tint.color = new Color(0.15f, 0.45f, 0.75f, 0.18f + 0.06f * Mathf.Sin(t * 20f));
        }
    }
}
