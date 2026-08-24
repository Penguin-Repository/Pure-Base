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

// Independently evaluates selected fixed-r angular intervals from raw binary64 inputs.

using System;

namespace PureBase.Tests.Daily
{
    /// <summary>Marks a selected interval's independent coverage and error-enrichment memberships.</summary>
    [Flags]
    internal enum AngularDiagnosticCohort { None = 0, Coverage = 1, Enrichment = 2 }

    /// <summary>Marks each selected interval's exact diagnostic selection strata.</summary>
    [Flags]
    internal enum AngularDiagnosticStratum { None = 0, ControlLow = 1, ControlHigh = 2, Narrowest = 4, Widest = 8, GuardAdjacent = 16, DistributionAdjacent = 32, SmoothNoRoot = 64, WeightedTop = 128, DifferenceTop = 256, DifferenceBottom = 512, WeightedExcludedLeafTop = 1024 }

    /// <summary>Explains why an independent checker reference is unavailable.</summary>
    internal enum AngularDiagnosticUnavailableReason { None, IdentityInconsistency, InvalidInput, RuleGenerationFailure, NonFiniteCalculation, UncertaintyRejected }

    /// <summary>Identifies whether the candidate indicator is zero or nonzero.</summary>
    internal enum AngularDiagnosticRatioState { UnavailableZeroIndicator, Available }

    /// <summary>States whether candidate provenance and the independent checker have the same bounded identity.</summary>
    internal enum AngularDiagnosticIdentityState { Compatible, Inconsistent }

    /// <summary>Names one immutable numerical interval signal without classifying terminal evidence.</summary>
    internal enum AngularDiagnosticIntervalSignal { B, A, FloorConsistent, Mixed }

    /// <summary>Marks the endpoint-inclusive shape of one retained candidate theta topology.</summary>
    [Flags]
    internal enum AngularDiagnosticTopologyMask { None = 0, LeftEndpoint = 1, Interior = 2, RightEndpoint = 4 }

    /// <summary>Contains only the raw identity needed by the isolated angular calculation layer.</summary>
    internal readonly struct AngularDiagnosticCalculationInput
    {
        /// <summary>Initializes one calculation input without imported estimator data.</summary>
        internal AngularDiagnosticCalculationInput(long pBits, long ndotVBits, IndependentOracleBranch branch, long radialBits, long thetaLeftBits, long thetaRightBits)
        {
            this.pBits = pBits; this.ndotVBits = ndotVBits; this.branch = branch; this.radialBits = radialBits; this.thetaLeftBits = thetaLeftBits; this.thetaRightBits = thetaRightBits;
        }

        /// <summary>Gets roughness binary64 bits.</summary>
        internal readonly long pBits;
        /// <summary>Gets view cosine binary64 bits.</summary>
        internal readonly long ndotVBits;
        /// <summary>Gets the visibility branch.</summary>
        internal readonly IndependentOracleBranch branch;
        /// <summary>Gets radial binary64 bits.</summary>
        internal readonly long radialBits;
        /// <summary>Gets left theta endpoint binary64 bits.</summary>
        internal readonly long thetaLeftBits;
        /// <summary>Gets right theta endpoint binary64 bits.</summary>
        internal readonly long thetaRightBits;
    }

    /// <summary>Stores requested, selected, and missing slots for one exact selection stratum.</summary>
    internal readonly struct AngularDiagnosticSelectionSlot
    {
        /// <summary>Initializes one immutable stratum result without backfilling missing slots.</summary>
        internal AngularDiagnosticSelectionSlot(AngularDiagnosticStratum stratum, int requested, int selected) { Stratum = stratum; Requested = requested; Selected = selected; }
        /// <summary>Gets the selection stratum.</summary>
        internal AngularDiagnosticStratum Stratum { get; }
        /// <summary>Gets the exact requested count.</summary>
        internal int Requested { get; }
        /// <summary>Gets the count actually selected from the stratum.</summary>
        internal int Selected { get; }
        /// <summary>Gets the retained no-backfill shortfall.</summary>
        internal int Shortfall => Requested - Selected;
    }

    /// <summary>Identifies the terminal state of an independent angular checker calculation.</summary>
    internal enum AngularDiagnosticCheckStopState { Accepted, InvalidInput, RuleGenerationFailure, NonFiniteCalculation }

