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

// Defines focused numerical and runtime contracts for Toon scene-light direction and two-band SH lighting.

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
    /// <summary>Defines fixed Toon lighting contracts independently of product HLSL implementation details.</summary>
    public sealed class PureBaseToonLightingContractTests
    {
        /// <summary>Defines the tolerance for fixed half/float-compatible lighting reference values.</summary>
        private const float OracleTolerance = 0.0002f;

        /// <summary>Identifies the global keyword that selects the ForwardAdd point-light variant.</summary>
        private const string PointKeyword = "POINT";

        /// <summary>Requires the fixed dominant-direction two-band SH equation and rejects the current continuous normal evaluation.</summary>
        [Test]
        public void FixedTwoBandShOracleMatchesReferenceAndRejectsContinuousNormalEvaluation()
        {
            Vector4 shAr = new Vector4(0.3f, 0.0f, 0.0f, 0.2f);
            Vector4 shAg = new Vector4(0.0f, 0.15f, 0.0f, 0.1f);
            Vector4 shAb = new Vector4(0.0f, 0.0f, 0.45f, 0.3f);
            Vector3 direction = EvaluateDominantDirection(
                Vector3.zero,
                shAr,
                shAg,
                shAb
            );
            Color bright = EvaluateTwoBandSh(direction, direction, shAr, shAg, shAb);
            Color dark = EvaluateTwoBandSh(-direction, direction, shAr, shAg, shAb);

            AssertVector(
                new Vector3(0.53452248f, 0.26726124f, 0.80178373f),
                direction,
                "Fixed Toon SH dominant direction"
            );
            AssertColor(
                new Color(0.30690450f, 0.12672612f, 0.54053512f, 1.0f),
                bright,
                "Fixed Toon SH bright band"
            );
            AssertColor(
                new Color(0.09309550f, 0.07327388f, 0.05946488f, 1.0f),
                dark,
                "Fixed Toon SH dark band"
            );

            Color oldContinuousBright = EvaluateContinuousNormalSh(direction, shAr, shAg, shAb);
            Color oldContinuousDark = EvaluateContinuousNormalSh(-direction, shAr, shAg, shAb);
            Assert.That(
                MaximumRgbDifference(oldContinuousBright, bright),
                Is.GreaterThan(0.02f),
                "The old continuous normal-evaluated SH must not satisfy the fixed bright-band oracle."
            );
            Assert.That(
                MaximumRgbDifference(oldContinuousDark, dark),
                Is.GreaterThan(0.02f),
                "The old continuous normal-evaluated SH must not satisfy the fixed dark-band oracle."
            );
        }

        /// <summary>Requires the fixed nonzero fallback before dominant-direction normalization.</summary>
        [Test]
        public void DegenerateDominantDirectionUsesFixedFiniteFallback()
        {
            Vector3 direction = EvaluateDominantDirection(
                Vector3.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero
            );

            AssertVector(
                new Vector3(0.40824829f, 0.81649658f, 0.40824829f),
                direction,
                "Fixed Toon SH degenerate-direction fallback"
            );
            Assert.That(IsFinite(direction), Is.True, "Fallback direction must remain finite.");
        }

        /// <summary>Requires Toon ForwardBase readbacks to select fixed bright and dark SH bands instead of continuous normal SH.</summary>
        [Test]
        public void ToonForwardBaseShOnlyReadbackRequiresFixedBrightAndDarkBands()
        {
            ShCoefficients coefficients = ShCoefficients.FixedOracle;
            Vector3 direction = EvaluateDominantDirection(
                Vector3.zero,
                coefficients.ar,
                coefficients.ag,
                coefficients.ab
            );
            Color expectedBright = EvaluateTwoBandSh(
                direction,
                direction,
                coefficients.ar,
                coefficients.ag,
                coefficients.ab
            );
            Color expectedDark = EvaluateTwoBandSh(
                -direction,
                direction,
                coefficients.ar,
                coefficients.ag,
                coefficients.ab
            );

            using (var capture = new ToonLightingCaptureScope())
            {
                Color bright = capture.Render(
                    "PureBase/Toon",
                    "ForwardBase",
                    direction,
                    Vector4.zero,
                    Vector4.zero,
                    coefficients
                );
                Color dark = capture.Render(
                    "PureBase/Toon",
                    "ForwardBase",
                    -direction,
                    Vector4.zero,
                    Vector4.zero,
                    coefficients
                );

                AssertFinite(bright, "Toon SH-only bright readback");
                AssertFinite(dark, "Toon SH-only dark readback");
                AssertColor(expectedBright, bright, "Toon SH-only bright readback");
                AssertColor(expectedDark, dark, "Toon SH-only dark readback");
            }
        }

        /// <summary>Requires direct aggregate direction to participate in Toon SH-band selection while direct binary lighting remains additive.</summary>
        [Test]
        public void ToonForwardBaseDirectAggregateParticipatesInShBandDirection()
        {
            ShCoefficients coefficients = ShCoefficients.FixedOracle;
            Vector4 directLight = new Vector4(0.2f, 0.2f, 0.2f, 1.0f);
            Vector4 directionalPosition = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
            Vector3 direction = EvaluateDominantDirection(
                new Vector3(0.0f, 0.0f, 0.2f),
                coefficients.ar,
                coefficients.ag,
                coefficients.ab
            );
            Color expectedBright = EvaluateTwoBandSh(
                direction,
                direction,
                coefficients.ar,
                coefficients.ag,
                coefficients.ab
            ) + new Color(0.2f, 0.2f, 0.2f, 0.0f);

            using (var capture = new ToonLightingCaptureScope())
            {
                Color actual = capture.Render(
                    "PureBase/Toon",
                    "ForwardBase",
                    direction,
                    directLight,
                    directionalPosition,
                    coefficients
                );

                AssertFinite(actual, "Toon direct-plus-SH readback");
                AssertColor(expectedBright, actual, "Toon direct-plus-SH readback");
            }
        }

        /// <summary>Requires injected SH to leave a point-light ForwardAdd readback unchanged while its RGB and alpha remain valid.</summary>
        [Test]
        public void ToonForwardAddSecondPointLightIgnoresInjectedShAndPreservesDestinationAlpha()
        {
            Vector4 pointPosition = new Vector4(0.0f, 0.0f, -2.0f, 1.0f);
            Vector4 pointLight = new Vector4(0.3f, 0.2f, 0.1f, 1.0f);
            using (var capture = new ToonLightingCaptureScope())
            {
                Color withoutSh = capture.Render(
                    "PureBase/Toon",
                    "ForwardAdd",
                    Vector3.back,
                    pointLight,
                    pointPosition,
                    ShCoefficients.Zero,
                    true
                );
                Color withSh = capture.Render(
                    "PureBase/Toon",
                    "ForwardAdd",
                    Vector3.back,
                    pointLight,
                    pointPosition,
                    ShCoefficients.FixedOracle,
                    true
                );

                AssertFinite(withoutSh, "Toon ForwardAdd point readback without SH");
                AssertFinite(withSh, "Toon ForwardAdd point readback with SH");
                Assert.That(
                    RgbMagnitude(withoutSh),
                    Is.GreaterThan(0.001f),
                    "The isolated second point-light ForwardAdd contribution must retain finite nonzero RGB."
                );
                Assert.That(
                    MaximumRgbDifference(withoutSh, withSh),
                    Is.LessThanOrEqualTo(0.002f),
                    "Injected SH must not alter the isolated additional-light ForwardAdd contribution."
                );
                Assert.That(
                    withSh.a,
                    Is.EqualTo(withoutSh.a).Within(0.002f),
                    "ForwardAdd must preserve the existing destination alpha contract."
                );
            }
        }

        /// <summary>Requires ForwardAdd point-light rendering to preserve the caller's global POINT keyword state.</summary>
        /// <param name="initiallyEnabled">Whether POINT is globally enabled before the transient capture begins.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void ToonForwardAddPointLightRenderRestoresPointKeywordState(bool initiallyEnabled)
        {
            bool originalPointKeywordState = Shader.IsKeywordEnabled(PointKeyword);
            try
            {
                RestorePointKeywordState(initiallyEnabled);
                using (var capture = new ToonLightingCaptureScope())
                {
                    Color readback = capture.Render(
                        "PureBase/Toon",
                        "ForwardAdd",
                        Vector3.back,
                        new Vector4(0.3f, 0.2f, 0.1f, 1.0f),
                        new Vector4(0.0f, 0.0f, -2.0f, 1.0f),
                        ShCoefficients.Zero,
                        true
                    );

                    AssertFinite(readback, "Toon ForwardAdd point keyword-state readback");
                    Assert.That(
                        Shader.IsKeywordEnabled(PointKeyword),
                        Is.EqualTo(initiallyEnabled),
                        "ForwardAdd point-light rendering must restore POINT after the draw."
                    );
                }

                Assert.That(
                    Shader.IsKeywordEnabled(PointKeyword),
                    Is.EqualTo(initiallyEnabled),
                    "Disposing the capture scope must retain the caller's POINT state."
                );
            }
            finally
            {
                RestorePointKeywordState(originalPointKeywordState);
            }
        }

        /// <summary>Restores the global POINT keyword to the supplied caller-owned state.</summary>
        /// <param name="enabled">Whether POINT must be enabled after restoration.</param>
        private static void RestorePointKeywordState(bool enabled)
        {
            if (enabled)
            {
                Shader.EnableKeyword(PointKeyword);
            }
            else
            {
                Shader.DisableKeyword(PointKeyword);
            }
        }

        /// <summary>Requires metallic-one PBR and Hybrid direct GGX to match and remain insensitive to injected SH.</summary>
        [Test]
        public void HybridMetallicOneDirectSpecularMatchesPbrAndIgnoresInjectedSh()
        {
            Vector4 directLight = new Vector4(0.7f, 0.6f, 0.5f, 1.0f);
            Vector4 directionalPosition = new Vector4(0.0f, 0.0f, 1.0f, 0.0f);
            using (var capture = new ToonLightingCaptureScope())
            {
                Color pbr = capture.Render(
                    "PureBase/PBR",
                    "ForwardBase",
                    Vector3.forward,
                    directLight,
                    directionalPosition,
                    ShCoefficients.Zero,
                    false,
                    1.0f
                );
                Color hybridWithoutSh = capture.Render(
                    "PureBase/Hybrid",
                    "ForwardBase",
                    Vector3.forward,
                    directLight,
                    directionalPosition,
                    ShCoefficients.Zero,
                    false,
                    1.0f
                );
                Color hybridWithSh = capture.Render(
                    "PureBase/Hybrid",
                    "ForwardBase",
                    Vector3.forward,
                    directLight,
                    directionalPosition,
                    ShCoefficients.FixedOracle,
                    false,
                    1.0f
                );
                Color hybridBackFace = capture.Render(
                    "PureBase/Hybrid",
                    "ForwardBase",
                    Vector3.back,
                    directLight,
                    directionalPosition,
                    ShCoefficients.Zero,
                    false,
                    0.0f
                );
                Color hybridFrontFace = capture.Render(
                    "PureBase/Hybrid",
                    "ForwardBase",
                    Vector3.forward,
                    directLight,
                    directionalPosition,
                    ShCoefficients.Zero,
                    false,
                    0.0f
                );

                AssertFinite(pbr, "PBR metallic-one direct-specular readback");
                AssertFinite(hybridWithoutSh, "Hybrid metallic-one direct-specular readback");
                AssertFinite(hybridWithSh, "Hybrid metallic-one SH-injected readback");
                Assert.That(
                    MaximumRgbDifference(pbr, hybridWithoutSh),
                    Is.LessThanOrEqualTo(0.01f),
                    "PBR and Hybrid metallic-one direct GGX must remain equivalent."
                );
                Assert.That(
                    MaximumRgbDifference(hybridWithoutSh, hybridWithSh),
                    Is.LessThanOrEqualTo(0.01f),
                    "Injected Toon SH must not enter Hybrid direct GGX."
                );
                Assert.That(
                    RgbMagnitude(hybridFrontFace),
                    Is.GreaterThan(RgbMagnitude(hybridBackFace) + 0.01f),
                    "Nonmetallic Hybrid must retain the binary direct-diffuse response."
                );
            }
        }

        /// <summary>Stores the seven Unity SH global vectors injected immediately before each explicit draw.</summary>
        private readonly struct ShCoefficients
        {
            /// <summary>Initializes the complete Unity SH global vector set.</summary>
            /// <param name="ar">The red linear SH vector.</param>
            /// <param name="ag">The green linear SH vector.</param>
            /// <param name="ab">The blue linear SH vector.</param>
            /// <param name="br">The red quadratic SH vector.</param>
            /// <param name="bg">The green quadratic SH vector.</param>
            /// <param name="bb">The blue quadratic SH vector.</param>
            /// <param name="c">The shared quadratic SH vector.</param>
            public ShCoefficients(
                Vector4 ar,
                Vector4 ag,
                Vector4 ab,
                Vector4 br,
                Vector4 bg,
                Vector4 bb,
                Vector4 c
            )
            {
                this.ar = ar;
                this.ag = ag;
                this.ab = ab;
                this.br = br;
                this.bg = bg;
                this.bb = bb;
                this.c = c;
            }

            /// <summary>Gets the red linear SH vector.</summary>
            public Vector4 ar { get; }

            /// <summary>Gets the green linear SH vector.</summary>
            public Vector4 ag { get; }

            /// <summary>Gets the blue linear SH vector.</summary>
            public Vector4 ab { get; }

            /// <summary>Gets the red quadratic SH vector.</summary>
            public Vector4 br { get; }

            /// <summary>Gets the green quadratic SH vector.</summary>
            public Vector4 bg { get; }

            /// <summary>Gets the blue quadratic SH vector.</summary>
            public Vector4 bb { get; }

            /// <summary>Gets the shared quadratic SH vector.</summary>
            public Vector4 c { get; }

            /// <summary>Gets the fixed coefficient set used by the pure C# oracle and runtime contracts.</summary>
            public static ShCoefficients FixedOracle => new ShCoefficients(
                new Vector4(0.3f, 0.0f, 0.0f, 0.2f),
                new Vector4(0.0f, 0.15f, 0.0f, 0.1f),
                new Vector4(0.0f, 0.0f, 0.45f, 0.3f),
                Vector4.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero
            );

            /// <summary>Gets the all-zero SH set used to isolate direct-light contributions.</summary>
            public static ShCoefficients Zero => new ShCoefficients(
                Vector4.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero
            );
        }

        /// <summary>Owns one isolated explicit-draw fixture and restores every Unity global it changes.</summary>
        private sealed class ToonLightingCaptureScope : IDisposable
        {
            /// <summary>Lists the injected global property names in draw-order ownership order.</summary>
            private static readonly string[] GlobalNames =
            {
                "_LightColor0",
                "_WorldSpaceLightPos0",
                "unity_SHAr",
                "unity_SHAg",
                "unity_SHAb",
                "unity_SHBr",
                "unity_SHBg",
                "unity_SHBb",
                "unity_SHC",
            };

            /// <summary>Stores the caller's POINT keyword state before this fixture can issue a point-light draw.</summary>
            private readonly bool pointKeywordEnabled;

            /// <summary>Stores the global vectors captured before the fixture writes any explicit lighting input.</summary>
            private readonly Dictionary<string, Vector4> globals = new Dictionary<string, Vector4>();

            /// <summary>Stores caller-owned temporary material instances.</summary>
            private readonly List<Material> materials = new List<Material>();

            /// <summary>Stores the active render target before CPU readback changes it.</summary>
            private readonly RenderTexture activeRenderTexture;

            /// <summary>Stores the active scene before the Preview Scene becomes the capture context.</summary>
            private readonly Scene activeScene;

            /// <summary>Stores the original loaded scene count for restoration diagnostics.</summary>
            private readonly int sceneCount;

            /// <summary>Stores the original pixel-light budget.</summary>
            private readonly int pixelLightCount;

            /// <summary>Stores the original fog setting for the formerly active scene.</summary>
            private readonly bool fogEnabled;

            /// <summary>Stores the disposable Preview Scene that owns all generated GameObjects.</summary>
            private readonly Scene scene;

            /// <summary>Stores the explicit-draw camera.</summary>
            private readonly Camera camera;

            /// <summary>Stores the mesh whose world normals are controlled by each contract.</summary>
            private readonly Mesh mesh;

            /// <summary>Stores the linear float render target.</summary>
            private readonly RenderTexture target;

            /// <summary>Stores the float CPU readback texture.</summary>
            private readonly Texture2D readback;

            /// <summary>Stores the command buffer that injects globals immediately before every draw.</summary>
            private readonly CommandBuffer commandBuffer;

            /// <summary>Tracks whether this scope has already released its resources.</summary>
            private bool disposed;

            /// <summary>Creates an isolated linear render fixture with no fog or reflection probes.</summary>
            public ToonLightingCaptureScope()
            {
                activeRenderTexture = RenderTexture.active;
                activeScene = SceneManager.GetActiveScene();
                sceneCount = SceneManager.sceneCount;
                pixelLightCount = QualitySettings.pixelLightCount;
                fogEnabled = RenderSettings.fog;
                pointKeywordEnabled = Shader.IsKeywordEnabled(PointKeyword);
                foreach (string globalName in GlobalNames)
                {
                    globals.Add(globalName, Shader.GetGlobalVector(globalName));
                }

                scene = EditorSceneManager.NewPreviewScene();
                RenderSettings.fog = false;
                QualitySettings.pixelLightCount = Mathf.Max(2, pixelLightCount);

                GameObject cameraObject = EditorUtility.CreateGameObjectWithHideFlags(
                    "PureBase Toon Lighting Contract Camera",
                    HideFlags.HideAndDontSave,
                    typeof(Camera)
                );
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.GetComponent<Camera>();
                target = new RenderTexture(
                    64,
                    64,
                    24,
                    RenderTextureFormat.ARGBFloat,
                    RenderTextureReadWrite.Linear
                ) { hideFlags = HideFlags.HideAndDontSave };
                target.Create();
                readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                mesh = CreateNormalControlledQuad(Vector3.forward);
                commandBuffer = new CommandBuffer { name = "PureBase Toon Lighting Contract Draw" };
                camera.enabled = false;
                camera.cullingMask = 0;
                camera.orthographic = true;
                camera.orthographicSize = 1.0f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 10.0f;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.37f);
                camera.renderingPath = RenderingPath.Forward;
                camera.targetTexture = target;
                camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);
            }

            /// <summary>Draws one product pass after installing the exact lighting globals for the current test case.</summary>
            /// <param name="shaderName">The required product shader name.</param>
            /// <param name="passName">The required explicit pass name.</param>
            /// <param name="normal">The uniform mesh world normal.</param>
            /// <param name="lightColor">The explicitly injected main or additional light color.</param>
            /// <param name="lightPosition">The explicitly injected directional or point light vector.</param>
            /// <param name="coefficients">The seven SH globals injected immediately before the draw.</param>
            /// <param name="pointLight">Whether the ForwardAdd draw selects the point-light shader variant.</param>
            /// <param name="metallic">The material metallic value.</param>
            /// <returns>The center linear float readback color.</returns>
            public Color Render(
                string shaderName,
                string passName,
                Vector3 normal,
                Vector4 lightColor,
                Vector4 lightPosition,
                ShCoefficients coefficients,
                bool pointLight = false,
                float metallic = 0.0f
            )
            {
                bool pointKeywordWasEnabled = Shader.IsKeywordEnabled(PointKeyword);
                try
                {
                    Shader shader = Shader.Find(shaderName);
                    Assert.That(shader, Is.Not.Null, "Product shader '" + shaderName + "' is unavailable.");
                    Assert.That(
                        ShaderUtil.ShaderHasError(shader),
                        Is.False,
                        "Product shader '" + shaderName + "' has compiler errors."
                    );
                    var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    materials.Add(material);
                    ConfigureMaterial(material, metallic);
                    int pass = material.FindPass(passName);
                    Assert.That(pass, Is.GreaterThanOrEqualTo(0), shaderName + " requires " + passName + ".");
                    SetMeshNormal(normal);

                    commandBuffer.Clear();
                    commandBuffer.SetGlobalVector("_LightColor0", lightColor);
                    commandBuffer.SetGlobalVector("_WorldSpaceLightPos0", lightPosition);
                    commandBuffer.SetGlobalVector("unity_SHAr", coefficients.ar);
                    commandBuffer.SetGlobalVector("unity_SHAg", coefficients.ag);
                    commandBuffer.SetGlobalVector("unity_SHAb", coefficients.ab);
                    commandBuffer.SetGlobalVector("unity_SHBr", coefficients.br);
                    commandBuffer.SetGlobalVector("unity_SHBg", coefficients.bg);
                    commandBuffer.SetGlobalVector("unity_SHBb", coefficients.bb);
                    commandBuffer.SetGlobalVector("unity_SHC", coefficients.c);
                    if (pointLight)
                    {
                        commandBuffer.EnableShaderKeyword(PointKeyword);
                    }
                    commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
                    if (pointLight)
                    {
                        SetCommandBufferPointKeywordState(pointKeywordWasEnabled);
                    }

                    camera.Render();
                    return ReadCenterPixel();
                }
                finally
                {
                    RestorePointKeywordState(pointKeywordWasEnabled);
                }
            }

            /// <summary>Releases generated objects and restores global, render-target, quality, fog, and active-scene state.</summary>
            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                try
                {
                    if (camera != null && commandBuffer != null)
                    {
                        camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);
                    }
                    if (commandBuffer != null)
                    {
                        commandBuffer.Release();
                    }
                    foreach (Material material in materials)
                    {
                        UnityEngine.Object.DestroyImmediate(material);
                    }
                    if (readback != null)
                    {
                        UnityEngine.Object.DestroyImmediate(readback);
                    }
                    if (target != null)
                    {
                        target.Release();
                        UnityEngine.Object.DestroyImmediate(target);
                    }
                    if (mesh != null)
                    {
                        UnityEngine.Object.DestroyImmediate(mesh);
                    }
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.ClosePreviewScene(scene);
                    }
                }
                finally
                {
                    RestorePointKeywordState(pointKeywordEnabled);
                    foreach (KeyValuePair<string, Vector4> global in globals)
                    {
                        Shader.SetGlobalVector(global.Key, global.Value);
                    }
                    RenderTexture.active = activeRenderTexture;
                    QualitySettings.pixelLightCount = pixelLightCount;
                    if (activeScene.IsValid() && activeScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(activeScene);
                        RenderSettings.fog = fogEnabled;
                    }
                    Assert.That(
                        SceneManager.sceneCount,
                        Is.EqualTo(sceneCount),
                        "The Toon lighting capture scope must restore the original loaded scene count."
                    );
                }
            }

            /// <summary>Queues restoration of the POINT keyword state after the explicit point-light draw.</summary>
            /// <param name="enabled">Whether POINT must remain enabled after the draw.</param>
            private void SetCommandBufferPointKeywordState(bool enabled)
            {
                if (enabled)
                {
                    commandBuffer.EnableShaderKeyword(PointKeyword);
                }
                else
                {
                    commandBuffer.DisableShaderKeyword(PointKeyword);
                }
            }

            /// <summary>Configures a white opaque product material with no texture-specific lighting variation.</summary>
            /// <param name="material">The transient product material.</param>
            /// <param name="metallic">The metallic value for PBR and Hybrid observations.</param>
            private static void ConfigureMaterial(Material material, float metallic)
            {
                material.SetTexture("_BaseTexture", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_NormalMap", Texture2D.normalTexture);
                material.SetFloat("_NormalScale", 1.0f);
                if (material.HasProperty("_Metallic"))
                {
                    material.SetFloat("_Metallic", metallic);
                    material.SetFloat("_Roughness", 0.25f);
                }
            }

            /// <summary>Updates the uniform quad normal and tangent basis without reallocating the transient mesh.</summary>
            /// <param name="normal">The required normalized world-space normal.</param>
            private void SetMeshNormal(Vector3 normal)
            {
                Vector3 normalized = normal.normalized;
                mesh.normals = new[] { normalized, normalized, normalized, normalized };
                mesh.tangents = new[]
                {
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                };
            }

            /// <summary>Creates the full-frame mesh used by explicit pass draws.</summary>
            /// <param name="normal">The initial uniform mesh normal.</param>
            /// <returns>The caller-owned transient mesh.</returns>
            private static Mesh CreateNormalControlledQuad(Vector3 normal)
            {
                var result = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                result.vertices = new[]
                {
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(-1.0f, 1.0f, 0.0f),
                    new Vector3(1.0f, 1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                };
                result.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                result.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                result.RecalculateBounds();
                result.normals = new[] { normal, normal, normal, normal };
                return result;
            }

            /// <summary>Reads the center pixel while restoring the caller's active render target.</summary>
            /// <returns>The center linear color.</returns>
            private Color ReadCenterPixel()
            {
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(31.0f, 31.0f, 1.0f, 1.0f), 0, 0);
                    readback.Apply(false, false);
                    return readback.GetPixel(0, 0);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }
        }

        /// <summary>Evaluates the fixed direct-plus-SH dominant direction with the required degenerate fallback.</summary>
        /// <param name="directAggregateDirection">The grayscale-weighted direct-light direction aggregate.</param>
        /// <param name="shAr">The Unity red first-order SH coefficient vector.</param>
        /// <param name="shAg">The Unity green first-order SH coefficient vector.</param>
        /// <param name="shAb">The Unity blue first-order SH coefficient vector.</param>
        /// <returns>The finite normalized Toon scene-light direction.</returns>
        private static Vector3 EvaluateDominantDirection(
            Vector3 directAggregateDirection,
            Vector4 shAr,
            Vector4 shAg,
            Vector4 shAb
        )
        {
            Vector3 shDirection = (new Vector3(shAr.x, shAr.y, shAr.z)
                    + new Vector3(shAg.x, shAg.y, shAg.z)
                    + new Vector3(shAb.x, shAb.y, shAb.z))
                / 3.0f;
            Vector3 directionVector = directAggregateDirection
                + new Vector3(shDirection.x, Mathf.Abs(shDirection.y), shDirection.z);
            if (Vector3.Dot(directionVector, directionVector) <= 0.000001f)
            {
                directionVector = new Vector3(0.001f, 0.002f, 0.001f);
            }

            return directionVector.normalized;
        }

        /// <summary>Evaluates the fixed Toon bright or dark SH band selected by the supplied surface normal.</summary>
        /// <param name="surfaceNormal">The normalized world-space surface normal.</param>
        /// <param name="lightDirection">The fixed dominant scene-light direction.</param>
        /// <param name="shAr">The Unity red first-order SH coefficient vector.</param>
        /// <param name="shAg">The Unity green first-order SH coefficient vector.</param>
        /// <param name="shAb">The Unity blue first-order SH coefficient vector.</param>
        /// <returns>The selected nonnegative SH band.</returns>
        private static Color EvaluateTwoBandSh(
            Vector3 surfaceNormal,
            Vector3 lightDirection,
            Vector4 shAr,
            Vector4 shAg,
            Vector4 shAb
        )
        {
            Vector3 evaluationDirection = lightDirection * 0.666666f;
            Vector3 baseTerm = new Vector3(shAr.w, shAg.w, shAb.w);
            Vector3 linear = new Vector3(
                Vector3.Dot(new Vector3(shAr.x, shAr.y, shAr.z), evaluationDirection),
                Vector3.Dot(new Vector3(shAg.x, shAg.y, shAg.z), evaluationDirection),
                Vector3.Dot(new Vector3(shAb.x, shAb.y, shAb.z), evaluationDirection)
            );
            Vector3 bright = Vector3.Max(baseTerm + linear, Vector3.zero);
            Vector3 dark = Vector3.Max(baseTerm - linear, Vector3.zero);
            Vector3 selected = Vector3.Dot(surfaceNormal, lightDirection) >= 0.0f ? bright : dark;
            return new Color(selected.x, selected.y, selected.z, 1.0f);
        }

        /// <summary>Evaluates the pre-change Toon continuous normal SH implementation with zero quadratic coefficients.</summary>
        /// <param name="surfaceNormal">The normalized world-space surface normal.</param>
        /// <param name="shAr">The Unity red first-order SH coefficient vector.</param>
        /// <param name="shAg">The Unity green first-order SH coefficient vector.</param>
        /// <param name="shAb">The Unity blue first-order SH coefficient vector.</param>
        /// <returns>The pre-change continuous nonnegative SH result.</returns>
        private static Color EvaluateContinuousNormalSh(
            Vector3 surfaceNormal,
            Vector4 shAr,
            Vector4 shAg,
            Vector4 shAb
        )
        {
            Vector4 normal = new Vector4(surfaceNormal.x, surfaceNormal.y, surfaceNormal.z, 1.0f);
            Vector3 ambient = Vector3.Max(
                new Vector3(
                    Vector4.Dot(shAr, normal),
                    Vector4.Dot(shAg, normal),
                    Vector4.Dot(shAb, normal)
                ),
                Vector3.zero
            );
            return new Color(ambient.x, ambient.y, ambient.z, 1.0f);
        }

        /// <summary>Asserts a fixed Vector3 reference within the half/float-compatible tolerance.</summary>
        /// <param name="expected">The fixed reference vector.</param>
        /// <param name="actual">The observed vector.</param>
        /// <param name="label">The diagnostic label.</param>
        private static void AssertVector(Vector3 expected, Vector3 actual, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(OracleTolerance), label + " red/x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(OracleTolerance), label + " green/y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(OracleTolerance), label + " blue/z");
        }

        /// <summary>Asserts a fixed RGB reference within the half/float-compatible tolerance.</summary>
        /// <param name="expected">The fixed reference color.</param>
        /// <param name="actual">The observed color.</param>
        /// <param name="label">The diagnostic label.</param>
        private static void AssertColor(Color expected, Color actual, string label)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(OracleTolerance), label + " red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(OracleTolerance), label + " green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(OracleTolerance), label + " blue");
        }

        /// <summary>Asserts that every component of one readback color is finite.</summary>
        /// <param name="value">The color to inspect.</param>
        /// <param name="label">The diagnostic label.</param>
        private static void AssertFinite(Color value, string label)
        {
            Assert.That(
                float.IsNaN(value.r) || float.IsInfinity(value.r),
                Is.False,
                label + " red is non-finite."
            );
            Assert.That(
                float.IsNaN(value.g) || float.IsInfinity(value.g),
                Is.False,
                label + " green is non-finite."
            );
            Assert.That(
                float.IsNaN(value.b) || float.IsInfinity(value.b),
                Is.False,
                label + " blue is non-finite."
            );
            Assert.That(
                float.IsNaN(value.a) || float.IsInfinity(value.a),
                Is.False,
                label + " alpha is non-finite."
            );
        }

        /// <summary>Returns the Euclidean magnitude of a color's RGB components.</summary>
        /// <param name="value">The color to measure.</param>
        /// <returns>The nonnegative RGB magnitude.</returns>
        private static float RgbMagnitude(Color value)
        {
            return Mathf.Sqrt(value.r * value.r + value.g * value.g + value.b * value.b);
        }

        /// <summary>Returns the greatest absolute RGB channel difference between two colors.</summary>
        /// <param name="left">The first color.</param>
        /// <param name="right">The second color.</param>
        /// <returns>The maximum absolute RGB difference.</returns>
        private static float MaximumRgbDifference(Color left, Color right)
        {
            return Mathf.Max(
                Mathf.Abs(left.r - right.r),
                Mathf.Abs(left.g - right.g),
                Mathf.Abs(left.b - right.b)
            );
        }

        /// <summary>Returns whether every vector component is finite.</summary>
        /// <param name="value">The vector to inspect.</param>
        /// <returns>True when all components are finite.</returns>
        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x)
                && !float.IsInfinity(value.x)
                && !float.IsNaN(value.y)
                && !float.IsInfinity(value.y)
                && !float.IsNaN(value.z)
                && !float.IsInfinity(value.z);
        }
    }
}
