using UnityEngine;
using UnityEngine.Rendering.Universal;
using System;
using System.Collections;
using System.Collections.Generic;

namespace NOcean
{
	[Serializable]
	public class NeoFFTParameters
	{
        public eFFTResolution fftresolution = eFFTResolution.eFFT_NeoMedium;

	    public float worldfftSize = 20;
        [Range(1, 10)]
	    public float windSpeed = 8.0f; //A higher wind speed gives greater swell to the waves
        [Range(0.01f, 10)]
	    public float waveAmp = 1.0f; //Scales the height of the waves
        [Range(0.01f, 1)]
	    public const float Omega = 0.84f;//A lower number means the waves last longer and will build up larger waves

        public Vector2 distort = Vector2.one;//A lower number means the waves last longer and will build up larger waves
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    [ExecuteInEditMode]
    public class NeoNormalGrid : MonoBehaviour
    {
        [Range(0, 1)]
        public float uniWaveDirFlow = 0.7f;

        [Range(0.1f, 10)]
        public float uniWaveSpeed = 1.0f; //Scales the speed of all waves
        [Range(0.5f, 2f)]
        public const float uniWaveScale = 0.5f; //small waves Scale

        public NeoFFTParameters fftParam = null;
        private bool usemips = true;
        /// <summary>
        /// fft
        /// </summary>
        protected int m_fftresolution = 128;
        protected int m_anisoLevel = 2;
        protected float m_offset;
        protected float m_worldfftSize = 20;
        protected Vector2 m_inverseWorldSizes;
        protected float m_choppiness = 1.6f;
        protected float m_windSpeed = 8.0f;
        protected float m_waveAngle = 90f;
        protected float m_waveAmp = 1.0f;
        //float m_Omega = 0.84f;
        protected float m_waveDirFlow = 0.0f;

        const float twoPI = 2f * Mathf.PI;

        protected RenderTexture m_spectrum01;
        protected RenderTexture[] m_fourierBuffer0;
        protected RenderTexture m_map0;

        protected LinkedListNode<RenderTexture> m_queueNode = null;
        protected LinkedList<RenderTexture> m_queueRTs = new LinkedList<RenderTexture>();

        public Material oceanMaterial = null;

        const float WAVE_KM = 370.0f;
        const float WAVE_CM = 0.23f;

        float Sqr(float x) { return x * x; }

        //Gravity Wave Dispersion Relations
        //http://graphics.ucsd.edu/courses/rendering/2005/jdewall/tessendorf.pdf
        //ω^2(k) = gk(1 + k^2 * L^2)
        float Dispersion(float k) { return Mathf.Sqrt(9.80665f * k * (1.0f + Sqr(k / WAVE_KM))); }


