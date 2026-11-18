using UnityEngine;

namespace FPS
{
    /// <summary>
    /// "Лазерный взгляд": the caster rises into the air, hangs there and burns everything it looks at with twin red
    /// beams from the eyes. While it lasts incoming damage is cut to a quarter. The player steers the beams with the
    /// view and drifts with WASD; a bot tracks its nearest visible enemy (with a little lag). Then a soft landing.
    /// Online: the flight travels in the snapshots, the beams are drawn elsewhere from the CharState.Laser flag and
    /// the look direction (RemoteLaser); damage is dealt by the caster's machine in 0.1 s chunks (proxies forward it).
    /// </summary>
    public partial class AbilityUser
    {
        public const float LaserDamageScale = 0.25f;
        const float LaserHeight = 5.5f, LaserRise = 0.8f, LaserLand = 0.6f, LaserDps = 85f, LaserRange = 80f;

        float laserT, laserDur = 1f, laserLandT = -1f, laserTick, laserFxT;
        Vector3 laserGround, laserAim;
        LineRenderer[] laserBeams;
        LineRenderer laserBounce;
        bool bounced;
        Vector3 bounceFrom, bounceTo;
        float parryBeamFx;
        Light laserGlow;
        AudioSource laserHum;
        Combatant laserTarget;
        static Material laserMat;
        static AudioClip humClip;

        public bool Lasering => laserT > 0f || laserLandT >= 0f;
        /// <summary>The beams are firing right now (replicated as CharState.Laser).</summary>
        public bool LaserBeamOn { get; private set; }
        float beamDmgT, bounceDmgT;
        /// <summary>Damage hook: what share of incoming damage still lands.</summary>
        public float IncomingDamageScale => Lasering ? LaserDamageScale : 1f;

        bool StartLaser(float dur)
        {
            if (!isPlayer && !bot) return false;
            laserT = laserDur = dur;
            laserLandT = -1f;
            laserGround = transform.position;
            laserTick = 0f;
            laserAim = transform.position + transform.forward * 10f + Vector3.up * 1.5f;
            if (isPlayer)
            {
                if (motor.IsPulled) motor.StopPull();
                motor.Locked = true;
                Say("Лазерный взгляд", "Смотрите на врагов — они горят. WASD — парить. Урон по вам ×0.25", AbilityDB.Get(AbilityType.LaserEyes).color);
            }
            else bot.ExternalControl = true;
            EnsureLaserFx();
            FX.Ensure().DustBurst(transform.position, Vector3.up, 14, new Color(0.6f, 0.57f, 0.52f, 0.6f), new Color(0.5f, 0.47f, 0.43f, 0.45f), 1.2f);
            ProcAudio.PlayAt(Sfx.RocketLaunch, transform.position + Vector3.up, 0.7f, 0.1f);
            return true;
        }

        void EnsureLaserFx()
        {
            if (!laserMat)
            {
                laserMat = FX.MakeParticleMat("LaserBeam", Texture2D.whiteTexture, true, new Color(3f, 0.25f, 0.12f, 1f), true);
                humClip = MakeHum();
            }
            if (laserBeams == null)
            {
                laserBeams = new LineRenderer[2];
                for (int i = 0; i < 2; i++)
                {
                    var go = new GameObject("LaserBeam" + i);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.sharedMaterial = laserMat;
                    lr.positionCount = 2;
                    lr.useWorldSpace = true;
                    lr.numCapVertices = 4;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    laserBeams[i] = lr;
                }
                var bgo = new GameObject("LaserBounce");
                laserBounce = bgo.AddComponent<LineRenderer>();
                laserBounce.sharedMaterial = laserMat;
                laserBounce.positionCount = 2;
                laserBounce.useWorldSpace = true;
                laserBounce.numCapVertices = 4;
                laserBounce.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                laserBounce.enabled = false;
                var lg = new GameObject("LaserGlow");
                laserGlow = lg.AddComponent<Light>();
                laserGlow.type = LightType.Point;
                laserGlow.color = new Color(1f, 0.15f, 0.08f);
                laserGlow.range = 6f;
                laserGlow.intensity = 4f;
                laserGlow.shadows = LightShadows.None;
                laserHum = lg.AddComponent<AudioSource>();
                laserHum.clip = humClip;
                laserHum.loop = true;
                laserHum.spatialBlend = isPlayer ? 0f : 1f;
                laserHum.minDistance = 3f;
                laserHum.maxDistance = 45f;
                laserHum.volume = isPlayer ? 0.35f : 0.8f;
            }
            foreach (var b in laserBeams) b.gameObject.SetActive(true);
            laserGlow.gameObject.SetActive(true);
            laserHum.Play();
        }