    /// <summary>Identifies an independently reconstructed endpoint or interior boundary source.</summary>
    internal enum AngularDiagnosticBoundaryKind { Endpoint, Guard, Distribution }

    /// <summary>Stores an independently generated quadrature result and bounded uncertainty facts.</summary>
    internal readonly struct AngularDiagnosticCheck
    {
        /// <summary>Initializes a completed calculation result.</summary>
        internal AngularDiagnosticCheck(double q17, double q33, double g127, double g251, double g503, int work, bool finite, AngularDiagnosticCheckStopState stopState, string ruleIdentity)
        {
            Q17 = q17; Q33 = q33; G127 = g127; G251 = g251; G503 = g503; Work = work; Finite = finite; StopState = stopState; RuleIdentity = ruleIdentity;
        }

        /// <summary>Gets independent Fejer-II order-17 value.</summary>
        internal double Q17 { get; }
        /// <summary>Gets independent Fejer-II order-33 value.</summary>
        internal double Q33 { get; }
        /// <summary>Gets independent Gauss-Legendre order-127 value.</summary>
        internal double G127 { get; }
        /// <summary>Gets independent Gauss-Legendre order-251 value.</summary>
        internal double G251 { get; }
        /// <summary>Gets independent Gauss-Legendre order-503 value.</summary>
        internal double G503 { get; }
        /// <summary>Gets the scalar evaluation count used by all five rules.</summary>
        internal int Work { get; }
        /// <summary>Gets whether every generated rule and calculated value was finite.</summary>
        internal bool Finite { get; }
        /// <summary>Gets the explicit bounded calculation terminal state.</summary>
        internal AngularDiagnosticCheckStopState StopState { get; }
        /// <summary>Gets the identity of every rule required by this calculation.</summary>
        internal string RuleIdentity { get; }
    }

    /// <summary>Stores immutable raw binary64 coordinates and weights for one independently generated rule.</summary>
    internal readonly struct AngularDiagnosticRuleBits
    {
        /// <summary>Initializes one immutable rule identity.</summary>
        internal AngularDiagnosticRuleBits(long[] coordinates, long[] weights) { this.coordinates = Copy(coordinates); this.weights = Copy(weights); }
        /// <summary>Gets ordered raw coordinate bits.</summary>
        internal long[] Coordinates => Copy(coordinates);
        /// <summary>Gets ordered raw weight bits.</summary>
        internal long[] Weights => Copy(weights);

        private readonly long[] coordinates;
        private readonly long[] weights;
        private static long[] Copy(long[] source) => source == null ? Array.Empty<long>() : (long[])source.Clone();
    }

    /// <summary>Builds independent angular rules, topology boundaries, and compensated fixed-r integrations.</summary>
    internal static class AngularDiagnosticQuadrature
    {
        /// <summary>Calculates all fixed diagnostic rules from one raw immutable interval identity.</summary>
        internal static AngularDiagnosticCheck Check(AngularDiagnosticCalculationInput input)
        {
            double left = Bits(input.thetaLeftBits); double right = Bits(input.thetaRightBits);
            if (!Finite(left) || !Finite(right) || left < 0.0d || right > Math.PI || left >= right) return Failed(AngularDiagnosticCheckStopState.InvalidInput);
            if (!TryLegendre(127, out Node[] g127Rule) || !TryLegendre(251, out Node[] g251Rule) || !TryLegendre(503, out Node[] g503Rule)) return Failed(AngularDiagnosticCheckStopState.RuleGenerationFailure);
            double q17 = Integrate(input, Fejer(17)); double q33 = Integrate(input, Fejer(33)); double g127 = Integrate(input, g127Rule); double g251 = Integrate(input, g251Rule); double g503 = Integrate(input, g503Rule);
            bool finite = Finite(q17) && Finite(q33) && Finite(g127) && Finite(g251) && Finite(g503); return new AngularDiagnosticCheck(q17, q33, g127, g251, g503, finite ? 931 : 0, finite, finite ? AngularDiagnosticCheckStopState.Accepted : AngularDiagnosticCheckStopState.NonFiniteCalculation, "FejerII17/FejerII33/GaussLegendre127/GaussLegendre251/GaussLegendre503");
        }

