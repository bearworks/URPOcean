
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
       // eFFT_NeoSmall = 16,
        eFFT_NeoLow = 64,
        eFFT_NeoMedium = 128,
      //  eFFT_NeoLarge = 128,
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

        private float amplitude = 0.5f;
        private float direction = 0.1f;
        private float wavelength = 2f;

        public BasicWaves basicWaves;

        [NonSerialized]
        public Wave[] _waves;

        public int randomSeed = 0;

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
            matSpectrum_l = CreateMaterial(ref shaderPack.spectruml_l, "NeoOcean/SpectrumFragment_L");
            matFourier_l = CreateMaterial(ref shaderPack.fourier_l, "NeoOcean/Fourier_L");
            matIspectrum = CreateMaterial(ref shaderPack.ispectrum, "NeoOcean/InitialSpectrum");
        }


        public void DestroyAllMaterial()
        {
            DestroyMaterial(ref matSpectrum_l);
            DestroyMaterial(ref matFourier_l);
            DestroyMaterial(ref matIspectrum);
        }

        public void CheckRT()
        {
            supportRT = needRT && SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.Depth) && 
             SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf);

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


	    // Use this for initialization
	    void Start()
	    {
            CheckInstance(true);
            SetupWaves();
        }

        void OnEnable()
        {
            CheckInstance(true);
            SetupWaves();
        }

        public NeoNormalGrid mainPG
        {
            get
            {
                return mainPGrid;
            }
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
            CheckRT();
#endif
            if(amplitude != basicWaves.amplitude ||
               direction != basicWaves.direction ||
               wavelength != basicWaves.wavelength)
            {
                SetupWaves();
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

            Camera cam = Camera.main;

            if (cam == null)
	            return;

            if (!cam.gameObject.activeSelf)
                return;

            gTime += Mathf.Min(Time.smoothDeltaTime, 1f);
            float rTime = (float)(gTime / 20f);
            Shader.SetGlobalFloat("_NeoGlobalTime", rTime - 4f);

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

            var _e = grids.GetEnumerator();
            while (_e.MoveNext())
            {
                _e.Current.SetupMaterial();
            }
        }

        public float UpdateCameraPlane(NeoNormalGrid pgrid, float fAdd)
        {
            Camera cam = Camera.main;

            return Mathf.Max(cam.nearClipPlane, cam.farClipPlane + fAdd);
        }

        public void SetupWaves()
        {
            //create basic waves based off basic wave settings
            UnityEngine.Random.State backupSeed = UnityEngine.Random.state;
            UnityEngine.Random.InitState(randomSeed);
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
                _waves[i] = new Wave(amp, dir, len);
                UnityEngine.Random.InitState(randomSeed + i + 1);
            }
            UnityEngine.Random.state = backupSeed;
        }

        public Vector4[] GetWaveData()
        {
            Vector4[] waveData = new Vector4[20];
            for (int i = 0; i < _waves.Length; i++)
            {
                waveData[i] = new Vector4(_waves[i].amplitude, _waves[i].direction, _waves[i].wavelength, 0f);
            }
            return waveData;
        }

        private void UpdateMainGrid()
	    {
			if (!mainPGrid.oceanMaterial)
                return;

            //Physics.gravity = -Vector3.up * g;
            UpdateMaterial(mainPGrid, mainPGrid.oceanMaterial, true);

            //GPU side
            Shader.SetGlobalVectorArray("waveData", GetWaveData());
            Shader.SetGlobalFloat("_WaveTime", gTime);

            //CPU side
            GerstnerWavesJobs.Init();

            CheckDepth(mainPGrid);
	    }

	    public void UpdateMaterial(NeoNormalGrid pgrid, Material mat, bool main)
	    {
            if (mat == null)
                return;

            if (envParam.sunLight != null)
	        {
                if(mat.IsKeywordEnabled("_PIXELFORCES_ON"))
                   mat.SetVector("_WorldLightPos", envParam.sunLight.transform.position);
                else
                   mat.SetVector("_WorldLightDir", envParam.sunLight.transform.forward);
                
                mat.SetColor("_SpecularColor", envParam.sunLight.color);

                //envParam.sunLight.cullingMask = 0;
                //envParam.sunLight.shadows = LightShadows.None;
                //envParam.sunLight.renderMode = LightRenderMode.ForceVertex;
            }
            else
            {
                mat.SetColor("_SpecularColor", Color.black);
            }

        }

	    public void AddPG(NeoNormalGrid PGrid)
	    {
	        if (!grids.Contains(PGrid))
	        {
                Material mat = PGrid.oceanMaterial;
	            UpdateMaterial(PGrid, mat, true);
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
