using UnityEngine;
using Random = UnityEngine.Random;

namespace FPS.Animation
{
    /// <summary>
    /// Procedural weapon recoil (KINEMATION's RecoilAnimation, same behavior):
    ///  • every shot picks random rotation / translation targets (RecoilProfile) and reaches them along the curves;
    ///  • semi auto: each shot restarts the semi curves from the current output (no pops);
    ///  • full auto: the auto curves loop at the fire rate and the output is smoothed (extra / smooth settings);
    ///  • noise (small accumulating jitter) and push-back layers on top.
    /// OutRot (degrees) / OutLoc (meters) feed the RecoilLayer through CharAnimData.recoilAnim.
    /// Improvements: deltaTime is passed in (pause / slow-motion safe), aiming blends continuously.
    /// </summary>
    public class RecoilAnimation
    {
        public Vector3 OutRot { get; private set; }
        public Vector3 OutLoc { get; private set; }
        public LocRot Output => new LocRot(OutLoc, Quaternion.Euler(OutRot));
        /// <summary>0..1 aiming down sights (blends the aim multipliers).</summary>
        public float aim;
        public FireMode fireMode;
        public RecoilProfile Profile => data;

        RecoilProfile data;
        float fireRate = 600f;
        bool looping, playing, smoothing;
        Vector3 targetRot, targetLoc, startRot, startLoc;
        Vector3 rawRot, rawLoc, smoothRot, smoothLoc;
        VectorCurve curRot, curLoc;
        bool3 restRot, restLoc;
        Vector2 noiseTarget, noiseOut;
        float pushTarget, pushOut;
        float lastFrameTime, playBack, lastShotTime = -10f;
        float time;

        struct bool3 { public bool x, y, z; public bool3(bool v) { x = y = z = v; } }

        public void Init(RecoilProfile profile, float rpm, FireMode mode)
        {
            data = profile;
            fireRate = Mathf.Max(1f, rpm);
            fireMode = mode;
            OutRot = OutLoc = Vector3.zero;
            targetRot = targetLoc = Vector3.zero;
            pushTarget = 0f;
            noiseTarget = Vector2.zero;
            looping = playing = smoothing = false;
        }

        float Interval => 60f / fireRate;

        public void Play()
        {
            if (!data) return;
            // semi state: semi fire mode, or the shots are further apart than the fire rate allows
            bool semi = fireMode == FireMode.Semi || (!looping && time - lastShotTime > Interval + 0.06f);
            if (semi)
            {
                if (smoothing && !looping) smoothing = false;
                SetupTransition(smoothRot, smoothLoc, data.recoilCurves.semiRotCurve, data.recoilCurves.semiLocCurve);
            }
            else if (!looping)
            {
                var c = data.recoilCurves;
                bool autoValid = c.autoRotCurve.IsValid && c.autoLocCurve.IsValid;
                smoothing = autoValid;
                var rot = autoValid ? c.autoRotCurve : c.semiRotCurve;
                var loc = autoValid ? c.autoLocCurve : c.semiLocCurve;
                CorrectAlpha(rot, loc, Interval);
                SetupTransition(startRot, startLoc, rot, loc);
                pushTarget = data.pushAmount;
                lastFrameTime = Interval;
                looping = true;
            }
            lastShotTime = time;
        }

        /// <summary>Trigger released: full auto finishes its last cycle.</summary>
        public void Stop()
        {
            if (!data || !looping) return;
            lastFrameTime = Mathf.Max(curRot.LastTime, curLoc.LastTime);
            playing = true;
            looping = false;
        }

        public void Update(float dt)
        {
            time += dt;
            if (!data) return;
            if (playing)
            {
                UpdateSolver(dt);
                UpdateTimeline(dt);
            }
            ApplySmoothing(dt);
            Vector3 loc = smoothLoc;
            ApplyNoise(ref loc, dt);
            ApplyPushback(ref loc, dt);
            OutRot = smoothRot;
            OutLoc = loc;
        }

        void CalculateTargetData()
        {
            float a = Mathf.Clamp01(aim);
            float pitch = Random.Range(data.pitch.x, data.pitch.y);
            float yawMin = Random.Range(data.yaw.x, data.yaw.y), yawMax = Random.Range(data.yaw.z, data.yaw.w);
            float yaw = Random.value >= 0.5f ? yawMax : yawMin;
            float rollMin = Random.Range(data.roll.x, data.roll.y), rollMax = Random.Range(data.roll.z, data.roll.w);
            float roll = Random.value >= 0.5f ? rollMax : rollMin;
            if (data.smoothRoll && targetRot.z * roll > 0f) roll = -roll;
            float kick = Random.Range(data.kickback.x, data.kickback.y);
            float kickRight = Random.Range(data.kickRight.x, data.kickRight.y);
            float kickUp = Random.Range(data.kickUp.x, data.kickUp.y);

            noiseTarget.x += Random.Range(data.noiseX.x, data.noiseX.y);
            noiseTarget.y += Random.Range(data.noiseY.x, data.noiseY.y);
            noiseTarget *= Mathf.Lerp(1f, data.noiseScalar, a);

            pitch *= Mathf.Lerp(1f, data.aimRot.x, a);
            yaw *= Mathf.Lerp(1f, data.aimRot.y, a);
            roll *= Mathf.Lerp(1f, data.aimRot.z, a);
            kick *= Mathf.Lerp(1f, data.aimLoc.z, a);
            kickRight *= Mathf.Lerp(1f, data.aimLoc.x, a);
            kickUp *= Mathf.Lerp(1f, data.aimLoc.y, a);

            targetRot = new Vector3(pitch, yaw, roll);
            targetLoc = new Vector3(kickRight, kickUp, kick);
        }

