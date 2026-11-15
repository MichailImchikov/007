using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Plays procedural IK motions (IKMotion assets: equip, landing, jump, bolt pull, inspect...) on the master pivot
    /// (KINEMATION's SlotLayer + DynamicMotion). A new motion blends from the previous one's current output.
    /// Improvement: several motions can be layered (additive channel), each with its own scale.
    /// </summary>
    public class SlotLayer : AnimLayer
    {
        struct Motion
        {
            public IKMotion asset;
            public float playBack, blendAlpha, length, scale;
            public LocRot cached, output;
            public bool Active => asset && playBack < length;
        }

        Motion main, additive;

        /// <summary>Plays a motion on the main channel (replaces the current one with a blend).</summary>
        public void PlayMotion(IKMotion motion, float scale = 1f) => Start(ref main, motion, scale);

        /// <summary>Plays a motion on the additive channel (stacks over the main one: landing over an equip).</summary>
        public void PlayAdditive(IKMotion motion, float scale = 1f) => Start(ref additive, motion, scale);

        public void Stop() { main.asset = null; additive.asset = null; main.output = additive.output = LocRot.Identity; }

        static void Start(ref Motion m, IKMotion asset, float scale)
        {
            if (!asset) return;
            m.cached = m.output;
            m.asset = asset;
            m.playBack = 0f;
            m.blendAlpha = 0f;
            m.length = Mathf.Max(0.0001f, asset.Length);
            m.scale = scale * asset.scale;
        }

        public override void OnAnimStart() { main.output = additive.output = LocRot.Identity; }

        static void Step(ref Motion m, float dt)
        {
            if (!m.Active) { m.output = LocRot.Lerp(m.output, LocRot.Identity, AnimMath.DampAlpha(12f, dt)); return; }
            m.playBack = Mathf.Min(m.playBack + dt * (m.asset.playRate > 0f ? m.asset.playRate : 1f), m.length);
            var cur = new LocRot(m.asset.loc.Evaluate(m.playBack) * m.scale, Quaternion.Euler(m.asset.rot.Evaluate(m.playBack) * m.scale));
            m.blendAlpha = Mathf.Min(1f, m.blendAlpha + dt * (m.asset.blendSpeed > 0f ? m.asset.blendSpeed : 1000f));
            m.output = LocRot.Lerp(m.cached, cur, m.blendAlpha);
            if (m.playBack >= m.length) m.cached = m.output;
        }

        public override void OnAnimUpdate()
        {
            float dt = DeltaTime;
            Step(ref main, dt);
            Step(ref additive, dt);
            var basePose = new LocRot(MasterPivot);
            foreach (var o in new[] { main.output, additive.output })
            {
                AnimMath.MoveInSpace(RootBone, MasterPivot, o.position, 1f);
                AnimMath.RotateInSpace(RootBone.rotation, MasterPivot, o.Rot, 1f);
            }
            BlendMaster(basePose, smoothLayerAlpha);
        }
    }
}
