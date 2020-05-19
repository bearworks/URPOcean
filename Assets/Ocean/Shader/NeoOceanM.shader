// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'


Shader "NeoOcean/NeoSurfaceM" { 
Properties {
	_BaseColor ("Base color", COLOR)  = ( .54, .95, .99, 1)	
	_ShallowColor ("Shallow color", COLOR)  = ( .10, .4, .43, 1)	

	_DistortParams ("Refract, Reflect, Normal Power, Normal Sharp Bias)", Vector) = (0.5 ,10.0, 2.0, 1.15)
	_AboveDepth ("AboveDepth", Range (0.1, 1)) = 0.3
	_ShallowEdge ("ShallowEdge", Range (0.01, 1.0)) = 0.12

	_Fade ("Fade", Range (0, 0.03)) = 0.002
	//_nSnell ("nSnell", Range (1.2, 3.0)) = 3
	
	_SunIntensity ("SunIntensity", Range (0.0, 10)) = 0.05
	_Shininess ("Shininess", Range (2.0, 500)) = 32	

	[HideInInspector]_WaveTex("WaveTex", 2D) = "black" {}
	[HideInInspector]_WaveCoord("WaveCoord", Vector) = (0, 0, 0, 0)

	[KeywordEnum(OFF, ON)] _PIXELFORCES("Point High Light", float) = 0
	[HideInInspector][KeywordEnum(OFF, ON)] _PROJECTED("Projected Mode", float) = 0
} 

Subshader
	{
		Tags{ "RenderType" = "Transparent" "Queue" = "Transparent-11" }

		Lod 400

		Pass{
		Tags{
		"LightMode" = "UniversalForward"
	}
		Blend One Zero
		ZTest LEqual
		ZWrite On
		Cull Off
		Fog{ Mode off }

		HLSLPROGRAM

		#pragma target 3.0 
		#pragma exclude_renderers gles

		#pragma multi_compile _PIXELFORCES_OFF _PIXELFORCES_ON
		#pragma multi_compile _PROJECTED_OFF _PROJECTED_ON
		#include "NeoInclude.hlsl"
		#pragma vertex vert_MQ
		#pragma fragment frag_MQ
		//#pragma multi_compile _FOGFORCE_OFF _FOGFORCE_ON
				//	#pragma multi_compile __ FOG_LINEAR
		#pragma fragmentoption ARB_precision_hint_fastest

		ENDHLSL
	}
	}

	Subshader
{
	Tags{ "RenderType" = "Transparent" "Queue" = "Transparent-11" }

	Lod 300

	Pass{
	Tags{
	"LightMode" = "UniversalForward"
}
	Blend One Zero
	ZTest LEqual
	ZWrite On
	Cull Off
	Fog{ Mode off }

	HLSLPROGRAM

	#pragma target 3.0 
	#pragma exclude_renderers gles
#pragma multi_compile _PIXELFORCES_OFF _PIXELFORCES_ON
#pragma multi_compile _PROJECTED_OFF _PROJECTED_ON
	#include "NeoInclude.hlsl"
	#pragma vertex vert_MQ
	#pragma fragment frag_MQ
			//	#pragma multi_compile __ FOG_LINEAR
	#pragma fragmentoption ARB_precision_hint_fastest

	ENDHLSL
}
}


Fallback off
}
