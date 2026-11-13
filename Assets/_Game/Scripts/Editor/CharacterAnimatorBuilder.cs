using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FPS.EditorTools
{
    /// <summary>
    /// Builds the character Animator Controllers from the JU TPS 3 humanoid clips
    /// (Assets/_Game/Generated/Anim) and stores them in Resources/CharacterModels.asset.
    /// Movement itself stays ours (PlayerMotor / NavMeshAgent): the controllers have no root motion and are fed
    /// from CharacterAnimator's parameters by SkinnedBody (Forward / Right in m/s, Crouch, Prone, Grounded, ...).
    /// </summary>
    public static class CharacterAnimatorBuilder
    {
        const string Dir = "Assets/_Game/Generated/Anim";
        const string JU = "Assets/Julhiecio TPS Controller/Animations/";
        const string HA = JU + "Humanoid Animations/";

        // blend positions in m/s, matched to PlayerMotor speeds (walk 4.6, sprint 7.4, crouch 2.5, prone 1.15)
        const float Walk = 2.0f, Run = 4.6f, Sprint = 7.4f, CrouchWalk = 2.5f, ProneMove = 1.15f;

        [MenuItem("Tools/FPS/Build Character Animators")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Generated")) AssetDatabase.CreateFolder("Assets/_Game", "Generated");
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/_Game/Generated", "Anim");

            var upperMask = UpperBodyMask();
            var tp = BuildThirdPerson(upperMask);
            var fp = BuildFirstPerson();

            var db = AssetDatabase.LoadAssetAtPath<CharacterModelDB>("Assets/Resources/CharacterModels.asset");
            if (db)
            {
                db.tpController = tp;
                db.fpController = fp;
                EditorUtility.SetDirty(db);
            }
            else Debug.LogWarning("[CharacterAnimatorBuilder] Resources/CharacterModels.asset not found — run Build Character Models first");
            AssetDatabase.SaveAssets();
            Debug.Log("[CharacterAnimatorBuilder] built " + AssetDatabase.GetAssetPath(tp) + " and " + AssetDatabase.GetAssetPath(fp));
        }

        // ------------------------------------------------------------------ clips

        static AnimationClip Clip(string path, string clipName = null, bool loop = true)
        {
            AnimationClip found = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (!(o is AnimationClip c) || c.name.StartsWith("__preview")) continue;
                if (clipName == null || c.name == clipName) { found = c; break; }
            }
            if (!found) { Debug.LogWarning("[CharacterAnimatorBuilder] clip not found: " + path + " " + clipName); return null; }
            if (loop) EnsureLoop(path, found);
            return found;
        }

        /// <summary>Idle / hold clips must loop (several JU clips are imported as one-shots).</summary>
        static void EnsureLoop(string path, AnimationClip clip)
        {
            if (path.EndsWith(".anim"))
            {
                var s = AnimationUtility.GetAnimationClipSettings(clip);
                if (s.loopTime) return;
                s.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(clip, s);
                EditorUtility.SetDirty(clip);
                return;
            }
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!imp) return;
            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            bool changed = false;
            foreach (var c in clips)
                if (c.name == clip.name && !c.loopTime) { c.loopTime = true; changed = true; }
            if (!changed) return;
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
        }

        static AnimationClip Dir8(string folder, string prefix, string dir, string fileStem)
            => Clip(folder + fileStem + " " + dir + ".fbx", prefix + " " + Title(dir));

        static string Title(string s)
        {
            var parts = s.Split(' ');
            for (int i = 0; i < parts.Length; i++) parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            return string.Join(" ", parts);
        }

        // ------------------------------------------------------------------ masks

        static AvatarMask UpperBodyMask()
        {
            string path = Dir + "/UpperBody.mask";
            var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (!m) { m = new AvatarMask(); AssetDatabase.CreateAsset(m, path); }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) m.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
            foreach (var p in new[] { AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                                      AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers, AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK })
                m.SetHumanoidBodyPartActive(p, true);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ third person

        static AnimatorController NewController(string path)
        {
            AssetDatabase.DeleteAsset(path);
            return AnimatorController.CreateAnimatorControllerAtPath(path);
        }

        static BlendTree Tree(AnimatorController ac, string name, BlendTreeType type, string px, string py = null)
        {
            var t = new BlendTree { name = name, blendType = type, blendParameter = px, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            if (py != null) t.blendParameterY = py;
            AssetDatabase.AddObjectToAsset(t, ac);
            return t;
        }

        static void Add2D(BlendTree t, AnimationClip c, float x, float y) { if (c) t.AddChild(c, new Vector2(x, y)); }

        /// <summary>8-way directional ring (+ optional center) at a given speed.</summary>
        static void Ring(BlendTree t, float s, AnimationClip f, AnimationClip b, AnimationClip l, AnimationClip r,
                         AnimationClip fl, AnimationClip fr, AnimationClip bl, AnimationClip br)
        {
            float d = s * 0.7071f;
            Add2D(t, f, 0f, s); Add2D(t, b, 0f, -s); Add2D(t, l, -s, 0f); Add2D(t, r, s, 0f);
            Add2D(t, fl, -d, d); Add2D(t, fr, d, d); Add2D(t, bl, -d, -d); Add2D(t, br, d, -d);
        }

        static AnimatorController BuildThirdPerson(AvatarMask upperMask)
        {
            var ac = NewController(Dir + "/FPS_Character_TPS.controller");
            foreach (var p in new[] { "Forward", "Right", "Crouch", "Prone", "Pistol", "AnimSpeed" })
                ac.AddParameter(p, AnimatorControllerParameterType.Float);
            ac.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            ac.AddParameter("Reload", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Throw", AnimatorControllerParameterType.Trigger);
            ac.AddParameter("Bolt", AnimatorControllerParameterType.Trigger);
            var ps = ac.parameters;
            foreach (var p in ps) if (p.name == "AnimSpeed") p.defaultFloat = 1f; else if (p.name == "Grounded") p.defaultBool = true;
            ac.parameters = ps;

            string walkDir = HA + "Walk Animations/Armed/", runDir = HA + "Running Animations/Armed/", crDir = HA + "Crouched Animations/Crouched Armed/";

            // ---- standing: idle + walk ring + run ring + sprint
            var stand = Tree(ac, "Stand", BlendTreeType.FreeformDirectional2D, "Right", "Forward");
            Add2D(stand, Clip(HA + "Idle Animations/Rifle Idle.fbx", "Rifle Idle Normal"), 0f, 0f);
            Ring(stand, Walk,
                Dir8(walkDir, "Walk", "forward", "walk"), Dir8(walkDir, "Walk", "backward", "walk"),
                Dir8(walkDir, "Walk", "left", "walk"), Dir8(walkDir, "Walk", "right", "walk"),
                Dir8(walkDir, "Walk", "forward left", "walk"), Dir8(walkDir, "Walk", "forward right", "walk"),
                Dir8(walkDir, "Walk", "backward left", "walk"), Dir8(walkDir, "Walk", "backward right", "walk"));
            Ring(stand, Run,
                Dir8(runDir, "Run", "forward", "run"), Dir8(runDir, "Run", "backward", "run"),
                Dir8(runDir, "Run", "left", "run"), Dir8(runDir, "Run", "right", "run"),
                Dir8(runDir, "Run", "forward left", "run"), Dir8(runDir, "Run", "forward right", "run"),
                Dir8(runDir, "Run", "backward left", "run"), Dir8(runDir, "Run", "backward right", "run"));
            Add2D(stand, Clip(HA + "Running Animations/Sprinting.anim"), 0f, Sprint);

            // ---- crouched: idle + crouch-walk ring
            var crouch = Tree(ac, "Crouch", BlendTreeType.FreeformDirectional2D, "Right", "Forward");
            Add2D(crouch, Clip(crDir + "crouch idle.fbx", "Crouch Idle Rifle"), 0f, 0f);
            Ring(crouch, CrouchWalk,
                Clip(crDir + "walk crouching forward.fbx"), Clip(crDir + "walk crouching backward.fbx"),
                Clip(crDir + "walk crouching left.fbx"), Clip(crDir + "walk crouching right.fbx"),
                Clip(crDir + "walk crouching forward left.fbx"), Clip(crDir + "walk crouching forward right.fbx"),
                Clip(crDir + "walk crouching backward left.fbx"), Clip(crDir + "walk crouching backward right.fbx"));

            // ---- prone: backwards / idle / forward on Forward
            var prone = Tree(ac, "Prone", BlendTreeType.Simple1D, "Forward");
            var pb = Clip(HA + "Prone/Prone Backwards.anim"); var pi = Clip(HA + "Prone/Prone Idle.anim"); var pf = Clip(HA + "Prone/Prone Forward.anim");
            if (pb) prone.AddChild(pb, -ProneMove);
            if (pi) prone.AddChild(pi, 0f);
            if (pf) prone.AddChild(pf, ProneMove);

            var standCrouch = Tree(ac, "StandCrouch", BlendTreeType.Simple1D, "Crouch");
            standCrouch.AddChild(stand, 0f);
            standCrouch.AddChild(crouch, 1f);

            var ground = Tree(ac, "Ground", BlendTreeType.Simple1D, "Prone");
            ground.AddChild(standCrouch, 0f);
            ground.AddChild(prone, 1f);

            var sm = ac.layers[0].stateMachine;
            var move = sm.AddState("Move");
            move.motion = ground;
            move.speedParameterActive = true;
            move.speedParameter = "AnimSpeed";
            sm.defaultState = move;

            var air = sm.AddState("Air");
            air.motion = Clip(HA + "Jump/Falling Loop.anim");
            var toAir = move.AddTransition(air);
            toAir.hasExitTime = false; toAir.duration = 0.2f;
            toAir.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            var toMove = air.AddTransition(move);
            toMove.hasExitTime = false; toMove.duration = 0.12f;
            toMove.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            // ---- upper body: weapon hold (rifle / pistol) + reload / throw / bolt one-shots
            var upper = new AnimatorControllerLayer
            {
                name = "UpperBody", defaultWeight = 1f, avatarMask = upperMask, blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = new AnimatorStateMachine { name = "UpperBody", hideFlags = HideFlags.HideInHierarchy },
            };
            AssetDatabase.AddObjectToAsset(upper.stateMachine, ac);
            ac.AddLayer(upper);
            var usm = ac.layers[1].stateMachine;

            var hold = Tree(ac, "Hold", BlendTreeType.Simple1D, "Pistol");
            var rifle = Clip(HA + "Armed Poses/Hold Rifle Two Hands 2.anim");
            var pistol = Clip(HA + "Armed Poses/Hold Pistol Two Hands.anim");
            if (rifle) hold.AddChild(rifle, 0f);
            if (pistol) hold.AddChild(pistol, 1f);
            var holdState = usm.AddState("Hold");
            holdState.motion = hold;
            usm.defaultState = holdState;

            void OneShot(string state, string trigger, AnimationClip clip)
            {
                if (!clip) return;
                var s = usm.AddState(state);
                s.motion = clip;
                var tin = usm.AddAnyStateTransition(s);
                tin.hasExitTime = false; tin.duration = 0.12f; tin.canTransitionToSelf = false;
                tin.AddCondition(AnimatorConditionMode.If, 0f, trigger);
                var tout = s.AddTransition(holdState);
                tout.hasExitTime = true; tout.exitTime = 0.9f; tout.duration = 0.15f;
            }
            OneShot("ReloadRifle", "Reload", Clip(JU + "ReloadRifle.anim", null, false));
            OneShot("Throw", "Throw", Clip(HA + "Throw/Throw.anim", null, false));
            OneShot("Bolt", "Bolt", Clip(JU + "PullBolt.anim", null, false));

            EditorUtility.SetDirty(ac);
            return ac;
        }

        // ------------------------------------------------------------------ first person arms

        static AnimatorController BuildFirstPerson()
        {
            var ac = NewController(Dir + "/FPS_Arms_FP.controller");
            ac.AddParameter("Pistol", AnimatorControllerParameterType.Float);
            // read by FPS Animation's AdsLayer (crouch pose curve) — stays 0
            ac.AddParameter("CrouchPose", AnimatorControllerParameterType.Float);
            var hold = Tree(ac, "Hold", BlendTreeType.Simple1D, "Pistol");
            var rifle = Clip(HA + "Armed Poses/Hold Rifle Two Hands 2.anim");
            var pistol = Clip(HA + "Armed Poses/Hold Pistol Two Hands.anim");
            if (rifle) hold.AddChild(rifle, 0f);
            if (pistol) hold.AddChild(pistol, 1f);
            var sm = ac.layers[0].stateMachine;
            var s = sm.AddState("Hold");
            s.motion = hold;
            sm.defaultState = s;
            EditorUtility.SetDirty(ac);
            return ac;
        }
    }
}
