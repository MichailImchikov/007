using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Procedural full-body animation for a HumanoidRig. Drive it by setting the public
    /// parameters every frame (works the same for the local player, bots or — later — network proxies):
    /// locomotion (walk/run/strafe), crouch, prone, lean, aim pitch, rifle hold, knife/throw actions,
    /// hit flinch, death fall and the execution victim sequence.
    /// Bone convention: limbs point down (-Y). +X rotation swings a limb backwards / bends torso forward.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class CharacterAnimator : MonoBehaviour
    {
        public HumanoidRig rig;

        [Header("Parameters (set every frame)")]
        public Vector3 localVelocity;      // m/s in character space
        public bool grounded = true;
        public float crouch;               // 0..1
        public float prone;                // 0..1
        public float lean;                 // -1..1
        public float aimPitch;             // degrees, + = looking down
        public float aim;                  // 0..1 shoulder the weapon
        public bool holdingWeapon = true;
        public float pistolHold;           // 0 rifle, 1 pistol
        public float knife = -1f;          // 0..1 slash progress, <0 none
        public float throwT = -1f;         // 0..1 grenade throw, <0 none
        public float executeAttack = -1f;  // attacker side of execution
        public float executeVictim = -1f;  // victim side of execution
        public float dead;                 // 0..1 fall progress
        public float deadDir = -1f;        // -1 backwards, +1 forwards
        public float lookYaw;              // head look around (bots), degrees

        [Header("Ability poses")]
        public float bentOver;             // 0..1 bent over, butt to the enemy (rocket)
        public bool swordHold;             // parry sword in the right hand
        public float parry = -1f;          // 0..1 parry swing, <0 none
        public int parryVariant;           // which parry move
        public float slip;                 // 0..1 slipped and lying on the back
        public float blind;                // 0..1 hands over the eyes

        float phase, gait;
        readonly Spring3 flinch = new Spring3(180f, 12f);
        Vector3 hipsRest;

        void Awake()
        {
            if (!rig) rig = GetComponentInChildren<HumanoidRig>();
            if (rig) hipsRest = rig.hips.localPosition;
        }

        public void Flinch(Vector3 localDir, float strength)
        {
            flinch.Kick(new Vector3(localDir.z * 40f, 0f, -localDir.x * 40f) * strength);
        }

        static Quaternion E(float x, float y, float z) => Quaternion.Euler(x, y, z);

        void LateUpdate()
        {
            if (!rig) return;
            float dt = Time.deltaTime;
            Vector3 fl = flinch.Step(dt);

            Vector2 hv = new Vector2(localVelocity.x, localVelocity.z);
            float speed = hv.magnitude;
            float stride = prone > 0.5f ? 1.2f : Mathf.Lerp(1.6f, 1.15f, crouch) + Mathf.Clamp01((speed - 3f) / 4f) * 0.9f;
            phase += speed * dt / stride * Mathf.PI * 2f * (grounded ? 1f : 0.2f);
            gait = Anim.Damp(gait, grounded ? Mathf.Clamp01(speed / 4f) : 0.3f, 8f, dt);
            float run = Mathf.Clamp01((speed - 4.5f) / 3f);
            float fwd = speed > 0.05f ? hv.y / speed : 1f;
            float side = speed > 0.05f ? hv.x / speed : 0f;
            if (fwd < -0.1f) fwd = -Mathf.Abs(fwd);

            float stand = 1f - Mathf.Max(crouch, prone);
            float A = Mathf.Lerp(28f, 45f, run) * gait * (1f - prone);
            float s1 = Mathf.Sin(phase), s2 = Mathf.Sin(phase + Mathf.PI);
            float c1 = Mathf.Cos(phase), c2 = Mathf.Cos(phase + Mathf.PI);

            // ---------- legs
            float thighBase = -72f * crouch;
            float shinBase = 105f * crouch;
            float footBase = -30f * crouch;
            Quaternion lThigh = E(thighBase - s1 * A * fwd, 0f, s1 * A * side * 0.6f - 3f);
            Quaternion rThigh = E(thighBase - s2 * A * fwd, 0f, s2 * A * side * 0.6f + 3f);
            float k1 = Mathf.Max(0f, c1) * A * 1.4f + 4f * gait;
            float k2 = Mathf.Max(0f, c2) * A * 1.4f + 4f * gait;
            Quaternion lShin = E(shinBase + k1, 0f, 0f);
            Quaternion rShin = E(shinBase + k2, 0f, 0f);
            Quaternion lFoot = E(footBase - k1 * 0.3f, 0f, 0f);
            Quaternion rFoot = E(footBase - k2 * 0.3f, 0f, 0f);

            // airborne tuck
            if (!grounded && prone < 0.1f)
            {
                lThigh *= E(-25f, 0f, 0f); rThigh *= E(-10f, 0f, 0f);
                lShin *= E(40f, 0f, 0f); rShin *= E(20f, 0f, 0f);
            }

            // ---------- hips / torso
            Vector3 hipsPos = hipsRest;
            hipsPos.y -= 0.34f * crouch;
            hipsPos.y -= Mathf.Abs(Mathf.Cos(phase)) * 0.035f * gait * stand;
            hipsPos.y = Mathf.Lerp(hipsPos.y, 0.17f, prone);
            Quaternion hips = E(85f * prone, 0f, Mathf.Sin(phase) * 3f * gait * stand);
            float pitch = Mathf.Clamp(aimPitch, -70f, 70f) * (1f - prone * 0.6f);
            float runLean = 10f * run * fwd;
            Quaternion spine = E(10f * crouch + runLean * 0.5f + pitch * 0.25f + fl.x * 0.5f, -Mathf.Sin(phase) * 4f * gait * stand, -12f * lean + fl.z * 0.5f);
            Quaternion chest = E(4f * crouch + runLean * 0.5f + pitch * 0.35f + fl.x * 0.5f, -Mathf.Sin(phase) * 3f * gait * stand, -12f * lean + fl.z * 0.5f);
            Quaternion neck = E(pitch * 0.4f - 75f * prone, lookYaw * 0.5f, 0f);
            Quaternion head = E(-5f * prone, lookYaw * 0.5f, 0f);

            // prone crawl: legs splay and paddle, arms reach
            if (prone > 0.01f)
            {
                float crawl = Mathf.Clamp01(speed / 1.2f) * prone;
                lThigh = Quaternion.Slerp(lThigh, E(-5f + s1 * 18f * crawl, 0f, -12f), prone);
                rThigh = Quaternion.Slerp(rThigh, E(-5f + s2 * 18f * crawl, 0f, 12f), prone);
                lShin = Quaternion.Slerp(lShin, E(25f + Mathf.Max(0f, c1) * 40f * crawl, 0f, 0f), prone);
                rShin = Quaternion.Slerp(rShin, E(25f + Mathf.Max(0f, c2) * 40f * crawl, 0f, 0f), prone);
                lFoot = Quaternion.Slerp(lFoot, E(60f, 0f, 0f), prone);
                rFoot = Quaternion.Slerp(rFoot, E(60f, 0f, 0f), prone);
            }

            // ---------- arms (rifle / pistol hold, unarmed swing)
            Quaternion lUA, lFA, rUA, rFA, lH, rH;
            if (holdingWeapon)
            {
                float a = Mathf.Lerp(0.6f, 1f, aim);
                rUA = Quaternion.Slerp(E(-20f, 0f, -5f), E(-35f, 0f, -8f), a);
                rFA = Quaternion.Slerp(E(-70f, 0f, -30f), E(-80f, 0f, -30f), a);
                lUA = Quaternion.Slerp(E(-50f, 0f, 13f), E(-68f, 0f, 15f), a);
                lFA = Quaternion.Slerp(E(-30f, 0f, 25f), E(-22f, 0f, 25f), a);
                if (pistolHold > 0.01f)
                {
                    rUA = Quaternion.Slerp(rUA, E(-80f, 0f, -10f), pistolHold);
                    rFA = Quaternion.Slerp(rFA, E(-10f, 0f, -10f), pistolHold);
                    lUA = Quaternion.Slerp(lUA, E(-80f, 0f, 22f), pistolHold);
                    lFA = Quaternion.Slerp(lFA, E(-10f, 0f, 20f), pistolHold);
                }
                lH = E(0f, 0f, 0f); rH = E(-10f, 0f, 0f);
                // walking sway of the held weapon
                rUA *= E(Mathf.Sin(phase * 2f) * 3f * gait, 0f, 0f);
                lUA *= E(Mathf.Sin(phase * 2f) * 3f * gait, 0f, 0f);
            }
            else
            {
                rUA = E(s1 * 30f * gait, 0f, 6f); rFA = E(-20f - Mathf.Max(0f, s1) * 25f * gait, 0f, 0f);
                lUA = E(s2 * 30f * gait, 0f, -6f); lFA = E(-20f - Mathf.Max(0f, s2) * 25f * gait, 0f, 0f);
                lH = rH = Quaternion.identity;
            }
            if (prone > 0.01f)
            {
                rUA = Quaternion.Slerp(rUA, E(-155f, 0f, -12f), prone);
                lUA = Quaternion.Slerp(lUA, E(-160f, 0f, 15f), prone);
                rFA = Quaternion.Slerp(rFA, E(-30f, 0f, 0f), prone);
                lFA = Quaternion.Slerp(lFA, E(-10f, 0f, 0f), prone);
            }

            // ---------- actions
            if (knife >= 0f)
            {
                float w = Mathf.Sin(Mathf.Clamp01(knife) * Mathf.PI);
                Quaternion wind = E(-110f, 40f, 40f), slash = E(-80f, -50f, -10f);
                Quaternion target = knife < 0.35f ? Quaternion.Slerp(rUA, wind, Anim.Smooth(knife / 0.35f)) : Quaternion.Slerp(wind, slash, Anim.Smooth((knife - 0.35f) / 0.25f));
                rUA = Quaternion.Slerp(rUA, target, w);
                rFA = Quaternion.Slerp(rFA, E(-40f, 0f, 0f), w);
                chest *= E(0f, -20f * w, 0f);
            }
            if (throwT >= 0f)
            {
                float w = Mathf.Sin(Mathf.Clamp01(throwT) * Mathf.PI);
                Quaternion back = E(-150f, 20f, 40f), fwdT = E(-60f, -10f, 10f);
                Quaternion target = throwT < 0.4f ? back : Quaternion.Slerp(back, fwdT, Anim.Smooth((throwT - 0.4f) / 0.3f));
                rUA = Quaternion.Slerp(rUA, target, w);
                rFA = Quaternion.Slerp(rFA, E(-60f, 0f, 0f), w);
                chest *= E(0f, 25f * (throwT < 0.4f ? w : -w), 0f);
            }
            if (executeAttack >= 0f)
            {
                float t = executeAttack;
                float w = Anim.Smooth(Mathf.Clamp01(t / 0.15f)) * (1f - Anim.Smooth(Mathf.Clamp01((t - 0.85f) / 0.15f)));
                Quaternion stabUA = t < 0.38f ? E(-120f, 20f, 30f) : E(-75f, -10f, 15f);
                rUA = Quaternion.Slerp(rUA, stabUA, w);
                rFA = Quaternion.Slerp(rFA, t < 0.38f ? E(-90f, 0f, 0f) : E(-20f, 0f, 0f), w);
                lUA = Quaternion.Slerp(lUA, E(-85f, -30f, -10f), w);
                lFA = Quaternion.Slerp(lFA, E(-60f, 0f, 0f), w);
                spine *= E(12f * w, 0f, 0f);
            }

            // ---------- ability poses
            if (swordHold)
            {
                rUA = E(-55f, 10f, -15f); rFA = E(-55f, 0f, 0f); rH = E(-20f, 0f, 0f);
                lUA = Quaternion.Slerp(lUA, E(-20f, 0f, 10f), 0.7f); lFA = E(-35f, 0f, 0f);
                if (parry >= 0f)
                {
                    float w = Mathf.Sin(Mathf.Clamp01(parry) * Mathf.PI);
                    Quaternion pose;
                    switch (parryVariant % 6)
                    {
                        case 0: pose = E(-160f, 20f, -30f); break;  // high block
                        case 1: pose = E(-95f, -60f, -10f); break;  // sweep left
                        case 2: pose = E(-85f, 65f, -45f); break;   // sweep right
                        case 3: pose = E(-25f, 0f, -20f); break;    // low guard
                        case 4: pose = E(-130f, -30f, 40f); break;  // backhand
                        default: pose = E(-110f, 90f, -60f); break; // spin cut
                    }
                    rUA = Quaternion.Slerp(rUA, pose, w);
                    rFA = Quaternion.Slerp(rFA, E(-15f, 0f, 0f), w);
                    float twist = (parryVariant % 2 == 0 ? 1f : -1f) * 25f * w;
                    if (parryVariant % 6 == 5) twist = Mathf.Sin(Mathf.Clamp01(parry) * Mathf.PI * 2f) * 60f;
                    chest *= E(0f, twist, 0f);
                }
            }
            if (bentOver > 0.001f)
            {
                float b = Anim.Smooth(Mathf.Clamp01(bentOver));
                hips = Quaternion.Slerp(hips, E(82f, 0f, 0f), b);
                lThigh = Quaternion.Slerp(lThigh, E(-86f, 0f, -7f), b);
                rThigh = Quaternion.Slerp(rThigh, E(-86f, 0f, 7f), b);
                lShin = Quaternion.Slerp(lShin, E(12f, 0f, 0f), b);
                rShin = Quaternion.Slerp(rShin, E(12f, 0f, 0f), b);
                lFoot = Quaternion.Slerp(lFoot, E(-6f, 0f, 0f), b);
                rFoot = Quaternion.Slerp(rFoot, E(-6f, 0f, 0f), b);
                spine = Quaternion.Slerp(spine, E(4f, 0f, 0f), b);
                chest = Quaternion.Slerp(chest, E(4f, 0f, 0f), b);
                neck = Quaternion.Slerp(neck, E(50f, 0f, 0f), b);   // peeking back between the legs
                head = Quaternion.Slerp(head, E(45f, 0f, 0f), b);
                rUA = Quaternion.Slerp(rUA, E(-55f, 0f, -12f), b);
                lUA = Quaternion.Slerp(lUA, E(-55f, 0f, 12f), b);
                rFA = Quaternion.Slerp(rFA, E(-8f, 0f, 0f), b);
                lFA = Quaternion.Slerp(lFA, E(-8f, 0f, 0f), b);
                hipsPos.y = Mathf.Lerp(hipsPos.y, hipsRest.y - 0.04f, b);
            }
            if (blind > 0.001f)
            {
                float b = Anim.Smooth(Mathf.Clamp01(blind));
                float shake = Mathf.Sin(Time.time * 18f) * 8f;
                rUA = Quaternion.Slerp(rUA, E(-150f, 0f, -28f), b);
                lUA = Quaternion.Slerp(lUA, E(-150f, 0f, 28f), b);
                rFA = Quaternion.Slerp(rFA, E(-115f, 0f, 0f), b);
                lFA = Quaternion.Slerp(lFA, E(-115f, 0f, 0f), b);
                neck = Quaternion.Slerp(neck, E(15f, shake, 0f), b);
                spine = Quaternion.Slerp(spine, E(15f, 0f, shake * 0.5f), b);
            }

            Vector3 rigPos = Vector3.zero;
            Quaternion rigRot = Quaternion.identity;
            if (slip > 0.001f)
            {
                float sl = Anim.Smooth(Mathf.Clamp01(slip));
                rigRot = E(-84f * sl, 0f, 0f);
                rigPos = new Vector3(0f, 0.12f * sl, 0f);
                lThigh = Quaternion.Slerp(lThigh, E(-75f + Mathf.Sin(Time.time * 14f) * 15f, 0f, -10f), sl);   // legs flailing in the air
                rThigh = Quaternion.Slerp(rThigh, E(-60f - Mathf.Sin(Time.time * 14f) * 15f, 0f, 10f), sl);
                lShin = Quaternion.Slerp(lShin, E(30f, 0f, 0f), sl);
                rShin = Quaternion.Slerp(rShin, E(40f, 0f, 0f), sl);
                rUA = Quaternion.Slerp(rUA, E(-20f, 0f, 75f), sl);
                lUA = Quaternion.Slerp(lUA, E(-20f, 0f, -75f), sl);
                neck = Quaternion.Slerp(neck, E(-20f, 0f, 0f), sl);
            }
            if (executeVictim >= 0f)
            {
                float t = executeVictim;
                float grab = Anim.Smooth(Mathf.Clamp01((t - 0.08f) / 0.15f));
                float stab = t >= 0.4f ? Mathf.Exp(-(t - 0.4f) * 25f) : 0f;
                float sag = Anim.Smooth(Mathf.Clamp01((t - 0.45f) / 0.35f));
                neck = Quaternion.Slerp(neck, E(-35f + 20f * sag, 0f, 8f * sag), grab);
                head = Quaternion.Slerp(head, E(-15f + 35f * sag, 0f, 10f * sag), grab);
                spine = Quaternion.Slerp(spine, E(-12f - 15f * stab + 10f * sag, 0f, 0f), grab);
                chest = Quaternion.Slerp(chest, E(-10f - 18f * stab + 10f * sag, 0f, 0f), grab);
                Quaternion struggleR = E(-130f, 0f, -25f), struggleL = E(-130f, 0f, 25f);
                rUA = Quaternion.Slerp(rUA, Quaternion.Slerp(struggleR, E(-5f, 0f, 10f), sag), grab);
                lUA = Quaternion.Slerp(lUA, Quaternion.Slerp(struggleL, E(-5f, 0f, -10f), sag), grab);
                rFA = Quaternion.Slerp(rFA, Quaternion.Slerp(E(-100f, 0f, 0f), E(-10f, 0f, 0f), sag), grab);
                lFA = Quaternion.Slerp(lFA, Quaternion.Slerp(E(-100f, 0f, 0f), E(-10f, 0f, 0f), sag), grab);
                lThigh = Quaternion.Slerp(lThigh, E(-30f, 0f, -4f), sag);
                rThigh = Quaternion.Slerp(rThigh, E(-20f, 0f, 4f), sag);
                lShin = Quaternion.Slerp(lShin, E(60f, 0f, 0f), sag);
                rShin = Quaternion.Slerp(rShin, E(45f, 0f, 0f), sag);
                hipsPos.y -= 0.18f * sag;
            }

            if (dead > 0f)
            {
                float d = Mathf.Clamp01(dead);
                float fall = d * d;
                rigRot = E(deadDir * 86f * fall, 0f, 0f);
                rigPos = new Vector3(0f, 0.11f * fall, 0f);
                hipsPos = Vector3.Lerp(hipsPos, hipsRest, d * 0.7f);
                Quaternion limp = E(deadDir * -15f, 0f, 0f);
                spine = Quaternion.Slerp(spine, E(deadDir * 10f, 0f, 5f), d);
                chest = Quaternion.Slerp(chest, E(deadDir * 5f, 0f, -5f), d);
                neck = Quaternion.Slerp(neck, E(deadDir * 25f, 20f, 0f), d);
                rUA = Quaternion.Slerp(rUA, E(deadDir * -30f, 0f, 60f), d);
                lUA = Quaternion.Slerp(lUA, E(deadDir * -30f, 0f, -70f), d);
                rFA = Quaternion.Slerp(rFA, E(-20f, 0f, 0f), d);
                lFA = Quaternion.Slerp(lFA, E(-35f, 0f, 0f), d);
                lThigh = Quaternion.Slerp(lThigh, E(-15f, 0f, -10f) * limp, d);
                rThigh = Quaternion.Slerp(rThigh, E(-35f, 0f, 8f) * limp, d);
                lShin = Quaternion.Slerp(lShin, E(20f, 0f, 0f), d);
                rShin = Quaternion.Slerp(rShin, E(55f, 0f, 0f), d);
                hips = Quaternion.Slerp(hips, Quaternion.identity, d);
            }

            rig.rig.localPosition = rigPos;
            rig.rig.localRotation = rigRot;
            rig.hips.localPosition = hipsPos;
            rig.hips.localRotation = hips;
            rig.spine.localRotation = spine;
            rig.chest.localRotation = chest;
            rig.neck.localRotation = neck;
            rig.head.localRotation = head;
            rig.lThigh.localRotation = lThigh; rig.rThigh.localRotation = rThigh;
            rig.lShin.localRotation = lShin; rig.rShin.localRotation = rShin;
            rig.lFoot.localRotation = lFoot; rig.rFoot.localRotation = rFoot;
            rig.lUpperArm.localRotation = lUA; rig.rUpperArm.localRotation = rUA;
            rig.lForearm.localRotation = lFA; rig.rForearm.localRotation = rFA;
            rig.lHand.localRotation = lH; rig.rHand.localRotation = rH;
            if (rig.weaponSocket)
            {
                float hide = Mathf.Max(knife >= 0f ? 1f : 0f, throwT >= 0f ? 1f : 0f, executeAttack >= 0f ? 1f : 0f);
                rig.weaponSocket.localRotation = E(pitch * 0.4f - 85f * prone + 50f * hide, 30f * hide, 0f);
            }
        }
    }
}
