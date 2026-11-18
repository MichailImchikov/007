using UnityEngine;

namespace FPS
{
    /// <summary>
    /// The fart's gas (on the cloud's SmokeCloud): the farter standing inside heals fast, enemies inside choke and
    /// take damage. Allies are left alone. Runs only on the caster's machine; damage to fighters simulated elsewhere
    /// goes through their network proxies like any other hit.
    /// </summary>
    public class GasCloud : MonoBehaviour
    {
        public const float HealPerSecond = 28f, DamagePerSecond = 14f;
        const float Tick = 0.25f;

        SmokeCloud cloud;
        Combatant owner;
        float maxRadius, tick, age;
        bool told;

        public static void Attach(SmokeCloud cloud, Combatant owner, float radius)
        {
            if (!cloud || !owner) return;
            var g = cloud.gameObject.AddComponent<GasCloud>();
            g.cloud = cloud;
            g.owner = owner;
            g.maxRadius = radius;
        }

        bool Inside(Combatant c, float r)
        {
            Vector3 d = c.transform.position - transform.position;
            float h = d.y;
            d.y = 0f;
            return d.magnitude < r && h > -1.5f && h < 3f;
        }

        void Update()
        {
            if (!cloud || !owner) return;
            age += Time.deltaTime;
            tick -= Time.deltaTime;
            if (tick > 0f) return;
            tick = Tick;
            // the cloud's current radius (it grows, then fades); the fresh puff already works at half size
            float r = age < 1.4f ? Mathf.Max(cloud.Radius, maxRadius * 0.5f) : cloud.Radius;
            if (r < 0.4f) return;

            foreach (var c in Combatant.All)
            {
                if (!c || !c.alive || !Inside(c, r)) continue;
                if (c == owner)
                {
                    Heal(c, HealPerSecond * Tick);
                    continue;
                }
                if (!owner.IsEnemyOf(c)) continue;
                var d = c.GetComponent<IDamageable>() ?? c.GetComponentInParent<IDamageable>();
                if (d == null) continue;
                var res = d.TakeDamage(new DamageInfo
                {
                    amount = DamagePerSecond * Tick, point = c.Chest, direction = Vector3.up, normal = Vector3.down,
                    attacker = owner, weapon = "Газ"
                });
                if (res.applied && Random.value < 0.35f) ProcAudio.PlayAt(Sfx.Hurt, c.Head, 0.35f, 0.25f); // coughing
            }
        }

        void Heal(Combatant c, float amount)
        {
            bool healed = false;
            var ph = c.GetComponent<PlayerHealth>();
            if (ph && ph.Health < ph.maxHealth) { ph.Heal(amount); healed = true; }
            var bot = c.GetComponent<CombatBot>();
            if (bot && bot.Health01 < 1f) { bot.Heal(amount); healed = true; }
            if (!healed) return;
            // little green sparkles rising from the farter
            FX.Ensure().Puff(c.transform.position + Vector3.up * 0.9f, Vector3.up, 3, 0.4f, new Vector2(0.6f, 1.4f), new Vector2(0.08f, 0.16f), new Vector2(0.5f, 0.9f),
                             new Color(0.45f, 1f, 0.35f, 0.9f), new Color(0.75f, 1f, 0.5f, 0.8f));
            if (ph && ph == PlayerHealth.Local && !told && c.abilities)
            {
                told = true;
                c.abilities.Notify("Лечение газом", "Пока стоите в облаке — здоровье быстро растёт", new Color(0.55f, 0.95f, 0.3f));
            }
        }
    }
}
