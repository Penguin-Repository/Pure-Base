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
// Verifies bounded raw angular diagnostic evidence without classifying the terminal result.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Names synthetic-only outcomes while leaving every terminal record unclassified.</summary>
    [Flags]
    internal enum AngularDiagnosticClassification { Unclassified = 0, ACompatible = 1, BCompatible = 2, CCompatible = 4, DCompatible = 8, ECompatible = 16, FCompatible = 32 }
    /// <summary>Names the one synthetic A or B mechanism claimed by localized evidence.</summary>
    internal enum AngularDiagnosticMechanism { None, A, B, Conflicting }
    /// <summary>Names the supported B conclusion without changing whether B is supported.</summary>
    internal enum AngularDiagnosticBConclusion { None, Overestimation, NumericalFloor }
    /// <summary>Provides immutable synthetic availability facts without terminal interpretation authority.</summary>
    internal readonly struct AngularDiagnosticAvailability
    {
        /// <summary>Initializes one isolated availability fixture.</summary>
        internal AngularDiagnosticAvailability(bool identityCompatible, bool checkerAvailable, bool coverageEnrichmentAgree, bool mixedSignal, bool greySignal) { IdentityCompatible = identityCompatible; CheckerAvailable = checkerAvailable; CoverageEnrichmentAgree = coverageEnrichmentAgree; MixedSignal = mixedSignal; GreySignal = greySignal; }
        internal bool IdentityCompatible { get; }
        internal bool CheckerAvailable { get; }
        internal bool CoverageEnrichmentAgree { get; }
        internal bool MixedSignal { get; }
        internal bool GreySignal { get; }
    }
    /// <summary>Stores one named cohort's usable distinct-leaf count.</summary>
    internal readonly struct AngularDiagnosticCohortEvidence
    {
        /// <summary>Initializes one immutable cohort evidence fixture.</summary>
        internal AngularDiagnosticCohortEvidence(AngularDiagnosticCohort cohort, int usableDistinctLeaves) { Cohort = cohort; UsableDistinctLeaves = usableDistinctLeaves; }
        internal AngularDiagnosticCohort Cohort { get; }
        internal int UsableDistinctLeaves { get; }
    }
    /// <summary>Stores one same-boundary mechanism and its matched smooth controls.</summary>
    internal readonly struct AngularDiagnosticBoundaryEvidence
    {
        /// <summary>Initializes one immutable boundary evidence fixture.</summary>
        internal AngularDiagnosticBoundaryEvidence(AngularDiagnosticMechanism mechanism, int sameBoundaryCoverageDistinctLeaves, int oneToOneMatchedSmoothControls) { Mechanism = mechanism; SameBoundaryCoverageDistinctLeaves = sameBoundaryCoverageDistinctLeaves; OneToOneMatchedSmoothControls = oneToOneMatchedSmoothControls; }
        internal AngularDiagnosticMechanism Mechanism { get; }
        internal int SameBoundaryCoverageDistinctLeaves { get; }
        internal int OneToOneMatchedSmoothControls { get; }
    }
    /// <summary>Stores one smooth mechanism and separated-quantile coverage facts.</summary>
    internal readonly struct AngularDiagnosticSmoothEvidence
    {
        /// <summary>Initializes one immutable smooth evidence fixture.</summary>
        internal AngularDiagnosticSmoothEvidence(AngularDiagnosticMechanism mechanism, int coverageDistinctLeaves, bool separatedQuantiles) { Mechanism = mechanism; CoverageDistinctLeaves = coverageDistinctLeaves; SeparatedQuantiles = separatedQuantiles; }
        internal AngularDiagnosticMechanism Mechanism { get; }
        internal int CoverageDistinctLeaves { get; }
        internal bool SeparatedQuantiles { get; }
    }

    /// <summary>Provides synthetic rubric facts without granting terminal interpretation authority.</summary>
    internal readonly struct AngularDiagnosticClassificationInput
    {
        /// <summary>Initializes one isolated synthetic classification fixture.</summary>
        internal AngularDiagnosticClassificationInput(AngularDiagnosticAvailability availability, AngularDiagnosticCohortEvidence a, AngularDiagnosticCohortEvidence b, bool floorConsistent, AngularDiagnosticBoundaryEvidence boundary, AngularDiagnosticSmoothEvidence smooth) { Availability = availability; A = a; B = b; FloorConsistent = floorConsistent; Boundary = boundary; Smooth = smooth; }
        internal AngularDiagnosticAvailability Availability { get; }
        internal AngularDiagnosticCohortEvidence A { get; }
        internal AngularDiagnosticCohortEvidence B { get; }
        internal bool FloorConsistent { get; }
        internal AngularDiagnosticBoundaryEvidence Boundary { get; }
        internal AngularDiagnosticSmoothEvidence Smooth { get; }
    }

    /// <summary>Classifies synthetic evidence only while terminal observations remain unclassified.</summary>
    internal static class AngularDiagnosticSyntheticClassifier
    {
        /// <summary>Applies frozen A-F evidence precedence to one synthetic fixture.</summary>
        internal static AngularDiagnosticClassification Classify(AngularDiagnosticClassificationInput input)
        {
            if (!input.Availability.IdentityCompatible) return AngularDiagnosticClassification.ECompatible;
            if (Inconclusive(input)) return AngularDiagnosticClassification.FCompatible;
            bool a = Replicated(input.A); bool b = Replicated(input.B); bool boundary = BoundarySupported(input.Boundary); bool smooth = SmoothSupported(input.Smooth);
            if (RequestedEvidenceIsInsufficient(input.Boundary, input.Smooth, boundary, smooth) || Conflicting(a, b, boundary, input.Boundary.Mechanism, smooth, input.Smooth.Mechanism)) return AngularDiagnosticClassification.FCompatible;
            return Classified(a, b, boundary, smooth);
        }

        /// <summary>Labels a replicated B conclusion as a floor only when its separate predicate holds.</summary>
        internal static AngularDiagnosticBConclusion DescribeB(AngularDiagnosticClassificationInput input) => (Classify(input) & AngularDiagnosticClassification.BCompatible) == 0 ? AngularDiagnosticBConclusion.None : input.FloorConsistent ? AngularDiagnosticBConclusion.NumericalFloor : AngularDiagnosticBConclusion.Overestimation;

        /// <summary>Requires three usable distinct leaves from one named cohort.</summary>
        private static bool Replicated(AngularDiagnosticCohortEvidence evidence) => (evidence.Cohort == AngularDiagnosticCohort.Coverage || evidence.Cohort == AngularDiagnosticCohort.Enrichment) && evidence.UsableDistinctLeaves >= 3;

        /// <summary>Rejects unavailable, mixed, grey, conflicting, or cohort-disagreed synthetic evidence.</summary>
        private static bool Inconclusive(AngularDiagnosticClassificationInput input) => !input.Availability.CheckerAvailable || !input.Availability.CoverageEnrichmentAgree || input.Availability.MixedSignal || input.Availability.GreySignal || input.Boundary.Mechanism == AngularDiagnosticMechanism.Conflicting || input.Smooth.Mechanism == AngularDiagnosticMechanism.Conflicting;

        /// <summary>Checks the fixed same-boundary and one-to-one control minimums for C.</summary>
        private static bool BoundarySupported(AngularDiagnosticBoundaryEvidence evidence) => evidence.Mechanism != AngularDiagnosticMechanism.None && evidence.SameBoundaryCoverageDistinctLeaves >= 3 && evidence.OneToOneMatchedSmoothControls >= 3;

        /// <summary>Checks the fixed smooth coverage and separated-quantile minimums for D.</summary>
        private static bool SmoothSupported(AngularDiagnosticSmoothEvidence evidence) => evidence.Mechanism != AngularDiagnosticMechanism.None && evidence.CoverageDistinctLeaves >= 3 && evidence.SeparatedQuantiles;

        /// <summary>Rejects any requested C or D explanation whose required controls are missing.</summary>
        private static bool RequestedEvidenceIsInsufficient(AngularDiagnosticBoundaryEvidence boundary, AngularDiagnosticSmoothEvidence smooth, bool boundarySupported, bool smoothSupported) => boundary.Mechanism != AngularDiagnosticMechanism.None && !boundarySupported || smooth.Mechanism != AngularDiagnosticMechanism.None && !smoothSupported;

        /// <summary>Combines only independently satisfied synthetic explanation flags.</summary>
        private static AngularDiagnosticClassification Classified(bool a, bool b, bool boundary, bool smooth)
        {
            AngularDiagnosticClassification result = AngularDiagnosticClassification.Unclassified;
            if (a) result |= AngularDiagnosticClassification.ACompatible; if (b) result |= AngularDiagnosticClassification.BCompatible; if (boundary) result |= AngularDiagnosticClassification.CCompatible; if (smooth) result |= AngularDiagnosticClassification.DCompatible;
            return result == AngularDiagnosticClassification.Unclassified ? AngularDiagnosticClassification.FCompatible : result;
        }

        /// <summary>Rejects evidence that supports incompatible A and B explanations.</summary>
        private static bool Conflicting(bool a, bool b, bool boundary, AngularDiagnosticMechanism boundaryMechanism, bool smooth, AngularDiagnosticMechanism smoothMechanism)
        {
            bool supportsA = a || boundary && boundaryMechanism == AngularDiagnosticMechanism.A || smooth && smoothMechanism == AngularDiagnosticMechanism.A;
            bool supportsB = b || boundary && boundaryMechanism == AngularDiagnosticMechanism.B || smooth && smoothMechanism == AngularDiagnosticMechanism.B;
            return supportsA && supportsB;
        }
    }

    /// <summary>Captures bounded independent angular evidence for the exact minimum-grazing terminal mesh.</summary>
    public sealed class PureBasePbrMultipleScatteringIndependentOracleCandidateAngularIndicatorDiagnosisTests
    {
        /// <summary>Checks independent rules and synthetic comparison availability without terminal classification.</summary>
        [Test]
        public void AngularDiagnosticRulesAndSyntheticComparisonsAreIndependentAndDeterministic()
        {
            Assert.That(AngularDiagnosticQuadrature.RulesAreValid(), Is.True);
            var input = new AngularDiagnosticCalculationInput(0x3FB6C8B439581062L, 0L, IndependentOracleBranch.Normal, BitConverter.DoubleToInt64Bits(0.5d), BitConverter.DoubleToInt64Bits(0.0d), BitConverter.DoubleToInt64Bits(Math.PI));
            AngularDiagnosticCheck first = AngularDiagnosticQuadrature.Check(input); AngularDiagnosticCheck second = AngularDiagnosticQuadrature.Check(input);
            Assert.That(first.Finite, Is.True); Assert.That(first.StopState, Is.EqualTo(AngularDiagnosticCheckStopState.Accepted)); Assert.That(first.RuleIdentity, Is.EqualTo("FejerII17/FejerII33/GaussLegendre127/GaussLegendre251/GaussLegendre503")); Assert.That(first.Work, Is.EqualTo(931)); Assert.That(first.G503, Is.EqualTo(second.G503));
            AngularDiagnosticCheck invalid = AngularDiagnosticQuadrature.Check(new AngularDiagnosticCalculationInput(0L, 0L, IndependentOracleBranch.Normal, 0L, BitConverter.DoubleToInt64Bits(1.0d), BitConverter.DoubleToInt64Bits(0.0d))); Assert.That(invalid.Finite, Is.False); Assert.That(invalid.StopState, Is.EqualTo(AngularDiagnosticCheckStopState.InvalidInput)); Assert.That(invalid.Work, Is.Zero);
            Assert.That(AngularDiagnosticQuadrature.LegendreStopStateForFixture(503, 32), Is.EqualTo(AngularDiagnosticCheckStopState.Accepted)); Assert.That(AngularDiagnosticQuadrature.LegendreStopStateForFixture(503, 0), Is.EqualTo(AngularDiagnosticCheckStopState.RuleGenerationFailure));
        }

        /// <summary>Locks the independent smooth interval, endpoint topology, and Gauss-Legendre fixture invariants.</summary>
        [Test]
        public void SyntheticQuadratureFixturesCoverSmoothIntervalAndBoundaryIdentity()
        {
            var smooth = new AngularDiagnosticCalculationInput(Bits(0.5d), Bits(0.0d), IndependentOracleBranch.Normal, Bits(0.5d), Bits(0.0d), Bits(Math.PI));
            AngularDiagnosticCheck check = AngularDiagnosticQuadrature.Check(smooth); long[] boundaries = AngularDiagnosticQuadrature.ReconstructBoundaryBits(smooth); AngularDiagnosticBoundaryKind[] kinds = AngularDiagnosticQuadrature.ReconstructBoundaryKinds(smooth);
            Assert.That(check.Finite, Is.True); Assert.That(check.G127, Is.GreaterThan(0.0d)); Assert.That(check.G251, Is.GreaterThan(0.0d)); Assert.That(check.G503, Is.GreaterThan(0.0d)); Assert.That(boundaries, Is.EqualTo(new[] { Bits(0.0d), Bits(Math.PI) })); Assert.That(kinds, Is.EqualTo(new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Endpoint }));
            Assert.That(AngularDiagnosticQuadrature.RulesAreValid(), Is.True, "positive, symmetric, normalized Gauss-Legendre rules must retain low polynomial moments");
        }

        /// <summary>Locks a smooth theta-independent scalar against its analytic interval integral.</summary>
        [Test]
        public void AnalyticSmoothAngularFixtureAgreesWithEveryIndependentRule()
        {
            var input = new AngularDiagnosticCalculationInput(Bits(0.5d), Bits(1.0d), IndependentOracleBranch.Normal, Bits(0.5d), Bits(0.0d), Bits(Math.PI)); AngularDiagnosticCheck check = AngularDiagnosticQuadrature.Check(input);
            double distribution = 0.0625d / (Math.PI * 0.296875d * 0.296875d); double visibility = 0.5d / 1.12501d; double expected = 0.5d * distribution * visibility * Math.PI * Math.PI;
            Assert.That(check.Finite, Is.True); AssertWithinUlps(check.Q17, expected, 256UL); AssertWithinUlps(check.Q33, expected, 256UL); AssertWithinUlps(check.G127, expected, 256UL); AssertWithinUlps(check.G251, expected, 256UL); AssertWithinUlps(check.G503, expected, 256UL);
        }

        /// <summary>Locks uncertainty clamps and immutable numerical interval signals at their exact boundaries.</summary>
        [Test]
        public void SyntheticUncertaintyFixturesUseTheExactComparisonGate()
        {
            double target = IndependentOracleContract.CandidateBaseTarget; AngularDiagnosticCalculationInput input = new AngularDiagnosticCalculationInput(Bits(0.5d), Bits(1.0d), IndependentOracleBranch.Normal, Bits(0.5d), 0L, Bits(Math.PI));
            AngularDiagnosticSelectedEvidence equality = Evidence(0.0d, 4.0d * target, -4.0d * target, 0.0d, target, input); Assert.That(equality.H, Is.EqualTo(target)); Assert.That(equality.DH, Is.EqualTo(equality.DL / 4.0d)); Assert.That(equality.UH, Is.EqualTo(target)); Assert.That(equality.AMinus, Is.GreaterThan(0.0d)); Assert.That(equality.UsableReference, Is.True);
            AngularDiagnosticSelectedEvidence b = Evidence(0.0d, 1.0d, 1.0d, 1.0d, 1.0d, input); Assert.That(b.IntervalSignal, Is.EqualTo(AngularDiagnosticIntervalSignal.B));
            AngularDiagnosticSelectedEvidence zero = Evidence(1.0d, 1.0d, 2.0d, 2.0d, 2.0d, input); Assert.That(zero.E, Is.Zero); Assert.That(zero.RatioState, Is.EqualTo(AngularDiagnosticRatioState.UnavailableZeroIndicator)); Assert.That(double.IsNaN(zero.RatioValue), Is.True); Assert.That(zero.IntervalSignal, Is.EqualTo(AngularDiagnosticIntervalSignal.A));
            double floor = 128.0d * (BitConverter.Int64BitsToDouble(Bits(1.0d) + 1L) - 1.0d); AngularDiagnosticSelectedEvidence floorEquality = Evidence(1.0d - 4.0d * floor, 1.0d, 1.0d + 3.0d * floor, 1.0d + 3.0d * floor, 1.0d + 3.0d * floor, input); Assert.That(floorEquality.E, Is.EqualTo(4.0d * floor)); Assert.That(floorEquality.APlus, Is.EqualTo(4.0d * floor)); Assert.That(floorEquality.IntervalSignal, Is.EqualTo(AngularDiagnosticIntervalSignal.FloorConsistent));
            AngularDiagnosticSelectedEvidence mixed = Evidence(1.0d - 4.0d * floor, 1.0d, 1.0d + 4.0d * floor, 1.0d + 4.0d * floor, 1.0d + 4.0d * floor, input); Assert.That(mixed.IntervalSignal, Is.EqualTo(AngularDiagnosticIntervalSignal.Mixed));
            AngularDiagnosticCheck unavailable = new AngularDiagnosticCheck(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0, false, AngularDiagnosticCheckStopState.InvalidInput, "fixture"); AngularDiagnosticSelectedEvidence rejected = Evidence(unavailable, 0.0d, 0.0d, input); Assert.That(rejected.UsableReference, Is.False); Assert.That(rejected.UnavailableReason, Is.EqualTo(AngularDiagnosticUnavailableReason.InvalidInput));
        }

        /// <summary>Requires three usable A records from distinct leaves in one named cohort.</summary>
        [Test]
        public void SyntheticClassifierRequiresNamedCohortReplicationForA()
        {
            Assert.That(Classify(Fixture().WithA(3)), Is.EqualTo(AngularDiagnosticClassification.ACompatible));
            Assert.That(Classify(Fixture().WithA(2)), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
        }

        /// <summary>Recognizes replicated non-floor B evidence as overestimation rather than inconclusive.</summary>
        [Test]
        public void SyntheticClassifierRecognizesNonFloorBAsOverestimation()
        {
            SyntheticClassifierFixture fixture = Fixture().WithB(3, false);
            Assert.That(Classify(fixture), Is.EqualTo(AngularDiagnosticClassification.BCompatible));
            Assert.That(DescribeB(fixture), Is.EqualTo(AngularDiagnosticBConclusion.Overestimation));
        }

        /// <summary>Labels a replicated floor-consistent B conclusion without changing its B classification.</summary>
        [Test]
        public void SyntheticClassifierLabelsFloorConsistentBAsNumericalFloor()
        {
            SyntheticClassifierFixture fixture = Fixture().WithB(3, true);
            Assert.That(Classify(fixture), Is.EqualTo(AngularDiagnosticClassification.BCompatible));
            Assert.That(DescribeB(fixture), Is.EqualTo(AngularDiagnosticBConclusion.NumericalFloor));
        }

        /// <summary>Requires material same-boundary coverage and one-to-one smooth controls for C.</summary>
        [Test]
        public void SyntheticClassifierRequiresBoundaryCoverageAndMatchedControlsForC()
        {
            Assert.That(Classify(Fixture().WithBoundary(3, 3)), Is.EqualTo(AngularDiagnosticClassification.CCompatible));
            Assert.That(Classify(Fixture().WithBoundary(2, 3)), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
            Assert.That(Classify(Fixture().WithBoundary(3, 2)), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
        }

        /// <summary>Requires distinct material smooth coverage and separated quantiles for D.</summary>
        [Test]
        public void SyntheticClassifierRequiresSmoothCoverageAndSeparatedQuantilesForD()
        {
            Assert.That(Classify(Fixture().WithSmooth(3, true)), Is.EqualTo(AngularDiagnosticClassification.DCompatible));
            Assert.That(Classify(Fixture().WithSmooth(2, true)), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
            Assert.That(Classify(Fixture().WithSmooth(3, false)), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
        }

        /// <summary>Combines compatible A and C evidence without suppressing either supported conclusion.</summary>
        [Test]
        public void SyntheticClassifierCombinesCompatibleAAndC()
        {
            Assert.That(Classify(Fixture().WithA(3).WithBoundary(3, 3)), Is.EqualTo(AngularDiagnosticClassification.ACompatible | AngularDiagnosticClassification.CCompatible));
        }

        /// <summary>Rejects conflicting A and B mechanisms even when each has three distinct leaves.</summary>
        [Test]
        public void SyntheticClassifierMapsConflictingAAndBMechanismsToF()
        {
            Assert.That(Classify(Fixture().WithA(3).WithB(3, false)), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
        }

        /// <summary>Gives deterministic identity inconsistency precedence over all other synthetic facts.</summary>
        [Test]
        public void SyntheticClassifierGivesEPrecedenceToIdentityMismatch()
        {
            Assert.That(Classify(Fixture().WithA(3).WithIdentityCompatible(false).WithoutChecker()), Is.EqualTo(AngularDiagnosticClassification.ECompatible));
        }

        /// <summary>Maps each isolated unavailable, mixed, and cohort-disagreement reason to F.</summary>
        [Test]
        public void SyntheticClassifierMapsInconclusiveReasonsToF()
        {
            Assert.That(Classify(Fixture().WithoutChecker()), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
            Assert.That(Classify(Fixture().WithMixedSignal()), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
            Assert.That(Classify(Fixture().WithGreySignal()), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
            Assert.That(Classify(Fixture().WithCohortAgreement(false)), Is.EqualTo(AngularDiagnosticClassification.FCompatible));
        }
        /// <summary>Requires defensive copies and explicit ratio formatting for selected evidence.</summary>
        [Test]
        public void SelectedEvidenceIsImmutableAndFormatsRatioStateAndProvenance()
        {
            AngularDiagnosticSelectedEvidence evidence = Evidence(1.0d, 2.0d, 1.0d, 1.0d, 1.0d, new AngularDiagnosticCalculationInput(Bits(0.5d), Bits(1.0d), IndependentOracleBranch.Normal, Bits(0.5d), 0L, Bits(Math.PI))); long expected = evidence.CandidateBoundaryBits[0]; long[] boundaries = evidence.CandidateBoundaryBits; boundaries[0] = 0L; Assert.That(evidence.CandidateBoundaryBits[0], Is.EqualTo(expected)); long[] coordinates = evidence.CandidateFejer17.Coordinates; coordinates[0] = 0L; Assert.That(evidence.CandidateFejer17.Coordinates[0], Is.Not.EqualTo(0L)); long[] weights = evidence.CheckerFejer33.Weights; weights[0] = 0L; Assert.That(evidence.CheckerFejer33.Weights[0], Is.Not.EqualTo(0L)); Assert.That(evidence.HasRatio, Is.True);
            List<CandidateAngularIntervalRecord> nonzero = AngularDiagnosticSampling.Select(new[] { SyntheticRecord(0, 0, 1.0d, 2.0d) }, out _); List<CandidateAngularIntervalRecord> zero = AngularDiagnosticSampling.Select(new[] { SyntheticRecord(1, 0, 0.0d, 2.0d) }, out _); string nonzeroText = AngularDiagnosticSampling.FormatSelected(nonzero)[0]; string zeroText = AngularDiagnosticSampling.FormatSelected(zero)[0];
            Assert.That(nonzero[0].TargetBits, Is.EqualTo(Bits(IndependentOracleContract.CandidateBaseTarget))); Assert.That(nonzero[0].SelectedEvidence.TargetBits, Is.EqualTo(nonzero[0].TargetBits)); Assert.That(nonzeroText, Does.Contain("target=CandidateBaseTarget:" + Bits(IndependentOracleContract.CandidateBaseTarget).ToString("X16")).And.Contain("signal=" + nonzero[0].SelectedEvidence.IntervalSignal)); Assert.That(nonzeroText, Does.Contain("ratio=Available:" + Bits(nonzero[0].SelectedEvidence.RatioValue).ToString("X16"))); Assert.That(zeroText, Does.Contain("ratio=UnavailableZeroIndicator:none")); PropertyInfo cohort = typeof(CandidateAngularIntervalRecord).GetProperty("Cohort", BindingFlags.Instance | BindingFlags.NonPublic); PropertyInfo strata = typeof(CandidateAngularIntervalRecord).GetProperty("Strata", BindingFlags.Instance | BindingFlags.NonPublic); Assert.That(cohort, Is.Not.Null); Assert.That(strata, Is.Not.Null); Assert.That(cohort.GetSetMethod(true), Is.Null); Assert.That(strata.GetSetMethod(true), Is.Null);
        }
        /// <summary>Requires selected evidence and formatted provenance to retain candidate theta width and adjacency facts.</summary>
        [Test]
        public void SelectedEvidenceRetainsCandidateWidthAndAdjacencyFacts() {
            List<CandidateAngularIntervalRecord> selected = AngularDiagnosticSampling.Select(new[] { SyntheticRecord(0, 0, 1.0d, 2.0d) }, out _); AngularDiagnosticSelectedEvidence evidence = selected[0].SelectedEvidence; string formatted = AngularDiagnosticSampling.FormatSelected(selected)[0]; Assert.That(evidence.CandidateThetaWidth, Is.EqualTo(2.0d)); Assert.That(evidence.CandidateThetaWidthBits, Is.EqualTo(Bits(2.0d))); Assert.That(evidence.CandidateEndpointAdjacent, Is.True); Assert.That(evidence.CandidateGuardAdjacent, Is.False); Assert.That(evidence.CandidateDistributionAdjacent, Is.False); Assert.That(formatted, Does.Contain("thetaWidth=2,thetaWidthBits=" + Bits(2.0d).ToString("X16") + ",endpointAdjacent=True,guardAdjacent=False,distributionAdjacent=False")); var input = new IndependentOracleInput(0.5d, 1.0d, IndependentOracleBranch.Normal); var leaf = new LightSpaceOracleCandidateLeaf(new IndependentOracleCanonicalPath(1, 1UL), 0.0d, 1.0d, 0.0d, 0.0d); var adjacent = new CandidateAngularIntervalRecord(input, leaf, 0, 0.0d, 1, 0.5d, new[] { 0.0d, 1.0d, 2.0d, Math.PI }, new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Guard, AngularDiagnosticBoundaryKind.Distribution, AngularDiagnosticBoundaryKind.Endpoint }, 1.0d, 0.5d, 0.0d, 0.0d, 0, AngularDiagnosticSampling.CandidateFejerBits(17), AngularDiagnosticSampling.CandidateFejerBits(33), IndependentOracleContract.CandidateBaseTarget); adjacent.AddCohort(AngularDiagnosticCohort.Coverage); adjacent.AddStratum(AngularDiagnosticStratum.GuardAdjacent | AngularDiagnosticStratum.DistributionAdjacent); adjacent.AttachSelectedEvidence(); string adjacentText = AngularDiagnosticSampling.FormatSelected(new[] { adjacent })[0]; Assert.That(adjacent.SelectedEvidence.CandidateThetaWidth, Is.EqualTo(1.0d)); Assert.That(adjacent.SelectedEvidence.CandidateEndpointAdjacent, Is.False); Assert.That(adjacent.SelectedEvidence.CandidateGuardAdjacent && adjacent.SelectedEvidence.CandidateDistributionAdjacent, Is.True); Assert.That(adjacentText, Does.Contain("thetaWidth=1,thetaWidthBits=" + Bits(1.0d).ToString("X16") + ",endpointAdjacent=False,guardAdjacent=True,distributionAdjacent=True"));
        }
        /// <summary>Locks per-stratum leaf reservation, duplicate replacement, shortfall, overlap, and deterministic selection.</summary>
        [Test]
        public void SyntheticSelectionFixturesRetainSlotsWithoutCrossStratumBackfill()
        {
            List<CandidateAngularIntervalRecord> firstRecords = SyntheticSelectionRecords(); List<CandidateAngularIntervalRecord> first = AngularDiagnosticSampling.Select(firstRecords, out List<AngularDiagnosticSelectionSlot> firstSlots); List<CandidateAngularIntervalRecord> second = AngularDiagnosticSampling.Select(SyntheticSelectionRecords(), out List<AngularDiagnosticSelectionSlot> secondSlots);
            AngularDiagnosticSelectionSlot narrowest = Slot(firstSlots, AngularDiagnosticStratum.Narrowest); AngularDiagnosticSelectionSlot guard = Slot(firstSlots, AngularDiagnosticStratum.GuardAdjacent); AngularDiagnosticSelectionSlot excluded = Slot(firstSlots, AngularDiagnosticStratum.WeightedExcludedLeafTop);
            Assert.That(narrowest.Selected, Is.EqualTo(2), "narrowest slot count"); Assert.That(first.Exists(record => (record.Strata & AngularDiagnosticStratum.Narrowest) != 0), Is.True, "per-stratum leaf reservation"); Assert.That(guard.Selected, Is.Zero, "no guard backfill"); Assert.That(guard.Shortfall, Is.EqualTo(3), "guard shortfall"); Assert.That(excluded.Selected, Is.EqualTo(4), "weighted exclusion slot count");
            var weightedLeaves = new HashSet<ulong>(); foreach (CandidateAngularIntervalRecord record in first) if ((record.Strata & AngularDiagnosticStratum.WeightedTop) != 0) weightedLeaves.Add(record.Path.BinaryPath); foreach (CandidateAngularIntervalRecord record in first) if ((record.Strata & AngularDiagnosticStratum.WeightedExcludedLeafTop) != 0) Assert.That(weightedLeaves.Contains(record.Path.BinaryPath), Is.False);
            Assert.That(first.Exists(record => record.Cohort == (AngularDiagnosticCohort.Coverage | AngularDiagnosticCohort.Enrichment)), Is.True, "cohort overlap"); CollectionAssert.AreEqual(Identities(first), Identities(second)); Assert.That(Slot(secondSlots, AngularDiagnosticStratum.DifferenceTop).Selected, Is.EqualTo(4), "difference-top slots"); Assert.That(Slot(secondSlots, AngularDiagnosticStratum.DifferenceBottom).Selected, Is.EqualTo(4), "difference-bottom slots");
        }
        /// <summary>Requires width-ranked coverage strata to retain width order and canonical tie breaks.</summary>
        [Test]
        public void SyntheticWidthStrataSelectExactDistinctLeavesInWidthOrder() { List<CandidateAngularIntervalRecord> selected = AngularDiagnosticSampling.Select(WidthSelectionRecords(), out List<AngularDiagnosticSelectionSlot> slots); CollectionAssert.AreEqual(new[] { "1:3:0:0", "1:2:0:0" }, WidthOrderedIdentities(selected, AngularDiagnosticStratum.Narrowest, false)); CollectionAssert.AreEqual(new[] { "1:6:0:0", "1:4:0:0" }, WidthOrderedIdentities(selected, AngularDiagnosticStratum.Widest, true)); Assert.That(Slot(slots, AngularDiagnosticStratum.Narrowest).Selected, Is.EqualTo(2)); Assert.That(Slot(slots, AngularDiagnosticStratum.Widest).Selected, Is.EqualTo(2)); }
        /// <summary>Requires descriptive aggregates to retain separate cohorts, overlap, no-backfill slots, and bounded deterministic output.</summary>
        [Test]
        public void DescriptiveAggregatesRetainCohortsOverlapAndShortfallsWithoutClassification() {
            List<CandidateAngularIntervalRecord> first = AngularDiagnosticSampling.Select(SyntheticSelectionRecords(), out List<AngularDiagnosticSelectionSlot> slots); List<CandidateAngularIntervalRecord> second = AngularDiagnosticSampling.Select(SyntheticSelectionRecords(), out List<AngularDiagnosticSelectionSlot> repeatedSlots); List<string> firstText = AngularDiagnosticSampling.FormatDescriptiveAggregates(first, slots); List<string> secondText = AngularDiagnosticSampling.FormatDescriptiveAggregates(second, repeatedSlots); int coverage = Count(first, AngularDiagnosticCohort.Coverage); int enrichment = Count(first, AngularDiagnosticCohort.Enrichment); Assert.That(firstText.Count, Is.LessThanOrEqualTo(12)); CollectionAssert.AreEqual(firstText, secondText); Assert.That(firstText[0], Does.Contain("coverage=count=" + coverage + ",complete=0,finite=" + coverage + ",usable=0,unavailable=" + coverage + ",identityMismatch=" + coverage).And.Contain("enrichment=count=" + enrichment + ",complete=0,finite=" + enrichment + ",usable=0,unavailable=" + enrichment + ",identityMismatch=" + enrichment).And.Contain("hSum=").And.Contain("eSum=").And.Contain("overlap=").And.Not.Contain("classification")); List<CandidateAngularIntervalRecord> numeric = CompleteAggregateRecords(); string numericAggregate = AngularDiagnosticSampling.FormatDescriptiveAggregates(numeric, slots)[0]; Assert.That(numericAggregate, Does.Contain("coverage=count=2,complete=2,finite=2,usable=2,unavailable=0,identityMismatch=0").And.Contain("enrichment=count=2,complete=2,finite=2,usable=2,unavailable=0,identityMismatch=0").And.Contain("overlap=1").And.Contain("hSum=").And.Contain("eMin=").And.Contain("fMax=").And.Contain("uHSum=").And.Contain("aMinusMin=").And.Contain("aPlusMax=")); Assert.That(firstText, Has.Member("stratum=GuardAdjacent,requested=3,selected=0,shortfall=3")); var oversized = new List<CandidateAngularIntervalRecord>(); for (int index = 0; index <= AngularDiagnosticSampling.MaximumSelected; index++) oversized.Add(first[0]); Assert.Throws<InvalidOperationException>(() => AngularDiagnosticSampling.FormatDescriptiveAggregates(oversized, slots));
        }
        /// <summary>Requires synthetic formatter output to preserve deterministic bounded review fields.</summary>
        [Test]
        public void SyntheticFormatterOutputIsBoundedDeterministicAndComplete()
        {
            List<CandidateAngularIntervalRecord> first = CompleteAggregateRecords(); List<CandidateAngularIntervalRecord> second = CompleteAggregateRecords();
            AngularDiagnosticSampling.Select(SyntheticSelectionRecords(), out List<AngularDiagnosticSelectionSlot> firstSlots); AngularDiagnosticSampling.Select(SyntheticSelectionRecords(), out List<AngularDiagnosticSelectionSlot> secondSlots);
            List<string> firstSelected = AngularDiagnosticSampling.FormatSelected(first); List<string> secondSelected = AngularDiagnosticSampling.FormatSelected(second); List<string> firstAggregates = AngularDiagnosticSampling.FormatDescriptiveAggregates(first, firstSlots); List<string> secondAggregates = AngularDiagnosticSampling.FormatDescriptiveAggregates(second, secondSlots);
            Assert.That(firstSelected, Is.Not.Empty); Assert.That(firstSelected.Count, Is.LessThanOrEqualTo(AngularDiagnosticSampling.MaximumSelected)); Assert.That(firstAggregates, Is.Not.Empty); Assert.That(firstAggregates.Count, Is.LessThanOrEqualTo(12)); CollectionAssert.AreEqual(firstSelected, secondSelected); CollectionAssert.AreEqual(firstAggregates, secondAggregates);
            Assert.That(firstSelected[0], Does.StartWith("id=").And.Contain("|checker=").And.Contain(",h=").And.Contain(",difference=").And.Contain(",f=").And.Contain(",uH=").And.Contain(",a-=").And.Contain(",a+=").And.Contain(",signal=").And.Contain(",available=").And.Contain(",unavailable=").And.Contain("|classification=Unclassified"));
            Assert.That(firstAggregates[0], Does.Contain("coverage=count=").And.Contain("enrichment=count=").And.Contain("overlap=")); Assert.That(firstAggregates, Has.Member("stratum=GuardAdjacent,requested=3,selected=0,shortfall=3"));
        }

        /// <summary>Requires the exact terminal mesh to yield deterministic bounded and separately tagged cohorts.</summary>
        [Test]
        public void MinimumGrazingTerminalIntervalsSelectDeterministicBoundedCohorts()
        {
            TerminalEvidence current = CaptureTerminalEvidence(); TerminalEvidence repeated = CaptureTerminalEvidence(); AssertTerminalIdentity(current, repeated);
            Assert.That(current.Result.StopState, Is.EqualTo(LightSpaceOracleStopState.EvaluationCap)); Assert.That(current.Result.Evaluations, Is.EqualTo(4000000)); Assert.That(current.Result.Panels, Is.EqualTo(4671)); Assert.That(current.Result.MaximumDepth, Is.EqualTo(15));
            Assert.That(current.Leaves.Count, Is.EqualTo(2335)); Assert.That(current.Records.Count, Is.LessThanOrEqualTo(119085)); Assert.That(current.Records.Count * 50, Is.LessThanOrEqualTo(5954250)); Assert.That(current.Selected.Count, Is.LessThanOrEqualTo(32)); Assert.That(current.Slots.Count, Is.EqualTo(11));
            Assert.That(Bits(current.Radial), Is.EqualTo(0x3EFF2BA9AE4C980FL));
            Assert.That(BitConverter.DoubleToInt64Bits(current.RecomposedAngular), Is.EqualTo(BitConverter.DoubleToInt64Bits(0.081611288762183473d)));
            Assert.That(Bits(current.Recomposed), Is.EqualTo(0x3FB4E66CF2D2B301L)); Assert.That(Bits(current.Committed), Is.EqualTo(0x3FB4E66CF2D2B301L));
            Assert.That(Count(current.Selected, AngularDiagnosticCohort.Coverage), Is.LessThanOrEqualTo(16)); Assert.That(Count(current.Selected, AngularDiagnosticCohort.Enrichment), Is.LessThanOrEqualTo(16)); AssertSlots(current.Slots); foreach (CandidateAngularIntervalRecord record in current.Records) Assert.That(record.TargetBits, Is.EqualTo(Bits(IndependentOracleContract.CandidateBaseTarget))); foreach (CandidateAngularIntervalRecord record in current.Selected) Assert.That(record.Strata, Is.Not.EqualTo(AngularDiagnosticStratum.None));
        }
        /// <summary>Requires selected records to retain independent higher-order facts or an explicit unavailable state.</summary>
        [Test]
        public void SelectedTerminalIntervalsRetainIndependentHigherOrderEvidence()
        {
            TerminalEvidence current = CaptureTerminalEvidence(); int work = 0;
            foreach (CandidateAngularIntervalRecord record in current.Selected)
            {
                AssertBoundaryAgreement(record); AngularDiagnosticSelectedEvidence evidence = record.SelectedEvidence; Assert.That(evidence, Is.Not.Null, record.Identity); work += evidence.Check.Work;
                Assert.That(evidence.Target, Is.EqualTo(IndependentOracleContract.CandidateBaseTarget)); Assert.That(evidence.UsableReference || evidence.UnavailableReason != AngularDiagnosticUnavailableReason.None, Is.True, record.Identity); Assert.That(evidence.Check.Finite, Is.True, record.Identity); Assert.That(evidence.UnavailableReason, Is.EqualTo(evidence.UsableReference ? AngularDiagnosticUnavailableReason.None : AngularDiagnosticUnavailableReason.UncertaintyRejected), record.Identity);
            }
            Assert.That(work, Is.LessThanOrEqualTo(29792)); Assert.That(4000000 + current.Records.Count * 50 + work, Is.LessThanOrEqualTo(9984042)); Assert.That(AngularDiagnosticSampling.SelectedEvidenceIsComplete(current.Selected), Is.True); CollectionAssert.AreEqual(AngularDiagnosticSampling.FormatSelected(current.Selected), AngularDiagnosticSampling.FormatSelected(CaptureTerminalEvidence().Selected));
            EmitTerminalEvidence(current);
        }
        /// <summary>Audits the calculation boundary so candidate numerical types cannot enter the checker DTO.</summary>
        [Test]
        public void AngularDiagnosticCheckerHasNoCandidateNumericalDependencies()
        {
            FieldInfo[] fields = typeof(AngularDiagnosticCalculationInput).GetFields(BindingFlags.Instance | BindingFlags.NonPublic); var names = new List<string>(); foreach (FieldInfo field in fields) names.Add(field.Name); names.Sort(StringComparer.Ordinal);
            CollectionAssert.AreEqual(new[] { "branch", "ndotVBits", "pBits", "radialBits", "thetaLeftBits", "thetaRightBits" }, names);
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "jp.penguin.purebase", "Tests", "Daily", "Editor", "PureBasePbrMultipleScatteringIndependentOracleCandidateAngularDiagnosticQuadrature.cs"); string source = File.ReadAllText(path);
            string[] forbidden = { "LightSpaceOracleCandidate", "LightSpaceOracleContractAligned", "IndependentOracleContract", "IndependentOracleReference", "IndependentOracleAdaptive", "Kronrod", "IndependentOracleWitness", "Shared", "CandidateAngularIntervalRecord", "EvaluateScalar" };
            foreach (string dependency in forbidden) Assert.That(source.Contains(dependency), Is.False, dependency);
            foreach (MethodInfo method in typeof(AngularDiagnosticQuadrature).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)) { Assert.That(method.ReturnType.Name.Contains("Candidate"), Is.False, method.Name); foreach (ParameterInfo parameter in method.GetParameters()) { Assert.That(parameter.ParameterType.Name.Contains("Candidate"), Is.False, method.Name); Assert.That(parameter.Name.IndexOf("candidate", StringComparison.OrdinalIgnoreCase), Is.LessThan(0), method.Name); Assert.That(parameter.Name.IndexOf("weight", StringComparison.OrdinalIgnoreCase), Is.LessThan(0), method.Name); Assert.That(parameter.Name.IndexOf("cohort", StringComparison.OrdinalIgnoreCase), Is.LessThan(0), method.Name); } }
        }

        /// <summary>Runs the frozen terminal scheduler and copies completed leaves only after it has returned.</summary>
        private static TerminalEvidence CaptureTerminalEvidence()
        {
            IndependentOracleInput input = MinimumGrazingInput(); var scheduler = new LightSpaceOracleCandidateScheduler(input, IndependentOracleContract.CandidateBaseTarget, null); LightSpaceOracleResult result = scheduler.Run(); List<LightSpaceOracleCandidateLeaf> leaves = ReadLeaves(scheduler);
            List<LightSpaceOracleCandidateLeaf> beforeReplay = new List<LightSpaceOracleCandidateLeaf>(leaves); List<CandidateAngularIntervalRecord> records = AngularDiagnosticSampling.Capture(input, leaves); List<CandidateAngularIntervalRecord> selected = AngularDiagnosticSampling.Select(records, out List<AngularDiagnosticSelectionSlot> slots); double recomposedAngular = AngularDiagnosticSampling.RecomposeAngular(records); RecomposeTerminalComponents(leaves, records, out double radial, out double recomposed, out double committed); AssertLeavesUnchanged(beforeReplay, ReadLeaves(scheduler));
            return new TerminalEvidence(input, result, leaves, records, selected, slots, recomposedAngular, radial, recomposed, committed);
        }

        /// <summary>Replays frozen outer and leaf reductions from complete interval provenance.</summary>
        private static void RecomposeTerminalComponents(IList<LightSpaceOracleCandidateLeaf> leaves, IList<CandidateAngularIntervalRecord> records, out double radial, out double recomposed, out double committed)
        {
            var radialValues = new List<double>(); var recomposedValues = new List<double>(); int start = 0;
            while (start < records.Count) { int end = start + 1; while (end < records.Count && records[start].Path.CompareSpatial(records[end].Path) == 0) end++; RecomposeLeaf(records, start, end, out double leafRadial, out double leafRecomposed); radialValues.Add(leafRadial); recomposedValues.Add(leafRecomposed); start = end; }
            var committedValues = new List<double>(leaves.Count); foreach (LightSpaceOracleCandidateLeaf leaf in leaves) committedValues.Add(leaf.Error);
            radial = PairwiseReduce(radialValues); recomposed = PairwiseReduce(recomposedValues); committed = PairwiseReduce(committedValues);
        }

        /// <summary>Replays one leaf's frozen 9/17 outer rules and composed error from its atomic records.</summary>
        private static void RecomposeLeaf(IList<CandidateAngularIntervalRecord> records, int start, int end, out double radial, out double recomposed)
        {
            var values = new double[17]; var weights = new double[17]; double angular = 0.0d;
            for (int index = start; index < end; index++) { CandidateAngularIntervalRecord record = records[index]; values[record.OuterIndex] += record.Q33; weights[record.OuterIndex] = record.OuterWeight; angular += record.WeightedContribution; }
            double q17 = 0.0d; double q9 = 0.0d; LightSpaceOracleCandidateRuleNode[] coarse = LightSpaceOracleCandidateQuadrature.ClenshawCurtis(9);
            for (int index = 0; index < values.Length; index++) q17 += weights[index] * values[index];
            for (int index = 0; index < coarse.Length; index++) q9 += coarse[index].Weight * values[index * 2];
            double half = records[start].RadialHalf; q17 *= half; q9 *= half; radial = Math.Abs(q17 - q9); recomposed = LightSpaceOracleContractAlignedCandidate.ComposeLeafError(q9, q17, angular);
        }

        /// <summary>Applies the scheduler's pairwise reduction to a copied observation sequence.</summary>
        private static double PairwiseReduce(IList<double> values)
        {
            var copy = new double[values.Count]; for (int index = 0; index < copy.Length; index++) copy[index] = values[index];
            for (int count = copy.Length; count > 1; count = (count + 1) / 2) { int pairs = count / 2; for (int index = 0; index < pairs; index++) copy[index] = copy[index * 2] + copy[index * 2 + 1]; if (count % 2 != 0) copy[pairs] = copy[count - 1]; }
            return copy.Length == 0 ? double.NaN : copy[0];
        }

        /// <summary>Builds the exact raw-binary64 minimum-grazing Normal input.</summary>
        private static IndependentOracleInput MinimumGrazingInput() => new IndependentOracleInput(BitConverter.Int64BitsToDouble(0x3FB6C8B439581062L), BitConverter.Int64BitsToDouble(0L), IndependentOracleBranch.Normal);
        /// <summary>Copies the scheduler's private completed-leaf collection after terminal execution.</summary>
        private static List<LightSpaceOracleCandidateLeaf> ReadLeaves(LightSpaceOracleCandidateScheduler scheduler)
        {
            FieldInfo field = typeof(LightSpaceOracleCandidateScheduler).GetField("leaves", BindingFlags.Instance | BindingFlags.NonPublic); Assert.That(field, Is.Not.Null); var result = new List<LightSpaceOracleCandidateLeaf>(); IEnumerable source = field.GetValue(scheduler) as IEnumerable; Assert.That(source, Is.Not.Null); foreach (object item in source) result.Add((LightSpaceOracleCandidateLeaf)item); return result;
        }

        /// <summary>Requires independently reconstructed boundaries to match one candidate provenance interval.</summary>
        private static void AssertBoundaryAgreement(CandidateAngularIntervalRecord record)
        {
            long[] actual = AngularDiagnosticQuadrature.ReconstructBoundaryBits(record.CalculationInput); AngularDiagnosticBoundaryKind[] kinds = AngularDiagnosticQuadrature.ReconstructBoundaryKinds(record.CalculationInput);
            Assert.That(record.PBits, Is.EqualTo(0x3FB6C8B439581062L)); Assert.That(record.NdotVBits, Is.Zero); Assert.That(record.Branch, Is.EqualTo(IndependentOracleBranch.Normal)); Assert.That(actual, Is.EqualTo(record.BoundaryBits)); Assert.That(kinds, Is.EqualTo(record.CandidateBoundaryKinds)); Assert.That(record.AtomicIndex, Is.LessThan(record.BoundaryBits.Length - 1)); Assert.That(record.SelectedEvidence.IdentityState, Is.EqualTo(AngularDiagnosticIdentityState.Compatible));
        }

        /// <summary>Counts records carrying one independently retained cohort tag.</summary>
        private static int Count(IList<CandidateAngularIntervalRecord> records, AngularDiagnosticCohort cohort) { int count = 0; foreach (CandidateAngularIntervalRecord record in records) if ((record.Cohort & cohort) != 0) count++; return count; }
        /// <summary>Builds isolated uncertainty evidence from fixed finite checker facts.</summary>
        private static AngularDiagnosticSelectedEvidence Evidence(double q17, double q33, double g127, double g251, double g503, AngularDiagnosticCalculationInput input) => Evidence(new AngularDiagnosticCheck(q17, q33, g127, g251, g503, 931, true, AngularDiagnosticCheckStopState.Accepted, "fixture"), q17, q33, input);
        /// <summary>Builds synthetic provenance without reusing checker nodes or weights as candidate facts.</summary>
        private static AngularDiagnosticSelectedEvidence Evidence(AngularDiagnosticCheck check, double q17, double q33, AngularDiagnosticCalculationInput input)
        {
            var candidateInput = new IndependentOracleInput(BitConverter.Int64BitsToDouble(input.pBits), BitConverter.Int64BitsToDouble(input.ndotVBits), input.branch); double radial = BitConverter.Int64BitsToDouble(input.radialBits); Assert.That(LightSpaceOracleContractAlignedCandidate.TryDeriveThetaPartition(candidateInput, radial, out LightSpaceOracleCandidateThetaPartition partition), Is.True);
            long left = input.thetaLeftBits; long right = input.thetaRightBits; int atomic = -1; for (int index = 0; index + 1 < partition.Boundaries.Length; index++) if (Bits(partition.Boundaries[index]) == left && Bits(partition.Boundaries[index + 1]) == right) { atomic = index; break; }
            return new AngularDiagnosticSelectedEvidence(check, q17, q33, input, Bits(partition.Boundaries), AngularDiagnosticSampling.CandidateBoundaryKinds(candidateInput, radial, partition.Boundaries), atomic, left, right, AngularDiagnosticSampling.CandidateFejerBits(17), AngularDiagnosticSampling.CandidateFejerBits(33), IndependentOracleContract.CandidateBaseTarget);
        }
        /// <summary>Builds a frozen ten-record selection universe with one deliberate same-leaf replacement.</summary>
        private static List<CandidateAngularIntervalRecord> SyntheticSelectionRecords()
        {
            var records = new List<CandidateAngularIntervalRecord>(); records.Add(SyntheticRecord(0, 0, 1.0d, 2.0d)); records.Add(SyntheticRecord(1, 0, 2.0d, 2.1d)); records.Add(SyntheticRecord(2, 0, 3.0d, 1.2d)); records.Add(SyntheticRecord(2, 1, 4.0d, 0.1d));
            records.Add(SyntheticRecord(3, 0, 5.0d, 2.2d)); records.Add(SyntheticRecord(4, 0, 6.0d, 2.3d)); records.Add(SyntheticRecord(5, 0, 7.0d, 2.4d)); records.Add(SyntheticRecord(6, 0, 8.0d, 2.5d)); records.Add(SyntheticRecord(7, 0, 9.0d, 2.6d)); records.Add(SyntheticRecord(8, 0, 10.0d, 2.7d)); return records;
        }
        /// <summary>Builds width-ranked leaves with deterministic canonical ties for both width strata.</summary>
        private static List<CandidateAngularIntervalRecord> WidthSelectionRecords() => new List<CandidateAngularIntervalRecord> { SyntheticRecord(0, 0, 1.0d, 1.0d), SyntheticRecord(1, 0, 2.0d, 0.5d), SyntheticRecord(2, 0, 3.0d, 0.5d), SyntheticRecord(3, 0, 4.0d, 0.25d), SyntheticRecord(4, 0, 5.0d, 1.5d), SyntheticRecord(5, 0, 6.0d, 1.5d), SyntheticRecord(6, 0, 7.0d, 2.0d) };
        /// <summary>Gets the exact selected width-stratum ordering with canonical tie resolution.</summary>
        private static string[] WidthOrderedIdentities(IList<CandidateAngularIntervalRecord> records, AngularDiagnosticStratum stratum, bool descending) { var matches = new List<CandidateAngularIntervalRecord>(); foreach (CandidateAngularIntervalRecord record in records) if ((record.Strata & stratum) != 0) matches.Add(record); matches.Sort((left, right) => { int width = left.SelectedEvidence.CandidateThetaWidth.CompareTo(right.SelectedEvidence.CandidateThetaWidth); if (width != 0) return descending ? -width : width; int path = left.Path.CompareSpatial(right.Path); return path != 0 ? path : left.OuterIndex.CompareTo(right.OuterIndex); }); return Identities(matches); }
        /// <summary>Builds overlap-preserving finite fixtures with independently retained cohort values.</summary>
        private static List<CandidateAngularIntervalRecord> CompleteAggregateRecords() => new List<CandidateAngularIntervalRecord> { CompleteAggregateRecord(0, 0.5d, AngularDiagnosticCohort.Coverage), CompleteAggregateRecord(1, 0.6d, AngularDiagnosticCohort.Enrichment), CompleteAggregateRecord(2, 0.7d, AngularDiagnosticCohort.Coverage | AngularDiagnosticCohort.Enrichment) };
        /// <summary>Builds one exact checker-compatible record for a cohort-local numerical summary.</summary>
        private static CandidateAngularIntervalRecord CompleteAggregateRecord(ulong leaf, double radial, AngularDiagnosticCohort cohort) { var input = new IndependentOracleInput(0.5d, 1.0d, IndependentOracleBranch.Normal); var calculation = new AngularDiagnosticCalculationInput(Bits(input.P), Bits(input.NdotV), input.Branch, Bits(radial), Bits(0.0d), Bits(Math.PI)); AngularDiagnosticCheck check = AngularDiagnosticQuadrature.Check(calculation); var record = new CandidateAngularIntervalRecord(input, new LightSpaceOracleCandidateLeaf(new IndependentOracleCanonicalPath(1, leaf), 0.0d, 1.0d, 0.0d, 0.0d), 0, 0.0d, 0, radial, new[] { 0.0d, Math.PI }, new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Endpoint }, 1.0d, 0.5d, check.Q17, check.Q33, (int)leaf, AngularDiagnosticSampling.CandidateFejerBits(17), AngularDiagnosticSampling.CandidateFejerBits(33), IndependentOracleContract.CandidateBaseTarget); record.AddCohort(cohort); record.AddStratum(AngularDiagnosticStratum.ControlLow); record.AttachSelectedEvidence(); return record; }
        /// <summary>Builds one finite endpoint-only candidate record with a deterministic leaf identity.</summary>
        private static CandidateAngularIntervalRecord SyntheticRecord(ulong leaf, int outer, double difference, double width)
        {
            var input = new IndependentOracleInput(0.5d, 1.0d, IndependentOracleBranch.Normal); var candidateLeaf = new LightSpaceOracleCandidateLeaf(new IndependentOracleCanonicalPath(1, leaf), 0.0d, 1.0d, 0.0d, 0.0d); double[] boundaries = { 0.0d, width };
            return new CandidateAngularIntervalRecord(input, candidateLeaf, outer, 0.0d, 0, 0.5d, boundaries, new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Endpoint }, difference * 2.0d, 0.5d, 0.0d, difference, outer + (int)leaf * 2, AngularDiagnosticSampling.CandidateFejerBits(17), AngularDiagnosticSampling.CandidateFejerBits(33), IndependentOracleContract.CandidateBaseTarget);
        }
        /// <summary>Gets one exact slot by stratum without inferring a missing slot.</summary>
        private static AngularDiagnosticSelectionSlot Slot(IList<AngularDiagnosticSelectionSlot> slots, AngularDiagnosticStratum stratum) { foreach (AngularDiagnosticSelectionSlot slot in slots) if (slot.Stratum == stratum) return slot; Assert.Fail("Missing selection slot: " + stratum); return default; }
        /// <summary>Gets a raw binary64 fixture identity.</summary>
        private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
        /// <summary>Copies a fixture boundary sequence into raw binary64 identities.</summary>
        private static long[] Bits(double[] values) { var result = new long[values.Length]; for (int index = 0; index < values.Length; index++) result[index] = Bits(values[index]); return result; }
        /// <summary>Checks all fixed stratum sizes and preserves real no-backfill shortfalls.</summary>
        private static void AssertSlots(IList<AngularDiagnosticSelectionSlot> slots)
        {
            AngularDiagnosticStratum[] strata = { AngularDiagnosticStratum.ControlLow, AngularDiagnosticStratum.ControlHigh, AngularDiagnosticStratum.Narrowest, AngularDiagnosticStratum.Widest, AngularDiagnosticStratum.GuardAdjacent, AngularDiagnosticStratum.DistributionAdjacent, AngularDiagnosticStratum.SmoothNoRoot, AngularDiagnosticStratum.WeightedTop, AngularDiagnosticStratum.DifferenceTop, AngularDiagnosticStratum.DifferenceBottom, AngularDiagnosticStratum.WeightedExcludedLeafTop };
            int[] requested = { 1, 1, 2, 2, 3, 3, 4, 4, 4, 4, 4 }; bool shortfall = false; Assert.That(slots.Count, Is.EqualTo(strata.Length));
            for (int index = 0; index < strata.Length; index++)
            {
                Assert.That(slots[index].Stratum, Is.EqualTo(strata[index])); Assert.That(slots[index].Requested, Is.EqualTo(requested[index])); Assert.That(slots[index].Requested, Is.EqualTo(slots[index].Selected + slots[index].Shortfall)); shortfall |= slots[index].Shortfall > 0;
            }
            Assert.That(shortfall, Is.True);
        }
        /// <summary>Requires the two fresh terminal reproductions to retain byte-identical terminal evidence.</summary>
        private static void AssertTerminalIdentity(TerminalEvidence first, TerminalEvidence second) { Assert.That(Bits(first.Result.Value), Is.EqualTo(Bits(second.Result.Value))); Assert.That(Bits(first.Result.EstimatedError), Is.EqualTo(Bits(second.Result.EstimatedError))); Assert.That(first.Result.StopState, Is.EqualTo(second.Result.StopState)); Assert.That(first.Result.Evaluations, Is.EqualTo(second.Result.Evaluations)); Assert.That(first.Result.Panels, Is.EqualTo(second.Result.Panels)); Assert.That(first.Result.MaximumDepth, Is.EqualTo(second.Result.MaximumDepth)); Assert.That(first.Result.Topology, Is.EqualTo(second.Result.Topology)); Assert.That(Bits(first.RecomposedAngular), Is.EqualTo(Bits(second.RecomposedAngular))); AssertLeavesUnchanged(first.Leaves, second.Leaves); CollectionAssert.AreEqual(Identities(first.Records), Identities(second.Records)); CollectionAssert.AreEqual(Identities(first.Selected), Identities(second.Selected)); }
        /// <summary>Emits only fully validated immutable terminal evidence through the NUnit test output channel.</summary>
        private static void EmitTerminalEvidence(TerminalEvidence evidence)
        {
            List<string> selected = AngularDiagnosticSampling.FormatSelected(evidence.Selected); List<string> aggregates = AngularDiagnosticSampling.FormatDescriptiveAggregates(evidence.Selected, evidence.Slots);
            Assert.That(selected, Is.Not.Empty); Assert.That(selected.Count, Is.LessThanOrEqualTo(AngularDiagnosticSampling.MaximumSelected)); Assert.That(aggregates, Is.Not.Empty); Assert.That(aggregates.Count, Is.LessThanOrEqualTo(12));
            foreach (string value in selected) TestContext.Out.WriteLine(value);
            foreach (string value in aggregates) TestContext.Out.WriteLine(value);
        }
        /// <summary>Requires observer-side replay to leave copied committed leaves byte-identical.</summary>
        private static void AssertLeavesUnchanged(IList<LightSpaceOracleCandidateLeaf> before, IList<LightSpaceOracleCandidateLeaf> after) { Assert.That(after.Count, Is.EqualTo(before.Count)); for (int index = 0; index < before.Count; index++) { Assert.That(before[index].Path.CompareSpatial(after[index].Path), Is.Zero); Assert.That(BitConverter.DoubleToInt64Bits(before[index].Left), Is.EqualTo(BitConverter.DoubleToInt64Bits(after[index].Left))); Assert.That(BitConverter.DoubleToInt64Bits(before[index].Right), Is.EqualTo(BitConverter.DoubleToInt64Bits(after[index].Right))); Assert.That(BitConverter.DoubleToInt64Bits(before[index].Value), Is.EqualTo(BitConverter.DoubleToInt64Bits(after[index].Value))); Assert.That(BitConverter.DoubleToInt64Bits(before[index].Error), Is.EqualTo(BitConverter.DoubleToInt64Bits(after[index].Error))); } }
        /// <summary>Builds a deterministic selected-identity sequence for repeat comparison.</summary>
        private static string[] Identities(IList<CandidateAngularIntervalRecord> records) { var result = new string[records.Count]; for (int index = 0; index < records.Count; index++) result[index] = records[index].Identity; return result; }
        /// <summary>Classifies one isolated synthetic fixture without granting terminal interpretation authority.</summary>
        private static AngularDiagnosticClassification Classify(SyntheticClassifierFixture fixture) => AngularDiagnosticSyntheticClassifier.Classify(fixture.Build());
        /// <summary>Describes one synthetic B conclusion without classifying terminal evidence.</summary>
        private static AngularDiagnosticBConclusion DescribeB(SyntheticClassifierFixture fixture) => AngularDiagnosticSyntheticClassifier.DescribeB(fixture.Build());
        /// <summary>Builds one isolated synthetic classifier fixture with no terminal evidence.</summary>
        private static SyntheticClassifierFixture Fixture() => new SyntheticClassifierFixture();
        /// <summary>Requires an analytic fixture value to remain within a documented ULP allowance.</summary>
        private static void AssertWithinUlps(double actual, double expected, ulong allowance) { ulong actualBits = unchecked((ulong)Bits(actual)); ulong expectedBits = unchecked((ulong)Bits(expected)); ulong distance = actualBits >= expectedBits ? actualBits - expectedBits : expectedBits - actualBits; Assert.That(distance, Is.LessThanOrEqualTo(allowance)); }

        /// <summary>Stores immutable terminal state captured before observer-side replay.</summary>
        private sealed class TerminalEvidence
        {
            /// <summary>Initializes terminal state and bounded diagnostic records.</summary>
            internal TerminalEvidence(IndependentOracleInput input, LightSpaceOracleResult result, List<LightSpaceOracleCandidateLeaf> leaves, List<CandidateAngularIntervalRecord> records, List<CandidateAngularIntervalRecord> selected, List<AngularDiagnosticSelectionSlot> slots, double recomposedAngular, double radial, double recomposed, double committed) { Input = input; Result = result; Leaves = leaves; Records = records; Selected = selected; Slots = slots; RecomposedAngular = recomposedAngular; Radial = radial; Recomposed = recomposed; Committed = committed; }
            /// <summary>Gets the exact raw terminal input.</summary>
            internal IndependentOracleInput Input { get; }
            /// <summary>Gets the immutable terminal scheduler result.</summary>
            internal LightSpaceOracleResult Result { get; }
            /// <summary>Gets the post-return copied committed leaves.</summary>
            internal List<LightSpaceOracleCandidateLeaf> Leaves { get; }
            /// <summary>Gets complete canonical interval provenance.</summary>
            internal List<CandidateAngularIntervalRecord> Records { get; }
            /// <summary>Gets the bounded selected union.</summary>
            internal List<CandidateAngularIntervalRecord> Selected { get; }
            /// <summary>Gets exact cohort slot and shortfall evidence.</summary>
            internal List<AngularDiagnosticSelectionSlot> Slots { get; }
            /// <summary>Gets the ownership-ordered angular recomposition.</summary>
            internal double RecomposedAngular { get; }
            /// <summary>Gets the outer 9/17 radial component reconstruction.</summary>
            internal double Radial { get; }
            /// <summary>Gets the terminal leaf-error recomposition from atomic records.</summary>
            internal double Recomposed { get; }
            /// <summary>Gets the committed terminal leaf-error reduction.</summary>
            internal double Committed { get; }
        }

    }
}
