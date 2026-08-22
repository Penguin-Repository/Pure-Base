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

// Defines immutable bounded provenance records for causal primary diagnostics.

using System;
using System.Collections.Generic;

namespace PureBase.Tests.Daily
{
    /// <summary>Stores one explicit Psi-to-Eta or Psi-to-Eta-x invocation edge.</summary>
    internal readonly struct PrimaryCausalCrossAxisEdge
    {
        /// <summary>Initializes an edge before the associated partition root is invoked.</summary>
        internal PrimaryCausalCrossAxisEdge(int edgeId, int sourcePsiInvocationId, string ruleKind, int nodeIndex, double psiNode, bool completesRule = false, double canonicalNode = double.NaN)
        {
            EdgeId = edgeId; SourcePsiInvocationId = sourcePsiInvocationId; RuleKind = ruleKind; NodeIndex = nodeIndex; PsiNode = psiNode; CompletesRule = completesRule; CanonicalNode = canonicalNode;
        }

        /// <summary>Gets the monotonic cross-axis edge identity.</summary>
        internal int EdgeId { get; }
        /// <summary>Gets the Psi invocation that created this edge.</summary>
        internal int SourcePsiInvocationId { get; }
        /// <summary>Gets the coarse3 or fine5 rule identity.</summary>
        internal string RuleKind { get; }
        /// <summary>Gets the canonical rule-node index.</summary>
        internal int NodeIndex { get; }
        /// <summary>Gets the exact Psi node value.</summary>
        internal double PsiNode { get; }
        /// <summary>Gets whether this edge completes the retained Psi 3/5 rule evaluation.</summary>
        internal bool CompletesRule { get; }
        /// <summary>Gets the exact canonical Gauss-Legendre node before interval mapping.</summary>
        internal double CanonicalNode { get; }
    }

    /// <summary>Stores online count, earliest-tie maximum, and digest for one axis partition.</summary>
    internal readonly struct PrimaryCausalAggregate
    {
        /// <summary>Initializes one bounded aggregate without retaining individual sample records.</summary>
        internal PrimaryCausalAggregate(string axis, int partitionLine, int partitionIndex, long count, double maximum, int maximumSequence, ulong rollingDigest)
        {
            Axis = axis; PartitionLine = partitionLine; PartitionIndex = partitionIndex; Count = count; Maximum = maximum; MaximumSequence = maximumSequence; RollingDigest = rollingDigest;
        }

        /// <summary>Gets the owning coordinate system.</summary>
        internal string Axis { get; }
        /// <summary>Gets the eta partition line, or a negative value when not applicable.</summary>
        internal int PartitionLine { get; }
        /// <summary>Gets the eta partition index, or a negative value when not applicable.</summary>
        internal int PartitionIndex { get; }
        /// <summary>Gets the total observations represented by this aggregate.</summary>
        internal long Count { get; }
        /// <summary>Gets the maximum observed value.</summary>
        internal double Maximum { get; }
        /// <summary>Gets the first sequence that reached the maximum.</summary>
        internal int MaximumSequence { get; }
        /// <summary>Gets the deterministic rolling sequence digest.</summary>
        internal ulong RollingDigest { get; }
    }

