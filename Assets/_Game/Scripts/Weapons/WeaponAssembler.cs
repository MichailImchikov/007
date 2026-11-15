using UnityEngine;
using UnityEngine.Rendering;

namespace FPS
{
    /// <summary>
    /// Builds modular weapons (WeaponData) on WeaponControllers: the scene's pre-built procedural ones (their model is
    /// replaced in WeaponController.Init through ImportedWeapons.Apply) and runtime ones the Loadout creates for catalog
    /// weapons a pre-built scene does not contain. The per-weapon state lives in <see cref="WeaponAttachments"/>.
    /// </summary>
    public static class WeaponAssembler
    {
        /// <summary>Called from WeaponController.Init: a weapon with WeaponData gets its modular model + the player's attachments.</summary>
        public static bool Apply(WeaponController w)
        {
            if (!w) return false;
            var wa = w.GetComponent<WeaponAttachments>();
            if (wa && wa.Assembled) return true;
            var d = WeaponDataDB.Get(w.weaponName);
            if (!d || !d.model) return false;
            if (!wa) wa = w.gameObject.AddComponent<WeaponAttachments>();
            wa.Setup(w, d);
            wa.Build(WeaponDataDB.Selection(d));
            return true;
        }

        /// <summary>
        /// First-person weapon built at runtime for a catalog entry (scenes are pre-built: no rebuild needed for new guns).
        /// The holder is created inactive under <paramref name="parent"/>; the muzzle flash and light are cloned from
        /// <paramref name="flashSource"/> (an existing weapon of the scene) so their materials match.
        /// </summary>
        public static WeaponController CreateRuntime(WeaponDef def, Transform parent, WeaponController flashSource,
                                                     PlayerMotor motor, PlayerCamera pc, Camera cam)
        {
            if (def == null || !WeaponDataDB.Get(def.name)) return null;
            var go = new GameObject("W_" + def.name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            go.layer = flashSource ? flashSource.gameObject.layer : parent ? parent.gameObject.layer : 0;
            var w = go.AddComponent<WeaponController>();
            w.weaponName = def.name;
            w.motor = motor; w.playerCam = pc; w.cam = cam;
            def.Apply(w);
            CloneMuzzleRig(w, flashSource);
            Apply(w);
            go.transform.localPosition = w.hipPosition;
            return w;
        }

        /// <summary>"Muzzle" with a copy of another weapon's flash quads and light (or a simple one).</summary>
        static void CloneMuzzleRig(WeaponController w, WeaponController src)
        {
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(w.transform, false);
            muzzle.gameObject.layer = w.gameObject.layer;
            w.muzzle = muzzle;
            GameObject flash;
            if (src && src.muzzleFlash)
            {
                flash = Object.Instantiate(src.muzzleFlash, muzzle, false);
            }
            else
            {
                flash = new GameObject();
                flash.transform.SetParent(muzzle, false);
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Kill(q.GetComponent<Collider>());
                q.name = "FlashFront";
                q.transform.SetParent(flash.transform, false);
                q.transform.localPosition = new Vector3(0f, 0f, 0.015f);
                q.transform.localScale = new Vector3(0.15f, 0.15f, 1f);
                var r = q.GetComponent<MeshRenderer>();
                r.sharedMaterial = FX.MakeParticleMat("WA_Flash", FX.MuzzleStarTex(64), true, new Color(4f, 3.2f, 2.4f, 1f), true);
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            flash.name = "MuzzleFlash";
            flash.transform.localPosition = Vector3.zero;
            flash.transform.localRotation = Quaternion.identity;
            flash.transform.localScale = Vector3.one;
            flash.SetActive(false);
            w.muzzleFlash = flash;

            GameObject lightGo;
            if (src && src.muzzleLight) lightGo = Object.Instantiate(src.muzzleLight.gameObject, muzzle, false);
            else
            {
                lightGo = new GameObject();
                lightGo.transform.SetParent(muzzle, false);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point; l.range = 6f; l.color = new Color(1f, 0.68f, 0.38f); l.intensity = 4f; l.shadows = LightShadows.None;
            }
            lightGo.name = "MuzzleLight";
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.08f);
            lightGo.transform.localRotation = Quaternion.identity;
            w.muzzleLight = lightGo.GetComponent<Light>();
            if (w.muzzleLight) w.muzzleLight.enabled = false;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>A fresh model / attachment instance: no animator or physics, first-person layer, no shadows.</summary>
        public static void Prepare(GameObject go, int layer)
        {
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) Kill(a);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Kill(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Kill(rb);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.lightProbeUsage = LightProbeUsage.BlendProbes;
            }
        }

        /// <summary>New parent at the part's pose, so two animations can move it (per-shot cycle + reload pull).</summary>
        public static Transform Pivot(string name, Transform part)
        {
            var p = new GameObject(name).transform;
            p.SetParent(part.parent, false);
            p.localPosition = part.localPosition;
            p.localRotation = part.localRotation;
            p.localScale = Vector3.one;
            p.gameObject.layer = part.gameObject.layer;
            part.SetParent(p, true);
            return p;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (!root) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r) return r;
            }
            return null;
        }

        /// <summary>Destroy that works in edit mode too; a destroyed GameObject leaves the hierarchy at once.</summary>
        public static void Kill(Object o)
        {
            if (!o) return;
            if (!Application.isPlaying) { Object.DestroyImmediate(o); return; }
            if (o is GameObject g)
            {
                g.SetActive(false);
                g.transform.SetParent(null, false);
            }
            Object.Destroy(o);
        }
    }
}
