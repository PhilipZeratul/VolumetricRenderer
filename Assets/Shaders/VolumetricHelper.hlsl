#ifndef VOLUMETRIC_HELPER
#define VOLUMETRIC_HELPER

#define PI 3.1415926535

// Parameters
RWTexture3D<float> _ShadowVolume; // R: Visibility
RWTexture3D<float4> _MaterialVolume_A; // RGB: Scattering Coef, A: Absorption
RWTexture3D<float4> _MaterialVolume_B; // R: Phase G
RWTexture3D<float4> _ScatterVolume; // RGB: Scattered Light, A: 
RWTexture2D<float4> _AccumulationTex; // RGB: Accumulated Light, A: Transmittance

Texture2D<float4> _ShadowMapTexture;

float3 _ScatteringCoef;
float _AbsorptionCoef;
float _PhaseG;

float3 _LightColor;
float3 _LightDir;
float3 _ViewDir;

int _VolumeWidth, _VolumeHeight, _VolumeDepth;
float _NearPlane, _VolumeDistance;

float4x4 _invFroxelVPMat;

// Functions
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

// (0, height, 0) - (width, 0, depth) -> (-1, -1, 0, 1) - (1, 1, 1, 1)
float4 FroxelPos2ClipPos(uint3 froxelPos)
{
    float4 clipPos = 0;
    clipPos.x = Remap(froxelPos.x, 0.0, _VolumeWidth, -1.0, 1.0);
    clipPos.y = Remap(froxelPos.y, _VolumeHeight, 0.0, -1.0, 1.0);
    clipPos.z = Remap(froxelPos.z, 0.0, _VolumeDepth, 0.0, 1.0);
    clipPos.w = 1;
    return clipPos;
}

float4 FroxelPos2WorldPos(uint3 froxelPos)
{
    float4 clipPos = FroxelPos2ClipPos(froxelPos);
    float z = Remap(clipPos.z, 0.0, 1.0, _NearPlane, _VolumeDistance);
    clipPos *= z;
    float4 worldPos = mul(_invFroxelVPMat, clipPos);
    return worldPos;
}

float Rgb2Gray(float3 c)
{
    float gray = c.r * 0.3 + c.g * 0.59 + c.b * 0.11;
    return gray;
}

#endif //VOLUMETRIC_HELPER