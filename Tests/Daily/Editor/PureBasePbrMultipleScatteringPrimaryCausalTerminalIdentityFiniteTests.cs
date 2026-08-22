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

// Verifies that nonfinite terminal identities fail closed before NoRepair classification.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Tests finite terminal identity requirements and paired raw-terminal match reporting.</summary>
    public sealed class PureBasePbrMultipleScatteringPrimaryCausalTerminalIdentityFiniteTests
    {
        /// <summary>Rejects identical nonfinite terminal identities on a BudgetExhausted-to-Accepted pair.</summary>
        [Test]
        public void PrimaryCausalDecisionRejectsNonfiniteTerminalIdentityForBudgetToAccepted()
        {
            DecisionPair(PrimaryCausalBaselineState.BudgetExhausted, out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            AssertRejectsEveryNonfiniteIdentity(finite, unrestricted);
        }

        /// <summary>Rejects identical nonfinite terminal identities on an Accepted-to-Accepted pair.</summary>
        [Test]
        public void PrimaryCausalDecisionRejectsNonfiniteTerminalIdentityForAcceptedToAccepted()
        {
            DecisionPair(PrimaryCausalBaselineState.Accepted, out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            AssertRejectsEveryNonfiniteIdentity(finite, unrestricted);
        }

        /// <summary>Rejects identical nonfinite terminal identities on a DepthCap-to-DepthCap pair.</summary>
        [Test]
        public void PrimaryCausalDecisionRejectsNonfiniteTerminalIdentityForDepthCapToDepthCap()
        {
            DecisionPair(PrimaryCausalBaselineState.DepthCap, out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            AssertRejectsEveryNonfiniteIdentity(finite, unrestricted);
        }

        /// <summary>Exercises every terminal coordinate against NaN and both infinity signs on both pair sides.</summary>
        private static void AssertRejectsEveryNonfiniteIdentity(PrimaryCausalRun finite, PrimaryCausalRun unrestricted)
        {
            PrimaryCausalTerminalEvidence template = TerminalEvidence();
            foreach (TerminalCoordinate coordinate in Coordinates())
            {
                foreach (double value in NonFiniteValues())
                {
                    PrimaryCausalTerminalEvidence terminal = WithCoordinate(template, coordinate, value);
                    PrimaryCausalDecisionRecord record = PrimaryCausalDecisionDiagnostics.Evaluate(WithTerminalEvidence(finite, terminal), WithTerminalEvidence(unrestricted, terminal));
                    Assert.That(record.GateResult, Is.EqualTo(PrimaryCausalGateResult.Reject), coordinate + "=" + value);
                    Assert.That(record.Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject), coordinate + "=" + value);
                    Assert.That(record.Reason, Is.EqualTo("non-finite-complete-result"), coordinate + "=" + value);
                }
            }
        }

        /// <summary>Runs the canonical pair for one NoRepair-shaped terminal state.</summary>
        private static void DecisionPair(PrimaryCausalBaselineState state, out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted)
        {
            var invocation = new PrimaryCausalInvocation(state == PrimaryCausalBaselineState.BudgetExhausted ? 207 : state == PrimaryCausalBaselineState.Accepted ? 0 : 195, state == PrimaryCausalBaselineState.DepthCap ? 0.25d : state == PrimaryCausalBaselineState.BudgetExhausted ? 0.5d : 1.0d, state == PrimaryCausalBaselineState.DepthCap ? BitConverter.Int64BitsToDouble(unchecked((long)0x3F50624DD2F1A9FCUL)) : 1.0d, false, state);
            finite = PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.Finite512); unrestricted = PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.NoSelectionBudget);
        }

        /// <summary>Provides a finite terminal evidence template independent of the pair's original result shape.</summary>
        private static PrimaryCausalTerminalEvidence TerminalEvidence()
        {
            var invocation = new PrimaryCausalInvocation(195, 0.25d, BitConverter.Int64BitsToDouble(unchecked((long)0x3F50624DD2F1A9FCUL)), false, PrimaryCausalBaselineState.DepthCap);
            return PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.NoSelectionBudget).CompleteResult.Value.TerminalEvidence.Value;
        }

        /// <summary>Replaces exactly one terminal identity coordinate while retaining all other evidence bits.</summary>
        private static PrimaryCausalTerminalEvidence WithCoordinate(PrimaryCausalTerminalEvidence source, TerminalCoordinate coordinate, double value)
        {
            PrimaryCausalTerminalInvocation identity = source.Identity;
            double outer = coordinate == TerminalCoordinate.Outer ? value : identity.Outer; double left = coordinate == TerminalCoordinate.Left ? value : identity.Left; double right = coordinate == TerminalCoordinate.Right ? value : identity.Right;
            return new PrimaryCausalTerminalEvidence(new PrimaryCausalTerminalInvocation(identity.Axis, identity.HasOuter, outer, left, right, identity.Depth), source.Category, source.Decision, source.Arithmetic, source.DecisionOrder);
        }

        /// <summary>Copies a complete result while injecting one terminal identity and shared finite top-level values.</summary>
        private static PrimaryCausalRun WithTerminalEvidence(PrimaryCausalRun source, PrimaryCausalTerminalEvidence terminal)
        {
            PrimaryCausalCompleteResult result = source.CompleteResult.Value;
            var replacement = new PrimaryCausalCompleteResult(result.TerminalState, result.Decision, result.StartedAttemptCount, 1.0d, 1.0d, terminal, 1.0d, result.Evaluations, result.Panels, result.Depth, result.Diagnostic);
            return new PrimaryCausalRun(source.Invocation, source.Mode, source.Availability, source.UnavailableReason, Copy(source.Attempts), Copy(source.Reservations), Copy(source.Lineage), Copy(source.Terminals), Copy(source.CrossAxisEdges), Copy(source.Aggregates), Copy(source.TerminalAncestorChain), source.FirstContradictionTrace, source.TerminalState, replacement, source.ModeCommonCoreDigest, source.PreObserverStateDigest, source.PostObserverStateDigest, source.ObserverDisabledWitness, source.ObserverIsolationSnapshot, source.DirectResult, source.PreReservationAttemptCore);
        }

        /// <summary>Copies retained evidence before immutable run construction applies its own bounds.</summary>
        private static T[] Copy<T>(IReadOnlyList<T> values)
        {
            var copy = new T[values.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = values[index];
            return copy;
        }

        /// <summary>Returns every terminal coordinate subject to the finite identity contract.</summary>
        private static TerminalCoordinate[] Coordinates() => new[] { TerminalCoordinate.Left, TerminalCoordinate.Right, TerminalCoordinate.Outer };

        /// <summary>Returns every rejected nonfinite binary64 value.</summary>
        private static double[] NonFiniteValues() => new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity };

        /// <summary>Identifies the mutable terminal identity coordinate in one fixture row.</summary>
        private enum TerminalCoordinate
        {
            /// <summary>The outer coordinate present in the template terminal.</summary>
            Outer,
            /// <summary>The interval left coordinate.</summary>
            Left,
            /// <summary>The interval right coordinate.</summary>
            Right
        }
    }
}
