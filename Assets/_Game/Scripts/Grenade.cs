using UnityEngine;

namespace FPS
{
    /// <summary>Frag grenade: bounces, blinks, explodes with falloff damage, line-of-sight check, physics push and camera shake.</summary>
    public class Grenade : MonoBehaviour
    {
        public float fuse = 2.6f;
        public float radius = 7f;
        public float damage = 150f;
        public float push = 9f;
        /// <summary>Dropped from a drone: goes off on the first hard contact (the fuse timer is a backup).</summary>
        public bool impactFuse;
        /// <summary>Kill-feed name of the blast.</summary>
        public string weaponName = "Граната";

        float t;
        bool exploded;
        Renderer led;
        static Material bodyMat, metalMat, ledMat;

        public Combatant thrower;
        /// <summary>Network mirror of someone else's grenade: bounces and blinks; the blast arrives as an Explosion message.</summary>
        public bool visualOnly;

        public static Grenade Spawn(Vector3 pos, Vector3 velocity, Collider owner, Combatant thrower = null, bool visualOnly = false, bool impactFuse = false)
        {
            if (!bodyMat)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                bodyMat = new Material(lit); bodyMat.SetColor("_BaseColor", new Color(0.22f, 0.27f, 0.16f)); bodyMat.SetFloat("_Smoothness", 0.35f);
                metalMat = new Material(lit); metalMat.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f)); metalMat.SetFloat("_Metallic", 1f); metalMat.SetFloat("_Smoothness", 0.6f);
                ledMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); ledMat.SetColor("_BaseColor", new Color(6f, 0.3f, 0.2f));
            }
            var go = new GameObject("Grenade");
            go.transform.position = pos;
            go.transform.rotation = Random.rotation;
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(0.075f, 0.09f, 0.075f);
            body.GetComponent<Renderer>().sharedMaterial = bodyMat;
            var top = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(top.GetComponent<Collider>());
            top.transform.SetParent(go.transform, false);
            top.transform.localPosition = new Vector3(0f, 0.048f, 0f);
            top.transform.localScale = new Vector3(0.028f, 0.012f, 0.028f);
            top.GetComponent<Renderer>().sharedMaterial = metalMat;
            var lever = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(lever.GetComponent<Collider>());
            lever.transform.SetParent(go.transform, false);
            lever.transform.localPosition = new Vector3(0.022f, 0.02f, 0f);
            lever.transform.localScale = new Vector3(0.008f, 0.06f, 0.014f);
            lever.GetComponent<Renderer>().sharedMaterial = metalMat;
            var ledGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ledGO.GetComponent<Collider>());
            ledGO.transform.SetParent(go.transform, false);
            ledGO.transform.localPosition = new Vector3(0f, 0.058f, 0f);
            ledGO.transform.localScale = Vector3.one * 0.01f;
            ledGO.GetComponent<Renderer>().sharedMaterial = ledMat;

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.042f;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.linearDamping = 0.15f;
            rb.angularDamping = 1.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 12f;
            var pm = new PhysicsMaterial("GrenadeBounce") { bounciness = 0.35f, dynamicFriction = 0.6f, staticFriction = 0.6f, bounceCombine = PhysicsMaterialCombine.Maximum };
            col.sharedMaterial = pm;
            var snd = go.AddComponent<PhysicsDebris>();
            snd.sound = Sfx.GrenadeBounce; snd.volume = 0.6f; snd.maxSounds = 5;
            if (owner) Physics.IgnoreCollision(col, owner, true);

            var g = go.AddComponent<Grenade>();
            g.led = ledGO.GetComponent<Renderer>();
            g.thrower = thrower;
            g.visualOnly = visualOnly;
            g.impactFuse = impactFuse;
            if (thrower && visualOnly)
                foreach (var c in thrower.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c, true);
            if (!visualOnly) NetGame.SendGrenade(thrower, pos, velocity, impactFuse);
            return g;
        }

        void Update()
        {
            t += Time.deltaTime;
            if (led)
            {
                float rate = Mathf.Lerp(3f, 14f, t / fuse);
                led.enabled = Mathf.Repeat(t * rate, 1f) < 0.4f;
            }
            if (!exploded && t >= fuse) Explode();
        }

        void Explode()
        {
            exploded = true;
            if (visualOnly) { Destroy(gameObject, 0.25f); return; } // the owner's explosion message plays the blast
            Explosions.Detonate(transform.position, radius, damage, push, thrower, weaponName, transform);
            Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            if (impactFuse && !exploded && t > 0.08f) Explode();
        }
    }
}
