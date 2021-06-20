using System.Collections.Generic;
using UnityEngine;

public struct Sphere
{
    public Vector3 position;
    public float radius;
    public Vector3 albedo;
    public Vector3 specular;
}

public class RayTracing : MonoBehaviour
{
    [Header("Compute Shader Configuration")]
    public Light directionalLight;

    public ComputeShader rayTracingCS;
    public Texture skyboxTexture;
    [Range(1, 10)] public int maximumBounce;
    public Shader samplingShader;
    public bool enableAntiAliasing;

    [Header("Ball Configuration")] public Vector2 sphereRadius = new Vector2(3f, 8f);
    public int maxSpheres = 100;
    public float spherePlacementRadius = 100f;
    public int sphereSeed = 1684;

    private RenderTexture target;
    private RenderTexture converged;
    private ComputeBuffer sphereBuffer;
    private Camera mainCamera;
    private uint currentSample = 0;
    private Material samplingMaterial;

    private void OnEnable()
    {
        currentSample = 0;
        SetUpSpheres();
    }

    private void OnDisable()
    {
        sphereBuffer?.Release();
    }

    private void Awake()
    {
        mainCamera = GetComponent<Camera>();
    }

    private void Update()
    {
        if (transform.hasChanged)
        {
            currentSample = 0;
            transform.hasChanged = false;
        }
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        SetShaderParameters();
        Render(destination);
    }

    private void SetUpSpheres()
    {
        Random.InitState(sphereSeed);
        List<Sphere> spheres = new List<Sphere>();

        for (int i = 0; i < maxSpheres; i++)
        {
            Sphere sphere = new Sphere();
            Vector2 randomPos = Random.insideUnitCircle * spherePlacementRadius;
            sphere.radius = Random.Range(sphereRadius.x, sphereRadius.y);
            sphere.position = new Vector3(randomPos.x, sphere.radius, randomPos.y);

            bool isIntersecting = false;
            foreach (Sphere other in spheres)
            {
                float minDist = sphere.radius + other.radius;
                if (Vector3.SqrMagnitude(sphere.position - other.position) < Mathf.Pow(minDist, 2))
                {
                    isIntersecting = true;
                    break;
                }
            }

            if (isIntersecting) continue;

            Color color = Random.ColorHSV();
            Vector3 colorVector = new Vector3(color.r, color.g, color.b);
            bool isMetal = Random.value < .5f;

            isMetal = false;

            sphere.albedo = isMetal ? Vector3.zero : colorVector;
            sphere.specular = isMetal ? colorVector : Vector3.one * 0.04f;
            spheres.Add(sphere);
        }

        int stride = sizeof(float) * 10;
        sphereBuffer = new ComputeBuffer(spheres.Count, stride);
        sphereBuffer.SetData(spheres);
    }

    private void SetShaderParameters()
    {
        Vector4 lightDirection = directionalLight.transform.forward;
        lightDirection.w = directionalLight.intensity;

        rayTracingCS.SetBuffer(0, "_Spheres", sphereBuffer);
        rayTracingCS.SetVector("_DirectionalLight", lightDirection);
        rayTracingCS.SetMatrix("_CameraToWorld", mainCamera.cameraToWorldMatrix);
        rayTracingCS.SetMatrix("_CameraInverseProjection", mainCamera.projectionMatrix.inverse);
        rayTracingCS.SetTexture(0, "_SkyboxTexture", skyboxTexture);
        rayTracingCS.SetInt("_MaximumBounce", maximumBounce);
        rayTracingCS.SetVector("_Time", Shader.GetGlobalVector("_Time"));
        rayTracingCS.SetFloat("_RandomSeed", Random.value);
        if (currentSample == 0 || !enableAntiAliasing)
        {
            rayTracingCS.SetVector("_PixelOffset", Vector2.zero);
        }
        else
        {
            rayTracingCS.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
        }
    }


    private void InitRenderTexture()
    {
        if (target != null && target.width == Screen.width && target.height == Screen.height) return;
        if (target != null)
        {
            target.Release();
            converged.Release();
        }

        target = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        converged = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGBFloat,
            RenderTextureReadWrite.Linear);
        
        target.enableRandomWrite = true;
        converged.enableRandomWrite = true;
        
        target.Create();
        converged.Create();
    }

    private void InitSamplingMaterial()
    {
        if (samplingMaterial == null)
        {
            samplingMaterial = new Material(samplingShader);
        }
    }

    private void Render(RenderTexture destination)
    {
        InitRenderTexture();
        InitSamplingMaterial();

        int threadGroupX = Mathf.CeilToInt(Screen.width / 8f);
        int threadGroupY = Mathf.CeilToInt(Screen.height / 8f);

        rayTracingCS.SetTexture(0, "Result", target);
        rayTracingCS.Dispatch(0, threadGroupX, threadGroupY, 1);
        samplingMaterial.SetFloat("_Sample", currentSample);

        if (enableAntiAliasing)
        {
            Graphics.Blit(target, converged, samplingMaterial);
            currentSample++;
        }
        else
        {
            Graphics.Blit(target, converged);
        }
        Graphics.Blit(converged, destination);
    }
}