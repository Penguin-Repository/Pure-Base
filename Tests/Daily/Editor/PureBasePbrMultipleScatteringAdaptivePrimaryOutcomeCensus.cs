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

// Builds immutable primary-only observations over the frozen training and validation inputs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PureBase.Tests.Daily
{
    /// <summary>Stores one exact-bit primary outcome coordinate and its frozen grid memberships.</summary>
    internal readonly struct PrimaryOutcomeCoordinate
    {
        /// <summary>Initializes one immutable coordinate with membership evidence from every frozen grid.</summary>
        internal PrimaryOutcomeCoordinate(AdaptiveCoordinate coordinate, bool training, bool validation, bool original, bool stress) { Coordinate = coordinate; IsTraining = training; IsValidation = validation; IsOriginal = original; IsStress = stress; }
        /// <summary>Gets the exact-bit coordinate used by both primary branches.</summary>
        internal AdaptiveCoordinate Coordinate { get; }
        /// <summary>Gets whether this coordinate is present in the training grid.</summary>
        internal bool IsTraining { get; }
        /// <summary>Gets whether this coordinate is present in the validation grid.</summary>
        internal bool IsValidation { get; }
        /// <summary>Gets whether this coordinate is present in the original grid.</summary>
        internal bool IsOriginal { get; }
        /// <summary>Gets whether this coordinate is present in the stress grid.</summary>
        internal bool IsStress { get; }
    }

    /// <summary>Stores one immutable direct-primary result with its complete capture-only evidence.</summary>
    internal sealed class PrimaryOutcomeRow
    {
        /// <summary>Initializes one branch-specific primary-only observation.</summary>
        internal PrimaryOutcomeRow(int coordinateIndex, PrimaryOutcomeCoordinate input, bool switchBranch, PrimaryDiagnosticRun run) { CoordinateIndex = coordinateIndex; Input = input; SwitchBranch = switchBranch; Run = run; ExceptionType = run.Exception?.GetType().FullName; ExceptionMessage = run.Exception?.Message; }
        /// <summary>Gets the source-order index in the exact-bit training-validation union.</summary>
        internal int CoordinateIndex { get; }
        /// <summary>Gets the immutable coordinate and source memberships.</summary>
        internal PrimaryOutcomeCoordinate Input { get; }
        /// <summary>Gets whether the visibility-tail switch branch was observed.</summary>
        internal bool SwitchBranch { get; }
        /// <summary>Gets the direct primary result, reservation trace, capture, and exception evidence.</summary>
        internal PrimaryDiagnosticRun Run { get; }
        /// <summary>Gets the classified terminal outcome without collapsing primary caps.</summary>
        internal PrimaryDiagnosticState State => Run.State;
        /// <summary>Gets the immutable per-row reservation limit.</summary>
        internal long ExecutionLimit => Run.Trace.Limit;
        /// <summary>Gets the retained exception type when direct integration faulted.</summary>
        internal string ExceptionType { get; }
        /// <summary>Gets the retained exception message when direct integration faulted.</summary>
        internal string ExceptionMessage { get; }
    }

    /// <summary>Stores an immutable complete primary-only observation of both branches for every union coordinate.</summary>
    internal sealed class PrimaryOutcomeCensus
    {
        private readonly PrimaryOutcomeCoordinate[] coordinates;
        private readonly PrimaryOutcomeRow[] rows;

        /// <summary>Initializes immutable source-order coordinates, source counts, candidate settings, and branch observations.</summary>
        internal PrimaryOutcomeCensus(PrimaryOutcomeCoordinate[] coordinates, PrimaryOutcomeRow[] rows, AdaptiveSettings frozenSettings, int trainingCount, int validationCount, int originalCount, int stressCount) { this.coordinates = (PrimaryOutcomeCoordinate[])coordinates.Clone(); this.rows = (PrimaryOutcomeRow[])rows.Clone(); FrozenSettings = frozenSettings; TrainingCount = trainingCount; ValidationCount = validationCount; OriginalCount = originalCount; StressCount = stressCount; }
        /// <summary>Gets copied exact-bit union coordinates in training-then-validation source order.</summary>
        internal IReadOnlyList<PrimaryOutcomeCoordinate> Coordinates => Array.AsReadOnly((PrimaryOutcomeCoordinate[])coordinates.Clone());
        /// <summary>Gets copied normal and switch direct-primary observations in coordinate source order.</summary>
        internal IReadOnlyList<PrimaryOutcomeRow> Rows => Array.AsReadOnly((PrimaryOutcomeRow[])rows.Clone());
        /// <summary>Gets the exact frozen settings copied from Candidates[0] for this census.</summary>
        internal AdaptiveSettings FrozenSettings { get; }
        /// <summary>Gets the frozen training source count before exact-bit unioning.</summary>
        internal int TrainingCount { get; }
        /// <summary>Gets the frozen validation source count before exact-bit unioning.</summary>
        internal int ValidationCount { get; }
        /// <summary>Gets the frozen original source count used for membership evidence.</summary>
        internal int OriginalCount { get; }
        /// <summary>Gets the frozen stress source count used for membership evidence.</summary>
        internal int StressCount { get; }
    }

    /// <summary>Renders deterministic nonpersistent evidence for primary-only observation.</summary>
    internal static class PrimaryOutcomeCensusRenderer
    {
        /// <summary>Renders all immutable primary-only outcomes without producing a selection or safety verdict.</summary>
        internal static string Render(PrimaryOutcomeCensus census)
        {
            var text = new StringBuilder("primary-outcome-census version=2 observation=primary-only nonpersistent=true selection-verdict=none shader-safety-certification=none");
            AppendSettings(text, census); AppendCounts(text, census); AppendCategoryCounts(text, census);
            foreach (PrimaryOutcomeRow row in census.Rows) AppendRow(text, row);
            return text.ToString();
        }

        /// <summary>Appends the frozen first candidate and fixed per-row reservation contract.</summary>
        private static void AppendSettings(StringBuilder text, PrimaryOutcomeCensus census)
        {
            AdaptiveSettings settings = census.FrozenSettings;
            text.Append("\nsettings={frozenCandidate=Candidates[0] name=").Append(settings.Name).Append(" absolute="); AppendDouble(text, settings.Absolute); text.Append(" relative="); AppendDouble(text, settings.Relative); text.Append(" witnessAbsolute="); AppendDouble(text, settings.WitnessAbsolute); text.Append(" witnessRelative="); AppendDouble(text, settings.WitnessRelative); text.Append(" maxDepth=").Append(settings.MaxDepth).Append(" maxPanels=").Append(settings.MaxPanels).Append(" maxEvaluations=").Append(settings.MaxEvaluations).Append(" fixedPerRowReservationLimit=512}");
        }

        /// <summary>Appends frozen source counts, exact-bit union size, and all retained memberships.</summary>
        private static void AppendCounts(StringBuilder text, PrimaryOutcomeCensus census)
        {
            text.Append("\ninputs={sourceCounts={training=").Append(census.TrainingCount).Append(" validation=").Append(census.ValidationCount).Append(" original=").Append(census.OriginalCount).Append(" stress=").Append(census.StressCount).Append("} unionCoordinates=").Append(census.Coordinates.Count).Append(" branchRows=").Append(census.Rows.Count).Append(" membershipCoordinates={training=").Append(CountMembership(census, input => input.IsTraining)).Append(" validation=").Append(CountMembership(census, input => input.IsValidation)).Append(" original=").Append(CountMembership(census, input => input.IsOriginal)).Append(" stress=").Append(CountMembership(census, input => input.IsStress)).Append("}}");
        }

        /// <summary>Appends exact taxonomy totals without removing any individual row evidence.</summary>
        private static void AppendCategoryCounts(StringBuilder text, PrimaryOutcomeCensus census)
        {
            text.Append("\ncategoryCounts={");
            foreach (PrimaryDiagnosticState state in Enum.GetValues(typeof(PrimaryDiagnosticState))) text.Append(state).Append("=").Append(CountState(census, state)).Append(" ");
            text.Append("}");
        }

        /// <summary>Appends one complete branch observation in fixed source order.</summary>
        private static void AppendRow(StringBuilder text, PrimaryOutcomeRow row)
        {
            AdaptiveCoordinate coordinate = row.Input.Coordinate; PrimaryDiagnosticRun run = row.Run; PrimaryDiagnosticSnapshot snapshot = run.Snapshot;
            text.Append("\nrow={index=").Append(row.CoordinateIndex).Append(" coordinate={p="); AppendDouble(text, coordinate.P); text.Append(" ndotV="); AppendDouble(text, coordinate.V); text.Append("} branch=").Append(row.SwitchBranch ? "switch" : "normal").Append(" memberships={").Append(Flags(row.Input)).Append("} state=").Append(row.State).Append(" result={raw="); AppendDouble(text, run.Result.Value); text.Append(" indicator="); AppendDouble(text, run.Result.Error); text.Append(" tolerance="); AppendDouble(text, run.Result.Tolerance); text.Append(" diagnostic=").Append(run.Result.Diagnostic ?? "accepted").Append(" evaluations=").Append(run.Result.Evaluations).Append(" panels=").Append(run.Result.Panels).Append(" depth=").Append(run.Result.Depth).Append("}");
            AppendTrace(text, run.Trace); AppendCapture(text, snapshot); text.Append(" exceptionType=").Append(row.ExceptionType ?? "none").Append(" exceptionMessage=").Append(row.ExceptionMessage ?? "none").Append("}");
        }

        /// <summary>Appends every retained reservation aggregate and terminal reservation context.</summary>
        private static void AppendTrace(StringBuilder text, SelectionExecutionTrace trace)
        {
            text.Append(" reservations={used=").Append(trace.Used).Append(" limit=").Append(trace.Limit).Append(" buckets=[");
            foreach (SelectionExecutionTraceBucket bucket in trace.Buckets) text.Append("{candidate=").Append(bucket.Candidate).Append(" stage=").Append(bucket.Stage).Append(" branch=").Append(bucket.Branch).Append(" grid=").Append(bucket.GridName).Append(" path=").Append(bucket.Path).Append(" accepted=").Append(bucket.Reservations).Append("}");
            text.Append("] lastAccepted="); AppendContext(text, trace.LastAccepted); text.Append(" firstRejection="); AppendRejection(text, trace.FirstRejection); text.Append("}");
        }

        /// <summary>Appends raw terminal, partition, work, and split evidence from one direct observation.</summary>
        private static void AppendCapture(StringBuilder text, PrimaryDiagnosticSnapshot snapshot)
        {
            if (snapshot == null) { text.Append(" capture=unavailable"); return; }
            PrimaryDiagnosticSample terminal = snapshot.TerminalSample;
            text.Append(" capture={kernelWork=").Append(snapshot.CompletedKernelWork).Append(" caps={depth=").Append(snapshot.MaxDepth).Append(" panels=").Append(snapshot.MaxPanels).Append(" evaluations=").Append(snapshot.MaxEvaluations).Append("} terminal={axis=").Append(terminal.Axis ?? "none").Append(" partition=").Append(terminal.Partition ?? "none").Append(" psi="); AppendDouble(text, terminal.Psi); text.Append(" left="); AppendDouble(text, terminal.Left); text.Append(" right="); AppendDouble(text, terminal.Right); text.Append(" depth=").Append(terminal.Depth).Append(" kernel=").Append(terminal.KernelStarted ? "started" : "pre-kernel").Append(" stop=").Append(terminal.Stop ?? "none").Append("} partitions="); AppendPartitions(text, snapshot.Partitions); text.Append(" work="); AppendWork(text, snapshot.Work); text.Append(" splits="); AppendSplits(text, snapshot.SplitEvents); text.Append("}");
        }

        /// <summary>Appends every analytic partition and its ordered boundary labels.</summary>
        private static void AppendPartitions(StringBuilder text, IReadOnlyList<PrimaryDiagnosticPartition> partitions)
        {
            text.Append("[");
            for (int line = 0; line < partitions.Count; line++) { PrimaryDiagnosticPartition partition = partitions[line]; text.Append("{line=").Append(line).Append(" psi="); AppendDouble(text, partition.Psi); text.Append(" boundaries=["); double[] boundaries = partition.Boundaries; string[] labels = partition.Labels; bool[] useX = partition.UseX; for (int index = 0; index < useX.Length; index++) { text.Append("{left="); AppendDouble(text, boundaries[index]); text.Append(" right="); AppendDouble(text, boundaries[index + 1]); text.Append(" leftLabel=").Append(labels[index]).Append(" rightLabel=").Append(labels[index + 1]).Append(" axis=").Append(useX[index] ? "eta-x" : "eta").Append("}"); } text.Append("]}"); }
            text.Append("]");
        }

        /// <summary>Appends completed work for every ordered initial partition, including zero-work partitions.</summary>
        private static void AppendWork(StringBuilder text, IReadOnlyList<PrimaryDiagnosticWork> work)
        {
            text.Append("[");
            foreach (PrimaryDiagnosticWork item in work) { text.Append("{line=").Append(item.PartitionLine).Append(" partition=").Append(item.InitialPartition).Append(" axis=").Append(item.Axis).Append(" psi="); AppendDouble(text, item.Psi); text.Append(" left="); AppendDouble(text, item.Left); text.Append(" right="); AppendDouble(text, item.Right); text.Append(" leftLabel=").Append(item.LeftLabel).Append(" rightLabel=").Append(item.RightLabel).Append(" samples=").Append(item.Samples).Append("}"); }
            text.Append("]");
        }

        /// <summary>Appends every already-decided refinement event in the captured deterministic DFS order.</summary>
        private static void AppendSplits(StringBuilder text, IReadOnlyList<PrimaryDiagnosticSplitEvent> events)
        {
            text.Append("[");
            foreach (PrimaryDiagnosticSplitEvent item in events) { text.Append("{axis=").Append(item.Axis).Append(" psi="); AppendDouble(text, item.Psi); text.Append(" left="); AppendDouble(text, item.Left); text.Append(" right="); AppendDouble(text, item.Right); text.Append(" depth=").Append(item.Depth).Append(" decision=").Append(item.Decision).Append(" coarse="); AppendDouble(text, item.Coarse.Value); text.Append(" fine="); AppendDouble(text, item.Fine.Value); text.Append(" inheritedError="); AppendDouble(text, item.InheritedError); text.Append(" ruleDelta="); AppendDouble(text, item.RuleDelta); text.Append(" absoluteLimit="); AppendDouble(text, item.AbsoluteLimit); text.Append(" relativeLimit="); AppendDouble(text, item.RelativeLimit); text.Append(" localError="); AppendDouble(text, item.LocalError); text.Append(" localLimit="); AppendDouble(text, item.LocalLimit); text.Append(" panels=").Append(item.Panels).Append(" evaluations=").Append(item.Evaluations).Append("}"); }
            text.Append("]");
        }

        /// <summary>Appends a nullable direct reservation context with lossless coordinate identity.</summary>
        private static void AppendContext(StringBuilder text, SelectionExecutionContext? context)
        {
            if (!context.HasValue) { text.Append("none"); return; }
            SelectionExecutionContext value = context.Value; text.Append("{candidate=").Append(value.Candidate).Append(" stage=").Append(value.Stage).Append(" branch=").Append(value.Branch).Append(" grid=").Append(value.GridName).Append(" index=").Append(value.GridIndex).Append(" path=").Append(value.Path).Append(" coordinate={p="); AppendDouble(text, value.Coordinate.P); text.Append(" ndotV="); AppendDouble(text, value.Coordinate.V); text.Append("}}");
        }

        /// <summary>Appends the first pre-kernel rejection with its exact reservation location when present.</summary>
        private static void AppendRejection(StringBuilder text, SelectionExecutionFailure? rejection)
        {
            if (!rejection.HasValue) { text.Append("none"); return; }
            SelectionExecutionFailure value = rejection.Value; text.Append("{used=").Append(value.Used).Append(" limit=").Append(value.Limit).Append(" kernelStarted=").Append(value.KernelStarted ? "true" : "false").Append(" context="); AppendContext(text, value.Context); text.Append("}");
        }

        /// <summary>Formats all frozen source memberships in a fixed label order.</summary>
        private static string Flags(PrimaryOutcomeCoordinate input) => "training=" + Bit(input.IsTraining) + ",validation=" + Bit(input.IsValidation) + ",original=" + Bit(input.IsOriginal) + ",stress=" + Bit(input.IsStress);
        /// <summary>Formats a Boolean membership as an invariant binary marker.</summary>
        private static string Bit(bool value) => value ? "1" : "0";
        /// <summary>Counts one frozen-grid membership without changing source-order evidence.</summary>
        private static int CountMembership(PrimaryOutcomeCensus census, Func<PrimaryOutcomeCoordinate, bool> predicate) { int count = 0; foreach (PrimaryOutcomeCoordinate input in census.Coordinates) if (predicate(input)) count++; return count; }
        /// <summary>Counts one terminal category without hiding the ordered row evidence.</summary>
        private static int CountState(PrimaryOutcomeCensus census, PrimaryDiagnosticState state) { int count = 0; foreach (PrimaryOutcomeRow row in census.Rows) if (row.State == state) count++; return count; }
        /// <summary>Formats a binary64 value by its exact IEEE-754 identity.</summary>
        private static string Bits(double value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
        /// <summary>Formats a raw binary64 result without changing its numerical value.</summary>
        private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        /// <summary>Formats one readable binary64 value together with its exact IEEE-754 identity.</summary>
        private static void AppendDouble(StringBuilder text, double value) => text.Append(D(value)).Append("/").Append(Bits(value));
    }

    /// <summary>Exposes a bounded, immutable primary-only outcome census without selection orchestration.</summary>
    internal static partial class AdaptiveProtocol
    {
        /// <summary>Runs one direct primary observation per exact-bit union coordinate and branch.</summary>
        internal static PrimaryOutcomeCensus RunPrimaryOutcomeCensusForTest()
        {
            PrimaryOutcomeCoordinate[] inputs = BuildPrimaryOutcomeCoordinates(); var rows = new PrimaryOutcomeRow[inputs.Length * 2]; int row = 0;
            for (int index = 0; index < inputs.Length; index++) { rows[row++] = RunPrimaryOutcome(index, inputs[index], false); rows[row++] = RunPrimaryOutcome(index, inputs[index], true); }
            return new PrimaryOutcomeCensus(inputs, rows, Candidates[0], Training.Length, Validation.Length, Original.Length, Stress.Length);
        }

        /// <summary>Builds the exact-bit training-validation union while retaining all frozen-grid memberships.</summary>
        private static PrimaryOutcomeCoordinate[] BuildPrimaryOutcomeCoordinates()
        {
            var values = new List<AdaptiveCoordinate>(); AppendDistinct(values, Training); AppendDistinct(values, Validation); var result = new PrimaryOutcomeCoordinate[values.Count];
            for (int index = 0; index < values.Count; index++) { AdaptiveCoordinate coordinate = values[index]; result[index] = new PrimaryOutcomeCoordinate(coordinate, ContainsExact(Training, coordinate), ContainsExact(Validation, coordinate), ContainsExact(Original, coordinate), ContainsExact(Stress, coordinate)); }
            return result;
        }

        /// <summary>Runs one isolated, direct primary branch with a fresh finite reservation budget.</summary>
        private static PrimaryOutcomeRow RunPrimaryOutcome(int index, PrimaryOutcomeCoordinate input, bool switchBranch)
        {
            AdaptiveSettings settings = Candidates[0]; AdaptiveCoordinate coordinate = input.Coordinate; var budget = new SelectionExecutionBudget(512); var context = new SelectionExecutionContext(settings.Name, "primary-outcome-census", switchBranch ? "switch" : "normal", "training-validation-union", index, coordinate, "primary-only"); var capture = new AdaptivePrimaryCapture();
            try { AdaptiveResult result = AdaptivePrimary.Integrate(settings, coordinate.P, coordinate.V, switchBranch, budget, context, capture); return new PrimaryOutcomeRow(index, input, switchBranch, new PrimaryDiagnosticRun(result, budget.Trace, capture.Snapshot, null)); }
            catch (Exception exception) { return new PrimaryOutcomeRow(index, input, switchBranch, new PrimaryDiagnosticRun(default, budget.Trace, capture.Snapshot, exception)); }
        }

        /// <summary>Appends coordinates not yet present under the exact pair of binary64 bit patterns.</summary>
        private static void AppendDistinct(List<AdaptiveCoordinate> values, AdaptiveCoordinate[] source)
        {
            foreach (AdaptiveCoordinate coordinate in source) if (!ContainsExact(values, coordinate)) values.Add(coordinate);
        }

        /// <summary>Tests frozen-grid membership using both binary64 coordinate bit patterns.</summary>
        private static bool ContainsExact(AdaptiveCoordinate[] source, AdaptiveCoordinate coordinate)
        {
            foreach (AdaptiveCoordinate value in source) if (SameBits(value, coordinate)) return true;
            return false;
        }

        /// <summary>Tests source-order union membership using both binary64 coordinate bit patterns.</summary>
        private static bool ContainsExact(List<AdaptiveCoordinate> source, AdaptiveCoordinate coordinate)
        {
            foreach (AdaptiveCoordinate value in source) if (SameBits(value, coordinate)) return true;
            return false;
        }

        /// <summary>Compares two coordinates without decimal formatting or approximate equality.</summary>
        private static bool SameBits(AdaptiveCoordinate left, AdaptiveCoordinate right) => BitConverter.DoubleToInt64Bits(left.P) == BitConverter.DoubleToInt64Bits(right.P) && BitConverter.DoubleToInt64Bits(left.V) == BitConverter.DoubleToInt64Bits(right.V);
    }
}
