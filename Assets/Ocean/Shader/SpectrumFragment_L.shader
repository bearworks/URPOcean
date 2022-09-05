// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

Shader "URPOcean/SpectrumFragment_L" {
	SubShader 
	{
		Pass 
    	{
			ZTest Always Cull Off ZWrite Off
      		Fog { Mode off }
    		
			HLSLPROGRAM
			#include "NeoInclude.hlsl"
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment frag
			#define WAVE_KM 370.0
			#pragma exclude_renderers gles
			sampler2D _Spectrum01;
			sampler2D _WTable;
			float2 _Offset;
			float4 _InverseGridSizes;
			float _T;

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
			
			float2 GetSpectrum(float w, float2 s0, float2 s0c) 
			{
				float w_T = w * (_T + 1e2);
				//this is cos wave compare with gerstner sin waves
			    float c = cos(w_T);
			    float s = sin(w_T);
			    return float2((s0.x + s0c.x) * c - (s0.y + s0c.y) * s, (s0.x - s0c.x) * s + (s0.y - s0c.y) * c);
			}
			
			float2 COMPLEX(float2 z) 
			{
			    return float2(-z.y, z.x); // returns i times z (complex number)
			}

			float4 frag(v2f IN): SV_Target
			{ 
				float2 uv = IN.uv.xy - _Offset;
			
				float2 st;
				st.x = uv.x > 0.5 ? uv.x - 1.0 : uv.x;
		    	st.y = uv.y > 0.5 ? uv.y - 1.0 : uv.y;
		    	
		    	float4 s12 = tex2D(_Spectrum01, uv);
		    	float4 s12c = tex2D(_Spectrum01, -uv);

			    float2 k2 = st * _InverseGridSizes.x;

				float k22 = length(k2);
#if 1
				float g = sqrt(9.81 * k22 * (1.0 + k22 * k22 / (WAVE_KM * WAVE_KM)));
#else
				float g = sqrt(9.81 * k22);
#endif

			    float2 h2 = GetSpectrum(g, XDecodeFloatRG(s12), XDecodeFloatRG(s12c));
			    
				float2 n2 = COMPLEX(k2.x * h2) - k2.y * h2;

				return XEncodeFloatRG(n2);
			}
			
			ENDHLSL

    	}
	}
	Fallback off
}
