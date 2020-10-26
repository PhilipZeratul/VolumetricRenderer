#define _DEBUG

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Volumetric
{
    // TODO: Make it work with EditMode.

    // Render
    //[ExecuteInEditMode]
    [RequireComponent(typeof(Camera))]
    public partial class VolumetricRenderer : MonoBehaviour
    {
        // Parameters
        [Range(0f, 100f)]
        public int maxSteps = 50;
        [Range(0.1f, 10f)]
        public float maxDistance = 10f;
        [Range(10f, 5000f)]
        public float volumeDistance = 1000f;

        //
        [Space]
        [SerializeField]
        private Shader shader;
        [SerializeField]
        private ComputeShader compute;

        private Camera mainCamera;
        private CommandBuffer command;
        private Material volumetricMaterial;

        private const int volumeWidth = 160;
        private const int volumeHeight = 88;
        private const int volumeDepth = 64;
        private const int dispatchWidth = volumeWidth / 8;
        private const int dispatchHeight = volumeHeight / 8;
        private const int dispatchDepth = volumeDepth / 8;

        private void Awake()
        {
            mainCamera = GetComponent<Camera>();
            command = new CommandBuffer()
            {
                name = "Volumetric Render Command"
            };
            clearCommand = new CommandBuffer()
            {
                name = "Volumetric Clear Command"
            };
            shadowCommand = new CommandBuffer()
            {
                name = "Volumetric Shadow Command"
            };

            volumetricMaterial = new Material(shader);

            InitShadowVolumetric();
            CreateMaterialVolumes();
            CreateScatterVolume();
            CreateAccumulationTex();
        }

        private void OnDestroy()
        {
            // TODO: Cleanup.
        }

        private void OnEnable()
        {
            mainCamera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, command);
            mainCamera.AddCommandBuffer(CameraEvent.BeforeGBuffer, clearCommand);
        }

        private void OnDisable()
        {
            mainCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, command);
            mainCamera.RemoveCommandBuffer(CameraEvent.BeforeGBuffer, clearCommand);
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            command.Clear();
            command.BeginSample("Volumetric Renderer");

            CalculateMatrices();

            ClearAllVolumes();

            SetPropertyShadowVolume();
            WriteShadowVolume();

            SetPropertyMaterialVolume();
            WriteMaterialVolume();

            SetPropertyScatterVolume();
            WriteScatterVolume();

            SetPropertyAccumulation();
            Accumulate();

            //camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.farClipPlane, camera.stereoActiveEye, frustumCorners);

            //screenTriangleCorners[0] = new Vector4(frustumCorners[1].x, frustumCorners[1].y, camera.farClipPlane, 0);
            //screenTriangleCorners[1] = new Vector4(frustumCorners[0].x, -3.0f * frustumCorners[1].y, camera.farClipPlane, 0);
            //screenTriangleCorners[2] = new Vector4(3.0f * frustumCorners[2].x, frustumCorners[1].y, camera.farClipPlane, 0);

            //screenTriangleCorners[0] = camera.transform.TransformVector(screenTriangleCorners[0]) / camera.farClipPlane;
            //screenTriangleCorners[1] = camera.transform.TransformVector(screenTriangleCorners[1]) / camera.farClipPlane;
            //screenTriangleCorners[2] = camera.transform.TransformVector(screenTriangleCorners[2]) / camera.farClipPlane;

            //sheet.properties.SetVectorArray("_ScreenQuadCorners", screenTriangleCorners);
            //sheet.properties.SetInt("_MaxSteps", settings.maxSteps);
            //sheet.properties.SetFloat("_MaxDistance", settings.maxDistance);

            //command.BlitFullscreenTriangle(source, destination, sheet, 0);

            //// Test
#if _DEBUG
            SetPropertyDebug();
            RenderDebug(source, destination, volumetricMaterial);
#endif

            command.EndSample("Volumetric Renderer");
        }
    }

    // Misc
    public partial class VolumetricRenderer
    {
        private CommandBuffer clearCommand;

        private int clearKernel;
        private Vector3[] frustumCorners = new Vector3[4];
        private Vector4[] screenTriangleCorners = new Vector4[3];
        private Matrix4x4 froxelProjMat;
        private Matrix4x4 viewMat;
        private Matrix4x4 invFroxelVPMat;

        private void CreateCommands()
        {

        }

        private void ClearAllVolumes()
        {
            clearKernel = compute.FindKernel("ClearAllVolumes");
            clearCommand.Clear();
            clearCommand.SetComputeTextureParam(compute, clearKernel, shadowVolumeId, shadowVolumeTargetId);
            clearCommand.SetComputeTextureParam(compute, clearKernel, materialVolumeAId, materialVolumeATargetId);
            clearCommand.SetComputeTextureParam(compute, clearKernel, materialVolumeBId, materialVolumeBTargetId);
            clearCommand.SetComputeTextureParam(compute, clearKernel, scatterVolumeId, scatterVolumeTargetId);
            clearCommand.SetComputeTextureParam(compute, clearKernel, accumulationTexId, accumulationTexTargetId);

            clearCommand.DispatchCompute(compute, clearKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }

        private void CreateVolume(ref RenderTexture volume, ref RenderTargetIdentifier id, string name,
                                  RenderTextureFormat format = RenderTextureFormat.ARGBHalf,
                                  int width = volumeWidth, int height = volumeHeight, int depth = volumeDepth)
        {
            if (volume != null)
            {
                GameObject.Destroy(volume);
            }
            volume = new RenderTexture(width, height, 0, format)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                dimension = TextureDimension.Tex3D,
                volumeDepth = depth,
                enableRandomWrite = true
            };
            volume.Create();
            id = new RenderTargetIdentifier(volume);
        }

        private void CalculateMatrices()
        {
            float froxelDistance = volumeDistance;
            float nearClipPlane = mainCamera.nearClipPlane;
            float b = 1.0f / Mathf.Tan(Mathf.Deg2Rad * mainCamera.fieldOfView / 2.0f);
            float a = b / mainCamera.aspect;
            float c = froxelDistance / (froxelDistance - nearClipPlane);
            float d = -froxelDistance * nearClipPlane / (froxelDistance - nearClipPlane);
            froxelProjMat = new Matrix4x4(
                new Vector4(a, 0, 0, 0),
                new Vector4(0, b, 0, 0),
                new Vector4(0, 0, c, 1),
                new Vector4(0, 0, d, 0));

            viewMat = Matrix4x4.TRS(-mainCamera.transform.position, Quaternion.Inverse(mainCamera.transform.rotation), new Vector3(1, 1, 1));
            invFroxelVPMat = (froxelProjMat * viewMat).inverse;
        }

#if _DEBUG

        private void SetPropertyDebug()
        {
            volumetricMaterial.SetTexture(materialVolumeAId, materialVolume_A);
            volumetricMaterial.SetTexture(materialVolumeBId, materialVolume_B);
            volumetricMaterial.SetTexture(scatterVolumeId, scatterVolume);
            volumetricMaterial.SetTexture(accumulationTexId, accumulationTex);
        }

        private void RenderDebug(RenderTexture source, RenderTexture destination, Material mat)
        {
            command.Blit(source, destination, mat, 1);
        }

#endif
    }

    // Shadow Volume
    public partial class VolumetricRenderer
    {
        public event Action WriteShadowVolumeEvent;

        public CommandBuffer shadowCommand;

        [SerializeField]
        private ComputeShader shadowCompute;

        private RenderTexture shadowVolume; // R: Visibility
        private RenderTargetIdentifier shadowVolumeTargetId;
        private RenderTargetIdentifier shadowMapTextureTargetId;

        private readonly int shadowVolumeId = Shader.PropertyToID("_ShadowVolume");
        private readonly int shadowMapTextureId = Shader.PropertyToID("_ShadowMapTexture");

        private int shadowVolumeDirKernel;

        private void InitShadowVolumetric()
        {
            CreateVolume(ref shadowVolume, ref shadowVolumeTargetId, "Shadow Volume", RenderTextureFormat.R16);
            shadowMapTextureTargetId = new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive);
        }

        private void SetPropertyShadowVolume()
        {
            shadowVolumeDirKernel = shadowCompute.FindKernel("WriteShadowVolumeDir");
        }

        private void WriteShadowVolume()
        {
            WriteShadowVolumeEvent?.Invoke();
        }

        public void DirLightShadow()
        {
            shadowCommand.Clear();

            shadowCommand.SetComputeMatrixParam(shadowCompute, invFroxelVPMatId, invFroxelVPMat);
            shadowCommand.SetComputeTextureParam(shadowCompute, shadowVolumeDirKernel, shadowVolumeId, shadowVolumeTargetId);
            shadowCommand.SetComputeTextureParam(shadowCompute, shadowVolumeDirKernel, shadowMapTextureId, shadowMapTextureTargetId);
            shadowCommand.DispatchCompute(shadowCompute, shadowVolumeDirKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }
    }

    // Material Volume
    public partial class VolumetricRenderer
    {
        private RenderTexture materialVolume_A; // RGB: Scattering, A: Absorption
        private RenderTargetIdentifier materialVolumeATargetId;
        private RenderTexture materialVolume_B; // R: Phase g, G: Global emissive intensity, B: Ambient intensity, A: Water droplet density
        private RenderTargetIdentifier materialVolumeBTargetId;

        private readonly int materialVolumeAId = Shader.PropertyToID("_MaterialVolume_A");
        private readonly int materialVolumeBId = Shader.PropertyToID("_MaterialVolume_B");
        private readonly int scatteringCoefId = Shader.PropertyToID("_ScatteringCoef");
        private readonly int absorptionCoefId = Shader.PropertyToID("_AbsorptionCoef");
        private readonly int phaseGId = Shader.PropertyToID("_PhaseG");

        private int constantVolumeKernel;

        private List<VolumetricMaterialVolume> materialVolumes = new List<VolumetricMaterialVolume>();

        private void CreateMaterialVolumes()
        {
            CreateVolume(ref materialVolume_A, ref materialVolumeATargetId, "Material Volume A");
            CreateVolume(ref materialVolume_B, ref materialVolumeBTargetId, "Material Volume B");
        }

        public void RegisterMaterialVolume(VolumetricMaterialVolume materialVolume)
        {
            if (!materialVolumes.Contains(materialVolume))
            {
                materialVolumes.Add(materialVolume);
            }
        }

        public void UnregisterMaterialVolume(VolumetricMaterialVolume materialVolume)
        {
            materialVolumes.Remove(materialVolume);
        }

        private void SetPropertyMaterialVolume()
        {
            constantVolumeKernel = compute.FindKernel("WriteMaterialVolumeConstant");
            command.SetComputeTextureParam(compute, constantVolumeKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, constantVolumeKernel, materialVolumeBId, materialVolumeBTargetId);
        }

        private void WriteMaterialVolume()
        {
            for (int i = 0; i < materialVolumes.Count; i++)
            {
                switch (materialVolumes[i].volumeType)
                {
                    case VolumetricMaterialVolume.VolumeType.Constant:
                        command.SetComputeVectorParam(compute, scatteringCoefId, Color2Vector(materialVolumes[i].ScatteringCoef));
                        command.SetComputeFloatParam(compute, absorptionCoefId, materialVolumes[i].AbsorptionCoef);
                        command.SetComputeFloatParam(compute, phaseGId, materialVolumes[i].phaseG);
                        command.DispatchCompute(compute, constantVolumeKernel, dispatchWidth, dispatchHeight, dispatchDepth);
                        break;

                    case VolumetricMaterialVolume.VolumeType.Box:
                        break;

                    default:
                        break;
                }
            }
        }

        // Unity will do gamma correction if set color directly.
        private Vector3 Color2Vector(Color color)
        {
            return new Vector3(color.r, color.g, color.b);
        }
    }

    // In-Scatter Volume
    public partial class VolumetricRenderer
    {
        private RenderTexture scatterVolume; // RGB: Scattering, A: Extinction
        private RenderTargetIdentifier scatterVolumeTargetId;

        private readonly int scatterVolumeId = Shader.PropertyToID("_ScatterVolume");
        private readonly int lightColorId = Shader.PropertyToID("_LightColor");
        private readonly int lightDirId = Shader.PropertyToID("_LightDir");
        private readonly int viewDirId = Shader.PropertyToID("_ViewDir");

        private int scatterVolumeDirKernel;

        private List<VolumetricLight> lights = new List<VolumetricLight>();

        public void RegisterLight(VolumetricLight light)
        {
            if (!lights.Contains(light))
            {
                lights.Add(light);
            }
        }

        public void UnregisterLight(VolumetricLight light)
        {
            lights.Remove(light);
        }

        private void CreateScatterVolume()
        {
            CreateVolume(ref scatterVolume, ref scatterVolumeTargetId, "Scatter Volume");
        }

        private void SetPropertyScatterVolume()
        {
            volumetricMaterial.SetTexture(scatterVolumeId, scatterVolume);

            scatterVolumeDirKernel = compute.FindKernel("WriteScatterVolumeDir");
            command.SetComputeTextureParam(compute, scatterVolumeDirKernel, shadowVolumeId, shadowVolumeTargetId);
            command.SetComputeTextureParam(compute, scatterVolumeDirKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, scatterVolumeDirKernel, materialVolumeBId, materialVolumeBTargetId);
            command.SetComputeTextureParam(compute, scatterVolumeDirKernel, scatterVolumeId, scatterVolumeTargetId);
        }

        private void WriteScatterVolume()
        {
            for (int i = 0; i < lights.Count; i++)
            {
                Color lightColor = lights[i].theLight.color * lights[i].theLight.intensity;
                lightColor.r = Mathf.Pow(lightColor.r, 2.2f);
                lightColor.g = Mathf.Pow(lightColor.g, 2.2f);
                lightColor.b = Mathf.Pow(lightColor.b, 2.2f);

                command.SetComputeVectorParam(compute, lightColorId, lightColor);
                command.SetComputeVectorParam(compute, lightDirId, lights[i].theLight.transform.forward);
                command.SetComputeVectorParam(compute, viewDirId, mainCamera.transform.forward);

                switch (lights[i].theLight.type)
                {
                    case LightType.Directional:
                        command.DispatchCompute(compute, scatterVolumeDirKernel, dispatchWidth, dispatchHeight, dispatchDepth);
                        break;

                    case LightType.Spot:
                        break;

                    case LightType.Point:
                        break;

                    default:
                        break;
                }
            }
        }
    }

    // Accumulation Pass
    public partial class VolumetricRenderer
    {
        private RenderTexture accumulationTex;
        private RenderTargetIdentifier accumulationTexTargetId;

        private readonly int accumulationTexId = Shader.PropertyToID("_AccumulationTex");
        private readonly int volumeWidthId = Shader.PropertyToID("_VolumeWidth");
        private readonly int volumeHeightId = Shader.PropertyToID("_VolumeHeight");
        private readonly int volumeDepthId = Shader.PropertyToID("_VolumeDepth");
        private readonly int nearPlaneId = Shader.PropertyToID("_NearPlane");
        private readonly int volumeDistanceId = Shader.PropertyToID("_VolumeDistance");
        private readonly int invFroxelVPMatId = Shader.PropertyToID("_invFroxelVPMat");

        private int accumulationKernel;

        private void CreateAccumulationTex()
        {
            if (accumulationTex != null)
            {
                GameObject.Destroy(accumulationTex);
            }
            accumulationTex = new RenderTexture(volumeWidth, volumeHeight, 0, RenderTextureFormat.ARGBFloat)
            {
                name = "Accumulation Tex",
                filterMode = FilterMode.Bilinear,
                dimension = TextureDimension.Tex2D,
                enableRandomWrite = true
            };
            accumulationTex.Create();
            accumulationTexTargetId = new RenderTargetIdentifier(accumulationTex);
        }

        private void SetPropertyAccumulation()
        {
            accumulationKernel = compute.FindKernel("Accumulation");
            command.SetComputeTextureParam(compute, accumulationKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, accumulationKernel, scatterVolumeId, scatterVolumeTargetId);
            command.SetComputeTextureParam(compute, accumulationKernel, accumulationTexId, accumulationTexTargetId);

            command.SetComputeIntParam(compute, volumeWidthId, volumeWidth);
            command.SetComputeIntParam(compute, volumeHeightId, volumeHeight);
            command.SetComputeIntParam(compute, volumeDepthId, volumeDepth);
            command.SetComputeFloatParam(compute, nearPlaneId, mainCamera.nearClipPlane);
            command.SetComputeFloatParam(compute, volumeDistanceId, volumeDistance);
            command.SetComputeMatrixParam(compute, invFroxelVPMatId, invFroxelVPMat);

            ///Debug
            command.SetComputeTextureParam(compute, accumulationKernel, shadowVolumeId, shadowVolumeTargetId);
        }

        private void Accumulate()
        {
            command.DispatchCompute(compute, accumulationKernel, dispatchWidth, dispatchHeight, 1);
        }
    }
}