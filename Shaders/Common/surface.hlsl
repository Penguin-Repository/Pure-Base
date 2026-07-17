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

// Defines deterministic Shader-Core surface initialization and immutable Cutout coverage.

#ifndef PUREBASE_SURFACE_INCLUDED
#define PUREBASE_SURFACE_INCLUDED

/// <summary>Initializes every shared shading field and executes the sole base phase insertion point.</summary>
void SCInitializeSurface(inout SCShadingData shadingData, out half coverage, SCVertexData vertex)
{
    shadingData.albedoAlpha = SCSample(_BaseTexture, sampler_BaseTexture, vertex.uv[0].xy * _BaseTexture_ST.xy + _BaseTexture_ST.zw) * _BaseColor;
    coverage = shadingData.albedoAlpha.a;
    shadingData.col = half4(0, 0, 0, 0);
    shadingData.mask = SCSample(_SharedMask, sampler_BaseTexture, vertex.uv[0].xy * _BaseTexture_ST.xy + _BaseTexture_ST.zw);
    shadingData.uv = vertex.uv[0].xy * _BaseTexture_ST.xy + _BaseTexture_ST.zw;
    shadingData.T = vertex.T;
    shadingData.B = vertex.B;
    shadingData.N = half3(0, 0, 1);
    shadingData.N_detail = half3(0, 0, 1);
    shadingData.L = half3(0, 0, 0);
    shadingData.lightColor = half3(0, 0, 0);
    shadingData.shadow = 1;
    shadingData.roughness = half2(1, 1);
    shadingData.add = half3(0, 0, 0);
    shadingData.postadd = half3(0, 0, 0);
    shadingData.normalMapWithRoughness = false;
    shadingData.maskTexture = _SharedMask;
    shadingData.gradientsTexture = _SharedGradients;
    SCModelInitializeTangentNormal(shadingData);

    __SC_PHASE_base__
}

/// <summary>Converts base-phase tangent-space normals into a valid world-space tangent basis.</summary>
void SCBuildWorldTangentBasis(inout SCShadingData shadingData, SCVertexData vertex)
{
    shadingData.N = normalize(mul(shadingData.N, vertex.TBN));
    shadingData.N_detail = normalize(mul(shadingData.N_detail, vertex.TBN));
    shadingData.T = normalize(vertex.T - shadingData.N_detail * dot(shadingData.N_detail, vertex.T));
    shadingData.B = normalize(cross(shadingData.N_detail, shadingData.T) * vertex.crossDirection * SCTangentScale());
}

/// <summary>Discards pixels below the fixed Cutout coverage threshold.</summary>
void SCClipCutoutCoverage(half coverage)
{
    clip(coverage - _Cutoff);
}

#endif