using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Volumetric
{
    [RequireComponent(typeof(Light))]
    public class VolumetricLight : MonoBehaviour
    {
        [HideInInspector]
        public Light theLight;

        private VolumetricRenderer volumetricRenderer;

        private void OnEnable()
        {
            theLight = GetComponent<Light>();
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
                        volumetricRenderer.RegisterLight(this);
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (volumetricRenderer != null)
            {
                volumetricRenderer.UnregisterLight(this);
            }
        }
    }
}