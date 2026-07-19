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

// Defines the Shader-Core callbacks expanded before the phase host pixel implementation.

#ifndef PUREBASE_TEST_PHASE_HOST_SC_COMMON_INCLUDED
#define PUREBASE_TEST_PHASE_HOST_SC_COMMON_INCLUDED

/// <summary>Runs the Shader-Core morph insertion point before varyings are populated.</summary>
void SCVertexMorph(inout SCVertexData vertex, SCPositionAndDirection camera, SCPositionAndDirection head, SCPositionAndDirection headBone)
{
    __SC_PHASE_morph__
}

/// <summary>Runs the Shader-Core post-vertex insertion point before clip-space conversion.</summary>
void SCVertexPost(inout SCVertexData vertex, SCPositionAndDirection camera, SCPositionAndDirection head, SCPositionAndDirection headBone, float3 lightDirection = 0)
{
    __SC_PHASE_postvertex__
}

/// <summary>Builds deterministic base color at the Shader-Core base insertion point.</summary>
half4 SCGetPhaseHostBaseColor()
{
    SCShadingData sd;
    sd.albedoAlpha = _PhaseHostColor;

    __SC_PHASE_base__

    return sd.albedoAlpha;
}

/// <summary>Applies the host's fixed alpha test for non-view passes.</summary>
void SCPixelClip(v2f input, bool isFront, float bitangentDirection)
{
    clip(_PhaseHostColor.a - _PhaseHostCutoff);
}

#endif