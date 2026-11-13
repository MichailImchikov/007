using System;
using UnityEngine;

namespace FPS
{
    public enum WeaponCategory { Assault, Marksman, SMG, Shotgun, Pistol }

    /// <summary>Procedural base model a weapon is built on (arms, animation anchors, reload style).
    /// The imported mesh is drawn over it, the procedural parts only stay as anchors.</summary>
    public enum WeaponArchetype { Rifle, SMG, Shotgun, DMR, Pistol }

    /// <summary>
    /// Single source of truth for every gun: player stats (applied by the map builder to the WeaponController),
    /// menu card (normalized bars), bot stats (derived), network index and the imported mesh.
    /// Adding a weapon = one entry here, then Tools > FPS > Build Weapon Models and Build All Maps.
    /// </summary>
    public class WeaponDef
    {
        public string name, kind, desc;
        public WeaponCategory category;
        public WeaponArchetype archetype;

        // ---- core stats
        public float damage = 26f, rpm = 700f;
        public int mag = 30, reserve = 210, pellets = 1;
        public bool fullAuto = true;
        public Sfx sfx = Sfx.Shot;
        /// <summary>Feel / handling extras on top of the archetype defaults.</summary>
        public Action<WeaponController> setup;

        // ---- menu bars (0..1)
        public float uiDamage, uiRate, uiRange, uiControl, uiMobility;

        // ---- imported mesh (Assets/Weapons_ChamferZone/...)
        public string fbx;
        /// <summary>Absolute scale of the imported mesh, aligned on the trigger. 0 = old fit (stretched to the procedural gun's length, centered).</summary>
        public float modelScale;
        /// <summary>Sight line point in the FBX root space; null = measured automatically (reticle, else the front sight top).</summary>
        public Vector3? sight;
        public bool keepMagazine;

        public bool Secondary => category == WeaponCategory.Pistol;
        public WeaponSlot Slot => Secondary ? WeaponSlot.Secondary : WeaponSlot.Primary;

        /// <summary>Writes the stats on a WeaponController (builder, at bake time).</summary>
        public void Apply(WeaponController w)
        {
            w.weaponName = name;
            w.slot = Slot;
            w.damage = damage; w.rpm = rpm; w.magSize = mag; w.reserveAmmo = reserve; w.pellets = pellets;
            w.fullAuto = fullAuto;
            if (!fullAuto) w.allowFireModeSwitch = false;
            w.shotSfx = sfx;
            switch (archetype)
            {
                case WeaponArchetype.SMG:
                    w.hipSpread = 1.5f; w.moveSpreadAdd = 1.2f; w.recoilUp = 0.5f; w.recoilSide = 0.45f; w.adsFov = 50f; w.moveSpeedMul = 1.06f;
                    w.falloffStart = 18f; w.falloffEnd = 60f; w.falloffMin = 0.55f;
                    w.hipPosition = new Vector3(0.15f, -0.165f, 0.31f); w.aimDistance = 0.2f; w.aimTime = 0.16f; w.reloadTime = 1.8f; w.emptyReloadTime = 2.2f;
                    w.magGripOffset = new Vector3(0f, -0.13f, 0f); w.chTravel = 0.06f; w.shotVolume = 0.65f; w.kickScale = 0.75f; w.punchScale = 0.7f;
                    break;
                case WeaponArchetype.Shotgun:
                    w.pelletSpread = 3.2f; w.allowFireModeSwitch = false;
                    w.hipSpread = 0.8f; w.adsSpread = 0.2f; w.recoilUp = 4.2f; w.recoilSide = 0.8f;
                    w.falloffStart = 10f; w.falloffEnd = 35f; w.falloffMin = 0.3f; w.adsFov = 54f; w.moveSpeedMul = 0.96f;
                    w.hipPosition = new Vector3(0.15f, -0.18f, 0.3f); w.aimDistance = 0.17f; w.reloadStyle = ReloadStyle.PerShell; w.shellInsertTime = 0.46f;
                    w.pumpTime = 0.52f; w.chTravel = 0.09f; w.shellScale = 1.8f; w.shotVolume = 0.95f;
                    w.kickScale = 2f; w.punchScale = 2.2f; w.bloomPerShot = 0f; w.impactForce = 10f;
                    break;
                case WeaponArchetype.DMR:
                    w.allowFireModeSwitch = false;
                    w.hipSpread = 2.6f; w.adsSpread = 0f; w.recoilUp = 2.4f; w.recoilSide = 0.5f; w.adsRecoilMul = 0.7f; w.adsFov = 16f; w.scoped = true;
                    w.aimDistance = 0.07f; w.aimTime = 0.26f; w.moveSpeedMul = 0.92f; w.falloffStart = 80f; w.falloffEnd = 250f; w.falloffMin = 0.85f;
                    w.reloadTime = 2.4f; w.emptyReloadTime = 2.9f; w.magGripOffset = new Vector3(0f, -0.07f, 0.01f);
                    w.shotVolume = 0.95f; w.kickScale = 1.6f; w.punchScale = 1.8f; w.bloomPerShot = 1f; w.impactForce = 8f;
                    break;
                case WeaponArchetype.Pistol:
                    w.allowFireModeSwitch = false;
                    w.hipSpread = 1.1f; w.adsSpread = 0.15f; w.recoilUp = 1.3f; w.recoilSide = 0.4f; w.adsFov = 52f;
                    w.hipPosition = new Vector3(0.13f, -0.15f, 0.34f); w.aimDistance = 0.3f; w.aimTime = 0.14f; w.moveSpeedMul = 1.08f;
                    w.sprintPos = new Vector3(-0.02f, -0.07f, -0.06f); w.sprintRot = new Vector3(45f, -15f, 10f);
                    w.reloadTime = 1.5f; w.emptyReloadTime = 1.9f; w.magGripOffset = new Vector3(0f, -0.11f, -0.025f); w.chTravel = 0.03f;
                    w.shotVolume = 0.75f; w.equipTime = 0.28f; w.kickScale = 1.3f; w.shellScale = 0.85f;
                    break;
            }
            setup?.Invoke(w);
        }

