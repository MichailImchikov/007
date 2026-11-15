using System;
using System.Collections.Generic;
using UnityEngine;

namespace FPS.EditorTools.Kimodo
{
    /// <summary>How a generated motion is turned into a game clip.</summary>
    public enum KimodoProcess
    {
        /// <summary>Locomotion cycle: in place, best loop, left foot lands at phase 0.</summary>
        LoopMove,
        /// <summary>Standing / crouching / lying idle: in place, best loop.</summary>
        LoopIdle,
        /// <summary>One-shot, horizontal travel removed (the motor moves the character).</summary>
        OneShot,
        /// <summary>One-shot, horizontal travel and the vertical root motion removed (jumps / vaults: the motor lifts).</summary>
        OneShotNoLift,
        /// <summary>Jump: split into Jump_Start / Fall / Land at the takeoff and landing frames.</summary>
        JumpSplit,
    }

    /// <summary>One clip of the soldier animation set generated with Kimodo.</summary>
    [Serializable]
    public class KimodoPreset
    {
        public bool enabled = true;
        [Tooltip("Clip / controller slot name (Jog_F, Crouch_L, Slide...)")] public string slot = "Clip";
        [TextArea(2, 4)] public string prompt = "A person walks forward.";
        [Tooltip("Seconds generated (max 10)")] public float duration = 4f;
        [Tooltip("Root travel in m/s, Unity character space (x right, z forward): constrains the path so the gait matches the game speed. Zero + constrain = stays in place")]
        public Vector2 velocity;
        [Tooltip("Constrain the root path / heading (keeps facing forward while strafing / backpedalling)")] public bool constrainPath = true;
        public KimodoProcess process = KimodoProcess.LoopMove;
        [Tooltip("Target loop length window (s) for loops")] public Vector2 loopLength = new Vector2(0.55f, 1.6f);
        public int seed = 7;
        [Tooltip("Upper body slot clip (played masked over the locomotion)")] public bool upperBody;
        [Tooltip("Retime after generation (1 = as generated); e.g. a slow crawl sped up to the game's prone speed")] public float playbackSpeed = 1f;
        [Tooltip("Loops: raise / lower the neck + head so the face looks level on average (Kimodo locomotion tends to look at the ground); the head bob stays")]
        public bool levelHead = true;
        [Tooltip("Not generated: this loop is another slot's clip played backward (e.g. Prone_B = Prone_F reversed). That slot must come earlier in the list")]
        public string reverseOf = "";
        [Tooltip("Loops: straighten / bend the spine so the chest leans forward by this much on average (degrees); " +
                 "< 0 = as generated. Kimodo's crouch is a hunch that puts the eyes far ahead of the hips")]
        public float torsoPitch = -1f;
        [Tooltip("Lying idle: the legs straight back along the ground, a little apart, toes pointed (Kimodo lifts the " +
                 "lower legs and keeps the feet in the air)")]
        public bool straightLegs;
        [Tooltip("Crouches: lower the hips to this height (m at model scale; ≤ 0 = as generated) and re-solve the legs " +
                 "(knees forward, feet where they were). Kimodo's crouch keeps the eyes ~1.45 m up while the game's crouch " +
                 "camera is at 1 m: without this the runtime body alignment pulled the body down ~0.45 m and the leg IK " +
                 "folded the legs (the trailing heel kicked up behind into the seat)")]
        public float crouchHips = 0f;
        [Tooltip("Deep crouch: the feet's travel ahead / behind the hips is scaled by this (shorter steps under the low " +
                 "hips; the cadence is re-tuned to the speed)")]
        public float crouchStride = 1f;
        [Tooltip("Deep crouch: the swing foot rises at most about this high (m)")]
        public float crouchLift = 0.1f;
    }

