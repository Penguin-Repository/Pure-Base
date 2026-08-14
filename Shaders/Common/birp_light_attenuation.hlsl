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

// Defines Unity BIRP's non-shadow light attenuation terms for the PureBase fragment host.

#ifndef PUREBASE_BIRP_LIGHT_ATTENUATION_INCLUDED
#define PUREBASE_BIRP_LIGHT_ATTENUATION_INCLUDED

/// <summary>Evaluates the active Unity BIRP light's distance and cookie attenuation without visibility.</summary>
/// <param name="input">The current BIRP fragment input containing light coordinates where Unity requires them.</param>
/// <param name="worldPos">The current world-space pixel position.</param>
/// <returns>The active light's non-shadow attenuation term.</returns>
inline fixed PureBaseEvaluateNonShadowLightAttenuation(v2f input, float3 worldPos)
{
    #if defined(DIRECTIONAL)
    return 1;
    #elif defined(POINT)
    unityShadowCoord3 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xyz;
    return tex2D(_LightTexture0, dot(lightCoord, lightCoord).rr).r;
    #elif defined(SPOT)
        #if !defined(UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS)
        unityShadowCoord4 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1));
        #else
        unityShadowCoord4 lightCoord = input._LightCoord;
        #endif
    return (lightCoord.z > 0) * UnitySpotCookie(lightCoord) * UnitySpotAttenuate(lightCoord.xyz);
    #elif defined(POINT_COOKIE)
        #if !defined(UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS)
        unityShadowCoord3 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xyz;
        #else
        unityShadowCoord3 lightCoord = input._LightCoord;
        #endif
    return tex2D(_LightTextureB0, dot(lightCoord, lightCoord).rr).r * texCUBE(_LightTexture0, lightCoord).w;
    #elif defined(DIRECTIONAL_COOKIE)
        #if !defined(UNITY_HALF_PRECISION_FRAGMENT_SHADER_REGISTERS)
        unityShadowCoord2 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xy;
        #else
        unityShadowCoord2 lightCoord = input._LightCoord;
        #endif
    return tex2D(_LightTexture0, lightCoord).w;
    #else
    return 1;
    #endif
}

#endif