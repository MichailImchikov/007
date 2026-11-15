using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS.EditorTools
{
    /// <summary>
    /// Modular weapons from the Blender export (Assets/_Game/Art/Weapons, weapons_manifest.json):
    ///  • URP/Lit materials (Art/Weapons/Materials) and the FBX importers remapped to them (no animator / animation import);
    ///  • AttachmentData assets (Resources/Weapons/Attachments) and WeaponData assets (Resources/Weapons): slots from the
    ///    sockets, allowed attachments from the manifest's compatibility lists, moving parts, defaults.
    /// Names, descriptions and stat modifiers come from the tables below; on an existing asset they are only written by
    /// "Rebuild (reset stats)", so values tuned in the inspector survive a normal build.
    /// New weapon: export it with the Blender tool (manifest entry + FBX), add a WeaponCatalog entry (no fbx) and a line
    /// to <see cref="WeaponSpecs"/>, run Build Weapon Data. New attachment: manifest entry + FBX, a line in
    /// <see cref="AttachmentSpecs"/>, run Build Weapon Data (or create the asset by hand: Create > FPS > Attachment Data).
    /// </summary>
    public static class WeaponAssetBuilder
    {
        const string ArtRoot = "Assets/_Game/Art/Weapons";
        const string ManifestPath = ArtRoot + "/weapons_manifest.json";
        /// <summary>
        /// Models converted from imported packs (not made by the Blender generator, which rewrites the main manifest):
        /// their own assets, plus "compat_add" — extra weapon ids for the compatibility lists of any attachment.
        /// </summary>
        const string ImportedManifestPath = ArtRoot + "/weapons_manifest_imported.json";
        const string MaterialDir = ArtRoot + "/Materials";
        const string DataDir = "Assets/Resources/" + WeaponDataDB.ResourcesFolder;
        const string AttachmentDir = DataDir + "/Attachments";

        // ------------------------------------------------------------------ tables

        class WeaponSpec
        {
            public string name;
            public float flash = 1f, cycle = 0.035f;
            /// <summary>WeaponData.modelScale: the imported real-size models are drawn bigger to read like the generated ones in the hands.</summary>
            public float scale = 1f;
            public bool reciprocates;
            public Vector3 hipOffset;
            public Dictionary<AttachmentSlot, string> defaults = new Dictionary<AttachmentSlot, string>();
        }

        // the imported models are true to size and slimmer than the generated ones: in the hands they read small, so the
        // whole gun (with what is mounted on it) is drawn bigger
        const float ImportedScale = 1.15f, ImportedPistolScale = 1.12f;

        /// <summary>Manifest weapon id → catalog name, view / animation details, default attachments.</summary>
        static readonly Dictionary<string, WeaponSpec> WeaponSpecs = new Dictionary<string, WeaponSpec>
        {
            ["M4A1"] = new WeaponSpec
            {
                name = "M4A1", flash = 1f,
                defaults = { [AttachmentSlot.Optic] = "Optic_Holo", [AttachmentSlot.Muzzle] = "Muzzle_FlashHider", [AttachmentSlot.Underbarrel] = "Grip_Vertical" },
            },
            ["AK12"] = new WeaponSpec
            {
                name = "AK-12", flash = 1.05f, reciprocates = true, cycle = 0.05f,
                defaults = { [AttachmentSlot.Optic] = "Optic_RedDot", [AttachmentSlot.Muzzle] = "Muzzle_Brake" },
            },
            ["M14EBR"] = new WeaponSpec
            {
                name = "M14 EBR", flash = 1.15f, reciprocates = true, cycle = 0.05f,
                defaults = { [AttachmentSlot.Optic] = "Optic_Prism4x", [AttachmentSlot.Muzzle] = "Muzzle_FlashHider" },
            },
            ["M700"] = new WeaponSpec
            {
                name = "M700", flash = 1.25f,
                defaults = { [AttachmentSlot.Optic] = "Optic_Scope8x" },
            },
            ["Glock17"] = new WeaponSpec { name = "Glock 17", flash = 0.7f, cycle = 0.04f },

            // ---- imported packs (weapons_manifest_imported.json)
            ["AKM"] = new WeaponSpec { scale = ImportedScale, name = "AKM", flash = 1.1f, reciprocates = true, cycle = 0.05f,
                defaults = { [AttachmentSlot.Optic] = "Optic_OKP7", [AttachmentSlot.Muzzle] = "Muzzle_Brake762" } },
            ["HK416"] = new WeaponSpec { scale = ImportedScale, name = "HK416", flash = 1f,
                defaults = { [AttachmentSlot.Optic] = "Optic_HoloEXPS", [AttachmentSlot.Underbarrel] = "Grip_Angled_AFG" } },
            ["MK47"] = new WeaponSpec { scale = ImportedScale, name = "Mk 47 Mutant", flash = 1.1f,
                defaults = { [AttachmentSlot.Optic] = "Optic_MiniDot", [AttachmentSlot.Muzzle] = "Muzzle_BrakeHex" } },
            ["QBZ191"] = new WeaponSpec { scale = ImportedScale, name = "QBZ-191", flash = 1f, defaults = { [AttachmentSlot.Optic] = "Optic_RedDot" } },
            ["M249"] = new WeaponSpec { scale = ImportedScale, name = "M249", flash = 1.15f, cycle = 0.03f },
            ["MK18"] = new WeaponSpec { scale = ImportedScale, name = "Mk18 Mod 1", flash = 1.1f,
                defaults = { [AttachmentSlot.Optic] = "Optic_HoloRed", [AttachmentSlot.Underbarrel] = "Grip_Vertical_Tactical", [AttachmentSlot.Side] = "Flashlight" } },
            ["HoneyBadger"] = new WeaponSpec { scale = ImportedScale, name = "Honey Badger", flash = 0.25f, defaults = { [AttachmentSlot.Optic] = "Optic_MiniDot" } },
            ["Vector"] = new WeaponSpec { scale = ImportedScale, name = "KRISS Vector", flash = 0.8f, cycle = 0.03f, defaults = { [AttachmentSlot.Optic] = "Optic_RedDot" } },
            ["MPX"] = new WeaponSpec { scale = ImportedScale, name = "SIG MPX", flash = 0.8f, cycle = 0.03f, defaults = { [AttachmentSlot.Optic] = "Optic_HoloEXPS" } },
            ["Scorpion"] = new WeaponSpec { scale = ImportedScale, name = "Scorpion EVO 3", flash = 0.8f, cycle = 0.03f, defaults = { [AttachmentSlot.Optic] = "Optic_RedDot" } },
            ["SR2"] = new WeaponSpec { scale = ImportedScale, name = "СР-2 «Вереск»", flash = 0.8f, reciprocates = true, cycle = 0.03f },
            ["Deagle"] = new WeaponSpec { scale = ImportedPistolScale, name = "Desert Eagle", flash = 1f, cycle = 0.05f },
            ["K6S"] = new WeaponSpec { scale = ImportedPistolScale, name = "Kimber K6s", flash = 0.9f },
            ["R870"] = new WeaponSpec { scale = ImportedScale, name = "Remington 870", flash = 1.3f },
            ["SVD"] = new WeaponSpec { scale = ImportedScale, name = "СВД", flash = 1.2f, reciprocates = true, cycle = 0.05f, defaults = { [AttachmentSlot.Optic] = "Optic_Scope6x" } },
            ["AWM"] = new WeaponSpec { scale = ImportedScale, name = "AWM", flash = 1.3f, defaults = { [AttachmentSlot.Optic] = "Optic_Scope8x" } },
            ["VSS"] = new WeaponSpec { scale = ImportedScale, name = "ВСС «Винторез»", flash = 0.2f, reciprocates = true, cycle = 0.04f, defaults = { [AttachmentSlot.Optic] = "Optic_Scope6x" } },
            ["AXMC"] = new WeaponSpec { scale = ImportedScale, name = "AXMC", flash = 1.35f,
                defaults = { [AttachmentSlot.Optic] = "Optic_Scope8x", [AttachmentSlot.Underbarrel] = "Bipod" } },
            ["RPG7"] = new WeaponSpec { scale = 1.08f, name = "РПГ-7", flash = 0.5f },
        };

        class AttSpec
        {
            public string name, desc;
            public float recoil = 1f, hip = 1f, ads = 1f, adsTime = 1f, move = 1f, reload = 1f, range = 1f, damage = 1f;
            public float adsFov, aimDistance;
            public bool scoped, addReticle, suppressed, laser, light, foregrip;
            public float flash = 1f, volume = 1f, noise = 1f;
        }

        /// <summary>Attachment id → menu name / description (Russian) and stat modifiers.</summary>
        static readonly Dictionary<string, AttSpec> AttachmentSpecs = new Dictionary<string, AttSpec>
        {
            // ---- optics
            ["Optic_RedDot"] = new AttSpec { name = "Коллиматор", desc = "Открытый коллиматорный прицел: красная точка поверх цели, быстрый захват.", ads = 0.9f },
            ["Optic_Holo"] = new AttSpec { name = "Голограф", desc = "Голографический прицел с кольцом вокруг точки: широкий обзор, чуть тяжелее коллиматора.", ads = 0.9f, adsTime = 1.03f },
            ["Optic_Prism4x"] = new AttSpec { name = "Призма 4×", desc = "Призматический прицел 4× для средней и дальней дистанции. Вскидывается медленнее.",
                // the magnification is in the eyepiece (OpticSight, picture in picture); the view zooms like a 1x sight
                adsFov = 50f, aimDistance = 0.13f, addReticle = true, ads = 0.6f, adsTime = 1.15f, move = 0.99f },
            ["Optic_Scope8x"] = new AttSpec { name = "Оптика 8×", desc = "Снайперский прицел 8×: увеличенная картинка в окуляре, сетка-дуплекс с подсвеченной точкой.",
                adsFov = 50f, aimDistance = 0.08f, ads = 0.4f, adsTime = 1.3f, move = 0.98f },
            ["Optic_PistolDot"] = new AttSpec { name = "Мини-коллиматор", desc = "Компактный коллиматор на затворе: точка над целиком и мушкой, цикл затвора вместе с ним.", ads = 0.8f, adsTime = 1.04f },
            // ---- muzzle devices
            ["Muzzle_FlashHider"] = new AttSpec { name = "Пламегаситель", desc = "Гасит вспышку выстрела: меньше слепит и меньше выдаёт.", flash = 0.45f, recoil = 0.97f },
            ["Muzzle_Brake"] = new AttSpec { name = "Дульный тормоз", desc = "Заметно гасит отдачу, но выстрел громче, а вспышка ярче.", recoil = 0.82f, flash = 1.3f, volume = 1.15f, noise = 1.2f },
            ["Muzzle_PistolComp"] = new AttSpec { name = "Компенсатор", desc = "Прижимает ствол: меньше подброс после выстрела.", recoil = 0.85f, flash = 1.2f, volume = 1.1f },
            ["Suppressor_Rifle"] = new AttSpec { name = "Глушитель", desc = "Тихий выстрел без вспышки света: боты слышат его втрое ближе. Чуть меньше дальность.",
                suppressed = true, volume = 0.4f, noise = 0.35f, flash = 0.25f, recoil = 0.92f, range = 0.9f, adsTime = 1.06f, move = 0.99f },
            ["Suppressor_Pistol"] = new AttSpec { name = "Глушитель", desc = "Пистолетный глушитель: выстрел почти не слышен и не светит. Чуть меньше дальность.",
                suppressed = true, volume = 0.35f, noise = 0.3f, flash = 0.2f, recoil = 0.92f, range = 0.88f, adsTime = 1.05f },
            ["Suppressor_Sniper"] = new AttSpec { name = "Тяжёлый глушитель", desc = "Длинный глушитель под 7,62: позицию стрелка не выдают ни звук, ни вспышка. Тяжёлый.",
                suppressed = true, volume = 0.45f, noise = 0.4f, flash = 0.25f, recoil = 0.88f, adsTime = 1.1f, move = 0.98f },
            // ---- under the barrel
            ["Grip_Vertical"] = new AttSpec { name = "Вертикальная рукоять", desc = "Вторая рука держит рукоять: меньше отдача, точнее от бедра.", foregrip = true, recoil = 0.85f, hip = 0.92f, adsTime = 1.04f },
            ["Grip_Angled"] = new AttSpec { name = "Наклонная рукоять", desc = "Упор для ладони: оружие быстрее вскидывается к глазу и немного спокойнее.", foregrip = true, recoil = 0.93f, adsTime = 0.9f },
            ["Bipod"] = new AttSpec { name = "Сошки", desc = "Складные сошки: ствол меньше гуляет, но прицеливание и бег медленнее.", recoil = 0.9f, adsTime = 1.1f, move = 0.97f },
            ["Pistol_LaserLight"] = new AttSpec { name = "ЛЦУ + фонарь", desc = "Лазер и фонарь под стволом пистолета: точнее от бедра, светит туда, куда смотрит ствол.",
                laser = true, light = true, hip = 0.8f, adsTime = 1.03f },
            // ---- side rail
            ["Laser_Module"] = new AttSpec { name = "ЛЦУ", desc = "Лазерный целеуказатель: луч и точка показывают, куда смотрит ствол. Точнее от бедра.", laser = true, hip = 0.75f },
            ["Flashlight"] = new AttSpec { name = "Фонарь", desc = "Тактический фонарь на цевье: заменяет налобный фонарик, светит по стволу.", light = true },
            // ---- magazines
            ["Mag_M4A1_Ext"] = new AttSpec { name = "Магазин на 40", desc = "Удлинённый магазин: 40 патронов, перезарядка чуть дольше.", reload = 1.12f, adsTime = 1.04f, move = 0.99f },
            ["Mag_AK12_Ext"] = new AttSpec { name = "Магазин на 45", desc = "Удлинённый магазин: 45 патронов, перезарядка чуть дольше.", reload = 1.15f, adsTime = 1.05f, move = 0.99f },
            ["Mag_Glock17_Ext"] = new AttSpec { name = "Магазин на 33", desc = "Длинный магазин: 33 патрона, торчит из рукояти, перезарядка дольше.", reload = 1.12f, adsTime = 1.03f },

            // ---- imported packs
            ["Optic_ACOG"] = new AttSpec { name = "ACOG 4×", desc = "Призма 4× с подсвеченным шевроном: увеличенная картинка в окуляре, средняя и дальняя дистанция.",
                adsFov = 50f, aimDistance = 0.12f, ads = 0.6f, adsTime = 1.14f, move = 0.99f },
            ["Optic_Scope6x"] = new AttSpec { name = "Оптика 6×", desc = "Винтовочный прицел 6× с дуплексом: дальняя дистанция без тяжести снайперской оптики.",
                adsFov = 50f, aimDistance = 0.09f, ads = 0.45f, adsTime = 1.22f, move = 0.985f },
            ["Optic_HoloEXPS"] = new AttSpec { name = "Голограф EXPS", desc = "Голографический прицел с кольцом 65 MOA и точкой: самый быстрый захват цели.", ads = 0.9f, adsTime = 1.02f },
            ["Optic_HoloRed"] = new AttSpec { name = "Красный голограф", desc = "Голографический прицел в красном корпусе на высоком креплении: кольцо и точка.", ads = 0.9f, adsTime = 1.03f },
            ["Optic_MiniDot"] = new AttSpec { name = "Мини-коллиматор", desc = "Компактная красная точка на райзере: лёгкий, почти не мешает обзору.", ads = 0.9f, adsTime = 0.98f },
            ["Optic_OKP7"] = new AttSpec { name = "ОКП-7", desc = "Российский коллиматор с широким окном: точка и быстрый захват на ближней и средней дистанции.", ads = 0.9f, adsTime = 1.01f },
            ["Muzzle_Comp"] = new AttSpec { name = "Компенсатор", desc = "Прижимает ствол: подброс меньше, вспышка и звук чуть ярче.", recoil = 0.86f, flash = 1.15f, volume = 1.08f },
            ["Muzzle_BrakeHex"] = new AttSpec { name = "Тормоз-шестигранник", desc = "Мощный дульный тормоз: сильно гасит отдачу, громкий и яркий.", recoil = 0.8f, flash = 1.35f, volume = 1.2f, noise = 1.25f },
            ["Muzzle_Linear"] = new AttSpec { name = "Линейный компенсатор", desc = "Уводит звук и вспышку вперёд: чуть меньше отдача, выстрел не слепит.", recoil = 0.93f, flash = 0.6f },
            ["Muzzle_Brake762"] = new AttSpec { name = "Тормоз 7,62", desc = "Дульный тормоз под 7,62: заметно меньше отдача, громче выстрел.", recoil = 0.82f, flash = 1.3f, volume = 1.15f, noise = 1.2f },
            ["Suppressor_Long"] = new AttSpec { name = "Длинный глушитель", desc = "Тихий выстрел без вспышки: боты слышат его втрое ближе. Тяжёлый, чуть меньше дальность.",
                suppressed = true, volume = 0.38f, noise = 0.33f, flash = 0.2f, recoil = 0.9f, range = 0.9f, adsTime = 1.08f, move = 0.985f },
            ["Suppressor_Flow"] = new AttSpec { name = "Прямоточный глушитель", desc = "Проточный глушитель: не задерживает газы, меньше отдача, чуть громче обычного.",
                suppressed = true, volume = 0.45f, noise = 0.4f, flash = 0.25f, recoil = 0.88f, range = 0.92f, adsTime = 1.06f, move = 0.99f },
            ["Suppressor_762"] = new AttSpec { name = "Глушитель 7,62", desc = "Глушитель под 7,62: выстрел не выдаёт позицию ни звуком, ни вспышкой.",
                suppressed = true, volume = 0.42f, noise = 0.38f, flash = 0.22f, recoil = 0.9f, range = 0.92f, adsTime = 1.07f, move = 0.985f },
            ["Suppressor_9mm"] = new AttSpec { name = "Глушитель 9 мм", desc = "Компактный глушитель для ПП: почти бесшумно, чуть меньше дальность.",
                suppressed = true, volume = 0.33f, noise = 0.3f, flash = 0.2f, recoil = 0.92f, range = 0.88f, adsTime = 1.05f },
            ["Grip_Vertical_RK2"] = new AttSpec { name = "Рукоять РК-2", desc = "Вертикальная рукоять Zenitco: вторая рука держит её, отдача меньше.", foregrip = true, recoil = 0.85f, hip = 0.92f, adsTime = 1.04f },
            ["Grip_Vertical_RK0"] = new AttSpec { name = "Рукоять РК-0", desc = "Короткая рукоять-упор: немного меньше отдача и почти ничего не весит.", foregrip = true, recoil = 0.9f, adsTime = 1.01f },
            ["Grip_Vertical_Tactical"] = new AttSpec { name = "Тактическая рукоять", desc = "Рукоять с насечкой: устойчивее очередь, но вскидывается чуть дольше.", foregrip = true, recoil = 0.84f, hip = 0.9f, adsTime = 1.05f },
            ["Grip_Angled_AFG"] = new AttSpec { name = "Упор AFG", desc = "Наклонный упор для ладони: быстрее к глазу, спокойнее ствол.", foregrip = true, recoil = 0.92f, adsTime = 0.9f },
            ["Grip_Angled_Ergo"] = new AttSpec { name = "Эргономичная рукоять", desc = "Наклонная рукоять под ладонь: компромисс между вертикальной и упором.", foregrip = true, recoil = 0.89f, adsTime = 0.96f },
            ["Grip_Angled_Stop"] = new AttSpec { name = "Упор-стоппер", desc = "Длинный упор для хвата «к дулу»: быстрее вскидывается, ствол меньше гуляет.", foregrip = true, recoil = 0.94f, adsTime = 0.88f },
            ["Mag_AR_Drum"] = new AttSpec { name = "Барабан на 60", desc = "Барабанный магазин на 60 патронов: долгая перезарядка, тяжелее в руках.", reload = 1.3f, adsTime = 1.1f, move = 0.97f },
            ["Mag_AR_Coupled"] = new AttSpec { name = "Спаренный магазин", desc = "Два магазина на 30 скреплены вместе: 60 патронов, перезарядка чуть дольше.", reload = 1.1f, adsTime = 1.05f, move = 0.985f },
            ["Mag_AK_Ext"] = new AttSpec { name = "Магазин на 45", desc = "Длинный «рожок» на 45 патронов: перезарядка чуть дольше.", reload = 1.15f, adsTime = 1.05f, move = 0.99f },
        };

        static readonly Dictionary<AttachmentSlot, string> SlotSockets = new Dictionary<AttachmentSlot, string>
        {
            [AttachmentSlot.Optic] = "Socket_Optic", [AttachmentSlot.Muzzle] = "Socket_Muzzle", [AttachmentSlot.Underbarrel] = "Socket_Underbarrel",
            [AttachmentSlot.Side] = "Socket_Side", [AttachmentSlot.Magazine] = "Socket_Mag",
        };

        // ------------------------------------------------------------------ menu

        [MenuItem("Tools/FPS/Weapons/Build Weapon Data")]
        public static void BuildMenu() => Build(false);

        [MenuItem("Tools/FPS/Weapons/Rebuild Weapon Data (reset names and stats)")]
        public static void RebuildMenu() => Build(true);

        public static void Build(bool resetStats) => Build(resetStats, false);

        static readonly List<UnityEngine.Object> touched = new List<UnityEngine.Object>();

        static void Build(bool resetStats, bool auto)
        {
            if (!File.Exists(ManifestPath)) { Debug.LogError("[WeaponAssetBuilder] missing " + ManifestPath); return; }
            var manifest = MiniJson.Parse(File.ReadAllText(ManifestPath)) as Dictionary<string, object>;
            if (manifest == null || !(manifest.TryGetValue("assets", out var ao) && ao is List<object> assets)) { Debug.LogError("[WeaponAssetBuilder] bad manifest"); return; }
            // the imported packs' models: more assets, and weapon ids added to the attachments' compatibility
            var compatAdd = new Dictionary<string, object>();
            if (File.Exists(ImportedManifestPath))
            {
                var imp = MiniJson.Parse(File.ReadAllText(ImportedManifestPath)) as Dictionary<string, object>;
                if (imp != null && imp.TryGetValue("assets", out var io) && io is List<object> more) assets.AddRange(more);
                if (imp != null && imp.TryGetValue("compat_add", out var co) && co is Dictionary<string, object> ca) compatAdd = ca;
            }
            touched.Clear();

            var mats = BuildMaterials();
            int remapped = 0;
            foreach (var o in assets) if (SetupImporter(D(o), mats)) remapped++;

            EnsureFolder(DataDir);
            EnsureFolder(AttachmentDir);
            var atts = new List<(AttachmentData data, string[] compat)>();
            foreach (var o in assets)
            {
                var a = D(o);
                if (S(a, "kind") != "attachment") continue;
                var ad = BuildAttachment(a, mats, resetStats);
                if (!ad) continue;
                if (compatAdd.TryGetValue(ad.id, out var extra))
                {
                    var list = new List<string>(ad.compatible ?? new string[0]);
                    foreach (var x in Strings(new Dictionary<string, object> { ["v"] = extra }, "v")) if (!list.Contains(x)) list.Add(x);
                    ad.compatible = list.ToArray();
                    EditorUtility.SetDirty(ad);
                }
                atts.Add((ad, ad.compatible));
            }
            int weapons = 0;
            foreach (var o in assets)
            {
                var a = D(o);
                if (S(a, "kind") != "weapon") continue;
                if (BuildWeapon(a, atts, mats, resetStats)) weapons++;
            }
            // only our own assets are written (another tool may have unsaved assets open)
            foreach (var t in touched) if (t) AssetDatabase.SaveAssetIfDirty(t);
            touched.Clear();
            WeaponDataDB.Clear();
            if (!auto || remapped > 0)
                Debug.Log($"[WeaponAssetBuilder] {weapons} weapons, {atts.Count} attachments, {mats.Count} materials, {remapped} models re-imported");
        }

        // ---- automatic: new / re-exported weapon art (FBX, textures, manifest) → materials, remaps and data refreshed

        static bool autoScheduled;
        static double autoWindow;
        static int autoRuns;

        public static void ScheduleAutoBuild()
        {
            if (autoScheduled) return;
            autoScheduled = true;
            EditorApplication.delayCall += () =>
            {
                autoScheduled = false;
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) { ScheduleAutoBuild(); return; }
                double now = EditorApplication.timeSinceStartup;
                if (now - autoWindow > 30.0) { autoWindow = now; autoRuns = 0; }
                if (++autoRuns > 6) { Debug.LogWarning("[WeaponAssetBuilder] automatic rebuild skipped (loop guard) — run Tools > FPS > Weapons > Build Weapon Data"); return; }
                Build(false, true);
            };
        }

        // ------------------------------------------------------------------ materials

        const string TextureDir = ArtRoot + "/Textures";

        static Dictionary<string, Material> BuildMaterials()
        {
            EnsureFolder(MaterialDir);
            var m = new Dictionary<string, Material>
            {
                ["Metal_Black"] = Lit("Metal_Black", new Color(0.045f, 0.047f, 0.05f), 0.7f, 0.45f),
                ["Metal_Dark"] = Lit("Metal_Dark", new Color(0.1f, 0.105f, 0.11f), 0.85f, 0.5f),
                ["Metal_Worn"] = Lit("Metal_Worn", new Color(0.34f, 0.34f, 0.35f), 0.9f, 0.55f),
                ["Polymer_Black"] = Lit("Polymer_Black", new Color(0.04f, 0.041f, 0.045f), 0f, 0.32f),
                ["Polymer_Tan"] = Lit("Polymer_Tan", new Color(0.5f, 0.42f, 0.3f), 0f, 0.3f),
                ["Polymer_OD"] = Lit("Polymer_OD", new Color(0.24f, 0.27f, 0.18f), 0f, 0.3f),
                ["Polymer_Red"] = Lit("Polymer_Red", new Color(0.45f, 0.06f, 0.05f), 0f, 0.35f),
                ["Wood"] = Lit("Wood", new Color(0.33f, 0.19f, 0.1f), 0f, 0.38f),
                ["Rubber"] = Lit("Rubber", new Color(0.03f, 0.03f, 0.032f), 0f, 0.12f),
                ["Brass"] = Lit("Brass", new Color(0.8f, 0.6f, 0.28f), 1f, 0.72f),
                ["Glass_Lens"] = Lit("Glass_Lens", new Color(0.3f, 0.45f, 0.6f, 0.16f), 0f, 0.95f, null, true),
                ["Reticle_Red"] = Lit("Reticle_Red", new Color(0.6f, 0.02f, 0.02f), 0f, 0.5f, new Color(5f, 0.2f, 0.12f)),
                ["Emitter_Laser"] = Lit("Emitter_Laser", new Color(0.5f, 0.02f, 0.02f), 0f, 0.6f, new Color(4f, 0.15f, 0.1f)),
                ["Emitter_Light"] = Lit("Emitter_Light", new Color(0.9f, 0.9f, 0.85f), 0f, 0.8f, new Color(2.2f, 2.1f, 1.9f)),
            };
            return m;
        }

        /// <summary>
        /// "&lt;Id&gt;_Main" (one opaque PBR material per textured model): URP/Lit from Textures/&lt;Id&gt;/&lt;Id&gt;_BaseColor, _Normal,
        /// _MetallicSmoothness (R metallic, A smoothness) and _Occlusion. Without the textures (yet): a plain dark material.
        /// </summary>
        static Material TexturedMain(string matName)
        {
            string id = matName.Substring(0, matName.Length - "_Main".Length);
            string dir = $"{TextureDir}/{id}/{id}";
            var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "_BaseColor.png");
            if (!baseMap) return Lit(matName, new Color(0.13f, 0.135f, 0.14f), 0.5f, 0.4f);
            string nPath = dir + "_Normal.png", msPath = dir + "_MetallicSmoothness.png", aoPath = dir + "_Occlusion.png";
            TextureSettings(nPath, true, false);
            TextureSettings(msPath, false, true);
            TextureSettings(aoPath, false, true);
            var m = Lit(matName, Color.white, 1f, 1f);
            m.SetTexture("_BaseMap", baseMap);
            m.SetTexture("_MainTex", baseMap);
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(nPath);
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            if (normal) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            var ms = AssetDatabase.LoadAssetAtPath<Texture2D>(msPath);
            m.SetTexture("_MetallicGlossMap", ms);
            m.SetFloat("_SmoothnessTextureChannel", 0f); // smoothness = metallic map alpha (× _Smoothness = 1)
            m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            if (ms) m.EnableKeyword("_METALLICSPECGLOSSMAP");
            else { m.DisableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Metallic", 0.5f); m.SetFloat("_Smoothness", 0.45f); }
            var occ = AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath);
            m.SetTexture("_OcclusionMap", occ);
            m.SetFloat("_OcclusionStrength", 1f);
            if (occ) m.EnableKeyword("_OCCLUSIONMAP"); else m.DisableKeyword("_OCCLUSIONMAP");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Normal maps as NormalMap, data maps (metallic / occlusion) linear.</summary>
        static void TextureSettings(string path, bool normal, bool linear)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return;
            bool changed = false;
            if (normal && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; changed = true; }
            if (linear && ti.sRGBTexture) { ti.sRGBTexture = false; changed = true; }
            if (changed) ti.SaveAndReimport();
        }

        /// <summary>The material for a source material name: a by-name one, a textured "&lt;Id&gt;_Main", or null.</summary>
        static Material MaterialFor(string sourceName, Dictionary<string, Material> mats)
        {
            if (string.IsNullOrEmpty(sourceName)) return null;
            if (mats.TryGetValue(sourceName, out var m)) return m;
            if (sourceName.EndsWith("_Main") && sourceName.Length > 5)
            {
                m = TexturedMain(sourceName);
                mats[sourceName] = m;
                return m;
            }
            return null;
        }

        /// <summary>URP/Lit material (created or updated in place, keeps its GUID). Transparent = alpha blended, double sided.</summary>
        static Material Lit(string name, Color baseColor, float metallic, float smoothness, Color? emission = null, bool transparent = false)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = shader;
            m.SetColor("_BaseColor", baseColor);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_WorkflowMode", 1f);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                m.DisableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.black);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            }
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_BlendModePreserveSpecular", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                m.SetFloat("_Surface", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.SetFloat("_Cull", (float)CullMode.Back);
                m.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = -1;
                m.SetShaderPassEnabled("DepthOnly", true);
                m.SetShaderPassEnabled("ShadowCaster", true);
            }
            EditorUtility.SetDirty(m);
            touched.Add(m);
            return m;
        }

        /// <summary>
        /// No animator / animation import; every material of the FBX (names read from the imported model, plus the
        /// manifest's lists) remapped to our URP materials; remaps of names the FBX no longer has are dropped.
        /// Returns true when the model was re-imported.
        /// </summary>
        static bool SetupImporter(Dictionary<string, object> asset, Dictionary<string, Material> mats)
        {
            string file = S(asset, "file");
            var imp = AssetImporter.GetAtPath(file) as ModelImporter;
            if (!imp) { Debug.LogWarning("[WeaponAssetBuilder] no model importer for " + file); return false; }
            bool changed = false;
            if (imp.animationType != ModelImporterAnimationType.None) { imp.animationType = ModelImporterAnimationType.None; changed = true; }
            if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
            if (imp.importCameras) { imp.importCameras = false; changed = true; }
            if (imp.importLights) { imp.importLights = false; changed = true; }

            // source material names: remapped materials carry the source name too
            var names = new HashSet<string>();
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(file);
            if (go)
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    foreach (var sm in r.sharedMaterials)
                        if (sm) names.Add(sm.name);
            if (names.Count == 0 && asset.TryGetValue("meshes", out var mo) && mo is Dictionary<string, object> meshes)
                foreach (var mesh in meshes.Values)
                    if (D(mesh).TryGetValue("materials", out var ml) && ml is List<object> list)
                        foreach (var n in list) if (n is string ns) names.Add(ns);

            var current = imp.GetExternalObjectMap();
            foreach (var n in names)
            {
                var mat = MaterialFor(n, mats);
                if (!mat) { Debug.LogWarning($"[WeaponAssetBuilder] {file}: no material for '{n}'"); continue; }
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), n);
                if (current.TryGetValue(id, out var cur) && cur == mat) continue;
                imp.AddRemap(id, mat);
                changed = true;
            }
            foreach (var kv in current)
                if (kv.Key.type == typeof(Material) && !names.Contains(kv.Key.name) && go)
                {
                    imp.RemoveRemap(kv.Key);
                    changed = true;
                }
            if (changed) imp.SaveAndReimport();
            return changed;
        }

        // ------------------------------------------------------------------ data assets

        static AttachmentData BuildAttachment(Dictionary<string, object> a, Dictionary<string, Material> mats, bool reset)
        {
            string id = S(a, "id");
            if (!Enum.TryParse(S(a, "slot"), out AttachmentSlot slot)) { Debug.LogWarning("[WeaponAssetBuilder] unknown slot of " + id); return null; }
            string path = $"{AttachmentDir}/{id}.asset";
            var d = AssetDatabase.LoadAssetAtPath<AttachmentData>(path);
            bool created = !d;
            if (created)
            {
                d = ScriptableObject.CreateInstance<AttachmentData>();
                AssetDatabase.CreateAsset(d, path);
            }
            // from the manifest (always)
            d.id = id;
            d.slot = slot;
            d.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(S(a, "file"));
            d.compatible = Strings(a, "compatible");
            d.magnification = F(a, "magnification", 1f);
            d.muzzleLength = F(a, "muzzleLength", 0f);
            d.magCapacity = slot == AttachmentSlot.Magazine ? (int)F(a, "capacity", 0f) : 0;
            if (!d.prefab) Debug.LogWarning("[WeaponAssetBuilder] missing model " + S(a, "file"));
            // names and stats (new asset or reset)
            if ((created || reset) && AttachmentSpecs.TryGetValue(id, out var s))
            {
                d.displayName = s.name;
                d.description = s.desc;
                d.recoilMul = s.recoil; d.hipSpreadMul = s.hip; d.adsSpreadMul = s.ads; d.adsTimeMul = s.adsTime;
                d.moveSpeedMul = s.move; d.reloadTimeMul = s.reload; d.rangeMul = s.range; d.damageMul = s.damage;
                d.adsFov = s.adsFov; d.aimDistance = s.aimDistance; d.scoped = s.scoped; d.addReticle = s.addReticle;
                d.suppressed = s.suppressed; d.flashMul = s.flash; d.volumeMul = s.volume; d.noiseMul = s.noise;
                d.laser = s.laser; d.flashlight = s.light; d.foregrip = s.foregrip;
            }
            else if (created) d.displayName = id;
            if (d.addReticle && !d.reticleMaterial) mats.TryGetValue("Reticle_Red", out d.reticleMaterial);
            // from the model (always): can the eye see through the optic along its sight line?
            d.opaqueSightLine = slot == AttachmentSlot.Optic && SightLineBlocked(d.prefab);
            EditorUtility.SetDirty(d);
            touched.Add(d);
            return d;
        }

        /// <summary>
        /// Rays from just in front of the optic's Sight_Point along its forward (center + 4 points 4 mm off-axis) against the
        /// optic's meshes except the glass (Lens*) and the Reticle: blocked when most of them hit the body.
        /// </summary>
        static bool SightLineBlocked(GameObject prefab)
        {
            if (!prefab) return false;
            var root = prefab.transform;
            Transform sp = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "Sight_Point") { sp = t; break; }
            if (!sp) return false;
            Vector3 dir = root.InverseTransformDirection(sp.forward).normalized;
            Vector3 o = root.InverseTransformPoint(sp.position) + dir * 0.002f;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized, up = Vector3.Cross(dir, right);
            const float r = 0.004f;
            Vector3[] offsets = { Vector3.zero, right * r, -right * r, up * r, -up * r };
            var tris = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name.StartsWith("Lens", StringComparison.Ordinal) || mf.name == "Reticle") continue;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var v = mf.sharedMesh.vertices;
                foreach (int i in mf.sharedMesh.triangles) tris.Add(m.MultiplyPoint3x4(v[i]));
            }
            int blocked = 0;
            foreach (var off in offsets)
                for (int i = 0; i + 2 < tris.Count; i += 3)
                    if (RayTriangle(o + off, dir, tris[i], tris[i + 1], tris[i + 2], 0.6f)) { blocked++; break; }
            return blocked >= 3;
        }

        /// <summary>Möller–Trumbore: the ray hits the triangle within maxDist.</summary>
        static bool RayTriangle(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, float maxDist)
        {
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-14f) return false;
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float w = Vector3.Dot(d, q) * inv;
            if (w < 0f || u + w > 1f) return false;
            float t = Vector3.Dot(e2, q) * inv;
            return t > 0f && t < maxDist;
        }

        static bool BuildWeapon(Dictionary<string, object> a, List<(AttachmentData data, string[] compat)> atts, Dictionary<string, Material> mats, bool reset)
        {
            string id = S(a, "id");
            if (!WeaponSpecs.TryGetValue(id, out var spec)) { Debug.LogWarning("[WeaponAssetBuilder] no WeaponSpecs entry for " + id + " (add one)"); return false; }
            if (WeaponCatalog.Get(spec.name) == null) Debug.LogWarning($"[WeaponAssetBuilder] {spec.name} is not in WeaponCatalog (add an entry without fbx)");
            string path = $"{DataDir}/Weapon_{id}.asset";
            var d = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            bool created = !d;
            if (created)
            {
                d = ScriptableObject.CreateInstance<WeaponData>();
                AssetDatabase.CreateAsset(d, path);
            }
            var oldSlots = d.slots ?? new WeaponData.Slot[0];
            d.weaponName = spec.name;
            d.modelId = id;
            d.model = AssetDatabase.LoadAssetAtPath<GameObject>(S(a, "file"));
            d.capacity = (int)F(a, "capacity", 0f);
            if (!d.model) Debug.LogWarning("[WeaponAssetBuilder] missing model " + S(a, "file"));

            // moving parts: {"Bolt": {travel, rotation_deg}} or {"Slide": {...}}
            if (a.TryGetValue("moving", out var mv) && mv is Dictionary<string, object> moving)
                foreach (var kv in moving)
                {
                    d.boltPart = kv.Key;
                    var p = D(kv.Value);
                    d.boltTravel = F(p, "travel", 0.08f);
                    d.boltLiftAngle = F(p, "rotation_deg", 0f);
                    d.slideCycles = kv.Key == "Slide";
                    break;
                }
            d.boltReciprocates = spec.reciprocates;
            d.modelScale = spec.scale;
            if (created || reset)
            {
                d.flashScale = spec.flash;
                d.cycleTravel = spec.cycle;
                d.hipOffset = spec.hipOffset;
            }
            if (!d.shellMaterial) mats.TryGetValue("Brass", out d.shellMaterial);

            // slots: every attachment socket that has compatible attachments
            var sockets = a.TryGetValue("sockets", out var so) ? so as Dictionary<string, object> : null;
            var hints = a.TryGetValue("socket_parent_hint", out var ho) ? ho as Dictionary<string, object> : null;
            bool irons = sockets != null && sockets.ContainsKey("Sight_Iron");
            var slots = new List<WeaponData.Slot>();
            foreach (AttachmentSlot s in Enum.GetValues(typeof(AttachmentSlot)))
            {
                string socket = SlotSockets[s];
                if (sockets == null || !sockets.ContainsKey(socket)) continue;
                var allowed = new List<AttachmentData>();
                foreach (var (ad, compat) in atts)
                    if (ad.slot == s && Array.IndexOf(compat, id) >= 0) allowed.Add(ad);
                if (allowed.Count == 0) continue;
                var slot = new WeaponData.Slot
                {
                    slot = s, socket = socket, allowed = allowed.ToArray(),
                    parentPart = hints != null && hints.TryGetValue(socket, out var hp) ? hp as string : null,
                    required = s == AttachmentSlot.Optic && !irons,
                };
                // default: kept from the existing asset (inspector), else the table
                var old = Array.Find(oldSlots, x => x != null && x.slot == s);
                AttachmentData def = null;
                if (!reset && !created && old != null) def = old.defaultAttachment && allowed.Contains(old.defaultAttachment) ? old.defaultAttachment : null;
                else if (spec.defaults.TryGetValue(s, out var defId)) def = allowed.Find(x => x.id == defId);
                if (!def && slot.required) def = allowed[0];
                slot.defaultAttachment = def;
                slots.Add(slot);
            }
            d.slots = slots.ToArray();
            EditorUtility.SetDirty(d);
            touched.Add(d);
            return true;
        }

        // ------------------------------------------------------------------ helpers

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static Dictionary<string, object> D(object o) => o as Dictionary<string, object> ?? new Dictionary<string, object>();
        static string S(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) ? v as string : null;

        static float F(Dictionary<string, object> d, string k, float def)
            => d.TryGetValue(k, out var v) && v is double x ? (float)x : def;

        /// <summary>A string or a list of strings ("compatible": "Glock17" / ["M4A1", "AK12"]).</summary>
        static string[] Strings(Dictionary<string, object> d, string k)
        {
            if (!d.TryGetValue(k, out var v) || v == null) return new string[0];
            if (v is string s) return new[] { s };
            var r = new List<string>();
            if (v is List<object> l) foreach (var x in l) if (x is string xs) r.Add(xs);
            return r.ToArray();
        }

        /// <summary>Minimal JSON reader: objects → Dictionary, arrays → List, numbers → double.</summary>
        static class MiniJson
        {
            public static object Parse(string s)
            {
                int i = 0;
                return Value(s, ref i);
            }

            static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            static object Value(string s, ref int i)
            {
                Ws(s, ref i);
                if (i >= s.Length) return null;
                char c = s[i];
                if (c == '{')
                {
                    var d = new Dictionary<string, object>();
                    i++;
                    Ws(s, ref i);
                    if (s[i] == '}') { i++; return d; }
                    while (true)
                    {
                        Ws(s, ref i);
                        string key = Str(s, ref i);
                        Ws(s, ref i);
                        i++; // ':'
                        d[key] = Value(s, ref i);
                        Ws(s, ref i);
                        if (s[i++] == ',') continue;
                        return d; // '}'
                    }
                }
                if (c == '[')
                {
                    var l = new List<object>();
                    i++;
                    Ws(s, ref i);
                    if (s[i] == ']') { i++; return l; }
                    while (true)
                    {
                        l.Add(Value(s, ref i));
                        Ws(s, ref i);
                        if (s[i++] == ',') continue;
                        return l; // ']'
                    }
                }
                if (c == '"') return Str(s, ref i);
                if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
                int st = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            static string Str(string s, ref int i)
            {
                var sb = new StringBuilder();
                i++; // opening quote
                while (s[i] != '"')
                {
                    if (s[i] == '\\')
                    {
                        i++;
                        switch (s[i])
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                            default: sb.Append(s[i]); break;
                        }
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++; // closing quote
                return sb.ToString();
            }
        }
    }
}
