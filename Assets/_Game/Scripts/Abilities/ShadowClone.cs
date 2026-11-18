using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>
    /// "Теневые клоны": in a burst of smoke the caster splits into identical copies — same look, same name, same gun.
    /// Clones scatter, then act on their own (hunt the nearest enemy, strafe, shoot), and enemies can't tell them
    /// apart from the original. Their shots are an illusion: they deal no damage at all — clones are pure decoys. Any hit makes a clone vanish in a puff; they also go when time runs out or the
    /// original falls. Popping a clone is no kill.
    /// Online: clones are harmless decoys, so every machine simulates them itself — the spawn (positions, gun) and
    /// every pop are synchronized (AbilityNetEvent.Clones / ClonePop), the wandering in between is local.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class ShadowClone : MonoBehaviour, IDamageable
    {
        public static readonly List<ShadowClone> All = new List<ShadowClone>();

        const float Life = 14f;
        static Material[] palette;

        Combatant self, owner;
        HumanoidRig rig;
        CharacterAnimator anim;
        NavMeshAgent agent;
        Transform muzzle;
        float life, think, fireCd, react, strafeT, yawVel, scatterT;
        float rpm, spread;
        int burst;
        Combatant target;
        bool targetVisible, popped;
        Vector3 dest, scatterDir, lastPos, strafeOffset;
        Sfx sfx;
        readonly RaycastHit[] hits = new RaycastHit[12];

        public Combatant Owner => owner;
        /// <summary>Which clone of the set (the same on every machine).</summary>
        int index;

        // ------------------------------------------------------------------ casting

        /// <summary>Ability entry point. False (with the reason) when it can't run here.</summary>
        public static bool Cast(Combatant caster, int count, out string why)
        {
            why = null;
            if (ObjectiveMode.Current) { why = "Не в командных раундах"; return false; }
            if (!caster || !caster.alive) return false;
            // a fresh set replaces the old one
            PopAll(caster);

            Vector3 f = caster.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            f.Normalize();
            float[] angles = count >= 3 ? new[] { -80f, 80f, 180f } : new[] { -80f, 80f };
            Vector3 chest = caster.Chest;
            int n = Mathf.Min(count, angles.Length);
            var ps = new Vector3[n];
            var ds = new Vector3[n];
            string gun = CurrentWeapon(caster);
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, angles[i], 0f) * f;
                float dist = 1.6f;
                if (Physics.SphereCast(chest, 0.3f, dir, out var h, dist + 0.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !h.collider.transform.IsChildOf(caster.transform))
                    dist = Mathf.Max(0.3f, h.distance - 0.4f);
                ps[i] = caster.transform.position + dir * dist;
                ds[i] = dir;
                Spawn(caster, ps[i], Quaternion.LookRotation(f), dir, i, gun);
            }
            Poof(caster.transform.position);
            // everybody else spawns the same set (and simulates it locally)
            if (caster.abilities)
                caster.abilities.SendEventPublic(AbilityNetEvent.Clones, w =>
                {
                    w.Write(gun ?? "");
                    w.Write(f.x); w.Write(f.z);
                    w.Write((byte)n);
                    for (int i = 0; i < n; i++) { w.Write(ps[i].x); w.Write(ps[i].y); w.Write(ps[i].z); w.Write(ds[i].x); w.Write(ds[i].z); }
                });
            return true;
        }

        static void PopAll(Combatant owner)
        {
            for (int i = All.Count - 1; i >= 0; i--) if (All[i] && All[i].owner == owner) All[i].Pop(false);
        }

        /// <summary>Another machine's fighter cast clones: spawn the same set here.</summary>
        public static void OnNetSpawn(Combatant caster, BinaryReader r)
        {
            string gun = r.ReadString();
            Vector3 f = new Vector3(r.ReadSingle(), 0f, r.ReadSingle());
            int n = r.ReadByte();
            if (!caster) return;
            PopAll(caster);
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                Vector3 d = new Vector3(r.ReadSingle(), 0f, r.ReadSingle());
                Spawn(caster, p, Quaternion.LookRotation(f.normalized), d, i, gun);
            }
            Poof(caster.transform.position);
        }

        /// <summary>
        /// A clone was popped on another machine. <paramref name="relay"/>: we own the original — pop it here and
        /// tell everybody else.
        /// </summary>
        public static void OnNetPop(Combatant owner, int index, bool relay)
        {
            for (int i = All.Count - 1; i >= 0; i--)
                if (All[i] && All[i].owner == owner && All[i].index == index) { All[i].Pop(false); break; }
            if (relay && owner && owner.abilities) owner.abilities.SendEventPublic(AbilityNetEvent.ClonePop, w => w.Write((byte)index));
        }

        static void Poof(Vector3 pos)
        {
            FX.Ensure().Puff(pos + Vector3.up * 0.9f, Vector3.up, 22, 1f, new Vector2(0.6f, 2.8f), new Vector2(0.9f, 1.8f), new Vector2(0.5f, 1.1f),
                             new Color(1f, 1f, 1f, 0.75f), new Color(0.85f, 0.87f, 0.9f, 0.6f));
            ProcAudio.PlayAt(Sfx.Warp, pos + Vector3.up, 0.9f, 0.15f);
            ProcAudio.PlayAt(Sfx.Fart, pos + Vector3.up, 0.35f, 0.3f); // the "pff" of the smoke
        }

        static Material[] Palette()
        {
            if (palette != null && palette[0]) return palette;
            palette = new[]
            {
                AbilityProps.Mat(new Color(0.78f, 0.6f, 0.48f), 0f, 0.35f), AbilityProps.Mat(new Color(0.24f, 0.28f, 0.22f), 0f, 0.15f),
                AbilityProps.Mat(new Color(0.3f, 0.3f, 0.26f), 0f, 0.15f), AbilityProps.Mat(new Color(0.2f, 0.22f, 0.17f), 0f, 0.2f),
                AbilityProps.Mat(new Color(0.2f, 0.15f, 0.1f), 0f, 0.3f), AbilityProps.Mat(new Color(0.07f, 0.07f, 0.075f), 0f, 0.28f),
                AbilityProps.Mat(new Color(0.24f, 0.27f, 0.2f), 0f, 0.35f), AbilityProps.Mat(new Color(0.14f, 0.14f, 0.13f), 0f, 0.25f),
            };
            return palette;
        }

        static void Spawn(Combatant caster, Vector3 pos, Quaternion rot, Vector3 scatter, int index, string gun)
        {
            if (Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out var gh, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                pos.y = gh.point.y;
            var go = new GameObject("ShadowClone_" + caster.displayName);
            go.SetActive(false);
            go.transform.SetPositionAndRotation(pos, rot);

            var pal = Palette();
            var rig = HumanoidRig.Build(go.transform, new HumanoidRig.Palette
            {
                skin = pal[0], shirt = pal[1], pants = pal[2], vest = pal[3], boots = pal[4], gloves = pal[5], helmet = pal[6], gear = pal[7]
            }, true);
            var anim = go.AddComponent<CharacterAnimator>();
            anim.rig = rig;

            var c = go.AddComponent<Combatant>();
            c.displayName = caster.displayName;   // nobody can tell which one is real
            c.team = caster.team;
            c.playerClass = caster.playerClass;
            c.cloneOf = caster.Root;
            c.isPlayer = false;
            c.human = caster.human;
            c.chestPoint = () => rig.chest.position + rig.chest.up * 0.12f;
            c.headPoint = () => rig.head.TransformPoint(new Vector3(0f, 0.1f, 0f));
            var look = caster.look ?? (caster.isPlayer ? CharacterLook.Local : null);
            if (look != null)
            {
                c.look = look.Clone();
                c.look.Apply(rig);
            }

            var sc = go.AddComponent<ShadowClone>();
            sc.self = c;
            sc.owner = caster;
            sc.rig = rig;
            sc.anim = anim;
            sc.scatterDir = scatter;
            sc.index = index;
            sc.SetupWeapon(string.IsNullOrEmpty(gun) ? CurrentWeapon(caster) : gun);
            go.SetActive(true);
            Poof(pos);
        }

        static string CurrentWeapon(Combatant c)
        {
            var lo = c.GetComponent<Loadout>();
            if (lo && lo.Active) return lo.Active.weaponName;
            var bot = c.GetComponent<CombatBot>();
            if (bot) return bot.weaponName;
            return "M4A1";
        }

        void SetupWeapon(string weaponName)
        {
            var def = WeaponCatalog.Get(weaponName) ?? WeaponCatalog.All[0];
            // a clone is a copy, not the real thing: less accurate (and harmless, see Shoot)
            rpm = def.fullAuto ? Mathf.Min(def.BotRpm, 600f) : Mathf.Min(def.BotRpm, 200f);
            spread = def.BotSpread + 1.6f;
            sfx = def.sfx;
            if (rig && rig.weaponSocket)
            {
                var lo = FindAnyObjectByType<Loadout>();
                var model = lo ? lo.CreateWorldModel(def.name, rig.weaponSocket) : null;
                if (model)
                {
                    for (int i = rig.weaponSocket.childCount - 1; i >= 0; i--)
                    {
                        var ch = rig.weaponSocket.GetChild(i);
                        if (ch.gameObject != model) Destroy(ch.gameObject);
                    }
                    foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == "Muzzle") muzzle = t;
                }
            }
            if (anim) anim.pistolHold = def.category == WeaponCategory.Pistol ? 1f : 0f;
        }

        // ------------------------------------------------------------------ life

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start()
        {
            lastPos = transform.position;
            scatterT = Random.Range(0.6f, 1.1f);
            react = Random.Range(0.25f, 0.5f);
            if (NavMesh.SamplePosition(transform.position, out var nh, 2f, NavMesh.AllAreas))
            {
                agent = gameObject.AddComponent<NavMeshAgent>();
                agent.updateRotation = false;
                agent.speed = 5.2f;
                agent.acceleration = 30f;
                agent.angularSpeed = 0f;
                agent.radius = 0.3f;
                agent.stoppingDistance = 0.3f;
                agent.Warp(nh.position);
            }
            dest = transform.position + scatterDir * 5f;
            Go(dest);
        }

        public DamageResult TakeDamage(DamageInfo info)
        {
            if (popped) return default;
            if (Teams.Friendly(info.attacker, self)) return default;
            Pop();
            return new DamageResult { applied = true, killed = true, dealt = info.amount };
        }

        /// <summary>Vanish in a puff of smoke.</summary>
        public void Pop() => Pop(true);

        /// <summary>Vanish in a puff. <paramref name="sync"/>: popped by a hit here — make it vanish on every machine.</summary>
        public void Pop(bool sync)
        {
            if (popped) return;
            popped = true;
            if (sync && Net.Active && owner)
            {
                if (!owner.remote) { if (owner.abilities) owner.abilities.SendEventPublic(AbilityNetEvent.ClonePop, w => w.Write((byte)index)); }
                else NetGame.SendAbilityEffect(owner, PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null, AbilityNetEffect.ClonePop, w => w.Write((byte)index));
            }
            self.alive = false;
            Poof(transform.position);
            Destroy(gameObject);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || popped) return;
            life += dt;
            if (life >= Life || !owner || !owner.alive || (MatchManager.Instance && MatchManager.Instance.Ended) || TimeRewind.Playing) { Pop(false); return; }

            think -= dt;
            if (think <= 0f) { think = Random.Range(0.12f, 0.2f); Perceive(); Plan(); }
            if (!agent) MoveManual(dt);

            Fight(dt);
            Body(dt);
        }

        void Perceive()
        {
            Combatant best = null;
            float bestD = 55f;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || !self.IsEnemyOf(c) || KnifeDuel.InDuel(c) || TicTacToe.InGame(c)) continue;
                if (c.abilities && c.abilities.Invisible && Time.time - c.lastShotTime > 0.7f) continue;
                float d = Vector3.Distance(c.transform.position, transform.position);
                if (d >= bestD || !Visible(c)) continue;
                bestD = d; best = c;
            }
            if (best != target) react = Random.Range(0.25f, 0.5f);
            target = best;
            targetVisible = best;
        }

        bool Visible(Combatant c)
        {
            Vector3 eye = rig.head.position + Vector3.up * 0.06f;
            Vector3 to = c.Chest - eye;
            float dist = to.magnitude;
            if (dist < 0.01f) return true;
            int n = Physics.RaycastNonAlloc(eye, to / dist, hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Transform first = null;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; first = hits[i].collider.transform; }
            }
            return first == null || first.IsChildOf(c.transform);
        }

        void Plan()
        {
            scatterT -= 0.15f;
            if (scatterT > 0f) return; // the first second: everybody runs off in their own direction
            strafeT -= 0.15f;
            if (target)
            {
                Vector3 to = target.transform.position - transform.position; to.y = 0f;
                float d = to.magnitude;
                if (strafeT <= 0f)
                {
                    strafeT = Random.Range(0.8f, 1.6f);
                    Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * (Random.value < 0.5f ? -1f : 1f);
                    strafeOffset = side * Random.Range(1.5f, 3.5f) + (d > 14f ? to.normalized * 4f : d < 5f ? -to.normalized * 3f : Vector3.zero);
                    Go(transform.position + strafeOffset);
                }
            }
            else if (owner)
            {
                // stay around the original, a few meters apart
                Vector3 o = owner.transform.position - transform.position; o.y = 0f;
                if (o.magnitude > 7f || strafeT <= 0f)
                {
                    strafeT = Random.Range(1.5f, 3f);
                    Vector2 r = Random.insideUnitCircle * 4f;
                    Go(owner.transform.position + new Vector3(r.x, 0f, r.y));
                }
            }
        }

        void Go(Vector3 p)
        {
            dest = p;
            if (agent && agent.isOnNavMesh && NavMesh.SamplePosition(p, out var nh, 3f, NavMesh.AllAreas)) agent.SetDestination(nh.position);
        }

        /// <summary>No NavMesh (shooting range): straight runs with a ground snap, stopping at walls.</summary>
        void MoveManual(float dt)
        {
            Vector3 to = dest - transform.position; to.y = 0f;
            float d = to.magnitude;
            if (d < 0.3f) return;
            Vector3 dir = to / d;
            Vector3 p = transform.position;
            if (Physics.SphereCast(p + Vector3.up * 0.9f, 0.3f, dir, out var h, 0.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !h.collider.transform.IsChildOf(transform) && !h.collider.GetComponentInParent<Combatant>())
            {
                dest = p; // a wall: stop and think again
                return;
            }
            p += dir * Mathf.Min(d, 5f * dt);
            // ground under the feet: skip our own hitboxes and other fighters (else the clone climbs its own body)
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.6f, Vector3.down, hits, 2.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, gy = p.y;
            for (int i = 0; i < n; i++)
            {
                var col = hits[i].collider;
                if (col.transform.IsChildOf(transform) || col.GetComponentInParent<Combatant>()) continue;
                if (hits[i].distance < best) { best = hits[i].distance; gy = hits[i].point.y; }
            }
            if (best < float.MaxValue) p.y = Mathf.Lerp(p.y, gy, 1f - Mathf.Exp(-20f * dt));
            transform.position = p;
        }

        void Fight(float dt)
        {
            fireCd -= dt;
            if (!target || !targetVisible) return;
            react -= dt;
            Vector3 to = target.Chest - transform.position; to.y = 0f;
            if (react > 0f || fireCd > 0f || Vector3.Angle(transform.forward, to) > 18f) return;
            if (burst <= 0) burst = Random.Range(3, 7);
            Shoot();
            burst--;
            fireCd = 60f / rpm + (burst <= 0 ? Random.Range(0.3f, 0.7f) : 0f);
        }

        void Shoot()
        {
            self.lastShotTime = Time.time;
            Vector3 eye = rig.head.position + Vector3.up * 0.06f;
            Vector3 aimAt = Random.value < 0.1f ? target.Head : target.Chest;
            Vector3 dir = (aimAt - eye).normalized;
            Vector2 rnd = Random.insideUnitCircle * spread;
            dir = Quaternion.AngleAxis(rnd.x, Vector3.up) * Quaternion.AngleAxis(rnd.y, Vector3.Cross(dir, Vector3.up)) * dir;
            Vector3 end = eye + dir * 120f;
            int n = Physics.RaycastNonAlloc(eye, dir, hits, 120f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit hit = default; float best = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; hit = hits[i]; any = true; }
            }
            if (any)
            {
                end = hit.point;
                // a clone's bullets are an illusion: no damage to anybody, no blood — only dust where they hit the world
                bool body = hit.collider.GetComponentInParent<HumanoidRig>() || hit.collider.GetComponentInParent<Combatant>();
                if (!body)
                {
                    var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                    FX.Ensure().Impact(hit, tag ? tag.kind : SurfaceKind.Concrete, false);
                }
            }
            Vector3 from = muzzle ? muzzle.position : eye + transform.forward * 0.5f;
            FX.Ensure().Tracer(from, end);
            FX.Ensure().MuzzleFlashWorld(from, dir);
            ProcAudio.PlayAt(sfx, from, 0.8f, 0.06f);
            if (anim) anim.Flinch(new Vector3(0f, 0f, -0.3f), 0.4f);
            AIEvents.Emit(from, 40f, self);
        }

        void Body(float dt)
        {
            Vector3 v = (transform.position - lastPos) / dt;
            lastPos = transform.position;
            float yaw = transform.eulerAngles.y, want = yaw;
            if (target)
            {
                Vector3 to = target.Chest - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) want = Quaternion.LookRotation(to).eulerAngles.y;
            }
            else if (new Vector2(v.x, v.z).sqrMagnitude > 0.3f) want = Quaternion.LookRotation(new Vector3(v.x, 0f, v.z)).eulerAngles.y;
            yaw = Mathf.SmoothDampAngle(yaw, want, ref yawVel, 0.1f, 900f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (!anim) return;
            anim.localVelocity = transform.InverseTransformDirection(v);
            anim.grounded = true;
            anim.holdingWeapon = true;
            anim.aim = Anim.Damp(anim.aim, target ? 1f : 0.3f, 6f, dt);
            float pitch = 0f;
            if (target)
            {
                Vector3 d = target.Chest - rig.head.position;
                pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            }
            anim.aimPitch = Anim.Damp(anim.aimPitch, pitch, 8f, dt);
        }
    }
}
