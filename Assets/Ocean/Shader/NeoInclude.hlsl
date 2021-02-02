

#ifndef NEOOCEAN_HLSL_INCLUDED
#define NEOOCEAN_HLSL_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/ImageBasedLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"


#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/lighting.hlsl"

	// Tranforms position from world to homogenous space
float4 UnityWorldToClipPos(in float3 pos)
{
	return mul(UNITY_MATRIX_VP, float4(pos, 1.0));
}

// Tranforms position from view to homogenous space
float4 UnityViewToClipPos(in float3 pos)
{
	return mul(UNITY_MATRIX_P, float4(pos, 1.0));
}

// Tranforms position from object to camera space
float3 UnityObjectToViewPos(in float3 pos)
{
	return mul(UNITY_MATRIX_V, mul(UNITY_MATRIX_M, float4(pos, 1.0))).xyz;
}

float3 UnityObjectToViewPos(float4 pos) // overload for float4; avoids "implicit truncation" warning for existing shaders
{
	return UnityObjectToViewPos(pos.xyz);
}

// Tranforms position from world to camera space
float3 UnityWorldToViewPos(in float3 pos)
{
	return mul(UNITY_MATRIX_V, float4(pos, 1.0)).xyz;
}

// Transforms direction from object to world space
float3 UnityObjectToWorldDir(in float3 dir)
{
	return normalize(mul((float3x3)UNITY_MATRIX_M, dir));
}

// Transforms direction from world to object space
float3 UnityWorldToObjectDir(in float3 dir)
{
	return normalize(mul((float3x3)UNITY_MATRIX_I_M, dir));
}

// Transforms normal from object to world space
float3 UnityObjectToWorldNormal(in float3 norm)
{
#ifdef UNITY_ASSUME_UNIFORM_SCALING
	return UnityObjectToWorldDir(norm);
#else
	// mul(IT_M, norm) => mul(norm, I_M) => {dot(norm, I_M.col0), dot(norm, I_M.col1), dot(norm, I_M.col2)}
	return normalize(mul(norm, (float3x3)UNITY_MATRIX_I_M));
#endif
}

// Tranforms position from object to homogenous space
float4 UnityObjectToClipPos(in float3 pos)
{
	// More efficient than computing M*VP matrix product
	return mul(UNITY_MATRIX_VP, mul(UNITY_MATRIX_M, float4(pos, 1.0)));
}

float4 UnityObjectToClipPos(float4 pos) // overload for float4; avoids "implicit truncation" warning for existing shaders
{
	return UnityObjectToClipPos(pos.xyz);
}

float3 UnityWorldSpaceViewDir(float3 worldPos)
{
	return _WorldSpaceCameraPos.xyz - worldPos;
}

float2 ParallaxOffset(half h, half height, half3 viewDir)
{
	h -= 0.5;
	float3 v = normalize(viewDir);
	v.z += 0.42;
	return h * height * (v.xy / v.z);
}

half LerpOneTo(half b, half t)
{
	half oneMinusT = 1 - t;
	return oneMinusT + b * t;
}

#include "GerstnerWaves.hlsl"

#define UNITY_PI 3.1415926
#define UNITY_TWO_PI 6.2831852

struct appdata_base {
	float4 vertex : POSITION;
	float3 normal : NORMAL;
	float4 texcoord : TEXCOORD0;
	//UNITY_VERTEX_INPUT_INSTANCE_ID
};

#define MaxLitValue 48

half _InvNeoScale;

// colors in use
half4 _SpecularColor;
half4 _BaseColor;
half4 _ShallowColor;
half _ShallowEdge;
half _Fresnel;
half _Shadow;

// edge & shore fading
half _AboveDepth;
float4 _FoamPeak;
float _Fade;

// specularity
float _Shininess;
half _SunIntensity;
half4 _WorldLightPos;
	
// fresnel, vertex & bump displacements & strength
float4 _DistortParams; // need float precision

