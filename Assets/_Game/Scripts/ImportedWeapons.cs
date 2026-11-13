using System.Collections.Generic;
using UnityEngine;

namespace FPS
{
    /// <summary>Marks a procedural weapon part replaced by the imported mesh: it never renders (copies too).</summary>
    public class ImportHidden : MonoBehaviour
    {
        /// <summary>Re-applies hiding on a copy (forceRenderingOff is not serialized).</summary>
        public static void Refresh(GameObject root)
        {
            foreach (var h in root.GetComponentsInChildren<ImportHidden>(true))
            {
                var r = h.GetComponent<Renderer>();
                if (r) r.forceRenderingOff = true;
            }
        }
    }

    /// <summary>
    /// Puts the imported gun mesh on a procedural WeaponController model: the procedural receiver/barrel/stock
    /// stop rendering (they stay as animation anchors), optics, arms and muzzle flash stay. The imported mesh is
    /// fitted to the procedural gun's length and centered on it. Imported slide / bolt / magazine / forend parts
    /// are re-parented to the matching animated procedural transforms, so reloads and cycling still move them.
    /// </summary>
    public static class ImportedWeapons
    {
        // arms, muzzle flash and optics (holo sight, scope) stay procedural
        static readonly string[] KeepVisible = { "Arm", "Flash", "Reflex", "Scope", "Ring", "Reticle", "Knife", "Glove", "Finger", "Thumb", "Cuff", "Sleeve", "Shell" };

        static bool Kept(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
            {
                if (p.name.StartsWith("Sight")) return true;
                foreach (var k in KeepVisible) if (p.name.Contains(k)) return true;
            }
            return false;
        }

        static readonly string[] Optics = { "Reflex", "Scope", "Ring", "Reticle" };

        /// <summary>Procedural sight / optic part (hidden when the imported gun brings its own).</summary>
        static bool IsOptic(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
            {
                if (p.name.StartsWith("Sight")) return true;
                foreach (var k in Optics) if (p.name.Contains(k)) return true;
            }
            return false;
        }

        static bool Under(Transform t, Transform parent)
        {
            if (!parent) return false;
            for (var p = t; p; p = p.parent) if (p == parent) return true;
            return false;
        }

        public static void Apply(WeaponController w)
        {
            if (!w || !w.model || w.model.Find("ImportedModel")) return;
            var e = WeaponModelDB.Get(w.weaponName);
            if (e == null) return;
            var model = w.model;

            // ---- procedural parts to hide + their bounds (model space)
            var hide = new List<Renderer>();
            Bounds pb = default; bool havePb = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (Kept(r.transform, model) && !(e.ownSight && IsOptic(r.transform, model))) continue;
                bool isMag = Under(r.transform, w.magazine);
                if (isMag && e.keepMagazine) continue;
                hide.Add(r);
                if (isMag) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var b = Transform(mf.sharedMesh.bounds, model.worldToLocalMatrix * r.transform.localToWorldMatrix);
                if (!havePb) { pb = b; havePb = true; } else pb.Encapsulate(b);
            }
            if (!havePb) return;

