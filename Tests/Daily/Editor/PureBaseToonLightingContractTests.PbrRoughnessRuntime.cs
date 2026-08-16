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

// Owns direct and reflection fixture setup for PBR perceptual-roughness GPU observations.

using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Provides roughness-specific extensions to the isolated BIRP capture scope.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Owns direct and reflection capture additions for the PBR roughness floor contracts.</summary>
        private partial class ToonLightingCaptureRuntimeScope
        {
            /// <summary>Renders a low-radiance metallic direct observation through an explicit forward pass.</summary>
            public Color RenderPbrRoughnessDirect(string shaderName, string passName, float roughness, Vector3 normal)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Material material = CreatePbrRoughnessMaterial(shaderName, passName, roughness);
                Vector3 lightDirection = Vector3.Reflect(Vector3.forward, normal.normalized).normalized;
                if (passName == "ForwardAdd")
                    return RenderLightDifference(material, CreateLightCaptureRequest(normal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 1.0f), ShCoefficients.Zero, LightType.Point, 4.0f));
                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(normal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f), ShCoefficients.Zero));
            }

            /// <summary>Renders direct-light-free metallic reflection from fixture-owned mip-distinct cubemap data.</summary>
            public Color RenderPbrRoughnessReflection(string shaderName, float roughness)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                ConfigurePbrRoughnessReflection();
                Material material = CreatePbrRoughnessMaterial(shaderName, "ForwardBase", roughness);
                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(Vector3.back, Vector4.zero, new Vector4(0.0f, 0.0f, -1.0f, 0.0f), ShCoefficients.Zero));
            }

            /// <summary>Creates a high-albedo metallic PBR-family material without a direct-diffuse contribution.</summary>
            private Material CreatePbrRoughnessMaterial(string shaderName, string passName, float roughness)
            {
                Material material = CreateProductMaterial(shaderName, passName, 1.0f);
                material.SetTexture("_BaseTexture", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Roughness", roughness);
                material.SetInteger("_UseUnityStandardDiffuseBrightness", 0);
                return material;
            }

            /// <summary>Installs a transient custom reflection cubemap with distinct finite colors in every mip level.</summary>
            private void ConfigurePbrRoughnessReflection()
            {
                var cubemap = new Cubemap(8, TextureFormat.RGBAFloat, true) { hideFlags = HideFlags.HideAndDontSave };
                pbrBrightnessResources.Add(cubemap);
                for (int mip = 0; mip < cubemap.mipmapCount; mip++)
                {
                    Color color = new Color(0.12f + (mip * 0.21f), 0.08f + (mip * 0.13f), 0.04f + (mip * 0.07f), 1.0f);
                    int size = Mathf.Max(1, cubemap.width >> mip);
                    Color[] pixels = CreatePbrRoughnessMipPixels(size, color);
                    foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                        cubemap.SetPixels(pixels, face, mip);
                }

                cubemap.Apply(false, true);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cubemap;
                RenderSettings.reflectionIntensity = 1.0f;
            }

            /// <summary>Creates the uniformly colored pixels assigned to one owned cubemap mip level.</summary>
            private static Color[] CreatePbrRoughnessMipPixels(int size, Color color)
            {
                var pixels = new Color[size * size];
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = color;
                return pixels;
            }
        }
    }
}
