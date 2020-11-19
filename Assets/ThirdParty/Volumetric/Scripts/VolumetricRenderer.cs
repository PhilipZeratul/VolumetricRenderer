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
        [Range(0f, 200f)]
        public int maxSteps = 50;
        [Range(10f, 1000f)]
        public float volumeDistance = 100f; // 50 - 100m in AC4

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
        private const int dispatchDepth = volumeDepth / 16;

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
            CreateAccumulationVolume();
            GetJitterSequence(froxelSampleOffsetSeq);
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
            beforeGBufferCommand.Clear();

            SetPropertyTemporal();
            
            CalculateMatrices();
            SetPropertyGeneral();

            InitAllVolumes();
            WriteShadowVolume();
        }

        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            command.Clear();
            command.BeginSample("Volumetric Renderer");

            TemporalBlendShadowVolume();

            WriteMaterialVolume();
            TemporalBlendMaterialVolume();

            WriteScatterVolume();
            TemporalBlendScatterVolume();

            Accumulate();

            SetProperyFinal();
            command.Blit(source, destination, volumetricMaterial, 0);

            //// Test
#if _DEBUG
            //SetPropertyDebug();
            //RenderDebug(source, destination, volumetricMaterial);
#endif

            SaveHistory();
            command.EndSample("Volumetric Renderer");
        }
    }

    // Misc
    public partial class VolumetricRenderer
    {
        [Range(0.1f, 1.0f)]
        public float depthDistribution = 0.5f;
        private CommandBuffer beforeGBufferCommand;

        private int initKernel;

        private Matrix4x4 worldToViewMat;
        private Matrix4x4 viewToWorldMat;
        private Vector4 froxelToWorldParams = new Vector4();
        private float nearPlane;

        private readonly int viewToWorldMatId = Shader.PropertyToID("_ViewToWorldMat");
        private readonly int worldToViewMatId = Shader.PropertyToID("_WorldToViewMat");
        private readonly int froxelToWorldParamsId = Shader.PropertyToID("_FroxelToWorldParams");

        private void InitAllVolumes()
        {
            initKernel = compute.FindKernel("InitAllVolumes");

            beforeGBufferCommand.SetComputeTextureParam(compute, initKernel, shadowVolumeId, shadowVolumeTargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, initKernel, materialVolumeAId, materialVolumeATargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, initKernel, materialVolumeBId, materialVolumeBTargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, initKernel, scatterVolumeId, scatterVolumeTargetId);
            beforeGBufferCommand.SetComputeTextureParam(compute, initKernel, accumulationVolumeId, accumulationVolumeTargetId);

            beforeGBufferCommand.DispatchCompute(compute, initKernel, dispatchWidth, dispatchHeight, dispatchDepth);
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
            nearPlane = mainCamera.nearClipPlane;
            Transform tr = mainCamera.transform;
            viewToWorldMat = Matrix4x4.LookAt(tr.position, tr.position + tr.forward, tr.up);
            worldToViewMat = viewToWorldMat.inverse;

            froxelToWorldParams.y = 1.0f / Mathf.Tan(Mathf.Deg2Rad * mainCamera.fieldOfView / 2.0f);
            froxelToWorldParams.x = froxelToWorldParams.y / mainCamera.aspect;
            froxelToWorldParams.z = depthDistribution * (volumeDepth - nearPlane * volumeDepth / volumeDistance) + 1;
            froxelToWorldParams.w = volumeDistance / depthDistribution / volumeDepth;
        }

        private void SetPropertyGeneral()
        {
            compute.SetInt(volumeWidthId, volumeWidth - 1);
            compute.SetInt(volumeHeightId, volumeHeight - 1);
            compute.SetInt(volumeDepthId, volumeDepth - 1);
            compute.SetFloat(nearPlaneId, nearPlane);
            compute.SetFloat(volumeDistanceId, volumeDistance);
            compute.SetMatrix(viewToWorldMatId, viewToWorldMat);
            compute.SetMatrix(worldToViewMatId, worldToViewMat);
            compute.SetVector(froxelToWorldParamsId, froxelToWorldParams);
        }
    }

    // Temporal Blend
    public partial class VolumetricRenderer
    {
        [Range(0f, 1f)]
        public float temporalBlendAlpha = 1 / 7f;
        private Vector3[] froxelSampleOffsetSeq = new Vector3[7];
        private Matrix4x4 prevWorldToViewMat;
        private int saveHistoryKernel;

        private RenderTexture prevShadowVolume;
        private RenderTargetIdentifier prevShadowVolumeTargetId;
        private RenderTexture prevMaterialVolumeA;
        private RenderTargetIdentifier prevMaterialVolumeATargetId;
        private RenderTexture prevScatterVolume;
        private RenderTargetIdentifier prevScatterVolumeTargetId;
        private RenderTexture prevAccumulationVolume;
        private RenderTargetIdentifier prevAccumulationVolumeTargetId;

        private readonly int prevShadowVolumeId = Shader.PropertyToID("_PrevShadowVolume");
        private readonly int prevShadowVolumeSrvId = Shader.PropertyToID("_PrevShadowVolumeSrv");
        private readonly int prevMaterialVolumeAId = Shader.PropertyToID("_PrevMaterialVolume_A");
        private readonly int prevScatterVolumeId = Shader.PropertyToID("_PrevScatterVolume");
        private readonly int prevAccumulationVolumeId = Shader.PropertyToID("_PrevAccumulationVolume");
        private readonly int temporalBlendAlphaId = Shader.PropertyToID("_TemporalBlendAlpha");
        private readonly int froxelSampleOffsetId = Shader.PropertyToID("_FroxelSampleOffset");
        private readonly int prevWorldToViewMatId = Shader.PropertyToID("_PrevWorldToViewMat");

        private void CreatePrevVolumes()
        {
            CreateVolume(ref prevShadowVolume, ref prevShadowVolumeTargetId, "Prev Shadow Volume", RenderTextureFormat.RHalf);
            CreateVolume(ref prevMaterialVolumeA, ref prevMaterialVolumeATargetId, "Prev Material Volume A");
            CreateVolume(ref prevScatterVolume, ref prevScatterVolumeTargetId, "Prev Scatter Volume");
            CreateVolume(ref prevAccumulationVolume, ref prevAccumulationVolumeTargetId, "Prev Accumulation Volume");
        }

        private void SetPropertyTemporal()
        {
            compute.SetVector(froxelSampleOffsetId, froxelSampleOffsetSeq[Time.frameCount % 7]);
            compute.SetFloat(temporalBlendAlphaId, temporalBlendAlpha);
            compute.SetMatrix(prevWorldToViewMatId, prevWorldToViewMat);
            shadowCompute.SetVector(froxelSampleOffsetId, froxelSampleOffsetSeq[Time.frameCount % 7]);
        }

        private void TemporalBlendShadowVolume()
        {
            int temporalBlendShadowVolumeKernel = compute.FindKernel("TemporalBlendShadowVolume");
            command.SetComputeFloatParam(compute, temporalBlendAlphaId, temporalBlendAlpha);
            command.SetComputeTextureParam(compute, temporalBlendShadowVolumeKernel, shadowVolumeId, shadowVolumeTargetId);
            //command.SetComputeTextureParam(compute, temporalBlendShadowVolumeKernel, prevShadowVolumeId, prevShadowVolumeTargetId);
            command.SetComputeTextureParam(compute, temporalBlendShadowVolumeKernel, prevShadowVolumeSrvId, prevShadowVolumeTargetId);

            command.DispatchCompute(compute, temporalBlendShadowVolumeKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }

        private void TemporalBlendMaterialVolume()
        {
            int temporalBlendMaterialVolumeKernel = compute.FindKernel("TemporalBlendMaterialVolume");
            command.SetComputeTextureParam(compute, temporalBlendMaterialVolumeKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, temporalBlendMaterialVolumeKernel, materialVolumeBId, materialVolumeBTargetId);
            command.SetComputeTextureParam(compute, temporalBlendMaterialVolumeKernel, prevMaterialVolumeAId, prevMaterialVolumeATargetId);

            command.DispatchCompute(compute, temporalBlendMaterialVolumeKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }

        private void TemporalBlendScatterVolume()
        {
            int temporalBlendScatterVolumeKernel = compute.FindKernel("TemporalBlendScatterVolume");
            command.SetComputeTextureParam(compute, temporalBlendScatterVolumeKernel, scatterVolumeId, scatterVolumeTargetId);
            command.SetComputeTextureParam(compute, temporalBlendScatterVolumeKernel, prevScatterVolumeId, prevScatterVolumeTargetId);

            command.DispatchCompute(compute, temporalBlendScatterVolumeKernel, dispatchWidth, dispatchHeight, dispatchDepth);
        }

        private void SaveHistory()
        {
            saveHistoryKernel = compute.FindKernel("SaveHistory");

            command.SetComputeTextureParam(compute, saveHistoryKernel, shadowVolumeId, shadowVolumeTargetId);
            command.SetComputeTextureParam(compute, saveHistoryKernel, materialVolumeAId, materialVolumeATargetId);
            command.SetComputeTextureParam(compute, saveHistoryKernel, scatterVolumeId, scatterVolumeTargetId);
            command.SetComputeTextureParam(compute, saveHistoryKernel, accumulationVolumeId, accumulationVolumeTargetId);
            command.SetComputeTextureParam(compute, saveHistoryKernel, prevShadowVolumeId, prevShadowVolumeTargetId);
            command.SetComputeTextureParam(compute, saveHistoryKernel, prevMaterialVolumeAId, prevMaterialVolumeATargetId);
            command.SetComputeTextureParam(compute, saveHistoryKernel, prevScatterVolumeId, prevScatterVolumeTargetId);
            command.SetComputeTextureParam(compute, saveHistoryKernel, prevAccumulationVolumeId, prevAccumulationVolumeTargetId);

            command.DispatchCompute(compute, saveHistoryKernel, dispatchWidth, dispatchHeight, dispatchDepth);

            prevWorldToViewMat = worldToViewMat;
        }

        // Ref: https://en.wikipedia.org/wiki/Close-packing_of_equal_spheres
        // The returned {x, y} coordinates (and all spheres) are all within the (-0.5, 0.5)^2 range.
        // The pattern has been rotated by 15 degrees to maximize the resolution along X and Y:
        // https://www.desmos.com/calculator/kcpfvltz7c
        // The returned {z} is (1/14, 13/14) with 2/14 interval.
        private static void GetJitterSequence(Vector3[] seq)
        {
            float r = 0.17054068870105443882f;
            float d = 2 * r;
            float s = r * Mathf.Sqrt(3);

            // Try to keep the weighted average as close to the center (0.5) as possible.
            //  (7)(5)    ( )( )    ( )( )    ( )( )    ( )( )    ( )(o)    ( )(x)    (o)(x)    (x)(x)
            // (2)(1)(3) ( )(o)( ) (o)(x)( ) (x)(x)(o) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x) (x)(x)(x)
            //  (4)(6)    ( )( )    ( )( )    ( )( )    (o)( )    (x)( )    (x)(o)    (x)(x)    (x)(x)
            seq[0] = new Vector3(0, 0, 1 / 14f);
            seq[1] = new Vector3(-d, 0, 3 / 14f);
            seq[2] = new Vector3(d, 0, 5 / 14f);
            seq[3] = new Vector3(-r, -s, 7 / 14f);
            seq[4] = new Vector3(r, s, 9 / 14f);
            seq[5] = new Vector3(r, -s, 11 / 14f);
            seq[6] = new Vector3(-r, s, 13 / 14f);

            // Rotate the sampling pattern by 15 degrees.
            const float cos15 = 0.96592582628906828675f;
            const float sin15 = 0.25881904510252076235f;

            for (int i = 0; i < 7; i++)
            {
                Vector3 coord = seq[i];

                seq[i].x = coord.x * cos15 - coord.y * sin15;
                seq[i].y = coord.x * sin15 + coord.y * cos15;
            }
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

        private readonly int shadowVolumeId = Shader.PropertyToID("_ShadowVolume");
        private readonly int shadowMapTextureId = Shader.PropertyToID("_ShadowMapTexture");

        private int writeShadowVolumeDirKernel;

        private void InitShadowVolumetric()
        {
            CreateVolume(ref shadowVolume, ref shadowVolumeTargetId, "Shadow Volume", RenderTextureFormat.RHalf);
            shadowMapTextureTargetId = new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive);
        }

        private void WriteShadowVolume()
        {
            writeShadowVolumeDirKernel = shadowCompute.FindKernel("WriteShadowVolumeDir");

            shadowCompute.SetVector(froxelToWorldParamsId, froxelToWorldParams);
            shadowCompute.SetMatrix(viewToWorldMatId, viewToWorldMat);
            shadowCompute.SetInt(volumeWidthId, volumeWidth - 1);
            shadowCompute.SetInt(volumeHeightId, volumeHeight - 1);
            shadowCompute.SetInt(volumeDepthId, volumeDepth - 1);
            shadowCompute.SetFloat(volumeDistanceId, volumeDistance);
            shadowCompute.SetFloat(nearPlaneId, nearPlane);

            WriteShadowVolumeEvent?.Invoke();
        }

        public void DirLightShadow(CommandBuffer shadowCommand, CommandBuffer dirShadowCommand)
        {
            dirShadowCommand.Clear();
            dirShadowCommand.SetComputeTextureParam(shadowCompute, writeShadowVolumeDirKernel, shadowMapTextureId, shadowMapTextureTargetId);

            shadowCommand.Clear();
            shadowCommand.SetComputeTextureParam(shadowCompute, writeShadowVolumeDirKernel, shadowVolumeId, shadowVolumeTargetId);
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
        private readonly int noiseTexId = Shader.PropertyToID("_NoiseTex");
        private readonly int noiseScrollingSpeedId = Shader.PropertyToID("_NoiseScrollingSpeed");
        private readonly int noiseTilingId = Shader.PropertyToID("_NoiseTiling");

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
            for (int i = 0; i < materialVolumes.Count; i++)
            {
                switch (materialVolumes[i].volumeType)
                {
                    case VolumetricMaterialVolume.VolumeType.Constant:
                        constantVolumeKernel = compute.FindKernel("WriteMaterialVolumeConstant");
                        if (materialVolumes[i].noiseTex != null)
                        {
                            constantVolumeKernel++;
                            command.SetComputeTextureParam(compute, constantVolumeKernel, noiseTexId, materialVolumes[i].noiseTex);
                            command.SetComputeVectorParam(compute, noiseScrollingSpeedId, materialVolumes[i].scrollingSpeed);
                            command.SetComputeVectorParam(compute, noiseTilingId, materialVolumes[i].tiling);
                        }
                        command.SetComputeTextureParam(compute, constantVolumeKernel, materialVolumeAId, materialVolumeATargetId);
                        command.SetComputeTextureParam(compute, constantVolumeKernel, materialVolumeBId, materialVolumeBTargetId);
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
        private RenderTexture accumulationVolume;
        private RenderTargetIdentifier accumulationVolumeTargetId;

        private readonly int accumulationVolumeId = Shader.PropertyToID("_AccumulationVolume");

        private int accumulationKernel;

        private void CreateAccumulationVolume()
        {
            CreateVolume(ref accumulationVolume, ref accumulationVolumeTargetId, "Accumulation Volume");
        }

        private void Accumulate()
        {
            accumulationKernel = compute.FindKernel("Accumulation");
            command.SetComputeTextureParam(compute, accumulationKernel, scatterVolumeId, scatterVolumeTargetId);
            command.SetComputeTextureParam(compute, accumulationKernel, accumulationVolumeId, accumulationVolumeTargetId);
            command.DispatchCompute(compute, accumulationKernel, dispatchWidth, dispatchHeight, 1);
        }
    }

    // Final
    public partial class VolumetricRenderer
    {
        private Vector3[] frustumCorners = new Vector3[4];
        private Vector4[] screenTriangleCorners = new Vector4[3];

        private readonly int accumulationVolumeSrvId = Shader.PropertyToID("_AccumulationVolumeSrv");
        private readonly int screenQuadCornersId = Shader.PropertyToID("_ScreenQuadCorners");
        private readonly int maxStepsId = Shader.PropertyToID("_MaxSteps");

        private void SetProperyFinal()
        {
            mainCamera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), mainCamera.farClipPlane, mainCamera.stereoActiveEye, frustumCorners);

            screenTriangleCorners[0] = new Vector4(frustumCorners[1].x, frustumCorners[1].y, mainCamera.farClipPlane, 0);
            screenTriangleCorners[1] = new Vector4(frustumCorners[0].x, -3.0f * frustumCorners[1].y, mainCamera.farClipPlane, 0);
            screenTriangleCorners[2] = new Vector4(3.0f * frustumCorners[2].x, frustumCorners[1].y, mainCamera.farClipPlane, 0);

            screenTriangleCorners[0] = mainCamera.transform.TransformVector(screenTriangleCorners[0]) / mainCamera.farClipPlane;
            screenTriangleCorners[1] = mainCamera.transform.TransformVector(screenTriangleCorners[1]) / mainCamera.farClipPlane;
            screenTriangleCorners[2] = mainCamera.transform.TransformVector(screenTriangleCorners[2]) / mainCamera.farClipPlane;

            volumetricMaterial.SetVectorArray(screenQuadCornersId, screenTriangleCorners);
            volumetricMaterial.SetVector(froxelToWorldParamsId, froxelToWorldParams);
            volumetricMaterial.SetMatrix(worldToViewMatId, worldToViewMat);
            volumetricMaterial.SetInt(maxStepsId, maxSteps);
            volumetricMaterial.SetInt(volumeWidthId, volumeWidth - 1);
            volumetricMaterial.SetInt(volumeHeightId, volumeHeight - 1);
            volumetricMaterial.SetInt(volumeDepthId, volumeDepth - 1);
            volumetricMaterial.SetFloat(volumeDistanceId, volumeDistance);
            volumetricMaterial.SetTexture(accumulationVolumeSrvId, accumulationVolume);
        }
    }

#if _DEBUG
    // Debug
    public partial class VolumetricRenderer
    {
        [Range(0, volumeDepth - 1)]
        public int slice = volumeDepth - 1;

        private void SetPropertyDebug()
        {
            volumetricMaterial.SetTexture(materialVolumeAId, materialVolumeA);
            volumetricMaterial.SetTexture(materialVolumeBId, materialVolumeB);
            volumetricMaterial.SetTexture(scatterVolumeId, scatterVolume);
            volumetricMaterial.SetTexture(accumulationVolumeId, accumulationVolume);
        }

        private void RenderDebug(RenderTexture source, RenderTexture destination, Material mat)
        {
            command.Blit(source, destination, mat, 1);
        }

        private void OnDrawGizmos()
        {
            if (Application.isPlaying)
            {
                Color oldColor = Gizmos.color;

                Gizmos.color = new Color(0.1f, 0.8f, 0.1f, 0.4f);
                Vector3 bl = FroxelPosToWorldPos(new Vector3(0, 0, slice));
                Vector3 br = FroxelPosToWorldPos(new Vector3(volumeWidth - 1, 0, slice));
                Vector3 tl = FroxelPosToWorldPos(new Vector3(0, volumeHeight - 1, slice));
                Vector3 tr = FroxelPosToWorldPos(new Vector3(volumeWidth - 1, volumeHeight - 1, slice));

                Mesh sliceMesh = new Mesh();
                sliceMesh.RecalculateBounds();
                sliceMesh.vertices = new Vector3[] { tl, bl, br, tr };
                sliceMesh.triangles = new int[] { 0, 3, 1, 1, 3, 2, 0, 1, 3, 1, 2, 3 };
                //sliceMesh.normals = new Vector3[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
                sliceMesh.RecalculateNormals();
                sliceMesh.RecalculateBounds();
                Gizmos.DrawMesh(sliceMesh, Vector3.zero, Quaternion.identity, Vector3.one);
                Gizmos.color = oldColor;
            }
        }
        Vector3 FroxelPosToWorldPos(Vector3 froxelPos)
        {
            Vector4 viewPos = Vector4.one;
            viewPos.z = (Mathf.Pow(froxelToWorldParams.z, froxelPos.z / (volumeDepth - 1)) - 1) * froxelToWorldParams.w + nearPlane;
            viewPos.x = (2.0f * froxelPos.x / (volumeWidth - 1) - 1) * viewPos.z / froxelToWorldParams.x;
            viewPos.y = (2.0f * froxelPos.y / (volumeHeight -1) - 1) * viewPos.z / froxelToWorldParams.y;
            Vector4 worldPos = viewToWorldMat * viewPos;
            worldPos /= worldPos.w;
            return new Vector3(worldPos.x, worldPos.y, worldPos.z);
        }
    }
#endif
}