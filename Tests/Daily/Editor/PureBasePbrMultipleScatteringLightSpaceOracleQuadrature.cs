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

// Implements the retained non-authoritative light-space quadrature prototype and its optional bounded observation hooks.

using System;
using System.Collections.Generic;

namespace PureBase.Tests.Daily
{
    /// <summary>Runs candidate-local rules without importing shared numerical helpers.</summary>
    internal static class LightSpaceOracleQuadrature
    {
        private const double GuardFloor = 1.0e-6d;
        private const double NormalEpsilon = 1.0e-5d;
        private const double SwitchEpsilon = 1.0d / 16384.0d;
        private const int MaximumDepth = 22;
        private const int MaximumPanels = 262144;
        private const int MaximumEvaluations = 4000000;
        private static readonly LightSpaceOracleRule[] OuterCoarse = BuildClenshawCurtis(9);
        private static readonly LightSpaceOracleRule[] OuterFine = BuildClenshawCurtis(17);
        private static readonly LightSpaceOracleRule[] InnerCoarse = BuildFejer(17);
        private static readonly LightSpaceOracleRule[] InnerFine = BuildFejer(33);

        /// <summary>Integrates a finite raw tuple using the retained deterministic error-descending leaf schedule.</summary>
        internal static LightSpaceOracleResult IntegrateReference(IndependentOracleInput input, double requestedTarget, LightSpaceOracleDiagnosticRecorder recorder)
        {
            if (!ValidInput(input) || !Finite(requestedTarget) || requestedTarget < 0.0d)
                return Result(double.NaN, double.NaN, 0, 0, 0, LightSpaceOracleStopState.NonFiniteInput, string.Empty);

            var state = new LightSpaceOracleRun(input, requestedTarget, recorder);
            if (!state.TryCreate(new IndependentOracleCanonicalPath(0, 0UL), 0.0d, 1.0d, out LightSpaceOraclePanel root))
                return state.ToResult();
            state.Commit(root);
            while (state.NeedsRefinement)
            {
                LightSpaceOraclePanel parent = state.TakeHighestError();
                if (!state.TryPrepareChildren(parent, out LightSpaceOraclePanel left, out LightSpaceOraclePanel right)) return state.ToResult();
                state.CommitChildren(parent, left, right);
            }
            return state.ToResult();
        }

        /// <summary>Builds an even-order Clenshaw--Curtis rule on the canonical interval.</summary>
        private static LightSpaceOracleRule[] BuildClenshawCurtis(int order)
        {
            int denominator = order - 1; var rules = new LightSpaceOracleRule[order];
            for (int index = 0; index < order; index++)
            {
                double angle = Math.PI * index / denominator; double series = 1.0d;
                for (int harmonic = 1; harmonic < denominator / 2; harmonic++) series -= 2.0d * Math.Cos(2.0d * harmonic * angle) / (4.0d * harmonic * harmonic - 1.0d);
                series -= Math.Cos(denominator * angle) / (denominator * denominator - 1.0d);
                double weight = index == 0 || index == denominator ? 1.0d / (denominator * denominator - 1.0d) : 2.0d * series / denominator;
                rules[index] = new LightSpaceOracleRule((1.0d - Math.Cos(angle)) * 0.5d, weight * 0.5d);
            }
            return rules;
        }

        /// <summary>Builds an endpoint-free Fejer-II rule on the canonical interval.</summary>
        private static LightSpaceOracleRule[] BuildFejer(int order)
        {
            var rules = new LightSpaceOracleRule[order]; int terms = (order + 1) / 2;
            for (int index = 1; index <= order; index++)
            {
                double angle = Math.PI * index / (order + 1); double series = 0.0d;
                for (int term = 1; term <= terms; term++) { int harmonic = 2 * term - 1; series += Math.Sin(harmonic * angle) / harmonic; }
                rules[index - 1] = new LightSpaceOracleRule((1.0d - Math.Cos(angle)) * 0.5d, 2.0d * Math.Sin(angle) * series / (order + 1));
            }
            return rules;
        }

        /// <summary>Gets whether a raw representative tuple is finite and within its closed domain.</summary>
        private static bool ValidInput(IndependentOracleInput input) => Finite(input.P) && Finite(input.NdotV) && input.P >= 0.0d && input.P <= 1.0d && input.NdotV >= 0.0d && input.NdotV <= 1.0d;

