using UnityEngine;
using UnityEngine.Rendering;

namespace Volumetric
{
    //[ExecuteInEditMode]
    [RequireComponent(typeof(Light))]
    public class VolumetricLight : MonoBehaviour
    {
        public bool volumetricShadow = false;

        [HideInInspector]
        public Light theLight;

        private VolumetricRenderer volumetricRenderer;

        private void OnEnable()
        {
            theLight = GetComponent<Light>();

            // TODO: Better way to get PostProcessLayer.
            volumetricRenderer = GameObject.FindObjectOfType<VolumetricRenderer>();
            if (volumetricRenderer != null)
            {
                volumetricRenderer.RegisterLight(this);
                theLight.AddCommandBuffer(LightEvent.AfterShadowMap, volumetricRenderer.shadowCommand);
                volumetricRenderer.WriteShadowVolumeEvent += WriteShadowVolume;
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
                    WriteShadowVolumeDir();
                    break;

                case LightType.Point:
                    break;

                case LightType.Spot:
                    break;

                default:
                    break;
            }
        }

        private void WriteShadowVolumeDir()
        {
            volumetricRenderer.DirLightShadow();
        }

        private void CalculateMatrices()
        {
            Matrix4x4 lightViewMat;
            Matrix4x4 lightProjMat;

            switch (theLight.type)
            {
                case LightType.Spot:
                    break;

                case LightType.Directional:
                    lightViewMat = Matrix4x4.TRS(-theLight.transform.position, Quaternion.Inverse(theLight.transform.rotation), new Vector3(1, 1, 1));

                    break;

                case LightType.Point:
                    break;

                default:
                    break;
            }
        }
    }
}