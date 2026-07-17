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

// Defines the shared Shader-Core callbacks and cutout surface support for Pure-Base BIRP hosts.

#ifndef PUREBASE_SC_COMMON_INCLUDED
#define PUREBASE_SC_COMMON_INCLUDED

#ifndef PUREBASE_MODEL_INCLUDE
#define PUREBASE_MODEL_INCLUDE "Models/unlit.hlsl"
#endif

#include PUREBASE_MODEL_INCLUDE
#include "Common/surface.hlsl"

/// <summary>Runs the Shader-Core morph phase before vertex varyings are populated.</summary>
void SCVertexMorph(inout SCVertexData vertex, SCPositionAndDirection camera, SCPositionAndDirection head, SCPositionAndDirection headBone)
{
    __SC_PHASE_morph__
}

/// <summary>Runs the Shader-Core post-vertex phase before clip-space conversion.</summary>
void SCVertexPost(inout SCVertexData vertex, SCPositionAndDirection camera, SCPositionAndDirection head, SCPositionAndDirection headBone, float3 lightDirection = 0)
{
    __SC_PHASE_postvertex__
}

/// <summary>Evaluates immutable Cutout coverage for the Shader-Core shadow-caster wrapper.</summary>
void SCPixelClip(v2f input, bool isFront, float bitangentDirection)
{
    SCPositionAndDirection camera = SCGetCameraData();
    SCPositionAndDirection head = SCGetHeadData();
    SCPositionAndDirection headBone = SCGetHeadBoneData();
    SCVertexData vertex = FromPixelInput(input, camera, head, headBone, bitangentDirection, isFront);
    SCShadingData shadingData;
    half coverage;
    SCInitializeSurface(shadingData, coverage, vertex);
    SCClipCutoutCoverage(coverage);
}

#endif