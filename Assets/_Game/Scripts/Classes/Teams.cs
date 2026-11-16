using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Team Deathmatch rules: two teams, no friendly fire, the team score is its members' kills, a fallen soldier lies
    /// for <see cref="ReviveWindow"/> seconds so a medic can bring him back. Free-for-all modes keep team = -1.
    /// Teams of humans are dealt by the host (<see cref="AssignHuman"/>, replicated in the roster); bots join the smaller
    /// team when they spawn (MatchManager).
    /// </summary>
    public static class Teams
    {
        public static readonly string[] Names = { "Синие", "Красные" };
        public static readonly Color[] Colors = { new Color(0.32f, 0.62f, 1f), new Color(1f, 0.36f, 0.3f) };

        /// <summary>Seconds a fallen soldier waits for a medic before respawning (Space respawns at once).</summary>
        public const float ReviveWindow = 7f;

        public static bool Active => IsTeamMode(MatchSettings.mode) && MatchManager.Instance;

        /// <summary>Team deathmatch and the objective modes (capture points, bomb, assault).</summary>
        public static bool IsTeamMode(GameMode m) => m == GameMode.TeamDeathmatch || ObjectiveMode.IsObjective(m);

        /// <summary>Same team in a team game (never in free-for-all): no damage between them, a medic can revive.</summary>
        public static bool Friendly(Combatant a, Combatant b) => a && b && a != b && ((a.team >= 0 && a.team == b.team) || a.Root == b.Root);

        /// <summary>Host: the team of a human player (host = id 1 → team 0, then alternating by join order).</summary>
        public static int AssignHuman(int netId) => IsTeamMode(MatchSettings.mode) ? Mathf.Max(0, netId - 1) % 2 : -1;

        /// <summary>The team with fewer members (ties: team 0), ignoring <paramref name="except"/>.</summary>
        public static int SmallerTeam(Combatant except = null)
        {
            int a = 0, b = 0;
            foreach (var c in Combatant.All)
            {
                if (!c || c == except || c.IsClone) continue;
                if (c.team == 0) a++; else if (c.team == 1) b++;
            }
            return b < a ? 1 : 0;
        }

        public static int Score(int team)
        {
            if (ObjectiveMode.Current) return ObjectiveMode.Current.TeamScore(team); // points, rounds, captured points
            int s = 0;
            foreach (var c in Combatant.All) if (c && c.team == team) s += c.kills;
            return s;
        }

        /// <summary>Team kills that end a team match (twice the free-for-all kill limit: the whole team shoots).</summary>
        public static int KillLimit => MatchSettings.killLimit * 2;

        public static Color ColorOf(int team) => team >= 0 && team < Colors.Length ? Colors[team] : Color.white;

        /// <summary>Colour of a combatant as seen by the local player: allies blue, enemies red; free-for-all: null.</summary>
        public static Color? RelationColor(Combatant c)
        {
            if (!Active || !c) return null;
            var me = PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null;
            if (!me || me.team < 0) return null;
            return c.team == me.team ? Colors[0] : Colors[1];
        }
    }
}