            // ---- imported mesh
            var inst = UnityEngine.Object.Instantiate(e.prefab, model, false);
            inst.name = "ImportedModel";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = Vector3.one;
            foreach (var c in inst.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            // loose cartridges the artist left next to the gun
            for (int i = inst.transform.childCount - 1; i >= 0; i--)
            {
                var c = inst.transform.GetChild(i);
                string cn = c.name.ToLowerInvariant();
                // the pistol's under-barrel light glows (bloom) right around the front sight
                if (c.name.StartsWith("cal_") || cn.Contains("shell") || cn.Contains("pistolflashlight")) UnityEngine.Object.DestroyImmediate(c.gameObject);
            }
            Bounds ib = default; bool haveIb = false;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var b = Transform(mf.sharedMesh.bounds, model.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                if (!haveIb) { ib = b; haveIb = true; } else ib.Encapsulate(b);
            }
            if (!haveIb) { UnityEngine.Object.DestroyImmediate(inst); return; }

            var guard = model.Find("TriggerGuard");
            if (e.fixedScale > 0f && guard)
            {
                // real proportions: the imported trigger sits where the procedural hand pulls the trigger
                float fs = e.fixedScale * (e.scale > 0f ? e.scale : 1f);
                Vector3 procTrigger = model.InverseTransformPoint(guard.position) + new Vector3(0f, 0.012f, -0.004f);
                inst.transform.localScale = Vector3.one * fs;
                inst.transform.localPosition = procTrigger - e.triggerPoint * fs + e.offset;
                // muzzle flash / tracers start at the imported muzzle
                if (w.muzzle && w.muzzle.parent == model) w.muzzle.localPosition = inst.transform.localPosition + e.muzzlePoint * fs;
                // the support hand moves onto the imported foregrip / handguard
                if (w.slot == WeaponSlot.Primary && w.leftHand && w.leftHandGrip && w.leftHand.parent == model)
                {
                    Vector3 grip = model.InverseTransformPoint(w.leftHandGrip.position), want = grip;
                    if (e.hasForegrip) want = inst.transform.localPosition + e.foregripPoint * fs + new Vector3(0f, 0.02f, 0f);
                    else
                    {
                        float muzzleZ = inst.transform.localPosition.z + e.muzzlePoint.z * fs;
                        want.z = Mathf.Min(grip.z, muzzleZ - 0.16f);
                    }
                    w.leftHand.localPosition += want - grip;
                }
            }
            else
            {
                float s = pb.size.z / Mathf.Max(0.01f, ib.size.z) * (e.scale > 0f ? e.scale : 1f);
                inst.transform.localScale = Vector3.one * s;
                inst.transform.localPosition = pb.center - ib.center * s + e.offset;
            }

            // ADS lines up on the imported sights: WeaponController.Init computes the aim pose from sightPoint
            if (e.ownSight && w.sightPoint)
            {
                Vector3 sp = model.InverseTransformPoint(inst.transform.TransformPoint(e.sightPoint));
                if (e.sightKeepDepth) sp.z = model.InverseTransformPoint(w.sightPoint.position).z;
                // keep the imported sight's own X: the fitted mesh may sit off the procedural centerline,
                // and zeroing X here put red dots / scopes to the side of screen center
                w.sightPoint.position = model.TransformPoint(sp);
                // the imported red-dot / scope reticle becomes the collimated reticle (it tracks the shot direction)
                Transform ret = null;
                foreach (var t in inst.GetComponentsInChildren<Transform>(true))
                    if (t.name.ToLowerInvariant().Contains("reticle")) { ret = t; break; }
                if (ret || (w.reticle && IsOptic(w.reticle, model))) w.reticle = ret;
                // with an optic on top the iron front post only fills the eyepiece (M16A4: the dot sat on the post)
                if (ret)
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                        if (r.name.ToLowerInvariant().Contains("frontsight")) r.forceRenderingOff = true;
            }

            int layer = hide.Count > 0 ? hide[0].gameObject.layer : model.gameObject.layer;
            foreach (var t in inst.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // ---- moving parts follow the procedural animation anchors
            foreach (var t in new List<Transform>(inst.GetComponentsInChildren<Transform>(true)))
            {
                if (t == inst.transform) continue;
                string n = t.name.ToLowerInvariant();
                Transform target = null;
                if (n.Contains("magazine") && !n.Contains("release")) target = e.keepMagazine ? null : w.magazine;
                else if (n.Contains("bolt_handle")) target = w.chargingHandle ? w.chargingHandle : w.bolt; // bolt-action handle rides the cycling anchor
                else if (n.Contains("slide") || n.EndsWith("_bolt")) target = w.bolt ? w.bolt : w.chargingHandle;
                else if (n.Contains("forend") || n.Contains("pump")) target = w.chargingHandle;
                if (target) t.SetParent(target, true);
            }

            foreach (var r in hide)
            {
                if (!r.GetComponent<ImportHidden>()) r.gameObject.AddComponent<ImportHidden>();
                r.forceRenderingOff = true;
            }
        }

        static Bounds Transform(Bounds b, Matrix4x4 m)
        {
            var c = m.MultiplyPoint3x4(b.center);
            var ex = b.extents;
            var ax = m.MultiplyVector(new Vector3(ex.x, 0, 0)); var ay = m.MultiplyVector(new Vector3(0, ex.y, 0)); var az = m.MultiplyVector(new Vector3(0, 0, ex.z));
            var e = new Vector3(Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x), Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y), Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));
            return new Bounds(c, e * 2f);
        }
    }
}
