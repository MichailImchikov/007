using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// The drone ability of one combatant (drone war mode). The ability key throws a drone into the air
    /// and the operator sees through its FPV camera while the body stays where it is. The ability is
    /// ready again 30 s after the drone is gone. Bots launch drones on their own (see DroneBotPilot).
    /// </summary>
    [DefaultExecutionOrder(45)]
    public class DroneOperator : MonoBehaviour
    {
        public const float Cooldown = 30f;

        public static DroneOperator Local { get; private set; }

        public Combatant Combatant { get; private set; }
        public Drone Drone { get; private set; }
        public float CooldownLeft { get; private set; }
        public bool IsLocalPlayer { get; private set; }
        public bool Ready => !Drone && CooldownLeft <= 0f && Combatant && Combatant.alive;
        public bool Piloting => Drone;
        /// <summary>The local view switched to / from the drone camera (time of the switch).</summary>
        public float ViewSwitchTime { get; private set; } = -99f;
        public event Action<string> Notice;

        /// <summary>Tests / cheats: the drone is ready right now.</summary>
        public void ResetCooldown() => CooldownLeft = 0f;

        PlayerHealth ph;
        CombatBot bot;
        DroneBotPilot botPilot;
        float botThink, failNoticeT = -99f;

        void Awake()
        {
            Combatant = GetComponent<Combatant>();
            ph = GetComponent<PlayerHealth>();
            bot = GetComponent<CombatBot>();
            IsLocalPlayer = ph && ph == PlayerHealth.Local;
            if (IsLocalPlayer) Local = this;
            if (ph) { ph.Died += OnOwnerDied; ph.Damaged += OnOwnerDamaged; }
            botThink = UnityEngine.Random.Range(4f, 10f);
        }

        void OnDestroy()
        {
            if (ph) { ph.Died -= OnOwnerDied; ph.Damaged -= OnOwnerDamaged; }
            if (IsLocalPlayer && Drone) ExitView();
            if (Local == this) Local = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (CooldownLeft > 0f && !Drone) CooldownLeft = Mathf.Max(0f, CooldownLeft - dt);
            if (Drone && Combatant && !Combatant.alive) Drone.CutLink();

            if (IsLocalPlayer && !Drone && GameInput.AbilityPressed)
            {
                if (Ready) TryLaunch();
                else if (Time.time - failNoticeT > 0.4f)
                {
                    failNoticeT = Time.time;
                    ProcAudio.Play(Sfx.ErrorTone, 0.35f);
                    Notice?.Invoke(CooldownLeft > 0f ? "Дрон будет готов через " + Mathf.CeilToInt(CooldownLeft) + " с" : "Дрон недоступен");
                }
            }
        }

        // ------------------------------------------------------------------ launch

        /// <summary>Throws the drone up from the hands; fails when there is no room (indoors under a low ceiling).</summary>
        public bool TryLaunch()
        {
            if (!Ready) return false;
            Transform t = transform;
            Vector3 fwd = t.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            float yaw = Quaternion.LookRotation(fwd).eulerAngles.y;
            if (IsLocalPlayer && ph && ph.playerCam) yaw = ph.playerCam.Yaw;
            Vector3 chest = Combatant.Chest;
            Vector3 pos = chest + fwd * 0.75f + Vector3.up * 0.35f;
            if (Physics.CheckSphere(pos, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !OnlySelf(pos, 0.3f))
            {
                pos = chest + Vector3.up * 0.75f;
                if (Physics.CheckSphere(pos, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && !OnlySelf(pos, 0.3f))
                {
                    if (IsLocalPlayer) { ProcAudio.Play(Sfx.ErrorTone, 0.4f); Notice?.Invoke("Нет места для запуска"); }
                    return false;
                }
            }
            Vector3 vel = fwd * 2.2f + Vector3.up * 2.6f;
            Drone = Drone.Launch(this, pos, yaw, vel);
            ProcAudio.PlayAt(Sfx.Throw, pos, 0.7f);
            if (IsLocalPlayer)
            {
                Drone.pilot = DrivePlayer;
                Drone.LookFlight = true;
                EnterView();
            }
            else
            {
                botPilot = new DroneBotPilot(this, bot);
                Drone.pilot = botPilot.Drive;
            }
            return true;
        }

        bool OnlySelf(Vector3 p, float r)
        {
            foreach (var c in Physics.OverlapSphere(p, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!c.transform.IsChildOf(transform)) return false;
            return true;
        }

        /// <summary>Called by the drone when it explodes, is shot down or dies in any way.</summary>
        public void OnDroneGone(Drone d)
        {
            if (d != Drone) return;
            bool wasView = IsLocalPlayer && d.IsLocalView;
            Drone = null;
            botPilot = null;
            CooldownLeft = Cooldown;
            if (wasView) ExitView();
            if (IsLocalPlayer && d.EndReason.Length > 0) Notice?.Invoke(d.EndReason);
        }

        void OnOwnerDied()
        {
            if (!Drone) return;
            // the operator is dead: the view goes back to the body (death camera), the drone falls
            if (IsLocalPlayer && Drone.IsLocalView)
            {
                Drone.IsLocalView = false;
                Drone.SetFirstPerson(false);
                Drone.Cam.enabled = false;
                Drone.Cam.tag = "Untagged";
                Drone.Cam.GetComponent<AudioListener>().enabled = false;
                ExitView();
            }
            Drone.CutLink();
        }

        void OnOwnerDamaged(Vector3 src, float amount)
        {
            if (Drone) Drone.OperatorHitT = Time.time;
        }

        /// <summary>Bot operator got shot while flying: it drops the controller to fight back.</summary>
        public void OperatorHurt()
        {
            if (Drone && bot) Drone.CutLink();
        }

        // ------------------------------------------------------------------ player piloting

        void DrivePlayer(Drone d, float dt)
        {
            // short boot after the throw so the launch key press does not toggle anything
            bool boot = d.Age < 0.35f;
            // WASD — move relative to where you look, Space / Ctrl — up / down, mouse — look, Shift — faster
            d.cmdCyclic = GameInput.DroneKeys.Cyclic;
            d.cmdThrottle = GameInput.DroneKeys.Throttle;
            d.cmdSport = GameInput.DroneKeys.SportHeld;
            d.cmdFire = !boot && GameInput.DroneKeys.FireHeld;
            d.Steer(GameInput.DroneKeys.Look, dt);
            if (boot) return;
            if (GameInput.DroneKeys.ThermalPressed && !d.ToggleThermal()) ProcAudio.Play(Sfx.ErrorTone, 0.3f);
            if (GameInput.DroneKeys.DetonatePressed) d.Explode();
            else if (GameInput.DroneKeys.AbortPressed) d.CutLink();
        }

        void EnterView()
        {
            if (!ph || !Drone) return;
            Drone.IsLocalView = true;
            Drone.SetFirstPerson(true);
            GameInput.Piloting = true;
            ViewSwitchTime = Time.unscaledTime;
            var pc = ph.playerCam;
            if (pc && pc.cam)
            {
                pc.cam.enabled = false;
                var al = pc.cam.GetComponent<AudioListener>();
                if (al) al.enabled = false;
                pc.cam.tag = "Untagged";
            }
            Drone.Cam.enabled = true;
            Drone.Cam.tag = "MainCamera";
            Drone.Cam.GetComponent<AudioListener>().enabled = true;
            if (ph.loadout && ph.loadout.Active) ph.loadout.Active.ExternalLower = 1f;
            SetBodyVisible(true);
            if (HUD.Instance) { var c = HUD.Instance.GetComponent<Canvas>(); if (c) c.enabled = false; }
        }

        void ExitView()
        {
            GameInput.Piloting = false;
            ViewSwitchTime = Time.unscaledTime;
            if (!ph) return;
            var pc = ph.playerCam;
            if (pc && pc.cam)
            {
                pc.cam.enabled = true;
                var al = pc.cam.GetComponent<AudioListener>();
                if (al) al.enabled = true;
                pc.cam.tag = "MainCamera";
            }
            if (ph.loadout && ph.loadout.Active && !ph.IsDead) ph.loadout.Active.ExternalLower = 0f;
            SetBodyVisible(false);
            if (HUD.Instance) { var c = HUD.Instance.GetComponent<Canvas>(); if (c) c.enabled = true; }
        }

        void SetBodyVisible(bool on)
        {
            var body = GetComponent<PlayerBody>();
            if (!body || !body.rig || !body.firstPersonShadowsOnly) return;
            body.rig.SetShadowsOnly(!on);
            if (body.rig.weaponSocket)
                foreach (var r in body.rig.weaponSocket.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        // ------------------------------------------------------------------ bots

        /// <summary>
        /// CombatBot hook. Decides when to launch; while the drone flies the bot crouches still.
        /// Returns true while the bot is busy flying.
        /// </summary>
        public bool BotTick(float dt, bool fighting)
        {
            if (Drone) return true;
            if (!Ready || fighting) return false;
            botThink -= dt;
            if (botThink > 0f) return false;
            botThink = UnityEngine.Random.Range(2f, 6f);
            bool anyEnemy = false;
            foreach (var c in Combatant.All) if (c && c.alive && Combatant.IsEnemyOf(c)) { anyEnemy = true; break; }
            if (!anyEnemy || UnityEngine.Random.value > 0.55f) return false;
            return TryLaunch();
        }
    }
}
