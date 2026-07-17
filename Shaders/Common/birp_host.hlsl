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

// Defines the minimal BIRP fragment host that exposes Shader-Core standard pixel phases.

#ifndef PUREBASE_BIRP_HOST_INCLUDED
#define PUREBASE_BIRP_HOST_INCLUDED

/// <summary>Accumulates a BIRP light after the Shader-Core per-light phase.</summary>
void SCCalculateLight(inout SCLightData lightSum, inout SCShadingData shadingData, inout SCCustomData customData, SCVertexData vertex, SCLightData light)
{
    light.direction = SCModelSelectMainLightDirection(vertex, light.direction);
    customData.mainLightDirection = light.direction;
    if (SCModelUsesIsolatedMainLightColor())
        light.color = customData.mainLightColor * customData.mainLightAttenuation;

    __SC_PHASE_light__

    lightSum.direction += light.direction * dot(light.color, half3(0.333333, 0.333333, 0.333333));
    lightSum.color += light.color * SCModelEvaluateDirectFactor(shadingData, light);
}

/// <summary>Publishes the aggregate light direction and applies the selected model's ambient SH response.</summary>
void SCCalculateEnvironmentLight(inout SCLightData lightSum, inout half3 environment, inout SCShadingData shadingData, inout SCCustomData customData, SCVertexData vertex, half4 shAr, half4 shAg, half4 shAb, half4 shBr, half4 shBg, half4 shBb, half4 shC)
{
    shadingData.L = dot(lightSum.direction, lightSum.direction) == 0 ? half3(0, 0, 0) : normalize(lightSum.direction);
    environment += SCModelEvaluateAmbient(shadingData, shAr, shAg, shAb, shBr, shBg, shBb, shC);
}

#include "Packages/jp.lilxyzw.shadercore/ShaderLibrary/birp_lighting.hlsl"

/// <summary>Evaluates the selected model's ForwardBase or ForwardAdd result with all standard pixel phase insertion points.</summary>
half4 frag(v2f input, bool isFront : SV_IsFrontFace) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SCPositionAndDirection camera = SCGetCameraData();
    SCPositionAndDirection head = SCGetHeadData();
    SCPositionAndDirection headBone = SCGetHeadBoneData();
    SCVertexData vertex = FromPixelInput(input, camera, head, headBone, SCTangentScale(), isFront);
    SCCustomData customData = (SCCustomData)0;
    SCShadingData shadingData;
    half coverage;
    SCInitializeSurface(shadingData, coverage, vertex);
    SCClipCutoutCoverage(coverage);
    SCBuildWorldTangentBasis(shadingData, vertex);

    SCLightData lightSum = (SCLightData)0;
    half3 environment = half3(0, 0, 0);
    UNITY_LIGHT_ATTENUATION(mainLightAttenuation, input, vertex.position);
    customData.mainLightColor = _LightColor0.rgb;
    customData.mainLightAttenuation = saturate(mainLightAttenuation);
    customData.mainLightDirection = half3(0, 0, 0);
    SCCalculateAllLights(lightSum, environment, shadingData, customData, vertex, input, SCModelSelectVertexLighting(SCVertexLighting(vertex.position)));
    environment = SCModelSelectEnvironmentLighting(environment);

    #if defined(UNITY_PASS_FORWARDADD)
        shadingData.lightColor = lightSum.color;
    #else
        shadingData.lightColor = lightSum.color + environment;
    #endif

    __SC_PHASE_modifylight__

    #if defined(UNITY_PASS_FORWARDADD)
        shadingData.col = SCModelAddSurfaceColor(shadingData, customData, vertex);
    #else
        shadingData.col = SCModelBaseSurfaceColor(shadingData, customData, vertex);
    #endif

    __SC_PHASE_shade__

    __SC_PHASE_reflection__

    __SC_PHASE_add__

    shadingData.col.rgb += shadingData.add + shadingData.postadd;

    __SC_PHASE_postpixel__

    shadingData.col.a = 1;
    #if defined(UNITY_PASS_FORWARDADD)
        UNITY_APPLY_FOG_COLOR(input.fogCoord, shadingData.col, fixed4(0, 0, 0, 0));
    #else
        UNITY_APPLY_FOG(input.fogCoord, shadingData.col);
    #endif
    return shadingData.col;
}

#endif