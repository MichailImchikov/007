using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// The local player's class (PlayerLoadout.Class, taken on every spawn by the Loadout):
    ///  • passives every frame — move speeds (the motor's own fields, so nothing stacks) and reload speed of every weapon;
    ///  • the class key (3): assault — adrenaline (+25 HP, faster still for a while); medic — a kit (+60 HP), or, held next
    ///    to a fallen ally in a team game, a revive; engineer — draw / put away the launcher; sniper — recon (enemies
    ///    through walls). Runs after WeaponController / AbilityUser (they set the motor's speed multiplier every frame).
    /// Bots have the same classes in CombatBot.Classes.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public class ClassAbility : MonoBehaviour
    {
        public static ClassAbility Local { get; private set; }

        Combatant self;
        PlayerHealth health;
        PlayerMotor motor;
        PlayerCamera pcam;
        Loadout loadout;
        float baseWalk, baseSprint, baseCrouch, baseMax;
        bool basesTaken, lowered;

        public PlayerClass Class => self ? self.playerClass : PlayerClass.Assault;
        public ClassInfo Info => ClassDB.Get(Class);
        public float Cooldown { get; private set; }
        public float CooldownTotal { get; private set; } = 1f;
        public float ActiveLeft { get; private set; }
        public float ActiveFraction => Info.duration > 0f ? ActiveLeft / Info.duration : 0f;
        public bool AdrenalineOn => Class == PlayerClass.Assault && ActiveLeft > 0f;
        /// <summary>A fallen ally within reach (medic, team game): the HUD prompts "[3] Поднять".</summary>
        public Combatant PromptTarget { get; private set; }
        public float ReviveProgress { get; private set; }
        /// <summary>Engineer: rockets left (loaded + reserve).</summary>
        public int Rockets { get { var g = loadout ? loadout.Gadget : null; return g ? g.Ammo + g.Reserve : 0; } }
        public bool LauncherOut => loadout && loadout.Gadget && loadout.Active == loadout.Gadget;

        /// <summary>A short line for the centre of the screen (ClassHUD).</summary>
        public static event Action<string> Message;

        void Awake()
        {
            self = GetComponent<Combatant>();
            health = GetComponent<PlayerHealth>();
            motor = GetComponent<PlayerMotor>();
            loadout = GetComponent<Loadout>();
            pcam = health ? health.playerCam : GetComponentInChildren<PlayerCamera>();
            if (health) Local = this;
        }

        void OnEnable()
        {
            if (!health) return;
            health.Respawned += OnRespawned;
            health.Died += OnDied;
        }

        void OnDisable()
        {
            if (!health) return;
            health.Respawned -= OnRespawned;
            health.Died -= OnDied;
        }

        void OnDestroy() { if (Local == this) Local = null; }

        void OnRespawned()
        {
            Cooldown = 0f;
            ActiveLeft = 0f;
            ReconVision.Stop();
        }

        void OnDied()
        {
            ActiveLeft = 0f;
            ReconVision.Stop();
            EndRevive();
        }

        void Say(string s) => Message?.Invoke(s);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !self || self.remote) return;
            if (Cooldown > 0f) Cooldown = Mathf.Max(0f, Cooldown - dt);
            if (ActiveLeft > 0f) ActiveLeft = Mathf.Max(0f, ActiveLeft - dt);
            ApplyPassives();
            if (!self.alive || (health && health.IsDead) || DroneMode.Active) { EndRevive(); PromptTarget = null; return; }
            UpdateRevive(dt);
            if (ReviveProgress > 0f) return; // the key is busy bringing someone back
            if (GameInput.ClassAbilityPressed) Activate();
        }

        // ------------------------------------------------------------------ passives

        void ApplyPassives()
        {
            if (!motor) return;
            if (!basesTaken)
            {
                baseWalk = motor.walkSpeed; baseSprint = motor.sprintSpeed; baseCrouch = motor.crouchSpeed; baseMax = motor.maxHorizontalSpeed;
                basesTaken = true;
            }
            float move = Info.moveMul * (AdrenalineOn ? 1.15f : 1f);
            motor.walkSpeed = baseWalk * move;
            motor.sprintSpeed = baseSprint * move;
            motor.crouchSpeed = baseCrouch * move;
            motor.maxHorizontalSpeed = baseMax * Mathf.Max(1f, move);
            float reload = Info.reloadSpeed * (AdrenalineOn ? 1.35f : 1f);
            if (loadout && loadout.weapons != null)
                foreach (var w in loadout.weapons) if (w) w.ReloadSpeed = reload;
        }

        // ------------------------------------------------------------------ the class key

        void Deny() => ProcAudio.Play(Sfx.Dry, 0.45f);

        void StartCooldown(float s) { Cooldown = s; CooldownTotal = Mathf.Max(0.01f, s); }

        void Activate()
        {
            var info = Info;
            switch (Class)
            {
                case PlayerClass.Assault:
                    if (Cooldown > 0f) { Deny(); return; }
                    ActiveLeft = info.duration;
                    if (health) health.Heal(25f);
                    StartCooldown(info.cooldown);
                    ProcAudio.Play(Sfx.Stab, 0.35f);
                    ProcAudio.Play(Sfx.Equip, 0.8f);
                    if (pcam) { pcam.Punch(new Vector3(-6f, 0f, 3f)); pcam.Shake(0.25f); }
                    Say("Адреналин!");
                    break;
                case PlayerClass.Medic:
                    if (Cooldown > 0f) { Deny(); return; }
                    if (health && health.Health >= health.maxHealth - 0.5f) { Say("Здоровье полное"); return; }
                    if (health) health.Heal(60f);
                    StartCooldown(info.cooldown);
                    ProcAudio.Play(Sfx.Equip, 0.8f);
                    Say("Аптечка: +60");
                    break;
                case PlayerClass.Engineer:
                {
                    var g = loadout ? loadout.Gadget : null;
                    if (!g) { Deny(); return; }
                    if (loadout.Active == g) loadout.SwitchTo(loadout.Primary ?? loadout.Secondary);
                    else
                    {
                        if (Rockets <= 0) Say("Нет гранат для РПГ");
                        loadout.SwitchTo(g);
                    }
                    break;
                }
                case PlayerClass.Sniper:
                    if (Cooldown > 0f) { Deny(); return; }
                    ActiveLeft = info.duration;
                    ReconVision.Show(info.duration, 80f, self);
                    StartCooldown(info.cooldown);
                    Say("Разведка: противники видны сквозь стены");
                    break;
            }
        }

        // ------------------------------------------------------------------ medic: revive

        void UpdateRevive(float dt)
        {
            PromptTarget = Class == PlayerClass.Medic ? FindFallenAlly() : null;
            if (PromptTarget && GameInput.ClassAbilityHeld)
            {
                ReviveProgress += dt / Revives.HoldTime;
                if (!lowered && loadout && loadout.Active) { loadout.Active.ExternalLower = 0.85f; lowered = true; }
                if (ReviveProgress >= 1f)
                {
                    var t = PromptTarget;
                    Revives.Apply(t, self);
                    ProcAudio.Play(Sfx.Equip, 0.9f);
                    Say("Поднят: " + t.displayName);
                    EndRevive();
                }
            }
            else EndRevive();
        }

        void EndRevive()
        {
            ReviveProgress = 0f;
            if (lowered && loadout && loadout.Active) loadout.Active.ExternalLower = 0f;
            lowered = false;
        }

        Combatant FindFallenAlly()
        {
            if (!Teams.Active) return null;
            Combatant best = null;
            float bd = Revives.Reach;
            Vector3 p = transform.position;
            foreach (var c in Combatant.All)
            {
                if (!Revives.CanRevive(self, c)) continue;
                Vector3 d = c.transform.position - p; d.y = 0f;
                float dist = d.magnitude;
                if (dist < bd) { bd = dist; best = c; }
            }
            return best;
        }
    }
}
