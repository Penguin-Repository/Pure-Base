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

// Defines the deterministic outer-only scheduler for the contract-aligned light-space candidate.

using System;
using System.Collections.Generic;

namespace PureBase.Tests.Daily
{
    /// <summary>Stores one fully evaluated candidate outer leaf before transactional commitment.</summary>
    internal readonly struct LightSpaceOracleCandidateLeaf
    {
        /// <summary>Initializes an immutable candidate outer leaf.</summary>
        internal LightSpaceOracleCandidateLeaf(IndependentOracleCanonicalPath path, double left, double right, double value, double error) { Path = path; Left = left; Right = right; Value = value; Error = error; }

        /// <summary>Gets the canonical leaf identity.</summary>
        internal IndependentOracleCanonicalPath Path { get; }

        /// <summary>Gets the closed radial interval start.</summary>
        internal double Left { get; }

        /// <summary>Gets the closed radial interval end.</summary>
        internal double Right { get; }

        /// <summary>Gets the fine candidate estimate.</summary>
        internal double Value { get; }

        /// <summary>Gets the independent radial-plus-angular error indicator.</summary>
        internal double Error { get; }
    }

    /// <summary>Runs the frozen outer-only candidate scheduler with fail-closed resource transactions.</summary>
    internal sealed class LightSpaceOracleCandidateScheduler
    {
        private readonly IndependentOracleInput input;
        private readonly double target;
        private readonly LightSpaceOracleCandidateDiagnosticSink diagnostics;
        private readonly LightSpaceOracleCandidateRuleNode[] outerFine = LightSpaceOracleCandidateQuadrature.ClenshawCurtis(17);
        private readonly LightSpaceOracleCandidateRuleNode[] outerCoarse = LightSpaceOracleCandidateQuadrature.ClenshawCurtis(9);
        private readonly LightSpaceOracleCandidateRuleNode[] innerCoarse = LightSpaceOracleCandidateQuadrature.FejerII(17);
        private readonly LightSpaceOracleCandidateRuleNode[] innerFine = LightSpaceOracleCandidateQuadrature.FejerII(33);
        private readonly List<LightSpaceOracleCandidateLeaf> leaves = new List<LightSpaceOracleCandidateLeaf>();
        private LightSpaceOracleStopState stopState = LightSpaceOracleStopState.Accepted;
        private int evaluations;
        private int panels;
        private int maximumDepth;

        /// <summary>Initializes one candidate run with a write-only observer-neutral diagnostic sink.</summary>
        internal LightSpaceOracleCandidateScheduler(IndependentOracleInput input, double target, LightSpaceOracleCandidateDiagnosticSink diagnostics) { this.input = input; this.target = target; this.diagnostics = diagnostics; }

        /// <summary>Runs root evaluation and deterministic transactional outer refinement to acceptance or a hard stop.</summary>
        internal LightSpaceOracleResult Run()
        {
            if (!LightSpaceOracleContractAlignedCandidate.ValidInput(input, target)) { Fail(LightSpaceOracleStopState.NonFiniteInput, default); return Result(); }
            if (!TryReservePanels(1) || !TryEvaluateLeaf(new IndependentOracleCanonicalPath(0, 0UL), 0.0d, 1.0d, out LightSpaceOracleCandidateLeaf root)) return Result();
            leaves.Add(root);
            while (true)
            {
                if (!TryAggregate(out double value, out double error)) { Fail(LightSpaceOracleStopState.GlobalError, default); return Result(); }
                if (error <= target) return Accept(value, error);
                int selected = SelectHighestErrorLeaf(); LightSpaceOracleCandidateLeaf parent = leaves[selected];
                if (parent.Path.Depth >= LightSpaceOracleContractAlignedCandidate.MaximumDepth) { Fail(LightSpaceOracleStopState.DepthCap, parent.Path); return Result(); }
                if (!TryReservePanels(2)) return Result();
                maximumDepth = Math.Max(maximumDepth, parent.Path.Depth + 1); double midpoint = (parent.Left + parent.Right) * 0.5d;
                if (!TryEvaluateLeaf(Child(parent.Path, false), parent.Left, midpoint, out LightSpaceOracleCandidateLeaf left)) return Result();
                if (!TryEvaluateLeaf(Child(parent.Path, true), midpoint, parent.Right, out LightSpaceOracleCandidateLeaf right)) return Result();
                leaves.RemoveAt(selected); leaves.Add(left); leaves.Add(right);
            }
        }

