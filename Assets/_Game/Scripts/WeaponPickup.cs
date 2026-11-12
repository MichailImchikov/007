using UnityEngine;

namespace FPS
{
    /// <summary>A weapon (or ammo crate) lying in the world. Look at it and press X.</summary>
    public class WeaponPickup : MonoBehaviour
    {
        public enum Kind { Weapon, Ammo }
        public Kind kind = Kind.Weapon;
        public string weaponName;
        public bool physical;
        public Vector3 initialVelocity;
        public bool spin = true;

        Transform visual;
        float baseY, phase;

        public string Label => kind == Kind.Ammo ? "Боеприпасы" : weaponName;

        void Start()
        {
            if (kind == Kind.Weapon && transform.childCount == 0)
            {
                var loadout = FindAnyObjectByType<Loadout>();
                if (loadout)
                {
                    var m = loadout.CreateWorldModel(weaponName, transform);
                    if (m) visual = m.transform;
                }
            }
            if (!GetComponent<Collider>())
            {
                var rends = GetComponentsInChildren<Renderer>();
                var bc = gameObject.AddComponent<BoxCollider>();
                if (rends.Length > 0)
                {
                    Bounds b = rends[0].bounds;
                    foreach (var r in rends) b.Encapsulate(r.bounds);
                    bc.center = transform.InverseTransformPoint(b.center);
                    Vector3 s = transform.InverseTransformVector(b.size);
                    bc.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) + Vector3.one * 0.04f;
                }
            }
            if (physical)
            {
                spin = false;
                var rb = gameObject.AddComponent<Rigidbody>();
                rb.mass = 3f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.linearVelocity = initialVelocity;
                rb.angularVelocity = Random.insideUnitSphere * 4f;
                var snd = gameObject.AddComponent<PhysicsDebris>();
                snd.sound = Sfx.MagDrop; snd.volume = 0.7f; snd.maxSounds = 3;
                var player = FindAnyObjectByType<PlayerMotor>();
                if (player) Physics.IgnoreCollision(GetComponent<Collider>(), player.Controller, true);
            }
            baseY = transform.position.y;
            phase = Random.value * 10f;
        }

        void Update()
        {
            if (!spin || !visual) return;
            phase += Time.deltaTime;
            visual.localRotation = Quaternion.Euler(0f, phase * 40f, 0f);
            visual.localPosition = new Vector3(0f, 0.06f + Mathf.Sin(phase * 2f) * 0.025f, 0f);
        }
    }
}
