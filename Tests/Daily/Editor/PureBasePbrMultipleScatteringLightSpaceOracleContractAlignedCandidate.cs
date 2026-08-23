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

        /// <summary>Derives roots from local q targets and admits only finite residual-valid interior boundaries.</summary>
        internal static bool TryDeriveThetaPartition(IndependentOracleInput input, double r, out LightSpaceOracleCandidateThetaPartition partition)
        {
            partition = default;
            if (!Unit(input.P) || !Unit(input.NdotV) || !Unit(r)) return false;
            double sine = Math.Sin(Math.PI * r * 0.5d); double u = sine * sine; double v = input.NdotV;
            double z = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v);
            if (!Finite(z)) return false;
            if (!TryRoot(0, 1.0e-6d, u, v, z, out double guard)) return false;
            if (!TryDistributionRoot(input.P, u, v, z, out double distribution)) return false;
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
        private static bool TryRoot(int kind, double target, double u, double v, double z, out double theta)
        {
            theta = double.NaN;
            if (z <= 0.0d) return true;
            double cosine = (target * 0.5d - 1.0d - u * v) / z;
            if (!Finite(cosine) || cosine <= -1.0d || cosine >= 1.0d) return true;
            theta = Math.Acos(cosine);
            double recovered = 2.0d * (1.0d + u * v + z * Math.Cos(theta));
            if (theta <= 0.0d || theta >= Math.PI || !WithinUlps(recovered, target, 128)) return false;
            return kind == 0 || kind == 1;
        }

        /// <summary>Derives the local GGX denominator transition, treating endpoint contacts as valid absences.</summary>
        private static bool TryDistributionRoot(double p, double u, double v, double z, out double theta)
        {
            theta = double.NaN;
            if (z <= 0.0d || p >= 1.0d) return true;
            double alphaSquared = p * p * p * p; double h2 = (1.0d - Math.Sqrt(1.0e-6d / Math.PI)) / (1.0d - alphaSquared);
            double target = (u + v) * (u + v) / h2;
            return Finite(target) && (target < 1.0e-6d || TryRoot(1, target, u, v, z, out theta));
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
