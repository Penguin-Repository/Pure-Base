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

// Applies fail-closed repair authorization to independently retained terminal evidence.

using System;
using System.Globalization;

namespace PureBase.Tests.Daily
{
    /// <summary>Identifies an independently proven terminal contradiction eligible for later repair.</summary>
    internal enum PrimaryCausalTerminalContradiction
    {
        /// <summary>The terminal is internally consistent and merely did not converge before its cap.</summary>
        None,
        /// <summary>The retained terminal arithmetic contradicts its independently recomputed rule.</summary>
        Arithmetic,
        /// <summary>The retained terminal decision or cap ordering contradicts its observed control flow.</summary>
        DecisionControlFlow,
        /// <summary>The claimed contradiction has no defined classification.</summary>
        Undefined
    }

    /// <summary>Stores synthetic or observed facts consumed by the helper-independent repair gate.</summary>
    internal readonly struct PrimaryCausalGateEvidence
    {
        /// <summary>Initializes complete gate input facts without calling production helpers.</summary>
        internal PrimaryCausalGateEvidence(PrimaryCausalBaselineState state, bool validPrefix, PrimaryCausalTerminalEvidence? causalTerminal, PrimaryCausalObserverDisabledWitness observerDisabledWitness, bool routingOnly, bool childReturnOnly, bool observerOnly, bool capBeforeAcceptance, PrimaryCausalTerminalContradiction? terminalContradiction = null)
        {
            State = state; ValidPrefix = validPrefix; CausalTerminal = causalTerminal; ObserverDisabledWitness = observerDisabledWitness; RoutingOnly = routingOnly; ChildReturnOnly = childReturnOnly; ObserverOnly = observerOnly; CapBeforeAcceptance = capBeforeAcceptance; TerminalContradiction = terminalContradiction;
        }

        /// <summary>Gets the independently classified terminal state.</summary>
        internal PrimaryCausalBaselineState State { get; }
        /// <summary>Gets whether the attempt prefix is internally valid.</summary>
        internal bool ValidPrefix { get; }
        /// <summary>Gets complete causal terminal evidence, or null when it was not retained.</summary>
        internal PrimaryCausalTerminalEvidence? CausalTerminal { get; }
        /// <summary>Gets independently parsed observer-disabled evidence, or null when unavailable.</summary>
        internal PrimaryCausalObserverDisabledWitness ObserverDisabledWitness { get; }
        /// <summary>Gets whether the claim is routing-only suspicion.</summary>
        internal bool RoutingOnly { get; }
        /// <summary>Gets whether the claim uses child-return aggregation only.</summary>
        internal bool ChildReturnOnly { get; }
        /// <summary>Gets whether the claim uses observer-only contradiction.</summary>
        internal bool ObserverOnly { get; }
        /// <summary>Gets whether the depth cap precedes the acceptance decision.</summary>
        internal bool CapBeforeAcceptance { get; }
        /// <summary>Gets the claimed contradiction classification, or null for a complete normal terminal.</summary>
        internal PrimaryCausalTerminalContradiction? TerminalContradiction { get; }
    }

    /// <summary>Applies the independent fail-closed arithmetic and control-flow authorization rule.</summary>
    internal static class PrimaryCausalDecisionGate
    {
        /// <summary>Returns authorization only for complete, agreeing intrinsic evidence with a proven terminal contradiction.</summary>
        internal static PrimaryCausalGateResult Evaluate(PrimaryCausalGateEvidence evidence)
        {
            if (!HasFailClosedEligibility(evidence)) return PrimaryCausalGateResult.Reject;
            PrimaryCausalTerminalEvidence causal = evidence.CausalTerminal.Value; PrimaryCausalObserverDisabledWitness witness = evidence.ObserverDisabledWitness;
            if (!HasExactEvidenceConsistency(causal, witness)) return PrimaryCausalGateResult.Reject;
            ArithmeticMismatch causalArithmetic = GetArithmeticMismatch(causal.Arithmetic); ArithmeticMismatch witnessArithmetic = GetArithmeticMismatch(witness.Arithmetic); DecisionMismatch causalDecision = GetDecisionMismatch(causal.Arithmetic); DecisionMismatch witnessDecision = GetDecisionMismatch(witness.Arithmetic);
            if (!HasMatchingRecomputation(causalArithmetic, witnessArithmetic, causalDecision, witnessDecision)) return PrimaryCausalGateResult.Reject;
            return AuthorizeConcreteContradiction(evidence.TerminalContradiction, causalArithmetic, causalDecision);
        }

