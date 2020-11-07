#ifndef VOLUMETRIC_HELPER
#define VOLUMETRIC_HELPER

#include "UnityCG.cginc"

#define PI 3.1415926535
#define EXPONENT 8.0

// Parameters
SamplerState sampler_point_clamp;
SamplerState sampler_bilinear_clamp;
SamplerComparisonState sampler_ShadowMapTexture;

RWTexture3D<float> _ShadowVolume, _PrevShadowVolume; // R: Visibility
RWTexture3D<float4> _MaterialVolume_A, _PrevMaterialVolume_A; // RGB: Scattering Coef, A: Absorption
RWTexture3D<float4> _MaterialVolume_B, _PrevMaterialVolume_B; // R: Phase G
RWTexture3D<float4> _ScatterVolume, _PrevScatterVolume; // RGB: Scattered Light, A: Transmission
RWTexture2D<float4> _AccumulationTex; // RGB: Accumulated Light, A: Transmittance

RWTexture2D<float> _EsmShadowMapUav;
Texture2D _EsmShadowMapTex;
Texture2D _ShadowMapTexture;
Texture2D _CameraDepthTexture;

float3 _ScatteringCoef;
float _AbsorptionCoef;
float _PhaseG;

float3 _LightColor;
float3 _LightDir;

int _VolumeWidth, _VolumeHeight, _VolumeDepth; // Value - 1
float _NearPlane, _VolumeDistance;

// https://www.desmos.com/calculator/pd3c4qqsng
float4 _FroxelToWorldParams; // x: cot(Fov_x / 2), y, cot(Fov_y / 2), z: c * (f - n * f / d) + 1, w: d / c / f
float4x4 _ViewToWorldMat;
float4x4 _WorldToViewMat;

float _TemporalOffset;
float _TemporalBlendAlpha;

// Helper Functions
// TODO:  Cornette-Shanks anisotropic phase function
float PhaseFunction(float g, float cosTheta)
{
    float gSquared = g * g;
    float hg = (1 - gSquared) / pow(1 + gSquared - 2.0 * g * cosTheta, 1.5) / 4.0 / PI;
    return hg;
}

float LogWithBase(float base, float x)
{
    return log(x) / log(base);
}

float Remap(float value, float inputFrom, float inputTo, float outputFrom, float outputTo)
{
    return (value - inputFrom) / (inputTo - inputFrom) * (outputTo - outputFrom) + outputFrom;
}

float3 FroxelPos2WorldPos(float3 froxelPos)
{
    float4 viewPos = 1;
    viewPos.z = (pow(_FroxelToWorldParams.z, froxelPos.z / _VolumeDepth) - 1) * _FroxelToWorldParams.w + _NearPlane;
    viewPos.x = (2.0 * froxelPos.x / _VolumeWidth - 1) * viewPos.z / _FroxelToWorldParams.x;
    viewPos.y = (2.0 * froxelPos.y / _VolumeHeight - 1) * viewPos.z / _FroxelToWorldParams.y;
    float4 worldPos = mul(_ViewToWorldMat, viewPos);
    worldPos /= worldPos.w;
    return worldPos.xyz;
}

float3 WorldPos2FroxelPos(float3 worldPos)
{
    float4 viewPos = mul(_WorldToViewMat, float4(worldPos, 1));
    viewPos /= viewPos.w;
    float3 froxelPos = 0;
    froxelPos.z = _VolumeDepth * LogWithBase(_FroxelToWorldParams.z, (viewPos.x - _NearPlane) / _FroxelToWorldParams.w + 1);
    froxelPos.x = _VolumeWidth * (_FroxelToWorldParams.x * viewPos.x / viewPos.z + 1) / 2.0;
    froxelPos.y = _VolumeHeight * (_FroxelToWorldParams.y * viewPos.y / viewPos.z + 1) / 2.0;
    return froxelPos;
}

float2 FroxelPos2Uv(float2 froxelPos)
{
    return float2(froxelPos.x / _VolumeWidth, froxelPos.y / _VolumeHeight);
}

float3 WorldPos2FroxelUvw(float3 worldPos)
{
    float3 froxelPos = WorldPos2FroxelPos(worldPos);
    float3 froxelUvw = froxelPos / float3(_VolumeWidth, _VolumeHeight, _VolumeDepth);
    return froxelUvw;
}

float Depth2FroxelPosZ(float depth)
{
    float z = floor(Remap(depth, 0.0, 1.0, 0.0, _VolumeDepth - 1));
    return z;
}

float Rgb2Gray(float3 c)
{
    float gray = c.r * 0.3 + c.g * 0.59 + c.b * 0.11;
    return gray;
}

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
    float shadow = _ShadowMapTexture.SampleCmpLevelZero(sampler_ShadowMapTexture, shadowCoord.xy, shadowCoord.z);
    shadow = lerp(_LightShadowData.r, 1.0, shadow);

    float4 res = shadow;
    return res;
}

#endif //VOLUMETRIC_HELPER