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

// Defines the unavailable independent light-space candidate entry point without fabricating a numerical result.

using System;
using System.Collections.Generic;

namespace PureBase.Tests.Daily
{
    /// <summary>Identifies one outer leaf by depth and root-to-leaf binary decisions in spatial order.</summary>
    internal readonly struct IndependentOracleCanonicalPath
    {
        /// <summary>Initializes one canonical outer-leaf identity.</summary>
        internal IndependentOracleCanonicalPath(int depth, ulong binaryPath) { Depth = depth; BinaryPath = binaryPath; }
        /// <summary>Gets the number of root-to-leaf decisions.</summary>
        internal int Depth { get; }
        /// <summary>Gets the path bits, with the root decision stored most-significantly within the used width.</summary>
        internal ulong BinaryPath { get; }
        /// <summary>Gets whether the path width and unused high bits form a valid finite identity.</summary>
        internal bool IsValid => Depth >= 0 && Depth <= 62 && (Depth == 0 ? BinaryPath == 0UL : BinaryPath < (1UL << Depth));

        /// <summary>Compares paths by their root-to-leaf spatial order across different depths.</summary>
        internal int CompareSpatial(IndependentOracleCanonicalPath other)
        {
            int sharedDepth = Math.Min(Depth, other.Depth);
            for (int index = 0; index < sharedDepth; index++)
            {
                ulong leftBit = (BinaryPath >> (Depth - index - 1)) & 1UL; ulong rightBit = (other.BinaryPath >> (other.Depth - index - 1)) & 1UL;
                if (leftBit != rightBit) return leftBit < rightBit ? -1 : 1;
            }
            return Depth.CompareTo(other.Depth);
        }
    }

    /// <summary>Classifies one observed candidate theta-root topology for coverage only.</summary>
    internal enum IndependentOracleRootMask
    {
        /// <summary>Records a node with no interior root.</summary>
        None,
        /// <summary>Records a node with only the safe-normalization guard root.</summary>
        Guard,
        /// <summary>Records a node with only the GGX distribution root.</summary>
        Distribution,
        /// <summary>Records both roots in guard then distribution theta order.</summary>
        GuardThenDistribution,
        /// <summary>Records both roots in distribution then guard theta order.</summary>
        DistributionThenGuard
    }

    /// <summary>Stores one committed outer leaf interval and its canonical binary identity.</summary>
    internal readonly struct LightSpaceOracleCommittedLeaf
    {
        /// <summary>Initializes one immutable committed outer leaf.</summary>
        internal LightSpaceOracleCommittedLeaf(IndependentOracleCanonicalPath path, double left, double right) { Path = path; Left = left; Right = right; }
        /// <summary>Gets the root-to-leaf binary identity.</summary>
        internal IndependentOracleCanonicalPath Path { get; }
        /// <summary>Gets the inclusive left radial boundary.</summary>
        internal double Left { get; }
        /// <summary>Gets the inclusive right radial boundary shared only with an adjacent leaf.</summary>
        internal double Right { get; }
    }

    /// <summary>Validates test-only committed outer-leaf topology without performing numerical integration.</summary>
    internal static class LightSpaceOracleTopologyContract
    {
        /// <summary>Creates the requested child identity while retaining root-to-leaf bit ordering.</summary>
        internal static IndependentOracleCanonicalPath ChildPath(IndependentOracleCanonicalPath parent, bool rightChild) => new IndependentOracleCanonicalPath(parent.Depth + 1, (parent.BinaryPath << 1) | (rightChild ? 1UL : 0UL));

        /// <summary>Requires a complete nonoverlapping root partition with no ancestor/descendant leaf overlap.</summary>
        internal static bool IsCompleteNonOverlappingPartition(IReadOnlyList<LightSpaceOracleCommittedLeaf> leaves)
        {
            if (leaves == null || leaves.Count == 0) return false;
            var ordered = new List<LightSpaceOracleCommittedLeaf>(leaves); ordered.Sort((left, right) => left.Left.CompareTo(right.Left));
            for (int index = 0; index < ordered.Count; index++)
            {
                LightSpaceOracleCommittedLeaf leaf = ordered[index];
                if (!leaf.Path.IsValid || !Finite(leaf.Left) || !Finite(leaf.Right) || leaf.Left < 0.0d || leaf.Right > 1.0d || leaf.Left >= leaf.Right) return false;
                if (index == 0 ? leaf.Left != 0.0d : leaf.Left != ordered[index - 1].Right) return false;
                for (int other = index + 1; other < ordered.Count; other++) if (PathsOverlap(leaf.Path, ordered[other].Path)) return false;
            }
            return ordered[ordered.Count - 1].Right == 1.0d;
        }

