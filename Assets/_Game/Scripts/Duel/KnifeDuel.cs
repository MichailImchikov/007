using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Knife duel. When two enemies knife each other at the same moment (one swings while the other's blade is
    /// already on its way, or answers a hit within <see cref="Window"/> seconds) the blades lock: both fighters are
    /// pulled face to face, the camera of a local participant goes cinematic, and every F (or LMB) is a strike —
    /// no damage, but it pushes the slider toward the opponent. Whoever drives it to the end plays the finisher:
    /// disarm, slow-motion stab, the loser drops. While the duel lasts nobody can hurt either of them.
    ///
    /// Local simulation only: participants simulated on another machine (LAN proxies) never start a duel — their
    /// knife hits go through as ordinary damage.
    /// </summary>
    [DefaultExecutionOrder(900)]
    public class KnifeDuel : MonoBehaviour
    {
        public static readonly List<KnifeDuel> Active = new List<KnifeDuel>();
        /// <summary>The local player is fighting: the camera is the duel's, the view model is hidden.</summary>
        public static bool LocalCinematic { get; private set; }

        /// <summary>Two knives within this many seconds of each other lock into a duel.</summary>
        public const float Window = 0.6f;
        const float Push = 0.062f;          // slider share per strike (≈16 strikes of advantage end it)
        const float IntroTime = 0.75f, MaxFight = 9f, FinishTime = 2.1f, OutroTime = 0.45f;
        const float Gap = 1.12f;            // distance between the fighters

        // ------------------------------------------------------------------ clash detection

        static readonly Dictionary<Combatant, float> swings = new Dictionary<Combatant, float>();
        struct Strike { public Combatant victim; public float time, amount; }
        static readonly Dictionary<Combatant, Strike> strikes = new Dictionary<Combatant, Strike>();
        static bool allowKill;

        public static bool IsKnife(string w)
            => !string.IsNullOrEmpty(w) && (w == "Нож" || MeleeCatalog.Get(w).name == w);

        public static KnifeDuel Of(Combatant c)
        {
            if (!c) return null;
            for (int i = 0; i < Active.Count; i++) if (Active[i] && (Active[i].a.c == c || Active[i].b.c == c)) return Active[i];
            return null;
        }

        public static bool InDuel(Combatant c) => Of(c) != null;

        /// <summary>Knife swing started (player slash, bot stab): a hit landing on this combatant meanwhile is a clash.</summary>
        public static void NoteSwing(Combatant c)
        {
            if (c) swings[c] = Time.time;
        }

        /// <summary>When this combatant last started a knife swing (-99 = never).</summary>
        public static float SwingTime(Combatant c) => c && swings.TryGetValue(c, out float t) ? t : -99f;

        /// <summary>
        /// Called by PlayerHealth / CombatBot before applying damage. True = swallow it: the victim or the attacker
        /// is in a duel, or this knife hit just started one.
        /// </summary>
        public static bool Blocks(DamageInfo info, Combatant victim)
        {
            if (!victim || allowKill) return false;
            if (InDuel(victim)) return true;
            var a = info.attacker;
            if (a && a != victim && InDuel(a)) return true;
            if (!a || a == victim || info.fromNetwork || !IsKnife(info.weapon)) return false;
            if (a.remote || victim.remote || !a.alive || !victim.alive || !a.IsEnemyOf(victim)) return false;
            if (SkyArena.Active || DroneMode.Active || (MatchManager.Instance && MatchManager.Instance.Ended)) return false;

            float now = Time.time;
            bool clash = false;
            float refund = 0f;
            // the victim's own blade was already on its way toward the attacker
            if (swings.TryGetValue(victim, out float st) && now - st <= Window && Faces(victim, a)) clash = true;
            // ... or it already landed on the attacker a moment ago: give that health back, the duel decides
            if (strikes.TryGetValue(victim, out var s) && s.victim == a && now - s.time <= Window) { clash = true; refund = s.amount; }
            if (!clash || !CanFight(a) || !CanFight(victim))
            {
                strikes[a] = new Strike { victim = victim, time = now, amount = info.amount };
                return false;
            }
            strikes.Remove(a); strikes.Remove(victim);
            swings.Remove(a); swings.Remove(victim);
            if (refund > 0f) Heal(a, refund);
            Begin(a, victim);
            return true;
        }

        static bool Faces(Combatant c, Combatant other)
        {
            Vector3 to = other.transform.position - c.transform.position; to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) return true;
            Vector3 f = c.transform.forward; f.y = 0f;
            return Vector3.Angle(f, to) < 80f;
        }

        static bool CanFight(Combatant c)
        {
            if (!c || !c.alive || c.remote || TicTacToe.InGame(c)) return false;
            var ph = c.GetComponent<PlayerHealth>();
            if (ph) return !ph.IsDead && !ph.BeingExecuted && !(ph.motor && (ph.motor.IsVaulting || ph.motor.IsPulled))
                           && !(c.abilities && (c.abilities.Phasing || c.abilities.Morphed));
            if (c.abilities && (c.abilities.Phasing || c.abilities.Morphed)) return false;
            var bot = c.GetComponent<CombatBot>();
            if (bot) return bot.IsAlive && !bot.ExternalControl;
            // shooting range sparring partner
            var dummy = c.GetComponent<TrainingBot>();
            return dummy && dummy.IsAlive && !dummy.ExternalControl && !dummy.IsBeingExecuted;
        }

        static void Heal(Combatant c, float amount)
        {
            var ph = c.GetComponent<PlayerHealth>();
            if (ph) { ph.Heal(amount); return; }
            var bot = c.GetComponent<CombatBot>();
            if (bot) bot.Heal(amount);
        }

        static void Begin(Combatant a, Combatant b)
        {
            var go = new GameObject("KnifeDuel");
            var d = go.AddComponent<KnifeDuel>();
            d.Init(a, b);
        }

        // ------------------------------------------------------------------ fighters

        class Side
        {
            public Combatant c;
            public PlayerHealth ph;
            public PlayerMotor motor;
            public PlayerCamera cam;
            public MeleeController melee;
            public Loadout loadout;
            public CombatBot bot;
            public TrainingBot dummy;
            /// <summary>Not a human: strikes on its own at <see cref="botRate"/>.</summary>
            public bool AI => bot || dummy;
            public CharacterAnimator anim;
            public HumanoidRig rig;
            public Transform knife, hand, forearm;
            public Vector3 startPos;
            public Quaternion startRot;
            public float strikeT = -1f, botNext, botRate, recoil;
            public int presses;
            public bool IsPlayer => ph;
            public bool IsLocal => ph && ph == PlayerHealth.Local;
            public Vector3 Hand => rig && rig.rHand ? rig.rHand.position : c.transform.position + Vector3.up * 1.25f;
        }

        Side a, b;
        int phase; // 0 intro, 1 fight, 2 finisher, 3 outro
        float t, fightT;
        /// <summary>-1 … +1: +1 = fighter A wins.</summary>
        float progress, shown;
        Side winner, loser;
        bool disarmed, stabbed, twisted, slowMo, ended;
        Vector3 center, axis;     // axis: A → B (flat)
        float wobble, wobbleSeed;
        Vector3 camFromPos;
        Quaternion camFromRot;
        Vector3 camPos;
        Quaternion camRot;
        float camShake;
        bool hudHidden;

        public Combatant A => a.c;
        public Combatant B => b.c;
        /// <summary>-1 … +1 toward A's victory (smoothed, for the slider).</summary>
        public float Progress => shown;
        public int Phase => phase;
        public float PhaseTime => t;
        public Combatant Winner => winner != null ? winner.c : null;
        public bool HasLocal => a.IsLocal || b.IsLocal;
        public Combatant LocalSide => a.IsLocal ? a.c : b.IsLocal ? b.c : null;
        public Vector3 ClashPoint => center + Vector3.up * 1.35f;
        /// <summary>Strike pulse 0..1 of a side (HUD shake).</summary>
        public float StrikePulse(Combatant c) { var s = c == a.c ? a : b; return s.strikeT < 0f ? 0f : 1f - Mathf.Clamp01(s.strikeT); }
        public int Presses(Combatant c) => c == a.c ? a.presses : b.presses;

        public static event System.Action<KnifeDuel> Started;

        Side MakeSide(Combatant c)
        {
            var s = new Side { c = c };
            s.ph = c.GetComponent<PlayerHealth>();
            if (s.ph)
            {
                s.motor = s.ph.motor;
                s.cam = s.ph.playerCam;
                s.loadout = s.ph.loadout;
                s.melee = c.GetComponent<MeleeController>();
                var body = c.GetComponent<PlayerBody>();
                if (body) { s.rig = body.rig; s.anim = body.anim; }
            }
            else
            {
                s.bot = c.GetComponent<CombatBot>();
                if (s.bot) { s.rig = s.bot.rig; s.anim = s.bot.anim; }
                else
                {
                    s.dummy = c.GetComponent<TrainingBot>();
                    if (s.dummy) { s.rig = s.dummy.rig; s.anim = s.dummy.anim; }
                }
            }
            if (!s.rig) s.rig = c.GetComponentInChildren<HumanoidRig>();
            if (!s.anim) s.anim = c.GetComponentInChildren<CharacterAnimator>();
            // the visible hand: the imported model's humanoid bones, else the procedural rig
            foreach (var an in c.GetComponentsInChildren<Animator>())
            {
                if (!an || !an.isHuman || !an.avatar) continue;
                var h = an.GetBoneTransform(HumanBodyBones.RightHand);
                var f = an.GetBoneTransform(HumanBodyBones.RightLowerArm);
                if (h && f) { s.hand = h; s.forearm = f; break; }
            }
            if (!s.hand && s.rig) { s.hand = s.rig.rHand; s.forearm = s.rig.rForearm; }
            s.startPos = c.transform.position;
            s.startRot = c.transform.rotation;
            s.botRate = 6f;
            return s;
        }

        void Init(Combatant ca, Combatant cb)
        {
            a = MakeSide(ca);
            b = MakeSide(cb);
            Active.Add(this);
            wobbleSeed = Random.value * 10f;

            Vector3 pa = ca.transform.position, pb = cb.transform.position;
            axis = pb - pa; axis.y = 0f;
            if (axis.sqrMagnitude < 0.0004f) axis = ca.transform.forward;
            axis.y = 0f; axis.Normalize();
            center = (pa + pb) * 0.5f;
            center.y = Mathf.Min(pa.y, pb.y);

            foreach (var s in new[] { a, b }) Grab(s);
            if (HasLocal)
            {
                var me = a.IsLocal ? a : b;
                camFromPos = me.cam.cam.transform.position;
                camFromRot = me.cam.cam.transform.rotation;
                camPos = camFromPos; camRot = camFromRot;
                LocalCinematic = true;
                if (HUD.Instance) { var cv = HUD.Instance.GetComponent<Canvas>(); if (cv && cv.enabled) { cv.enabled = false; hudHidden = true; } }
                KnifeDuelHUD.Ensure();
            }
            else if (PlayerHealth.Local) KnifeDuelHUD.Ensure(); // a floating bar over somebody else's duel

            // the clash itself: blades meet with a bang
            Vector3 cp = ClashPoint;
            FX.Ensure().SparkBurst(cp, Vector3.up, 30, 11f);
            ProcAudio.PlayAt(Sfx.KnifeClang, cp, 1f, 0.05f);
            ProcAudio.PlayAt(Sfx.Parry, cp, 0.8f, 0.05f);
            if (HasLocal) { ProcAudio.Play(Sfx.Parry, 0.6f, 0.05f); camShake = 1f; }
            Started?.Invoke(this);
        }

        void Grab(Side s)
        {
            if (s.c.abilities) s.c.abilities.ClearStatus();
            if (s.IsPlayer)
            {
                s.motor.Locked = true;
                s.cam.Locked = true;
                if (s.loadout && s.loadout.Active) s.loadout.Active.ExternalLower = 1f;
                if (s.melee) s.melee.DuelStrike = -1f;
            }
            else if (s.bot)
            {
                s.bot.CancelMelee();
                s.bot.ExternalControl = true;
                s.bot.YawOverride = null;
                int diff = s.bot.difficulty;
                s.botRate = (diff == 2 ? 7.4f : diff == 0 ? 4.6f : 6.1f) * Random.Range(0.88f, 1.12f);
                s.botNext = Random.Range(0.05f, 0.25f);
            }
            else if (s.dummy)
            {
                s.dummy.ExternalControl = true;
                s.botRate = s.dummy.duelRate * Random.Range(0.92f, 1.08f);
                s.botNext = Random.Range(0.05f, 0.25f);
            }
            if (s.anim) { s.anim.holdingWeapon = false; s.anim.knife = 0f; }
            HideGun(s);
            s.knife = BuildKnife();
            PoseKnife(s);
        }

        static Material bladeMat, gripMat;

        /// <summary>The gun in the body's hands goes away for the fight (AbilityUser.RefreshVisibility brings it back).</summary>
        static void HideGun(Side s)
        {
            if (!s.rig || !s.rig.weaponSocket) return;
            foreach (Transform ch in s.rig.weaponSocket) if (ch.gameObject.activeSelf) ch.gameObject.SetActive(false);
        }

        /// <summary>Knife follows the visible right hand: blade continues the forearm, out of the fist.</summary>
        void PoseKnife(Side s)
        {
            if (!s.knife || !s.hand) return;
            Vector3 dir = s.forearm ? s.hand.position - s.forearm.position : s.c.transform.forward;
            if (dir.sqrMagnitude < 1e-6f) dir = s.c.transform.forward;
            dir.Normalize();
            Vector3 fwd = s.c.transform.forward;
            Vector3 blade = (dir + fwd * 0.6f + Vector3.up * 0.25f).normalized;
            s.knife.SetPositionAndRotation(s.hand.position + dir * 0.06f, Quaternion.LookRotation(blade, Vector3.Cross(blade, s.c.transform.right)) * Quaternion.Euler(90f, 0f, 0f));
        }

        /// <summary>Simple knife (blade along local +Y), visible to everybody.</summary>
        static Transform BuildKnife()
        {
            if (!bladeMat)
            {
                bladeMat = AbilityProps.Mat(new Color(0.78f, 0.8f, 0.84f), 0f, 0.9f, 1f);
                gripMat = AbilityProps.Mat(new Color(0.08f, 0.08f, 0.09f), 0f, 0.3f);
            }
            var root = AbilityProps.Root("DuelKnife", null);
            root.localScale = Vector3.one * 1.25f;
            AbilityProps.Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0f), new Vector3(0.026f, 0.11f, 0.03f), gripMat, null, false);
            AbilityProps.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.06f, 0f), new Vector3(0.07f, 0.012f, 0.036f), gripMat, null, false);
            AbilityProps.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.15f, 0f), new Vector3(0.006f, 0.17f, 0.032f), bladeMat, null, false);
            AbilityProps.Part(PrimitiveType.Cube, root, new Vector3(0f, 0.245f, -0.006f), new Vector3(0.006f, 0.035f, 0.02f), bladeMat, new Vector3(30f, 0f, 0f), false);
            foreach (var c in root.GetComponentsInChildren<Collider>()) Destroy(c);
            return root;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || ended) return;
            if (!Valid(a) || !Valid(b)) { Abort(); return; }
            t += dt;
            wobble += dt;
            camShake = Mathf.MoveTowards(camShake, 0f, dt * 2.2f);

            switch (phase)
            {
                case 0:
                    if (t >= IntroTime) { phase = 1; t = 0f; }
                    break;
                case 1:
                    UpdateFight(dt);
                    break;
                case 2:
                    UpdateFinisher(dt);
                    break;
                case 3:
                    if (t >= OutroTime) { End(); return; }
                    break;
            }
            shown = Mathf.MoveTowards(Mathf.Lerp(shown, progress, 1f - Mathf.Exp(-14f * dt)), progress, dt * 0.05f);

            PlaceFighters(dt);
            Animate(a, dt);
            Animate(b, dt);
        }

        bool Valid(Side s) => s.c && s.c.gameObject.activeInHierarchy;

        void UpdateFight(float dt)
        {
            fightT += dt;
            if (MatchManager.Instance && MatchManager.Instance.Ended) { Abort(); return; }
            foreach (var s in new[] { a, b })
            {
                if (s.IsLocal)
                {
                    if (GameInput.MeleePressed || GameInput.FirePressed) Hit(s);
                }
                else if (s.AI)
                {
                    s.botNext -= dt;
                    if (s.botNext <= 0f)
                    {
                        float mine = s == a ? progress : -progress;
                        // a little rubber band: losing makes a bot mash harder
                        float rate = s.botRate * (1f + 0.3f * Mathf.Max(0f, -mine));
                        s.botNext = 1f / rate * Random.Range(0.6f, 1.4f);
                        Hit(s);
                    }
                }
                else if (s.IsPlayer && Random.value < dt * 6f) Hit(s); // a non-local human (should not happen offline)
            }
            if (Mathf.Abs(progress) >= 1f || fightT >= MaxFight)
            {
                winner = progress >= 0f ? a : b;
                loser = winner == a ? b : a;
                progress = winner == a ? 1f : -1f;
                phase = 2; t = 0f;
                ProcAudio.PlayAt(Sfx.KnifeClang, ClashPoint, 1f, 0.02f);
                if (HasLocal) { ProcAudio.Play(winner.IsLocal ? Sfx.AbilityReady : Sfx.Hurt, 0.7f, 0f); camShake = 0.8f; }
            }
        }

        /// <summary>One strike: the blade swings, meets the other one with sparks, the slider moves.</summary>
        void Hit(Side s)
        {
            s.presses++;
            s.strikeT = s.strikeT >= 0f && s.strikeT < 0.55f ? 0.28f : 0f;
            progress = Mathf.Clamp(progress + (s == a ? Push : -Push), -1f, 1f);
            var o = s == a ? b : a;
            o.recoil = Mathf.Min(1f, o.recoil + 0.45f);
            Vector3 cp = ClashPoint + Vector3.Cross(Vector3.up, axis) * Random.Range(-0.15f, 0.15f) + Vector3.up * Random.Range(-0.2f, 0.18f);
            Vector3 side = Vector3.Cross(Vector3.up, axis) * (Random.value < 0.5f ? -1f : 1f);
            FX.Ensure().SparkBurst(cp, (side + Vector3.up * 0.6f).normalized, Random.Range(6, 12), Random.Range(6f, 10f));
            ProcAudio.PlayAt(Random.value < 0.75f ? Sfx.KnifeClang : Sfx.Parry, cp, Random.Range(0.55f, 0.85f), 0.12f);
            if (Random.value < 0.35f) ProcAudio.PlayAt(Sfx.KnifeSwing, cp, 0.4f, 0.15f);
            if (s.IsLocal) { s.cam.Punch(new Vector3(-2f, Random.Range(-3f, 3f), Random.Range(-4f, 4f))); camShake = Mathf.Max(camShake, 0.18f); }
            if (HasLocal) fovPunch = Mathf.Min(fovPunch + 1.6f, 6f);
            if (o.IsLocal) camShake = Mathf.Max(camShake, 0.3f);
        }

        void UpdateFinisher(float dt)
        {
            // disarm: the loser's knife is knocked out of the hand
            if (!disarmed && t >= 0.12f)
            {
                disarmed = true;
                Vector3 cp = ClashPoint;
                FX.Ensure().SparkBurst(cp, Vector3.up, 24, 12f);
                ProcAudio.PlayAt(Sfx.KnifeClang, cp, 1f, 0.03f);
                ThrowKnife(loser);
                loser.recoil = 1f;
                camShake = Mathf.Max(camShake, 0.5f);
            }
            if (!slowMo && t >= 0.36f && !Net.Active && Mathf.Approximately(Time.timeScale, 1f) && HasLocal)
            {
                slowMo = true;
                Time.timeScale = 0.3f;
            }
            if (!stabbed && t >= 0.5f)
            {
                stabbed = true;
                winner.strikeT = 0.3f;
                Kill();
            }
            if (slowMo && t >= 0.72f) { slowMo = false; if (Mathf.Approximately(Time.timeScale, 0.3f)) Time.timeScale = 1f; }
            if (!twisted && t >= 0.95f)
            {
                twisted = true;
                Vector3 chest = loser.c.Chest;
                FX.Ensure().Blood(chest, -axisFrom(winner), 10);
                ProcAudio.PlayAt(Sfx.KnifeHit, chest, 0.7f, 0.1f);
            }
            if (t >= FinishTime) { phase = 3; t = 0f; ReleaseCamera(); }
        }

        Vector3 axisFrom(Side s) => s == a ? axis : -axis;

        void Kill()
        {
            Vector3 dir = axisFrom(winner);
            Vector3 chest = loser.c.Chest;
            FX.Ensure().Blood(chest, -dir + Vector3.up * 0.3f, 22);
            FX.Ensure().Blood(chest + dir * 0.15f, dir, 12);
            ProcAudio.PlayAt(Sfx.Stab, chest, 1f, 0.05f);
            ProcAudio.PlayAt(Sfx.Kill, chest, 0.6f, 0.05f);
            if (winner.IsLocal) { ProcAudio.Play(Sfx.Stab, 0.8f); HUD.Ensure().ShowHitmarker(true, true); HUD.Ensure().RegisterHit(true); }
            camShake = 1f;

            // release the loser so its own death code owns the body
            if (loser.bot) loser.bot.ExternalControl = false;
            if (loser.dummy) loser.dummy.ExternalControl = false;
            var dmg = loser.c.GetComponent<IDamageable>();
            allowKill = true;
            try
            {
                dmg?.TakeDamage(new DamageInfo
                {
                    amount = 9999f, point = chest, normal = -dir, direction = dir, critical = true,
                    attacker = winner.c, weapon = "Нож"
                });
            }
            finally { allowKill = false; }
            if (loser.c.alive)
            {
                // something refused the damage (spawn protection etc.): the duel still ends
                if (loser.bot) loser.bot.ExternalControl = true;
                if (loser.dummy) loser.dummy.ExternalControl = true;
            }
            if (loser.anim) loser.anim.knife = -1f;
        }

        void ThrowKnife(Side s)
        {
            if (!s.knife) return;
            var k = s.knife;
            s.knife = null;
            k.SetParent(null, true);
            var bc = k.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.12f, 0f);
            bc.size = new Vector3(0.03f, 0.3f, 0.04f);
            var rb = k.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.3f;
            Vector3 away = -axisFrom(s) + Vector3.Cross(Vector3.up, axis) * Random.Range(-0.8f, 0.8f);
            rb.linearVelocity = away.normalized * 3.5f + Vector3.up * 4.5f;
            rb.angularVelocity = Random.onUnitSphere * 25f;
            Destroy(k.gameObject, 6f);
        }

        // ------------------------------------------------------------------ bodies

        void PlaceFighters(float dt)
        {
            // the pair sways around its center; the one losing is pushed back, the winner leans in
            float intro = phase == 0 ? Anim.Smooth(t / IntroTime) : 1f;
            float sway = Mathf.Sin(wobble * 0.9f + wobbleSeed) * 9f + Mathf.Sin(wobble * 2.3f + wobbleSeed * 2f) * 3f;
            Vector3 ax = Quaternion.Euler(0f, phase <= 1 ? sway : 0f, 0f) * axis;
            float drift = shown * 0.22f;
            float lunge = phase >= 2 && winner != null ? Anim.Smooth((phase == 2 ? t : FinishTime) / 0.35f) * 0.3f * (winner == a ? 1f : -1f) : 0f;
            Vector3 mid = center + ax * (drift + lunge);
            Place(a, mid - ax * (Gap * 0.5f + (a.recoil * 0.06f)), ax, intro, dt);
            Place(b, mid + ax * (Gap * 0.5f + (b.recoil * 0.06f)), -ax, intro, dt);
        }

        void Place(Side s, Vector3 want, Vector3 facing, float intro, float dt)
        {
            s.recoil = Mathf.MoveTowards(s.recoil, 0f, dt * 3.5f);
            if (!s.c.alive) return; // its death animation owns it now
            // every strike is a short lunge of the whole body
            if (s.strikeT >= 0f) want += facing.normalized * 0.11f * Mathf.Sin(Mathf.Clamp01(s.strikeT) * Mathf.PI);
            var tr = s.c.transform;
            want.y = tr.position.y;
            if (Physics.Raycast(want + Vector3.up * 1f, Vector3.down, out var gh, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !gh.collider.transform.IsChildOf(tr))
                want.y = Mathf.Lerp(want.y, gh.point.y, 0.5f);
            Vector3 pos = phase == 0 ? Vector3.Lerp(s.startPos, want, intro) : Vector3.Lerp(tr.position, want, 1f - Mathf.Exp(-12f * dt));
            float yaw = Quaternion.LookRotation(facing).eulerAngles.y;
            if (s.IsPlayer)
            {
                tr.position = pos;
                float curYaw = phase == 0 ? Mathf.LerpAngle(s.startRot.eulerAngles.y, yaw, intro) : yaw;
                s.cam.SetLook(curYaw, Mathf.Lerp(s.cam.Pitch, 6f, 1f - Mathf.Exp(-6f * dt)));
            }
            else
            {
                tr.position = pos;
                Quaternion r = Quaternion.Euler(0f, yaw, 0f);
                tr.rotation = phase == 0 ? Quaternion.Slerp(s.startRot, r, intro) : r;
            }
        }

        void Animate(Side s, float dt)
        {
            if (s.strikeT >= 0f)
            {
                s.strikeT += dt / 0.3f;
                if (s.strikeT >= 1f) s.strikeT = -1f;
            }
            bool alive = s.c.alive;
            // between strikes the arm stays in the guard (wind-up) pose
            // guard: the knife hand up and ready (most of the wind-up), breathing a little
            float knife = !alive ? -1f : s.strikeT >= 0f ? s.strikeT : 0.3f + 0.03f * Mathf.Sin(wobble * 5f + (s == a ? 0f : 1.7f));
            if (phase == 3 && s == winner) knife = Mathf.Lerp(0.3f, -1f, Mathf.Clamp01(t / OutroTime));
            if (s.anim)
            {
                s.anim.holdingWeapon = false;
                s.anim.knife = knife < 0f ? -1f : Mathf.Clamp01(knife);
                if (s.AI) { s.anim.aim = 0.2f; s.anim.crouch = Anim.Damp(s.anim.crouch, alive && phase <= 1 ? 0.18f : 0f, 6f, dt); }
            }
            if (s.melee) s.melee.DuelStrike = s.anim ? s.anim.knife : knife;
            if (alive) HideGun(s);
            if (s.IsPlayer)
            {
                if (alive && s.loadout && s.loadout.Active) s.loadout.Active.ExternalLower = 1f;
                // the camera films the whole body (head included) until the duel lets go
                if (s.IsLocal && (alive || phase < 3)) s.cam.ThirdPerson = phase == 3 ? 1f - Mathf.Clamp01(t / OutroTime) : 1f;
            }
        }

        // ------------------------------------------------------------------ camera (local participant)

        // cinematic shots: the fight is cut like a film. Framings change every 1.3-2.1 s (hard cuts or fast whips),
        // each framing moves on its own (dolly, orbit, crane); strikes punch the lens, a lopsided fight pushes in
        int shot = 1, lastShot = -1, camPhase = -1;
        float shotSide = 1f, shotT, shotLen = 1.7f, blendLen, fovPunch;
        Vector3 prevPos;
        Quaternion prevRot;
        float prevFov = 55f;

        const int Shots = 6;

        /// <summary>One framing at <paramref name="st"/> seconds into it.</summary>
        void ShotPose(int s, float st, Vector3 myPos, Vector3 otherPos, Vector3 ax, Vector3 right, Vector3 mid,
                      out Vector3 pos, out Vector3 look, out float fov, out float roll)
        {
            Vector3 up = Vector3.up;
            look = mid + up * 1.3f;
            fov = 55f;
            roll = 0f;
            float side = shotSide;
            switch (s)
            {
                default: // over my shoulder, slow push in
                {
                    float push = Mathf.Lerp(1.75f, 1.3f, Anim.Smooth(st / 2.2f));
                    pos = myPos - ax * push + right * (side * 0.8f) + up * 1.72f;
                    look += right * side * 0.1f;
                    fov = 52f;
                    break;
                }
                case 1: // wide orbit around the pair
                {
                    Vector3 d = Quaternion.Euler(0f, side * (62f + st * 16f), 0f) * -ax;
                    pos = mid + d * 2.9f + up * (1.55f + st * 0.05f);
                    fov = 50f;
                    break;
                }
                case 2: // low hero angle from the side, the crane rises
                    pos = mid + right * side * 1.75f - ax * 0.35f + up * (0.32f + st * 0.12f);
                    look = mid + up * 1.55f;
                    fov = 60f;
                    break;
                case 3: // reverse shot over the opponent's shoulder: your own face in the fight
                {
                    float push = Mathf.Lerp(1.7f, 1.4f, Anim.Smooth(st / 2f));
                    pos = otherPos + ax * push - right * side * 0.8f + up * 1.7f;
                    fov = 52f;
                    break;
                }
                case 4: // top-down spiral
                {
                    Vector3 d = Quaternion.Euler(0f, st * 38f * side, 0f) * right;
                    pos = mid + d * 1.3f + up * (3.7f - st * 0.35f);
                    look = mid + up * 0.9f;
                    fov = 58f;
                    break;
                }
                case 5: // tight on the blades, dutch angle, drifting across
                    pos = mid + right * side * (0.95f - st * 0.05f) - ax * (0.25f - st * 0.2f) + up * 1.48f;
                    look = ClashPoint;
                    fov = 46f;
                    roll = 11f * side;
                    break;
            }
        }

        bool FramedPose(int s, float st, Vector3 myPos, Vector3 otherPos, Vector3 ax, Vector3 right, Vector3 mid, Transform ia, Transform ib,
                        out Vector3 pos, out Quaternion rot, out float fov)
        {
            ShotPose(s, st, myPos, otherPos, ax, right, mid, out var want, out var look, out fov, out float roll);
            // a one-sided fight closes in on the blades
            float press = Mathf.Abs(progress);
            want = Vector3.Lerp(want, look, 0.18f * press * press);
            fov -= 6f * press * press;
            float intended = Vector3.Distance(look, want);
            pos = Collide(look, want, ia, ib);
            Vector3 fwd = look - pos;
            if (fwd.sqrMagnitude < 1e-4f) fwd = ax;
            rot = Quaternion.LookRotation(fwd) * Quaternion.Euler(0f, 0f, roll);
            // false: a wall pushed the lens in, or it sits inside one of the fighters
            float got = fwd.magnitude;
            if (got < intended * 0.7f || got < 0.95f) return false;
            return !InsideBody(pos, ia) && !InsideBody(pos, ib);
        }

        static bool InsideBody(Vector3 p, Transform body)
        {
            Vector3 b = body.position;
            Vector3 d = p - b;
            float h = d.y;
            d.y = 0f;
            return h > -0.1f && h < 2f && d.magnitude < 0.55f;
        }

        void NextShot(Vector3 myPos, Vector3 otherPos, Vector3 ax, Vector3 right, Vector3 mid, Transform ia, Transform ib, Camera cam)
        {
            prevPos = cam.transform.position;
            prevRot = cam.transform.rotation;
            prevFov = cam.fieldOfView;
            float press = Mathf.Abs(progress);
            for (int tries = 0; tries < 8; tries++)
            {
                int s;
                // a close fight goes wide, a lopsided one goes tight
                if (press > 0.55f && Random.value < 0.6f) s = Random.value < 0.5f ? 5 : 2;
                else s = Random.Range(0, Shots);
                if (s == shot || (s == lastShot && tries < 6)) continue;
                shotSide = Random.value < 0.5f ? -1f : 1f;
                if (!FramedPose(s, 0f, myPos, otherPos, ax, right, mid, ia, ib, out _, out _, out _) && tries < 7) continue;
                lastShot = shot;
                shot = s;
                break;
            }
            shotT = 0f;
            shotLen = Random.Range(1.3f, 2.1f);
            blendLen = Random.value < 0.45f ? 0.22f : 0f; // whip pan or a hard cut
            if (blendLen <= 0f) fovPunch = Mathf.Max(fovPunch, 3f);
        }

        void LateUpdate()
        {
            if (ended) return;
            PoseKnife(a);
            PoseKnife(b);
            if (!HasLocal) return;
            var me = a.IsLocal ? a : b;
            if (!me.cam || !me.cam.cam) return;
            var cam = me.cam.cam;
            if (phase == 3)
            {
                // back to the player's own camera (PlayerCamera already placed it this frame)
                var nt = cam.transform;
                float k = Anim.Smooth(t / OutroTime);
                nt.SetPositionAndRotation(Vector3.Lerp(camPos, nt.position, k), Quaternion.Slerp(camRot, nt.rotation, k));
                return;
            }
            var other = me == a ? b : a;
            float dt = Time.unscaledDeltaTime;
            Transform ia = me.c.transform, ib = other.c.transform;
            Vector3 myPos = ia.position, otherPos = ib.position;
            Vector3 ax = otherPos - myPos; ax.y = 0f;
            if (ax.sqrMagnitude < 0.001f) ax = me == a ? axis : -axis;
            ax.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, ax);
            Vector3 mid = (myPos + otherPos) * 0.5f;
            fovPunch = Mathf.MoveTowards(fovPunch, 0f, dt * 18f);

            Vector3 pos;
            Quaternion rot;
            float fov;
            if (phase == 0)
            {
                // swoop: an arc around the pair from the player's eyes into the first framing
                FramedPose(shot, 0f, myPos, otherPos, ax, right, mid, ia, ib, out var tp, out var tr, out fov);
                float k = Anim.Smooth(t / IntroTime);
                Vector3 c0 = mid + Vector3.up * 1.3f;
                Vector3 from = camFromPos - c0, to = tp - c0;
                pos = c0 + Vector3.Slerp(from, to, k);
                rot = Quaternion.Slerp(camFromRot, tr, Anim.Smooth(Mathf.Clamp01(k * 1.4f)));
                fov = Mathf.Lerp(62f, fov, k) - 10f * Mathf.Sin(k * Mathf.PI); // a dolly-zoom kick mid-swoop
            }
            else if (phase == 1)
            {
                if (camPhase != 1) { shotT = 0f; shotLen = Random.Range(1.4f, 2f); blendLen = 0f; }
                shotT += dt;
                if (shotT >= shotLen) NextShot(myPos, otherPos, ax, right, mid, ia, ib, cam);
                // the fighters drifted and a wall / a body got in the way: cut away right now
                if (!FramedPose(shot, shotT, myPos, otherPos, ax, right, mid, ia, ib, out pos, out rot, out fov) && shotT > 0.25f)
                {
                    NextShot(myPos, otherPos, ax, right, mid, ia, ib, cam);
                    blendLen = 0f;
                    FramedPose(shot, shotT, myPos, otherPos, ax, right, mid, ia, ib, out pos, out rot, out fov);
                }
                if (blendLen > 0f && shotT < blendLen)
                {
                    float k = Anim.Smooth(shotT / blendLen);
                    pos = Vector3.Lerp(prevPos, pos, k);
                    rot = Quaternion.Slerp(prevRot, rot, k);
                    fov = Mathf.Lerp(prevFov, fov, k);
                }
            }
            else
            {
                // finisher: cut low by the pair, the crane rises and orbits while it pushes in on the stab
                if (camPhase != 2) { prevPos = cam.transform.position; prevRot = cam.transform.rotation; prevFov = cam.fieldOfView; shotSide = winner == me ? 1f : -1f; }
                Vector3 wPos = winner.c.transform.position;
                Vector3 toL = loser.c.transform.position - wPos; toL.y = 0f;
                if (toL.sqrMagnitude < 1e-4f) toL = ax;
                toL.Normalize();
                Vector3 r2 = Vector3.Cross(Vector3.up, toL);
                float u = Anim.Smooth(t / FinishTime);
                float orbit = Mathf.Lerp(-55f, 25f, u) * shotSide;
                float dist = Mathf.Lerp(2.9f, 1.75f, Anim.Smooth((t - 0.25f) / 0.45f));
                Vector3 dir = Quaternion.Euler(0f, orbit, 0f) * r2;
                Vector3 look = mid + Vector3.up * Mathf.Lerp(1.35f, 1.1f, u);
                Vector3 want = mid + dir * dist + Vector3.up * Mathf.Lerp(0.45f, 1.25f, u) - toL * 0.2f;
                pos = Collide(look, want, ia, ib);
                rot = Quaternion.LookRotation(look - pos) * Quaternion.Euler(0f, 0f, Mathf.Lerp(-8f, 0f, u) * shotSide);
                fov = Mathf.Lerp(54f, 38f, Anim.Smooth((t - 0.35f) / 0.3f));
                if (t < 0.12f)
                {
                    float k = Anim.Smooth(t / 0.12f);
                    pos = Vector3.Lerp(prevPos, pos, k);
                    rot = Quaternion.Slerp(prevRot, rot, k);
                    fov = Mathf.Lerp(prevFov, fov, k);
                }
            }
            camPhase = phase;
            camPos = pos;
            camRot = rot;

            float sh = camShake * camShake;
            float tt = Time.unscaledTime * 24f;
            Quaternion shake = Quaternion.Euler((Mathf.PerlinNoise(tt, 0.3f) - 0.5f) * 6f * sh, (Mathf.PerlinNoise(1.7f, tt) - 0.5f) * 6f * sh, (Mathf.PerlinNoise(tt, 3.1f) - 0.5f) * 8f * sh);
            // handheld breathing so even a still framing lives
            float ht = Time.unscaledTime;
            Quaternion breath = Quaternion.Euler(Mathf.Sin(ht * 1.3f) * 0.6f, Mathf.Sin(ht * 0.9f + 1f) * 0.8f, 0f);
            cam.transform.SetPositionAndRotation(camPos, camRot * breath * shake);
            cam.fieldOfView = Mathf.Clamp(fov - fovPunch, 30f, 75f);
        }

        static readonly RaycastHit[] camHits = new RaycastHit[12];

        static Vector3 Collide(Vector3 from, Vector3 to, Transform ignoreA, Transform ignoreB)
        {
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return to;
            d /= len;
            int n = Physics.SphereCastNonAlloc(from, 0.2f, d, camHits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float allowed = len;
            for (int i = 0; i < n; i++)
            {
                var tr = camHits[i].collider.transform;
                if (tr.IsChildOf(ignoreA) || tr.IsChildOf(ignoreB) || camHits[i].distance <= 0f) continue;
                if (camHits[i].distance < allowed) allowed = Mathf.Max(0.35f, camHits[i].distance - 0.05f);
            }
            return from + d * allowed;
        }

        // ------------------------------------------------------------------ end

        void ReleaseCamera()
        {
            if (slowMo) { slowMo = false; if (Mathf.Approximately(Time.timeScale, 0.3f)) Time.timeScale = 1f; }
            if (!HasLocal) return;
            LocalCinematic = false;
            if (hudHidden && HUD.Instance) { var cv = HUD.Instance.GetComponent<Canvas>(); if (cv) cv.enabled = true; }
            hudHidden = false;
        }

        void Release(Side s)
        {
            if (!s.c) return;
            if (s.knife) Destroy(s.knife.gameObject);
            s.knife = null;
            if (s.anim) { s.anim.knife = -1f; s.anim.holdingWeapon = true; }
            if (s.c.abilities) s.c.abilities.RefreshVisibility(); // the gun comes back
            if (s.dummy && s.dummy.sparring && s.dummy.heldWeapon) s.dummy.heldWeapon.gameObject.SetActive(false); // ... except for a sparring partner
            if (s.IsPlayer)
            {
                if (s.melee) s.melee.DuelStrike = -1f;
                if (!s.ph.IsDead)
                {
                    s.motor.Locked = false;
                    s.cam.Locked = false;
                    s.cam.ThirdPerson = 0f;
                    if (s.loadout && s.loadout.Active) s.loadout.Active.ExternalLower = 0f;
                }
            }
            else if (s.bot && s.bot.IsAlive) s.bot.ExternalControl = false;
            else if (s.dummy && s.dummy.IsAlive) s.dummy.ExternalControl = false;
        }

        void End()
        {
            if (ended) return;
            ended = true;
            ReleaseCamera();
            Release(a);
            Release(b);
            Active.Remove(this);
            Destroy(gameObject);
        }

        /// <summary>Match over / a fighter vanished: both walk away.</summary>
        public void Abort()
        {
            if (phase < 2) progress = 0f;
            End();
        }

        void OnDestroy()
        {
            if (!ended)
            {
                ended = true;
                ReleaseCamera();
                if (a != null) Release(a);
                if (b != null) Release(b);
            }
            Active.Remove(this);
            if (Active.Count == 0 || !Active.Exists(d => d && d.HasLocal)) LocalCinematic = false;
        }
    }
}
