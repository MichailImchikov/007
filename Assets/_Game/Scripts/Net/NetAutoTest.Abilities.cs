using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// "-netabil": network check of the newer abilities between two human players (run with -nettest on both).
    /// The CLIENT casts them one by one at the host's player (Sidearm, Lag, EmptyMag, fart, hyper speed, clones,
    /// laser eyes, rewind, tic-tac-toe, a knife clash); BOTH machines answer on their own — play tic-tac-toe,
    /// mash F in a knife duel, swing a knife when an enemy is close — and log "[NetTest] abil ..." lines.
    /// </summary>
    public partial class NetAutoTest
    {
        static bool abil;
        int abilStep = -1;
        float abilT, respondT, swingT, abilLogT, knifeT = -1f;
        /// <summary>-netabilduel: skip straight to the knife clash (the last step).</summary>
        static bool abilDuelOnly;

        static readonly AbilityType[] AbilPlan =
        {
            AbilityType.Sidearm, AbilityType.Lag, AbilityType.EmptyMag, AbilityType.FartBlind, AbilityType.Hyperspeed,
            AbilityType.ShadowClones, AbilityType.LaserEyes, AbilityType.Rewind, AbilityType.TicTacToe, AbilityType.None, // None = knife clash
        };

        static void ParseAbil(string[] args)
        {
            foreach (var a in args)
            {
                if (a == "-netabil") abil = true;
                if (a == "-netabilduel") { abil = true; abilDuelOnly = true; }
            }
        }

        /// <summary>True when the ability pilot took over this frame (the regular Act cycle is skipped).</summary>
        bool AbilityPilot(PlayerHealth ph, float dt)
        {
            if (!abil) return false;
            var me = ph.GetComponent<Combatant>();
            Respond(ph, me, dt);

            abilLogT -= dt;
            if (abilLogT <= 0f) { abilLogT = 1f; Log("abil " + AbilState(me)); }

            if (Net.IsHost || ph.IsDead) return true;   // the client drives the plan
            var host = NearestRemote(me.transform.position, true);
            if (!host || !host.human) return true;
            if (knifeT >= 0f)
            {
                // the host has seen us in front of it by now and keeps its blade swinging: strike
                knifeT -= dt;
                if (knifeT < 0f)
                {
                    Vector3 hf = host.transform.forward; hf.y = 0f; hf.Normalize();
                    var d = host.GetComponent<IDamageable>();
                    var r = d.TakeDamage(new DamageInfo { amount = 55f, point = host.Chest, direction = -hf, attacker = me, weapon = "Нож" });
                    KnifeDuel.NoteSwing(me);
                    Log($"abil knife hit on {host.displayName} applied={r.applied} (a clash starts on the host's machine)");
                }
                return true;
            }
            if (abilDuelOnly && abilStep < AbilPlan.Length - 2) abilStep = AbilPlan.Length - 2;
            abilT -= dt;
            if (abilT > 0f) return true;
            if (TicTacToe.InGame(me) || KnifeDuel.InDuel(me) || me.abilities.Lasering || TimeRewind.Playing) { abilT = 0.5f; return true; }
            abilStep++;
            abilT = 9f;
            if (abilStep >= AbilPlan.Length) { if (abilStep == AbilPlan.Length) Log("abil plan done"); return true; }
            var t = AbilPlan[abilStep];

            // stand in front of the host's player, facing it
            Vector3 f = host.transform.forward; f.y = 0f; f.Normalize();
            float dist = t == AbilityType.None ? 1.3f : t == AbilityType.FartBlind ? 1.8f : 4f;
            ph.motor.Teleport(host.transform.position + f * dist + Vector3.up * 0.05f);
            ph.playerCam.SetLook(Quaternion.LookRotation(-f).eulerAngles.y, 0f);

            if (t == AbilityType.None)
            {
                // knife clash: stand in front of the host for a moment (its responder starts swinging), then strike
                knifeT = 1.5f;
                abilT = 14f;
                Log($"abil step {abilStep} knife clash: in front of {host.displayName}, striking in 1.5 s");
                return true;
            }
            me.abilities.Give(t, t);
            bool ok = me.abilities.TryActivate();
            Log($"abil step {abilStep} cast {t} -> {ok}");
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
                    for (int i = 0; i < 9; i++) if (g.board[(i * 5 + 4) % 9] == 0) { int c = (i * 5 + 4) % 9; g.TryHumanMove(me, c); Log($"abil ttt move {c} (mirror={g != null && !g.X.c.Equals(me)})"); break; }
                }
            }
            else respondT = 0f;

            // knife duel: mash F (the host a little faster, so the duel has a clear winner)
            var duel = KnifeDuel.Of(me);
            if (duel != null && duel.Phase == 1)
            {
                swingT -= dt;
                if (swingT <= 0f) { swingT = Net.IsHost ? 0.1f : 0.16f; KnifeDuel.TestPress(me); }
            }
            else if (!ph.IsDead)
            {
                // an enemy human right in front of us: keep the knife swinging (the client's hit then is a clash)
                var near = NearestRemote(me.transform.position, true);
                if (near && near.human && Vector3.Distance(near.transform.position, me.transform.position) < 2.4f)
                {
                    swingT -= dt;
                    if (swingT <= 0f) { swingT = 0.25f; KnifeDuel.NoteSwing(me); }
                }
            }

            // laser eyes: keep the beams on the nearest remote human
            if (me.abilities && me.abilities.Lasering)
            {
                var tgt = NearestRemote(me.transform.position, true);
                if (tgt)
                {
                    Vector3 to = tgt.Chest - ph.playerCam.cam.transform.position;
                    ph.playerCam.SetLook(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, -Mathf.Asin(Mathf.Clamp(to.normalized.y, -1f, 1f)) * Mathf.Rad2Deg);
                }
            }
        }

        static string AbilState(Combatant me)
        {
            var ph = PlayerHealth.Local;
            var u = me.abilities;
            var lo = ph ? ph.loadout : null;
            string s = $"me[hp={(ph ? ph.Health : 0f):0} dead={(ph && ph.IsDead)} gun={(lo && lo.Active ? lo.Active.weaponName : "-")} mag={(lo && lo.Active ? lo.Active.Ammo : -1)}" +
                       $" forced={(lo ? lo.ForcedSecondary : 0f):0.0} lag={(u && u.Lagging)} fps={Application.targetFrameRate} hyper={(u && u.Hyper)} laser={(u && u.Lasering)}" +
                       $" ttt={TicTacToe.InGame(me)} duel={KnifeDuel.InDuel(me)} rewind={TimeRewind.Playing} y={me.transform.position.y:0.0}]";
            s += $" clones={ShadowClone.All.Count} smoke={SmokeCloud.All.Count}";
            var g = TicTacToe.Of(me);
            if (g != null) s += $" tttBoard={string.Join("", Array.ConvertAll(g.board, v => v == 0 ? "." : v == 1 ? "X" : "O"))} turn={g.Turn} res={g.Result}";
            var d = KnifeDuel.Of(me);
            if (d != null) s += $" duelPhase={d.Phase} prog={d.Progress:0.00} mirror={d.IsMirror}";
            foreach (var c in Combatant.All)
            {
                if (!c || !c.remote || !c.human) continue;
                var px = c.GetComponent<NetProxy>();
                if (!px) continue;
                var st = px.Latest;
                s += $" P{c.netId}[alive={c.alive} hp={st.health * 100f:0} laserFlag={st.Has(CharState.Laser)} hyperFlag={st.Has(CharState.Hyper)} prot={st.Has(CharState.Protected)} y={c.transform.position.y:0.0}]";
            }
            return s;
        }
    }
}
