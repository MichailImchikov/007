using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Core of the procedural animation framework (our replacement for KINEMATION's CoreAnimComponent).
    ///
    /// Per frame, after the Animator evaluated the pose (LateUpdate, or Evaluate() from a controller):
    ///   1. foot goals ← feet, weapon bone ← base hold pose (root bone space), master pivot ← weapon bone (+ pivot)
    ///   2. hand goals ← the animated hands, or the weapon's grip anchors (grip IK: hands bound to the weapon model)
    ///   3. layers: OnPreAnimUpdate → OnAnimUpdate (they move the master pivot, goals, bones)
    ///   4. two-bone IK (hands, feet), twist correction of the forearms, layers OnPostIK
    ///   5. the weapon bone is aligned to the final master pivot (the gun follows the hands' frame)
    ///   6. PostAnimUpdate event (camera shake, attachments...)
    /// The root bone is the aim frame: the camera for a first-person body, the eyes for others.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class FPSAnimator : MonoBehaviour
    {
        public AnimRig rig = new AnimRig();
        [Tooltip("Evaluate in LateUpdate (turn off when a controller calls Evaluate itself)")] public bool autoUpdate = true;
        public bool useIK = true;
        [Range(0f, 1f)] public float handIkWeight = 1f;
        [Range(0f, 1f)] public float legIkWeight = 1f;
        [Range(0f, 1f)] public float twistCorrection = 1f;
        [Tooltip("Rotate the spine root by the slot animation's spine rotation")] public bool applySpineOffset = true;
        public bool drawDebug;
        [SerializeField] List<AnimLayer> layers = new List<AnimLayer>();

        [NonSerialized] public CharAnimData characterData;
        [NonSerialized] public WeaponTransforms weaponTransforms;
        /// <summary>
        /// The transform the weapon model hangs on (null = the rig's weapon bone). When it is another transform
        /// (e.g. a gameplay weapon socket), the controller copies the weapon bone pose onto it after Evaluate.
        /// </summary>
        [NonSerialized] public Transform weaponRoot;

        public AnimGraph Graph { get; private set; }
        public WeaponAnimSettings Weapon => weapon ??= new WeaponAnimSettings();
        public float DeltaTime { get; private set; }
        public bool Ready { get; private set; }
        public IReadOnlyList<AnimLayer> Layers => layers;

        /// <summary>0 = hand on the animated pose, 1 = on the weapon's grip anchor (multiplied by the Mask*Hand curves).</summary>
        public float RightGripWeight { get; set; } = 1f;
        public float LeftGripWeight { get; set; } = 1f;

        /// <summary>After the weapon bone / goals are set, before the layers.</summary>
        public event Action PreLayers;
        /// <summary>After the layers, before the IK solve.</summary>
        public event Action PreIK;
        /// <summary>The pose is final (camera shake, attachments, followers).</summary>
        public event Action PostAnimUpdate;

        WeaponAnimSettings weapon;
        Vector2 rightHandWeight = Vector2.one, leftHandWeight = Vector2.one, rightFootWeight = Vector2.one, leftFootWeight = Vector2.one;
        LocRot rightGripRel, leftGripRel;
        bool haveRightGrip, haveLeftGrip;

        // ------------------------------------------------------------------ setup

        /// <summary>
        /// Builds the rig on a humanoid and the playable graph (controller may be null: the pose then comes from
        /// overlay poses only). owner: object the root bone and foot goals live under.
        /// </summary>
        public bool Initialize(Animator animator, Transform owner, RuntimeAnimatorController controller, AvatarMask upperBodyMask)
        {
            if (!rig.Setup(animator, owner ? owner : transform)) { Debug.LogWarning("[FPSAnimator] rig setup failed on " + animator); return false; }
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Graph?.Destroy();
            Graph = AnimGraph.Create(animator, controller, upperBodyMask);
            foreach (var l in layers) if (l) { l.Bind(this); l.OnAnimStart(); }
            Ready = true;
            return true;
        }

        public T AddLayer<T>() where T : AnimLayer
        {
            var l = gameObject.AddComponent<T>();
            AddLayer(l);
            return l;
        }

        public void AddLayer(AnimLayer layer)
        {
            if (!layer || layers.Contains(layer)) return;
            layers.Add(layer);
            layer.Bind(this);
            if (Ready) layer.OnAnimStart();
        }

        public void InsertLayer(int index, AnimLayer layer)
        {
            if (!layer || layers.Contains(layer)) return;
            layers.Insert(Mathf.Clamp(index, 0, layers.Count), layer);
            layer.Bind(this);
            if (Ready) layer.OnAnimStart();
        }

        public void RemoveLayer(AnimLayer layer)
        {
            if (!layer) return;
            layers.Remove(layer);
            Destroy(layer);
        }

        public T GetLayer<T>() where T : AnimLayer
        {
            foreach (var l in layers) if (l is T t) return t;
            return null;
        }

        void OnDestroy()
        {
            Graph?.Destroy();
            Graph = null;
            PreLayers = PreIK = PostAnimUpdate = null;
        }

        // ------------------------------------------------------------------ weapon

        /// <summary>A weapon was equipped: its data, its anchors and the overlay pose.</summary>
        public void OnWeaponEquipped(WeaponAnimSettings settings, WeaponTransforms transforms)
        {
            weapon = settings ?? new WeaponAnimSettings();
            weaponTransforms = transforms;
            rig.weaponTransform = weapon.weaponBone;
            if (!Ready) return;
            rig.RetargetWeaponBone();
            if (weapon.overlayPose && Graph != null) Graph.PlayPose(weapon.overlayPose);
            rig.RetargetHandBones();
            foreach (var l in layers) if (l && l.enabled) l.OnPoseSampled();
        }

        public void OnSightChanged(Transform aimPoint) => weaponTransforms.aimPoint = aimPoint;
        public void SetCharData(CharAnimData data) => characterData = data;

        public void SetRightHandIKWeight(float effector, float hint) => rightHandWeight = new Vector2(effector, hint);
        public void SetLeftHandIKWeight(float effector, float hint) => leftHandWeight = new Vector2(effector, hint);
        public void SetRightFootIKWeight(float effector, float hint) => rightFootWeight = new Vector2(effector, hint);
        public void SetLeftFootIKWeight(float effector, float hint) => leftFootWeight = new Vector2(effector, hint);

        // ------------------------------------------------------------------ update

        void Update()
        {
            Graph?.Update(Time.deltaTime);
        }

        void LateUpdate()
        {
            if (autoUpdate) Evaluate(Time.deltaTime);
        }

        public void Evaluate(float dt)
        {
            if (!Ready || !rig.Valid) return;
            DeltaTime = dt;

            rig.rightFoot.RetargetGoal(); rig.leftFoot.RetargetGoal();
            rig.rightFoot.RetargetHint(); rig.leftFoot.RetargetHint();
            UpdateWeaponBone();
            PreLayers?.Invoke();

            foreach (var l in layers) if (l && l.enabled) l.OnPreAnimUpdate();
            UpdateLayers();
            PreIK?.Invoke();
            ApplyIK();
            foreach (var l in layers) if (l && l.enabled) l.OnPostIK();
            if (twistCorrection > 0f) { rig.rightTwist.Apply(twistCorrection); rig.leftTwist.Apply(twistCorrection); }

            AlignWeaponBone();
            PostAnimUpdate?.Invoke();
        }

        void UpdateWeaponBone()
        {
            rig.RetargetWeaponBone();
            if (applySpineOffset && Graph != null && rig.spineRoot)
                AnimMath.RotateInSpace(rig.rootBone.rotation, rig.spineRoot, Graph.SpineOffset, 1f);

            // grip anchors relative to the weapon (the weapon hangs on the weapon bone, which is at the base pose now)
            haveRightGrip = GripRelative(weaponTransforms.rightHandTarget, out rightGripRel);
            haveLeftGrip = GripRelative(weaponTransforms.leftHandTarget, out leftGripRel);

            var master = rig.masterIK;
            master.SetPositionAndRotation(rig.weaponBone.position, rig.weaponBone.rotation);
            rig.UpdateWeaponParent();
            AnimMath.RotateInSpace(master.rotation, master, Quaternion.Euler(Weapon.rotationOffset), 1f);
            AnimMath.MoveInSpace(master, master, PivotOffset, 1f);

            RetargetHandGoals();
        }

        Vector3 PivotOffset => weaponTransforms.pivotPoint ? weaponTransforms.pivotPoint.localPosition : Vector3.zero;

        bool GripRelative(Transform grip, out LocRot rel)
        {
            rel = LocRot.Identity;
            if (!grip || !grip.gameObject.activeInHierarchy) return false;
            var wb = weaponRoot ? weaponRoot : rig.weaponBone;
            Quaternion inv = Quaternion.Inverse(wb.rotation);
            rel = new LocRot(inv * (grip.position - wb.position), inv * grip.rotation);
            return true;
        }

        void RetargetHandGoals()
        {
            rig.rightHand.RetargetGoal();
            rig.leftHand.RetargetGoal();
            if (!Weapon.gripIK) return;

            // where the weapon root will be at the end of the frame (before the layers' offsets): master minus the pivot
            var master = rig.masterIK;
            Vector3 weaponPos = master.position - master.rotation * PivotOffset;
            Quaternion weaponRot = master.rotation;
            float maskR = Graph != null ? Graph.GetCurveValue(AnimCurves.MaskRightHand) : 0f;
            float maskL = Graph != null ? Graph.GetCurveValue(AnimCurves.MaskLeftHand) : 0f;

            void Place(IKLimb limb, PalmSolver palm, bool have, LocRot rel, float w)
            {
                if (!have || palm == null || !palm.Ready || w <= 0.001f) return;
                Vector3 gp = weaponPos + weaponRot * rel.position;
                Quaternion gr = weaponRot * rel.Rot;
                LocRot wrist = palm.WristPose(gp, gr);
                if (w < 0.999f) wrist = LocRot.Lerp(new LocRot(limb.goal), wrist, w);
                wrist.ApplyWorld(limb.goal);
            }
            Place(rig.rightHand, rig.rightPalm, haveRightGrip, rightGripRel, RightGripWeight * (1f - maskR));
            Place(rig.leftHand, rig.leftPalm, haveLeftGrip, leftGripRel, LeftGripWeight * (1f - maskL));
        }

        void UpdateLayers()
        {
            Transform rh = rig.rightHand.hint, lh = rig.leftHand.hint;
            bool hints = rh && lh;
            if (hints)
            {
                Quaternion rr = rig.rootBone.rotation;
                rig.rightHand.RetargetHint();
                rig.leftHand.RetargetHint();
                rh.position += rr * Weapon.rightElbowHint.normalized * 0.12f;
                lh.position += rr * Weapon.leftElbowHint.normalized * 0.12f;
            }
            foreach (var layer in layers)
            {
                if (!layer || !layer.enabled) continue;
                LocRot cr = hints ? new LocRot(rh) : default, cl = hints ? new LocRot(lh) : default;
                layer.OnAnimUpdate();
                if (hints && layer.elbowsWeight < 0.999f)
                {
                    LocRot.Lerp(cr, new LocRot(rh), layer.elbowsWeight).ApplyWorld(rh);
                    LocRot.Lerp(cl, new LocRot(lh), layer.elbowsWeight).ApplyWorld(lh);
                }
            }
        }

        void ApplyIK()
        {
            if (!useIK) return;
            void Solve(IKLimb limb, Vector2 w, float global)
            {
                if (global <= 0f || !limb.Valid) return;
                limb.positionWeight = w.x; limb.rotationWeight = w.x; limb.hintWeight = w.y;
                limb.Solve(global);
            }
            Solve(rig.rightHand, rightHandWeight, handIkWeight);
            Solve(rig.leftHand, leftHandWeight, handIkWeight);
            Solve(rig.rightFoot, rightFootWeight, legIkWeight);
            Solve(rig.leftFoot, leftFootWeight, legIkWeight);
        }

        void AlignWeaponBone()
        {
            var master = rig.masterIK;
            AnimMath.MoveInSpace(master, master, -PivotOffset, 1f);
            rig.weaponBone.SetPositionAndRotation(master.position, master.rotation);
        }

        // ------------------------------------------------------------------ debug

        void OnDrawGizmos()
        {
            if (!drawDebug || !Ready) return;
            void Dot(Transform t, Color c, float r = 0.025f) { if (!t) return; Gizmos.color = c; Gizmos.DrawWireSphere(t.position, r); }
            Dot(rig.rightHand.goal, Color.green); Dot(rig.leftHand.goal, Color.green);
            Dot(rig.rightHand.hint, Color.yellow, 0.015f); Dot(rig.leftHand.hint, Color.yellow, 0.015f);
            Dot(rig.rightFoot.goal, Color.cyan); Dot(rig.leftFoot.goal, Color.cyan);
            Dot(rig.masterIK, Color.magenta, 0.035f);
            if (rig.rootBone)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawWireCube(rig.rootBone.position, Vector3.one * 0.05f);
                Gizmos.DrawLine(rig.rootBone.position, rig.rootBone.position + rig.rootBone.forward * 0.5f);
            }
        }
    }
}