        /// <summary>A searing electric hum: detuned saws with a fast flutter.</summary>
        static AudioClip MakeHum()
        {
            const int SR = 44100;
            int n = SR; // 1 s loop
            var d = new float[n];
            var rnd = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float saw = (t * 110f % 1f) * 2f - 1f, saw2 = (t * 165f % 1f) * 2f - 1f;
                lp += ((float)(rnd.NextDouble() * 2.0 - 1.0) - lp) * 0.4f;
                float flutter = 0.75f + 0.25f * Mathf.Sin(t * 2f * Mathf.PI * 30f);
                d[i] = (saw * 0.22f + saw2 * 0.12f + lp * 0.2f) * flutter;
            }
            var c = AudioClip.Create("LaserHum", n, 1, SR, false);
            c.SetData(d, 0);
            return c;
        }

        void EndLaserFx()
        {
            LaserBeamOn = false;
            if (laserBeams != null) foreach (var b in laserBeams) if (b) b.gameObject.SetActive(false);
            if (laserBounce) laserBounce.enabled = false;
            if (laserGlow) laserGlow.gameObject.SetActive(false);
            if (laserHum) laserHum.Stop();
        }

        /// <summary>Stops everything at once (death, respawn, status reset).</summary>
        void EndLaser()
        {
            if (!Lasering) return;
            laserT = 0f;
            laserLandT = -1f;
            EndLaserFx();
            if (isPlayer) motor.Locked = false;
            else if (bot && bot.ExternalControl) bot.ExternalControl = false;
        }

