using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// «Штурм» (attack / defend): one team attacks, the other defends, one point at a time. The attackers take the open
    /// point by standing in it (15 s alone, faster together; defenders inside stop the capture and push it back), then the
    /// next point opens deeper in the defenders' side — four in a row, each with its own 2 minutes (an attacker still on
    /// the point keeps the fight going after zero). The front moves with the points: attackers respawn behind it,
    /// defenders in front of the next point. Two halves with the sides swapped; more points taken wins, a tie goes to
    /// the team that took its points faster.
    /// </summary>
    public class AssaultMode : ObjectiveMode
    {
        public override GameMode Mode => GameMode.Assault;

        public const int Points = 4, Halves = 2;
        const float PointTime = 120f, CaptureTime = 10f, FreezeTime = 4f, SwapTime = 7f;
        static readonly string[] Letters = { "A", "B", "C", "D" };
        const byte EvHalf = 1, EvTaken = 2, EvSwap = 3;

        enum Phase : byte { Waiting, Freeze, Live, Swap, Done }

        Phase phase = Phase.Waiting;
        int half, attackers, active;
        float timer, progress, opened;
        bool contested;
        int nAtt, nDef;
        readonly int[] taken = new int[2];
        readonly float[] used = new float[2];
        readonly Vector3[] pts = new Vector3[Points];
        bool placed, frozeLocal;
        ZoneRing ring;
        readonly Dictionary<CombatBot, Vector2> botOff = new Dictionary<CombatBot, Vector2>();

        int Defenders => 1 - attackers;

        protected override void Setup()
        {
            float[] ts = { 0.3f, 0.5f, 0.68f, 0.85f };
            var used = new List<Vector3>();
            float gap = Mathf.Max(ZoneRadius * 2.6f, span * 0.15f);
            for (int i = 0; i < Points; i++)
            {
                pts[i] = PickSpot(ts[i], 0.09f, used, gap, 1.2f);
                used.Add(pts[i]);
            }
            placed = true;
        }

        // ------------------------------------------------------------------ what the match asks

        public override int TeamScore(int team) => team >= 0 && team < 2 ? taken[team] : 0;
        public override bool OwnsClock => true;
        public override float Clock => phase == Phase.Waiting || phase == Phase.Done ? 0f : Mathf.Max(0f, timer);
        public override bool Frozen => phase == Phase.Freeze;
        public override bool CanRespawn(Combatant c) => phase != Phase.Swap;

        public override string ScoreLine(int myTeam) =>
            $"{Title}  ·  половина {Mathf.Min(half + 1, Halves)}/{Halves}  ·  <color=#{Hex(Teams.Colors[0])}><b>{Teams.Names[0].ToUpperInvariant()} {taken[0]}</b></color> : " +
            $"<color=#{Hex(Teams.Colors[1])}><b>{taken[1]} {Teams.Names[1].ToUpperInvariant()}</b></color>  ·  взятых точек";

        public override string TaskLine(int myTeam)
        {
            switch (phase)
            {
                case Phase.Waiting: return "Ожидание игроков в обеих командах";
                case Phase.Freeze: return (myTeam == attackers ? "Вы атакуете" : "Вы защищаетесь") + "  ·  старт через " + Mathf.CeilToInt(timer);
                case Phase.Swap: return "Смена сторон";
                case Phase.Done: return "";
            }
            string l = Letters[Mathf.Clamp(active, 0, Points - 1)];
            string state = contested ? "  ·  <color=#FFD040>точка оспаривается</color>" : timer <= 0f ? "  ·  <color=#FF6040>дополнительное время</color>" : "";
            return (myTeam == attackers ? $"Атака: захватите точку {l}" : $"Защита: удержите точку {l}") + $"  ({active + 1}/{Points}){state}";
        }

        public override int DecideWinner()
        {
            if (taken[0] != taken[1]) return taken[0] > taken[1] ? 0 : 1;
            if (taken[0] == 0 || Mathf.Abs(used[0] - used[1]) < 0.5f) return -1;
            return used[0] < used[1] ? 0 : 1;
        }

        public override string EndReason(int winner) =>
            $"Взято точек: {Teams.Names[0]} {taken[0]} : {taken[1]} {Teams.Names[1]}" + (taken[0] == taken[1] && winner >= 0 ? "  ·  быстрее взяли " + Teams.Names[winner] : "");

        public override float SpawnBias(Combatant who, Vector3 sp)
        {
            if (!who || who.team < 0 || phase == Phase.Waiting) return 0f;
            float tA = SideT(pts[Mathf.Clamp(active, 0, Points - 1)]);
            float t = SideT(sp);
            // attackers close behind the front, defenders a run away from their point (the attack needs the time)
            if (who.team == attackers)
            {
                float want = Mathf.Max(0f, tA - 0.22f);
                return -Mathf.Abs(t - want) * span * 0.8f - (t > tA - 0.08f ? 60f : 0f);
            }
            float wantD = Mathf.Min(1f, tA + 0.3f);
            return -Mathf.Abs(t - wantD) * span * 0.8f - (t < tA + 0.1f ? 60f : 0f);
        }

        public override bool ForcedSpawn(Combatant who, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = default;
            // start of a half: each team on its own side
            if (!who || who.team < 0 || phase != Phase.Freeze) return false;
            return SpawnNear(who, who.team == attackers ? Bases[0] : Bases[1], out pos, out rot);
        }

        // ------------------------------------------------------------------ rules (host)

        protected override void HostTick(float dt)
        {
            if (!placed) return;
            switch (phase)
            {
                case Phase.Waiting:
                    if (Members(0) > 0 && Members(1) > 0) StartHalf(0);
                    break;
                case Phase.Freeze:
                    timer -= dt;
                    if (timer <= 0f) { phase = Phase.Live; timer = PointTime; opened = Time.time; }
                    break;
                case Phase.Live:
                {
                    timer -= dt;
                    var p = pts[active];
                    nAtt = CountIn(p, ZoneRadius, attackers);
                    nDef = CountIn(p, ZoneRadius, Defenders);
                    contested = nAtt > 0 && nDef > 0;
                    if (nAtt > 0 && nDef == 0) progress += (1f + 0.4f * (Mathf.Min(nAtt, 3) - 1)) / CaptureTime * dt;
                    else if (nAtt == 0 && nDef > 0) progress -= dt / 20f;
                    else if (nAtt == 0) progress -= dt / 45f;
                    progress = Mathf.Clamp01(progress);
                    if (progress >= 1f) Take();
                    else if (timer <= 0f && nAtt == 0) EndHalf();
                    break;
                }
                case Phase.Swap:
                    timer -= dt;
                    if (timer <= 0f) StartHalf(half + 1);
                    break;
            }
        }

        void StartHalf(int h)
        {
            half = h;
            attackers = h % 2 == 0 ? 0 : 1;
            active = 0;
            progress = 0f;
            contested = false;
            phase = Phase.Freeze;
            timer = FreezeTime;
            botOff.Clear();
            RespawnEverybody();
            Announce(EvHalf, attackers, h);
        }

        void Take()
        {
            taken[attackers]++;
            used[attackers] += Time.time - opened;
            int idx = active;
            progress = 0f;
            active++;
            botOff.Clear();
            Announce(EvTaken, attackers, idx);
            // the second half is decided as soon as the attackers pass the first half's count
            if (active >= Points || (half == Halves - 1 && taken[attackers] > taken[Defenders])) { EndHalf(); return; }
            timer = PointTime;
            opened = Time.time;
        }

        void EndHalf()
        {
            active = Mathf.Min(active, Points - 1);
            if (half + 1 >= Halves)
            {
                phase = Phase.Done;
                if (MatchManager.Instance) MatchManager.Instance.EndByMode();
                return;
            }
            phase = Phase.Swap;
            timer = SwapTime;
            Announce(EvSwap, attackers, taken[attackers]);
        }

        public override bool BotOrder(CombatBot bot, out BotOrder order)
        {
            order = default;
            var c = bot ? bot.combatant : null;
            if (!placed || !c || c.team < 0 || (phase != Phase.Live && phase != Phase.Freeze)) return false;
            Vector3 p = pts[Mathf.Clamp(active, 0, Points - 1)];
            if (!botOff.TryGetValue(bot, out var off))
            {
                // attackers: into the circle. Defenders: a quarter stand in it, a quarter hold the approach in front,
                // half watch it from its edge on their own side (x = NaN marks "relative to the point's sides", y = slot)
                float r = Random.value;
                off = c.team == attackers || r < 0.25f ? Random.insideUnitCircle * ZoneRadius * 0.55f : new Vector2(float.NaN, r < 0.5f ? 9f + Random.value : Random.Range(-1f, 1f));
                botOff[bot] = off;
            }
            if (float.IsNaN(off.x))
            {
                Vector3 toward = Bases[0] - p; toward.y = 0f;
                toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, toward);
                if (off.y >= 9f)
                {
                    order.dest = p + toward * (ZoneRadius + 7f) + side * (off.y - 9.5f) * 8f; // in front, toward the attackers
                    order.radius = 4f;
                }
                else
                {
                    float a = off.y * 100f * Mathf.Deg2Rad; // behind the circle, up to 100° either side
                    Vector3 dir = -toward * Mathf.Cos(a) + side * Mathf.Sin(a);
                    order.dest = p + dir * (ZoneRadius + 3f);
                    order.radius = 2.5f;
                }
            }
            else
            {
                order.dest = p + new Vector3(off.x, 0f, off.y);
                order.radius = ZoneRadius * 0.6f;
            }
            return true;
        }

        // ------------------------------------------------------------------ every machine

        protected override void OnEvent(byte kind, int team, int arg)
        {
            if (kind == EvHalf)
            {
                attackers = team;
                half = arg;
                active = 0;
                progress = 0f;
                phase = Phase.Freeze;
                timer = FreezeTime;
                if (!host) RespawnLocal();
            }
        }

        protected override void LocalTick(float dt)
        {
            if (!host && (phase == Phase.Live || phase == Phase.Freeze || phase == Phase.Swap)) timer -= dt;
            var ph = PlayerHealth.Local;
            bool frz = Frozen && ph && !ph.IsDead;
            if (frz != frozeLocal && ph)
            {
                frozeLocal = frz;
                if (!ph.IsDead) ph.motor.Locked = frz;
            }
            if (!placed) return;
            if (!ring) ring = ZoneRing.Create("AssaultPoint", transform);
            bool show = phase == Phase.Live || phase == Phase.Freeze;
            ring.SetVisible(show);
            if (!show) return;
            ring.Place(pts[Mathf.Clamp(active, 0, Points - 1)], ZoneRadius);
            float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 5f));
            ring.SetColor(RelColor(Defenders, MyTeam), contested || progress > 0f ? pulse : 1f);
        }

        public override void Icons(List<ObjectiveIcon> list, int myTeam)
        {
            if (!placed || (phase != Phase.Live && phase != Phase.Freeze)) return;
            int i = Mathf.Clamp(active, 0, Points - 1);
            list.Add(new ObjectiveIcon
            {
                pos = pts[i], label = Letters[i], owner = Defenders, progress = progress, progressTeam = attackers,
                pulse = contested || (progress > 0f && myTeam == Defenders), important = true, radius = ZoneRadius,
            });
        }

        protected override string EventText(byte kind, int team, int arg, int my, out string sub, out bool good, out Sfx? sound)
        {
            sub = null; good = true; sound = Sfx.Switch;
            switch (kind)
            {
                case EvHalf:
                    sub = my == team ? $"Вы атакуете: берите точки по очереди, {Points} подряд" : "Вы защищаетесь: не отдавайте точки";
                    return arg == 0 ? "ШТУРМ" : "ВТОРАЯ ПОЛОВИНА";
                case EvTaken:
                {
                    string l = Letters[Mathf.Clamp(arg, 0, Points - 1)];
                    bool last = arg + 1 >= Points;
                    good = my == team;
                    sound = good ? Sfx.AbilityReady : Sfx.ErrorTone;
                    if (good) { sub = last ? "Все точки взяты!" : $"Следующая — точка {Letters[arg + 1]}"; return $"ТОЧКА {l} ВЗЯТА"; }
                    sub = last ? "Оборона прорвана" : $"Отходите к точке {Letters[arg + 1]}";
                    return $"ТОЧКА {l} ПОТЕРЯНА";
                }
                case EvSwap:
                    sub = $"{Teams.Names[team]} взяли точек: {arg}. Теперь атакуют {Teams.Names[1 - team]}";
                    return "СМЕНА СТОРОН";
            }
            return null;
        }

        // ------------------------------------------------------------------ network

        public override void WriteState(BinaryWriter w)
        {
            WriteBases(w);
            w.Write(placed);
            for (int i = 0; i < Points; i++) WV(w, pts[i]);
            w.Write((byte)phase); w.Write((byte)half); w.Write((sbyte)attackers); w.Write((byte)active);
            w.Write(timer); w.Write(progress); w.Write(contested);
            w.Write((byte)taken[0]); w.Write((byte)taken[1]); w.Write(used[0]); w.Write(used[1]);
        }

        public override void ReadState(BinaryReader r)
        {
            ReadBases(r);
            placed = r.ReadBoolean();
            for (int i = 0; i < Points; i++) pts[i] = RV(r);
            phase = (Phase)r.ReadByte(); half = r.ReadByte(); attackers = r.ReadSByte(); active = r.ReadByte();
            timer = r.ReadSingle(); progress = r.ReadSingle(); contested = r.ReadBoolean();
            taken[0] = r.ReadByte(); taken[1] = r.ReadByte(); used[0] = r.ReadSingle(); used[1] = r.ReadSingle();
        }
    }
}