        /// <summary>Gets whether a binary64 value is finite.</summary>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Builds one terminal result without admitting a partial numerical estimate after a stop.</summary>
        private static LightSpaceOracleResult Result(double value, double error, int evaluations, int panels, int depth, LightSpaceOracleStopState stop, string topology) => new LightSpaceOracleResult(value, error, evaluations, panels, depth, stop, topology);

        /// <summary>Stores a node and weight mapped to the unit interval.</summary>
        private readonly struct LightSpaceOracleRule
        {
            internal LightSpaceOracleRule(double position, double weight) { Position = position; Weight = weight; }
            internal double Position { get; }
            internal double Weight { get; }
        }

        /// <summary>Identifies one candidate-local theta-root source without sharing contract root types.</summary>
        private enum LightSpaceOracleRootKind
        {
            /// <summary>Identifies the safe-normalization guard transition.</summary>
            Guard,
            /// <summary>Identifies the GGX distribution-denominator transition.</summary>
            Distribution
        }

        /// <summary>Stores one independent theta root before semantic ordering.</summary>
        private readonly struct LightSpaceOracleRoot
        {
            internal LightSpaceOracleRoot(LightSpaceOracleRootKind kind, double theta, bool present, bool valid) { Kind = kind; Theta = theta; Present = present; Valid = valid; }
            internal LightSpaceOracleRootKind Kind { get; }
            internal double Theta { get; }
            internal bool Present { get; }
            internal bool Valid { get; }
        }

        /// <summary>Stores one radial-node nested estimate and observed root topology.</summary>
        private readonly struct LightSpaceOracleNodeEstimate
        {
            internal LightSpaceOracleNodeEstimate(double coarse, double fine, double error, IndependentOracleRootMask mask, LightSpaceOracleStopState stop) { Coarse = coarse; Fine = fine; Error = error; Mask = mask; Stop = stop; }
            internal double Coarse { get; }
            internal double Fine { get; }
            internal double Error { get; }
            internal IndependentOracleRootMask Mask { get; }
            internal LightSpaceOracleStopState Stop { get; }
        }

        /// <summary>Stores one fully prepared outer panel and its local estimates.</summary>
        private readonly struct LightSpaceOraclePanel
        {
            internal LightSpaceOraclePanel(IndependentOracleCanonicalPath path, double left, double right, double value, double error, string topology) { Path = path; Left = left; Right = right; Value = value; Error = error; Topology = topology; }
            internal IndependentOracleCanonicalPath Path { get; }
            internal double Left { get; }
            internal double Right { get; }
            internal double Value { get; }
            internal double Error { get; }
            internal string Topology { get; }
        }

        /// <summary>Owns fixed resource counters and the frozen outer max-heap transaction.</summary>
        private sealed class LightSpaceOracleRun
        {
            private readonly IndependentOracleInput input;
            private readonly double target;
            private readonly LightSpaceOracleDiagnosticRecorder recorder;
            private readonly List<LightSpaceOraclePanel> leaves = new List<LightSpaceOraclePanel>();
            private readonly List<IndependentOracleRootMask> masks = new List<IndependentOracleRootMask>();
            private LightSpaceOracleStopState stop = LightSpaceOracleStopState.Accepted;
            private int evaluations;
            private int panels;
            private int maximumDepth;

            internal LightSpaceOracleRun(IndependentOracleInput input, double target, LightSpaceOracleDiagnosticRecorder recorder) { this.input = input; this.target = target; this.recorder = recorder; }
            internal bool NeedsRefinement => stop == LightSpaceOracleStopState.Accepted && TotalError() > target;

            internal bool TryCreate(IndependentOracleCanonicalPath path, double left, double right, out LightSpaceOraclePanel panel)
            {
                panel = default;
                if (recorder != null) recorder.SetPanelContext(path);
                if (panels >= MaximumPanels) return Fail(LightSpaceOracleStopState.PanelCap, "panel-reservation");
                panels++; if (path.Depth > maximumDepth) maximumDepth = path.Depth;
                if (recorder != null) recorder.RecordPrepared(path, left, right);
                return TryEvaluatePanel(path, left, right, out panel);
            }

            internal void Commit(LightSpaceOraclePanel panel)
            {
                leaves.Add(panel); RecordMasks(panel.Topology);
                if (recorder != null) recorder.RecordCommit(panel.Path, panel.Left, panel.Right);
            }

            internal LightSpaceOraclePanel TakeHighestError()
            {
                leaves.Sort(ComparePanels); LightSpaceOraclePanel selected = leaves[0];
                if (recorder != null) recorder.RecordSelected(selected.Path);
                return selected;
            }