        /// <summary>Builds a stable coverage-only signature from the observed node-local root masks.</summary>
        internal static string BuildRootMaskTopologySignature(IReadOnlyList<IndependentOracleRootMask> masks)
        {
            if (masks == null || masks.Count == 0) return string.Empty;
            var observed = new List<IndependentOracleRootMask>(masks); observed.Sort();
            var names = new List<string>(); for (int index = 0; index < observed.Count; index++) if (index == 0 || observed[index] != observed[index - 1]) names.Add(RootMaskName(observed[index]));
            return string.Join("|", names);
        }

        /// <summary>Gets whether two path identities describe the same leaf or an ancestor/descendant pair.</summary>
        private static bool PathsOverlap(IndependentOracleCanonicalPath left, IndependentOracleCanonicalPath right)
        {
            if (left.Depth > right.Depth) { IndependentOracleCanonicalPath temporary = left; left = right; right = temporary; }
            return (right.BinaryPath >> (right.Depth - left.Depth)) == left.BinaryPath;
        }

        /// <summary>Maps a root-mask identity to stable coverage metadata.</summary>
        private static string RootMaskName(IndependentOracleRootMask mask)
        {
            switch (mask)
            {
                case IndependentOracleRootMask.None: return "none";
                case IndependentOracleRootMask.Guard: return "guard";
                case IndependentOracleRootMask.Distribution: return "distribution";
                case IndependentOracleRootMask.GuardThenDistribution: return "guard-distribution";
                default: return "distribution-guard";
            }
        }

        /// <summary>Gets whether a binary64 coordinate is finite.</summary>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Stores the deterministic key used by the candidate outer-leaf heap.</summary>
    internal readonly struct IndependentOracleLeafKey
    {
        /// <summary>Initializes one immutable outer-leaf scheduling key.</summary>
        internal IndependentOracleLeafKey(double error, IndependentOracleCanonicalPath path) { Error = error; Path = path; }
        /// <summary>Gets the unnormalized leaf error.</summary>
        internal double Error { get; }
        /// <summary>Gets the canonical spatial leaf identity.</summary>
        internal IndependentOracleCanonicalPath Path { get; }
    }

    /// <summary>Stores a raw candidate result without granting an unavailable numerical state.</summary>
    internal readonly struct LightSpaceOracleResult
    {
        /// <summary>Initializes every retained candidate result field.</summary>
        internal LightSpaceOracleResult(double value, double estimatedError, int evaluations, int panels, int maximumDepth, LightSpaceOracleStopState stopState, string topology) { Value = value; EstimatedError = estimatedError; Evaluations = evaluations; Panels = panels; MaximumDepth = maximumDepth; StopState = stopState; Topology = topology; }
        /// <summary>Gets the candidate integral estimate.</summary>
        internal double Value { get; }
        /// <summary>Gets the candidate estimated error.</summary>
        internal double EstimatedError { get; }
        /// <summary>Gets historical scalar-evaluation work.</summary>
        internal int Evaluations { get; }
        /// <summary>Gets historical outer-panel attempts.</summary>
        internal int Panels { get; }
        /// <summary>Gets the maximum successfully reserved depth.</summary>
        internal int MaximumDepth { get; }
        /// <summary>Gets the deterministic candidate terminal state.</summary>
        internal LightSpaceOracleStopState StopState { get; }
        /// <summary>Gets coverage-only topology metadata.</summary>
        internal string Topology { get; }
    }

    /// <summary>Classifies a test-only global aggregation outcome before numerical integration exists.</summary>
    internal enum LightSpaceOracleAggregationDisposition
    {
        /// <summary>Indicates that finite leaf errors meet the requested target.</summary>
        Accepted,
        /// <summary>Indicates that the deterministic highest-error refinable leaf must be split.</summary>
        RefinementRequired,
        /// <summary>Indicates that at least one aggregated leaf was nonfinite or invalid.</summary>
        NonFiniteLeaf,
        /// <summary>Indicates that finite unresolved error has no refinable leaf.</summary>
        GlobalError
    }

