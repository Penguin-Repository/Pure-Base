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

// Implements the independent light-space tan-half-angle adaptive furnace cross-check.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PureBase.Tests.Daily
{
    /// <summary>Implements the independent light-space tan-half-angle adaptive furnace cross-check.</summary>
    internal static partial class AdaptiveCrossCheck
    {
        internal static readonly AdaptiveIdentity Identity = new AdaptiveIdentity("u=NdotL; x=sqrt(u) visibility-prefix, u=x^2, du=2*x dx; tau=tan((phi-pi)/2); q0>guard2 uses sigma2=((q0-(1-p4)*A)*q0)/(4*s*(1-p4)*A), y=asinh(tau/sigma), z=y/(1+abs(y)), tau=sigma*sinh(z/(1-abs(z))), dphi/dz=2*sigma*cosh(y)/((1+tau2)*(1-abs(z))^2); otherwise z=tau/(1+abs(tau)); phi=pi+2*atan(tau)", "analytic u roots of NdotL=0 and guarded GGX clamp plus active-epsilon visibility-prefix selection and analytic tau roots of safe-normalize and GGX clamp mapped through the active tau chart", "fixed binary64 embedded G15/K31 on every atomic chart interval", "largest indicator first; tie rootSegmentId, depth, binaryPath; left child first", "Kahan completed-leaf accumulator");
        private static readonly double[] KronrodNodes = { -0.9980022986933971d, -0.9879925180204854d, -0.9677390756791391d, -0.937273392400706d, -0.8972645323440819d, -0.8482065834104272d, -0.7904185014424659d, -0.72441773136017d, -0.650996741297417d, -0.5709721726085388d, -0.4850818636402397d, -0.3941513470775634d, -0.2991800071531688d, -0.2011940939974345d, -0.1011420669187175d, 0.0d, 0.1011420669187175d, 0.2011940939974345d, 0.2991800071531688d, 0.3941513470775634d, 0.4850818636402397d, 0.5709721726085388d, 0.650996741297417d, 0.72441773136017d, 0.7904185014424659d, 0.8482065834104272d, 0.8972645323440819d, 0.937273392400706d, 0.9677390756791391d, 0.9879925180204854d, 0.9980022986933971d };
        private static readonly double[] KronrodWeights = { 0.005377479872923349d, 0.015007947329316123d, 0.02546084732671532d, 0.03534636079137585d, 0.04458975132476488d, 0.05348152469092809d, 0.06200956780067064d, 0.06985412131872826d, 0.07684968075772038d, 0.08308050282313302d, 0.08856444305621177d, 0.09312659817082532d, 0.09664272698362368d, 0.09814752051373843d, 0.09917359872179196d, 0.09972054479342645d, 0.09917359872179196d, 0.09814752051373843d, 0.09664272698362368d, 0.09312659817082532d, 0.08856444305621177d, 0.08308050282313302d, 0.07684968075772038d, 0.06985412131872826d, 0.06200956780067064d, 0.05348152469092809d, 0.04458975132476488d, 0.03534636079137585d, 0.02546084732671532d, 0.015007947329316123d, 0.005377479872923349d };
        private static readonly int[] GaussSubset = { 1, 3, 5, 7, 9, 11, 13, 15, 17, 19, 21, 23, 25, 27, 29 };
        private static readonly double[] GaussWeights = { 0.03075324199611727d, 0.07036604748810812d, 0.1071592204671719d, 0.1395706779261543d, 0.1662692058169939d, 0.1861610000155622d, 0.1984314853271116d, 0.2025782419255613d, 0.1984314853271116d, 0.1861610000155622d, 0.1662692058169939d, 0.1395706779261543d, 0.1071592204671719d, 0.07036604748810812d, 0.03075324199611727d };

        /// <summary>Integrates with path-private mapping, panels, scheduler, rule, and accumulator.</summary>
        internal static AdaptiveResult Integrate(AdaptiveSettings settings, double p, double v, bool switchBranch)
        {
            return Integrate(settings, p, v, switchBranch, null, default);
        }

        /// <summary>Integrates with an optional selection-wide scalar-kernel reservation context.</summary>
        internal static AdaptiveResult Integrate(AdaptiveSettings settings, double p, double v, bool switchBranch, SelectionExecutionBudget budget, SelectionExecutionContext context)
        {
            var state = new State(settings, p, v, switchBranch, budget, context); double[] boundaries = UBoundaries(p, v); var total = new EstimateSum();
            for (int index = 0; index < boundaries.Length - 1 && !state.Failed; index++) { double left = boundaries[index]; double right = boundaries[index + 1]; double width = right - left; total.Add(UsesVisibilityPrefixX(p, v, switchBranch, left, right) ? IntegrateX(state, Math.Sqrt(left), Math.Sqrt(right), 0, settings.Absolute * width, width) : IntegrateU(state, left, right, 0, settings.Absolute * width, width)); }
            return state.Result(total.Value);
        }

        /// <summary>Gets the deterministic light-cosine boundaries used by the cross-check.</summary>
        internal static double[] GetUPartitionBoundariesForTest(double p, double v) => UBoundaries(p, v);

        /// <summary>Gets the active visibility transition when it is a strict light-space interior point.</summary>
        internal static double GetVisibilityTransitionForTest(double p, double v, bool switchBranch) => TryGetVisibilityTransition(p, v, switchBranch, out double transition) ? transition : double.NaN;

        /// <summary>Gets whether each initial light-cosine partition uses the fixed visibility-prefix x transform.</summary>
        internal static bool[] GetVisibilityPrefixXPartitionsForTest(double p, double v, bool switchBranch)
        {
            double[] boundaries = UBoundaries(p, v); var values = new bool[boundaries.Length - 1];
            for (int index = 0; index < values.Length; index++) values[index] = UsesVisibilityPrefixX(p, v, switchBranch, boundaries[index], boundaries[index + 1]);
            return values;
        }

        /// <summary>Gets every untransformed finite tau root introduced for the GGX denominator clamp.</summary>
        internal static double[] GetDistributionTauRootsForTest(double p, double v, double u) => DistributionTauRoots(p, v, u);

        /// <summary>Gets the deterministic active-chart boundaries used for one cross-check light cosine.</summary>
        internal static double[] GetTauPartitionBoundariesForTest(double p, double v, double u) => TauBoundaries(p, v, u);

        /// <summary>Maps one finite tau value through the active chart for a cross-check light cosine.</summary>
        internal static double MapTauToChartForTest(double p, double v, double u, double tau) => TauChart.Create(p, v, u).FromTau(tau);

        /// <summary>Gets clones of the canonical embedded-rule tables in ascending evaluation order.</summary>
        internal static KronrodRuleProbe GetKronrodRuleForTest() => new KronrodRuleProbe((double[])KronrodNodes.Clone(), (double[])KronrodWeights.Clone(), (int[])GaussSubset.Clone(), (double[])GaussWeights.Clone());

        /// <summary>Verifies refined x-rule child allocations preserve their parent error budget.</summary>
        internal static bool RefinedXRuleChildBudgetsConserveParentForTest(double absoluteBudget, double relativeShare)
        {
            var child = RefinedXRuleChildBudget(absoluteBudget, relativeShare);
            return child.AbsoluteBudget + child.AbsoluteBudget == absoluteBudget && child.RelativeShare + child.RelativeShare == relativeShare;
        }

        /// <summary>Splits the low-u grazing lobe before the independent adaptive light integration.</summary>
        private static double[] UBoundaries(double p, double v)
        {
            double p2 = p * p; var values = new List<double> { 0.0d, Math.Min(1.0d, p2), Math.Min(1.0d, 4.0d * p2), 0.125d, 0.5d, 1.0d };
            if (AdaptiveProtocol.TryGetDistributionNdotHSquared(p, out double ndotHSquared)) AddU(values, Math.Sqrt(ndotHSquared * AdaptiveProtocol.GuardSquared) - v);
            values.Sort(); for (int index = values.Count - 2; index >= 0; index--) if (values[index] == values[index + 1]) values.RemoveAt(index + 1); return values.ToArray();
        }

        /// <summary>Adds one strict light-cosine interior boundary.</summary>
        private static void AddU(List<double> values, double u) { if (u > 0.0d && u < 1.0d && PureBasePbrMultipleScatteringReference.IsFinite(u)) values.Add(u); }

        /// <summary>Returns whether a retained low-u partition belongs to the active visibility-prefix transform.</summary>
        private static bool UsesVisibilityPrefixX(double p, double v, bool switchBranch, double left, double right)
        {
            double prefixEnd = Math.Min(1.0d, p * p);
            return TryGetVisibilityTransition(p, v, switchBranch, out double transition) && transition < prefixEnd && left >= 0.0d && right <= prefixEnd;
        }

        /// <summary>Finds the active normal-visibility denominator transition in the light-cosine domain.</summary>
        private static bool TryGetVisibilityTransition(double p, double v, bool switchBranch, out double transition)
        {
            double a = p * p; double epsilon = switchBranch ? PureBasePbrMultipleScatteringReference.SwitchEpsilon : PureBasePbrMultipleScatteringReference.NormalEpsilon; double denominator = a + 2.0d * v * (1.0d - a);
            transition = (epsilon - a * v) / denominator;
            return denominator != 0.0d && PureBasePbrMultipleScatteringReference.IsFinite(transition) && transition > 0.0d && transition < 1.0d;
        }

        /// <summary>Recursively integrates normal-light cosine with a private midpoint embedded rule.</summary>
        private static AdaptiveEstimate IntegrateU(State state, double left, double right, int depth, double absoluteBudget, double relativeShare)
        {
            if (state.Failed) return default;
            AdaptiveEstimate coarse = URule(state, left, right, absoluteBudget, relativeShare);
            return state.Failed ? coarse : IntegrateU(state, left, right, depth, absoluteBudget, relativeShare, coarse);
        }

        /// <summary>Refines one light-cosine panel from its parent-provided midpoint estimate.</summary>
        private static AdaptiveEstimate IntegrateU(State state, double left, double right, int depth, double absoluteBudget, double relativeShare, AdaptiveEstimate coarse)
        {
            if (state.Failed) return coarse;
            OuterFineEstimate fine = RefinedURule(state, left, right, absoluteBudget, relativeShare);
            return state.Failed ? fine.Value : state.Split(coarse, fine, "u", double.NaN, left, right, depth, absoluteBudget, relativeShare, IntegrateU);
        }

        /// <summary>Recursively integrates the fixed low-u visibility prefix through u=x squared.</summary>
        private static AdaptiveEstimate IntegrateX(State state, double left, double right, int depth, double absoluteBudget, double relativeShare)
        {
            if (state.Failed) return default;
            AdaptiveEstimate coarse = XRule(state, left, right, absoluteBudget, relativeShare);
            return state.Failed ? coarse : IntegrateX(state, left, right, depth, absoluteBudget, relativeShare, coarse);
        }

        /// <summary>Refines one visibility-prefix panel from its parent-provided midpoint estimate.</summary>
        private static AdaptiveEstimate IntegrateX(State state, double left, double right, int depth, double absoluteBudget, double relativeShare, AdaptiveEstimate coarse)
        {
            if (state.Failed) return coarse;
            OuterFineEstimate fine = RefinedXRule(state, left, right, absoluteBudget, relativeShare);
            return state.Failed ? fine.Value : state.Split(coarse, fine, "x", double.NaN, left, right, depth, absoluteBudget, relativeShare, IntegrateX);
        }

        /// <summary>Evaluates a light-cosine rule using independently split finite tau subintervals.</summary>
        private static AdaptiveEstimate URule(State state, double left, double right, double absoluteBudget, double relativeShare)
        {
            double width = right - left;
            return AdaptiveEstimate.Scale(IntegrateTauPartitions(state, (left + right) * 0.5d, absoluteBudget / width, relativeShare / width), width);
        }

        /// <summary>Evaluates both outer child estimates so each becomes its matching recursive coarse estimate.</summary>
        private static OuterFineEstimate RefinedURule(State state, double left, double right, double absoluteBudget, double relativeShare)
        {
            double width = right - left;
            double middle = (left + right) * 0.5d; double scale = width * 0.5d;
            AdaptiveEstimate lower = AdaptiveEstimate.Scale(IntegrateTauPartitions(state, (left + middle) * 0.5d, absoluteBudget / width, relativeShare / width), scale);
            if (state.Failed) return new OuterFineEstimate(lower, default);
            AdaptiveEstimate upper = AdaptiveEstimate.Scale(IntegrateTauPartitions(state, (middle + right) * 0.5d, absoluteBudget / width, relativeShare / width), scale);
            return new OuterFineEstimate(lower, upper);
        }

        /// <summary>Evaluates the x-space embedded rule and carries du=2*x dx into each weighted tau estimate.</summary>
        private static AdaptiveEstimate XRule(State state, double left, double right, double absoluteBudget, double relativeShare)
        {
            double width = right - left;
            return XSample(state, (left + right) * 0.5d, width, absoluteBudget, relativeShare);
        }

        /// <summary>Evaluates both x child estimates so each becomes its matching recursive coarse estimate.</summary>
        private static OuterFineEstimate RefinedXRule(State state, double left, double right, double absoluteBudget, double relativeShare)
        {
            double width = right - left;
            double middle = (left + right) * 0.5d; var child = RefinedXRuleChildBudget(absoluteBudget, relativeShare); AdaptiveEstimate lower = XSample(state, (left + middle) * 0.5d, width * 0.5d, child.AbsoluteBudget, child.RelativeShare);
            if (state.Failed) return new OuterFineEstimate(lower, default);
            AdaptiveEstimate upper = XSample(state, (middle + right) * 0.5d, width * 0.5d, child.AbsoluteBudget, child.RelativeShare);
            return new OuterFineEstimate(lower, upper);
        }

        /// <summary>Divides one refined x-rule error budget equally between its two samples.</summary>
        private static (double AbsoluteBudget, double RelativeShare) RefinedXRuleChildBudget(double absoluteBudget, double relativeShare) => (absoluteBudget * 0.5d, relativeShare * 0.5d);

        /// <summary>Weights one x-space sample while preserving its full local inner-error allocation.</summary>
        private static AdaptiveEstimate XSample(State state, double x, double width, double absoluteBudget, double relativeShare)
        {
            if (state.Failed) return default;
            double jacobian = 2.0d * x; double weight = width * jacobian;
            return AdaptiveEstimate.Scale(IntegrateTauPartitions(state, x * x, absoluteBudget / weight, relativeShare / weight), weight);
        }

        /// <summary>Splits the active finite tau chart at its analytic guard-curve roots.</summary>
        private static AdaptiveEstimate IntegrateTauPartitions(State state, double u, double absoluteBudget, double relativeShare)
        {
            if (state.Failed) return default;
            TauChart chart = TauChart.Create(state.P, state.V, u); double[] boundaries = TauBoundaries(state.P, state.V, u, chart); var total = new EstimateSum();
            for (int index = 0; index < boundaries.Length - 1 && !state.Failed; index++) { double share = (boundaries[index + 1] - boundaries[index]) * 0.5d; total.Add(IntegrateTau(state, u, chart, boundaries[index], boundaries[index + 1], index, absoluteBudget * share, relativeShare * share)); }
            return total.Value;
        }

        /// <summary>Integrates one root-bounded chart interval through the independent embedded leaf scheduler.</summary>
        private static AdaptiveEstimate IntegrateTau(State state, double u, TauChart chart, double left, double right, int rootSegmentId, double absoluteBudget, double relativeShare)
        {
            var pending = new List<KronrodLeaf> { EvaluateLeaf(state, u, chart, left, right, rootSegmentId, 0, "", absoluteBudget, relativeShare) };
            var completed = new List<KronrodLeaf>();
            while (pending.Count != 0 && !state.Failed)
            {
                pending.Sort(KronrodLeaf.Compare); KronrodLeaf leaf = pending[0]; pending.RemoveAt(0);
                if (leaf.Indicator <= leaf.Limit) { completed.Add(leaf); continue; }
                if (leaf.Depth >= state.MaxDepth) { state.Fail("numerical-limit cross-check depth axis=tau rootSegmentId=" + leaf.RootSegmentId); break; }
                double middle = (leaf.Left + leaf.Right) * 0.5d;
                pending.Add(EvaluateLeaf(state, u, chart, leaf.Left, middle, rootSegmentId, leaf.Depth + 1, leaf.Path + "0", absoluteBudget * 0.5d, relativeShare * 0.5d));
                if (!state.Failed) pending.Add(EvaluateLeaf(state, u, chart, middle, leaf.Right, rootSegmentId, leaf.Depth + 1, leaf.Path + "1", absoluteBudget * 0.5d, relativeShare * 0.5d));
            }

            if (state.Failed) return default;
            completed.Sort(KronrodLeaf.CanonicalCompare); var sum = new Sum(); double error = 0.0d;
            foreach (KronrodLeaf leaf in completed) { sum.Add(leaf.Kronrod); error += leaf.Indicator; }
            return new AdaptiveEstimate(sum.Value, error);
        }

        /// <summary>Exercises one outer split and records its carried midpoint work for the accounting contract.</summary>
        internal static OuterSplitReuseProbe ProbeOuterSplitReuseForTest()
        {
            var state = new State(new AdaptiveSettings("outer-reuse-probe", 0.0d, 0.0d, 0.0d, 0.0d, 1, int.MaxValue, int.MaxValue), 0.5d, 0.5d, false);
            var lower = new AdaptiveEstimate(2.0d, 0.0d); var upper = new AdaptiveEstimate(3.0d, 0.0d); int nestedTauIntegrations = 3; int childIndex = 0; bool reuseMatches = true; bool rightBeforeLeft = true;
            state.Split(new AdaptiveEstimate(0.0d, 0.0d), new OuterFineEstimate(lower, upper), "u", double.NaN, 0.0d, 1.0d, 0, 1.0d, 1.0d, (current, left, right, depth, absoluteBudget, relativeShare, coarse) =>
            {
                bool upperChild = childIndex++ == 0; double expectedLeft = upperChild ? 0.5d : 0.0d; double expectedRight = upperChild ? 1.0d : 0.5d; AdaptiveEstimate expectedCoarse = upperChild ? upper : lower;
                reuseMatches &= left == expectedLeft && right == expectedRight && depth == 1 && absoluteBudget == 0.5d && relativeShare == 0.5d && coarse.Value == expectedCoarse.Value && coarse.Error == expectedCoarse.Error;
                rightBeforeLeft &= upperChild ? left == 0.5d : left == 0.0d; nestedTauIntegrations += 2;
                return coarse;
            });
            return new OuterSplitReuseProbe(reuseMatches && childIndex == 2, rightBeforeLeft, nestedTauIntegrations);
        }

        /// <summary>Forces a panel-cap rejection after one permitted outer split without entering later work.</summary>
        internal static PanelCapProbe ProbePanelCapForTest()
        {
            var state = new State(new AdaptiveSettings("panel-cap-probe", 0.0d, 0.0d, 0.0d, 0.0d, 4, 1, 1), 0.5d, 0.5d, false);
            int startedRecursions = 0; int laterRecursions = 0;
            state.Split(new AdaptiveEstimate(0.0d, 0.0d), new AdaptiveEstimate(1.0d, 0.0d), "u", double.NaN, 0.0d, 1.0d, 0, 0.5d, 1.0d, (current, left, right, depth, absoluteBudget, relativeShare) =>
            {
                startedRecursions++;
                return current.Split(new AdaptiveEstimate(0.0d, 0.0d), new AdaptiveEstimate(1.0d, 0.0d), "tau", 0.75d, left, right, depth, absoluteBudget, relativeShare, (next, childLeft, childRight, childDepth, childBudget, childShare) => { laterRecursions++; return default; });
            });
            return new PanelCapProbe(state.Panels, state.Evaluations, startedRecursions, laterRecursions, state.Failure);
        }

        /// <summary>Forces an evaluation-cap rejection before a second sample kernel can begin.</summary>
        internal static EvaluationCapProbe ProbeEvaluationCapForTest()
        {
            var state = new State(new AdaptiveSettings("evaluation-cap-probe", 0.0d, 0.0d, 0.0d, 0.0d, 4, 8, 1), 0.5d, 0.5d, false);
            TauChart chart = TauChart.Create(0.5d, 0.5d, 0.5d);
            Sample(state, 0.5d, chart, 0.0d, "tau", -1.0d, 1.0d, 0);
            Sample(state, 0.5d, chart, 0.0d, "tau", -1.0d, 1.0d, 0);
            return new EvaluationCapProbe(state.Panels, state.Evaluations, state.SampleKernelWork, state.Failure);
        }

        /// <summary>Evaluates one root-bounded atomic chart interval with the fixed G15/K31 pair.</summary>
        private static KronrodLeaf EvaluateLeaf(State state, double u, TauChart chart, double left, double right, int rootSegmentId, int depth, string path, double absoluteBudget, double relativeShare)
        {
            if (!state.BeginKronrodPanel(u, left, right, depth)) return default;
            double center = (left + right) * 0.5d; double half = (right - left) * 0.5d; var kronrod = new Sum(); var gauss = new Sum(); int gaussIndex = 0;
            for (int index = 0; index < KronrodNodes.Length && !state.Failed; index++)
            {
                double value = Sample(state, u, chart, center + half * KronrodNodes[index], "tau", left, right, depth); kronrod.Add(KronrodWeights[index] * value);
                if (gaussIndex < GaussSubset.Length && GaussSubset[gaussIndex] == index) { gauss.Add(GaussWeights[gaussIndex] * value); gaussIndex++; }
            }

            double k31 = half * kronrod.Value; double g15 = half * gauss.Value; double limit = absoluteBudget + state.Relative * Math.Abs(k31) * relativeShare;
            return new KronrodLeaf(left, right, rootSegmentId, depth, path, k31, Math.Abs(k31 - g15), limit);
        }

        /// <summary>Rebuilds light and view, then evaluates the shared exact HLSL-equivalent scalar kernel.</summary>
        private static double Sample(State state, double u, TauChart chart, double coordinate, string axis, double left, double right, int depth)
        {
            if (state.Failed) return 0.0d;
            if (!state.Evaluate(axis, u, left, right, depth)) return 0.0d;
            state.RecordSampleKernelWork(); double tau = chart.ToTau(coordinate); double phi = Math.PI + 2.0d * Math.Atan(tau); double dPhi = chart.PhiJacobian(coordinate, tau); double sinL = Math.Sqrt(Math.Max(0.0d, 1.0d - u * u));
            var light = new PureBasePbrMultipleScatteringReference.Direction(sinL * Math.Cos(phi), sinL * Math.Sin(phi), u);
            PureBasePbrMultipleScatteringReference.GuardedTerms terms = PureBasePbrMultipleScatteringReference.EvaluateGuardedTerms(light, state.View, state.P, state.SwitchBranch); double value = terms.Distribution * terms.Visibility * u * dPhi;
            if (!PureBasePbrMultipleScatteringReference.IsFinite(value)) state.Fail("nonfinite cross-check sample"); return value;
        }

        /// <summary>Finds active-chart roots of the safe-normalize and GGX clamp curves.</summary>
        private static double[] TauBoundaries(double p, double v, double u)
        {
            return TauBoundaries(p, v, u, TauChart.Create(p, v, u));
        }

        /// <summary>Finds active-chart roots of the safe-normalize and GGX clamp curves with a retained chart.</summary>
        private static double[] TauBoundaries(double p, double v, double u, TauChart chart)
        {
            double sinL = Math.Sqrt(Math.Max(0.0d, 1.0d - u * u)); double denominator = sinL * Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)); var values = new List<double> { -1.0d, 1.0d };
            if (denominator > 0.0d) AddCosineRoots(values, denominator, (AdaptiveProtocol.GuardSquared * 0.5d - 1.0d - u * v) / denominator, chart);
            foreach (double tau in DistributionTauRoots(p, v, u)) AddTau(values, tau, chart);
            values.Sort(); return values.ToArray();
        }

        /// <summary>Finds untransformed finite tau roots introduced by the GGX denominator clamp.</summary>
        private static double[] DistributionTauRoots(double p, double v, double u)
        {
            var values = new List<double>();
            if (!AdaptiveProtocol.TryGetDistributionNdotHSquared(p, out double ndotHSquared)) return values.ToArray();
            double denominator = Math.Sqrt(Math.Max(0.0d, 1.0d - u * u)) * Math.Sqrt(Math.Max(0.0d, 1.0d - v * v));
            double cosine = ((u + v) * (u + v) / (2.0d * ndotHSquared) - 1.0d - u * v) / denominator;
            AddUntransformedCosineRoots(values, denominator, cosine); values.Sort(); return values.ToArray();
        }

        /// <summary>Adds both active-chart azimuths for one strict cosine root.</summary>
        private static void AddCosineRoots(List<double> values, double denominator, double cosine, TauChart chart)
        {
            if (denominator <= 0.0d || cosine <= -1.0d || cosine >= 1.0d) return; double phi = Math.Acos(cosine); AddTau(values, Math.Tan((phi - Math.PI) * 0.5d), chart); AddTau(values, Math.Tan((Math.PI - phi) * 0.5d), chart);
        }

        /// <summary>Adds both untransformed tau azimuths for one strict cosine root.</summary>
        private static void AddUntransformedCosineRoots(List<double> values, double denominator, double cosine)
        {
            if (denominator <= 0.0d || cosine <= -1.0d || cosine >= 1.0d) return; double phi = Math.Acos(cosine); AddUntransformedTau(values, Math.Tan((phi - Math.PI) * 0.5d)); AddUntransformedTau(values, Math.Tan((Math.PI - phi) * 0.5d));
        }

        /// <summary>Maps one finite tau value to one strict active-chart integration boundary.</summary>
        private static void AddTau(List<double> values, double tau, TauChart chart)
        {
            double coordinate = chart.FromTau(tau);
            if (!PureBasePbrMultipleScatteringReference.IsFinite(coordinate) || coordinate <= -1.0d || coordinate >= 1.0d) return; foreach (double value in values) if (Math.Abs(value - coordinate) < 1.0e-13d) return; values.Add(coordinate);
        }

        /// <summary>Adds one strict finite untransformed tau root.</summary>
        private static void AddUntransformedTau(List<double> values, double tau)
        {
            if (!PureBasePbrMultipleScatteringReference.IsFinite(tau)) return; foreach (double value in values) if (Math.Abs(value - tau) < 1.0e-13d) return; values.Add(tau);
        }

        /// <summary>Maps finite tan-half-angle coordinates through the conditional ridge-aware chart.</summary>
        private readonly struct TauChart
        {
            private readonly bool ridgeAware; private readonly double sigma;

            private TauChart(double sigma) { ridgeAware = true; this.sigma = sigma; }

            /// <summary>Creates the ridge-aware chart only for a nondegenerate guarded light-space configuration.</summary>
            internal static TauChart Create(double p, double v, double u)
            {
                double sinL = Math.Sqrt(Math.Max(0.0d, 1.0d - u * u)); double sinV = Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)); double s = sinL * sinV; double sum = u + v; double a = sum * sum;
                double q0 = 2.0d * (1.0d + u * v - s); double qInfinity = 2.0d * (1.0d + u * v + s); double p2 = p * p; double p4 = p2 * p2; double denominator = 4.0d * s * (1.0d - p4) * a;
                double numerator = (q0 - (1.0d - p4) * a) * q0; double sigmaSquared = numerator / denominator;
                if (!(q0 > AdaptiveProtocol.GuardSquared && qInfinity > q0 && denominator > 0.0d && sigmaSquared > 0.0d && PureBasePbrMultipleScatteringReference.IsFinite(sigmaSquared))) return default;
                double sigma = Math.Sqrt(sigmaSquared); return sigma > 0.0d && PureBasePbrMultipleScatteringReference.IsFinite(sigma) ? new TauChart(sigma) : default;
            }

            /// <summary>Maps an untransformed finite tau value to the active finite chart coordinate.</summary>
            internal double FromTau(double tau)
            {
                if (!ridgeAware) return tau / (1.0d + Math.Abs(tau));
                double y = Math.Asinh(tau / sigma); return y / (1.0d + Math.Abs(y));
            }

            /// <summary>Maps an active finite chart coordinate back to its tan-half-angle value.</summary>
            internal double ToTau(double coordinate)
            {
                double denominator = Math.Max(1.0e-12d, 1.0d - Math.Abs(coordinate));
                if (!ridgeAware) return coordinate / denominator;
                double y = coordinate / denominator; return sigma * Math.Sinh(y);
            }

            /// <summary>Gets the azimuth Jacobian for an active finite chart coordinate and tau value.</summary>
            internal double PhiJacobian(double coordinate, double tau)
            {
                double denominator = Math.Max(1.0e-12d, 1.0d - Math.Abs(coordinate));
                if (!ridgeAware) return 2.0d / ((1.0d + tau * tau) * denominator * denominator);
                double y = coordinate / denominator; return 2.0d * sigma * Math.Cosh(y) / ((1.0d + tau * tau) * denominator * denominator);
            }
        }

        /// <summary>Retains each refined outer child estimate for its matching recursive coarse rule.</summary>
        private readonly struct OuterFineEstimate
        {
            internal OuterFineEstimate(AdaptiveEstimate lower, AdaptiveEstimate upper) { Lower = lower; Upper = upper; }
            internal AdaptiveEstimate Lower { get; }
            internal AdaptiveEstimate Upper { get; }
            internal AdaptiveEstimate Value => new AdaptiveEstimate(Lower.Value + Upper.Value, Lower.Error + Upper.Error);
        }

        /// <summary>Exposes immutable embedded-rule data for canonical contract tests.</summary>
        internal readonly struct KronrodRuleProbe
        {
            internal KronrodRuleProbe(double[] nodes, double[] kronrodWeights, int[] gaussSubset, double[] gaussWeights) { Nodes = nodes; KronrodWeights = kronrodWeights; GaussSubset = gaussSubset; GaussWeights = gaussWeights; }
            internal double[] Nodes { get; }
            internal double[] KronrodWeights { get; }
            internal int[] GaussSubset { get; }
            internal double[] GaussWeights { get; }
        }

        /// <summary>Stores one independently evaluated atomic K31/G15 chart interval.</summary>
        private readonly struct KronrodLeaf
        {
            internal KronrodLeaf(double left, double right, int rootSegmentId, int depth, string path, double kronrod, double indicator, double limit) { Left = left; Right = right; RootSegmentId = rootSegmentId; Depth = depth; Path = path; Kronrod = kronrod; Indicator = indicator; Limit = limit; }
            internal double Left { get; }
            internal double Right { get; }
            internal int RootSegmentId { get; }
            internal int Depth { get; }
            internal string Path { get; }
            internal double Kronrod { get; }
            internal double Indicator { get; }
            internal double Limit { get; }
            internal static int Compare(KronrodLeaf left, KronrodLeaf right)
            {
                int indicator = right.Indicator.CompareTo(left.Indicator); if (indicator != 0) return indicator;
                int root = left.RootSegmentId.CompareTo(right.RootSegmentId); if (root != 0) return root;
                int depth = left.Depth.CompareTo(right.Depth); return depth != 0 ? depth : string.CompareOrdinal(left.Path, right.Path);
            }
            internal static int CanonicalCompare(KronrodLeaf left, KronrodLeaf right)
            {
                int root = left.RootSegmentId.CompareTo(right.RootSegmentId); return root != 0 ? root : string.CompareOrdinal(left.Path, right.Path);
            }
        }

        /// <summary>Records the carried-work observations from one forced outer split.</summary>
        internal readonly struct OuterSplitReuseProbe
        {
            internal OuterSplitReuseProbe(bool childCoarseReused, bool rightBeforeLeft, int nestedTauIntegrationCalls) { ChildCoarseReused = childCoarseReused; RightBeforeLeft = rightBeforeLeft; NestedTauIntegrationCalls = nestedTauIntegrationCalls; }
            internal bool ChildCoarseReused { get; }
            internal bool RightBeforeLeft { get; }
            internal int NestedTauIntegrationCalls { get; }
        }

        /// <summary>Records a forced panel-cap rejection and the absence of later recursive work.</summary>
        internal readonly struct PanelCapProbe
        {
            internal PanelCapProbe(int panels, int evaluations, int startedRecursions, int laterRecursions, string diagnostic) { Panels = panels; Evaluations = evaluations; StartedRecursions = startedRecursions; LaterRecursions = laterRecursions; Diagnostic = diagnostic; }
            internal int Panels { get; }
            internal int Evaluations { get; }
            internal int StartedRecursions { get; }
            internal int LaterRecursions { get; }
            internal string Diagnostic { get; }
        }

        /// <summary>Records a pre-increment evaluation-cap rejection and completed sample-kernel work.</summary>
        internal readonly struct EvaluationCapProbe
        {
            internal EvaluationCapProbe(int panels, int evaluations, int sampleKernelWork, string diagnostic) { Panels = panels; Evaluations = evaluations; SampleKernelWork = sampleKernelWork; Diagnostic = diagnostic; }
            internal int Panels { get; }
            internal int Evaluations { get; }
            internal int SampleKernelWork { get; }
            internal string Diagnostic { get; }
        }

    }
}
