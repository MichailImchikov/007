using UnityEngine;

namespace FPS
{
    public enum PlayerClass { Assault, Medic, Engineer, Sniper }

    /// <summary>A soldier class: its own weapons, passive bonuses and a class ability on the class key (3).</summary>
    public class ClassInfo
    {
        public PlayerClass id;
        public string name, desc, glyph;
        public Color color;
        /// <summary>Catalog names of the weapons this class may carry.</summary>
        public string[] primaries, secondaries;
        /// <summary>A third weapon on the class key (the engineer's launcher), or null.</summary>
        public string gadget;
        public string abilityName, abilityDesc;
        /// <summary>Passives: sprint / walk speed and reload speed multipliers (1 = none).</summary>
        public float moveMul = 1f, reloadSpeed = 1f;
        /// <summary>Active ability: cooldown and duration (s).</summary>
        public float cooldown, duration;
    }

    /// <summary>
    /// The four classes. Weapons are catalog names (WeaponCatalog); only guns with real models are offered, the old
    /// placeholder guns of the catalog stay out of the classes.
    /// </summary>
    public static class ClassDB
    {
        public static readonly string[] Pistols = { "Glock 17", "P226", "Desert Eagle", "Kimber K6s" };
        public static readonly string[] Shotguns = { "Remington 870", "Super 90" };

        public static readonly ClassInfo[] All =
        {
            new ClassInfo
            {
                id = PlayerClass.Assault, name = "Штурмовик", glyph = "Ш", color = new Color(1f, 0.55f, 0.2f),
                desc = "Передовая: автоматы и пулемёт. Бегает и перезаряжается быстрее остальных, вместо пистолета может взять дробовик.",
                primaries = new[] { "M4A1", "AK-12", "AKM", "HK416", "Mk 47 Mutant", "QBZ-191", "M249" },
                secondaries = new[] { "Glock 17", "P226", "Desert Eagle", "Kimber K6s", "Remington 870", "Super 90" },
                abilityName = "Адреналин", abilityDesc = "Укол адреналина: +25 здоровья, 8 секунд ещё быстрее бег и перезарядка.",
                moveMul = 1.07f, reloadSpeed = 1.15f, cooldown = 30f, duration = 8f,
            },
            new ClassInfo
            {
                id = PlayerClass.Medic, name = "Медик", glyph = "+", color = new Color(0.35f, 1f, 0.5f),
                desc = "Пистолеты-пулемёты. Лечит себя аптечкой и поднимает павших союзников, пока они лежат на поле боя.",
                primaries = new[] { "KRISS Vector", "SIG MPX", "Scorpion EVO 3", "СР-2 «Вереск»", "UMP-45" },
                secondaries = Pistols,
                abilityName = "Аптечка", abilityDesc = "Лечит 60 здоровья. Рядом с павшим союзником — зажмите, чтобы поднять его.",
                cooldown = 16f,
            },
            new ClassInfo
            {
                id = PlayerClass.Engineer, name = "Инженер", glyph = "И", color = new Color(1f, 0.85f, 0.25f),
                desc = "Карабины и дробовики. С собой РПГ-7: реактивные гранаты против групп и укрытий.",
                primaries = new[] { "Mk18 Mod 1", "Honey Badger", "Remington 870", "Super 90" },
                secondaries = Pistols,
                gadget = "РПГ-7",
                abilityName = "РПГ-7", abilityDesc = "Достать гранатомёт (ещё раз — убрать). 4 выстрела, пополняются у ящиков и при возрождении.",
            },
            new ClassInfo
            {
                id = PlayerClass.Sniper, name = "Снайпер", glyph = "С", color = new Color(0.55f, 0.75f, 1f),
                desc = "Только снайперские винтовки. Разведка: на несколько секунд видит противников сквозь стены.",
                primaries = new[] { "M700", "AWM", "AXMC", "СВД", "ВСС «Винторез»", "M14 EBR", "Barrett M82" },
                secondaries = Pistols,
                abilityName = "Разведка", abilityDesc = "6 секунд видно противников в 80 м сквозь стены.",
                cooldown = 32f, duration = 6f,
            },
        };

        public static ClassInfo Get(PlayerClass c) => All[Mathf.Clamp((int)c, 0, All.Length - 1)];

        static bool Has(string[] list, string w) { if (list == null || string.IsNullOrEmpty(w)) return false; foreach (var x in list) if (x == w) return true; return false; }

        public static bool AllowsPrimary(PlayerClass c, string w) => Has(Get(c).primaries, w);
        public static bool AllowsSecondary(PlayerClass c, string w) => Has(Get(c).secondaries, w);

        /// <summary>The first weapon of the list that exists in the catalog.</summary>
        static string First(string[] list)
        {
            foreach (var x in list) if (WeaponCatalog.Get(x) != null) return x;
            return list.Length > 0 ? list[0] : "";
        }

        public static string DefaultPrimary(PlayerClass c) => First(Get(c).primaries);
        public static string DefaultSecondary(PlayerClass c) => First(Get(c).secondaries);

        public static PlayerClass RandomForBot() => (PlayerClass)Random.Range(0, All.Length);

        /// <summary>A bot's primary: any of the class's weapons that exist in the catalog.</summary>
        public static string RandomPrimary(PlayerClass c)
        {
            var list = Get(c).primaries;
            for (int tries = 0; tries < 8; tries++)
            {
                var w = list[Random.Range(0, list.Length)];
                if (WeaponCatalog.Get(w) != null) return w;
            }
            return DefaultPrimary(c);
        }
    }

    /// <summary>A medic brings a fallen ally back: on the machine that simulates the ally (else sent there).</summary>
    public static class Revives
    {
        /// <summary>Max distance (m) between the medic and the fallen ally.</summary>
        public const float Reach = 2.6f;
        /// <summary>Seconds the medic holds the key.</summary>
        public const float HoldTime = 1.1f;

        public static bool CanRevive(Combatant medic, Combatant target) =>
            medic && target && medic.alive && !target.alive && Teams.Active && Teams.Friendly(medic, target)
            && (!ObjectiveMode.Current || ObjectiveMode.Current.CanRevive(target));

        public static void Apply(Combatant target, Combatant medic)
        {
            if (!target || target.alive) return;
            if (target.remote) { NetGame.SendRevive(target, medic); return; }
            var ph = target.GetComponent<PlayerHealth>();
            if (ph) { ph.Revive(medic); return; }
            var bot = target.GetComponent<CombatBot>();
            if (bot) bot.Revive(medic);
        }
    }
}
