using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FPS
{
    public enum WeaponSlot { Primary, Secondary }
    public enum ReloadStyle { Magazine, PerShell }

    /// <summary>
    /// Generic hitscan weapon with fully procedural first-person animation:
    /// ADS with collimated reticle / scope overlay, sway, bob, sprint / prone / vault / wall-tuck poses,
    /// spring recoil, pump action, magazine or shell-by-shell reloads, equip / holster.
    /// Visual model is any hierarchy with the named anchor transforms (swap for Asset Store models).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class WeaponController : MonoBehaviour
    {
        [Header("Identity")]
        public string weaponName = "M4A1";
        public WeaponSlot slot = WeaponSlot.Primary;

        [Header("Refs")]
        public PlayerMotor motor;
        public PlayerCamera playerCam;
        public Camera cam;
        public Transform model;
        public Transform muzzle;
        public Transform ejectPort;
        public Transform sightPoint;
        public Transform reticle;
        public Transform magazine;
        public Transform chargingHandle;
        public Transform bolt;
        public Transform leftHand;
        public Transform leftHandGrip;
        public Transform loadPort;
        public GameObject muzzleFlash;
        public Light muzzleLight;

        [Header("Stats")]
        public float damage = 26f;
        public float rpm = 700f;
        public int magSize = 30;
        public int reserveAmmo = 210;
        public float range = 300f;
        public float impactForce = 4f;
        public bool fullAuto = true;
        public bool allowFireModeSwitch = true;
        public int pellets = 1;
        public float pelletSpread = 0f;
        public float moveSpeedMul = 1f;
        public float falloffStart = 35f, falloffEnd = 120f, falloffMin = 0.7f;

        [Header("Spread (degrees)")]
        public float hipSpread = 1.3f;
        public float adsSpread = 0.06f;
        public float moveSpreadAdd = 1.8f;
        public float airSpreadAdd = 4f;
        public float bloomPerShot = 0.32f;
        public float maxBloom = 2.6f;
        public float bloomRecovery = 5f;

        [Header("Recoil")]
        public float recoilUp = 0.85f;
        public float recoilSide = 0.38f;
        public float adsRecoilMul = 0.62f;
        public float crouchRecoilMul = 0.8f;
        public float kickScale = 1f;
        public float punchScale = 1f;

        [Header("Poses")]
        public Vector3 hipPosition = new Vector3(0.155f, -0.175f, 0.33f);
        public float aimDistance = 0.21f;
        public float aimTime = 0.2f;
        public float adsFov = 46f;
        public bool scoped;
        public Vector3 sprintPos = new Vector3(-0.05f, -0.055f, -0.05f);
        public Vector3 sprintRot = new Vector3(16f, -42f, 22f);

        [Header("Reload")]
        public ReloadStyle reloadStyle = ReloadStyle.Magazine;
        public float reloadTime = 2.1f;
        public float emptyReloadTime = 2.65f;
        public float shellInsertTime = 0.48f;
        public Vector3 magGripOffset = new Vector3(0f, -0.095f, 0.01f);
        public float chTravel = 0.085f;

        [Header("Action")]
        public bool pumpAction;
        /// <summary>Bolt action (uses the pump cycle): the bolt goes back after every shot, the support hand stays, the scope drops.</summary>
        public bool boltAction;
        public float pumpTime = 0.5f;
        public float shellScale = 1f;

        [Header("Audio")]
        public Sfx shotSfx = Sfx.Shot;
        public float shotVolume = 0.8f;
        public float shotPitch = 1f;

        [Header("Equip")]
        public float equipTime = 0.38f;

        // ---- public state
        public int Ammo => ammo;
        public int Reserve => reserve;
        public bool IsReloading => reloading;
        public float AimBlend => aimEased;
        public float CurrentSpread { get; private set; }
        public bool FullAuto => fullAuto;
        public bool IsBusy => reloading || pumping || holstering || equipT < 1f;
        public bool IsScoped => scopeOn;
        /// <summary>0..1, lowers the weapon out of view (knife, grenade, executions).</summary>
        public float ExternalLower { get; set; }
        /// <summary>0..1, lowers every first-person weapon (abilities: sword, chicken). Only the local player has viewmodels.</summary>
        public static float AbilityLower;

        // ---- FPS Animation Framework (FPArms): the holder sits on the rig's weapon bone and the framework's layers do
        // ADS / sway / recoil / wall collision; this controller only reports its gameplay pose offsets (camera space)
        /// <summary>Set by FPArms: don't move the holder, publish PoseOffsetPos / PoseOffsetRot instead.</summary>
        public bool AnimDriven { get; set; }
        public Vector3 PoseOffsetPos { get; private set; }
        public Vector3 PoseOffsetRot { get; private set; }
        /// <summary>Aim input this frame (ADS allowed and held).</summary>
        public bool WantAim { get; private set; }
        public float AimTime => aimTime;
        /// <summary>Fired after every shot (FPArms plays the recoil animation and camera shake).</summary>
        public event Action Fired;

        int ammo, reserve;
        bool initialized;
        float nextFire, lastShotTime;
        int shotIndex, shotsSinceSmoke;
        float recoilSeed, bloom;
        float aim, aimEased;
        float sprintBlend, vaultBlend, crouchBlend, proneBlend, slideBlend, wallTuck, bobWeight, boltT, lowerBlend;
        float flashTimer;
        Vector3 aimPosition;
        bool scopeOn;
        Renderer[] renderers;

        float equipT = 1f, holsterT;
        bool holstering;
        Action onHolstered;

        readonly Spring3 kickPos = new Spring3(340f, 24f);
        readonly Spring3 kickRot = new Spring3(300f, 21f);
        readonly Spring3 swayRot = new Spring3(160f, 15f);
        readonly Spring3 swayPos = new Spring3(170f, 16f);
        readonly Spring3 bumpPos = new Spring3(130f, 11f);
        readonly Spring3 bumpRot = new Spring3(120f, 10f);

        Vector3 magRest, chRest, boltRest, handRest, reticleRest, reticleCenter;
        bool reloading, reloadEmpty, magHidden;
        float reloadT, reloadDuration;
        int evtIndex;
        ReloadAnim tacAnim, emptyAnim;

        // shell-by-shell
        int shellPhase; // 0 start, 1 insert, 2 end
        bool shellInterrupt, shellInserted;
        Vector3 shellH0, shellPort;

        // pump
        bool pumping, pumpEjected, pumpBackPlayed, pumpFwdPlayed;
        float pumpT;

        enum Evt { MagOut, Drop, Show, MagIn, BoltPull, BoltRelease, Grab }

        class ReloadAnim
        {
            public KeyFrame[] wPos, wRot, mag, hand, ch;
            public (float t, Evt e)[] events;
        }

        // ------------------------------------------------------------------ lifecycle

        void Awake() => Init();

        /// <summary>Initializes an inactive weapon (imported mesh, grips) — world copies are cloned from its model.</summary>
        public void EnsureInit() => Init();

        void Init()
        {
            if (initialized) return;
            initialized = true;
            if (!model && transform.childCount > 0) model = transform.GetChild(0);
            ImportedWeapons.Apply(this); // imported gun mesh over the procedural one (if the pack is present)
            HandGrips.Ensure(this);      // palm anchors for the IK hands (first-person arms, third-person bodies)
            ammo = magSize;
            reserve = reserveAmmo;
            recoilSeed = Random.value * 100f;
            if (magazine) magRest = magazine.localPosition;
            if (chargingHandle) chRest = chargingHandle.localPosition;
            if (bolt) boltRest = bolt.localPosition;
            if (leftHand) handRest = leftHand.localPosition;
            if (reticle)
            {
                reticleRest = reticle.localPosition;
                // the visible mark's center (imported reticles have their pivot far from the mesh)
                var rmf = reticle.GetComponent<MeshFilter>();
                reticleCenter = rmf && rmf.sharedMesh ? rmf.sharedMesh.bounds.center : Vector3.zero;
            }

            // place the holder so the sight sits exactly in screen center when aiming
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            Vector3 s = transform.parent.InverseTransformPoint(sightPoint.position);
            aimPosition = new Vector3(-s.x, -s.y, aimDistance - s.z);
            transform.localPosition = hipPosition;

            Vector3 grip = leftHandGrip ? model.InverseTransformPoint(leftHandGrip.position) : Vector3.zero;
            Vector3 G = magazine && reloadStyle == ReloadStyle.Magazine
                ? model.InverseTransformPoint(magazine.TransformPoint(magGripOffset)) - grip
                : new Vector3(0.005f, -0.115f, -0.215f);
            Vector3 CH = chargingHandle ? model.InverseTransformPoint(chargingHandle.position) - grip + new Vector3(-0.03f, 0.035f, 0f) : new Vector3(-0.03f, 0.1f, -0.4f);
            BuildAnimations(G, CH);
            if (loadPort)
            {
                shellPort = model.InverseTransformPoint(loadPort.position) - grip;
                shellH0 = shellPort + new Vector3(-0.04f, -0.09f, -0.02f);
            }
            renderers = model.GetComponentsInChildren<Renderer>(true);
            // the builder bakes the flash enabled; only a shot may show it (weapons never held before stayed lit)
            if (muzzleFlash) muzzleFlash.SetActive(false);
            if (muzzleLight) muzzleLight.enabled = false;
            if (reloadStyle == ReloadStyle.PerShell && magazine) magazine.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            Init();
            if (motor)
            {
                motor.Landed += OnLanded;
                motor.Jumped += OnJumped;
                motor.VaultStarted += OnVault;
            }
            equipT = 0f;
            holstering = false;
            holsterT = 0f;
            aim = 0f;
            lowerBlend = 1f;
            bumpRot.Kick(new Vector3(-30f, 20f, 30f));
            ProcAudio.Play(Sfx.Equip, 0.5f, 0.06f);
        }

        void OnDisable()
        {
            if (motor)
            {
                motor.Landed -= OnLanded;
                motor.Jumped -= OnJumped;
                motor.VaultStarted -= OnVault;
            }
            CancelReload();
            pumping = false;
            if (chargingHandle) chargingHandle.localPosition = chRest;
            if (leftHand) leftHand.localPosition = handRest;
            if (magazine) magazine.localPosition = magRest;
            if (muzzleFlash) muzzleFlash.SetActive(false);
            if (muzzleLight) muzzleLight.enabled = false;
            SetScope(false);
            if (playerCam) playerCam.AimBlend = 0f;
        }

        Combatant owner;

        void Start()
        {
            owner = motor.GetComponent<Combatant>();
            ProcAudio.Init();
            HUD.Ensure();
            DamageNumbers.Ensure();
            FX.Ensure().SetPlayer(motor.Controller);
        }

        public void Holster(Action done)
        {
            if (holstering) return;
            CancelReload();
            holstering = true;
            holsterT = 0f;
            onHolstered = done;
            ProcAudio.Play(Sfx.Equip, 0.35f, 0.1f);
        }

        public void RefillAmmo()
        {
            Init();
            reserve = reserveAmmo;
            if (ammo < magSize && !reloading) ammo = magSize;
        }

        public void ResetAmmo()
        {
            Init();
            ammo = magSize;
            reserve = reserveAmmo;
        }

        void OnLanded(float speed)
        {
            float s = Mathf.Clamp01((speed - 2f) / 10f);
            bumpPos.Kick(new Vector3(0f, -0.35f - 0.7f * s, 0f));
            bumpRot.Kick(new Vector3(25f + 50f * s, 0f, Random.Range(-15f, 15f)));
        }

        void OnJumped()
        {
            bumpPos.Kick(new Vector3(0f, -0.3f, 0f));
            bumpRot.Kick(new Vector3(-20f, 0f, 6f));
        }

        void OnVault(bool mantle)
        {
            bumpRot.Kick(new Vector3(40f, -15f, 30f));
            if (reloading && reloadT < 0.3f && reloadStyle == ReloadStyle.Magazine) CancelReload();
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (equipT < 1f) equipT = Mathf.Min(1f, equipT + dt / equipTime);
            if (holstering) holsterT = Mathf.Min(1f, holsterT + dt / 0.2f);
            lowerBlend = Anim.Damp(lowerBlend, Mathf.Max(ExternalLower, AbilityLower), 14f, dt);
            float lower = Mathf.Max(lowerBlend, Mathf.Max(1f - Anim.Smooth(equipT), Anim.Smooth(holsterT)));

            bool wantAim = GameInput.AimHeld && !reloading && !motor.IsVaulting && lower < 0.5f && !holstering;
            WantAim = wantAim;
            aim =Mathf.MoveTowards(aim, wantAim ? 1f : 0f, dt / Mathf.Max(0.01f, aimTime));
            aimEased = Anim.Smooth(aim);
            playerCam.AimBlend = aimEased;
            playerCam.AdsFov = adsFov;

            motor.BlockSprint = wantAim || (reloading && reloadStyle == ReloadStyle.Magazine) || (GameInput.FireHeld && ammo > 0);
            motor.SpeedMultiplier = Mathf.Lerp(1f, 0.62f, aimEased) * moveSpeedMul;

            sprintBlend = Anim.Damp(sprintBlend, motor.IsSprinting && motor.Speed01 > 0.45f && !reloading ? 1f : 0f, 9f, dt);
            vaultBlend = Anim.Damp(vaultBlend, motor.IsVaulting ? 1f : 0f, 14f, dt);
            crouchBlend = Anim.Damp(crouchBlend, motor.IsCrouching ? 1f : 0f, 8f, dt);
            proneBlend = Anim.Damp(proneBlend, motor.ProneAmount, 8f, dt);
            slideBlend = Anim.Damp(slideBlend, motor.IsSliding ? 1f : 0f, 10f, dt);

            if (!holstering && lower < 0.5f)
            {
                if (GameInput.FireModePressed && allowFireModeSwitch && !reloading)
                {
                    fullAuto = !fullAuto;
                    ProcAudio.Play(Sfx.Switch, 0.6f);
                    bumpRot.Kick(new Vector3(-10f, 0f, 35f));
                }
                if (GameInput.ReloadPressed) TryReload();
                // auto reload: the magazine ran dry (after the last shot's cycle) or an empty gun was drawn
                else if (ammo == 0 && reserve > 0 && !reloading && !pumping && equipT >= 1f && Time.time >= nextFire) TryReload();
            }
            if (reloading)
            {
                if (reloadStyle == ReloadStyle.PerShell) UpdateShellReload(dt);
                else UpdateReload(dt);
            }
            if (pumping) UpdatePump(dt);

            bool canFire = !reloading && !pumping && !holstering && lower < 0.15f
                           && sprintBlend < 0.3f && vaultBlend < 0.3f && wallTuck < 0.6f;
            bool trigger = fullAuto ? GameInput.FireHeld : GameInput.FirePressed;
            if (trigger && canFire && Time.time >= nextFire)
            {
                if (ammo > 0) Fire();
                else if (GameInput.FirePressed) DryFire();
            }

            if (Time.time - lastShotTime > 0.3f) shotIndex = 0;
            if (shotsSinceSmoke >= 4 && Time.time - lastShotTime > 0.12f)
            {
                FX.Ensure().MuzzleSmoke(muzzle.position, muzzle.forward, Mathf.Clamp(shotsSinceSmoke / 3, 2, 6));
                shotsSinceSmoke = 0;
            }

            if (Time.time - lastShotTime > 0.1f) bloom = Mathf.MoveTowards(bloom, 0f, bloomRecovery * dt);
            float moveSpread = motor.Speed01 * moveSpreadAdd;
            float air = motor.IsGrounded || motor.IsVaulting ? 0f : airSpreadAdd;
            float stanceMul = motor.IsProne ? 0.5f : motor.IsCrouching ? 0.75f : 1f;
            float hip = (hipSpread + moveSpread + air) * stanceMul + bloom;
            float ads = adsSpread + moveSpread * 0.12f + air * 0.3f + bloom * 0.18f;
            CurrentSpread = Mathf.Lerp(hip, ads, aimEased);

            if (flashTimer > 0f)
            {
                flashTimer -= dt;
                if (flashTimer <= 0f)
                {
                    if (muzzleFlash) muzzleFlash.SetActive(false);
                    if (muzzleLight) muzzleLight.enabled = false;
                }
                else if (muzzleLight) muzzleLight.intensity *= 0.6f;
            }

            UpdateWallTuck(dt);
            UpdatePose(dt, lower);

            SetScope(scoped && aimEased > 0.93f && !reloading && !holstering && !(boltAction && pumping && pumpT > 0.05f));

            if (holstering && holsterT >= 1f)
            {
                holstering = false;
                var cb = onHolstered;
                onHolstered = null;
                gameObject.SetActive(false);
                cb?.Invoke();
            }
        }

        void SetScope(bool on)
        {
            if (scopeOn == on) return;
            scopeOn = on;
            if (renderers != null)
                foreach (var r in renderers) if (r) r.enabled = !on;
            if (HUD.Instance) HUD.Instance.SetScope(on);
            if (on) ProcAudio.Play(Sfx.Switch, 0.25f, 0.05f);
        }

        // ------------------------------------------------------------------ shooting

        readonly Dictionary<IDamageable, (float amount, bool crit, Vector3 point, Vector3 normal)> hitAccum
            = new Dictionary<IDamageable, (float, bool, Vector3, Vector3)>();

        static readonly RaycastHit[] shotHits = new RaycastHit[24];

        /// <summary>Nearest hit along a bullet ray that is not the shooter's own body / capsule.</summary>
        bool RaycastShot(Vector3 origin, Vector3 dir, out RaycastHit best)
        {
            best = default;
            Transform self = owner ? owner.transform : motor ? motor.transform : null;
            int n = Physics.RaycastNonAlloc(origin, dir, shotHits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var h = shotHits[i];
                if (!h.collider || h.distance >= bd) continue;
                if (self && h.collider.transform.IsChildOf(self)) continue;
                bd = h.distance; best = h; any = true;
            }
            return any;
        }

        void Fire()
        {
            float interval = 60f / rpm;
            if (Time.time - nextFire > interval) nextFire = Time.time;
            nextFire += interval;
            ammo--;
            shotIndex++;
            shotsSinceSmoke++;
            lastShotTime = Time.time;
            if (owner) owner.lastShotTime = Time.time;

            Transform ct = cam.transform;
            HUD.Ensure().RegisterShot();
            hitAccum.Clear();
            NetGame.ShotEnds.Clear();
            int n = Mathf.Max(1, pellets);
            for (int p = 0; p < n; p++)
            {
                Vector2 r = Random.insideUnitCircle * CurrentSpread + (n > 1 ? Random.insideUnitCircle * pelletSpread : Vector2.zero);
                Vector3 dir = ct.rotation * Quaternion.Euler(-r.y, r.x, 0f) * Vector3.forward;
                Vector3 end = ct.position + dir * range;
                if (RaycastShot(ct.position, dir, out RaycastHit hit))
                {
                    end = hit.point;
                    var hb = hit.collider.GetComponent<Hitbox>();
                    IDamageable dmg = hb ? hb.Owner : hit.collider.GetComponent<IDamageable>();
                    var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                    SurfaceKind surf = tag ? tag.kind : SurfaceKind.Concrete;
                    if (dmg != null)
                    {
                        bool crit = hb && hb.critical;
                        float mult = hb ? hb.multiplier : 1f;
                        float falloff = Mathf.Lerp(1f, falloffMin, Mathf.InverseLerp(falloffStart, falloffEnd, hit.distance));
                        float amount = damage * mult * falloff * Random.Range(0.94f, 1.06f);
                        if (hitAccum.TryGetValue(dmg, out var acc)) hitAccum[dmg] = (acc.amount + amount, acc.crit || crit, acc.point, acc.normal);
                        else hitAccum[dmg] = (amount, crit, hit.point, hit.normal);
                    }
                    if (hit.rigidbody && !hit.rigidbody.isKinematic)
                        hit.rigidbody.AddForceAtPosition(dir * impactForce / n, hit.point, ForceMode.Impulse);
                    var flesh = hit.collider.GetComponentInParent<HumanoidRig>();
                    if (flesh) FX.Ensure().Blood(hit.point, hit.normal, n > 1 ? 4 : 8);
                    else FX.Ensure().Impact(hit, dmg != null && !(dmg is Destructible) ? SurfaceKind.Metal : surf, dmg == null && hit.rigidbody == null);
                }
                if (p == 0 || p % 3 == 0) FX.Ensure().Tracer(muzzle.position, end);
                if (NetGame.ShotEnds.Count < 9)
                    NetGame.ShotEnds.Add((end, hit.collider == null ? (byte)2 : hit.collider.GetComponentInParent<HumanoidRig>() ? (byte)1 : (byte)0));
            }

            bool anyKill = false, anyCrit = false, anyBody = false;
            foreach (var kv in hitAccum)
            {
                var res = kv.Key.TakeDamage(new DamageInfo
                {
                    amount = Mathf.Round(kv.Value.amount), point = kv.Value.point, normal = kv.Value.normal,
                    direction = ct.forward, critical = kv.Value.crit, attacker = owner, weapon = weaponName
                });
                // cover (crates, sandbags) takes damage but is not a "hit" — no hitmarker, no accuracy
                if (!res.applied || kv.Key is Destructible) continue;
                anyBody = true;
                anyKill |= res.killed;
                anyCrit |= kv.Value.crit;
                HUD.Ensure().RegisterHit(res.killed);
            }
            if (anyBody)
            {
                HUD.Ensure().ShowHitmarker(anyKill, anyCrit);
                ProcAudio.Play(anyKill ? Sfx.Kill : anyCrit ? Sfx.HitCrit : Sfx.Hit, anyKill ? 0.6f : 0.42f, 0.03f);
            }

            if (muzzleFlash)
            {
                muzzleFlash.SetActive(true);
                muzzleFlash.transform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                muzzleFlash.transform.localScale = Vector3.one * Random.Range(0.75f, 1.25f) * (n > 1 ? 1.4f : 1f);
            }
            if (muzzleLight) { muzzleLight.enabled = true; muzzleLight.intensity = Random.Range(3f, 5f); }
            flashTimer = 0.045f;

            ProcAudio.Play(shotSfx, shotVolume, 0.035f);
            AIEvents.Emit(ct.position, 45f, owner);
            NetGame.SendShot(owner, ct.position, NetGame.ShotEnds);
            if (!pumpAction) FX.Ensure().EjectShell(ejectPort.position, ct, motor.Velocity, shellScale);
            boltT = 1f;

            float mul = Mathf.Lerp(1f, adsRecoilMul, aimEased) * (motor.IsProne ? 0.55f : motor.IsCrouching ? crouchRecoilMul : 1f);
            float up = recoilUp * mul * (shotIndex <= 3 ? 1.3f : 1f);
            float side = (Mathf.PerlinNoise(recoilSeed, shotIndex * 0.33f) - 0.5f) * 2f * recoilSide * mul;
            playerCam.AddRecoil(up, side);
            playerCam.Punch(new Vector3(-Random.Range(5f, 9f) * mul, Random.Range(-3f, 3f), Random.Range(-7f, 7f)) * punchScale);

            Fired?.Invoke();
            float vm = Mathf.Lerp(1f, 0.42f, aimEased) * kickScale;
            kickPos.Kick(new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(0.02f, 0.1f), -Random.Range(1.0f, 1.3f)) * vm);
            kickRot.Kick(new Vector3(-Random.Range(55f, 80f), Random.Range(-22f, 22f), Random.Range(-40f, 40f)) * vm);

            bloom = Mathf.Min(maxBloom, bloom + bloomPerShot);
            if (ammo == 0) ProcAudio.Play(Sfx.Dry, 0.25f);
            if (pumpAction && ammo > 0) StartPump(boltAction ? 0.28f : 0.1f);
        }

        void DryFire()
        {
            ProcAudio.Play(Sfx.Dry, 0.7f);
            nextFire = Time.time + 0.25f;
            kickRot.Kick(new Vector3(-8f, 0f, 4f));
            if (reserve > 0) TryReload();
        }

        // ------------------------------------------------------------------ pump action

        void StartPump(float delay)
        {
            pumping = true;
            pumpT = -delay / pumpTime;
            pumpEjected = pumpBackPlayed = pumpFwdPlayed = false;
        }

        void UpdatePump(float dt)
        {
            pumpT += dt / pumpTime;
            if (!pumpBackPlayed && pumpT >= 0.02f) { pumpBackPlayed = true; ProcAudio.Play(Sfx.PumpBack, 0.7f); bumpRot.Kick(new Vector3(10f, 0f, -8f)); }
            if (!pumpEjected && pumpT >= 0.4f) { pumpEjected = true; FX.Ensure().EjectShell(ejectPort.position, cam.transform, motor.Velocity, shellScale); }
            if (!pumpFwdPlayed && pumpT >= 0.55f) { pumpFwdPlayed = true; ProcAudio.Play(Sfx.PumpForward, 0.75f); bumpRot.Kick(new Vector3(-14f, 0f, 10f)); bumpPos.Kick(new Vector3(0f, 0f, 0.25f)); }
            if (pumpT >= 1f) pumping = false;
        }

        float PumpOffset()
        {
            if (!pumping || pumpT <= 0f) return 0f;
            float t = pumpT;
            if (t < 0.38f) return Anim.Smooth(t / 0.38f);
            if (t < 0.5f) return 1f;
            if (t < 0.8f) return 1f - Anim.Smooth((t - 0.5f) / 0.3f);
            return 0f;
        }

        // ------------------------------------------------------------------ reload

        void TryReload()
        {
            if (reloading || ammo >= magSize || reserve <= 0 || motor.IsVaulting || pumping || holstering) return;
            reloading = true;
            reloadT = 0f;
            evtIndex = 0;
            reloadEmpty = ammo == 0;
            magHidden = false;
            if (reloadStyle == ReloadStyle.PerShell)
            {
                shellPhase = 0;
                shellInterrupt = false;
                shellInserted = false;
                reloadDuration = 0.32f;
            }
            else reloadDuration = reloadEmpty ? emptyReloadTime : reloadTime;
        }

        public void CancelReload()
        {
            reloading = false;
            magHidden = false;
            reloadT = 0f;
            if (reloadStyle == ReloadStyle.PerShell && magazine) magazine.gameObject.SetActive(false);
        }

        void UpdateReload(float dt)
        {
            reloadT += dt / reloadDuration;
            var a = reloadEmpty ? emptyAnim : tacAnim;
            while (evtIndex < a.events.Length && reloadT >= a.events[evtIndex].t)
            {
                DoEvent(a.events[evtIndex].e);
                evtIndex++;
            }
            if (reloadT >= 1f)
            {
                reloading = false;
                magHidden = false;
                reloadT = 0f;
            }
        }

        void UpdateShellReload(float dt)
        {
            if (GameInput.FirePressed && ammo > 0) shellInterrupt = true;
            reloadT += dt / reloadDuration;
            if (shellPhase == 1 && !shellInserted && reloadT >= 0.55f)
            {
                shellInserted = true;
                ammo++;
                reserve--;
                ProcAudio.Play(Sfx.ShellIn, 0.7f, 0.06f);
                bumpRot.Kick(new Vector3(-10f, 4f, 8f));
                bumpPos.Kick(new Vector3(0f, 0.12f, 0f));
            }
            if (reloadT < 1f) return;
            reloadT = 0f;
            if (shellPhase == 0 || shellPhase == 1)
            {
                bool more = ammo < magSize && reserve > 0 && !shellInterrupt;
                if (more) { shellPhase = 1; shellInserted = false; reloadDuration = shellInsertTime; }
                else { shellPhase = 2; reloadDuration = 0.4f; }
            }
            else
            {
                reloading = false;
                if (magazine) magazine.gameObject.SetActive(false);
                if (reloadEmpty && pumpAction) StartPump(0f);
            }
        }

        void DoEvent(Evt e)
        {
            switch (e)
            {
                case Evt.Grab:
                    bumpRot.Kick(new Vector3(6f, 0f, -8f));
                    break;
                case Evt.MagOut:
                    ProcAudio.Play(Sfx.MagOut, 0.7f);
                    bumpRot.Kick(new Vector3(14f, 0f, -14f));
                    bumpPos.Kick(new Vector3(0f, -0.15f, 0f));
                    break;
                case Evt.Drop:
                    if (magazine) FX.Ensure().DropMagazine(magazine, motor.Velocity, cam.transform);
                    magHidden = true;
                    break;
                case Evt.Show:
                    magHidden = false;
                    break;
                case Evt.MagIn:
                    ProcAudio.Play(Sfx.MagIn, 0.8f);
                    bumpRot.Kick(new Vector3(-45f, 8f, 30f));
                    bumpPos.Kick(new Vector3(0f, 0.4f, 0.05f));
                    int take = Mathf.Min(magSize - ammo, reserve);
                    ammo += take;
                    reserve -= take;
                    break;
                case Evt.BoltPull:
                    ProcAudio.Play(Sfx.BoltPull, 0.7f);
                    bumpPos.Kick(new Vector3(0f, 0f, -0.35f));
                    break;
                case Evt.BoltRelease:
                    ProcAudio.Play(Sfx.BoltRelease, 0.85f);
                    bumpRot.Kick(new Vector3(-35f, 12f, -25f));
                    bumpPos.Kick(new Vector3(0f, 0.1f, 0.45f));
                    break;
            }
        }

        void BuildAnimations(Vector3 G, Vector3 CH)
        {
            float pull = -chTravel;
            tacAnim = new ReloadAnim
            {
                wPos = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.14f, -0.035f, 0.03f, -0.02f), new KeyFrame(0.32f, -0.045f, 0.02f, -0.03f),
                    new KeyFrame(0.52f, -0.05f, 0.015f, -0.02f), new KeyFrame(0.62f, -0.05f, 0.035f, -0.02f),
                    new KeyFrame(0.82f, -0.02f, 0.01f, -0.01f), new KeyFrame(1f, 0, 0, 0)
                },
                wRot = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.15f, -9f, -12f, 26f), new KeyFrame(0.5f, -7f, -13f, 29f),
                    new KeyFrame(0.62f, -11f, -10f, 24f), new KeyFrame(0.84f, -2f, -3f, 7f), new KeyFrame(1f, 0, 0, 0)
                },
                mag = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.2f, 0, 0, 0), new KeyFrame(0.27f, 0f, -0.06f, 0.006f), new KeyFrame(0.31f, 0f, -0.09f, 0.01f),
                    new KeyFrame(0.42f, -0.06f, -0.3f, -0.1f), new KeyFrame(0.53f, -0.01f, -0.1f, -0.01f), new KeyFrame(0.57f, 0f, -0.065f, 0f),
                    new KeyFrame(0.6f, 0, 0, 0), new KeyFrame(1f, 0, 0, 0)
                },
                hand = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.08f, -0.01f, -0.02f, -0.03f), new KeyFrame(0.19f, G),
                    new KeyFrame(0.27f, G + new Vector3(0f, -0.06f, 0.006f)), new KeyFrame(0.31f, G + new Vector3(0f, -0.09f, 0.01f)),
                    new KeyFrame(0.37f, -0.09f, -0.36f, -0.16f), new KeyFrame(0.42f, G + new Vector3(-0.06f, -0.3f, -0.1f)),
                    new KeyFrame(0.53f, G + new Vector3(-0.01f, -0.1f, -0.01f)), new KeyFrame(0.57f, G + new Vector3(0f, -0.065f, 0f)),
                    new KeyFrame(0.6f, G), new KeyFrame(0.66f, G + new Vector3(0f, -0.03f, 0.02f)),
                    new KeyFrame(0.82f, 0f, -0.01f, 0f), new KeyFrame(1f, 0, 0, 0)
                },
                ch = new[] { new KeyFrame(0f, 0, 0, 0), new KeyFrame(1f, 0, 0, 0) },
                events = new[] { (0.19f, Evt.Grab), (0.22f, Evt.MagOut), (0.31f, Evt.Drop), (0.42f, Evt.Show), (0.595f, Evt.MagIn) }
            };

            emptyAnim = new ReloadAnim
            {
                wPos = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.11f, -0.035f, 0.03f, -0.02f), new KeyFrame(0.27f, -0.045f, 0.02f, -0.03f),
                    new KeyFrame(0.46f, -0.05f, 0.025f, -0.02f), new KeyFrame(0.56f, -0.03f, 0.0f, -0.04f), new KeyFrame(0.68f, -0.02f, -0.01f, -0.06f),
                    new KeyFrame(0.75f, -0.02f, 0f, -0.045f), new KeyFrame(0.88f, -0.01f, 0f, -0.01f), new KeyFrame(1f, 0, 0, 0)
                },
                wRot = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.12f, -9f, -12f, 26f), new KeyFrame(0.42f, -7f, -13f, 29f),
                    new KeyFrame(0.5f, -10f, -10f, 24f), new KeyFrame(0.6f, -5f, -6f, -12f), new KeyFrame(0.72f, -7f, -4f, -16f),
                    new KeyFrame(0.88f, -2f, -2f, -3f), new KeyFrame(1f, 0, 0, 0)
                },
                mag = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.15f, 0, 0, 0), new KeyFrame(0.21f, 0f, -0.06f, 0.006f), new KeyFrame(0.245f, 0f, -0.09f, 0.01f),
                    new KeyFrame(0.34f, -0.06f, -0.3f, -0.1f), new KeyFrame(0.42f, -0.01f, -0.1f, -0.01f), new KeyFrame(0.455f, 0f, -0.065f, 0f),
                    new KeyFrame(0.48f, 0, 0, 0), new KeyFrame(1f, 0, 0, 0)
                },
                hand = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.06f, -0.01f, -0.02f, -0.03f), new KeyFrame(0.15f, G),
                    new KeyFrame(0.21f, G + new Vector3(0f, -0.06f, 0.006f)), new KeyFrame(0.245f, G + new Vector3(0f, -0.09f, 0.01f)),
                    new KeyFrame(0.29f, -0.09f, -0.36f, -0.16f), new KeyFrame(0.34f, G + new Vector3(-0.06f, -0.3f, -0.1f)),
                    new KeyFrame(0.42f, G + new Vector3(-0.01f, -0.1f, -0.01f)), new KeyFrame(0.455f, G + new Vector3(0f, -0.065f, 0f)),
                    new KeyFrame(0.48f, G), new KeyFrame(0.56f, (G + CH) * 0.5f + new Vector3(-0.1f, 0.02f, 0f)), new KeyFrame(0.64f, CH),
                    new KeyFrame(0.7f, CH + new Vector3(0f, 0f, pull)), new KeyFrame(0.745f, CH + new Vector3(-0.03f, 0.03f, pull - 0.025f)),
                    new KeyFrame(0.88f, -0.02f, -0.02f, 0f), new KeyFrame(1f, 0, 0, 0)
                },
                ch = new[]
                {
                    new KeyFrame(0f, 0, 0, 0), new KeyFrame(0.64f, 0, 0, 0), new KeyFrame(0.7f, 0f, 0f, pull),
                    new KeyFrame(0.725f, 0f, 0f, pull), new KeyFrame(0.735f, 0, 0, 0), new KeyFrame(1f, 0, 0, 0)
                },
                events = new[] { (0.15f, Evt.Grab), (0.17f, Evt.MagOut), (0.245f, Evt.Drop), (0.34f, Evt.Show), (0.475f, Evt.MagIn), (0.66f, Evt.BoltPull), (0.73f, Evt.BoltRelease) }
            };
        }

        // ------------------------------------------------------------------ pose / procedural animation

        void UpdateWallTuck(float dt)
        {
            float target = 0f;
            Transform ct = cam.transform;
            if (Physics.SphereCast(ct.position, 0.07f, ct.forward, out RaycastHit h, 0.8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!h.collider.transform.IsChildOf(motor.transform) && !h.collider.GetComponentInParent<HumanoidRig>())
                    target = Mathf.InverseLerp(0.8f, 0.35f, h.distance);
            }
            wallTuck = Anim.Damp(wallTuck, target, 10f, dt);
        }

        void UpdatePose(float dt, float lower)
        {
            Vector2 lookRate = playerCam.LastLookDelta / dt;
            float swayMul = Mathf.Lerp(1f, 0.28f, aimEased);
            swayRot.target = Vector3.ClampMagnitude(new Vector3(lookRate.y * 0.014f, -lookRate.x * 0.012f, -lookRate.x * 0.02f), 8f) * swayMul;
            swayPos.target = Vector3.ClampMagnitude(new Vector3(-lookRate.x * 0.00018f, -lookRate.y * 0.00018f, 0f), 0.03f) * swayMul;

            // movement sway — vertical lag only while airborne (on the ground the motor keeps a constant stick-down velocity)
            Vector2 mv = motor.MoveInput;
            float vy = motor.IsGrounded ? 0f : motor.Velocity.y;
            swayRot.target += new Vector3(0f, 0f, -mv.x * 3.5f) * swayMul;
            swayPos.target += new Vector3(-mv.x * 0.01f, Mathf.Clamp(-vy * 0.004f, -0.035f, 0.035f), -mv.y * 0.012f) * swayMul;

            Vector3 sr = swayRot.Step(dt), sp = swayPos.Step(dt);
            Vector3 kp = kickPos.Step(dt), kr = kickRot.Step(dt);
            Vector3 bp = bumpPos.Step(dt), br = bumpRot.Step(dt);

            float ph = motor.StepPhase * Mathf.PI * 2f;
            bool grounded = motor.IsGrounded && !motor.IsSliding && !motor.IsVaulting;
            bobWeight = Anim.Damp(bobWeight, grounded ? Mathf.Clamp01(motor.Speed01 * 1.7f) : 0f, 10f, dt);
            float bobAmp = bobWeight * Mathf.Lerp(1f, 0.12f, aimEased) * (1f + sprintBlend * 0.9f + proneBlend * 1.5f);
            Vector3 bobP = new Vector3(Mathf.Sin(ph) * 0.012f, -Mathf.Cos(ph * 2f) * 0.009f, 0f) * bobAmp;
            Vector3 bobR = new Vector3(-Mathf.Cos(ph * 2f) * 0.9f, Mathf.Sin(ph) * 0.9f, Mathf.Sin(ph) * 1.8f) * bobAmp;

            float tt = Time.time;
            float breath = Mathf.Lerp(1f, scoped ? 0.6f : 0.35f, aimEased) * (1f - bobWeight * 0.8f) * (motor.IsProne ? 0.4f : 1f);
            Vector3 idleP = new Vector3(Mathf.Sin(tt * 0.9f) * 0.0025f, Mathf.Sin(tt * 1.6f) * 0.003f, 0f) * breath;
            Vector3 idleR = new Vector3(Mathf.Sin(tt * 1.3f) * 0.45f, Mathf.Sin(tt * 0.7f) * 0.3f, Mathf.Sin(tt * 0.5f) * 0.3f) * breath;

            float hipW = 1f - aimEased;
            Vector3 pos = Vector3.Lerp(hipPosition, aimPosition, aimEased);
            Vector3 rot = Vector3.zero;

            pos += sprintPos * sprintBlend;
            rot += sprintRot * sprintBlend;
            rot += new Vector3(Mathf.Cos(ph * 2f) * 3f, Mathf.Sin(ph) * 5f, Mathf.Sin(ph) * 2f) * sprintBlend * bobWeight;

            pos += new Vector3(-0.012f, 0.012f, -0.01f) * crouchBlend * hipW;
            rot += new Vector3(0f, 0f, -6f) * crouchBlend * hipW;
            pos += new Vector3(-0.02f, 0.02f, -0.03f) * proneBlend * hipW;
            rot += new Vector3(-3f, 0f, -10f) * proneBlend * hipW;
            pos += new Vector3(-0.02f, -0.03f, -0.02f) * slideBlend * hipW;
            rot += new Vector3(0f, -6f, -14f) * slideBlend * hipW;
            pos += new Vector3(0.04f, -0.22f, -0.1f) * vaultBlend;
            rot += new Vector3(38f, -22f, 48f) * vaultBlend;
            if (!AnimDriven) // with FPS Animation the WeaponCollision layer pulls the gun back from walls
            {
                pos += new Vector3(-0.03f, -0.02f, -0.14f) * wallTuck;
                rot += new Vector3(-38f, -14f, 10f) * wallTuck;
            }
            pos += new Vector3(0.03f, -0.3f, -0.12f) * lower;
            rot += new Vector3(40f, -18f, 25f) * lower;

            Vector3 magOff = Vector3.zero, handOff = Vector3.zero, chOff = Vector3.zero;
            bool shellVisible = false;
            if (reloading && reloadStyle == ReloadStyle.Magazine)
            {
                var a = reloadEmpty ? emptyAnim : tacAnim;
                pos += Anim.Eval(a.wPos, reloadT);
                rot += Anim.Eval(a.wRot, reloadT);
                magOff = Anim.Eval(a.mag, reloadT);
                handOff = Anim.Eval(a.hand, reloadT);
                chOff = Anim.Eval(a.ch, reloadT);
            }
            else if (reloading)
            {
                float w = shellPhase == 0 ? Anim.Smooth(reloadT) : shellPhase == 2 ? 1f - Anim.Smooth(reloadT) : 1f;
                pos += new Vector3(-0.03f, 0.035f, -0.02f) * w;
                rot += new Vector3(-6f, 8f, -24f) * w;
                if (shellPhase == 1)
                {
                    float u = reloadT;
                    Vector3 h = u < 0.5f ? Vector3.Lerp(shellH0, shellPort, Anim.Smooth(u / 0.5f))
                              : u < 0.65f ? shellPort
                              : Vector3.Lerp(shellPort, shellH0, Anim.Smooth((u - 0.65f) / 0.35f));
                    handOff = h;
                    shellVisible = u < 0.55f;
                }
                else handOff = shellH0 * w;
            }

            if (pumpAction)
            {
                float po = PumpOffset();
                chOff += new Vector3(0f, 0f, -chTravel * po);
                if (boltAction)
                {
                    // the gun cants and comes off the eye while the right hand works the bolt
                    float bw = pumping && pumpT > 0f ? Mathf.Sin(Mathf.Clamp01(pumpT) * Mathf.PI) : 0f;
                    pos += new Vector3(-0.015f, -0.02f, -0.02f) * bw;
                    rot += new Vector3(-4f, 8f, 24f) * bw;
                }
                else
                {
                    handOff += new Vector3(0f, -0.004f * po, -chTravel * po);
                    pos += new Vector3(0f, 0f, -0.012f * po);
                    rot += new Vector3(-2f * po, 0f, 3f * po);
                }
            }

            if (AnimDriven)
            {
                // hip / ADS base, look sway and shot kicks come from the FPS Animation layers (AdsLayer, SwayLayer, RecoilLayer)
                PoseOffsetPos = pos - Vector3.Lerp(hipPosition, aimPosition, aimEased) + bobP + idleP + bp;
                PoseOffsetRot = rot + bobR + idleR + br;
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
            }
            else
            {
                pos += bobP + idleP + sp + kp + bp;
                rot += bobR + idleR + sr + kr + br;
                transform.localPosition = pos;
                transform.localRotation = Quaternion.Euler(rot);
            }

            if (magazine)
            {
                if (reloadStyle == ReloadStyle.Magazine)
                {
                    magazine.localPosition = magRest + magOff;
                    if (magazine.gameObject.activeSelf == magHidden) magazine.gameObject.SetActive(!magHidden);
                }
                else if (magazine.gameObject.activeSelf != shellVisible) magazine.gameObject.SetActive(shellVisible);
            }
            if (leftHand) leftHand.localPosition = handRest + handOff;
            if (chargingHandle) chargingHandle.localPosition = chRest + chOff;
            boltT = Mathf.MoveTowards(boltT, 0f, dt * 22f);
            if (bolt) bolt.localPosition = boltRest + new Vector3(0f, 0f, -0.035f * Mathf.Sin(boltT * Mathf.PI * 0.5f));

            // collimated reticle: like a real red dot, it always marks where the camera (and the bullets) point
            if (reticle)
            {
                Vector3 rest = reticle.parent.TransformPoint(reticleRest);
                Vector3 mark = rest + (reticle.TransformPoint(reticleCenter) - reticle.position);
                Transform ct = cam.transform;
                float d = Vector3.Dot(mark - ct.position, ct.forward);
                Vector3 onAxis = ct.position + ct.forward * d;
                Vector3 off = Vector3.ClampMagnitude(onAxis - mark, 0.012f);
                reticle.position = rest + off * aimEased;
            }
        }
    }
}
