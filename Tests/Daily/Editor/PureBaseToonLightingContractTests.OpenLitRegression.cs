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

// Defines focused OpenLit regression and runtime-readback contracts for Toon lighting.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines focused OpenLit regression and runtime-readback contracts for Toon lighting.</summary>
    [SuppressMessage("SonarAnalyzer.CSharp", "S2333", Justification = "This declaration remains partial so focused OpenLit regressions stay separate from the numerical oracle.")]
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Defines the D3D11 Linear product readback tolerance for the Shader-Core half and float execution boundary.</summary>
        private const float RuntimeReadbackTolerance = 0.002f;

        /// <summary>Identifies the product Toon lighting include relative to the Unity project root.</summary>
        private const string ToonLightingRelativePath = "Packages/jp.penguin.purebase/Shaders/Common/toon_lighting.hlsl";

        /// <summary>Identifies the product BIRP host include relative to the Unity project root.</summary>
        private const string BirpHostRelativePath = "Packages/jp.penguin.purebase/Shaders/Common/birp_host.hlsl";

        /// <summary>Requires finite nonzero direct and SH cancellation residuals to normalize without being replaced by the fallback direction.</summary>
        [Test]
        public void OpenLitDirectionNormalizesFiniteNonzeroCancellationResidual()
        {
            Vector3 fallbackDirection = new Vector3(0.001f, 0.002f, 0.001f);
            Vector3 directAggregateDirection = -fallbackDirection + new Vector3(0.0005f, 0.0f, 0.0f);
            Vector3 actual = EvaluateOpenLitDominantDirection(
                directAggregateDirection,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero
            );
            string source = ReadPackageSource(ToonLightingRelativePath);

            AssertVector(Vector3.right, actual, "OpenLit near-cancellation residual direction");
            StringAssert.Contains("if (all(directionVector == 0) || !all(isfinite(directionVector)))", source);
            StringAssert.DoesNotContain("dot(directionVector, directionVector) <= 0.000001", source);
        }

        /// <summary>Requires every finite nonzero summed first-order SH direction to contribute to the OpenLit dark L1 band.</summary>
        [Test]
        public void OpenLitDarkL1NormalizesFiniteNearCancellation()
        {
            ShCoefficients coefficients = new ShCoefficients(
                new Vector4(1.0f, 0.0f, 0.0f, 0.0f),
                new Vector4(-0.9995f, 0.0f, 0.0f, 0.0f),
                Vector4.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero
            );
            Vector3 expectedDarkL1 = new Vector3(1.0f, -0.9995f, 0.0f);
            Vector3 darkL1 = EvaluateOpenLitDarkL1(coefficients);
            Color darkBand = EvaluateOpenLitTwoBandSh(-Vector3.right, Vector3.right, coefficients, false);

            AssertVector(expectedDarkL1, darkL1, "OpenLit finite near-cancellation dark L1");
            AssertColor(new Color(expectedDarkL1.x, expectedDarkL1.y, expectedDarkL1.z, 1.0f), darkBand, "OpenLit finite near-cancellation dark band");
        }

        /// <summary>Requires Toon ForwardBase lightmap variants to retain fallback-inclusive direct direction without adding Toon SH direction or ambient bands.</summary>
        [Test]
        public void ToonForwardBaseLightmapPublishesFallbackDirectDirectionWithoutToonShBand()
        {
            string source = ReadPackageSource(BirpHostRelativePath);

            AssertForwardBaseDirectDirectionPrecedesToonShGate(source, "LIGHTMAP_ON");
        }

        /// <summary>Requires Toon ForwardBase SH-disabled variants to retain fallback-inclusive direct direction without adding Toon SH direction or ambient bands.</summary>
        [Test]
        public void ToonForwardBaseShDisabledPublishesFallbackDirectDirectionWithoutToonShBand()
        {
            string source = ReadPackageSource(BirpHostRelativePath);

            AssertForwardBaseDirectDirectionPrecedesToonShGate(source, "UNITY_SHOULD_SAMPLE_SH");
        }

        /// <summary>Renders one ForwardAdd diagnostic using the selected host without changing any persistent Shader-Core selection.</summary>
        /// <param name="capture">The isolated Toon lighting capture.</param>
        /// <param name="lightColor">The diagnostic directional light color.</param>
        /// <param name="coefficients">The injected spherical-harmonic coefficients.</param>
        /// <returns>The isolated ForwardAdd diagnostic readback.</returns>
        private static Color RenderToonOpenLitForwardAddDiagnostic(
            ToonLightingCaptureScope capture,
            Vector4 lightColor,
            ShCoefficients coefficients
        )
        {
            return capture.RenderForwardAddLightDifference(
                ToonOpenLitGammaShaderName,
                new LightCaptureRequest
                {
                    normal = Vector3.forward,
                    lightColor = lightColor,
                    lightPosition = new Vector4(0.0f, 0.0f, 1.0f, 0.0f),
                    coefficients = coefficients,
                    lightType = LightType.Directional,
                    lightCount = lightColor == Vector4.zero ? 0 : 1,
                }
            );
        }

        /// <summary>Asserts the fallback direct direction is assigned before the gate that permits Toon SH direction and ambient evaluation.</summary>
        /// <param name="source">The BIRP host source.</param>
        /// <param name="disabledFeature">The disabled feature whose gate must retain direct direction.</param>
        private static void AssertForwardBaseDirectDirectionPrecedesToonShGate(string source, string disabledFeature)
        {
            const string directAssignment = "sd.L = SCModelSelectAggregateLightDirection(lightSum.direction, half4(0, 0, 0, 0), half4(0, 0, 0, 0), half4(0, 0, 0, 0));";
            const string toonShGate = "#if !defined(LIGHTMAP_ON) && UNITY_SHOULD_SAMPLE_SH";
            const string toonShDirection = "sd.L = SCModelSelectAggregateLightDirection(lightSum.direction, shAr, shAg, shAb);";
            const string toonAmbientBand = "env += SCModelEvaluateAmbient(sd, shAr, shAg, shAb, shBr, shBg, shBb, shC);";
            int directIndex = source.IndexOf(directAssignment, StringComparison.Ordinal);
            int gateIndex = source.IndexOf(toonShGate, StringComparison.Ordinal);
            int shDirectionIndex = source.IndexOf(toonShDirection, StringComparison.Ordinal);
            int ambientBandIndex = source.IndexOf(toonAmbientBand, StringComparison.Ordinal);

            StringAssert.Contains(disabledFeature, toonShGate);
            Assert.That(
                source.IndexOf("unity_SH", StringComparison.Ordinal),
                Is.LessThan(0),
                "Toon SH evaluation must use supplied parameters instead of Unity SH globals."
            );
            Assert.That(directIndex, Is.GreaterThanOrEqualTo(0), "ForwardBase must initialize sd.L from the fallback-inclusive direct aggregate.");
            Assert.That(gateIndex, Is.GreaterThan(directIndex), "Only Toon SH augmentation may be gated after the direct direction is published.");
            Assert.That(shDirectionIndex, Is.GreaterThan(gateIndex), "Toon SH direction must remain inside the SH gate.");
            Assert.That(ambientBandIndex, Is.GreaterThan(gateIndex), "Toon ambient band evaluation must remain inside the SH gate.");
        }

        /// <summary>Asserts a Linear product readback using the bounded D3D11 Shader-Core half and float tolerance.</summary>
        /// <param name="expected">The oracle color.</param>
        /// <param name="actual">The product readback color.</param>
        /// <param name="label">The diagnostic assertion label.</param>
        private static void AssertRuntimeColor(Color expected, Color actual, string label)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(RuntimeReadbackTolerance), label + " red");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(RuntimeReadbackTolerance), label + " green");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(RuntimeReadbackTolerance), label + " blue");
        }

        /// <summary>Reads one package-owned source file from Unity's project root.</summary>
        /// <param name="relativePath">The slash-separated path relative to the Unity project root.</param>
        /// <returns>The source file contents.</returns>
        private static string ReadPackageSource(string relativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projectRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.That(File.Exists(path), Is.True, "Required package source file is missing: " + relativePath);
            return File.ReadAllText(path);
        }
    }
}
