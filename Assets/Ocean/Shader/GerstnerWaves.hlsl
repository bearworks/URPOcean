#ifndef GERSTNER_WAVES_INCLUDED
#define GERSTNER_WAVES_INCLUDED

#define USE_TANGENT

float _WaveTime;
float _Choppiness;

#define _WaveCount 16 // how many waves, set via the water component

half4 waveData[_WaveCount]; // 0-9 amplitude, direction, wavelength, omni, 10-19 origin.xy

struct WaveStruct
{
	float3 position;
	float3 normal;
#ifdef USE_TANGENT
	float3 tangent;
	float3 binormal;
#endif
	float da;
};

WaveStruct GerstnerWave(half2 pos, half amplitude, half2 direction, half wavelength)
{
	WaveStruct waveOut;

	////////////////////////////////wave value calculations//////////////////////////
	half3 wave = 0; // wave vector
	half w = 6.28318 / wavelength; // 2pi over wavelength(hardcoded)
	half wSpeed = sqrt(9.8 * w); // frequency of the wave based off wavelength
	half peak = _Choppiness; // peak value, 1 is the sharpest peaks
	half qia = peak / w;

	half2 windDir = direction; // calculate wind direction
	half dir = dot(windDir, pos); // calculate a gradient along the wind direction

	////////////////////////////position output calculations/////////////////////////
	half calc = dir * w + -_WaveTime * wSpeed; // the wave calculation
	half cosCalc = cos(calc); // cosine version(used for horizontal undulation)
	half sinCalc = sin(calc); // sin version(used for vertical undulation)

	// calculate the offsets for the current point
	half2 dirCos = windDir.xy * cosCalc;
	wave.xz = qia * dirCos;
	wave.y = sinCalc * amplitude;// the height is divided by the number of waves
	
	////////////////////////////normal output calculations/////////////////////////
	half qiwaSin = peak * sinCalc;

	// normal vector
	half2 Nxy = -(w * amplitude * dirCos);
	half3 n = half3(Nxy.x, 1 - qiwaSin, Nxy.y);

	half Jxx = 1 - (windDir.x * windDir.x * qiwaSin);
	half Jyy = 1 - (windDir.y * windDir.y * qiwaSin);
	half Jxy = -(windDir.x * windDir.y * qiwaSin);

	waveOut.da = (Jxx * Jyy - Jxy * Jxy); // determinant(float2x2(Jxx, Jxy, Jxy, Jyy))
	waveOut.da = (1 - waveOut.da);

	////////////////////////////////assign to output///////////////////////////////
	waveOut.position = wave * saturate(amplitude * 10000);
	waveOut.normal = n;

#ifdef USE_TANGENT	
	// tangent vector
	half3 t = half3(Jxy, Nxy.y, Jyy);

	// binormal vector
	half3 b = half3(Jxx, Nxy.x, Jxy);

	waveOut.tangent = t;
	waveOut.binormal = b;
#endif

	return waveOut;
}

inline void SampleWaves(float2 position, out WaveStruct waveOut)
{
	half2 pos = position;
	WaveStruct waves[_WaveCount];
	waveOut.position = 0;
	waveOut.normal = 0;
	waveOut.da = 0;
#ifdef USE_TANGENT
	waveOut.tangent = 0;
	waveOut.binormal = 0;
#endif
	half waveCountMulti = 1.0 / _WaveCount;
	
	for(uint i = 0; i < _WaveCount; i++)
	{
		waves[i] = GerstnerWave(pos,
        						waveData[i].x, 
        						waveData[i].yw, 
        						waveData[i].z); // calculate the wave
		waveOut.position += waves[i].position; // add the position
		waveOut.normal += waves[i].normal; // add the normal
		waveOut.da += waves[i].da; // add the da
#ifdef USE_TANGENT
		waveOut.tangent += waves[i].tangent; // add the tangent
		waveOut.binormal += waves[i].binormal; // add the binormal
#endif
	}

		waveOut.position *= waveCountMulti;
		waveOut.normal *= waveCountMulti;
		waveOut.da *= waveCountMulti;
#ifdef USE_TANGENT
		waveOut.tangent *= waveCountMulti;
		waveOut.binormal *= waveCountMulti;
#endif

}

half3 GerstnerWavePos(half2 pos, half amplitude, half2 direction, half wavelength)
{
	////////////////////////////////wave value calculations//////////////////////////
	half3 wave = 0; // wave vector
	half w = 6.28318 / wavelength; // 2pi over wavelength(hardcoded)
	half wSpeed = sqrt(9.8 * w); // frequency of the wave based off wavelength
	half peak = _Choppiness; // peak value, 1 is the sharpest peaks
	half qia = peak / w;

	half2 windDir = direction; // calculate wind direction
	half dir = dot(windDir, pos); // calculate a gradient along the wind direction

	////////////////////////////position output calculations/////////////////////////
	half calc = dir * w + -_WaveTime * wSpeed; // the wave calculation
	half cosCalc = cos(calc); // cosine version(used for horizontal undulation)
	half sinCalc = sin(calc); // sin version(used for vertical undulation)

	// calculate the offsets for the current point
	wave.xz = qia * windDir.xy * cosCalc;
	wave.y = ((sinCalc * amplitude));// the height is divided by the number of waves

	return wave * saturate(amplitude * 10000);
}

inline void SampleWavesPos(float2 position, out float3 waveOut)
{
	half2 pos = position;
	float3 waves[_WaveCount];
	half waveCountMulti = 1.0 / _WaveCount;
	waveOut = 0;
	for (uint i = 0; i < _WaveCount; i++)
	{
		waves[i] = GerstnerWavePos(pos,
			waveData[i].x,
			waveData[i].yw,
			waveData[i].z); // calculate the wave

		waveOut += waves[i]; // add the position
	}
	waveOut *= waveCountMulti;
}

#endif // GERSTNER_WAVES_INCLUDED