using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>
    /// A bot in an objective mode (<see cref="ObjectiveMode"/>): with no enemy around it follows the mode's order instead of
    /// wandering — runs to the point / site / bomb, patrols inside the zone once there, or stands crouched on the spot
    /// while it plants or defuses (<see cref="BotOrder.hold"/>). A fight interrupts it (the plant starts over); an urgent
    /// order (a ticking bomb) cuts a search for a lost enemy short. At the start of a round everybody stands still.
    /// </summary>
    public partial class CombatBot
    {
        float objWanderT;
        bool objHolding, objNavOk;
        Vector3 objNav, objNavFor;

        /// <summary>Roam with an objective: true when the mode gave an order (and it was handled).</summary>
        bool ObjectiveRoam()
        {
            var mode = ObjectiveMode.Current;
            if (!mode || !mode.BotOrder(this, out var o)) { ReleaseObjective(); return false; }
            // the walkable spot for this order (a post by a zone's edge may sit off the NavMesh, behind a crate)
            if (!objNavOk || (objNavFor - o.dest).sqrMagnitude > 1f)
            {
                objNavFor = o.dest;
                objNavOk = NavMesh.SamplePosition(o.dest, out var nh, 3f, NavMesh.AllAreas) || NavMesh.SamplePosition(o.dest, out nh, 7f, NavMesh.AllAreas);
                objNav = objNavOk ? nh.position : o.dest;
            }
            Vector3 me = transform.position;
            Vector3 flat = objNav - me; flat.y = 0f;
            bool there = flat.magnitude <= o.radius && Mathf.Abs(objNav.y - me.y) < 2.5f;
            if (o.hold && there)
            {
                agent.isStopped = true;
                crouched = true;
                objHolding = true;
                mode.BotHold(this, Time.deltaTime);
                return true;
            }
            ReleaseObjective();
            agent.isStopped = false;
            crouched = false;
            agent.speed = SpeedMul * (there ? 2.6f : 4.6f);
            if (there)
            {
                // inside the area: walk around in it, now and then
                objWanderT -= Time.deltaTime;
                if (objWanderT <= 0f || (!agent.pathPending && agent.remainingDistance < 0.8f))
                {
                    objWanderT = Random.Range(2.5f, 5f);
                    Vector2 r = Random.insideUnitCircle * o.radius;
                    if (NavMesh.SamplePosition(objNav + new Vector3(r.x, 0f, r.y), out var nh, 2f, NavMesh.AllAreas)) agent.SetDestination(nh.position);
                }
            }
            else if (objNavOk && !agent.pathPending && (!agent.hasPath || (agent.destination - objNav).sqrMagnitude > 4f))
            {
                agent.SetDestination(objNav);
                objWanderT = 0f;
            }
            return true;
        }

        NavMeshPath objPath;

        /// <summary>
        /// In a fight: a step (up to 4.5 m) along the NavMesh path toward the objective, mixed into the strafing, so a
        /// gunfight down a long aisle still moves the bot onto the point. Zero when already there or without an order.
        /// </summary>
        Vector3 ObjectivePush()
        {
            var mode = ObjectiveMode.Current;
            if (!mode || !agent.enabled || !mode.BotOrder(this, out var o)) return Vector3.zero;
            Vector3 me = transform.position;
            Vector3 flat = o.dest - me; flat.y = 0f;
            if (flat.magnitude <= o.radius) return Vector3.zero;
            objPath ??= new NavMeshPath();
            Vector3 goal = NavMesh.SamplePosition(o.dest, out var nh, 3f, NavMesh.AllAreas) ? nh.position : o.dest;
            if (!NavMesh.CalculatePath(me, goal, NavMesh.AllAreas, objPath) || objPath.corners.Length < 2)
                return flat.normalized * Mathf.Min(flat.magnitude, 4f);
            var cs = objPath.corners;
            for (int i = 1; i < cs.Length; i++)
            {
                Vector3 d = cs[i] - me; d.y = 0f;
                if (d.magnitude > 1f || i == cs.Length - 1) return d.sqrMagnitude > 0.0001f ? d.normalized * Mathf.Min(d.magnitude, 4.5f) : Vector3.zero;
            }
            return Vector3.zero;
        }

        /// <summary>An urgent order (defuse) pulls a searching bot back to the objective.</summary>
        bool ObjectiveUrgent()
        {
            var mode = ObjectiveMode.Current;
            return mode && mode.BotOrder(this, out var o) && o.urgent;
        }

        void ReleaseObjective()
        {
            if (!objHolding) return;
            objHolding = false;
            if (ObjectiveMode.Current) ObjectiveMode.Current.BotRelease(this);
        }

        /// <summary>Start of a round: stand still (true = handled this frame).</summary>
        bool ObjectiveFrozen(float dt)
        {
            var mode = ObjectiveMode.Current;
            if (!mode || !mode.Frozen) return false;
            if (agent.enabled) agent.isStopped = true;
            ReleaseObjective();
            UpdateBody(dt);
            return true;
        }
    }
}
