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

// Defines deterministic selection-wide scalar-kernel work accounting and failure provenance.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Identifies one scalar-kernel reservation within a synchronous candidate selection.</summary>
    internal readonly struct SelectionExecutionContext
    {
        /// <summary>Initializes immutable candidate, grid, branch, and coordinate identity.</summary>
        internal SelectionExecutionContext(string candidate, string stage, string branch, string gridName, int gridIndex, AdaptiveCoordinate coordinate, string path)
        {
            Candidate = candidate;
            Stage = stage;
            Branch = branch;
            GridName = gridName;
            GridIndex = gridIndex;
            Coordinate = coordinate;
            Path = path;
        }

        /// <summary>Gets the candidate settings identifier.</summary>
        internal string Candidate { get; }
        /// <summary>Gets the named selection stage.</summary>
        internal string Stage { get; }
        /// <summary>Gets the active epsilon branch.</summary>
        internal string Branch { get; }
        /// <summary>Gets the declared grid name.</summary>
        internal string GridName { get; }
        /// <summary>Gets the zero-based coordinate index in the declared grid.</summary>
        internal int GridIndex { get; }
        /// <summary>Gets the exact scalar coordinate.</summary>
        internal AdaptiveCoordinate Coordinate { get; }
        /// <summary>Gets the independently implemented numerical path.</summary>
        internal string Path { get; }

        /// <summary>Returns the same selection location for a different numerical path.</summary>
        internal SelectionExecutionContext WithPath(string path) => new SelectionExecutionContext(Candidate, Stage, Branch, GridName, GridIndex, Coordinate, path);
    }

    /// <summary>Records the first selection-wide reservation rejected before scalar kernel work starts.</summary>
    internal readonly struct SelectionExecutionFailure
    {
        /// <summary>Initializes immutable failed-reservation evidence.</summary>
        internal SelectionExecutionFailure(SelectionExecutionContext context, long used, long limit)
        {
            Context = context;
            Used = used;
            Limit = limit;
        }

        /// <summary>Gets the complete immutable location of the rejected reservation.</summary>
        internal SelectionExecutionContext Context { get; }
        /// <summary>Gets completed scalar-kernel reservations at rejection.</summary>
        internal long Used { get; }
        /// <summary>Gets the immutable selection-wide reservation limit.</summary>
        internal long Limit { get; }
        /// <summary>Gets whether the rejected scalar kernel began execution.</summary>
        internal bool KernelStarted => false;

        /// <summary>Formats the deterministic finite selection-wide failure diagnostic.</summary>
        public override string ToString()
        {
            AdaptiveCoordinate coordinate = Context.Coordinate;
            return "selection-budget candidate=" + Context.Candidate + " stage=" + Context.Stage + " branch=" + Context.Branch
                + " grid=" + Context.GridName + " index=" + Context.GridIndex.ToString(CultureInfo.InvariantCulture)
                + " coordinateBits=p=" + Bits(coordinate.P) + ",ndotV=" + Bits(coordinate.V)
                + " coordinate=" + coordinate.Text + " path=" + Context.Path
                + " used=" + Used.ToString(CultureInfo.InvariantCulture) + " limit=" + Limit.ToString(CultureInfo.InvariantCulture)
                + " kernelStarted=false";
        }

        /// <summary>Formats one binary64 coordinate without culture-dependent formatting.</summary>
        private static string Bits(double value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
    }

    /// <summary>Aggregates accepted scalar-kernel reservations sharing one deterministic selection location.</summary>
    internal readonly struct SelectionExecutionTraceBucket
    {
        /// <summary>Initializes immutable aggregate identity and its accepted reservation count.</summary>
        internal SelectionExecutionTraceBucket(SelectionExecutionContext context, long reservations)
        {
            Candidate = context.Candidate;
            Stage = context.Stage;
            Branch = context.Branch;
            GridName = context.GridName;
            Path = context.Path;
            Reservations = reservations;
        }

        /// <summary>Gets the candidate settings identifier.</summary>
        internal string Candidate { get; }
        /// <summary>Gets the named selection stage.</summary>
        internal string Stage { get; }
        /// <summary>Gets the active epsilon branch.</summary>
        internal string Branch { get; }
        /// <summary>Gets the declared grid name.</summary>
        internal string GridName { get; }
        /// <summary>Gets the independently implemented numerical path.</summary>
        internal string Path { get; }
        /// <summary>Gets the accepted scalar-kernel reservation total.</summary>
        internal long Reservations { get; }

        /// <summary>Tests whether a reservation belongs to this aggregate identity.</summary>
        internal bool Matches(SelectionExecutionContext context) => Candidate == context.Candidate && Stage == context.Stage && Branch == context.Branch && GridName == context.GridName && Path == context.Path;
        /// <summary>Returns an immutable bucket with one additional accepted reservation.</summary>
        internal SelectionExecutionTraceBucket Increment() => new SelectionExecutionTraceBucket(new SelectionExecutionContext(Candidate, Stage, Branch, GridName, 0, default, Path), Reservations + 1);
    }

    /// <summary>Captures immutable ordered aggregate progress for one finite selection execution.</summary>
    internal readonly struct SelectionExecutionTrace
    {
        private readonly SelectionExecutionTraceBucket[] buckets;

        /// <summary>Initializes an immutable trace snapshot from accepted-reservation state.</summary>
        internal SelectionExecutionTrace(long used, long limit, SelectionExecutionTraceBucket[] buckets, SelectionExecutionContext? lastAccepted, SelectionExecutionFailure? firstRejection)
        {
            Used = used;
            Limit = limit;
            this.buckets = (SelectionExecutionTraceBucket[])buckets.Clone();
            LastAccepted = lastAccepted;
            FirstRejection = firstRejection;
        }

        /// <summary>Gets the completed scalar-kernel reservations.</summary>
        internal long Used { get; }
        /// <summary>Gets the immutable scalar-kernel reservation limit.</summary>
        internal long Limit { get; }
        /// <summary>Gets first-occurrence-ordered aggregate accepted reservations.</summary>
        internal IReadOnlyList<SelectionExecutionTraceBucket> Buckets => Array.AsReadOnly((SelectionExecutionTraceBucket[])buckets.Clone());
        /// <summary>Gets the final accepted reservation context when any work started.</summary>
        internal SelectionExecutionContext? LastAccepted { get; }
        /// <summary>Gets the first rejected reservation when the finite budget exhausted.</summary>
        internal SelectionExecutionFailure? FirstRejection { get; }
    }

    /// <summary>Identifies the structured outcome of a cache-isolated finite selection probe.</summary>
    internal enum SelectionProbeState
    {
        /// <summary>The selection completed and returned a selected candidate.</summary>
        Selected,
        /// <summary>The shared finite reservation budget rejected a scalar-kernel request.</summary>
        BudgetExhausted,
        /// <summary>Every candidate completed but failed a numerical selection gate.</summary>
        NumericallyRejected,
        /// <summary>An unexpected exception escaped the selection implementation.</summary>
        Faulted
    }

    /// <summary>Returns immutable finite-selection evidence without invoking cache or artifact persistence.</summary>
    internal sealed class SelectionProbeResult
    {
        /// <summary>Initializes the structured outcome, optional selection, trace, and captured exception.</summary>
        internal SelectionProbeResult(SelectionProbeState state, AdaptiveSelection selection, SelectionExecutionTrace trace, Exception exception)
        {
            State = state;
            Selection = selection;
            Trace = trace;
            Exception = exception;
        }

        /// <summary>Gets the structured selection outcome.</summary>
        internal SelectionProbeState State { get; }
        /// <summary>Gets the completed selected candidate only for a selected probe.</summary>
        internal AdaptiveSelection Selection { get; }
        /// <summary>Gets immutable accepted-reservation telemetry.</summary>
        internal SelectionExecutionTrace Trace { get; }
        /// <summary>Gets the captured exception for non-selected probe outcomes.</summary>
        internal Exception Exception { get; }
    }

    /// <summary>Identifies completion of every candidate without a numerically acceptable selection.</summary>
    internal sealed class SelectionNumericalRejectionException : InvalidOperationException
    {
        /// <summary>Initializes the deterministic numerical selection rejection diagnostic.</summary>
        internal SelectionNumericalRejectionException(string message) : base(message) { }
    }

    /// <summary>Counts scalar-kernel reservations and retains only the first deterministic rejection.</summary>
    internal sealed class SelectionExecutionBudget
    {
        private readonly long limit;
        private readonly List<SelectionExecutionTraceBucket> buckets = new List<SelectionExecutionTraceBucket>();
        private long used;
        private SelectionExecutionContext? lastAccepted;
        private SelectionExecutionFailure? firstFailure;

        /// <summary>Initializes a finite selection-wide scalar-kernel reservation limit.</summary>
        internal SelectionExecutionBudget(long limit)
        {
            if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
            this.limit = limit;
        }

        /// <summary>Gets the number of successful scalar-kernel reservations.</summary>
        internal long Used => used;
        /// <summary>Gets the immutable scalar-kernel reservation limit.</summary>
        internal long Limit => limit;
        /// <summary>Gets the first rejected reservation when the budget is exhausted.</summary>
        internal SelectionExecutionFailure? FirstFailure => firstFailure;
        /// <summary>Gets whether selection-wide scalar work has been rejected.</summary>
        internal bool IsExhausted => firstFailure.HasValue;

        /// <summary>Captures immutable telemetry without exposing mutable reservation state.</summary>
        internal SelectionExecutionTrace Trace => new SelectionExecutionTrace(used, limit, buckets.ToArray(), lastAccepted, firstFailure);

        /// <summary>Reserves exactly one scalar-kernel evaluation before its numerical work starts.</summary>
        internal bool TryReserve(SelectionExecutionContext context)
        {
            if (firstFailure.HasValue) return false;
            if (used >= limit)
            {
                firstFailure = new SelectionExecutionFailure(context, used, limit);
                return false;
            }

            used++;
            lastAccepted = context;
            RecordAccepted(context);
            return true;
        }

        /// <summary>Updates the first-occurrence-ordered aggregate after a reservation has been accepted.</summary>
        private void RecordAccepted(SelectionExecutionContext context)
        {
            for (int index = 0; index < buckets.Count; index++)
            {
                if (!buckets[index].Matches(context)) continue;
                buckets[index] = buckets[index].Increment();
                return;
            }

            buckets.Add(new SelectionExecutionTraceBucket(context, 1));
        }

        /// <summary>Gets the first failure as the finite exception required by selection orchestration.</summary>
        internal InvalidOperationException CreateException()
        {
            if (!firstFailure.HasValue) throw new InvalidOperationException("The selection execution budget has not failed.");
            return new InvalidOperationException(firstFailure.Value.ToString());
        }
    }

    /// <summary>Runs budget-aware candidate selection without changing the canonical selected-only path.</summary>
    internal static partial class AdaptiveProtocol
    {
        /// <summary>Selects a frozen candidate and persists only a completed selected result.</summary>
        internal static AdaptiveSelection Select() => SelectCore(null, true);

        /// <summary>Runs selection with an injected finite budget without accessing the oracle lazy cache.</summary>
        internal static AdaptiveSelection RunSelectionForTest(SelectionExecutionBudget budget)
        {
            if (budget == null) throw new ArgumentNullException(nameof(budget));
            return SelectCore(budget, false);
        }

        /// <summary>Runs finite selection without cache or persistence and returns structured stop evidence.</summary>
        internal static SelectionProbeResult RunSelectionProbeForTest(SelectionExecutionBudget budget)
        {
            if (budget == null) throw new ArgumentNullException(nameof(budget));
            try { return new SelectionProbeResult(SelectionProbeState.Selected, SelectCore(budget, false), budget.Trace, null); }
            catch (Exception exception)
            {
                if (budget.FirstFailure.HasValue) return new SelectionProbeResult(SelectionProbeState.BudgetExhausted, null, budget.Trace, exception);
                if (exception is SelectionNumericalRejectionException) return new SelectionProbeResult(SelectionProbeState.NumericallyRejected, null, budget.Trace, exception);
                return new SelectionProbeResult(SelectionProbeState.Faulted, null, budget.Trace, exception);
            }
        }

        /// <summary>Runs synchronous candidate selection and keeps persistence unreachable for injected runs.</summary>
        private static AdaptiveSelection SelectCore(SelectionExecutionBudget budget, bool persist)
        {
            var failures = new List<string>(); var ladder = new List<AdaptiveCandidateEvidence>();
            foreach (AdaptiveSettings candidate in Candidates)
            {
                AdaptiveSelection calibration = Measure(candidate, Combine(Original, Stress), false, "calibration", budget);
                ThrowIfBudgetFailed(budget); AdaptiveSelection original = Measure(candidate, Original, false, "original", budget);
                ThrowIfBudgetFailed(budget);
                if (!calibration.StressStable) { AddCalibrationFailure(candidate, calibration, original, ladder, failures); continue; }
                AdaptiveSelection canonical = Measure(candidate, Original, true, "canonical", budget); ThrowIfBudgetFailed(budget);
                string reason = canonical.IsSelected ? "selected" : FailureSummary("canonical", canonical);
                ladder.Add(new AdaptiveCandidateEvidence(candidate, calibration, original, canonical, reason));
                if (canonical.IsSelected) return Selected(candidate, canonical, ladder, persist);
                failures.Add(reason);
            }

            throw new SelectionNumericalRejectionException("numerical-limit: no frozen adaptive candidate passed. " + string.Join(" | ", failures));
        }

        /// <summary>Adds one noncanonical calibration failure to the deterministic candidate ladder.</summary>
        private static void AddCalibrationFailure(AdaptiveSettings candidate, AdaptiveSelection calibration, AdaptiveSelection original, List<AdaptiveCandidateEvidence> ladder, List<string> failures)
        {
            string reason = FailureSummary("stress", calibration); ladder.Add(new AdaptiveCandidateEvidence(candidate, calibration, original, null, reason)); failures.Add(reason);
        }

        /// <summary>Builds and optionally persists the completed selected candidate result.</summary>
        private static AdaptiveSelection Selected(AdaptiveSettings candidate, AdaptiveSelection canonical, List<AdaptiveCandidateEvidence> ladder, bool persist)
        {
            var selected = new AdaptiveSelection(canonical.Protocol, canonical.Normal, canonical.Switch, true, canonical.StressStable, ladder.ToArray());
            if (persist) Persist(BuildArtifact(selected));
            return selected;
        }

        /// <summary>Stops selection immediately when the shared scalar-work budget rejects a reservation.</summary>
        private static void ThrowIfBudgetFailed(SelectionExecutionBudget budget)
        {
            if (budget != null && budget.IsExhausted) throw budget.CreateException();
        }

        /// <summary>Measures both epsilon branches, witnesses, and downstream stability for one selection stage.</summary>
        private static AdaptiveSelection Measure(AdaptiveSettings settings, AdaptiveCoordinate[] audit, bool canonical, string stage, SelectionExecutionBudget budget)
        {
            AdaptiveBranch normal = MeasureBranch(settings, audit, false, canonical, stage, budget);
            ThrowIfBudgetFailed(budget); AdaptiveBranch switchBranch = MeasureBranch(settings, audit, true, canonical, stage, budget);
            return new AdaptiveSelection(settings, normal, switchBranch, normal.Passes && switchBranch.Passes, normal.StressStable && switchBranch.StressStable);
        }

        /// <summary>Measures one branch with selected, witness, and cross-check evidence in fixed order.</summary>
        private static AdaptiveBranch MeasureBranch(AdaptiveSettings settings, AdaptiveCoordinate[] audit, bool switchBranch, bool canonical, string stage, SelectionExecutionBudget budget)
        {
            var values = new AdaptiveEvidence[audit.Length]; string branch = switchBranch ? "switch" : "normal";
            string grid = stage == "calibration" ? "" : "original";
            for (int index = 0; index < audit.Length; index++) { values[index] = Evidence(settings, audit[index], switchBranch, Context(settings, stage, branch, audit[index], index, grid), budget); ThrowIfBudgetFailed(budget); }
            AdaptiveFit fit = canonical ? Fit(settings, switchBranch, branch, budget) : AdaptiveFit.Empty;
            return new AdaptiveBranch(values, fit, canonical && Passes(values, fit), PassesStress(values));
        }

        /// <summary>Collects independently calculated primary, witness, and cross-check evidence at one point.</summary>
        private static AdaptiveEvidence Evidence(AdaptiveSettings settings, AdaptiveCoordinate point, bool switchBranch, SelectionExecutionContext context, SelectionExecutionBudget budget)
        {
            AdaptiveResult primary = AdaptivePrimary.Integrate(settings, point.P, point.V, switchBranch, budget, context.WithPath("primary")); ThrowIfBudgetFailed(budget);
            AdaptiveResult witness = KronrodWitness.Integrate(settings.Witness(), point.P, point.V, switchBranch, budget, context.WithPath("witness")); ThrowIfBudgetFailed(budget);
            AdaptiveResult cross = AdaptiveCrossCheck.Integrate(settings, point.P, point.V, switchBranch, budget, context.WithPath("cross-check"));
            return new AdaptiveEvidence(point, primary, witness, cross);
        }

        /// <summary>Runs the immutable QR fitting path and compact validation with budget-aware primary calls.</summary>
        private static AdaptiveFit Fit(AdaptiveSettings settings, bool switchBranch, string branch, SelectionExecutionBudget budget)
        {
            var selected = new double[Training.Length]; var witness = new double[Training.Length];
            for (int index = 0; index < Training.Length; index++)
            {
                SelectionExecutionContext context = Context(settings, "fit-training", branch, Training[index], index, "training");
                selected[index] = Require(AdaptivePrimary.Integrate(settings, Training[index].P, Training[index].V, switchBranch, budget, context.WithPath("primary")), Training[index], budget);
                witness[index] = Require(AdaptivePrimary.Integrate(settings.Witness(), Training[index].P, Training[index].V, switchBranch, budget, context.WithPath("witness")), Training[index], budget);
            }

            return Validate(SolveFit(selected), SolveFit(witness), settings, switchBranch, branch, budget);
        }

        /// <summary>Rejects a local failed result while preserving an earlier shared-budget failure.</summary>
        private static double Require(AdaptiveResult result, AdaptiveCoordinate point, SelectionExecutionBudget budget)
        {
            ThrowIfBudgetFailed(budget);
            if (result.IsAccepted) return result.Value;
            throw new InvalidOperationException(result.Diagnostic + " coordinate=" + point.Text + ".");
        }

        /// <summary>Checks coefficients, compact validation errors, and roughness improvement without changing gates.</summary>
        private static AdaptiveFit Validate(float[] selected, float[] witness, AdaptiveSettings settings, bool switchBranch, string branch, SelectionExecutionBudget budget)
        {
            float coefficientDelta = 0.0f; float gainDelta = 0.0f; var selectedErrors = new List<float>(); var witnessErrors = new List<float>(); bool improves = true;
            for (int index = 0; index < CoefficientCount; index++) coefficientDelta = Math.Max(coefficientDelta, Math.Abs(selected[index] - witness[index]));
            for (int index = 0; index < Validation.Length; index++)
            {
                SelectionExecutionContext context = Context(settings, "fit-validation", branch, Validation[index], index, "validation");
                double energy = Require(AdaptivePrimary.Integrate(settings, Validation[index].P, Validation[index].V, switchBranch, budget, context.WithPath("primary")), Validation[index], budget);
                double expected = Require(AdaptivePrimary.Integrate(settings.Witness(), Validation[index].P, Validation[index].V, switchBranch, budget, context.WithPath("witness")), Validation[index], budget);
                float gain = Evaluate(selected, (float)Validation[index].P, (float)Validation[index].V); float witnessGain = Evaluate(witness, (float)Validation[index].P, (float)Validation[index].V);
                gainDelta = Math.Max(gainDelta, Math.Abs(gain - witnessGain)); selectedErrors.Add((float)Math.Abs(energy * (1.0d + gain) - 1.0d)); witnessErrors.Add((float)Math.Abs(expected * (1.0d + witnessGain) - 1.0d));
                if (Validation[index].P >= 0.5d && 1.0d - energy > 0.001d) improves &= selectedErrors[selectedErrors.Count - 1] < 1.0d - energy;
            }

            selectedErrors.Sort(); witnessErrors.Sort(); int p95 = (int)Math.Ceiling(selectedErrors.Count * 0.95d) - 1;
            return new AdaptiveFit(coefficientDelta, gainDelta, selectedErrors[p95], selectedErrors[selectedErrors.Count - 1], witnessErrors[p95], witnessErrors[witnessErrors.Count - 1], improves);
        }

        /// <summary>Creates immutable grid identity while preserving original-first calibration ordering.</summary>
        private static SelectionExecutionContext Context(AdaptiveSettings settings, string stage, string branch, AdaptiveCoordinate point, int index, string fixedGrid)
        {
            string grid = fixedGrid; int gridIndex = index;
            if (stage == "calibration") { gridIndex = IndexOf(Original, point); if (gridIndex < 0) { grid = "stress"; gridIndex = IndexOf(Stress, point); } else grid = "original"; }
            return new SelectionExecutionContext(settings.Name, stage, branch, grid, gridIndex, point, "");
        }

        /// <summary>Finds one exact binary64 coordinate in its declared fixed grid.</summary>
        private static int IndexOf(AdaptiveCoordinate[] grid, AdaptiveCoordinate point)
        {
            for (int index = 0; index < grid.Length; index++) if (BitConverter.DoubleToInt64Bits(grid[index].P) == BitConverter.DoubleToInt64Bits(point.P) && BitConverter.DoubleToInt64Bits(grid[index].V) == BitConverter.DoubleToInt64Bits(point.V)) return index;
            return -1;
        }

        /// <summary>Builds deterministic per-path failure evidence without creating a canonical artifact.</summary>
        private static string FailureSummary(string stage, AdaptiveSelection selection) => stage + " candidate=" + selection.Protocol.Name + " " + BranchFailure("normal", selection.Normal) + " " + BranchFailure("switch", selection.Switch);

        /// <summary>Returns the first failed coordinate or fit threshold for one branch.</summary>
        private static string BranchFailure(string branch, AdaptiveBranch evidence)
        {
            foreach (AdaptiveEvidence value in evidence.Values) if (!value.Passes) return branch + " " + EvidenceText(value);
            return evidence.Fit.Stable ? branch + " passed" : branch + " fit coefficient=" + F(evidence.Fit.CoefficientDelta) + " gain=" + F(evidence.Fit.GainDelta) + " p95=" + F(evidence.Fit.P95) + " max=" + F(evidence.Fit.Maximum);
        }

        /// <summary>Formats one path/branch coordinate with raw values, estimates, and cap evidence.</summary>
        private static string EvidenceText(AdaptiveEvidence value) => "coordinate=" + value.Point.Text + " primary partition=analytic-half " + FailureResult(value.Primary) + " witness partition=analytic-half " + FailureResult(value.Witness) + " cross partition=analytic-light " + FailureResult(value.Cross) + " crossDifference=" + D(value.CrossDifference);

        /// <summary>Formats a failed path result with its stop state in addition to deterministic counts.</summary>
        private static string FailureResult(AdaptiveResult value) => Result(value) + " state=" + (value.Diagnostic ?? "estimate-or-comparison");
        /// <summary>Formats one bounded result for a human-readable selection failure diagnostic.</summary>
        private static string Result(AdaptiveResult value) => "raw=" + D(value.Value) + " indicator=" + D(value.Error) + " tolerance=" + D(value.Tolerance) + " evaluations=" + value.Evaluations + " panels=" + value.Panels + " depth=" + value.Depth;
        /// <summary>Tests frozen error, witness, cross-path, and numerical-limit gates.</summary>
        private static bool Passes(AdaptiveEvidence[] values, AdaptiveFit fit) { foreach (AdaptiveEvidence value in values) if (!value.Passes) return false; return fit.Stable; }
        /// <summary>Tests noncanonical path and witness stability while calibrating candidates.</summary>
        private static bool PassesStress(AdaptiveEvidence[] values) { foreach (AdaptiveEvidence value in values) if (!value.Passes) return false; return true; }
    }

    /// <summary>Verifies deterministic selection-wide scalar-kernel accounting without running full selection.</summary>
    public sealed partial class PureBasePbrMultipleScatteringCompensationTests
    {
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
    }
}
