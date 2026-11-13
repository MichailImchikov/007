using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>Sky arena behaviour of a bot: gun away, sprint (x2) at the nearest runner zig-zagging, knife it.</summary>
    public partial class CombatBot
    {
        bool arenaWas, arenaHit;
        float arenaKnifeT = -1f, arenaKnifeCd, arenaRetarget;

        /// <summary>Moved onto / back from the arena by SkyArena.</summary>
        public void ArenaTeleport(Vector3 pos, Quaternion rot)
        {
            if (dead || executing) return;
            external = false;
            stunTimer = 0f;
            YawOverride = null;
            target = null;
            targetVisible = false;
            state = State.Roam;
            transform.rotation = rot;
            if (NavMesh.SamplePosition(pos, out var nh, 3f, NavMesh.AllAreas)) pos = nh.position;
            if (!agent.enabled) agent.enabled = true;
            agent.Warp(pos);
            if (agent.isOnNavMesh) { agent.ResetPath(); agent.isStopped = false; }
            extLastPos = pos;
        }

        /// <summary>True while the arena drives this bot.</summary>
        bool ArenaTick(float dt)
        {
            if (!SkyArena.Active)
            {
                if (arenaWas) ArenaExit();
                return false;
            }
            if (SkyArena.IsGunner(combatant)) return false; // SkyArena shoots for it; the body is under external control
            arenaWas = true;
            crouched = false;
            lookAtOverride = null;
            if (anim) anim.holdingWeapon = false;
            if (!agent.enabled || !agent.isOnNavMesh) return true;

            arenaRetarget -= dt;
            if (arenaRetarget <= 0f || !target || !target.alive || SkyArena.IsGunner(target) || !SkyArena.OnPlane(target.transform.position))
            {
                arenaRetarget = Random.Range(0.4f, 0.8f);
                target = SkyArena.NearestRunner(combatant, transform.position);
            }

            agent.isStopped = false;
            agent.speed = 5.4f * SkyArena.SpeedScale;
            if (target)
            {
                state = State.Engage;
                targetVisible = true;
                lastSeen = Time.time;
                lastKnown = target.Chest;
                Vector3 tp = target.transform.position;
                Vector3 to = tp - transform.position; to.y = 0f;
                float dist = to.magnitude;
                // zig-zag on the way in: the gun in the sky has a harder time
                Vector3 dest = tp;
                if (dist > 4f && dist > 0.01f)
                    dest += Vector3.Cross(Vector3.up, to / dist) * Mathf.Sin(Time.time * 2.6f + combatant.netId * 1.7f) * Mathf.Min(3.5f, dist * 0.4f);
                if (NavMesh.SamplePosition(dest, out var nh, 3f, NavMesh.AllAreas) && (agent.destination - nh.position).sqrMagnitude > 0.2f)
                    agent.SetDestination(nh.position);

                arenaKnifeCd -= dt;
                bool facing = dist < 0.01f || Vector3.Angle(transform.forward, to) < 40f;
                if (arenaKnifeT < 0f && arenaKnifeCd <= 0f && dist < 1.9f && facing)
                {
                    arenaKnifeT = 0f;
                    arenaHit = false;
                    arenaKnifeCd = difficulty == 2 ? 0.55f : difficulty == 0 ? 1f : 0.75f;
                    ProcAudio.PlayAt(Sfx.KnifeSwing, transform.position + Vector3.up * 1.3f, 0.8f, 0.1f);
                }
            }
            else
            {
                state = State.Roam;
                if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance < 1f)
                    && NavMesh.SamplePosition(SkyArena.RandomPoint(), out var nh, 3f, NavMesh.AllAreas))
                    agent.SetDestination(nh.position);
            }

            if (arenaKnifeT >= 0f)
            {
                arenaKnifeT += dt / 0.55f;
                if (!arenaHit && arenaKnifeT >= 0.33f) { arenaHit = true; ArenaStab(); }
                if (arenaKnifeT >= 1f) arenaKnifeT = -1f;
            }
            if (anim) anim.knife = arenaKnifeT;
            return true;
        }

        void ArenaStab()
        {
            if (!target || !target.alive) return;
            Vector3 to = target.transform.position - transform.position; to.y = 0f;
            if (to.magnitude > 2.3f) return;
            var d = target.GetComponent<IDamageable>() ?? target.GetComponentInParent<IDamageable>();
            if (d == null) return;
            Vector3 dir = (target.Chest - (rig ? rig.head.position : transform.position + Vector3.up * 1.6f)).normalized;
            bool back = Vector3.Dot(target.transform.forward, to.normalized) > 0.5f;
            var res = d.TakeDamage(new DamageInfo
            {
                amount = back ? 110f : 55f, point = target.Chest, normal = -dir, direction = dir,
                critical = back, attacker = combatant, weapon = "Нож"
            });
            if (!res.applied) return;
            FX.Ensure().Blood(target.Chest, -dir, 10);
            ProcAudio.PlayAt(Sfx.KnifeHit, target.Chest, 0.9f);
        }

        void ArenaExit()
        {
            arenaWas = false;
            arenaKnifeT = -1f;
            if (anim) { anim.holdingWeapon = true; anim.knife = -1f; }
            target = null;
            targetVisible = false;
            state = State.Roam;
            if (agent.enabled) agent.speed = 4.4f;
        }
    }
}
