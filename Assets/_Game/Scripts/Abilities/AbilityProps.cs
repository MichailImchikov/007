using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>Procedural models for abilities: pickup icons, sword, rocket, hook, banana, chicken.</summary>
    public static class AbilityProps
    {
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static Material Mat(Color c, float emission = 0f, float smooth = 0.4f, float metal = 0f)
        {
            string key = $"{c.r:F2}{c.g:F2}{c.b:F2}{c.a:F2}_{emission:F1}_{smooth:F1}_{metal:F1}";
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Ability_" + key };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metal);
            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            mats[key] = m;
            return m;
        }

        public static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m, Vector3? euler = null, bool shadows = true)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler ?? Vector3.zero);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return go.transform;
        }

        public static Transform Root(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        // ------------------------------------------------------------------ weapons / props

        /// <summary>Katana with the blade along local +Y (grip at origin, tip at ~1.0).</summary>
        public static Transform BuildSword(Transform parent, bool shadows = true)
        {
            var root = Root("Sword", parent);
            var steel = Mat(new Color(0.85f, 0.9f, 0.97f), 0.35f, 0.92f, 1f);
            var edge = Mat(new Color(0.75f, 0.9f, 1f), 2.5f, 0.9f, 0.5f);
            var wrap = Mat(new Color(0.08f, 0.06f, 0.07f), 0f, 0.2f);
            var gold = Mat(new Color(0.85f, 0.65f, 0.25f), 0f, 0.7f, 1f);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0.55f, 0f), new Vector3(0.032f, 0.86f, 0.008f), steel, null, shadows);
            Part(PrimitiveType.Cube, root, new Vector3(0.017f, 0.55f, 0f), new Vector3(0.004f, 0.86f, 0.009f), edge, null, shadows);
            Part(PrimitiveType.Cube, root, new Vector3(0f, 1.0f, 0f), new Vector3(0.024f, 0.06f, 0.007f), steel, new Vector3(0f, 0f, 30f), shadows);
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0.115f, 0f), new Vector3(0.09f, 0.008f, 0.07f), gold, null, shadows);
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, -0.02f, 0f), new Vector3(0.03f, 0.12f, 0.03f), wrap, null, shadows);
            Part(PrimitiveType.Sphere, root, new Vector3(0f, -0.145f, 0f), new Vector3(0.035f, 0.02f, 0.035f), gold, null, shadows);
            return root;
        }

        /// <summary>Big cartoon rocket pointing along local +Z (nozzle at origin).</summary>
        public static Transform BuildRocket(Transform parent, out Transform flame)
        {
            var root = Root("Rocket", parent);
            var red = Mat(new Color(0.85f, 0.12f, 0.1f), 0f, 0.55f, 0.3f);
            var white = Mat(new Color(0.92f, 0.92f, 0.9f), 0f, 0.5f);
            var dark = Mat(new Color(0.12f, 0.12f, 0.13f), 0f, 0.5f, 0.8f);
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, 0.45f), new Vector3(0.22f, 0.32f, 0.22f), white, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, 0.3f), new Vector3(0.225f, 0.06f, 0.225f), red, new Vector3(90f, 0f, 0f));
            Part(PrimitiveType.Sphere, root, new Vector3(0f, 0f, 0.8f), new Vector3(0.22f, 0.22f, 0.42f), red);
            Part(PrimitiveType.Cylinder, root, new Vector3(0f, 0f, 0.08f), new Vector3(0.16f, 0.07f, 0.16f), dark, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 4; i++)
                Part(PrimitiveType.Cube, root, Quaternion.Euler(0f, 0f, 45f + i * 90f) * new Vector3(0.15f, 0f, 0f) + new Vector3(0f, 0f, 0.22f),
                     new Vector3(0.14f, 0.015f, 0.22f), red, new Vector3(0f, 0f, 45f + i * 90f));
            flame = Part(PrimitiveType.Sphere, root, new Vector3(0f, 0f, -0.08f), new Vector3(0.14f, 0.14f, 0.3f), Mat(new Color(1f, 0.6f, 0.2f), 6f), null, false);
            return root;
        }

        public static Transform BuildHook(Transform parent)
        {
            var root = Root("Hook", parent);
            var metal = Mat(new Color(0.3f, 0.3f, 0.32f), 0f, 0.7f, 1f);
            Part(PrimitiveType.Cylinder, root, Vector3.zero, new Vector3(0.035f, 0.09f, 0.035f), metal, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 3; i++)
            {
                var arm = Root("Prong", root);
                arm.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                Part(PrimitiveType.Cube, arm, new Vector3(0f, 0.05f, 0.07f), new Vector3(0.02f, 0.1f, 0.02f), metal, new Vector3(-35f, 0f, 0f));
                Part(PrimitiveType.Cube, arm, new Vector3(0f, 0.09f, 0.03f), new Vector3(0.018f, 0.06f, 0.018f), metal, new Vector3(40f, 0f, 0f));
            }
            return root;
        }

        // ------------------------------------------------------------------ pickup icons (≈0.45 m)

        /// <summary>Floating icon for a pickup. The two farts and the trap's disguise look exactly like the real thing.</summary>
        public static Transform BuildIcon(AbilityType t, Transform parent)
        {
            var info = AbilityDB.Get(t);
            var root = Root("Icon", parent);
            var glow = Mat(info.color, 2.2f, 0.5f);
            var dark = Mat(new Color(0.12f, 0.12f, 0.14f), 0f, 0.5f, 0.6f);
            switch (t)
            {
                case AbilityType.Grapple:
                {
                    var h = BuildHook(root);
                    h.localScale = Vector3.one * 2.4f;
                    h.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    foreach (var r in h.GetComponentsInChildren<Renderer>()) r.sharedMaterial = glow;
                    for (int i = 0; i < 5; i++) Part(PrimitiveType.Cylinder, root, new Vector3(0f, -0.16f - i * 0.05f, Mathf.Sin(i) * 0.03f), new Vector3(0.02f, 0.025f, 0.02f), dark);
                    break;
                }
                case AbilityType.Parry:
                {
                    var s = BuildSword(root);
                    s.localScale = Vector3.one * 0.5f;
                    s.localPosition = new Vector3(0f, -0.22f, 0f);
                    s.localRotation = Quaternion.Euler(0f, 0f, 25f);
                    Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.05f, 0f), new Vector3(0.2f, 0.2f, 0.02f), Mat(info.color * new Color(1, 1, 1, 1), 1.5f), null);
                    break;
                }
                case AbilityType.ButtRocket:
                {
                    var r = BuildRocket(root, out _);
                    r.localScale = Vector3.one * 0.45f;
                    r.localRotation = Quaternion.Euler(-60f, 0f, 0f);
                    r.localPosition = new Vector3(0f, -0.15f, 0.05f);
                    break;
                }
                case AbilityType.Invisibility:
                {
                    var ghost = Mat(new Color(info.color.r, info.color.g, info.color.b, 1f), 1.6f, 0.9f);
                    Part(PrimitiveType.Capsule, root, new Vector3(0f, -0.05f, 0f), new Vector3(0.18f, 0.16f, 0.12f), ghost);
                    Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.15f, 0f), new Vector3(0.14f, 0.14f, 0.14f), ghost);
                    Part(PrimitiveType.Cube, root, new Vector3(0.06f, 0.16f, 0.06f), new Vector3(0.03f, 0.04f, 0.02f), dark);
                    Part(PrimitiveType.Cube, root, new Vector3(-0.06f, 0.16f, 0.06f), new Vector3(0.03f, 0.04f, 0.02f), dark);
                    break;
                }
                case AbilityType.Phase:
                {
                    for (int i = 0; i < 12; i++)
                    {
                        float a = i / 12f * Mathf.PI * 2f;
                        Part(PrimitiveType.Cube, root, new Vector3(Mathf.Cos(a) * 0.2f, Mathf.Sin(a) * 0.2f, 0f), new Vector3(0.07f, 0.04f, 0.04f), glow, new Vector3(0f, 0f, a * Mathf.Rad2Deg + 90f));
                    }
                    Part(PrimitiveType.Cube, root, Vector3.zero, new Vector3(0.24f, 0.24f, 0.02f), Mat(new Color(0.3f, 0.1f, 0.5f), 3f));
                    break;
                }
                case AbilityType.FartBlind:
                case AbilityType.FartSmoke:
                {
                    var fart = Mat(AbilityDB.Get(AbilityType.FartBlind).color, 1.2f, 0.2f);
                    Part(PrimitiveType.Sphere, root, new Vector3(-0.08f, 0f, 0f), Vector3.one * 0.2f, fart);
                    Part(PrimitiveType.Sphere, root, new Vector3(0.09f, 0.03f, 0f), Vector3.one * 0.22f, fart);
                    Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.12f, 0.02f), Vector3.one * 0.18f, fart);
                    Part(PrimitiveType.Sphere, root, new Vector3(0.02f, -0.08f, 0.05f), Vector3.one * 0.15f, fart);
                    break;
                }
                case AbilityType.Swap:
                {
                    Part(PrimitiveType.Cube, root, new Vector3(0f, 0.07f, 0f), new Vector3(0.32f, 0.05f, 0.05f), glow);
                    Part(PrimitiveType.Cube, root, new Vector3(0.14f, 0.1f, 0f), new Vector3(0.1f, 0.04f, 0.05f), glow, new Vector3(0f, 0f, -40f));
                    Part(PrimitiveType.Cube, root, new Vector3(0f, -0.07f, 0f), new Vector3(0.32f, 0.05f, 0.05f), glow);
                    Part(PrimitiveType.Cube, root, new Vector3(-0.14f, -0.1f, 0f), new Vector3(0.1f, 0.04f, 0.05f), glow, new Vector3(0f, 0f, -40f));
                    break;
                }
                case AbilityType.Shrink:
                    Part(PrimitiveType.Capsule, root, new Vector3(-0.1f, -0.12f, 0f), new Vector3(0.07f, 0.08f, 0.07f), glow);
                    Part(PrimitiveType.Cube, root, new Vector3(0.1f, 0f, 0f), new Vector3(0.05f, 0.08f, 0.32f), dark, new Vector3(0f, 0f, 0f));
                    Part(PrimitiveType.Cube, root, new Vector3(0.1f, -0.08f, -0.08f), new Vector3(0.05f, 0.12f, 0.05f), dark);
                    break;
                case AbilityType.BigHeads:
                    Part(PrimitiveType.Sphere, root, new Vector3(0f, 0.08f, 0f), Vector3.one * 0.32f, glow);
                    Part(PrimitiveType.Capsule, root, new Vector3(0f, -0.18f, 0f), new Vector3(0.08f, 0.08f, 0.08f), Mat(info.color, 0.5f));
                    break;
                case AbilityType.Screamer:
                {
                    var pale = Mat(new Color(0.85f, 0.85f, 0.8f), 0.6f, 0.3f);
                    Part(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(0.28f, 0.34f, 0.26f), pale);
                    Part(PrimitiveType.Sphere, root, new Vector3(0.06f, 0.05f, 0.11f), new Vector3(0.07f, 0.09f, 0.05f), dark);
                    Part(PrimitiveType.Sphere, root, new Vector3(-0.06f, 0.05f, 0.11f), new Vector3(0.07f, 0.09f, 0.05f), dark);
                    Part(PrimitiveType.Sphere, root, new Vector3(0f, -0.08f, 0.11f), new Vector3(0.09f, 0.13f, 0.05f), Mat(new Color(0.3f, 0f, 0f), 1f));
                    break;
                }
                case AbilityType.Confuse:
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i * 0.7f;
                        Part(PrimitiveType.Cube, root, new Vector3(Mathf.Cos(a) * i * 0.022f, Mathf.Sin(a) * i * 0.022f, 0f), Vector3.one * 0.05f, glow, new Vector3(0f, 0f, a * Mathf.Rad2Deg));
                    }
                    break;
                case AbilityType.Launch:
                    for (int i = 0; i < 5; i++) Part(PrimitiveType.Cylinder, root, new Vector3(0f, -0.2f + i * 0.05f, 0f), new Vector3(0.18f, 0.008f, 0.18f), dark);
                    Part(PrimitiveType.Cube, root, new Vector3(0f, 0.12f, 0f), new Vector3(0.05f, 0.18f, 0.05f), glow);
                    Part(PrimitiveType.Cube, root, new Vector3(0.04f, 0.2f, 0f), new Vector3(0.04f, 0.1f, 0.04f), glow, new Vector3(0f, 0f, 45f));
                    Part(PrimitiveType.Cube, root, new Vector3(-0.04f, 0.2f, 0f), new Vector3(0.04f, 0.1f, 0.04f), glow, new Vector3(0f, 0f, -45f));
                    break;
                case AbilityType.Minesweeper:
                    for (int x = 0; x < 4; x++)
                        for (int y = 0; y < 4; y++)
                            Part(PrimitiveType.Cube, root, new Vector3((x - 1.5f) * 0.085f, (y - 1.5f) * 0.085f, 0f), new Vector3(0.075f, 0.075f, 0.03f),
                                 (x + y) % 3 == 0 ? Mat(new Color(0.85f, 0.85f, 0.88f), 0.6f) : Mat(new Color(0.55f, 0.57f, 0.6f), 0.4f));
                    Part(PrimitiveType.Sphere, root, new Vector3(0.04f, 0.04f, 0.04f), Vector3.one * 0.07f, dark);
                    Part(PrimitiveType.Sphere, root, new Vector3(0.04f, 0.04f, 0.075f), Vector3.one * 0.02f, Mat(new Color(1f, 0.1f, 0.1f), 4f));
                    break;
                case AbilityType.Build:
                {
                    var wood = Mat(new Color(0.75f, 0.52f, 0.28f), 0.7f, 0.3f);
                    for (int i = 0; i < 3; i++) Part(PrimitiveType.Cube, root, new Vector3(0f, -0.12f + i * 0.12f, 0f), new Vector3(0.36f, 0.1f, 0.03f), wood, new Vector3(0f, 0f, i == 1 ? 3f : -2f));
                    Part(PrimitiveType.Cube, root, new Vector3(0.1f, 0f, 0.05f), new Vector3(0.03f, 0.34f, 0.02f), dark);
                    break;
                }
                case AbilityType.BSOD:
                    Part(PrimitiveType.Cube, root, new Vector3(0f, 0.04f, 0f), new Vector3(0.38f, 0.24f, 0.02f), Mat(new Color(0.05f, 0.45f, 0.9f), 2.5f));
                    Part(PrimitiveType.Cube, root, new Vector3(0f, 0.04f, -0.015f), new Vector3(0.4f, 0.26f, 0.02f), dark);
                    Part(PrimitiveType.Cube, root, new Vector3(0f, -0.13f, 0f), new Vector3(0.04f, 0.1f, 0.04f), dark);
                    Part(PrimitiveType.Cube, root, new Vector3(0f, -0.18f, 0f), new Vector3(0.16f, 0.015f, 0.08f), dark);
                    break;
                case AbilityType.Spin:
                    for (int i = 0; i < 3; i++)
                    {
                        var arm = Root("Arm", root);
                        arm.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                        Part(PrimitiveType.Cube, arm, new Vector3(0.12f, 0f, 0f), new Vector3(0.2f, 0.05f, 0.04f), glow);
                        Part(PrimitiveType.Cube, arm, new Vector3(0.2f, 0.05f, 0f), new Vector3(0.05f, 0.12f, 0.04f), glow);
                    }
                    Part(PrimitiveType.Sphere, root, Vector3.zero, Vector3.one * 0.1f, dark);
                    break;
                default:
                    Part(PrimitiveType.Cube, root, Vector3.zero, Vector3.one * 0.25f, glow, new Vector3(45f, 0f, 45f));
                    break;
            }
            return root;
        }
    }
}
