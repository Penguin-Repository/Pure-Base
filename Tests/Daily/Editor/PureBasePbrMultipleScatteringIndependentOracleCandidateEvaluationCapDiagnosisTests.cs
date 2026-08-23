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

// Profiles the committed candidate mesh after its known minimum-grazing EvaluationCap stop.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Captures observer-neutral radial and angular error evidence from the terminal candidate mesh.</summary>
    public sealed class PureBasePbrMultipleScatteringIndependentOracleCandidateEvaluationCapDiagnosisTests
    {
        /// <summary>Requires the exact minimum-grazing stop to retain a reconcilable terminal mesh profile.</summary>
        [Test]
        public void MinimumGrazingBaseTargetCapturesTerminalMeshErrorProfile()
        {
            IndependentOracleInput input = MinimumGrazingNormalInput();
            var scheduler = new LightSpaceOracleCandidateScheduler(input, IndependentOracleContract.CandidateBaseTarget, null);
            LightSpaceOracleResult result = scheduler.Run();
            AssertEvaluationCap(result);

            List<LightSpaceOracleCandidateLeaf> leaves = ReadCommittedLeaves(scheduler);
            List<TerminalLeafProfile> profile = BuildProfile(input, leaves);
            TerminalMeshTotals totals = Reconcile(result, leaves, profile);

            Assert.That(totals.RecomposedBits, Is.EqualTo(totals.CommittedBits), totals.Evidence);
            Assert.That(totals.LeafCount, Is.EqualTo(leaves.Count));
            TestContext.Progress.WriteLine(totals.Evidence);
            UnityEngine.Debug.Log(totals.Evidence);
        }

        /// <summary>Requires direct public and witness integration boundaries to remain unavailable.</summary>
        [Test]
        public void PublicAndWitnessIntegrationBoundariesRemainUnavailable()
        {
            IndependentOracleInput input = MinimumGrazingNormalInput();
            Assert.That(() => LightSpaceOracle.Integrate(input, IndependentOracleContract.CandidateBaseTarget), Throws.TypeOf<NotImplementedException>());
            Assert.That(() => IndependentOracleWitness.Integrate(input), Throws.TypeOf<NotImplementedException>());
        }

        /// <summary>Builds the exact raw-binary64 minimum-grazing Normal input without decimal normalization.</summary>
        private static IndependentOracleInput MinimumGrazingNormalInput()
        {
            double p = BitConverter.Int64BitsToDouble(unchecked((long)0x3FB6C8B439581062UL));
            double ndotV = BitConverter.Int64BitsToDouble(0L);
            return new IndependentOracleInput(p, ndotV, IndependentOracleBranch.Normal);
        }

        /// <summary>Requires only the retained fail-closed EvaluationCap terminal facts.</summary>
        private static void AssertEvaluationCap(LightSpaceOracleResult result)
        {
            Assert.That(result.StopState, Is.EqualTo(LightSpaceOracleStopState.EvaluationCap));
            Assert.That(result.Evaluations, Is.EqualTo(4000000));
            Assert.That(double.IsNaN(result.Value), Is.True);
            Assert.That(double.IsNaN(result.EstimatedError), Is.True);
            Assert.That(result.Topology, Is.Empty);
            Assert.That(result.Panels, Is.EqualTo(4671));
            Assert.That(result.MaximumDepth, Is.EqualTo(15));
        }

        /// <summary>Reads the scheduler's private completed-leaf collection after terminal execution without mutating it.</summary>
        private static List<LightSpaceOracleCandidateLeaf> ReadCommittedLeaves(LightSpaceOracleCandidateScheduler scheduler)
        {
            FieldInfo field = typeof(LightSpaceOracleCandidateScheduler).GetField("leaves", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "The scheduler no longer exposes its committed leaf collection to this diagnostic probe.");
            var copied = new List<LightSpaceOracleCandidateLeaf>();
            IEnumerable source = field.GetValue(scheduler) as IEnumerable;
            Assert.That(source, Is.Not.Null, "The committed leaf collection is unavailable.");
            foreach (object item in source) copied.Add((LightSpaceOracleCandidateLeaf)item);
            Assert.That(copied, Is.Not.Empty);
            return copied;
        }

        /// <summary>Recomputes every committed leaf using fresh diagnostic rule instances in canonical spatial order.</summary>
        private static List<TerminalLeafProfile> BuildProfile(IndependentOracleInput input, List<LightSpaceOracleCandidateLeaf> leaves)
        {
            var profiles = new List<TerminalLeafProfile>();
            foreach (LightSpaceOracleCandidateLeaf leaf in leaves) profiles.Add(new TerminalLeafEvaluator(input).Evaluate(leaf));
            profiles.Sort((left, right) => left.Leaf.Path.CompareSpatial(right.Leaf.Path));
            return profiles;
        }

        /// <summary>Pairwise-reduces component and committed sequences to check the frozen aggregate ordering.</summary>
        private static TerminalMeshTotals Reconcile(LightSpaceOracleResult result, List<LightSpaceOracleCandidateLeaf> leaves, List<TerminalLeafProfile> profiles)
        {
            var ordered = new List<LightSpaceOracleCandidateLeaf>(leaves);
            ordered.Sort((left, right) => left.Path.CompareSpatial(right.Path));
            var radial = new double[profiles.Count]; var angular = new double[profiles.Count]; var recomposed = new double[profiles.Count]; var committed = new double[ordered.Count];
            for (int index = 0; index < profiles.Count; index++)
            {
                TerminalLeafProfile current = profiles[index];
                AssertFiniteNonNegative(current.RadialError, current.AngularError, current.RecomposedError, current.MaximumIntervalError);
                Assert.That(current.Leaf.Path.Depth, Is.EqualTo(ordered[index].Path.Depth));
                Assert.That(current.Leaf.Path.BinaryPath, Is.EqualTo(ordered[index].Path.BinaryPath));
                radial[index] = current.RadialError; angular[index] = current.AngularError; recomposed[index] = current.RecomposedError; committed[index] = ordered[index].Error;
            }
            double radialSum = PairwiseReduce(radial); double angularSum = PairwiseReduce(angular);
            double recomposedSum = PairwiseReduce(recomposed); double committedSum = PairwiseReduce(committed);
            return new TerminalMeshTotals(result, radialSum, angularSum, recomposedSum, committedSum, profiles);
        }

        /// <summary>Requires each reported component to remain a finite nonnegative diagnostic fact.</summary>
        private static void AssertFiniteNonNegative(double radial, double angular, double recomposed, double interval)
        {
            Assert.That(Finite(radial) && radial >= 0.0d, Is.True);
            Assert.That(Finite(angular) && angular >= 0.0d, Is.True);
            Assert.That(Finite(recomposed) && recomposed >= 0.0d, Is.True);
            Assert.That(Finite(interval) && interval >= 0.0d, Is.True);
        }

        /// <summary>Applies the scheduler's adjacent-pair reduction to a copied canonical sequence.</summary>
        private static double PairwiseReduce(double[] values)
        {
            for (int count = values.Length; count > 1; count = (count + 1) / 2)
            {
                int pairs = count / 2;
                for (int index = 0; index < pairs; index++) values[index] = values[index * 2] + values[index * 2 + 1];
                if (count % 2 != 0) values[pairs] = values[count - 1];
            }
            return values[0];
        }

        /// <summary>Gets whether a binary64 value is finite.</summary>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Recomputes one committed outer leaf without sharing scheduler state or diagnostics.</summary>
        private sealed class TerminalLeafEvaluator
        {
            private readonly IndependentOracleInput input;
            private readonly LightSpaceOracleCandidateRuleNode[] outerFine;
            private readonly LightSpaceOracleCandidateRuleNode[] outerCoarse;
            private readonly LightSpaceOracleCandidateRuleNode[] innerCoarse;
            private readonly LightSpaceOracleCandidateRuleNode[] innerFine;

            /// <summary>Initializes one fresh diagnostic evaluator with frozen quadrature factories.</summary>
            public TerminalLeafEvaluator(IndependentOracleInput input)
            {
                this.input = input;
                outerFine = LightSpaceOracleCandidateQuadrature.ClenshawCurtis(17);
                outerCoarse = LightSpaceOracleCandidateQuadrature.ClenshawCurtis(9);
                innerCoarse = LightSpaceOracleCandidateQuadrature.FejerII(17);
                innerFine = LightSpaceOracleCandidateQuadrature.FejerII(33);
            }

            /// <summary>Recomputes the radial and weighted angular indicators for one frozen interval.</summary>
            public TerminalLeafProfile Evaluate(LightSpaceOracleCandidateLeaf leaf)
            {
                var values = new double[17]; var angular = new double[17]; var counts = new int[17]; var maxima = new double[17]; int intervals = 0; double maximumInterval = 0.0d;
                double half = (leaf.Right - leaf.Left) * 0.5d; double middle = (leaf.Right + leaf.Left) * 0.5d;
                for (int index = 0; index < outerFine.Length; index++) EvaluateNode(middle + half * outerFine[index].Coordinate, out values[index], out angular[index], out counts[index], out maxima[index]);
                for (int index = 0; index < outerFine.Length; index++) { intervals += counts[index]; maximumInterval = Math.Max(maximumInterval, maxima[index]); }
                return BuildLeafProfile(leaf, values, angular, half, intervals, maximumInterval);
            }

            /// <summary>Builds one leaf profile with the candidate's nested 9/17 outer accumulation order.</summary>
            private TerminalLeafProfile BuildLeafProfile(LightSpaceOracleCandidateLeaf leaf, double[] values, double[] angular, double half, int intervals, double maximumInterval)
            {
                double q17 = 0.0d; double q9 = 0.0d; double angularError = 0.0d;
                for (int index = 0; index < outerFine.Length; index++) { q17 += outerFine[index].Weight * values[index]; angularError += outerFine[index].Weight * angular[index]; }
                for (int index = 0; index < outerCoarse.Length; index++) q9 += outerCoarse[index].Weight * values[index * 2];
                q17 *= half; q9 *= half; angularError *= half;
                double radialError = Math.Abs(q17 - q9); double recomposed = LightSpaceOracleContractAlignedCandidate.ComposeLeafError(q9, q17, angularError);
                return new TerminalLeafProfile(leaf, radialError, angularError, recomposed, intervals, maximumInterval);
            }

            /// <summary>Recomputes a radial-node fine value and unweighted angular difference from atomic intervals.</summary>
            private void EvaluateNode(double radial, out double value, out double angularError, out int intervals, out double maximumInterval)
            {
                value = 0.0d; angularError = 0.0d; intervals = 0; maximumInterval = 0.0d;
                Assert.That(LightSpaceOracleContractAlignedCandidate.TryDeriveThetaPartition(input, radial, out LightSpaceOracleCandidateThetaPartition partition), Is.True);
                for (int index = 0; index + 1 < partition.Boundaries.Length; index++)
                {
                    EvaluateInterval(radial, partition.Boundaries[index], partition.Boundaries[index + 1], out double coarse, out double fine);
                    double difference = Math.Abs(fine - coarse); value += fine; angularError += difference; intervals++; maximumInterval = Math.Max(maximumInterval, difference);
                }
            }

            /// <summary>Applies the frozen endpoint-free 17/33 inner rules to one atomic theta interval.</summary>
            private void EvaluateInterval(double radial, double left, double right, out double coarse, out double fine)
            {
                coarse = 0.0d; fine = 0.0d; double half = (right - left) * 0.5d; double middle = (right + left) * 0.5d;
                for (int index = 0; index < innerCoarse.Length; index++) coarse += innerCoarse[index].Weight * LightSpaceOracleContractAlignedCandidate.EvaluateScalar(input, radial, middle + half * innerCoarse[index].Coordinate);
                for (int index = 0; index < innerFine.Length; index++) fine += innerFine[index].Weight * LightSpaceOracleContractAlignedCandidate.EvaluateScalar(input, radial, middle + half * innerFine[index].Coordinate);
                coarse *= half; fine *= half;
            }
        }

        /// <summary>Stores one recomputed terminal leaf and its component evidence.</summary>
        private readonly struct TerminalLeafProfile
        {
            /// <summary>Initializes immutable radial and angular evidence for one committed interval.</summary>
            public TerminalLeafProfile(LightSpaceOracleCandidateLeaf leaf, double radialError, double angularError, double recomposedError, int atomicIntervals, double maximumIntervalError)
            {
                Leaf = leaf; RadialError = radialError; AngularError = angularError; RecomposedError = recomposedError; AtomicIntervals = atomicIntervals; MaximumIntervalError = maximumIntervalError;
            }

            /// <summary>Gets the reflected committed leaf identity and radial interval.</summary>
            public LightSpaceOracleCandidateLeaf Leaf { get; }
            /// <summary>Gets the independent outer 17-versus-9 radial error.</summary>
            public double RadialError { get; }
            /// <summary>Gets the outer-weighted sum of atomic 33-versus-17 angular errors.</summary>
            public double AngularError { get; }
            /// <summary>Gets the frozen composed leaf error.</summary>
            public double RecomposedError { get; }
            /// <summary>Gets the sampled atomic theta-interval count.</summary>
            public int AtomicIntervals { get; }
            /// <summary>Gets the largest unweighted atomic theta-interval difference.</summary>
            public double MaximumIntervalError { get; }
        }

        /// <summary>Stores deterministic aggregate evidence and a bounded contributor record for one terminal mesh.</summary>
        private readonly struct TerminalMeshTotals
        {
            /// <summary>Initializes component sums and formats the bounded deterministic diagnostic record.</summary>
            public TerminalMeshTotals(LightSpaceOracleResult result, double radial, double angular, double recomposed, double committed, List<TerminalLeafProfile> profiles)
            {
                Radial = radial; Angular = angular; Recomposed = recomposed; Committed = committed; LeafCount = profiles.Count;
                RadialAngularTarget = angular > IndependentOracleContract.CandidateBaseTarget;
                RadialDominates = radial > angular;
                RecomposedBits = BitConverter.DoubleToInt64Bits(recomposed); CommittedBits = BitConverter.DoubleToInt64Bits(committed);
                Evidence = string.Empty;
                Evidence = BuildEvidence(result, profiles);
            }

            /// <summary>Gets the pairwise-reduced radial component sum.</summary>
            public double Radial { get; }
            /// <summary>Gets the pairwise-reduced angular component sum.</summary>
            public double Angular { get; }
            /// <summary>Gets the pairwise-reduced recomposed component sum.</summary>
            public double Recomposed { get; }
            /// <summary>Gets the pairwise-reduced reflected leaf-error sum.</summary>
            public double Committed { get; }
            /// <summary>Gets the terminal committed leaf count.</summary>
            public int LeafCount { get; }
            /// <summary>Gets whether angular sum exceeds the unchanged base target.</summary>
            public bool RadialAngularTarget { get; }
            /// <summary>Gets whether radial sum is greater than angular sum.</summary>
            public bool RadialDominates { get; }
            /// <summary>Gets the raw binary64 recomposed aggregate bits.</summary>
            public long RecomposedBits { get; }
            /// <summary>Gets the raw binary64 reflected aggregate bits.</summary>
            public long CommittedBits { get; }
            /// <summary>Gets the bounded deterministic terminal evidence record.</summary>
            public string Evidence { get; }

            /// <summary>Formats the four highest recomposed-error leaves using stable canonical tie breaking.</summary>
            private string BuildEvidence(LightSpaceOracleResult result, List<TerminalLeafProfile> profiles)
            {
                var top = new List<TerminalLeafProfile>(profiles);
                top.Sort((left, right) => right.RecomposedError != left.RecomposedError ? right.RecomposedError.CompareTo(left.RecomposedError) : left.Leaf.Path.CompareSpatial(right.Leaf.Path));
                if (top.Count > 4) top.RemoveRange(4, top.Count - 4);
                var text = new System.Text.StringBuilder();
                for (int index = 0; index < top.Count; index++) AppendContributor(text, top[index], index);
                return string.Format(CultureInfo.InvariantCulture, "stopState={0};evaluations={1};panels={2};maximumDepth={3};pBits=3FB6C8B439581062;ndotVBits=0000000000000000;target={4:R};leaves={5};radial={6:R};angular={7:R};recomposed={8:R};committed={9:R};recomposedBits={10};committedBits={11};angularAboveTarget={12};radialDominates={13};top={14}", result.StopState, result.Evaluations, result.Panels, result.MaximumDepth, IndependentOracleContract.CandidateBaseTarget, LeafCount, Radial, Angular, Recomposed, Committed, RecomposedBits, CommittedBits, RadialAngularTarget, RadialDominates, text);
            }

            /// <summary>Appends one bounded leaf interval and component evidence record.</summary>
            private static void AppendContributor(System.Text.StringBuilder text, TerminalLeafProfile profile, int index)
            {
                if (index > 0) text.Append('|');
                text.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1}:{2:R}-{3:R}:er={4:R}:etheta={5:R}:atoms={6}:max={7:R}", profile.Leaf.Path.Depth, profile.Leaf.Path.BinaryPath, profile.Leaf.Left, profile.Leaf.Right, profile.RadialError, profile.AngularError, profile.AtomicIntervals, profile.MaximumIntervalError);
            }
        }
    }
}
