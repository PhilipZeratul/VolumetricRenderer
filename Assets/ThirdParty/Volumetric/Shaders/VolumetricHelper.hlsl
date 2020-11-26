#ifndef VOLUMETRIC_HELPER
#define VOLUMETRIC_HELPER

#include "UnityCG.cginc"
#include "Random.hlsl"

#define PI 3.1415926535

//
// ------------------------------------ Parameters ----------------------------------------------
//
SamplerState sampler_point_clamp;
SamplerState sampler_bilinear_clamp;
SamplerState sampler_bilinear_repeat;
SamplerComparisonState sampler_bilinear_clamp_compare;

RWTexture3D<float> _ShadowVolume, _PrevShadowVolume; // R: Visibility
RWTexture3D<float4> _MaterialVolume_A, _PrevMaterialVolume_A; // RGB: Scattering Coef, A: Absorption
RWTexture3D<float4> _MaterialVolume_B; // R: Phase G
RWTexture3D<float4> _ScatterVolume, _PrevScatterVolume; // RGB: Scattered Light, A: Transmission
RWTexture3D<float4> _AccumulationVolume, _PrevAccumulationVolume; // RGB: Accumulated Light, A: Total Transmittance
Texture3D<float> _PrevShadowVolumeSrv;
Texture3D<float4> _ScatterVolumeSrv, _PrevScatterVolumeSrv;
Texture3D<float4> _PrevAccumulationVolumeSrv;

Texture2D<float> _ShadowMapTexture;
TextureCube<float> _ShadowCubeMapTexture;
Texture2D<float> _CameraDepthTexture;
Texture2D<float> _LightTextureB0;
Texture2D<float> _LightTexture0;

float3 _ScatteringCoef;
float _AbsorptionCoef;
float _PhaseG;
Texture3D<float> _NoiseTex;
float3 _NoiseScrollingSpeed;
float3 _NoiseTiling;

float3 _LightColor;
float3 _LightDir;
float _LightAttenuationMultiplier;

// Point light
float4 _LightPos;
float _PointLightRange;

// Spot light
float4x4 unity_WorldToLight;
float3 _SpotLightDir;
float _SpotCosOuterCone;
float _SpotCosInnerConeRcp;

int _VolumeWidth, _VolumeHeight, _VolumeDepth;
float _NearPlane, _VolumeDistance;

float4 _FroxelToWorldParams; // x: cot(Fov_x / 2), y: cot(Fov_y / 2), 
                             // z: depthDistribution * (volumeDepth - near * volumeDepth / volumeDistance) + 1, 
                             // w: volumeDistance / depthDistribution / volumeDepth
float4x4 _ViewToWorldMat;
float4x4 _WorldToViewMat;
float4x4 _PrevWorldToViewMat;

float _TemporalBlendAlpha;
float3 _FroxelSampleOffset; // xy: -0.5 <-> 0.5, z: 1/14 <-> 13/14
// TODO: _IsTemporalHistoryValid
uint _IsTemporalHistoryValid;

//
// ------------------------------------ Miscellaneous ----------------------------------------------
//

float LogWithBase(float base, float x)
{
    return log(x) / log(base);
}

float Remap(float value, float inputFrom, float inputTo, float outputFrom, float outputTo)
{
    return (value - inputFrom) / (inputTo - inputFrom) * (outputTo - outputFrom) + outputFrom;
}

float Rgb2Gray(float3 c)
{
    float gray = c.r * 0.3 + c.g * 0.59 + c.b * 0.11;
    return gray;
}

//
// ------------------------------------ Phase Function ----------------------------------------------
//

// TODO:  Cornette-Shanks anisotropic phase function
float PhaseFunction(float g, float cosTheta)
{
    float gSquared = g * g;
    float hg = (1 - gSquared) / pow(1 + gSquared - 2.0 * g * cosTheta, 1.5) / 4.0 / PI;
    return hg;
}

//
// ------------------------------------ Position Transformation ----------------------------------------------
//

float3 DiscreteToContinuous(uint3 froxelPosDisc)
{
    return froxelPosDisc + 0.5;
}

uint3 ContinuousToDiscrete(float3 froxelPosCont)
{
    return floor(froxelPosCont);
}

float3 JitterFroxelPos(float3 froxelPos)
{
    float3 jitter = 0;
    jitter.xy = _FroxelSampleOffset.xy;
    jitter.z += _FroxelSampleOffset.z;
    froxelPos += jitter;
    return froxelPos;
}

// https://www.desmos.com/calculator/pd3c4qqsng
float3 FroxelPosToViewPos(float3 froxelPos)
{
    float3 viewPos = 1;
    viewPos.z = (pow(_FroxelToWorldParams.z, froxelPos.z / _VolumeDepth) - 1) * _FroxelToWorldParams.w + _NearPlane;
    viewPos.x = (2.0 * froxelPos.x / _VolumeWidth - 1) * viewPos.z / _FroxelToWorldParams.x;
    viewPos.y = (2.0 * froxelPos.y / _VolumeHeight - 1) * viewPos.z / _FroxelToWorldParams.y;
    return viewPos;
}

float3 FroxelPosToWorldPos(float3 froxelPos)
{
    float3 viewPos = FroxelPosToViewPos(froxelPos);
    float4 worldPos = mul(_ViewToWorldMat, float4(viewPos, 1));
    worldPos /= worldPos.w;
    return worldPos.xyz;
}

float3 ViewPosToFroxelPos(float3 viewPos)
{
    float3 froxelPos = 0;
    froxelPos.z = _VolumeDepth * LogWithBase(_FroxelToWorldParams.z, (viewPos.z - _NearPlane) / _FroxelToWorldParams.w + 1);
    froxelPos.x = _VolumeWidth * (_FroxelToWorldParams.x * viewPos.x / viewPos.z + 1) / 2.0;
    froxelPos.y = _VolumeHeight * (_FroxelToWorldParams.y * viewPos.y / viewPos.z + 1) / 2.0;
    //froxelPos = round(froxelPos);

    return froxelPos;
}

