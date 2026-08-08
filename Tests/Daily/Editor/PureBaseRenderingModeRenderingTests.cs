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
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines focused BIRP rendering-mode observations without changing canonical scenes or baselines.</summary>
    public sealed partial class PureBaseRenderingModeRenderingTests
    {
        /// <summary>Tracks transient materials so rendering observations release every native Unity object they allocate.</summary>
        private readonly List<Material> transientMaterials = new List<Material>();

        /// <summary>Defines the small readback dimension used by transient numeric observations.</summary>
        private const int RenderSize = 64;

        /// <summary>Defines the largest per-channel readback difference treated as directional-shadow noise.</summary>
        private const float ShadowPixelNoiseThreshold = 0.002f;

        /// <summary>Defines the minimum changed-pixel count required for a meaningful directional-shadow silhouette.</summary>
        private const int MinimumShadowSilhouettePixelCount = 32;

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
                AssertShadowContributions(opaque, cutout, cutoutBelow, transparent);
                AssertMetaContributions(opaque, cutout, transparent, contributingBaseColor.linear);
            }
        }

        /// <summary>Asserts ShadowCaster enablement and measured contribution boundaries for all rendering modes.</summary>
        private static void AssertShadowContributions(Material opaque, Material cutout, Material cutoutBelow, Material transparent)
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
            AssertContributingShadowReadbacks(opaqueShadow, cutoutShadow);
            AssertNoncontributingShadowReadbacks(opaqueShadow, cutoutShadow, cutoutBelowShadow, transparentShadow);
        }

        /// <summary>Asserts that Opaque and Cutout ShadowCaster measurements retain meaningful silhouettes.</summary>
        private static void AssertContributingShadowReadbacks(ShadowReadback opaqueShadow, ShadowReadback cutoutShadow)
        {
            Assert.That(opaqueShadow.maxAbsoluteRgbDelta, Is.GreaterThan(ShadowPixelNoiseThreshold), opaqueShadow.Describe("Opaque"));
            Assert.That(opaqueShadow.changedPixelCount, Is.GreaterThan(MinimumShadowSilhouettePixelCount), opaqueShadow.Describe("Opaque"));
            Assert.That(cutoutShadow.maxAbsoluteRgbDelta, Is.GreaterThan(ShadowPixelNoiseThreshold), cutoutShadow.Describe("Cutout"));
            Assert.That(cutoutShadow.changedPixelCount, Is.GreaterThan(MinimumShadowSilhouettePixelCount), cutoutShadow.Describe("Cutout"));
            Assert.That(cutoutShadow.maxAbsoluteRgbDelta, Is.GreaterThan(opaqueShadow.maxAbsoluteRgbDelta * 0.25f), cutoutShadow.Describe("Cutout") + " must retain a visible silhouette relative to Opaque.");
            Assert.That(cutoutShadow.changedPixelCount, Is.GreaterThan(opaqueShadow.changedPixelCount * 0.25f), cutoutShadow.Describe("Cutout") + " must retain sufficient changed pixels relative to Opaque.");
        }

        /// <summary>Asserts that below-cutoff and Transparent ShadowCaster measurements remain noncontributing.</summary>
        private static void AssertNoncontributingShadowReadbacks(ShadowReadback opaqueShadow, ShadowReadback cutoutShadow, ShadowReadback cutoutBelowShadow, ShadowReadback transparentShadow)
        {
            Assert.That(cutoutBelowShadow.maxAbsoluteRgbDelta, Is.LessThanOrEqualTo(ShadowPixelNoiseThreshold), cutoutBelowShadow.Describe("Cutout below cutoff"));
            Assert.That(cutoutBelowShadow.changedPixelCount, Is.LessThanOrEqualTo(MinimumShadowSilhouettePixelCount), cutoutBelowShadow.Describe("Cutout below cutoff"));
            Assert.That(transparentShadow.maxAbsoluteRgbDelta, Is.LessThanOrEqualTo(ShadowPixelNoiseThreshold), transparentShadow.Describe("Transparent"));
            Assert.That(transparentShadow.changedPixelCount, Is.LessThanOrEqualTo(MinimumShadowSilhouettePixelCount), transparentShadow.Describe("Transparent"));
            float minimumContributingShadowDelta = Mathf.Min(opaqueShadow.maxAbsoluteRgbDelta, cutoutShadow.maxAbsoluteRgbDelta);
            int minimumContributingShadowPixels = Mathf.Min(opaqueShadow.changedPixelCount, cutoutShadow.changedPixelCount);
            Assert.That(transparentShadow.maxAbsoluteRgbDelta, Is.LessThan(minimumContributingShadowDelta * 0.25f), transparentShadow.Describe("Transparent") + " must remain below the Opaque and Cutout contribution boundary.");
            Assert.That(transparentShadow.changedPixelCount, Is.LessThan(minimumContributingShadowPixels * 0.25f), transparentShadow.Describe("Transparent") + " must remain below the Opaque and Cutout changed-pixel contribution boundary.");
        }

        /// <summary>Asserts Meta readback contribution boundaries for Opaque, Cutout, and Transparent materials.</summary>
        private static void AssertMetaContributions(Material opaque, Material cutout, Material transparent, Color expectedContributingMeta)
        {
            float opaqueMetaMagnitude = AssertContributingMeta(RenderMetaCenterPixel(opaque), expectedContributingMeta, "Opaque");
            float cutoutMetaMagnitude = AssertContributingMeta(RenderMetaCenterPixel(cutout), expectedContributingMeta, "Cutout");
            Color transparentMeta = RenderMetaCenterPixel(transparent);
            AssertFinite(transparentMeta, "Transparent Meta readback");
            float transparentMetaMagnitude = RgbMagnitude(transparentMeta);
            Assert.That(transparentMetaMagnitude, Is.LessThan(0.02f), "Transparent Meta must not contribute effective albedo data in the actual BIRP readback.");
            float minimumContributingMetaMagnitude = Mathf.Min(opaqueMetaMagnitude, cutoutMetaMagnitude);
            Assert.That(transparentMetaMagnitude, Is.LessThan(minimumContributingMetaMagnitude * 0.25f), "Transparent Meta must remain below the Opaque and Cutout contribution boundary.");
        }

        /// <summary>Asserts one Meta contribution's expected linear albedo and returns its RGB magnitude.</summary>
        private static float AssertContributingMeta(Color observedMeta, Color expectedMeta, string label)
        {
            AssertFinite(observedMeta, label + " Meta readback");
            Assert.That(observedMeta.r, Is.EqualTo(expectedMeta.r).Within(0.08f));
            Assert.That(observedMeta.g, Is.EqualTo(expectedMeta.g).Within(0.08f));
            Assert.That(observedMeta.b, Is.EqualTo(expectedMeta.b).Within(0.08f));
            float magnitude = RgbMagnitude(observedMeta);
            Assert.That(magnitude, Is.GreaterThan(0.2f), label + " Meta pass must contribute non-clear albedo data.");
            return magnitude;
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
                ConfigureCenterPixelCamera(camera, renderTexture, background);
                quadObject.GetComponent<Renderer>().sharedMaterial = material;
                camera.Render();
                return ReadCenterPixel(renderTexture, texture);
            }
            finally
            {
                ReleaseQuadReadbackResources(cameraObject, quadObject, camera, renderTexture, texture);
            }
        }

        /// <summary>Configures the temporary camera used for one center-pixel readback.</summary>
        private static void ConfigureCenterPixelCamera(Camera camera, RenderTexture renderTexture, Color background)
        {
            camera.orthographic = true;
            camera.orthographicSize = 0.5f;
            camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.targetTexture = renderTexture;
        }

        /// <summary>Releases one temporary quad readback fixture in its original ownership order.</summary>
        private static void ReleaseQuadReadbackResources(GameObject cameraObject, GameObject quadObject, Camera camera, RenderTexture renderTexture, Texture2D texture)
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
                ConfigureExplicitDepthProbeCamera(camera, renderTexture);
                renderTexture.Create();
                commandBuffer = CreateExplicitDepthProbeCommandBuffer(quadObject, transparentMaterial, markerMaterial);
                camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                camera.Render();
                return ReadCenterPixel(renderTexture, texture);
            }
            finally
            {
                ReleaseExplicitDepthProbeResources(cameraObject, quadObject, camera, commandBuffer, renderTexture, texture);
            }
        }

        /// <summary>Configures the camera used by the explicit ForwardBase depth probe.</summary>
        private static void ConfigureExplicitDepthProbeCamera(Camera camera, RenderTexture renderTexture)
        {
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.orthographic = true;
            camera.orthographicSize = 0.5f;
            camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.targetTexture = renderTexture;
        }

        /// <summary>Creates the command buffer that draws Transparent before the farther opaque marker.</summary>
        private static CommandBuffer CreateExplicitDepthProbeCommandBuffer(GameObject quadObject, Material transparentMaterial, Material markerMaterial)
        {
            int transparentPass = transparentMaterial.FindPass("ForwardBase");
            Assert.That(transparentPass, Is.GreaterThanOrEqualTo(0), "The Transparent depth probe requires ForwardBase.");
            var commandBuffer = new CommandBuffer { name = "PureBase Rendering Mode Explicit Depth Probe" };
            Mesh quadMesh = quadObject.GetComponent<MeshFilter>().sharedMesh;
            commandBuffer.DrawMesh(quadMesh, Matrix4x4.identity, transparentMaterial, 0, transparentPass);
            commandBuffer.DrawMesh(quadMesh, Matrix4x4.Translate(new Vector3(0.0f, 0.0f, 0.1f)), markerMaterial, 0, 0);
            return commandBuffer;
        }

        /// <summary>Releases the explicit depth probe command buffer and transient render resources.</summary>
        private static void ReleaseExplicitDepthProbeResources(GameObject cameraObject, GameObject quadObject, Camera camera, CommandBuffer commandBuffer, RenderTexture renderTexture, Texture2D texture)
        {
            if (camera != null && commandBuffer != null)
                camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
            if (commandBuffer != null)
                commandBuffer.Release();
            ReleaseQuadReadbackResources(cameraObject, quadObject, camera, renderTexture, texture);
        }

        /// <summary>Renders Transparent Toon with a controlled one- or two-directional-light setup and a nonzero-alpha destination.</summary>
        /// <param name="material">The configured Transparent Toon material.</param>
        /// <param name="lightCount">The number of directional lights to render.</param>
        /// <returns>The center pixel after BIRP ForwardBase and ForwardAdd work.</returns>
        private static Color RenderTransparentToonPixel(Material material, int lightCount)
        {
            const int renderingLayer = 31;
            int cullingMask = 1 << renderingLayer;
            GameObject cameraObject = null;
            GameObject quadObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            var lightObjects = new List<GameObject>();
            Camera camera = null;
            try
            {
                cameraObject = new GameObject("PureBaseRenderingModeToonCamera");
                quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                renderTexture = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBFloat);
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
                camera = cameraObject.AddComponent<Camera>();
                ConfigureTransparentToonCamera(camera, renderTexture, cullingMask);
                quadObject.layer = renderingLayer;
                quadObject.GetComponent<Renderer>().sharedMaterial = material;
                CreateTransparentToonLights(lightObjects, lightCount, renderingLayer, cullingMask);
                camera.Render();
                return ReadCenterPixel(renderTexture, texture);
            }
            finally
            {
                ReleaseTransparentToonResources(lightObjects, cameraObject, quadObject, camera, renderTexture, texture);
            }
        }

        /// <summary>Configures the temporary camera used for Transparent Toon light accumulation.</summary>
        private static void ConfigureTransparentToonCamera(Camera camera, RenderTexture renderTexture, int cullingMask)
        {
            camera.orthographic = true;
            camera.orthographicSize = 0.5f;
            camera.cullingMask = cullingMask;
            camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.6f);
            camera.targetTexture = renderTexture;
        }

        /// <summary>Creates the directional lights used to isolate ForwardAdd alpha behavior.</summary>
        private static void CreateTransparentToonLights(List<GameObject> lightObjects, int lightCount, int renderingLayer, int cullingMask)
        {
            for (int index = 0; index < lightCount; index++)
            {
                var lightObject = new GameObject("PureBaseRenderingModeToonLight" + index);
                lightObjects.Add(lightObject);
                lightObject.layer = renderingLayer;
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1.0f;
                light.cullingMask = cullingMask;
                lightObject.transform.rotation = Quaternion.Euler(30.0f, index == 0 ? -30.0f : 30.0f, 0.0f);
            }
        }

        /// <summary>Releases Transparent Toon lights and temporary render resources in their original order.</summary>
        private static void ReleaseTransparentToonResources(List<GameObject> lightObjects, GameObject cameraObject, GameObject quadObject, Camera camera, RenderTexture renderTexture, Texture2D texture)
        {
            foreach (GameObject lightObject in lightObjects)
                UnityEngine.Object.DestroyImmediate(lightObject);
            ReleaseQuadReadbackResources(cameraObject, quadObject, camera, renderTexture, texture);
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
