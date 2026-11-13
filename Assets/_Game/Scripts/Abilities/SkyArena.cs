using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// One-shot ability "Небесный тир": everybody except the caster is thrown onto a flat neon arena high above
    /// the map under a black sky. There they can only run and knife each other, respawn instantly and move
    /// twice as fast. The caster hangs above the arena and sees nothing but a crosshair: mouse — aim,
    /// WASD — move over the arena, Space / Ctrl — height, LMB — machine gun. After the time is up everyone
    /// goes back to where they stood.
    ///
    /// Network: the caster's AbilityUser broadcasts AbilityNetEvent.Arena; every machine runs Begin() and
    /// moves only the combatants it simulates (its own player, the host's bots). Hits on remote runners go
    /// through the usual NetProxy damage forwarding, tracers through Msg.Shot (NetProxy draws them from the sky).
    /// </summary>
    [DefaultExecutionOrder(70)]
    public class SkyArena : MonoBehaviour
    {
        public static SkyArena Instance { get; private set; }

        public const float SpeedMul = 2f;
        public const string WeaponName = "Небесный пулемёт";
        /// <summary>Arena floor center: far above every map, nothing of the level is in view.</summary>
        public static readonly Vector3 Center = new Vector3(0f, 1500f, 0f);
        const float Half = 30f, WallHeight = 6f;
        const float FireRate = 13f, Damage = 26f, Spread = 0.6f;
        const float MinHeight = 18f, MaxHeight = 60f, StartHeight = 34f;

        static readonly Color SkyColor = new Color(0.012f, 0.01f, 0.03f);
        static readonly Color Neon = new Color(0.85f, 0.15f, 1f);

        public static bool Active => Instance && Instance.active;
        public static float SpeedScale => Active ? SpeedMul : 1f;
        public static bool IsGunner(Combatant c) => Active && c && c == Instance.caster;
        /// <summary>A runner: only legs and a knife while the arena lasts.</summary>
        public static bool KnifeOnly(Combatant c) => Active && c && c != Instance.caster;
        public static bool LocalRunner => Active && PlayerHealth.Local && KnifeOnly(PlayerHealth.Local.GetComponent<Combatant>());
        public static bool OnPlane(Vector3 p) => Mathf.Abs(p.y - Center.y) < 25f && Mathf.Abs(p.x - Center.x) < Half + 4f && Mathf.Abs(p.z - Center.z) < Half + 4f;
        /// <summary>1 → 0 while the arena lasts.</summary>
        public static float Fraction => Active ? Mathf.Clamp01(Instance.left / Mathf.Max(0.01f, Instance.total)) : 0f;

        bool active;
        float left, total = 1f;
        Combatant caster;
        GameObject root;
        readonly Dictionary<Combatant, (Vector3 pos, float yaw)> back = new Dictionary<Combatant, (Vector3, float)>();
        readonly RaycastHit[] hits = new RaycastHit[24];

        // sky / lighting backup
        Material savedSky;
        bool savedFog;
        Color savedFogColor, savedAmbient;
        FogMode savedFogMode;
        float savedFogStart, savedFogEnd, savedFogDensity, savedAmbientIntensity;
        AmbientMode savedAmbientMode;
        readonly List<(Camera cam, CameraClearFlags flags, Color bg)> savedCams = new List<(Camera, CameraClearFlags, Color)>();

        // local player as gunner
        bool localGunner;
        PlayerHealth gunnerPH;
        Camera skyCam;
        Vector3 camPos;
        float yaw, pitch = 75f, fireCd, hitFlash, kick;
        readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        // bot as gunner
        CombatBot gunnerBot;
        Combatant botTarget;
        Vector3 botAim;
        float botRetarget, botFireCd, botPause;

        // UI
        GameObject uiRoot;
        RectTransform crossRoot;
        Image[] crossBars;
        Text timerText;

        // ------------------------------------------------------------------ start / stop

        static SkyArena Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("SkyArena");
            return go.AddComponent<SkyArena>();
        }

        void Awake() => Instance = this;

        /// <summary>Caster side (the machine that simulates the caster). False if nothing would happen.</summary>
        public static bool TryBegin(Combatant who, float duration)
        {
            if (!who || !who.alive || Active || !MatchManager.Instance || MatchManager.Instance.Ended) return false;
            bool anyone = false;
            foreach (var c in Combatant.All) if (c && c != who && who.IsEnemyOf(c)) { anyone = true; break; }
            if (!anyone) return false;
            Begin(who, duration);
            return true;
        }

        /// <summary>Runs on every machine (locally on the caster's, from the network event elsewhere).</summary>
        public static void Begin(Combatant who, float duration)
        {
            if (!who || Active) return;
            Ensure().StartArena(who, duration);
        }

        public static void Stop()
        {
            if (Active) Instance.EndArena();
        }

        void StartArena(Combatant who, float duration)
        {
            active = true;
            caster = who;
            left = total = Mathf.Max(1f, duration);
            back.Clear();
            BuildArena();
            ApplySky(true);

            var list = new List<Combatant>(Combatant.All);
            foreach (var c in list)
            {
                if (!c || c.remote) continue;
                if (c == who) BecomeGunner(c);
                else SendToArena(c);
            }

            ProcAudio.Play(Sfx.Warp, 0.8f, 0f);
            ProcAudio.Play(Sfx.Scream, 0.25f, 0.05f);
            var ph = PlayerHealth.Local;
            if (ph && !localGunner)
            {
                var au = ph.GetComponent<AbilityUser>();
                if (au) au.Notify("НЕБЕСНЫЙ ТИР!", $"{(who.displayName ?? "Кто-то")} стреляет сверху. Бегите и режьте друг друга!", Neon);
                if (ph.playerCam) ph.playerCam.Shake(0.6f);
            }
        }

        void EndArena()
        {
            if (!active) return;
            active = false;
            ApplySky(false);

            if (localGunner) LeaveGunner();
            if (gunnerBot) { gunnerBot.ExternalControl = false; gunnerBot = null; }
            botTarget = null;

            var list = new List<Combatant>(Combatant.All);
            foreach (var c in list)
            {
                if (!c || c.remote || c == caster || !c.alive) continue;
                Vector3 pos; float y;
                if (back.TryGetValue(c, out var b)) { pos = b.pos; y = b.yaw; }
                else if (MatchManager.Instance && MatchManager.Instance.GetSpawn(c, out var p, out var r)) { pos = p; y = r.eulerAngles.y; }
                else continue;
                if (c.abilities) c.abilities.ClearStatus();
                Place(c, pos, y);
            }
            back.Clear();
            caster = null;
            if (root) root.SetActive(false);
            if (uiRoot) uiRoot.SetActive(false);

            var ph = PlayerHealth.Local;
            if (ph)
            {
                var au = ph.GetComponent<AbilityUser>();
                if (au) au.Notify("Тир закрыт", "Все вернулись на свои места", Neon);
            }
            ProcAudio.Play(Sfx.Warp, 0.6f, 0f);
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            if (active)
            {
                ApplySky(false);
                if (localGunner) GameInput.Piloting = false;
            }
            Instance = null;
        }

        // ------------------------------------------------------------------ moving people

        void SendToArena(Combatant c)
        {
            if (!c.alive) return; // dead ones respawn on the arena (MatchManager.GetSpawn)
            back[c] = (c.transform.position, c.transform.eulerAngles.y);
            if (c.abilities) c.abilities.ClearStatus();
            Vector3 p = SpawnPoint(c, out float y);
            Place(c, p, y);
            FX.Ensure().Puff(p + Vector3.up, Vector3.up, 10, 1f, new Vector2(0.5f, 2.5f), new Vector2(0.5f, 1.1f), new Vector2(0.5f, 1f),
                             new Color(0.8f, 0.3f, 1f, 0.5f), new Color(0.4f, 0.1f, 0.6f, 0.4f));
        }

        static void Place(Combatant c, Vector3 pos, float yaw)
        {
            var ph = c.GetComponent<PlayerHealth>();
            if (ph)
            {
                if (ph.motor.IsPulled) ph.motor.StopPull();
                ph.motor.Teleport(pos + Vector3.up * 0.05f);
                ph.playerCam.SetLook(yaw, 0f);
                return;
            }
            var bot = c.GetComponent<CombatBot>();
            if (bot) bot.ArenaTeleport(pos, Quaternion.Euler(0f, yaw, 0f));
        }

        /// <summary>Random arena point far from the other runners, facing the center.</summary>
        public Vector3 SpawnPoint(Combatant who, out float faceYaw)
        {
            Vector3 best = Center;
            float bestScore = float.MinValue;
            for (int i = 0; i < 10; i++)
            {
                Vector3 p = Center + new Vector3(Random.Range(-Half + 3f, Half - 3f), 0f, Random.Range(-Half + 3f, Half - 3f));
                float minD = 30f;
                foreach (var c in Combatant.All)
                {
                    if (!c || c == who || !c.alive || c == caster || !OnPlane(c.transform.position)) continue;
                    minD = Mathf.Min(minD, Vector3.Distance(c.transform.position, p));
                }
                float s = minD + Random.value * 3f;
                if (s > bestScore) { bestScore = s; best = p; }
            }
            if (NavMesh.SamplePosition(best, out var nh, 2f, NavMesh.AllAreas)) best = nh.position;
            Vector3 to = Center - best; to.y = 0f;
            faceYaw = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to).eulerAngles.y : Random.Range(0f, 360f);
            return best;
        }

        public static Vector3 RandomPoint() => Center + new Vector3(Random.Range(-Half + 2f, Half - 2f), 0f, Random.Range(-Half + 2f, Half - 2f));

        /// <summary>Closest living enemy that is on the arena (for runner bots).</summary>
        public static Combatant NearestRunner(Combatant self, Vector3 from)
        {
            Combatant best = null;
            float bd = float.MaxValue;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || c == self || IsGunner(c) || !self.IsEnemyOf(c) || !OnPlane(c.transform.position)) continue;
                float d = (c.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // ------------------------------------------------------------------ gunner

        void BecomeGunner(Combatant c)
        {
            if (c.abilities) c.abilities.ClearStatus();
            var ph = c.GetComponent<PlayerHealth>();
            if (ph && ph == PlayerHealth.Local)
            {
                localGunner = true;
                gunnerPH = ph;
                ph.motor.Locked = true;
                GameInput.Piloting = true; // normal controls off, raw keys read through GameInput.DroneKeys
                camPos = Center + Vector3.up * StartHeight;
                yaw = ph.playerCam.Yaw;
                pitch = 75f;
                fireCd = 0f;
                EnsureSkyCam();
                var pc = ph.playerCam.cam;
                if (pc)
                {
                    pc.enabled = false;
                    var al = pc.GetComponent<AudioListener>();
                    if (al) al.enabled = false;
                    pc.tag = "Untagged";
                }
                skyCam.gameObject.SetActive(true);
                skyCam.tag = "MainCamera";
                HideHud(true);
                EnsureUI();
                uiRoot.SetActive(true);
                if (crossRoot) crossRoot.gameObject.SetActive(true);
                if (timerText) timerText.gameObject.SetActive(false);
                ProcAudio.Play(Sfx.BoltPull, 0.8f, 0f);
                return;
            }
            var bot = c.GetComponent<CombatBot>();
            if (bot)
            {
                gunnerBot = bot;
                bot.ExternalControl = true; // the body freezes where it stood
                botAim = Center;
                botTarget = null;
                botRetarget = 0f;
                botPause = 0.8f;
            }
        }

        void LeaveGunner()
        {
            localGunner = false;
            GameInput.Piloting = false;
            if (skyCam) { skyCam.tag = "Untagged"; skyCam.gameObject.SetActive(false); }
            if (gunnerPH)
            {
                var pc = gunnerPH.playerCam.cam;
                if (pc)
                {
                    pc.enabled = true;
                    var al = pc.GetComponent<AudioListener>();
                    if (al) al.enabled = true;
                    pc.tag = "MainCamera";
                }
                gunnerPH.motor.Locked = false;
            }
            gunnerPH = null;
            HideHud(false);
        }

        void EnsureSkyCam()
        {
            if (skyCam) return;
            var go = new GameObject("SkyArenaCam");
            go.transform.SetParent(transform, false);
            skyCam = go.AddComponent<Camera>();
            skyCam.fieldOfView = 62f;
            skyCam.nearClipPlane = 0.1f;
            skyCam.farClipPlane = 400f;
            skyCam.clearFlags = CameraClearFlags.SolidColor;
            skyCam.backgroundColor = SkyColor;
            var acd = go.AddComponent<UniversalAdditionalCameraData>();
            acd.renderPostProcessing = true;
            acd.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            go.AddComponent<AudioListener>();
            go.SetActive(false);
        }

        void HideHud(bool hide)
        {
            if (hide)
            {
                hiddenCanvases.Clear();
                Hide(HUD.Instance ? HUD.Instance.GetComponent<Canvas>() : null);
                Hide(MatchHUD.Instance ? MatchHUD.Instance.GetComponent<Canvas>() : null);
                Hide(AbilityHUD.Instance ? AbilityHUD.Instance.GetComponent<Canvas>() : null);
            }
            else
            {
                foreach (var c in hiddenCanvases) if (c) c.enabled = true;
                hiddenCanvases.Clear();
            }
        }

        void Hide(Canvas c)
        {
            if (!c || !c.enabled) return;
            c.enabled = false;
            hiddenCanvases.Add(c);
        }

        void UpdateLocalGunner(float dt)
        {
            Vector2 look = GameInput.DroneKeys.Look;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, 30f, 89.5f);
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector2 mv = GameInput.DroneKeys.Cyclic;
            float speed = 10f * SpeedMul * (GameInput.DroneKeys.SportHeld ? 1.6f : 1f);
            camPos += yawRot * new Vector3(mv.x, 0f, mv.y) * speed * dt;
            camPos.y += GameInput.DroneKeys.Throttle * 12f * dt;
            camPos.x = Mathf.Clamp(camPos.x, Center.x - Half, Center.x + Half);
            camPos.z = Mathf.Clamp(camPos.z, Center.z - Half, Center.z + Half);
            camPos.y = Mathf.Clamp(camPos.y, Center.y + MinHeight, Center.y + MaxHeight);

            bool fire = GameInput.DroneKeys.FireHeld;
            if (fire)
            {
                fireCd -= dt;
                int guard = 0;
                while (fireCd <= 0f && guard++ < 4)
                {
                    fireCd += 1f / FireRate;
                    Vector3 dir = skyCam ? skyCam.transform.forward : Vector3.down;
                    FireShot(skyCam.transform.position, dir, caster, true);
                }
            }
            else fireCd = Mathf.Max(0f, fireCd - dt);

            kick = Mathf.MoveTowards(kick, 0f, dt * 6f);
            Vector2 shake = kick > 0f ? Random.insideUnitCircle * kick * 0.35f : Vector2.zero;
            skyCam.transform.SetPositionAndRotation(camPos, Quaternion.Euler(pitch + shake.y - kick * 0.6f, yaw + shake.x, 0f));

            hitFlash = Mathf.MoveTowards(hitFlash, 0f, dt * 5f);
            UpdateCrosshair(fire);
        }

        void UpdateBotGunner(float dt)
        {
            if (!gunnerBot || !caster.alive) return;
            Vector3 origin = Center + Vector3.up * StartHeight;
            botPause -= dt;
            botRetarget -= dt;
            if (botRetarget <= 0f || !botTarget || !botTarget.alive || !OnPlane(botTarget.transform.position))
            {
                botRetarget = Random.Range(2.5f, 4.5f);
                var runners = new List<Combatant>();
                foreach (var c in Combatant.All)
                    if (c && c.alive && c != caster && caster.IsEnemyOf(c) && OnPlane(c.transform.position)) runners.Add(c);
                var prev = botTarget;
                botTarget = runners.Count > 0 ? runners[Random.Range(0, runners.Count)] : null;
                if (botTarget != prev) botPause = Random.Range(0.25f, 0.6f);
            }
            if (!botTarget) return;
            // the aim point chases the target: a sprinting runner (x2 speed) can outrun it
            float chase = 8f + gunnerBot.difficulty * 2.5f;
            botAim = Vector3.MoveTowards(botAim, botTarget.Chest, chase * dt);
            botFireCd -= dt;
            if (botPause > 0f || Vector3.Distance(botAim, botTarget.Chest) > 3f) return;
            while (botFireCd <= 0f)
            {
                botFireCd += 1f / (FireRate * 0.75f);
                Vector3 dir = (botAim + Random.insideUnitSphere * 0.6f - origin).normalized;
                FireShot(origin, dir, caster, false);
            }
        }

        void FireShot(Vector3 origin, Vector3 dir, Combatant shooter, bool local)
        {
            Vector2 rnd = Random.insideUnitCircle * Spread;
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 0.001f) side = Vector3.right;
            dir = Quaternion.AngleAxis(rnd.x, Vector3.up) * Quaternion.AngleAxis(rnd.y, side.normalized) * dir;

            Vector3 end = origin + dir * 300f;
            int n = Physics.RaycastNonAlloc(origin, dir, hits, 300f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit hit = default; float best = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (shooter && hits[i].collider.transform.IsChildOf(shooter.transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; hit = hits[i]; any = true; }
            }
            byte kind = 2;
            if (any)
            {
                end = hit.point;
                var hb = hit.collider.GetComponent<Hitbox>();
                IDamageable d = hb ? hb.Owner : hit.collider.GetComponentInParent<IDamageable>();
                bool body = hit.collider.GetComponentInParent<HumanoidRig>();
                kind = body ? (byte)1 : (byte)0;
                if (d != null)
                {
                    float mult = hb ? Mathf.Min(hb.multiplier, 2f) : 1f;
                    bool crit = hb && hb.critical;
                    if (d is PlayerHealth ph)
                    {
                        crit = hit.point.y > ph.transform.position.y + ph.motor.EyeHeight - 0.2f;
                        mult = crit ? 1.6f : 1f;
                    }
                    var res = d.TakeDamage(new DamageInfo
                    {
                        amount = Mathf.Round(Damage * mult), point = hit.point, normal = hit.normal, direction = dir,
                        critical = crit, attacker = shooter, weapon = WeaponName
                    });
                    if (local && res.applied)
                    {
                        hitFlash = res.killed ? 2f : 1f;
                        if (HUD.Instance) HUD.Instance.RegisterHit(res.killed);
                        ProcAudio.Play(res.killed ? Sfx.Kill : crit ? Sfx.HitCrit : Sfx.Hit, res.killed ? 0.7f : 0.35f, 0.05f);
                    }
                }
                if (body) FX.Ensure().Blood(hit.point, hit.normal, 6);
                else
                {
                    var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                    FX.Ensure().Impact(hit, tag ? tag.kind : SurfaceKind.Metal, hit.rigidbody == null && d == null);
                }
            }

            // the tracer leaves a bit off-center so the gunner can see it fly
            Vector3 from = local && skyCam ? origin + skyCam.transform.right * 0.35f - skyCam.transform.up * 0.3f + dir * 0.6f : origin;
            FX.Ensure().Tracer(from, end);
            if (local)
            {
                ProcAudio.Play(Sfx.Shot, 0.45f, 0.08f);
                kick = Mathf.Min(1f, kick + 0.25f);
            }
            else ProcAudio.PlayAt(Sfx.Shot, end + (origin - end).normalized * 12f, 0.7f, 0.08f);
            if (shooter) shooter.lastShotTime = Time.time;
            NetGame.ShotEnds.Clear();
            NetGame.ShotEnds.Add((end, kind));
            NetGame.SendShot(shooter, origin, NetGame.ShotEnds);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (!active) return;
            float dt = Time.deltaTime;
            if (!caster || (MatchManager.Instance && MatchManager.Instance.Ended)) { EndArena(); return; }
            left -= dt;
            if (left <= 0f) { EndArena(); return; }

            // fell off somehow (knocked through a wall, glitch): straight back onto the floor
            var ph = PlayerHealth.Local;
            if (ph && !localGunner && !ph.IsDead && ph.transform.position.y < Center.y - 12f && ph.transform.position.y > Center.y - 600f)
            {
                Vector3 p = SpawnPoint(ph.GetComponent<Combatant>(), out float y);
                Place(ph.GetComponent<Combatant>(), p, y);
            }

            if (localGunner && skyCam && dt > 0f) UpdateLocalGunner(dt);
            else if (gunnerBot && dt > 0f) UpdateBotGunner(dt);

            if (!localGunner && ph)
            {
                EnsureUI();
                uiRoot.SetActive(true);
                if (crossRoot) crossRoot.gameObject.SetActive(false);
                timerText.gameObject.SetActive(true);
                timerText.text = "НЕБЕСНЫЙ ТИР  " + Mathf.CeilToInt(left);
            }
        }

        // ------------------------------------------------------------------ arena geometry

        void BuildArena()
        {
            if (root) { root.SetActive(true); return; }
            root = new GameObject("SkyArena_Floor");
            root.transform.SetParent(transform, false);
            root.transform.position = Center;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(root.transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(Half * 2f, 1f, Half * 2f);
            floor.GetComponent<Renderer>().sharedMaterial = FloorMat();
            floor.AddComponent<SurfaceTag>().kind = SurfaceKind.Metal;

            var rim = AbilityProps.Mat(Neon, 3f, 0.6f);
            for (int i = 0; i < 4; i++)
            {
                bool x = i < 2;
                float s = i % 2 == 0 ? 1f : -1f;
                Vector3 pos = x ? new Vector3(s * (Half + 0.25f), 0f, 0f) : new Vector3(0f, 0f, s * (Half + 0.25f));
                Vector3 size = x ? new Vector3(0.5f, 1f, Half * 2f + 1f) : new Vector3(Half * 2f + 1f, 1f, 0.5f);

                // glowing rim the runners can see in the dark
                var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "Rim" + i;
                strip.transform.SetParent(root.transform, false);
                strip.transform.localPosition = pos + Vector3.up * 0.08f;
                strip.transform.localScale = new Vector3(size.x, 0.16f, size.z);
                strip.GetComponent<Renderer>().sharedMaterial = rim;
                Destroy(strip.GetComponent<Collider>());

                // invisible wall: stops bodies, not bullets (Ignore Raycast layer)
                var wall = new GameObject("Wall" + i);
                wall.layer = 2;
                wall.transform.SetParent(root.transform, false);
                wall.transform.localPosition = pos + Vector3.up * WallHeight * 0.5f;
                var bc = wall.AddComponent<BoxCollider>();
                bc.size = new Vector3(size.x, WallHeight, size.z);
            }

            // navigation for the runner bots
            var surf = root.AddComponent<NavMeshSurface>();
            surf.collectObjects = CollectObjects.Children;
            surf.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surf.layerMask = ~0;
            var mm = MatchManager.Instance;
            if (mm && mm.navSurface) surf.agentTypeID = mm.navSurface.agentTypeID;
            surf.BuildNavMesh();
        }

        static Material floorMat;

        static Material FloorMat()
        {
            if (floorMat) return floorMat;
            const int N = 256, Cell = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var emi = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color32[N * N];
            var pe = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int gx = x % Cell, gy = y % Cell;
                    float line = Mathf.Max(Mathf.Clamp01(1.6f - Mathf.Min(gx, Cell - gx)), Mathf.Clamp01(1.6f - Mathf.Min(gy, Cell - gy)));
                    float n = Mathf.PerlinNoise(x * 0.07f, y * 0.07f) * 0.02f;
                    byte b = (byte)((0.045f + n) * 255f);
                    px[y * N + x] = line > 0f ? new Color32(90, 40, 120, 255) : new Color32(b, b, (byte)(b + 6), 255);
                    byte e = (byte)(line * 255f);
                    pe[y * N + x] = new Color32(e, e, e, 255);
                }
            tex.SetPixels32(px); tex.Apply(true);
            emi.SetPixels32(pe); emi.Apply(true);
            floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "SkyArenaFloor" };
            floorMat.SetTexture("_BaseMap", tex);
            floorMat.SetColor("_BaseColor", Color.white);
            floorMat.SetTextureScale("_BaseMap", new Vector2(Half * 2f / 16f, Half * 2f / 16f)); // 2 m cells
            floorMat.SetFloat("_Smoothness", 0.75f);
            floorMat.SetFloat("_Metallic", 0.3f);
            floorMat.EnableKeyword("_EMISSION");
            floorMat.SetTexture("_EmissionMap", emi);
            floorMat.SetColor("_EmissionColor", Neon * 1.6f);
            floorMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return floorMat;
        }

        // ------------------------------------------------------------------ dark sky

        void ApplySky(bool on)
        {
            if (on)
            {
                savedSky = RenderSettings.skybox;
                savedFog = RenderSettings.fog; savedFogColor = RenderSettings.fogColor; savedFogMode = RenderSettings.fogMode;
                savedFogStart = RenderSettings.fogStartDistance; savedFogEnd = RenderSettings.fogEndDistance; savedFogDensity = RenderSettings.fogDensity;
                savedAmbientMode = RenderSettings.ambientMode; savedAmbient = RenderSettings.ambientLight; savedAmbientIntensity = RenderSettings.ambientIntensity;

                RenderSettings.skybox = null;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = SkyColor;
                RenderSettings.fogStartDistance = 45f;
                RenderSettings.fogEndDistance = 150f;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.16f, 0.13f, 0.22f);

                savedCams.Clear();
                foreach (var cam in Camera.allCameras)
                {
                    if (!cam || cam == skyCam) continue;
                    savedCams.Add((cam, cam.clearFlags, cam.backgroundColor));
                    if (cam.clearFlags == CameraClearFlags.Skybox) cam.clearFlags = CameraClearFlags.SolidColor;
                    if (cam.clearFlags == CameraClearFlags.SolidColor) cam.backgroundColor = SkyColor;
                }
                var ph = PlayerHealth.Local;
                var pc = ph && ph.playerCam ? ph.playerCam.cam : null;
                if (pc && !savedCams.Exists(s => s.cam == pc))
                {
                    savedCams.Add((pc, pc.clearFlags, pc.backgroundColor));
                    pc.clearFlags = CameraClearFlags.SolidColor;
                    pc.backgroundColor = SkyColor;
                }
            }
            else
            {
                RenderSettings.skybox = savedSky;
                RenderSettings.fog = savedFog; RenderSettings.fogColor = savedFogColor; RenderSettings.fogMode = savedFogMode;
                RenderSettings.fogStartDistance = savedFogStart; RenderSettings.fogEndDistance = savedFogEnd; RenderSettings.fogDensity = savedFogDensity;
                RenderSettings.ambientMode = savedAmbientMode; RenderSettings.ambientLight = savedAmbient; RenderSettings.ambientIntensity = savedAmbientIntensity;
                foreach (var s in savedCams) if (s.cam) { s.cam.clearFlags = s.flags; s.cam.backgroundColor = s.bg; }
                savedCams.Clear();
            }
        }

        // ------------------------------------------------------------------ UI: only a crosshair for the gunner, a timer for runners

        void EnsureUI()
        {
            if (uiRoot) return;
            uiRoot = new GameObject("SkyArenaUI");
            uiRoot.transform.SetParent(transform, false);
            UIKit.MakeCanvas(uiRoot, 60);
            uiRoot.GetComponent<GraphicRaycaster>().enabled = false;

            crossRoot = UIKit.Rect("Cross", uiRoot.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(80f, 80f));
            crossBars = new Image[5];
            Vector2[] pos = { new Vector2(0f, 16f), new Vector2(0f, -16f), new Vector2(16f, 0f), new Vector2(-16f, 0f), Vector2.zero };
            Vector2[] size = { new Vector2(3f, 14f), new Vector2(3f, 14f), new Vector2(14f, 3f), new Vector2(14f, 3f), new Vector2(4f, 4f) };
            for (int i = 0; i < 5; i++)
            {
                var img = UIKit.Solid(crossRoot, "Bar" + i, Color.white);
                var rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos[i];
                rt.sizeDelta = size[i];
                img.raycastTarget = false;
                var ol = img.gameObject.AddComponent<Outline>();
                ol.effectColor = new Color(0f, 0f, 0f, 0.7f);
                ol.effectDistance = new Vector2(1f, -1f);
                crossBars[i] = img;
            }

            timerText = UIKit.Label(uiRoot.transform, "", 34, TextAnchor.MiddleCenter, Neon, true, FontStyle.Bold);
            var tr = timerText.rectTransform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.anchoredPosition = new Vector2(0f, -150f);
            tr.sizeDelta = new Vector2(800f, 60f);
            var to = timerText.gameObject.AddComponent<Outline>();
            to.effectColor = new Color(0f, 0f, 0f, 0.8f);
            uiRoot.SetActive(false);
        }

        void UpdateCrosshair(bool firing)
        {
            if (crossBars == null) return;
            float gap = 16f + (firing ? 5f : 0f) + kick * 6f;
            crossBars[0].rectTransform.anchoredPosition = new Vector2(0f, gap);
            crossBars[1].rectTransform.anchoredPosition = new Vector2(0f, -gap);
            crossBars[2].rectTransform.anchoredPosition = new Vector2(gap, 0f);
            crossBars[3].rectTransform.anchoredPosition = new Vector2(-gap, 0f);
            Color c = hitFlash > 1f ? new Color(1f, 0.15f, 0.1f) : Color.Lerp(Color.white, new Color(1f, 0.3f, 0.25f), hitFlash);
            foreach (var b in crossBars) b.color = c;
        }
    }
}
