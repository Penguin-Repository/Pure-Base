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

// Defines actual Meta-pass roughness-floor contracts using the existing validation-scene capture helpers.

using NUnit.Framework;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Provides roughness-floor Meta capture contracts without enlarging the validation-scene fixture.</summary>
    public sealed partial class PureBaseValidationSceneRegressionTests
    {
        /// <summary>Requires below-floor Meta output to match the public floor while retaining an above-floor discriminator.</summary>
        [Test]
        public void PbrAndHybridMetaRoughnessFloorMatchesFormulaAndExactFloor()
        {
            WithPbrAndHybridMaterials(materials =>
            {
                foreach (Material material in materials)
                    AssertPbrRoughnessMetaContract(material);
            });
        }

        /// <summary>Captures below-floor, exact-floor, and above-floor Meta output with formula and toggle controls.</summary>
        private static void AssertPbrRoughnessMetaContract(Material sourceMaterial)
        {
            Color albedo = new Color(0.92f, 0.61f, 0.28f, 1.0f);
            MetaCaptureReadback exact = CapturePbrRoughnessMeta(sourceMaterial, albedo, 0.089f, 0);
            MetaCaptureReadback above = CapturePbrRoughnessMeta(sourceMaterial, albedo, 0.25f, 0);
            MetaCaptureReadback below = CapturePbrRoughnessMeta(sourceMaterial, albedo, 0.0f, 0);
            AssertMetaReadback(exact, EvaluateExpectedMetaAlbedo(albedo, 0.9f, 0.089f, true), sourceMaterial.shader.name + " Meta exact floor");
            AssertMetaReadback(above, EvaluateExpectedMetaAlbedo(albedo, 0.9f, 0.25f, true), sourceMaterial.shader.name + " Meta above floor");
            AssertMetaReadback(below, EvaluateExpectedMetaAlbedo(albedo, 0.9f, 0.0f, true), sourceMaterial.shader.name + " Meta below floor");
            Assert.That(MaximumAbsoluteRgbDifference(below.meanColor, exact.meanColor), Is.LessThanOrEqualTo(MetaCaptureTolerance), sourceMaterial.shader.name + " Meta below-floor output must equal the exact floor.");
            Assert.That(MaximumAbsoluteRgbDifference(exact.meanColor, above.meanColor), Is.GreaterThan(0.002f), sourceMaterial.shader.name + " Meta must distinguish 0.25 roughness.");
            AssertPbrRoughnessMetaToggleInvariant(sourceMaterial, albedo, exact);
        }

        /// <summary>Captures one fully covered finite metallic Meta readback at the requested stored roughness.</summary>
        private static MetaCaptureReadback CapturePbrRoughnessMeta(Material sourceMaterial, Color albedo, float roughness, int toggle)
        {
            return RenderMetaCapture(sourceMaterial, material =>
            {
                ConfigureMetaMaterial(material, albedo, 0.9f, roughness, 0.0f);
                material.SetInteger("_UseUnityStandardDiffuseBrightness", toggle);
            }, false, null, MetaAlbedoFragmentControl);
        }

        /// <summary>Requires the direct-only brightness toggle to leave the exact-floor Meta result unchanged.</summary>
        private static void AssertPbrRoughnessMetaToggleInvariant(Material sourceMaterial, Color albedo, MetaCaptureReadback exact)
        {
            MetaCaptureReadback enabled = CapturePbrRoughnessMeta(sourceMaterial, albedo, 0.089f, 1);
            AssertMetaReadback(enabled, EvaluateExpectedMetaAlbedo(albedo, 0.9f, 0.089f, true), sourceMaterial.shader.name + " Meta exact-floor toggle");
            Assert.That(MaximumAbsoluteRgbDifference(exact.meanColor, enabled.meanColor), Is.LessThanOrEqualTo(MetaCaptureTolerance), sourceMaterial.shader.name + " Meta must ignore the direct-diffuse brightness toggle.");
        }
    }
}
