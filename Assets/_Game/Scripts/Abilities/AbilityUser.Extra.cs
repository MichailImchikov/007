using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

namespace FPS
{
    /// <summary>
    /// Second batch of abilities: shrink, Fortnite-style building, and area effects on nearby enemies
    /// (big heads, screamer, snowman, confusion, launch, minesweeper, blue screen of death).
    /// </summary>
    public partial class AbilityUser
    {
        // ------------------------------------------------------------------ status
        public bool Shrunk => shrinkT > 0f;
        public bool BigHead => bigHeadT > 0f;
        public bool IsSnowman => snowmanT > 0f;
        public bool Confused => confuseT > 0f;
        public bool Building => building;
        public bool Minesweeping => mineActive || mineBotT > 0f;
        public float ScreamT => isPlayer ? screamT : 0f;
        public float BSODLeft => isPlayer ? bsodT : 0f;
        public float BSODDuration => bsodDur;
        public int BuildLeft => buildLeft;
        /// <summary>Extra aim error in degrees for bots (confusion).</summary>
        public float AimPenalty => Confused ? 9f : 0f;
        bool ExtraBlocksShooting => Minesweeping || bsodT > 0f || launched || building || screamT > 0f;

        const float ShrinkScale = 0.4f, BigHeadScale = 3.2f;

        float shrinkT, shrinkDur = 1f, bodyScale = 1f;
        bool shrinkApplied;
        float baseStand, baseCrouch, baseProne, baseEyeStand, baseEyeCrouch, baseEyeProne, baseRadius;
        float bigHeadT, headScale = 1f;
        float screamT;
        float snowmanT, snowmanDur = 1f;
        SnowmanModel snowman;
        float confuseT, confuseDur = 1f;
        bool launched;
        float launchT;
        Vector3 launchVel;
        bool mineActive;
        float mineBotT;
        bool mineBotBoom;
        Combatant mineCaster;
        bool building;
        float buildT, buildDur = 1f;
        int buildLeft, buildRot;
        Transform ghost;
        static Material ghostOk, ghostBad;
        float bsodT, bsodDur = 1f;

        float ExtraFraction(out Color c)
        {
            c = Color.white;
            if (building) { c = AbilityDB.Get(AbilityType.Build).color; return buildT / buildDur; }
            if (IsSnowman) { c = AbilityDB.Get(AbilityType.Snowman).color; return snowmanT / snowmanDur; }
            if (Shrunk) { c = AbilityDB.Get(AbilityType.Shrink).color; return shrinkT / shrinkDur; }
            if (Confused) { c = AbilityDB.Get(AbilityType.Confuse).color; return confuseT / confuseDur; }
            return -1f;
        }

        bool ExecuteExtra(AbilityType t, AbilityInfo info)
        {
            switch (t)
            {
                case AbilityType.Shrink: StartShrink(info.duration); return true;
                case AbilityType.Build: return StartBuild(info.duration);
                case AbilityType.BigHeads:
                case AbilityType.Screamer:
                case AbilityType.Snowman:
                case AbilityType.Confuse:
                case AbilityType.Launch:
                case AbilityType.Minesweeper:
                case AbilityType.BSOD:
                    return AreaBlast(t, info);
            }
            return false;
        }

        // ------------------------------------------------------------------ area abilities

        bool AreaBlast(AbilityType t, AbilityInfo info)
        {
            Vector3 c = transform.position + Vector3.up;
            int n = 0;
            foreach (var other in Combatant.All)
            {
                if (!other || !other.alive || other == self || !self.IsEnemyOf(other) || !other.abilities) continue;
                if (Vector3.Distance(other.transform.position, transform.position) > info.radius) continue;
                var o = other;
                EffectOn(other, AbilityNetEffect.Area, w => w.Write((byte)t), () => o.abilities.ReceiveArea(t, self, info));
                n++;
            }
            if (n == 0 && isPlayer)
            {
                Say("Никого в радиусе", $"Подойдите ближе к врагам ({info.radius:0} м)", new Color(1f, 0.4f, 0.4f));
                return false;
            }
            Shockwave.Spawn(transform.position + Vector3.up * 0.1f, info.radius, info.color);
            ProcAudio.PlayAt(Sfx.Warp, c, 0.8f, 0.1f);
            SendEvent(AbilityNetEvent.Area, w => w.Write((byte)t));
            if (isPlayer) Say(info.name, n == 1 ? "Задет 1 враг" : $"Задето врагов: {n}", info.color);
            return true;
        }

