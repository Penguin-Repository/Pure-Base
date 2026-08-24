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

// Captures candidate provenance and selects bounded immutable angular diagnostic evidence.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PureBase.Tests.Daily
{
    /// <summary>Stores immutable selected checker values and their explicit comparison state.</summary>
    internal sealed class AngularDiagnosticSelectedEvidence
    {
        /// <summary>Initializes selected evidence from separately frozen candidate provenance and independent reconstruction.</summary>
        internal AngularDiagnosticSelectedEvidence(AngularDiagnosticCheck check, double candidateQ17, double candidateQ33, AngularDiagnosticCalculationInput input, long[] candidateBoundaryBits, AngularDiagnosticBoundaryKind[] candidateBoundaryKinds, int candidateAtomicIndex, long candidateAtomicLeftBits, long candidateAtomicRightBits, AngularDiagnosticRuleBits candidateFejer17, AngularDiagnosticRuleBits candidateFejer33, double target)
        {
            Check = check; CandidateQ17 = candidateQ17; CandidateQ33 = candidateQ33; Target = target; TargetBits = BitConverter.DoubleToInt64Bits(target);
            H = check.G503; E = Math.Abs(candidateQ33 - candidateQ17); DL = Math.Abs(check.G251 - check.G127); DH = Math.Abs(check.G503 - check.G251); F = 128.0d * Ulp(MaximumMagnitude(candidateQ17, candidateQ33, check)); UH = Math.Max(DH, F); AMinus = Math.Max(0.0d, Math.Abs(H - candidateQ33) - UH); APlus = Math.Abs(H - candidateQ33) + UH;
            RatioState = E > 0.0d ? AngularDiagnosticRatioState.Available : AngularDiagnosticRatioState.UnavailableZeroIndicator; RatioValue = E > 0.0d ? Math.Abs(H - candidateQ33) / E : double.NaN;
            IntervalSignal = Signal(E, AMinus, APlus, F);
            this.candidateBoundaryBits = Copy(candidateBoundaryBits); this.candidateBoundaryKinds = Copy(candidateBoundaryKinds); CandidateAtomicIndex = candidateAtomicIndex; CandidateAtomicLeftBits = candidateAtomicLeftBits; CandidateAtomicRightBits = candidateAtomicRightBits; CandidateThetaWidth = BitConverter.Int64BitsToDouble(candidateAtomicRightBits) - BitConverter.Int64BitsToDouble(candidateAtomicLeftBits); CandidateThetaWidthBits = BitConverter.DoubleToInt64Bits(CandidateThetaWidth); CandidateEndpointAdjacent = candidateAtomicIndex >= 0 && candidateAtomicIndex + 1 < this.candidateBoundaryBits.Length && (candidateAtomicIndex == 0 || candidateAtomicIndex + 1 == this.candidateBoundaryBits.Length - 1); CandidateGuardAdjacent = Adjacent(this.candidateBoundaryKinds, candidateAtomicIndex, AngularDiagnosticBoundaryKind.Guard); CandidateDistributionAdjacent = Adjacent(this.candidateBoundaryKinds, candidateAtomicIndex, AngularDiagnosticBoundaryKind.Distribution); CandidateFejer17 = Copy(candidateFejer17); CandidateFejer33 = Copy(candidateFejer33);
            reconstructedBoundaryBits = AngularDiagnosticQuadrature.ReconstructBoundaryBits(input); reconstructedBoundaryKinds = AngularDiagnosticQuadrature.ReconstructBoundaryKinds(input); ReconstructedTopologyMask = TopologyMask(reconstructedBoundaryBits); CheckerFejer17 = AngularDiagnosticQuadrature.FejerBits(17); CheckerFejer33 = AngularDiagnosticQuadrature.FejerBits(33);
            CheckerAtomicIndex = MapAtomicInterval(reconstructedBoundaryBits, candidateAtomicLeftBits, candidateAtomicRightBits); CheckerAtomicLeftBits = CheckerAtomicIndex < 0 ? 0L : reconstructedBoundaryBits[CheckerAtomicIndex]; CheckerAtomicRightBits = CheckerAtomicIndex < 0 ? 0L : reconstructedBoundaryBits[CheckerAtomicIndex + 1];
            IdentityState = Same(CandidateBoundaryBits, ReconstructedBoundaryBits) && Same(CandidateBoundaryKinds, ReconstructedBoundaryKinds) && CandidateAtomicIndex == CheckerAtomicIndex && CandidateAtomicIndex >= 0 && CandidateAtomicIndex + 1 < CandidateBoundaryBits.Length && CandidateAtomicLeftBits == CandidateBoundaryBits[CandidateAtomicIndex] && CandidateAtomicRightBits == CandidateBoundaryBits[CandidateAtomicIndex + 1] && CandidateAtomicLeftBits == CheckerAtomicLeftBits && CandidateAtomicRightBits == CheckerAtomicRightBits && Same(CandidateFejer17.Coordinates, CheckerFejer17.Coordinates) && Same(CandidateFejer17.Weights, CheckerFejer17.Weights) && Same(CandidateFejer33.Coordinates, CheckerFejer33.Coordinates) && Same(CandidateFejer33.Weights, CheckerFejer33.Weights) && WithinUlps(candidateQ17, check.Q17, CandidateCheckerReplayUlpAllowance) && WithinUlps(candidateQ33, check.Q33, CandidateCheckerReplayUlpAllowance) ? AngularDiagnosticIdentityState.Compatible : AngularDiagnosticIdentityState.Inconsistent;
            UsableReference = IdentityState == AngularDiagnosticIdentityState.Compatible && check.Finite && Finite(candidateQ17) && Finite(candidateQ33) && Finite(H) && Finite(E) && Finite(DL) && Finite(DH) && Finite(F) && Finite(UH) && Finite(AMinus) && Finite(APlus) && DH <= Math.Max(DL / 4.0d, F) && UH <= Target && (E > 0.0d ? UH <= Math.Max(E / 4.0d, F) : UH <= F);
            UnavailableReason = UsableReference ? AngularDiagnosticUnavailableReason.None : !check.Finite && check.StopState == AngularDiagnosticCheckStopState.InvalidInput ? AngularDiagnosticUnavailableReason.InvalidInput : !check.Finite && check.StopState == AngularDiagnosticCheckStopState.RuleGenerationFailure ? AngularDiagnosticUnavailableReason.RuleGenerationFailure : !check.Finite ? AngularDiagnosticUnavailableReason.NonFiniteCalculation : IdentityState == AngularDiagnosticIdentityState.Inconsistent ? AngularDiagnosticUnavailableReason.IdentityInconsistency : AngularDiagnosticUnavailableReason.UncertaintyRejected;
        }
        /// <summary>Gets the documented binary64 replay allowance shared by both independent Fejer checks.</summary>
        internal const ulong CandidateCheckerReplayUlpAllowance = 128UL;

        /// <summary>Gets the independent rule result including finite and stop semantics.</summary>
        internal AngularDiagnosticCheck Check { get; }
        /// <summary>Gets candidate Fejer-II values used by the checker comparison.</summary>
        internal double CandidateQ17 { get; }
        internal double CandidateQ33 { get; }
        /// <summary>Gets the fixed candidate target used by the uncertainty gate.</summary>
        internal double Target { get; }
        /// <summary>Gets the immutable raw binary64 identity of the candidate target.</summary>
        internal long TargetBits { get; }
        /// <summary>Gets retained Gauss-Legendre differences, floor, uncertainty, and interval endpoints.</summary>
        internal double H { get; }
        internal double E { get; }
        internal double DL { get; }
        internal double DH { get; }
        internal double F { get; }
        internal double UH { get; }
        internal double AMinus { get; }
        internal double APlus { get; }
        /// <summary>Gets explicit candidate-indicator ratio state.</summary>
        internal AngularDiagnosticRatioState RatioState { get; }
        /// <summary>Gets the descriptive comparison ratio only when the candidate indicator is nonzero.</summary>
        internal double RatioValue { get; }
        /// <summary>Gets whether the descriptive comparison ratio is defined.</summary>
        internal bool HasRatio => RatioState == AngularDiagnosticRatioState.Available;
        /// <summary>Gets the frozen numerical interval signal without terminal classification authority.</summary>
        internal AngularDiagnosticIntervalSignal IntervalSignal { get; }
        /// <summary>Gets whether the exact floor-consistent predicate holds.</summary>
        internal bool FloorConsistent => IntervalSignal == AngularDiagnosticIntervalSignal.FloorConsistent;
        /// <summary>Gets whether this is a usable independent reference.</summary>
        internal bool UsableReference { get; }
        /// <summary>Gets a concrete reason when a usable reference is unavailable.</summary>
        internal AngularDiagnosticUnavailableReason UnavailableReason { get; }
        /// <summary>Gets the fail-closed candidate-versus-checker identity result.</summary>
        internal AngularDiagnosticIdentityState IdentityState { get; }
        /// <summary>Gets separately frozen candidate boundary provenance.</summary>
        internal long[] CandidateBoundaryBits => Copy(candidateBoundaryBits);
        internal AngularDiagnosticBoundaryKind[] CandidateBoundaryKinds => Copy(candidateBoundaryKinds);
        internal int CandidateAtomicIndex { get; }
        internal AngularDiagnosticRuleBits CandidateFejer17 { get; }
        internal AngularDiagnosticRuleBits CandidateFejer33 { get; }
        /// <summary>Gets candidate-owned atomic endpoint bits and the matched checker atomic mapping.</summary>
        internal long CandidateAtomicLeftBits { get; }
        internal long CandidateAtomicRightBits { get; }
        /// <summary>Gets the candidate-owned atomic theta width and its raw binary64 identity.</summary>
        internal double CandidateThetaWidth { get; }
        internal long CandidateThetaWidthBits { get; }
        /// <summary>Gets candidate-owned endpoint, guard, and distribution adjacency facts.</summary>
        internal bool CandidateEndpointAdjacent { get; }
        internal bool CandidateGuardAdjacent { get; }
        internal bool CandidateDistributionAdjacent { get; }
        internal int CheckerAtomicIndex { get; }
        internal long CheckerAtomicLeftBits { get; }
        internal long CheckerAtomicRightBits { get; }
        /// <summary>Gets independently reconstructed endpoint-inclusive checker topology facts.</summary>
        internal long[] ReconstructedBoundaryBits => Copy(reconstructedBoundaryBits);
        internal AngularDiagnosticBoundaryKind[] ReconstructedBoundaryKinds => Copy(reconstructedBoundaryKinds);
        internal AngularDiagnosticTopologyMask ReconstructedTopologyMask { get; }
        /// <summary>Gets independent Fejer-II raw rule identities generated by the checker.</summary>
        internal AngularDiagnosticRuleBits CheckerFejer17 { get; }
        internal AngularDiagnosticRuleBits CheckerFejer33 { get; }
        /// <summary>Gets the terminal-safe classification signal, which remains intentionally unclassified.</summary>
        internal AngularDiagnosticClassification Classification => AngularDiagnosticClassification.Unclassified;

        private readonly long[] candidateBoundaryBits;
        private readonly AngularDiagnosticBoundaryKind[] candidateBoundaryKinds;
        private readonly long[] reconstructedBoundaryBits;
        private readonly AngularDiagnosticBoundaryKind[] reconstructedBoundaryKinds;
        private static double MaximumMagnitude(double q17, double q33, AngularDiagnosticCheck check) => Math.Max(Math.Max(Math.Abs(q17), Math.Abs(q33)), Math.Max(Math.Abs(check.G127), Math.Max(Math.Abs(check.G251), Math.Abs(check.G503))));
        private static AngularDiagnosticIntervalSignal Signal(double e, double aMinus, double aPlus, double floor)
        {
            if (e > Math.Max(4.0d * aPlus, 4.0d * floor)) return AngularDiagnosticIntervalSignal.B;
            if (aMinus > Math.Max(e / 2.0d, 4.0d * floor)) return AngularDiagnosticIntervalSignal.A;
            return e <= 4.0d * floor && aPlus <= 4.0d * floor ? AngularDiagnosticIntervalSignal.FloorConsistent : AngularDiagnosticIntervalSignal.Mixed;
        }
        private static AngularDiagnosticTopologyMask TopologyMask(long[] bits) => bits.Length < 2 ? AngularDiagnosticTopologyMask.None : bits.Length == 2 ? AngularDiagnosticTopologyMask.LeftEndpoint | AngularDiagnosticTopologyMask.RightEndpoint : AngularDiagnosticTopologyMask.LeftEndpoint | AngularDiagnosticTopologyMask.Interior | AngularDiagnosticTopologyMask.RightEndpoint;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Ulp(double value) { if (value == 0.0d) return double.Epsilon; long bits = BitConverter.DoubleToInt64Bits(value); return BitConverter.Int64BitsToDouble(bits + 1L) - value; }
        private static bool Same<T>(T[] left, T[] right) { if (left == null || right == null || left.Length != right.Length) return false; for (int index = 0; index < left.Length; index++) if (!EqualityComparer<T>.Default.Equals(left[index], right[index])) return false; return true; }
        private static T[] Copy<T>(T[] source) => source == null ? Array.Empty<T>() : (T[])source.Clone();
        private static AngularDiagnosticRuleBits Copy(AngularDiagnosticRuleBits source) => new AngularDiagnosticRuleBits(Copy(source.Coordinates), Copy(source.Weights));
        private static int MapAtomicInterval(long[] boundaries, long left, long right) { for (int index = 0; index + 1 < boundaries.Length; index++) if (boundaries[index] == left && boundaries[index + 1] == right) return index; return -1; }
        private static bool Adjacent(AngularDiagnosticBoundaryKind[] kinds, int atomicIndex, AngularDiagnosticBoundaryKind kind) => atomicIndex >= 0 && atomicIndex + 1 < kinds.Length && (kinds[atomicIndex] == kind || kinds[atomicIndex + 1] == kind);
        private static bool WithinUlps(double left, double right, ulong allowance) { if (!Finite(left) || !Finite(right) || left < 0.0d || right < 0.0d) return false; ulong leftBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(left)); ulong rightBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(right)); return leftBits >= rightBits ? leftBits - rightBits <= allowance : rightBits - leftBits <= allowance; }
    }

    /// <summary>Stores one canonical candidate-owned terminal atomic interval record.</summary>
    internal sealed class CandidateAngularIntervalRecord
    {
        /// <summary>Initializes immutable provenance and replay facts for one atomic theta interval.</summary>
        internal CandidateAngularIntervalRecord(IndependentOracleInput input, LightSpaceOracleCandidateLeaf leaf, int outerIndex, double outerCoordinate, int atomicIndex, double radial, double[] boundaries, AngularDiagnosticBoundaryKind[] candidateBoundaryKinds, double outerWeight, double radialHalf, double q17, double q33, int sourceOrdinal, AngularDiagnosticRuleBits candidateFejer17, AngularDiagnosticRuleBits candidateFejer33, double candidateTarget)
        {
            Path = leaf.Path; LeafLeftBits = Bits(leaf.Left); LeafRightBits = Bits(leaf.Right); LeafValueBits = Bits(leaf.Value); LeafErrorBits = Bits(leaf.Error); PBits = Bits(input.P); NdotVBits = Bits(input.NdotV); Branch = input.Branch;
            OuterIndex = outerIndex; OuterCoordinateBits = Bits(outerCoordinate); AtomicIndex = atomicIndex; Radial = radial; Left = boundaries[atomicIndex]; Right = boundaries[atomicIndex + 1]; AtomicLeftBits = Bits(Left); AtomicRightBits = Bits(Right); boundaryBits = CopyBits(boundaries); CandidateTopologyMask = TopologyMask(boundaryBits); this.candidateBoundaryKinds = CopyKinds(candidateBoundaryKinds); CandidateFejer17 = CopyRule(candidateFejer17); CandidateFejer33 = CopyRule(candidateFejer33); Target = candidateTarget; TargetBits = Bits(candidateTarget); OuterWeight = outerWeight; RadialHalf = radialHalf; Q17 = q17; Q33 = q33; CandidateScalarWork = 50; CandidateFinite = Finite(q17) && Finite(q33); CandidateRuleIdentity = "FejerII17/FejerII33"; SourceOrdinal = sourceOrdinal;
            Difference = Math.Abs(q33 - q17); WeightedContribution = outerWeight * radialHalf * Difference; cohort = AngularDiagnosticCohort.None; strata = AngularDiagnosticStratum.None;
        }

        /// <summary>Gets the terminal leaf's canonical path.</summary>
        internal IndependentOracleCanonicalPath Path { get; }
        /// <summary>Gets immutable raw input and copied terminal-leaf identities.</summary>
        internal long PBits { get; }
        internal long NdotVBits { get; }
        internal IndependentOracleBranch Branch { get; }
        internal long LeafLeftBits { get; }
        internal long LeafRightBits { get; }
        internal long LeafValueBits { get; }
        internal long LeafErrorBits { get; }
        /// <summary>Gets the outer CC17 index.</summary>
        internal int OuterIndex { get; }
        /// <summary>Gets the raw outer coordinate identity.</summary>
        internal long OuterCoordinateBits { get; }
        /// <summary>Gets the interval's atomic index within its theta partition.</summary>
        internal int AtomicIndex { get; }
        /// <summary>Gets the deterministic replay source ordinal.</summary>
        internal int SourceOrdinal { get; }
        /// <summary>Gets the sampled radial coordinate.</summary>
        internal double Radial { get; }
        /// <summary>Gets the atomic left theta endpoint.</summary>
        internal double Left { get; }
        /// <summary>Gets the atomic right theta endpoint.</summary>
        internal double Right { get; }
        /// <summary>Gets the original raw binary64 atomic endpoint identities.</summary>
        internal long AtomicLeftBits { get; }
        internal long AtomicRightBits { get; }
        /// <summary>Gets a copied endpoint-inclusive candidate topology identity.</summary>
        internal long[] BoundaryBits => CopyBits(boundaryBits);
        /// <summary>Gets the candidate-owned endpoint-inclusive topology mask.</summary>
        internal AngularDiagnosticTopologyMask CandidateTopologyMask { get; }
        /// <summary>Gets candidate-owned semantic kinds in the same retained topology order.</summary>
        internal AngularDiagnosticBoundaryKind[] CandidateBoundaryKinds => Copy(candidateBoundaryKinds);
        /// <summary>Gets raw candidate Fejer rule identities captured before checker replay.</summary>
        internal AngularDiagnosticRuleBits CandidateFejer17 { get; }
        internal AngularDiagnosticRuleBits CandidateFejer33 { get; }
        /// <summary>Gets the candidate target captured during terminal provenance enumeration.</summary>
        internal double Target { get; }
        /// <summary>Gets the candidate target's raw binary64 identity.</summary>
        internal long TargetBits { get; }
        /// <summary>Gets the candidate outer CC17 weight.</summary>
        internal double OuterWeight { get; }
        /// <summary>Gets the leaf radial half-width.</summary>
        internal double RadialHalf { get; }
        /// <summary>Gets the candidate Fejer-II order-17 value.</summary>
        internal double Q17 { get; }
        /// <summary>Gets the candidate Fejer-II order-33 value.</summary>
        internal double Q33 { get; }
        /// <summary>Gets the fixed candidate replay work and terminal calculation facts.</summary>
        internal int CandidateScalarWork { get; }
        internal bool CandidateFinite { get; }
        internal string CandidateRuleIdentity { get; }
        /// <summary>Gets the unweighted candidate angular indicator.</summary>
        internal double Difference { get; }
        /// <summary>Gets the candidate ownership-weighted angular contribution.</summary>
        internal double WeightedContribution { get; }
        /// <summary>Gets selected cohort memberships without suppressing overlaps.</summary>
        internal AngularDiagnosticCohort Cohort => cohort;
        /// <summary>Gets exact selection-stratum memberships without suppressing overlaps.</summary>
        internal AngularDiagnosticStratum Strata => strata;
        /// <summary>Gets selected immutable checker evidence after the record enters a cohort.</summary>
        internal AngularDiagnosticSelectedEvidence SelectedEvidence { get; private set; }
        /// <summary>Gets the immutable raw-bit calculation input for the isolated checker.</summary>
        internal AngularDiagnosticCalculationInput CalculationInput => new AngularDiagnosticCalculationInput(PBits, NdotVBits, Branch, Bits(Radial), Bits(Left), Bits(Right));
        /// <summary>Gets deterministic human-readable spatial identity.</summary>
        internal string Identity => Path.Depth + ":" + Path.BinaryPath + ":" + OuterIndex + ":" + AtomicIndex;
        /// <summary>Attaches the independent check exactly once after selection.</summary>
        internal void AttachSelectedEvidence() { if (SelectedEvidence == null) SelectedEvidence = new AngularDiagnosticSelectedEvidence(AngularDiagnosticQuadrature.Check(CalculationInput), Q17, Q33, CalculationInput, BoundaryBits, CandidateBoundaryKinds, AtomicIndex, AtomicLeftBits, AtomicRightBits, CandidateFejer17, CandidateFejer33, Target); }
        /// <summary>Adds one selection cohort before the attached evidence freezes the record.</summary>
        internal void AddCohort(AngularDiagnosticCohort value) { EnsureUnfrozen(); cohort |= value; }
        /// <summary>Adds one selection stratum before the attached evidence freezes the record.</summary>
        internal void AddStratum(AngularDiagnosticStratum value) { EnsureUnfrozen(); strata |= value; }
        private readonly long[] boundaryBits;
        private readonly AngularDiagnosticBoundaryKind[] candidateBoundaryKinds;
        private AngularDiagnosticCohort cohort;
        private AngularDiagnosticStratum strata;
        /// <summary>Gets raw binary64 bits from a double.</summary>
        private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
        /// <summary>Copies all candidate topology boundary bits before observer-side replay.</summary>
        private static long[] CopyBits(double[] boundaries) { var result = new long[boundaries.Length]; for (int index = 0; index < boundaries.Length; index++) result[index] = Bits(boundaries[index]); return result; }
        /// <summary>Copies retained raw binary64 topology bits before exposing them.</summary>
        private static long[] CopyBits(long[] source) => source == null ? Array.Empty<long>() : (long[])source.Clone();
        private static AngularDiagnosticBoundaryKind[] Copy(AngularDiagnosticBoundaryKind[] source) => source == null ? Array.Empty<AngularDiagnosticBoundaryKind>() : (AngularDiagnosticBoundaryKind[])source.Clone();
        private static AngularDiagnosticBoundaryKind[] CopyKinds(AngularDiagnosticBoundaryKind[] source) => Copy(source);
        private static AngularDiagnosticRuleBits CopyRule(AngularDiagnosticRuleBits source) { long[] coordinates = source.Coordinates == null ? Array.Empty<long>() : (long[])source.Coordinates.Clone(); long[] weights = source.Weights == null ? Array.Empty<long>() : (long[])source.Weights.Clone(); return new AngularDiagnosticRuleBits(coordinates, weights); }
        private static AngularDiagnosticTopologyMask TopologyMask(long[] bits) => bits.Length < 2 ? AngularDiagnosticTopologyMask.None : bits.Length == 2 ? AngularDiagnosticTopologyMask.LeftEndpoint | AngularDiagnosticTopologyMask.RightEndpoint : AngularDiagnosticTopologyMask.LeftEndpoint | AngularDiagnosticTopologyMask.Interior | AngularDiagnosticTopologyMask.RightEndpoint;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private void EnsureUnfrozen() { if (SelectedEvidence != null) throw new InvalidOperationException("Selected angular diagnostic record is frozen."); }
    }

    /// <summary>Captures candidate provenance and deterministic selections without altering scheduler state.</summary>
    internal static class AngularDiagnosticSampling
    {
        /// <summary>Gets the maximum immutable terminal interval record count.</summary>
        internal const int MaximumRecords = 119085;
        /// <summary>Gets the maximum selected union size.</summary>
        internal const int MaximumSelected = 32;
        /// <summary>Gets the maximum records reserved for either cohort.</summary>
        internal const int MaximumPerCohort = 16;
        /// <summary>Replays completed leaves into canonical atomic records after terminal scheduler return.</summary>
        internal static List<CandidateAngularIntervalRecord> Capture(IndependentOracleInput input, IList<LightSpaceOracleCandidateLeaf> leaves)
        {
            var records = new List<CandidateAngularIntervalRecord>(); var outer = LightSpaceOracleCandidateQuadrature.ClenshawCurtis(17); var coarse = LightSpaceOracleCandidateQuadrature.FejerII(17); var fine = LightSpaceOracleCandidateQuadrature.FejerII(33);
            var ordered = new List<LightSpaceOracleCandidateLeaf>(leaves); ordered.Sort((left, right) => left.Path.CompareSpatial(right.Path));
            AngularDiagnosticRuleBits candidateFejer17 = RuleBits(coarse); AngularDiagnosticRuleBits candidateFejer33 = RuleBits(fine); double target = IndependentOracleContract.CandidateBaseTarget;
            foreach (LightSpaceOracleCandidateLeaf leaf in ordered) CaptureLeaf(input, leaf, outer, coarse, fine, candidateFejer17, candidateFejer33, target, records);
            if (records.Count > MaximumRecords) throw new InvalidOperationException("Angular diagnostic provenance work cap exceeded.");
            return records;
        }

        /// <summary>Captures all candidate-owned outer nodes and atomic intervals for one completed leaf.</summary>
        private static void CaptureLeaf(IndependentOracleInput input, LightSpaceOracleCandidateLeaf leaf, LightSpaceOracleCandidateRuleNode[] outer, LightSpaceOracleCandidateRuleNode[] coarse, LightSpaceOracleCandidateRuleNode[] fine, AngularDiagnosticRuleBits candidateFejer17, AngularDiagnosticRuleBits candidateFejer33, double target, List<CandidateAngularIntervalRecord> records)
        {
            double half = (leaf.Right - leaf.Left) * 0.5d; double middle = (leaf.Right + leaf.Left) * 0.5d;
            for (int outerIndex = 0; outerIndex < outer.Length; outerIndex++) { double radial = middle + half * outer[outerIndex].Coordinate; if (!LightSpaceOracleContractAlignedCandidate.TryDeriveThetaPartition(input, radial, out LightSpaceOracleCandidateThetaPartition partition)) throw new InvalidOperationException("Candidate theta provenance is unavailable."); AngularDiagnosticBoundaryKind[] kinds = CandidateBoundaryKinds(input, radial, partition.Boundaries); for (int atomicIndex = 0; atomicIndex + 1 < partition.Boundaries.Length; atomicIndex++) records.Add(CreateRecord(input, leaf, outerIndex, outer[outerIndex].Coordinate, atomicIndex, radial, partition.Boundaries, kinds, outer[outerIndex].Weight, half, coarse, fine, records.Count, candidateFejer17, candidateFejer33, target)); }
        }

        /// <summary>Replays one candidate-owned Fejer-II 17/33 atomic interval in frozen order.</summary>
        private static CandidateAngularIntervalRecord CreateRecord(IndependentOracleInput input, LightSpaceOracleCandidateLeaf leaf, int outerIndex, double outerCoordinate, int atomicIndex, double radial, double[] boundaries, AngularDiagnosticBoundaryKind[] kinds, double outerWeight, double radialHalf, LightSpaceOracleCandidateRuleNode[] coarse, LightSpaceOracleCandidateRuleNode[] fine, int sourceOrdinal, AngularDiagnosticRuleBits candidateFejer17, AngularDiagnosticRuleBits candidateFejer33, double target)
        {
            double half = (boundaries[atomicIndex + 1] - boundaries[atomicIndex]) * 0.5d; double middle = (boundaries[atomicIndex + 1] + boundaries[atomicIndex]) * 0.5d; double q17 = 0.0d; double q33 = 0.0d;
            for (int index = 0; index < coarse.Length; index++) q17 += coarse[index].Weight * LightSpaceOracleContractAlignedCandidate.EvaluateScalar(input, radial, middle + half * coarse[index].Coordinate);
            for (int index = 0; index < fine.Length; index++) q33 += fine[index].Weight * LightSpaceOracleContractAlignedCandidate.EvaluateScalar(input, radial, middle + half * fine[index].Coordinate);
            return new CandidateAngularIntervalRecord(input, leaf, outerIndex, outerCoordinate, atomicIndex, radial, boundaries, kinds, outerWeight, radialHalf, q17 * half, q33 * half, sourceOrdinal, candidateFejer17, candidateFejer33, target);
        }

        /// <summary>Captures the candidate Fejer rule bits without obtaining them from the checker.</summary>
        internal static AngularDiagnosticRuleBits CandidateFejerBits(int order) => RuleBits(LightSpaceOracleCandidateQuadrature.FejerII(order));

        /// <summary>Derives candidate semantic topology in the candidate root ordering before checker replay.</summary>
        internal static AngularDiagnosticBoundaryKind[] CandidateBoundaryKinds(IndependentOracleInput input, double radial, double[] boundaries)
        {
            double sine = Math.Sin(Math.PI * radial * 0.5d); double u = sine * sine; double z = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - input.NdotV * input.NdotV);
            double guard = CandidateRoot(1.0e-6d, radial, u, input.NdotV, z); double distribution = CandidateDistributionRoot(input.P, radial, u, input.NdotV, z);
            if (!Finite(guard) && !Finite(distribution)) return new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Endpoint };
            if (Finite(guard) && (!Finite(distribution) || WithinUlps(guard, distribution, 32UL))) return new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Guard, AngularDiagnosticBoundaryKind.Endpoint };
            if (!Finite(guard)) return new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Distribution, AngularDiagnosticBoundaryKind.Endpoint };
            return guard < distribution ? new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Guard, AngularDiagnosticBoundaryKind.Distribution, AngularDiagnosticBoundaryKind.Endpoint } : new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Distribution, AngularDiagnosticBoundaryKind.Guard, AngularDiagnosticBoundaryKind.Endpoint };
        }

        /// <summary>Derives one candidate root using candidate-local admission semantics.</summary>
        private static double CandidateRoot(double target, double radial, double u, double v, double z)
        {
            if (!Finite(z) || z <= 0.0d || radial == 0.0d) return double.NaN;
            double cosine = (target * 0.5d - 1.0d - u * v) / z;
            if (!Finite(cosine) || cosine <= -1.0d || cosine >= 1.0d) return double.NaN;
            double theta = Math.Acos(cosine); return Finite(theta) && theta > 0.0d && theta < Math.PI ? theta : double.NaN;
        }

        /// <summary>Derives the candidate distribution transition without consulting checker topology.</summary>
        private static double CandidateDistributionRoot(double p, double radial, double u, double v, double z)
        {
            if (z <= 0.0d || p >= 1.0d) return double.NaN;
            double alphaSquared = p * p * p * p; double h2 = (1.0d - Math.Sqrt(1.0e-6d / Math.PI)) / (1.0d - alphaSquared); double target = (u + v) * (u + v) / h2;
            return Finite(target) && target >= 1.0e-6d ? CandidateRoot(target, radial, u, v, z) : double.NaN;
        }

        /// <summary>Copies one candidate rule into immutable raw binary64 arrays.</summary>
        private static AngularDiagnosticRuleBits RuleBits(LightSpaceOracleCandidateRuleNode[] rule)
        {
            var coordinates = new long[rule.Length]; var weights = new long[rule.Length];
            for (int index = 0; index < rule.Length; index++) { coordinates[index] = BitConverter.DoubleToInt64Bits(rule[index].Coordinate); weights[index] = BitConverter.DoubleToInt64Bits(rule[index].Weight); }
            return new AngularDiagnosticRuleBits(coordinates, weights);
        }

        /// <summary>Recomposes weighted angular contributions in candidate leaf, node, and atomic ownership order.</summary>
        internal static double RecomposeAngular(IList<CandidateAngularIntervalRecord> records)
        {
            var leaves = new Dictionary<string, List<CandidateAngularIntervalRecord>>();
            foreach (CandidateAngularIntervalRecord record in records) { string key = record.Path.Depth + ":" + record.Path.BinaryPath; if (!leaves.TryGetValue(key, out List<CandidateAngularIntervalRecord> values)) { values = new List<CandidateAngularIntervalRecord>(); leaves.Add(key, values); } values.Add(record); }
            var ordered = new List<List<CandidateAngularIntervalRecord>>(leaves.Values); ordered.Sort((left, right) => left[0].Path.CompareSpatial(right[0].Path)); var sums = new double[ordered.Count];
            for (int index = 0; index < ordered.Count; index++) { ordered[index].Sort(Compare); double leafSum = 0.0d; foreach (CandidateAngularIntervalRecord record in ordered[index]) leafSum += record.WeightedContribution; sums[index] = leafSum; }
            return PairwiseReduce(sums);
        }

        /// <summary>Formats bounded selected provenance without classifying the terminal evidence.</summary>
        internal static List<string> FormatSelected(IList<CandidateAngularIntervalRecord> selected)
        {
            if (selected.Count > MaximumSelected) throw new InvalidOperationException("Angular diagnostic format cap exceeded."); var result = new List<string>(selected.Count);
            foreach (CandidateAngularIntervalRecord record in selected) result.Add(Format(record));
            return result;
        }

        /// <summary>Formats bounded cohort counts and no-backfill slots without estimating prevalence or classifying evidence.</summary>
        internal static List<string> FormatDescriptiveAggregates(IList<CandidateAngularIntervalRecord> selected, IList<AngularDiagnosticSelectionSlot> slots)
        {
            if (selected.Count > MaximumSelected || slots.Count > 11) throw new InvalidOperationException("Angular diagnostic aggregate cap exceeded."); var result = new List<string>(slots.Count + 1); int overlap = 0;
            foreach (CandidateAngularIntervalRecord record in selected) if ((record.Cohort & (AngularDiagnosticCohort.Coverage | AngularDiagnosticCohort.Enrichment)) == (AngularDiagnosticCohort.Coverage | AngularDiagnosticCohort.Enrichment)) overlap++;
            result.Add("aggregate=selected=" + selected.Count + ",complete=" + SelectedEvidenceIsComplete(selected) + ",coverage=" + CohortAggregate(selected, AngularDiagnosticCohort.Coverage) + ",enrichment=" + CohortAggregate(selected, AngularDiagnosticCohort.Enrichment) + ",overlap=" + overlap);
            var ordered = new List<AngularDiagnosticSelectionSlot>(slots); ordered.Sort((left, right) => left.Stratum.CompareTo(right.Stratum)); foreach (AngularDiagnosticSelectionSlot slot in ordered) result.Add("stratum=" + slot.Stratum + ",requested=" + slot.Requested + ",selected=" + slot.Selected + ",shortfall=" + slot.Shortfall);
            return result;
        }

        /// <summary>Formats one selected record with complete candidate and checker provenance.</summary>
        private static string Format(CandidateAngularIntervalRecord record)
        {
            AngularDiagnosticSelectedEvidence evidence = record.SelectedEvidence;
            if (evidence == null) throw new InvalidOperationException("Selected angular diagnostic evidence is unattached.");
            return "id=" + record.Identity + "|candidate=p=" + Hex(record.PBits) + ",v=" + Hex(record.NdotVBits) + ",branch=" + record.Branch + ",target=CandidateBaseTarget:" + Hex(record.TargetBits) + ",leaf=" + Hex(record.LeafLeftBits) + ":" + Hex(record.LeafRightBits) + ":" + Hex(record.LeafValueBits) + ":" + Hex(record.LeafErrorBits) + ",source=" + record.SourceOrdinal + ",outer=" + record.OuterIndex + ":" + Hex(record.OuterCoordinateBits) + ",outerWeight=" + Hex(BitConverter.DoubleToInt64Bits(record.OuterWeight)) + ",radialHalf=" + Hex(BitConverter.DoubleToInt64Bits(record.RadialHalf)) + ",atomic=" + record.AtomicIndex + ":" + Hex(record.AtomicLeftBits) + ":" + Hex(record.AtomicRightBits) + ",thetaWidth=" + evidence.CandidateThetaWidth.ToString("R", CultureInfo.InvariantCulture) + ",thetaWidthBits=" + Hex(evidence.CandidateThetaWidthBits) + ",endpointAdjacent=" + evidence.CandidateEndpointAdjacent + ",guardAdjacent=" + evidence.CandidateGuardAdjacent + ",distributionAdjacent=" + evidence.CandidateDistributionAdjacent + ",radial=" + Hex(BitConverter.DoubleToInt64Bits(record.Radial)) + ",boundaries=" + Bits(record.BoundaryBits) + ",kinds=" + Kinds(record.CandidateBoundaryKinds) + ",topology=" + record.CandidateTopologyMask + ",q17=" + Hex(BitConverter.DoubleToInt64Bits(record.Q17)) + ",q33=" + Hex(BitConverter.DoubleToInt64Bits(record.Q33)) + ",difference=" + Hex(BitConverter.DoubleToInt64Bits(record.Difference)) + ",contribution=" + Hex(BitConverter.DoubleToInt64Bits(record.WeightedContribution)) + ",work=" + record.CandidateScalarWork + ",finite=" + record.CandidateFinite + ",rule=" + record.CandidateRuleIdentity + ",fejer17=" + Rule(record.CandidateFejer17) + ",fejer33=" + Rule(record.CandidateFejer33) + "|checker=roots=" + Bits(evidence.ReconstructedBoundaryBits) + ",kinds=" + Kinds(evidence.ReconstructedBoundaryKinds) + ",topology=" + evidence.ReconstructedTopologyMask + ",atomic=" + evidence.CheckerAtomicIndex + ":" + Hex(evidence.CheckerAtomicLeftBits) + ":" + Hex(evidence.CheckerAtomicRightBits) + ",fejer17=" + Rule(evidence.CheckerFejer17) + ",fejer33=" + Rule(evidence.CheckerFejer33) + ",q17=" + Hex(BitConverter.DoubleToInt64Bits(evidence.Check.Q17)) + ",q33=" + Hex(BitConverter.DoubleToInt64Bits(evidence.Check.Q33)) + ",g127=" + Hex(BitConverter.DoubleToInt64Bits(evidence.Check.G127)) + ",g251=" + Hex(BitConverter.DoubleToInt64Bits(evidence.Check.G251)) + ",h=" + Hex(BitConverter.DoubleToInt64Bits(evidence.H)) + ",dL=" + Hex(BitConverter.DoubleToInt64Bits(evidence.DL)) + ",dH=" + Hex(BitConverter.DoubleToInt64Bits(evidence.DH)) + ",f=" + Hex(BitConverter.DoubleToInt64Bits(evidence.F)) + ",uH=" + Hex(BitConverter.DoubleToInt64Bits(evidence.UH)) + ",a-=" + Hex(BitConverter.DoubleToInt64Bits(evidence.AMinus)) + ",a+=" + Hex(BitConverter.DoubleToInt64Bits(evidence.APlus)) + ",signal=" + evidence.IntervalSignal + ",ratio=" + evidence.RatioState + ":" + (evidence.HasRatio ? Hex(BitConverter.DoubleToInt64Bits(evidence.RatioValue)) : "none") + ",available=" + evidence.UsableReference + ",unavailable=" + evidence.UnavailableReason + ",identity=" + evidence.IdentityState + ",stop=" + evidence.Check.StopState + ",finite=" + evidence.Check.Finite + ",work=" + evidence.Check.Work + ",rule=" + evidence.Check.RuleIdentity + "|cohort=" + record.Cohort + "|strata=" + record.Strata + "|classification=" + evidence.Classification;
        }

        /// <summary>Audits every selected record's raw identity and independent terminal evidence.</summary>
        internal static bool SelectedEvidenceIsComplete(IList<CandidateAngularIntervalRecord> selected)
        {
            if (selected.Count > MaximumSelected) return false;
            foreach (CandidateAngularIntervalRecord record in selected) if (!RecordEvidenceIsComplete(record)) return false;
            return true;
        }

        /// <summary>Audits one selected record before it contributes a complete descriptive count.</summary>
        private static bool RecordEvidenceIsComplete(CandidateAngularIntervalRecord record)
        {
            AngularDiagnosticSelectedEvidence evidence = record.SelectedEvidence; AngularDiagnosticCalculationInput input = record.CalculationInput;
            if (evidence == null || record.Cohort == AngularDiagnosticCohort.None || record.Strata == AngularDiagnosticStratum.None || record.TargetBits != BitConverter.DoubleToInt64Bits(record.Target) || record.TargetBits != BitConverter.DoubleToInt64Bits(IndependentOracleContract.CandidateBaseTarget) || record.AtomicLeftBits != BitConverter.DoubleToInt64Bits(record.Left) || record.AtomicRightBits != BitConverter.DoubleToInt64Bits(record.Right) || record.CandidateScalarWork != 50 || !record.CandidateFinite || record.CandidateRuleIdentity != "FejerII17/FejerII33") return false;
            if (input.pBits != record.PBits || input.ndotVBits != record.NdotVBits || input.radialBits != BitConverter.DoubleToInt64Bits(record.Radial) || input.thetaLeftBits != record.AtomicLeftBits || input.thetaRightBits != record.AtomicRightBits || evidence.Check.RuleIdentity != "FejerII17/FejerII33/GaussLegendre127/GaussLegendre251/GaussLegendre503") return false;
            if (evidence.Check.Work != (evidence.Check.Finite ? 931 : 0) || evidence.Check.StopState == AngularDiagnosticCheckStopState.Accepted != evidence.Check.Finite || evidence.ReconstructedTopologyMask != record.CandidateTopologyMask || !Same(evidence.CandidateBoundaryBits, record.BoundaryBits) || !Same(evidence.CandidateBoundaryKinds, record.CandidateBoundaryKinds) || !Same(evidence.CandidateFejer17.Coordinates, record.CandidateFejer17.Coordinates) || !Same(evidence.CandidateFejer17.Weights, record.CandidateFejer17.Weights) || !Same(evidence.CandidateFejer33.Coordinates, record.CandidateFejer33.Coordinates) || !Same(evidence.CandidateFejer33.Weights, record.CandidateFejer33.Weights)) return false;
            return evidence.TargetBits == record.TargetBits && evidence.IdentityState == AngularDiagnosticIdentityState.Compatible && evidence.CandidateAtomicIndex == record.AtomicIndex && evidence.CheckerAtomicIndex == record.AtomicIndex && evidence.CandidateAtomicLeftBits == record.AtomicLeftBits && evidence.CandidateAtomicRightBits == record.AtomicRightBits && evidence.CheckerAtomicLeftBits == record.AtomicLeftBits && evidence.CheckerAtomicRightBits == record.AtomicRightBits && evidence.CandidateThetaWidth == record.Right - record.Left && evidence.CandidateThetaWidthBits == BitConverter.DoubleToInt64Bits(record.Right - record.Left) && evidence.CandidateEndpointAdjacent == (record.AtomicIndex == 0 || record.AtomicIndex + 1 == record.BoundaryBits.Length - 1) && evidence.CandidateGuardAdjacent == IsGuardAdjacent(record) && evidence.CandidateDistributionAdjacent == IsDistributionAdjacent(record) && WithinUlps(record.Q17, evidence.Check.Q17, AngularDiagnosticSelectedEvidence.CandidateCheckerReplayUlpAllowance) && WithinUlps(record.Q33, evidence.Check.Q33, AngularDiagnosticSelectedEvidence.CandidateCheckerReplayUlpAllowance) && evidence.Classification == AngularDiagnosticClassification.Unclassified;
        }

        /// <summary>Selects all fixed strata and retains every unavailable slot without backfill.</summary>
        internal static List<CandidateAngularIntervalRecord> Select(IList<CandidateAngularIntervalRecord> records) { return Select(records, out _); }

        /// <summary>Selects exact coverage and enrichment cohorts with deterministic slot evidence.</summary>
        internal static List<CandidateAngularIntervalRecord> Select(IList<CandidateAngularIntervalRecord> records, out List<AngularDiagnosticSelectionSlot> slots)
        {
            var coverage = new List<CandidateAngularIntervalRecord>(); var enrichment = new List<CandidateAngularIntervalRecord>(); slots = new List<AngularDiagnosticSelectionSlot>(); var canonical = Sorted(records, Compare);
            AddRank(coverage, canonical, (canonical.Count - 1) / 3, AngularDiagnosticStratum.ControlLow, slots); AddRank(coverage, canonical, 2 * (canonical.Count - 1) / 3, AngularDiagnosticStratum.ControlHigh, slots);
            AddDistinct(coverage, Sorted(records, ByWidthAscending), 2, AngularDiagnosticStratum.Narrowest, slots, ByWidthAscending); AddDistinct(coverage, Sorted(records, ByWidthDescending), 2, AngularDiagnosticStratum.Widest, slots, ByWidthDescending);
            AddDistinct(coverage, Filter(records, IsGuardAdjacent), 3, AngularDiagnosticStratum.GuardAdjacent, slots, Compare); AddDistinct(coverage, Filter(records, IsDistributionAdjacent), 3, AngularDiagnosticStratum.DistributionAdjacent, slots, Compare);
            AddQuantiles(coverage, Filter(records, IsSmooth), AngularDiagnosticStratum.SmoothNoRoot, slots);
            AddDistinct(enrichment, Sorted(records, ByWeightedDescending), 4, AngularDiagnosticStratum.WeightedTop, slots, ByWeightedDescending); AddDistinct(enrichment, Sorted(records, ByDifferenceDescending), 4, AngularDiagnosticStratum.DifferenceTop, slots, ByDifferenceDescending); AddDistinct(enrichment, Filter(records, record => record.Difference > 0.0d), 4, AngularDiagnosticStratum.DifferenceBottom, slots, ByDifferenceAscending);
            var weightedLeaves = LeavesFor(enrichment, AngularDiagnosticStratum.WeightedTop); AddDistinct(enrichment, Filter(records, record => !weightedLeaves.Contains(LeafKey(record))), 4, AngularDiagnosticStratum.WeightedExcludedLeafTop, slots, ByWeightedDescending);
            Mark(coverage, AngularDiagnosticCohort.Coverage); Mark(enrichment, AngularDiagnosticCohort.Enrichment); var selected = new List<CandidateAngularIntervalRecord>(coverage); foreach (CandidateAngularIntervalRecord record in enrichment) if (!selected.Contains(record)) selected.Add(record); selected.Sort(Compare); foreach (CandidateAngularIntervalRecord record in selected) record.AttachSelectedEvidence();
            if (coverage.Count > MaximumPerCohort || enrichment.Count > MaximumPerCohort || selected.Count > MaximumSelected) throw new InvalidOperationException("Angular diagnostic selection cap exceeded."); return selected;
        }

        /// <summary>Adds one fixed canonical control rank without replacing an unavailable slot.</summary>
        private static void AddRank(List<CandidateAngularIntervalRecord> selected, List<CandidateAngularIntervalRecord> source, int index, AngularDiagnosticStratum stratum, List<AngularDiagnosticSelectionSlot> slots)
        {
            int added = TryAdd(selected, source, index, new HashSet<string>(), stratum) ? 1 : 0; slots.Add(new AngularDiagnosticSelectionSlot(stratum, 1, added));
        }

        /// <summary>Adds leaf-distinct candidates in one ordered stratum without cross-stratum backfill.</summary>
        private static void AddDistinct(List<CandidateAngularIntervalRecord> selected, List<CandidateAngularIntervalRecord> source, int requested, AngularDiagnosticStratum stratum, List<AngularDiagnosticSelectionSlot> slots, Comparison<CandidateAngularIntervalRecord> comparison)
        {
            source.Sort(comparison); var leaves = new HashSet<string>(); int added = 0;
            for (int slot = 0; slot < requested; slot++) if (TryAdd(selected, source, 0, leaves, stratum)) added++;
            slots.Add(new AngularDiagnosticSelectionSlot(stratum, requested, added));
        }

        /// <summary>Adds the four required smooth quantile slots without replacing duplicate leaves.</summary>
        private static void AddQuantiles(List<CandidateAngularIntervalRecord> selected, List<CandidateAngularIntervalRecord> source, AngularDiagnosticStratum stratum, List<AngularDiagnosticSelectionSlot> slots)
        {
            source.Sort(Compare); var leaves = new HashSet<string>(); int added = 0;
            for (int rank = 0; rank < 4; rank++) if (source.Count != 0 && TryAdd(selected, source, rank * (source.Count - 1) / 3, leaves, stratum)) added++;
            slots.Add(new AngularDiagnosticSelectionSlot(stratum, 4, added));
        }

        /// <summary>Adds one unique candidate from the stratum's own ordered eligible universe.</summary>
        private static bool TryAdd(List<CandidateAngularIntervalRecord> selected, List<CandidateAngularIntervalRecord> source, int start, HashSet<string> leaves, AngularDiagnosticStratum stratum)
        {
            for (int offset = 0; offset < source.Count; offset++) { CandidateAngularIntervalRecord record = source[(start + offset) % source.Count]; if ((record.Strata & stratum) != 0 || !leaves.Add(LeafKey(record))) continue; record.AddStratum(stratum); if (!selected.Contains(record)) selected.Add(record); return true; }
            return false;
        }

        /// <summary>Gets whether this atomic interval touches an independently reconstructed guard boundary.</summary>
        private static bool IsGuardAdjacent(CandidateAngularIntervalRecord record) => record.CandidateBoundaryKinds.Length == record.BoundaryBits.Length && (record.CandidateBoundaryKinds[record.AtomicIndex] == AngularDiagnosticBoundaryKind.Guard || record.CandidateBoundaryKinds[record.AtomicIndex + 1] == AngularDiagnosticBoundaryKind.Guard);
        /// <summary>Gets whether this atomic interval touches an independently reconstructed distribution boundary.</summary>
        private static bool IsDistributionAdjacent(CandidateAngularIntervalRecord record) => record.CandidateBoundaryKinds.Length == record.BoundaryBits.Length && (record.CandidateBoundaryKinds[record.AtomicIndex] == AngularDiagnosticBoundaryKind.Distribution || record.CandidateBoundaryKinds[record.AtomicIndex + 1] == AngularDiagnosticBoundaryKind.Distribution);
        /// <summary>Gets whether this radial node has no interior root boundary.</summary>
        private static bool IsSmooth(CandidateAngularIntervalRecord record) => record.BoundaryBits.Length == 2;
        /// <summary>Stores deterministic finite metric summaries for one named cohort.</summary>
        private readonly struct AngularDiagnosticMetricSummary
        {
            /// <summary>Gets the number of finite metric values.</summary>
            internal int Count { get; }
            /// <summary>Gets the deterministic sum, minimum, and maximum.</summary>
            internal double Sum { get; }
            internal double Minimum { get; }
            internal double Maximum { get; }
            /// <summary>Adds one finite value without changing an existing summary.</summary>
            internal AngularDiagnosticMetricSummary Add(double value) => Count == 0 ? new AngularDiagnosticMetricSummary(1, value, value, value) : new AngularDiagnosticMetricSummary(Count + 1, Sum + value, Math.Min(Minimum, value), Math.Max(Maximum, value));
            /// <summary>Initializes one finite metric summary.</summary>
            private AngularDiagnosticMetricSummary(int count, double sum, double minimum, double maximum) { Count = count; Sum = sum; Minimum = minimum; Maximum = maximum; }
            /// <summary>Formats explicitly named raw binary64 summary values.</summary>
            internal string Format(string name) => Count == 0 ? name + "Sum=none," + name + "Min=none," + name + "Max=none" : name + "Sum=" + Hex(BitConverter.DoubleToInt64Bits(Sum)) + "," + name + "Min=" + Hex(BitConverter.DoubleToInt64Bits(Minimum)) + "," + name + "Max=" + Hex(BitConverter.DoubleToInt64Bits(Maximum));
        }

        /// <summary>Formats independent cohort facts and finite numerical summaries.</summary>
        private static string CohortAggregate(IList<CandidateAngularIntervalRecord> selected, AngularDiagnosticCohort cohort)
        {
            int count = 0; int complete = 0; int finite = 0; int usable = 0; int unavailable = 0; int mismatched = 0; AngularDiagnosticMetricSummary h = default; AngularDiagnosticMetricSummary e = default; AngularDiagnosticMetricSummary f = default; AngularDiagnosticMetricSummary uH = default; AngularDiagnosticMetricSummary aMinus = default; AngularDiagnosticMetricSummary aPlus = default;
            foreach (CandidateAngularIntervalRecord record in selected) if ((record.Cohort & cohort) != 0) { count++; if (RecordEvidenceIsComplete(record)) complete++; AngularDiagnosticSelectedEvidence evidence = record.SelectedEvidence; if (evidence != null && evidence.UsableReference) usable++; else unavailable++; if (evidence != null && evidence.IdentityState == AngularDiagnosticIdentityState.Inconsistent) mismatched++; if (!FiniteEvidence(evidence)) continue; finite++; h = h.Add(evidence.H); e = e.Add(evidence.E); f = f.Add(evidence.F); uH = uH.Add(evidence.UH); aMinus = aMinus.Add(evidence.AMinus); aPlus = aPlus.Add(evidence.APlus); }
            return "count=" + count + ",complete=" + complete + ",finite=" + finite + ",usable=" + usable + ",unavailable=" + unavailable + ",identityMismatch=" + mismatched + "," + h.Format("h") + "," + e.Format("e") + "," + f.Format("f") + "," + uH.Format("uH") + "," + aMinus.Format("aMinus") + "," + aPlus.Format("aPlus");
        }
        /// <summary>Gets whether all retained numerical summary values are finite.</summary>
        private static bool FiniteEvidence(AngularDiagnosticSelectedEvidence evidence) => evidence != null && Finite(evidence.H) && Finite(evidence.E) && Finite(evidence.F) && Finite(evidence.UH) && Finite(evidence.AMinus) && Finite(evidence.APlus);
        /// <summary>Formats one signed raw binary64 value as its stable hexadecimal identity.</summary>
        private static string Hex(long value) => value.ToString("X16");
        /// <summary>Formats a bounded raw-bit sequence without locale-dependent numeric text.</summary>
        private static string Bits(long[] values) { var result = new string[values.Length]; for (int index = 0; index < values.Length; index++) result[index] = Hex(values[index]); return string.Join(",", result); }
        /// <summary>Formats one bounded semantic boundary sequence in captured order.</summary>
        private static string Kinds(AngularDiagnosticBoundaryKind[] values) { var result = new string[values.Length]; for (int index = 0; index < values.Length; index++) result[index] = values[index].ToString(); return string.Join(",", result); }
        /// <summary>Formats both immutable coordinate and weight sequences for one fixed rule.</summary>
        private static string Rule(AngularDiagnosticRuleBits value) => Bits(value.Coordinates) + "/" + Bits(value.Weights);
        /// <summary>Marks records in one selected cohort while preserving stratum tags.</summary>
        private static void Mark(List<CandidateAngularIntervalRecord> records, AngularDiagnosticCohort cohort) { foreach (CandidateAngularIntervalRecord record in records) record.AddCohort(cohort); }
        /// <summary>Gets deterministic leaf identities from records or one tagged stratum.</summary>
        private static HashSet<string> LeavesFor(IList<CandidateAngularIntervalRecord> records, AngularDiagnosticStratum stratum) { var result = new HashSet<string>(); foreach (CandidateAngularIntervalRecord record in records) if (stratum == AngularDiagnosticStratum.None || (record.Strata & stratum) != 0) result.Add(LeafKey(record)); return result; }
        /// <summary>Gets one stable leaf key.</summary>
        private static string LeafKey(CandidateAngularIntervalRecord record) => record.Path.Depth + ":" + record.Path.BinaryPath;
        /// <summary>Returns a deterministic filtered copy without changing canonical provenance.</summary>
        private static List<CandidateAngularIntervalRecord> Filter(IList<CandidateAngularIntervalRecord> records, Predicate<CandidateAngularIntervalRecord> predicate) { var result = new List<CandidateAngularIntervalRecord>(); foreach (CandidateAngularIntervalRecord record in records) if (predicate(record)) result.Add(record); result.Sort(Compare); return result; }
        /// <summary>Returns a sorted copy for one selection stratum.</summary>
        private static List<CandidateAngularIntervalRecord> Sorted(IList<CandidateAngularIntervalRecord> records, Comparison<CandidateAngularIntervalRecord> comparison) { var result = new List<CandidateAngularIntervalRecord>(records); result.Sort(comparison); return result; }
        /// <summary>Compares canonical spatial, outer-node, and atomic identities.</summary>
        private static int Compare(CandidateAngularIntervalRecord left, CandidateAngularIntervalRecord right) { int path = left.Path.CompareSpatial(right.Path); if (path != 0) return path; int outer = left.OuterIndex.CompareTo(right.OuterIndex); return outer != 0 ? outer : left.AtomicIndex.CompareTo(right.AtomicIndex); }
        /// <summary>Compares shortest atomic intervals with canonical tie breaking.</summary>
        private static int ByWidthAscending(CandidateAngularIntervalRecord left, CandidateAngularIntervalRecord right) { int value = (left.Right - left.Left).CompareTo(right.Right - right.Left); return value != 0 ? value : Compare(left, right); }
        /// <summary>Compares widest atomic intervals with canonical tie breaking.</summary>
        private static int ByWidthDescending(CandidateAngularIntervalRecord left, CandidateAngularIntervalRecord right) { int value = (right.Right - right.Left).CompareTo(left.Right - left.Left); return value != 0 ? value : Compare(left, right); }
        /// <summary>Compares weighted contributions with canonical tie breaking.</summary>
        private static int ByWeightedDescending(CandidateAngularIntervalRecord left, CandidateAngularIntervalRecord right) { int value = right.WeightedContribution.CompareTo(left.WeightedContribution); return value != 0 ? value : Compare(left, right); }
        /// <summary>Compares unweighted differences with canonical tie breaking.</summary>
        private static int ByDifferenceDescending(CandidateAngularIntervalRecord left, CandidateAngularIntervalRecord right) { int value = right.Difference.CompareTo(left.Difference); return value != 0 ? value : Compare(left, right); }
        /// <summary>Compares ascending nonzero differences with canonical tie breaking.</summary>
        private static int ByDifferenceAscending(CandidateAngularIntervalRecord left, CandidateAngularIntervalRecord right) { int value = left.Difference.CompareTo(right.Difference); return value != 0 ? value : Compare(left, right); }
        /// <summary>Compares two immutable evidence arrays without widening their ownership boundary.</summary>
        private static bool Same<T>(T[] left, T[] right) { if (left == null || right == null || left.Length != right.Length) return false; for (int index = 0; index < left.Length; index++) if (!EqualityComparer<T>.Default.Equals(left[index], right[index])) return false; return true; }
        /// <summary>Checks finite nonnegative replay values against a documented binary64 ULP allowance.</summary>
        private static bool WithinUlps(double left, double right, ulong allowance) { if (!Finite(left) || !Finite(right) || left < 0.0d || right < 0.0d) return false; ulong leftBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(left)); ulong rightBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(right)); return leftBits >= rightBits ? leftBits - rightBits <= allowance : rightBits - leftBits <= allowance; }
        /// <summary>Gets whether a diagnostic value is finite.</summary>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        /// <summary>Applies the candidate's adjacent-pair reduction to a copied canonical sequence.</summary>
        private static double PairwiseReduce(double[] values) { for (int count = values.Length; count > 1; count = (count + 1) / 2) { int pairs = count / 2; for (int index = 0; index < pairs; index++) values[index] = values[index * 2] + values[index * 2 + 1]; if (count % 2 != 0) values[pairs] = values[count - 1]; } return values.Length == 0 ? double.NaN : values[0]; }
    }
}
