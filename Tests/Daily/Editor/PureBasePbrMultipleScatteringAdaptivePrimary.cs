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

// Implements the independently partitioned scaled half-vector adaptive furnace path.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PureBase.Tests.Daily
{
    /// <summary>Implements the independently partitioned scaled half-vector adaptive furnace path.</summary>
    internal static class AdaptivePrimary
    {
        internal static readonly AdaptiveIdentity Identity = new AdaptiveIdentity("r=p^2*sqrt(eta/(1-eta)); fixed visibility-tail x=sqrt(1-eta), eta=1-x^2, deta=2x dx; Hhat=(r*cos(psi),r*sin(psi),1)/sqrt(1+r^2); L=2*dot(V,Hhat)*Hhat-V", "analytic eta roots of q=0, NdotL=0, 4q^2=1e-6, GGX denominator clamp, and fixed visibility-tail suffix selection", "Gauss-Legendre 3/5 embedded tensor with weighted accepted-leaf error", "left-before-right recursive depth-first", "Neumaier primary accumulator");
        private static readonly double[] Nodes3 = { -0.7745966692414834d, 0.0d, 0.7745966692414834d };
        private static readonly double[] Weights3 = { 0.5555555555555556d, 0.8888888888888888d, 0.5555555555555556d };
        private static readonly double[] Nodes5 = { -0.906179845938664d, -0.5384693101056831d, 0.0d, 0.5384693101056831d, 0.906179845938664d };
        private static readonly double[] Weights5 = { 0.2369268850561891d, 0.4786286704993665d, 0.5688888888888889d, 0.4786286704993665d, 0.2369268850561891d };

        /// <summary>Integrates with path-private rules, panels, scheduler, and accumulator.</summary>
        internal static AdaptiveResult Integrate(AdaptiveSettings settings, double p, double v, bool switchBranch)
        {
            return Integrate(settings, p, v, switchBranch, null, default);
        }

        /// <summary>Integrates with an optional selection-wide scalar-kernel reservation context.</summary>
        internal static AdaptiveResult Integrate(AdaptiveSettings settings, double p, double v, bool switchBranch, SelectionExecutionBudget budget, SelectionExecutionContext context)
        {
            var state = new State(settings, p, v, switchBranch, budget, context); AdaptiveEstimate value = IntegratePsi(state, 0.0d, 2.0d * Math.PI, 0, settings.Absolute, 1.0d); return state.Result(value);
        }

        /// <summary>Gets the deterministic eta boundaries used for a primary azimuth line.</summary>
        internal static double[] GetEtaPartitionBoundariesForTest(double p, double v, double psi) => EtaBoundaries(p, v, psi);

        /// <summary>Gets every positive safe-normalize root reported for a primary azimuth line.</summary>
        internal static double[] GetSafeNormalizeEtaRootsForTest(double p, double v, double psi)
        {
            double sinV = Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)); var values = new List<double>(); double cosine = Math.Cos(psi);
            AddRoots(values, sinV * sinV * cosine * cosine - AdaptiveProtocol.GuardSquared * 0.25d, 2.0d * sinV * v * cosine, v * v - AdaptiveProtocol.GuardSquared * 0.25d, p); values.Sort(); return values.ToArray();
        }

        /// <summary>Gets every eta root introduced for the GGX denominator clamp.</summary>
        internal static double[] GetDistributionEtaRootsForTest(double p, double v, double psi)
        {
            var values = new List<double>(); AddDistributionRoots(values, p, v, psi); values.Sort(); return values.ToArray();
        }

        /// <summary>Gets the initial eta partitions selected for the fixed visibility-tail x transformation.</summary>
        internal static bool[] GetVisibilityTailXPartitionsForTest(double p, double v, double psi, bool switchBranch)
        {
            double[] boundaries = EtaBoundaries(p, v, psi); return VisibilityTailXPartitions(p, v, psi, switchBranch, boundaries);
        }

        /// <summary>Recursively integrates half-vector azimuth with the primary embedded pair.</summary>
        private static AdaptiveEstimate IntegratePsi(State state, double left, double right, int depth, double absoluteBudget, double relativeShare)
        {
            if (state.Failed) return default;
            AdaptiveEstimate coarse = PsiRule(state, left, right, Nodes3, Weights3, absoluteBudget, relativeShare);
            if (state.Failed) return coarse;
            AdaptiveEstimate fine = PsiRule(state, left, right, Nodes5, Weights5, absoluteBudget, relativeShare);
            return state.Failed ? fine : state.Split(coarse, fine, "psi", double.NaN, left, right, depth, absoluteBudget, relativeShare, IntegratePsi);
        }

        /// <summary>Evaluates the outer primary rule with independently split eta integrals.</summary>
        private static AdaptiveEstimate PsiRule(State state, double left, double right, double[] nodes, double[] weights, double absoluteBudget, double relativeShare)
        {
            double center = (left + right) * 0.5d; double half = (right - left) * 0.5d; var total = new EstimateSum();
            for (int index = 0; index < nodes.Length && !state.Failed; index++)
            {
                double scale = half * weights[index]; AdaptiveEstimate eta = IntegrateEtaPartitions(state, center + half * nodes[index], absoluteBudget / (2.0d * half), relativeShare); total.Add(AdaptiveEstimate.Scale(eta, scale));
            }
            return total.Value;
        }

        /// <summary>Splits each eta line at both declared validity and safe-normalize guard curves.</summary>
        private static AdaptiveEstimate IntegrateEtaPartitions(State state, double psi, double absoluteBudget, double relativeShare)
        {
            double[] boundaries = EtaBoundaries(state.P, state.V, psi); bool[] useX = VisibilityTailXPartitions(state.P, state.V, psi, state.SwitchBranch, boundaries); var total = new EstimateSum();
            for (int index = 0; index < boundaries.Length - 1 && !state.Failed; index++)
            {
                double left = boundaries[index]; double right = boundaries[index + 1]; double share = right - left;
                total.Add(useX[index] ? IntegrateVisibilityTailEta(state, psi, left, right, 0, absoluteBudget * share, relativeShare) : IntegrateEta(state, psi, left, right, 0, absoluteBudget * share, relativeShare));
            }
            return total.Value;
        }

        /// <summary>Recursively integrates one analytic eta partition with the primary embedded pair.</summary>
        private static AdaptiveEstimate IntegrateEta(State state, double psi, double left, double right, int depth, double absoluteBudget, double relativeShare)
        {
            if (state.Failed) return default;
            AdaptiveEstimate coarse = EtaRule(state, psi, left, right, depth, Nodes3, Weights3);
            if (state.Failed) return coarse;
            AdaptiveEstimate fine = EtaRule(state, psi, left, right, depth, Nodes5, Weights5);
            return state.Failed ? fine : state.Split(coarse, fine, "eta", psi, left, right, depth, absoluteBudget, relativeShare, (current, a, b, d, budget, share) => IntegrateEta(current, psi, a, b, d, budget, share));
        }

        /// <summary>Reparameterizes one fixed visibility-tail suffix with eta=1-x^2 and deta=2x dx.</summary>
        private static AdaptiveEstimate IntegrateVisibilityTailEta(State state, double psi, double left, double right, int depth, double absoluteBudget, double relativeShare)
        {
            return IntegrateTerminalX(state, psi, Math.Sqrt(1.0d - right), Math.Sqrt(1.0d - left), depth, absoluteBudget, relativeShare);
        }

        /// <summary>Recursively integrates one fixed visibility-tail suffix in x=sqrt(1-eta) coordinates.</summary>
        private static AdaptiveEstimate IntegrateTerminalX(State state, double psi, double left, double right, int depth, double absoluteBudget, double relativeShare)
        {
            if (state.Failed) return default;
            AdaptiveEstimate coarse = TerminalEtaRule(state, psi, left, right, depth, Nodes3, Weights3);
            if (state.Failed) return coarse;
            AdaptiveEstimate fine = TerminalEtaRule(state, psi, left, right, depth, Nodes5, Weights5);
            return state.Failed ? fine : state.Split(coarse, fine, "eta-x", psi, left, right, depth, absoluteBudget, relativeShare, (current, a, b, d, budget, share) => IntegrateTerminalX(current, psi, a, b, d, budget, share));
        }

        /// <summary>Evaluates the primary eta rule without sampling its partition endpoints.</summary>
        private static AdaptiveEstimate EtaRule(State state, double psi, double left, double right, int depth, double[] nodes, double[] weights)
        {
            double center = (left + right) * 0.5d; double half = (right - left) * 0.5d; var total = new Sum();
            for (int index = 0; index < nodes.Length && !state.Failed; index++) total.Add(weights[index] * Sample(state, center + half * nodes[index], psi, "eta", left, right, depth));
            return new AdaptiveEstimate(half * total.Value, 0.0d);
        }

        /// <summary>Evaluates the unchanged eta kernel through the fixed visibility-tail Jacobian deta=2x dx.</summary>
        private static AdaptiveEstimate TerminalEtaRule(State state, double psi, double left, double right, int depth, double[] nodes, double[] weights)
        {
            double center = (left + right) * 0.5d; double half = (right - left) * 0.5d; var total = new Sum();
            for (int index = 0; index < nodes.Length && !state.Failed; index++) { double x = center + half * nodes[index]; total.Add(weights[index] * Sample(state, 1.0d - x * x, psi, "eta-x", left, right, depth) * (2.0d * x)); }
            return new AdaptiveEstimate(half * total.Value, 0.0d);
        }

        /// <summary>Rebuilds L and V before the shared exact HLSL-equivalent scalar kernel.</summary>
        private static double Sample(State state, double eta, double psi, string axis, double left, double right, int depth)
        {
            if (!state.Evaluate(axis, psi, left, right, depth)) return 0.0d;
            state.RecordSampleKernelWork(); double a = state.P * state.P; double a2 = a * a; double r2 = a2 * eta / (1.0d - eta); double inverse = 1.0d / Math.Sqrt(1.0d + r2);
            var half = new PureBasePbrMultipleScatteringReference.Direction(Math.Sqrt(r2) * Math.Cos(psi) * inverse, Math.Sqrt(r2) * Math.Sin(psi) * inverse, inverse);
            double q = state.View.Dot(half); PureBasePbrMultipleScatteringReference.Direction light = half * (2.0d * q) - state.View;
            if (q <= 0.0d || light.Z <= 0.0d) return 0.0d;
            PureBasePbrMultipleScatteringReference.GuardedTerms terms = PureBasePbrMultipleScatteringReference.EvaluateGuardedTerms(light, state.View, state.P, state.SwitchBranch);
            double jacobian = 2.0d * q * a2 * half.Z * half.Z * half.Z / ((1.0d - eta) * (1.0d - eta)); double value = terms.Distribution * terms.Visibility * light.Z * jacobian;
            if (!PureBasePbrMultipleScatteringReference.IsFinite(value)) state.Fail("nonfinite primary sample"); return value;
        }

        /// <summary>Finds analytic eta roots for all primary kernel branch transitions.</summary>
        private static double[] EtaBoundaries(double p, double v, double psi)
        {
            double sinV = Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)); double cosine = Math.Cos(psi); var values = new List<double> { 0.0d, 1.0d };
            AddLinearRoot(values, sinV * cosine, v, p); AddRoots(values, -v, 2.0d * sinV * cosine, v, p);
            foreach (double root in GetSafeNormalizeEtaRootsForTest(p, v, psi)) AddEta(values, root);
            AddDistributionRoots(values, p, v, psi); values.Sort(); return values.ToArray();
        }

        /// <summary>Selects whole initial eta suffixes intersecting the analytic visibility-epsilon tail.</summary>
        private static bool[] VisibilityTailXPartitions(double p, double v, double psi, bool switchBranch, double[] boundaries)
        {
            int first = boundaries.Length - 2;
            if (TryGetVisibilityTailEta(p, v, psi, switchBranch, out double tail))
            {
                for (int index = 0; index < boundaries.Length - 1; index++) if (boundaries[index + 1] > tail) { first = index; break; }
            }

            var result = new bool[boundaries.Length - 1];
            for (int index = first; index < result.Length; index++) result[index] = true;
            return result;
        }

        /// <summary>Finds the valid descending-side lambda=epsilon eta root for the active visibility branch.</summary>
        private static bool TryGetVisibilityTailEta(double p, double v, double psi, bool switchBranch, out double eta)
        {
            eta = 1.0d; double a = p * p; double epsilon = switchBranch ? PureBasePbrMultipleScatteringReference.SwitchEpsilon : PureBasePbrMultipleScatteringReference.NormalEpsilon;
            if (epsilon <= a * v) return false;
            double denominator = a + 2.0d * v * (1.0d - a); double u = (epsilon - a * v) / denominator; double s = Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)); double c = Math.Cos(psi); double discriminant = s * s * c * c + v * v - u * u;
            if (u <= 0.0d || u >= 1.0d || discriminant < 0.0d || v + u <= 0.0d) return false;
            double r = (s * c + Math.Sqrt(discriminant)) / (v + u);
            if (r <= 0.0d || !PureBasePbrMultipleScatteringReference.IsFinite(r)) return false;
            eta = r * r / (a * a + r * r); return eta > 0.0d && eta < 1.0d;
        }

        /// <summary>Adds all normal and guarded GGX denominator clamp roots.</summary>
        private static void AddDistributionRoots(List<double> values, double p, double v, double psi)
        {
            if (!AdaptiveProtocol.TryGetDistributionNdotHSquared(p, out double ndotHSquared)) return;
            AddRoot(values, Math.Sqrt(1.0d / ndotHSquared - 1.0d), p);
            double sinV = Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)); double cosine = sinV * Math.Cos(psi); double scale = Math.Sqrt(ndotHSquared * AdaptiveProtocol.GuardSquared * 0.25d);
            AddRoots(values, scale, -cosine, scale - v, p); AddRoots(values, scale, cosine, scale + v, p);
        }

        /// <summary>Adds a q=0 root when it is a strict eta interior point.</summary>
        private static void AddLinearRoot(List<double> values, double coefficient, double constant, double p)
        {
            if (Math.Abs(coefficient) > 1.0e-15d) AddRoot(values, -constant / coefficient, p);
        }

        /// <summary>Adds positive quadratic r roots as eta=r^2/(p^4+r^2) without duplicates.</summary>
        private static void AddRoots(List<double> values, double a, double b, double c, double p)
        {
            if (Math.Abs(a) < 1.0e-15d) { if (Math.Abs(b) > 1.0e-15d) AddRoot(values, -c / b, p); return; }
            double discriminant = b * b - 4.0d * a * c; if (discriminant < 0.0d) return; double root = Math.Sqrt(discriminant);
            AddRoot(values, (-b - root) / (2.0d * a), p); AddRoot(values, (-b + root) / (2.0d * a), p);
        }

        /// <summary>Adds one strict interior eta boundary in deterministic ascending order.</summary>
        private static void AddRoot(List<double> values, double r, double p)
        {
            if (r <= 0.0d || !PureBasePbrMultipleScatteringReference.IsFinite(r)) return; double p4 = Math.Pow(p, 4.0d); AddEta(values, r * r / (p4 + r * r));
        }

        /// <summary>Adds one strict eta value without changing zero-measure endpoint handling.</summary>
        private static void AddEta(List<double> values, double eta)
        {
            if (eta <= 0.0d || eta >= 1.0d) return; foreach (double value in values) if (Math.Abs(value - eta) < 1.0e-13d) return; values.Add(eta);
        }

        /// <summary>Exercises primary scheduler caps without running the numerical selection path.</summary>
        internal static ResourceCapProbe ProbeResourceCapsForTest()
        {
            var panelState = new State(new AdaptiveSettings("primary-panel-cap-probe", 0.0d, 0.0d, 0.0d, 0.0d, 4, 1, 8), 0.5d, 0.5d, false);
            int startedRecursions = 0; int laterRecursions = 0;
            panelState.Split(new AdaptiveEstimate(0.0d, 0.0d), new AdaptiveEstimate(1.0d, 0.0d), "psi", double.NaN, 0.0d, 1.0d, 0, 0.5d, 1.0d, (current, left, right, depth, absoluteBudget, relativeShare) =>
            {
                startedRecursions++;
                return current.Split(new AdaptiveEstimate(0.0d, 0.0d), new AdaptiveEstimate(1.0d, 0.0d), "eta", 0.75d, left, right, depth, absoluteBudget, relativeShare, (next, childLeft, childRight, childDepth, childBudget, childShare) => { laterRecursions++; return default; });
            });

            var evaluationState = new State(new AdaptiveSettings("primary-evaluation-cap-probe", 0.0d, 0.0d, 0.0d, 0.0d, 4, 8, 1), 0.5d, 0.5d, false);
            Sample(evaluationState, 0.5d, 0.0d, "eta", 0.0d, 1.0d, 0);
            Sample(evaluationState, 0.5d, 0.0d, "eta", 0.0d, 1.0d, 0);
            return new ResourceCapProbe(panelState.Panels, panelState.Evaluations, startedRecursions, laterRecursions, panelState.Failure, evaluationState.Panels, evaluationState.Evaluations, evaluationState.SampleKernelWork, evaluationState.Failure);
        }

        /// <summary>Records primary pre-increment resource-cap behavior for direct scheduler tests.</summary>
        internal readonly struct ResourceCapProbe
        {
            internal ResourceCapProbe(int panels, int evaluations, int startedRecursions, int laterRecursions, string panelDiagnostic, int evaluationPanels, int evaluationCount, int sampleKernelWork, string evaluationDiagnostic) { Panels = panels; Evaluations = evaluations; StartedRecursions = startedRecursions; LaterRecursions = laterRecursions; PanelDiagnostic = panelDiagnostic; EvaluationPanels = evaluationPanels; EvaluationCount = evaluationCount; SampleKernelWork = sampleKernelWork; EvaluationDiagnostic = evaluationDiagnostic; }
            internal int Panels { get; } internal int Evaluations { get; } internal int StartedRecursions { get; } internal int LaterRecursions { get; } internal string PanelDiagnostic { get; } internal int EvaluationPanels { get; } internal int EvaluationCount { get; } internal int SampleKernelWork { get; } internal string EvaluationDiagnostic { get; }
        }

        /// <summary>Maintains primary-only caps and its fixed depth-first panel ordering.</summary>
        private sealed class State
        {
            private readonly AdaptiveSettings settings; private readonly SelectionExecutionBudget budget; private readonly SelectionExecutionContext context; private int evaluations; private int panels; private int maximumDepth; private int sampleKernelWork; private string failure;
            internal State(AdaptiveSettings settings, double p, double v, bool switchBranch, SelectionExecutionBudget budget = null, SelectionExecutionContext context = default) { this.settings = settings; this.budget = budget; this.context = context; P = p; V = v; View = new PureBasePbrMultipleScatteringReference.Direction(Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)), 0.0d, v); SwitchBranch = switchBranch; }
            internal double P { get; } internal double V { get; } internal PureBasePbrMultipleScatteringReference.Direction View { get; } internal bool SwitchBranch { get; }
            internal bool Failed => failure != null;
            internal int Panels => panels;
            internal int Evaluations => evaluations;
            internal int SampleKernelWork => sampleKernelWork;
            internal string Failure => failure;
            internal bool Evaluate(string axis, double outerCoordinate, double left, double right, int depth)
            {
                if (Failed) return false;
                if (evaluations >= settings.MaxEvaluations) { Fail(CapDiagnostic("evaluations", axis, outerCoordinate, left, right, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, depth)); return false; }
                evaluations++;
                if (budget != null && !budget.TryReserve(context)) { Fail(budget.CreateException().Message); return false; }
                return true;
            }
            internal void RecordSampleKernelWork() { if (!Failed) sampleKernelWork++; }
            internal void Fail(string reason) { if (failure == null) failure = reason; }
            internal AdaptiveEstimate Split(AdaptiveEstimate coarse, AdaptiveEstimate fine, string axis, double outerCoordinate, double left, double right, int depth, double absoluteBudget, double relativeShare, Func<State, double, double, int, double, double, AdaptiveEstimate> recurse)
            {
                if (Failed) return fine;
                double ruleDelta = Math.Abs(fine.Value - coarse.Value); double relativeLimit = settings.Relative * Math.Abs(fine.Value) * relativeShare; double error = fine.Error + ruleDelta; double limit = absoluteBudget + relativeLimit;
                if (panels >= settings.MaxPanels) { Fail(CapDiagnostic("panels", axis, outerCoordinate, left, right, coarse.Value, fine.Value, fine.Error, ruleDelta, absoluteBudget, relativeLimit, error, limit, depth)); return fine; }
                panels++; maximumDepth = Math.Max(maximumDepth, depth);
                if (error <= limit) return new AdaptiveEstimate(fine.Value, error);
                if (depth >= settings.MaxDepth) { Fail(DepthDiagnostic(axis, outerCoordinate, left, right, coarse.Value, fine.Value, fine.Error, ruleDelta, absoluteBudget, relativeLimit, error, limit, depth)); return fine; }
                double middle = (left + right) * 0.5d; AdaptiveEstimate first = recurse(this, left, middle, depth + 1, absoluteBudget * 0.5d, relativeShare);
                if (Failed) return first;
                AdaptiveEstimate second = recurse(this, middle, right, depth + 1, absoluteBudget * 0.5d, relativeShare);
                return new AdaptiveEstimate(first.Value + second.Value, first.Error + second.Error);
            }
            /// <summary>Formats the first pre-increment resource-cap rejection with complete local evidence.</summary>
            private string CapDiagnostic(string cap, string axis, double outerCoordinate, double left, double right, double coarse, double fine, double inheritedInnerError, double ruleDelta, double absoluteLimit, double relativeLimit, double error, double limit, int depth)
            {
                return "numerical-limit primary " + cap + " axis=" + axis + " outer=" + FormatCoordinate(outerCoordinate) + " interval=[" + FormatCoordinate(left) + "," + FormatCoordinate(right) + "] coarse=" + FormatCoordinate(coarse) + " fine=" + FormatCoordinate(fine) + " inheritedInnerError=" + FormatCoordinate(inheritedInnerError) + " ruleDelta=" + FormatCoordinate(ruleDelta) + " absoluteLimit=" + FormatCoordinate(absoluteLimit) + " relativeLimit=" + FormatCoordinate(relativeLimit) + " error=" + FormatCoordinate(error) + " limit=" + FormatCoordinate(limit) + " depth=" + depth.ToString(CultureInfo.InvariantCulture) + " panels=" + panels.ToString(CultureInfo.InvariantCulture) + " maxPanels=" + settings.MaxPanels.ToString(CultureInfo.InvariantCulture) + " evaluations=" + evaluations.ToString(CultureInfo.InvariantCulture) + " maxEvaluations=" + settings.MaxEvaluations.ToString(CultureInfo.InvariantCulture);
            }
            /// <summary>Formats noncanonical local depth evidence for a rejected adaptive panel.</summary>
            private static string DepthDiagnostic(string axis, double outerCoordinate, double left, double right, double coarse, double fine, double inheritedInnerError, double ruleDelta, double absoluteLimit, double relativeLimit, double error, double limit, int depth)
            {
                return "numerical-limit primary depth axis=" + axis + " outer=" + FormatCoordinate(outerCoordinate) + " interval=[" + FormatCoordinate(left) + "," + FormatCoordinate(right) + "] coarse=" + FormatCoordinate(coarse) + " fine=" + FormatCoordinate(fine) + " inheritedInnerError=" + FormatCoordinate(inheritedInnerError) + " ruleDelta=" + FormatCoordinate(ruleDelta) + " absoluteLimit=" + FormatCoordinate(absoluteLimit) + " relativeLimit=" + FormatCoordinate(relativeLimit) + " error=" + FormatCoordinate(error) + " limit=" + FormatCoordinate(limit) + " errorOverLimit=" + FormatCoordinate(error / limit) + " depth=" + depth.ToString(CultureInfo.InvariantCulture);
            }
            /// <summary>Formats a finite coordinate or an unavailable outer coordinate for diagnostics.</summary>
            private static string FormatCoordinate(double value) => double.IsNaN(value) ? "none" : value.ToString("R", CultureInfo.InvariantCulture);
            internal AdaptiveResult Result(AdaptiveEstimate value)
            {
                double tolerance = settings.Tolerance(value.Value); if (failure == null && value.Error > tolerance) Fail("numerical-limit primary global-error"); return new AdaptiveResult(value.Value, failure == null ? value.Error : double.PositiveInfinity, tolerance, evaluations, panels, maximumDepth, failure);
            }
        }

        /// <summary>Accumulates primary quadrature terms with deterministic Neumaier compensation.</summary>
        private struct Sum { private double value; private double correction; internal double Value => value + correction; internal void Add(double term) { double next = value + term; correction += Math.Abs(value) >= Math.Abs(term) ? value - next + term : term - next + value; value = next; } }
        /// <summary>Accumulates retained nested estimates without retaining discarded parent errors.</summary>
        private struct EstimateSum { private Sum value; private double error; internal AdaptiveEstimate Value => new AdaptiveEstimate(value.Value, error); internal void Add(AdaptiveEstimate term) { value.Add(term.Value); error += term.Error; } }
    }
}
