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

// Provides the minimal Toon direct factor, dominant direction, and two-band SH lighting for the BIRP host.
// Derived from lilToon 2.3.4 OpenLit 1.0.2 BIRP concepts: ComputeLightDirection, ShadeSH9ToonDouble, and ComputeLights; see the reference package NOTICE.

#ifndef PUREBASE_TOON_LIGHTING_INCLUDED
#define PUREBASE_TOON_LIGHTING_INCLUDED

/// <summary>Evaluates the binary Toon direct-light response after the Shader-Core light phase.</summary>
half PureBaseToonEvaluateDirectFactor(float3 surfaceNormal, float3 lightDirection)
{
    return step(0, dot(surfaceNormal, lightDirection));
}

/// <summary>Builds a finite Toon band direction from direct-light and spherical-harmonics aggregates.</summary>
float3 PureBaseToonComputeLightDirection(float3 directAggregateDirection, float4 shAr, float4 shAg, float4 shAb)
{
    float3 shDirection = (shAr.xyz + shAg.xyz + shAb.xyz) / 3;
    float3 directionVector = directAggregateDirection + float3(shDirection.x, abs(shDirection.y), shDirection.z);
    if (dot(directionVector, directionVector) <= 0.000001)
    {
        directionVector = float3(0.001, 0.002, 0.001);
    }

    return normalize(directionVector);
}

/// <summary>Evaluates the fixed bright and dark spherical-harmonics bands for a Toon surface.</summary>
float3 PureBaseToonEvaluateTwoBandSh(float3 surfaceNormal, float3 L, float4 shAr, float4 shAg, float4 shAb, float4 shBr, float4 shBg, float4 shBb, float4 shC)
{
    float3 E = L * 0.666666;
    float4 quadratic = E.xyzz * E.yzzx;
    float3 base = float3(shAr.w, shAg.w, shAb.w) + float3(dot(shBr, quadratic), dot(shBg, quadratic), dot(shBb, quadratic)) + shC.rgb * (E.x * E.x - E.y * E.y);
    float3 linearTerm = float3(dot(shAr.xyz, E), dot(shAg.xyz, E), dot(shAb.xyz, E));
    float3 bright = max(base + linearTerm, 0);
    float3 dark = max(base - linearTerm, 0);
    return lerp(dark, bright, step(0, dot(surfaceNormal, L)));
}

#endif