        /// <summary>Evaluates shared fine outer nodes and composes the frozen radial and weighted angular indicators.</summary>
        private bool TryEvaluateLeaf(IndependentOracleCanonicalPath path, double left, double right, out LightSpaceOracleCandidateLeaf leaf)
        {
            leaf = default; var values = new double[17]; var angularErrors = new double[17]; double half = (right - left) * 0.5d; double middle = (right + left) * 0.5d;
            for (int index = 0; index < outerFine.Length; index++) if (!TryEvaluateNode(middle + half * outerFine[index].Coordinate, out values[index], out angularErrors[index])) return false;
            double q17 = 0.0d; double q9 = 0.0d; double thetaError = 0.0d;
            for (int index = 0; index < outerFine.Length; index++) { q17 += outerFine[index].Weight * values[index]; thetaError += outerFine[index].Weight * angularErrors[index]; }
            for (int index = 0; index < outerCoarse.Length; index++) q9 += outerCoarse[index].Weight * values[index * 2];
            q17 *= half; q9 *= half; thetaError *= half; double error = LightSpaceOracleContractAlignedCandidate.ComposeLeafError(q9, q17, thetaError);
            if (!Finite(q17) || !Finite(error) || error < 0.0d) return Fail(LightSpaceOracleStopState.NonFiniteSample, path);
            leaf = new LightSpaceOracleCandidateLeaf(path, left, right, q17, error); diagnostics?.Record(path, evaluations, panels, q17, error, LightSpaceOracleStopState.Accepted); return true;
        }

        /// <summary>Evaluates exactly one 17/33 Fejer-II pair for every root-defined atomic theta interval.</summary>
        private bool TryEvaluateNode(double r, out double value, out double angularError)
        {
            value = double.NaN; angularError = double.NaN;
            if (!LightSpaceOracleContractAlignedCandidate.TryDeriveThetaPartition(input, r, out LightSpaceOracleCandidateThetaPartition partition, diagnostics)) return Fail(LightSpaceOracleStopState.RootTopologyFailure, default);
            value = 0.0d; angularError = 0.0d; double[] boundaries = partition.Boundaries;
            for (int index = 0; index + 1 < boundaries.Length; index++)
            {
                if (!TryEvaluateInterval(r, boundaries[index], boundaries[index + 1], out double coarse, out double fine)) return false;
                value += fine; angularError += Math.Abs(fine - coarse);
            }
            return Finite(value) && Finite(angularError) && angularError >= 0.0d || Fail(LightSpaceOracleStopState.NonFiniteSample, default);
        }

        /// <summary>Integrates one atomic interval once with private endpoint-free coarse and fine rules.</summary>
        private bool TryEvaluateInterval(double r, double left, double right, out double coarse, out double fine)
        {
            coarse = 0.0d; fine = 0.0d; double half = (right - left) * 0.5d; double middle = (right + left) * 0.5d;
            for (int index = 0; index < innerCoarse.Length; index++)
            {
                if (!TrySample(r, middle + half * innerCoarse[index].Coordinate, out double sample)) return false;
                coarse += innerCoarse[index].Weight * sample;
            }
            for (int index = 0; index < innerFine.Length; index++)
            {
                if (!TrySample(r, middle + half * innerFine[index].Coordinate, out double sample)) return false;
                fine += innerFine[index].Weight * sample;
            }
            coarse *= half; fine *= half; return Finite(coarse) && Finite(fine) || Fail(LightSpaceOracleStopState.NonFiniteSample, default);
        }

        /// <summary>Reserves exactly one scalar call immediately before its local scalar evaluation.</summary>
        private bool TrySample(double r, double theta, out double value)
        {
            value = double.NaN;
            if (evaluations >= LightSpaceOracleContractAlignedCandidate.MaximumEvaluations) return Fail(LightSpaceOracleStopState.EvaluationCap, default);
            evaluations++; value = LightSpaceOracleContractAlignedCandidate.EvaluateScalar(input, r, theta);
            return Finite(value) || Fail(LightSpaceOracleStopState.NonFiniteSample, default);
        }

