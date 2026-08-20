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

// Provides the noncanonical direct light-space calibration witness.

using System;
using System.Collections.Generic;

namespace PureBase.Tests.Daily
{
    /// <summary>Provides the noncanonical direct light-space calibration witness.</summary>
    internal static class KronrodWitness
    {
        /// <summary>Defines the nested coarse Fejer-II order.</summary>
        private const int CoarseOrder = 31;
        /// <summary>Defines the nested fine Fejer-II order.</summary>
        private const int FineOrder = 63;
        /// <summary>Stores the witness-owned coarse endpoint-free rule.</summary>
        private static readonly FejerRule CoarseRule = BuildFejerRule(CoarseOrder);
        /// <summary>Stores the witness-owned fine endpoint-free rule.</summary>
        private static readonly FejerRule FineRule = BuildFejerRule(FineOrder);

        internal static readonly AdaptiveIdentity Identity = new AdaptiveIdentity(
            "t in [0,1], NdotL=t^2, dNdotL=2t dt; phi in [0,2*pi]; endpoint limits are excluded by Fejer-II nodes",
            "independent adaptive direct-light t-phi directional bisection",
            "independent embedded endpoint-free Fejer-II 31/63 tensor product",
            "largest local rule indicator first; deterministic directional child order",
            "Neumaier witness accumulator");

        /// <summary>Integrates a direct light-space witness outside the canonical oracle path.</summary>
        internal static AdaptiveResult Integrate(AdaptiveSettings settings, double p, double v, bool switchBranch)
        {
            return Integrate(settings, p, v, switchBranch, null, default);
        }

        /// <summary>Integrates with an optional selection-wide scalar-kernel reservation context.</summary>
        internal static AdaptiveResult Integrate(AdaptiveSettings settings, double p, double v, bool switchBranch, SelectionExecutionBudget budget, SelectionExecutionContext context)
        {
            var state = new State(settings, p, v, switchBranch, budget, context);
            var leaves = new List<Panel> { EvaluatePanel(state, 0.0d, 1.0d, 0.0d, 2.0d * Math.PI, 0, 1L) };
            while (!state.Failed)
            {
                AdaptiveEstimate estimate = SumLeaves(leaves);
                if (estimate.Error <= settings.WitnessTolerance(estimate.Value)) return state.Result(estimate);
                int selected = SelectLargestIndicator(leaves);
                Panel panel = leaves[selected];
                if (panel.Depth >= settings.MaxDepth) { state.Fail("numerical-limit witness depth=" + panel.Depth + " maxDepth=" + settings.MaxDepth); break; }
                leaves.RemoveAt(selected);
                SplitPanel(state, leaves, panel);
            }

            return state.Result(SumLeaves(leaves));
        }

        /// <summary>Splits the selected direct-light rectangle along its dominant embedded-rule axis.</summary>
        private static void SplitPanel(State state, List<Panel> leaves, Panel panel)
        {
            double tMidpoint = (panel.TLeft + panel.TRight) * 0.5d;
            double phiMidpoint = (panel.PhiLeft + panel.PhiRight) * 0.5d;
            if (panel.SplitT)
            {
                AddPanel(state, leaves, panel.TLeft, tMidpoint, panel.PhiLeft, panel.PhiRight, panel.Depth + 1, panel.Path << 1);
                AddPanel(state, leaves, tMidpoint, panel.TRight, panel.PhiLeft, panel.PhiRight, panel.Depth + 1, (panel.Path << 1) | 1L);
                return;
            }

            AddPanel(state, leaves, panel.TLeft, panel.TRight, panel.PhiLeft, phiMidpoint, panel.Depth + 1, panel.Path << 1);
            AddPanel(state, leaves, panel.TLeft, panel.TRight, phiMidpoint, panel.PhiRight, panel.Depth + 1, (panel.Path << 1) | 1L);
        }

        /// <summary>Adds one independently evaluated descendant when the witness remains within its caps.</summary>
        private static void AddPanel(State state, List<Panel> leaves, double tLeft, double tRight, double phiLeft, double phiRight, int depth, long path)
        {
            if (!state.Failed) leaves.Add(EvaluatePanel(state, tLeft, tRight, phiLeft, phiRight, depth, path));
        }

