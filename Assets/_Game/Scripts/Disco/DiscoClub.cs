using UnityEngine;

namespace FPS
{
    /// <summary>
    /// The disco map's rules: fills the dance floors with a crowd of dancing mannequins (the same character models
    /// the players wear) and hides the nameplates over other players (NetProxy), so a player in the crowd looks
    /// exactly like a mannequin. Kill feed and scoreboard keep the names. The head in the sky is DiscoHead.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class DiscoClub : MonoBehaviour
    {
        public const string RocketWeapon = "Ракета Головы";

        public static DiscoClub Instance { get; private set; }
        /// <summary>No nameplates over characters on this map.</summary>
        public static bool HideNameplates => Instance;

        [Tooltip("Humanoid idle clips the mannequins play (directly, via Playables); the dance is added on top.")]
        public AnimationClip[] idleClips = new AnimationClip[0];
        public Transform[] dancerSpots = new Transform[0];
        public int maxDancers = 90;
        /// <summary>Play area (x/z) the head aims its rockets into.</summary>
        public Vector2 areaMin = new Vector2(-40f, -28f), areaMax = new Vector2(40f, 28f);

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            // the same crowd on every machine of a LAN game (players hide behind the same mannequins); random offline
            var rnd = Net.Active ? new System.Random(20261113) : new System.Random(Random.Range(0, int.MaxValue));
            int models = CharacterModelDB.Count;
            if (models <= 0) { Debug.LogWarning("[Disco] no character models: the crowd stays empty"); return; }
            var root = new GameObject("Crowd").transform;
            int spawned = 0;
            foreach (var spot in dancerSpots)
            {
                if (!spot || spawned >= maxDancers) continue;
                var room = spot.GetComponentInParent<DiscoRoom>();
                var d = DiscoDancer.Spawn(spot.position, spot.eulerAngles.y + (float)(rnd.NextDouble() * 40.0 - 20.0), room,
                                          rnd.Next(1, models + 1), idleClips, rnd);
                if (!d) continue;
                d.transform.SetParent(root, true);
                spawned++;
            }
        }
    }
}
