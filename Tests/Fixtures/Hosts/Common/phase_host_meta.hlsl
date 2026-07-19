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

// Defines the Meta pass used by persistent Shader-Core phase test hosts.

/// <summary>Declares Unity's Meta pass vertex input.</summary>
struct PhaseHostMetaAppData
{
    /// <summary>Provides the object-space position.</summary>
    float4 vertex : POSITION;
    /// <summary>Provides the primary UV.</summary>
    float2 uv0 : TEXCOORD0;
    /// <summary>Provides the static lightmap UV.</summary>
    float2 uv1 : TEXCOORD1;
    /// <summary>Provides the dynamic lightmap UV.</summary>
    float2 uv2 : TEXCOORD2;
};

/// <summary>Declares the interpolants required by Unity's Meta pass.</summary>
struct PhaseHostMetaVaryings
{
    /// <summary>Provides the Meta pass clip-space position.</summary>
    float4 position : SV_POSITION;
};

/// <summary>Converts object-space vertices to Unity's Meta pass position.</summary>
PhaseHostMetaVaryings PhaseHostMetaVertex(PhaseHostMetaAppData input)
{
    PhaseHostMetaVaryings output;
    output.position = UnityMetaVertexPosition(input.vertex, input.uv1, input.uv2, unity_LightmapST, unity_DynamicLightmapST);
    return output;
}

/// <summary>Returns deterministic albedo-only Meta data without standard phase insertion points.</summary>
float4 PhaseHostMetaFragment(PhaseHostMetaVaryings input) : SV_Target
{
    UnityMetaInput output;
    UNITY_INITIALIZE_OUTPUT(UnityMetaInput, output);
    output.Albedo = _PhaseHostColor.rgb;
    output.SpecularColor = 0;
    output.Emission = 0;
    return UnityMetaFragment(output);
}