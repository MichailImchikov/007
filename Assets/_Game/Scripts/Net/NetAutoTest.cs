using System;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FPS
{
    /// <summary>
    /// Command-line driven network smoke test (for a second instance without a human at the keyboard):
    ///   007.exe -nethost Map_Port [-netbots 3] [-nettest]
    ///   007.exe -netjoin 127.0.0.1 [-nettest]
    /// -nettest makes the local player act on its own (hits, grenade, cover, abilities, execution)
    /// and logs the replicated state every 2 s with the "[NetTest]" prefix.
    /// </summary>
    public class NetAutoTest : MonoBehaviour
    {
        static string hostScene, joinAddr;
        static bool test;
        static int bots = -1;
        public static NetAutoTest Instance;

        float t, logT, cycleT;
        int step;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string next = i + 1 < args.Length ? args[i + 1] : null;
                if (a == "-nethost" && next != null) hostScene = next;
                else if (a == "-netjoin" && next != null) joinAddr = next;
                else if (a == "-nettest") test = true;
                else if (a == "-netbots" && next != null) int.TryParse(next, out bots);
                else if (a == "-netnick" && next != null) CharacterLook.Local.nick = next;
            }
            if (hostScene == null && joinAddr == null && !test) return;
            var go = new GameObject("NetAutoTest");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NetAutoTest>();
            if (bots >= 0) MatchSettings.botCount = bots;
            if (hostScene != null) NetGame.Ensure().StartHost(hostScene);
            else if (joinAddr != null) NetGame.Ensure().Join(joinAddr);
        }

        /// <summary>Editor-driven tests can switch the autopilot on for the local player.</summary>
        public static void EnableAutopilot()
        {
            test = true;
            if (!Instance) { var go = new GameObject("NetAutoTest"); DontDestroyOnLoad(go); Instance = go.AddComponent<NetAutoTest>(); }
        }

        void OnEnable() => SceneManager.sceneLoaded += (s, m) => { t = 0f; step = 0; };

        static void Log(string s) => Debug.Log("[NetTest] " + s);

        public static string Describe()
        {
            var sb = new StringBuilder();
            var ng = NetGame.I;
            sb.Append(ng && ng.Running ? (ng.Hosting ? "HOST" : "CLIENT") + " id=" + ng.LocalId + " players=" + ng.PlayerCount : "OFFLINE");
            sb.Append(" scene=").Append(SceneManager.GetActiveScene().name);
            var ph = PlayerHealth.Local;
            if (ph)
            {
                var me = ph.GetComponent<Combatant>();
                sb.Append($" me[{me.displayName} hp={ph.Health:0} dead={ph.IsDead} k={me.kills} d={me.deaths} ab={(me.abilities ? me.abilities.Type.ToString() : "-")}]");
            }
            int bots = 0, humans = 0;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.remote) continue;
                if (c.human) humans++; else bots++;
                var px = c.GetComponent<NetProxy>();
                if (c.human || bots <= 2)
                    sb.Append($" {(c.human ? "P" : "B")}{c.netId}[{c.displayName} alive={c.alive} hp={(px ? px.Latest.health * 100f : 0f):0} k={c.kills} d={c.deaths} pos={c.transform.position.x:0.0},{c.transform.position.z:0.0}" +
                              $" crouch={(px ? px.Latest.crouch : 0f):0.0} inv={(c.abilities && c.abilities.Invisible)} chick={(c.abilities && c.abilities.IsChicken)}]");
            }
            sb.Append($" remoteBots={bots} remoteHumans={humans}");
            int broken = 0, cover = 0;
            foreach (var d in Destructible.All) { if (!d) continue; cover++; if (d.Broken) broken++; }
            sb.Append($" cover={cover} broken={broken} pickups={UnityEngine.Object.FindObjectsByType<AbilityPickup>().Length} smoke={SmokeCloud.All.Count}");
            if (MatchManager.Instance) sb.Append($" time={MatchManager.Instance.TimeLeft:0} feed={MatchManager.Instance.Feed.Count} ended={MatchManager.Instance.Ended}");
            return sb.ToString();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            t += dt;
            logT -= dt;
            if (logT <= 0f) { logT = 2f; Log(Describe()); }
            if (!test || !MatchManager.Instance || !PlayerHealth.Local) return;
            var ph = PlayerHealth.Local;
            if (ph.IsDead) return;
            cycleT += dt;
            if (cycleT < 2.5f) return;
            cycleT = 0f;
            try { Act(ph, step++ % 8); }
            catch (Exception e) { Log("action failed: " + e); }
        }

        static Combatant NearestRemote(Vector3 from, bool humanFirst)
        {
            Combatant best = null; float bd = float.MaxValue;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.remote || !c.alive) continue;
                float d = (c.transform.position - from).sqrMagnitude - (humanFirst && c.human ? 1e6f : 0f);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        void Act(PlayerHealth ph, int s)
        {
            var me = ph.GetComponent<Combatant>();
            var target = NearestRemote(me.transform.position, true);
            switch (s)
            {
                case 0:
                    if (!target) { Log("no remote target yet"); return; }
                    var d = target.GetComponent<IDamageable>();
                    var r = d.TakeDamage(new DamageInfo { amount = 35f, point = target.Chest, direction = (target.Chest - me.Head).normalized, attacker = me, weapon = "M4A1" });
                    NetGame.ShotEnds.Clear(); NetGame.ShotEnds.Add((target.Chest, 1));
                    NetGame.SendShot(me, me.Head, NetGame.ShotEnds);
                    Log($"shot {target.displayName}#{target.netId} for 35 → applied={r.applied} predictedKill={r.killed}");
                    break;
                case 1:
                    Grenade.Spawn(me.Head + me.transform.forward, me.transform.forward * 6f + Vector3.up * 3f, null, me);
                    Log("threw grenade");
                    break;
                case 2:
                {
                    Destructible best = null; float bd = float.MaxValue;
                    foreach (var c in Destructible.All) if (c && !c.Broken) { float dd = (c.transform.position - me.transform.position).sqrMagnitude; if (dd < bd) { bd = dd; best = c; } }
                    if (best) { best.TakeDamage(new DamageInfo { amount = 999f, point = best.transform.position, direction = Vector3.forward, attacker = me, weapon = "M4A1" }); Log("hit cover #" + best.netId + " for 999"); }
                    break;
                }
                case 3:
                    me.abilities.Give(AbilityType.Invisibility, AbilityType.Invisibility);
                    Log("invisibility activate=" + me.abilities.TryActivate());
                    break;
                case 4:
                    me.abilities.Give(AbilityType.FartSmoke, AbilityType.FartSmoke);
                    Log("fart smoke activate=" + me.abilities.TryActivate());
                    break;
                case 5:
                    if (!target) return;
                    ph.motor.Teleport(target.transform.position - target.transform.forward * 3f);
                    me.abilities.Give(AbilityType.BigHeads, AbilityType.BigHeads);
                    Log("big heads near " + target.displayName + " activate=" + me.abilities.TryActivate());
                    break;
                case 6:
                {
                    if (!target) return;
                    var px = target.GetComponent<NetProxy>();
                    ph.motor.Teleport(target.transform.position - target.transform.forward * 1.1f);
                    var melee = ph.GetComponent<MeleeController>();
                    bool can = px && px.CanBeExecutedBy(ph.transform);
                    if (can) melee.StartExecution(px);
                    Log("execution on " + target.displayName + " canExecute=" + can);
                    break;
                }
                case 7:
                    me.abilities.Give(AbilityType.ChickenTrap, AbilityType.Grapple);
                    Log("chicken trap activate=" + me.abilities.TryActivate());
                    break;
            }
        }
    }
}