    /// <summary>Stores a same-axis invocation record with complete partition and transform provenance.</summary>
    internal readonly struct PrimaryCausalLineageRecord
    {
        /// <summary>Initializes a compatibility lineage record without optional numerical provenance.</summary>
        internal PrimaryCausalLineageRecord(int invocationId, int parentId, string path, string axis, double outer, int depth, string decision)
            : this(invocationId, parentId, path, axis, outer, depth, 0.0d, 0.0d, 0.0d, 0.0d, decision, null, null, 0, -1, -1, 0.0d, 0.0d, 0.0d, 0.0d, null, null, null) { }

        /// <summary>Initializes one complete immutable same-axis invocation record.</summary>
        internal PrimaryCausalLineageRecord(int invocationId, int sameAxisParentId, string path, string axis, double outer, int depth, double entryEstimate, double returnEstimate, double absoluteAllocation, double relativeShare, string decision, string childAggregation, string sameAxisEdge, int crossAxisEdgeId, int partitionLine, int partitionIndex, double originalEtaLeft, double originalEtaRight, double transformedLeft, double transformedRight, string leftBoundary, string rightBoundary, string transformIdentity)
        {
            InvocationId = invocationId; SameAxisParentId = sameAxisParentId; Path = path; Axis = axis; Outer = outer; Depth = depth; EntryEstimate = entryEstimate; ReturnEstimate = returnEstimate; AbsoluteAllocation = absoluteAllocation; RelativeShare = relativeShare; Decision = decision; ChildAggregation = childAggregation; SameAxisEdge = sameAxisEdge; CrossAxisEdgeId = crossAxisEdgeId; PartitionLine = partitionLine; PartitionIndex = partitionIndex; OriginalEtaLeft = originalEtaLeft; OriginalEtaRight = originalEtaRight; TransformedLeft = transformedLeft; TransformedRight = transformedRight; LeftBoundary = leftBoundary; RightBoundary = rightBoundary; TransformIdentity = transformIdentity;
        }

        /// <summary>Gets the monotonic invocation identity.</summary>
        internal int InvocationId { get; }
        /// <summary>Gets the same-axis parent identity, or zero for a root.</summary>
        internal int SameAxisParentId { get; }
        /// <summary>Gets the compatibility alias for the same-axis parent identity.</summary>
        internal int ParentId => SameAxisParentId;
        /// <summary>Gets the immutable root/L/R path.</summary>
        internal string Path { get; }
        /// <summary>Gets the invocation coordinate system.</summary>
        internal string Axis { get; }
        /// <summary>Gets the exact outer coordinate, when applicable.</summary>
        internal double Outer { get; }
        /// <summary>Gets the local recursive depth.</summary>
        internal int Depth { get; }
        /// <summary>Gets the estimate on invocation entry.</summary>
        internal double EntryEstimate { get; }
        /// <summary>Gets the estimate returned by this invocation.</summary>
        internal double ReturnEstimate { get; }
        /// <summary>Gets the local absolute allocation.</summary>
        internal double AbsoluteAllocation { get; }
        /// <summary>Gets the local relative allocation share.</summary>
        internal double RelativeShare { get; }
        /// <summary>Gets the recorded return or terminal decision.</summary>
        internal string Decision { get; }
        /// <summary>Gets the observed child-return aggregation.</summary>
        internal string ChildAggregation { get; }
        /// <summary>Gets L or R for a same-axis descendant, or null for a root.</summary>
        internal string SameAxisEdge { get; }
        /// <summary>Gets the originating cross-axis edge, or zero for Psi lineage.</summary>
        internal int CrossAxisEdgeId { get; }
        /// <summary>Gets the eta partition line, or a negative value when not applicable.</summary>
        internal int PartitionLine { get; }
        /// <summary>Gets the eta partition index, or a negative value when not applicable.</summary>
        internal int PartitionIndex { get; }
        /// <summary>Gets the original eta interval's left endpoint.</summary>
        internal double OriginalEtaLeft { get; }
        /// <summary>Gets the original eta interval's right endpoint.</summary>
        internal double OriginalEtaRight { get; }
        /// <summary>Gets the transformed interval's left endpoint.</summary>
        internal double TransformedLeft { get; }
        /// <summary>Gets the transformed interval's right endpoint.</summary>
        internal double TransformedRight { get; }
        /// <summary>Gets the source label of the transformed left boundary.</summary>
        internal string LeftBoundary { get; }
        /// <summary>Gets the source label of the transformed right boundary.</summary>
        internal string RightBoundary { get; }
        /// <summary>Gets the eta or eta-x transform identity.</summary>
        internal string TransformIdentity { get; }
    }

    /// <summary>Stores terminal identity required to match observer-disabled depth evidence.</summary>
    internal readonly struct PrimaryCausalTerminalInvocation
    {
        /// <summary>Initializes one terminal invocation identity.</summary>
        internal PrimaryCausalTerminalInvocation(string axis, bool hasOuter, double outer, double left, double right, int depth)
        {
            Axis = axis; HasOuter = hasOuter; Outer = outer; Left = left; Right = right; Depth = depth;
        }

        /// <summary>Gets the terminal coordinate system.</summary>
        internal string Axis { get; }
        /// <summary>Gets whether an outer coordinate is applicable.</summary>
        internal bool HasOuter { get; }
        /// <summary>Gets the exact outer coordinate when applicable.</summary>
        internal double Outer { get; }
        /// <summary>Gets the exact terminal interval left endpoint.</summary>
        internal double Left { get; }
        /// <summary>Gets the exact terminal interval right endpoint.</summary>
        internal double Right { get; }
        /// <summary>Gets the terminal recursion depth.</summary>
        internal int Depth { get; }
    }

    /// <summary>Stores the observed order of terminal split decisions without encoding their numerical result.</summary>
    internal readonly struct PrimaryCausalDecisionOrder
    {
        /// <summary>Initializes the strictly increasing panel, acceptance, and depth-cap decision sequence.</summary>
        internal PrimaryCausalDecisionOrder(int panelCapOrder, int acceptanceOrder, int depthCapOrder)
        {
            PanelCapOrder = panelCapOrder; AcceptanceOrder = acceptanceOrder; DepthCapOrder = depthCapOrder;
        }

        /// <summary>Gets the panel-cap decision sequence number.</summary>
        internal int PanelCapOrder { get; }
        /// <summary>Gets the acceptance decision sequence number.</summary>
        internal int AcceptanceOrder { get; }
        /// <summary>Gets the depth-cap decision sequence number.</summary>
        internal int DepthCapOrder { get; }
    }

