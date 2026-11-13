using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// The screamer's face hanging in the night sky over the disco. It always looks at you, its jaw moves with the
    /// music of the main hall and it "talks" (subtitles). Every <see cref="interval"/> seconds it screams and fires
    /// a volley of rockets into random open-sky points of the map — every impact point is telegraphed first by a red
    /// circle (blast radius) with a shrinking countdown ring, a light pillar and beeping, and the HUD warns 3 s
    /// before the volley. Covered places (VIP cabins, the lobby, under the balcony) are safe.
    /// Network: the host picks the targets and sends them (NetGame.SendDiscoVolley); clients fly visual rockets,
    /// the blasts arrive as ordinary Explosion messages.
    /// </summary>
    public class DiscoHead : MonoBehaviour
    {
        public class Marker
        {
            public Vector3 pos;
            public float radius, impactAt, born;
        }

        public static DiscoHead Instance { get; private set; }
        public static readonly List<Marker> Markers = new List<Marker>();

        public float interval = 30f;
        public int rockets = 9;
        public float blastRadius = 6f, damage = 140f, flightTime = 2.6f;
        public float size = 22f;
        [Tooltip("Room whose music moves the jaw (the main hall).")]
        public DiscoRoom voiceRoom;

        float timer, talkT = 6f, subtitleT, warnT, screamT, eyeFlare;
        Transform face, halo, eyeL, eyeR;
        Material faceMat;
        Texture2D[] frames;
        Light glow;
        Vector3 home;
        Text subtitle, warning;
        CanvasGroup subGroup;
        int lastFrame = -1;
        bool warned;
        static Material ringMat, pillarMat, glowMat;

        static readonly string[] Lines =
        {
            "ТАНЦУЙТЕ, СМЕРТНЫЕ.", "Я ВИЖУ КАЖДОГО ИЗ ВАС.", "КТО ИЗ ВАС НАСТОЯЩИЙ?", "НЕ ПРЕКРАЩАЙТЕ ТАНЦЕВАТЬ.",
            "ПРЯЧЬТЕСЬ ПОД КРЫШАМИ. ПОКА МОЖЕТЕ.", "ГРОМЧЕ!", "ТАНЦПОЛ ГОЛОДЕН.", "МАНЕКЕНЫ ТАНЦУЮТ ЛУЧШЕ ВАС.",
            "БАС ПОДНИМАЕТ МЕРТВЫХ.", "ЕЩЁ ОДИН ТРЕК — И НЕБО ЗАГОРИТСЯ.", "ВАШИ ИМЕНА МНЕ НЕ НУЖНЫ.", "ВЫ НЕ ОТЛИЧИТЕ ДРУГА ОТ КУКЛЫ.",
        };
        static readonly string[] VolleyLines = { "НЕБО ГОРИТ!", "ЗАЛП!", "ЛОВИТЕ РИТМ!", "ПОД НОГИ СМОТРИТЕ!" };

        void Awake()
        {
            Instance = this;
            Markers.Clear();
            home = transform.position;
            timer = 0f;
            BuildFace();
            BuildHud();
        }

        void OnDestroy() { if (Instance == this) Instance = null; Markers.Clear(); }

        // ------------------------------------------------------------------ visuals

        static Material GlowMat()
        {
            if (!glowMat) glowMat = FX.MakeParticleMat("HeadGlow", FX.SoftCircle(64, 2f), true, Color.white);
            return glowMat;
        }

        void BuildFace()
        {
            frames = new Texture2D[6];
            for (int i = 0; i < frames.Length; i++) frames[i] = ScreamFace.Make(256, i / (frames.Length - 1f), true);

            halo = MakeQuad("Halo", GlowMat(), new Vector3(0f, 0f, 0.5f), size * 1.9f);
            var hm = new MaterialPropertyBlock(); hm.SetColor("_BaseColor", new Color(0.55f, 0.02f, 0.05f, 1f));
            halo.GetComponent<Renderer>().SetPropertyBlock(hm);

            faceMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "DiscoHeadFace" };
            faceMat.SetTexture("_BaseMap", frames[0]);
            faceMat.SetColor("_BaseColor", new Color(1.1f, 1.05f, 1.05f, 1f));
            faceMat.SetFloat("_AlphaClip", 1f);
            faceMat.SetFloat("_Cutoff", 0.5f);
            faceMat.EnableKeyword("_ALPHATEST_ON");
            faceMat.SetOverrideTag("RenderType", "TransparentCutout");
            faceMat.renderQueue = (int)RenderQueue.AlphaTest;
            face = MakeQuad("Face", faceMat, Vector3.zero, size);

            eyeL = MakeQuad("EyeL", GlowMat(), new Vector3(-0.17f * size, 0.14f * size, -0.05f), size * 0.16f);
            eyeR = MakeQuad("EyeR", GlowMat(), new Vector3(0.17f * size, 0.14f * size, -0.05f), size * 0.16f);

            var lgo = new GameObject("HeadLight");
            lgo.transform.SetParent(transform, false);
            lgo.transform.localPosition = new Vector3(0f, -size * 0.3f, -2f);
            glow = lgo.AddComponent<Light>();
            glow.type = LightType.Point; glow.color = new Color(1f, 0.2f, 0.15f); glow.range = 70f; glow.intensity = 1.5f; glow.shadows = LightShadows.None;
        }

        Transform MakeQuad(string name, Material m, Vector3 localPos, float s)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * s;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void BuildHud()
        {
            var go = new GameObject("DiscoHUD");
            go.transform.SetParent(transform, false);
            UIKit.MakeCanvas(go, 4);
            var root = (RectTransform)go.transform;

            var sgo = new GameObject("Subtitle", typeof(RectTransform), typeof(CanvasGroup));
            sgo.transform.SetParent(root, false);
            subGroup = sgo.GetComponent<CanvasGroup>();
            subGroup.alpha = 0f;
            var srt = (RectTransform)sgo.transform;
            srt.anchorMin = new Vector2(0.15f, 1f); srt.anchorMax = new Vector2(0.85f, 1f); srt.pivot = new Vector2(0.5f, 1f);
            srt.sizeDelta = new Vector2(0f, 60f); srt.anchoredPosition = new Vector2(0f, -112f);
            subtitle = UIKit.Label(srt, "", 30, TextAnchor.MiddleCenter, new Color(1f, 0.82f, 0.78f), true);
            Fill(subtitle.rectTransform);
            var sh = subtitle.gameObject.AddComponent<Outline>(); sh.effectColor = new Color(0f, 0f, 0f, 0.85f); sh.effectDistance = new Vector2(2f, -2f);

            warning = UIKit.Label(root, "", 34, TextAnchor.MiddleCenter, new Color(1f, 0.25f, 0.2f), true);
            var wrt = warning.rectTransform;
            wrt.anchorMin = new Vector2(0.1f, 1f); wrt.anchorMax = new Vector2(0.9f, 1f); wrt.pivot = new Vector2(0.5f, 1f);
            wrt.sizeDelta = new Vector2(0f, 50f); wrt.anchoredPosition = new Vector2(0f, -176f);
            var wo = warning.gameObject.AddComponent<Outline>(); wo.effectColor = new Color(0f, 0f, 0f, 0.9f); wo.effectDistance = new Vector2(2f, -2f);
        }

        static void Fill(RectTransform rt) { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero; }

        void Say(string line, float time = 3.5f)
        {
            subtitle.text = "<color=#ff5a4a>ГОЛОВА:</color> " + line;
            subtitleT = time;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var mm = MatchManager.Instance;
            bool running = (!mm || !mm.Ended) && !SkyArena.Active;

            // ---- volley clock (host / offline)
            if (running && !Net.IsClient)
            {
                timer += dt;
                if (!warned && timer >= interval - 3f)
                {
                    warned = true;
                    LocalWarn();
                    NetGame.SendDiscoWarn();
                }
                if (timer >= interval)
                {
                    timer = 0f;
                    warned = false;
                    FireVolley();
                }
            }

            // ---- talking
            talkT -= dt;
            if (talkT <= 0f && running)
            {
                talkT = Random.Range(9f, 15f);
                if (warnT <= 0f && screamT <= 0f) Say(Lines[Random.Range(0, Lines.Length)]);
            }
            subtitleT -= dt;
            subGroup.alpha = Mathf.MoveTowards(subGroup.alpha, subtitleT > 0f ? 1f : 0f, dt * 3f);

            warnT -= dt;
            if (warnT > 0f)
            {
                warning.text = "⚠ ГОЛОВА ГОТОВИТ ЗАЛП — УХОДИТЕ ИЗ КРАСНЫХ КРУГОВ ИЛИ ПРЯЧЬТЕСЬ ПОД КРЫШЕЙ";
                warning.color = new Color(1f, 0.25f, 0.2f, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 7f)));
            }
            else if (Markers.Count > 0)
            {
                warning.text = "⚠ РАКЕТЫ В ВОЗДУХЕ";
                warning.color = new Color(1f, 0.35f, 0.25f, 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 10f)));
            }
            else warning.text = "";

            // ---- face: look at the camera, float, jaw on the music
            var cam = Camera.main;
            screamT -= dt;
            eyeFlare = Mathf.MoveTowards(eyeFlare, 0f, dt * 0.6f);
            float level = voiceRoom ? voiceRoom.Level : 0.4f;
            float kick = voiceRoom ? voiceRoom.Kick : 0f;
            float speaking = subtitleT > 0f ? 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.time * 9f) * Mathf.Sin(Time.time * 3.1f)) : 0f;
            float mouth = screamT > 0f ? 1f : Mathf.Clamp01(Mathf.Max(level * 0.75f * (0.4f + kick), speaking * 0.8f));
            int frame = Mathf.Clamp(Mathf.RoundToInt(mouth * (frames.Length - 1)), 0, frames.Length - 1);
            if (frame != lastFrame) { lastFrame = frame; faceMat.SetTexture("_BaseMap", frames[frame]); }

            float t = Time.time;
            Vector3 drift = new Vector3(Mathf.Sin(t * 0.13f) * 5f, Mathf.Sin(t * 0.31f) * 1.2f, Mathf.Cos(t * 0.11f) * 4f);
            Vector3 shake = screamT > 0f || warnT > 0f ? Random.insideUnitSphere * 0.5f : Vector3.zero;
            transform.position = home + drift + shake;
            if (cam)
            {
                Vector3 away = transform.position - cam.transform.position;
                if (away.sqrMagnitude > 0.01f)
                    transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.7f) * 4f);
            }
            float pulse = 1f + kick * 0.04f + (screamT > 0f ? 0.08f : 0f);
            transform.localScale = Vector3.one * pulse;

            float eye = 0.6f + level * 0.6f + eyeFlare * 2.5f;
            var eb = new MaterialPropertyBlock();
            eb.SetColor("_BaseColor", new Color(1f * eye, 0.12f * eye, 0.05f * eye, 1f));
            eyeL.GetComponent<Renderer>().SetPropertyBlock(eb);
            eyeR.GetComponent<Renderer>().SetPropertyBlock(eb);
            eyeL.localScale = eyeR.localScale = Vector3.one * size * (0.13f + 0.05f * eye);
            glow.intensity = 1.2f + kick * 1.5f + eyeFlare * 6f;
        }

        // ------------------------------------------------------------------ volley

        /// <summary>3 s before the volley (also on clients, from the host's message).</summary>
        public void LocalWarn()
        {
            warnT = 3f;
            eyeFlare = 1f;
            Say(VolleyLines[Random.Range(0, VolleyLines.Length)], 3f);
        }

        /// <summary>Volley right now (tests, host only).</summary>
        public void FireNow() { if (!Net.IsClient) { timer = 0f; warned = false; FireVolley(); } }

        Vector3 Mouth => transform.position + transform.up * (-0.25f * size) - transform.forward * 0.5f;

        void FireVolley()
        {
            var targets = new List<Vector3>();
            var alive = new List<Combatant>();
            foreach (var c in Combatant.All) if (c && c.alive) alive.Add(c);
            var club = DiscoClub.Instance;
            Vector2 mn = club ? club.areaMin : new Vector2(-40f, -28f), mx = club ? club.areaMax : new Vector2(40f, 28f);
            for (int attempt = 0; attempt < rockets * 25 && targets.Count < rockets; attempt++)
            {
                Vector3 p;
                // about a third of the rockets go near somebody — the rest anywhere under the open sky
                if (alive.Count > 0 && targets.Count < rockets / 3)
                {
                    var c = alive[Random.Range(0, alive.Count)];
                    Vector2 o = Random.insideUnitCircle * 5f;
                    p = c.transform.position + new Vector3(o.x, 0f, o.y);
                }
                else p = new Vector3(Random.Range(mn.x, mx.x), 0f, Random.Range(mn.y, mx.y));
                if (!NavMesh.SamplePosition(p, out var nh, 3f, NavMesh.AllAreas)) continue;
                if (!OpenSky(nh.position, out var ground)) continue;
                bool close = false;
                foreach (var q in targets) if ((q - ground).sqrMagnitude < 4.5f * 4.5f) { close = true; break; }
                if (close) continue;
                targets.Add(ground);
            }
            if (targets.Count == 0) return;
            var delays = new float[targets.Count];
            for (int i = 0; i < delays.Length; i++) delays[i] = 0.6f + i * 0.17f + Random.Range(0f, 0.1f);
            var arr = targets.ToArray();
            StartVolley(arr, delays, false);
            NetGame.SendDiscoVolley(arr, delays);
        }

        /// <summary>Nothing above the point up to the sky (roofs, balconies and cabin canopies shelter).</summary>
        static bool OpenSky(Vector3 p, out Vector3 ground)
        {
            ground = p;
            if (!Physics.Raycast(new Vector3(p.x, p.y + 80f, p.z), Vector3.down, out var h, 90f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            if (h.point.y > p.y + 0.6f || h.collider.GetComponentInParent<DiscoDancer>()) return false;
            ground = h.point;
            return true;
        }

        public void StartVolley(Vector3[] targets, float[] delays, bool visualOnly)
        {
            if (targets == null || delays == null) return;
            screamT = 1.6f;
            eyeFlare = 1f;
            warnT = 0f;
            ProcAudio.Play(Sfx.Scream, 0.55f, 0.02f);
            for (int i = 0; i < targets.Length && i < delays.Length; i++)
                StartCoroutine(Rocket(targets[i], delays[i], visualOnly));
        }

        IEnumerator Rocket(Vector3 target, float delay, bool visualOnly)
        {
            var m = new Marker { pos = target, radius = blastRadius, born = Time.time, impactAt = Time.time + delay + flightTime };
            Markers.Add(m);
            var mk = RocketMarker.Create(m);
            yield return new WaitForSeconds(delay);
            var r = HeadRocket.Create(Mouth, target, flightTime, size * 0.18f);
            while (r && !r.Arrived) yield return null;
            Markers.Remove(m);
            if (mk) Destroy(mk.gameObject);
            if (!visualOnly) Explosions.Detonate(target + Vector3.up * 0.3f, blastRadius, damage, 12f, null, DiscoClub.RocketWeapon);
            if (r) Destroy(r.gameObject);
        }

        // ------------------------------------------------------------------ bots

        /// <summary>A bot standing in a marked circle runs out of it. Returns true while it is fleeing.</summary>
        public static bool BotDodge(Transform bot, NavMeshAgent agent)
        {
            if (Markers.Count == 0 || !agent || !agent.enabled || !agent.isOnNavMesh) return false;
            Vector3 p = bot.position;
            foreach (var m in Markers)
            {
                Vector3 d = p - m.pos; d.y = 0f;
                float r = m.radius + 1.5f;
                if (d.sqrMagnitude > r * r) continue;
                Vector3 dir = d.sqrMagnitude > 0.01f ? d.normalized : Random.insideUnitSphere;
                dir.y = 0f;
                Vector3 dest = m.pos + dir.normalized * (m.radius + 4f);
                if (!agent.hasPath || (agent.destination - dest).sqrMagnitude > 4f)
                    if (NavMesh.SamplePosition(dest, out var nh, 4f, NavMesh.AllAreas)) agent.SetDestination(nh.position);
                agent.isStopped = false;
                agent.speed = 6.5f;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ shared materials

        internal static Material RingMat()
        {
            if (ringMat) return ringMat;
            const int N = 256;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "HeadTargetRing" };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = 0f;
                    a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.95f) / 0.035f));           // outer edge = blast radius
                    a = Mathf.Max(a, Mathf.Clamp01(1f - Mathf.Abs(r - 0.6f) / 0.015f) * 0.6f);
                    if (r < 0.95f) a = Mathf.Max(a, 0.14f + 0.1f * Mathf.Clamp01(Mathf.Sin(r * 40f)));  // filled hazard zone
                    if ((Mathf.Abs(dx) < 0.012f || Mathf.Abs(dy) < 0.012f) && r < 0.95f && r > 0.12f) a = Mathf.Max(a, 0.8f);
                    if (r > 1f) a = 0f;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            ringMat = FX.MakeParticleMat("HeadTargetRing", t, true, Color.white, true);
            return ringMat;
        }

        internal static Material PillarMat()
        {
            if (pillarMat) return pillarMat;
            const int W = 8, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "HeadPillar" };
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float a = Mathf.Pow(1f - y / (H - 1f), 1.5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            pillarMat = FX.MakeParticleMat("HeadPillar", t, true, Color.white, true);
            return pillarMat;
        }
    }

    /// <summary>Telegraph of one impact point: blast-radius circle, a ring closing in until impact, a light pillar, a red light, beeps.</summary>
    public class RocketMarker : MonoBehaviour
    {
        DiscoHead.Marker m;
        Transform ring, closing, pillar;
        Renderer ringR, closingR, pillarR;
        Light lamp;
        float beepT;
        MaterialPropertyBlock mpb;

        public static RocketMarker Create(DiscoHead.Marker m)
        {
            var go = new GameObject("RocketMarker");
            go.transform.position = m.pos + Vector3.up * 0.06f;
            var mk = go.AddComponent<RocketMarker>();
            mk.m = m;
            mk.mpb = new MaterialPropertyBlock();
            mk.ring = mk.Flat("Ring", m.radius * 2f, out mk.ringR);
            mk.closing = mk.Flat("Closing", m.radius * 2f, out mk.closingR);
            mk.closing.localPosition = Vector3.up * 0.02f;

            var p = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(p.GetComponent<Collider>());
            p.name = "Pillar";
            p.transform.SetParent(go.transform, false);
            p.transform.localPosition = Vector3.up * 20f;
            p.transform.localScale = new Vector3(0.5f, 20f, 0.5f);
            mk.pillar = p.transform;
            mk.pillarR = p.GetComponent<Renderer>();
            mk.pillarR.sharedMaterial = DiscoHead.PillarMat();
            mk.pillarR.shadowCastingMode = ShadowCastingMode.Off;

            var lgo = new GameObject("Lamp");
            lgo.transform.SetParent(go.transform, false);
            lgo.transform.localPosition = Vector3.up * 1.2f;
            mk.lamp = lgo.AddComponent<Light>();
            mk.lamp.type = LightType.Point; mk.lamp.color = new Color(1f, 0.1f, 0.05f); mk.lamp.range = m.radius * 1.8f; mk.lamp.shadows = LightShadows.None;
            ProcAudio.PlayAt(Sfx.ErrorTone, go.transform.position + Vector3.up, 0.5f, 0.02f);
            return mk;
        }

        Transform Flat(string name, float s, out Renderer r)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = name;
            q.transform.SetParent(transform, false);
            q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = Vector3.one * s;
            r = q.GetComponent<Renderer>();
            r.sharedMaterial = DiscoHead.RingMat();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return q.transform;
        }

        void Update()
        {
            float total = Mathf.Max(0.1f, m.impactAt - m.born);
            float left = Mathf.Max(0f, m.impactAt - Time.time);
            float k = 1f - left / total;                       // 0 → 1 at impact
            float blink = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.Lerp(5f, 22f, k)));

            mpb.SetColor("_BaseColor", new Color(1.6f * blink, 0.12f * blink, 0.06f * blink, 1f));
            ringR.SetPropertyBlock(mpb);
            pillarR.SetPropertyBlock(mpb);
            closing.localScale = Vector3.one * Mathf.Max(0.3f, m.radius * 2f * (1f - k));
            mpb.SetColor("_BaseColor", new Color(2.2f, 0.5f, 0.2f, 1f));
            closingR.SetPropertyBlock(mpb);
            pillar.localScale = new Vector3(0.35f + k * 0.5f, 20f, 0.35f + k * 0.5f);
            lamp.intensity = (2f + 6f * k) * blink;
            ring.Rotate(0f, 0f, 40f * Time.deltaTime, Space.Self);

            beepT -= Time.deltaTime;
            if (beepT <= 0f)
            {
                beepT = Mathf.Lerp(0.7f, 0.12f, k);
                ProcAudio.PlayAt(Sfx.Click, transform.position + Vector3.up, 0.8f, 0f);
            }
        }
    }

    /// <summary>A rocket from the head's mouth: arcs to its target in a fixed time, smokes, whistles near the end.</summary>
    public class HeadRocket : MonoBehaviour
    {
        Vector3 a, b, c;
        float t, duration;
        Transform flame;
        bool whistled;
        public bool Arrived { get; private set; }

        public static HeadRocket Create(Vector3 from, Vector3 to, float duration, float scale)
        {
            var go = new GameObject("HeadRocket");
            var r = go.AddComponent<HeadRocket>();
            var model = AbilityProps.BuildRocket(go.transform, out r.flame);
            model.localScale = Vector3.one * Mathf.Max(1f, scale);
            r.a = from; r.c = to;
            r.b = (from + to) * 0.5f + Vector3.up * (8f + Vector3.Distance(from, to) * 0.15f) + Random.insideUnitSphere * 4f;
            r.duration = duration;
            go.transform.position = from;
            var lgo = new GameObject("RocketLight");
            lgo.transform.SetParent(go.transform, false);
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(1f, 0.55f, 0.25f); l.range = 9f; l.intensity = 5f; l.shadows = LightShadows.None;
            ProcAudio.PlayAt(Sfx.RocketLaunch, from, 1f, 0.08f);
            return r;
        }

        Vector3 At(float u) { float iu = 1f - u; return iu * iu * a + 2f * iu * u * b + u * u * c; }

        float puffT;

        void Update()
        {
            if (Arrived) return;
            float dt = Time.deltaTime;
            t += dt / duration;
            // eases in: slow out of the mouth, fast at the end
            float u = Mathf.Clamp01(t * t * 0.4f + t * 0.6f);
            Vector3 p = At(u), next = At(Mathf.Min(1f, u + 0.02f));
            transform.position = p;
            if ((next - p).sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(next - p);
            if (flame) flame.localScale = new Vector3(0.14f, 0.14f, 0.3f) * Random.Range(0.8f, 1.4f);
            puffT -= dt;
            if (puffT <= 0f)
            {
                puffT = 0.03f;
                FX.Ensure().Puff(p - transform.forward * 0.6f, -transform.forward, 1, 0.3f, new Vector2(0.1f, 0.6f), new Vector2(0.6f, 1.2f), new Vector2(0.8f, 1.6f),
                                 new Color(0.6f, 0.55f, 0.55f, 0.45f), new Color(0.35f, 0.3f, 0.3f, 0.35f));
            }
            if (!whistled && t > 0.75f) { whistled = true; ProcAudio.PlayAt(Sfx.Whiz, c + Vector3.up * 3f, 1f, 0.05f); }
            if (t >= 1f) Arrived = true;
        }
    }
}