        // ---- bots (derived, so a new weapon needs no bot code)
        public float BotDamage => damage * pellets * (pellets > 1 ? 0.5f : 0.75f);
        public float BotRpm => Mathf.Min(rpm * 0.88f, 880f);
        public float BotSpread => category switch { WeaponCategory.SMG => 1.6f, WeaponCategory.Shotgun => 2.4f, WeaponCategory.Marksman => 0.35f, WeaponCategory.Pistol => 1.2f, _ => 0.9f };
        public float BotPrefDist => category switch { WeaponCategory.SMG => 9f, WeaponCategory.Shotgun => 6f, WeaponCategory.Marksman => 30f, WeaponCategory.Pistol => 12f, _ => 20f };
        public int BotBurstMin => !fullAuto ? 1 : category == WeaponCategory.SMG ? 4 : 3;
        public int BotBurstMax => !fullAuto ? (mag > 2 ? 2 : 1) : category == WeaponCategory.SMG ? 9 : 6;
    }

    public static class WeaponCatalog
    {
        public static readonly string[] CategoryNames = { "Автоматы", "Снайперские", "ПП", "Дробовики", "Пистолеты" };

        const string W = "Assets/Weapons_ChamferZone/";

        public static readonly WeaponDef[] All =
        {
            // ================= assault rifles
            new WeaponDef { name = "M4A1", kind = "Штурмовая винтовка", category = WeaponCategory.Assault, archetype = WeaponArchetype.Rifle,
                uiDamage = 0.45f, uiRate = 0.76f, uiRange = 0.55f, uiControl = 0.7f, uiMobility = 0.62f,
                desc = "Универсал: точная очередь на средней дистанции, коллиматорный прицел, переключение режима огня.",
                fbx = W + "AR15/WPN_MK18.FBX", sight = new Vector3(0f, 0.072f, -0.261f) },
            new WeaponDef { name = "CV-47", kind = "Автомат", category = WeaponCategory.Assault, archetype = WeaponArchetype.Rifle,
                damage = 31f, rpm = 600f, mag = 30, reserve = 180,
                setup = w => { w.recoilUp = 1.15f; w.recoilSide = 0.55f; w.hipSpread = 1.5f; w.adsSpread = 0.1f; w.adsFov = 50f; w.moveSpeedMul = 0.97f;
                               w.reloadTime = 2.3f; w.emptyReloadTime = 2.8f; w.falloffStart = 40f; w.falloffEnd = 130f; w.falloffMin = 0.72f; w.shotPitch = 0.9f; w.kickScale = 1.15f; },
                uiDamage = 0.55f, uiRate = 0.62f, uiRange = 0.6f, uiControl = 0.48f, uiMobility = 0.58f,
                desc = "Тяжёлый патрон и жёсткая отдача. Три попадания вместо четырёх — если удержишь ствол.",
                fbx = W + "CV47/CV47.FBX", modelScale = 1.3f },

            // ================= marksman
            new WeaponDef { name = "DMR-7", kind = "Марксманская винтовка", category = WeaponCategory.Marksman, archetype = WeaponArchetype.DMR,
                damage = 58f, rpm = 300f, mag = 10, reserve = 60, fullAuto = false, sfx = Sfx.ShotSniper,
                uiDamage = 0.8f, uiRate = 0.33f, uiRange = 1f, uiControl = 0.45f, uiMobility = 0.42f,
                desc = "Оптика 6×, 58 урона за выстрел: два попадания в тело, одно в голову. Для длинных линий.",
                fbx = W + "Hunter85/WPN_Hunter85.FBX", sight = new Vector3(0f, 0.089f, -0.206f) },
            new WeaponDef { name = "M16A4", kind = "Винтовка с оптикой 4×", category = WeaponCategory.Marksman, archetype = WeaponArchetype.Rifle,
                damage = 36f, rpm = 720f, mag = 30, reserve = 150, fullAuto = false,
                setup = w => { w.adsFov = 30f; w.adsSpread = 0.02f; w.aimTime = 0.24f; w.aimDistance = 0.26f; w.recoilUp = 0.95f; w.recoilSide = 0.3f;
                               w.falloffStart = 60f; w.falloffEnd = 180f; w.falloffMin = 0.8f; w.moveSpeedMul = 0.95f; w.reloadTime = 2.2f; w.emptyReloadTime = 2.7f; },
                uiDamage = 0.62f, uiRate = 0.55f, uiRange = 0.85f, uiControl = 0.72f, uiMobility = 0.5f,
                desc = "Одиночный огонь и прицел ACOG: быстрые точные выстрелы на средней и дальней дистанции.",
                fbx = W + "M16/WPN_M16.FBX", modelScale = 1.2f },

            // ================= SMGs
            new WeaponDef { name = "SMG-9", kind = "Пистолет-пулемёт", category = WeaponCategory.SMG, archetype = WeaponArchetype.SMG,
                damage = 19f, rpm = 920f, mag = 32, reserve = 256,
                uiDamage = 0.33f, uiRate = 1f, uiRange = 0.28f, uiControl = 0.62f, uiMobility = 0.85f,
                desc = "Бесшумный и самый скорострельный. Короткие коридоры, склады и забеги через фланг.",
                fbx = W + "SMG5/SMG5.FBX" },
            new WeaponDef { name = "KM4", kind = "Пистолет-пулемёт .45", category = WeaponCategory.SMG, archetype = WeaponArchetype.SMG,
                damage = 24f, rpm = 650f, mag = 25, reserve = 200,
                setup = w => { w.hipSpread = 1.35f; w.recoilUp = 0.72f; w.recoilSide = 0.4f; w.moveSpeedMul = 1.04f; w.falloffStart = 15f; w.falloffEnd = 50f; w.shotVolume = 0.8f; w.kickScale = 0.9f; },
                uiDamage = 0.42f, uiRate = 0.58f, uiRange = 0.3f, uiControl = 0.72f, uiMobility = 0.8f,
                desc = "Медленный, но увесистый .45: меньше пуль на противника, спокойная отдача.",
                fbx = W + "KM4/KM4.FBX", modelScale = 1.2f },
            new WeaponDef { name = "R90", kind = "Пистолет-пулемёт", category = WeaponCategory.SMG, archetype = WeaponArchetype.SMG,
                damage = 16f, rpm = 900f, mag = 50, reserve = 250,
                setup = w => { w.hipSpread = 1.2f; w.recoilUp = 0.38f; w.recoilSide = 0.35f; w.moveSpeedMul = 1.06f; w.falloffStart = 16f; w.falloffEnd = 50f; w.falloffMin = 0.5f;
                               w.reloadTime = 2.6f; w.emptyReloadTime = 3.1f; w.shotVolume = 0.75f; w.shotPitch = 1.1f; },
                uiDamage = 0.28f, uiRate = 0.95f, uiRange = 0.25f, uiControl = 0.82f, uiMobility = 0.86f,
                desc = "Магазин на 50 патронов и почти нет отдачи. Долгая перезарядка — не трать его зря.",
                fbx = W + "R90/WPN_R90.FBX", modelScale = 1.25f, sight = new Vector3(0f, 0.109f, 0f) },
            new WeaponDef { name = "CX8", kind = "Карабин", category = WeaponCategory.SMG, archetype = WeaponArchetype.SMG,
                damage = 30f, rpm = 520f, mag = 20, reserve = 160, fullAuto = false,
                setup = w => { w.hipSpread = 1.2f; w.adsSpread = 0.04f; w.recoilUp = 0.6f; w.aimDistance = 0.32f; w.falloffStart = 30f; w.falloffEnd = 90f; w.falloffMin = 0.65f; w.moveSpeedMul = 1.02f; w.shotVolume = 0.8f; },
                uiDamage = 0.52f, uiRate = 0.45f, uiRange = 0.5f, uiControl = 0.8f, uiMobility = 0.75f,
                desc = "Полуавтоматический карабин под пистолетный патрон: дальше и точнее любого ПП.",
                fbx = W + "CX8_Alaris/WPN_CX8.FBX", modelScale = 1.2f },

            // ================= shotguns
            new WeaponDef { name = "M870", kind = "Помповый дробовик", category = WeaponCategory.Shotgun, archetype = WeaponArchetype.Shotgun,
                damage = 13f, pellets = 9, rpm = 80f, mag = 7, reserve = 35, fullAuto = false, sfx = Sfx.ShotShotgun,
                setup = w => { w.pumpAction = true; },
                uiDamage = 1f, uiRate = 0.1f, uiRange = 0.12f, uiControl = 0.3f, uiMobility = 0.55f,
                desc = "9 картечин за выстрел: убивает в упор с одного выстрела, на дистанции бесполезен.",
                fbx = W + "590A1/WPN_590A1.FBX", sight = new Vector3(0f, 0.0226f, 0f), keepMagazine = true },
            new WeaponDef { name = "SG-4", kind = "Полуавтоматический дробовик", category = WeaponCategory.Shotgun, archetype = WeaponArchetype.Shotgun,
                damage = 11f, pellets = 8, rpm = 230f, mag = 6, reserve = 36, fullAuto = false, sfx = Sfx.ShotShotgun,
                setup = w => { w.pelletSpread = 3.5f; w.recoilUp = 3.3f; w.falloffStart = 9f; w.falloffEnd = 30f; w.kickScale = 1.7f; },
                uiDamage = 0.82f, uiRate = 0.32f, uiRange = 0.1f, uiControl = 0.38f, uiMobility = 0.55f,
                desc = "Без помпы: стреляет так быстро, как жмёшь. Чуть слабее и шире разлёт.",
                fbx = W + "M4/WPN_M4.FBX", modelScale = 1.12f, keepMagazine = true },
            new WeaponDef { name = "FBS", kind = "Помповый дробовик", category = WeaponCategory.Shotgun, archetype = WeaponArchetype.Shotgun,
                damage = 15f, pellets = 8, rpm = 70f, mag = 5, reserve = 30, fullAuto = false, sfx = Sfx.ShotShotgun,
                setup = w => { w.pumpAction = true; w.pumpTime = 0.6f; w.pelletSpread = 2.5f; w.falloffStart = 13f; w.falloffEnd = 42f; w.falloffMin = 0.35f; w.recoilUp = 4.6f; },
                uiDamage = 0.95f, uiRate = 0.08f, uiRange = 0.18f, uiControl = 0.3f, uiMobility = 0.52f,
                desc = "Кучная осыпь бьёт дальше обычного, но помпа медленная, а магазин на 5.",
                fbx = W + "FBS/FBS.FBX", modelScale = 1.2f, keepMagazine = true },
            new WeaponDef { name = "DT22", kind = "Двустволка", category = WeaponCategory.Shotgun, archetype = WeaponArchetype.Shotgun,
                damage = 16f, pellets = 10, rpm = 420f, mag = 2, reserve = 30, fullAuto = false, sfx = Sfx.ShotShotgun,
                setup = w => { w.pelletSpread = 2.6f; w.recoilUp = 4.8f; w.shellInsertTime = 0.38f; w.moveSpeedMul = 1f; w.shotVolume = 1f; w.shotPitch = 0.9f; },
                uiDamage = 1f, uiRate = 0.5f, uiRange = 0.15f, uiControl = 0.22f, uiMobility = 0.6f,
                desc = "Два ствола — два выстрела подряд, потом перезарядка. Дуплет в упор не оставляет шансов.",
                fbx = W + "DT22/DT22.FBX", modelScale = 0.95f, keepMagazine = true },

            // ================= pistols (secondary)
            new WeaponDef { name = "P-17", kind = "Пистолет", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 24f, rpm = 450f, mag = 15, reserve = 90, fullAuto = false, sfx = Sfx.ShotPistol,
                uiDamage = 0.42f, uiRate = 0.5f, uiRange = 0.3f, uiControl = 0.75f, uiMobility = 1f,
                desc = "Быстро достаётся. Добивает, когда кончился магазин основного оружия.",
                fbx = W + "M9/WPN_M9.FBX", sight = new Vector3(0f, 0.077f, -0.066f) },
            new WeaponDef { name = "AP85", kind = "Пистолет", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 25f, rpm = 480f, mag = 16, reserve = 96, fullAuto = false, sfx = Sfx.ShotPistol,
                setup = w => { w.recoilUp = 1.1f; w.adsSpread = 0.12f; },
                uiDamage = 0.44f, uiRate = 0.55f, uiRange = 0.32f, uiControl = 0.8f, uiMobility = 1f,
                desc = "Тяжёлая стальная рамка гасит отдачу: самый послушный пистолет.",
                fbx = W + "AP85/WPN_AP85.FBX", modelScale = 0.95f },
            new WeaponDef { name = "C1911", kind = "Пистолет .45", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 35f, rpm = 380f, mag = 8, reserve = 64, fullAuto = false, sfx = Sfx.ShotPistol,
                setup = w => { w.recoilUp = 1.8f; w.shotVolume = 0.85f; w.shotPitch = 0.92f; w.kickScale = 1.5f; },
                uiDamage = 0.6f, uiRate = 0.4f, uiRange = 0.36f, uiControl = 0.6f, uiMobility = 1f,
                desc = "Классика под .45: восемь патронов, но каждый на треть сильнее обычного.",
                fbx = W + "C1911/WPN_C1911.FBX", modelScale = 0.95f },
            new WeaponDef { name = "Eder 22", kind = "Пистолет", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 22f, rpm = 540f, mag = 17, reserve = 102, fullAuto = false, sfx = Sfx.ShotPistol,
                setup = w => { w.moveSpeedMul = 1.1f; w.equipTime = 0.24f; },
                uiDamage = 0.38f, uiRate = 0.6f, uiRange = 0.3f, uiControl = 0.74f, uiMobility = 1f,
                desc = "Полимерный и лёгкий: быстрее всех достаётся и стреляет.",
                fbx = W + "Eder22/WPN_Eder22.FBX", modelScale = 0.95f },
            new WeaponDef { name = "P350", kind = "Компактный пистолет", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 23f, rpm = 500f, mag = 12, reserve = 84, fullAuto = false, sfx = Sfx.ShotPistol,
                setup = w => { w.moveSpeedMul = 1.12f; w.hipSpread = 0.9f; w.aimTime = 0.12f; },
                uiDamage = 0.4f, uiRate = 0.56f, uiRange = 0.28f, uiControl = 0.7f, uiMobility = 1f,
                desc = "Самая точная стрельба от бедра и быстрее всего вскидывается.",
                fbx = W + "P350/P350.FBX", modelScale = 0.95f },
            new WeaponDef { name = "PT8", kind = "Пистолет", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 28f, rpm = 420f, mag = 14, reserve = 84, fullAuto = false, sfx = Sfx.ShotPistol,
                setup = w => { w.recoilUp = 1.4f; w.falloffStart = 40f; },
                uiDamage = 0.48f, uiRate = 0.46f, uiRange = 0.4f, uiControl = 0.7f, uiMobility = 1f,
                desc = "Золотая середина: урон выше среднего и неплохая дальность.",
                fbx = W + "PT8_Alaris/PT8_Alaris.FBX", modelScale = 0.95f },
            new WeaponDef { name = "Gruber .22", kind = "Спортивный пистолет", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 16f, rpm = 600f, mag = 10, reserve = 100, fullAuto = false, sfx = Sfx.ShotPistol,
                setup = w => { w.recoilUp = 0.45f; w.recoilSide = 0.15f; w.adsSpread = 0.02f; w.hipSpread = 0.8f; w.falloffStart = 45f; w.falloffEnd = 140f; w.falloffMin = 0.8f;
                               w.shotVolume = 0.5f; w.shotPitch = 1.3f; w.kickScale = 0.6f; },
                uiDamage = 0.28f, uiRate = 0.66f, uiRange = 0.6f, uiControl = 1f, uiMobility = 1f,
                desc = "Мелкий калибр, но пуля летит ровно туда, куда смотришь. Тихий.",
                fbx = W + "Gruber/WPN_Gruber.FBX", modelScale = 0.95f },
            new WeaponDef { name = "R-357", kind = "Револьвер", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 52f, rpm = 160f, mag = 6, reserve = 36, fullAuto = false, sfx = Sfx.ShotPistol,
                setup = w => { w.recoilUp = 3f; w.recoilSide = 0.6f; w.reloadTime = 2.4f; w.emptyReloadTime = 2.4f; w.shotVolume = 1f; w.shotPitch = 0.78f;
                               w.kickScale = 2f; w.punchScale = 1.6f; w.falloffStart = 30f; w.falloffMin = 0.75f; },
                uiDamage = 0.9f, uiRate = 0.18f, uiRange = 0.5f, uiControl = 0.35f, uiMobility = 0.95f,
                desc = "Два попадания — и всё. Шесть патронов и долгая перезарядка.",
                fbx = W + "Revolver/WPN_Revolver.FBX", modelScale = 0.95f },
            new WeaponDef { name = "Ракетница", kind = "Сигнальный пистолет", category = WeaponCategory.Pistol, archetype = WeaponArchetype.Pistol,
                damage = 75f, rpm = 60f, mag = 1, reserve = 8, fullAuto = false, sfx = Sfx.ShotShotgun,
                setup = w => { w.recoilUp = 3.5f; w.reloadTime = 1.6f; w.emptyReloadTime = 1.6f; w.shotVolume = 0.7f; w.shotPitch = 1.5f; w.hipSpread = 0.9f;
                               w.falloffStart = 20f; w.falloffEnd = 60f; w.falloffMin = 0.4f; w.kickScale = 1.8f; },
                uiDamage = 1f, uiRate = 0.05f, uiRange = 0.25f, uiControl = 0.5f, uiMobility = 1f,
                desc = "Один заряд, огромный урон вблизи. Для отчаянных.",
                fbx = W + "Flaregun/WPN_Flaregun.FBX", modelScale = 0.95f },
        };