        /// <summary>Rejects incomplete, indirect, or non-depth-cap inputs before accessing retained evidence.</summary>
        private static bool HasFailClosedEligibility(PrimaryCausalGateEvidence evidence)
        {
            return evidence.State == PrimaryCausalBaselineState.DepthCap && evidence.ValidPrefix && evidence.CausalTerminal.HasValue && evidence.ObserverDisabledWitness != null && !evidence.RoutingOnly && !evidence.ChildReturnOnly && !evidence.ObserverOnly && evidence.CapBeforeAcceptance;
        }

        /// <summary>Requires both independent terminals to retain one exact complete depth-cap record.</summary>
        private static bool HasExactEvidenceConsistency(PrimaryCausalTerminalEvidence causal, PrimaryCausalObserverDisabledWitness witness)
        {
            if (causal.Category != PrimaryCausalBaselineState.DepthCap || witness.Category != PrimaryCausalBaselineState.DepthCap || string.IsNullOrEmpty(witness.RawDiagnostic) || !SameTerminal(causal.Identity, witness.Terminal)) return false;
            if (!HasCompleteDepthCapEvidence(causal.Arithmetic, causal.Decision) || !HasCompleteDepthCapEvidence(witness.Arithmetic, witness.Decision)) return false;
            return SameDepth(causal.Arithmetic, witness.Arithmetic);
        }

        /// <summary>Requires finite intrinsic arithmetic and the depth-cap decision emitted by the observer-disabled grammar.</summary>
        private static bool HasCompleteDepthCapEvidence(PrimaryCausalDepthEvidence arithmetic, string decision)
        {
            return HasFiniteDepthCapFacts(arithmetic) && decision == "depth-cap";
        }

        /// <summary>Requires both independent recomputations to identify the same exact mismatch types.</summary>
        private static bool HasMatchingRecomputation(ArithmeticMismatch causalArithmetic, ArithmeticMismatch witnessArithmetic, DecisionMismatch causalDecision, DecisionMismatch witnessDecision)
        {
            return causalArithmetic == witnessArithmetic && causalDecision == witnessDecision;
        }

        /// <summary>Authorizes only one shared and explicitly classified concrete terminal contradiction.</summary>
        private static PrimaryCausalGateResult AuthorizeConcreteContradiction(PrimaryCausalTerminalContradiction? contradiction, ArithmeticMismatch arithmetic, DecisionMismatch decision)
        {
            if (!contradiction.HasValue) return arithmetic == ArithmeticMismatch.None && decision == DecisionMismatch.None ? PrimaryCausalGateResult.NoRepair : PrimaryCausalGateResult.Reject;
            if (contradiction == PrimaryCausalTerminalContradiction.Arithmetic) return arithmetic != ArithmeticMismatch.None && decision == DecisionMismatch.None ? PrimaryCausalGateResult.AuthorizeTerminalSplitRepair : PrimaryCausalGateResult.Reject;
            if (contradiction == PrimaryCausalTerminalContradiction.DecisionControlFlow) return arithmetic == ArithmeticMismatch.None && decision != DecisionMismatch.None ? PrimaryCausalGateResult.AuthorizeTerminalSplitRepair : PrimaryCausalGateResult.Reject;
            return PrimaryCausalGateResult.Reject;
        }

        /// <summary>Identifies an exact arithmetic recomputation failure from retained raw values.</summary>
        private enum ArithmeticMismatch
        {
            /// <summary>No arithmetic mismatch was found.</summary>
            None,
            /// <summary>The retained error does not match its recomputation.</summary>
            Error,
            /// <summary>The retained limit does not match its recomputation.</summary>
            Limit,
            /// <summary>The retained ratio does not match its recomputation.</summary>
            ErrorOverLimit
        }

