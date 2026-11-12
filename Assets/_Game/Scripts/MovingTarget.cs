using UnityEngine;

namespace FPS
{
    /// <summary>Slides back and forth on a rail with smooth turnarounds.</summary>
    public class MovingTarget : MonoBehaviour
    {
        public Vector3 axis = Vector3.right;
        public float distance = 3f;
        public float speed = 2f;

        Vector3 origin;
        float phase;

        void Start()
        {
            origin = transform.position;
            phase = Random.value * Mathf.PI * 2f;
        }

        void Update()
        {
            phase += Time.deltaTime * speed / Mathf.Max(0.1f, distance);
            transform.position = origin + axis.normalized * (Mathf.Sin(phase) * distance);
        }
    }
}
