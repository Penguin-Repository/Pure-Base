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

// Defines analytical and source-layout contracts for the shared Smith GGX visibility approximation.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines formula characterization and source-layout contracts for PBR Smith GGX visibility.</summary>
    public sealed class PureBasePbrVisibilityApproximationTests
    {
        /// <summary>Identifies the product source that owns shared direct PBR visibility.</summary>
        private const string PbrBrdfPath = "Packages/jp.penguin.purebase/Shaders/Common/pbr_brdf.hlsl";

        /// <summary>Defines the legacy direct-evaluator radicand and denominator guard.</summary>
        private const float LegacyEpsilon = 0.000001f;

        /// <summary>Defines Unity's normal-platform additive visibility denominator.</summary>
        private const float NormalEpsilon = 0.00001f;

        /// <summary>Defines Unity's binary16 minimum-normal denominator for the Switch branch.</summary>
        private const float SwitchEpsilon = 0.00006103515625f;

        /// <summary>Defines the fixed relative-error denominator for reference values near zero.</summary>
        private const float RelativeNearZeroFloor = 0.0001f;

        /// <summary>Lists the representative perceptual roughness samples.</summary>
        private static readonly float[] PerceptualRoughnessSamples = { 0.089f, 0.125f, 0.25f, 0.5f, 0.75f, 1.0f };

        /// <summary>Lists the representative light and view cosine samples.</summary>
        private static readonly float[] CosineSamples = { 0.0f, 0.01f, 0.05f, 0.25f, 0.5f, 0.75f, 1.0f };

        /// <summary>Characterizes all three visibility formulas without converting their measured deltas into a product threshold.</summary>
        [Test]
        public void FixedDomainCharacterizationIsFiniteSymmetricAndRecordsApproximationDelta()
        {
            foreach (bool useSwitchDenominator in new[] { false, true })
            {
                var legacyDeltas = new List<VisibilityDelta>();
                var regularizedDeltas = new List<VisibilityDelta>();
                foreach (float perceptualRoughness in PerceptualRoughnessSamples)
                {
                    foreach (float ndotL in CosineSamples)
                    {
                        foreach (float ndotV in CosineSamples)
                        {
                            CharacterizeFormulaTriplet(perceptualRoughness, ndotL, ndotV, useSwitchDenominator, legacyDeltas, regularizedDeltas);
                        }
                    }
                }

                LogDeltaSummary("legacy-exact", useSwitchDenominator, legacyDeltas);
                LogDeltaSummary("regularized-exact", useSwitchDenominator, regularizedDeltas);
            }
        }

        /// <summary>Requires the future source implementation to isolate Unity's fast joint GGX visibility contract.</summary>
        [Test]
        public void FastApproximationSourceUsesSquaredRoughnessWithoutSquareRoots()
        {
            string source = File.ReadAllText(PbrBrdfPath);
            string helperBody = RequireDocumentedFastVisibilityHelper(source);
            AssertFastVisibilityFormula(helperBody);
            AssertFastVisibilityDenominators(helperBody);
            AssertDirectEvaluatorUsesFastVisibility(source);
            AssertUnchangedNeighborOwnership(source);
        }

        /// <summary>Finds the exactly-once documented helper before inspecting its implementation.</summary>
        private static string RequireDocumentedFastVisibilityHelper(string source)
        {
            const string signature = @"(?m)^[ \t]*float[ \t]+PureBasePbrEvaluateSmithJointGgxVisibility\s*\(\s*float[ \t]+NdotL\s*,\s*float[ \t]+NdotV\s*,\s*float[ \t]+roughness\s*\)";
            MatchCollection helpers = Regex.Matches(source, signature);
            Assert.That(helpers.Count, Is.EqualTo(1), "Expected RED until exactly one PureBasePbrEvaluateSmithJointGgxVisibility(float NdotL, float NdotV, float roughness) helper is introduced.");
            Match documented = Regex.Match(source, @"(?m)^[ \t]*///\s*<summary>.*</summary>[^\r\n]*\r?\n(?:[ \t]*///.*\r?\n)*" + signature);
            Assert.That(documented.Success, Is.True, "The shared fast visibility helper must have XML documentation directly contiguous to its declaration.");
            Match definition = Regex.Match(source, signature + @"\s*\{(?<body>.*?)^[ \t]*\}[ \t]*(?:\r?\n|$)", RegexOptions.Singleline | RegexOptions.Multiline);
            Assert.That(definition.Success, Is.True, "The documented fast visibility helper must have a complete source body.");
            return definition.Groups["body"].Value;
        }

        /// <summary>Requires the square-root-free joint GGX lambda expressions.</summary>
        private static void AssertFastVisibilityFormula(string helperBody)
        {
            Assert.That(Regex.IsMatch(helperBody, @"\bsqrt\s*\("), Is.False, "The fast visibility helper must not evaluate square roots.");
            Assert.That(Regex.IsMatch(helperBody, @"\broughnessFourth\b"), Is.False, "The fast visibility helper must not accept or derive roughnessFourth.");
            Assert.That(Regex.IsMatch(helperBody, @"\bfloat\s+lambdaV\s*=\s*NdotL\s*\*\s*\(\s*NdotV\s*\*\s*\(\s*1(?:\.0+)?f?\s*-\s*roughness\s*\)\s*\+\s*roughness\s*\)\s*;"), Is.True, "The fast visibility helper must independently calculate lambdaV from NdotL, NdotV, and academic roughness.");
            Assert.That(Regex.IsMatch(helperBody, @"\bfloat\s+lambdaL\s*=\s*NdotV\s*\*\s*\(\s*NdotL\s*\*\s*\(\s*1(?:\.0+)?f?\s*-\s*roughness\s*\)\s*\+\s*roughness\s*\)\s*;"), Is.True, "The fast visibility helper must independently calculate lambdaL from NdotV, NdotL, and academic roughness.");
        }

        /// <summary>Requires the Switch-aware additive visibility denominator and return value.</summary>
        private static void AssertFastVisibilityDenominators(string helperBody)
        {
            Assert.That(Regex.IsMatch(helperBody, @"#\s*if\s+defined\s*\(\s*SHADER_API_SWITCH\s*\).*?\bfloat\s+epsilon\s*=\s*UNITY_HALF_MIN\s*;.*?#\s*else.*?\bfloat\s+epsilon\s*=\s*1e-5f\s*;.*?#\s*endif", RegexOptions.Singleline), Is.True, "The helper must select UNITY_HALF_MIN on Switch and 1e-5f on normal platforms.");
            Assert.That(Regex.IsMatch(helperBody, @"\breturn\s+0\.5f\s*/\s*\(\s*lambdaV\s*\+\s*lambdaL\s*\+\s*epsilon\s*\)\s*;"), Is.True, "The helper must return the documented additive-denominator visibility expression.");
        }

        /// <summary>Requires the direct evaluator to form its complete finite specular contribution before narrowing.</summary>
        private static void AssertDirectEvaluatorUsesFastVisibility(string source)
        {
            string body = RequireHlslFunctionBody(source, "PureBasePbrEvaluateDirect");
            AssertFloatDirectInputs(body);
            AssertFloatDirectSpecularContribution(body);
        }

        /// <summary>Finds one HLSL function body by matching its balanced outer braces.</summary>
        private static string RequireHlslFunctionBody(string source, string functionName)
        {
            Match header = Regex.Match(source, @"(?m)^[ \t]*half3\s+" + Regex.Escape(functionName) + @"\s*\([^)]*\)\s*(?<open>\{)");
            Assert.That(header.Success, Is.True, functionName + " must have a complete source body.");
            int openingBrace = header.Groups["open"].Index;
            int closingBrace = FindMatchingClosingBrace(source, openingBrace, functionName);
            return source.Substring(openingBrace + 1, closingBrace - openingBrace - 1);
        }

        /// <summary>Returns the closing brace paired with one known opening brace.</summary>
        private static int FindMatchingClosingBrace(string source, int openingBrace, string functionName)
        {
            int depth = 0;
            for (int index = openingBrace; index < source.Length; index++)
            {
                if (source[index] == '{')
                    depth++;
                else if (source[index] == '}' && --depth == 0)
                    return index;
            }

            Assert.Fail(functionName + " must close its outer source body.");
            return -1;
        }

        /// <summary>Requires float distribution and helper visibility inputs within the direct evaluator body.</summary>
        private static void AssertFloatDirectInputs(string body)
        {
            Assert.That(Regex.IsMatch(body, @"\bfloat\s+distribution\s*=\s*.*?;", RegexOptions.Singleline), Is.True, "The direct evaluator must retain distribution in float precision.");
            Assert.That(Regex.IsMatch(body, @"\bfloat\s+visibility\s*=\s*PureBasePbrEvaluateSmithJointGgxVisibility\s*\(\s*NdotL\s*,\s*NdotV\s*,\s*brdf\.roughnessSquared\s*\)\s*;"), Is.True, "The direct evaluator must pass academic roughness p^2 from brdf.roughnessSquared.");
        }

        /// <summary>Requires the direct evaluator to preserve the complete float specular product through light incidence.</summary>
        private static void AssertFloatDirectSpecularContribution(string body)
        {
            Match contribution = Regex.Match(body, @"\bfloat3\s+(?<specular>[A-Za-z_]\w*)\s*=\s*distribution\s*\*\s*visibility\s*\*\s*NdotL\s*\*\s*PureBasePbrSchlickFresnel\s*\(\s*brdf\.specularColor\s*,\s*LdotH\s*\)\s*;");
            Assert.That(contribution.Success, Is.True, "The direct evaluator must form distribution * visibility * NdotL in one float direct-specular expression before narrowing.");
            string specular = contribution.Groups["specular"].Value;
            Assert.That(Regex.IsMatch(body, @"\breturn\b(?:(?!;).)*\b" + Regex.Escape(specular) + @"\b", RegexOptions.Singleline), Is.True, "The final direct result must consume the completed float direct-specular contribution.");
        }

        /// <summary>Evaluates and validates one formula triplet for a representative coordinate.</summary>
        private static void CharacterizeFormulaTriplet(float perceptualRoughness, float ndotL, float ndotV, bool useSwitchDenominator, List<VisibilityDelta> legacyDeltas, List<VisibilityDelta> regularizedDeltas)
        {
            float legacy = LegacyExactVisibility(perceptualRoughness, ndotL, ndotV);
            float regularized = RegularizedExactVisibility(perceptualRoughness, ndotL, ndotV, useSwitchDenominator);
            float fast = FastVisibility(perceptualRoughness, ndotL, ndotV, useSwitchDenominator);
            string coordinate = Coordinate(perceptualRoughness, ndotL, ndotV, useSwitchDenominator);
            AssertFiniteNonNegative(legacy, "legacy exact " + coordinate);
            AssertFiniteNonNegative(regularized, "regularized exact " + coordinate);
            AssertFiniteNonNegative(fast, "fast " + coordinate);
            AssertSymmetry(perceptualRoughness, ndotL, ndotV, useSwitchDenominator, legacy, regularized, fast);
            if (ndotL == 0.0f)
                Assert.That(fast * ndotL, Is.EqualTo(0.0f).Within(0.0f), "The float direct visibility product must remain exactly zero at NdotL zero.");
            if (perceptualRoughness == 1.0f)
                Assert.That(fast, Is.EqualTo(regularized).Within(0.000001f), "Regularized exact and fast visibility must agree at p = 1 for " + coordinate + ".");
            legacyDeltas.Add(VisibilityDelta.Create(legacy, fast, perceptualRoughness, ndotL, ndotV, useSwitchDenominator));
            regularizedDeltas.Add(VisibilityDelta.Create(regularized, fast, perceptualRoughness, ndotL, ndotV, useSwitchDenominator));
        }

        /// <summary>Mirrors the current square-root visibility expression and its max-denominator guard.</summary>
        private static float LegacyExactVisibility(float perceptualRoughness, float ndotL, float ndotV)
        {
            float roughnessSquared = perceptualRoughness * perceptualRoughness;
            float roughnessFourth = roughnessSquared * roughnessSquared;
            float visibilityV = ndotL * MathF.Sqrt(MathF.Max(ndotV * (ndotV - ndotV * roughnessFourth) + roughnessFourth, LegacyEpsilon));
            float visibilityL = ndotV * MathF.Sqrt(MathF.Max(ndotL * (ndotL - ndotL * roughnessFourth) + roughnessFourth, LegacyEpsilon));
            return 0.5f / MathF.Max(visibilityV + visibilityL, LegacyEpsilon);
        }

        /// <summary>Evaluates the exact square-root form with the future additive platform denominator.</summary>
        private static float RegularizedExactVisibility(float perceptualRoughness, float ndotL, float ndotV, bool useSwitchDenominator)
        {
            float roughnessSquared = perceptualRoughness * perceptualRoughness;
            float roughnessFourth = roughnessSquared * roughnessSquared;
            float visibilityV = ndotL * MathF.Sqrt(MathF.Max(ndotV * (ndotV - ndotV * roughnessFourth) + roughnessFourth, LegacyEpsilon));
            float visibilityL = ndotV * MathF.Sqrt(MathF.Max(ndotL * (ndotL - ndotL * roughnessFourth) + roughnessFourth, LegacyEpsilon));
            return 0.5f / (visibilityV + visibilityL + SelectPlatformEpsilon(useSwitchDenominator));
        }

        /// <summary>Evaluates Unity's linearized joint GGX visibility from academic roughness a equals p squared.</summary>
        private static float FastVisibility(float perceptualRoughness, float ndotL, float ndotV, bool useSwitchDenominator)
        {
            float roughnessSquared = perceptualRoughness * perceptualRoughness;
            float lambdaV = ndotL * (ndotV * (1.0f - roughnessSquared) + roughnessSquared);
            float lambdaL = ndotV * (ndotL * (1.0f - roughnessSquared) + roughnessSquared);
            return 0.5f / (lambdaV + lambdaL + SelectPlatformEpsilon(useSwitchDenominator));
        }

        /// <summary>Selects the documented additive denominator for the represented platform branch.</summary>
        private static float SelectPlatformEpsilon(bool useSwitchDenominator)
        {
            return useSwitchDenominator ? SwitchEpsilon : NormalEpsilon;
        }

        /// <summary>Requires every formula to preserve symmetry under light and view cosine exchange.</summary>
        private static void AssertSymmetry(float perceptualRoughness, float ndotL, float ndotV, bool useSwitchDenominator, float legacy, float regularized, float fast)
        {
            string coordinate = Coordinate(perceptualRoughness, ndotL, ndotV, useSwitchDenominator);
            Assert.That(legacy, Is.EqualTo(LegacyExactVisibility(perceptualRoughness, ndotV, ndotL)).Within(0.000001f), "Legacy exact symmetry failed for " + coordinate + ".");
            Assert.That(regularized, Is.EqualTo(RegularizedExactVisibility(perceptualRoughness, ndotV, ndotL, useSwitchDenominator)).Within(0.000001f), "Regularized exact symmetry failed for " + coordinate + ".");
            Assert.That(fast, Is.EqualTo(FastVisibility(perceptualRoughness, ndotV, ndotL, useSwitchDenominator)).Within(0.000001f), "Fast symmetry failed for " + coordinate + ".");
        }

        /// <summary>Requires a scalar visibility result to be finite and nonnegative.</summary>
        private static void AssertFiniteNonNegative(float value, string label)
        {
            Assert.That(float.IsFinite(value), Is.True, label + " is non-finite.");
            Assert.That(value, Is.GreaterThanOrEqualTo(0.0f), label + " is negative.");
        }

        /// <summary>Logs percentiles, maxima, and worst coordinates with the relative near-zero policy.</summary>
        private static void LogDeltaSummary(string referenceName, bool useSwitchDenominator, List<VisibilityDelta> deltas)
        {
            VisibilityDeltaSummary summary = CreateDeltaSummary(referenceName, useSwitchDenominator, deltas);
            TestContext.Progress.WriteLine(
                "Smith GGX visibility {0} vs fast ({1} denominator): maxAbs={2:R}, maxRel={3:R}, p50Abs={4:R}, p95Abs={5:R}, p99Abs={6:R}, p50Rel={7:R}, p95Rel={8:R}, p99Rel={9:R}, worstAbs={10}, worstRel={11}; relative denominator=max(abs(reference), {12:R}).",
                summary.ReferenceName,
                summary.DenominatorBranch,
                summary.MaximumAbsolute,
                summary.MaximumRelative,
                summary.Percentile50Absolute,
                summary.Percentile95Absolute,
                summary.Percentile99Absolute,
                summary.Percentile50Relative,
                summary.Percentile95Relative,
                summary.Percentile99Relative,
                summary.WorstAbsoluteCoordinate,
                summary.WorstRelativeCoordinate,
                RelativeNearZeroFloor
            );
        }

        /// <summary>Builds the immutable characterization record exported with enabled legacy GPU evidence.</summary>
        internal static string BuildVisibilityCharacterizationArtifact()
        {
            var summaries = new List<VisibilityDeltaSummary>();
            foreach (bool useSwitchDenominator in new[] { false, true })
            {
                var legacyDeltas = new List<VisibilityDelta>();
                var regularizedDeltas = new List<VisibilityDelta>();
                foreach (float perceptualRoughness in PerceptualRoughnessSamples)
                {
                    foreach (float ndotL in CosineSamples)
                    {
                        foreach (float ndotV in CosineSamples)
                        {
                            float legacy = LegacyExactVisibility(perceptualRoughness, ndotL, ndotV);
                            float regularized = RegularizedExactVisibility(perceptualRoughness, ndotL, ndotV, useSwitchDenominator);
                            float fast = FastVisibility(perceptualRoughness, ndotL, ndotV, useSwitchDenominator);
                            legacyDeltas.Add(VisibilityDelta.Create(legacy, fast, perceptualRoughness, ndotL, ndotV, useSwitchDenominator));
                            regularizedDeltas.Add(VisibilityDelta.Create(regularized, fast, perceptualRoughness, ndotL, ndotV, useSwitchDenominator));
                        }
                    }
                }

                summaries.Add(CreateDeltaSummary("legacy-exact", useSwitchDenominator, legacyDeltas));
                summaries.Add(CreateDeltaSummary("regularized-exact", useSwitchDenominator, regularizedDeltas));
            }

            var artifact = new StringBuilder("{\n  \"relativeDenominator\": \"max(abs(reference), 0.0001)\",\n  \"summaries\": [\n");
            for (int index = 0; index < summaries.Count; index++)
            {
                if (index > 0)
                    artifact.Append(",\n");
                artifact.Append(summaries[index].ToJson());
            }

            artifact.Append("\n  ]\n}\n");
            return artifact.ToString();
        }

        /// <summary>Creates complete extrema and percentile data for one formula and denominator branch.</summary>
        private static VisibilityDeltaSummary CreateDeltaSummary(string referenceName, bool useSwitchDenominator, List<VisibilityDelta> deltas)
        {
            deltas.Sort((first, second) => first.Absolute.CompareTo(second.Absolute));
            VisibilityDelta worstAbsolute = deltas[deltas.Count - 1];
            VisibilityDelta worstRelative = deltas[0];
            float[] absolute = deltas.ConvertAll(delta => delta.Absolute).ToArray();
            float[] relative = deltas.ConvertAll(delta => delta.Relative).ToArray();
            foreach (VisibilityDelta delta in deltas)
                if (delta.Relative > worstRelative.Relative)
                    worstRelative = delta;
            Array.Sort(relative);
            return new VisibilityDeltaSummary(referenceName, useSwitchDenominator, worstAbsolute, worstRelative, Percentile(absolute, 0.50f), Percentile(absolute, 0.95f), Percentile(absolute, 0.99f), Percentile(relative, 0.50f), Percentile(relative, 0.95f), Percentile(relative, 0.99f));
        }

        /// <summary>Returns the inclusive nearest-rank percentile from an ascending sequence.</summary>
        private static float Percentile(float[] ascendingValues, float percentile)
        {
            int index = (int)Math.Ceiling((ascendingValues.Length - 1) * percentile);
            return ascendingValues[index];
        }

        /// <summary>Builds a deterministic label for one roughness and cosine coordinate.</summary>
        private static string Coordinate(float perceptualRoughness, float ndotL, float ndotV, bool useSwitchDenominator)
        {
            return string.Format("p={0:R}, NdotL={1:R}, NdotV={2:R}, denominator={3}", perceptualRoughness, ndotL, ndotV, useSwitchDenominator ? "Switch" : "normal");
        }

        /// <summary>Protects direct-neighbor BRDF ownership from a visibility-only implementation.</summary>
        private static void AssertUnchangedNeighborOwnership(string source)
        {
            StringAssert.Contains("roughnessFourth = brdf.roughnessSquared * brdf.roughnessSquared", source);
            StringAssert.Contains("PureBasePbrSchlickFresnel", source);
            StringAssert.Contains("brdf.diffuseColor * diffuseNdotL * diffuseNormalization", source);
            StringAssert.Contains("PureBasePbrEvaluateIndirect", source);
            StringAssert.Contains("PureBasePbrEvaluateLightmappingAlbedo", source);
        }

        /// <summary>Stores one deterministic exact-versus-fast delta and its representative coordinate.</summary>
        private readonly struct VisibilityDelta
        {
            /// <summary>Gets the absolute visibility difference.</summary>
            public float Absolute { get; }

            /// <summary>Gets the near-zero-stabilized relative visibility difference.</summary>
            public float Relative { get; }

            /// <summary>Gets the coordinate at which this delta was observed.</summary>
            public string Coordinate { get; }

            /// <summary>Initializes one visibility delta observation.</summary>
            private VisibilityDelta(float absolute, float relative, string coordinate)
            {
                Absolute = absolute;
                Relative = relative;
                Coordinate = coordinate;
            }

            /// <summary>Creates one exact-reference delta using the documented relative near-zero policy.</summary>
            public static VisibilityDelta Create(float reference, float fast, float perceptualRoughness, float ndotL, float ndotV, bool useSwitchDenominator)
            {
                float absolute = MathF.Abs(reference - fast);
                float relative = absolute / MathF.Max(MathF.Abs(reference), RelativeNearZeroFloor);
                return new VisibilityDelta(absolute, relative, Coordinate(perceptualRoughness, ndotL, ndotV, useSwitchDenominator));
            }
        }

        /// <summary>Stores the complete deterministic characterization record for one formula and denominator branch.</summary>
        private readonly struct VisibilityDeltaSummary
        {
            /// <summary>Initializes one complete delta summary.</summary>
            public VisibilityDeltaSummary(string referenceName, bool useSwitchDenominator, VisibilityDelta worstAbsolute, VisibilityDelta worstRelative, float percentile50Absolute, float percentile95Absolute, float percentile99Absolute, float percentile50Relative, float percentile95Relative, float percentile99Relative)
            {
                ReferenceName = referenceName;
                DenominatorBranch = useSwitchDenominator ? "Switch" : "normal";
                MaximumAbsolute = worstAbsolute.Absolute;
                MaximumRelative = worstRelative.Relative;
                WorstAbsoluteCoordinate = worstAbsolute.Coordinate;
                WorstRelativeCoordinate = worstRelative.Coordinate;
                Percentile50Absolute = percentile50Absolute;
                Percentile95Absolute = percentile95Absolute;
                Percentile99Absolute = percentile99Absolute;
                Percentile50Relative = percentile50Relative;
                Percentile95Relative = percentile95Relative;
                Percentile99Relative = percentile99Relative;
            }

            /// <summary>Gets the reference formula name.</summary>
            public string ReferenceName { get; }

            /// <summary>Gets the actual additive denominator branch.</summary>
            public string DenominatorBranch { get; }

            /// <summary>Gets the maximum absolute delta.</summary>
            public float MaximumAbsolute { get; }

            /// <summary>Gets the maximum relative delta.</summary>
            public float MaximumRelative { get; }

            /// <summary>Gets the coordinate for the maximum absolute delta.</summary>
            public string WorstAbsoluteCoordinate { get; }

            /// <summary>Gets the coordinate for the maximum relative delta.</summary>
            public string WorstRelativeCoordinate { get; }

            /// <summary>Gets the absolute p50 delta.</summary>
            public float Percentile50Absolute { get; }

            /// <summary>Gets the absolute p95 delta.</summary>
            public float Percentile95Absolute { get; }

            /// <summary>Gets the absolute p99 delta.</summary>
            public float Percentile99Absolute { get; }

            /// <summary>Gets the relative p50 delta.</summary>
            public float Percentile50Relative { get; }

            /// <summary>Gets the relative p95 delta.</summary>
            public float Percentile95Relative { get; }

            /// <summary>Gets the relative p99 delta.</summary>
            public float Percentile99Relative { get; }

            /// <summary>Serializes this summary without locale-dependent numeric formatting.</summary>
            public string ToJson()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "    {{ \"reference\": \"{0}\", \"denominatorBranch\": \"{1}\", \"maxAbsolute\": {2:R}, \"maxRelative\": {3:R}, \"percentiles\": {{ \"absolute\": {{ \"p50\": {4:R}, \"p95\": {5:R}, \"p99\": {6:R} }}, \"relative\": {{ \"p50\": {7:R}, \"p95\": {8:R}, \"p99\": {9:R} }} }}, \"worstAbsolute\": \"{10}\", \"worstRelative\": \"{11}\" }}",
                    ReferenceName,
                    DenominatorBranch,
                    MaximumAbsolute,
                    MaximumRelative,
                    Percentile50Absolute,
                    Percentile95Absolute,
                    Percentile99Absolute,
                    Percentile50Relative,
                    Percentile95Relative,
                    Percentile99Relative,
                    WorstAbsoluteCoordinate,
                    WorstRelativeCoordinate
                );
            }
        }
    }
}
