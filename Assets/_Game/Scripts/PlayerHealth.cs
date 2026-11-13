using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Local player health: damage feedback, regeneration, death camera and respawn
    /// (at a match spawn point in Deathmatch, or at the start position elsewhere).
    /// </summary>
    [RequireComponent(typeof(Combatant))]
    public class PlayerHealth : MonoBehaviour, IDamageable
    {
        public static PlayerHealth Local { get; private set; }

        public float maxHealth = 100f;
        public float regenDelay = 5f;
        public float regenRate = 22f;
        public float respawnDelay = 3.5f;
        public float spawnProtection = 1.5f;

        public PlayerMotor motor;
        public PlayerCamera playerCam;
        public Loadout loadout;

        public float Health { get; private set; }
        public bool IsDead { get; private set; }
        public float RespawnIn => Mathf.Max(0f, deathTimer);
        public Combatant LastKiller { get; private set; }
        public string LastKillerWeapon { get; private set; }
        public bool Protected => protectTimer > 0f;
        /// <summary>0..1 while being executed from behind by another player (multiplayer), else -1.</summary>
        public float ExecutedProgress => execT;
        /// <summary>The body falls forward (executions) instead of backwards.</summary>
        public bool DeadForward { get; private set; }
        public bool BeingExecuted => execT >= 0f;

        /// <summary>Raised on every hit: world position of the source, damage amount.</summary>
        public event Action<Vector3, float> Damaged;
        public event Action Died, Respawned;

        Combatant self;
        AbilityUser abilities;
        float lastHit = -99f, deathTimer, deathT, protectTimer, execT = -1f;
        Combatant executor;
        bool execKilled;
        Vector3 startPos;
        float startYaw;

        void Awake()
        {
            Local = this;
            self = GetComponent<Combatant>();
            self.isPlayer = true;
            abilities = GetComponent<AbilityUser>();
            if (!abilities) abilities = gameObject.AddComponent<AbilityUser>();
            self.chestPoint = () => transform.position + Vector3.up * (motor.EyeHeight - 0.4f);
            self.headPoint = () => transform.position + Vector3.up * (motor.EyeHeight - 0.03f);
            Health = maxHealth;
            startPos = transform.position;
            startYaw = transform.eulerAngles.y;
        }

        void Start()
        {
            MatchHUD.Ensure();
            PauseMenu.Ensure();
            AbilitySpawner.Ensure();
            GameInput.Blocked = false;
        }

        public DamageResult TakeDamage(DamageInfo info)
        {
            if (IsDead || protectTimer > 0f || execT >= 0f) return default;
            if (abilities && abilities.TryDeflect(info)) return default;
            float amount = info.amount;
            if (info.attacker == self) amount *= 0.6f; // own grenade hurts less
            Health -= amount;
            lastHit = Time.time;

            Vector3 src = info.attacker ? info.attacker.transform.position : info.point - info.direction * 5f;
            Damaged?.Invoke(src, amount);
            Vector3 local = transform.InverseTransformDirection((transform.position - src).normalized);
            playerCam.Punch(new Vector3(-6f - amount * 0.25f, local.x * 10f, -local.x * 14f));
            playerCam.Shake(Mathf.Clamp01(amount / 80f));
            ProcAudio.Play(Sfx.Hurt, Mathf.Clamp(0.4f + amount / 60f, 0.4f, 1f), 0.1f);

            bool killed = Health <= 0f;
            if (killed) Die(info);
            return new DamageResult { applied = true, killed = killed, dealt = amount };
        }

        /// <summary>
        /// Another player started a knife execution on us (network): input is frozen, the body plays the
        /// victim animation (replicated to everyone) and the stab at 40% kills.
        /// </summary>
        public void BeginExecutedBy(Combatant attacker)
        {
            if (IsDead || execT >= 0f) return;
            execT = 0f;
            executor = attacker;
            execKilled = false;
            if (abilities) abilities.OnDeath();
            GameInput.Blocked = true;
            motor.Locked = true;
            playerCam.Locked = true;
            if (loadout && loadout.Active) loadout.Active.ExternalLower = 1f;
            playerCam.Shake(0.5f);
            ProcAudio.Play(Sfx.Hurt, 0.9f, 0.05f);
        }

        void UpdateExecuted(float dt)
        {
            execT += dt / 1.9f;
            playerCam.SetLook(playerCam.Yaw, Mathf.Lerp(playerCam.Pitch, -25f, dt * 3f));
            if (!execKilled && execT >= 0.4f)
            {
                execKilled = true;
                ProcAudio.Play(Sfx.Stab, 1f);
                FX.Ensure().Blood(transform.position + Vector3.up * 1.5f, transform.forward, 14);
                DeadForward = true;
                Die(new DamageInfo { amount = 200f, attacker = executor, weapon = "Нож", critical = true, direction = transform.forward });
            }
            if (execT >= 1f) execT = -1f;
        }

        void Die(DamageInfo info)
        {
            if (abilities) abilities.OnDeath();
            IsDead = true;
            Health = 0f;
            self.alive = false;
            deathTimer = respawnDelay;
            deathT = 0f;
            LastKiller = info.attacker;
            LastKillerWeapon = info.weapon;
            GameInput.Blocked = true;
            motor.Locked = true;
            playerCam.Locked = true;
            if (loadout && loadout.Active) loadout.Active.ExternalLower = 1f;
            MatchManager.Report(info.attacker, self, info.weapon, info.critical);
            ProcAudio.Play(Sfx.Land, 0.9f, 0.05f);
            Died?.Invoke();
        }

        public void Respawn()
        {
            Vector3 pos = startPos;
            float yaw = startYaw;
            if (MatchManager.Instance && MatchManager.Instance.GetSpawn(self, out var p, out var r)) { pos = p; yaw = r.eulerAngles.y; }
            motor.Locked = false;
            motor.Teleport(pos + Vector3.up * 0.05f);
            playerCam.Locked = false;
            playerCam.SetLook(yaw, 0f);
            playerCam.DeathAmount = 0f;
            Health = maxHealth;
            IsDead = false;
            DeadForward = false;
            execT = -1f;
            self.alive = true;
            protectTimer = spawnProtection;
            GameInput.Blocked = MatchManager.Instance && MatchManager.Instance.Ended;
            if (loadout)
            {
                loadout.ApplyPlayerChoice();
                loadout.RefillAll();
                if (loadout.Active) loadout.Active.ExternalLower = 0f;
            }
            Respawned?.Invoke();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (protectTimer > 0f) protectTimer -= dt;
            if (execT >= 0f) UpdateExecuted(dt);
            if (execT >= 0f && !IsDead) return;
            if (IsDead)
            {
                deathT += dt;
                playerCam.DeathAmount = Anim.Smooth(deathT / 0.9f);
                deathTimer -= dt;
                bool canRespawn = !MatchManager.Instance || !MatchManager.Instance.Ended;
                if (deathTimer <= 0f && canRespawn) Respawn();
                return;
            }
            if (Time.time - lastHit > regenDelay && Health < maxHealth)
                Health = Mathf.Min(maxHealth, Health + regenRate * dt);
        }
    }
}
