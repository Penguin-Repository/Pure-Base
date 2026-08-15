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

// Defines focused source contracts for the OpenLit-derived Toon lighting integration.

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines focused source contracts for the OpenLit-derived Toon lighting integration.</summary>
    public sealed partial class PureBaseRenderingModeRenderingTests
    {
        /// <summary>Provides source-contract assertions for the OpenLit-derived Toon lighting integration.</summary>
        private static class OpenLitSourceContractAssertions
        {
            /// <summary>Requires the OpenLit-derived Toon equation and rejects the superseded scaled or inverted SH approximation.</summary>
            /// <param name="helper">The Toon-only lighting helper source.</param>
            internal static void AssertOpenLitToonEquationContracts(string helper)
            {
                StringAssert.Contains("float3(0.22, 0.707, 0.071)", helper);
                StringAssert.Contains("float3(0.0396819152, 0.458021790, 0.00609653955)", helper);
                StringAssert.Contains("float3(0.001, 0.002, 0.001)", helper);
                StringAssert.Contains("UNITY_COLORSPACE_GAMMA", helper);
                Assert.That(Regex.IsMatch(helper, @"normalize\s*\(\s*shDirection\s*\)"), Is.True, "The dark L1 direction must derive from the summed SH coefficients.");
                Assert.That(Regex.IsMatch(helper, @"if\s*\(\s*all\s*\(\s*shDirection\s*==\s*0\s*\)\s*\|\|\s*!all\s*\(\s*isfinite\s*\(\s*shDirection\s*\)\s*\)\s*\)"), Is.True, "The dark L1 direction must only reject exact-zero or nonfinite summed SH coefficients.");
                Assert.That(Regex.IsMatch(helper, @"dot\s*\(\s*shDirection\s*,\s*shDirection\s*\)\s*<=\s*0\.000001"), Is.False, "The dark L1 direction must not discard finite near-cancellation residuals.");
                Assert.That(Regex.IsMatch(helper, @"float3\s+E\s*=\s*L\s*\*\s*0\.666666"), Is.False, "OpenLit bright L0/L2 and L1 must use unscaled V.");
                Assert.That(Regex.IsMatch(helper, @"base\s*-\s*linearTerm"), Is.False, "OpenLit dark L1 must not invert the bright L1 term.");
                Assert.That(Regex.IsMatch(helper, @"\bsd\.shadow\b"), Is.False, "Toon direction and SH evaluation must remain visibility-independent.");
            }

            /// <summary>Requires the fixed fallback to enter the Toon direction sum before normalization.</summary>
            /// <param name="helper">The Toon-only lighting helper source.</param>
            internal static void AssertOpenLitFallbackPrecedesNormalization(string helper)
            {
                StringAssert.Contains("float3 fallbackDirection = float3(0.001, 0.002, 0.001);", helper);
                Assert.That(
                    Regex.IsMatch(
                        helper,
                        @"float3\s+directionVector\s*=\s*directAggregateDirection\s*\+\s*float3\s*\(\s*shDirection\.x\s*,\s*abs\s*\(\s*shDirection\.y\s*\)\s*,\s*shDirection\.z\s*\)\s*\+\s*fallbackDirection\s*;[\s\S]*?return\s+normalize\s*\(\s*directionVector\s*\)\s*;",
                        RegexOptions.Singleline
                    ),
                    Is.True,
                    "Toon direction must add the fixed fallback to the direct and SH direction sum before normalization."
                );
            }

            /// <summary>Requires Toon-only SH gates and direct-only ForwardAdd direction publication in the shared host.</summary>
            /// <param name="host">The common BIRP fragment host source.</param>
            internal static void AssertOpenLitHostGateContracts(string host)
            {
                const string fallbackDirectDirection = "sd.L = SCModelSelectAggregateLightDirection(lightSum.direction, half4(0, 0, 0, 0), half4(0, 0, 0, 0), half4(0, 0, 0, 0));";
                const string shDirection = "sd.L = SCModelSelectAggregateLightDirection(lightSum.direction, unity_SHAr, unity_SHAg, unity_SHAb);";
                const string toonAmbient = "env += SCModelEvaluateAmbient(sd, unity_SHAr, unity_SHAg, unity_SHAb, unity_SHBr, unity_SHBg, unity_SHBb, unity_SHC);";
                int fallbackDirectIndex = RequireIndex(host, fallbackDirectDirection);
                int shGateIndex = RequireIndex(host, "#if !defined(LIGHTMAP_ON) && UNITY_SHOULD_SAMPLE_SH");
                int shDirectionIndex = RequireIndex(host, shDirection);
                int toonAmbientIndex = RequireIndex(host, toonAmbient);

                Assert.That(Regex.IsMatch(host, @"#if\s+defined\(PUREBASE_TOON_MODEL_INCLUDED\)\s*&&\s*!defined\(LIGHTMAP_ON\)"), Is.False, "Fallback-inclusive direct direction must not be owned by the obsolete combined Toon/lightmap gate.");
                Assert.That(fallbackDirectIndex, Is.LessThan(shGateIndex), "ForwardBase must publish fallback-inclusive direct direction before deciding whether Toon SH is available.");
                Assert.That(shGateIndex, Is.LessThan(shDirectionIndex), "Toon SH direction must remain inside the nested no-lightmap Unity SH gate.");
                Assert.That(shDirectionIndex, Is.LessThan(toonAmbientIndex), "The Toon ambient band must remain after its SH direction contribution inside the nested gate.");
                Assert.That(
                    Regex.IsMatch(
                        host,
                        @"#else\s*sd\.L\s*=\s*SCModelSelectAggregateLightDirection\(lightSum\.direction,\s*half4\(0,\s*0,\s*0,\s*0\),\s*half4\(0,\s*0,\s*0,\s*0\),\s*half4\(0,\s*0,\s*0,\s*0\)\);\s*#if\s*!defined\(LIGHTMAP_ON\)\s*&&\s*UNITY_SHOULD_SAMPLE_SH\s*sd\.L\s*=\s*SCModelSelectAggregateLightDirection\(lightSum\.direction,\s*unity_SHAr,\s*unity_SHAg,\s*unity_SHAb\);\s*env\s*\+=\s*SCModelEvaluateAmbient\(sd,\s*unity_SHAr,\s*unity_SHAg,\s*unity_SHAb,\s*unity_SHBr,\s*unity_SHBg,\s*unity_SHBb,\s*unity_SHC\);\s*#endif",
                        RegexOptions.Singleline
                    ),
                    Is.True,
                    "Only the nested ForwardBase no-lightmap Unity SH gate may add Toon SH direction and ambient bands."
                );
                Assert.That(Regex.IsMatch(host, @"#if\s+defined\(UNITY_PASS_FORWARDADD\)[\s\S]*?sd\.L\s*=\s*dot\(lightSum\.direction\s*,\s*lightSum\.direction\)\s*>\s*0\.000001\s*\?\s*normalize\(lightSum\.direction\)\s*:\s*(?:half|float)3\(0(?:\.0+)?\s*,\s*0(?:\.0+)?\s*,\s*0(?:\.0+)?\)"), Is.True, "ForwardAdd must publish normalized direct direction or zero without SH fallback.");
                Assert.That(Regex.Matches(host, @"\bsd\.L\s*=\s*[^;]*?(?:half|float)3\(0(?:\.0+)?\s*,\s*0(?:\.0+)?\s*,\s*0(?:\.0+)?\)").Count, Is.EqualTo(1), "Only ForwardAdd may reset sd.L to zero; lightmap and SH-disabled ForwardBase branches must retain fallback-inclusive direct direction.");
            }

        }

        /// <summary>Requires Toon-owned binary direct and two-band environment lighting with Shader-Core lightmap and ForwardAdd isolation.</summary>
        [Test]
        public void ToonLightingOwnershipKeepsBinaryDirectTwoBandShaderCoreLightmapsAndForwardAddIsolation()
        {
            string toon = File.ReadAllText(ToonModelPath);
            string helper = File.ReadAllText(ToonLightingHelperPath);
            string host = File.ReadAllText(BirpHostPath);
            string shaderCoreLighting = File.ReadAllText(ShaderCoreBirpLightingPath);
            string pbr = File.ReadAllText(PbrModelPath);
            string pbrBrdf = File.ReadAllText(PbrBrdfPath);
            string hybrid = File.ReadAllText(HybridModelPath);

            AssertToonHelperAndModelContracts(toon, helper);
            AssertBirpHostForwardAddAndLightmapContracts(host, shaderCoreLighting);
            AssertPbrAndHybridLightingOwnership(pbr, pbrBrdf, hybrid);
            AssertLightingPhaseOrder(host);
            OpenLitSourceContractAssertions.AssertOpenLitFallbackPrecedesNormalization(helper);
        }

        /// <summary>Requires the Toon direction fallback to affect nondegenerate aggregates before normalization.</summary>
        [Test]
        public void ToonOpenLitFallbackIsAddedBeforeDirectionNormalization()
        {
            OpenLitSourceContractAssertions.AssertOpenLitFallbackPrecedesNormalization(File.ReadAllText(ToonLightingHelperPath));
        }
    }
}