        /// <summary>Selects the unresolved leaf with the greatest embedded-rule difference.</summary>
        private static int SelectLargestIndicator(List<Panel> leaves)
        {
            int selected = 0;
            for (int index = 1; index < leaves.Count; index++)
            {
                Panel candidate = leaves[index]; Panel current = leaves[selected];
                if (candidate.Indicator > current.Indicator || candidate.Indicator == current.Indicator && candidate.Path < current.Path) selected = index;
            }

            return selected;
        }

        /// <summary>Returns the independently accumulated Fejer estimates over all unresolved leaves.</summary>
        private static AdaptiveEstimate SumLeaves(List<Panel> leaves)
        {
            var value = new Sum(); var error = new Sum();
            for (int index = 0; index < leaves.Count; index++) { value.Add(leaves[index].Fine); error.Add(leaves[index].Indicator); }
            return new AdaptiveEstimate(value.Value, error.Value);
        }

        /// <summary>Evaluates one direct-light rectangle with nested endpoint-free Fejer-II rules.</summary>
        private static Panel EvaluatePanel(State state, double tLeft, double tRight, double phiLeft, double phiRight, int depth, long path)
        {
            if (!state.BeginPanel(depth)) return default;
            var fine = new Sum(); var tCoarse = new Sum(); var phiCoarse = new Sum();
            double tCenter = (tLeft + tRight) * 0.5d; double tHalf = (tRight - tLeft) * 0.5d;
            double phiCenter = (phiLeft + phiRight) * 0.5d; double phiHalf = (phiRight - phiLeft) * 0.5d;
            for (int tIndex = 0; tIndex < FineOrder && !state.Failed; tIndex++)
            {
                double t = tCenter + tHalf * FineRule.Nodes[tIndex];
                for (int phiIndex = 0; phiIndex < FineOrder && !state.Failed; phiIndex++)
                {
                    double phi = phiCenter + phiHalf * FineRule.Nodes[phiIndex];
                    double value = Sample(state, t * t, phi) * (2.0d * t);
                    fine.Add(FineRule.Weights[tIndex] * FineRule.Weights[phiIndex] * value);
                    if ((tIndex & 1) != 0) tCoarse.Add(CoarseRule.Weights[(tIndex - 1) / 2] * FineRule.Weights[phiIndex] * value);
                    if ((phiIndex & 1) != 0) phiCoarse.Add(FineRule.Weights[tIndex] * CoarseRule.Weights[(phiIndex - 1) / 2] * value);
                }
            }

            double scale = tHalf * phiHalf; double fineValue = fine.Value * scale;
            double tIndicator = Math.Abs(fineValue - tCoarse.Value * scale); double phiIndicator = Math.Abs(fineValue - phiCoarse.Value * scale);
            double indicator = Math.Max(tIndicator, phiIndicator);
            return new Panel(tLeft, tRight, phiLeft, phiRight, depth, path, fineValue, indicator, tIndicator >= phiIndicator);
        }

        /// <summary>Builds a witness-private Fejer-II rule whose odd orders nest at every other fine node.</summary>
        private static FejerRule BuildFejerRule(int order)
        {
            var nodes = new double[order]; var weights = new double[order];
            for (int index = 0; index < order; index++)
            {
                double angle = (index + 1) * Math.PI / (order + 1); double series = 0.0d;
                for (int harmonic = 1; harmonic <= order; harmonic += 2) series += Math.Sin(harmonic * angle) / harmonic;
                nodes[index] = Math.Cos(angle); weights[index] = 4.0d * Math.Sin(angle) * series / (order + 1);
            }

            return new FejerRule(nodes, weights);
        }

        /// <summary>Evaluates one interior direct light-space witness sample.</summary>
        private static double Sample(State state, double u, double phi)
        {
            if (!state.BeginEvaluation()) return 0.0d;
            double sinL = Math.Sqrt(Math.Max(0.0d, 1.0d - u * u));
            var light = new PureBasePbrMultipleScatteringReference.Direction(sinL * Math.Cos(phi), sinL * Math.Sin(phi), u);
            PureBasePbrMultipleScatteringReference.GuardedTerms terms = PureBasePbrMultipleScatteringReference.EvaluateGuardedTerms(light, state.View, state.P, state.SwitchBranch);
            double value = terms.Distribution * terms.Visibility * u;
            if (!PureBasePbrMultipleScatteringReference.IsFinite(value)) state.Fail("nonfinite witness sample");
            return value;
        }

