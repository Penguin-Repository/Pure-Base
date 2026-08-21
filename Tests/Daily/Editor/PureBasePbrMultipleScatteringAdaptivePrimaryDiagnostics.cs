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

// Captures immutable, test-only evidence from the fixed primary adaptive path.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PureBase.Tests.Daily
{
    /// <summary>Identifies the actual stop observed by a direct primary diagnostic run.</summary>
    internal enum PrimaryDiagnosticState
    {
        /// <summary>The direct run completed without a terminal diagnostic.</summary>
        Accepted,
        /// <summary>The selection-wide reservation limit rejected the next sample.</summary>
        BudgetExhausted,
        /// <summary>The primary evaluation cap rejected the next sample.</summary>
        EvaluationCap,
        /// <summary>The primary panel cap rejected the next refinement decision.</summary>
        PanelCap,
        /// <summary>The primary recursive depth cap rejected the next refinement decision.</summary>
        DepthCap,
        /// <summary>The completed direct result exceeded its final global tolerance.</summary>
        GlobalError,
        /// <summary>A completed primary scalar-kernel sample produced a nonfinite value.</summary>
        Nonfinite,
        /// <summary>An unclassified local numerical limit stopped the direct run.</summary>
        OtherLimit,
        /// <summary>An exception escaped the direct diagnostic run.</summary>
        Faulted
    }

    /// <summary>Stores one observed primary sample interval without retaining scalar-kernel values.</summary>
    internal readonly struct PrimaryDiagnosticSample
    {
        /// <summary>Initializes immutable terminal sample context without scalar-kernel values.</summary>
        internal PrimaryDiagnosticSample(string axis, double psi, double left, double right, int depth, bool kernelStarted, string stop) { Axis = axis; Psi = psi; Left = left; Right = right; Depth = depth; KernelStarted = kernelStarted; Stop = stop; Partition = axis == "eta-x" ? "eta-x visibility tail" : axis; }
        /// <summary>Gets the coordinate system of the terminal sample interval.</summary>
        internal string Axis { get; }
        /// <summary>Gets the outer azimuth coordinate for the terminal sample.</summary>
        internal double Psi { get; }
        /// <summary>Gets the inclusive-left terminal interval coordinate.</summary>
        internal double Left { get; }
        /// <summary>Gets the inclusive-right terminal interval coordinate.</summary>
        internal double Right { get; }
        /// <summary>Gets the recursive depth of the terminal sample.</summary>
        internal int Depth { get; }
        /// <summary>Gets whether scalar-kernel execution started for the terminal sample.</summary>
        internal bool KernelStarted { get; }
        /// <summary>Gets the terminal observation category when the kernel did not continue.</summary>
        internal string Stop { get; }
        /// <summary>Gets the human-readable initial-partition transformation.</summary>
        internal string Partition { get; }
    }

    /// <summary>Stores one already-decided embedded-pair refinement event in DFS order.</summary>
    internal readonly struct PrimaryDiagnosticSplitEvent
    {
        /// <summary>Initializes one immutable refinement decision already made by the primary path.</summary>
        internal PrimaryDiagnosticSplitEvent(string axis, double psi, double left, double right, int depth, AdaptiveEstimate coarse, AdaptiveEstimate fine, double delta, double absolute, double relative, double error, double limit, string decision, int panels, int evaluations) { Axis = axis; Psi = psi; Left = left; Right = right; Depth = depth; Coarse = coarse; Fine = fine; RuleDelta = delta; AbsoluteLimit = absolute; RelativeLimit = relative; LocalError = error; LocalLimit = limit; Decision = decision; Panels = panels; Evaluations = evaluations; }
        /// <summary>Gets the split coordinate system.</summary>
        internal string Axis { get; }
        /// <summary>Gets the outer azimuth coordinate.</summary>
        internal double Psi { get; }
        /// <summary>Gets the split interval's left coordinate.</summary>
        internal double Left { get; }
        /// <summary>Gets the split interval's right coordinate.</summary>
        internal double Right { get; }
        /// <summary>Gets the recursive decision depth.</summary>
        internal int Depth { get; }
        /// <summary>Gets the already-computed coarse embedded estimate.</summary>
        internal AdaptiveEstimate Coarse { get; }
        /// <summary>Gets the already-computed fine embedded estimate.</summary>
        internal AdaptiveEstimate Fine { get; }
        /// <summary>Gets the inherited inner integration error from the fine estimate.</summary>
        internal double InheritedError => Fine.Error;
        /// <summary>Gets the absolute difference between fine and coarse estimates.</summary>
        internal double RuleDelta { get; }
        /// <summary>Gets the allocated absolute error limit.</summary>
        internal double AbsoluteLimit { get; }
        /// <summary>Gets the allocated relative error limit.</summary>
        internal double RelativeLimit { get; }
        /// <summary>Gets the local embedded-pair error used for the decision.</summary>
        internal double LocalError { get; }
        /// <summary>Gets the local error limit used for the decision.</summary>
        internal double LocalLimit { get; }
        /// <summary>Gets the already-made adaptive decision category.</summary>
        internal string Decision { get; }
        /// <summary>Gets the panel count observed at this decision.</summary>
        internal int Panels { get; }
        /// <summary>Gets the evaluation count observed at this decision.</summary>
        internal int Evaluations { get; }
    }

    /// <summary>Stores one analytic eta line and its verified boundary provenance.</summary>
    internal sealed class PrimaryDiagnosticPartition
    {
        private readonly double[] boundaries; private readonly string[] labels; private readonly bool[] useX;
        /// <summary>Initializes immutable analytic boundary provenance for one eta line.</summary>
        internal PrimaryDiagnosticPartition(double psi, double[] boundaries, string[] labels, bool[] useX) { Psi = psi; this.boundaries = (double[])boundaries.Clone(); this.labels = (string[])labels.Clone(); this.useX = (bool[])useX.Clone(); }
        /// <summary>Gets the azimuth coordinate for this analytic eta line.</summary>
        internal double Psi { get; }
        /// <summary>Gets a copy of ordered eta boundaries.</summary>
        internal double[] Boundaries => (double[])boundaries.Clone();
        /// <summary>Gets a copy of authoritative ordered boundary provenance labels.</summary>
        internal string[] Labels => (string[])labels.Clone();
        /// <summary>Gets a copy of fixed visibility-tail transformation selections.</summary>
        internal bool[] UseX => (bool[])useX.Clone();
    }

    /// <summary>Stores completed kernel work attributed to one ordered initial eta partition.</summary>
    internal readonly struct PrimaryDiagnosticWork
    {
        /// <summary>Initializes one immutable initial-partition scalar-kernel work total.</summary>
        internal PrimaryDiagnosticWork(int partitionLine, int initialPartition, string axis, double psi, double left, double right, string leftLabel, string rightLabel, bool useX, int samples) { PartitionLine = partitionLine; InitialPartition = initialPartition; Axis = axis; Psi = psi; Left = left; Right = right; LeftLabel = leftLabel; RightLabel = rightLabel; UseX = useX; Samples = samples; }
        /// <summary>Gets the first-occurrence-ordered eta line containing this work.</summary>
        internal int PartitionLine { get; }
        /// <summary>Gets the ordered initial interval inside the containing eta line.</summary>
        internal int InitialPartition { get; }
        /// <summary>Gets the coordinate system used by this initial partition.</summary>
        internal string Axis { get; }
        /// <summary>Gets the outer azimuth coordinate of this initial partition.</summary>
        internal double Psi { get; }
        /// <summary>Gets the original eta interval's left boundary.</summary>
        internal double Left { get; }
        /// <summary>Gets the original eta interval's right boundary.</summary>
        internal double Right { get; }
        /// <summary>Gets the authoritative provenance label for the interval's left boundary.</summary>
        internal string LeftLabel { get; }
        /// <summary>Gets the authoritative provenance label for the interval's right boundary.</summary>
        internal string RightLabel { get; }
        /// <summary>Gets whether this initial interval uses the fixed visibility-tail x transformation.</summary>
        internal bool UseX { get; }
        /// <summary>Gets completed scalar-kernel samples attributed to this initial partition.</summary>
        internal int Samples { get; }
    }

    /// <summary>Stores an immutable completed direct diagnostic and explicit unavailable DFS scheduler fields.</summary>
    internal sealed class PrimaryDiagnosticSnapshot
    {
        private readonly PrimaryDiagnosticSplitEvent[] splitEvents; private readonly PrimaryDiagnosticPartition[] partitions; private readonly PrimaryDiagnosticWork[] work;
        /// <summary>Initializes a completed immutable direct diagnostic snapshot.</summary>
        internal PrimaryDiagnosticSnapshot(AdaptiveResult result, SelectionExecutionTrace trace, PrimaryDiagnosticState state, int kernelWork, PrimaryDiagnosticSample terminal, PrimaryDiagnosticSplitEvent[] splitEvents, PrimaryDiagnosticPartition[] partitions, PrimaryDiagnosticWork[] work, int maxDepth, int maxPanels, int maxEvaluations) { Result = result; Trace = trace; State = state; CompletedKernelWork = kernelWork; TerminalSample = terminal; this.splitEvents = (PrimaryDiagnosticSplitEvent[])splitEvents.Clone(); this.partitions = (PrimaryDiagnosticPartition[])partitions.Clone(); this.work = (PrimaryDiagnosticWork[])work.Clone(); MaxDepth = maxDepth; MaxPanels = maxPanels; MaxEvaluations = maxEvaluations; MaximumObservedLocalError = MaximumLocalError(splitEvents); MaximumObservedErrorOverLimit = MaximumErrorOverLimit(splitEvents); }
        /// <summary>Gets the immutable direct adaptive result.</summary>
        internal AdaptiveResult Result { get; }
        /// <summary>Gets the immutable selection reservation trace.</summary>
        internal SelectionExecutionTrace Trace { get; }
        /// <summary>Gets the classified terminal state.</summary>
        internal PrimaryDiagnosticState State { get; }
        /// <summary>Gets completed scalar-kernel work before the direct run stopped.</summary>
        internal int CompletedKernelWork { get; }
        /// <summary>Gets the final observed sample context.</summary>
        internal PrimaryDiagnosticSample TerminalSample { get; }
        /// <summary>Gets copied DFS-ordered embedded-pair decisions.</summary>
        internal IReadOnlyList<PrimaryDiagnosticSplitEvent> SplitEvents => Array.AsReadOnly((PrimaryDiagnosticSplitEvent[])splitEvents.Clone());
        /// <summary>Gets copied analytic eta-line partition provenance.</summary>
        internal IReadOnlyList<PrimaryDiagnosticPartition> Partitions => Array.AsReadOnly((PrimaryDiagnosticPartition[])partitions.Clone());
        /// <summary>Gets copied completed work for every initial partition, including zeros.</summary>
        internal IReadOnlyList<PrimaryDiagnosticWork> Work => Array.AsReadOnly((PrimaryDiagnosticWork[])work.Clone());
        /// <summary>Gets the current configured recursive depth cap.</summary>
        internal int MaxDepth { get; }
        /// <summary>Gets the configured primary panel cap.</summary>
        internal int MaxPanels { get; }
        /// <summary>Gets the configured primary evaluation cap.</summary>
        internal int MaxEvaluations { get; }
        /// <summary>Gets the maximum already-observed local embedded-pair error.</summary>
        internal double MaximumObservedLocalError { get; }
        /// <summary>Gets the maximum already-observed local error-to-limit ratio.</summary>
        internal double MaximumObservedErrorOverLimit { get; }
        /// <summary>Gets whether the terminal result retains a completed local error.</summary>
        internal bool HasFinalLocalError => Result.Diagnostic == null;
        /// <summary>Gets the completed local error or NaN when a terminal diagnostic invalidated it.</summary>
        internal double FinalLocalError => HasFinalLocalError ? Result.Error : double.NaN;
        /// <summary>Gets whether a global DFS scheduler queue metric is available.</summary>
        internal bool GlobalQueueAvailable => false;
        /// <summary>Gets whether a global DFS pending-interval metric is available.</summary>
        internal bool PendingIntervalCountAvailable => false;
        /// <summary>Gets whether a global DFS largest-error metric is available.</summary>
        internal bool SchedulerGlobalLargestErrorAvailable => false;

        /// <summary>Finds the largest local error among already-decided split events.</summary>
        private static double MaximumLocalError(PrimaryDiagnosticSplitEvent[] values) { double maximum = 0.0d; foreach (PrimaryDiagnosticSplitEvent value in values) maximum = Math.Max(maximum, value.LocalError); return maximum; }
        /// <summary>Finds the largest finite local error-to-limit ratio among split events.</summary>
        private static double MaximumErrorOverLimit(PrimaryDiagnosticSplitEvent[] values) { double maximum = 0.0d; foreach (PrimaryDiagnosticSplitEvent value in values) if (value.LocalLimit != 0.0d) maximum = Math.Max(maximum, value.LocalError / value.LocalLimit); return maximum; }
    }

    /// <summary>Returns one direct run and its optional observer snapshot without selection orchestration.</summary>
    internal sealed class PrimaryDiagnosticRun
    {
        /// <summary>Initializes one direct diagnostic outcome and optional immutable observation.</summary>
        internal PrimaryDiagnosticRun(AdaptiveResult result, SelectionExecutionTrace trace, PrimaryDiagnosticSnapshot snapshot, Exception exception) { Result = result; Trace = trace; Snapshot = snapshot; Exception = exception; State = exception == null ? snapshot?.State ?? PrimaryDiagnosticState.Accepted : PrimaryDiagnosticState.Faulted; }
        /// <summary>Gets the direct adaptive result.</summary>
        internal AdaptiveResult Result { get; }
        /// <summary>Gets the direct reservation trace.</summary>
        internal SelectionExecutionTrace Trace { get; }
        /// <summary>Gets the optional immutable capture snapshot.</summary>
        internal PrimaryDiagnosticSnapshot Snapshot { get; }
        /// <summary>Gets an exception that escaped the direct run, when present.</summary>
        internal Exception Exception { get; }
        /// <summary>Gets the classified direct outcome.</summary>
        internal PrimaryDiagnosticState State { get; }
    }

    /// <summary>Records already-computed primary state and produces no numerical control-flow value.</summary>
    internal sealed class AdaptivePrimaryCapture
    {
        private readonly List<PrimaryDiagnosticSplitEvent> splitEvents = new List<PrimaryDiagnosticSplitEvent>(); private readonly List<PrimaryDiagnosticPartition> partitions = new List<PrimaryDiagnosticPartition>(); private readonly List<PrimaryDiagnosticWork> work = new List<PrimaryDiagnosticWork>(); private int currentPartitionLine = -1; private PrimaryDiagnosticSample terminal;
        /// <summary>Records a started scalar-kernel sample and attributes its completed work.</summary>
        internal void RecordStartedSample(string axis, double psi, double left, double right, int depth) { terminal = new PrimaryDiagnosticSample(axis, psi, left, right, depth, true, null); AddWork(axis, psi, left, right, depth); }
        /// <summary>Records a pre-kernel terminal sample without changing scheduler behavior.</summary>
        internal void RecordTerminalSample(string axis, double psi, double left, double right, int depth, string stop) { terminal = new PrimaryDiagnosticSample(axis, psi, left, right, depth, false, stop); }
        /// <summary>Records a nonfinite completed kernel sample as a structured terminal observation.</summary>
        internal void RecordNonfiniteSample(string axis, double psi, double left, double right, int depth) { terminal = new PrimaryDiagnosticSample(axis, psi, left, right, depth, true, "nonfinite-primary-sample"); }
        /// <summary>Records every ordered initial eta partition with zero work before samples begin.</summary>
        internal void RecordPartitions(double psi, double[] boundaries, string[] labels, bool[] useX) { currentPartitionLine = partitions.Count; partitions.Add(new PrimaryDiagnosticPartition(psi, boundaries, labels, useX)); for (int index = 0; index < useX.Length; index++) work.Add(new PrimaryDiagnosticWork(currentPartitionLine, index, useX[index] ? "eta-x" : "eta", psi, boundaries[index], boundaries[index + 1], labels[index], labels[index + 1], useX[index], 0)); }
        /// <summary>Records an already-decided embedded-pair outcome in fixed DFS order.</summary>
        internal void RecordSplit(string axis, double psi, double left, double right, int depth, AdaptiveEstimate coarse, AdaptiveEstimate fine, double delta, double absolute, double relative, double error, double limit, string decision, int panels, int evaluations) { splitEvents.Add(new PrimaryDiagnosticSplitEvent(axis, psi, left, right, depth, coarse, fine, delta, absolute, relative, error, limit, decision, panels, evaluations)); }
        /// <summary>Freezes the captured immutable values after the direct primary run completes.</summary>
        internal void Complete(AdaptiveResult result, int kernelWork, SelectionExecutionTrace? trace, int maxDepth, int maxPanels, int maxEvaluations) { Snapshot = new PrimaryDiagnosticSnapshot(result, trace ?? default, StopState(result, trace), kernelWork, terminal, splitEvents.ToArray(), partitions.ToArray(), work.ToArray(), maxDepth, maxPanels, maxEvaluations); }
        /// <summary>Gets the completed immutable direct diagnostic snapshot.</summary>
        internal PrimaryDiagnosticSnapshot Snapshot { get; private set; }

        /// <summary>Attributes one completed sample to its recorded initial partition.</summary>
        private void AddWork(string axis, double psi, double left, double right, int depth)
        {
            int initial = FindInitialPartition(axis, psi, left, right); if (initial < 0) return; PrimaryDiagnosticWork value = work[initial]; work[initial] = new PrimaryDiagnosticWork(value.PartitionLine, value.InitialPartition, value.Axis, value.Psi, value.Left, value.Right, value.LeftLabel, value.RightLabel, value.UseX, value.Samples + 1);
        }

        /// <summary>Finds the active initial partition containing one recursively sampled interval.</summary>
        private int FindInitialPartition(string axis, double psi, double left, double right)
        {
            if (currentPartitionLine < 0) return -1; PrimaryDiagnosticPartition partition = partitions[currentPartitionLine]; double[] boundaries = partition.Boundaries; bool[] useX = partition.UseX;
            for (int index = 0; index < useX.Length; index++)
            {
                if ((useX[index] ? "eta-x" : "eta") != axis) continue;
                double partitionLeft = useX[index] ? Math.Sqrt(1.0d - boundaries[index + 1]) : boundaries[index]; double partitionRight = useX[index] ? Math.Sqrt(1.0d - boundaries[index]) : boundaries[index + 1];
                if (partition.Psi == psi && left >= partitionLeft && right <= partitionRight) return WorkIndex(currentPartitionLine, index);
            }
            return -1;
        }

        /// <summary>Finds the immutable work entry assigned to one initial partition.</summary>
        private int WorkIndex(int partitionLine, int initialPartition)
        {
            for (int index = 0; index < work.Count; index++) if (work[index].PartitionLine == partitionLine && work[index].InitialPartition == initialPartition) return index;
            return -1;
        }

        /// <summary>Classifies a completed direct run from its existing result and reservation trace.</summary>
        private static PrimaryDiagnosticState StopState(AdaptiveResult result, SelectionExecutionTrace? trace)
        {
            if (trace.HasValue && trace.Value.FirstRejection.HasValue) return PrimaryDiagnosticState.BudgetExhausted;
            if (result.Diagnostic == null) return PrimaryDiagnosticState.Accepted;
            if (result.Diagnostic.Contains(" primary evaluations ")) return PrimaryDiagnosticState.EvaluationCap;
            if (result.Diagnostic.Contains(" primary panels ")) return PrimaryDiagnosticState.PanelCap;
            if (result.Diagnostic.Contains(" primary depth ")) return PrimaryDiagnosticState.DepthCap;
            if (result.Diagnostic == "nonfinite primary sample") return PrimaryDiagnosticState.Nonfinite;
            return result.Diagnostic == "numerical-limit primary global-error" ? PrimaryDiagnosticState.GlobalError : PrimaryDiagnosticState.OtherLimit;
        }
    }

    /// <summary>Renders compact, deterministic direct-primary evidence without reading mutable execution state.</summary>
    internal static class PrimaryDiagnosticTraceRenderer
    {
        /// <summary>Renders a bounded deterministic summary of immutable direct-primary evidence.</summary>
        internal static string Render(PrimaryDiagnosticRun run)
        {
            PrimaryDiagnosticSnapshot value = run.Snapshot;
            if (value == null) return "primary-diagnostic version=1 state=Faulted exception=" + run.Exception?.GetType().FullName;
            var text = new StringBuilder();
            AppendSemantics(text); AppendInput(text, value.Trace); AppendResult(text, value); AppendTerminal(text, value.TerminalSample);
            AppendPartitions(text, value.Partitions, value.Work); AppendTrends(text, value.SplitEvents, value.Work, value.CompletedKernelWork);
            text.Append(" dfs={queue=unavailable pending=unavailable largestError=unavailable}");
            return text.ToString();
        }

        /// <summary>Appends fixed meanings for every compact trend field.</summary>
        private static void AppendSemantics(StringBuilder text)
        {
            text.Append("primary-diagnostic version=1 semantics={events=ordered-dfs-decisions over=error/limit(limit!=0) refinement=adjacent-split-pairs monotonic=adjacent-error<=previous partitionConcentration=max-initial-work/kernelWork refinementConcentration=max(axis,psi,depth)-split-count/refined maximum=highest-over-earliest-tie dfs=unavailable}");
        }

        /// <summary>Appends the direct immutable context used to create the reservation trace.</summary>
        private static void AppendInput(StringBuilder text, SelectionExecutionTrace trace)
        {
            SelectionExecutionContext? context = trace.FirstRejection.HasValue ? trace.FirstRejection.Value.Context : trace.LastAccepted;
            text.Append(" input={used=").Append(trace.Used).Append(" limit=").Append(trace.Limit);
            if (!context.HasValue) { text.Append(" context=unavailable}"); return; }
            SelectionExecutionContext value = context.Value; AdaptiveCoordinate coordinate = value.Coordinate;
            text.Append(" candidate=").Append(value.Candidate).Append(" stage=").Append(value.Stage).Append(" branch=").Append(value.Branch).Append(" grid=").Append(value.GridName).Append(" index=").Append(value.GridIndex).Append(" path=").Append(value.Path).Append(" pBits=").Append(Bits(coordinate.P)).Append(" vBits=").Append(Bits(coordinate.V)).Append("}");
        }

        /// <summary>Appends result, tolerance, immutable caps, and terminal global-local evidence.</summary>
        private static void AppendResult(StringBuilder text, PrimaryDiagnosticSnapshot value)
        {
            AdaptiveResult result = value.Result;
            text.Append(" result={state=").Append(value.State).Append(" raw=").Append(D(result.Value)).Append(" indicator=").Append(D(result.Error)).Append(" tolerance=").Append(D(result.Tolerance)).Append(" diagnostic=").Append(result.Diagnostic ?? "none").Append(" evaluations=").Append(result.Evaluations).Append(" panels=").Append(result.Panels).Append(" depth=").Append(result.Depth).Append(" kernelWork=").Append(value.CompletedKernelWork).Append(" caps=").Append(value.MaxDepth).Append(",").Append(value.MaxPanels).Append(",").Append(value.MaxEvaluations).Append(" finalLocalError=").Append(value.HasFinalLocalError ? D(value.FinalLocalError) : "unavailable").Append("}");
        }

        /// <summary>Appends the last observed local sample context without scalar-kernel data.</summary>
        private static void AppendTerminal(StringBuilder text, PrimaryDiagnosticSample sample)
        {
            text.Append(" terminal={axis=").Append(sample.Axis).Append(" partition=").Append(sample.Partition).Append(" psi=").Append(D(sample.Psi)).Append(" interval=").Append(D(sample.Left)).Append(",").Append(D(sample.Right)).Append(" depth=").Append(sample.Depth).Append(" kernelStarted=").Append(sample.KernelStarted.ToString().ToLowerInvariant()).Append(" stop=").Append(sample.Stop ?? "none").Append("}");
        }

        /// <summary>Appends every initial partition, boundary provenance, transform, and completed work.</summary>
        private static void AppendPartitions(StringBuilder text, IReadOnlyList<PrimaryDiagnosticPartition> partitions, IReadOnlyList<PrimaryDiagnosticWork> work)
        {
            text.Append(" partitions=["); for (int line = 0; line < partitions.Count; line++) { if (line > 0) text.Append(";"); PrimaryDiagnosticPartition partition = partitions[line]; text.Append("{line=").Append(line).Append(" psi=").Append(D(partition.Psi)).Append(" initial=["); for (int index = 0; index < partition.UseX.Length; index++) { if (index > 0) text.Append(","); PrimaryDiagnosticWork item = FindWork(work, line, index); text.Append(index).Append(":").Append(item.Axis).Append("@").Append(D(item.Left)).Append("..").Append(D(item.Right)).Append("|").Append(item.LeftLabel).Append(">").Append(item.RightLabel).Append("|x=").Append(item.UseX ? "1" : "0").Append("|w=").Append(item.Samples); } text.Append("]}"); } text.Append("]");
        }

        /// <summary>Finds the immutable work total for one ordered initial partition.</summary>
        private static PrimaryDiagnosticWork FindWork(IReadOnlyList<PrimaryDiagnosticWork> values, int line, int partition)
        {
            for (int index = 0; index < values.Count; index++) if (values[index].PartitionLine == line && values[index].InitialPartition == partition) return values[index];
            throw new InvalidOperationException("primary diagnostic work is incomplete");
        }

        /// <summary>Appends aggregate decision trends without serializing every observed event.</summary>
        private static void AppendTrends(StringBuilder text, IReadOnlyList<PrimaryDiagnosticSplitEvent> events, IReadOnlyList<PrimaryDiagnosticWork> work, int kernelWork)
        {
            int accepted = 0; int refined = 0; int panelCap = 0; int depthCap = 0; int consecutive = 0; int monotonic = 0; int first = -1; int last = -1; int minimum = -1; int maximum = -1;
            for (int index = 0; index < events.Count; index++) { PrimaryDiagnosticSplitEvent current = events[index]; CountDecision(current.Decision, ref accepted, ref refined, ref panelCap, ref depthCap); if (index > 0) { PrimaryDiagnosticSplitEvent previous = events[index - 1]; if (previous.Decision == "split" && current.Decision == "split") consecutive++; if (current.LocalError <= previous.LocalError) monotonic++; } if (current.LocalLimit == 0.0d) continue; if (first < 0) first = index; last = index; if (minimum < 0 || Ratio(current) < Ratio(events[minimum])) minimum = index; if (maximum < 0 || Ratio(current) > Ratio(events[maximum])) maximum = index; }
            text.Append(" trends={ordered=").Append(events.Count).Append(" decisions=").Append(accepted).Append(",").Append(refined).Append(",").Append(panelCap).Append(",").Append(depthCap).Append(" over="); AppendRatios(text, events, first, last, minimum, maximum); text.Append(" transitions=").Append(consecutive).Append(",").Append(monotonic).Append(" concentration="); AppendConcentration(text, events, work, refined, kernelWork); text.Append(" maximum="); AppendMaximum(text, events, maximum); text.Append("}");
        }

        /// <summary>Counts the supported decision categories in deterministic event order.</summary>
        private static void CountDecision(string decision, ref int accepted, ref int refined, ref int panelCap, ref int depthCap)
        {
            if (decision == "accepted") accepted++; else if (decision == "split") refined++; else if (decision == "panel-cap") panelCap++; else if (decision == "depth-cap") depthCap++;
        }

        /// <summary>Appends first, last, minimum, and maximum defined local error-to-limit ratios.</summary>
        private static void AppendRatios(StringBuilder text, IReadOnlyList<PrimaryDiagnosticSplitEvent> events, int first, int last, int minimum, int maximum)
        {
            if (first < 0) { text.Append("unavailable"); return; }
            text.Append(D(Ratio(events[first]))).Append(",").Append(D(Ratio(events[last]))).Append(",").Append(D(Ratio(events[minimum]))).Append(",").Append(D(Ratio(events[maximum])));
        }

        /// <summary>Appends maximum initial work and most concentrated refinement identity.</summary>
        private static void AppendConcentration(StringBuilder text, IReadOnlyList<PrimaryDiagnosticSplitEvent> events, IReadOnlyList<PrimaryDiagnosticWork> work, int refined, int kernelWork)
        {
            int partition = MaxWork(work); int refinement = MaxRefinement(events); text.Append("partition="); if (partition < 0) text.Append("unavailable"); else { PrimaryDiagnosticWork value = work[partition]; text.Append(value.PartitionLine).Append(":").Append(value.InitialPartition).Append(":").Append(value.Samples).Append("/").Append(kernelWork); } text.Append(" refinement="); if (refinement < 0) text.Append("unavailable"); else { PrimaryDiagnosticSplitEvent value = events[refinement]; int count = RefinementCount(events, value); text.Append(value.Axis).Append("@").Append(D(value.Psi)).Append("#").Append(value.Depth).Append(":").Append(count).Append("/").Append(refined); }
        }

        /// <summary>Finds the earliest initial partition with the largest completed work.</summary>
        private static int MaxWork(IReadOnlyList<PrimaryDiagnosticWork> values)
        {
            int maximum = -1; for (int index = 0; index < values.Count; index++) if (maximum < 0 || values[index].Samples > values[maximum].Samples) maximum = index; return maximum;
        }

        /// <summary>Finds the earliest split event with the largest matching refinement group.</summary>
        private static int MaxRefinement(IReadOnlyList<PrimaryDiagnosticSplitEvent> values)
        {
            int maximum = -1; int count = 0; for (int index = 0; index < values.Count; index++) { if (values[index].Decision != "split") continue; int current = RefinementCount(values, values[index]); if (current > count) { maximum = index; count = current; } } return maximum;
        }

        /// <summary>Counts split events sharing one axis, outer coordinate, and recursive depth.</summary>
        private static int RefinementCount(IReadOnlyList<PrimaryDiagnosticSplitEvent> values, PrimaryDiagnosticSplitEvent key)
        {
            int count = 0; for (int index = 0; index < values.Count; index++) { PrimaryDiagnosticSplitEvent value = values[index]; if (value.Decision == "split" && value.Axis == key.Axis && value.Psi == key.Psi && value.Depth == key.Depth) count++; } return count;
        }

        /// <summary>Appends the earliest event attaining the largest defined local error-to-limit ratio.</summary>
        private static void AppendMaximum(StringBuilder text, IReadOnlyList<PrimaryDiagnosticSplitEvent> events, int index)
        {
            if (index < 0) { text.Append("unavailable"); return; } PrimaryDiagnosticSplitEvent value = events[index]; text.Append("index=").Append(index).Append(" axis=").Append(value.Axis).Append(" psi=").Append(D(value.Psi)).Append(" interval=").Append(D(value.Left)).Append(",").Append(D(value.Right)).Append(" depth=").Append(value.Depth).Append(" decision=").Append(value.Decision).Append(" delta=").Append(D(value.RuleDelta)).Append(" error=").Append(D(value.LocalError)).Append(" limit=").Append(D(value.LocalLimit)).Append(" over=").Append(D(Ratio(value))).Append(" panels=").Append(value.Panels).Append(" evaluations=").Append(value.Evaluations);
        }

        /// <summary>Gets one defined local error-to-limit ratio from an immutable split event.</summary>
        private static double Ratio(PrimaryDiagnosticSplitEvent value) => value.LocalError / value.LocalLimit;

        /// <summary>Formats a binary64 value as a stable hexadecimal identity.</summary>
        private static string Bits(double value) => "0x" + BitConverter.DoubleToInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture);
        /// <summary>Formats a binary64 value with an invariant round-trippable diagnostic representation.</summary>
        private static string D(double value) => double.IsNaN(value) ? "none" : value.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Exposes the frozen calibration-a direct primary probe without accessing selection cache or persistence.</summary>
    internal static partial class AdaptiveProtocol
    {
        /// <summary>Runs the captured calibration-a direct primary diagnostic.</summary>
        internal static PrimaryDiagnosticRun RunCalibrationAPrimaryDiagnosticForTest() => RunCalibrationAPrimaryForTest(true);
        /// <summary>Runs the uncaptured calibration-a direct primary diagnostic.</summary>
        internal static PrimaryDiagnosticRun RunCalibrationAPrimaryWithoutCaptureForTest() => RunCalibrationAPrimaryForTest(false);

        /// <summary>Runs a captured direct primary probe that deterministically reaches the nonfinite stop category.</summary>
        internal static PrimaryDiagnosticRun RunNonfinitePrimaryDiagnosticForTest()
        {
            var observer = new AdaptivePrimaryCapture(); var settings = new AdaptiveSettings("primary-nonfinite-diagnostic", 0.0d, 0.0d, 0.0d, 0.0d, 4, 8, 32);
            try { AdaptiveResult result = AdaptivePrimary.Integrate(settings, double.PositiveInfinity, 0.5d, false, null, default, observer); return new PrimaryDiagnosticRun(result, default, observer.Snapshot, null); }
            catch (Exception exception) { return new PrimaryDiagnosticRun(default, default, observer.Snapshot, exception); }
        }

        /// <summary>Runs the fixed direct calibration-a input with optional observational capture.</summary>
        private static PrimaryDiagnosticRun RunCalibrationAPrimaryForTest(bool capture)
        {
            var budget = new SelectionExecutionBudget(512); AdaptiveSettings settings = Candidates[0]; var context = new SelectionExecutionContext(settings.Name, "direct-primary-diagnostic", "normal", "direct", 0, new AdaptiveCoordinate(0.089d, 0.0d), "primary"); var observer = capture ? new AdaptivePrimaryCapture() : null;
            try { AdaptiveResult result = AdaptivePrimary.Integrate(settings, context.Coordinate.P, context.Coordinate.V, false, budget, context, observer); return new PrimaryDiagnosticRun(result, budget.Trace, observer?.Snapshot, null); }
            catch (Exception exception) { return new PrimaryDiagnosticRun(default, budget.Trace, observer?.Snapshot, exception); }
        }
    }
}
