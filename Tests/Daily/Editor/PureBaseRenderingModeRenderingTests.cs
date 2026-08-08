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

// Defines source-order and BIRP numeric rendering contracts for rendering-mode alpha, depth, lighting, ShadowCaster, and Meta behavior.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines focused BIRP rendering-mode observations without changing canonical scenes or baselines.</summary>
    public sealed class PureBaseRenderingModeRenderingTests
    {
        /// <summary>Tracks transient materials so rendering observations release every native Unity object they allocate.</summary>
        private readonly List<Material> transientMaterials = new List<Material>();

        /// <summary>Identifies the common BIRP fragment host whose ordering is part of the generated-source ABI.</summary>
        private const string BirpHostPath = "Packages/jp.penguin.purebase/Shaders/Common/birp_host.hlsl";

        /// <summary>Identifies the shared rendering-mode helper that owns mode clip and output-alpha semantics.</summary>
        private const string RenderingModeHelperPath = "Packages/jp.penguin.purebase/Shaders/Common/rendering_mode.hlsl";

        /// <summary>Identifies the shared operation that publishes the mode-specific output alpha.</summary>
        private const string RenderingModeOutputAlphaOperation = "PureBaseApplyRenderingModeOutputAlpha";

        /// <summary>Identifies the rendering-mode keyword whose output alpha preserves coverage.</summary>
        private const string TransparentRenderingModeKeyword = "PUREBASE_RENDERING_TRANSPARENT";

        /// <summary>Identifies the release-only postpixel alpha probe source.</summary>
        private const string PostPixelProbePath = "Packages/jp.penguin.purebase/Tests/Release/Modules/RenderingMode/PostPixelAlpha/phase_postpixel.hlsl";

        /// <summary>Defines the small readback dimension used by transient numeric observations.</summary>
        private const int RenderSize = 64;

        /// <summary>Defines the largest per-channel readback difference treated as directional-shadow noise.</summary>
        private const float ShadowPixelNoiseThreshold = 0.002f;

        /// <summary>Defines the minimum changed-pixel count required for a meaningful directional-shadow silhouette.</summary>
        private const int MinimumShadowSilhouettePixelCount = 32;

        /// <summary>Requires the shared mode-alpha helper to run after add and before fog, postpixel, and return.</summary>
        [Test]
        public void BirpHostPreservesModeAlphaFogPostPixelAndForwardAddSourceOrder()
        {
            string host = File.ReadAllText(BirpHostPath);
            string renderingModeHelper = File.ReadAllText(RenderingModeHelperPath);
            int addPhase = RequireIndex(host, "__SC_PHASE_add__");
            Match modeOutputAlphaCall = Regex.Match(host, @"\b" + Regex.Escape(RenderingModeOutputAlphaOperation) + @"\s*\(");
            Assert.That(modeOutputAlphaCall.Success, Is.True, "The BIRP host must call the shared rendering-mode output-alpha operation.");
            int modeOutputAlpha = modeOutputAlphaCall.Index;
            int fog = RequireIndex(host, "UNITY_APPLY_FOG");
            int postPixel = RequireIndex(host, "__SC_PHASE_postpixel__");
            int returnStatement = RequireIndex(host, "return sd.col;");
            StringAssert.Contains("#include \"Packages/jp.penguin.purebase/Shaders/Common/rendering_mode.hlsl\"", host);
            Assert.That(modeOutputAlpha, Is.GreaterThan(addPhase), "The shared mode-alpha helper must run after the add phase.");
            Assert.That(modeOutputAlpha, Is.LessThan(fog), "The shared mode-alpha helper must run before fog.");
            Assert.That(fog, Is.LessThan(postPixel), "Fog must occur before postpixel.");
            Assert.That(postPixel, Is.LessThan(returnStatement), "Postpixel must remain the final color mutation point before return.");
            StringAssert.Contains(RenderingModeOutputAlphaOperation, renderingModeHelper);
            StringAssert.Contains(TransparentRenderingModeKeyword, renderingModeHelper);
            StringAssert.Contains("coverage", renderingModeHelper);
            StringAssert.Contains(".a", renderingModeHelper);
            Assert.That(
                Regex.IsMatch(renderingModeHelper, @"\b1(?:\.0+)?\b"),
                Is.True,
                "The shared helper must distinguish Transparent coverage alpha from Opaque and Cutout alpha one."
            );
            string generatedProductSource = LoadProductSource("PureBase/Toon");
            Assert.That(
                Regex.IsMatch(generatedProductSource, @"\b" + Regex.Escape(RenderingModeOutputAlphaOperation) + @"\s*\("),
                Is.True,
                "The generated product source must retain the shared rendering-mode output-alpha operation."
            );
            StringAssert.Contains("Blend [_AddSrcBlend] [_AddDstBlend]", generatedProductSource);
            StringAssert.Contains("ColorMask RGB", generatedProductSource);
            StringAssert.Contains("sd.col.a = half(0.25)", File.ReadAllText(PostPixelProbePath));
        }

        /// <summary>Requires representative Opaque, Cutout, and Transparent material state before fragile BIRP observations execute.</summary>
        [Test]
        public void RepresentativeModesHaveNumericAlphaDepthAndContributionObservationPreconditions()
        {
            Shader unlit = RequireProductShader("PureBase/Unlit");
            Shader toon = RequireProductShader("PureBase/Toon");
            var opaque = CreateMaterial(unlit);
            var cutout = CreateMaterial(unlit);
            var transparent = CreateMaterial(unlit);
            var transparentToon = CreateMaterial(toon);
            {
                RequireRenderingModeProperty(opaque);
                ConfigureMode(opaque, 0);
                ConfigureMode(cutout, 1);
                ConfigureMode(transparent, 2);
                ConfigureMode(transparentToon, 2);

                Assert.That(opaque.GetFloat("_ZWrite"), Is.EqualTo(1.0f));
                Assert.That(cutout.GetFloat("_ZWrite"), Is.EqualTo(1.0f));
                Assert.That(transparent.GetFloat("_ZWrite"), Is.EqualTo(0.0f));
                Assert.That(transparent.GetFloat("_AddSrcBlend"), Is.EqualTo((float)BlendMode.SrcAlpha));
                Assert.That(transparentToon.GetShaderPassEnabled("ShadowCaster"), Is.False);
                Assert.That(transparentToon.GetShaderPassEnabled("Meta"), Is.False);
            }
        }

        /// <summary>Defines finite, threshold, and numeric alpha metrics for the future BIRP mode rendering observations.</summary>
        [Test]
        public void NumericObservationMetricsRejectOpaqueAlphaLeakCutoutLeakAndTransparentDepthOrAddAlphaErrors()
        {
            Shader shader = RequireProductShader("PureBase/Unlit");
            var opaque = CreateConfiguredMaterial(shader, 0, new Color(0.8f, 0.2f, 0.1f, 0.1f));
            var cutoutBelow = CreateConfiguredMaterial(shader, 1, new Color(0.8f, 0.2f, 0.1f, 0.25f));
            var transparent = CreateConfiguredMaterial(shader, 2, new Color(0.8f, 0.2f, 0.1f, 0.25f));
            {
                RequireRenderingModeProperty(opaque);
                Color opaquePixel = RenderCenterPixel(opaque, Color.clear);
                Color cutoutPixel = RenderCenterPixel(cutoutBelow, Color.clear);
                Color transparentPixel = RenderCenterPixel(transparent, Color.clear);
                AssertFinite(opaquePixel, "Opaque readback");
                AssertFinite(cutoutPixel, "Cutout readback");
                AssertFinite(transparentPixel, "Transparent readback");
                Assert.That(opaquePixel.a, Is.GreaterThan(0.95f), "Opaque output must ignore base alpha.");
                Assert.That(cutoutPixel.a, Is.LessThan(0.02f), "Cutout coverage below _Cutoff must not contribute.");
                Assert.That(
                    transparentPixel.a,
                    Is.EqualTo(0.0625f).Within(0.005f),
                    "Transparent source alpha 0.25 over clear destination alpha 0 must use standard alpha blending: 0.25 * 0.25."
                );
            }
        }

        /// <summary>Requires Transparent material sorting to produce the expected finite back-to-front two-layer readback without depth writes.</summary>
        [Test]
        public void TransparentDepthOrderingUsesBackToFrontCompositionWithoutDepthWrite()
        {
            Shader shader = RequireProductShader("PureBase/Unlit");
            var red = CreateConfiguredMaterial(shader, 2, new Color(1.0f, 0.0f, 0.0f, 0.25f));
            var blue = CreateConfiguredMaterial(shader, 2, new Color(0.0f, 0.0f, 1.0f, 0.25f));
            {
                Color redInFront = RenderLayeredCenterPixel(red, blue);
                Color blueInFront = RenderLayeredCenterPixel(blue, red);
                AssertFinite(redInFront, "Red-front transparent depth readback");
                AssertFinite(blueInFront, "Blue-front transparent depth readback");
                Assert.That(redInFront.r, Is.EqualTo(0.25f).Within(0.05f));
                Assert.That(redInFront.b, Is.EqualTo(0.1875f).Within(0.05f));
                Assert.That(blueInFront.b, Is.EqualTo(0.25f).Within(0.05f));
                Assert.That(blueInFront.r, Is.EqualTo(0.1875f).Within(0.05f));
                Assert.That(redInFront.r, Is.GreaterThan(redInFront.b + 0.02f));
                Assert.That(blueInFront.b, Is.GreaterThan(blueInFront.r + 0.02f));
            }
        }

        /// <summary>Requires Transparent ForwardBase to leave depth unchanged so an explicitly later opaque marker behind it remains visible.</summary>
        [Test]
        public void TransparentDepthWriteDoesNotOccludeAnExplicitlyLaterOpaqueMarker()
        {
            Shader transparentShader = RequireProductShader("PureBase/Unlit");
            Shader markerShader = Shader.Find("Unlit/Color");
            Assert.That(markerShader, Is.Not.Null, "The Built-in Unlit/Color shader is unavailable for the Transparent depth-write probe.");
            var transparent = CreateConfiguredMaterial(transparentShader, 2, new Color(1.0f, 0.0f, 0.0f, 0.25f));
            var marker = CreateMaterial(markerShader);
            {
                marker.SetColor("_Color", Color.green);
                Color observed = RenderTransparentThenOpaqueDepthProbe(transparent, marker);
                AssertFinite(observed, "Transparent explicit-depth probe readback");
                Assert.That(
                    observed.g,
                    Is.GreaterThan(0.85f),
                    "Transparent ZWrite Off must allow the explicitly later opaque marker behind the transparent surface to pass depth."
                );
                Assert.That(
                    observed.r,
                    Is.LessThan(0.08f),
                    "The later opaque marker must replace the transparent probe color when Transparent does not write depth."
                );
            }
        }

        /// <summary>Requires controlled numeric ShadowCaster and Meta readbacks for all three rendering-mode contribution boundaries.</summary>
        [Test]
        public void OpaqueCutoutAndTransparentModesHaveObservedShadowCasterAndMetaContributions()
        {
            Shader shader = RequireProductShader("PureBase/Unlit");
            Color contributingBaseColor = new Color(0.8f, 0.2f, 0.1f, 1.0f);
            var opaque = CreateConfiguredMaterial(shader, 0, contributingBaseColor);
            var cutout = CreateConfiguredMaterial(shader, 1, contributingBaseColor);
            var cutoutBelow = CreateConfiguredMaterial(shader, 1, new Color(0.8f, 0.2f, 0.1f, 0.25f));
            var transparent = CreateConfiguredMaterial(shader, 2, new Color(0.8f, 0.2f, 0.1f, 0.25f));
            {
                Assert.That(opaque.GetShaderPassEnabled("ShadowCaster"), Is.True, "Opaque ShadowCaster must be enabled before its silhouette is observed.");
                Assert.That(cutout.GetShaderPassEnabled("ShadowCaster"), Is.True, "Cutout ShadowCaster must be enabled before its silhouette is observed.");
                Assert.That(cutoutBelow.GetShaderPassEnabled("ShadowCaster"), Is.True, "Cutout below-cutoff ShadowCaster must remain enabled so clip behavior is observed at runtime.");
                Assert.That(transparent.GetShaderPassEnabled("ShadowCaster"), Is.False, "Transparent ShadowCaster must be disabled before its missing silhouette is observed.");
                ShadowReadback opaqueShadow = RenderShadowReadback(opaque);
                ShadowReadback cutoutShadow = RenderShadowReadback(cutout);
                ShadowReadback cutoutBelowShadow = RenderShadowReadback(cutoutBelow);
                ShadowReadback transparentShadow = RenderShadowReadback(transparent);
                AssertFinite(opaqueShadow.maxAbsoluteRgbDelta, "Opaque ShadowCaster maximum RGB delta");
                AssertFinite(cutoutShadow.maxAbsoluteRgbDelta, "Cutout ShadowCaster maximum RGB delta");
                AssertFinite(cutoutBelowShadow.maxAbsoluteRgbDelta, "Cutout below-cutoff ShadowCaster maximum RGB delta");
                AssertFinite(transparentShadow.maxAbsoluteRgbDelta, "Transparent ShadowCaster maximum RGB delta");
                Assert.That(opaqueShadow.maxAbsoluteRgbDelta, Is.GreaterThan(ShadowPixelNoiseThreshold), opaqueShadow.Describe("Opaque"));
                Assert.That(opaqueShadow.changedPixelCount, Is.GreaterThan(MinimumShadowSilhouettePixelCount), opaqueShadow.Describe("Opaque"));
                Assert.That(cutoutShadow.maxAbsoluteRgbDelta, Is.GreaterThan(ShadowPixelNoiseThreshold), cutoutShadow.Describe("Cutout"));
                Assert.That(cutoutShadow.changedPixelCount, Is.GreaterThan(MinimumShadowSilhouettePixelCount), cutoutShadow.Describe("Cutout"));
                Assert.That(
                    cutoutShadow.maxAbsoluteRgbDelta,
                    Is.GreaterThan(opaqueShadow.maxAbsoluteRgbDelta * 0.25f),
                    cutoutShadow.Describe("Cutout") + " must retain a visible silhouette relative to Opaque."
                );
                Assert.That(
                    cutoutShadow.changedPixelCount,
                    Is.GreaterThan(opaqueShadow.changedPixelCount * 0.25f),
                    cutoutShadow.Describe("Cutout") + " must retain sufficient changed pixels relative to Opaque."
                );
                Assert.That(cutoutBelowShadow.maxAbsoluteRgbDelta, Is.LessThanOrEqualTo(ShadowPixelNoiseThreshold), cutoutBelowShadow.Describe("Cutout below cutoff"));
                Assert.That(cutoutBelowShadow.changedPixelCount, Is.LessThanOrEqualTo(MinimumShadowSilhouettePixelCount), cutoutBelowShadow.Describe("Cutout below cutoff"));
                Assert.That(transparentShadow.maxAbsoluteRgbDelta, Is.LessThanOrEqualTo(ShadowPixelNoiseThreshold), transparentShadow.Describe("Transparent"));
                Assert.That(transparentShadow.changedPixelCount, Is.LessThanOrEqualTo(MinimumShadowSilhouettePixelCount), transparentShadow.Describe("Transparent"));
                float minimumContributingShadowDelta = Mathf.Min(opaqueShadow.maxAbsoluteRgbDelta, cutoutShadow.maxAbsoluteRgbDelta);
                int minimumContributingShadowPixels = Mathf.Min(opaqueShadow.changedPixelCount, cutoutShadow.changedPixelCount);
                Assert.That(
                    transparentShadow.maxAbsoluteRgbDelta,
                    Is.LessThan(minimumContributingShadowDelta * 0.25f),
                    transparentShadow.Describe("Transparent") + " must remain below the Opaque and Cutout contribution boundary."
                );
                Assert.That(
                    transparentShadow.changedPixelCount,
                    Is.LessThan(minimumContributingShadowPixels * 0.25f),
                    transparentShadow.Describe("Transparent") + " must remain below the Opaque and Cutout changed-pixel contribution boundary."
                );

                Color opaqueMeta = RenderMetaCenterPixel(opaque);
                Color expectedContributingMeta = contributingBaseColor.linear;
                AssertFinite(opaqueMeta, "Opaque Meta readback");
                Assert.That(opaqueMeta.r, Is.EqualTo(expectedContributingMeta.r).Within(0.08f));
                Assert.That(opaqueMeta.g, Is.EqualTo(expectedContributingMeta.g).Within(0.08f));
                Assert.That(opaqueMeta.b, Is.EqualTo(expectedContributingMeta.b).Within(0.08f));
                float opaqueMetaMagnitude = RgbMagnitude(opaqueMeta);
                Assert.That(opaqueMetaMagnitude, Is.GreaterThan(0.2f), "Opaque Meta pass must contribute non-clear albedo data.");

                Color cutoutMeta = RenderMetaCenterPixel(cutout);
                AssertFinite(cutoutMeta, "Cutout Meta readback");
                Assert.That(cutoutMeta.r, Is.EqualTo(expectedContributingMeta.r).Within(0.08f));
                Assert.That(cutoutMeta.g, Is.EqualTo(expectedContributingMeta.g).Within(0.08f));
                Assert.That(cutoutMeta.b, Is.EqualTo(expectedContributingMeta.b).Within(0.08f));
                float cutoutMetaMagnitude = RgbMagnitude(cutoutMeta);
                Assert.That(cutoutMetaMagnitude, Is.GreaterThan(0.2f), "Cutout Meta pass must contribute non-clear albedo data.");

                Color transparentMeta = RenderMetaCenterPixel(transparent);
                AssertFinite(transparentMeta, "Transparent Meta readback");
                float transparentMetaMagnitude = RgbMagnitude(transparentMeta);
                Assert.That(
                    transparentMetaMagnitude,
                    Is.LessThan(0.02f),
                    "Transparent Meta must not contribute effective albedo data in the actual BIRP readback."
                );
                float minimumContributingMetaMagnitude = Mathf.Min(opaqueMetaMagnitude, cutoutMetaMagnitude);
                Assert.That(
                    transparentMetaMagnitude,
                    Is.LessThan(minimumContributingMetaMagnitude * 0.25f),
                    "Transparent Meta must remain below the Opaque and Cutout contribution boundary."
                );
            }
        }

        /// <summary>Requires Transparent Toon ForwardAdd to accumulate a second light in RGB while preserving the once-blended destination alpha.</summary>
        [Test]
        public void TransparentToonForwardAddAccumulatesRgbBySourceAlphaWithoutChangingDestinationAlpha()
        {
            Shader toon = RequireProductShader("PureBase/Toon");
            var lowAlphaMaterial = CreateConfiguredMaterial(toon, 2, new Color(0.8f, 0.6f, 0.4f, 0.25f));
            var highAlphaMaterial = CreateConfiguredMaterial(toon, 2, new Color(0.8f, 0.6f, 0.4f, 0.5f));
            {
                Color oneLowAlphaLight = RenderTransparentToonPixel(lowAlphaMaterial, 1);
                Color twoLowAlphaLights = RenderTransparentToonPixel(lowAlphaMaterial, 2);
                Color oneHighAlphaLight = RenderTransparentToonPixel(highAlphaMaterial, 1);
                Color twoHighAlphaLights = RenderTransparentToonPixel(highAlphaMaterial, 2);
                AssertFinite(oneLowAlphaLight, "Transparent Toon low-alpha one-light readback");
                AssertFinite(twoLowAlphaLights, "Transparent Toon low-alpha two-light readback");
                AssertFinite(oneHighAlphaLight, "Transparent Toon high-alpha one-light readback");
                AssertFinite(twoHighAlphaLights, "Transparent Toon high-alpha two-light readback");
                float lowAlphaAddDelta = RgbMagnitude(twoLowAlphaLights - oneLowAlphaLight);
                float highAlphaAddDelta = RgbMagnitude(twoHighAlphaLights - oneHighAlphaLight);
                AssertFinite(lowAlphaAddDelta, "Transparent Toon low-alpha ForwardAdd delta");
                AssertFinite(highAlphaAddDelta, "Transparent Toon high-alpha ForwardAdd delta");
                Assert.That(
                    lowAlphaAddDelta,
                    Is.GreaterThan(0.01f),
                    "A second ForwardAdd light must increase Transparent Toon RGB contribution."
                );
                Assert.That(
                    highAlphaAddDelta,
                    Is.GreaterThan(lowAlphaAddDelta),
                    "ForwardAdd RGB must respond to the Transparent source alpha."
                );
                Assert.That(
                    highAlphaAddDelta / lowAlphaAddDelta,
                    Is.InRange(1.65f, 2.35f),
                    "Doubling Transparent source alpha must double the isolated ForwardAdd RGB delta; alpha-ignored and alpha-squared contributions are invalid."
                );
                Assert.That(
                    twoLowAlphaLights.a,
                    Is.EqualTo(oneLowAlphaLight.a).Within(0.01f),
                    "ForwardAdd must not modify the destination alpha written by ForwardBase."
                );
                Assert.That(
                    twoHighAlphaLights.a,
                    Is.EqualTo(oneHighAlphaLight.a).Within(0.01f),
                    "ForwardAdd must not modify the destination alpha written by ForwardBase at either source alpha."
                );
                Assert.That(
                    oneLowAlphaLight.a,
                    Is.InRange(0.49f, 0.54f),
                    "ForwardBase must blend the 0.25 source alpha exactly once against the 0.60 destination alpha."
                );
            }
        }

        /// <summary>Requires Transparent materials to disable both contribution passes for every public product before shadow or Meta work can run.</summary>
        [Test]
        public void TransparentMaterialsHaveNoEffectiveShadowCasterOrMetaContribution()
        {
            foreach (string shaderName in new[] { "PureBase/Unlit", "PureBase/Toon", "PureBase/PBR", "PureBase/Hybrid" })
            {
                var material = CreateMaterial(RequireProductShader(shaderName));
                ConfigureMode(material, 2);
                Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.False, shaderName + " Transparent ShadowCaster contribution.");
                Assert.That(material.GetShaderPassEnabled("Meta"), Is.False, shaderName + " Transparent Meta contribution.");
            }
        }

        /// <summary>Creates one configured transient material without saving or modifying any persistent asset.</summary>
        /// <param name="shader">The source shader.</param>
        /// <param name="mode">The requested rendering-mode value.</param>
        /// <param name="baseColor">The base color assigned before rendering.</param>
        /// <returns>The caller-owned material.</returns>
        private Material CreateConfiguredMaterial(Shader shader, int mode, Color baseColor)
        {
            Material material = CreateMaterial(shader);
            material.SetColor("_BaseColor", baseColor);
            material.SetFloat("_Cutoff", 0.5f);
            ConfigureMode(material, mode);
            return material;
        }

        /// <summary>Creates and registers one transient material for deterministic test cleanup.</summary>
        /// <param name="shader">The shader assigned to the material.</param>
        /// <returns>The tracked material.</returns>
        private Material CreateMaterial(Shader shader)
        {
            var material = new Material(shader);
            transientMaterials.Add(material);
            return material;
        }

        /// <summary>Releases every transient material after each rendering observation, including failure paths.</summary>
        [TearDown]
        public void DestroyTransientMaterials()
        {
            foreach (Material material in transientMaterials)
            {
                if (material != null)
                    UnityEngine.Object.DestroyImmediate(material);
            }

            transientMaterials.Clear();
        }

        /// <summary>Calls the reflected public normalizer after assigning the public mode value.</summary>
        /// <param name="material">The material to normalize.</param>
        /// <param name="mode">The requested mode value.</param>
        private static void ConfigureMode(Material material, int mode)
        {
            material.SetInteger("_RenderingMode", mode);
            Type type = FindLoadedType("PureBase.Editor.PureBaseMaterialRenderingMode");
            Assert.That(type, Is.Not.Null, "PureBaseMaterialRenderingMode is required for rendering observations.");
            var apply = type.GetMethod("Apply", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(Material) }, null);
            Assert.That(apply, Is.Not.Null, "PureBaseMaterialRenderingMode.Apply(Material) is required for rendering observations.");
            apply.Invoke(null, new object[] { material });
        }

        /// <summary>Renders a full-frame quad through a temporary camera and returns its center pixel.</summary>
        /// <param name="material">The transient material to render.</param>
        /// <param name="background">The camera clear color.</param>
        /// <returns>The center readback pixel.</returns>
        private static Color RenderCenterPixel(Material material, Color background)
        {
            GameObject cameraObject = null;
            GameObject quadObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            Camera camera = null;
            try
            {
                cameraObject = new GameObject("PureBaseRenderingModeCamera");
                quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                renderTexture = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBFloat);
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
                camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 0.5f;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = background;
                camera.targetTexture = renderTexture;
                quadObject.GetComponent<Renderer>().sharedMaterial = material;
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = renderTexture;
                    texture.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0);
                    texture.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                return texture.GetPixel(RenderSize / 2, RenderSize / 2);
            }
            finally
            {
                if (camera != null)
                    camera.targetTexture = null;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (quadObject != null)
                    UnityEngine.Object.DestroyImmediate(quadObject);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>Renders two Transparent quads at controlled depths and returns the center pixel after Unity's transparent sorting.</summary>
        /// <param name="frontMaterial">The material assigned to the camera-nearest quad.</param>
        /// <param name="rearMaterial">The material assigned to the camera-farthest quad.</param>
        /// <returns>The sorted layered center readback.</returns>
        private static Color RenderLayeredCenterPixel(Material frontMaterial, Material rearMaterial)
        {
            GameObject cameraObject = null;
            GameObject frontObject = null;
            GameObject rearObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            try
            {
                cameraObject = new GameObject("PureBaseRenderingModeDepthCamera");
                frontObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                rearObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                renderTexture = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBFloat);
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 0.5f;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.targetTexture = renderTexture;
                frontObject.transform.position = Vector3.zero;
                rearObject.transform.position = new Vector3(0.0f, 0.0f, 0.1f);
                frontObject.GetComponent<Renderer>().sharedMaterial = frontMaterial;
                rearObject.GetComponent<Renderer>().sharedMaterial = rearMaterial;
                camera.Render();
                return ReadCenterPixel(renderTexture, texture);
            }
            finally
            {
                Camera camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
                if (camera != null)
                    camera.targetTexture = null;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (rearObject != null)
                    UnityEngine.Object.DestroyImmediate(rearObject);
                if (frontObject != null)
                    UnityEngine.Object.DestroyImmediate(frontObject);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>Draws Transparent before an opaque marker at a farther depth to make Transparent depth-write behavior observable.</summary>
        /// <param name="transparentMaterial">The configured Transparent material drawn first.</param>
        /// <param name="markerMaterial">The opaque marker material drawn after Transparent.</param>
        /// <returns>The center pixel after the controlled explicit draw order.</returns>
        private static Color RenderTransparentThenOpaqueDepthProbe(Material transparentMaterial, Material markerMaterial)
        {
            GameObject cameraObject = null;
            GameObject quadObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            CommandBuffer commandBuffer = null;
            Camera camera = null;
            try
            {
                cameraObject = new GameObject("PureBaseRenderingModeExplicitDepthCamera");
                quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                renderTexture = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBFloat);
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 0;
                camera.orthographic = true;
                camera.orthographicSize = 0.5f;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.targetTexture = renderTexture;
                renderTexture.Create();
                int transparentPass = transparentMaterial.FindPass("ForwardBase");
                Assert.That(transparentPass, Is.GreaterThanOrEqualTo(0), "The Transparent depth probe requires ForwardBase.");
                commandBuffer = new CommandBuffer { name = "PureBase Rendering Mode Explicit Depth Probe" };
                Mesh quadMesh = quadObject.GetComponent<MeshFilter>().sharedMesh;
                commandBuffer.DrawMesh(quadMesh, Matrix4x4.identity, transparentMaterial, 0, transparentPass);
                commandBuffer.DrawMesh(quadMesh, Matrix4x4.Translate(new Vector3(0.0f, 0.0f, 0.1f)), markerMaterial, 0, 0);
                camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                camera.Render();
                return ReadCenterPixel(renderTexture, texture);
            }
            finally
            {
                if (camera != null && commandBuffer != null)
                    camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                if (commandBuffer != null)
                    commandBuffer.Release();
                if (camera != null)
                    camera.targetTexture = null;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (quadObject != null)
                    UnityEngine.Object.DestroyImmediate(quadObject);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>Renders an isolated directional-light fixture with and without shadows and returns the measured receiver silhouette.</summary>
        /// <param name="material">The configured material assigned to the shadow caster.</param>
        /// <returns>The controlled actual ShadowCaster readback.</returns>
        private static ShadowReadback RenderShadowReadback(Material material)
        {
            const int fixtureLayer = 31;
            Scene scene = default(Scene);
            GameObject cameraObject = null;
            GameObject lightObject = null;
            GameObject receiver = null;
            GameObject caster = null;
            Material receiverMaterial = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            try
            {
                scene = EditorSceneManager.NewPreviewScene();
                cameraObject = new GameObject("PureBaseRenderingModeShadowCamera");
                lightObject = new GameObject("PureBaseRenderingModeShadowLight");
                receiver = GameObject.CreatePrimitive(PrimitiveType.Plane);
                caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Shader receiverShader = Shader.Find("Standard");
                Assert.That(receiverShader, Is.Not.Null, "The Built-in Standard shader is unavailable for ShadowCaster readback.");
                receiverMaterial = new Material(receiverShader);
                renderTexture = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBFloat);
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                SceneManager.MoveGameObjectToScene(receiver, scene);
                SceneManager.MoveGameObjectToScene(caster, scene);
                cameraObject.layer = fixtureLayer;
                lightObject.layer = fixtureLayer;
                receiver.layer = fixtureLayer;
                caster.layer = fixtureLayer;
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << fixtureLayer;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1.0f);
                camera.transform.position = new Vector3(0.0f, 3.0f, -7.0f);
                camera.transform.LookAt(new Vector3(0.0f, 0.5f, 0.0f));
                camera.fieldOfView = 45.0f;
                camera.targetTexture = renderTexture;
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.cullingMask = 1 << fixtureLayer;
                light.shadows = LightShadows.Hard;
                lightObject.transform.rotation = Quaternion.Euler(55.0f, -35.0f, 0.0f);
                receiver.transform.localScale = Vector3.one * 0.8f;
                receiver.GetComponent<MeshRenderer>().sharedMaterial = receiverMaterial;
                caster.transform.position = new Vector3(0.0f, 1.0f, 0.0f);
                MeshRenderer casterRenderer = caster.GetComponent<MeshRenderer>();
                casterRenderer.sharedMaterial = material;
                casterRenderer.shadowCastingMode = material.GetShaderPassEnabled("ShadowCaster")
                    ? ShadowCastingMode.ShadowsOnly
                    : ShadowCastingMode.Off;
                renderTexture.Create();
                light.shadows = LightShadows.None;
                camera.Render();
                Color[] withoutShadows = ReadPixels(renderTexture, texture);
                light.shadows = LightShadows.Hard;
                camera.Render();
                Color[] withShadows = ReadPixels(renderTexture, texture);
                return AnalyzeShadowReadback(withoutShadows, withShadows);
            }
            finally
            {
                Camera camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
                if (camera != null)
                    camera.targetTexture = null;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (receiverMaterial != null)
                    UnityEngine.Object.DestroyImmediate(receiverMaterial);
                if (caster != null)
                    UnityEngine.Object.DestroyImmediate(caster);
                if (receiver != null)
                    UnityEngine.Object.DestroyImmediate(receiver);
                if (lightObject != null)
                    UnityEngine.Object.DestroyImmediate(lightObject);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Renders the actual Meta pass into a linear target and returns its center pixel without changing persistent assets.</summary>
        /// <param name="material">The configured source material.</param>
        /// <returns>The linear Meta center readback.</returns>
        private static Color RenderMetaCenterPixel(Material material)
        {
            GameObject cameraObject = null;
            GameObject quadObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            Vector4 originalVertexControl = Shader.GetGlobalVector("unity_MetaVertexControl");
            Vector4 originalFragmentControl = Shader.GetGlobalVector("unity_MetaFragmentControl");
            Vector4 originalLightmapSt = Shader.GetGlobalVector("unity_LightmapST");
            float originalOutputBoost = Shader.GetGlobalFloat("unity_OneOverOutputBoost");
            float originalMaxOutput = Shader.GetGlobalFloat("unity_MaxOutputValue");
            CommandBuffer commandBuffer = null;
            try
            {
                cameraObject = new GameObject("PureBaseRenderingModeMetaCamera");
                quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                renderTexture = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBFloat);
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
                commandBuffer = new CommandBuffer { name = "PureBase Rendering Mode Meta Readback" };
                int pass = material.FindPass("Meta");
                Assert.That(pass, Is.GreaterThanOrEqualTo(0), "The material must expose an actual Meta pass.");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 0;
                camera.orthographic = true;
                camera.orthographicSize = 1.0f;
                camera.transform.position = new Vector3(0.0f, 0.0f, -5.0f);
                camera.targetTexture = renderTexture;
                renderTexture.Create();
                Shader.SetGlobalVector("unity_MetaVertexControl", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                Shader.SetGlobalVector("unity_MetaFragmentControl", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
                Shader.SetGlobalVector("unity_LightmapST", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
                Shader.SetGlobalFloat("unity_OneOverOutputBoost", 1.0f);
                Shader.SetGlobalFloat("unity_MaxOutputValue", 1.0f);
                commandBuffer.SetRenderTarget(renderTexture);
                commandBuffer.ClearRenderTarget(true, true, Color.clear);
                if (material.GetShaderPassEnabled("Meta"))
                    commandBuffer.DrawMesh(quadObject.GetComponent<MeshFilter>().sharedMesh, Matrix4x4.identity, material, 0, pass);
                camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                camera.Render();
                return ReadCenterPixel(renderTexture, texture);
            }
            finally
            {
                Shader.SetGlobalVector("unity_MetaVertexControl", originalVertexControl);
                Shader.SetGlobalVector("unity_MetaFragmentControl", originalFragmentControl);
                Shader.SetGlobalVector("unity_LightmapST", originalLightmapSt);
                Shader.SetGlobalFloat("unity_OneOverOutputBoost", originalOutputBoost);
                Shader.SetGlobalFloat("unity_MaxOutputValue", originalMaxOutput);
                Camera camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
                if (camera != null && commandBuffer != null)
                    camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                if (commandBuffer != null)
                    commandBuffer.Release();
                if (camera != null)
                    camera.targetTexture = null;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (quadObject != null)
                    UnityEngine.Object.DestroyImmediate(quadObject);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>Reads the center pixel of an already-rendered target without changing the active render target after completion.</summary>
        /// <param name="renderTexture">The source render target.</param>
        /// <param name="texture">The transient readback texture.</param>
        /// <returns>The center pixel.</returns>
        private static Color ReadCenterPixel(RenderTexture renderTexture, Texture2D texture)
        {
            ReadPixels(renderTexture, texture);
            return texture.GetPixel(RenderSize / 2, RenderSize / 2);
        }

        /// <summary>Reads every pixel from one target while restoring the previous active render target.</summary>
        /// <param name="renderTexture">The source render target.</param>
        /// <param name="texture">The transient readback texture.</param>
        /// <returns>The copied target pixels.</returns>
        private static Color[] ReadPixels(RenderTexture renderTexture, Texture2D texture)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0);
                texture.Apply(false, false);
                return texture.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        /// <summary>Measures the maximum RGB delta and changed-pixel count caused by directional shadows.</summary>
        /// <param name="withoutShadows">The unshadowed readback pixels.</param>
        /// <param name="withShadows">The shadowed readback pixels.</param>
        /// <returns>The measured directional-shadow silhouette.</returns>
        private static ShadowReadback AnalyzeShadowReadback(Color[] withoutShadows, Color[] withShadows)
        {
            Assert.That(withShadows.Length, Is.EqualTo(withoutShadows.Length), "Directional-shadow readbacks must have matching dimensions.");
            var changedPixelCount = 0;
            var maxAbsoluteRgbDelta = 0.0f;
            for (int index = 0; index < withoutShadows.Length; index++)
            {
                Color delta = withoutShadows[index] - withShadows[index];
                float maximumAbsoluteDelta = Mathf.Max(
                    Mathf.Abs(delta.r),
                    Mathf.Max(Mathf.Abs(delta.g), Mathf.Abs(delta.b))
                );
                if (maximumAbsoluteDelta > ShadowPixelNoiseThreshold)
                    changedPixelCount++;
                if (maximumAbsoluteDelta > maxAbsoluteRgbDelta)
                    maxAbsoluteRgbDelta = maximumAbsoluteDelta;
            }

            return new ShadowReadback(maxAbsoluteRgbDelta, changedPixelCount);
        }

        /// <summary>Renders Transparent Toon with a controlled one- or two-directional-light setup and a nonzero-alpha destination.</summary>
        /// <param name="material">The configured Transparent Toon material.</param>
        /// <param name="lightCount">The number of directional lights to render.</param>
        /// <returns>The center pixel after BIRP ForwardBase and ForwardAdd work.</returns>
        private static Color RenderTransparentToonPixel(Material material, int lightCount)
        {
            const int RenderingLayer = 31;
            int cullingMask = 1 << RenderingLayer;
            GameObject cameraObject = null;
            GameObject quadObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            var lightObjects = new System.Collections.Generic.List<GameObject>();
            Camera camera = null;
            try
            {
                cameraObject = new GameObject("PureBaseRenderingModeToonCamera");
                quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                renderTexture = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBFloat);
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
                camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 0.5f;
                camera.cullingMask = cullingMask;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.6f);
                camera.targetTexture = renderTexture;
                quadObject.layer = RenderingLayer;
                quadObject.GetComponent<Renderer>().sharedMaterial = material;
                for (int index = 0; index < lightCount; index++)
                {
                    var lightObject = new GameObject("PureBaseRenderingModeToonLight" + index);
                    lightObjects.Add(lightObject);
                    lightObject.layer = RenderingLayer;
                    var light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.color = Color.white;
                    light.intensity = 1.0f;
                    light.cullingMask = cullingMask;
                    lightObject.transform.rotation = Quaternion.Euler(30.0f, index == 0 ? -30.0f : 30.0f, 0.0f);
                }

                camera.Render();
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = renderTexture;
                    texture.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0);
                    texture.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                return texture.GetPixel(RenderSize / 2, RenderSize / 2);
            }
            finally
            {
                foreach (GameObject lightObject in lightObjects)
                    UnityEngine.Object.DestroyImmediate(lightObject);
                if (camera != null)
                    camera.targetTexture = null;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (quadObject != null)
                    UnityEngine.Object.DestroyImmediate(quadObject);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        /// <summary>Returns the Euclidean magnitude of a color's RGB channels.</summary>
        /// <param name="color">The color to measure.</param>
        /// <returns>The nonnegative RGB magnitude.</returns>
        private static float RgbMagnitude(Color color)
        {
            return Mathf.Sqrt(color.r * color.r + color.g * color.g + color.b * color.b);
        }

        /// <summary>Asserts that each color component is finite.</summary>
        /// <param name="color">The observed color.</param>
        /// <param name="label">The observation label.</param>
        private static void AssertFinite(Color color, string label)
        {
            Assert.That(float.IsNaN(color.r) || float.IsInfinity(color.r), Is.False, label + " red is non-finite.");
            Assert.That(float.IsNaN(color.g) || float.IsInfinity(color.g), Is.False, label + " green is non-finite.");
            Assert.That(float.IsNaN(color.b) || float.IsInfinity(color.b), Is.False, label + " blue is non-finite.");
            Assert.That(float.IsNaN(color.a) || float.IsInfinity(color.a), Is.False, label + " alpha is non-finite.");
        }

        /// <summary>Asserts that one scalar readback metric is finite.</summary>
        /// <param name="value">The observed scalar value.</param>
        /// <param name="label">The observation label.</param>
        private static void AssertFinite(float value, string label)
        {
            Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False, label + " is non-finite.");
        }

        /// <summary>Requires one imported public shader with no compiler errors.</summary>
        /// <param name="shaderName">The public shader name.</param>
        /// <returns>The imported shader.</returns>
        private static Shader RequireProductShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, "Product shader '" + shaderName + "' was not imported.");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, "Product shader '" + shaderName + "' has compiler errors.");
            return shader;
        }

        /// <summary>Requires the public material property before performing a mode observation.</summary>
        /// <param name="material">The material to inspect.</param>
        private static void RequireRenderingModeProperty(Material material)
        {
            Assert.That(material.HasProperty("_RenderingMode"), Is.True, "Rendering observations require the public _RenderingMode property.");
        }

        /// <summary>Loads one generated product source subasset without modifying its import state.</summary>
        /// <param name="shaderName">The product shader name.</param>
        /// <returns>The generated source text.</returns>
        private static string LoadProductSource(string shaderName)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Packages/jp.penguin.purebase/Shaders" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null || !string.Equals(shader.name, shaderName, StringComparison.Ordinal))
                    continue;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var source = asset as TextAsset;
                    if (source != null && source.name == "Shader Source")
                        return source.text;
                }
            }

            Assert.Fail("Generated source for product shader '" + shaderName + "' was unavailable.");
            return null;
        }

        /// <summary>Finds a loaded type without adding a compile-time dependency on the future Editor assembly.</summary>
        /// <param name="fullName">The fully-qualified type name.</param>
        /// <returns>The loaded type, or <see langword="null"/>.</returns>
        private static Type FindLoadedType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        /// <summary>Returns one required marker index with a diagnostic that keeps source-order failures local.</summary>
        /// <param name="source">The source text to inspect.</param>
        /// <param name="marker">The required marker.</param>
        /// <returns>The marker index.</returns>
        private static int RequireIndex(string source, string marker)
        {
            int index = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Required source marker '" + marker + "' was absent.");
            return index;
        }

        /// <summary>Stores the measured silhouette caused by one actual ShadowCaster render.</summary>
        private sealed class ShadowReadback
        {
            /// <summary>Initializes one immutable ShadowCaster measurement.</summary>
            /// <param name="maxAbsoluteRgbDelta">The largest RGB difference between unshadowed and shadowed receiver pixels.</param>
            /// <param name="changedPixelCount">The number of receiver pixels changed beyond the noise threshold.</param>
            public ShadowReadback(float maxAbsoluteRgbDelta, int changedPixelCount)
            {
                this.maxAbsoluteRgbDelta = maxAbsoluteRgbDelta;
                this.changedPixelCount = changedPixelCount;
            }

            /// <summary>Stores the largest RGB difference between unshadowed and shadowed receiver pixels.</summary>
            public readonly float maxAbsoluteRgbDelta;

            /// <summary>Stores the number of receiver pixels changed beyond the noise threshold.</summary>
            public readonly int changedPixelCount;

            /// <summary>Formats the shadow measurement for assertion diagnostics.</summary>
            /// <param name="label">The mode label associated with this measurement.</param>
            /// <returns>The formatted measurement.</returns>
            public string Describe(string label) =>
                label + ": maxAbsoluteRgbDelta=" + maxAbsoluteRgbDelta + ", changedPixels=" + changedPixelCount;
        }
    }
}
