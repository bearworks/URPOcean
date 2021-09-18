using UnityEngine;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Mathematics;

namespace NOcean
{
    /// <summary>
    /// This scriptable object contains setting for how the water looks visually
    /// </summary>
    [System.Serializable]
    [CreateAssetMenu(fileName = "WaterSurfaceData", menuName = "WaterSystem/Surface Data", order = 0)]
    public class WaterSurfaceData : ScriptableObject
    {
        public List<Wave> _waves = new List<Wave>();
        public BasicWaves _basicWaveSettings = new BasicWaves(1.5f, 45.0f, 5.0f, 1.5f, 0);
    }

    [System.Serializable]
    public struct Wave
    {
        public float amplitude; // height of the wave in units(m)
        public float2 direction; // direction the wave travels in degrees from Z+
        public float wavelength; // distance between crest>crest
        public float choppiness;

        public Wave(float amp, float dir, float length, float chopp)
        {
            amplitude = amp;

            dir = Mathf.Deg2Rad * dir; // convert the incoming degrees to radians, for directional waves
            direction = new float2(Mathf.Sin(dir), Mathf.Cos(dir));
            wavelength = length;
            choppiness = chopp;
        }
    }

    [System.Serializable]
    public class BasicWaves
    {
        public const int numWaves = 16;
        public float amplitude = 0.5f;
        [Range(0, 360)]
        public float direction = 0.1f;
        public float wavelength = 2f;
        [Range(1, 2)]
        public float choppiness = 1.5f;

        public int randomSeed = 0;

        public BasicWaves()
        {
        }

        public BasicWaves(float amp, float dir, float len, float chopp, int random)
        {
            amplitude = amp;
            direction = dir;
            wavelength = len;
            choppiness = chopp;
            randomSeed = random;
    }
    }

    public static class GerstnerWavesJobs
    {
        //General variables
        public static bool init;
        public static bool firstFrame = true;
        public static bool processing = false;
        static int _waveCount;
        static NativeArray<Wave> waveData; // Wave data from the water system

        //Details for Buoyant Objects
        static NativeArray<float3> positions;
        static int positionCount = 0;
        static NativeArray<float3> wavePos;
        static NativeArray<float3> waveNormal;
        static JobHandle waterHeightHandle;
        static Dictionary<int, int2> registry = new Dictionary<int, int2>();

        static int maxId = 0;
        public static int GenId()
        {
            return maxId++;
        }

        public static void Init()
        {
            if (init)
                return;

            maxId = 0;

            //Wave data
            _waveCount = NeoOcean.instance._waves.Length;
            waveData = new NativeArray<Wave>(_waveCount, Allocator.Persistent);
            for (var i = 0; i < waveData.Length; i++)
            {
                waveData[i] = NeoOcean.instance._waves[i];
            }

            positions = new NativeArray<float3>(4096, Allocator.Persistent);
            wavePos = new NativeArray<float3>(4096, Allocator.Persistent);
            waveNormal = new NativeArray<float3>(4096, Allocator.Persistent);

            init = true;
        }

        public static void Cleanup()
        {
            if (!init)
                return;

            Debug.LogWarning("Cleaning up GerstnerWaves");
            waterHeightHandle.Complete();

            //Cleanup native arrays
            waveData.Dispose();

            positions.Dispose();
            wavePos.Dispose();
            waveNormal.Dispose();

            init = false;

            maxId = 0;
        }

        public static void UpdateSamplePoints(float3[] samplePoints, int guid)
        {
            if (!init)
                return;

            CompleteJobs();
            int2 offsets;
            if (registry.TryGetValue(guid, out offsets))
            {
                for (var i = offsets.x; i < offsets.y; i++)
                {
                    var id = i - offsets.x;
                    if (id >= samplePoints.Length)
                    {
                        continue;
                    }

                    positions[i] = samplePoints[id];
                }
            }
            else
            {
                if (positionCount + samplePoints.Length < positions.Length)
                {
                    offsets = new int2(positionCount, positionCount + samplePoints.Length);
                    //Debug.Log("<color=yellow>Adding Object:" + guid + " to the registry at offset:" + offsets + "</color>");
                    registry.Add(guid, offsets);
                    positionCount += samplePoints.Length;
                }
            }
        }

        public static void GetData(int guid, ref float3[] outPos, ref float3[] outNorm)
        {
            if (!init)
                return;

            var offsets = new int2(0, 0);
            if (registry.TryGetValue(guid, out offsets))
            {
                wavePos.Slice(offsets.x, offsets.y - offsets.x).CopyTo(outPos);
                waveNormal.Slice(offsets.x, offsets.y - offsets.x).CopyTo(outNorm);
            }
        }

