using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Passive training soldier: patrols between waypoints or stands guard, takes damage per body part,
    /// flinches, dies with a fall and respawns. Can be executed from behind with the knife.
    /// </summary>
    public class TrainingBot : MonoBehaviour, IDamageable, IExecutable, IAbilityPuppet
    {
        public static readonly List<TrainingBot> All = new List<TrainingBot>();

        public float maxHealth = 100f;
        public float walkSpeed = 1.5f;
        public float respawnDelay = 5f;
        public Vector3[] waypoints;
        public float guardYaw;
        public HumanoidRig rig;
        public CharacterAnimator anim;
        public Transform heldWeapon;

        [Header("Knife sparring")]
        [Tooltip("Sparring partner: answers every knife swing of the player in front of it — the blades lock into a KnifeDuel")]
        public bool sparring;
        [Tooltip("Strikes per second in a knife duel")] public float duelRate = 6f;
        public string sparName = "";

        public bool IsAlive => health > 0f;
        public bool IsBeingExecuted => executing;
        public Vector3 NeckPoint => rig && rig.neckPoint ? rig.neckPoint.position : transform.position + Vector3.up * 1.6f;

        float health;
        int wp;
        float wait;
        float deadTimer, deadAnim;
        bool executing;
        Vector3 spawnPos;
        Quaternion spawnRot;
        Vector3 lastPos;
        float lookTimer, lookTarget;
        Renderer[] flashRenderers;
        float flash;
        MaterialPropertyBlock mpb;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        // ---- ability target (grappling hook, swap, banana, farts...)
        Combatant combatant;
        AbilityUser abilities;
        bool external;
        float stunTimer;

        public bool ExternalControl
        {
            get => external;
            set
            {
                if (external == value) return;
                external = value;
                if (!value) SettleOnGround();
            }
        }

        public void Stun(float seconds) { if (IsAlive) stunTimer = Mathf.Max(stunTimer, seconds); }

        void SettleOnGround()
        {
            Vector3 p = transform.position;
            float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(transform.position + Vector3.up * 1.5f, Vector3.down, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < best) { best = h.distance; p.y = h.point.y; }
            }
            transform.position = p;
            lastPos = p;
        }

        void OnEnable() { All.Add(this); Executables.All.Add(this); }
        void OnDisable() { All.Remove(this); Executables.All.Remove(this); }

        void Awake()
        {
            health = maxHealth;
            spawnPos = transform.position;
            spawnRot = transform.rotation;
            lastPos = transform.position;
            if (!rig) rig = GetComponentInChildren<HumanoidRig>();
            if (!anim) anim = GetComponent<CharacterAnimator>();
            flashRenderers = rig ? rig.renderers : new Renderer[0];
            mpb = new MaterialPropertyBlock();
            wait = Random.Range(0f, 2f);

            // a Combatant + ability slot so targeted abilities (hook, swap, farts, banana) work on the range
            combatant = GetComponent<Combatant>();
            if (!combatant) combatant = gameObject.AddComponent<Combatant>();
            combatant.displayName = sparring && sparName.Length > 0 ? sparName : "Манекен";
            combatant.isPlayer = false;
            if (rig)
            {
                combatant.chestPoint = () => rig.chest.position + rig.chest.up * 0.12f;
                combatant.headPoint = () => rig.head.position + Vector3.up * 0.1f;
            }
            abilities = GetComponent<AbilityUser>();
            if (!abilities) abilities = gameObject.AddComponent<AbilityUser>();
            if (sparring && heldWeapon) heldWeapon.gameObject.SetActive(false); // bare hands until the knives come out
        }

        public DamageResult TakeDamage(DamageInfo info)
        {
            if (!IsAlive || executing) return default;
            if (TicTacToe.Protects(combatant)) return default; // tic-tac-toe: both players untouchable
            if (KnifeDuel.Blocks(info, combatant)) return default; // knife duel: untouchable / the blades just locked
            health -= info.amount;
            bool killed = health <= 0f;
            DamageNumbers.Spawn(info.point, info.amount, info.critical, killed);
            flash = 1f;
            if (anim) anim.Flinch(transform.InverseTransformDirection(info.direction), Mathf.Clamp(info.amount / 30f, 0.4f, 2f));
            if (killed)
            {
                float dirDot = Vector3.Dot(info.direction, transform.forward);
                Die(dirDot > 0f ? 1f : -1f);
            }
            return new DamageResult { applied = true, killed = killed, dealt = info.amount };
        }

        void Die(float dir)
        {
            health = 0f;
            if (combatant) combatant.alive = false;
            external = false;
            stunTimer = 0f;
            if (abilities) abilities.OnDeath();
            deadTimer = respawnDelay;
            deadAnim = 0f;
            if (anim) anim.deadDir = dir;
            if (rig) rig.SetCollidersEnabled(false);
        }

        // ------------------------------------------------------------------ execution (victim side)

        public bool CanBeExecutedBy(Transform attacker)
        {
            if (!IsAlive || executing || external || sparring || KnifeDuel.InDuel(combatant) || TicTacToe.InGame(combatant)) return false;
            Vector3 to = transform.position - attacker.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > 1.9f || dist < 0.2f) return false;
            // attacker must be behind: the bot faces away from the attacker
            return Vector3.Dot(transform.forward, to / dist) > 0.4f;
        }

        public void BeginExecution()
        {
            executing = true;
            if (abilities) abilities.OnDeath();
            if (anim) anim.executeVictim = 0f;
        }

        public void SetExecutionProgress(float t)
        {
            if (anim) anim.executeVictim = t;
            if (t >= 0.82f && IsAlive) { health = 0f; if (combatant) combatant.alive = false; deadTimer = respawnDelay + 1f; deadAnim = 0f; if (anim) anim.deadDir = 1f; }
        }

        public void ExecutionKilled(Combatant killer)
        {
            DamageNumbers.Spawn(NeckPoint, 200f, true, true);
            flash = 1f;
        }

        public void EndExecution()
        {
            executing = false;
            if (anim) anim.executeVictim = -1f;
            if (IsAlive) Die(1f);
            else if (rig) rig.SetCollidersEnabled(false);
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // sparring partner: never a gun in the hands (ability visibility refreshes may turn it back on)
            if (sparring && heldWeapon && heldWeapon.gameObject.activeSelf) heldWeapon.gameObject.SetActive(false);

            if (!IsAlive)
            {
                if (!executing || (anim && anim.executeVictim >= 0.82f))
                {
                    deadAnim = Mathf.Min(1f, deadAnim + dt / 0.6f);
                    float bounce = deadAnim >= 1f ? 0f : 0f;
                    if (anim) anim.dead = Mathf.Min(1f, deadAnim) + bounce;
                }
                if (!executing)
                {
                    deadTimer -= dt;
                    if (deadTimer <= 0f) Respawn();
                }
            }
            else if (stunTimer > 0f) stunTimer -= dt;
            else if (!executing && !external) { if (sparring) Spar(dt); else Think(dt); }

            // feed the animator
            Vector3 vel = (transform.position - lastPos) / dt;
            lastPos = transform.position;
            if (anim)
            {
                anim.localVelocity = transform.InverseTransformDirection(vel);
                anim.grounded = true;
                anim.aim = 0.2f;
                anim.aimPitch = 5f;
                if (!IsAlive && !executing) anim.executeVictim = -1f;
            }

            if (flash > 0f)
            {
                flash = Mathf.MoveTowards(flash, 0f, dt * 6f);
                foreach (var r in flashRenderers)
                {
                    if (!r) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor(EmissionId, new Color(1f, 0.25f, 0.2f) * (flash * flash * 1.5f));
                    r.SetPropertyBlock(mpb);
                }
            }
        }

        void Think(float dt)
        {
            if (waypoints != null && waypoints.Length > 1)
            {
                if (wait > 0f) { wait -= dt; LookAround(dt); return; }
                Vector3 target = waypoints[wp];
                Vector3 to = target - transform.position; to.y = 0f;
                if (to.magnitude < 0.15f)
                {
                    wp = (wp + 1) % waypoints.Length;
                    wait = Random.Range(1.5f, 3.5f);
                    return;
                }
                Quaternion want = Quaternion.LookRotation(to.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 160f * dt);
                float facing = Mathf.Clamp01(Vector3.Dot(transform.forward, to.normalized));
                transform.position += transform.forward * walkSpeed * facing * dt;
                if (anim) anim.lookYaw = Anim.Damp(anim.lookYaw, 0f, 4f, dt);
            }
            else
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, guardYaw, 0f), 90f * dt);
                LookAround(dt);
            }
        }

        // ------------------------------------------------------------------ knife sparring

        float sparT = -1f, seenSwing = -99f;

        /// <summary>Faces a player who comes close and answers each knife swing with its own: two blades → a duel.</summary>
        void Spar(float dt)
        {
            var ph = PlayerHealth.Local;
            var pc = ph && !ph.IsDead ? ph.GetComponent<Combatant>() : null;
            float wantYaw = guardYaw;
            float dist = 99f;
            if (pc)
            {
                Vector3 to = pc.transform.position - transform.position; to.y = 0f;
                dist = to.magnitude;
                if (dist < 5f && dist > 0.05f) wantYaw = Quaternion.LookRotation(to).eulerAngles.y;
                float sw = KnifeDuel.SwingTime(pc);
                bool facing = dist > 0.05f && Vector3.Angle(transform.forward, to) < 60f;
                if (sw > seenSwing && Time.time - sw < 0.3f && dist < 2.4f && facing && sparT < 0f)
                {
                    seenSwing = sw;
                    sparT = 0f;
                    KnifeDuel.NoteSwing(combatant);
                    ProcAudio.PlayAt(Sfx.KnifeSwing, transform.position + Vector3.up * 1.3f, 0.8f, 0.1f);
                }
            }
            // pushed around by a duel: walk back to the mark while nobody is close
            Vector3 home = spawnPos - transform.position; home.y = 0f;
            if (dist > 3.5f && home.magnitude > 0.2f)
            {
                wantYaw = Quaternion.LookRotation(home).eulerAngles.y;
                transform.position += home.normalized * Mathf.Min(walkSpeed * dt, home.magnitude);
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, wantYaw, 0f), 400f * dt);
            if (sparT >= 0f)
            {
                sparT += dt / 0.5f;
                if (sparT >= 1f) sparT = -1f;
            }
            if (anim)
            {
                // ready stance while somebody is close, a counter swing when the player strikes
                anim.knife = sparT >= 0f ? sparT : -1f;
                anim.holdingWeapon = false;
                anim.lookYaw = Anim.Damp(anim.lookYaw, 0f, 4f, dt);
                anim.crouch = Anim.Damp(anim.crouch, dist < 5f ? 0.15f : 0f, 4f, dt);
            }
        }

        void LookAround(float dt)
        {
            lookTimer -= dt;
            if (lookTimer <= 0f) { lookTimer = Random.Range(1.5f, 4f); lookTarget = Random.Range(-40f, 40f); }
            if (anim) anim.lookYaw = Anim.Damp(anim.lookYaw, lookTarget, 3f, dt);
        }

        void Respawn()
        {
            health = maxHealth;
            if (combatant) combatant.alive = true;
            external = false;
            stunTimer = 0f;
            if (abilities) abilities.OnSpawn();
            transform.SetPositionAndRotation(spawnPos, spawnRot);
            lastPos = spawnPos;
            wp = 0;
            wait = 1f;
            if (anim) { anim.dead = 0f; anim.executeVictim = -1f; }
            if (rig) rig.SetCollidersEnabled(true);
            ProcAudio.PlayAt(Sfx.TargetUp, transform.position, 0.5f);
        }
    }
}
