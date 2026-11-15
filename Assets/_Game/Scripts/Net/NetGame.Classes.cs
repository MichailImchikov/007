using System.IO;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Classes and teams over the network:
    ///  • Launcher — a launcher shot (shooter, origin, direction, speed): every other machine flies a visual-only rocket;
    ///    the blast itself arrives as the usual Explosion message from the shooter's machine.
    ///  • Revive — a medic brings a fallen ally back: routed to the machine that owns the ally (Targeted), which revives it.
    ///  • Teams travel in the roster (RosterEntry.team), assigned by the host (<see cref="Teams.AssignHuman"/>).
    /// </summary>
    public partial class NetGame
    {
        public static void SendLauncherShot(Combatant shooter, Vector3 origin, Vector3 dir, float speed)
        {
            if (!Ok || !shooter || shooter.remote) return;
            var w = I.Begin(Msg.Launcher);
            w.Write((short)shooter.netId); WV(w, origin); WV(w, dir); w.Write(speed);
            I.Publish(I.End());
        }

        /// <summary>Medic → the owner of a fallen ally: bring it back where it lies.</summary>
        public static void SendRevive(Combatant target, Combatant medic)
        {
            if (!Ok || !target || target.netId == 0) return;
            var w = I.Begin(Msg.Revive);
            w.Write((short)target.netId); w.Write((short)IdOf(medic));
            I.Route(target.netId, I.End());
        }

        void DispatchClasses(Msg m, BinaryReader r, bool fromHost)
        {
            switch (m)
            {
                case Msg.Launcher:
                {
                    var shooter = Find(r.ReadInt16());
                    Vector3 origin = RV(r), dir = RV(r);
                    float speed = r.ReadSingle();
                    if (shooter && !shooter.remote) return;
                    var rocket = RocketProjectile.CreateLauncherRocket(null);
                    rocket.visualOnly = true;
                    rocket.transform.position = origin;
                    rocket.Launch(dir, speed, shooter, shooter ? shooter.transform : null);
                    break;
                }
                case Msg.Revive:
                {
                    var target = Find(r.ReadInt16());
                    var medic = Find(r.ReadInt16());
                    if (!target || target.remote) return;
                    Revives.Apply(target, medic);
                    break;
                }
                default:
                    DispatchObjectives(m, r, fromHost);
                    break;
            }
        }
    }
}
