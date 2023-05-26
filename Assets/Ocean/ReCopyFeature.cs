using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class ReCopyFeature : ScriptableRendererFeature
    {
#if UNITY_2022_1_OR_NEWER
        Material samplingMaterial;
        ColorBlitPass m_CopyColorPass = null;

        DepthBlitPass m_CopyDepthPass;
        Material copyDepthPassMaterial = null;

        internal RTHandle m_DepthTexture;
        public override void AddRenderPasses(ScriptableRenderer renderer,
                                        ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Game && renderingData.cameraData.postProcessEnabled)
            {
                renderer.EnqueuePass(m_CopyDepthPass);
                renderer.EnqueuePass(m_CopyColorPass);
            }
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer,
                                            in RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType == CameraType.Game && renderingData.cameraData.postProcessEnabled)
            {
                // Calling ConfigureInput with the ScriptableRenderPassInput.Color argument
                // ensures that the opaque texture is available to the Render Pass.
                m_CopyColorPass.ConfigureInput(ScriptableRenderPassInput.Color);
                m_CopyColorPass.SetTarget(renderer.cameraColorTargetHandle);

                var depthDescriptor = renderingData.cameraData.cameraTargetDescriptor;
  
                depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
                depthDescriptor.depthStencilFormat = GraphicsFormat.None;
                depthDescriptor.depthBufferBits = 0;

                depthDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA
                RenderingUtils.ReAllocateIfNeeded(ref m_DepthTexture, depthDescriptor, FilterMode.Point, wrapMode: TextureWrapMode.Clamp, name: "_CameraDepthTexture");

                m_CopyDepthPass.Setup(renderer.cameraDepthTargetHandle, m_DepthTexture);
            }
        }

        public override void Create()
        {
            samplingMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Universal Render Pipeline/Sampling"));
            copyDepthPassMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Universal Render Pipeline/CopyDepth"));

            m_CopyColorPass = new ColorBlitPass(RenderPassEvent.AfterRenderingTransparents, samplingMaterial);
            m_CopyDepthPass = new DepthBlitPass(RenderPassEvent.AfterRenderingTransparents, copyDepthPassMaterial);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(samplingMaterial);
            CoreUtils.Destroy(copyDepthPassMaterial);
            m_DepthTexture?.Release();
        }
#else
        XCopyDepthPass m_CopyDepthPass;
        XCopyColorPass m_CopyColorPass;

        Material copyDepthPassMaterial = null;
        Material samplingMaterial;

        RenderTargetHandle m_CameraDepthAttachment;
        RenderTargetHandle m_DepthTexture;

        RenderTargetHandle m_CameraColorAttachment;
        RenderTargetHandle m_ColorTexture;

        public override void Create()
        {
            m_CameraDepthAttachment.Init("_CameraDepthAttachment");
            m_DepthTexture.Init("_CameraDepthTexture");

            m_CameraColorAttachment.Init("_CameraColorTexture");
            m_ColorTexture.Init("_ColorTexture");

            copyDepthPassMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Universal Render Pipeline/CopyDepth"));
            samplingMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Universal Render Pipeline/Sampling"));

            m_CopyDepthPass = new XCopyDepthPass(RenderPassEvent.AfterRenderingTransparents, copyDepthPassMaterial);
            m_CopyColorPass = new XCopyColorPass(RenderPassEvent.AfterRenderingTransparents, samplingMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!Application.isPlaying)
                return;

            m_CopyDepthPass.Setup(m_CameraDepthAttachment, m_DepthTexture);
            renderer.EnqueuePass(m_CopyDepthPass);

            Downsampling downsamplingMethod = UniversalRenderPipeline.asset.opaqueDownsampling;
            m_CopyColorPass.Setup(m_CameraColorAttachment.Identifier(), m_ColorTexture, downsamplingMethod);
            renderer.EnqueuePass(m_CopyColorPass);
        }
#endif
    }
}
