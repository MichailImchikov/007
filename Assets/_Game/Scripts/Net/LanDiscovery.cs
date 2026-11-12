using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Finds games on the local network without any server: clients broadcast a UDP probe on port 47777,
    /// hosts answer with name / map / player count. Non-blocking sockets polled from Update (no threads).
    /// </summary>
    public class LanDiscovery : MonoBehaviour
    {
        public const int Port = 47777;
        const string Probe = "F7?", Answer = "F7!";

        public class Game
        {
            public string address, host, map;
            public int players, max, port;
            public float seen;
        }

        public readonly List<Game> Found = new List<Game>();
        public bool Searching => client != null;

        UdpClient server, client;
        float probeT;
        readonly byte[] probeBytes = Encoding.UTF8.GetBytes(Probe + NetGame.Protocol);

        // ------------------------------------------------------------------ host side

        public void StartAdvertising()
        {
            StopAdvertising();
            try
            {
                server = new UdpClient();
                server.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                server.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
                server.Client.Blocking = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LAN] discovery server failed: " + e.Message);
                server = null;
            }
        }

        public void StopAdvertising()
        {
            server?.Close();
            server = null;
        }

        // ------------------------------------------------------------------ client side

        public void StartSearch()
        {
            StopSearch();
            Found.Clear();
            try
            {
                client = new UdpClient(0) { EnableBroadcast = true };
                client.Client.Blocking = false;
                probeT = 0f;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LAN] discovery client failed: " + e.Message);
                client = null;
            }
        }

        public void StopSearch()
        {
            client?.Close();
            client = null;
        }

        void OnDestroy() { StopAdvertising(); StopSearch(); }

        static IEnumerable<IPAddress> BroadcastAddresses()
        {
            yield return IPAddress.Broadcast;
            yield return IPAddress.Loopback; // same machine (second instance)
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); } catch { yield break; }
            foreach (var nic in nics)
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                UnicastIPAddressInformationCollection addrs;
                try { addrs = nic.GetIPProperties().UnicastAddresses; } catch { continue; }
                foreach (var a in addrs)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork || a.IPv4Mask == null) continue;
                    var ip = a.Address.GetAddressBytes(); var mask = a.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                    yield return new IPAddress(b);
                }
            }
        }

        /// <summary>This machine's LAN IPv4 addresses (shown to the host so others can type them in).</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var a in nic.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(a.Address.ToString());
                }
            }
            catch { }
            return list;
        }

        void Update()
        {
            if (server != null) PollServer();
            if (client != null) PollClient();
        }

        void PollServer()
        {
            try
            {
                while (server.Available > 0)
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var data = server.Receive(ref ep);
                    string s = Encoding.UTF8.GetString(data);
                    if (s != Probe + NetGame.Protocol) continue;
                    var ng = NetGame.I;
                    if (!ng || !ng.Hosting) continue;
                    string reply = string.Join("|", Answer, CharacterLook.Local.nick.Replace("|", " "), ng.MapScene ?? "?", ng.PlayerCount, NetGame.MaxPlayers, NetGame.GamePort);
                    var rb = Encoding.UTF8.GetBytes(reply);
                    server.Send(rb, rb.Length, ep);
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        void PollClient()
        {
            probeT -= Time.unscaledDeltaTime;
            if (probeT <= 0f)
            {
                probeT = 1f;
                foreach (var a in BroadcastAddresses())
                {
                    try { client.Send(probeBytes, probeBytes.Length, new IPEndPoint(a, Port)); }
                    catch (SocketException) { }
                }
            }
            try
            {
                while (client.Available > 0)
                {
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var data = client.Receive(ref ep);
                    var parts = Encoding.UTF8.GetString(data).Split('|');
                    if (parts.Length < 6 || parts[0] != Answer) continue;
                    string addr = ep.Address.ToString();
                    int.TryParse(parts[5], out int port);
                    // the same host answers on several interfaces: keep one entry per host+port, prefer a real LAN address
                    var g = Found.Find(x => x.port == port && (x.address == addr || x.host == parts[1]));
                    if (g == null) { g = new Game(); Found.Add(g); g.address = addr; }
                    else if (g.address == "127.0.0.1" && addr != "127.0.0.1") g.address = addr;
                    g.host = parts[1]; g.map = parts[2];
                    int.TryParse(parts[3], out g.players); int.TryParse(parts[4], out g.max);
                    g.port = port;
                    g.seen = Time.unscaledTime;
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            Found.RemoveAll(x => Time.unscaledTime - x.seen > 4f);
        }
    }
}