        public static WeaponDef Get(string name)
        {
            foreach (var d in All) if (d.name == name) return d;
            return null;
        }

        public static int Index(string name)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].name == name) return i;
            return -1;
        }
    }

    /// <summary>Melee weapon choice (replaces the knife in the knife slot).</summary>
    public class MeleeDef
    {
        public string name, desc;
        public float damage, range, slashTime, backstab;
        public float uiDamage, uiSpeed, uiRange;
    }

    public static class MeleeCatalog
    {
        public static readonly MeleeDef[] All =
        {
            new MeleeDef { name = "Боевой нож", damage = 55f, range = 1.9f, slashTime = 0.55f, backstab = 2f, uiDamage = 0.6f, uiSpeed = 0.6f, uiRange = 0.5f,
                desc = "Нож с гардой. Удар в спину — вдвое сильнее." },
            new MeleeDef { name = "Штык-нож", damage = 42f, range = 1.75f, slashTime = 0.42f, backstab = 2.4f, uiDamage = 0.45f, uiSpeed = 0.85f, uiRange = 0.45f,
                desc = "Лёгкий и быстрый: два удара, пока противник замахивается. Удар в спину — смертелен." },
            new MeleeDef { name = "Меч", damage = 85f, range = 2.5f, slashTime = 0.8f, backstab = 1.5f, uiDamage = 0.95f, uiSpeed = 0.3f, uiRange = 0.85f,
                desc = "Медленный размах, но длинный клинок и почти убивающий удар." },
        };

        public static MeleeDef Get(string name)
        {
            foreach (var d in All) if (d.name == name) return d;
            return All[0];
        }
    }
}
