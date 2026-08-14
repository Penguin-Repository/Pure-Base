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

// Defines the minimal Unity Standard GI-backed metallic model shared by the PureBase PBR and Hybrid BIRP hosts.

#ifndef PUREBASE_METALLIC_MODEL_INCLUDED
#define PUREBASE_METALLIC_MODEL_INCLUDED

#include "../Common/pbr_brdf.hlsl"

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

/// <summary>Samples the metallic model normal map before the Shader-Core base phase can alter the tangent-space normal.</summary>
void SCModelInitializeTangentNormal(inout SCShadingData shadingData)
{
    shadingData.N = SCUnpackNormal(SCSample(_NormalMap, sampler_NormalMap, shadingData.uv), _NormalScale);
    shadingData.N_detail = shadingData.N;
}

/// <summary>Preserves the post-light-phase radiance for the model's shared direct BRDF evaluation.</summary>
half SCModelEvaluateDirectFactor(SCShadingData shadingData, SCLightData light)
{
    return 1;
}

/// <summary>Prepares the Unity Standard main light while keeping Shader-Core visibility neutral.</summary>
void SCModelPrepareMainLight(inout SCLightData light, inout SCShadingData sd, half3 mainLightColor, half mainLightAttenuation, half mainLightNonShadowAttenuation, half mainLightShadowVisibility)
{
    light.color = mainLightColor * mainLightAttenuation;
}

/// <summary>Selects a normalized per-pixel Unity main-light direction before Shader-Core's light phase.</summary>
half3 SCModelSelectMainLightDirection(SCVertexData vertex, half3 lightDirection)
{
    return PureBasePbrSafeNormalize(UnityWorldSpaceLightDir(vertex.position));
}

/// <summary>Preserves the existing normalized direct-light aggregate for PBR and Hybrid BRDF evaluation.</summary>
half3 PureBasePbrSelectAggregateLightDirection(half3 directAggregateDirection, half4 shAr, half4 shAg, half4 shAb)
{
    return dot(directAggregateDirection, directAggregateDirection) == 0 ? half3(0, 0, 0) : normalize(directAggregateDirection);
}

/// <summary>Disables Shader-Core ambient SH because Unity Standard GI owns ambient and lightmap evaluation.</summary>
half3 SCModelEvaluateAmbient(SCShadingData shadingData, half4 shAr, half4 shAg, half4 shAb, half4 shBr, half4 shBg, half4 shBb, half4 shC)
{
    return half3(0, 0, 0);
}

/// <summary>Suppresses directionless Shader-Core vertex-light aggregation for per-pixel direct BRDF evaluation.</summary>
half3 SCModelSelectVertexLighting(half3 vertexLighting)
{
    return half3(0, 0, 0);
}

/// <summary>Discards Shader-Core baked and ambient aggregates because Unity Standard GI evaluates them once.</summary>
half3 SCModelSelectEnvironmentLighting(half3 environment)
{
    return half3(0, 0, 0);
}

/// <summary>Copies Unity's ForwardBase reflection-probe globals into the Unity Standard GI input contract.</summary>
/// <param name="input">The GI input populated with active reflection-probe state.</param>
void SCModelPopulateReflectionProbeInput(inout UnityGIInput input)
{
    input.probeHDR[0] = unity_SpecCube0_HDR;
    input.probeHDR[1] = unity_SpecCube1_HDR;

    #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
    input.boxMin[0] = unity_SpecCube0_BoxMin;
    input.boxMin[1] = unity_SpecCube1_BoxMin;
    #endif

    #if defined(UNITY_SPECCUBE_BOX_PROJECTION)
    input.boxMax[0] = unity_SpecCube0_BoxMax;
    input.boxMax[1] = unity_SpecCube1_BoxMax;
    input.probePosition[0] = unity_SpecCube0_ProbePosition;
    input.probePosition[1] = unity_SpecCube1_ProbePosition;
    #endif
}

/// <summary>Builds Unity Standard GI input from the current Shader-Core pixel and isolated main-light context.</summary>
/// <param name="input">The fully initialized Unity Standard GI input.</param>
/// <param name="customData">The raw main-light data captured by the BIRP host.</param>
/// <param name="vertex">The current Shader-Core world-space pixel data.</param>
void SCModelInitializeGiInput(out UnityGIInput input, SCCustomData customData, SCVertexData vertex)
{
    input = (UnityGIInput)0;
    input.light.color = customData.mainLightColor;
    input.light.dir = customData.mainLightDirection;
    input.light.ndotl = 0;
    input.worldPos = vertex.position;
    input.worldViewDir = PureBasePbrSafeNormalize(vertex.V);
    input.atten = customData.mainLightAttenuation;
    input.ambient = 0;
    input.lightmapUV = float4(
    vertex.uv[1].xy * unity_LightmapST.xy + unity_LightmapST.zw,
    vertex.uv[2].xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw);
    SCModelPopulateReflectionProbeInput(input);
}

