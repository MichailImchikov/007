using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// A procedural animation layer (KINEMATION's AnimLayer). Layers run in list order every frame, after the Animator:
    ///   OnPreAnimUpdate (weights) → OnAnimUpdate (move the master pivot / IK goals / bones) → IK → OnPostIK.
    /// Most layers work on the master pivot: the weapon and both hand goals are attached to it, so moving it moves
    /// the gun and the hands together. Derive from this class to add your own layers.
    /// (No [DisallowMultipleComponent] here: on a base class Unity applies it to every subtype, so a second layer of
    /// any kind could not be added to the animator object.)
    /// </summary>
    public abstract class AnimLayer : MonoBehaviour
    {
        [Header("Layer Blending")]
        [Tooltip("Animator float parameter / slot curve that drives the weight (empty = Layer Alpha)")]
        public string curveName;
        [Range(0f, 1f)] public float layerAlpha = 1f;
        [Tooltip("Blend speed of the weight (1/s), 0 = instant")] public float lerpSpeed;
        [Tooltip("How much this layer's master pivot motion also moves the elbow hints")]
        [Range(0f, 1f)] public float elbowsWeight;

        protected float smoothLayerAlpha;
        protected FPSAnimator core;

        public void SetLayerAlpha(float weight) => layerAlpha = Mathf.Clamp01(weight);
        public float GetLayerAlpha() => smoothLayerAlpha;
        public FPSAnimator Core => core;

        internal void Bind(FPSAnimator c) => core = c;

        /// <summary>Once, when the animator starts (after the rig is set up).</summary>
        public virtual void OnAnimStart() { }

        /// <summary>Before the main update: weights / smoothing.</summary>
        public virtual void OnPreAnimUpdate()
        {
            float target = string.IsNullOrEmpty(curveName) ? layerAlpha : GetCurveValue(curveName) * layerAlpha;
            smoothLayerAlpha = lerpSpeed <= 0f ? Mathf.Clamp01(target) : AnimMath.Damp(smoothLayerAlpha, Mathf.Clamp01(target), lerpSpeed, DeltaTime);
        }

        /// <summary>Main update.</summary>
        public virtual void OnAnimUpdate() { }

        /// <summary>After the IK solve.</summary>
        public virtual void OnPostIK() { }

        /// <summary>A new weapon / overlay pose was applied (cache per-weapon data here).</summary>
        public virtual void OnPoseSampled() { }

        // ------------------------------------------------------------------ accessors

        protected float DeltaTime => core ? core.DeltaTime : Time.deltaTime;
        protected AnimRig Rig => core.rig;
        protected Transform RootBone => core.rig.rootBone;
        protected Transform MasterPivot => core.rig.masterIK;
        protected Transform Pelvis => core.rig.pelvis;
        protected Animator Animator => core.rig.animator;
        protected AnimGraph Graph => core.Graph;
        protected CharAnimData CharData => core.characterData;
        protected WeaponAnimSettings Weapon => core.Weapon;
        protected WeaponTransforms Transforms => core.weaponTransforms;
        protected Transform RightHandGoal => core.rig.rightHand.goal;
        protected Transform LeftHandGoal => core.rig.leftHand.goal;
        protected Transform RightFootGoal => core.rig.rightFoot.goal;
        protected Transform LeftFootGoal => core.rig.leftFoot.goal;
        protected Transform PivotPoint => core.weaponTransforms.pivotPoint;
        protected Transform AimPoint => core.weaponTransforms.aimPoint;
        protected Vector3 PivotOffset => core.weaponTransforms.pivotPoint ? core.weaponTransforms.pivotPoint.localPosition : Vector3.zero;

        protected float GetCurveValue(string curve) => core && core.Graph != null ? core.Graph.GetCurveValue(curve) : 0f;

        /// <summary>Moves the master pivot without moving the hand goals (weapons with several pivots).</summary>
        protected void OffsetMasterPivot(LocRot offset)
        {
            var r = new LocRot(RightHandGoal);
            var l = new LocRot(LeftHandGoal);
            AnimMath.MoveInSpace(MasterPivot, MasterPivot, offset.position, 1f);
            AnimMath.RotateInSpace(MasterPivot.rotation, MasterPivot, offset.Rot, 1f);
            r.ApplyWorld(RightHandGoal);
            l.ApplyWorld(LeftHandGoal);
        }

        /// <summary>Blends the master pivot from a cached pose to its current pose by the layer weight.</summary>
        protected void BlendMaster(LocRot before, float alpha)
        {
            if (alpha >= 0.9999f) return;
            var after = new LocRot(MasterPivot);
            LocRot.Lerp(before, after, alpha).ApplyWorld(MasterPivot);
        }
    }
}