        protected void GenBuffer()
        {

            m_queueRTs.Clear();

            supportRT = NeoOcean.instance.supportRT;
            if (!NeoOcean.instance.supportRT)
                return;

            RenderTextureFormat mapFormat = RenderTextureFormat.ARGBHalf;

            m_passes = (int)(Mathf.Log(m_fftresolution) / Mathf.Log(2.0f));
            m_butterflyLookupTable = new Texture2D[m_passes];

			m_map0 = new RenderTexture(m_fftresolution, m_fftresolution, 0, mapFormat, QualitySettings.activeColorSpace == ColorSpace.Linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
			m_map0.filterMode = FilterMode.Trilinear;
			m_map0.wrapMode = TextureWrapMode.Repeat;
			m_map0.anisoLevel = m_anisoLevel;
            m_map0.autoGenerateMips = usemips;
            //m_map0.useMipMap = usemips; //bug on Some cards
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

        protected void RelBuffer()
        {
            RenderTexture.active = null;

            if (m_map0 != null)
                m_map0.Release();

            if (m_spectrum01 != null)
                m_spectrum01.Release();

            DestroyImmediate(m_map0);

            m_map0 = null;

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

        void Start()
        {
            Init();
        }

        protected virtual void OnEnable()
        {
            GetComponent<Renderer>().enabled = true;
        }

        protected virtual void OnDisable()
        {
            GetComponent<Renderer>().enabled = false;
        }

        protected eFFTResolution GetFFTResolution()
        {
            return  fftParam.fftresolution;
        }

        protected virtual void Init()
        {
            if (NeoOcean.instance == null)
                return;

            GetComponent<Renderer>().sharedMaterial = oceanMaterial;
            GetComponent<Renderer>().enabled = true;

            m_fftresolution = (int)GetFFTResolution();

            m_worldfftSize = fftParam.worldfftSize;
            
            m_windSpeed = fftParam.windSpeed;

            m_waveAmp = fftParam.waveAmp;
            //m_Omega = fftParam.Omega;
            m_waveDirFlow = uniWaveDirFlow;

            m_inverseWorldSizes = INVERSEV(fftParam.worldfftSize);

            GenBuffer();

            NeoOcean.instance.AddPG(this);

        }

        protected virtual void OnDestroy()
        {
            RelBuffer();

            GetComponent<Renderer>().enabled = false;

            if (NeoOcean.instance != null)
                NeoOcean.instance.RemovePG(this);

        }

        bool bChangeBuffer = false;
        
        public void ForceReload(bool bReGen)
        {
            if (NeoOcean.instance == null)
                return;

            if (bReGen)
            {
                RelBuffer();
                GenBuffer();
            }

            bChangeBuffer = true;
        }

        //public bool willRender = false;
        
        public virtual void LateUpdate()
        {
            Camera cam = Camera.main;

            if (cam == null)
                return;

            if (NeoOcean.instance == null)
                return;

            NeoOcean.oceanheight = this.transform.position.y;

#if UNITY_EDITOR
            NeoOcean.instance.AddPG(this);
#endif
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

            
            //float pTimeA = twoPI / Dispersion(twoPI * m_inverseWorldSizes.y);
            //use ωt' = ωt - (int)(ωt/(2pi)) * 2pi to wrap the period of sin waves,
            //solve the common measure period of fft waves
            _ScaledTime = Mathf.PingPong(Time.time * uniWaveSpeed, 1e4f);

            oceanMaterial.DisableKeyword("_PROJECTED_ON");

            PhysicsUpdate();
        }
        
        bool supportRT = false;
        protected void PhysicsUpdate()
        {
            if (NeoOcean.instance == null)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            if (!cam.gameObject.activeSelf)
                return;

            if (supportRT != NeoOcean.instance.supportRT)
                ForceReload(true);

            supportRT = NeoOcean.instance.supportRT;

            if (!NeoOcean.instance.supportRT)
                return;

            CheckParams();

            if (NeoOcean.instance.matSpectrum_l == null)
                return;

            if (bChangeBuffer)
            {

	            //Creates all the data needed to generate the waves.
	            //This is not called in the constructor because greater control
	            //over when exactly this data is created is needed.
	            GenerateWavesSpectrum();

                NeoOcean.instance.matSpectrum_l.SetTexture("_Spectrum01", m_spectrum01);
                
                bChangeBuffer = false;
            }

            if(m_fourierBuffer0.Length == 0)
            {
                ForceReload(true);
                return;
            }

            int count = 2;
            count = Time.frameCount % count;
            if (count == 0)
            {
                InitWaveSpectrum(_ScaledTime);
            }

           PeformFFT(m_fourierBuffer0, count);
            
            if (count == 1)
            { 
                NeoOcean.Blit(m_fourierBuffer0[1], m_map0, null);
            }


        }


        // Update is called once per frame
        public void SetupMaterial()
        {
            Camera cam = Camera.main;

            if (cam == null)
                return;

            if (NeoOcean.instance == null)
                return;

            if (oceanMaterial == null)
                return;

            float scale = (uniWaveScale * fftParam.worldfftSize);
            oceanMaterial.SetFloat("_InvNeoScale", 1f / scale);

            oceanMaterial.SetTexture("_Map0", m_map0);

#if UNITY_EDITOR
            GetComponent<Renderer>().hideFlags = HideFlags.HideInInspector;
#endif
            GetComponent<Renderer>().sharedMaterial = oceanMaterial;
        }

        public bool debug = false;

#if UNITY_EDITOR
        public void OnGUI()
        {
            if (debug)
            {
                if (m_map0 != null)
                    GUI.DrawTexture(new Rect(0, 0, m_map0.width * 2, m_map0.height * 2), m_map0, ScaleMode.ScaleToFit, false);

            }
        }
#endif

        void InitWaveSpectrum(float t)
        {
            float factor = twoPI * m_fftresolution;
            NeoOcean.instance.matSpectrum_l.SetTexture("_Spectrum01", m_spectrum01);
            NeoOcean.instance.matSpectrum_l.SetVector("_Offset", m_offset * fftParam.distort);
            NeoOcean.instance.matSpectrum_l.SetVector("_InverseGridSizes", m_inverseWorldSizes * factor);
            NeoOcean.instance.matSpectrum_l.SetFloat("_T", t);

            NeoOcean.Blit(null, m_fourierBuffer0[1], NeoOcean.instance.matSpectrum_l, 0);
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


        void GenerateWavesSpectrum()
        {
            NeoOcean.instance.matIspectrum.SetFloat("Omega", NeoFFTParameters.Omega);
            NeoOcean.instance.matIspectrum.SetFloat("windSpeed", fftParam.windSpeed);
            NeoOcean.instance.matIspectrum.SetFloat("waveDirFlow", uniWaveDirFlow);
            NeoOcean.instance.matIspectrum.SetFloat("waveAngle", NeoOcean.instance.basicWaves.direction);
            NeoOcean.instance.matIspectrum.SetFloat("waveAmp", fftParam.waveAmp);
            NeoOcean.instance.matIspectrum.SetFloat("fftresolution", m_fftresolution);
            
            Vector2 twoInvSizes = twoPI * m_inverseWorldSizes;
            Vector4 sampleFFTSize = new Vector4(twoInvSizes.y, twoInvSizes.x, twoInvSizes.y, twoInvSizes.y); 
            NeoOcean.instance.matIspectrum.SetVector("sampleFFTSize", sampleFFTSize);

            NeoOcean.Blit(null, m_spectrum01, NeoOcean.instance.matIspectrum);
        }

        protected float _ScaledTime = 0.0f;
        
        int m_passes;
        [HideInInspector]
        public Texture2D[] m_butterflyLookupTable = null;

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
            Material fouriermat = NeoOcean.instance.matFourier_l;

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


        protected Vector2 INVERSEV(float V)
        {
            const float goldenNum = 2 * 1.61803398875f;
            return new Vector2(1f / (V * goldenNum), 1f / V);
        }


        public virtual void CheckParams()
        {
            if (NeoOcean.instance == null)
                return;

            int fftsize = (int)GetFFTResolution();
            if (m_fftresolution != fftsize)
            {
                m_fftresolution = fftsize;
                ForceReload(true);
                return;
            }

            if (m_worldfftSize != fftParam.worldfftSize)
            {
                fftParam.worldfftSize = Mathf.Max(1f, fftParam.worldfftSize);
                m_inverseWorldSizes = INVERSEV(fftParam.worldfftSize);
                m_worldfftSize = fftParam.worldfftSize;

                ForceReload(false);
                return;
            }

            if (m_windSpeed != fftParam.windSpeed)
            {
                m_windSpeed = fftParam.windSpeed;
                ForceReload(false);
            }
            else if (m_waveAmp != fftParam.waveAmp)
            {
                m_waveAmp = fftParam.waveAmp;
                ForceReload(false);
            }
            //else if (m_Omega != fftParam.Omega)
            //{
            //    fftParam.Omega = Mathf.Clamp01(fftParam.Omega);
            //    m_Omega = fftParam.Omega;
            //    ForceReload(false);
            //}
            else if (m_waveDirFlow != uniWaveDirFlow)
            {
                m_waveDirFlow = uniWaveDirFlow;
                ForceReload(false);
            }
        }
    }
}