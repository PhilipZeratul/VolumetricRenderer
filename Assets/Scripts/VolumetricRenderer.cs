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
        [Range(10f, 500f)]
        public float volumeDistance = 100f; // 50 - 100m in AC4

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

        private readonly int volumeWidthId = Shader.PropertyToID("_VolumeWidth");
        private readonly int volumeHeightId = Shader.PropertyToID("_VolumeHeight");
        private readonly int volumeDepthId = Shader.PropertyToID("_VolumeDepth");
        private readonly int nearPlaneId = Shader.PropertyToID("_NearPlane");
        private readonly int volumeDistanceId = Shader.PropertyToID("_VolumeDistance");

        private void Awake()
        {
            mainCamera = GetComponent<Camera>();
            command = new CommandBuffer()
            {
                name = "Volumetric Render Command"
            };
            beforeGBufferCommand = new CommandBuffer()
            {
                name = "Volumetric Before GBuffer Command"
            };

            volumetricMaterial = new Material(shader);

            CreatePrevVolumes();
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
            mainCamera.AddCommandBuffer(CameraEvent.BeforeGBuffer, beforeGBufferCommand);
        }

        private void OnDisable()
        {
            mainCamera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, command);
            mainCamera.RemoveCommandBuffer(CameraEvent.BeforeGBuffer, beforeGBufferCommand);
        }

        private void OnPreRender()
        {
            CalculateMatrices();
            SetPropertyGeneral();
            
            ClearAllVolumes();
            WriteShadowVolume();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            command.Clear();
            command.BeginSample("Volumetric Renderer");

            //TemporalBlendShadowVolume();

            WriteMaterialVolume();
            //TemporalBlendMaterialVolume();

            WriteScatterVolume();
            //TemporalBlendScatterVolume();

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
        private CommandBuffer beforeGBufferCommand;

        private int clearKernel;
        private Vector3[] frustumCorners = new Vector3[4];
        private Vector4[] screenTriangleCorners = new Vector4[3];
        private Matrix4x4 froxelProjMat;
        private Matrix4x4 viewMat;
        private Matrix4x4 clipToWorldMat;

        private readonly int clipToWorldMatId = Shader.PropertyToID("_ClipToWorldMat");

        private void ClearAllVolumes()
        {
            clearKernel = compute.FindKernel("InitAllVolumes");
            beforeGBufferCommand.Clear();
            beforeGBufferCommand.SetComputeTextureParam(compute, clearKernel, shadowVolumeId, shadowVolumeTargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, clearKernel, materialVolumeAId, materialVolumeATargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, clearKernel, materialVolumeBId, materialVolumeBTargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, clearKernel, scatterVolumeId, scatterVolumeTargetId);

            beforeGBufferCommand.SetComputeTextureParam(compute, clearKernel, prevShadowVolumeId, prevShadowVolumeTargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, clearKernel, prevMaterialVolumeAId, prevMaterialVolumeATargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, clearKernel, prevMaterialVolumeBId, prevMaterialVolumeBTargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, clearKernel, prevScatterVolumeId, prevScatterVolumeTargetId);

            beforeGBufferCommand.DispatchCompute(compute, clearKernel, dispatchWidth, dispatchHeight, dispatchDepth);
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
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp
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

            GetJitterdMatrix(ref froxelProjMat);

            Transform tr = mainCamera.transform;
            Matrix4x4 lookMatrix = Matrix4x4.LookAt(tr.position, tr.position + tr.forward, tr.up);
            clipToWorldMat = lookMatrix * froxelProjMat.inverse;

            reprojMat = prevClipToWorldMat.inverse * clipToWorldMat;
            prevClipToWorldMat = clipToWorldMat;
        }

        private void SetPropertyGeneral()
        {
            command.SetComputeIntParam(compute, volumeWidthId, volumeWidth);
            command.SetComputeIntParam(compute, volumeHeightId, volumeHeight);
            command.SetComputeIntParam(compute, volumeDepthId, volumeDepth);
            command.SetComputeFloatParam(compute, nearPlaneId, mainCamera.nearClipPlane);
            command.SetComputeFloatParam(compute, volumeDistanceId, volumeDistance);
            command.SetComputeMatrixParam(compute, clipToWorldMatId, clipToWorldMat);
            command.SetComputeMatrixParam(compute, reprojMatId, reprojMat);
        }

#if _DEBUG

        private void SetPropertyDebug()
        {
            volumetricMaterial.SetTexture(materialVolumeAId, materialVolumeA);
            volumetricMaterial.SetTexture(materialVolumeBId, materialVolumeB);
            volumetricMaterial.SetTexture(scatterVolumeId, scatterVolume);
            volumetricMaterial.SetTexture(accumulationTexId, accumulationTex);
        }

        private void RenderDebug(RenderTexture source, RenderTexture destination, Material mat)
        {
            command.Blit(source, destination, mat, 1);
        }
#endif
    }

    // Temporal Blend
    public partial class VolumetricRenderer
    {
        public float temporalJitterScale = 0.1f;
        [Range(0f, 1f)]
        public float temporalBlendAlpha = 0.05f;
        public int temporalCount = 4;
        private int jitterIndex = 1;
        private Matrix4x4 reprojMat;
        private Matrix4x4 prevClipToWorldMat;

        private RenderTexture prevShadowVolume;
        private RenderTargetIdentifier prevShadowVolumeTargetId;
        private RenderTexture prevMaterialVolumeA;
        private RenderTargetIdentifier prevMaterialVolumeATargetId;
        private RenderTexture prevMaterialVolumeB;
        private RenderTargetIdentifier prevMaterialVolumeBTargetId;
        private RenderTexture prevScatterVolume;
        private RenderTargetIdentifier prevScatterVolumeTargetId;

        private readonly int prevShadowVolumeId = Shader.PropertyToID("_PrevShadowVolume");
        private readonly int prevMaterialVolumeAId = Shader.PropertyToID("_PrevMaterialVolume_A");
        private readonly int prevMaterialVolumeBId = Shader.PropertyToID("_PrevMaterialVolume_B");
        private readonly int prevScatterVolumeId = Shader.PropertyToID("_PrevScatterVolume");
        private readonly int temporalBlendAlphaId = Shader.PropertyToID("_TemporalBlendAlpha");
        private readonly int reprojMatId = Shader.PropertyToID("_ReprojMat");

        private void CreatePrevVolumes()
        {
            CreateVolume(ref prevShadowVolume, ref prevShadowVolumeTargetId, "Prev Shadow Volume", RenderTextureFormat.RHalf);
            CreateVolume(ref prevMaterialVolumeA, ref prevMaterialVolumeATargetId, "Prev Material Volume A");
            CreateVolume(ref prevMaterialVolumeB, ref prevMaterialVolumeBTargetId, "Prev Material Volume B");
            CreateVolume(ref prevScatterVolume, ref prevScatterVolumeTargetId, "Prev Scatter Volume");
        }

        private void GetJitterdMatrix(ref Matrix4x4 projMat)
        {
            if (jitterIndex > temporalCount)
            {
                jitterIndex = -temporalCount;
            }
            projMat[2, 3] += temporalJitterScale * jitterIndex;
            jitterIndex++;
        }

        private void TemporalBlendShadowVolume()
        {
            int temporalBlendShadowVolumeKernel = compute.FindKernel("TemporalBlendShadowVolume");
            command.SetComputeFloatParam(compute, temporalBlendAlphaId, temporalBlendAlpha);
            command.SetComputeTextureParam(compute, temporalBlendShadowVolumeKernel, shadowVolumeId, shadowVolumeTargetId);
            command.SetComputeTextureParam(compute, temporalBlendShadowVolumeKernel, prevShadowVolumeId, prevShadowVolumeTargetId);

            command.DispatchCompute(compute, temporalBlendShadowVolumeKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }

        private void TemporalBlendMaterialVolume()
        {
            int temporalBlendMaterialVolumeKernel = compute.FindKernel("TemporalBlendMaterialVolume");
            command.SetComputeTextureParam(compute, temporalBlendMaterialVolumeKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, temporalBlendMaterialVolumeKernel, materialVolumeBId, materialVolumeBTargetId);
            command.SetComputeTextureParam(compute, temporalBlendMaterialVolumeKernel, prevMaterialVolumeAId, prevMaterialVolumeATargetId);
            command.SetComputeTextureParam(compute, temporalBlendMaterialVolumeKernel, prevMaterialVolumeBId, prevMaterialVolumeBTargetId);

            command.DispatchCompute(compute, temporalBlendMaterialVolumeKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }

        private void TemporalBlendScatterVolume()
        {
            int temporalBlendScatterVolumeKernel = compute.FindKernel("TemporalBlendScatterVolume");
            command.SetComputeTextureParam(compute, temporalBlendScatterVolumeKernel, scatterVolumeId, scatterVolumeTargetId);
            command.SetComputeTextureParam(compute, temporalBlendScatterVolumeKernel, prevScatterVolumeId, prevScatterVolumeTargetId);

            command.DispatchCompute(compute, temporalBlendScatterVolumeKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }
    }

    // Shadow Volume
        public partial class VolumetricRenderer
    {
        public event Action WriteShadowVolumeEvent;

        [SerializeField]
        private ComputeShader shadowCompute;

        private RenderTexture shadowVolume; // R: Visibility
        private RenderTargetIdentifier shadowVolumeTargetId;
        private RenderTargetIdentifier shadowMapTextureTargetId;
        private RenderTexture esmShadowMapTex;
        private RenderTargetIdentifier esmShadowMapTexTargetId;

        private readonly int shadowVolumeId = Shader.PropertyToID("_ShadowVolume");
        private readonly int shadowMapTextureId = Shader.PropertyToID("_ShadowMapTexture");
        private readonly int esmShadowMapTexId = Shader.PropertyToID("_EsmShadowMapTex");
        private readonly int esmShadowMapUavId = Shader.PropertyToID("_EsmShadowMapUav");

        private int writeShadowVolumeDirKernel;
        private int esmShadowMapKernel;

        private const int esmShadowMapRes = 256;

        private void InitShadowVolumetric()
        {
            CreateVolume(ref shadowVolume, ref shadowVolumeTargetId, "Shadow Volume", RenderTextureFormat.RHalf);
            shadowMapTextureTargetId = new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive);
            esmShadowMapTex = new RenderTexture(esmShadowMapRes, esmShadowMapRes, 0, RenderTextureFormat.RHalf)
            {
                name = "ESM Shadowmap Texture",
                filterMode = FilterMode.Bilinear,
                dimension = TextureDimension.Tex2D,
                enableRandomWrite = true,
                wrapMode = TextureWrapMode.Clamp
            };
            esmShadowMapTex.Create();
            esmShadowMapTexTargetId = new RenderTargetIdentifier(esmShadowMapTex);
        }

        private void WriteShadowVolume()
        {
            writeShadowVolumeDirKernel = shadowCompute.FindKernel("WriteShadowVolumeDir");
            esmShadowMapKernel = shadowCompute.FindKernel("EsmShadowMap");

            shadowCompute.SetMatrix(clipToWorldMatId, clipToWorldMat);
            shadowCompute.SetInt(volumeWidthId, volumeWidth);
            shadowCompute.SetInt(volumeHeightId, volumeHeight);
            shadowCompute.SetInt(volumeDepthId, volumeDepth);
            shadowCompute.SetFloat(volumeDistanceId, volumeDistance);
            shadowCompute.SetFloat(nearPlaneId, mainCamera.nearClipPlane);

            WriteShadowVolumeEvent?.Invoke();
        }

        public void DirLightShadow(CommandBuffer shadowCommand, CommandBuffer dirShadowCommand)
        {
            dirShadowCommand.Clear();
            dirShadowCommand.SetComputeTextureParam(shadowCompute, esmShadowMapKernel, shadowMapTextureId, shadowMapTextureTargetId);

            shadowCommand.Clear();
            shadowCommand.SetComputeTextureParam(shadowCompute, esmShadowMapKernel, esmShadowMapUavId, esmShadowMapTexTargetId);
            shadowCommand.DispatchCompute(shadowCompute, esmShadowMapKernel, esmShadowMapRes / 8, esmShadowMapRes / 8, 1);

            shadowCommand.SetComputeTextureParam(shadowCompute, writeShadowVolumeDirKernel, shadowVolumeId, shadowVolumeTargetId);
            shadowCommand.SetComputeTextureParam(shadowCompute, writeShadowVolumeDirKernel, esmShadowMapTexId, esmShadowMapTexTargetId);
            shadowCommand.DispatchCompute(shadowCompute, writeShadowVolumeDirKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }
    }

    // Material Volume
    public partial class VolumetricRenderer
    {
        private RenderTexture materialVolumeA; // RGB: Scattering, A: Absorption
        private RenderTargetIdentifier materialVolumeATargetId;
        private RenderTexture materialVolumeB; // R: Phase g, G: Global emissive intensity, B: Ambient intensity, A: Water droplet density
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
            CreateVolume(ref materialVolumeA, ref materialVolumeATargetId, "Material Volume A");
            CreateVolume(ref materialVolumeB, ref materialVolumeBTargetId, "Material Volume B");
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

        private void WriteMaterialVolume()
        {
            constantVolumeKernel = compute.FindKernel("WriteMaterialVolumeConstant");
            command.SetComputeTextureParam(compute, constantVolumeKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, constantVolumeKernel, materialVolumeBId, materialVolumeBTargetId);

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

        private void WriteScatterVolume()
        {
            //volumetricMaterial.SetTexture(scatterVolumeId, scatterVolume);

            scatterVolumeDirKernel = compute.FindKernel("WriteScatterVolumeDir");
            command.SetComputeTextureParam(compute, scatterVolumeDirKernel, shadowVolumeId, shadowVolumeTargetId);
            command.SetComputeTextureParam(compute, scatterVolumeDirKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, scatterVolumeDirKernel, materialVolumeBId, materialVolumeBTargetId);
            command.SetComputeTextureParam(compute, scatterVolumeDirKernel, scatterVolumeId, scatterVolumeTargetId);

            for (int i = 0; i < lights.Count; i++)
            {
                Color lightColor = lights[i].theLight.color * lights[i].theLight.intensity;
                lightColor.r = Mathf.Pow(lightColor.r, 2.2f);
                lightColor.g = Mathf.Pow(lightColor.g, 2.2f);
                lightColor.b = Mathf.Pow(lightColor.b, 2.2f);

                command.SetComputeVectorParam(compute, lightColorId, lightColor);
                command.SetComputeVectorParam(compute, lightDirId, lights[i].theLight.transform.forward);

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
        private readonly int cameraDepthTexId = Shader.PropertyToID("_CameraDepthTexture");

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

        private void Accumulate()
        {
            accumulationKernel = compute.FindKernel("Accumulation");
            command.SetComputeTextureParam(compute, accumulationKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, accumulationKernel, scatterVolumeId, scatterVolumeTargetId);
            command.SetComputeTextureParam(compute, accumulationKernel, accumulationTexId, accumulationTexTargetId);
            compute.SetTextureFromGlobal(accumulationKernel, cameraDepthTexId, cameraDepthTexId);

            command.DispatchCompute(compute, accumulationKernel, dispatchWidth, dispatchHeight, 1);
        }
    }
}