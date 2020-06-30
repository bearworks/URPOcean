

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

#define MaxLitValue 4

half _InvNeoScale;

// colors in use
half4 _SpecularColor;
half4 _BaseColor;
half4 _ShallowColor;
half4 _ReflectionColor;

// edge & shore fading
half _AboveDepth;
half _ShallowEdge;	
float _Fade;

// specularity
float _Shininess;
half _SunIntensity;
#if defined (_PIXELFORCES_ON)
half4 _WorldLightPos;
#else
half4 _WorldLightDir;
#endif

	
// fresnel, vertex & bump displacements & strength
float4 _DistortParams; // need float precision
half _FresnelScale;	

sampler2D _FresnelLookUp;

// shortcuts
#define LERPREFL _DistortParams.x
#define REALTIME_DISTORTION _DistortParams.y
#define NORMAL_POWER _DistortParams.z
#define NORMAL_SHARPBIAS _DistortParams.w
#define BUMP_POWER _DistortParams.z
#define BUMP_SHARPBIAS _DistortParams.w
#define WORLD_UP half3(0,1,0)

#define FO_TANGENTSPACE \
	half3x3 m; \
    m[0] = B; \
    m[1] = N; \
    m[2] = T; \

#if defined (_PIXELFORCES_ON)

inline float NeoGGXTerm(float NdotH, float roughness)
{
	float a = roughness * roughness;
	float ta = a;
	a *= a;
	//on some gpus need float precision
	float d = NdotH * NdotH * (a - 1.f) + 1.f;
	return ta / max(UNITY_PI * (d), 1e-7f);
}

inline float PhongSpecularDir(float3 V, float3 N, float3 Dir)
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
	return clamp(NeoGGXTerm(nh, _Shininess), 0, MaxLitValue) * _SunIntensity;
#endif
}

inline float BlinnPhongSpecularDir(float3 V, float3 N, float3 Dir)
{
	float3 h = normalize(V - Dir);
	float nh = max(0, dot(N, h));
	return clamp(pow(nh, _Shininess), 0, MaxLitValue) * _SunIntensity;
}

#else

inline float NeoGGXTerm(float NdotH, float roughness)
{
	float a = roughness * roughness;
	a *= a;
	//on some gpus need float precision
	float d = NdotH * NdotH * (a - 1.f) + 1.f;
	return a / (UNITY_PI * d * d + 1e-7f);
}

inline half GGXPhongSpecular(float3 V, float3 N)
{
	float3 h = normalize(-_WorldLightDir.xyz + V);
	float nh = 1 - dot(N, h);
	return clamp(NeoGGXTerm(nh, _Shininess), 0, MaxLitValue) * _SunIntensity;
}

inline half PhongSpecular(float3 V, float3 N)
{
	float3 h = reflect(_WorldLightDir.xyz, N);
	float nh = max (0,dot(V, h));
	return clamp(pow (nh, _Shininess), 0, MaxLitValue) * _SunIntensity;
}


inline half BlinnPhongSpecular(float3 V, float3 N)
{
	float3 h = normalize (-_WorldLightDir.xyz + V);
	float nh = max (0,dot(N, h));
	return clamp(pow (nh, _Shininess), 0, MaxLitValue) * _SunIntensity;
}
#endif

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
	float3 viewInterpolator : TEXCOORD0;
	float4 bumpCoords : TEXCOORD1;
	float4 screenPos : TEXCOORD2;
	float3 normalInterpolator : TEXCOORD3;
	float4 shadowCoord : TEXCOORD4;
#ifdef USE_TANGENT
	float3 tanInterpolator : TEXCOORD5;
	float3 binInterpolator : TEXCOORD6;
#endif
};

sampler2D _Map0;
float4 _Map0_TexelSize;

sampler2D _PlanarReflectionTexture;

TEXTURE2D(_CameraDepthTexture); SAMPLER(sampler_CameraDepthTexture);
TEXTURE2D(_CameraOpaqueTexture); SAMPLER(sampler_CameraOpaqueTexture);

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

	o.pos = mul(UNITY_MATRIX_VP, float4(worldSpaceVertex, 1.0));

	o.screenPos = ComputeScreenPos(o.pos);

	float2 tileableUv = worldSpaceVertex.xz;
	float2 tileableUvScale = tileableUv * _InvNeoScale;;
	o.bumpCoords.xyzw = float4(tileableUvScale, tileableUv);

	o.viewInterpolator = worldSpaceVertex;

#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
	o.shadowCoord = TransformWorldToShadowCoord(o.viewInterpolator);
	#else
	o.shadowCoord = float4(0, 0, 0, 0);
#endif

	return o;
}

