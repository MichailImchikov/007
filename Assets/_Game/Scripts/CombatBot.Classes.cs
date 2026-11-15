using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>
    /// A bot's class (Combatant.playerClass, drawn by MatchManager): the same passives as the player's (move / reload
    /// multipliers) and simple use of the class abilities —
    ///  • assault: adrenaline when hurt in a fight;
    ///  • medic: a kit when below half health; in a team game, when no enemy is in sight, walks to an ally lying within
    ///    15 m and revives him;
    ///  • engineer: a launcher rocket at a visible enemy 12–60 m away every ~20 s;
    ///  • sniper: nothing extra.
    /// </summary>
    public partial class CombatBot
    {
        float classCd, adrenalineLeft, rocketCd = 6f, reviveHold;
        Combatant reviveTarget;

        PlayerClass BotClass => combatant ? combatant.playerClass : PlayerClass.Assault;

        /// <summary>Returns true while the class takes over the body (a medic going to / kneeling at a fallen ally).</summary>
        bool ClassTick(float dt)
        {
            var info = ClassDB.Get(BotClass);
            if (classCd > 0f) classCd -= dt;
            if (adrenalineLeft > 0f) adrenalineLeft -= dt;
            if (rocketCd > 0f) rocketCd -= dt;
            bool adrenaline = adrenalineLeft > 0f;
            SpeedMul = info.moveMul * (adrenaline ? 1.15f : 1f);
            ReloadMul = 1f / Mathf.Max(0.1f, info.reloadSpeed * (adrenaline ? 1.35f : 1f));
            bool fighting = target && targetVisible;
            switch (BotClass)
            {
                case PlayerClass.Assault:
                    if (classCd <= 0f && fighting && health < maxHealth * 0.45f)
                    {
                        adrenalineLeft = info.duration;
                        Heal(25f);
                        classCd = info.cooldown;
                    }
                    break;
                case PlayerClass.Medic:
                    if (classCd <= 0f && health < maxHealth * 0.5f)
                    {
                        Heal(60f);
                        classCd = info.cooldown;
                    }
                    if (!fighting) return BotRevive(dt);
                    reviveTarget = null;
                    break;
                case PlayerClass.Engineer:
                    if (fighting && rocketCd <= 0f) BotRocket();
                    break;
            }
            return false;
        }

        bool BotRevive(float dt)
        {
            if (!Teams.Active || !agent.enabled) { reviveTarget = null; return false; }
            if (!reviveTarget || reviveTarget.alive || !Teams.Friendly(combatant, reviveTarget))
            {
                reviveTarget = null;
                reviveHold = 0f;
                float best = 15f;
                foreach (var c in Combatant.All)
                {
                    if (!Revives.CanRevive(combatant, c)) continue;
                    float d = Vector3.Distance(transform.position, c.transform.position);
                    if (d < best) { best = d; reviveTarget = c; }
                }
                if (!reviveTarget) return false;
            }
            Vector3 to = reviveTarget.transform.position - transform.position; to.y = 0f;
            float dist = to.magnitude;
            if (dist > 1.6f)
            {
                crouched = false;
                agent.isStopped = false;
                agent.speed = SpeedMul * 5f;
                if (NavMesh.SamplePosition(reviveTarget.transform.position, out var nh, 2f, NavMesh.AllAreas)) agent.SetDestination(nh.position);
                reviveHold = 0f;
                return true;
            }
            agent.isStopped = true;
            crouched = true;
            lookAtOverride = reviveTarget.transform.position;
            reviveHold += dt;
            if (reviveHold >= Revives.HoldTime + 0.3f)
            {
                Revives.Apply(reviveTarget, combatant);
                reviveTarget = null;
                reviveHold = 0f;
                crouched = false;
                agent.isStopped = false;
                classCd = Mathf.Max(classCd, 2f);
            }
            return true;
        }

        void BotRocket()
        {
            if (!target || !muzzle) return;
            Vector3 aimAt = target.Chest;
            float dist = Vector3.Distance(muzzle.position, aimAt);
            if (dist < 12f || dist > 60f) return;
            // a little off, and ground under a far target (the blast does the rest)
            Vector3 dir = (aimAt + Random.insideUnitSphere * (0.4f + dist * 0.02f) * (difficulty == 2 ? 0.6f : 1f) - Vector3.up * 0.4f - muzzle.position).normalized;
            if (Physics.Raycast(muzzle.position, dir, out var hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !hit.collider.transform.IsChildOf(transform)) return; // something right in front: no rocket into our own face
            var r = RocketProjectile.CreateLauncherRocket(null);
            r.SetBlast(7f, 160f, 13f, "РПГ-7");
            r.transform.position = muzzle.position + dir * 0.3f;
            r.Launch(dir, 75f, combatant, transform);
            NetGame.SendLauncherShot(combatant, r.transform.position, dir, 75f);
            rocketCd = 18f + Random.value * 8f;
        }
    }
}