        /// <summary>An enemy's area ability hit this combatant.</summary>
        public void ReceiveArea(AbilityType t, Combatant caster, AbilityInfo info)
        {
            if (!self.alive) return;
            Color col = info.color;
            switch (t)
            {
                case AbilityType.BigHeads:
                    bigHeadT = info.duration;
                    ProcAudio.PlayAt(Sfx.GrowUp, transform.position + Vector3.up * 1.7f, 0.8f, 0.1f);
                    if (isPlayer) Say("Голова раздулась!", "В неё теперь очень легко попасть", col);
                    break;
                case AbilityType.Screamer:
                    if (isPlayer)
                    {
                        screamT = 1.7f;
                        ProcAudio.Play(Sfx.Scream, 1f, 0.03f);
                        ProcAudio.Play(Sfx.Hurt, 0.8f);
                        pcam.Shake(0.8f);
                        pcam.Punch(new Vector3(-20f, Random.Range(-10f, 10f), Random.Range(-15f, 15f)));
                    }
                    else
                    {
                        screamT = 1.4f;
                        if (puppet != null) puppet.Stun(1.4f);
                        ProcAudio.PlayAt(Sfx.Scream, transform.position + Vector3.up * 1.6f, 0.5f, 0.1f);
                    }
                    break;
                case AbilityType.Snowman:
                    StartSnowman(info.duration);
                    break;
                case AbilityType.Confuse:
                    confuseT = confuseDur = info.duration;
                    ProcAudio.PlayAt(Sfx.Warp, transform.position + Vector3.up, 0.5f, 0.2f);
                    if (isPlayer) Say("Путаница!", "Всё управление задом наперёд", col);
                    break;
                case AbilityType.Launch:
                {
                    if (Phasing) break;
                    Vector2 r = Random.insideUnitCircle * 2.5f;
                    Vector3 v = new Vector3(r.x, Random.Range(16f, 19f), r.y);
                    ProcAudio.PlayAt(Sfx.Boing, transform.position, 1f, 0.08f);
                    if (isPlayer)
                    {
                        if (motor.IsPulled) motor.StopPull();
                        motor.AddImpulse(v);
                        ProcAudio.Play(Sfx.Boing, 0.7f);
                    }
                    else if (puppet != null && !puppet.ExternalControl)
                    {
                        launched = true;
                        launchT = 0f;
                        launchVel = v;
                        puppet.ExternalControl = true;
                    }
                    break;
                }
                case AbilityType.Minesweeper:
                    mineCaster = caster;
                    if (isPlayer)
                    {
                        if (mineActive) break;
                        mineActive = true;
                        GameInput.ModalUI = true;
                        AbilityHUD.Ensure().OpenMinesweeper(this);
                        ProcAudio.Play(Sfx.ErrorTone, 0.6f);
                    }
                    else
                    {
                        mineBotT = Random.Range(2.5f, 6f);
                        mineBotBoom = Random.value < 0.3f;
                        if (puppet != null) puppet.Stun(mineBotT);
                    }
                    break;
                case AbilityType.BSOD:
                    bsodT = bsodDur = info.duration;
                    if (isPlayer) ProcAudio.Play(Sfx.ErrorTone, 0.9f, 0f);
                    else if (puppet != null) puppet.Stun(info.duration);
                    ProcAudio.PlayAt(Sfx.ErrorTone, transform.position + Vector3.up, 0.5f, 0f);
                    break;
            }
        }

        /// <summary>Minesweeper finished (player). Lose = the mine goes off for real.</summary>
        public void OnMineResult(bool win)
        {
            if (!mineActive) return;
            mineActive = false;
            GameInput.ModalUI = false;
            if (self.alive)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (win)
            {
                ProcAudio.Play(Sfx.AbilityReady, 0.7f, 0f);
                Say("Разминировано!", "Можно двигаться", new Color(0.5f, 1f, 0.5f));
            }
            else if (self.alive)
                Explosions.Detonate(transform.position + Vector3.up * 0.4f, 4f, 250f, 8f, mineCaster ? mineCaster : self, "Сапёр");
        }

        // ------------------------------------------------------------------ shrink

