using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// A participant of a match (local player or bot; later — network players).
    /// Holds identity and score; health lives in PlayerHealth / CombatBot.
    /// </summary>
    public class Combatant : MonoBehaviour
    {
        public static readonly List<Combatant> All = new List<Combatant>();

        public string displayName = "Игрок";
        public bool isPlayer;
        public int team = -1; // -1 = free for all
        /// <summary>Soldier class (weapons, passives, class ability): the player's choice, a bot's draw.</summary>
        public PlayerClass playerClass;
        public int kills, deaths;
        /// <summary>Kills since the last death (KillStreak): the comic look grows with it.</summary>
        public int streak;
        public bool alive = true;
        /// <summary>Ability slot of this participant (set by AbilityUser).</summary>
        public AbilityUser abilities;
        /// <summary>Time of the last shot fired (reveals invisible shooters).</summary>
        public float lastShotTime = -99f;

        /// <summary>Network entity id (0 = not networked). Players: 1 + clientId, bots: 1000+.</summary>
        public int netId;
        /// <summary>Simulated on another machine; this copy is a NetProxy driven by snapshots.</summary>
        public bool remote;
        /// <summary>A human player (local or remote), as opposed to a bot.</summary>
        public bool human;
        /// <summary>Appearance (customization) of this character.</summary>
        public CharacterLook look;

        /// <summary>Points other combatants aim at (set by the owner).</summary>
        public Func<Vector3> chestPoint, headPoint;

        public Vector3 Chest => chestPoint != null ? chestPoint() : transform.position + Vector3.up * 1.3f;
        public Vector3 Head => headPoint != null ? headPoint() : transform.position + Vector3.up * 1.7f;

        /// <summary>Shadow clone: the combatant it copies (its kills go to the original; never on the scoreboard).</summary>
        [NonSerialized] public Combatant cloneOf;
        public bool IsClone => cloneOf;
        /// <summary>The real fighter behind this body (itself, or the original of a shadow clone).</summary>
        public Combatant Root => cloneOf ? cloneOf : this;

        public bool IsEnemyOf(Combatant other) => other && other != this && Root != other.Root && (team < 0 || team != other.team);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}
