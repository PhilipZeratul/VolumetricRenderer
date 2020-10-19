using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using Volumetric;

[ExecuteInEditMode]
public class VolumetricMaterialVolume : MonoBehaviour
{
    public enum VolumeType
    {
        Constant,
        Box
    }

    public enum BlendType
    {
        Additive,
        AlphaBlend
    }

    public VolumeType volumeType;
    public BlendType blendType;

    [Space]
    public Color scatteringCoef;
    public float absorptionCoef;
    public float phaseG;
    // Global emissive intensity
    // Ambient intensity
    // Water droplet density

    private VolumetricRenderer volumetricRenderer;

    private void OnEnable()
    {
        StartCoroutine(nameof(RegisterCoroutine));
    }

    private IEnumerator RegisterCoroutine()
    {
        // Wait for PostProcessLayer.sortedBundles to construct.
        yield return null;

        // TODO: Better way to get PostProcessLayer.
        PostProcessLayer postLayer = GameObject.FindObjectOfType<PostProcessLayer>();
        if (postLayer != null)
        {
            List<PostProcessLayer.SerializedBundleRef> sortedBundles = postLayer.sortedBundles[PostProcessEvent.BeforeTransparent];
            string typeName = typeof(Volumetric.Volumetric).AssemblyQualifiedName;
            PostProcessLayer.SerializedBundleRef bundleRef = sortedBundles.Find(x => x.assemblyQualifiedName == typeName);
            if (bundleRef != null)
            {
                volumetricRenderer = bundleRef.bundle.renderer as VolumetricRenderer;
                if (volumetricRenderer != null)
                {
                    volumetricRenderer.RegisterMaterialVolume(this);
                }
            }
        }
    }

    private void OnDisable()
    {
        if (volumetricRenderer != null)
        {
            volumetricRenderer.UnregisterMaterialVolume(this);
        }
    }
}