// shortcuts
#define LERPREFL _DistortParams.x
#define REALTIME_DISTORTION _DistortParams.y
#define NORMAL_POWER _DistortParams.z
#define NORMAL_SHARPBIAS _DistortParams.w
#define BUMP_POWER _DistortParams.z
#define BUMP_SHARPBIAS _DistortParams.w
#define WORLD_UP half3(0,1,0)

#if defined (_WATERWAVE_ON)
sampler2D _WaveTex;
float4 _WaveCoord;
#endif

inline float NeoGGXTerm(float NdotH, float roughness)
{
	float a = roughness * roughness;
	float ta = a;
	a *= a;
	//on some gpus need float precision
	float d = NdotH * NdotH * (a - 1.f) + 1.f;
	return ta / max(UNITY_PI * (d), 1e-7f);
}

inline float GGXSpecularDir(float3 V, float3 N, float3 Dir)
{
	float3 h = normalize(V - Dir);
	float nh = 1 - dot(N, h);

#if 0
	//-------------------------------------
	//##  GEOMETRY FUNCTIONS (Implicit)  ##
	//-------------------------------------
	float gf = max(0, dot(N, -Dir)) * dot(V, N);

	return clamp(gf * NeoGGXTerm(nh, _Shininess), 0, MaxLitValue) * _SunIntensity;
#else
	return clamp(NeoGGXTerm(nh, _Shininess) * saturate(dot(N, -Dir)), 0, MaxLitValue) * _SunIntensity;
#endif
}


inline float Fade(float3 d) 
{
	//on some gpus need float precision
	float _f = length(d) * _Fade;
	return saturate(1 / exp2(_f));
}

// interpolator structs

struct v2f_MQ
{
	float4 pos : SV_POSITION;
	float4 worldPos : TEXCOORD0;
	float4 bumpCoords : TEXCOORD1;
	float4 screenPos : TEXCOORD2;
	float3 normalInterpolator : TEXCOORD3;
#ifdef USE_TANGENT
	float3 tanInterpolator : TEXCOORD4;
	float3 binInterpolator : TEXCOORD5;
#endif
};

sampler2D _Map0;
float4 _Map0_TexelSize;

sampler2D _PlanarReflectionTexture;

#if defined (_WATERWAVE_ON)
TEXTURE2D(_WaterFXMap); SAMPLER(sampler_WaterFXMap);
#endif

TEXTURE2D(_FoamMask); SAMPLER(sampler_FoamMask);
TEXTURE2D(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

float4 _Foam;// need float precision
float4 _FoamMask_ST;// need float precision

// interpolator structs
#if defined (_PROJECTED_ON)

half4 _FoCorners0;
half4 _FoCorners1;
half4 _FoCorners2;
half4 _FoCorners3;
float4 _FoCenter;

inline void FoProjInterpolate(inout half3 i)
{
	half v = i.x; 
	half _1_v = 1 - i.x; 
	half u = i.z; 
	half _1_u = 1 - i.z; 
	i.x = _1_v*(_1_u*_FoCorners0.x + u*_FoCorners1.x) + v*(_1_u*_FoCorners2.x + u*_FoCorners3.x); 
	i.y = _1_v*(_1_u*_FoCorners0.y + u*_FoCorners1.y) + v*(_1_u*_FoCorners2.y + u*_FoCorners3.y); 
	i.z = _1_v*(_1_u*_FoCorners0.z + u*_FoCorners1.z) + v*(_1_u*_FoCorners2.z + u*_FoCorners3.z); 
	half w = _1_v*(_1_u*_FoCorners0.w + u*_FoCorners1.w) + v*(_1_u*_FoCorners2.w + u*_FoCorners3.w); 
	half divide = 1.0f / w; 
	i.x *= divide; 
	i.y *= divide; 
	i.z *= divide;
}
#endif

#if defined(_SSREFLECTION_ON)

void SSRRayConvert(float3 worldPos, out float4 clipPos, out float3 screenPos)
{
	clipPos = TransformWorldToHClip(worldPos);
	float k = ((1.0) / (clipPos.w));

	screenPos.xy = ComputeScreenPos(clipPos).xy * k;

	screenPos.z = k;
}

float3 SSRRayMarch(float3 worldPos, float3 reflection)
{
	float4 startClipPos;
	float3 startScreenPos;

	SSRRayConvert(worldPos, startClipPos, startScreenPos);

	float4 farClipPos;
	float3 farScreenPos;

	SSRRayConvert(worldPos + reflection * 100000, farClipPos, farScreenPos);

	if ((farScreenPos.x > 0) && (farScreenPos.x < 1) && (farScreenPos.y > 0) && (farScreenPos.y < 1))
	{
		float farDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, farScreenPos.xy), _ZBufferParams);

		if (farDepth > startClipPos.w)
		{
			return float3(farScreenPos.xy, 1);
		}
	}

	return float3(0, 0, 0);
}