            internal bool TryPrepareChildren(LightSpaceOraclePanel parent, out LightSpaceOraclePanel left, out LightSpaceOraclePanel right)
            {
                left = default; right = default;
                if (recorder != null) recorder.SetPanelContext(parent.Path);
                if (parent.Path.Depth >= MaximumDepth) return Fail(LightSpaceOracleStopState.DepthCap, "child-reservation");
                if (panels > MaximumPanels - 2) return Fail(LightSpaceOracleStopState.PanelCap, "child-reservation");
                double middle = (parent.Left + parent.Right) * 0.5d;
                if (!TryCreate(Child(parent.Path, false), parent.Left, middle, out left)) return false;
                if (!TryCreate(Child(parent.Path, true), middle, parent.Right, out right))
                {
                    if (recorder != null) recorder.RecordDiscard(left.Path, left.Left, left.Right);
                    if (recorder != null) recorder.RecordFailure(stop, evaluations, "child-discard");
                    return false;
                }
                return true;
            }

            internal void CommitChildren(LightSpaceOraclePanel parent, LightSpaceOraclePanel left, LightSpaceOraclePanel right)
            {
                for (int index = 0; index < leaves.Count; index++)
                    if (leaves[index].Path.Depth == parent.Path.Depth && leaves[index].Path.BinaryPath == parent.Path.BinaryPath) { leaves.RemoveAt(index); break; }
                leaves.Add(left); leaves.Add(right); RecordMasks(left.Topology); RecordMasks(right.Topology);
                if (recorder != null) recorder.RecordChildrenCommit(parent.Path, new LightSpaceOracleCommittedLeaf(left.Path, left.Left, left.Right), new LightSpaceOracleCommittedLeaf(right.Path, right.Left, right.Right));
            }

            internal LightSpaceOracleResult ToResult()
            {
                if (stop != LightSpaceOracleStopState.Accepted) return Result(double.NaN, double.NaN, evaluations, panels, maximumDepth, stop, string.Empty);
                leaves.Sort((left, right) => left.Path.CompareSpatial(right.Path));
                var values = new double[leaves.Count]; var errors = new double[leaves.Count];
                for (int index = 0; index < leaves.Count; index++) { values[index] = leaves[index].Value; errors[index] = leaves[index].Error; }
                double value = Pairwise(values); double error = Pairwise(errors);
                return Finite(value) && Finite(error) && error <= target ? Result(value, error, evaluations, panels, maximumDepth, LightSpaceOracleStopState.Accepted, Topology()) : Result(double.NaN, double.NaN, evaluations, panels, maximumDepth, LightSpaceOracleStopState.GlobalError, string.Empty);
            }

            private bool TryEvaluatePanel(IndependentOracleCanonicalPath path, double left, double right, out LightSpaceOraclePanel panel)
            {
                panel = default; double coarse = 0.0d; double fine = 0.0d; double fineWithCoarseInner = 0.0d; double coarseWithFineInner = 0.0d; var localMasks = new List<IndependentOracleRootMask>();
                for (int index = 0; index < OuterFine.Length; index++)
                {
                    double r = left + (right - left) * OuterFine[index].Position;
                    if (!TryNode(path, index, r, out LightSpaceOracleNodeEstimate node)) return false;
                    localMasks.Add(node.Mask); fine += (right - left) * OuterFine[index].Weight * node.Fine; fineWithCoarseInner += (right - left) * OuterFine[index].Weight * node.Coarse;
                    if (index % 2 == 0) { coarse += (right - left) * OuterCoarse[index / 2].Weight * node.Coarse; coarseWithFineInner += (right - left) * OuterCoarse[index / 2].Weight * node.Fine; }
                }
                double error = Math.Abs(fineWithCoarseInner - coarse) + Math.Abs(fine - fineWithCoarseInner);
                if (!Finite(fine) || !Finite(error) || error < 0.0d) return Fail(LightSpaceOracleStopState.NonFiniteSample);
                if (recorder != null) recorder.RecordPanelEstimate(path, left, right, coarse, fineWithCoarseInner, coarseWithFineInner, fine, error);
                panel = new LightSpaceOraclePanel(path, left, right, fine, error, MaskTopology(localMasks)); return true;
            }

