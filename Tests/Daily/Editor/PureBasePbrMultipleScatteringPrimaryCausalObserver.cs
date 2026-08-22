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

// Owns bounded causal observations that are never consumed by primary arithmetic.

using System;
using System.Collections.Generic;

namespace PureBase.Tests.Daily
{
    /// <summary>Records bounded primary provenance without returning numerical control-flow values.</summary>
    internal sealed class PrimaryCausalObserver
    {
        private const int Limit = 513;
        private const int TerminalRuleEdgeAllowance = 8;
        private readonly PrimaryCausalInvocation invocation;
        private readonly List<PrimaryCausalAttemptCore> attempts = new List<PrimaryCausalAttemptCore>();
        private readonly List<ReservationObservation> reservations = new List<ReservationObservation>();
        private readonly List<Node> records = new List<Node>();
        private readonly List<PrimaryCausalCrossAxisEdge> edges = new List<PrimaryCausalCrossAxisEdge>();
        private readonly List<PrimaryCausalCrossAxisEdge> activePsiRuleEdges = new List<PrimaryCausalCrossAxisEdge>();
        private readonly Stack<Node> stack = new Stack<Node>();
        private readonly List<AggregateState> aggregates = new List<AggregateState>();
        private int nextInvocation;
        private int nextEdge;
        private int nextSequence;
        private int nextDecisionEvent;
        private int startedAttemptCount;
        private int pendingParent;
        private string pendingEdge;
        private PrimaryCausalCrossAxisEdge pendingCrossAxisEdge;
        private PrimaryCausalCrossAxisEdge activeCrossAxisEdge;
        private int activePsiRuleSource;
        private Partition pendingPartition;
        private PrimaryCausalTerminalEvidence? terminal;
        private PrimaryCausalLineageRecord[] terminalAncestorChain;
        private PrimaryCausalAttemptCore? preReservation;
        private PrimaryCausalCrossAxisEdge[] terminalRuleEdges;
        private AggregateState psiOverflow;
        private AggregateState etaOverflow;
        private AggregateState etaXOverflow;
        private bool hasPsiOverflow;
        private bool hasEtaOverflow;
        private bool hasEtaXOverflow;

        /// <summary>Initializes one observer for exactly one primary invocation.</summary>
        internal PrimaryCausalObserver(PrimaryCausalInvocation invocation) { this.invocation = invocation; }

        /// <summary>Allocates immutable same-axis invocation identity at actual function entry.</summary>
        internal void Enter(string axis, double outer, double left, double right, int depth)
        {
            int parent = pendingParent; string edge = pendingEdge; PrimaryCausalCrossAxisEdge crossAxisEdge = pendingCrossAxisEdge; Partition partition = pendingPartition;
            pendingParent = 0; pendingEdge = null; pendingCrossAxisEdge = default; pendingPartition = default;
            int id = ++nextInvocation; string path = parent == 0 ? "root" : stack.Peek().Path + "/" + edge;
            var node = new Node(id, parent, path, axis, outer, depth, edge, crossAxisEdge, partition, left, right); stack.Push(node); StoreRecord(node);
        }

        /// <summary>Records the return estimate and releases the current lineage frame.</summary>
        internal void Exit(double value)
        {
            if (stack.Count == 0) return;
            Node node = stack.Pop(); node.Return = value; StoreRecord(node); UpdateAggregate(node);
        }

        /// <summary>Creates one explicit Psi-to-partition edge in canonical rule-node order.</summary>
        internal void BeginPsiNode(string rule, int index, double psi, double canonical)
        {
            if (stack.Count == 0 || stack.Peek().Axis != "psi") return;
            int source = stack.Peek().Id; if (activePsiRuleSource != source) { activePsiRuleSource = source; activePsiRuleEdges.Clear(); }
            var edge = new PrimaryCausalCrossAxisEdge(++nextEdge, source, rule, index, psi, rule == "fine5" && index == 4, canonical);
            if (source <= Limit && edges.Count < Limit) edges.Add(edge); activeCrossAxisEdge = edge; pendingCrossAxisEdge = edge;
            if (activePsiRuleEdges.Count < TerminalRuleEdgeAllowance) activePsiRuleEdges.Add(edge);
        }

        /// <summary>Supplies immutable partition provenance to the next eta-root entry.</summary>
        internal void BeginPartition(int line, int index, double etaLeft, double etaRight, bool x, string leftLabel, string rightLabel)
        {
            pendingPartition = new Partition(line, index, etaLeft, etaRight, x, leftLabel, rightLabel); pendingParent = 0; pendingCrossAxisEdge = activeCrossAxisEdge;
        }

