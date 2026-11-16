using System.Collections.Generic;
using FPS.Animation;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// Wears an imported skinned (Humanoid) character on a procedural HumanoidRig and animates it with the
    /// FPS.Animation framework (our replacement of the KINEMATION FPS Animation Framework).
    ///
    /// Animated mode (Resources/CharacterAnimSet present, built by Tools > FPS > Build Military Animations):
    ///  • the soldier controller plays the locomotion (8-way blend trees per stance, air, slide, vault, death)
    ///    from CharacterAnimator's parameters — movement itself stays ours (motor / NavMeshAgent, no root motion);
    ///  • an FPSAnimator runs the layers: Look (aim pitch / lean / head look over the spine), BodyAlign (eyes on the
    ///    aim frame), LegIK (feet on the ground), PoseOffset / Ads / Sway / Slot / Recoil / WeaponCollision (weapon);
    ///  • the hands are bound to the held weapon's grip anchors (GripR / GripL) by the core's grip IK;
    ///  • third person: the aim frame is the body's eyes (aligned to the stance height, pitched by the aim), the gun hangs
    ///    on rig.weaponSocket and is held like in first person: butt in the shoulder, sight onto the eye line when aiming;
    ///    first person (the local player, EnableFirstPerson): the aim frame is the camera, the player's own
    ///    WeaponControllers hang on the weapon bone and the head renders as a shadow only — true first person;
    ///  • procedural poses (executions, knife, abilities) are blended in per bone group from the procedural rig;
    ///  • the procedural rig follows the model, so rig.head / rig.chest / weaponSocket users keep working;
    ///  • hit boxes live on the model's real bones (head = critical).
    /// Without the animation set it falls back to the old retarget: the model copies the procedural rig's pose.
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
        // gun kept in the right hand while dead / not holding (pose relative to the hand, captured when it let go)
        bool gunInHand;
        Vector3 gunInHandPos;
        Quaternion gunInHandRot = Quaternion.identity;
        Transform body;          // HumanoidRig transform (shared reference frame)
        Transform modelHips;
        readonly List<Map> maps = new List<Map>();
        Transform modelHead;
        Vector3 modelHeadScale = Vector3.one;
        public Renderer[] Renderers { get; private set; }
        public int ModelIndex { get; private set; }

        // ---- animated mode
        Animator an;
        bool animated;
        CharacterAnimSet set;
        FPSAnimator core;
        LookLayer look;
        BodyAlignLayer align;
        LegIKLayer legIK;
        StockMountLayer stockMount;
        PoseOffsetLayer poseLayer;
        AdsLayer ads;
        SwayLayer sway;
        SlotLayer slot;
        RecoilLayer recoilLayer;
        WeaponCollisionLayer collision;
        Transform aimTarget;
        readonly float[] procW = new float[5];
        float gripW, alignW = 1f;
        float fpAimRelief = 0.2f; // eye → sight distance when aiming (m), per weapon
        // what the equipped weapon was measured with: a new optic / grip (WeaponAttachments.Rebuild) moves the sight
        // point and replaces the hand grip anchors, so the weapon is equipped again
        WeaponAttachments fpAttachments;
        Transform fpSight, fpGripR;
        string fpSignature = "";
        // where the body's eyes are relative to the camera pivot (the player's yaw frame, smoothed): the camera and the
        // aim frame sit there. Standing, the bent knees keep the eyes ~10 cm under the gameplay eye height, and the body
        // alignment never lifts the body to it (straight legs, tiptoes)
        Vector3 fpEye;
        bool fpEyeValid;
        AnimSequence currentPose, reloadSeq, throwSeq;
        bool poseApplied, lastReload, lastThrow;

        public bool Animated => animated;
        public Animator ModelAnimator => an;
        /// <summary>The character's own CharacterAnimator (motion state: velocity, slide, grounded...).</summary>
        public CharacterAnimator Driver => ResolveCharacterAnimator();
        public FPSAnimator Core => core;

        // ---- first person (the local player wears this body)
        public bool FirstPerson { get; private set; }
        PlayerCamera fpCam;
        Loadout fpLoadout;
        PlayerMotor fpMotor;
        WeaponController fpWeapon;
        readonly CameraShaker shaker = new CameraShaker();
        readonly Dictionary<WeaponController, RecoilProfile> recoilProfiles = new Dictionary<WeaponController, RecoilProfile>();
        readonly Dictionary<WeaponController, Transform> pivots = new Dictionary<WeaponController, Transform>();
        Renderer[] headRenderers = new Renderer[0], bodyRenderers = new Renderer[0];
        int visState = -1;

        // ---- third person: the gun under rig.weaponSocket, held the way the first-person body holds the player's own
        Transform heldGun;
        // the stance's eye height (the body aligns its eyes to it), and where the body's eyes really ended relative to
        // it (character frame, smoothed): the aim frame rides on the real eyes, like the camera in first person
        Transform tpEyeTarget;
        Vector3 tpEye;
        bool tpEyeValid;
        // the held gun: pistol, butt in the shoulder, sprint pose; the shoulder (aim) weight and the sprint blend
        bool tpPistol, tpStock;
        Vector3 tpSprintPos, tpSprintRot;
        float tpAim, tpSprint;
        // third person: above this ground speed (m/s) the gun swings into the sprint pose (players sprint at 7.4, walk
        // at 4.6; bots run at up to ~5.4)
        const float TpSprintSpeed = 5.8f;

        const float EyeStand = 1.68f, EyeCrouch = 1.0f, EyeProne = 0.4f; // PlayerMotor's eye heights

        static readonly int pVelX = Animator.StringToHash("VelX"), pVelZ = Animator.StringToHash("VelZ"), pSpeed = Animator.StringToHash("Speed"),
            pCrouch = Animator.StringToHash("Crouch"), pProne = Animator.StringToHash("Prone"), pGrounded = Animator.StringToHash("Grounded"),
            pVert = Animator.StringToHash("VertSpeed"), pSlide = Animator.StringToHash("Slide"), pVault = Animator.StringToHash("Vault"),
            pMantle = Animator.StringToHash("Mantle"), pVaultT = Animator.StringToHash("VaultT"), pDead = Animator.StringToHash("Dead"),
            pDeadFwd = Animator.StringToHash("DeadForward"), pAnimSpeed = Animator.StringToHash("AnimSpeed");

        static readonly string[] HeadParts = { "Head", "Cap", "Helmet", "Goggle", "Headset", "Balaclava", "Gsmask", "Gasmask", "NVG", "Hair", "Beard", "Eye", "Glasses", "Mask" };

        // ================================================================================================ setup

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
            sb.set = CharacterAnimSet.Instance;
            sb.animated = sb.set && sb.set.controller && Application.isPlaying;
            sb.an = an;
            sb.Bind(rig, an);
            if (sb.animated)
            {
                sb.SetupAnimated();
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
            ca = null;
            ResolveCharacterAnimator();
            Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
            modelHips = B(HumanBodyBones.Hips);

            if (animated && set.restEyeHeight > 0.5f)
                transform.localScale = Vector3.one * (EyeStand / set.restEyeHeight); // eyes at the camera height when standing
            else
            {
                // scale the model so its hips sit where the procedural hips rest
                float hipsH = BodyPos(modelHips).y;
                float rigHips = rig.hips.localPosition.y + (rig.rig ? rig.rig.localPosition.y : 0f);
                if (hipsH > 0.1f) transform.localScale = Vector3.one * (rigHips / hipsH);
            }

            var chest = B(HumanBodyBones.Chest);
            var upper = B(HumanBodyBones.UpperChest);
            var neck = B(HumanBodyBones.Neck);
            var head = B(HumanBodyBones.Head);
            var spine = B(HumanBodyBones.Spine);
            Transform chestTop = upper ? upper : chest;

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
            if (!animated) Sync();
        }

        /// <summary>The framework on the model (the model is still in its bind pose: the rig is calibrated from it).</summary>
        void SetupAnimated()
        {
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            an.enabled = true;

            core = an.gameObject.AddComponent<FPSAnimator>();
            core.autoUpdate = false;
            look = core.AddLayer<LookLayer>();
            align = core.AddLayer<BodyAlignLayer>();
            legIK = core.AddLayer<LegIKLayer>();
            stockMount = core.AddLayer<StockMountLayer>(); // on with a stocked gun in the hands (EnableFirstPerson / EquipHeldGun)
            stockMount.enabled = false;
            poseLayer = core.AddLayer<PoseOffsetLayer>();
            ads = core.AddLayer<AdsLayer>();
            sway = core.AddLayer<SwayLayer>();
            slot = core.AddLayer<SlotLayer>();
            recoilLayer = core.AddLayer<RecoilLayer>();
            collision = core.AddLayer<WeaponCollisionLayer>();
            align.eyeOffset = set.eyeOffset;
            align.cameraOffset = Vector3.zero;
            align.maxHorizontal = 0.35f;
            // the crouch clips stand taller than the crouch eye height (1.0 m): the knees bend (leg IK) instead of the
            // back hunching, so the pelvis may come down this far
            align.maxVertical = 0.45f;
            align.smoothSpeed = 8f; // follows stance changes, not the head bob of every step
            legIK.SetIgnoreRoot(rigRef.transform.parent ? rigRef.transform.parent : rigRef.transform);

            core.Initialize(an, transform, set.controller, set.upperBodyMask);
            core.PreIK += PlaceElbowHints;
            // the comic villain look a kill streak gives the character (seen by everyone else)
            if (!GetComponent<ComicAvatar>()) gameObject.AddComponent<ComicAvatar>();
            align.Calibrate();
            look.SetupHumanoid(core.rig, false);
            // third person: the head turns for lookYaw, the body / gun follow the aim frame
            look.lookRightOffset.Clear();
            if (core.rig.neck) look.lookRightOffset.Add(new LookLayer.AimOffsetBone(core.rig.neck, new Vector2(45f, 45f)));
            if (core.rig.head) look.lookRightOffset.Add(new LookLayer.AimOffsetBone(core.rig.head, new Vector2(45f, 45f)));

            aimTarget = new GameObject("AimTarget").transform;
            aimTarget.SetParent(core.rig.rootBone, false);
            ads.aimTarget = aimTarget;
            ads.enabled = sway.enabled = collision.enabled = false;
            core.weaponRoot = rigRef.weaponSocket;
            core.OnWeaponEquipped(new WeaponAnimSettings(), default);
            // third person: the body aligns its eyes to the stance's eye height; the aim frame (root bone) then sits on
            // the eyes where they really ended (FollowEyesThirdPerson), so the gun is held from the eyes as in first
            // person, not from ~10 cm above them (the bent knees keep the eyes under the stance height), which lifted the
            // arms up to the face. The butt goes into the right shoulder (StockMountLayer), as in first person
            tpEyeTarget = new GameObject("EyeTarget").transform;
            tpEyeTarget.SetParent(transform, false);
            align.target = tpEyeTarget;
            stockMount.shoulder = an.GetBoneTransform(HumanBodyBones.RightUpperArm);

            if (set.reloadRifle)
            {
                var mask = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.1f, 1f), new Keyframe(0.86f, 1f), new Keyframe(0.95f, 0f));
                reloadSeq = AnimSequence.Create(set.reloadRifle, 0.15f, 0.25f, set.upperBodyMask).WithCurve(AnimCurves.MaskLeftHand, mask);
            }
            if (set.throwGrenade)
            {
                var both = AnimationCurve.Constant(0f, 1f, 1f);
                throwSeq = AnimSequence.Create(set.throwGrenade, 0.1f, 0.2f, set.upperBodyMask)
                    .WithCurve(AnimCurves.MaskLeftHand, both).WithCurve(AnimCurves.MaskRightHand, both);
            }
        }

        // ================================================================================================ first person

        /// <summary>
        /// True first person for the local player: the camera sits at the body's eyes and is the aim frame, the player's
        /// WeaponControllers hang on the body's weapon bone (no separate arms model), the head is a shadow. False if this
        /// body is not animated.
        /// </summary>
        public bool EnableFirstPerson(PlayerCamera pc, Loadout loadout, PlayerMotor motor)
        {
            if (!animated || !pc || !loadout) return false;
            FirstPerson = true;
            fpCam = pc; fpLoadout = loadout; fpMotor = motor;
            look.SetupHumanoid(core.rig, true);
            look.useRightOffset = false;
            // the butt rests in the right shoulder; the camera-space hold position only gives the gun's direction
            stockMount.enabled = true;
            stockMount.shoulder = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            ads.enabled = sway.enabled = collision.enabled = true;
            // the body follows the gameplay pivot (motor eye height, bob, lean); the camera and the aim frame then come
            // to the eyes (FollowEyes). The eyes a little in front of the pivot: the shoulders come forward enough for
            // the arms to hold the gun; looking down, the bending head moves the body back
            align.target = pc.fxPivot;
            align.cameraOffset = new Vector3(0f, -0.02f, 0.02f);
            Transform owner = motor ? motor.transform : rigRef.transform.root;
            collision.SetIgnoreRoot(owner);
            legIK.SetIgnoreRoot(owner);
            int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
            collision.layerMask = mask;
            core.weaponRoot = null;

            foreach (var w in loadout.weapons)
            {
                if (!w) continue;
                w.EnsureInit();
                w.AnimDriven = true;
                w.transform.SetParent(core.rig.weaponBone, false);
                w.transform.localPosition = Vector3.zero;
                w.transform.localRotation = Quaternion.identity;
                HideProceduralArms(w);
                foreach (var r in w.model.GetComponentsInChildren<Renderer>(true))
                    if (!(r is ParticleSystemRenderer) && !r.name.Contains("Flash")) r.shadowCastingMode = ShadowCastingMode.On;
                var wc = w;
                w.Fired += () => OnFired(wc);
            }
            loadout.ActiveChanged += Equip;
            if (loadout.Active) Equip(loadout.Active);

            var head = new List<Renderer>(); var rest = new List<Renderer>();
            foreach (var r in Renderers)
            {
                if (!r) continue;
                bool isHead = false;
                foreach (var h in HeadParts) if (r.name.Contains(h)) { isHead = true; break; }
                (isHead ? head : rest).Add(r);
            }
            headRenderers = head.ToArray();
            bodyRenderers = rest.ToArray();
            visState = -1;
            return true;
        }

        static readonly string[] ArmParts = { "Glove", "Finger", "Thumb", "Cuff", "Forearm", "UpperArm" };

        static void HideProceduralArms(WeaponController w)
        {
            foreach (var r in w.model.GetComponentsInChildren<Renderer>(true))
                foreach (var p in ArmParts)
                    if (r.name.Contains(p)) { r.forceRenderingOff = true; if (!r.GetComponent<ImportHidden>()) r.gameObject.AddComponent<ImportHidden>(); break; }
        }

        Transform Pivot(WeaponController w)
        {
            if (pivots.TryGetValue(w, out var p) && p) return p;
            p = new GameObject("FP_Pivot").transform;
            p.SetParent(w.transform, false);
            var grip = HandGrips.Find(w.model, HandGrips.Right);
            // rotations pivot a little ahead of the trigger hand (center of the gun's mass)
            p.localPosition = grip ? w.transform.InverseTransformPoint(grip.position) + new Vector3(0f, 0.02f, 0.06f) : Vector3.zero;
            pivots[w] = p;
            return p;
        }

        RecoilProfile Recoil(WeaponController w)
        {
            if (recoilProfiles.TryGetValue(w, out var d) && d) return d;
            d = RecoilProfile.CreateRuntime(w.weaponName, w.kickScale, w.recoilUp, w.slot == WeaponSlot.Secondary);
            recoilProfiles[w] = d;
            return d;
        }

        // true first person: where the trigger hand holds the gun, ahead of the eyes (m). The view-model offsets of the
        // weapons held them ~0.2 m further out than a shoulder can reach; rifles: stock at the shoulder. Pistols: in front
        // of the chest (lowered by FpPistolHipDrop), pushed out to nearly straight arms when aiming (at 0.24 m the gun
        // was at the face and the gloves filled the view)
        const float FpRifleGripZ = 0.08f, FpPistolGripZ = 0.30f, FpPistolAimGripZ = 0.40f, FpPistolHipDrop = 0.05f;
        // bladed rifle stance (torso twist to the right): the support shoulder comes forward to the handguard
        const float FpRifleStanceYaw = 28f;
        // prone: chest arched up on the elbows (degrees over the spine chain)
        const float PronePropAngle = 24f;
        // aiming: the gun rises only this fraction of the way to the eye line, the head comes down (degrees) and the eye
        // goes to the sight the rest of the way. A shouldered rifle comes up a few cm (head onto the stock; the body is
        // not lifted: the knees stay bent and the butt stays in the shoulder); a pistol comes most of the way up
        const float AdsGunRise = 0.2f, AdsHeadDrop = 14f, AdsGunRisePistol = 0.7f, AdsHeadDropPistol = 6f;
        // the camera follows the body's eyes this fast (1/s: quick enough to stay with the head when looking down, while
        // softening the steps' bob), at most this far (m; looking at the feet the eyes are ~0.3 m under the pivot)
        const float EyeFollowSpeed = 12f, MaxEyeOffset = 0.45f;
        // aiming, the camera rides on the sight: the eye and the butt in the shoulder then follow the body slowly (1/s),
        // keeping the stance's average and dropping each step's sway (strafing swung them by up to 14 cm, and the whole
        // view jerked with every step)
        const float EyeFollowSpeedAim = 1.5f, StockFollow = 14f, StockFollowAim = 1.2f;
        // aimed, the eye follows the sight's place at this rate (1/s): a burst's kicks (~10 per second) pass under it
        const float AdsEyeFollow = 7f;
        // aimed: how far the gun may sway off the line of sight before the eye turns with it (degrees); how far the eye
        // may trail off the sight's axis (m; behind an optic a share of its lens radius, its eyebox) and along it (a
        // share of the eye relief: a crouch moved the gun 5 cm along its axis under the trailing eye, and behind a 4x
        // the eye ended up inside the tube). The trail eases into these limits, so the sway stays and the reticle never
        // leaves the middle of the sight
        const float AdsSwayLimit = 1.2f, AdsSwayLimitOptic = 0.4f, AdsEyeSlack = 0.003f, OpticEyeSlack = 0.12f;
        const float AdsEyeTrail = 0.12f;
        // a magnified eyepiece is seen at this apparent half-angle (degrees): a wide field that fills most of the aimed
        // view's height (14° was half the view; from the cheek weld the prism's ocular was a 5° hole)
        const float OpticHalfAngle = 20f;
        Vector3 adsEye;
        bool adsEyeValid;
        OpticSight fpOptic;
        Quaternion adsTilt = Quaternion.identity;
        // standing / moving: extra pelvis drop for bent knees (m), on top of the ~4 cm the leg IK takes to plant the
        // clip's feet. 0 left ~153° at the knee, 4 cm gave a half squat (~135°): 3.5 cm is a ready stance (~140°)
        const float StandKneeDrop = 0.035f;

        void Equip(WeaponController w)
        {
            if (!w || !core) return;
            fpWeapon = w;
            bool pistol = w.slot == WeaponSlot.Secondary;
            Vector3 hip = w.hipPosition;
            float aimDist = w.aimDistance;
            var gripR = HandGrips.Find(w.model, HandGrips.Right);
            if (gripR)
            {
                // the gun hangs on the weapon bone untransformed: its local space is the bone's (camera-aligned) space
                Vector3 g = w.transform.InverseTransformPoint(gripR.position);
                float gripZ = pistol ? FpPistolGripZ : FpRifleGripZ;
                hip.z = gripZ - g.z;
                if (pistol) hip.y -= FpPistolHipDrop;
                // the sight distance follows from the gun's own geometry and where the hand goes aiming (a rifle's
                // stays at the shoulder, a pistol is pushed out)
                float aimGripZ = pistol ? FpPistolAimGripZ : FpRifleGripZ;
                if (w.sightPoint) aimDist = Mathf.Max(0.08f, aimGripZ + (w.transform.InverseTransformPoint(w.sightPoint.position).z - g.z));
            }
            look.stanceYaw = pistol ? 0f : FpRifleStanceYaw;
            fpAimRelief = Mathf.Clamp(aimDist, 0.08f, 0.5f);
            bool stock = StockMountLayer.FindButtTop(w.transform, w.model, w.muzzle, gripR, out var butt);
            stockMount.hasStock = stock && !pistol;
            stockMount.buttTop = stockMount.hasStock ? butt : Vector3.zero;
            fpAttachments = w.GetComponent<WeaponAttachments>();
            fpSignature = fpAttachments ? fpAttachments.Signature : "";
            fpOptic = w.GetComponentInChildren<OpticSight>();
            fpSight = w.sightPoint;
            fpGripR = gripR;
            var s = new WeaponAnimSettings
            {
                weaponBone = new LocRot(hip, Quaternion.identity),
                aimDistance = aimDist,
                ads = new AdsData(1f / Mathf.Max(0.05f, w.AimTime)) { changeSightSpeed = 30f, pointAimSpeed = 1f },
                recoil = Recoil(w),
                gripIK = true,
            };
            s.sway.loc.scale = new Vector3(-0.018f, -0.018f, 0f);
            s.sway.rot.scale = new Vector3(1.4f, -1.2f, -2f);
            var pivot = Pivot(w);
            float muzzle = w.muzzle ? w.transform.InverseTransformPoint(w.muzzle.position).z - pivot.localPosition.z : 0.6f;
            s.block = new GunBlockData(new LocRot(new Vector3(0.02f, -0.06f, -0.08f), Quaternion.Euler(-35f, -15f, 12f)))
            {
                weaponLength = Mathf.Max(0.25f, muzzle) + 0.12f, startOffset = 0.12f, threshold = 0.22f,
            };
            var t = new WeaponTransforms
            {
                pivotPoint = pivot, aimPoint = w.sightPoint,
                rightHandTarget = HandGrips.Find(w.model, HandGrips.Right),
                leftHandTarget = HandGrips.Find(w.model, HandGrips.Left),
            };
            aimTarget.localPosition = new Vector3(0f, 0f, aimDist);
            core.OnWeaponEquipped(s, t);
            ads.ResetSight();
        }

        // the gun's own kick in the hands, procedural and smooth: back into the shoulder and the muzzle up a little,
        // springing back critically damped (no shake, nothing random). The bullets follow the barrel, so a kick still up
        // at the next shot of a burst lifts that shot too. Peaks: ~0.45° up and ~2 cm back at kickScale 1 (the climb itself
        // is the view's, PlayerCamera.AddRecoil: a bigger kick here held the sight off the view center in a burst)
        readonly Spring3 kickRot = new Spring3(170f, 26.1f), kickPos = new Spring3(190f, 27.6f);
        int kickCount;

        void OnFired(WeaponController w)
        {
            if (w != fpWeapon) return;
            float k = Mathf.Clamp(w.kickScale, 0.4f, 2.5f) * Mathf.Lerp(1f, 0.85f, w.AimBlend) * (fpMotor && fpMotor.IsProne ? 0.6f : 1f);
            kickCount++;
            float roll = kickCount % 2 == 0 ? 1f : -1f; // alternating, not random
            // aimed, the muzzle flip and the cant are what the eye sees on the reticle: a burst made it bounce and rock
            // with every shot. They are kept small there; the felt recoil is the view's climb (PlayerCamera.AddRecoil)
            float flip = Mathf.Lerp(1f, 0.5f, w.AimBlend), cant = Mathf.Lerp(1f, 0.3f, w.AimBlend);
            kickRot.Kick(new Vector3(-16f * flip, 0f, roll * 9f * cant) * k);  // deg/s
            kickPos.Kick(new Vector3(0f, 0.05f, -0.75f) * k);   // m/s
        }

        void OnDestroy()
        {
            if (fpLoadout) fpLoadout.ActiveChanged -= Equip;
            foreach (var d in recoilProfiles.Values) if (d) Destroy(d);
        }

        // ================================================================================================ per frame

        /// <summary>The character's own CharacterAnimator: the player has it on the rig, bots / proxies on their root.
        /// Bots can get theirs after the model is attached, so this resolves lazily, and never searches wider than the
        /// character itself (bots share a manager root: a root-wide search picked another bot's animator).</summary>
        CharacterAnimator ResolveCharacterAnimator()
        {
            if (ca || !rigRef) return ca;
            ca = rigRef.GetComponentInParent<CharacterAnimator>();
            if (!ca && rigRef.transform.parent) ca = rigRef.transform.parent.GetComponentInChildren<CharacterAnimator>();
            return ca;
        }

        /// <summary>Animator parameters, overlay pose, one-shots and blend weights (before the Animator evaluates).</summary>
        void Update()
        {
            if (!animated || !ResolveCharacterAnimator() || core == null || core.Graph == null) return;
            float dt = Time.deltaTime;
            var g = core.Graph;
            bool dead = ca.dead > 0.001f;
            Vector3 v = ca.localVelocity;
            g.SetFloat(pVelX, v.x, 0.08f, dt);
            g.SetFloat(pVelZ, v.z, 0.08f, dt);
            float speed = new Vector2(v.x, v.z).magnitude;
            g.SetFloat(pSpeed, speed);
            g.SetFloat(pCrouch, ca.crouch);
            g.SetFloat(pProne, ca.prone);
            g.SetBool(pGrounded, ca.grounded || ca.prone > 0.5f);
            g.SetFloat(pVert, ca.verticalSpeed);
            g.SetBool(pSlide, ca.sliding && !dead);
            g.SetBool(pVault, ca.vault >= 0f && !dead);
            g.SetBool(pMantle, ca.vault >= 0f && ca.mantle && !dead);
            g.SetFloat(pVaultT, Mathf.Clamp01(ca.vault));
            g.SetBool(pDead, dead);
            g.SetBool(pDeadFwd, ca.deadDir > 0f);
            float maxRing = set.speeds.z > 0f ? set.speeds.z : 7.4f;
            g.SetFloat(pAnimSpeed, speed > maxRing ? speed / maxRing : 1f);

            // weapon hold pose over the arms. Not lying down: the humanoid keeps the body orientation, which averages the
            // hips with the shoulders, so standing shoulders over the prone clip turned the pelvis and lifted the legs
            // ~7° off the ground. Prone the clip's own arms (on the elbows) are the base and the hand IK holds the gun
            bool holding = ca.holdingWeapon && !dead;
            var want = holding && ca.prone < 0.5f ? (ca.pistolHold > 0.5f ? set.pistolHold : set.rifleHold) : null;
            if (!poseApplied || want != currentPose)
            {
                currentPose = want;
                poseApplied = true;
                if (want) g.PlayPose(want); else g.ClearPose(0.25f);
            }

            // upper-body one-shots (third person: the local player's reload is the weapon's own procedural one)
            bool reload = ca.reloading && holding && !FirstPerson;
            if (reload && !lastReload && reloadSeq) g.PlayAnimation(reloadSeq);
            if (!reload && lastReload) g.StopAnimation(0.2f);
            lastReload = reload;
            bool thr = ca.throwT >= 0f && !FirstPerson;
            if (thr && !lastThrow && throwSeq) g.PlayAnimation(throwSeq);
            lastThrow = thr;

            // procedural overrides per bone group (smoothed so nothing pops); deaths are clips now
            float full = Mathf.Max(ca.executeVictim >= 0f ? 1f : 0f, ca.slip > 0.01f ? 1f : 0f, ca.bentOver > 0.01f ? 1f : 0f);
            float arms = Mathf.Max(full, ca.executeAttack >= 0f ? 1f : 0f, ca.swordHold ? 1f : 0f, ca.blind > 0.01f ? 1f : 0f);
            float right = Mathf.Max(arms, ca.knife >= 0f && !FirstPerson ? 1f : 0f, ca.throwT >= 0f && !throwSeq && !FirstPerson ? 1f : 0f);
            float coreW = Mathf.Max(full, ca.executeAttack >= 0f ? 1f : 0f);
            float head = Mathf.Max(full, ca.blind > 0.01f ? 1f : 0f);
            const float k = 10f;
            procW[(int)Group.Core] = Anim.Damp(procW[(int)Group.Core], coreW, k, dt);
            procW[(int)Group.Head] = Anim.Damp(procW[(int)Group.Head], head, k, dt);
            procW[(int)Group.LArm] = Anim.Damp(procW[(int)Group.LArm], arms, k, dt);
            procW[(int)Group.RArm] = Anim.Damp(procW[(int)Group.RArm], right, k, dt);
            procW[(int)Group.Legs] = Anim.Damp(procW[(int)Group.Legs], full, k, dt);

            bool fpHolding = !FirstPerson || (fpWeapon && fpWeapon.gameObject.activeInHierarchy && !SkyArena.LocalRunner);
            // prone included: the arms hold the gun (propped on the elbows) instead of the clip's arms reaching forward
            gripW = Anim.Damp(gripW, holding && fpHolding ? 1f : 0f, 10f, dt);
            bool special = dead || ca.vault >= 0f || full > 0.5f;
            alignW = Anim.Damp(alignW, special ? 0f : 1f, 8f, dt);
        }

        void LateUpdate()
        {
            if (!animated) { Sync(); return; }
            if (core == null || !core.Ready || !ResolveCharacterAnimator()) return;
            float dt = Time.deltaTime;

            PlaceRootBone();
            FeedCharData(dt);
            if (FirstPerson) UpdateFirstPersonWeapon(dt); else UpdateHeldGun(dt);
            core.handIkWeight = gripW;
            core.RightGripWeight = 1f - procW[(int)Group.RArm];
            core.LeftGripWeight = 1f - procW[(int)Group.LArm];
            look.SetLayerAlpha((1f - procW[(int)Group.Core]) * (ca.dead > 0.001f ? 0f : 1f));
            align.SetLayerAlpha(alignW);
            // prone: the feet are not planted (the leg IK kept them where they were while the body slid to the camera,
            // folding the legs up), and the chest props up on the elbows to bring the eyes to the prone eye height
            float prone = Mathf.Clamp01(ca.prone);
            core.legIkWeight = 1f - prone;
            legIK.SetLayerAlpha(1f - prone);
            // lying, the head is ~0.6 m ahead of the pelvis: the body slides back further to put the eyes on the camera
            // (clamped at the standing limit the camera ended up inside the shoulders)
            align.maxHorizontal = Mathf.Lerp(0.35f, 0.9f, prone);
            // never lift the body to the camera when on the feet (straight legs, the feet came off the ground: "tiptoes");
            // the eyes may sit a few cm under the camera instead (the head is hidden). Standing the knees bend a little
            align.maxLift = Mathf.Lerp(0f, 0.08f, prone);
            float crouch = Mathf.Clamp01(ca.crouch);
            align.pelvisDrop = StandKneeDrop * (1f - crouch) * (1f - prone);
            look.stancePitch = PronePropAngle * prone;
            // lying, the shoulder is low under the eyes and the head far ahead of it: the butt in the shoulder put the
            // optic above and behind the eye, filling the view. Crouched alike: the deep crouch leans the chest far
            // forward and its shoulders sit well behind the eyes, so the butt pulled the sight up against the eye in hip
            // fire. Lying or crouched the gun is held from the eyes
            float heldFromEyes = Mathf.Max(prone, crouch);
            stockMount.SetLayerAlpha(1f - heldFromEyes);
            // aiming: cheek onto the stock, or the head a little forward over a pistol (the rest is the eye going to the
            // sight, PlaceEyeCamera)
            float aimW = FirstPerson && fpWeapon ? Anim.Smooth(fpWeapon.AimBlend) : 0f;
            // (third person the head comes down by the held gun's shoulder weight; its stock keeps following the
            // shoulder briskly: no camera rides on the sight there)
            float headAim = FirstPerson ? aimW : tpAim;
            look.headDrop = (stockMount.hasStock ? AdsHeadDrop * (1f - heldFromEyes) : AdsHeadDropPistol) * headAim;
            stockMount.smoothSpeed = Mathf.Lerp(StockFollow, StockFollowAim, aimW);

            core.Evaluate(dt);

            ApplyOverrides();
            FollowRig();
            if (!FirstPerson) { PlaceSocket(); FollowEyesThirdPerson(dt); }
            if (modelHead && rigRef.head) modelHead.localScale = Vector3.Scale(modelHeadScale, rigRef.head.localScale);
            if (FirstPerson)
            {
                PlaceEyeCamera(dt);
                FollowEyes(dt);
                FirstPersonVisibility();
                CameraShake(dt);
                // shots fired before the next pose use the barrel where the body holds it now (recoil kick included)
                if (fpWeapon && fpCam && fpCam.cam) fpWeapon.CacheAimFrame(fpCam.cam.transform);
            }
        }

        /// <summary>
        /// The camera is the body's eye: it sits at the eyes (fpEye, this frame's aim frame) inside its pivot, and when
        /// aiming it goes to the sight line instead of the gun coming all the way up to the eye (AdsLayer.positionScale):
        /// the weapon's sight distance behind the sight point, whatever optic is mounted (the sight point follows the
        /// attachments). The body is aligned to the pivot, not to the moved camera, so nothing chases it. Once the gun is
        /// up the eye follows the sight's place smoothly (AdsEyeFollow), not each shot's kick: the gun kicks in front of
        /// a steady eye (riding the kick 1:1 shook the whole view back and forth with every shot).
        /// </summary>
        void PlaceEyeCamera(float dt)
        {
            if (!fpCam || !fpCam.cam || !fpCam.fxPivot || fpCam.ThirdPerson > 0.001f) return;
            var camT = fpCam.cam.transform;
            var pivot = fpCam.fxPivot;
            var w = fpWeapon;
            Vector3 eye = pivot.position + fpCam.transform.rotation * fpEye;
            float a = w && w.sightPoint && w.gameObject.activeInHierarchy ? Anim.Smooth(w.AimBlend) : 0f;
            adsTilt = Quaternion.identity;
            if (a > 0.0001f)
            {
                bool optic = fpOptic && fpOptic.Magnified && fpOptic.LensRadius > 0f;
                // behind a magnified eyepiece the eye comes close enough for it to fill a good part of the view
                float relief = optic ? Mathf.Clamp(fpOptic.EyeReliefFor(w.sightPoint, OpticHalfAngle), 0.03f, fpAimRelief) : fpAimRelief;
                Vector3 axisW = w.transform.forward;
                Vector3 axis = pivot.InverseTransformDirection(axisW);
                Vector3 onSight = pivot.InverseTransformPoint(w.sightPoint.position - axisW * relief);
                if (adsEyeValid && a > 0.6f)
                {
                    // the eye trails the sight's place: along its axis by a share of the relief (a shot's kick passes
                    // under it), across it hardly at all (the sight stays framed around the eye however the body moves)
                    Vector3 lag = adsEye - onSight;
                    float along = Vector3.Dot(lag, axis);
                    float slack = optic ? fpOptic.LensRadius * OpticEyeSlack : AdsEyeSlack;
                    adsEye = onSight + axis * EyeTrail(along, relief * AdsEyeTrail, dt) + EyeTrail(lag - axis * along, slack, dt);
                }
                else adsEye = onSight;
                adsEyeValid = true;
                eye = Vector3.Lerp(eye, pivot.TransformPoint(adsEye), a);
                // the gun sways around the line of sight, but only so far: past the limit the eye turns with it, so the
                // reticle stays near the middle of the sight and an optic's picture in its eyebox (applied in CameraShake)
                Vector3 viewF = pivot.forward;
                float ang = Vector3.Angle(viewF, axisW);
                float limit = optic ? AdsSwayLimitOptic : AdsSwayLimit;
                if (ang > limit)
                {
                    Quaternion follow = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(viewF, axisW), (ang - limit) / ang * a);
                    adsTilt = Quaternion.Inverse(pivot.rotation) * follow * pivot.rotation;
                }
            }
            else adsEyeValid = false;
            Vector3 offset = pivot.InverseTransformPoint(eye);
            // the camera keeps it for the next frame's updates too (the collimated reticle, firing): every script
            // between now and the next aim update sees the same eye
            fpCam.EyeOffset = offset;
            camT.localPosition = offset;
        }

        /// <summary>
        /// One frame of the aimed eye's trail behind the sight (the lag, this frame's target move included): it decays at
        /// AdsEyeFollow, faster the nearer it is to its limit (a soft stop, not a wall), and never passes the limit.
        /// </summary>
        static float EyeTrail(float lag, float limit, float dt)
        {
            float u = Mathf.Abs(lag) / Mathf.Max(limit, 1e-5f);
            return Mathf.Clamp(lag * Mathf.Exp(-AdsEyeFollow * (1f + 3f * u * u) * dt), -limit, limit);
        }

        static Vector3 EyeTrail(Vector3 lag, float limit, float dt)
        {
            float m = lag.magnitude;
            return m > 1e-7f ? lag * (EyeTrail(m, limit, dt) / m) : Vector3.zero;
        }

        /// <summary>
        /// Where the body's eyes ended this frame relative to the camera pivot, for the next frame's aim frame and camera.
        /// In the player's yaw frame (looking up and down does not swing it) and smoothed (the stance height, not each
        /// step's bob). While the body leaves the camera (vaults, executions) the eye height is held; dead, the camera
        /// goes back to the pivot (the death camera) and snaps to the eyes again once the body is aligned.
        /// </summary>
        void FollowEyes(float dt)
        {
            if (!fpCam || !fpCam.fxPivot) return;
            Vector3 target = Vector3.zero;
            if (ca.dead > 0.001f) fpEyeValid = false;
            else if (alignW < 0.99f) return;
            else target = Vector3.ClampMagnitude(Quaternion.Inverse(fpCam.transform.rotation) * (align.EyePosition - fpCam.fxPivot.position), MaxEyeOffset);
            bool snap = !fpEyeValid && ca.dead <= 0.001f;
            float speed = Mathf.Lerp(EyeFollowSpeed, EyeFollowSpeedAim, fpWeapon ? Anim.Smooth(fpWeapon.AimBlend) : 0f);
            fpEye = snap ? target : Anim.Damp(fpEye, target, speed, dt);
            fpEyeValid = ca.dead <= 0.001f;
        }

        /// <summary>Aim frame: the camera (first person) or a virtual eye at the stance's eye height, pitched by the aim.</summary>
        void PlaceRootBone()
        {
            var root = core.rig.rootBone;
            if (FirstPerson && fpCam && fpCam.fxPivot)
            {
                // the eye: the gameplay pivot plus where the body's eyes sit under it (the camera goes there too)
                root.SetPositionAndRotation(fpCam.fxPivot.position + fpCam.transform.rotation * fpEye, fpCam.fxPivot.rotation);
                return;
            }
            float eye = Mathf.Lerp(Mathf.Lerp(EyeStand, EyeCrouch, Mathf.Clamp01(ca.crouch)), EyeProne, Mathf.Clamp01(ca.prone)) * body.lossyScale.y;
            Vector3 pos = body.position + body.up * eye + body.right * (ca.lean * 0.26f) + body.forward * 0.03f;
            Quaternion rot = body.rotation * Quaternion.Euler(Mathf.Clamp(ca.aimPitch, -80f, 80f), 0f, -ca.lean * 14f);
            // the body brings its eyes to the stance's eye height (BodyAlignLayer); the aim frame sits where they really are
            if (tpEyeTarget) tpEyeTarget.SetPositionAndRotation(pos, rot);
            root.SetPositionAndRotation(pos + body.rotation * tpEye, rot);
        }

        /// <summary>
        /// Third person, like FollowEyes in first person: where the body's eyes ended this frame relative to the stance's
        /// eye height, for the next frame's aim frame. In the character's frame and smoothed; held while the body leaves
        /// it (vaults, executions); dead it starts again from the real eyes at the next life.
        /// </summary>
        void FollowEyesThirdPerson(float dt)
        {
            if (!tpEyeTarget) return;
            if (ca.dead > 0.001f) { tpEyeValid = false; return; }
            if (alignW < 0.99f) return;
            Vector3 target = Vector3.ClampMagnitude(Quaternion.Inverse(body.rotation) * (align.EyePosition - tpEyeTarget.position), MaxEyeOffset);
            tpEye = tpEyeValid ? Anim.Damp(tpEye, target, EyeFollowSpeed, dt) : target;
            tpEyeValid = true;
        }

        void FeedCharData(float dt)
        {
            var d = core.characterData;
            if (FirstPerson)
            {
                d.deltaAimInput = fpCam.LastLookDelta;
                d.totalAimInput = new Vector2(0f, -fpCam.Pitch);
                d.moveInput = fpMotor ? fpMotor.MoveInput : Vector2.zero;
                d.lean = fpCam.Lean;
                d.aim = fpWeapon ? fpWeapon.AimBlend : 0f;
                d.recoilAnim = new LocRot(kickPos.Step(dt), Quaternion.Euler(kickRot.Step(dt)));
            }
            else
            {
                d.deltaAimInput = Vector2.zero;
                d.totalAimInput = new Vector2(ca.lookYaw, -ca.aimPitch);
                d.moveInput = Vector2.ClampMagnitude(new Vector2(ca.localVelocity.x, ca.localVelocity.z) / 4.6f, 1f);
                d.lean = ca.lean;
                d.aim = ca.aim;
                d.recoilAnim = LocRot.Identity;
            }
            d.velocity = ca.localVelocity;
            d.grounded = ca.grounded || ca.prone > 0.5f;
            core.characterData = d;
        }

        void UpdateFirstPersonWeapon(float dt)
        {
            var w = fpLoadout ? fpLoadout.Active : null;
            if (w && (w != fpWeapon || WeaponRebuilt(w))) Equip(w);
            w = fpWeapon;
            if (w)
            {
                poseLayer.offset = new LocRot(w.PoseOffsetPos, Quaternion.Euler(w.PoseOffsetRot));
                // the gun turns fully onto the sight line but comes up only part of the way; the eye goes to the sight
                // the rest (PlaceEyeCamera). Prone or crouched (no butt in the shoulder) the gun comes all the way to the eye
                ads.positionScale = stockMount.hasStock ? Mathf.Lerp(AdsGunRise, 1f, Mathf.Clamp01(Mathf.Max(ca.prone, ca.crouch))) : AdsGunRisePistol;
                ads.SetAdsWeight(w.AimBlend);
            }
        }

        /// <summary>
        /// The equipped weapon was re-assembled since it was measured (another optic or grip): the sight point moved or
        /// the hand grip anchors were replaced (the old ones are destroyed).
        /// </summary>
        bool WeaponRebuilt(WeaponController w) =>
            w.sightPoint != fpSight || (fpGripR is object && !fpGripR) ||
            (fpAttachments && fpAttachments.Signature != fpSignature);

        /// <summary>
        /// Third person (bots, other players): the gun under rig.weaponSocket, held the way the first-person body holds
        /// the local player's gun (Equip / UpdateFirstPersonWeapon): from the real eyes, the trigger hand just ahead of
        /// them, the butt in the right shoulder, a bladed stance for long guns. Aiming turns it onto the line of sight
        /// (AdsLayer) and brings the head down onto the stock; running fast it swings into the weapon's sprint pose.
        /// </summary>
        void UpdateHeldGun(float dt)
        {
            var sock = rigRef.weaponSocket;
            Transform gun = null;
            if (sock)
                for (int i = 0; i < sock.childCount; i++)
                {
                    var c = sock.GetChild(i);
                    if (c.gameObject.activeInHierarchy) { gun = c; break; }
                }
            if (gun != heldGun) EquipHeldGun(sock, gun);
            core.weaponRoot = sock;

            // shouldered by the aim weight (bots engaging, players aiming); the gun rises as far as in first person
            // (a stocked gun only a little: the head comes down onto it, lying / crouched all the way)
            float a = gun ? Anim.Smooth(Mathf.Clamp01((ca.aim - 0.3f) / 0.6f)) : 0f;
            tpAim = a;
            float low = Mathf.Clamp01(Mathf.Max(ca.prone, ca.crouch));
            ads.positionScale = tpStock ? Mathf.Lerp(AdsGunRise, 1f, low) : AdsGunRisePistol;
            ads.SetAdsWeight(a);

            // running fast, not aiming: the weapon's own sprint pose (across the chest), like the first-person gun
            float speed = new Vector2(ca.localVelocity.x, ca.localVelocity.z).magnitude;
            bool sprint = gun && speed > TpSprintSpeed && a < 0.3f && ca.grounded && low < 0.5f && !ca.reloading && !ca.sliding;
            tpSprint = Anim.Damp(tpSprint, sprint ? 1f : 0f, 9f, dt);
            poseLayer.offset = new LocRot(tpSprintPos * tpSprint, Quaternion.Euler(tpSprintRot * tpSprint));
            if (!gun) gripW = 0f;
        }

        /// <summary>
        /// Third person: a new gun in the hands (or none). Measured like the first-person Equip: the hip hold puts the
        /// trigger hand FpRifleGripZ / FpPistolGripZ ahead of the eyes, aiming the sight goes to the eye line at the
        /// distance the gun's own geometry gives; the butt (StockMountLayer.FindButtTop) rests in the right shoulder.
        /// The hold data comes from the first-person original (WorldWeapon, Loadout.MakeWorldModel).
        /// </summary>
        void EquipHeldGun(Transform sock, Transform gun)
        {
            heldGun = gun;
            var ww = gun ? gun.GetComponent<WorldWeapon>() : null;
            tpPistol = ww ? ww.pistol : ca.pistolHold > 0.5f;
            var gripR = gun ? HandGrips.Find(gun, HandGrips.Right) : null;
            var gripL = gun ? HandGrips.Find(gun, HandGrips.Left) : null;
            Transform sight = ww ? ww.aimPoint : null;
            Vector3 hip = ww ? ww.hipPosition : tpPistol ? new Vector3(0.13f, -0.15f, 0.34f) : new Vector3(0.155f, -0.175f, 0.33f);
            float aimDist = 0.21f;
            if (gun && sock && gripR)
            {
                // in the aim frame's units (the root bone carries the model's scale), as the first-person holder measures it
                float s = Mathf.Max(0.01f, core.rig.rootBone.lossyScale.x);
                Quaternion inv = Quaternion.Inverse(sock.rotation);
                Vector3 g = inv * (gripR.position - sock.position) / s;
                hip.z = (tpPistol ? FpPistolGripZ : FpRifleGripZ) - g.z;
                if (tpPistol) hip.y -= FpPistolHipDrop;
                float aimGripZ = tpPistol ? FpPistolAimGripZ : FpRifleGripZ;
                if (sight) aimDist = Mathf.Max(0.08f, aimGripZ + ((inv * (sight.position - sock.position)).z / s - g.z));
            }
            tpSprintPos = ww ? ww.sprintPos : Vector3.zero;
            tpSprintRot = ww ? ww.sprintRot : Vector3.zero;
            if (!gun) tpSprint = 0f;
            look.stanceYaw = gun && !tpPistol ? FpRifleStanceYaw : 0f;
            Vector3 butt = Vector3.zero;
            tpStock = gun && sock && !tpPistol && StockMountLayer.FindButtTop(sock, gun, ww ? ww.muzzle : null, gripR, out butt);
            stockMount.hasStock = tpStock; // (no stock: the layer idles and forgets its smoothing)
            stockMount.buttTop = tpStock ? butt : Vector3.zero;
            stockMount.enabled = true;
            ads.enabled = gun && sight;
            aimTarget.localPosition = new Vector3(0f, 0f, aimDist);
            var settings = new WeaponAnimSettings
            {
                weaponBone = new LocRot(hip, Quaternion.identity),
                aimDistance = aimDist,
                ads = new AdsData(5f) { changeSightSpeed = 30f, pointAimSpeed = 1f },
                gripIK = true,
            };
            core.OnWeaponEquipped(settings, new WeaponTransforms { aimPoint = sight, rightHandTarget = gripR, leftHandTarget = gripL });
            ads.ResetSight();
        }

        /// <summary>
        /// Elbow directions for the arm IK, from the arm itself: below the shoulder → hand line and a little outward,
        /// in the character's frame. The animated elbows (clip + hold pose) turn with the chest, so a leaning / twisted
        /// torso (crouch) swung the trigger elbow up over the shoulder and twisted the wrist around the grip.
        /// </summary>
        void PlaceElbowHints()
        {
            if (core == null || !an) return;
            Transform ch = an.transform;
            Vector3 up = ch.up, right = ch.right, fwd = ch.forward;
            void Hint(IKLimb limb, Vector3 outward)
            {
                if (limb == null || !limb.hint || !limb.upper || !limb.goal) return;
                Vector3 mid = (limb.upper.position + limb.goal.position) * 0.5f;
                limb.hint.position = mid - up * 0.3f + outward;
            }
            Hint(core.rig.rightHand, right * 0.16f - fwd * 0.04f); // trigger arm: elbow down and out
            Hint(core.rig.leftHand, -right * 0.1f);                // support arm: elbow under the handguard
        }

        /// <summary>The socket (and the gun under it) takes the weapon bone's pose; procedural moves (knife / throw) still lower it.
        /// Dead or not holding: the gun stays in the right hand where it was (it falls with the body instead of hanging in the air).</summary>
        void PlaceSocket()
        {
            var sock = rigRef.weaponSocket;
            if (!sock || !heldGun) return;
            var hand = an ? an.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand && (ca.dead > 0.001f || !ca.holdingWeapon))
            {
                if (!gunInHand)
                {
                    gunInHand = true;
                    gunInHandPos = hand.InverseTransformPoint(sock.position);
                    gunInHandRot = Quaternion.Inverse(hand.rotation) * sock.rotation;
                }
                sock.SetPositionAndRotation(hand.TransformPoint(gunInHandPos), hand.rotation * gunInHandRot);
                return;
            }
            gunInHand = false;
            float w = 1f - procW[(int)Group.RArm];
            if (w <= 0.001f) return;
            var wb = core.rig.weaponBone;
            sock.SetPositionAndRotation(Vector3.Lerp(sock.position, wb.position, w), Quaternion.Slerp(sock.rotation, wb.rotation, w));
        }

        void FirstPersonVisibility()
        {
            bool fpView = fpCam.ThirdPerson < 0.5f;
            bool scoped = fpWeapon && fpWeapon.IsScoped;
            int state = (fpView ? 1 : 0) | (scoped ? 2 : 0);
            if (state == visState) return;
            visState = state;
            foreach (var r in headRenderers) if (r) r.shadowCastingMode = fpView ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var r in bodyRenderers) if (r) r.shadowCastingMode = fpView && scoped ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        void CameraShake(float dt)
        {
            if (!fpCam || !fpCam.cam || fpCam.ThirdPerson > 0.001f) return;
            var t = fpCam.cam.transform;
            t.localRotation = adsTilt; // aimed, the eye turned after a gun swayed past the limit (PlaceEyeCamera)
            shaker.Apply(t, dt);
        }

        /// <summary>Procedural poses (executions, knife, abilities) over the animated pose, per bone group.</summary>
        void ApplyOverrides()
        {
            Quaternion br = body.rotation;
            foreach (var m in maps)
            {
                float w = procW[(int)m.group];
                if (w <= 0.001f) continue;
                Quaternion proc = br * (BodyRot(m.rig) * m.correction * m.rest);
                m.model.rotation = w >= 0.999f ? proc : Quaternion.Slerp(m.model.rotation, proc, w);
            }
            float wc = procW[(int)Group.Core];
            if (modelHips && rigRef.hips && wc > 0.001f) modelHips.position = Vector3.Lerp(modelHips.position, rigRef.hips.position, wc);
        }

        /// <summary>The procedural rig follows the model (aim points, executions, hit tests keep using rig bones).</summary>
        void FollowRig()
        {
            Quaternion br = body.rotation;
            var sock = rigRef.weaponSocket;
            Vector3 sockPos = sock ? sock.position : Vector3.zero;
            Quaternion sockRot = sock ? sock.rotation : Quaternion.identity;
            foreach (var m in maps)
            {
                if (!m.follow) continue;
                Quaternion modelBody = BodyRot(m.model);
                m.rig.rotation = br * (modelBody * Quaternion.Inverse(m.rest) * Quaternion.Inverse(m.correction));
            }
            if (modelHips && rigRef.hips) rigRef.hips.position = modelHips.position;
            if (sock) sock.SetPositionAndRotation(sockPos, sockRot); // the chest moved: keep the gun where it was this frame
        }

        /// <summary>Non-animated fallback: the model copies the procedural rig (also used for editor previews).</summary>
        public void Sync()
        {
            if (!rigRef || !body || animated) return;
            Quaternion br = body.rotation;
            foreach (var m in maps)
                m.model.rotation = br * (BodyRot(m.rig) * m.correction * m.rest);
            if (modelHips && rigRef.hips) modelHips.position = rigRef.hips.position;
            if (modelHead && rigRef.head) modelHead.localScale = Vector3.Scale(modelHeadScale, rigRef.head.localScale);
        }

        // ================================================================================================ hit boxes

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
    }
}
