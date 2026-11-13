using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// A character simulated on another machine (a remote player, or a bot when we are a client).
    /// Built from the same procedural HumanoidRig + CharacterAnimator as everyone else, with the
    /// owner's customization, and driven by interpolated CharState snapshots (~100 ms behind).
    /// Hits on it are forwarded to the owner, who applies the damage (and reports deaths to the host).
    /// </summary>
    [DefaultExecutionOrder(190)]
    public class NetProxy : MonoBehaviour, IDamageable, IExecutable
    {
        public Combatant combatant;
        public HumanoidRig rig;
        public CharacterAnimator anim;
        public AbilityUser abilities;
        public bool bot;

        public CharState Latest => latest;
        public bool HasState { get; private set; }
        public Vector3 NeckPoint => rig && rig.neckPoint ? rig.neckPoint.position : transform.position + Vector3.up * 1.6f;

        const float InterpDelay = 0.1f;
        readonly List<(float t, CharState s)> buffer = new List<(float, CharState)>();
        CharState latest;
        float predictedHealth = 1f, predictedUntil;
        byte weaponIdx = 254;
        Transform muzzle;
        bool wasAlive = true, collidersOn = true;
        TextMesh plate;
        Transform plateT;
        float plateCheck;
        bool plateVisible;

        // ------------------------------------------------------------------ construction

        public static NetProxy Create(int id, string nick, CharacterLook look, bool isBot)
        {
            var go = new GameObject((isBot ? "NetBot_" : "NetPlayer_") + nick);
            var c = go.AddComponent<Combatant>();
            c.netId = id;
            c.remote = true;
            c.human = !isBot;
            c.isPlayer = false;
            c.displayName = nick;
            c.look = look;
            c.alive = false;
            var rig = HumanoidRig.Build(go.transform, look.Palette(), true);
            look.Apply(rig);
            var anim = rig.gameObject.AddComponent<CharacterAnimator>();
            anim.rig = rig;
            var p = go.AddComponent<NetProxy>();
            p.combatant = c; p.rig = rig; p.anim = anim; p.bot = isBot;
            c.chestPoint = () => rig.chest.position + rig.chest.up * 0.12f;
            c.headPoint = () => rig.head.TransformPoint(new Vector3(0f, 0.1f, 0f));
            p.abilities = go.AddComponent<AbilityUser>();
            p.abilities.SetRemote();
            rig.SetCollidersEnabled(false);
            p.collidersOn = false;
            p.wasAlive = false;
            if (!isBot) p.BuildPlate(nick);
            go.SetActive(true);
            return p;
        }

        public void SetIdentity(string nick, CharacterLook look)
        {
            combatant.displayName = nick;
            name = (bot ? "NetBot_" : "NetPlayer_") + nick;
            if (look != null && (combatant.look == null || !SameLook(look, combatant.look)))
            {
                combatant.look = look;
                look.Apply(rig);
                if (abilities) abilities.RefreshVisibility();
            }
            if (plate) plate.text = nick;
        }

        static bool SameLook(CharacterLook a, CharacterLook b)
        {
            var x = a.GetBytes(); var y = b.GetBytes();
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        void BuildPlate(string nick)
        {
            var go = new GameObject("Nameplate");
            go.transform.SetParent(transform, false);
            plate = go.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            plate.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            plate.text = nick;
            plate.fontSize = 48;
            plate.characterSize = 0.035f;
            plate.anchor = TextAnchor.LowerCenter;
            plate.alignment = TextAlignment.Center;
            plate.color = new Color(1f, 0.95f, 0.8f, 0.95f);
            plateT = go.transform;
            go.SetActive(false);
        }

        void OnEnable() => Executables.All.Add(this);
        void OnDisable() => Executables.All.Remove(this);

        // ------------------------------------------------------------------ snapshots

        public void Push(CharState s)
        {
            float now = Time.unscaledTime;
            if (!HasState)
            {
                HasState = true;
                transform.SetPositionAndRotation(s.pos, Quaternion.Euler(0f, s.yaw, 0f));
            }
            // teleports (respawn, swap, phase yank): drop history so we don't slide across the map
            if (buffer.Count > 0 && (buffer[buffer.Count - 1].s.pos - s.pos).sqrMagnitude > 6f * 6f) buffer.Clear();
            buffer.Add((now, s));
            while (buffer.Count > 32) buffer.RemoveAt(0);
            latest = s;
            if (now > predictedUntil) predictedHealth = s.health;
            DetectBhop(s, now);
        }

        // ------------------------------------------------------------------ bhop anti-cheat (old builds without the local penalty)

        /// <summary>Damage dealt by the local player to a remote player caught bunny hopping.</summary>
        public const float BhopPunishDamage = 99f;
        // jump airtime on flat ground is ~0.59 s; PlayerMotor counts a jump within 0.2 s of landing as a bhop
        const float BhopChainMaxInterval = 0.8f;
        const int BhopChainToPunish = 2;      // chained hops in a row (i.e. 3 jumps) before we strike
        const float BhopPunishCooldown = 2f;
        float lastVy, lastJumpStart = -10f, lastBhopPunish = -10f;
        int bhopChain;

        /// <summary>
        /// Watches the owner's replicated vertical velocity: a jump start shows up as vy jumping up to
        /// ~6-7 m/s. Jumps started right after the previous one landed are bunny hops; a streak of them
        /// means the owner runs a build without the local bhop penalty, so we hit them for 99 from the local player.
        /// </summary>
        void DetectBhop(CharState s, float now)
        {
            float vy = s.vel.y;
            float prevVy = lastVy;
            lastVy = vy;
            if (bot || !s.Has(CharState.Alive) || s.Has(CharState.ButtRocket) || s.Has(CharState.Phasing)) { bhopChain = 0; return; }

            bool jumpStart = vy > 4.5f && vy > prevVy + 0.5f;
            if (!jumpStart) return;
            bhopChain = now - lastJumpStart <= BhopChainMaxInterval ? bhopChain + 1 : 0;
            lastJumpStart = now;
            if (bhopChain < BhopChainToPunish || now - lastBhopPunish < BhopPunishCooldown) return;

            var me = PlayerHealth.Local;
            if (!me || me.IsDead || s.Has(CharState.Protected) || s.execV >= 0f) return;
            bhopChain = 0;
            lastBhopPunish = now;
            NetGame.SendDamage(combatant, new DamageInfo
            {
                amount = BhopPunishDamage,
                attacker = me.GetComponent<Combatant>(),
                weapon = "Бхоп",
                point = s.pos + Vector3.up,
                direction = Vector3.down,
            });
            predictedHealth -= BhopPunishDamage / 100f;
            predictedUntil = now + 0.4f;
        }

        void LateUpdate()
        {
            if (!HasState) return;
            float dt = Time.deltaTime;
            float rt = Time.unscaledTime - InterpDelay;
            CharState s;
            Vector3 pos; float yaw;
            int n = buffer.Count;
            if (n == 0) return;
            if (rt <= buffer[0].t || n == 1) { s = buffer[0].s; pos = s.pos; yaw = s.yaw; }
            else if (rt >= buffer[n - 1].t)
            {
                s = buffer[n - 1].s;
                // short extrapolation using the replicated velocity
                float ex = Mathf.Min(rt - buffer[n - 1].t, 0.15f);
                pos = s.pos + Quaternion.Euler(0f, s.yaw, 0f) * new Vector3(s.vel.x, 0f, s.vel.z) * ex;
                yaw = s.yaw;
            }
            else
            {
                int i = 0;
                while (i < n - 2 && buffer[i + 1].t < rt) i++;
                var a = buffer[i]; var b = buffer[i + 1];
                float u = Mathf.InverseLerp(a.t, b.t, rt);
                s = u < 0.5f ? a.s : b.s;
                pos = Vector3.Lerp(a.s.pos, b.s.pos, u);
                yaw = Mathf.LerpAngle(a.s.yaw, b.s.yaw, u);
                s.pitch = Mathf.Lerp(a.s.pitch, b.s.pitch, u);
                s.crouch = Mathf.Lerp(a.s.crouch, b.s.crouch, u);
                s.prone = Mathf.Lerp(a.s.prone, b.s.prone, u);
                s.lean = Mathf.Lerp(a.s.lean, b.s.lean, u);
                s.aim = Mathf.Lerp(a.s.aim, b.s.aim, u);
                s.dead = Mathf.Lerp(a.s.dead, b.s.dead, u);
                s.bent = Mathf.Lerp(a.s.bent, b.s.bent, u);
                s.slip = Mathf.Lerp(a.s.slip, b.s.slip, u);
                s.blind = Mathf.Lerp(a.s.blind, b.s.blind, u);
                s.vel = Vector3.Lerp(a.s.vel, b.s.vel, u);
            }
            // drop states older than what we render
            while (buffer.Count > 2 && buffer[1].t < rt - 0.5f) buffer.RemoveAt(0);

            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            Apply(s, dt);
        }

        void Apply(CharState s, float dt)
        {
            bool alive = s.Has(CharState.Alive);
            combatant.alive = alive;
            if (wasAlive && !alive && abilities) abilities.OnDeath();
            if (!wasAlive && alive) { predictedHealth = 1f; predictedUntil = 0f; }
            wasAlive = alive;

            if (anim)
            {
                anim.localVelocity = s.vel;
                anim.grounded = s.Has(CharState.Grounded);
                anim.crouch = s.crouch; anim.prone = s.prone; anim.lean = s.lean;
                anim.aimPitch = s.pitch; anim.aim = s.aim;
                anim.holdingWeapon = s.Has(CharState.Holding) && s.weapon != 255;
                anim.pistolHold = s.pistol;
                anim.knife = s.knife; anim.throwT = s.throwT; anim.executeAttack = s.execA; anim.executeVictim = s.execV;
                anim.dead = s.dead; anim.deadDir = s.Has(CharState.DeadForward) ? 1f : -1f;
                anim.lookYaw = s.lookYaw;
                anim.bentOver = s.bent; anim.swordHold = s.Has(CharState.Sword) && alive;
                anim.parry = s.parry; anim.parryVariant = s.parryVariant; anim.slip = s.slip; anim.blind = s.blind;
            }
            if (abilities) abilities.ApplyRemoteState(s, alive);

            bool morphed = s.Has(CharState.Chicken);
            bool wantCols = alive && !morphed && s.execV < 0f;
            if (wantCols != collidersOn)
            {
                collidersOn = wantCols;
                rig.SetCollidersEnabled(wantCols);
            }

            if (s.weapon != weaponIdx) SetWeapon(s.weapon);
            UpdatePlate(s, alive);
        }

        void SetWeapon(byte idx)
        {
            weaponIdx = idx;
            muzzle = null;
            if (!rig || !rig.weaponSocket) return;
            for (int i = rig.weaponSocket.childCount - 1; i >= 0; i--) Destroy(rig.weaponSocket.GetChild(i).gameObject);
            if (idx >= CharState.Weapons.Length) return;
            var loadout = FindAnyObjectByType<Loadout>();
            if (!loadout) return;
            var model = loadout.CreateWorldModel(CharState.Weapons[idx], rig.weaponSocket);
            if (model) foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == "Muzzle") muzzle = t;
            if (abilities) abilities.RefreshVisibility();
        }

        void UpdatePlate(CharState s, bool alive)
        {
            if (!plate) return;
            var cam = Camera.main;
            bool want = alive && cam && !s.Has(CharState.Invisible) && !s.Has(CharState.Chicken);
            if (want)
            {
                Vector3 top = combatant.Head + Vector3.up * 0.42f * Mathf.Max(1f, s.headScale);
                Vector3 to = top - cam.transform.position;
                float d = to.magnitude;
                want = d < 45f;
                plateCheck -= Time.unscaledDeltaTime;
                if (want && plateCheck <= 0f)
                {
                    plateCheck = 0.2f;
                    // no wallhack: only when the head is in line of sight
                    plateVisible = !Physics.Raycast(cam.transform.position, to / d, out RaycastHit h, d - 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                                   || h.collider.transform.IsChildOf(transform);
                }
                want &= plateVisible;
                if (want)
                {
                    plateT.position = top;
                    plateT.rotation = Quaternion.LookRotation(to);
                    plateT.localScale = Vector3.one * Mathf.Clamp(d / 12f, 0.6f, 2.5f);
                }
            }
            if (plate.gameObject.activeSelf != want) plate.gameObject.SetActive(want);
        }

        // ------------------------------------------------------------------ remote events

        static Sfx ShotSound(byte w)
        {
            switch (w)
            {
                case 2: return Sfx.ShotShotgun;
                case 3: return Sfx.ShotSniper;
                case 4: return Sfx.ShotPistol;
            }
            return Sfx.Shot;
        }

        public Vector3 MuzzlePoint => muzzle ? muzzle.position : (rig && rig.weaponSocket ? rig.weaponSocket.position + transform.forward * 0.45f : combatant.Head);

        /// <summary>The owner fired: muzzle flash, tracer, sound and impacts (the hits themselves arrive as damage).</summary>
        public void OnShot(Vector3 origin, List<(Vector3 end, byte kind)> ends)
        {
            combatant.lastShotTime = Time.time;
            Vector3 from = MuzzlePoint;
            Vector3 dir0 = ends.Count > 0 ? (ends[0].end - origin).normalized : transform.forward;
            var fx = FX.Ensure();
            fx.MuzzleFlashWorld(from, dir0);
            ProcAudio.PlayAt(ShotSound(latest.weapon), from, 0.85f, 0.06f);
            if (anim) anim.Flinch(new Vector3(0f, 0f, -0.3f), 0.4f);
            for (int i = 0; i < ends.Count; i++)
            {
                var (end, kind) = ends[i];
                if (i == 0 || i % 3 == 0) fx.Tracer(from, end);
                Vector3 d = end - origin;
                float len = d.magnitude;
                if (len < 0.01f || kind == 2) continue;
                if (kind == 1) { fx.Blood(end, -d / len, 6); continue; }
                if (Physics.Raycast(origin, d / len, out RaycastHit hit, len + 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !hit.collider.GetComponentInParent<HumanoidRig>())
                {
                    var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                    fx.Impact(hit, tag ? tag.kind : SurfaceKind.Concrete, hit.rigidbody == null && hit.collider.GetComponent<IDamageable>() == null);
                }
            }
            // near miss crack for the local player
            var ph = PlayerHealth.Local;
            if (ph && !ph.IsDead && ends.Count > 0)
            {
                Vector3 cam = ph.playerCam.cam.transform.position;
                Vector3 end = ends[0].end;
                Vector3 dir = end - origin; float len = dir.magnitude;
                if (len > 0.01f)
                {
                    dir /= len;
                    Vector3 closest = origin + dir * Mathf.Clamp(Vector3.Dot(cam - origin, dir), 0f, len);
                    if (Vector3.Distance(closest, cam) < 1.5f && Vector3.Distance(end, cam) > 0.8f) ProcAudio.PlayAt(Sfx.Whiz, closest, 0.7f, 0.15f);
                }
            }
            if (Net.IsHost) AIEvents.Emit(origin, 45f, combatant);
        }

        // ------------------------------------------------------------------ damage (forwarded to the owner)

        public DamageResult TakeDamage(DamageInfo info)
        {
            if (!combatant.alive || latest.Has(CharState.Protected) || latest.execV >= 0f) return default;
            NetGame.SendDamage(combatant, info);
            // predict the result for hitmarkers / kill sounds; corrected by the next snapshots
            predictedHealth -= info.amount / 100f;
            predictedUntil = Time.unscaledTime + 0.4f;
            bool killed = predictedHealth <= 0f;
            if (info.attacker && info.attacker.isPlayer && bot) DamageNumbers.Spawn(info.point, info.amount, info.critical, killed);
            if (anim) anim.Flinch(transform.InverseTransformDirection(info.direction), Mathf.Clamp(info.amount / 30f, 0.4f, 2f));
            return new DamageResult { applied = true, killed = killed, dealt = info.amount };
        }

        // ------------------------------------------------------------------ knife execution (victim plays it on its own machine)

        public bool CanBeExecutedBy(Transform attacker)
        {
            if (!combatant.alive || latest.execV >= 0f || latest.Has(CharState.Protected) || latest.Has(CharState.Chicken) || latest.Has(CharState.Phasing)) return false;
            Vector3 to = transform.position - attacker.position; to.y = 0f;
            float d = to.magnitude;
            if (d > 1.9f || d < 0.2f) return false;
            return Vector3.Dot(transform.forward, to / d) >= 0.35f;
        }

        public void BeginExecution() => NetGame.SendExecution(combatant, PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null);
        public void SetExecutionProgress(float t) { }
        public void ExecutionKilled(Combatant killer) { DamageNumbers.Spawn(NeckPoint, 200f, true, true); }
        public void EndExecution() { }
    }
}
