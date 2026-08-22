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

// Seeds exact-bit causal contracts with observed primary identity requirements.

using System;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Tests frozen causal inputs, explicit unavailable evidence, and independent gate contracts.</summary>
    public sealed class PureBasePbrMultipleScatteringPrimaryCausalDiagnosticTests
    {
        /// <summary>Stores every fixed source-order failure row and Accepted control as exact literals.</summary>
        private static readonly PrimaryCausalCase[] FixedCases =
        {
            Case(0, 0x3FF0000000000000UL, 0x3FF0000000000000UL, false, PrimaryCausalBaselineState.Accepted, true, true, true, false, 192),
            Case(0, 0x3FF0000000000000UL, 0x3FF0000000000000UL, true, PrimaryCausalBaselineState.Accepted, true, true, true, false, 192),
            Case(1, 0x3FF0000000000000UL, 0x3FEF746EA3A45F8AUL, false, PrimaryCausalBaselineState.BudgetExhausted, true, false, false, false, 512),
            Case(1, 0x3FF0000000000000UL, 0x3FEF746EA3A45F8AUL, true, PrimaryCausalBaselineState.BudgetExhausted, true, false, false, false, 512),
            Case(195, 0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, false, PrimaryCausalBaselineState.DepthCap, false, true, false, true, 0),
            Case(195, 0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, true, PrimaryCausalBaselineState.DepthCap, false, true, false, true, 0),
            Case(207, 0x3FE0000000000000UL, 0x3FF0000000000000UL, false, PrimaryCausalBaselineState.BudgetExhausted, false, true, true, false, 512),
            Case(207, 0x3FE0000000000000UL, 0x3FF0000000000000UL, true, PrimaryCausalBaselineState.BudgetExhausted, false, true, true, false, 512),
        };

        /// <summary>Requires the literal source-order matrix before unavailable causal evidence makes this contract RED.</summary>
        [Test]
        public void PrimaryCausalDiagnosticMatrixUsesFrozenRowsAndModes()
        {
            AssertFixedMatrix(); PrimaryCausalRun[] runs = ExecuteFixedOrder(); AssertFixedOrder(runs);
        }

        /// <summary>Verifies the normal accepted control through both reservation modes twice.</summary>
        [Test]
        public void PrimaryCausalAcceptedControlNormal() => AssertFixedCase(FixedCases[0]);

        /// <summary>Verifies the switch accepted control through both reservation modes twice.</summary>
        [Test]
        public void PrimaryCausalAcceptedControlSwitch() => AssertFixedCase(FixedCases[1]);

        /// <summary>Verifies the normal index-one reservation exhaustion prefix through both modes twice.</summary>
        [Test]
        public void PrimaryCausalBudgetIndex1Normal() => AssertFixedCase(FixedCases[2]);

        /// <summary>Verifies the switch index-one reservation exhaustion prefix through both modes twice.</summary>
        [Test]
        public void PrimaryCausalBudgetIndex1Switch() => AssertFixedCase(FixedCases[3]);

        /// <summary>Verifies the normal intrinsic depth terminal through both reservation modes twice.</summary>
        [Test]
        public void PrimaryCausalDepthIndex195Normal() => AssertFixedCase(FixedCases[4]);

        /// <summary>Verifies the switch intrinsic depth terminal through both reservation modes twice.</summary>
        [Test]
        public void PrimaryCausalDepthIndex195Switch() => AssertFixedCase(FixedCases[5]);

        /// <summary>Verifies the normal index-207 reservation exhaustion prefix through both modes twice.</summary>
        [Test]
        public void PrimaryCausalBudgetIndex207Normal() => AssertFixedCase(FixedCases[6]);

        /// <summary>Verifies the switch index-207 reservation exhaustion prefix through both modes twice.</summary>
        [Test]
        public void PrimaryCausalBudgetIndex207Switch() => AssertFixedCase(FixedCases[7]);

        /// <summary>Requires bounded lineage and common-prefix evidence, which is intentionally unavailable before instrumentation.</summary>
        [Test]
        public void PrimaryCausalDiagnosticRecordsBoundedLineageAndCommonPrefix()
        {
            PrimaryCausalRun finite = PrimaryCausalDiagnosticRunner.Run(FixedCases[2].Invocation, PrimaryCausalMode.Finite512);
            PrimaryCausalRun unrestricted = PrimaryCausalDiagnosticRunner.Run(FixedCases[2].Invocation, PrimaryCausalMode.NoSelectionBudget);
            AssertAvailable(finite);
            AssertAvailable(unrestricted);
            AssertCompleteResult(finite, PrimaryCausalBaselineState.BudgetExhausted);
            AssertBudgetExhaustedPrefix(finite, unrestricted);
        }

        /// <summary>Requires deterministic observational isolation evidence.</summary>
        [Test]
        public void PrimaryCausalDiagnosticIsDeterministicObservationalAndIsolated()
        {
            PrimaryCausalRun first = PrimaryCausalDiagnosticRunner.Run(FixedCases[0].Invocation, PrimaryCausalMode.Finite512);
            PrimaryCausalRun second = PrimaryCausalDiagnosticRunner.Run(FixedCases[0].Invocation, PrimaryCausalMode.Finite512);
            PrimaryCausalRun unrestricted = PrimaryCausalDiagnosticRunner.Run(FixedCases[0].Invocation, PrimaryCausalMode.NoSelectionBudget);
            AssertAvailable(first);
            AssertAvailable(second);
            AssertAvailable(unrestricted);
            AssertDeterministicAndIsolated(first, second);
            AssertFullNumericalModeEquality(first, unrestricted, PrimaryCausalBaselineState.Accepted);
        }

        /// <summary>Requires observed invariant evidence, which is intentionally unavailable before instrumentation.</summary>
        [Test]
        public void PrimaryCausalDiagnosticIndependentInvariantsGateRepair()
        {
            PrimaryCausalRun normalFinite = PrimaryCausalDiagnosticRunner.Run(FixedCases[4].Invocation, PrimaryCausalMode.Finite512);
            PrimaryCausalRun normalUnrestricted = PrimaryCausalDiagnosticRunner.Run(FixedCases[4].Invocation, PrimaryCausalMode.NoSelectionBudget);
            PrimaryCausalRun switchFinite = PrimaryCausalDiagnosticRunner.Run(FixedCases[5].Invocation, PrimaryCausalMode.Finite512);
            PrimaryCausalRun switchUnrestricted = PrimaryCausalDiagnosticRunner.Run(FixedCases[5].Invocation, PrimaryCausalMode.NoSelectionBudget);
            AssertAvailable(normalFinite);
            AssertAvailable(normalUnrestricted); AssertAvailable(switchFinite); AssertAvailable(switchUnrestricted);
            AssertFullNumericalModeEquality(normalFinite, normalUnrestricted, PrimaryCausalBaselineState.DepthCap); AssertFullNumericalModeEquality(switchFinite, switchUnrestricted, PrimaryCausalBaselineState.DepthCap);
            AssertIndependentDepthCapGate(normalFinite); AssertIndependentDepthCapGate(normalUnrestricted); AssertIndependentDepthCapGate(switchFinite); AssertIndependentDepthCapGate(switchUnrestricted);
        }

        /// <summary>Proves every synthetic authorization and rejection path without production arithmetic helpers.</summary>
        [Test]
        public void PrimaryCausalDecisionGateFailsClosedForSyntheticEvidence()
        {
            foreach (PrimaryCausalGateCase test in CorrectedGateCases()) Assert.That(PrimaryCausalDecisionGate.Evaluate(test.Evidence), Is.EqualTo(test.Expected), test.Name);
            AssertIndependentTerminalRetention();
        }

        /// <summary>Parses an exact depth terminal and retains its round-trippable binary64 arithmetic fields.</summary>
        [Test]
        public void PrimaryCausalObserverDisabledDepthParserMatchesExactTerminal()
        {
            PrimaryCausalRun run = ParserIdentityFixture(new[] { Terminal() }); AssertParserFixtureUnavailable(run);
            Assert.That(PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(DepthText(), run, out PrimaryCausalDepthEvidence evidence), Is.True);
            Assert.That(PrimaryCausalDiagnosticParser.TryParseObserverDisabledDepth(DepthText(), out PrimaryCausalObserverDisabledWitness witness), Is.True);
            Assert.That(witness.RawDiagnostic, Is.EqualTo(DepthText())); Assert.That(witness.Category, Is.EqualTo(PrimaryCausalBaselineState.DepthCap)); Assert.That(witness.Decision, Is.EqualTo("depth-cap")); Assert.That(witness.DecisionOrder.HasValue, Is.False);
            Assert.That(witness.Terminal.Axis, Is.EqualTo("eta")); Assert.That(witness.Terminal.Depth, Is.EqualTo(7)); Assert.That(Bits(witness.Arithmetic.Error), Is.EqualTo(Bits(evidence.Error)));
            Assert.That(Bits(evidence.Coarse), Is.EqualTo(0x3FF0000000000000UL)); Assert.That(Bits(evidence.Fine), Is.EqualTo(0x4000000000000000UL)); Assert.That(Bits(evidence.Inherited), Is.EqualTo(0x4000000000000000UL));
            Assert.That(Bits(evidence.Delta), Is.EqualTo(0x4008000000000000UL)); Assert.That(Bits(evidence.Absolute), Is.EqualTo(0x4010000000000000UL)); Assert.That(Bits(evidence.Relative), Is.EqualTo(0x4014000000000000UL));
            Assert.That(Bits(evidence.Error), Is.EqualTo(0x4014000000000000UL)); Assert.That(Bits(evidence.Limit), Is.EqualTo(0x4022000000000000UL)); Assert.That(Bits(evidence.ErrorOverLimit), Is.EqualTo(0x3FE1C71C71C71C72UL));
            PrimaryCausalRun outerNone = ParserIdentityFixture(new[] { new PrimaryCausalTerminalInvocation("eta", false, 0.0d, 0.25d, 0.5d, 7) }); AssertParserFixtureUnavailable(outerNone); Assert.That(PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(DepthText().Replace("outer=0.125", "outer=none"), outerNone, out _), Is.True);
            AssertParsedWitnessGate(NormalDepthText(), null, PrimaryCausalGateResult.NoRepair);
            AssertParsedWitnessGate(DepthText(), PrimaryCausalTerminalContradiction.DecisionControlFlow, PrimaryCausalGateResult.AuthorizeTerminalSplitRepair);
            AssertParsedWitnessGateRejectsCausalDifferences();
        }

        /// <summary>Rejects malformed, ambiguous, ineligible, nonfinite, and identity-mismatched depth evidence.</summary>
        [Test]
        public void PrimaryCausalObserverDisabledDepthParserRejectsMalformedAmbiguousOrMismatchedEvidence()
        {
            PrimaryCausalRun run = ParserIdentityFixture(new[] { Terminal() }); AssertParserFixtureUnavailable(run);
            foreach (string token in RequiredTokens()) { AssertRejected(DepthText().Replace(token, string.Empty), run); AssertRejected(DepthText().Replace(token, token + " " + token), run); }
            foreach (string field in Binary64Fields()) { AssertRejected(DepthText().Replace(field + "=" + FieldValue(field), field + "=invalid"), run); AssertRejected(DepthText().Replace(field + "=" + FieldValue(field), field + "=NaN"), run); AssertRejected(DepthText().Replace(field + "=" + FieldValue(field), field + "=Infinity"), run); }
            AssertRejected(DepthText().Replace("outer=0.125", "outer=NaN"), run); AssertRejected(DepthText().Replace("outer=0.125", "outer=Infinity"), run); AssertRejected(DepthText().Replace("outer=0.125", "outer=invalid"), run);
            AssertRejected(DepthText().Replace("[0.25,0.5]", "[invalid,0.5]"), run); AssertRejected(DepthText().Replace("[0.25,0.5]", "[NaN,0.5]"), run); AssertRejected(DepthText().Replace("[0.25,0.5]", "[Infinity,0.5]"), run);
            AssertRejected(DepthText().Replace("[0.25,0.5]", "[0.25,invalid]"), run); AssertRejected(DepthText().Replace("[0.25,0.5]", "[0.25,NaN]"), run); AssertRejected(DepthText().Replace("[0.25,0.5]", "[0.25,Infinity]"), run);
            AssertRejected(DepthText().Replace("depth=7", "depth=invalid"), run); AssertRejected(DepthText().Replace("depth=7", "depth=7.0"), run); AssertRejected(DepthText().Replace("depth=7", "depth=NaN"), run);
            AssertRejected(DepthText() + " trailing", run); AssertRejected(DepthText().Replace("axis=eta", "axis=eta-x"), run); AssertRejected(DepthText().Replace("outer=0.125", "outer=0.12500000000000003"), run);
            AssertRejected(DepthText().Replace("[0.25,0.5]", "[0.25000000000000006,0.5]"), run); AssertRejected(DepthText().Replace("[0.25,0.5]", "[0.25,0.50000000000000011]"), run); AssertRejected(DepthText().Replace("depth=7", "depth=8"), run);
            AssertRejected(DepthText(), ParserIdentityFixture(Array.Empty<PrimaryCausalTerminalInvocation>())); AssertRejected(DepthText(), ParserIdentityFixture(new[] { Terminal(), Terminal() }));
            foreach (PrimaryCausalBaselineState state in IneligibleStates()) AssertRejected(DepthText(), PrimaryCausalRun.UnavailableForParser(state, new[] { Terminal() }));
        }

        /// <summary>Builds one literal fixed row without reading mutable census data.</summary>
        private static PrimaryCausalCase Case(int index, ulong p, ulong v, bool branch, PrimaryCausalBaselineState state, bool training, bool validation, bool original, bool stress, int completed)
        {
            return new PrimaryCausalCase(new PrimaryCausalInvocation(index, FromBits(p), FromBits(v), branch, state), training, validation, original, stress, completed);
        }

        /// <summary>Verifies all fixed identities and normal-before-switch source ordering.</summary>
        private static void AssertFixedMatrix()
        {
            Assert.That(FixedCases.Length, Is.EqualTo(8)); Assert.That(FixedCases[0].Invocation.CoordinateIndex, Is.EqualTo(0)); Assert.That(FixedCases[1].Invocation.CoordinateIndex, Is.EqualTo(0));
            for (int index = 0; index < FixedCases.Length; index += 2) { Assert.That(FixedCases[index].Invocation.SwitchBranch, Is.False); Assert.That(FixedCases[index + 1].Invocation.SwitchBranch, Is.True); Assert.That(FixedCases[index].Invocation.CoordinateIndex, Is.EqualTo(FixedCases[index + 1].Invocation.CoordinateIndex)); Assert.That(Bits(FixedCases[index].Invocation.P), Is.EqualTo(Bits(FixedCases[index + 1].Invocation.P))); Assert.That(Bits(FixedCases[index].Invocation.NdotV), Is.EqualTo(Bits(FixedCases[index + 1].Invocation.NdotV))); }
            AssertRow(0, 0, 0x3FF0000000000000UL, 0x3FF0000000000000UL, PrimaryCausalBaselineState.Accepted, true, true, true, false, 192); AssertRow(1, 0, 0x3FF0000000000000UL, 0x3FF0000000000000UL, PrimaryCausalBaselineState.Accepted, true, true, true, false, 192);
            AssertRow(2, 1, 0x3FF0000000000000UL, 0x3FEF746EA3A45F8AUL, PrimaryCausalBaselineState.BudgetExhausted, true, false, false, false, 512); AssertRow(3, 1, 0x3FF0000000000000UL, 0x3FEF746EA3A45F8AUL, PrimaryCausalBaselineState.BudgetExhausted, true, false, false, false, 512);
            AssertRow(4, 195, 0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, PrimaryCausalBaselineState.DepthCap, false, true, false, true, 0); AssertRow(5, 195, 0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, PrimaryCausalBaselineState.DepthCap, false, true, false, true, 0);
            AssertRow(6, 207, 0x3FE0000000000000UL, 0x3FF0000000000000UL, PrimaryCausalBaselineState.BudgetExhausted, false, true, true, false, 512); AssertRow(7, 207, 0x3FE0000000000000UL, 0x3FF0000000000000UL, PrimaryCausalBaselineState.BudgetExhausted, false, true, true, false, 512);
            Assert.That(FixedCases[2].Invocation.BaselineState, Is.EqualTo(PrimaryCausalBaselineState.BudgetExhausted)); Assert.That(FixedCases[4].Invocation.BaselineState, Is.EqualTo(PrimaryCausalBaselineState.DepthCap)); Assert.That(FixedCases[6].Invocation.BaselineState, Is.EqualTo(PrimaryCausalBaselineState.BudgetExhausted)); Assert.That(FixedCases[0].CompletedKernelSamples, Is.EqualTo(192)); Assert.That(FixedCases[1].CompletedKernelSamples, Is.EqualTo(192));
        }

        /// <summary>Requires one matrix row to retain its literal identity, memberships, and expected result.</summary>
        private static void AssertRow(int row, int index, ulong p, ulong v, PrimaryCausalBaselineState state, bool training, bool validation, bool original, bool stress, int completed)
        {
            PrimaryCausalCase value = FixedCases[row]; Assert.That(value.Invocation.CoordinateIndex, Is.EqualTo(index)); Assert.That(Bits(value.Invocation.P), Is.EqualTo(p)); Assert.That(Bits(value.Invocation.NdotV), Is.EqualTo(v)); Assert.That(value.Invocation.BaselineState, Is.EqualTo(state)); Assert.That(value.Training, Is.EqualTo(training)); Assert.That(value.Validation, Is.EqualTo(validation)); Assert.That(value.Original, Is.EqualTo(original)); Assert.That(value.Stress, Is.EqualTo(stress)); Assert.That(value.CompletedKernelSamples, Is.EqualTo(completed));
        }

        /// <summary>Executes finite then null modes for every fixed row and repeats the entire order once.</summary>
        private static PrimaryCausalRun[] ExecuteFixedOrder()
        {
            var runs = new PrimaryCausalRun[FixedCases.Length * 4]; int written = 0;
            for (int pass = 0; pass < 2; pass++) foreach (PrimaryCausalCase value in FixedCases)
            {
                PrimaryCausalRun finite = PrimaryCausalDiagnosticRunner.Run(value.Invocation, PrimaryCausalMode.Finite512);
                if (written == 0) AssertAvailable(finite);
                runs[written++] = finite;
                runs[written++] = PrimaryCausalDiagnosticRunner.Run(value.Invocation, PrimaryCausalMode.NoSelectionBudget);
            }
            return runs;
        }

        /// <summary>Runs one frozen literal case in finite then null order for two deterministic passes.</summary>
        private static void AssertFixedCase(PrimaryCausalCase value)
        {
            PrimaryCausalRun finite = PrimaryCausalDiagnosticRunner.Run(value.Invocation, PrimaryCausalMode.Finite512); PrimaryCausalRun unrestricted = PrimaryCausalDiagnosticRunner.Run(value.Invocation, PrimaryCausalMode.NoSelectionBudget);
            PrimaryCausalRun repeatedFinite = PrimaryCausalDiagnosticRunner.Run(value.Invocation, PrimaryCausalMode.Finite512); PrimaryCausalRun repeatedUnrestricted = PrimaryCausalDiagnosticRunner.Run(value.Invocation, PrimaryCausalMode.NoSelectionBudget);
            var runs = new[] { finite, unrestricted, repeatedFinite, repeatedUnrestricted }; foreach (PrimaryCausalRun run in runs) AssertAvailable(run);
            AssertCaseRuns(value, finite, unrestricted, repeatedFinite, repeatedUnrestricted);
        }

        /// <summary>Applies the finite-prefix or complete-mode contract for two repeated runs of one literal case.</summary>
        private static void AssertCaseRuns(PrimaryCausalCase value, PrimaryCausalRun finite, PrimaryCausalRun unrestricted, PrimaryCausalRun repeatedFinite, PrimaryCausalRun repeatedUnrestricted)
        {
            PrimaryCausalDecisionRecord firstDecision = PrimaryCausalDecisionDiagnostics.Evaluate(finite, unrestricted); PrimaryCausalDecisionRecord repeatedDecision = PrimaryCausalDecisionDiagnostics.Evaluate(repeatedFinite, repeatedUnrestricted);
            PrimaryCausalDecisionClassification expectedDecision = value.Invocation.BaselineState == PrimaryCausalBaselineState.Accepted ? PrimaryCausalDecisionClassification.NoRepair : PrimaryCausalDecisionClassification.Reject;
            Assert.That(firstDecision.Classification, Is.EqualTo(expectedDecision), PrimaryCausalDecisionRenderer.Render(firstDecision)); Assert.That(repeatedDecision.Classification, Is.EqualTo(expectedDecision), PrimaryCausalDecisionRenderer.Render(repeatedDecision));
            if (value.Invocation.BaselineState != PrimaryCausalBaselineState.BudgetExhausted) { PrimaryCausalAvailableAssertions.AssertRepeatedMatrix(new[] { finite, unrestricted, repeatedFinite, repeatedUnrestricted }); AssertFullNumericalModeEquality(finite, unrestricted, value.Invocation.BaselineState); return; }
            AssertBudgetExhaustedPrefix(finite, unrestricted); AssertBudgetExhaustedPrefix(repeatedFinite, repeatedUnrestricted); PrimaryCausalAvailableAssertions.AssertDeterministicBudgetMaskedRuns(finite, repeatedFinite); PrimaryCausalAvailableAssertions.AssertDeterministicBudgetMaskedRuns(unrestricted, repeatedUnrestricted);
        }

        /// <summary>Requires complete causal evidence with observed identities.</summary>
        private static void AssertAvailable(PrimaryCausalRun run)
        {
            Assert.That(run.Availability, Is.EqualTo(PrimaryCausalAvailability.Available), "Causal primary evidence requires complete observed identities.");
        }

        /// <summary>Checks intended matrix run ordering after the primary runner becomes available.</summary>
        private static void AssertFixedOrder(PrimaryCausalRun[] runs)
        {
            Assert.That(runs.Length, Is.EqualTo(FixedCases.Length * 4));
            for (int index = 0; index < runs.Length; index++)
            {
                PrimaryCausalRun run = runs[index]; PrimaryCausalInvocation expected = FixedCases[(index / 2) % FixedCases.Length].Invocation;
                Assert.That(run.Mode, Is.EqualTo(index % 2 == 0 ? PrimaryCausalMode.Finite512 : PrimaryCausalMode.NoSelectionBudget)); Assert.That(run.Invocation.CoordinateIndex, Is.EqualTo(expected.CoordinateIndex)); Assert.That(run.Invocation.SwitchBranch, Is.EqualTo(expected.SwitchBranch)); Assert.That(Bits(run.Invocation.P), Is.EqualTo(Bits(expected.P))); Assert.That(Bits(run.Invocation.NdotV), Is.EqualTo(Bits(expected.NdotV))); Assert.That(run.Invocation.BaselineState, Is.EqualTo(expected.BaselineState));
            }
            int passLength = FixedCases.Length * 2;
            for (int index = 0; index < FixedCases.Length; index++) AssertCaseRuns(FixedCases[index], runs[index * 2], runs[index * 2 + 1], runs[passLength + index * 2], runs[passLength + index * 2 + 1]);
        }

        /// <summary>Requires an available run to retain a complete result and nonzero common-core digest.</summary>
        private static void AssertCompleteResult(PrimaryCausalRun run, PrimaryCausalBaselineState expected)
        {
            Assert.That(run.CompleteResult.HasValue, Is.True); Assert.That(run.ModeCommonCoreDigest, Is.Not.EqualTo(0UL));
            PrimaryCausalCompleteResult result = run.CompleteResult.Value;
            Assert.That(run.TerminalState, Is.EqualTo(expected)); Assert.That(result.TerminalState, Is.EqualTo(expected)); Assert.That(result.Decision, Is.Not.Empty); Assert.That(result.StartedAttemptCount, Is.GreaterThanOrEqualTo(0));
        }

        /// <summary>Requires the finite budget to retain 512 started cores and one separate rejected reservation.</summary>
        private static void AssertBudgetExhaustedPrefix(PrimaryCausalRun finite, PrimaryCausalRun unrestricted)
        {
            PrimaryCausalAvailableAssertions.AssertBudgetExhausted(finite, unrestricted);
        }

        /// <summary>Requires Accepted and DepthCap modes to retain every scalar value without a fabricated 513th core.</summary>
        private static void AssertFullNumericalModeEquality(PrimaryCausalRun finite, PrimaryCausalRun unrestricted, PrimaryCausalBaselineState expected)
        {
            PrimaryCausalAvailableAssertions.AssertFullModeEquality(finite, unrestricted, expected);
        }

        /// <summary>Requires two same-mode observations to preserve all retained evidence and primary state.</summary>
        private static void AssertDeterministicAndIsolated(PrimaryCausalRun first, PrimaryCausalRun second)
        {
            PrimaryCausalAvailableAssertions.AssertDeterministicAndIsolated(first, second);
        }

        /// <summary>Computes terminal arithmetic and control-flow checks independently before evaluating the repair gate.</summary>
        private static void AssertIndependentDepthCapGate(PrimaryCausalRun run)
        {
            PrimaryCausalGateResult expected = PrimaryCausalAvailableAssertions.AssertDepthCapInvariants(run);
            AssertCompleteResult(run, PrimaryCausalBaselineState.DepthCap); Assert.That(run.CompleteResult.Value.TerminalEvidence.HasValue, Is.True); Assert.That(run.ObserverDisabledWitness, Is.Not.Null);
            PrimaryCausalTerminalEvidence causal = run.CompleteResult.Value.TerminalEvidence.Value;
            bool parsed = PrimaryCausalDiagnosticParser.TryParseObserverDisabledDepth(run.ObserverDisabledWitness.RawDiagnostic, out PrimaryCausalObserverDisabledWitness witness);
            bool capBeforeAcceptance = causal.DecisionOrder.HasValue && causal.DecisionOrder.Value.PanelCapOrder < causal.DecisionOrder.Value.AcceptanceOrder && causal.DecisionOrder.Value.AcceptanceOrder < causal.DecisionOrder.Value.DepthCapOrder;
            var evidence = new PrimaryCausalGateEvidence(run.TerminalState, true, causal, parsed ? witness : null, false, false, false, capBeforeAcceptance, ExpectedContradiction(expected));
            Assert.That(PrimaryCausalDecisionGate.Evaluate(evidence), Is.EqualTo(expected));
        }

        /// <summary>Requires two mode-common attempt prefixes to agree bit-for-bit.</summary>
        private static void AssertCommonAttemptPrefix(PrimaryCausalRun first, PrimaryCausalRun second, int count)
        {
            Assert.That(first.Attempts.Count, Is.GreaterThanOrEqualTo(count)); Assert.That(second.Attempts.Count, Is.GreaterThanOrEqualTo(count));
            for (int index = 0; index < count; index++) AssertSameAttempt(first.Attempts[index], second.Attempts[index]);
        }

        /// <summary>Compares one retained scalar core without numeric normalization.</summary>
        private static void AssertSameAttempt(PrimaryCausalAttemptCore left, PrimaryCausalAttemptCore right)
        {
            Assert.That(left.Sequence, Is.EqualTo(right.Sequence)); Assert.That(left.SwitchBranch, Is.EqualTo(right.SwitchBranch)); Assert.That(Bits(left.P), Is.EqualTo(Bits(right.P))); Assert.That(Bits(left.NdotV), Is.EqualTo(Bits(right.NdotV))); Assert.That(left.Axis, Is.EqualTo(right.Axis));
            Assert.That(Bits(left.Psi), Is.EqualTo(Bits(right.Psi))); Assert.That(Bits(left.Eta), Is.EqualTo(Bits(right.Eta))); Assert.That(Bits(left.Sample), Is.EqualTo(Bits(right.Sample))); Assert.That(Bits(left.Left), Is.EqualTo(Bits(right.Left))); Assert.That(Bits(left.Right), Is.EqualTo(Bits(right.Right)));
            Assert.That(left.Depth, Is.EqualTo(right.Depth)); Assert.That(left.PartitionLine, Is.EqualTo(right.PartitionLine)); Assert.That(left.PartitionIndex, Is.EqualTo(right.PartitionIndex)); Assert.That(Bits(left.PreTransformEta), Is.EqualTo(Bits(right.PreTransformEta))); Assert.That(Bits(left.RawX), Is.EqualTo(Bits(right.RawX))); Assert.That(Bits(left.Jacobian), Is.EqualTo(Bits(right.Jacobian)));
        }

        /// <summary>Compares complete numerical results and optional terminal evidence bit-for-bit.</summary>
        private static void AssertSameResult(PrimaryCausalCompleteResult left, PrimaryCausalCompleteResult right)
        {
            Assert.That(left.TerminalState, Is.EqualTo(right.TerminalState)); Assert.That(left.Decision, Is.EqualTo(right.Decision)); Assert.That(left.StartedAttemptCount, Is.EqualTo(right.StartedAttemptCount)); Assert.That(Bits(left.Estimate), Is.EqualTo(Bits(right.Estimate))); Assert.That(Bits(left.Error), Is.EqualTo(Bits(right.Error))); Assert.That(left.TerminalEvidence.HasValue, Is.EqualTo(right.TerminalEvidence.HasValue));
            if (left.TerminalEvidence.HasValue) AssertSameTerminalEvidence(left.TerminalEvidence.Value, right.TerminalEvidence.Value);
        }

        /// <summary>Compares complete causal terminal evidence required by the independent witness gate.</summary>
        private static void AssertSameTerminalEvidence(PrimaryCausalTerminalEvidence left, PrimaryCausalTerminalEvidence right)
        {
            Assert.That(left.Category, Is.EqualTo(right.Category)); Assert.That(left.Decision, Is.EqualTo(right.Decision)); Assert.That(left.Identity.Axis, Is.EqualTo(right.Identity.Axis)); Assert.That(left.Identity.HasOuter, Is.EqualTo(right.Identity.HasOuter)); Assert.That(Bits(left.Identity.Outer), Is.EqualTo(Bits(right.Identity.Outer))); Assert.That(Bits(left.Identity.Left), Is.EqualTo(Bits(right.Identity.Left))); Assert.That(Bits(left.Identity.Right), Is.EqualTo(Bits(right.Identity.Right))); Assert.That(left.Identity.Depth, Is.EqualTo(right.Identity.Depth));
            Assert.That(Bits(left.Arithmetic.Coarse), Is.EqualTo(Bits(right.Arithmetic.Coarse))); Assert.That(Bits(left.Arithmetic.Fine), Is.EqualTo(Bits(right.Arithmetic.Fine))); Assert.That(Bits(left.Arithmetic.Inherited), Is.EqualTo(Bits(right.Arithmetic.Inherited))); Assert.That(Bits(left.Arithmetic.Delta), Is.EqualTo(Bits(right.Arithmetic.Delta))); Assert.That(Bits(left.Arithmetic.Absolute), Is.EqualTo(Bits(right.Arithmetic.Absolute))); Assert.That(Bits(left.Arithmetic.Relative), Is.EqualTo(Bits(right.Arithmetic.Relative))); Assert.That(Bits(left.Arithmetic.Error), Is.EqualTo(Bits(right.Arithmetic.Error))); Assert.That(Bits(left.Arithmetic.Limit), Is.EqualTo(Bits(right.Arithmetic.Limit))); Assert.That(Bits(left.Arithmetic.ErrorOverLimit), Is.EqualTo(Bits(right.Arithmetic.ErrorOverLimit)));
        }

        /// <summary>Builds helper-free synthetic rows that require both evidence sources to prove one shared contradiction.</summary>
        private static PrimaryCausalGateCase[] CorrectedGateCases()
        {
            PrimaryCausalDepthEvidence valid = new PrimaryCausalDepthEvidence(1.0d, 2.0d, 2.0d, 8.0d, 4.0d, 5.0d, 10.0d, 9.0d, 10.0d / 9.0d);
            PrimaryCausalDepthEvidence inheritedMismatch = new PrimaryCausalDepthEvidence(1.0d, 2.0d, 2.0d, 8.0d, 4.0d, 5.0d, 11.0d, 9.0d, 11.0d / 9.0d);
            PrimaryCausalDepthEvidence limitMismatch = new PrimaryCausalDepthEvidence(1.0d, 2.0d, 2.0d, 8.0d, 4.0d, 5.0d, 10.0d, 8.0d, 1.25d);
            PrimaryCausalDepthEvidence decisionMismatch = new PrimaryCausalDepthEvidence(1.0d, 2.0d, 2.0d, 3.0d, 2.0d, 3.0d, 5.0d, 5.0d, 1.0d);
            return new[]
            {
                Gate("valid-arithmetic-no-repair", GateEvidence(valid), PrimaryCausalGateResult.NoRepair),
                Gate("inherited-plus-delta-mismatch", GateEvidence(inheritedMismatch, terminalContradiction: PrimaryCausalTerminalContradiction.Arithmetic), PrimaryCausalGateResult.AuthorizeTerminalSplitRepair),
                Gate("absolute-plus-relative-mismatch", GateEvidence(limitMismatch, terminalContradiction: PrimaryCausalTerminalContradiction.Arithmetic), PrimaryCausalGateResult.AuthorizeTerminalSplitRepair),
                Gate("decision-inconsistency-error-at-or-below-limit", GateEvidence(decisionMismatch, terminalContradiction: PrimaryCausalTerminalContradiction.DecisionControlFlow), PrimaryCausalGateResult.AuthorizeTerminalSplitRepair),
                Gate("invalid-prefix", GateEvidence(valid, validPrefix: false), PrimaryCausalGateResult.Reject),
                Gate("missing-evidence", GateEvidence(valid, includeWitness: false), PrimaryCausalGateResult.Reject),
                Gate("routing-only", GateEvidence(inheritedMismatch, routingOnly: true, terminalContradiction: PrimaryCausalTerminalContradiction.Arithmetic), PrimaryCausalGateResult.Reject),
                Gate("child-return-only", GateEvidence(inheritedMismatch, childReturnOnly: true, terminalContradiction: PrimaryCausalTerminalContradiction.Arithmetic), PrimaryCausalGateResult.Reject),
                Gate("observer-only-contradiction", GateEvidence(inheritedMismatch, observerOnly: true, terminalContradiction: PrimaryCausalTerminalContradiction.Arithmetic), PrimaryCausalGateResult.Reject),
                Gate("nonfinite-evidence", GateEvidence(new PrimaryCausalDepthEvidence(1.0d, 2.0d, 2.0d, 8.0d, 4.0d, 5.0d, double.NaN, 9.0d, 1.0d), terminalContradiction: PrimaryCausalTerminalContradiction.Arithmetic), PrimaryCausalGateResult.Reject),
                Gate("fail-closed-classification", GateEvidence(valid, terminalContradiction: PrimaryCausalTerminalContradiction.Undefined), PrimaryCausalGateResult.Reject)
            };
        }

        /// <summary>Builds independent causal and observer-disabled evidence without a boolean witness shortcut.</summary>
        private static PrimaryCausalGateEvidence GateEvidence(PrimaryCausalDepthEvidence causalDepth, PrimaryCausalDepthEvidence? witnessDepth = null, PrimaryCausalTerminalInvocation? witnessTerminal = null, PrimaryCausalBaselineState state = PrimaryCausalBaselineState.DepthCap, string witnessDecision = "depth-cap", bool validPrefix = true, bool includeTerminal = true, bool includeWitness = true, bool routingOnly = false, bool childReturnOnly = false, bool observerOnly = false, string rawDiagnostic = "synthetic-depth", PrimaryCausalTerminalContradiction? terminalContradiction = null)
        {
            PrimaryCausalTerminalEvidence? causal = includeTerminal ? new PrimaryCausalTerminalEvidence(Terminal(), state, "depth-cap", causalDepth) : (PrimaryCausalTerminalEvidence?)null;
            PrimaryCausalObserverDisabledWitness witness = includeWitness ? new PrimaryCausalObserverDisabledWitness(rawDiagnostic, witnessTerminal ?? Terminal(), state, witnessDecision, witnessDepth ?? causalDepth) : null;
            return new PrimaryCausalGateEvidence(state, validPrefix, causal, witness, routingOnly, childReturnOnly, observerOnly, true, terminalContradiction);
        }

        /// <summary>Creates a named table row for one independent gate outcome.</summary>
        private static PrimaryCausalGateCase Gate(string name, PrimaryCausalGateEvidence evidence, PrimaryCausalGateResult expected) => new PrimaryCausalGateCase(name, evidence, expected);

        /// <summary>Creates an unavailable parser identity fixture containing the supplied exact terminals.</summary>
        private static PrimaryCausalRun ParserIdentityFixture(PrimaryCausalTerminalInvocation[] terminals) => PrimaryCausalRun.UnavailableForParser(PrimaryCausalBaselineState.DepthCap, terminals);

        /// <summary>Requires parser identity fixtures to carry no runtime result or isolation evidence.</summary>
        private static void AssertParserFixtureUnavailable(PrimaryCausalRun run)
        {
            Assert.That(run.Availability, Is.EqualTo(PrimaryCausalAvailability.Unavailable)); Assert.That(run.UnavailableReason, Is.EqualTo("Parser-only evidence does not establish runtime availability.")); Assert.That(run.CompleteResult.HasValue, Is.False); Assert.That(run.ModeCommonCoreDigest, Is.EqualTo(0UL)); Assert.That(run.PreObserverStateDigest.HasValue, Is.False); Assert.That(run.PostObserverStateDigest.HasValue, Is.False); Assert.That(run.ObserverIsolationSnapshot.HasValue, Is.False);
        }

        /// <summary>Creates the one exact terminal identity used by parser tests.</summary>
        private static PrimaryCausalTerminalInvocation Terminal() => new PrimaryCausalTerminalInvocation("eta", true, 0.125d, 0.25d, 0.5d, 7);

        /// <summary>Returns the sole accepted observer-disabled depth grammar literal.</summary>
        private static string DepthText() => "numerical-limit primary depth axis=eta outer=0.125 interval=[0.25,0.5] coarse=1 fine=2 inheritedInnerError=2 ruleDelta=3 absoluteLimit=4 relativeLimit=5 error=5 limit=9 errorOverLimit=0.55555555555555558 depth=7";

        /// <summary>Returns a complete intrinsic depth-cap diagnostic whose arithmetic requires no repair.</summary>
        private static string NormalDepthText() => "numerical-limit primary depth axis=eta outer=0.125 interval=[0.25,0.5] coarse=1 fine=2 inheritedInnerError=2 ruleDelta=8 absoluteLimit=4 relativeLimit=5 error=10 limit=9 errorOverLimit=1.1111111111111112 depth=7";

        /// <summary>Passes a real parser-produced immutable witness into the fail-closed authorization gate.</summary>
        private static void AssertParsedWitnessGate(string text, PrimaryCausalTerminalContradiction? contradiction, PrimaryCausalGateResult expected)
        {
            Assert.That(PrimaryCausalDiagnosticParser.TryParseObserverDisabledDepth(text, out PrimaryCausalObserverDisabledWitness witness), Is.True);
            Assert.That(witness.DecisionOrder.HasValue, Is.False);
            var causal = new PrimaryCausalTerminalEvidence(witness.Terminal, witness.Category, witness.Decision, witness.Arithmetic);
            var evidence = new PrimaryCausalGateEvidence(witness.Category, true, causal, witness, false, false, false, true, contradiction);
            Assert.That(PrimaryCausalDecisionGate.Evaluate(evidence), Is.EqualTo(expected));
        }

        /// <summary>Pairs one immutable parser witness with causal evidence that differs in exactly one required gate fact.</summary>
        private static void AssertParsedWitnessGateRejectsCausalDifferences()
        {
            Assert.That(PrimaryCausalDiagnosticParser.TryParseObserverDisabledDepth(DepthText(), out PrimaryCausalObserverDisabledWitness witness), Is.True);
            AssertParsedWitnessGateRejects(witness, null);
            AssertParsedWitnessGateRejects(witness, CausalWithNonfiniteArithmetic(witness));
            AssertParsedWitnessGateRejects(witness, CausalWithDifferentIdentity(witness));
            AssertParsedWitnessGateRejects(witness, CausalWithDifferentArithmeticBit(witness));
            AssertParsedWitnessGateRejects(witness, CausalWithDifferentDecision(witness));
        }

        /// <summary>Requires one parser-produced witness and one otherwise-authorizable causal difference to fail closed.</summary>
        private static void AssertParsedWitnessGateRejects(PrimaryCausalObserverDisabledWitness witness, PrimaryCausalTerminalEvidence? causal)
        {
            var evidence = new PrimaryCausalGateEvidence(PrimaryCausalBaselineState.DepthCap, true, causal, witness, false, false, false, true, PrimaryCausalTerminalContradiction.DecisionControlFlow);
            Assert.That(PrimaryCausalDecisionGate.Evaluate(evidence), Is.EqualTo(PrimaryCausalGateResult.Reject));
        }

        /// <summary>Creates causal evidence with only one nonfinite arithmetic field.</summary>
        private static PrimaryCausalTerminalEvidence CausalWithNonfiniteArithmetic(PrimaryCausalObserverDisabledWitness witness)
        {
            PrimaryCausalDepthEvidence value = witness.Arithmetic;
            return new PrimaryCausalTerminalEvidence(witness.Terminal, witness.Category, witness.Decision, new PrimaryCausalDepthEvidence(value.Coarse, double.NaN, value.Inherited, value.Delta, value.Absolute, value.Relative, value.Error, value.Limit, value.ErrorOverLimit));
        }

        /// <summary>Creates causal evidence with only its terminal identity changed.</summary>
        private static PrimaryCausalTerminalEvidence CausalWithDifferentIdentity(PrimaryCausalObserverDisabledWitness witness)
        {
            PrimaryCausalTerminalInvocation terminal = new PrimaryCausalTerminalInvocation("theta", witness.Terminal.HasOuter, witness.Terminal.Outer, witness.Terminal.Left, witness.Terminal.Right, witness.Terminal.Depth);
            return new PrimaryCausalTerminalEvidence(terminal, witness.Category, witness.Decision, witness.Arithmetic);
        }

        /// <summary>Creates causal evidence with only one arithmetic bit changed.</summary>
        private static PrimaryCausalTerminalEvidence CausalWithDifferentArithmeticBit(PrimaryCausalObserverDisabledWitness witness)
        {
            PrimaryCausalDepthEvidence value = witness.Arithmetic;
            double coarse = FromBits(Bits(value.Coarse) + 1UL);
            return new PrimaryCausalTerminalEvidence(witness.Terminal, witness.Category, witness.Decision, new PrimaryCausalDepthEvidence(coarse, value.Fine, value.Inherited, value.Delta, value.Absolute, value.Relative, value.Error, value.Limit, value.ErrorOverLimit));
        }

        /// <summary>Creates causal evidence with only its terminal decision changed.</summary>
        private static PrimaryCausalTerminalEvidence CausalWithDifferentDecision(PrimaryCausalObserverDisabledWitness witness) => new PrimaryCausalTerminalEvidence(witness.Terminal, witness.Category, "accepted", witness.Arithmetic);

        /// <summary>Maps an observed gate result to its only admissible contradiction classification.</summary>
        private static PrimaryCausalTerminalContradiction? ExpectedContradiction(PrimaryCausalGateResult expected)
        {
            return expected == PrimaryCausalGateResult.AuthorizeTerminalSplitRepair ? PrimaryCausalTerminalContradiction.DecisionControlFlow : null;
        }

        /// <summary>Returns every exact grammar token required to occur once.</summary>
        private static string[] RequiredTokens() => new[] { "numerical-limit", "primary", "depth", "axis=eta", "outer=0.125", "interval=[0.25,0.5]", "coarse=1", "fine=2", "inheritedInnerError=2", "ruleDelta=3", "absoluteLimit=4", "relativeLimit=5", "error=5", "limit=9", "errorOverLimit=0.55555555555555558", "depth=7" };

        /// <summary>Returns every binary64 arithmetic field in the strict terminal grammar.</summary>
        private static string[] Binary64Fields() => new[] { "coarse", "fine", "inheritedInnerError", "ruleDelta", "absoluteLimit", "relativeLimit", "error", "limit", "errorOverLimit" };

        /// <summary>Gets the exact source spelling used by one arithmetic grammar field.</summary>
        private static string FieldValue(string field)
        {
            switch (field)
            {
                case "coarse": return "1";
                case "fine": case "inheritedInnerError": return "2";
                case "ruleDelta": return "3";
                case "absoluteLimit": return "4";
                case "relativeLimit": case "error": return "5";
                case "limit": return "9";
                default: return "0.55555555555555558";
            }
        }

        /// <summary>Returns all terminal states that are ineligible for an observer-disabled depth witness.</summary>
        private static PrimaryCausalBaselineState[] IneligibleStates() => new[] { PrimaryCausalBaselineState.Accepted, PrimaryCausalBaselineState.BudgetExhausted, PrimaryCausalBaselineState.EvaluationCap, PrimaryCausalBaselineState.GlobalError, PrimaryCausalBaselineState.Fault, PrimaryCausalBaselineState.Timeout, PrimaryCausalBaselineState.Other };

        /// <summary>Proves terminal lineage and the first contradiction survive attempt-prefix truncation.</summary>
        private static void AssertIndependentTerminalRetention()
        {
            var attempts = new PrimaryCausalAttemptCore[514];
            for (int index = 0; index < attempts.Length; index++) attempts[index] = new PrimaryCausalAttemptCore(index + 1, false, 1.0d, 1.0d, "eta", 0.0d, 0.0d, 0.0d, 0.0d, 1.0d, 0, 0, 0, 0.0d, 0.0d, 1.0d);
            var lineage = new PrimaryCausalLineageRecord[514];
            for (int index = 0; index < lineage.Length; index++) lineage[index] = new PrimaryCausalLineageRecord(index + 1, index, "root", "eta", 0.0d, index, "depth-cap");
            var chain = new[] { lineage[513] };
            var run = new PrimaryCausalRun(FixedCases[4].Invocation, PrimaryCausalMode.NoSelectionBudget, PrimaryCausalAvailability.Unavailable, "retention-unit-fixture", attempts, Array.Empty<ReservationObservation>(), lineage, new[] { Terminal() }, Array.Empty<PrimaryCausalCrossAxisEdge>(), Array.Empty<PrimaryCausalAggregate>(), chain, null, PrimaryCausalBaselineState.DepthCap, null, 0UL, null, null, null);
            lineage[513] = new PrimaryCausalLineageRecord(999, 0, "changed", "eta", 0.0d, 0, "changed"); chain[0] = lineage[513];
            Assert.That(run.Availability, Is.EqualTo(PrimaryCausalAvailability.Unavailable)); Assert.That(run.UnavailableReason, Is.EqualTo("retention-unit-fixture")); Assert.That(run.CompleteResult.HasValue, Is.False); Assert.That(run.ModeCommonCoreDigest, Is.EqualTo(0UL)); Assert.That(run.PreObserverStateDigest.HasValue, Is.False); Assert.That(run.PostObserverStateDigest.HasValue, Is.False); Assert.That(run.ObserverIsolationSnapshot.HasValue, Is.False); Assert.That(run.ObserverDisabledWitness, Is.Null);
            Assert.That(run.Attempts.Count, Is.EqualTo(513)); Assert.That(run.Lineage.Count, Is.EqualTo(513)); Assert.That(run.TerminalAncestorChain.Count, Is.EqualTo(1)); Assert.That(run.TerminalAncestorChain[0].InvocationId, Is.EqualTo(514)); Assert.That(run.TerminalAncestorChain[0].SameAxisParentId, Is.EqualTo(513)); Assert.That(run.TerminalState, Is.EqualTo(PrimaryCausalBaselineState.DepthCap));
        }

        /// <summary>Requires a parser rejection without accepting a partial or ambiguous recovery.</summary>
        private static void AssertRejected(string text, PrimaryCausalRun run) => Assert.That(PrimaryCausalDiagnosticParser.TryMatchObserverDisabledDepth(text, run, out _), Is.False);

        /// <summary>Converts an exact unsigned binary64 bit literal to a double.</summary>
        private static double FromBits(ulong value) => BitConverter.Int64BitsToDouble(unchecked((long)value));

        /// <summary>Gets unsigned binary64 bits without numeric normalization.</summary>
        private static ulong Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value));

        /// <summary>Stores one fixed causal matrix row and immutable source memberships.</summary>
        private readonly struct PrimaryCausalCase
        {
            /// <summary>Initializes one literal causal matrix row.</summary>
            internal PrimaryCausalCase(PrimaryCausalInvocation invocation, bool training, bool validation, bool original, bool stress, int completedKernelSamples)
            {
                Invocation = invocation; Training = training; Validation = validation; Original = original; Stress = stress; CompletedKernelSamples = completedKernelSamples;
            }

            /// <summary>Gets the exact primary input and branch.</summary>
            internal PrimaryCausalInvocation Invocation { get; }
            /// <summary>Gets the frozen training membership.</summary>
            internal bool Training { get; }
            /// <summary>Gets the frozen validation membership.</summary>
            internal bool Validation { get; }
            /// <summary>Gets the frozen original membership.</summary>
            internal bool Original { get; }
            /// <summary>Gets the frozen stress membership.</summary>
            internal bool Stress { get; }
            /// <summary>Gets known completed kernel work for Accepted controls, otherwise zero.</summary>
            internal int CompletedKernelSamples { get; }
        }

        /// <summary>Stores one named synthetic decision-gate expectation.</summary>
        private readonly struct PrimaryCausalGateCase
        {
            /// <summary>Initializes one table-driven gate case.</summary>
            internal PrimaryCausalGateCase(string name, PrimaryCausalGateEvidence evidence, PrimaryCausalGateResult expected) { Name = name; Evidence = evidence; Expected = expected; }
            /// <summary>Gets the diagnostic table name.</summary>
            internal string Name { get; }
            /// <summary>Gets independent gate input facts.</summary>
            internal PrimaryCausalGateEvidence Evidence { get; }
            /// <summary>Gets the required fail-closed result.</summary>
            internal PrimaryCausalGateResult Expected { get; }
        }
    }
}