    /// <summary>Editable list of the clips the soldier needs (Tools > FPS > Kimodo).</summary>
    [CreateAssetMenu(fileName = "KimodoLibrary", menuName = "FPS/Animation/Kimodo Library")]
    public class KimodoLibrary : ScriptableObject
    {
        public string serverUrl = "http://127.0.0.1:7861";
        public string kimodoRoot = "E:/Kimodo";
        [Tooltip("Diffusion steps (quality vs speed)")] public int diffusionSteps = 100;
        [Tooltip("Classifier-free guidance [text, constraint]")] public Vector2 cfg = new Vector2(2f, 2f);
        public List<KimodoPreset> presets = new List<KimodoPreset>();

        // game speeds (PlayerMotor): walk 4.6, sprint 7.4, crouch 2.5, prone 1.15; slow walk ring for bots 1.45
        public void ResetToDefaults()
        {
            presets.Clear();
            KimodoPreset P(string slot, string prompt, float dur, Vector2 v, KimodoProcess proc, bool path = true, float speed = 1f, bool upper = false, Vector2? loop = null)
            {
                var k = new KimodoPreset { slot = slot, prompt = prompt, duration = dur, velocity = v, process = proc, constrainPath = path, playbackSpeed = speed, upperBody = upper, loopLength = loop ?? new Vector2(0.55f, 1.6f) };
                presets.Add(k);
                return k;
            }
            const float W = 1.45f, J = 4.6f, C = 2.5f;

            // neutral, physical wording ("A person ..."), as recommended for Kimodo; the root path / heading constraint
            // holds the speed and keeps the character facing forward while strafing / backpedalling
            P("Idle", "A person stands still in a relaxed ready stance with feet shoulder width apart.", 6f, Vector2.zero, KimodoProcess.LoopIdle, loop: new Vector2(2f, 4.5f));
            // the generated strafes / runs lean far forward (sidestep jogs ~50°): a rifleman strafes nearly upright,
            // and a lean puts the eyes ahead of the feet, so the body would slide back under the first-person camera
            P("Walk_F", "A person walks forward at a steady pace, looking straight ahead.", 5f, new Vector2(0f, W), KimodoProcess.LoopMove);
            P("Walk_B", "A person walks backward at a steady pace, looking straight ahead.", 5f, new Vector2(0f, -W), KimodoProcess.LoopMove).torsoPitch = 8f;
            P("Walk_L", "A person sidesteps to the left, looking straight ahead.", 5f, new Vector2(-W, 0f), KimodoProcess.LoopMove).torsoPitch = 8f;
            P("Walk_R", "A person sidesteps to the right, looking straight ahead.", 5f, new Vector2(W, 0f), KimodoProcess.LoopMove).torsoPitch = 8f;
            P("Jog_F", "A person jogs forward, looking straight ahead.", 5f, new Vector2(0f, J), KimodoProcess.LoopMove).torsoPitch = 12f;
            P("Jog_B", "A person jogs backward, looking straight ahead.", 5f, new Vector2(0f, -J), KimodoProcess.LoopMove).torsoPitch = 10f;
            P("Jog_L", "A person jogs sideways to the left, looking straight ahead.", 5f, new Vector2(-J, 0f), KimodoProcess.LoopMove).torsoPitch = 10f;
            P("Jog_R", "A person jogs sideways to the right, looking straight ahead.", 5f, new Vector2(J, 0f), KimodoProcess.LoopMove).torsoPitch = 10f;
            // 7.4 m/s is past what the model holds (the body takes off mid-clip): generated at 6.2, cadence tuned to 7.4
            var sprint = P("Sprint_F", "A person sprints forward, looking straight ahead.", 4f, new Vector2(0f, 6.2f), KimodoProcess.LoopMove, speed: 7.4f / 6.2f);
            sprint.seed = 11;
            sprint.torsoPitch = 22f;

            // crouches: Kimodo's crouch is shallow (hips ~0.83 m, eyes ~1.45 m) for the game's 1 m crouch camera, so the
            // clips are deepened (hips ~0.55 m, chest ~38° forward, shorter steps, feet kept low) instead of letting the
            // runtime body alignment pull the body down and the leg IK fold the legs
            const float CrouchTorso = 38f;
            KimodoPreset Deep(KimodoPreset k) { k.torsoPitch = CrouchTorso; k.crouchHips = 0.55f; k.crouchStride = 0.8f; k.crouchLift = 0.1f; return k; }
            Deep(P("Crouch_Idle", "A person crouches low and stays still.", 6f, Vector2.zero, KimodoProcess.LoopIdle, loop: new Vector2(2f, 4.5f)));
            Deep(P("Crouch_F", "A person walks forward while crouching low, looking straight ahead.", 5f, new Vector2(0f, C), KimodoProcess.LoopMove));
            // a backward crouch at 2.5 m/s makes the model skate (the feet move ~1/3 slower than the body, even when
            // generated at 1.7 m/s): the forward crouch run played backward has the right foot speed and cadence
            P("Crouch_B", "(Crouch_F played backward)", 0f, new Vector2(0f, -C), KimodoProcess.LoopMove).reverseOf = "Crouch_F";
            Deep(P("Crouch_L", "A person sidesteps to the left while crouching low, looking straight ahead.", 5f, new Vector2(-C, 0f), KimodoProcess.LoopMove));
            Deep(P("Crouch_R", "A person sidesteps to the right while crouching low, looking straight ahead.", 5f, new Vector2(C, 0f), KimodoProcess.LoopMove));

            P("Prone_Idle", "A person lies on their stomach on the ground, propped on their elbows.", 6f, Vector2.zero, KimodoProcess.LoopIdle, loop: new Vector2(2f, 4.5f)).straightLegs = true;
            // with a forward path constraint the model switches to a hands-and-knees crawl (hips ~45 cm); free, it does a real
            // belly crawl (hips ~10-20 cm) but slowly: generated free and sped up. Backward = the same crawl reversed
            var proneF = P("Prone_F", "A person lying on their stomach crawls forward.", 8f, new Vector2(0f, 0.3f), KimodoProcess.LoopMove, path: false, speed: 2f, loop: new Vector2(1f, 3f));
            proneF.seed = 3;
            P("Prone_B", "(Prone_F played backward)", 0f, new Vector2(0f, -0.3f), KimodoProcess.LoopMove).reverseOf = "Prone_F";
            P("Prone_L", "A person lying on their stomach crawls to the left.", 6f, new Vector2(-0.5f, 0f), KimodoProcess.LoopMove, speed: 2.3f, loop: new Vector2(0.9f, 2.6f));
            P("Prone_R", "A person lying on their stomach crawls to the right.", 6f, new Vector2(0.5f, 0f), KimodoProcess.LoopMove, speed: 2.3f, loop: new Vector2(0.9f, 2.6f));

            P("Jump", "A person jumps up in place.", 2.5f, Vector2.zero, KimodoProcess.JumpSplit, path: false);
            // the model does not know a feet-first slide (it runs on, or dives head first): off, the procedural slide pose is used
            P("Slide", "A person runs forward and slides feet first on the ground.", 2.5f, Vector2.zero, KimodoProcess.OneShot, path: false).enabled = false;
            P("Vault", "A person vaults over a low wall using one hand.", 2.2f, Vector2.zero, KimodoProcess.OneShotNoLift, path: false);
            P("Mantle", "A person climbs up onto a high ledge.", 2.4f, Vector2.zero, KimodoProcess.OneShotNoLift, path: false);
            P("Death_B", "A person gets shot and falls backward onto the ground.", 3f, Vector2.zero, KimodoProcess.OneShot, path: false);
            P("Death_F", "A person gets shot and collapses forward onto the ground.", 3f, Vector2.zero, KimodoProcess.OneShot, path: false);
            P("Throw", "A person throws overhand with the right arm.", 1.6f, Vector2.zero, KimodoProcess.OneShot, path: false, upper: true);
            P("Knife", "A person slashes forward with the right hand.", 1.2f, Vector2.zero, KimodoProcess.OneShot, path: false, upper: true);
        }
    }
}
