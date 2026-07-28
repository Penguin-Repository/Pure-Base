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
    brdf.roughness = clamp(roughness, 0.002, 1.0);
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

/// <summary>Evaluates direct GGX diffuse and specular lighting with an optional binary diffuse response.</summary>
/// <param name="brdf">The material BRDF terms.</param>
/// <param name="normal">The normalized world-space surface normal.</param>
/// <param name="lightDirection">The normalized post-light-phase light direction.</param>
/// <param name="viewDirection">The normalized world-space view direction.</param>
/// <param name="lightColor">The post-light-phase radiance.</param>
/// <param name="binaryDiffuse">Selects the Hybrid diffuse step response.</param>
/// <returns>The finite direct BRDF contribution.</returns>
half3 PureBasePbrEvaluateDirect(PureBasePbrBrdfData brdf, half3 normal, half3 lightDirection, half3 viewDirection, half3 lightColor, bool binaryDiffuse)
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
    half roughnessFourth = brdf.roughnessSquared * brdf.roughnessSquared;
    half distributionDenominator = max(3.14159265 * pow(NdotH * NdotH * (roughnessFourth - 1.0) + 1.0, 2.0), 0.000001);
    half distribution = roughnessFourth / distributionDenominator;
    half visibilityV = NdotL * sqrt(max(NdotV * (NdotV - NdotV * roughnessFourth) + roughnessFourth, 0.000001));
    half visibilityL = NdotV * sqrt(max(NdotL * (NdotL - NdotL * roughnessFourth) + roughnessFourth, 0.000001));
    half visibility = 0.5 / max(visibilityV + visibilityL, 0.000001);
    half3 specular = distribution * visibility * PureBasePbrSchlickFresnel(brdf.specularColor, LdotH);
    return max(lightColor, half3(0, 0, 0)) * (brdf.diffuseColor * (diffuseNdotL * 0.318309886) + specular * NdotL);
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