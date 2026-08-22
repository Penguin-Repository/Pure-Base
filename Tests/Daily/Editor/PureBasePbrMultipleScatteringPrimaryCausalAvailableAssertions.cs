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

// Asserts complete available causal evidence without changing primary arithmetic.

using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Asserts complete retained causal evidence after availability is established.</summary>
    internal static class PrimaryCausalAvailableAssertions
    {
        /// <summary>Stores the exact three-node canonical Gauss-Legendre Psi rule.</summary>
        private static readonly double[] CanonicalPsiNodes3 = { -0.7745966692414834d, 0.0d, 0.7745966692414834d };
        /// <summary>Stores the exact five-node canonical Gauss-Legendre Psi rule.</summary>
        private static readonly double[] CanonicalPsiNodes5 = { -0.906179845938664d, -0.5384693101056831d, 0.0d, 0.5384693101056831d, 0.906179845938664d };

        /// <summary>Checks finite budget exhaustion, the rejected core, and null-mode reservations.</summary>
        internal static void AssertBudgetExhausted(PrimaryCausalRun finite, PrimaryCausalRun unrestricted)
        {
            AssertComplete(finite, PrimaryCausalBaselineState.BudgetExhausted); AssertComplete(unrestricted);
            Assert.That(finite.Attempts.Count, Is.EqualTo(512)); Assert.That(unrestricted.Attempts.Count, Is.GreaterThanOrEqualTo(513));
            Assert.That(finite.CompleteResult.Value.StartedAttemptCount, Is.EqualTo(512)); Assert.That(finite.PreReservationAttemptCore.HasValue, Is.True);
            PrimaryCausalAttemptCore finitePreReservationCore = finite.PreReservationAttemptCore.Value;
            Assert.That(finitePreReservationCore.Sequence, Is.EqualTo(513)); Assert.That(unrestricted.Attempts[512].Sequence, Is.EqualTo(513)); Assert.That(unrestricted.CompleteResult.Value.StartedAttemptCount, Is.GreaterThanOrEqualTo(513)); Assert.That(finite.ModeCommonCoreDigest, Is.EqualTo(unrestricted.ModeCommonCoreDigest)); AssertAttemptRange(finite, unrestricted, 512); AssertAttempt(finitePreReservationCore, unrestricted.Attempts[512]); AssertFiniteReservations(finite, finitePreReservationCore); AssertNullReservations(unrestricted);
            AssertLineage(finite); AssertLineage(unrestricted); AssertTerminalRetention(finite); AssertTerminalRetention(unrestricted);
        }

        /// <summary>Checks every shared numerical and causal surface for an Accepted or DepthCap result.</summary>
        internal static void AssertFullModeEquality(PrimaryCausalRun finite, PrimaryCausalRun unrestricted, PrimaryCausalBaselineState expected)
        {
            AssertComplete(finite, expected); AssertComplete(unrestricted, expected); AssertRunCore(finite, unrestricted);
            Assert.That(finite.Attempts.Count, Is.EqualTo(unrestricted.Attempts.Count)); Assert.That(finite.Attempts.Count, Is.LessThan(513)); Assert.That(unrestricted.Attempts.Count, Is.LessThan(513)); Assert.That(finite.CompleteResult.Value.StartedAttemptCount, Is.EqualTo(finite.Attempts.Count)); Assert.That(unrestricted.CompleteResult.Value.StartedAttemptCount, Is.EqualTo(unrestricted.Attempts.Count)); AssertFiniteAcceptedReservations(finite); AssertNullReservations(unrestricted);
            AssertLineage(finite); AssertLineage(unrestricted); AssertTerminalRetention(finite); AssertTerminalRetention(unrestricted);
        }

        /// <summary>Checks two complete matrix passes for exact run, evidence, direct-entry, and isolation equality.</summary>
        internal static void AssertRepeatedMatrix(PrimaryCausalRun[] runs)
        {
            int passLength = runs.Length / 2; Assert.That(runs.Length, Is.EqualTo(passLength * 2));
            for (int index = 0; index < passLength; index++) { AssertRun(runs[index], runs[index + passLength]); AssertDirectParity(runs[index]); AssertDirectParity(runs[index + passLength]); }
        }

        /// <summary>Checks deterministic evidence equality and cache-artifact isolation across two observations.</summary>
        internal static void AssertDeterministicAndIsolated(PrimaryCausalRun first, PrimaryCausalRun second)
        {
            AssertRun(first, second); AssertIsolation(first); AssertIsolation(second); AssertDirectParity(first); AssertDirectParity(second);
        }

        /// <summary>Checks same-mode deterministic evidence when a finite reservation masks a different null-mode outcome.</summary>
        internal static void AssertDeterministicBudgetMaskedRuns(PrimaryCausalRun first, PrimaryCausalRun second)
        {
            AssertComplete(first); AssertComplete(second); Assert.That(first.Mode, Is.EqualTo(second.Mode)); AssertInvocation(first.Invocation, second.Invocation); AssertRunCore(first, second); AssertReservations(first, second); AssertIsolation(first); AssertIsolation(second); AssertTrace(first.FirstContradictionTrace, second.FirstContradictionTrace);
        }

        /// <summary>Classifies a complete observed depth terminal from its causal record and independently reparsed witness.</summary>
        internal static PrimaryCausalGateResult AssertDepthCapInvariants(PrimaryCausalRun run)
        {
            AssertComplete(run, PrimaryCausalBaselineState.DepthCap); Assert.That(run.CompleteResult.Value.TerminalEvidence.HasValue, Is.True); AssertTerminalRetention(run);
            PrimaryCausalTerminalEvidence causal = run.CompleteResult.Value.TerminalEvidence.Value;
            if (run.ObserverDisabledWitness == null || !PrimaryCausalDiagnosticParser.TryParseObserverDisabledDepth(run.ObserverDisabledWitness.RawDiagnostic, out PrimaryCausalObserverDisabledWitness witness)) return PrimaryCausalGateResult.Reject;
            if (!SameEvidence(causal, witness)) return PrimaryCausalGateResult.Reject;
            if (IsNormalDepthCap(causal) && IsNormalDepthCap(witness)) return PrimaryCausalGateResult.NoRepair;
            return IsDecisionControlFlowContradiction(causal) && IsDecisionControlFlowContradiction(witness) && HasObservedCapOrder(causal) ? PrimaryCausalGateResult.AuthorizeTerminalSplitRepair : PrimaryCausalGateResult.Reject;
        }

        /// <summary>Requires exactly matching depth terminals and parsed arithmetic before classification.</summary>
        private static bool SameEvidence(PrimaryCausalTerminalEvidence causal, PrimaryCausalObserverDisabledWitness witness)
        {
            return causal.Category == PrimaryCausalBaselineState.DepthCap && witness.Category == PrimaryCausalBaselineState.DepthCap && causal.Decision == "depth-cap" && witness.Decision == "depth-cap" && SameTerminal(causal.Identity, witness.Terminal) && SameDepth(causal.Arithmetic, witness.Arithmetic);
        }

        /// <summary>Recognizes one finite, correctly recomputed normal depth-cap decision.</summary>
        private static bool IsNormalDepthCap(PrimaryCausalTerminalEvidence terminal) => HasCorrectArithmetic(terminal.Arithmetic) && terminal.Arithmetic.Error > terminal.Arithmetic.Limit;

        /// <summary>Recognizes one finite, correctly recomputed parser-produced normal depth-cap decision.</summary>
        private static bool IsNormalDepthCap(PrimaryCausalObserverDisabledWitness witness) => HasCorrectArithmetic(witness.Arithmetic) && witness.Arithmetic.Error > witness.Arithmetic.Limit;

        /// <summary>Recognizes one finite depth-cap decision that contradicts its at-or-below-limit evidence.</summary>
        private static bool IsDecisionControlFlowContradiction(PrimaryCausalTerminalEvidence terminal) => HasCorrectArithmetic(terminal.Arithmetic) && terminal.Arithmetic.Error <= terminal.Arithmetic.Limit;

        /// <summary>Recognizes one parser-produced depth-cap decision that contradicts its at-or-below-limit evidence.</summary>
        private static bool IsDecisionControlFlowContradiction(PrimaryCausalObserverDisabledWitness witness) => HasCorrectArithmetic(witness.Arithmetic) && witness.Arithmetic.Error <= witness.Arithmetic.Limit;

        /// <summary>Requires finite arithmetic facts and exact raw recomputation without consulting the repair gate.</summary>
        private static bool HasCorrectArithmetic(PrimaryCausalDepthEvidence value)
        {
            return IsFinite(value.Coarse) && IsFinite(value.Fine) && IsFinite(value.Inherited) && IsFinite(value.Delta) && IsFinite(value.Absolute) && IsFinite(value.Relative) && IsFinite(value.Error) && IsFinite(value.Limit) && IsFinite(value.ErrorOverLimit) && Bits(value.Error) == Bits(value.Inherited + value.Delta) && Bits(value.Limit) == Bits(value.Absolute + value.Relative) && Bits(value.ErrorOverLimit) == Bits(value.Error / value.Limit);
        }

        /// <summary>Requires the observed causal ordering to reach depth-cap after acceptance.</summary>
        private static bool HasObservedCapOrder(PrimaryCausalTerminalEvidence terminal)
        {
            if (!terminal.DecisionOrder.HasValue) return false;
            PrimaryCausalDecisionOrder order = terminal.DecisionOrder.Value;
            return order.PanelCapOrder < order.AcceptanceOrder && order.AcceptanceOrder < order.DepthCapOrder;
        }

        /// <summary>Compares exact terminal identity fields without numeric normalization.</summary>
        private static bool SameTerminal(PrimaryCausalTerminalInvocation left, PrimaryCausalTerminalInvocation right)
        {
            return left.Axis == right.Axis && left.HasOuter == right.HasOuter && (!left.HasOuter || Bits(left.Outer) == Bits(right.Outer)) && Bits(left.Left) == Bits(right.Left) && Bits(left.Right) == Bits(right.Right) && left.Depth == right.Depth;
        }

        /// <summary>Compares every retained depth fact without tolerance.</summary>
        private static bool SameDepth(PrimaryCausalDepthEvidence left, PrimaryCausalDepthEvidence right)
        {
            return Bits(left.Coarse) == Bits(right.Coarse) && Bits(left.Fine) == Bits(right.Fine) && Bits(left.Inherited) == Bits(right.Inherited) && Bits(left.Delta) == Bits(right.Delta) && Bits(left.Absolute) == Bits(right.Absolute) && Bits(left.Relative) == Bits(right.Relative) && Bits(left.Error) == Bits(right.Error) && Bits(left.Limit) == Bits(right.Limit) && Bits(left.ErrorOverLimit) == Bits(right.ErrorOverLimit);
        }

        /// <summary>Gets whether one binary64 value is finite.</summary>
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Checks a complete result, observed terminal category, and mode-common digest.</summary>
        private static void AssertComplete(PrimaryCausalRun run, PrimaryCausalBaselineState expected)
        {
            AssertComplete(run); Assert.That(run.TerminalState, Is.EqualTo(expected)); Assert.That(run.CompleteResult.Value.TerminalState, Is.EqualTo(expected));
        }

        /// <summary>Checks complete retained evidence without assuming a mode-specific terminal category.</summary>
        private static void AssertComplete(PrimaryCausalRun run)
        {
            Assert.That(run.CompleteResult.HasValue, Is.True); Assert.That(run.TerminalState, Is.EqualTo(run.CompleteResult.Value.TerminalState)); Assert.That(run.ModeCommonCoreDigest, Is.Not.EqualTo(0UL));
        }

        /// <summary>Checks every non-reservation surface shared by both budget modes.</summary>
        private static void AssertRunCore(PrimaryCausalRun left, PrimaryCausalRun right)
        {
            AssertResult(left.CompleteResult.Value, right.CompleteResult.Value); Assert.That(left.ModeCommonCoreDigest, Is.EqualTo(right.ModeCommonCoreDigest)); Assert.That(left.Attempts.Count, Is.EqualTo(right.Attempts.Count)); AssertAttemptRange(left, right, left.Attempts.Count);
            AssertLineageRange(left, right); AssertEdges(left, right); AssertAggregates(left, right); AssertTerminals(left, right); AssertLineageRange(left.TerminalAncestorChain, right.TerminalAncestorChain); Assert.That(left.TerminalState, Is.EqualTo(right.TerminalState)); AssertWitness(left.ObserverDisabledWitness, right.ObserverDisabledWitness); AssertDirectResult(left.DirectResult, right.DirectResult); AssertCompleteDirectResult(left); AssertCompleteDirectResult(right); AssertTrace(left.FirstContradictionTrace, right.FirstContradictionTrace); AssertIsolationSnapshot(left.ObserverIsolationSnapshot, right.ObserverIsolationSnapshot); Assert.That(left.PreObserverStateDigest, Is.EqualTo(right.PreObserverStateDigest)); Assert.That(left.PostObserverStateDigest, Is.EqualTo(right.PostObserverStateDigest));
        }

        /// <summary>Checks every retained field for repeated observations of one identical causal run.</summary>
        private static void AssertRun(PrimaryCausalRun left, PrimaryCausalRun right)
        {
            Assert.That(left.Availability, Is.EqualTo(PrimaryCausalAvailability.Available)); Assert.That(right.Availability, Is.EqualTo(PrimaryCausalAvailability.Available)); AssertComplete(left, left.Invocation.BaselineState); AssertComplete(right, right.Invocation.BaselineState); Assert.That(left.Mode, Is.EqualTo(right.Mode)); AssertInvocation(left.Invocation, right.Invocation); AssertRunCore(left, right); AssertReservations(left, right); AssertWitness(left.ObserverDisabledWitness, right.ObserverDisabledWitness); AssertDirectResult(left.DirectResult, right.DirectResult);
            AssertIsolation(left); AssertIsolation(right); Assert.That(left.PreObserverStateDigest, Is.EqualTo(right.PreObserverStateDigest)); Assert.That(left.PostObserverStateDigest, Is.EqualTo(right.PostObserverStateDigest)); AssertTrace(left.FirstContradictionTrace, right.FirstContradictionTrace);
        }

        /// <summary>Checks finite accepted reservations plus the separate rejected 513th reservation.</summary>
        private static void AssertFiniteReservations(PrimaryCausalRun run, PrimaryCausalAttemptCore rejectedCore)
        {
            Assert.That(run.Reservations.Count, Is.EqualTo(513));
            for (int index = 0; index < 512; index++) AssertReservation(run.Reservations[index], index + 1, ReservationObservationState.Accepted, index + 1, 512, run.Attempts[index]);
            AssertReservation(run.Reservations[512], 513, ReservationObservationState.Rejected, 512, 512, rejectedCore);
        }

        /// <summary>Checks finite reservations for a result that completed before the selection limit.</summary>
        private static void AssertFiniteAcceptedReservations(PrimaryCausalRun run)
        {
            Assert.That(run.Reservations.Count, Is.EqualTo(run.Attempts.Count));
            for (int index = 0; index < run.Attempts.Count; index++) AssertReservation(run.Reservations[index], index + 1, ReservationObservationState.Accepted, index + 1, 512, run.Attempts[index]);
        }

        /// <summary>Checks a no-selection-budget mode retains a NotApplicable reservation per started core.</summary>
        private static void AssertNullReservations(PrimaryCausalRun run)
        {
            Assert.That(run.Reservations.Count, Is.EqualTo(run.Attempts.Count));
            for (int index = 0; index < run.Attempts.Count; index++) AssertReservation(run.Reservations[index], index + 1, ReservationObservationState.NotApplicable, 0, 0, run.Attempts[index]);
        }

        /// <summary>Checks one complete reservation observation and its scalar-core identity.</summary>
        private static void AssertReservation(ReservationObservation actual, int sequence, ReservationObservationState state, int used, int limit, PrimaryCausalAttemptCore? core)
        {
            Assert.That(actual.Sequence, Is.EqualTo(sequence)); Assert.That(actual.State, Is.EqualTo(state)); Assert.That(actual.Used, Is.EqualTo(used)); Assert.That(actual.Limit, Is.EqualTo(limit)); Assert.That(actual.Core.HasValue, Is.EqualTo(core.HasValue));
            if (core.HasValue) AssertAttempt(actual.Core.Value, core.Value);
        }

        /// <summary>Checks exact same-axis parent, root/L/R path, cross-axis, and transform provenance.</summary>
        private static void AssertLineage(PrimaryCausalRun run)
        {
            Assert.That(run.Attempts.Count, Is.LessThanOrEqualTo(513)); Assert.That(run.Lineage.Count, Is.LessThanOrEqualTo(513));
            int previous = 0;
            for (int index = 0; index < run.Lineage.Count; index++) { PrimaryCausalLineageRecord record = run.Lineage[index]; Assert.That(record.InvocationId, Is.GreaterThan(previous)); previous = record.InvocationId; AssertLineageRecord(run, record); }
            AssertCrossAxisEdges(run);
        }

        /// <summary>Checks the same-axis parent and complete partition-transform provenance of one invocation.</summary>
        private static void AssertLineageRecord(PrimaryCausalRun run, PrimaryCausalLineageRecord record)
        {
            if (record.SameAxisParentId == 0) { Assert.That(record.Path, Is.EqualTo("root")); Assert.That(record.SameAxisEdge, Is.Null); }
            else { PrimaryCausalLineageRecord parent = Parent(run, record.SameAxisParentId); Assert.That(record.Axis, Is.EqualTo(parent.Axis)); Assert.That(record.SameAxisEdge == "L" || record.SameAxisEdge == "R", Is.True); Assert.That(record.Path, Is.EqualTo(parent.Path + "/" + record.SameAxisEdge)); Assert.That(record.CrossAxisEdgeId, Is.EqualTo(parent.CrossAxisEdgeId)); AssertProvenance(record, parent); }
            if (record.Axis == "psi") AssertPsiRecord(record); else AssertPartitionRecord(run, record);
        }

        /// <summary>Checks monotonic cross-axis edges, their Psi owners, and each retained rule-node prefix.</summary>
        private static void AssertCrossAxisEdges(PrimaryCausalRun run)
        {
            int previous = 0;
            foreach (PrimaryCausalCrossAxisEdge edge in run.CrossAxisEdges)
            {
                Assert.That(edge.EdgeId, Is.GreaterThan(previous)); Assert.That(edge.RuleKind == "coarse3" || edge.RuleKind == "fine5", Is.True); Assert.That(edge.SourcePsiInvocationId, Is.GreaterThan(0)); Assert.That(Record(run, edge.SourcePsiInvocationId).Axis, Is.EqualTo("psi")); previous = edge.EdgeId;
                AssertCanonicalRuleNode(edge);
                if (!HasEarlierRuleEdge(run, edge)) AssertRuleNodePrefix(run, edge.SourcePsiInvocationId);
            }
        }

        /// <summary>Checks coarse-three then fine-five node indices in one Psi invocation's execution order.</summary>
        private static void AssertRuleNodePrefix(PrimaryCausalRun run, int sourcePsiInvocationId)
        {
            int coarse = 0; int fine = 0; bool sawFine = false;
            foreach (PrimaryCausalCrossAxisEdge edge in run.CrossAxisEdges)
            {
                if (edge.SourcePsiInvocationId != sourcePsiInvocationId) continue;
                if (edge.RuleKind == "coarse3") { Assert.That(sawFine, Is.False); Assert.That(edge.NodeIndex, Is.EqualTo(coarse++)); }
                else { sawFine = true; Assert.That(edge.NodeIndex, Is.EqualTo(fine++)); }
            }
            Assert.That(coarse, Is.InRange(0, 3)); Assert.That(fine, Is.InRange(0, 5)); if (fine > 0) Assert.That(coarse, Is.EqualTo(3));
            bool completed = false;
            foreach (PrimaryCausalCrossAxisEdge edge in run.CrossAxisEdges) if (edge.SourcePsiInvocationId == sourcePsiInvocationId && edge.CompletesRule) { Assert.That(completed, Is.False); Assert.That(edge.RuleKind, Is.EqualTo("fine5")); Assert.That(edge.NodeIndex, Is.EqualTo(4)); completed = true; }
            if (completed) { Assert.That(coarse, Is.EqualTo(3)); Assert.That(fine, Is.EqualTo(5)); }
        }

        /// <summary>Checks one retained rule node against its exact canonical Gauss-Legendre value.</summary>
        private static void AssertCanonicalRuleNode(PrimaryCausalCrossAxisEdge edge)
        {
            double[] nodes = edge.RuleKind == "coarse3" ? CanonicalPsiNodes3 : CanonicalPsiNodes5; Assert.That(edge.NodeIndex, Is.InRange(0, nodes.Length - 1)); Assert.That(Bits(edge.CanonicalNode), Is.EqualTo(Bits(nodes[edge.NodeIndex])));
        }

        /// <summary>Checks the explicit non-applicable provenance fields of a Psi invocation.</summary>
        private static void AssertPsiRecord(PrimaryCausalLineageRecord record)
        {
            Assert.That(record.CrossAxisEdgeId, Is.EqualTo(0)); Assert.That(record.PartitionLine, Is.EqualTo(-1)); Assert.That(record.PartitionIndex, Is.EqualTo(-1)); Assert.That(record.TransformIdentity, Is.EqualTo("psi")); Assert.That(record.LeftBoundary, Is.Null); Assert.That(record.RightBoundary, Is.Null);
        }

        /// <summary>Checks an eta partition root or descendant retains its edge, interval, labels, and transform exactly.</summary>
        private static void AssertPartitionRecord(PrimaryCausalRun run, PrimaryCausalLineageRecord record)
        {
            Assert.That(record.Axis == "eta" || record.Axis == "eta-x", Is.True); Assert.That(record.CrossAxisEdgeId, Is.GreaterThan(0)); Assert.That(Record(run, Edge(run, record.CrossAxisEdgeId).SourcePsiInvocationId).Axis, Is.EqualTo("psi")); Assert.That(record.PartitionLine, Is.GreaterThanOrEqualTo(0)); Assert.That(record.PartitionIndex, Is.GreaterThanOrEqualTo(0)); Assert.That(record.LeftBoundary, Is.Not.Empty); Assert.That(record.RightBoundary, Is.Not.Empty); Assert.That(record.TransformIdentity, Is.EqualTo(record.Axis)); Assert.That(record.OriginalEtaLeft, Is.LessThan(record.OriginalEtaRight));
            if (record.Axis == "eta") { Assert.That(Bits(record.TransformedLeft), Is.EqualTo(Bits(record.OriginalEtaLeft))); Assert.That(Bits(record.TransformedRight), Is.EqualTo(Bits(record.OriginalEtaRight))); return; }
            Assert.That(Bits(record.TransformedLeft), Is.EqualTo(Bits(Math.Sqrt(1.0d - record.OriginalEtaRight)))); Assert.That(Bits(record.TransformedRight), Is.EqualTo(Bits(Math.Sqrt(1.0d - record.OriginalEtaLeft))));
        }

        /// <summary>Checks all partition and transform provenance survives same-axis recursion unchanged.</summary>
        private static void AssertProvenance(PrimaryCausalLineageRecord child, PrimaryCausalLineageRecord parent)
        {
            Assert.That(child.PartitionLine, Is.EqualTo(parent.PartitionLine)); Assert.That(child.PartitionIndex, Is.EqualTo(parent.PartitionIndex)); Assert.That(Bits(child.OriginalEtaLeft), Is.EqualTo(Bits(parent.OriginalEtaLeft))); Assert.That(Bits(child.OriginalEtaRight), Is.EqualTo(Bits(parent.OriginalEtaRight))); Assert.That(Bits(child.TransformedLeft), Is.EqualTo(Bits(parent.TransformedLeft))); Assert.That(Bits(child.TransformedRight), Is.EqualTo(Bits(parent.TransformedRight))); Assert.That(child.LeftBoundary, Is.EqualTo(parent.LeftBoundary)); Assert.That(child.RightBoundary, Is.EqualTo(parent.RightBoundary)); Assert.That(child.TransformIdentity, Is.EqualTo(parent.TransformIdentity));
        }

        /// <summary>Checks terminal ancestry remains independently retained once the bounded prefix fills.</summary>
        private static void AssertTerminalRetention(PrimaryCausalRun run)
        {
            if (!run.CompleteResult.Value.TerminalEvidence.HasValue) return;
            Assert.That(run.TerminalAncestorChain.Count, Is.GreaterThan(0));
            for (int index = 0; index < run.TerminalAncestorChain.Count; index++) { AssertLineageRecord(run, run.TerminalAncestorChain[index]); if (index + 1 < run.TerminalAncestorChain.Count) AssertTerminalAncestorStep(run, run.TerminalAncestorChain[index], run.TerminalAncestorChain[index + 1]); }
            PrimaryCausalLineageRecord final = run.TerminalAncestorChain[run.TerminalAncestorChain.Count - 1]; Assert.That(final.Axis, Is.EqualTo("psi")); Assert.That(final.SameAxisParentId, Is.EqualTo(0)); Assert.That(final.CrossAxisEdgeId, Is.EqualTo(0)); Assert.That(final.Path, Is.EqualTo("root"));
        }

        /// <summary>Checks one terminal-to-root ancestry transition without relabeling a cross-axis call as a child edge.</summary>
        private static void AssertTerminalAncestorStep(PrimaryCausalRun run, PrimaryCausalLineageRecord current, PrimaryCausalLineageRecord next)
        {
            if (current.SameAxisParentId != 0) { Assert.That(next.InvocationId, Is.EqualTo(current.SameAxisParentId)); Assert.That(next.Axis, Is.EqualTo(current.Axis)); return; }
            Assert.That(current.Axis == "eta" || current.Axis == "eta-x", Is.True); Assert.That(current.CrossAxisEdgeId, Is.GreaterThan(0)); Assert.That(next.InvocationId, Is.EqualTo(Edge(run, current.CrossAxisEdgeId).SourcePsiInvocationId)); Assert.That(next.Axis, Is.EqualTo("psi"));
        }

        /// <summary>Checks that retained depth decisions evaluated panel, acceptance, then depth-cap conditions in order.</summary>
        private static void AssertDecisionOrder(PrimaryCausalTerminalEvidence terminal)
        {
            Assert.That(terminal.DecisionOrder.HasValue, Is.True); PrimaryCausalDecisionOrder order = terminal.DecisionOrder.Value; Assert.That(order.PanelCapOrder, Is.LessThan(order.AcceptanceOrder)); Assert.That(order.AcceptanceOrder, Is.LessThan(order.DepthCapOrder));
        }

        /// <summary>Checks observed output against the existing capture-free primary entry point.</summary>
        private static void AssertDirectParity(PrimaryCausalRun run)
        {
            Assert.That(run.DirectResult.HasValue, Is.True); DirectIsolationSnapshot before = CaptureDirectIsolation(); AdaptiveResult direct = RunDirect(run); DirectIsolationSnapshot after = CaptureDirectIsolation(); AssertDirectIsolation(before, after); PrimaryCausalCompleteResult result = run.CompleteResult.Value; PrimaryCausalDirectResult observed = run.DirectResult.Value;
            AssertDirectResult(observed, direct); AssertCompleteDirectResult(run); AssertIsolation(run);
        }

        /// <summary>Runs the existing primary entry point with the causal mode's independent reservation policy.</summary>
        private static AdaptiveResult RunDirect(PrimaryCausalRun run)
        {
            AdaptiveSettings settings = FrozenCalibrationASettings();
            if (run.Mode == PrimaryCausalMode.NoSelectionBudget) return AdaptivePrimary.Integrate(settings, run.Invocation.P, run.Invocation.NdotV, run.Invocation.SwitchBranch);
            var budget = new SelectionExecutionBudget(512); var coordinate = new AdaptiveCoordinate(run.Invocation.P, run.Invocation.NdotV); var context = new SelectionExecutionContext(settings.Name, "causal-direct-parity", run.Invocation.SwitchBranch ? "switch" : "normal", "causal", run.Invocation.CoordinateIndex, coordinate, "primary");
            return AdaptivePrimary.Integrate(settings, coordinate.P, coordinate.V, run.Invocation.SwitchBranch, budget, context);
        }

        /// <summary>Creates the exact frozen calibration-a settings without consulting the selection cache.</summary>
        private static AdaptiveSettings FrozenCalibrationASettings() => new AdaptiveSettings("calibration-a", 0.00004d, 0.0004d, 0.00001d, 0.0001d, 18, 65536, 1000000);

        /// <summary>Captures cache realization and exact artifact bytes around a direct primary comparison.</summary>
        private static DirectIsolationSnapshot CaptureDirectIsolation()
        {
            string path = AdaptiveProtocol.CanonicalArtifactPath; bool exists = File.Exists(path); return new DirectIsolationSnapshot(GetSelectionCache().IsValueCreated, exists, exists ? File.ReadAllBytes(path) : null);
        }

        /// <summary>Checks that a direct primary comparison did not realize the cache or mutate the artifact.</summary>
        private static void AssertDirectIsolation(DirectIsolationSnapshot before, DirectIsolationSnapshot after)
        {
            Assert.That(after.CacheCreated, Is.EqualTo(before.CacheCreated)); Assert.That(after.ArtifactExists, Is.EqualTo(before.ArtifactExists)); Assert.That(after.ArtifactBytes == null, Is.EqualTo(before.ArtifactBytes == null)); if (before.ArtifactBytes != null) CollectionAssert.AreEqual(before.ArtifactBytes, after.ArtifactBytes);
        }

        /// <summary>Checks the explicit cache and artifact snapshots did not change around observation.</summary>
        private static void AssertIsolation(PrimaryCausalRun run)
        {
            Assert.That(run.Availability, Is.EqualTo(PrimaryCausalAvailability.Available)); Assert.That(run.ObserverIsolationSnapshot.HasValue, Is.True); PrimaryCausalObserverIsolationSnapshot snapshot = run.ObserverIsolationSnapshot.Value;
            Assert.That(snapshot.IsObserved, Is.True); Assert.That(run.PreObserverStateDigest.HasValue, Is.True); Assert.That(run.PostObserverStateDigest.HasValue, Is.True); Assert.That(snapshot.PreCacheDigest.Value, Is.EqualTo(snapshot.PostCacheDigest.Value)); Assert.That(snapshot.PreArtifactDigest.Value, Is.EqualTo(snapshot.PostArtifactDigest.Value)); Assert.That(run.PreObserverStateDigest.Value, Is.EqualTo(run.PostObserverStateDigest.Value));
        }

        /// <summary>Checks every exact-bit attempt field within a shared mode-common range.</summary>
        private static void AssertAttemptRange(PrimaryCausalRun left, PrimaryCausalRun right, int count)
        {
            Assert.That(left.Attempts.Count, Is.GreaterThanOrEqualTo(count)); Assert.That(right.Attempts.Count, Is.GreaterThanOrEqualTo(count));
            for (int index = 0; index < count; index++) AssertAttempt(left.Attempts[index], right.Attempts[index]);
        }

        /// <summary>Checks one retained scalar core without numeric normalization.</summary>
        private static void AssertAttempt(PrimaryCausalAttemptCore left, PrimaryCausalAttemptCore right)
        {
            Assert.That(left.Sequence, Is.EqualTo(right.Sequence)); Assert.That(left.SwitchBranch, Is.EqualTo(right.SwitchBranch)); Assert.That(Bits(left.P), Is.EqualTo(Bits(right.P))); Assert.That(Bits(left.NdotV), Is.EqualTo(Bits(right.NdotV))); Assert.That(left.Axis, Is.EqualTo(right.Axis)); Assert.That(Bits(left.Psi), Is.EqualTo(Bits(right.Psi))); Assert.That(Bits(left.Eta), Is.EqualTo(Bits(right.Eta))); Assert.That(Bits(left.Sample), Is.EqualTo(Bits(right.Sample))); Assert.That(Bits(left.Left), Is.EqualTo(Bits(right.Left))); Assert.That(Bits(left.Right), Is.EqualTo(Bits(right.Right))); Assert.That(left.Depth, Is.EqualTo(right.Depth)); Assert.That(left.PartitionLine, Is.EqualTo(right.PartitionLine)); Assert.That(left.PartitionIndex, Is.EqualTo(right.PartitionIndex)); Assert.That(Bits(left.PreTransformEta), Is.EqualTo(Bits(right.PreTransformEta))); Assert.That(Bits(left.RawX), Is.EqualTo(Bits(right.RawX))); Assert.That(Bits(left.Jacobian), Is.EqualTo(Bits(right.Jacobian)));
        }

        /// <summary>Checks all retained outcome, lineage, edge, aggregate, witness, and trace fields.</summary>
        private static void AssertReservations(PrimaryCausalRun left, PrimaryCausalRun right)
        {
            Assert.That(left.Reservations.Count, Is.EqualTo(right.Reservations.Count));
            for (int index = 0; index < left.Reservations.Count; index++) { ReservationObservation first = left.Reservations[index]; ReservationObservation second = right.Reservations[index]; AssertReservation(first, second.Sequence, second.State, second.Used, second.Limit, second.Core); }
        }

        /// <summary>Checks the complete direct-entry result against either retained or newly executed evidence.</summary>
        private static void AssertDirectResult(PrimaryCausalDirectResult? left, PrimaryCausalDirectResult? right)
        {
            Assert.That(left.HasValue, Is.EqualTo(right.HasValue)); if (!left.HasValue) return; AssertDirectResult(left.Value, right.Value);
        }

        /// <summary>Checks two retained direct-entry results without numeric normalization.</summary>
        private static void AssertDirectResult(PrimaryCausalDirectResult left, PrimaryCausalDirectResult right)
        {
            Assert.That(Bits(left.Estimate), Is.EqualTo(Bits(right.Estimate))); Assert.That(Bits(left.Error), Is.EqualTo(Bits(right.Error))); Assert.That(Bits(left.Tolerance), Is.EqualTo(Bits(right.Tolerance))); Assert.That(left.Evaluations, Is.EqualTo(right.Evaluations)); Assert.That(left.Panels, Is.EqualTo(right.Panels)); Assert.That(left.Depth, Is.EqualTo(right.Depth)); Assert.That(left.Diagnostic, Is.EqualTo(right.Diagnostic));
        }

        /// <summary>Checks a retained direct result against the existing capture-free primary entry point.</summary>
        private static void AssertDirectResult(PrimaryCausalDirectResult observed, AdaptiveResult direct)
        {
            Assert.That(Bits(observed.Estimate), Is.EqualTo(Bits(direct.Value))); Assert.That(Bits(observed.Error), Is.EqualTo(Bits(direct.Error))); Assert.That(Bits(observed.Tolerance), Is.EqualTo(Bits(direct.Tolerance))); Assert.That(observed.Evaluations, Is.EqualTo(direct.Evaluations)); Assert.That(observed.Panels, Is.EqualTo(direct.Panels)); Assert.That(observed.Depth, Is.EqualTo(direct.Depth)); Assert.That(observed.Diagnostic, Is.EqualTo(direct.Diagnostic));
        }

        /// <summary>Checks every complete-result value against the retained direct result.</summary>
        private static void AssertCompleteDirectResult(PrimaryCausalRun run)
        {
            Assert.That(run.DirectResult.HasValue, Is.True); PrimaryCausalCompleteResult result = run.CompleteResult.Value; PrimaryCausalDirectResult direct = run.DirectResult.Value; Assert.That(Bits(result.Estimate), Is.EqualTo(Bits(direct.Estimate))); Assert.That(Bits(result.Error), Is.EqualTo(Bits(direct.Error))); Assert.That(Bits(result.Tolerance), Is.EqualTo(Bits(direct.Tolerance))); Assert.That(result.Evaluations, Is.EqualTo(direct.Evaluations)); Assert.That(result.Panels, Is.EqualTo(direct.Panels)); Assert.That(result.Depth, Is.EqualTo(direct.Depth)); Assert.That(result.Diagnostic, Is.EqualTo(direct.Diagnostic));
        }

        /// <summary>Checks two complete causal results without tolerances.</summary>
        private static void AssertResult(PrimaryCausalCompleteResult left, PrimaryCausalCompleteResult right)
        {
            Assert.That(left.TerminalState, Is.EqualTo(right.TerminalState)); Assert.That(left.Decision, Is.EqualTo(right.Decision)); Assert.That(left.StartedAttemptCount, Is.EqualTo(right.StartedAttemptCount)); Assert.That(Bits(left.Estimate), Is.EqualTo(Bits(right.Estimate))); Assert.That(Bits(left.Error), Is.EqualTo(Bits(right.Error))); Assert.That(Bits(left.Tolerance), Is.EqualTo(Bits(right.Tolerance))); Assert.That(left.Evaluations, Is.EqualTo(right.Evaluations)); Assert.That(left.Panels, Is.EqualTo(right.Panels)); Assert.That(left.Depth, Is.EqualTo(right.Depth)); Assert.That(left.Diagnostic, Is.EqualTo(right.Diagnostic)); Assert.That(left.TerminalEvidence.HasValue, Is.EqualTo(right.TerminalEvidence.HasValue)); if (left.TerminalEvidence.HasValue) AssertTerminalEvidence(left.TerminalEvidence.Value, right.TerminalEvidence.Value);
        }

        /// <summary>Checks terminal identity, category, decision, and arithmetic evidence exactly.</summary>
        private static void AssertTerminalEvidence(PrimaryCausalTerminalEvidence left, PrimaryCausalTerminalEvidence right)
        {
            Assert.That(left.Category, Is.EqualTo(right.Category)); Assert.That(left.Decision, Is.EqualTo(right.Decision)); AssertTerminal(left.Identity, right.Identity); AssertDepth(left.Arithmetic, right.Arithmetic); Assert.That(left.DecisionOrder.HasValue, Is.EqualTo(right.DecisionOrder.HasValue)); if (left.DecisionOrder.HasValue) { AssertDecisionOrder(left); AssertDecisionOrder(right); Assert.That(left.DecisionOrder.Value.PanelCapOrder, Is.EqualTo(right.DecisionOrder.Value.PanelCapOrder)); Assert.That(left.DecisionOrder.Value.AcceptanceOrder, Is.EqualTo(right.DecisionOrder.Value.AcceptanceOrder)); Assert.That(left.DecisionOrder.Value.DepthCapOrder, Is.EqualTo(right.DecisionOrder.Value.DepthCapOrder)); }
        }

        /// <summary>Checks one terminal identity without numeric normalization.</summary>
        private static void AssertTerminal(PrimaryCausalTerminalInvocation left, PrimaryCausalTerminalInvocation right)
        {
            Assert.That(left.Axis, Is.EqualTo(right.Axis)); Assert.That(left.HasOuter, Is.EqualTo(right.HasOuter)); Assert.That(Bits(left.Outer), Is.EqualTo(Bits(right.Outer))); Assert.That(Bits(left.Left), Is.EqualTo(Bits(right.Left))); Assert.That(Bits(left.Right), Is.EqualTo(Bits(right.Right))); Assert.That(left.Depth, Is.EqualTo(right.Depth));
        }

        /// <summary>Checks every retained observer-disabled terminal identity exactly.</summary>
        private static void AssertTerminals(PrimaryCausalRun left, PrimaryCausalRun right)
        {
            Assert.That(left.Terminals.Count, Is.EqualTo(right.Terminals.Count)); for (int index = 0; index < left.Terminals.Count; index++) AssertTerminal(left.Terminals[index], right.Terminals[index]);
        }

        /// <summary>Checks every retained terminal arithmetic field without a tolerance.</summary>
        private static void AssertDepth(PrimaryCausalDepthEvidence left, PrimaryCausalDepthEvidence right)
        {
            Assert.That(Bits(left.Coarse), Is.EqualTo(Bits(right.Coarse))); Assert.That(Bits(left.Fine), Is.EqualTo(Bits(right.Fine))); Assert.That(Bits(left.Inherited), Is.EqualTo(Bits(right.Inherited))); Assert.That(Bits(left.Delta), Is.EqualTo(Bits(right.Delta))); Assert.That(Bits(left.Absolute), Is.EqualTo(Bits(right.Absolute))); Assert.That(Bits(left.Relative), Is.EqualTo(Bits(right.Relative))); Assert.That(Bits(left.Error), Is.EqualTo(Bits(right.Error))); Assert.That(Bits(left.Limit), Is.EqualTo(Bits(right.Limit))); Assert.That(Bits(left.ErrorOverLimit), Is.EqualTo(Bits(right.ErrorOverLimit)));
        }

        /// <summary>Checks identical lineage, cross-axis edge, and aggregate arrays.</summary>
        private static void AssertLineageRange(PrimaryCausalRun left, PrimaryCausalRun right) => AssertLineageRange(left.Lineage, right.Lineage);
        /// <summary>Checks identical retained lineage records.</summary>
        private static void AssertLineageRange(System.Collections.Generic.IReadOnlyList<PrimaryCausalLineageRecord> left, System.Collections.Generic.IReadOnlyList<PrimaryCausalLineageRecord> right)
        {
            Assert.That(left.Count, Is.EqualTo(right.Count)); for (int index = 0; index < left.Count; index++) Assert.That(LineageText(left[index]), Is.EqualTo(LineageText(right[index])));
        }

        /// <summary>Builds a lossless binary64 lineage comparison representation.</summary>
        private static string LineageText(PrimaryCausalLineageRecord value) => value.InvocationId + "|" + value.SameAxisParentId + "|" + value.Path + "|" + value.Axis + "|" + Bits(value.Outer) + "|" + value.Depth + "|" + Bits(value.EntryEstimate) + "|" + Bits(value.ReturnEstimate) + "|" + Bits(value.AbsoluteAllocation) + "|" + Bits(value.RelativeShare) + "|" + value.Decision + "|" + value.ChildAggregation + "|" + value.SameAxisEdge + "|" + value.CrossAxisEdgeId + "|" + value.PartitionLine + "|" + value.PartitionIndex + "|" + Bits(value.OriginalEtaLeft) + "|" + Bits(value.OriginalEtaRight) + "|" + Bits(value.TransformedLeft) + "|" + Bits(value.TransformedRight) + "|" + value.LeftBoundary + "|" + value.RightBoundary + "|" + value.TransformIdentity;

        /// <summary>Checks identical explicit cross-axis edges and aggregate values.</summary>
        private static void AssertEdges(PrimaryCausalRun left, PrimaryCausalRun right)
        {
            Assert.That(left.CrossAxisEdges.Count, Is.EqualTo(right.CrossAxisEdges.Count)); for (int index = 0; index < left.CrossAxisEdges.Count; index++) { PrimaryCausalCrossAxisEdge first = left.CrossAxisEdges[index]; PrimaryCausalCrossAxisEdge second = right.CrossAxisEdges[index]; Assert.That(first.EdgeId, Is.EqualTo(second.EdgeId)); Assert.That(first.SourcePsiInvocationId, Is.EqualTo(second.SourcePsiInvocationId)); Assert.That(first.RuleKind, Is.EqualTo(second.RuleKind)); Assert.That(first.NodeIndex, Is.EqualTo(second.NodeIndex)); Assert.That(Bits(first.PsiNode), Is.EqualTo(Bits(second.PsiNode))); Assert.That(first.CompletesRule, Is.EqualTo(second.CompletesRule)); Assert.That(Bits(first.CanonicalNode), Is.EqualTo(Bits(second.CanonicalNode))); }
        }

        /// <summary>Checks identical partition aggregates and earliest-tie state.</summary>
        private static void AssertAggregates(PrimaryCausalRun left, PrimaryCausalRun right)
        {
            Assert.That(left.Aggregates.Count, Is.EqualTo(right.Aggregates.Count)); for (int index = 0; index < left.Aggregates.Count; index++) { PrimaryCausalAggregate first = left.Aggregates[index]; PrimaryCausalAggregate second = right.Aggregates[index]; Assert.That(first.Axis, Is.EqualTo(second.Axis)); Assert.That(first.PartitionLine, Is.EqualTo(second.PartitionLine)); Assert.That(first.PartitionIndex, Is.EqualTo(second.PartitionIndex)); Assert.That(first.Count, Is.EqualTo(second.Count)); Assert.That(Bits(first.Maximum), Is.EqualTo(Bits(second.Maximum))); Assert.That(first.MaximumSequence, Is.EqualTo(second.MaximumSequence)); Assert.That(first.RollingDigest, Is.EqualTo(second.RollingDigest)); }
        }

        /// <summary>Checks immutable parsed observer-disabled evidence exactly when both runs retain it.</summary>
        private static void AssertWitness(PrimaryCausalObserverDisabledWitness left, PrimaryCausalObserverDisabledWitness right)
        {
            Assert.That(left == null, Is.EqualTo(right == null)); if (left == null) return; Assert.That(left.RawDiagnostic, Is.EqualTo(right.RawDiagnostic)); Assert.That(left.Category, Is.EqualTo(right.Category)); Assert.That(left.Decision, Is.EqualTo(right.Decision)); AssertTerminal(left.Terminal, right.Terminal); AssertDepth(left.Arithmetic, right.Arithmetic); Assert.That(left.DecisionOrder.HasValue, Is.EqualTo(right.DecisionOrder.HasValue)); if (left.DecisionOrder.HasValue) { Assert.That(left.DecisionOrder.Value.PanelCapOrder, Is.EqualTo(right.DecisionOrder.Value.PanelCapOrder)); Assert.That(left.DecisionOrder.Value.AcceptanceOrder, Is.EqualTo(right.DecisionOrder.Value.AcceptanceOrder)); Assert.That(left.DecisionOrder.Value.DepthCapOrder, Is.EqualTo(right.DecisionOrder.Value.DepthCapOrder)); }
        }

        /// <summary>Checks exact observer-isolation snapshots when both modes retain them.</summary>
        private static void AssertIsolationSnapshot(PrimaryCausalObserverIsolationSnapshot? left, PrimaryCausalObserverIsolationSnapshot? right)
        {
            Assert.That(left.HasValue, Is.True); Assert.That(right.HasValue, Is.True); Assert.That(left.Value.IsObserved, Is.True); Assert.That(right.Value.IsObserved, Is.True); Assert.That(left.Value.PreCacheDigest.Value, Is.EqualTo(right.Value.PreCacheDigest.Value)); Assert.That(left.Value.PostCacheDigest.Value, Is.EqualTo(right.Value.PostCacheDigest.Value)); Assert.That(left.Value.PreArtifactDigest.Value, Is.EqualTo(right.Value.PreArtifactDigest.Value)); Assert.That(left.Value.PostArtifactDigest.Value, Is.EqualTo(right.Value.PostArtifactDigest.Value));
        }

        /// <summary>Checks the optional first contradiction trace exactly.</summary>
        private static void AssertTrace(PrimaryCausalContradictionTrace left, PrimaryCausalContradictionTrace right)
        {
            Assert.That(left == null, Is.EqualTo(right == null)); if (left == null) return; Assert.That(left.TerminalInvocationId, Is.EqualTo(right.TerminalInvocationId)); Assert.That(left.Reason, Is.EqualTo(right.Reason)); AssertLineageRange(left.Lineage, right.Lineage);
        }

        /// <summary>Gets a prior same-axis lineage record without accepting an absent parent.</summary>
        private static PrimaryCausalLineageRecord Parent(PrimaryCausalRun run, int id)
        {
            return Record(run, id);
        }

        /// <summary>Gets whether one explicit cross-axis edge exists.</summary>
        private static bool HasEdge(PrimaryCausalRun run, int id)
        {
            foreach (PrimaryCausalCrossAxisEdge edge in run.CrossAxisEdges) if (edge.EdgeId == id) return true; return false;
        }

        /// <summary>Gets one explicit cross-axis edge without accepting an absent retained edge.</summary>
        private static PrimaryCausalCrossAxisEdge Edge(PrimaryCausalRun run, int id)
        {
            foreach (PrimaryCausalCrossAxisEdge edge in run.CrossAxisEdges) if (edge.EdgeId == id) return edge; Assert.Fail("Missing cross-axis edge."); return default;
        }

        /// <summary>Gets one retained lineage record from the bounded prefix or terminal chain.</summary>
        private static PrimaryCausalLineageRecord Record(PrimaryCausalRun run, int id)
        {
            foreach (PrimaryCausalLineageRecord record in run.Lineage) if (record.InvocationId == id) return record;
            foreach (PrimaryCausalLineageRecord record in run.TerminalAncestorChain) if (record.InvocationId == id) return record;
            Assert.Fail("Missing retained lineage record."); return default;
        }

        /// <summary>Gets whether an earlier edge already began the same Psi rule-node sequence.</summary>
        private static bool HasEarlierRuleEdge(PrimaryCausalRun run, PrimaryCausalCrossAxisEdge edge)
        {
            foreach (PrimaryCausalCrossAxisEdge prior in run.CrossAxisEdges) if (prior.EdgeId < edge.EdgeId && prior.SourcePsiInvocationId == edge.SourcePsiInvocationId) return true; return false;
        }

        /// <summary>Checks exact frozen invocation identity.</summary>
        private static void AssertInvocation(PrimaryCausalInvocation left, PrimaryCausalInvocation right)
        {
            Assert.That(left.CoordinateIndex, Is.EqualTo(right.CoordinateIndex)); Assert.That(Bits(left.P), Is.EqualTo(Bits(right.P))); Assert.That(Bits(left.NdotV), Is.EqualTo(Bits(right.NdotV))); Assert.That(left.SwitchBranch, Is.EqualTo(right.SwitchBranch)); Assert.That(left.BaselineState, Is.EqualTo(right.BaselineState));
        }

        /// <summary>Gets an unsigned binary64 representation without numeric normalization.</summary>
        private static ulong Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value));

        /// <summary>Stores exact cache realization and artifact-byte state around one direct primary comparison.</summary>
        private readonly struct DirectIsolationSnapshot
        {
            /// <summary>Initializes an immutable direct-comparison isolation snapshot.</summary>
            internal DirectIsolationSnapshot(bool cacheCreated, bool artifactExists, byte[] artifactBytes) { CacheCreated = cacheCreated; ArtifactExists = artifactExists; ArtifactBytes = artifactBytes == null ? null : (byte[])artifactBytes.Clone(); }
            /// <summary>Gets whether the lazy selection cache had materialized.</summary>
            internal bool CacheCreated { get; }
            /// <summary>Gets whether the canonical artifact existed.</summary>
            internal bool ArtifactExists { get; }
            /// <summary>Gets a copied canonical artifact byte snapshot, or null when absent.</summary>
            internal byte[] ArtifactBytes { get; }
        }

        /// <summary>Gets the lazy selection cache without requesting its value.</summary>
        private static Lazy<AdaptiveSelection> GetSelectionCache()
        {
            FieldInfo field = typeof(PureBasePbrMultipleScatteringFurnaceOracle).GetField("SelectionCache", BindingFlags.NonPublic | BindingFlags.Static); Assert.That(field, Is.Not.Null); return (Lazy<AdaptiveSelection>)field.GetValue(null);
        }
    }
}
