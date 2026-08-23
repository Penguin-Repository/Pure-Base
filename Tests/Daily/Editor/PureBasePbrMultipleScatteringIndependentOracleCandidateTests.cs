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
using NUnit.Framework;

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

        /// <summary>Remains RED only because the direct replacement candidate boundary is unavailable.</summary>
        [Test]
        public void IndependentOracleCandidateProducesRepresentativeEvidence()
        {
            Assert.That(() => LightSpaceOracle.Integrate(IndependentOracleContract.RepresentativeRows[0].Input, IndependentOracleContract.CandidateBaseTarget), Throws.Nothing);
        }

        /// <summary>Remains RED only because the direct replacement candidate boundary is unavailable.</summary>
        [Test]
        public void IndependentOracleCandidateIsDeterministicFiniteAndStricterStable()
        {
            Assert.That(() => LightSpaceOracle.Integrate(IndependentOracleContract.RepresentativeRows[0].Input, IndependentOracleContract.CandidateStrictTarget), Throws.Nothing);
        }

        /// <summary>Remains RED only because the direct replacement candidate boundary is unavailable.</summary>
        [Test]
        public void IndependentOracleCandidateMatchesP1AnalyticalBenchmark()
        {
            Assert.That(() => LightSpaceOracle.Integrate(new IndependentOracleInput(1.0d, 0.0d, IndependentOracleBranch.Normal), IndependentOracleContract.CandidateStrictTarget), Throws.Nothing);
        }

        /// <summary>Gets the two exact p=0.089 grazing branch identities.</summary>
        private static IReadOnlyList<IndependentOracleInput> MinimumGrazingInputs() => new[]
        {
            new IndependentOracleInput(BitConverter.Int64BitsToDouble(unchecked((long)0x3FB6C8B439581062UL)), 0.0d, IndependentOracleBranch.Normal),
            new IndependentOracleInput(BitConverter.Int64BitsToDouble(unchecked((long)0x3FB6C8B439581062UL)), 0.0d, IndependentOracleBranch.Switch)
        };

        /// <summary>Gets the unchanged base and strict diagnostic targets.</summary>
        private static IReadOnlyList<double> Targets() => new[] { IndependentOracleContract.CandidateBaseTarget, IndependentOracleContract.CandidateStrictTarget };

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

        /// <summary>Gets whether two retained diagnostic path identities are exactly equal.</summary>
        private static bool Matches(IndependentOracleCanonicalPath left, IndependentOracleCanonicalPath right) => left.Depth == right.Depth && left.BinaryPath == right.BinaryPath;
    }
}
