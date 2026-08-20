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

// Tests deterministic, cache-isolated telemetry for bounded PBR candidate selection.

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Tests deterministic, cache-isolated telemetry for bounded PBR candidate selection.</summary>
    public sealed partial class PureBasePbrMultipleScatteringCompensationTests
    {
        /// <summary>Requires repeated explicit finite probes to return identical structured budget evidence.</summary>
        [Test]
        public void SelectionProbeReturnsDeterministicBudgetTrace()
        {
            SelectionProbeResult first = AdaptiveProtocol.RunSelectionProbeForTest(new SelectionExecutionBudget(64));
            SelectionProbeResult second = AdaptiveProtocol.RunSelectionProbeForTest(new SelectionExecutionBudget(64));
            Assert.That(first.State, Is.EqualTo(SelectionProbeState.BudgetExhausted));
            Assert.That(first.Selection, Is.Null);
            Assert.That(first.Exception, Is.TypeOf<InvalidOperationException>());
            AssertProbesEqual(first, second);
        }

        /// <summary>Requires finite probe buckets to account for every accepted reservation and preserve failure evidence.</summary>
        [Test]
        public void SelectionProbeTraceAccountsForEveryAcceptedReservation()
        {
            SelectionProbeResult probe = AdaptiveProtocol.RunSelectionProbeForTest(new SelectionExecutionBudget(64));
            long reservations = CountReservations(probe.Trace);
            Assert.That(probe.State, Is.EqualTo(SelectionProbeState.BudgetExhausted));
            Assert.That(reservations, Is.EqualTo(probe.Trace.Used));
            Assert.That(probe.Trace.Used, Is.EqualTo(probe.Trace.Limit));
            Assert.That(probe.Trace.LastAccepted.HasValue, Is.True);
            Assert.That(probe.Trace.FirstRejection.HasValue, Is.True);
            Assert.That(probe.Trace.FirstRejection.Value.KernelStarted, Is.False);
            Assert.That(probe.Exception.Message, Is.EqualTo(probe.Trace.FirstRejection.Value.ToString()));
        }

        /// <summary>Requires the finite probe to avoid both canonical artifact mutation and lazy-cache access.</summary>
        [Test]
        public void SelectionProbeIsCacheAndArtifactIsolated()
        {
            string path = AdaptiveProtocol.CanonicalArtifactPath; bool existed = File.Exists(path); byte[] before = existed ? File.ReadAllBytes(path) : null;
            var field = typeof(PureBasePbrMultipleScatteringFurnaceOracle).GetField("SelectionCache", BindingFlags.NonPublic | BindingFlags.Static);
            var cache = (Lazy<AdaptiveSelection>)field.GetValue(null); bool cacheBefore = cache.IsValueCreated;
            SelectionProbeResult probe = AdaptiveProtocol.RunSelectionProbeForTest(new SelectionExecutionBudget(64));
            Assert.That(probe.State, Is.EqualTo(SelectionProbeState.BudgetExhausted));
            Assert.That(cache.IsValueCreated, Is.EqualTo(cacheBefore));
            Assert.That(File.Exists(path), Is.EqualTo(existed)); if (existed) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        /// <summary>Observes one explicit 128-reservation prefix without authorizing further numerical work.</summary>
        [Test]
        public void SelectionProbeObservesExplicit128ReservationPrefix()
        {
            string path = AdaptiveProtocol.CanonicalArtifactPath; bool existed = File.Exists(path); byte[] before = existed ? File.ReadAllBytes(path) : null;
            var field = typeof(PureBasePbrMultipleScatteringFurnaceOracle).GetField("SelectionCache", BindingFlags.NonPublic | BindingFlags.Static);
            var cache = (Lazy<AdaptiveSelection>)field.GetValue(null); bool cacheBefore = cache.IsValueCreated;
            SelectionProbeResult first = AdaptiveProtocol.RunSelectionProbeForTest(new SelectionExecutionBudget(128));
            SelectionProbeResult second = AdaptiveProtocol.RunSelectionProbeForTest(new SelectionExecutionBudget(128));
            Assert.That(cache.IsValueCreated, Is.EqualTo(cacheBefore));
            Assert.That(File.Exists(path), Is.EqualTo(existed)); if (existed) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
            AssertProbesEqual(first, second);
            AssertTypedNonfaultPrefixState(first);
            TestContext.Progress.WriteLine(SelectionProbeTraceRenderer.Render(first));
        }

        /// <summary>Requires primary exhaustion to reject before the shared scalar kernel starts.</summary>
        [Test]
        public void SelectionBudgetRejectsBeforePrimaryKernelWork()
        {
            SelectionExecutionBudget budget = new SelectionExecutionBudget(0);
            AdaptiveResult result = AdaptivePrimary.Integrate(ProbeSettings(), 0.5d, 0.5d, false, budget, ProbeContext("primary"));
            AssertRejectedBeforeKernel(result, budget, "primary");
        }

        /// <summary>Requires cross-check exhaustion to reject before the shared scalar kernel starts.</summary>
        [Test]
        public void SelectionBudgetRejectsBeforeCrossKernelWork()
        {
            SelectionExecutionBudget budget = new SelectionExecutionBudget(0);
            AdaptiveResult result = AdaptiveCrossCheck.Integrate(ProbeSettings(), 0.5d, 0.5d, false, budget, ProbeContext("cross-check"));
            AssertRejectedBeforeKernel(result, budget, "cross-check");
        }

        /// <summary>Requires witness exhaustion to reject before the shared scalar kernel starts.</summary>
        [Test]
        public void SelectionBudgetRejectsBeforeWitnessKernelWork()
        {
            SelectionExecutionBudget budget = new SelectionExecutionBudget(0);
            AdaptiveResult result = KronrodWitness.Integrate(ProbeSettings(), 0.5d, 0.5d, false, budget, ProbeContext("witness"));
            AssertRejectedBeforeKernel(result, budget, "witness");
        }

        /// <summary>Requires injected failures to remain outside canonical artifact persistence.</summary>
        [Test]
        public void SelectionBudgetFailureCreatesNoCanonicalArtifact()
        {
            string path = AdaptiveProtocol.CanonicalArtifactPath; bool existed = File.Exists(path); byte[] before = existed ? File.ReadAllBytes(path) : null;
            Assert.Throws<InvalidOperationException>(() => AdaptiveProtocol.RunSelectionForTest(new SelectionExecutionBudget(0)));
            Assert.That(File.Exists(path), Is.EqualTo(existed)); if (existed) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        /// <summary>Requires injected selection execution to leave the production lazy cache untouched.</summary>
        [Test]
        public void SelectionBudgetInjectedRunnerDoesNotAccessSelectionCache()
        {
            var field = typeof(PureBasePbrMultipleScatteringFurnaceOracle).GetField("SelectionCache", BindingFlags.NonPublic | BindingFlags.Static);
            var cache = (Lazy<AdaptiveSelection>)field.GetValue(null); bool before = cache.IsValueCreated;
            Assert.Throws<InvalidOperationException>(() => AdaptiveProtocol.RunSelectionForTest(new SelectionExecutionBudget(0)));
            Assert.That(cache.IsValueCreated, Is.EqualTo(before));
        }

        /// <summary>Requires repeated bounded selection attempts to report the identical first stage location.</summary>
        [Test]
        public void SelectionBudgetReportsDeterministicStageContext()
        {
            string first = SelectionFailure(0); string second = SelectionFailure(0);
            Assert.That(first, Is.EqualTo(second)); Assert.That(first, Does.Contain("candidate=calibration-a stage=calibration branch=normal grid=original index=0"));
            Assert.That(first, Does.Contain("coordinateBits=p=0x3FB6C8B439581062,ndotV=0x0000000000000000 coordinate=p=0.089,ndotV=0 path=primary used=0 limit=0 kernelStarted=false"));
        }

        /// <summary>Requires optional accounting to preserve each path's no-budget numerical result and order.</summary>
        [Test]
        public void SelectionBudgetLeavesNoBudgetPathOutputsUnchanged()
        {
            AdaptiveSettings settings = ProbeSettings(); AssertResultsEqual(AdaptivePrimary.Integrate(settings, 0.5d, 0.5d, false), AdaptivePrimary.Integrate(settings, 0.5d, 0.5d, false, new SelectionExecutionBudget(long.MaxValue), ProbeContext("primary")));
            AssertResultsEqual(AdaptiveCrossCheck.Integrate(settings, 0.5d, 0.5d, false), AdaptiveCrossCheck.Integrate(settings, 0.5d, 0.5d, false, new SelectionExecutionBudget(long.MaxValue), ProbeContext("cross-check")));
            AssertResultsEqual(KronrodWitness.Integrate(settings, 0.5d, 0.5d, false), KronrodWitness.Integrate(settings, 0.5d, 0.5d, false, new SelectionExecutionBudget(long.MaxValue), ProbeContext("witness")));
        }

        /// <summary>Requires an explicit finite pilot to return selection evidence or its first deterministic failure.</summary>
        [Test]
        public void BoundedKronrodSelectionPilotTerminates()
        {
            var budget = new SelectionExecutionBudget(64);
            try { Assert.That(AdaptiveProtocol.RunSelectionForTest(budget).IsSelected, Is.True); }
            catch (InvalidOperationException exception) { Assert.That(budget.FirstFailure.HasValue, Is.True); Assert.That(exception.Message, Is.EqualTo(budget.FirstFailure.Value.ToString())); }
            Assert.That(budget.Used, Is.LessThanOrEqualTo(budget.Limit));
        }

        /// <summary>Requires a typed finite prefix result without treating it as a product selection.</summary>
        private static void AssertTypedNonfaultPrefixState(SelectionProbeResult probe)
        {
            Assert.That(probe.State, Is.Not.EqualTo(SelectionProbeState.Faulted));
            if (probe.State == SelectionProbeState.BudgetExhausted) { AssertExhaustedTrace(probe); return; }
            if (probe.State == SelectionProbeState.NumericallyRejected)
            {
                Assert.That(probe.Selection, Is.Null);
                Assert.That(probe.Exception, Is.TypeOf<SelectionNumericalRejectionException>());
                return;
            }

            Assert.That(probe.State, Is.EqualTo(SelectionProbeState.Selected));
        }

        /// <summary>Requires an exhausted prefix to preserve exact bucket and first-rejection invariants.</summary>
        private static void AssertExhaustedTrace(SelectionProbeResult probe)
        {
            Assert.That(probe.Selection, Is.Null); Assert.That(probe.Exception, Is.TypeOf<InvalidOperationException>());
            Assert.That(CountReservations(probe.Trace), Is.EqualTo(probe.Trace.Used)); Assert.That(probe.Trace.Used, Is.EqualTo(probe.Trace.Limit));
            Assert.That(probe.Trace.FirstRejection.HasValue, Is.True); Assert.That(probe.Trace.FirstRejection.Value.KernelStarted, Is.False);
            Assert.That(probe.Exception.Message, Is.EqualTo(probe.Trace.FirstRejection.Value.ToString()));
        }

        /// <summary>Counts accepted reservations across immutable first-occurrence-ordered buckets.</summary>
        private static long CountReservations(SelectionExecutionTrace trace)
        {
            long reservations = 0;
            foreach (SelectionExecutionTraceBucket bucket in trace.Buckets) reservations += bucket.Reservations;
            return reservations;
        }

        /// <summary>Creates safe direct-path settings that accept their initial bounded panels.</summary>
        private static AdaptiveSettings ProbeSettings() => new AdaptiveSettings("selection-budget-probe", 100.0d, 0.0d, 100.0d, 0.0d, 2, 32, 1000000);

        /// <summary>Creates one stable direct-path location used by the three budget rejection probes.</summary>
        private static SelectionExecutionContext ProbeContext(string path) => new SelectionExecutionContext("probe", "calibration", "normal", "probe", 0, new AdaptiveCoordinate(0.5d, 0.5d), path);

        /// <summary>Asserts an attempted scalar kernel was rejected before any shared kernel work began.</summary>
        private static void AssertRejectedBeforeKernel(AdaptiveResult result, SelectionExecutionBudget budget, string path)
        {
            Assert.That(budget.FirstFailure.HasValue, Is.True); SelectionExecutionFailure failure = budget.FirstFailure.Value;
            Assert.That(failure.Context.Path, Is.EqualTo(path)); Assert.That(failure.Used, Is.Zero); Assert.That(failure.KernelStarted, Is.False); Assert.That(result.Diagnostic, Is.EqualTo(failure.ToString()));
        }

        /// <summary>Runs one zero-budget selection attempt and returns its first deterministic diagnostic.</summary>
        private static string SelectionFailure(long limit)
        {
            var budget = new SelectionExecutionBudget(limit); InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => AdaptiveProtocol.RunSelectionForTest(budget));
            Assert.That(budget.FirstFailure.HasValue, Is.True); return exception.Message;
        }

        /// <summary>Compares all observable adaptive-result fields without introducing a tolerance change.</summary>
        private static void AssertResultsEqual(AdaptiveResult expected, AdaptiveResult actual)
        {
            Assert.That(actual.Value, Is.EqualTo(expected.Value)); Assert.That(actual.Error, Is.EqualTo(expected.Error)); Assert.That(actual.Tolerance, Is.EqualTo(expected.Tolerance));
            Assert.That(actual.Evaluations, Is.EqualTo(expected.Evaluations)); Assert.That(actual.Panels, Is.EqualTo(expected.Panels)); Assert.That(actual.Depth, Is.EqualTo(expected.Depth)); Assert.That(actual.Diagnostic, Is.EqualTo(expected.Diagnostic));
        }

        /// <summary>Compares repeated finite probes without relying on exception diagnostic parsing.</summary>
        private static void AssertProbesEqual(SelectionProbeResult expected, SelectionProbeResult actual)
        {
            Assert.That(actual.State, Is.EqualTo(expected.State));
            Assert.That(actual.Trace.Used, Is.EqualTo(expected.Trace.Used)); Assert.That(actual.Trace.Limit, Is.EqualTo(expected.Trace.Limit));
            Assert.That(actual.Exception == null, Is.EqualTo(expected.Exception == null));
            if (expected.Exception != null) { Assert.That(actual.Exception.GetType(), Is.EqualTo(expected.Exception.GetType())); Assert.That(actual.Exception.Message, Is.EqualTo(expected.Exception.Message)); }
            AssertContextsEqual(expected.Trace.LastAccepted, actual.Trace.LastAccepted); AssertFailuresEqual(expected.Trace.FirstRejection, actual.Trace.FirstRejection);
            Assert.That(actual.Trace.Buckets.Count, Is.EqualTo(expected.Trace.Buckets.Count));
            for (int index = 0; index < expected.Trace.Buckets.Count; index++) AssertBucketsEqual(expected.Trace.Buckets[index], actual.Trace.Buckets[index]);
        }

        /// <summary>Compares nullable raw-coordinate contexts exactly by their binary64 identity.</summary>
        private static void AssertContextsEqual(SelectionExecutionContext? expected, SelectionExecutionContext? actual)
        {
            Assert.That(actual.HasValue, Is.EqualTo(expected.HasValue));
            if (!expected.HasValue) return;
            SelectionExecutionContext expectedValue = expected.Value; SelectionExecutionContext actualValue = actual.Value;
            Assert.That(actualValue.Candidate, Is.EqualTo(expectedValue.Candidate)); Assert.That(actualValue.Stage, Is.EqualTo(expectedValue.Stage)); Assert.That(actualValue.Branch, Is.EqualTo(expectedValue.Branch));
            Assert.That(actualValue.GridName, Is.EqualTo(expectedValue.GridName)); Assert.That(actualValue.GridIndex, Is.EqualTo(expectedValue.GridIndex)); Assert.That(actualValue.Path, Is.EqualTo(expectedValue.Path));
            Assert.That(BitConverter.DoubleToInt64Bits(actualValue.Coordinate.P), Is.EqualTo(BitConverter.DoubleToInt64Bits(expectedValue.Coordinate.P))); Assert.That(BitConverter.DoubleToInt64Bits(actualValue.Coordinate.V), Is.EqualTo(BitConverter.DoubleToInt64Bits(expectedValue.Coordinate.V)));
        }

        /// <summary>Compares optional finite failure context and its immutable reservation counters.</summary>
        private static void AssertFailuresEqual(SelectionExecutionFailure? expected, SelectionExecutionFailure? actual)
        {
            Assert.That(actual.HasValue, Is.EqualTo(expected.HasValue));
            if (!expected.HasValue) return;
            AssertContextsEqual(expected.Value.Context, actual.Value.Context);
            Assert.That(actual.Value.Used, Is.EqualTo(expected.Value.Used)); Assert.That(actual.Value.Limit, Is.EqualTo(expected.Value.Limit));
            Assert.That(actual.Value.KernelStarted, Is.EqualTo(expected.Value.KernelStarted));
        }

        /// <summary>Compares one ordered aggregate bucket without depending on record equality.</summary>
        private static void AssertBucketsEqual(SelectionExecutionTraceBucket expected, SelectionExecutionTraceBucket actual)
        {
            Assert.That(actual.Candidate, Is.EqualTo(expected.Candidate)); Assert.That(actual.Stage, Is.EqualTo(expected.Stage)); Assert.That(actual.Branch, Is.EqualTo(expected.Branch));
            Assert.That(actual.GridName, Is.EqualTo(expected.GridName)); Assert.That(actual.Path, Is.EqualTo(expected.Path)); Assert.That(actual.Reservations, Is.EqualTo(expected.Reservations));
        }
    }
}
