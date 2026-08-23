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

// Tests bounded retained-prototype evidence separately from the unavailable replacement candidate boundary.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Tests diagnostic reference evidence and the intentionally unavailable candidate entry.</summary>
    public sealed class PureBasePbrMultipleScatteringIndependentOracleCandidateTests
    {
        /// <summary>Requires exact normal and Switch traces to be bounded, repeatable, and observer-neutral.</summary>
        [Test]
        public void ReferencePrototypeTraceIsBoundedDeterministicAndObserverNeutral()
        {
            foreach (IndependentOracleInput input in MinimumGrazingInputs())
            foreach (double target in Targets())
            {
                LightSpaceOracleDiagnosticTrace observed = LightSpaceOracleReferencePrototype.IntegrateWithDiagnostics(input, target);
                LightSpaceOracleDiagnosticTrace repeated = LightSpaceOracleReferencePrototype.IntegrateWithDiagnostics(input, target);
                LightSpaceOracleDiagnosticTrace disabled = LightSpaceOracleReferencePrototype.IntegrateWithDiagnostics(input, target, false);
                AssertTrace(observed, repeated, disabled);
            }
        }

        /// <summary>Requires every retained terminal stop to preserve exact failure context without assuming a cap outcome.</summary>
        [Test]
        public void ReferencePrototypeCapturesEvaluationCapFirstFailureContext()
        {
            foreach (IndependentOracleInput input in MinimumGrazingInputs())
            foreach (double target in Targets())
            {
                LightSpaceOracleDiagnosticTrace trace = LightSpaceOracleReferencePrototype.IntegrateWithDiagnostics(input, target);
                Assert.That(trace.Result.StopState, Is.EqualTo(trace.FirstFailure.StopState));
                Assert.That(trace.FirstFailure.NextScalarReservation, Is.GreaterThanOrEqualTo(0));
                Assert.That(trace.Result.Evaluations, Is.LessThanOrEqualTo(4000000));
                Assert.That(trace.Result.StopState == LightSpaceOracleStopState.EvaluationCap || trace.Result.StopState != LightSpaceOracleStopState.Accepted, Is.True);
                if (trace.Result.StopState == LightSpaceOracleStopState.EvaluationCap) Assert.That(trace.FirstFailure.Phase, Is.Not.Empty);
            }
        }

        /// <summary>Requires retained leaf, node, and interval ledgers to account for bounded prototype work.</summary>
        [Test]
        public void ReferencePrototypeTelemetryAccountsLeafNodeAndThetaIntervalWork()
        {
            LightSpaceOracleDiagnosticTrace trace = LightSpaceOracleReferencePrototype.IntegrateWithDiagnostics(MinimumGrazingInputs()[0], IndependentOracleContract.CandidateBaseTarget);
            Assert.That(trace.Leaves.Count, Is.GreaterThan(0));
            Assert.That(trace.Nodes.Count, Is.GreaterThan(0));
            Assert.That(trace.Intervals.Count, Is.GreaterThan(0));
            Assert.That(trace.IntervalScalarCalls, Is.EqualTo(trace.Result.Evaluations));
            Assert.That(trace.ContributionConcentration.Count, Is.GreaterThan(0));
            Assert.That(trace.ErrorConcentration.Count, Is.GreaterThan(0));
            Assert.That(trace.WorkConcentration.Count, Is.GreaterThan(0));
            AssertStoppedHierarchy(trace); AssertConcentrations(trace);
        }

        /// <summary>Requires replay to retain independent conforming and drift observations after any terminal stop.</summary>
        [Test]
        public void ReferencePrototypeReplayClassifiesFrozenContractDispositions()
        {
            LightSpaceOracleDiagnosticTrace trace = LightSpaceOracleReferencePrototype.IntegrateWithDiagnostics(MinimumGrazingInputs()[0], IndependentOracleContract.CandidateBaseTarget);
            LightSpaceOracleConformanceReport report = LightSpaceOracleDiagnosticReplay.Classify(trace);
            Assert.That(report.Dispositions.Count, Is.EqualTo(13));
            Assert.That(report.Has("scalar-ledger", LightSpaceOracleConformanceDisposition.Conforming), Is.True);
            Assert.That(report.Has("panel-reservations", LightSpaceOracleConformanceDisposition.Conforming), Is.True);
            Assert.That(report.Has("root-residuals", LightSpaceOracleConformanceDisposition.Drift), Is.True, "128-ULP root residual replay detects retained prototype drift");
            Assert.That(report.Has("root-presence", LightSpaceOracleConformanceDisposition.Conforming), Is.True, "interior root presence replay");
            Assert.That(report.Has("root-semantic-ordering", LightSpaceOracleConformanceDisposition.Conforming), Is.True, "32-ULP semantic root ordering replay");
            Assert.That(report.Has("fixed-256-subpanels", LightSpaceOracleConformanceDisposition.Drift), Is.True);
            Assert.That(report.Has("shared-fine-33", LightSpaceOracleConformanceDisposition.Drift), Is.True);
            Assert.That(report.Has("leaf-error", LightSpaceOracleConformanceDisposition.Drift), Is.True);
            Assert.That(report.Has("terminal-stop", LightSpaceOracleConformanceDisposition.Conforming), Is.True);
        }

        /// <summary>Requires every representative base and strict direct candidate result to be finite and accepted.</summary>
        [Test]
        public void IndependentOracleCandidateProducesRepresentativeEvidence()
        {
            foreach (IndependentOracleRepresentativeRow row in IndependentOracleContract.RepresentativeRows)
            foreach (double target in Targets())
            {
                LightSpaceOracleResult result = LightSpaceOracleContractAlignedCandidate.Integrate(row.Input, target);
                Assert.That(result.StopState, Is.EqualTo(LightSpaceOracleStopState.Accepted), row.PBits + "/" + row.NdotVBits + "/" + row.Input.Branch + "/" + target);
                Assert.That(double.IsNaN(result.Value) || double.IsInfinity(result.Value), Is.False);
                Assert.That(double.IsNaN(result.EstimatedError) || double.IsInfinity(result.EstimatedError), Is.False);
            }
        }

        /// <summary>Requires bit-exact direct repeats and the frozen base-to-strict uncertainty bound.</summary>
        [Test]
        public void IndependentOracleCandidateIsDeterministicFiniteAndStricterStable()
        {
            foreach (IndependentOracleRepresentativeRow row in IndependentOracleContract.RepresentativeRows)
            {
                LightSpaceOracleResult baseResult = LightSpaceOracleContractAlignedCandidate.Integrate(row.Input, IndependentOracleContract.CandidateBaseTarget);
                LightSpaceOracleResult strictResult = LightSpaceOracleContractAlignedCandidate.Integrate(row.Input, IndependentOracleContract.CandidateStrictTarget);
                LightSpaceOracleResult repeated = LightSpaceOracleContractAlignedCandidate.Integrate(row.Input, IndependentOracleContract.CandidateStrictTarget);
                AssertAccepted(baseResult, row, IndependentOracleContract.CandidateBaseTarget); AssertAccepted(strictResult, row, IndependentOracleContract.CandidateStrictTarget); AssertResults(strictResult, repeated);
                double uncertainty = Math.Max(strictResult.EstimatedError, Math.Abs(baseResult.Value - strictResult.Value));
                Assert.That(uncertainty, Is.LessThanOrEqualTo(IndependentOracleContract.CandidateBaseTarget), row.PBits + "/" + row.NdotVBits + "/" + row.Input.Branch);
            }
        }

        /// <summary>Requires every p=1 branch and view control to meet the approved composed analytical inequality.</summary>
        [Test]
        public void IndependentOracleCandidateMatchesP1AnalyticalBenchmark()
        {
            foreach (double view in IndependentOracleContract.AnalyticalViewCosines)
            foreach (IndependentOracleBranch branch in new[] { IndependentOracleBranch.Normal, IndependentOracleBranch.Switch })
            {
                LightSpaceOracleResult result = LightSpaceOracleContractAlignedCandidate.Integrate(new IndependentOracleInput(1.0d, view, branch), IndependentOracleContract.CandidateStrictTarget);
                Assert.That(result.StopState, Is.EqualTo(LightSpaceOracleStopState.Accepted), view + "/" + branch);
                Assert.That(IndependentOracleContract.CandidateAnalyticalPass(IndependentOracleContract.EvaluateP1Analytical(view, branch), result.Value, result.EstimatedError), Is.True, view + "/" + branch);
            }
        }

        /// <summary>Requires the p=1 Normal strict candidate to stop without committing a default zero leaf.</summary>
        [Test]
        public void IndependentOracleCandidateP1NormalStrictFailureIsFailClosed()
        {
            var input = new IndependentOracleInput(1.0d, 0.0d, IndependentOracleBranch.Normal);
            LightSpaceOracleResult result = LightSpaceOracleContractAlignedCandidate.Integrate(input, IndependentOracleContract.CandidateStrictTarget);
            Assert.That(result.StopState, Is.EqualTo(LightSpaceOracleStopState.RootTopologyFailure));
            Assert.That(result.StopState == LightSpaceOracleStopState.Accepted && result.Value == 0.0d && result.EstimatedError == 0.0d, Is.False);
            Assert.That(double.IsNaN(result.Value), Is.True); Assert.That(double.IsNaN(result.EstimatedError), Is.True);
            Assert.That(result.Evaluations, Is.EqualTo(750)); Assert.That(result.Panels, Is.EqualTo(1));
            Assert.That(result.Topology, Is.Empty);
        }

        /// <summary>Requires the strict p=1 Normal root rejection to be single, deterministic, and observer-neutral.</summary>
        [Test]
        public void IndependentOracleCandidateP1NormalStrictRootTopologyFailureTraceIsDeterministic()
        {
            var input = new IndependentOracleInput(1.0d, 0.0d, IndependentOracleBranch.Normal); var observed = new LightSpaceOracleCandidateDiagnosticSink(); var repeated = new LightSpaceOracleCandidateDiagnosticSink();
            LightSpaceOracleResult direct = LightSpaceOracleContractAlignedCandidate.Integrate(input, IndependentOracleContract.CandidateStrictTarget);
            LightSpaceOracleResult enabled = LightSpaceOracleContractAlignedCandidate.Integrate(input, IndependentOracleContract.CandidateStrictTarget, observed);
            LightSpaceOracleResult enabledRepeated = LightSpaceOracleContractAlignedCandidate.Integrate(input, IndependentOracleContract.CandidateStrictTarget, repeated);
            AssertResults(direct, enabled); AssertResults(enabled, enabledRepeated); Assert.That(observed.Digest, Is.EqualTo(repeated.Digest)); Assert.That(observed.Records, Is.EqualTo(1));
            Assert.That(observed.FirstRootTopologyFailure.HasValue, Is.True); Assert.That(repeated.FirstRootTopologyFailure.HasValue, Is.True);
            AssertRootTopologyFailure(observed.FirstRootTopologyFailure.Value); AssertRootTopologyFailure(repeated.FirstRootTopologyFailure.Value);
            Assert.That(enabled.StopState, Is.EqualTo(LightSpaceOracleStopState.RootTopologyFailure)); Assert.That(enabled.Evaluations, Is.EqualTo(750)); Assert.That(enabled.Panels, Is.EqualTo(1)); Assert.That(enabled.Topology, Is.Empty);
        }

        /// <summary>Requires private 9/17 and 17/33 rules, root partitions, and leaf-error composition to retain frozen semantics.</summary>
        [Test]
        public void IndependentOracleCandidateUsesFrozenRulesRootsAndLeafErrorComposition()
        {
            Assert.That(LightSpaceOracleContractAlignedCandidate.MaximumDepth, Is.EqualTo(22)); Assert.That(LightSpaceOracleContractAlignedCandidate.MaximumPanels, Is.EqualTo(262144)); Assert.That(LightSpaceOracleContractAlignedCandidate.MaximumEvaluations, Is.EqualTo(4000000));
            AssertRule(LightSpaceOracleCandidateQuadrature.ClenshawCurtis(9), true); AssertRule(LightSpaceOracleCandidateQuadrature.ClenshawCurtis(17), true);
            AssertRule(LightSpaceOracleCandidateQuadrature.FejerII(17), false); AssertRule(LightSpaceOracleCandidateQuadrature.FejerII(33), false);
            Assert.That(LightSpaceOracleContractAlignedCandidate.ComposeLeafError(2.0d, 3.5d, 0.25d), Is.EqualTo(1.75d)); Assert.That(double.IsNaN(LightSpaceOracleContractAlignedCandidate.ComposeLeafError(1.0d, 2.0d, -1.0d)), Is.True);
            AssertPartition(new IndependentOracleInput(0.089d, 0.5d, IndependentOracleBranch.Normal), 0.5d); AssertPartition(new IndependentOracleInput(1.0d, 0.0d, IndependentOracleBranch.Switch), 0.5d);
        }

        /// <summary>Requires candidate-local atomic theta boundaries to cover every sampled node domain exactly once.</summary>
        [Test]
        public void IndependentOracleCandidateThetaPartitionsCoverEveryNodeDomainExactlyOnce()
        {
            foreach (IndependentOracleRepresentativeRow row in IndependentOracleContract.RepresentativeRows)
                AssertPartition(row.Input, 0.5d);
        }

        /// <summary>Requires direct invalid-input stops, deterministic canonical topology, and frozen cap constants.</summary>
        [Test]
        public void IndependentOracleCandidateSchedulerAndCapsAreDeterministicAndFailClosed()
        {
            var invalid = new IndependentOracleInput(double.NaN, 0.5d, IndependentOracleBranch.Normal);
            LightSpaceOracleResult invalidResult = LightSpaceOracleContractAlignedCandidate.Integrate(invalid, IndependentOracleContract.CandidateBaseTarget);
            LightSpaceOracleResult invalidTarget = LightSpaceOracleContractAlignedCandidate.Integrate(IndependentOracleContract.RepresentativeRows[0].Input, -1.0d);
            LightSpaceOracleResult first = LightSpaceOracleContractAlignedCandidate.Integrate(IndependentOracleContract.RepresentativeRows[0].Input, IndependentOracleContract.CandidateStrictTarget);
            LightSpaceOracleResult repeated = LightSpaceOracleContractAlignedCandidate.Integrate(IndependentOracleContract.RepresentativeRows[0].Input, IndependentOracleContract.CandidateStrictTarget);
            Assert.That(invalidResult.StopState, Is.EqualTo(LightSpaceOracleStopState.NonFiniteInput)); Assert.That(double.IsNaN(invalidResult.Value), Is.True); Assert.That(invalidTarget.StopState, Is.EqualTo(LightSpaceOracleStopState.NonFiniteInput));
            AssertAccepted(first, IndependentOracleContract.RepresentativeRows[0], IndependentOracleContract.CandidateStrictTarget); Assert.That(first.Topology, Is.Not.Empty); AssertResults(first, repeated);
        }

        /// <summary>Requires bounded write-only candidate diagnostics to preserve direct numerical results and deterministic identity.</summary>
        [Test]
        public void IndependentOracleCandidateDiagnosticsAccountEveryReservedEvaluation()
        {
            IndependentOracleInput input = IndependentOracleContract.RepresentativeRows[0].Input; var observed = new LightSpaceOracleCandidateDiagnosticSink(); var repeated = new LightSpaceOracleCandidateDiagnosticSink();
            LightSpaceOracleResult direct = LightSpaceOracleContractAlignedCandidate.Integrate(input, IndependentOracleContract.CandidateStrictTarget);
            LightSpaceOracleResult withDiagnostics = LightSpaceOracleContractAlignedCandidate.Integrate(input, IndependentOracleContract.CandidateStrictTarget, observed);
            LightSpaceOracleResult repeatedDiagnostics = LightSpaceOracleContractAlignedCandidate.Integrate(input, IndependentOracleContract.CandidateStrictTarget, repeated);
            AssertResults(direct, withDiagnostics); AssertResults(withDiagnostics, repeatedDiagnostics); Assert.That(observed.Records, Is.InRange(1, 128)); Assert.That(observed.Digest, Is.EqualTo(repeated.Digest));
        }

        /// <summary>Requires candidate source to exclude retained prototype, legacy, witness, and contract numerical helpers.</summary>
        [Test]
        public void IndependentOracleContractAlignedCandidateHasNoReferenceNumericalDependencies()
        {
            string[] forbidden = { "LightSpaceOracleQuadrature", "LightSpaceOracleReferencePrototype", "IndependentOracleContract", "AdaptivePrimary", "AdaptiveCrossCheck", "KronrodWitness", "IndependentOracleWitness", "PureBasePbrMultipleScatteringReference", "PureBasePbrSafeNormalize", "PureBasePbrEvaluateSmithJointGgxVisibility", "EvaluateGuardedTerms" };
            foreach (string path in CandidateSourcePaths())
            {
                string source = File.ReadAllText(path); foreach (string token in forbidden) Assert.That(source, Does.Not.Contain(token), Path.GetFileName(path) + " references " + token);
                Assert.That(Regex.IsMatch(source, @"diagnostics\s*\.\s*(?!(?:Record|RecordFirstRootTopologyFailure)\b)"), Is.False, Path.GetFileName(path) + " reads diagnostic sink state");
            }
        }

        /// <summary>Requires candidate execution to avoid cache, artifact, and shared telemetry mutation by construction.</summary>
        [Test]
        public void IndependentOracleCandidateIsLegacyCacheArtifactAndTelemetryIsolated()
        {
            foreach (string path in CandidateSourcePaths())
            {
                string source = File.ReadAllText(path); Assert.That(source, Does.Not.Contain("File.")); Assert.That(source, Does.Not.Contain("Directory.")); Assert.That(source, Does.Not.Contain("PlayerPrefs"));
            }
        }

        /// <summary>Gets the two exact p=0.089 grazing branch identities.</summary>
        private static IReadOnlyList<IndependentOracleInput> MinimumGrazingInputs() => new[]
        {
            new IndependentOracleInput(BitConverter.Int64BitsToDouble(unchecked((long)0x3FB6C8B439581062UL)), 0.0d, IndependentOracleBranch.Normal),
            new IndependentOracleInput(BitConverter.Int64BitsToDouble(unchecked((long)0x3FB6C8B439581062UL)), 0.0d, IndependentOracleBranch.Switch)
        };

        /// <summary>Gets the unchanged base and strict diagnostic targets.</summary>
        private static IReadOnlyList<double> Targets() => new[] { IndependentOracleContract.CandidateBaseTarget, IndependentOracleContract.CandidateStrictTarget };

        /// <summary>Gets exactly the three authoritative candidate implementation sources.</summary>
        private static IReadOnlyList<string> CandidateSourcePaths()
        {
            string directory = Path.Combine(Application.dataPath, "..", "Packages", "jp.penguin.purebase", "Tests", "Daily", "Editor");
            string[] paths = Directory.GetFiles(directory, "PureBasePbrMultipleScatteringLightSpaceOracleContractAligned*.cs", SearchOption.TopDirectoryOnly); Array.Sort(paths, StringComparer.Ordinal);
            Assert.That(paths.Length, Is.EqualTo(3)); return paths;
        }

        /// <summary>Requires one private candidate rule to remain symmetric, positive, and normalized.</summary>
        private static void AssertRule(LightSpaceOracleCandidateRuleNode[] rule, bool includesEndpoints)
        {
            double weight = 0.0d; foreach (LightSpaceOracleCandidateRuleNode node in rule) { Assert.That(node.Weight, Is.GreaterThan(0.0d)); Assert.That(node.Coordinate, Is.InRange(-1.0d, 1.0d)); weight += node.Weight; }
            bool firstIsEndpoint = Math.Abs(Math.Abs(rule[0].Coordinate) - 1.0d) <= 2.0e-15d;
            Assert.That(weight, Is.EqualTo(2.0d).Within(2.0e-13d)); Assert.That(firstIsEndpoint, Is.EqualTo(includesEndpoints));
        }

        /// <summary>Requires one local root partition to form an ordered exact half-azimuth cover.</summary>
        private static void AssertPartition(IndependentOracleInput input, double radial)
        {
            Assert.That(LightSpaceOracleContractAlignedCandidate.TryDeriveThetaPartition(input, radial, out LightSpaceOracleCandidateThetaPartition partition), Is.True, input.P + "/" + input.NdotV + "/" + radial);
            Assert.That(partition.Boundaries[0], Is.EqualTo(0.0d)); Assert.That(partition.Boundaries[partition.Boundaries.Length - 1], Is.EqualTo(Math.PI));
            double measure = 0.0d; for (int index = 0; index + 1 < partition.Boundaries.Length; index++) { Assert.That(partition.Boundaries[index], Is.LessThan(partition.Boundaries[index + 1])); measure += partition.Boundaries[index + 1] - partition.Boundaries[index]; }
            Assert.That(measure, Is.EqualTo(Math.PI).Within(2.0e-15d));
        }

        /// <summary>Requires one direct candidate result to be finite, accepted, and within frozen ceilings.</summary>
        private static void AssertAccepted(LightSpaceOracleResult result, IndependentOracleRepresentativeRow row, double target)
        {
            Assert.That(result.StopState, Is.EqualTo(LightSpaceOracleStopState.Accepted), row.PBits + "/" + row.NdotVBits + "/" + row.Input.Branch + "/" + target);
            Assert.That(double.IsNaN(result.Value) || double.IsInfinity(result.Value), Is.False); Assert.That(double.IsNaN(result.EstimatedError) || double.IsInfinity(result.EstimatedError), Is.False);
            Assert.That(result.EstimatedError, Is.LessThanOrEqualTo(target)); Assert.That(result.Evaluations, Is.LessThanOrEqualTo(LightSpaceOracleContractAlignedCandidate.MaximumEvaluations)); Assert.That(result.Panels, Is.LessThanOrEqualTo(LightSpaceOracleContractAlignedCandidate.MaximumPanels));
        }

        /// <summary>Compares terminal fields and the trace identity while requiring bounded capture.</summary>
        private static void AssertTrace(LightSpaceOracleDiagnosticTrace observed, LightSpaceOracleDiagnosticTrace repeated, LightSpaceOracleDiagnosticTrace disabled)
        {
            Assert.That(observed.IsBounded, Is.True); Assert.That(observed.Digest, Is.EqualTo(repeated.Digest));
            AssertResults(observed.Result, repeated.Result); AssertResults(disabled.Result, observed.Result);
            Assert.That(disabled.Leaves, Is.Empty); Assert.That(disabled.Nodes, Is.Empty); Assert.That(disabled.Intervals, Is.Empty);
        }

        /// <summary>Requires a terminal diagnostic to retain its partial node and theta-interval evidence.</summary>
        private static void AssertStoppedHierarchy(LightSpaceOracleDiagnosticTrace trace)
        {
            Assert.That(trace.Result.StopState, Is.Not.EqualTo(LightSpaceOracleStopState.Accepted)); Assert.That(trace.FirstFailure.Path.IsValid, Is.True); Assert.That(trace.FirstFailure.RadialRuleIndex, Is.GreaterThanOrEqualTo(0)); Assert.That(trace.FirstFailure.IntervalIndex, Is.GreaterThanOrEqualTo(0));
            LightSpaceOracleDiagnosticNode node = default; bool nodeFound = false; foreach (LightSpaceOracleDiagnosticNode current in trace.Nodes) if (Matches(current.Path, trace.FirstFailure.Path) && current.RuleIndex == trace.FirstFailure.RadialRuleIndex) { node = current; nodeFound = true; }
            LightSpaceOracleDiagnosticInterval interval = default; bool intervalFound = false; foreach (LightSpaceOracleDiagnosticInterval current in trace.Intervals) if (Matches(current.Path, trace.FirstFailure.Path) && current.RuleIndex == trace.FirstFailure.RadialRuleIndex && current.IntervalIndex == trace.FirstFailure.IntervalIndex) { interval = current; intervalFound = true; }
            Assert.That(nodeFound, Is.True); Assert.That(intervalFound, Is.True); Assert.That(interval.Complete, Is.False); Assert.That(interval.ScalarCalls, Is.GreaterThan(0)); Assert.That(interval.ScalarCalls, Is.EqualTo(interval.LastEvaluation - interval.FirstEvaluation + 1));
            Assert.That(interval.LastEvaluation, Is.EqualTo(trace.Result.Evaluations)); Assert.That(node.ScalarCalls, Is.GreaterThanOrEqualTo(interval.ScalarCalls)); Assert.That(Math.Abs(interval.Coarse) + Math.Abs(interval.Fine), Is.GreaterThan(0.0d)); Assert.That(trace.FirstFailure.NextScalarReservation, Is.EqualTo(trace.Result.Evaluations + 1));
        }

        /// <summary>Rebuilds every retained concentration summary from interval aggregates in its frozen order.</summary>
        private static void AssertConcentrations(LightSpaceOracleDiagnosticTrace trace)
        {
            AssertConcentration(trace.Intervals, trace.ContributionConcentration, 0); AssertConcentration(trace.Intervals, trace.ErrorConcentration, 1); AssertConcentration(trace.Intervals, trace.WorkConcentration, 2);
        }

        /// <summary>Checks one top-eight interval concentration summary against its raw bounded interval ledger.</summary>
        private static void AssertConcentration(IReadOnlyList<LightSpaceOracleDiagnosticInterval> intervals, IReadOnlyList<LightSpaceOracleDiagnosticConcentration> actual, int mode)
        {
            var expected = new List<LightSpaceOracleDiagnosticConcentration>(); foreach (LightSpaceOracleDiagnosticInterval interval in intervals) expected.Add(new LightSpaceOracleDiagnosticConcentration(interval.Path, interval.RuleIndex, interval.IntervalIndex, mode == 0 ? Math.Abs(interval.Fine) : mode == 1 ? Math.Abs(interval.Fine - interval.Coarse) : interval.ScalarCalls));
            expected.Sort(CompareConcentration); if (expected.Count > 8) expected.RemoveRange(8, expected.Count - 8); Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int index = 0; index < expected.Count; index++) { Assert.That(actual[index].Path.Depth, Is.EqualTo(expected[index].Path.Depth)); Assert.That(actual[index].Path.BinaryPath, Is.EqualTo(expected[index].Path.BinaryPath)); Assert.That(actual[index].RuleIndex, Is.EqualTo(expected[index].RuleIndex)); Assert.That(actual[index].IntervalIndex, Is.EqualTo(expected[index].IntervalIndex)); Assert.That(actual[index].Value, Is.EqualTo(expected[index].Value)); }
        }

        /// <summary>Compares concentration entries by value, spatial path, radial rule, then theta interval.</summary>
        private static int CompareConcentration(LightSpaceOracleDiagnosticConcentration left, LightSpaceOracleDiagnosticConcentration right)
        {
            int value = right.Value.CompareTo(left.Value); if (value != 0) return value; int path = left.Path.CompareSpatial(right.Path); if (path != 0) return path; int rule = left.RuleIndex.CompareTo(right.RuleIndex); return rule != 0 ? rule : left.IntervalIndex.CompareTo(right.IntervalIndex);
        }

        /// <summary>Checks every terminal result field, including diagnostic-only depth and topology.</summary>
        private static void AssertResults(LightSpaceOracleResult actual, LightSpaceOracleResult expected)
        {
            Assert.That(actual.Value, Is.EqualTo(expected.Value)); Assert.That(actual.EstimatedError, Is.EqualTo(expected.EstimatedError)); Assert.That(actual.Evaluations, Is.EqualTo(expected.Evaluations)); Assert.That(actual.Panels, Is.EqualTo(expected.Panels)); Assert.That(actual.MaximumDepth, Is.EqualTo(expected.MaximumDepth)); Assert.That(actual.StopState, Is.EqualTo(expected.StopState)); Assert.That(actual.Topology, Is.EqualTo(expected.Topology));
        }

        /// <summary>Requires the known first guard residual rejection to retain only locally observed root facts.</summary>
        private static void AssertRootTopologyFailure(LightSpaceOracleCandidateRootTopologyFailure failure)
        {
            Assert.That(failure.Kind, Is.EqualTo(LightSpaceOracleCandidateRootKind.Guard)); Assert.That(failure.RadialCoordinate, Is.GreaterThan(0.0d).And.LessThan(0.01d));
            Assert.That(failure.Target, Is.EqualTo(1.0e-6d)); Assert.That(double.IsNaN(failure.RawCosine) || double.IsInfinity(failure.RawCosine), Is.False);
            Assert.That(failure.Correction, Is.EqualTo(LightSpaceOracleCandidateCosineCorrection.None)); Assert.That(double.IsNaN(failure.Theta) || double.IsInfinity(failure.Theta), Is.False);
            Assert.That(failure.ResidualValidity, Is.EqualTo(LightSpaceOracleCandidateRootResidualValidity.Invalid)); Assert.That(failure.InteriorPresence, Is.EqualTo(LightSpaceOracleCandidateRootInteriorPresence.Present));
            Assert.That(failure.SemanticOrder, Is.EqualTo(LightSpaceOracleCandidateRootSemanticOrder.NotReached));
        }

        /// <summary>Gets whether two retained diagnostic path identities are exactly equal.</summary>
        private static bool Matches(IndependentOracleCanonicalPath left, IndependentOracleCanonicalPath right) => left.Depth == right.Depth && left.BinaryPath == right.BinaryPath;
    }
}
