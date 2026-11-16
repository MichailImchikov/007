using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPS
{
    /// <summary>
    /// The eyepiece of a mounted optic: its rear lens draws the sight (FPS/OpticLens shader).
    ///  - 1x sights (red dot, holographic, pistol dot): clear glass with a collimated reticle at infinity along the optic's
    ///    axis. It marks where the barrel points from wherever the eye is, and shows only through the glass.
    ///  - magnified optics (prism, scope): picture in picture. One shared camera renders the view along the optic's axis
    ///    while the local player aims through it; the lens shows it magnified with the reticle, the field stop and the
    ///    eyebox shadow. The main view only zooms like a 1x sight; the look slows down by the magnification
    ///    (PlayerCamera.ScopeZoom), so the picture in the eyepiece sweeps as fast as the view does from the hip. The eye
    ///    sits close enough for the eyepiece to fill a good part of the view (EyeReliefFor).
    /// Set up by WeaponAttachments on the optic it mounts; bots' and world models' optics stay plain glass with a reticle.
    /// </summary>
    [DefaultExecutionOrder(300)] // after SkinnedBody (260) has placed the weapon and the eye for this frame
    public class OpticSight : MonoBehaviour
    {
        public enum Style { Dot, Holo, Chevron, Duplex }

        public WeaponController weapon;
        public Renderer lens;
        [Min(1f)] public float magnification = 1f;
        public Style style;
        [Tooltip("Size of a dot reticle (minutes of angle)")] public float dotMoa = 2f;
        [Tooltip("Lit reticle (HDR: a little bloom, not a blur)")]
        [ColorUsage(true, true)] public Color reticleColor = new Color(2f, 0.06f, 0.04f, 1f);

        const int PipSize = 1024;
        const float PipNear = 1f; // the gun, the hands and the optic itself are closer than this: never in the picture

        Material mat;
        float lensRadius;

        static Camera pip;
        static RenderTexture pipRT;
        static OpticSight pipOwner;

        static readonly int pTint = Shader.PropertyToID("_Tint"), pStyle = Shader.PropertyToID("_Style"),
            pDot = Shader.PropertyToID("_DotMoa"), pMag = Shader.PropertyToID("_Mag"), pColor = Shader.PropertyToID("_ReticleColor"),
            pPip = Shader.PropertyToID("_PipTex"), pPipOn = Shader.PropertyToID("_PipOn"), pPipTan = Shader.PropertyToID("_PipTanHalf"),
            pApparent = Shader.PropertyToID("_ApparentTan"), pAim = Shader.PropertyToID("_Aim"), pCenter = Shader.PropertyToID("_LensCenter"),
            pRadius = Shader.PropertyToID("_LensRadius"), pAxis = Shader.PropertyToID("_Axis"), pUp = Shader.PropertyToID("_AxisUp"),
            pRight = Shader.PropertyToID("_AxisRight"), pStencilPass = Shader.PropertyToID("_StencilPass");

        public bool Magnified => magnification > 1.01f;
        /// <summary>Radius of the rear lens (m).</summary>
        public float LensRadius => lensRadius;

        /// <summary>
        /// The eyepiece zooms less than the optic's nominal power (its name, the menu): the zoom above 1× is scaled by
        /// this, so 4× magnifies 2.5×, 6× 3.5×, 8× 4.5× (on top of the aimed view's own zoom, AdsFov).
        /// </summary>
        public const float ZoomScale = 0.5f;
        public static float EffectiveMagnification(float nominal) => 1f + (Mathf.Max(1f, nominal) - 1f) * ZoomScale;

        /// <summary>The eyepiece camera while the local player looks through a magnified optic (else null): overlays
        /// that must show in the scope's picture (recon) draw for it too.</summary>
        public static Camera ActivePip => pip && pip.enabled && pipOwner ? pip : null;

        /// <summary>
        /// Eye distance behind the sight point (along the weapon's axis) at which the rear lens is seen at the given
        /// apparent half-angle: an eyepiece that fills a good part of the view instead of a hole at arm's length.
        /// </summary>
        public float EyeReliefFor(Transform sightPoint, float halfAngleDeg)
        {
            if (!lens || !sightPoint || lensRadius <= 0f) return 0f;
            Vector3 axis = weapon ? weapon.transform.forward : transform.forward;
            float lensBehindSight = Vector3.Dot(sightPoint.position - lens.bounds.center, axis);
            return lensRadius / Mathf.Tan(halfAngleDeg * Mathf.Deg2Rad) + lensBehindSight;
        }

        /// <summary>Draws the sight in the optic's rear lens: style and size from the optic's data.</summary>
        public void Setup(WeaponController w, Renderer rearLens, AttachmentData data)
        {
            weapon = w;
            lens = rearLens;
            float nominal = data ? Mathf.Max(1f, data.magnification) : 1f;
            magnification = EffectiveMagnification(nominal);
            string n = data ? data.name : "";
            style = nominal >= 6f ? Style.Duplex : nominal > 1.01f ? Style.Chevron
                  : n.Contains("Holo") ? Style.Holo : Style.Dot;
            dotMoa = n.Contains("Pistol") ? 3.5f : 2f;

            var template = Resources.Load<Material>("OpticLens");
            if (!template) { Debug.LogWarning("[OpticSight] Resources/OpticLens material missing"); return; }
            if (!mat) mat = new Material(template) { name = "OpticLens_" + n };
            mat.SetFloat(pStyle, (int)style);
            mat.SetFloat(pDot, dotMoa);
            mat.SetFloat(pMag, magnification);
            mat.SetColor(pColor, reticleColor);
            mat.SetFloat(pPipOn, 0f);
            // a magnified eyepiece marks its pixels in the stencil: the main view's recon x-ray (unmagnified, in the wrong
            // place for the scope's picture) is not drawn over it, the eyepiece camera draws its own (ReconVision)
            mat.SetFloat(pStencilPass, Magnified ? (float)StencilOp.Replace : (float)StencilOp.Keep);
            lens.sharedMaterial = mat;
            lens.shadowCastingMode = ShadowCastingMode.Off;
            lens.receiveShadows = false;

            var mf = lens.GetComponent<MeshFilter>();
            Vector3 ext = mf && mf.sharedMesh ? mf.sharedMesh.bounds.extents : Vector3.one * 0.01f;
            Vector3 s = lens.transform.lossyScale;
            lensRadius = Mathf.Max(0.002f, Mathf.Max(Mathf.Abs(ext.x * s.x), Mathf.Abs(ext.y * s.y)));
        }

        void LateUpdate()
        {
            if (!mat || !lens || !weapon) return;
            var cam = Camera.main;
            if (!cam) return;
            Transform wt = weapon.transform;
            Vector3 axis = wt.forward, up = wt.up, right = wt.right;
            Vector3 center = lens.bounds.center;
            mat.SetVector(pCenter, center);
            mat.SetFloat(pRadius, lensRadius);
            mat.SetVector(pAxis, axis);
            mat.SetVector(pUp, up);
            mat.SetVector(pRight, right);

            // the local player's first-person weapon (bots' and world models' optics are only glass with a reticle)
            bool mine = weapon.AnimDriven && weapon.isActiveAndEnabled;
            float aim = mine ? weapon.AimBlend : 0f;
            mat.SetFloat(pAim, aim);
            if (!Magnified) return;

            float dist = Mathf.Max(0.02f, Vector3.Dot(center - cam.transform.position, axis));
            float apparent = lensRadius / dist; // the eyepiece's apparent radius seen from the eye (tan)
            mat.SetFloat(pApparent, apparent);
            bool render = mine && aim > 0.02f;
            if (render) RenderPip(cam, axis, up, apparent / magnification * 1.1f);
            else if (pipOwner == this) StopPip();
            mat.SetFloat(pPipOn, render && pipOwner == this ? 1f : 0f);
            if (mine && weapon.playerCam) weapon.playerCam.ScopeZoom = Mathf.Lerp(1f, magnification, aim);
        }

        void RenderPip(Camera main, Vector3 axis, Vector3 up, float tanHalf)
        {
            if (!pip)
            {
                var go = new GameObject("OpticPipCamera") { hideFlags = HideFlags.DontSave };
                pip = go.AddComponent<Camera>();
                pip.enabled = false;
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false; // the main camera's tonemapping applies once, through the lens
                data.renderShadows = true;
                pipRT = new RenderTexture(PipSize, PipSize, 24, main.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default)
                { name = "OpticPip", antiAliasing = 2 };
                pip.targetTexture = pipRT;
            }
            pipOwner = this;
            pip.transform.SetPositionAndRotation(main.transform.position, Quaternion.LookRotation(axis, up));
            pip.fieldOfView = 2f * Mathf.Atan(tanHalf) * Mathf.Rad2Deg;
            pip.aspect = 1f;
            pip.nearClipPlane = PipNear;
            pip.farClipPlane = main.farClipPlane;
            pip.cullingMask = main.cullingMask;
            pip.clearFlags = main.clearFlags;
            pip.backgroundColor = main.backgroundColor;
            pip.depth = main.depth - 1f;
            pip.enabled = true;
            mat.SetTexture(pPip, pipRT);
            mat.SetFloat(pPipTan, tanHalf);
        }

        static void StopPip()
        {
            if (pip) pip.enabled = false;
            pipOwner = null;
        }

        void OnDisable()
        {
            if (pipOwner == this) StopPip();
            if (weapon && weapon.playerCam && weapon.AnimDriven && Magnified) weapon.playerCam.ScopeZoom = 1f;
        }

        void OnDestroy()
        {
            if (mat) Destroy(mat);
        }
    }
}
