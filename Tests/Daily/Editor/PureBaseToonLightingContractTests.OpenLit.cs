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

// Defines pure OpenLit 1.0.2 numerical contracts for Toon dominant direction and two-band SH evaluation.

using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines pure OpenLit 1.0.2 numerical contracts for Toon dominant direction and two-band SH evaluation.</summary>
    [SuppressMessage("SonarAnalyzer.CSharp", "S2333", Justification = "This declaration remains partial so the pure OpenLit oracle stays separate from capture and shadow fixtures.")]
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Identifies the fixed forced-Gamma Toon OpenLit diagnostic host.</summary>
        private const string ToonOpenLitGammaShaderName =
            "PureBase/Tests/ShaderCore/ToonOpenLitGamma";

        /// <summary>Identifies the selected forced-Gamma Toon OpenLit diagnostic module.</summary>
        private const string ToonOpenLitGammaModuleId =
            "jp.penguin.purebase.tests.shadercore.toonopenlitgamma";

        /// <summary>Identifies the forced-Gamma Toon OpenLit Shader-Core host asset.</summary>
        private const string ToonOpenLitGammaHostAssetPath =
            "Packages/jp.penguin.purebase/Tests/Fixtures/Hosts/ToonOpenLit/PureBaseTestToonOpenLitGamma.scshader";

        /// <summary>Requires OpenLit direction weighting to use color-space luminance, positive-Y SH, and an unconditional fallback.</summary>
        [Test]
        public void OpenLitDirectionUsesColorSpaceLuminanceShAndFallback()
        {
            Color directColor = new Color(0.7f, 0.2f, 0.1f, 1.0f);
            Vector3 directDirection = Vector3.right;
            Vector4 shAr = new Vector4(0.0f, -0.15f, 0.0f, 0.0f);
            Vector4 shAg = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            Vector4 shAb = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            float gammaLuminance = EvaluateOpenLitLuminance(directColor, true);
            float linearLuminance = EvaluateOpenLitLuminance(directColor, false);
            Vector3 gammaDirection = EvaluateOpenLitDominantDirection(
                EvaluateOpenLitDirectAggregate(directDirection, directColor, true),
                shAr,
                shAg,
                shAb
            );
            Vector3 linearDirection = EvaluateOpenLitDominantDirection(
                EvaluateOpenLitDirectAggregate(directDirection, directColor, false),
                shAr,
                shAg,
                shAb
            );

            Assert.That(gammaLuminance, Is.EqualTo(0.3025f).Within(OracleTolerance));
            Assert.That(linearLuminance, Is.EqualTo(0.11999135f).Within(OracleTolerance));
            Assert.That(gammaLuminance, Is.GreaterThan(linearLuminance + 0.1f));
            Assert.That(gammaDirection.y, Is.GreaterThan(0.0f), "Positive-Y SH direction must be included before normalization.");
            Assert.That(linearDirection.y, Is.GreaterThan(0.0f), "Positive-Y SH direction must be included before normalization.");
            Assert.That(Vector3.Distance(gammaDirection, linearDirection), Is.GreaterThan(0.01f));
            Assert.That(gammaDirection.z, Is.GreaterThan(0.0f), "The fixed fallback must contribute before normalization even for a nonzero aggregate.");
            Assert.That(linearDirection.z, Is.GreaterThan(0.0f), "The fixed fallback must contribute before normalization even for a nonzero aggregate.");
        }

        /// <summary>Requires unscaled bright L0/L2 plus L1 and SH-dominant dark L1 across every Unity SH coefficient input.</summary>
        [Test]
        public void OpenLitTwoBandShUsesUnscaledBrightAndShDominantDarkDirections()
        {
            ShCoefficients coefficients = CreateOpenLitCoefficients();
            Vector3 lightDirection = new Vector3(0.6f, 0.2f, Mathf.Sqrt(0.6f));
            Color bright = EvaluateOpenLitTwoBandSh(lightDirection, lightDirection, coefficients, false);
            Color dark = EvaluateOpenLitTwoBandSh(-lightDirection, lightDirection, coefficients, false);
            Color legacyBright = EvaluateLegacyScaledTwoBandSh(lightDirection, lightDirection, coefficients);
            Color legacyDark = EvaluateLegacyScaledTwoBandSh(-lightDirection, lightDirection, coefficients);

            Assert.That(lightDirection.sqrMagnitude, Is.EqualTo(1.0f).Within(OracleTolerance));
            Assert.That(bright.r, Is.EqualTo(0.7244621f).Within(OracleTolerance));
            Assert.That(bright.g, Is.EqualTo(0.8120943f).Within(OracleTolerance));
            Assert.That(bright.b, Is.EqualTo(1.0022154f).Within(OracleTolerance));
            Assert.That(dark.r, Is.EqualTo(0.7016153f).Within(OracleTolerance));
            Assert.That(dark.g, Is.EqualTo(0.8667072f).Within(OracleTolerance));
            Assert.That(dark.b, Is.EqualTo(1.0255581f).Within(OracleTolerance));
            Assert.That(MaximumRgbDifference(bright, dark), Is.GreaterThan(0.05f));
            Assert.That(MaximumRgbDifference(legacyBright, bright), Is.GreaterThan(0.05f));
            Assert.That(MaximumRgbDifference(legacyDark, dark), Is.GreaterThan(0.05f));
        }

        /// <summary>Requires Linear SH to remain raw and Gamma SH to convert both assembled bands to sRGB.</summary>
        [Test]
        public void OpenLitTwoBandShDefinesLinearAndGammaResults()
        {
            ShCoefficients coefficients = CreateOpenLitCoefficients();
            Vector3 lightDirection = new Vector3(0.6f, 0.2f, Mathf.Sqrt(0.6f));
            Color linearBright = EvaluateOpenLitTwoBandSh(lightDirection, lightDirection, coefficients, false);
            Color linearDark = EvaluateOpenLitTwoBandSh(-lightDirection, lightDirection, coefficients, false);
            Color gammaBright = EvaluateOpenLitTwoBandSh(lightDirection, lightDirection, coefficients, true);
            Color gammaDark = EvaluateOpenLitTwoBandSh(-lightDirection, lightDirection, coefficients, true);

            AssertColor(linearBright, EvaluateOpenLitTwoBandSh(lightDirection, lightDirection, coefficients, false), "OpenLit Linear bright band");
            AssertColor(linearDark, EvaluateOpenLitTwoBandSh(-lightDirection, lightDirection, coefficients, false), "OpenLit Linear dark band");
            Assert.That(gammaBright.r, Is.EqualTo(Mathf.LinearToGammaSpace(linearBright.r)).Within(OracleTolerance));
            Assert.That(gammaBright.g, Is.EqualTo(Mathf.LinearToGammaSpace(linearBright.g)).Within(OracleTolerance));
            Assert.That(gammaBright.b, Is.EqualTo(Mathf.LinearToGammaSpace(linearBright.b)).Within(OracleTolerance));
            Assert.That(gammaDark.r, Is.EqualTo(Mathf.LinearToGammaSpace(linearDark.r)).Within(OracleTolerance));
            Assert.That(gammaDark.g, Is.EqualTo(Mathf.LinearToGammaSpace(linearDark.g)).Within(OracleTolerance));
            Assert.That(gammaDark.b, Is.EqualTo(Mathf.LinearToGammaSpace(linearDark.b)).Within(OracleTolerance));
            Assert.That(MaximumRgbDifference(gammaBright, linearBright), Is.GreaterThan(0.1f));
            Assert.That(MaximumRgbDifference(gammaDark, linearDark), Is.GreaterThan(0.1f));
        }

        /// <summary>Requires finite fallback direction and finite zero dark L1 when all SH first-order direction coefficients are zero.</summary>
        [Test]
        public void OpenLitDegenerateDirectionsRemainFinite()
        {
            Vector3 direction = EvaluateOpenLitDominantDirection(Vector3.zero, Vector4.zero, Vector4.zero, Vector4.zero);
            ShCoefficients coefficients = new ShCoefficients(
                new Vector4(0.0f, 0.0f, 0.0f, 0.2f),
                new Vector4(0.0f, 0.0f, 0.0f, 0.3f),
                new Vector4(0.0f, 0.0f, 0.0f, 0.4f),
                Vector4.zero,
                Vector4.zero,
                Vector4.zero,
                Vector4.zero
            );
            Color dark = EvaluateOpenLitTwoBandSh(-direction, direction, coefficients, false);

            AssertVector(new Vector3(0.40824829f, 0.81649658f, 0.40824829f), direction, "OpenLit fallback direction");
            Assert.That(IsFinite(direction), Is.True);
            AssertFinite(dark, "OpenLit zero SH-dark direction");
            AssertColor(new Color(0.2f, 0.3f, 0.4f, 1.0f), dark, "OpenLit zero SH-dark L1");
        }

        /// <summary>Requires the Linear D3D11 product capture to follow the OpenLit oracle for top, side, and bottom samples across changed full SH inputs.</summary>
        [Test]
        public void ToonLinearD3D11RuntimeMatchesOpenLitTopSideBottomAndChangedShInputs()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.EqualTo(GraphicsDeviceType.Direct3D11));
            Assert.That(QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Linear));

            ShCoefficients firstCoefficients = CreateOpenLitCoefficients();
            ShCoefficients secondCoefficients = CreateChangedOpenLitCoefficients();
            AssertLinearRuntimeBands(firstCoefficients, "first OpenLit SH set");
            AssertLinearRuntimeBands(secondCoefficients, "second OpenLit SH set");
        }

        /// <summary>Requires the fixed host to compile and read back the product Gamma branch against the Gamma OpenLit oracle.</summary>
        [Test]
        public void ToonOpenLitGammaHostReadbackMatchesGammaOracle()
        {
            ShCoefficients coefficients = CreateOpenLitCoefficients();
            Vector3 lightDirection = EvaluateOpenLitDominantDirection(
                Vector3.zero,
                coefficients.ar,
                coefficients.ag,
                coefficients.ab
            );
            Color expected = EvaluateOpenLitTwoBandSh(
                lightDirection,
                lightDirection,
                coefficients,
                true
            );

            using (
                var selection = new ToonShadowHostSelectionScope(
                    ToonOpenLitGammaShaderName,
                    ToonOpenLitGammaModuleId,
                    ToonOpenLitGammaHostAssetPath
                )
            )
            using (var capture = new ToonLightingCaptureScope())
            {
                AssertImportedToonOpenLitGammaHost();
                Color actual = capture.Render(
                    ToonOpenLitGammaShaderName,
                    "ForwardBase",
                    lightDirection,
                    Vector4.zero,
                    new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f),
                    coefficients
                );

                AssertFinite(actual, "Toon OpenLit Gamma ForwardBase readback");
                AssertColor(expected, actual, "Toon OpenLit Gamma ForwardBase readback");
            }
        }

        /// <summary>Requires the selected ForwardAdd diagnostic to publish only normalized direct direction or zero, independent of injected SH.</summary>
        [Test]
        public void ToonOpenLitForwardAddDiagnosticDecodesDirectOnlyDirectionForNonzeroAndZeroAggregates()
        {
            Vector4 coloredDirectLight = new Vector4(0.3f, 0.6f, 0.2f, 1.0f);
            Vector3 expectedDirection = Vector3.forward;

            using (
                var selection = new ToonShadowHostSelectionScope(
                    ToonOpenLitGammaShaderName,
                    ToonOpenLitGammaModuleId,
                    ToonOpenLitGammaHostAssetPath
                )
            )
            using (var capture = new ToonLightingCaptureScope())
            {
                AssertImportedToonOpenLitGammaHost();
                Vector3 firstDirection = DecodeToonOpenLitForwardAddDirection(
                    RenderToonOpenLitForwardAddDiagnostic(
                        capture,
                        coloredDirectLight,
                        CreateOpenLitCoefficients()
                    )
                );
                Vector3 secondDirection = DecodeToonOpenLitForwardAddDirection(
                    RenderToonOpenLitForwardAddDiagnostic(
                        capture,
                        coloredDirectLight,
                        CreateChangedOpenLitCoefficients()
                    )
                );
                Vector3 zeroDirection = DecodeToonOpenLitForwardAddDirection(
                    RenderToonOpenLitForwardAddDiagnostic(
                        capture,
                        Vector4.zero,
                        CreateOpenLitCoefficients()
                    )
                );

                AssertVector(expectedDirection, firstDirection, "Toon OpenLit ForwardAdd first SH direction");
                AssertVector(expectedDirection, secondDirection, "Toon OpenLit ForwardAdd second SH direction");
                AssertVector(Vector3.zero, zeroDirection, "Toon OpenLit ForwardAdd zero direct direction");
            }
        }

        /// <summary>Renders one full SH input set at top, side, and bottom normals and compares the product readbacks with the Linear OpenLit oracle.</summary>
        private static void AssertLinearRuntimeBands(ShCoefficients coefficients, string label)
        {
            Vector3 lightDirection = EvaluateOpenLitDominantDirection(
                Vector3.zero,
                coefficients.ar,
                coefficients.ag,
                coefficients.ab
            );
            using (var capture = new ToonLightingCaptureScope())
            {
                AssertLinearRuntimeBand(capture, lightDirection, lightDirection, coefficients, label + " top");
                AssertLinearRuntimeBand(capture, Vector3.right, lightDirection, coefficients, label + " side");
                AssertLinearRuntimeBand(capture, -lightDirection, lightDirection, coefficients, label + " bottom");
            }
        }

        /// <summary>Renders one Linear product SH sample and compares it with the unconverted OpenLit reference band.</summary>
        private static void AssertLinearRuntimeBand(
            ToonLightingCaptureScope capture,
            Vector3 normal,
            Vector3 lightDirection,
            ShCoefficients coefficients,
            string label
        )
        {
            Color expected = EvaluateOpenLitTwoBandSh(normal, lightDirection, coefficients, false);
            Color actual = capture.Render(
                "PureBase/Toon",
                "ForwardBase",
                normal,
                Vector4.zero,
                new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f),
                coefficients
            );
            AssertFinite(actual, label + " readback");
            AssertRuntimeColor(expected, actual, label + " readback");
        }

        /// <summary>Creates a second full SH input set whose first-order, L2, and C vectors differ from the primary OpenLit fixture.</summary>
        private static ShCoefficients CreateChangedOpenLitCoefficients()
        {
            return new ShCoefficients(
                new Vector4(-0.15f, 0.12f, 0.08f, 0.31f),
                new Vector4(0.11f, -0.07f, 0.18f, 0.46f),
                new Vector4(0.04f, 0.16f, -0.13f, 0.57f),
                new Vector4(0.09f, -0.05f, 0.12f, 0.06f),
                new Vector4(-0.04f, 0.14f, 0.07f, 0.02f),
                new Vector4(0.13f, 0.01f, -0.08f, 0.05f),
                new Vector4(0.06f, -0.02f, 0.04f, 0.0f)
            );
        }

        /// <summary>Checks that the temporary forced-Gamma selection produced a supported compiler-clean shader.</summary>
        private static void AssertImportedToonOpenLitGammaHost()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ToonOpenLitGammaHostAssetPath);
            Assert.That(shader, Is.Not.Null, "The temporary Toon OpenLit Gamma host import did not produce a shader.");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, "The temporary Toon OpenLit Gamma host import produced shader compiler errors.");
            Assert.That(shader.isSupported, Is.True, "The temporary Toon OpenLit Gamma host shader is unsupported.");
        }

        /// <summary>Decodes the host Shade diagnostic from its centered RGB representation.</summary>
        private static Vector3 DecodeToonOpenLitForwardAddDirection(Color readback)
        {
            return new Vector3(readback.r, readback.g, readback.b) * 2.0f - Vector3.one;
        }

        /// <summary>Builds the fixed nonzero coefficient set used to exercise every Unity SH input vector.</summary>
        /// <returns>A seven-vector coefficient set with distinct linear and quadratic contributions.</returns>
        private static ShCoefficients CreateOpenLitCoefficients()
        {
            return new ShCoefficients(
                new Vector4(0.1f, 0.05f, 0.2f, 0.4f),
                new Vector4(0.05f, 0.2f, 0.1f, 0.5f),
                new Vector4(0.2f, 0.1f, 0.05f, 0.6f),
                new Vector4(0.08f, 0.03f, 0.1f, 0.02f),
                new Vector4(0.02f, 0.1f, 0.2f, 0.03f),
                new Vector4(0.05f, 0.06f, 0.3f, 0.04f),
                new Vector4(0.05f, 0.04f, 0.03f, 0.0f)
            );
        }

        /// <summary>Evaluates OpenLit luminance for the requested output color-space branch.</summary>
        /// <param name="color">The direct light RGB value.</param>
        /// <param name="isGamma">Whether the Gamma coefficient set applies.</param>
        /// <returns>The OpenLit luminance weight.</returns>
        private static float EvaluateOpenLitLuminance(Color color, bool isGamma)
        {
            Vector3 coefficients = isGamma
                ? new Vector3(0.22f, 0.707f, 0.071f)
                : new Vector3(0.0396819152f, 0.458021790f, 0.00609653955f);
            return Vector3.Dot(new Vector3(color.r, color.g, color.b), coefficients);
        }

        /// <summary>Builds the OpenLit direct aggregate from a light direction and color-space luminance weight.</summary>
        /// <param name="lightDirection">The unnormalized direct-light direction.</param>
        /// <param name="lightColor">The direct light RGB value.</param>
        /// <param name="isGamma">Whether the Gamma luminance coefficients apply.</param>
        /// <returns>The luminance-weighted direct direction aggregate.</returns>
        private static Vector3 EvaluateOpenLitDirectAggregate(Vector3 lightDirection, Color lightColor, bool isGamma)
        {
            return lightDirection * EvaluateOpenLitLuminance(lightColor, isGamma);
        }

        /// <summary>Evaluates the fallback-inclusive OpenLit dominant direction from an already weighted direct aggregate.</summary>
        /// <param name="directAggregateDirection">The post-light luminance-weighted direct direction aggregate.</param>
        /// <param name="shAr">The Unity red first-order SH coefficient vector.</param>
        /// <param name="shAg">The Unity green first-order SH coefficient vector.</param>
        /// <param name="shAb">The Unity blue first-order SH coefficient vector.</param>
        /// <returns>The finite normalized dominant direction.</returns>
        private static Vector3 EvaluateOpenLitDominantDirection(Vector3 directAggregateDirection, Vector4 shAr, Vector4 shAg, Vector4 shAb)
        {
            Vector3 shDirection = (new Vector3(shAr.x, shAr.y, shAr.z)
                    + new Vector3(shAg.x, shAg.y, shAg.z)
                    + new Vector3(shAb.x, shAb.y, shAb.z))
                / 3.0f;
            Vector3 directionVector = directAggregateDirection
                + new Vector3(shDirection.x, Mathf.Abs(shDirection.y), shDirection.z)
                + new Vector3(0.001f, 0.002f, 0.001f);
            return directionVector.normalized;
        }

        /// <summary>Evaluates the unscaled OpenLit L0/L2 SH base term.</summary>
        /// <param name="evaluationDirection">The unscaled OpenLit V direction.</param>
        /// <param name="coefficients">All Unity SH coefficient vectors.</param>
        /// <returns>The L0/L2 base term shared by both bands.</returns>
        private static Vector3 EvaluateOpenLitL0L2Base(Vector3 evaluationDirection, ShCoefficients coefficients)
        {
            Vector4 quadratic = new Vector4(
                evaluationDirection.x * evaluationDirection.y,
                evaluationDirection.y * evaluationDirection.z,
                evaluationDirection.z * evaluationDirection.z,
                evaluationDirection.z * evaluationDirection.x
            );
            return new Vector3(coefficients.ar.w, coefficients.ag.w, coefficients.ab.w)
                + new Vector3(
                    Vector4.Dot(coefficients.br, quadratic),
                    Vector4.Dot(coefficients.bg, quadratic),
                    Vector4.Dot(coefficients.bb, quadratic)
                )
                + new Vector3(coefficients.c.x, coefficients.c.y, coefficients.c.z)
                    * (evaluationDirection.x * evaluationDirection.x - evaluationDirection.y * evaluationDirection.y);
        }

        /// <summary>Evaluates the OpenLit bright L1 SH term along the unscaled V direction.</summary>
        /// <param name="evaluationDirection">The unscaled OpenLit V direction.</param>
        /// <param name="coefficients">All Unity SH coefficient vectors.</param>
        /// <returns>The bright-band L1 term.</returns>
        private static Vector3 EvaluateOpenLitBrightL1(Vector3 evaluationDirection, ShCoefficients coefficients)
        {
            return EvaluateOpenLitL1(evaluationDirection, coefficients);
        }

        /// <summary>Evaluates the finite OpenLit dark L1 SH term along the normalized summed first-order coefficients.</summary>
        /// <param name="coefficients">All Unity SH coefficient vectors.</param>
        /// <returns>The dark-band L1 term, or zero when the SH direction is degenerate.</returns>
        private static Vector3 EvaluateOpenLitDarkL1(ShCoefficients coefficients)
        {
            Vector3 shDirection = new Vector3(coefficients.ar.x, coefficients.ar.y, coefficients.ar.z)
                + new Vector3(coefficients.ag.x, coefficients.ag.y, coefficients.ag.z)
                + new Vector3(coefficients.ab.x, coefficients.ab.y, coefficients.ab.z);
            if (!IsFinite(shDirection) || (shDirection.x == 0.0f && shDirection.y == 0.0f && shDirection.z == 0.0f))
            {
                return Vector3.zero;
            }

            return EvaluateOpenLitL1(shDirection.normalized, coefficients);
        }

        /// <summary>Evaluates the selected OpenLit bright or dark SH band and performs Gamma-only linear-to-sRGB conversion.</summary>
        /// <param name="surfaceNormal">The surface normal that selects the binary band.</param>
        /// <param name="lightDirection">The OpenLit V direction.</param>
        /// <param name="coefficients">All Unity SH coefficient vectors.</param>
        /// <param name="isGamma">Whether Gamma conversion applies after both bands are assembled.</param>
        /// <returns>The selected OpenLit SH color.</returns>
        private static Color EvaluateOpenLitTwoBandSh(Vector3 surfaceNormal, Vector3 lightDirection, ShCoefficients coefficients, bool isGamma)
        {
            Vector3 baseTerm = EvaluateOpenLitL0L2Base(lightDirection, coefficients);
            Vector3 bright = baseTerm + EvaluateOpenLitBrightL1(lightDirection, coefficients);
            Vector3 dark = baseTerm + EvaluateOpenLitDarkL1(coefficients);
            if (isGamma)
            {
                bright = ConvertLinearToSrgb(bright);
                dark = ConvertLinearToSrgb(dark);
            }

            Vector3 selected = Vector3.Dot(surfaceNormal, lightDirection) >= 0.0f ? bright : dark;
            return new Color(selected.x, selected.y, selected.z, 1.0f);
        }

        /// <summary>Evaluates the shared OpenLit first-order SH term along one supplied direction.</summary>
        /// <param name="direction">The direction used to evaluate L1.</param>
        /// <param name="coefficients">All Unity SH coefficient vectors.</param>
        /// <returns>The three-channel L1 term.</returns>
        private static Vector3 EvaluateOpenLitL1(Vector3 direction, ShCoefficients coefficients)
        {
            return new Vector3(
                Vector3.Dot(new Vector3(coefficients.ar.x, coefficients.ar.y, coefficients.ar.z), direction),
                Vector3.Dot(new Vector3(coefficients.ag.x, coefficients.ag.y, coefficients.ag.z), direction),
                Vector3.Dot(new Vector3(coefficients.ab.x, coefficients.ab.y, coefficients.ab.z), direction)
            );
        }

        /// <summary>Converts a three-channel Linear SH value to Unity-compatible sRGB.</summary>
        /// <param name="value">The assembled Linear SH value.</param>
        /// <returns>The component-wise sRGB value.</returns>
        private static Vector3 ConvertLinearToSrgb(Vector3 value)
        {
            return new Vector3(
                Mathf.LinearToGammaSpace(value.x),
                Mathf.LinearToGammaSpace(value.y),
                Mathf.LinearToGammaSpace(value.z)
            );
        }

        /// <summary>Evaluates the obsolete scaled and inverted two-band approximation for contrast with the OpenLit reference.</summary>
        /// <param name="surfaceNormal">The surface normal that selects the binary band.</param>
        /// <param name="lightDirection">The dominant light direction.</param>
        /// <param name="coefficients">All Unity SH coefficient vectors.</param>
        /// <returns>The obsolete selected SH approximation.</returns>
        private static Color EvaluateLegacyScaledTwoBandSh(Vector3 surfaceNormal, Vector3 lightDirection, ShCoefficients coefficients)
        {
            Vector3 evaluationDirection = lightDirection * 0.666666f;
            Vector3 baseTerm = EvaluateOpenLitL0L2Base(evaluationDirection, coefficients);
            Vector3 linearTerm = EvaluateOpenLitBrightL1(evaluationDirection, coefficients);
            Vector3 selected = Vector3.Dot(surfaceNormal, lightDirection) >= 0.0f
                ? Vector3.Max(baseTerm + linearTerm, Vector3.zero)
                : Vector3.Max(baseTerm - linearTerm, Vector3.zero);
            return new Color(selected.x, selected.y, selected.z, 1.0f);
        }
    }
}
