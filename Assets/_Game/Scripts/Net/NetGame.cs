using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS
{
    /// <summary>Quick queries about the network session (all false / authority when offline).</summary>
    public static class Net
    {
        public static bool Active => NetGame.I && NetGame.I.Running;
        public static bool IsHost => Active && NetGame.I.Hosting;
        /// <summary>A pure client (not the host).</summary>
        public static bool IsClient => Active && !NetGame.I.Hosting;
    }

    enum Msg : byte
    {
        Welcome, Ready, Roster, State, Shot, Damage, Kill, KillFeed, Score, MatchEnd, LoadMap,
        Grenade, Explosion, Exec, Ability, Effect, PickupSpawn, PickupGone, PickupTake, PickupGrant,
        CoverDamage, CoverState, CoverBreak,
        DroneSpawn, DroneState, DroneEnd, DroneDamage, DroneReveal, DroneWind,
        DiscoVolley,
    }

    /// <summary>
    /// LAN multiplayer for Deathmatch on top of Netcode for GameObjects + Unity Transport (no internet, no accounts).
    ///
    /// Topology: one player hosts (server + player), others join by LAN discovery or IP.
    /// Scenes are procedural, so instead of NetworkObject prefabs everything travels as NGO named messages:
    ///  • every machine simulates its own player; the host also simulates bots, cover, pickups, score and the clock;
    ///  • 20 Hz unreliable snapshots (CharState) drive NetProxy characters elsewhere;
    ///  • hits are detected by the shooter and sent to the victim's owner, who applies damage
    ///    (so the existing PlayerHealth / CombatBot / parry code keeps working unchanged);
    ///  • deaths are reported to the host, which keeps score and broadcasts the kill feed;
    ///  • one-shot events (shots, grenades, explosions, ability visuals) are relayed through the host.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public partial class NetGame : MonoBehaviour
    {
        public static NetGame I { get; private set; }

        public const ushort GamePort = 7777;
        public const int Protocol = 2;
        public const int MaxPlayers = 8;
        const float SendInterval = 0.05f;
        const string RelName = "f7r", UnrelName = "f7u";

        public bool Running { get; private set; }
        public bool Hosting { get; private set; }
        public int LocalId { get; private set; }
        public string MapScene { get; private set; }
        /// <summary>Connection status / last error for the menu.</summary>
        public static string Status = "";
        public static event Action StatusChanged;
        public LanDiscovery Discovery { get; private set; }
        public int PlayerCount => peers.Count + 1;

        NetworkManager nm;
        UnityTransport utp;

        class Peer { public ulong client; public string nick; public byte[] look; public bool ready; }
        class RosterEntry { public int id; public string nick; public byte[] look; public bool bot; }

        readonly Dictionary<ulong, Peer> peers = new Dictionary<ulong, Peer>();
        readonly Dictionary<ulong, Peer> pending = new Dictionary<ulong, Peer>();
        readonly Dictionary<int, Combatant> ents = new Dictionary<int, Combatant>();
        readonly Dictionary<int, NetProxy> proxies = new Dictionary<int, NetProxy>();
        readonly Dictionary<int, CharState> clientStates = new Dictionary<int, CharState>();
        readonly Dictionary<int, Destructible> covers = new Dictionary<int, Destructible>();
        readonly HashSet<int> coverDirty = new HashSet<int>();
        readonly Dictionary<int, AbilityPickup> pickups = new Dictionary<int, AbilityPickup>();
        List<RosterEntry> pendingRoster;
        bool sceneReady;
        float sendT, scoreT, coverT;
        int idCounter;
        float lastHostMsg;

        // ------------------------------------------------------------------ lifecycle

        public static NetGame Ensure()
        {
            if (I) return I;
            var go = new GameObject("NetGame");
            go.SetActive(false);
            I = go.AddComponent<NetGame>();
            I.utp = go.AddComponent<UnityTransport>();
            I.nm = go.AddComponent<NetworkManager>();
            I.nm.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = I.utp,
                EnableSceneManagement = false,
                ConnectionApproval = true,
                ForceSamePrefabs = false,
                ClientConnectionBufferTimeout = 15,
                TickRate = 30,
            };
            I.Discovery = go.AddComponent<LanDiscovery>();
            DontDestroyOnLoad(go);
            go.SetActive(true);
            return I;
        }

        void Awake() => I = this;

        void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        static void SetStatus(string s) { Status = s; StatusChanged?.Invoke(); Debug.Log("[Net] " + s); }

        static byte[] JoinPayload()
        {
            var look = CharacterLook.Local;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Protocol);
            w.Write(look.nick);
            var b = look.GetBytes();
            w.Write((byte)b.Length); w.Write(b);
            return ms.ToArray();
        }

        /// <summary>Main menu → "Создать игру": start hosting and load the map.</summary>
        public bool StartHost(string scene)
        {
            Shutdown();
            if (Busy) { StartAfterShutdown(() => BeginHost(scene)); return true; }
            return BeginHost(scene);
        }

        bool BeginHost(string scene)
        {
            utp.SetConnectionData("127.0.0.1", GamePort, "0.0.0.0");
            nm.NetworkConfig.ConnectionData = JoinPayload();
            nm.ConnectionApprovalCallback = Approve;
            Hosting = true;
            if (!nm.StartHost())
            {
                Hosting = false;
                SetStatus("Не удалось создать игру (порт " + GamePort + " занят?)");
                return false;
            }
            Running = true;
            LocalId = 1;
            MapScene = scene;
            Hook();
            Discovery.StartAdvertising();
            SetStatus("Игра создана. Порт " + GamePort);
            LoadMapLocal(scene);
            return true;
        }

        /// <summary>Main menu → "Найти игру по сети": connect to a host.</summary>
        public void Join(string address)
        {
            Shutdown();
            address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            ushort port = GamePort;
            int colon = address.LastIndexOf(':');
            if (colon > 0 && ushort.TryParse(address.Substring(colon + 1), out var p)) { port = p; address = address.Substring(0, colon); }
            if (address == "localhost") address = "127.0.0.1";
            if (!System.Net.IPAddress.TryParse(address, out _)) { SetStatus("Неверный IP-адрес: " + address); return; }
            if (Busy) { StartAfterShutdown(() => BeginJoin(address, port)); return; }
            BeginJoin(address, port);
        }

        // NetworkManager.Shutdown() finishes on the next frame: starting again before that fails with
        // "Can't start while listening" (reconnecting, joining right after a failed attempt, host → join).
        bool Busy => nm && (nm.IsListening || nm.ShutdownInProgress);
        Coroutine startRoutine;

        void StartAfterShutdown(Action start)
        {
            if (startRoutine != null) StopCoroutine(startRoutine);
            SetStatus("Завершение прошлого подключения...");
            startRoutine = StartCoroutine(WaitIdleThen(start));
        }

        IEnumerator WaitIdleThen(Action start)
        {
            float until = Time.unscaledTime + 3f;
            while (Busy && Time.unscaledTime < until) yield return null;
            startRoutine = null;
            if (Busy) { SetStatus("Прошлое подключение не закрылось, попробуйте ещё раз"); yield break; }
            start();
        }

        void BeginJoin(string address, ushort port)
        {
            utp.SetConnectionData(address, port);
            nm.NetworkConfig.ConnectionData = JoinPayload();
            Hosting = false;
            if (!nm.StartClient()) { SetStatus("Не удалось начать подключение"); return; }
            Running = true;
            LocalId = 0;
            Hook();
            lastHostMsg = Time.unscaledTime;
            SetStatus("Подключение к " + address + ":" + port + "...");
        }

        void Hook()
        {
            nm.OnClientConnectedCallback += OnConnected;
            nm.OnClientDisconnectCallback += OnDisconnected;
            nm.OnTransportFailure += OnTransportFailure;
            nm.CustomMessagingManager.RegisterNamedMessageHandler(RelName, OnNamed);
            nm.CustomMessagingManager.RegisterNamedMessageHandler(UnrelName, OnNamed);
        }

        /// <summary>Leave the session (pause menu, errors). Safe to call when offline.</summary>
        public static void Leave()
        {
            if (I) I.Shutdown();
        }

        void Shutdown()
        {
            if (startRoutine != null) { StopCoroutine(startRoutine); startRoutine = null; }
            if (nm)
            {
                nm.OnClientConnectedCallback -= OnConnected;
                nm.OnClientDisconnectCallback -= OnDisconnected;
                nm.OnTransportFailure -= OnTransportFailure;
                if (nm.CustomMessagingManager != null)
                {
                    nm.CustomMessagingManager.UnregisterNamedMessageHandler(RelName);
                    nm.CustomMessagingManager.UnregisterNamedMessageHandler(UnrelName);
                }
                if (nm.IsListening || nm.IsClient || nm.IsServer) nm.Shutdown();
            }
            if (Discovery) Discovery.StopAdvertising();
            Running = Hosting = false;
            sceneReady = false;
            peers.Clear(); pending.Clear(); ents.Clear(); proxies.Clear(); clientStates.Clear();
            covers.Clear(); coverDirty.Clear(); pickups.Clear();
            ClearDrones();
            pendingRoster = null;
            LocalId = 0;
        }

        void OnDestroy() { if (I == this) { Shutdown(); I = null; } }
        void OnApplicationQuit() => Shutdown();

        void Fail(string reason)
        {
            bool inGame = SceneManager.GetActiveScene().name != "MainMenu";
            Shutdown();
            SetStatus(reason);
            if (inGame)
            {
                Time.timeScale = 1f;
                GameInput.Blocked = false;
                SceneManager.LoadScene("MainMenu");
            }
        }

        // ------------------------------------------------------------------ connection events

        void Approve(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse resp)
        {
            resp.CreatePlayerObject = false;
            if (req.ClientNetworkId == NetworkManager.ServerClientId) { resp.Approved = true; return; }
            try
            {
                var r = new BinaryReader(new MemoryStream(req.Payload));
                int proto = r.ReadInt32();
                string nick = CharacterLook.SanitizeNick(r.ReadString());
                int n = r.ReadByte();
                var look = r.ReadBytes(n);
                if (proto != Protocol) { resp.Approved = false; resp.Reason = "Другая версия игры"; return; }
                if (peers.Count + pending.Count + 1 >= MaxPlayers) { resp.Approved = false; resp.Reason = "Сервер заполнен"; return; }
                pending[req.ClientNetworkId] = new Peer { client = req.ClientNetworkId, nick = UniqueNick(nick), look = look };
                resp.Approved = true;
            }
            catch (Exception)
            {
                resp.Approved = false;
                resp.Reason = "Неверные данные подключения";
            }
        }

        string UniqueNick(string nick)
        {
            bool Taken(string n)
            {
                if (n == CharacterLook.Local.nick) return true;
                foreach (var p in peers.Values) if (p.nick == n) return true;
                foreach (var p in pending.Values) if (p.nick == n) return true;
                return false;
            }
            string s = nick;
            for (int i = 2; Taken(s); i++) s = nick + i;
            return s;
        }

        void OnConnected(ulong client)
        {
            if (Hosting)
            {
                if (client == NetworkManager.ServerClientId) return;
                if (!pending.TryGetValue(client, out var p)) p = new Peer { client = client, nick = "Игрок" + client, look = new CharacterLook().GetBytes() };
                pending.Remove(client);
                peers[client] = p;
                var w = Begin(Msg.Welcome);
                w.Write(client);
                w.Write(MapScene);
                w.Write(p.nick);
                WriteSettings(w);
                SendTo(client, End(), true);
                SetStatus(p.nick + " подключился");
            }
            else if (client == nm.LocalClientId)
            {
                LocalId = 1 + (int)client;
                SetStatus("Подключено. Загрузка карты...");
            }
        }

        void OnDisconnected(ulong client)
        {
            if (Hosting)
            {
                pending.Remove(client);
                if (peers.TryGetValue(client, out var p))
                {
                    peers.Remove(client);
                    int id = PlayerId(client);
                    clientStates.Remove(id);
                    if (proxies.TryGetValue(id, out var px) && px) Destroy(px.gameObject);
                    proxies.Remove(id);
                    ents.Remove(id);
                    BroadcastRoster();
                    SetStatus(p.nick + " отключился");
                    if (MatchHUD.Instance) MatchHUD.Instance.Notice(p.nick + " покинул игру");
                }
                return;
            }
            string reason = nm ? nm.DisconnectReason : "";
            Fail(string.IsNullOrEmpty(reason) ? (LocalId == 0 ? "Не удалось подключиться к хосту" : "Соединение с хостом потеряно") : reason);
        }

        void OnTransportFailure() => Fail("Сетевая ошибка (транспорт)");

        public static int PlayerId(ulong client) => 1 + (int)client;
        static ulong OwnerOf(int id) => id >= 1 && id < 1000 ? (ulong)(id - 1) : NetworkManager.ServerClientId;

        // ------------------------------------------------------------------ scenes

        void LoadMapLocal(string scene)
        {
            sceneReady = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Time.timeScale = 1f;
            SceneManager.LoadScene(scene);
        }

        /// <summary>Host: (re)start the match on a map for everybody.</summary>
        public static void HostLoadMap(string scene)
        {
            if (!Net.IsHost) return;
            I.MapScene = scene;
            foreach (var p in I.peers.Values) p.ready = false;
            var w = I.Begin(Msg.LoadMap);
            w.Write(scene);
            WriteSettings(w);
            I.SendAll(I.End(), true);
            I.LoadMapLocal(scene);
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!Running) return;
            ents.Clear(); proxies.Clear(); covers.Clear(); coverDirty.Clear(); pickups.Clear(); clientStates.Clear();
            ClearDrones();
            sceneReady = false;
            if (scene.name == "MainMenu") { Shutdown(); return; }
            // index scene cover by position: identical on every machine, before physics can move anything
            var list = new List<Destructible>();
            foreach (var d in Destructible.All) if (d && d.gameObject.scene == scene) list.Add(d);
            list.Sort((a, b) =>
            {
                Vector3 pa = a.transform.position, pb = b.transform.position;
                int c = Mathf.RoundToInt(pa.x * 100f).CompareTo(Mathf.RoundToInt(pb.x * 100f));
                if (c == 0) c = Mathf.RoundToInt(pa.z * 100f).CompareTo(Mathf.RoundToInt(pb.z * 100f));
                if (c == 0) c = Mathf.RoundToInt(pa.y * 100f).CompareTo(Mathf.RoundToInt(pb.y * 100f));
                if (c == 0) c = string.CompareOrdinal(a.name, b.name);
                return c;
            });
            for (int i = 0; i < list.Count; i++) { list[i].netId = 1 + i; covers[1 + i] = list[i]; }
            StartCoroutine(SceneReadyNextFrame());
        }

        IEnumerator SceneReadyNextFrame()
        {
            yield return null; // MatchManager.Start has spawned the bots, PlayerBody.Start applied the look
            if (!Running) yield break;
            var ph = PlayerHealth.Local;
            if (ph)
            {
                var c = ph.GetComponent<Combatant>();
                c.netId = LocalId;
                c.human = true;
                ents[LocalId] = c;
            }
            sceneReady = true;
            if (Hosting)
            {
                foreach (var p in peers.Values) if (p.ready) AddPeerProxy(p);
                BroadcastRoster();
                foreach (var p in peers.Values) if (p.ready) SendFullState(p.client);
            }
            else
            {
                SendHost(Begin(Msg.Ready), true);
                if (pendingRoster != null) ApplyRoster(pendingRoster);
            }
        }

        // ------------------------------------------------------------------ registration hooks (called by game code)

        public static void RegisterBot(Combatant c, int id)
        {
            if (!Net.IsHost || !c) return;
            c.netId = id;
            I.ents[id] = c;
        }

        public static void RegisterDestructible(Destructible d)
        {
            if (!I || !d || d.netId == 0) return;
            I.covers[d.netId] = d;
        }

        public static Combatant Find(int id) => I && id != 0 && I.ents.TryGetValue(id, out var c) ? c : null;

        /// <summary>Id for objects created at runtime (banana peels, built walls): unique across machines.</summary>
        public static int NewId()
        {
            if (!I) return 0;
            return (Mathf.Max(1, I.LocalId) << 20) | (++I.idCounter & 0xFFFFF);
        }

        static int IdOf(Combatant c) => c ? c.netId : 0;

        // ------------------------------------------------------------------ message plumbing

        readonly MemoryStream ms = new MemoryStream(1024);
        BinaryWriter bw;

        BinaryWriter Begin(Msg m)
        {
            ms.SetLength(0);
            bw ??= new BinaryWriter(ms);
            bw.Write((byte)m);
            return bw;
        }

        byte[] End() { bw.Flush(); return ms.ToArray(); }

        void SendRaw(ulong client, byte[] data, bool reliable)
        {
            if (!nm || !nm.IsListening) return;
            using var w = new FastBufferWriter(data.Length + 4, Allocator.Temp);
            w.WriteBytesSafe(data);
            nm.CustomMessagingManager.SendNamedMessage(reliable ? RelName : UnrelName, client, w,
                reliable ? NetworkDelivery.ReliableFragmentedSequenced : NetworkDelivery.UnreliableSequenced);
        }

        void SendTo(ulong client, byte[] data, bool reliable)
        {
            if (client == nm.LocalClientId) return;
            SendRaw(client, data, reliable);
        }

        void SendHost(BinaryWriter _, bool reliable) => SendRaw(NetworkManager.ServerClientId, End(), reliable);

        /// <summary>Host → every client that has loaded the map (except one).</summary>
        void SendAll(byte[] data, bool reliable, ulong except = ulong.MaxValue, bool onlyReady = false)
        {
            foreach (var p in peers.Values)
                if (p.client != except && (!onlyReady || p.ready)) SendRaw(p.client, data, reliable);
        }

        /// <summary>An event that happened here and everybody else should see: host broadcasts, client sends to the host for relay.</summary>
        void Publish(byte[] data, bool reliable = true)
        {
            if (Hosting) SendAll(data, reliable, ulong.MaxValue, true);
            else SendRaw(NetworkManager.ServerClientId, data, reliable);
        }

        /// <summary>A message for whoever simulates entity <paramref name="target"/>.</summary>
        void Route(int target, byte[] data)
        {
            ulong owner = OwnerOf(target);
            if (Hosting)
            {
                if (owner == NetworkManager.ServerClientId) Dispatch(NetworkManager.ServerClientId, data);
                else SendRaw(owner, data, true);
            }
            else SendRaw(NetworkManager.ServerClientId, data, true);
        }

        static bool Relayed(Msg m) => m == Msg.Shot || m == Msg.Grenade || m == Msg.Explosion || m == Msg.Ability
                                      || m == Msg.DroneSpawn || m == Msg.DroneEnd || m == Msg.DroneReveal;
        static bool Targeted(Msg m) => m == Msg.Damage || m == Msg.Exec || m == Msg.Effect || m == Msg.DroneDamage;

        void OnNamed(ulong sender, FastBufferReader reader)
        {
            int len = reader.Length - reader.Position;
            if (len <= 0) return;
            var data = new byte[len];
            reader.ReadBytesSafe(ref data, len);
            if (!Hosting) lastHostMsg = Time.unscaledTime;
            var m = (Msg)data[0];
            if (Hosting && sender != NetworkManager.ServerClientId)
            {
                if (DroneOwned(m))
                {
                    // a client only speaks for its own drone
                    if (data.Length < 3 || BitConverter.ToInt16(data, 1) != PlayerId(sender)) return;
                    if (m == Msg.DroneState) SendAll(data, false, sender, true);
                }
                if (Relayed(m)) SendAll(data, true, sender, true);
                if (Targeted(m))
                {
                    int target = BitConverter.ToInt16(data, 1);
                    ulong owner = OwnerOf(target);
                    if (owner != NetworkManager.ServerClientId) { if (owner != sender) SendRaw(owner, data, true); return; }
                }
            }
            try { Dispatch(sender, data); }
            catch (Exception e) { Debug.LogWarning("[Net] bad message " + m + ": " + e.Message); }
        }

        static void WV(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 RV(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        // ------------------------------------------------------------------ incoming

        void Dispatch(ulong sender, byte[] data)
        {
            var r = new BinaryReader(new MemoryStream(data, 1, data.Length - 1));
            var m = (Msg)data[0];
            bool fromHost = !Hosting;
            switch (m)
            {
                case Msg.Welcome:
                {
                    if (!fromHost) return;
                    ulong me = r.ReadUInt64();
                    string scene = r.ReadString();
                    string nick = r.ReadString();
                    ReadSettings(r);
                    LocalId = PlayerId(me);
                    if (nick != CharacterLook.Local.nick) CharacterLook.Local.nick = nick; // host made it unique (not saved)
                    MapScene = scene;
                    SetStatus("Загрузка карты " + scene);
                    LoadMapLocal(scene);
                    break;
                }
                case Msg.LoadMap:
                {
                    if (!fromHost) return;
                    string scene = r.ReadString();
                    ReadSettings(r);
                    MapScene = scene;
                    LoadMapLocal(scene);
                    break;
                }
                case Msg.Ready:
                {
                    if (!Hosting || !peers.TryGetValue(sender, out var p)) return;
                    p.ready = true;
                    if (!sceneReady) return; // handled when our own map finished loading
                    AddPeerProxy(p);
                    BroadcastRoster();
                    SendFullState(sender);
                    break;
                }
                case Msg.Roster:
                {
                    if (!fromHost) return;
                    int n = r.ReadInt16();
                    var list = new List<RosterEntry>();
                    for (int i = 0; i < n; i++)
                        list.Add(new RosterEntry { id = r.ReadInt16(), nick = r.ReadString(), look = r.ReadBytes(r.ReadByte()), bot = r.ReadBoolean() });
                    if (sceneReady) ApplyRoster(list); else pendingRoster = list;
                    break;
                }
                case Msg.State:
                {
                    int n = r.ReadByte();
                    for (int i = 0; i < n; i++)
                    {
                        var s = CharState.Read(r);
                        if (Hosting)
                        {
                            if (s.id != PlayerId(sender)) continue; // a client only speaks for itself
                            clientStates[s.id] = s;
                        }
                        if (s.id == LocalId) continue;
                        if (proxies.TryGetValue(s.id, out var px) && px) px.Push(s);
                    }
                    break;
                }
                case Msg.Shot:
                {
                    int id = r.ReadInt16();
                    Vector3 origin = RV(r);
                    int n = r.ReadByte();
                    var ends = new List<(Vector3, byte)>(n);
                    for (int i = 0; i < n; i++) ends.Add((RV(r), r.ReadByte()));
                    if (proxies.TryGetValue(id, out var px) && px) px.OnShot(origin, ends);
                    break;
                }
                case Msg.Damage:
                {
                    var target = Find(r.ReadInt16());
                    var attacker = Find(r.ReadInt16());
                    var info = new DamageInfo
                    {
                        amount = r.ReadSingle(), critical = r.ReadBoolean(), point = RV(r), direction = RV(r), weapon = r.ReadString(),
                        attacker = attacker, fromNetwork = true,
                    };
                    info.normal = -info.direction;
                    if (!target || target.remote) return;
                    var d = target.GetComponent<IDamageable>();
                    d?.TakeDamage(info);
                    break;
                }
                case Msg.Exec:
                {
                    var victim = Find(r.ReadInt16());
                    var attacker = Find(r.ReadInt16());
                    if (!victim || victim.remote || !victim.alive) return;
                    var ph = victim.GetComponent<PlayerHealth>();
                    if (ph) { ph.BeginExecutedBy(attacker); return; }
                    var bot = victim.GetComponent<CombatBot>();
                    if (bot) StartCoroutine(BotExecuted(bot, attacker));
                    break;
                }
                case Msg.Kill:
                {
                    if (!Hosting) return;
                    var killer = Find(r.ReadInt16());
                    int vid = r.ReadInt16();
                    string weapon = r.ReadString();
                    bool hs = r.ReadBoolean();
                    if (vid != PlayerId(sender)) return;
                    MatchManager.Report(killer, Find(vid), weapon, hs);
                    break;
                }
                case Msg.KillFeed:
                {
                    if (!fromHost) return;
                    var killer = Find(r.ReadInt16());
                    var victim = Find(r.ReadInt16());
                    string weapon = r.ReadString();
                    bool hs = r.ReadBoolean();
                    int kk = r.ReadInt16(), kd = r.ReadInt16(), vk = r.ReadInt16(), vd = r.ReadInt16();
                    if (killer) { killer.kills = kk; killer.deaths = kd; }
                    if (victim) { victim.kills = vk; victim.deaths = vd; }
                    if (MatchManager.Instance && victim) MatchManager.Instance.ApplyNetKill(killer, victim, weapon, hs);
                    break;
                }
                case Msg.Score:
                {
                    if (!fromHost) return;
                    float left = r.ReadSingle();
                    int n = r.ReadInt16();
                    for (int i = 0; i < n; i++)
                    {
                        var c = Find(r.ReadInt16());
                        int k = r.ReadInt16(), d = r.ReadInt16();
                        if (c) { c.kills = k; c.deaths = d; }
                    }
                    if (MatchManager.Instance) MatchManager.Instance.SetNetTime(left);
                    break;
                }
                case Msg.MatchEnd:
                    if (fromHost && MatchManager.Instance) MatchManager.Instance.NetEnd(Find(r.ReadInt16()));
                    break;
                case Msg.Grenade:
                {
                    var thrower = Find(r.ReadInt16());
                    Vector3 pos = RV(r), vel = RV(r);
                    bool impact = r.ReadBoolean();
                    if (thrower && !thrower.remote) return;
                    // dropped from a drone: do not let the mirror of that drone (a few cm off) set the copy off
                    var drone = thrower ? RemoteDrone(thrower.netId) : null;
                    Grenade.Spawn(pos, vel, drone ? drone.Collider : null, thrower, true, impact);
                    ProcAudio.PlayAt(Sfx.Throw, pos, 0.7f);
                    break;
                }
                case Msg.Explosion:
                {
                    Vector3 pos = RV(r);
                    float radius = r.ReadSingle(), push = r.ReadSingle();
                    Explosions.Visual(pos, radius, push, Find(r.ReadInt16()));
                    break;
                }
                case Msg.Ability:
                {
                    var caster = Find(r.ReadInt16());
                    var e = (AbilityNetEvent)r.ReadByte();
                    if (caster && caster.remote && caster.abilities) caster.abilities.OnNetEvent(e, r);
                    break;
                }
                case Msg.Effect:
                {
                    var target = Find(r.ReadInt16());
                    var caster = Find(r.ReadInt16());
                    var e = (AbilityNetEffect)r.ReadByte();
                    if (target && !target.remote && target.abilities) target.abilities.OnNetEffect(e, caster, r);
                    break;
                }
                case Msg.PickupSpawn:
                {
                    if (!fromHost) return;
                    int id = r.ReadInt32();
                    Vector3 pos = RV(r);
                    var type = (AbilityType)r.ReadByte();
                    var shown = (AbilityType)r.ReadByte();
                    if (pickups.ContainsKey(id)) return;
                    var pk = AbilityPickup.Create(pos, type, shown);
                    pk.netId = id;
                    pickups[id] = pk;
                    break;
                }
                case Msg.PickupGone:
                {
                    if (!fromHost) return;
                    int id = r.ReadInt32();
                    if (pickups.TryGetValue(id, out var pk) && pk) pk.RemoteGone();
                    pickups.Remove(id);
                    break;
                }
                case Msg.PickupTake:
                {
                    if (!Hosting) return;
                    int id = r.ReadInt32();
                    var who = Find(PlayerId(sender));
                    if (!who || !who.alive || !who.abilities || !pickups.TryGetValue(id, out var pk) || !pk) return;
                    pk.Take(who.abilities); // → GrantPickup + PickupGone
                    break;
                }
                case Msg.PickupGrant:
                {
                    if (!fromHost) return;
                    var type = (AbilityType)r.ReadByte();
                    var shown = (AbilityType)r.ReadByte();
                    var me = Find(LocalId);
                    if (me && me.abilities && me.alive) me.abilities.Give(type, shown);
                    break;
                }
                case Msg.CoverDamage:
                {
                    if (!Hosting) return;
                    int id = r.ReadInt32();
                    var info = new DamageInfo { amount = r.ReadSingle(), weapon = r.ReadString(), point = RV(r), normal = RV(r), direction = RV(r), fromNetwork = true };
                    info.attacker = Find(r.ReadInt16());
                    if (covers.TryGetValue(id, out var d) && d) d.TakeDamage(info);
                    break;
                }
                case Msg.CoverState:
                {
                    if (!fromHost) return;
                    int n = r.ReadInt16();
                    for (int i = 0; i < n; i++)
                    {
                        int id = r.ReadInt32(); float h = r.ReadSingle();
                        if (covers.TryGetValue(id, out var d) && d) d.SetNetHealth(h);
                    }
                    break;
                }
                case Msg.CoverBreak:
                {
                    if (!fromHost) return;
                    int id = r.ReadInt32();
                    Vector3 dir = RV(r), point = RV(r);
                    if (covers.TryGetValue(id, out var d) && d && !d.Broken) d.Break(dir, point);
                    break;
                }
                default:
                    DispatchDrone(m, r, fromHost);
                    break;
            }
        }

        static void WriteSettings(BinaryWriter w)
        {
            w.Write(MatchSettings.botCount); w.Write(MatchSettings.difficulty); w.Write(MatchSettings.killLimit); w.Write(MatchSettings.timeLimit);
            w.Write((byte)MatchSettings.mode);
        }

        static void ReadSettings(BinaryReader r)
        {
            MatchSettings.botCount = r.ReadInt32();
            MatchSettings.difficulty = r.ReadInt32();
            MatchSettings.killLimit = r.ReadInt32();
            MatchSettings.timeLimit = r.ReadSingle();
            MatchSettings.mode = (GameMode)r.ReadByte();
        }

        IEnumerator BotExecuted(CombatBot bot, Combatant attacker)
        {
            if (!bot || !bot.IsAlive) yield break;
            bot.BeginExecution();
            float t = 0f;
            bool killed = false;
            while (t < 1f && bot)
            {
                t += Time.deltaTime / 1.9f;
                bot.SetExecutionProgress(Mathf.Min(t, 1f));
                if (!killed && t >= 0.4f) { killed = true; bot.ExecutionKilled(attacker); }
                yield return null;
            }
            if (bot) bot.EndExecution();
        }

        // ------------------------------------------------------------------ roster & proxies

        void AddPeerProxy(Peer p)
        {
            int id = PlayerId(p.client);
            if (proxies.TryGetValue(id, out var existing) && existing) return;
            var look = new CharacterLook { nick = p.nick };
            look.SetBytes(p.look);
            var px = NetProxy.Create(id, p.nick, look, false);
            proxies[id] = px;
            ents[id] = px.combatant;
        }

        List<RosterEntry> BuildRoster()
        {
            var list = new List<RosterEntry>();
            var me = Find(LocalId);
            list.Add(new RosterEntry { id = LocalId, nick = me ? me.displayName : CharacterLook.Local.nick, look = CharacterLook.Local.GetBytes(), bot = false });
            foreach (var p in peers.Values)
                if (p.ready) list.Add(new RosterEntry { id = PlayerId(p.client), nick = p.nick, look = p.look, bot = false });
            foreach (var kv in ents)
            {
                if (kv.Key < 1000 || !kv.Value) continue;
                var c = kv.Value;
                list.Add(new RosterEntry { id = kv.Key, nick = c.displayName, look = (c.look ?? new CharacterLook()).GetBytes(), bot = true });
            }
            return list;
        }

        void BroadcastRoster()
        {
            if (!Hosting) return;
            var list = BuildRoster();
            var w = Begin(Msg.Roster);
            w.Write((short)list.Count);
            foreach (var e in list) { w.Write((short)e.id); w.Write(e.nick); w.Write((byte)e.look.Length); w.Write(e.look); w.Write(e.bot); }
            SendAll(End(), true, ulong.MaxValue, true);
        }

        void ApplyRoster(List<RosterEntry> list)
        {
            pendingRoster = null;
            var keep = new HashSet<int>();
            foreach (var e in list)
            {
                if (e.id == LocalId) continue;
                keep.Add(e.id);
                var look = new CharacterLook { nick = e.nick };
                look.SetBytes(e.look);
                if (proxies.TryGetValue(e.id, out var px) && px) { px.SetIdentity(e.nick, look); continue; }
                px = NetProxy.Create(e.id, e.nick, look, e.bot);
                proxies[e.id] = px;
                ents[e.id] = px.combatant;
            }
            var gone = new List<int>();
            foreach (var kv in proxies) if (!keep.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int id in gone)
            {
                if (proxies[id]) Destroy(proxies[id].gameObject);
                proxies.Remove(id);
                ents.Remove(id);
            }
        }

        /// <summary>Host → a client that just loaded the map: everything it missed.</summary>
        void SendFullState(ulong client)
        {
            foreach (var kv in pickups)
            {
                if (!kv.Value) continue;
                SendTo(client, PickupSpawnMsg(kv.Value), true);
            }
            var w = Begin(Msg.CoverState);
            var damaged = new List<Destructible>();
            foreach (var d in covers.Values) if (d && !d.Broken && d.Health < d.maxHealth - 0.5f) damaged.Add(d);
            w.Write((short)damaged.Count);
            foreach (var d in damaged) { w.Write(d.netId); w.Write(d.Health); }
            SendTo(client, End(), true);
            foreach (var d in covers.Values)
            {
                if (!d || !d.Broken) continue;
                var b = Begin(Msg.CoverBreak);
                b.Write(d.netId); WV(b, Vector3.down); WV(b, d.transform.position);
                SendTo(client, End(), true);
            }
            SendTo(client, ScoreMsg(), true);
            SendDroneFullState(client);
        }

        // ------------------------------------------------------------------ per-frame: snapshots, score, cover

        void LateUpdate()
        {
            if (!Running || !nm || !nm.IsListening) return;
            float now = Time.unscaledTime;
            if (!Hosting && LocalId != 0 && now - lastHostMsg > 12f) { Fail("Хост не отвечает"); return; }
            if (!sceneReady) return;

            if (now - sendT >= SendInterval)
            {
                sendT = now;
                if (Hosting) SendHostSnapshots();
                else
                {
                    var me = Find(LocalId);
                    if (me)
                    {
                        var w = Begin(Msg.State);
                        w.Write((byte)1);
                        CharState.Capture(me).Write(w);
                        SendRaw(NetworkManager.ServerClientId, End(), false);
                    }
                }
                SendDroneStates();
            }
            if (!Hosting) return;
            if (now - scoreT >= 1f)
            {
                scoreT = now;
                SendAll(ScoreMsg(), true, ulong.MaxValue, true);
            }
            if (coverDirty.Count > 0 && now - coverT >= 0.1f)
            {
                coverT = now;
                var w = Begin(Msg.CoverState);
                w.Write((short)coverDirty.Count);
                foreach (int id in coverDirty) { w.Write(id); w.Write(covers.TryGetValue(id, out var d) && d ? d.Health : 0f); }
                coverDirty.Clear();
                SendAll(End(), true, ulong.MaxValue, true);
            }
        }

        readonly List<CharState> snap = new List<CharState>();

        void SendHostSnapshots()
        {
            snap.Clear();
            foreach (var kv in ents)
            {
                var c = kv.Value;
                if (!c || c.remote) continue;
                snap.Add(CharState.Capture(c));
            }
            foreach (var s in clientStates.Values) snap.Add(s);
            foreach (var p in peers.Values)
            {
                if (!p.ready) continue;
                int own = PlayerId(p.client);
                int i = 0;
                while (i < snap.Count)
                {
                    var w = Begin(Msg.State);
                    long countPos = ms.Position;
                    w.Write((byte)0);
                    byte n = 0;
                    for (; i < snap.Count && n < 16; i++)
                    {
                        if (snap[i].id == own) continue;
                        snap[i].Write(w);
                        n++;
                    }
                    if (n == 0) break;
                    var data = End();
                    data[countPos] = n;
                    SendRaw(p.client, data, false);
                }
            }
        }

        byte[] ScoreMsg()
        {
            var w = Begin(Msg.Score);
            w.Write(MatchManager.Instance ? MatchManager.Instance.TimeLeft : 0f);
            var list = new List<Combatant>();
            foreach (var c in ents.Values) if (c) list.Add(c);
            w.Write((short)list.Count);
            foreach (var c in list) { w.Write((short)c.netId); w.Write((short)c.kills); w.Write((short)c.deaths); }
            return End();
        }

        // ------------------------------------------------------------------ outgoing events (static API used by game code)

        static bool Ok => Net.Active && I.sceneReady;

        public static readonly List<(Vector3 end, byte kind)> ShotEnds = new List<(Vector3, byte)>();

        public static void SendShot(Combatant shooter, Vector3 origin, List<(Vector3 end, byte kind)> ends)
        {
            if (!Ok || !shooter || shooter.remote || shooter.netId == 0) return;
            var w = I.Begin(Msg.Shot);
            w.Write((short)shooter.netId);
            WV(w, origin);
            int n = Mathf.Min(ends.Count, 9);
            w.Write((byte)n);
            for (int i = 0; i < n; i++) { WV(w, ends[i].end); w.Write(ends[i].kind); }
            I.Publish(I.End());
        }

        public static void SendDamage(Combatant target, DamageInfo info)
        {
            if (!Ok || !target || target.netId == 0) return;
            var w = I.Begin(Msg.Damage);
            w.Write((short)target.netId);
            w.Write((short)IdOf(info.attacker));
            w.Write(info.amount); w.Write(info.critical); WV(w, info.point); WV(w, info.direction); w.Write(info.weapon ?? "");
            I.Route(target.netId, I.End());
        }

        public static void SendExecution(Combatant victim, Combatant attacker)
        {
            if (!Ok || !victim) return;
            var w = I.Begin(Msg.Exec);
            w.Write((short)victim.netId); w.Write((short)IdOf(attacker));
            I.Route(victim.netId, I.End());
        }

        public static void SendKill(Combatant killer, Combatant victim, string weapon, bool headshot)
        {
            if (!Ok || !victim) return;
            var w = I.Begin(Msg.Kill);
            w.Write((short)IdOf(killer)); w.Write((short)victim.netId); w.Write(weapon ?? ""); w.Write(headshot);
            I.SendRaw(NetworkManager.ServerClientId, I.End(), true);
        }

        public static void BroadcastKill(KillEvent e)
        {
            if (!Net.IsHost || !I.sceneReady) return;
            var w = I.Begin(Msg.KillFeed);
            w.Write((short)IdOf(e.killer)); w.Write((short)IdOf(e.victim)); w.Write(e.weapon ?? ""); w.Write(e.headshot);
            w.Write((short)(e.killer ? e.killer.kills : 0)); w.Write((short)(e.killer ? e.killer.deaths : 0));
            w.Write((short)(e.victim ? e.victim.kills : 0)); w.Write((short)(e.victim ? e.victim.deaths : 0));
            I.SendAll(I.End(), true, ulong.MaxValue, true);
        }

        public static void BroadcastMatchEnd(Combatant winner)
        {
            if (!Net.IsHost) return;
            I.SendAll(I.ScoreMsg(), true, ulong.MaxValue, true);
            var w = I.Begin(Msg.MatchEnd);
            w.Write((short)IdOf(winner));
            I.SendAll(I.End(), true, ulong.MaxValue, true);
        }

        public static void SendGrenade(Combatant thrower, Vector3 pos, Vector3 vel, bool impactFuse = false)
        {
            if (!Ok || !thrower || thrower.remote) return;
            var w = I.Begin(Msg.Grenade);
            w.Write((short)thrower.netId); WV(w, pos); WV(w, vel); w.Write(impactFuse);
            I.Publish(I.End());
        }

        public static void SendExplosion(Vector3 pos, float radius, float push, Combatant attacker)
        {
            if (!Ok) return;
            var w = I.Begin(Msg.Explosion);
            WV(w, pos); w.Write(radius); w.Write(push); w.Write((short)IdOf(attacker));
            I.Publish(I.End());
        }

        public static void SendAbilityEvent(Combatant caster, AbilityNetEvent e, Action<BinaryWriter> body)
        {
            if (!Ok || !caster || caster.netId == 0) return;
            var w = I.Begin(Msg.Ability);
            w.Write((short)caster.netId); w.Write((byte)e);
            body?.Invoke(w);
            I.Publish(I.End());
        }

        public static void SendAbilityEffect(Combatant target, Combatant caster, AbilityNetEffect e, Action<BinaryWriter> body)
        {
            if (!Ok || !target || target.netId == 0) return;
            var w = I.Begin(Msg.Effect);
            w.Write((short)target.netId); w.Write((short)IdOf(caster)); w.Write((byte)e);
            body?.Invoke(w);
            I.Route(target.netId, I.End());
        }

        // cover
        public static void SendCoverDamage(Destructible d, DamageInfo info)
        {
            if (!Ok || !d || d.netId == 0) return;
            var w = I.Begin(Msg.CoverDamage);
            w.Write(d.netId); w.Write(info.amount); w.Write(info.weapon ?? ""); WV(w, info.point); WV(w, info.normal); WV(w, info.direction);
            w.Write((short)IdOf(info.attacker));
            I.SendRaw(NetworkManager.ServerClientId, I.End(), true);
        }

        public static void CoverChanged(Destructible d)
        {
            if (Net.IsHost && d && d.netId != 0) I.coverDirty.Add(d.netId);
        }

        public static void CoverBroken(Destructible d, Vector3 dir, Vector3 point)
        {
            if (!Net.IsHost || !I.sceneReady || !d || d.netId == 0) return;
            I.coverDirty.Remove(d.netId);
            var w = I.Begin(Msg.CoverBreak);
            w.Write(d.netId); WV(w, dir); WV(w, point);
            I.SendAll(I.End(), true, ulong.MaxValue, true);
        }

        // pickups
        byte[] PickupSpawnMsg(AbilityPickup pk)
        {
            var w = Begin(Msg.PickupSpawn);
            w.Write(pk.netId); WV(w, pk.transform.position); w.Write((byte)pk.type); w.Write((byte)pk.shown);
            return End();
        }

        public static void PickupSpawned(AbilityPickup pk)
        {
            if (!Net.IsHost || !pk) return;
            pk.netId = ++I.idCounter + 50000;
            I.pickups[pk.netId] = pk;
            I.SendAll(I.PickupSpawnMsg(pk), true, ulong.MaxValue, true);
        }

        public static void PickupGone(AbilityPickup pk)
        {
            if (!Net.IsHost || !pk || pk.netId == 0) return;
            I.pickups.Remove(pk.netId);
            var w = I.Begin(Msg.PickupGone);
            w.Write(pk.netId);
            I.SendAll(I.End(), true, ulong.MaxValue, true);
        }

        public static void RequestPickup(AbilityPickup pk)
        {
            if (!Ok || !pk) return;
            I.pickups.Remove(pk.netId);
            var w = I.Begin(Msg.PickupTake);
            w.Write(pk.netId);
            I.SendRaw(NetworkManager.ServerClientId, I.End(), true);
        }

        public static void GrantPickup(AbilityUser u, AbilityType type, AbilityType shown)
        {
            if (!Net.IsHost || !u) return;
            var c = u.GetComponent<Combatant>();
            if (!c || c.netId == 0 || c.netId >= 1000) return;
            var w = I.Begin(Msg.PickupGrant);
            w.Write((byte)type); w.Write((byte)shown);
            I.SendRaw(OwnerOf(c.netId), I.End(), true);
        }
    }
}