        /// <summary>Maintains witness-only resource accounting and stop state.</summary>
        private sealed class State
        {
            private readonly AdaptiveSettings settings;
            private readonly SelectionExecutionBudget budget;
            private readonly SelectionExecutionContext context;
            private int evaluations;
            private string failure;

            internal State(AdaptiveSettings settings, double p, double v, bool switchBranch, SelectionExecutionBudget budget = null, SelectionExecutionContext context = default)
            {
                this.settings = settings;
                this.budget = budget;
                this.context = context;
                P = p;
                SwitchBranch = switchBranch;
                View = new PureBasePbrMultipleScatteringReference.Direction(Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)), 0.0d, v);
            }

            internal double P { get; }
            internal bool SwitchBranch { get; }
            internal PureBasePbrMultipleScatteringReference.Direction View { get; }
            internal bool Failed => failure != null;

            /// <summary>Reserves one whole witness panel before its Fejer samples begin.</summary>
            internal bool BeginPanel(int depth)
            {
                if (Failed) return false;
                if (Panels >= settings.MaxPanels) { Fail("numerical-limit witness panels=" + Panels + " maxPanels=" + settings.MaxPanels); return false; }
                Panels++; Depth = Math.Max(Depth, depth); return true;
            }

            internal bool BeginEvaluation()
            {
                if (Failed) return false;
                if (evaluations >= settings.MaxEvaluations)
                {
                    Fail("numerical-limit witness evaluations=" + evaluations + " maxEvaluations=" + settings.MaxEvaluations);
                    return false;
                }

                evaluations++;
                if (budget != null && !budget.TryReserve(context))
                {
                    Fail(budget.CreateException().Message);
                    return false;
                }

                return true;
            }

            internal void Fail(string diagnostic)
            {
                if (failure == null) failure = diagnostic;
            }

            /// <summary>Builds the final fail-closed witness result from accumulated adaptive leaves.</summary>
            internal AdaptiveResult Result(AdaptiveEstimate estimate)
            {
                double tolerance = settings.WitnessTolerance(estimate.Value);
                if (!Failed && estimate.Error > tolerance) Fail("numerical-limit witness global-error");
                return new AdaptiveResult(estimate.Value, estimate.Error, tolerance, evaluations, Panels, Depth, failure);
            }

            /// <summary>Gets the number of evaluated Fejer rectangles.</summary>
            internal int Panels { get; private set; }
            /// <summary>Gets the deepest evaluated Fejer rectangle.</summary>
            internal int Depth { get; private set; }
        }

        /// <summary>Stores one independent adaptive direct-light rectangle and its embedded estimate.</summary>
        private readonly struct Panel
        {
            internal Panel(double tLeft, double tRight, double phiLeft, double phiRight, int depth, long path, double fine, double indicator, bool splitT) { TLeft = tLeft; TRight = tRight; PhiLeft = phiLeft; PhiRight = phiRight; Depth = depth; Path = path; Fine = fine; Indicator = indicator; SplitT = splitT; }
            internal double TLeft { get; }
            internal double TRight { get; }
            internal double PhiLeft { get; }
            internal double PhiRight { get; }
            internal int Depth { get; }
            internal long Path { get; }
            internal double Fine { get; }
            internal double Indicator { get; }
            internal bool SplitT { get; }
        }

        /// <summary>Stores a witness-private endpoint-free Fejer-II rule.</summary>
        private sealed class FejerRule
        {
            internal FejerRule(double[] nodes, double[] weights) { Nodes = nodes; Weights = weights; }
            internal double[] Nodes { get; }
            internal double[] Weights { get; }
        }

        /// <summary>Accumulates witness samples with Neumaier compensation.</summary>
        private struct Sum
        {
            private double value;
            private double correction;

            internal double Value => value + correction;

            internal void Add(double term)
            {
                double next = value + term;
                correction += Math.Abs(value) >= Math.Abs(term) ? value - next + term : term - next + value;
                value = next;
            }
        }
    }
}
