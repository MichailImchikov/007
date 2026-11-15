using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// The comic villain look a character takes on as its kill streak (and its speed) grows, seen by everyone else:
    /// the body itself turns comic (a thick ink outline, halftone shading, a hard rim light: ComicAvatarFeature draws
    /// them with FPS/ComicAvatar), a cel-shaded flame aura licks up off its silhouette and throws sparks, the eyes glow
    /// red and leave light trails, an evil grin spreads over the mouth, and sound words pop around it: PAW! BANG! over
    /// the gun when it fires, SVOOSH! under a slide, WHOOSH! / ZOOM! behind a runner, BOING! on a jump. Every client
    /// works it out from the same kill events and the body's own motion. The local player's own body (first person)
    /// shows none of it: the player gets the screen effects (ComicFX) instead.
    /// </summary>
    [DefaultExecutionOrder(310)] // after the body is posed (SkinnedBody 260)
    public class ComicAvatar : MonoBehaviour
    {
        [Tooltip("Debug / showcase: streak used when there is no combatant (or to force a level)")]
        public int streakOverride = -1;

        // the face, measured on the Military heads in the bind pose: model axes (x right, y up, z forward), metres at
        // model scale, from the head bone. x of the eyes is half their spacing.
        static readonly Vector3 EyeOffset = new Vector3(0.035f, 0.106f, 0.124f);
        static readonly Vector3 MouthOffset = new Vector3(0f, 0.03f, 0.153f);

        SkinnedBody body;
        Animator an, boundAn;
        Combatant combatant;
        CharacterAnimator driver;
        Transform head, hand;
        Vector3 headRight = Vector3.right, headUp = Vector3.up, headFwd = Vector3.forward;   // model axes in the head bone's frame
        Transform eyeL, eyeR, haloL, haloR, grin, trailL, trailR;
        TrailRenderer trailRL, trailRR;
        Material eyeMat, haloMat, grinMat, trailMat, sparkMat, comicMat;
        ParticleSystem sparks;
        readonly List<Renderer> drawn = new List<Renderer>();
        readonly List<SkinnedMeshRenderer> scan = new List<SkinnedMeshRenderer>();
        Transform[] segA, segB;
        float[] segR;
        float level, momentum, lastShotSeen, nextShotWord, nextRunWord, sparkDebt, nextScan;
        bool wasSliding, wasGrounded = true, built, trailsOn;

        static readonly List<ComicAvatar> All = new List<ComicAvatar>();
        static Mesh quad, grinMesh;
        static Shader sprite;
        static readonly string[] ShotWords = { "PAW!", "PEW!", "BANG!", "PAW PAW!", "BLAM!", "RA-TA-TA!" };
        static readonly string[] RunWords = { "WHOOSH!", "ZOOM!", "VROOM!", "ZIP!" };
        static readonly int LevelId = Shader.PropertyToID("_Level"), AuraId = Shader.PropertyToID("_AuraColor"),
                            CoreId = Shader.PropertyToID("_AuraCore"), BaseId = Shader.PropertyToID("_Base"),
                            SeedId = Shader.PropertyToID("_Seed"), ColorId = Shader.PropertyToID("_Color");

        /// <summary>How far the look has grown, 0..1.</summary>
        public float Level => level;

        /// <summary>Whether any character shows the comic body look this frame (the renderer feature skips its pass if not).</summary>
        public static bool AnyDrawn
        {
            get
            {
                for (int i = 0; i < All.Count; i++) if (All[i] && All[i].level > 0.02f && All[i].comicMat) return true;
                return false;
            }
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            body = GetComponent<SkinnedBody>();
            an = body ? body.ModelAnimator : GetComponentInChildren<Animator>();
            combatant = GetComponentInParent<Combatant>();
        }

        // ------------------------------------------------------------------ the body look (ComicAvatarFeature)

        /// <summary>Draws every streaking character's ink, tone, rim and aura passes over its own renderers.</summary>
        internal static void DrawAll(RasterCommandBuffer cmd)
        {
            for (int pass = 0; pass < 4; pass++)
                for (int i = 0; i < All.Count; i++)
                {
                    var a = All[i];
                    if (!a || a.level <= 0.02f || !a.comicMat) continue;
                    var list = a.drawn;
                    for (int j = 0; j < list.Count; j++)
                    {
                        var r = list[j];
                        if (!r || !r.enabled || !r.isVisible || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                        var smr = r as SkinnedMeshRenderer;
                        int subs = smr && smr.sharedMesh ? smr.sharedMesh.subMeshCount : 1;
                        for (int s = 0; s < subs; s++) cmd.DrawRenderer(r, a.comicMat, s, pass);
                    }
                }
        }

        /// <summary>Whether any character body can go into the character mask this frame (ComicAvatarFeature).</summary>
        public static bool AnyBody
        {
            get
            {
                for (int i = 0; i < All.Count; i++) if (All[i] && All[i].comicMat && All[i].drawn.Count > 0) return true;
                return false;
            }
        }

        /// <summary>
        /// The character mask for FPS/ToonPost (pass 4, streak or not): every visible body's eye depth, so the post can
        /// keep characters readable — silhouette ink only, no crease lines over the gear, lighter dark tones. The local
        /// player's own first-person body is left out.
        /// </summary>
        internal static void DrawMask(RasterCommandBuffer cmd)
        {
            for (int i = 0; i < All.Count; i++)
            {
                var a = All[i];
                if (!a || !a.comicMat || (a.body && a.body.FirstPerson)) continue;
                var list = a.drawn;
                for (int j = 0; j < list.Count; j++)
                {
                    var r = list[j];
                    if (!r || !r.enabled || !r.isVisible || r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                    var smr = r as SkinnedMeshRenderer;
                    int subs = smr && smr.sharedMesh ? smr.sharedMesh.subMeshCount : 1;
                    for (int s = 0; s < subs; s++) cmd.DrawRenderer(r, a.comicMat, s, 4);
                }
            }
        }

        // ------------------------------------------------------------------ setup

        void Build()
        {
            if (built) return;
            built = true;
            if (!sprite) sprite = Shader.Find("FPS/ComicSprite");
            if (!quad) quad = MakeQuad();
            if (!grinMesh) grinMesh = MakeGrinMesh(0.088f, 0.044f, 0.045f, 12);

            eyeMat = SpriteMat("ComicEyes", ComicArt.Eye, true);
            haloMat = SpriteMat("ComicEyeHalo", ComicArt.Glow, true);
            grinMat = SpriteMat("ComicGrin", ComicArt.Grin, false);
            trailMat = SpriteMat("ComicEyeTrail", ComicArt.Streak, true);
            sparkMat = SpriteMat("ComicSparks", ComicArt.Sparkle, true);
            eyeL = Part("ComicEyeL", quad, eyeMat, false);
            eyeR = Part("ComicEyeR", quad, eyeMat, true);
            haloL = Part("ComicHaloL", quad, haloMat, false);
            haloR = Part("ComicHaloR", quad, haloMat, false);
            grin = Part("ComicGrin", grinMesh, grinMat, false);
            trailL = Trail("ComicTrailL", out trailRL);
            trailR = Trail("ComicTrailR", out trailRR);

            var template = Resources.Load<Material>("ComicAvatar");
            if (template)
            {
                comicMat = new Material(template) { name = "ComicAvatar (instance)" };
                comicMat.SetFloat(SeedId, Random.Range(0f, 100f));
            }
            else Debug.LogWarning("ComicAvatar: Resources/ComicAvatar material missing: no ink, tone or aura on the body");

            // sparks thrown off the aura: emitted by hand along the body's bones (EmitSpark)
            var go = new GameObject("ComicSparks");
            go.transform.SetParent(transform, false);
            sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = sparks.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            main.gravityModifier = -0.04f;
            var emission = sparks.emission;
            emission.enabled = false;
            var shape = sparks.shape;
            shape.enabled = false;
            var col = sparks.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var sz = sparks.sizeOverLifetime;
            sz.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.2f));
            sz.size = new ParticleSystem.MinMaxCurve(1f, curve);
            var rot = sparks.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = sparkMat;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            sparks.Play();
        }

        Material SpriteMat(string name, Texture tex, bool additive)
        {
            var m = new Material(sprite) { name = name };
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            return m;
        }

        Transform Part(string name, Mesh mesh, Material m, bool mirror)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (mirror) go.transform.localScale = new Vector3(-1f, 1f, 1f);
            go.SetActive(false);
            return go.transform;
        }

        Transform Trail(string name, out TrailRenderer tr)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            tr = go.AddComponent<TrailRenderer>();
            tr.sharedMaterial = trailMat;
            tr.time = 0.22f;
            tr.minVertexDistance = 0.015f;
            tr.widthMultiplier = 0.022f;
            tr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.45f, 0.25f), 0f), new GradientColorKey(new Color(1f, 0.08f, 0.04f), 0.35f), new GradientColorKey(new Color(0.6f, 0.02f, 0.05f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
            tr.colorGradient = g;
            tr.textureMode = LineTextureMode.Stretch;
            tr.alignment = LineAlignment.View;
            tr.numCapVertices = 2;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            tr.receiveShadows = false;
            tr.emitting = false;
            return go.transform;
        }

        /// <summary>Finds the head, the gun hand, the face axes and the body's bones and renderers (again if the model is rebuilt).</summary>
        bool BindRig()
        {
            boundAn = an;
            head = null;
            if (!an || !an.isHuman) return false;
            head = an.GetBoneTransform(HumanBodyBones.Head);
            if (!head) return false;
            hand = an.GetBoneTransform(HumanBodyBones.RightHand);
            if (!CalibrateFace())
            {
                Quaternion inv = Quaternion.Inverse(head.rotation);
                headRight = inv * an.transform.right;
                headUp = inv * an.transform.up;
                headFwd = inv * an.transform.forward;
            }

            var a = new List<Transform>(); var b = new List<Transform>(); var r = new List<float>();
            void Seg(HumanBodyBones from, HumanBodyBones to, HumanBodyBones alt, float radius)
            {
                var ta = an.GetBoneTransform(from);
                var tb = an.GetBoneTransform(to);
                if (!tb) tb = an.GetBoneTransform(alt);
                if (ta && tb) { a.Add(ta); b.Add(tb); r.Add(radius); }
            }
            Seg(HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, 0.15f);
            Seg(HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, 0.16f);
            Seg(HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head, 0.16f);
            Seg(HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.Head, 0.08f);
            Seg(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.06f);
            Seg(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.06f);
            Seg(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftHand, 0.05f);
            Seg(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightHand, 0.05f);
            Seg(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.08f);
            Seg(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.08f);
            Seg(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftFoot, 0.06f);
            Seg(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightFoot, 0.06f);
            segA = a.ToArray(); segB = b.ToArray(); segR = r.ToArray();
            ScanRenderers();
            return true;
        }

        /// <summary>
        /// The model's right / up / forward in the head bone's frame, from the bind pose (where the head is upright and
        /// looks ahead), so the face stays put whatever the head is doing when the character spawns.
        /// </summary>
        bool CalibrateFace()
        {
            an.GetComponentsInChildren(true, scan);
            foreach (var smr in scan)
            {
                var mesh = smr.sharedMesh;
                var bones = smr.bones;
                if (!mesh || bones == null) continue;
                int i = System.Array.IndexOf(bones, head);
                var bind = mesh.bindposes;
                if (i < 0 || i >= bind.Length) continue;
                Matrix4x4 modelFromMesh = an.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                Matrix4x4 boneInModel = modelFromMesh * bind[i].inverse;
                Vector3 z = boneInModel.GetColumn(2), y = boneInModel.GetColumn(1);
                if (z.sqrMagnitude < 1e-8f || y.sqrMagnitude < 1e-8f) continue;
                Quaternion inv = Quaternion.Inverse(Quaternion.LookRotation(z, y));
                headRight = inv * Vector3.right;
                headUp = inv * Vector3.up;
                headFwd = inv * Vector3.forward;
                return true;
            }
            return false;
        }

        /// <summary>The body's visible skinned renderers (gear can change at any time, so this runs every second).</summary>
        void ScanRenderers()
        {
            drawn.Clear();
            if (!an) return;
            an.GetComponentsInChildren(false, scan);
            foreach (var smr in scan) if (smr.enabled && smr.sharedMesh) drawn.Add(smr);
            nextScan = Time.time + 1f;
        }

        static Mesh MakeQuad()
        {
            var m = new Mesh { name = "ComicQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>The grin's card in metres, bent around the face (a cylinder of the given radius about the up axis).</summary>
        static Mesh MakeGrinMesh(float width, float height, float radius, int segments)
        {
            var m = new Mesh { name = "ComicGrin" };
            var v = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[v.Length];
            var tri = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float u = (float)i / segments;
                float ang = (u - 0.5f) * width / radius;
                float x = Mathf.Sin(ang) * radius, z = (Mathf.Cos(ang) - 1f) * radius;
                v[i * 2] = new Vector3(x, -height * 0.5f, z);
                v[i * 2 + 1] = new Vector3(x, height * 0.5f, z);
                uv[i * 2] = new Vector2(u, 0f);
                uv[i * 2 + 1] = new Vector2(u, 1f);
                if (i < segments)
                {
                    int k = i * 6, b = i * 2;
                    tri[k] = b; tri[k + 1] = b + 1; tri[k + 2] = b + 2;
                    tri[k + 3] = b + 1; tri[k + 4] = b + 3; tri[k + 5] = b + 2;
                }
            }
            m.vertices = v;
            m.uv = uv;
            m.triangles = tri;
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            if (!an) { Start(); if (!an) return; }
            if (!built) Build();
            if (an != boundAn || !head) { if (!BindRig()) return; }
            if (Time.time >= nextScan) ScanRenderers();
            bool own = body && body.FirstPerson;
            if (!driver && body) driver = body.Driver;
            float dt = Time.deltaTime;
            float t = Time.time;

            int streak = streakOverride >= 0 ? streakOverride : combatant ? combatant.streak : 0;
            float speed = driver ? new Vector2(driver.localVelocity.x, driver.localVelocity.z).magnitude : 0f;
            bool sliding = driver && driver.sliding;
            bool grounded = !driver || driver.grounded;
            float speedN = Mathf.InverseLerp(3.2f, 8.5f, speed);
            if (sliding) speedN = Mathf.Max(speedN, 0.9f);
            momentum = Mathf.MoveTowards(momentum, speedN, dt * (speedN > momentum ? 1.2f : 0.3f));
            bool dead = combatant && !combatant.alive;
            float target = dead || own ? 0f : Mathf.Clamp01(KillStreak.Level(streak) * 0.85f + momentum * 0.3f * Mathf.Clamp01(streak));
            level = Mathf.Lerp(level, target, 1f - Mathf.Exp(-3f * dt));

            float s = an.transform.lossyScale.y;
            Vector3 R = head.rotation * headRight, U = head.rotation * headUp, F = head.rotation * headFwd;
            Quaternion face = Quaternion.LookRotation(F, U);
            Vector3 eyeMid = head.position + (U * EyeOffset.y + F * EyeOffset.z) * s;
            Vector3 eyePosL = eyeMid - R * (EyeOffset.x * s), eyePosR = eyeMid + R * (EyeOffset.x * s);
            float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(t * 9f, 0.3f);

            // ---- the body: ink, halftone, rim and the flame aura (drawn by ComicAvatarFeature), hotter with the streak:
            // gold → orange → crimson → violet
            Color auraHue = AuraHue(level);
            if (comicMat)
            {
                comicMat.SetFloat(LevelId, level);
                comicMat.SetColor(AuraId, auraHue * (1f + 0.6f * level));
                comicMat.SetColor(CoreId, Color.Lerp(auraHue, Color.white, 0.3f) * (1.2f + 0.6f * level));
                comicMat.SetVector(BaseId, an.transform.position);
            }
            Vector3 parentScale = transform.lossyScale;   // the parts hang under this object: undo its scale

            // ---- eyes: a slanted glowing slit on each eye, a halo that shows from any side, light trails behind
            bool showEyes = level > 0.12f;
            eyeL.gameObject.SetActive(showEyes);
            eyeR.gameObject.SetActive(showEyes);
            haloL.gameObject.SetActive(showEyes);
            haloR.gameObject.SetActive(showEyes);
            if (showEyes)
            {
                float fade = Mathf.Clamp01((level - 0.12f) * 5f);
                float sz = (0.026f + 0.012f * level) * s;
                eyeL.SetPositionAndRotation(eyePosL, face);
                eyeR.SetPositionAndRotation(eyePosR, face);
                eyeL.localScale = Unscale(new Vector3(sz * 1.7f, sz, 1f), parentScale);
                eyeR.localScale = Unscale(new Vector3(-sz * 1.7f, sz, 1f), parentScale);
                Color c = Color.Lerp(new Color(1f, 0.85f, 0.45f), Color.white, level) * ((1.4f + 2.6f * level) * flicker);
                c.a = fade;
                eyeMat.SetColor(ColorId, c);

                var cam = Camera.main;
                Vector3 camPos = cam ? cam.transform.position : eyeMid + F;
                Vector3 hs = Unscale(Vector3.one * ((0.045f + 0.03f * level) * s), parentScale);
                PlaceHalo(haloL, eyePosL, camPos, hs, s);
                PlaceHalo(haloR, eyePosR, camPos, hs, s);
                Color h = new Color(1f, 0.1f, 0.04f) * ((0.4f + 1f * level) * flicker);
                h.a = fade * 0.8f;
                haloMat.SetColor(ColorId, h);
            }
            bool trails = showEyes && level > 0.3f;
            trailL.position = eyePosL + F * (0.01f * s);
            trailR.position = eyePosR + F * (0.01f * s);
            if (trails != trailsOn)
            {
                trailsOn = trails;
                if (trails) { trailRL.Clear(); trailRR.Clear(); }
                trailRL.emitting = trailRR.emitting = trails;
            }
            if (trails)
            {
                trailRL.time = trailRR.time = Mathf.Lerp(0.12f, 0.3f, level);
                trailRL.widthMultiplier = trailRR.widthMultiplier = 0.02f * s;
                trailMat.SetColor(ColorId, Color.white * (1.5f + 2f * level));
            }

            // ---- the grin
            float grinA = Mathf.Clamp01((level - 0.3f) / 0.25f);
            grin.gameObject.SetActive(grinA > 0.01f);
            if (grinA > 0.01f)
            {
                grin.SetPositionAndRotation(head.position + (U * MouthOffset.y + F * MouthOffset.z) * s, face);
                grin.localScale = Unscale(Vector3.one * (s * (0.92f + 0.16f * level)), parentScale);
                grinMat.SetColor(ColorId, new Color(1f, 1f, 1f, grinA));
            }

            // ---- sparks off the aura
            float rate = level > 0.2f ? Mathf.Pow(level, 1.3f) * 55f : 0f;
            sparkDebt = Mathf.Min(sparkDebt + rate * dt, 6f);
            if (segA != null && segA.Length > 0)
                while (sparkDebt >= 1f) { sparkDebt -= 1f; EmitSpark(auraHue, s); }
            sparkMat.SetColor(ColorId, Color.white * (1.6f + 1.6f * level));

            // ---- sound words
            if (own || dead) { lastShotSeen = combatant ? combatant.lastShotTime : 0f; wasSliding = sliding; wasGrounded = grounded; return; }
            float words = Mathf.Clamp01((level - 0.18f) / 0.3f);
            Color hot = Color.Lerp(new Color(1f, 0.92f, 0.3f), new Color(1f, 0.35f, 0.2f), level);
            if (combatant && combatant.lastShotTime > lastShotSeen + 1e-4f)
            {
                lastShotSeen = combatant.lastShotTime;
                if (words > 0f && t >= nextShotWord)
                {
                    nextShotWord = t + Mathf.Lerp(0.45f, 0.14f, level);
                    Vector3 gun = (hand ? hand.position : head.position - U * 0.3f) + an.transform.forward * 0.45f + Vector3.up * 0.25f;
                    ComicWords.Spawn(gun + Random.insideUnitSphere * 0.12f, ShotWords[Random.Range(0, ShotWords.Length)], hot, 0.7f + 0.5f * level);
                }
            }
            if (sliding && !wasSliding && words > 0f)
                ComicWords.Spawn(an.transform.position + Vector3.up * 0.35f - an.transform.forward * 0.3f, "SVOOSH!", new Color(0.55f, 0.95f, 1f), 0.9f + 0.4f * level, -an.transform.forward * 1.2f + Vector3.up * 0.2f);
            if (grounded && !sliding && speed > 5.2f && words > 0f && t >= nextRunWord)
            {
                nextRunWord = t + Mathf.Lerp(1.1f, 0.5f, level);
                ComicWords.Spawn(an.transform.position + Vector3.up * 1.1f - an.transform.forward * 0.7f, RunWords[Random.Range(0, RunWords.Length)], new Color(1f, 1f, 1f), 0.7f + 0.4f * level, -an.transform.forward * 1.5f + Vector3.up * 0.3f);
            }
            if (wasGrounded && !grounded && driver && driver.verticalSpeed > 1f && words > 0f)
                ComicWords.Spawn(an.transform.position + Vector3.up * 0.2f, "BOING!", new Color(0.7f, 1f, 0.45f), 0.8f + 0.3f * level);
            wasSliding = sliding;
            wasGrounded = grounded;
        }

        /// <summary>The aura's hue for a level: gold, orange, crimson, then violet at the top of a streak.</summary>
        static Color AuraHue(float level)
        {
            Color a0 = new Color(1f, 0.78f, 0.22f), a1 = new Color(1f, 0.38f, 0.07f), a2 = new Color(0.95f, 0.08f, 0.12f), a3 = new Color(0.62f, 0.12f, 1f);
            if (level < 0.45f) return Color.Lerp(a0, a1, level / 0.45f);
            if (level < 0.7f) return Color.Lerp(a1, a2, (level - 0.45f) / 0.25f);
            return Color.Lerp(a2, a3, (level - 0.7f) / 0.3f);
        }

        /// <summary>A camera-facing glow just in front of an eye (it still hides behind the head seen from the back).</summary>
        static void PlaceHalo(Transform halo, Vector3 eye, Vector3 camPos, Vector3 localScale, float s)
        {
            Vector3 toCam = camPos - eye;
            float d = toCam.magnitude;
            Vector3 dir = d > 1e-4f ? toCam / d : Vector3.forward;
            halo.SetPositionAndRotation(eye + dir * (0.03f * s), Quaternion.LookRotation(-dir));
            halo.localScale = new Vector3(localScale.x, localScale.y, 1f);
        }

        /// <summary>The local scale that gives a child of a parent with this lossy scale the given world size.</summary>
        static Vector3 Unscale(Vector3 world, Vector3 parent) =>
            new Vector3(world.x / Mathf.Max(parent.x, 1e-4f), world.y / Mathf.Max(parent.y, 1e-4f), world.z / Mathf.Max(parent.z, 1e-4f));

        void EmitSpark(Color hue, float s)
        {
            int k = Random.Range(0, segA.Length);
            if (!segA[k] || !segB[k]) return;
            Vector3 p = Vector3.Lerp(segA[k].position, segB[k].position, Random.value) + Random.insideUnitSphere * (segR[k] * s);
            var ep = new ParticleSystem.EmitParams
            {
                position = p,
                velocity = Vector3.up * Random.Range(0.4f, 1.1f) + Random.insideUnitSphere * 0.3f,
                startSize = Random.Range(0.045f, 0.11f) * s,
                startLifetime = Random.Range(0.35f, 0.75f),
                startColor = Color.Lerp(hue, Color.white, Random.Range(0f, 0.45f)),
                rotation = Random.Range(0f, 90f),
            };
            sparks.Emit(ep, 1);
        }

        void OnDestroy()
        {
            foreach (var m in new[] { eyeMat, haloMat, grinMat, trailMat, sparkMat, comicMat })
                if (m) Destroy(m);
        }
    }
}
