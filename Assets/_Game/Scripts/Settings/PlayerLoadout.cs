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
        public WeaponCategory category;
    }

    /// <summary>
    /// The player's chosen loadout (saved): primary, secondary (pistol) and melee weapon. Chosen in the main menu
    /// before a match, or in the pause menu during one — a change made in a match is applied on the next respawn.
    /// </summary>
    public static class PlayerLoadout
    {
        static WeaponInfo[] weapons;

        public static WeaponInfo[] Weapons
        {
            get
            {
                if (weapons != null) return weapons;
                var all = WeaponCatalog.All;
                weapons = new WeaponInfo[all.Length];
                for (int i = 0; i < all.Length; i++)
                {
                    var d = all[i];
                    weapons[i] = new WeaponInfo
                    {
                        name = d.name, kind = d.kind, desc = d.desc, mag = d.mag, secondary = d.Secondary, category = d.category,
                        damage = d.uiDamage, fireRate = d.uiRate, range = d.uiRange, control = d.uiControl, mobility = d.uiMobility,
                    };
                }
                return weapons;
            }
        }

        const string Key = "fps.loadout.primary", KeySecondary = "fps.loadout.secondary", KeyMelee = "fps.loadout.melee";
        static string primary, secondary, melee;

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

        public static string Secondary
        {
            get
            {
                if (secondary == null) secondary = PlayerPrefs.GetString(KeySecondary, "P-17");
                if (!IsSecondary(secondary)) secondary = "P-17";
                return secondary;
            }
            set
            {
                if (!IsSecondary(value)) return;
                secondary = value;
                PlayerPrefs.SetString(KeySecondary, value);
                PlayerPrefs.Save();
            }
        }

        public static string Melee
        {
            get
            {
                if (melee == null) melee = PlayerPrefs.GetString(KeyMelee, MeleeCatalog.All[0].name);
                return MeleeCatalog.Get(melee).name;
            }
            set
            {
                melee = MeleeCatalog.Get(value).name;
                PlayerPrefs.SetString(KeyMelee, melee);
                PlayerPrefs.Save();
            }
        }

        public static bool IsPrimary(string n)
        {
            foreach (var w in Weapons) if (w.name == n && !w.secondary) return true;
            return false;
        }

        public static bool IsSecondary(string n)
        {
            foreach (var w in Weapons) if (w.name == n && w.secondary) return true;
            return false;
        }

        public static WeaponInfo Info(string n)
        {
            foreach (var w in Weapons) if (w.name == n) return w;
            return Weapons[0];
        }
    }
}
