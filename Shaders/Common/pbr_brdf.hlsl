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

// Defines the shared finite metallic GGX BRDF used by the PureBase PBR and Hybrid BIRP models.

#ifndef PUREBASE_PBR_BRDF_INCLUDED
#define PUREBASE_PBR_BRDF_INCLUDED

/// <summary>Stores the metallic material terms shared by direct and indirect BRDF evaluation.</summary>
struct PureBasePbrBrdfData
{
    /// <summary>Stores the energy-conserving diffuse reflectance.</summary>
    half3 diffuseColor;
    /// <summary>Stores the metallic Schlick F0 reflectance.</summary>
    half3 specularColor;
    /// <summary>Stores the clamped perceptual roughness.</summary>
    half roughness;
    /// <summary>Stores squared GGX roughness for the visibility and distribution terms.</summary>
    half roughnessSquared;
};

/// <summary>Defines the shared rounded-up perceptual-roughness floor as a compile-time half value.</summary>
static const half PureBasePbrPerceptualRoughnessFloor = 0.0890;

/// <summary>Clamps perceptual roughness to 0.089; the rounded-up floor protects p^4 above the IEEE binary16 minimum normal and aligns with Unity URP HALF_MIN_SQRT/HALF_MIN initialization.</summary>
half PureBasePbrClampPerceptualRoughness(half perceptualRoughness)
{
    return clamp(perceptualRoughness, PureBasePbrPerceptualRoughnessFloor, 1.0);
}

/// <summary>Returns a finite unit direction and maps zero-length directions to zero.</summary>
/// <param name="direction">The direction to normalize.</param>
/// <returns>A finite unit direction or zero.</returns>
half3 PureBasePbrSafeNormalize(half3 direction)
{
    return direction * rsqrt(max(dot(direction, direction), 0.000001));
}

/// <summary>Builds the energy-conserving metallic BRDF terms from material albedo, metallic, and roughness.</summary>
/// <param name="albedo">The linear surface albedo.</param>
/// <param name="metallic">The material metallic weight.</param>
/// <param name="roughness">The perceptual material roughness.</param>
/// <returns>The finite GGX BRDF terms.</returns>
PureBasePbrBrdfData PureBasePbrCreateBrdf(half3 albedo, half metallic, half roughness)
{
    PureBasePbrBrdfData brdf;
    half clampedMetallic = saturate(metallic);
    brdf.roughness = PureBasePbrClampPerceptualRoughness(roughness);
    brdf.roughnessSquared = brdf.roughness * brdf.roughness;
    brdf.diffuseColor = saturate(albedo) * (1.0 - clampedMetallic);
    brdf.specularColor = lerp(half3(0.04, 0.04, 0.04), saturate(albedo), clampedMetallic);
    return brdf;
}

/// <summary>Aggregates the PureBase material diffuse and specular decomposition with Unity Standard's Meta lightmapping roughness rule.</summary>
/// <param name="brdf">The PureBase material BRDF terms; this function does not recreate Unity Standard metallic terms.</param>
/// <returns>The Unity Standard Meta lightmapping albedo using actual squared roughness.</returns>
half3 PureBasePbrEvaluateLightmappingAlbedo(PureBasePbrBrdfData brdf)
{
    return brdf.diffuseColor + brdf.specularColor * brdf.roughnessSquared * 0.5;
}

/// <summary>Evaluates Schlick Fresnel for a shared metallic GGX BRDF.</summary>
/// <param name="specularColor">The material F0 reflectance.</param>
/// <param name="cosine">The clamped incident half-angle cosine.</param>
/// <returns>The angle-dependent specular reflectance.</returns>
half3 PureBasePbrSchlickFresnel(half3 specularColor, half cosine)
{
    half fresnelWeight = pow(1.0 - saturate(cosine), 5.0);
    return specularColor + (1.0 - specularColor) * fresnelWeight;
}

/// <summary>Selects the direct diffuse normalization used by the material toggle.</summary>
/// <param name="useUnityStandardDiffuseBrightness">The unsigned material toggle; every nonzero value enables Unity Standard brightness.</param>
/// <returns>One for enabled brightness or the existing inverse-pi normalization when disabled.</returns>
half PureBasePbrSelectDiffuseNormalization(uint useUnityStandardDiffuseBrightness)
{
    return useUnityStandardDiffuseBrightness != 0 ? 1.0 : 0.318309886;
}