    /// <summary>Stores one finite candidate leaf for test-only global aggregation.</summary>
    internal readonly struct LightSpaceOracleAggregateLeaf
    {
        /// <summary>Initializes a leaf with its frozen binary-path identity and refinement eligibility.</summary>
        internal LightSpaceOracleAggregateLeaf(IndependentOracleCanonicalPath path, double value, double error, bool refinable) { Path = path; Value = value; Error = error; Refinable = refinable; }
        /// <summary>Gets the frozen depth-aware spatial ordering key.</summary>
        internal IndependentOracleCanonicalPath Path { get; }
        /// <summary>Gets the independently evaluated leaf value.</summary>
        internal double Value { get; }
        /// <summary>Gets the nonnegative leaf error.</summary>
        internal double Error { get; }
        /// <summary>Gets whether the heap leaf may be refined.</summary>
        internal bool Refinable { get; }
    }

    /// <summary>Stores separately reduced global totals and the deterministic next action.</summary>
    internal readonly struct LightSpaceOracleAggregationResult
    {
        /// <summary>Initializes one test-only aggregation outcome.</summary>
        internal LightSpaceOracleAggregationResult(double value, double error, LightSpaceOracleAggregationDisposition disposition, IndependentOracleCanonicalPath refinementPath) { Value = value; Error = error; Disposition = disposition; RefinementPath = refinementPath; }
        /// <summary>Gets the pairwise-reduced leaf value total.</summary>
        internal double Value { get; }
        /// <summary>Gets the separately pairwise-reduced leaf error total.</summary>
        internal double Error { get; }
        /// <summary>Gets whether the row accepts, refines, or terminates with a contract failure.</summary>
        internal LightSpaceOracleAggregationDisposition Disposition { get; }
        /// <summary>Gets the selected refinable leaf identity when refinement is required.</summary>
        internal IndependentOracleCanonicalPath RefinementPath { get; }
    }

    /// <summary>Freezes test-only leaf ordering, separate reductions, and global acceptance behavior.</summary>
    internal static class LightSpaceOracleAggregationContract
    {
        /// <summary>Separately reduces ordered finite leaf values and errors without performing integration.</summary>
        internal static LightSpaceOracleAggregationResult Aggregate(System.Collections.Generic.IReadOnlyList<LightSpaceOracleAggregateLeaf> leaves, double requestedTarget)
        {
            if (!Finite(requestedTarget) || requestedTarget < 0.0d || leaves.Count == 0) return GlobalError();
            var ordered = new List<LightSpaceOracleAggregateLeaf>(leaves); ordered.Sort((left, right) => left.Path.CompareSpatial(right.Path));
            var values = new double[ordered.Count]; var errors = new double[ordered.Count]; int selected = -1;
            for (int index = 0; index < ordered.Count; index++)
            {
                LightSpaceOracleAggregateLeaf leaf = ordered[index];
                if (!leaf.Path.IsValid || index > 0 && leaf.Path.CompareSpatial(ordered[index - 1].Path) == 0) return GlobalError();
                if (!Finite(leaf.Value) || !Finite(leaf.Error) || leaf.Error < 0.0d) return new LightSpaceOracleAggregationResult(double.NaN, double.NaN, LightSpaceOracleAggregationDisposition.NonFiniteLeaf, default);
                values[index] = leaf.Value; errors[index] = leaf.Error;
                if (leaf.Refinable && (selected < 0 || HigherPriority(leaf, ordered[selected]))) selected = index;
            }
            double value = Reduce(values); double error = Reduce(errors);
            if (!Finite(value) || !Finite(error)) return GlobalError();
            if (error <= requestedTarget) return new LightSpaceOracleAggregationResult(value, error, LightSpaceOracleAggregationDisposition.Accepted, default);
            return selected >= 0 ? new LightSpaceOracleAggregationResult(value, error, LightSpaceOracleAggregationDisposition.RefinementRequired, ordered[selected].Path) : GlobalError(value, error);
        }

        /// <summary>Gets whether the current leaf wins the deterministic error-descending, path-ascending heap order.</summary>
        private static bool HigherPriority(LightSpaceOracleAggregateLeaf current, LightSpaceOracleAggregateLeaf selected)
        {
            if (current.Error != selected.Error) return current.Error > selected.Error;
            int depth = current.Path.Depth.CompareTo(selected.Path.Depth); return depth != 0 ? depth < 0 : current.Path.CompareSpatial(selected.Path) < 0;
        }

        /// <summary>Reduces one ordered finite sequence using the frozen adjacent-pair tree.</summary>
        private static double Reduce(double[] values)
        {
            for (int count = values.Length; count > 1; count = (count + 1) / 2)
            {
                int pairs = count / 2; for (int index = 0; index < pairs; index++) values[index] = values[index * 2] + values[index * 2 + 1];
                if (count % 2 != 0) values[pairs] = values[count - 1];
            }
            return values[0];
        }

