using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Bot drone pilot. Flies the same physics as the player through the same sticks (stabilized mode):
    /// climbs to cruise height, heads for an enemy, avoids obstacles, then either dives into the target
    /// (kamikaze) or hangs at a stand-off distance and shoots, finishing with a dive when out of ammo.
    /// Uses the thermal sight when it has lost sight of everyone.
    /// </summary>
    public class DroneBotPilot
    {
        enum Phase { Climb, Cruise, Attack }

        readonly DroneOperator op;
        readonly CombatBot bot;
        readonly bool gunner;
        readonly float skill;
        readonly RaycastHit[] hits = new RaycastHit[12];
        Phase phase = Phase.Climb;
        Combatant target;
        Vector3 targetLast, targetVel;
        float retarget, noLosT, think;
        bool los;
        int drops;

        public DroneBotPilot(DroneOperator op, CombatBot bot)
        {
            this.op = op;
            this.bot = bot;
            gunner = Random.value < 0.4f;
            int diff = bot ? bot.difficulty : 1;
            skill = diff == 0 ? 0.6f : diff == 2 ? 1.15f : 0.85f;
        }

        public void Drive(Drone d, float dt)
        {
            Vector3 pos = d.transform.position;
            Vector3 v = d.Velocity;

            retarget -= dt;
            if (!target || !target.alive || retarget <= 0f) { PickTarget(pos); retarget = 6f; }
            think -= dt;
            if (think <= 0f && target)
            {
                think = 0.15f;
                Vector3 tp = target.Chest;
                targetVel = Vector3.Lerp(targetVel, (tp - targetLast) / 0.15f, 0.5f);
                targetLast = tp;
                los = LineOfSight(d, pos, tp) || LineOfSight(d, pos, target.Head);
                if (!los && !d.Thermal && d.ThermalCd <= 0f && phase != Phase.Climb) d.ToggleThermal();
            }

            Vector3 vDes = Vector3.zero;
            float ground = GroundHeight(pos);
            float cruiseAlt = ground + 11f;
            bool sport = false;
            Vector3 lookAt = pos + d.transform.forward * 10f;

            if (phase == Phase.Climb)
            {
                vDes = Vector3.up * 4f;
                if (pos.y > cruiseAlt - 2f || d.Age > 4f) phase = Phase.Cruise;
            }
            else if (!target)
            {
                vDes = new Vector3(0f, (cruiseAlt - pos.y) * 0.8f, 0f);
            }
            else
            {
                Vector3 tp = target.Chest;
                Vector3 to = tp - pos;
                Vector3 flat = new Vector3(to.x, 0f, to.z);
                float hd = flat.magnitude;
                lookAt = tp;
                // seen through the thermal sight within its through-wall range counts as "known"
                bool known = los || (d.Thermal && to.magnitude < DroneThermal.XRayRange);
                if (phase == Phase.Cruise)
                {
                    Vector3 dir = hd > 0.1f ? flat / hd : Vector3.zero;
                    float alt = Mathf.Max(cruiseAlt, tp.y + 9f);
                    vDes = dir * Mathf.Min(16f, hd * 0.6f) * skill + Vector3.up * Mathf.Clamp((alt - pos.y) * 0.8f, -4f, 4f);
                    sport = hd > 60f;
                    float engage = gunner && drops < 3 ? 34f : 28f;
                    if (hd < engage && los) phase = Phase.Attack;
                    if (hd < 20f && !los) { noLosT += dt; if (noLosT > 10f) { noLosT = 0f; PickTarget(pos, target); } }
                }
                else
                {
                    if (!los && !known) { noLosT += dt; if (noLosT > 2.5f) { phase = Phase.Cruise; noLosT = 0f; } }
                    else noLosT = 0f;
                    float dist = to.magnitude;
                    if (gunner && drops < 3)
                    {
                        // bomber: hang ~9 m above the target and release when the fall lands on it
                        float h = Mathf.Max(4f, pos.y - tp.y);
                        float tFall = Mathf.Sqrt(2f * h / 9.81f);
                        Vector3 over = tp + targetVel * tFall * skill + Vector3.up * 9f;
                        vDes = Vector3.ClampMagnitude((over - pos) * 1.4f, 12f);
                        Vector3 impact = pos + new Vector3(v.x, 0f, v.z) * tFall;
                        Vector2 miss = new Vector2(impact.x - tp.x - targetVel.x * tFall, impact.z - tp.z - targetVel.z * tFall);
                        if (d.DropCd <= 0f && miss.magnitude < 1.6f / skill && los)
                        {
                            d.cmdFire = true;
                            drops++;
                        }
                        lookAt = tp;
                    }
                    else
                    {
                        // kamikaze dive with lead
                        float tgo = dist / 18f;
                        Vector3 aimP = tp + targetVel * tgo * 0.6f * skill;
                        vDes = (aimP - pos).normalized * 18f * Mathf.Lerp(0.8f, 1f, skill);
                        sport = true;
                    }
                }
            }

            // obstacle avoidance along the flight path (ignores the target itself)
            float sp = v.magnitude;
            if (sp > 1.5f && !(phase == Phase.Attack && !gunner))
            {
                int n = Physics.SphereCastNonAlloc(pos, 0.35f, v / sp, hits, sp * 1.1f + 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var c = hits[i].collider;
                    if (c.transform.IsChildOf(d.transform) || (target && c.transform.IsChildOf(target.transform)) || DroneMode.IsGhost(c)) continue;
                    vDes += Vector3.up * 7f + hits[i].normal * 4f;
                    break;
                }
            }
            if (phase != Phase.Attack && pos.y - ground < 3f) vDes.y = Mathf.Max(vDes.y, 3f);
            if (pos.y < DroneMode.WaterLevel + 2f) vDes.y = Mathf.Max(vDes.y, 3f);

            // the same keys a player has: horizontal speed relative to the heading (WASD), up / down
            Vector3 face = lookAt - pos; face.y = 0f;
            if (phase == Phase.Attack || face.sqrMagnitude > 4f)
                d.SetHeading(Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : d.transform.forward).eulerAngles.y);
            Vector3 e = vDes - v;
            Vector3 vLocal = Quaternion.Inverse(Quaternion.Euler(0f, d.HeadingTarget, 0f)) * new Vector3(vDes.x, 0f, vDes.z);
            float top = sport ? Drone.MaxSpeedSport : Drone.MaxSpeed;
            d.cmdCyclic = Vector2.ClampMagnitude(new Vector2(vLocal.x, vLocal.z) / top, 1f);
            float rate = vDes.y > 0f ? (sport ? 7f : 4.5f) : (sport ? 7f : 3.5f);
            d.cmdThrottle = Mathf.Clamp(vDes.y / rate + e.y * 0.15f, -1f, 1f);
            d.cmdSport = sport;

            // camera on the target
            Vector3 lt = lookAt - d.Cam.transform.position;
            d.SetCamTilt(Mathf.Atan2(lt.y, new Vector2(lt.x, lt.z).magnitude) * Mathf.Rad2Deg);
        }

        void PickTarget(Vector3 pos, Combatant exclude = null)
        {
            Combatant best = null;
            float bestScore = float.MaxValue;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || c == exclude || !op.Combatant.IsEnemyOf(c)) continue;
                float s = Vector3.Distance(c.transform.position, pos) * Random.Range(0.8f, 1.2f);
                if (s < bestScore) { bestScore = s; best = c; }
            }
            if (best != target) { target = best; if (target) targetLast = target.Chest; targetVel = Vector3.zero; }
        }

        bool LineOfSight(Drone d, Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.1f) return true;
            int n = Physics.RaycastNonAlloc(from, dir / dist, hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c.transform.IsChildOf(d.transform) || (target && c.transform.IsChildOf(target.transform)) || DroneMode.IsGhost(c)) continue;
                return false;
            }
            return true;
        }

        static float GroundHeight(Vector3 p)
        {
            if (Physics.Raycast(p, Vector3.down, out RaycastHit h, 60f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return Mathf.Max(h.point.y, DroneMode.WaterLevel);
            return Mathf.Max(p.y - 20f, DroneMode.WaterLevel);
        }
    }
}
