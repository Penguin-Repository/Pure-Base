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

// Tests bounded causal retention and unavailable observer-state identity contracts.

using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Tests bounded causal retention and unavailable observer-state identity contracts.</summary>
    public sealed class PureBasePbrMultipleScatteringPrimaryCausalObserverRetentionTests
    {
        /// <summary>Retains only the bounded ordinary prefix while preserving a later terminal's ancestry and owner edge.</summary>
        [Test]
        public void PrimaryCausalObserverBoundsOrdinaryEvidenceAndRetainsTerminalAncestry()
        {
            var observer = new PrimaryCausalObserver(new PrimaryCausalInvocation(0, 0.25d, 0.75d, false, PrimaryCausalBaselineState.DepthCap));
            for (int index = 0; index < 514; index++) RecordSyntheticRule(observer, index); RecordAttempts(observer, 514, -1);
            RecordTerminal(observer); PrimaryCausalRun run = Complete(observer);

            Assert.That(run.Availability, Is.EqualTo(PrimaryCausalAvailability.Unavailable)); Assert.That(run.UnavailableReason, Is.EqualTo("observer-state-identity-unavailable")); Assert.That(run.ObserverIsolationSnapshot.HasValue, Is.False); Assert.That(run.PreObserverStateDigest.HasValue, Is.False); Assert.That(run.PostObserverStateDigest.HasValue, Is.False);
            Assert.That(run.Lineage.Count, Is.EqualTo(513)); Assert.That(run.Attempts.Count, Is.EqualTo(513)); Assert.That(run.CompleteResult.Value.StartedAttemptCount, Is.EqualTo(514)); Assert.That(run.CrossAxisEdges.Count, Is.EqualTo(521)); Assert.That(run.Aggregates.Count, Is.LessThanOrEqualTo(516));
            Assert.That(HasAggregate(run, "eta", -1, -1), Is.True);
            Assert.That(run.TerminalAncestorChain.Count, Is.EqualTo(3));
            PrimaryCausalLineageRecord terminal = run.TerminalAncestorChain[0]; PrimaryCausalLineageRecord etaRoot = run.TerminalAncestorChain[1]; PrimaryCausalLineageRecord psiRoot = run.TerminalAncestorChain[2];
            Assert.That(terminal.SameAxisParentId, Is.EqualTo(etaRoot.InvocationId)); Assert.That(etaRoot.SameAxisParentId, Is.EqualTo(0)); Assert.That(psiRoot.Axis, Is.EqualTo("psi"));
            PrimaryCausalCrossAxisEdge edge = TerminalEdge(run, etaRoot.CrossAxisEdgeId); Assert.That(edge.SourcePsiInvocationId, Is.EqualTo(psiRoot.InvocationId)); Assert.That(edge.EdgeId, Is.GreaterThan(513));
        }

        /// <summary>Changes one retained core field while preserving sequence identity and total started attempts.</summary>
        [Test]
        public void PrimaryCausalObserverDigestIncludesNonSequenceCoreFields()
        {
            var baseline = new PrimaryCausalObserver(new PrimaryCausalInvocation(1, 0.25d, 0.75d, false, PrimaryCausalBaselineState.Accepted));
            var changed = new PrimaryCausalObserver(new PrimaryCausalInvocation(1, 0.25d, 0.75d, false, PrimaryCausalBaselineState.Accepted));
            RecordAttempts(baseline, 514, -1); RecordAttempts(changed, 514, 257);
            PrimaryCausalRun baselineRun = Complete(baseline); PrimaryCausalRun changedRun = Complete(changed);

            Assert.That(baselineRun.Attempts.Count, Is.EqualTo(513)); Assert.That(changedRun.Attempts.Count, Is.EqualTo(513)); Assert.That(baselineRun.CompleteResult.Value.StartedAttemptCount, Is.EqualTo(514)); Assert.That(changedRun.CompleteResult.Value.StartedAttemptCount, Is.EqualTo(514)); Assert.That(baselineRun.Attempts[257].Sequence, Is.EqualTo(changedRun.Attempts[257].Sequence)); Assert.That(baselineRun.ModeCommonCoreDigest, Is.Not.EqualTo(changedRun.ModeCommonCoreDigest));
        }

        /// <summary>Publishes available evidence when the runner captures complete observer-state identities.</summary>
        [Test]
        public void PrimaryCausalDiagnosticRunnerPublishesAvailableWithObserverStateIdentity()
        {
            var invocation = new PrimaryCausalInvocation(0, 1.0d, 1.0d, false, PrimaryCausalBaselineState.Accepted); PrimaryCausalRun run = null;
            Assert.DoesNotThrow(() => run = PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.Finite512));

            Assert.That(run.Availability, Is.EqualTo(PrimaryCausalAvailability.Available)); Assert.That(run.CompleteResult.HasValue, Is.True); Assert.That(run.ModeCommonCoreDigest, Is.Not.EqualTo(0UL)); Assert.That(run.ObserverIsolationSnapshot.HasValue, Is.True); Assert.That(run.PreObserverStateDigest, Is.EqualTo(run.PostObserverStateDigest));
        }

        /// <summary>Requires a seeded observation to preserve Unity random state without consuming it.</summary>
        [Test]
        public void PrimaryCausalDiagnosticCapturesRandomStateIdentityWithoutConsumption()
        {
            UnityEngine.Random.State originalState = UnityEngine.Random.state;
            try
            {
                var invocation = new PrimaryCausalInvocation(0, 1.0d, 1.0d, false, PrimaryCausalBaselineState.Accepted);
                UnityEngine.Random.InitState(12345); PrimaryCausalRun seedA = PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.Finite512);
                UnityEngine.Random.InitState(67890); PrimaryCausalRun seedB = PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.Finite512);
                Assert.That(seedA.Availability, Is.EqualTo(PrimaryCausalAvailability.Available)); Assert.That(seedB.Availability, Is.EqualTo(PrimaryCausalAvailability.Available));
                Assert.That(seedA.PreObserverStateDigest, Is.EqualTo(seedA.PostObserverStateDigest)); Assert.That(seedB.PreObserverStateDigest, Is.EqualTo(seedB.PostObserverStateDigest)); Assert.That(seedA.PreObserverStateDigest, Is.Not.EqualTo(seedB.PreObserverStateDigest));
            }
            finally
            {
                UnityEngine.Random.state = originalState;
            }
        }

        /// <summary>Requires complete identities before a unit-only Available record can be constructed from an unavailable run.</summary>
        [Test]
        public void PrimaryCausalRunRejectsIncompleteIdentityForAvailableUnitContract()
        {
            var invocation = new PrimaryCausalInvocation(0, 1.0d, 1.0d, false, PrimaryCausalBaselineState.Accepted);
            PrimaryCausalRun observedRun = PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.Finite512); ulong observedIdentity = observedRun.ModeCommonCoreDigest;

            Assert.That(observedRun.Availability, Is.EqualTo(PrimaryCausalAvailability.Available)); Assert.That(observedIdentity, Is.Not.EqualTo(0UL));
            Assert.Throws<ArgumentException>(() => CreateAvailableUnitContract(observedRun, null, observedIdentity)); Assert.Throws<ArgumentException>(() => CreateAvailableUnitContract(observedRun, observedIdentity, null));
        }

        /// <summary>Rejects a budget-masked pair when the retained complete numerical result is nonfinite.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordBudgetToAcceptedRejectsNonfiniteResult()
        {
            PrimaryCausalInvocation invocation = Invocation(207, 0x3FE0000000000000UL, 0x3FF0000000000000UL, false, PrimaryCausalBaselineState.BudgetExhausted);
            PrimaryCausalDecisionRecord record = Decision(invocation, out _, out _);
            Assert.That(record.FiniteState, Is.EqualTo(PrimaryCausalBaselineState.BudgetExhausted)); Assert.That(record.UnrestrictedState, Is.EqualTo(PrimaryCausalBaselineState.Accepted)); Assert.That(record.Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject)); Assert.That(record.GateResult, Is.EqualTo(PrimaryCausalGateResult.Reject)); Assert.That(PrimaryCausalDecisionRenderer.Render(record), Does.Contain("reason=non-finite-complete-result"));
        }

        /// <summary>Rejects budget-masked depth evidence when the complete numerical result is nonfinite.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordBudgetToDepthRejectsNonfiniteResult()
        {
            PrimaryCausalInvocation invocation = Invocation(195, 0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, false, PrimaryCausalBaselineState.DepthCap);
            Decision(invocation, out PrimaryCausalRun observedFinite, out PrimaryCausalRun unrestricted); PrimaryCausalRun finite = AsFiniteBudgetFixture(observedFinite);
            PrimaryCausalDecisionRecord record = PrimaryCausalDecisionDiagnostics.Evaluate(finite, unrestricted);
            Assert.That(record.FiniteState, Is.EqualTo(PrimaryCausalBaselineState.BudgetExhausted)); Assert.That(record.UnrestrictedState, Is.EqualTo(PrimaryCausalBaselineState.DepthCap)); Assert.That(record.RawTerminalMatch, Is.False); Assert.That(record.GateResult, Is.EqualTo(PrimaryCausalGateResult.Reject)); Assert.That(record.Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject)); Assert.That(record.Reason, Is.EqualTo("observer-disabled-terminal-mismatch")); Assert.That(PrimaryCausalDecisionRenderer.Render(record), Does.Contain("rawTerminalMatch=False"));
            Assert.That(PrimaryCausalDecisionDiagnostics.Evaluate(finite, unrestricted, unrestricted.ObserverDisabledWitness.RawDiagnostic, true, false).Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject)); Assert.That(PrimaryCausalDecisionDiagnostics.Evaluate(finite, unrestricted, unrestricted.ObserverDisabledWitness.RawDiagnostic, false, true).Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject));
        }

        /// <summary>Classifies exact Accepted mode equality as no repair without interpreting the outcome as a defect.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordAcceptedEqualityIsNoRepair()
        {
            PrimaryCausalInvocation invocation = Invocation(0, 0x3FF0000000000000UL, 0x3FF0000000000000UL, false, PrimaryCausalBaselineState.Accepted);
            PrimaryCausalDecisionRecord record = Decision(invocation, out _, out _);
            Assert.That(record.FiniteState, Is.EqualTo(PrimaryCausalBaselineState.Accepted)); Assert.That(record.UnrestrictedState, Is.EqualTo(PrimaryCausalBaselineState.Accepted)); Assert.That(record.RawTerminalMatch, Is.False); Assert.That(record.Classification, Is.EqualTo(PrimaryCausalDecisionClassification.NoRepair)); Assert.That(record.Reason, Is.EqualTo("accepted-exact-equality"));
        }

        /// <summary>Rejects duplicate or mismatched raw terminal identities without selecting a candidate terminal.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordRejectsDuplicateAndMismatchedTerminals()
        {
            PrimaryCausalRun duplicate = PrimaryCausalRun.UnavailableForParser(PrimaryCausalBaselineState.DepthCap, new[] { Terminal(), Terminal() }); PrimaryCausalRun mismatch = PrimaryCausalRun.UnavailableForParser(PrimaryCausalBaselineState.DepthCap, new[] { new PrimaryCausalTerminalInvocation("eta", true, 0.125d, 0.125d, 0.5d, 7) });
            Assert.That(PrimaryCausalDecisionDiagnostics.Evaluate(duplicate, duplicate, DepthText(), false, false).Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject)); Assert.That(PrimaryCausalDecisionDiagnostics.Evaluate(mismatch, mismatch, DepthText(), false, false).Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject));
        }

        /// <summary>Rejects a same-depth decision when only finite mode loses its terminal identity.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordRejectsFiniteDepthTerminalMissing()
        {
            DepthPair(out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            AssertRejectWithoutRawTerminalMatch(WithTerminals(finite, Array.Empty<PrimaryCausalTerminalInvocation>()), unrestricted);
        }

        /// <summary>Rejects a same-depth decision when only finite mode retains a duplicate terminal identity.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordRejectsFiniteDepthTerminalDuplicate()
        {
            DepthPair(out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            PrimaryCausalTerminalInvocation terminal = finite.Terminals[0];
            AssertRejectWithoutRawTerminalMatch(WithTerminals(finite, new[] { terminal, terminal }), unrestricted);
        }

        /// <summary>Rejects a same-depth decision when only finite mode retains a mismatched terminal identity.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordRejectsFiniteDepthTerminalMismatch()
        {
            DepthPair(out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            PrimaryCausalTerminalInvocation terminal = finite.Terminals[0];
            var mismatch = new PrimaryCausalTerminalInvocation(terminal.Axis, terminal.HasOuter, terminal.Outer + 0.125d, terminal.Left, terminal.Right, terminal.Depth);
            AssertRejectWithoutRawTerminalMatch(WithTerminals(finite, new[] { mismatch }), unrestricted);
        }

        /// <summary>Rejects a same-depth decision when only finite mode reports a different complete result.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordRejectsFiniteDepthCompleteResultDifference()
        {
            DepthPair(out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            PrimaryCausalCompleteResult result = finite.CompleteResult.Value;
            AssertReject(WithCompleteResult(finite, result.Estimate + 1.0d, result.Error, result.Tolerance), unrestricted);
        }

        /// <summary>Rejects budget masking when a retained complete numerical result is nonfinite.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordRejectsNonfiniteBudgetMasking()
        {
            PrimaryCausalInvocation invocation = Invocation(207, 0x3FE0000000000000UL, 0x3FF0000000000000UL, false, PrimaryCausalBaselineState.BudgetExhausted);
            Decision(invocation, out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            PrimaryCausalCompleteResult result = finite.CompleteResult.Value;
            AssertReject(WithCompleteResult(finite, double.NaN, result.Error, result.Tolerance), unrestricted);
        }

        /// <summary>Rejects Accepted equality when equal binary64 NaN values replace retained error evidence.</summary>
        [Test]
        public void PrimaryCausalDecisionRecordRejectsNonfiniteAcceptedEquality()
        {
            PrimaryCausalInvocation invocation = Invocation(0, 0x3FF0000000000000UL, 0x3FF0000000000000UL, false, PrimaryCausalBaselineState.Accepted);
            Decision(invocation, out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted);
            AssertReject(WithCompleteResult(finite, finite.CompleteResult.Value.Estimate, double.NaN, finite.CompleteResult.Value.Tolerance), WithCompleteResult(unrestricted, unrestricted.CompleteResult.Value.Estimate, double.NaN, unrestricted.CompleteResult.Value.Tolerance));
        }

        /// <summary>Records one completed Psi-to-Eta partition without consulting the primary integrator.</summary>
        private static void RecordSyntheticRule(PrimaryCausalObserver observer, int index)
        {
            observer.Enter("psi", double.NaN, 0.0d, 1.0d, 0); observer.BeginPsiNode("coarse3", 0, index - 0.5d, -0.7745966692414834d); observer.BeginPsiNode("coarse3", 1, index, 0.0d); observer.BeginPsiNode("fine5", 4, index + 0.5d, 0.906179845938664d);
            observer.BeginPartition(0, index, 0.0d, 1.0d, false, "zero", "one"); observer.Enter("eta", index, 0.0d, 1.0d, 0);
            observer.Exit(index); observer.Exit(index);
        }

        /// <summary>Records more scalar starts than the ordinary retained core prefix allows.</summary>
        private static void RecordAttempts(PrimaryCausalObserver observer, int count, int changedIndex)
        {
            for (int index = 0; index < count; index++)
            {
                double rawX = index == changedIndex ? index + 0.25d : index + 0.125d;
                int sequence = observer.BeginAttempt("eta-x", index, index * 0.001d, rawX, 0.0d, 1.0d, index % 3, rawX, 2.0d * rawX);
                observer.RecordReservation(sequence, false, true, 0, 0); observer.RecordStartedAttempt(sequence);
            }
        }

        /// <summary>Records a depth terminal whose ownership edge occurs after the ordinary edge prefix is full.</summary>
        private static void RecordTerminal(PrimaryCausalObserver observer)
        {
            observer.Enter("psi", double.NaN, 0.0d, 1.0d, 0); RecordTerminalRule(observer);
            observer.BeginPartition(0, 514, 0.0d, 1.0d, false, "zero", "one"); observer.Enter("eta", 1.0d, 0.0d, 1.0d, 0);
            observer.BeginChild("L"); observer.Enter("eta", 1.0d, 0.0d, 0.5d, 1);
            observer.RecordDecisionCondition("panel-cap"); observer.RecordDecisionCondition("accepted"); observer.RecordDecisionCondition("depth-cap");
            observer.RecordDecision("depth-cap", "eta", 1.0d, 0.0d, 0.5d, 1, new AdaptiveEstimate(1.0d, 0.25d), new AdaptiveEstimate(2.0d, 0.5d), 0.5d, 0.25d, 0.25d, 1.0d, 0.5d);
        }

        /// <summary>Records the full canonical Psi prefix needed by the retained terminal ownership edge.</summary>
        private static void RecordTerminalRule(PrimaryCausalObserver observer)
        {
            observer.BeginPsiNode("coarse3", 0, -0.75d, -0.7745966692414834d); observer.BeginPsiNode("coarse3", 1, 0.0d, 0.0d); observer.BeginPsiNode("coarse3", 2, 0.75d, 0.7745966692414834d);
            observer.BeginPsiNode("fine5", 0, -0.9d, -0.906179845938664d); observer.BeginPsiNode("fine5", 1, -0.5d, -0.5384693101056831d); observer.BeginPsiNode("fine5", 2, 0.0d, 0.0d); observer.BeginPsiNode("fine5", 3, 0.5d, 0.5384693101056831d); observer.BeginPsiNode("fine5", 4, 1.0d, 0.906179845938664d);
        }

        /// <summary>Builds a complete synthetic depth-cap result through the observer's regular publication path.</summary>
        private static PrimaryCausalRun Complete(PrimaryCausalObserver observer)
        {
            var result = new AdaptiveResult(2.0d, 1.0d, 0.5d, 514, 514, 18, "primary depth synthetic");
            var direct = new PrimaryCausalDirectResult(result.Value, result.Error, result.Tolerance, result.Evaluations, result.Panels, result.Depth, result.Diagnostic);
            return observer.Complete(result, PrimaryCausalMode.NoSelectionBudget, null, direct, null, null, null);
        }

        /// <summary>Builds a unit-only Available construction request from real unavailable-run fields.</summary>
        private static PrimaryCausalRun CreateAvailableUnitContract(PrimaryCausalRun unavailableRun, ulong? preObserverStateDigest, ulong? postObserverStateDigest)
        {
            ulong observedIdentity = unavailableRun.ModeCommonCoreDigest; var isolation = new PrimaryCausalObserverIsolationSnapshot(observedIdentity, observedIdentity, observedIdentity, observedIdentity);
            return new PrimaryCausalRun(unavailableRun.Invocation, unavailableRun.Mode, PrimaryCausalAvailability.Available, null, Array.Empty<PrimaryCausalAttemptCore>(), Array.Empty<ReservationObservation>(), Array.Empty<PrimaryCausalLineageRecord>(), Array.Empty<PrimaryCausalTerminalInvocation>(), Array.Empty<PrimaryCausalCrossAxisEdge>(), Array.Empty<PrimaryCausalAggregate>(), Array.Empty<PrimaryCausalLineageRecord>(), null, unavailableRun.TerminalState, unavailableRun.CompleteResult, observedIdentity, preObserverStateDigest, postObserverStateDigest, unavailableRun.ObserverDisabledWitness, isolation, unavailableRun.DirectResult);
        }

        /// <summary>Runs one fixed pair and produces the single bounded decision retained by the test.</summary>
        private static PrimaryCausalDecisionRecord Decision(PrimaryCausalInvocation invocation, out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted)
        {
            finite = PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.Finite512); unrestricted = PrimaryCausalDiagnosticRunner.Run(invocation, PrimaryCausalMode.NoSelectionBudget); return PrimaryCausalDecisionDiagnostics.Evaluate(finite, unrestricted);
        }

        /// <summary>Runs the fixed intrinsic-depth invocation under both selection reservation modes.</summary>
        private static void DepthPair(out PrimaryCausalRun finite, out PrimaryCausalRun unrestricted)
        {
            Decision(Invocation(195, 0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, false, PrimaryCausalBaselineState.DepthCap), out finite, out unrestricted);
            finite = WithCompleteResult(finite, 1.0d, 1.0d, 1.0d); unrestricted = WithCompleteResult(unrestricted, 1.0d, 1.0d, 1.0d);
            Assert.That(PrimaryCausalDecisionDiagnostics.Evaluate(finite, unrestricted).Classification, Is.EqualTo(PrimaryCausalDecisionClassification.NoRepair));
        }

        /// <summary>Requires an altered finite-only fixture to fail closed without changing the null-mode evidence.</summary>
        private static void AssertReject(PrimaryCausalRun finite, PrimaryCausalRun unrestricted)
        {
            Assert.That(PrimaryCausalDecisionDiagnostics.Evaluate(finite, unrestricted).Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject));
        }

        /// <summary>Requires a finite-side raw terminal failure to remain false in both the record and rendered output.</summary>
        private static void AssertRejectWithoutRawTerminalMatch(PrimaryCausalRun finite, PrimaryCausalRun unrestricted)
        {
            PrimaryCausalDecisionRecord record = PrimaryCausalDecisionDiagnostics.Evaluate(finite, unrestricted);
            Assert.That(record.Classification, Is.EqualTo(PrimaryCausalDecisionClassification.Reject)); Assert.That(record.RawTerminalMatch, Is.False); Assert.That(PrimaryCausalDecisionRenderer.Render(record), Does.Contain("rawTerminalMatch=False"));
        }

        /// <summary>Copies a run while replacing only its retained terminal identity collection.</summary>
        private static PrimaryCausalRun WithTerminals(PrimaryCausalRun source, PrimaryCausalTerminalInvocation[] terminals)
        {
            return Clone(source, source.CompleteResult.Value, terminals);
        }

        /// <summary>Copies a run while replacing only its final numerical result values.</summary>
        private static PrimaryCausalRun WithCompleteResult(PrimaryCausalRun source, double estimate, double error, double tolerance)
        {
            PrimaryCausalCompleteResult result = source.CompleteResult.Value;
            var replacement = new PrimaryCausalCompleteResult(result.TerminalState, result.Decision, result.StartedAttemptCount, estimate, error, result.TerminalEvidence, tolerance, result.Evaluations, result.Panels, result.Depth, result.Diagnostic);
            return Clone(source, replacement, Copy(source.Terminals));
        }

        /// <summary>Copies all immutable retained fields while replacing fixture-specific terminal and result data.</summary>
        private static PrimaryCausalRun Clone(PrimaryCausalRun source, PrimaryCausalCompleteResult result, PrimaryCausalTerminalInvocation[] terminals)
        {
            return new PrimaryCausalRun(source.Invocation, source.Mode, source.Availability, source.UnavailableReason, Copy(source.Attempts), Copy(source.Reservations), Copy(source.Lineage), terminals, Copy(source.CrossAxisEdges), Copy(source.Aggregates), Copy(source.TerminalAncestorChain), source.FirstContradictionTrace, source.TerminalState, result, source.ModeCommonCoreDigest, source.PreObserverStateDigest, source.PostObserverStateDigest, source.ObserverDisabledWitness, source.ObserverIsolationSnapshot, source.DirectResult, source.PreReservationAttemptCore);
        }

        /// <summary>Builds a test-only finite-budget outcome while retaining the exact fixed depth invocation and raw evidence.</summary>
        private static PrimaryCausalRun AsFiniteBudgetFixture(PrimaryCausalRun source)
        {
            PrimaryCausalCompleteResult result = source.CompleteResult.Value;
            var budget = new PrimaryCausalCompleteResult(PrimaryCausalBaselineState.BudgetExhausted, "selection-budget-pre-kernel", result.StartedAttemptCount, result.Estimate, result.Error, null, result.Tolerance, result.Evaluations, result.Panels, result.Depth, "selection-budget-pre-kernel");
            return new PrimaryCausalRun(source.Invocation, PrimaryCausalMode.Finite512, PrimaryCausalAvailability.Available, string.Empty, Copy(source.Attempts), Copy(source.Reservations), Copy(source.Lineage), Array.Empty<PrimaryCausalTerminalInvocation>(), Copy(source.CrossAxisEdges), Copy(source.Aggregates), Copy(source.TerminalAncestorChain), source.FirstContradictionTrace, PrimaryCausalBaselineState.BudgetExhausted, budget, source.ModeCommonCoreDigest, source.PreObserverStateDigest, source.PostObserverStateDigest, null, source.ObserverIsolationSnapshot, source.DirectResult, source.PreReservationAttemptCore);
        }

        /// <summary>Copies a retained immutable list before the run constructor applies its own bounded copy.</summary>
        private static T[] Copy<T>(IReadOnlyList<T> values)
        {
            var copy = new T[values.Count]; for (int index = 0; index < copy.Length; index++) copy[index] = values[index]; return copy;
        }

        /// <summary>Builds one exact-bit fixed invocation without consulting mutable selection state.</summary>
        private static PrimaryCausalInvocation Invocation(int index, ulong p, ulong v, bool branch, PrimaryCausalBaselineState state)
        {
            return new PrimaryCausalInvocation(index, BitConverter.Int64BitsToDouble(unchecked((long)p)), BitConverter.Int64BitsToDouble(unchecked((long)v)), branch, state);
        }

        /// <summary>Returns the sole exact depth diagnostic accepted by the parser fixture.</summary>
        private static string DepthText() => "numerical-limit primary depth axis=eta outer=0.125 interval=[0.25,0.5] coarse=1 fine=2 inheritedInnerError=2 ruleDelta=3 absoluteLimit=4 relativeLimit=5 error=5 limit=9 errorOverLimit=0.55555555555555558 depth=7";

        /// <summary>Creates the exact terminal identity used by duplicate and mismatch fixtures.</summary>
        private static PrimaryCausalTerminalInvocation Terminal() => new PrimaryCausalTerminalInvocation("eta", true, 0.125d, 0.25d, 0.5d, 7);

        /// <summary>Gets whether one retained aggregate represents an axis and partition identity.</summary>
        private static bool HasAggregate(PrimaryCausalRun run, string axis, int line, int index)
        {
            foreach (PrimaryCausalAggregate aggregate in run.Aggregates) if (aggregate.Axis == axis && aggregate.PartitionLine == line && aggregate.PartitionIndex == index) return true;
            return false;
        }

        /// <summary>Gets the terminal ownership edge without accepting absent provenance.</summary>
        private static PrimaryCausalCrossAxisEdge TerminalEdge(PrimaryCausalRun run, int edgeId)
        {
            foreach (PrimaryCausalCrossAxisEdge edge in run.CrossAxisEdges) if (edge.EdgeId == edgeId) return edge;
            Assert.Fail("Missing terminal ownership edge."); return default;
        }
    }
}