            private bool TryNode(IndependentOracleCanonicalPath path, int nodeIndex, double r, out LightSpaceOracleNodeEstimate estimate)
            {
                estimate = default;
                if (recorder != null) recorder.SetNodeContext(path, nodeIndex, r, OuterFine[nodeIndex].Weight, nodeIndex % 2 == 0 ? OuterCoarse[nodeIndex / 2].Weight : 0.0d);
                int initialEvaluations = evaluations;
                if (r == 0.0d || r == 1.0d)
                {
                    estimate = new LightSpaceOracleNodeEstimate(0.0d, 0.0d, 0.0d, IndependentOracleRootMask.None, LightSpaceOracleStopState.Accepted);
                    if (recorder != null) recorder.RecordNode(r, estimate.Mask, estimate.Coarse, estimate.Fine, estimate.Error, evaluations - initialEvaluations);
                    return true;
                }
                if (!TryBoundaries(r, out double[] boundaries, out IndependentOracleRootMask mask))
                {
                    if (recorder != null) recorder.RecordNode(r, mask, 0.0d, 0.0d, 0.0d, evaluations - initialEvaluations);
                    return false;
                }
                double coarse = 0.0d; double fine = 0.0d;
                for (int interval = 0; interval + 1 < boundaries.Length; interval++)
                {
                    double left = boundaries[interval]; double width = boundaries[interval + 1] - left;
                    if (recorder != null) recorder.BeginInterval(interval, left, left + width, evaluations);
                    bool complete = TryAdaptiveInner(r, left, width, out double currentCoarse, out double currentFine);
                    coarse += currentCoarse; fine += currentFine;
                    if (recorder != null) { if (complete) recorder.CompleteInterval(currentCoarse, currentFine, evaluations); else recorder.RecordPartialInterval(currentCoarse, currentFine, evaluations); }
                    if (!complete)
                    {
                        if (recorder != null) recorder.RecordNode(r, mask, coarse, fine, Math.Abs(fine - coarse), evaluations - initialEvaluations);
                        return false;
                    }
                }
                double error = Math.Abs(fine - coarse);
                if (!Finite(coarse) || !Finite(fine) || !Finite(error)) return Fail(LightSpaceOracleStopState.NonFiniteSample, "node-aggregation");
                estimate = new LightSpaceOracleNodeEstimate(coarse, fine, error, mask, LightSpaceOracleStopState.Accepted);
                if (recorder != null) recorder.RecordNode(r, mask, coarse, fine, error, evaluations - initialEvaluations);
                return true;
            }

            /// <summary>Evaluates one atomic theta interval through 256 fixed endpoint-free nested subpanels.</summary>
            private bool TryAdaptiveInner(double r, double left, double width, out double coarse, out double fine)
            {
                coarse = 0.0d; fine = 0.0d; double panelWidth = width / 256.0d;
                for (int panel = 0; panel < 256; panel++)
                {
                    double panelLeft = left + panel * panelWidth;
                    bool coarseComplete = TryInner(r, panelLeft, panelWidth, InnerCoarse, panel, "fejer-17", out double currentCoarse);
                    coarse += currentCoarse; if (!coarseComplete) return false;
                    bool fineComplete = TryInner(r, panelLeft, panelWidth, InnerFine, panel, "fejer-33", out double currentFine);
                    fine += currentFine; if (!fineComplete) return false;
                }
                return Finite(coarse) && Finite(fine) || Fail(LightSpaceOracleStopState.NonFiniteSample);
            }

            private bool TryInner(double r, double left, double width, LightSpaceOracleRule[] rule, int currentSubpanel, string ruleName, out double result)
            {
                result = 0.0d;
                for (int index = 0; index < rule.Length; index++)
                {
                    if (recorder != null) recorder.SetScalarContext(currentSubpanel, ruleName, index);
                    if (evaluations >= MaximumEvaluations) return Fail(LightSpaceOracleStopState.EvaluationCap, "scalar-reservation");
                    evaluations++; double value = Scalar(r, left + width * rule[index].Position);
                    if (!Finite(value)) return Fail(LightSpaceOracleStopState.NonFiniteSample, "scalar-sample");
                    result += width * rule[index].Weight * value;
                }
                return Finite(result) || Fail(LightSpaceOracleStopState.NonFiniteSample, "inner-aggregation");
            }

