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

// Provides shared double-precision primitives and the non-canonical legacy quadrature diagnostic.

using System;

namespace PureBase.Tests.Daily
{
    /// <summary>Provides HLSL-equivalent primitives and the retained non-canonical legacy diagnostic.</summary>
    internal static class PureBasePbrMultipleScatteringReference
    {
        /// <summary>Defines the current public perceptual-roughness floor.</summary>
        internal const double RoughnessFloor = 0.089d;
        /// <summary>Defines the normal-platform visibility denominator epsilon.</summary>
        internal const double NormalEpsilon = 0.00001d;
        /// <summary>Defines the Unity binary16 minimum-normal visibility epsilon.</summary>
        internal const double SwitchEpsilon = 0.00006103515625d;
        /// <summary>Defines the retained non-canonical legacy polar sample count.</summary>
        internal const int LegacyPolarSamples = 64;
        /// <summary>Defines the retained non-canonical legacy azimuth sample count.</summary>
        internal const int LegacyAzimuthSamples = 128;

        private const int MaximumLegendreRootIterations = 64;
        private const double LegendreRootResidualTolerance = 1e-15d;
        private const double LegendreRootUpdateTolerance = 1.7763568394002505e-15d;
        private static readonly Lazy<LegacyRule> LegacyRuleCache = new Lazy<LegacyRule>(CreateLegacyRule);

        /// <summary>Evaluates the retained light-space diagnostic; it is never a canonical furnace source.</summary>
        internal static double IntegrateLegacyDiagnostic(double perceptualRoughness, double ndotV, bool switchBranch)
        {
            LegacyRule rule = LegacyRuleCache.Value;
            double total = 0.0d;
            for (int polar = 0; polar < LegacyPolarSamples; polar++)
            {
                double ndotL = 0.5d * (rule.Nodes[polar] + 1.0d);
                double weight = 0.5d * rule.Weights[polar];
                for (int azimuth = 0; azimuth < LegacyAzimuthSamples; azimuth++)
                    total += weight * EvaluateLegacyIntegrand(perceptualRoughness, ndotL, ndotV, rule.Azimuths[azimuth], switchBranch);
            }

            return total * rule.AzimuthWeight;
        }

        /// <summary>Applies the exact PureBasePbrSafeNormalize algebra in double precision.</summary>
        internal static Direction SafeNormalize(Direction direction)
        {
            double inverseLength = 1.0d / Math.Sqrt(Math.Max(direction.Dot(direction), 0.000001d));
            return direction * inverseLength;
        }

        /// <summary>Evaluates guarded directional terms used by both independent numerical methods.</summary>
        internal static GuardedTerms EvaluateGuardedTerms(Direction light, Direction view, double perceptualRoughness, bool switchBranch)
        {
            Direction half = SafeNormalize(light + view);
            double ndotL = Math.Max(0.0d, light.Z);
            double ndotV = Math.Max(0.0d, view.Z);
            double ndotH = Math.Max(0.0d, half.Z);
            double roughnessSquared = perceptualRoughness * perceptualRoughness;
            double roughnessFourth = roughnessSquared * roughnessSquared;
            double distribution = roughnessFourth / Math.Max(Math.PI * Math.Pow(ndotH * ndotH * (roughnessFourth - 1.0d) + 1.0d, 2.0d), 0.000001d);
            double lambda = ndotL * (ndotV * (1.0d - roughnessSquared) + roughnessSquared)
                + ndotV * (ndotL * (1.0d - roughnessSquared) + roughnessSquared);
            double visibility = 0.5d / (lambda + (switchBranch ? SwitchEpsilon : NormalEpsilon));
            return new GuardedTerms(ndotH, distribution, visibility);
        }

        /// <summary>Creates the bounded-Newton Gauss-Legendre legacy diagnostic rule.</summary>
        private static LegacyRule CreateLegacyRule()
        {
            CreateLegacyGaussLegendre(LegacyPolarSamples, out double[] nodes, out double[] weights);
            var azimuths = new double[LegacyAzimuthSamples];
            double azimuthWeight = 2.0d * Math.PI / LegacyAzimuthSamples;
            for (int index = 0; index < azimuths.Length; index++) azimuths[index] = (index + 0.5d) * azimuthWeight;
            return new LegacyRule(nodes, weights, azimuths, azimuthWeight);
        }

        /// <summary>Evaluates the retained legacy light-space product integrand.</summary>
        private static double EvaluateLegacyIntegrand(double p, double ndotL, double ndotV, double phi, bool switchBranch)
        {
            double sinL = Math.Sqrt(Math.Max(0.0d, 1.0d - ndotL * ndotL));
            double sinV = Math.Sqrt(Math.Max(0.0d, 1.0d - ndotV * ndotV));
            var light = new Direction(sinL * Math.Cos(phi), sinL * Math.Sin(phi), ndotL);
            GuardedTerms terms = EvaluateGuardedTerms(light, new Direction(sinV, 0.0d, ndotV), p, switchBranch);
            return terms.Distribution * terms.Visibility * ndotL;
        }

