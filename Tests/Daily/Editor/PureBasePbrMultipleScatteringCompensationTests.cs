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

// Defines numerical-oracle contracts for the independent furnace protocol.

using System;
using System.IO;
using System.Text;
using NUnit.Framework;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines the independent fit artifact and numerical protocol contracts.</summary>
    public sealed partial class PureBasePbrMultipleScatteringCompensationTests
    {
        /// <summary>Proves the retained bounded-Newton diagnostic terminates without becoming canonical data.</summary>
        [Test, Timeout(10000)]
        public void FixedQuadratureRulesTerminate()
        {
            double legacy = PureBasePbrMultipleScatteringReference.IntegrateLegacyDiagnostic(0.5d, 0.5d, false);
            Assert.That(double.IsNaN(legacy) || double.IsInfinity(legacy), Is.False, "The retained legacy diagnostic must produce a finite value.");
        }

        /// <summary>Proves exact and nonzero near-antiparallel vectors use the HLSL safe-normalize guard.</summary>
        [Test]
        public void SafeNormalizeMatchesHlslGuardForExactAndNearAntiparallelVectors()
        {
            AssertGuardedTerms(new PureBasePbrMultipleScatteringReference.Direction(-1.0d, 0.0d, 0.0d), new PureBasePbrMultipleScatteringReference.Direction(1.0d, 0.0d, 0.0d));
            var light = new PureBasePbrMultipleScatteringReference.Direction(-1.0d, 0.0d, 0.0d);
            var view = new PureBasePbrMultipleScatteringReference.Direction(1.0d, 0.0d, 0.0005d);
            PureBasePbrMultipleScatteringReference.Direction sum = light + view;
            Assert.That(sum.Dot(sum), Is.GreaterThan(0.0d).And.LessThan(0.000001d));
            AssertGuardedTerms(light, view);
        }

        /// <summary>Compares all primitive guarded terms against the exact HLSL algebra.</summary>
        private static void AssertGuardedTerms(PureBasePbrMultipleScatteringReference.Direction light, PureBasePbrMultipleScatteringReference.Direction view)
        {
            PureBasePbrMultipleScatteringReference.GuardedTerms actual = PureBasePbrMultipleScatteringReference.EvaluateGuardedTerms(light, view, 0.089d, false);
            double inverseLength = 1.0d / Math.Sqrt(Math.Max((light + view).Dot(light + view), 0.000001d));
            double ndotH = Math.Max(0.0d, (light.Z + view.Z) * inverseLength);
            double roughnessFourth = Math.Pow(0.089d * 0.089d, 2.0d);
            double distribution = roughnessFourth / Math.Max(Math.PI * Math.Pow(ndotH * ndotH * (roughnessFourth - 1.0d) + 1.0d, 2.0d), 0.000001d);
            double ndotL = Math.Max(0.0d, light.Z);
            double ndotV = Math.Max(0.0d, view.Z);
            double roughnessSquared = 0.089d * 0.089d;
            double lambda = ndotL * (ndotV * (1.0d - roughnessSquared) + roughnessSquared) + ndotV * (ndotL * (1.0d - roughnessSquared) + roughnessSquared);
            double visibility = 0.5d / (lambda + PureBasePbrMultipleScatteringReference.NormalEpsilon);
            Assert.That(actual.NdotH, Is.EqualTo(ndotH).Within(1e-15d));
            Assert.That(actual.Distribution, Is.EqualTo(distribution).Within(1e-15d));
            Assert.That(actual.Visibility, Is.EqualTo(visibility).Within(1e-9d));
        }

        /// <summary>Requires separately implemented transforms, splitters, rules, schedulers, and accumulators.</summary>
        [Test]
        public void AdaptiveProtocolsRemainIndependent()
        {
            AdaptiveIdentity primary = AdaptivePrimary.Identity;
            AdaptiveIdentity crossCheck = AdaptiveCrossCheck.Identity;
            AdaptiveIdentity witness = KronrodWitness.Identity;
            Assert.That(primary.Transform, Does.Contain("eta"));
            Assert.That(crossCheck.Transform, Does.Contain("tau"));
            Assert.That(witness.Transform, Does.Contain("t^2"));
            Assert.That(primary.Transform, Is.Not.EqualTo(crossCheck.Transform));
            Assert.That(primary.Splitter, Is.Not.EqualTo(crossCheck.Splitter));
            Assert.That(primary.Rule, Is.Not.EqualTo(crossCheck.Rule));
            Assert.That(primary.Scheduler, Is.Not.EqualTo(crossCheck.Scheduler));
            Assert.That(primary.Accumulator, Is.Not.EqualTo(crossCheck.Accumulator));
            Assert.That(witness.Transform, Is.Not.EqualTo(primary.Transform).And.Not.EqualTo(crossCheck.Transform));
            Assert.That(witness.Splitter, Is.Not.EqualTo(primary.Splitter).And.Not.EqualTo(crossCheck.Splitter));
            Assert.That(witness.Rule, Is.Not.EqualTo(primary.Rule).And.Not.EqualTo(crossCheck.Rule));
            Assert.That(witness.Scheduler, Is.Not.EqualTo(primary.Scheduler).And.Not.EqualTo(crossCheck.Scheduler));
            Assert.That(witness.Accumulator, Is.Not.EqualTo(primary.Accumulator).And.Not.EqualTo(crossCheck.Accumulator));
        }

        /// <summary>Requires the explicit G15 subset to remain embedded in ascending endpoint-free K31 order.</summary>
        [Test]
        public void KronrodRuleHasCanonicalEmbeddedSubsetAndNoEndpoints()
        {
            AdaptiveCrossCheck.KronrodRuleProbe rule = AdaptiveCrossCheck.GetKronrodRuleForTest();
            Assert.That(rule.Nodes, Has.Length.EqualTo(31)); Assert.That(rule.KronrodWeights, Has.Length.EqualTo(31)); Assert.That(rule.GaussSubset, Has.Length.EqualTo(15)); Assert.That(rule.GaussWeights, Has.Length.EqualTo(15));
            for (int index = 0; index < rule.Nodes.Length; index++) { Assert.That(rule.Nodes[index], Is.GreaterThan(-1.0d).And.LessThan(1.0d)); if (index != 0) Assert.That(rule.Nodes[index], Is.GreaterThan(rule.Nodes[index - 1])); }
            for (int index = 0; index < rule.GaussSubset.Length; index++) { int node = rule.GaussSubset[index]; Assert.That(node, Is.GreaterThanOrEqualTo(0).And.LessThan(rule.Nodes.Length)); if (index != 0) Assert.That(node, Is.GreaterThan(rule.GaussSubset[index - 1])); Assert.That(rule.GaussWeights[index], Is.GreaterThan(0.0d)); }
        }

        /// <summary>Requires every reported primary guard root to reconstruct the HLSL threshold exactly.</summary>
        [Test]
        public void PrimarySafeNormalizeRootsUseFourQSquared()
        {
            foreach (double v in new[] { 0.0d, 0.001d, 0.05d })
            foreach (double psi in new[] { 0.0d, Math.PI * 0.5d, Math.PI })
            {
                double[] boundaries = AdaptivePrimary.GetEtaPartitionBoundariesForTest(0.089d, v, psi); double[] roots = AdaptivePrimary.GetSafeNormalizeEtaRootsForTest(0.089d, v, psi);
                foreach (double eta in roots)
                {
                    double r2 = Math.Pow(0.089d, 4.0d) * eta / (1.0d - eta); double q = (Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)) * Math.Cos(psi) * Math.Sqrt(r2) + v) / Math.Sqrt(1.0d + r2);
                    Assert.That(4.0d * q * q, Is.EqualTo(AdaptiveProtocol.GuardSquared).Within(1.0e-12d)); AssertRootIsPanelEdge(boundaries, eta);
                }
            }
        }

        /// <summary>Requires every distribution-clamp transition to be an initial panel endpoint in both paths.</summary>
        [Test]
        public void KronrodAtomicIntervalsPreserveGuardAndGgxRoots()
        {
            double[] primaryRoots = AdaptivePrimary.GetDistributionEtaRootsForTest(0.089d, 0.0d, 0.0d); Assert.That(primaryRoots, Is.Not.Empty);
            foreach (double root in primaryRoots) AssertRootIsPanelEdge(AdaptivePrimary.GetEtaPartitionBoundariesForTest(0.089d, 0.0d, 0.0d), root);
            int crossRoots = 0;
            foreach (double u in AdaptiveCrossCheck.GetUPartitionBoundariesForTest(0.089d, 0.0d))
            {
                if (u <= 0.0d || u >= 1.0d) continue; double[] roots = AdaptiveCrossCheck.GetDistributionTauRootsForTest(0.089d, 0.0d, u); crossRoots += roots.Length;
                foreach (double root in roots) AssertRootIsPanelEdge(AdaptiveCrossCheck.GetTauPartitionBoundariesForTest(0.089d, 0.0d, u), AdaptiveCrossCheck.MapTauToChartForTest(0.089d, 0.0d, u, root));
            }
            Assert.That(crossRoots, Is.GreaterThan(0));
        }

        /// <summary>Requires the negative GGX clamp root to remain a ridge-chart partition endpoint.</summary>
        [Test]
        public void RidgeAwareTauChartPreservesNegativeGgxClampRoot()
        {
            const double p = 0.089d; const double v = 0.0d; const double u = 0.00669571543954991d;
            double[] roots = AdaptiveCrossCheck.GetDistributionTauRootsForTest(p, v, u); double negativeRoot = double.NaN;
            foreach (double root in roots) if (root < 0.0d) negativeRoot = root;
            Assert.That(negativeRoot, Is.EqualTo(-7.41482849870826e-5d).Within(1.0e-15d));
            double coordinate = AdaptiveCrossCheck.MapTauToChartForTest(p, v, u, negativeRoot);
            Assert.That(coordinate, Is.EqualTo(-0.626d).Within(0.001d)); AssertRootIsPanelEdge(AdaptiveCrossCheck.GetTauPartitionBoundariesForTest(p, v, u), coordinate);
        }

        /// <summary>Routes the guarded GGX-root suffix through the fixed normal-visibility x transformation.</summary>
        [Test]
        public void VisibilityTailRoutesNonterminalGuardedGgxRootPartitionToX()
        {
            const double p = 0.089d; const double v = 0.0d; const double psi = 0.70812544800562582d; const double guardedRoot = 0.99999999997282729d;
            double[] boundaries = AdaptivePrimary.GetEtaPartitionBoundariesForTest(p, v, psi); double[] roots = AdaptivePrimary.GetDistributionEtaRootsForTest(p, v, psi); bool[] routed = AdaptivePrimary.GetVisibilityTailXPartitionsForTest(p, v, psi, false);
            bool reported = false; int partition = -1;
            foreach (double root in roots) reported |= Math.Abs(root - guardedRoot) <= 1.0e-15d;
            for (int index = 0; index < boundaries.Length - 1; index++) if (Math.Abs(boundaries[index + 1] - guardedRoot) <= 1.0e-15d) partition = index;
            Assert.That(reported, Is.True, "The fixed witness must retain its guarded GGX root.");
            Assert.That(partition, Is.GreaterThanOrEqualTo(0)); Assert.That(boundaries[partition + 1], Is.LessThan(1.0d));
            Assert.That(routed.Length, Is.EqualTo(boundaries.Length - 1)); Assert.That(routed[partition], Is.True);
        }

        /// <summary>Routes the normal-visibility transition-containing low-u cross-check prefix through x.</summary>
        [Test]
        public void VisibilityPrefixRoutesNormalTransitionContainingLowUPanelToX()
        {
            const double p = 0.089d; const double v = 0.0d;
            double transition = AdaptiveCrossCheck.GetVisibilityTransitionForTest(p, v, false); double[] boundaries = AdaptiveCrossCheck.GetUPartitionBoundariesForTest(p, v); bool[] routed = AdaptiveCrossCheck.GetVisibilityPrefixXPartitionsForTest(p, v, false);
            int partition = -1;
            for (int index = 0; index < boundaries.Length - 1; index++) if (transition > boundaries[index] && transition < boundaries[index + 1]) partition = index;
            Assert.That(partition, Is.GreaterThanOrEqualTo(0)); Assert.That(routed.Length, Is.EqualTo(boundaries.Length - 1)); Assert.That(routed[partition], Is.True);
        }

        /// <summary>Requires refined cross-check x samples to share, rather than duplicate, their parent budget.</summary>
        [Test]
        public void RefinedCrossCheckXRuleChildBudgetsConserveParentAllocation()
        {
            Assert.That(AdaptiveCrossCheck.RefinedXRuleChildBudgetsConserveParentForTest(0.00004d, 0.0004d), Is.True);
        }

        /// <summary>Requires each split outer child to reuse its parent fine estimate without recomputing its midpoint.</summary>
        [Test]
        public void CrossCheckOuterSplitReusesChildCoarseTauWork()
        {
            AdaptiveCrossCheck.OuterSplitReuseProbe probe = AdaptiveCrossCheck.ProbeOuterSplitReuseForTest();
            Assert.That(probe.ChildCoarseReused, Is.True);
            Assert.That(probe.RightBeforeLeft, Is.True);
            Assert.That(probe.NestedTauIntegrationCalls, Is.EqualTo(7));
        }

        /// <summary>Requires panel-cap rejection to retain the cap count and stop before a later recursive child begins.</summary>
        [Test]
        public void KronrodSchedulerUsesCanonicalPriorityAndHardCaps()
        {
            AdaptiveCrossCheck.PanelCapProbe probe = AdaptiveCrossCheck.ProbePanelCapForTest();
            Assert.That(probe.Panels, Is.EqualTo(1).And.LessThanOrEqualTo(1));
            Assert.That(probe.Evaluations, Is.Zero);
            Assert.That(probe.StartedRecursions, Is.EqualTo(1));
            Assert.That(probe.LaterRecursions, Is.Zero);
            Assert.That(probe.Diagnostic, Does.StartWith("numerical-limit cross-check panels axis=tau outer=0.75 interval=[0.5,1]"));
            Assert.That(probe.Diagnostic, Does.Contain("coarse=0 fine=1 inheritedInnerError=0 ruleDelta=1 absoluteLimit=0.25 relativeLimit=0 error=1 limit=0.25 errorOverLimit=4 depth=1 panels=1 maxPanels=1 evaluations=0 maxEvaluations=1"));
        }

        /// <summary>Requires cross-check evaluation caps to reject before excess sample-kernel work begins.</summary>
        [Test]
        public void CrossCheckEvaluationCapStopsBeforeExcessSampleWork()
        {
            AdaptiveCrossCheck.EvaluationCapProbe probe = AdaptiveCrossCheck.ProbeEvaluationCapForTest();
            Assert.That(probe.Panels, Is.Zero);
            Assert.That(probe.Evaluations, Is.EqualTo(1).And.LessThanOrEqualTo(1));
            Assert.That(probe.SampleKernelWork, Is.EqualTo(1));
            Assert.That(probe.Diagnostic, Does.StartWith("numerical-limit cross-check evaluations axis=tau outer=0.5 interval=[-1,1]"));
            Assert.That(probe.Diagnostic, Does.Contain("coarse=none fine=none inheritedInnerError=none ruleDelta=none absoluteLimit=none relativeLimit=none error=none limit=none errorOverLimit=none depth=0 panels=0 maxPanels=8 evaluations=1 maxEvaluations=1"));
        }

        /// <summary>Requires primary panel and evaluation caps to reject before scheduling excess work.</summary>
        [Test]
        public void PrimaryResourceCapsRejectBeforeWorkExceedsLimits()
        {
            AdaptivePrimary.ResourceCapProbe probe = AdaptivePrimary.ProbeResourceCapsForTest();
            Assert.That(probe.Panels, Is.EqualTo(1).And.LessThanOrEqualTo(1));
            Assert.That(probe.Evaluations, Is.Zero);
            Assert.That(probe.StartedRecursions, Is.EqualTo(1));
            Assert.That(probe.LaterRecursions, Is.Zero);
            Assert.That(probe.PanelDiagnostic, Does.StartWith("numerical-limit primary panels axis=eta outer=0.75 interval=[0,0.5]"));
            Assert.That(probe.PanelDiagnostic, Does.Contain("coarse=0 fine=1 inheritedInnerError=0 ruleDelta=1 absoluteLimit=0.25 relativeLimit=0 error=1 limit=0.25 depth=1 panels=1 maxPanels=1 evaluations=0 maxEvaluations=8"));
            Assert.That(probe.EvaluationPanels, Is.Zero);
            Assert.That(probe.EvaluationCount, Is.EqualTo(1).And.LessThanOrEqualTo(1));
            Assert.That(probe.SampleKernelWork, Is.EqualTo(1));
            Assert.That(probe.EvaluationDiagnostic, Does.StartWith("numerical-limit primary evaluations axis=eta outer=0 interval=[0,1]"));
            Assert.That(probe.EvaluationDiagnostic, Does.Contain("coarse=none fine=none inheritedInnerError=none ruleDelta=none absoluteLimit=none relativeLimit=none error=none limit=none depth=0 panels=0 maxPanels=8 evaluations=1 maxEvaluations=1"));
        }

        /// <summary>Requires both branches to retain finite independent witness results within their existing grazing tolerance.</summary>
        [Test, Timeout(120000)]
        public void KronrodWitnessIsIndependentAndConverged()
        {
            var settings = new AdaptiveSettings("targeted", 0.00004d, 0.0004d, 0.00001d, 0.0001d, 18, 65536, 1000000);
            foreach (bool switchBranch in new[] { false, true })
            {
                AdaptiveResult witness = KronrodWitness.Integrate(settings.Witness(), 0.089d, 0.0d, switchBranch);
                Assert.That(witness.IsAccepted, Is.True, DescribeAdaptiveResult(witness));
                Assert.That(witness.Error, Is.LessThanOrEqualTo(witness.Tolerance));
            }
        }

        /// <summary>Requires a stricter direct-light witness rerun to remain compatible with the selected witness.</summary>
        [Test, Timeout(120000)]
        public void KronrodWitnessStricterRerunRemainsCompatible()
        {
            var selected = new AdaptiveSettings("targeted", 0.00004d, 0.0004d, 0.00001d, 0.0001d, 18, 65536, 1000000).Witness();
            var stricter = new AdaptiveSettings("targeted-stricter", 0.0000025d, 0.000025d, 0.0000025d, 0.000025d, 20, 262144, 4000000);
            foreach (bool switchBranch in new[] { false, true })
            {
                AdaptiveResult witness = KronrodWitness.Integrate(selected, 0.089d, 0.0d, switchBranch);
                AdaptiveResult rerun = KronrodWitness.Integrate(stricter, 0.089d, 0.0d, switchBranch);
                Assert.That(witness.IsAccepted, Is.True, DescribeAdaptiveResult(witness)); Assert.That(rerun.IsAccepted, Is.True, DescribeAdaptiveResult(rerun));
                Assert.That(Math.Abs(witness.Value - rerun.Value), Is.LessThanOrEqualTo(selected.WitnessTolerance(rerun.Value)));
            }
        }

        /// <summary>Formats bounded adaptive evidence when a targeted acceptance assertion fails.</summary>
        private static string DescribeAdaptiveResult(AdaptiveResult result)
        {
            return result.Diagnostic + "; value=" + result.Value.ToString("R") + "; error=" + result.Error.ToString("R") + "; tolerance=" + result.Tolerance.ToString("R") + "; evaluations=" + result.Evaluations + "; panels=" + result.Panels + "; depth=" + result.Depth;
        }

        /// <summary>Proves a listed transition cannot be strictly inside an initial analytic panel.</summary>
        private static void AssertRootIsPanelEdge(double[] boundaries, double root)
        {
            bool edge = false;
            for (int index = 0; index < boundaries.Length; index++) edge |= Math.Abs(boundaries[index] - root) <= 1.0e-13d;
            Assert.That(edge, Is.True, "Every kernel transition must be a panel endpoint.");
            for (int index = 0; index < boundaries.Length - 1; index++) Assert.That(root > boundaries[index] + 1.0e-13d && root < boundaries[index + 1] - 1.0e-13d, Is.False);
        }

        /// <summary>Requires both branches, stricter witnesses, and downstream fit stability before selection.</summary>
        [Test, Timeout(900000)]
        public void KronrodOracleSelectsStableProtocol()
        {
            AdaptiveSelection selection = PureBasePbrMultipleScatteringFurnaceOracle.Selected;
            Assert.That(selection.IsSelected, Is.True);
            Assert.That(selection.StressStable, Is.True);
            Assert.That(selection.Normal.Passes, Is.True);
            Assert.That(selection.Switch.Passes, Is.True);
        }

        /// <summary>Requires the selected v3 numerical protocol artifact to reproduce exactly in memory.</summary>
        [Test, Timeout(900000)]
        public void KronrodOracleRecordReproducesExactly()
        {
            string artifact = PureBasePbrMultipleScatteringFurnaceOracle.BuildArtifact(PureBasePbrMultipleScatteringFurnaceOracle.Selected);
            TestContext.Progress.WriteLine("PBR multiple-scattering oracle artifact follows:\n" + artifact);
            Assert.That(File.Exists(AdaptiveProtocol.CanonicalArtifactPath), Is.True, "The strict pbr-multiple-scattering-kronrod-oracle-v4.json artifact is required after selection.");
            byte[] expected = AdaptiveProtocol.CanonicalArtifactBytesForTest(artifact); byte[] actual = File.ReadAllBytes(AdaptiveProtocol.CanonicalArtifactPath);
            Assert.That(actual, Is.EqualTo(expected), "Daily must reproduce the selected numerical oracle artifact byte-for-byte.");
            Assert.That(AdaptiveProtocol.IsCanonicalArtifactBytesForTest(actual), Is.True, "Daily must retain the strict ASCII, no-BOM, LF-only record format.");
        }

        /// <summary>Requires deterministic record bytes, scheduler identity, thresholds, and fit evidence without selection.</summary>
        [Test]
        public void AdaptiveProtocolRecordFormatAndIdentityAreStrict()
        {
            AdaptiveSelection selection = CreateDeterministicRecordSelection(); string artifact = AdaptiveProtocol.BuildArtifact(selection); byte[] bytes = AdaptiveProtocol.CanonicalArtifactBytesForTest(artifact);
            Assert.That(bytes, Is.EqualTo(new UTF8Encoding(false).GetBytes(artifact)));
            Assert.That(AdaptiveProtocol.IsCanonicalArtifactBytesForTest(bytes), Is.True);
            Assert.That(AdaptiveProtocol.IsCanonicalArtifactBytesForTest(WithBom(bytes)), Is.False);
            Assert.That(AdaptiveProtocol.IsCanonicalArtifactBytesForTest(WithCarriageReturn(bytes)), Is.False);
            Assert.That(AdaptiveProtocol.IsCanonicalArtifactBytesForTest(WithNonAscii(bytes)), Is.False);
            Assert.That(AdaptiveProtocol.IsCanonicalArtifactBytesForTest(WithoutTerminalLf(bytes)), Is.False);
            Assert.That(artifact, Does.Contain("\"schedulers\": { \"primary\": \"left-before-right-depth-first\", \"crossCheck\": \"right-before-left-depth-first\" }"));
            Assert.That(artifact, Does.Contain("\"selectedWitnessDifference\": 0.0001, \"crossDifference\": 0.001"));
            Assert.That(artifact, Does.Contain("\"selectedP95\": 0.004, \"selectedMaximum\": 0.008, \"witnessP95\": 0.0042, \"witnessMaximum\": 0.0082, \"highRoughnessImproves\": true"));
        }

        /// <summary>Requires the v4 record to retain complete immutable evidence and regenerate raw bytes exactly.</summary>
        [Test]
        public void AdaptiveProtocolRecordIsCompleteAndRegeneratesRawBytes()
        {
            AdaptiveSelection selection = CreateDeterministicRecordSelection();
            string first = AdaptiveProtocol.BuildArtifact(selection);
            string second = AdaptiveProtocol.BuildArtifact(selection);
            byte[] firstBytes = AdaptiveProtocol.CanonicalArtifactBytesForTest(first);
            byte[] secondBytes = AdaptiveProtocol.CanonicalArtifactBytesForTest(second);
            Assert.That(firstBytes, Is.EqualTo(secondBytes));
            Assert.That(AdaptiveProtocol.IsCanonicalArtifactBytesForTest(firstBytes), Is.True);
            Assert.That(first, Does.Contain("\"grids\": { \"original\": { \"order\": \"p-then-ndotV-row-major\""));
            Assert.That(first, Does.Contain("\"embeddedRule\": { \"k31\": { \"nodes\":"));
            Assert.That(first, Does.Contain("\"g15\": { \"subset\":"));
            Assert.That(first, Does.Contain("\"identities\": { \"primary\": { \"transform\":"));
            Assert.That(first, Does.Contain("\"jacobian\": \"embedded in eta transform\""));
            Assert.That(first, Does.Contain("\"candidateLadder\": [{ \"settings\":"));
            Assert.That(first, Does.Contain("\"primaryWitnessDifference\":"));
            Assert.That(first, Does.Contain("\"stopState\": \"accepted\""));
            Assert.That(first, Does.Not.Contain("\"runtime\"").And.Not.Contain("\"host\"").And.Not.Contain("\"elapsed\""));
        }

        /// <summary>Creates selected in-memory evidence for strict serialization without running numerical selection.</summary>
        private static AdaptiveSelection CreateDeterministicRecordSelection()
        {
            var settings = new AdaptiveSettings("serialization-probe", 0.00004d, 0.0004d, 0.00001d, 0.0001d, 18, 65536, 1000000);
            var result = new AdaptiveResult(0.5d, 0.00001d, 0.00024d, 3, 2, 1, null); var evidence = new AdaptiveEvidence(new AdaptiveCoordinate(0.089d, 0.0d), result, result, result);
            var fit = new AdaptiveFit(0.0001f, 0.0002f, 0.004f, 0.008f, 0.0042f, 0.0082f, true); var branch = new AdaptiveBranch(new[] { evidence }, fit, true, true);
            return new AdaptiveSelection(settings, branch, branch, true, true);
        }

        /// <summary>Prefixes the bytes with a UTF-8 BOM for strict-format rejection coverage.</summary>
        private static byte[] WithBom(byte[] bytes)
        {
            var result = new byte[bytes.Length + 3]; result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF; Buffer.BlockCopy(bytes, 0, result, 3, bytes.Length); return result;
        }

        /// <summary>Replaces one LF with a CR for strict-format rejection coverage.</summary>
        private static byte[] WithCarriageReturn(byte[] bytes)
        {
            byte[] result = (byte[])bytes.Clone(); for (int index = 0; index < result.Length; index++) if (result[index] == (byte)'\n') { result[index] = (byte)'\r'; break; } return result;
        }

        /// <summary>Injects one non-ASCII byte for strict-format rejection coverage.</summary>
        private static byte[] WithNonAscii(byte[] bytes)
        {
            byte[] result = (byte[])bytes.Clone(); result[0] = 0x80; return result;
        }

        /// <summary>Removes the terminal LF for strict-format rejection coverage.</summary>
        private static byte[] WithoutTerminalLf(byte[] bytes)
        {
            var result = new byte[bytes.Length - 1]; Buffer.BlockCopy(bytes, 0, result, 0, result.Length); return result;
        }

    }
}
