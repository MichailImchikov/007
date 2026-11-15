using System;
using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Everything the layers need to know about the held weapon (KINEMATION's WeaponAnimData / WeaponAnimAsset).
    /// Built at runtime from the weapon's gameplay numbers; a WeaponAnimProfile asset can override it per weapon.
    /// </summary>
    [Serializable]
    public class WeaponAnimSettings
    {
        [Header("Hold pose")]
        [Tooltip("Weapon bone in root bone (camera / eye) space: the hip-fire hold")] public LocRot weaponBone = new LocRot(new Vector3(0.155f, -0.175f, 0.33f), Quaternion.identity);
        [Tooltip("Rotation offset of the weapon model (degrees)")] public Vector3 rotationOffset;
        [Tooltip("Offsets the arms + weapon in root space (per-weapon view tweak)")] public LocRot viewOffset = LocRot.Identity;

        [Header("Aiming (AdsLayer)")]
        public AdsData ads = new AdsData(5f);
        [Tooltip("Distance from the eye to the sight when aiming (m)")] public float aimDistance = 0.21f;

        [Header("Sway (SwayLayer)")]
        public LocRotSpring sway = new LocRotSpring
        {
            loc = new VectorSpring(160f, 0.55f, new Vector3(-0.012f, -0.012f, 0f)),
            rot = new VectorSpring(150f, 0.5f, new Vector3(0.9f, -0.7f, -1.2f)),
        };
        public FreeAimData freeAim = new FreeAimData { scalar = 0.15f, maxValue = 3f, speed = 10f };
        public MoveSwayData moveSway = new MoveSwayData
        {
            maxMoveLocSway = new Vector3(-0.01f, 0f, -0.012f),
            maxMoveRotSway = new Vector3(0f, 0f, -3.5f),
            moveLocSway = new VectorSpring(120f, 0.6f),
            moveRotSway = new VectorSpring(120f, 0.6f),
            locSpeed = new Vector3(8f, 8f, 8f),
            rotSpeed = new Vector3(8f, 8f, 8f),
        };

        [Header("Wall collision (WeaponCollisionLayer)")]
        public GunBlockData block = new GunBlockData(new LocRot(new Vector3(0.02f, -0.06f, -0.08f), Quaternion.Euler(-35f, -15f, 12f)));

        [Header("Recoil")]
        public RecoilProfile recoil;
        public CameraShakeProfile shotShake;

        [Header("Poses")]
        [Tooltip("Upper body hold pose (overlay) for this weapon")] public AnimSequence overlayPose;

        [Header("Hands")]
        [Tooltip("Hands are placed on the weapon's grip anchors (GripR / GripL) instead of the animated hand pose")] public bool gripIK = true;
        [Tooltip("Elbow direction hints in root space")] public Vector3 rightElbowHint = new Vector3(0.6f, -1f, -0.2f);
        public Vector3 leftElbowHint = new Vector3(-0.8f, -1f, 0f);

        public WeaponAnimSettings Clone() => (WeaponAnimSettings)MemberwiseClone();
    }

    [CreateAssetMenu(fileName = "NewWeaponAnimProfile", menuName = "FPS/Animation/Weapon Anim Profile")]
    public class WeaponAnimProfile : ScriptableObject
    {
        public WeaponAnimSettings settings = new WeaponAnimSettings();
    }
}