        /// <summary>Reconstructs endpoint-inclusive boundary bits from the raw physical input.</summary>
        internal static long[] ReconstructBoundaryBits(AngularDiagnosticCalculationInput input)
        {
            double p = Bits(input.pBits); double r = Bits(input.radialBits); double v = Bits(input.ndotVBits);
            if (!Unit(p) || !Unit(r) || !Unit(v)) return Array.Empty<long>();
            double sine = Math.Sin(Math.PI * r * 0.5d); double u = sine * sine; double z = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v);
            if (!Finite(z)) return Array.Empty<long>();
            double guard = Root(1.0e-6d, r, u, v, z); double distribution = DistributionRoot(p, r, u, v, z);
            return BoundaryBits(guard, distribution);
        }

        /// <summary>Reconstructs semantic boundary kinds in the same independent topology order.</summary>
        internal static AngularDiagnosticBoundaryKind[] ReconstructBoundaryKinds(AngularDiagnosticCalculationInput input)
        {
            double p = Bits(input.pBits); double r = Bits(input.radialBits); double v = Bits(input.ndotVBits); if (!Unit(p) || !Unit(r) || !Unit(v)) return Array.Empty<AngularDiagnosticBoundaryKind>();
            double sine = Math.Sin(Math.PI * r * 0.5d); double u = sine * sine; double z = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v); if (!Finite(z)) return Array.Empty<AngularDiagnosticBoundaryKind>();
            double guard = Root(1.0e-6d, r, u, v, z); double distribution = DistributionRoot(p, r, u, v, z); bool hasGuard = Finite(guard); bool hasDistribution = Finite(distribution);
            if (!hasGuard && !hasDistribution) return new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Endpoint };
            if (hasGuard && (!hasDistribution || Close(guard, distribution))) return new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Guard, AngularDiagnosticBoundaryKind.Endpoint };
            if (!hasGuard) return new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Distribution, AngularDiagnosticBoundaryKind.Endpoint };
            return guard < distribution ? new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Guard, AngularDiagnosticBoundaryKind.Distribution, AngularDiagnosticBoundaryKind.Endpoint } : new[] { AngularDiagnosticBoundaryKind.Endpoint, AngularDiagnosticBoundaryKind.Distribution, AngularDiagnosticBoundaryKind.Guard, AngularDiagnosticBoundaryKind.Endpoint };
        }

        /// <summary>Checks independent rule invariants used before interpreting a calculation.</summary>
        internal static bool RulesAreValid()
        {
            return TryLegendre(127, out Node[] g127) && TryLegendre(251, out Node[] g251) && TryLegendre(503, out Node[] g503) && RuleIsValid(Fejer(17), false) && RuleIsValid(Fejer(33), false) && RuleIsValid(g127, true) && RuleIsValid(g251, true) && RuleIsValid(g503, true);
        }

        /// <summary>Generates one independent Fejer-II rule identity for candidate-versus-checker comparison.</summary>
        internal static AngularDiagnosticRuleBits FejerBits(int order)
        {
            Node[] rule = Fejer(order); var coordinates = new long[rule.Length]; var weights = new long[rule.Length];
            for (int index = 0; index < rule.Length; index++) { coordinates[index] = Bits(rule[index].X); weights[index] = Bits(rule[index].Weight); }
            return new AngularDiagnosticRuleBits(coordinates, weights);
        }

        /// <summary>Exposes bounded Newton convergence for a synthetic rule-generation fixture.</summary>
        internal static AngularDiagnosticCheckStopState LegendreStopStateForFixture(int order, int maximumIterations) => TryLegendre(order, maximumIterations, out Node[] _) ? AngularDiagnosticCheckStopState.Accepted : AngularDiagnosticCheckStopState.RuleGenerationFailure;

        /// <summary>Evaluates the expanded scalar without importing another numerical implementation.</summary>
        private static double Scalar(AngularDiagnosticCalculationInput input, double theta)
        {
            double p = Bits(input.pBits); double r = Bits(input.radialBits); double v = Bits(input.ndotVBits); double sine = Math.Sin(Math.PI * r * 0.5d); double u = sine * sine;
            double alpha = p * p; double alphaSquared = alpha * alpha; double root = Math.Sqrt(1.0d - u * u) * Math.Sqrt(1.0d - v * v);
            double q = 2.0d * (1.0d + u * v + root * Math.Cos(theta)); double h2 = (u + v) * (u + v) / Math.Max(q, 1.0e-6d);
            double baseValue = h2 * (alphaSquared - 1.0d) + 1.0d; double distribution = alphaSquared / Math.Max(Math.PI * baseValue * baseValue, 1.0e-6d);
            double epsilon = input.branch == IndependentOracleBranch.Normal ? 1.0e-5d : 1.0d / 16384.0d;
            double visibility = 0.5d / (u * (v * (1.0d - alpha) + alpha) + v * (u * (1.0d - alpha) + alpha) + epsilon);
            return 2.0d * distribution * visibility * u * Math.PI * Math.Sin(Math.PI * r) * 0.5d;
        }

        /// <summary>Integrates one interval by a private compensated accumulation.</summary>
        private static double Integrate(AngularDiagnosticCalculationInput input, Node[] rule)
        {
            double left = Bits(input.thetaLeftBits); double right = Bits(input.thetaRightBits); double half = (right - left) * 0.5d; double middle = (right + left) * 0.5d; double sum = 0.0d; double correction = 0.0d;
            for (int index = 0; index < rule.Length; index++) Add(ref sum, ref correction, rule[index].Weight * Scalar(input, middle + half * rule[index].X));
            return (sum + correction) * half;
        }

        /// <summary>Adds one finite term with Neumaier compensation.</summary>
        private static void Add(ref double sum, ref double correction, double value)
        {
            double next = sum + value; correction += Math.Abs(sum) >= Math.Abs(value) ? sum - next + value : value - next + sum; sum = next;
        }

        /// <summary>Creates an endpoint-free Fejer-II rule by its finite odd-harmonic series.</summary>
        private static Node[] Fejer(int order)
        {
            var result = new Node[order]; int terms = (order + 1) / 2;
            for (int index = 1; index <= order; index++) { double angle = Math.PI * index / (order + 1); double series = 0.0d; for (int term = 1; term <= terms; term++) { int harmonic = 2 * term - 1; series += Math.Sin(harmonic * angle) / harmonic; } result[index - 1] = new Node(Math.Cos(angle), 4.0d * Math.Sin(angle) * series / (order + 1)); }
            return result;
        }

        /// <summary>Creates a Gauss-Legendre rule only when every bounded Newton solve converges.</summary>
        private static bool TryLegendre(int order, out Node[] result) => TryLegendre(order, 32, out result);

        /// <summary>Creates a Gauss-Legendre rule with an explicit bounded Newton iteration count.</summary>
        private static bool TryLegendre(int order, int maximumIterations, out Node[] result)
        {
            result = new Node[order];
            for (int root = 0; root < (order + 1) / 2; root++)
            {
                double x = Math.Cos(Math.PI * (root + 0.75d) / (order + 0.5d)); bool converged = false;
                for (int iteration = 0; iteration < maximumIterations; iteration++) { Polynomial(order, x, out double value, out double derivative); if (!Finite(value) || !Finite(derivative) || derivative == 0.0d) return false; double next = x - value / derivative; if (!Finite(next)) return false; if (Math.Abs(next - x) <= 4.0e-16d) { x = next; converged = true; break; } x = next; }
                if (!converged) return false;
                Polynomial(order, x, out double finalValue, out double finalDerivative); double weight = 2.0d / ((1.0d - x * x) * finalDerivative * finalDerivative); if (!Finite(finalValue) || !Finite(finalDerivative) || !Finite(weight) || weight <= 0.0d) return false; result[root] = new Node(-x, weight); result[order - 1 - root] = new Node(x, weight);
            }
            return true;
        }

        /// <summary>Evaluates the Legendre polynomial and derivative by its three-term recurrence.</summary>
        private static void Polynomial(int order, double x, out double value, out double derivative)
        {
            double previous = 1.0d; double current = x;
            for (int degree = 2; degree <= order; degree++)
            {
                double next = ((2.0d * degree - 1.0d) * x * current - (degree - 1.0d) * previous) / degree;
                previous = current; current = next;
            }
            value = order == 0 ? previous : current; derivative = order * (x * current - previous) / (x * x - 1.0d);
        }

        /// <summary>Derives one valid interior theta root or an absent NaN marker.</summary>
        private static double Root(double target, double r, double u, double v, double z)
        {
            if (z <= 0.0d || r == 0.0d) return double.NaN; double cosine = (target * 0.5d - 1.0d - u * v) / z;
            if (!Finite(cosine) || cosine <= -1.0d || cosine >= 1.0d) return double.NaN; double theta = Math.Acos(cosine); return Finite(theta) && theta > 0.0d && theta < Math.PI ? theta : double.NaN;
        }

        /// <summary>Derives the local distribution transition using the same physical equation.</summary>
        private static double DistributionRoot(double p, double r, double u, double v, double z)
        {
            if (z <= 0.0d || p >= 1.0d) return double.NaN; double alphaSquared = p * p * p * p; double h2 = (1.0d - Math.Sqrt(1.0e-6d / Math.PI)) / (1.0d - alphaSquared); double target = (u + v) * (u + v) / h2;
            return Finite(target) && target >= 1.0e-6d ? Root(target, r, u, v, z) : double.NaN;
        }

        /// <summary>Builds guard-first ordered endpoint-inclusive topology bits.</summary>
        private static long[] BoundaryBits(double guard, double distribution)
        {
            bool hasGuard = Finite(guard); bool hasDistribution = Finite(distribution);
            if (!hasGuard && !hasDistribution) return new[] { Bits(0.0d), Bits(Math.PI) };
            if (hasGuard && (!hasDistribution || Close(guard, distribution))) return new[] { Bits(0.0d), Bits(guard), Bits(Math.PI) };
            if (!hasGuard) return new[] { Bits(0.0d), Bits(distribution), Bits(Math.PI) };
            return guard < distribution ? new[] { Bits(0.0d), Bits(guard), Bits(distribution), Bits(Math.PI) } : new[] { Bits(0.0d), Bits(distribution), Bits(guard), Bits(Math.PI) };
        }

        /// <summary>Checks a rule's finite positive symmetric normalization invariants.</summary>
        private static bool RuleIsValid(Node[] rule, bool moments)
        {
            double sum = 0.0d; for (int index = 0; index < rule.Length; index++) { if (!Finite(rule[index].X) || !Finite(rule[index].Weight) || rule[index].Weight <= 0.0d || Math.Abs(rule[index].X + rule[rule.Length - 1 - index].X) > 5.0e-13d || Math.Abs(rule[index].Weight - rule[rule.Length - 1 - index].Weight) > 5.0e-13d) return false; sum += rule[index].Weight; }
            return Math.Abs(sum - 2.0d) <= 5.0e-11d && (!moments || MomentIsValid(rule));
        }

        /// <summary>Checks low polynomial moments independently of any interval calculation.</summary>
        private static bool MomentIsValid(Node[] rule)
        {
            for (int power = 0; power < 8; power++) { double actual = 0.0d; for (int index = 0; index < rule.Length; index++) actual += rule[index].Weight * Math.Pow(rule[index].X, power); double expected = power % 2 == 0 ? 2.0d / (power + 1) : 0.0d; if (Math.Abs(actual - expected) > 5.0e-11d) return false; }
            return true;
        }

        /// <summary>Compares nonnegative finite values by a small binary64 distance.</summary>
        private static bool Close(double left, double right)
        {
            long leftBits = Bits(left); long rightBits = Bits(right); ulong distance = leftBits >= rightBits ? unchecked((ulong)(leftBits - rightBits)) : unchecked((ulong)(rightBits - leftBits)); return distance <= 32UL;
        }

        /// <summary>Gets a double from raw bits.</summary>
        private static double Bits(long value) => BitConverter.Int64BitsToDouble(value);
        /// <summary>Gets raw bits from a double.</summary>
        private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
        /// <summary>Builds a failed calculation without exposing partial rule values or work.</summary>
        private static AngularDiagnosticCheck Failed(AngularDiagnosticCheckStopState stopState) => new AngularDiagnosticCheck(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0, false, stopState, "FejerII17/FejerII33/GaussLegendre127/GaussLegendre251/GaussLegendre503");
        /// <summary>Gets whether a value is finite.</summary>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        /// <summary>Gets whether a value is within the closed unit domain.</summary>
        private static bool Unit(double value) => Finite(value) && value >= 0.0d && value <= 1.0d;

        /// <summary>Stores one private rule point.</summary>
        private readonly struct Node
        {
            /// <summary>Initializes one rule point.</summary>
            internal Node(double x, double weight) { X = x; Weight = weight; }
            /// <summary>Gets the canonical coordinate.</summary>
            internal double X { get; }
            /// <summary>Gets the integration weight.</summary>
            internal double Weight { get; }
        }
    }
}
