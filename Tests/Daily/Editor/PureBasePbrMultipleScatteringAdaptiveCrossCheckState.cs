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

// Maintains resource accounting, diagnostics, and deterministic accumulation for the adaptive cross-check.

using System;
using System.Globalization;

namespace PureBase.Tests.Daily
{
    /// <summary>Maintains resource accounting, diagnostics, and deterministic accumulation for the adaptive cross-check.</summary>
    internal static partial class AdaptiveCrossCheck
    {
        /// <summary>Maintains cross-check-only caps and its fixed right-first panel ordering.</summary>
        private sealed class State
        {
            private readonly AdaptiveSettings settings;
            private readonly SelectionExecutionBudget budget;
            private readonly SelectionExecutionContext context;
            private int evaluations;
            private int panels;
            private int maximumDepth;
            private int sampleKernelWork;
            private string failure;

            /// <summary>Initializes resource accounting for one independent cross-check invocation.</summary>
            internal State(AdaptiveSettings settings, double p, double v, bool switchBranch, SelectionExecutionBudget budget = null, SelectionExecutionContext context = default)
            {
                this.settings = settings;
                this.budget = budget;
                this.context = context;
                P = p;
                V = v;
                View = new PureBasePbrMultipleScatteringReference.Direction(Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)), 0.0d, v);
                SwitchBranch = switchBranch;
            }

            /// <summary>Gets the perceptual roughness for the current integration.</summary>
            internal double P { get; }
            /// <summary>Gets the current view cosine.</summary>
            internal double V { get; }
            /// <summary>Gets the reconstructed view direction.</summary>
            internal PureBasePbrMultipleScatteringReference.Direction View { get; }
            /// <summary>Gets whether the switch-epsilon branch is active.</summary>
            internal bool SwitchBranch { get; }
            /// <summary>Gets whether this integration has stopped at its first failure.</summary>
            internal bool Failed => failure != null;
            /// <summary>Gets the consumed panel count.</summary>
            internal int Panels => panels;
            /// <summary>Gets the consumed sample evaluation count.</summary>
            internal int Evaluations => evaluations;
            /// <summary>Gets the completed scalar-kernel sample count.</summary>
            internal int SampleKernelWork => sampleKernelWork;
            /// <summary>Gets the first deterministic failure diagnostic.</summary>
            internal string Failure => failure;
            /// <summary>Gets the configured recursion-depth cap.</summary>
            internal int MaxDepth => settings.MaxDepth;
            /// <summary>Gets the configured relative tolerance.</summary>
            internal double Relative => settings.Relative;