            private double Scalar(double r, double theta)
            {
                double p = input.P; double v = input.NdotV; double sine = Math.Sin(Math.PI * r * 0.5d); double u = sine * sine;
                double a = p * p; double m = a * a; double q = 2.0d * (1.0d + u * v + Math.Sqrt(Math.Max(0.0d, 1.0d - u * u)) * Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)) * Math.Cos(theta));
                double h2 = (u + v) * (u + v) / Math.Max(q, GuardFloor); double denominator = h2 * (m - 1.0d) + 1.0d;
                double distribution = m / Math.Max(Math.PI * denominator * denominator, GuardFloor); double epsilon = input.Branch == IndependentOracleBranch.Normal ? NormalEpsilon : SwitchEpsilon;
                double visibility = 0.5d / (u * (v * (1.0d - a) + a) + v * (u * (1.0d - a) + a) + epsilon);
                return 2.0d * distribution * visibility * u * Math.PI * Math.Sin(Math.PI * r) * 0.5d;
            }

            private bool TryBoundaries(double r, out double[] boundaries, out IndependentOracleRootMask mask)
            {
                boundaries = Array.Empty<double>(); mask = IndependentOracleRootMask.None;
                double sine = Math.Sin(Math.PI * r * 0.5d); double u = sine * sine; double v = input.NdotV;
                double z = Math.Sqrt(Math.Max(0.0d, 1.0d - u * u)) * Math.Sqrt(Math.Max(0.0d, 1.0d - v * v));
                LightSpaceOracleRoot guard = Root(LightSpaceOracleRootKind.Guard, GuardFloor, u, v, z);
                double p4 = input.P * input.P * input.P * input.P; LightSpaceOracleRoot distribution = input.P < 1.0d ? DistributionRoot(u, v, z, p4) : new LightSpaceOracleRoot(LightSpaceOracleRootKind.Distribution, 0.0d, false, true);
                if (!guard.Valid || !distribution.Valid) return Fail(LightSpaceOracleStopState.RootTopologyFailure, "root-topology");
                var roots = new List<LightSpaceOracleRoot>(); if (guard.Present) roots.Add(guard); if (distribution.Present) roots.Add(distribution);
                roots.Sort((left, right) => left.Theta.CompareTo(right.Theta));
                if (roots.Count == 2 && NearlyEqual(roots[0].Theta, roots[1].Theta, 32)) { roots.Clear(); roots.Add(guard); }
                if (roots.Count == 2 && roots[0].Theta >= roots[1].Theta) return Fail(LightSpaceOracleStopState.RootTopologyFailure, "root-ordering");
                boundaries = roots.Count == 0 ? new[] { 0.0d, Math.PI } : roots.Count == 1 ? new[] { 0.0d, roots[0].Theta, Math.PI } : new[] { 0.0d, roots[0].Theta, roots[1].Theta, Math.PI };
                mask = Mask(roots); return true;
            }

            private LightSpaceOracleRoot DistributionRoot(double u, double v, double z, double p4)
            {
                double h2 = (1.0d - Math.Sqrt(GuardFloor / Math.PI)) / (1.0d - p4); double target = (u + v) * (u + v) / h2;
                if (target >= GuardFloor) return Root(LightSpaceOracleRootKind.Distribution, target, u, v, z);
                if (recorder != null) recorder.RecordRoot("distribution", target, double.NaN, double.NaN, double.NaN, false, true);
                return new LightSpaceOracleRoot(LightSpaceOracleRootKind.Distribution, 0.0d, false, true);
            }

            private LightSpaceOracleRoot Root(LightSpaceOracleRootKind kind, double target, double u, double v, double z)
            {
                if (!Finite(target) || z <= 0.0d)
                {
                    if (recorder != null) recorder.RecordRoot(Name(kind), target, double.NaN, double.NaN, double.NaN, false, Finite(target));
                    return new LightSpaceOracleRoot(kind, 0.0d, false, Finite(target));
                }
                double cosine = (target * 0.5d - 1.0d - u * v) / z;
                if (!Finite(cosine) || cosine <= -1.0d || cosine >= 1.0d)
                {
                    if (recorder != null) recorder.RecordRoot(Name(kind), target, cosine, double.NaN, double.NaN, false, Finite(cosine));
                    return new LightSpaceOracleRoot(kind, 0.0d, false, Finite(cosine));
                }
                double reconstructed = 2.0d * ((1.0d - z) + u * v + z * (1.0d + cosine));
                if (!WithinUlps(reconstructed, target, 128)) cosine += (target - reconstructed) / (2.0d * z);
                if (cosine <= -1.0d || cosine >= 1.0d)
                {
                    if (recorder != null) recorder.RecordRoot(Name(kind), target, cosine, double.NaN, reconstructed, false, true);
                    return new LightSpaceOracleRoot(kind, 0.0d, false, true);
                }
                double theta = Math.Acos(cosine); reconstructed = 2.0d * ((1.0d - z) + u * v + z * (1.0d + cosine));
                bool present = theta > 0.0d && theta < Math.PI;
                if (recorder != null) recorder.RecordRoot(Name(kind), target, cosine, theta, reconstructed, present, WithinUlps(reconstructed, target, 128));
                return new LightSpaceOracleRoot(kind, theta, present, true);
            }

