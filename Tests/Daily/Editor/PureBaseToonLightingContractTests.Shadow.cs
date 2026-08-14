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

// Defines numerical and additional-light contracts for Toon shadow attenuation separation.

using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines numerical and additional-light contracts for Toon shadow attenuation separation.</summary>
    [SuppressMessage("SonarAnalyzer.CSharp", "S2333", Justification = "This declaration must remain partial because the test fixture is split between its base, runtime capture, and shadow oracle source files.")]
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Requires effective visibility to remain independent from Toon direct radiance and direction weighting.</summary>
        [Test]
        public void ToonShadowSeparationOracleChangesOnlyPublishedVisibility()
        {
            ToonShadowInputs visible = new ToonShadowInputs(0.8f, 0.65f, 1.0f, 0.4f);
            ToonShadowInputs shadowed = new ToonShadowInputs(0.8f, 0.65f, 0.25f, 0.4f);
            ToonShadowObservation visibleObservation = EvaluateToonShadowContract(visible);
            ToonShadowObservation shadowedObservation = EvaluateToonShadowContract(shadowed);

            Assert.That(shadowedObservation.directRadiance, Is.EqualTo(visibleObservation.directRadiance).Within(OracleTolerance));
            Assert.That(shadowedObservation.directionWeight, Is.EqualTo(visibleObservation.directionWeight).Within(OracleTolerance));
            Assert.That(shadowedObservation.publishedVisibility, Is.EqualTo(0.25f).Within(OracleTolerance));
            Assert.That(visibleObservation.fullAttenuation, Is.EqualTo(0.65f).Within(OracleTolerance));
            Assert.That(shadowedObservation.fullAttenuation, Is.EqualTo(0.1625f).Within(OracleTolerance));
        }

        /// <summary>Requires Point and Spot non-shadow attenuation to change both direct radiance and aggregate direction weight.</summary>
        [Test]
        public void ToonPointAndSpotNonShadowAttenuationOracleChangesDirectRadianceAndDirectionWeight()
        {
            ToonShadowObservation pointNear = EvaluateToonShadowContract(new ToonShadowInputs(0.8f, 0.8f, 1.0f, 0.4f));
            ToonShadowObservation pointRangeEdge = EvaluateToonShadowContract(new ToonShadowInputs(0.8f, 0.2f, 1.0f, 0.4f));
            ToonShadowObservation spotInside = EvaluateToonShadowContract(new ToonShadowInputs(0.8f, 0.75f, 1.0f, 0.4f));
            ToonShadowObservation spotConeEdge = EvaluateToonShadowContract(new ToonShadowInputs(0.8f, 0.15f, 1.0f, 0.4f));

            Assert.That(pointRangeEdge.directRadiance, Is.LessThan(pointNear.directRadiance - 0.02f));
            Assert.That(pointRangeEdge.directionWeight, Is.LessThan(pointNear.directionWeight - 0.02f));
            Assert.That(spotConeEdge.directRadiance, Is.LessThan(spotInside.directRadiance - 0.02f));
            Assert.That(spotConeEdge.directionWeight, Is.LessThan(spotInside.directionWeight - 0.02f));
        }

        /// <summary>Requires Point and Spot non-shadow attenuation to remain observable in isolated ForwardAdd rendering.</summary>
        [Test]
        public void ToonForwardAddPointAndSpotRetainFiniteRangeConeShAndDestinationAlphaContracts()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                Vector4 color = new Vector4(0.3f, 0.2f, 0.1f, 1.0f);
                Color pointNear = capture.RenderAdditionalLight("PureBase/Toon", Vector3.back, color, new Vector4(0.0f, 0.0f, -2.0f, 1.0f), LightType.Point, 4.0f, 30.0f);
                Color pointEdge = capture.RenderAdditionalLight("PureBase/Toon", Vector3.back, color, new Vector4(0.0f, 0.0f, -3.8f, 1.0f), LightType.Point, 4.0f, 30.0f);
                Color spotInside = capture.RenderAdditionalLight("PureBase/Toon", Vector3.back, color, new Vector4(0.0f, 0.0f, -2.0f, 1.0f), LightType.Spot, 4.0f, 35.0f);
                Color spotOutside = capture.RenderAdditionalLight("PureBase/Toon", Vector3.back, color, new Vector4(2.0f, 0.0f, -2.0f, 1.0f), LightType.Spot, 4.0f, 20.0f);
                Color pointWithSh = capture.RenderAdditionalLight("PureBase/Toon", Vector3.back, color, new Vector4(0.0f, 0.0f, -2.0f, 1.0f), LightType.Point, 4.0f, 30.0f, ShCoefficients.FixedOracle);
                Color spotWithSh = capture.RenderAdditionalLight("PureBase/Toon", Vector3.back, color, new Vector4(0.0f, 0.0f, -2.0f, 1.0f), LightType.Spot, 4.0f, 35.0f, ShCoefficients.FixedOracle);

                AssertFinite(pointNear, "Toon ForwardAdd Point near contribution");
                AssertFinite(pointEdge, "Toon ForwardAdd Point range-edge contribution");
                AssertFinite(spotInside, "Toon ForwardAdd Spot inside-cone contribution");
                AssertFinite(spotOutside, "Toon ForwardAdd Spot outside-cone contribution");
                AssertFinite(pointWithSh, "Toon ForwardAdd Point SH-isolated contribution");
                AssertFinite(spotWithSh, "Toon ForwardAdd Spot SH-isolated contribution");
                Assert.That(RgbMagnitude(pointNear), Is.GreaterThan(0.001f));
                Assert.That(RgbMagnitude(spotInside), Is.GreaterThan(0.001f));
                Assert.That(RgbMagnitude(pointEdge), Is.LessThan(RgbMagnitude(pointNear) - 0.01f));
                Assert.That(RgbMagnitude(spotOutside), Is.LessThan(0.001f));
                Assert.That(MaximumRgbDifference(pointNear, pointWithSh), Is.LessThanOrEqualTo(0.002f));
                Assert.That(MaximumRgbDifference(spotInside, spotWithSh), Is.LessThanOrEqualTo(0.002f));
                Assert.That(pointEdge.a, Is.EqualTo(pointNear.a).Within(0.002f));
                Assert.That(spotInside.a, Is.EqualTo(spotOutside.a).Within(0.002f));
            }
        }

        /// <summary>Requires the fixed Toon host to publish directional visibility through all three selected phases.</summary>
        [Test]
        public void FixedToonShadowHostPublishesFinitePhaseLocalRgbVisibilityForNoneHardAndSoft()
        {
            using (var selection = new ToonShadowHostSelectionScope())
            using (var capture = new ToonLightingCaptureScope())
            {
                AssertImportedToonShadowGeneratedSource();
                ShadowReceiverObservation none = capture.RenderDirectionalShadowReceiver(
                    "PureBase/Tests/ShaderCore/ToonShadow",
                    LightShadows.None
                );
                ShadowReceiverObservation hard = capture.RenderDirectionalShadowReceiver(
                    "PureBase/Tests/ShaderCore/ToonShadow",
                    LightShadows.Hard
                );
                ShadowReceiverObservation soft = capture.RenderDirectionalShadowReceiver(
                    "PureBase/Tests/ShaderCore/ToonShadow",
                    LightShadows.Soft
                );

                AssertShadowReceiverFinite(none, "Toon shadow host None");
                AssertShadowReceiverFinite(hard, "Toon shadow host Hard");
                AssertShadowReceiverFinite(soft, "Toon shadow host Soft");
                AssertPhaseChannelsAgree(none.meanColor, "Toon shadow host None");
                AssertPhaseChannelsAgree(hard.meanColor, "Toon shadow host Hard");
                AssertPhaseChannelsAgree(soft.meanColor, "Toon shadow host Soft");
                AssertPhaseVisibilityBoundaries(none, hard, soft);
            }
        }

        /// <summary>Requires PBR and Hybrid to retain real Hard-shadow response while Unlit remains shadow-invariant.</summary>
        [Test]
        public void NonToonDirectionalShadowControlsRetainLightingResponseAndUnlitInvariance()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                AssertNonToonShadowControl(capture, "PureBase/PBR", true);
                AssertNonToonShadowControl(capture, "PureBase/Hybrid", true);
                AssertNonToonShadowControl(capture, "PureBase/Unlit", false);
            }
        }

        /// <summary>Checks that the temporary fixed-host selection produced all three phase diagnostics before runtime evidence is sampled.</summary>
        private static void AssertImportedToonShadowGeneratedSource()
        {
            const string assetPath = "Packages/jp.penguin.purebase/Tests/Fixtures/Hosts/ToonShadow/PureBaseTestToonShadow.scshader";
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            Assert.That(shader, Is.Not.Null, "The temporary ToonShadow host import did not produce a shader.");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, "The temporary ToonShadow host import produced shader compiler errors.");

            TextAsset generatedSource = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset is TextAsset textAsset && textAsset.name == "Shader Source")
                {
                    generatedSource = textAsset;
                    break;
                }
            }

            Assert.That(generatedSource, Is.Not.Null, "The temporary ToonShadow host import did not produce generated source.");
            StringAssert.Contains("PUREBASE_TEST_TOON_SHADOW_SENTINEL_LIGHT", generatedSource.text);
            StringAssert.Contains("PUREBASE_TEST_TOON_SHADOW_SENTINEL_MODIFYLIGHT", generatedSource.text);
            StringAssert.Contains("PUREBASE_TEST_TOON_SHADOW_SENTINEL_SHADE", generatedSource.text);
        }

        /// <summary>Requires product Toon direct color and aggregate direction to remain independent from directional visibility.</summary>
        [Test]
        public void ToonProductDirectionalShadowDoesNotContaminateDirectColorOrAggregateDirection()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                ShadowReceiverObservation none = capture.RenderDirectionalShadowReceiver(
                    "PureBase/Toon",
                    LightShadows.None
                );
                ShadowReceiverObservation hard = capture.RenderDirectionalShadowReceiver(
                    "PureBase/Toon",
                    LightShadows.Hard
                );
                ShadowReceiverObservation soft = capture.RenderDirectionalShadowReceiver(
                    "PureBase/Toon",
                    LightShadows.Soft
                );

                AssertShadowReceiverFinite(none, "Product Toon None");
                AssertShadowReceiverFinite(hard, "Product Toon Hard");
                AssertShadowReceiverFinite(soft, "Product Toon Soft");
                Assert.That(
                    MaximumRgbDifference(none.meanColor, hard.meanColor),
                    Is.LessThanOrEqualTo(0.02f),
                    "Hard directional visibility must not attenuate Toon direct color or its aggregate direction."
                );
                Assert.That(
                    MaximumRgbDifference(none.meanColor, soft.meanColor),
                    Is.LessThanOrEqualTo(0.02f),
                    "Soft directional visibility must not attenuate Toon direct color or its aggregate direction."
                );
            }
        }

        /// <summary>Requires visibility to leave direct radiance intact while its shadow-weighted aggregate direction crosses the Toon SH band boundary.</summary>
        [Test]
        public void ToonShadowWeightedAggregateDirectionOracleChangesShBandWithoutChangingDirectRadiance()
        {
            ToonShadowInputs visibleInputs = new ToonShadowInputs(0.8f, 0.65f, 1.0f, 0.4f);
            ToonShadowInputs shadowedInputs = new ToonShadowInputs(0.8f, 0.65f, 0.1f, 0.4f);
            ToonShadowObservation visible = EvaluateToonShadowContract(visibleInputs);
            ToonShadowObservation shadowed = EvaluateToonShadowContract(shadowedInputs);
            Vector4 shAr = new Vector4(0.3f, 0.0f, 0.0f, 0.2f);
            Vector3 normal = (Vector3.forward - 0.5f * Vector3.right).normalized;
            Vector3 visibleDirection = EvaluateDominantDirection(
                Vector3.forward * EvaluateShadowWeightedDirectionWeight(visible),
                shAr,
                Vector4.zero,
                Vector4.zero
            );
            Vector3 shadowedDirection = EvaluateDominantDirection(
                Vector3.forward * EvaluateShadowWeightedDirectionWeight(shadowed),
                shAr,
                Vector4.zero,
                Vector4.zero
            );
            Color visibleBand = EvaluateTwoBandSh(normal, visibleDirection, shAr, Vector4.zero, Vector4.zero);
            Color shadowedBand = EvaluateTwoBandSh(normal, shadowedDirection, shAr, Vector4.zero, Vector4.zero);

            Assert.That(shadowed.directRadiance, Is.EqualTo(visible.directRadiance).Within(OracleTolerance));
            Assert.That(EvaluateShadowWeightedDirectionWeight(shadowed), Is.LessThan(EvaluateShadowWeightedDirectionWeight(visible) - 0.02f));
            Assert.That(Vector3.Dot(normal, visibleDirection), Is.GreaterThan(0.0f));
            Assert.That(Vector3.Dot(normal, shadowedDirection), Is.LessThan(0.0f));
            Assert.That(MaximumRgbDifference(visibleBand, shadowedBand), Is.GreaterThan(0.02f));
        }

        /// <summary>Requires a transient Directional Texture2D cookie to preserve white and suppress black contributions without persistent assets.</summary>
        [Test]
        public void ToonDirectionalCookieReadbacksAreSemanticAndTransient()
        {
            Texture2D whiteDirectionalCookie = CreateCookieTexture(Color.white);
            Texture2D blackDirectionalCookie = CreateCookieTexture(Color.clear);
            try
            {
                using (var capture = new ToonLightingCaptureScope())
                {
                    AssertCookieSemanticReadback(
                        capture,
                        new CookieReadbackCase
                        {
                            passName = "ForwardBase",
                            normal = Vector3.forward,
                            lightColor = new Vector4(0.45f, 0.35f, 0.25f, 1.0f),
                            lightPosition = new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                            lightType = LightType.Directional,
                            whiteCookie = whiteDirectionalCookie,
                            blackCookie = blackDirectionalCookie,
                            label = "Directional",
                        }
                    );
                }
            }
            finally
            {
                Object.DestroyImmediate(whiteDirectionalCookie);
                Object.DestroyImmediate(blackDirectionalCookie);
            }
        }

        /// <summary>Requires a transient Point Cubemap cookie to preserve white, suppress black, and retain range attenuation without persistent assets.</summary>
        [Test]
        public void ToonPointCookieReadbacksAreSemanticAndTransient()
        {
            Cubemap whitePointCookie = CreatePointCookie(Color.white);
            Cubemap blackPointCookie = CreatePointCookie(Color.clear);
            try
            {
                using (var capture = new ToonLightingCaptureScope())
                {
                    AssertCookieSemanticReadback(
                        capture,
                        new CookieReadbackCase
                        {
                            passName = "ForwardAdd",
                            normal = Vector3.back,
                            lightColor = new Vector4(0.45f, 0.35f, 0.25f, 1.0f),
                            lightPosition = new Vector4(0.0f, 0.0f, -2.0f, 1.0f),
                            lightType = LightType.Point,
                            whiteCookie = whitePointCookie,
                            blackCookie = blackPointCookie,
                            label = "Point",
                        }
                    );
                    AssertPointWhiteCookieRetainsRangeAttenuation(capture, whitePointCookie);
                }
            }
            finally
            {
                Object.DestroyImmediate(whitePointCookie);
                Object.DestroyImmediate(blackPointCookie);
            }
        }

        /// <summary>Warms each required Unity light-kind form with nonpersistent collections only.</summary>
        [Test]
        public void ProductLightKindsAndApplicableShadowFormsWarmWithoutPersistentVariants()
        {
            Assert.That(WarmAllLightKindVariants(), Is.EqualTo(56));
        }

        /// <summary>Asserts Hard-shadow response for lit models and invariance for the unlit control on one shared receiver route.</summary>
        private static void AssertNonToonShadowControl(ToonLightingCaptureScope capture, string shaderName, bool expectShadowResponse)
        {
            ShadowReceiverObservation none = capture.RenderDirectionalShadowReceiver(shaderName, LightShadows.None);
            ShadowReceiverObservation hard = capture.RenderDirectionalShadowReceiver(shaderName, LightShadows.Hard);
            AssertShadowReceiverFinite(none, shaderName + " None");
            AssertShadowReceiverFinite(hard, shaderName + " Hard");
            if (expectShadowResponse)
            {
                Assert.That(RgbMagnitude(hard.meanColor), Is.LessThan(RgbMagnitude(none.meanColor) - 0.02f), shaderName + " must retain measurable Hard-shadow response.");
                return;
            }

            Assert.That(MaximumRgbDifference(none.meanColor, hard.meanColor), Is.LessThanOrEqualTo(0.02f), shaderName + " must remain invariant to the shared shadow route.");
        }

        /// <summary>Checks the three independently written phase channels against the required visibility boundaries.</summary>
        private static void AssertPhaseVisibilityBoundaries(ShadowReceiverObservation none, ShadowReceiverObservation hard, ShadowReceiverObservation soft)
        {
            foreach (float value in new[] { none.meanColor.r, none.meanColor.g, none.meanColor.b })
            {
                Assert.That(value, Is.EqualTo(1.0f).Within(0.05f));
            }

            foreach (float value in new[] { hard.meanColor.r, hard.meanColor.g, hard.meanColor.b })
            {
                Assert.That(value, Is.LessThanOrEqualTo(0.95f));
            }

            foreach (float value in new[] { soft.meanColor.r, soft.meanColor.g, soft.meanColor.b })
            {
                Assert.That(value, Is.InRange(0.05f, 0.95f));
            }

            Assert.That(hard.meanColor.r, Is.LessThan(none.meanColor.r - 0.02f));
            Assert.That(soft.meanColor.r, Is.LessThan(none.meanColor.r - 0.02f));
        }

        /// <summary>Stores the fixed light and texture inputs for one semantic cookie readback.</summary>
        private sealed class CookieReadbackCase
        {
            /// <summary>Gets or sets the product pass that receives the light.</summary>
            public string passName { get; set; }

            /// <summary>Gets or sets the uniform mesh world normal.</summary>
            public Vector3 normal { get; set; }

            /// <summary>Gets or sets the light color.</summary>
            public Vector4 lightColor { get; set; }

            /// <summary>Gets or sets the directional vector or local-light position.</summary>
            public Vector4 lightPosition { get; set; }

            /// <summary>Gets or sets the real Unity light type.</summary>
            public LightType lightType { get; set; }

            /// <summary>Gets or sets the caller-owned white transmission cookie.</summary>
            public Texture whiteCookie { get; set; }

            /// <summary>Gets or sets the caller-owned black transmission cookie.</summary>
            public Texture blackCookie { get; set; }

            /// <summary>Gets or sets the assertion label.</summary>
            public string label { get; set; }

            /// <summary>Creates one readback request for the supplied caller-owned cookie.</summary>
            /// <param name="cookie">The cookie to apply to the transient Unity light.</param>
            /// <returns>The complete light capture request.</returns>
            public LightCaptureRequest CreateRequest(Texture cookie)
            {
                return new LightCaptureRequest(cookie)
                {
                    normal = normal,
                    lightColor = lightColor,
                    lightPosition = lightPosition,
                    coefficients = ShCoefficients.Zero,
                    lightType = lightType,
                    lightCount = 1,
                };
            }
        }

        /// <summary>Asserts semantic no-cookie, white-cookie, and black-cookie readbacks for one Unity light kind.</summary>
        private static void AssertCookieSemanticReadback(ToonLightingCaptureScope capture, CookieReadbackCase readbackCase)
        {
            Color noCookieReadback = capture.RenderLightWithCookie("PureBase/Toon", readbackCase.passName, readbackCase.CreateRequest(null));
            Color whiteCookieReadback = capture.RenderLightWithCookie("PureBase/Toon", readbackCase.passName, readbackCase.CreateRequest(readbackCase.whiteCookie));
            Color blackCookieReadback = capture.RenderLightWithCookie("PureBase/Toon", readbackCase.passName, readbackCase.CreateRequest(readbackCase.blackCookie));
            AssertFinite(noCookieReadback, readbackCase.label + " no-cookie readback");
            AssertFinite(whiteCookieReadback, readbackCase.label + " white-cookie readback");
            AssertFinite(blackCookieReadback, readbackCase.label + " black-cookie readback");
            Assert.That(MaximumRgbDifference(noCookieReadback, whiteCookieReadback), Is.LessThanOrEqualTo(0.02f), readbackCase.label + " white cookie must preserve the no-cookie contribution.");
            Assert.That(RgbMagnitude(blackCookieReadback), Is.LessThan(RgbMagnitude(whiteCookieReadback) - 0.02f), readbackCase.label + " black cookie must suppress the light contribution.");
            Assert.That(whiteCookieReadback.a, Is.EqualTo(noCookieReadback.a).Within(0.002f), readbackCase.label + " white cookie must preserve destination alpha.");
            Assert.That(blackCookieReadback.a, Is.EqualTo(noCookieReadback.a).Within(0.002f), readbackCase.label + " black cookie must preserve destination alpha.");
        }

        /// <summary>Requires a white Point cubemap cookie to preserve the ordinary finite range falloff.</summary>
        private static void AssertPointWhiteCookieRetainsRangeAttenuation(ToonLightingCaptureScope capture, Cubemap whiteCookie)
        {
            Vector4 color = new Vector4(0.45f, 0.35f, 0.25f, 1.0f);
            Color near = capture.RenderLightWithCookie(
                "PureBase/Toon",
                "ForwardAdd",
                new LightCaptureRequest(whiteCookie)
                {
                    normal = Vector3.back,
                    lightColor = color,
                    lightPosition = new Vector4(0.0f, 0.0f, -2.0f, 1.0f),
                    coefficients = ShCoefficients.Zero,
                    lightType = LightType.Point,
                    lightCount = 1,
                    range = 4.0f,
                }
            );
            Color edge = capture.RenderLightWithCookie(
                "PureBase/Toon",
                "ForwardAdd",
                new LightCaptureRequest(whiteCookie)
                {
                    normal = Vector3.back,
                    lightColor = color,
                    lightPosition = new Vector4(0.0f, 0.0f, -3.8f, 1.0f),
                    coefficients = ShCoefficients.Zero,
                    lightType = LightType.Point,
                    lightCount = 1,
                    range = 4.0f,
                }
            );
            AssertFinite(near, "Point white-cookie near readback");
            AssertFinite(edge, "Point white-cookie range-edge readback");
            Assert.That(RgbMagnitude(edge), Is.LessThan(RgbMagnitude(near) - 0.02f));
            Assert.That(edge.a, Is.EqualTo(near.a).Within(0.002f));
        }

        /// <summary>Creates a transient LDR directional cookie that is never persisted to the AssetDatabase.</summary>
        /// <param name="color">The exact cookie transmission color.</param>
        /// <returns>The caller-owned transient cookie.</returns>
        private static Texture2D CreateCookieTexture(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Creates a transient LDR cubemap cookie for Unity Point-light cookie variants.</summary>
        /// <param name="color">The exact cookie transmission color for every cubemap face.</param>
        /// <returns>The caller-owned transient Point cookie.</returns>
        private static Cubemap CreatePointCookie(Color color)
        {
            var texture = new Cubemap(2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            foreach (CubemapFace face in new[]
            {
                CubemapFace.PositiveX,
                CubemapFace.NegativeX,
                CubemapFace.PositiveY,
                CubemapFace.NegativeY,
                CubemapFace.PositiveZ,
                CubemapFace.NegativeZ,
            })
            {
                texture.SetPixels(new[] { color, color, color, color }, face);
            }

            texture.Apply(false, true);
            return texture;
        }

        /// <summary>Asserts that one region contains finite opaque receiver data.</summary>
        private static void AssertShadowReceiverFinite(ShadowReceiverObservation observation, string label)
        {
            Assert.That(observation.sampleCount, Is.GreaterThan(64), label + " requires a receiver region.");
            AssertFinite(observation.meanColor, label + " mean RGB");
        }

        /// <summary>Asserts that phase-local red, green, and blue diagnostics publish one shadow visibility value.</summary>
        private static void AssertPhaseChannelsAgree(Color value, string label)
        {
            Assert.That(Mathf.Abs(value.r - value.g), Is.LessThanOrEqualTo(0.02f), label + " red and green phase visibility disagree.");
            Assert.That(Mathf.Abs(value.r - value.b), Is.LessThanOrEqualTo(0.02f), label + " red and blue phase visibility disagree.");
        }

        /// <summary>Returns the aggregate direct-light weight after the selected visibility scales it.</summary>
        /// <param name="observation">The split Toon light observation.</param>
        /// <returns>The shadow-weighted aggregate direction weight.</returns>
        private static float EvaluateShadowWeightedDirectionWeight(ToonShadowObservation observation)
        {
            return observation.directionWeight * observation.publishedVisibility;
        }

        /// <summary>Separates the inputs that the Toon host must retain for direct lighting and future shade phases.</summary>
        [SuppressMessage("SonarAnalyzer.CSharp", "S3898", Justification = "Field assertions are the only intended contract for this private test carrier; it has no equality or hash-based use.")]
        private readonly struct ToonShadowInputs
        {
            /// <summary>Initializes one direct-light attenuation and visibility sample.</summary>
            /// <param name="sceneColor">The unattenuated direct scene-light color magnitude.</param>
            /// <param name="nonShadowAttenuation">The distance, cone, and cookie attenuation.</param>
            /// <param name="effectiveVisibility">Unity's effective per-light visibility.</param>
            /// <param name="directionLuminance">The direct luminance used to weight aggregate direction.</param>
            public ToonShadowInputs(float sceneColor, float nonShadowAttenuation, float effectiveVisibility, float directionLuminance)
            {
                this.sceneColor = sceneColor;
                this.nonShadowAttenuation = nonShadowAttenuation;
                this.effectiveVisibility = effectiveVisibility;
                this.directionLuminance = directionLuminance;
            }

            /// <summary>Gets the unattenuated direct scene-light color magnitude.</summary>
            public float sceneColor { get; }

            /// <summary>Gets the non-shadow attenuation.</summary>
            public float nonShadowAttenuation { get; }

            /// <summary>Gets Unity's effective per-light visibility.</summary>
            public float effectiveVisibility { get; }

            /// <summary>Gets the direct luminance used for aggregate direction.</summary>
            public float directionLuminance { get; }
        }

        /// <summary>Stores one evaluated Toon split-light observation.</summary>
        [SuppressMessage("SonarAnalyzer.CSharp", "S3898", Justification = "Field assertions are the only intended contract for this private test carrier; it has no equality or hash-based use.")]
        private readonly struct ToonShadowObservation
        {
            /// <summary>Initializes one evaluated Toon split-light observation.</summary>
            /// <param name="directRadiance">The host-owned direct radiance.</param>
            /// <param name="directionWeight">The host-owned aggregate direction weight.</param>
            /// <param name="publishedVisibility">The visibility published to Toon phases.</param>
            /// <param name="fullAttenuation">The full attenuation retained by PBR and Hybrid.</param>
            public ToonShadowObservation(float directRadiance, float directionWeight, float publishedVisibility, float fullAttenuation)
            {
                this.directRadiance = directRadiance;
                this.directionWeight = directionWeight;
                this.publishedVisibility = publishedVisibility;
                this.fullAttenuation = fullAttenuation;
            }

            /// <summary>Gets the host-owned direct radiance.</summary>
            public float directRadiance { get; }

            /// <summary>Gets the host-owned aggregate direction weight.</summary>
            public float directionWeight { get; }

            /// <summary>Gets the visibility published to Toon phases.</summary>
            public float publishedVisibility { get; }

            /// <summary>Gets full attenuation retained by PBR and Hybrid inputs.</summary>
            public float fullAttenuation { get; }
        }

        /// <summary>Evaluates the intended host contract without coupling the numerical oracle to product HLSL.</summary>
        /// <param name="inputs">The split light inputs to evaluate.</param>
        /// <returns>The resulting Toon and full-attenuation observations.</returns>
        private static ToonShadowObservation EvaluateToonShadowContract(ToonShadowInputs inputs)
        {
            float directRadiance = inputs.sceneColor * inputs.nonShadowAttenuation;
            float directionWeight = inputs.directionLuminance * inputs.nonShadowAttenuation;
            float fullAttenuation = inputs.nonShadowAttenuation * inputs.effectiveVisibility;
            return new ToonShadowObservation(
                directRadiance,
                directionWeight,
                inputs.effectiveVisibility,
                fullAttenuation
            );
        }
    }
}