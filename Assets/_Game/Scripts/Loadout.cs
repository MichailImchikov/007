using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace FPS
{
    /// <summary>
    /// Player inventory: one primary, one secondary, frag grenades.
    /// 1 / 2 / mouse wheel — switch, X — pick up weapon / ammo, hold G — aim grenade (arc preview), release — throw.
    /// </summary>
    [DefaultExecutionOrder(40)]
    public class Loadout : MonoBehaviour
    {
        public PlayerMotor motor;
        public PlayerCamera playerCam;
        public Camera cam;
        public MeleeController melee;
        public WeaponController[] weapons;
        public string primaryName = "M4A1";
        public string secondaryName = "P-17";

        [Header("Grenades")]
        public int grenades = 3;
        public int maxGrenades = 4;
        public Transform grenadeArm;
        public Transform grenadeInHand;
        public float throwSpeed = 17f;

        [Header("Pickups")]
        public float pickupRange = 2.8f;

        public WeaponController Active { get; private set; }
        public WeaponController Primary => Find(primaryName);
        public WeaponController Secondary => Find(secondaryName);
        public bool Switching => switching;
        public bool Throwing => throwState != 0;
        /// <summary>Set by abilities (sword, chicken): no switching, grenades, knife or pickups.</summary>
        public bool ActionsBlocked { get; set; }
        public WeaponPickup LookedPickup { get; private set; }
        public event Action<WeaponController> ActiveChanged;

        bool switching;
        WeaponController pending;
        int throwState; // 0 none, 1 holding, 2 throwing
        float holdT, throwT;
        bool thrown;
        LineRenderer arc;
        Transform templatesRoot;
        readonly Dictionary<string, GameObject> templates = new Dictionary<string, GameObject>();

        public WeaponController Find(string n)
        {
            foreach (var w in weapons) if (w && w.weaponName == n) return w;
            return null;
        }

        /// <summary>The weapons chosen in the loadout menu differ from what we carry (applied on respawn).</summary>
        public bool ChoicePending =>
            (!DroneMode.Active && Find(PlayerLoadout.Primary) && primaryName != PlayerLoadout.Primary) ||
            (Find(PlayerLoadout.Secondary) && secondaryName != PlayerLoadout.Secondary) ||
            (melee && melee.VariantName.Length > 0 && melee.VariantName != PlayerLoadout.Melee);

        /// <summary>Takes the weapons chosen in the loadout menu (start of the match, respawn).</summary>
        public void ApplyPlayerChoice()
        {
            if (!GetComponent<PlayerHealth>() && !GetComponent<PlayerMotor>()) return;
            string p = PlayerLoadout.Primary, s = PlayerLoadout.Secondary;
            if (Find(p) && Find(p).slot == WeaponSlot.Primary) primaryName = p;
            if (Find(s) && Find(s).slot == WeaponSlot.Secondary) secondaryName = s;
            if (melee) melee.SetVariant(PlayerLoadout.Melee);
            if (DroneMode.Active) { primaryName = ""; grenades = 0; } // drone war: knife + pistol only
            var want = Primary ?? Secondary;
            bool stale = Active && Active.weaponName != primaryName && Active.weaponName != secondaryName;
            if (Active && want && Active != want && (stale || Active.slot == WeaponSlot.Primary))
            {
                Active.gameObject.SetActive(false);
                switching = false;
                Activate(want);
            }
        }

        void Start()
        {
            EnsureTemplates();
            ApplyPlayerChoice();
            foreach (var w in weapons) if (w) w.gameObject.SetActive(false);
            Active = Primary ?? Secondary;
            if (Active) Active.gameObject.SetActive(true);
            if (grenadeArm) grenadeArm.gameObject.SetActive(false);

            var go = new GameObject("GrenadeArc");
            go.transform.SetParent(transform, false);
            arc = go.AddComponent<LineRenderer>();
            arc.useWorldSpace = true;
            arc.widthMultiplier = 0.03f;
            arc.numCapVertices = 2;
            arc.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arc.sharedMaterial = FX.MakeParticleMat("Arc", Texture2D.whiteTexture, true, new Color(1f, 0.85f, 0.5f, 0.55f));
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f), new GradientAlphaKey(0.5f, 1f) });
            arc.colorGradient = g;
            arc.enabled = false;

            ActiveChanged?.Invoke(Active);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool meleeBusy = melee && melee.Busy;
            if (ActionsBlocked && throwState == 0) { LookedPickup = null; return; }

            if (!meleeBusy && throwState == 0 && !switching)
            {
                if (GameInput.Slot1Pressed) SwitchTo(Primary);
                else if (GameInput.Slot2Pressed) SwitchTo(Secondary);
                else if (GameInput.SwapPressed) SwitchTo(Active == Primary ? Secondary : Primary);
            }

            UpdatePickup();
            UpdateGrenade(dt, meleeBusy);
        }

        public void SwitchTo(WeaponController w)
        {
            if (!w || w == Active || switching) return;
            if (!Active) { Activate(w); return; }
            switching = true;
            pending = w;
            Active.Holster(() => Activate(pending));
        }

        void Activate(WeaponController w)
        {
            Active = w;
            switching = false;
            w.ExternalLower = 0f;
            w.gameObject.SetActive(true);
            ActiveChanged?.Invoke(w);
        }

        // ------------------------------------------------------------------ pickups

        void UpdatePickup()
        {
            LookedPickup = null;
            Transform ct = cam.transform;
            if (Physics.SphereCast(ct.position, 0.12f, ct.forward, out RaycastHit hit, pickupRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                LookedPickup = hit.collider.GetComponentInParent<WeaponPickup>();

            if (LookedPickup && GameInput.PickupPressed && !switching && throwState == 0 && !(melee && melee.Busy))
                Pickup(LookedPickup);
        }

        public void RefillAll()
        {
            foreach (var w in weapons) if (w) w.ResetAmmo();
            grenades = DroneMode.Active ? 0 : Mathf.Max(grenades, 2);
        }

        void Pickup(WeaponPickup p)
        {
            if (p.kind == WeaponPickup.Kind.Ammo)
            {
                foreach (var w in weapons) if (w) w.RefillAmmo();
                if (!DroneMode.Active) grenades = maxGrenades;
                ProcAudio.Play(Sfx.MagIn, 0.6f);
                playerCam.Punch(new Vector3(6f, 0f, 3f));
                return;
            }
            var nw = Find(p.weaponName);
            if (!nw) return;
            if (DroneMode.Active && nw.slot == WeaponSlot.Primary) { ProcAudio.Play(Sfx.Dry, 0.4f); return; } // sidearms only
            bool isPrimary = nw.slot == WeaponSlot.Primary;
            string current = isPrimary ? primaryName : secondaryName;
            if (current == nw.weaponName)
            {
                nw.RefillAmmo();
                ProcAudio.Play(Sfx.MagIn, 0.5f);
                Destroy(p.gameObject);
                return;
            }
            // drop the weapon currently in that slot
            var old = Find(current);
            if (old) DropWeapon(old.weaponName);
            if (isPrimary) primaryName = nw.weaponName; else secondaryName = nw.weaponName;
            nw.ResetAmmo();
            Destroy(p.gameObject);
            SwitchTo(nw);
        }

        void DropWeapon(string weaponName)
        {
            Transform ct = cam.transform;
            var go = new GameObject("Pickup_" + weaponName);
            go.transform.position = ct.position + ct.forward * 0.6f - Vector3.up * 0.3f;
            go.transform.rotation = Quaternion.LookRotation(ct.right, Vector3.up) * Quaternion.Euler(0f, 0f, 80f);
            var p = go.AddComponent<WeaponPickup>();
            p.kind = WeaponPickup.Kind.Weapon;
            p.weaponName = weaponName;
            p.physical = true;
            p.initialVelocity = motor.Velocity + ct.forward * 2.5f + Vector3.up * 1.2f;
        }

        // ------------------------------------------------------------------ world models (pickups, third person)

        void EnsureTemplates()
        {
            if (templatesRoot) return;
            templatesRoot = new GameObject("WeaponTemplates").transform;
            templatesRoot.gameObject.SetActive(false);
            foreach (var w in weapons)
            {
                if (!w || !w.model) continue;
                var clone = Instantiate(w.model.gameObject, templatesRoot);
                clone.name = w.weaponName;
                clone.transform.localPosition = Vector3.zero;
                clone.transform.localRotation = Quaternion.identity;
                foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                {
                    if (!t || t == clone.transform) continue;
                    string n = t.name;
                    if (n.Contains("Arm") || n.Contains("Flash") || n == "ShellInHand") DestroyImmediate(t.gameObject);
                }
                foreach (var l in clone.GetComponentsInChildren<Light>(true)) DestroyImmediate(l.gameObject);
                foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = true;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                templates[w.weaponName] = clone;
            }
        }

        public GameObject CreateWorldModel(string weaponName, Transform parent)
        {
            EnsureTemplates();
            if (!templates.TryGetValue(weaponName, out var t)) return null;
            var go = Instantiate(t, parent);
            go.name = weaponName + "_World";
            ImportHidden.Refresh(go);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.SetActive(true);
            return go;
        }

        // ------------------------------------------------------------------ grenades

        Vector3 ThrowOrigin => cam.transform.position + cam.transform.forward * 0.35f + cam.transform.right * 0.18f - cam.transform.up * 0.05f;
        Vector3 ThrowVelocity => (cam.transform.forward + cam.transform.up * 0.15f).normalized * throwSpeed + motor.Velocity * 0.6f;

        void UpdateGrenade(float dt, bool meleeBusy)
        {
            if (throwState == 0)
            {
                if (GameInput.GrenadePressed && grenades > 0 && !switching && !meleeBusy && Active)
                {
                    throwState = 1;
                    holdT = 0f;
                    thrown = false;
                    Active.ExternalLower = 1f;
                    Active.CancelReload();
                    if (grenadeArm) grenadeArm.gameObject.SetActive(true);
                    if (grenadeInHand) grenadeInHand.gameObject.SetActive(true);
                    ProcAudio.Play(Sfx.GrenadePin, 0.7f);
                }
                else if (GameInput.GrenadePressed && grenades == 0) ProcAudio.Play(Sfx.Dry, 0.4f);
                return;
            }

            if (throwState == 1)
            {
                holdT += dt;
                DrawArc();
                if (!GameInput.GrenadeHeld && holdT > 0.15f)
                {
                    throwState = 2;
                    throwT = 0f;
                    arc.enabled = false;
                }
            }
            else if (throwState == 2)
            {
                throwT += dt / 0.5f;
                if (!thrown && throwT >= 0.32f)
                {
                    thrown = true;
                    grenades--;
                    Grenade.Spawn(ThrowOrigin, ThrowVelocity, motor.Controller, GetComponent<Combatant>());
                    if (grenadeInHand) grenadeInHand.gameObject.SetActive(false);
                    ProcAudio.Play(Sfx.Throw, 0.7f);
                    playerCam.Punch(new Vector3(10f, -6f, -6f));
                }
                if (throwT >= 1f)
                {
                    throwState = 0;
                    if (grenadeArm) grenadeArm.gameObject.SetActive(false);
                    if (Active) Active.ExternalLower = 0f;
                }
            }
            PoseGrenadeArm();
        }

        void PoseGrenadeArm()
        {
            if (!grenadeArm) return;
            Vector3 pos; Vector3 rot;
            if (throwState == 1)
            {
                float u = Anim.Smooth(holdT / 0.22f);
                pos = Vector3.Lerp(new Vector3(0.28f, -0.5f, 0.2f), new Vector3(0.24f, -0.13f, 0.26f), u);
                rot = Vector3.Lerp(new Vector3(40f, -10f, 0f), new Vector3(-12f, -12f, 8f), u);
                pos += new Vector3(0f, Mathf.Sin(Time.time * 1.5f) * 0.004f, 0f);
            }
            else
            {
                var pk = new[]
                {
                    new KeyFrame(0f, 0.24f, -0.13f, 0.26f), new KeyFrame(0.22f, 0.3f, 0.0f, 0.05f),
                    new KeyFrame(0.36f, 0.08f, -0.02f, 0.5f), new KeyFrame(0.6f, -0.05f, -0.25f, 0.4f), new KeyFrame(1f, 0.05f, -0.6f, 0.2f)
                };
                var rk = new[]
                {
                    new KeyFrame(0f, -12f, -12f, 8f), new KeyFrame(0.22f, -55f, 5f, 15f),
                    new KeyFrame(0.36f, 30f, -15f, -10f), new KeyFrame(0.6f, 60f, -25f, -20f), new KeyFrame(1f, 70f, -10f, 0f)
                };
                pos = Anim.Eval(pk, throwT);
                rot = Anim.Eval(rk, throwT);
            }
            grenadeArm.localPosition = pos;
            grenadeArm.localRotation = Quaternion.Euler(rot);
        }

        readonly List<Vector3> arcPts = new List<Vector3>(64);

        void DrawArc()
        {
            arcPts.Clear();
            Vector3 p = ThrowOrigin, v = ThrowVelocity;
            arcPts.Add(p);
            const float step = 0.04f;
            for (int i = 0; i < 70; i++)
            {
                Vector3 np = p + v * step;
                v += Physics.gravity * step;
                if (Physics.Linecast(p, np, out RaycastHit h, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && !h.collider.transform.IsChildOf(motor.transform))
                {
                    arcPts.Add(h.point);
                    break;
                }
                arcPts.Add(np);
                p = np;
            }
            arc.positionCount = arcPts.Count;
            for (int i = 0; i < arcPts.Count; i++) arc.SetPosition(i, arcPts[i]);
            arc.enabled = true;
        }
    }
}
