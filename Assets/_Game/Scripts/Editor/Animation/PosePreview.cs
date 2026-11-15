using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FPS.EditorTools.Anim
{
    /// <summary>
    /// Renders poses of a HumanRigProbe into a PNG contact sheet (visual checks of generated clips). The skinned
    /// meshes are baked per frame (a manual Camera.Render does not re-skin), drawn on a private layer.
    /// </summary>
    public static class PosePreview
    {
        const int Layer = 31;

        public static void Sheet(HumanRigProbe p, IList<PoseFrame> frames, string path, Vector3 viewDir, float size = 1.15f, Vector3? center = null, int cell = 256)
        {
            var camGO = new GameObject("PosePreviewCam") { hideFlags = HideFlags.HideAndDontSave };
            var lightGO = new GameObject("PosePreviewLight") { hideFlags = HideFlags.HideAndDontSave };
            var bakedRoot = new GameObject("PosePreviewBaked") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var sheet = new Texture2D(cell * frames.Count, cell, TextureFormat.RGBA32, false);
            var smrs = new List<SkinnedMeshRenderer>();
            foreach (var smr in p.go.GetComponentsInChildren<SkinnedMeshRenderer>(false)) if (smr.enabled && smr.gameObject.activeInHierarchy) smrs.Add(smr);
            var baked = new List<(SkinnedMeshRenderer smr, Transform t, Mesh mesh)>();
            try
            {
                foreach (var smr in smrs)
                {
                    var go = new GameObject("B_" + smr.name) { hideFlags = HideFlags.HideAndDontSave, layer = Layer };
                    go.transform.SetParent(bakedRoot.transform, false);
                    var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = smr.sharedMaterials;
                    baked.Add((smr, go.transform, mesh));
                    smr.forceRenderingOff = true;
                }

                var cam = camGO.AddComponent<Camera>();
                cam.enabled = false;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.6f, 0.66f, 1f);
                cam.orthographic = true;
                cam.orthographicSize = size;
                cam.cullingMask = 1 << Layer;
                cam.nearClipPlane = 0.01f; cam.farClipPlane = 20f;
                cam.targetTexture = rt;
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;
                light.cullingMask = 1 << Layer;
                light.transform.rotation = Quaternion.LookRotation(new Vector3(-0.4f, -0.8f, 0.5f));

                Vector3 c = center ?? new Vector3(0f, size * 0.9f, 0f);
                Vector3 d = viewDir.normalized;
                camGO.transform.position = c - d * 6f;
                camGO.transform.rotation = Quaternion.LookRotation(d, Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up);
                var tmp = new Texture2D(cell, cell, TextureFormat.RGBA32, false);
                for (int i = 0; i < frames.Count; i++)
                {
                    p.Apply(frames[i]);
                    foreach (var b in baked)
                    {
                        b.smr.BakeMesh(b.mesh, true);
                        b.t.SetPositionAndRotation(b.smr.transform.position, b.smr.transform.rotation);
                        b.t.localScale = Vector3.one;
                    }
                    cam.Render();
                    RenderTexture.active = rt;
                    tmp.ReadPixels(new Rect(0, 0, cell, cell), 0, 0);
                    tmp.Apply();
                    sheet.SetPixels(i * cell, 0, cell, cell, tmp.GetPixels());
                }
                Object.DestroyImmediate(tmp);
                RenderTexture.active = null;
                // ground line (y = 0) and frame separators
                Vector3 g = cam.WorldToViewportPoint(new Vector3(0f, 0f, c.z));
                int gy = Mathf.RoundToInt(g.y * cell);
                for (int i = 0; i < frames.Count; i++)
                {
                    for (int y = 0; y < cell; y++) sheet.SetPixel(i * cell, y, Color.black);
                    if (gy > 0 && gy < cell && Mathf.Abs(d.y) < 0.5f) for (int x = 0; x < cell; x++) sheet.SetPixel(i * cell + x, gy, new Color(0.2f, 0.25f, 0.2f));
                }
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, sheet.EncodeToPNG());
            }
            finally
            {
                foreach (var b in baked) { if (b.smr) b.smr.forceRenderingOff = false; if (b.mesh) Object.DestroyImmediate(b.mesh); }
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(camGO);
                Object.DestroyImmediate(lightGO);
                Object.DestroyImmediate(bakedRoot);
            }
        }

        public static List<PoseFrame> Frames(HumanAnim a, int n)
        {
            var l = new List<PoseFrame>();
            for (int i = 0; i < n; i++) l.Add(a.Sample(a.Length * i / Mathf.Max(1, n - (a.loop ? 0 : 1))));
            return l;
        }
    }
}
