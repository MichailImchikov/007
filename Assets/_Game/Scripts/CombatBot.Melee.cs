using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Close-quarters knife of a Deathmatch bot: face to face it stabs instead of shooting, and an enemy knife swing
    /// within reach is sometimes answered with its own — two blades at once lock into a <see cref="KnifeDuel"/>.
    /// </summary>
    public partial class CombatBot
    {
        float meleeT = -1f, meleeCd, seenSwing = -99f;
        bool meleeHit, meleeAnim;

        /// <summary>A knife duel took over: drop the stab in progress.</summary>
        public void CancelMelee()
        {
            meleeT = -1f;
            meleeHit = true;
            meleeAnim = false;
        }

        float CounterChance => difficulty == 2 ? 0.75f : difficulty == 0 ? 0.35f : 0.55f;

        /// <summary>Engage hook. True while the knife is out (no shooting this frame).</summary>
        bool MeleeTick(float dt, float dist)
        {
            meleeCd -= dt;
            if (meleeT >= 0f)
            {
                meleeT += dt / 0.5f;
                if (!meleeHit && meleeT >= 0.36f) { meleeHit = true; MeleeStab(); }
                if (anim) anim.knife = Mathf.Min(meleeT, 1f);
                if (meleeT >= 1f) { meleeT = -1f; if (anim) anim.knife = -1f; meleeAnim = false; }
                if (agent.enabled) agent.isStopped = true;
                return true;
            }
            if (meleeAnim && anim) { anim.knife = -1f; meleeAnim = false; }
            if (!target || !target.alive || dist > 2.4f) return false;

            Vector3 to = target.transform.position - transform.position; to.y = 0f;
            bool facing = to.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, to) < 50f;
            if (!facing) return false;

            // an enemy blade is on its way: answer it once per swing
            bool counter = false;
            float swing = KnifeDuel.SwingTime(target);
            if (swing > seenSwing && Time.time - swing < 0.3f)
            {
                seenSwing = swing;
                counter = Random.value < CounterChance;
            }
            bool close = dist < 1.65f && meleeCd <= 0f;
            if (!counter && !close) return false;

            meleeT = 0f;
            meleeHit = false;
            meleeAnim = true;
            meleeCd = difficulty == 2 ? 0.7f : difficulty == 0 ? 1.3f : 1f;
            KnifeDuel.NoteSwing(combatant);
            ProcAudio.PlayAt(Sfx.KnifeSwing, transform.position + Vector3.up * 1.3f, 0.8f, 0.1f);
            if (agent.enabled) agent.isStopped = true;
            return true;
        }

        void MeleeStab()
        {
            if (!target || !target.alive || KnifeDuel.InDuel(combatant)) return;
            Vector3 to = target.transform.position - transform.position; to.y = 0f;
            if (to.magnitude > 2.2f) return;
            var d = target.GetComponent<IDamageable>() ?? target.GetComponentInParent<IDamageable>();
            if (d == null) return;
            Vector3 dir = (target.Chest - (rig ? rig.head.position : transform.position + Vector3.up * 1.6f)).normalized;
            bool back = Vector3.Dot(target.transform.forward, to.normalized) > 0.5f;
            var res = d.TakeDamage(new DamageInfo
            {
                amount = Mathf.Round((back ? 110f : 55f) * dmgMul), point = target.Chest, normal = -dir, direction = dir,
                critical = back, attacker = combatant, weapon = "Нож"
            });
            if (KnifeDuel.InDuel(combatant)) { meleeT = -1f; meleeAnim = false; return; } // the blades locked
            if (!res.applied) return;
            FX.Ensure().Blood(target.Chest, -dir, 10);
            ProcAudio.PlayAt(Sfx.KnifeHit, target.Chest, 0.9f);
        }
    }
}
