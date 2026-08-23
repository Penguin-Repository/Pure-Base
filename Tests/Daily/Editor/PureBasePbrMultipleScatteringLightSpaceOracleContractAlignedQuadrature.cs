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

// Defines private fixed quadrature rules for the contract-aligned light-space candidate.

using System;

namespace PureBase.Tests.Daily
{
    /// <summary>Stores one private candidate quadrature coordinate and its integration weight.</summary>
    internal readonly struct LightSpaceOracleCandidateRuleNode
    {
        /// <summary>Initializes one immutable candidate rule node.</summary>
        internal LightSpaceOracleCandidateRuleNode(double coordinate, double weight) { Coordinate = coordinate; Weight = weight; }

        /// <summary>Gets the canonical coordinate on [-1, 1].</summary>
        internal double Coordinate { get; }

        /// <summary>Gets the positive integration weight.</summary>
        internal double Weight { get; }
    }

    /// <summary>Builds the fixed nested outer and endpoint-free inner rules without shared numerical helpers.</summary>
    internal static class LightSpaceOracleCandidateQuadrature
    {
        /// <summary>Builds the candidate's endpoint-inclusive Clenshaw--Curtis rule.</summary>
        internal static LightSpaceOracleCandidateRuleNode[] ClenshawCurtis(int order)
        {
            int denominator = order - 1;
            var nodes = new LightSpaceOracleCandidateRuleNode[order];
            for (int index = 0; index < order; index++)
            {
                double angle = Math.PI * index / denominator;
                double sum = 1.0d;
                for (int harmonic = 1; harmonic < denominator / 2; harmonic++) sum -= 2.0d * Math.Cos(2.0d * harmonic * angle) / (4.0d * harmonic * harmonic - 1.0d);
                sum -= Math.Cos(denominator * angle) / (denominator * denominator - 1.0d);
                double weight = index == 0 || index == denominator ? 1.0d / (denominator * denominator - 1.0d) : 2.0d * sum / denominator;
                nodes[index] = new LightSpaceOracleCandidateRuleNode(Math.Cos(angle), weight);
            }
            return nodes;
        }

        /// <summary>Builds the candidate's endpoint-free Fejer-II rule.</summary>
        internal static LightSpaceOracleCandidateRuleNode[] FejerII(int order)
        {
            var nodes = new LightSpaceOracleCandidateRuleNode[order];
            int terms = (order + 1) / 2;
            for (int index = 1; index <= order; index++)
            {
                double angle = Math.PI * index / (order + 1);
                double series = 0.0d;
                for (int term = 1; term <= terms; term++)
                {
                    int harmonic = 2 * term - 1;
                    series += Math.Sin(harmonic * angle) / harmonic;
                }
                nodes[index - 1] = new LightSpaceOracleCandidateRuleNode(Math.Cos(angle), 4.0d * Math.Sin(angle) * series / (order + 1));
            }
            return nodes;
        }
    }
}
