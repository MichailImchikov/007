using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// One dance room of the disco: plays its own track (3D, loud inside the room, fading through the doors),
    /// listens to it (loudness, bass, beat onsets) and runs the light show from what it hears —
    /// LED dance floor patterns, moving heads with fake volumetric beams, laser fans, neon pulses,
    /// a spectrum on the LED wall, colour washes and a mirror ball. Dancers in the room move to its beat.
    /// Built by Tools > FPS > Maps > Disco (Editor/MapBuilderDisco.cs).
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class DiscoRoom : MonoBehaviour
    {
        public static readonly List<DiscoRoom> All = new List<DiscoRoom>();

        public string title = "Зал";
        public AudioClip track;
        [Range(0f, 1f)] public float volume = 0.75f;
        public Color[] palette = { new Color(1f, 0.1f, 0.5f), new Color(0.1f, 0.6f, 1f), new Color(0.6f, 0.1f, 1f), new Color(1f, 0.8f, 0.1f) };
        public Vector3 center;
        public float radius = 12f;

        [Header("Light show (built by the map builder)")]
        public Renderer[] tiles = new Renderer[0];
        public int tileCols = 1;
        public Transform[] movingHeads = new Transform[0];
        public Transform[] lasers = new Transform[0];
        public Renderer[] neon = new Renderer[0];
        public Renderer ledWall;
        public Light[] washes = new Light[0];
        public Transform mirrorBall;

        // ---- what the room hears
        /// <summary>Smoothed loudness 0..1 (normalized to the track's own recent peak).</summary>
        public float Level { get; private set; }
        /// <summary>Bass kick envelope 0..1 (jumps on a beat, decays).</summary>
        public float Kick { get; private set; }
        /// <summary>Beats since the start (increments on each detected onset or the fallback clock).</summary>
        public int BeatCount { get; private set; }
        /// <summary>0..1 position between the last beat and the expected next one.</summary>
        public float BeatPhase => Mathf.Clamp01((Time.time - lastBeat) / beatInterval);
        public float BeatInterval => beatInterval;
        public AudioSource Source => src;

        const float BleedFloor = 0.03f;
        AudioSource src;
        AnimationCurve rolloff;
        AudioListener listener;
        readonly float[] spec = new float[512], spec2 = new float[512];
        readonly float[] wave = new float[256];
        float bassAvg = 0.01f, peak = 0.05f, lastBeat, beatInterval = 0.5f;
        int pattern;
        float patternT;
        readonly System.Random rnd = new System.Random();

        // ---- runtime visuals
        MaterialPropertyBlock mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), EmissionColor = Shader.PropertyToID("_EmissionColor");
        Light[] headLights;
        Renderer[] beams;
        Transform[][] laserBeams;
        Renderer[][] laserRends;
        Texture2D ledTex;
        Color32[] ledPx;
        float[] tileSpark;
        Light strobe;
        float strobeT;
        static Material beamMat, laserMat;

        public static DiscoRoom Nearest(Vector3 p)
        {
            DiscoRoom best = null; float bd = float.MaxValue;
            foreach (var r in All)
            {
                if (!r) continue;
                float d = (r.center - p).sqrMagnitude / (r.radius * r.radius);
                if (d < bd) { bd = d; best = r; }
            }
            return best;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            lastBeat = Time.time;

            var sgo = new GameObject("Music");
            sgo.transform.SetParent(transform, false);
            sgo.transform.position = center + Vector3.up * 3f;
            src = sgo.AddComponent<AudioSource>();
            src.clip = track;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            // loud inside the room, fading through the doors to a faint bleed (never 0: the analysis below reads
            // the source's output after distance attenuation and divides it back out)
            src.maxDistance = radius * 2.1f;
            src.minDistance = 1f;
            float knee = radius * 0.85f / src.maxDistance, slope = (BleedFloor - 1f) / (1f - knee);
            rolloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(knee, 1f, 0f, slope), new Keyframe(1f, BleedFloor, slope, 0f));
            src.rolloffMode = AudioRolloffMode.Custom;
            src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);
            src.dopplerLevel = 0f;
            src.spread = 120f;
            src.priority = 10;
            src.volume = volume * ProcAudio.MasterVolume;
            if (track)
            {
                // each match starts somewhere else in the track
                src.time = (float)rnd.NextDouble() * Mathf.Max(0f, track.length - 20f);
                src.Play();
            }

            BuildHeads();
            BuildLasers();
            BuildLed();
            tileSpark = new float[tiles.Length];

            var stgo = new GameObject("Strobe");
            stgo.transform.SetParent(transform, false);
            stgo.transform.position = center + Vector3.up * 7f;
            strobe = stgo.AddComponent<Light>();
            strobe.type = LightType.Point; strobe.range = radius * 2.2f; strobe.intensity = 0f; strobe.color = Color.white; strobe.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ building

        static Material BeamMaterial()
        {
            if (beamMat) return beamMat;
            // soft fade along the beam (v = 0 at the lamp) and across it
            const int W = 32, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BeamFade" };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float v = y / (H - 1f), u = x / (W - 1f);
                    float along = Mathf.Pow(1f - v, 1.6f);
                    float across = Mathf.Sin(u * Mathf.PI);
                    float a = along * (0.35f + 0.65f * across * across);
                    t.SetPixel(x, y, new Color(a, a, a, a));
                }
            t.Apply();
            beamMat = FX.MakeParticleMat("DiscoBeam", t, true, Color.white, true);
            return beamMat;
        }

        static Material LaserMaterial()
        {
            if (laserMat) return laserMat;
            var t = new Texture2D(4, 32, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "LaserFade" };
            // cylinder uv: v runs along the beam (0 at the far end of the default primitive, 1 at the emitter)
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 4; x++) { float a = Mathf.Lerp(0.25f, 1f, y / 31f); t.SetPixel(x, y, new Color(1f, 1f, 1f, a)); }
            t.Apply();
            laserMat = FX.MakeParticleMat("DiscoLaser", t, true, Color.white, true);
            return laserMat;
        }

        /// <summary>Open cone along +Z: apex at the origin, length 1, radius 1 at the far end (scaled per beam).</summary>
        static Mesh coneMesh;
        static Mesh Cone()
        {
            if (coneMesh) return coneMesh;
            const int S = 16;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= S; i++)
            {
                float a = i / (float)S * Mathf.PI * 2f;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d * 0.03f); uv.Add(new Vector2(i / (float)S, 0f));
                v.Add(d + Vector3.forward); uv.Add(new Vector2(i / (float)S, 1f));
            }
            for (int i = 0; i < S; i++)
            {
                int a = i * 2;
                tri.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
            }
            // u wraps around the cone: remap so that the edges of the beam (silhouette) fade — use |sin| of the angle
            for (int i = 0; i < uv.Count; i++) uv[i] = new Vector2(Mathf.Abs(Mathf.Sin(uv[i].x * Mathf.PI)) * 0.5f + 0.25f, uv[i].y);
            coneMesh = new Mesh { name = "DiscoBeamCone" };
            coneMesh.SetVertices(v); coneMesh.SetUVs(0, uv); coneMesh.SetTriangles(tri, 0);
            coneMesh.RecalculateBounds();
            coneMesh.bounds = new Bounds(new Vector3(0f, 0f, 0.5f), new Vector3(2f, 2f, 1f));
            return coneMesh;
        }

        void BuildHeads()
        {
            headLights = new Light[movingHeads.Length];
            beams = new Renderer[movingHeads.Length];
            for (int i = 0; i < movingHeads.Length; i++)
            {
                var h = movingHeads[i];
                if (!h) continue;
                var lgo = new GameObject("Spot");
                lgo.transform.SetParent(h, false);
                lgo.transform.localPosition = new Vector3(0f, 0f, 0.15f);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Spot; l.spotAngle = 24f; l.innerSpotAngle = 10f; l.range = 30f; l.intensity = 12f; l.shadows = LightShadows.None;
                headLights[i] = l;

                var bgo = new GameObject("Beam");
                bgo.transform.SetParent(h, false);
                bgo.transform.localPosition = new Vector3(0f, 0f, 0.1f);
                bgo.transform.localScale = new Vector3(2.1f, 2.1f, 10f);
                bgo.AddComponent<MeshFilter>().sharedMesh = Cone();
                var mr = bgo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = BeamMaterial();
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                beams[i] = mr;
            }
        }

        void BuildLasers()
        {
            laserBeams = new Transform[lasers.Length][];
            laserRends = new Renderer[lasers.Length][];
            for (int i = 0; i < lasers.Length; i++)
            {
                const int N = 7;
                laserBeams[i] = new Transform[N];
                laserRends[i] = new Renderer[N];
                if (!lasers[i]) continue;
                for (int k = 0; k < N; k++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(go.GetComponent<Collider>());
                    go.name = "LaserBeam";
                    var pivot = new GameObject("LaserPivot").transform;
                    pivot.SetParent(lasers[i], false);
                    go.transform.SetParent(pivot, false);
                    go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    go.transform.localPosition = new Vector3(0f, 0f, 14f);
                    go.transform.localScale = new Vector3(0.05f, 14f, 0.05f);
                    var r = go.GetComponent<Renderer>();
                    r.sharedMaterial = LaserMaterial();
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    laserBeams[i][k] = pivot;
                    laserRends[i][k] = r;
                }
            }
        }

        void BuildLed()
        {
            if (!ledWall) return;
            ledTex = new Texture2D(48, 20, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "DiscoLED" };
            ledPx = new Color32[48 * 20];
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "DiscoLED_" + title };
            m.SetTexture("_BaseMap", ledTex);
            m.SetColor("_BaseColor", Color.white * 2.2f);
            ledWall.sharedMaterial = m;
        }

        // ------------------------------------------------------------------ listening

        void Listen(float dt)
        {
            if (!src || !src.isPlaying)
            {
                Level = Mathf.MoveTowards(Level, 0.35f, dt);
                FallbackBeat(0.5f);
                Kick = Mathf.MoveTowards(Kick, 0f, dt * 4f);
                return;
            }
            src.volume = volume * ProcAudio.MasterVolume;
            // what the listener hears of this room → undo that gain so every room "hears" its full track
            if (!listener) listener = FindAnyObjectByType<AudioListener>();
            float dist = listener ? Vector3.Distance(listener.transform.position, src.transform.position) : 0f;
            float gain = Mathf.Max(0.005f, rolloff.Evaluate(Mathf.Clamp01(dist / src.maxDistance)) * src.volume * AudioListener.volume);
            float inv = 1f / gain;
            src.GetSpectrumData(spec, 0, FFTWindow.BlackmanHarris);
            src.GetSpectrumData(spec2, 1, FFTWindow.BlackmanHarris);
            for (int i = 0; i < spec.Length; i++) spec[i] = (spec[i] + spec2[i]) * 0.5f * inv;
            float rms = 0f;
            for (int ch = 0; ch < 2; ch++)
            {
                src.GetOutputData(wave, ch);
                float acc = 0f;
                for (int i = 0; i < wave.Length; i++) acc += wave[i] * wave[i];
                rms = Mathf.Max(rms, Mathf.Sqrt(acc / wave.Length) * inv);
            }
            peak = Mathf.Max(rms, peak - dt * 0.02f, 0.02f);
            Level = Mathf.Lerp(Level, Mathf.Clamp01(rms / peak), 1f - Mathf.Exp(-dt * 14f));

            // kick drum ≈ 45..200 Hz
            float bass = 0f;
            for (int i = 1; i <= 4; i++) bass += spec[i];
            bassAvg = Mathf.Lerp(bassAvg, bass, 1f - Mathf.Exp(-dt * 1.6f));
            float since = Time.time - lastBeat;
            if (bass > bassAvg * 1.45f && bass > 0.004f && since > 0.26f)
            {
                beatInterval = Mathf.Clamp(Mathf.Lerp(beatInterval, since < 1.2f ? since : beatInterval, 0.25f), 0.3f, 0.9f);
                Beat(Mathf.Clamp01(bass / (bassAvg * 2.5f)));
            }
            else FallbackBeat(beatInterval * 1.6f);
            Kick = Mathf.MoveTowards(Kick, 0f, dt * 3.5f);
        }

        void FallbackBeat(float after)
        {
            if (Time.time - lastBeat > after) Beat(0.5f);
        }

        void Beat(float strength)
        {
            lastBeat = Time.time;
            BeatCount++;
            Kick = Mathf.Max(Kick, 0.55f + 0.45f * strength);
            if (BeatCount % 16 == 0) pattern = rnd.Next(0, 5);
            // short white flashes on strong hits in loud passages (kept rare: at most every other beat)
            if (Level > 0.82f && strength > 0.8f && BeatCount % 2 == 0) strobeT = 0.06f;
            for (int i = 0; i < tileSpark.Length; i++) if (rnd.NextDouble() < 0.12) tileSpark[i] = 1f;
        }

        /// <summary>Additive particle materials multiply by alpha too: keep it at 1 when scaling a colour.</summary>
        static Color Opaque(Color c) { c.a = 1f; return c; }

        public Color Pal(int i) => palette.Length > 0 ? palette[((i % palette.Length) + palette.Length) % palette.Length] : Color.white;

        Color PalLerp(float t)
        {
            if (palette.Length == 0) return Color.white;
            t = Mathf.Repeat(t, palette.Length);
            int a = Mathf.FloorToInt(t);
            return Color.Lerp(Pal(a), Pal(a + 1), t - a);
        }

        // ------------------------------------------------------------------ show

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Listen(dt);
            patternT += dt;
            float t = Time.time;
            float energy = 0.7f + 0.5f * Level;

            // ---- LED floor
            int rows = tileCols > 0 ? Mathf.CeilToInt(tiles.Length / (float)tileCols) : 1;
            float cx = (tileCols - 1) * 0.5f, cy = (rows - 1) * 0.5f;
            for (int i = 0; i < tiles.Length; i++)
            {
                var r = tiles[i];
                if (!r) continue;
                int x = i % Mathf.Max(1, tileCols), y = i / Mathf.Max(1, tileCols);
                tileSpark[i] = Mathf.MoveTowards(tileSpark[i], 0f, dt * 2.5f);
                Color c; float b;
                switch (pattern)
                {
                    case 0: // checkerboard swapping on the beat
                        c = Pal(((x + y) & 1) + BeatCount); b = 0.35f + Kick; break;
                    case 1: // rings from the centre
                    {
                        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                        float w = Mathf.Repeat(d * 0.45f - t * 1.8f, 1f);
                        c = PalLerp(d * 0.3f + BeatCount * 0.25f); b = Mathf.Pow(w, 3f) * 1.6f + Kick * 0.3f; break;
                    }
                    case 2: // row chase
                        c = Pal(y + BeatCount); b = ((y + BeatCount) % 3 == 0 ? 1.2f : 0.15f) + Kick * 0.3f; break;
                    case 3: // diagonal sweep
                    {
                        float w = Mathf.Repeat((x + y) * 0.18f - t * 0.9f, 1f);
                        c = PalLerp((x + y) * 0.15f + t * 0.2f); b = w * w * 1.4f + 0.1f; break;
                    }
                    default: // sparkle
                        c = Pal(i * 7 + BeatCount); b = 0.08f + tileSpark[i] * 1.6f; break;
                }
                b *= energy;
                mpb.SetColor(BaseColor, Color.Lerp(new Color(0.05f, 0.05f, 0.06f), c, 0.35f));
                mpb.SetColor(EmissionColor, c * (0.5f + b * 5f));
                r.SetPropertyBlock(mpb);
            }

            // ---- moving heads: lazy figure-eights, faster with the music, colour per bar
            for (int i = 0; i < movingHeads.Length; i++)
            {
                var h = movingHeads[i];
                if (!h) continue;
                float ph = i * 1.37f;
                float speed = 0.35f + Level * 0.5f;
                float pan = Mathf.Sin(t * speed + ph) * 55f;
                float tilt = 35f + Mathf.Sin(t * speed * 2f + ph * 0.7f) * 25f;
                // rest pose looks down (+Z of the head = beam direction)
                h.localRotation = Quaternion.Euler(0f, pan, 0f) * Quaternion.Euler(90f - tilt, 0f, 0f);
                Color c = Pal(i + BeatCount / 4);
                float inten = 0.8f + Kick * 0.7f;
                if (headLights[i]) { headLights[i].color = c; headLights[i].intensity = 30f * inten; }
                if (beams[i]) { mpb.Clear(); mpb.SetColor(BaseColor, Opaque(c * (0.5f * inten))); beams[i].SetPropertyBlock(mpb); }
            }

            // ---- laser fans
            for (int i = 0; i < lasers.Length; i++)
            {
                if (!lasers[i]) continue;
                var set = laserBeams[i];
                Color c = Pal(i * 2 + BeatCount / 8 + 1);
                float spread = 18f + Mathf.Sin(t * 0.7f + i) * 14f;
                bool on = (BeatCount / 16) % 4 != 3 || Level > 0.6f;
                for (int k = 0; k < set.Length; k++)
                {
                    if (!set[k]) continue;
                    float off = (k - (set.Length - 1) * 0.5f) * spread / set.Length;
                    set[k].localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f + i) * 10f - 4f, off + Mathf.Sin(t * 0.5f + i * 2f) * 25f, 0f);
                    var rr = laserRends[i][k];
                    rr.enabled = on;
                    mpb.Clear(); mpb.SetColor(BaseColor, Opaque(c * (3f + Kick * 3f)));
                    rr.SetPropertyBlock(mpb);
                }
            }

            // ---- neon strips breathe with the bass
            for (int i = 0; i < neon.Length; i++)
            {
                var r = neon[i];
                if (!r) continue;
                Color c = Pal(i + BeatCount / 16);
                mpb.Clear();
                mpb.SetColor(BaseColor, c);
                mpb.SetColor(EmissionColor, c * (2.5f + Kick * 4f) * energy);
                r.SetPropertyBlock(mpb);
            }

            // ---- colour washes
            for (int i = 0; i < washes.Length; i++)
            {
                var l = washes[i];
                if (!l) continue;
                l.color = Color.Lerp(l.color, Pal(i + BeatCount / 2), 1f - Mathf.Exp(-dt * 6f));
                l.intensity = 7f + Kick * 6f * energy;
            }

            if (mirrorBall) mirrorBall.Rotate(0f, 25f * dt, 0f, Space.World);

            if (strobeT > 0f) { strobeT -= dt; strobe.intensity = 40f; }
            else strobe.intensity = 0f;

            UpdateLed(t);
        }

        void UpdateLed(float t)
        {
            if (!ledTex) return;
            const int W = 48, H = 20;
            for (int x = 0; x < W; x++)
            {
                // log-spaced spectrum bars, mirrored from the centre
                int bx = Mathf.Abs(x - W / 2);
                int bin = Mathf.Clamp(Mathf.RoundToInt(Mathf.Pow(bx / (W * 0.5f), 2f) * 120f) + 1, 1, spec.Length - 1);
                float v = Mathf.Clamp01(Mathf.Sqrt(spec[bin] * 40f) * (0.6f + Level));
                int hgt = Mathf.RoundToInt(v * H);
                Color col = PalLerp(bx * 0.08f + t * 0.3f);
                for (int y = 0; y < H; y++)
                {
                    bool lit = Mathf.Abs(y - H / 2) * 2 < hgt;
                    float bg = 0.04f + 0.04f * Mathf.Sin(t * 2f + x * 0.4f + y * 0.3f);
                    Color c = lit ? col * (0.6f + 0.4f * Kick) : Pal(BeatCount / 8) * bg;
                    ledPx[y * W + x] = c;
                }
            }
            ledTex.SetPixels32(ledPx);
            ledTex.Apply(false);
        }
    }
}
