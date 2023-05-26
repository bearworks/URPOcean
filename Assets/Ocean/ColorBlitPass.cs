#if UNITY_2022_1_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Rendering.Universal.Internal
{
    public class ColorBlitPass : ScriptableRenderPass
    {
        ProfilingSampler m_ProfilingSampler = new ProfilingSampler("ColorBlitPass");
        RTHandle m_CameraColorTarget;
        RTHandle targetHandle;

        int m_SampleOffsetShaderHandle;
        Material m_SamplingMaterial;
        Downsampling m_DownsamplingMethod;

        static readonly int TempTargetId = Shader.PropertyToID("_ColorTexture");

        public ColorBlitPass(RenderPassEvent evt, Material samplingMaterial)
        {
            renderPassEvent = evt;
            m_SamplingMaterial = samplingMaterial;
            m_SampleOffsetShaderHandle = Shader.PropertyToID("_SampleOffset");
            m_DownsamplingMethod = Downsampling.None;
        }

        public void SetTarget(RTHandle colorHandle)
        {
            m_CameraColorTarget = colorHandle;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.msaaSamples = 1;
            descriptor.depthBufferBits = 0;

            targetHandle = RTHandles.Alloc(TempTargetId);

            cmd.GetTemporaryRT(TempTargetId, descriptor, FilterMode.Bilinear);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            if (cameraData.camera.cameraType != CameraType.Game)
                return;

             if (!renderingData.cameraData.postProcessEnabled)
                return;

            if (m_SamplingMaterial == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                switch (m_DownsamplingMethod)
                {
                    case Downsampling.None:
                        Blitter.BlitCameraTexture(cmd, m_CameraColorTarget, targetHandle);
                        break;
                    case Downsampling._2xBilinear:
                        Blitter.BlitCameraTexture(cmd, m_CameraColorTarget, targetHandle);
                        break;
                    case Downsampling._4xBox:
                        m_SamplingMaterial.SetFloat(m_SampleOffsetShaderHandle, 2.0f);
                        Blitter.BlitCameraTexture(cmd, m_CameraColorTarget, targetHandle, m_SamplingMaterial, 0);
                        break;
                    case Downsampling._4xBilinear:
                        Blitter.BlitCameraTexture(cmd, m_CameraColorTarget, targetHandle);
                        break;
                }
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            CommandBufferPool.Release(cmd);
        }


        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            if (cmd == null)
                throw new ArgumentNullException("cmd");

             if (targetHandle != m_CameraColorTarget)
            {
                cmd.ReleaseTemporaryRT(TempTargetId);
            }
        }
    }
}
#endif