using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS
{
    /// <summary>
    /// Thin wrapper over the Input System devices: keyboard/mouse with rebindable keys from
    /// <see cref="GameSettings"/>, plus gamepad. Hold/press modes for sprint, aim and crouch live here.
    /// </summary>
    public static class GameInput
    {
        public static float MouseSensitivity = 0.085f; // degrees per pixel (set from GameSettings)
        public static float StickSensitivity = 200f;   // degrees per second

        static Mouse M => Mouse.current;
        static Gamepad G => Gamepad.current;

        /// <summary>Set while dead / match over — gameplay input is ignored.</summary>
        public static bool Blocked;
        /// <summary>Inverted movement and look (chicken trap).</summary>
        public static bool Inverted;
        /// <summary>Gameplay input frozen (blue screen of death).</summary>
        public static bool Frozen;
        /// <summary>A gameplay UI (minesweeper) owns the mouse: the cursor stays free.</summary>
        public static bool ModalUI;
        /// <summary>The local player flies a drone: on-foot input is off, the drone reads <see cref="DroneKeys"/>.</summary>
        public static bool Piloting;
        public static bool Enabled => Cursor.lockState == CursorLockMode.Locked && !Blocked && !Frozen && !Piloting;
        public static bool DroneEnabled => Cursor.lockState == CursorLockMode.Locked && !Blocked && !Frozen && Piloting;

        /// <summary>Drone sticks and buttons (same bindings as on foot).</summary>
        public static class DroneKeys
        {
            static bool On => DroneEnabled;
            static Keyboard K => Keyboard.current;

            /// <summary>Movement like on foot: x = strafe (A/D), y = forward (W/S), gamepad left stick.</summary>
            public static Vector2 Cyclic
            {
                get
                {
                    if (!On) return Vector2.zero;
                    Vector2 v = Vector2.zero;
                    if (H(BindAction.MoveForward)) v.y += 1f;
                    if (H(BindAction.MoveBack)) v.y -= 1f;
                    if (H(BindAction.MoveRight)) v.x += 1f;
                    if (H(BindAction.MoveLeft)) v.x -= 1f;
                    if (G != null) v += G.leftStick.ReadValue();
                    return Vector2.ClampMagnitude(v, 1f);
                }
            }
            /// <summary>+1 up (Space), -1 down (Ctrl), gamepad A / B.</summary>
            public static float Throttle
            {
                get
                {
                    if (!On) return 0f;
                    float t = 0f;
                    if (H(BindAction.Jump) || (K != null && K.spaceKey.isPressed)) t += 1f;
                    if (H(BindAction.CrouchHold) || (K != null && (K.leftCtrlKey.isPressed || K.rightCtrlKey.isPressed))) t -= 1f;
                    if (G != null) { if (G.buttonSouth.isPressed) t += 1f; if (G.buttonEast.isPressed) t -= 1f; }
                    return Mathf.Clamp(t, -1f, 1f);
                }
            }
            /// <summary>Look in degrees this frame (x = turn, y = look up / down): mouse, gamepad right stick.</summary>
            public static Vector2 Look
            {
                get
                {
                    if (!On) return Vector2.zero;
                    Vector2 v = Vector2.zero;
                    if (M != null) v += M.delta.ReadValue() * MouseSensitivity;
                    if (G != null) { Vector2 s = G.rightStick.ReadValue(); v += s * s.magnitude * StickSensitivity * Time.deltaTime; }
                    if (GameSettings.InvertY) v.y = -v.y;
                    return v;
                }
            }
            public static bool FireHeld => On && (H(BindAction.Fire) || (G != null && G.rightTrigger.isPressed));
            public static bool DetonatePressed => On && (P(BindAction.Aim) || (G != null && G.leftTrigger.wasPressedThisFrame));
            public static bool SportHeld => On && (H(BindAction.Sprint) || (G != null && G.leftShoulder.isPressed));
            public static bool ThermalPressed => On && (P(BindAction.Ability) || (G != null && G.buttonNorth.wasPressedThisFrame));
            public static bool AbortPressed => On && (P(BindAction.Grenade) || (G != null && G.dpad.down.wasPressedThisFrame));
        }

        static bool H(BindAction a) => GameSettings.Held(a);
        static bool P(BindAction a) => GameSettings.Pressed(a);

        public static bool RotatePressed => Enabled && P(BindAction.Reload);
        public static bool AltFirePressed => Enabled && (P(BindAction.Aim) || (G != null && G.leftTrigger.wasPressedThisFrame));

        static Vector2 RawMove
        {
            get
            {
                Vector2 v = Vector2.zero;
                if (H(BindAction.MoveForward)) v.y += 1f;
                if (H(BindAction.MoveBack)) v.y -= 1f;
                if (H(BindAction.MoveRight)) v.x += 1f;
                if (H(BindAction.MoveLeft)) v.x -= 1f;
                if (G != null) v += G.leftStick.ReadValue();
                return Vector2.ClampMagnitude(v, 1f);
            }
        }

        public static Vector2 Move
        {
            get
            {
                if (!Enabled) return Vector2.zero;
                var v = RawMove;
                return Inverted ? -v : v;
            }
        }

        /// <summary>Look delta in degrees for this frame.</summary>
        public static Vector2 LookDelta
        {
            get
            {
                if (!Enabled) return Vector2.zero;
                Vector2 v = Vector2.zero;
                if (M != null) v += M.delta.ReadValue() * MouseSensitivity;
                if (G != null)
                {
                    Vector2 s = G.rightStick.ReadValue();
                    v += s * s.magnitude * StickSensitivity * Time.deltaTime;
                }
                if (GameSettings.InvertY) v.y = -v.y;
                return Inverted ? -v : v;
            }
        }

        // ---- hold / press latches (evaluated once per frame)
        static int latchFrame = -1;
        static bool sprintOn, aimOn;
        static float sprintStopT;

        /// <summary>Sprint latch (toggle mode) — read by the HUD.</summary>
        public static bool SprintLatched => sprintOn;

        static void UpdateLatches()
        {
            if (latchFrame == Time.frameCount) return;
            latchFrame = Time.frameCount;
            if (!Enabled) { sprintOn = false; aimOn = false; return; }

            if (GameSettings.SprintToggle)
            {
                if (P(BindAction.Sprint) || (G != null && G.leftStickButton.wasPressedThisFrame)) { sprintOn = !sprintOn; sprintStopT = 0f; }
                // stop running forward for a moment -> sprint switches off
                if (sprintOn && RawMove.y < 0.3f) { sprintStopT += Time.deltaTime; if (sprintStopT > 0.25f) sprintOn = false; }
                else sprintStopT = 0f;
            }
            else sprintOn = false;

            if (GameSettings.AimToggle)
            {
                if (P(BindAction.Aim) || (G != null && G.leftTrigger.wasPressedThisFrame)) aimOn = !aimOn;
                bool running = GameSettings.SprintToggle ? sprintOn : H(BindAction.Sprint) && RawMove.y > 0.3f;
                if (running) aimOn = false;
            }
            else aimOn = false;
        }

        public static bool FireHeld => Enabled && (H(BindAction.Fire) || (G != null && G.rightTrigger.isPressed));
        public static bool FirePressed => Enabled && (P(BindAction.Fire) || (G != null && G.rightTrigger.wasPressedThisFrame));

        /// <summary>Automated tests (ADS screenshots): aim as if the button were held.</summary>
        public static bool ForceAim;

        public static bool AimHeld
        {
            get
            {
                if (ForceAim) return true;
                if (!Enabled) return false;
                if (GameSettings.AimToggle) { UpdateLatches(); return aimOn; }
                return H(BindAction.Aim) || (G != null && G.leftTrigger.isPressed);
            }
        }

        public static bool SprintHeld
        {
            get
            {
                if (!Enabled) return false;
                if (GameSettings.SprintToggle) { UpdateLatches(); return sprintOn; }
                return H(BindAction.Sprint) || (G != null && G.leftStickButton.isPressed);
            }
        }

        public static bool ReloadPressed => Enabled && (P(BindAction.Reload) || (G != null && G.buttonWest.wasPressedThisFrame));
        public static bool JumpPressed => Enabled && (P(BindAction.Jump) || (G != null && G.buttonSouth.wasPressedThisFrame));
        public static bool JumpHeld => Enabled && (H(BindAction.Jump) || (G != null && G.buttonSouth.isPressed));
        public static bool CrouchHeld => Enabled && !GameSettings.CrouchHoldIsToggle && H(BindAction.CrouchHold);
        public static bool CrouchHoldPressed => Enabled && !GameSettings.CrouchHoldIsToggle && P(BindAction.CrouchHold);
        public static bool CrouchTogglePressed => Enabled && (P(BindAction.CrouchToggle) || (GameSettings.CrouchHoldIsToggle && P(BindAction.CrouchHold))
                                                              || (G != null && G.buttonEast.wasPressedThisFrame));
        public static bool FireModePressed => Enabled && (P(BindAction.FireMode) || (G != null && G.dpad.up.wasPressedThisFrame));
        public static bool LeanLeftHeld => Enabled && (H(BindAction.LeanLeft) || (G != null && G.dpad.left.isPressed));
        public static bool LeanRightHeld => Enabled && (H(BindAction.LeanRight) || (G != null && G.dpad.right.isPressed));
        public static bool PronePressed => Enabled && (P(BindAction.Prone) || (G != null && G.dpad.down.wasPressedThisFrame));
        public static bool MeleePressed => Enabled && (P(BindAction.Melee) || (G != null && G.rightStickButton.wasPressedThisFrame));
        public static bool GrenadePressed => Enabled && (P(BindAction.Grenade) || (G != null && G.rightShoulder.wasPressedThisFrame));
        public static bool GrenadeHeld => H(BindAction.Grenade) || (G != null && G.rightShoulder.isPressed);
        public static bool PickupPressed => Enabled && (P(BindAction.Pickup) || (G != null && G.buttonNorth.wasPressedThisFrame));
        public static bool Slot1Pressed => Enabled && P(BindAction.Primary);
        public static bool Slot2Pressed => Enabled && P(BindAction.Secondary);
        public static bool AbilityHeld => Enabled && (H(BindAction.Ability) || (G != null && G.selectButton.isPressed));
        public static bool AbilityPressed => Enabled && (P(BindAction.Ability) || (G != null && G.selectButton.wasPressedThisFrame));
        public static bool SwapPressed => Enabled && ((M != null && Mathf.Abs(M.scroll.ReadValue().y) > 0.1f) || (G != null && G.leftShoulder.wasPressedThisFrame));
        /// <summary>Scoreboard (works while dead too).</summary>
        public static bool ScoreboardHeld => H(BindAction.Scoreboard) || (G != null && G.startButton.isPressed);
        /// <summary>Melee key held — held long enough it toggles the flashlight.</summary>
        public static bool FlashlightHeld => Enabled && (H(BindAction.Melee) || (G != null && G.rightStickButton.isPressed));
    }
}
