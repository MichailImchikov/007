using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// Bone references of a procedural humanoid (primitive meshes on a real bone hierarchy).
    /// Animated by CharacterAnimator. Swap for a skinned Asset Store character later by
    /// pointing these fields at its bones (or replacing CharacterAnimator with an Animator).
    /// </summary>
    public class HumanoidRig : MonoBehaviour
    {
        public Transform rig, hips, spine, chest, neck, head;
        public Transform lUpperArm, lForearm, lHand, rUpperArm, rForearm, rHand;
        public Transform lThigh, lShin, lFoot, rThigh, rShin, rFoot;
        public Transform weaponSocket;
        public Transform neckPoint;
        public Renderer[] renderers;

        public void SetShadowsOnly(bool on)
        {
            foreach (var r in renderers)
                if (r) r.shadowCastingMode = on ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        public void SetCollidersEnabled(bool on)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        }

        public struct Palette
        {
            public Material skin, shirt, pants, vest, boots, gloves, helmet, gear;
        }

        /// <summary>Builds the rig under parent (feet at parent origin, facing +Z).</summary>
        public static HumanoidRig Build(Transform parent, Palette p, bool hitboxes)
        {
            var root = new GameObject("Body");
            root.transform.SetParent(parent, false);
            var h = root.AddComponent<HumanoidRig>();

            h.rig = Bone("Rig", root.transform, Vector3.zero);
            h.hips = Bone("Hips", h.rig, new Vector3(0f, 0.98f, 0f));
            h.spine = Bone("Spine", h.hips, new Vector3(0f, 0.1f, 0f));
            h.chest = Bone("Chest", h.spine, new Vector3(0f, 0.2f, 0f));
            h.neck = Bone("Neck", h.chest, new Vector3(0f, 0.3f, 0f));
            h.head = Bone("Head", h.neck, new Vector3(0f, 0.08f, 0f));
            h.lUpperArm = Bone("L_UpperArm", h.chest, new Vector3(-0.22f, 0.26f, 0f));
            h.lForearm = Bone("L_Forearm", h.lUpperArm, new Vector3(0f, -0.29f, 0f));
            h.lHand = Bone("L_Hand", h.lForearm, new Vector3(0f, -0.26f, 0f));
            h.rUpperArm = Bone("R_UpperArm", h.chest, new Vector3(0.22f, 0.26f, 0f));
            h.rForearm = Bone("R_Forearm", h.rUpperArm, new Vector3(0f, -0.29f, 0f));
            h.rHand = Bone("R_Hand", h.rForearm, new Vector3(0f, -0.26f, 0f));
            h.lThigh = Bone("L_Thigh", h.hips, new Vector3(-0.1f, -0.05f, 0f));
            h.lShin = Bone("L_Shin", h.lThigh, new Vector3(0f, -0.44f, 0f));
            h.lFoot = Bone("L_Foot", h.lShin, new Vector3(0f, -0.42f, 0f));
            h.rThigh = Bone("R_Thigh", h.hips, new Vector3(0.1f, -0.05f, 0f));
            h.rShin = Bone("R_Shin", h.rThigh, new Vector3(0f, -0.44f, 0f));
            h.rFoot = Bone("R_Foot", h.rShin, new Vector3(0f, -0.42f, 0f));
            h.weaponSocket = Bone("WeaponSocket", h.chest, new Vector3(0.13f, 0.12f, 0.32f));
            h.neckPoint = Bone("NeckPoint", h.neck, new Vector3(0f, 0.04f, -0.05f));

            // ---- meshes
            Mesh(PrimitiveType.Cube, "Pelvis", h.hips, new Vector3(0f, -0.02f, 0f), new Vector3(0.34f, 0.18f, 0.22f), p.pants, hitboxes, 1f);
            Mesh(PrimitiveType.Cube, "Belt", h.hips, new Vector3(0f, 0.07f, 0f), new Vector3(0.35f, 0.05f, 0.23f), p.gear, false, 0f);
            Mesh(PrimitiveType.Cube, "Abdomen", h.spine, new Vector3(0f, 0.1f, 0f), new Vector3(0.31f, 0.2f, 0.2f), p.shirt, hitboxes, 1f);
            Mesh(PrimitiveType.Cube, "Torso", h.chest, new Vector3(0f, 0.14f, 0f), new Vector3(0.4f, 0.3f, 0.23f), p.shirt, hitboxes, 1f);
            Mesh(PrimitiveType.Cube, "Vest", h.chest, new Vector3(0f, 0.12f, 0.005f), new Vector3(0.42f, 0.26f, 0.27f), p.vest, false, 0f);
            Mesh(PrimitiveType.Cube, "PouchL", h.chest, new Vector3(-0.1f, 0.04f, 0.15f), new Vector3(0.08f, 0.1f, 0.05f), p.gear, false, 0f);
            Mesh(PrimitiveType.Cube, "PouchR", h.chest, new Vector3(0.1f, 0.04f, 0.15f), new Vector3(0.08f, 0.1f, 0.05f), p.gear, false, 0f);
            Mesh(PrimitiveType.Cube, "Backpack", h.chest, new Vector3(0f, 0.13f, -0.19f), new Vector3(0.3f, 0.32f, 0.13f), p.gear, false, 0f);
            Mesh(PrimitiveType.Cube, "Shoulders", h.chest, new Vector3(0f, 0.27f, 0f), new Vector3(0.46f, 0.07f, 0.2f), p.shirt, hitboxes, 1f);
            Mesh(PrimitiveType.Cylinder, "NeckMesh", h.neck, new Vector3(0f, 0.04f, 0f), new Vector3(0.1f, 0.05f, 0.1f), p.skin, false, 0f);
            var headMesh = Mesh(PrimitiveType.Sphere, "HeadMesh", h.head, new Vector3(0f, 0.1f, 0.01f), new Vector3(0.2f, 0.24f, 0.22f), p.skin, hitboxes, 2.2f, true);
            // the helmet shell sticks out of the head sphere: it is a head hitbox too (hidden helmets have no collider)
            Mesh(PrimitiveType.Sphere, "Helmet", h.head, new Vector3(0f, 0.16f, -0.005f), new Vector3(0.25f, 0.17f, 0.27f), p.helmet, hitboxes, 2.2f, true);
            if (hitboxes) TightCollider(h.head.Find("Helmet"));
            Mesh(PrimitiveType.Cube, "HelmetRim", h.head, new Vector3(0f, 0.12f, 0f), new Vector3(0.255f, 0.03f, 0.275f), p.helmet, false, 0f);
            Mesh(PrimitiveType.Cube, "Goggles", h.head, new Vector3(0f, 0.12f, 0.115f), new Vector3(0.17f, 0.045f, 0.03f), p.gear, false, 0f);
            Mesh(PrimitiveType.Cube, "Mask", h.head, new Vector3(0f, 0.045f, 0.095f), new Vector3(0.15f, 0.07f, 0.05f), p.gear, false, 0f);

            Limb("L_UpperArmMesh", h.lUpperArm, 0.29f, 0.055f, p.shirt, hitboxes, 0.8f);
            Limb("L_ForearmMesh", h.lForearm, 0.26f, 0.045f, p.shirt, hitboxes, 0.8f);
            Mesh(PrimitiveType.Cube, "L_Glove", h.lHand, new Vector3(0f, -0.05f, 0f), new Vector3(0.07f, 0.1f, 0.04f), p.gloves, hitboxes, 0.6f);
            Limb("R_UpperArmMesh", h.rUpperArm, 0.29f, 0.055f, p.shirt, hitboxes, 0.8f);
            Limb("R_ForearmMesh", h.rForearm, 0.26f, 0.045f, p.shirt, hitboxes, 0.8f);
            Mesh(PrimitiveType.Cube, "R_Glove", h.rHand, new Vector3(0f, -0.05f, 0f), new Vector3(0.07f, 0.1f, 0.04f), p.gloves, hitboxes, 0.6f);

            Limb("L_ThighMesh", h.lThigh, 0.44f, 0.078f, p.pants, hitboxes, 0.8f);
            Limb("L_ShinMesh", h.lShin, 0.42f, 0.062f, p.pants, hitboxes, 0.8f);
            Mesh(PrimitiveType.Cube, "L_Boot", h.lFoot, new Vector3(0f, -0.035f, 0.05f), new Vector3(0.11f, 0.09f, 0.25f), p.boots, hitboxes, 0.6f);
            Mesh(PrimitiveType.Cube, "L_KneePad", h.lShin, new Vector3(0f, -0.02f, 0.06f), new Vector3(0.1f, 0.1f, 0.04f), p.gear, false, 0f);
            Limb("R_ThighMesh", h.rThigh, 0.44f, 0.078f, p.pants, hitboxes, 0.8f);
            Limb("R_ShinMesh", h.rShin, 0.42f, 0.062f, p.pants, hitboxes, 0.8f);
            Mesh(PrimitiveType.Cube, "R_Boot", h.rFoot, new Vector3(0f, -0.035f, 0.05f), new Vector3(0.11f, 0.09f, 0.25f), p.boots, hitboxes, 0.6f);
            Mesh(PrimitiveType.Cube, "R_KneePad", h.rShin, new Vector3(0f, -0.02f, 0.06f), new Vector3(0.1f, 0.1f, 0.04f), p.gear, false, 0f);

            h.renderers = root.GetComponentsInChildren<Renderer>(true);
            return h;
        }

        static Transform Bone(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static GameObject Mesh(PrimitiveType type, string name, Transform bone, Vector3 pos, Vector3 scale, Material m, bool hitbox, float mult, bool crit = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(bone, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = m;
            if (!hitbox)
            {
                var c = go.GetComponent<Collider>();
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            else
            {
                var hb = go.AddComponent<Hitbox>();
                hb.multiplier = mult;
                hb.critical = crit;
            }
            return go;
        }

        /// <summary>Replaces a stretched primitive's sphere collider (radius = largest axis) with its exact convex shape.</summary>
        static void TightCollider(Transform t)
        {
            if (!t) return;
            var sc = t.GetComponent<SphereCollider>();
            if (!sc) return;
            if (Application.isPlaying) Destroy(sc); else DestroyImmediate(sc);
            var mc = t.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = t.GetComponent<MeshFilter>().sharedMesh;
            mc.convex = true;
        }

        static void Limb(string name, Transform bone, float length, float radius, Material m, bool hitbox, float mult)
        {
            Mesh(PrimitiveType.Capsule, name, bone, new Vector3(0f, -length * 0.5f, 0f),
                 new Vector3(radius * 2f, length * 0.5f + radius * 0.3f, radius * 2f), m, hitbox, mult);
        }
    }
}
