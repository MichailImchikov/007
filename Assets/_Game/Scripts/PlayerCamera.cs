using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS
{
    /// <summary>
    /// Mouse look + every procedural camera effect: recoil, view punch, shake, head bob,
    /// lean (Q/E) with wall collision, strafe/slide tilt, landing dip, vault roll and dynamic FOV.
    /// Hierarchy: Player(yaw) > CameraRoot(pitch) > Recoil > CameraFX > Main Camera.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class PlayerCamera : MonoBehaviour
    {
        public PlayerMotor motor;
        public Transform pitchPivot;
        public Transform recoilPivot;
        public Transform fxPivot;
        public Camera cam;

        [Header("FOV")]
        public float baseFov = 64f;
        public float sprintFovAdd = 6f;
        public float slideFovAdd = 9f;

        [Header("Look")]
        public float pitchLimit = 88f;

        [Header("Recoil")]
        public float recoilSnappiness = 20f;
        public float recoilReturn = 6.5f;

        [Header("Bob")]
        public float bobAmount = 1f;

        [Header("Lean")]
        public float leanAngle = 16f;
        public float leanPivot = 0.9f;
        public float leanSlide = 0.12f;

        public float AimBlend { get; set; }
        public float AdsFov { get; set; } = 48f;
        public Vector2 LastLookDelta { get; private set; }
        public float Lean { get; private set; }
        public float Yaw => yaw;
        public float Pitch => pitch;
        /// <summary>When locked, input is ignored and look is driven via SetLook (executions).</summary>
        public bool Locked { get; set; }
        /// <summary>0..1 death camera (falls to the ground and tilts).</summary>
        public float DeathAmount { get; set; }
        /// <summary>0..1 third-person chase camera (chicken trap).</summary>
        public float ThirdPerson { get; set; }
        /// <summary>0..1 slipped on a banana: camera on the floor looking at the sky.</summary>
        public float SlipAmount { get; set; }
        /// <summary>Extra FOV added by abilities (ghost mode, rocket kick).</summary>
        public float FovAdd { get; set; }
        readonly RaycastHit[] tpHits = new RaycastHit[8];

        float yaw, pitch;
        Vector3 recoilTarget, recoilCurrent;
        readonly Spring3 punchRot = new Spring3(240f, 17f);
        readonly Spring3 punchPos = new Spring3(200f, 16f);
        float roll, moveFov, fovKick, bobWeight, trauma;

        void Start()
        {
            yaw = transform.eulerAngles.y;
            moveFov = baseFov;
            LockCursor(true);
            if (motor)
            {
                motor.Landed += OnLanded;
                motor.Jumped += OnJumped;
                motor.VaultStarted += OnVault;
                motor.Footstep += OnFootstep;
                motor.SlideStarted += OnSlide;
                motor.ProneStarted += OnProne;
            }
        }

        void OnDestroy()
        {
            if (!motor) return;
            motor.Landed -= OnLanded;
            motor.Jumped -= OnJumped;
            motor.VaultStarted -= OnVault;
            motor.Footstep -= OnFootstep;
            motor.SlideStarted -= OnSlide;
            motor.ProneStarted -= OnProne;
        }

        public void AddRecoil(float up, float side) => recoilTarget += new Vector3(-up, side, 0f);
        public void Punch(Vector3 rotKick) => punchRot.Kick(rotKick);
        public void PunchPos(Vector3 kick) => punchPos.Kick(kick);
        public void Shake(float amount) => trauma = Mathf.Clamp01(Mathf.Max(trauma, amount));
        public void SetLook(float newYaw, float newPitch) { yaw = newYaw; pitch = Mathf.Clamp(newPitch, -pitchLimit, pitchLimit); }

        void OnLanded(float speed)
        {
            float s = Mathf.Clamp01((speed - 2f) / 10f);
            punchPos.Kick(new Vector3(0f, -0.9f - 2.6f * s, 0f));
            punchRot.Kick(new Vector3(18f + 70f * s, 0f, Random.Range(-12f, 12f) * s));
            ProcAudio.Play(Sfx.Land, 0.25f + 0.5f * s, 0.08f);
        }

        void OnJumped()
        {
            punchRot.Kick(new Vector3(-14f, 0f, 0f));
            punchPos.Kick(new Vector3(0f, 0.35f, 0f));
            ProcAudio.Play(Sfx.Step, 0.35f, 0.1f);
        }

        void OnVault(bool mantle)
        {
            fovKick += mantle ? 3f : 6f;
            punchRot.Kick(new Vector3(mantle ? 40f : 25f, 0f, 0f));
            ProcAudio.Play(Sfx.Vault, 0.7f, 0.08f);
        }

        void OnSlide()
        {
            fovKick += 4f;
            punchPos.Kick(new Vector3(0f, -1.2f, 0f));
            ProcAudio.Play(Sfx.Slide, 0.55f, 0.08f);
        }

        void OnProne()
        {
            punchRot.Kick(new Vector3(30f, 0f, 8f));
            ProcAudio.Play(Sfx.Prone, 0.6f, 0.06f);
        }

        void OnFootstep()
        {
            float v = motor.IsProne ? 0.1f : motor.IsCrouching ? 0.12f : (motor.IsSprinting ? 0.38f : 0.25f);
            ProcAudio.Play(motor.IsProne ? Sfx.Slide : Sfx.Step, v, 0.12f);
        }

        void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void HandleCursor()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            if (GameInput.ModalUI) { if (Cursor.lockState != CursorLockMode.None) LockCursor(false); return; }
            if (PauseMenu.Instance) { if (!PauseMenu.IsOpen && Cursor.lockState != CursorLockMode.Locked && m != null && m.leftButton.wasPressedThisFrame) LockCursor(true); return; }
            if (k != null && k.escapeKey.wasPressedThisFrame) LockCursor(false);
            else if (Cursor.lockState != CursorLockMode.Locked && m != null && m.leftButton.wasPressedThisFrame) LockCursor(true);
        }

        void Update()
        {
            HandleCursor();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // ---- look (sensitivity scales with zoom so scopes feel consistent)
            float zoomSens = Mathf.Clamp(AdsFov / baseFov, 0.12f, 1f) * 1.05f * GameSettings.AdsSensitivity;
            Vector2 look = Locked ? Vector2.zero : GameInput.LookDelta * Mathf.Lerp(1f, zoomSens, AimBlend);
            LastLookDelta = look;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, -pitchLimit, pitchLimit);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);

            // ---- recoil (kicks up fast, recovers smoothly)
            recoilTarget = Vector3.Lerp(recoilTarget, Vector3.zero, 1f - Mathf.Exp(-recoilReturn * dt));
            recoilCurrent = Vector3.Lerp(recoilCurrent, recoilTarget, 1f - Mathf.Exp(-recoilSnappiness * dt));
            recoilPivot.localRotation = Quaternion.Euler(recoilCurrent);

            Vector3 pr = punchRot.Step(dt);
            Vector3 pp = punchPos.Step(dt) * 0.5f;

            // ---- shake (explosions)
            trauma = Mathf.MoveTowards(trauma, 0f, dt * 1.3f);
            float tt = Time.time * 22f;
            float sh = trauma * trauma;
            Vector3 shakeRot = new Vector3(Mathf.PerlinNoise(tt, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, tt) - 0.5f, Mathf.PerlinNoise(tt, 4.1f) - 0.5f) * (12f * sh);

            // ---- head bob (walk) / crawl sway (prone)
            bool grounded = motor.IsGrounded && !motor.IsSliding && !motor.IsVaulting && !motor.Locked;
            bobWeight = Anim.Damp(bobWeight, grounded ? Mathf.Clamp01(motor.Speed01 * 1.6f) : 0f, 8f, dt);
            float ph = motor.StepPhase * Mathf.PI * 2f;
            float amp = bobWeight * bobAmount * Mathf.Lerp(1f, 0.2f, AimBlend) * (motor.IsSprinting ? 1.35f : 1f);
            float prone = motor.ProneAmount;
            Vector3 bobPos = new Vector3(Mathf.Sin(ph) * 0.018f, -Mathf.Cos(ph * 2f) * 0.022f * (1f - prone * 0.7f), 0f) * amp;
            Vector3 bobRot = new Vector3(-Mathf.Cos(ph * 2f) * 0.35f, Mathf.Sin(ph) * 1.2f * prone, Mathf.Sin(ph) * (0.5f + 2.5f * prone)) * amp;

            // ---- lean with collision against walls
            float leanInput = (GameInput.LeanRightHeld ? 1f : 0f) - (GameInput.LeanLeftHeld ? 1f : 0f);
            if (motor.IsSprinting || motor.IsSliding || motor.IsVaulting || Locked) leanInput = 0f;
            float maxLean = 1f;
            if (Mathf.Abs(leanInput) > 0.01f)
            {
                Vector3 side = transform.right * Mathf.Sign(leanInput);
                if (Physics.SphereCast(pitchPivot.position, 0.16f, side, out RaycastHit hit, 0.55f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(transform))
                    maxLean = Mathf.Clamp01((hit.distance - 0.04f) / 0.5f);
            }
            Lean = Anim.Damp(Lean, leanInput * maxLean, 9f, dt);
            float leanScale = 1f - prone * 0.6f;
            float la = -Lean * leanAngle * leanScale;
            float L = leanPivot * (1f - prone * 0.6f);
            Vector3 leanPos = new Vector3(Mathf.Sin(-la * Mathf.Deg2Rad) * L + Lean * leanSlide * leanScale,
                                          (Mathf.Cos(la * Mathf.Deg2Rad) - 1f) * L, 0f);

            float targetRoll = -motor.MoveInput.x * 1.5f * (1f - AimBlend * 0.6f);
            if (motor.IsSliding) targetRoll += 6f;
            roll = Anim.Damp(roll, targetRoll, 7f, dt);

            Vector3 vaultRot = Vector3.zero, vaultPos = Vector3.zero;
            if (motor.IsVaulting)
            {
                float t = motor.VaultT;
                float s = Mathf.Sin(t * Mathf.PI);
                if (motor.VaultIsMantle)
                {
                    float pull = Mathf.Sin(Mathf.Clamp01(t * 1.25f) * Mathf.PI);
                    vaultRot = new Vector3(13f * pull, 0f, motor.VaultSide * 4f * s);
                    vaultPos = new Vector3(0f, -0.06f * pull, 0f);
                }
                else
                {
                    vaultRot = new Vector3(8f * s, motor.VaultSide * 4f * s, motor.VaultSide * 14f * s * s);
                    vaultPos = new Vector3(motor.VaultSide * 0.05f * s, -0.04f * s, 0f);
                }
            }

            Vector3 deathPos = new Vector3(0.15f, -(motor.EyeHeight - 0.25f), 0.2f) * DeathAmount;
            Vector3 deathRot = new Vector3(-15f, 10f, 72f) * DeathAmount;
            float slip = Anim.Smooth(SlipAmount);
            deathPos += new Vector3(0f, -(motor.EyeHeight - 0.3f), -0.4f) * slip;
            deathRot += new Vector3(-75f, 0f, 18f) * slip;
            fxPivot.localPosition = pp + bobPos + vaultPos + leanPos + deathPos;
            fxPivot.localRotation = Quaternion.Euler(pr + bobRot + vaultRot + shakeRot + deathRot + new Vector3(0f, 0f, roll + la));

            // ---- third person chase camera with wall collision
            if (ThirdPerson > 0.001f)
            {
                float tp = Anim.Smooth(ThirdPerson);
                Vector3 local = new Vector3(0.25f, 0.45f, -2.8f);
                Vector3 dirW = fxPivot.TransformDirection(local);
                float len = dirW.magnitude;
                dirW /= len;
                float allowed = len;
                int n = Physics.SphereCastNonAlloc(fxPivot.position, 0.18f, dirW, tpHits, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (!tpHits[i].collider.transform.IsChildOf(transform) && tpHits[i].distance < allowed) allowed = Mathf.Max(0.2f, tpHits[i].distance - 0.05f);
                cam.transform.localPosition = local / len * allowed * tp;
                cam.transform.localRotation = Quaternion.Euler(8f * tp, 0f, 0f);
            }
            else if (cam.transform.localPosition != Vector3.zero)
            {
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.identity;
            }

            // ---- FOV
            baseFov = GameSettings.Fov;
            float targetFov = baseFov;
            if (motor.IsSprinting && motor.Speed01 > 0.6f) targetFov += sprintFovAdd;
            if (motor.IsSliding) targetFov += slideFovAdd;
            targetFov += Mathf.Clamp(motor.HorizontalSpeed - motor.sprintSpeed, 0f, 6f) * 1.2f; // bhop speed feel
            fovKick = Anim.Damp(fovKick, 0f, 5f, dt);
            moveFov = Anim.Damp(moveFov, targetFov, 6f, dt);
            cam.fieldOfView = Mathf.Lerp(moveFov + fovKick + FovAdd, AdsFov, AimBlend);
        }
    }
}
