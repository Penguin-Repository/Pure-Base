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

// Defines the common BIRP Shader-Core callbacks used by persistent phase test hosts.

#ifndef PUREBASE_TEST_PHASE_HOST_COMMON_INCLUDED
#define PUREBASE_TEST_PHASE_HOST_COMMON_INCLUDED

/// <summary>Stores deterministic host-owned callback state.</summary>
struct SCCustomData
{
    /// <summary>Reserves the callback data contract without introducing phase-specific state.</summary>
    half reserved;
};

/// <summary>Accumulates one BIRP light after the Shader-Core light insertion point.</summary>
void SCCalculateLight(inout SCLightData lightSum, inout SCShadingData sd, inout SCCustomData cd, SCVertexData vertex, SCLightData light)
{
    __SC_PHASE_light__

    lightSum.direction += light.direction * dot(light.color, half3(0.333333, 0.333333, 0.333333));
    lightSum.color += light.color;
}

/// <summary>Publishes a stable aggregate light direction for subsequent test phases.</summary>
void SCCalculateEnvironmentLight(inout SCLightData lightSum, inout half3 environment, inout SCShadingData sd, inout SCCustomData cd, SCVertexData vertex, half4 shAr, half4 shAg, half4 shAb, half4 shBr, half4 shBg, half4 shBb, half4 shC)
{
    sd.L = dot(lightSum.direction, lightSum.direction) == 0 ? 0 : normalize(lightSum.direction);
}

#include "Packages/jp.lilxyzw.shadercore/ShaderLibrary/birp_lighting.hlsl"

/// <summary>Evaluates the BIRP phase-host color through every standard pixel insertion point.</summary>
half4 frag(v2f input, bool isFront : SV_IsFrontFace) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SCPositionAndDirection camera = SCGetCameraData();
    SCPositionAndDirection head = SCGetHeadData();
    SCPositionAndDirection headBone = SCGetHeadBoneData();
    SCVertexData vertex = FromPixelInput(input, camera, head, headBone, SCTangentScale(), isFront);
    SCCustomData cd = (SCCustomData)0;
    SCShadingData sd;
    sd.albedoAlpha = SCGetPhaseHostBaseColor();
    clip(sd.albedoAlpha.a - _PhaseHostCutoff);
    sd.col = sd.albedoAlpha;
    sd.mask = 1;
    sd.uv = vertex.uv[0].xy;
    sd.T = vertex.T;
    sd.B = vertex.B;
    sd.N = vertex.N;
    sd.N_detail = vertex.N;
    sd.L = 0;
    sd.lightColor = 0;
    sd.shadow = 1;
    sd.roughness = 1;
    sd.add = 0;
    sd.postadd = 0;
    sd.normalMapWithRoughness = false;
    sd.maskTexture = _PhaseHostMask;
    sd.gradientsTexture = _PhaseHostGradients;

    SCLightData lightSum = (SCLightData)0;
    half3 environment = 0;
    SCCalculateAllLights(lightSum, environment, sd, cd, vertex, input);
    sd.lightColor = lightSum.color + environment;

    __SC_PHASE_modifylight__

    sd.col.rgb = sd.albedoAlpha.rgb + sd.lightColor;

    __SC_PHASE_shade__

    __SC_PHASE_reflection__

    __SC_PHASE_add__

    sd.col.rgb += sd.add + sd.postadd;
    sd.col.a = 1;

    __SC_PHASE_postpixel__

    return sd.col;
}

#endif