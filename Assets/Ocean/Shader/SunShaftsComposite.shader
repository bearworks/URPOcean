Shader "Hidden/Universal Render Pipeline/SunShaftsComposite" {
	Properties {
		_MainTex ("Base", 2D) = "white" {}
	}
	
    HLSLINCLUDE

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	#include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
			
	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
	};
		
	struct v2f_radial {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 blurVector : TEXCOORD1;
	};
		
	TEXTURE2D_X(_MainTex);
	TEXTURE2D_X_FLOAT(_CameraDepthTexture);
	TEXTURE2D_X(_ColorBuffer);

	half4 _FoSunColor;
	half _SinRaysWave;
	half4 _BlurRadius4;
	half4 _SunPosition;
	half4 _MainTex_TexelSize;	

	#define SAMPLES_FLOAT 6.0f
	#define SAMPLES_INT 6
			
	v2f vert(Attributes v ) 
	{
		v2f o;
		o.pos = TransformObjectToHClip(v.positionOS);
		o.uv = v.uv.xy;
		
		return o;
	}
		
	half4 fragScreen(v2f i) : SV_Target 
	{ 
		half4 colorA = SAMPLE_TEXTURE2D_X(_MainTex, sampler_PointClamp, i.uv.xy);

		half4 colorB = SAMPLE_TEXTURE2D_X(_ColorBuffer, sampler_PointClamp, i.uv.xy);

		half4 depthMask = saturate (colorB * _FoSunColor);
		return 1.0f - (1.0f-colorA) * (1.0f-depthMask);	
	}

	half4 fragAdd(v2f i) : SV_Target 
	{ 
		half4 colorA = SAMPLE_TEXTURE2D_X(_MainTex, sampler_PointClamp, i.uv.xy);
		half4 colorB = SAMPLE_TEXTURE2D_X(_ColorBuffer, sampler_PointClamp, i.uv.xy);
		half4 depthMask = saturate (colorB * _FoSunColor);
		return colorA + depthMask;	
	}
	
	v2f_radial vert_radial(Attributes v ) 
	{
		v2f_radial o;
		o.pos = TransformObjectToHClip(v.positionOS);
		
		o.uv.xy =  v.uv.xy;
		o.blurVector = (_SunPosition.xy - v.uv.xy) * _BlurRadius4.xy;
		
		return o; 
	}
	
	half4 frag_radial(v2f_radial i) : SV_Target 
	{	
		half4 color = half4(0,0,0,0);
		for(int j = 0; j < SAMPLES_INT; j++)   
		{	
			half4 tmpColor = SAMPLE_TEXTURE2D_X(_MainTex, sampler_PointClamp, i.uv.xy);
			color += tmpColor;
			i.uv.xy += i.blurVector; 	
		}
		return color / SAMPLES_FLOAT;
	}	
	
	half TransformColor (half4 skyboxValue) {
		return max (skyboxValue.a, dot (skyboxValue.rgb, float3 (0.59,0.3,0.11))); 		
	}
	
	half4 frag_depth (v2f i) : SV_Target {

		float depthSample = SAMPLE_TEXTURE2D_X(_CameraDepthTexture, sampler_PointClamp, i.uv.xy);

		half4 tex = SAMPLE_TEXTURE2D_X(_MainTex, sampler_PointClamp, i.uv.xy);
		
		depthSample = Linear01Depth (depthSample, _ZBufferParams);
		 
		// consider maximum radius
		half2 vec = _SunPosition.xy - i.uv.xy;		

		half dist = saturate (_SunPosition.w - length (vec.xy));		
		
		half4 outColor = 0;
		
		// consider shafts blockers
		if (depthSample > 0.99)
			outColor = TransformColor (tex) * dist;
			
		return outColor;
	}
	
	half4 frag_depth_masks (v2f i) : SV_Target {

		return 1;
	}	

	ENDHLSL
		
	Subshader {
	  
	 //0
	 Pass {
		  ZTest Always Cull Off ZWrite Off
		  Fog { Mode off }      

		  HLSLPROGRAM
		  
		  #pragma fragmentoption ARB_precision_hint_fastest 
		  #pragma vertex vert
		  #pragma fragment fragScreen
		  #pragma exclude_renderers gles

		  ENDHLSL
	  }
	  
	  //1
	 Pass {
		  ZTest Always Cull Off ZWrite Off
		  Fog { Mode off }      

		  HLSLPROGRAM
		  
		  #pragma fragmentoption ARB_precision_hint_fastest
		  #pragma vertex vert_radial
		  #pragma fragment frag_radial
		  #pragma exclude_renderers gles

		  ENDHLSL
	  }
	  
	  //2
	  Pass {
		  ZTest Always Cull Off ZWrite Off
		  Fog { Mode off }      

		  HLSLPROGRAM
		  
		  #pragma fragmentoption ARB_precision_hint_fastest      
		  #pragma vertex vert
		  #pragma fragment frag_depth
		  #pragma exclude_renderers gles

		  ENDHLSL
	  }
	  
	  //3
	  Pass {
		  ZTest Always Cull Off ZWrite Off
		  Fog { Mode off }      

		  HLSLPROGRAM
		  
		  #pragma fragmentoption ARB_precision_hint_fastest      
		  #pragma vertex vert
		  #pragma fragment frag_depth_masks
		  #pragma exclude_renderers gles

		  ENDHLSL
	  } 

	  //4
	  Pass {
		  ZTest Always Cull Off ZWrite Off
		  Fog { Mode off }      

		  HLSLPROGRAM
		  
		  #pragma fragmentoption ARB_precision_hint_fastest 
		  #pragma vertex vert
		  #pragma fragment fragAdd
		  #pragma exclude_renderers gles

		  ENDHLSL
	  } 
	}

	Fallback off
	
} // shader