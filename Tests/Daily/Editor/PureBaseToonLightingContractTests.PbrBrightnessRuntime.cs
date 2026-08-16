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

// Owns isolated PBR direct-diffuse, Standard, SH, and reflection-probe runtime observations.

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Provides feature-specific additions to the isolated BIRP capture scope.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Owns PBR brightness-specific runtime capture extensions and caller-state restoration.</summary>
        private partial class ToonLightingCaptureRuntimeScope
        {
            /// <summary>Stores the caller's global reflection mode for restoration after the capture.</summary>
            private readonly DefaultReflectionMode originalReflectionMode = RenderSettings.defaultReflectionMode;

            /// <summary>Stores the caller's global custom reflection texture for restoration after the capture.</summary>
            private readonly Texture originalCustomReflection = RenderSettings.customReflectionTexture;

            /// <summary>Stores the caller's global reflection intensity for restoration after the capture.</summary>
            private readonly float originalReflectionIntensity = RenderSettings.reflectionIntensity;

            /// <summary>Tracks transient PBR brightness resources owned and destroyed by this capture scope.</summary>
            private readonly List<Object> pbrBrightnessResources = new List<Object>();

            /// <summary>Renders the metallic differential that isolates direct diffuse in the requested forward pass.</summary>
            /// <param name="shaderName">The required PBR or Hybrid product shader.</param>
            /// <param name="passName">The ForwardBase or ForwardAdd path to observe.</param>
            /// <param name="enabled">Whether the direct-diffuse brightness toggle is enabled.</param>
            /// <returns>The metallic-zero minus metallic-one direct diffuse observation.</returns>
            public Color RenderPbrBrightnessDiffuseDifferential(string shaderName, string passName, bool enabled)
            {
                Color metallicZero = RenderPbrBrightnessDirect(shaderName, passName, 0.0f, enabled);
                Color metallicOne = RenderPbrBrightnessDirect(shaderName, passName, 1.0f, enabled);
                return Subtract(metallicZero, metallicOne);
            }

            /// <summary>Renders a normal-incidence metallic-one direct-specular observation.</summary>
            /// <param name="shaderName">The required PBR or Hybrid product shader.</param>
            /// <param name="passName">The ForwardBase or ForwardAdd path to observe.</param>
            /// <param name="enabled">Whether the direct-diffuse brightness toggle is enabled.</param>
            /// <returns>The direct metallic-one observation.</returns>
            public Color RenderPbrBrightnessMetallicOne(string shaderName, string passName, bool enabled)
            {
                return RenderPbrBrightnessDirect(shaderName, passName, 1.0f, enabled);
            }

            /// <summary>Renders a real Standard ForwardAdd dielectric differential using two aligned Point lights.</summary>
            /// <returns>The Standard metallic-zero minus metallic-one contribution before its 0.96 correction.</returns>
            public Color RenderStandardForwardAddDiffuseDifferential()
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Color metallicZero = RenderStandardForwardAdd(0.0f);
                Color metallicOne = RenderStandardForwardAdd(1.0f);
                return Subtract(metallicZero, metallicOne);
            }

            /// <summary>Renders non-black renderer-local custom SH indirect diffuse with no direct light or reflection probe.</summary>
            /// <param name="shaderName">The required PBR or Hybrid product shader.</param>
            /// <param name="enabled">Whether the direct-diffuse brightness toggle is enabled.</param>
            /// <returns>The direct-light-free custom-SH indirect diffuse observation.</returns>
            public Color RenderPbrBrightnessIndirectDiffuse(string shaderName, bool enabled)
            {
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Material material = CreatePbrBrightnessMaterial(shaderName, "ForwardBase", 0.0f, enabled);
                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(Vector3.back, Vector4.zero, new Vector4(0.0f, 0.0f, -1.0f, 0.0f), CreateConstantSh()));
            }

            /// <summary>Renders non-black custom-reflection indirect specular with no direct light or SH diffuse.</summary>
            /// <param name="shaderName">The required PBR or Hybrid product shader.</param>
            /// <param name="enabled">Whether the direct-diffuse brightness toggle is enabled.</param>
            /// <returns>The direct-light-free reflection-probe specular observation.</returns>
            public Color RenderPbrBrightnessReflectionSpecular(string shaderName, bool enabled)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                ConfigureCustomReflection();
                Material material = CreatePbrBrightnessMaterial(shaderName, "ForwardBase", 1.0f, enabled);
                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(Vector3.back, Vector4.zero, new Vector4(0.0f, 0.0f, -1.0f, 0.0f), ShCoefficients.Zero));
            }

            /// <summary>Restores custom-reflection state and destroys extension-owned resources after every capture scope.</summary>
            partial void RestorePbrBrightnessCallerState()
            {
                RenderSettings.defaultReflectionMode = originalReflectionMode;
                RenderSettings.customReflectionTexture = originalCustomReflection;
                RenderSettings.reflectionIntensity = originalReflectionIntensity;
                foreach (Object resource in pbrBrightnessResources)
                {
                    Object.DestroyImmediate(resource);
                }

                pbrBrightnessResources.Clear();
            }

            /// <summary>Renders one low-albedo product material through the selected direct forward path.</summary>
            private Color RenderPbrBrightnessDirect(string shaderName, string passName, float metallic, bool enabled)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Material material = CreatePbrBrightnessMaterial(shaderName, passName, metallic, enabled);
                if (passName == "ForwardAdd")
                {
                    return RenderLightDifference(material, CreateLightCaptureRequest(Vector3.back, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), new Vector4(0.0f, 0.0f, -1.0f, 1.0f), ShCoefficients.Zero, LightType.Point, 4.0f));
                }

                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(Vector3.back, new Vector4(0.5f, 0.5f, 0.5f, 1.0f), new Vector4(0.0f, 0.0f, -1.0f, 0.0f), ShCoefficients.Zero));
            }

            /// <summary>Renders one Standard metallic state through the isolated second Point-light contribution.</summary>
            /// <param name="metallic">The Standard metallic input.</param>
            /// <returns>The isolated Standard ForwardAdd color.</returns>
            private Color RenderStandardForwardAdd(float metallic)
            {
                Shader shader = Shader.Find("Standard");
                Assert.That(shader, Is.Not.Null, "The Built-in Standard shader is unavailable.");
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                materials.Add(material);
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetColor("_Color", new Color(0.04f, 0.04f, 0.04f, 1.0f).gamma);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Glossiness", 0.75f);
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
                return RenderLightDifference(material, CreateLightCaptureRequest(Vector3.back, new Vector4(1.0f, 1.0f, 1.0f, 1.0f), new Vector4(0.0f, 0.0f, -1.0f, 1.0f), ShCoefficients.Zero, LightType.Point, 4.0f));
            }

            /// <summary>Creates a white-texture low-albedo PBR or Hybrid material and applies the requested brightness value.</summary>
            private Material CreatePbrBrightnessMaterial(string shaderName, string passName, float metallic, bool enabled)
            {
                Material material = CreateProductMaterial(shaderName, passName, metallic);
                material.SetTexture("_BaseTexture", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.04f, 1.0f).gamma);
                material.SetFloat("_Roughness", 0.25f);
                material.SetInteger("_UseUnityStandardDiffuseBrightness", enabled ? 1 : 0);
                return material;
            }

            /// <summary>Creates the positive L0-only SH input used to make direct-light-free indirect diffuse observable.</summary>
            private static ShCoefficients CreateConstantSh()
            {
                return new ShCoefficients(new Vector4(0.0f, 0.0f, 0.0f, 1.5f), new Vector4(0.0f, 0.0f, 0.0f, 1.5f), new Vector4(0.0f, 0.0f, 0.0f, 1.5f), Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);
            }

            /// <summary>Installs a fixture-owned constant cubemap as the isolated custom reflection source.</summary>
            private void ConfigureCustomReflection()
            {
                var cubemap = new Cubemap(1, TextureFormat.RGBAFloat, false) { hideFlags = HideFlags.HideAndDontSave };
                pbrBrightnessResources.Add(cubemap);
                foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                {
                    cubemap.SetPixel(face, 0, 0, new Color(2.0f, 1.5f, 1.0f, 1.0f));
                }

                cubemap.Apply(false, true);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cubemap;
                RenderSettings.reflectionIntensity = 1.0f;
            }
        }
    }
}