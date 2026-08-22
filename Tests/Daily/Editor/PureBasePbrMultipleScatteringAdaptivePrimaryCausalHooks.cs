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

// Holds causal observer notifications apart from the primary numerical implementation.

namespace PureBase.Tests.Daily
{
    /// <summary>Hosts optional causal observation without changing primary numerical decisions.</summary>
    internal static partial class AdaptivePrimary
    {
        /// <summary>Hosts null-safe causal observer adapters for one primary integration state.</summary>
        private sealed partial class State
        {
            private readonly PrimaryCausalObserver causalObserver;

            /// <summary>Records entry to one same-axis primary invocation when observation is enabled.</summary>
            internal void ObserveEntry(string axis, double outer, double left, double right, int depth) => causalObserver?.Enter(axis, outer, left, right, depth);

            /// <summary>Records the completed estimate of one same-axis primary invocation when enabled.</summary>
            internal void ObserveExit(double value) => causalObserver?.Exit(value);

            /// <summary>Records one canonical Psi rule node before its Eta invocation when enabled.</summary>
            internal void ObservePsiNode(string rule, int index, double psi, double canonical) => causalObserver?.BeginPsiNode(rule, index, psi, canonical);

            /// <summary>Records immutable Eta partition provenance before its root invocation when enabled.</summary>
            internal void ObservePartition(int line, int index, double left, double right, bool x, string leftLabel, string rightLabel) => causalObserver?.BeginPartition(line, index, left, right, x, leftLabel, rightLabel);

            /// <summary>Marks the next recursive entry as the current left same-axis child when enabled.</summary>
            internal void ObserveLeftChild() => causalObserver?.BeginChild("L");

            /// <summary>Marks the next recursive entry as the current right same-axis child when enabled.</summary>
            internal void ObserveRightChild() => causalObserver?.BeginChild("R");

            /// <summary>Records one pre-reservation scalar-kernel identity when enabled.</summary>
            internal int ObserveAttempt(string axis, double psi, double eta, double sample, double left, double right, int depth, double rawX, double jacobian) => causalObserver?.BeginAttempt(axis, psi, eta, sample, left, right, depth, rawX, jacobian) ?? 0;

            /// <summary>Records the independent reservation outcome for one attempt when enabled.</summary>
            internal void ObserveReservation(int sequence, bool hasBudget, bool accepted, int used, int limit) => causalObserver?.RecordReservation(sequence, hasBudget, accepted, used, limit);

            /// <summary>Records one scalar attempt that passed all pre-kernel checks when enabled.</summary>
            internal void ObserveStartedAttempt(int sequence) => causalObserver?.RecordStartedAttempt(sequence);

            /// <summary>Records one adaptive condition evaluation in the existing split order when enabled.</summary>
            internal void ObserveDecisionCondition(string condition) => causalObserver?.RecordDecisionCondition(condition);

            /// <summary>Records an adaptive decision after it was computed by the unchanged primary path.</summary>
            internal void ObserveDecision(string decision, string axis, double outer, double left, double right, int depth, AdaptiveEstimate coarse, AdaptiveEstimate fine, double delta, double absolute, double relative, double error, double limit) => causalObserver?.RecordDecision(decision, axis, outer, left, right, depth, coarse, fine, delta, absolute, relative, error, limit);
        }
    }
}
