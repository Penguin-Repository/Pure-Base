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

// Defines explicit D24S8 runtime observations for product shader Stencil pass behavior.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    public sealed partial class PureBaseRenderingModeRenderingTests
    {
        /// <summary>Requires default Always and Keep Stencil state to render independently of the cleared Stencil value.</summary>
        [Test]
        public void D24S8StencilDefaultsRenderIndependentlyOfClearStencilAcrossProductShaders()
        {
            foreach (string shaderName in ProductShaderNames)
            {
                var fixture = new D24S8StencilFixture();
                try
                {
                    fixture.Initialize();
                    Shader shader = RequireProductShader(shaderName);
                    Color zeroClear = fixture.RenderSingle(
                        shader,
                        new Color(0.8f, 0.6f, 0.4f, 1.0f),
                        0,
                        new StencilState(37, 255, 255, CompareFunction.Always, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep),
                        0
                    );
                    Color nonzeroClear = fixture.RenderSingle(
                        shader,
                        new Color(0.8f, 0.6f, 0.4f, 1.0f),
                        203,
                        new StencilState(37, 255, 255, CompareFunction.Always, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep),
                        0
                    );

                    AssertFinite(zeroClear, shaderName + " default-Always clear-0 " + fixture.FormatDescription);
                    AssertFinite(nonzeroClear, shaderName + " default-Always clear-203 " + fixture.FormatDescription);
                    Assert.That(RgbMagnitude(zeroClear), Is.GreaterThan(0.05f), shaderName + " default Always+Keep must draw over clear stencil 0. " + fixture.FormatDescription + " Pixel=" + zeroClear);
                    Assert.That(RgbMagnitude(nonzeroClear), Is.GreaterThan(0.05f), shaderName + " default Always+Keep must draw over clear stencil 203. " + fixture.FormatDescription + " Pixel=" + nonzeroClear);
                    Assert.That(RgbMagnitude(zeroClear - nonzeroClear), Is.LessThan(0.02f), shaderName + " default Always+Keep must not depend on the cleared stencil value. " + fixture.FormatDescription + " Clear0=" + zeroClear + " Clear203=" + nonzeroClear);
                }
                finally
                {
                    fixture.Dispose();
                }
            }
        }

        /// <summary>Requires ForwardBase Replace to make matching Equal readers pass and mismatched readers reject across product shaders.</summary>
        [Test]
        public void D24S8StencilForwardBaseReplaceControlsEqualAndMismatchedReadersAcrossProductShaders()
        {
            var writerState = new StencilState(60, 255, 255, CompareFunction.Always, StencilOp.Replace, StencilOp.Keep, StencilOp.Keep);
            var matchingReaderState = new StencilState(60, 255, 0, CompareFunction.Equal, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            var mismatchedReaderState = new StencilState(61, 255, 0, CompareFunction.Equal, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            foreach (string shaderName in ProductShaderNames)
            {
                var fixture = new D24S8StencilFixture();
                try
                {
                    fixture.Initialize();
                    Shader shader = RequireProductShader(shaderName);
                    Color matching = fixture.RenderWriterThenReader(
                        shader,
                        0,
                        Color.black,
                        writerState,
                        new Color(0.7f, 0.8f, 0.9f, 1.0f),
                        matchingReaderState,
                        0
                    );
                    Color mismatched = fixture.RenderWriterThenReader(
                        shader,
                        0,
                        Color.black,
                        writerState,
                        new Color(0.7f, 0.8f, 0.9f, 1.0f),
                        mismatchedReaderState,
                        0
                    );

                    AssertFinite(matching, shaderName + " Replace/Equal matching reader " + fixture.FormatDescription);
                    AssertFinite(mismatched, shaderName + " Replace/Equal mismatched reader " + fixture.FormatDescription);
                    Assert.That(RgbMagnitude(matching), Is.GreaterThan(0.05f), shaderName + " Replace writer followed by Equal reader with ref 60 must render. " + fixture.FormatDescription + " Pixel=" + matching);
                    Assert.That(RgbMagnitude(mismatched), Is.LessThan(RgbMagnitude(matching) * 0.2f), shaderName + " Replace writer followed by Equal reader with ref 61 must reject. " + fixture.FormatDescription + " Matching=" + matching + " Mismatched=" + mismatched);
                }
                finally
                {
                    fixture.Dispose();
                }
            }
        }

        /// <summary>Requires partial Stencil WriteMask and ReadMask behavior to preserve and compare only the selected low bits.</summary>
        [Test]
        public void D24S8StencilHonorsPartialReadAndWriteMasksAcrossProductShaders()
        {
            var writerState = new StencilState(0x12, 255, 0x0f, CompareFunction.Always, StencilOp.Replace, StencilOp.Keep, StencilOp.Keep);
            var matchingReaderState = new StencilState(0xf2, 0x0f, 0, CompareFunction.Equal, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            var mismatchedReaderState = new StencilState(0xf1, 0x0f, 0, CompareFunction.Equal, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            foreach (string shaderName in ProductShaderNames)
            {
                var fixture = new D24S8StencilFixture();
                try
                {
                    fixture.Initialize();
                    Shader shader = RequireProductShader(shaderName);
                    Color matching = fixture.RenderWriterThenReader(
                        shader,
                        0xa0,
                        Color.black,
                        writerState,
                        new Color(0.9f, 0.7f, 0.5f, 1.0f),
                        matchingReaderState,
                        0
                    );
                    Color mismatched = fixture.RenderWriterThenReader(
                        shader,
                        0xa0,
                        Color.black,
                        writerState,
                        new Color(0.9f, 0.7f, 0.5f, 1.0f),
                        mismatchedReaderState,
                        0
                    );

                    AssertFinite(matching, shaderName + " partial-mask matching reader " + fixture.FormatDescription);
                    AssertFinite(mismatched, shaderName + " partial-mask mismatched reader " + fixture.FormatDescription);
                    Assert.That(RgbMagnitude(matching), Is.GreaterThan(0.05f), shaderName + " WriteMask 0x0f must write low bits that ReadMask 0x0f can match. " + fixture.FormatDescription + " Pixel=" + matching);
                    Assert.That(RgbMagnitude(mismatched), Is.LessThan(RgbMagnitude(matching) * 0.2f), shaderName + " ReadMask 0x0f must reject a mismatched low-bit reference. " + fixture.FormatDescription + " Matching=" + matching + " Mismatched=" + mismatched);
                }
                finally
                {
                    fixture.Dispose();
                }
            }
        }

        /// <summary>Requires Toon ForwardAdd to retain Equal+Keep contribution and reject add contribution after NotEqual+Replace mutates ForwardBase stencil.</summary>
        [Test]
        public void D24S8StencilToonForwardAddRecomparesPostBaseStencilWithoutWriting()
        {
            Shader toon = RequireProductShader("PureBase/Toon");
            var equalKeep = new StencilState(71, 255, 255, CompareFunction.Equal, StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            var notEqualReplace = new StencilState(72, 255, 255, CompareFunction.NotEqual, StencilOp.Replace, StencilOp.Keep, StencilOp.Keep);
            var fixture = new D24S8StencilFixture();
            try
            {
                fixture.Initialize();
                Color equalKeepOneLight = fixture.RenderToonComposite(toon, 71, equalKeep, 1);
                Color equalKeepTwoLights = fixture.RenderToonComposite(toon, 71, equalKeep, 2);
                Color notEqualReplaceOneLight = fixture.RenderToonComposite(toon, 71, notEqualReplace, 1);
                Color notEqualReplaceTwoLights = fixture.RenderToonComposite(toon, 71, notEqualReplace, 2);
                float retainedAddDelta = RgbMagnitude(equalKeepTwoLights - equalKeepOneLight);
                float rejectedAddDelta = RgbMagnitude(notEqualReplaceTwoLights - notEqualReplaceOneLight);

                AssertFinite(equalKeepOneLight, "Toon Equal+Keep one-light " + fixture.FormatDescription);
                AssertFinite(equalKeepTwoLights, "Toon Equal+Keep two-light " + fixture.FormatDescription);
                AssertFinite(notEqualReplaceOneLight, "Toon NotEqual+Replace one-light " + fixture.FormatDescription);
                AssertFinite(notEqualReplaceTwoLights, "Toon NotEqual+Replace two-light " + fixture.FormatDescription);
                Assert.That(retainedAddDelta, Is.GreaterThan(0.01f), "Toon two-light control must expose a measurable ForwardAdd contribution before testing Stencil recompare. " + fixture.FormatDescription + " OneLight=" + equalKeepOneLight + " TwoLights=" + equalKeepTwoLights + " Delta=" + retainedAddDelta);
                Assert.That(rejectedAddDelta, Is.LessThan(retainedAddDelta * 0.2f), "Toon NotEqual+Replace must make ForwardAdd recompare the post-ForwardBase value and reject its add contribution. " + fixture.FormatDescription + " OneLight=" + notEqualReplaceOneLight + " TwoLights=" + notEqualReplaceTwoLights + " RetainedDelta=" + retainedAddDelta + " RejectedDelta=" + rejectedAddDelta);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        /// <summary>Stores all user-controlled Stencil state values for one material draw.</summary>
        private sealed class StencilState
        {
            /// <summary>Initializes one complete material Stencil state.</summary>
            /// <param name="referenceValue">The Stencil reference value.</param>
            /// <param name="readMask">The Stencil read mask.</param>
            /// <param name="writeMask">The Stencil write mask.</param>
            /// <param name="comparison">The Stencil comparison function.</param>
            /// <param name="passOperation">The operation when Stencil and depth tests pass.</param>
            /// <param name="failOperation">The operation when the Stencil test fails.</param>
            /// <param name="depthFailOperation">The operation when the depth test fails.</param>
            public StencilState(byte referenceValue, byte readMask, byte writeMask, CompareFunction comparison, StencilOp passOperation, StencilOp failOperation, StencilOp depthFailOperation)
            {
                this.referenceValue = referenceValue;
                this.readMask = readMask;
                this.writeMask = writeMask;
                this.comparison = comparison;
                this.passOperation = passOperation;
                this.failOperation = failOperation;
                this.depthFailOperation = depthFailOperation;
            }

            /// <summary>Stores the reference value used by Stencil comparison and writes.</summary>
            public readonly byte referenceValue;

            /// <summary>Stores the mask applied before Stencil comparison.</summary>
            public readonly byte readMask;

            /// <summary>Stores the mask applied to a Stencil write operation.</summary>
            public readonly byte writeMask;

            /// <summary>Stores the Stencil comparison function.</summary>
            public readonly CompareFunction comparison;

            /// <summary>Stores the Stencil operation when tests pass.</summary>
            public readonly StencilOp passOperation;

            /// <summary>Stores the Stencil operation when the Stencil test fails.</summary>
            public readonly StencilOp failOperation;

            /// <summary>Stores the Stencil operation when the depth test fails.</summary>
            public readonly StencilOp depthFailOperation;
        }

        /// <summary>Owns an isolated D3D11 D24S8 camera, readback target, mesh, material, command-buffer, and light fixture.</summary>
        private sealed class D24S8StencilFixture : IDisposable
        {
            /// <summary>Identifies the isolated rendering layer used by the temporary fixture.</summary>
            private const int FixtureLayer = 31;

            /// <summary>Identifies the imported RenderTexture asset that prevents compatible-format promotion.</summary>
            private const string RenderTextureAssetPath = "Packages/jp.penguin.purebase/Tests/Daily/Editor/PureBaseD24S8StencilTarget.renderTexture";

            /// <summary>Stores the isolated preview scene that prevents unrelated scene content from affecting readback.</summary>
            private Scene scene;

            /// <summary>Stores the command buffer used to explicitly clear color, depth, and Stencil before every observation.</summary>
            private CommandBuffer commandBuffer;

            /// <summary>Stores the temporary camera GameObject.</summary>
            private GameObject cameraObject;

            /// <summary>Stores the imported D24S8 render target asset whose GPU resource is owned only while this fixture runs.</summary>
            private RenderTexture renderTexture;

            /// <summary>Stores the temporary linear CPU readback texture.</summary>
            private Texture2D texture;

            /// <summary>Stores the temporary quad GameObject.</summary>
            private GameObject quadObject;

            /// <summary>Stores the temporary directional light GameObjects in creation order.</summary>
            private readonly List<GameObject> lightObjects = new List<GameObject>();

            /// <summary>Stores the temporary materials in creation order.</summary>
            private readonly List<Material> materials = new List<Material>();

            /// <summary>Gets format and device diagnostics for every product-behavior assertion.</summary>
            public string FormatDescription
            {
                get
                {
                    return "Device=" + SystemInfo.graphicsDeviceType +
                        " AssetPath=" + RenderTextureAssetPath +
                        " RequestedColor=" + (renderTexture == null ? "<unloaded>" : renderTexture.descriptor.graphicsFormat.ToString()) +
                        " RequestedDepthStencil=" + GraphicsFormat.D24_UNorm_S8_UInt +
                        " ActualColor=" + (renderTexture == null ? "<unallocated>" : renderTexture.graphicsFormat.ToString()) +
                        " ActualDepthStencil=" + (renderTexture == null ? "<unallocated>" : renderTexture.depthStencilFormat.ToString()) +
                        " StencilBits=" + (renderTexture == null ? "<unallocated>" : GetStencilBitCount(renderTexture.depthStencilFormat).ToString()) +
                        " IsCreated=" + (renderTexture != null && renderTexture.IsCreated());
                }
            }

            /// <summary>Allocates an isolated D3D11 fixture around the imported D24S8 asset and verifies its exact attachment format.</summary>
            public void Initialize()
            {
                Assert.That(SystemInfo.graphicsDeviceType, Is.EqualTo(GraphicsDeviceType.Direct3D11), "The D24S8 Stencil fixture requires D3D11 and must not silently run on " + SystemInfo.graphicsDeviceType + ".");
                Assert.That(SystemInfo.IsFormatSupported(GraphicsFormat.D24_UNorm_S8_UInt, FormatUsage.Render), Is.True, "D3D11 must support D24_UNorm_S8_UInt as a render attachment for the Stencil fixture.");
                Assert.That(SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, FormatUsage.Render), Is.True, "D3D11 must support the fixture color attachment format.");

                renderTexture = LoadAndCreateD24S8RenderTextureAsset();

                scene = EditorSceneManager.NewPreviewScene();
                commandBuffer = new CommandBuffer { name = "PureBase D24S8 Stencil Clear" };

                cameraObject = new GameObject("PureBaseD24S8StencilCamera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBA32, false, true);
                quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                SceneManager.MoveGameObjectToScene(quadObject, scene);

                ConfigureCamera();
                ConfigureQuad();
                SetLightCount(1);
            }

            /// <summary>Renders one configured product material after an explicit color, depth, and Stencil clear.</summary>
            /// <param name="shader">The product shader under observation.</param>
            /// <param name="baseColor">The material base color.</param>
            /// <param name="clearStencil">The initial Stencil byte.</param>
            /// <param name="stencilState">The explicit material Stencil state.</param>
            /// <param name="renderingMode">The Pure-Base rendering mode to apply.</param>
            /// <returns>The center pixel after the product draw.</returns>
            public Color RenderSingle(Shader shader, Color baseColor, byte clearStencil, StencilState stencilState, int renderingMode)
            {
                Material material = CreateProductMaterial(shader, baseColor, stencilState, renderingMode, "single draw");
                ClearTarget(clearStencil);
                RenderMaterial(material);
                return ReadCenterPixel(renderTexture, texture);
            }

            /// <summary>Renders a Stencil writer and reader consecutively without clearing their shared depth-stencil attachment between draws.</summary>
            /// <param name="shader">The product shader under observation.</param>
            /// <param name="clearStencil">The initial Stencil byte.</param>
            /// <param name="writerColor">The writer material base color.</param>
            /// <param name="writerState">The writer material Stencil state.</param>
            /// <param name="readerColor">The reader material base color.</param>
            /// <param name="readerState">The reader material Stencil state.</param>
            /// <param name="renderingMode">The Pure-Base rendering mode to apply to both materials.</param>
            /// <returns>The center pixel after the reader draw.</returns>
            public Color RenderWriterThenReader(Shader shader, byte clearStencil, Color writerColor, StencilState writerState, Color readerColor, StencilState readerState, int renderingMode)
            {
                Material writer = CreateProductMaterial(shader, writerColor, writerState, renderingMode, "Stencil writer");
                Material reader = CreateProductMaterial(shader, readerColor, readerState, renderingMode, "Stencil reader");
                ClearTarget(clearStencil);
                RenderMaterial(writer);
                RenderMaterial(reader);
                return ReadCenterPixel(renderTexture, texture);
            }

            /// <summary>Renders Toon with a controlled number of directional lights to observe its ForwardBase and ForwardAdd composition.</summary>
            /// <param name="shader">The Toon product shader.</param>
            /// <param name="clearStencil">The initial Stencil byte.</param>
            /// <param name="stencilState">The material Stencil state.</param>
            /// <param name="lightCount">The number of directional lights to render.</param>
            /// <returns>The center pixel after the Toon composite.</returns>
            public Color RenderToonComposite(Shader shader, byte clearStencil, StencilState stencilState, int lightCount)
            {
                SetLightCount(lightCount);
                Material material = CreateProductMaterial(shader, new Color(0.8f, 0.6f, 0.4f, 0.5f), stencilState, 2, "Toon ForwardAdd composite");
                ClearTarget(clearStencil);
                RenderMaterial(material);
                return ReadCenterPixel(renderTexture, texture);
            }

            /// <summary>Releases every temporary resource in reverse creation order, including partial initialization after an exception.</summary>
            public void Dispose()
            {
                for (int index = materials.Count - 1; index >= 0; index--)
                    UnityEngine.Object.DestroyImmediate(materials[index]);
                materials.Clear();
                DestroyLights();
                if (quadObject != null)
                    UnityEngine.Object.DestroyImmediate(quadObject);
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                Camera camera = cameraObject == null ? null : cameraObject.GetComponent<Camera>();
                if (camera != null)
                    camera.targetTexture = null;
                if (renderTexture != null)
                    renderTexture.Release();
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                if (commandBuffer != null)
                    commandBuffer.Release();
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.ClosePreviewScene(scene);
            }

            /// <summary>Loads and validates the required non-fallback D24S8 asset before allocating its GPU resource.</summary>
            /// <returns>The exact tracked D24S8 asset with a created GPU resource.</returns>
            private static RenderTexture LoadAndCreateD24S8RenderTextureAsset()
            {
                RenderTexture target = AssetDatabase.LoadAssetAtPath<RenderTexture>(RenderTextureAssetPath);
                Assert.That(target, Is.Not.Null, "The D24S8 fixture asset must exist at '" + RenderTextureAssetPath + "'. This is fixture configuration, not product behavior.");
                AssertCompatibleFormatFallbackIsDisabled(target);

                RenderTextureDescriptor descriptor = target.descriptor;
                Assert.That(descriptor.graphicsFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm), "The D24S8 fixture asset descriptor must request R8G8B8A8_UNorm color without fallback. " + DescribeTarget(target));
                Assert.That(descriptor.depthStencilFormat, Is.EqualTo(GraphicsFormat.D24_UNorm_S8_UInt), "The D24S8 fixture asset descriptor must request D24_UNorm_S8_UInt depth-stencil without fallback. " + DescribeTarget(target));
                Assert.That(target.antiAliasing, Is.EqualTo(1), "The D24S8 fixture asset must use MSAA 1. " + DescribeTarget(target));
                Assert.That(descriptor.msaaSamples, Is.EqualTo(1), "The D24S8 fixture descriptor must use MSAA 1. " + DescribeTarget(target));
                Assert.That(target.sRGB, Is.False, "The D24S8 fixture asset must be linear and non-sRGB. " + DescribeTarget(target));
                Assert.That(descriptor.sRGB, Is.False, "The D24S8 fixture descriptor must be linear and non-sRGB. " + DescribeTarget(target));
                Assert.That(target.useMipMap, Is.False, "The D24S8 fixture asset must not use mipmaps. " + DescribeTarget(target));
                Assert.That(descriptor.useMipMap, Is.False, "The D24S8 fixture descriptor must not use mipmaps. " + DescribeTarget(target));
                Assert.That(target.useDynamicScale, Is.False, "The D24S8 fixture asset must not use dynamic scaling. " + DescribeTarget(target));
                Assert.That(descriptor.useDynamicScale, Is.False, "The D24S8 fixture descriptor must not use dynamic scaling. " + DescribeTarget(target));
                Assert.That(target.enableRandomWrite, Is.False, "The D24S8 fixture asset must not enable random writes. " + DescribeTarget(target));
                Assert.That(descriptor.enableRandomWrite, Is.False, "The D24S8 fixture descriptor must not enable random writes. " + DescribeTarget(target));

                try
                {
                    Assert.That(target.Create(), Is.True, "The configured D24S8 fixture asset must create its GPU resource without compatible-format fallback. " + DescribeTarget(target));
                    Assert.That(target.IsCreated(), Is.True, "The configured D24S8 fixture asset GPU resource must be created. " + DescribeTarget(target));
                    Assert.That(target.graphicsFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm), "The created D24S8 fixture asset must allocate exact R8G8B8A8_UNorm color. " + DescribeTarget(target));
                    Assert.That(target.depthStencilFormat, Is.EqualTo(GraphicsFormat.D24_UNorm_S8_UInt), "The created D24S8 fixture asset must allocate exact D24_UNorm_S8_UInt depth-stencil. " + DescribeTarget(target));
                    Assert.That(RenderTexture.SupportsStencil(target), Is.True, "The created D24S8 fixture asset must provide a Stencil attachment. " + DescribeTarget(target));
                    Assert.That(GetStencilBitCount(target.depthStencilFormat), Is.EqualTo(8), "The created D24S8 fixture asset must provide exactly eight Stencil bits. " + DescribeTarget(target));
                    return target;
                }
                catch
                {
                    target.Release();
                    throw;
                }
            }

            /// <summary>Uses the RenderTexture Inspector serialization to require disabled compatible-format fallback without modifying the asset.</summary>
            /// <param name="target">The imported RenderTexture asset.</param>
            private static void AssertCompatibleFormatFallbackIsDisabled(RenderTexture target)
            {
                using (var serializedTarget = new SerializedObject(target))
                {
                    serializedTarget.Update();
                    SerializedProperty compatibleFallback = serializedTarget.FindProperty("m_EnableCompatibleFormat");
                    Assert.That(compatibleFallback, Is.Not.Null, "The D24S8 fixture asset must expose m_EnableCompatibleFormat through SerializedObject inspection. " + DescribeTarget(target));
                    Assert.That(compatibleFallback.boolValue, Is.False, "The D24S8 fixture asset must disable compatible-format fallback. " + DescribeTarget(target));
                }
            }

            /// <summary>Returns the Stencil bit count implied by a verified exact D24S8 allocation format.</summary>
            /// <param name="depthStencilFormat">The actual RenderTexture depth-stencil attachment format.</param>
            /// <returns>Eight for exact D24S8; otherwise zero so fixture configuration failures remain explicit.</returns>
            private static int GetStencilBitCount(GraphicsFormat depthStencilFormat)
            {
                return depthStencilFormat == GraphicsFormat.D24_UNorm_S8_UInt ? 8 : 0;
            }

            /// <summary>Formats target diagnostics for fixture configuration and allocation failures.</summary>
            /// <param name="target">The target being inspected.</param>
            /// <returns>Inspectable asset, descriptor, and allocation details.</returns>
            private static string DescribeTarget(RenderTexture target)
            {
                if (target == null)
                    return "AssetPath=" + RenderTextureAssetPath + " Target=<null>";

                RenderTextureDescriptor descriptor = target.descriptor;
                return "AssetPath=" + RenderTextureAssetPath +
                    " DescriptorColor=" + descriptor.graphicsFormat +
                    " DescriptorDepthStencil=" + descriptor.depthStencilFormat +
                    " DescriptorMSAA=" + descriptor.msaaSamples +
                    " DescriptorSRGB=" + descriptor.sRGB +
                    " DescriptorMipMap=" + descriptor.useMipMap +
                    " DescriptorDynamicScale=" + descriptor.useDynamicScale +
                    " DescriptorRandomWrite=" + descriptor.enableRandomWrite +
                    " ActualColor=" + target.graphicsFormat +
                    " ActualDepthStencil=" + target.depthStencilFormat +
                    " ActualMSAA=" + target.antiAliasing +
                    " ActualSRGB=" + target.sRGB +
                    " ActualMipMap=" + target.useMipMap +
                    " ActualDynamicScale=" + target.useDynamicScale +
                    " ActualRandomWrite=" + target.enableRandomWrite +
                    " StencilBits=" + GetStencilBitCount(target.depthStencilFormat) +
                    " IsCreated=" + target.IsCreated();
            }

            /// <summary>Configures the fixture camera to render only its isolated preview scene into the D24S8 target.</summary>
            private void ConfigureCamera()
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 0.5f;
                camera.cullingMask = 1 << FixtureLayer;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.clearFlags = CameraClearFlags.Nothing;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.targetTexture = renderTexture;
            }

            /// <summary>Configures the centered quad rendered by every product observation.</summary>
            private void ConfigureQuad()
            {
                quadObject.layer = FixtureLayer;
                quadObject.transform.position = Vector3.zero;
            }

            /// <summary>Creates the requested number of directional lights for a ForwardAdd observation.</summary>
            /// <param name="lightCount">The required number of lights.</param>
            private void SetLightCount(int lightCount)
            {
                Assert.That(lightCount, Is.GreaterThanOrEqualTo(1), "The Stencil fixture must have at least one directional light.");
                DestroyLights();
                int cullingMask = 1 << FixtureLayer;
                for (int index = 0; index < lightCount; index++)
                {
                    var lightObject = new GameObject("PureBaseD24S8StencilLight" + index);
                    lightObjects.Add(lightObject);
                    SceneManager.MoveGameObjectToScene(lightObject, scene);
                    lightObject.layer = FixtureLayer;
                    Light light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.color = Color.white;
                    light.intensity = 1.0f;
                    light.cullingMask = cullingMask;
                    lightObject.transform.rotation = Quaternion.Euler(30.0f, index == 0 ? -30.0f : 30.0f, 0.0f);
                }
            }

            /// <summary>Destroys temporary directional lights in reverse creation order.</summary>
            private void DestroyLights()
            {
                for (int index = lightObjects.Count - 1; index >= 0; index--)
                    UnityEngine.Object.DestroyImmediate(lightObjects[index]);
                lightObjects.Clear();
            }

            /// <summary>Creates one owned product material and applies the requested mode and explicit Stencil values.</summary>
            /// <param name="shader">The product shader.</param>
            /// <param name="baseColor">The material base color.</param>
            /// <param name="stencilState">The required Stencil state.</param>
            /// <param name="renderingMode">The Pure-Base rendering mode.</param>
            /// <param name="scenario">The scenario used to distinguish missing product behavior from fixture failure.</param>
            /// <returns>The configured material.</returns>
            private Material CreateProductMaterial(Shader shader, Color baseColor, StencilState stencilState, int renderingMode, string scenario)
            {
                Assert.That(shader, Is.Not.Null, "The product shader is required for the D24S8 Stencil " + scenario + " fixture.");
                var material = new Material(shader);
                materials.Add(material);
                Assert.That(material.HasProperty("_BaseColor"), Is.True, "Product shader '" + shader.name + "' must expose _BaseColor for D24S8 Stencil " + scenario + ". " + FormatDescription);
                Assert.That(material.HasProperty("_Cutoff"), Is.True, "Product shader '" + shader.name + "' must expose _Cutoff for D24S8 Stencil " + scenario + ". " + FormatDescription);
                material.SetColor("_BaseColor", baseColor);
                material.SetFloat("_Cutoff", 0.5f);
                ConfigureMode(material, renderingMode);
                ConfigureStencil(material, stencilState, scenario);
                return material;
            }

            /// <summary>Requires the product ABI and assigns every explicit Stencil state property.</summary>
            /// <param name="material">The material receiving Stencil values.</param>
            /// <param name="stencilState">The required Stencil values.</param>
            /// <param name="scenario">The scenario used in failure diagnostics.</param>
            private void ConfigureStencil(Material material, StencilState stencilState, string scenario)
            {
                RequireStencilProperty(material, "_StencilRef", scenario);
                RequireStencilProperty(material, "_StencilReadMask", scenario);
                RequireStencilProperty(material, "_StencilWriteMask", scenario);
                RequireStencilProperty(material, "_StencilComp", scenario);
                RequireStencilProperty(material, "_StencilPass", scenario);
                RequireStencilProperty(material, "_StencilFail", scenario);
                RequireStencilProperty(material, "_StencilZFail", scenario);
                material.SetFloat("_StencilRef", stencilState.referenceValue);
                material.SetFloat("_StencilReadMask", stencilState.readMask);
                material.SetFloat("_StencilWriteMask", stencilState.writeMask);
                material.SetFloat("_StencilComp", (float)stencilState.comparison);
                material.SetFloat("_StencilPass", (float)stencilState.passOperation);
                material.SetFloat("_StencilFail", (float)stencilState.failOperation);
                material.SetFloat("_StencilZFail", (float)stencilState.depthFailOperation);
            }

            /// <summary>Fails as missing product behavior when the requested visible Stencil property is absent.</summary>
            /// <param name="material">The product material.</param>
            /// <param name="propertyName">The required public Stencil property.</param>
            /// <param name="scenario">The scenario used in failure diagnostics.</param>
            private void RequireStencilProperty(Material material, string propertyName, string scenario)
            {
                Assert.That(material.HasProperty(propertyName), Is.True, "Product shader '" + material.shader.name + "' is missing Stencil ABI property '" + propertyName + "' for " + scenario + ". This is product behavior, not fixture format failure. " + FormatDescription);
            }

            /// <summary>Clears color, depth, and the exact requested Stencil byte through the owned command buffer.</summary>
            /// <param name="clearStencil">The Stencil value to install before rendering.</param>
            private void ClearTarget(byte clearStencil)
            {
                commandBuffer.Clear();
                commandBuffer.SetRenderTarget(renderTexture);
                commandBuffer.ClearRenderTarget(RTClearFlags.All, Color.clear, 1.0f, clearStencil);
                Graphics.ExecuteCommandBuffer(commandBuffer);
            }

            /// <summary>Renders one material through the camera without clearing the existing depth-stencil attachment.</summary>
            /// <param name="material">The material to render.</param>
            private void RenderMaterial(Material material)
            {
                quadObject.GetComponent<Renderer>().sharedMaterial = material;
                cameraObject.GetComponent<Camera>().Render();
            }
        }
    }
}
