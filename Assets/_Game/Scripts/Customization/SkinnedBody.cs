using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Wears an imported skinned (Humanoid) character on top of a procedural HumanoidRig.
    /// The procedural rig keeps doing everything it did before (CharacterAnimator poses it, its primitive
    /// colliders stay the hitboxes); its meshes stop rendering and every frame the skinned skeleton copies
    /// the rig's pose.
    ///
    /// Retarget: the procedural bones rest with identity rotations (arms hanging down), the imported model
    /// rests in a T-pose. For each mapped bone we store C = rotation that turns the model's rest bone direction
    /// into the rig's rest bone direction, then per frame: modelBone = rigBoneDelta * C * modelRest.
    /// </summary>
    [DefaultExecutionOrder(260)]
    public class SkinnedBody : MonoBehaviour
    {
        struct Map
        {
            public Transform model, rig;
            public Quaternion correction, rest; // rest = model bone rotation relative to the body root
        }

        HumanoidRig rigRef;
        Transform body;          // HumanoidRig transform (shared reference frame)
        Transform modelHips;
        readonly List<Map> maps = new List<Map>();
        Transform modelHead;
        Vector3 modelHeadScale = Vector3.one;
        public Renderer[] Renderers { get; private set; }
        public int ModelIndex { get; private set; }

        /// <summary>Instantiates the model under the rig and binds it. Returns null if the model has no humanoid avatar.</summary>
        public static SkinnedBody Attach(HumanoidRig rig, int modelIndex, CharacterModelDB.Entry e)
        {
            var inst = Instantiate(e.prefab, rig.transform, false);
            inst.name = "SkinnedModel";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            var an = inst.GetComponentInChildren<Animator>();
            if (!an || !an.avatar || !an.avatar.isHuman || !an.GetBoneTransform(HumanBodyBones.Hips))
            {
                Debug.LogWarning($"[SkinnedBody] {e.prefab.name} has no humanoid avatar");
                if (Application.isPlaying) Destroy(inst); else DestroyImmediate(inst);
                return null;
            }
            // the model must not collide or block shots: the procedural rig's colliders are the hitboxes
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) lg.enabled = true;

            var sb = inst.AddComponent<SkinnedBody>();
            sb.ModelIndex = modelIndex;
            sb.Bind(rig, an);
            an.enabled = false;

            var rs = new List<Renderer>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (e.skin) { var mats = r.sharedMaterials; for (int i = 0; i < mats.Length; i++) mats[i] = e.skin; r.sharedMaterials = mats; }
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                rs.Add(r);
            }
            sb.Renderers = rs.ToArray();
            return sb;
        }

        Quaternion BodyRot(Transform t) => Quaternion.Inverse(body.rotation) * t.rotation;
        Vector3 BodyPos(Transform t) => body.InverseTransformPoint(t.position);

        void Bind(HumanoidRig rig, Animator an)
        {
            rigRef = rig;
            body = rig.transform;
            Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
            modelHips = B(HumanBodyBones.Hips);

            // scale the model so its hips sit where the procedural hips rest
            float hipsH = BodyPos(modelHips).y;
            float rigHips = rig.hips.localPosition.y + (rig.rig ? rig.rig.localPosition.y : 0f);
            if (hipsH > 0.1f) transform.localScale = Vector3.one * (rigHips / hipsH);

            var chest = B(HumanBodyBones.Chest);
            var upper = B(HumanBodyBones.UpperChest);
            var neck = B(HumanBodyBones.Neck);
            var head = B(HumanBodyBones.Head);
            var spine = B(HumanBodyBones.Spine);
            Transform chestTop = upper ? upper : chest;

            Quaternion Add(Transform m, Transform mChild, Transform r, Vector3 rigRestDir, Quaternion? inherit = null)
            {
                if (!m || !r) return inherit ?? Quaternion.identity;
                Quaternion c;
                if (inherit.HasValue || !mChild) c = inherit ?? Quaternion.identity;
                else
                {
                    Vector3 md = BodyPos(mChild) - BodyPos(m);
                    c = md.sqrMagnitude > 1e-6f && rigRestDir.sqrMagnitude > 1e-6f ? Quaternion.FromToRotation(md, rigRestDir) : Quaternion.identity;
                }
                maps.Add(new Map { model = m, rig = r, correction = c, rest = BodyRot(m) });
                return c;
            }

            Vector3 up = Vector3.up;
            Add(modelHips, spine, rig.hips, rig.spine ? rig.spine.localPosition : up);
            Add(spine, chest ? chest : chestTop, rig.spine, rig.chest ? rig.chest.localPosition : up);
            var cc = Add(chest, upper ? upper : neck, rig.chest, rig.neck ? rig.neck.localPosition : up);
            if (upper) Add(upper, neck, rig.chest, rig.neck ? rig.neck.localPosition : up, null);
            var nc = Add(neck, head, rig.neck, rig.head ? rig.head.localPosition : up);
            Add(head, null, rig.head, up, nc);

            void Arm(HumanBodyBones ua, HumanBodyBones la, HumanBodyBones hand, Transform rUa, Transform rLa, Transform rHand)
            {
                Add(B(ua), B(la), rUa, rLa ? rLa.localPosition : Vector3.down);
                var c = Add(B(la), B(hand), rLa, rHand ? rHand.localPosition : Vector3.down);
                Add(B(hand), null, rHand, Vector3.down, c);
            }
            Arm(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, rig.lUpperArm, rig.lForearm, rig.lHand);
            Arm(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, rig.rUpperArm, rig.rForearm, rig.rHand);
            Arm(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, rig.lThigh, rig.lShin, rig.lFoot);
            Arm(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, rig.rThigh, rig.rShin, rig.rFoot);

            modelHead = head;
            if (head) modelHeadScale = head.localScale;
            Sync();
        }

        void LateUpdate() => Sync();

        /// <summary>Copies the rig's current pose onto the model (runs every LateUpdate; public for editor previews).</summary>
        public void Sync()
        {
            if (!rigRef || !body) return;
            Quaternion br = body.rotation;
            foreach (var m in maps)
                m.model.rotation = br * (BodyRot(m.rig) * m.correction * m.rest);
            if (modelHips && rigRef.hips) modelHips.position = rigRef.hips.position;
            if (modelHead && rigRef.head) modelHead.localScale = Vector3.Scale(modelHeadScale, rigRef.head.localScale);
        }
    }
}
