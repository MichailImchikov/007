using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>What a bot should do for the objective (<see cref="ObjectiveMode.BotOrder"/>).</summary>
    public struct BotOrder
    {
        /// <summary>Where to go.</summary>
        public Vector3 dest;
        /// <summary>Close enough within this (metres).</summary>
        public float radius;
        /// <summary>Stand still there (plant / defuse) instead of patrolling the area.</summary>
        public bool hold;
        /// <summary>Run there even while a fight is going on (a ticking bomb).</summary>
        public bool urgent;
    }

    /// <summary>An objective drawn by the HUD (screen icon) and the minimap.</summary>
    public struct ObjectiveIcon
    {
        public Vector3 pos;
        public string label;
        /// <summary>-1 neutral, else the team that holds it.</summary>
        public int owner;
        /// <summary>0..1 capture / plant / defuse progress (0 = none) and the team it goes to.</summary>
        public float progress;
        public int progressTeam;
        public bool pulse;
        /// <summary>Shown even when the camera looks away (pinned to the screen edge).</summary>
        public bool important;
        /// <summary>Zone radius for the minimap (0 = a point).</summary>
        public float radius;
        /// <summary>Overrides the owner colour (alpha 0 = use the owner).</summary>
        public Color tint;
    }

    /// <summary>
    /// Base of the objective team modes — <see cref="DominationMode"/> (Захват точек), <see cref="BombMode"/> (Закладка
    /// бомбы), <see cref="AssaultMode"/> (Штурм). Created by <see cref="MatchManager"/> on top of the team rules (two teams,
    /// no friendly fire, medics). The host (or the single-player game) simulates; clients get the state 5 times a second
    /// (<see cref="WriteState"/>), events as they happen (<see cref="ShowEvent"/>) and send their own plant / defuse
    /// (<see cref="OnClientAction"/>).
    ///
    /// Objective places are not authored per map: the host reads the map — the two spawns farthest apart are the sides,
    /// candidate spots are NavMesh points on the ground floor reachable from both sides, scored by how open they are
    /// (a capture circle should not be cut by walls) — and each mode picks its points along the line between the sides.
    /// </summary>
    public abstract class ObjectiveMode : MonoBehaviour
    {
        public static ObjectiveMode Current { get; private set; }

        public static bool IsObjective(GameMode m) => m == GameMode.Domination || m == GameMode.Bomb || m == GameMode.Assault;

        public static string ModeTitle(GameMode m) => m switch
        {
            GameMode.Domination => "ЗАХВАТ ТОЧЕК",
            GameMode.Bomb => "ЗАКЛАДКА БОМБЫ",
            GameMode.Assault => "ШТУРМ",
            GameMode.TeamDeathmatch => "КОМАНДНЫЙ БОЙ",
            GameMode.Drones => "ВОЙНА ДРОНОВ",
            _ => "DEATHMATCH",
        };

        public static ObjectiveMode Create(GameMode m)
        {
            var go = new GameObject("Objective_" + m);
            switch (m)
            {
                case GameMode.Domination: return go.AddComponent<DominationMode>();
                case GameMode.Bomb: return go.AddComponent<BombMode>();
                case GameMode.Assault: return go.AddComponent<AssaultMode>();
            }
            Destroy(go);
            return null;
        }

        // ------------------------------------------------------------------ what every mode tells the match

        public abstract GameMode Mode { get; }
        public string Title => ModeTitle(Mode);
        /// <summary>Team score shown and compared at the end (points, rounds, captured points).</summary>
        public abstract int TeamScore(int team);
        /// <summary>Score line under the clock.</summary>
        public abstract string ScoreLine(int myTeam);
        /// <summary>Second line of the top panel: what to do now ("Захватите B", "Вы атакуете").</summary>
        public virtual string TaskLine(int myTeam) => "";
        /// <summary>The match clock shows the mode's timer (round, point) instead of the match time.</summary>
        public virtual bool OwnsClock => false;
        public virtual float Clock => 0f;
        /// <summary>Everybody stands still (start of a round).</summary>
        public virtual bool Frozen => false;
        /// <summary>A dead soldier may come back now (one life per round: no).</summary>
        public virtual bool CanRespawn(Combatant c) => true;
        /// <summary>A medic may still bring the fallen soldier back.</summary>
        public virtual bool CanRevive(Combatant target) => true;
        /// <summary>The death screen line instead of the respawn countdown (null = the countdown).</summary>
        public virtual string DeadLine(Combatant me) => null;
        /// <summary>Added to a spawn point's score when <paramref name="who"/> respawns (sides, held points).</summary>
        public virtual float SpawnBias(Combatant who, Vector3 spawn) => 0f;
        /// <summary>A spawn the mode places <paramref name="who"/> at (start of a round), else false.</summary>
        public virtual bool ForcedSpawn(Combatant who, out Vector3 pos, out Quaternion rot) { pos = default; rot = default; return false; }
        /// <summary>The winning team at the end (-1 draw); by default the higher score.</summary>
        public virtual int DecideWinner() { int a = TeamScore(0), b = TeamScore(1); return a > b ? 0 : b > a ? 1 : -1; }
        public virtual string EndReason(int winner) => "";
        /// <summary>Host: what a bot of this team should do now.</summary>
        public virtual bool BotOrder(CombatBot bot, out BotOrder order) { order = default; return false; }
        /// <summary>Host: a bot standing on its <see cref="BotOrder.hold"/> spot (planting, defusing), every frame.</summary>
        public virtual void BotHold(CombatBot bot, float dt) { }
        /// <summary>Host: a bot left its hold spot or got into a fight.</summary>
        public virtual void BotRelease(CombatBot bot) { }
        public abstract void Icons(List<ObjectiveIcon> list, int myTeam);
        /// <summary>A hint for the local player ("Удерживайте [X] — заложить бомбу"), null = none.</summary>
        public virtual string Prompt => null;
        /// <summary>The local player's plant / defuse progress 0..1 (-1 = not doing it) and its caption.</summary>
        public virtual float Channel => -1f;
        public virtual string ChannelLabel => "";

        // network
        public abstract void WriteState(BinaryWriter w);
        public abstract void ReadState(BinaryReader r);
        /// <summary>Host: a client's player did something (plant, defuse).</summary>
        public virtual void OnClientAction(Combatant who, byte action, byte arg, Vector3 pos) { }

        // ------------------------------------------------------------------ shared

        /// <summary>Side A / side B of the map. A: team 0's home in Domination, the attackers' side in Bomb and Assault.</summary>
        public readonly Vector3[] Bases = new Vector3[2];
        public float ZoneRadius { get; protected set; } = 5f;
        /// <summary>This machine runs the rules (single player or the network host).</summary>
        protected bool host;
        protected float span = 60f, floorY;
        protected readonly List<Spot> spots = new List<Spot>();
        bool ready;
        float sendT;

        protected struct Spot { public Vector3 pos; public float t, lateral, open; }

        protected virtual void Awake() { Current = this; }

        protected virtual void OnDestroy()
        {
            if (Current == this) Current = null;
            MiniMap.CollectMarkers -= Markers;
        }

        void Start() => Init();

        /// <summary>
        /// Called by <see cref="MatchManager"/> right after creating the mode, before anybody spawns (the sides and the
        /// objective places are needed for the first spawns). Host: reads the map and places the objectives.
        /// </summary>
        public void Init()
        {
            if (ready) return;
            host = !Net.IsClient;
            MiniMap.CollectMarkers += Markers;
            ObjectiveHUD.Ensure();
            if (host)
            {
                float t0 = Time.realtimeSinceStartup;
                Analyse();
                Setup();
                Debug.Log($"[{GetType().Name}] {spots.Count} spots, span {span:0} m, zone {ZoneRadius:0.0} m, {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
            }
            ready = true;
        }

        /// <summary>Host: choose the objective places (after <see cref="Analyse"/>).</summary>
        protected abstract void Setup();
        /// <summary>Host: the rules, every frame while the match runs.</summary>
        protected abstract void HostTick(float dt);
        /// <summary>Every machine: visuals, the local player's own actions, local clocks.</summary>
        protected virtual void LocalTick(float dt) { }

        void Update()
        {
            if (!ready) return;
            var mm = MatchManager.Instance;
            if (host && mm && !mm.Ended) HostTick(Time.deltaTime);
            LocalTick(Time.deltaTime);
            if (Net.IsHost)
            {
                sendT -= Time.unscaledDeltaTime;
                if (sendT <= 0f) { sendT = 0.2f; NetGame.SendObjState(this); }
            }
        }

        // ------------------------------------------------------------------ events (banners)

        /// <summary>Host: an event everybody sees (formatted per viewer by <see cref="EventText"/>).</summary>
        protected void Announce(byte kind, int team, int arg)
        {
            Debug.Log($"[{Mode}] event {kind} team {team} arg {arg} at {Time.timeSinceLevelLoad:0.0}s");
            ShowEvent(kind, team, arg);
            NetGame.SendObjEvent(kind, team, arg);
        }

        public void ShowEvent(byte kind, int team, int arg)
        {
            OnEvent(kind, team, arg);
            int my = MyTeam;
            string text = EventText(kind, team, arg, my, out string sub, out bool good, out Sfx? sound);
            if (string.IsNullOrEmpty(text)) return;
            ObjectiveHUD.Banner(text, sub, good ? Teams.Colors[0] : Teams.Colors[1]);
            if (sound.HasValue) ProcAudio.Play(sound.Value, 0.8f, 0f);
        }

        /// <summary>Every machine, before the banner: state the event changes (a new round: respawn the local player).</summary>
        protected virtual void OnEvent(byte kind, int team, int arg) { }

        /// <summary>Text of an event for a viewer of <paramref name="myTeam"/>; good = in the viewer's favour.</summary>
        protected abstract string EventText(byte kind, int team, int arg, int myTeam, out string sub, out bool good, out Sfx? sound);

        // ------------------------------------------------------------------ helpers

        public static Combatant Me => PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null;
        public static int MyTeam { get { var m = Me; return m ? m.team : 0; } }

        /// <summary>Living members of a team inside a zone (horizontal circle, a floor's height).</summary>
        protected static int CountIn(Vector3 center, float radius, int team)
        {
            int n = 0;
            foreach (var c in Combatant.All)
                if (c && c.alive && c.team == team && Inside(c.transform.position, center, radius)) n++;
            return n;
        }

        public static bool Inside(Vector3 p, Vector3 center, float radius)
        {
            float dy = p.y - center.y;
            if (dy < -1.5f || dy > 2.6f) return false;
            p.y = center.y;
            return (p - center).sqrMagnitude <= radius * radius;
        }

        protected static int Alive(int team)
        {
            int n = 0;
            foreach (var c in Combatant.All) if (c && c.alive && c.team == team) n++;
            return n;
        }

        protected static int Members(int team)
        {
            int n = 0;
            foreach (var c in Combatant.All) if (c && c.team == team) n++;
            return n;
        }

        /// <summary>0 at side A, 1 at side B (projection on the line between the sides).</summary>
        public float SideT(Vector3 p)
        {
            Vector3 ax = Bases[1] - Bases[0]; ax.y = 0f;
            Vector3 d = p - Bases[0]; d.y = 0f;
            float l2 = Mathf.Max(1f, ax.sqrMagnitude);
            return Vector3.Dot(d, ax) / l2;
        }

        protected static void WV(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        protected static Vector3 RV(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        protected void WriteBases(BinaryWriter w) { WV(w, Bases[0]); WV(w, Bases[1]); w.Write(ZoneRadius); }
        protected void ReadBases(BinaryReader r) { Bases[0] = RV(r); Bases[1] = RV(r); ZoneRadius = r.ReadSingle(); }

        /// <summary>Puts the local player back on its feet for a new round / half (each machine does its own).</summary>
        protected static void RespawnLocal()
        {
            var ph = PlayerHealth.Local;
            if (ph) ph.Respawn();
        }

        /// <summary>Host: everybody to the new round's spawns — bots here, the host's player; clients do theirs on the event.</summary>
        protected void RespawnEverybody()
        {
            RespawnLocal();
            foreach (var c in Combatant.All)
            {
                if (!c || c.remote || c.isPlayer) continue;
                var bot = c.GetComponent<CombatBot>();
                if (!bot || !MatchManager.Instance) continue;
                MatchManager.Instance.GetSpawn(c, out var p, out var r);
                bot.Spawn(p, r);
            }
        }

        /// <summary>A spot for member <paramref name="who"/> among the spawns nearest <paramref name="anchor"/> (round starts).</summary>
        protected bool SpawnNear(Combatant who, Vector3 anchor, out Vector3 pos, out Quaternion rot)
        {
            pos = anchor; rot = Quaternion.identity;
            var sps = FindObjectsByType<SpawnPoint>();
            if (sps.Length == 0) return false;
            var list = new List<SpawnPoint>(sps);
            list.Sort((a, b) => (a.transform.position - anchor).sqrMagnitude.CompareTo((b.transform.position - anchor).sqrMagnitude));
            // members of the team in a fixed order: each gets its own spawn
            var mates = new List<Combatant>();
            foreach (var c in Combatant.All) if (c && c.team == who.team) mates.Add(c);
            mates.Sort((a, b) => a.netId != b.netId ? a.netId.CompareTo(b.netId) : string.CompareOrdinal(a.displayName, b.displayName));
            int idx = Mathf.Max(0, mates.IndexOf(who));
            int pool = Mathf.Clamp(list.Count / 2, 1, Mathf.Max(1, mates.Count));
            var sp = list[idx % pool];
            pos = sp.transform.position;
            // more members than spawns: stand around it
            int ring = idx / pool;
            if (ring > 0)
            {
                float a = ring * 2.4f;
                Vector3 off = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (1.3f * ring);
                if (NavMesh.SamplePosition(pos + off, out var nh, 1.5f, NavMesh.AllAreas) && Mathf.Abs(nh.position.y - pos.y) < 0.8f) pos = nh.position;
            }
            // face the other side
            Vector3 to = (Bases[0] - anchor).sqrMagnitude < (Bases[1] - anchor).sqrMagnitude ? Bases[1] - pos : Bases[0] - pos;
            to.y = 0f;
            rot = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to) : sp.transform.rotation;
            return true;
        }

        // ------------------------------------------------------------------ map analysis (host)

        void Analyse()
        {
            var sps = FindObjectsByType<SpawnPoint>();
            var pts = new List<Vector3>();
            foreach (var s in sps) pts.Add(s.transform.position);
            if (pts.Count < 2) { pts.Add(Vector3.zero); pts.Add(new Vector3(0f, 0f, 40f)); }

            // the sides: the two spawns farthest apart (A = the one nearer the map's south-west, the same on every run)
            float best = -1f;
            Vector3 a = pts[0], b = pts[1];
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                {
                    Vector3 d = pts[i] - pts[j]; d.y = 0f;
                    if (d.sqrMagnitude > best) { best = d.sqrMagnitude; a = pts[i]; b = pts[j]; }
                }
            if (a.x + a.z > b.x + b.z) (a, b) = (b, a);
            Bases[0] = a; Bases[1] = b;
            span = Mathf.Max(20f, Mathf.Sqrt(best));
            var hs = new List<float>();
            foreach (var p in pts) hs.Add(p.y);
            hs.Sort();
            floorY = hs[hs.Count / 5];
            ZoneRadius = Mathf.Clamp(span * 0.055f, 3.6f, 6f);

            // snap the sides onto the NavMesh
            var navBase = new Vector3[2];
            for (int k = 0; k < 2; k++)
                navBase[k] = NavMesh.SamplePosition(Bases[k], out var bh, 3f, NavMesh.AllAreas) ? bh.position : Bases[k];

            // candidate spots: a grid over the spawns' area (with a margin), on the ground floor
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
            float margin = span * 0.12f;
            x0 -= margin; x1 += margin; z0 -= margin; z1 += margin;
            float step = Mathf.Max(2.5f, Mathf.Max(x1 - x0, z1 - z0) / 26f);
            var path = new NavMeshPath();
            var tries = new List<Vector3>(pts);
            for (float x = x0; x <= x1; x += step)
                for (float z = z0; z <= z1; z += step)
                    tries.Add(new Vector3(x, floorY, z));
            spots.Clear();
            foreach (var p in tries)
            {
                if (!NavMesh.SamplePosition(p + Vector3.up * 0.5f, out var h, step * 0.75f, NavMesh.AllAreas)) continue;
                Vector3 c = h.position;
                if (Mathf.Abs(c.y - floorY) > 3.2f) continue;
                float open = Openness(c, ZoneRadius);
                if (open < 0.55f) continue;
                if (!(NavMesh.CalculatePath(navBase[0], c, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)) continue;
                if (!(NavMesh.CalculatePath(navBase[1], c, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)) continue;
                float t = SideT(c);
                Vector3 ax = Bases[1] - Bases[0]; ax.y = 0f;
                Vector3 onAxis = Bases[0] + ax * t;
                Vector3 off = c - onAxis; off.y = 0f;
                spots.Add(new Spot { pos = c, t = t, lateral = off.magnitude, open = open });
            }
        }

        /// <summary>Share of a circle around <paramref name="c"/> a soldier can walk straight to (walls cut zones).</summary>
        static float Openness(Vector3 c, float r)
        {
            const int N = 12;
            int ok = 0;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                Vector3 to = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!NavMesh.Raycast(c, to, out var hit, NavMesh.AllAreas)) ok++;
                else if ((hit.position - c).magnitude > r * 0.75f) ok++;
            }
            return ok / (float)N;
        }

        /// <summary>The best spot near <paramref name="t"/> on the A→B line, away from <paramref name="avoid"/>.</summary>
        protected Vector3 PickSpot(float t, float tol, List<Vector3> avoid, float minGap, float lateralWeight = 1f, float lateralPrefer = 0f, Vector3? lateralFrom = null)
        {
            float bestScore = float.MinValue;
            Vector3 bestPos = Vector3.Lerp(Bases[0], Bases[1], t);
            bool found = false;
            foreach (var s in spots)
            {
                if (Mathf.Abs(s.t - t) > tol * 2f) continue;
                bool near = false;
                foreach (var a in avoid) { Vector3 d = a - s.pos; d.y = 0f; if (d.magnitude < minGap) { near = true; break; } }
                if (near) continue;
                float score = s.open * 3f - Mathf.Abs(s.t - t) / tol * 2f - s.lateral / span * lateralWeight * 2f;
                if (lateralFrom.HasValue)
                {
                    Vector3 d = s.pos - lateralFrom.Value; d.y = 0f;
                    score += Mathf.Min(d.magnitude, span * 0.6f) / span * lateralPrefer * 4f;
                }
                if (score > bestScore) { bestScore = score; bestPos = s.pos; found = true; }
            }
            if (!found && NavMesh.SamplePosition(bestPos, out var h, 12f, NavMesh.AllAreas)) bestPos = h.position;
            return bestPos;
        }

        // ------------------------------------------------------------------ minimap

        readonly List<ObjectiveIcon> iconBuf = new List<ObjectiveIcon>();

        void Markers(List<MiniMap.Marker> list)
        {
            iconBuf.Clear();
            Icons(iconBuf, MyTeam);
            foreach (var ic in iconBuf)
            {
                Color c = IconColor(ic, MyTeam);
                list.Add(new MiniMap.Marker
                {
                    pos = ic.pos, color = c, size = 19f, ring = ic.radius, label = ic.label, pulse = ic.pulse, edge = ic.important,
                });
            }
        }

        public static readonly Color Neutral = new Color(0.86f, 0.86f, 0.88f);

        /// <summary>Allies blue, enemies red, nobody's grey (the viewer's point of view).</summary>
        public static Color IconColor(ObjectiveIcon ic, int myTeam)
        {
            if (ic.tint.a > 0f) return ic.tint;
            return ic.owner < 0 ? Neutral : ic.owner == myTeam ? Teams.Colors[0] : Teams.Colors[1];
        }

        public static Color RelColor(int team, int myTeam) => team < 0 ? Neutral : team == myTeam ? Teams.Colors[0] : Teams.Colors[1];

        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }
}
