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

        /// <summary>
        /// Host: the team of a human player — the one it picked itself (<see cref="RequestSwitch"/>), else host = id 1 →
        /// team 0, then alternating by join order. Offline the local player is id 0.
        /// </summary>
        public static int AssignHuman(int netId)
        {
            if (!IsTeamMode(MatchSettings.mode)) return -1;
            return picked.TryGetValue(netId, out int t) ? t : Mathf.Max(0, netId - 1) % 2;
        }

        // ------------------------------------------------------------------ switching sides

        /// <summary>Teams the human players picked themselves, by net id (0 = offline). Kept for the session: a restarted
        /// match keeps them; a new network session clears them (<see cref="ClearPicks"/>).</summary>
        static readonly System.Collections.Generic.Dictionary<int, int> picked = new System.Collections.Generic.Dictionary<int, int>();
        static float lastSwitch = -100f;

        /// <summary>Seconds between two team switches of the local player.</summary>
        public const float SwitchCooldown = 3f;

        public static void Pick(int netId, int team)
        {
            if (team == 0 || team == 1) picked[netId] = team;
        }

        public static void ClearPicks() => picked.Clear();

        /// <summary>The local player can switch sides now (a team game is running and the last switch is not too recent).</summary>
        public static bool CanSwitch
        {
            get
            {
                var me = PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null;
                return Active && me && me.team >= 0 && !MatchManager.Instance.Ended && Time.unscaledTime - lastSwitch >= SwitchCooldown;
            }
        }

        /// <summary>Seconds until the local player may switch sides again.</summary>
        public static float SwitchWait => Mathf.Max(0f, SwitchCooldown - (Time.unscaledTime - lastSwitch));

        /// <summary>
        /// The local player goes over to the other team. Offline and on the host it happens at once (the host tells everybody
        /// in the roster); a client asks the host, and switches when the roster comes back with its new team. The player
        /// then starts again on the new side without a death on the scoreboard (PlayerHealth.ChangedTeam); bots of the
        /// bigger team move over at their next spawn, so the sides stay even (MatchManager).
        /// </summary>
        public static bool RequestSwitch()
        {
            if (!CanSwitch) return false;
            var me = PlayerHealth.Local.GetComponent<Combatant>();
            int team = 1 - me.team;
            lastSwitch = Time.unscaledTime;
            if (Net.IsClient) { NetGame.SendTeamRequest(team); return true; }
            Pick(Net.IsHost ? NetGame.I.LocalId : 0, team);
            me.team = team;
            NetGame.TeamsChanged();
            PlayerHealth.Local.ChangedTeam();
            return true;
        }

        /// <summary>
        /// Host / offline: a bot about to (re)spawn on a team two or more soldiers bigger than the other (a player
        /// switched sides) goes over to the smaller one. True if it moved (the roster must be sent again).
        /// </summary>
        public static bool BalanceBot(Combatant bot)
        {
            if (!Active || !bot || bot.isPlayer || bot.remote || bot.team < 0 || Net.IsClient || !bot.GetComponent<CombatBot>()) return false;
            int mine = 0, other = 0;
            foreach (var c in Combatant.All)
            {
                if (!c || c.team < 0) continue;
                if (c.team == bot.team) mine++; else other++;
            }
            if (mine - other < 2) return false;
            bot.team = 1 - bot.team;
            return true;
        }

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
