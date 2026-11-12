using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// Pooled combat effects: tracers, impact particles, bullet holes, shell casings,
    /// dropped magazines and muzzle smoke. Materials can be assigned in the scene
    /// (so shaders are included in builds) or are created at runtime.
    /// </summary>
    public class FX : MonoBehaviour
    {
        public static FX Instance { get; private set; }

        [Header("Optional material assets (created at runtime if empty)")]
        public Material sparkMaterial;
        public Material dustMaterial;
        public Material tracerMaterial;
        public Material decalMaterial;
        public Material brassMaterial;

        ParticleSystem sparks, metalSparks, dust, debris, smoke, fire, bigSmoke;
        Material shellRedMaterial;
        Transform[] scorches; int scorchIdx;
        LineRenderer[] tracers;
        Vector3[] trA, trB; float[] trDist, trTravel; int trIdx;
        Transform[] decals; int decalIdx;
        Rigidbody[] shells; PhysicsDebris[] shellSnd; int shellIdx;
        readonly Queue<GameObject> droppedMags = new Queue<GameObject>();
        Collider player;

        const float TracerSpeed = 420f, TracerLength = 9f;

        public static FX Ensure()
        {
            if (Instance) return Instance;
            var existing = FindAnyObjectByType<FX>();
            if (existing) { Instance = existing; return existing; }
            Instance = new GameObject("FX").AddComponent<FX>();
            return Instance;
        }

        public void SetPlayer(Collider c) => player = c;

        void Awake()
        {
            Instance = this;
            var soft = SoftCircle(64, 2.2f);
            if (!sparkMaterial) sparkMaterial = MakeParticleMat("FX_Spark", soft, true, new Color(3.5f, 3.5f, 3.5f, 1f));
            if (!dustMaterial) dustMaterial = MakeParticleMat("FX_Dust", SmokeTex(64, 5), false, Color.white);
            if (!tracerMaterial) tracerMaterial = MakeParticleMat("FX_Tracer", TracerTex(), true, new Color(3f, 3f, 3f, 1f));
            if (!decalMaterial) decalMaterial = MakeParticleMat("FX_Decal", BulletHoleTex(64, 3), false, Color.white);
            if (!brassMaterial)
            {
                brassMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                brassMaterial.SetColor("_BaseColor", new Color(0.86f, 0.64f, 0.27f));
                brassMaterial.SetFloat("_Metallic", 1f);
                brassMaterial.SetFloat("_Smoothness", 0.72f);
            }
            Build();
            EnsureExtra();
        }

        void Build()
        {
            sparks = MakePS("Sparks", sparkMaterial, true, 1.4f, true);
            metalSparks = MakePS("MetalSparks", sparkMaterial, true, 1.1f, true);
            dust = MakePS("Dust", dustMaterial, false, -0.05f, false);
            var sol = dust.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.3f, 0.8f), new Keyframe(1f, 1f)));
            var dl = dust.limitVelocityOverLifetime; dl.enabled = true; dl.limit = 0.4f; dl.dampen = 0.15f;
            debris = MakePS("Debris", dustMaterial, false, 1.6f, true);
            smoke = MakePS("MuzzleSmoke", dustMaterial, false, -0.08f, false);
            var ssol = smoke.sizeOverLifetime; ssol.enabled = true;
            ssol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1f));

            tracers = new LineRenderer[24];
            trA = new Vector3[24]; trB = new Vector3[24]; trDist = new float[24]; trTravel = new float[24];
            for (int i = 0; i < tracers.Length; i++)
            {
                var go = new GameObject("Tracer");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = tracerMaterial;
                lr.positionCount = 2;
                lr.useWorldSpace = true;
                lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.006f), new Keyframe(1f, 0.022f));
                lr.textureMode = LineTextureMode.Stretch;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.55f, 0.2f), 0f), new GradientColorKey(new Color(1f, 0.9f, 0.6f), 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                lr.colorGradient = g;
                lr.enabled = false;
                tracers[i] = lr;
                trTravel[i] = -1f;
            }

            decals = new Transform[120];
            for (int i = 0; i < decals.Length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "BulletHole";
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = decalMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                q.SetActive(false);
                decals[i] = q.transform;
            }

            shells = new Rigidbody[32];
            shellSnd = new PhysicsDebris[32];
            for (int i = 0; i < shells.Length; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                s.name = "Shell";
                s.transform.SetParent(transform, false);
                s.transform.localScale = new Vector3(0.011f, 0.0125f, 0.011f);
                var mr = s.GetComponent<MeshRenderer>();
                mr.sharedMaterial = brassMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                var rb = s.AddComponent<Rigidbody>();
                rb.mass = 0.012f;
                rb.linearDamping = 0.1f;
                rb.angularDamping = 0.2f;
                rb.maxAngularVelocity = 60f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var snd = s.AddComponent<PhysicsDebris>();
                snd.sound = Sfx.Shell; snd.volume = 0.28f; snd.maxSounds = 2;
                s.SetActive(false);
                shells[i] = rb; shellSnd[i] = snd;
            }
        }

        ParticleSystem MakePS(string name, Material mat, bool stretch, float gravity, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1500;
            main.gravityModifier = gravity;
            main.startSpeed = 0f;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (collide)
            {
                var c = ps.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World;
                c.mode = ParticleSystemCollisionMode.Collision3D; c.quality = ParticleSystemCollisionQuality.Low;
                c.dampen = 0.5f; c.bounce = 0.35f; c.lifetimeLoss = 0.2f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (stretch) { r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.035f; r.lengthScale = 1.2f; }
            else r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        static void Burst(ParticleSystem ps, Vector3 pos, Vector3 dir, int count, float cone, Vector2 speed, Vector2 size, Vector2 life, Color col, Color col2)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector3 d = Vector3.Slerp(dir, Random.onUnitSphere, cone).normalized;
                if (Vector3.Dot(d, dir) < 0f) d = Vector3.Reflect(d, dir);
                ep.position = pos + dir * 0.02f;
                ep.velocity = d * Random.Range(speed.x, speed.y);
                ep.startSize = Random.Range(size.x, size.y);
                ep.startLifetime = Random.Range(life.x, life.y);
                ep.startColor = Color.Lerp(col, col2, Random.value);
                ep.rotation = Random.Range(0f, 360f);
                ps.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------ API

        public void Impact(RaycastHit hit, SurfaceKind kind, bool decal)
        {
            Vector3 p = hit.point, n = hit.normal;
            if (kind == SurfaceKind.Metal)
            {
                Burst(metalSparks, p, n, Random.Range(10, 16), 0.55f, new Vector2(3f, 9f), new Vector2(0.012f, 0.03f), new Vector2(0.12f, 0.4f),
                      new Color(1f, 0.7f, 0.3f, 1f), new Color(1f, 0.45f, 0.12f, 1f));
                Burst(dust, p, n, 2, 0.4f, new Vector2(0.2f, 0.6f), new Vector2(0.1f, 0.18f), new Vector2(0.3f, 0.5f),
                      new Color(0.8f, 0.8f, 0.8f, 0.35f), new Color(0.6f, 0.6f, 0.6f, 0.25f));
                ProcAudio.PlayAt(Sfx.ImpactMetal, p, 0.45f, 0.06f);
            }
            else
            {
                Color dustA = kind == SurfaceKind.Wood ? new Color(0.55f, 0.42f, 0.3f, 0.55f) : new Color(0.62f, 0.6f, 0.57f, 0.55f);
                Color dustB = dustA * 0.8f; dustB.a = 0.4f;
                Burst(dust, p, n, Random.Range(5, 8), 0.35f, new Vector2(0.4f, 1.8f), new Vector2(0.12f, 0.3f), new Vector2(0.6f, 1.3f), dustA, dustB);
                Burst(debris, p, n, Random.Range(5, 9), 0.5f, new Vector2(2f, 5f), new Vector2(0.015f, 0.035f), new Vector2(0.5f, 1f),
                      new Color(0.25f, 0.24f, 0.22f, 1f), new Color(0.4f, 0.38f, 0.35f, 1f));
                Burst(sparks, p, n, Random.Range(2, 5), 0.6f, new Vector2(4f, 8f), new Vector2(0.01f, 0.02f), new Vector2(0.06f, 0.15f),
                      new Color(1f, 0.65f, 0.25f, 1f), new Color(1f, 0.5f, 0.15f, 1f));
                ProcAudio.PlayAt(Sfx.ImpactConcrete, p, 0.55f, 0.1f);
                if (decal) PlaceDecal(hit);
            }
        }

        void PlaceDecal(RaycastHit hit)
        {
            var d = decals[decalIdx];
            decalIdx = (decalIdx + 1) % decals.Length;
            d.gameObject.SetActive(true);
            d.SetParent(transform, false);
            d.position = hit.point + hit.normal * 0.004f;
            d.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            d.localScale = Vector3.one * Random.Range(0.06f, 0.09f);
        }

        public void Tracer(Vector3 from, Vector3 to)
        {
            int i = trIdx;
            trIdx = (trIdx + 1) % tracers.Length;
            trA[i] = from; trB[i] = to;
            trDist[i] = Vector3.Distance(from, to);
            trTravel[i] = 0f;
            tracers[i].enabled = true;
            tracers[i].SetPosition(0, from);
            tracers[i].SetPosition(1, from);
        }

        public void EjectShell(Vector3 pos, Transform cam, Vector3 playerVelocity, float scale = 1f)
        {
            var rb = shells[shellIdx];
            var snd = shellSnd[shellIdx];
            shellIdx = (shellIdx + 1) % shells.Length;
            var go = rb.gameObject;
            go.SetActive(true);
            go.transform.localScale = new Vector3(0.011f, 0.0125f, 0.011f) * scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = scale > 1.4f ? shellRedMaterial : brassMaterial;
            rb.position = pos;
            rb.rotation = cam.rotation * Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(Random.Range(-15f, 15f), 0f, 0f);
            go.transform.SetPositionAndRotation(rb.position, rb.rotation);
            rb.linearVelocity = playerVelocity + cam.right * Random.Range(2.2f, 3.2f) + cam.up * Random.Range(1.4f, 2.3f) - cam.forward * Random.Range(0.2f, 0.6f);
            rb.angularVelocity = Random.insideUnitSphere * 30f;
            snd.ResetSounds();
            if (player) Physics.IgnoreCollision(go.GetComponent<Collider>(), player, true);
        }

        public void DropMagazine(Transform mag, Vector3 playerVelocity, Transform cam)
        {
            var clone = Instantiate(mag.gameObject, mag.position, mag.rotation);
            clone.name = "DroppedMag";
            clone.transform.localScale = mag.lossyScale;
            var rends = clone.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Destroy(clone); return; }
            Bounds wb = rends[0].bounds;
            foreach (var r in rends) { wb.Encapsulate(r.bounds); r.shadowCastingMode = ShadowCastingMode.On; }
            var bc = clone.AddComponent<BoxCollider>();
            bc.center = clone.transform.InverseTransformPoint(wb.center);
            Vector3 sz = clone.transform.InverseTransformVector(wb.size);
            bc.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z)) * 0.8f;
            var rb = clone.AddComponent<Rigidbody>();
            rb.mass = 0.45f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearVelocity = playerVelocity - cam.up * 1.2f - cam.right * 0.3f + cam.forward * 0.3f;
            rb.angularVelocity = new Vector3(Random.Range(-6f, 6f), Random.Range(-3f, 3f), Random.Range(-6f, 6f));
            var snd = clone.AddComponent<PhysicsDebris>();
            snd.sound = Sfx.MagDrop; snd.volume = 0.6f; snd.maxSounds = 2;
            if (player) Physics.IgnoreCollision(bc, player, true);
            droppedMags.Enqueue(clone);
            while (droppedMags.Count > 10)
            {
                var old = droppedMags.Dequeue();
                if (old) Destroy(old);
            }
        }

        public void MuzzleSmoke(Vector3 pos, Vector3 forward, int amount)
        {
            Burst(smoke, pos, Vector3.up, amount, 0.25f, new Vector2(0.15f, 0.45f), new Vector2(0.06f, 0.12f), new Vector2(0.9f, 1.6f),
                  new Color(0.85f, 0.85f, 0.85f, 0.18f), new Color(0.7f, 0.7f, 0.72f, 0.12f));
            Burst(smoke, pos + forward * 0.05f, forward, 1, 0.2f, new Vector2(0.3f, 0.6f), new Vector2(0.05f, 0.08f), new Vector2(0.6f, 1f),
                  new Color(0.85f, 0.85f, 0.85f, 0.14f), new Color(0.7f, 0.7f, 0.72f, 0.1f));
        }

        // ------------------------------------------------------------------ explosions & blood

        void EnsureExtra()
        {
            if (fire) return;
            if (!shellRedMaterial)
            {
                shellRedMaterial = new Material(brassMaterial);
                shellRedMaterial.SetColor("_BaseColor", new Color(0.7f, 0.1f, 0.08f));
                shellRedMaterial.SetFloat("_Metallic", 0.2f);
            }
            var fireMat = MakeParticleMat("FX_Fire", SmokeTex(64, 9), true, new Color(2.6f, 2.2f, 1.9f, 1f));
            fire = MakePS("Fire", fireMat, false, -0.15f, false);
            var fsol = fire.sizeOverLifetime; fsol.enabled = true;
            fsol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.2f)));
            var fl = fire.limitVelocityOverLifetime; fl.enabled = true; fl.limit = 1.5f; fl.dampen = 0.25f;
            bigSmoke = MakePS("ExplosionSmoke", dustMaterial, false, -0.06f, false);
            var bsol = bigSmoke.sizeOverLifetime; bsol.enabled = true;
            bsol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.2f, 0.8f), new Keyframe(1f, 1.4f)));
            var bl = bigSmoke.limitVelocityOverLifetime; bl.enabled = true; bl.limit = 0.8f; bl.dampen = 0.08f;
            var scorchMat = MakeParticleMat("FX_Scorch", ScorchTex(128, 4), false, Color.white);
            scorches = new Transform[12];
            for (int i = 0; i < scorches.Length; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Scorch";
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = scorchMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                q.SetActive(false);
                scorches[i] = q.transform;
            }
        }

        public void Explosion(Vector3 pos, Vector3 groundNormal, bool onGround)
        {
            EnsureExtra();
            Vector3 up = groundNormal.sqrMagnitude > 0.1f ? groundNormal : Vector3.up;
            Burst(fire, pos + up * 0.3f, up, 3, 0.3f, new Vector2(0f, 0.5f), new Vector2(3f, 4.5f), new Vector2(0.08f, 0.14f), new Color(1f, 0.95f, 0.85f, 1f), Color.white);
            Burst(fire, pos, up, 16, 0.75f, new Vector2(2f, 7f), new Vector2(0.8f, 1.6f), new Vector2(0.25f, 0.55f),
                  new Color(1f, 0.6f, 0.25f, 0.9f), new Color(1f, 0.35f, 0.1f, 0.8f));
            Burst(sparks, pos, up, 40, 0.7f, new Vector2(8f, 24f), new Vector2(0.02f, 0.05f), new Vector2(0.3f, 0.9f),
                  new Color(1f, 0.7f, 0.3f, 1f), new Color(1f, 0.45f, 0.1f, 1f));
            Burst(debris, pos, up, 26, 0.6f, new Vector2(4f, 12f), new Vector2(0.03f, 0.07f), new Vector2(0.8f, 1.6f),
                  new Color(0.15f, 0.14f, 0.13f, 1f), new Color(0.3f, 0.28f, 0.25f, 1f));
            Burst(bigSmoke, pos + up * 0.3f, up, 14, 0.8f, new Vector2(1f, 4f), new Vector2(1.2f, 2.6f), new Vector2(2.5f, 4.5f),
                  new Color(0.32f, 0.3f, 0.29f, 0.55f), new Color(0.5f, 0.48f, 0.46f, 0.45f));
            Burst(dust, pos, up, 12, 0.95f, new Vector2(2f, 6f), new Vector2(0.5f, 1f), new Vector2(0.8f, 1.6f),
                  new Color(0.6f, 0.57f, 0.52f, 0.5f), new Color(0.5f, 0.47f, 0.43f, 0.4f));

            var lgo = new GameObject("ExplosionLight");
            lgo.transform.position = pos + up * 0.5f;
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Point; l.range = 14f; l.color = new Color(1f, 0.65f, 0.35f); l.intensity = 30f; l.shadows = LightShadows.None;
            StartCoroutine(FadeLight(l, 0.45f));

            if (onGround)
            {
                var s = scorches[scorchIdx];
                scorchIdx = (scorchIdx + 1) % scorches.Length;
                s.gameObject.SetActive(true);
                s.position = pos + up * 0.01f;
                s.rotation = Quaternion.LookRotation(-up) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                s.localScale = Vector3.one * Random.Range(2.6f, 3.4f);
            }
        }

        System.Collections.IEnumerator FadeLight(Light l, float time)
        {
            float t = 0f, i0 = l.intensity;
            while (t < time && l)
            {
                t += Time.deltaTime;
                float u = t / time;
                l.intensity = i0 * (1f - u) * (1f - u);
                yield return null;
            }
            if (l) Destroy(l.gameObject);
        }

        public void Blood(Vector3 pos, Vector3 dir, int amount = 10)
        {
            EnsureExtra();
            Burst(debris, pos, dir, amount, 0.45f, new Vector2(1.5f, 4.5f), new Vector2(0.012f, 0.03f), new Vector2(0.4f, 0.9f),
                  new Color(0.5f, 0.02f, 0.02f, 1f), new Color(0.3f, 0.01f, 0.01f, 1f));
            Burst(dust, pos, dir, Mathf.Max(2, amount / 3), 0.4f, new Vector2(0.3f, 1.2f), new Vector2(0.1f, 0.22f), new Vector2(0.35f, 0.7f),
                  new Color(0.55f, 0.03f, 0.03f, 0.7f), new Color(0.35f, 0.02f, 0.02f, 0.6f));
        }

        // ------------------------------------------------------------------ generic bursts (abilities, destruction)

        /// <summary>Big soft smoke-like particles (fart clouds, dust of broken cover).</summary>
        public void Puff(Vector3 pos, Vector3 dir, int count, float cone, Vector2 speed, Vector2 size, Vector2 life, Color a, Color b)
        {
            EnsureExtra();
            Burst(bigSmoke, pos, dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.up, count, cone, speed, size, life, a, b);
        }

        public void SparkBurst(Vector3 pos, Vector3 dir, int count, float speed = 9f)
        {
            Burst(sparks, pos, dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.up, count, 0.55f, new Vector2(speed * 0.4f, speed), new Vector2(0.015f, 0.035f), new Vector2(0.15f, 0.45f),
                  new Color(1f, 0.85f, 0.5f, 1f), new Color(1f, 0.6f, 0.25f, 1f));
        }

        public void Chunks(Vector3 pos, Vector3 dir, int count, Color a, Color b, float speed = 5f)
        {
            EnsureExtra();
            Burst(debris, pos, dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.up, count, 0.7f, new Vector2(speed * 0.3f, speed), new Vector2(0.025f, 0.07f), new Vector2(0.6f, 1.4f), a, b);
        }

        public void DustBurst(Vector3 pos, Vector3 dir, int count, Color a, Color b, float size = 0.6f)
        {
            Burst(dust, pos, dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.up, count, 0.9f, new Vector2(0.5f, 2.5f), new Vector2(size * 0.6f, size * 1.4f), new Vector2(0.8f, 1.8f), a, b);
        }

        Light[] flashLights;
        float[] flashTimers;
        int flashIdx;

        /// <summary>Third-person muzzle flash (bots, later network players).</summary>
        public void MuzzleFlashWorld(Vector3 pos, Vector3 dir)
        {
            EnsureExtra();
            Burst(fire, pos + dir * 0.05f, dir, 2, 0.15f, new Vector2(0.5f, 1.5f), new Vector2(0.18f, 0.32f), new Vector2(0.04f, 0.07f),
                  new Color(1f, 0.85f, 0.55f, 1f), new Color(1f, 0.6f, 0.25f, 1f));
            Burst(sparks, pos, dir, 3, 0.25f, new Vector2(6f, 12f), new Vector2(0.01f, 0.02f), new Vector2(0.05f, 0.1f),
                  new Color(1f, 0.75f, 0.35f, 1f), new Color(1f, 0.55f, 0.2f, 1f));
            if (flashLights == null)
            {
                flashLights = new Light[6];
                flashTimers = new float[6];
                for (int i = 0; i < flashLights.Length; i++)
                {
                    var go = new GameObject("FlashLight");
                    go.transform.SetParent(transform, false);
                    var l = go.AddComponent<Light>();
                    l.type = LightType.Point; l.range = 5f; l.color = new Color(1f, 0.7f, 0.4f); l.shadows = LightShadows.None;
                    l.enabled = false;
                    flashLights[i] = l;
                }
            }
            var fl = flashLights[flashIdx];
            flashTimers[flashIdx] = 0.05f;
            flashIdx = (flashIdx + 1) % flashLights.Length;
            fl.transform.position = pos + dir * 0.1f;
            fl.intensity = 3f;
            fl.enabled = true;
        }

        void LateUpdate()
        {
            if (flashLights == null) return;
            for (int i = 0; i < flashLights.Length; i++)
            {
                if (flashTimers[i] <= 0f) continue;
                flashTimers[i] -= Time.deltaTime;
                if (flashTimers[i] <= 0f) flashLights[i].enabled = false;
            }
        }

        public static Texture2D ScorchTex(int size, int seed)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Scorch", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float o = seed * 3.7f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    float dx = u * 2f - 1f, dy = v * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = Mathf.PerlinNoise(u * 6f + o, v * 6f + o) * 0.6f + Mathf.PerlinNoise(u * 15f - o, v * 15f + o) * 0.4f;
                    float a = Mathf.Clamp01((0.95f - r - n * 0.35f) / 0.4f);
                    a = a * a * 0.85f;
                    byte g = (byte)(10 + n * 25);
                    px[y * size + x] = new Color32(g, g, g, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < tracers.Length; i++)
            {
                if (trTravel[i] < 0f) continue;
                trTravel[i] += TracerSpeed * dt;
                float head = Mathf.Min(trTravel[i], trDist[i]);
                float tail = Mathf.Max(0f, trTravel[i] - TracerLength);
                if (tail >= trDist[i]) { trTravel[i] = -1f; tracers[i].enabled = false; continue; }
                Vector3 dir = trDist[i] > 0.001f ? (trB[i] - trA[i]) / trDist[i] : Vector3.forward;
                tracers[i].SetPosition(0, trA[i] + dir * tail);
                tracers[i].SetPosition(1, trA[i] + dir * head);
            }
        }

        // ------------------------------------------------------------------ material & texture helpers

        public static Material MakeParticleMat(string name, Texture tex, bool additive, Color tint, bool doubleSided = false)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!sh) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", doubleSided ? 0f : 2f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        static Texture2D NewTex(int w, int h, string name)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, true)
            { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        }

        public static Texture2D SoftCircle(int size, float power)
        {
            var t = NewTex(size, size, "SoftCircle");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), power);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        public static Texture2D SmokeTex(int size, int seed)
        {
            var t = NewTex(size, size, "Smoke");
            var px = new Color32[size * size];
            float o = seed * 13.37f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    float dx = u * 2f - 1f, dy = v * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = Mathf.PerlinNoise(u * 4f + o, v * 4f + o) * 0.65f + Mathf.PerlinNoise(u * 9f + o, v * 9f - o) * 0.35f;
                    float a = Mathf.Clamp01(1f - r);
                    a = a * a * Mathf.Clamp01(n * 1.6f - 0.2f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        public static Texture2D TracerTex()
        {
            const int w = 64, h = 16;
            var t = NewTex(w, h, "Tracer");
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w, v = (y + 0.5f) / h * 2f - 1f;
                    float a = Mathf.Pow(u, 1.6f) * Mathf.Clamp01(1f - v * v);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        public static Texture2D BulletHoleTex(int size, int seed)
        {
            var t = NewTex(size, size, "BulletHole");
            var px = new Color32[size * size];
            float o = seed * 7.1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float n = Mathf.PerlinNoise(Mathf.Cos(ang) * 2.5f + o, Mathf.Sin(ang) * 2.5f + r * 5f);
                    Color c; float a;
                    if (r < 0.16f) { c = new Color(0.03f, 0.03f, 0.03f); a = 1f; }
                    else if (r < 0.28f) { c = new Color(0.1f, 0.095f, 0.09f); a = 0.95f; }
                    else
                    {
                        float edge = 0.45f + n * 0.5f;
                        a = Mathf.Clamp01((edge - r) / 0.25f) * 0.75f;
                        c = new Color(0.2f, 0.19f, 0.18f);
                    }
                    px[y * size + x] = new Color(c.r, c.g, c.b, a);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }

        public static Texture2D MuzzleStarTex(int size)
        {
            var t = NewTex(size, size, "MuzzleStar");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float spikes = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f)), 10f) * 0.75f + Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 3f + 0.52f)), 18f) * 0.4f;
                    float reach = 0.22f + 0.78f * spikes;
                    float body = Mathf.Pow(Mathf.Clamp01(1f - r / reach), 1.6f);
                    float core = Mathf.Clamp01(1f - r * 3.5f);
                    Color c = Color.Lerp(new Color(1f, 0.5f, 0.12f), new Color(1f, 0.95f, 0.8f), Mathf.Clamp01(core * 1.5f + body * 0.3f));
                    float a = Mathf.Clamp01(Mathf.Max(body, core));
                    px[y * size + x] = new Color(c.r, c.g, c.b, a);
                }
            t.SetPixels32(px); t.Apply();
            return t;
        }
    }
}
