using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Network check of the abilities and the knife duel between two machines (run both with -nettest):
    ///   -netabil       the CLIENT casts the newer abilities at the host's player and strikes a knife clash (host = duel authority);
    ///                  add -netabilold for the older abilities instead
    ///   -netabilhost   the HOST casts them at the client's player (client = duel authority)
    ///   -netabilbot    the CLIENT plays against a host BOT (tic-tac-toe, laser, clones, knife clash; start the host with -netbots 1)
    ///   -netabilduel   only the knife clash
    /// Both machines answer on their own (tic-tac-toe moves, F mashing in a duel, a knife swinging when an enemy is close,
    /// beams kept on the target) and log time-stamped lines: "[NetTest] HH:mm:ss.f STEP …" from the caster and
    /// "[NetTest] HH:mm:ss.f STATE …" twice a second from both — Tools/netcheck.py turns the two logs into a verdict.
    /// </summary>
    public partial class NetAutoTest
    {
        static bool abil, abilHostActs, abilBot, abilDuelOnly;
        int abilStep = -1;
        float abilT = 4f, respondT, swingT, abilLogT, knifeT = -1f;

        static readonly AbilityType[] FullPlan =
        {
            AbilityType.Sidearm, AbilityType.Lag, AbilityType.EmptyMag, AbilityType.FartBlind, AbilityType.Hyperspeed,
            AbilityType.ShadowClones, AbilityType.LaserEyes, AbilityType.Rewind, AbilityType.TicTacToe, AbilityType.None, // None = knife clash
        };
        /// <summary>-netabilold: the older abilities as well (hook, swap, invisibility … sky arena).</summary>
        static readonly AbilityType[] OldPlan =
        {
            AbilityType.Grapple, AbilityType.Swap, AbilityType.Invisibility, AbilityType.Phase, AbilityType.ChickenTrap, AbilityType.Parry,
            AbilityType.Shrink, AbilityType.BigHeads, AbilityType.Screamer, AbilityType.Confuse, AbilityType.Launch, AbilityType.BSOD,
            AbilityType.Spin, AbilityType.Build, AbilityType.ButtRocket, AbilityType.SkyArena,
        };
        static bool abilOld;
        static readonly AbilityType[] BotPlan = { AbilityType.TicTacToe, AbilityType.LaserEyes, AbilityType.ShadowClones, AbilityType.None };
        static readonly AbilityType[] DuelPlan = { AbilityType.None };

        static AbilityType[] Plan => abilDuelOnly ? DuelPlan : abilBot ? BotPlan : abilOld ? OldPlan : FullPlan;

        static void ParseAbil(string[] args)
        {
            foreach (var a in args)
            {
                if (a == "-netabil") abil = true;
                if (a == "-netabilhost") { abil = true; abilHostActs = true; }
                if (a == "-netabilbot") { abil = true; abilBot = true; }
                if (a == "-netabilduel") { abil = true; abilDuelOnly = true; }
                if (a == "-netabilold") abilOld = true;
            }
        }

        static void LogT(string s) => Debug.Log("[NetTest] " + DateTime.Now.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture) + " " + s);

        bool IAmActor => Net.Active && Net.IsHost == abilHostActs;

        static Combatant TargetRemote(Vector3 from, bool human)
        {
            Combatant best = null; float bd = float.MaxValue;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.remote || !c.alive || c.human != human || c.IsClone) continue;
                float d = (c.transform.position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        /// <summary>True when the ability pilot took over this frame (the regular Act cycle is skipped).</summary>
        bool AbilityPilot(PlayerHealth ph, float dt)
        {
            if (!abil) return false;
            var me = ph.GetComponent<Combatant>();
            Respond(ph, me, dt);

            abilLogT -= dt;
            if (abilLogT <= 0f) { abilLogT = 0.5f; LogT("STATE " + AbilState(me)); }

            if (!IAmActor || ph.IsDead) return true;
            var target = TargetRemote(me.transform.position, !abilBot);
            if (!target) return true;
            if (knifeT >= 0f)
            {
                // the target has seen us in front of it by now and keeps its blade swinging: strike
                knifeT -= dt;
                if (knifeT < 0f)
                {
                    Vector3 hf = target.transform.forward; hf.y = 0f; hf.Normalize();
                    var d = target.GetComponent<IDamageable>();
                    var r = d.TakeDamage(new DamageInfo { amount = 55f, point = target.Chest, direction = -hf, attacker = me, weapon = "Нож" });
                    KnifeDuel.NoteSwing(me);
                    LogT($"KNIFE hit on {target.displayName}#{target.netId} applied={r.applied}");
                }
                return true;
            }
            abilT -= dt;
            if (abilT > 0f) return true;
            if (TicTacToe.InGame(me) || KnifeDuel.InDuel(me) || me.abilities.Lasering || TimeRewind.Playing || SkyArena.Active) { abilT = 0.5f; return true; }
            var plan = Plan;
            abilStep++;
            if (abilStep >= plan.Length) { if (abilStep == plan.Length) LogT("PLAN done"); return true; }
            var t = plan[abilStep];
            abilT = t == AbilityType.SkyArena ? 24f : t == AbilityType.None ? 14f : t == AbilityType.TicTacToe ? 16f : 9f;

            // stand in front of the target, facing it, at the distance the ability wants
            Vector3 f = target.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            float dist = t == AbilityType.None ? 1.3f : t == AbilityType.FartBlind ? 1.8f
                       : t == AbilityType.Grapple || t == AbilityType.Swap || t == AbilityType.ButtRocket ? 11f : 4f;
            ph.motor.Teleport(target.transform.position + f * dist + Vector3.up * 0.05f);
            Vector3 to = target.Chest - (target.transform.position + f * dist + Vector3.up * ph.motor.EyeHeight);
            ph.playerCam.SetLook(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);

            if (t == AbilityType.None)
            {
                // knife clash: stand in front for a moment (its blade starts swinging), then strike
                knifeT = 1.5f;
                LogT($"STEP {abilStep} KnifeClash target={target.displayName}#{target.netId}");
                return true;
            }
            me.abilities.Give(t, t);
            bool ok = me.abilities.TryActivate();
            LogT($"STEP {abilStep} {t} cast={ok} target={target.displayName}#{target.netId}");
            return true;
        }

        /// <summary>Both machines: answer whatever the other side started.</summary>
        void Respond(PlayerHealth ph, Combatant me, float dt)
        {
            // tic-tac-toe: make a move a moment after it becomes ours
            var g = TicTacToe.Of(me);
            if (g != null && g.Result == 0 && g.ToMove.c == me)
            {
                respondT += dt;
                if (respondT > 0.8f)
                {
                    respondT = 0f;
                    for (int i = 0; i < 9; i++)
                    {
                        int c = (i * 5 + 4) % 9;
                        if (g.board[c] != 0) continue;
                        g.TryHumanMove(me, c);
                        LogT($"TTT move {c}");
                        break;
                    }
                }
            }
            else respondT = 0f;

            // knife duel: mash F (the client a little faster, so the duel has a clear winner)
            var duel = KnifeDuel.Of(me);
            if (duel != null && duel.Phase == 1)
            {
                swingT -= dt;
                if (swingT <= 0f) { swingT = Net.IsHost ? 0.16f : 0.1f; KnifeDuel.TestPress(me); }
            }
            else if (!ph.IsDead)
            {
                // an enemy right in front of us: keep the knife swinging (a knife hit arriving then is a clash)
                foreach (var c in Combatant.All)
                {
                    if (!c || !c.remote || !c.alive || c.IsClone || !me.IsEnemyOf(c)) continue;
                    if (Vector3.Distance(c.transform.position, me.transform.position) > 2.4f) continue;
                    swingT -= dt;
                    if (swingT <= 0f) { swingT = 0.25f; KnifeDuel.NoteSwing(me); }
                    break;
                }
            }

            // laser eyes: keep the beams on the target
            if (me.abilities && me.abilities.Lasering)
            {
                var tgt = TargetRemote(me.transform.position, !abilBot) ?? TargetRemote(me.transform.position, abilBot);
                if (tgt)
                {
                    Vector3 to = tgt.Chest - ph.playerCam.cam.transform.position;
                    ph.playerCam.SetLook(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
                }
            }
        }

        static string B(bool v) => v ? "1" : "0";
        static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        static string AbilState(Combatant me)
        {
            var ph = PlayerHealth.Local;
            var u = me.abilities;
            var lo = ph ? ph.loadout : null;
            var p = me.transform.position;
            var sb = new StringBuilder();
            sb.Append($"id={me.netId} hp={F(ph ? ph.Health : 0f)} dead={B(ph && ph.IsDead)} x={F(p.x)} y={F(p.y)} z={F(p.z)}");
            sb.Append($" gun={(lo && lo.Active ? lo.Active.weaponName.Replace(' ', '_') : "-")} mag={(lo && lo.Active ? lo.Active.Ammo : -1)} forced={F(lo ? lo.ForcedSecondary : 0f)}");
            sb.Append($" lag={B(u && u.Lagging)} fps={Application.targetFrameRate} hyper={B(u && u.Hyper)} laser={B(u && u.Lasering)}");
            sb.Append($" ttt={B(TicTacToe.InGame(me))} duel={B(KnifeDuel.InDuel(me))} rewind={B(TimeRewind.Playing)} arena={B(SkyArena.Active)}");
            sb.Append($" bighead={B(u && u.BigHead)} confused={B(u && u.Confused)} spin={B(u && u.Spinning)} bsod={B(u && u.BSODLeft > 0f)} scream={B(u && u.ScreamT > 0f)}");
            sb.Append($" blind={B(u && u.Blinded)} chicken={B(u && u.IsChicken)} invis={B(u && u.Invisible)} parry={B(u && u.Parrying)} phase={B(u && u.PhaseActive)} scale={F(u ? u.BodyScale : 1f)}");
            sb.Append($" clones={ShadowClone.All.Count} smoke={SmokeCloud.All.Count} pieces={UnityEngine.Object.FindObjectsByType<BuiltPiece>().Length}");
            var g = TicTacToe.Of(me);
            if (g != null) sb.Append($" tttBoard={string.Join("", Array.ConvertAll(g.board, v => v == 0 ? "." : v == 1 ? "X" : "O"))} tttRes={g.Result}");
            var d = KnifeDuel.Of(me);
            if (d != null) sb.Append($" duelPhase={d.Phase} duelProg={d.Progress.ToString("0.00", CultureInfo.InvariantCulture)} duelMirror={B(d.IsMirror)}");
            foreach (var c in Combatant.All)
            {
                if (!c || !c.remote || c.IsClone) continue;
                var px = c.GetComponent<NetProxy>();
                if (!px) continue;
                var st = px.Latest;
                var cu = c.abilities;
                var cp = c.transform.position;
                sb.Append($" | P{c.netId} human={B(c.human)} alive={B(c.alive)} hp={F(st.health * 100f)} x={F(cp.x)} y={F(cp.y)} z={F(cp.z)}");
                sb.Append($" laserFlag={B(st.Has(CharState.Laser))} hyperFlag={B(st.Has(CharState.Hyper))} prot={B(st.Has(CharState.Protected))}");
                sb.Append($" inv={B(cu && cu.Invisible)} chick={B(cu && cu.IsChicken)} sword={B(cu && cu.Parrying)} phase={B(cu && cu.PhaseActive)} scale={F(cu ? cu.BodyScale : 1f)}");
            }
            return sb.ToString();
        }
    }
}
