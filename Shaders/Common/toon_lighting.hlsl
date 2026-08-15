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

/// <summary>Evaluates the OpenLit-derived direct-light luminance for the active Unity color-space branch.</summary>
float PureBaseToonLuminance(float3 rgb)
{
    #if defined(UNITY_COLORSPACE_GAMMA)
    return dot(rgb, float3(0.22, 0.707, 0.071));
    #else
    return dot(rgb, float3(0.0396819152, 0.458021790, 0.00609653955));
    #endif
}

/// <summary>Builds a finite fallback-inclusive OpenLit Toon band direction from post-light direct and first-order SH aggregates.</summary>
float3 PureBaseToonComputeLightDirection(float3 directAggregateDirection, float4 shAr, float4 shAg, float4 shAb)
{
    float3 shDirection = (shAr.xyz + shAg.xyz + shAb.xyz) / 3;
    float3 fallbackDirection = float3(0.001, 0.002, 0.001);
    float3 directionVector = directAggregateDirection + float3(shDirection.x, abs(shDirection.y), shDirection.z) + fallbackDirection;
    if (all(directionVector == 0) || !all(isfinite(directionVector)))
    {
        directionVector = fallbackDirection;
    }

    return normalize(directionVector);
}

/// <summary>Evaluates the OpenLit-derived L0/L2 SH base shared by the bright and dark Toon bands.</summary>
float3 PureBaseToonEvaluateShL0L2(float3 V, float4 shAr, float4 shAg, float4 shAb, float4 shBr, float4 shBg, float4 shBb, float4 shC)
{
    float4 quadratic = V.xyzz * V.yzzx;
    return float3(shAr.w, shAg.w, shAb.w)
        + float3(dot(shBr, quadratic), dot(shBg, quadratic), dot(shBb, quadratic))
        + shC.rgb * (V.x * V.x - V.y * V.y);
}

/// <summary>Evaluates a first-order SH term along the supplied direction.</summary>
float3 PureBaseToonEvaluateShL1(float3 direction, float4 shAr, float4 shAg, float4 shAb)
{
    return float3(dot(shAr.rgb, direction), dot(shAg.rgb, direction), dot(shAb.rgb, direction));
}

/// <summary>Evaluates the finite dark-band L1 term along the summed first-order SH direction.</summary>
float3 PureBaseToonEvaluateDarkShL1(float4 shAr, float4 shAg, float4 shAb)
{
    float3 shDirection = shAr.xyz + shAg.xyz + shAb.xyz;
    if (all(shDirection == 0) || !all(isfinite(shDirection)))
    {
        return float3(0, 0, 0);
    }

    return PureBaseToonEvaluateShL1(normalize(shDirection), shAr, shAg, shAb);
}

/// <summary>Evaluates the OpenLit-derived bright and dark SH bands before selecting the Toon surface-facing band.</summary>
float3 PureBaseToonEvaluateTwoBandSh(float3 surfaceNormal, float3 L, float4 shAr, float4 shAg, float4 shAb, float4 shBr, float4 shBg, float4 shBb, float4 shC)
{
    float3 base = PureBaseToonEvaluateShL0L2(L, shAr, shAg, shAb, shBr, shBg, shBb, shC);
    float3 bright = base + PureBaseToonEvaluateShL1(L, shAr, shAg, shAb);
    float3 dark = base + PureBaseToonEvaluateDarkShL1(shAr, shAg, shAb);
    #if defined(UNITY_COLORSPACE_GAMMA)
    bright = LinearToGammaSpace(bright);
    dark = LinearToGammaSpace(dark);
    #endif
    return lerp(dark, bright, step(0, dot(surfaceNormal, L)));
}

#endif