        /// <summary>Marks the next recursive entry as the current same-axis left or right child.</summary>
        internal void BeginChild(string edge)
        {
            if (stack.Count == 0) return;
            Node parent = stack.Peek(); pendingParent = parent.Id; pendingEdge = edge; pendingCrossAxisEdge = parent.CrossAxisEdge; pendingPartition = parent.Partition;
        }

        /// <summary>Records a mode-common attempt identity before reservation is observed.</summary>
        internal int BeginAttempt(string axis, double psi, double eta, double sample, double left, double right, int depth, double rawX, double jacobian)
        {
            Node node = stack.Count == 0 ? default : stack.Peek(); int sequence = ++nextSequence;
            var core = new PrimaryCausalAttemptCore(sequence, invocation.SwitchBranch, invocation.P, invocation.NdotV, axis, psi, eta, sample, left, right, depth, node.Partition.Line, node.Partition.Index, axis == "eta-x" ? eta : double.NaN, rawX, jacobian);
            return StoreCore(core) ? sequence : sequence;
        }

        /// <summary>Records one scalar attempt that passed every pre-kernel condition.</summary>
        internal void RecordStartedAttempt(int sequence)
        {
            if (sequence > 0) startedAttemptCount++;
        }

        /// <summary>Records reservation state separately and retains a finite rejected pre-kernel core.</summary>
        internal void RecordReservation(int sequence, bool hasBudget, bool accepted, int used, int limit)
        {
            PrimaryCausalAttemptCore core = Find(sequence); ReservationObservationState state = !hasBudget ? ReservationObservationState.NotApplicable : accepted ? ReservationObservationState.Accepted : ReservationObservationState.Rejected;
            if (reservations.Count < Limit) reservations.Add(new ReservationObservation(sequence, state, hasBudget ? used : 0, hasBudget ? limit : 0, core));
            if (!accepted && hasBudget) { preReservation = core; RemoveLastCore(sequence); }
        }

        /// <summary>Records one real panel, acceptance, or depth condition evaluation in split order.</summary>
        internal void RecordDecisionCondition(string condition)
        {
            if (stack.Count == 0) return;
            Node node = stack.Pop(); int order = ++nextDecisionEvent;
            if (condition == "panel-cap") node.PanelCapOrder = order;
            else if (condition == "accepted") node.AcceptanceOrder = order;
            else if (condition == "depth-cap") node.DepthCapOrder = order;
            stack.Push(node); StoreRecord(node);
        }

        /// <summary>Records decision evidence without determining the decision itself.</summary>
        internal void RecordDecision(string decision, string axis, double outer, double left, double right, int depth, AdaptiveEstimate coarse, AdaptiveEstimate fine, double delta, double absolute, double relative, double error, double limit)
        {
            if (stack.Count == 0) return;
            Node node = stack.Pop(); node.Decision = decision; node.Entry = fine.Value; node.Absolute = absolute; node.Relative = relative; stack.Push(node); StoreRecord(node);
            if (decision != "depth-cap" || terminal.HasValue) return;
            var identity = new PrimaryCausalTerminalInvocation(axis, !double.IsNaN(outer), double.IsNaN(outer) ? 0.0d : outer, left, right, depth);
            var arithmetic = new PrimaryCausalDepthEvidence(coarse.Value, fine.Value, fine.Error, delta, absolute, relative, error, limit, error / limit);
            terminal = new PrimaryCausalTerminalEvidence(identity, PrimaryCausalBaselineState.DepthCap, decision, arithmetic, DecisionOrder(node)); terminalRuleEdges = activePsiRuleEdges.ToArray(); terminalAncestorChain = Chain();
        }

