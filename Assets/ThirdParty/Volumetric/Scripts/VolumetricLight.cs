using UnityEngine;
using UnityEngine.Rendering;

namespace Volumetric
{
    //[ExecuteInEditMode]
    [RequireComponent(typeof(Light))]
    public class VolumetricLight : MonoBehaviour
    {
        public bool hasVolumetricShadow = false;

        [HideInInspector]
        public Light theLight;

        private VolumetricRenderer volumetricRenderer;
        private CommandBuffer shadowCommand;
        private CommandBuffer dirShadowCommand;

        private void Awake()
        {
            shadowCommand = new CommandBuffer()
            {
                name = "Volumetric Light Command"
            };
            dirShadowCommand = new CommandBuffer()
            {
                name = "Volumetric Dir Light Command"
            };
            //dirShadowCommand.SetGlobalTexture("_ShadowMapTexture", new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive));
        }

        private void OnEnable()
        {
            theLight = GetComponent<Light>();
            if (theLight.type == LightType.Directional)
            {
                theLight.AddCommandBuffer(LightEvent.AfterShadowMap, dirShadowCommand);
                theLight.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, shadowCommand);
            }
            else
            {
                theLight.AddCommandBuffer(LightEvent.AfterShadowMap, shadowCommand);
            }

            // TODO: Better way to get volumetricRenderer.
            volumetricRenderer = GameObject.FindObjectOfType<VolumetricRenderer>();
            if (volumetricRenderer != null)
            {
                volumetricRenderer.RegisterLight(this);
                volumetricRenderer.WriteShadowVolumeEvent += WriteShadowVolume;
                volumetricRenderer.WriteScatterVolumeEvent += WriteScatterVolume;
            }
        }

        private void OnDisable()
        {
            if (theLight.type == LightType.Directional)
            {
                theLight.RemoveCommandBuffer(LightEvent.AfterShadowMap, dirShadowCommand);
                theLight.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, shadowCommand);
            }
            else
            {
                theLight.RemoveCommandBuffer(LightEvent.AfterShadowMap, shadowCommand);
            }

            if (volumetricRenderer != null)
            {
                volumetricRenderer.UnregisterLight(this);
                volumetricRenderer.WriteShadowVolumeEvent -= WriteShadowVolume;
                volumetricRenderer.WriteScatterVolumeEvent -= WriteScatterVolume;
            }
        }

        // TODO: Should Cast Shadow
        private void WriteShadowVolume()
        {
            switch (theLight.type)
            {
                case LightType.Directional:
                    volumetricRenderer.DirLightShadow(shadowCommand, dirShadowCommand);
                    break;

                case LightType.Point:
                    break;

                case LightType.Spot:
                    break;

                default:
                    break;
            }
        }

        private void WriteScatterVolume()
        {
            switch (theLight.type)
            {
                case LightType.Point:
                    volumetricRenderer.WriteScatterVolumePoint(shadowCommand, this);
                    break;

                case LightType.Spot:
                    break;

                default:
                    break;
            }
        }
    }
}