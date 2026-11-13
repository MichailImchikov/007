using System.Collections.Generic;
using Kinemation.FPSFramework.Runtime.Camera;
using Kinemation.FPSFramework.Runtime.Core.Components;
using Kinemation.FPSFramework.Runtime.Core.Types;
using Kinemation.FPSFramework.Runtime.Layers;
using Kinemation.FPSFramework.Runtime.Recoil;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// First-person arms on the KINEMATION FPS Animation Framework.
    ///
    /// The player's own character model (CharacterLook.Local) is instantiated under the camera with the FP hold-pose
    /// Animator (JU TPS 3 clips) and a CoreAnimComponent with the framework's layers:
    ///   FPPoseLayer (our gameplay pose: sprint, lower, reload, bob, bumps) → FPAdsLayer (sight on the eye line) →
    ///   SwayLayer (look + move sway springs) → RecoilLayer (RecoilAnimation curves) → WeaponCollision (walls).
    /// The weapons hang on the framework's weapon bone; after its update both hands are IK'd onto the gun's
    /// grip anchors (ArmIK — explicit humanoid bones, so twist bones are fine). FPSCamera adds the per-shot shake.
    /// Gameplay (firing, spread, reload timing, camera recoil) stays in WeaponController / PlayerCamera.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class FPArms : MonoBehaviour
    {
        public Loadout loadout;
        public PlayerCamera playerCam;
        public PlayerMotor motor;

        CoreAnimComponent core;
        RecoilAnimation recoil;
        FPAdsLayer ads;
        FPPoseLayer poseLayer;
        FPSCamera fpsCam;
        Animator an;
        Transform cam, rootBone, weaponBone, aimTarget;
        ArmIK lArm, rArm;
        Renderer[] renderers;
        WeaponController current;
        bool firing, visible = true;
        CharAnimData charData;
        readonly Dictionary<WeaponController, RecoilAnimData> recoilData = new Dictionary<WeaponController, RecoilAnimData>();
        readonly Dictionary<WeaponController, Transform> pivots = new Dictionary<WeaponController, Transform>();
        static readonly int pPistol = Animator.StringToHash("Pistol");

        public WeaponController Current => current;

        /// <summary>FP model scale (only the arms are seen — longer arms reach the foregrip of long guns).</summary>
        const float ArmScale = 1.3f;
        /// <summary>Where the shoulders' midpoint sits in camera space (low and back enough to keep the torso out of view).</summary>
        static readonly Vector3 ShoulderPos = new Vector3(0f, -0.29f, -0.02f);

        // ------------------------------------------------------------------ build

        public static FPArms Create(Loadout loadout, PlayerCamera pc, PlayerMotor motor)
        {
            var db = CharacterModelDB.Instance;
            if (!db || !db.fpController || !loadout || !pc || !pc.cam) return null;
            int model = CharacterLook.Local != null ? CharacterLook.Local.model : 0;
            var e = CharacterModelDB.Get(model) ?? CharacterModelDB.Get(1);
            if (e == null) return null;

            var inst = Instantiate(e.prefab);
            inst.name = "FPArms_" + e.prefab.name;
            var an = inst.GetComponentInChildren<Animator>();
            if (!an || !an.avatar || !an.avatar.isHuman) { Destroy(inst); return null; }
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is FPArms)) Destroy(mb);

            var arms = an.gameObject.AddComponent<FPArms>();
            arms.loadout = loadout; arms.playerCam = pc; arms.motor = motor;
            arms.Build(inst.transform, an, e);
            return arms;
        }

        void Build(Transform instRoot, Animator animator, CharacterModelDB.Entry e)
        {
            an = animator;
            cam = playerCam.cam.transform;
            instRoot.SetParent(cam, false);
            instRoot.localRotation = Quaternion.identity;
            instRoot.localScale = Vector3.one * ArmScale;

            var rs = new List<Renderer>();
            foreach (var r in instRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (e.skin) { var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = e.skin; r.sharedMaterials = mats; }
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true;
                rs.Add(r);
            }
            renderers = rs.ToArray();
            foreach (var lg in instRoot.GetComponentsInChildren<LODGroup>(true)) { lg.ForceLOD(0); }

            an.runtimeAnimatorController = CharacterModelDB.Instance.fpController;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            an.Update(0f);

            // place the body so its shoulders sit just below / ahead of the eye (only the arms are in view)
            Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
            Vector3 sh = (B(HumanBodyBones.LeftUpperArm).position + B(HumanBodyBones.RightUpperArm).position) * 0.5f;
            instRoot.position += cam.TransformPoint(ShoulderPos) - sh;
            // head / neck / legs are never seen from inside: collapse them
            var neck = B(HumanBodyBones.Neck) ? B(HumanBodyBones.Neck) : B(HumanBodyBones.Head);
            if (neck) neck.localScale = Vector3.zero;
            foreach (var leg in new[] { B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.RightUpperLeg) }) if (leg) leg.localScale = Vector3.zero;

            // ---- framework rig: root bone = camera frame (unit scale), weapon bone, master IK, hand / elbow IK objects
            rootBone = new GameObject("rootBone").transform;
            rootBone.SetParent(transform, false);
            rootBone.position = cam.position; rootBone.rotation = cam.rotation;
            rootBone.localScale = Vector3.one / Mathf.Max(0.01f, transform.lossyScale.x);
            weaponBone = new GameObject("WeaponBone").transform;
            weaponBone.SetParent(rootBone, false);
            aimTarget = new GameObject("AimTarget").transform;
            aimTarget.SetParent(rootBone, false);
            var master = new GameObject("MasterIK").transform;
            master.SetParent(rootBone, false);
            GameObject Obj(string n) { var g = new GameObject(n); g.transform.SetParent(master, false); return g; }
            var rHand = B(HumanBodyBones.RightHand); var lHand = B(HumanBodyBones.LeftHand);
            Transform Child(Transform p, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); return t; }

            core = gameObject.AddComponent<CoreAnimComponent>();
            // the framework's own two-bone IK takes hand.parent as the forearm (breaks on twist bones): ArmIK does the hands
            JsonUtility.FromJsonOverwrite("{\"useIK\":false,\"handIkWeight\":0.0,\"legIkWeight\":0.0}", core);
            core.ikRigData.animator = an;
            core.ikRigData.pelvis = B(HumanBodyBones.Hips);
            core.ikRigData.spineRoot = B(HumanBodyBones.Spine);
            core.ikRigData.rootBone = rootBone;
            core.ikRigData.weaponBone = weaponBone;
            core.ikRigData.weaponBoneRight = Child(rHand, "WeaponBoneRight");
            core.ikRigData.weaponBoneLeft = Child(lHand, "WeaponBoneLeft");
            core.ikRigData.masterDynamic = new DynamicBone { obj = master.gameObject, target = weaponBone };
            core.ikRigData.rightHand = new DynamicBone { target = rHand, hintTarget = B(HumanBodyBones.RightLowerArm), obj = Obj("RightHandIK"), hintObj = Obj("RightElbowIK") };
            core.ikRigData.leftHand = new DynamicBone { target = lHand, hintTarget = B(HumanBodyBones.LeftLowerArm), obj = Obj("LeftHandIK"), hintObj = Obj("LeftElbowIK") };
            // the weapon follows the weapon bone (our hip pose), not the clip's right hand
            core.ikRigData.weaponBoneWeight = 1f;

            poseLayer = gameObject.AddComponent<FPPoseLayer>();
            poseLayer.arms = this;
            ads = gameObject.AddComponent<FPAdsLayer>();
            ads.Configure(aimTarget);
            var sway = gameObject.AddComponent<SwayLayer>();
            gameObject.AddComponent<RecoilLayer>();
            var collision = gameObject.AddComponent<WeaponCollision>();
            int mask = Physics.DefaultRaycastLayers & ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
            JsonUtility.FromJsonOverwrite("{\"layerMask\":{\"m_Bits\":" + mask + "}}", collision);
            foreach (var l in new AnimLayer[] { poseLayer, ads, sway, GetComponent<RecoilLayer>(), collision }) core.AddLayer(l);

            recoil = gameObject.AddComponent<RecoilAnimation>();
            fpsCam = cam.GetComponent<FPSCamera>();
            if (!fpsCam) fpsCam = cam.gameObject.AddComponent<FPSCamera>();

            lArm = ArmIK.Make(an, true);
            rArm = ArmIK.Make(an, false);

            core.InitializeLayers();
            core.OnPostAnimUpdate += AfterAnimUpdate;

            // the weapons hang on the weapon bone; every WeaponController switches to framework-driven posing
            foreach (var w in loadout.weapons)
            {
                if (!w) continue;
                w.EnsureInit();
                w.AnimDriven = true;
                w.transform.SetParent(weaponBone, false);
                w.transform.localPosition = Vector3.zero;
                w.transform.localRotation = Quaternion.identity;
                HideProceduralArms(w);
                w.Fired += () => OnFired(w);
            }
            loadout.ActiveChanged += Equip;
            if (loadout.Active) Equip(loadout.Active);
        }

        static readonly string[] ArmParts = { "Glove", "Finger", "Thumb", "Cuff", "Forearm", "UpperArm" };

        static void HideProceduralArms(WeaponController w)
        {
            foreach (var r in w.model.GetComponentsInChildren<Renderer>(true))
                foreach (var p in ArmParts)
                    if (r.name.Contains(p)) { r.forceRenderingOff = true; if (!r.GetComponent<ImportHidden>()) r.gameObject.AddComponent<ImportHidden>(); break; }
        }

        // ------------------------------------------------------------------ weapon data

        Transform Pivot(WeaponController w)
        {
            if (pivots.TryGetValue(w, out var p) && p) return p;
            p = new GameObject("FP_Pivot").transform;
            p.SetParent(w.transform, false);
            var grip = w.model.Find(HandGrips.Right);
            // rotations pivot a little ahead of the trigger hand (center of the gun's mass)
            p.localPosition = grip ? w.transform.InverseTransformPoint(grip.position) + new Vector3(0f, 0.02f, 0.06f) : Vector3.zero;
            pivots[w] = p;
            return p;
        }

        RecoilAnimData Recoil(WeaponController w)
        {
            if (recoilData.TryGetValue(w, out var d) && d) return d;
            d = ScriptableObject.CreateInstance<RecoilAnimData>();
            d.name = "Recoil_" + w.weaponName;
            bool pistol = w.slot == WeaponSlot.Secondary;
            float k = Mathf.Clamp(w.kickScale, 0.5f, 2.2f);
            float up = Mathf.Clamp(w.recoilUp, 0.3f, 5f);
            d.pitch = new Vector2(-4f - up * 1.6f, -6f - up * 2.2f) * k;               // muzzle climbs (degrees)
            d.yaw = new Vector4(-1.6f, -0.4f, 0.4f, 1.6f) * k;
            d.roll = new Vector4(-4f, -1.5f, 1.5f, 4f) * k;
            d.kickback = new Vector2(-0.035f, -0.05f) * k * (pistol ? 0.7f : 1f);   // into the shoulder (meters)
            d.kickUp = new Vector2(0.002f, 0.007f) * k;
            d.kickRight = new Vector2(-0.004f, 0.004f) * k;
            d.aimRot = new Vector3(0.45f, 0.4f, 0.35f);
            d.aimLoc = new Vector3(0.4f, 0.4f, 0.55f);
            d.smoothRot = new Vector3(18f, 18f, 18f);
            d.smoothLoc = new Vector3(20f, 20f, 20f);
            d.extraRot = Vector3.one; d.extraLoc = Vector3.one;
            d.noiseX = new Vector2(-0.0015f, 0.0015f); d.noiseY = new Vector2(0.0005f, 0.002f);
            d.noiseAccel = new Vector2(12f, 12f); d.noiseDamp = new Vector2(6f, 6f);
            d.noiseScalar = 0.5f;
            d.pushAmount = 0f; d.pushAccel = 10f; d.pushDamp = 8f;
            d.smoothRoll = true;
            d.playRate = 1f;
            float len = Mathf.Clamp(0.12f + 0.06f * k + 0.03f * up, 0.16f, 0.42f);
            Keyframe[] rot = { new Keyframe(0f, 0f), new Keyframe(0.035f, 1f), new Keyframe(len, 0f) };
            Keyframe[] loc = { new Keyframe(0f, 0f), new Keyframe(0.03f, 1f), new Keyframe(len * 0.85f, 0f) };
            d.recoilCurves = new RecoilCurves
            {
                semiRotCurve = new VectorCurve(rot), semiLocCurve = new VectorCurve(loc),
                autoRotCurve = new VectorCurve(rot), autoLocCurve = new VectorCurve(loc),
            };
            recoilData[w] = d;
            return d;
        }

        WeaponAnimData AnimData(WeaponController w)
        {
            var spring = new VectorSpringData(40f, 0.75f, 1f);
            var data = new WeaponAnimData(LocRot.identity)
            {
                leftHandTarget = w.model.Find(HandGrips.Left) ?? HandGrips.Find(w.model, HandGrips.Left),
                rotationOffset = Quaternion.identity,
                gunAimData = new GunAimData(LocRot.identity)
                {
                    pivotPoint = Pivot(w), aimPoint = w.sightPoint,
                    aimSpeed = 1f / Mathf.Max(0.05f, w.AimTime), changeSightSpeed = 30f, pointAimSpeed = 1f,
                },
                springData = new LocRotSpringData
                {
                    loc = new VectorSpringData(40f, 0.75f, 1f) { scale = new Vector3(-1.8f, -1.8f, 0f) },
                    rot = new VectorSpringData(40f, 0.75f, 1f) { scale = new Vector3(140f, -120f, -200f) },
                },
                moveSwayData = new MoveSwayData
                {
                    maxMoveLocSway = new Vector3(-0.01f, 0f, -0.012f),
                    maxMoveRotSway = new Vector3(0f, 0f, -3.5f),
                    moveLocSway = new VectorSpringData(40f, 0.75f, 1f) { scale = Vector3.one },
                    moveRotSway = new VectorSpringData(40f, 0.75f, 1f) { scale = Vector3.one },
                    locSpeed = new Vector3(8f, 8f, 8f), rotSpeed = new Vector3(8f, 8f, 8f),
                },
            };
            // wall collision: trace along the gun from a bit behind the pivot to past the muzzle
            float muzzle = w.muzzle ? w.transform.InverseTransformPoint(w.muzzle.position).z - Pivot(w).localPosition.z : 0.6f;
            data.blockData = new GunBlockData(new LocRot(new Vector3(0.02f, -0.06f, -0.08f), Quaternion.Euler(-35f, -15f, 12f)))
            {
                weaponLength = Mathf.Max(0.25f, muzzle) + 0.12f, startOffset = 0.12f, threshold = 0.22f,
            };
            return data;
        }

        void Equip(WeaponController w)
        {
            if (!w || !core) return;
            current = w;
            var data = AnimData(w);
            core.OnGunEquipped(data);
            core.ikRigData.weaponTransform = new LocRot(w.hipPosition, Quaternion.identity);
            aimTarget.localPosition = new Vector3(0f, 0f, w.aimDistance);
            recoil.Init(Recoil(w), w.rpm, FireMode.Semi);
            // initialize the sight interpolation (it starts from an invalid zero rotation)
            ads.SetAds(true); ads.SetAds(false);
            an.SetFloat(pPistol, w.slot == WeaponSlot.Secondary ? 1f : 0f);
            an.Update(0f);
            core.OnPrePoseSampled();
            core.OnPoseSampled();
        }

        void OnFired(WeaponController w)
        {
            if (w != current || !recoil) return;
            recoil.isAiming = w.AimBlend > 0.5f;
            recoil.Play();
            firing = true;
            if (fpsCam)
            {
                float k = Mathf.Clamp(w.kickScale * Mathf.Lerp(1f, 0.6f, w.AimBlend), 0.4f, 2.5f);
                var shake = new CameraShakeInfo(new[] { new Keyframe(0f, 0f), new Keyframe(0.04f, 1f), new Keyframe(0.22f, 0f) }, 1f, 22f)
                {
                    pitch = new Vector4(-0.5f, -0.25f, -0.25f, -0.5f) * k,
                    yaw = new Vector4(-0.25f, -0.1f, 0.1f, 0.25f) * k,
                    roll = new Vector4(-0.6f, -0.2f, 0.2f, 0.6f) * k,
                };
                fpsCam.PlayShake(shake);
            }
        }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            if (!core) return;
            var w = loadout ? loadout.Active : null;
            if (w != current && w) Equip(w);

            float dt = Time.deltaTime;
            Vector2 look = playerCam ? playerCam.LastLookDelta : Vector2.zero;
            float aim = w ? w.AimBlend : 0f;
            charData.deltaAimInput = look * Mathf.Lerp(1f, 0.3f, aim);
            charData.moveInput = motor ? motor.MoveInput * Mathf.Lerp(1f, 0.35f, aim) : Vector2.zero;
            charData.recoilAnim = new LocRot(recoil.OutLoc, Quaternion.Euler(recoil.OutRot));
            core.SetCharData(charData);
            core.ikRigData.RetargetWeaponBone();
            if (w) ads.SetAds(w.WantAim);
            if (firing && w && !(w.FullAuto ? GameInput.FireHeld : GameInput.FirePressed)) { recoil.Stop(); firing = false; }

            bool show = w && w.gameObject.activeInHierarchy && !w.IsScoped && playerCam.ThirdPerson < 0.01f && playerCam.DeathAmount < 0.01f;
            if (show != visible)
            {
                visible = show;
                foreach (var r in renderers) if (r) r.forceRenderingOff = !show;
            }
        }

        /// <summary>After the framework placed the weapon bone: hands onto the grips, then the camera shake.</summary>
        void AfterAnimUpdate()
        {
            var w = current;
            if (w && w.gameObject.activeInHierarchy)
            {
                var gr = w.model.Find(HandGrips.Right);
                var gl = HandGrips.Find(w.model, HandGrips.Left);
                Vector3 down = -cam.up, side = cam.right, back = -cam.forward;
                if (rArm != null && gr) rArm.Solve(gr, down + side * 0.7f + back * 0.2f, 1f);
                if (lArm != null && gl)
                {
                    // a long gun's foregrip may be out of reach: slide the hand back along the handguard
                    Vector3 target = gl.position;
                    float reach = lArm.Reach * 0.98f;
                    Vector3 shoulder = lArm.upper.position;
                    for (int i = 0; i < 8 && Vector3.Distance(shoulder, target) > reach; i++) target -= w.model.forward * 0.03f;
                    var rot = lArm.HandRotation(gl);
                    lArm.Solve(lArm.WristFor(target, rot), rot, down - side * 0.8f, 1f);
                }
            }
            if (fpsCam && playerCam && playerCam.ThirdPerson < 0.001f)
            {
                cam.localRotation = Quaternion.identity;
                fpsCam.UpdateCamera();
            }
        }

        void OnDestroy()
        {
            if (loadout) loadout.ActiveChanged -= Equip;
            if (core) core.OnPostAnimUpdate -= AfterAnimUpdate;
            foreach (var d in recoilData.Values) if (d) Destroy(d);
        }
    }

    /// <summary>Our gameplay pose (WeaponController.PoseOffsetPos/Rot, camera space) on the master pivot.</summary>
    public class FPPoseLayer : AnimLayer
    {
        public FPArms arms;

        public override void OnAnimUpdate()
        {
            var w = arms ? arms.Current : null;
            if (!w) return;
            var root = GetRootBone();
            CoreToolkitLib.MoveInBoneSpace(root, GetMasterPivot(), w.PoseOffsetPos, 1f);
            CoreToolkitLib.RotateInBoneSpace(root.rotation, GetMasterPivot(), Quaternion.Euler(w.PoseOffsetRot), 1f);
        }
    }

    /// <summary>The framework's AdsLayer with its aim target set from code (the eye line point at the weapon's ADS distance).</summary>
    public class FPAdsLayer : AdsLayer
    {
        public void Configure(Transform target)
        {
            aimTarget = target;
            // sine-eased aim in/out like WeaponController's own blend; the crouch pose curve is an Animator float that stays 0
            JsonUtility.FromJsonOverwrite("{\"adsEaseMode\":{\"easeFunc\":1},\"pointAimEaseMode\":{\"easeFunc\":1},\"crouchPoseCurve\":\"CrouchPose\"," +
                                          "\"aimLayerAlphaLoc\":0.0,\"aimLayerAlphaRot\":0.0,\"layerAlpha\":1.0}", this);
        }
    }
}
