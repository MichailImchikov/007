using System.IO;
using UnityEngine;

namespace FPS
{
    /// <summary>One-shot ability events broadcast by the caster's machine (visuals for everybody else).</summary>
    public enum AbilityNetEvent : byte { Grapple, Rocket, Fart, Swap, Area, Build }

    /// <summary>Effects one combatant's ability has on another one that lives on a different machine.</summary>
    public enum AbilityNetEffect : byte { Blind, Area, Teleport, Pull, PullEnd }

    /// <summary>
    /// Multiplayer side of the ability system.
    /// Rule: an ability runs for real only on the machine that simulates the caster (owner). Everybody else
    /// gets (a) persistent states via snapshots — sword, chicken, invisibility, phasing, scale, butt rocket —
    /// and (b) one-shot visual events (rope, rocket flight, fart cloud, built walls, shockwave).
    /// Effects on a combatant simulated elsewhere (blind, area debuffs, swap teleport, hook pull) are sent
    /// to that combatant's owner, who applies them through the normal single-player code.
    /// </summary>
    public partial class AbilityUser
    {
        // ------------------------------------------------------------------ replicated status getters
        public bool PhaseActive => phaseT > 0f || yankT >= 0f;
        public bool ButtRocketShown => rocketAim || (mountedRocket && !mountedRocket.Flying);
        public float BodyScale => bodyScale;
        public float HeadScale => headScale;

        /// <summary>Mirror of a combatant simulated on another machine (lives on a NetProxy).</summary>
        public bool Remote { get; private set; }
        public void SetRemote() => Remote = true;
        public void RefreshVisibility() { visKey = -1; ApplyVisibility(); }

        RocketProjectile remoteRocket;
        bool remoteInvis, remotePhase;

