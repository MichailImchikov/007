using UnityEngine;

namespace FPS
{
    /// <summary>
    /// A capture zone / bomb site on the ground: a coloured ring laid over the floor (each point dropped onto what is
    /// below it, so it follows steps and ramps) with a faint inner ring. Unlit and transparent: no shadow, no comic outline.
    /// </summary>
    public class ZoneRing : MonoBehaviour
    {
        const int Segments = 56;
        static Material mat;
        LineRenderer outer, inner;
        Vector3 center;
        float radius = -1f;

        public static ZoneRing Create(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var z = go.AddComponent<ZoneRing>();
            z.outer = z.Line("Outer", 0.16f);
            z.inner = z.Line("Inner", 0.06f);
            return z;
        }

        LineRenderer Line(string n, float width)
        {
            if (!mat)
            {
                var sh = Shader.Find("Sprites/Default");
                mat = new Material(sh ? sh : Shader.Find("Universal Render Pipeline/Unlit")) { name = "ZoneRing" };
            }
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.loop = true;
            lr.useWorldSpace = true;
            lr.positionCount = Segments;
            lr.widthMultiplier = width;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            return lr;
        }

        public void Place(Vector3 c, float r)
        {
            if ((c - center).sqrMagnitude < 0.0001f && Mathf.Abs(r - radius) < 0.001f) return;
            center = c; radius = r;
            Lay(outer, r);
            Lay(inner, r * 0.93f);
        }

        void Lay(LineRenderer lr, float r)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                Vector3 p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                // the floor under the ring (steps and slopes), not the tops of wells, counters and crates standing in it
                float y = center.y;
                if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && Mathf.Abs(hit.point.y - center.y) < 0.45f)
                    y = hit.point.y;
                lr.SetPosition(i, new Vector3(p.x, y + 0.07f, p.z));
            }
        }

        public void SetColor(Color c, float alpha = 1f)
        {
            Color o = c; o.a = 0.9f * alpha;
            Color i = c; i.a = 0.35f * alpha;
            outer.startColor = outer.endColor = o;
            inner.startColor = inner.endColor = i;
        }

        public void SetVisible(bool v)
        {
            if (outer.enabled != v) outer.enabled = inner.enabled = v;
        }
    }
}