        void UpdateTimeline(float dt)
        {
            playBack = Mathf.Clamp(playBack + dt * data.playRate, 0f, lastFrameTime);
            if (playBack >= lastFrameTime - 1e-5f)
            {
                playBack = 0f;
                playing = looping;
            }
        }

        void UpdateSolver(float dt)
        {
            if (playBack <= 0f) CalculateTargetData();
            float lastPlayback = Mathf.Max(playBack - dt * data.playRate, 0f);

            Vector3 alpha = curRot.Evaluate(playBack), lastAlpha = curRot.Evaluate(lastPlayback);
            rawRot = new Vector3(
                Mathf.LerpUnclamped(CorrectStart(lastAlpha.x, alpha.x, ref restRot.x, ref startRot.x), targetRot.x, alpha.x),
                Mathf.LerpUnclamped(CorrectStart(lastAlpha.y, alpha.y, ref restRot.y, ref startRot.y), targetRot.y, alpha.y),
                Mathf.LerpUnclamped(CorrectStart(lastAlpha.z, alpha.z, ref restRot.z, ref startRot.z), targetRot.z, alpha.z));

            alpha = curLoc.Evaluate(playBack); lastAlpha = curLoc.Evaluate(lastPlayback);
            rawLoc = new Vector3(
                Mathf.LerpUnclamped(CorrectStart(lastAlpha.x, alpha.x, ref restLoc.x, ref startLoc.x), targetLoc.x, alpha.x),
                Mathf.LerpUnclamped(CorrectStart(lastAlpha.y, alpha.y, ref restLoc.y, ref startLoc.y), targetLoc.y, alpha.y),
                Mathf.LerpUnclamped(CorrectStart(lastAlpha.z, alpha.z, ref restLoc.z, ref startLoc.z), targetLoc.z, alpha.z));
        }

        /// <summary>Once the curve starts returning, the start value is dropped (the recoil returns to zero, not to the old start).</summary>
        float CorrectStart(float last, float current, ref bool canRest, ref float start)
        {
            if (Mathf.Abs(last) > Mathf.Abs(current) && canRest && !looping) { start = 0f; canRest = false; }
            return start;
        }

        void ApplySmoothing(float dt)
        {
            if (!smoothing) { smoothRot = rawRot; smoothLoc = rawLoc; return; }
            float Interp(float a, float b, float speed, float scale)
            {
                scale = Mathf.Approximately(scale, 0f) ? 1f : scale;
                return speed <= 0f ? b * scale : AnimMath.Damp(a, b * scale, speed, dt);
            }
            smoothRot = new Vector3(Interp(smoothRot.x, rawRot.x, data.smoothRot.x, data.extraRot.x),
                                    Interp(smoothRot.y, rawRot.y, data.smoothRot.y, data.extraRot.y),
                                    Interp(smoothRot.z, rawRot.z, data.smoothRot.z, data.extraRot.z));
            smoothLoc = new Vector3(Interp(smoothLoc.x, rawLoc.x, data.smoothLoc.x, data.extraLoc.x),
                                    Interp(smoothLoc.y, rawLoc.y, data.smoothLoc.y, data.extraLoc.y),
                                    Interp(smoothLoc.z, rawLoc.z, data.smoothLoc.z, data.extraLoc.z));
        }

        void ApplyNoise(ref Vector3 v, float dt)
        {
            noiseTarget.x = AnimMath.Damp(noiseTarget.x, 0f, data.noiseDamp.x, dt);
            noiseTarget.y = AnimMath.Damp(noiseTarget.y, 0f, data.noiseDamp.y, dt);
            noiseOut.x = AnimMath.Damp(noiseOut.x, noiseTarget.x, data.noiseAccel.x, dt);
            noiseOut.y = AnimMath.Damp(noiseOut.y, noiseTarget.y, data.noiseAccel.y, dt);
            v += new Vector3(noiseOut.x, noiseOut.y, 0f);
        }

        void ApplyPushback(ref Vector3 v, float dt)
        {
            pushTarget = AnimMath.Damp(pushTarget, 0f, data.pushDamp, dt);
            pushOut = AnimMath.Damp(pushOut, pushTarget, data.pushAccel, dt);
            v += new Vector3(0f, 0f, pushOut);
        }

        void SetupTransition(Vector3 fromRot, Vector3 fromLoc, VectorCurve rot, VectorCurve loc)
        {
            if (!rot.IsValid || !loc.IsValid) return;
            startRot = fromRot;
            startLoc = fromLoc;
            restRot = restLoc = new bool3(true);
            curRot = rot;
            curLoc = loc;
            lastFrameTime = Mathf.Max(rot.LastTime, loc.LastTime);
            playBack = 0f;
            playing = true;
        }

        void CorrectAlpha(VectorCurve rot, VectorCurve loc, float t)
        {
            Vector3 a = rot.Evaluate(t);
            startRot = new Vector3(Mathf.LerpUnclamped(startRot.x, targetRot.x, a.x), Mathf.LerpUnclamped(startRot.y, targetRot.y, a.y), Mathf.LerpUnclamped(startRot.z, targetRot.z, a.z));
            a = loc.Evaluate(t);
            startLoc = new Vector3(Mathf.LerpUnclamped(startLoc.x, targetLoc.x, a.x), Mathf.LerpUnclamped(startLoc.y, targetLoc.y, a.y), Mathf.LerpUnclamped(startLoc.z, targetLoc.z, a.z));
        }
    }
}