/// <summary>Creates the Unity Standard surface descriptor needed only for GI and reflection-probe evaluation.</summary>
/// <param name="shadingData">The Shader-Core surface data after all base-phase modifications.</param>
/// <returns>The initialized Standard surface descriptor.</returns>
SurfaceOutputStandard SCModelCreateStandardSurface(SCShadingData shadingData)
{
    SurfaceOutputStandard surface = (SurfaceOutputStandard)0;
    surface.Albedo = saturate(shadingData.albedoAlpha.rgb);
    surface.Normal = PureBasePbrSafeNormalize(shadingData.N);
    surface.Emission = 0;
    surface.Metallic = saturate(_Metallic);
    surface.Smoothness = 1.0 - clamp(_Roughness, 0.002, 1.0);
    surface.Occlusion = 1;
    surface.Alpha = 1;
    return surface;
}

/// <summary>Evaluates Unity Standard GI and reflection probes once for the current ForwardBase surface.</summary>
/// <param name="shadingData">The Shader-Core surface data after phase modifications.</param>
/// <param name="customData">The isolated raw main-light data.</param>
/// <param name="vertex">The current Shader-Core world-space pixel data.</param>
/// <returns>The decoded Unity Standard indirect diffuse and probe contribution.</returns>
half3 SCModelEvaluateIndirectLighting(SCShadingData shadingData, SCCustomData customData, SCVertexData vertex)
{
    SurfaceOutputStandard surface = SCModelCreateStandardSurface(shadingData);
    UnityGIInput giInput;
    SCModelInitializeGiInput(giInput, customData, vertex);
    UnityGI gi;
    LightingStandard_GI(surface, giInput, gi);
    PureBasePbrBrdfData brdf = PureBasePbrCreateBrdf(shadingData.albedoAlpha.rgb, _Metallic, _Roughness);
    return PureBasePbrEvaluateIndirect(brdf, shadingData.N, vertex.V, gi.indirect.diffuse, gi.indirect.specular);
}

/// <summary>Returns whether this model uses the fixed Hybrid diffuse light response.</summary>
/// <returns>True only for the Hybrid wrapper.</returns>
bool SCModelUsesHybridDiffuse()
{
    #if defined(PUREBASE_HYBRID_DIFFUSE)
    return true;
    #else
    return false;
    #endif
}

/// <summary>Evaluates the shared direct BRDF from Shader-Core's post-light-phase aggregate.</summary>
/// <param name="shadingData">The Shader-Core surface and post-light-phase aggregate data.</param>
/// <param name="vertex">The current Shader-Core world-space pixel data.</param>
/// <returns>The direct PBR or Hybrid contribution.</returns>
half3 SCModelEvaluateDirectLighting(SCShadingData shadingData, SCVertexData vertex)
{
    PureBasePbrBrdfData brdf = PureBasePbrCreateBrdf(shadingData.albedoAlpha.rgb, _Metallic, _Roughness);
    return PureBasePbrEvaluateDirect(brdf, shadingData.N, shadingData.L, vertex.V, shadingData.lightColor, SCModelUsesHybridDiffuse());
}

/// <summary>Produces the PBR or Hybrid ForwardBase result from post-light direct BRDF and Unity Standard indirect lighting.</summary>
half4 SCModelBaseSurfaceColor(SCShadingData shadingData, SCCustomData customData, SCVertexData vertex)
{
    return half4(SCModelEvaluateDirectLighting(shadingData, vertex) + SCModelEvaluateIndirectLighting(shadingData, customData, vertex), 1);
}

/// <summary>Produces the PBR or Hybrid ForwardAdd result from post-light direct BRDF only.</summary>
half4 SCModelAddSurfaceColor(SCShadingData shadingData, SCCustomData customData, SCVertexData vertex)
{
    return half4(SCModelEvaluateDirectLighting(shadingData, vertex), 1);
}

#define SCModelSelectAggregateLightDirection(directAggregateDirection, shAr, shAg, shAb) PureBasePbrSelectAggregateLightDirection(directAggregateDirection, shAr, shAg, shAb)

#endif