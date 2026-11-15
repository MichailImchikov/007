using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// «Захват точек» (Domination): three points A, B, C — A on the blue side, B in the middle, C on the red side.
    /// Standing in a point's circle captures it (8 s alone, faster with up to three; both teams inside = contested, the
    /// clock stops); taking an enemy point needs no neutral step, but a half-taken point first has to be wiped clean by
    /// its owner or the other team. Every held point gives the team one point per 4 s; the first to 150 (or the leader
    /// when the match time runs out) wins. Endless respawns, closer to the team's own points.
    /// </summary>
    public class DominationMode : ObjectiveMode
    {
        public override GameMode Mode => GameMode.Domination;

        public const int Limit = 150;
        const float CaptureTime = 8f, TickSeconds = 4f;
        const byte EvCaptured = 1;
        public static readonly string[] Letters = { "A", "B", "C" };

        class Point
        {
            public Vector3 pos;
            public int owner = -1, capTeam = -1;
            public float progress;
            public bool contested;
            public ZoneRing ring;
        }

        readonly Point[] points = { new Point(), new Point(), new Point() };
        readonly float[] score = new float[2];
        bool placed;

        // bots: which point each one is after
        class Goal { public int idx; public float until; public Vector2 off; }
        readonly Dictionary<CombatBot, Goal> goals = new Dictionary<CombatBot, Goal>();

        protected override void Setup()
        {
            var used = new List<Vector3>();
            float gap = Mathf.Max(ZoneRadius * 3f, span * 0.22f);
            float[] ts = { 0.2f, 0.5f, 0.8f };
            for (int i = 0; i < 3; i++)
            {
                points[i].pos = PickSpot(ts[i], 0.12f, used, gap, 1.5f);
                used.Add(points[i].pos);
            }
            placed = true;
        }

        public override int TeamScore(int team) => team >= 0 && team < 2 ? Mathf.FloorToInt(score[team]) : 0;

        public override string ScoreLine(int myTeam) =>
            $"{Title}  ·  <color=#{Hex(Teams.Colors[0])}><b>{Teams.Names[0].ToUpperInvariant()} {TeamScore(0)}</b></color> : <color=#{Hex(Teams.Colors[1])}><b>{TeamScore(1)} {Teams.Names[1].ToUpperInvariant()}</b></color>  ·  до {Limit}";

        public override string TaskLine(int myTeam)
        {
            int mine = 0;
            foreach (var p in points) if (p.owner == myTeam) mine++;
            return mine == 3 ? "Все точки ваши — удерживайте" : mine == 0 ? "Захватите точки A, B, C" : $"Ваших точек: {mine} из 3";
        }

        public override string EndReason(int winner) => $"{Teams.Names[0]} {TeamScore(0)} : {TeamScore(1)} {Teams.Names[1]}";

        // ------------------------------------------------------------------ rules (host)

        protected override void HostTick(float dt)
        {
            if (!placed) return;
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                int n0 = CountIn(p.pos, ZoneRadius, 0), n1 = CountIn(p.pos, ZoneRadius, 1);
                p.contested = n0 > 0 && n1 > 0;
                if (p.contested) continue;
                int t = n0 > 0 ? 0 : n1 > 0 ? 1 : -1;
                if (t < 0)
                {
                    // nobody there: a half-done capture slowly fades
                    p.progress = Mathf.MoveTowards(p.progress, 0f, dt / 20f);
                    if (p.progress <= 0f) p.capTeam = -1;
                    continue;
                }
                int n = Mathf.Min(t == 0 ? n0 : n1, 3);
                float rate = (1f + 0.4f * (n - 1)) / CaptureTime;
                if (p.owner == t || (p.capTeam >= 0 && p.capTeam != t && p.progress > 0f))
                {
                    // the owner (or the other side) wipes the enemy's half-done capture
                    p.progress = Mathf.MoveTowards(p.progress, 0f, rate * dt);
                    if (p.progress <= 0f) p.capTeam = -1;
                    continue;
                }
                p.capTeam = t;
                p.progress += rate * dt;
                if (p.progress >= 1f)
                {
                    int prev = p.owner;
                    p.owner = t;
                    p.progress = 0f;
                    p.capTeam = -1;
                    Announce(EvCaptured, t, i | ((prev + 1) << 4));
                }
            }
            foreach (var p in points) if (p.owner >= 0) score[p.owner] += dt / TickSeconds;
            if ((score[0] >= Limit || score[1] >= Limit) && MatchManager.Instance) MatchManager.Instance.EndByMode();
        }

        public override float SpawnBias(Combatant who, Vector3 sp)
        {
            if (!who || who.team < 0) return 0f;
            float t = SideT(sp);
            float home = who.team == 0 ? 1f - t : t;
            float bias = (home - 0.5f) * span * 0.5f;
            foreach (var p in points)
            {
                Vector3 d = sp - p.pos; d.y = 0f;
                float dist = d.magnitude;
                if (p.owner == who.team && dist < 26f) bias += 7f;
                bool hot = p.contested || (p.capTeam >= 0 && p.capTeam != who.team) || (p.owner >= 0 && p.owner != who.team);
                if (hot && dist < ZoneRadius + 7f) bias -= 45f;
            }
            return bias;
        }

        public override bool BotOrder(CombatBot bot, out BotOrder order)
        {
            order = default;
            if (!placed || !bot || !bot.combatant || bot.combatant.team < 0) return false;
            int team = bot.combatant.team;
            if (!goals.TryGetValue(bot, out var g) || Time.time > g.until || (points[g.idx].owner == team && points[g.idx].progress <= 0f && Random.value < 0.02f))
            {
                g = new Goal { idx = PickPoint(bot, team), until = Time.time + Random.Range(8f, 16f), off = Random.insideUnitCircle * ZoneRadius * 0.5f };
                goals[bot] = g;
            }
            var p = points[g.idx];
            order.dest = p.pos + new Vector3(g.off.x, 0f, g.off.y);
            order.radius = ZoneRadius * 0.65f;
            return true;
        }

        int PickPoint(CombatBot bot, int team)
        {
            // weighted: points to take (nearer = likelier), points of ours under attack; ours otherwise rarely
            float total = 0f;
            var w = new float[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                Vector3 d = p.pos - bot.transform.position; d.y = 0f;
                float near = 1f / (d.magnitude + 12f);
                float k = p.owner != team ? 3f : (p.capTeam >= 0 && p.capTeam != team) || p.contested ? 4f : 0.35f;
                w[i] = near * k;
                total += w[i];
            }
            float r = Random.value * total;
            for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0f) return i; }
            return 1;
        }

        // ------------------------------------------------------------------ view

        protected override void LocalTick(float dt)
        {
            if (!placed) return;
            int my = MyTeam;
            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 5f));
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                if (!p.ring) p.ring = ZoneRing.Create("Point_" + Letters[i], transform);
                p.ring.Place(p.pos, ZoneRadius);
                bool hot = p.contested || p.progress > 0f;
                p.ring.SetColor(RelColor(p.owner, my), hot ? pulse : 1f);
            }
        }

        public override void Icons(List<ObjectiveIcon> list, int myTeam)
        {
            if (!placed) return;
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                list.Add(new ObjectiveIcon
                {
                    pos = p.pos, label = Letters[i], owner = p.owner, progress = p.progress, progressTeam = p.capTeam,
                    pulse = p.contested || (p.progress > 0f && p.capTeam != myTeam), important = true, radius = ZoneRadius,
                });
            }
        }

        protected override string EventText(byte kind, int team, int arg, int my, out string sub, out bool good, out Sfx? sound)
        {
            sub = null; good = team == my; sound = Sfx.AbilityReady;
            if (kind != EvCaptured) return null;
            int idx = Mathf.Clamp(arg & 15, 0, 2), prev = (arg >> 4) - 1;
            string l = Letters[idx];
            if (team == my) { sub = "+1 очко каждые 4 секунды"; return $"ТОЧКА {l} ЗАХВАЧЕНА"; }
            sound = Sfx.ErrorTone;
            return prev == my ? $"ТОЧКА {l} ПОТЕРЯНА" : $"ПРОТИВНИК ЗАНЯЛ ТОЧКУ {l}";
        }

        // ------------------------------------------------------------------ network

        public override void WriteState(BinaryWriter w)
        {
            WriteBases(w);
            w.Write(placed);
            foreach (var p in points)
            {
                WV(w, p.pos); w.Write((sbyte)p.owner); w.Write((sbyte)p.capTeam); w.Write(p.progress); w.Write(p.contested);
            }
            w.Write(score[0]); w.Write(score[1]);
        }

        public override void ReadState(BinaryReader r)
        {
            ReadBases(r);
            placed = r.ReadBoolean();
            foreach (var p in points)
            {
                p.pos = RV(r); p.owner = r.ReadSByte(); p.capTeam = r.ReadSByte(); p.progress = r.ReadSingle(); p.contested = r.ReadBoolean();
            }
            score[0] = r.ReadSingle(); score[1] = r.ReadSingle();
        }
    }
}
