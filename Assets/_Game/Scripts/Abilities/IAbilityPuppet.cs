using UnityEngine;

namespace FPS
{
    /// <summary>
    /// A non-player body abilities can push around: pulled by the grappling hook, swapped, stunned by a banana.
    /// Implemented by CombatBot (Deathmatch) and TrainingBot (shooting range).
    /// </summary>
    public interface IAbilityPuppet
    {
        Transform transform { get; }
        /// <summary>While true something else moves the body; on release it settles back on the ground / NavMesh.</summary>
        bool ExternalControl { get; set; }
        void Stun(float seconds);
    }
}