float3 GetSSRUVZ(float2 screenUV, float fresnel, float3 worldPos, float3 reflection)
{
	screenUV = screenUV * 2 - 1;
	screenUV *= screenUV;

	half ssrWeight = saturate(1 - dot(screenUV, screenUV));

	ssrWeight *= fresnel;

	if (ssrWeight > 0.005)
	{
		float3 uvz = SSRRayMarch(worldPos, reflection);
		uvz.z *= ssrWeight;
		return uvz;
	}

	return float3(0, 0, 0);
}

half4 GetSSRLighting(float2 screenUV, float fresnel, float3 worldPos, float3 reflection)
{
	float3 uvz = GetSSRUVZ(screenUV, fresnel, worldPos, reflection);

	half3 ssrColor = lerp(half3(0, 0, 0), SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uvz.xy), uvz.z > 0);

	return half4(ssrColor, uvz.z);
}
#endif

struct appdata_img
{
	float4 vertex : POSITION;
	half2 texcoord : TEXCOORD0;
};

struct v2f_img
{
	float4 pos : SV_POSITION;
	half2 uv : TEXCOORD0;
};

v2f_img vert_img(appdata_img v)
{
	v2f_img o = (v2f_img)0;

	o.pos = UnityObjectToClipPos(v.vertex);
	o.uv = v.texcoord;
	return o;
}

//
v2f_MQ vert_MQ(appdata_base vert)
{
	v2f_MQ o = (v2f_MQ)0;

	half3 localSpaceVertex = vert.vertex.xyz;
#if defined (_PROJECTED_ON)
		FoProjInterpolate(localSpaceVertex);

		//float3 worldSpaceVertex = mul(_Object2World, half4(localSpaceVertex,1)).xyz;
		float3 center = float3(_FoCenter.x, 0, _FoCenter.z);
		float3 worldSpaceVertex = localSpaceVertex + center;
#else

		float3 worldSpaceVertex = mul(unity_ObjectToWorld, half4(localSpaceVertex, 1)).xyz;
#endif

	WaveStruct wave;
	SampleWaves(worldSpaceVertex.xz, wave);
	worldSpaceVertex += wave.position;

	o.normalInterpolator.xyz = wave.normal;
#ifdef USE_TANGENT
	o.tanInterpolator.xyz = wave.tangent;
	o.binInterpolator.xyz = wave.binormal;
#endif

	half4 screenUV = ComputeScreenPos(TransformWorldToHClip(worldSpaceVertex));
	screenUV.xyz /= screenUV.w;
	
#if defined (_WATERWAVE_ON)
	half4 waterFX = SAMPLE_TEXTURE2D_LOD(_WaterFXMap, sampler_WaterFXMap, screenUV.xy, 0);
	worldSpaceVertex.y += (waterFX.w * 2 - 1) ;
#endif
	o.pos = mul(UNITY_MATRIX_VP, float4(worldSpaceVertex, 1.0));

	o.screenPos = ComputeScreenPos(o.pos);

	o.screenPos.z = lerp(saturate(_FoamPeak.y * wave.position.y), exp2((wave.position.y - length(wave.position.xz) * _FoamPeak.w) * _FoamPeak.z), _FoamPeak.x);

	float2 tileableUv = worldSpaceVertex.xz;
	float2 tileableUvScale = tileableUv * _InvNeoScale;;
	o.bumpCoords.xyzw = float4(tileableUvScale, tileableUv);

	o.worldPos.xyz = worldSpaceVertex;
	o.worldPos.w = ComputeFogFactor(o.pos.z);

	return o;
}

