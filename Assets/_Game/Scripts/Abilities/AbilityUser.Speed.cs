using UnityEngine;

namespace FPS
{
    /// <summary>
    /// "Гиперскорость": 7 seconds of absurd speed (×3.6). The body becomes slippery — it barely turns, drifts wide
    /// and takes ages to stop — so it is fast but hard to steer: overshooting corners and sliding into walls is part
    /// of the joke. Afterimage trail, wind rush, wide FOV and a shaking view for the player.
    /// </summary>
    public partial class AbilityUser
    {
        public const float HyperMul = 3.6f;

        float hyperT, hyperDur = 1f, hyperPuffT, hyperFov;
        TrailRenderer hyperTrail;
        static Material hyperTrailMat;

        public bool Hyper => hyperT > 0f;

        bool StartHyper(float dur)
        {
            hyperT = hyperDur = dur;
            var info = AbilityDB.Get(AbilityType.Hyperspeed);
            if (!hyperTrail)
            {
                if (!hyperTrailMat) hyperTrailMat = FX.MakeParticleMat("HyperTrail", Texture2D.whiteTexture, true, new Color(0.4f, 0.9f, 1.6f, 0.6f), true);
                var go = new GameObject("HyperTrail");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = Vector3.up * 1.1f;
                hyperTrail = go.AddComponent<TrailRenderer>();
                hyperTrail.sharedMaterial = hyperTrailMat;
                hyperTrail.time = 0.35f;
                hyperTrail.startWidth = 1.1f;
                hyperTrail.endWidth = 0.1f;
                hyperTrail.minVertexDistance = 0.2f;
                hyperTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hyperTrail.colorGradient = new Gradient
                {
                    colorKeys = new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    alphaKeys = new[] { new GradientAlphaKey(0.55f, 0f), new GradientAlphaKey(0f, 1f) }
                };
            }
            hyperTrail.Clear();
            hyperTrail.emitting = true;
            ProcAudio.PlayAt(Sfx.Warp, transform.position + Vector3.up, 0.9f, 0.05f);
            ProcAudio.PlayAt(Sfx.Slide, transform.position, 1f, 0.05f);
            FX.Ensure().DustBurst(transform.position, Vector3.up, 10, new Color(0.6f, 0.57f, 0.52f, 0.6f), new Color(0.5f, 0.47f, 0.43f, 0.45f), 0.9f);
            if (isPlayer) Say(info.name, "Вы очень быстрый. Слишком быстрый. Тормозить заранее!", info.color);
            return true;
        }

        void UpdateHyper(float dt)
        {
            bool on = hyperT > 0f && self.alive;
            if (hyperT > 0f) { hyperT -= dt; if (!self.alive) hyperT = 0f; }
            if (hyperTrail) hyperTrail.emitting = on;
            if (isPlayer)
            {
                motor.HyperSpeed = on ? HyperMul : 1f;
                if (!on) hyperFov = Anim.Damp(hyperFov, 0f, 6f, dt);
                if (on)
                {
                    // the faster you go, the more the view shakes and widens
                    float k = Mathf.Clamp01(motor.HorizontalSpeed / (motor.sprintSpeed * 2f));
                    pcam.Shake(0.12f + 0.25f * k);
                    hyperFov = Anim.Damp(hyperFov, 22f * k, 6f, dt);
                }
            }
            else if (bot)
            {
                bot.HyperMul = on ? HyperMul : 1f;
                var ag = bot.Agent;
                if (ag)
                {
                    // slippery agent: little grip, no braking — it overshoots and slides past its corners
                    ag.acceleration = on ? 7f : 22f;
                    ag.autoBraking = !on;
                }
            }
            if (!on) return;
            hyperPuffT -= dt;
            if (hyperPuffT <= 0f)
            {
                hyperPuffT = 0.07f;
                FX.Ensure().Puff(transform.position + Vector3.up * 0.1f, Vector3.up, 2, 0.6f, new Vector2(0.2f, 0.8f), new Vector2(0.3f, 0.6f), new Vector2(0.3f, 0.6f),
                                 new Color(0.6f, 0.58f, 0.55f, 0.45f), new Color(0.7f, 0.68f, 0.64f, 0.35f));
            }
        }

        void EndHyper()
        {
            hyperT = 0f;
            if (hyperTrail) hyperTrail.emitting = false;
            if (isPlayer && motor) motor.HyperSpeed = 1f;
            else if (bot)
            {
                bot.HyperMul = 1f;
                if (bot.Agent) { bot.Agent.acceleration = 22f; bot.Agent.autoBraking = true; }
            }
        }
    }
}
