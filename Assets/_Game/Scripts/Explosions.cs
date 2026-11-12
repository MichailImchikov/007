using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>Shared blast logic (grenades, rockets): FX, sound, falloff damage with line of sight, physics push, camera shake.</summary>
    public static class Explosions
    {
        public static void Detonate(Vector3 pos, float radius, float damage, float push, Combatant attacker, string weapon, Transform ignore = null)
        {
            NetGame.SendExplosion(pos, radius, push, attacker);
            bool onGround = Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, out RaycastHit g, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            FX.Ensure().Explosion(pos, onGround ? g.normal : Vector3.up, onGround);
            ProcAudio.PlayAt(Sfx.Explosion, pos, 1f, 0.06f);
            AIEvents.Emit(pos, 60f, attacker);

            var cols = Physics.OverlapSphere(pos, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            var done = new HashSet<IDamageable>();
            var bodies = new HashSet<Rigidbody>();
            bool anyHit = false, anyKill = false;
            Vector3 origin = pos + Vector3.up * 0.15f;
            foreach (var c in cols)
            {
                if (!c || (ignore && c.transform.IsChildOf(ignore))) continue;
                var hb = c.GetComponent<Hitbox>();
                IDamageable d = hb ? hb.Owner : c.GetComponent<IDamageable>();
                Vector3 cp = c.ClosestPoint(origin);
                float dist = Vector3.Distance(origin, cp);
                if (d != null && !done.Contains(d))
                {
                    bool blocked = Physics.Linecast(origin, cp, out RaycastHit h, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                                   && h.collider != c && h.collider.transform.root != c.transform.root
                                   && !(ignore && h.collider.transform.IsChildOf(ignore))
                                   && Vector3.Distance(h.point, cp) > 0.05f;
                    if (!blocked)
                    {
                        done.Add(d);
                        float f = 1f - Mathf.Clamp01(dist / radius);
                        float amount = Mathf.Round(damage * Mathf.Pow(f, 1.3f));
                        if (amount >= 3f)
                        {
                            Vector3 dir = (cp - pos).sqrMagnitude > 0.0001f ? (cp - pos).normalized : Vector3.up;
                            var res = d.TakeDamage(new DamageInfo { amount = amount, point = cp, normal = -dir, direction = dir, critical = false, attacker = attacker, weapon = weapon });
                            bool byPlayer = attacker && attacker.isPlayer;
                            if (res.applied && byPlayer && !(d is PlayerHealth)) { anyHit = true; anyKill |= res.killed; if (HUD.Instance) HUD.Instance.RegisterHit(res.killed); }
                        }
                    }
                }
                var rb = c.attachedRigidbody;
                if (rb && !rb.isKinematic && bodies.Add(rb))
                    rb.AddExplosionForce(push, pos, radius, 0.8f, ForceMode.VelocityChange);
            }
            if (anyHit && HUD.Instance)
            {
                HUD.Instance.ShowHitmarker(anyKill, false);
                ProcAudio.Play(anyKill ? Sfx.Kill : Sfx.Hit, 0.5f);
            }

            ShakeCamera(pos);
        }

        /// <summary>Explosion that happened on another machine: FX, sound, physics push and shake — the damage was dealt there.</summary>
        public static void Visual(Vector3 pos, float radius, float push, Combatant attacker)
        {
            bool onGround = Physics.Raycast(pos + Vector3.up * 0.2f, Vector3.down, out RaycastHit g, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            FX.Ensure().Explosion(pos, onGround ? g.normal : Vector3.up, onGround);
            ProcAudio.PlayAt(Sfx.Explosion, pos, 1f, 0.06f);
            if (Net.IsHost) AIEvents.Emit(pos, 60f, attacker);
            var bodies = new HashSet<Rigidbody>();
            foreach (var c in Physics.OverlapSphere(pos, radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var rb = c ? c.attachedRigidbody : null;
                if (rb && !rb.isKinematic && bodies.Add(rb)) rb.AddExplosionForce(push, pos, radius, 0.8f, ForceMode.VelocityChange);
            }
            ShakeCamera(pos);
        }

        static void ShakeCamera(Vector3 pos)
        {
            var pc = PlayerHealth.Local ? PlayerHealth.Local.playerCam : Object.FindAnyObjectByType<PlayerCamera>();
            if (pc)
            {
                float d = Vector3.Distance(pc.transform.position, pos);
                pc.Shake(Mathf.Clamp01(1.1f - d / 22f));
                if (d < 12f) pc.Punch(new Vector3(-30f * (1f - d / 12f), Random.Range(-10f, 10f), Random.Range(-15f, 15f)));
            }
        }
    }
}
