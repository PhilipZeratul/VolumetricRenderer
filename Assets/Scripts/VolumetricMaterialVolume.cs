using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Volumetric
{
    [ExecuteInEditMode]
    public class VolumetricMaterialVolume : MonoBehaviour
    {
        // TODO: 1000m?
        private const float scatterScale = 0.00692f;
        private const float absorptScale = 0.00077f;

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

        [SerializeField]
        private Color scatteringColor = new Color(0.58f, 0.58f, 0.58f);
        [SerializeField]
        [Range(0.00001f, 1.0f)]
        private float absorption = 0.58f;
        [Range(0.0f, 1.0f)]
        public float phaseG = 0.002f;
        // Global emissive intensity
        // Ambient intensity
        // Water droplet density

        public Color ScatteringCoef
        {
            get { return scatteringColor * scatterScale; }
        }
        public float AbsorptionCoef
        {
            get { return absorption * absorptScale; }
        }

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
                string typeName = typeof(Volumetric).AssemblyQualifiedName;
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
}