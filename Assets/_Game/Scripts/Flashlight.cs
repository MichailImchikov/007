using UnityEngine;

namespace FPS
{
    /// <summary>
    /// Player flashlight: hold the melee key (F) for <see cref="holdTime"/> seconds to switch it on or off.
    /// A short tap is still a knife slash. The spot light rides on the player camera.
    /// </summary>
    public class Flashlight : MonoBehaviour
    {
        public float holdTime = 1.5f;
        public float range = 38f;
        public float angle = 52f;
        public float intensity = 18f;

        public bool On { get; private set; }
        /// <summary>0..1 while the key is held towards a toggle, 0 otherwise (read by the HUD).</summary>
        public float HoldProgress => holding && held > 0.25f ? Mathf.Clamp01(held / holdTime) : 0f;

        Light spot;
        float held;
        bool holding, fired;

        void Update()
        {
            if (!spot) Build();
            if (!spot) return;

            if (GameInput.FlashlightHeld)
            {
                holding = true;
                held += Time.deltaTime;
                if (!fired && held >= holdTime) { fired = true; Set(!On); }
            }
            else { holding = false; held = 0f; fired = false; }

            if (GameInput.Blocked && On) Set(false); // dead / match over
        }

        void Build()
        {
            var melee = GetComponent<MeleeController>();
            Camera cam = melee ? melee.cam : null;
            if (!cam) cam = Camera.main;
            if (!cam) return;
            var go = new GameObject("Flashlight");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0.12f, -0.1f, 0.15f);
            go.transform.localRotation = Quaternion.Euler(1.5f, -1f, 0f);
            spot = go.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = range;
            spot.spotAngle = angle;
            spot.innerSpotAngle = angle * 0.45f;
            spot.intensity = intensity;
            spot.color = new Color(1f, 0.95f, 0.85f);
            spot.shadows = LightShadows.Soft;
            spot.shadowStrength = 0.85f;
            spot.renderMode = LightRenderMode.ForcePixel;
            spot.enabled = On;
        }

        public void Set(bool on)
        {
            On = on;
            if (spot) spot.enabled = on;
        }
    }
}