        /// <summary>NetProxy → mirror: apply the persistent ability states of the snapshot.</summary>
        public void ApplyRemoteState(in CharState s, bool alive)
        {
            if (!alive)
            {
                if (remoteRocket) Destroy(remoteRocket.gameObject);
                return;
            }
            bool chick = s.Has(CharState.Chicken), sword = s.Has(CharState.Sword);
            if (chick && chickenT <= 0f) StartChicken(1e6f);
            else if (!chick && chickenT > 0f) EndChicken();
            if (sword && parryT <= 0f) StartParry(1e6f);
            else if (!sword && parryT > 0f) EndParry();

            bool inv = s.Has(CharState.Invisible);
            if (inv && !remoteInvis) StartInvisibility(1e6f);
            if (!inv) invisT = 0f;
            remoteInvis = inv;

            bool ph = s.Has(CharState.Phasing);
            if (ph != remotePhase)
            {
                ProcAudio.PlayAt(Sfx.Warp, transform.position + Vector3.up, 0.9f, 0.05f);
                FX.Ensure().Puff(transform.position + Vector3.up, Vector3.up, 10, 1f, new Vector2(0.5f, 2f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                 new Color(0.75f, 0.5f, 1f, 0.45f), new Color(0.5f, 0.3f, 0.9f, 0.35f));
                visKey = -1;
            }
            remotePhase = ph;
            phaseT = ph ? 1f : 0f;

            bigHeadT = s.Has(CharState.BigHead) ? 1f : 0f;
            confuseT = s.Has(CharState.Confused) ? 1f : 0f;
            if (!Mathf.Approximately(bodyScale, s.bodyScale) || !Mathf.Approximately(headScale, s.headScale))
            {
                bodyScale = Mathf.Max(0.05f, s.bodyScale);
                headScale = Mathf.Max(0.05f, s.headScale);
                ApplyBodyScale();
            }

            if (s.Has(CharState.ButtRocket))
            {
                if (!remoteRocket) remoteRocket = RocketProjectile.Create(1.1f);
                Vector3 back = -transform.forward;
                remoteRocket.transform.position = ButtPoint + back * 0.04f;
                remoteRocket.transform.rotation = Quaternion.LookRotation((back + Vector3.up * 0.3f).normalized);
            }
            else if (remoteRocket) Destroy(remoteRocket.gameObject);

            ApplyVisibility();
        }

        void RemoteUpdate(float dt)
        {
            UpdateHook(dt);
            if (chickenT > 0f)
            {
                cluckT -= dt;
                if (cluckT <= 0f) { cluckT = Random.Range(1.2f, 2.8f); ProcAudio.PlayAt(Sfx.Cluck, transform.position + Vector3.up * 0.6f, 0.9f, 0.12f); }
            }
            if (parryAnim >= 0f) { parryAnim += dt / 0.3f; if (parryAnim >= 1f) parryAnim = -1f; }
        }

        // ------------------------------------------------------------------ sending (caster side)

        bool Networked => Net.Active && !Remote && self && self.netId != 0;

        void SendEvent(AbilityNetEvent e, System.Action<BinaryWriter> body)
        {
            if (Networked) NetGame.SendAbilityEvent(self, e, body);
        }

        /// <summary>Routing helper: apply now if the target is simulated here, otherwise send to its owner.</summary>
        void EffectOn(Combatant target, AbilityNetEffect e, System.Action<BinaryWriter> body, System.Action local)
        {
            if (!target) return;
            if (target.remote) { if (Net.Active && !Remote) NetGame.SendAbilityEffect(target, self, e, body); }
            else local();
        }

        static void W(BinaryWriter w, Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
        static Vector3 RV(BinaryReader r) => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
        static void W(BinaryWriter w, Quaternion q) { w.Write(q.x); w.Write(q.y); w.Write(q.z); w.Write(q.w); }
        static Quaternion RQ(BinaryReader r) => new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

        // ------------------------------------------------------------------ receiving: one-shot events on the mirror

        public void OnNetEvent(AbilityNetEvent e, BinaryReader r)
        {
            switch (e)
            {
                case AbilityNetEvent.Grapple:
                {
                    Vector3 end = RV(r);
                    int victimId = r.ReadInt16();
                    bool hasHit = r.ReadBoolean();
                    hookVictim = NetGame.Find(victimId);
                    hookEnd = end;
                    hookHasHit = hasHit;
                    hookPos = HandPoint;
                    hookState = 1;
                    hookTimer = 0f;
                    EnsureHookVisuals();
                    ProcAudio.PlayAt(Sfx.HookFire, hookPos, 0.9f, 0.06f);
                    break;
                }
                case AbilityNetEvent.Rocket:
                {
                    Vector3 pos = RV(r), dir = RV(r);
                    float speed = r.ReadSingle(), scale = r.ReadSingle();
                    var rp = RocketProjectile.Create(scale);
                    rp.visualOnly = true;
                    rp.transform.position = pos;
                    rp.Launch(dir, speed, self, transform);
                    bentPulse = 1.1f;
                    break;
                }
                case AbilityNetEvent.Fart:
                {
                    bool blind = r.ReadBoolean();
                    float smoke = r.ReadSingle();
                    Fart(blind, smoke);
                    break;
                }
                case AbilityNetEvent.Swap:
                {
                    Vector3 a = RV(r), b = RV(r);
                    foreach (var p in new[] { a, b })
                    {
                        FX.Ensure().Puff(p + Vector3.up, Vector3.up, 12, 1f, new Vector2(0.5f, 2.5f), new Vector2(0.5f, 1.1f), new Vector2(0.5f, 1f),
                                         new Color(1f, 0.4f, 0.85f, 0.5f), new Color(0.9f, 0.3f, 0.7f, 0.4f));
                        ProcAudio.PlayAt(Sfx.Warp, p + Vector3.up, 0.9f, 0.05f);
                    }
                    break;
                }
                case AbilityNetEvent.Area:
                {
                    var t = (AbilityType)r.ReadByte();
                    var info = AbilityDB.Get(t);
                    Shockwave.Spawn(transform.position + Vector3.up * 0.1f, info.radius, info.color);
                    ProcAudio.PlayAt(Sfx.Warp, transform.position + Vector3.up, 0.8f, 0.1f);
                    break;
                }
                case AbilityNetEvent.Build:
                {
                    int id = r.ReadInt32();
                    Vector3 c = RV(r); Quaternion q = RQ(r); Vector3 s = RV(r);
                    BuiltPiece.Create(c, q, s, id);
                    ProcAudio.PlayAt(Sfx.Hammer, c, 0.9f, 0.1f);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ receiving: effects on a combatant we simulate

        Combatant netPuller;
        float netPullT;

        public void OnNetEffect(AbilityNetEffect e, Combatant caster, BinaryReader r)
        {
            if (Remote || !self) return;
            switch (e)
            {
                case AbilityNetEffect.Blind:
                    Blind(r.ReadSingle());
                    break;
                case AbilityNetEffect.Area:
                {
                    var t = (AbilityType)r.ReadByte();
                    ReceiveArea(t, caster, AbilityDB.Get(t));
                    break;
                }
                case AbilityNetEffect.Teleport:
                {
                    Vector3 p = RV(r);
                    if (!self.alive) break;
                    Teleport(this, p);
                    if (isPlayer) ProcAudio.Play(Sfx.Warp, 0.5f);
                    break;
                }
                case AbilityNetEffect.Pull:
                    if (!self.alive || Phasing || !caster) break;
                    if (isPlayer)
                    {
                        var me = caster;
                        motor.StartPull(() => me ? me.Chest : transform.position, 16f, 2.2f, null);
                        pcam.Shake(0.4f);
                        ProcAudio.Play(Sfx.HookHit, 0.7f);
                    }
                    else if (puppet != null)
                    {
                        netPuller = caster;
                        netPullT = 0f;
                        puppet.ExternalControl = true;
                    }
                    break;
                case AbilityNetEffect.PullEnd:
                {
                    bool stun = r.ReadBoolean();
                    if (isPlayer) { if (motor.IsPulled) motor.StopPull(); }
                    else EndNetPull(stun);
                    break;
                }
            }
        }

        /// <summary>A bot dragged by a grappling hook whose caster lives on another machine.</summary>
        void UpdateNetPull(float dt)
        {
            if (!netPuller || puppet == null) return;
            netPullT += dt;
            if (!self.alive || !netPuller.alive) { EndNetPull(false); return; }
            Vector3 f = netPuller.transform.forward; f.y = 0f;
            Vector3 dest = netPuller.transform.position + (f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward) * 1.5f;
            Vector3 cur = puppet.transform.position;
            Vector3 to = dest - cur; to.y = 0f;
            if (to.magnitude < 0.4f || netPullT > 2.4f) { EndNetPull(true); return; }
            Vector3 np = cur + to.normalized * Mathf.Min(17f * dt, to.magnitude);
            float gy = GroundY(np, puppet.transform, cur.y);
            np.y = Mathf.Lerp(cur.y, Mathf.Max(gy, cur.y - 0.5f) + 0.25f * Mathf.Sin(Mathf.Clamp01(netPullT * 2f) * Mathf.PI), 12f * dt);
            puppet.transform.position = np;
        }

        void EndNetPull(bool stun)
        {
            if (!netPuller) return;
            netPuller = null;
            if (puppet != null && puppet.ExternalControl)
            {
                puppet.ExternalControl = false;
                if (stun && self.alive) puppet.Stun(0.9f);
            }
        }
    }
}
