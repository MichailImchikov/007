using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// True first person: keeps the eyes of the body on the camera pivot (the target, or the root bone) by moving the
    /// pelvis, so looking down shows the body and legs without the camera ending up inside the neck, and the shoulders
    /// stay where the arms can reach the weapon. The feet keep their goals (the legs adapt through the leg IK). What
    /// the limits leave (standing the body is never lifted) is closed by the camera itself coming down to the eyes.
    /// Also used for third-person bodies with a virtual camera (bots), so first and third person match.
    /// </summary>
    public class BodyAlignLayer : AnimLayer
    {
        [Tooltip("Eye point relative to the head bone, in the character's upright frame (x right, y up, z forward)")]
        public Vector3 eyeOffset = new Vector3(0f, 0.085f, 0.105f);
        [Tooltip("Where the eyes go (null = the root bone). True first person: the gameplay camera pivot, while the root " +
                 "bone (the aim frame) rides on the body's eyes")]
        public Transform target;
        [Tooltip("Where the eyes should be relative to the target (camera space): a little behind the lens")]
        public Vector3 cameraOffset = new Vector3(0f, -0.02f, -0.06f);
        [Tooltip("Max horizontal correction (m)")] public float maxHorizontal = 0.4f;
        [Tooltip("Max downward correction (m)")] public float maxVertical = 0.3f;
        [Tooltip("Max upward correction (m): lifting the pelvis makes the body float (prone); raise the eyes with the pose instead")]
        public float maxLift = 0.08f;
        [Range(0f, 1f)] public float horizontalWeight = 1f;
        [Range(0f, 1f)] public float verticalWeight = 1f;
        [Tooltip("Smoothing of the correction (1/s, in the character's space), 0 = none. The head bobs and sways with " +
                 "every step: following it frame by frame moved the whole body (feet included) against the bob")]
        public float smoothSpeed = 0f;
        [Tooltip("Pelvis lowered after the alignment (m): the legs bend that much through the leg IK (a ready stance)")]
        public float pelvisDrop;

        Vector3 eyeLocal;
        bool calibrated;
        Vector3 smoothLocal;

        public Vector3 EyePosition => Rig.head ? Rig.head.TransformPoint(eyeLocal) : RootBone.position;
        public Vector3 LastCorrection => Animator ? Animator.transform.rotation * smoothLocal : smoothLocal;

        /// <summary>Call in the rest pose (character upright, facing its forward).</summary>
        public void Calibrate()
        {
            var head = Rig.head;
            if (!head || !Animator) return;
            Transform ch = Animator.transform;
            Vector3 eyeW = head.position + ch.rotation * eyeOffset;
            eyeLocal = head.InverseTransformPoint(eyeW);
            calibrated = true;
        }

        public override void OnAnimStart() { if (!calibrated) Calibrate(); }

        public override void OnAnimUpdate()
        {
            if (!Rig.head || !Pelvis || smoothLayerAlpha <= 0f) { smoothLocal = Vector3.zero; return; }
            Transform ch = Animator.transform;
            Transform t = target ? target : RootBone;
            Vector3 delta = t.position + t.rotation * cameraOffset - EyePosition;
            Vector3 up = ch.up;
            float v = Mathf.Clamp(Vector3.Dot(delta, up), -maxVertical, maxLift) * verticalWeight;
            Vector3 h = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(delta, up), maxHorizontal) * horizontalWeight;
            Vector3 d = (h + up * v) * smoothLayerAlpha;
            // smoothed in the character's space, so turning does not swing the body out
            Vector3 dLocal = Quaternion.Inverse(ch.rotation) * d;
            smoothLocal = smoothSpeed > 0f ? AnimMath.Damp(smoothLocal, dLocal, smoothSpeed, DeltaTime) : dLocal;
            Pelvis.position += ch.rotation * smoothLocal - up * (pelvisDrop * smoothLayerAlpha);
        }
    }
}