/// <summary>Evaluates the linearized joint GGX visibility approximation independently inspected in Unity 2022.3.22f1.</summary>
/// <param name="NdotL">The nonnegative surface-to-light cosine.</param>
/// <param name="NdotV">The nonnegative surface-to-view cosine.</param>
/// <param name="roughness">Academic roughness a = p^2.</param>
/// <returns>The finite linearized joint GGX visibility factor.</returns>
/// <remarks>Exact Smith theory provenance belongs in documentation; this does not imply copied Unity code.</remarks>
float PureBasePbrEvaluateSmithJointGgxVisibility(float NdotL, float NdotV, float roughness)
{
    float lambdaV = NdotL * (NdotV * (1.0f - roughness) + roughness);
    float lambdaL = NdotV * (NdotL * (1.0f - roughness) + roughness);
#if defined(SHADER_API_SWITCH)
    float epsilon = UNITY_HALF_MIN;
#else
    float epsilon = 1e-5f;
#endif
    return 0.5f / (lambdaV + lambdaL + epsilon);
}

/// <summary>Evaluates direct GGX diffuse and specular lighting with independent diffuse normalization and binary response controls.</summary>
/// <param name="brdf">The material BRDF terms.</param>
/// <param name="normal">The normalized world-space surface normal.</param>
/// <param name="lightDirection">The normalized post-light-phase light direction.</param>
/// <param name="viewDirection">The normalized world-space view direction.</param>
/// <param name="lightColor">The post-light-phase radiance.</param>
/// <param name="diffuseNormalization">The direct diffuse normalization coefficient.</param>
/// <param name="binaryDiffuse">Selects the Hybrid diffuse step response.</param>
/// <returns>The finite direct BRDF contribution.</returns>
half3 PureBasePbrEvaluateDirect(PureBasePbrBrdfData brdf, half3 normal, half3 lightDirection, half3 viewDirection, half3 lightColor, half diffuseNormalization, bool binaryDiffuse)
{
    half3 N = PureBasePbrSafeNormalize(normal);
    half3 L = PureBasePbrSafeNormalize(lightDirection);
    half3 V = PureBasePbrSafeNormalize(viewDirection);
    half3 H = PureBasePbrSafeNormalize(L + V);
    half signedNdotL = dot(N, L);
    half NdotL = max(0.0, signedNdotL);
    half NdotV = max(0.0, dot(N, V));
    half NdotH = max(0.0, dot(N, H));
    half LdotH = max(0.0, dot(L, H));
    half diffuseNdotL = binaryDiffuse ? step(0.0, signedNdotL) : NdotL;
    float roughnessFourth = brdf.roughnessSquared * brdf.roughnessSquared;
    float distributionDenominator = max(3.14159265f * pow((float)NdotH * (float)NdotH * (roughnessFourth - 1.0f) + 1.0f, 2.0f), 0.000001f);
    float distribution = roughnessFourth / distributionDenominator;
    float visibility = PureBasePbrEvaluateSmithJointGgxVisibility(NdotL, NdotV, brdf.roughnessSquared);
    float3 specular = distribution * visibility * NdotL * PureBasePbrSchlickFresnel(brdf.specularColor, LdotH);
    return max(lightColor, half3(0, 0, 0)) * (brdf.diffuseColor * diffuseNdotL * diffuseNormalization + specular);
}

/// <summary>Evaluates the Unity Standard helper's decoded diffuse and reflection-probe environment terms.</summary>
/// <param name="brdf">The material BRDF terms.</param>
/// <param name="normal">The normalized world-space surface normal.</param>
/// <param name="viewDirection">The normalized world-space view direction.</param>
/// <param name="indirectDiffuse">Unity Standard's indirect diffuse irradiance.</param>
/// <param name="indirectSpecular">Unity Standard's decoded reflection-probe radiance.</param>
/// <returns>The finite indirect BRDF contribution.</returns>
half3 PureBasePbrEvaluateIndirect(PureBasePbrBrdfData brdf, half3 normal, half3 viewDirection, half3 indirectDiffuse, half3 indirectSpecular)
{
    half NdotV = max(0.0, dot(PureBasePbrSafeNormalize(normal), PureBasePbrSafeNormalize(viewDirection)));
    half3 fresnel = PureBasePbrSchlickFresnel(brdf.specularColor, NdotV);
    return max(indirectDiffuse, half3(0, 0, 0)) * brdf.diffuseColor + max(indirectSpecular, half3(0, 0, 0)) * fresnel;
}

#endif