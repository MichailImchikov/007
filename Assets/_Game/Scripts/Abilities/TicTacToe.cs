using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// "Крестики-нолики" ability (replaced the minesweeper): the caster drags the nearest enemy into a game of
    /// tic-tac-toe. Both freeze where they stand and are untouchable for the whole game; the caster plays X and
    /// moves first. The loser blows up, a draw lets both go. A human clicks the cells (10 s per move, then a random
    /// one is made for him), a bot thinks a moment and plays well or sloppily by its difficulty.
    /// Local simulation only: combatants living on another machine are never picked.
    /// </summary>
    public class TicTacToe : MonoBehaviour
    {
        public static readonly List<TicTacToe> Active = new List<TicTacToe>();
        public const string Weapon = "Крестики-нолики";
        public const float MoveTime = 10f;

        public class Side
        {
            public Combatant c;
            public PlayerHealth ph;
            public CombatBot bot;
            public TrainingBot dummy;
            public float skill; // chance of the best move for an AI side
            public bool IsLocalHuman => ph && ph == PlayerHealth.Local;
        }

        public Side X { get; private set; }
        public Side O { get; private set; }
        /// <summary>0 empty, 1 X, 2 O.</summary>
        public readonly int[] board = new int[9];
        /// <summary>1 = X to move, 2 = O to move.</summary>
        public int Turn { get; private set; } = 1;
        /// <summary>0 playing, 1 X won, 2 O won, 3 draw.</summary>
        public int Result { get; private set; }
        public int[] WinLine { get; private set; }
        public float MoveLeft => moveT;
        public float EndT => endT;

        float moveT, thinkT, endT;
        bool released;
        static bool allowKill;

        static readonly int[][] Lines =
        {
            new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 }, new[] { 0, 3, 6 },
            new[] { 1, 4, 7 }, new[] { 2, 5, 8 }, new[] { 0, 4, 8 }, new[] { 2, 4, 6 }
        };

        // ------------------------------------------------------------------ queries

        public static TicTacToe Of(Combatant c)
        {
            if (!c) return null;
            foreach (var g in Active) if (g && !g.released && (g.X.c == c || g.O.c == c)) return g;
            return null;
        }

        public static bool InGame(Combatant c) => Of(c) != null;

        /// <summary>Damage hook: both players are untouchable while the game lasts (the loser's blast excepted).</summary>
        public static bool Protects(Combatant victim) => !allowKill && InGame(victim);

        public Side Me(Combatant c) => X.c == c ? X : O.c == c ? O : null;
        public Side Other(Side s) => s == X ? O : X;
        public int Mark(Side s) => s == X ? 1 : 2;

        // ------------------------------------------------------------------ start

        public static bool Begin(Combatant caster, float radius, out string why)
        {
            why = null;
            if (!caster || !caster.alive) return false;
            Combatant best = null;
            float bestD = radius;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || c.remote || c.IsClone || !caster.IsEnemyOf(c)) continue;
                if (InGame(c) || KnifeDuel.InDuel(c) || SkyArena.IsGunner(c)) continue;
                if (!c.GetComponent<PlayerHealth>() && !c.GetComponent<CombatBot>() && !c.GetComponent<TrainingBot>()) continue;
                float d = Vector3.Distance(c.transform.position, caster.transform.position);
                if (d < bestD) { bestD = d; best = c; }
            }
            if (!best) { why = $"Нет соперника рядом ({radius:0} м)"; return false; }
            var go = new GameObject("TicTacToe");
            go.AddComponent<TicTacToe>().Init(caster, best);
            return true;
        }

        Side MakeSide(Combatant c)
        {
            var s = new Side { c = c, ph = c.GetComponent<PlayerHealth>(), bot = c.GetComponent<CombatBot>(), dummy = c.GetComponent<TrainingBot>() };
            s.skill = s.bot ? (s.bot.difficulty == 2 ? 0.9f : s.bot.difficulty == 0 ? 0.35f : 0.65f) : 0.55f;
            return s;
        }

        void Init(Combatant caster, Combatant victim)
        {
            X = MakeSide(caster);
            O = MakeSide(victim);
            Active.Add(this);
            foreach (var s in new[] { X, O }) Freeze(s);
            StartTurn();
            foreach (var s in new[] { X, O })
            {
                Vector3 p = s.c.transform.position + Vector3.up * 1.2f;
                FX.Ensure().Puff(p, Vector3.up, 10, 1f, new Vector2(0.3f, 1.2f), new Vector2(0.4f, 0.8f), new Vector2(0.4f, 0.8f),
                                 new Color(0.95f, 0.95f, 1f, 0.5f), new Color(0.7f, 0.8f, 1f, 0.4f));
                ProcAudio.PlayAt(Sfx.ErrorTone, p, 0.6f, 0.05f);
            }
            if (X.IsLocalHuman || O.IsLocalHuman) TicTacToeHUD.Ensure().Open(this);
        }

        void Freeze(Side s)
        {
            if (s.c.abilities) s.c.abilities.ClearStatus();
            if (s.ph)
            {
                s.ph.motor.Locked = true;
                if (s.IsLocalHuman) GameInput.ModalUI = true; // free cursor for the board
            }
            else if (s.bot) { s.bot.CancelMelee(); s.bot.ExternalControl = true; }
            else if (s.dummy) s.dummy.ExternalControl = true;
            // face each other
            var o = Other(s);
            Vector3 to = o.c.transform.position - s.c.transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f)
            {
                if (s.ph) s.ph.playerCam.SetLook(Quaternion.LookRotation(to).eulerAngles.y, s.ph.playerCam.Pitch);
                else s.c.transform.rotation = Quaternion.LookRotation(to);
            }
        }

        void Release(Side s)
        {
            if (!s.c) return;
            if (s.ph)
            {
                if (!s.ph.IsDead) s.ph.motor.Locked = false;
                if (s.IsLocalHuman)
                {
                    GameInput.ModalUI = false;
                    if (!s.ph.IsDead) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
                }
            }
            else if (s.bot && s.bot.IsAlive) { s.bot.ExternalControl = false; s.bot.Stun(0.4f); }
            else if (s.dummy && s.dummy.IsAlive) s.dummy.ExternalControl = false;
        }

        // ------------------------------------------------------------------ play

        public Side ToMove => Turn == 1 ? X : O;

        void StartTurn()
        {
            moveT = MoveTime;
            thinkT = Random.Range(0.6f, 1.3f);
        }

        /// <summary>The local human clicked a cell. False if it is not his move or the cell is taken.</summary>
        public bool TryHumanMove(Combatant who, int cell)
        {
            if (Result != 0 || released) return false;
            var s = ToMove;
            if (s.c != who || cell < 0 || cell > 8 || board[cell] != 0) return false;
            Play(cell);
            return true;
        }

        void Play(int cell)
        {
            board[cell] = Turn;
            ProcAudio.Play(Sfx.Click, 0.7f, 0.05f);
            int w = Winner(board, out var line);
            if (w != 0)
            {
                Result = w;
                WinLine = line;
                endT = 1.6f;
                ProcAudio.Play(Sfx.AbilityReady, 0.6f, 0f);
                return;
            }
            if (Full(board)) { Result = 3; endT = 1.4f; ProcAudio.Play(Sfx.Dry, 0.6f, 0f); return; }
            Turn = 3 - Turn;
            StartTurn();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || released) return;
            if (!Valid(X) || !Valid(O) || (MatchManager.Instance && MatchManager.Instance.Ended) || TimeRewind.Playing) { Finish(true); return; }
            KeepFrozen(X); KeepFrozen(O);

            if (Result != 0)
            {
                endT -= dt;
                if (endT <= 0f) Finish(false);
                return;
            }
            var s = ToMove;
            moveT -= dt;
            if (s.IsLocalHuman)
            {
                if (moveT <= 0f) Play(RandomFree()); // out of time: a random move
                return;
            }
            thinkT -= dt;
            if (thinkT <= 0f) Play(Random.value < s.skill ? BestMove(board, Turn) : RandomFree());
        }

        bool Valid(Side s) => s.c && s.c.gameObject.activeInHierarchy && s.c.alive;

        void KeepFrozen(Side s)
        {
            if (s.ph)
            {
                s.ph.motor.Locked = true;
                if (s.IsLocalHuman) GameInput.ModalUI = true;
            }
            else if (s.bot && !s.bot.ExternalControl) s.bot.ExternalControl = true;
            else if (s.dummy && !s.dummy.ExternalControl) s.dummy.ExternalControl = true;
        }

        void Finish(bool aborted)
        {
            if (released) return;
            released = true;
            Release(X);
            Release(O);
            Active.Remove(this);
            if (!aborted && (Result == 1 || Result == 2))
            {
                var winner = Result == 1 ? X : O;
                var loser = Other(winner);
                if (loser.c && loser.c.alive) Blow(loser.c, winner.c);
            }
            if (TicTacToeHUD.Instance) TicTacToeHUD.Instance.Closed(this);
            Destroy(gameObject);
        }

        /// <summary>The loser goes off like a mine — only the loser (the winner may stand right next to him).</summary>
        static void Blow(Combatant loser, Combatant winner)
        {
            Vector3 p = loser.transform.position + Vector3.up * 0.4f;
            Explosions.Visual(p, 4f, 8f, winner);
            var d = loser.GetComponent<IDamageable>();
            allowKill = true;
            try { d?.TakeDamage(new DamageInfo { amount = 9999f, point = loser.Chest, direction = Vector3.up, attacker = winner, weapon = Weapon, critical = true }); }
            finally { allowKill = false; }
        }

        void OnDestroy()
        {
            Active.Remove(this);
            if (!released)
            {
                released = true;
                if (X != null) Release(X);
                if (O != null) Release(O);
                if (TicTacToeHUD.Instance) TicTacToeHUD.Instance.Closed(this);
            }
        }

        // ------------------------------------------------------------------ rules & AI

        int RandomFree()
        {
            var free = new List<int>();
            for (int i = 0; i < 9; i++) if (board[i] == 0) free.Add(i);
            return free.Count > 0 ? free[Random.Range(0, free.Count)] : 0;
        }

        public static int Winner(int[] b, out int[] line)
        {
            foreach (var l in Lines)
                if (b[l[0]] != 0 && b[l[0]] == b[l[1]] && b[l[1]] == b[l[2]]) { line = l; return b[l[0]]; }
            line = null;
            return 0;
        }

        static bool Full(int[] b) { foreach (var v in b) if (v == 0) return false; return true; }

        /// <summary>Perfect play (minimax); among equal moves picks at random so games differ.</summary>
        static int BestMove(int[] b, int me)
        {
            int best = int.MinValue;
            var picks = new List<int>();
            for (int i = 0; i < 9; i++)
            {
                if (b[i] != 0) continue;
                b[i] = me;
                int v = -Negamax(b, 3 - me, 1);
                b[i] = 0;
                if (v > best) { best = v; picks.Clear(); }
                if (v == best) picks.Add(i);
            }
            return picks.Count > 0 ? picks[Random.Range(0, picks.Count)] : 0;
        }

        static int Negamax(int[] b, int toMove, int depth)
        {
            int w = Winner(b, out _);
            if (w != 0) return w == toMove ? 10 - depth : depth - 10;
            if (Full(b)) return 0;
            int best = int.MinValue;
            for (int i = 0; i < 9; i++)
            {
                if (b[i] != 0) continue;
                b[i] = toMove;
                int v = -Negamax(b, 3 - toMove, depth + 1);
                b[i] = 0;
                if (v > best) best = v;
            }
            return best;
        }
    }
}