            private bool Fail(LightSpaceOracleStopState state, string phase = "terminal")
            {
                if (stop == LightSpaceOracleStopState.Accepted)
                {
                    stop = state;
                    if (recorder != null) recorder.RecordFailure(state, evaluations, phase);
                }
                return false;
            }
            private static IndependentOracleCanonicalPath Child(IndependentOracleCanonicalPath parent, bool right) => new IndependentOracleCanonicalPath(parent.Depth + 1, (parent.BinaryPath << 1) | (right ? 1UL : 0UL));
            private static int ComparePanels(LightSpaceOraclePanel left, LightSpaceOraclePanel right) { int error = right.Error.CompareTo(left.Error); return error != 0 ? error : left.Path.Depth != right.Path.Depth ? left.Path.Depth.CompareTo(right.Path.Depth) : left.Path.CompareSpatial(right.Path); }
            private double TotalError() { var errors = new double[leaves.Count]; for (int index = 0; index < leaves.Count; index++) errors[index] = leaves[index].Error; return Pairwise(errors); }
            private void RecordMasks(string topology) { if (topology.Contains("guard-distribution")) masks.Add(IndependentOracleRootMask.GuardThenDistribution); else if (topology.Contains("distribution-guard")) masks.Add(IndependentOracleRootMask.DistributionThenGuard); else if (topology.Contains("guard")) masks.Add(IndependentOracleRootMask.Guard); else if (topology.Contains("distribution")) masks.Add(IndependentOracleRootMask.Distribution); else masks.Add(IndependentOracleRootMask.None); }
            private string Topology() => LightSpaceOracleTopologyContract.BuildRootMaskTopologySignature(masks);
            private static string MaskTopology(List<IndependentOracleRootMask> localMasks) => LightSpaceOracleTopologyContract.BuildRootMaskTopologySignature(localMasks);
            private static IndependentOracleRootMask Mask(List<LightSpaceOracleRoot> roots) => roots.Count == 0 ? IndependentOracleRootMask.None : roots.Count == 1 ? roots[0].Kind == LightSpaceOracleRootKind.Guard ? IndependentOracleRootMask.Guard : IndependentOracleRootMask.Distribution : roots[0].Kind == LightSpaceOracleRootKind.Guard ? IndependentOracleRootMask.GuardThenDistribution : IndependentOracleRootMask.DistributionThenGuard;
            private static double Pairwise(double[] values) { if (values.Length == 0) return 0.0d; for (int count = values.Length; count > 1; count = (count + 1) / 2) { int pairs = count / 2; for (int index = 0; index < pairs; index++) values[index] = values[index * 2] + values[index * 2 + 1]; if (count % 2 != 0) values[pairs] = values[count - 1]; } return values[0]; }
            private static string Name(LightSpaceOracleRootKind kind) => kind == LightSpaceOracleRootKind.Guard ? "guard" : "distribution";
        }

        /// <summary>Gets one nonzero binary64 spacing for an independent ULP residual check.</summary>
        private static double Ulp(double value)
        {
            if (!Finite(value)) return double.NaN; long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
            if (bits == 0L) return BitConverter.Int64BitsToDouble(1L);
            return BitConverter.Int64BitsToDouble(bits + 1L) - BitConverter.Int64BitsToDouble(bits);
        }

        /// <summary>Compares finite nonnegative values by their IEEE 754 binary64 ULP distance.</summary>
        private static bool WithinUlps(double left, double right, int limit)
        {
            if (!Finite(left) || !Finite(right) || left < 0.0d || right < 0.0d) return false;
            ulong leftBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(left)); ulong rightBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(right));
            return leftBits >= rightBits ? leftBits - rightBits <= (ulong)limit : rightBits - leftBits <= (ulong)limit;
        }

        /// <summary>Compares finite theta values with the frozen semantic tie allowance.</summary>
        private static bool NearlyEqual(double left, double right, int ulps) => Finite(left) && Finite(right) && Math.Abs(left - right) <= ulps * Math.Max(Ulp(left), Ulp(right));
    }
}
