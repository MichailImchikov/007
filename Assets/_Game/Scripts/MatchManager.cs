using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace FPS
{
    /// <summary>Settings chosen in the main menu, carried across scene loads.</summary>
    public static class MatchSettings
    {
        public static int botCount = 7;
        public static int difficulty = 1;
        public static int killLimit = 25;
        public static float timeLimit = 600f;
    }

    public struct KillEvent
    {
        public Combatant killer, victim;
        public string weapon;
        public bool headshot;
        public float time;
    }

    /// <summary>
    /// Free-for-all Deathmatch: bakes the NavMesh, spawns bots, picks safe spawn points,
    /// keeps score, kill feed, time / kill limit, end-of-match results and restart.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MatchManager : MonoBehaviour
    {
        public static MatchManager Instance { get; private set; }

        public string mapName = "Карта";
        public GameObject botTemplate;
        public NavMeshSurface navSurface;

        public float TimeLeft { get; private set; }
        public bool Ended { get; private set; }
        public Combatant Winner { get; private set; }
        public float EndTime { get; private set; }
        public readonly List<KillEvent> Feed = new List<KillEvent>();
        public event Action<KillEvent> Killed;

        readonly List<SpawnPoint> spawns = new List<SpawnPoint>();
        static readonly string[] Names = { "Ворон", "Сокол", "Гром", "Призрак", "Кобра", "Шторм", "Тень", "Волк", "Лис", "Рысь", "Барс", "Беркут", "Кедр", "Удав" };
        /// <summary>Any primary from the catalog; assault rifles and SMGs more often, shotguns rarely (bots fight at range).</summary>
        static string RandomBotWeapon()
        {
            float total = 0f;
            foreach (var d in WeaponCatalog.All) total += BotWeight(d);
            float r = Random.value * total;
            foreach (var d in WeaponCatalog.All) { r -= BotWeight(d); if (r <= 0f && BotWeight(d) > 0f) return d.name; }
            return "M4A1";
        }

        static float BotWeight(WeaponDef d) => d.category switch
        {
            WeaponCategory.Assault => 2f, WeaponCategory.SMG => 1.5f, WeaponCategory.Marksman => 1f, WeaponCategory.Shotgun => 0.5f, _ => 0f,
        };

        public static void Report(Combatant killer, Combatant victim, string weapon, bool headshot)
        {
            // clients only report their own deaths; the host keeps the score and broadcasts the kill feed
            if (Net.IsClient) { NetGame.SendKill(killer, victim, weapon, headshot); return; }
            if (Instance) Instance.ReportKill(killer, victim, weapon, headshot);
            else if (victim) { victim.deaths++; if (killer && killer != victim) killer.kills++; }
        }

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
            GameInput.Blocked = false;
            TimeLeft = MatchSettings.timeLimit;
            spawns.AddRange(FindObjectsByType<SpawnPoint>());

            // bake navigation from colliders (players' capsules excluded); big imported maps ship a pre-baked NavMesh
            if (navSurface && !navSurface.navMeshData)
            {
                var ccs = FindObjectsByType<CharacterController>();
                foreach (var c in ccs) c.enabled = false;
                // breakable cover is carved by NavMeshObstacles instead, so paths open up when it is destroyed
                var cover = FindObjectsByType<Destructible>();
                foreach (var d in cover) { var dc = d.GetComponent<Collider>(); if (dc) dc.enabled = false; }
                navSurface.BuildNavMesh();
                foreach (var d in cover) { var dc = d.GetComponent<Collider>(); if (dc) dc.enabled = true; }
                foreach (var c in ccs) c.enabled = true;
            }
        }

        void Start()
        {
            MatchHUD.Ensure();
            AbilitySpawner.Ensure();
            if (PlayerHealth.Local) PlayerHealth.Local.Respawn();
            if (botTemplate)
            {
                botTemplate.SetActive(false);
                // in a network game bots live on the host; clients see them as proxies
                if (!Net.IsClient)
                    for (int i = 0; i < MatchSettings.botCount; i++) SpawnBot(i);
            }
        }

        void SpawnBot(int i)
        {
            var go = Instantiate(botTemplate, transform);
            go.name = "Bot_" + Names[i % Names.Length];
            var bot = go.GetComponent<CombatBot>();
            bot.difficulty = MatchSettings.difficulty;
            bot.weaponName = RandomBotWeapon();
            var c = go.GetComponent<Combatant>();
            c.displayName = Names[i % Names.Length];
            c.isPlayer = false;
            c.look = CharacterLook.Random(new System.Random(Random.Range(0, int.MaxValue)), c.displayName);
            if (bot.rig) c.look.Apply(bot.rig);
            go.SetActive(true);
            NetGame.RegisterBot(c, 1000 + i);
            GetSpawn(c, out var p, out var r);
            bot.Spawn(p, r);
        }

        public void RequestRespawn(CombatBot bot, float delay) => StartCoroutine(RespawnLater(bot, delay));

        IEnumerator RespawnLater(CombatBot bot, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (!bot || Ended) yield break;
            GetSpawn(bot.combatant, out var p, out var r);
            bot.Spawn(p, r);
        }

        public Vector3 RandomSpawnPosition()
        {
            if (spawns.Count == 0) return Vector3.zero;
            return spawns[Random.Range(0, spawns.Count)].transform.position;
        }

        static bool Sees(Combatant c, Vector3 point)
        {
            Vector3 from = c.Head;
            Vector3 d = point - from;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;
            var hits = Physics.RaycastAll(from, d / dist, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
                if (!h.collider.transform.IsChildOf(c.transform)) return false; // something other than the viewer's own body blocks it
            return true;
        }

        /// <summary>Chooses a spawn far from living enemies and out of their sight.</summary>
        public bool GetSpawn(Combatant who, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (spawns.Count == 0) return false;
            var scored = new List<(SpawnPoint sp, float score)>();
            foreach (var sp in spawns)
            {
                float minD = 40f;
                bool seen = false;
                Vector3 p = sp.transform.position + Vector3.up * 1.6f;
                foreach (var c in Combatant.All)
                {
                    if (!c || c == who || !c.alive) continue;
                    float d = Vector3.Distance(c.transform.position, sp.transform.position);
                    minD = Mathf.Min(minD, d);
                    if (d < 45f && Sees(c, p)) seen = true;
                }
                float score = minD - (seen ? 25f : 0f) - (minD < 8f ? 100f : 0f) + Random.value * 5f;
                scored.Add((sp, score));
            }
            scored.Sort((a, b) => b.score.CompareTo(a.score));
            var pick = scored[Random.Range(0, Mathf.Min(3, scored.Count))].sp;
            pos = pick.transform.position;
            if (NavMesh.SamplePosition(pos, out var nh, 3f, NavMesh.AllAreas)) pos = nh.position;
            rot = pick.transform.rotation;
            return true;
        }

        void ReportKill(Combatant killer, Combatant victim, string weapon, bool headshot)
        {
            if (Ended || !victim) return;
            victim.deaths++;
            if (killer && killer != victim) killer.kills++;
            else if (killer == victim) victim.kills = Mathf.Max(0, victim.kills - 1);
            var e = new KillEvent { killer = killer, victim = victim, weapon = string.IsNullOrEmpty(weapon) ? "?" : weapon, headshot = headshot, time = Time.unscaledTime };
            Feed.Add(e);
            if (Feed.Count > 30) Feed.RemoveAt(0);
            Killed?.Invoke(e);
            NetGame.BroadcastKill(e);
            if (killer && killer != victim && killer.kills >= MatchSettings.killLimit) EndMatch();
        }

        /// <summary>Client: a kill the host registered (counts are the host's authoritative values).</summary>
        public void ApplyNetKill(Combatant killer, Combatant victim, string weapon, bool headshot)
        {
            if (Ended) return;
            var e = new KillEvent { killer = killer, victim = victim, weapon = string.IsNullOrEmpty(weapon) ? "?" : weapon, headshot = headshot, time = Time.unscaledTime };
            Feed.Add(e);
            if (Feed.Count > 30) Feed.RemoveAt(0);
            Killed?.Invoke(e);
        }

        /// <summary>Client: the host's clock.</summary>
        public void SetNetTime(float left) { if (!Ended) TimeLeft = left; }

        /// <summary>Client: the host ended the match.</summary>
        public void NetEnd(Combatant winner)
        {
            if (Ended) return;
            EndMatch();
            Winner = winner;
        }

        public Combatant Leader
        {
            get
            {
                Combatant best = null;
                foreach (var c in Combatant.All) if (c && (!best || c.kills > best.kills || (c.kills == best.kills && c.deaths < best.deaths))) best = c;
                return best;
            }
        }

        void EndMatch()
        {
            if (Ended) return;
            Ended = true;
            Winner = Leader;
            if (Net.IsHost) NetGame.BroadcastMatchEnd(Winner);
            EndTime = Time.unscaledTime;
            GameInput.Blocked = true;
            StartCoroutine(SlowMo());
            ProcAudio.Play(Sfx.Kill, 0.8f, 0f);
        }

        IEnumerator SlowMo()
        {
            Time.timeScale = 0.25f;
            yield return new WaitForSecondsRealtime(2f);
            Time.timeScale = Net.Active ? 0.05f : 0f; // the world keeps (barely) ticking for network messages
        }

        void Update()
        {
            if (Ended)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (Net.IsClient) return; // the host restarts the match for everybody
                if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) && Time.unscaledTime - EndTime > 2f) Restart();
                if (Time.unscaledTime - EndTime > 25f) Restart();
                return;
            }
            TimeLeft -= Time.deltaTime;
            if (Net.IsClient) { TimeLeft = Mathf.Max(0f, TimeLeft); return; }
            if (TimeLeft <= 0f) { TimeLeft = 0f; EndMatch(); }
        }

        public void Restart()
        {
            if (Net.IsClient) return;
            Time.timeScale = 1f;
            GameInput.Blocked = false;
            if (Net.IsHost) { NetGame.HostLoadMap(SceneManager.GetActiveScene().name); return; }
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; Time.timeScale = 1f; GameInput.Blocked = false; }
        }
    }
}