        /// <summary>Identifies an exact decision-order failure from retained control-flow facts.</summary>
        private enum DecisionMismatch
        {
            /// <summary>No decision-order mismatch was found.</summary>
            None,
            /// <summary>A depth-cap decision contradicts finite error-at-or-below-limit arithmetic.</summary>
            DepthCapAtOrBelowLimit
        }

        /// <summary>Checks all finite raw arithmetic facts required by a depth terminal.</summary>
        private static bool HasFiniteDepthCapFacts(PrimaryCausalDepthEvidence value) => Finite(value);

        /// <summary>Identifies the first exact arithmetic recomputation mismatch.</summary>
        private static ArithmeticMismatch GetArithmeticMismatch(PrimaryCausalDepthEvidence value)
        {
            if (!SameBits(value.Error, value.Inherited + value.Delta)) return ArithmeticMismatch.Error;
            if (!SameBits(value.Limit, value.Absolute + value.Relative)) return ArithmeticMismatch.Limit;
            return SameBits(value.ErrorOverLimit, value.Error / value.Limit) ? ArithmeticMismatch.None : ArithmeticMismatch.ErrorOverLimit;
        }

        /// <summary>Identifies the concrete depth-cap contradiction present in both independent arithmetic records.</summary>
        private static DecisionMismatch GetDecisionMismatch(PrimaryCausalDepthEvidence value) => value.Error <= value.Limit ? DecisionMismatch.DepthCapAtOrBelowLimit : DecisionMismatch.None;

        /// <summary>Compares every exact terminal identity field without numeric normalization.</summary>
        private static bool SameTerminal(PrimaryCausalTerminalInvocation left, PrimaryCausalTerminalInvocation right)
        {
            return left.Axis == right.Axis && left.HasOuter == right.HasOuter && (!left.HasOuter || SameBits(left.Outer, right.Outer)) && SameBits(left.Left, right.Left) && SameBits(left.Right, right.Right) && left.Depth == right.Depth;
        }

        /// <summary>Compares every parsed arithmetic field without a tolerance.</summary>
        private static bool SameDepth(PrimaryCausalDepthEvidence left, PrimaryCausalDepthEvidence right)
        {
            return SameBits(left.Coarse, right.Coarse) && SameBits(left.Fine, right.Fine) && SameBits(left.Inherited, right.Inherited) && SameBits(left.Delta, right.Delta) && SameBits(left.Absolute, right.Absolute) && SameBits(left.Relative, right.Relative) && SameBits(left.Error, right.Error) && SameBits(left.Limit, right.Limit) && SameBits(left.ErrorOverLimit, right.ErrorOverLimit);
        }

        /// <summary>Requires every arithmetic value used by the gate to be finite.</summary>
        private static bool Finite(PrimaryCausalDepthEvidence value)
        {
            return IsFinite(value.Coarse) && IsFinite(value.Fine) && IsFinite(value.Inherited) && IsFinite(value.Delta) && IsFinite(value.Absolute) && IsFinite(value.Relative) && IsFinite(value.Error) && IsFinite(value.Limit) && IsFinite(value.ErrorOverLimit);
        }

        /// <summary>Gets whether one binary64 value is finite.</summary>
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Compares binary64 results without a tolerance.</summary>
        private static bool SameBits(double left, double right) => BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
    }

    /// <summary>Classifies one bounded paired diagnostic decision without applying a numerical repair.</summary>
    internal enum PrimaryCausalDecisionClassification
    {
        /// <summary>The paired evidence is complete and does not require a local repair.</summary>
        NoRepair,
        /// <summary>The evidence is contradictory or ineligible and must fail closed.</summary>
        Reject,
        /// <summary>The required paired evidence was not available.</summary>
        Inconclusive,
        /// <summary>The gate found a concrete contradiction for external review only.</summary>
        ExternalReviewOnly
    }

