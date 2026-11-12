using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Pop-up range target on a hinge: wobbles when hit, flashes, shows damage numbers,
    /// falls backwards with a bounce when killed and springs back up after a delay.
    /// With canFall = false it behaves as a training dummy that heals itself.
    /// </summary>
    public class Target : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        public bool canFall = true;
        public float resetDelay = 2.5f;
        public float fallAngle = 86f;
        public Transform hinge;
        public Renderer[] flashRenderers;
        public Transform healthBarRoot;
        public Transform healthBarFill;

        public bool IsDown => down;
        public float Health => health;

        float health, angle, angVel, fallDir = -1f, downTimer, flash, lastHitTime = -10f, hpShown = 1f;
        bool down, rising;
        Color flashColor = Color.white;
        MaterialPropertyBlock mpb;
        Color[] baseColors;
        Camera cam;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            health = maxHealth;
            mpb = new MaterialPropertyBlock();
            if (flashRenderers == null) flashRenderers = new Renderer[0];
            baseColors = new Color[flashRenderers.Length];
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                var m = flashRenderers[i] ? flashRenderers[i].sharedMaterial : null;
                baseColors[i] = m && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
            }
            if (healthBarRoot) healthBarRoot.gameObject.SetActive(false);
        }

        public DamageResult TakeDamage(DamageInfo info)
        {
            if (down) return default;
            health -= info.amount;
            bool killed = health <= 0f;
            DamageNumbers.Spawn(info.point, info.amount, info.critical, killed);

            flash = 1f;
            flashColor = killed ? new Color(1f, 0.3f, 0.2f) : info.critical ? new Color(1f, 0.85f, 0.3f) : Color.white;
            lastHitTime = Time.time;

            // push the plate away from the bullet, harder the higher it was hit
            float push = Vector3.Dot(info.direction, transform.forward) < 0f ? -1f : 1f;
            float lever = hinge ? Mathf.Clamp(hinge.InverseTransformPoint(info.point).y, 0.3f, 1.5f) : 1f;
            angVel += push * (35f + info.amount * 2.2f) * lever;

            if (killed)
            {
                if (canFall)
                {
                    down = true;
                    rising = false;
                    downTimer = resetDelay;
                    fallDir = push;
                    angVel = push * Mathf.Max(Mathf.Abs(angVel), 140f);
                    ProcAudio.PlayAt(Sfx.TargetDown, transform.position, 0.8f);
                }
                else
                {
                    health = maxHealth;
                }
            }
            return new DamageResult { applied = true, killed = killed, dealt = info.amount };
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (!cam) cam = Camera.main;

            int steps = 4;
            float h = dt / steps;
            for (int s = 0; s < steps; s++)
            {
                if (down)
                {
                    // inverted pendulum falling under gravity, bouncing on the ground
                    angVel += (700f * Mathf.Sin(angle * Mathf.Deg2Rad) + 160f * fallDir) * h;
                    angle += angVel * h;
                    if (Mathf.Abs(angle) >= fallAngle)
                    {
                        angle = fallAngle * Mathf.Sign(angle);
                        if (Mathf.Abs(angVel) > 45f)
                        {
                            if (Mathf.Abs(angVel) > 200f) ProcAudio.PlayAt(Sfx.ImpactMetal, transform.position, 0.25f, 0.2f);
                            angVel = -angVel * 0.32f;
                        }
                        else angVel = 0f;
                    }
                }
                else
                {
                    float k = rising ? 70f : 150f;
                    float c = rising ? 12f : 8.5f;
                    angVel += (-k * angle - c * angVel) * h;
                    angle += angVel * h;
                    angle = Mathf.Clamp(angle, -fallAngle, fallAngle);
                }
            }

            if (down)
            {
                downTimer -= dt;
                if (downTimer <= 0f)
                {
                    down = false;
                    rising = true;
                    health = maxHealth;
                    hpShown = 1f;
                    ProcAudio.PlayAt(Sfx.TargetUp, transform.position, 0.7f);
                }
            }
            if (rising && Mathf.Abs(angle) < 2f) rising = false;

            if (!canFall && Time.time - lastHitTime > 2.5f) health = maxHealth;

            if (hinge) hinge.localRotation = Quaternion.Euler(angle, 0f, 0f);

            // hit flash
            if (flash > 0f)
            {
                flash = Mathf.MoveTowards(flash, 0f, dt * 7f);
                float f = flash * flash;
                for (int i = 0; i < flashRenderers.Length; i++)
                {
                    var r = flashRenderers[i];
                    if (!r) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor(BaseColorId, Color.Lerp(baseColors[i], flashColor, f * 0.7f));
                    mpb.SetColor(EmissionId, flashColor * (f * 2.5f));
                    r.SetPropertyBlock(mpb);
                }
            }

            // health bar
            if (healthBarRoot)
            {
                bool show = !down && Time.time - lastHitTime < 3f;
                if (healthBarRoot.gameObject.activeSelf != show) healthBarRoot.gameObject.SetActive(show);
                if (show)
                {
                    hpShown = Anim.Damp(hpShown, Mathf.Clamp01(health / maxHealth), 12f, dt);
                    if (healthBarFill) healthBarFill.localScale = new Vector3(Mathf.Max(0.001f, hpShown), 1f, 1f);
                    if (cam) healthBarRoot.rotation = cam.transform.rotation;
                }
            }
        }
    }
}
