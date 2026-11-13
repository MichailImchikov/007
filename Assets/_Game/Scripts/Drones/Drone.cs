using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPS
{
    public enum DroneFlightMode { Stab, Manual, Acro }

    /// <summary>
    /// FPV strike drone with quadcopter physics: four motors with spool-up lag push at their real
    /// positions (attitude comes from thrust differences, yaw from rotor drag torque), a flight controller
    /// (angle / rate loops, motor mixer with saturation), quadratic air drag, wind, prop wash, battery sag
    /// and a radio link that weakens with distance and walls. Carries a small gun and a contact-fused
    /// warhead. The pilot (player or bot) writes the stick commands every frame.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class Drone : MonoBehaviour, IDamageable
    {
        public static readonly List<Drone> All = new List<Drone>();

        public const string WeaponGun = "Дрон (пулемёт)";
        public const string WeaponBoom = "FPV-дрон";

        // ---- airframe (≈ 7" combat FPV with a warhead)
        public const float Mass = 1.15f;
        const float ArmL = 0.115f;              // motor offset along local x and z
        const float MotorMax = 9.4f;            // N per motor at full battery (TWR ≈ 3.3)
        const float MotorIdle = 0.22f;          // N, air-mode idle
        const float MotorTau = 0.035f;          // s, motor + prop spool-up
        const float YawCoef = 0.018f;           // rotor drag torque per newton of thrust
        static readonly Vector3 Inertia = new Vector3(0.0105f, 0.019f, 0.0105f);
        static readonly float[] MX = { -1f, 1f, -1f, 1f }, MZ = { 1f, 1f, -1f, -1f }, MS = { 1f, -1f, -1f, 1f };

        /// <summary>Ground speed for full WASD (m/s): normal and with Shift.</summary>
        public const float MaxSpeed = 15f, MaxSpeedSport = 24f;
        public const float MaxHealth = 30f;
        public const int MagSize = 60;
        public const float ThermalDuration = 10f, ThermalCooldown = 10f;
        public const float SignalRange = 380f;
        public const float BlastRadius = 6.5f, BlastDamage = 175f;

        // ---- public state
        public DroneOperator Operator { get; private set; }
        public Combatant Owner => Operator ? Operator.Combatant : null;
        public Rigidbody Body => rb;
        public Camera Cam => cam;
        public DroneFlightMode Mode { get; private set; } = DroneFlightMode.Stab;
        public bool Dead { get; private set; }
        public bool Armed { get; private set; } = true;
        public float Health { get; private set; } = MaxHealth;
        public float Battery { get; private set; } = 1f;
        public float Voltage => Mathf.Lerp(13.4f, 16.8f, Mathf.Pow(Battery, 0.7f)) - ThrottleFraction * 1.3f; // 4S pack with sag
        public float Signal { get; private set; } = 1f;
        public bool LinkLost => linkLost;
        public bool OutOfBounds { get; private set; }
        public int Ammo { get; private set; } = MagSize;
        public float ThermalLeft { get; private set; }
        public float ThermalCd { get; private set; }
        public bool Thermal => ThermalLeft > 0f;
        public float ThrottleFraction { get; private set; }
        public float CamTilt { get; private set; } = -5f;
        public float Age { get; private set; }
        public float HitFlash { get; private set; }
        public float AltitudeAGL { get; private set; } = 99f;
        public float Heading => transform.eulerAngles.y;
        public float HeadingTarget => yawTarget;
        public Vector3 Velocity => rb ? rb.linearVelocity : Vector3.zero;
        public bool Sport => cmdSport;
        public bool IsLocalView { get; set; }
        /// <summary>The operator was hit while flying (shown on the OSD).</summary>
        public float OperatorHitT { get; set; } = -99f;
        /// <summary>Why the flight ended (first cause wins): shown to the pilot.</summary>
        public string EndReason { get; private set; } = "";

        // ---- commands, written by the pilot every frame
        /// <summary>x = roll right, y = pitch forward (-1..1).</summary>
        [System.NonSerialized] public Vector2 cmdCyclic;
        /// <summary>Stab: climb / descend rate; Manual / Acro: moves the throttle stick.</summary>
        [System.NonSerialized] public float cmdThrottle;
        [System.NonSerialized] public bool cmdSport, cmdFire;
        /// <summary>Drives the commands (player input or bot AI). Called from Update.</summary>
        public System.Action<Drone, float> pilot;

        Rigidbody rb;
        Camera cam;
        AudioListener listener;
        AudioSource buzz;
        BoxCollider col;
        readonly float[] thrust = new float[4], cmdT = new float[4], eff = { 1f, 1f, 1f, 1f };
        readonly Transform[] props = new Transform[4];
        readonly Renderer[] propDiscs = new Renderer[4], propBlades = new Renderer[4];
        readonly float[] propAngle = new float[4];
        Renderer[] ledRenderers;
        public readonly List<MeshFilter> meshes = new List<MeshFilter>();
        float yawTarget, thrStick = 0.45f, holdAlt = float.NaN;
        Quaternion acroSet = Quaternion.identity;
        float fireCd, noiseSeed;
        bool linkLost;
        Vector3 lastVel;
        readonly RaycastHit[] hits = new RaycastHit[16];

        // ------------------------------------------------------------------ spawn

        public static Drone Launch(DroneOperator op, Vector3 pos, float yaw, Vector3 velocity)
        {
            var go = new GameObject("Drone_" + (op && op.Combatant ? op.Combatant.displayName : "?"));
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var d = go.AddComponent<Drone>();
            d.Operator = op;
            d.Build();
            d.yawTarget = yaw;
            d.acroSet = go.transform.rotation;
            d.holdAlt = pos.y + 2f; // the throw carries it up a bit, then it holds there
            d.rb.linearVelocity = velocity;
            // hover thrust from the first frame: the drone is thrown up and catches itself
            float hover = Mass * 9.81f / 4f;
            for (int i = 0; i < 4; i++) d.thrust[i] = hover * 0.8f;
            d.thrStick = Mathf.Sqrt(Mass * 9.81f / (MotorMax * 4f));
            if (op && op.Combatant)
                foreach (var c in op.Combatant.GetComponentsInChildren<Collider>(true))
                    if (c) Physics.IgnoreCollision(d.col, c, true);
            foreach (var g in DroneMode.Ghosts) if (g) Physics.IgnoreCollision(d.col, g, true);
            return d;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Build()
        {
            noiseSeed = Random.value * 100f;
            rb = gameObject.AddComponent<Rigidbody>();
            rb.mass = Mass;
            rb.useGravity = true;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.6f;
            rb.automaticCenterOfMass = false;
            rb.centerOfMass = Vector3.zero;
            rb.automaticInertiaTensor = false;
            rb.inertiaTensor = Inertia;
            rb.inertiaTensorRotation = Quaternion.identity;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxAngularVelocity = 40f;

            col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -0.015f, 0.01f);
            col.size = new Vector3(0.38f, 0.11f, 0.4f);

            var carbon = AbilityProps.Mat(new Color(0.07f, 0.07f, 0.08f), 0f, 0.55f, 0.2f);
            var metal = AbilityProps.Mat(new Color(0.32f, 0.33f, 0.35f), 0f, 0.7f, 0.9f);
            var bell = AbilityProps.Mat(new Color(0.75f, 0.32f, 0.12f), 0f, 0.6f, 0.8f);
            var prop = AbilityProps.Mat(new Color(0.1f, 0.1f, 0.12f), 0f, 0.4f, 0f);
            var olive = AbilityProps.Mat(new Color(0.24f, 0.27f, 0.17f), 0f, 0.35f, 0.1f);
            var pack = AbilityProps.Mat(new Color(0.12f, 0.2f, 0.42f), 0f, 0.5f, 0.1f);
            var tape = AbilityProps.Mat(new Color(0.55f, 0.5f, 0.38f), 0f, 0.2f, 0f);
            var glass = AbilityProps.Mat(new Color(0.02f, 0.02f, 0.03f), 0f, 0.95f, 0.3f);
            var red = AbilityProps.Mat(new Color(1f, 0.1f, 0.05f), 4f);
            var green = AbilityProps.Mat(new Color(0.1f, 1f, 0.2f), 3f);
            var disc = FX.MakeParticleMat("DronePropDisc", FX.SoftCircle(64, 0.35f), false, new Color(0.15f, 0.15f, 0.17f, 0.32f), true);

            var model = new GameObject("Model").transform;
            model.SetParent(transform, false);

            Part(PrimitiveType.Cube, model, "Plate", new Vector3(0f, 0f, 0f), Vector3.zero, new Vector3(0.085f, 0.008f, 0.15f), carbon);
            Part(PrimitiveType.Cube, model, "TopPlate", new Vector3(0f, 0.032f, 0f), Vector3.zero, new Vector3(0.07f, 0.004f, 0.12f), carbon);
            for (int s = -1; s <= 1; s += 2)
                Part(PrimitiveType.Cube, model, "Arm", new Vector3(0f, -0.002f, 0f), new Vector3(0f, 45f * s, 0f), new Vector3(0.022f, 0.008f, 0.335f), carbon);
            for (int i = 0; i < 4; i++)
            {
                Vector3 mp = new Vector3(MX[i] * ArmL, 0.004f, MZ[i] * ArmL);
                Part(PrimitiveType.Cylinder, model, "Stator", mp + Vector3.up * 0.008f, Vector3.zero, new Vector3(0.03f, 0.008f, 0.03f), metal);
                Part(PrimitiveType.Cylinder, model, "Bell", mp + Vector3.up * 0.019f, Vector3.zero, new Vector3(0.032f, 0.006f, 0.032f), bell);
                var hub = new GameObject("Prop" + i).transform;
                hub.SetParent(model, false);
                hub.localPosition = mp + Vector3.up * 0.03f;
                props[i] = hub;
                var b1 = Part(PrimitiveType.Cube, hub, "Blades", Vector3.zero, new Vector3(0f, 0f, 6f), new Vector3(0.165f, 0.002f, 0.016f), prop);
                propBlades[i] = b1.GetComponent<Renderer>();
                var dq = Part(PrimitiveType.Quad, hub, "Disc", Vector3.up * 0.001f, new Vector3(90f, 0f, 0f), new Vector3(0.175f, 0.175f, 1f), disc);
                propDiscs[i] = dq.GetComponent<Renderer>();
                propDiscs[i].enabled = false;
            }
            // stack, battery with straps, FPV camera, VTX antenna
            Part(PrimitiveType.Cube, model, "Stack", new Vector3(0f, 0.016f, 0f), Vector3.zero, new Vector3(0.04f, 0.022f, 0.04f), metal);
            Part(PrimitiveType.Cube, model, "Battery", new Vector3(0f, 0.054f, -0.005f), Vector3.zero, new Vector3(0.038f, 0.034f, 0.085f), pack);
            Part(PrimitiveType.Cube, model, "Strap", new Vector3(0f, 0.055f, 0.02f), Vector3.zero, new Vector3(0.042f, 0.037f, 0.012f), carbon);
            Part(PrimitiveType.Cube, model, "Strap", new Vector3(0f, 0.055f, -0.03f), Vector3.zero, new Vector3(0.042f, 0.037f, 0.012f), carbon);
            Part(PrimitiveType.Cube, model, "CamBody", new Vector3(0f, 0.016f, 0.072f), Vector3.zero, new Vector3(0.022f, 0.022f, 0.02f), carbon);
            Part(PrimitiveType.Cylinder, model, "Lens", new Vector3(0f, 0.017f, 0.085f), new Vector3(90f, 0f, 0f), new Vector3(0.014f, 0.006f, 0.014f), glass);
            Part(PrimitiveType.Cylinder, model, "Antenna", new Vector3(0f, 0.04f, -0.08f), new Vector3(-35f, 0f, 0f), new Vector3(0.004f, 0.04f, 0.004f), carbon);
            Part(PrimitiveType.Sphere, model, "AntennaCap", new Vector3(0f, 0.072f, -0.126f), Vector3.zero, new Vector3(0.016f, 0.016f, 0.016f), red);
            // warhead under the frame (taped on) and the gun barrel beside it
            Part(PrimitiveType.Cylinder, model, "Warhead", new Vector3(0f, -0.035f, 0.01f), new Vector3(90f, 0f, 0f), new Vector3(0.05f, 0.07f, 0.05f), olive);
            Part(PrimitiveType.Capsule, model, "WarheadNose", new Vector3(0f, -0.035f, 0.08f), new Vector3(90f, 0f, 0f), new Vector3(0.047f, 0.03f, 0.047f), olive);
            Part(PrimitiveType.Cylinder, model, "Fuse", new Vector3(0f, -0.035f, 0.115f), new Vector3(90f, 0f, 0f), new Vector3(0.012f, 0.012f, 0.012f), metal);
            Part(PrimitiveType.Cube, model, "Tape", new Vector3(0f, -0.022f, 0.01f), Vector3.zero, new Vector3(0.056f, 0.032f, 0.02f), tape);
            Part(PrimitiveType.Cube, model, "Tape", new Vector3(0f, -0.022f, -0.035f), Vector3.zero, new Vector3(0.056f, 0.032f, 0.02f), tape);
            Part(PrimitiveType.Cube, model, "GunBody", new Vector3(0.045f, -0.02f, 0.03f), Vector3.zero, new Vector3(0.022f, 0.026f, 0.08f), metal);
            Part(PrimitiveType.Cylinder, model, "Barrel", new Vector3(0.045f, -0.016f, 0.1f), new Vector3(90f, 0f, 0f), new Vector3(0.008f, 0.045f, 0.008f), carbon);
            var l1 = Part(PrimitiveType.Cube, model, "LedL", new Vector3(-0.03f, 0.004f, -0.074f), Vector3.zero, new Vector3(0.01f, 0.005f, 0.006f), red);
            var l2 = Part(PrimitiveType.Cube, model, "LedR", new Vector3(0.03f, 0.004f, -0.074f), Vector3.zero, new Vector3(0.01f, 0.005f, 0.006f), green);
            ledRenderers = new[] { l1.GetComponent<Renderer>(), l2.GetComponent<Renderer>() };

            // FPV camera (hard-mounted, tilt servo) — enabled only for the local pilot
            var camGO = new GameObject("FPVCam");
            camGO.transform.SetParent(transform, false);
            camGO.transform.localPosition = new Vector3(0f, 0.017f, 0.095f);
            cam = camGO.AddComponent<Camera>();
            cam.nearClipPlane = 0.03f;
            cam.farClipPlane = 600f;
            cam.fieldOfView = 84f;
            var acd = camGO.AddComponent<UniversalAdditionalCameraData>();
            acd.renderPostProcessing = true;
            acd.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            cam.enabled = false;
            listener = camGO.AddComponent<AudioListener>();
            listener.enabled = false;

            buzz = gameObject.AddComponent<AudioSource>();
            buzz.clip = DroneAudio.Buzz;
            buzz.loop = true;
            buzz.spatialBlend = 1f;
            buzz.minDistance = 3.5f;
            buzz.maxDistance = 140f;
            buzz.rolloffMode = AudioRolloffMode.Logarithmic;
            buzz.dopplerLevel = 0.6f;
            buzz.spread = 40f;
            buzz.volume = 0f;
            buzz.time = Random.value * buzz.clip.length;
            buzz.Play();
        }

        GameObject Part(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var c = go.GetComponent<Collider>();
            if (c) DestroyImmediate(c);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = type == PrimitiveType.Quad ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (type != PrimitiveType.Quad) meshes.Add(go.GetComponent<MeshFilter>());
            return go;
        }

        // ------------------------------------------------------------------ control API

        /// <summary>Mouse / stick look: yaw + camera tilt (Stab, Manual) or pitch + yaw rates (Acro).</summary>
        public void Steer(Vector2 look, float dt)
        {
            if (Dead) return;
            if (Mode == DroneFlightMode.Acro)
            {
                float rollRate = 300f * cmdCyclic.x;
                acroSet = acroSet * Quaternion.Euler(-look.y * 1.2f, look.x * 1.2f, -rollRate * dt);
                // like an I-term limit: the setpoint never runs far ahead of the airframe
                acroSet = Quaternion.RotateTowards(rb.rotation, acroSet, 35f);
            }
            else
            {
                yawTarget += look.x;
                CamTilt = Mathf.Clamp(CamTilt + look.y, -85f, 60f);
            }
        }

        public const int OwnModelLayer = 31;

        /// <summary>The pilot's own camera: the frame, props and gun are not drawn into its own video.</summary>
        public void SetFirstPerson(bool on)
        {
            var model = transform.Find("Model");
            if (model) foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = on ? OwnModelLayer : 0;
            cam.cullingMask = on ? ~(1 << OwnModelLayer) : ~0;
        }

        public void SetHeading(float yaw) => yawTarget = yaw;
        /// <summary>Camera elevation in degrees (world-relative in stabilized mode).</summary>
        public void SetCamTilt(float t) => CamTilt = Mathf.Clamp(t, -85f, 60f);

        /// <summary>
        /// Stabilized mode: a gimbal keeps the view where the mouse points (pitch held in world space,
        /// a little of the frame's roll shows through). Other modes: camera hard-mounted with a tilt.
        /// </summary>
        void AimCamera()
        {
            if (Mode == DroneFlightMode.Stab)
            {
                float roll = Mathf.Asin(Mathf.Clamp(transform.right.y, -1f, 1f)) * Mathf.Rad2Deg;
                cam.transform.rotation = Quaternion.Euler(-CamTilt, transform.eulerAngles.y, roll * 0.3f);
            }
            else cam.transform.localRotation = Quaternion.Euler(-CamTilt, 0f, 0f);
        }

        void LateUpdate()
        {
            if (!Dead && cam) AimCamera(); // after interpolation moved the frame this frame
        }

        public void CycleMode()
        {
            if (Dead) return;
            Mode = (DroneFlightMode)(((int)Mode + 1) % 3);
            // bumpless transfer between controllers
            yawTarget = Heading;
            acroSet = rb.rotation;
            thrStick = Mathf.Sqrt(Mathf.Clamp01(ThrottleFraction));
            if (Mode == DroneFlightMode.Acro) CamTilt = 25f;
            ProcAudio.Play(Sfx.Click, 0.4f);
        }

        public bool ToggleThermal()
        {
            if (Dead) return false;
            if (ThermalLeft > 0f) { ThermalLeft = 0f; ThermalCd = ThermalCooldown; return true; }
            if (ThermalCd > 0f) return false;
            ThermalLeft = ThermalDuration;
            if (IsLocalView) ProcAudio.Play(Sfx.Switch, 0.5f);
            return true;
        }

        /// <summary>Radio link cut (operator killed / hurt): failsafe, the motors stop.</summary>
        public void CutLink()
        {
            if (Dead) return;
            if (EndReason.Length == 0) EndReason = Battery <= 0f ? "Батарея разряжена" : "Связь с дроном потеряна";
            linkLost = true;
            Armed = false;
            Signal = 0f;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            if (Dead) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Age += dt;
            HitFlash = Mathf.Max(0f, HitFlash - dt * 2.5f);

            cmdFire = false;
            if (Armed && !linkLost && pilot != null) pilot(this, dt);
            else { cmdCyclic = Vector2.zero; cmdThrottle = 0f; }

            if (ThermalLeft > 0f) { ThermalLeft -= dt; if (ThermalLeft <= 0f) { ThermalLeft = 0f; ThermalCd = ThermalCooldown; } }
            else if (ThermalCd > 0f) ThermalCd = Mathf.Max(0f, ThermalCd - dt);

            AimCamera();

            UpdateGun(dt);
            UpdateLink(dt);
            UpdateVisuals(dt);
            CheckFuse();
        }

        void FixedUpdate()
        {
            if (Dead || !rb) return;
            float dt = Time.fixedDeltaTime;
            Quaternion rot = rb.rotation;
            Vector3 up = rot * Vector3.up;
            Vector3 v = rb.linearVelocity;

            // battery: drain grows with power; sagging voltage lowers the available thrust
            float total = thrust[0] + thrust[1] + thrust[2] + thrust[3];
            float sag = 0.8f + 0.2f * Battery;
            float tMax = MotorMax * sag;
            ThrottleFraction = Mathf.Clamp01(total / (MotorMax * 4f));
            if (Armed) Battery = Mathf.Max(0f, Battery - dt * (0.0035f + 0.014f * Mathf.Pow(ThrottleFraction, 1.5f)));
            if (Battery <= 0f && Armed) { Armed = false; if (EndReason.Length == 0) EndReason = "Батарея разряжена"; }

            if (Armed)
            {
                bool sport = cmdSport;
                float maxTilt = sport ? 50f : 35f;
                Vector3 wDes;
                if (Mode == DroneFlightMode.Acro) wDes = AttitudeRate(acroSet, rot);
                else if (Mode == DroneFlightMode.Stab)
                {
                    // position mode (GPS-style): WASD asks for a ground speed in the look direction; the flight
                    // controller tilts the frame to get there and tilts back to brake when the keys are released
                    Quaternion head = Quaternion.Euler(0f, yawTarget, 0f);
                    Vector3 vDes = head * new Vector3(cmdCyclic.x, 0f, cmdCyclic.y) * (sport ? MaxSpeedSport : MaxSpeed);
                    Vector3 vH = new Vector3(v.x, 0f, v.z);
                    Vector3 aDes = (vDes - vH) * 2.4f;
                    // feed-forward against air drag so the commanded speed is actually held
                    Vector3 airH = vH - new Vector3(DroneMode.Wind.x, 0f, DroneMode.Wind.z);
                    aDes += airH * airH.magnitude * 0.024f / Mass;
                    Vector3 aLocal = Quaternion.Inverse(head) * aDes;
                    float pitch = Mathf.Clamp(Mathf.Atan2(aLocal.z, 9.81f) * Mathf.Rad2Deg, -maxTilt, maxTilt);
                    float roll = Mathf.Clamp(Mathf.Atan2(aLocal.x, 9.81f) * Mathf.Rad2Deg, -maxTilt, maxTilt);
                    var target = head * Quaternion.Euler(pitch, 0f, -roll);
                    wDes = AttitudeRate(target, rot);
                }
                else
                {
                    var target = Quaternion.Euler(0f, yawTarget, 0f) * Quaternion.Euler(cmdCyclic.y * maxTilt, 0f, -cmdCyclic.x * maxTilt);
                    wDes = AttitudeRate(target, rot);
                }

                float collective;
                if (Mode == DroneFlightMode.Stab)
                {
                    // barometer altitude hold: the throttle commands a climb rate
                    // barometer hold: Space / Ctrl set a climb rate; released, the drone holds the height it was
                    // left at (position loop), so tilting to fly forward neither climbs nor sinks
                    float vzDes;
                    float y = rb.position.y;
                    if (Mathf.Abs(cmdThrottle) > 0.05f || float.IsNaN(holdAlt))
                    {
                        vzDes = cmdThrottle * (cmdThrottle > 0f ? (sport ? 7f : 4.5f) : (sport ? 7f : 3.5f));
                        holdAlt = y;
                    }
                    else vzDes = Mathf.Clamp((holdAlt - y) * 2.5f, -4f, 4f);
                    var bb = DroneMode.Bounds;
                    if (bb.size != Vector3.zero) holdAlt = Mathf.Min(holdAlt, bb.max.y - 0.5f);
                    float az = 5f * (vzDes - v.y);
                    collective = Mass * (9.81f + az) / Mathf.Max(0.35f, up.y);
                }
                else
                {
                    // manual throttle stick (stays where you leave it), quadratic thrust curve
                    thrStick = Mathf.Clamp01(thrStick + cmdThrottle * dt * (Mode == DroneFlightMode.Acro ? 0.7f : 0.5f));
                    collective = thrStick * thrStick * MotorMax * 4f;
                }
                collective = Mathf.Clamp(collective, MotorIdle * 4f, tMax * 4f);

                // rate loop -> torque (inertia-scaled P), then mixer
                Vector3 wLocal = Quaternion.Inverse(rot) * rb.angularVelocity;
                Vector3 tau = Vector3.Scale(Inertia, (wDes - wLocal) * 22f);
                float mn = float.MaxValue, mx = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    cmdT[i] = collective * 0.25f - tau.x * MZ[i] / (4f * ArmL) + tau.z * MX[i] / (4f * ArmL) + tau.y * MS[i] / (4f * YawCoef);
                    mn = Mathf.Min(mn, cmdT[i]); mx = Mathf.Max(mx, cmdT[i]);
                }
                // desaturation: keep attitude authority by shifting collective (air mode)
                float shift = 0f;
                if (mx > tMax) shift = tMax - mx;
                if (mn + shift < MotorIdle) shift = MotorIdle - mn;
                for (int i = 0; i < 4; i++) cmdT[i] = Mathf.Clamp(cmdT[i] + shift, MotorIdle, tMax);
            }
            else for (int i = 0; i < 4; i++) cmdT[i] = 0f;

            float k = 1f - Mathf.Exp(-dt / MotorTau);
            float yawSum = 0f;
            for (int i = 0; i < 4; i++)
            {
                thrust[i] = Mathf.Lerp(thrust[i], cmdT[i] * eff[i], k);
                Vector3 p = rb.worldCenterOfMass + rot * new Vector3(MX[i] * ArmL, 0.02f, MZ[i] * ArmL);
                rb.AddForceAtPosition(up * thrust[i], p, ForceMode.Force);
                yawSum += MS[i] * thrust[i];
            }
            rb.AddRelativeTorque(0f, YawCoef * yawSum, 0f, ForceMode.Force);

            // aerodynamics: quadratic drag against the air (the frame is a flat plate seen from above)
            Vector3 air = v - DroneMode.Wind;
            float sp = air.magnitude;
            if (sp > 0.01f)
            {
                float kd = 0.022f + 0.03f * Mathf.Abs(Vector3.Dot(air / sp, up));
                rb.AddForce(-air * sp * kd, ForceMode.Force);
            }
            // turbulence and gusts
            float t = Time.time * 0.7f + noiseSeed;
            Vector3 gust = new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, (Mathf.PerlinNoise(t, 7.1f) - 0.5f) * 0.5f, Mathf.PerlinNoise(t, 4.7f) - 0.5f);
            rb.AddForce(gust * 0.9f, ForceMode.Force);
            // prop wash: descending into your own downwash shakes the airframe
            float hs = new Vector2(v.x, v.z).magnitude;
            if (Armed && v.y < -2.2f && hs < 4f)
            {
                float w = Mathf.Clamp01((-v.y - 2.2f) / 3f) * (1f - hs / 4f);
                float u = Time.time * 9f + noiseSeed;
                rb.AddRelativeTorque(new Vector3(Mathf.PerlinNoise(u, 0f) - 0.5f, 0f, Mathf.PerlinNoise(0f, u) - 0.5f) * 0.05f * w, ForceMode.Force);
                rb.AddForce(Vector3.up * -1.5f * w, ForceMode.Force);
            }
            KeepInsideMap();
            lastVel = v;
        }

        /// <summary>
        /// The map edge (and ceiling) is a hard limit for drones: it stops there like at a glass wall, without
        /// exploding. Outward velocity is removed and the drone is held just inside.
        /// </summary>
        void KeepInsideMap()
        {
            var b = DroneMode.Bounds;
            if (b.size == Vector3.zero) { OutOfBounds = false; return; }
            Vector3 p = rb.position, min = b.min, max = b.max;
            Vector3 c = new Vector3(Mathf.Clamp(p.x, min.x, max.x), Mathf.Min(p.y, max.y), Mathf.Clamp(p.z, min.z, max.z));
            // "near the edge" for the OSD warning
            OutOfBounds = p.x < min.x + 6f || p.x > max.x - 6f || p.z < min.z + 6f || p.z > max.z - 6f || p.y > max.y - 4f;
            if (c == p) return;
            Vector3 v = rb.linearVelocity;
            if (p.x != c.x) v.x = 0f;
            if (p.y != c.y) v.y = Mathf.Min(0f, v.y);
            if (p.z != c.z) v.z = 0f;
            rb.position = c;
            rb.linearVelocity = v;
        }

        static Vector3 AttitudeRate(Quaternion target, Quaternion rot)
        {
            Quaternion err = target * Quaternion.Inverse(rot);
            if (err.w < 0f) { err.x = -err.x; err.y = -err.y; err.z = -err.z; err.w = -err.w; } // shortest way
            Vector3 axis = new Vector3(err.x, err.y, err.z);
            float s = axis.magnitude;
            if (s < 1e-5f) return Vector3.zero;
            float ang = 2f * Mathf.Atan2(s, err.w);
            Vector3 wWorld = axis / s * ang * 7f;
            return Vector3.ClampMagnitude(Quaternion.Inverse(rot) * wWorld, 12f);
        }

        // ------------------------------------------------------------------ gun

        void UpdateGun(float dt)
        {
            fireCd -= dt;
            if (!cmdFire || !Armed || Ammo <= 0 || fireCd > 0f || Age < 0.4f) return;
            fireCd = 60f / 600f;
            Ammo--;
            if (Owner) Owner.lastShotTime = Time.time;
            Transform ct = cam.transform;
            Vector3 origin = transform.TransformPoint(new Vector3(0.045f, -0.016f, 0.15f));
            Vector2 r = Random.insideUnitCircle * 0.7f;
            Vector3 dir = ct.rotation * Quaternion.Euler(-r.y, r.x, 0f) * Vector3.forward;
            // converge on what the camera centre points at
            Vector3 aim = ct.position + dir * 150f;
            int n = Physics.RaycastNonAlloc(ct.position, dir, hits, 150f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            RaycastHit hit = default; float best = float.MaxValue; bool any = false;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(transform)) continue;
                if (Owner && hits[i].collider.transform.IsChildOf(Owner.transform)) continue;
                if (DroneMode.IsGhost(hits[i].collider)) continue;
                if (hits[i].distance < best) { best = hits[i].distance; hit = hits[i]; any = true; }
            }
            Vector3 end = aim;
            if (any)
            {
                end = hit.point;
                var hb = hit.collider.GetComponent<Hitbox>();
                IDamageable d = hb ? hb.Owner : hit.collider.GetComponent<IDamageable>();
                if (d != null && !ReferenceEquals(d, this))
                {
                    float mult = hb ? hb.multiplier : 1f;
                    bool crit = hb && hb.critical;
                    if (d is PlayerHealth ph) { crit = hit.point.y > ph.transform.position.y + ph.motor.EyeHeight - 0.2f; mult = crit ? 1.6f : 1f; }
                    float falloff = Mathf.Lerp(1f, 0.55f, Mathf.InverseLerp(25f, 90f, hit.distance));
                    var res = d.TakeDamage(new DamageInfo
                    {
                        amount = Mathf.Round(11f * mult * falloff), point = hit.point, normal = hit.normal, direction = dir,
                        critical = crit, attacker = Owner, weapon = WeaponGun
                    });
                    if (IsLocalView && res.applied && !(d is Destructible))
                    {
                        HUD.Ensure().RegisterHit(res.killed);
                        DroneHUD.Hitmarker(res.killed);
                        ProcAudio.Play(res.killed ? Sfx.Kill : Sfx.Hit, 0.45f);
                    }
                }
                if (hit.collider.GetComponentInParent<HumanoidRig>()) FX.Ensure().Blood(hit.point, hit.normal, 6);
                else
                {
                    var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                    FX.Ensure().Impact(hit, tag ? tag.kind : SurfaceKind.Concrete, d == null && hit.rigidbody == null);
                }
                if (hit.rigidbody && !hit.rigidbody.isKinematic && hit.rigidbody != rb) hit.rigidbody.AddForceAtPosition(dir * 2f, hit.point, ForceMode.Impulse);
            }
            FX.Ensure().Tracer(origin, end);
            FX.Ensure().MuzzleFlashWorld(origin, dir);
            ProcAudio.PlayAt(Sfx.ShotPistol, origin, IsLocalView ? 0.5f : 0.8f, 0.12f);
            // recoil: the light airframe is pushed back and the nose kicks up
            rb.AddForce(-dir * 0.09f, ForceMode.Impulse);
            rb.AddRelativeTorque(new Vector3(-0.0012f, Random.Range(-0.0004f, 0.0004f), 0f), ForceMode.Impulse);
            AIEvents.Emit(origin, 35f, Owner);
        }

        // ------------------------------------------------------------------ link, fuse, visuals

        void UpdateLink(float dt)
        {
            // the link reaches the whole map (no range / wall limit); only the operator's death or a manual cut ends it
            Signal = linkLost ? 0f : 1f;
        }

        void CheckFuse()
        {
            if (Age < 0.25f) return;
            if (transform.position.y < DroneMode.WaterLevel)
            {
                FX.Ensure().Puff(transform.position, Vector3.up, 16, 0.5f, new Vector2(2f, 6f), new Vector2(0.3f, 0.8f), new Vector2(0.6f, 1.2f),
                                 new Color(0.8f, 0.85f, 0.9f, 0.7f), new Color(0.6f, 0.65f, 0.7f, 0.4f));
                Explode();
                return;
            }
            // proximity fuse: arms after launch, sets off right next to an enemy
            if (Age < 1f || !Owner) return;
            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || !Owner.IsEnemyOf(c)) continue;
                if ((c.Chest - transform.position).sqrMagnitude < 0.8f * 0.8f || (c.Head - transform.position).sqrMagnitude < 0.6f * 0.6f) { Explode(); return; }
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (Dead) return;
            if (Owner && c.collider.transform.IsChildOf(Owner.transform)) return;
            Explode(); // contact fuse: anything it touches sets the warhead off
        }

        void UpdateVisuals(float dt)
        {
            for (int i = 0; i < 4; i++)
            {
                float spin = thrust[i] / MotorMax; // 0..1
                propAngle[i] += (MS[i] > 0f ? 1f : -1f) * (Armed ? 900f + spin * 5000f : spin * 4000f) * dt;
                props[i].localRotation = Quaternion.Euler(0f, propAngle[i], 0f);
                bool fast = spin > 0.08f;
                propDiscs[i].enabled = fast;
                propBlades[i].enabled = !fast || Random.value < 0.3f;
            }
            bool blink = Mathf.Repeat(Time.time * 1.5f, 1f) < 0.15f;
            foreach (var r in ledRenderers) if (r) r.enabled = Armed && !blink;

            // sound: four motors, pitch follows thrust; the pilot hears it from the frame, not from outside
            float tf = ThrottleFraction;
            buzz.pitch = (0.62f + tf * 0.95f) * (1f + (Mathf.PerlinNoise(Time.time * 3f, noiseSeed) - 0.5f) * 0.05f);
            float vol = Mathf.Clamp01(0.25f + tf * 1.4f);
            if (IsLocalView) { buzz.spatialBlend = 0f; buzz.volume = vol * 0.22f * ProcAudio.MasterVolume; }
            else { buzz.spatialBlend = 1f; buzz.volume = vol * 0.95f * ProcAudio.MasterVolume; }
        }

        // ------------------------------------------------------------------ damage / end

        public DamageResult TakeDamage(DamageInfo info)
        {
            if (Dead) return default;
            if (info.attacker && info.attacker == Owner && info.weapon == WeaponBoom) return default;
            Health -= info.amount;
            HitFlash = 1f;
            FX.Ensure().SparkBurst(info.point, info.normal == Vector3.zero ? -info.direction : info.normal, 8, 6f);
            ProcAudio.PlayAt(Sfx.ImpactMetal, info.point, 0.7f);
            if (rb) rb.AddForceAtPosition(info.direction.normalized * 0.4f, info.point, ForceMode.Impulse);
            bool killed = Health <= 0f;
            if (!killed)
            {
                // a hit that does not bring it down chews up a motor or prop: the drone wobbles
                int m = Random.Range(0, 4);
                eff[m] = Mathf.Max(0.45f, eff[m] * 0.72f);
            }
            else
            {
                bool byEnemy = info.attacker && Owner && info.attacker != Owner;
                if (byEnemy) DroneMode.Reveal(Owner, info.attacker);
                if (info.weapon == WeaponBoom || (info.weapon != null && info.weapon.Contains("Гранат"))) { Dead = true; Invoke(nameof(SympatheticBlast), 0.08f); }
                else ShotDown();
            }
            return new DamageResult { applied = true, killed = killed, dealt = info.amount };
        }

        void SympatheticBlast() { Dead = false; Explode(); }

        /// <summary>Warhead detonation (contact, proximity, the pilot's button, water).</summary>
        public void Explode()
        {
            if (Dead) return;
            Dead = true;
            if (EndReason.Length == 0) EndReason = "Дрон подорван";
            Vector3 p = transform.position;
            Explosions.Detonate(p, BlastRadius, BlastDamage, 10f, Owner, WeaponBoom, transform);
            Finish();
            Destroy(gameObject);
        }

        /// <summary>Shot down: the warhead does not go off, the frame breaks apart and falls.</summary>
        void ShotDown()
        {
            Dead = true;
            if (EndReason.Length == 0) EndReason = "Дрон сбит";
            Vector3 p = transform.position;
            FX.Ensure().SparkBurst(p, Vector3.up, 24, 8f);
            FX.Ensure().Puff(p, Vector3.up, 8, 0.9f, new Vector2(0.5f, 2f), new Vector2(0.3f, 0.7f), new Vector2(0.8f, 1.6f),
                             new Color(0.2f, 0.2f, 0.2f, 0.7f), new Color(0.35f, 0.33f, 0.3f, 0.5f));
            ProcAudio.PlayAt(Sfx.ImpactMetal, p, 1f, 0.05f);
            ProcAudio.PlayAt(Sfx.WoodBreak, p, 0.6f, 0.2f);
            Vector3 v = rb ? rb.linearVelocity : Vector3.zero;
            var model = transform.Find("Model");
            if (model)
            {
                var parts = new List<Transform>();
                foreach (Transform t in model) parts.Add(t);
                foreach (var t in parts)
                {
                    if (t.GetComponentInChildren<Renderer>() == null) continue;
                    t.SetParent(null, true);
                    foreach (var r in t.GetComponentsInChildren<Renderer>()) if (r.sharedMaterial && r.sharedMaterial.name.StartsWith("DronePropDisc")) r.enabled = false;
                    var prb = t.gameObject.AddComponent<Rigidbody>();
                    prb.mass = 0.05f;
                    prb.linearVelocity = v + Random.insideUnitSphere * 3f + Vector3.up * 1.5f;
                    prb.angularVelocity = Random.insideUnitSphere * 15f;
                    var bc = t.gameObject.AddComponent<BoxCollider>();
                    bc.size = Vector3.one * 0.9f;
                    Destroy(t.gameObject, Random.Range(6f, 9f));
                }
            }
            Finish();
            Destroy(gameObject);
        }

        void Finish()
        {
            if (buzz) buzz.Stop();
            if (Operator) Operator.OnDroneGone(this);
        }

        void OnDestroy()
        {
            if (!Dead && Operator) Operator.OnDroneGone(this);
        }
    }
}
