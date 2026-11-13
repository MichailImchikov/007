using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FPS
{
    /// <summary>
    /// A mannequin in the disco crowd: one of the real character models (the same ones players and bots wear),
    /// playing an idle clip with a procedural dance layered on top in LateUpdate — bounce, fist pumps, hands up,
    /// shuffles, head banging, jumps — locked to the beat of its room. Bones are posed by direction in body space,
    /// so it works on any humanoid rig. Shots and blasts make it stagger or fall over (it is plastic: no blood,
    /// no hitmarker, it gets back up). Blocks movement and sight; bots path around it (NavMeshObstacle).
    /// </summary>
    [DefaultExecutionOrder(300)]
    public class DiscoDancer : MonoBehaviour, IDamageable
    {
        enum Style { Pump, HandsUp, Shuffle, Headbang, Jump, Sway, Count }

        public DiscoRoom room;

        Animator an;
        Transform hips, spine, chest, neck, head, lUp, lLo, lHand, rUp, rLo, rHand;
        Renderer[] rends;
        Style style;
        float amp, phaseOff, sideBias, styleUntil;
        int lastBeat;
        float beatT; // 0..1 inside the current beat, from the room
        float flinch, fallen, fallW, getUpAt;
        Vector3 basePos, flinchDir;
        Quaternion standRot, fallRot;
        bool visible = true;
        Transform lFoot, rFoot;
        PlayableGraph graph;
        AnimationClipPlayable clipPlayable;
        double clipLength;
        /// <summary>Ankle height above the floor in a normal stance (feet never sink below it).</summary>
        const float AnkleHeight = 0.085f;

        public static DiscoDancer Spawn(Vector3 pos, float yaw, DiscoRoom room, int model, AnimationClip[] idles, System.Random rnd)
        {
            var e = CharacterModelDB.Get(model);
            if (e == null) return null;
            var go = new GameObject("Dancer");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var inst = Instantiate(e.prefab, go.transform, false);
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var mb in inst.GetComponentsInChildren<MonoBehaviour>(true)) if (!(mb is DiscoDancer)) Destroy(mb);
            var an = inst.GetComponentInChildren<Animator>();
            if (!an || !an.avatar || !an.avatar.isHuman) { Destroy(go); return null; }
            var rs = inst.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rs)
            {
                if (e.skin) { var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = e.skin; r.sharedMaterials = mats; }
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }

            var cap = go.AddComponent<CapsuleCollider>();
            cap.center = new Vector3(0f, 0.9f, 0f); cap.height = 1.8f; cap.radius = 0.27f;
            var obs = go.AddComponent<NavMeshObstacle>();
            obs.shape = NavMeshObstacleShape.Capsule;
            obs.center = cap.center; obs.height = cap.height; obs.radius = 0.24f;
            obs.carving = true; obs.carveOnlyStationary = true;

            var d = go.AddComponent<DiscoDancer>();
            d.room = room;
            d.an = an;
            d.rends = rs;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            an.runtimeAnimatorController = null;
            // every dancer plays one of the idle clips directly (no controller asset), from a random point and at
            // a slightly different speed
            var clip = idles != null && idles.Length > 0 ? idles[rnd.Next(0, idles.Length)] : null;
            if (clip)
            {
                d.graph = PlayableGraph.Create("Dancer");
                d.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                d.clipPlayable = AnimationClipPlayable.Create(d.graph, clip);
                d.clipPlayable.SetApplyFootIK(true);
                d.clipLength = Mathf.Max(0.1f, clip.length);
                d.clipPlayable.SetTime(rnd.NextDouble() * d.clipLength);
                d.clipPlayable.SetSpeed(0.85 + rnd.NextDouble() * 0.3);
                var output = AnimationPlayableOutput.Create(d.graph, "Dance", an);
                output.SetSourcePlayable(d.clipPlayable);
                d.graph.Play();
            }
            d.Init(rnd);
            return d;
        }

        void Init(System.Random rnd)
        {
            hips = an.GetBoneTransform(HumanBodyBones.Hips);
            lFoot = an.GetBoneTransform(HumanBodyBones.LeftFoot);
            rFoot = an.GetBoneTransform(HumanBodyBones.RightFoot);
            spine = an.GetBoneTransform(HumanBodyBones.Spine);
            chest = an.GetBoneTransform(HumanBodyBones.UpperChest) ?? an.GetBoneTransform(HumanBodyBones.Chest);
            neck = an.GetBoneTransform(HumanBodyBones.Neck);
            head = an.GetBoneTransform(HumanBodyBones.Head);
            lUp = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); lLo = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); lHand = an.GetBoneTransform(HumanBodyBones.LeftHand);
            rUp = an.GetBoneTransform(HumanBodyBones.RightUpperArm); rLo = an.GetBoneTransform(HumanBodyBones.RightLowerArm); rHand = an.GetBoneTransform(HumanBodyBones.RightHand);
            amp = 0.75f + (float)rnd.NextDouble() * 0.45f;
            phaseOff = ((float)rnd.NextDouble() - 0.5f) * 0.16f;
            sideBias = rnd.NextDouble() < 0.5 ? -1f : 1f;
            style = (Style)rnd.Next(0, (int)Style.Count);
            styleUntil = Time.time + 12f + (float)rnd.NextDouble() * 20f;
            basePos = transform.position;
            standRot = fallRot = transform.rotation;
        }

        // ------------------------------------------------------------------ damage

        public DamageResult TakeDamage(DamageInfo info)
        {
            Vector3 dir = info.direction; dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = -transform.forward;
            flinchDir = dir.normalized;
            flinch = 1f;
            if (info.amount >= 55f && fallen <= 0f)
            {
                // knocked over by a blast or a heavy hit, gets back up a few seconds later
                fallen = 1f;
                getUpAt = Time.time + Random.Range(4f, 7f);
                fallRot = Quaternion.AngleAxis(82f, Vector3.Cross(Vector3.up, flinchDir)) * standRot;
            }
            FX.Ensure().SparkBurst(info.point, info.normal, 4, 4f);
            return new DamageResult { applied = false };
        }

        // ------------------------------------------------------------------ dance

        static Vector3 Dir(Transform root, float x, float y, float z) => (root.right * x + root.up * y + root.forward * z).normalized;

        /// <summary>Rotates <paramref name="bone"/> so that it points from itself to <paramref name="child"/> along <paramref name="dir"/> (blend w).</summary>
        static void Aim(Transform bone, Transform child, Vector3 dir, float w)
        {
            if (!bone || !child || w <= 0f) return;
            Vector3 cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-6f) return;
            var q = Quaternion.FromToRotation(cur.normalized, dir);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, q, w) * bone.rotation;
        }

        static void Turn(Transform bone, Vector3 axis, float deg)
        {
            if (bone && Mathf.Abs(deg) > 0.01f) bone.rotation = Quaternion.AngleAxis(deg, axis) * bone.rotation;
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (clipPlayable.IsValid())
            {
                double tm = clipPlayable.GetTime();
                if (tm >= clipLength) clipPlayable.SetTime(tm % clipLength);
            }
            visible = false;
            foreach (var r in rends) if (r && r.isVisible) { visible = true; break; }

            // beat clock from the room (falls back to 120 BPM)
            if (room)
            {
                if (room.BeatCount != lastBeat) { lastBeat = room.BeatCount; }
                beatT = Mathf.Repeat(room.BeatPhase + phaseOff, 1f);
            }
            else beatT = Mathf.Repeat(Time.time * 2f + phaseOff, 1f);

            if (Time.time > styleUntil)
            {
                style = (Style)Random.Range(0, (int)Style.Count);
                styleUntil = Time.time + Random.Range(12f, 30f);
            }

            flinch = Mathf.MoveTowards(flinch, 0f, dt * 2.2f);
            if (fallen > 0f && Time.time > getUpAt) fallen = 0f;
            fallW = Mathf.MoveTowards(fallW, fallen > 0f ? 1f : 0f, dt * (fallen > 0f ? 3.2f : 1.1f));

            // falling over: pivot on the feet, away from the hit
            float jump = style == Style.Jump && fallW <= 0f ? JumpHeight() : 0f;
            transform.SetPositionAndRotation(basePos + Vector3.up * jump, Quaternion.Slerp(standRot, fallRot, Ease(fallW)));
        }

        static float Ease(float x) => x * x * (3f - 2f * x);

        float JumpHeight()
        {
            // a hop on every other beat
            float t = room ? Mathf.Repeat((room.BeatCount % 2) + beatT, 2f) * 0.5f : Mathf.Repeat(Time.time, 1f);
            float h = t < 0.45f ? Mathf.Sin(t / 0.45f * Mathf.PI) : 0f;
            return h * 0.28f * amp;
        }

        void LateUpdate()
        {
            if (!visible || !an) return;
            var root = transform;
            float kick = room ? room.Kick : 0.5f;
            float e = amp * (0.7f + 0.5f * (room ? room.Level : 0.5f)) * (1f - fallW);
            float b = beatT;
            float bounce = Mathf.Pow(1f - b, 2f);                 // sharp on the beat
            float sw = Mathf.Sin((lastBeat + b) * Mathf.PI);      // side to side, one swing per beat
            float half = Mathf.Sin((lastBeat + b) * Mathf.PI * 0.5f);
            Vector3 up = root.up, right = root.right, fwd = root.forward;

            if (hips && style != Style.Jump) hips.position += up * (bounce * 0.05f * e);

            switch (style)
            {
                case Style.Pump:
                {
                    Turn(spine, right, bounce * 6f * e);
                    Turn(head, right, bounce * 14f * e);
                    float pump = Mathf.Lerp(0.15f, 1f, 1f - bounce);
                    Transform up1 = sideBias > 0 ? rUp : lUp, lo1 = sideBias > 0 ? rLo : lLo, h1 = sideBias > 0 ? rHand : lHand;
                    Aim(up1, lo1, Dir(root, 0.35f * sideBias, Mathf.Lerp(0.1f, 0.95f, pump), 0.4f), e);
                    Aim(lo1, h1, Dir(root, 0.15f * sideBias, 1f, 0.15f), e);
                    Transform up2 = sideBias > 0 ? lUp : rUp, lo2 = sideBias > 0 ? lLo : rLo, h2 = sideBias > 0 ? lHand : rHand;
                    Aim(up2, lo2, Dir(root, -0.25f * sideBias, -0.8f, 0.35f), e * 0.7f);
                    Aim(lo2, h2, Dir(root, -0.1f * sideBias, 0.1f, 1f), e * 0.8f);
                    break;
                }
                case Style.HandsUp:
                {
                    Turn(hips, fwd, sw * 7f * e);
                    Turn(chest, fwd, -sw * 9f * e);
                    Turn(head, right, -10f * e);
                    float wave = sw * 0.45f;
                    Aim(lUp, lLo, Dir(root, -0.35f + wave, 1f, 0.1f), e);
                    Aim(lLo, lHand, Dir(root, -0.15f + wave * 1.4f, 1f, 0.05f), e);
                    Aim(rUp, rLo, Dir(root, 0.35f + wave, 1f, 0.1f), e);
                    Aim(rLo, rHand, Dir(root, 0.15f + wave * 1.4f, 1f, 0.05f), e);
                    break;
                }
                case Style.Shuffle:
                {
                    Turn(spine, up, sw * 18f * e);
                    Turn(chest, right, 8f * e);
                    Turn(head, up, -sw * 12f * e);
                    if (hips) hips.position += right * (sw * 0.07f * e);
                    float pumpL = Mathf.Max(0f, sw), pumpR = Mathf.Max(0f, -sw);
                    Aim(lUp, lLo, Dir(root, -0.4f, -0.6f + pumpL * 0.5f, 0.45f), e);
                    Aim(lLo, lHand, Dir(root, 0.15f, 0.2f + pumpL * 0.6f, 1f), e);
                    Aim(rUp, rLo, Dir(root, 0.4f, -0.6f + pumpR * 0.5f, 0.45f), e);
                    Aim(rLo, rHand, Dir(root, -0.15f, 0.2f + pumpR * 0.6f, 1f), e);
                    break;
                }
                case Style.Headbang:
                {
                    float bang = bounce;
                    Turn(spine, right, (8f + bang * 16f) * e);
                    Turn(chest, right, bang * 12f * e);
                    Turn(neck, right, bang * 22f * e);
                    Turn(head, right, bang * 18f * e);
                    Aim(lUp, lLo, Dir(root, -0.5f, -0.75f, 0.35f), e * 0.6f);
                    Aim(rUp, rLo, Dir(root, 0.35f, 0.2f + kick * 0.5f, 0.6f), e);
                    Aim(rLo, rHand, Dir(root, 0.1f, 1f, 0.4f), e);
                    break;
                }
                case Style.Jump:
                {
                    float air = JumpHeight() / Mathf.Max(0.01f, 0.28f * amp);
                    Turn(head, right, -8f * air * e);
                    Aim(lUp, lLo, Dir(root, -0.4f, Mathf.Lerp(-0.4f, 1f, air), 0.2f), e);
                    Aim(lLo, lHand, Dir(root, -0.2f, 1f, 0.1f), e * air);
                    Aim(rUp, rLo, Dir(root, 0.4f, Mathf.Lerp(-0.4f, 1f, air), 0.2f), e);
                    Aim(rLo, rHand, Dir(root, 0.2f, 1f, 0.1f), e * air);
                    break;
                }
                default: // Sway
                {
                    Turn(hips, fwd, half * 9f * e);
                    Turn(spine, fwd, -half * 6f * e);
                    Turn(spine, up, half * 10f * e);
                    Turn(head, fwd, -half * 8f * e);
                    Aim(lUp, lLo, Dir(root, -0.45f, -0.7f, 0.25f + half * 0.2f), e * 0.8f);
                    Aim(lLo, lHand, Dir(root, 0.1f + half * 0.3f, 0f, 1f), e * 0.8f);
                    Aim(rUp, rLo, Dir(root, 0.45f, -0.7f, 0.25f - half * 0.2f), e * 0.8f);
                    Aim(rLo, rHand, Dir(root, -0.1f + half * 0.3f, 0f, 1f), e * 0.8f);
                    break;
                }
            }

            // stagger from a hit: lean away from the shot
            if (flinch > 0f && spine)
            {
                Vector3 axis = Vector3.Cross(Vector3.up, flinchDir);
                Turn(spine, axis, flinch * flinch * 28f);
                Turn(head, axis, flinch * 15f);
            }

            // feet never go through the floor, whatever the clip / pose did
            if (hips && lFoot && rFoot && fallW <= 0f)
            {
                float floor = basePos.y + AnkleHeight;
                float low = Mathf.Min(lFoot.position.y, rFoot.position.y);
                if (low < floor) hips.position += Vector3.up * (floor - low);
            }
        }
    }
}
