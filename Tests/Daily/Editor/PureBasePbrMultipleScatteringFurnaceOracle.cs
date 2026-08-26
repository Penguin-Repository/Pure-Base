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

// Calibrates and freezes the independent finite numerical furnace protocol.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Calibrates and freezes the independent finite numerical furnace protocol.</summary>
    /// <summary>Calibrates and freezes the independent finite numerical furnace protocol.</summary>
    internal static class PureBasePbrMultipleScatteringFurnaceOracle
    {
        private const int CoefficientCount = 16;
        private const int TrainingSamples = 13;
        private const int ValidationSamples = 7;
        private const int LightCosinePower = 3;
        private const int LightAzimuthPower = 3;
        private static readonly TimeSpan WallTimeLimit = TimeSpan.FromMinutes(15);
        internal const double EnergyBudget = 0.00015d;
        internal const double CompensationBudget = 0.0003d;
        internal const float CoefficientBudget = 0.0005f;
        internal const double CrossCheckBudget = 0.001d;
        internal const double FurnaceDeltaBudget = 0.0005d;
        private static readonly ProtocolIdentity PrimaryIdentity = new ProtocolIdentity("eta-phi-ggx-density", "uniform-midpoint", "uniform-midpoint-tensor-product");
        private static readonly ProtocolIdentity CrossIdentity = new ProtocolIdentity("ndotl-phi-boundary-power-map", "fejer-ii", "fejer-ii-tensor-product");
        private static readonly Rung[] Ladder = { new Rung(64, 128), new Rung(128, 256), new Rung(256, 512), new Rung(512, 1024) };
        private static readonly Coordinate[] Audit = CreateAudit();
        private static readonly Coordinate[] Training = CreateChebyshevGrid();
        private static readonly Coordinate[] Validation = CreateValidationGrid();
        private static readonly Dictionary<int, FejerRule> FejerRules = new Dictionary<int, FejerRule>();
        private static readonly Lazy<AdaptiveSelection> SelectionCache = new Lazy<AdaptiveSelection>(AdaptiveProtocol.Select);

        /// <summary>Gets the selected immutable adaptive protocol.</summary>
        internal static AdaptiveSelection Selected => SelectionCache.Value;
        /// <summary>Gets the primary half-vector protocol identity.</summary>
        internal static AdaptiveIdentity PrimaryProtocol => AdaptivePrimary.Identity;
        /// <summary>Gets the independent light-space cross-check identity.</summary>
        internal static AdaptiveIdentity CrossCheckProtocol => AdaptiveCrossCheck.Identity;
        /// <summary>Gets the strict selected-protocol artifact path.</summary>
        internal static string CalibrationArtifactPath => Path.Combine(PackageRoot, "Tests", "Fixtures", "Data", "pbr-multiple-scattering-adaptive-oracle-v3.json");

        /// <summary>Integrates the selected primary half-vector directional albedo.</summary>
        internal static AdaptiveResult IntegratePrimary(double p, double v, bool switchBranch) => AdaptivePrimary.Integrate(Selected.Protocol, p, v, switchBranch);
        /// <summary>Integrates the selected light-space cross-check directional albedo.</summary>
        internal static AdaptiveResult IntegrateCrossCheck(double p, double v, bool switchBranch) => AdaptiveCrossCheck.Integrate(Selected.Protocol, p, v, switchBranch);

        /// <summary>Builds the deterministic strict calibration record for a completed selection.</summary>
        internal static string BuildArtifact(ProtocolSelection selection)
        {
            var text = new StringBuilder();
            text.Append("{\n  \"schemaVersion\": 2,\n  \"protocol\": \"purebase-issue14-ggx-smith-f1-oracle-v2\",\n");
            text.Append("  \"codeRevision\": \"").Append(CodeRevision()).Append("\",\n  \"stopReason\": \"selected\",\n");
            text.Append("  \"hlslGuard\": \"direction * rsqrt(max(dot(direction,direction),1e-6))\",\n");
            text.Append("  \"primary\": { \"coordinates\": \"eta-phi\", \"nodes\": \"uniform-midpoint\", \"transform\": \"r2=a2*eta/(1-eta); Hhat=(r*cos(phi),r*sin(phi),1)/sqrt(1+r2)\", \"jacobian\": \"4*q*a2*NdotHhat^3/(2*(1-eta)^2)\", \"order\": \"eta-then-phi\" },\n");
            text.Append("  \"crossCheck\": { \"coordinates\": \"NdotL-phi\", \"nodes\": \"Fejer-II\", \"transform\": \"NdotL=t^3; phi=pi+pi*sign(z)*abs(z)^3\", \"jacobian\": \"(3*t^2/2)*(3*pi*abs(z)^2)\", \"order\": \"light-cosine-then-azimuth\" },\n");
            text.Append("  \"bounds\": { \"energy\": ").Append(D(EnergyBudget)).Append(", \"compensation\": ").Append(D(CompensationBudget)).Append(", \"floatCoefficient\": ").Append(F(CoefficientBudget)).Append(", \"primaryCrossEnergy\": ").Append(D(CrossCheckBudget)).Append(", \"validationP95\": 0.0095, \"validationMaximum\": 0.0195, \"furnaceDelta\": ").Append(D(FurnaceDeltaBudget)).Append(" },\n");
            AppendGrid(text, "audit", Audit, "stable row-major Cartesian plus deduplicated stress pairs");
            AppendGrid(text, "training", Training, "13x13 p-major Chebyshev-Lobatto");
            AppendGrid(text, "validation", Validation, "7x7 p-major fixed endpoint grid");
            AppendMeasurements(text, selection);
            text.Append("  \"selectedRung\": ").Append(selection.CandidateRung).Append(",\n  \"witnessRung\": ").Append(selection.WitnessRung).Append("\n}\n");
            return text.ToString();
        }

        /// <summary>Builds the deterministic selected adaptive v3 record.</summary>
        internal static string BuildArtifact(AdaptiveSelection selection) => AdaptiveProtocol.BuildArtifact(selection);

        /// <summary>Selects the lowest passing candidate after measuring the complete fixed ladder.</summary>
        private static ProtocolSelection SelectProtocol()
        {
            var measurements = new Measurement[Ladder.Length];
            for (int index = 0; index < Ladder.Length; index++) measurements[index] = Measure(Ladder[index]);
            for (int candidate = 0; candidate < Ladder.Length - 1; candidate++)
            {
                PairComparison comparison = Compare(measurements[candidate], measurements[candidate + 1]);
                if (!comparison.Passes) continue;
                var selection = new ProtocolSelection(measurements[candidate].Rung, candidate, candidate + 1, measurements, comparison);
                PersistArtifact(BuildArtifact(selection));
                return selection;
            }

            throw new InvalidOperationException("No selectable finite rung passed its immediate witness: " + FailureSummary(measurements));
        }

        /// <summary>Measures both branches and both independent paths for one fixed ladder rung.</summary>
        private static Measurement Measure(Rung rung)
        {
            var budget = new Budget(ExpectedWork(rung), WallTimeLimit, rung);
            Branch normal = MeasureBranch(rung, false, budget);
            Branch switchBranch = MeasureBranch(rung, true, budget);
            budget.Complete();
            return new Measurement(rung, normal, switchBranch, budget.Work, budget.ElapsedMilliseconds, budget.WorkLimit);
        }

        /// <summary>Measures one denominator branch over the audit, training, and validation grids.</summary>
        private static Branch MeasureBranch(Rung rung, bool switchBranch, Budget budget)
        {
            Sample[] audit = MeasureGrid(rung, Audit, switchBranch, budget);
            Sample[] training = MeasureGrid(rung, Training, switchBranch, budget);
            Sample[] validation = MeasureGrid(rung, Validation, switchBranch, budget);
            float[] coefficients = Fit(training);
            return new Branch(audit, training, validation, coefficients, Validate(validation, coefficients));
        }

        /// <summary>Records raw primary and cross-check directional albedos for a grid in its declared order.</summary>
        private static Sample[] MeasureGrid(Rung rung, Coordinate[] grid, bool switchBranch, Budget budget)
        {
            var samples = new Sample[grid.Length];
            for (int index = 0; index < grid.Length; index++)
            {
                double primary = IntegratePrimary(rung, grid[index].P, grid[index].V, switchBranch, budget);
                double cross = IntegrateCrossCheck(rung, grid[index].P, grid[index].V, switchBranch, budget);
                if (!FinitePositive(primary) || !FinitePositive(cross)) throw new InvalidOperationException("Nonfinite directional albedo at " + CoordinateText(grid[index]) + ".");
                samples[index] = new Sample(primary, cross);
            }

            return samples;
        }

        /// <summary>Integrates the primary in half-vector GGX density coordinates with midpoint nodes.</summary>
        private static double IntegratePrimary(Rung rung, double p, double v, bool switchBranch, Budget budget)
        {
            budget?.Add((long)rung.Eta * rung.Phi);
            double total = 0.0d;
            for (int etaIndex = 0; etaIndex < rung.Eta; etaIndex++)
            {
                double eta = (etaIndex + 0.5d) / rung.Eta;
                for (int phiIndex = 0; phiIndex < rung.Phi; phiIndex++) total += EvaluatePrimary(p, v, eta, (phiIndex + 0.5d) * 2.0d * Math.PI / rung.Phi, switchBranch);
            }

            return total * 2.0d * Math.PI / ((double)rung.Eta * rung.Phi);
        }

        /// <summary>Evaluates a primary reflected-light point with the explicit solid-angle Jacobian.</summary>
        private static double EvaluatePrimary(double p, double v, double eta, double phi, bool switchBranch)
        {
            double a = p * p; double a2 = a * a; double r2 = a2 * eta / (1.0d - eta); double inverse = 1.0d / Math.Sqrt(1.0d + r2);
            var half = new PureBasePbrMultipleScatteringReference.Direction(Math.Sqrt(r2) * Math.Cos(phi) * inverse, Math.Sqrt(r2) * Math.Sin(phi) * inverse, inverse);
            var view = View(v); double q = view.Dot(half); PureBasePbrMultipleScatteringReference.Direction light = half * (2.0d * q) - view;
            if (q <= 0.0d || light.Z <= 0.0d) return 0.0d;
            PureBasePbrMultipleScatteringReference.GuardedTerms terms = PureBasePbrMultipleScatteringReference.EvaluateGuardedTerms(light, view, p, switchBranch);
            double jacobian = 2.0d * q * a2 * half.Z * half.Z * half.Z / ((1.0d - eta) * (1.0d - eta));
            return terms.Distribution * terms.Visibility * light.Z * jacobian;
        }

        /// <summary>Integrates the independent light-space Fejer-II cross-check.</summary>
        private static double IntegrateCrossCheck(Rung rung, double p, double v, bool switchBranch, Budget budget)
        {
            budget?.Add((long)rung.Eta * rung.Phi);
            FejerRule cosine = GetFejerRule(rung.Eta); FejerRule azimuth = GetFejerRule(rung.Phi); double total = 0.0d;
            for (int cosineIndex = 0; cosineIndex < rung.Eta; cosineIndex++)
            for (int azimuthIndex = 0; azimuthIndex < rung.Phi; azimuthIndex++)
                total += cosine.Weights[cosineIndex] * azimuth.Weights[azimuthIndex] * EvaluateCross(p, v, cosine.Nodes[cosineIndex], azimuth.Nodes[azimuthIndex], switchBranch);
            return total;
        }

        /// <summary>Evaluates a boundary-focused Fejer-II light-space point and its mapping Jacobian.</summary>
        private static double EvaluateCross(double p, double v, double cosineNode, double azimuthNode, bool switchBranch)
        {
            double t = 0.5d * (cosineNode + 1.0d); double z = azimuthNode; double ndotL = Math.Pow(t, LightCosinePower);
            double phi = Math.PI + Math.PI * Math.Sign(z) * Math.Pow(Math.Abs(z), LightAzimuthPower);
            double cosineJacobian = 0.5d * LightCosinePower * Math.Pow(t, LightCosinePower - 1); double azimuthJacobian = Math.PI * LightAzimuthPower * Math.Pow(Math.Abs(z), LightAzimuthPower - 1);
            double sinL = Math.Sqrt(Math.Max(0.0d, 1.0d - ndotL * ndotL));
            var light = new PureBasePbrMultipleScatteringReference.Direction(sinL * Math.Cos(phi), sinL * Math.Sin(phi), ndotL);
            PureBasePbrMultipleScatteringReference.GuardedTerms terms = PureBasePbrMultipleScatteringReference.EvaluateGuardedTerms(light, View(v), p, switchBranch);
            return terms.Distribution * terms.Visibility * ndotL * cosineJacobian * azimuthJacobian;
        }

        /// <summary>Creates Fejer-II nodes and weights without sharing a primary node generator.</summary>
        private static FejerRule GetFejerRule(int count)
        {
            if (FejerRules.TryGetValue(count, out FejerRule rule)) return rule;
            var nodes = new double[count]; var weights = new double[count];
            for (int index = 0; index < count; index++)
            {
                double theta = (index + 1.0d) * Math.PI / (count + 1.0d); nodes[index] = Math.Cos(theta); double sum = 0.0d;
                for (int harmonic = 1; harmonic <= count; harmonic += 2) sum += Math.Sin(harmonic * theta) / harmonic;
                weights[index] = 4.0d * Math.Sin(theta) * sum / (count + 1.0d);
            }

            rule = new FejerRule(nodes, weights); FejerRules.Add(count, rule); return rule;
        }

        /// <summary>Fits the unpivoted Householder-QR cubic from primary training energy.</summary>
        private static float[] Fit(Sample[] training)
        {
            int rows = training.Length; var matrix = new double[rows * CoefficientCount]; var targets = new double[rows];
            for (int row = 0; row < rows; row++)
            {
                FillMonomials(matrix, row, Normalize(Training[row].P, PureBasePbrMultipleScatteringReference.RoughnessFloor, 1.0d), Normalize(Training[row].V, 0.0d, 1.0d));
                targets[row] = Compensation(training[row].Primary);
            }

            double[] solved = SolveQr(matrix, targets, rows); var coefficients = new float[CoefficientCount];
            for (int index = 0; index < CoefficientCount; index++) coefficients[index] = (float)solved[index];
            return coefficients;
        }

        /// <summary>Validates the float nested-Horner fit against the fixed compact validation grid.</summary>
        private static FitResult Validate(Sample[] validation, float[] coefficients)
        {
            var errors = new List<float>(validation.Length); bool improves = true;
            for (int index = 0; index < validation.Length; index++)
            {
                double energy = validation[index].Primary; double error = Math.Abs(energy * (1.0d + EvaluateFloat(coefficients, (float)Validation[index].P, (float)Validation[index].V)) - 1.0d);
                errors.Add((float)error); if (Validation[index].P >= 0.5d && 1.0d - energy > 0.001d) improves &= error < 1.0d - energy;
            }

            errors.Sort(); return new FitResult(errors[(int)Math.Ceiling(0.95d * errors.Count) - 1], errors[errors.Count - 1], improves);
        }

        /// <summary>Compares all separate numerical signals between an eligible rung and its witness.</summary>
        private static PairComparison Compare(Measurement candidate, Measurement witness) => new PairComparison(CompareBranch(candidate.Normal, witness.Normal), CompareBranch(candidate.Switch, witness.Switch));

        /// <summary>Compares raw adjacent-path and same-rung signals for one visibility branch.</summary>
        private static BranchComparison CompareBranch(Branch candidate, Branch witness)
        {
            return new BranchComparison(Find(candidate.Audit, witness.Audit, 0, false), Find(candidate.Audit, witness.Audit, 0, true), Find(candidate.Audit, witness.Audit, 1, false), Find(candidate.Audit, witness.Audit, 1, true), FindSame(candidate.Audit), FindSame(witness.Audit), FindCoefficient(candidate.Coefficients, witness.Coefficients), candidate.Fit, witness.Fit);
        }

        /// <summary>Finds a maximum adjacent-rung path difference with raw energy and compensation values.</summary>
        private static Difference Find(Sample[] current, Sample[] next, int path, bool compensation)
        {
            int maximumIndex = 0; double maximum = -1.0d;
            for (int index = 0; index < current.Length; index++)
            {
                double left = path == 0 ? current[index].Primary : current[index].Cross; double right = path == 0 ? next[index].Primary : next[index].Cross;
                double difference = Math.Abs((compensation ? Compensation(left) : left) - (compensation ? Compensation(right) : right));
                if (difference > maximum) { maximum = difference; maximumIndex = index; }
            }

            return new Difference(maximum, maximumIndex, path == 0 ? current[maximumIndex].Primary : current[maximumIndex].Cross, path == 0 ? next[maximumIndex].Primary : next[maximumIndex].Cross);
        }

        /// <summary>Finds the same-rung primary/cross-check energy difference.</summary>
        private static Difference FindSame(Sample[] samples)
        {
            int maximumIndex = 0; double maximum = -1.0d;
            for (int index = 0; index < samples.Length; index++) { double difference = Math.Abs(samples[index].Primary - samples[index].Cross); if (difference > maximum) { maximum = difference; maximumIndex = index; } }
            return new Difference(maximum, maximumIndex, samples[maximumIndex].Primary, samples[maximumIndex].Cross);
        }

        /// <summary>Finds the largest float-rounded coefficient difference.</summary>
        private static CoefficientDifference FindCoefficient(float[] current, float[] next)
        {
            int maximumIndex = 0; float maximum = -1.0f;
            for (int index = 0; index < CoefficientCount; index++) { float difference = Math.Abs(current[index] - next[index]); if (difference > maximum) { maximum = difference; maximumIndex = index; } }
            return new CoefficientDifference(maximum, maximumIndex, current[maximumIndex], next[maximumIndex]);
        }

        /// <summary>Solves the fixed least-squares matrix with unpivoted Householder QR.</summary>
        private static double[] SolveQr(double[] matrix, double[] targets, int rows)
        {
            for (int column = 0; column < CoefficientCount; column++)
            {
                double norm = 0.0d; for (int row = column; row < rows; row++) norm += matrix[row * CoefficientCount + column] * matrix[row * CoefficientCount + column];
                double alpha = matrix[column * CoefficientCount + column] >= 0.0d ? -Math.Sqrt(norm) : Math.Sqrt(norm); var vector = new double[rows - column];
                for (int row = column; row < rows; row++) vector[row - column] = matrix[row * CoefficientCount + column];
                vector[0] -= alpha; ApplyHouseholder(matrix, targets, vector, column, rows); matrix[column * CoefficientCount + column] = alpha;
                for (int row = column + 1; row < rows; row++) matrix[row * CoefficientCount + column] = 0.0d;
            }

            var solution = new double[CoefficientCount];
            for (int row = CoefficientCount - 1; row >= 0; row--) { double sum = targets[row]; for (int column = row + 1; column < CoefficientCount; column++) sum -= matrix[row * CoefficientCount + column] * solution[column]; solution[row] = sum / matrix[row * CoefficientCount + row]; }
            return solution;
        }

        /// <summary>Applies a Householder reflector to the remaining matrix and target columns.</summary>
        private static void ApplyHouseholder(double[] matrix, double[] targets, double[] vector, int start, int rows)
        {
            double norm = 0.0d; foreach (double value in vector) norm += value * value; double scale = 2.0d / norm;
            for (int column = start; column < CoefficientCount; column++) { double projection = 0.0d; for (int row = start; row < rows; row++) projection += vector[row - start] * matrix[row * CoefficientCount + column]; for (int row = start; row < rows; row++) matrix[row * CoefficientCount + column] -= scale * vector[row - start] * projection; }
            double targetProjection = 0.0d; for (int row = start; row < rows; row++) targetProjection += vector[row - start] * targets[row]; for (int row = start; row < rows; row++) targets[row] -= scale * vector[row - start] * targetProjection;
        }

        /// <summary>Appends the declared grid coordinates and their stable evaluation order.</summary>
        private static void AppendGrid(StringBuilder text, string name, Coordinate[] grid, string order)
        {
            text.Append("  \"").Append(name).Append("Grid\": { \"order\": \"").Append(order).Append("\", \"coordinates\": [");
            for (int index = 0; index < grid.Length; index++) text.Append("{ \"p\": ").Append(D(grid[index].P)).Append(", \"ndotV\": ").Append(D(grid[index].V)).Append(" }").Append(index + 1 == grid.Length ? "" : ", ");
            text.Append("] },\n");
        }

        /// <summary>Appends all measured rung summaries and raw audit energy/compensation values.</summary>
        private static void AppendMeasurements(StringBuilder text, ProtocolSelection selection)
        {
            text.Append("  \"ladder\": [\n");
            for (int index = 0; index < selection.Measurements.Length; index++)
            {
                Measurement measurement = selection.Measurements[index]; text.Append("    { \"rung\": ").Append(index).Append(", \"counts\": [").Append(measurement.Rung.Eta).Append(", ").Append(measurement.Rung.Phi).Append("], \"selectable\": ").Append(index < Ladder.Length - 1 ? "true" : "false").Append(", \"work\": ").Append(measurement.Work).Append(", \"workLimit\": ").Append(measurement.WorkLimit).Append(", \"elapsedMilliseconds\": ").Append(measurement.ElapsedMilliseconds).Append(", \"normal\": ");
                AppendBranch(text, measurement.Normal); text.Append(", \"switch\": "); AppendBranch(text, measurement.Switch); text.Append(" }").Append(index + 1 == selection.Measurements.Length ? "\n" : ",\n");
            }

            text.Append("  ],\n  \"selectedComparison\": "); AppendComparison(text, selection.Comparison); text.Append(",\n");
        }

        /// <summary>Appends one branch's raw audit values and fit summary.</summary>
        private static void AppendBranch(StringBuilder text, Branch branch)
        {
            text.Append("{ \"audit\": [");
            for (int index = 0; index < branch.Audit.Length; index++) text.Append("{ \"primaryE\": ").Append(D(branch.Audit[index].Primary)).Append(", \"primaryC\": ").Append(D(Compensation(branch.Audit[index].Primary))).Append(", \"crossE\": ").Append(D(branch.Audit[index].Cross)).Append(", \"crossC\": ").Append(D(Compensation(branch.Audit[index].Cross))).Append(" }").Append(index + 1 == branch.Audit.Length ? "" : ", ");
            text.Append("], \"training\": "); AppendSummary(text, branch.Training); text.Append(", \"validation\": "); AppendSummary(text, branch.Validation); text.Append(", \"fit\": { \"coefficients\": [");
            for (int index = 0; index < branch.Coefficients.Length; index++) text.Append(F(branch.Coefficients[index])).Append(index + 1 == branch.Coefficients.Length ? "" : ", ");
            text.Append("], \"p95\": ").Append(F(branch.Fit.P95)).Append(", \"maximum\": ").Append(F(branch.Fit.Maximum)).Append(", \"highRoughnessImproves\": ").Append(branch.Fit.Improves ? "true" : "false").Append(" } }");
        }

        /// <summary>Appends finite per-path energy/compensation extrema for a non-audit grid.</summary>
        private static void AppendSummary(StringBuilder text, Sample[] samples)
        {
            double minimumPrimary = double.MaxValue; double maximumPrimary = 0.0d; double minimumCross = double.MaxValue; double maximumCross = 0.0d;
            for (int index = 0; index < samples.Length; index++) { minimumPrimary = Math.Min(minimumPrimary, samples[index].Primary); maximumPrimary = Math.Max(maximumPrimary, samples[index].Primary); minimumCross = Math.Min(minimumCross, samples[index].Cross); maximumCross = Math.Max(maximumCross, samples[index].Cross); }
            text.Append("{ \"samples\": ").Append(samples.Length).Append(", \"primaryE\": [").Append(D(minimumPrimary)).Append(", ").Append(D(maximumPrimary)).Append("], \"primaryC\": [").Append(D(Compensation(maximumPrimary))).Append(", ").Append(D(Compensation(minimumPrimary))).Append("], \"crossE\": [").Append(D(minimumCross)).Append(", ").Append(D(maximumCross)).Append("], \"crossC\": [").Append(D(Compensation(maximumCross))).Append(", ").Append(D(Compensation(minimumCross))).Append("] }");
        }

        /// <summary>Appends every separate selected candidate/witness comparison.</summary>
        private static void AppendComparison(StringBuilder text, PairComparison comparison)
        {
            text.Append("{ \"normal\": "); AppendComparison(text, comparison.Normal); text.Append(", \"switch\": "); AppendComparison(text, comparison.Switch); text.Append(" }");
        }

        /// <summary>Appends one visibility branch's maximum comparison diagnostics.</summary>
        private static void AppendComparison(StringBuilder text, BranchComparison comparison)
        {
            text.Append("{ \"primaryEnergy\": "); AppendDifference(text, comparison.PrimaryEnergy); text.Append(", \"primaryCompensation\": "); AppendDifference(text, comparison.PrimaryCompensation); text.Append(", \"crossEnergy\": "); AppendDifference(text, comparison.CrossEnergy); text.Append(", \"crossCompensation\": "); AppendDifference(text, comparison.CrossCompensation); text.Append(", \"candidateSameRung\": "); AppendDifference(text, comparison.CandidateSame); text.Append(", \"witnessSameRung\": "); AppendDifference(text, comparison.WitnessSame); text.Append(", \"coefficientDifference\": ").Append(F(comparison.Coefficient.Difference)).Append(", \"furnaceP95Delta\": ").Append(F(Math.Abs(comparison.CandidateFit.P95 - comparison.WitnessFit.P95))).Append(", \"furnaceMaximumDelta\": ").Append(F(Math.Abs(comparison.CandidateFit.Maximum - comparison.WitnessFit.Maximum))).Append(" }");
        }

        /// <summary>Appends a maximum difference with its raw directional-albedo and compensation values.</summary>
        private static void AppendDifference(StringBuilder text, Difference difference)
        {
            text.Append("{ \"difference\": ").Append(D(difference.Value)).Append(", \"coordinate\": { \"p\": ").Append(D(Audit[difference.Index].P)).Append(", \"ndotV\": ").Append(D(Audit[difference.Index].V)).Append(" }, \"leftE\": ").Append(D(difference.Left)).Append(", \"leftC\": ").Append(D(Compensation(difference.Left))).Append(", \"rightE\": ").Append(D(difference.Right)).Append(", \"rightC\": ").Append(D(Compensation(difference.Right))).Append(" }");
        }

        /// <summary>Persists only a newly selected or byte-identical strict calibration record.</summary>
        private static void PersistArtifact(string artifact)
        {
            if (File.Exists(CalibrationArtifactPath) && File.ReadAllText(CalibrationArtifactPath) != artifact) throw new InvalidOperationException("The existing strict calibration record does not match this selected protocol.");
            if (!File.Exists(CalibrationArtifactPath)) File.WriteAllText(CalibrationArtifactPath, artifact, new UTF8Encoding(false));
        }

        /// <summary>Creates the fixed audit coordinates with stable first-occurrence deduplication.</summary>
        private static Coordinate[] CreateAudit()
        {
            var coordinates = new List<Coordinate>();
            foreach (double p in new[] { 0.089d, 0.25d, 0.5d, 1.0d }) foreach (double v in new[] { 0.0d, 0.05d, 0.5d, 1.0d }) AddUnique(coordinates, p, v);
            foreach (double p in new[] { 0.089d, 0.09d, 0.1d, 0.125d, 0.25d }) foreach (double v in new[] { 0.0d, 0.001d, 0.01d, 0.05d, 0.1d }) AddUnique(coordinates, p, v);
            return coordinates.ToArray();
        }

        /// <summary>Creates the original p-major Chebyshev-Lobatto calibration-training grid.</summary>
        private static Coordinate[] CreateChebyshevGrid()
        {
            var coordinates = new Coordinate[TrainingSamples * TrainingSamples]; int index = 0;
            for (int pIndex = 0; pIndex < TrainingSamples; pIndex++) for (int vIndex = 0; vIndex < TrainingSamples; vIndex++) coordinates[index++] = new Coordinate(Chebyshev(pIndex, 0.089d, 1.0d), Chebyshev(vIndex, 0.0d, 1.0d));
            return coordinates;
        }

        /// <summary>Creates the fixed p-major calibration-validation endpoint grid.</summary>
        private static Coordinate[] CreateValidationGrid()
        {
            double[] p = { 0.089d, 0.09d, 0.1d, 0.125d, 0.25d, 0.5d, 1.0d }; double[] v = { 0.0d, 0.001d, 0.01d, 0.05d, 0.1d, 0.5d, 1.0d };
            var coordinates = new Coordinate[ValidationSamples * ValidationSamples]; int index = 0;
            foreach (double roughness in p) foreach (double view in v) coordinates[index++] = new Coordinate(roughness, view);
            return coordinates;
        }

        /// <summary>Adds one coordinate only when it has not already appeared in the audit order.</summary>
        private static void AddUnique(List<Coordinate> coordinates, double p, double v)
        {
            foreach (Coordinate coordinate in coordinates) if (coordinate.P == p && coordinate.V == v) return;
            coordinates.Add(new Coordinate(p, v));
        }

        /// <summary>Builds a deterministic failure diagnostic without writing an artifact.</summary>
        private static string FailureSummary(Measurement[] measurements)
        {
            var text = new StringBuilder();
            for (int index = 0; index < measurements.Length - 1; index++) { PairComparison comparison = Compare(measurements[index], measurements[index + 1]); text.Append("rung ").Append(index).Append("->").Append(index + 1).Append(" normal primaryE=").Append(D(comparison.Normal.PrimaryEnergy.Value)).Append(" crossE=").Append(D(comparison.Normal.CrossEnergy.Value)).Append(" same=").Append(D(comparison.Normal.CandidateSame.Value)).Append("; "); }
            return text.ToString();
        }

        /// <summary>Reads the package repository HEAD for strict artifact identity.</summary>
        private static string CodeRevision()
        {
            string headPath = Path.Combine(PackageRoot, ".git", "HEAD");
            if (!File.Exists(headPath)) return "unavailable";
            string head = File.ReadAllText(headPath).Trim();
            if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return head;
            string reference = Path.Combine(PackageRoot, ".git", head.Substring(5).Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(reference) ? File.ReadAllText(reference).Trim() : head;
        }

        /// <summary>Gets the package root from Unity's current project path.</summary>
        private static string PackageRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Packages", "jp.penguin.purebase"));
        /// <summary>Creates a view direction with the requested normal cosine.</summary>
        private static PureBasePbrMultipleScatteringReference.Direction View(double v) => new PureBasePbrMultipleScatteringReference.Direction(Math.Sqrt(Math.Max(0.0d, 1.0d - v * v)), 0.0d, v);
        /// <summary>Returns a directional-albedo compensation target.</summary>
        private static double Compensation(double energy) => 1.0d / energy - 1.0d;
        /// <summary>Returns whether a directional albedo is finite and positive.</summary>
        private static bool FinitePositive(double value) => PureBasePbrMultipleScatteringReference.IsFinite(value) && value > 0.0d;
        /// <summary>Normalizes a double to the cubic domain.</summary>
        private static double Normalize(double value, double minimum, double maximum) => ((value - minimum) / (maximum - minimum)) * 2.0d - 1.0d;
        /// <summary>Normalizes a float to the cubic domain.</summary>
        private static float Normalize(float value, float minimum, float maximum) => ((value - minimum) / (maximum - minimum)) * 2.0f - 1.0f;
        /// <summary>Maps an index to a Chebyshev-Lobatto coordinate.</summary>
        private static double Chebyshev(int index, double minimum, double maximum) => minimum + (maximum - minimum) * (1.0d + Math.Cos(Math.PI * index / (TrainingSamples - 1))) * 0.5d;
        /// <summary>Rounds a float through Unity binary16 storage.</summary>
        private static float HalfWiden(float value) => Mathf.HalfToFloat(Mathf.FloatToHalf(value));
        /// <summary>Formats a double as an invariant round-trip decimal.</summary>
        private static string D(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        /// <summary>Formats a float as an invariant round-trip decimal.</summary>
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        /// <summary>Formats a coordinate for an exception.</summary>
        private static string CoordinateText(Coordinate coordinate) => "p=" + D(coordinate.P) + ", ndotV=" + D(coordinate.V);
        /// <summary>Returns deterministic full-grid work for both paths and branches.</summary>
        private static long ExpectedWork(Rung rung) => (long)(Audit.Length + Training.Length + Validation.Length) * 4L * rung.Eta * rung.Phi;

        /// <summary>Builds one x-major cubic monomial row.</summary>
        private static void FillMonomials(double[] matrix, int row, double x, double y)
        {
            int offset = row * CoefficientCount; double xPower = 1.0d;
            for (int xDegree = 0; xDegree < 4; xDegree++) { double yPower = 1.0d; for (int yDegree = 0; yDegree < 4; yDegree++) { matrix[offset + xDegree * 4 + yDegree] = xPower * yPower; yPower *= y; } xPower *= x; }
        }

        /// <summary>Evaluates the float nested-Horner cubic after binary16 input widening.</summary>
        private static float EvaluateFloat(float[] coefficients, float p, float v)
        {
            float x = Normalize(HalfWiden(p), 0.089f, 1.0f); float y = Normalize(HalfWiden(v), 0.0f, 1.0f); float result = 0.0f;
            for (int degree = 3; degree >= 0; degree--) { int offset = degree * 4; float row = ((coefficients[offset + 3] * y + coefficients[offset + 2]) * y + coefficients[offset + 1]) * y + coefficients[offset]; result = result * x + row; }
            return result;
        }

        /// <summary>Defines one finite primary and cross-check rung.</summary>
        internal readonly struct Rung { internal Rung(int eta, int phi) { Eta = eta; Phi = phi; } internal int Eta { get; } internal int Phi { get; } }
        /// <summary>Identifies a numerical integration algorithm independently of its sample count.</summary>
        internal readonly struct ProtocolIdentity { internal ProtocolIdentity(string transform, string nodes, string family) { CoordinateTransform = transform; NodeGenerator = nodes; QuadratureFamily = family; } internal string CoordinateTransform { get; } internal string NodeGenerator { get; } internal string QuadratureFamily { get; } }
        /// <summary>Stores a selected candidate, its immediate witness, and all measured ladder evidence.</summary>
        internal sealed class ProtocolSelection { internal ProtocolSelection(Rung candidate, int candidateRung, int witnessRung, Measurement[] measurements, PairComparison comparison) { Candidate = candidate; CandidateRung = candidateRung; WitnessRung = witnessRung; Measurements = measurements; Comparison = comparison; } internal Rung Candidate { get; } internal int CandidateRung { get; } internal int WitnessRung { get; } internal Measurement[] Measurements { get; } internal PairComparison Comparison { get; } internal bool IsStable => Comparison.Passes; }
        /// <summary>Stores one complete rung measurement across both visibility branches.</summary>
        internal sealed class Measurement { internal Measurement(Rung rung, Branch normal, Branch switchBranch, long work, long elapsedMilliseconds, long workLimit) { Rung = rung; Normal = normal; Switch = switchBranch; Work = work; ElapsedMilliseconds = elapsedMilliseconds; WorkLimit = workLimit; } internal Rung Rung { get; } internal Branch Normal { get; } internal Branch Switch { get; } internal long Work { get; } internal long ElapsedMilliseconds { get; } internal long WorkLimit { get; } }
        /// <summary>Stores all raw grid results and the branch-local float fit.</summary>
        internal sealed class Branch { internal Branch(Sample[] audit, Sample[] training, Sample[] validation, float[] coefficients, FitResult fit) { Audit = audit; Training = training; Validation = validation; Coefficients = coefficients; Fit = fit; } internal Sample[] Audit { get; } internal Sample[] Training { get; } internal Sample[] Validation { get; } internal float[] Coefficients { get; } internal FitResult Fit { get; } }
        /// <summary>Stores both branches' separate candidate/witness comparisons.</summary>
        internal readonly struct PairComparison { internal PairComparison(BranchComparison normal, BranchComparison switchBranch) { Normal = normal; Switch = switchBranch; } internal BranchComparison Normal { get; } internal BranchComparison Switch { get; } internal bool Passes => Normal.Passes && Switch.Passes; }
        /// <summary>Stores all branch-local convergence and validation gates.</summary>
        internal readonly struct BranchComparison { internal BranchComparison(Difference primaryEnergy, Difference primaryCompensation, Difference crossEnergy, Difference crossCompensation, Difference candidateSame, Difference witnessSame, CoefficientDifference coefficient, FitResult candidateFit, FitResult witnessFit) { PrimaryEnergy = primaryEnergy; PrimaryCompensation = primaryCompensation; CrossEnergy = crossEnergy; CrossCompensation = crossCompensation; CandidateSame = candidateSame; WitnessSame = witnessSame; Coefficient = coefficient; CandidateFit = candidateFit; WitnessFit = witnessFit; } internal Difference PrimaryEnergy { get; } internal Difference PrimaryCompensation { get; } internal Difference CrossEnergy { get; } internal Difference CrossCompensation { get; } internal Difference CandidateSame { get; } internal Difference WitnessSame { get; } internal CoefficientDifference Coefficient { get; } internal FitResult CandidateFit { get; } internal FitResult WitnessFit { get; } internal bool Passes => PrimaryEnergy.Value <= EnergyBudget && PrimaryCompensation.Value <= CompensationBudget && CrossEnergy.Value <= EnergyBudget && CrossCompensation.Value <= CompensationBudget && CandidateSame.Value <= CrossCheckBudget && WitnessSame.Value <= CrossCheckBudget && Coefficient.Difference <= CoefficientBudget && CandidateFit.Passes && WitnessFit.Passes && Math.Abs(CandidateFit.P95 - WitnessFit.P95) <= FurnaceDeltaBudget && Math.Abs(CandidateFit.Maximum - WitnessFit.Maximum) <= FurnaceDeltaBudget; }
        /// <summary>Stores raw maximum difference values at one audit coordinate.</summary>
        internal readonly struct Difference { internal Difference(double value, int index, double left, double right) { Value = value; Index = index; Left = left; Right = right; } internal double Value { get; } internal int Index { get; } internal double Left { get; } internal double Right { get; } }
        /// <summary>Stores a maximum float coefficient difference.</summary>
        internal readonly struct CoefficientDifference { internal CoefficientDifference(float difference, int index, float left, float right) { Difference = difference; Index = index; Left = left; Right = right; } internal float Difference { get; } internal int Index { get; } internal float Left { get; } internal float Right { get; } }
        /// <summary>Stores raw primary and independent cross-check energy at one coordinate.</summary>
        internal readonly struct Sample { internal Sample(double primary, double cross) { Primary = primary; Cross = cross; } internal double Primary { get; } internal double Cross { get; } }
        /// <summary>Stores a roughness and normal-view cosine coordinate.</summary>
        internal readonly struct Coordinate { internal Coordinate(double p, double v) { P = p; V = v; } internal double P { get; } internal double V { get; } }
        /// <summary>Stores validation errors and the strict high-roughness improvement result.</summary>
        internal readonly struct FitResult { internal FitResult(float p95, float maximum, bool improves) { P95 = p95; Maximum = maximum; Improves = improves; } internal float P95 { get; } internal float Maximum { get; } internal bool Improves { get; } internal bool Passes => P95 <= 0.0095f && Maximum <= 0.0195f && Improves; }
        /// <summary>Stores one cross-check-only Fejer-II rule.</summary>
        private sealed class FejerRule { internal FejerRule(double[] nodes, double[] weights) { Nodes = nodes; Weights = weights; } internal double[] Nodes { get; } internal double[] Weights { get; } }
        /// <summary>Counts declared work and enforces one fixed rung-local wall-time limit.</summary>
        private sealed class Budget { private readonly Stopwatch stopwatch = Stopwatch.StartNew(); internal Budget(long workLimit, TimeSpan wallTimeLimit, Rung rung) { WorkLimit = workLimit; WallTimeLimit = wallTimeLimit; Rung = rung; } internal long Work { get; private set; } internal long WorkLimit { get; } internal TimeSpan WallTimeLimit { get; } internal Rung Rung { get; } internal long ElapsedMilliseconds => stopwatch.ElapsedMilliseconds; internal void Add(long work) { Work += work; if (Work > WorkLimit || stopwatch.Elapsed > WallTimeLimit) throw new InvalidOperationException("resource-stop rung=" + Rung.Eta + "x" + Rung.Phi + " work=" + Work + "/" + WorkLimit + " elapsedMilliseconds=" + ElapsedMilliseconds + " limitMilliseconds=" + (long)WallTimeLimit.TotalMilliseconds + "."); } internal void Complete() { if (Work != WorkLimit) throw new InvalidOperationException("Non-deterministic calibration work count for rung " + Rung.Eta + "x" + Rung.Phi + "."); } }
    }
}