        /// <summary>Builds immutable bounded evidence after the unchanged primary result is complete.</summary>
        internal PrimaryCausalRun Complete(AdaptiveResult result, PrimaryCausalMode mode, PrimaryCausalObserverDisabledWitness witness, PrimaryCausalDirectResult direct, PrimaryCausalObserverIsolationSnapshot? isolation, ulong? preObserverStateDigest, ulong? postObserverStateDigest)
        {
            PrimaryCausalBaselineState state = State(result); string decision = terminal.HasValue ? terminal.Value.Decision : state == PrimaryCausalBaselineState.Accepted ? "accepted" : state == PrimaryCausalBaselineState.BudgetExhausted ? "selection-budget-pre-kernel" : "other";
            PrimaryCausalCompleteResult? complete = new PrimaryCausalCompleteResult(state, decision, startedAttemptCount, result.Value, result.Error, terminal, result.Tolerance, result.Evaluations, result.Panels, result.Depth, result.Diagnostic); ulong modeCommonCoreDigest = Digest();
            bool available = complete.HasValue && modeCommonCoreDigest != 0UL && isolation.HasValue && isolation.Value.IsObserved && preObserverStateDigest.HasValue && postObserverStateDigest.HasValue;
            return new PrimaryCausalRun(invocation, mode, available ? PrimaryCausalAvailability.Available : PrimaryCausalAvailability.Unavailable, available ? string.Empty : "observer-state-identity-unavailable", attempts.ToArray(), reservations.ToArray(), Records(), Terminals(), Edges(), Aggregates(), terminalAncestorChain ?? Array.Empty<PrimaryCausalLineageRecord>(), null, state, complete, modeCommonCoreDigest, preObserverStateDigest, postObserverStateDigest, witness, isolation, direct, preReservation);
        }

