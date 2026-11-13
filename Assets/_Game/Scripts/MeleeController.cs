using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Knife on F: quick slash in front, or — when standing behind an unaware bot — a full
    /// first-person execution: the player is pulled into position, grabs the victim with the
    /// left hand, stabs, twists and pulls the knife out while the victim collapses.
    /// </summary>
    [DefaultExecutionOrder(45)]
    public class MeleeController : MonoBehaviour
    {
        public PlayerMotor motor;
        public PlayerCamera playerCam;
        public Camera cam;
        public Loadout loadout;
        public Transform knifeArm;
        public Transform execLeftArm;

        public float damage = 55f;
        public float backstabMultiplier = 2f;
        public float range = 1.9f;
        public float slashTime = 0.55f;
        public float executionTime = 1.9f;

        public bool Busy => state != 0;
        public IExecutable Candidate { get; private set; }
        public float SlashProgress => state == 1 ? t : -1f;
        public float ExecutionProgress => state == 2 ? t : -1f;

        int state; // 0 idle, 1 slash, 2 execution
        float t;
        bool hitDone, stabbed, twisted, pulled, swung;
        IExecutable victim;
        Combatant self;
        Vector3 startPos, targetPos;
        float startYaw, startPitch, targetYaw;

        static readonly KeyFrame[] SlashPos =
        {
            new KeyFrame(0f, 0.32f, -0.45f, 0.15f), new KeyFrame(0.18f, 0.3f, -0.06f, 0.26f), new KeyFrame(0.3f, 0.24f, -0.02f, 0.34f),
            new KeyFrame(0.45f, -0.2f, -0.16f, 0.42f), new KeyFrame(0.7f, -0.12f, -0.36f, 0.3f), new KeyFrame(1f, 0.1f, -0.55f, 0.18f)
        };
        static readonly KeyFrame[] SlashRot =
        {
            new KeyFrame(0f, 40f, -20f, -30f), new KeyFrame(0.18f, -10f, -40f, -70f), new KeyFrame(0.3f, -5f, -30f, -80f),
            new KeyFrame(0.45f, 20f, 50f, -110f), new KeyFrame(0.7f, 40f, 40f, -80f), new KeyFrame(1f, 50f, 0f, -30f)
        };
        static readonly KeyFrame[] ExecPos =
        {
            new KeyFrame(0f, 0.3f, -0.5f, 0.1f), new KeyFrame(0.18f, 0.17f, -0.1f, 0.25f), new KeyFrame(0.33f, 0.2f, 0.02f, 0.16f),
            new KeyFrame(0.4f, 0.03f, -0.05f, 0.5f), new KeyFrame(0.62f, 0.03f, -0.06f, 0.48f), new KeyFrame(0.72f, 0.04f, -0.06f, 0.47f),
            new KeyFrame(0.82f, 0.13f, -0.05f, 0.3f), new KeyFrame(1f, 0.3f, -0.55f, 0.1f)
        };
        static readonly KeyFrame[] ExecRot =
        {
            new KeyFrame(0f, 40f, -20f, -30f), new KeyFrame(0.18f, -25f, -10f, -80f), new KeyFrame(0.33f, -50f, -5f, -90f),
            new KeyFrame(0.4f, 10f, -5f, -90f), new KeyFrame(0.62f, 10f, -5f, -95f), new KeyFrame(0.72f, 10f, -5f, -145f),
            new KeyFrame(0.82f, -15f, -5f, -90f), new KeyFrame(1f, 40f, -20f, -30f)
        };
        static readonly KeyFrame[] LeftPos =
        {
            new KeyFrame(0f, -0.3f, -0.55f, 0.1f), new KeyFrame(0.16f, -0.1f, -0.02f, 0.48f), new KeyFrame(0.4f, -0.09f, 0.0f, 0.44f),
            new KeyFrame(0.75f, -0.1f, -0.02f, 0.43f), new KeyFrame(0.86f, -0.25f, -0.3f, 0.35f), new KeyFrame(1f, -0.3f, -0.6f, 0.1f)
        };
        static readonly KeyFrame[] LeftRot =
        {
            new KeyFrame(0f, 30f, 20f, 40f), new KeyFrame(0.16f, -25f, 10f, 75f), new KeyFrame(0.4f, -30f, 10f, 80f),
            new KeyFrame(0.75f, -20f, 10f, 70f), new KeyFrame(0.86f, 10f, 15f, 40f), new KeyFrame(1f, 30f, 20f, 40f)
        };

        /// <summary>Current melee weapon (MeleeCatalog name).</summary>
        public string VariantName { get; private set; } = "";
        Transform variantModel;
        static readonly string[] ProceduralBlade = { "Handle", "Guard", "Blade", "BladeTip", "Spine" };

        /// <summary>Switches the melee weapon: stats plus the model in the knife hand.</summary>
        public void SetVariant(string variant)
        {
            var d = MeleeCatalog.Get(variant);
            if (d.name == VariantName) return;
            VariantName = d.name;
            damage = d.damage; range = d.range; slashTime = d.slashTime; backstabMultiplier = d.backstab;
            if (!knifeArm) return;
            if (variantModel) { Destroy(variantModel.gameObject); variantModel = null; }

            Transform model = null;
            var db = WeaponModelDB.Instance;
            if (d.name == "Меч")
            {
                model = new GameObject("MeleeModel").transform;
                model.SetParent(knifeArm, false);
                var sword = AbilityProps.BuildSword(model, false);
                sword.localRotation = Quaternion.Euler(90f, 0f, 0f);
                sword.localScale = Vector3.one * 0.8f;
                model.localPosition = new Vector3(0f, 0.004f, 0.06f);
            }
            else if (d.name == "Боевой нож" && db && db.knifePrefab)
            {
                model = new GameObject("MeleeModel").transform;
                model.SetParent(knifeArm, false);
                var k = Instantiate(db.knifePrefab, model, false).transform;
                k.localRotation = Quaternion.Euler(db.knifeEuler);
                k.localScale = Vector3.one * db.knifeScale;
                k.localPosition = Vector3.zero;
                model.localPosition = db.knifeOffset;
                foreach (var c in k.GetComponentsInChildren<Collider>(true)) Destroy(c);
            }
            bool procedural = model == null;
            foreach (var n in ProceduralBlade) { var t = knifeArm.Find(n); if (t) t.gameObject.SetActive(procedural); }
            if (model)
            {
                foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = knifeArm.gameObject.layer;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                variantModel = model;
            }
        }

        void Start()
        {
            self = GetComponent<Combatant>();
            if (VariantName.Length == 0 && GetComponent<PlayerMotor>()) SetVariant(PlayerLoadout.Melee);
            if (GetComponent<PlayerMotor>() && !GetComponent<Flashlight>()) gameObject.AddComponent<Flashlight>();
            if (knifeArm) knifeArm.gameObject.SetActive(false);
            if (execLeftArm) execLeftArm.gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (state == 0)
            {
                Candidate = FindCandidate();
                bool blocked = loadout && (loadout.Switching || loadout.Throwing || loadout.ActionsBlocked);
                if (GameInput.MeleePressed && !blocked && !motor.IsVaulting)
                {
                    if (Candidate != null) StartExecution(Candidate);
                    else StartSlash();
                }
                return;
            }
            Candidate = null;
            if (state == 1) UpdateSlash(dt);
            else UpdateExecution(dt);
        }

        IExecutable FindCandidate()
        {
            if (motor.IsProne || motor.IsSliding || !motor.IsGrounded) return null;
            IExecutable best = null;
            float bestD = float.MaxValue;
            Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
            foreach (var b in Executables.All)
            {
                if (b == null || !b.CanBeExecutedBy(transform)) continue;
                Vector3 to = b.transform.position - transform.position; to.y = 0f;
                if (Vector3.Angle(fwd, to) > 55f) continue;
                float d = to.sqrMagnitude;
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        void SetActive(WeaponController w, float lower) { if (w) w.ExternalLower = lower; }

        // ------------------------------------------------------------------ slash

        void StartSlash()
        {
            state = 1;
            t = 0f;
            hitDone = swung = false;
            SetActive(loadout ? loadout.Active : null, 1f);
            if (knifeArm) knifeArm.gameObject.SetActive(true);
        }

        void UpdateSlash(float dt)
        {
            t += dt / slashTime;
            if (!swung && t >= 0.22f) { swung = true; ProcAudio.Play(Sfx.KnifeSwing, 0.6f, 0.1f); playerCam.Punch(new Vector3(4f, -12f, 6f)); }
            if (!hitDone && t >= 0.33f) { hitDone = true; DoSlashHit(); }
            PoseKnife(SlashPos, SlashRot, t);
            if (t >= 1f) Finish();
        }

        void DoSlashHit()
        {
            Transform ct = cam.transform;
            if (!Physics.SphereCast(ct.position, 0.22f, ct.forward, out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return;
            if (hit.collider.transform.IsChildOf(transform)) return;
            var hb = hit.collider.GetComponent<Hitbox>();
            IDamageable d = hb ? hb.Owner : hit.collider.GetComponent<IDamageable>();
            var bot = hit.collider.GetComponentInParent<HumanoidRig>();
            if (d != null)
            {
                float amount = damage;
                bool crit = false;
                if (bot && Vector3.Dot(bot.transform.parent ? bot.transform.parent.forward : bot.transform.forward, ct.forward) > 0.5f) { amount *= backstabMultiplier; crit = true; }
                var res = d.TakeDamage(new DamageInfo { amount = amount, point = hit.point, normal = hit.normal, direction = ct.forward, critical = crit, attacker = self, weapon = VariantName.Length > 0 ? VariantName : "Нож" });
                if (res.applied)
                {
                    HUD.Ensure().ShowHitmarker(res.killed, crit);
                    HUD.Ensure().RegisterHit(res.killed);
                }
            }
            if (bot)
            {
                FX.Ensure().Blood(hit.point, -ct.forward, 12);
                ProcAudio.PlayAt(Sfx.KnifeHit, hit.point, 0.9f);
            }
            else
            {
                var tag = hit.collider.GetComponentInParent<SurfaceTag>();
                FX.Ensure().Impact(hit, d != null || (tag && tag.kind == SurfaceKind.Metal) ? SurfaceKind.Metal : SurfaceKind.Concrete, false);
                ProcAudio.PlayAt(Sfx.KnifeClang, hit.point, 0.7f);
            }
            if (hit.rigidbody && !hit.rigidbody.isKinematic) hit.rigidbody.AddForceAtPosition(ct.forward * 3f, hit.point, ForceMode.Impulse);
            playerCam.Punch(new Vector3(10f, 15f, -10f));
        }

        // ------------------------------------------------------------------ execution

        public void StartExecution(IExecutable b)
        {
            if (state != 0 || b == null) return;
            state = 2;
            t = 0f;
            victim = b;
            stabbed = twisted = pulled = false;
            b.BeginExecution();
            motor.Locked = true;
            playerCam.Locked = true;
            startPos = transform.position;
            startYaw = playerCam.Yaw;
            startPitch = playerCam.Pitch;
            targetYaw = b.transform.eulerAngles.y;
            targetPos = b.transform.position - b.transform.forward * 0.62f;
            targetPos.y = transform.position.y;
            SetActive(loadout ? loadout.Active : null, 1f);
            if (knifeArm) knifeArm.gameObject.SetActive(true);
            if (execLeftArm) execLeftArm.gameObject.SetActive(true);
            ProcAudio.Play(Sfx.KnifeSwing, 0.4f, 0.05f);
            ProcAudio.Play(Sfx.Vault, 0.4f, 0.05f);
        }

        void UpdateExecution(float dt)
        {
            t += dt / executionTime;
            float m = Anim.Smooth(t / 0.16f);
            transform.position = Vector3.Lerp(startPos, targetPos, m);
            float yaw = Mathf.LerpAngle(startYaw, targetYaw, m);
            float pitch = Mathf.Lerp(startPitch, 18f, m);
            // lean in with the stab, follow the victim down when he collapses
            pitch += 6f * Mathf.Sin(Mathf.Clamp01((t - 0.35f) / 0.4f) * Mathf.PI) + 14f * Anim.Smooth((t - 0.82f) / 0.15f);
            playerCam.SetLook(yaw, pitch);

            if (victim != null) victim.SetExecutionProgress(t);

            if (!stabbed && t >= 0.4f)
            {
                stabbed = true;
                ProcAudio.Play(Sfx.Stab, 0.9f);
                playerCam.Punch(new Vector3(-18f, 6f, 10f));
                playerCam.PunchPos(new Vector3(0f, 0f, 1.2f));
                if (victim != null)
                {
                    FX.Ensure().Blood(victim.NeckPoint, -cam.transform.forward + Vector3.up * 0.3f, 18);
                    victim.ExecutionKilled(self);
                }
                HUD.Ensure().ShowHitmarker(true, true);
                HUD.Ensure().RegisterHit(true);
                ProcAudio.Play(Sfx.Kill, 0.5f);
            }
            if (!twisted && t >= 0.68f)
            {
                twisted = true;
                ProcAudio.Play(Sfx.KnifeHit, 0.5f);
                playerCam.Punch(new Vector3(0f, 0f, -14f));
                if (victim != null) FX.Ensure().Blood(victim.NeckPoint, Vector3.up, 6);
            }
            if (!pulled && t >= 0.8f)
            {
                pulled = true;
                ProcAudio.Play(Sfx.KnifeSwing, 0.4f);
                playerCam.Punch(new Vector3(8f, 0f, 0f));
                if (victim != null) FX.Ensure().Blood(victim.NeckPoint, -cam.transform.forward, 10);
            }

            PoseKnife(ExecPos, ExecRot, t);
            if (execLeftArm)
            {
                execLeftArm.localPosition = Anim.Eval(LeftPos, t);
                execLeftArm.localRotation = Quaternion.Euler(Anim.Eval(LeftRot, t));
            }
            if (t >= 1f)
            {
                if (victim != null) victim.EndExecution();
                victim = null;
                motor.Locked = false;
                playerCam.Locked = false;
                Finish();
            }
        }

        void PoseKnife(KeyFrame[] pk, KeyFrame[] rk, float time)
        {
            if (!knifeArm) return;
            knifeArm.localPosition = Anim.Eval(pk, time);
            knifeArm.localRotation = Quaternion.Euler(Anim.Eval(rk, time));
        }

        void Finish()
        {
            state = 0;
            t = 0f;
            if (knifeArm) knifeArm.gameObject.SetActive(false);
            if (execLeftArm) execLeftArm.gameObject.SetActive(false);
            SetActive(loadout ? loadout.Active : null, 0f);
        }
    }
}
