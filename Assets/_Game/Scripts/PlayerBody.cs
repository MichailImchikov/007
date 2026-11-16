using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Drives the local player's full-body model from motor / camera / loadout state.
    /// With an imported (animated) character the body is the first-person body too (true first person: the
    /// FPS.Animation framework puts the player's weapons in its hands, the head renders as a shadow).
    /// Without one the procedural body renders as shadows only and the weapons keep their own procedural arms.
    /// The same parameters are what a network proxy replicates for other players to see the character.
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
        SkinnedBody fpBody;

        /// <summary>The player sees the world through the animated body (true first person).</summary>
        public bool TrueFirstPerson => fpBody && fpBody.FirstPerson;

        void Start()
        {
            // the saved customization: nickname + appearance (seen by others in multiplayer)
            var look = CharacterLook.Local;
            var c = GetComponent<Combatant>();
            if (c) { c.look = look; c.displayName = look.nick; c.human = true; }
            if (rig) look.Apply(rig);
            if (rig) rig.SetShadowsOnly(firstPersonShadowsOnly);
            // true first person on the player's own character model (FPS.Animation framework)
            var sb = rig ? rig.GetComponentInChildren<SkinnedBody>() : null;
            if (sb && loadout && playerCam && sb.EnableFirstPerson(playerCam, loadout, motor)) fpBody = sb;
            if (loadout) loadout.ActiveChanged += OnActiveChanged;
            if (loadout && loadout.Active) OnActiveChanged(loadout.Active);
        }

        void OnDestroy()
        {
            if (loadout) loadout.ActiveChanged -= OnActiveChanged;
        }

        /// <summary>Shadow / third-person copy of the held weapon (not needed when the body holds the real one).</summary>
        void OnActiveChanged(WeaponController w)
        {
            if (TrueFirstPerson) { if (held) { Destroy(held); held = null; heldName = null; } return; }
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
            // on a wall the legs run (others see the body lean off the wall: wallRun, replicated)
            anim.grounded = motor.IsGrounded || motor.Locked || motor.IsWallRunning;
            anim.wallRun = motor.WallSide;
            anim.crouch = motor.CrouchAmount * (1f - motor.ProneAmount);
            anim.prone = motor.ProneAmount;
            anim.lean = playerCam.Lean;
            anim.aimPitch = playerCam.Pitch;
            var w = loadout ? loadout.Active : null;
            anim.aim = w ? Mathf.Max(0.3f, w.AimBlend) : 0f;
            anim.holdingWeapon = w && !SkyArena.LocalRunner;
            anim.pistolHold = w && w.slot == WeaponSlot.Secondary ? 1f : 0f;
            anim.reloading = w && w.IsReloading;
            anim.knife = melee ? melee.SlashProgress : -1f;
            anim.executeAttack = melee ? melee.ExecutionProgress : -1f;
            anim.executeVictim = health ? health.ExecutedProgress : -1f;
            anim.throwT = loadout && loadout.Throwing ? 0.5f : -1f;
            anim.dead = playerCam.DeathAmount;
            anim.deadDir = health && health.DeadForward ? 1f : -1f;
            anim.sliding = motor.IsSliding;
            anim.vault = motor.IsVaulting ? motor.VaultT : -1f;
            anim.mantle = motor.IsVaulting && motor.VaultIsMantle;
            anim.verticalSpeed = motor.IsGrounded || motor.IsWallRunning ? 0f : motor.Velocity.y;
        }
    }
}
