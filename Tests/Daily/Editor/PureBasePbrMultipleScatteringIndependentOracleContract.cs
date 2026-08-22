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

// Defines test-only immutable numerical contracts for the independent directional-albedo oracle.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PureBase.Tests.Daily
{
    /// <summary>Identifies the product visibility epsilon branch without exposing a numerical stop state.</summary>
    internal enum IndependentOracleBranch
    {
        /// <summary>Uses the ordinary floating-point visibility epsilon.</summary>
        Normal,
        /// <summary>Uses the Switch visibility epsilon.</summary>
        Switch
    }

    /// <summary>Classifies the retained current outcome used only to label a fixed representative row.</summary>
    internal enum IndependentOracleLegacyOutcome
    {
        /// <summary>Records that the retained current path accepted its result.</summary>
        Accepted,
        /// <summary>Records that the retained current path exhausted its selection budget.</summary>
        BudgetExhausted,
        /// <summary>Records that the retained current path reached its depth cap.</summary>
        DepthCap
    }
    /// <summary>Identifies a fixed representative row's purpose.</summary>
    internal enum IndependentOracleRepresentativeRole
    {
        /// <summary>Provides a retained-current accepted control row.</summary>
        AcceptedControl,
        /// <summary>Provides the minimum-roughness grazing budget-exhausted row.</summary>
        BudgetMinimumGrazing,
        /// <summary>Provides the mid-roughness budget-exhausted row.</summary>
        BudgetMidRoughness,
        /// <summary>Provides the interior high-roughness budget-exhausted row.</summary>
        BudgetInterior,
        /// <summary>Provides the grazing depth-cap row.</summary>
        DepthGrazing
    }
    /// <summary>Identifies the only candidate terminal states permitted by the numerical contract.</summary>
    internal enum LightSpaceOracleStopState
    {
        /// <summary>Indicates that every finite candidate acceptance condition passed.</summary>
        Accepted,
        /// <summary>Indicates that a raw candidate input was nonfinite or outside its domain.</summary>
        NonFiniteInput,
        /// <summary>Indicates that a candidate scalar sample or accumulator was nonfinite.</summary>
        NonFiniteSample,
        /// <summary>Indicates that an interior theta root failed residual or canonical ordering validation.</summary>
        RootTopologyFailure,
        /// <summary>Indicates that unresolved global error reached the fixed outer-depth ceiling.</summary>
        DepthCap,
        /// <summary>Indicates that a candidate panel reservation would exceed the fixed panel ceiling.</summary>
        PanelCap,
        /// <summary>Indicates that a candidate scalar-call reservation would exceed the fixed evaluation ceiling.</summary>
        EvaluationCap,
        /// <summary>Indicates a finite deterministic global-error invariant failure without a prior hard stop.</summary>
        GlobalError
    }
    /// <summary>Identifies the only exclusive oracle decisions.</summary>
    internal enum IndependentOracleDecision
    {
        /// <summary>Accepts the candidate for later full characterization.</summary>
        A,
        /// <summary>Rejects the candidate.</summary>
        B,
        /// <summary>Classifies the witness evidence as insufficient.</summary>
        C,
        /// <summary>Classifies finite independent evidence as numerically inconclusive.</summary>
        D,
        /// <summary>Classifies a fully characterized candidate as eligible for a later fit decision.</summary>
        E
    }
    /// <summary>Classifies whether candidate uncertainty evidence was computed and passed its fixed bound.</summary>
    internal enum IndependentOracleCandidateUncertaintyEvidence
    {
        /// <summary>Indicates that candidate uncertainty was omitted or not computed.</summary>
        Unavailable,
        /// <summary>Indicates that computed candidate uncertainty did not meet its fixed bound.</summary>
        Rejected,
        /// <summary>Indicates that computed candidate uncertainty met its fixed bound.</summary>
        Accepted
    }
    /// <summary>Identifies an independently derived candidate theta-boundary kind.</summary>
    internal enum IndependentOracleRootKind
    {
        /// <summary>Identifies the safe-normalization guard transition.</summary>
        Guard,
        /// <summary>Identifies the GGX distribution-denominator transition.</summary>
        Distribution
    }
    /// <summary>Stores one verified interior candidate theta boundary.</summary>
    internal readonly struct IndependentOracleThetaRoot
    {
        /// <summary>Initializes an immutable candidate theta boundary.</summary>
        internal IndependentOracleThetaRoot(IndependentOracleRootKind kind, double theta, double cosine, bool present, bool topologyValid = true) { Kind = kind; Theta = theta; Cosine = cosine; Present = present; TopologyValid = topologyValid; }
        /// <summary>Gets the semantic source of the boundary.</summary>
        internal IndependentOracleRootKind Kind { get; }
        /// <summary>Gets the interior theta coordinate.</summary>
        internal double Theta { get; }
        /// <summary>Gets the independently derived cosine retained for stable q-target reconstruction.</summary>
        internal double Cosine { get; }
        /// <summary>Gets whether this boundary survives finite, interior, and residual checks.</summary>
        internal bool Present { get; }
        /// <summary>Gets whether this root is a valid absence or passed residual validation rather than failing topology validation.</summary>
        internal bool TopologyValid { get; }
    }
    /// <summary>Stores the sorted and semantically deduplicated interior theta boundaries.</summary>
    internal readonly struct IndependentOracleThetaPartition
    {
        /// <summary>Initializes one immutable theta partition.</summary>
        internal IndependentOracleThetaPartition(IndependentOracleThetaRoot first, IndependentOracleThetaRoot second, int count)
        {
            First = first; Second = second; Count = count;
            StopState = IsCanonical(first, second, count) ? LightSpaceOracleStopState.Accepted : LightSpaceOracleStopState.RootTopologyFailure;
        }
        /// <summary>Gets the first retained boundary in theta order.</summary>
        internal IndependentOracleThetaRoot First { get; }
        /// <summary>Gets the second retained boundary in theta order.</summary>
        internal IndependentOracleThetaRoot Second { get; }
        /// <summary>Gets the count of retained interior boundaries.</summary>
        internal int Count { get; }
        /// <summary>Gets whether root residual and ordering validation accepted this partition.</summary>
        internal LightSpaceOracleStopState StopState { get; }

        /// <summary>Gets whether the supplied roots form an ordered, semantically valid partition or a valid empty absence.</summary>
        private static bool IsCanonical(IndependentOracleThetaRoot first, IndependentOracleThetaRoot second, int count)
        {
            if (count == 0) return !first.Present && !second.Present && first.TopologyValid && second.TopologyValid;
            if (count == 1) return first.Present && first.TopologyValid && !second.Present && second.TopologyValid;
            return count == 2 && first.Present && second.Present && first.TopologyValid && second.TopologyValid && first.Kind != second.Kind && first.Theta < second.Theta;
        }
    }
    /// <summary>Stores the raw binary64 input identity shared across independent oracle boundaries.</summary>
    internal readonly struct IndependentOracleInput
    {
        /// <summary>Initializes one immutable raw input tuple.</summary>
        internal IndependentOracleInput(double p, double ndotv, IndependentOracleBranch branch) { P = p; NdotV = ndotv; Branch = branch; }
        /// <summary>Gets perceptual roughness.</summary>
        internal double P { get; }
        /// <summary>Gets the view cosine.</summary>
        internal double NdotV { get; }
        /// <summary>Gets the visibility epsilon branch.</summary>
        internal IndependentOracleBranch Branch { get; }
    }
    /// <summary>Stores one exact-bit representative row and its retained legacy observation.</summary>
    internal readonly struct IndependentOracleRepresentativeRow
    {
        /// <summary>Initializes one immutable representative identity.</summary>
        internal IndependentOracleRepresentativeRow(ulong pBits, ulong ndotvBits, IndependentOracleBranch branch, IndependentOracleRepresentativeRole role, IndependentOracleLegacyOutcome legacyOutcome)
        {
            PBits = pBits; NdotVBits = ndotvBits; Input = new IndependentOracleInput(FromBits(pBits), FromBits(ndotvBits), branch); Role = role; LegacyOutcome = legacyOutcome;
        }
        /// <summary>Gets the exact perceptual-roughness bit pattern.</summary>
        internal ulong PBits { get; }
        /// <summary>Gets the exact view-cosine bit pattern.</summary>
        internal ulong NdotVBits { get; }
        /// <summary>Gets the raw input tuple.</summary>
        internal IndependentOracleInput Input { get; }
        /// <summary>Gets the row's fixed representative role.</summary>
        internal IndependentOracleRepresentativeRole Role { get; }
        /// <summary>Gets the retained current outcome label.</summary>
        internal IndependentOracleLegacyOutcome LegacyOutcome { get; }
        /// <summary>Converts one frozen unsigned binary64 pattern without decimal normalization.</summary>
        private static double FromBits(ulong value) => BitConverter.Int64BitsToDouble(unchecked((long)value));
    }

    /// <summary>Stores one fixed witness tensor resolution and whether it uses the shifted periodic phase.</summary>
    internal readonly struct IndependentOracleWitnessResolution
    {
        /// <summary>Initializes one immutable tensor resolution.</summary>
        internal IndependentOracleWitnessResolution(int tOrder, int phiOrder, bool shifted) { TOrder = tOrder; PhiOrder = phiOrder; Shifted = shifted; }
        /// <summary>Gets the Gauss--Legendre t-axis order.</summary>
        internal int TOrder { get; }
        /// <summary>Gets the periodic midpoint phi-axis order.</summary>
        internal int PhiOrder { get; }
        /// <summary>Gets whether the phi rule uses the fixed quarter-cell phase.</summary>
        internal bool Shifted { get; }
    }

    /// <summary>Freezes formulas, limits, ordering, and comparison semantics without implementing either kernel.</summary>
    internal static class IndependentOracleContract
    {
        /// <summary>Gets the safe-normalization and distribution denominator floor.</summary>
        internal const double GuardFloor = 1.0e-6d;
        /// <summary>Gets the normal visibility epsilon.</summary>
        internal const double NormalEpsilon = 1.0e-5d;
        /// <summary>Gets the Switch visibility epsilon.</summary>
        internal const double SwitchEpsilon = 1.0d / 16384.0d;
        /// <summary>Gets the maximum permitted candidate outer depth.</summary>
        internal const int MaxDepth = 22;
        /// <summary>Gets the maximum historical candidate panel attempts.</summary>
        internal const int MaxPanels = 262144;
        /// <summary>Gets the maximum historical candidate scalar evaluations.</summary>
        internal const int MaxEvaluations = 4000000;
        /// <summary>Gets the absolute part of the total comparison budget.</summary>
        internal const double AbsoluteBudget = 2.5e-6d;
        /// <summary>Gets the relative part of the total comparison budget.</summary>
        internal const double RelativeBudget = 2.5e-5d;
        /// <summary>Gets the candidate base requested target.</summary>
        internal const double CandidateBaseTarget = AbsoluteBudget / 8.0d;
        /// <summary>Gets the candidate stricter requested target.</summary>
        internal const double CandidateStrictTarget = AbsoluteBudget / 32.0d;
        /// <summary>Gets the required witness work after all shared tensor values are reused.</summary>
        internal const int WitnessScalarEvaluations = 3912482;
        /// <summary>Gets the outer Clenshaw--Curtis point counts in coarse/fine order.</summary>
        internal static IReadOnlyList<int> CandidateClenshawCurtisOrders { get; } = Array.AsReadOnly(new[] { 9, 17 });
        /// <summary>Gets the endpoint-free inner Fejer-II point counts in coarse/fine order.</summary>
        internal static IReadOnlyList<int> CandidateFejerOrders { get; } = Array.AsReadOnly(new[] { 17, 33 });
        /// <summary>Gets the candidate theta-root residual allowance in binary64 ULP units.</summary>
        internal const int RootResidualUlps = 128;
        /// <summary>Gets the candidate semantic theta-root tie allowance in binary64 ULP units.</summary>
        internal const int RootTieUlps = 32;

        /// <summary>Gets the ten fixed rows in table order, with normal before Switch for every coordinate.</summary>
        internal static IReadOnlyList<IndependentOracleRepresentativeRow> RepresentativeRows { get; } = Array.AsReadOnly(new[]
        {
            new IndependentOracleRepresentativeRow(0x3FF0000000000000UL, 0x3FF0000000000000UL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.AcceptedControl, IndependentOracleLegacyOutcome.Accepted),
            new IndependentOracleRepresentativeRow(0x3FF0000000000000UL, 0x3FF0000000000000UL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.AcceptedControl, IndependentOracleLegacyOutcome.Accepted),
            new IndependentOracleRepresentativeRow(0x3FB6C8B439581062UL, 0x0000000000000000UL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.BudgetMinimumGrazing, IndependentOracleLegacyOutcome.BudgetExhausted),
            new IndependentOracleRepresentativeRow(0x3FB6C8B439581062UL, 0x0000000000000000UL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.BudgetMinimumGrazing, IndependentOracleLegacyOutcome.BudgetExhausted),
            new IndependentOracleRepresentativeRow(0x3FE0000000000000UL, 0x3FF0000000000000UL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.BudgetMidRoughness, IndependentOracleLegacyOutcome.BudgetExhausted),
            new IndependentOracleRepresentativeRow(0x3FE0000000000000UL, 0x3FF0000000000000UL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.BudgetMidRoughness, IndependentOracleLegacyOutcome.BudgetExhausted),
            new IndependentOracleRepresentativeRow(0x3FF0000000000000UL, 0x3FEF746EA3A45F8AUL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.BudgetInterior, IndependentOracleLegacyOutcome.BudgetExhausted),
            new IndependentOracleRepresentativeRow(0x3FF0000000000000UL, 0x3FEF746EA3A45F8AUL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.BudgetInterior, IndependentOracleLegacyOutcome.BudgetExhausted),
            new IndependentOracleRepresentativeRow(0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, IndependentOracleBranch.Normal, IndependentOracleRepresentativeRole.DepthGrazing, IndependentOracleLegacyOutcome.DepthCap),
            new IndependentOracleRepresentativeRow(0x3FD0000000000000UL, 0x3F50624DD2F1A9FCUL, IndependentOracleBranch.Switch, IndependentOracleRepresentativeRole.DepthGrazing, IndependentOracleLegacyOutcome.DepthCap)
        });

        /// <summary>Gets the eight unique fixed witness tensor evaluations in execution order.</summary>
        internal static IReadOnlyList<IndependentOracleWitnessResolution> WitnessResolutions { get; } = Array.AsReadOnly(new[]
        {
            new IndependentOracleWitnessResolution(127, 509, false), new IndependentOracleWitnessResolution(251, 1021, false), new IndependentOracleWitnessResolution(503, 2039, false), new IndependentOracleWitnessResolution(127, 2039, false),
            new IndependentOracleWitnessResolution(251, 2039, false), new IndependentOracleWitnessResolution(503, 509, false), new IndependentOracleWitnessResolution(503, 1021, false), new IndependentOracleWitnessResolution(503, 2039, true)
        });

        /// <summary>Gets the four fixed p=1 analytical view cosines.</summary>
        internal static IReadOnlyList<double> AnalyticalViewCosines { get; } = Array.AsReadOnly(new[] { 0.0d, 0.5d, BitConverter.Int64BitsToDouble(unchecked((long)0x3FEF746EA3A45F8AUL)), 1.0d });

        /// <summary>Evaluates the independently specified candidate r/theta form, including its half-azimuth symmetry factor.</summary>
        internal static double EvaluateCandidateTransform(IndependentOracleInput input, double r, double theta)
        {
            double p = input.P; double v = input.NdotV; double u = Math.Pow(Math.Sin(Math.PI * r * 0.5d), 2.0d); double a = p * p; double m = a * a;
            double q = 2.0d * (1.0d + u * v + Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v) * Math.Cos(theta)); double h2 = (u + v) * (u + v) / Math.Max(q, GuardFloor);
            double d = h2 * (m - 1.0d) + 1.0d; double distribution = m / Math.Max(Math.PI * d * d, GuardFloor); double epsilon = input.Branch == IndependentOracleBranch.Normal ? NormalEpsilon : SwitchEpsilon;
            double visibility = 0.5d / (u * (v * (1.0d - a) + a) + v * (u * (1.0d - a) + a) + epsilon); return 2.0d * distribution * visibility * u * Math.PI * Math.Sin(Math.PI * r) * 0.5d;
        }

        /// <summary>Evaluates the independently specified witness t/phi form over the full periodic domain.</summary>
        internal static double EvaluateWitnessTransform(IndependentOracleInput input, double t, double phi)
        {
            double u = t * t; double v = input.NdotV; double alpha = input.P * input.P; double alphaSquared = alpha * alpha;
            double halfDotDenominator = 2.0d * (1.0d + u * v + Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v) * Math.Cos(phi)); double numerator = (u + v) * (u + v);
            double squaredHalfCosine = numerator / Math.Max(halfDotDenominator, GuardFloor); double ggxBase = squaredHalfCosine * (alphaSquared - 1.0d) + 1.0d;
            double d = alphaSquared / Math.Max(Math.PI * ggxBase * ggxBase, GuardFloor); double epsilon = input.Branch == IndependentOracleBranch.Normal ? NormalEpsilon : SwitchEpsilon;
            double smith = 0.5d / (u * (v * (1.0d - alpha) + alpha) + v * (u * (1.0d - alpha) + alpha) + epsilon); return d * smith * u * (2.0d * t);
        }

        /// <summary>Gets whether one candidate sample lies in its closed radial and half-azimuth domain.</summary>
        internal static bool CandidateDomainPass(double r, double theta) => Unit(r) && Finite(theta) && theta >= 0.0d && theta <= Math.PI;

        /// <summary>Gets whether one witness sample lies in its closed squared-light and periodic-azimuth domain.</summary>
        internal static bool WitnessDomainPass(double t, double phi) => Unit(t) && Finite(phi) && phi >= 0.0d && phi <= 2.0d * Math.PI;

        /// <summary>Gets the candidate r-to-u Jacobian before the half-azimuth symmetry factor.</summary>
        internal static double CandidateJacobian(double r) => Math.PI * Math.Sin(Math.PI * r) * 0.5d;

        /// <summary>Gets the witness t-to-u Jacobian.</summary>
        internal static double WitnessJacobian(double t) => 2.0d * t;

        /// <summary>Derives and semantically orders the candidate's interior theta roots for one radial node.</summary>
        internal static IndependentOracleThetaPartition DeriveCandidateThetaPartition(IndependentOracleInput input, double r)
        {
            if (!Unit(input.P) || !Unit(input.NdotV) || !Unit(r)) return EmptyPartition();
            double u = Math.Pow(Math.Sin(Math.PI * r * 0.5d), 2.0d); double v = input.NdotV; double z = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v);
            IndependentOracleThetaRoot guard = DeriveGuardRoot(u, v, z); IndependentOracleThetaRoot distribution = DeriveDistributionRoot(input.P, u, v, z);
            return OrderRoots(guard, distribution);
        }

        /// <summary>Applies the frozen semantic tie rule to already validated root candidates.</summary>
        internal static IndependentOracleThetaPartition CanonicalizeThetaRoots(IndependentOracleThetaRoot guard, IndependentOracleThetaRoot distribution) => OrderRoots(guard, distribution);

        /// <summary>Builds the endpoint-inclusive atomic theta boundaries for one candidate node.</summary>
        internal static IReadOnlyList<double> ThetaBoundaries(IndependentOracleThetaPartition partition)
        {
            if (partition.StopState != LightSpaceOracleStopState.Accepted) return Array.AsReadOnly(Array.Empty<double>());
            if (partition.Count == 0) return Array.AsReadOnly(new[] { 0.0d, Math.PI });
            if (partition.Count == 1) return Array.AsReadOnly(new[] { 0.0d, partition.First.Theta, Math.PI });
            return Array.AsReadOnly(new[] { 0.0d, partition.First.Theta, partition.Second.Theta, Math.PI });
        }

        /// <summary>Gets the candidate leaf error as the unnormalized radial and angular indicator sum.</summary>
        internal static double CandidateLeafError(double coarse, double fine, double angularError)
        {
            if (!Finite(coarse) || !Finite(fine) || !NonNegativeFinite(angularError)) return double.NaN;
            double value = Math.Abs(fine - coarse) + angularError; return NonNegativeFinite(value) ? value : double.NaN;
        }

        /// <summary>Orders the candidate heap by error descending and depth/path ascending.</summary>
        internal static int CompareLeafKeys(IndependentOracleLeafKey left, IndependentOracleLeafKey right)
        {
            int error = right.Error.CompareTo(left.Error); if (error != 0) return error;
            int depth = left.Path.Depth.CompareTo(right.Path.Depth); return depth != 0 ? depth : left.Path.CompareSpatial(right.Path);
        }

        /// <summary>Reduces a binary-path ordered sequence with the frozen adjacent-pair reduction tree.</summary>
        internal static double PairwiseReduce(IReadOnlyList<double> values)
        {
            if (values.Count == 0) return 0.0d;
            var current = new double[values.Count]; for (int index = 0; index < values.Count; index++) current[index] = values[index];
            for (int count = current.Length; count > 1; count = (count + 1) / 2)
            {
                int pairs = count / 2; for (int index = 0; index < pairs; index++) current[index] = current[index * 2] + current[index * 2 + 1];
                if (count % 2 != 0) current[pairs] = current[count - 1];
            }
            return current[0];
        }

        /// <summary>Computes one positive-delta geometric-tail uncertainty without using the exact-zero exception.</summary>
        internal static bool TryPositiveGeometricTail(double delta0, double delta1, out double ratio, out double tail)
        {
            ratio = double.NaN; tail = double.NaN; if (!Finite(delta0) || !Finite(delta1) || delta0 < 0.0d || delta1 < 0.0d) return false;
            if (delta1 <= 0.0d) return false; ratio = delta0 / delta1; if (!Finite(ratio) || ratio < 4.0d) return false;
            tail = delta1 / (ratio - 1.0d); return NonNegativeFinite(tail);
        }

        /// <summary>Computes one geometric tail, admitting exact zeros only with independent finite sensitivity evidence.</summary>
        internal static bool TryGeometricTail(double delta0, double delta1, double probeDelta, double otherAxisDelta, double phaseDelta, out double ratio, out double tail)
        {
            if (delta0 == 0.0d && delta1 == 0.0d)
            {
                ratio = double.NaN; tail = double.NaN;
                if (!ExactZeroTailPass(delta0, delta1, probeDelta, otherAxisDelta, phaseDelta)) return false;
                ratio = double.PositiveInfinity; tail = 0.0d; return true;
            }
            return TryPositiveGeometricTail(delta0, delta1, out ratio, out tail);
        }

        /// <summary>Composes the three geometric-tail terms and fixed shifted-phase term into witness uncertainty.</summary>
        internal static double WitnessUncertainty(double jointTail, double uTail, double phiTail, double phaseDelta)
        {
            if (!NonNegativeFinite(jointTail) || !NonNegativeFinite(uTail) || !NonNegativeFinite(phiTail) || !NonNegativeFinite(phaseDelta)) return double.NaN;
            double uncertainty = jointTail + uTail + phiTail + phaseDelta; return NonNegativeFinite(uncertainty) ? uncertainty : double.NaN;
        }

        /// <summary>Checks the fixed shifted-phase and total witness-uncertainty limits at one comparison scale.</summary>
        internal static bool WitnessUncertaintyPass(double uncertainty, double phaseDelta, double scale) => NonNegativeFinite(uncertainty) && NonNegativeFinite(phaseDelta) && NonNegativeFinite(scale) && phaseDelta <= Budget(scale) / 8.0d && uncertainty <= Budget(scale) / 4.0d;

        /// <summary>Evaluates the independent p=1 analytical directional-albedo value.</summary>
        internal static double EvaluateP1Analytical(double v, IndependentOracleBranch branch)
        {
            double epsilon = Epsilon(branch); double baseValue = v + epsilon; return 1.0d - baseValue * Math.Log((1.0d + baseValue) / baseValue);
        }

        /// <summary>Gets the frozen total comparison budget at a nonnegative magnitude.</summary>
        internal static double Budget(double magnitude) => AbsoluteBudget + RelativeBudget * magnitude;

        /// <summary>Gets the candidate uncertainty from its strict error and base/strict stability delta.</summary>
        internal static double CandidateUncertainty(double strictError, double baseValue, double strict)
        {
            if (!NonNegativeFinite(strictError) || !Finite(baseValue) || !Finite(strict)) return double.NaN;
            double uncertainty = Math.Max(strictError, Math.Abs(baseValue - strict)); return NonNegativeFinite(uncertainty) ? uncertainty : double.NaN;
        }

        /// <summary>Requires candidate uncertainty to be finite, nonnegative, and no greater than the base target.</summary>
        internal static bool CandidateUncertaintyPass(double uncertainty) => NonNegativeFinite(uncertainty) && uncertainty <= CandidateBaseTarget;

        /// <summary>Gets the 32-ULP symmetric comparison allowance or NaN for a nonfinite magnitude.</summary>
        internal static double ComparisonAllowance(double magnitude) => 32.0d * Ulp(magnitude);

        /// <summary>Applies the one composed representative comparison inequality.</summary>
        internal static bool ComposedComparisonPass(double candidate, double witness, double candidateUncertainty, double witnessUncertainty)
        {
            double scale = Math.Max(Math.Abs(candidate), Math.Abs(witness)); return Finite(candidate) && Finite(witness) && NonNegativeFinite(candidateUncertainty) && NonNegativeFinite(witnessUncertainty) && Math.Abs(candidate - witness) + candidateUncertainty + witnessUncertainty + ComparisonAllowance(scale) <= Budget(scale);
        }

        /// <summary>Applies the candidate's p=1 composed analytical inequality.</summary>
        internal static bool CandidateAnalyticalPass(double analytical, double candidate, double uncertainty)
        {
            double scale = Math.Max(Math.Abs(analytical), Math.Abs(candidate)); return Finite(analytical) && Finite(candidate) && NonNegativeFinite(uncertainty) && Math.Abs(candidate - analytical) + uncertainty + ComparisonAllowance(scale) <= AbsoluteBudget;
        }

        /// <summary>Applies the witness's p=1 composed analytical inequality.</summary>
        internal static bool WitnessAnalyticalPass(double analytical, double witness, double uncertainty)
        {
            double scale = Math.Max(Math.Abs(analytical), Math.Abs(witness)); return Finite(analytical) && Finite(witness) && NonNegativeFinite(uncertainty) && Math.Abs(witness - analytical) + uncertainty + ComparisonAllowance(scale) <= Budget(scale);
        }

        /// <summary>Requires exact-zero tails to retain finite positive sensitivity and finite independent convergence inputs.</summary>
        internal static bool ExactZeroTailPass(double delta0, double delta1, double probeDelta, double otherAxisDelta, double phaseDelta) => delta0 == 0.0d && delta1 == 0.0d && Finite(probeDelta) && probeDelta > 0.0d && Finite(otherAxisDelta) && Finite(phaseDelta);

        /// <summary>Chooses one exclusive decision with candidate failure taking precedence over witness insufficiency.</summary>
        internal static IndependentOracleDecision Decide(IndependentOracleDecisionEvidence evidence)
        {
            if (!evidence.CandidateIndependent || !evidence.CandidateFinite || !evidence.CandidateAccepted || evidence.CandidateUncertainty != IndependentOracleCandidateUncertaintyEvidence.Accepted || !evidence.CandidateAnalytical) return IndependentOracleDecision.B;
            if (!evidence.WitnessIndependent || !evidence.WitnessFinite || !evidence.WitnessAnalytical || !evidence.WitnessConverged) return IndependentOracleDecision.C;
            if (!evidence.ComposedAgreement) return IndependentOracleDecision.D;
            return evidence.CharacterizationEligible ? IndependentOracleDecision.E : IndependentOracleDecision.A;
        }

        /// <summary>Derives the safe-normalization transition from its reconstructed q target.</summary>
        private static IndependentOracleThetaRoot DeriveGuardRoot(double u, double v, double z)
        {
            if (!Finite(z) || z <= 0.0d) return MissingRoot();
            double cosine = (GuardFloor * 0.5d - 1.0d - u * v) / z; return RootFromCosine(IndependentOracleRootKind.Guard, cosine, u, v, z, GuardFloor);
        }

        /// <summary>Derives the unclamped GGX-denominator transition from its reconstructed q target.</summary>
        private static IndependentOracleThetaRoot DeriveDistributionRoot(double p, double u, double v, double z)
        {
            if (!Finite(z) || z <= 0.0d || p >= 1.0d) return MissingRoot();
            double m = p * p * p * p; double h2 = (1.0d - Math.Sqrt(GuardFloor / Math.PI)) / (1.0d - m); double targetQ = (u + v) * (u + v) / h2;
            if (!Finite(h2) || !Finite(targetQ)) return InvalidRoot(IndependentOracleRootKind.Distribution);
            if (targetQ < GuardFloor) return MissingRoot();
            double cosine = (targetQ * 0.5d - 1.0d - u * v) / z; return RootFromCosine(IndependentOracleRootKind.Distribution, cosine, u, v, z, targetQ);
        }

        /// <summary>Retains only finite interior roots whose reconstructed q target is within the frozen ULP bound.</summary>
        private static IndependentOracleThetaRoot RootFromCosine(IndependentOracleRootKind kind, double cosine, double u, double v, double z, double targetQ)
        {
            if (!Finite(cosine) || cosine <= -1.0d || cosine >= 1.0d) return MissingRoot();
            double theta = Math.Acos(cosine); double recoveredQ = 2.0d * ((1.0d - z) + u * v + z * (1.0d + cosine));
            return theta > 0.0d && theta < Math.PI && WithinUlps(recoveredQ, targetQ, RootResidualUlps) ? new IndependentOracleThetaRoot(kind, theta, cosine, true) : InvalidRoot(kind);
        }

        /// <summary>Sorts roots by theta and resolves semantic ties with the guard boundary first.</summary>
        private static IndependentOracleThetaPartition OrderRoots(IndependentOracleThetaRoot guard, IndependentOracleThetaRoot distribution)
        {
            if (!guard.TopologyValid || !distribution.TopologyValid) return FailurePartition();
            if (!guard.Present && !distribution.Present) return EmptyPartition(); if (!guard.Present) return new IndependentOracleThetaPartition(distribution, MissingRoot(), 1);
            if (!distribution.Present || WithinUlps(guard.Theta, distribution.Theta, RootTieUlps)) return new IndependentOracleThetaPartition(guard, MissingRoot(), 1);
            return guard.Theta < distribution.Theta ? new IndependentOracleThetaPartition(guard, distribution, 2) : new IndependentOracleThetaPartition(distribution, guard, 2);
        }

        /// <summary>Gets the empty root partition used for invalid inputs and endpoint contacts.</summary>
        private static IndependentOracleThetaPartition EmptyPartition() => new IndependentOracleThetaPartition(MissingRoot(), MissingRoot(), 0);

        /// <summary>Gets the non-numerical root-topology failure partition without retaining a usable boundary.</summary>
        private static IndependentOracleThetaPartition FailurePartition() => new IndependentOracleThetaPartition(InvalidRoot(IndependentOracleRootKind.Guard), InvalidRoot(IndependentOracleRootKind.Distribution), 0);

        /// <summary>Gets the non-boundary sentinel without assigning it a numerical theta.</summary>
        private static IndependentOracleThetaRoot MissingRoot() => new IndependentOracleThetaRoot(IndependentOracleRootKind.Guard, double.NaN, double.NaN, false);

        /// <summary>Gets an invalid interior candidate sentinel that must surface as a root-topology stop.</summary>
        private static IndependentOracleThetaRoot InvalidRoot(IndependentOracleRootKind kind) => new IndependentOracleThetaRoot(kind, double.NaN, double.NaN, false, false);

        /// <summary>Gets whether a value is a finite closed-unit-interval coordinate.</summary>
        private static bool Unit(double value) => Finite(value) && value >= 0.0d && value <= 1.0d;

        /// <summary>Compares two finite nonnegative binary64 values by their ordered ULP distance.</summary>
        private static bool WithinUlps(double left, double right, int limit)
        {
            if (!Finite(left) || !Finite(right) || left < 0.0d || right < 0.0d) return false;
            ulong leftBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(left)); ulong rightBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(right));
            return leftBits >= rightBits ? leftBits - rightBits <= (ulong)limit : rightBits - leftBits <= (ulong)limit;
        }

        /// <summary>Gets the branch-specific visibility epsilon.</summary>
        internal static double Epsilon(IndependentOracleBranch branch) => branch == IndependentOracleBranch.Normal ? NormalEpsilon : SwitchEpsilon;

        /// <summary>Gets whether a binary64 component is finite.</summary>
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Gets whether a finite numerical error, tail, delta, or uncertainty is nonnegative.</summary>
        private static bool NonNegativeFinite(double value) => Finite(value) && value >= 0.0d;

        /// <summary>Gets the positive binary64 ULP at a finite nonnegative magnitude.</summary>
        internal static double Ulp(double value)
        {
            if (!Finite(value) || value < 0.0d) return double.NaN;
            if (value == 0.0d) return BitConverter.Int64BitsToDouble(1L);
            long bits = BitConverter.DoubleToInt64Bits(value); return BitConverter.Int64BitsToDouble(bits + 1L) - value;
        }

    }
}
