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

// Defines deterministic Shader-Core surface initialization and module-compatible Cutout coverage.

#ifndef PUREBASE_SURFACE_INCLUDED
#define PUREBASE_SURFACE_INCLUDED

/// <summary>Initializes every shared shading field and executes the sole base phase insertion point.</summary>
void SCInitializeSurface(inout SCShadingData sd, out half coverage, SCVertexData vertex)
{
    sd.albedoAlpha = SCSample(_BaseTexture, sampler_BaseTexture, vertex.uv[0].xy * _BaseTexture_ST.xy + _BaseTexture_ST.zw) * _BaseColor;
    sd.col = half4(0, 0, 0, 0);
    sd.mask = SCSample(_SharedMask, sampler_BaseTexture, vertex.uv[0].xy * _BaseTexture_ST.xy + _BaseTexture_ST.zw);
    sd.uv = vertex.uv[0].xy * _BaseTexture_ST.xy + _BaseTexture_ST.zw;
    sd.T = vertex.T;
    sd.B = vertex.B;
    sd.N = half3(0, 0, 1);
    sd.N_detail = half3(0, 0, 1);
    sd.L = half3(0, 0, 0);
    sd.lightColor = half3(0, 0, 0);
    sd.shadow = 1;
    sd.roughness = half2(1, 1);
    sd.add = half3(0, 0, 0);
    sd.postadd = half3(0, 0, 0);
    sd.normalMapWithRoughness = false;
    sd.maskTexture = _SharedMask;
    sd.gradientsTexture = _SharedGradients;
    SCModelInitializeTangentNormal(sd);

    __SC_PHASE_base__

    sd.albedoAlpha.a = saturate(sd.albedoAlpha.a);
    sd.col = sd.albedoAlpha;
    coverage = sd.albedoAlpha.a;
}

/// <summary>Converts base-phase tangent-space normals into a valid world-space tangent basis.</summary>
void SCBuildWorldTangentBasis(inout SCShadingData sd, SCVertexData vertex)
{
    sd.N = normalize(mul(sd.N, vertex.TBN));
    sd.N_detail = normalize(mul(sd.N_detail, vertex.TBN));
    sd.T = normalize(vertex.T - sd.N_detail * dot(sd.N_detail, vertex.T));
    sd.B = normalize(cross(sd.N_detail, sd.T) * vertex.crossDirection * SCTangentScale());
}

/// <summary>Discards pixels below the module-adjusted Cutout coverage threshold.</summary>
void SCClipCutoutCoverage(half coverage)
{
    clip(coverage - _Cutoff);
}

#endif