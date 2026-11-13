using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Wears an imported skinned (Humanoid) character on a procedural HumanoidRig.
    ///
    /// Animated mode (CharacterModelDB.tpController present): the model's own Animator plays the JU TPS 3 clips
    /// (locomotion / crouch / prone / air + upper-body weapon hold), fed every frame from CharacterAnimator's
    /// parameters — movement itself stays ours (motor / NavMeshAgent, no root motion). On top of it:
    ///  • procedural poses (death fall, executions, knife, throw, abilities) are blended in per bone group;
    ///  • aim pitch / lean / head look are added to the spine;
    ///  • the hands are IK'd onto the held gun's grips (HandGrips);
    ///  • the procedural rig follows the model, so every rig.head / rig.chest / weaponSocket user keeps working;
    ///  • hit boxes live on the model's real bones (head = critical).
    ///
    /// Without a controller it falls back to the old retarget: the model copies the procedural rig's pose.
    /// Retarget math: the procedural bones rest with identity rotations, the model in a T-pose; per mapped bone
    /// C = rotation from the model's rest bone direction to the rig's, modelBone = rigBoneDelta * C * modelRest.
    /// </summary>
    [DefaultExecutionOrder(260)]
    public class SkinnedBody : MonoBehaviour
    {
        enum Group { Core, Head, LArm, RArm, Legs }

        struct Map
        {
            public Transform model, rig;
            public Quaternion correction, rest; // rest = model bone rotation relative to the body root
            public Group group;
            public bool follow;                 // this model bone drives its rig bone (one per rig bone)
        }

        HumanoidRig rigRef;
        CharacterAnimator ca;
        Transform body;          // HumanoidRig transform (shared reference frame)
        Transform modelHips;
        readonly List<Map> maps = new List<Map>();
        Transform modelHead;
        Vector3 modelHeadScale = Vector3.one;
        public Renderer[] Renderers { get; private set; }
        public int ModelIndex { get; private set; }

        // animated mode
        Animator an;
        bool animated;
        Transform mSpine, mChestTop, mNeck, mHead;
        ArmIK lArm, rArm;
        readonly float[] procW = new float[5];
        float upperW = 1f, ikW;
        bool lastReload, lastThrow;
        Vector3 socketRest;
        public bool Animated => animated;
        public Animator ModelAnimator => an;

        static readonly int pForward = Animator.StringToHash("Forward"), pRight = Animator.StringToHash("Right"),
            pCrouch = Animator.StringToHash("Crouch"), pProne = Animator.StringToHash("Prone"), pPistol = Animator.StringToHash("Pistol"),
            pGrounded = Animator.StringToHash("Grounded"), pReload = Animator.StringToHash("Reload"), pThrow = Animator.StringToHash("Throw");

        /// <summary>Instantiates the model under the rig and binds it. Returns null if the model has no humanoid avatar.</summary>
        public static SkinnedBody Attach(HumanoidRig rig, int modelIndex, CharacterModelDB.Entry e)
        {
            var inst = Instantiate(e.prefab, rig.transform, false);
            inst.name = "SkinnedModel";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            var an = inst.GetComponentInChildren<Animator>();
            if (!an || !an.avatar || !an.avatar.isHuman || !an.GetBoneTransform(HumanBodyBones.Hips))
            {
                Debug.LogWarning($"[SkinnedBody] {e.prefab.name} has no humanoid avatar");
                if (Application.isPlaying) Destroy(inst); else DestroyImmediate(inst);
                return null;
            }
            // the pack's own colliders would block shots: our hit boxes are built on the bones below
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) lg.enabled = true;

            var sb = inst.AddComponent<SkinnedBody>();
            sb.ModelIndex = modelIndex;
            sb.Bind(rig, an);

            var db = CharacterModelDB.Instance;
            sb.animated = db && db.tpController && Application.isPlaying;
            if (sb.animated)
            {
                an.runtimeAnimatorController = db.tpController;
                an.applyRootMotion = false;
                an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                an.enabled = true;
                sb.an = an;
                sb.lArm = ArmIK.Make(an, true);
                sb.rArm = ArmIK.Make(an, false);
                sb.BuildHitboxes(rig);
            }
            else an.enabled = false;

            var rs = new List<Renderer>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (e.skin) { var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = e.skin; r.sharedMaterials = mats; }
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                rs.Add(r);
            }
            sb.Renderers = rs.ToArray();
            return sb;
        }

        Quaternion BodyRot(Transform t) => Quaternion.Inverse(body.rotation) * t.rotation;
        Vector3 BodyPos(Transform t) => body.InverseTransformPoint(t.position);

        void Bind(HumanoidRig rig, Animator animator)
        {
            rigRef = rig;
            body = rig.transform;
            if (rig.weaponSocket) socketRest = rig.weaponSocket.localPosition;
            // the player has it on the rig, bots / proxies on their root
            ca = rig.GetComponentInParent<CharacterAnimator>();
            if (!ca) ca = rig.transform.root.GetComponentInChildren<CharacterAnimator>();
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            modelHips = B(HumanBodyBones.Hips);

            // scale the model so its hips sit where the procedural hips rest
            float hipsH = BodyPos(modelHips).y;
            float rigHips = rig.hips.localPosition.y + (rig.rig ? rig.rig.localPosition.y : 0f);
            if (hipsH > 0.1f) transform.localScale = Vector3.one * (rigHips / hipsH);

            var chest = B(HumanBodyBones.Chest);
            var upper = B(HumanBodyBones.UpperChest);
            var neck = B(HumanBodyBones.Neck);
            var head = B(HumanBodyBones.Head);
            var spine = B(HumanBodyBones.Spine);
            Transform chestTop = upper ? upper : chest;
            mSpine = spine; mChestTop = chestTop; mNeck = neck; mHead = head;

            Quaternion Add(Transform m, Transform mChild, Transform r, Vector3 rigRestDir, Group g, bool follow, Quaternion? inherit = null)
            {
                if (!m || !r) return inherit ?? Quaternion.identity;
                Quaternion c;
                if (inherit.HasValue || !mChild) c = inherit ?? Quaternion.identity;
                else
                {
                    Vector3 md = BodyPos(mChild) - BodyPos(m);
                    c = md.sqrMagnitude > 1e-6f && rigRestDir.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(md, rigRestDir) : Quaternion.identity;
                }
                maps.Add(new Map { model = m, rig = r, correction = c, rest = BodyRot(m), group = g, follow = follow });
                return c;
            }

            Vector3 up = Vector3.up;
            Add(modelHips, spine, rig.hips, rig.spine ? rig.spine.localPosition : up, Group.Core, true);
            Add(spine, chest ? chest : chestTop, rig.spine, rig.chest ? rig.chest.localPosition : up, Group.Core, true);
            Add(chest, upper ? upper : neck, rig.chest, rig.neck ? rig.neck.localPosition : up, Group.Core, !upper);
            if (upper) Add(upper, neck, rig.chest, rig.neck ? rig.neck.localPosition : up, Group.Core, true);
            var nc = Add(neck, head, rig.neck, rig.head ? rig.head.localPosition : up, Group.Head, true);
            Add(head, null, rig.head, up, Group.Head, true, nc);

            void Limb(HumanBodyBones ua, HumanBodyBones la, HumanBodyBones hand, Transform rUa, Transform rLa, Transform rHand, Group g)
            {
                Add(B(ua), B(la), rUa, rLa ? rLa.localPosition : Vector3.down, g, true);
                var c = Add(B(la), B(hand), rLa, rHand ? rHand.localPosition : Vector3.down, g, true);
                Add(B(hand), null, rHand, Vector3.down, g, true, c);
            }
            Limb(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, rig.lUpperArm, rig.lForearm, rig.lHand, Group.LArm);
            Limb(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rig.rUpperArm, rig.rForearm, rig.rHand, Group.RArm);
            Limb(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, rig.lThigh, rig.lShin, rig.lFoot, Group.Legs);
            Limb(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, rig.rThigh, rig.rShin, rig.rFoot, Group.Legs);

            modelHead = head;
            if (head) modelHeadScale = head.localScale;
            Sync();
        }

        // ------------------------------------------------------------------ hit boxes on the real bones

        /// <summary>Colliders + Hitbox on the model's bones (only when the rig had hit boxes, i.e. not the local player).
        /// The procedural primitives' colliders are removed — they no longer match the animated body.</summary>
        void BuildHitboxes(HumanoidRig rig)
        {
            var old = rig.GetComponentsInChildren<Hitbox>(true);
            bool any = false;
            foreach (var h in old) if (!h.transform.IsChildOf(transform)) { any = true; break; }
            if (!any) return;
            int layer = old[0].gameObject.layer;
            foreach (var h in old)
            {
                if (h.transform.IsChildOf(transform)) continue;
                var c = h.GetComponent<Collider>();
                if (c) Destroy(c);
                Destroy(h);
            }

            float s = transform.lossyScale.x;
            Transform B(HumanBodyBones b) => an.GetBoneTransform(b);

            void Capsule(Transform a, Transform b, float radius, float mult)
            {
                if (!a || !b) return;
                var go = new GameObject("HB_" + a.name);
                go.layer = layer;
                go.transform.SetParent(a, false);
                Vector3 local = a.InverseTransformPoint(b.position);
                var col = go.AddComponent<CapsuleCollider>();
                float len = local.magnitude;
                go.transform.localPosition = local * 0.5f;
                go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, local.normalized);
                col.direction = 1;
                col.radius = radius / Mathf.Max(0.01f, a.lossyScale.x);
                col.height = len + col.radius * 1.2f;
                go.AddComponent<Hitbox>().multiplier = mult;
            }
            void Box(Transform a, Transform b, Vector3 sizeW, float mult, bool crit = false)
            {
                if (!a) return;
                var go = new GameObject("HB_" + a.name);
                go.layer = layer;
                go.transform.SetParent(a, false);
                Vector3 top = b ? b.position : a.position + body.up * sizeW.y;
                go.transform.position = (a.position + top) * 0.5f;
                go.transform.rotation = Quaternion.LookRotation(body.forward, (top - a.position).normalized);
                var col = go.AddComponent<BoxCollider>();
                float sc = Mathf.Max(0.01f, go.transform.lossyScale.x);
                col.size = new Vector3(sizeW.x, Mathf.Max(sizeW.y, (top - a.position).magnitude + 0.04f), sizeW.z) / sc;
                var hb = go.AddComponent<Hitbox>(); hb.multiplier = mult; hb.critical = crit;
            }

            var hips = B(HumanBodyBones.Hips);
            var spine = B(HumanBodyBones.Spine);
            var chest = B(HumanBodyBones.Chest);
            var upper = B(HumanBodyBones.UpperChest);
            var neck = B(HumanBodyBones.Neck);
            var head = B(HumanBodyBones.Head);
            Transform chestTop = upper ? upper : chest;

            Box(hips, spine, new Vector3(0.34f, 0.18f, 0.24f) * s, 1f);
            Box(spine, chest ? chest : chestTop, new Vector3(0.32f, 0.16f, 0.22f) * s, 1f);
            if (chest && upper) Box(chest, upper, new Vector3(0.36f, 0.14f, 0.24f) * s, 1f);
            Box(chestTop, neck, new Vector3(0.40f, 0.2f, 0.26f) * s, 1f);
            Capsule(neck, head, 0.06f * s, 1f);
            if (head)
            {
                // head: sphere around the skull (from the head bone up), critical
                var go = new GameObject("HB_Head");
                go.layer = layer;
                go.transform.SetParent(head, false);
                go.transform.position = head.position + body.up * 0.09f * s + body.forward * 0.015f * s;
                var col = go.AddComponent<SphereCollider>();
                col.radius = 0.125f * s / Mathf.Max(0.01f, head.lossyScale.x);
                var hb = go.AddComponent<Hitbox>(); hb.multiplier = 2.2f; hb.critical = true;
            }
            Capsule(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), 0.055f * s, 0.8f);
            Capsule(B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), 0.045f * s, 0.8f);
            Capsule(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), 0.055f * s, 0.8f);
            Capsule(B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), 0.045f * s, 0.8f);
            Capsule(B(HumanBodyBones.LeftHand), B(HumanBodyBones.LeftMiddleProximal), 0.04f * s, 0.6f);
            Capsule(B(HumanBodyBones.RightHand), B(HumanBodyBones.RightMiddleProximal), 0.04f * s, 0.6f);
            Capsule(B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), 0.08f * s, 0.8f);
            Capsule(B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), 0.065f * s, 0.8f);
            Capsule(B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), 0.08f * s, 0.8f);
            Capsule(B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), 0.065f * s, 0.8f);
            Capsule(B(HumanBodyBones.LeftFoot), B(HumanBodyBones.LeftToes), 0.055f * s, 0.6f);
            Capsule(B(HumanBodyBones.RightFoot), B(HumanBodyBones.RightToes), 0.055f * s, 0.6f);
        }

        // ------------------------------------------------------------------ animator parameters (before the Animator update)

        void Update()
        {
            if (!animated || !an || !ca) return;
            float dt = Time.deltaTime;
            Vector3 v = ca.localVelocity;
            an.SetFloat(pForward, v.z, 0.08f, dt);
            an.SetFloat(pRight, v.x, 0.08f, dt);
            an.SetFloat(pCrouch, ca.crouch);
            an.SetFloat(pProne, ca.prone);
            an.SetFloat(pPistol, ca.pistolHold, 0.1f, dt);
            an.SetBool(pGrounded, ca.grounded || ca.prone > 0.5f);

            bool reload = ca.reloading && ca.holdingWeapon;
            if (reload && !lastReload) an.SetTrigger(pReload);
            lastReload = reload;
            bool thr = ca.throwT >= 0f;
            if (thr && !lastThrow) an.SetTrigger(pThrow);
            lastThrow = thr;

            // procedural overrides per bone group (smoothed so nothing pops)
            float full = Mathf.Max(ca.dead > 0f ? 1f : 0f, ca.executeVictim >= 0f ? 1f : 0f, ca.slip > 0.01f ? 1f : 0f, ca.bentOver > 0.01f ? 1f : 0f);
            float arms = Mathf.Max(full, ca.executeAttack >= 0f ? 1f : 0f, ca.swordHold ? 1f : 0f, ca.blind > 0.01f ? 1f : 0f);
            float right = Mathf.Max(arms, ca.knife >= 0f ? 1f : 0f, ca.throwT >= 0f ? 1f : 0f);
            float core = Mathf.Max(full, ca.executeAttack >= 0f ? 1f : 0f);
            float head = Mathf.Max(full, ca.blind > 0.01f ? 1f : 0f);
            float k = ca.dead > 0f ? 18f : 10f;
            procW[(int)Group.Core] = Anim.Damp(procW[(int)Group.Core], core, k, dt);
            procW[(int)Group.Head] = Anim.Damp(procW[(int)Group.Head], head, k, dt);
            procW[(int)Group.LArm] = Anim.Damp(procW[(int)Group.LArm], arms, k, dt);
            procW[(int)Group.RArm] = Anim.Damp(procW[(int)Group.RArm], right, k, dt);
            procW[(int)Group.Legs] = Anim.Damp(procW[(int)Group.Legs], full, k, dt);

            upperW = Anim.Damp(upperW, ca.holdingWeapon ? 1f - ca.prone : 0f, 8f, dt);
            an.SetLayerWeight(1, upperW);
            ikW = Anim.Damp(ikW, ca.holdingWeapon && !reload && ca.prone < 0.5f ? 1f : 0f, 10f, dt);
        }

        void LateUpdate() => Sync();

        /// <summary>Final pose for this frame (runs every LateUpdate; public for editor previews).</summary>
        public void Sync()
        {
            if (!rigRef || !body) return;
            Quaternion br = body.rotation;
            if (!animated)
            {
                foreach (var m in maps)
                    m.model.rotation = br * (BodyRot(m.rig) * m.correction * m.rest);
                if (modelHips && rigRef.hips) modelHips.position = rigRef.hips.position;
                if (modelHead && rigRef.head) modelHead.localScale = Vector3.Scale(modelHeadScale, rigRef.head.localScale);
                return;
            }

            // 1) procedural overrides on top of the Animator pose
            foreach (var m in maps)
            {
                float w = procW[(int)m.group];
                if (w <= 0.001f) continue;
                Quaternion proc = br * (BodyRot(m.rig) * m.correction * m.rest);
                m.model.rotation = w >= 0.999f ? proc : Quaternion.Slerp(m.model.rotation, proc, w);
            }
            float wc = procW[(int)Group.Core];
            if (modelHips && rigRef.hips && wc > 0.001f) modelHips.position = Vector3.Lerp(modelHips.position, rigRef.hips.position, wc);

            // 2) aim pitch / lean / head look (the clips aim straight ahead)
            if (ca)
            {
                float add = 1f - wc;
                float rawPitch = Mathf.Clamp(ca.aimPitch, -70f, 70f) * (1f - ca.prone * 0.6f);
                float pitch = rawPitch * add;
                float lean = ca.lean * add;
                RotBody(mSpine, new Vector3(pitch * 0.25f, 0f, -12f * lean));
                RotBody(mChestTop, new Vector3(pitch * 0.35f, 0f, -12f * lean));
                float addHead = 1f - procW[(int)Group.Head];
                RotBody(mNeck, new Vector3(rawPitch * 0.4f * addHead, ca.lookYaw * 0.5f * addHead, 0f));
                RotBody(mHead, new Vector3(0f, ca.lookYaw * 0.5f * addHead, 0f));
            }
            if (modelHead && rigRef.head) modelHead.localScale = Vector3.Scale(modelHeadScale, rigRef.head.localScale);

            // 3) the procedural rig follows the model (aim points, executions keep using rig bones). The weapon socket
            // keeps its procedural pose (aimed by pitch): the clips twist the chest, the gun must still point where we aim.
            var sock = rigRef.weaponSocket;
            if (sock) sock.localPosition = socketRest; // last frame's world override must not accumulate
            Vector3 sockPos = sock ? sock.position : Vector3.zero;
            Quaternion sockRot = sock ? sock.rotation : Quaternion.identity;
            foreach (var m in maps)
            {
                if (!m.follow) continue;
                Quaternion modelBody = BodyRot(m.model);
                m.rig.rotation = br * (modelBody * Quaternion.Inverse(m.rest) * Quaternion.Inverse(m.correction));
            }
            if (modelHips && rigRef.hips) rigRef.hips.position = modelHips.position;
            if (sock) sock.SetPositionAndRotation(sockPos, sockRot);

            // 4) hands onto the held gun
            if (ikW > 0.001f && rigRef.weaponSocket && rigRef.weaponSocket.childCount > 0)
            {
                Transform gun = null;
                for (int i = 0; i < rigRef.weaponSocket.childCount; i++)
                {
                    var c = rigRef.weaponSocket.GetChild(i);
                    if (c.gameObject.activeInHierarchy) { gun = c; break; }
                }
                if (gun)
                {
                    var gr = gun.Find(HandGrips.Right);
                    var gl = gun.Find(HandGrips.Left);
                    Vector3 down = -body.up, side = body.right;
                    if (rArm != null && gr) rArm.Solve(gr, down + side * 0.6f - body.forward * 0.2f, ikW * (1f - procW[(int)Group.RArm]));
                    if (lArm != null && gl) lArm.Solve(gl, down - side * 0.8f, ikW * (1f - procW[(int)Group.LArm]));
                }
            }
        }

        void RotBody(Transform b, Vector3 euler)
        {
            if (!b || euler.sqrMagnitude < 1e-6f) return;
            Quaternion br = body.rotation;
            b.rotation = br * Quaternion.Euler(euler) * Quaternion.Inverse(br) * b.rotation;
        }
    }
}
