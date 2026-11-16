using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Classes and teams over the network:
    ///  • Launcher — a launcher shot (shooter, origin, direction, speed): every other machine flies a visual-only rocket;
    ///    the blast itself arrives as the usual Explosion message from the shooter's machine.
    ///  • Revive — a medic brings a fallen ally back: routed to the machine that owns the ally (Targeted), which revives it.
    ///  • Teams travel in the roster (RosterEntry.team), assigned by the host (<see cref="Teams.AssignHuman"/>).
    ///  • TeamChange — a client → the host: its player wants the other side (pause menu). The host records the pick and
    ///    sends the roster again; the client switches when its new team comes back in it.
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

        /// <summary>Client → host: our player wants to play on <paramref name="team"/>.</summary>
        public static void SendTeamRequest(int team)
        {
            if (!Net.IsClient || !I.sceneReady) return;
            var w = I.Begin(Msg.TeamChange);
            w.Write((sbyte)team);
            I.SendRaw(NetworkManager.ServerClientId, I.End(), true);
        }

        /// <summary>Host: somebody changed sides (a player's switch, a bot evening the teams): everybody gets the roster.</summary>
        public static void TeamsChanged()
        {
            if (!Net.IsHost || !I.sceneReady) return;
            I.BroadcastRoster();
        }

        void DispatchClasses(Msg m, BinaryReader r, bool fromHost)
        {
            switch (m)
            {
                case Msg.TeamChange:
                {
                    if (!Hosting) return;
                    int id = PlayerId(dispatchSender); // a client only asks for its own player
                    int team = r.ReadSByte();
                    if (!Teams.Active || (team != 0 && team != 1)) return;
                    Teams.Pick(id, team);
                    BroadcastRoster(); // also puts the team on the host's proxy of that player
                    break;
                }
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