        /// <summary>Stores one core while its bounded retention prefix has capacity.</summary>
        private bool StoreCore(PrimaryCausalAttemptCore core) { if (attempts.Count < Limit) attempts.Add(core); return true; }
        /// <summary>Finds a retained core by its run-local sequence.</summary>
        private PrimaryCausalAttemptCore Find(int sequence) { foreach (PrimaryCausalAttemptCore value in attempts) if (value.Sequence == sequence) return value; return default; }
        /// <summary>Removes the immediately preceding rejected pre-reservation core from the retained prefix.</summary>
        private void RemoveLastCore(int sequence) { if (attempts.Count > 0 && attempts[attempts.Count - 1].Sequence == sequence) attempts.RemoveAt(attempts.Count - 1); }
        /// <summary>Stores or updates one ordinary lineage record inside the bounded prefix.</summary>
        private void StoreRecord(Node node) { if (node.Id > Limit) return; if (records.Count < node.Id) records.Add(node); else records[node.Id - 1] = node; }
        /// <summary>Publishes the retained ordinary lineage prefix as immutable records.</summary>
        private PrimaryCausalLineageRecord[] Records() { var values = new PrimaryCausalLineageRecord[records.Count]; for (int index = 0; index < values.Length; index++) values[index] = records[index].Record; return values; }
        /// <summary>Publishes the single terminal identity when a depth terminal was observed.</summary>
        private PrimaryCausalTerminalInvocation[] Terminals() => terminal.HasValue ? new[] { terminal.Value.Identity } : Array.Empty<PrimaryCausalTerminalInvocation>();
        /// <summary>Publishes the ordinary edge prefix plus independent terminal-rule ownership edges.</summary>
        private PrimaryCausalCrossAxisEdge[] Edges() { int extra = 0; if (terminalRuleEdges != null) foreach (PrimaryCausalCrossAxisEdge edge in terminalRuleEdges) if (!ContainsEdge(edge.EdgeId)) extra++; var values = new PrimaryCausalCrossAxisEdge[edges.Count + extra]; edges.CopyTo(values); int destination = edges.Count; if (terminalRuleEdges != null) foreach (PrimaryCausalCrossAxisEdge edge in terminalRuleEdges) if (!ContainsEdge(edge.EdgeId)) values[destination++] = edge; return values; }
        /// <summary>Gets whether an ordinary retained edge owns the specified identity.</summary>
        private bool ContainsEdge(int id) { foreach (PrimaryCausalCrossAxisEdge edge in edges) if (edge.EdgeId == id) return true; return false; }
        /// <summary>Copies the current terminal-to-root same-axis and cross-axis ancestry chain.</summary>
        private PrimaryCausalLineageRecord[] Chain() { if (!terminal.HasValue || stack.Count == 0) return Array.Empty<PrimaryCausalLineageRecord>(); Node[] active = stack.ToArray(); var values = new PrimaryCausalLineageRecord[active.Length]; for (int index = 0; index < values.Length; index++) values[index] = active[index].Record; return values; }
        /// <summary>Builds a terminal decision order only from the condition events observed on its node.</summary>
        private static PrimaryCausalDecisionOrder? DecisionOrder(Node node) { if (node.PanelCapOrder <= 0 || node.AcceptanceOrder <= node.PanelCapOrder || node.DepthCapOrder <= node.AcceptanceOrder) return null; return new PrimaryCausalDecisionOrder(node.PanelCapOrder, node.AcceptanceOrder, node.DepthCapOrder); }
        /// <summary>Updates an ordinary partition aggregate or its axis-specific overflow summary.</summary>
        private void UpdateAggregate(Node node) { int index = FindAggregate(node); if (index >= 0) { AggregateState value = aggregates[index]; value.Add(node.Id, node.Return); aggregates[index] = value; return; } if (aggregates.Count < Limit) { var value = new AggregateState(node.Axis, node.Partition.Line, node.Partition.Index); value.Add(node.Id, node.Return); aggregates.Add(value); return; } UpdateOverflow(node); }
        /// <summary>Finds the aggregate corresponding to a node's axis and source partition.</summary>
        private int FindAggregate(Node node) { for (int index = 0; index < aggregates.Count; index++) if (aggregates[index].Matches(node.Axis, node.Partition.Line, node.Partition.Index)) return index; return -1; }
        /// <summary>Updates the single overflow aggregate reserved for the node's axis.</summary>
        private void UpdateOverflow(Node node) { if (node.Axis == "psi") { if (!hasPsiOverflow) { psiOverflow = new AggregateState("psi", -1, -1); hasPsiOverflow = true; } psiOverflow.Add(node.Id, node.Return); return; } if (node.Axis == "eta") { if (!hasEtaOverflow) { etaOverflow = new AggregateState("eta", -1, -1); hasEtaOverflow = true; } etaOverflow.Add(node.Id, node.Return); return; } if (!hasEtaXOverflow) { etaXOverflow = new AggregateState("eta-x", -1, -1); hasEtaXOverflow = true; } etaXOverflow.Add(node.Id, node.Return); }
        /// <summary>Publishes ordinary aggregates and at most one overflow summary for each axis.</summary>
        private PrimaryCausalAggregate[] Aggregates() { int count = aggregates.Count + (hasPsiOverflow ? 1 : 0) + (hasEtaOverflow ? 1 : 0) + (hasEtaXOverflow ? 1 : 0); var values = new PrimaryCausalAggregate[count]; for (int index = 0; index < aggregates.Count; index++) values[index] = aggregates[index].Record; int destination = aggregates.Count; if (hasPsiOverflow) values[destination++] = psiOverflow.Record; if (hasEtaOverflow) values[destination++] = etaOverflow.Record; if (hasEtaXOverflow) values[destination] = etaXOverflow.Record; return values; }
        /// <summary>Hashes every mode-common field of the first 512 retained scalar cores.</summary>
        private ulong Digest() { ulong hash = 1469598103934665603UL; int count = Math.Min(attempts.Count, 512); for (int index = 0; index < count; index++) hash = AppendCore(hash, attempts[index]); return hash == 0UL ? 1UL : hash; }
        /// <summary>Appends all immutable fields of one scalar core to the deterministic digest.</summary>
        private static ulong AppendCore(ulong hash, PrimaryCausalAttemptCore core) { hash = AppendInt(hash, core.Sequence); hash = AppendInt(hash, core.SwitchBranch ? 1 : 0); hash = AppendDouble(hash, core.P); hash = AppendDouble(hash, core.NdotV); hash = AppendText(hash, core.Axis); hash = AppendDouble(hash, core.Psi); hash = AppendDouble(hash, core.Eta); hash = AppendDouble(hash, core.Sample); hash = AppendDouble(hash, core.Left); hash = AppendDouble(hash, core.Right); hash = AppendInt(hash, core.Depth); hash = AppendInt(hash, core.PartitionLine); hash = AppendInt(hash, core.PartitionIndex); hash = AppendDouble(hash, core.PreTransformEta); hash = AppendDouble(hash, core.RawX); return AppendDouble(hash, core.Jacobian); }
        /// <summary>Appends one signed integer without normalization.</summary>
        private static ulong AppendInt(ulong hash, int value) => (hash ^ unchecked((ulong)value)) * 1099511628211UL;
        /// <summary>Appends one binary64 value by its exact bit identity.</summary>
        private static ulong AppendDouble(ulong hash, double value) => (hash ^ unchecked((ulong)BitConverter.DoubleToInt64Bits(value))) * 1099511628211UL;
        /// <summary>Appends one nullable text value by length and UTF-16 code units.</summary>
        private static ulong AppendText(ulong hash, string value) { hash = AppendInt(hash, value == null ? -1 : value.Length); if (value != null) for (int index = 0; index < value.Length; index++) hash = AppendInt(hash, value[index]); return hash; }
        /// <summary>Classifies the immutable adaptive result without affecting integration behavior.</summary>
        private static PrimaryCausalBaselineState State(AdaptiveResult result) { if (result.Diagnostic == null) return PrimaryCausalBaselineState.Accepted; if (result.Diagnostic.IndexOf("selection-budget", StringComparison.Ordinal) >= 0) return PrimaryCausalBaselineState.BudgetExhausted; return result.Diagnostic.IndexOf("primary depth", StringComparison.Ordinal) >= 0 ? PrimaryCausalBaselineState.DepthCap : PrimaryCausalBaselineState.Other; }

