using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace FPS
{
    /// <summary>
    /// Characters in the comic look, right after the opaque scene:
    ///  - the character mask (every frame there are bodies): each visible body's eye depth into _FPS_CharMask, which
    ///    FPS/ToonPost compares with the scene depth to keep characters readable (silhouette ink only, no crease lines
    ///    over the gear — at a distance those lines turned soldiers into black shapes — and lighter dark tones);
    ///  - the kill-streak look (<see cref="ComicAvatar"/>): ink outline, halftone shading, rim light and the flame aura
    ///    (FPS/ComicAvatar passes 0..3).
    /// Both draw over each character's own skinned renderers, so nothing is skinned twice and no renderer or material of
    /// the character changes.
    /// </summary>
    public class ComicAvatarFeature : ScriptableRendererFeature
    {
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingSkybox;

        static readonly int MaskId = Shader.PropertyToID("_FPS_CharMask"), MaskOnId = Shader.PropertyToID("_FPS_CharMaskOn");

        DrawPass pass;
        MaskPass maskPass;

        public override void Create()
        {
            pass = new DrawPass { renderPassEvent = passEvent };
            maskPass = new MaskPass { renderPassEvent = passEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var type = renderingData.cameraData.cameraType;
            var cam = renderingData.cameraData.camera;
            // the mask only for the view the player looks through (not the minimap, previews or reflections)
            bool mask = ComicAvatar.AnyBody && (type == CameraType.SceneView || (type == CameraType.Game && cam && cam.CompareTag("MainCamera")));
            Shader.SetGlobalFloat(MaskOnId, mask ? 1f : 0f);
            if (mask) renderer.EnqueuePass(maskPass);
            if (ComicAvatar.AnyDrawn) renderer.EnqueuePass(pass);
        }

        protected override void Dispose(bool disposing) => maskPass?.Dispose();

        class DrawPass : ScriptableRenderPass
        {
            class PassData { }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cam = frameData.Get<UniversalCameraData>();
                if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
                var res = frameData.Get<UniversalResourceData>();
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Comic Avatars", out _))
                {
                    builder.SetRenderAttachment(res.activeColorTexture, 0);
                    builder.SetRenderAttachmentDepth(res.activeDepthTexture, AccessFlags.ReadWrite);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) => ComicAvatar.DrawAll(ctx.cmd));
                }
            }
        }

        /// <summary>
        /// Bodies' eye depth into a persistent R16F texture with its own depth buffer (nearest body wins). Persistent, not a
        /// render-graph transient: the full-screen ToonPost pass reads it as a plain global texture.
        /// </summary>
        class MaskPass : ScriptableRenderPass
        {
            class PassData { }

            RTHandle maskRT;

            public void Dispose() { maskRT?.Release(); maskRT = null; }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cam = frameData.Get<UniversalCameraData>();
                var desc = cam.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;
                desc.depthStencilFormat = GraphicsFormat.None;
                desc.graphicsFormat = GraphicsFormat.R16_SFloat;
                desc.useMipMap = false;
                desc.autoGenerateMips = false;
                RenderingUtils.ReAllocateHandleIfNeeded(ref maskRT, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_FPS_CharMask");
                var mask = renderGraph.ImportTexture(maskRT);
                var depth = renderGraph.CreateTexture(new TextureDesc(desc.width, desc.height)
                {
                    depthBufferBits = DepthBits.Depth24, clearBuffer = true, name = "_FPS_CharMaskDepth",
                });
                Shader.SetGlobalTexture(MaskId, maskRT);
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Character Mask", out _))
                {
                    builder.SetRenderAttachment(mask, 0);
                    builder.SetRenderAttachmentDepth(depth, AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.ClearRenderTarget(RTClearFlags.All, Color.clear, 1f, 0);
                        ComicAvatar.DrawMask(ctx.cmd);
                    });
                }
            }
        }
    }
}