        public static void GetData(int guid, ref float3[] outPos)
        {
            if (!init)
                return;

            var offsets = new int2(0, 0);
            if (registry.TryGetValue(guid, out offsets))
            {
                NativeSlice<float3> ws = wavePos.Slice(offsets.x, offsets.y - offsets.x);
                if (ws.Length == outPos.Length)
                    ws.CopyTo(outPos);
            }
        }

        // Height jobs for the next frame
        public static void UpdateHeights()
        {
            if (!init)
                return;

            if (!processing)
            {
                processing = true;

                // Buoyant Object Job
                var waterHeight = new GerstnerWavesJobs.HeightJob()
                {
                    waveData = waveData,
                    position = positions,
                    offsetLength = new int2(0, positions.Length),
                    time = NeoOcean.gTime,
                    outPosition = wavePos,
                    outNormal = waveNormal,
                    normal = 1
                };
                // dependant on job4
                waterHeightHandle = waterHeight.Schedule(positionCount, 32);

                JobHandle.ScheduleBatchedJobs();

                firstFrame = false;
            }
        }

        public static void CompleteJobs()
        {
            if (!firstFrame && processing)
            {
                waterHeightHandle.Complete();
                processing = false;
            }
        }

        // Gerstner Height C# Job
        [BurstCompile]
        public struct HeightJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<Wave> waveData; // wave data stroed in vec4's like the shader version but packed into one
            [ReadOnly]
            public NativeArray<float3> position;

            [WriteOnly]
            public NativeArray<float3> outPosition;
            [WriteOnly]
            public NativeArray<float3> outNormal;

            [ReadOnly]
            public float time;
            [ReadOnly]
            public int2 offsetLength;
            [ReadOnly]
            public int normal;

            // The code actually running on the job
            public void Execute(int i)
            {
                if (i >= offsetLength.x && i < offsetLength.y - offsetLength.x)
                {
                    var waveCountMulti = 1f / waveData.Length;
                    float3 wavePos = new float3(0f, 0f, 0f);
                    float3 waveNorm = new float3(0f, 0f, 0f);

                    for (var wave = 0; wave < waveData.Length; wave++) // for each wave
                    {
                        // Wave data vars
                        var pos = position[i].xz;

                        var amplitude = waveData[wave].amplitude;
                        var direction = waveData[wave].direction;
                        var wavelength = waveData[wave].wavelength;
                        ////////////////////////////////wave value calculations//////////////////////////
                        var w = 6.28318f / wavelength; // 2pi over wavelength(hardcoded)
                        var wSpeed = math.sqrt(9.8f * w); // frequency of the wave based off wavelength
                        var peak = waveData[wave].choppiness; // peak value, 1 is the sharpest peaks
                        var qia = peak / (w * waveData.Length);
                        var qiwa = peak / waveData.Length;

                        var windDir = new float2(0f, 0f);
                        var dir = 0f;

                        var windDirInput = direction; // calculate wind direction 
                        windDir += windDirInput;
                        windDir = math.normalize(windDir);
                        dir = math.dot(windDir, pos); // calculate a gradient along the wind direction

                        ////////////////////////////position output calculations/////////////////////////
                        var calc = dir * w + -time * wSpeed; // the wave calculation

                        var cosCalc = math.cos(calc); // cosine version(used for horizontal undulation)
                        var sinCalc = math.sin(calc); // sin version(used for vertical undulation)

                        float sa = math.clamp(amplitude * 10000, 0, 1);

                        // calculate the offsets for the current point
                        wavePos.x += qia * windDir.x * sa;
                        wavePos.z += qia * windDir.y * sa;
                        wavePos.y += ((sinCalc * amplitude)) * waveCountMulti; // the height is divided by the number of waves 

                        if (normal == 1)
                        {
                            ////////////////////////////normal output calculations/////////////////////////
                            float wa = w * amplitude;
                            // normal vector
                            float3 norm = new float3(-(windDir.xy * wa * cosCalc),
                                            1 - (qiwa * sinCalc));
                            waveNorm += (norm * waveCountMulti) * amplitude;
                        }
                    }

                    outPosition[i] = wavePos;
                    if (normal == 1)
                    {
                        if(waveNorm.x == 0f && waveNorm.y == 0f && waveNorm.z == 0f)
                            outNormal[i] = new float3(0,1,0);
                        else
                            outNormal[i] = math.normalize(waveNorm.xzy);
                    }
                }
            }
        }
    }

}
