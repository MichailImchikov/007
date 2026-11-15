using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// An offset of the weapon pose set from gameplay every frame (sprint / crouch / slide / vault / lower / reload /
    /// bob / breathing...), applied to the master pivot in the root (camera) frame. KINEMATION's demo did these with
    /// ready poses and animations; here gameplay code computes them and this layer applies them.
    /// </summary>
    public class PoseOffsetLayer : AnimLayer
    {
        [Tooltip("Position (m) and rotation in the root frame, at the weapon pivot")] public LocRot offset = LocRot.Identity;

        public void Set(Vector3 position, Vector3 euler) => offset = new LocRot(position, Quaternion.Euler(euler));

        public override void OnAnimUpdate()
        {
            if (smoothLayerAlpha <= 0f) return;
            AnimMath.MoveInSpace(RootBone, MasterPivot, offset.position, smoothLayerAlpha);
            AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, offset.Rot, smoothLayerAlpha);
        }
    }
}