        void StartShrink(float dur)
        {
            shrinkT = shrinkDur = dur;
            ProcAudio.PlayAt(Sfx.ShrinkDown, transform.position + Vector3.up, 0.9f, 0.05f);
            if (isPlayer)
            {
                ProcAudio.Play(Sfx.ShrinkDown, 0.6f);
                ApplyPlayerShrink(true);
                Say("Мини-я", "Вы крошечный. Оружие — нет.", AbilityDB.Get(AbilityType.Shrink).color);
            }
        }

        void ApplyPlayerShrink(bool on)
        {
            if (!motor || on == shrinkApplied) return;
            shrinkApplied = on;
            var cc = motor.Controller;
            if (on)
            {
                baseStand = motor.standHeight; baseCrouch = motor.crouchHeight; baseProne = motor.proneHeight;
                baseEyeStand = motor.eyeStand; baseEyeCrouch = motor.eyeCrouch; baseEyeProne = motor.eyeProne;
                baseRadius = cc.radius;
                float s = ShrinkScale;
                motor.standHeight = baseStand * s; motor.crouchHeight = baseCrouch * s; motor.proneHeight = Mathf.Max(baseProne * s, baseRadius * s * 2f + 0.02f);
                motor.eyeStand = baseEyeStand * s; motor.eyeCrouch = baseEyeCrouch * s; motor.eyeProne = baseEyeProne * s;
                cc.radius = baseRadius * s;
            }
            else
            {
                motor.standHeight = baseStand; motor.crouchHeight = baseCrouch; motor.proneHeight = baseProne;
                motor.eyeStand = baseEyeStand; motor.eyeCrouch = baseEyeCrouch; motor.eyeProne = baseEyeProne;
                cc.radius = baseRadius;
            }
        }

        void ApplyBodyScale()
        {
            if (!rig) return;
            rig.transform.localScale = Vector3.one * bodyScale;
            if (rig.weaponSocket) rig.weaponSocket.localScale = Vector3.one / Mathf.Max(0.05f, bodyScale); // the gun keeps its size
            if (rig.head) rig.head.localScale = Vector3.one * headScale;
        }

        // ------------------------------------------------------------------ snowman

        void StartSnowman(float dur)
        {
            if (IsChicken) EndChicken();
            snowmanT = snowmanDur = dur;
            if (!snowman) snowman = SnowmanModel.Build(transform, !isPlayer);
            snowman.gameObject.SetActive(true);
            if (!isPlayer && rig) rig.SetCollidersEnabled(false);
            if (isPlayer && motor.IsPulled) motor.StopPull();
            Vector3 c = transform.position + Vector3.up;
            FX.Ensure().Puff(c, Vector3.up, 16, 1f, new Vector2(0.5f, 3f), new Vector2(0.6f, 1.3f), new Vector2(0.6f, 1.4f), new Color(0.95f, 0.97f, 1f, 0.7f), new Color(0.85f, 0.9f, 1f, 0.6f));
            ProcAudio.PlayAt(Sfx.SandBreak, c, 0.8f, 0.1f);
            if (isPlayer) Say("Вы — снеговик!", "Еле ползёте и не можете стрелять", AbilityDB.Get(AbilityType.Snowman).color);
        }

        void EndSnowman()
        {
            snowmanT = 0f;
            if (snowman) snowman.gameObject.SetActive(false);
            if (!isPlayer && rig && self.alive && !IsChicken) rig.SetCollidersEnabled(true);
            FX.Ensure().Puff(transform.position + Vector3.up, Vector3.up, 12, 1f, new Vector2(0.5f, 2.5f), new Vector2(0.5f, 1.1f), new Vector2(0.5f, 1f),
                             new Color(0.95f, 0.97f, 1f, 0.6f), new Color(0.85f, 0.9f, 1f, 0.5f));
            visKey = -1;
        }

        // ------------------------------------------------------------------ building

