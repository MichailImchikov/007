using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Drone war over the network. A drone is simulated only on the machine of its operator (the player's
    /// own machine, or the host for bots); everybody else sees a kinematic mirror (<see cref="Drone.Remote"/>):
    ///  • DroneSpawn / DroneEnd (reliable) create and remove the mirror, DroneState (20 Hz, unreliable) moves it;
    ///  • a hit on a mirror is forwarded to the owner as DroneDamage, which applies it to the real drone;
    ///  • the warhead blast travels as the usual Explosion message, grenade drops as the usual Grenade message;
    ///  • "operator revealed" marks are announced by the owner (DroneReveal), the host's wind is sent on join.
    /// Every drone message starts with the operator's id: one operator has at most one drone in the air.
    /// </summary>
    public partial class NetGame
    {
        readonly Dictionary<int, Drone> remoteDrones = new Dictionary<int, Drone>();
        readonly Dictionary<int, float> droneEndedAt = new Dictionary<int, float>();

        /// <summary>Messages a machine may only send about its own drones (checked by the host).</summary>
        static bool DroneOwned(Msg m) => m == Msg.DroneSpawn || m == Msg.DroneState || m == Msg.DroneEnd || m == Msg.DroneReveal;

        static void WQ(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        static Quaternion RQ(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        void ClearDrones()
        {
            foreach (var d in remoteDrones.Values) if (d) Destroy(d.gameObject);
            remoteDrones.Clear();
            droneEndedAt.Clear();
        }

        /// <summary>The mirror of the drone flown by operator <paramref name="ownerId"/> (null when there is none).</summary>
        static Drone RemoteDrone(int ownerId) => I && I.remoteDrones.TryGetValue(ownerId, out var d) && d && !d.Dead ? d : null;

        static bool LocalDrone(Drone d) => d && !d.Remote && d.Owner && !d.Owner.remote && d.Owner.netId != 0;

        byte[] DroneSpawnMsg(Drone d, bool launch)
        {
            var w = Begin(Msg.DroneSpawn);
            w.Write((short)d.Owner.netId);
            WV(w, d.transform.position); WQ(w, d.transform.rotation);
            w.Write(launch);
            return End();
        }

        // ------------------------------------------------------------------ outgoing (called by Drone / DroneMode)

        public static void DroneLaunched(Drone d)
        {
            if (!Ok || !LocalDrone(d)) return;
            I.Publish(I.DroneSpawnMsg(d, true));
        }

        /// <summary>0 — warhead went off, 1 — shot down, 2 — removed.</summary>
        public static void DroneEnded(Drone d, int kind)
        {
            if (!Ok || !LocalDrone(d)) return;
            var w = I.Begin(Msg.DroneEnd);
            w.Write((short)d.Owner.netId); w.Write((byte)kind); WV(w, d.transform.position);
            I.Publish(I.End());
        }

        /// <summary>Someone hit the mirror of a drone here: the owner applies it to the real one.</summary>
        public static void SendDroneDamage(Drone d, DamageInfo info)
        {
            if (!Ok || !d || !d.Remote || !d.Owner || d.Owner.netId == 0) return;
            var w = I.Begin(Msg.DroneDamage);
            w.Write((short)d.Owner.netId); w.Write((short)IdOf(info.attacker));
            w.Write(info.amount); WV(w, info.point); WV(w, info.direction); w.Write(info.weapon ?? "");
            I.Route(d.Owner.netId, I.End());
        }

        public static void SendDroneReveal(Combatant owner, Combatant by, Vector3 center)
        {
            if (!Ok || !owner || owner.remote || owner.netId == 0) return;
            var w = I.Begin(Msg.DroneReveal);
            w.Write((short)owner.netId); w.Write((short)IdOf(by)); WV(w, center);
            I.Publish(I.End());
        }

        /// <summary>Snapshots of the drones flown on this machine (called with the character snapshots).</summary>
        void SendDroneStates()
        {
            for (int i = 0; i < Drone.All.Count; i++)
            {
                var d = Drone.All[i];
                if (!LocalDrone(d) || d.Dead) continue;
                var w = Begin(Msg.DroneState);
                w.Write((short)d.Owner.netId);
                var b = d.Body;
                WV(w, b ? b.position : d.transform.position);
                WQ(w, b ? b.rotation : d.transform.rotation);
                WV(w, d.Velocity);
                w.Write((byte)Mathf.RoundToInt(Mathf.Clamp01(d.ThrottleFraction) * 255f));
                byte f = 0;
                if (d.Armed && !d.LinkLost) f |= 1;
                if (d.Thermal) f |= 2;
                w.Write(f);
                Publish(End(), false);
            }
        }

        /// <summary>Host → a client that just loaded the map: the wind and the drones already in the air.</summary>
        void SendDroneFullState(ulong client)
        {
            if (!DroneMode.Active) return;
            var w = Begin(Msg.DroneWind);
            WV(w, DroneMode.Wind);
            SendTo(client, End(), true);
            int own = PlayerId(client);
            foreach (var d in Drone.All)
            {
                if (!d || d.Dead || !d.Owner || d.Owner.netId == 0 || d.Owner.netId == own) continue;
                SendTo(client, DroneSpawnMsg(d, false), true);
            }
        }

        // ------------------------------------------------------------------ incoming

        void DispatchDrone(Msg m, BinaryReader r, bool fromHost)
        {
            switch (m)
            {
                case Msg.DroneSpawn:
                {
                    int id = r.ReadInt16();
                    Vector3 pos = RV(r);
                    Quaternion rot = RQ(r);
                    bool launch = r.ReadBoolean();
                    droneEndedAt.Remove(id);
                    var d = EnsureRemoteDrone(id, pos, rot);
                    if (d && launch) ProcAudio.PlayAt(Sfx.Throw, pos, 0.7f);
                    break;
                }
                case Msg.DroneState:
                {
                    int id = r.ReadInt16();
                    Vector3 pos = RV(r);
                    Quaternion rot = RQ(r);
                    Vector3 vel = RV(r);
                    float thr = r.ReadByte() / 255f;
                    byte f = r.ReadByte();
                    // a late unreliable snapshot must not bring back a drone that already ended
                    if (droneEndedAt.TryGetValue(id, out float ended) && Time.time - ended < 1.5f) return;
                    var d = EnsureRemoteDrone(id, pos, rot);
                    if (d) d.NetState(pos, rot, vel, thr, (f & 1) != 0, (f & 2) != 0);
                    break;
                }
                case Msg.DroneEnd:
                {
                    int id = r.ReadInt16();
                    int kind = r.ReadByte();
                    Vector3 pos = RV(r);
                    droneEndedAt[id] = Time.time;
                    if (remoteDrones.TryGetValue(id, out var d) && d)
                    {
                        d.transform.position = pos;
                        d.NetEnd(kind);
                    }
                    remoteDrones.Remove(id);
                    break;
                }
                case Msg.DroneDamage:
                {
                    var owner = Find(r.ReadInt16());
                    var attacker = Find(r.ReadInt16());
                    var info = new DamageInfo
                    {
                        amount = r.ReadSingle(), point = RV(r), direction = RV(r), weapon = r.ReadString(),
                        attacker = attacker, fromNetwork = true,
                    };
                    info.normal = -info.direction;
                    if (!owner || owner.remote) return;
                    var op = owner.GetComponent<DroneOperator>();
                    if (op && op.Drone && !op.Drone.Dead) op.Drone.TakeDamage(info);
                    break;
                }
                case Msg.DroneReveal:
                {
                    var owner = Find(r.ReadInt16());
                    var by = Find(r.ReadInt16());
                    Vector3 center = RV(r);
                    if (owner && owner.remote) DroneMode.AddReveal(owner, by, center);
                    break;
                }
                case Msg.DroneWind:
                    if (fromHost) DroneMode.SetNetWind(RV(r));
                    break;
                default:
                    DispatchDisco(m, r, fromHost);
                    break;
            }
        }

        Drone EnsureRemoteDrone(int id, Vector3 pos, Quaternion rot)
        {
            if (remoteDrones.TryGetValue(id, out var d) && d && !d.Dead) return d;
            var owner = Find(id);
            if (!owner || !owner.remote || !DroneMode.Active) return null;
            d = Drone.CreateRemote(owner, pos, rot);
            remoteDrones[id] = d;
            return d;
        }
    }
}
