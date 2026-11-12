using UnityEngine;
using UnityEngine.InputSystem;

namespace FPS
{
    public struct DamageInfo
    {
        public float amount;
        public Vector3 point;
        public Vector3 normal;
        public Vector3 direction;
        public bool critical;
        public Combatant attacker;
        public string weapon;
        /// <summary>Damage forwarded by the network (the shooter already showed the impact effects).</summary>
        public bool fromNetwork;
    }

    /// <summary>Anything that can be knifed from behind with a first-person execution.</summary>
    public interface IExecutable
    {
        Transform transform { get; }
        Vector3 NeckPoint { get; }
        bool CanBeExecutedBy(Transform attacker);
        void BeginExecution();
        void SetExecutionProgress(float t);
        void ExecutionKilled(Combatant killer);
        void EndExecution();
    }

    public static class Executables
    {
        public static readonly System.Collections.Generic.List<IExecutable> All = new System.Collections.Generic.List<IExecutable>();
    }

    /// <summary>Global AI hearing: gunshots and explosions alert nearby bots.</summary>
    public static class AIEvents
    {
        public static event System.Action<Vector3, float, Combatant> Noise;
        public static void Emit(Vector3 pos, float radius, Combatant source) => Noise?.Invoke(pos, radius, source);
    }

    public struct DamageResult
    {
        public bool applied;
        public bool killed;
        public float dealt;
    }

    public interface IDamageable
    {
        DamageResult TakeDamage(DamageInfo info);
    }

    public enum SurfaceKind { Concrete, Metal, Wood }

    /// <summary>Damped spring for procedural animation (position or euler angles).</summary>
    public class Spring3
    {
        public float stiffness, damping;
        public Vector3 value, velocity, target;

        public Spring3(float stiffness, float damping)
        {
            this.stiffness = stiffness;
            this.damping = damping;
        }

        public Vector3 Step(float dt)
        {
            if (dt <= 0f) return value;
            int n = Mathf.Clamp(Mathf.CeilToInt(dt / 0.004f), 1, 20);
            float h = dt / n;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = (target - value) * stiffness - velocity * damping;
                velocity += a * h;
                value += velocity * h;
            }
            return value;
        }

        public void Kick(Vector3 v) => velocity += v;
    }

    public struct KeyFrame
    {
        public float t;
        public Vector3 v;
        public KeyFrame(float t, float x, float y, float z) { this.t = t; v = new Vector3(x, y, z); }
        public KeyFrame(float t, Vector3 v) { this.t = t; this.v = v; }
    }

    public static class Anim
    {
        /// <summary>Catmull-Rom evaluation through keyframes (smooth, flowing motion).</summary>
        public static Vector3 Eval(KeyFrame[] k, float t)
        {
            if (k == null || k.Length == 0) return Vector3.zero;
            if (t <= k[0].t) return k[0].v;
            int last = k.Length - 1;
            if (t >= k[last].t) return k[last].v;
            for (int i = 0; i < last; i++)
            {
                if (t > k[i + 1].t) continue;
                float u = Mathf.InverseLerp(k[i].t, k[i + 1].t, t);
                Vector3 p0 = k[Mathf.Max(i - 1, 0)].v, p1 = k[i].v, p2 = k[i + 1].v, p3 = k[Mathf.Min(i + 2, last)].v;
                float u2 = u * u, u3 = u2 * u;
                return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
            }
            return k[last].v;
        }

        public static float Damp(float a, float b, float lambda, float dt) => Mathf.Lerp(a, b, 1f - Mathf.Exp(-lambda * dt));
        public static Vector3 Damp(Vector3 a, Vector3 b, float lambda, float dt) => Vector3.Lerp(a, b, 1f - Mathf.Exp(-lambda * dt));
        public static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    }
}
