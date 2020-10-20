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
        private Shader shader;
        private ComputeShader compute;
        private Vector3[] frustumCorners = new Vector3[4];
        private Vector4[] screenTriangleCorners = new Vector4[3];

        private const int volumeWidth = 160;
        private const int volumeHeight = 88;
        private const int volumeDepth = 64;
        private const int dispatchWidth = volumeWidth / 8;
        private const int dispatchHeight = volumeHeight / 8;
        private const int dispatchDepth = volumeDepth / 8;

        public override void Init()
        {
            CreateVolumes();
        }

        public override void Render(PostProcessRenderContext context)
        {
            context.command.BeginSample("Volumetric");
            context.command.Clear();

            camera = context.camera;
            shader = context.resources.shaders.volumetric;
            compute = context.resources.computeShaders.volumetric;
            PropertySheet sheet = context.propertySheets.Get(shader);

            SetPropertyClearVolume();
            ClearAllVolumes(context.command);

            SetPropertyMaterialVolume(sheet);
            WriteMaterialVolume(context.command);

            SetPropertyScatterVolume(sheet);
            WriteScatterVolume(context.command);

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


            //context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);

            //// Test
            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 1);
            context.command.EndSample("Volumetric");
        }
    }

    // Misc
    public partial class VolumetricRenderer
    {
        private int clearKernel;

        private void SetPropertyClearVolume()
        {
            clearKernel = compute.FindKernel("ClearAllVolumes");
            compute.SetTexture(clearKernel, materialVolumeAId, materialVolume_A);
            compute.SetTexture(clearKernel, materialVolumeBId, materialVolume_B);
            compute.SetTexture(clearKernel, scatterVolumeId, scatterVolume);
        }

        private void ClearAllVolumes(CommandBuffer command)
        {
            command.DispatchCompute(compute, clearKernel, dispatchWidth, dispatchHeight, dispatchDepth);
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

        private void CreateVolumes()
        {
            CreateVolume(ref materialVolume_A, ref materialVolumeATargetId, "Material Volume A", RenderTextureFormat.ARGBHalf);
            CreateVolume(ref materialVolume_B, ref materialVolumeBTargetId, "Material Volume B", RenderTextureFormat.ARGBHalf);
            CreateVolume(ref scatterVolume, ref scatterVolumeTargetId, "Scatter  Volume", RenderTextureFormat.ARGBHalf);
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

        private void SetPropertyMaterialVolume(PropertySheet sheet)
        {
            sheet.material.SetTexture(materialVolumeAId, materialVolume_A);
            sheet.material.SetTexture(materialVolumeBId, materialVolume_B);

            constantVolumeKernel = compute.FindKernel("WriteMaterialVolumeConstant");
            compute.SetTexture(constantVolumeKernel, materialVolumeAId, materialVolume_A);
            compute.SetTexture(constantVolumeKernel, materialVolumeBId, materialVolume_B);
        }

        private void WriteMaterialVolume(CommandBuffer commandBuffer)
        {
            for (int i = 0; i < materialVolumes.Count; i++)
            {
                switch (materialVolumes[i].volumeType)
                {
                    case VolumetricMaterialVolume.VolumeType.Constant:
                        commandBuffer.SetComputeVectorParam(compute, scatteringCoefId, materialVolumes[i].scatteringCoef);
                        commandBuffer.SetComputeFloatParam(compute, absorptionCoefId, materialVolumes[i].absorptionCoef);
                        commandBuffer.SetComputeFloatParam(compute, phaseGId, materialVolumes[i].phaseG);
                        commandBuffer.DispatchCompute(compute, constantVolumeKernel, dispatchWidth, dispatchHeight, dispatchDepth);
                        break;
                    case VolumetricMaterialVolume.VolumeType.Box:
                        break;
                    default:
                        break;
                }
            }
        }
    }

    // In-Scatter Volume
    public partial class VolumetricRenderer
    {
        private RenderTexture scatterVolume; // RGB: Scattering, A: Transmittance
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

        private void SetPropertyScatterVolume(PropertySheet sheet)
        {
            sheet.material.SetTexture(scatterVolumeId, scatterVolume);

            scatterVolumeDirKernel = compute.FindKernel("WriteScatterVolumeDir");
            compute.SetTexture(scatterVolumeDirKernel, materialVolumeAId, materialVolume_A);
            compute.SetTexture(scatterVolumeDirKernel, materialVolumeBId, materialVolume_B);
            compute.SetTexture(scatterVolumeDirKernel, scatterVolumeId, scatterVolume);
        }

        private void WriteScatterVolume(CommandBuffer command)
        {
            for (int i = 0; i < lights.Count; i++)
            {
                Color lightColor = lights[i].theLight.color * lights[i].theLight.intensity;
                lightColor.r = Mathf.Pow(lightColor.r, 2.2f);
                lightColor.g = Mathf.Pow(lightColor.g, 2.2f);
                lightColor.b = Mathf.Pow(lightColor.b, 2.2f);

                command.SetComputeVectorParam(compute, lightColorId, lightColor);
                command.SetComputeVectorParam(compute, lightDirId, lights[i].theLight.transform.forward);
                command.SetComputeVectorParam(compute, viewDirId, camera.transform.forward);

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
}