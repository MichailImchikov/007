using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FPS.EditorTools
{
    /// <summary>
    /// "Пустынная база": a map cut out of the ithappy Military Desert demo, a whole air base on ~1.5 km of dunes. The
    /// pack-map pipeline (MapBuilderPacks: copy, demo rig strip, fence, NavMesh, spawns, match stack) does the work; the
    /// fix-ups here trim the demo around the play area — the vehicle depot with its rows of tanks, the long hangar, the
    /// helipad, the tent rows and the HESCO walls:
    ///  - the spline traffic rig goes, parked vehicles stop animating;
    ///  - far clutter is removed, scenery beyond the fence keeps its looks without colliders (the base, hangars, dunes);
    ///  - soft things (grass, bushes, wires, flags, rotors) and anything that moves become walk- and shoot-through
    ///    (the pack puts a mesh collider on every object).
    /// </summary>
    public static partial class RangeBuilder
    {
        const string DesertSource = "Assets/ithappy/Military/Scenes/URP_Scenes/Demonstration_URP.unity";
        // ~105 m square (a medium map): the oval tents, the long hangar, the helipad, the west two thirds of the depot and
        // the south rows of the tent town
        static readonly Rect DesertCore = Rect.MinMaxRect(150f, -132f, 255f, -27f);
        const float DesertPad = 6f;

        // lower-case name parts of things that keep their looks but stop blocking
        static readonly string[] DesertSoftNames = { "grass", "bush", "wire", "flag", "blade", "cable", "antenna", "rope", "decal", "leaf", "leaves", "plant", "palm" };
        // scripts that move their object (pack demo decoration: radars, windsocks, rotors)
        static readonly string[] DesertMovers = { "RotationScript", "OscillateRotation", "OscillatePosition" };

        [MenuItem("Tools/FPS/Maps/Packs/Desert Base")] static void MPack7() => BuildPackDesertBase();

        /// <summary>Hot afternoon: high warm sun, blue sky, sandy haze far out.</summary>
        static void BuildPackDesertBase() => BuildFromPack(new PackMap
        {
            source = DesertSource, scene = "Map_DesertBase", title = "Пустынная база",
            core = DesertCore, pad = DesertPad, spawns = 16,
            light = new LightPreset
            {
                // the pack's sand and concrete are nearly white: a modest sun and a dark exposure keep them sandy, not blown out
                name = "DesertBase", sunIntensity = 1.25f, sunColor = new Color(1f, 0.92f, 0.78f), sunEuler = new Vector3(48f, -135f, 0f),
                ambSky = new Color(0.62f, 0.7f, 0.82f), ambEquator = new Color(0.7f, 0.62f, 0.5f), ambGround = new Color(0.5f, 0.41f, 0.29f),
                fogColor = new Color(0.86f, 0.8f, 0.68f), fogStart = 90f, fogEnd = 560f,
                skyTint = new Color(0.45f, 0.55f, 0.7f), atmosphere = 0.9f, skyExposure = 1.35f,
                exposure = -0.6f, contrast = 15f, saturation = 4f, bloom = 0.7f,
            },
            fixups = DesertFixups,
        });

        static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        static void DesertFixups(Scene scene)
        {
            var play = Grow(DesertCore, DesertPad);
            var near = Grow(play, 35f);   // small things stay in view this far beyond the fence
            var mid = Grow(play, 280f);   // buildings, tents and vehicles stay in view this far
            int removed = 0, scenery = 0, soft = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "Road_Splines") { Object.DestroyImmediate(root); continue; }
                bool vehicles = root.name == "Vehicles";
                if (!vehicles && root.name != "Demonstration") continue;
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var go = root.transform.GetChild(i).gameObject;
                    if (!DesertBounds(go, out var b)) continue;
                    var r = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
                    if (r.Overlaps(play))
                    {
                        // parked traffic: the wheels / turret animation would run on a standing vehicle
                        if (vehicles) foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                        soft += DesertSoften(go);
                        continue;
                    }
                    float size = Mathf.Max(b.size.x, b.size.z);
                    bool keep = r.Overlaps(near) || (r.Overlaps(mid) && size >= 3.5f) || size >= 60f;
                    if (!keep) { PackRemove(go); removed++; continue; }
                    foreach (var c in go.GetComponentsInChildren<Collider>(true)) if (c.enabled) { c.enabled = false; scenery++; }
                }
            }
            Debug.Log($"[FPS] Map_DesertBase fix-ups: {removed} far objects removed, {scenery} scenery colliders off, {soft} soft / moving colliders off");
        }

        static bool DesertBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any;
        }

        /// <summary>Switches off the colliders of soft and moving parts under <paramref name="go"/>; returns how many.</summary>
        static int DesertSoften(GameObject go)
        {
            int n = 0;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                if (!c.enabled) continue;
                bool off = false;
                string name = c.name.ToLowerInvariant();
                foreach (var s in DesertSoftNames) if (name.Contains(s)) { off = true; break; }
                if (!off)
                {
                    // wires and rods (two thin dimensions), tiny clutter
                    Vector3 e = c.bounds.size;
                    float lo = Mathf.Min(e.x, Mathf.Min(e.y, e.z)), hi = Mathf.Max(e.x, Mathf.Max(e.y, e.z)), mid = e.x + e.y + e.z - lo - hi;
                    off = hi < 0.15f || (mid < 0.12f && hi > 1f);
                }
                if (!off)
                {
                    // animated decoration (the parked vehicles' animators are already off and stay solid)
                    var anim = c.GetComponentInParent<Animator>(true);
                    off = (anim && anim.enabled) || DesertMoves(c.transform);
                }
                if (off) { c.enabled = false; n++; }
            }
            return n;
        }

        static bool DesertMoves(Transform t)
        {
            for (; t; t = t.parent)
                foreach (var mb in t.GetComponents<MonoBehaviour>())
                    if (mb && System.Array.IndexOf(DesertMovers, mb.GetType().Name) >= 0) return true;
            return false;
        }
    }
}