        /// <summary>Gets a finite invariant failure without an eligible leaf.</summary>
        private static LightSpaceOracleAggregationResult GlobalError() => GlobalError(double.NaN, double.NaN);

        /// <summary>Gets a finite or invalid global-error aggregation outcome.</summary>
        private static LightSpaceOracleAggregationResult GlobalError(double value, double error) => new LightSpaceOracleAggregationResult(value, error, LightSpaceOracleAggregationDisposition.GlobalError, default);

        /// <summary>Gets whether a binary64 component is finite.</summary>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Stores the first scalar-call failure observed by a scripted candidate transaction.</summary>
    internal readonly struct LightSpaceOracleScriptedFailure
    {
        /// <summary>Initializes immutable first-failure context.</summary>
        internal LightSpaceOracleScriptedFailure(LightSpaceOracleStopState stopState, string context, string path, int scalarCall) { StopState = stopState; Context = context; Path = path; ScalarCall = scalarCall; }
        /// <summary>Gets the terminal stop state.</summary>
        internal LightSpaceOracleStopState StopState { get; }
        /// <summary>Gets the root, child-0, or child-1 execution context.</summary>
        internal string Context { get; }
        /// <summary>Gets the binary path whose work first failed.</summary>
        internal string Path { get; }
        /// <summary>Gets the one-based scalar-call position, or zero for pre-work stops.</summary>
        internal int ScalarCall { get; }
    }

    /// <summary>Models only the candidate reservation and prepare/commit stop transaction for contract tests.</summary>
    internal sealed class LightSpaceOracleScriptedState
    {
        /// <summary>Stores the greatest depth that the scripted refinement may reserve.</summary>
        private readonly int maximumDepth;
        /// <summary>Stores the greatest number of outer panels that the scripted transaction may reserve.</summary>
        private readonly int maximumPanels;
        /// <summary>Stores the greatest number of scalar calls that the scripted transaction may reserve.</summary>
        private readonly int maximumEvaluations;
        /// <summary>Stores the fixed number of scalar calls performed by each scripted leaf.</summary>
        private readonly int scalarCallsPerLeaf;
        /// <summary>Tracks whether the root completed finite scalar work and remains the committed topology.</summary>
        private bool rootCommitted;
        /// <summary>Stores only committed leaves while child work is prepared outside the topology.</summary>
        private List<LightSpaceOracleCommittedLeaf> committedLeaves = new List<LightSpaceOracleCommittedLeaf>();
        /// <summary>Stores root masks observed by test-only scheduler probes.</summary>
        private readonly List<IndependentOracleRootMask> observedRootMasks = new List<IndependentOracleRootMask>();

        /// <summary>Initializes fixed resource limits and scalar calls for each scripted leaf.</summary>
        internal LightSpaceOracleScriptedState(int maximumDepth, int maximumPanels, int maximumEvaluations, int scalarCallsPerLeaf)
        {
            this.maximumDepth = maximumDepth; this.maximumPanels = maximumPanels; this.maximumEvaluations = maximumEvaluations; this.scalarCallsPerLeaf = scalarCallsPerLeaf;
            StopState = LightSpaceOracleStopState.Accepted; FirstFailure = new LightSpaceOracleScriptedFailure(LightSpaceOracleStopState.Accepted, string.Empty, string.Empty, 0);
        }

        /// <summary>Gets historical panel reservations.</summary>
        internal int Panels { get; private set; }
        /// <summary>Gets historical scalar-call reservations through the first failure.</summary>
        internal int Evaluations { get; private set; }
        /// <summary>Gets the greatest successfully reserved outer depth.</summary>
        internal int MaximumDepth { get; private set; }
        /// <summary>Gets the committed leaf topology without exposing a numerical result.</summary>
        internal string Topology => string.Join("|", committedLeaves.ConvertAll(leaf => leaf.Path.Depth == 0 ? "root" : leaf.Path.BinaryPath.ToString()));
        /// <summary>Gets the committed leaf intervals retained across prepare/commit refinement.</summary>
        internal IReadOnlyList<LightSpaceOracleCommittedLeaf> CommittedLeaves => committedLeaves.AsReadOnly();
        /// <summary>Gets coverage-only metadata built from observed root masks.</summary>
        internal string RootMaskTopologySignature => LightSpaceOracleTopologyContract.BuildRootMaskTopologySignature(observedRootMasks);
        /// <summary>Gets the terminal scripted state.</summary>
        internal LightSpaceOracleStopState StopState { get; private set; }
        /// <summary>Gets first-failure context, preserved after later work is suppressed.</summary>
        internal LightSpaceOracleScriptedFailure FirstFailure { get; private set; }
        /// <summary>Gets whether any acceptable partial numerical result escaped a hard stop.</summary>
        internal bool HasAcceptablePartialResult => false;

