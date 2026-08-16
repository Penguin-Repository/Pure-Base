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

// Defines direct and reflection GPU contracts for the PBR perceptual-roughness floor.

using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines GPU contracts for the PBR and Hybrid perceptual-roughness floor.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Identifies the public lower bound shared by PBR and Hybrid perceptual roughness.</summary>
        private const float PbrRoughnessFloor = 0.089f;

        /// <summary>Lists the PBR-family products that must share roughness behavior.</summary>
        private static readonly string[] PbrRoughnessShaderNames = { "PureBase/PBR", "PureBase/Hybrid" };

        /// <summary>Requires both direct forward paths to map below-floor metallic roughness to the public floor.</summary>
        [Test]
        public void PbrAndHybridDirectRoughnessFloorIsFiniteEquivalentAndDiscriminating()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                foreach (string shaderName in PbrRoughnessShaderNames)
                {
                    AssertDirectRoughnessCase(capture, shaderName, "ForwardBase", Vector3.back, "normal incidence");
                    AssertDirectRoughnessCase(capture, shaderName, "ForwardBase", new Vector3(0.98f, 0.0f, -0.2f), "grazing incidence");
                    AssertDirectRoughnessCase(capture, shaderName, "ForwardAdd", Vector3.back, "normal incidence");
                    AssertDirectRoughnessCase(capture, shaderName, "ForwardAdd", new Vector3(0.98f, 0.0f, -0.2f), "grazing incidence");
                }
            }
        }

        /// <summary>Requires direct-light-free reflection to select the shared floor and a distinct higher roughness response.</summary>
        [Test]
        public void PbrAndHybridReflectionRoughnessFloorIsFiniteEquivalentAndDiscriminating()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                foreach (string shaderName in PbrRoughnessShaderNames)
                {
                    Color below = capture.RenderPbrRoughnessReflection(shaderName, 0.0f);
                    Color floor = capture.RenderPbrRoughnessReflection(shaderName, PbrRoughnessFloor);
                    Color above = capture.RenderPbrRoughnessReflection(shaderName, 0.25f);
                    AssertPbrRoughnessObservation(below, shaderName + " reflection below floor", true);
                    AssertPbrRoughnessObservation(floor, shaderName + " reflection floor", true);
                    AssertPbrRoughnessObservation(above, shaderName + " reflection above floor", true);
                    AssertColorWithin(floor, below, 0.01f, shaderName + " reflection below-floor equivalence");
                    Assert.That(MaximumPbrRoughnessDifference(floor, above), Is.GreaterThan(0.01f), shaderName + " reflection must distinguish 0.25 roughness.");
                }
            }
        }

        /// <summary>Ensures reflection capture restores caller-owned reflection globals after disposal.</summary>
        [Test]
        public void PbrRoughnessReflectionCaptureRestoresCallerState()
        {
            DefaultReflectionMode mode = RenderSettings.defaultReflectionMode;
            Texture texture = RenderSettings.customReflectionTexture;
            float intensity = RenderSettings.reflectionIntensity;
            using (var capture = new ToonLightingCaptureScope())
                AssertPbrRoughnessObservation(capture.RenderPbrRoughnessReflection("PureBase/PBR", PbrRoughnessFloor), "reflection restoration control", true);
            Assert.That(RenderSettings.defaultReflectionMode, Is.EqualTo(mode));
            Assert.That(RenderSettings.customReflectionTexture, Is.EqualTo(texture));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(intensity));
        }

        /// <summary>Asserts one selected direct incidence and pass case.</summary>
        private static void AssertDirectRoughnessCase(ToonLightingCaptureScope capture, string shaderName, string passName, Vector3 normal, string incidence)
        {
            Color below = capture.RenderPbrRoughnessDirect(shaderName, passName, 0.0f, normal);
            Color floor = capture.RenderPbrRoughnessDirect(shaderName, passName, PbrRoughnessFloor, normal);
            Color above = capture.RenderPbrRoughnessDirect(shaderName, passName, 0.25f, normal);
            string label = shaderName + " " + passName + " " + incidence;
            AssertPbrRoughnessObservation(below, label + " below floor", false);
            AssertPbrRoughnessObservation(floor, label + " floor", true);
            AssertPbrRoughnessObservation(above, label + " above floor", true);
            AssertColorWithin(floor, below, 0.01f, label + " below-floor equivalence");
            Assert.That(MaximumPbrRoughnessDifference(floor, above), Is.GreaterThan(0.005f), label + " must distinguish 0.25 roughness.");
        }

        /// <summary>Requires a finite, nonnegative, nonblack HDR observation.</summary>
        private static void AssertPbrRoughnessObservation(Color color, string label, bool requireNonBlack)
        {
            Assert.That(float.IsFinite(color.r) && float.IsFinite(color.g) && float.IsFinite(color.b), Is.True, label + " is non-finite.");
            Assert.That(color.r >= 0.0f && color.g >= 0.0f && color.b >= 0.0f, Is.True, label + " is negative.");
            if (requireNonBlack)
                Assert.That(color.maxColorComponent, Is.GreaterThan(0.001f), label + " is black or nondiscriminating.");
        }

        /// <summary>Calculates the largest absolute RGB difference between two roughness observations.</summary>
        private static float MaximumPbrRoughnessDifference(Color first, Color second)
        {
            return Mathf.Max(Mathf.Abs(first.r - second.r), Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b));
        }
    }
}
