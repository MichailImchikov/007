using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FPS.EditorTools
{
    /// <summary>
    /// Edit-mode check of the modular weapons (Tools > FPS > Weapons > Check Weapon Assembly): in a preview scene every
    /// modular weapon is built through the runtime path (WeaponAssembler.CreateRuntime + WeaponController.Init), then
    /// re-assembled with every compatible attachment and a few combinations; it verifies the WeaponController wiring
    /// (non-null, at the right sockets), the sight line on the optic's axis, the ADS aim position, stat modifiers,
    /// URP materials and the world-model template. The open scene's procedural M4A1 is converted as well.
    /// Optional: renders side / ADS views to PNG files.
    /// </summary>
    public static class WeaponAssemblyCheck
    {
        [MenuItem("Tools/FPS/Weapons/Check Weapon Assembly")]
        static void Menu() => Debug.Log(Run(null));

        static readonly FieldInfo AimField = typeof(WeaponController).GetField("aimPosition", BindingFlags.NonPublic | BindingFlags.Instance);

        class Ctx
        {
            public StringBuilder log = new StringBuilder();
            public int checks, fails;
            public void Fail(string what) { fails++; log.AppendLine("  FAIL " + what); }
            public void Ok(bool cond, string what) { checks++; if (!cond) Fail(what); }
        }

        public static string Run(string imageDir)
        {
            WeaponDataDB.Clear();
            var ctx = new Ctx();
            var active = SceneManager.GetActiveScene();
            bool wasDirty = active.isDirty;
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                // ---- fake first-person rig: camera + weapons root
                var root = new GameObject("CheckRig");
                SceneManager.MoveGameObjectToScene(root, scene);
                var camGo = new GameObject("Main Camera");
                camGo.transform.SetParent(root.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.nearClipPlane = 0.01f;
                var weaponsRoot = new GameObject("Weapons").transform;
                weaponsRoot.SetParent(camGo.transform, false);

                // ---- the open scene's procedural M4A1 (a copy) is converted to the modular one
                WeaponController src = null;
                var lo = Object.FindAnyObjectByType<Loadout>(FindObjectsInactive.Include);
                var sceneM4 = lo ? lo.Find("M4A1") : null;
                if (sceneM4)
                {
                    var copy = Object.Instantiate(sceneM4.gameObject, weaponsRoot, false);
                    copy.name = "W_M4A1 (scene copy)";
                    var w = copy.GetComponent<WeaponController>();
                    w.cam = cam; w.motor = null; w.playerCam = null;
                    var procModel = w.model;
                    w.EnsureInit();
                    ctx.log.AppendLine("== scene M4A1 (procedural → modular)");
                    ctx.Ok(w.GetComponent<WeaponAttachments>() && w.GetComponent<WeaponAttachments>().Assembled, "scene M4A1 assembled");
                    ctx.Ok(!procModel, "procedural model destroyed");
                    ctx.Ok(!WeaponAssembler.FindDeep(w.transform, "Receiver") && !WeaponAssembler.FindDeep(w.transform, "LeftGlove"), "no procedural parts left");
                    ctx.Ok(w.muzzleFlash && w.muzzleFlash.transform.IsChildOf(w.muzzle), "procedural flash moved to the new muzzle");
                    CheckWeapon(ctx, w, "scene copy defaults");
                    src = w;
                }
                else ctx.log.AppendLine("(no Loadout / M4A1 in the open scene: runtime weapons get the fallback flash)");

                // ---- every modular catalog weapon through the runtime path
                var built = new List<WeaponController>();
                foreach (var def in WeaponCatalog.All)
                {
                    var data = WeaponDataDB.Get(def.name);
                    if (!data) continue;
                    var w = WeaponAssembler.CreateRuntime(def, weaponsRoot, src, null, null, cam);
                    ctx.Ok(w, def.name + " created");
                    if (!w) continue;
                    w.EnsureInit();
                    built.Add(w);
                    ctx.log.AppendLine($"== {def.name}");
                    var wa = w.GetComponent<WeaponAttachments>();
                    var defaults = DefaultSelection(data);
                    wa.Rebuild(defaults);
                    CheckWeapon(ctx, w, "defaults");
                    if (imageDir != null) RenderViews(w, scene, imageDir, def.name.Replace(" ", "") + "_default");

                    // every compatible attachment on its own (others at default)
                    for (int i = 0; i < data.slots.Length; i++)
                    {
                        var options = new List<AttachmentData>(data.slots[i].allowed);
                        if (!data.slots[i].required) options.Add(null);
                        foreach (var a in options)
                        {
                            var sel = (AttachmentData[])defaults.Clone();
                            sel[i] = a;
                            wa.Rebuild(sel);
                            CheckWeapon(ctx, w, data.slots[i].slot + "=" + (a ? a.id : "none"));
                        }
                    }
                    // all empty, then the last option of every slot
                    var none = new AttachmentData[data.slots.Length];
                    wa.Rebuild(none);
                    CheckWeapon(ctx, w, "bare");
                    var max = new AttachmentData[data.slots.Length];
                    for (int i = 0; i < max.Length; i++) max[i] = data.slots[i].allowed.Length > 0 ? data.slots[i].allowed[data.slots[i].allowed.Length - 1] : null;
                    wa.Rebuild(max);
                    CheckWeapon(ctx, w, "last of each slot");
                    if (imageDir != null) RenderViews(w, scene, imageDir, def.name.Replace(" ", "") + "_max");
                    // the base stats come back (no stacking)
                    wa.Rebuild(defaults);
                    var d2 = WeaponCatalog.Get(def.name);
                    var probe = new GameObject("StatProbe").AddComponent<WeaponController>();
                    SceneManager.MoveGameObjectToScene(probe.gameObject, scene);
                    d2.Apply(probe);
                    ExpectStats(ctx, w, probe, defaults);
                    Object.DestroyImmediate(probe.gameObject);
                    wa.Rebuild(defaults);
                }
                ctx.Ok(built.Count >= 5, "modular weapons built (got " + built.Count + ")");
            }
            catch (System.Exception e)
            {
                ctx.Fail("exception " + e);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
                WeaponDataDB.Clear();
            }
            ctx.log.AppendLine($"== {ctx.checks} checks, {ctx.fails} failed; open scene dirty before/after: {wasDirty}/{SceneManager.GetActiveScene().isDirty}");
            return ctx.log.ToString();
        }

        static AttachmentData[] DefaultSelection(WeaponData d)
        {
            var sel = new AttachmentData[d.slots.Length];
            for (int i = 0; i < sel.Length; i++) sel[i] = d.slots[i].defaultAttachment;
            return sel;
        }

        static void CheckWeapon(Ctx ctx, WeaponController w, string label)
        {
            int before = ctx.fails;
            string n = w.weaponName + " [" + label + "]: ";
            var wa = w.GetComponent<WeaponAttachments>();
            var model = w.model;
            ctx.Ok(model && wa && wa.modelRoot == model, n + "model is the assembled FBX");
            if (!model || !wa) return;
            var data = wa.data;
            // a gun without a cycling part (SVD / VSS / AXMC model, revolver, launcher) has no charging handle: the reload
            // skips the rack and the hand goes to a default place
            bool hasCycling = !string.IsNullOrEmpty(data.boltPart) && WeaponAssembler.FindDeep(model, data.boltPart);
            string missing = (w.muzzle ? "" : " muzzle") + (w.ejectPort ? "" : " eject") + (w.sightPoint ? "" : " sight") + (w.magazine ? "" : " mag") +
                             (w.chargingHandle || !hasCycling ? "" : " charging-handle") + (w.leftHand ? "" : " left-hand") +
                             (w.leftHandGrip ? "" : " grip") + (w.muzzleFlash ? "" : " flash");
            ctx.Ok(missing.Length == 0, n + "required fields non-null" + (missing.Length > 0 ? " (missing:" + missing + ")" : ""));
            if (!(w.muzzle && w.ejectPort && w.sightPoint && w.magazine && w.leftHand && w.leftHandGrip)) return;

            bool wantBolt = hasCycling && (data.slideCycles || data.boltReciprocates);
            ctx.Ok(!wantBolt || w.bolt, n + "bolt (cycling part) wired");
            ctx.Ok((!w.chargingHandle || w.chargingHandle.IsChildOf(model)) && w.magazine.IsChildOf(model) && w.leftHand.IsChildOf(model), n + "parts under the model");
            ctx.Ok(w.leftHand.parent == model, n + "leftHand anchor directly under the model");
            ctx.Ok(HandGrips.Find(model, HandGrips.Right) && HandGrips.Find(model, HandGrips.Left), n + "GripR / GripL anchors");
            var gl = HandGrips.Find(model, HandGrips.Left);
            ctx.Ok(gl && gl.IsChildOf(w.leftHand), n + "GripL under the leftHand anchor (reload moves it)");
            ctx.Ok(WeaponAssembler.FindDeep(model, "Grip_R") && WeaponAssembler.FindDeep(model, "Grip_L"), n + "Grip_R / Grip_L sockets kept");
            ctx.Ok(Near(w.ejectPort.position, WeaponAssembler.FindDeep(model, "Eject").position), n + "ejectPort = Eject");

            // muzzle at the device's Socket_Muzzle, else the barrel's
            var dev = wa.MountedInstance(AttachmentSlot.Muzzle);
            var ms = dev ? WeaponAssembler.FindDeep(dev.transform, "Socket_Muzzle") : model.Find("Socket_Muzzle");
            ctx.Ok(ms && Near(w.muzzle.position, ms.position) && Vector3.Dot(w.muzzle.forward, model.forward) > 0.999f, n + "muzzle at the muzzle socket, pointing forward");
            if (dev)
            {
                float len = Vector3.Dot(w.muzzle.position - model.Find("Socket_Muzzle").position, model.forward);
                // the device length is in the model's units: a scaled-up model scales its devices too
                float want = wa.Mounted(AttachmentSlot.Muzzle).muzzleLength * model.lossyScale.z;
                ctx.Ok(Mathf.Abs(len - want) < 0.002f, n + $"muzzle moved by the device length ({len:F3})");
            }
            ctx.Ok(w.muzzle.name == "Muzzle" && w.muzzleFlash.transform.IsChildOf(w.muzzle), n + "flash under 'Muzzle' (bots find it by name)");

            // sight: optic Sight_Point (or a resting proxy for a slide optic), iron sights hidden
            var opticData = wa.Mounted(AttachmentSlot.Optic);
            var optic = wa.MountedInstance(AttachmentSlot.Optic);
            var irons = model.Find("IronSights");
            if (optic)
            {
                var sp = WeaponAssembler.FindDeep(optic.transform, "Sight_Point");
                ctx.Ok(sp && Near(w.sightPoint.position, sp.position), n + "sightPoint = optic Sight_Point");
                ctx.Ok(!irons || !irons.gameObject.activeSelf, n + "iron sights hidden under the optic");
                // the sight line runs along the optic axis: the reticle (or the lens center) sits on it
                Vector3 axisPoint;
                if (w.reticle)
                {
                    var mf = w.reticle.GetComponent<MeshFilter>();
                    axisPoint = w.reticle.TransformPoint(mf && mf.sharedMesh ? mf.sharedMesh.bounds.center : Vector3.zero);
                }
                else
                {
                    var lens = WeaponAssembler.FindDeep(optic.transform, "Lens");
                    var lmf = lens ? lens.GetComponent<MeshFilter>() : null;
                    axisPoint = lmf && lmf.sharedMesh ? lens.TransformPoint(lmf.sharedMesh.bounds.center) : w.sightPoint.position + model.forward;
                }
                // (a reflex sight's LED may sit a little behind its Sight_Point: only the distance from the axis counts)
                Vector3 rel = axisPoint - w.sightPoint.position;
                float off = Vector3.ProjectOnPlane(rel, model.forward).magnitude;
                ctx.Ok(off < 0.001f, n + $"sight point on the optic axis (off {off * 1000f:F2} mm)");
                // in play the rear lens draws the reticle / magnified picture (OpticSight); edit-time previews use a mesh
                var rearLens = WeaponAssembler.FindDeep(optic.transform, "Lens_Rear");
                if (!rearLens) rearLens = WeaponAssembler.FindDeep(optic.transform, "Lens");
                bool lensDraws = rearLens && rearLens.GetComponent<Renderer>();
                bool needsReticle = !opticData.UsesScopeOverlay;
                ctx.Ok(!needsReticle || w.reticle || lensDraws, n + "reticle wired (mesh, or drawn on the rear lens in play)");
                ctx.Ok(w.scoped == opticData.UsesScopeOverlay, n + "scoped follows the optic");
                if (opticData.adsFov > 0f) ctx.Ok(Mathf.Approximately(w.adsFov, opticData.adsFov), n + "optic ADS fov");
                var slot = data.GetSlot(AttachmentSlot.Optic);
                if (!string.IsNullOrEmpty(slot.parentPart))
                    ctx.Ok(optic.transform.IsChildOf(WeaponAssembler.FindDeep(model, slot.parentPart)), n + "optic rides on " + slot.parentPart);
            }
            else
            {
                var si = model.Find("Sight_Iron");
                ctx.Ok(si && w.sightPoint == si, n + "sightPoint = Sight_Iron");
                ctx.Ok(!irons || irons.gameObject.activeSelf, n + "iron sights visible");
                ctx.Ok(!w.reticle, n + "no reticle with iron sights");
            }

            // ADS: with the holder at the aim position the sight point sits on the camera axis at aimDistance
            if (AimField != null && w.transform.parent)
            {
                w.RecomputeAimPosition();
                var aim = (Vector3)AimField.GetValue(w);
                var lp = w.transform.localPosition; var lr = w.transform.localRotation;
                w.transform.localPosition = aim; w.transform.localRotation = Quaternion.identity;
                Vector3 s = w.transform.parent.InverseTransformPoint(w.sightPoint.position);
                w.transform.localPosition = lp; w.transform.localRotation = lr;
                ctx.Ok(Mathf.Abs(s.x) < 1e-4f && Mathf.Abs(s.y) < 1e-4f && Mathf.Abs(s.z - w.aimDistance) < 1e-4f, n + $"ADS aim puts the sight on the eye line ({s.x:F4},{s.y:F4},{s.z:F3})");
            }

            // magazine
            var ext = wa.MountedInstance(AttachmentSlot.Magazine);
            var mag = model.Find("Mag");
            if (w.reloadStyle == ReloadStyle.PerShell)
                ctx.Ok(w.magazine.name == "ShellInHand" && w.loadPort, n + "single-round loading: cartridge in hand + load port");
            else if (ext)
            {
                ctx.Ok(w.magazine == ext.transform && mag && !mag.gameObject.activeSelf, n + "extended magazine replaces Mag");
                ctx.Ok(w.magSize == wa.Mounted(AttachmentSlot.Magazine).magCapacity, n + "magazine capacity " + w.magSize);
            }
            else ctx.Ok(w.magazine == mag && mag.gameObject.activeSelf, n + "magazine = Mag");

            // support hand
            var ub = wa.MountedInstance(AttachmentSlot.Underbarrel);
            var ubData = wa.Mounted(AttachmentSlot.Underbarrel);
            if (ub && ubData.foregrip)
            {
                var fg = WeaponAssembler.FindDeep(ub.transform, "Grip_L");
                ctx.Ok(w.leftHandGrip == fg, n + "support hand on the foregrip's Grip_L");
                ctx.Ok(ub.name.StartsWith("Grip_Vertical") || ub.name.StartsWith("Grip_Angled"), n + "foregrip root name kept for HandGrips");
            }
            else
            {
                var gripL = model.Find("Grip_L");
                ctx.Ok(gripL && w.leftHandGrip.IsChildOf(w.leftHand) && Near(w.leftHandGrip.position, gripL.position), n + "support hand grip at Grip_L under leftHand");
            }

            // suppressor: no muzzle light
            var md = wa.Mounted(AttachmentSlot.Muzzle);
            if (md && md.suppressed) ctx.Ok(!w.muzzleLight, n + "suppressed: no muzzle light");
            else ctx.Ok(w.muzzleLight, n + "muzzle light");

            // URP materials, nothing pink
            string badMat = null;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    bool good = m && m.shader && m.shader.isSupported && m.shader.name.StartsWith("Universal Render Pipeline/");
                    if (!good && badMat == null) badMat = r.name + ": " + (m ? m.name + " / " + (m.shader ? m.shader.name : "no shader") : "null");
                }
            ctx.Ok(badMat == null, n + "URP materials (" + badMat + ")");

            // world model (third person / pickups)
            var holder = new GameObject("WorldCheck").transform;
            SceneManager.MoveGameObjectToScene(holder.gameObject, w.gameObject.scene);
            var world = Loadout.MakeWorldModel(w, holder);
            ctx.Ok(world.transform.Find(HandGrips.Right) && world.transform.Find(HandGrips.Left), n + "world model: GripR / GripL direct children");
            ctx.Ok(WeaponAssembler.FindDeep(world.transform, "Muzzle"), n + "world model: 'Muzzle'");
            ctx.Ok(world.GetComponentsInChildren<Light>(true).Length == 0 && !WeaponAssembler.FindDeep(world.transform, "MuzzleFlash"), n + "world model: no lights / flash");
            int atts = 0;
            foreach (var a in wa.GetComponentsInChildren<AttachmentInstance>(true)) atts++;
            ctx.Ok(world.GetComponentsInChildren<AttachmentInstance>(true).Length == atts, n + "world model keeps the attachments");
            Object.DestroyImmediate(holder.gameObject);

            if (ctx.fails == before) ctx.log.AppendLine("  ok " + label);
        }

        static void ExpectStats(Ctx ctx, WeaponController w, WeaponController baseW, AttachmentData[] sel)
        {
            var wa = w.GetComponent<WeaponAttachments>();
            float recoil = baseW.recoilUp, aim = baseW.aimTime, hip = baseW.hipSpread;
            foreach (var a in sel) if (a) { recoil *= a.recoilMul; aim *= a.adsTimeMul; hip *= a.hipSpreadMul; }
            ctx.Ok(Mathf.Abs(w.recoilUp - recoil) < 1e-4f && Mathf.Abs(w.aimTime - aim) < 1e-4f && Mathf.Abs(w.hipSpread - hip) < 1e-4f,
                   $"{w.weaponName}: stats re-applied from the base after many rebuilds (recoil {w.recoilUp:F3} vs {recoil:F3}, ads {w.aimTime:F3} vs {aim:F3})");
            ctx.log.AppendLine($"  stats {w.weaponName}: dmg {w.damage} rpm {w.rpm} mag {w.magSize} recoil {w.recoilUp:F2}/{w.recoilSide:F2} hip {w.hipSpread:F2} ads {w.adsSpread:F3} aim {w.aimTime:F2}s fov {w.adsFov} scoped {w.scoped} reload {w.reloadTime:F2}/{w.emptyReloadTime:F2} [{wa.Signature}]");
        }

        static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-8f;

        /// <summary>
        /// The real player of the open scene (a copy in a preview scene): Loadout creates the missing modular weapons at
        /// runtime (Awake path), builds the world-model templates (Start path) and world models for bots / pickups.
        /// </summary>
        public static string CheckLoadout()
        {
            var log = new StringBuilder();
            var lo = Object.FindAnyObjectByType<Loadout>(FindObjectsInactive.Include);
            if (!lo) return "no Loadout in the open scene";
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var copy = Object.Instantiate(lo.gameObject);
                SceneManager.MoveGameObjectToScene(copy, scene);
                var l = copy.GetComponent<Loadout>();
                int before = l.weapons.Length;
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                typeof(Loadout).GetMethod("EnsureRuntimeWeapons", flags).Invoke(l, null);
                typeof(Loadout).GetMethod("EnsureTemplates", flags).Invoke(l, null);
                log.AppendLine($"weapons {before} → {l.weapons.Length}");
                foreach (var name in new[] { "M4A1", "AK-12", "M14 EBR", "M700", "Glock 17" })
                {
                    var w = l.Find(name);
                    var wa = w ? w.GetComponent<WeaponAttachments>() : null;
                    bool ok = w && wa && wa.Assembled && w.IsInitialized && w.transform.parent == l.Find("M4A1").transform.parent
                              && w.muzzleFlash && w.muzzleFlash.GetComponentsInChildren<Renderer>(true).Length > 0 && w.motor == l.motor && w.cam == l.cam;
                    var holder = new GameObject("World_" + name);
                    SceneManager.MoveGameObjectToScene(holder, scene);
                    var world = l.CreateWorldModel(name, holder.transform);
                    bool worldOk = world && world.transform.Find(HandGrips.Right) && world.transform.Find(HandGrips.Left) && WeaponAssembler.FindDeep(world.transform, "Muzzle");
                    log.AppendLine($"  {name}: {(ok ? "ok" : "FAIL")} runtime weapon (slot {(w ? w.slot.ToString() : "-")}, active {(w ? w.gameObject.activeSelf.ToString() : "-")}), world model {(worldOk ? "ok" : "FAIL")} [{(wa ? wa.Signature : "")}]");
                }
            }
            catch (System.Exception e) { log.AppendLine("exception " + e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            return log.ToString();
        }

        /// <summary>Builds the loadout window and the attachments window of a weapon off-screen and saves PNGs (layout check).</summary>
        public static string RenderMenus(string dir, string weaponName)
        {
            Directory.CreateDirectory(dir);
            var scene = EditorSceneManager.NewPreviewScene();
            var log = new StringBuilder();
            try
            {
                var camGo = new GameObject("UICam");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.22f, 0.25f);
                var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                for (int pass = 0; pass < 2; pass++)
                {
                    var canvasGo = new GameObject("Canvas");
                    SceneManager.MoveGameObjectToScene(canvasGo, scene);
                    var canvas = UIKit.MakeCanvas(canvasGo, 10);
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                    var rtCanvas = (RectTransform)canvasGo.transform;
                    rtCanvas.sizeDelta = new Vector2(1920f, 1080f);
                    if (pass == 0) LoadoutPanel.Open(canvasGo.transform, false);
                    else AttachmentsPanel.Open(canvasGo.transform, weaponName, true);
                    Canvas.ForceUpdateCanvases();
                    cam.Render();
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = prev;
                    string path = Path.Combine(dir, pass == 0 ? "menu_loadout.png" : "menu_attachments.png");
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    Object.DestroyImmediate(tex);
                    log.AppendLine("saved " + path);
                    Object.DestroyImmediate(canvasGo);
                }
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
            }
            catch (System.Exception e) { log.AppendLine("exception " + e); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
            return log.ToString();
        }

        // ------------------------------------------------------------------ pictures

        static void RenderViews(WeaponController w, Scene scene, string dir, string name)
        {
            Directory.CreateDirectory(dir);
            var go = new GameObject("ShotCam");
            SceneManager.MoveGameObjectToScene(go, scene);
            var lightGo = new GameObject("ShotLight");
            SceneManager.MoveGameObjectToScene(lightGo, scene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.6f;
            lightGo.transform.rotation = Quaternion.Euler(35f, -60f, 0f);
            var fill = new GameObject("Fill");
            SceneManager.MoveGameObjectToScene(fill, scene);
            var fl = fill.AddComponent<Light>();
            fl.type = LightType.Directional; fl.intensity = 0.7f; fill.transform.rotation = Quaternion.Euler(-20f, 120f, 0f);
            var head = new GameObject("HeadLight");
            SceneManager.MoveGameObjectToScene(head, scene);
            var hl = head.AddComponent<Light>();
            hl.type = LightType.Directional; hl.intensity = 0.9f;
            var cam = go.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.35f, 0.37f, 0.4f);
            cam.nearClipPlane = 0.005f;
            var rt = new RenderTexture(1280, 640, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            // only this weapon is drawn
            var others = new List<GameObject>();
            foreach (Transform sib in w.transform.parent)
                if (sib != w.transform && sib.gameObject.activeSelf) { others.Add(sib.gameObject); sib.gameObject.SetActive(false); }
            bool wasActive = w.gameObject.activeSelf;
            w.gameObject.SetActive(true);
            try
            {
                // side view of the model, from the right
                var b = new Bounds(w.model.position, Vector3.zero);
                foreach (var r in w.model.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
                cam.fieldOfView = 30f;
                float dist = b.extents.magnitude / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.05f;
                go.transform.position = b.center + w.model.right * dist;
                go.transform.rotation = Quaternion.LookRotation(-w.model.right, w.model.up);
                head.transform.rotation = go.transform.rotation * Quaternion.Euler(20f, -25f, 0f);
                Save(cam, rt, Path.Combine(dir, name + "_side.png"));

                // ADS view: holder at the aim position, eye at the parent origin
                var aim = AimField != null ? (Vector3)AimField.GetValue(w) : w.hipPosition;
                var lp = w.transform.localPosition;
                w.transform.localPosition = aim;
                var parent = w.transform.parent;
                go.transform.SetPositionAndRotation(parent.position, parent.rotation);
                head.transform.rotation = go.transform.rotation * Quaternion.Euler(25f, 20f, 0f);
                cam.fieldOfView = Mathf.Max(w.scoped ? 30f : w.adsFov, 20f);
                Save(cam, rt, Path.Combine(dir, name + "_ads.png"));
                w.transform.localPosition = lp;
            }
            finally
            {
                w.gameObject.SetActive(wasActive);
                foreach (var o in others) if (o) o.SetActive(true);
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(fill);
                Object.DestroyImmediate(head);
            }
        }

        static void Save(Camera cam, RenderTexture rt, string path)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
