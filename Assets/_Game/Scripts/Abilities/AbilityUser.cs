using System;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace FPS
{
    /// <summary>
    /// One ability slot of a combatant (local player or bot — later network players): pickup, cooldown,
    /// activation, every ability effect and the status effects other abilities inflict
    /// (blind, slip, chicken, pulled). Bots decide when to use what they carry in BotThink.
    /// The player presses V; X replaces the slot with a pickup under the feet.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public partial class AbilityUser : MonoBehaviour
    {
        // ------------------------------------------------------------------ slot
        public AbilityType Type { get; private set; }
        /// <summary>What the slot looks like (a trap is disguised as another ability).</summary>
        public AbilityType Shown { get; private set; }
        public float Cooldown { get; private set; }
        public float CooldownTotal { get; private set; } = 1f;
        /// <summary>-1 = unlimited.</summary>
        public int UsesLeft { get; private set; } = -1;
        public AbilityPickup NearbyPickup { get; set; }
        public bool IsPlayer => isPlayer;
        /// <summary>Picks up and uses abilities (player, Deathmatch bots). Training dummies are only targets.</summary>
        public bool CanUseAbilities => isPlayer || bot;
        IAbilityPuppet puppet;
        public PlayerMotor Motor => motor;

        /// <summary>HUD messages: title, subtitle, color.</summary>
        public event Action<string, string, Color> Message;

        // ------------------------------------------------------------------ status
        public bool Invisible => invisT > 0f;
        public bool IsChicken => chickenT > 0f;
        public bool Parrying => parryT > 0f;
        public bool Phasing => phaseT > 0f || yankT >= 0f;
        public bool Blinded => blindT > 0f;
        public float BlindAmount => blindT <= 0f ? 0f : Mathf.Clamp01(blindT / Mathf.Max(0.01f, blindTotal * 0.5f));
        public bool Morphed => IsChicken;
        public bool BlocksShooting => Morphed || Parrying || rocketT >= 0f || slipT > 0f || ExtraBlocksShooting;
        public bool Busy => Morphed || Parrying || Phasing || hookState != 0 || rocketT >= 0f || Building || Minesweeping || rocketAim;
        /// <summary>Player holds V with the launcher: zoomed in, bent over, charge sticking out of the butt.</summary>
        public bool RocketAiming => rocketAim;
        public float SlipAmount => slipAmt;

        /// <summary>Timed ability in progress, 1 → 0 (for the HUD bar). Negative when none.</summary>
        public float ActiveFraction
        {
            get
            {
                float ef = ExtraFraction(out _);
                if (ef >= 0f) return ef;
                if (IsChicken) return chickenT / chickenDur;
                if (Parrying) return parryT / parryDur;
                if (phaseT > 0f) return phaseT / phaseDur;
                if (Invisible) return invisT / invisDur;
                return -1f;
            }
        }

        public Color ActiveColor
        {
            get
            {
                if (ExtraFraction(out Color ec) >= 0f) return ec;
                if (IsChicken) return new Color(1f, 0.85f, 0.3f);
                if (Parrying) return AbilityDB.Get(AbilityType.Parry).color;
                if (phaseT > 0f) return AbilityDB.Get(AbilityType.Phase).color;
                return AbilityDB.Get(AbilityType.Invisibility).color;
            }
        }

        // ------------------------------------------------------------------ refs
        Combatant self;
        bool isPlayer;
        CombatBot bot;
        PlayerMotor motor;
        PlayerCamera pcam;
        Loadout loadout;
        MeleeController melee;
        HumanoidRig rig;
        CharacterAnimator anim;

        // ------------------------------------------------------------------ state
        float invisT, invisDur = 1f;
        float chickenT, chickenDur = 1f, cluckT;
        ChickenModel chicken;
        float parryT, parryDur = 1f, parryAnim = -1f, slashCd;
        int parryVariant;
        SwordViewmodel swordVM;
        Transform botSword;
        float phaseT, phaseDur = 1f, yankT = -1f;
        Vector3 phaseStart, yankFrom, phaseDest;
        float blindT, blindTotal = 1f;
        float slipT, slipAmt;
        float bent, bentPulse;
        bool rocketAim;
        float rocketAimGrow;
        RocketProjectile aimRocket;
        float rocketT = -1f;
        RocketProjectile mountedRocket;
        Combatant rocketTarget;
        float tpBlend, fovAdd, thinkT;
        int visKey = -1;

        // hook
        int hookState; // 0 none, 1 flying, 2 pulling self, 3 pulling victim, 4 retracting
        Vector3 hookPos, hookEnd;
        bool hookHasHit;
        float hookTimer;
        Combatant hookVictim;
        LineRenderer rope;
        Transform hookModel;
        static Material ropeMat;

        readonly RaycastHit[] hits = new RaycastHit[16];

        void Awake()
        {
            self = GetComponent<Combatant>();
            if (self) self.abilities = this;
            bot = GetComponent<CombatBot>();
            puppet = GetComponent<IAbilityPuppet>();
            motor = GetComponent<PlayerMotor>();
            isPlayer = !bot && motor;
            pcam = GetComponent<PlayerCamera>();
            loadout = GetComponent<Loadout>();
            melee = GetComponent<MeleeController>();
            rig = GetComponentInChildren<HumanoidRig>();
            anim = GetComponentInChildren<CharacterAnimator>();
        }

        void Start()
        {
            if (isPlayer) AbilityHUD.Ensure().Bind(this);
        }

        void OnDestroy()
        {
            if (isPlayer) { GameInput.Inverted = false; GameInput.Frozen = false; GameInput.ModalUI = false; WeaponController.AbilityLower = 0f; }
            CleanupHook();
            if (mountedRocket) Destroy(mountedRocket.gameObject);
        }

        // ------------------------------------------------------------------ slot API

        public void Give(AbilityType type, AbilityType shown)
        {
            Type = type;
            Shown = shown == AbilityType.None ? type : shown;
            var info = AbilityDB.Get(Shown);
            UsesLeft = AbilityDB.Get(type).uses > 0 ? AbilityDB.Get(type).uses : -1;
            Cooldown = 0f;
            CooldownTotal = 1f;
            if (isPlayer) Say("Способность: " + info.name, info.desc + "   [V]", info.color);
        }

        void Say(string title, string sub, Color c) => Message?.Invoke(title, sub, c);

        public bool TryActivate()
        {
            if (Type == AbilityType.None || !self || !self.alive) return false;
            bool blocked = Cooldown > 0f || Busy || slipT > 0f;
            if (isPlayer && (motor.Locked || (loadout && (loadout.Throwing || loadout.Switching)) || (melee && melee.Busy))) blocked = true;
            if (blocked)
            {
                if (isPlayer) ProcAudio.Play(Sfx.Dry, 0.4f);
                return false;
            }
            var t = Type;
            if (!Execute(t)) return false;
            var info = AbilityDB.Get(t == AbilityType.ChickenTrap ? Shown : t);
            Cooldown = CooldownTotal = Mathf.Max(0.5f, info.cooldown);
            if (t == AbilityType.ChickenTrap) { Type = AbilityType.None; Shown = AbilityType.None; Cooldown = 0f; }
            else if (UsesLeft > 0 && --UsesLeft == 0) { Type = AbilityType.None; Shown = AbilityType.None; }
            return true;
        }

        bool Execute(AbilityType t)
        {
            var info = AbilityDB.Get(t);
            switch (t)
            {
                case AbilityType.Grapple: return StartGrapple();
                case AbilityType.Parry: StartParry(info.duration); return true;
                case AbilityType.ButtRocket: return StartRocket();
                case AbilityType.ChickenTrap: StartChicken(info.duration); return true;
                case AbilityType.Invisibility: StartInvisibility(info.duration); return true;
                case AbilityType.Phase: StartPhase(info.duration); return true;
                case AbilityType.FartBlind: Fart(true, 0f); return true;
                case AbilityType.FartSmoke: Fart(false, info.duration); return true;
                case AbilityType.Swap: return StartSwap();
            }
            return ExecuteExtra(t, info);
        }

        // ------------------------------------------------------------------ helpers

        Transform Cam => pcam ? pcam.cam.transform : null;

        Vector3 AimOrigin => isPlayer ? Cam.position : (rig ? rig.head.position + Vector3.up * 0.06f : transform.position + Vector3.up * 1.6f);

        Vector3 HandPoint => isPlayer ? Cam.TransformPoint(new Vector3(0.2f, -0.2f, 0.45f)) : (rig ? rig.lHand.position : AimOrigin);

        Vector3 ButtPoint => rig ? rig.hips.position - transform.forward * 0.22f + Vector3.up * 0.03f : transform.position + Vector3.up * 0.9f - transform.forward * 0.3f;

        Vector3 Facing
        {
            get
            {
                Vector3 f = isPlayer ? Cam.forward : transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.001f ? f.normalized : transform.forward;
            }
        }

        /// <summary>Bot aim direction at its current target with some error. False if no target.</summary>
        bool BotAim(out Vector3 dir, float errDeg)
        {
            dir = transform.forward;
            if (!bot || !bot.Target) return false;
            dir = (bot.Target.Chest - AimOrigin).normalized;
            dir = Quaternion.Euler(Random.Range(-errDeg, errDeg), Random.Range(-errDeg, errDeg), 0f) * dir;
            return true;
        }

        bool RaycastAim(Vector3 o, Vector3 d, float range, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(o, d, hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; hit = hits[i]; any = true; }
            }
            return any;
        }

        static Combatant CombatantOf(Collider c) => c ? c.GetComponentInParent<Combatant>() : null;

        float GroundY(Vector3 p, Transform ignore, float fallback)
        {
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 1.2f, Vector3.down, hits, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; float y = fallback;
            for (int i = 0; i < n; i++)
            {
                if (ignore && hits[i].collider.transform.IsChildOf(ignore)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; y = hits[i].point.y; }
            }
            return y;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (Remote) { RemoteUpdate(dt); return; }
            UpdateNetPull(dt);

            if (Cooldown > 0f)
            {
                Cooldown -= dt;
                if (Cooldown <= 0f && isPlayer && Type != AbilityType.None) ProcAudio.Play(Sfx.AbilityReady, 0.3f, 0f);
            }

            if (invisT > 0f) invisT -= dt;
            if (blindT > 0f) blindT -= dt;
            UpdateChicken(dt);
            UpdateParry(dt);
            UpdatePhase(dt);
            UpdateHook(dt);
            UpdateBotRocket(dt);
            UpdateSlip(dt);
            UpdateExtra(dt);

            if (bentPulse > 0f) bentPulse -= dt;
            if (rocketT < 0f) bent = Mathf.MoveTowards(bent, bentPulse > 0.35f || rocketAim ? 1f : 0f, dt * 4f);

            if (anim)
            {
                anim.bentOver = bent;
                anim.swordHold = Parrying;
                anim.parry = parryAnim;
                anim.parryVariant = parryVariant;
                anim.slip = slipAmt;
                anim.blind = Mathf.Clamp01(Mathf.Max(blindT, screamT) * 2f);
            }
            ApplyVisibility();

            if (isPlayer) UpdatePlayer(dt);
            else if (self && self.alive) BotThink(dt);
        }

        void UpdatePlayer(float dt)
        {
            if (self.alive)
            {
                if (rocketAim) UpdateRocketAim(dt);
                else if (GameInput.AbilityPressed && Type == AbilityType.ButtRocket) BeginRocketAim();
                else if (GameInput.AbilityPressed) { if (Building) EndBuild(); else TryActivate(); }
                DebugCycle();
                if (NearbyPickup && GameInput.PickupPressed && (!loadout || !loadout.LookedPickup) && !Busy) NearbyPickup.Take(this);
            }

            if (rocketAim && !self.alive) CancelRocketAim();
            bool lowerAll = Morphed || Parrying || Building || Minesweeping || rocketAim;
            if (rocketAim) { motor.SpeedMultiplier *= 0.5f; motor.BlockSprint = true; }
            WeaponController.AbilityLower = lowerAll ? 1f : 0f;
            if (loadout) loadout.ActionsBlocked = lowerAll;
            GameInput.Inverted = IsChicken || Confused;
            GameInput.Frozen = bsodT > 0f;
            motor.InputBlocked = slipT > 0f || Minesweeping;
            if (IsChicken) motor.SpeedMultiplier *= 1.25f;
            if (Shrunk) motor.SpeedMultiplier *= 1.1f;
            tpBlend = Mathf.MoveTowards(tpBlend, Morphed ? 1f : 0f, dt * 3f);
            pcam.ThirdPerson = tpBlend;
            pcam.SlipAmount = slipAmt;
            fovAdd = Anim.Damp(fovAdd, phaseT > 0f ? 12f : rocketAim ? -22f * rocketAimGrow : 0f, 6f, dt);
            pcam.FovAdd = fovAdd;
        }

        void ApplyVisibility()
        {
            bool flicker = phaseT > 0f && !isPlayer && Mathf.Repeat(Time.time * 9f, 1f) < 0.45f;
            bool bodyOn = !Morphed && !Invisible && !flicker;
            bool gunOn = !Morphed && !Parrying && !flicker;
            int key = (bodyOn ? 1 : 0) | (gunOn ? 2 : 0) | (flicker ? 4 : 0);
            if (key == visKey) return;
            visKey = key;
            if (rig)
            {
                foreach (var r in rig.renderers) if (r) r.enabled = bodyOn;
                if (rig.weaponSocket) foreach (Transform ch in rig.weaponSocket) ch.gameObject.SetActive(gunOn);
            }
            if (botSword) botSword.gameObject.SetActive(!flicker);
        }

        // ------------------------------------------------------------------ death / respawn

        public void OnDeath()
        {
            if (hookState != 0) { ReleaseVictim(false); CleanupHook(); }
            if (chickenT > 0f) EndChicken();
            if (parryT > 0f) EndParry();
            ResetExtra();
            CancelRocketAim();
            invisT = 0f; blindT = 0f; slipT = 0f; slipAmt = 0f; bent = 0f; bentPulse = 0f; parryAnim = -1f;
            if (mountedRocket) Destroy(mountedRocket.gameObject);
            mountedRocket = null;
            rocketT = -1f;
            if (bot) bot.YawOverride = null;
            if (phaseT > 0f || yankT >= 0f)
            {
                phaseT = 0f; yankT = -1f;
                if (isPlayer) motor.Noclip = false;
                else if (bot) bot.ExternalControl = false;
            }
            Type = AbilityType.None; Shown = AbilityType.None; Cooldown = 0f; NearbyPickup = null;
            if (isPlayer)
            {
                GameInput.Inverted = false;
                WeaponController.AbilityLower = 0f;
                if (loadout) loadout.ActionsBlocked = false;
                tpBlend = 0f; fovAdd = 0f;
                pcam.ThirdPerson = 0f; pcam.SlipAmount = 0f; pcam.FovAdd = 0f;
                motor.InputBlocked = false;
                if (motor.IsPulled) motor.StopPull();
            }
            visKey = -1;
            ApplyVisibility();
            if (anim) { anim.bentOver = 0f; anim.swordHold = false; anim.parry = -1f; anim.slip = 0f; anim.blind = 0f; }
        }

        public void OnSpawn() => OnDeath();

        // ------------------------------------------------------------------ status effects (inflicted)

        public void Blind(float t)
        {
            if (!self.alive) return;
            if (t > blindT) { blindT = t; blindTotal = t; }
            if (isPlayer) { ProcAudio.Play(Sfx.Hurt, 0.5f); pcam.Shake(0.4f); Say("Вас ослепило!", "Чем-то очень вонючим...", new Color(0.7f, 0.9f, 0.3f)); }
        }

        public void Slip()
        {
            if (!self.alive || slipT > 0f || Phasing) return;
            slipT = 2.4f;
            ProcAudio.PlayAt(Sfx.Slip, transform.position, 1f, 0.06f);
            if (isPlayer)
            {
                ProcAudio.Play(Sfx.Slip, 0.6f);
                if (motor.IsPulled) motor.StopPull();
                pcam.Shake(0.3f);
            }
            else if (puppet != null) puppet.Stun(2.4f);
        }

        void UpdateSlip(float dt)
        {
            if (slipT > 0f)
            {
                slipT -= dt;
                if (slipT > 0.75f) slipAmt = Mathf.MoveTowards(slipAmt, 1f, dt * 7f);
                else slipAmt = Mathf.MoveTowards(slipAmt, 0f, dt * 1.6f);
            }
            else slipAmt = Mathf.MoveTowards(slipAmt, 0f, dt * 2f);
        }

        // ------------------------------------------------------------------ invisibility

        void StartInvisibility(float dur)
        {
            invisT = invisDur = dur;
            ProcAudio.PlayAt(Sfx.Warp, transform.position, 0.6f, 0.05f);
            FX.Ensure().Puff(transform.position + Vector3.up, Vector3.up, 8, 0.9f, new Vector2(0.3f, 1.5f), new Vector2(0.4f, 0.9f), new Vector2(0.4f, 0.9f),
                             new Color(0.5f, 1f, 1f, 0.3f), new Color(0.8f, 1f, 1f, 0.2f));
            if (isPlayer) Say("Невидимость", "Тело невидимо, оружие — нет. Выстрелы выдают вас.", AbilityDB.Get(AbilityType.Invisibility).color);
        }

        // ------------------------------------------------------------------ chicken trap

        void StartChicken(float dur)
        {
            chickenT = chickenDur = dur;
            cluckT = 0.3f;
            if (!chicken) chicken = ChickenModel.Build(transform, !isPlayer);
            chicken.gameObject.SetActive(true);
            if (!isPlayer && rig) rig.SetCollidersEnabled(false);
            if (isPlayer && motor.IsPulled) motor.StopPull();
            Vector3 c = transform.position + Vector3.up * 0.8f;
            FX.Ensure().Puff(c, Vector3.up, 14, 1f, new Vector2(0.5f, 3f), new Vector2(0.5f, 1.2f), new Vector2(0.6f, 1.2f), new Color(1f, 1f, 1f, 0.6f), new Color(0.95f, 0.95f, 0.9f, 0.5f));
            FX.Ensure().Chunks(c, Vector3.up, 30, new Color(1f, 1f, 1f), new Color(0.95f, 0.92f, 0.85f), 4f);
            ProcAudio.PlayAt(Sfx.Trap, c, 1f, 0.02f);
            ProcAudio.PlayAt(Sfx.Cluck, c, 1f, 0.1f);
            if (isPlayer)
            {
                ProcAudio.Play(Sfx.Trap, 0.7f, 0f);
                Say("ЛОВУШКА!", "Вы — курица! Управление инвертировано.", new Color(1f, 0.85f, 0.3f));
            }
        }

        void UpdateChicken(float dt)
        {
            if (chickenT <= 0f) return;
            chickenT -= dt;
            cluckT -= dt;
            if (cluckT <= 0f)
            {
                cluckT = Random.Range(1.2f, 2.8f);
                ProcAudio.PlayAt(Sfx.Cluck, transform.position + Vector3.up * 0.6f, 0.9f, 0.12f);
            }
            if (chickenT <= 0f) EndChicken();
        }

        void EndChicken()
        {
            chickenT = 0f;
            if (chicken) chicken.gameObject.SetActive(false);
            if (!isPlayer && rig && self.alive) rig.SetCollidersEnabled(true);
            if (isPlayer) GameInput.Inverted = false;
            FX.Ensure().Puff(transform.position + Vector3.up * 0.8f, Vector3.up, 10, 1f, new Vector2(0.5f, 2.5f), new Vector2(0.5f, 1.1f), new Vector2(0.5f, 1f),
                             new Color(1f, 1f, 1f, 0.5f), new Color(0.9f, 0.9f, 0.9f, 0.4f));
        }

        // ------------------------------------------------------------------ parry sword

        void StartParry(float dur)
        {
            parryT = parryDur = dur;
            slashCd = 0.2f;
            if (isPlayer)
            {
                if (swordVM) Destroy(swordVM.gameObject);
                swordVM = SwordViewmodel.Create(Cam);
                Say("Меч парирования", "Пули спереди отражаются. ЛКМ — удар мечом.", AbilityDB.Get(AbilityType.Parry).color);
            }
            else if (rig)
            {
                botSword = AbilityProps.BuildSword(rig.rHand);
                botSword.localPosition = new Vector3(0f, -0.08f, 0.01f);
                botSword.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            ProcAudio.PlayAt(Sfx.KnifeSwing, transform.position + Vector3.up * 1.3f, 0.9f);
            ProcAudio.PlayAt(Sfx.Parry, transform.position + Vector3.up * 1.3f, 0.35f);
        }

        void EndParry()
        {
            parryT = 0f;
            parryAnim = -1f;
            if (swordVM) Destroy(swordVM.gameObject);
            if (botSword) Destroy(botSword.gameObject);
            swordVM = null; botSword = null;
            visKey = -1;
        }

        void UpdateParry(float dt)
        {
            if (parryAnim >= 0f) { parryAnim += dt / 0.3f; if (parryAnim >= 1f) parryAnim = -1f; }
            if (parryT <= 0f) return;
            parryT -= dt;
            slashCd -= dt;
            if (self.alive)
            {
                if (isPlayer)
                {
                    if (GameInput.FirePressed && slashCd <= 0f) PlayerSlash();
                }
                else if (bot && bot.Target && bot.Target.alive && slashCd <= 0f
                         && Vector3.Distance(bot.Target.transform.position, transform.position) < 2.4f)
                    BotSlash(bot.Target);
            }
            if (parryT <= 0f) EndParry();
        }

        void PlayParryAnim()
        {
            parryVariant = (parryVariant + Random.Range(1, 6)) % 6;
            parryAnim = 0f;
            if (swordVM) swordVM.Parry(parryVariant);
        }

        void PlayerSlash()
        {
            slashCd = 0.5f;
            if (swordVM) swordVM.Slash();
            parryVariant = 1; parryAnim = 0f;
            ProcAudio.Play(Sfx.KnifeSwing, 0.8f, 0.1f);
            pcam.Punch(new Vector3(4f, -6f, 8f));
            var ct = Cam;
            int n = Physics.SphereCastNonAlloc(ct.position, 0.25f, ct.forward, hits, 2.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit hit = default; float best = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance <= 0f) continue;
                if (hits[i].distance < best) { best = hits[i].distance; hit = hits[i]; any = true; }
            }
            if (!any) return;
            var hb = hit.collider.GetComponent<Hitbox>();
            IDamageable d = hb ? hb.Owner : hit.collider.GetComponent<IDamageable>();
            if (d != null)
            {
                var res = d.TakeDamage(new DamageInfo { amount = 65f, point = hit.point, normal = hit.normal, direction = ct.forward, attacker = self, weapon = "Меч" });
                if (res.applied && HUD.Instance) { HUD.Instance.ShowHitmarker(res.killed, false); HUD.Instance.RegisterHit(res.killed); }
                if (res.applied) ProcAudio.Play(res.killed ? Sfx.Kill : Sfx.KnifeHit, 0.7f);
            }
            if (hit.collider.GetComponentInParent<HumanoidRig>()) FX.Ensure().Blood(hit.point, hit.normal, 12);
            else { FX.Ensure().SparkBurst(hit.point, hit.normal, 10); ProcAudio.PlayAt(Sfx.KnifeClang, hit.point, 0.7f); }
        }

        void BotSlash(Combatant t)
        {
            slashCd = 0.9f;
            parryVariant = Random.Range(0, 6); parryAnim = 0f;
            ProcAudio.PlayAt(Sfx.KnifeSwing, transform.position + Vector3.up * 1.3f, 0.9f);
            var d = t.GetComponent<IDamageable>();
            if (d == null) return;
            Vector3 dir = (t.Chest - AimOrigin).normalized;
            d.TakeDamage(new DamageInfo { amount = 40f, point = t.Chest, normal = -dir, direction = dir, attacker = self, weapon = "Меч" });
            ProcAudio.PlayAt(Sfx.KnifeHit, t.Chest, 0.8f);
            FX.Ensure().Blood(t.Chest, -dir, 8);
        }

        /// <summary>Called by PlayerHealth / CombatBot before damage is applied. True = bullet bounced off the sword.</summary>
        public bool TryDeflect(DamageInfo info)
        {
            if (!Parrying || !AbilityDB.IsBullet(info.weapon)) return false;
            Vector3 inc = -info.direction; inc.y = 0f;
            if (inc.sqrMagnitude < 0.001f || Vector3.Dot(Facing, inc.normalized) < 0.25f) return false;

            PlayParryAnim();
            Vector3 blade = isPlayer ? (swordVM ? swordVM.BladePoint : Cam.position + Cam.forward * 0.6f)
                                     : (botSword ? botSword.TransformPoint(new Vector3(0f, 0.6f, 0f)) : AimOrigin + transform.forward * 0.6f);
            FX.Ensure().SparkBurst(blade, -info.direction, 16, 10f);
            ProcAudio.PlayAt(Sfx.Parry, blade, 0.9f, 0.05f);
            if (isPlayer) { ProcAudio.Play(Sfx.Parry, 0.5f, 0.08f); pcam.Punch(new Vector3(-3f, Random.Range(-4f, 4f), Random.Range(-6f, 6f))); }

            // ricochet: sometimes right back into the shooter
            var shooter = info.attacker;
            Vector3 end;
            if (shooter && shooter != self && shooter.alive && Random.value < 0.45f)
            {
                end = shooter.Chest;
                Vector3 dir = end - blade;
                bool clear = !RaycastAim(blade, dir.normalized, dir.magnitude - 0.3f, out RaycastHit block) || block.collider.transform.IsChildOf(shooter.transform);
                if (clear)
                {
                    var d = shooter.GetComponent<IDamageable>();
                    if (d != null)
                    {
                        var res = d.TakeDamage(new DamageInfo { amount = Mathf.Round(info.amount * 0.8f), point = end, normal = -dir.normalized, direction = dir.normalized, attacker = self, weapon = "Рикошет" });
                        if (res.applied && isPlayer && HUD.Instance) { HUD.Instance.ShowHitmarker(res.killed, false); HUD.Instance.RegisterHit(res.killed); ProcAudio.Play(res.killed ? Sfx.Kill : Sfx.Hit, 0.5f); }
                    }
                    if (shooter.GetComponentInChildren<HumanoidRig>()) FX.Ensure().Blood(end, -dir.normalized, 6);
                }
                else end = block.point;
            }
            else
            {
                Vector3 r = Vector3.Reflect(info.direction, Random.onUnitSphere).normalized;
                if (Vector3.Dot(r, info.direction) > 0f) r = -r;
                r = (r + Vector3.up * Random.Range(0f, 0.6f)).normalized;
                end = blade + r * 40f;
                if (RaycastAim(blade, r, 40f, out RaycastHit h))
                {
                    end = h.point;
                    var tag = h.collider.GetComponentInParent<SurfaceTag>();
                    FX.Ensure().Impact(h, tag ? tag.kind : SurfaceKind.Concrete, h.rigidbody == null && h.collider.GetComponent<IDamageable>() == null);
                }
            }
            FX.Ensure().Tracer(blade, end);
            return true;
        }

        public void OnParriedProjectile(Vector3 pos)
        {
            PlayParryAnim();
            FX.Ensure().SparkBurst(pos, Vector3.up, 25, 12f);
            ProcAudio.PlayAt(Sfx.Parry, pos, 1f, 0.05f);
            if (isPlayer) Say("ОТБИТО!", "Ракета летит обратно", AbilityDB.Get(AbilityType.Parry).color);
        }

        // ------------------------------------------------------------------ "grenade launcher"

        /// <summary>Player pressed V: crouch over, the charge pokes out of the butt, the view zooms in for aiming.</summary>
        void BeginRocketAim()
        {
            bool blocked = Cooldown > 0f || Busy || slipT > 0f || motor.Locked
                           || (loadout && (loadout.Throwing || loadout.Switching)) || (melee && melee.Busy);
            if (blocked) { ProcAudio.Play(Sfx.Dry, 0.4f); return; }
            rocketAim = true;
            rocketAimGrow = 0f;
            aimRocket = RocketProjectile.Create(1.1f);
            // like the rest of the local body: seen by others, only the shadow for yourself
            foreach (var r in aimRocket.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            PlaceAimRocket();
            ProcAudio.Play(Sfx.Fart, 0.25f, 0.15f);
            ProcAudio.Play(Sfx.Equip, 0.5f);
            Say("Гранатомёт", "Целься — отпусти V для выстрела", AbilityDB.Get(AbilityType.ButtRocket).color);
        }

        void PlaceAimRocket()
        {
            if (!aimRocket) return;
            Vector3 back = -transform.forward;
            aimRocket.transform.position = ButtPoint + back * 0.04f;
            aimRocket.transform.rotation = Quaternion.LookRotation((back + Vector3.up * 0.3f).normalized);
            aimRocket.transform.localScale = Vector3.one * Mathf.Lerp(0.1f, 1f, Anim.Smooth(rocketAimGrow)) * (1f + Mathf.Sin(Time.time * 35f) * 0.02f);
        }

        void UpdateRocketAim(float dt)
        {
            rocketAimGrow = Mathf.MoveTowards(rocketAimGrow, 1f, dt / 0.45f);
            PlaceAimRocket();
            if (!GameInput.AbilityHeld) FireAimedRocket();
        }

        void FireAimedRocket()
        {
            if (aimRocket) Destroy(aimRocket.gameObject);
            aimRocket = null;
            rocketAim = false;
            if (!self.alive || Type != AbilityType.ButtRocket) return;
            StartRocket();
            var info = AbilityDB.Get(AbilityType.ButtRocket);
            Cooldown = CooldownTotal = Mathf.Max(0.5f, info.cooldown);
            if (UsesLeft > 0 && --UsesLeft == 0) { Type = AbilityType.None; Shown = AbilityType.None; }
            bentPulse = 0.6f;
        }

        void CancelRocketAim()
        {
            if (aimRocket) Destroy(aimRocket.gameObject);
            aimRocket = null;
            rocketAim = false;
        }

        bool StartRocket()
        {
            if (isPlayer)
            {
                // the player just sees a charge leaving the crosshair...
                var ct = Cam;
                var r = RocketProjectile.Create(0.55f);
                r.transform.position = ct.position + ct.forward * 0.9f - ct.up * 0.12f;
                r.Launch(ct.forward, 34f, self, transform);
                // others see it leave the butt
                Vector3 rp = ButtPoint - transform.forward * 0.1f, rd = ct.forward;
                SendEvent(AbilityNetEvent.Rocket, w => { W(w, rp); W(w, rd); w.Write(34f); w.Write(0.9f); });
                // ...while the body (shadow, other players) bends over and fires it from the butt
                bentPulse = 1.1f;
                pcam.Punch(new Vector3(-14f, Random.Range(-3f, 3f), Random.Range(-6f, 6f)));
                pcam.Shake(0.35f);
                ProcAudio.Play(Sfx.Fart, 0.3f, 0.1f);
                return true;
            }
            if (!bot || !bot.Target) return false;
            rocketT = 0f;
            rocketTarget = bot.Target;
            mountedRocket = RocketProjectile.Create(1.3f);
            mountedRocket.transform.position = ButtPoint;
            mountedRocket.transform.localScale = Vector3.one * 0.05f;
            ProcAudio.PlayAt(Sfx.Fart, ButtPoint, 0.7f, 0.15f);
            return true;
        }

        void UpdateBotRocket(float dt)
        {
            if (rocketT < 0f) return;
            rocketT += dt;
            Vector3 tp = rocketTarget && rocketTarget.alive ? rocketTarget.Chest : transform.position - transform.forward * 10f + Vector3.up;
            Vector3 away = transform.position - tp; away.y = 0f;
            if (away.sqrMagnitude > 0.01f && bot) bot.YawOverride = Quaternion.LookRotation(away).eulerAngles.y;

            const float launchAt = 1.05f;
            bent = rocketT < launchAt ? Anim.Smooth(rocketT / 0.4f) : 1f - Anim.Smooth((rocketT - launchAt - 0.15f) / 0.4f);

            if (mountedRocket && !mountedRocket.Flying)
            {
                float grow = Anim.Smooth((rocketT - 0.3f) / 0.65f);
                Vector3 butt = ButtPoint;
                Vector3 back = -transform.forward;
                Vector3 aimDir = (tp - butt).normalized;
                mountedRocket.transform.position = butt + back * 0.04f;
                mountedRocket.transform.rotation = Quaternion.LookRotation(Vector3.Slerp((back + Vector3.up * 0.25f).normalized, aimDir, grow * 0.8f));
                mountedRocket.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 1f, grow) * (1f + Mathf.Sin(rocketT * 40f) * 0.03f);
                if (rocketT >= launchAt)
                {
                    Vector3 dir = Quaternion.Euler(Random.Range(-2.5f, 2.5f), Random.Range(-2.5f, 2.5f), 0f) * aimDir;
                    mountedRocket.Launch(dir, 26f, self, transform);
                    Vector3 rp = mountedRocket.transform.position;
                    SendEvent(AbilityNetEvent.Rocket, w => { W(w, rp); W(w, dir); w.Write(26f); w.Write(1.3f); });
                    mountedRocket = null;
                    FX.Ensure().Puff(butt, back, 10, 0.5f, new Vector2(1f, 3f), new Vector2(0.6f, 1.2f), new Vector2(0.6f, 1.2f),
                                     new Color(0.8f, 0.78f, 0.75f, 0.5f), new Color(0.6f, 0.6f, 0.6f, 0.4f));
                }
            }
            if (rocketT > launchAt + 0.6f)
            {
                rocketT = -1f;
                bent = 0f;
                if (bot) bot.YawOverride = null;
            }
        }

        // ------------------------------------------------------------------ phase through walls

        void StartPhase(float dur)
        {
            phaseT = phaseDur = dur;
            phaseStart = transform.position;
            yankT = -1f;
            if (isPlayer)
            {
                if (motor.IsPulled) motor.StopPull();
                motor.Noclip = true;
                Say("Сквозь стены", "Летите куда смотрите. Через 6 сек. вас вернёт обратно!", AbilityDB.Get(AbilityType.Phase).color);
            }
            else if (bot)
            {
                phaseDest = bot.Target ? bot.Target.transform.position : bot.LastKnown;
                bot.ExternalControl = true;
            }
            ProcAudio.PlayAt(Sfx.Warp, transform.position + Vector3.up, 0.9f, 0.05f);
            FX.Ensure().Puff(transform.position + Vector3.up, Vector3.up, 10, 1f, new Vector2(0.5f, 2f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                             new Color(0.75f, 0.5f, 1f, 0.45f), new Color(0.5f, 0.3f, 0.9f, 0.35f));
        }

        void UpdatePhase(float dt)
        {
            if (phaseT > 0f)
            {
                phaseT -= dt;
                if (!isPlayer && bot)
                {
                    Vector3 goal = bot.Target && bot.Target.alive ? bot.Target.transform.position : phaseDest;
                    Vector3 to = goal - transform.position; to.y = 0f;
                    if (to.magnitude > 1.2f)
                    {
                        transform.position += to.normalized * 3.6f * dt;
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), dt * 6f);
                    }
                }
                if (phaseT <= 0f)
                {
                    // time is up: snap back to where it started
                    phaseT = 0f;
                    yankFrom = transform.position;
                    yankT = 0f;
                    if (isPlayer) { motor.Noclip = false; motor.Locked = true; ProcAudio.Play(Sfx.Warp, 0.7f); }
                    ProcAudio.PlayAt(Sfx.Warp, transform.position, 0.9f, 0.05f);
                    visKey = -1;
                }
            }
            else if (yankT >= 0f)
            {
                yankT += dt / 0.45f;
                float e = Anim.Smooth(Mathf.Clamp01(yankT));
                transform.position = Vector3.Lerp(yankFrom, phaseStart, e) + Vector3.up * Mathf.Sin(e * Mathf.PI) * 0.6f;
                if (yankT >= 1f)
                {
                    yankT = -1f;
                    if (isPlayer)
                    {
                        motor.Locked = false;
                        motor.Teleport(phaseStart + Vector3.up * 0.02f);
                        pcam.Shake(0.4f);
                    }
                    else if (bot) bot.ExternalControl = false;
                    FX.Ensure().Puff(phaseStart + Vector3.up, Vector3.up, 12, 1f, new Vector2(0.5f, 2.5f), new Vector2(0.5f, 1.1f), new Vector2(0.5f, 1f),
                                     new Color(0.75f, 0.5f, 1f, 0.5f), new Color(0.5f, 0.3f, 0.9f, 0.4f));
                    visKey = -1;
                }
            }
        }

        // ------------------------------------------------------------------ farts

        void Fart(bool blind, float smokeTime)
        {
            Vector3 o = ButtPoint;
            Vector3 back = -transform.forward;
            ProcAudio.PlayAt(Sfx.Fart, o, 1f, 0.12f);
            if (isPlayer) { ProcAudio.Play(Sfx.Fart, 0.55f, 0.12f); pcam.Shake(0.25f); }
            AIEvents.Emit(o, 30f, self);

            // identical puff for both kinds — you only find out which one it was afterwards
            Color a = new Color(0.55f, 0.62f, 0.2f, 0.75f), b = new Color(0.5f, 0.42f, 0.2f, 0.65f);
            FX.Ensure().Puff(o, back, 18, 0.55f, new Vector2(1.5f, 5f), new Vector2(0.8f, 1.8f), new Vector2(1.2f, 2.4f), a, b);
            FX.Ensure().Puff(o, Vector3.up, 8, 0.9f, new Vector2(0.3f, 1.5f), new Vector2(1.2f, 2.4f), new Vector2(1.4f, 2.6f), a, b);

            SendEvent(AbilityNetEvent.Fart, w => { w.Write(blind); w.Write(smokeTime); });
            if (blind && Remote) return; // the caster's machine decides who got blinded
            if (blind)
            {
                foreach (var c in Combatant.All)
                {
                    if (!c || !c.alive || c == self || !self.IsEnemyOf(c) || !c.abilities) continue;
                    float d = Vector3.Distance(c.Head, o);
                    if (d > 7.5f) continue;
                    Vector3 to = c.Head - (o + Vector3.up * 0.3f);
                    if (RaycastAim(o + Vector3.up * 0.3f, to.normalized, to.magnitude, out RaycastHit h) && !h.collider.transform.IsChildOf(c.transform)) continue;
                    float bt = Mathf.Lerp(5.5f, 3f, d / 7.5f);
                    var cc = c;
                    EffectOn(c, AbilityNetEffect.Blind, w => w.Write(bt), () => cc.abilities.Blind(bt));
                }
            }
            else SmokeCloud.Spawn(o, 6f, Mathf.Max(6f, smokeTime));
        }

        // ------------------------------------------------------------------ swap

        bool StartSwap()
        {
            Vector3 o = AimOrigin;
            Vector3 dir;
            if (isPlayer) dir = Cam.forward;
            else if (!BotAim(out dir, 1f)) return false;
            Combatant victim = null;
            if (RaycastAim(o, dir, 70f, out RaycastHit hit)) victim = CombatantOf(hit.collider);
            if (!victim || victim == self || !victim.alive || (victim.abilities && victim.abilities.Phasing))
            {
                if (isPlayer) Say("Нет цели", "Наведите прицел на врага", new Color(1f, 0.4f, 0.4f));
                return false;
            }
            Vector3 myPos = transform.position, theirPos = victim.transform.position;
            Teleport(this, theirPos);
            if (victim.abilities) EffectOn(victim, AbilityNetEffect.Teleport, w => W(w, myPos), () => Teleport(victim.abilities, myPos));
            SendEvent(AbilityNetEvent.Swap, w => { W(w, myPos); W(w, theirPos); });
            foreach (var p in new[] { myPos, theirPos })
            {
                FX.Ensure().Puff(p + Vector3.up, Vector3.up, 12, 1f, new Vector2(0.5f, 2.5f), new Vector2(0.5f, 1.1f), new Vector2(0.5f, 1f),
                                 new Color(1f, 0.4f, 0.85f, 0.5f), new Color(0.9f, 0.3f, 0.7f, 0.4f));
                ProcAudio.PlayAt(Sfx.Warp, p + Vector3.up, 0.9f, 0.05f);
            }
            if (isPlayer) ProcAudio.Play(Sfx.Warp, 0.5f);
            if (victim.isPlayer) ProcAudio.Play(Sfx.Warp, 0.5f);
            return true;
        }

        static void Teleport(AbilityUser u, Vector3 pos)
        {
            if (u.isPlayer)
            {
                if (u.motor.IsPulled) u.motor.StopPull();
                u.motor.Teleport(pos + Vector3.up * 0.05f);
            }
            else if (u.puppet != null)
            {
                bool was = u.puppet.ExternalControl;
                u.puppet.ExternalControl = true;
                u.transform.position = pos;
                if (!was) u.puppet.ExternalControl = false;
            }
        }

        // ------------------------------------------------------------------ grappling hook

        bool StartGrapple()
        {
            Vector3 o = AimOrigin;
            Vector3 dir;
            if (isPlayer) dir = Cam.forward;
            else if (!BotAim(out dir, 1.5f)) return false;

            hookVictim = null;
            hookHasHit = false;
            if (RaycastAim(o, dir, 45f, out RaycastHit hit))
            {
                hookEnd = hit.point;
                hookHasHit = true;
                var c = CombatantOf(hit.collider);
                if (c && c != self && c.alive && !(c.abilities && c.abilities.Phasing)) hookVictim = c;
            }
            else hookEnd = o + dir * 45f;

            hookPos = HandPoint;
            hookState = 1;
            hookTimer = 0f;
            EnsureHookVisuals();
            ProcAudio.PlayAt(Sfx.HookFire, hookPos, 0.9f, 0.06f);
            if (isPlayer) ProcAudio.Play(Sfx.HookFire, 0.5f);
            var hv = hookVictim; var he = hookEnd; bool hh = hookHasHit;
            SendEvent(AbilityNetEvent.Grapple, w => { W(w, he); w.Write((short)(hv ? hv.netId : 0)); w.Write(hh); });
            return true;
        }

        void EnsureHookVisuals()
        {
            if (!ropeMat) ropeMat = FX.MakeParticleMat("Rope", Texture2D.whiteTexture, false, new Color(0.25f, 0.2f, 0.15f, 1f));
            if (!rope)
            {
                var go = new GameObject("GrappleRope");
                rope = go.AddComponent<LineRenderer>();
                rope.sharedMaterial = ropeMat;
                rope.positionCount = 2;
                rope.useWorldSpace = true;
                rope.widthMultiplier = 0.025f;
                rope.numCapVertices = 2;
                rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (!hookModel) hookModel = AbilityProps.BuildHook(null);
            rope.enabled = true;
            hookModel.gameObject.SetActive(true);
        }

        void CleanupHook()
        {
            hookState = 0;
            if (rope) Destroy(rope.gameObject);
            if (hookModel) Destroy(hookModel.gameObject);
            rope = null; hookModel = null;
        }

        void UpdateHook(float dt)
        {
            if (hookState == 0) return;
            Vector3 hand = HandPoint;
            switch (hookState)
            {
                case 1:
                {
                    Vector3 target = hookVictim ? hookVictim.Chest : hookEnd;
                    hookPos = Vector3.MoveTowards(hookPos, target, 80f * dt);
                    if ((hookPos - target).sqrMagnitude < 0.01f)
                    {
                        if (hookVictim && hookVictim.alive)
                        {
                            hookState = 3;
                            hookTimer = 0f;
                            ProcAudio.PlayAt(Sfx.HookHit, hookPos, 1f);
                            ProcAudio.PlayAt(Sfx.Reel, hand, 0.8f);
                            if (!Remote) GrabVictim();
                        }
                        else if (hookHasHit && (isPlayer || Remote))
                        {
                            hookState = 2;
                            hookTimer = 0f;
                            ProcAudio.PlayAt(Sfx.HookHit, hookPos, 1f);
                            if (isPlayer)
                            {
                                ProcAudio.Play(Sfx.Reel, 0.7f);
                                motor.StartPull(() => hookEnd, 24f, 2.5f, null);
                            }
                        }
                        else
                        {
                            if (hookHasHit) ProcAudio.PlayAt(Sfx.HookHit, hookPos, 0.6f);
                            else if (Cooldown > 2f) Cooldown = 2f; // complete miss: quick retry
                            hookState = 4;
                        }
                    }
                    break;
                }
                case 2:
                    hookPos = hookEnd;
                    hookTimer += dt;
                    if (Remote) { if ((transform.position - hookEnd).sqrMagnitude < 1.6f * 1.6f || hookTimer > 2.6f) hookState = 4; }
                    else if (!motor.IsPulled) hookState = 4;
                    break;
                case 3:
                {
                    hookTimer += dt;
                    if (!hookVictim || !hookVictim.alive || !self.alive) { ReleaseVictim(false); hookState = 4; break; }
                    hookPos = hookVictim.Chest;
                    if (Remote || hookVictim.remote)
                    {
                        // the victim is dragged on its own machine; here the rope just follows it
                        Vector3 gap = hookVictim.transform.position - transform.position; gap.y = 0f;
                        if (hookTimer > 2.5f || gap.magnitude < 1.9f) { ReleaseVictim(true); hookState = 4; }
                        break;
                    }
                    var vb = hookVictim.GetComponent<IAbilityPuppet>();
                    if (vb != null)
                    {
                        Vector3 dest = transform.position + Facing * 1.5f;
                        Vector3 cur = vb.transform.position;
                        Vector3 to = dest - cur; to.y = 0f;
                        if (to.magnitude < 0.4f || hookTimer > 2.2f) { ReleaseVictim(true); hookState = 4; break; }
                        Vector3 np = cur + to.normalized * Mathf.Min(17f * dt, to.magnitude);
                        float gy = GroundY(np, vb.transform, cur.y);
                        np.y = Mathf.Lerp(cur.y, Mathf.Max(gy, cur.y - 0.5f) + 0.25f * Mathf.Sin(Mathf.Clamp01(hookTimer * 2f) * Mathf.PI), 12f * dt);
                        vb.transform.position = np;
                    }
                    else if (hookVictim.abilities && hookVictim.abilities.motor)
                    {
                        if (!hookVictim.abilities.motor.IsPulled || hookTimer > 2.5f) { ReleaseVictim(false); hookState = 4; }
                    }
                    else { hookState = 4; }
                    break;
                }
                case 4:
                    hookPos = Vector3.MoveTowards(hookPos, hand, 90f * dt);
                    if ((hookPos - hand).sqrMagnitude < 0.04f) { CleanupHook(); return; }
                    break;
            }
            if (rope)
            {
                rope.SetPosition(0, hand);
                rope.SetPosition(1, hookPos);
            }
            if (hookModel)
            {
                hookModel.position = hookPos;
                Vector3 d = hookPos - hand;
                if (d.sqrMagnitude > 0.001f) hookModel.rotation = Quaternion.LookRotation(d);
            }
        }

        void GrabVictim()
        {
            if (hookVictim.remote) { EffectOn(hookVictim, AbilityNetEffect.Pull, w => { }, null); return; }
            var vb = hookVictim.GetComponent<IAbilityPuppet>();
            if (vb != null) { vb.ExternalControl = true; return; }
            var vu = hookVictim.abilities;
            if (vu && vu.motor)
            {
                var me = self;
                vu.motor.StartPull(() => me ? me.Chest : vu.transform.position, 16f, 2.2f, null);
                if (vu.isPlayer) { vu.pcam.Shake(0.4f); ProcAudio.Play(Sfx.HookHit, 0.7f); }
            }
        }

        void ReleaseVictim(bool stun)
        {
            if (!hookVictim) return;
            if (Remote) { hookVictim = null; return; }
            if (hookVictim.remote)
            {
                if (hookState == 3) EffectOn(hookVictim, AbilityNetEffect.PullEnd, w => w.Write(stun), null);
                hookVictim = null;
                return;
            }
            var vb = hookVictim.GetComponent<IAbilityPuppet>();
            if (vb != null && vb.ExternalControl && hookState == 3)
            {
                vb.ExternalControl = false;
                if (stun && hookVictim.alive) vb.Stun(0.9f);
            }
            var vu = hookVictim.abilities;
            if (vu && vu.motor && vu.motor.IsPulled && hookState == 3) vu.motor.StopPull();
            hookVictim = null;
        }

        // ------------------------------------------------------------------ bots

        /// <summary>Called by CombatBot before its own AI. True = the ability drives the bot this frame.</summary>
        public bool BotOverride(float dt)
        {
            if (!bot) return false;
            var ag = bot.Agent;
            if (IsChicken || Confused)
            {
                if (ag.enabled)
                {
                    ag.isStopped = false;
                    ag.speed = Confused ? 4.5f : 6.5f;
                    if (!ag.pathPending && (!ag.hasPath || ag.remainingDistance < 1f))
                    {
                        Vector2 r = Random.insideUnitCircle * 9f;
                        if (NavMesh.SamplePosition(transform.position + new Vector3(r.x, 0f, r.y), out var nh, 4f, NavMesh.AllAreas)) ag.SetDestination(nh.position);
                    }
                }
                return true;
            }
            if (rocketT >= 0f)
            {
                if (ag.enabled) ag.isStopped = true;
                return true;
            }
            return false;
        }

        void BotThink(float dt)
        {
            thinkT -= dt;
            if (thinkT > 0f) return;
            thinkT = Random.Range(0.25f, 0.5f);
            if (!bot || Type == AbilityType.None || Cooldown > 0f || Busy || Blinded || slipT > 0f || bot.ExternalControl) return;

            var t = bot.Target;
            bool vis = t && bot.TargetVisible;
            float dist = t ? Vector3.Distance(t.transform.position, transform.position) : 999f;
            // a bot believes the disguise, just like a player would
            AbilityType logic = Type == AbilityType.ChickenTrap ? Shown : Type;
            bool use = false;
            switch (logic)
            {
                case AbilityType.Grapple: use = vis && dist > 7f && dist < 35f && Random.value < 0.5f; break;
                case AbilityType.Parry: use = vis && bot.IsEngaging && dist < 28f && (bot.Health01 < 0.85f || Random.value < 0.25f); break;
                case AbilityType.ButtRocket: use = vis && dist > 8f && dist < 45f && Random.value < 0.5f; break;
                case AbilityType.Invisibility: use = vis ? Random.value < 0.25f : Random.value < 0.08f; break;
                case AbilityType.Phase: use = t && !vis && bot.IsSearching && dist < 25f && Random.value < 0.5f; break;
                case AbilityType.FartBlind:
                case AbilityType.FartSmoke: use = vis && (dist < 6.5f || (bot.Health01 < 0.4f && Random.value < 0.3f)); break;
                case AbilityType.Swap: use = vis && dist > 12f && Random.value < 0.25f; break;
            }
            if (logic == AbilityType.ChickenTrap) use = vis && Random.value < 0.3f;
            if (!use) use = BotWantsExtra(logic, t, vis, dist);
            if (use) TryActivate();
        }
    }
}
