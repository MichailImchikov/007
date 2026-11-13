using UnityEngine;
using UnityEngine.AI;

namespace FPS
{
    /// <summary>
    /// Deathmatch AI soldier (free-for-all): roams the NavMesh, reacts to sight and gunfire,
    /// engages with reaction time and converging aim error, strafes and crouches, bursts fire,
    /// reloads, retreats to cover when hurt. Uses real raycasts — cover actually blocks its bullets.
    /// Can be executed from behind with the knife while unaware.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class CombatBot : MonoBehaviour, IDamageable, IExecutable, IAbilityPuppet
    {
        public HumanoidRig rig;
        public CharacterAnimator anim;
        public Combatant combatant;
        public NavMeshAgent agent;
        public string weaponName = "M4A1";
        [Range(0, 2)] public int difficulty = 1;
        public float maxHealth = 100f;

        public AbilityUser abilities;

        public bool IsAlive => !dead;
        public Combatant Target => target;
        public bool TargetVisible => targetVisible;
        public Vector3 LastKnown => lastKnown;
        public bool IsEngaging => state == State.Engage;
        public bool IsSearching => state == State.Search;
        public float Health01 => health / maxHealth;
        public NavMeshAgent Agent => agent;
        /// <summary>Forces the body yaw (e.g. turning the butt toward the enemy).</summary>
        public float? YawOverride { get; set; }

        bool external;
        float stunTimer;
        Vector3 extLastPos;

        /// <summary>
        /// Something else moves this bot (grappling hook, ghost mode, swap): the agent is switched off
        /// and on release the bot is snapped back onto the NavMesh.
        /// </summary>
        public bool ExternalControl
        {
            get => external;
            set
            {
                if (external == value) return;
                external = value;
                if (value)
                {
                    if (agent.enabled) agent.enabled = false;
                    extLastPos = transform.position;
                }
                else if (!dead)
                {
                    Vector3 p = transform.position;
                    if (NavMesh.SamplePosition(p, out var nh, 6f, NavMesh.AllAreas)) p = nh.position;
                    agent.enabled = true;
                    agent.Warp(p);
                    agent.isStopped = false;
                }
            }
        }

        public void Stun(float t)
        {
            if (dead) return;
            stunTimer = Mathf.Max(stunTimer, t);
            burstLeft = 0;
        }
        public Vector3 NeckPoint => rig && rig.neckPoint ? rig.neckPoint.position : transform.position + Vector3.up * 1.6f;

        enum State { Roam, Engage, Search, Retreat }

        // weapon profile
        float dmg, rpm, spread, prefDist;
        int magSize, burstMin, burstMax;
        Sfx sfx;

        // difficulty profile
        float reaction, errStart, errMin, headChance, dmgMul;

        float health;
        bool dead, executing;
        State state;
        Combatant target;
        Vector3 lastKnown;
        float lastSeen = -99f, reactTimer, aimError, fireCd, reloadTimer, thinkTimer, strafeTimer, crouchTimer, searchTimer, lastHurt = -99f;
        int ammo, burstLeft;
        bool crouched, targetVisible;
        Transform muzzle;
        float deadAnim, yawVel;
        readonly RaycastHit[] hits = new RaycastHit[12];

        void Awake()
        {
            if (!agent) agent = GetComponent<NavMeshAgent>();
            if (!combatant) combatant = GetComponent<Combatant>();
            combatant.chestPoint = () => rig.chest.position + rig.chest.up * 0.12f;
            combatant.headPoint = () => rig.head.TransformPoint(new Vector3(0f, 0.1f, 0f));
            abilities = GetComponent<AbilityUser>();
            if (!abilities) abilities = gameObject.AddComponent<AbilityUser>();
        }

        void OnEnable()
        {
            Executables.All.Add(this);
            AIEvents.Noise += OnNoise;
        }

        void OnDisable()
        {
            Executables.All.Remove(this);
            AIEvents.Noise -= OnNoise;
        }

        void Start()
        {
            agent.updateRotation = false;
            agent.speed = 4.4f;
            agent.acceleration = 22f;
            agent.angularSpeed = 720f;
            agent.stoppingDistance = 0.4f;
            agent.autoBraking = true;
            SetupWeapon();
            SetupDifficulty();
        }

        void SetupWeapon()
        {
            var def = WeaponCatalog.Get(weaponName) ?? WeaponCatalog.All[0];
            dmg = def.BotDamage; rpm = def.BotRpm; spread = def.BotSpread; prefDist = def.BotPrefDist; magSize = def.mag;
            burstMin = def.BotBurstMin; burstMax = def.BotBurstMax; sfx = def.sfx;
            // bots never miss a trigger pull: cap semi-automatic fire rates
            if (!def.fullAuto) rpm = Mathf.Min(rpm, def.category == WeaponCategory.Marksman ? 140f : 260f);
            ammo = magSize;
            if (rig && rig.weaponSocket)
            {
                var loadout = FindAnyObjectByType<Loadout>();
                if (loadout)
                {
                    var model = loadout.CreateWorldModel(weaponName, rig.weaponSocket);
                    if (model)
                    {
                        for (int i = rig.weaponSocket.childCount - 1; i >= 0; i--)
                        {
                            var c = rig.weaponSocket.GetChild(i);
                            if (c.gameObject != model) Destroy(c.gameObject);
                        }
                        foreach (var t in model.GetComponentsInChildren<Transform>()) if (t.name == "Muzzle") muzzle = t;
                    }
                }
            }
        }

        void SetupDifficulty()
        {
            switch (difficulty)
            {
                case 0: reaction = 0.75f; errStart = 7f; errMin = 2.6f; headChance = 0.05f; dmgMul = 0.65f; break;
                case 2: reaction = 0.3f; errStart = 3.5f; errMin = 0.9f; headChance = 0.22f; dmgMul = 1f; break;
                default: reaction = 0.48f; errStart = 5f; errMin = 1.6f; headChance = 0.12f; dmgMul = 0.82f; break;
            }
        }

        // ------------------------------------------------------------------ lifecycle

        public void Spawn(Vector3 pos, Quaternion rot)
        {
            dead = false;
            executing = false;
            external = false;
            stunTimer = 0f;
            YawOverride = null;
            if (abilities) abilities.OnSpawn();
            health = maxHealth;
            combatant.alive = true;
            target = null;
            state = State.Roam;
            ammo = magSize;
            reloadTimer = 0f;
            deadAnim = 0f;
            transform.rotation = rot;
            if (!agent.enabled) agent.enabled = true;
            agent.Warp(pos);
            agent.isStopped = false;
            if (anim) { anim.dead = 0f; anim.executeVictim = -1f; }
            if (rig) rig.SetCollidersEnabled(true);
        }

        public DamageResult TakeDamage(DamageInfo info)
        {
            if (dead || executing) return default;
            if (abilities && abilities.TryDeflect(info)) return default;
            health -= info.amount;
            lastHurt = Time.time;
            if (DroneOp) DroneOp.OperatorHurt(); // shot while flying: drops the controller
            bool killed = health <= 0f;
            if (info.attacker && info.attacker.isPlayer) DamageNumbers.Spawn(info.point, info.amount, info.critical, killed);
            if (anim) anim.Flinch(transform.InverseTransformDirection(info.direction), Mathf.Clamp(info.amount / 30f, 0.4f, 2f));

            // turn on whoever hurt us
            if (info.attacker && info.attacker != combatant && (!target || !targetVisible))
            {
                target = info.attacker;
                lastKnown = info.attacker.Chest;
                lastSeen = Time.time - 0.3f;
                reactTimer = reaction * 0.6f;
                aimError = errStart;
                if (state == State.Roam) state = State.Search;
            }
            if (killed) Die(info.attacker, info.weapon, info.critical, info.direction);
            return new DamageResult { applied = true, killed = killed, dealt = info.amount };
        }

        void Die(Combatant killer, string weapon, bool headshot, Vector3 dir)
        {
            dead = true;
            health = 0f;
            combatant.alive = false;
            if (abilities) abilities.OnDeath();
            external = false;
            if (agent.enabled) { agent.isStopped = true; agent.enabled = false; }
            if (anim) anim.deadDir = Vector3.Dot(dir, transform.forward) > 0f ? 1f : -1f;
            if (rig) rig.SetCollidersEnabled(false);
            deadAnim = 0f;
            MatchManager.Report(killer, combatant, weapon, headshot);
            if (MatchManager.Instance) MatchManager.Instance.RequestRespawn(this, 4f);
        }

        // ------------------------------------------------------------------ execution (victim side)

        public bool CanBeExecutedBy(Transform attacker)
        {
            if (dead || executing || external) return false;
            Vector3 to = transform.position - attacker.position; to.y = 0f;
            float d = to.magnitude;
            if (d > 1.9f || d < 0.2f) return false;
            if (Vector3.Dot(transform.forward, to / d) < 0.35f) return false;
            // only while unaware of that attacker
            var c = attacker.GetComponent<Combatant>();
            return !(target == c && Time.time - lastSeen < 1.5f);
        }

        public void BeginExecution()
        {
            executing = true;
            if (abilities) abilities.OnDeath();
            if (agent.enabled) agent.isStopped = true;
            if (anim) anim.executeVictim = 0f;
        }

        public void SetExecutionProgress(float t)
        {
            if (anim) anim.executeVictim = t;
            if (t >= 0.82f && anim) { anim.deadDir = 1f; deadAnim = Mathf.Max(deadAnim, 0.01f); }
        }

        public void ExecutionKilled(Combatant killer)
        {
            DamageNumbers.Spawn(NeckPoint, 200f, true, true);
            health = 0f;
            combatant.alive = false;
            MatchManager.Report(killer, combatant, "Нож", true);
        }

        public void EndExecution()
        {
            executing = false;
            dead = true;
            if (anim) anim.executeVictim = -1f;
            if (agent.enabled) agent.enabled = false;
            if (rig) rig.SetCollidersEnabled(false);
            deadAnim = Mathf.Max(deadAnim, 0.3f);
            if (MatchManager.Instance) MatchManager.Instance.RequestRespawn(this, 4f);
        }

        // ------------------------------------------------------------------ hearing

        void OnNoise(Vector3 pos, float radius, Combatant src)
        {
            if (dead || executing || !src || src == combatant || !combatant.IsEnemyOf(src)) return;
            if ((pos - transform.position).sqrMagnitude > radius * radius) return;
            if (target && targetVisible) return;
            target = src;
            lastKnown = pos;
            lastSeen = Time.time - 0.5f;
            if (state == State.Roam) { state = State.Search; searchTimer = 8f; }
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (dead || executing)
            {
                if (dead && !executing)
                {
                    deadAnim = Mathf.Min(1f, deadAnim + dt / 0.6f);
                    if (anim) anim.dead = deadAnim;
                }
                if (anim) { anim.localVelocity = Vector3.zero; }
                return;
            }

            if (external)
            {
                Vector3 ev = (transform.position - extLastPos) / dt;
                extLastPos = transform.position;
                if (anim)
                {
                    anim.localVelocity = transform.InverseTransformDirection(ev);
                    anim.grounded = true;
                    anim.aim = Anim.Damp(anim.aim, 0.3f, 6f, dt);
                }
                return;
            }

            if (Time.time - lastHurt > 4f && health < maxHealth) health = Mathf.Min(maxHealth, health + 12f * dt);

            if (stunTimer > 0f)
            {
                stunTimer -= dt;
                if (agent.enabled) agent.isStopped = true;
                UpdateBody(dt);
                return;
            }

            thinkTimer -= dt;
            if (thinkTimer <= 0f) { thinkTimer = 0.12f + Random.value * 0.06f; Perceive(); }

            if (abilities && abilities.BotOverride(dt))
            {
                UpdateBody(dt);
                return;
            }

            lookAtOverride = null;
            if (DroneMode.Active)
            {
                // flying its own drone: the body crouches still where it launched
                var dop = DroneOp;
                if (dop && dop.BotTick(dt, state == State.Engage && targetVisible))
                {
                    if (agent.enabled) agent.isStopped = true;
                    crouched = true;
                    UpdateBody(dt);
                    return;
                }
                if (AntiDrone(dt)) { UpdateBody(dt); return; }
            }

            switch (state)
            {
                case State.Roam: Roam(); break;
                case State.Search: Search(dt); break;
                case State.Engage: Engage(dt); break;
                case State.Retreat: Retreat(dt); break;
            }

            if (reloadTimer > 0f)
            {
                reloadTimer -= dt;
                if (reloadTimer <= 0f) ammo = magSize;
            }

            UpdateBody(dt);
        }

        bool Visible(Combatant c, out Vector3 aimAt)
        {
            Vector3 eye = rig.head.position + Vector3.up * 0.08f;
            aimAt = c.Chest;
            if (SmokeCloud.All.Count > 0 && SmokeCloud.Blocks(eye, c.Chest) && SmokeCloud.Blocks(eye, c.Head)) return false;
            Vector3 to = aimAt - eye;
            float dist = to.magnitude;
            if (dist > 75f) return false;
            int n = Physics.RaycastNonAlloc(eye, to / dist, hits, dist + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Transform first = null;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < best) { best = h.distance; first = h.collider.transform; }
            }
            if (first == null) return true;
            if (first.IsChildOf(c.transform)) return true;
            // try the head as well (peeking over cover)
            aimAt = c.Head;
            to = aimAt - eye; dist = to.magnitude;
            n = Physics.RaycastNonAlloc(eye, to / dist, hits, dist + 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            best = float.MaxValue; first = null;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < best) { best = h.distance; first = h.collider.transform; }
            }
            return first == null || first.IsChildOf(c.transform);
        }

        void Perceive()
        {
            if (abilities && abilities.Blinded)
            {
                targetVisible = false;
                if (state == State.Engage) { state = State.Search; searchTimer = 3f; }
                return;
            }
            Combatant best = null;
            float bestScore = float.MaxValue;
            Vector3 bestAim = Vector3.zero;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || !combatant.IsEnemyOf(c)) continue;
                Vector3 to = c.Chest - transform.position;
                float d = to.magnitude;
                // invisible: only the floating weapon gives them away up close, or their own gunfire
                if (c.abilities && c.abilities.Invisible && d > 9f && Time.time - c.lastShotTime > 0.7f) continue;
                bool inFov = Vector3.Angle(transform.forward, to) < 80f || d < 5f || c == target;
                if (!inFov) continue;
                if (!Visible(c, out var aimAt)) continue;
                float score = d * (c == target ? 0.6f : 1f) * (c.isPlayer ? 0.9f : 1f);
                if (score < bestScore) { bestScore = score; best = c; bestAim = aimAt; }
            }

            targetVisible = best != null;
            if (best)
            {
                if (best != target || Time.time - lastSeen > 1.5f)
                {
                    reactTimer = reaction + Random.Range(0f, 0.2f);
                    aimError = errStart;
                }
                target = best;
                lastKnown = bestAim;
                lastSeen = Time.time;
                if (state != State.Retreat)
                {
                    if (health < maxHealth * 0.35f && Random.value < 0.6f && TryFindCover(out var cover)) { state = State.Retreat; agent.SetDestination(cover); }
                    else state = State.Engage;
                }
            }
            else if (target && state == State.Engage && Time.time - lastSeen > 0.8f)
            {
                state = State.Search;
                searchTimer = 7f;
            }
            if (target && !target.alive) { target = null; targetVisible = false; if (state != State.Retreat) state = State.Roam; }
        }

        void Roam()
        {
            agent.isStopped = false;
            agent.speed = 4.4f;
            crouched = false;
            if (!agent.pathPending && (!agent.hasPath || agent.remainingDistance < 1.5f))
            {
                Vector3 dest;
                var pickup = AbilitySpawner.Instance ? AbilitySpawner.Instance.Nearest(transform.position, 30f) : null;
                if (pickup && abilities && (abilities.Type == AbilityType.None || Random.value < 0.25f)) dest = pickup.transform.position;
                else if (MatchManager.Instance && Random.value < 0.6f) dest = MatchManager.Instance.RandomSpawnPosition();
                else dest = transform.position + Random.insideUnitSphere * 25f;
                if (NavMesh.SamplePosition(dest, out var nh, 6f, NavMesh.AllAreas)) agent.SetDestination(nh.position);
            }
        }

        void Search(float dt)
        {
            agent.isStopped = false;
            agent.speed = 4.0f;
            crouched = false;
            searchTimer -= dt;
            if (NavMesh.SamplePosition(lastKnown, out var nh, 3f, NavMesh.AllAreas) && (agent.destination - nh.position).sqrMagnitude > 1f)
                agent.SetDestination(nh.position);
            if (searchTimer <= 0f || (!agent.pathPending && agent.remainingDistance < 1f))
            {
                state = State.Roam;
                target = null;
            }
        }

        void Engage(float dt)
        {
            if (!target) { state = State.Roam; return; }
            Vector3 tp = target.Chest;
            Vector3 to = tp - transform.position; to.y = 0f;
            float dist = to.magnitude;

            // sword out: charge in, the ability does the slashing
            if (abilities && abilities.Parrying)
            {
                agent.isStopped = false;
                agent.speed = 5.4f;
                crouched = false;
                if (NavMesh.SamplePosition(target.transform.position, out var ph, 2f, NavMesh.AllAreas)) agent.SetDestination(ph.position);
                return;
            }
            if (abilities && abilities.BlocksShooting) return;

            // movement: close in / back off to preferred range, strafe while shooting
            strafeTimer -= dt;
            if (strafeTimer <= 0f)
            {
                strafeTimer = Random.Range(0.7f, 1.6f);
                Vector3 side = Vector3.Cross(Vector3.up, to.normalized) * (Random.value < 0.5f ? -1f : 1f);
                Vector3 radial = dist > prefDist * 1.3f ? to.normalized * 4f : dist < prefDist * 0.5f ? -to.normalized * 3f : Vector3.zero;
                Vector3 dest = transform.position + side * Random.Range(1.5f, 3.5f) + radial;
                if (NavMesh.SamplePosition(dest, out var nh, 2f, NavMesh.AllAreas)) agent.SetDestination(nh.position);
                crouched = Random.value < 0.22f && dist > 8f;
                if (crouched) agent.SetDestination(transform.position);
            }
            agent.isStopped = false;
            agent.speed = crouched ? 1.6f : 3.2f;

            // aim converges while tracking; target movement makes it harder
            float targetSpeed = target.isPlayer && PlayerHealth.Local ? PlayerHealth.Local.motor.HorizontalSpeed : 3f;
            aimError = Mathf.MoveTowards(aimError, errMin + targetSpeed * 0.18f, dt * 2.2f);

            reactTimer -= dt;
            fireCd -= dt;
            bool fresh = Time.time - lastSeen < 0.25f && targetVisible;
            if (reloadTimer > 0f) return;
            if (ammo <= 0) { reloadTimer = (WeaponCatalog.Get(weaponName)?.category ?? WeaponCategory.Assault) == WeaponCategory.Marksman ? 2.6f : 2.2f; ProcAudio.PlayAt(Sfx.MagOut, transform.position, 0.4f); return; }
            if (fresh && reactTimer <= 0f && fireCd <= 0f && FacingTarget(tp))
            {
                if (burstLeft <= 0) burstLeft = Random.Range(burstMin, burstMax + 1);
                Shoot();
                burstLeft--;
                fireCd = 60f / rpm;
                if (burstLeft <= 0) fireCd += Random.Range(0.25f, 0.65f);
            }
        }

        bool FacingTarget(Vector3 tp)
        {
            Vector3 to = tp - transform.position; to.y = 0f;
            return Vector3.Angle(transform.forward, to) < 18f;
        }

        void Retreat(float dt)
        {
            agent.isStopped = false;
            agent.speed = 5f;
            crouched = false;
            if (!agent.pathPending && agent.remainingDistance < 0.8f)
            {
                crouched = true;
                if (health > maxHealth * 0.7f || Time.time - lastHurt > 6f) state = target ? State.Search : State.Roam;
            }
        }

        bool TryFindCover(out Vector3 cover)
        {
            cover = transform.position;
            if (!target) return false;
            Vector3 threat = target.Head;
            float bestD = float.MaxValue;
            bool found = false;
            for (int i = 0; i < 14; i++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(4f, 12f);
                Vector3 p = transform.position + new Vector3(r.x, 0f, r.y);
                if (!NavMesh.SamplePosition(p, out var nh, 2f, NavMesh.AllAreas)) continue;
                Vector3 eye = nh.position + Vector3.up * 1.2f;
                Vector3 dir = eye - threat;
                float len = dir.magnitude;
                bool blocked = false;
                foreach (var h in Physics.RaycastAll(threat, dir / len, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.transform.IsChildOf(target.transform) || h.collider.transform.IsChildOf(transform)) continue;
                    blocked = true;
                    break;
                }
                if (!blocked) continue;
                float d = (nh.position - transform.position).sqrMagnitude + (Vector3.Dot(nh.position - transform.position, threat - transform.position) > 0f ? 50f : 0f);
                if (d < bestD) { bestD = d; cover = nh.position; found = true; }
            }
            return found;
        }

        // ------------------------------------------------------------------ drone war: shooting drones down

        Drone antiTarget;
        float antiThink, antiReact;
        Vector3? lookAtOverride;
        DroneOperator droneOpCache;
        float droneOpCheck;

        DroneOperator DroneOp
        {
            get
            {
                if (!droneOpCache && Time.time > droneOpCheck) { droneOpCheck = Time.time + 1f; droneOpCache = GetComponent<DroneOperator>(); }
                return droneOpCache;
            }
        }

        /// <summary>An enemy drone in sight (or heard close by) is the first threat: stop, track and shoot it.</summary>
        bool AntiDrone(float dt)
        {
            antiThink -= dt;
            if (antiThink <= 0f)
            {
                antiThink = 0.2f;
                var prev = antiTarget;
                antiTarget = FindDrone();
                if (antiTarget && antiTarget != prev) antiReact = reaction + Random.Range(0.15f, 0.45f);
            }
            if (!antiTarget || antiTarget.Dead) { antiTarget = null; return false; }
            // an enemy soldier close and in sight is still the bigger problem
            if (targetVisible && target && (target.Chest - transform.position).sqrMagnitude < 10f * 10f) return false;

            Vector3 dp = antiTarget.transform.position;
            lookAtOverride = dp;
            if (agent.enabled) agent.isStopped = true;
            crouched = false;
            if (reloadTimer > 0f)
            {
                reloadTimer -= dt;
                if (reloadTimer <= 0f) ammo = magSize;
                return true;
            }
            if (ammo <= 0) { reloadTimer = 2.2f; ProcAudio.PlayAt(Sfx.MagOut, transform.position, 0.4f); return true; }
            antiReact -= dt;
            fireCd -= dt;
            Vector3 to = dp - transform.position; to.y = 0f;
            if (antiReact > 0f || fireCd > 0f || Vector3.Angle(transform.forward, to) > 14f) return true;
            // small, fast target: the error grows with its speed and distance
            float err = errMin * 1.4f + antiTarget.Velocity.magnitude * 0.16f + Vector3.Distance(dp, transform.position) * 0.02f;
            Shoot(dp + antiTarget.Velocity * 0.05f, err - aimError);
            fireCd = 60f / rpm + Random.Range(0.05f, 0.2f);
            return true;
        }

        Drone FindDrone()
        {
            Drone best = null;
            float bestD = 45f;
            Vector3 eye = rig.head.position + Vector3.up * 0.08f;
            foreach (var d in Drone.All)
            {
                if (!d || d.Dead || !d.Owner || !combatant.IsEnemyOf(d.Owner)) continue;
                Vector3 to = d.transform.position - eye;
                float dist = to.magnitude;
                if (dist > bestD) continue;
                // heard within ~22 m wherever it is, seen in a wide cone farther out
                if (dist > 22f && Vector3.Angle(transform.forward, to) > 100f) continue;
                int n = Physics.RaycastNonAlloc(eye, to / dist, hits, dist + 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                float first = float.MaxValue; Transform ft = null;
                for (int i = 0; i < n; i++)
                {
                    if (hits[i].collider.transform.IsChildOf(transform)) continue;
                    if (hits[i].distance < first) { first = hits[i].distance; ft = hits[i].collider.transform; }
                }
                if (ft && !ft.IsChildOf(d.transform)) continue;
                bestD = dist; best = d;
            }
            return best;
        }

        void Shoot(Vector3? forcedAim = null, float extraErr = 0f)
        {
            ammo--;
            combatant.lastShotTime = Time.time;
            Vector3 eye = rig.head.position + Vector3.up * 0.06f;
            float hc = target ? headChance + (target.abilities && target.abilities.BigHead ? 0.45f : 0f) : 0f;
            Vector3 aimAt = forcedAim ?? (Random.value < hc ? target.Head : target.Chest);
            Vector3 dir = (aimAt - eye).normalized;
            float err = Mathf.Max(0.2f, aimError + extraErr) + spread + (agent.enabled && agent.velocity.magnitude > 0.5f ? 0.8f : 0f) + (abilities ? abilities.AimPenalty : 0f);
            Vector2 rnd = Random.insideUnitCircle * err;
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
                var hb = hit.collider.GetComponent<Hitbox>();
                IDamageable d = hb ? hb.Owner : hit.collider.GetComponent<IDamageable>();
                if (d != null)
                {
                    float mult = hb ? hb.multiplier : 1f;
                    bool crit = hb && hb.critical;
                    if (d is PlayerHealth ph)
                    {
                        crit = hit.point.y > ph.transform.position.y + ph.motor.EyeHeight - 0.2f;
                        mult = crit ? 1.6f : 1f;
                    }
                    float falloff = Mathf.Lerp(1f, 0.6f, Mathf.InverseLerp(prefDist * 1.5f, prefDist * 4f, hit.distance));
                    d.TakeDamage(new DamageInfo
                    {
                        amount = Mathf.Round(dmg * mult * falloff * dmgMul), point = hit.point, normal = hit.normal, direction = dir,
                        critical = crit, attacker = combatant, weapon = weaponName
                    });
                }
                if (hit.collider.GetComponentInParent<HumanoidRig>()) FX.Ensure().Blood(hit.point, hit.normal, 6);
                else if (!(d is PlayerHealth) && !(d is CombatBot))
                {
                    var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                    FX.Ensure().Impact(hit, tag ? tag.kind : SurfaceKind.Concrete, hit.rigidbody == null && d == null);
                }
                if (hit.rigidbody && !hit.rigidbody.isKinematic) hit.rigidbody.AddForceAtPosition(dir * 3f, hit.point, ForceMode.Impulse);
            }

            // near-miss crack for the player
            if (PlayerHealth.Local && target && target.isPlayer && !forcedAim.HasValue)
            {
                Vector3 cam = PlayerHealth.Local.playerCam.cam.transform.position;
                Vector3 closest = eye + dir * Mathf.Clamp(Vector3.Dot(cam - eye, dir), 0f, Vector3.Distance(eye, end));
                if (Vector3.Distance(closest, cam) < 1.5f && (!any || !(hit.collider.GetComponent<PlayerHealth>())))
                    ProcAudio.PlayAt(Sfx.Whiz, closest, 0.7f, 0.15f);
            }

            Vector3 from = muzzle ? muzzle.position : eye + transform.forward * 0.5f;
            FX.Ensure().Tracer(from, end);
            FX.Ensure().MuzzleFlashWorld(from, dir);
            NetGame.ShotEnds.Clear();
            NetGame.ShotEnds.Add((end, !any ? (byte)2 : hit.collider.GetComponentInParent<HumanoidRig>() ? (byte)1 : (byte)0));
            NetGame.SendShot(combatant, eye, NetGame.ShotEnds);
            ProcAudio.PlayAt(sfx, from, 0.85f, 0.06f);
            if (anim) anim.Flinch(new Vector3(0f, 0f, -0.3f), 0.4f);
            AIEvents.Emit(from, 40f, combatant);
        }

        void UpdateBody(float dt)
        {
            Vector3 v = agent.enabled ? agent.velocity : Vector3.zero;
            float yaw = transform.eulerAngles.y;
            float want = yaw;
            if ((state == State.Engage || (state == State.Search && Time.time - lastSeen < 2f)) && target)
            {
                Vector3 to = (state == State.Engage ? target.Chest : lastKnown) - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) want = Quaternion.LookRotation(to).eulerAngles.y;
            }
            else if (v.sqrMagnitude > 0.2f) want = Quaternion.LookRotation(new Vector3(v.x, 0f, v.z)).eulerAngles.y;
            if (lookAtOverride.HasValue)
            {
                Vector3 to = lookAtOverride.Value - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) want = Quaternion.LookRotation(to).eulerAngles.y;
            }
            if (YawOverride.HasValue) want = YawOverride.Value;
            extLastPos = transform.position;
            yaw = Mathf.SmoothDampAngle(yaw, want, ref yawVel, 0.12f, 900f);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (!anim) return;
            anim.localVelocity = transform.InverseTransformDirection(v);
            anim.grounded = true;
            anim.crouch = Anim.Damp(anim.crouch, crouched ? 1f : 0f, 8f, dt);
            anim.aim = Anim.Damp(anim.aim, state == State.Engage || lookAtOverride.HasValue ? 1f : 0.25f, 6f, dt);
            float pitch = 0f;
            if (lookAtOverride.HasValue)
            {
                Vector3 d = lookAtOverride.Value - rig.head.position;
                pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            }
            else if (state == State.Engage && target)
            {
                Vector3 d = target.Chest - rig.head.position;
                pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            }
            anim.aimPitch = Anim.Damp(anim.aimPitch, pitch, 8f, dt);
            anim.lookYaw = Anim.Damp(anim.lookYaw, 0f, 4f, dt);
        }
    }
}
