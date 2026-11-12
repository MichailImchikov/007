using UnityEngine;

namespace FPS
{
    /// <summary>Menu-side description of a weapon (stats normalized 0..1 for the loadout cards).</summary>
    public struct WeaponInfo
    {
        public string name, kind, desc;
        public float damage, fireRate, range, control, mobility;
        public int mag;
        public bool secondary;
    }

    /// <summary>
    /// The player's chosen loadout (saved). Chosen in the main menu before a match, or in the pause menu
    /// during one — a change made in a match is applied on the next respawn.
    /// </summary>
    public static class PlayerLoadout
    {
        public static readonly WeaponInfo[] Weapons =
        {
            new WeaponInfo { name = "M4A1", kind = "Штурмовая винтовка", mag = 30, damage = 0.45f, fireRate = 0.76f, range = 0.55f, control = 0.7f, mobility = 0.62f,
                             desc = "Универсал: точная очередь на средней дистанции, коллиматорный прицел, переключение режима огня." },
            new WeaponInfo { name = "SMG-9", kind = "Пистолет-пулемёт", mag = 32, damage = 0.33f, fireRate = 1f, range = 0.28f, control = 0.62f, mobility = 0.85f,
                             desc = "Бесшумный и самый скорострельный. Короткие коридоры, склады и забеги через фланг." },
            new WeaponInfo { name = "M870", kind = "Помповый дробовик", mag = 7, damage = 1f, fireRate = 0.1f, range = 0.12f, control = 0.3f, mobility = 0.55f,
                             desc = "9 картечин за выстрел: убивает в упор с одного выстрела, на дистанции бесполезен." },
            new WeaponInfo { name = "DMR-7", kind = "Марксманская винтовка", mag = 10, damage = 0.8f, fireRate = 0.33f, range = 1f, control = 0.45f, mobility = 0.42f,
                             desc = "Оптика 6×, 58 урона за выстрел: два попадания в тело, одно в голову. Для длинных линий." },
            new WeaponInfo { name = "P-17", kind = "Пистолет", mag = 15, damage = 0.42f, fireRate = 0.5f, range = 0.3f, control = 0.75f, mobility = 1f, secondary = true,
                             desc = "Быстро достаётся. Добивает, когда кончился магазин основного оружия." },
        };

        const string Key = "fps.loadout.primary";
        static string primary;

        public static string Primary
        {
            get
            {
                if (primary == null) primary = PlayerPrefs.GetString(Key, "M4A1");
                if (!IsPrimary(primary)) primary = "M4A1";
                return primary;
            }
            set
            {
                if (!IsPrimary(value)) return;
                primary = value;
                PlayerPrefs.SetString(Key, value);
                PlayerPrefs.Save();
            }
        }

        public static string Secondary => "P-17";

        public static bool IsPrimary(string n)
        {
            foreach (var w in Weapons) if (w.name == n && !w.secondary) return true;
            return false;
        }

        public static WeaponInfo Info(string n)
        {
            foreach (var w in Weapons) if (w.name == n) return w;
            return Weapons[0];
        }
    }
}
