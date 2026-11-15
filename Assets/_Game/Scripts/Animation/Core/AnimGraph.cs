using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FPS.Animation
{
    /// <summary>
    /// Playable graph of a character (KINEMATION's CoreAnimGraph, rebuilt):
    ///   AnimatorController (locomotion, full body) → overlay poses (static / looping hold poses, masked) →
    ///   slot animations (one-shots like reloads, masked, with curves) → output.
    /// We own the graph: the Animator's own controller is cleared and the controller runs as a playable here,
    /// so parameters go through SetFloat / SetBool / SetTrigger of this class.
    /// </summary>
    public class AnimGraph
    {
        public PlayableGraph Graph => graph;
        public bool IsValid => graph.IsValid();
        public AnimatorControllerPlayable Controller => controller;
        /// <summary>Blend-in progress (0..1) of the newest overlay pose — layers use it to blend per-weapon data.</summary>
        public float PoseProgress { get; private set; } = 1f;
        /// <summary>Global weight of overlay poses and slot animations (1 - sprint pose, etc.).</summary>
        public float GraphWeight { get; private set; } = 1f;
        /// <summary>Spine root rotation requested by the playing slot animation.</summary>
        public Quaternion SpineOffset { get; private set; } = Quaternion.identity;
        public AvatarMask UpperBodyMask { get; set; }

        PlayableGraph graph;
        AnimatorControllerPlayable controller;
        SlotMixer overlay, slot;
        readonly Dictionary<int, AnimatorControllerParameterType> parameters = new Dictionary<int, AnimatorControllerParameterType>();
        readonly Dictionary<string, float> curveValues = new Dictionary<string, float>();

        public static AnimGraph Create(Animator animator, RuntimeAnimatorController controllerAsset, AvatarMask upperBodyMask, string name = "FPSAnimGraph")
        {
            if (!animator) return null;
            var g = new AnimGraph { UpperBodyMask = upperBodyMask };
            g.graph = PlayableGraph.Create(name + "_" + animator.name);
            g.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

            Playable baseInput;
            if (controllerAsset)
            {
                g.controller = AnimatorControllerPlayable.Create(g.graph, controllerAsset);
                baseInput = g.controller;
                for (int i = 0; i < g.controller.GetParameterCount(); i++)
                {
                    var p = g.controller.GetParameter(i);
                    g.parameters[p.nameHash] = p.type;
                }
            }
            else baseInput = AnimationLayerMixerPlayable.Create(g.graph, 1);

            g.overlay = new SlotMixer(g.graph, 4, baseInput, true);
            g.slot = new SlotMixer(g.graph, 3, g.overlay.Mixer, false);
            var output = AnimationPlayableOutput.Create(g.graph, "FPSAnimator", animator);
            output.SetSourcePlayable(g.slot.Mixer);
            // our graph drives the Animator; its own controller would run a second, unrelated copy
            animator.runtimeAnimatorController = null;
            g.graph.Play();
            return g;
        }

        public void Destroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }

        // ------------------------------------------------------------------ controller parameters

        public bool HasParameter(int hash) => parameters.ContainsKey(hash);

        public void SetFloat(int hash, float v) { if (Has(hash, AnimatorControllerParameterType.Float)) controller.SetFloat(hash, v); }
        public void SetFloat(int hash, float v, float dampTime, float dt)
        {
            if (!Has(hash, AnimatorControllerParameterType.Float)) return;
            float cur = controller.GetFloat(hash);
            controller.SetFloat(hash, dampTime <= 0f ? v : Mathf.Lerp(cur, v, 1f - Mathf.Exp(-dt / dampTime)));
        }
        public float GetFloat(int hash) => Has(hash, AnimatorControllerParameterType.Float) ? controller.GetFloat(hash) : 0f;
        public void SetBool(int hash, bool v) { if (Has(hash, AnimatorControllerParameterType.Bool)) controller.SetBool(hash, v); }
        public bool GetBool(int hash) => Has(hash, AnimatorControllerParameterType.Bool) && controller.GetBool(hash);
        public void SetInteger(int hash, int v) { if (Has(hash, AnimatorControllerParameterType.Int)) controller.SetInteger(hash, v); }
        public void SetTrigger(int hash) { if (Has(hash, AnimatorControllerParameterType.Trigger)) controller.SetTrigger(hash); }
        public void ResetTrigger(int hash) { if (Has(hash, AnimatorControllerParameterType.Trigger)) controller.ResetTrigger(hash); }

        bool Has(int hash, AnimatorControllerParameterType type)
            => controller.IsValid() && parameters.TryGetValue(hash, out var t) && t == type;

        public AnimatorStateInfo GetStateInfo(int layer) => controller.IsValid() ? controller.GetCurrentAnimatorStateInfo(layer) : default;
        public void SetLayerWeight(int layer, float w) { if (controller.IsValid() && layer < controller.GetLayerCount()) controller.SetLayerWeight(layer, w); }
        public void Play(string state, int layer = 0, float normalizedTime = 0f) { if (controller.IsValid()) controller.Play(state, layer, normalizedTime); }
        public void CrossFade(string state, float time, int layer = 0) { if (controller.IsValid()) controller.CrossFadeInFixedTime(state, time, layer); }

        // ------------------------------------------------------------------ curves

        /// <summary>Value of a slot-animation curve, else of an animated float parameter of the controller (0 if none).</summary>
        public float GetCurveValue(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0f;
            if (curveValues.TryGetValue(name, out var v)) return v;
            int h = Animator.StringToHash(name);
            return Has(h, AnimatorControllerParameterType.Float) ? controller.GetFloat(h) : 0f;
        }

        // ------------------------------------------------------------------ poses / animations

        public void SetGraphWeight(float w) => GraphWeight = Mathf.Clamp01(w);

        /// <summary>Static (speed 0) or looping hold pose on top of the controller. mask null = the upper body mask.</summary>
        public void PlayPose(AnimationClip clip, float blendIn, float time = 0f, bool animate = false, AvatarMask mask = null)
        {
            if (!clip || !graph.IsValid()) return;
            var p = AnimationClipPlayable.Create(graph, clip);
            p.SetApplyFootIK(false);
            p.SetTime(time);
            p.SetSpeed(animate ? 1f : 0f);
            overlay.Push(new SlotMixer.Item { playable = p, length = clip.length, blendIn = blendIn, mask = mask ? mask : UpperBodyMask, persistent = true });
        }

        public void PlayPose(AnimSequence seq)
        {
            if (seq && seq.clip) PlayPose(seq.clip, seq.blendTime.blendIn, seq.poseTime, seq.loop, seq.mask);
        }

        /// <summary>Clears the overlay (back to the controller pose).</summary>
        public void ClearPose(float blendOut) => overlay.FadeAll(blendOut);

        /// <summary>One-shot animation on the masked body part, blends out at its end.</summary>
        public void PlayAnimation(AnimationClip clip, BlendTime blend, AvatarMask mask = null, float playRate = 1f,
                                  Quaternion? spineRotation = null, AnimCurveBinding[] curves = null)
        {
            if (!clip || !graph.IsValid()) return;
            var p = AnimationClipPlayable.Create(graph, clip);
            p.SetApplyFootIK(false);
            p.SetTime(0f);
            p.SetSpeed(playRate);
            slot.Push(new SlotMixer.Item
            {
                playable = p, length = clip.length / Mathf.Max(0.01f, Mathf.Abs(playRate)), blendIn = blend.blendIn, blendOut = blend.blendOut,
                mask = mask ? mask : UpperBodyMask, curves = curves, spine = spineRotation ?? Quaternion.identity, persistent = false,
            });
        }

        public void PlayAnimation(AnimSequence seq, float playRate = 1f)
        {
            if (seq && seq.clip) PlayAnimation(seq.clip, seq.blendTime, seq.mask, playRate * seq.playRate, seq.spineRotation, seq.curves);
        }

        public void StopAnimation(float blendOut) => slot.FadeAll(blendOut);
        public bool IsPlayingAnimation => slot.ActiveCount > 0;

        /// <summary>Advance blends. Call once per frame before the Animator evaluates (Update).</summary>
        public void Update(float dt)
        {
            if (!graph.IsValid()) return;
            overlay.Update(dt, GraphWeight);
            slot.Update(dt, GraphWeight);
            PoseProgress = overlay.TopWeight;
            SpineOffset = slot.SpineRotation();
            curveValues.Clear();
            slot.WriteCurves(curveValues);
        }

        /// <summary>Evaluates the graph now (sample a pose immediately, e.g. right after equipping).</summary>
        public void Evaluate() { if (graph.IsValid()) graph.Evaluate(0f); }

        // =================================================================================================

        /// <summary>Layer mixer whose input 0 is the base; items stack on top, the newest blends in over the older ones.</summary>
        class SlotMixer
        {
            public class Item
            {
                public AnimationClipPlayable playable;
                public float length, blendIn, blendOut, weight;
                public AvatarMask mask;
                public AnimCurveBinding[] curves;
                public Quaternion spine = Quaternion.identity;
                public bool persistent, fading;
                public float fadeSpeed;
            }

            public AnimationLayerMixerPlayable Mixer { get; }
            readonly PlayableGraph graph;
            readonly int capacity;
            readonly List<Item> items = new List<Item>();

            public SlotMixer(PlayableGraph graph, int capacity, Playable source, bool overlay)
            {
                this.graph = graph;
                this.capacity = capacity;
                Mixer = AnimationLayerMixerPlayable.Create(graph, capacity + 1);
                graph.Connect(source, 0, Mixer, 0);
                Mixer.SetInputWeight(0, 1f);
            }

            public int ActiveCount => items.Count;
            public float TopWeight => items.Count > 0 ? items[items.Count - 1].weight : 1f;

            public void Push(Item it)
            {
                if (items.Count >= capacity) { Release(items[0]); items.RemoveAt(0); }
                it.weight = it.blendIn <= 0f ? 1f : 0f;
                items.Add(it);
                Reconnect();
            }

            public void FadeAll(float time)
            {
                foreach (var it in items) { it.fading = true; it.fadeSpeed = time <= 0f ? 1e6f : 1f / time; }
            }

            void Release(Item it)
            {
                if (it.playable.IsValid()) it.playable.Destroy();
            }

            void Reconnect()
            {
                for (int i = 1; i <= capacity; i++) if (Mixer.GetInput(i).IsValid()) graph.Disconnect(Mixer, i);
                for (int i = 0; i < items.Count; i++)
                {
                    graph.Connect(items[i].playable, 0, Mixer, i + 1);
                    if (items[i].mask) Mixer.SetLayerMaskFromAvatarMask((uint)(i + 1), items[i].mask);
                    else Mixer.SetLayerMaskFromAvatarMask((uint)(i + 1), new AvatarMask());
                    Mixer.SetInputWeight(i + 1, items[i].weight);
                }
                for (int i = items.Count + 1; i <= capacity; i++) Mixer.SetInputWeight(i, 0f);
            }

            public void Update(float dt, float globalWeight)
            {
                bool changed = false;
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (!it.fading && !it.persistent)
                    {
                        // real seconds left of the clip (play rate included)
                        float t = (float)it.playable.GetTime();
                        float spd = Mathf.Max(0.0001f, Mathf.Abs((float)it.playable.GetSpeed()));
                        float remaining = (ClipLength(it) - t) / spd;
                        if (remaining <= it.blendOut) { it.fading = true; it.fadeSpeed = it.blendOut <= 0f ? 1e6f : 1f / it.blendOut; }
                    }
                    if (it.fading) it.weight = Mathf.MoveTowards(it.weight, 0f, it.fadeSpeed * dt);
                    else it.weight = Mathf.MoveTowards(it.weight, 1f, it.blendIn <= 0f ? 1e6f : dt / it.blendIn);
                }
                // the newest fully blended in hides everything under it (same mask) → drop the older ones
                if (items.Count > 1 && !items[items.Count - 1].fading && items[items.Count - 1].weight >= 1f)
                {
                    for (int i = items.Count - 2; i >= 0; i--) { Release(items[i]); items.RemoveAt(i); changed = true; }
                }
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    if (items[i].fading && items[i].weight <= 0f) { Release(items[i]); items.RemoveAt(i); changed = true; }
                }
                if (changed) Reconnect();
                for (int i = 0; i < items.Count; i++) Mixer.SetInputWeight(i + 1, items[i].weight * globalWeight);
            }

            public Quaternion SpineRotation()
            {
                Quaternion q = Quaternion.identity;
                foreach (var it in items) q = Quaternion.Slerp(q, AnimMath.Valid(it.spine), it.weight);
                return q;
            }

            static float ClipLength(Item it)
            {
                var c = it.playable.IsValid() ? it.playable.GetAnimationClip() : null;
                return c ? c.length : it.length;
            }

            /// <summary>Curve values of the playing items, stacked like the layers (newer over older, by weight).</summary>
            public void WriteCurves(Dictionary<string, float> table)
            {
                foreach (var it in items)
                {
                    if (it.curves == null || !it.playable.IsValid()) continue;
                    float n = Mathf.Clamp01((float)it.playable.GetTime() / Mathf.Max(0.0001f, ClipLength(it)));
                    foreach (var c in it.curves)
                    {
                        if (string.IsNullOrEmpty(c.name) || c.curve == null) continue;
                        table.TryGetValue(c.name, out float prev);
                        table[c.name] = Mathf.Lerp(prev, c.curve.Evaluate(n), it.weight);
                    }
                }
            }
        }
    }
}
