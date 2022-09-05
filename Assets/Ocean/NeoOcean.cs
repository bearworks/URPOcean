
#if UNITY_IPHONE || UNITY_ANDROID || UNITY_WP8 || UNITY_BLACKBERRY
#define MOBILE
#endif

using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NOcean
{
    public enum eFFTResolution
    {
        eFFT_NeoLow = 64,
        eFFT_NeoMedium = 128,
    }

    public enum eRWQuality
    {
        eRW_High = 0,
        eRW_Medium = 1,
        eRW_Low = 2,
    }
    
	[Serializable]
	public class NeoEnvParameters
	{
       // public bool useDepth = true;
        public Light sunLight;
	}

    [Serializable]
    public class NeoFFTParameters
    {
        public eFFTResolution fftresolution = eFFTResolution.eFFT_NeoMedium;

        public float worldSize = 20;
        [Range(1, 5)]
        public float windSpeed = 4.0f; //A higher wind speed gives greater swell to the waves
        [Range(0.01f, 2f)]
        public float waveAmp = 1.0f; //Scales the height of the waves
        [Range(0.01f, 1)]
        public const float Omega = 0.84f;//A lower number means the waves last longer and will build up larger waves

        [Range(0, 1)]
        public float waveFlow = 0.7f;

        public Vector2 distort = Vector2.one;//A lower number means the waves last longer and will build up larger waves
    }

    [Serializable]
    public class NeoShaderPack
    {   
        public Shader spectruml_l = null;
        public Shader fourier_l = null;
        public Shader ispectrum = null;
    }

    [DisallowMultipleComponent]
    [ExecuteInEditMode]
    public class NeoOcean : MonoBehaviour
    {
        private NeoReflection reflection;

        public NeoEnvParameters envParam = null;
        public static NeoOcean instance = null;
		
        public static float mainScale = 1f;

        private float amplitude = 0.5f;
        private float direction = 0.1f;
        private float wavelength = 2f;
        private float choppiness = 1.5f;
        private int randomSeed = 0;

        public BasicWaves basicWaves;

        public NeoFFTParameters detailWaves = null;

        [Range(0.0f, 2)]
        public float uniWaveSpeed = 1.0f; //Scales the speed of all waves

        private bool usemips = true;
        /// <summary>
        /// fft
        /// </summary>
        private int m_fftresolution = 128;
        private int m_anisoLevel = 2;
        private float m_offset;
        private float m_worldfftSize = 20;
        private static Vector2 m_inverseWorldSizes;
        private float m_windSpeed = 8.0f;
        private float m_waveAmp = 1.0f;
        private float m_waveDirFlow = 0.0f;

        const float twoPI = 2f * Mathf.PI;

        private RenderTexture m_spectrum01;
        private RenderTexture[] m_fourierBuffer0;
        private RenderTexture m_map0;

        private LinkedListNode<RenderTexture> m_queueNode = null;
        private LinkedList<RenderTexture> m_queueRTs = new LinkedList<RenderTexture>();

        const float WAVE_KM = 370.0f;
        const float WAVE_CM = 0.23f;

        float Sqr(float x) { return x * x; }

        //Gravity Wave Dispersion Relations
        //http://graphics.ucsd.edu/courses/rendering/2005/jdewall/tessendorf.pdf
        //ω^2(k) = gk(1 + k^2 * L^2)
        float Dispersion(float k) { return Mathf.Sqrt(9.80665f * k * (1.0f + Sqr(k / WAVE_KM))); }


        [NonSerialized]
        public Wave[] _waves;

        private NeoNormalGrid mainPGrid;

        private HashSet<NeoNormalGrid> grids = new HashSet<NeoNormalGrid>();

        public NeoShaderPack shaderPack;

        [HideInInspector]
        public Material matSpectrum_l = null;
        [HideInInspector]
        public Material matFourier_l = null;
        [HideInInspector]
        public Material matIspectrum = null;

        const bool needRT = true;

        const float octaveNum = 2;

        [NonSerialized]
        public bool supportRT;
        bool CheckInstance(bool force)
        {
            if (instance == null)
                instance = this;
            else if (force)
                instance = this;

            if (!reflection)
            {
                reflection = transform.GetComponent<NeoReflection>();
            }

            {
                if (reflection)
                    reflection.enabled = (instance == this);
            }

            return instance == this;
        }

        private eFFTResolution GetFFTResolution()
        {
            return detailWaves.fftresolution;
        }

        private void RelBuffer()
        {
            RenderTexture.active = null;

            if (m_map0 != null)
                m_map0.Release();

            if (m_spectrum01 != null)
                m_spectrum01.Release();

            DestroyImmediate(m_map0);

            m_map0 = null;

#if UNITY_EDITOR
            RenderTexture.ReleaseTemporary(m_map1);
            m_map1 = null;
#endif
            DestroyImmediate(m_spectrum01);

            m_spectrum01 = null;

            for (int i = 0; i < 2; i++)
            {
                if (m_fourierBuffer0 != null && i < m_fourierBuffer0.Length && m_fourierBuffer0[i] != null)
                {
                    m_fourierBuffer0[i].Release();
                    DestroyImmediate(m_fourierBuffer0[i]);
                }

            }

            m_fourierBuffer0 = null;


            if (m_butterflyLookupTable != null)
            {
                for (int i = 0; i < m_butterflyLookupTable.Length; i++)
                {
                    Texture2D tex = m_butterflyLookupTable[i];
                    DestroyImmediate(tex);
                }
                m_butterflyLookupTable = null;
            }

            m_queueRTs.Clear();
        }


        Material CreateMaterial(ref Shader shader, string shaderName)
        {
            Material newMat = null;
            if (shader == null)
            {
                Debug.LogWarningFormat("ShaderName: " + shaderName.ToString() + " is missing, would find the shader, then please save NeoOcean prefab.");
                shader = Shader.Find(shaderName);
            }

            if (shader == null)
            {
                Debug.LogError("NeoOcean CreateMaterial Failed.");
                return newMat;
            }

            newMat = new Material(shader);
            newMat.hideFlags = HideFlags.DontSave;

            return newMat;
        }

        public void GenAllMaterial()
        {
            matSpectrum_l = CreateMaterial(ref shaderPack.spectruml_l, "URPOcean/SpectrumFragment_L");
            matFourier_l = CreateMaterial(ref shaderPack.fourier_l, "URPOcean/Fourier_L");
            matIspectrum = CreateMaterial(ref shaderPack.ispectrum, "URPOcean/InitialSpectrum");
        }


        public void DestroyAllMaterial()
        {
            DestroyMaterial(ref matSpectrum_l);
            DestroyMaterial(ref matFourier_l);
            DestroyMaterial(ref matIspectrum);
        }

        int m_passes;
        private Texture2D[] m_butterflyLookupTable = null;

        private void GenBuffer()
        {
            m_queueRTs.Clear();

            if (!supportRT)
                return;

            RenderTextureFormat mapFormat = RenderTextureFormat.ARGB32;

            m_passes = (int)(Mathf.Log(m_fftresolution) / Mathf.Log(2.0f));
            m_butterflyLookupTable = new Texture2D[m_passes];

            m_map0 = new RenderTexture(m_fftresolution, m_fftresolution, 0, mapFormat, QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            m_map0.filterMode = FilterMode.Trilinear;
            m_map0.wrapMode = TextureWrapMode.Repeat;
            m_map0.anisoLevel = m_anisoLevel;
            m_map0.autoGenerateMips = usemips;
            m_map0.useMipMap = usemips; //bug on Some cards
            m_map0.hideFlags = HideFlags.DontSave;
            m_map0.Create();
            m_map0.DiscardContents();

            m_queueRTs.AddLast(m_map0);

            //These textures hold the specturm the fourier transform is performed on
            m_spectrum01 = new RenderTexture(m_fftresolution, m_fftresolution, 0, mapFormat, QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            m_spectrum01.filterMode = FilterMode.Point;
            m_spectrum01.wrapMode = TextureWrapMode.Repeat;
            m_spectrum01.useMipMap = false;
            m_spectrum01.Create();
            m_spectrum01.DiscardContents();
            m_spectrum01.hideFlags = HideFlags.DontSave;
            m_queueRTs.AddLast(m_spectrum01);

            //These textures are used to perform the fourier transform
            m_fourierBuffer0 = new RenderTexture[2];

            CreateBuffer(m_fourierBuffer0, mapFormat);

            ComputeButterflyLookupTable();

            m_offset = 0.5f / m_fftresolution;

            bChangeBuffer = true;
        }

        void GenerateWavesSpectrum()
        {
            matIspectrum.SetFloat("Omega", NeoFFTParameters.Omega);
            matIspectrum.SetFloat("windSpeed", detailWaves.windSpeed);
            matIspectrum.SetFloat("waveDirFlow", detailWaves.waveFlow);
            matIspectrum.SetFloat("waveAngle", basicWaves.direction);
            matIspectrum.SetFloat("waveAmp", detailWaves.waveAmp);
            matIspectrum.SetFloat("fftresolution", m_fftresolution);

            Vector2 twoInvSizes = twoPI * m_inverseWorldSizes;
            Vector4 sampleFFTSize = new Vector4(twoInvSizes.x, twoInvSizes.y, 0, 0);
            matIspectrum.SetVector("sampleFFTSize", sampleFFTSize);

            Blit(null, m_spectrum01, matIspectrum);
        }

        public bool debug = false;

#if UNITY_EDITOR
        private RenderTexture m_map1;
        public void OnGUI()
        {
            if (debug)
            {
                if (m_map1 != null)
                    GUI.DrawTexture(new Rect(0, 0, m_map1.width * 2, m_map1.height * 2), m_map1, ScaleMode.ScaleToFit, false);

            }
        }
#endif

        void InitWaveSpectrum(float t)
        {
            float factor = twoPI * m_fftresolution;
            matSpectrum_l.SetTexture("_Spectrum01", m_spectrum01);
            matSpectrum_l.SetVector("_Offset", m_offset * detailWaves.distort);
            matSpectrum_l.SetVector("_InverseGridSizes", m_inverseWorldSizes * factor);
            matSpectrum_l.SetFloat("_T", t);

            NeoOcean.Blit(null, m_fourierBuffer0[1], matSpectrum_l, 0);
        }

        void CreateBuffer(RenderTexture[] tex, RenderTextureFormat format)
        {
            for (int i = 0; i < 2; i++)
            {
                if (tex[i] != null)
                {
                    continue;
                }

                tex[i] = new RenderTexture(m_fftresolution, m_fftresolution, 0, format, QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
                tex[i].filterMode = FilterMode.Point;
                tex[i].wrapMode = TextureWrapMode.Repeat;
                tex[i].useMipMap = false;
                tex[i].hideFlags = HideFlags.DontSave;
                tex[i].Create();
                tex[i].DiscardContents();
                m_queueRTs.AddLast(tex[i]);
            }
        }

        int BitReverse(int n)
        {
            int nrev = n;  // nrev will store the bit-reversed pattern
            for (int i = 1; i < m_passes; i++)
            {
                n >>= 1; //push
                nrev <<= 1; //pop
                nrev |= n & 1;   //set last bit
            }
            nrev &= (1 << m_passes) - 1;         // clear all bits more significant than N-1

            return nrev;
        }

        //TextureFormat.ARGB32 -> m_fftresolution < 256
        Texture2D Make1DTex()
        {
            Texture2D tex1D = new Texture2D(m_fftresolution, 1, TextureFormat.ARGB32, false, QualitySettings.activeColorSpace == ColorSpace.Linear);
            tex1D.filterMode = FilterMode.Point;
            tex1D.wrapMode = TextureWrapMode.Clamp;
            tex1D.hideFlags = HideFlags.DontSave;
            return tex1D;
        }

        void ComputeButterflyLookupTable()
        {
            for (int i = 0; i < m_passes; i++)
            {
                int nBlocks = (int)Mathf.Pow(2, m_passes - 1 - i);
                int nHInputs = (int)Mathf.Pow(2, i);
                int nInputs = nHInputs << 1;

                m_butterflyLookupTable[i] = Make1DTex();

                for (int j = 0; j < nBlocks; j++)
                {
                    for (int k = 0; k < nHInputs; k++)
                    {
                        int i1, i2, j1, j2;

                        i1 = j * nInputs + k;
                        i2 = i1 + nHInputs;

                        if (i == 0)
                        {
                            j1 = BitReverse(i1);
                            j2 = BitReverse(i2);
                        }
                        else
                        {
                            j1 = i1;
                            j2 = i2;
                        }

                        float weight = (float)(k * nBlocks) / (float)m_fftresolution;
                        //coordinates of the Raster renderbuffer result -0.5 texel, so need shift half texel to sample
                        float uva = ((float)j1 + 0.5f) / m_fftresolution;
                        float uvb = ((float)j2 + 0.5f) / m_fftresolution;

                        m_butterflyLookupTable[i].SetPixel(i1, 0, new Color(uva, uvb, weight, 1));
                        m_butterflyLookupTable[i].SetPixel(i2, 0, new Color(uva, uvb, weight, 0));

                    }
                }

                m_butterflyLookupTable[i].Apply();
            }
        }


        int pj = 0;

        void PeformFFT(RenderTexture[] data0, int c)
        {
            Material fouriermat = matFourier_l;

            RenderTexture pass0 = data0[0];
            RenderTexture pass1 = data0[1];

            if (c == 0)
            {
                pj = 0;

                for (int i = 0; i < m_passes; i++, pj++)
                {
                    int idx = pj % 2;
                    int idx1 = (pj + 1) % 2;

                    fouriermat.SetTexture("_ButterFlyLookUp", m_butterflyLookupTable[i]);

                    fouriermat.SetTexture("_ReadBuffer0", data0[idx1]);

                    if (idx == 0)
                        NeoOcean.Blit(null, pass0, fouriermat, 0);
                    else
                        NeoOcean.Blit(null, pass1, fouriermat, 0);
                }
            }
            else
            {
                for (int i = 0; i < m_passes; i++, pj++)
                {
                    int idx = pj % 2;
                    int idx1 = (pj + 1) % 2;

                    fouriermat.SetTexture("_ButterFlyLookUp", m_butterflyLookupTable[i]);

                    fouriermat.SetTexture("_ReadBuffer0", data0[idx1]);

                    if (idx == 0)
                        NeoOcean.Blit(null, pass0, fouriermat, 1);
                    else
                        NeoOcean.Blit(null, pass1, fouriermat, 1);
                }
            }
        }

        private Vector2 InverseToScale(float V)
        {
            return new Vector2(1f / (V * octaveNum), 1f / V);
        }

        public void ForceReload(bool bReGen)
        {
            if (bReGen)
            {
                RelBuffer();
                GenBuffer();
            }

            bChangeBuffer = true;
        }

        bool bChangeBuffer = false;

        public void CheckRT()
        {
            supportRT = needRT && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth) &&
             SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32);

            if (supportRT)
            {
                supportRT = SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2;
            }
        }

        void Awake()
        {
            if (!CheckInstance(true))
                return;

            CheckRT();

            GenAllMaterial();

            gTime = 0;
        }

        float NormalizeWorldSize()
        {
            detailWaves.worldSize = Mathf.Max(1f, detailWaves.worldSize);
            return detailWaves.worldSize * m_fftresolution / (int)(eFFTResolution.eFFT_NeoMedium);
        }

	    // Use this for initialization
	    void Start()
	    {
            CheckInstance(true);
			
            SetupWaves();

            m_fftresolution = (int)GetFFTResolution();

            m_worldfftSize = NormalizeWorldSize();

            m_windSpeed = detailWaves.windSpeed;

            m_waveAmp = detailWaves.waveAmp;
            //m_Omega = fftParam.Omega;
            m_waveDirFlow = detailWaves.waveFlow;

            m_inverseWorldSizes = InverseToScale(m_worldfftSize);

            GenBuffer();
        }

        void OnEnable()
        {
            CheckInstance(true);
            SetupWaves();
        }

        private NeoNormalGrid FindMainPGrid()
        {
            var _e = grids.GetEnumerator();
            NeoNormalGrid closestPG = null;
            while (_e.MoveNext())
            {
                return _e.Current;
            }

            return closestPG;
        }

        public void CheckParams()
        {
#if UNITY_EDITOR
            if(debug)
               CheckRT();
#endif
            if(amplitude != basicWaves.amplitude ||
               direction != basicWaves.direction ||
               wavelength != basicWaves.wavelength ||
               choppiness != basicWaves.choppiness ||
               randomSeed != basicWaves.randomSeed)
            {
                SetupWaves();

                amplitude = basicWaves.amplitude;
                direction = basicWaves.direction;
                wavelength = basicWaves.wavelength;
                choppiness = basicWaves.choppiness;
                randomSeed = basicWaves.randomSeed;

                ForceReload(false);
            }

            int fftsize = (int)GetFFTResolution();
            if (m_fftresolution != fftsize)
            {
                m_fftresolution = fftsize;
                ForceReload(true);
                return;
            }

            float worldsize = NormalizeWorldSize();
            if (m_worldfftSize != worldsize)
            {
                m_worldfftSize = worldsize;
                m_inverseWorldSizes = InverseToScale(m_worldfftSize);

                ForceReload(false);
                return;
            }

            if (m_windSpeed != detailWaves.windSpeed)
            {
                m_windSpeed = detailWaves.windSpeed;
                ForceReload(false);
            }
            else if (m_waveAmp != detailWaves.waveAmp)
            {
                m_waveAmp = detailWaves.waveAmp;
                ForceReload(false);
            }
            //else if (m_Omega != fftParam.Omega)
            //{
            //    fftParam.Omega = Mathf.Clamp01(fftParam.Omega);
            //    m_Omega = fftParam.Omega;
            //    ForceReload(false);
            //}
            else if (m_waveDirFlow != detailWaves.waveFlow)
            {
                m_waveDirFlow = detailWaves.waveFlow;
                ForceReload(false);
            }
        }

        public static float oceanheight = 0;

        public static float gTime = 0;

        // Update is called once per frame
        void Update()
        {
            var _e = grids.GetEnumerator();
            if (!CheckInstance(false))
            {
                while (_e.MoveNext())
                {
                    _e.Current.enabled = false;
                }

                return;
            }

            while (_e.MoveNext())
            {
                if (_e.Current != null)
                 _e.Current.enabled = _e.Current.gameObject.activeSelf;
            }

            gTime += Mathf.Min(Time.smoothDeltaTime, 1f) * uniWaveSpeed;
            gTime = Mathf.PingPong(gTime, 1e4f);

            CheckParams();

            //find main grid
            mainPGrid = FindMainPGrid();

	        if (mainPGrid != null)
	            UpdateMainGrid();
	    }

        void LateUpdate()
        {
            GerstnerWavesJobs.UpdateHeights();
            if (!CheckInstance(false))
                return;

            mainScale = m_inverseWorldSizes.y;

            var _e = grids.GetEnumerator();
            while (_e.MoveNext())
            {
                _e.Current.SetupMaterial(m_map0, mainScale);
            }

            if (m_queueNode != null)
            {
                if (m_queueNode.Value != null && !m_queueNode.Value.IsCreated())
                {
                    if (Application.isPlaying)
                        ForceReload(true);

                    m_queueNode = null;
                    return;
                }

                m_queueNode = m_queueNode.Next;
            }
            else
                m_queueNode = m_queueRTs.First;

            PhysicsUpdate();
        }

        protected void PhysicsUpdate()
        {
            CheckParams();

            if (matSpectrum_l == null || m_fourierBuffer0 == null)
                return;

            if (bChangeBuffer)
            {

                //Creates all the data needed to generate the waves.
                //This is not called in the constructor because greater control
                //over when exactly this data is created is needed.
                GenerateWavesSpectrum();

                matSpectrum_l.SetTexture("_Spectrum01", m_spectrum01);

                bChangeBuffer = false;
            }

            if (m_fourierBuffer0.Length == 0)
            {
                ForceReload(true);
                return;
            }

            int count = 2;
            count = Time.frameCount % count;
            if (count == 0)
            {
                InitWaveSpectrum(gTime);
            }

            PeformFFT(m_fourierBuffer0, count);

            if (count == 1)
            {
               NeoOcean.Blit(m_fourierBuffer0[1], m_map0, null);

#if UNITY_EDITOR
               if(debug)
               {
                  RenderTexture.ReleaseTemporary(m_map1);
                  m_map1 = RenderTexture.GetTemporary(m_map0.width, m_map0.height, 0, m_map0.format);
                  matFourier_l.SetTexture("_ReadBuffer0", m_map0);
                  NeoOcean.Blit(null, m_map1, matFourier_l, 2);
               }
#endif
            }
        }

        public void SetupWaves()
        {
            //create basic waves based off basic wave settings
            UnityEngine.Random.State backupSeed = UnityEngine.Random.state;
            UnityEngine.Random.InitState(basicWaves.randomSeed);
            float a = basicWaves.amplitude;
            float d = basicWaves.direction;
            float l = basicWaves.wavelength;
            int numWave = BasicWaves.numWaves;
            _waves = new Wave[numWave];

            float r = 1f / numWave;

            for (int i = 0; i < numWave; i++)
            {
                float p = Mathf.Lerp(0.5f, 1.5f, (float)i * r);
                float amp = a * p * UnityEngine.Random.Range(0.8f, 1.2f);
                float dir = d + UnityEngine.Random.Range(-45f, 45f);
                float len = l * p * UnityEngine.Random.Range(0.6f, 1.4f);
                _waves[i] = new Wave(amp, dir, len, basicWaves.choppiness);
                UnityEngine.Random.InitState(basicWaves.randomSeed + i + 1);
            }
            UnityEngine.Random.state = backupSeed;
        }

        Vector4[] waveData = new Vector4[BasicWaves.numWaves];
        public Vector4[] GetWaveData()
        {
            for (int i = 0; i < _waves.Length; i++)
            {
                waveData[i] = new Vector4(_waves[i].amplitude, _waves[i].direction.x, _waves[i].wavelength, _waves[i].direction.y);
            }
            return waveData;
        }

        private void UpdateMainGrid()
	    {
			if (!mainPGrid)
                return;

            UpdateMaterial(mainPGrid.oceanMaterial);

            //GPU side
            Shader.SetGlobalVectorArray("waveData", GetWaveData());
            Shader.SetGlobalFloat("_WaveTime", gTime);
            Shader.SetGlobalFloat("_Choppiness", basicWaves.choppiness);

            //CPU side
            GerstnerWavesJobs.Init();

            CheckDepth(mainPGrid);
	    }

	    public void UpdateMaterial(Material mat)
	    {
            if (mat == null)
                return;

            if (reflection)
            {
                if (reflection.m_settings.m_SSR)
                {
                    mat.EnableKeyword("_SSREFLECTION_ON");
                }
                else
                {
                    mat.DisableKeyword("_SSREFLECTION_ON");
                }
            }

            if (envParam.sunLight != null)
	        {
                if (envParam.sunLight.type == LightType.Point)
                {
                    mat.SetVector("_WorldLightPos", envParam.sunLight.transform.position);
                }
                else
                {
                    mat.SetVector("_WorldLightPos", -envParam.sunLight.transform.forward * 10000);
                }

                mat.SetColor("_SpecularColor", envParam.sunLight.color * envParam.sunLight.intensity);

                //envParam.sunLight.cullingMask = 0;
                //envParam.sunLight.shadows = LightShadows.None;
                //envParam.sunLight.renderMode = LightRenderMode.ForceVertex;
            }
            else if (RenderSettings.sun != null)
            {
                if (RenderSettings.sun.type == LightType.Point)
                {
                    mat.SetVector("_WorldLightPos", RenderSettings.sun.transform.position);
                }
                else
                {
                    mat.SetVector("_WorldLightPos", -RenderSettings.sun.transform.forward * 10000);
                }

                mat.SetColor("_SpecularColor", RenderSettings.sun.color * RenderSettings.sun.intensity);
            }
            else
            {
                mat.SetColor("_SpecularColor", Color.black);
            }

        }

	    public void AddPG(NeoNormalGrid PGrid)
	    {
	        if (PGrid && !grids.Contains(PGrid))
	        {
	            UpdateMaterial(PGrid.oceanMaterial);
	            grids.Add(PGrid);
	        }
	    }

	    public void RemovePG(NeoNormalGrid PGrid)
	    {
	         grids.Remove(PGrid);
	    }

        static Vector2[] blurOffsets = new Vector2[4]; 
        public static void BlurTapCone(RenderTexture src, RenderTexture dst, Material mat, float blurSpread)
        {
#if MOBILE
            if (dst != null)
                dst.DiscardContents();
            else
                return;
#endif
            if (mat == null)
                return;

            float off = 0.1f + blurSpread;
            blurOffsets[0] = new Vector2(-off, -off);
            blurOffsets[1] = new Vector2(-off, off);
            blurOffsets[2] = new Vector2(off, off);
            blurOffsets[3] = new Vector2(off, -off);

            Graphics.BlitMultiTap(src, dst, mat, blurOffsets);
        }

	    public static void Blit(RenderTexture src, RenderTexture dst, Material mat)
	    {
            if (dst != null)
                dst.DiscardContents();
            else
                return;

	        if (mat != null)
	            Graphics.Blit(src, dst, mat);
	        else
	            Graphics.Blit(src, dst);

	    }

	    public static void Blit(RenderTexture src, RenderTexture dst, Material mat, int pass)
	    {
            if (dst != null)
	            dst.DiscardContents();
            else
                return;

	        if (mat != null)
	            Graphics.Blit(src, dst, mat, pass);
	        else
	            Graphics.Blit(src, dst);
	    }
       

	    public void CheckDepth(NeoNormalGrid grid)
	    {

            if(UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null)
            {
                UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.supportsCameraOpaqueTexture = true;
                UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.supportsCameraDepthTexture = true;
            }
        }

		void DestroyMaterial(ref Material mat)
		{
			if(mat != null)
			   DestroyImmediate(mat);

			mat = null;
		}

        void OnDisable()
        {
            GerstnerWavesJobs.Cleanup();

            if (reflection)
                reflection.enabled = false;

            if (instance == this)
                instance = null;
        }

        void OnDestroy()
	    {
            RelBuffer();

            gTime = 0;

            DestroyAllMaterial();

            //if (Application.isPlaying)
            //    GerstnerWavesJobs.Cleanup();

            mainPGrid = null;

            if(instance == this)
               instance = null;
        }

       
    }
}
