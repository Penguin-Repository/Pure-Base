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
using System.Diagnostics.CodeAnalysis;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines fixed Toon lighting contracts independently of product HLSL implementation details.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Defines the tolerance for fixed half/float-compatible lighting reference values.</summary>
        private const float OracleTolerance = 0.0002f;

        /// <summary>Identifies the global keyword that selects the ForwardAdd point-light variant.</summary>
        private const string PointKeyword = "POINT";

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
                Color pbr = RenderDirectObservation(capture, "PureBase/PBR", Vector3.forward, directLight, directionalPosition, ShCoefficients.Zero, 1.0f);
                Color hybridWithoutSh = RenderDirectObservation(capture, "PureBase/Hybrid", Vector3.forward, directLight, directionalPosition, ShCoefficients.Zero, 1.0f);
                Color hybridWithSh = RenderDirectObservation(capture, "PureBase/Hybrid", Vector3.forward, directLight, directionalPosition, ShCoefficients.FixedOracle, 1.0f);
                Color hybridBackFace = RenderDirectObservation(capture, "PureBase/Hybrid", Vector3.back, directLight, directionalPosition, ShCoefficients.Zero, 0.0f);
                Color hybridFrontFace = RenderDirectObservation(capture, "PureBase/Hybrid", Vector3.forward, directLight, directionalPosition, ShCoefficients.Zero, 0.0f);

                AssertFinite(pbr, "PBR metallic-one direct-specular readback");
                AssertFinite(hybridWithoutSh, "Hybrid metallic-one direct-specular readback");
                AssertFinite(hybridWithSh, "Hybrid metallic-one SH-injected readback");
                AssertMetallicOneDirectEquivalence(pbr, hybridWithoutSh);
                AssertInjectedShIsolation(hybridWithoutSh, hybridWithSh);
                AssertNonmetallicBinaryDirectResponse(hybridBackFace, hybridFrontFace);
            }
        }

        /// <summary>Renders one controlled ForwardBase direct-light observation.</summary>
        private static Color RenderDirectObservation(ToonLightingCaptureScope capture, string shaderName, Vector3 normal, Vector4 lightColor, Vector4 lightPosition, ShCoefficients coefficients, float metallic)
        {
            return capture.Render(shaderName, "ForwardBase", normal, lightColor, lightPosition, coefficients, false, metallic);
        }

        /// <summary>Asserts metallic-one Hybrid direct GGX remains equivalent to PBR.</summary>
        private static void AssertMetallicOneDirectEquivalence(Color pbr, Color hybrid)
        {
            Assert.That(MaximumRgbDifference(pbr, hybrid), Is.LessThanOrEqualTo(0.01f), "PBR and Hybrid metallic-one direct GGX must remain equivalent.");
        }

        /// <summary>Asserts injected Toon SH does not contribute to Hybrid direct GGX.</summary>
        private static void AssertInjectedShIsolation(Color withoutSh, Color withSh)
        {
            Assert.That(MaximumRgbDifference(withoutSh, withSh), Is.LessThanOrEqualTo(0.01f), "Injected Toon SH must not enter Hybrid direct GGX.");
        }

        /// <summary>Asserts nonmetallic Hybrid retains its binary direct-diffuse response.</summary>
        private static void AssertNonmetallicBinaryDirectResponse(Color backFace, Color frontFace)
        {
            Assert.That(RgbMagnitude(frontFace), Is.GreaterThan(RgbMagnitude(backFace) + 0.01f), "Nonmetallic Hybrid must retain the binary direct-diffuse response.");
        }

        /// <summary>Stores the seven Unity SH global vectors injected immediately before each explicit draw.</summary>
        [SuppressMessage(
            "Major Code Smell",
            "S3898:Implement this method because it is defined in 'ValueType'.",
            Justification = "This private immutable Unity-global input carrier is never compared or used as a hash key."
        )]
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

        /// <summary>Owns one isolated explicit-draw fixture, restores every Unity global it changes, and preserves the established capture type while the runtime implementation resides in a partial source file.</summary>
        private sealed class ToonLightingCaptureScope : ToonLightingCaptureRuntimeScope
        {
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