        bool StartBuild(float dur)
        {
            var info = AbilityDB.Get(AbilityType.Build);
            if (!isPlayer)
            {
                // bots: instant cover wall toward the enemy
                if (!bot || !bot.Target) return false;
                Vector3 to = bot.Target.transform.position - transform.position; to.y = 0f;
                if (to.sqrMagnitude < 1f) return false;
                float yaw = Quaternion.LookRotation(to).eulerAngles.y;
                Vector3 p = transform.position + to.normalized * 1.6f;
                PlacePiece(false, p, Quaternion.Euler(0f, yaw, 0f));
                return true;
            }
            building = true;
            buildT = buildDur = dur;
            buildLeft = 6;
            buildRot = 0;
            if (!ghostOk)
            {
                ghostOk = FX.MakeParticleMat("BuildGhost", Texture2D.whiteTexture, false, new Color(0.3f, 0.7f, 1f, 0.35f), true);
                ghostBad = FX.MakeParticleMat("BuildGhostBad", Texture2D.whiteTexture, false, new Color(1f, 0.25f, 0.2f, 0.35f), true);
            }
            if (!ghost)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = "BuildGhost";
                Destroy(g.GetComponent<Collider>());
                g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ghost = g.transform;
            }
            ghost.gameObject.SetActive(true);
            ProcAudio.Play(Sfx.Hammer, 0.6f);
            Say(info.name, "ЛКМ — стена, ПКМ — пандус, R — повернуть, V — выйти", info.color);
            return true;
        }

        public void EndBuild()
        {
            building = false;
            buildT = 0f;
            if (ghost) Destroy(ghost.gameObject);
            ghost = null;
        }

        static Vector3 WallSize => new Vector3(3f, 2.8f, 0.2f);
        const float RampRun = 3f, RampRise = 2.6f;

        /// <summary>Grid-snapped placement in front of the player (Fortnite style: 90° steps).</summary>
        void PieceTransform(bool ramp, Vector3 basePos, Quaternion yawRot, out Vector3 center, out Quaternion rot, out Vector3 size)
        {
            float gy = GroundY(basePos, transform, basePos.y);
            Vector3 b = new Vector3(Mathf.Round(basePos.x * 2f) / 2f, gy, Mathf.Round(basePos.z * 2f) / 2f);
            if (!ramp)
            {
                size = WallSize;
                rot = yawRot;
                center = b + Vector3.up * size.y * 0.5f;
            }
            else
            {
                float len = Mathf.Sqrt(RampRun * RampRun + RampRise * RampRise);
                size = new Vector3(3f, 0.2f, len);
                rot = yawRot * Quaternion.Euler(-Mathf.Atan2(RampRise, RampRun) * Mathf.Rad2Deg, 0f, 0f);
                center = b + yawRot * Vector3.forward * (RampRun * 0.5f - 0.5f) + Vector3.up * (RampRise * 0.5f + 0.05f);
            }
        }

        void UpdateBuild(float dt)
        {
            if (!building) return;
            buildT -= dt;
            if (!self.alive || buildT <= 0f || buildLeft <= 0) { EndBuild(); return; }
            if (!isPlayer || !ghost) return;
            if (GameInput.RotatePressed) { buildRot = (buildRot + 1) % 4; ProcAudio.Play(Sfx.Click, 0.5f); }
            bool ramp = GameSettings.Held(BindAction.Aim);
            Vector3 f = Facing;
            float yaw = Mathf.Round(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg / 90f) * 90f + buildRot * 90f;
            var yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 basePos = transform.position + f * (ramp ? 1.2f : 2.4f);
            PieceTransform(ramp, basePos, yawRot, out var c, out var r, out var s);
            ghost.position = c; ghost.rotation = r; ghost.localScale = s;
            bool ok = PieceFree(c, r, s);
            ghost.GetComponent<Renderer>().sharedMaterial = ok ? ghostOk : ghostBad;

            bool placeWall = GameInput.FirePressed;
            bool placeRamp = Mouse.current != null && Mouse.current.rightButton.wasReleasedThisFrame && GameInput.Enabled;
            if ((placeWall || placeRamp) && ok)
            {
                PlacePiece(placeRamp, basePos, yawRot);
                buildLeft--;
            }
            else if ((placeWall || placeRamp) && !ok) ProcAudio.Play(Sfx.Dry, 0.4f);
        }

