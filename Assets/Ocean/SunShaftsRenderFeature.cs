using System;

namespace UnityEngine.Rendering.Universal
{
    public class SunShaftsRenderFeature : ScriptableRendererFeature
    {
        SunShaftsPass sunShaftsPass;
        
        public Shader sunShaftsPS;

        public override void Create()
        {
            if(sunShaftsPS == null)
               sunShaftsPS = Shader.Find("Hidden/Universal Render Pipeline/SunShaftsComposite");

            sunShaftsPass = new SunShaftsPass(RenderPassEvent.BeforeRenderingPostProcessing, sunShaftsPS);
        }
#if UNITY_2022_1_OR_NEWER
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(sunShaftsPass);
        }

        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            sunShaftsPass.Setup(renderer.cameraColorTarget);  // use of target after allocation
        }
#else
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            sunShaftsPass.Setup(renderer.cameraColorTarget);
            renderer.EnqueuePass(sunShaftsPass);
        }
#endif
    }
}
