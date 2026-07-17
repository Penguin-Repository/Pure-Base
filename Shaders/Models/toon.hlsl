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

/// <summary>Samples the Toon normal map before the Shader-Core base phase can alter the tangent-space normal.</summary>
void SCModelInitializeTangentNormal(inout SCShadingData shadingData)
{
    shadingData.N = SCUnpackNormal(SCSample(_NormalMap, sampler_NormalMap, shadingData.uv), _NormalScale);
    shadingData.N_detail = shadingData.N;
}

/// <summary>Returns the quantized per-light Toon response after the Shader-Core light phase.</summary>
half SCModelEvaluateDirectFactor(SCShadingData shadingData, SCLightData light)
{
    return step(0, dot(shadingData.N, light.direction));
}

/// <summary>Evaluates the supplied Unity spherical-harmonics coefficients with the model world normal.</summary>
half3 SCModelEvaluateAmbient(SCShadingData shadingData, half4 shAr, half4 shAg, half4 shAb, half4 shBr, half4 shBg, half4 shBb, half4 shC)
{
    half4 normal = half4(shadingData.N, 1);
    half3 ambient = half3(dot(shAr, normal), dot(shAg, normal), dot(shAb, normal));
    half4 quadratic = normal.xyzz * normal.yzzx;
    ambient += half3(dot(shBr, quadratic), dot(shBg, quadratic), dot(shBb, quadratic));
    ambient += shC.rgb * (normal.x * normal.x - normal.y * normal.y);
    return max(ambient, half3(0, 0, 0));
}

/// <summary>Suppresses Shader-Core's continuous vertex-light aggregate for the Toon model.</summary>
half3 SCModelSelectVertexLighting(half3 vertexLighting)
{
    return half3(0, 0, 0);
}

/// <summary>Produces the Toon ForwardBase result from aggregate direct, ambient, and baked lighting.</summary>
half4 SCModelBaseSurfaceColor(SCShadingData shadingData)
{
    return half4(shadingData.albedoAlpha.rgb * shadingData.lightColor, 1);
}

/// <summary>Produces the Toon ForwardAdd result from the current quantized direct-light contribution.</summary>
half4 SCModelAddSurfaceColor(SCShadingData shadingData)
{
    return half4(shadingData.albedoAlpha.rgb * shadingData.lightColor, 1);
}

#endif