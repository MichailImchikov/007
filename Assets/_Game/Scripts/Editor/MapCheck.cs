using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace FPS.EditorTools
{
    /// <summary>
    /// Automated sanity check of the Deathmatch maps (Tools > FPS > Check Maps): bakes each NavMesh like the match
    /// does, then checks spawn points (on the NavMesh, not inside props, connected to each other), how many random
    /// NavMesh points the ability spawner would reject as unreachable, and bot hitboxes along test rays.
    /// Results go to the console and Logs/fps_check.txt.
    /// </summary>
    public static class MapCheck
    {
        static readonly string[] Maps = { "Map_Port", "Map_Town", "Map_Warehouse", "Map_Plant", "Map_Village", "Map_Flooded" };

        [MenuItem("Tools/FPS/Check Maps", priority = 30)]
        public static void Run()
        {
            var sb = new StringBuilder();
            foreach (var m in Maps)
            {
                string path = "Assets/Scenes/" + m + ".unity";
                if (!File.Exists(path)) { sb.AppendLine(m + ": NOT BUILT"); continue; }
                EditorSceneManager.OpenScene(path);
                CheckMap(m, sb);
            }
            CheckHitboxes(sb);
            string outPath = Path.Combine(Application.dataPath, "../Logs/fps_check.txt");
            File.WriteAllText(outPath, sb.ToString());
            Debug.Log("[FPS] Map check\n" + sb);
        }

        /// <summary>Batch entry: rebuild every scene, then check.</summary>
        public static void BuildAndCheck()
        {
            RangeBuilder.Build();
            RangeBuilder.BuildAll();
            Run();
        }

        /// <summary>Batch entry (with -fpsShots): play the main menu; UIShots takes the screenshots and quits.</summary>
        public static void PlayShots()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            EditorApplication.isPlaying = true;
        }

        static void CheckMap(string name, StringBuilder sb)
        {
            var surface = Object.FindAnyObjectByType<NavMeshSurface>();
            var ccs = Object.FindObjectsByType<CharacterController>();
            foreach (var c in ccs) c.enabled = false;
            var cover = Object.FindObjectsByType<Destructible>();
            foreach (var d in cover) { var dc = d.GetComponent<Collider>(); if (dc) dc.enabled = false; }
            surface.BuildNavMesh();
            foreach (var d in cover) { var dc = d.GetComponent<Collider>(); if (dc) dc.enabled = true; }
            Physics.SyncTransforms();

            var spawns = Object.FindObjectsByType<SpawnPoint>();
            var navSpawns = new List<Vector3>();
            int off = 0, blocked = 0;
            var issues = new List<string>();
            foreach (var sp in spawns)
            {
                Vector3 p = sp.transform.position;
                if (!NavMesh.SamplePosition(p, out var h, 1.2f, NavMesh.AllAreas)) { off++; issues.Add("off-nav " + V(p)); continue; }
                navSpawns.Add(h.position);
                if (Physics.CheckCapsule(h.position + Vector3.up * 0.45f, h.position + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                { blocked++; issues.Add("blocked " + V(p)); }
            }
            var path = new NavMeshPath();
            int disconnected = 0;
            for (int i = 1; i < navSpawns.Count; i++)
                if (!NavMesh.CalculatePath(navSpawns[0], navSpawns[i], NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                { disconnected++; issues.Add("no path spawn0 -> " + V(navSpawns[i])); }

            // ability spawner sampling (same area weighting and filters as AbilitySpawner.Reachable)
            var tri = NavMesh.CalculateTriangulation();
            var cum = new float[tri.indices.Length / 3];
            float total = 0f;
            for (int i = 0; i < cum.Length; i++)
            {
                Vector3 a = tri.vertices[tri.indices[i * 3]], b = tri.vertices[tri.indices[i * 3 + 1]], c = tri.vertices[tri.indices[i * 3 + 2]];
                var cr = Vector3.Cross(b - a, c - a);
                total += cr.normalized.y < 0.8f ? 0f : cr.magnitude * 0.5f;
                cum[i] = total;
            }
            var rnd = new System.Random(3);
            int samples = 600, unreachable = 0, inProp = 0, onCover = 0, ok = 0;
            var highRejected = new List<string>();
            float maxOkY = 0f;
            for (int s = 0; s < samples; s++)
            {
                float r = (float)rnd.NextDouble() * total;
                int lo = 0, hi = cum.Length - 1;
                while (lo < hi) { int mid = (lo + hi) / 2; if (cum[mid] < r) lo = mid + 1; else hi = mid; }
                Vector3 a = tri.vertices[tri.indices[lo * 3]], b = tri.vertices[tri.indices[lo * 3 + 1]], c = tri.vertices[tri.indices[lo * 3 + 2]];
                float u = (float)rnd.NextDouble(), v = (float)rnd.NextDouble();
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                Vector3 p = a + (b - a) * u + (c - a) * v;
                bool conn = false;
                for (int i = 0; i < navSpawns.Count && i < 3 && !conn; i++)
                    conn = NavMesh.CalculatePath(navSpawns[(i * 5) % navSpawns.Count], p, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete;
                if (!conn) { unreachable++; if (p.y > 1.5f && highRejected.Count < 6) highRejected.Add(V(p)); continue; }
                if (Physics.CheckCapsule(p + Vector3.up * 0.45f, p + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) { inProp++; continue; }
                if (Physics.Raycast(p + Vector3.up * 0.3f, Vector3.down, out var down, 0.8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && (down.collider.GetComponentInParent<Destructible>() || down.rigidbody)) { onCover++; continue; }
                ok++;
                maxOkY = Mathf.Max(maxOkY, p.y);
            }
            foreach (var c in ccs) c.enabled = true;

            sb.AppendLine($"{name}: spawns {spawns.Length} (off-nav {off}, blocked {blocked}, disconnected {disconnected}); nav area {total:0} m²");
            sb.AppendLine($"   pickup sampling {samples}: ok {ok}, unreachable {unreachable}, inside props {inProp}, on cover {onCover}; highest accepted y {maxOkY:0.0}");
            if (highRejected.Count > 0) sb.AppendLine("   rejected high points (roofs/containers): " + string.Join("  ", highRejected));
            foreach (var i in issues) sb.AppendLine("   ! " + i);
        }

        static void CheckHitboxes(StringBuilder sb)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Map_Port.unity");
            var mm = Object.FindAnyObjectByType<MatchManager>();
            if (!mm || !mm.botTemplate) { sb.AppendLine("hitboxes: no bot template"); return; }
            var bot = Object.Instantiate(mm.botTemplate);
            bot.transform.position = new Vector3(0f, 200f, 0f);
            bot.transform.rotation = Quaternion.identity;
            bot.SetActive(true);
            var rig = bot.GetComponentInChildren<HumanoidRig>();
            Physics.SyncTransforms();
            sb.AppendLine("hitboxes (ray from the front, bot at y=200, feet at 0):");
            void Ray(string label, Vector3 local)
            {
                Vector3 target = bot.transform.TransformPoint(local);
                Vector3 from = target + Vector3.forward * 5f;
                var hits = Physics.RaycastAll(from, Vector3.back, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                RaycastHit best = default; float bd = float.MaxValue;
                foreach (var h in hits) if (h.distance < bd && h.collider.transform.IsChildOf(bot.transform)) { bd = h.distance; best = h; }
                if (!best.collider) { sb.AppendLine($"   {label,-14} miss"); return; }
                var hb = best.collider.GetComponent<Hitbox>();
                sb.AppendLine($"   {label,-14} {best.collider.name,-16} x{(hb ? hb.multiplier : 0f):0.0}{(hb && hb.critical ? " HEAD" : "")}");
            }
            Vector3 head = bot.transform.InverseTransformPoint(rig.head.position);
            Ray("head center", head + new Vector3(0f, 0.1f, 0f));
            Ray("helmet top", head + new Vector3(0f, 0.235f, 0f));
            Ray("chest", bot.transform.InverseTransformPoint(rig.chest.position) + new Vector3(0f, 0.12f, 0f));
            Ray("shoulder edge", bot.transform.InverseTransformPoint(rig.chest.position) + new Vector3(0.215f, 0.27f, 0f));
            Ray("right hand", bot.transform.InverseTransformPoint(rig.rHand.position) + new Vector3(0f, -0.05f, 0f));
            Ray("left boot", bot.transform.InverseTransformPoint(rig.lFoot.position) + new Vector3(0f, -0.035f, 0.05f));
            Ray("above helmet", head + new Vector3(0f, 0.33f, 0f));
            Object.DestroyImmediate(bot);
        }

        static string V(Vector3 p) => $"({p.x:0.0},{p.y:0.0},{p.z:0.0})";
    }
}