        /// <summary>Creates finite legacy Gauss-Legendre roots and weights on [-1, 1].</summary>
        private static void CreateLegacyGaussLegendre(int count, out double[] nodes, out double[] weights)
        {
            nodes = new double[count]; weights = new double[count];
            for (int index = 0; index < (count + 1) / 2; index++)
            {
                double root = Math.Cos(Math.PI * (index + 0.75d) / (count + 0.5d));
                RefineLegacyRoot(count, index, ref root);
                double residual = EvaluateLegendre(count, root, out double derivative);
                if (!IsFinite(residual) || !IsFinite(derivative) || derivative == 0.0d) throw new InvalidOperationException("Legacy root became invalid.");
                double weight = 2.0d / ((1.0d - root * root) * derivative * derivative);
                if (!IsFinite(weight)) throw new InvalidOperationException("Legacy weight became nonfinite.");
                nodes[index] = -root; nodes[count - 1 - index] = root;
                weights[index] = weight; weights[count - 1 - index] = weight;
            }
        }

        /// <summary>Refines one legacy Legendre root with a finite bounded Newton loop.</summary>
        private static void RefineLegacyRoot(int count, int index, ref double root)
        {
            for (int iteration = 0; iteration < MaximumLegendreRootIterations; iteration++)
            {
                double residual = EvaluateLegendre(count, root, out double derivative);
                if (!IsFinite(residual) || !IsFinite(derivative) || derivative == 0.0d) throw new InvalidOperationException("Legacy Newton inputs became invalid.");
                double refined = root - residual / derivative;
                if (!IsFinite(refined)) throw new InvalidOperationException("Legacy Newton update became nonfinite.");
                double update = Math.Abs(refined - root); root = refined;
                if (Math.Abs(residual) <= LegendreRootResidualTolerance || update <= LegendreRootUpdateTolerance * Math.Max(1.0d, Math.Abs(refined))) return;
            }

            throw new InvalidOperationException("Legacy Gauss-Legendre root refinement exhausted for node " + index + ".");
        }

        /// <summary>Evaluates one Legendre polynomial and its derivative.</summary>
        private static double EvaluateLegendre(int count, double x, out double derivative)
        {
            double previous = 1.0d; double current = x;
            for (int degree = 2; degree <= count; degree++)
            {
                double next = ((2.0d * degree - 1.0d) * x * current - (degree - 1.0d) * previous) / degree;
                previous = current; current = next;
            }

            derivative = count * (x * current - previous) / (x * x - 1.0d);
            return current;
        }

        /// <summary>Returns whether a double is finite.</summary>
        internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Stores a double-precision direction.</summary>
        internal readonly struct Direction
        {
            /// <summary>Initializes a direction.</summary>
            internal Direction(double x, double y, double z) { X = x; Y = y; Z = z; }
            /// <summary>Gets the x component.</summary>
            internal double X { get; }
            /// <summary>Gets the y component.</summary>
            internal double Y { get; }
            /// <summary>Gets the z component.</summary>
            internal double Z { get; }
            /// <summary>Returns the dot product.</summary>
            internal double Dot(Direction other) => X * other.X + Y * other.Y + Z * other.Z;
            /// <summary>Adds two directions.</summary>
            public static Direction operator +(Direction left, Direction right) => new Direction(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
            /// <summary>Subtracts two directions.</summary>
            public static Direction operator -(Direction left, Direction right) => new Direction(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
            /// <summary>Scales a direction.</summary>
            public static Direction operator *(Direction direction, double scalar) => new Direction(direction.X * scalar, direction.Y * scalar, direction.Z * scalar);
        }

        /// <summary>Stores HLSL-guarded GGX directional terms.</summary>
        internal readonly struct GuardedTerms
        {
            /// <summary>Initializes guarded terms.</summary>
            internal GuardedTerms(double ndotH, double distribution, double visibility) { NdotH = ndotH; Distribution = distribution; Visibility = visibility; }
            /// <summary>Gets the guarded normal-half cosine.</summary>
            internal double NdotH { get; }
            /// <summary>Gets the GGX distribution.</summary>
            internal double Distribution { get; }
            /// <summary>Gets the joint Smith visibility.</summary>
            internal double Visibility { get; }
        }

        /// <summary>Stores the retained legacy quadrature data.</summary>
        private sealed class LegacyRule
        {
            /// <summary>Initializes the legacy rule.</summary>
            internal LegacyRule(double[] nodes, double[] weights, double[] azimuths, double azimuthWeight) { Nodes = nodes; Weights = weights; Azimuths = azimuths; AzimuthWeight = azimuthWeight; }
            /// <summary>Gets the legacy nodes.</summary>
            internal double[] Nodes { get; }
            /// <summary>Gets the legacy weights.</summary>
            internal double[] Weights { get; }
            /// <summary>Gets the legacy azimuths.</summary>
            internal double[] Azimuths { get; }
            /// <summary>Gets the legacy azimuth weight.</summary>
            internal double AzimuthWeight { get; }
        }
    }
}
