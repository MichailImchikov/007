using System.Collections.Generic;
using System.Text;
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
    /// The player's chosen loadout (saved): primary, secondary (pistol) and melee weapon, plus the attachments of every
    /// modular weapon. Chosen in the main menu before a match, or in the pause menu during one — a change made in a
    /// match is applied on the next respawn.
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

        // the class, and per class its own primary and secondary (switching class brings back what that class carried)
        const string KeyClass = "fps.loadout.class", KeyMelee = "fps.loadout.melee";
        static string KeyPrimary(PlayerClass c) => "fps.loadout." + c + ".primary";
        static string KeySecondary(PlayerClass c) => "fps.loadout." + c + ".secondary";
        static PlayerClass? cls;
        static string melee;

        /// <summary>The soldier class (applied on the next spawn, like the weapons).</summary>
        public static PlayerClass Class
        {
            get
            {
                if (cls == null) cls = (PlayerClass)Mathf.Clamp(PlayerPrefs.GetInt(KeyClass, 0), 0, ClassDB.All.Length - 1);
                return cls.Value;
            }
            set
            {
                cls = value;
                PlayerPrefs.SetInt(KeyClass, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static string Primary
        {
            get
            {
                var c = Class;
                string p = PlayerPrefs.GetString(KeyPrimary(c), "");
                return IsPrimary(p) ? p : ClassDB.DefaultPrimary(c);
            }
            set
            {
                if (!IsPrimary(value)) return;
                PlayerPrefs.SetString(KeyPrimary(Class), value);
                PlayerPrefs.Save();
            }
        }

        public static string Secondary
        {
            get
            {
                var c = Class;
                string s = PlayerPrefs.GetString(KeySecondary(c), "");
                return IsSecondary(s) ? s : ClassDB.DefaultSecondary(c);
            }
            set
            {
                if (!IsSecondary(value)) return;
                PlayerPrefs.SetString(KeySecondary(Class), value);
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

        /// <summary>A primary the current class may carry (and the catalog has).</summary>
        public static bool IsPrimary(string n) => WeaponCatalog.Get(n) != null && ClassDB.AllowsPrimary(Class, n);

        /// <summary>A secondary the current class may carry: pistols, and for the assault class shotguns too.</summary>
        public static bool IsSecondary(string n) => WeaponCatalog.Get(n) != null && ClassDB.AllowsSecondary(Class, n);

        public static WeaponInfo Info(string n)
        {
            foreach (var w in Weapons) if (w.name == n) return w;
            return Weapons[0];
        }

        // ------------------------------------------------------------------ attachments (per weapon, per slot)

        // compact text: "M4A1:Optic=Optic_Holo,Muzzle=;Glock 17:Optic=Optic_PistolDot" — a slot that is not listed keeps
        // the weapon's default attachment, an empty id means "nothing mounted"
        const string KeyAttachments = "fps.loadout.attachments";
        static Dictionary<string, Dictionary<string, string>> attachments;

        static void LoadAttachments()
        {
            if (attachments != null) return;
            attachments = new Dictionary<string, Dictionary<string, string>>();
            string s = PlayerPrefs.GetString(KeyAttachments, "");
            foreach (var entry in s.Split(';'))
            {
                int c = entry.IndexOf(':');
                if (c <= 0) continue;
                var slots = new Dictionary<string, string>();
                foreach (var kv in entry.Substring(c + 1).Split(','))
                {
                    int e = kv.IndexOf('=');
                    if (e > 0) slots[kv.Substring(0, e)] = kv.Substring(e + 1);
                }
                attachments[entry.Substring(0, c)] = slots;
            }
        }

        static void SaveAttachments()
        {
            var sb = new StringBuilder();
            foreach (var w in attachments)
            {
                if (w.Value.Count == 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(w.Key).Append(':');
                bool first = true;
                foreach (var kv in w.Value)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(kv.Key).Append('=').Append(kv.Value);
                }
            }
            PlayerPrefs.SetString(KeyAttachments, sb.ToString());
            PlayerPrefs.Save();
        }

        /// <summary>Saved attachment id of a weapon slot: null = never chosen (the weapon's default), "" = nothing mounted.</summary>
        public static string GetAttachment(string weapon, string slot)
        {
            if (string.IsNullOrEmpty(weapon) || string.IsNullOrEmpty(slot)) return null;
            LoadAttachments();
            return attachments.TryGetValue(weapon, out var slots) && slots.TryGetValue(slot, out var id) ? id : null;
        }

        /// <summary>Chooses an attachment for a weapon slot ("" = none, null = back to the default). Applied on (re)spawn.</summary>
        public static void SetAttachment(string weapon, string slot, string id)
        {
            if (string.IsNullOrEmpty(weapon) || string.IsNullOrEmpty(slot)) return;
            LoadAttachments();
            if (!attachments.TryGetValue(weapon, out var slots)) attachments[weapon] = slots = new Dictionary<string, string>();
            if (id == null) slots.Remove(slot);
            else slots[slot] = Clean(id);
            SaveAttachments();
        }

        /// <summary>Every slot of a weapon back to its default attachment.</summary>
        public static void ResetAttachments(string weapon)
        {
            LoadAttachments();
            if (weapon != null && attachments.Remove(weapon)) SaveAttachments();
        }

        static string Clean(string id) => id.Replace(";", "").Replace(",", "").Replace(":", "").Replace("=", "");
    }
}
