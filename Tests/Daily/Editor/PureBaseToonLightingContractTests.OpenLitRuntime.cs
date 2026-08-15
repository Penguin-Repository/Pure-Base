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

// Defines the OpenLit-specific ForwardAdd runtime capture operation.

using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines the OpenLit-specific ForwardAdd runtime capture operation.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Owns the OpenLit-specific runtime capture extensions.</summary>
        private partial class ToonLightingCaptureRuntimeScope
        {
            /// <summary>Renders the isolated second-light ForwardAdd contribution for a named diagnostic shader.</summary>
            /// <param name="shaderName">The required diagnostic shader name.</param>
            /// <param name="request">The coherent directional light and spherical-harmonic input.</param>
            /// <returns>The isolated ForwardAdd center linear readback.</returns>
            public Color RenderForwardAddLightDifference(string shaderName, LightCaptureRequest request)
            {
                return RenderLightDifference(CreateProductMaterial(shaderName, "ForwardAdd", 0.0f), request);
            }
        }
    }
}
