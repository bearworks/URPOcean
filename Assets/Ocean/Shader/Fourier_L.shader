// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'


Shader "URPOcean/Fourier_L" 
{
	HLSLINCLUDE
	
	#include "NeoInclude.hlsl"

	sampler2D _ReadBuffer0;
	sampler2D _ButterFlyLookUp;
	
	struct v2f 
	{
		float4  pos : SV_POSITION;
		float2  uv : TEXCOORD0;
	};

	v2f vert(appdata_base v)
	{
		v2f OUT;
		OUT.pos = UnityObjectToClipPos(v.vertex);
		OUT.uv = v.texcoord.xy;
		return OUT;
	}
	


	float2 FFT2(float2 w, float2 input1, float2 input2) 
	{
		float rx = w.x * input2.x - w.y * input2.y;
		float ry = w.y * input2.x + w.x * input2.y;

		return input1 + float2(rx,ry);
	}

	float4 fragX_L(v2f IN): SV_Target
	{
		float4 lookUp = tex2D(_ButterFlyLookUp, float2(IN.uv.x, 0));

		float a = UNITY_TWO_PI * lookUp.z;
		float2 w = float2(cos(a), sin(a));
		
		 w *= (lookUp.w * 2 - 1.0);
		
		float2 uv1 = float2(lookUp.x, IN.uv.y);
		float2 uv2 = float2(lookUp.y, IN.uv.y);
		
		float4 raw1 = tex2D(_ReadBuffer0, uv1);
		float4 raw2 = tex2D(_ReadBuffer0, uv2);

		float2 OUT = FFT2(w, XDecodeFloatRG(raw1), XDecodeFloatRG(raw2));

		return XEncodeFloatRG(OUT);
	}
	
	float4 fragY_L(v2f IN): SV_Target
	{
		float4 lookUp = tex2D(_ButterFlyLookUp, float2(IN.uv.y, 0));
		
		//todo: Wlut
		float a = UNITY_TWO_PI*lookUp.z;
		float2 w = float2(cos(a), sin(a));
		
		w *= (lookUp.w * 2 - 1.0);
		
		float2 uv1 = float2(IN.uv.x, lookUp.x);
		float2 uv2 = float2(IN.uv.x, lookUp.y);
		
		float4 raw1 = tex2D(_ReadBuffer0, uv1);
		float4 raw2 = tex2D(_ReadBuffer0, uv2);

		float2 OUT = FFT2(w, XDecodeFloatRG(raw1), XDecodeFloatRG(raw2));

		return XEncodeFloatRG(OUT);
	}

	float4 fragF(v2f IN): SV_Target
	{
		float4 raw1 = tex2D(_ReadBuffer0, IN.uv.xy);
		return half4(XDecodeFloatRG(raw1), 0, 0);
	}
	
	
	ENDHLSL
			
	SubShader 
	{
		Pass 
    	{
			ZTest Always Cull Off ZWrite Off
      		Fog { Mode off }
    		
			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragX_L
			#pragma exclude_renderers gles

			#pragma fragmentoption ARB_precision_hint_fastest

			ENDHLSL
		}
		
		Pass 
    	{
			ZTest Always Cull Off ZWrite Off
      		Fog { Mode off }
    		
			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragY_L
			#pragma exclude_renderers gles

			#pragma fragmentoption ARB_precision_hint_fastest

			ENDHLSL
		}

		Pass 
    	{
			ZTest Always Cull Off ZWrite Off
      		Fog { Mode off }
    		
			HLSLPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragF
			#pragma exclude_renderers gles

			#pragma fragmentoption ARB_precision_hint_fastest

			ENDHLSL
		}
	}

}