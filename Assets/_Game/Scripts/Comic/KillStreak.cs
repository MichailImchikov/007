using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Kills in a row without dying, for every combatant (Combatant.streak) and for the local player (works in the range
    /// too, where no match counts kills). The comic screen effects (ComicFX) and the comic look others see on a
    /// character (ComicAvatar) grow with it. Every client counts it from the same kill events, so nothing extra goes
    /// over the network.
    /// </summary>
    public static class KillStreak
    {
        /// <summary>The local player's streak.</summary>
        public static int Local { get; private set; }
        /// <summary>Time of the local player's last kill (unscaled).</summary>
        public static float LastLocalKill { get; private set; } = -99f;
        /// <summary>The local player killed: the streak after it.</summary>
        public static event Action<int> LocalKill;
        /// <summary>The local player's streak ended (death).</summary>
        public static event Action LocalReset;
        /// <summary>A combatant killed (anyone): the combatant and its streak after it.</summary>
        public static event Action<Combatant, int> Kill;

        /// <summary>A kill registered by the match (host or client).</summary>
        public static void OnKill(Combatant killer, Combatant victim)
        {
            if (victim)
            {
                victim.streak = 0;
                if (victim.isPlayer && !victim.remote) ResetLocal();
            }
            if (!killer || killer == victim) return;
            killer.streak++;
            Kill?.Invoke(killer, killer.streak);
            if (killer.isPlayer && !killer.remote) AddLocal();
        }

        /// <summary>The local player killed something outside a match (the range).</summary>
        public static void OnLocalKillNoMatch(Combatant self)
        {
            if (self) { self.streak++; Kill?.Invoke(self, self.streak); }
            AddLocal();
        }

        /// <summary>The local player died.</summary>
        public static void ResetLocal()
        {
            if (Local == 0) return;
            Local = 0;
            LocalReset?.Invoke();
        }

        static void AddLocal()
        {
            Local++;
            LastLocalKill = Time.unscaledTime;
            LocalKill?.Invoke(Local);
        }

        /// <summary>0..1 from a streak: the first kills count most (1 → 0.28, 3 → 0.63, 5 → 0.81, 8 → 0.93).</summary>
        public static float Level(int streak) => 1f - Mathf.Exp(-streak / 3f);
    }
}