        bool PieceFree(Vector3 c, Quaternion r, Vector3 s)
        {
            foreach (var h in Physics.OverlapBox(c, s * 0.48f, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (h.GetComponentInParent<Combatant>()) return false;
            return true;
        }

        void PlacePiece(bool ramp, Vector3 basePos, Quaternion yawRot)
        {
            PieceTransform(ramp, basePos, yawRot, out var c, out var r, out var s);
            if (!PieceFree(c, r, s)) return;
            int id = NetGame.NewId();
            BuiltPiece.Create(c, r, s, id);
            SendEvent(AbilityNetEvent.Build, w => { w.Write(id); W(w, c); W(w, r); W(w, s); });
            ProcAudio.PlayAt(Sfx.Hammer, c, 0.9f, 0.1f);
            if (isPlayer) ProcAudio.Play(Sfx.Hammer, 0.4f, 0.1f);
        }

        // ------------------------------------------------------------------ per-frame

        void UpdateExtra(float dt)
        {
            // shrink
            if (shrinkT > 0f)
            {
                shrinkT -= dt;
                if (shrinkT <= 0f)
                {
                    shrinkT = 0f;
                    ApplyPlayerShrink(false);
                    ProcAudio.PlayAt(Sfx.GrowUp, transform.position + Vector3.up, 0.8f, 0.05f);
                }
            }
            if (bigHeadT > 0f) bigHeadT -= dt;
            float wantBody = Shrunk ? ShrinkScale : 1f, wantHead = BigHead ? BigHeadScale : 1f;
            if (!Mathf.Approximately(bodyScale, wantBody) || !Mathf.Approximately(headScale, wantHead))
            {
                bodyScale = Mathf.MoveTowards(bodyScale, wantBody, dt * 2.5f);
                headScale = Mathf.MoveTowards(headScale, wantHead, dt * 6f);
                ApplyBodyScale();
            }

            if (screamT > 0f) screamT -= dt;
            if (bsodT > 0f) bsodT -= dt;

            if (snowmanT > 0f)
            {
                snowmanT -= dt;
                if (snowmanT <= 0f) EndSnowman();
            }

            if (confuseT > 0f)
            {
                confuseT -= dt;
                if (anim && !isPlayer) anim.lookYaw = Mathf.Sin(Time.time * 7f) * 45f;
            }

            if (launched) UpdateLaunch(dt);

            if (mineBotT > 0f)
            {
                mineBotT -= dt;
                if (mineBotT <= 0f && mineBotBoom && self.alive)
                    Explosions.Detonate(transform.position + Vector3.up * 0.4f, 4f, 250f, 8f, mineCaster ? mineCaster : self, "Сапёр");
            }

            UpdateBuild(dt);
        }

        void UpdateLaunch(float dt)
        {
            launchT += dt;
            launchVel.y += -24f * dt;
            Vector3 step = launchVel * dt;
            if (launchVel.y > 0f && Physics.Raycast(transform.position + Vector3.up * 1.7f, Vector3.up, step.y + 0.15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                launchVel.y = 0f;
            transform.position += step;
            if (launchVel.y < 0f)
            {
                float gy = GroundY(transform.position, transform, -9999f);
                if (transform.position.y <= gy + 0.02f || launchT > 6f)
                {
                    if (gy > -9000f) transform.position = new Vector3(transform.position.x, gy, transform.position.z);
                    launched = false;
                    if (puppet != null)
                    {
                        puppet.ExternalControl = false;
                        puppet.Stun(0.7f);
                    }
                    ProcAudio.PlayAt(Sfx.Land, transform.position, 1f, 0.1f);
                    FX.Ensure().DustBurst(transform.position, Vector3.up, 6, new Color(0.6f, 0.57f, 0.52f, 0.5f), new Color(0.5f, 0.47f, 0.43f, 0.4f), 0.8f);
                }
            }
        }

        void ResetExtra()
        {
            if (shrinkT > 0f || shrinkApplied) { shrinkT = 0f; ApplyPlayerShrink(false); }
            bigHeadT = 0f;
            bodyScale = 1f; headScale = 1f;
            ApplyBodyScale();
            screamT = 0f;
            if (snowmanT > 0f) EndSnowman();
            confuseT = 0f;
            if (anim && !isPlayer) anim.lookYaw = 0f;
            if (launched) { launched = false; if (puppet != null) puppet.ExternalControl = false; }
            if (mineActive)
            {
                mineActive = false;
                GameInput.ModalUI = false;
                if (AbilityHUD.Instance) AbilityHUD.Instance.CloseMinesweeper();
            }
            mineBotT = 0f;
            if (building) EndBuild();
            bsodT = 0f;
            if (isPlayer) GameInput.Frozen = false;
        }

        // ------------------------------------------------------------------ bots & debug

        bool BotWantsExtra(AbilityType logic, Combatant t, bool vis, float dist)
        {
            var info = AbilityDB.Get(logic);
            switch (logic)
            {
                case AbilityType.Shrink: return vis && Random.value < 0.3f;
                case AbilityType.Build: return vis && dist > 6f && (bot.Health01 < 0.6f || Random.value < 0.15f);
                case AbilityType.BigHeads:
                case AbilityType.Screamer:
                case AbilityType.Snowman:
                case AbilityType.Confuse:
                case AbilityType.Launch:
                case AbilityType.Minesweeper:
                case AbilityType.BSOD:
                    return t && dist < info.radius * 0.8f && (vis || dist < info.radius * 0.4f) && Random.value < 0.5f;
            }
            return false;
        }

        /// <summary>Testing helper: PageUp / PageDown give the next / previous ability (Home — a trap).</summary>
        void DebugCycle()
        {
            var k = Keyboard.current;
            if (k == null) return;
            int dir = k.pageUpKey.wasPressedThisFrame ? 1 : k.pageDownKey.wasPressedThisFrame ? -1 : 0;
            if (k.homeKey.wasPressedThisFrame) { Give(AbilityType.ChickenTrap, AbilityDB.RandomDisguise()); return; }
            if (dir == 0) return;
            int count = System.Enum.GetValues(typeof(AbilityType)).Length;
            int i = (int)(Type == AbilityType.ChickenTrap ? Shown : Type);
            do { i = (i + dir + count) % count; } while (i == 0 || i == (int)AbilityType.ChickenTrap);
            Give((AbilityType)i, (AbilityType)i);
        }
    }

    /// <summary>Expanding ground ring that shows the radius of an area ability.</summary>
    public class Shockwave : MonoBehaviour
    {
        float radius, t;
        Material mat;
        Color color;
        static Texture2D ringTex;

        public static void Spawn(Vector3 pos, float radius, Color c)
        {
            if (!ringTex)
            {
                const int N = 128;
                ringTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.92f) / 0.08f) + Mathf.Clamp01(0.92f - r) * 0.15f;
                        px[y * N + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                    }
                ringTex.SetPixels32(px); ringTex.Apply();
            }
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "Shockwave";
            Destroy(q.GetComponent<Collider>());
            q.transform.position = pos;
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var s = q.AddComponent<Shockwave>();
            s.radius = radius;
            s.color = c;
            s.mat = FX.MakeParticleMat("Shockwave", ringTex, true, c * 2f, true);
            var r2 = q.GetComponent<Renderer>();
            r2.sharedMaterial = s.mat;
            r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q.transform.localScale = Vector3.zero;
        }

        void Update()
        {
            t += Time.deltaTime / 0.55f;
            float e = 1f - (1f - t) * (1f - t);
            transform.localScale = Vector3.one * radius * 2f * e;
            if (mat) mat.SetColor("_BaseColor", new Color(color.r * 2f, color.g * 2f, color.b * 2f, 1f - t));
            if (t >= 1f) { if (mat) Destroy(mat); Destroy(gameObject); }
        }
    }

    /// <summary>A built wall / ramp: rises out of the ground, breakable cover, disappears after a while.</summary>
    public class BuiltPiece : MonoBehaviour
    {
        Vector3 size;
        float t, life;
        Destructible d;

        public static void Create(Vector3 center, Quaternion rot, Vector3 size, int netId = 0)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "BuiltPiece";
            go.transform.SetPositionAndRotation(center, rot);
            go.transform.localScale = new Vector3(size.x, size.y * 0.05f, size.z);
            go.GetComponent<Renderer>().sharedMaterial = AbilityProps.Mat(new Color(0.72f, 0.5f, 0.28f), 0f, 0.25f);
            go.AddComponent<SurfaceTag>().kind = SurfaceKind.Wood;
            var d = go.AddComponent<Destructible>();
            d.kind = Destructible.Kind.Wood;
            d.maxHealth = 160f;
            d.netId = netId;
            NetGame.RegisterDestructible(d);
            var p = go.AddComponent<BuiltPiece>();
            p.size = size;
            p.d = d;
            FX.Ensure().Chunks(center, Vector3.up, 10, new Color(0.7f, 0.5f, 0.28f), new Color(0.5f, 0.35f, 0.18f), 3f);
        }

        void Update()
        {
            if (t < 1f)
            {
                t = Mathf.Min(1f, t + Time.deltaTime / 0.2f);
                float e = 1f - (1f - t) * (1f - t);
                transform.localScale = new Vector3(size.x, Mathf.Lerp(size.y * 0.05f, size.y, e), size.z);
            }
            life += Time.deltaTime;
            if (life > 45f && d && !d.Broken) d.Break(Vector3.down, transform.position);
        }
    }

