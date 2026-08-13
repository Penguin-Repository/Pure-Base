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

#include "Packages/jp.penguin.purebase/Shaders/Common/rendering_mode.hlsl"

#ifndef SCModelSelectAggregateLightDirection
    #define SCModelSelectAggregateLightDirection(directAggregateDirection, shAr, shAg, shAb) (dot(directAggregateDirection, directAggregateDirection) == 0 ? half3(0, 0, 0) : normalize(directAggregateDirection))
#endif

/// <summary>Accumulates a BIRP light after the Shader-Core per-light phase.</summary>
void SCCalculateLight(inout SCLightData lightSum, inout SCShadingData sd, inout SCCustomData cd, SCVertexData vertex, SCLightData light)
{
    light.direction = SCModelSelectMainLightDirection(vertex, light.direction);
    cd.mainLightDirection = light.direction;
    SCModelPrepareMainLight(
        light,
        sd,
        cd.mainLightColor,
        cd.mainLightAttenuation,
        cd.mainLightNonShadowAttenuation,
        cd.mainLightShadowVisibility);

    __SC_PHASE_light__

    lightSum.direction += light.direction * dot(light.color, half3(0.333333, 0.333333, 0.333333));
    lightSum.color += light.color * SCModelEvaluateDirectFactor(sd, light);
}

/// <summary>Publishes the aggregate light direction and applies the selected model's ambient SH response.</summary>
void SCCalculateEnvironmentLight(inout SCLightData lightSum, inout half3 env, inout SCShadingData sd, inout SCCustomData cd, SCVertexData vertex, half4 shAr, half4 shAg, half4 shAb, half4 shBr, half4 shBg, half4 shBb, half4 shC)
{
    #if defined(PUREBASE_TOON_MODEL_INCLUDED) && !defined(LIGHTMAP_ON)
    sd.L = SCModelSelectAggregateLightDirection(lightSum.direction, unity_SHAr, unity_SHAg, unity_SHAb);
        #if !defined(UNITY_PASS_FORWARDADD)
        env += SCModelEvaluateAmbient(sd, unity_SHAr, unity_SHAg, unity_SHAb, unity_SHBr, unity_SHBg, unity_SHBb, unity_SHC);
        #endif
    #else
    sd.L = SCModelSelectAggregateLightDirection(lightSum.direction, shAr, shAg, shAb);
        #if !defined(UNITY_PASS_FORWARDADD)
        env += SCModelEvaluateAmbient(sd, shAr, shAg, shAb, shBr, shBg, shBb, shC);
        #endif
    #endif
}

#include "Packages/jp.lilxyzw.shadercore/ShaderLibrary/birp_lighting.hlsl"
#include "Packages/jp.penguin.purebase/Shaders/Common/birp_light_attenuation.hlsl"

/// <summary>Evaluates the selected model's ForwardBase or ForwardAdd result with all standard pixel phase insertion points.</summary>
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
    half coverage;
    SCInitializeSurface(sd, coverage, vertex);
    SCClipCutoutCoverage(coverage);
    SCBuildWorldTangentBasis(sd, vertex);

    SCLightData lightSum = (SCLightData)0;
    half3 env = half3(0, 0, 0);
    half mainLightShadowVisibility = UNITY_SHADOW_ATTENUATION(input, vertex.position);
    half mainLightNonShadowAttenuation = PureBaseEvaluateNonShadowLightAttenuation(input, vertex.position);
    cd.mainLightColor = _LightColor0.rgb;
    cd.mainLightShadowVisibility = saturate(mainLightShadowVisibility);
    cd.mainLightNonShadowAttenuation = saturate(mainLightNonShadowAttenuation);
    cd.mainLightAttenuation = cd.mainLightNonShadowAttenuation * cd.mainLightShadowVisibility;
    cd.mainLightDirection = half3(0, 0, 0);
    SCCalculateAllLights(lightSum, env, sd, cd, vertex, input, SCModelSelectVertexLighting(SCVertexLighting(vertex.position)));

    #if defined(UNITY_PASS_FORWARDADD)
    sd.lightColor = lightSum.color;
    #else
    env = SCModelSelectEnvironmentLighting(env);
    sd.lightColor = lightSum.color + env;
    #endif

    __SC_PHASE_modifylight__

    #if defined(UNITY_PASS_FORWARDADD)
    sd.col = SCModelAddSurfaceColor(sd, cd, vertex);
    #else
    sd.col = SCModelBaseSurfaceColor(sd, cd, vertex);
    #endif

    __SC_PHASE_shade__

    __SC_PHASE_reflection__

    __SC_PHASE_add__

    sd.col.rgb += sd.add + sd.postadd;
    PureBaseApplyRenderingModeOutputAlpha(sd.col, coverage);
    #if defined(UNITY_PASS_FORWARDADD)
    UNITY_APPLY_FOG_COLOR(input.fogCoord, sd.col, fixed4(0, 0, 0, 0));
    #else
    UNITY_APPLY_FOG(input.fogCoord, sd.col);
    #endif

    __SC_PHASE_postpixel__

    return sd.col;
}

#endif
