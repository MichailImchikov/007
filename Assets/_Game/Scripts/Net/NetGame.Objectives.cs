using System.IO;
using Unity.Netcode;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Objective modes over the network (<see cref="ObjectiveMode"/>), all through the host:
    ///  • ObjState — host → clients 5 times a second: the mode's whole state (sides, points, owners, progress, round,
    ///    bomb), so a player who joins mid-match is in sync within a fifth of a second;
    ///  • ObjEvent — host → clients as it happens: banners, and "new round / half" (each client respawns its own player);
    ///  • ObjAction — a client → the host: its player finished planting / defusing (the host checks the carrier, the
    ///    site and the distance against the player's replicated position before accepting it).
    /// Capture progress needs no messages: the host counts everybody in the zones from the positions it already has.
    /// </summary>
    public partial class NetGame
    {
        public static void SendObjState(ObjectiveMode mode)
        {
            if (!Net.IsHost || !I.sceneReady || !mode) return;
            var w = I.Begin(Msg.ObjState);
            mode.WriteState(w);
            I.SendAll(I.End(), true, ulong.MaxValue, true);
        }

        public static void SendObjEvent(byte kind, int team, int arg)
        {
            if (!Net.IsHost || !I.sceneReady) return;
            var w = I.Begin(Msg.ObjEvent);
            w.Write(kind); w.Write((sbyte)team); w.Write(arg);
            I.SendAll(I.End(), true, ulong.MaxValue, true);
        }

        public static void SendObjAction(byte action, byte arg, Vector3 pos)
        {
            if (!Net.IsClient || !I.sceneReady) return;
            var w = I.Begin(Msg.ObjAction);
            w.Write(action); w.Write(arg); WV(w, pos);
            I.SendRaw(NetworkManager.ServerClientId, I.End(), true);
        }

        void DispatchObjectives(Msg m, BinaryReader r, bool fromHost)
        {
            var mode = ObjectiveMode.Current;
            switch (m)
            {
                case Msg.ObjState:
                    if (fromHost && mode) mode.ReadState(r);
                    break;
                case Msg.ObjEvent:
                {
                    if (!fromHost || !mode) return;
                    byte kind = r.ReadByte();
                    int team = r.ReadSByte();
                    int arg = r.ReadInt32();
                    mode.ShowEvent(kind, team, arg);
                    break;
                }
                case Msg.ObjAction:
                {
                    if (!Hosting || !mode) return;
                    var who = Find(PlayerId(dispatchSender)); // a client only acts for its own player
                    byte act = r.ReadByte(), arg = r.ReadByte();
                    Vector3 pos = RV(r);
                    if (who) mode.OnClientAction(who, act, arg, pos);
                    break;
                }
            }
        }
    }
}
