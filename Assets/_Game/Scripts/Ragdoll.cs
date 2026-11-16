using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// A physics ragdoll instead of a death animation. When a character dies (<see cref="CharacterAnimator.dead"/> rises
    /// above zero: bots, other players' proxies, training dummies — not the local player's own first-person body, whose
    /// death is the camera's) its eleven main bones (pelvis, chest, head, upper / lower arms and legs) get rigidbodies,
    /// colliders and character joints, and the body falls from the pose it died in: it keeps its running speed, takes
    /// the killing hit's push (the last flinch) and is thrown by a blast. Back alive (respawn, a medic) the physics is
    /// removed, the bones go back to their places and the animation takes over again.
    ///
    /// Bones: the imported model's humanoid bones when <see cref="SkinnedBody"/> animates it (the procedural rig follows
    /// them, so heads, nameplates and the held gun follow the body), otherwise the procedural <see cref="HumanoidRig"/>
    /// (an imported model in retarget mode copies it). The colliders sit on child objects on the Ignore Raycast layer:
    /// corpses do not stop bullets or line-of-sight checks, HumanoidRig.SetCollidersEnabled leaves them alone and the
    /// local player's CharacterController walks through them.
    /// </summary>
    [DefaultExecutionOrder(280)] // after CharacterAnimator and SkinnedBody (260) have posed this frame
    public class Ragdoll : MonoBehaviour
    {
        /// <summary>Layer of the ragdoll colliders (Ignore Raycast).</summary>
        public const int Layer = 2;

        /// <summary>Total mass of a body (kg), split over the parts.</summary>
        const float Mass = 70f;

        enum P { Hips, Chest, Head, LUpperArm, LForearm, RUpperArm, RForearm, LThigh, LShin, RThigh, RShin }

        class Part
        {
            public Transform bone;
            public Rigidbody rb;
            public GameObject col;
            public CharacterJoint joint;
            public Vector3 restPos;
            public Quaternion restRot;
        }

        struct Blast { public Vector3 pos; public float radius, push, time; }

        static readonly List<Blast> blasts = new List<Blast>();
        static readonly List<Ragdoll> live = new List<Ragdoll>();

        CharacterAnimator ca;
        readonly Part[] parts = new Part[11];
        Animator modelAnimator;
        bool animatorWasOn, active, checkedOwner, allowed;
        Vector3 lastRoot, rootVel;

        /// <summary>The ragdoll is simulating.</summary>
        public bool Active => active;
        /// <summary>Dead and ragdolled: the animators leave the bones to the physics.</summary>
        public bool Holds => active && ca && ca.dead > 0.001f;
        /// <summary>The simulated pelvis (null when inactive).</summary>
        public Transform Hips => active && parts[(int)P.Hips] != null ? parts[(int)P.Hips].bone : null;

        public static void Ensure(CharacterAnimator a)
        {
            if (a && !a.GetComponent<Ragdoll>()) a.gameObject.AddComponent<Ragdoll>();
        }

        /// <summary>
        /// An explosion: ragdolls already lying in it are thrown, and a body that starts its ragdoll in the next moments
        /// (killed by it) takes the push too. Explosions.Detonate / Visual call this on every machine.
        /// </summary>
        public static void NoteBlast(Vector3 pos, float radius, float push)
        {
            float now = Time.time;
            blasts.RemoveAll(b => now - b.time > 0.5f);
            var bl = new Blast { pos = pos, radius = Mathf.Max(1f, radius), push = push, time = now };
            blasts.Add(bl);
            foreach (var r in live) if (r && r.active) r.ApplyBlast(bl);
        }

        void Awake()
        {
            ca = GetComponent<CharacterAnimator>();
            lastRoot = transform.position;
        }

        void OnDisable()
        {
            if (active) Deactivate();
        }

        /// <summary>Not for the local player's own body (first person: the death is the camera's).</summary>
        bool Allowed
        {
            get
            {
                if (!checkedOwner)
                {
                    checkedOwner = true;
                    allowed = !GetComponentInParent<PlayerHealth>();
                }
                return allowed && ca && ca.rig;
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (!active && dt > 0f)
            {
                // the body's own speed, carried into the fall (a teleport — respawn — is not a speed)
                Vector3 v = (transform.position - lastRoot) / dt;
                rootVel = v.sqrMagnitude > 15f * 15f ? Vector3.zero : Vector3.Lerp(rootVel, v, 0.5f);
            }
            lastRoot = transform.position;

            bool dead = ca && ca.dead > 0.001f && ca.executeVictim < 0f;
            if (dead && !active && Allowed) Activate();
            else if (!dead && active) Deactivate();
        }

        // ------------------------------------------------------------------ start

        void Activate()
        {
            // which skeleton: the animated imported model, else the procedural rig
            Transform[] b, ends;
            var sb = ca.rig.GetComponentInChildren<SkinnedBody>();
            modelAnimator = sb && sb.Animated ? sb.ModelAnimator : null;
            if (modelAnimator && modelAnimator.isHuman)
            {
                var an = modelAnimator;
                Transform H(HumanBodyBones x) => an.GetBoneTransform(x);
                b = new[]
                {
                    H(HumanBodyBones.Hips), H(HumanBodyBones.Chest) ? H(HumanBodyBones.Chest) : H(HumanBodyBones.Spine), H(HumanBodyBones.Head),
                    H(HumanBodyBones.LeftUpperArm), H(HumanBodyBones.LeftLowerArm), H(HumanBodyBones.RightUpperArm), H(HumanBodyBones.RightLowerArm),
                    H(HumanBodyBones.LeftUpperLeg), H(HumanBodyBones.LeftLowerLeg), H(HumanBodyBones.RightUpperLeg), H(HumanBodyBones.RightLowerLeg),
                };
                ends = new[] { H(HumanBodyBones.LeftHand), H(HumanBodyBones.RightHand), H(HumanBodyBones.LeftFoot), H(HumanBodyBones.RightFoot) };
            }
            else
            {
                modelAnimator = null;
                var r = ca.rig;
                b = new[] { r.hips, r.chest, r.head, r.lUpperArm, r.lForearm, r.rUpperArm, r.rForearm, r.lThigh, r.lShin, r.rThigh, r.rShin };
                ends = new[] { r.lHand, r.rHand, r.lFoot, r.rFoot };
            }
            foreach (var t in b) if (!t) return;     // an odd skeleton: keep the old death animation
            foreach (var t in ends) if (!t) return;

            // the body's frame (from the procedural rig: the character's facing)
            Transform frame = ca.rig.transform;
            Vector3 right = frame.right, up = frame.up, fwd = frame.forward;
            float scale = Mathf.Max(0.5f, Vector3.Distance(b[(int)P.Hips].position, b[(int)P.Head].position) / 0.6f);

            if (modelAnimator) { animatorWasOn = modelAnimator.enabled; modelAnimator.enabled = false; }

            for (int i = 0; i < parts.Length; i++)
                parts[i] = new Part { bone = b[i], restPos = b[i].localPosition, restRot = b[i].localRotation };

            // ---- colliders (child objects in the body's frame), masses
            Vector3 hipsP = b[(int)P.Hips].position, chestP = b[(int)P.Chest].position, headP = b[(int)P.Head].position;
            float hipWidth = Vector3.Distance(b[(int)P.LThigh].position, b[(int)P.RThigh].position);
            float shoulders = Vector3.Distance(b[(int)P.LUpperArm].position, b[(int)P.RUpperArm].position);
            Vector3 pelvisTop = Vector3.Lerp(hipsP, chestP, 0.55f);
            Box(P.Hips, (hipsP + pelvisTop) * 0.5f - up * 0.02f * scale, frame.rotation,
                new Vector3(Mathf.Max(hipWidth * 1.9f, 0.26f * scale), Vector3.Distance(hipsP, pelvisTop) + 0.14f * scale, 0.2f * scale));
            Vector3 neckBase = Vector3.Lerp(chestP, headP, 0.85f);
            Box(P.Chest, Vector3.Lerp(pelvisTop, neckBase, 0.5f), frame.rotation,
                new Vector3(Mathf.Max(shoulders * 1.05f, 0.3f * scale), Vector3.Distance(pelvisTop, neckBase), 0.22f * scale));
            Sphere(P.Head, headP + up * 0.09f * scale, 0.11f * scale);
            Capsule(P.LUpperArm, b[(int)P.LForearm].position, 0.2f, 0f);
            Capsule(P.LForearm, ends[0].position, 0.17f, 0.12f);
            Capsule(P.RUpperArm, b[(int)P.RForearm].position, 0.2f, 0f);
            Capsule(P.RForearm, ends[1].position, 0.17f, 0.12f);
            Capsule(P.LThigh, b[(int)P.LShin].position, 0.18f, 0f);
            Capsule(P.LShin, ends[2].position, 0.15f, 0.1f);
            Capsule(P.RThigh, b[(int)P.RShin].position, 0.18f, 0f);
            Capsule(P.RShin, ends[3].position, 0.15f, 0.1f);

            float[] share = { 0.2f, 0.26f, 0.07f, 0.035f, 0.025f, 0.035f, 0.025f, 0.11f, 0.065f, 0.11f, 0.065f };
            for (int i = 0; i < parts.Length; i++)
            {
                var rb = parts[i].bone.gameObject.AddComponent<Rigidbody>();
                rb.mass = Mass * share[i];
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.85f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.maxDepenetrationVelocity = 2f;
                rb.solverIterations = 12;
                rb.solverVelocityIterations = 4;
                parts[i].rb = rb;
            }

            // ---- joints (limits around the pose it died in, Unity's ragdoll wizard's axes)
            Joint(P.Chest, P.Hips, right, fwd, -25f, 25f, 15f);
            Joint(P.Head, P.Chest, right, fwd, -40f, 30f, 30f);
            Joint(P.LUpperArm, P.Chest, up, fwd, -80f, 50f, 60f);
            Joint(P.LForearm, P.LUpperArm, fwd, up, -100f, 30f, 10f);
            Joint(P.RUpperArm, P.Chest, up, fwd, -80f, 50f, 60f);
            Joint(P.RForearm, P.RUpperArm, fwd, up, -100f, 30f, 10f);
            Joint(P.LThigh, P.Hips, right, fwd, -30f, 70f, 30f);
            Joint(P.LShin, P.LThigh, right, fwd, -90f, 10f, 5f);
            Joint(P.RThigh, P.Hips, right, fwd, -30f, 70f, 30f);
            Joint(P.RShin, P.RThigh, right, fwd, -90f, 10f, 5f);

            // the local player walks through corpses
            var ph = PlayerHealth.Local;
            var cc = ph ? ph.GetComponent<CharacterController>() : null;
            foreach (var p in parts)
            {
                var col = p.col ? p.col.GetComponent<Collider>() : null;
                if (col && cc) Physics.IgnoreCollision(col, cc, true);
            }

            // ---- the fall: running speed, the killing hit, recent blasts
            Vector3 carry = Vector3.ClampMagnitude(rootVel, 9f);
            foreach (var p in parts) p.rb.linearVelocity = carry;
            float since = Time.time - ca.LastHitTime;
            if (since < 0.6f && ca.LastHitDir.sqrMagnitude > 0.01f)
            {
                Vector3 dir = ca.LastHitDir.normalized;
                float speed = Mathf.Clamp(1.2f + ca.LastHitStrength * 1.7f, 1.2f, 4.6f);
                Push(P.Chest, dir * speed);
                Push(P.Head, dir * speed * 0.8f);
                Push(P.Hips, dir * speed * 0.5f);
            }
            else
            {
                // no known hit (another player's kill seen from the side): tip it over the way the animation fell
                Vector3 dir = fwd * (ca.deadDir > 0f ? 1f : -1f);
                Push(P.Chest, dir * 1.4f);
                Push(P.Head, dir * 1.2f);
            }
            float now = Time.time;
            foreach (var bl in blasts) if (now - bl.time < 0.4f) ApplyBlast(bl);

            active = true;
            live.Add(this);
        }

        void Push(P p, Vector3 dv) => parts[(int)p].rb.AddForce(dv, ForceMode.VelocityChange);

        void ApplyBlast(Blast bl)
        {
            var hips = parts[(int)P.Hips];
            if (hips == null || !hips.rb) return;
            if ((hips.bone.position - bl.pos).sqrMagnitude > (bl.radius + 1.5f) * (bl.radius + 1.5f)) return;
            foreach (var p in parts)
                if (p != null && p.rb) p.rb.AddExplosionForce(bl.push * 0.75f, bl.pos, bl.radius * 1.3f, 0.5f, ForceMode.VelocityChange);
        }

        // ------------------------------------------------------------------ pieces

        GameObject ColliderObject(P p, Vector3 pos, Quaternion rot)
        {
            var go = new GameObject("RagdollCollider");
            go.layer = Layer;
            go.transform.SetParent(parts[(int)p].bone, false);
            go.transform.SetPositionAndRotation(pos, rot);
            parts[(int)p].col = go;
            return go;
        }

        void Box(P p, Vector3 center, Quaternion rot, Vector3 size)
        {
            var go = ColliderObject(p, center, rot);
            var c = go.AddComponent<BoxCollider>();
            // the collider object may sit under a scaled bone: the size is meant in world metres
            Vector3 ls = go.transform.lossyScale;
            c.size = new Vector3(size.x / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), size.y / Mathf.Max(1e-4f, Mathf.Abs(ls.y)), size.z / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));
        }

        void Sphere(P p, Vector3 center, float radius)
        {
            var go = ColliderObject(p, center, parts[(int)p].bone.rotation);
            var c = go.AddComponent<SphereCollider>();
            float ls = Mathf.Max(1e-4f, Mathf.Abs(go.transform.lossyScale.x));
            c.radius = radius / ls;
        }

        /// <summary>A capsule from the bone to <paramref name="end"/> (+ extend × length past it), radius = share of the length.</summary>
        void Capsule(P p, Vector3 end, float radiusShare, float extend)
        {
            Vector3 a = parts[(int)p].bone.position;
            Vector3 d = end - a;
            float len = Mathf.Max(0.05f, d.magnitude) * (1f + extend);
            Vector3 dir = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down;
            var go = ColliderObject(p, a + dir * (len * 0.5f), Quaternion.FromToRotation(Vector3.up, dir));
            var c = go.AddComponent<CapsuleCollider>();
            float ls = Mathf.Max(1e-4f, Mathf.Abs(go.transform.lossyScale.y));
            c.direction = 1;
            c.height = len / ls;
            c.radius = Mathf.Max(0.035f, len / (1f + extend) * radiusShare) / ls;
        }

        void Joint(P child, P parent, Vector3 twistAxis, Vector3 swingAxis, float lowTwist, float highTwist, float swing)
        {
            var part = parts[(int)child];
            var j = part.bone.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parts[(int)parent].rb;
            j.autoConfigureConnectedAnchor = true;
            j.anchor = Vector3.zero;
            j.axis = part.bone.InverseTransformDirection(twistAxis).normalized;
            j.swingAxis = part.bone.InverseTransformDirection(swingAxis).normalized;
            j.lowTwistLimit = new SoftJointLimit { limit = lowTwist };
            j.highTwistLimit = new SoftJointLimit { limit = highTwist };
            j.swing1Limit = new SoftJointLimit { limit = swing };
            j.swing2Limit = new SoftJointLimit { limit = swing * 0.5f };
            j.enablePreprocessing = false;
            j.enableProjection = true;
            j.projectionDistance = 0.05f;
            j.projectionAngle = 15f;
            part.joint = j;
        }

        // ------------------------------------------------------------------ back alive

        void Deactivate()
        {
            active = false;
            live.Remove(this);
            foreach (var p in parts)
            {
                if (p == null) continue;
                if (p.joint) Destroy(p.joint);
                if (p.rb) { p.rb.isKinematic = true; p.rb.detectCollisions = false; Destroy(p.rb); }
                if (p.col) Destroy(p.col);
            }
            // the bones back where the animation expects them (the joints may have stretched them a little)
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var p = parts[i];
                if (p == null || !p.bone) continue;
                p.bone.localPosition = p.restPos;
                p.bone.localRotation = p.restRot;
                parts[i] = null;
            }
            if (modelAnimator) modelAnimator.enabled = animatorWasOn;
            modelAnimator = null;
        }

        void OnDestroy() => live.Remove(this);
    }
}
