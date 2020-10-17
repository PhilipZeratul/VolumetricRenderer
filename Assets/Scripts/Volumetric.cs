using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace Volumetric
{
    [Serializable]
    [PostProcess(typeof(VolumetricRenderer), PostProcessEvent.AfterStack, "Custom/VolumetricRenderer", false)]
    public sealed class Volumetric : PostProcessEffectSettings
    {
        [DisplayName("Ray Marching Steps"), Range(0f, 100f)]
        public IntParameter maxSteps = new IntParameter { value = 50 };

        [DisplayName("Ray Marching Distance"), Range(0.1f, 10f)]
        public FloatParameter maxDistance = new FloatParameter { value = 10f };
    }

    public partial class VolumetricRenderer : PostProcessEffectRenderer<Volumetric>
    {
        private Camera camera;
        private CommandBuffer commandBuffer;
        private Shader shader;
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

        private void WriteMaterialVolume()
        {

        }
    }

    public partial class VolumetricRenderer
    {
        private RenderTexture materialVolume_A; // RGB: Scattering, A: Absorption
        private RenderTargetIdentifier materialVolumeID_A;
        private RenderTexture materialVolume_B; // R: Phase g, G: Global emissive intensity, B: Ambient intensity, A: Water droplet density
        private RenderTargetIdentifier materialVolumeID_B;
        private RenderTexture scatterVolume; // RGB: Scattering, A: Transmittance
        private RenderTargetIdentifier scatterVolumeID;

        private const int VolumeWidth = 160;
        private const int VolumeHeight = 88;
        private const int VolumeDepth = 64;

        private void CreateVolumes()
        {
            CreateVolume(ref materialVolume_A, ref materialVolumeID_A, "Material Volume A", RenderTextureFormat.ARGBInt);
            CreateVolume(ref materialVolume_B, ref materialVolumeID_B, "Material Volume B", RenderTextureFormat.ARGBInt);
            CreateVolume(ref scatterVolume, ref scatterVolumeID, "Scatter  Volume", RenderTextureFormat.ARGBHalf);
        }

        private void CreateVolume(ref RenderTexture volume, ref RenderTargetIdentifier id, string name, RenderTextureFormat format,
                                  int width = VolumeWidth, int height = VolumeHeight, int depth = VolumeDepth)
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
    }
}