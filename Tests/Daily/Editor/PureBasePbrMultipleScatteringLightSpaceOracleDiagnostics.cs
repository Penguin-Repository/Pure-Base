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

// Defines bounded, non-authoritative observation records for the retained light-space quadrature prototype.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PureBase.Tests.Daily
{
    /// <summary>Classifies a retained outer-panel lifecycle observation.</summary>
    internal enum LightSpaceOracleDiagnosticLeafState { Prepared, Committed, Replaced, Discarded }

    /// <summary>Stores one bounded outer-panel preparation, commitment, replacement, or discard observation.</summary>
    internal readonly struct LightSpaceOracleDiagnosticLeaf
    {
        internal LightSpaceOracleDiagnosticLeaf(IndependentOracleCanonicalPath path, double left, double right, LightSpaceOracleDiagnosticLeafState state, int sequence) { Path = path; Left = left; Right = right; State = state; Sequence = sequence; }
        internal IndependentOracleCanonicalPath Path { get; }
        internal double Left { get; }
        internal double Right { get; }
        internal LightSpaceOracleDiagnosticLeafState State { get; }
        internal int Sequence { get; }
    }

    /// <summary>Stores one completed outer-panel estimate and its observed and contract replay components.</summary>
    internal readonly struct LightSpaceOracleDiagnosticPanel
    {
        internal LightSpaceOracleDiagnosticPanel(IndependentOracleCanonicalPath path, double left, double right, double coarse, double fineWithCoarseInner, double coarseWithFineInner, double fine, double error) { Path = path; Left = left; Right = right; Coarse = coarse; FineWithCoarseInner = fineWithCoarseInner; CoarseWithFineInner = coarseWithFineInner; Fine = fine; RadialError = Math.Abs(fineWithCoarseInner - coarse); AngularError = Math.Abs(fine - fineWithCoarseInner); Error = error; }
        internal IndependentOracleCanonicalPath Path { get; }
        internal double Left { get; }
        internal double Right { get; }
        internal double Coarse { get; }
        internal double FineWithCoarseInner { get; }
        internal double CoarseWithFineInner { get; }
        internal double Fine { get; }
        internal double RadialError { get; }
        internal double AngularError { get; }
        internal double Error { get; }
    }

    /// <summary>Stores one prototype root attempt and its retained reconstruction evidence.</summary>
    internal readonly struct LightSpaceOracleDiagnosticRoot
    {
        internal LightSpaceOracleDiagnosticRoot(string kind, double target, double cosine, double theta, double reconstructed, bool present, bool finalValid) { Kind = kind; Target = target; Cosine = cosine; Theta = theta; Reconstructed = reconstructed; Present = present; FinalValid = finalValid; }
        internal string Kind { get; }
        internal double Target { get; }
        internal double Cosine { get; }
        internal double Theta { get; }
        internal double Reconstructed { get; }
        internal bool Present { get; }
        internal bool FinalValid { get; }
    }

    /// <summary>Stores one radial rule node and its root, inner-estimate, and scalar-work evidence.</summary>
    internal readonly struct LightSpaceOracleDiagnosticNode
    {
        internal LightSpaceOracleDiagnosticNode(IndependentOracleCanonicalPath path, int ruleIndex, double radialCoordinate, double fineOuterWeight, double coarseOuterWeight, IndependentOracleRootMask mask, IReadOnlyList<LightSpaceOracleDiagnosticRoot> roots, double coarse, double fine, double angularError, int scalarCalls) { Path = path; RuleIndex = ruleIndex; RadialCoordinate = radialCoordinate; FineOuterWeight = fineOuterWeight; CoarseOuterWeight = coarseOuterWeight; Mask = mask; Roots = roots; Coarse = coarse; Fine = fine; AngularError = angularError; ScalarCalls = scalarCalls; }
        internal IndependentOracleCanonicalPath Path { get; }
        internal int RuleIndex { get; }
        internal double RadialCoordinate { get; }
        internal double FineOuterWeight { get; }
        internal double CoarseOuterWeight { get; }
        internal IndependentOracleRootMask Mask { get; }
        internal IReadOnlyList<LightSpaceOracleDiagnosticRoot> Roots { get; }
        internal double Coarse { get; }
        internal double Fine { get; }
        internal double AngularError { get; }
        internal int ScalarCalls { get; }
    }

    /// <summary>Stores aggregate work for one atomic theta interval without retaining scalar events.</summary>
    internal readonly struct LightSpaceOracleDiagnosticInterval
    {
        internal LightSpaceOracleDiagnosticInterval(IndependentOracleCanonicalPath path, int ruleIndex, int intervalIndex, double left, double right, int subpanels, double coarse, double fine, int scalarCalls, int firstEvaluation, int lastEvaluation, bool complete) { Path = path; RuleIndex = ruleIndex; IntervalIndex = intervalIndex; Left = left; Right = right; Subpanels = subpanels; Coarse = coarse; Fine = fine; ScalarCalls = scalarCalls; FirstEvaluation = firstEvaluation; LastEvaluation = lastEvaluation; Complete = complete; }
        internal IndependentOracleCanonicalPath Path { get; }
        internal int RuleIndex { get; }
        internal int IntervalIndex { get; }
        internal double Left { get; }
        internal double Right { get; }
        internal int Subpanels { get; }
        internal double Coarse { get; }
        internal double Fine { get; }
        internal int ScalarCalls { get; }
        internal int FirstEvaluation { get; }
        internal int LastEvaluation { get; }
        internal bool Complete { get; }
    }

    /// <summary>Stores the first observed terminal failure or an explicit accepted sentinel.</summary>
    internal readonly struct LightSpaceOracleDiagnosticFailure
    {
        internal LightSpaceOracleDiagnosticFailure(LightSpaceOracleStopState stopState, string phase, IndependentOracleCanonicalPath path, int radialRuleIndex, int intervalIndex, int subpanel, string rule, int ruleIndex, int nextScalarReservation, string committedTopology) { StopState = stopState; Phase = phase; Path = path; RadialRuleIndex = radialRuleIndex; IntervalIndex = intervalIndex; Subpanel = subpanel; Rule = rule; RuleIndex = ruleIndex; NextScalarReservation = nextScalarReservation; CommittedTopology = committedTopology; }
        internal LightSpaceOracleStopState StopState { get; }
        internal string Phase { get; }
        internal IndependentOracleCanonicalPath Path { get; }
        internal int RadialRuleIndex { get; }
        internal int IntervalIndex { get; }
        internal int Subpanel { get; }
        internal string Rule { get; }
        internal int RuleIndex { get; }
        internal int NextScalarReservation { get; }
        internal string CommittedTopology { get; }
    }

    /// <summary>Stores one deterministic contribution, error, or work concentration entry.</summary>
    internal readonly struct LightSpaceOracleDiagnosticConcentration
    {
        internal LightSpaceOracleDiagnosticConcentration(IndependentOracleCanonicalPath path, int ruleIndex, int intervalIndex, double value) { Path = path; RuleIndex = ruleIndex; IntervalIndex = intervalIndex; Value = value; }
        internal IndependentOracleCanonicalPath Path { get; }
        internal int RuleIndex { get; }
        internal int IntervalIndex { get; }
        internal double Value { get; }
    }

    /// <summary>Stores a bounded immutable reference-prototype execution trace.</summary>
    internal sealed class LightSpaceOracleDiagnosticTrace
    {
        internal LightSpaceOracleDiagnosticTrace(IndependentOracleInput input, double target, LightSpaceOracleResult result, IReadOnlyList<LightSpaceOracleDiagnosticLeaf> leaves, IReadOnlyList<LightSpaceOracleDiagnosticPanel> panels, IReadOnlyList<LightSpaceOracleCommittedLeaf> committedLeaves, IReadOnlyList<IndependentOracleCanonicalPath> selectionOrder, IReadOnlyList<LightSpaceOracleDiagnosticNode> nodes, IReadOnlyList<LightSpaceOracleDiagnosticInterval> intervals, LightSpaceOracleDiagnosticFailure firstFailure)
        { Input = input; PBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(input.P)); NdotVBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(input.NdotV)); Target = target; Result = result; Leaves = leaves; Panels = panels; CommittedLeaves = committedLeaves; SelectionOrder = selectionOrder; Nodes = nodes; Intervals = intervals; FirstFailure = firstFailure; ContributionConcentration = Concentration(intervals, 0); ErrorConcentration = Concentration(intervals, 1); WorkConcentration = Concentration(intervals, 2); Digest = CalculateDigest(); }
        internal IndependentOracleInput Input { get; }
        internal ulong PBits { get; }
        internal ulong NdotVBits { get; }
        internal double Target { get; }
        internal LightSpaceOracleResult Result { get; }
        internal IReadOnlyList<LightSpaceOracleDiagnosticLeaf> Leaves { get; }
        internal IReadOnlyList<LightSpaceOracleDiagnosticPanel> Panels { get; }
        internal IReadOnlyList<LightSpaceOracleCommittedLeaf> CommittedLeaves { get; }
        internal IReadOnlyList<IndependentOracleCanonicalPath> SelectionOrder { get; }
        internal IReadOnlyList<LightSpaceOracleDiagnosticNode> Nodes { get; }
        internal IReadOnlyList<LightSpaceOracleDiagnosticInterval> Intervals { get; }
        internal LightSpaceOracleDiagnosticFailure FirstFailure { get; }
        internal string Digest { get; }
        internal IReadOnlyList<LightSpaceOracleDiagnosticConcentration> ContributionConcentration { get; }
        internal IReadOnlyList<LightSpaceOracleDiagnosticConcentration> ErrorConcentration { get; }
        internal IReadOnlyList<LightSpaceOracleDiagnosticConcentration> WorkConcentration { get; }
        internal int IntervalScalarCalls { get { int total = 0; foreach (LightSpaceOracleDiagnosticInterval interval in Intervals) total += interval.ScalarCalls; return total; } }
        internal bool IsBounded => Result.Evaluations <= 4000000 && Result.Panels <= 262144 && Result.MaximumDepth <= 22 && Intervals.Count <= Result.Evaluations;

        /// <summary>Builds an empty trace for the disabled observer path without executing diagnostic allocation.</summary>
        internal static LightSpaceOracleDiagnosticTrace Disabled(IndependentOracleInput input, double target, LightSpaceOracleResult result) => new LightSpaceOracleDiagnosticTrace(input, target, result, Empty<LightSpaceOracleDiagnosticLeaf>(), Empty<LightSpaceOracleDiagnosticPanel>(), Empty<LightSpaceOracleCommittedLeaf>(), Empty<IndependentOracleCanonicalPath>(), Empty<LightSpaceOracleDiagnosticNode>(), Empty<LightSpaceOracleDiagnosticInterval>(), new LightSpaceOracleDiagnosticFailure(result.StopState, string.Empty, default, -1, -1, -1, string.Empty, -1, 0, string.Empty));

        /// <summary>Builds one stable sorted summary from interval aggregates without retaining scalar evaluations.</summary>
        private static IReadOnlyList<LightSpaceOracleDiagnosticConcentration> Concentration(IReadOnlyList<LightSpaceOracleDiagnosticInterval> intervals, int mode)
        {
            var values = new List<LightSpaceOracleDiagnosticConcentration>();
            foreach (LightSpaceOracleDiagnosticInterval interval in intervals) values.Add(new LightSpaceOracleDiagnosticConcentration(interval.Path, interval.RuleIndex, interval.IntervalIndex, mode == 0 ? Math.Abs(interval.Fine) : mode == 1 ? Math.Abs(interval.Fine - interval.Coarse) : interval.ScalarCalls));
            values.Sort((left, right) => right.Value != left.Value ? right.Value.CompareTo(left.Value) : left.Path.CompareSpatial(right.Path) != 0 ? left.Path.CompareSpatial(right.Path) : left.RuleIndex != right.RuleIndex ? left.RuleIndex.CompareTo(right.RuleIndex) : left.IntervalIndex.CompareTo(right.IntervalIndex));
            if (values.Count > 8) values.RemoveRange(8, values.Count - 8); return new ReadOnlyCollection<LightSpaceOracleDiagnosticConcentration>(values);
        }

        /// <summary>Hashes every retained primitive and derived concentration summary in stable collection order.</summary>
        private string CalculateDigest()
        {
            var digest = new LightSpaceOracleDigest(); digest.Add(Input.P); digest.Add(Input.NdotV); digest.Add((int)Input.Branch); digest.Add(Target); AddResult(digest, Result); AddFailure(digest, FirstFailure);
            AddLeaves(digest); AddCommittedLeaves(digest); AddSelections(digest); AddPanels(digest); AddNodes(digest); AddIntervals(digest); AddConcentrations(digest, ContributionConcentration); AddConcentrations(digest, ErrorConcentration); AddConcentrations(digest, WorkConcentration);
            return digest.Value;
        }

        /// <summary>Hashes one terminal result without omitting depth or topology.</summary>
        private static void AddResult(LightSpaceOracleDigest digest, LightSpaceOracleResult result) { digest.Add(result.Value); digest.Add(result.EstimatedError); digest.Add(result.Evaluations); digest.Add(result.Panels); digest.Add(result.MaximumDepth); digest.Add((int)result.StopState); digest.Add(result.Topology); }
        /// <summary>Hashes every retained first-failure field.</summary>
        private static void AddFailure(LightSpaceOracleDigest digest, LightSpaceOracleDiagnosticFailure failure) { digest.Add((int)failure.StopState); digest.Add(failure.Phase); AddPath(digest, failure.Path); digest.Add(failure.RadialRuleIndex); digest.Add(failure.IntervalIndex); digest.Add(failure.Subpanel); digest.Add(failure.Rule); digest.Add(failure.RuleIndex); digest.Add(failure.NextScalarReservation); digest.Add(failure.CommittedTopology); }
        /// <summary>Hashes one canonical path identity.</summary>
        private static void AddPath(LightSpaceOracleDigest digest, IndependentOracleCanonicalPath path) { digest.Add(path.Depth); digest.Add(path.BinaryPath); }
        /// <summary>Hashes every retained outer lifecycle observation.</summary>
        private void AddLeaves(LightSpaceOracleDigest digest) { digest.Add(Leaves.Count); foreach (LightSpaceOracleDiagnosticLeaf leaf in Leaves) { AddPath(digest, leaf.Path); digest.Add(leaf.Left); digest.Add(leaf.Right); digest.Add((int)leaf.State); digest.Add(leaf.Sequence); } }
        /// <summary>Hashes every retained committed outer-leaf boundary.</summary>
        private void AddCommittedLeaves(LightSpaceOracleDigest digest) { digest.Add(CommittedLeaves.Count); foreach (LightSpaceOracleCommittedLeaf leaf in CommittedLeaves) { AddPath(digest, leaf.Path); digest.Add(leaf.Left); digest.Add(leaf.Right); } }
        /// <summary>Hashes the full retained outer selection order.</summary>
        private void AddSelections(LightSpaceOracleDigest digest) { digest.Add(SelectionOrder.Count); foreach (IndependentOracleCanonicalPath selection in SelectionOrder) AddPath(digest, selection); }
        /// <summary>Hashes every completed outer-panel estimate.</summary>
        private void AddPanels(LightSpaceOracleDigest digest) { digest.Add(Panels.Count); foreach (LightSpaceOracleDiagnosticPanel panel in Panels) { AddPath(digest, panel.Path); digest.Add(panel.Left); digest.Add(panel.Right); digest.Add(panel.Coarse); digest.Add(panel.FineWithCoarseInner); digest.Add(panel.CoarseWithFineInner); digest.Add(panel.Fine); digest.Add(panel.RadialError); digest.Add(panel.AngularError); digest.Add(panel.Error); } }
        /// <summary>Hashes every radial node and retained root primitive.</summary>
        private void AddNodes(LightSpaceOracleDigest digest) { digest.Add(Nodes.Count); foreach (LightSpaceOracleDiagnosticNode node in Nodes) { AddPath(digest, node.Path); digest.Add(node.RuleIndex); digest.Add(node.RadialCoordinate); digest.Add(node.FineOuterWeight); digest.Add(node.CoarseOuterWeight); digest.Add((int)node.Mask); digest.Add(node.Roots.Count); foreach (LightSpaceOracleDiagnosticRoot root in node.Roots) { digest.Add(root.Kind); digest.Add(root.Target); digest.Add(root.Cosine); digest.Add(root.Theta); digest.Add(root.Reconstructed); digest.Add(root.Present ? 1 : 0); digest.Add(root.FinalValid ? 1 : 0); } digest.Add(node.Coarse); digest.Add(node.Fine); digest.Add(node.AngularError); digest.Add(node.ScalarCalls); } }
        /// <summary>Hashes every complete or partial theta-interval aggregate and evaluation offset.</summary>
        private void AddIntervals(LightSpaceOracleDigest digest) { digest.Add(Intervals.Count); foreach (LightSpaceOracleDiagnosticInterval interval in Intervals) { AddPath(digest, interval.Path); digest.Add(interval.RuleIndex); digest.Add(interval.IntervalIndex); digest.Add(interval.Left); digest.Add(interval.Right); digest.Add(interval.Subpanels); digest.Add(interval.Coarse); digest.Add(interval.Fine); digest.Add(interval.ScalarCalls); digest.Add(interval.FirstEvaluation); digest.Add(interval.LastEvaluation); digest.Add(interval.Complete ? 1 : 0); } }
        /// <summary>Hashes one concentration summary including its stable selection identity.</summary>
        private static void AddConcentrations(LightSpaceOracleDigest digest, IReadOnlyList<LightSpaceOracleDiagnosticConcentration> values) { digest.Add(values.Count); foreach (LightSpaceOracleDiagnosticConcentration value in values) { AddPath(digest, value.Path); digest.Add(value.RuleIndex); digest.Add(value.IntervalIndex); digest.Add(value.Value); } }

        /// <summary>Gets an immutable empty sequence with no scalar-event representation.</summary>
        private static IReadOnlyList<T> Empty<T>() => Array.AsReadOnly(Array.Empty<T>());
    }

    /// <summary>Records bounded hierarchical observations around the retained prototype without entering scalar arithmetic.</summary>
    internal sealed class LightSpaceOracleDiagnosticRecorder
    {
        private readonly IndependentOracleInput input;
        private readonly double target;
        private readonly List<LightSpaceOracleDiagnosticLeaf> leaves = new List<LightSpaceOracleDiagnosticLeaf>();
        private readonly List<LightSpaceOracleDiagnosticPanel> panels = new List<LightSpaceOracleDiagnosticPanel>();
        private readonly List<LightSpaceOracleCommittedLeaf> committed = new List<LightSpaceOracleCommittedLeaf>();
        private readonly List<IndependentOracleCanonicalPath> selections = new List<IndependentOracleCanonicalPath>();
        private readonly List<LightSpaceOracleDiagnosticNode> nodes = new List<LightSpaceOracleDiagnosticNode>();
        private readonly List<LightSpaceOracleDiagnosticInterval> intervals = new List<LightSpaceOracleDiagnosticInterval>();
        private readonly List<LightSpaceOracleDiagnosticRoot> roots = new List<LightSpaceOracleDiagnosticRoot>();
        private IndependentOracleCanonicalPath path; private int radialRuleIndex; private double fineOuterWeight; private double coarseOuterWeight; private int intervalIndex; private int subpanel; private string rule = string.Empty; private int ruleIndex; private int intervalRecord = -1; private int intervalStart; private LightSpaceOracleDiagnosticFailure failure;

        internal LightSpaceOracleDiagnosticRecorder(IndependentOracleInput input, double target) { this.input = input; this.target = target; failure = AcceptedFailure(); }
        internal void SetPanelContext(IndependentOracleCanonicalPath panelPath) { path = panelPath; radialRuleIndex = -1; intervalIndex = -1; }
        internal void RecordPrepared(IndependentOracleCanonicalPath panelPath, double left, double right) { path = panelPath; leaves.Add(new LightSpaceOracleDiagnosticLeaf(panelPath, left, right, LightSpaceOracleDiagnosticLeafState.Prepared, leaves.Count)); }
        internal void RecordCommit(IndependentOracleCanonicalPath panelPath, double left, double right) { committed.Add(new LightSpaceOracleCommittedLeaf(panelPath, left, right)); leaves.Add(new LightSpaceOracleDiagnosticLeaf(panelPath, left, right, LightSpaceOracleDiagnosticLeafState.Committed, leaves.Count)); }
        internal void RecordChildrenCommit(IndependentOracleCanonicalPath parent, LightSpaceOracleCommittedLeaf left, LightSpaceOracleCommittedLeaf right) { committed.RemoveAll(item => item.Path.Depth == parent.Depth && item.Path.BinaryPath == parent.BinaryPath); leaves.Add(new LightSpaceOracleDiagnosticLeaf(parent, 0.0d, 0.0d, LightSpaceOracleDiagnosticLeafState.Replaced, leaves.Count)); committed.Add(left); committed.Add(right); RecordCommitState(left); RecordCommitState(right); }
        internal void RecordSelected(IndependentOracleCanonicalPath panelPath) { path = panelPath; selections.Add(panelPath); }
        internal void SetNodeContext(IndependentOracleCanonicalPath panelPath, int index, double radialCoordinate, double fineOuterWeight, double coarseOuterWeight) { path = panelPath; radialRuleIndex = index; this.fineOuterWeight = fineOuterWeight; this.coarseOuterWeight = coarseOuterWeight; roots.Clear(); }
        internal void RecordRoot(string kind, double targetValue, double cosine, double theta, double reconstructed, bool present, bool finalValid) { roots.Add(new LightSpaceOracleDiagnosticRoot(kind, targetValue, cosine, theta, reconstructed, present, finalValid)); }
        internal void RecordNode(double radialCoordinate, IndependentOracleRootMask mask, double coarse, double fine, double error, int scalarCalls) { nodes.Add(new LightSpaceOracleDiagnosticNode(path, radialRuleIndex, radialCoordinate, fineOuterWeight, coarseOuterWeight, mask, new ReadOnlyCollection<LightSpaceOracleDiagnosticRoot>(new List<LightSpaceOracleDiagnosticRoot>(roots)), coarse, fine, error, scalarCalls)); }
        internal void RecordPanelEstimate(IndependentOracleCanonicalPath panelPath, double left, double right, double coarse, double fineWithCoarseInner, double coarseWithFineInner, double fine, double error) { panels.Add(new LightSpaceOracleDiagnosticPanel(panelPath, left, right, coarse, fineWithCoarseInner, coarseWithFineInner, fine, error)); }
        internal void RecordDiscard(IndependentOracleCanonicalPath panelPath, double left, double right) { leaves.Add(new LightSpaceOracleDiagnosticLeaf(panelPath, left, right, LightSpaceOracleDiagnosticLeafState.Discarded, leaves.Count)); }
        internal void BeginInterval(int index, double left, double right, int evaluations) { intervalIndex = index; intervalStart = evaluations; intervalRecord = intervals.Count; intervals.Add(new LightSpaceOracleDiagnosticInterval(path, radialRuleIndex, index, left, right, 256, 0.0d, 0.0d, 0, evaluations + 1, evaluations, false)); }
        internal void CompleteInterval(double coarse, double fine, int evaluations) { ReplaceInterval(coarse, fine, evaluations - intervalStart, evaluations, true); intervalRecord = -1; }
        internal void RecordPartialInterval(double coarse, double fine, int evaluations) { ReplaceInterval(coarse, fine, evaluations - intervalStart, evaluations, false); intervalRecord = -1; }
        internal void SetScalarContext(int currentSubpanel, string currentRule, int currentRuleIndex) { subpanel = currentSubpanel; rule = currentRule; ruleIndex = currentRuleIndex; }
        internal void RecordFailure(LightSpaceOracleStopState state, int evaluations, string phase)
        {
            if (failure.StopState != LightSpaceOracleStopState.Accepted) return;
            failure = new LightSpaceOracleDiagnosticFailure(state, phase, path, radialRuleIndex, intervalIndex, subpanel, rule, ruleIndex, evaluations + 1, Topology());
            RecordActiveDiscard();
        }
        internal LightSpaceOracleDiagnosticTrace Complete(LightSpaceOracleResult result)
        {
            LightSpaceOracleDiagnosticFailure terminal = failure.StopState == LightSpaceOracleStopState.Accepted ? new LightSpaceOracleDiagnosticFailure(result.StopState, string.Empty, default, -1, -1, -1, string.Empty, -1, 0, Topology()) : failure;
            return new LightSpaceOracleDiagnosticTrace(input, target, result, ReadOnly(leaves), ReadOnly(panels), ReadOnly(committed), ReadOnly(selections), ReadOnly(nodes), ReadOnly(intervals), terminal);
        }

        /// <summary>Updates the active aggregate interval while preserving its identity and retained bounds.</summary>
        private void ReplaceInterval(double coarse, double fine, int scalarCalls, int evaluations, bool complete)
        {
            LightSpaceOracleDiagnosticInterval prior = intervals[intervalRecord]; intervals[intervalRecord] = new LightSpaceOracleDiagnosticInterval(prior.Path, prior.RuleIndex, prior.IntervalIndex, prior.Left, prior.Right, prior.Subpanels, coarse, fine, scalarCalls, prior.FirstEvaluation, evaluations, complete);
        }
        private void RecordCommitState(LightSpaceOracleCommittedLeaf leaf) { leaves.Add(new LightSpaceOracleDiagnosticLeaf(leaf.Path, leaf.Left, leaf.Right, LightSpaceOracleDiagnosticLeafState.Committed, leaves.Count)); }
        private void RecordActiveDiscard()
        {
            for (int index = leaves.Count - 1; index >= 0; index--) if (leaves[index].Path.Depth == path.Depth && leaves[index].Path.BinaryPath == path.BinaryPath && leaves[index].State == LightSpaceOracleDiagnosticLeafState.Prepared) { RecordDiscard(path, leaves[index].Left, leaves[index].Right); return; }
            RecordDiscard(path, 0.0d, 0.0d);
        }
        private string Topology() { var names = new List<string>(); foreach (LightSpaceOracleCommittedLeaf leaf in committed) names.Add(leaf.Path.Depth + ":" + leaf.Path.BinaryPath); return string.Join("|", names); }
        private static LightSpaceOracleDiagnosticFailure AcceptedFailure() => new LightSpaceOracleDiagnosticFailure(LightSpaceOracleStopState.Accepted, string.Empty, default, -1, -1, -1, string.Empty, -1, 0, string.Empty);
        private static IReadOnlyList<T> ReadOnly<T>(List<T> values) => new ReadOnlyCollection<T>(values);
    }

    /// <summary>Classifies one replayed contract observation without inferring a numerical root cause.</summary>
    internal enum LightSpaceOracleConformanceDisposition { Conforming, Drift }

    /// <summary>Stores one independent replay disposition and its local evidence label.</summary>
    internal readonly struct LightSpaceOracleConformanceObservation
    {
        internal LightSpaceOracleConformanceObservation(string name, LightSpaceOracleConformanceDisposition disposition) { Name = name; Disposition = disposition; }
        internal string Name { get; }
        internal LightSpaceOracleConformanceDisposition Disposition { get; }
    }

    /// <summary>Stores immutable independent replay results for one reference-prototype trace.</summary>
    internal sealed class LightSpaceOracleConformanceReport
    {
        internal LightSpaceOracleConformanceReport(List<LightSpaceOracleConformanceObservation> dispositions) { Dispositions = new ReadOnlyCollection<LightSpaceOracleConformanceObservation>(dispositions); }
        internal IReadOnlyList<LightSpaceOracleConformanceObservation> Dispositions { get; }
        internal bool Has(string name, LightSpaceOracleConformanceDisposition disposition) { foreach (LightSpaceOracleConformanceObservation item in Dispositions) if (item.Name == name && item.Disposition == disposition) return true; return false; }
    }

    /// <summary>Replays bounded prototype observations without calling prototype numerical helpers.</summary>
    internal static class LightSpaceOracleDiagnosticReplay
    {
        internal static LightSpaceOracleConformanceReport Classify(LightSpaceOracleDiagnosticTrace trace)
        {
            var results = new List<LightSpaceOracleConformanceObservation>();
            Add(results, "scalar-ledger", trace.IntervalScalarCalls == trace.Result.Evaluations);
            Add(results, "resource-ceilings", trace.Result.Evaluations <= 4000000 && trace.Result.Panels <= 262144 && trace.Result.MaximumDepth <= 22);
            Add(results, "panel-reservations", PreparedPanels(trace) == trace.Result.Panels);
            Add(results, "committed-partition", trace.CommittedLeaves.Count == 0 || LightSpaceOracleTopologyContract.IsCompleteNonOverlappingPartition(trace.CommittedLeaves));
            Add(results, "terminal-stop", trace.FirstFailure.StopState == trace.Result.StopState);
            Add(results, "root-residuals", RootsHaveValidResiduals(trace.Nodes));
            Add(results, "root-presence", RootsHaveValidPresence(trace.Nodes));
            Add(results, "root-semantic-ordering", RootsMatchRecordedMasks(trace.Nodes));
            Add(results, "shared-fine-33", UsesFineInnerAtSharedNodes(trace));
            Add(results, "radial-error", UsesContractRadialError(trace));
            Add(results, "angular-error", UsesContractAngularError(trace));
            Add(results, "leaf-error", UsesContractLeafError(trace));
            results.Add(new LightSpaceOracleConformanceObservation("fixed-256-subpanels", HasFixedSubpanels(trace.Intervals) ? LightSpaceOracleConformanceDisposition.Drift : LightSpaceOracleConformanceDisposition.Conforming));
            return new LightSpaceOracleConformanceReport(results);
        }

        private static void Add(List<LightSpaceOracleConformanceObservation> results, string name, bool condition) => results.Add(new LightSpaceOracleConformanceObservation(name, condition ? LightSpaceOracleConformanceDisposition.Conforming : LightSpaceOracleConformanceDisposition.Drift));
        private static int PreparedPanels(LightSpaceOracleDiagnosticTrace trace) { int count = 0; foreach (LightSpaceOracleDiagnosticLeaf leaf in trace.Leaves) if (leaf.State == LightSpaceOracleDiagnosticLeafState.Prepared) count++; return count; }
        private static bool HasFixedSubpanels(IReadOnlyList<LightSpaceOracleDiagnosticInterval> intervals) { foreach (LightSpaceOracleDiagnosticInterval interval in intervals) if (interval.Subpanels == 256) return true; return false; }
        private static bool RootsHaveValidResiduals(IReadOnlyList<LightSpaceOracleDiagnosticNode> nodes) { foreach (LightSpaceOracleDiagnosticNode node in nodes) foreach (LightSpaceOracleDiagnosticRoot root in node.Roots) if (root.Present && (!Finite(root.Target) || !Finite(root.Reconstructed) || !WithinUlps(root.Reconstructed, root.Target, 128))) return false; return true; }
        private static bool RootsHaveValidPresence(IReadOnlyList<LightSpaceOracleDiagnosticNode> nodes) { foreach (LightSpaceOracleDiagnosticNode node in nodes) foreach (LightSpaceOracleDiagnosticRoot root in node.Roots) if (root.Present != (Finite(root.Theta) && root.Theta > 0.0d && root.Theta < Math.PI)) return false; return true; }
        private static bool RootsMatchRecordedMasks(IReadOnlyList<LightSpaceOracleDiagnosticNode> nodes)
        {
            foreach (LightSpaceOracleDiagnosticNode node in nodes)
            {
                LightSpaceOracleDiagnosticRoot guard = default; LightSpaceOracleDiagnosticRoot distribution = default; bool hasGuard = false; bool hasDistribution = false;
                foreach (LightSpaceOracleDiagnosticRoot root in node.Roots) { if (root.Kind == "guard") { if (hasGuard) return false; guard = root; hasGuard = root.Present; } else if (root.Kind == "distribution") { if (hasDistribution) return false; distribution = root; hasDistribution = root.Present; } else return false; }
                if (!MaskMatches(node.Mask, guard, distribution, hasGuard, hasDistribution)) return false;
            }
            return true;
        }
        private static bool MaskMatches(IndependentOracleRootMask mask, LightSpaceOracleDiagnosticRoot guard, LightSpaceOracleDiagnosticRoot distribution, bool hasGuard, bool hasDistribution)
        {
            if (!hasGuard && !hasDistribution) return mask == IndependentOracleRootMask.None;
            if (hasGuard && !hasDistribution) return mask == IndependentOracleRootMask.Guard;
            if (!hasGuard) return mask == IndependentOracleRootMask.Distribution;
            if (NearlyEqual(guard.Theta, distribution.Theta, 32)) return mask == IndependentOracleRootMask.Guard;
            return guard.Theta < distribution.Theta ? mask == IndependentOracleRootMask.GuardThenDistribution : mask == IndependentOracleRootMask.DistributionThenGuard;
        }
        private static bool UsesFineInnerAtSharedNodes(LightSpaceOracleDiagnosticTrace trace) { foreach (LightSpaceOracleDiagnosticPanel panel in trace.Panels) if (panel.Coarse != panel.CoarseWithFineInner) return false; return true; }
        private static bool UsesContractRadialError(LightSpaceOracleDiagnosticTrace trace) { foreach (LightSpaceOracleDiagnosticPanel panel in trace.Panels) if (panel.RadialError != Math.Abs(panel.Fine - panel.CoarseWithFineInner)) return false; return true; }
        private static bool UsesContractAngularError(LightSpaceOracleDiagnosticTrace trace) { foreach (LightSpaceOracleDiagnosticPanel panel in trace.Panels) if (panel.AngularError != ExpectedAngularError(trace.Nodes, panel)) return false; return true; }
        private static bool UsesContractLeafError(LightSpaceOracleDiagnosticTrace trace) { foreach (LightSpaceOracleDiagnosticPanel panel in trace.Panels) if (panel.Error != Math.Abs(panel.Fine - panel.CoarseWithFineInner) + ExpectedAngularError(trace.Nodes, panel)) return false; return true; }
        private static double ExpectedAngularError(IReadOnlyList<LightSpaceOracleDiagnosticNode> nodes, LightSpaceOracleDiagnosticPanel panel) { double value = 0.0d; foreach (LightSpaceOracleDiagnosticNode node in nodes) if (node.Path.Depth == panel.Path.Depth && node.Path.BinaryPath == panel.Path.BinaryPath) value += (panel.Right - panel.Left) * node.FineOuterWeight * Math.Abs(node.Fine - node.Coarse); return value; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static bool WithinUlps(double left, double right, int limit) { if (!Finite(left) || !Finite(right) || left < 0.0d || right < 0.0d) return false; ulong leftBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(left)); ulong rightBits = unchecked((ulong)BitConverter.DoubleToInt64Bits(right)); return leftBits >= rightBits ? leftBits - rightBits <= (ulong)limit : rightBits - leftBits <= (ulong)limit; }
        private static bool NearlyEqual(double left, double right, int limit) => Finite(left) && Finite(right) && Math.Abs(left - right) <= limit * Math.Max(Ulp(left), Ulp(right));
        private static double Ulp(double value) { long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value)); return bits == 0L ? BitConverter.Int64BitsToDouble(1L) : BitConverter.Int64BitsToDouble(bits + 1L) - BitConverter.Int64BitsToDouble(bits); }
    }

    /// <summary>Builds a host-independent FNV-1a digest from primitive diagnostic fields.</summary>
    internal sealed class LightSpaceOracleDigest
    {
        private ulong value = 14695981039346656037UL;
        internal string Value => value.ToString("X16");
        internal void Add(double item) => Add(unchecked((ulong)BitConverter.DoubleToInt64Bits(item)));
        internal void Add(int item) => Add(unchecked((ulong)(uint)item));
        internal void Add(ulong item) { value ^= item; value *= 1099511628211UL; }
        internal void Add(string item) { if (item == null) { Add(-1); return; } Add(item.Length); foreach (char character in item) Add(character); }
    }

    /// <summary>Classifies the candidate root whose topology admission failed.</summary>
    internal enum LightSpaceOracleCandidateRootKind { Guard, Distribution }

    /// <summary>Classifies whether the candidate changed a raw root cosine before evaluation.</summary>
    internal enum LightSpaceOracleCandidateCosineCorrection { None }

    /// <summary>Classifies the residual admission result for one candidate root.</summary>
    internal enum LightSpaceOracleCandidateRootResidualValidity { Valid, Invalid }

    /// <summary>Classifies whether one candidate root lies strictly inside the theta domain.</summary>
    internal enum LightSpaceOracleCandidateRootInteriorPresence { Absent, Present }

    /// <summary>Classifies the semantic order state when a candidate root attempt terminates.</summary>
    internal enum LightSpaceOracleCandidateRootSemanticOrder { NotReached }

    /// <summary>Stores the first rejected candidate root using only immutable primitive observations.</summary>
    internal readonly struct LightSpaceOracleCandidateRootTopologyFailure
    {
        /// <summary>Initializes the local facts available at one root topology rejection.</summary>
        internal LightSpaceOracleCandidateRootTopologyFailure(LightSpaceOracleCandidateRootKind kind, double radialCoordinate, double target, double rawCosine, LightSpaceOracleCandidateCosineCorrection correction, double theta, double reconstructedTarget, LightSpaceOracleCandidateRootResidualValidity residualValidity, LightSpaceOracleCandidateRootInteriorPresence interiorPresence, LightSpaceOracleCandidateRootSemanticOrder semanticOrder) { Kind = kind; RadialCoordinate = radialCoordinate; Target = target; RawCosine = rawCosine; Correction = correction; Theta = theta; ReconstructedTarget = reconstructedTarget; ResidualValidity = residualValidity; InteriorPresence = interiorPresence; SemanticOrder = semanticOrder; }
        internal LightSpaceOracleCandidateRootKind Kind { get; }
        internal double RadialCoordinate { get; }
        internal double Target { get; }
        internal double RawCosine { get; }
        internal LightSpaceOracleCandidateCosineCorrection Correction { get; }
        internal double Theta { get; }
        internal double ReconstructedTarget { get; }
        internal LightSpaceOracleCandidateRootResidualValidity ResidualValidity { get; }
        internal LightSpaceOracleCandidateRootInteriorPresence InteriorPresence { get; }
        internal LightSpaceOracleCandidateRootSemanticOrder SemanticOrder { get; }
    }

    /// <summary>Collects a bounded, write-only digest of candidate lifecycle observations.</summary>
    internal sealed class LightSpaceOracleCandidateDiagnosticSink
    {
        private const int MaximumRecords = 128;
        private readonly LightSpaceOracleDigest digest = new LightSpaceOracleDigest();
        private int records;
        private LightSpaceOracleCandidateRootTopologyFailure? firstRootTopologyFailure;

        /// <summary>Gets the number of retained bounded candidate observations.</summary>
        internal int Records => records;

        /// <summary>Gets the stable digest of all retained candidate observations.</summary>
        internal string Digest => digest.Value;

        /// <summary>Gets the independent first root topology failure without exposing it to candidate computation.</summary>
        internal LightSpaceOracleCandidateRootTopologyFailure? FirstRootTopologyFailure => firstRootTopologyFailure;

        /// <summary>Records one terminal or completed-leaf observation without supplying values back to the candidate.</summary>
        internal void Record(IndependentOracleCanonicalPath path, int evaluations, int panels, double value, double error, LightSpaceOracleStopState state)
        {
            if (records >= MaximumRecords) return;
            digest.Add(path.Depth); digest.Add(path.BinaryPath); digest.Add(evaluations); digest.Add(panels); digest.Add(value); digest.Add(error); digest.Add((int)state); records++;
        }

        /// <summary>Records only the first root failure independently of the lifecycle-record capacity.</summary>
        internal void RecordFirstRootTopologyFailure(LightSpaceOracleCandidateRootKind kind, double radialCoordinate, double target, double rawCosine, LightSpaceOracleCandidateCosineCorrection correction, double theta, double reconstructedTarget, LightSpaceOracleCandidateRootResidualValidity residualValidity, LightSpaceOracleCandidateRootInteriorPresence interiorPresence, LightSpaceOracleCandidateRootSemanticOrder semanticOrder)
        {
            if (firstRootTopologyFailure.HasValue) return;
            firstRootTopologyFailure = new LightSpaceOracleCandidateRootTopologyFailure(kind, radialCoordinate, target, rawCosine, correction, theta, reconstructedTarget, residualValidity, interiorPresence, semanticOrder);
            digest.Add("root-topology-failure"); digest.Add((int)kind); digest.Add(radialCoordinate); digest.Add(target); digest.Add(rawCosine); digest.Add((int)correction); digest.Add(theta); digest.Add(reconstructedTarget); digest.Add((int)residualValidity); digest.Add((int)interiorPresence); digest.Add((int)semanticOrder);
        }
    }
}