bool IsUnderwater(const float facing)
{
	const bool backface = facing < 0.0;
	return backface;
}

half4 frag_MQ(v2f_MQ i, float facing : VFACE) : SV_Target
{
	half2 ior = (i.screenPos.xy) / i.screenPos.w;

	bool underwater = IsUnderwater(facing);

	// get tangent space basis    	
	half2 slope = 0;
	half4 c = tex2D(_Map0, i.bumpCoords.xy);
	slope += c.xy;
	slope += c.zw;
	
#if defined (_WATERWAVE_ON)
	half4 waterFX = SAMPLE_TEXTURE2D(_WaterFXMap, sampler_WaterFXMap, ior);
	slope += half2(1 - waterFX.y, 1 - waterFX.z) - 0.5;
#endif
	half3 worldNormal = (half3(-slope.x, NORMAL_POWER, -slope.y)); //shallow normal
	half3 worldNormal2 = (half3(-slope.x, NORMAL_SHARPBIAS, -slope.y)); //sharp normal

#if defined (_WATERWAVE_ON)
	half k = 0;
	half2 uv = 1 - (i.bumpCoords.zw - _WaveCoord.xy) * _WaveCoord.zw;
	half2 ba = tex2D(_WaveTex, uv).rg;
	ba *= step(0, uv.x);
	ba *= step(uv.x, 1);
	ba *= step(0, uv.y);
	ba *= step(uv.y, 1);

	k = length(ba.xy);

	float3 T0 = (float3(0, ba.x, 1));
	float3 B0 = (float3(1, ba.y, 0));
	float3 N0 = (float3(-ba.x, 1, -ba.y));

	half3x3 m0;
	m0[0] = B0;
	m0[1] = N0;
	m0[2] = T0;

	worldNormal = (mul(m0, worldNormal));
	worldNormal2 = (mul(m0, worldNormal2));

#endif

#ifdef USE_TANGENT

	float3 T = i.tanInterpolator.xyz;
	float3 B = i.binInterpolator.xyz;
	float3 N = i.normalInterpolator.xyz;

	half3x3 m;
	m[0] = B;
	m[1] = N;
	m[2] = T;

	worldNormal = (mul(worldNormal, m));
	worldNormal2 = (mul(worldNormal2, m));
#else
	//UDN
	 worldNormal = normalize(worldNormal + i.normalInterpolator.xyz); //shallow normal
	 worldNormal2 = normalize(worldNormal2 + i.normalInterpolator.xyz); //sharp normal
#endif

	float3 viewVector = (_WorldSpaceCameraPos - i.worldPos.xyz);
	float fade = Fade(viewVector);
	viewVector = normalize(viewVector);

	// shading for fresnel 
	worldNormal = normalize(lerp(WORLD_UP, worldNormal, fade));
	worldNormal2 = normalize(lerp(worldNormal2, WORLD_UP, 0));

	if (underwater)
	{
		worldNormal = -worldNormal;
		worldNormal2 = -worldNormal2;
	}

	half4 rtReflections;
	if (!underwater)
		rtReflections = tex2D(_PlanarReflectionTexture, ior + lerp(0, worldNormal.xz * REALTIME_DISTORTION, fade));
	else
		rtReflections = _ShallowColor;

	half dotNV = saturate(dot(viewVector, worldNormal));

	half fresnelPow = 5;
	if (underwater)
	{
		fresnelPow = 1;
	}

	float fresnel = pow(1 - dotNV, fresnelPow);

	half fresnelFac = saturate(_Fresnel + (1 - _Fresnel) * fresnel);

	if (underwater)
	{
		fresnelFac = 1 - fresnelFac;
	}

#if defined(_SSREFLECTION_ON)
	half3 reflectVector = normalize(reflect(-viewVector, normalize(lerp(WORLD_UP, worldNormal, REALTIME_DISTORTION * fade * 2))));
	half4 SSReflections = GetSSRLighting(ior, fresnelFac, i.worldPos.xyz, reflectVector);
	rtReflections = lerp(lerp(rtReflections, SSReflections, SSReflections.a), SSReflections, SSReflections.a > 0.99);
#endif

	// base, depth & reflection colors
	half4 baseColor = _BaseColor;

	half2 refrCoord = (i.screenPos.xy) / i.screenPos.w + worldNormal.xz * LERPREFL;

	float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, refrCoord), _ZBufferParams);
	depth = depth - i.screenPos.w;

	half4 shallowColor = _ShallowColor;

	half4 reflectionColor = rtReflections;