        void UpdateLaser(float dt)
        {
            if (!Lasering) return;
            if (!self.alive) { EndLaser(); return; }

            // ---- landing: back down to the ground we left from, then the normal controller takes over
            if (laserLandT >= 0f)
            {
                laserLandT += dt / LaserLand;
                Vector3 p = transform.position;
                float gy = GroundY(p, transform, laserGround.y);
                p.y = Mathf.Lerp(p.y, gy, Anim.Smooth(laserLandT));
                transform.position = p;
                if (laserLandT >= 1f)
                {
                    laserLandT = -1f;
                    if (isPlayer) motor.Locked = false;
                    else if (bot) { bot.ExternalControl = false; bot.Stun(0.3f); }
                    ProcAudio.PlayAt(Sfx.Land, transform.position, 1f, 0.05f);
                    FX.Ensure().DustBurst(transform.position, Vector3.up, 10, new Color(0.6f, 0.57f, 0.52f, 0.5f), new Color(0.5f, 0.47f, 0.43f, 0.4f), 0.9f);
                }
                return;
            }

            float elapsed = laserDur - laserT;
            laserT -= dt;

            // ---- flight: rise, then hover with a slow bob; the player drifts with WASD
            float rise = Anim.Smooth(elapsed / LaserRise);
            Vector3 pos = transform.position;
            float wantY = laserGround.y + LaserHeight * rise + Mathf.Sin(elapsed * 1.7f) * 0.15f * rise;
            if (Physics.SphereCast(pos + Vector3.up * 1.6f, 0.3f, Vector3.up, out var ceil, Mathf.Max(0f, wantY - pos.y) + 0.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && !ceil.collider.transform.IsChildOf(transform))
                wantY = Mathf.Min(wantY, pos.y + Mathf.Max(0f, ceil.distance - 0.1f)); // a roof stops the climb
            pos.y = Mathf.MoveTowards(pos.y, wantY, 12f * dt);
            if (isPlayer && rise > 0.5f)
            {
                Vector2 mv = GameInput.Enabled ? GameInput.Move : Vector2.zero;
                Vector3 f = Cam.forward; f.y = 0f; f.Normalize();
                Vector3 r = Cam.right; r.y = 0f; r.Normalize();
                Vector3 drift = (f * mv.y + r * mv.x) * 3.5f * dt;
                if (drift.sqrMagnitude > 1e-6f && !Physics.SphereCast(pos + Vector3.up * 0.9f, 0.35f, drift.normalized, out _, drift.magnitude + 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    pos += drift;
            }
            transform.position = pos;
            laserGround = new Vector3(pos.x, laserGround.y, pos.z);

            // ---- the beams
            Vector3 eye, dir;
            if (isPlayer)
            {
                eye = Cam.position;
                dir = Cam.forward;
            }
            else
            {
                eye = rig ? rig.head.position + transform.forward * 0.1f : transform.position + Vector3.up * 1.7f;
                laserTick -= dt;
                if (laserTick <= 0f) { laserTick = 0.25f; laserTarget = LaserPickTarget(eye); }
                Vector3 want = laserTarget ? laserTarget.Chest + Random.insideUnitSphere * 0.25f : eye + transform.forward * 10f - Vector3.up * 2f;
                laserAim = Vector3.Lerp(laserAim, want, 1f - Mathf.Exp(-(bot.difficulty == 2 ? 7f : bot.difficulty == 0 ? 2.5f : 4.5f) * dt));
                dir = (laserAim - eye).normalized;
                Vector3 flat = dir; flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 1f - Mathf.Exp(-8f * dt));
                if (anim) { anim.aimPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg; anim.holdingWeapon = false; anim.grounded = false; }
            }
            bool active = rise > 0.6f && laserT > 0f;
            LaserBeamOn = active;
            Vector3 end = eye + dir * LaserRange;
            bounced = false;
            if (active && RaycastAim(eye, dir, LaserRange, out RaycastHit hit))
            {
                end = hit.point;
                var victim = CombatantOf(hit.collider);
                if (victim && victim.abilities && victim.abilities.ReflectsBeam(dir, out Vector3 blade, out Vector3 rdir))
                {
                    // a parrying sword: the beam stops on the blade and bounces back toward where it came from
                    end = blade;
                    bounced = true;
                    bounceFrom = blade;
                    bounceTo = blade + rdir * LaserRange;
                    if (RaycastIgnoring(blade, rdir, LaserRange, victim.transform, out RaycastHit rh))
                    {
                        bounceTo = rh.point;
                        BeamBurn(rh, rdir, victim, dt, "Рикошет", ref bounceDmgT);
                    }
                }
                else BeamBurn(hit, dir, self, dt, "Лазерный взгляд", ref beamDmgT);
            }

            // eyes: from the player's own view the beams come in from just under the screen edges
            Vector3 up = isPlayer ? Cam.up : Vector3.up;
            Vector3 right = isPlayer ? Cam.right : transform.right;
            Vector3 c0 = isPlayer ? eye + dir * 0.25f - up * 0.11f : eye;
            float spread = isPlayer ? 0.07f : 0.035f;
            float flick = 0.7f + 0.3f * Mathf.PerlinNoise(Time.time * 30f, 0.5f);
            for (int i = 0; i < 2; i++)
            {
                var lr = laserBeams[i];
                lr.enabled = active;
                if (!active) continue;
                Vector3 s = c0 + right * (i == 0 ? -spread : spread);
                lr.SetPosition(0, s);
                lr.SetPosition(1, end + right * (i == 0 ? -0.03f : 0.03f));
                // thin and even: a wide end turns into a floodlight when it passes the camera
                float w = (isPlayer ? 0.03f : 0.045f) * flick;
                lr.startWidth = isPlayer ? 0.008f : w;
                lr.endWidth = w;
            }
            if (laserBounce)
            {
                laserBounce.enabled = active && bounced;
                if (laserBounce.enabled)
                {
                    laserBounce.SetPosition(0, bounceFrom);
                    laserBounce.SetPosition(1, bounceTo);
                    laserBounce.startWidth = laserBounce.endWidth = 0.05f * flick;
                }
            }
            if (laserGlow)
            {
                laserGlow.intensity = active ? 4f * flick : 0f; // the burning spot glows
                laserGlow.transform.position = active ? end - dir * 0.3f : eye;
            }
            if (laserHum) laserHum.transform.position = eye;

            if (laserT <= 0f)
            {
                EndLaserFx();
                laserLandT = 0f; // float down
            }
        }

        /// <summary>One frame of beam on a surface: damage (attributed to <paramref name="by"/>) and burn effects.</summary>
        void BeamBurn(RaycastHit hit, Vector3 dir, Combatant by, float dt, string weapon, ref float dmgT)
        {
            var hb = hit.collider.GetComponent<Hitbox>();
            IDamageable d = hb ? hb.Owner : hit.collider.GetComponent<IDamageable>();
            // damage in 0.1 s chunks: same damage per second, ten network messages a second instead of one per frame
            dmgT += dt;
            if (d != null && dmgT >= 0.1f)
            {
                // a bot's beam is gentler: it never leaves its target
                float amount = LaserDps * dmgT * (isPlayer ? 1f : 0.5f);
                dmgT = 0f;
                var res = d.TakeDamage(new DamageInfo { amount = amount, point = hit.point, normal = hit.normal, direction = dir, attacker = by, weapon = weapon });
                bool mine = by && by == (PlayerHealth.Local ? PlayerHealth.Local.GetComponent<Combatant>() : null);
                if (res.applied && mine && HUD.Instance && res.killed) { HUD.Instance.ShowHitmarker(true, false); HUD.Instance.RegisterHit(true); ProcAudio.Play(Sfx.Kill, 0.5f); }
            }
            laserFxT -= dt;
            if (laserFxT <= 0f)
            {
                laserFxT = 0.06f;
                FX.Ensure().SparkBurst(hit.point, hit.normal, 6, 6f);
                if (hit.collider.GetComponentInParent<HumanoidRig>()) FX.Ensure().Blood(hit.point, hit.normal, 2);
                else FX.Ensure().Puff(hit.point, hit.normal, 2, 0.6f, new Vector2(0.3f, 1f), new Vector2(0.2f, 0.4f), new Vector2(0.4f, 0.8f),
                                      new Color(0.15f, 0.15f, 0.15f, 0.5f), new Color(0.3f, 0.3f, 0.3f, 0.4f));
            }
        }

        bool RaycastIgnoring(Vector3 o, Vector3 d, float range, Transform ignore, out RaycastHit hit)
        {
            hit = default;
            int n = Physics.RaycastNonAlloc(o, d, hits, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(ignore) || hits[i].distance <= 0f) continue;
                if (hits[i].distance < best) { best = hits[i].distance; hit = hits[i]; any = true; }
            }
            return any;
        }

        /// <summary>
        /// Defender side: an enemy laser hits this combatant while the parry sword is out and facing it. The beam
        /// stops on the blade and goes back the way it came, swaying (so now and then it burns its own caster).
        /// </summary>
        public bool ReflectsBeam(Vector3 dir, out Vector3 blade, out Vector3 outDir)
        {
            blade = default; outDir = -dir;
            if (!Parrying) return false;
            Vector3 inc = -dir; inc.y = 0f;
            if (inc.sqrMagnitude < 0.001f || Vector3.Dot(Facing, inc.normalized) < 0.25f) return false;
            blade = isPlayer ? (swordVM ? swordVM.BladePoint : Cam.position + Cam.forward * 0.6f)
                             : (botSword ? botSword.TransformPoint(new Vector3(0f, 0.6f, 0f)) : AimOrigin + transform.forward * 0.6f);
            float t = Time.time;
            Vector3 side = Vector3.Cross(Vector3.up, -dir).normalized;
            outDir = Quaternion.AngleAxis(Mathf.Sin(t * 2.3f) * 11f + Mathf.Sin(t * 7.1f) * 3f, Vector3.up)
                     * Quaternion.AngleAxis(Mathf.Sin(t * 3.1f) * 5f, side) * -dir;
            parryBeamFx -= Time.deltaTime;
            if (parryBeamFx <= 0f)
            {
                parryBeamFx = 0.22f;
                PlayParryAnim();
                FX.Ensure().SparkBurst(blade, -dir, 12, 9f);
                ProcAudio.PlayAt(Sfx.Parry, blade, 0.6f, 0.15f);
                if (isPlayer) pcam.Punch(new Vector3(-1.5f, Random.Range(-2f, 2f), Random.Range(-3f, 3f)));
            }
            return true;
        }

        bool remoteLaserWas;

        /// <summary>Mirror of a combatant on another machine: draw its beams from the head along its look (visual only).</summary>
        void RemoteLaser(float dt, bool on)
        {
            if (!on)
            {
                if (remoteLaserWas) { remoteLaserWas = false; EndLaserFx(); }
                return;
            }
            if (!remoteLaserWas) { remoteLaserWas = true; EnsureLaserFx(); }
            float pitch = anim ? anim.aimPitch : 0f;
            Vector3 dir = Quaternion.Euler(pitch, transform.eulerAngles.y, 0f) * Vector3.forward;
            Vector3 eye = rig ? rig.head.position + dir * 0.1f : transform.position + Vector3.up * 1.7f;
            Vector3 end = eye + dir * LaserRange;
            bounced = false;
            if (RaycastAim(eye, dir, LaserRange, out RaycastHit hit))
            {
                end = hit.point;
                var victim = CombatantOf(hit.collider);
                if (victim && victim.abilities && victim.abilities.ReflectsBeam(dir, out Vector3 blade, out Vector3 rdir))
                {
                    end = blade;
                    bounced = true;
                    bounceFrom = blade;
                    bounceTo = RaycastIgnoring(blade, rdir, LaserRange, victim.transform, out RaycastHit rh) ? rh.point : blade + rdir * LaserRange;
                }
                laserFxT -= dt;
                if (laserFxT <= 0f) { laserFxT = 0.08f; FX.Ensure().SparkBurst(hit.point, hit.normal, 5, 6f); }
            }
            float flick = 0.7f + 0.3f * Mathf.PerlinNoise(Time.time * 30f, 0.5f);
            Vector3 right = transform.right;
            for (int i = 0; i < 2; i++)
            {
                var lr = laserBeams[i];
                lr.enabled = true;
                lr.SetPosition(0, eye + right * (i == 0 ? -0.035f : 0.035f));
                lr.SetPosition(1, end + right * (i == 0 ? -0.03f : 0.03f));
                lr.startWidth = lr.endWidth = 0.045f * flick;
            }
            if (laserBounce)
            {
                laserBounce.enabled = bounced;
                if (bounced) { laserBounce.SetPosition(0, bounceFrom); laserBounce.SetPosition(1, bounceTo); laserBounce.startWidth = laserBounce.endWidth = 0.05f * flick; }
            }
            if (laserGlow) { laserGlow.intensity = 4f * flick; laserGlow.transform.position = end - dir * 0.3f; }
            if (laserHum) laserHum.transform.position = eye;
        }

        Combatant LaserPickTarget(Vector3 eye)
        {
            Combatant best = null;
            float bestD = LaserRange;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || !self.IsEnemyOf(c) || KnifeDuel.InDuel(c) || TicTacToe.InGame(c)) continue;
                Vector3 to = c.Chest - eye;
                float d = to.magnitude;
                if (d >= bestD) continue;
                if (RaycastAim(eye, to / d, d, out RaycastHit h) && !h.collider.transform.IsChildOf(c.transform)) continue;
                bestD = d; best = c;
            }
            return best;
        }
    }
}
