using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>
    /// «Закладка бомбы» (search and destroy): rounds with one life each. Attackers start on side A with the bomb (a random
    /// attacker carries it, it drops where the carrier falls and any attacker picks it up by walking over it) and must
    /// plant it on site A or B (hold the use key 3 s inside the circle); defenders start on side B and win by running the
    /// clock out, by killing every attacker before the plant, or by defusing (6 s next to the bomb). The bomb goes off
    /// 40 s after the plant. A round also ends when one side is wiped out. First to 4 rounds; the sides swap after 3.
    /// The dead wait for the next round (a medic has the usual few seconds to bring a teammate back).
    /// </summary>
    public class BombMode : ObjectiveMode
    {
        public override GameMode Mode => GameMode.Bomb;

        public const int WinRounds = 4, RoundsPerHalf = 3;
        public const float FreezeTime = 5f, RoundTime = 105f, BombTime = 40f, EndTime = 5f, PlantTime = 3.2f, DefuseTime = 6f;
        const float PickupDist = 1.5f, DefuseDist = 1.8f, BlastRadius = 15f;
        public const string BombWeapon = "Бомба";
        static readonly string[] Letters = { "A", "B" };

        enum Phase : byte { Waiting, Freeze, Live, Planted, End }
        enum BombState : byte { None, Carried, Dropped, Planted, Defused, Exploded }
        const byte EvRound = 1, EvPlanted = 2, EvRoundEnd = 3, EvGotBomb = 4, EvDropped = 5;
        const byte ReasonElim = 0, ReasonTime = 1, ReasonBomb = 2, ReasonDefused = 3;
        public const byte ActPlant = 1, ActDefuse = 2;

        Phase phase = Phase.Waiting;
        float timer;
        int round;
        readonly int[] wins = new int[2];
        int attackers;
        BombState bomb;
        Combatant carrier;
        int carrierId;
        Vector3 bombPos, carrierLast;
        int plantedSite = -1;
        readonly Vector3[] sites = new Vector3[2];
        bool placed;
        int targetSite;

        // the local player's plant / defuse
        float channel = -1f;
        byte channelKind;
        bool frozeLocal, lockedLocal;
        string prompt;
        // bots planting / defusing (host)
        readonly Dictionary<CombatBot, float> botChannel = new Dictionary<CombatBot, float>();
        readonly Dictionary<Combatant, float> fellAt = new Dictionary<Combatant, float>();
        readonly Dictionary<CombatBot, int> botSite = new Dictionary<CombatBot, int>();
        MatchManager hooked;

        // the bomb in the world
        GameObject prop;
        Renderer led;
        Light glow;
        float beepT;
        readonly ZoneRing[] rings = new ZoneRing[2];

        int Defenders => 1 - attackers;
        public static int AttackersOf(int round) => ((Mathf.Max(1, round) - 1) / RoundsPerHalf) % 2 == 0 ? 0 : 1;

        // read-only view (network autotest, other systems)
        public int Attackers => attackers;
        public Combatant Carrier => carrier;
        public bool IsLive => phase == Phase.Live;
        public bool IsPlanted => phase == Phase.Planted && bomb == BombState.Planted;
        public Vector3 BombPosition => bombPos;
        public Vector3 SitePosition(int i) => sites[Mathf.Clamp(i, 0, 1)];

        protected override void Setup()
        {
            // both sites in the defenders' half, as far apart sideways as the map allows
            var none = new List<Vector3>();
            sites[0] = PickSpot(0.7f, 0.12f, none, 0f, 0f);
            var avoid = new List<Vector3> { sites[0] };
            float gap = Mathf.Max(ZoneRadius * 3.5f, span * 0.28f);
            sites[1] = PickSpot(0.7f, 0.14f, avoid, gap, 0f, 1.5f, sites[0]);
            Vector3 d = sites[1] - sites[0]; d.y = 0f;
            if (d.magnitude < gap * 0.8f)
            {
                // a narrow map: one site nearer the middle, one deep in the defenders' side
                sites[0] = PickSpot(0.55f, 0.1f, none, 0f, 0.5f);
                sites[1] = PickSpot(0.88f, 0.1f, new List<Vector3> { sites[0] }, ZoneRadius * 3f, 0.5f);
            }
            // site A is the one on the left of the attackers
            Vector3 fwd = Bases[1] - Bases[0]; fwd.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, fwd.normalized);
            if (Vector3.Dot(sites[0] - Bases[0], right) > Vector3.Dot(sites[1] - Bases[0], right)) (sites[0], sites[1]) = (sites[1], sites[0]);
            placed = true;
        }

        // ------------------------------------------------------------------ what the match asks

        public override int TeamScore(int team) => team >= 0 && team < 2 ? wins[team] : 0;
        public override bool OwnsClock => true;
        public override float Clock => phase == Phase.Waiting ? 0f : Mathf.Max(0f, timer);
        public override bool Frozen => phase == Phase.Freeze;
        public override bool CanRespawn(Combatant c) => phase == Phase.Waiting;
        public override bool CanRevive(Combatant target) =>
            target && fellAt.TryGetValue(target, out float t) && Time.time - t < Teams.ReviveWindow + 0.5f && (phase == Phase.Live || phase == Phase.Planted);
        public override string DeadLine(Combatant me) => phase == Phase.Waiting ? null : $"Вы выбыли до конца раунда  ·  живых: {Alive(me ? me.team : 0)}";
        public override string Prompt => prompt;
        public override float Channel => channel;
        public override string ChannelLabel => channelKind == ActPlant ? "ЗАКЛАДКА БОМБЫ" : "РАЗМИНИРОВАНИЕ";

        public override string ScoreLine(int myTeam)
        {
            string role = phase == Phase.Waiting ? "" : myTeam == attackers ? "  ·  вы атакуете" : "  ·  вы защищаете";
            return $"{Title}  ·  раунд {Mathf.Max(1, round)}  ·  <color=#{Hex(Teams.Colors[0])}><b>{Teams.Names[0].ToUpperInvariant()} {wins[0]}</b></color> : " +
                   $"<color=#{Hex(Teams.Colors[1])}><b>{wins[1]} {Teams.Names[1].ToUpperInvariant()}</b></color>  ·  до {WinRounds} побед{role}";
        }

        public override string TaskLine(int myTeam)
        {
            int me = myTeam, them = 1 - myTeam;
            string alive = $"   <color=#{Hex(Teams.Colors[0])}>живы {Alive(me)}</color> / <color=#{Hex(Teams.Colors[1])}>{Alive(them)}</color>";
            switch (phase)
            {
                case Phase.Waiting: return "Ожидание игроков в обеих командах";
                case Phase.Freeze: return (myTeam == attackers ? "Атака: несите бомбу на A или B" : "Защита: точки A и B") + "  ·  старт через " + Mathf.CeilToInt(timer);
                case Phase.Live:
                    if (myTeam == attackers)
                        return (carrier && carrier == Me ? "<color=#FFB040><b>У вас бомба</b></color> — заложите её на A или B"
                                : bomb == BombState.Dropped ? "<color=#FFB040>Бомба на земле — подберите её</color>" : "Атака: заложите бомбу на A или B") + alive;
                    return "Защита: не дайте заложить бомбу" + alive;
                case Phase.Planted:
                    string s = plantedSite >= 0 ? Letters[plantedSite] : "?";
                    return (myTeam == attackers ? $"Бомба на {s} — не подпускайте сапёров" : $"<color=#FF6040><b>Обезвредьте бомбу на {s}!</b></color>") + alive;
                default: return "";
            }
        }

        public override int DecideWinner() => wins[0] > wins[1] ? 0 : wins[1] > wins[0] ? 1 : -1;
        public override string EndReason(int winner) => $"Раунды: {Teams.Names[0]} {wins[0]} : {wins[1]} {Teams.Names[1]}";

        public override bool ForcedSpawn(Combatant who, out Vector3 pos, out Quaternion rot)
        {
            pos = default; rot = default;
            if (!who || who.team < 0) return false;
            int att = phase == Phase.Waiting ? AttackersOf(1) : attackers;
            return SpawnNear(who, who.team == att ? Bases[0] : Bases[1], out pos, out rot);
        }

        // ------------------------------------------------------------------ rules (host)

        void Hook()
        {
            var mm = MatchManager.Instance;
            if (!mm || mm == hooked) return;
            hooked = mm;
            mm.Killed += e => { if (e.victim) fellAt[e.victim] = Time.time; };
        }

        protected override void HostTick(float dt)
        {
            Hook();
            if (!placed) return;
            switch (phase)
            {
                case Phase.Waiting:
                    if (Members(0) > 0 && Members(1) > 0) StartRound(1);
                    break;
                case Phase.Freeze:
                    timer -= dt;
                    if (timer <= 0f) { phase = Phase.Live; timer = RoundTime; GiveBomb(); }
                    break;
                case Phase.Live:
                    timer -= dt;
                    TrackBomb();
                    if (Alive(Defenders) == 0) EndRound(attackers, ReasonElim);
                    else if (Alive(attackers) == 0) EndRound(Defenders, ReasonElim);
                    else if (timer <= 0f) EndRound(Defenders, ReasonTime);
                    break;
                case Phase.Planted:
                    timer -= dt;
                    if (timer <= 0f) { Explode(); EndRound(attackers, ReasonBomb); }
                    else if (Alive(Defenders) == 0) EndRound(attackers, ReasonElim);
                    break;
                case Phase.End:
                    timer -= dt;
                    if (timer <= 0f)
                    {
                        if (wins[0] >= WinRounds || wins[1] >= WinRounds) { if (MatchManager.Instance) MatchManager.Instance.EndByMode(); }
                        else StartRound(round + 1);
                    }
                    break;
            }
        }

        void StartRound(int r)
        {
            round = r;
            attackers = AttackersOf(r);
            phase = Phase.Freeze;
            timer = FreezeTime;
            bomb = BombState.None;
            carrier = null; carrierId = 0;
            plantedSite = -1;
            targetSite = Random.Range(0, 2);
            botChannel.Clear(); botSite.Clear(); fellAt.Clear();
            RespawnEverybody();
            Announce(EvRound, attackers, r);
        }

        void GiveBomb()
        {
            var list = new List<Combatant>();
            foreach (var c in Combatant.All) if (c && c.alive && c.team == attackers) list.Add(c);
            if (list.Count == 0) return;
            SetCarrier(list[Random.Range(0, list.Count)]);
        }

        void SetCarrier(Combatant c)
        {
            carrier = c;
            carrierId = c ? c.netId : 0;
            bomb = c ? BombState.Carried : BombState.None;
            if (c) { carrierLast = c.transform.position; Announce(EvGotBomb, attackers, c.netId); }
        }

        void TrackBomb()
        {
            if (bomb == BombState.Carried)
            {
                if (carrier && carrier.alive) { carrierLast = carrier.transform.position; return; }
                // the carrier fell (or left): the bomb lies where it fell
                bomb = BombState.Dropped;
                bombPos = Ground(carrierLast);
                carrier = null; carrierId = 0;
                Announce(EvDropped, attackers, 0);
            }
            else if (bomb == BombState.Dropped)
            {
                foreach (var c in Combatant.All)
                {
                    if (!c || !c.alive || c.team != attackers) continue;
                    Vector3 d = c.transform.position - bombPos;
                    if (Mathf.Abs(d.y) > 1.8f) continue;
                    d.y = 0f;
                    if (d.magnitude <= PickupDist) { SetCarrier(c); break; }
                }
            }
        }

        static Vector3 Ground(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 0.5f, Vector3.down, out var hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return hit.point;
            return NavMesh.SamplePosition(p, out var nh, 2f, NavMesh.AllAreas) ? nh.position : p;
        }

        int SiteAt(Vector3 p)
        {
            for (int i = 0; i < 2; i++) if (Inside(p, sites[i], ZoneRadius)) return i;
            return -1;
        }

        void HostPlant(Combatant who, int site, Vector3 pos)
        {
            if (phase != Phase.Live || bomb != BombState.Carried || !who || who != carrier || !who.alive) return;
            if (site < 0 || site > 1 || !Inside(pos, sites[site], ZoneRadius + 1.2f)) return;
            bomb = BombState.Planted;
            bombPos = Ground(pos);
            plantedSite = site;
            carrier = null; carrierId = 0;
            phase = Phase.Planted;
            timer = BombTime;
            botChannel.Clear();
            Announce(EvPlanted, attackers, site);
        }

        void HostDefuse(Combatant who)
        {
            if (phase != Phase.Planted || !who || !who.alive || who.team == attackers) return;
            Vector3 d = who.transform.position - bombPos; d.y = 0f;
            if (d.magnitude > DefuseDist + 1f) return;
            bomb = BombState.Defused;
            EndRound(Defenders, ReasonDefused);
        }

        void Explode()
        {
            bomb = BombState.Exploded;
            Explosions.Detonate(bombPos + Vector3.up * 0.4f, BlastRadius, 420f, 14f, null, BombWeapon);
        }

        void EndRound(int winner, byte reason)
        {
            if (phase == Phase.End) return;
            wins[winner]++;
            phase = Phase.End;
            timer = EndTime;
            botChannel.Clear();
            Announce(EvRoundEnd, winner, reason | (wins[0] << 4) | (wins[1] << 8));
        }

        public override void OnClientAction(Combatant who, byte action, byte arg, Vector3 pos)
        {
            if (action == ActPlant) HostPlant(who, arg, pos);
            else if (action == ActDefuse) HostDefuse(who);
        }

        // ------------------------------------------------------------------ bots (host)

        public override bool BotOrder(CombatBot bot, out BotOrder order)
        {
            order = default;
            var c = bot ? bot.combatant : null;
            if (!placed || !c || c.team < 0 || phase == Phase.Waiting || phase == Phase.End) return false;
            if (c.team == attackers)
            {
                switch (bomb)
                {
                    case BombState.Carried when carrier == c:
                        order.dest = sites[targetSite];
                        order.radius = ZoneRadius * 0.45f;
                        order.hold = phase == Phase.Live;
                        return true;
                    case BombState.Dropped:
                        order.dest = bombPos;
                        order.radius = 0.6f;
                        return true;
                    case BombState.Carried when carrier:
                    {
                        // half escort the carrier, half go ahead to the site
                        if (!botSite.TryGetValue(bot, out int role)) { role = botSite.Count % 2; botSite[bot] = role; }
                        order.dest = role == 0 ? carrier.transform.position : sites[targetSite];
                        order.radius = role == 0 ? 5f : ZoneRadius;
                        return true;
                    }
                    case BombState.Planted:
                        order.dest = bombPos;
                        order.radius = ZoneRadius + 3f;
                        return true;
                }
                order.dest = sites[targetSite];
                order.radius = ZoneRadius;
                return true;
            }
            // defenders
            if (bomb == BombState.Planted)
            {
                order.dest = bombPos;
                order.radius = 1.2f;
                order.hold = true;
                order.urgent = true;
                return true;
            }
            if (!botSite.TryGetValue(bot, out int s)) { s = botSite.Count % 2; botSite[bot] = s; }
            order.dest = sites[s];
            order.radius = ZoneRadius * 0.9f;
            return true;
        }

        public override void BotHold(CombatBot bot, float dt)
        {
            var c = bot.combatant;
            botChannel.TryGetValue(bot, out float t);
            if (phase == Phase.Live && bomb == BombState.Carried && carrier == c)
            {
                int site = SiteAt(c.transform.position);
                if (site < 0) { botChannel.Remove(bot); return; }
                t += dt / PlantTime;
                if (t >= 1f) { HostPlant(c, site, c.transform.position); botChannel.Remove(bot); return; }
            }
            else if (phase == Phase.Planted && c.team != attackers)
            {
                t += dt / DefuseTime;
                if (t >= 1f) { HostDefuse(c); botChannel.Remove(bot); return; }
            }
            else { botChannel.Remove(bot); return; }
            botChannel[bot] = t;
        }

        public override void BotRelease(CombatBot bot) => botChannel.Remove(bot);

        // ------------------------------------------------------------------ every machine

        protected override void OnEvent(byte kind, int team, int arg)
        {
            if (kind == EvRound)
            {
                attackers = team;
                round = arg;
                phase = Phase.Freeze;
                timer = FreezeTime;
                bomb = BombState.None;
                plantedSite = -1;
                fellAt.Clear();
                if (!host) RespawnLocal();
            }
        }

        protected override void LocalTick(float dt)
        {
            if (!host && phase != Phase.Waiting) timer -= dt;
            if (!host) { carrier = carrierId != 0 ? NetGame.Find(carrierId) : null; Hook(); }
            var ph = PlayerHealth.Local;
            // start of a round: the local player stands still
            bool frz = Frozen && ph && !ph.IsDead;
            if (frz != frozeLocal && ph)
            {
                frozeLocal = frz;
                if (!ph.IsDead) ph.motor.Locked = frz;
            }
            LocalChannel(dt);
            View(dt);
        }

        void LocalChannel(float dt)
        {
            var me = Me;
            var ph = PlayerHealth.Local;
            byte want = 0;
            int site = -1;
            prompt = null;
            if (me && me.alive && ph && !ph.IsDead && placed)
            {
                if (phase == Phase.Live && bomb == BombState.Carried && carrier == me)
                {
                    site = SiteAt(me.transform.position);
                    if (site >= 0) want = ActPlant;
                }
                else if (phase == Phase.Planted && me.team != attackers)
                {
                    Vector3 d = me.transform.position - bombPos;
                    if (Mathf.Abs(d.y) < 1.8f) { d.y = 0f; if (d.magnitude < DefuseDist) want = ActDefuse; }
                }
            }
            string key = GameSettings.KeyName(BindAction.Pickup);
            if (want == ActPlant) prompt = $"Удерживайте [{key}] — заложить бомбу на {Letters[site]}";
            else if (want == ActDefuse) prompt = $"Удерживайте [{key}] — обезвредить бомбу";

            if (want != 0 && GameInput.InteractHeld)
            {
                if (channel < 0f || channelKind != want)
                {
                    channel = 0f;
                    channelKind = want;
                    ProcAudio.Play(want == ActPlant ? Sfx.GrenadePin : Sfx.Click, 0.8f);
                }
                Lock(ph, true);
                channel += dt / (want == ActPlant ? PlantTime : DefuseTime);
                if (channel >= 1f)
                {
                    if (host)
                    {
                        if (want == ActPlant) HostPlant(me, site, me.transform.position);
                        else HostDefuse(me);
                    }
                    else NetGame.SendObjAction(want, (byte)Mathf.Max(0, site), me.transform.position);
                    channel = -1f;
                    Lock(ph, false);
                }
            }
            else if (channel >= 0f)
            {
                channel = -1f;
                Lock(ph, false);
            }
        }

        void Lock(PlayerHealth ph, bool on)
        {
            if (!ph || lockedLocal == on) return;
            lockedLocal = on;
            if (!ph.IsDead && !frozeLocal) ph.motor.Locked = on;
            if (ph.loadout && ph.loadout.Active && !ph.IsDead) ph.loadout.Active.ExternalLower = on ? 0.85f : 0f;
        }

        void View(float dt)
        {
            if (!placed) return;
            int my = MyTeam;
            for (int i = 0; i < 2; i++)
            {
                if (!rings[i]) rings[i] = ZoneRing.Create("Site_" + Letters[i], transform);
                rings[i].Place(sites[i], ZoneRadius);
                bool hot = plantedSite == i && phase == Phase.Planted;
                float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * 6f));
                rings[i].SetColor(RelColor(phase == Phase.Waiting ? -1 : Defenders, my), hot ? pulse : 1f);
            }
            // the bomb on the ground
            bool show = bomb == BombState.Dropped || bomb == BombState.Planted || bomb == BombState.Defused;
            if (show && !prop) BuildProp();
            if (prop)
            {
                if (prop.activeSelf != show) prop.SetActive(show);
                if (show)
                {
                    prop.transform.position = bombPos + Vector3.up * 0.07f;
                    bool ticking = bomb == BombState.Planted && phase == Phase.Planted;
                    float left = Mathf.Max(0f, timer);
                    float interval = ticking ? Mathf.Lerp(0.12f, 1f, left / BombTime) : 1.4f;
                    beepT -= dt;
                    if (beepT <= 0f)
                    {
                        beepT = interval;
                        if (ticking) ProcAudio.PlayAt(Sfx.Click, bombPos, 0.9f);
                    }
                    bool on = beepT > interval * 0.55f || (ticking && left < 5f);
                    if (led) led.enabled = bomb != BombState.Defused && on;
                    if (glow) glow.intensity = ticking && on ? 2.2f : 0f;
                }
            }
        }

        void BuildProp()
        {
            prop = new GameObject("Bomb");
            prop.transform.SetParent(transform, false);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            Material M(Color c) { var m = new Material(lit); m.SetColor("_BaseColor", c); m.color = c; return m; }
            void Part(PrimitiveType t, Vector3 pos, Vector3 size, Material m, out Renderer r)
            {
                var g = GameObject.CreatePrimitive(t);
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(prop.transform, false);
                g.transform.localPosition = pos;
                g.transform.localScale = size;
                r = g.GetComponent<Renderer>();
                r.sharedMaterial = m;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            Part(PrimitiveType.Cube, new Vector3(0f, 0.07f, 0f), new Vector3(0.38f, 0.14f, 0.26f), M(new Color(0.24f, 0.27f, 0.17f)), out _);
            Part(PrimitiveType.Cube, new Vector3(0.04f, 0.145f, 0f), new Vector3(0.2f, 0.02f, 0.14f), M(new Color(0.08f, 0.08f, 0.09f)), out _);
            Part(PrimitiveType.Cylinder, new Vector3(-0.13f, 0.07f, 0f), new Vector3(0.08f, 0.15f, 0.08f), M(new Color(0.5f, 0.12f, 0.08f)), out _);
            var unlit = Shader.Find("Sprites/Default");
            var ledMat = new Material(unlit ? unlit : lit) { color = new Color(1f, 0.12f, 0.05f) };
            Part(PrimitiveType.Sphere, new Vector3(0.14f, 0.16f, 0.07f), Vector3.one * 0.035f, ledMat, out led);
            var lg = new GameObject("Glow");
            lg.transform.SetParent(prop.transform, false);
            lg.transform.localPosition = new Vector3(0.14f, 0.3f, 0.07f);
            glow = lg.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.15f, 0.08f);
            glow.range = 3.5f;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
        }

        public override void Icons(List<ObjectiveIcon> list, int myTeam)
        {
            if (!placed) return;
            for (int i = 0; i < 2; i++)
            {
                bool hot = plantedSite == i && phase == Phase.Planted;
                list.Add(new ObjectiveIcon
                {
                    pos = sites[i], label = Letters[i], owner = phase == Phase.Waiting ? -1 : Defenders, radius = ZoneRadius,
                    important = phase != Phase.End && (bomb != BombState.Planted || hot), pulse = hot,
                });
            }
            Color orange = new Color(1f, 0.6f, 0.15f);
            if (bomb == BombState.Planted && phase == Phase.Planted)
                list.Add(new ObjectiveIcon { pos = bombPos, label = "Б", owner = attackers, tint = orange, pulse = true, important = true,
                                             progress = channel >= 0f && channelKind == ActDefuse ? channel : 0f, progressTeam = Defenders });
            else if (myTeam == attackers && bomb == BombState.Dropped)
                list.Add(new ObjectiveIcon { pos = bombPos, label = "Б", owner = attackers, tint = orange, pulse = true, important = true });
            else if (myTeam == attackers && bomb == BombState.Carried && carrier && carrier != Me)
                list.Add(new ObjectiveIcon { pos = carrier.transform.position + Vector3.up * 0.4f, label = "Б", owner = attackers, tint = orange });
        }

        protected override string EventText(byte kind, int team, int arg, int my, out string sub, out bool good, out Sfx? sound)
        {
            sub = null; good = true; sound = Sfx.Switch;
            switch (kind)
            {
                case EvRound:
                {
                    bool swap = arg > 1 && AttackersOf(arg) != AttackersOf(arg - 1);
                    sub = (swap ? "Смена сторон!  " : "") + (my == team ? "Вы атакуете: заложите бомбу на A или B" : "Вы защищаете точки A и B");
                    return $"РАУНД {arg}";
                }
                case EvGotBomb:
                {
                    // the host knows the carrier itself (offline every netId is 0); clients match the id
                    var me = Me;
                    bool mine = me && my == team && (host ? carrier == me : me.netId != 0 && me.netId == arg);
                    if (!mine) { sound = null; return null; }
                    sub = "Несите её на точку A или B";
                    sound = Sfx.Equip;
                    return "У ВАС БОМБА";
                }
                case EvDropped:
                    if (my != team) { sound = null; return null; }
                    good = false; sound = Sfx.ErrorTone; sub = "Подберите её — она отмечена на миникарте";
                    return "БОМБА ПОТЕРЯНА";
                case EvPlanted:
                {
                    string s = Letters[Mathf.Clamp(arg, 0, 1)];
                    good = my == team;
                    sound = good ? Sfx.AbilityReady : Sfx.ErrorTone;
                    sub = good ? $"Охраняйте её {Mathf.RoundToInt(BombTime)} секунд" : $"{Mathf.RoundToInt(BombTime)} секунд до взрыва — обезвредьте!";
                    return good ? $"БОМБА ЗАЛОЖЕНА НА {s}" : $"БОМБА НА ТОЧКЕ {s}!";
                }
                case EvRoundEnd:
                {
                    int reason = arg & 15, w0 = (arg >> 4) & 15, w1 = (arg >> 8) & 15;
                    wins[0] = w0; wins[1] = w1;
                    good = my == team;
                    sound = good ? Sfx.AbilityReady : Sfx.ErrorTone;
                    string why = reason switch
                    {
                        ReasonBomb => "Бомба взорвалась",
                        ReasonDefused => "Бомба обезврежена",
                        ReasonTime => "Время вышло",
                        _ => team == my ? "Противники уничтожены" : "Ваша команда уничтожена",
                    };
                    sub = $"{why}  ·  {Teams.Names[0]} {w0} : {w1} {Teams.Names[1]}";
                    return good ? "РАУНД ВЫИГРАН" : "РАУНД ПРОИГРАН";
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ network

        public override void WriteState(BinaryWriter w)
        {
            WriteBases(w);
            w.Write(placed);
            WV(w, sites[0]); WV(w, sites[1]);
            w.Write((byte)phase); w.Write(timer); w.Write((byte)round);
            w.Write((byte)wins[0]); w.Write((byte)wins[1]); w.Write((sbyte)attackers);
            w.Write((byte)bomb); w.Write((short)carrierId); WV(w, bombPos); w.Write((sbyte)plantedSite);
        }

        public override void ReadState(BinaryReader r)
        {
            ReadBases(r);
            placed = r.ReadBoolean();
            sites[0] = RV(r); sites[1] = RV(r);
            phase = (Phase)r.ReadByte(); timer = r.ReadSingle(); round = r.ReadByte();
            wins[0] = r.ReadByte(); wins[1] = r.ReadByte(); attackers = r.ReadSByte();
            bomb = (BombState)r.ReadByte(); carrierId = r.ReadInt16(); bombPos = RV(r); plantedSite = r.ReadSByte();
        }
    }
}
