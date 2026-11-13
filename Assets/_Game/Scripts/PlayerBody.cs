using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Drives the local player's full-body model from motor / camera / loadout state.
    /// Locally the body renders as shadows only; the same parameters are what a network
    /// proxy would replicate later for other players to see the full animated character.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public class PlayerBody : MonoBehaviour
    {
        public PlayerMotor motor;
        public PlayerCamera playerCam;
        public Loadout loadout;
        public MeleeController melee;
        public CharacterAnimator anim;
        public HumanoidRig rig;
        public bool firstPersonShadowsOnly = true;
        PlayerHealth health;

        GameObject held;
        string heldName;

        void Start()
        {
            // the saved customization: nickname + appearance (seen by others in multiplayer, as a shadow by yourself)
            var look = CharacterLook.Local;
            var c = GetComponent<Combatant>();
            if (c) { c.look = look; c.displayName = look.nick; c.human = true; }
            if (rig) look.Apply(rig);
            if (rig) rig.SetShadowsOnly(firstPersonShadowsOnly);
            if (loadout) loadout.ActiveChanged += OnActiveChanged;
            if (loadout && loadout.Active) OnActiveChanged(loadout.Active);
            // first-person arms on the FPS Animation Framework (the player's own character model)
            if (!fpArms && loadout && playerCam) fpArms = FPArms.Create(loadout, playerCam, motor);
        }

        public FPArms fpArms;

        void OnDestroy()
        {
            if (loadout) loadout.ActiveChanged -= OnActiveChanged;
        }

        void OnActiveChanged(WeaponController w)
        {
            if (!w || w.weaponName == heldName || !rig || !rig.weaponSocket) return;
            if (held) Destroy(held);
            heldName = w.weaponName;
            held = loadout.CreateWorldModel(w.weaponName, rig.weaponSocket);
            if (held && firstPersonShadowsOnly)
                foreach (var r in held.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        }

        void Update()
        {
            if (!anim) return;
            if (!health) health = GetComponent<PlayerHealth>();
            anim.localVelocity = transform.InverseTransformDirection(motor.Velocity);
            anim.grounded = motor.IsGrounded || motor.Locked;
            anim.crouch = motor.CrouchAmount * (1f - motor.ProneAmount);
            anim.prone = motor.ProneAmount;
            anim.lean = playerCam.Lean;
            anim.aimPitch = playerCam.Pitch;
            var w = loadout ? loadout.Active : null;
            anim.aim = w ? Mathf.Max(0.3f, w.AimBlend) : 0f;
            anim.holdingWeapon = w;
            anim.pistolHold = w && w.slot == WeaponSlot.Secondary ? 1f : 0f;
            anim.reloading = w && w.IsReloading;
            anim.knife = melee ? melee.SlashProgress : -1f;
            anim.executeAttack = melee ? melee.ExecutionProgress : -1f;
            anim.executeVictim = health ? health.ExecutedProgress : -1f;
            anim.throwT = loadout && loadout.Throwing ? 0.5f : -1f;
            anim.dead = playerCam.DeathAmount;
            anim.deadDir = health && health.DeadForward ? 1f : -1f;
        }
    }
}
