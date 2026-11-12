using UnityEngine;

namespace FPS
{
    /// <summary>Put on colliders that should deal damage to an IDamageable in a parent.</summary>
    public class Hitbox : MonoBehaviour
    {
        public float multiplier = 1f;
        public bool critical;

        IDamageable owner;
        public IDamageable Owner => owner ??= GetComponentInParent<IDamageable>();
    }
}
