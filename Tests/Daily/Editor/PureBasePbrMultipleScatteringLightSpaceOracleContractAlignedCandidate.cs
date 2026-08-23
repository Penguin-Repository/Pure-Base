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

// Defines candidate-local scalar evaluation and validated atomic theta partition derivation.

using System;

namespace PureBase.Tests.Daily
{
    /// <summary>Stores one independently validated candidate theta partition.</summary>
    internal readonly struct LightSpaceOracleCandidateThetaPartition
    {
        /// <summary>Initializes the endpoint-inclusive atomic boundaries for one radial node.</summary>
        internal LightSpaceOracleCandidateThetaPartition(double[] boundaries) { Boundaries = boundaries; }

        /// <summary>Gets the ordered endpoint-inclusive theta boundaries.</summary>
        internal double[] Boundaries { get; }
    }

    /// <summary>Provides the isolated numerical authority for the contract-aligned light-space candidate.</summary>
    internal static class LightSpaceOracleContractAlignedCandidate
    {
        /// <summary>Gets the fixed maximum outer binary refinement depth.</summary>
        internal const int MaximumDepth = 22;

        /// <summary>Gets the fixed maximum number of historical outer panel reservations.</summary>
        internal const int MaximumPanels = 262144;

        /// <summary>Gets the fixed maximum number of historical scalar reservations.</summary>
        internal const int MaximumEvaluations = 4000000;

        /// <summary>Runs the candidate directly without granting public-entry authority.</summary>
        internal static LightSpaceOracleResult Integrate(IndependentOracleInput input, double requestedTarget, LightSpaceOracleCandidateDiagnosticSink diagnostics = null)
        {
            return new LightSpaceOracleCandidateScheduler(input, requestedTarget, diagnostics).Run();
        }

        /// <summary>Evaluates the expanded light-space scalar with no contract or prototype helper dependency.</summary>
        internal static double EvaluateScalar(IndependentOracleInput input, double r, double theta)
        {
            double p = input.P; double v = input.NdotV; double sine = Math.Sin(Math.PI * r * 0.5d); double u = sine * sine;
            double alpha = p * p; double alphaSquared = alpha * alpha; double root = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v);
            double q = 2.0d * (1.0d + u * v + root * Math.Cos(theta)); double h2 = (u + v) * (u + v) / Math.Max(q, 1.0e-6d);
            double baseValue = h2 * (alphaSquared - 1.0d) + 1.0d; double distribution = alphaSquared / Math.Max(Math.PI * baseValue * baseValue, 1.0e-6d);
            double epsilon = input.Branch == IndependentOracleBranch.Normal ? 1.0e-5d : 1.0d / 16384.0d;
            double visibility = 0.5d / (u * (v * (1.0d - alpha) + alpha) + v * (u * (1.0d - alpha) + alpha) + epsilon);
            return 2.0d * distribution * visibility * u * Math.PI * Math.Sin(Math.PI * r) * 0.5d;
        }

