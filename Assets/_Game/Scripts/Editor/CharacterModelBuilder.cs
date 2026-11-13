using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Builds Resources/CharacterModels.asset from the imported character packs:
    /// the Assault character (one mesh, a material per texture set in genAssault/Textures/*) and the
    /// Russian FSB / police soldiers (Police_ru prefabs).
    /// </summary>
    public static class CharacterModelBuilder
    {
        const string DbPath = "Assets/Resources/CharacterModels.asset";
        const string AssaultPrefab = "Assets/genAssault/Prefabs/Assault_Char(NO_LODS).prefab";
        const string AssaultMat = "Assets/genAssault/Materials/Assault_Char.mat";
        const string SkinDir = "Assets/genAssault/Materials/Skins";

        static readonly (string folder, string name)[] AssaultSkins =
        {
            ("Military", "Штурмовик: армия"), ("SWAT", "Штурмовик: спецназ"), ("DeltaForce", "Штурмовик: «Дельта»"),
            ("MERC", "Штурмовик: наёмник"), ("North", "Штурмовик: север"), ("Arctic", "Штурмовик: арктика"),
            ("Red", "Штурмовик: красный"), ("GreenArmyMan", "Штурмовик: солдатик"), ("GoldenMan", "Штурмовик: золотой"),
            ("Impostor", "Штурмовик: самозванец"),
        };

        static readonly (string prefab, string name)[] PolicePrefabs =
        {
            ("RFSS_a", "ФСБ: штурм"), ("RFSS_b", "ФСБ: в каске"), ("RFSS_c", "ФСБ: тяжёлый"),
            ("RFSS_desert_a", "ФСБ пустыня: штурм"), ("RFSS_desert_b", "ФСБ пустыня: в каске"), ("RFSS_desert_c", "ФСБ пустыня: тяжёлый"),
            ("Rissian Police_a", "Полиция: патруль"), ("Rusian_police_b", "Полиция: ОМОН"), ("Russian_police_c", "Полиция: спецназ"),
        };

        [MenuItem("Tools/FPS/Build Character Models")]
        public static void Build()
        {
            ImportedMaterialFixer.FixAll();
            var entries = new List<CharacterModelDB.Entry>();

            var assault = AssetDatabase.LoadAssetAtPath<GameObject>(AssaultPrefab);
            var baseMat = AssetDatabase.LoadAssetAtPath<Material>(AssaultMat);
            if (assault && baseMat)
            {
                if (!AssetDatabase.IsValidFolder(SkinDir)) AssetDatabase.CreateFolder("Assets/genAssault/Materials", "Skins");
                var sharedAo = Tex("Assets/genAssault/Textures", "_ao");
                foreach (var (folder, name) in AssaultSkins)
                {
                    string dir = "Assets/genAssault/Textures/" + folder;
                    var d = Tex(dir, "_d");
                    if (!d) continue;
                    string matPath = $"{SkinDir}/Assault_{folder}.mat";
                    var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (!m) { m = new Material(baseMat); AssetDatabase.CreateAsset(m, matPath); }
                    else m.CopyPropertiesFromMaterial(baseMat);
                    m.SetTexture("_BaseMap", d);
                    m.SetColor("_BaseColor", Color.white);
                    var sg = Tex(dir, "_sg");
                    if (sg) { m.SetTexture("_MetallicGlossMap", sg); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                    var nm = Tex(dir, "_nm");
                    if (nm) { m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP"); }
                    var ao = Tex(dir, "_ao") ?? sharedAo;
                    if (ao) { m.SetTexture("_OcclusionMap", ao); m.EnableKeyword("_OCCLUSIONMAP"); }
                    EditorUtility.SetDirty(m);
                    entries.Add(new CharacterModelDB.Entry { name = name, prefab = assault, skin = m });
                }
            }
            else Debug.LogWarning("[CharacterModelBuilder] Assault character pack not found");

            foreach (var (prefab, name) in PolicePrefabs)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Police_ru/{prefab}.prefab");
                if (go) entries.Add(new CharacterModelDB.Entry { name = name, prefab = go });
            }

            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var db = AssetDatabase.LoadAssetAtPath<CharacterModelDB>(DbPath);
            if (!db) { db = ScriptableObject.CreateInstance<CharacterModelDB>(); AssetDatabase.CreateAsset(db, DbPath); }
            db.entries = entries.ToArray();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterModelBuilder] {entries.Count} character models");
            BuildWeapons();
        }

        const string WeaponDbPath = "Assets/Resources/WeaponModels.asset";

        /// <summary>Resources/WeaponModels.asset from WeaponCatalog: imported mesh + measured trigger / muzzle / foregrip / sight points.</summary>
        [MenuItem("Tools/FPS/Build Weapon Models")]
        public static void BuildWeapons()
        {
            var list = new List<WeaponModelDB.Entry>();
            var db = AssetDatabase.LoadAssetAtPath<WeaponModelDB>(WeaponDbPath);
            foreach (var def in WeaponCatalog.All)
            {
                if (string.IsNullOrEmpty(def.fbx)) continue;
                if (!string.IsNullOrEmpty(def.juPrefab)) CleanJUPrefab(def.juPrefab, def.fbx);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(def.fbx);
                if (!go) { Debug.LogWarning("[CharacterModelBuilder] missing " + def.fbx); continue; }
                // keep hand-tuned scale / offset from a previous build
                var old = db ? System.Array.Find(db.entries, x => x != null && x.weaponName == def.name) : null;
                var e = new WeaponModelDB.Entry
                {
                    weaponName = def.name, prefab = go, keepMagazine = def.keepMagazine,
                    scale = old != null ? old.scale : 1f, offset = old != null ? old.offset : Vector3.zero,
                    fixedScale = def.modelScale,
                };
                Measure(go, e);
                if (def.trigger.HasValue) e.triggerPoint = def.trigger.Value;
                if (def.foregrip.HasValue) { e.foregripPoint = def.foregrip.Value; e.hasForegrip = true; }
                Debug.Log($"[CharacterModelBuilder] {def.name}: trigger {e.triggerPoint:F3} muzzle {e.muzzlePoint:F3} sight {e.sightPoint:F3} foregrip {(e.hasForegrip ? e.foregripPoint.ToString("F3") : "-")}");
                // an optic's measured reticle center beats a hand-typed sight point (that one was a few mm off)
                if (def.sight.HasValue) { e.ownSight = true; if (!HasReticle(go)) e.sightPoint = def.sight.Value; }
                if (def.modelScale > 0f) { e.ownSight = true; e.sightKeepDepth = !HasReticle(go); }
                list.Add(e);
            }
            if (!db) { db = ScriptableObject.CreateInstance<WeaponModelDB>(); AssetDatabase.CreateAsset(db, WeaponDbPath); }
            db.entries = list.ToArray();
            // genAssault knife: blade along +Z, grip at the origin, 0.47 m long → scaled to the procedural knife's size
            db.knifePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/genAssault/weaponPack/Prefabs/Knife.prefab");
            db.knifeScale = 0.72f;
            db.knifeEuler = Vector3.zero;
            db.knifeOffset = new Vector3(0f, 0.004f, 0.01f);
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterModelBuilder] {list.Count} weapon models");
        }

        /// <summary>JU TPS 3 weapon prefab → meshes-only copy (its Weapon / Rigidbody / audio / particle parts would act on their own).</summary>
        static void CleanJUPrefab(string src, string dst)
        {
            var srcGo = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            if (!srcGo) { Debug.LogWarning("[CharacterModelBuilder] missing " + src); return; }
            string dir = System.IO.Path.GetDirectoryName(dst).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(dir).Replace('\\', '/'), System.IO.Path.GetFileName(dir));
            var inst = Object.Instantiate(srcGo);
            inst.name = System.IO.Path.GetFileNameWithoutExtension(dst);
            inst.transform.position = Vector3.zero;
            inst.transform.rotation = Quaternion.identity;
            // children without a mesh (shooting / IK points, shell particles) go; then every non-render component
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                if (t && t != inst.transform && !t.GetComponent<MeshFilter>() && t.GetComponentsInChildren<MeshFilter>(true).Length == 0) Object.DestroyImmediate(t.gameObject);
            for (int pass = 0; pass < 3; pass++)
                foreach (var c in inst.GetComponentsInChildren<Component>(true))
                    if (c && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)) Object.DestroyImmediate(c);
            PrefabUtility.SaveAsPrefabAsset(inst, dst);
            Object.DestroyImmediate(inst);
        }

        static bool Skip(string n)
        {
            n = n.ToLowerInvariant();
            return n.StartsWith("cal_") || n.Contains("shell") || n.Contains("pistolflashlight");
        }

        static bool HasReticle(GameObject go)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.name.ToLowerInvariant().Contains("reticle")) return true;
            return false;
        }

        /// <summary>Trigger, muzzle, foregrip and sight-line points in the FBX root space (from the mesh vertices).</summary>
        static void Measure(GameObject go, WeaponModelDB.Entry e)
        {
            var root = go.transform.worldToLocalMatrix;
            var pts = new List<Vector3>();
            Bounds trig = default, grip = default, ret = default; bool haveTrig = false, haveRet = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || Skip(mf.name)) continue;
                var m = root * mf.transform.localToWorldMatrix;
                string n = mf.name.ToLowerInvariant();
                Bounds b = default; bool hb = false;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (!hb) { b = new Bounds(p, Vector3.zero); hb = true; } else b.Encapsulate(p);
                    if (!n.Contains("magazine")) pts.Add(p);
                }
                if (!hb) continue;
                if (n.Contains("trigger") && !n.Contains("guard")) { trig = b; haveTrig = true; }
                if (n.Contains("foregrip")) { grip = b; e.hasForegrip = true; }
                if (n.Contains("reticle")) { ret = b; haveRet = true; }
            }
            if (pts.Count == 0) return;
            float zMin = float.MaxValue, zMax = float.MinValue, yMin = float.MaxValue;
            foreach (var p in pts) { zMin = Mathf.Min(zMin, p.z); zMax = Mathf.Max(zMax, p.z); yMin = Mathf.Min(yMin, p.y); }
            float len = zMax - zMin;

            e.triggerPoint = haveTrig ? trig.center : new Vector3(0f, yMin + 0.05f, zMin + len * 0.4f);
            if (e.hasForegrip) e.foregripPoint = new Vector3(0f, grip.max.y, grip.center.z);

            // muzzle: center of the front-most vertices
            Vector3 mSum = Vector3.zero; int mN = 0;
            foreach (var p in pts) if (p.z > zMax - 0.012f) { mSum += p; mN++; }
            e.muzzlePoint = mN > 0 ? new Vector3(0f, mSum.y / mN, zMax) : new Vector3(0f, 0f, zMax);

            // sight line: reticle center, else the top of the front sight (highest point of the front 15%)
            if (haveRet) e.sightPoint = new Vector3(0f, ret.center.y, ret.center.z);
            else
            {
                float yTop = float.MinValue;
                foreach (var p in pts) if (p.z > zMax - len * 0.15f && Mathf.Abs(p.x) < 0.012f) yTop = Mathf.Max(yTop, p.y);
                e.sightPoint = new Vector3(0f, yTop, zMax - len * 0.15f);
            }
        }

        /// <summary>First texture in dir (not recursive) whose file name ends with suffix.</summary>
        static Texture2D Tex(string dir, string suffix)
        {
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetDirectoryName(p).Replace('\\', '/') != dir) continue;
                if (Path.GetFileNameWithoutExtension(p).ToLowerInvariant().EndsWith(suffix.ToLowerInvariant()) && !p.Contains("frabric"))
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }
    }
}
