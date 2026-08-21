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

// Tests direct, capture-only primary convergence evidence for frozen calibration-a.

using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Tests direct, capture-only primary convergence evidence for frozen calibration-a.</summary>
    public sealed partial class PureBasePbrMultipleScatteringCompensationTests
    {
        /// <summary>Requires repeated direct captured probes to preserve every deterministic observation.</summary>
        [Test]
        public void DirectPrimaryDiagnosticIsBitExactAndNonfaulted()
        {
            PrimaryDiagnosticRun first = AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest();
            PrimaryDiagnosticRun second = AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest();
            Assert.That(first.State, Is.Not.EqualTo(PrimaryDiagnosticState.Faulted));
            Assert.That(first.Exception, Is.Null);
            AssertRunsEqual(first, second, true);
        }

        /// <summary>Requires capture to preserve the exact direct primary result and reservation trace.</summary>
        [Test]
        public void DirectPrimaryDiagnosticCaptureLeavesDirectRunUnchanged()
        {
            PrimaryDiagnosticRun captured = AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest();
            PrimaryDiagnosticRun uncaptured = AdaptiveProtocol.RunCalibrationAPrimaryWithoutCaptureForTest();
            Assert.That(captured.State, Is.Not.EqualTo(PrimaryDiagnosticState.Faulted));
            Assert.That(uncaptured.Exception, Is.Null);
            AssertAdaptiveResultsEqual(captured.Result, uncaptured.Result);
            AssertTracesEqual(captured.Trace, uncaptured.Trace);
        }

        /// <summary>Requires direct primary observation to bypass the selection cache and canonical artifact path.</summary>
        [Test]
        public void DirectPrimaryDiagnosticIsCacheAndArtifactIsolated()
        {
            string path = AdaptiveProtocol.CanonicalArtifactPath; bool existed = File.Exists(path); byte[] before = existed ? File.ReadAllBytes(path) : null;
            Lazy<AdaptiveSelection> cache = GetSelectionCache(); bool cacheBefore = cache.IsValueCreated;
            PrimaryDiagnosticRun run = AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest();
            Assert.That(run.State, Is.Not.EqualTo(PrimaryDiagnosticState.Faulted));
            Assert.That(cache.IsValueCreated, Is.EqualTo(cacheBefore));
            Assert.That(File.Exists(path), Is.EqualTo(existed)); if (existed) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        /// <summary>Requires budget exhaustion to remain a pre-kernel terminal sample with actual accounting.</summary>
        [Test]
        public void DirectPrimaryDiagnosticReportsActualBudgetExhaustion()
        {
            PrimaryDiagnosticRun run = AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest();
            PrimaryDiagnosticSnapshot snapshot = RequireSnapshot(run); SelectionExecutionFailure rejection = RequireRejection(snapshot.Trace);
            Assert.That(snapshot.State, Is.EqualTo(PrimaryDiagnosticState.BudgetExhausted));
            Assert.That(rejection.KernelStarted, Is.False); Assert.That(snapshot.TerminalSample.KernelStarted, Is.False);
            Assert.That(snapshot.TerminalSample.Stop, Is.EqualTo("selection-budget-pre-kernel"));
            Assert.That(snapshot.Trace.Used, Is.EqualTo(snapshot.Trace.Limit)); Assert.That(snapshot.CompletedKernelWork, Is.EqualTo(snapshot.Trace.Used));
            Assert.That(snapshot.Result.Evaluations, Is.EqualTo(snapshot.CompletedKernelWork + 1)); Assert.That(snapshot.Result.Error, Is.EqualTo(double.PositiveInfinity));
            Assert.That(snapshot.HasFinalLocalError, Is.False); Assert.That(double.IsNaN(snapshot.FinalLocalError), Is.True);
        }

        /// <summary>Requires captured recursive evidence to expose local alternatives to unavailable global DFS scheduler fields.</summary>
        [Test]
        public void DirectPrimaryDiagnosticReportsPartitionsEventsCapsAndUnavailableDfsGlobals()
        {
            PrimaryDiagnosticSnapshot snapshot = RequireSnapshot(AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest());
            Assert.That(snapshot.SplitEvents.Count, Is.GreaterThan(0)); Assert.That(snapshot.Partitions.Count, Is.GreaterThan(0)); Assert.That(snapshot.Work.Count, Is.GreaterThan(0));
            Assert.That(snapshot.MaxDepth, Is.GreaterThanOrEqualTo(snapshot.Result.Depth)); Assert.That(snapshot.MaxPanels, Is.GreaterThanOrEqualTo(snapshot.Result.Panels)); Assert.That(snapshot.MaxEvaluations, Is.GreaterThanOrEqualTo(snapshot.Result.Evaluations));
            Assert.That(snapshot.GlobalQueueAvailable, Is.False); Assert.That(snapshot.PendingIntervalCountAvailable, Is.False); Assert.That(snapshot.SchedulerGlobalLargestErrorAvailable, Is.False);
            Assert.That(CountWork(snapshot), Is.EqualTo(snapshot.CompletedKernelWork)); Assert.That(snapshot.Work.Count, Is.EqualTo(CountInitialPartitions(snapshot))); Assert.That(snapshot.TerminalSample.Axis == "eta" || snapshot.TerminalSample.Axis == "eta-x", Is.True); Assert.That(snapshot.TerminalSample.Partition == "eta" || snapshot.TerminalSample.Partition == "eta-x visibility tail", Is.True);
            Assert.That(HasVerifiedBoundaryLabel(snapshot), Is.True); Assert.That(HasReflectionRidgeLabel(snapshot), Is.False);
            AssertCapturedPartitionsMatchAuthoritativeRules(snapshot);
        }

        /// <summary>Requires a completed kernel nonfinite value to retain its structured stop classification.</summary>
        [Test]
        public void DirectPrimaryDiagnosticReportsStructuredNonfiniteStop()
        {
            PrimaryDiagnosticSnapshot snapshot = RequireSnapshot(AdaptiveProtocol.RunNonfinitePrimaryDiagnosticForTest());
            Assert.That(snapshot.State, Is.EqualTo(PrimaryDiagnosticState.Nonfinite)); Assert.That(snapshot.Result.Diagnostic, Is.EqualTo("nonfinite primary sample"));
            Assert.That(snapshot.TerminalSample.KernelStarted, Is.True); Assert.That(snapshot.TerminalSample.Stop, Is.EqualTo("nonfinite-primary-sample"));
        }

        /// <summary>Requires the compact direct diagnostic summary to be deterministic and failure-payload bounded.</summary>
        [Test]
        public void DirectPrimaryDiagnosticRendersCompactDeterministicEvidence()
        {
            PrimaryDiagnosticRun first = AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest(); PrimaryDiagnosticRun second = AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest();
            Assert.That(first.State, Is.Not.EqualTo(PrimaryDiagnosticState.Faulted)); Assert.That(second.State, Is.Not.EqualTo(PrimaryDiagnosticState.Faulted));
            string rendered = PrimaryDiagnosticTraceRenderer.Render(first); Assert.That(PrimaryDiagnosticTraceRenderer.Render(second), Is.EqualTo(rendered));
            Assert.That(rendered.Length, Is.LessThanOrEqualTo(16000));
            AssertRenderedDiagnosticHash(rendered);
            TestContext.Progress.WriteLine(rendered);
        }

        /// <summary>Gets the private canonical selection cache without forcing its lazy value.</summary>
        private static Lazy<AdaptiveSelection> GetSelectionCache()
        {
            FieldInfo field = typeof(PureBasePbrMultipleScatteringFurnaceOracle).GetField("SelectionCache", BindingFlags.NonPublic | BindingFlags.Static);
            return (Lazy<AdaptiveSelection>)field.GetValue(null);
        }

        /// <summary>Requires a completed capture snapshot before inspecting direct diagnostic state.</summary>
        private static PrimaryDiagnosticSnapshot RequireSnapshot(PrimaryDiagnosticRun run)
        {
            Assert.That(run.State, Is.Not.EqualTo(PrimaryDiagnosticState.Faulted)); Assert.That(run.Snapshot, Is.Not.Null); return run.Snapshot;
        }

        /// <summary>Requires the immutable first rejected scalar reservation from the direct finite budget.</summary>
        private static SelectionExecutionFailure RequireRejection(SelectionExecutionTrace trace)
        {
            Assert.That(trace.FirstRejection.HasValue, Is.True); return trace.FirstRejection.Value;
        }

        /// <summary>Compares result fields exactly without converting their failure sentinel into a local indicator.</summary>
        private static void AssertAdaptiveResultsEqual(AdaptiveResult expected, AdaptiveResult actual)
        {
            AssertDoubleBitsEqual(expected.Value, actual.Value); AssertDoubleBitsEqual(expected.Error, actual.Error); AssertDoubleBitsEqual(expected.Tolerance, actual.Tolerance);
            Assert.That(actual.Evaluations, Is.EqualTo(expected.Evaluations)); Assert.That(actual.Panels, Is.EqualTo(expected.Panels)); Assert.That(actual.Depth, Is.EqualTo(expected.Depth)); Assert.That(actual.Diagnostic, Is.EqualTo(expected.Diagnostic));
        }

        /// <summary>Compares direct run outcome, result, trace, and every captured observation exactly.</summary>
        private static void AssertRunsEqual(PrimaryDiagnosticRun expected, PrimaryDiagnosticRun actual, bool includeSnapshots)
        {
            Assert.That(actual.State, Is.EqualTo(expected.State)); Assert.That(actual.Exception, Is.Null); AssertAdaptiveResultsEqual(expected.Result, actual.Result); AssertTracesEqual(expected.Trace, actual.Trace);
            if (includeSnapshots) AssertSnapshotsEqual(RequireSnapshot(expected), RequireSnapshot(actual));
        }

        /// <summary>Compares the immutable direct capture fields in observation order.</summary>
        private static void AssertSnapshotsEqual(PrimaryDiagnosticSnapshot expected, PrimaryDiagnosticSnapshot actual)
        {
            Assert.That(actual.State, Is.EqualTo(expected.State)); Assert.That(actual.CompletedKernelWork, Is.EqualTo(expected.CompletedKernelWork)); Assert.That(actual.MaxDepth, Is.EqualTo(expected.MaxDepth)); Assert.That(actual.MaxPanels, Is.EqualTo(expected.MaxPanels)); Assert.That(actual.MaxEvaluations, Is.EqualTo(expected.MaxEvaluations)); AssertDoubleBitsEqual(expected.MaximumObservedLocalError, actual.MaximumObservedLocalError); AssertDoubleBitsEqual(expected.MaximumObservedErrorOverLimit, actual.MaximumObservedErrorOverLimit);
            AssertSampleEqual(expected.TerminalSample, actual.TerminalSample); Assert.That(actual.SplitEvents.Count, Is.EqualTo(expected.SplitEvents.Count)); Assert.That(actual.Partitions.Count, Is.EqualTo(expected.Partitions.Count)); Assert.That(actual.Work.Count, Is.EqualTo(expected.Work.Count));
            for (int index = 0; index < expected.SplitEvents.Count; index++) AssertSplitEqual(expected.SplitEvents[index], actual.SplitEvents[index]);
            for (int index = 0; index < expected.Partitions.Count; index++) AssertPartitionEqual(expected.Partitions[index], actual.Partitions[index]);
            for (int index = 0; index < expected.Work.Count; index++) AssertWorkEqual(expected.Work[index], actual.Work[index]);
        }

        /// <summary>Compares one terminal recursive sample interval exactly.</summary>
        private static void AssertSampleEqual(PrimaryDiagnosticSample expected, PrimaryDiagnosticSample actual)
        {
            Assert.That(actual.Axis, Is.EqualTo(expected.Axis)); AssertDoubleBitsEqual(expected.Psi, actual.Psi); AssertDoubleBitsEqual(expected.Left, actual.Left); AssertDoubleBitsEqual(expected.Right, actual.Right); Assert.That(actual.Depth, Is.EqualTo(expected.Depth)); Assert.That(actual.KernelStarted, Is.EqualTo(expected.KernelStarted)); Assert.That(actual.Stop, Is.EqualTo(expected.Stop)); Assert.That(actual.Partition, Is.EqualTo(expected.Partition));
        }

        /// <summary>Compares one split decision and its already-computed embedded-pair limits exactly.</summary>
        private static void AssertSplitEqual(PrimaryDiagnosticSplitEvent expected, PrimaryDiagnosticSplitEvent actual)
        {
            Assert.That(actual.Axis, Is.EqualTo(expected.Axis)); AssertDoubleBitsEqual(expected.Psi, actual.Psi); AssertDoubleBitsEqual(expected.Left, actual.Left); AssertDoubleBitsEqual(expected.Right, actual.Right); Assert.That(actual.Depth, Is.EqualTo(expected.Depth));
            AssertDoubleBitsEqual(expected.Coarse.Value, actual.Coarse.Value); AssertDoubleBitsEqual(expected.Fine.Value, actual.Fine.Value); AssertDoubleBitsEqual(expected.InheritedError, actual.InheritedError); AssertDoubleBitsEqual(expected.RuleDelta, actual.RuleDelta); AssertDoubleBitsEqual(expected.AbsoluteLimit, actual.AbsoluteLimit); AssertDoubleBitsEqual(expected.RelativeLimit, actual.RelativeLimit); AssertDoubleBitsEqual(expected.LocalError, actual.LocalError); AssertDoubleBitsEqual(expected.LocalLimit, actual.LocalLimit); Assert.That(actual.Decision, Is.EqualTo(expected.Decision)); Assert.That(actual.Panels, Is.EqualTo(expected.Panels)); Assert.That(actual.Evaluations, Is.EqualTo(expected.Evaluations));
        }

        /// <summary>Compares one analytic partition's source labels and eta-versus-eta-x selection.</summary>
        private static void AssertPartitionEqual(PrimaryDiagnosticPartition expected, PrimaryDiagnosticPartition actual)
        {
            AssertDoubleBitsEqual(expected.Psi, actual.Psi); AssertDoubleArraysEqual(expected.Boundaries, actual.Boundaries); Assert.That(actual.Labels, Is.EqualTo(expected.Labels)); Assert.That(actual.UseX, Is.EqualTo(expected.UseX));
        }

        /// <summary>Compares one interval's completed scalar-kernel work count.</summary>
        private static void AssertWorkEqual(PrimaryDiagnosticWork expected, PrimaryDiagnosticWork actual)
        {
            Assert.That(actual.PartitionLine, Is.EqualTo(expected.PartitionLine)); Assert.That(actual.InitialPartition, Is.EqualTo(expected.InitialPartition)); Assert.That(actual.Axis, Is.EqualTo(expected.Axis)); AssertDoubleBitsEqual(expected.Psi, actual.Psi); AssertDoubleBitsEqual(expected.Left, actual.Left); AssertDoubleBitsEqual(expected.Right, actual.Right); Assert.That(actual.LeftLabel, Is.EqualTo(expected.LeftLabel)); Assert.That(actual.RightLabel, Is.EqualTo(expected.RightLabel)); Assert.That(actual.UseX, Is.EqualTo(expected.UseX)); Assert.That(actual.Samples, Is.EqualTo(expected.Samples));
        }

        /// <summary>Compares accepted reservation ordering and the immutable first pre-kernel rejection.</summary>
        private static void AssertTracesEqual(SelectionExecutionTrace expected, SelectionExecutionTrace actual)
        {
            Assert.That(actual.Used, Is.EqualTo(expected.Used)); Assert.That(actual.Limit, Is.EqualTo(expected.Limit)); Assert.That(actual.LastAccepted.HasValue, Is.EqualTo(expected.LastAccepted.HasValue)); Assert.That(actual.FirstRejection.HasValue, Is.EqualTo(expected.FirstRejection.HasValue));
            AssertPrimaryDiagnosticContextsEqual(expected.LastAccepted, actual.LastAccepted); AssertPrimaryDiagnosticFailuresEqual(expected.FirstRejection, actual.FirstRejection); Assert.That(actual.Buckets.Count, Is.EqualTo(expected.Buckets.Count));
            for (int index = 0; index < expected.Buckets.Count; index++) AssertPrimaryDiagnosticBucketEqual(expected.Buckets[index], actual.Buckets[index]);
        }

        /// <summary>Compares nullable execution context identity and binary64 coordinates exactly.</summary>
        private static void AssertPrimaryDiagnosticContextsEqual(SelectionExecutionContext? expected, SelectionExecutionContext? actual)
        {
            Assert.That(actual.HasValue, Is.EqualTo(expected.HasValue)); if (!expected.HasValue) return; SelectionExecutionContext expectedValue = expected.Value; SelectionExecutionContext actualValue = actual.Value;
            Assert.That(actualValue.Candidate, Is.EqualTo(expectedValue.Candidate)); Assert.That(actualValue.Stage, Is.EqualTo(expectedValue.Stage)); Assert.That(actualValue.Branch, Is.EqualTo(expectedValue.Branch)); Assert.That(actualValue.GridName, Is.EqualTo(expectedValue.GridName)); Assert.That(actualValue.GridIndex, Is.EqualTo(expectedValue.GridIndex)); Assert.That(actualValue.Path, Is.EqualTo(expectedValue.Path)); AssertDoubleBitsEqual(expectedValue.Coordinate.P, actualValue.Coordinate.P); AssertDoubleBitsEqual(expectedValue.Coordinate.V, actualValue.Coordinate.V);
        }

        /// <summary>Compares nullable pre-kernel rejection context and reservation counters exactly.</summary>
        private static void AssertPrimaryDiagnosticFailuresEqual(SelectionExecutionFailure? expected, SelectionExecutionFailure? actual)
        {
            Assert.That(actual.HasValue, Is.EqualTo(expected.HasValue)); if (!expected.HasValue) return; AssertPrimaryDiagnosticContextsEqual(expected.Value.Context, actual.Value.Context); Assert.That(actual.Value.Used, Is.EqualTo(expected.Value.Used)); Assert.That(actual.Value.Limit, Is.EqualTo(expected.Value.Limit)); Assert.That(actual.Value.KernelStarted, Is.EqualTo(expected.Value.KernelStarted));
        }

        /// <summary>Compares one ordered reservation bucket's complete public aggregate identity.</summary>
        private static void AssertPrimaryDiagnosticBucketEqual(SelectionExecutionTraceBucket expected, SelectionExecutionTraceBucket actual)
        {
            Assert.That(actual.Candidate, Is.EqualTo(expected.Candidate)); Assert.That(actual.Stage, Is.EqualTo(expected.Stage)); Assert.That(actual.Branch, Is.EqualTo(expected.Branch)); Assert.That(actual.GridName, Is.EqualTo(expected.GridName)); Assert.That(actual.Path, Is.EqualTo(expected.Path)); Assert.That(actual.Reservations, Is.EqualTo(expected.Reservations));
        }

        /// <summary>Counts completed scalar-kernel work across the observed recursive partition distribution.</summary>
        private static int CountWork(PrimaryDiagnosticSnapshot snapshot)
        {
            int total = 0; foreach (PrimaryDiagnosticWork value in snapshot.Work) total += value.Samples; return total;
        }

        /// <summary>Counts every ordered initial eta partition recorded by the capture.</summary>
        private static int CountInitialPartitions(PrimaryDiagnosticSnapshot snapshot)
        {
            int total = 0; foreach (PrimaryDiagnosticPartition value in snapshot.Partitions) total += value.UseX.Length; return total;
        }

        /// <summary>Compares every captured partition to the authoritative primary boundary algorithm.</summary>
        private static void AssertCapturedPartitionsMatchAuthoritativeRules(PrimaryDiagnosticSnapshot snapshot)
        {
            foreach (PrimaryDiagnosticPartition partition in snapshot.Partitions)
            {
                double[] boundaries = AdaptivePrimary.GetEtaPartitionBoundariesForTest(0.089d, 0.0d, partition.Psi); AssertDoubleArraysEqual(boundaries, partition.Boundaries);
                Assert.That(partition.Labels, Is.EqualTo(AdaptivePrimary.GetEtaPartitionLabelsForTest(0.089d, 0.0d, partition.Psi))); Assert.That(partition.UseX, Is.EqualTo(AdaptivePrimary.GetVisibilityTailXPartitionsForTest(0.089d, 0.0d, partition.Psi, false)));
            }
        }

        /// <summary>Compares binary64 values without NUnit numeric-equivalence normalization.</summary>
        private static void AssertDoubleBitsEqual(double expected, double actual) => Assert.That(BitConverter.DoubleToInt64Bits(actual), Is.EqualTo(BitConverter.DoubleToInt64Bits(expected)));

        /// <summary>Compares every binary64 array element without collection-level numeric normalization.</summary>
        private static void AssertDoubleArraysEqual(double[] expected, double[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length)); for (int index = 0; index < expected.Length; index++) AssertDoubleBitsEqual(expected[index], actual[index]);
        }

        /// <summary>Requires every UTF-8 byte of the recovered direct diagnostic renderer output.</summary>
        private static void AssertRenderedDiagnosticHash(string rendered)
        {
            using (SHA256 algorithm = SHA256.Create()) Assert.That(BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(rendered))).Replace("-", string.Empty).ToLowerInvariant(), Is.EqualTo("d91f329c398e3a71010861a784ed1b7ffda1e548067518d466c9ea337044972a"));
        }

        /// <summary>Gets whether at least one observed boundary has a verified primary branch source label.</summary>
        private static bool HasVerifiedBoundaryLabel(PrimaryDiagnosticSnapshot snapshot)
        {
            foreach (PrimaryDiagnosticPartition partition in snapshot.Partitions) foreach (string label in partition.Labels) if (label != "endpoint" && label != "unavailable") return true;
            return false;
        }

        /// <summary>Rejects unsupported reflection-ridge terminology from every observed boundary label.</summary>
        private static bool HasReflectionRidgeLabel(PrimaryDiagnosticSnapshot snapshot)
        {
            foreach (PrimaryDiagnosticPartition partition in snapshot.Partitions) foreach (string label in partition.Labels) if (label.Contains("reflection-ridge")) return true;
            return false;
        }
    }
}
