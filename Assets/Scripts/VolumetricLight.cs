using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

namespace Volumetric
{
    [ExecuteInEditMode]
    [RequireComponent(typeof(Light))]
    public class VolumetricLight : MonoBehaviour
    {
        public bool shouldCastShadow = false;

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
            yield return null; // Wait for PostProcessLayer.sortedBundles to construct.

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
                        theLight.AddCommandBuffer(LightEvent.AfterShadowMap, volumetricRenderer.shadowCommand);
                        volumetricRenderer.WriteShadowVolumeEvent += WriteShadowVolume;
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (volumetricRenderer != null)
            {
                volumetricRenderer.UnregisterLight(this);
                theLight.RemoveCommandBuffer(LightEvent.AfterShadowMap, volumetricRenderer.shadowCommand);
                volumetricRenderer.WriteShadowVolumeEvent -= WriteShadowVolume;
            }
        }

        // TODO: Should Cast Shadow
        private void WriteShadowVolume()
        {
            switch (theLight.type)
            {
                case LightType.Directional:
                    volumetricRenderer.DirLightShadow();
                    break;
                case LightType.Point:
                    break;
                case LightType.Spot:
                    break;
                default:
                    break;
            }
        }
    }
}