    /// <summary>Stores complete causal terminal evidence for independent witness agreement.</summary>
    internal readonly struct PrimaryCausalTerminalEvidence
    {
        /// <summary>Initializes one complete causal terminal observation.</summary>
        internal PrimaryCausalTerminalEvidence(PrimaryCausalTerminalInvocation identity, PrimaryCausalBaselineState category, string decision, PrimaryCausalDepthEvidence arithmetic, PrimaryCausalDecisionOrder? decisionOrder = null)
        {
            Identity = identity; Category = category; Decision = decision; Arithmetic = arithmetic; DecisionOrder = decisionOrder;
        }

        /// <summary>Gets the exact terminal invocation identity.</summary>
        internal PrimaryCausalTerminalInvocation Identity { get; }
        /// <summary>Gets the observed terminal category.</summary>
        internal PrimaryCausalBaselineState Category { get; }
        /// <summary>Gets the observed terminal decision.</summary>
        internal string Decision { get; }
        /// <summary>Gets the observed terminal arithmetic.</summary>
        internal PrimaryCausalDepthEvidence Arithmetic { get; }
        /// <summary>Gets the retained split decision order, or null when causal instrumentation is unavailable.</summary>
        internal PrimaryCausalDecisionOrder? DecisionOrder { get; }
    }

    /// <summary>Stores the complete outcome retained for one available causal run.</summary>
    internal readonly struct PrimaryCausalCompleteResult
    {
        /// <summary>Initializes a complete result without discarding terminal numerical facts.</summary>
        internal PrimaryCausalCompleteResult(PrimaryCausalBaselineState terminalState, string decision, int startedAttemptCount, double estimate, double error, PrimaryCausalTerminalEvidence? terminalEvidence, double tolerance = 0.0d, int evaluations = 0, int panels = 0, int depth = 0, string diagnostic = null)
        {
            TerminalState = terminalState; Decision = decision; StartedAttemptCount = startedAttemptCount; Estimate = estimate; Error = error; TerminalEvidence = terminalEvidence; Tolerance = tolerance; Evaluations = evaluations; Panels = panels; Depth = depth; Diagnostic = diagnostic;
        }

        /// <summary>Gets the observed terminal state.</summary>
        internal PrimaryCausalBaselineState TerminalState { get; }
        /// <summary>Gets the complete terminal decision.</summary>
        internal string Decision { get; }
        /// <summary>Gets the number of scalar attempts started before the terminal result.</summary>
        internal int StartedAttemptCount { get; }
        /// <summary>Gets the final numerical estimate.</summary>
        internal double Estimate { get; }
        /// <summary>Gets the final numerical error.</summary>
        internal double Error { get; }
        /// <summary>Gets complete terminal evidence when the terminal exposes arithmetic facts.</summary>
        internal PrimaryCausalTerminalEvidence? TerminalEvidence { get; }
        /// <summary>Gets the final primary tolerance.</summary>
        internal double Tolerance { get; }
        /// <summary>Gets the completed primary evaluation count.</summary>
        internal int Evaluations { get; }
        /// <summary>Gets the completed primary panel count.</summary>
        internal int Panels { get; }
        /// <summary>Gets the final primary recursion depth.</summary>
        internal int Depth { get; }
        /// <summary>Gets the final primary diagnostic, or null for acceptance.</summary>
        internal string Diagnostic { get; }
    }

    /// <summary>Stores the only expanded trace retained for the first exact contradiction.</summary>
    internal sealed class PrimaryCausalContradictionTrace
    {
        /// <summary>Stores an immutable copy of the contradiction's expanded lineage.</summary>
        private readonly PrimaryCausalLineageRecord[] lineage;

        /// <summary>Initializes one immutable expanded contradiction trace.</summary>
        internal PrimaryCausalContradictionTrace(int terminalInvocationId, string reason, PrimaryCausalLineageRecord[] lineage)
        {
            TerminalInvocationId = terminalInvocationId; Reason = reason; this.lineage = Copy(lineage);
        }

        /// <summary>Gets the terminal invocation that first contradicted its decision rule.</summary>
        internal int TerminalInvocationId { get; }
        /// <summary>Gets the exact contradiction classification.</summary>
        internal string Reason { get; }
        /// <summary>Gets the retained expanded lineage for the first contradiction only.</summary>
        internal IReadOnlyList<PrimaryCausalLineageRecord> Lineage => lineage;

        /// <summary>Copies a trace so later observer writes cannot alter published evidence.</summary>
        private static PrimaryCausalLineageRecord[] Copy(PrimaryCausalLineageRecord[] values)
        {
            if (values == null || values.Length == 0) return Array.Empty<PrimaryCausalLineageRecord>();
            var copy = new PrimaryCausalLineageRecord[values.Length]; Array.Copy(values, copy, values.Length); return copy;
        }
    }
}
