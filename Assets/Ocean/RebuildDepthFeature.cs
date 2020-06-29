using System;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class RebuildDepthFeature : ScriptableRendererFeature
    {
        XCopyDepthPass m_CopyDepthPass;

        Material copyDepthPassMaterial = null;

        RenderTargetHandle m_CameraDepthAttachment;
        RenderTargetHandle m_DepthTexture;

        public override void Create()
        {
            m_CameraDepthAttachment.Init("_CameraDepthAttachment");
            m_DepthTexture.Init("_CameraDepthTexture");

            copyDepthPassMaterial = CoreUtils.CreateEngineMaterial(Shader.Find("Hidden/Universal Render Pipeline/CopyDepth"));

            m_CopyDepthPass = new XCopyDepthPass(RenderPassEvent.AfterRenderingTransparents, copyDepthPassMaterial);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            m_CopyDepthPass.Setup(m_CameraDepthAttachment, m_DepthTexture);
            renderer.EnqueuePass(m_CopyDepthPass);
        }
    }
}