        /// <summary>Derives roots from local q targets and admits only finite interval-contained interior boundaries.</summary>
        internal static bool TryDeriveThetaPartition(IndependentOracleInput input, double r, out LightSpaceOracleCandidateThetaPartition partition, LightSpaceOracleCandidateDiagnosticSink diagnostics = null)
        {
            partition = default;
            if (!Unit(input.P) || !Unit(input.NdotV) || !Unit(r)) return false;
            double sine = Math.Sin(Math.PI * r * 0.5d); double u = sine * sine; double v = input.NdotV;
            double z = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v);
            if (!Finite(z)) return false;
            if (!TryRoot(LightSpaceOracleCandidateRootKind.Guard, r, 1.0e-6d, u, v, z, diagnostics, out double guard)) return false;
            if (!TryDistributionRoot(input.P, r, u, v, z, diagnostics, out double distribution)) return false;
            partition = BuildPartition(guard, distribution);
            return partition.Boundaries != null;
        }

        /// <summary>Gets whether the raw candidate input and requested target are finite closed-domain values.</summary>
        internal static bool ValidInput(IndependentOracleInput input, double requestedTarget)
        {
            return Unit(input.P) && Unit(input.NdotV) && Finite(requestedTarget) && requestedTarget >= 0.0d;
        }

        /// <summary>Combines the independent radial and weighted angular leaf indicators.</summary>
        internal static double ComposeLeafError(double coarse, double fine, double angularError)
        {
            if (!Finite(coarse) || !Finite(fine) || !Finite(angularError) || angularError < 0.0d) return double.NaN;
            double error = Math.Abs(fine - coarse) + angularError;
            return Finite(error) && error >= 0.0d ? error : double.NaN;
        }

        /// <summary>Derives a guard or distribution root from a finite q target.</summary>
        internal static bool TryRoot(LightSpaceOracleCandidateRootKind kind, double r, double target, double u, double v, double z, LightSpaceOracleCandidateDiagnosticSink diagnostics, out double theta)
        {
            theta = double.NaN;
            if (z <= 0.0d) return true;
            if (r == 0.0d) return true;
            double cosine = (target * 0.5d - 1.0d - u * v) / z;
            if (!Finite(cosine)) return RecordRootTopologyFailure(diagnostics, kind, r, target, cosine, double.NaN, LightSpaceOracleCandidateRootInteriorPresence.NotReached, LightSpaceOracleCandidateRootTopologyStage.CosineDerived);
            if (cosine <= -1.0d || cosine >= 1.0d) return true;
            theta = Math.Acos(cosine);
            if (!Finite(theta) || theta <= 0.0d || theta >= Math.PI) return RecordRootTopologyFailure(diagnostics, kind, r, target, cosine, theta, Finite(theta) ? LightSpaceOracleCandidateRootInteriorPresence.Absent : LightSpaceOracleCandidateRootInteriorPresence.NotReached, LightSpaceOracleCandidateRootTopologyStage.ThetaDerived);
            if (!TryAffineCosineInterval(target, u, v, z, out double cosineLower, out double cosineUpper)) return RecordRootTopologyFailure(diagnostics, kind, r, target, cosine, theta, LightSpaceOracleCandidateRootInteriorPresence.Present, LightSpaceOracleCandidateRootTopologyStage.ThetaDerived);
            if (!TryAffineTargetInterval(u, v, z, cosineLower, cosineUpper, out double targetLower, out double targetUpper)) return RecordRootTopologyFailure(diagnostics, kind, r, target, cosine, theta, LightSpaceOracleCandidateRootInteriorPresence.Present, LightSpaceOracleCandidateRootTopologyStage.ThetaDerived);
            if (target < targetLower || target > targetUpper) return RecordRootTopologyFailure(diagnostics, kind, r, target, cosine, theta, LightSpaceOracleCandidateRootInteriorPresence.Present, LightSpaceOracleCandidateRootTopologyStage.ThetaDerived);
            double recovered = 2.0d * (1.0d + u * v + z * Math.Cos(theta));
            bool residualValid = WithinUlps(recovered, target, 128);
            if (!residualValid) diagnostics?.RecordFirstLegacyRoundTripObservation(kind, r, target, cosine, theta, recovered, LightSpaceOracleCandidateRootResidualValidity.Invalid);
            return true;
        }

        /// <summary>Records one rejected root using only facts computed before its admission branch returns.</summary>
        private static bool RecordRootTopologyFailure(LightSpaceOracleCandidateDiagnosticSink diagnostics, LightSpaceOracleCandidateRootKind kind, double r, double target, double cosine, double theta, LightSpaceOracleCandidateRootInteriorPresence interiorPresence, LightSpaceOracleCandidateRootTopologyStage stage)
        {
            diagnostics?.RecordFirstRootTopologyFailure(kind, r, target, cosine, LightSpaceOracleCandidateCosineCorrection.None, theta, double.NaN, LightSpaceOracleCandidateRootResidualValidity.NotEvaluated, interiorPresence, LightSpaceOracleCandidateRootSemanticOrder.NotReached, stage);
            return false;
        }

        /// <summary>Builds outward binary64 cosine bounds for the local affine root equation.</summary>
        private static bool TryAffineCosineInterval(double target, double u, double v, double z, out double lower, out double upper)
        {
            lower = double.NaN; upper = double.NaN;
            if (!Finite(target) || !Finite(u) || !Finite(v) || !Finite(z) || z <= 0.0d) return false;
            if (!TryOutward(target * 0.5d, true, out double halfLower) || !TryOutward(target * 0.5d, false, out double halfUpper)) return false;
            if (!TryOutward(halfLower - 1.0d, true, out double baseLower) || !TryOutward(halfUpper - 1.0d, false, out double baseUpper)) return false;
            if (!TryOutward(u * v, true, out double productLower) || !TryOutward(u * v, false, out double productUpper)) return false;
            if (!TryOutward(baseLower - productUpper, true, out double numeratorLower) || !TryOutward(baseUpper - productLower, false, out double numeratorUpper)) return false;
            return TryOutward(numeratorLower / z, true, out lower) && TryOutward(numeratorUpper / z, false, out upper) && lower <= upper;
        }

        /// <summary>Builds outward binary64 q bounds solely from affine cosine bounds and stored coefficients.</summary>
        private static bool TryAffineTargetInterval(double u, double v, double z, double cosineLower, double cosineUpper, out double lower, out double upper)
        {
            lower = double.NaN; upper = double.NaN;
            if (!Finite(u) || !Finite(v) || !Finite(z) || z <= 0.0d || !Finite(cosineLower) || !Finite(cosineUpper) || cosineLower > cosineUpper) return false;
            if (!TryOutward(u * v, true, out double productLower) || !TryOutward(u * v, false, out double productUpper)) return false;
            if (!TryOutward(z * cosineLower, true, out double cosineProductLower) || !TryOutward(z * cosineUpper, false, out double cosineProductUpper)) return false;
            if (!TryOutward(1.0d + productLower, true, out double baseLower) || !TryOutward(1.0d + productUpper, false, out double baseUpper)) return false;
            if (!TryOutward(baseLower + cosineProductLower, true, out double sumLower) || !TryOutward(baseUpper + cosineProductUpper, false, out double sumUpper)) return false;
            return TryOutward(2.0d * sumLower, true, out lower) && TryOutward(2.0d * sumUpper, false, out upper) && lower <= upper;
        }

        /// <summary>Rounds one finite binary64 operation result outward by one adjacent representable value.</summary>
        private static bool TryOutward(double value, bool lower, out double bound)
        {
            bound = double.NaN;
            if (!Finite(value)) return false;
            long bits = BitConverter.DoubleToInt64Bits(value);
            if (value == 0.0d) bits = lower ? unchecked((long)0x8000000000000001UL) : 1L;
            else bits += value > 0.0d == lower ? -1L : 1L;
            bound = BitConverter.Int64BitsToDouble(bits);
            return Finite(bound);
        }

        /// <summary>Derives the local GGX denominator transition, treating endpoint contacts as valid absences.</summary>
        private static bool TryDistributionRoot(double p, double r, double u, double v, double z, LightSpaceOracleCandidateDiagnosticSink diagnostics, out double theta)
        {
            theta = double.NaN;
            if (z <= 0.0d || p >= 1.0d) return true;
            double alphaSquared = p * p * p * p; double h2 = (1.0d - Math.Sqrt(1.0e-6d / Math.PI)) / (1.0d - alphaSquared);
            double target = (u + v) * (u + v) / h2;
            if (!Finite(target)) return RecordRootTopologyFailure(diagnostics, LightSpaceOracleCandidateRootKind.Distribution, r, target, double.NaN, double.NaN, LightSpaceOracleCandidateRootInteriorPresence.NotReached, LightSpaceOracleCandidateRootTopologyStage.TargetDerived);
            return target < 1.0e-6d || TryRoot(LightSpaceOracleCandidateRootKind.Distribution, r, target, u, v, z, diagnostics, out theta);
        }

        /// <summary>Builds semantic guard-first ties and strictly ordered endpoint-inclusive atomic boundaries.</summary>
        private static LightSpaceOracleCandidateThetaPartition BuildPartition(double guard, double distribution)
        {
            bool hasGuard = Finite(guard); bool hasDistribution = Finite(distribution);
            if (!hasGuard && !hasDistribution) return new LightSpaceOracleCandidateThetaPartition(new[] { 0.0d, Math.PI });
            if (hasGuard && (!hasDistribution || WithinUlps(guard, distribution, 32))) return new LightSpaceOracleCandidateThetaPartition(new[] { 0.0d, guard, Math.PI });
            if (!hasGuard) return new LightSpaceOracleCandidateThetaPartition(new[] { 0.0d, distribution, Math.PI });
            return guard < distribution ? new LightSpaceOracleCandidateThetaPartition(new[] { 0.0d, guard, distribution, Math.PI }) : new LightSpaceOracleCandidateThetaPartition(new[] { 0.0d, distribution, guard, Math.PI });
        }

        /// <summary>Compares finite nonnegative magnitudes by binary64 ULP distance.</summary>
        private static bool WithinUlps(double left, double right, int limit)
        {
            if (!Finite(left) || !Finite(right) || left < 0.0d || right < 0.0d) return false;
            ulong leftBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(left)); ulong rightBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(right));
            return leftBits >= rightBits ? leftBits - rightBits <= (ulong)limit : rightBits - leftBits <= (ulong)limit;
        }

        /// <summary>Gets whether a binary64 component is finite.</summary>
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Gets whether a finite value lies in the closed unit interval.</summary>
        private static bool Unit(double value) => Finite(value) && value >= 0.0d && value <= 1.0d;
    }
}
