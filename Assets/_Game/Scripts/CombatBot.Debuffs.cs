using UnityEngine;

namespace FPS
{
    /// <summary>
    /// What the newer enemy abilities do to a bot: "Табельное" (pistol forced for a while), "Лагалка" (the bot
    /// thinks and moves at 10 frames per second), "Пустой магазин" (mag dumped) — plus the time-rewind setters.
    /// </summary>
    public partial class CombatBot
    {
        float sidearmT, lagAcc;
        string sidearmPrev;
        bool lagChoppy;

        /// <summary>Time rewind: rounds in the magazine.</summary>
        public int RewindAmmo
        {
            get => ammo;
            set { ammo = Mathf.Clamp(value, 0, magSize); reloadTimer = 0f; }
        }

        /// <summary>Time rewind: health as it was (a living bot only).</summary>
        public void SetHealthRewind(float h)
        {
            if (dead) return;
            health = Mathf.Clamp(h, 1f, maxHealth);
        }

        /// <summary>"Пустой магазин": every round in the magazine is gone — reload first.</summary>
        public void EmptyMagazine()
        {
            if (dead) return;
            ammo = 0;
            reloadTimer = 0f;
            burstLeft = 0;
        }

        /// <summary>"Табельное": the pistol comes out for <paramref name="seconds"/>, then the own gun returns.</summary>
        public void ForceSidearm(float seconds)
        {
            if (dead) return;
            if (sidearmT <= 0f)
            {
                var cur = WeaponCatalog.Get(weaponName);
                if (cur != null && cur.category == WeaponCategory.Pistol) return; // already a pistol
                sidearmPrev = weaponName;
                weaponName = "P-17";
                SetupWeapon();
                ProcAudio.PlayAt(Sfx.Switch, transform.position + Vector3.up * 1.2f, 0.7f);
            }
            sidearmT = Mathf.Max(sidearmT, seconds);
        }

        void RestoreSidearm()
        {
            sidearmT = 0f;
            if (string.IsNullOrEmpty(sidearmPrev)) return;
            weaponName = sidearmPrev;
            sidearmPrev = null;
            SetupWeapon();
        }

        /// <summary>Called by Spawn: a fresh life gets its own gun back and no lag.</summary>
        void ResetDebuffs()
        {
            if (sidearmT > 0f || sidearmPrev != null) RestoreSidearm();
            EndChoppy();
            lagAcc = 0f;
        }

        /// <summary>
        /// Update gate. Ticks the sidearm timer; under "Лагалка" lets only every 0.1 s through (with the whole
        /// accumulated dt) and moves the body in steps. False = skip this frame.
        /// </summary>
        bool DebuffGate(ref float dt)
        {
            if (sidearmT > 0f) { sidearmT -= dt; if (sidearmT <= 0f) RestoreSidearm(); }
            bool lag = abilities && abilities.Lagging;
            if (!lag)
            {
                if (lagChoppy) EndChoppy();
                lagAcc = 0f;
                return true;
            }
            if (!lagChoppy && agent.enabled)
            {
                lagChoppy = true;
                agent.updatePosition = false;
            }
            lagAcc += dt;
            if (lagAcc < 0.1f) return false;
            dt = lagAcc;
            lagAcc = 0f;
            if (lagChoppy && agent.enabled) transform.position = agent.nextPosition;
            return true;
        }

        void EndChoppy()
        {
            if (!lagChoppy) return;
            lagChoppy = false;
            if (agent.enabled)
            {
                transform.position = agent.nextPosition;
                agent.updatePosition = true;
            }
            else agent.updatePosition = true;
        }
    }
}
