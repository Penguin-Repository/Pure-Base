/*
 * Copyright 2026 Penguin
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

// Defines the minimal quantized-lighting model for the PureBase Toon BIRP host.

#ifndef PUREBASE_TOON_MODEL_INCLUDED
#define PUREBASE_TOON_MODEL_INCLUDED

#include "Packages/jp.penguin.purebase/Shaders/Common/toon_lighting.hlsl"

/// <summary>Stores deterministic host-owned data for Shader-Core lighting callbacks.</summary>
struct SCCustomData
{
    /// <summary>Reserves nonempty storage for the Shader-Core callback contract.</summary>
    half reserved;
    /// <summary>Stores the Unity wrapper's unattenuated main-light color.</summary>
    half3 mainLightColor;
    /// <summary>Stores the Unity wrapper's full main-light attenuation.</summary>
    half mainLightAttenuation;
    /// <summary>Stores the Unity wrapper's distance and cookie main-light attenuation without visibility.</summary>
    half mainLightNonShadowAttenuation;
    /// <summary>Stores Unity's effective visibility for the current light, including realtime shadow, mixed or baked occlusion, and fade.</summary>
    half mainLightShadowVisibility;
    /// <summary>Stores the normalized main-light direction before Shader-Core light-phase modifications.</summary>
    half3 mainLightDirection;
};

/// <summary>Samples the Toon normal map before the Shader-Core base phase can alter the tangent-space normal.</summary>
void SCModelInitializeTangentNormal(inout SCShadingData shadingData)
{
    shadingData.N = SCUnpackNormal(SCSample(_NormalMap, sampler_NormalMap, shadingData.uv), _NormalScale);
    shadingData.N_detail = shadingData.N;
}

/// <summary>Returns the quantized per-light Toon response with Unity effective visibility applied once after the Shader-Core light phase.</summary>
half SCModelEvaluateDirectFactor(SCShadingData shadingData, SCLightData light)
{
    return PureBaseToonEvaluateDirectFactor(shadingData.N, light.direction) * shadingData.shadow;
}

/// <summary>Prepares the Toon main light so direct radiance remains independent from directional visibility.</summary>
void SCModelPrepareMainLight(inout SCLightData light, inout SCShadingData sd, half3 mainLightColor, half mainLightAttenuation, half mainLightNonShadowAttenuation, half mainLightShadowVisibility)
{
    light.color = mainLightColor * mainLightNonShadowAttenuation;
    sd.shadow = mainLightShadowVisibility;
}

/// <summary>Preserves the Shader-Core light direction for the Toon quantization response.</summary>
half3 SCModelSelectMainLightDirection(SCVertexData vertex, half3 lightDirection)
{
    return lightDirection;
}

/// <summary>Evaluates the supplied Unity spherical-harmonics coefficients as OpenLit-derived bright and dark Toon bands.</summary>
half3 SCModelEvaluateAmbient(SCShadingData shadingData, half4 shAr, half4 shAg, half4 shAb, half4 shBr, half4 shBg, half4 shBb, half4 shC)
{
    return PureBaseToonEvaluateTwoBandSh(shadingData.N, shadingData.L, shAr, shAg, shAb, shBr, shBg, shBb, shC);
}

/// <summary>Suppresses Shader-Core's continuous vertex-light aggregate for the Toon model.</summary>
half3 SCModelSelectVertexLighting(half3 vertexLighting)
{
    return half3(0, 0, 0);
}

/// <summary>Preserves the existing Toon ambient and baked-light environment aggregate.</summary>
half3 SCModelSelectEnvironmentLighting(half3 environment)
{
    return environment;
}

/// <summary>Produces the Toon ForwardBase result from aggregate direct, ambient, and baked lighting.</summary>
half4 SCModelBaseSurfaceColor(SCShadingData shadingData, SCCustomData customData, SCVertexData vertex)
{
    return half4(shadingData.albedoAlpha.rgb * shadingData.lightColor, 1);
}

/// <summary>Produces the Toon ForwardAdd result from the current quantized direct-light contribution.</summary>
half4 SCModelAddSurfaceColor(SCShadingData shadingData, SCCustomData customData, SCVertexData vertex)
{
    return half4(shadingData.albedoAlpha.rgb * shadingData.lightColor, 1);
}

#define SCModelSelectAggregateLightDirection(directAggregateDirection, shAr, shAg, shAb) PureBaseToonComputeLightDirection(directAggregateDirection, shAr, shAg, shAb)
#define SCModelEvaluateLightDirectionWeight(lightColor) PureBaseToonLuminance(lightColor)

#endif