#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
	half shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.worldPos.xyz));
#else
	half shadow = 1;
#endif

	shadow = lerp(1 - _Shadow, 1, shadow);

	half4 refractions = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrCoord);

	float edge = exp2(-_AboveDepth * depth);

	baseColor = lerp(baseColor, lerp(shallowColor, refractions, pow(edge, _ShallowEdge)), edge);
	
	float3 Dir = normalize(i.worldPos.xyz - _WorldLightPos.xyz);
	float spec = GGXSpecularDir(viewVector, worldNormal2, Dir);

	// to get an effect when you see through the material
	// hard coded pow constant
	float phase = abs(dot(viewVector, normalize(lerp(i.normalInterpolator.xyz, WORLD_UP, 0.75) + Dir))) * 0.5 + 0.5;
	float4 InScatter = phase * phase * lerp(0.5, 1, edge);
	baseColor += InScatter * shallowColor;

	baseColor = lerp(baseColor, reflectionColor, fresnelFac ) * shadow;

	baseColor += _SpecularColor * spec * lerp(fade, shadow, 0.5) / max(edge, 0.1);

	half height = i.screenPos.z;
	half3 foamMap = SAMPLE_TEXTURE2D(_FoamMask, sampler_FoamMask, i.bumpCoords.xy * _FoamMask_ST.xy + worldNormal.xz * _Foam.w * fresnelFac * height).rgb;
#if defined (_WATERWAVE_ON)
	half fxFoam = max(length(waterFX.a - 0.5) * foamMap.g * 10 * edge, max(waterFX.r, k) * foamMap.r) * 2;
#else
	half fxFoam = foamMap.g * edge;
#endif
	half shoreDepth = exp2(-_Foam.y * depth);
	float maxInt = saturate(max(shoreDepth * (1 - fresnelFac), height));
	half shoreFoam = (sin(_WaveTime * _FoamMask_ST.z + maxInt * _FoamMask_ST.w) * _Foam.z + 1) * maxInt * foamMap.b;
	half peakFoam = height * foamMap.r * phase;

    baseColor += min(max(max(fxFoam.rrrr, peakFoam.rrrr), shoreFoam.rrrr) * _Foam.x * fade, 2) * shadow;

	baseColor.rgb = MixFog(baseColor.rgb, i.worldPos.w);

	if (underwater)
	{
		edge *= edge;
		fresnelFac *= fade;
		fresnelFac *= fresnelFac;
		edge += fresnelFac;
	}

	baseColor = lerp(baseColor, refractions, saturate(edge));

	return half4(clamp(baseColor.rgb, 0, MaxLitValue), 1);
}


#endif
