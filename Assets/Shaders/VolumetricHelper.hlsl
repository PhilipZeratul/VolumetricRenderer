#ifndef VOLUMETRIC_HELPER
#define VOLUMETRIC_HELPER

#include "HLSLSupport.cginc"

#define PI 3.1415926535

// Parameters
RWTexture3D<float> _ShadowVolume; // R: Visibility
RWTexture3D<float4> _MaterialVolume_A; // RGB: Scattering Coef, A: Absorption
RWTexture3D<float4> _MaterialVolume_B; // R: Phase G
RWTexture3D<float4> _ScatterVolume; // RGB: Scattered Light, A: 
RWTexture2D<float4> _AccumulationTex; // RGB: Accumulated Light, A: Transmittance

//Texture2D<float4> _ShadowMapTexture; SamplerComparisonState sampler_ShadowMapTexture;
UNITY_DECLARE_SHADOWMAP(_ShadowMapTexture);

float3 _ScatteringCoef;
float _AbsorptionCoef;
float _PhaseG;

float3 _LightColor;
float3 _LightDir;
float3 _ViewDir;

int _VolumeWidth, _VolumeHeight, _VolumeDepth;
float _NearPlane, _VolumeDistance;

float4x4 _invFroxelVPMat;

// Helper Functions
float PhaseFunction(float g, float cosTheta)
{
    float gSquared = g * g;
    float hg = (1 - gSquared) / pow(1 + gSquared - 2.0 * g * cosTheta, 1.5) / 4.0 / PI;
    return hg;
}

float Remap(float value, float inputFrom, float inputTo, float outputFrom, float outputTo)
{
    return (value - inputFrom) / (inputTo - inputFrom) * (outputTo - outputFrom) + outputFrom;
}

// TODO: Integraget remap into matrix.
// (0, 0, 0) - (width, height, depth) -> (-1, -1, 0, 1) - (1, 1, 1, 1)
float4 FroxelPos2ClipPos(uint3 froxelPos)
{
    float4 clipPos = 0;
    clipPos.x = Remap(froxelPos.x, 0.0, _VolumeWidth - 1, -1.0, 1.0);
    clipPos.y = Remap(froxelPos.y, 0.0, _VolumeHeight - 1, -1.0, 1.0);
    clipPos.z = Remap(froxelPos.z, 0.0, _VolumeDepth - 1, 0.0, 1.0); 
    clipPos.w = 1;
    return clipPos;
}

float4 FroxelPos2WorldPos(uint3 froxelPos)
{
    float4 clipPos = FroxelPos2ClipPos(froxelPos);
    float z = Remap(clipPos.z, 0.0, 1.0, _NearPlane, _VolumeDistance);
    clipPos *= z;
    float4 worldPos = mul(_invFroxelVPMat, clipPos);
    worldPos /= worldPos.w;
    return worldPos;
}

float Rgb2Gray(float3 c)
{
    float gray = c.r * 0.3 + c.g * 0.59 + c.b * 0.11;
    return gray;
}

// Shadows
float4 unity_ShadowSplitSpheres[4];
float4 unity_ShadowSplitSqRadii;
float4x4 unity_WorldToShadow[4];
half4 _LightShadowData;

/**
 * Gets the cascade weights based on the world position of the fragment and the poisitions of the split spheres for each cascade.
 * Returns a float4 with only one component set that corresponds to the appropriate cascade.
 */
inline float4 getCascadeWeights_splitSpheres(float3 wpos)
{
    float3 fromCenter0 = wpos.xyz - unity_ShadowSplitSpheres[0].xyz;
    float3 fromCenter1 = wpos.xyz - unity_ShadowSplitSpheres[1].xyz;
    float3 fromCenter2 = wpos.xyz - unity_ShadowSplitSpheres[2].xyz;
    float3 fromCenter3 = wpos.xyz - unity_ShadowSplitSpheres[3].xyz;
    float4 distances2 = float4(dot(fromCenter0, fromCenter0), dot(fromCenter1, fromCenter1), dot(fromCenter2, fromCenter2), dot(fromCenter3, fromCenter3));
    float4 weights = float4(distances2 < unity_ShadowSplitSqRadii);
    weights.yzw = saturate(weights.yzw - weights.xyz);
    return weights;
}

/**
 * Returns the shadowmap coordinates for the given fragment based on the world position and z-depth.
 * These coordinates belong to the shadowmap atlas that contains the maps for all cascades.
 */
inline float4 getShadowCoord(float4 wpos, float4 cascadeWeights)
{
    float3 sc0 = mul(unity_WorldToShadow[0], wpos).xyz;
    float3 sc1 = mul(unity_WorldToShadow[1], wpos).xyz;
    float3 sc2 = mul(unity_WorldToShadow[2], wpos).xyz;
    float3 sc3 = mul(unity_WorldToShadow[3], wpos).xyz;
    float4 shadowMapCoordinate = float4(sc0 * cascadeWeights[0] + sc1 * cascadeWeights[1] + sc2 * cascadeWeights[2] + sc3 * cascadeWeights[3], 1);
#if defined(UNITY_REVERSED_Z)
    float  noCascadeWeights = 1 - dot(cascadeWeights, float4(1, 1, 1, 1));
    shadowMapCoordinate.z += noCascadeWeights;
#endif
    return shadowMapCoordinate;
}

float SampleShadow(float4 wpos)
{
    float4 cascadeWeights = getCascadeWeights_splitSpheres(wpos);
    float4 shadowCoord = getShadowCoord(wpos, cascadeWeights);

    //1 tap hard shadow
    //float shadow = _ShadowMapTexture.Sample(sampler_ShadowMapTexture, shadowCoord).r;
    float shadow = UNITY_SAMPLE_SHADOW(_ShadowMapTexture, shadowCoord);
    shadow = lerp(_LightShadowData.r, 1.0, shadow);

    float4 res = shadow;
    return res;
}

#endif //VOLUMETRIC_HELPER