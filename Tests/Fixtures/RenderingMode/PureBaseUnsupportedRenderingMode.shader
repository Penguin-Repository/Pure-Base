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

// Provides a supported non-PureBase shader with properties covering material atomicity test types.

Shader "PureBaseTests/Unsupported Rendering Mode"
{
    Properties
    {
        _RenderingMode ("Rendering Mode", Int) = 1
        _FloatProperty ("Float", Float) = 0
        _RangeProperty ("Range", Range(0, 1)) = 0.5
        _IntProperty ("Integer", Integer) = 0
        _ColorProperty ("Color", Color) = (1, 1, 1, 1)
        _VectorProperty ("Vector", Vector) = (0, 0, 0, 0)
        _TextureProperty ("Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            fixed4 _ColorProperty;

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return _ColorProperty;
            }
            ENDCG
        }
    }
}