bool IsUnderwater(const float facing)
{
	const bool backface = facing < 0.0;
	return backface;
}


float Jacobi(float2 uv)
{
	// sample displacement texture and generate foam from it
    float3 dd = float3(_Map0_TexelSize.x, 0.0, _Map0_TexelSize.y);
	half3 s = tex2D(_Map0, uv).xyz;
	half3 sx = tex2D(_Map0, uv + dd.xy).xyz;
	half3 sz = tex2D(_Map0, uv + dd.yx).xyz;
	float3 disp = s.xyz;
	float3 disp_x = dd.zyy + sx.xyz;
	float3 disp_z = dd.yyz + sz.xyz;
	// The determinant of the displacement Jacobian is a good measure for turbulence:
	// > 1: Stretch
	// < 1: Squash
	// < 0: Overlap
	float4 du = float4(disp_x.xz, disp_z.xz) - disp.xzxz;
	float det = (du.x * du.w - du.y * du.z) / (_Map0_TexelSize.x * _Map0_TexelSize.x);

	return saturate(1 - det);
}

half4 frag_MQ(v2f_MQ i, float facing : VFACE) : SV_Target
{
	bool underwater = IsUnderwater(facing);

	// get tangent space basis    	
	half2 slope = 0;
	half4 c = tex2D(_Map0, i.bumpCoords.xy);
	slope += c.xy;
	slope += c.zw;

	half k = 0;

#ifdef USE_TANGENT
	float3 T = i.tanInterpolator.xyz;
	float3 B = i.binInterpolator.xyz;
	float3 N = i.normalInterpolator.xyz;

	FO_TANGENTSPACE
#endif

	half3 worldNormal = (half3(-slope.x, NORMAL_POWER, -slope.y)); //shallow normal
	half3 worldNormal2 = (half3(-slope.x, NORMAL_SHARPBIAS, -slope.y)); //sharp normal

#ifdef USE_TANGENT
	worldNormal = (mul(worldNormal, m));
	worldNormal2 = (mul(worldNormal2, m));
#else
	//UDN
	 worldNormal = normalize(worldNormal + i.normalInterpolator.xyz); //shallow normal
	 worldNormal2 = normalize(worldNormal2 + i.normalInterpolator.xyz); //sharp normal
#endif


	float3 viewVector = (_WorldSpaceCameraPos - i.viewInterpolator.xyz);
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
		rtReflections = tex2D(_PlanarReflectionTexture, i.screenPos.xy / i.screenPos.w + lerp(0, worldNormal.xz * REALTIME_DISTORTION, fade));
	else
		rtReflections = _ShallowColor;


	half dotNV = saturate(dot(viewVector, worldNormal));
	// base, depth & reflection colors
	half4 baseColor = _BaseColor;

	half2 refrCoord = (i.screenPos.xy) / i.screenPos.w + worldNormal.xz * LERPREFL;

	float depth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, sampler_CameraDepthTexture, refrCoord), _ZBufferParams);
	depth = depth - i.screenPos.w + 0;

	half4 shallowColor = _ShallowColor;

	half4 reflectionColor = rtReflections;

	float fresnel = pow(1 - dotNV, 5);

	half4 fresnelFac = baseColor + (1 - baseColor) * fresnel;

	if (underwater)
	{
		fresnelFac = 1 - fresnelFac;
	}

#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
	half shadow = MainLightRealtimeShadow(i.shadowCoord);
#else
	half shadow = 1;
#endif

	baseColor = lerp(baseColor * shadow, reflectionColor, fresnelFac );

	float edge = saturate(_ShallowEdge * depth);
	baseColor = lerp(shallowColor, baseColor, edge);

#if defined (_PIXELFORCES_ON)
		float3 Dir = normalize(i.viewInterpolator.xyz - _WorldLightPos);
		float spec = PhongSpecularDir(viewVector, worldNormal2, Dir);

		// to get an effect when you see through the material
		// hard coded pow constant
		float InScatter = pow(saturate(dot(viewVector, normalize(worldNormal + Dir))), 2) * lerp(3, .1f, edge);
		baseColor += InScatter * shallowColor;
#else
		half spec = PhongSpecular(viewVector, worldNormal2);
#endif

	float alpha = saturate(_AboveDepth * depth) * saturate(_BaseColor.a + dotNV);

	baseColor += spec * lerp(_SpecularColor * fade, shadow, 0.5) / max(alpha, 0.1);

	half4 refractions = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, refrCoord);

	baseColor = lerp(refractions, baseColor, alpha);

	return half4(clamp(baseColor.rgb, 0, 48),saturate(baseColor.a));
}


#endif
