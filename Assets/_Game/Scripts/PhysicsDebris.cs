using UnityEngine;

namespace FPS
{
    /// <summary>Shell casings / dropped magazines: plays a sound on the first few impacts.</summary>
    public class PhysicsDebris : MonoBehaviour
    {
        public Sfx sound = Sfx.Shell;
        public float volume = 0.35f;
        public int maxSounds = 2;
        int played;
        float lastTime;

        public void ResetSounds() { played = 0; }

        void OnCollisionEnter(Collision c)
        {
            if (played >= maxSounds || Time.time - lastTime < 0.05f) return;
            float v = c.relativeVelocity.magnitude;
            if (v < 0.6f) return;
            played++;
            lastTime = Time.time;
            ProcAudio.PlayAt(sound, transform.position, volume * Mathf.Clamp01(v / 3f), 0.12f);
        }
    }
}
