using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// First-person movement: walk / sprint / crouch / prone / slide / jump (no air strafing or
    /// speed carry between jumps), plus vaulting over low obstacles and mantling onto ledges
    /// along a smooth bezier path.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speeds")]
        public float walkSpeed = 4.6f;
        public float sprintSpeed = 7.4f;
        public float crouchSpeed = 2.5f;
        public float proneSpeed = 1.15f;
        public float groundAccel = 11f;
        public float groundDecel = 13f;
        public float jumpHeight = 1.05f;
        public float gravity = -24f;
        public float coyoteTime = 0.12f;
        public float jumpBuffer = 0.15f;

        [Header("Air")]
        [Tooltip("How fast the player can steer toward the input direction while airborne (never above the run speed).")]
        public float airControl = 2.5f;
        [Tooltip("Hard cap on horizontal speed (slides, impulses).")]
        public float maxHorizontalSpeed = 8.2f;

        [Header("Stances")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.1f;
        public float proneHeight = 0.72f;
        public float eyeStand = 1.68f;
        public float eyeCrouch = 1.0f;
        public float eyeProne = 0.4f;
        public float crouchLerp = 12f;
        public float proneLerp = 5f;

        // (new names on purpose: the maps carried the old, shorter slide as slideBoost 10 / slideFriction 7 /
        // slideMaxTime 1.1, which would have overridden these defaults)
        [Header("Slide")]
        [Tooltip("Speed a slide starts with (m/s): from a sprint it is a burst forward, about 10 m long")]
        public float slideStartSpeed = 11.5f;
        [Tooltip("How fast a slide loses speed (m/s²)")]
        public float slideDecel = 5.5f;
        public float slideMinSpeed = 3.2f;
        [Tooltip("Longest slide (s)")]
        public float slideDuration = 1.25f;
        [Tooltip("Horizontal speed cap while sliding (the run cap is lower)")]
        public float slideMaxSpeed = 12f;
        [Tooltip("A new slide can start this long after the last one ended (s)")]
        public float slideCooldown = 0.6f;

        [Header("Slide jump (dash)")]
        [Tooltip("Jumping out of a slide launches forward at least this fast (m/s): a dash")]
        public float slideJumpSpeed = 11.5f;
        [Tooltip("Speed cap of a slide jump until it lands")]
        public float slideJumpMaxSpeed = 13f;
        [Tooltip("A jump this soon after the slide ended still dashes (s)")]
        public float slideJumpGrace = 0.2f;
        [Tooltip("The dash may turn the slide's direction this far toward the movement input (degrees)")]
        public float slideJumpSteer = 45f;

        [Header("Wall run")]
        [Tooltip("Horizontal speed needed to catch a wall (m/s)")]
        public float wallRunMinSpeed = 4.2f;
        [Tooltip("Speed along the wall (at least: a faster entry keeps its speed, up to the max)")]
        public float wallRunSpeed = 8f;
        public float wallRunMaxSpeed = 10.5f;
        [Tooltip("Longest run on one wall (s)")]
        public float wallRunMaxTime = 2.2f;
        [Tooltip("Share of the run that keeps its height; after it the feet slowly slip down the wall")]
        public float wallRunHold = 0.7f;
        [Tooltip("How fast the feet slip down the wall at the very end of a run (m/s)")]
        public float wallRunSink = 1.5f;
        [Tooltip("Share of the vertical speed kept when the feet catch the wall: a jump onto it carries them up a little, a fall into it is stopped")]
        public float wallRunCatch = 0.4f;
        [Tooltip("How fast that caught vertical speed levels out (m/s²): after it the run is level")]
        public float wallRunLevel = 5f;
        [Tooltip("How far the view may turn from the run's direction (toward the wall or away from it) and keep running (degrees)")]
        public float wallRunMaxLook = 90f;
        [Tooltip("Jump off a wall: speed away from it and up (m/s)")]
        public float wallJumpOut = 6f;
        public float wallJumpUp = 6.2f;
        [Tooltip("A jump this soon after coming off a wall still kicks off it (s)")]
        public float wallJumpCoyote = 0.2f;
        [Tooltip("Wall runs in a row before the feet touch the ground again")]
        public int wallRunChain = 4;

        [Header("Vault / Mantle")]
        public float minVaultHeight = 0.35f;
        public float maxVaultHeight = 1.45f;
        public float vaultReach = 0.9f;
        public float maxVaultThickness = 1.25f;

        [Header("Refs")]
        public Transform cameraRoot;

        // ---- state exposed to camera / weapon / body animation
        public Vector3 Velocity => velocity;
        public float HorizontalSpeed => new Vector2(velocity.x, velocity.z).magnitude;
        public bool IsGrounded { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsProne { get; private set; }
        public bool IsSliding { get; private set; }
        public bool IsVaulting { get; private set; }
        public bool VaultIsMantle { get; private set; }
        public float VaultT { get; private set; }
        public float VaultSide { get; private set; } = 1f;
        public float VaultHeight01 { get; private set; }
        public float CrouchAmount { get; private set; }
        public float ProneAmount { get; private set; }
        public float StepPhase { get; private set; }
        public float Speed01 { get; private set; }
        public Vector2 MoveInput { get; private set; }
        public float SpeedMultiplier { get; set; } = 1f;
        public bool BlockSprint { get; set; }
        public CharacterController Controller => cc;
        /// <summary>Running along a wall (airborne, feet on it).</summary>
        public bool IsWallRunning { get; private set; }
        /// <summary>
        /// Which side of the view the wall is on while running along it: +1 right, -1 left, in between as the view turns
        /// toward or away from it (0: straight ahead or behind, or not on a wall).
        /// </summary>
        public float WallSide { get; private set; }

        /// <summary>When locked (e.g. during an execution) the motor ignores input and something else drives the transform.</summary>
        public bool Locked
        {
            get => locked;
            set
            {
                if (locked == value) return;
                locked = value;
                velocity = Vector3.zero;
                IsSliding = false; slideQueued = false;
                IsWallRunning = false; WallSide = 0f;
                pulling = false;
                cc.enabled = !value && !noclip;
                if (!value) ApplyHeight();
            }
        }

        /// <summary>Movement and jump input are ignored (stunned / slipped on a banana).</summary>
        public bool InputBlocked { get; set; }
        public bool IsPulled => pulling;
        public bool IsNoclip => noclip;

        /// <summary>Ghost mode: flies where the camera looks, passes through everything.</summary>
        public bool Noclip
        {
            get => noclip;
            set
            {
                if (noclip == value) return;
                noclip = value;
                velocity = Vector3.zero;
                IsSliding = false; slideQueued = false;
                IsWallRunning = false; WallSide = 0f;
                if (value) { pulling = false; proneToggled = crouchToggled = false; }
                cc.enabled = !value && !locked;
                if (!value) ApplyHeight();
            }
        }

        /// <summary>Drags the player toward a moving point (grappling hook), with collisions.</summary>
        public void StartPull(Func<Vector3> target, float speed, float maxTime, Action done)
        {
            if (locked || noclip) return;
            pulling = true;
            pullTarget = target;
            pullSpeed = speed;
            pullTime = maxTime;
            pullStuck = 0f;
            pullDone = done;
            IsSliding = false; slideQueued = false;
            IsVaulting = false;
            IsWallRunning = false; WallSide = 0f;
            proneToggled = crouchToggled = false;
            if (!cc.enabled) cc.enabled = true;
        }

        public void StopPull()
        {
            if (!pulling) return;
            pulling = false;
            var cb = pullDone; pullDone = null;
            cb?.Invoke();
        }

        public void AddImpulse(Vector3 v)
        {
            // a blast or a shove knocks the player off a wall (the run would overwrite the push)
            if (IsWallRunning && v.sqrMagnitude > 4f) EndWallRun(true, 0f);
            velocity += v;
            if (v.y > 0.5f) { lastGroundedTime = -10f; IsGrounded = false; }
        }

        public event Action<float> Landed;
        public event Action Jumped;
        public event Action<bool> VaultStarted;
        public event Action Footstep;
        public event Action SlideStarted;
        /// <summary>A jump out of a slide launched a dash (raised in the same frame, just before Jumped).</summary>
        public event Action Dashed;
        public event Action ProneStarted;
        /// <summary>The feet caught a wall (the run starts).</summary>
        public event Action WallRunStarted;
        /// <summary>A jump off a wall (raised just before Jumped).</summary>
        public event Action WallJumped;

        enum Stance { Stand, Crouch, Prone }

        // ---- wall run state
        Vector3 wallNormal, wallAlong, lastWallNormal, lastWallPoint, wallPoint;
        bool lastWallValid, wallCoyote;
        float wallTime, wallSpeed, wallVy, wallLeftTime = -10f;
        int wallRuns;
        readonly System.Collections.Generic.Dictionary<Collider, bool> wallOk = new System.Collections.Generic.Dictionary<Collider, bool>();

        CharacterController cc;
        Vector3 velocity;
        float currentHeight;
        bool crouchToggled, proneToggled, locked;
        float lastGroundedTime = -10f, lastJumpPressTime = -10f;
        float slideTimer, slideEndTime = -10f;
        // crouch pressed in the air: a slide as the feet touch down (see Update)
        bool slideQueued;
        float groundedFor, lastAirTime;
        // airborne after a slide jump: the dash keeps its speed above the run cap until it lands
        bool dashAir;
        float stepAccum; int lastStepIndex;
        float airTime, lastVy;
        bool wasGrounded = true;

        bool noclip, pulling;
        Func<Vector3> pullTarget;
        float pullSpeed, pullTime, pullStuck;
        Action pullDone;

        Vector3 vStart, vC1, vC2, vEnd, vExitVel;
        float vDuration, vTime, vaultCooldown;

        readonly Collider[] overlap = new Collider[16];
        const QueryTriggerInteraction QTI = QueryTriggerInteraction.Ignore;

        public void Teleport(Vector3 pos)
        {
            bool was = cc.enabled;
            cc.enabled = false;
            transform.position = pos;
            velocity = Vector3.zero;
            IsSliding = false; slideQueued = false;
            IsVaulting = false;
            IsWallRunning = false; WallSide = 0f; wallRuns = 0; lastWallValid = false; wallCoyote = false;
            proneToggled = crouchToggled = false;
            cc.enabled = was && !locked;
        }

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            currentHeight = standHeight;
            ApplyHeight();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            vaultCooldown -= dt;

            if (locked)
            {
                MoveInput = Vector2.zero;
                Speed01 = 0f;
                UpdateHeight(dt, Stance.Stand);
                return;
            }

            if (noclip) { UpdateNoclip(dt); return; }
            if (pulling) { UpdatePull(dt); return; }

            if (IsVaulting)
            {
                UpdateVault(dt);
                UpdateHeight(dt, Stance.Crouch);
                return;
            }

            Vector2 input = InputBlocked ? Vector2.zero : GameInput.Move;
            MoveInput = input;
            IsGrounded = cc.isGrounded;
            if (IsGrounded) lastGroundedTime = Time.time;
            groundedFor = IsGrounded ? groundedFor + dt : 0f;
            if (GameInput.JumpPressed && !InputBlocked) lastJumpPressTime = Time.time;

            Vector3 hv = new Vector3(velocity.x, 0f, velocity.z);
            float hSpeed = hv.magnitude;

            // ---- stance input (each crouch key read once a frame)
            bool togglePressed = GameInput.CrouchTogglePressed;
            bool crouchPressed = togglePressed || GameInput.CrouchHoldPressed;
            if (GameInput.PronePressed)
            {
                if (proneToggled) proneToggled = false;
                else if (IsGrounded && !IsSliding) { proneToggled = true; crouchToggled = false; ProneStarted?.Invoke(); }
            }
            if (togglePressed)
            {
                if (proneToggled) { proneToggled = false; crouchToggled = true; }
                else crouchToggled = !crouchToggled;
            }

            // ---- slide cancel: the crouch key again during a slide stops it and leaves the player crouched — the toggle key
            // has just switched crouch on above, the hold key crouches while held. The speed bleeds off by the ground
            // deceleration; a jump right after still dashes (slideJumpGrace)
            bool slideCancelled = false;
            if (IsSliding && crouchPressed && slideTimer > 0.05f)
            {
                IsSliding = false;
                slideEndTime = Time.time;
                slideCancelled = true;
            }

            // ---- slide on landing: crouch pressed in the air (a jump, a fall, dropping off a wall) arms a slide for the moment
            // the feet touch down. Meanwhile the body crouches in the air and the sprint key does not stand it up (it did:
            // with crouch on a toggle and sprint held, the press in the air was undone the same frame). Letting go of a
            // hold key keeps it armed; switching a toggled crouch off again disarms it. A press just after touching down
            // counts too, and an armed slide waits a few frames after it (the slide cooldown may end just then)
            if (crouchPressed && !IsGrounded) slideQueued = !(togglePressed && !crouchToggled);
            bool lateLand = crouchPressed && IsGrounded && groundedFor <= 0.15f && lastAirTime >= 0.25f;

            // ---- slide start: crouch while sprinting, or a landing slide with some speed forward (a fall bleeds the run
            // toward walking speed, so less is asked of it); the speed of a landing dash does not stack into the next slide
            bool sprintSlide = crouchPressed && IsSprinting && hSpeed > walkSpeed + 0.5f;
            bool landSlide = (slideQueued || lateLand) && hSpeed > walkSpeed * 0.85f && Vector3.Dot(hv, transform.forward) > -0.2f * hSpeed;
            if (!IsSliding && !slideCancelled && (sprintSlide || landSlide) && IsGrounded && Time.time - slideEndTime >= slideCooldown)
            {
                IsSliding = true;
                slideQueued = false;
                slideTimer = 0f;
                crouchToggled = false;
                Vector3 dir = hSpeed > 0.1f ? hv / hSpeed : transform.forward;
                hv = dir * Mathf.Max(Mathf.Min(hSpeed, sprintSpeed) + 2.5f, slideStartSpeed);
                SlideStarted?.Invoke();
            }
            if (IsGrounded && groundedFor > 0.15f) slideQueued = false;

            bool sprintInput = GameInput.SprintHeld && input.y > 0.3f && !BlockSprint && !InputBlocked;
            if (sprintInput && !IsSliding && !slideQueued) { crouchToggled = false; proneToggled = false; }

            Stance want = proneToggled ? Stance.Prone
                        : (crouchToggled || GameInput.CrouchHeld || IsSliding || slideQueued) ? Stance.Crouch : Stance.Stand;
            Stance stance = want;
            if (stance == Stance.Stand && !CanFit(standHeight)) stance = Stance.Crouch;
            if (stance == Stance.Crouch && !CanFit(crouchHeight)) stance = Stance.Prone;
            IsProne = stance == Stance.Prone;
            IsCrouching = stance == Stance.Crouch;
            IsSprinting = sprintInput && stance == Stance.Stand;

            Vector3 wish = transform.right * input.x + transform.forward * input.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            bool hasInput = wish.sqrMagnitude > 0.01f;
            float target = IsProne ? proneSpeed : IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
            target *= SpeedMultiplier * SkyArena.SpeedScale; // sky arena: everybody runs twice as fast

            // ---- wall run: its own movement while on a wall (like the vault); a landing ends the chain
            if (IsGrounded) { wallRuns = 0; lastWallValid = false; wallCoyote = false; }
            if (IsWallRunning)
            {
                if (UpdateWallRun(dt, input, wish, hasInput, crouchPressed)) { UpdateHeight(dt, Stance.Stand); return; }
                hv = new Vector3(velocity.x, 0f, velocity.z); // came off the wall: fly on with the wall's speed
                hSpeed = hv.magnitude;
            }
            else if (TryStartWallRun(input, hv, hSpeed, stance))
            {
                if (UpdateWallRun(dt, input, wish, hasInput, false)) { UpdateHeight(dt, Stance.Stand); return; }
                hv = new Vector3(velocity.x, 0f, velocity.z);
                hSpeed = hv.magnitude;
            }
            // a jump just after coming off a wall still kicks off it
            if (wallCoyote && !IsGrounded && Time.time - wallLeftTime <= wallJumpCoyote && Time.time - lastJumpPressTime <= jumpBuffer)
            {
                WallJump();
                MoveFree(dt);
                UpdateHeight(dt, stance);
                return;
            }

            // ---- jump / vault decisions
            bool jumpPressedBuffered = Time.time - lastJumpPressTime <= jumpBuffer;
            if (jumpPressedBuffered && !IsProne && TryVault(hSpeed)) { lastJumpPressTime = -10f; return; }
            if (jumpPressedBuffered && IsProne) { proneToggled = false; crouchToggled = true; lastJumpPressTime = -10f; jumpPressedBuffered = false; }

            bool wantJump = jumpPressedBuffered;
            bool coyote = Time.time - lastGroundedTime <= coyoteTime;
            bool jumpNow = wantJump && coyote && velocity.y <= 0.5f && !IsProne && CanFit(standHeight);

            if (!jumpNow && !IsGrounded && GameInput.JumpHeld && input.y > 0.5f && velocity.y < 3f && !IsProne)
            {
                if (TryVault(hSpeed)) return; // ledge grab while airborne
            }

            // ---- horizontal movement
            if (IsSliding)
            {
                slideTimer += dt;
                float spd = Mathf.Max(0f, hv.magnitude - slideDecel * dt);
                Vector3 dir = hv.sqrMagnitude > 0.0001f ? hv.normalized : transform.forward;
                if (hasInput) dir = Vector3.RotateTowards(dir, wish.normalized, 0.9f * dt, 0f);
                hv = dir * spd;
                if (spd < slideMinSpeed || slideTimer > slideDuration || !IsGrounded) { IsSliding = false; slideEndTime = Time.time; }
            }
            else if (IsGrounded && !jumpNow)
            {
                float accel = hasInput ? groundAccel : groundDecel;
                hv = Vector3.Lerp(hv, wish * target, 1f - Mathf.Exp(-accel * dt));
            }
            else if (hasInput)
            {
                // limited air steering: turning toward the input never adds speed above the run speed. Crouched for a
                // slide on landing it keeps the speed it has (the crouch speed would brake the slide away)
                float before = hv.magnitude;
                float airTarget = slideQueued ? Mathf.Max(target, before) : target;
                hv = Vector3.MoveTowards(hv, wish * airTarget, airControl * dt);
                float cap = Mathf.Max(before, airTarget);
                if (hv.magnitude > cap) hv = hv.normalized * cap;
            }
            // a landed dash is over: back under the run cap
            if (IsGrounded && !jumpNow) dashAir = false;
            float capH = IsSliding ? Mathf.Max(maxHorizontalSpeed, slideMaxSpeed)
                       : dashAir ? Mathf.Max(maxHorizontalSpeed, slideJumpMaxSpeed) : maxHorizontalSpeed;
            float maxH = capH * SkyArena.SpeedScale;
            if (hv.magnitude > maxH) hv = hv.normalized * maxH;

            if (jumpNow)
            {
                bool dash = (IsSliding || Time.time - slideEndTime <= slideJumpGrace) && hv.sqrMagnitude > 1f;
                if (dash)
                {
                    // jumping out of a slide: a dash forward. The slide's speed carries on, topped up to slideJumpSpeed,
                    // along the slide (turned a little toward the movement input)
                    Vector3 dir = hv.normalized;
                    if (hasInput) dir = Vector3.RotateTowards(dir, wish.normalized, slideJumpSteer * Mathf.Deg2Rad, 0f);
                    float spd = Mathf.Min(Mathf.Max(hv.magnitude, slideJumpSpeed), slideJumpMaxSpeed) * SkyArena.SpeedScale;
                    hv = dir * spd;
                    dashAir = true;
                    Dashed?.Invoke();
                }
                // any other jump never carries more than the current run speed
                else if (hv.magnitude > target) hv = hv.normalized * target;
                if (IsSliding) slideEndTime = Time.time;
                crouchToggled = false;
                IsSliding = false;
                slideQueued = false;
                velocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
                lastGroundedTime = -10f;
                lastJumpPressTime = -10f;
                Jumped?.Invoke();
            }

            if (IsGrounded && !jumpNow && velocity.y < 0f) velocity.y = -3f;
            velocity.y += gravity * dt;
            velocity.x = hv.x;
            velocity.z = hv.z;
            lastVy = velocity.y;

            CollisionFlags flags = cc.Move(velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;

            bool groundedNow = cc.isGrounded;
            if (groundedNow && !wasGrounded && airTime > 0.08f) Landed?.Invoke(-lastVy);
            if (groundedNow && !wasGrounded) lastAirTime = airTime;
            airTime = groundedNow ? 0f : airTime + dt;
            wasGrounded = groundedNow;

            Vector3 real = cc.velocity;
            velocity.x = real.x;
            velocity.z = real.z;

            // ---- step cycle for head bob, footsteps, body animation
            float sp = new Vector2(velocity.x, velocity.z).magnitude;
            if (groundedNow && !IsSliding)
            {
                float stride = IsProne ? 1.4f : IsCrouching ? 2.2f : (IsSprinting ? 4.2f : 3.2f);
                stepAccum += sp * dt / stride;
            }
            StepPhase = stepAccum;
            int idx = Mathf.FloorToInt(stepAccum * 2f);
            if (idx != lastStepIndex)
            {
                lastStepIndex = idx;
                if (groundedNow && !IsSliding && sp > 0.6f) Footstep?.Invoke();
            }
            Speed01 = Mathf.Clamp01(sp / sprintSpeed);

            UpdateHeight(dt, stance);
        }

        // ------------------------------------------------------------------ wall run

        /// <summary>
        /// A wall the feet can run on: hit from the body's middle in <paramref name="dir"/> within the capsule radius +
        /// <paramref name="reach"/>; vertical; a static, visible surface (not a person, a loose prop or the map's invisible
        /// edge); wide (it goes on 0.45 m to both sides along it) and tall (it is there at head and knee height too).
        /// </summary>
        bool ProbeWall(Vector3 dir, float reach, out RaycastHit hit)
        {
            Vector3 c = transform.position + Vector3.up * (cc.height * 0.5f);
            if (!Physics.Raycast(c, dir, out hit, cc.radius + reach, Physics.DefaultRaycastLayers, QTI)) return false;
            if (Mathf.Abs(hit.normal.y) > 0.3f || !WallSurface(hit.collider)) return false;
            Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            Vector3 along = Vector3.Cross(Vector3.up, n);
            float d = hit.distance + 0.3f;
            return SameWall(c + along * 0.45f, n, d) && SameWall(c - along * 0.45f, n, d)
                && SameWall(c + Vector3.up * (cc.height * 0.38f), n, d) && SameWall(c - Vector3.up * (cc.height * 0.25f), n, d);
        }

        bool SameWall(Vector3 from, Vector3 n, float dist)
        {
            if (!Physics.Raycast(from, -n, out var h, dist, Physics.DefaultRaycastLayers, QTI)) return false;
            Vector3 hn = new Vector3(h.normal.x, 0f, h.normal.z);
            return Mathf.Abs(h.normal.y) < 0.35f && Vector3.Dot(hn.normalized, n) > 0.85f && WallSurface(h.collider);
        }

        /// <summary>Static and visible (cached per collider): not a character, not a dynamic body, not an invisible blocker.</summary>
        bool WallSurface(Collider c)
        {
            if (!c) return false;
            var rb = c.attachedRigidbody;
            if (rb && !rb.isKinematic) return false;
            if (wallOk.TryGetValue(c, out bool ok)) return ok;
            ok = !c.GetComponentInParent<Combatant>() && !c.GetComponent<Hitbox>()
                 && (c.GetComponent<Renderer>() || c.GetComponentInParent<Renderer>());
            if (wallOk.Count > 512) wallOk.Clear();
            wallOk[c] = ok;
            return ok;
        }

        bool TryStartWallRun(Vector2 input, Vector3 hv, float hSpeed, Stance stance)
        {
            if (IsGrounded || airTime < 0.06f || stance != Stance.Stand || IsSliding || InputBlocked) return false;
            if (input.y < 0.35f || hSpeed < wallRunMinSpeed * SkyArena.SpeedScale || velocity.y < -12f) return false;
            if (wallRuns >= wallRunChain || Time.time - wallLeftTime < 0.2f) return false;
            // the ground right under the feet: a hop beside a wall, not a wall run
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, 0.4f, Physics.DefaultRaycastLayers, QTI)) return false;

            // beside the flight path and diagonally ahead of it (a wall reached at an angle), wherever the view points
            Vector3 mv = hv / hSpeed, side = Vector3.Cross(Vector3.up, mv), fwd = transform.forward;
            bool found = false;
            RaycastHit best = default;
            for (int i = 0; i < 4; i++)
            {
                Vector3 dir = i == 0 ? side : i == 1 ? -side : i == 2 ? (side + mv).normalized : (mv - side).normalized;
                if (!ProbeWall(dir, 0.4f, out var h)) continue;
                if (!found || h.distance < best.distance) { best = h; found = true; }
            }
            if (!found) return false;

            Vector3 n = new Vector3(best.normal.x, 0f, best.normal.z).normalized;
            Vector3 along = Vector3.Cross(Vector3.up, n);
            if (Vector3.Dot(along, hv) < 0f) along = -along;
            // moving along the wall (head-on into it is a stop or a vault, not a run), the view a little inside the turn
            // the run allows (UpdateWallRun)
            if (Vector3.Dot(hv, along) < wallRunMinSpeed * 0.55f || Vector3.Dot(fwd, along) < MaxLookCos + 0.15f) return false;
            // the same wall again before landing: no climbing one wall by kicking off and catching it again
            if (lastWallValid && Vector3.Dot(n, lastWallNormal) > 0.9f && Mathf.Abs(Vector3.Dot(best.point - lastWallPoint, n)) < 0.6f) return false;

            IsWallRunning = true;
            IsSliding = false;
            slideQueued = false;
            dashAir = false;
            wallNormal = n;
            wallAlong = along;
            wallPoint = best.point;
            WallSide = Mathf.Clamp(Vector3.Dot(-n, transform.right), -1f, 1f);
            wallTime = 0f;
            wallCoyote = false;
            wallSpeed = Mathf.Min(Mathf.Max(Vector3.Dot(hv, along), wallRunSpeed * SkyArena.SpeedScale), wallRunMaxSpeed * SkyArena.SpeedScale);
            // the vertical speed is caught: a share of a jump's rise carries the feet a little up the wall (less on each
            // wall in a row), a fall into it is stopped; either levels out within a fraction of a second (UpdateWallRun)
            float riseCap = 2f * Mathf.Max(0.4f, 1f - 0.2f * wallRuns);
            wallVy = Mathf.Clamp(velocity.y * wallRunCatch, -1f, riseCap);
            wallRuns++;
            WallRunStarted?.Invoke();
            return true;
        }

        /// <summary>One frame on the wall. False when the run ended this frame (the normal movement goes on).</summary>
        bool UpdateWallRun(float dt, Vector2 input, Vector3 wish, bool hasInput, bool crouchPressed)
        {
            wallTime += dt;
            // jump: kick off the wall
            if (Time.time - lastJumpPressTime <= jumpBuffer)
            {
                WallJump();
                MoveFree(dt);
                return true;
            }
            // the view may turn up to wallRunMaxLook from the run's direction — toward the wall or away from it, aiming across
            // the room — and the run goes on along the wall: forward input (the move key, or input pointing along the run)
            // keeps it. Turned further round, the move keys let go, a strafe away from the wall, crouch (a slide on landing,
            // see Update) or prone: off the wall
            bool onward = input.y > 0.2f || (hasInput && Vector3.Dot(wish, wallAlong) > 0.3f);
            bool away = Vector3.Dot(transform.right * input.x, wallNormal) > 0.5f;
            bool turned = Vector3.Dot(transform.forward, wallAlong) < MaxLookCos - 0.02f;
            if (!onward || away || turned || crouchPressed || GameInput.PronePressed || wallTime > wallRunMaxTime)
            {
                EndWallRun(false, away ? 1.6f : 0.8f);
                return false;
            }
            // keep to the wall: follow it as it curves gently; it ended (an outer corner, a doorway): fly on
            if (!ProbeWall(-wallNormal, 0.55f, out var hit)) { EndWallRun(false, 0.5f); return false; }
            Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
            if (Vector3.Dot(n, wallNormal) < 0.7f) { EndWallRun(false, 0.5f); return false; }
            wallNormal = n;
            wallPoint = hit.point;
            Vector3 along = Vector3.Cross(Vector3.up, n);
            if (Vector3.Dot(along, wallAlong) < 0f) along = -along;
            wallAlong = along;
            // the side of the view the wall is on (the camera and the body lean off it) follows the turning view
            WallSide = Mathf.Clamp(Vector3.Dot(-wallNormal, transform.right), -1f, 1f);

            // along: a slow loss. Height: level — what the jump onto the wall carried up or down settles within a fraction
            // of a second, at a steady rate — and only toward the end do the feet slip down, a little faster each moment
            wallSpeed = Mathf.Max(wallRunMinSpeed * SkyArena.SpeedScale, wallSpeed - 0.8f * dt);
            float slip = Mathf.InverseLerp(wallRunMaxTime * wallRunHold, wallRunMaxTime, wallTime);
            wallVy = Mathf.MoveTowards(wallVy, -wallRunSink * slip, wallRunLevel * dt);
            // the capsule stays just off the wall (pressed onto it each frame it rattled along it)
            float gap = hit.distance - (cc.radius + 0.04f);
            Vector3 stick = -wallNormal * Mathf.Clamp(gap * 12f, -2.5f, 2.5f);
            velocity = wallAlong * wallSpeed + stick + Vector3.up * wallVy;

            CollisionFlags flags = cc.Move(velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && wallVy > 0f) wallVy = 0f;
            airTime += dt;
            wasGrounded = false;
            lastVy = wallVy;
            IsSprinting = false; // the gun is up on a wall: shoot while running

            // feet on the floor: the run is over (the normal movement lands next frame); blocked ahead (an inner corner,
            // a crate against the wall): it stops
            // (this frame has moved already: handled)
            if (cc.isGrounded) { EndWallRun(false, 0f); return true; }
            if (wallTime > 0.12f && Vector3.Dot(cc.velocity, wallAlong) < wallSpeed * 0.4f) { EndWallRun(false, 0.6f); return true; }

            // steps along the wall: head bob, footsteps, body animation
            float sp = wallSpeed;
            stepAccum += sp * dt / 3.6f;
            StepPhase = stepAccum;
            int idx = Mathf.FloorToInt(stepAccum * 2f);
            if (idx != lastStepIndex) { lastStepIndex = idx; Footstep?.Invoke(); }
            Speed01 = Mathf.Clamp01(sp / sprintSpeed);
            return true;
        }

        /// <summary>cos of the largest turn of the view from the run's direction that keeps a wall run going.</summary>
        float MaxLookCos => Mathf.Cos(Mathf.Clamp(wallRunMaxLook, 10f, 170f) * Mathf.Deg2Rad);

        /// <summary>
        /// Off the wall with a jump: away from it and up, keeping most of the run's speed along it; looking away from the
        /// wall turns the jump toward the look (up to 60°: looking straight off the wall, the kick goes straight off it),
        /// so the next wall or a ledge can be aimed at.
        /// </summary>
        void WallJump()
        {
            Vector3 look = cameraRoot ? cameraRoot.forward : transform.forward;
            look.y = 0f;
            look = look.sqrMagnitude > 0.001f ? look.normalized : transform.forward;
            Vector3 h = wallAlong * wallSpeed * 0.9f + wallNormal * wallJumpOut * SkyArena.SpeedScale;
            if (Vector3.Dot(look, wallNormal) > 0.05f) h = Vector3.RotateTowards(h, look * h.magnitude, 60f * Mathf.Deg2Rad, 0f);
            float cap = Mathf.Max(wallSpeed, wallRunSpeed * SkyArena.SpeedScale) + 2f;
            if (h.magnitude > cap) h = h.normalized * cap;
            float up = wallJumpUp * Mathf.Max(0.6f, 1f - 0.1f * Mathf.Max(0, wallRuns - 1));
            EndWallRun(true, 0f);
            velocity = new Vector3(h.x, up, h.z);
            dashAir = true; // the kick keeps its speed above the run cap until it lands
            crouchToggled = false; // (a jump just after crouching off the wall) up again, like any jump
            slideQueued = false;
            lastJumpPressTime = -10f;
            lastGroundedTime = -10f;
            WallJumped?.Invoke();
            Jumped?.Invoke();
        }

        void EndWallRun(bool jumped, float push)
        {
            if (!IsWallRunning) { wallCoyote = false; return; }
            IsWallRunning = false;
            WallSide = 0f;
            lastWallNormal = wallNormal;
            lastWallPoint = wallPoint;
            lastWallValid = true;
            wallLeftTime = Time.time;
            wallCoyote = !jumped;
            // flying on: the run's speed along the wall, its vertical speed, a little away from it
            if (!jumped) velocity = wallAlong * wallSpeed + Vector3.up * wallVy + wallNormal * push;
            if (!jumped) dashAir = true;
        }

        /// <summary>One airborne frame with the current velocity (after a wall jump).</summary>
        void MoveFree(float dt)
        {
            velocity.y += gravity * dt;
            CollisionFlags flags = cc.Move(velocity * dt);
            if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f) velocity.y = 0f;
            airTime += dt;
            wasGrounded = cc.isGrounded;
            lastVy = velocity.y;
        }

        // ------------------------------------------------------------------ ability movement modes

        void UpdateNoclip(float dt)
        {
            Vector2 input = InputBlocked ? Vector2.zero : GameInput.Move;
            MoveInput = input;
            Vector3 fwd = cameraRoot ? cameraRoot.forward : transform.forward;
            Vector3 wish = transform.right * input.x + fwd * input.y;
            if (GameInput.JumpHeld) wish += Vector3.up;
            if (GameInput.CrouchHeld) wish -= Vector3.up;
            wish = Vector3.ClampMagnitude(wish, 1f);
            float spd = walkSpeed * (GameInput.SprintHeld ? 1.7f : 1.25f);
            velocity = Vector3.Lerp(velocity, wish * spd, 1f - Mathf.Exp(-8f * dt));
            transform.position += velocity * dt;
            IsGrounded = false;
            IsSprinting = false;
            IsCrouching = false;
            IsProne = false;
            Speed01 = Mathf.Clamp01(velocity.magnitude / sprintSpeed);
            stepAccum += velocity.magnitude * dt / 4f;
            StepPhase = stepAccum;
            wasGrounded = false;
            UpdateHeight(dt, Stance.Stand);
        }

        void UpdatePull(float dt)
        {
            pullTime -= dt;
            Vector3 tgt = pullTarget != null ? pullTarget() : transform.position;
            Vector3 from = transform.position + Vector3.up * 0.9f;
            Vector3 to = tgt - from;
            float d = to.magnitude;
            IsGrounded = false;
            IsSprinting = false;
            MoveInput = Vector2.zero;
            if (d < 1.1f || pullTime <= 0f || pullStuck > 0.25f)
            {
                velocity = (d > 0.01f ? to / d : Vector3.zero) * pullSpeed * 0.3f + Vector3.up * 3.5f;
                lastGroundedTime = -10f;
                wasGrounded = false;
                StopPull();
                return;
            }
            Vector3 step = to / d * Mathf.Min(pullSpeed * dt, d);
            Vector3 before = transform.position;
            cc.Move(step);
            float moved = (transform.position - before).magnitude;
            if (moved < pullSpeed * dt * 0.2f) pullStuck += dt; else pullStuck = 0f;
            velocity = step / dt;
            Speed01 = 1f;
            wasGrounded = false;
            UpdateHeight(dt, Stance.Stand);
        }

        // ------------------------------------------------------------------ stances

        void UpdateHeight(float dt, Stance stance)
        {
            float target = stance == Stance.Prone ? proneHeight : stance == Stance.Crouch ? crouchHeight : standHeight;
            if (IsVaulting) target = Mathf.Lerp(standHeight, crouchHeight, 0.55f);
            bool proneTransition = target < crouchHeight - 0.01f || currentHeight < crouchHeight - 0.01f;
            float speed = proneTransition ? proneLerp : IsSliding ? crouchLerp * 1.5f : crouchLerp;
            currentHeight = Mathf.Lerp(currentHeight, target, 1f - Mathf.Exp(-speed * dt));
            if (Mathf.Abs(currentHeight - target) < 0.002f) currentHeight = target;
            ApplyHeight();
            CrouchAmount = Mathf.InverseLerp(standHeight, crouchHeight, currentHeight);
            ProneAmount = Mathf.InverseLerp(crouchHeight, proneHeight, currentHeight);
        }

        public float EyeHeight
        {
            get
            {
                float h = currentHeight;
                if (h <= crouchHeight) return Mathf.Lerp(eyeProne, eyeCrouch, Mathf.InverseLerp(proneHeight, crouchHeight, h));
                return Mathf.Lerp(eyeCrouch, eyeStand, Mathf.InverseLerp(crouchHeight, standHeight, h));
            }
        }

        void ApplyHeight()
        {
            if (cc.enabled)
            {
                float h = Mathf.Max(currentHeight, cc.radius * 2f + 0.01f);
                cc.height = h;
                cc.center = new Vector3(0f, h * 0.5f, 0f);
            }
            if (cameraRoot) cameraRoot.localPosition = new Vector3(0f, EyeHeight, 0f);
        }

        bool CanFit(float height)
        {
            if (currentHeight >= height - 0.01f) return true;
            float r = cc.radius * 0.95f;
            Vector3 p = transform.position;
            return !CapsuleBlocked(p + Vector3.up * Mathf.Max(currentHeight - r, r + 0.05f), p + Vector3.up * (height - r), r);
        }

        bool CapsuleBlocked(Vector3 a, Vector3 b, float r)
        {
            int n = Physics.OverlapCapsuleNonAlloc(a, b, r, overlap, Physics.DefaultRaycastLayers, QTI);
            for (int i = 0; i < n; i++)
            {
                var c = overlap[i];
                if (c == cc || c.transform.IsChildOf(transform)) continue;
                if (c.attachedRigidbody && !c.attachedRigidbody.isKinematic && c.attachedRigidbody.mass < 2f) continue;
                return true;
            }
            return false;
        }

        bool Ray(Vector3 o, Vector3 d, out RaycastHit hit, float dist)
        {
            if (Physics.Raycast(o, d, out hit, dist, Physics.DefaultRaycastLayers, QTI))
            {
                if (hit.collider == cc || hit.collider.transform.IsChildOf(transform)) return false;
                if (hit.rigidbody && !hit.rigidbody.isKinematic && hit.rigidbody.mass < 2f) return false;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ vault / mantle

        bool TryVault(float hSpeed)
        {
            if (vaultCooldown > 0f || IsVaulting) return false;
            Vector3 fwd = transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 feet = transform.position;
            float r = cc.radius;

            RaycastHit wall = default;
            bool found = false;
            for (float h = minVaultHeight * 0.5f; h <= maxVaultHeight + 0.01f; h += 0.15f)
            {
                if (Ray(feet + Vector3.up * h, fwd, out wall, r + vaultReach)) { found = true; break; }
            }
            if (!found) return false;
            if (Mathf.Abs(wall.normal.y) > 0.4f) return false;
            Vector3 into = -wall.normal; into.y = 0f; into.Normalize();
            if (Vector3.Dot(into, fwd) < 0.45f) return false;

            Vector3 probe = wall.point + into * 0.12f;
            probe.y = feet.y + maxVaultHeight + 0.3f;
            if (!Ray(probe, Vector3.down, out RaycastHit top, maxVaultHeight + 0.3f)) return false;
            float height = top.point.y - feet.y;
            if (height < minVaultHeight || height > maxVaultHeight || top.normal.y < 0.7f) return false;
            float topY = top.point.y;

            Vector3 edge = new Vector3(wall.point.x, topY, wall.point.z) + into * 0.12f;
            if (CapsuleBlocked(edge + Vector3.up * 0.4f, edge + Vector3.up * 0.95f, 0.22f)) return false;

            float thickness = -1f;
            for (float d = 0.1f; d <= maxVaultThickness + 0.3f; d += 0.08f)
            {
                Vector3 p = wall.point + into * d; p.y = topY + 0.25f;
                if (!Ray(p, Vector3.down, out _, 0.45f)) { thickness = d; break; }
            }

            bool over = thickness > 0f && thickness <= maxVaultThickness;
            Vector3 end = Vector3.zero;
            if (over)
            {
                Vector3 far = wall.point + into * (thickness + r + 0.45f);
                far.y = topY + 0.2f;
                if (Ray(far, Vector3.down, out RaycastHit g, height + 3f)) end = g.point;
                else end = new Vector3(far.x, feet.y, far.z);
                end.y = Mathf.Max(end.y, topY - 1.2f);
                if (CapsuleBlocked(end + Vector3.up * (r + 0.08f), end + Vector3.up * (crouchHeight - r), r * 0.9f)) over = false;
            }
            if (!over)
            {
                end = new Vector3(wall.point.x, 0f, wall.point.z) + into * (r + 0.25f);
                end.y = topY + 0.02f;
                if (CapsuleBlocked(end + Vector3.up * (r + 0.08f), end + Vector3.up * (crouchHeight - r), r * 0.9f)) return false;
            }

            StartVault(feet, end, topY, into, over, height, hSpeed);
            return true;
        }

        void StartVault(Vector3 start, Vector3 end, float topY, Vector3 into, bool over, float height, float hSpeed)
        {
            IsSliding = false;
            slideQueued = false;
            IsVaulting = true;
            VaultIsMantle = !over;
            VaultT = 0f;
            vTime = 0f;
            VaultHeight01 = Mathf.InverseLerp(minVaultHeight, maxVaultHeight, height);
            VaultSide = -VaultSide;

            float peak = topY + (over ? 0.5f : 0.4f);
            vStart = start;
            vEnd = end;
            vC1 = Vector3.Lerp(start, end, over ? 0.2f : 0.04f); vC1.y = peak;
            vC2 = Vector3.Lerp(start, end, over ? 0.72f : 0.7f); vC2.y = over ? peak : peak - 0.05f;

            float speed01 = Mathf.Clamp01(hSpeed / sprintSpeed);
            float dist = Vector3.Distance(new Vector3(start.x, 0, start.z), new Vector3(end.x, 0, end.z));
            vDuration = over ? 0.42f + 0.14f * VaultHeight01 + dist * 0.035f : 0.55f + 0.28f * VaultHeight01;
            vDuration *= Mathf.Lerp(1.05f, 0.85f, speed01);

            float exitSpeed = over ? Mathf.Max(hSpeed, walkSpeed * 0.95f) : walkSpeed * 0.45f;
            vExitVel = into * exitSpeed;

            cc.enabled = false;
            VaultStarted?.Invoke(VaultIsMantle);
        }

        void UpdateVault(float dt)
        {
            vTime += dt;
            float t = Mathf.Clamp01(vTime / vDuration);
            VaultT = t;
            float e = Mathf.Lerp(t, t * t * (3f - 2f * t), 0.55f);
            float u = 1f - e;
            transform.position = u * u * u * vStart + 3f * u * u * e * vC1 + 3f * u * e * e * vC2 + e * e * e * vEnd;
            Speed01 = Mathf.Clamp01(vExitVel.magnitude / sprintSpeed);

            if (t >= 1f)
            {
                IsVaulting = false;
                cc.enabled = true;
                ApplyHeight();
                velocity = vExitVel;
                velocity.y = -1f;
                vaultCooldown = 0.25f;
                wasGrounded = true;
                Landed?.Invoke(VaultIsMantle ? 2.5f : 4.5f);
            }
        }
    }
}
