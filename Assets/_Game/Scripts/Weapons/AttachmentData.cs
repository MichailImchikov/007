using UnityEngine;

namespace FPS
{
    /// <summary>Attachment slot = a socket on a weapon model (Blender sockets Socket_Optic, Socket_Muzzle, ...).</summary>
    public enum AttachmentSlot { Optic, Muzzle, Underbarrel, Side, Magazine }

    /// <summary>
    /// A weapon attachment (Resources/Weapons/Attachments): its model, slot and the stat changes it makes.
    /// Multipliers stack over the weapon's base stats (always re-applied from the base, never on top of each other).
    /// Socket conventions of the models (Tools/Blender/weapons): muzzle devices carry a new Socket_Muzzle, optics a
    /// Sight_Point (+ Reticle / Lens), foregrips a Grip_L, lasers a Laser_Emitter, lights a Light_Emitter; an extended
    /// magazine shares the weapon's Mag origin.
    /// </summary>
    [CreateAssetMenu(menuName = "FPS/Attachment Data", fileName = "Attachment")]
    public class AttachmentData : ScriptableObject
    {
        [Tooltip("Saved in the player's loadout (= the model / manifest id)")] public string id;
        [Tooltip("Short name for the menu chips")] public string displayName;
        [TextArea(2, 4)] public string description;
        public AttachmentSlot slot;
        [Tooltip("Model (FBX) placed at the slot's socket")] public GameObject prefab;
        [Tooltip("Manifest weapon ids it fits (the WeaponData slot lists decide)")] public string[] compatible = new string[0];

        [Header("Handling (multipliers, 1 = no change)")]
        [Tooltip("Camera recoil, view kick")] public float recoilMul = 1f;
        [Tooltip("Hip-fire and movement spread")] public float hipSpreadMul = 1f;
        public float adsSpreadMul = 1f;
        [Tooltip("Time to aim down sights")] public float adsTimeMul = 1f;
        public float moveSpeedMul = 1f;
        public float reloadTimeMul = 1f;
        [Tooltip("Range and damage falloff distances")] public float rangeMul = 1f;
        public float damageMul = 1f;

        [Header("Optic")]
        public float magnification = 1f;
        [Tooltip("Field of view while aiming (0 = the weapon's own)")] public float adsFov;
        [Tooltip("Eye distance behind the sight point when aiming (0 = the weapon's own)")] public float aimDistance;
        [Tooltip("Full-screen scope overlay when aimed (sniper scope)")] public bool scoped;
        [Tooltip("Measured by the builder: the optic's body blocks the view along its sight line (no see-through bore), " +
                 "so aimed it works like a scope (full-screen overlay) instead of covering the screen")]
        public bool opaqueSightLine;
        [Tooltip("The model has no reticle mesh: a red dot is added on the sight line")] public bool addReticle;
        [Tooltip("Material of the added reticle dot")] public Material reticleMaterial;

        [Header("Muzzle")]
        [Tooltip("Quieter shot, no muzzle light, smaller flash, bots hear it from less far")] public bool suppressed;
        public float flashMul = 1f;
        public float volumeMul = 1f;
        [Tooltip("Radius in which bots hear the shot")] public float noiseMul = 1f;
        public float muzzleLength;

        [Header("Magazine")]
        [Tooltip("Magazine capacity (0 = the weapon's own)")] public int magCapacity;

        [Header("Tactical devices")]
        [Tooltip("Laser beam + dot from Laser_Emitter")] public bool laser;
        [Tooltip("Spot light from Light_Emitter (the flashlight key)")] public bool flashlight;
        [Tooltip("The support hand holds it (Grip_L of a vertical / angled foregrip)")] public bool foregrip;

        public string Name => string.IsNullOrEmpty(displayName) ? name : displayName;
        /// <summary>Aimed, the weapon is hidden behind the HUD scope overlay.</summary>
        /// <summary>
        /// Full-screen scope overlay: asked for, or a 1x sight whose body blocks the sight line. A magnified optic draws its
        /// picture on the rear lens (OpticSight), so a solid tube never sends it to the overlay.
        /// </summary>
        public bool UsesScopeOverlay => scoped || (opaqueSightLine && magnification <= 1.01f);
    }
}
