using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace WaterSystem.Ocean
{
    // URP 17 RenderGraph. Installed once on renderer assets; Ocean controls the effect per camera.
    public sealed class OceanUnderwaterFeature : ScriptableRendererFeature
    {
        UnderwaterPass pass;
        public override void Create()
        {
            pass?.Dispose();
            pass = new UnderwaterPass();
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var data = renderingData.cameraData;
            // Overlay cameras already inherit the base camera color. Never fog that color twice.
            if (data.renderType != CameraRenderType.Base || data.xr.enabled) return;
            var ocean = OceanRenderer.FindUnderwaterOcean(data.camera);
            if (ocean == null || !pass.Setup(ocean)) return;
            renderer.EnqueuePass(pass);
        }
        protected override void Dispose(bool disposing) { pass?.Dispose(); pass = null; }

        sealed class UnderwaterPass : ScriptableRenderPass
        {
            Material material;
            Shader materialShader;
            CopyDepthPass copyDepth;
            OceanRenderer ocean;
            public UnderwaterPass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
                requiresIntermediateTexture = true;
                ConfigureInput(ScriptableRenderPassInput.Depth);
            }
            public bool Setup(OceanRenderer value)
            {
                ocean = value;
                var resources = OceanResources.Load();
                // Physical and stylized each own a volume shader; both resolve depth the same way.
                Shader shader = value.ShadingMode == OceanShadingMode.Stylized
                    ? resources.StylizedUnderwaterShader : resources.UnderwaterShader;
                if (shader == null || resources.UnderwaterDepthCopyShader == null) return false;
                // Switching shading mode swaps the volume shader, so key the cache on the shader too.
                if (material != null && materialShader == shader) return true;
                CoreUtils.Destroy(material);
                material = CoreUtils.CreateEngineMaterial(shader);
                materialShader = shader;
                copyDepth ??= new CopyDepthPass(renderPassEvent, resources.UnderwaterDepthCopyShader, customPassName: "Ocean / resolve depth after water");
                return true;
            }
            sealed class PassData
            {
                internal Material Material;
                internal MaterialPropertyBlock Properties;
                internal TextureHandle Source, Depth, Destination;
            }
            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var camera = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer || ocean == null) return;

                // URP's earlier cameraDepthTexture may contain only opaque objects. Resolve the
                // actual depth attachment AFTER the ocean, including its SV_Depth and MSAA samples.
                var depthDesc = graph.GetTextureDesc(resources.activeDepthTexture);
                depthDesc.name = "Ocean depth including surface";
                depthDesc.format = GraphicsFormat.R32_SFloat;
                depthDesc.msaaSamples = MSAASamples.None;
                depthDesc.bindTextureMS = false;
                depthDesc.clearBuffer = false;
                var depth = graph.CreateTexture(depthDesc);
                copyDepth.Render(graph, depth, resources.activeDepthTexture, resources, camera, false, "Ocean / resolve depth after water");

                var source = resources.activeColorTexture;
                var colorDesc = graph.GetTextureDesc(source);
                colorDesc.name = "Ocean underwater color";
                colorDesc.msaaSamples = MSAASamples.None;
                colorDesc.bindTextureMS = false;
                colorDesc.clearBuffer = false;
                var destination = graph.CreateTexture(colorDesc);
                using (var builder = graph.AddRasterRenderPass<PassData>("Ocean / underwater scattering", out var data))
                {
                    data.Material = material;
                    data.Properties = new MaterialPropertyBlock();
                    if (ocean.ShadingMode == OceanShadingMode.Stylized) ocean.BindStylizedUnderwater(data.Properties, camera.camera);
                    else ocean.BindUnderwater(data.Properties, camera.camera);
                    data.Source = source; data.Depth = depth; data.Destination = destination;
                    builder.UseTexture(source);
                    builder.UseTexture(depth);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) =>
                    {
                        RTHandle color = d.Source, depthTexture = d.Depth;
                        d.Properties.SetTexture("_BlitTexture", color);
                        d.Properties.SetTexture("_OceanSceneDepth", depthTexture);
                        Vector2 scale = color.useScaling ? new Vector2(color.rtHandleProperties.rtHandleScale.x, color.rtHandleProperties.rtHandleScale.y) : Vector2.one;
                        bool flip = context.GetTextureUVOrigin(in d.Source) != context.GetTextureUVOrigin(in d.Destination);
                        d.Properties.SetVector("_BlitScaleBias", flip ? new Vector4(scale.x,-scale.y,0,scale.y) : new Vector4(scale.x,scale.y,0,0));
                        context.cmd.DrawProcedural(Matrix4x4.identity, d.Material, 0, MeshTopology.Triangles, 3, 1, d.Properties);
                    });
                }
                resources.cameraColor = destination;
            }
            public void Dispose() { CoreUtils.Destroy(material); material = null; materialShader = null; copyDepth?.Dispose(); copyDepth = null; }
        }
    }
}
