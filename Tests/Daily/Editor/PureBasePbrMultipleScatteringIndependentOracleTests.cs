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

// Tests the independent-oracle contract without using product or retained numerical helpers.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines only the test data required for one exclusive, fail-closed decision.</summary>
    internal readonly struct IndependentOracleDecisionEvidence
    {
        /// <summary>Initializes immutable synthetic or observed decision evidence.</summary>
        internal IndependentOracleDecisionEvidence(bool candidateIndependent, bool witnessIndependent, bool candidateFinite, bool witnessFinite, bool candidateAccepted, IndependentOracleCandidateUncertaintyEvidence candidateUncertainty, bool candidateAnalytical, bool witnessAnalytical, bool witnessConverged, bool composedAgreement, bool characterizationEligible)
        { CandidateIndependent = candidateIndependent; WitnessIndependent = witnessIndependent; CandidateFinite = candidateFinite; WitnessFinite = witnessFinite; CandidateAccepted = candidateAccepted; CandidateUncertainty = candidateUncertainty; CandidateAnalytical = candidateAnalytical; WitnessAnalytical = witnessAnalytical; WitnessConverged = witnessConverged; ComposedAgreement = composedAgreement; CharacterizationEligible = characterizationEligible; }
        /// <summary>Gets whether the candidate passed the dependency audit.</summary>
        internal bool CandidateIndependent { get; }
        /// <summary>Gets whether the witness passed the dependency audit.</summary>
        internal bool WitnessIndependent { get; }
        /// <summary>Gets whether candidate numerical components were finite.</summary>
        internal bool CandidateFinite { get; }
        /// <summary>Gets whether witness numerical components were finite.</summary>
        internal bool WitnessFinite { get; }
        /// <summary>Gets whether the candidate avoided every resource and numerical stop.</summary>
        internal bool CandidateAccepted { get; }
        /// <summary>Gets whether candidate uncertainty was computed and met the fixed base-target bound.</summary>
        internal IndependentOracleCandidateUncertaintyEvidence CandidateUncertainty { get; }
        /// <summary>Gets whether the candidate passed its independent analytical control.</summary>
        internal bool CandidateAnalytical { get; }
        /// <summary>Gets whether the witness passed its independent analytical control.</summary>
        internal bool WitnessAnalytical { get; }
        /// <summary>Gets whether the witness convergence model was applicable.</summary>
        internal bool WitnessConverged { get; }
        /// <summary>Gets whether the single composed comparison passed.</summary>
        internal bool ComposedAgreement { get; }
        /// <summary>Gets whether characterization eligibility evidence is complete.</summary>
        internal bool CharacterizationEligible { get; }
    }

    /// <summary>Stores test-only candidate base/strict evidence and its fail-closed acceptance result.</summary>
    internal readonly struct IndependentOracleCandidateAcceptanceEvidence
    {
        /// <summary>Initializes immutable candidate convergence evidence without producing a numerical candidate result.</summary>
        internal IndependentOracleCandidateAcceptanceEvidence(double strictError, double baseValue, double strictValue)
        {
            StrictError = strictError; BaseValue = baseValue; StrictValue = strictValue;
            Uncertainty = IndependentOracleContract.CandidateUncertainty(strictError, baseValue, strictValue);
            Accepted = IndependentOracleContract.CandidateUncertaintyPass(Uncertainty);
        }
        /// <summary>Gets the stricter candidate error estimate.</summary>
        internal double StrictError { get; }
        /// <summary>Gets the base-target candidate estimate.</summary>
        internal double BaseValue { get; }
        /// <summary>Gets the stricter-target candidate estimate.</summary>
        internal double StrictValue { get; }
        /// <summary>Gets the composed base/strict candidate uncertainty.</summary>
        internal double Uncertainty { get; }
        /// <summary>Gets whether finite nonnegative uncertainty satisfies the fixed base target.</summary>
        internal bool Accepted { get; }
    }

    /// <summary>Tests the frozen independent directional-albedo oracle contract and unavailable entry boundaries.</summary>
    public sealed class PureBasePbrMultipleScatteringIndependentOracleTests
    {
        /// <summary>Compares both transformed integrands with a separately expanded unreduced formula at fixed raw inputs.</summary>
        [Test]
        public void IndependentOracleContractFreezesUnreducedIntegralAndTransforms()
        {
            foreach (IndependentOracleBranch branch in new[] { IndependentOracleBranch.Normal, IndependentOracleBranch.Switch })
            {
                var input = new IndependentOracleInput(0.25d, 0.5d, branch); double r = 0.37d; double theta = 1.23d; double u = Math.Pow(Math.Sin(Math.PI * r * 0.5d), 2.0d);
                Assert.That(IndependentOracleContract.EvaluateCandidateTransform(input, r, theta), Is.EqualTo(2.0d * UnreducedFormula(input, u, theta) * Math.PI * Math.Sin(Math.PI * r) * 0.5d).Within(1.0e-15d));
                Assert.That(IndependentOracleContract.EvaluateWitnessTransform(input, 0.61d, theta), Is.EqualTo(UnreducedFormula(input, 0.61d * 0.61d, theta) * 1.22d).Within(1.0e-15d));
            }
        }

        /// <summary>Freezes transform domains, endpoint measures, Jacobians, and the even half-azimuth reduction.</summary>
        [Test]
        public void IndependentOracleContractFreezesTransformDomainsMeasuresAndSymmetry()
        {
            var input = new IndependentOracleInput(0.25d, 0.5d, IndependentOracleBranch.Normal); double phi = 0.73d;
            Assert.That(IndependentOracleContract.CandidateDomainPass(0.0d, 0.0d), Is.True); Assert.That(IndependentOracleContract.CandidateDomainPass(1.0d, Math.PI), Is.True);
            Assert.That(IndependentOracleContract.CandidateDomainPass(-BitConverter.Int64BitsToDouble(1L), 0.0d), Is.False); Assert.That(IndependentOracleContract.CandidateDomainPass(0.5d, NextUp(Math.PI)), Is.False);
            Assert.That(IndependentOracleContract.WitnessDomainPass(0.0d, 0.0d), Is.True); Assert.That(IndependentOracleContract.WitnessDomainPass(1.0d, 2.0d * Math.PI), Is.True);
            Assert.That(IndependentOracleContract.WitnessDomainPass(NextUp(1.0d), 0.0d), Is.False); Assert.That(IndependentOracleContract.WitnessDomainPass(0.5d, NextUp(2.0d * Math.PI)), Is.False);
            Assert.That(IndependentOracleContract.CandidateJacobian(0.0d), Is.EqualTo(0.0d).Within(1.0e-15d)); Assert.That(IndependentOracleContract.CandidateJacobian(1.0d), Is.EqualTo(0.0d).Within(1.0e-15d));
            Assert.That(IndependentOracleContract.WitnessJacobian(0.0d), Is.EqualTo(0.0d)); Assert.That(IndependentOracleContract.WitnessJacobian(1.0d), Is.EqualTo(2.0d));
            Assert.That(UnreducedFormula(input, 0.4d, phi), Is.EqualTo(UnreducedFormula(input, 0.4d, 2.0d * Math.PI - phi)).Within(1.0e-15d));
            Assert.That(IndependentOracleContract.EvaluateCandidateTransform(input, 0.0d, phi), Is.EqualTo(0.0d).Within(1.0e-15d)); Assert.That(IndependentOracleContract.EvaluateWitnessTransform(input, 0.0d, phi), Is.EqualTo(0.0d));
        }

        /// <summary>Freezes root reconstruction, semantic ordering, atomic coverage, leaf accounting, and witness convergence terms.</summary>
        [Test]
        public void IndependentOracleContractFreezesRootsSchedulingAndConvergence()
        {
            IndependentOracleThetaPartition missing = IndependentOracleContract.DeriveCandidateThetaPartition(new IndependentOracleInput(1.0d, 1.0d, IndependentOracleBranch.Normal), 0.0d);
            IndependentOracleThetaPartition residual = IndependentOracleContract.DeriveCandidateThetaPartition(new IndependentOracleInput(0.089d, 0.0d, IndependentOracleBranch.Normal), 0.0d);
            IndependentOracleThetaPartition distribution = IndependentOracleContract.DeriveCandidateThetaPartition(new IndependentOracleInput(0.089d, 0.5d, IndependentOracleBranch.Normal), 0.5d);
            Assert.That(missing.Count, Is.EqualTo(0)); Assert.That(missing.StopState, Is.EqualTo(LightSpaceOracleStopState.Accepted), "true missing roots remain valid absences"); Assert.That(residual.StopState, Is.EqualTo(LightSpaceOracleStopState.RootTopologyFailure), "interior residual failure"); Assert.That(distribution.Count, Is.EqualTo(1), "distribution root"); Assert.That(distribution.StopState, Is.EqualTo(LightSpaceOracleStopState.Accepted)); Assert.That(distribution.First.Kind, Is.EqualTo(IndependentOracleRootKind.Distribution));
            AssertReconstructedRoot(distribution.First, 0.5d, 0.5d, 0.089d);
            AssertAtomicCoverage(IndependentOracleContract.ThetaBoundaries(missing)); AssertAtomicCoverage(IndependentOracleContract.ThetaBoundaries(distribution));
            var tied = IndependentOracleContract.CanonicalizeThetaRoots(new IndependentOracleThetaRoot(IndependentOracleRootKind.Guard, 1.0d, 0.0d, true), new IndependentOracleThetaRoot(IndependentOracleRootKind.Distribution, NextUp(1.0d), 0.0d, true));
            Assert.That(tied.Count, Is.EqualTo(1)); Assert.That(tied.First.Kind, Is.EqualTo(IndependentOracleRootKind.Guard));
            var residualFailure = IndependentOracleContract.CanonicalizeThetaRoots(new IndependentOracleThetaRoot(IndependentOracleRootKind.Guard, 1.0d, 0.0d, true, false), new IndependentOracleThetaRoot(IndependentOracleRootKind.Distribution, double.NaN, double.NaN, false));
            var orderingFailure = new IndependentOracleThetaPartition(new IndependentOracleThetaRoot(IndependentOracleRootKind.Guard, 2.0d, 0.0d, true), new IndependentOracleThetaRoot(IndependentOracleRootKind.Distribution, 1.0d, 0.0d, true), 2);
            Assert.That(residualFailure.StopState, Is.EqualTo(LightSpaceOracleStopState.RootTopologyFailure)); Assert.That(orderingFailure.StopState, Is.EqualTo(LightSpaceOracleStopState.RootTopologyFailure)); Assert.That(IndependentOracleContract.ThetaBoundaries(residualFailure), Is.Empty);
            Assert.That(IndependentOracleContract.CompareLeafKeys(new IndependentOracleLeafKey(2.0d, new IndependentOracleCanonicalPath(4, 3UL)), new IndependentOracleLeafKey(1.0d, new IndependentOracleCanonicalPath(0, 0UL))), Is.LessThan(0)); Assert.That(IndependentOracleContract.CompareLeafKeys(new IndependentOracleLeafKey(1.0d, new IndependentOracleCanonicalPath(1, 1UL)), new IndependentOracleLeafKey(1.0d, new IndependentOracleCanonicalPath(2, 1UL))), Is.LessThan(0));
            Assert.That(IndependentOracleContract.CandidateLeafError(1.0d, 1.25d, 0.75d), Is.EqualTo(1.0d)); Assert.That(IndependentOracleContract.PairwiseReduce(new[] { 1.0e16d, 1.0d, -1.0e16d }), Is.EqualTo(0.0d));
            Assert.That(IndependentOracleContract.TryPositiveGeometricTail(8.0d, 2.0d, out double ratio, out double tail), Is.True); Assert.That(ratio, Is.EqualTo(4.0d)); Assert.That(tail, Is.EqualTo(2.0d / 3.0d));
            Assert.That(IndependentOracleContract.TryPositiveGeometricTail(2.0d, 0.0d, out _, out _), Is.False); double uncertainty = IndependentOracleContract.WitnessUncertainty(1.0e-7d, 1.0e-7d, 1.0e-7d, 1.0e-7d);
            Assert.That(IndependentOracleContract.WitnessUncertaintyPass(uncertainty, 1.0e-7d, 1.0d), Is.True); Assert.That(IndependentOracleContract.WitnessUncertaintyPass(uncertainty, IndependentOracleContract.Budget(1.0d), 1.0d), Is.False); Assert.That(IndependentOracleContract.CandidateUncertainty(1.0d, 4.0d, 2.0d), Is.EqualTo(2.0d));
        }

        /// <summary>Freezes exact representative ordering, ceilings, candidate rule identities, and witness work without retry rungs.</summary>
        [Test]
        public void IndependentOracleContractFreezesRepresentativeRowsOrderingAndLimits()
        {
            IReadOnlyList<IndependentOracleRepresentativeRow> rows = IndependentOracleContract.RepresentativeRows;
            (ulong PBits, ulong NdotVBits, IndependentOracleBranch Branch, IndependentOracleRepresentativeRole Role, IndependentOracleLegacyOutcome LegacyOutcome)[] expectedRows =
            {
                (0x3FF0000000000000UL, 0x3FF0000000000000UL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.AcceptedControl, IndependentOracleLegacyOutcome.Accepted), (0x3FF0000000000000UL, 0x3FF0000000000000UL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.AcceptedControl, IndependentOracleLegacyOutcome.Accepted),
                (0x3FB6C8B439581062UL, 0x0000000000000000UL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.BudgetMinimumGrazing, IndependentOracleLegacyOutcome.BudgetExhausted), (0x3FB6C8B439581062UL, 0x0000000000000000UL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.BudgetMinimumGrazing, IndependentOracleLegacyOutcome.BudgetExhausted),
                (0x3FE0000000000000UL, 0x3FF0000000000000UL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.BudgetMidRoughness, IndependentOracleLegacyOutcome.BudgetExhausted), (0x3FE0000000000000UL, 0x3FF0000000000000UL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.BudgetMidRoughness, IndependentOracleLegacyOutcome.BudgetExhausted),
                (0x3FF0000000000000UL, 0x3FEF746EA3A45F8AUL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.BudgetInterior, IndependentOracleLegacyOutcome.BudgetExhausted), (0x3FF0000000000000UL, 0x3FEF746EA3A45F8AUL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.BudgetInterior, IndependentOracleLegacyOutcome.BudgetExhausted),
                (0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.DepthGrazing, IndependentOracleLegacyOutcome.DepthCap), (0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.DepthGrazing, IndependentOracleLegacyOutcome.DepthCap)
            };
            Assert.That(rows.Count, Is.EqualTo(10)); Assert.That(IndependentOracleContract.MaxDepth, Is.EqualTo(22)); Assert.That(IndependentOracleContract.MaxPanels, Is.EqualTo(262144)); Assert.That(IndependentOracleContract.MaxEvaluations, Is.EqualTo(4000000));
            Assert.That(IndependentOracleContract.CandidateClenshawCurtisOrders, Is.EqualTo(new[] { 9, 17 })); Assert.That(IndependentOracleContract.CandidateFejerOrders, Is.EqualTo(new[] { 17, 33 })); Assert.That(IndependentOracleContract.WitnessScalarEvaluations, Is.EqualTo(3912482));
            Assert.That(rows.Count, Is.EqualTo(expectedRows.Length)); for (int index = 0; index < expectedRows.Length; index++) { var expected = expectedRows[index]; var actual = rows[index]; Assert.That(actual.PBits, Is.EqualTo(expected.PBits)); Assert.That(actual.NdotVBits, Is.EqualTo(expected.NdotVBits)); Assert.That(actual.Input.Branch, Is.EqualTo(expected.Branch)); Assert.That(actual.Role, Is.EqualTo(expected.Role)); Assert.That(actual.LegacyOutcome, Is.EqualTo(expected.LegacyOutcome)); Assert.That(unchecked((ulong)BitConverter.DoubleToInt64Bits(actual.Input.P)), Is.EqualTo(expected.PBits)); Assert.That(unchecked((ulong)BitConverter.DoubleToInt64Bits(actual.Input.NdotV)), Is.EqualTo(expected.NdotVBits)); }
            (int TOrder, int PhiOrder, bool Shifted)[] expectedResolutions = { (127, 509, false), (251, 1021, false), (503, 2039, false), (127, 2039, false), (251, 2039, false), (503, 509, false), (503, 1021, false), (503, 2039, true) };
            Assert.That(IndependentOracleContract.WitnessResolutions.Count, Is.EqualTo(expectedResolutions.Length)); for (int index = 0; index < expectedResolutions.Length; index++) { var expected = expectedResolutions[index]; var actual = IndependentOracleContract.WitnessResolutions[index]; Assert.That(actual.TOrder, Is.EqualTo(expected.TOrder)); Assert.That(actual.PhiOrder, Is.EqualTo(expected.PhiOrder)); Assert.That(actual.Shifted, Is.EqualTo(expected.Shifted)); }
            ulong[] expectedCosines = { 0x0000000000000000UL, 0x3FE0000000000000UL, 0x3FEF746EA3A45F8AUL, 0x3FF0000000000000UL };
            Assert.That(IndependentOracleContract.AnalyticalViewCosines.Count, Is.EqualTo(expectedCosines.Length)); for (int index = 0; index < expectedCosines.Length; index++) Assert.That(unchecked((ulong)BitConverter.DoubleToInt64Bits(IndependentOracleContract.AnalyticalViewCosines[index])), Is.EqualTo(expectedCosines[index]));
            Assert.That(IndependentOracleContract.RepresentativeRows, Is.SameAs(rows)); Assert.That(IndependentOracleContract.RepresentativeRows[0], Is.EqualTo(rows[0]));
            AssertReadOnly(rows, rows[0]); AssertReadOnly(IndependentOracleContract.CandidateClenshawCurtisOrders, 9); AssertReadOnly(IndependentOracleContract.CandidateFejerOrders, 17);
            AssertReadOnly(IndependentOracleContract.WitnessResolutions, IndependentOracleContract.WitnessResolutions[0]); AssertReadOnly(IndependentOracleContract.AnalyticalViewCosines, 0.0d);
        }

        /// <summary>Freezes nested candidate and witness primitive identities, including endpoint ownership and exact accumulation probes.</summary>
        [Test]
        public void IndependentOraclePrimitiveContractsBindRulesAndAccumulator()
        {
            AssertQuadrature(ClenshawCurtis(9), 8); AssertQuadrature(ClenshawCurtis(17), 16); AssertQuadrature(FejerII(17), 16); AssertQuadrature(FejerII(33), 32);
            Assert.That(FejerII(17)[0].X, Is.LessThan(1.0d)); Assert.That(FejerII(17)[16].X, Is.GreaterThan(-1.0d)); AssertLegendre(127); AssertLegendre(251); AssertLegendre(503);
            foreach (int count in new[] { 509, 1021, 2039 }) { AssertPeriodic(count, 0.5d); AssertPeriodic(count, 0.25d); }
            AssertDoubleDouble(0x4340000000000000UL, 0x3FF0000000000000UL, false); AssertDoubleDouble(0x4340000000000000UL, 0x3FF0000000000000UL, true); AssertDoubleDouble(0x4330000000000000UL, 0x3FE0000000000000UL, false);
        }

        /// <summary>Requires both exact-zero convergence deltas to retain positive, finite adjacent-input sensitivity evidence.</summary>
        [Test]
        public void IndependentOracleWitnessExactZeroTailRequiresSensitivityEvidence()
        {
            Assert.That(IndependentOracleContract.TryGeometricTail(0.0d, 0.0d, BitConverter.Int64BitsToDouble(1L), 0.0d, 0.0d, out double ratio, out double tail), Is.True); Assert.That(ratio, Is.EqualTo(double.PositiveInfinity)); Assert.That(tail, Is.EqualTo(0.0d));
            Assert.That(IndependentOracleContract.TryGeometricTail(0.0d, 0.0d, 0.0d, 0.0d, 0.0d, out _, out _), Is.False); Assert.That(IndependentOracleContract.TryGeometricTail(0.0d, 0.0d, double.NaN, 0.0d, 0.0d, out _, out _), Is.False);
            Assert.That(IndependentOracleContract.TryGeometricTail(0.0d, 0.0d, 1.0d, double.NaN, 0.0d, out _, out _), Is.False); Assert.That(IndependentOracleContract.TryGeometricTail(0.0d, 0.0d, 1.0d, 0.0d, double.PositiveInfinity, out _, out _), Is.False);
        }

        /// <summary>Binds candidate base/strict uncertainty to fail-closed candidate acceptance evidence.</summary>
        [Test]
        public void IndependentOracleCandidateAcceptanceRequiresFiniteBoundedUncertainty()
        {
            IndependentOracleCandidateAcceptanceEvidence exact = new IndependentOracleCandidateAcceptanceEvidence(IndependentOracleContract.CandidateBaseTarget, 1.0d, 1.0d);
            IndependentOracleCandidateAcceptanceEvidence over = new IndependentOracleCandidateAcceptanceEvidence(NextUp(IndependentOracleContract.CandidateBaseTarget), 1.0d, 1.0d);
            IndependentOracleCandidateAcceptanceEvidence nonFinite = new IndependentOracleCandidateAcceptanceEvidence(double.NaN, 1.0d, 1.0d);
            IndependentOracleCandidateAcceptanceEvidence negative = new IndependentOracleCandidateAcceptanceEvidence(-1.0d, 1.0d, 1.0d);
            Assert.That(exact.Accepted, Is.True); Assert.That(exact.Uncertainty, Is.EqualTo(IndependentOracleContract.CandidateBaseTarget)); Assert.That(over.Accepted, Is.False); Assert.That(nonFinite.Accepted, Is.False); Assert.That(negative.Accepted, Is.False);
            Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Unavailable, true, true, true, true, false)), Is.EqualTo(IndependentOracleDecision.B));
            Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Rejected, true, true, true, true, false)), Is.EqualTo(IndependentOracleDecision.B));
        }

        /// <summary>Rejects negative errors, tails, phase deltas, and uncertainties at every comparison boundary.</summary>
        [Test]
        public void IndependentOracleUncertaintyBoundariesRejectNegativeValues()
        {
            Assert.That(double.IsNaN(IndependentOracleContract.CandidateLeafError(1.0d, 2.0d, -1.0d)), Is.True); Assert.That(IndependentOracleContract.TryPositiveGeometricTail(-8.0d, 2.0d, out _, out _), Is.False);
            Assert.That(double.IsNaN(IndependentOracleContract.WitnessUncertainty(-1.0d, 0.0d, 0.0d, 0.0d)), Is.True); Assert.That(double.IsNaN(IndependentOracleContract.WitnessUncertainty(0.0d, 0.0d, 0.0d, -1.0d)), Is.True);
            Assert.That(IndependentOracleContract.WitnessUncertaintyPass(-1.0d, 0.0d, 1.0d), Is.False); Assert.That(IndependentOracleContract.WitnessUncertaintyPass(0.0d, -1.0d, 1.0d), Is.False);
            Assert.That(double.IsNaN(IndependentOracleContract.CandidateUncertainty(-1.0d, 1.0d, 1.0d)), Is.True); Assert.That(IndependentOracleContract.ComposedComparisonPass(1.0d, 1.0d, -1.0d, 0.0d), Is.False); Assert.That(IndependentOracleContract.ComposedComparisonPass(1.0d, 1.0d, 0.0d, -1.0d), Is.False);
            Assert.That(IndependentOracleContract.CandidateAnalyticalPass(1.0d, 1.0d, -1.0d), Is.False); Assert.That(IndependentOracleContract.WitnessAnalyticalPass(1.0d, 1.0d, -1.0d), Is.False);
        }

        /// <summary>Freezes both p=1 composed analytical bounds at equality, one ULP over equality, and nonfinite rejection.</summary>
        [Test]
        public void IndependentOracleAnalyticalBenchmarkUsesFixedComposedBounds()
        {
            foreach (double v in IndependentOracleContract.AnalyticalViewCosines) foreach (IndependentOracleBranch branch in new[] { IndependentOracleBranch.Normal, IndependentOracleBranch.Switch })
            {
                double analytical = IndependentOracleContract.EvaluateP1Analytical(v, branch); double candidateBoundary = IndependentOracleContract.AbsoluteBudget - IndependentOracleContract.ComparisonAllowance(Math.Abs(analytical));
                Assert.That(IndependentOracleContract.CandidateAnalyticalPass(analytical, analytical, candidateBoundary), Is.True); Assert.That(IndependentOracleContract.CandidateAnalyticalPass(analytical, analytical, NextUp(candidateBoundary)), Is.False); Assert.That(IndependentOracleContract.CandidateAnalyticalPass(analytical, double.NaN, 0.0d), Is.False);
                double witnessBudget = IndependentOracleContract.Budget(Math.Abs(analytical)); double witnessBoundary = witnessBudget - IndependentOracleContract.ComparisonAllowance(Math.Abs(analytical));
                Assert.That(IndependentOracleContract.WitnessAnalyticalPass(analytical, analytical, witnessBoundary), Is.True); Assert.That(IndependentOracleContract.WitnessAnalyticalPass(analytical, analytical, NextUp(witnessBoundary)), Is.False); Assert.That(IndependentOracleContract.WitnessAnalyticalPass(analytical, analytical, double.PositiveInfinity), Is.False);
            }
        }

        /// <summary>Exercises every exclusive decision, including hard stops, nonfinite evidence, dependency violations, and exact budget boundaries.</summary>
        [Test]
        public void IndependentOracleDecisionTableFailsClosed()
        {
            Assert.That(IndependentOracleContract.ComposedComparisonPass(1.0d, 1.0d, 0.0d, IndependentOracleContract.Budget(1.0d) - IndependentOracleContract.ComparisonAllowance(1.0d)), Is.True);
            Assert.That(IndependentOracleContract.ComposedComparisonPass(1.0d, 1.0d, 0.0d, NextUp(IndependentOracleContract.Budget(1.0d) - IndependentOracleContract.ComparisonAllowance(1.0d))), Is.False); Assert.That(IndependentOracleContract.ComposedComparisonPass(double.NaN, 1.0d, 0.0d, 0.0d), Is.False);
            Assert.That(IndependentOracleContract.Decide(Evidence(false, true, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, true, true, false)), Is.EqualTo(IndependentOracleDecision.B)); Assert.That(IndependentOracleContract.Decide(Evidence(true, true, false, true, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, true, true, false)), Is.EqualTo(IndependentOracleDecision.B)); Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, true, false, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, true, true, false)), Is.EqualTo(IndependentOracleDecision.B));
            Assert.That(IndependentOracleContract.Decide(Evidence(true, false, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, true, true, false)), Is.EqualTo(IndependentOracleDecision.C)); Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, false, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, true, true, false)), Is.EqualTo(IndependentOracleDecision.C)); Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, false, true, true, false)), Is.EqualTo(IndependentOracleDecision.C)); Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, false, true, false)), Is.EqualTo(IndependentOracleDecision.C));
            Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, true, false, false)), Is.EqualTo(IndependentOracleDecision.D)); Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, true, true, false)), Is.EqualTo(IndependentOracleDecision.A)); Assert.That(IndependentOracleContract.Decide(Evidence(true, true, true, true, true, IndependentOracleCandidateUncertaintyEvidence.Accepted, true, true, true, true, true)), Is.EqualTo(IndependentOracleDecision.E));
        }

        /// <summary>Freezes ordered separate reductions, finite acceptance, and deterministic unresolved-error handling.</summary>
        [Test]
        public void IndependentOracleGlobalAggregationSeparatesValuesErrorsAndAcceptance()
        {
            var leaves = new[] { Leaf(2, 1UL, 1.0e16d, 1.0d, false), Leaf(2, 0UL, 1.0d, 2.0d, true), Leaf(1, 1UL, -1.0e16d, 3.0d, true) };
            LightSpaceOracleAggregationResult exact = LightSpaceOracleAggregationContract.Aggregate(leaves, 6.0d);
            Assert.That(exact.Value, Is.EqualTo(0.0d)); Assert.That(exact.Error, Is.EqualTo(6.0d)); Assert.That(exact.Disposition, Is.EqualTo(LightSpaceOracleAggregationDisposition.Accepted));
            var oneUlpOver = new[] { Leaf(2, 1UL, 1.0e16d, 1.0d, true), Leaf(2, 0UL, 1.0d, 2.0d, true), Leaf(1, 1UL, -1.0e16d, NextUp(NextUp(3.0d)), true) };
            LightSpaceOracleAggregationResult over = LightSpaceOracleAggregationContract.Aggregate(oneUlpOver, 6.0d);
            Assert.That(over.Error, Is.EqualTo(NextUp(6.0d))); Assert.That(over.Disposition, Is.EqualTo(LightSpaceOracleAggregationDisposition.RefinementRequired)); Assert.That(over.RefinementPath.Depth, Is.EqualTo(1)); Assert.That(over.RefinementPath.BinaryPath, Is.EqualTo(1UL));
            LightSpaceOracleAggregationResult nonFinite = LightSpaceOracleAggregationContract.Aggregate(new[] { Leaf(0, 0UL, double.NaN, 1.0d, true) }, 1.0d);
            LightSpaceOracleAggregationResult duplicate = LightSpaceOracleAggregationContract.Aggregate(new[] { Leaf(1, 1UL, 1.0d, 1.0d, true), Leaf(1, 1UL, 1.0d, 1.0d, true) }, 1.0d);
            LightSpaceOracleAggregationResult exhausted = LightSpaceOracleAggregationContract.Aggregate(new[] { Leaf(0, 0UL, 1.0d, 1.0d, false) }, 0.0d);
            Assert.That(nonFinite.Disposition, Is.EqualTo(LightSpaceOracleAggregationDisposition.NonFiniteLeaf)); Assert.That(duplicate.Disposition, Is.EqualTo(LightSpaceOracleAggregationDisposition.GlobalError)); Assert.That(exhausted.Disposition, Is.EqualTo(LightSpaceOracleAggregationDisposition.GlobalError));
        }

        /// <summary>Freezes root and child first-failure transactions, exact counters, and uncommitted topology.</summary>
        [Test]
        public void IndependentOracleHardStopsPreserveFirstFailureAndNoPartialResult()
        {
            AssertHardStop(CreateState(4, 2, 4), "root-cap", 0, 0, LightSpaceOracleStopState.EvaluationCap, "root", string.Empty, 3, 1, 2, 0, string.Empty);
            AssertHardStop(CreateRootFailureState(4, 4, 4, 2), "root-nonfinite", 0, 0, LightSpaceOracleStopState.NonFiniteSample, "root", string.Empty, 2, 1, 2, 0, string.Empty);
            AssertHardStop(CreateState(4, 4, 4), "child-zero-cap", 0, 0, LightSpaceOracleStopState.EvaluationCap, "child-0", "0", 1, 3, 4, 1, "root");
            AssertHardStop(CreateState(4, 6, 4), "child-zero-nonfinite", 2, 0, LightSpaceOracleStopState.NonFiniteSample, "child-0", "0", 2, 3, 6, 1, "root");
            AssertHardStop(CreateState(4, 9, 4), "child-one-cap", 0, 0, LightSpaceOracleStopState.EvaluationCap, "child-1", "1", 2, 3, 9, 1, "root");
            AssertHardStop(CreateState(4, 10, 4), "child-one-nonfinite", 0, 2, LightSpaceOracleStopState.NonFiniteSample, "child-1", "1", 2, 3, 10, 1, "root");
        }

        /// <summary>Freezes depth and panel hard stops before child reservations or scalar work begin.</summary>
        [Test]
        public void IndependentOracleDepthAndPanelStopsOccurBeforeRefinementWork()
        {
            var depth = new LightSpaceOracleScriptedState(0, 8, 8, 2); depth.EvaluateRoot(0); depth.RefineRoot(0, 0);
            var panel = new LightSpaceOracleScriptedState(4, 2, 8, 2); panel.EvaluateRoot(0); panel.RefineRoot(0, 0);
            var rootPanel = new LightSpaceOracleScriptedState(4, 0, 8, 2); rootPanel.EvaluateRoot(0);
            AssertStop(depth, LightSpaceOracleStopState.DepthCap, "root", string.Empty, 0, 1, 2, 0, "root"); AssertStop(panel, LightSpaceOracleStopState.PanelCap, "root", string.Empty, 0, 1, 2, 0, "root");
            AssertStop(rootPanel, LightSpaceOracleStopState.PanelCap, "root", string.Empty, 0, 0, 0, 0, string.Empty);
        }

        /// <summary>Requires successful refinement to replace a retained parent with two complete nonoverlapping child intervals.</summary>
        [Test]
        public void IndependentOracleSchedulerCommitsChildrenAsCompleteRootPartition()
        {
            var state = new LightSpaceOracleScriptedState(4, 8, 16, 2); state.EvaluateRoot(0);
            Assert.That(state.CommittedLeaves.Count, Is.EqualTo(1)); Assert.That(state.CommittedLeaves[0].Left, Is.EqualTo(0.0d)); Assert.That(state.CommittedLeaves[0].Right, Is.EqualTo(1.0d));
            state.RefineRoot(0, 0);
            Assert.That(state.StopState, Is.EqualTo(LightSpaceOracleStopState.Accepted)); Assert.That(state.CommittedLeaves.Count, Is.EqualTo(2));
            Assert.That(state.CommittedLeaves[0].Path.Depth, Is.EqualTo(1)); Assert.That(state.CommittedLeaves[0].Path.BinaryPath, Is.EqualTo(0UL)); Assert.That(state.CommittedLeaves[0].Left, Is.EqualTo(0.0d)); Assert.That(state.CommittedLeaves[0].Right, Is.EqualTo(0.5d));
            Assert.That(state.CommittedLeaves[1].Path.Depth, Is.EqualTo(1)); Assert.That(state.CommittedLeaves[1].Path.BinaryPath, Is.EqualTo(1UL)); Assert.That(state.CommittedLeaves[1].Left, Is.EqualTo(0.5d)); Assert.That(state.CommittedLeaves[1].Right, Is.EqualTo(1.0d));
            Assert.That(LightSpaceOracleTopologyContract.IsCompleteNonOverlappingPartition(state.CommittedLeaves), Is.True); Assert.That(LightSpaceOracleTopologyContract.IsCompleteNonOverlappingPartition(new[] { state.CommittedLeaves[0], new LightSpaceOracleCommittedLeaf(new IndependentOracleCanonicalPath(0, 0UL), 0.0d, 1.0d) }), Is.False);
            state.ObserveRootMasks(IndependentOracleRootMask.Distribution, IndependentOracleRootMask.None, IndependentOracleRootMask.GuardThenDistribution, IndependentOracleRootMask.Guard, IndependentOracleRootMask.DistributionThenGuard, IndependentOracleRootMask.Guard);
            Assert.That(state.RootMaskTopologySignature, Is.EqualTo("none|guard|distribution|guard-distribution|distribution-guard"));
        }

        /// <summary>Audits each new kernel source independently and permits only raw contract DTO references across the boundary.</summary>
        [Test]
        public void IndependentOracleKernelsHaveNoForbiddenNumericalDependencies()
        {
            string[] legacy = { "AdaptivePrimary", "AdaptiveCrossCheck", "KronrodWitness", "AdaptiveProtocol", "PureBasePbrMultipleScatteringReference", "PureBasePbrSafeNormalize", "PureBasePbrEvaluateSmithJointGgxVisibility", "EvaluateGuardedTerms" };
            AssertKernelDependencies("PureBasePbrMultipleScatteringLightSpaceOracle*.cs", legacy, @"\bIndependentOracleWitness[A-Za-z0-9_]*\b", new[] { "IndependentOracleInput", "LightSpaceOracleStopState" });
            AssertKernelDependencies("PureBasePbrMultipleScatteringIndependentOracleWitness*.cs", legacy, @"\bLightSpaceOracle[A-Za-z0-9_]*\b", new[] { "IndependentOracleInput", "IndependentOracleDecisionEvidence" });
            Assert.That(WitnessDependencyAuditPasses("IndependentOracleInput"), Is.True, "the raw input tuple is an approved witness boundary");
            Assert.That(WitnessDependencyAuditPasses("IndependentOracleDecisionEvidence"), Is.True, "comparison evidence is an approved witness boundary");
            Assert.That(WitnessDependencyAuditPasses("LightSpaceOracleStopState"), Is.False, "a witness reference to a candidate stop enum must fail the dependency audit");
            Assert.That(WitnessDependencyAuditPasses("LightSpaceOracleResult"), Is.False, "a candidate numerical result remains outside the witness boundary");
        }

        /// <summary>Requires a numerical candidate result when the candidate implementation becomes available.</summary>
        [Test]
        public void IndependentOracleCandidateProducesRepresentativeEvidence()
        {
            Assert.That(() => LightSpaceOracle.Integrate(IndependentOracleContract.RepresentativeRows[0].Input, IndependentOracleContract.CandidateBaseTarget), Throws.Nothing);
        }

        /// <summary>Requires witness analytical-control evidence when the witness implementation becomes available.</summary>
        [Test]
        public void IndependentOracleWitnessProducesAnalyticalControls()
        {
            Assert.That(() => IndependentOracleWitness.Integrate(IndependentOracleContract.RepresentativeRows[0].Input), Throws.Nothing);
        }

        /// <summary>Expands the unreduced product algebra locally so the test does not validate through an oracle helper.</summary>
        private static double UnreducedFormula(IndependentOracleInput input, double u, double phi)
        {
            double v = input.NdotV; double a = input.P * input.P; double m = a * a; double epsilon = input.Branch == IndependentOracleBranch.Normal ? 1.0e-5d : 1.0d / 16384.0d;
            double q = 2.0d * (1.0d + u * v + Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v) * Math.Cos(phi)); double h2 = (u + v) * (u + v) / Math.Max(q, 1.0e-6d); double d = h2 * (m - 1.0d) + 1.0d;
            return m / Math.Max(Math.PI * d * d, 1.0e-6d) * (0.5d / (u * (v * (1.0d - a) + a) + v * (u * (1.0d - a) + a) + epsilon)) * u;
        }

        /// <summary>Requires an immutable collection to reject indexed mutation.</summary>
        private static void AssertReadOnly<T>(IReadOnlyList<T> values, T expected)
        {
            Assert.That(values[0], Is.EqualTo(expected));
            Assert.That(() => ((IList<T>)values)[0] = expected, Throws.TypeOf<NotSupportedException>());
        }

        /// <summary>Checks a local guard or GGX q-target reconstruction for one retained root.</summary>
        private static void AssertReconstructedRoot(IndependentOracleThetaRoot root, double r, double v, double p)
        {
            double u = Math.Pow(Math.Sin(Math.PI * r * 0.5d), 2.0d); double z = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v);
            double q = 2.0d * ((1.0d - z) + u * v + z * (1.0d + root.Cosine)); double target = IndependentOracleContract.GuardFloor;
            if (root.Kind == IndependentOracleRootKind.Distribution) { double m = p * p * p * p; double h2 = (1.0d - Math.Sqrt(IndependentOracleContract.GuardFloor / Math.PI)) / (1.0d - m); target = (u + v) * (u + v) / h2; }
            Assert.That(root.Present, Is.True); Assert.That(root.Theta, Is.GreaterThan(0.0d)); Assert.That(root.Theta, Is.LessThan(Math.PI)); Assert.That(q, Is.EqualTo(target).Within(128.0d * IndependentOracleContract.Ulp(target)));
        }

        /// <summary>Requires ordered atomic intervals to cover the half-azimuth domain exactly once.</summary>
        private static void AssertAtomicCoverage(IReadOnlyList<double> boundaries)
        {
            Assert.That(boundaries[0], Is.EqualTo(0.0d)); Assert.That(boundaries[boundaries.Count - 1], Is.EqualTo(Math.PI));
            double measure = 0.0d; for (int index = 0; index + 1 < boundaries.Count; index++) { Assert.That(boundaries[index], Is.LessThan(boundaries[index + 1])); measure += boundaries[index + 1] - boundaries[index]; }
            Assert.That(measure, Is.EqualTo(Math.PI).Within(2.0e-15d));
        }

        /// <summary>Builds a minimal decision fixture with no implicit default state.</summary>
        private static IndependentOracleDecisionEvidence Evidence(bool candidateIndependent, bool witnessIndependent, bool candidateFinite, bool witnessFinite, bool candidateAccepted, IndependentOracleCandidateUncertaintyEvidence candidateUncertainty, bool candidateAnalytical, bool witnessAnalytical, bool witnessConverged, bool agreement, bool eligibility) => new IndependentOracleDecisionEvidence(candidateIndependent, witnessIndependent, candidateFinite, witnessFinite, candidateAccepted, candidateUncertainty, candidateAnalytical, witnessAnalytical, witnessConverged, agreement, eligibility);

        /// <summary>Gets the next positive binary64 value without using a platform-version-specific helper.</summary>
        private static double NextUp(double value) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(value) + 1L);

        /// <summary>Builds Clenshaw--Curtis nodes and weights with the frozen ascending node formula.</summary>
        private static NodeWeight[] ClenshawCurtis(int order)
        {
            int n = order - 1; var result = new NodeWeight[order]; for (int index = 0; index < order; index++) { double theta = Math.PI * index / n; double sum = 1.0d; for (int k = 1; k < n / 2; k++) sum -= 2.0d * Math.Cos(2.0d * k * theta) / (4.0d * k * k - 1.0d); sum -= Math.Cos(n * theta) / (n * n - 1.0d); result[index] = new NodeWeight(Math.Cos(theta), index == 0 || index == n ? 1.0d / (n * n - 1.0d) : 2.0d * sum / n); } return result;
        }

        /// <summary>Builds endpoint-free Fejer-II nodes and weights from the frozen finite sine expansion.</summary>
        private static NodeWeight[] FejerII(int order)
        {
            var result = new NodeWeight[order]; int terms = (order + 1) / 2; for (int index = 1; index <= order; index++) { double theta = Math.PI * index / (order + 1); double series = 0.0d; for (int term = 1; term <= terms; term++) { int harmonic = 2 * term - 1; series += Math.Sin(harmonic * theta) / harmonic; } result[index - 1] = new NodeWeight(Math.Cos(theta), 4.0d * Math.Sin(theta) * series / (order + 1)); } return result;
        }

        /// <summary>Requires symmetry, positive weights, unit-domain sum, and the requested polynomial moments.</summary>
        private static void AssertQuadrature(NodeWeight[] rule, int degree)
        {
            double sum = 0.0d; foreach (NodeWeight item in rule) { Assert.That(item.Weight, Is.GreaterThan(0.0d)); sum += item.Weight; } Assert.That(sum, Is.EqualTo(2.0d).Within(2.0e-13d));
            for (int index = 0; index < rule.Length / 2; index++) { Assert.That(rule[index].X, Is.EqualTo(-rule[rule.Length - 1 - index].X).Within(2.0e-14d)); Assert.That(rule[index].Weight, Is.EqualTo(rule[rule.Length - 1 - index].Weight).Within(2.0e-14d)); }
            for (int power = 0; power <= degree; power++) { double actual = 0.0d; foreach (NodeWeight item in rule) actual += item.Weight * Math.Pow(item.X, power); double expected = power % 2 == 0 ? 2.0d / (power + 1) : 0.0d; Assert.That(actual, Is.EqualTo(expected).Within(5.0e-11d)); }
        }

        /// <summary>Checks high-order private-Legendre invariants without sharing any witness implementation.</summary>
        private static void AssertLegendre(int order)
        {
            NodeWeight[] rule = Legendre(order); double sum = 0.0d; foreach (NodeWeight item in rule) { Assert.That(item.Weight, Is.GreaterThan(0.0d)); sum += item.Weight; } Assert.That(sum, Is.EqualTo(2.0d).Within(5.0e-13d));
            for (int index = 0; index < order / 2; index++) { Assert.That(rule[index].X, Is.EqualTo(-rule[order - 1 - index].X).Within(5.0e-14d)); Assert.That(rule[index].Weight, Is.EqualTo(rule[order - 1 - index].Weight).Within(5.0e-14d)); }
            for (int power = 0; power < 2 * order; power++) { double actual = 0.0d; foreach (NodeWeight item in rule) actual += item.Weight * Math.Pow(item.X, power); double expected = power % 2 == 0 ? 2.0d / (power + 1) : 0.0d; Assert.That(actual, Is.EqualTo(expected).Within(5.0e-11d)); }
        }

        /// <summary>Generates a Gauss--Legendre rule by its independent bounded Newton recurrence.</summary>
        private static NodeWeight[] Legendre(int order)
        {
            var rule = new NodeWeight[order]; for (int root = 0; root < (order + 1) / 2; root++) { double x = Math.Cos(Math.PI * (root + 0.75d) / (order + 0.5d)); double derivative = 0.0d; for (int iteration = 0; iteration < 32; iteration++) { LegendreValue(order, x, out double value, out derivative); double next = x - value / derivative; if (Math.Abs(next - x) <= 4.0e-16d) { x = next; break; } x = next; } LegendreValue(order, x, out _, out derivative); double weight = 2.0d / ((1.0d - x * x) * derivative * derivative); rule[root] = new NodeWeight(-x, weight); rule[order - 1 - root] = new NodeWeight(x, weight); } return rule;
        }

        /// <summary>Evaluates one Legendre polynomial and derivative by the three-term recurrence.</summary>
        private static void LegendreValue(int order, double x, out double value, out double derivative)
        {
            double previous = 1.0d; double current = x; for (int degree = 2; degree <= order; degree++) { double next = ((2.0d * degree - 1.0d) * x * current - (degree - 1.0d) * previous) / degree; previous = current; current = next; } value = order == 0 ? previous : current; derivative = order * (x * current - previous) / (x * x - 1.0d);
        }

        /// <summary>Checks periodic midpoint constants and Fourier modes below the stated Nyquist limit at both fixed phases.</summary>
        private static void AssertPeriodic(int count, double phase)
        {
            double weight = 2.0d * Math.PI / count; double constant = 0.0d; foreach (int mode in new[] { 1, 2, 7, 31, 127 })
            {
                double cosine = 0.0d; double sine = 0.0d; for (int index = 0; index < count; index++) { double phi = 2.0d * Math.PI * (index + phase) / count; constant += weight; cosine += Math.Cos(mode * phi) * weight; sine += Math.Sin(mode * phi) * weight; }
                Assert.That(cosine, Is.EqualTo(0.0d).Within(2.0e-11d)); Assert.That(sine, Is.EqualTo(0.0d).Within(2.0e-11d));
            }
            Assert.That(constant / 5.0d, Is.EqualTo(2.0d * Math.PI).Within(2.0e-12d));
        }

        /// <summary>Checks exact binary64 large-small-large recovery in both sign orders.</summary>
        private static void AssertDoubleDouble(ulong largeBits, ulong smallBits, bool reverse)
        {
            double large = BitConverter.Int64BitsToDouble(unchecked((long)largeBits)); double small = BitConverter.Int64BitsToDouble(unchecked((long)smallBits)); var sum = new DoubleDouble();
            sum.Add(reverse ? -large : large); sum.Add(small); sum.Add(reverse ? large : -large); Assert.That(sum.Value, Is.EqualTo(small));
        }

        /// <summary>Audits every present and future implementation artifact for one kernel without scanning contract or test files.</summary>
        private static void AssertKernelDependencies(string pattern, string[] forbidden, string otherKernelPattern, string[] allowedBoundaryTypes)
        {
            string directory = Path.Combine(Application.dataPath, "..", "Packages", "jp.penguin.purebase", "Tests", "Daily", "Editor"); string[] paths = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly); Array.Sort(paths, StringComparer.Ordinal);
            Assert.That(paths.Length, Is.GreaterThan(0), pattern + " found no kernel artifacts"); var declared = new HashSet<string>(allowedBoundaryTypes);
            foreach (string path in paths) foreach (Match match in Regex.Matches(File.ReadAllText(path), @"\b(?:class|struct|enum)\s+(\w+)")) declared.Add(match.Groups[1].Value);
            foreach (string path in paths)
            {
                string source = File.ReadAllText(path); string fileName = Path.GetFileName(path);
                foreach (string token in forbidden) Assert.That(source, Does.Not.Contain(token), fileName + " references " + token);
                Assert.That(source, Does.Not.Contain("IndependentOracleContract"), fileName + " reuses a contract numerical helper");
                foreach (Match match in Regex.Matches(source, otherKernelPattern)) Assert.That(IsPermittedCrossKernelBoundary(match.Value, allowedBoundaryTypes) || !declared.Contains(match.Value), Is.True, fileName + " crosses the kernel boundary through " + match.Value);
                foreach (Match match in Regex.Matches(source, @"\b(?:IndependentOracle|LightSpaceOracle)[A-Za-z0-9_]+\b")) Assert.That(declared, Does.Contain(match.Value), fileName + " crosses an unapproved boundary through " + match.Value);
            }
        }

        /// <summary>Gets whether a matched cross-kernel type is an explicitly approved neutral boundary DTO.</summary>
        private static bool IsPermittedCrossKernelBoundary(string typeName, IEnumerable<string> allowedBoundaryTypes) => new HashSet<string>(allowedBoundaryTypes).Contains(typeName);

        /// <summary>Applies the witness dependency allowlist used by the source audit to one cross-kernel reference.</summary>
        private static bool WitnessDependencyAuditPasses(string typeName) => IsPermittedCrossKernelBoundary(typeName, new[] { "IndependentOracleInput", "IndependentOracleDecisionEvidence" });

        /// <summary>Builds one test-only leaf with a depth-aware canonical path.</summary>
        private static LightSpaceOracleAggregateLeaf Leaf(int depth, ulong binaryPath, double value, double error, bool refinable) => new LightSpaceOracleAggregateLeaf(new IndependentOracleCanonicalPath(depth, binaryPath), value, error, refinable);

        /// <summary>Builds a root-committed scripted state for one targeted hard-stop probe.</summary>
        private static LightSpaceOracleScriptedState CreateState(int maximumDepth, int maximumEvaluations, int scalarCalls)
        {
            var state = new LightSpaceOracleScriptedState(maximumDepth, 8, maximumEvaluations, scalarCalls); state.EvaluateRoot(0); return state;
        }

        /// <summary>Builds a root-only scripted state that has already failed at a requested scalar-call position.</summary>
        private static LightSpaceOracleScriptedState CreateRootFailureState(int maximumDepth, int maximumEvaluations, int scalarCalls, int nonFiniteScalarCall)
        {
            var state = new LightSpaceOracleScriptedState(maximumDepth, 8, maximumEvaluations, scalarCalls); state.EvaluateRoot(nonFiniteScalarCall); return state;
        }

        /// <summary>Runs the requested child sequence and asserts every retained hard-stop field.</summary>
        private static void AssertHardStop(LightSpaceOracleScriptedState state, string name, int childZeroNonFiniteCall, int childOneNonFiniteCall, LightSpaceOracleStopState expectedStop, string context, string path, int scalarCall, int panels, int evaluations, int maximumDepth, string topology)
        {
            state.RefineRoot(childZeroNonFiniteCall, childOneNonFiniteCall); AssertStop(state, expectedStop, context, path, scalarCall, panels, evaluations, maximumDepth, topology);
            Assert.That(state.HasAcceptablePartialResult, Is.False, name + " returned a partial result");
        }

        /// <summary>Asserts the frozen stop context, reserved work, and parent-only topology after a hard stop.</summary>
        private static void AssertStop(LightSpaceOracleScriptedState state, LightSpaceOracleStopState expectedStop, string context, string path, int scalarCall, int panels, int evaluations, int maximumDepth, string topology)
        {
            Assert.That(state.StopState, Is.EqualTo(expectedStop)); Assert.That(state.FirstFailure.StopState, Is.EqualTo(expectedStop)); Assert.That(state.FirstFailure.Context, Is.EqualTo(context)); Assert.That(state.FirstFailure.Path, Is.EqualTo(path)); Assert.That(state.FirstFailure.ScalarCall, Is.EqualTo(scalarCall));
            Assert.That(state.Panels, Is.EqualTo(panels)); Assert.That(state.Evaluations, Is.EqualTo(evaluations)); Assert.That(state.MaximumDepth, Is.EqualTo(maximumDepth)); Assert.That(state.Topology, Is.EqualTo(topology));
        }

        /// <summary>Stores one local quadrature node and positive weight.</summary>
        private readonly struct NodeWeight
        {
            /// <summary>Initializes one immutable local quadrature node and its positive weight.</summary>
            internal NodeWeight(double x, double weight) { X = x; Weight = weight; }
            /// <summary>Gets the local quadrature node coordinate.</summary>
            internal double X { get; }
            /// <summary>Gets the local quadrature weight.</summary>
            internal double Weight { get; }
        }

        /// <summary>Accumulates a small double-double expansion for cancellation contracts.</summary>
        private struct DoubleDouble
        {
            /// <summary>Stores the leading binary64 component of the expansion.</summary>
            private double high;
            /// <summary>Stores the corrective binary64 component of the expansion.</summary>
            private double low;
            /// <summary>Adds one binary64 term using a two-sum expansion.</summary>
            internal void Add(double value) { double sum = high + value; double virtualValue = sum - high; double error = (high - (sum - virtualValue)) + (value - virtualValue); double corrected = low + error; high = sum + corrected; low = corrected - (high - sum); }
            /// <summary>Gets the reconstructed expansion value.</summary>
            internal double Value => high + low;
        }

    }
}
