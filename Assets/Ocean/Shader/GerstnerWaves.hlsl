#ifndef GERSTNER_WAVES_INCLUDED
#define GERSTNER_WAVES_INCLUDED

#define USE_TANGENT

float _WaveTime;
float _Choppiness;

#define _WaveCount 20 // how many waves, set via the water component

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

WaveStruct GerstnerWave(half2 pos, float waveCountMulti, half amplitude, half direction, half wavelength)
{
	WaveStruct waveOut;

	////////////////////////////////wave value calculations//////////////////////////
	half3 wave = 0; // wave vector
	half w = 6.28318 / wavelength; // 2pi over wavelength(hardcoded)
	half wSpeed = sqrt(9.8 * w); // frequency of the wave based off wavelength
	half peak = _Choppiness; // peak value, 1 is the sharpest peaks
	half qia = peak / (w * _WaveCount);
	half qiwa = peak / (_WaveCount);

	direction = radians(direction); // convert the incoming degrees to radians, for directional waves
	half2 dirWaveInput = half2(sin(direction), cos(direction));

	half2 windDir = normalize(dirWaveInput); // calculate wind direction
	half dir = dot(windDir, pos); // calculate a gradient along the wind direction

	////////////////////////////position output calculations/////////////////////////
	half calc = dir * w + -_WaveTime * wSpeed; // the wave calculation
	half cosCalc = cos(calc); // cosine version(used for horizontal undulation)
	half sinCalc = sin(calc); // sin version(used for vertical undulation)

	// calculate the offsets for the current point
	wave.xz = qia * windDir.xy * cosCalc;
	wave.y = ((sinCalc * amplitude)) * waveCountMulti;// the height is divided by the number of waves
	
	////////////////////////////normal output calculations/////////////////////////
	half wa = w * amplitude;

	// normal vector
	half3 n = half3(-(windDir.xy * wa * cosCalc), 1-(qiwa * sinCalc));
	n = n.xzy;

	half Jxx = 1 - (qiwa * windDir.x * windDir.x * sinCalc);
	half Jyy = 1 - (qiwa * windDir.y * windDir.y * sinCalc);
	half Jxy = -(qiwa * windDir.x * windDir.y * sinCalc);

	waveOut.da = (Jxx * Jyy - Jxy * Jxy); // determinant(float2x2(Jxx, Jxy, Jxy, Jyy))
	waveOut.da = (1 - waveOut.da) * waveCountMulti;

#ifdef USE_TANGENT
	// tangent vector
	half3 t = half3(Jxy, (windDir.y * wa * cosCalc), Jyy);

	// binormal vector
	half3 b = half3(Jxx, (windDir.x * wa * cosCalc), Jxy);
#endif

	////////////////////////////////assign to output///////////////////////////////
	waveOut.position = wave * saturate(amplitude * 10000);
	waveOut.normal = (n * waveCountMulti);

#ifdef USE_TANGENT
	waveOut.tangent = (t * waveCountMulti);
	waveOut.binormal = (b * waveCountMulti);
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
        						waveCountMulti, 
        						waveData[i].x, 
        						waveData[i].y, 
        						waveData[i].z); // calculate the wave
		waveOut.position += waves[i].position; // add the position
		waveOut.normal += waves[i].normal; // add the normal
		waveOut.da += waves[i].da; // add the da
#ifdef USE_TANGENT
		waveOut.tangent += waves[i].tangent; // add the tangent
		waveOut.binormal += waves[i].binormal; // add the binormal
#endif
	}
}

half3 GerstnerWavePos(half2 pos, float waveCountMulti, half amplitude, half direction, half wavelength)
{
	////////////////////////////////wave value calculations//////////////////////////
	half3 wave = 0; // wave vector
	half w = 6.28318 / wavelength; // 2pi over wavelength(hardcoded)
	half wSpeed = sqrt(9.8 * w); // frequency of the wave based off wavelength
	half peak = _Choppiness; // peak value, 1 is the sharpest peaks
	half qia = peak / (w * _WaveCount);

	direction = radians(direction); // convert the incoming degrees to radians, for directional waves
	half2 dirWaveInput = half2(sin(direction), cos(direction));

	half2 windDir = normalize(dirWaveInput); // calculate wind direction
	half dir = dot(windDir, pos); // calculate a gradient along the wind direction

	////////////////////////////position output calculations/////////////////////////
	half calc = dir * w + -_WaveTime * wSpeed; // the wave calculation
	half cosCalc = cos(calc); // cosine version(used for horizontal undulation)
	half sinCalc = sin(calc); // sin version(used for vertical undulation)

	// calculate the offsets for the current point
	wave.xz = qia * windDir.xy * cosCalc;
	wave.y = ((sinCalc * amplitude)) * waveCountMulti;// the height is divided by the number of waves

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
			waveCountMulti,
			waveData[i].x,
			waveData[i].y,
			waveData[i].z); // calculate the wave

		waveOut += waves[i]; // add the position
	}
}

#endif // GERSTNER_WAVES_INCLUDED