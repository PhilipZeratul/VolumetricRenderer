using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace Volumetric
{
    [Serializable]
    [PostProcess(typeof(VolumetricRenderer), PostProcessEvent.BeforeTransparent, "Custom/VolumetricRenderer", false)]
    public sealed class Volumetric : PostProcessEffectSettings
    {
        [DisplayName("Ray Marching Steps"), Range(0f, 100f)]
        public IntParameter maxSteps = new IntParameter { value = 50 };

        [DisplayName("Ray Marching Distance"), Range(0.1f, 10f)]
        public FloatParameter maxDistance = new FloatParameter { value = 10f };
    }

    // Post Processing
    public partial class VolumetricRenderer : PostProcessEffectRenderer<Volumetric>
    {
        private Camera camera;
        private CommandBuffer commandBuffer;
        private Shader shader;
        private ComputeShader compute;
        private Vector3[] frustumCorners = new Vector3[4];
        private Vector4[] screenTriangleCorners = new Vector4[3];

        public override void Init()
        {
            commandBuffer = new CommandBuffer()
            {
                name = "Volumetric Command Buffer"
            };
            shader = Shader.Find("Volumetric/VolumetricRenderer");

            CreateVolumes();
        }

        public override void Render(PostProcessRenderContext context)
        {
            camera = context.camera;
            PropertySheet sheet = context.propertySheets.Get(shader);
            compute = context.resources.computeShaders.volumetric;

            SetProperties();
            WriteMaterialVolume();

            camera.CalculateFrustumCorners(new Rect(0, 0, 1, 1), camera.farClipPlane, camera.stereoActiveEye, frustumCorners);

            screenTriangleCorners[0] = new Vector4(frustumCorners[1].x, frustumCorners[1].y, camera.farClipPlane, 0);
            screenTriangleCorners[1] = new Vector4(frustumCorners[0].x, -3.0f * frustumCorners[1].y, camera.farClipPlane, 0);
            screenTriangleCorners[2] = new Vector4(3.0f * frustumCorners[2].x, frustumCorners[1].y, camera.farClipPlane, 0);

            screenTriangleCorners[0] = camera.transform.TransformVector(screenTriangleCorners[0]) / camera.farClipPlane;
            screenTriangleCorners[1] = camera.transform.TransformVector(screenTriangleCorners[1]) / camera.farClipPlane;
            screenTriangleCorners[2] = camera.transform.TransformVector(screenTriangleCorners[2]) / camera.farClipPlane;

            sheet.properties.SetVectorArray("_ScreenQuadCorners", screenTriangleCorners);
            sheet.properties.SetInt("_MaxSteps", settings.maxSteps);
            sheet.properties.SetFloat("_MaxDistance", settings.maxDistance);


            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }

    // Volume
    public partial class VolumetricRenderer
    {
        private RenderTexture materialVolume_A; // RGB: Scattering, A: Absorption
        private RenderTargetIdentifier materialVolumeID_A;
        private RenderTexture materialVolume_B; // R: Phase g, G: Global emissive intensity, B: Ambient intensity, A: Water droplet density
        private RenderTargetIdentifier materialVolumeID_B;
        private RenderTexture scatterVolume; // RGB: Scattering, A: Transmittance
        private RenderTargetIdentifier scatterVolumeID;

        private const int volumeWidth = 160;
        private const int volumeHeight = 88;
        private const int volumeDepth = 64;

        private readonly int materialVolumeAId = Shader.PropertyToID("_MaterialVolume_A");
        private readonly int materialVolumeBId = Shader.PropertyToID("_MaterialVolume_B");
        private readonly int scatteringCoefId = Shader.PropertyToID("_ScatteringCoef");
        private readonly int absorptionCoefId = Shader.PropertyToID("_AbsorptionCoef");
        private readonly int phaseGId = Shader.PropertyToID("_PhaseG");

        private int constantVolumeKernel;

        private List<VolumetricMaterialVolume> materialVolumes = new List<VolumetricMaterialVolume>();

        private void CreateVolumes()
        {
            CreateVolume(ref materialVolume_A, ref materialVolumeID_A, "Material Volume A", RenderTextureFormat.ARGBInt);
            CreateVolume(ref materialVolume_B, ref materialVolumeID_B, "Material Volume B", RenderTextureFormat.ARGBInt);
            CreateVolume(ref scatterVolume, ref scatterVolumeID, "Scatter  Volume", RenderTextureFormat.ARGBHalf);
        }

        private void CreateVolume(ref RenderTexture volume, ref RenderTargetIdentifier id, string name, RenderTextureFormat format,
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

        private void SetProperties()
        {
            constantVolumeKernel = compute.FindKernel("WriteMaterialVolumeConstant");
            compute.SetTexture(constantVolumeKernel, materialVolumeAId, materialVolume_A);
            compute.SetTexture(constantVolumeKernel, materialVolumeBId, materialVolume_B);
        }

        private void WriteMaterialVolume()
        {
            for (int i = 0; i < materialVolumes.Count; i++)
            {
                switch (materialVolumes[i].volumeType)
                {
                    case VolumetricMaterialVolume.VolumeType.Constant:
                        commandBuffer.SetComputeVectorParam(compute, scatteringCoefId, materialVolumes[i].scatteringCoef);
                        commandBuffer.SetComputeFloatParam(compute, absorptionCoefId, materialVolumes[i].absorptionCoef);
                        commandBuffer.SetComputeFloatParam(compute, phaseGId, materialVolumes[i].phaseG);
                        commandBuffer.DispatchCompute(compute, constantVolumeKernel, volumeWidth / 8, volumeHeight / 8, volumeDepth / 8);
                        break;
                    case VolumetricMaterialVolume.VolumeType.Box:
                        break;
                    default:
                        break;
                }
            }
        }
    }
}