    /// <summary>Snowman model (snowman area ability victims). Wobbles while moving.</summary>
    public class SnowmanModel : MonoBehaviour
    {
        Transform body;
        Vector3 lastPos;
        float wob;

        public static SnowmanModel Build(Transform parent, bool hitbox)
        {
            var root = AbilityProps.Root("Snowman", parent);
            var m = root.gameObject.AddComponent<SnowmanModel>();
            var snow = AbilityProps.Mat(new Color(0.95f, 0.97f, 1f), 0.15f, 0.15f);
            var coal = AbilityProps.Mat(new Color(0.05f, 0.05f, 0.05f), 0f, 0.6f);
            var carrot = AbilityProps.Mat(new Color(1f, 0.5f, 0.1f), 0f, 0.4f);
            var wood = AbilityProps.Mat(new Color(0.35f, 0.22f, 0.12f), 0f, 0.3f);
            var bucket = AbilityProps.Mat(new Color(0.55f, 0.15f, 0.12f), 0f, 0.5f, 0.6f);
            m.body = AbilityProps.Root("Body", root);
            AbilityProps.Part(PrimitiveType.Sphere, m.body, new Vector3(0f, 0.38f, 0f), Vector3.one * 0.8f, snow);
            AbilityProps.Part(PrimitiveType.Sphere, m.body, new Vector3(0f, 0.98f, 0f), Vector3.one * 0.58f, snow);
            AbilityProps.Part(PrimitiveType.Sphere, m.body, new Vector3(0f, 1.42f, 0f), Vector3.one * 0.42f, snow);
            AbilityProps.Part(PrimitiveType.Cylinder, m.body, new Vector3(0f, 1.47f, 0.27f), new Vector3(0.05f, 0.11f, 0.05f), carrot, new Vector3(90f, 0f, 0f));
            AbilityProps.Part(PrimitiveType.Sphere, m.body, new Vector3(0.08f, 1.52f, 0.18f), Vector3.one * 0.05f, coal);
            AbilityProps.Part(PrimitiveType.Sphere, m.body, new Vector3(-0.08f, 1.52f, 0.18f), Vector3.one * 0.05f, coal);
            for (int i = 0; i < 3; i++) AbilityProps.Part(PrimitiveType.Sphere, m.body, new Vector3(0f, 0.85f + i * 0.13f, 0.28f), Vector3.one * 0.05f, coal);
            AbilityProps.Part(PrimitiveType.Cylinder, m.body, new Vector3(0f, 1.7f, 0f), new Vector3(0.3f, 0.12f, 0.3f), bucket, new Vector3(-8f, 0f, 0f));
            AbilityProps.Part(PrimitiveType.Cube, m.body, new Vector3(0.45f, 1.05f, 0f), new Vector3(0.45f, 0.03f, 0.03f), wood, new Vector3(0f, 0f, 25f));
            AbilityProps.Part(PrimitiveType.Cube, m.body, new Vector3(-0.45f, 1.05f, 0f), new Vector3(0.45f, 0.03f, 0.03f), wood, new Vector3(0f, 0f, -25f));
            if (hitbox)
            {
                var cap = root.gameObject.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 0.85f, 0f); cap.radius = 0.38f; cap.height = 1.7f;
                root.gameObject.AddComponent<Hitbox>().multiplier = 1f;
            }
            m.lastPos = root.position;
            return m;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 v = (transform.position - lastPos) / dt;
            lastPos = transform.position;
            wob = Anim.Damp(wob, Mathf.Clamp01(new Vector2(v.x, v.z).magnitude / 1.5f), 6f, dt);
            float s = Mathf.Sin(Time.time * 9f);
            body.localRotation = Quaternion.Euler(4f * wob, 0f, s * 9f * wob);
            body.localPosition = new Vector3(0f, Mathf.Abs(s) * 0.05f * wob, 0f);
        }
    }
}
