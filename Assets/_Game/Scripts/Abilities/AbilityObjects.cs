using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>The "grenade launcher" charge. Flies straight, smokes, explodes on impact; a parrying sword sends it back.</summary>
    public class RocketProjectile : MonoBehaviour
    {
        public Combatant owner;
        /// <summary>Network mirror of someone else's rocket: flies and smokes, the real blast arrives as an Explosion message.</summary>
        public bool visualOnly;
        Transform ignoreRoot, flame;
        Vector3 vel;
        bool flying;
        float life, puffT, speed;
        Light glow;
        readonly RaycastHit[] hits = new RaycastHit[8];

        public bool Flying => flying;

        public static RocketProjectile Create(float scale)
        {
            var go = new GameObject("ButtRocket");
            var r = go.AddComponent<RocketProjectile>();
            var model = AbilityProps.BuildRocket(go.transform, out r.flame);
            model.localScale = Vector3.one * scale;
            r.flame.gameObject.SetActive(false);
            var lgo = new GameObject("RocketLight");
            lgo.transform.SetParent(go.transform, false);
            lgo.transform.localPosition = new Vector3(0f, 0f, -0.3f * scale);
            r.glow = lgo.AddComponent<Light>();
            r.glow.type = LightType.Point; r.glow.color = new Color(1f, 0.6f, 0.3f); r.glow.range = 6f; r.glow.intensity = 0f; r.glow.shadows = LightShadows.None;
            return r;
        }

        public void Launch(Vector3 dir, float spd, Combatant shooter, Transform ignore)
        {
            transform.SetParent(null, true);
            owner = shooter;
            ignoreRoot = ignore;
            speed = spd;
            vel = dir.normalized * spd;
            transform.rotation = Quaternion.LookRotation(vel);
            flying = true;
            flame.gameObject.SetActive(true);
            glow.intensity = 4f;
            ProcAudio.PlayAt(Sfx.RocketLaunch, transform.position, 1f, 0.05f);
            if (!visualOnly || Net.IsHost) AIEvents.Emit(transform.position, 40f, shooter);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || !flying) return;
            life += dt;
            if (life > 6f) { Explode(transform.position); return; }
            flame.localScale = new Vector3(0.14f, 0.14f, 0.3f) * Random.Range(0.8f, 1.35f);

            // a parrying sword in front bats the rocket back to its sender
            if (!visualOnly)
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || c == owner || !c.abilities || !c.abilities.Parrying) continue;
                Vector3 to = c.Chest - transform.position;
                if (to.sqrMagnitude > 2.6f * 2.6f) continue;
                Vector3 f = c.transform.forward; f.y = 0f;
                Vector3 inc = -vel; inc.y = 0f;
                if (Vector3.Dot(f.normalized, inc.normalized) < 0.25f) continue;
                Vector3 back = owner ? (owner.Chest - transform.position).normalized : -vel.normalized;
                c.abilities.OnParriedProjectile(transform.position);
                owner = c;
                ignoreRoot = c.transform;
                vel = back * speed * 1.15f;
                life = 0f;
                break;
            }

            Vector3 step = vel * dt;
            float len = step.magnitude;
            int n = Physics.RaycastNonAlloc(transform.position, step / len, hits, len + 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Vector3 point = Vector3.zero; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (ignoreRoot && hits[i].collider.transform.IsChildOf(ignoreRoot) && life < 4f) continue;
                if (hits[i].distance < best) { best = hits[i].distance; point = hits[i].point - step / len * 0.1f; any = true; }
            }
            if (any) { Explode(point); return; }
            transform.position += step;
            transform.rotation = Quaternion.LookRotation(vel);

            puffT -= dt;
            if (puffT <= 0f)
            {
                puffT = 0.025f;
                FX.Ensure().Puff(transform.position - vel.normalized * 0.4f, -vel, 1, 0.3f, new Vector2(0.1f, 0.6f), new Vector2(0.3f, 0.6f), new Vector2(0.8f, 1.6f),
                                 new Color(0.75f, 0.74f, 0.72f, 0.45f), new Color(0.6f, 0.6f, 0.6f, 0.35f));
            }
        }

        void Explode(Vector3 p)
        {
            flying = false;
            if (!visualOnly) Explosions.Detonate(p, 6.5f, 125f, 11f, owner, "Ягодичная ракета", transform);
            Destroy(gameObject);
        }
    }

    /// <summary>Thrown banana peel: whoever steps on it slips onto their back.</summary>
    public class BananaPeel : MonoBehaviour
    {
        public static readonly System.Collections.Generic.Dictionary<int, BananaPeel> ById = new System.Collections.Generic.Dictionary<int, BananaPeel>();
        Combatant thrower;
        float age;
        int netId;

        void OnDestroy() { if (netId != 0 && ById.TryGetValue(netId, out var b) && b == this) ById.Remove(netId); }

        /// <summary>Someone stepped on it on another machine.</summary>
        public static void RemoteGone(int id)
        {
            if (!ById.TryGetValue(id, out var b) || !b) return;
            FX.Ensure().Chunks(b.transform.position, Vector3.up, 8, new Color(1f, 0.85f, 0.2f), new Color(0.9f, 0.75f, 0.1f), 3f);
            Destroy(b.gameObject);
        }

        public static void Throw(Vector3 pos, Vector3 velocity, Combatant thrower, int netId = 0)
        {
            var go = new GameObject("BananaPeel");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            AbilityProps.BuildBanana(go.transform, 1.3f);
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.09f;
            col.center = new Vector3(0f, 0.04f, 0f);
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.insideUnitSphere * 6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (thrower)
                foreach (var c in thrower.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(col, c, true);
            var p = go.AddComponent<BananaPeel>();
            p.thrower = thrower;
            p.netId = netId;
            if (netId != 0) ById[netId] = p;
            ProcAudio.PlayAt(Sfx.Throw, pos, 0.7f);
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age > 45f) { Destroy(gameObject); return; }
            if (age < 0.35f) return;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || (c == thrower && age < 2f)) continue;
                if (c.remote) continue; // every machine only checks the characters it simulates
                Vector3 d = c.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 1.1f) continue;
                d.y = 0f;
                if (d.sqrMagnitude > 0.75f * 0.75f) continue;
                if (c.abilities) c.abilities.Slip();
                NetGame.SendBananaGone(netId);
                FX.Ensure().Chunks(transform.position, Vector3.up, 8, new Color(1f, 0.85f, 0.2f), new Color(0.9f, 0.75f, 0.1f), 3f);
                Destroy(gameObject);
                return;
            }
        }
    }

    /// <summary>Thick lingering gas cloud. Blocks bot line of sight.</summary>
    public class SmokeCloud : MonoBehaviour
    {
        public static readonly List<SmokeCloud> All = new List<SmokeCloud>();
        float maxRadius, duration, age;
        public float Radius { get; private set; }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static SmokeCloud Spawn(Vector3 pos, float radius, float duration)
        {
            var go = new GameObject("FartCloud");
            go.transform.position = pos;
            var s = go.AddComponent<SmokeCloud>();
            s.maxRadius = radius;
            s.duration = duration;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(duration - 1.5f, duration + 0.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.7f, radius * 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, radius * 0.35f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.5f, 0.22f, 0.92f), new Color(0.55f, 0.48f, 0.28f, 0.9f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;
            main.gravityModifier = -0.004f;
            var em = ps.emission;
            em.rateOverTime = 40f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 30) });
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius * 0.3f;
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true; lim.limit = 0.25f; lim.dampen = 0.06f;
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.15f, 0.85f), new Keyframe(1f, 1.1f)));
            var rol = ps.rotationOverLifetime;
            rol.enabled = true; rol.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.05f), new GradientAlphaKey(0.9f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FX.Ensure().dustMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.maxParticleSize = 3f;
            ps.Play();
            return s;
        }

        void Update()
        {
            age += Time.deltaTime;
            float grow = Anim.Smooth(age / 1.4f);
            float fade = 1f - Anim.Smooth((age - (duration - 2f)) / 2f);
            Radius = maxRadius * grow * fade;
            if (age > duration + 1.5f) Destroy(gameObject);
        }

        public static bool Blocks(Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len = ab.magnitude;
            if (len < 2.5f) return false;
            Vector3 dir = ab / len;
            foreach (var s in All)
            {
                if (!s || s.Radius < 0.5f) continue;
                Vector3 c = s.transform.position + Vector3.up * 0.8f;
                float t = Mathf.Clamp(Vector3.Dot(c - a, dir), 0f, len);
                float r = s.Radius * 0.85f;
                if ((a + dir * t - c).sqrMagnitude < r * r) return true;
            }
            return false;
        }

        /// <summary>0..1 how deep a point is inside any cloud (player screen fog).</summary>
        public static float Density(Vector3 p)
        {
            float best = 0f;
            foreach (var s in All)
            {
                if (!s || s.Radius < 0.5f) continue;
                float d = Vector3.Distance(p, s.transform.position + Vector3.up * 0.8f);
                best = Mathf.Max(best, Mathf.Clamp01(1.2f - d / s.Radius));
            }
            return best;
        }
    }

    /// <summary>Procedural animated chicken (trap victim). Optionally carries a hitbox for bots.</summary>
    public class ChickenModel : MonoBehaviour
    {
        Transform body, head, lLeg, rLeg, lWing, rWing;
        Vector3 lastPos;
        float phase, flap, speedS;

        public static ChickenModel Build(Transform parent, bool hitbox)
        {
            var root = AbilityProps.Root("Chicken", parent);
            var c = root.gameObject.AddComponent<ChickenModel>();
            var white = AbilityProps.Mat(new Color(0.96f, 0.95f, 0.9f), 0f, 0.25f);
            var red = AbilityProps.Mat(new Color(0.9f, 0.12f, 0.1f), 0f, 0.4f);
            var orange = AbilityProps.Mat(new Color(1f, 0.6f, 0.1f), 0f, 0.4f);
            var black = AbilityProps.Mat(new Color(0.03f, 0.03f, 0.03f), 0f, 0.9f);
            float S = 1.35f;
            root.localScale = Vector3.one * S;

            c.body = AbilityProps.Root("Body", root);
            c.body.localPosition = new Vector3(0f, 0.36f, 0f);
            AbilityProps.Part(PrimitiveType.Sphere, c.body, Vector3.zero, new Vector3(0.36f, 0.32f, 0.44f), white);
            AbilityProps.Part(PrimitiveType.Sphere, c.body, new Vector3(0f, 0.1f, -0.22f), new Vector3(0.16f, 0.22f, 0.1f), white, new Vector3(-30f, 0f, 0f)); // tail
            c.head = AbilityProps.Root("Head", c.body);
            c.head.localPosition = new Vector3(0f, 0.2f, 0.17f);
            AbilityProps.Part(PrimitiveType.Sphere, c.head, new Vector3(0f, 0.08f, 0f), new Vector3(0.17f, 0.2f, 0.17f), white);
            AbilityProps.Part(PrimitiveType.Cube, c.head, new Vector3(0f, 0.07f, 0.1f), new Vector3(0.04f, 0.04f, 0.08f), orange, new Vector3(10f, 0f, 45f));
            AbilityProps.Part(PrimitiveType.Sphere, c.head, new Vector3(0f, 0.01f, 0.08f), new Vector3(0.035f, 0.06f, 0.03f), red);
            AbilityProps.Part(PrimitiveType.Cube, c.head, new Vector3(0f, 0.2f, 0.01f), new Vector3(0.025f, 0.06f, 0.1f), red);
            AbilityProps.Part(PrimitiveType.Cube, c.head, new Vector3(0f, 0.22f, -0.03f), new Vector3(0.025f, 0.05f, 0.04f), red);
            AbilityProps.Part(PrimitiveType.Sphere, c.head, new Vector3(0.07f, 0.11f, 0.05f), Vector3.one * 0.03f, black);
            AbilityProps.Part(PrimitiveType.Sphere, c.head, new Vector3(-0.07f, 0.11f, 0.05f), Vector3.one * 0.03f, black);
            c.lWing = AbilityProps.Root("LWing", c.body); c.lWing.localPosition = new Vector3(-0.17f, 0.04f, 0f);
            AbilityProps.Part(PrimitiveType.Sphere, c.lWing, new Vector3(-0.02f, -0.02f, -0.02f), new Vector3(0.06f, 0.2f, 0.3f), white);
            c.rWing = AbilityProps.Root("RWing", c.body); c.rWing.localPosition = new Vector3(0.17f, 0.04f, 0f);
            AbilityProps.Part(PrimitiveType.Sphere, c.rWing, new Vector3(0.02f, -0.02f, -0.02f), new Vector3(0.06f, 0.2f, 0.3f), white);
            c.lLeg = AbilityProps.Root("LLeg", root); c.lLeg.localPosition = new Vector3(-0.07f, 0.24f, 0f);
            c.rLeg = AbilityProps.Root("RLeg", root); c.rLeg.localPosition = new Vector3(0.07f, 0.24f, 0f);
            foreach (var l in new[] { c.lLeg, c.rLeg })
            {
                AbilityProps.Part(PrimitiveType.Cylinder, l, new Vector3(0f, -0.12f, 0f), new Vector3(0.025f, 0.12f, 0.025f), orange);
                AbilityProps.Part(PrimitiveType.Cube, l, new Vector3(0f, -0.235f, 0.04f), new Vector3(0.08f, 0.015f, 0.1f), orange);
            }
            if (hitbox)
            {
                var cap = root.gameObject.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 0.4f, 0f); cap.radius = 0.24f; cap.height = 0.8f;
                root.gameObject.AddComponent<Hitbox>().multiplier = 1f;
            }
            c.lastPos = root.position;
            return c;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 v = (transform.position - lastPos) / dt;
            lastPos = transform.position;
            float sp = new Vector2(v.x, v.z).magnitude;
            speedS = Anim.Damp(speedS, Mathf.Min(sp, 10f), 10f, dt);
            phase += speedS * dt * 5f;
            float gait = Mathf.Clamp01(speedS / 3f);
            bool air = Mathf.Abs(v.y) > 1.2f;
            flap = Anim.Damp(flap, air ? 1f : gait * 0.35f, 8f, dt);
            float s = Mathf.Sin(phase * Mathf.PI * 2f);
            lLeg.localRotation = Quaternion.Euler(s * 45f * gait, 0f, 0f);
            rLeg.localRotation = Quaternion.Euler(-s * 45f * gait, 0f, 0f);
            float w = Mathf.Sin(Time.time * 30f) * 55f * flap;
            lWing.localRotation = Quaternion.Euler(0f, 0f, -w - 5f);
            rWing.localRotation = Quaternion.Euler(0f, 0f, w + 5f);
            head.localPosition = new Vector3(0f, 0.2f, 0.17f + Mathf.Sin(phase * Mathf.PI * 4f) * 0.04f * gait + Mathf.Sin(Time.time * 7f) * 0.01f);
            head.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * 2.3f) * 20f * (1f - gait), 0f);
            body.localPosition = new Vector3(0f, 0.36f + Mathf.Abs(s) * 0.03f * gait, 0f);
            body.localRotation = Quaternion.Euler(10f * gait, 0f, s * 6f * gait);
        }
    }

    /// <summary>First-person katana: idle sway, six different parry moves, slash.</summary>
    public class SwordViewmodel : MonoBehaviour
    {
        Transform sword;
        float t = -1f, equip, dur;
        KeyFrame[] kp, kr;

        static readonly Vector3 IdlePos = new Vector3(0.25f, -0.27f, 0.5f);
        static readonly Vector3 IdleRot = new Vector3(35f, -10f, 22f);

        static readonly Vector3[] PosePos =
        {
            new Vector3(0.02f, 0.06f, 0.45f), new Vector3(-0.14f, -0.06f, 0.5f), new Vector3(0.3f, -0.05f, 0.45f),
            new Vector3(0.05f, -0.33f, 0.5f), new Vector3(0.02f, -0.14f, 0.42f), new Vector3(0.15f, -0.2f, 0.5f)
        };
        static readonly Vector3[] PoseRot =
        {
            new Vector3(0f, 0f, 90f), new Vector3(25f, 0f, 45f), new Vector3(25f, 0f, -45f),
            new Vector3(100f, 0f, 80f), new Vector3(5f, 0f, -5f), new Vector3(35f, 170f, 22f)
        };

        public static SwordViewmodel Create(Transform cam)
        {
            var root = AbilityProps.Root("SwordViewmodel", cam);
            var vm = root.gameObject.AddComponent<SwordViewmodel>();
            vm.sword = AbilityProps.BuildSword(root, false);
            vm.sword.localScale = Vector3.one * 0.8f;
            vm.equip = 0f;
            return vm;
        }

        public void Parry(int variant)
        {
            int v = Mathf.Abs(variant) % PosePos.Length;
            dur = 0.3f;
            if (v == 5)
            {
                kp = new[] { new KeyFrame(0f, IdlePos), new KeyFrame(0.5f, PosePos[v]), new KeyFrame(1f, IdlePos) };
                kr = new[] { new KeyFrame(0f, IdleRot), new KeyFrame(0.33f, new Vector3(35f, 120f, 22f)), new KeyFrame(0.66f, new Vector3(35f, 240f, 22f)), new KeyFrame(1f, IdleRot + new Vector3(0f, 360f, 0f)) };
                dur = 0.4f;
            }
            else
            {
                kp = new[] { new KeyFrame(0f, IdlePos), new KeyFrame(0.3f, PosePos[v]), new KeyFrame(0.55f, PosePos[v]), new KeyFrame(1f, IdlePos) };
                kr = new[] { new KeyFrame(0f, IdleRot), new KeyFrame(0.3f, PoseRot[v]), new KeyFrame(0.55f, PoseRot[v] + new Vector3(Random.Range(-8f, 8f), 0f, Random.Range(-8f, 8f))), new KeyFrame(1f, IdleRot) };
            }
            t = 0f;
        }

        public void Slash()
        {
            dur = 0.42f;
            kp = new[] { new KeyFrame(0f, IdlePos), new KeyFrame(0.25f, new Vector3(0.35f, 0.08f, 0.38f)), new KeyFrame(0.55f, new Vector3(-0.3f, -0.22f, 0.6f)), new KeyFrame(1f, IdlePos) };
            kr = new[] { new KeyFrame(0f, IdleRot), new KeyFrame(0.25f, new Vector3(-25f, 0f, -60f)), new KeyFrame(0.55f, new Vector3(110f, 0f, 70f)), new KeyFrame(1f, IdleRot) };
            t = 0f;
        }

        public Vector3 BladePoint => sword ? sword.TransformPoint(new Vector3(0f, 0.6f, 0f)) : transform.position;

        void Update()
        {
            float dt = Time.deltaTime;
            equip = Mathf.MoveTowards(equip, 1f, dt / 0.25f);
            Vector3 p = IdlePos, r = IdleRot;
            if (t >= 0f && kp != null)
            {
                t += dt / dur;
                if (t >= 1f) t = -1f;
                else { p = Anim.Eval(kp, t); r = Anim.Eval(kr, t); }
            }
            float sway = Mathf.Sin(Time.time * 1.7f);
            p += new Vector3(sway * 0.005f, Mathf.Sin(Time.time * 2.3f) * 0.006f, 0f);
            float e = 1f - Anim.Smooth(equip);
            p += new Vector3(0f, -0.45f, -0.1f) * e;
            r += new Vector3(40f, 0f, 30f) * e;
            sword.localPosition = p;
            sword.localRotation = Quaternion.Euler(r);
        }
    }
}
