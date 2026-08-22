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
}
