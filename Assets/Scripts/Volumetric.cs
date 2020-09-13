using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

[Serializable]
[PostProcess(typeof(VolumetricRenderer), PostProcessEvent.AfterStack, "Custom/VolumetricRenderer", false)]
public sealed class Volumetric : PostProcessEffectSettings
{
    [DisplayName("Ray Marching Steps"), Range(0f, 100f)]
    public IntParameter maxSteps = new IntParameter { value = 50 };

    [DisplayName("Ray Marching Distance"), Range(0.1f, 10f)]
    public FloatParameter maxDistance = new FloatParameter { value = 10f };
}

public class VolumetricRenderer : PostProcessEffectRenderer<Volumetric>
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
}