    /// <summary>Stores one immutable bounded decision for one finite and null-budget invocation pair.</summary>
    internal readonly struct PrimaryCausalDecisionRecord
    {
        /// <summary>Initializes every retained fact for one deterministic paired decision.</summary>
        internal PrimaryCausalDecisionRecord(PrimaryCausalInvocation invocation, PrimaryCausalBaselineState finiteState, PrimaryCausalBaselineState unrestrictedState, bool finitePrefixAvailable, bool unrestrictedPrefixAvailable, bool finiteTerminalAvailable, bool unrestrictedTerminalAvailable, bool rawTerminalMatch, PrimaryCausalGateResult gateResult, PrimaryCausalDecisionClassification classification, string reason)
        {
            Invocation = invocation; FiniteState = finiteState; UnrestrictedState = unrestrictedState; FinitePrefixAvailable = finitePrefixAvailable; UnrestrictedPrefixAvailable = unrestrictedPrefixAvailable; FiniteTerminalAvailable = finiteTerminalAvailable; UnrestrictedTerminalAvailable = unrestrictedTerminalAvailable; RawTerminalMatch = rawTerminalMatch; GateResult = gateResult; Classification = classification; Reason = reason;
        }

        /// <summary>Gets the exact input identity shared by both observed modes.</summary>
        internal PrimaryCausalInvocation Invocation { get; }
        /// <summary>Gets the finite-budget observed terminal category.</summary>
        internal PrimaryCausalBaselineState FiniteState { get; }
        /// <summary>Gets the null-budget observed terminal category.</summary>
        internal PrimaryCausalBaselineState UnrestrictedState { get; }
        /// <summary>Gets whether finite mode retained its raw bounded prefix.</summary>
        internal bool FinitePrefixAvailable { get; }
        /// <summary>Gets whether null-budget mode retained its raw bounded prefix.</summary>
        internal bool UnrestrictedPrefixAvailable { get; }
        /// <summary>Gets whether finite mode retained terminal evidence.</summary>
        internal bool FiniteTerminalAvailable { get; }
        /// <summary>Gets whether null-budget mode retained terminal evidence.</summary>
        internal bool UnrestrictedTerminalAvailable { get; }
        /// <summary>Gets whether both raw observer-disabled depth terminals matched exactly once.</summary>
        internal bool RawTerminalMatch { get; }
        /// <summary>Gets the independent terminal gate result.</summary>
        internal PrimaryCausalGateResult GateResult { get; }
        /// <summary>Gets the final non-mutating diagnostic classification.</summary>
        internal PrimaryCausalDecisionClassification Classification { get; }
        /// <summary>Gets the deterministic reason for the final classification.</summary>
        internal string Reason { get; }
    }

    /// <summary>Builds bounded pair decisions only from retained prefixes and exact terminal evidence.</summary>
    internal static class PrimaryCausalDecisionDiagnostics
    {
        /// <summary>Evaluates a retained finite and null-budget pair using the unrestricted diagnostic.</summary>
        internal static PrimaryCausalDecisionRecord Evaluate(PrimaryCausalRun finite, PrimaryCausalRun unrestricted)
        {
            return Evaluate(finite, unrestricted, unrestricted?.ObserverDisabledWitness?.RawDiagnostic, false, false);
        }

