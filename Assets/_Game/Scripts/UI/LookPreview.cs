using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FPS
{
    /// <summary>
    /// Studio for the menu: the local soldier (built from <see cref="CharacterLook.Local"/>) far below the
    /// menu, lit by its own lights and rendered by its own camera into a texture shown in RawImages.
    /// Drag a preview image to turn the soldier.
    /// </summary>
    public class LookPreview : MonoBehaviour
    {
        public static LookPreview Instance { get; private set; }

        public RenderTexture Texture { get; private set; }
        public HumanoidRig Rig { get; private set; }
        public float yaw = 200f;
        float idleT, lastDrag = -10f;
        Transform turntable;
        Camera cam;
        CharacterAnimator anim;

        static readonly Vector3 Origin = new Vector3(0f, -800f, 0f);

        public static LookPreview Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("LookPreview");
            Instance = go.AddComponent<LookPreview>();
            Instance.Build();
            return Instance;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Texture) { Texture.Release(); Destroy(Texture); }
        }

        void Build()
        {
            transform.position = Origin;
            Texture = new RenderTexture(900, 1200, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "LookPreviewRT" };
            Texture.Create();

            turntable = new GameObject("Turntable").transform;
            turntable.SetParent(transform, false);
            Rebuild();

            // floor disc + soft back wall
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(transform, false);
            floor.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            floor.transform.localScale = new Vector3(1.7f, 0.02f, 1.7f);
            floor.GetComponent<Renderer>().sharedMaterial = CharacterLook.Mat(new Color(0.13f, 0.14f, 0.16f), 0.2f, 0.55f);
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(ring.GetComponent<Collider>());
            ring.transform.SetParent(transform, false);
            ring.transform.localPosition = new Vector3(0f, -0.025f, 0f);
            ring.transform.localScale = new Vector3(1.78f, 0.015f, 1.78f);
            var ringMat = CharacterLook.Mat(new Color(0.3f, 0.22f, 0.08f), 0f, 0.3f);
            ringMat.SetColor("_EmissionColor", UIKit.Accent * 1.6f);
            ring.GetComponent<Renderer>().sharedMaterial = ringMat;

            // lights: warm key, cool fill, amber rim
            Light("Key", new Vector3(2.2f, 2.6f, 2.4f), new Color(1f, 0.93f, 0.85f), 9f, 7f);
            Light("Fill", new Vector3(-2.6f, 1.5f, 1.8f), new Color(0.6f, 0.72f, 1f), 3.5f, 7f);
            Light("Rim", new Vector3(-0.6f, 2.4f, -2.4f), new Color(1f, 0.72f, 0.3f), 8f, 6f);

            var camGO = new GameObject("PreviewCamera");
            camGO.transform.SetParent(transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.05f, 5.2f);
            camGO.transform.LookAt(transform.position + new Vector3(0f, 0.82f, 0f));
            cam = camGO.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.075f, 0.082f, 0.095f, 1f);
            cam.targetTexture = Texture;
            cam.depth = -10;
        }

        void Light(string n, Vector3 pos, Color c, float intensity, float range)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c; l.intensity = intensity; l.range = range;
            l.shadows = n == "Key" ? LightShadows.Soft : LightShadows.None;
        }

        /// <summary>Rebuilds the soldier (call after changing build / anything structural) and applies the look.</summary>
        public void Rebuild()
        {
            if (Rig) Destroy(Rig.gameObject);
            var look = CharacterLook.Local;
            Rig = HumanoidRig.Build(turntable, look.Palette(), false);
            look.Apply(Rig);
            anim = Rig.gameObject.AddComponent<CharacterAnimator>();
            anim.rig = Rig;
            anim.holdingWeapon = false;
        }

        public void Apply() { if (Rig) CharacterLook.Local.Apply(Rig); }

        public void Drag(float dx) { yaw -= dx * 0.4f; lastDrag = Time.unscaledTime; }

        void Update()
        {
            idleT += Time.unscaledDeltaTime;
            if (Time.unscaledTime - lastDrag > 3f) yaw += Time.unscaledDeltaTime * 8f; // slow showroom spin
            turntable.localRotation = Quaternion.Euler(0f, yaw, 0f);
            if (anim) anim.lookYaw = Mathf.Sin(idleT * 0.4f) * 18f;
        }

        /// <summary>Adds a draggable preview image to a UI parent.</summary>
        public RawImage Show(RectTransform parent)
        {
            var img = UIKit.Rect("Preview", parent).gameObject.AddComponent<RawImage>();
            UIKit.Fill(img.rectTransform);
            img.texture = Texture;
            img.gameObject.AddComponent<PreviewDrag>().preview = this;
            return img;
        }

        class PreviewDrag : MonoBehaviour, IDragHandler
        {
            public LookPreview preview;
            public void OnDrag(PointerEventData e) { if (preview) preview.Drag(e.delta.x); }
        }
    }
}
