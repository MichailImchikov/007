using UnityEngine;

namespace FPS.Animation
{
    /// <summary>
    /// Procedural motion of the weapon / arms (KINEMATION's IKAnimation): translation (m) and rotation (degrees)
    /// curves over seconds, played by the SlotLayer on the master pivot. Equip, landing, jump, inspect, bolt pulls...
    /// </summary>
    [CreateAssetMenu(fileName = "NewIKMotion", menuName = "FPS/Animation/IK Motion")]
    public class IKMotion : ScriptableObject
    {
        public VectorCurve rot = new VectorCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f));
        public VectorCurve loc = new VectorCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0f));
        [Tooltip("1 / seconds to blend from the previous motion")] public float blendSpeed = 10f;
        public float playRate = 1f;
        [Tooltip("Multiplier of the whole motion")] public float scale = 1f;

        public float Length => Mathf.Max(rot.LastTime, loc.LastTime);

        /// <summary>Builds a motion from keyframes (time, position, euler).</summary>
        public static IKMotion Create(string name, (float t, Vector3 pos, Vector3 euler)[] keys, float blendSpeed = 12f)
        {
            var m = CreateInstance<IKMotion>();
            m.name = name;
            var px = new AnimationCurve(); var py = new AnimationCurve(); var pz = new AnimationCurve();
            var rx = new AnimationCurve(); var ry = new AnimationCurve(); var rz = new AnimationCurve();
            foreach (var k in keys)
            {
                px.AddKey(k.t, k.pos.x); py.AddKey(k.t, k.pos.y); pz.AddKey(k.t, k.pos.z);
                rx.AddKey(k.t, k.euler.x); ry.AddKey(k.t, k.euler.y); rz.AddKey(k.t, k.euler.z);
            }
            foreach (var c in new[] { px, py, pz, rx, ry, rz })
                for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            m.loc = new VectorCurve(px, py, pz);
            m.rot = new VectorCurve(rx, ry, rz);
            m.blendSpeed = blendSpeed;
            return m;
        }
    }
}
