using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace FPS
{
    /// <summary>
    /// "Drone war" rules on top of the Deathmatch match: knife + pistol only, everyone has the drone
    /// ability (DroneOperator), no ability pickups. Keeps the wind, the geofence and the water line for
    /// drone physics, and the "operator revealed" marks shown after a drone is brought down by an enemy.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class DroneMode : MonoBehaviour
    {
        public class RevealMark
        {
            public Combatant owner, by;
            public Vector3 center;
            public float radius, born, until;
        }

        public const float RevealTime = 9f, RevealRadius = 14f;

        public static DroneMode Instance { get; private set; }
        public static bool Active => MatchSettings.mode == GameMode.Drones && MatchManager.Instance && !Net.Active;
        public static Vector3 Wind { get; private set; }
        public static float WaterLevel { get; private set; } = -10000f;
        public static Bounds Bounds { get; private set; }
        public static readonly List<RevealMark> Reveals = new List<RevealMark>();
        public static event Action<RevealMark> Revealed;
        /// <summary>Invisible blockers drones fly through (collisions ignored, rays skip them).</summary>
        public static readonly HashSet<Collider> Ghosts = new HashSet<Collider>();
        public static bool IsGhost(Collider c) => c && Ghosts.Contains(c);

        float scanT;

        public static DroneMode Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("DroneMode");
            Instance = go.AddComponent<DroneMode>();
            go.AddComponent<DroneThermal>();
            go.AddComponent<DroneHUD>();
            return Instance;
        }

        public static string RandomPistol()
        {
            var list = new List<string>();
            foreach (var d in WeaponCatalog.All) if (d.category == WeaponCategory.Pistol) list.Add(d.name);
            return list.Count > 0 ? list[Random.Range(0, list.Count)] : "P-17";
        }

        void Awake()
        {
            Instance = this;
            Reveals.Clear();
            float a = Random.value * Mathf.PI * 2f;
            Wind = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Random.Range(1.2f, 3.8f);
            WaterLevel = DetectWater();

            // invisible walls (map fence, helper blockers) stop people, not drones: the geofence handles drones
            Ghosts.Clear();
            Bounds fence = default; bool hasFence = false;
            foreach (var t in FindObjectsByType<Transform>())
            {
                if (t.name != "Bounds") continue;
                foreach (var c in t.GetComponentsInChildren<Collider>())
                {
                    Ghosts.Add(c);
                    if (!hasFence) { fence = c.bounds; hasFence = true; } else fence.Encapsulate(c.bounds);
                }
            }
            foreach (var r in FindObjectsByType<Renderer>())
            {
                if (!r || r.enabled || !r.sharedMaterial || !r.sharedMaterial.name.StartsWith("HLP_")) continue;
                foreach (var c in r.GetComponents<Collider>()) Ghosts.Add(c);
            }

            // geofence: the fenced play area (or the spawn area plus a margin), up to ~70 m above the spawns
            var spawns = FindObjectsByType<SpawnPoint>();
            if (hasFence)
            {
                Vector3 min = fence.min + new Vector3(2f, 0f, 2f), max = fence.max - new Vector3(2f, 0f, 2f);
                min.y = -100f; max.y = Mathf.Max(max.y, 90f);
                fence.SetMinMax(min, max);
                Bounds = fence;
            }
            else if (spawns.Length > 0)
            {
                var b = new Bounds(spawns[0].transform.position, Vector3.zero);
                foreach (var s in spawns) b.Encapsulate(s.transform.position);
                Vector3 min = b.min - new Vector3(35f, 15f, 35f), max = b.max + new Vector3(35f, 70f, 35f);
                b.SetMinMax(min, max);
                Bounds = b;
            }
            else Bounds = new Bounds(Vector3.zero, Vector3.zero);
        }

        static float DetectWater()
        {
            // the flooded village: the flood plane is the lowest big "water" renderer
            float best = -10000f;
            foreach (var r in FindObjectsByType<MeshRenderer>())
            {
                if (!r || !r.enabled) continue;
                string n = r.gameObject.name.ToLowerInvariant();
                if (!n.Contains("water")) continue;
                var b = r.bounds;
                if (b.size.x < 40f || b.size.z < 40f || b.size.y > 3f) continue;
                if (best < -9999f || b.center.y < best) best = b.center.y;
            }
            if (best < -9999f && SceneManager.GetActiveScene().name == "Map_Flooded") best = 15.9f;
            return best;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameInput.Piloting = false;
            Reveals.Clear();
        }

        void Update()
        {
            // every local combatant gets the drone ability (bots respawn as the same objects)
            scanT -= Time.deltaTime;
            if (scanT <= 0f)
            {
                scanT = 0.5f;
                foreach (var c in Combatant.All)
                    if (c && !c.remote && !c.GetComponent<DroneOperator>()) c.gameObject.AddComponent<DroneOperator>();
                if (AbilityHUD.Instance && AbilityHUD.Instance.gameObject.activeSelf) AbilityHUD.Instance.gameObject.SetActive(false);
            }
            for (int i = Reveals.Count - 1; i >= 0; i--)
                if (Time.time > Reveals[i].until) Reveals.RemoveAt(i);
        }

        /// <summary>
        /// An enemy brought a drone down: the area around its operator is shown to every player for a few
        /// seconds (not the exact spot), and bots go hunting there.
        /// </summary>
        public static void Reveal(Combatant owner, Combatant by)
        {
            if (!owner || !owner.alive) return;
            Vector2 off = Random.insideUnitCircle * (RevealRadius * 0.55f);
            var m = new RevealMark
            {
                owner = owner, by = by,
                center = owner.transform.position + new Vector3(off.x, 0f, off.y),
                radius = RevealRadius, born = Time.time, until = Time.time + RevealTime,
            };
            Reveals.RemoveAll(r => r.owner == owner);
            Reveals.Add(m);
            Revealed?.Invoke(m);
            AIEvents.Emit(m.center, 1000f, owner);
        }
    }
}
