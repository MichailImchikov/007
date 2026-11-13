using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Appearance of a procedural soldier: nickname, colors, headwear, vest, face gear, backpack, build.
    /// Compact (a dozen bytes) so it travels in the connection payload and the network roster.
    /// Applied to any HumanoidRig by recoloring and toggling its named meshes plus a few extra parts.
    /// The local player's choice is saved in PlayerPrefs.
    /// </summary>
    [Serializable]
    public class CharacterLook
    {
        public string nick = "Игрок";
        public byte skin = 1, shirt = 0, pants = 0, vest = 4, helmet = 0, gear = 6;
        public byte headwear;   // see HeadwearNames
        public byte vestType;   // see VestNames
        public byte face;       // see FaceNames
        public byte backpack;   // 0 yes, 1 no
        public byte build;      // see BuildNames
        /// <summary>0 = procedural soldier, i = imported model CharacterModelDB.entries[i - 1].</summary>
        public byte model = DefaultModel;

        /// <summary>Default for new / old saves: the first imported model if the pack is present.</summary>
        public const byte DefaultModel = 1;

        public const int MaxNick = 16;

        // ------------------------------------------------------------------ option tables

        public static readonly Color[] SkinTones =
        {
            new Color(0.93f, 0.76f, 0.64f), new Color(0.78f, 0.6f, 0.48f), new Color(0.66f, 0.47f, 0.34f),
            new Color(0.52f, 0.36f, 0.25f), new Color(0.38f, 0.26f, 0.18f), new Color(0.26f, 0.18f, 0.13f),
        };
        public static readonly string[] SkinNames = { "Светлая", "Средняя", "Смуглая", "Оливковая", "Тёмная", "Очень тёмная" };

        public static readonly Color[] Colors =
        {
            new Color(0.46f, 0.43f, 0.33f), new Color(0.36f, 0.34f, 0.27f), new Color(0.62f, 0.55f, 0.4f), new Color(0.5f, 0.4f, 0.27f),
            new Color(0.26f, 0.29f, 0.2f), new Color(0.2f, 0.24f, 0.17f), new Color(0.14f, 0.14f, 0.13f), new Color(0.07f, 0.07f, 0.08f),
            new Color(0.4f, 0.42f, 0.44f), new Color(0.22f, 0.25f, 0.31f), new Color(0.12f, 0.16f, 0.3f), new Color(0.82f, 0.84f, 0.86f),
            new Color(0.45f, 0.1f, 0.1f), new Color(0.8f, 0.18f, 0.12f), new Color(0.95f, 0.5f, 0.1f), new Color(0.9f, 0.78f, 0.2f),
            new Color(0.15f, 0.5f, 0.25f), new Color(0.15f, 0.4f, 0.85f), new Color(0.5f, 0.2f, 0.65f), new Color(0.9f, 0.4f, 0.6f),
        };
        public static readonly string[] ColorNames =
        {
            "Хаки", "Тёмный хаки", "Песок", "Койот", "Олива", "Тёмная олива", "Графит", "Чёрный",
            "Серый", "Серо-синий", "Тёмно-синий", "Белый", "Бордовый", "Красный", "Оранжевый", "Жёлтый",
            "Зелёный", "Синий", "Фиолетовый", "Розовый",
        };

        public static readonly string[] HeadwearNames = { "Каска", "Кепка", "Берет", "Панама", "Шапка", "Без головного убора" };
        public static readonly string[] VestNames = { "Бронежилет", "Разгрузка", "Без снаряжения" };
        public static readonly string[] FaceNames = { "Очки и маска", "Очки", "Маска", "Ничего" };
        public static readonly string[] BackpackNames = { "Есть", "Нет" };
        public static readonly string[] BuildNames = { "Обычное", "Худощавое", "Крепкое" };

        /// <summary>Number of model choices (procedural + imported).</summary>
        public static int ModelCount => 1 + CharacterModelDB.Count;
        public static string ModelName(int i)
        {
            if (i <= 0) return "Классический (настраиваемый)";
            var e = CharacterModelDB.Get(i);
            return e != null ? e.name : "?";
        }
        /// <summary>True when the procedural options (colors, headwear, vest...) are visible on this look.</summary>
        public bool Procedural => CharacterModelDB.Get(model) == null;

        // ------------------------------------------------------------------ persistence

        const string PrefKey = "fps.look.v1", NickKey = "fps.nick";
        static CharacterLook local;

        /// <summary>The local player's saved look (loaded once).</summary>
        public static CharacterLook Local
        {
            get
            {
                if (local != null) return local;
                local = new CharacterLook();
                try
                {
                    string s = PlayerPrefs.GetString(PrefKey, "");
                    if (!string.IsNullOrEmpty(s)) local.SetBytes(Convert.FromBase64String(s));
                    local.nick = SanitizeNick(PlayerPrefs.GetString(NickKey, "Игрок" + UnityEngine.Random.Range(10, 100)));
                }
                catch (Exception) { local = new CharacterLook(); }
                return local;
            }
        }

        public static void SaveLocal()
        {
            if (local == null) return;
            local.nick = SanitizeNick(local.nick);
            PlayerPrefs.SetString(PrefKey, Convert.ToBase64String(local.GetBytes()));
            PlayerPrefs.SetString(NickKey, local.nick);
            PlayerPrefs.Save();
        }

        public static string SanitizeNick(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Игрок";
            s = s.Replace("<", "").Replace(">", "").Replace("\n", " ").Replace("\r", "").Trim();
            if (s.Length > MaxNick) s = s.Substring(0, MaxNick);
            return s.Length == 0 ? "Игрок" : s;
        }

        // ------------------------------------------------------------------ serialization (without nick)

        public byte[] GetBytes() => new[] { (byte)2, skin, shirt, pants, vest, helmet, gear, headwear, vestType, face, backpack, build, model };

        public void SetBytes(byte[] b)
        {
            if (b == null || b.Length < 12 || (b[0] != 1 && b[0] != 2)) return;
            skin = Clamp(b[1], SkinTones.Length); shirt = Clamp(b[2], Colors.Length); pants = Clamp(b[3], Colors.Length);
            vest = Clamp(b[4], Colors.Length); helmet = Clamp(b[5], Colors.Length); gear = Clamp(b[6], Colors.Length);
            headwear = Clamp(b[7], HeadwearNames.Length); vestType = Clamp(b[8], VestNames.Length); face = Clamp(b[9], FaceNames.Length);
            backpack = Clamp(b[10], BackpackNames.Length); build = Clamp(b[11], BuildNames.Length);
            model = b[0] >= 2 && b.Length >= 13 ? b[12] : DefaultModel;
            if (model >= ModelCount) model = (byte)(ModelCount > 1 ? DefaultModel : 0);
        }

        static byte Clamp(byte v, int n) => (byte)(v < n ? v : 0);

        public CharacterLook Clone()
        {
            var c = new CharacterLook { nick = nick };
            c.SetBytes(GetBytes());
            return c;
        }

        /// <summary>Random but tasteful soldier (bots): mostly military colors, sometimes a bright accent.</summary>
        public static CharacterLook Random(System.Random r, string nick)
        {
            int Mil() => r.Next(0, 11);
            var l = new CharacterLook
            {
                nick = nick,
                skin = (byte)r.Next(SkinTones.Length),
                shirt = (byte)Mil(), pants = (byte)Mil(), vest = (byte)Mil(), helmet = (byte)Mil(),
                gear = (byte)(r.NextDouble() < 0.35 ? r.Next(12, Colors.Length) : r.Next(0, 9)),
                headwear = (byte)(r.NextDouble() < 0.45 ? 0 : r.Next(HeadwearNames.Length)),
                vestType = (byte)(r.NextDouble() < 0.6 ? 0 : r.Next(VestNames.Length)),
                face = (byte)r.Next(FaceNames.Length),
                backpack = (byte)(r.NextDouble() < 0.7 ? 0 : 1),
                build = (byte)r.Next(BuildNames.Length),
                // mostly imported soldiers, sometimes the classic procedural one
                model = (byte)(ModelCount > 1 && r.NextDouble() < 0.85 ? r.Next(1, ModelCount) : 0),
            };
            return l;
        }

        // ------------------------------------------------------------------ materials

        static readonly Dictionary<int, Material> matCache = new Dictionary<int, Material>();
        static Shader lit;

        public static Material Mat(Color c, float metal, float smooth)
        {
            int key = ((int)(c.r * 255f) << 24) ^ ((int)(c.g * 255f) << 16) ^ ((int)(c.b * 255f) << 8) ^ (int)(metal * 10f) * 31 ^ (int)(smooth * 100f) * 7919;
            if (matCache.TryGetValue(key, out var m) && m) return m;
            if (!lit) lit = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(lit) { name = "Look_" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metal);
            m.SetFloat("_Smoothness", smooth);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            matCache[key] = m;
            return m;
        }

        Color SkinC => SkinTones[skin % SkinTones.Length];
        static Color C(byte i) => Colors[i % Colors.Length];

        /// <summary>Palette for HumanoidRig.Build (runtime characters: network proxies, menu preview).</summary>
        public HumanoidRig.Palette Palette() => new HumanoidRig.Palette
        {
            skin = Mat(SkinC, 0f, 0.35f),
            shirt = Mat(C(shirt), 0f, 0.15f),
            pants = Mat(C(pants), 0f, 0.15f),
            vest = Mat(C(vest), 0f, 0.2f),
            boots = Mat(new Color(0.2f, 0.15f, 0.1f), 0f, 0.3f),
            gloves = Mat(new Color(0.07f, 0.07f, 0.075f), 0f, 0.28f),
            helmet = Mat(C(helmet), 0.1f, 0.35f),
            gear = Mat(C(gear), 0f, 0.25f),
        };

        // ------------------------------------------------------------------ applying to a rig

        enum Slot { None, Skin, Shirt, Pants, Vest, Helmet, Gear }

        static Slot SlotOf(string n)
        {
            switch (n)
            {
                case "NeckMesh": case "HeadMesh": return Slot.Skin;
                case "Abdomen": case "Torso": case "Shoulders":
                case "L_UpperArmMesh": case "L_ForearmMesh": case "R_UpperArmMesh": case "R_ForearmMesh": return Slot.Shirt;
                case "Pelvis": case "L_ThighMesh": case "L_ShinMesh": case "R_ThighMesh": case "R_ShinMesh": return Slot.Pants;
                case "Vest": return Slot.Vest;
                case "Helmet": case "HelmetRim": return Slot.Helmet;
                case "Belt": case "PouchL": case "PouchR": case "Backpack": case "Goggles": case "Mask": case "L_KneePad": case "R_KneePad": return Slot.Gear;
            }
            return Slot.None;
        }

        /// <summary>Remembers the original part scales so a look can be re-applied any number of times.</summary>
        class LookState : MonoBehaviour
        {
            public readonly Dictionary<Transform, Vector3> baseScale = new Dictionary<Transform, Vector3>();
            public readonly List<GameObject> extras = new List<GameObject>();
        }

        static readonly HashSet<string> TorsoParts = new HashSet<string> { "Pelvis", "Abdomen", "Torso", "Vest", "Shoulders", "Belt" };
        static readonly HashSet<string> LimbParts = new HashSet<string>
        {
            "L_UpperArmMesh", "L_ForearmMesh", "R_UpperArmMesh", "R_ForearmMesh", "L_ThighMesh", "L_ShinMesh", "R_ThighMesh", "R_ShinMesh"
        };

        public void Apply(HumanoidRig rig)
        {
            if (!rig) return;
            var st = rig.GetComponent<LookState>();
            if (!st) st = rig.gameObject.AddComponent<LookState>();
            foreach (var e in st.extras) if (e) UnityEngine.Object.Destroy(e);
            st.extras.Clear();

            var pal = Palette();
            float torsoMul = build == 1 ? 0.88f : build == 2 ? 1.14f : 1f;
            float limbMul = build == 1 ? 0.88f : build == 2 ? 1.12f : 1f;

            foreach (var tr in rig.GetComponentsInChildren<Transform>(true))
            {
                if (!tr || tr == rig.transform) continue;
                string n = tr.name;
                var r = tr.GetComponent<Renderer>();
                if (r)
                {
                    switch (SlotOf(n))
                    {
                        case Slot.Skin: r.sharedMaterial = pal.skin; break;
                        case Slot.Shirt: r.sharedMaterial = pal.shirt; break;
                        case Slot.Pants: r.sharedMaterial = pal.pants; break;
                        case Slot.Vest: r.sharedMaterial = pal.vest; break;
                        case Slot.Helmet: r.sharedMaterial = pal.helmet; break;
                        case Slot.Gear: r.sharedMaterial = pal.gear; break;
                    }
                }
                bool torso = TorsoParts.Contains(n) || n == "Backpack", limb = LimbParts.Contains(n);
                if (torso || limb)
                {
                    if (!st.baseScale.TryGetValue(tr, out var bs)) st.baseScale[tr] = bs = tr.localScale;
                    float m = torso ? torsoMul : limbMul;
                    tr.localScale = new Vector3(bs.x * m, bs.y, bs.z * (torso && n != "Backpack" ? Mathf.Lerp(1f, m, 0.6f) : m));
                }
                switch (n)
                {
                    case "Helmet": case "HelmetRim": tr.gameObject.SetActive(headwear == 0); break;
                    case "Vest": tr.gameObject.SetActive(vestType == 0); break;
                    case "PouchL": case "PouchR": tr.gameObject.SetActive(vestType != 2); break;
                    case "Backpack": tr.gameObject.SetActive(backpack == 0); break;
                    case "Goggles": tr.gameObject.SetActive(face == 0 || face == 1); break;
                    case "Mask": tr.gameObject.SetActive(face == 0 || face == 2); break;
                }
            }

            // extra headwear on the head bone (head mesh: center (0, 0.1, 0.01), size 0.2 x 0.24 x 0.22)
            if (rig.head)
            {
                var hat = pal.helmet;
                var hair = Mat(new Color(0.12f, 0.09f, 0.07f), 0f, 0.2f);
                switch (headwear)
                {
                    case 1: // cap
                        Extra(st, rig.head, PrimitiveType.Sphere, new Vector3(0f, 0.165f, 0f), new Vector3(0.215f, 0.13f, 0.235f), Vector3.zero, hat);
                        Extra(st, rig.head, PrimitiveType.Cube, new Vector3(0f, 0.15f, 0.125f), new Vector3(0.17f, 0.015f, 0.11f), new Vector3(-12f, 0f, 0f), hat);
                        break;
                    case 2: // beret
                        Extra(st, rig.head, PrimitiveType.Sphere, new Vector3(0.025f, 0.195f, -0.01f), new Vector3(0.245f, 0.075f, 0.25f), new Vector3(0f, 0f, 12f), hat);
                        Extra(st, rig.head, PrimitiveType.Cylinder, new Vector3(0f, 0.17f, 0.005f), new Vector3(0.215f, 0.012f, 0.23f), Vector3.zero, Mat(new Color(0.08f, 0.08f, 0.08f), 0f, 0.3f));
                        break;
                    case 3: // boonie hat
                        Extra(st, rig.head, PrimitiveType.Cylinder, new Vector3(0f, 0.185f, 0f), new Vector3(0.22f, 0.05f, 0.23f), Vector3.zero, hat);
                        Extra(st, rig.head, PrimitiveType.Cylinder, new Vector3(0f, 0.15f, 0f), new Vector3(0.36f, 0.006f, 0.37f), Vector3.zero, hat);
                        break;
                    case 4: // beanie
                        Extra(st, rig.head, PrimitiveType.Sphere, new Vector3(0f, 0.175f, -0.005f), new Vector3(0.215f, 0.16f, 0.235f), Vector3.zero, hat);
                        Extra(st, rig.head, PrimitiveType.Cylinder, new Vector3(0f, 0.14f, -0.005f), new Vector3(0.222f, 0.022f, 0.242f), Vector3.zero, hat);
                        break;
                    case 5: // short hair
                        Extra(st, rig.head, PrimitiveType.Sphere, new Vector3(0f, 0.15f, -0.012f), new Vector3(0.208f, 0.18f, 0.228f), Vector3.zero, hair);
                        break;
                }
            }

            // keep the renderer list in sync (visibility toggles iterate it); hidden parts are inactive objects
            ApplyModel(rig);
        }

        /// <summary>Imported model: wear it over the rig and hide the procedural meshes (their colliders stay as hitboxes).</summary>
        void ApplyModel(HumanoidRig rig)
        {
            var e = CharacterModelDB.Get(model);
            var sb = rig.GetComponentInChildren<SkinnedBody>(true);
            if (sb && (e == null || sb.ModelIndex != model)) { sb.gameObject.SetActive(false); UnityEngine.Object.Destroy(sb.gameObject); sb = null; }
            if (e != null && !sb) sb = SkinnedBody.Attach(rig, model, e);

            var all = rig.GetComponentsInChildren<Renderer>(true);
            if (!sb) { foreach (var r in all) r.forceRenderingOff = false; rig.renderers = all; return; }
            var own = new HashSet<Renderer>(sb.Renderers);
            foreach (var r in all) r.forceRenderingOff = !own.Contains(r);
            // keep the current shadows-only state (first person) on the fresh model
            bool shadowsOnly = rig.renderers != null && rig.renderers.Length > 0 && rig.renderers[0] && rig.renderers[0].shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            rig.renderers = sb.Renderers;
            if (shadowsOnly) rig.SetShadowsOnly(true);
        }

        static void Extra(LookState st, Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Vector3 euler, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "LookPart";
            var col = go.GetComponent<Collider>();
            if (Application.isPlaying) UnityEngine.Object.Destroy(col); else UnityEngine.Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            st.extras.Add(go);
        }
    }
}
