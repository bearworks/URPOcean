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
				float w_T = w * _T;
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
			    float2 k3 = st * _InverseGridSizes.y;

				// float k1 = length(uv * inverseWorldSizes.x);
				float k22 = length(k2);
				float k33 = length(k3);
				//float k4 = length(uv * inverseWorldSizes.w);

				//float r = sqrt(9.81 * k1 * (1.0 + k1 * k1 / (WAVE_KM * WAVE_KM)));
#if 1
				float g = sqrt(9.81 * k22 * (1.0 + k22 * k22 / (WAVE_KM * WAVE_KM)));
				float b = sqrt(9.81 * k33 * (1.0 + k33 * k33 / (WAVE_KM * WAVE_KM)));
#else
				float g = sqrt(9.81 * k22);
				float b = sqrt(9.81 * k33);
#endif

#if 0
				const float period = 10; //quantization
				g /= period;
				b /= period;
				g -= frac(g);
				b -= frac(b);
				g *= period;
				b *= period;
#endif

			    float2 h2 = GetSpectrum(g, s12.xy, s12c.xy);
			   	float2 h3 = GetSpectrum(b, s12.zw, s12c.zw);
			    
				float2 n2 = COMPLEX(k2.x * h2) - k2.y * h2;
				float2 n3 = COMPLEX(k3.x * h3) - k3.y * h3;
	
			    float4 OUT;
			    
	//#if 0
	//			float choppiness = 1;
	//			float ik2 = choppiness / max(g, 0.01);
	//			float ik3 = choppiness / max(b, 0.01);

	//			//displace
	//			OUT = float4(h2 + h3, n2 * ik2 + n3 * ik3);
	//#else
				//normal only
				OUT = float4(n2 + n3, 0, 0);
	//#endif
				return OUT;
			}
			
			ENDHLSL

    	}
	}
	Fallback off
}
