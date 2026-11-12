using System;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// First-person movement: walk / sprint / crouch / prone / slide / jump, Source-style
    /// bunny hopping (air strafing + no friction on the landing frame), plus vaulting over
    /// low obstacles and mantling onto ledges along a smooth bezier path.
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

        [Header("Bunny hop / air strafe")]
        public bool autoBhop = true;
        public float airAccelerate = 45f;
        public float airSpeedCap = 0.9f;
        public float maxBhopSpeed = 14f;

        [Header("Stances")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.1f;
        public float proneHeight = 0.72f;
        public float eyeStand = 1.68f;
        public float eyeCrouch = 1.0f;
        public float eyeProne = 0.4f;
        public float crouchLerp = 12f;
        public float proneLerp = 5f;

        [Header("Slide")]
        public float slideBoost = 10f;
        public float slideFriction = 7f;
        public float slideMinSpeed = 3.2f;
        public float slideMaxTime = 1.1f;

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

        /// <summary>When locked (e.g. during an execution) the motor ignores input and something else drives the transform.</summary>
        public bool Locked
        {
            get => locked;
            set
            {
                if (locked == value) return;
                locked = value;
                velocity = Vector3.zero;
                IsSliding = false;
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
                IsSliding = false;
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
            IsSliding = false;
            IsVaulting = false;
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
            velocity += v;
            if (v.y > 0.5f) { lastGroundedTime = -10f; IsGrounded = false; }
        }

        public event Action<float> Landed;
        public event Action Jumped;
        public event Action<bool> VaultStarted;
        public event Action Footstep;
        public event Action SlideStarted;
        public event Action ProneStarted;

        enum Stance { Stand, Crouch, Prone }

        CharacterController cc;
        Vector3 velocity;
        float currentHeight;
        bool crouchToggled, proneToggled, locked;
        float lastGroundedTime = -10f, lastJumpPressTime = -10f;
        float slideTimer;
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
            IsSliding = false;
            IsVaulting = false;
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
            if (GameInput.JumpPressed && !InputBlocked) lastJumpPressTime = Time.time;

            Vector3 hv = new Vector3(velocity.x, 0f, velocity.z);
            float hSpeed = hv.magnitude;

            // ---- stance input
            bool crouchPressed = GameInput.CrouchTogglePressed || GameInput.CrouchHoldPressed;
            if (GameInput.PronePressed)
            {
                if (proneToggled) proneToggled = false;
                else if (IsGrounded && !IsSliding) { proneToggled = true; crouchToggled = false; ProneStarted?.Invoke(); }
            }
            if (GameInput.CrouchTogglePressed)
            {
                if (proneToggled) { proneToggled = false; crouchToggled = true; }
                else crouchToggled = !crouchToggled;
            }

            // ---- slide start (crouch while sprinting)
            if (!IsSliding && crouchPressed && IsGrounded && IsSprinting && hSpeed > walkSpeed + 0.5f)
            {
                IsSliding = true;
                slideTimer = 0f;
                crouchToggled = false;
                Vector3 dir = hSpeed > 0.1f ? hv / hSpeed : transform.forward;
                hv = dir * Mathf.Max(hSpeed + 2.5f, slideBoost);
                SlideStarted?.Invoke();
            }

            bool sprintInput = GameInput.SprintHeld && input.y > 0.3f && !BlockSprint && !InputBlocked;
            if (sprintInput && !IsSliding) { crouchToggled = false; proneToggled = false; }

            Stance want = proneToggled ? Stance.Prone
                        : (crouchToggled || GameInput.CrouchHeld || IsSliding) ? Stance.Crouch : Stance.Stand;
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
            target *= SpeedMultiplier;

            // ---- jump / vault decisions
            bool jumpPressedBuffered = Time.time - lastJumpPressTime <= jumpBuffer;
            if (jumpPressedBuffered && !IsProne && TryVault(hSpeed)) { lastJumpPressTime = -10f; return; }
            if (jumpPressedBuffered && IsProne) { proneToggled = false; crouchToggled = true; lastJumpPressTime = -10f; jumpPressedBuffered = false; }

            bool wantJump = jumpPressedBuffered || (autoBhop && GameInput.JumpHeld && !InputBlocked);
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
                float spd = Mathf.Max(0f, hv.magnitude - slideFriction * dt);
                Vector3 dir = hv.sqrMagnitude > 0.0001f ? hv.normalized : transform.forward;
                if (hasInput) dir = Vector3.RotateTowards(dir, wish.normalized, 0.9f * dt, 0f);
                hv = dir * spd;
                if (spd < slideMinSpeed || slideTimer > slideMaxTime || !IsGrounded) IsSliding = false;
            }
            else if (IsGrounded && !jumpNow)
            {
                float accel = hasInput ? groundAccel : groundDecel;
                hv = Vector3.Lerp(hv, wish * target, 1f - Mathf.Exp(-accel * dt));
            }
            else if (hasInput)
            {
                // Quake/Source air acceleration: strafe + turn to gain speed
                Vector3 wd = wish.normalized;
                float cap = Mathf.Min(target, airSpeedCap);
                float current = Vector3.Dot(hv, wd);
                float add = cap - current;
                if (add > 0f) hv += wd * Mathf.Min(airAccelerate * target * dt, add);
            }
            if (hv.magnitude > maxBhopSpeed) hv = hv.normalized * maxBhopSpeed;

            if (jumpNow)
            {
                crouchToggled = false;
                IsSliding = false;
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