            /// <summary>Reserves exactly one sample evaluation before kernel work begins.</summary>
            internal bool Evaluate(string axis, double outerCoordinate, double left, double right, int depth)
            {
                if (Failed) return false;
                if (evaluations >= settings.MaxEvaluations)
                {
                    Fail(CapDiagnostic("evaluations", axis, outerCoordinate, left, right, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, depth));
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

            /// <summary>Records scalar-kernel work after its evaluation reservation succeeds.</summary>
            internal void RecordSampleKernelWork()
            {
                if (!Failed) sampleKernelWork++;
            }

            /// <summary>Retains the first failure to preserve deterministic stop evidence.</summary>
            internal void Fail(string reason)
            {
                if (failure == null) failure = reason;
            }

            /// <summary>Starts one K31 leaf only when its hard panel cap remains available.</summary>
            internal bool BeginKronrodPanel(double outerCoordinate, double left, double right, int depth)
            {
                if (Failed) return false;
                if (panels >= settings.MaxPanels)
                {
                    Fail(CapDiagnostic("panels", "tau", outerCoordinate, left, right, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, depth));
                    return false;
                }

                panels++;
                maximumDepth = Math.Max(maximumDepth, depth);
                return true;
            }

            /// <summary>Splits an outer panel using recursively recomputed child coarse estimates.</summary>
            internal AdaptiveEstimate Split(AdaptiveEstimate coarse, AdaptiveEstimate fine, string axis, double outerCoordinate, double left, double right, int depth, double absoluteBudget, double relativeShare, Func<State, double, double, int, double, double, AdaptiveEstimate> recurse)
            {
                if (Failed) return fine;
                double ruleDelta = Math.Abs(fine.Value - coarse.Value);
                double relativeLimit = settings.Relative * Math.Abs(fine.Value) * relativeShare;
                double error = fine.Error + ruleDelta;
                double limit = absoluteBudget + relativeLimit;
                if (!TryAcceptOrContinue(axis, outerCoordinate, left, right, depth, coarse.Value, fine.Value, fine.Error, ruleDelta, absoluteBudget, relativeLimit, error, limit)) return fine;
                double middle = (left + right) * 0.5d;
                AdaptiveEstimate upper = recurse(this, middle, right, depth + 1, absoluteBudget * 0.5d, relativeShare * 0.5d);
                if (Failed) return upper;
                AdaptiveEstimate lower = recurse(this, left, middle, depth + 1, absoluteBudget * 0.5d, relativeShare * 0.5d);
                return new AdaptiveEstimate(upper.Value + lower.Value, upper.Error + lower.Error);
            }

            /// <summary>Splits an outer panel while carrying each refined child estimate into its matching coarse rule.</summary>
            internal AdaptiveEstimate Split(AdaptiveEstimate coarse, OuterFineEstimate fine, string axis, double outerCoordinate, double left, double right, int depth, double absoluteBudget, double relativeShare, Func<State, double, double, int, double, double, AdaptiveEstimate, AdaptiveEstimate> recurse)
            {
                if (Failed) return fine.Value;
                AdaptiveEstimate refined = fine.Value;
                double ruleDelta = Math.Abs(refined.Value - coarse.Value);
                double relativeLimit = settings.Relative * Math.Abs(refined.Value) * relativeShare;
                double error = refined.Error + ruleDelta;
                double limit = absoluteBudget + relativeLimit;
                if (!TryAcceptOrContinue(axis, outerCoordinate, left, right, depth, coarse.Value, refined.Value, refined.Error, ruleDelta, absoluteBudget, relativeLimit, error, limit)) return refined;
                double middle = (left + right) * 0.5d;
                AdaptiveEstimate upper = recurse(this, middle, right, depth + 1, absoluteBudget * 0.5d, relativeShare * 0.5d, fine.Upper);
                if (Failed) return upper;
                AdaptiveEstimate lower = recurse(this, left, middle, depth + 1, absoluteBudget * 0.5d, relativeShare * 0.5d, fine.Lower);
                return new AdaptiveEstimate(upper.Value + lower.Value, upper.Error + lower.Error);
            }

            /// <summary>Accepts a bounded panel or records the first cap or depth failure.</summary>
            private bool TryAcceptOrContinue(string axis, double outerCoordinate, double left, double right, int depth, double coarse, double fine, double inheritedInnerError, double ruleDelta, double absoluteLimit, double relativeLimit, double error, double limit)
            {
                if (panels >= settings.MaxPanels)
                {
                    Fail(CapDiagnostic("panels", axis, outerCoordinate, left, right, coarse, fine, inheritedInnerError, ruleDelta, absoluteLimit, relativeLimit, error, limit, depth));
                    return false;
                }

                panels++;
                maximumDepth = Math.Max(maximumDepth, depth);
                if (error <= limit) return false;
                if (depth >= settings.MaxDepth)
                {
                    Fail(DepthDiagnostic(axis, outerCoordinate, left, right, coarse, fine, inheritedInnerError, ruleDelta, absoluteLimit, relativeLimit, error, limit, depth));
                    return false;
                }

                return true;
            }

            /// <summary>Formats the first pre-increment panel-cap rejection with complete local evidence.</summary>
            private string CapDiagnostic(string cap, string axis, double outerCoordinate, double left, double right, double coarse, double fine, double inheritedInnerError, double ruleDelta, double absoluteLimit, double relativeLimit, double error, double limit, int depth)
            {
                return "numerical-limit cross-check " + cap + " axis=" + axis + " outer=" + FormatCoordinate(outerCoordinate) + " interval=[" + FormatCoordinate(left) + "," + FormatCoordinate(right) + "] coarse=" + FormatCoordinate(coarse) + " fine=" + FormatCoordinate(fine) + " inheritedInnerError=" + FormatCoordinate(inheritedInnerError) + " ruleDelta=" + FormatCoordinate(ruleDelta) + " absoluteLimit=" + FormatCoordinate(absoluteLimit) + " relativeLimit=" + FormatCoordinate(relativeLimit) + " error=" + FormatCoordinate(error) + " limit=" + FormatCoordinate(limit) + " errorOverLimit=" + FormatCoordinate(error / limit) + " depth=" + depth.ToString(CultureInfo.InvariantCulture) + " panels=" + panels.ToString(CultureInfo.InvariantCulture) + " maxPanels=" + settings.MaxPanels.ToString(CultureInfo.InvariantCulture) + " evaluations=" + evaluations.ToString(CultureInfo.InvariantCulture) + " maxEvaluations=" + settings.MaxEvaluations.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>Formats noncanonical local depth evidence for a rejected cross-check panel.</summary>
            private static string DepthDiagnostic(string axis, double outerCoordinate, double left, double right, double coarse, double fine, double inheritedInnerError, double ruleDelta, double absoluteLimit, double relativeLimit, double error, double limit, int depth)
            {
                return "numerical-limit cross-check depth axis=" + axis + " outer=" + FormatCoordinate(outerCoordinate) + " interval=[" + FormatCoordinate(left) + "," + FormatCoordinate(right) + "] coarse=" + FormatCoordinate(coarse) + " fine=" + FormatCoordinate(fine) + " inheritedInnerError=" + FormatCoordinate(inheritedInnerError) + " ruleDelta=" + FormatCoordinate(ruleDelta) + " absoluteLimit=" + FormatCoordinate(absoluteLimit) + " relativeLimit=" + FormatCoordinate(relativeLimit) + " error=" + FormatCoordinate(error) + " limit=" + FormatCoordinate(limit) + " errorOverLimit=" + FormatCoordinate(error / limit) + " depth=" + depth.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>Formats a finite coordinate or an unavailable outer coordinate for diagnostics.</summary>
            private static string FormatCoordinate(double value) => double.IsNaN(value) ? "none" : value.ToString("R", CultureInfo.InvariantCulture);

            /// <summary>Builds the bounded result or fail-closed numerical-limit result.</summary>
            internal AdaptiveResult Result(AdaptiveEstimate value)
            {
                double tolerance = settings.Tolerance(value.Value);
                if (failure == null && value.Error > tolerance) Fail("numerical-limit cross-check global-error");
                return new AdaptiveResult(value.Value, failure == null ? value.Error : double.PositiveInfinity, tolerance, evaluations, panels, maximumDepth, failure);
            }
        }

        /// <summary>Accumulates cross-check terms with deterministic Kahan compensation.</summary>
        private struct Sum
        {
            private double value;
            private double correction;

            /// <summary>Gets the compensated sum.</summary>
            internal double Value => value;

            /// <summary>Adds one term using Kahan compensation.</summary>
            internal void Add(double term)
            {
                double adjusted = term - correction;
                double next = value + adjusted;
                correction = next - value - adjusted;
                value = next;
            }
        }

        /// <summary>Accumulates retained nested estimates without retaining discarded parent errors.</summary>
        private struct EstimateSum
        {
            private Sum value;
            private double error;

            /// <summary>Gets the accumulated retained estimate.</summary>
            internal AdaptiveEstimate Value => new AdaptiveEstimate(value.Value, error);

            /// <summary>Adds one retained nested estimate.</summary>
            internal void Add(AdaptiveEstimate term)
            {
                value.Add(term.Value);
                error += term.Error;
            }
        }
    }
}