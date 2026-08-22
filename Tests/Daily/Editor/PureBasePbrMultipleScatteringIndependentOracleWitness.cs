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

// Defines the unavailable independent fixed-resolution witness entry point without fabricating a numerical result.

using System;

namespace PureBase.Tests.Daily
{
    /// <summary>Stores the witness values, uncertainty evidence, and fixed scalar-work count.</summary>
    internal readonly struct IndependentOracleWitnessResult
    {
        /// <summary>Initializes the raw witness outcome.</summary>
        internal IndependentOracleWitnessResult(double value, double uncertainty, double jointRatio, double uRatio, double phiRatio, double probeDelta, int evaluations, bool finite) { Value = value; Uncertainty = uncertainty; JointRatio = jointRatio; URatio = uRatio; PhiRatio = phiRatio; ProbeDelta = probeDelta; Evaluations = evaluations; Finite = finite; }
        /// <summary>Gets the finest witness estimate.</summary>
        internal double Value { get; }
        /// <summary>Gets the composed witness uncertainty.</summary>
        internal double Uncertainty { get; }
        /// <summary>Gets the joint-sequence convergence ratio.</summary>
        internal double JointRatio { get; }
        /// <summary>Gets the t-axis convergence ratio.</summary>
        internal double URatio { get; }
        /// <summary>Gets the phi-axis convergence ratio.</summary>
        internal double PhiRatio { get; }
        /// <summary>Gets the adjacent-roughness sensitivity delta.</summary>
        internal double ProbeDelta { get; }
        /// <summary>Gets the historical scalar work.</summary>
        internal int Evaluations { get; }
        /// <summary>Gets whether all scalar and accumulator values were finite.</summary>
        internal bool Finite { get; }
    }

    /// <summary>Provides the unavailable independent fixed-resolution witness integration boundary.</summary>
    internal static class IndependentOracleWitness
    {
        /// <summary>Throws because no independent witness implementation is available to construct a numerical result.</summary>
        internal static IndependentOracleWitnessResult Integrate(IndependentOracleInput input)
        {
            throw new NotImplementedException("The independent fixed-resolution witness is not implemented.");
        }
    }
}