        /// <summary>Stores immutable source-partition provenance for a pending eta root.</summary>
        private readonly struct Partition
        {
            /// <summary>Initializes one immutable partition provenance value.</summary>
            internal Partition(int line, int index, double left, double right, bool x, string leftLabel, string rightLabel) { Line = line; Index = index; Left = left; Right = right; X = x; LeftLabel = leftLabel; RightLabel = rightLabel; }
            internal int Line { get; } internal int Index { get; } internal double Left { get; } internal double Right { get; } internal bool X { get; } internal string LeftLabel { get; } internal string RightLabel { get; }
        }

        /// <summary>Tracks a mutable active same-axis invocation before publication.</summary>
        private struct Node
        {
            /// <summary>Initializes one active invocation with empty result and condition evidence.</summary>
            internal Node(int id, int parent, string path, string axis, double outer, int depth, string edge, PrimaryCausalCrossAxisEdge crossAxisEdge, Partition partition, double left, double right) { Id = id; Parent = parent; Path = path; Axis = axis; Outer = outer; Depth = depth; Edge = edge; CrossAxisEdge = crossAxisEdge; Partition = partition; Left = left; Right = right; Entry = 0.0d; Return = 0.0d; Absolute = 0.0d; Relative = 0.0d; Decision = null; PanelCapOrder = 0; AcceptanceOrder = 0; DepthCapOrder = 0; }
            internal int Id; internal int Parent; internal string Path; internal string Axis; internal double Outer; internal int Depth; internal string Edge; internal PrimaryCausalCrossAxisEdge CrossAxisEdge; internal Partition Partition; internal double Left; internal double Right; internal double Entry; internal double Return; internal double Absolute; internal double Relative; internal string Decision; internal int PanelCapOrder; internal int AcceptanceOrder; internal int DepthCapOrder;
            /// <summary>Builds the immutable lineage projection of this active invocation.</summary>
            internal PrimaryCausalLineageRecord Record => Axis == "psi" ? new PrimaryCausalLineageRecord(Id, Parent, Path, Axis, Outer, Depth, Entry, Return, Absolute, Relative, Decision, null, Edge, CrossAxisEdge.EdgeId, -1, -1, 0.0d, 0.0d, 0.0d, 0.0d, null, null, "psi") : new PrimaryCausalLineageRecord(Id, Parent, Path, Axis, Outer, Depth, Entry, Return, Absolute, Relative, Decision, null, Edge, CrossAxisEdge.EdgeId, Partition.Line, Partition.Index, Partition.Left, Partition.Right, Axis == "eta-x" ? Left : Partition.Left, Axis == "eta-x" ? Right : Partition.Right, Partition.LeftLabel, Partition.RightLabel, Axis);
        }

        /// <summary>Accumulates online axis and partition observations without raw-core retention.</summary>
        private struct AggregateState
        {
            /// <summary>Initializes an empty aggregate with deterministic digest seed.</summary>
            internal AggregateState(string axis, int line, int index) { Axis = axis; Line = line; Index = index; Count = 0; Maximum = double.NegativeInfinity; MaximumSequence = 0; Digest = 1469598103934665603UL; }
            internal string Axis; internal int Line; internal int Index; internal long Count; internal double Maximum; internal int MaximumSequence; internal ulong Digest;
            /// <summary>Gets whether the aggregate owns the supplied axis and partition.</summary>
            internal bool Matches(string axis, int line, int index) => Axis == axis && Line == line && Index == index;
            /// <summary>Adds one value while preserving the earliest maximum tie.</summary>
            internal void Add(int sequence, double value) { Count++; if (value > Maximum) { Maximum = value; MaximumSequence = sequence; } Digest = (Digest ^ unchecked((ulong)sequence)) * 1099511628211UL; Digest = (Digest ^ unchecked((ulong)BitConverter.DoubleToInt64Bits(value))) * 1099511628211UL; }
            /// <summary>Builds the immutable aggregate projection.</summary>
            internal PrimaryCausalAggregate Record => new PrimaryCausalAggregate(Axis, Line, Index, Count, Maximum, MaximumSequence, Digest == 0UL ? 1UL : Digest);
        }
    }
}
