using UnityEngine;

namespace FPS.Animation
{
    /// <summary>Applies the recoil animation output (CharData.recoilAnim) to the master pivot (KINEMATION's RecoilLayer).</summary>
    public class RecoilLayer : AnimLayer
    {
        [Tooltip("Offsets in the root (camera) frame instead of the weapon's own frame")] public bool useMeshSpace;

        public override void OnAnimUpdate()
        {
            var master = MasterPivot;
            var recoil = CharData.recoilAnim;
            var basePose = new LocRot(master);
            if (useMeshSpace)
            {
                AnimMath.MoveInSpace(RootBone, master, recoil.position, 1f);
                AnimMath.RotateInSpace(RootBone.rotation, master, recoil.Rot, 1f);
            }
            else
            {
                AnimMath.MoveInSpace(master, master, recoil.position, 1f);
                AnimMath.RotateInSpace(master.rotation, master, recoil.Rot, 1f);
            }
            BlendMaster(basePose, smoothLayerAlpha);
        }
    }
}
