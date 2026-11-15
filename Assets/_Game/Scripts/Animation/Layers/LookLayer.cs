using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Aim offset of the spine (KINEMATION's LookLayer): the pitch / yaw of the aim and the lean are distributed over
    /// spine bones (each takes a share up to its max angle), so the shoulders, head and arms follow the view.
    /// Pitch: CharData.totalAimInput.y (degrees, + up). Yaw: totalAimInput.x (upper body twist). Lean: CharData.lean.
    /// </summary>
    public class LookLayer : AnimLayer
    {
        [Serializable]
        public struct AimOffsetBone
        {
            public Transform bone;
            [Tooltip("Degrees this bone takes at 90° of aim: x = down / left, y = up / right")] public Vector2 maxAngle;
            public AimOffsetBone(Transform bone, Vector2 maxAngle) { this.bone = bone; this.maxAngle = maxAngle; }
        }

        [Header("Aim offset")]
        public List<AimOffsetBone> lookUpOffset = new List<AimOffsetBone>();
        public List<AimOffsetBone> lookRightOffset = new List<AimOffsetBone>();
        [Tooltip("Bones that take the lean (empty = the look-right chain)")]
        public List<AimOffsetBone> leanOffset = new List<AimOffsetBone>();
        public bool useRightOffset = true;
        [Tooltip("Aim lag (1/s), 0 = none")] public float smoothAim;
        [Tooltip("Scale of the pitch that goes into the spine (1 = the head follows the aim fully)")] public float pitchScale = 1f;

        [Header("Hands")]
        [Tooltip("Hand goals follow the spine rotation (only when they are not in the aim frame)")]
        [Range(0f, 1f)] public float handsLayerAlpha;
        public float handsLerpSpeed;

        [Header("Pelvis")]
        [Range(0f, 1f)] public float pelvisLayerAlpha = 1f;
        public float pelvisLerpSpeed;
        public Vector3 pelvisOffset;

        [Header("Leaning")]
        [Tooltip("Spine roll at full lean (degrees)")] public float leanAmount = 25f;
        public float leanSpeed = 8f;

        [Header("Shooting stance")]
        [Tooltip("Constant twist of the torso to the right (degrees): the bladed rifle stance brings the support " +
                 "shoulder forward so the arm reaches the handguard. The neck turns back, the head keeps facing the aim")]
        public float stanceYaw;
        [Tooltip("Constant arch of the upper body (degrees, + = chest up), e.g. propped on the elbows when prone. " +
                 "The neck bends back down by the same amount, so only the shoulders and head rise")]
        public float stancePitch;
        [Tooltip("Bones that share the stance twist / arch (set by SetupHumanoid: the spine chain)")]
        public List<Transform> stanceBones = new List<Transform>();
        public Transform stanceNeck;
        [Tooltip("Head lowered onto the stock (degrees over neck + head, + = down), e.g. while aiming a rifle")]
        public float headDrop;
        [Tooltip("Head bones for the head drop (set by SetupHumanoid)")]
        public Transform headDropNeck, headDropHead;

        protected Vector2 lerpedAim;
        protected float leanInput, interpPelvis, interpHands;

        /// <summary>Default distribution on a humanoid: most of the pitch in the chest so the shoulders follow the aim.</summary>
        public void SetupHumanoid(AnimRig rig, bool firstPerson)
        {
            lookUpOffset.Clear();
            lookRightOffset.Clear();
            leanOffset.Clear();
            void Up(Transform b, float down, float up) { if (b) lookUpOffset.Add(new AimOffsetBone(b, new Vector2(down, up))); }
            void Right(Transform b, float left, float right)
            {
                if (!b) return;
                lookRightOffset.Add(new AimOffsetBone(b, new Vector2(left, right)));
                leanOffset.Add(new AimOffsetBone(b, new Vector2(left, right)));
            }
            bool upper = rig.upperChest;
            if (firstPerson)
            {
                // the eyes must end exactly on the camera pitch: the sums are 90 (down and up). Looking down most of it is
                // in the neck / head (hidden in first person): a torso that folded forward put the upper back right under
                // the camera. The eyes then move forward as the head bends, so the body slides back from the camera.
                if (upper) { Up(rig.spineRoot, 8f, 15f); Up(rig.chest, 10f, 18f); Up(rig.upperChest, 8f, 17f); }
                else { Up(rig.spineRoot, 10f, 20f); Up(rig.chest, 16f, 30f); }
                Up(rig.neck, 34f, 22f); Up(rig.head, 30f, 18f);
            }
            else
            {
                Up(rig.spineRoot, 18f, 18f); Up(rig.chest, 22f, 22f); if (upper) Up(rig.upperChest, 20f, 20f);
                Up(rig.neck, upper ? 15f : 25f, upper ? 15f : 25f); Up(rig.head, 15f, 15f);
            }
            Right(rig.spineRoot, 25f, 25f); Right(rig.chest, 25f, 25f); if (upper) Right(rig.upperChest, 20f, 20f);
            Right(rig.neck, 12f, 12f); Right(rig.head, 8f, 8f);

            stanceBones.Clear();
            foreach (var b in new[] { rig.spineRoot, rig.chest, rig.upperChest }) if (b) stanceBones.Add(b);
            stanceNeck = rig.neck;
            headDropNeck = rig.neck;
            headDropHead = rig.head;
        }

        public override void OnPreAnimUpdate()
        {
            base.OnPreAnimUpdate();
            float dt = DeltaTime;
            Vector2 aim = CharData.totalAimInput;
            if (!useRightOffset || lookRightOffset.Count == 0) aim.x = 0f;
            leanInput = AnimMath.Damp(leanInput, leanAmount * Mathf.Clamp(CharData.lean, -1f, 1f), leanSpeed, dt);

            interpPelvis = AnimMath.Damp(interpPelvis, pelvisLayerAlpha * smoothLayerAlpha, pelvisLerpSpeed, dt);
            if (pelvisOffset.sqrMagnitude > 0f && Pelvis)
                AnimMath.MoveInSpace(RootBone, Pelvis, pelvisOffset * interpPelvis, 1f);

            lerpedAim.y = smoothAim <= 0f ? aim.y : AnimMath.Damp(lerpedAim.y, aim.y, smoothAim, dt);
            lerpedAim.x = smoothAim <= 0f ? aim.x : AnimMath.Damp(lerpedAim.x, aim.x, smoothAim, dt);
            interpHands = AnimMath.Damp(interpHands, handsLayerAlpha, handsLerpSpeed, dt);
        }

        public override void OnAnimUpdate()
        {
            float alpha = smoothLayerAlpha * (1f - GetCurveValue(AnimCurves.MaskLookLayer));
            if (alpha <= 0.0001f) return;

            var r = new LocRot(RightHandGoal);
            var l = new LocRot(LeftHandGoal);
            Quaternion body = transform.rotation;
            Transform charRoot = Animator ? Animator.transform : transform;
            body = charRoot.rotation;

            // bladed stance: the torso twists right over the spine chain, the neck turns back
            if (Mathf.Abs(stanceYaw) > 0.01f && stanceBones.Count > 0)
            {
                float per = stanceYaw / stanceBones.Count;
                foreach (var b in stanceBones) if (b) AnimMath.RotateInSpace(body, b, Quaternion.Euler(0f, per, 0f), alpha);
                if (stanceNeck) AnimMath.RotateInSpace(body, stanceNeck, Quaternion.Euler(0f, -stanceYaw, 0f), alpha);
            }
            // arch (prone: chest up on the elbows), around the character's right axis
            if (Mathf.Abs(stancePitch) > 0.01f && stanceBones.Count > 0)
            {
                float per = stancePitch / stanceBones.Count;
                foreach (var b in stanceBones) if (b) AnimMath.RotateInSpace(body, b, Quaternion.Euler(-per, 0f, 0f), alpha);
                if (stanceNeck) AnimMath.RotateInSpace(body, stanceNeck, Quaternion.Euler(stancePitch, 0f, 0f), alpha);
            }

            // lean (roll) and yaw over the "right" chain, in the character's space
            if (Mathf.Abs(leanInput) > 0.01f)
                foreach (var b in leanOffset.Count > 0 ? leanOffset : lookRightOffset)
                {
                    if (!b.bone) continue;
                    float share = b.maxAngle.x / 90f;
                    AnimMath.RotateInSpace(body, b.bone, Quaternion.Euler(0f, 0f, -leanInput * share), alpha);
                }
            if (Mathf.Abs(lerpedAim.x) > 0.01f)
                foreach (var b in lookRightOffset)
                {
                    if (!b.bone) continue;
                    float frac = lerpedAim.x >= 0f ? b.maxAngle.y : b.maxAngle.x;
                    AnimMath.RotateInSpace(body, b.bone, Quaternion.Euler(0f, lerpedAim.x * frac / 90f, 0f), alpha);
                }

            // pitch over the "up" chain, around the character's right axis turned by the yaw
            float pitch = lerpedAim.y * pitchScale;
            if (Mathf.Abs(pitch) > 0.01f)
            {
                Quaternion space = body * Quaternion.Euler(0f, lerpedAim.x, 0f);
                foreach (var b in lookUpOffset)
                {
                    if (!b.bone) continue;
                    float frac = pitch >= 0f ? b.maxAngle.y : b.maxAngle.x;
                    AnimMath.RotateInSpace(space, b.bone, Quaternion.Euler(-pitch * frac / 90f, 0f, 0f), alpha);
                }
            }

            // cheek on the stock: the neck and head bend down on top of the aim (the eyes drop, so the body alignment
            // lifts the shoulders toward the gun)
            if (headDrop > 0.01f)
            {
                var q = Quaternion.Euler(headDrop * 0.5f, 0f, 0f);
                if (headDropNeck) AnimMath.RotateInSpace(body, headDropNeck, q, alpha);
                if (headDropHead) AnimMath.RotateInSpace(body, headDropHead, q, alpha);
            }

            // hand goals (children of the aim frame) stay unless asked to follow the spine
            LocRot.Lerp(r, new LocRot(RightHandGoal), interpHands).ApplyWorld(RightHandGoal);
            LocRot.Lerp(l, new LocRot(LeftHandGoal), interpHands).ApplyWorld(LeftHandGoal);
        }
    }
}
