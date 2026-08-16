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

// Defines direct-diffuse brightness contracts for the PBR and Hybrid product shaders.

using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines isolated product contracts for opt-in Unity Standard direct-diffuse brightness.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Lists the product shaders that expose the opt-in PBR direct-diffuse brightness ABI.</summary>
        private static readonly string[] PbrBrightnessShaderNames = { "PureBase/PBR", "PureBase/Hybrid" };

        /// <summary>Requires the direct diffuse differential to select Unity Standard brightness in both forward paths.</summary>
        [Test]
        public void PbrAndHybridDirectDiffuseBrightnessScalesByPiInForwardBaseAndForwardAdd()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                foreach (string shaderName in PbrBrightnessShaderNames)
                {
                    foreach (string passName in new[] { "ForwardBase", "ForwardAdd" })
                    {
                        Color disabled = capture.RenderPbrBrightnessDiffuseDifferential(shaderName, passName, false);
                        Color enabled = capture.RenderPbrBrightnessDiffuseDifferential(shaderName, passName, true);
                        AssertFiniteNonBlack(disabled, shaderName + " " + passName + " disabled diffuse differential");
                        AssertScaledByPi(disabled, enabled, shaderName + " " + passName + " direct diffuse differential");
                    }
                }
            }
        }

        /// <summary>Compares only the enabled PBR ForwardAdd diffuse differential with Unity Standard at normal incidence.</summary>
        [Test]
        public void PbrForwardAddEnabledDiffuseMatchesStandardAfterDielectricSplitCorrection()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                Color pureBase = capture.RenderPbrBrightnessDiffuseDifferential("PureBase/PBR", "ForwardAdd", true);
                Color standard = capture.RenderStandardForwardAddDiffuseDifferential();
                Color correctedStandard = standard / 0.96f;
                AssertFiniteNonBlack(pureBase, "Pure Base enabled PBR ForwardAdd diffuse differential");
                AssertFiniteNonBlack(correctedStandard, "Unity Standard corrected ForwardAdd diffuse differential");
                AssertColorWithin(pureBase, correctedStandard, 0.02f, "Unity Standard normal-incidence diffuse comparison");
            }
        }

        /// <summary>Requires direct GGX specular to remain unchanged at metallic one in both forward paths.</summary>
        [Test]
        public void PbrAndHybridMetallicOneDirectSpecularIsInvariantAcrossBrightnessToggle()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                foreach (string shaderName in PbrBrightnessShaderNames)
                {
                    foreach (string passName in new[] { "ForwardBase", "ForwardAdd" })
                    {
                        Color disabled = capture.RenderPbrBrightnessMetallicOne(shaderName, passName, false);
                        Color enabled = capture.RenderPbrBrightnessMetallicOne(shaderName, passName, true);
                        AssertFiniteNonBlack(disabled, shaderName + " " + passName + " metallic-one control");
                        AssertColorWithin(disabled, enabled, 0.002f, shaderName + " " + passName + " metallic-one direct specular");
                    }
                }
            }
        }

        /// <summary>Requires non-black custom SH diffuse and custom reflection-probe specular to ignore the direct-only toggle.</summary>
        [Test]
        public void PbrAndHybridIndirectDiffuseAndReflectionSpecularRemainInvariantAndNonBlack()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                foreach (string shaderName in PbrBrightnessShaderNames)
                {
                    Color indirectDisabled = capture.RenderPbrBrightnessIndirectDiffuse(shaderName, false);
                    Color indirectEnabled = capture.RenderPbrBrightnessIndirectDiffuse(shaderName, true);
                    AssertFiniteNonBlack(indirectDisabled, shaderName + " custom SH indirect diffuse");
                    AssertColorWithin(indirectDisabled, indirectEnabled, 0.002f, shaderName + " custom SH indirect diffuse");

                    Color reflectionDisabled = capture.RenderPbrBrightnessReflectionSpecular(shaderName, false);
                    Color reflectionEnabled = capture.RenderPbrBrightnessReflectionSpecular(shaderName, true);
                    AssertFiniteNonBlack(reflectionDisabled, shaderName + " custom reflection indirect specular");
                    AssertColorWithin(reflectionDisabled, reflectionEnabled, 0.002f, shaderName + " custom reflection indirect specular");
                }
            }
        }

        /// <summary>Requires the reflection globals changed by the fixture to be restored after an indirect-specular observation.</summary>
        [Test]
        public void PbrBrightnessReflectionFixtureRestoresCallerState()
        {
            DefaultReflectionMode mode = RenderSettings.defaultReflectionMode;
            Texture reflection = RenderSettings.customReflectionTexture;
            float intensity = RenderSettings.reflectionIntensity;
            using (var capture = new ToonLightingCaptureScope())
            {
                AssertFiniteNonBlack(capture.RenderPbrBrightnessReflectionSpecular("PureBase/PBR", true), "reflection restoration control");
            }

            Assert.That(RenderSettings.defaultReflectionMode, Is.EqualTo(mode));
            Assert.That(RenderSettings.customReflectionTexture, Is.EqualTo(reflection));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(intensity));
        }

        /// <summary>Computes the dielectric diffuse differential after cancelling identical metallic-one specular.</summary>
        /// <param name="metallicZero">The metallic-zero product observation.</param>
        /// <param name="metallicOne">The metallic-one product observation.</param>
        /// <returns>The isolated direct diffuse observation.</returns>
        private static Color Subtract(Color metallicZero, Color metallicOne)
        {
            return metallicZero - metallicOne;
        }

        /// <summary>Requires an enabled direct-diffuse observation to equal the disabled observation times pi.</summary>
        /// <param name="disabled">The physical-normalization observation.</param>
        /// <param name="enabled">The Unity Standard-normalization observation.</param>
        /// <param name="label">The diagnostic observation label.</param>
        private static void AssertScaledByPi(Color disabled, Color enabled, string label)
        {
            Color expected = disabled * Mathf.PI;
            AssertColorWithin(expected, enabled, 0.01f, label);
        }

        /// <summary>Requires finite RGB output with enough energy to discriminate a black indirect control.</summary>
        /// <param name="color">The observed linear output.</param>
        /// <param name="label">The diagnostic observation label.</param>
        private static void AssertFiniteNonBlack(Color color, string label)
        {
            Assert.That(float.IsFinite(color.r) && float.IsFinite(color.g) && float.IsFinite(color.b), Is.True, label + " is non-finite.");
            Assert.That(color.r + color.g + color.b, Is.GreaterThan(0.01f), label + " is black or nondiscriminating.");
        }

        /// <summary>Requires an RGB observation to remain within an absolute floor and relative tolerance.</summary>
        /// <param name="expected">The expected linear RGB value.</param>
        /// <param name="actual">The actual linear RGB value.</param>
        /// <param name="relativeTolerance">The allowed relative difference.</param>
        /// <param name="label">The diagnostic observation label.</param>
        private static void AssertColorWithin(Color expected, Color actual, float relativeTolerance, string label)
        {
            AssertChannel(expected.r, actual.r, relativeTolerance, label + " red");
            AssertChannel(expected.g, actual.g, relativeTolerance, label + " green");
            AssertChannel(expected.b, actual.b, relativeTolerance, label + " blue");
        }

        /// <summary>Requires one finite color channel to respect its absolute and relative tolerance.</summary>
        /// <param name="expected">The expected channel value.</param>
        /// <param name="actual">The actual channel value.</param>
        /// <param name="relativeTolerance">The allowed relative difference.</param>
        /// <param name="label">The diagnostic channel label.</param>
        private static void AssertChannel(float expected, float actual, float relativeTolerance, string label)
        {
            float tolerance = Mathf.Max(0.002f, Mathf.Abs(expected) * relativeTolerance);
            Assert.That(Mathf.Abs(actual - expected), Is.LessThanOrEqualTo(tolerance), label + " differed by more than " + tolerance + ".");
        }
    }
}