        /// <summary>Evaluates a retained pair with an explicitly supplied observer-disabled diagnostic.</summary>
        internal static PrimaryCausalDecisionRecord Evaluate(PrimaryCausalRun finite, PrimaryCausalRun unrestricted, string rawDiagnostic, bool routingOnly, bool childReturnOnly)
        {
            bool finitePrefix = HasPrefix(finite); bool unrestrictedPrefix = HasPrefix(unrestricted); bool finiteTerminal = HasTerminal(finite); bool unrestrictedTerminal = HasTerminal(unrestricted);
            bool sameInvocation = SameInvocation(finite, unrestricted); bool depthCandidate = unrestricted != null && unrestricted.TerminalState == PrimaryCausalBaselineState.DepthCap;
            bool rawMatch = HasRawTerminalPairMatch(finite, unrestricted, rawDiagnostic);
            if (!sameInvocation) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, PrimaryCausalGateResult.Reject, PrimaryCausalDecisionClassification.Reject, "invocation-mismatch");
            if (depthCandidate && !rawMatch) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, false, PrimaryCausalGateResult.Reject, PrimaryCausalDecisionClassification.Reject, "observer-disabled-terminal-mismatch");
            if (!HasCompletePair(finite, unrestricted)) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, PrimaryCausalGateResult.Reject, PrimaryCausalDecisionClassification.Inconclusive, "paired-evidence-unavailable");
            if (!finitePrefix || !unrestrictedPrefix) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, PrimaryCausalGateResult.Reject, PrimaryCausalDecisionClassification.Reject, "retained-prefix-unavailable");
            if (HasFaultOrTimeout(finite) || HasFaultOrTimeout(unrestricted)) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, PrimaryCausalGateResult.Reject, PrimaryCausalDecisionClassification.Reject, "fault-or-timeout");
            if (!HasFiniteNumericalResults(finite, unrestricted)) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, PrimaryCausalGateResult.Reject, PrimaryCausalDecisionClassification.Reject, "non-finite-complete-result");
            if (finite.TerminalState == PrimaryCausalBaselineState.BudgetExhausted && unrestricted.TerminalState == PrimaryCausalBaselineState.Accepted) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, false, PrimaryCausalGateResult.NoRepair, PrimaryCausalDecisionClassification.NoRepair, "finite-budget-masked-by-null-accepted");
            if (finite.TerminalState == PrimaryCausalBaselineState.Accepted && unrestricted.TerminalState == PrimaryCausalBaselineState.Accepted && SameCompleteResult(finite, unrestricted)) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, false, PrimaryCausalGateResult.NoRepair, PrimaryCausalDecisionClassification.NoRepair, "accepted-exact-equality");
            if ((finite.TerminalState == PrimaryCausalBaselineState.BudgetExhausted || finite.TerminalState == PrimaryCausalBaselineState.DepthCap) && unrestricted.TerminalState == PrimaryCausalBaselineState.DepthCap) return EvaluateDepth(finite, unrestricted, rawDiagnostic, routingOnly, childReturnOnly, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal);
            return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, PrimaryCausalGateResult.Reject, PrimaryCausalDecisionClassification.Reject, "terminal-outcome-ineligible");
        }

        /// <summary>Evaluates an eligible depth-cap pair against its retained terminal evidence.</summary>
        private static PrimaryCausalDecisionRecord EvaluateDepth(PrimaryCausalRun finite, PrimaryCausalRun unrestricted, string rawDiagnostic, bool routingOnly, bool childReturnOnly, bool finitePrefix, bool unrestrictedPrefix, bool finiteTerminal, bool unrestrictedTerminal)
        {
            bool rawMatch = HasRawTerminalPairMatch(finite, unrestricted, rawDiagnostic);
            if (finite.TerminalState == PrimaryCausalBaselineState.DepthCap && !HasMatchingDepthPair(finite, unrestricted, rawDiagnostic, finiteTerminal, unrestrictedTerminal)) return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, PrimaryCausalGateResult.Reject, PrimaryCausalDecisionClassification.Reject, "paired-depth-evidence-mismatch");
            PrimaryCausalGateResult gate = EvaluateDepthGate(unrestricted, rawDiagnostic, finitePrefix && unrestrictedPrefix, routingOnly, childReturnOnly);
            if (finite.TerminalState == PrimaryCausalBaselineState.DepthCap && EvaluateDepthGate(finite, finite.ObserverDisabledWitness?.RawDiagnostic, true, false, false) != PrimaryCausalGateResult.NoRepair) gate = PrimaryCausalGateResult.Reject;
            PrimaryCausalDecisionClassification classification = gate == PrimaryCausalGateResult.NoRepair ? PrimaryCausalDecisionClassification.NoRepair : gate == PrimaryCausalGateResult.AuthorizeTerminalSplitRepair ? PrimaryCausalDecisionClassification.ExternalReviewOnly : PrimaryCausalDecisionClassification.Reject;
            string reason = gate == PrimaryCausalGateResult.NoRepair ? "independent-depth-evidence-no-repair" : gate == PrimaryCausalGateResult.AuthorizeTerminalSplitRepair ? "independent-depth-contradiction-external-review-only" : "independent-depth-evidence-rejected";
            return Create(finite, unrestricted, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, gate, classification, reason);
        }

        /// <summary>Requires both depth runs to retain one matching terminal, witness, digest, and complete result.</summary>
        private static bool HasMatchingDepthPair(PrimaryCausalRun finite, PrimaryCausalRun unrestricted, string unrestrictedDiagnostic, bool finiteTerminal, bool unrestrictedTerminal)
        {
            return finiteTerminal && unrestrictedTerminal && PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(finite.ObserverDisabledWitness?.RawDiagnostic, finite, out _) && PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(unrestrictedDiagnostic, unrestricted, out _) && finite.ModeCommonCoreDigest == unrestricted.ModeCommonCoreDigest && SameCompleteResult(finite, unrestricted);
        }

        /// <summary>Requires both pair sides to retain exactly one terminal matching their own observer-disabled diagnostic.</summary>
        private static bool HasRawTerminalPairMatch(PrimaryCausalRun finite, PrimaryCausalRun unrestricted, string unrestrictedDiagnostic)
        {
            return unrestricted != null && unrestricted.TerminalState == PrimaryCausalBaselineState.DepthCap && PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(finite?.ObserverDisabledWitness?.RawDiagnostic, finite, out _) && PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(unrestrictedDiagnostic, unrestricted, out _);
        }

        /// <summary>Evaluates one complete depth terminal against its independently captured observer-disabled witness.</summary>
        private static PrimaryCausalGateResult EvaluateDepthGate(PrimaryCausalRun run, string rawDiagnostic, bool validPrefix, bool routingOnly, bool childReturnOnly)
        {
            PrimaryCausalTerminalEvidence? causal = run.CompleteResult.Value.TerminalEvidence;
            bool parsed = PrimaryCausalDiagnosticParser.TryParseObserverDisabledDepth(rawDiagnostic, out PrimaryCausalObserverDisabledWitness witness);
            bool capOrder = causal.HasValue && HasCapOrder(causal.Value);
            var evidence = new PrimaryCausalGateEvidence(run.TerminalState, validPrefix, causal, parsed ? witness : null, routingOnly, childReturnOnly, false, capOrder);
            return PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(rawDiagnostic, run, out _) ? PrimaryCausalDecisionGate.Evaluate(evidence) : PrimaryCausalGateResult.Reject;
        }

        /// <summary>Creates one decision record from the retained pair evidence and classification.</summary>
        private static PrimaryCausalDecisionRecord Create(PrimaryCausalRun finite, PrimaryCausalRun unrestricted, bool finitePrefix, bool unrestrictedPrefix, bool finiteTerminal, bool unrestrictedTerminal, bool rawMatch, PrimaryCausalGateResult gate, PrimaryCausalDecisionClassification classification, string reason)
        {
            PrimaryCausalInvocation invocation = finite != null ? finite.Invocation : unrestricted != null ? unrestricted.Invocation : default;
            return new PrimaryCausalDecisionRecord(invocation, finite == null ? PrimaryCausalBaselineState.Other : finite.TerminalState, unrestricted == null ? PrimaryCausalBaselineState.Other : unrestricted.TerminalState, finitePrefix, unrestrictedPrefix, finiteTerminal, unrestrictedTerminal, rawMatch, gate, classification, reason);
        }

        /// <summary>Gets whether a run retains an available non-empty bounded prefix.</summary>
        private static bool HasPrefix(PrimaryCausalRun run) => run != null && run.Availability == PrimaryCausalAvailability.Available && run.CompleteResult.HasValue && run.ModeCommonCoreDigest != 0UL && run.Attempts.Count > 0;
        /// <summary>Gets whether a run retains complete terminal evidence.</summary>
        private static bool HasTerminal(PrimaryCausalRun run) => run != null && run.CompleteResult.HasValue && run.CompleteResult.Value.TerminalEvidence.HasValue && run.Terminals.Count > 0;
        /// <summary>Gets whether both runs retain available complete results.</summary>
        private static bool HasCompletePair(PrimaryCausalRun finite, PrimaryCausalRun unrestricted) => finite != null && unrestricted != null && finite.Availability == PrimaryCausalAvailability.Available && unrestricted.Availability == PrimaryCausalAvailability.Available && finite.CompleteResult.HasValue && unrestricted.CompleteResult.HasValue;
        /// <summary>Gets whether a run terminated with a fault or timeout.</summary>
        private static bool HasFaultOrTimeout(PrimaryCausalRun run) => run.TerminalState == PrimaryCausalBaselineState.Fault || run.TerminalState == PrimaryCausalBaselineState.Timeout;
        /// <summary>Requires both complete results to expose finite numerical evidence before NoRepair.</summary>
        private static bool HasFiniteNumericalResults(PrimaryCausalRun finite, PrimaryCausalRun unrestricted) => HasFiniteNumericalResult(finite.CompleteResult.Value) && HasFiniteNumericalResult(unrestricted.CompleteResult.Value);
        /// <summary>Requires all retained top-level numerical fields and terminal identity and arithmetic facts to be finite.</summary>
        private static bool HasFiniteNumericalResult(PrimaryCausalCompleteResult result) => IsFinite(result.Estimate) && IsFinite(result.Error) && IsFinite(result.Tolerance) && (!result.TerminalEvidence.HasValue || HasFiniteTerminalEvidence(result.TerminalEvidence.Value));
        /// <summary>Requires every terminal identity coordinate and arithmetic field to be finite.</summary>
        private static bool HasFiniteTerminalEvidence(PrimaryCausalTerminalEvidence terminal) => IsFinite(terminal.Identity.Left) && IsFinite(terminal.Identity.Right) && (!terminal.Identity.HasOuter || IsFinite(terminal.Identity.Outer)) && HasFiniteTerminalArithmetic(terminal);
        /// <summary>Requires every depth arithmetic field retained by a terminal evidence record to be finite.</summary>
        private static bool HasFiniteTerminalArithmetic(PrimaryCausalTerminalEvidence terminal)
        {
            PrimaryCausalDepthEvidence value = terminal.Arithmetic;
            return IsFinite(value.Coarse) && IsFinite(value.Fine) && IsFinite(value.Inherited) && IsFinite(value.Delta) && IsFinite(value.Absolute) && IsFinite(value.Relative) && IsFinite(value.Error) && IsFinite(value.Limit) && IsFinite(value.ErrorOverLimit);
        }
        /// <summary>Gets whether the observed panel-cap, acceptance, and depth-cap order is valid.</summary>
        private static bool HasCapOrder(PrimaryCausalTerminalEvidence terminal)
        {
            if (!terminal.DecisionOrder.HasValue) return false;
            PrimaryCausalDecisionOrder order = terminal.DecisionOrder.Value;
            return order.PanelCapOrder < order.AcceptanceOrder && order.AcceptanceOrder < order.DepthCapOrder;
        }
        /// <summary>Compares every retained complete-result field exactly.</summary>
        private static bool SameCompleteResult(PrimaryCausalRun finite, PrimaryCausalRun unrestricted)
        {
            PrimaryCausalCompleteResult left = finite.CompleteResult.Value; PrimaryCausalCompleteResult right = unrestricted.CompleteResult.Value;
            return left.TerminalState == right.TerminalState && left.Decision == right.Decision && left.StartedAttemptCount == right.StartedAttemptCount && SameBits(left.Estimate, right.Estimate) && SameBits(left.Error, right.Error) && SameTerminalEvidence(left.TerminalEvidence, right.TerminalEvidence) && SameBits(left.Tolerance, right.Tolerance) && left.Evaluations == right.Evaluations && left.Panels == right.Panels && left.Depth == right.Depth && left.Diagnostic == right.Diagnostic;
        }
        /// <summary>Compares optional terminal evidence without normalizing any retained binary64 field.</summary>
        private static bool SameTerminalEvidence(PrimaryCausalTerminalEvidence? left, PrimaryCausalTerminalEvidence? right)
        {
            if (left.HasValue != right.HasValue) return false;
            if (!left.HasValue) return true;
            PrimaryCausalTerminalEvidence first = left.Value; PrimaryCausalTerminalEvidence second = right.Value;
            return first.Identity.Axis == second.Identity.Axis && first.Identity.HasOuter == second.Identity.HasOuter && (!first.Identity.HasOuter || SameBits(first.Identity.Outer, second.Identity.Outer)) && SameBits(first.Identity.Left, second.Identity.Left) && SameBits(first.Identity.Right, second.Identity.Right) && first.Identity.Depth == second.Identity.Depth && first.Category == second.Category && first.Decision == second.Decision && SameDepth(first.Arithmetic, second.Arithmetic) && SameDecisionOrder(first.DecisionOrder, second.DecisionOrder);
        }
        /// <summary>Compares all retained depth arithmetic fields bitwise.</summary>
        private static bool SameDepth(PrimaryCausalDepthEvidence left, PrimaryCausalDepthEvidence right) => SameBits(left.Coarse, right.Coarse) && SameBits(left.Fine, right.Fine) && SameBits(left.Inherited, right.Inherited) && SameBits(left.Delta, right.Delta) && SameBits(left.Absolute, right.Absolute) && SameBits(left.Relative, right.Relative) && SameBits(left.Error, right.Error) && SameBits(left.Limit, right.Limit) && SameBits(left.ErrorOverLimit, right.ErrorOverLimit);
        /// <summary>Compares optional terminal decision-order records exactly.</summary>
        private static bool SameDecisionOrder(PrimaryCausalDecisionOrder? left, PrimaryCausalDecisionOrder? right) => left.HasValue == right.HasValue && (!left.HasValue || (left.Value.PanelCapOrder == right.Value.PanelCapOrder && left.Value.AcceptanceOrder == right.Value.AcceptanceOrder && left.Value.DepthCapOrder == right.Value.DepthCapOrder));
        /// <summary>Compares paired invocation identities with exact binary64 fields.</summary>
        private static bool SameInvocation(PrimaryCausalRun finite, PrimaryCausalRun unrestricted)
        {
            if (finite == null || unrestricted == null) return false;
            PrimaryCausalInvocation left = finite.Invocation; PrimaryCausalInvocation right = unrestricted.Invocation;
            return left.CoordinateIndex == right.CoordinateIndex && SameBits(left.P, right.P) && SameBits(left.NdotV, right.NdotV) && left.SwitchBranch == right.SwitchBranch && left.BaselineState == right.BaselineState;
        }
        /// <summary>Compares binary64 values by their exact bit patterns.</summary>
        private static bool SameBits(double left, double right) => BitConverter.DoubleToInt64Bits(left) == BitConverter.DoubleToInt64Bits(right);
        /// <summary>Gets whether a binary64 value is neither NaN nor infinite.</summary>
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Renders every bounded pair decision with exact invocation identities and no host data.</summary>
    internal static class PrimaryCausalDecisionRenderer
    {
        /// <summary>Renders one bounded pair decision as deterministic diagnostic text.</summary>
        internal static string Render(PrimaryCausalDecisionRecord value)
        {
            PrimaryCausalInvocation invocation = value.Invocation;
            return "index=" + invocation.CoordinateIndex.ToString(CultureInfo.InvariantCulture) + " p=0x" + Bits(invocation.P) + " v=0x" + Bits(invocation.NdotV) + " branch=" + invocation.SwitchBranch + " expected=" + invocation.BaselineState + " finite=" + value.FiniteState + " null=" + value.UnrestrictedState + " finitePrefix=" + value.FinitePrefixAvailable + " nullPrefix=" + value.UnrestrictedPrefixAvailable + " finiteTerminal=" + value.FiniteTerminalAvailable + " nullTerminal=" + value.UnrestrictedTerminalAvailable + " rawTerminalMatch=" + value.RawTerminalMatch + " gate=" + value.GateResult + " classification=" + value.Classification + " reason=" + value.Reason;
        }
        /// <summary>Formats one binary64 value as a fixed-width hexadecimal bit pattern.</summary>
        private static string Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("X16", CultureInfo.InvariantCulture);
    }
}
