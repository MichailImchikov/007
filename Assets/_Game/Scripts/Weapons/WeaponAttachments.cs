using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>Root of an instantiated attachment model (its name stays the model name: Grip_Vertical, Optic_RedDot, ...).</summary>
    public class AttachmentInstance : MonoBehaviour
    {
        public AttachmentData data;
    }

    /// <summary>
    /// Modular weapon on a WeaponController (see WeaponData): builds the Blender model (replacing a procedural one),
    /// mounts the chosen attachments at their sockets, wires every WeaponController field from the sockets and applies
    /// the attachments' stat modifiers on top of the base stats (always from the base). Also runs the tactical devices
    /// of the first-person weapon: laser beam + dot (toggle: GameInput.LaserTogglePressed) and the weapon light (follows
    /// the player's flashlight key and replaces the camera flashlight while it is mounted).
    ///
    /// Model layout (FBX root = WeaponController.model): Body, Mag, Bolt / Slide, Trigger, IronSights, sockets Socket_Muzzle,
    /// Socket_Optic, Socket_Underbarrel, Socket_Side, Socket_Mag, Grip_R, Grip_L, Sight_Iron, Eject. Added here:
    /// "LeftHand" (support-hand anchor the reload animation moves) with "LeftHandGrip" at Grip_L, "Muzzle" (flash rig)
    /// under the active muzzle socket, pivots BoltCarrier / SlideRack for parts moved by two animations.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public class WeaponAttachments : MonoBehaviour
    {
        public WeaponData data;
        public Transform modelRoot;

        /// <summary>Laser sights on (all weapons, toggled with the laser key).</summary>
        public static bool LaserOn = true;

        WeaponController w;
        AttachmentData[] selection = new AttachmentData[0];
        GameObject[] instances = new GameObject[0];
        string signature = "";

        // fixed parts of the model
        Transform mag, ironSights, sightIron, gripL, socketMuzzle;
        Transform leftHand, leftHandGrip, muzzle, flashScale, shellInHand, sightProxy;
        Vector3 leftHandHome;
        Light muzzleLight;
        Stats baseStats;
        bool haveBase;

        // tactical devices (first-person weapon only)
        Transform laserEmitter, lightEmitter;
        LineRenderer beam;
        Transform dot;
        Light spot;
        Flashlight playerLight;
        Light cameraLight;
        bool suppressingCameraLight;
        static Material beamMat, dotMat;
        static readonly RaycastHit[] hits = new RaycastHit[16];

        public WeaponController Weapon => w;
        /// <summary>Mounted attachment ids per slot ("Optic_Holo|Muzzle_FlashHider||").</summary>
        public string Signature => signature;
        public bool Assembled => w && modelRoot && w.model == modelRoot;
        public bool HasLaser => laserEmitter;
        public bool HasLight => lightEmitter;

        public AttachmentData Mounted(AttachmentSlot s)
        {
            int i = data ? data.SlotIndex(s) : -1;
            return i >= 0 && i < selection.Length ? selection[i] : null;
        }

        public GameObject MountedInstance(AttachmentSlot s)
        {
            int i = data ? data.SlotIndex(s) : -1;
            return i >= 0 && i < instances.Length ? instances[i] : null;
        }

        Transform Part(string n)
        {
            if (!modelRoot || string.IsNullOrEmpty(n)) return null;
            var t = modelRoot.Find(n);
            return t ? t : WeaponAssembler.FindDeep(modelRoot, n);
        }

        // ------------------------------------------------------------------ assembly

        /// <summary>First assembly: base stats from the catalog, the Blender model replaces the procedural one, fixed parts wired.</summary>
        public void Setup(WeaponController wc, WeaponData d)
        {
            w = wc;
            data = d;
            var def = WeaponCatalog.Get(w.weaponName);
            if (def != null) def.Apply(w);
            w.hipPosition += d.hipOffset;

            // the muzzle flash rig (from the procedural model, or cloned by the loadout) survives the model swap
            var old = w.model;
            muzzle = w.muzzle ? w.muzzle : new GameObject("Muzzle").transform;
            muzzle.SetParent(w.transform, false);
            muzzle.name = "Muzzle";
            muzzleLight = w.muzzleLight;
            if (w.muzzleFlash)
            {
                flashScale = new GameObject("FlashScale").transform;
                flashScale.SetParent(muzzle, false);
                flashScale.gameObject.layer = w.gameObject.layer;
                var ft = w.muzzleFlash.transform;
                ft.SetParent(flashScale, false);
                ft.localPosition = Vector3.zero;
                ft.localRotation = Quaternion.identity;
                ft.localScale = Vector3.one;
            }
            if (muzzleLight)
            {
                muzzleLight.transform.SetParent(muzzle, false);
                muzzleLight.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            }

            var go = Instantiate(d.model, w.transform, false);
            go.name = w.weaponName + "_Model";
            modelRoot = go.transform;
            modelRoot.localPosition = Vector3.zero;
            modelRoot.localRotation = Quaternion.identity;
            // the whole gun scales, attachments included (they mount under its sockets): proportions stay, hands and
            // sights follow the sockets' world positions
            modelRoot.localScale = Vector3.one * Mathf.Clamp(d.modelScale, 0.5f, 2f);
            WeaponAssembler.Prepare(go, w.gameObject.layer);
            w.model = modelRoot;
            // the procedural gun (receiver boxes, arms, holo sight) goes away entirely
            if (old && old != modelRoot && old != muzzle && old.IsChildOf(w.transform) && old != w.transform) WeaponAssembler.Kill(old.gameObject);

            mag = Part("Mag");
            ironSights = Part("IronSights");
            sightIron = Part("Sight_Iron");
            gripL = Part("Grip_L");
            socketMuzzle = Part("Socket_Muzzle");
            var eject = Part("Eject");

            // moving parts: the reload pulls the charging handle; the bolt cycles on every shot
            var cycling = Part(d.boltPart);
            w.chargingHandle = null;
            w.bolt = null;
            if (cycling)
            {
                if (d.slideCycles)
                {
                    // pistol: the slide cycles on every shot, the empty reload racks it through a parent pivot
                    w.chargingHandle = WeaponAssembler.Pivot("SlideRack", cycling);
                    w.bolt = cycling;
                }
                else if (d.boltReciprocates)
                {
                    // AK / M14: the handle rides the bolt carrier on every shot and is pulled by the reload
                    w.bolt = WeaponAssembler.Pivot("BoltCarrier", cycling);
                    w.chargingHandle = cycling;
                }
                else w.chargingHandle = cycling;
            }
            if (d.boltTravel > 0f) w.chTravel = d.boltTravel;
            w.boltKick = d.cycleTravel;
            w.boltLiftAngle = d.boltLiftAngle;
            w.ejectPort = eject ? eject : modelRoot;
            w.loadPort = w.reloadStyle == ReloadStyle.PerShell ? w.ejectPort : null;

            // support hand: the reload animation moves this anchor; its grip point sits on the handguard socket
            leftHand = new GameObject("LeftHand").transform;
            leftHand.SetParent(modelRoot, false);
            leftHand.gameObject.layer = w.gameObject.layer;
            leftHand.position = gripL ? gripL.position : modelRoot.TransformPoint(new Vector3(0f, 0f, 0.3f));
            leftHandHome = leftHand.localPosition;
            leftHandGrip = new GameObject("LeftHandGrip").transform;
            leftHandGrip.SetParent(leftHand, false);
            if (gripL) leftHandGrip.SetPositionAndRotation(gripL.position, gripL.rotation);
            w.leftHand = leftHand;
            w.leftHandGrip = leftHandGrip;

            // single-round loading (bolt action): the cartridge in the support hand is the reload's "magazine"
            if (w.reloadStyle == ReloadStyle.PerShell) shellInHand = MakeShell();
            haveBase = false;
        }

        /// <summary>Mounts one attachment per slot (WeaponData.slots order; null = none) and re-wires the weapon.</summary>
        public void Build(AttachmentData[] sel)
        {
            if (!w || !data || !modelRoot) return;
            // the flash rig may sit on the old muzzle device: park it on the barrel before the attachments go
            if (muzzle) muzzle.SetParent(socketMuzzle ? socketMuzzle : modelRoot, false);
            foreach (var g in instances) if (g) WeaponAssembler.Kill(g);
            if (sightProxy) { WeaponAssembler.Kill(sightProxy.gameObject); sightProxy = null; }
            selection = new AttachmentData[data.slots.Length];
            instances = new GameObject[data.slots.Length];
            for (int i = 0; i < data.slots.Length; i++)
            {
                var s = data.slots[i];
                if (s == null) continue;
                var a = sel != null && i < sel.Length ? sel[i] : null;
                if (a && a.slot != s.slot) a = null;
                if (!a && s.required) a = s.defaultAttachment;
                selection[i] = a;
                if (a && a.prefab) instances[i] = Mount(s, a);
            }
            Wire();
            ApplyStats();
            signature = WeaponDataDB.Signature(selection);
        }

        /// <summary>
        /// Attachments changed after WeaponController.Init (respawn with a new choice, menu preview): re-assembles,
        /// re-measures the controller (rest poses, ADS aim, reload animation) and rebuilds the hand grip anchors.
        /// </summary>
        public void Rebuild(AttachmentData[] sel)
        {
            if (!w) return;
            bool live = w.IsInitialized;
            if (live) w.ResetRigPose();
            Build(sel);
            if (!live) return;
            w.RefreshModel();
            if (Application.isPlaying) HandGrips.Rebuild(w);
            else
            {
                foreach (var n in new[] { HandGrips.Right, HandGrips.Left })
                {
                    var t = HandGrips.Find(w.model, n);
                    if (t) Object.DestroyImmediate(t.gameObject);
                }
                HandGrips.Ensure(w);
            }
        }

        GameObject Mount(WeaponData.Slot s, AttachmentData a)
        {
            var socket = Part(s.socket);
            if (!socket)
            {
                Debug.LogWarning($"[WeaponAttachments] {w.weaponName}: no socket {s.socket} for {a.id}");
                return null;
            }
            var go = Instantiate(a.prefab, socket, false);
            go.name = a.prefab.name; // HandGrips recognizes foregrips by the root name (Grip_Vertical*, Grip_Angled*)
            var t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            if (!string.IsNullOrEmpty(s.parentPart))
            {
                var p = Part(s.parentPart);
                if (p) t.SetParent(p, true); // Glock: the optic rides on the slide
            }
            WeaponAssembler.Prepare(go, w.gameObject.layer);
            go.AddComponent<AttachmentInstance>().data = a;
            return go;
        }

        void Wire()
        {
            // ---- sight: the optic's sight line (iron sights fold away); its reticle is the collimated mark
            var optic = MountedInstance(AttachmentSlot.Optic);
            var opticData = Mounted(AttachmentSlot.Optic);
            var sp = optic ? WeaponAssembler.FindDeep(optic.transform, "Sight_Point") : null;
            if (ironSights) ironSights.gameObject.SetActive(!sp);
            w.reticle = null;
            if (sp)
            {
                var slot = data.GetSlot(AttachmentSlot.Optic);
                if (slot != null && !string.IsNullOrEmpty(slot.parentPart))
                {
                    // the optic rides on a moving part (slide): ADS aims at where its sight line rests
                    sightProxy = new GameObject("SightPoint").transform;
                    sightProxy.SetParent(modelRoot, false);
                    sightProxy.SetPositionAndRotation(sp.position, sp.rotation);
                    w.sightPoint = sightProxy;
                }
                else w.sightPoint = sp;
                // the sight is drawn in the rear lens (OpticSight: a collimated reticle, or a magnified picture in
                // picture with its reticle); the model's own reticle mesh goes away
                var rear = WeaponAssembler.FindDeep(optic.transform, "Lens_Rear");
                if (!rear) rear = WeaponAssembler.FindDeep(optic.transform, "Lens");
                var rearR = rear ? rear.GetComponent<Renderer>() : null;
                var meshReticle = WeaponAssembler.FindDeep(optic.transform, "Reticle");
                if (rearR && Application.isPlaying) // edit-time previews keep the model's own reticle
                {
                    var os = optic.GetComponent<OpticSight>();
                    if (!os) os = optic.AddComponent<OpticSight>();
                    os.Setup(w, rearR, opticData);
                    if (meshReticle) meshReticle.gameObject.SetActive(false);
                    w.reticle = null;
                }
                else
                {
                    if (!meshReticle && opticData && opticData.addReticle) meshReticle = MakeReticle(optic.transform, sp, opticData);
                    w.reticle = meshReticle;
                }
            }
            else if (sightIron) w.sightPoint = sightIron;
            else
            {
                // no iron sights and no optic: a sight line above the optic rail
                var rail = Part("Socket_Optic");
                sightProxy = new GameObject("SightPoint").transform;
                sightProxy.SetParent(modelRoot, false);
                sightProxy.localPosition = (rail ? rail.localPosition : Vector3.zero) + new Vector3(0f, 0.035f, -0.05f);
                w.sightPoint = sightProxy;
            }

            // ---- muzzle: flash, tracers and smoke start at the muzzle device's own muzzle
            var dev = MountedInstance(AttachmentSlot.Muzzle);
            var ms = dev ? WeaponAssembler.FindDeep(dev.transform, "Socket_Muzzle") : null;
            if (!ms) ms = socketMuzzle ? socketMuzzle : modelRoot;
            muzzle.SetParent(ms, false);
            muzzle.localPosition = Vector3.zero;
            muzzle.localRotation = Quaternion.identity;
            muzzle.localScale = Vector3.one;
            w.muzzle = muzzle;

            // ---- magazine: an extended one replaces the standard one and is what the reload moves
            var ext = MountedInstance(AttachmentSlot.Magazine);
            if (mag) mag.gameObject.SetActive(!ext);
            if (w.reloadStyle == ReloadStyle.PerShell) w.magazine = shellInHand;
            else w.magazine = ext ? ext.transform : mag;
            if (w.magazine && w.reloadStyle == ReloadStyle.Magazine) w.magGripOffset = MagGrip(w.magazine);

            // ---- support hand: on a foregrip's Grip_L, else on the handguard (Grip_L of the weapon)
            var under = MountedInstance(AttachmentSlot.Underbarrel);
            var underData = Mounted(AttachmentSlot.Underbarrel);
            var fg = under && underData && underData.foregrip ? WeaponAssembler.FindDeep(under.transform, "Grip_L") : null;
            if (fg)
            {
                w.leftHandGrip = fg;
                leftHand.position = fg.position;
            }
            else
            {
                w.leftHandGrip = leftHandGrip;
                leftHand.localPosition = leftHandHome;
            }

            // ---- tactical devices
            laserEmitter = lightEmitter = null;
            for (int i = 0; i < instances.Length; i++)
            {
                if (!instances[i] || !selection[i]) continue;
                if (selection[i].laser && !laserEmitter) laserEmitter = WeaponAssembler.FindDeep(instances[i].transform, "Laser_Emitter");
                if (selection[i].flashlight && !lightEmitter) lightEmitter = WeaponAssembler.FindDeep(instances[i].transform, "Light_Emitter");
            }
            if (spot && spot.transform.parent != lightEmitter) { WeaponAssembler.Kill(spot.gameObject); spot = null; }
            if (!laserEmitter) HideLaser();
        }

        /// <summary>Where the support hand grabs the magazine (magazine space): mid-body on rifles, the base plate on pistols.</summary>
        Vector3 MagGrip(Transform m)
        {
            var mf = m.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) mf = m.GetComponentInChildren<MeshFilter>();
            if (!mf || !mf.sharedMesh) return w.magGripOffset;
            var b = mf.sharedMesh.bounds;
            Vector3 lo = m.InverseTransformPoint(mf.transform.TransformPoint(b.min));
            Vector3 hi = m.InverseTransformPoint(mf.transform.TransformPoint(b.max));
            float yMin = Mathf.Min(lo.y, hi.y), zMid = (lo.z + hi.z) * 0.5f;
            return w.slot == WeaponSlot.Secondary ? new Vector3(0f, yMin - 0.01f, zMid) : new Vector3(0f, yMin * 0.45f, zMid);
        }

        /// <summary>Red dot on the sight line near the objective of an optic without its own reticle (prism).</summary>
        Transform MakeReticle(Transform optic, Transform sp, AttachmentData a)
        {
            Vector3 local = optic.InverseTransformPoint(sp.position);
            float z = local.z + 0.12f;
            var lens = WeaponAssembler.FindDeep(optic, "Lens");
            var lmf = lens ? lens.GetComponent<MeshFilter>() : null;
            if (lmf && lmf.sharedMesh)
                z = optic.InverseTransformPoint(lens.TransformPoint(new Vector3(0f, 0f, lmf.sharedMesh.bounds.max.z))).z - 0.012f;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            WeaponAssembler.Kill(go.GetComponent<Collider>());
            go.name = "Reticle";
            go.transform.SetParent(optic, false);
            go.transform.localPosition = new Vector3(local.x, local.y, z);
            go.transform.localScale = Vector3.one * 0.0011f;
            var r = go.GetComponent<MeshRenderer>();
            if (a.reticleMaterial) r.sharedMaterial = a.reticleMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            go.layer = w.gameObject.layer;
            return go.transform;
        }

        Transform MakeShell()
        {
            var shell = new GameObject("ShellInHand").transform;
            shell.SetParent(leftHand, false);
            shell.gameObject.layer = w.gameObject.layer;
            shell.position = leftHandGrip.position + modelRoot.TransformVector(new Vector3(0f, 0.03f, 0.04f));
            shell.rotation = modelRoot.rotation;
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            WeaponAssembler.Kill(body.GetComponent<Collider>());
            body.name = "ShellBody";
            body.layer = w.gameObject.layer;
            body.transform.SetParent(shell, false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(data.shellDiameter, data.shellLength * 0.5f, data.shellDiameter);
            var r = body.GetComponent<MeshRenderer>();
            if (data.shellMaterial) r.sharedMaterial = data.shellMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            shell.gameObject.SetActive(false);
            return shell;
        }

        // ------------------------------------------------------------------ stats

        /// <summary>Base stats (catalog + model data) restored, then every mounted attachment's modifiers on top.</summary>
        void ApplyStats()
        {
            if (!haveBase) { baseStats = Stats.Of(w); haveBase = true; }
            baseStats.ApplyTo(w);
            float flash = data.flashScale;
            bool suppressed = false;
            AttachmentData optic = null;
            foreach (var a in selection)
            {
                if (!a) continue;
                w.recoilUp *= a.recoilMul;
                w.recoilSide *= a.recoilMul;
                float kick = Mathf.Lerp(1f, a.recoilMul, 0.6f);
                w.kickScale *= kick;
                w.punchScale *= kick;
                w.hipSpread *= a.hipSpreadMul;
                w.moveSpreadAdd *= a.hipSpreadMul;
                w.adsSpread *= a.adsSpreadMul;
                w.aimTime *= a.adsTimeMul;
                w.moveSpeedMul *= a.moveSpeedMul;
                w.reloadTime *= a.reloadTimeMul;
                w.emptyReloadTime *= a.reloadTimeMul;
                w.shellInsertTime *= a.reloadTimeMul;
                w.range *= a.rangeMul;
                w.falloffStart *= a.rangeMul;
                w.falloffEnd *= a.rangeMul;
                w.damage *= a.damageMul;
                w.shotVolume *= a.volumeMul;
                w.noiseRadius *= a.noiseMul;
                flash *= a.flashMul;
                suppressed |= a.suppressed;
                if (a.magCapacity > 0) w.magSize = a.magCapacity;
                if (a.slot == AttachmentSlot.Optic) optic = a;
            }
            if (optic)
            {
                if (optic.adsFov > 0f) w.adsFov = optic.adsFov;
                if (optic.aimDistance > 0f) w.aimDistance = optic.aimDistance;
                // a magnified optic without a see-through bore would fill the screen with its eyepiece: scope overlay
                w.scoped = optic.UsesScopeOverlay;
            }
            if (flashScale) flashScale.localScale = Vector3.one * flash;
            // suppressed: no muzzle light (the flash itself only shrinks)
            w.muzzleLight = suppressed ? null : muzzleLight;
            if (muzzleLight) muzzleLight.enabled = false;
        }

        struct Stats
        {
            float damage, range, falloffStart, falloffEnd, hipSpread, adsSpread, moveSpreadAdd, recoilUp, recoilSide, kickScale, punchScale,
                  aimTime, aimDistance, adsFov, moveSpeedMul, reloadTime, emptyReloadTime, shellInsertTime, shotVolume, noiseRadius;
            int magSize;
            bool scoped;

            public static Stats Of(WeaponController w) => new Stats
            {
                damage = w.damage, range = w.range, falloffStart = w.falloffStart, falloffEnd = w.falloffEnd,
                hipSpread = w.hipSpread, adsSpread = w.adsSpread, moveSpreadAdd = w.moveSpreadAdd,
                recoilUp = w.recoilUp, recoilSide = w.recoilSide, kickScale = w.kickScale, punchScale = w.punchScale,
                aimTime = w.aimTime, aimDistance = w.aimDistance, adsFov = w.adsFov, moveSpeedMul = w.moveSpeedMul,
                reloadTime = w.reloadTime, emptyReloadTime = w.emptyReloadTime, shellInsertTime = w.shellInsertTime,
                shotVolume = w.shotVolume, noiseRadius = w.noiseRadius, magSize = w.magSize, scoped = w.scoped,
            };

            public void ApplyTo(WeaponController w)
            {
                w.damage = damage; w.range = range; w.falloffStart = falloffStart; w.falloffEnd = falloffEnd;
                w.hipSpread = hipSpread; w.adsSpread = adsSpread; w.moveSpreadAdd = moveSpreadAdd;
                w.recoilUp = recoilUp; w.recoilSide = recoilSide; w.kickScale = kickScale; w.punchScale = punchScale;
                w.aimTime = aimTime; w.aimDistance = aimDistance; w.adsFov = adsFov; w.moveSpeedMul = moveSpeedMul;
                w.reloadTime = reloadTime; w.emptyReloadTime = emptyReloadTime; w.shellInsertTime = shellInsertTime;
                w.shotVolume = shotVolume; w.noiseRadius = noiseRadius; w.magSize = magSize; w.scoped = scoped;
            }
        }

        // ------------------------------------------------------------------ tactical devices (first-person)

        void Awake()
        {
            if (!w) w = GetComponent<WeaponController>();
        }

        void OnEnable() => RenderPipelineManager.beginCameraRendering += BeforeCameraRender;

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCameraRender;
            HideLaser();
            if (spot) spot.enabled = false;
            RestoreCameraLight();
        }

        void Update()
        {
            if (!w || !Application.isPlaying) return;
            if (laserEmitter && GameInput.LaserTogglePressed)
            {
                LaserOn = !LaserOn;
                ProcAudio.Play(Sfx.Click, 0.5f, 0.05f);
            }
            UpdateLight();
        }

        void UpdateLight()
        {
            if (!lightEmitter)
            {
                if (spot) spot.enabled = false;
                RestoreCameraLight();
                return;
            }
            if (!playerLight && w.motor) playerLight = w.motor.GetComponent<Flashlight>();
            bool on = playerLight && playerLight.On && !SkyArena.LocalRunner;
            if (on && !spot) spot = MakeSpot();
            if (spot) spot.enabled = on;
            // the weapon light replaces the camera-mounted flashlight while this weapon is out
            var cl = CameraLight();
            if (cl && cl.enabled) cl.enabled = false;
            suppressingCameraLight = true;
        }

        void RestoreCameraLight()
        {
            if (!suppressingCameraLight) return;
            suppressingCameraLight = false;
            var cl = CameraLight();
            if (cl && playerLight) cl.enabled = playerLight.On;
        }

        /// <summary>The player's flashlight (Flashlight.cs builds it as "Flashlight" under the camera).</summary>
        Light CameraLight()
        {
            if (cameraLight) return cameraLight;
            if (!w || !w.cam) return null;
            var t = w.cam.transform.Find("Flashlight");
            cameraLight = t ? t.GetComponent<Light>() : null;
            return cameraLight;
        }

        Light MakeSpot()
        {
            var go = new GameObject("WA_Light");
            go.transform.SetParent(lightEmitter, false);
            go.layer = w.gameObject.layer;
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.range = 40f;
            l.spotAngle = 46f;
            l.innerSpotAngle = 20f;
            l.intensity = 20f;
            l.color = new Color(1f, 0.96f, 0.88f);
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.85f;
            l.renderMode = LightRenderMode.ForcePixel;
            return l;
        }

        void BeforeCameraRender(ScriptableRenderContext ctx, Camera c)
        {
            if (!w || !Application.isPlaying || c != w.cam) return;
            UpdateLaser();
        }

        void UpdateLaser()
        {
            var pc = w.playerCam;
            bool show = laserEmitter && LaserOn && w.isActiveAndEnabled && !w.IsScoped && w.ExternalLower < 0.5f
                        && !(w.motor && w.motor.IsSprinting) && !SkyArena.LocalRunner && WeaponController.AbilityLower < 0.5f
                        && !(pc && (pc.DeathAmount > 0.01f || pc.ThirdPerson > 0.01f));
            if (!show) { HideLaser(); return; }
            if (!beam) MakeLaser();
            Transform ct = w.cam.transform;
            Vector3 o = laserEmitter.position;
            // zeroed on the camera axis 25 m out, then turned with the gun (sway, recoil and reloads swing the beam)
            Vector3 zero = (ct.position + ct.forward * 25f - o).normalized;
            Vector3 dir = Quaternion.FromToRotation(ct.forward, w.model.forward) * zero;
            float dist = 150f;
            Vector3 end = o + dir * dist, n = -dir;
            bool hit = Raycast(o, dir, dist, out var h);
            if (hit) { end = h.point; n = h.normal; dist = h.distance; }
            beam.SetPosition(0, o);
            beam.SetPosition(1, end);
            beam.enabled = true;
            dot.gameObject.SetActive(hit);
            if (hit)
            {
                dot.position = end + n * 0.004f;
                dot.rotation = Quaternion.LookRotation(-n);
                float s = (0.014f + dist * 0.0018f) / Mathf.Max(0.001f, transform.lossyScale.x);
                dot.localScale = new Vector3(s, s, s);
            }
        }

        void HideLaser()
        {
            if (beam) beam.enabled = false;
            if (dot) dot.gameObject.SetActive(false);
        }

        bool Raycast(Vector3 o, Vector3 dir, float dist, out RaycastHit best)
        {
            best = default;
            Transform self = w.motor ? w.motor.transform : null;
            int n = Physics.RaycastNonAlloc(o, dir, hits, dist, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            bool any = false;
            for (int i = 0; i < n; i++)
            {
                var hh = hits[i];
                if (!hh.collider || hh.distance >= bd) continue;
                if (self && hh.collider.transform.IsChildOf(self)) continue;
                bd = hh.distance; best = hh; any = true;
            }
            return any;
        }

        void MakeLaser()
        {
            if (!beamMat) beamMat = FX.MakeParticleMat("WA_LaserBeam", Texture2D.whiteTexture, true, new Color(1f, 0.12f, 0.08f, 0.85f));
            if (!dotMat) dotMat = FX.MakeParticleMat("WA_LaserDot", FX.SoftCircle(32, 1.6f), true, new Color(5f, 0.35f, 0.25f, 1f));
            var go = new GameObject("WA_LaserBeam");
            go.transform.SetParent(transform, false);
            go.layer = w.gameObject.layer;
            beam = go.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.widthMultiplier = 1f;
            beam.widthCurve = new AnimationCurve(new Keyframe(0f, 0.0024f), new Keyframe(1f, 0.0016f));
            beam.numCapVertices = 0;
            beam.shadowCastingMode = ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.sharedMaterial = beamMat;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.2f, 0.15f), 0f), new GradientColorKey(new Color(1f, 0.2f, 0.15f), 1f) },
                      new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.3f, 0.12f), new GradientAlphaKey(0f, 1f) });
            beam.colorGradient = g;
            beam.enabled = false;

            var d = GameObject.CreatePrimitive(PrimitiveType.Quad);
            WeaponAssembler.Kill(d.GetComponent<Collider>());
            d.name = "WA_LaserDot";
            d.layer = w.gameObject.layer;
            d.transform.SetParent(transform, false);
            var r = d.GetComponent<MeshRenderer>();
            r.sharedMaterial = dotMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            dot = d.transform;
            d.SetActive(false);
        }
    }
}
