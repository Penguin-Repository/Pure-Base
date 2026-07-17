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

// Defines the lighting-independent surface output for the Pure-Base Unlit model.

#ifndef PUREBASE_UNLIT_MODEL_INCLUDED
#define PUREBASE_UNLIT_MODEL_INCLUDED

/// <summary>Initializes the Unlit tangent-space normal without sampling an optional normal map.</summary>
void SCModelInitializeTangentNormal(inout SCShadingData shadingData)
{
    shadingData.N = half3(0, 0, 1);
    shadingData.N_detail = half3(0, 0, 1);
}

/// <summary>Preserves every BIRP direct-light contribution for Shader-Core callbacks.</summary>
half SCModelEvaluateDirectFactor(SCShadingData shadingData, SCLightData light)
{
    return 1;
}

/// <summary>Disables model-specific ambient SH for the lighting-independent Unlit surface.</summary>
half3 SCModelEvaluateAmbient(SCShadingData shadingData, half4 shAr, half4 shAg, half4 shAb, half4 shBr, half4 shBg, half4 shBb, half4 shC)
{
    return half3(0, 0, 0);
}

/// <summary>Preserves Shader-Core's existing BIRP vertex-light input for the Unlit host.</summary>
half3 SCModelSelectVertexLighting(half3 vertexLighting)
{
    return vertexLighting;
}

/// <summary>Returns the base-phase albedo without adding direct, baked, or environmental lighting.</summary>
half4 SCUnlitSurfaceColor(SCShadingData shadingData)
{
    return half4(shadingData.albedoAlpha.rgb, 1);
}

/// <summary>Produces the Unlit ForwardBase result.</summary>
half4 SCModelBaseSurfaceColor(SCShadingData shadingData)
{
    return SCUnlitSurfaceColor(shadingData);
}

/// <summary>Produces the black Unlit ForwardAdd result.</summary>
half4 SCModelAddSurfaceColor(SCShadingData shadingData)
{
    return half4(0, 0, 0, 1);
}

#endif