        /// <summary>Reserves complete root or sibling panel transactions before numerical work begins.</summary>
        private bool TryReservePanels(int count)
        {
            if (panels > LightSpaceOracleContractAlignedCandidate.MaximumPanels - count) return Fail(LightSpaceOracleStopState.PanelCap, default);
            panels += count; return true;
        }

        /// <summary>Pairwise-reduces spatially ordered value and error sequences separately.</summary>
        private bool TryAggregate(out double value, out double error)
        {
            leaves.Sort((left, right) => left.Path.CompareSpatial(right.Path)); var values = new double[leaves.Count]; var errors = new double[leaves.Count];
            for (int index = 0; index < leaves.Count; index++) { values[index] = leaves[index].Value; errors[index] = leaves[index].Error; }
            value = PairwiseReduce(values); error = PairwiseReduce(errors); return Finite(value) && Finite(error) && error >= 0.0d;
        }

        /// <summary>Selects the deterministic maximum-error leaf, breaking ties by depth then spatial path.</summary>
        private int SelectHighestErrorLeaf()
        {
            int selected = 0;
            for (int index = 1; index < leaves.Count; index++) if (HigherPriority(leaves[index], leaves[selected])) selected = index;
            return selected;
        }

        /// <summary>Gets whether one leaf precedes another in the frozen max-heap order.</summary>
        private static bool HigherPriority(LightSpaceOracleCandidateLeaf left, LightSpaceOracleCandidateLeaf right)
        {
            if (left.Error != right.Error) return left.Error > right.Error;
            int depth = left.Path.Depth.CompareTo(right.Path.Depth); return depth != 0 ? depth < 0 : left.Path.CompareSpatial(right.Path) < 0;
        }

        /// <summary>Reduces a finite sequence with the adjacent-pair tree in its supplied canonical order.</summary>
        private static double PairwiseReduce(double[] values)
        {
            for (int count = values.Length; count > 1; count = (count + 1) / 2)
            {
                int pairs = count / 2; for (int index = 0; index < pairs; index++) values[index] = values[index * 2] + values[index * 2 + 1];
                if (count % 2 != 0) values[pairs] = values[count - 1];
            }
            return values[0];
        }

        /// <summary>Builds a child canonical path without reusing a reference scheduler helper.</summary>
        private static IndependentOracleCanonicalPath Child(IndependentOracleCanonicalPath parent, bool right) => new IndependentOracleCanonicalPath(parent.Depth + 1, (parent.BinaryPath << 1) | (right ? 1UL : 0UL));

        /// <summary>Records a first terminal state and always propagates failure to the immediate caller.</summary>
        private bool Fail(LightSpaceOracleStopState state, IndependentOracleCanonicalPath path)
        {
            if (stopState == LightSpaceOracleStopState.Accepted) { stopState = state; diagnostics?.Record(path, evaluations, panels, double.NaN, double.NaN, state); }
            return false;
        }

        /// <summary>Builds an accepted result only after finite error meets the requested target.</summary>
        private LightSpaceOracleResult Accept(double value, double error) => new LightSpaceOracleResult(value, error, evaluations, panels, maximumDepth, LightSpaceOracleStopState.Accepted, Topology());

        /// <summary>Builds a fail-closed terminal result while retaining only historical resource counters.</summary>
        private LightSpaceOracleResult Result() => new LightSpaceOracleResult(double.NaN, double.NaN, evaluations, panels, maximumDepth, stopState, string.Empty);

        /// <summary>Builds deterministic committed spatial topology metadata for accepted results.</summary>
        private string Topology()
        {
            leaves.Sort((left, right) => left.Path.CompareSpatial(right.Path)); var names = new List<string>();
            foreach (LightSpaceOracleCandidateLeaf leaf in leaves) names.Add(leaf.Path.Depth + ":" + leaf.Path.BinaryPath);
            return string.Join("|", names);
        }

        /// <summary>Gets whether a binary64 value is finite.</summary>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