float3 WorldPosToFroxelPos(float3 worldPos)
{
    float4 viewPos = mul(_WorldToViewMat, float4(worldPos, 1));
    viewPos /= viewPos.w;

    float3 froxelPos = ViewPosToFroxelPos(viewPos.xyz);
    return froxelPos;
}

float3 FroxelPosToFroxelUvw(float3 froxelPos)
{
    return froxelPos / float3(_VolumeWidth, _VolumeHeight, _VolumeDepth);

}
float3 WorldPosToFroxelUvw(float3 worldPos)
{
    float3 froxelPos = WorldPosToFroxelPos(worldPos);
    return FroxelPosToFroxelUvw(froxelPos);
}

float DepthToFroxelPosZ(float depth)
{
    float froxelPosZ = _VolumeDepth * LogWithBase(_FroxelToWorldParams.z, (depth - _NearPlane) / _FroxelToWorldParams.w + 1);
    return froxelPosZ;
}

float3 WorldPosToPrevFroxelPos(float3 worldPos)
{
    float4 viewPos = mul(_PrevWorldToViewMat, float4(worldPos, 1));
    viewPos /= viewPos.w;

    float3 froxelPos = ViewPosToFroxelPos(viewPos.xyz);
    return froxelPos;
}

//
// ------------------------------------ Shadow ----------------------------------------------
//

/**
 * Gets the cascade weights based on the world position of the fragment and the poisitions of the split spheres for each cascade.
 * Returns a float4 with only one component set that corresponds to the appropriate cascade.
 */
inline float4 GetCascadeWeights_SplitSpheres(float3 wpos)
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
inline float4 GetShadowCoord(float4 wpos, float4 cascadeWeights)
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

float SampleDirShadow(float4 wpos)
{
    float4 cascadeWeights = GetCascadeWeights_SplitSpheres(wpos);
    float4 shadowCoord = GetShadowCoord(wpos, cascadeWeights);

    //1 tap hard shadow
    float shadow = _ShadowMapTexture.SampleCmpLevelZero(sampler_bilinear_clamp_compare, shadowCoord.xy, shadowCoord.z);
    shadow = lerp(_LightShadowData.r, 1.0, shadow);
    return shadow;
}

float SamplePointShadow(float3 vec)
{
    float3 absVec = abs(vec);
    float dominantAxis = max(max(absVec.x, absVec.y), absVec.z);
    dominantAxis = max(0.00001, dominantAxis - _LightProjectionParams.z); // shadow bias from point light is apllied here.
    dominantAxis *= _LightProjectionParams.w; // bias
    float mydist = -_LightProjectionParams.x + _LightProjectionParams.y / dominantAxis; // project to shadow map clip space [0; 1]

    #if defined(UNITY_REVERSED_Z)
        mydist = 1.0 - mydist; // depth buffers are reversed! Additionally we can move this to CPP code!
    #endif

    float shadow = _ShadowCubeMapTexture.SampleCmpLevelZero(sampler_bilinear_clamp_compare, vec.xyz, mydist);
    return lerp(_LightShadowData.r, 1.0, shadow);
}

float SampleSpotShadow(float3 vec)
{
    float4 shadowCoord = mul(unity_WorldToShadow[0], float4(vec, 1));
    float shadow = _ShadowMapTexture.SampleCmpLevelZero(sampler_bilinear_clamp_compare, shadowCoord.xy / shadowCoord.w, shadowCoord.z / shadowCoord.w);
    shadow = lerp(_LightShadowData.r, 1.0f, shadow);
    return shadow;
}

//
// ------------------------------------ Functions ----------------------------------------------
//

float4 ScatterStep(float3 accumuLight, float totalTransmittance, float3 inScatterLight, float sliceExtinction, float stepLength)
{
    float sliceTransmittance = exp(-sliceExtinction * stepLength);
    float3 sliceLightIntegral = inScatterLight * (1.0 - sliceTransmittance) / sliceExtinction;

    accumuLight += sliceLightIntegral * totalTransmittance;
    totalTransmittance *= sliceTransmittance;
    return float4(accumuLight, totalTransmittance);
}

float Square(float x)
{
    return x * x;
}

float PointLightFalloff(float distance)
{
    float atten = distance * distance * _LightPos.w;
    float falloff = _LightTextureB0.SampleLevel(sampler_bilinear_clamp, atten.rr, 0.0).r;
    falloff *= _LightAttenuationMultiplier;

    return falloff;
}

float SpotLightFalloff(float3 worldPos, float3 lightToPosDir, float distance)
{
    // Cookie
    //float4 uvCookie = mul(unity_WorldToLight, float4(worldPos, 1));
    //// negative bias because http://aras-p.info/blog/2010/01/07/screenspace-vs-mip-mapping/
    //float atten = _LightTexture0.SampleLevel(sampler_bilinear_clamp, uvCookie.xy / uvCookie.w, -8.0);
    //atten *= uvCookie.w < 0;

    float att = distance * distance * _LightPos.w;
    float distAtten = _LightTextureB0.SampleLevel(sampler_bilinear_clamp, att.rr, 0.0).r;

    float cosAngle = dot(lightToPosDir, _SpotLightDir);
    float coneAtten = 1.0 - smoothstep(1.0 / _SpotCosInnerConeRcp, _SpotCosOuterCone, cosAngle);

    return coneAtten * distAtten * _LightAttenuationMultiplier;
}

#endif //VOLUMETRIC_HELPER