        /// <summary>Reserves the root before scalar work and commits it only after a finite completion.</summary>
        internal void EvaluateRoot(int nonFiniteScalarCall)
        {
            if (Panels >= maximumPanels) { Fail(LightSpaceOracleStopState.PanelCap, "root", string.Empty, 0); return; }
            Panels++; if (Evaluate("root", string.Empty, nonFiniteScalarCall)) { rootCommitted = true; committedLeaves.Add(new LightSpaceOracleCommittedLeaf(new IndependentOracleCanonicalPath(0, 0UL), 0.0d, 1.0d)); }
        }

        /// <summary>Reserves both children before work and atomically retains the root on either child failure.</summary>
        internal void RefineRoot(int childZeroNonFiniteScalarCall, int childOneNonFiniteScalarCall)
        {
            if (!rootCommitted || StopState != LightSpaceOracleStopState.Accepted) return;
            if (MaximumDepth >= maximumDepth) { Fail(LightSpaceOracleStopState.DepthCap, "root", string.Empty, 0); return; }
            if (Panels > maximumPanels - 2) { Fail(LightSpaceOracleStopState.PanelCap, "root", string.Empty, 0); return; }
            LightSpaceOracleCommittedLeaf parent = committedLeaves[0]; double midpoint = (parent.Left + parent.Right) * 0.5d;
            LightSpaceOracleCommittedLeaf childZero = new LightSpaceOracleCommittedLeaf(LightSpaceOracleTopologyContract.ChildPath(parent.Path, false), parent.Left, midpoint);
            LightSpaceOracleCommittedLeaf childOne = new LightSpaceOracleCommittedLeaf(LightSpaceOracleTopologyContract.ChildPath(parent.Path, true), midpoint, parent.Right);
            Panels += 2; MaximumDepth++;
            if (!Evaluate("child-0", "0", childZeroNonFiniteScalarCall)) return;
            if (!Evaluate("child-1", "1", childOneNonFiniteScalarCall)) return;
            TopologyAfterSuccessfulRefinement(childZero, childOne);
        }

        /// <summary>Records observed root-mask coverage without altering scheduling or numerical acceptance.</summary>
        internal void ObserveRootMasks(params IndependentOracleRootMask[] masks) { observedRootMasks.AddRange(masks); }

        /// <summary>Reserves each scalar call immediately before its scripted finite or nonfinite evaluation.</summary>
        private bool Evaluate(string context, string path, int nonFiniteScalarCall)
        {
            for (int scalarCall = 1; scalarCall <= scalarCallsPerLeaf; scalarCall++)
            {
                if (Evaluations >= maximumEvaluations) { Fail(LightSpaceOracleStopState.EvaluationCap, context, path, scalarCall); return false; }
                Evaluations++; if (scalarCall == nonFiniteScalarCall) { Fail(LightSpaceOracleStopState.NonFiniteSample, context, path, scalarCall); return false; }
            }
            return true;
        }

        /// <summary>Atomically replaces the retained parent with both fully prepared children.</summary>
        private void TopologyAfterSuccessfulRefinement(LightSpaceOracleCommittedLeaf childZero, LightSpaceOracleCommittedLeaf childOne)
        {
            committedLeaves = new List<LightSpaceOracleCommittedLeaf> { childZero, childOne }; rootCommitted = false;
        }

        /// <summary>Records only the first terminal failure.</summary>
        private void Fail(LightSpaceOracleStopState stopState, string context, string path, int scalarCall)
        {
            if (StopState != LightSpaceOracleStopState.Accepted) return;
            StopState = stopState; FirstFailure = new LightSpaceOracleScriptedFailure(stopState, context, path, scalarCall);
        }
    }

    /// <summary>Provides the unavailable independent light-space candidate integration boundary.</summary>
    internal static class LightSpaceOracle
    {
        /// <summary>Throws because no independent candidate implementation is available to construct a numerical result.</summary>
        internal static LightSpaceOracleResult Integrate(IndependentOracleInput input, double requestedTarget)
        {
            throw new NotImplementedException("The independent light-space candidate is not implemented.");
        }
    }
}
