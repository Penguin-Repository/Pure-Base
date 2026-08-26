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

// Serializes the complete, nonvolatile v4 adaptive numerical-oracle record.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PureBase.Tests.Daily
{
    /// <summary>Serializes the complete, nonvolatile v4 adaptive numerical-oracle record.</summary>
    internal static partial class AdaptiveProtocol
    {
        /// <summary>Builds a selected-only strict JSON record without host, runtime, or elapsed-time data.</summary>
        private static string BuildArtifactRecord(AdaptiveSelection selection)
        {
            if (!selection.IsSelected) throw new InvalidOperationException("A nonselected protocol cannot create a canonical record.");
            var text = new StringBuilder();
            text.Append("{\n  \"schemaVersion\": 4,\n  \"protocol\": \"purebase-issue14-kronrod-oracle-v4\",\n");
            AppendSourceDigests(text);
            AppendFixedContract(text, selection.Protocol);
            AppendGrids(text);
            AppendKronrodRule(text);
            AppendIdentities(text);
            AppendCandidateLadder(text, selection);
            AppendSelection(text, selection);
            text.Append("}\n");
            return text.ToString();
        }

        /// <summary>Appends all five logical source digests while covering split implementation files.</summary>
        private static void AppendSourceDigests(StringBuilder text)
        {
            text.Append("  \"sourceDigests\": { \"primary\": \"").Append(SourceDigest("PureBasePbrMultipleScatteringAdaptivePrimary.cs"));
            text.Append("\", \"crossCheck\": \"").Append(SourceDigest("PureBasePbrMultipleScatteringAdaptiveCrossCheck.cs", "PureBasePbrMultipleScatteringAdaptiveCrossCheckState.cs"));
            text.Append("\", \"protocol\": \"").Append(SourceDigest("PureBasePbrMultipleScatteringAdaptiveProtocol.cs", "PureBasePbrMultipleScatteringAdaptiveProtocolRecord.cs"));
            text.Append("\", \"witness\": \"").Append(SourceDigest("PureBasePbrMultipleScatteringKronrodWitness.cs"));
            text.Append("\", \"scalarKernel\": \"").Append(SourceDigest("PureBasePbrMultipleScatteringReference.cs")).Append("\" },\n");
        }

        /// <summary>Hashes one logical source from one or more ordered package-relative implementation files.</summary>
        private static string SourceDigest(params string[] fileNames)
        {
            var input = new List<byte>();
            foreach (string fileName in fileNames)
            {
                input.AddRange(Encoding.ASCII.GetBytes(fileName));
                input.Add(0);
                input.AddRange(File.ReadAllBytes(Path.Combine(PackageRoot, "Tests", "Daily", "Editor", fileName)));
                input.Add(0);
            }

            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(input.ToArray());
                var result = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        /// <summary>Appends frozen HLSL, epsilon, threshold, selected-setting, and stricter-setting identities.</summary>
        private static void AppendFixedContract(StringBuilder text, AdaptiveSettings settings)
        {
            text.Append("  \"hlslGuard\": \"direction*rsqrt(max(dot(direction,direction),1e-6))\",\n");
            text.Append("  \"epsilonBits\": { \"normal\": \"0x3EE4F8B588E368F1\", \"switch\": \"0x3F10000000000000\", \"safeNormalize\": \"0x3EB0C6F7A0B5ED8D\" },\n");
            text.Append("  \"effectiveSettings\": { \"selected\": ");
            AppendSettings(text, settings);
            text.Append(", \"stricterWitness\": ");
            AppendSettings(text, settings.Witness());
            text.Append(" },\n");
            text.Append("  \"selectionThresholds\": { \"selectedWitnessDifference\": 0.0001, \"crossDifference\": 0.001, \"fit\": { \"coefficientDelta\": 0.0005, \"gainDelta\": 0.0005, \"selectedP95\": 0.0095, \"selectedMaximum\": 0.0195, \"witnessP95Difference\": 0.0005, \"witnessMaximumDifference\": 0.0005, \"highRoughnessImproves\": true } },\n");
        }

        /// <summary>Appends all fixed grids as exact IEEE-754 bits in their declared evaluation order.</summary>
        private static void AppendGrids(StringBuilder text)
        {
            text.Append("  \"grids\": { ");
            AppendGrid(text, "original", "p-then-ndotV-row-major", Original);
            text.Append(", ");
            AppendGrid(text, "stress", "p-then-ndotV-row-major", Stress);
            text.Append(", ");
            AppendGrid(text, "training", "p-then-ndotV-chebyshev-lobatto-row-major", Training);
            text.Append(", ");
            AppendGrid(text, "validation", "p-then-ndotV-row-major", Validation);
            text.Append(" },\n");
        }

        /// <summary>Appends one grid and its point bits without decimal normalization ambiguity.</summary>
        private static void AppendGrid(StringBuilder text, string name, string order, AdaptiveCoordinate[] grid)
        {
            text.Append("\"").Append(name).Append("\": { \"order\": \"").Append(order).Append("\", \"points\": [");
            for (int index = 0; index < grid.Length; index++)
            {
                text.Append("{ \"p\": \"").Append(Bits(grid[index].P)).Append("\", \"ndotV\": \"").Append(Bits(grid[index].V)).Append("\" }");
                if (index + 1 != grid.Length) text.Append(", ");
            }

            text.Append("] }");
        }

        /// <summary>Appends the full endpoint-free K31 rule and its embedded G15 subset in index order.</summary>
        private static void AppendKronrodRule(StringBuilder text)
        {
            AdaptiveCrossCheck.KronrodRuleProbe rule = AdaptiveCrossCheck.GetKronrodRuleForTest();
            text.Append("  \"embeddedRule\": { \"k31\": { \"nodes\": ");
            AppendBits(text, rule.Nodes);
            text.Append(", \"weights\": ");
            AppendBits(text, rule.KronrodWeights);
            text.Append(" }, \"g15\": { \"subset\": ");
            AppendIntegers(text, rule.GaussSubset);
            text.Append(", \"weights\": ");
            AppendBits(text, rule.GaussWeights);
            text.Append(" } },\n");
        }

        /// <summary>Appends a fixed-order array of binary64 bit strings.</summary>
        private static void AppendBits(StringBuilder text, double[] values)
        {
            text.Append("[");
            for (int index = 0; index < values.Length; index++)
            {
                text.Append("\"").Append(Bits(values[index])).Append("\"");
                if (index + 1 != values.Length) text.Append(", ");
            }

            text.Append("]");
        }

        /// <summary>Appends a fixed-order array of nonnegative subset indexes.</summary>
        private static void AppendIntegers(StringBuilder text, int[] values)
        {
            text.Append("[");
            for (int index = 0; index < values.Length; index++)
            {
                text.Append(values[index]);
                if (index + 1 != values.Length) text.Append(", ");
            }

            text.Append("]");
        }

        /// <summary>Appends independent transform, Jacobian, partition, scheduler, tie, and accumulator identities.</summary>
        private static void AppendIdentities(StringBuilder text)
        {
            text.Append("  \"identities\": { ");
            AppendIdentity(text, "primary", AdaptivePrimary.Identity, "embedded in eta transform", "depth then left child");
            text.Append(", ");
            AppendIdentity(text, "crossCheck", AdaptiveCrossCheck.Identity, "embedded in x and tau charts", "rootSegmentId then depth then binaryPath");
            text.Append(", ");
            AppendIdentity(text, "witness", KronrodWitness.Identity, "embedded in t-phi transform", "indicator then directional child order");
            text.Append(" },\n");
        }

        /// <summary>Appends one path's independently implemented execution identity.</summary>
        private static void AppendIdentity(StringBuilder text, string name, AdaptiveIdentity identity, string jacobian, string tie)
        {
            text.Append("\"").Append(name).Append("\": { \"transform\": ");
            AppendJsonString(text, identity.Transform);
            text.Append(", \"jacobian\": ");
            AppendJsonString(text, jacobian);
            text.Append(", \"partition\": ");
            AppendJsonString(text, identity.Splitter);
            text.Append(", \"rule\": ");
            AppendJsonString(text, identity.Rule);
            text.Append(", \"scheduler\": ");
            AppendJsonString(text, identity.Scheduler);
            text.Append(", \"tie\": ");
            AppendJsonString(text, tie);
            text.Append(", \"accumulator\": ");
            AppendJsonString(text, identity.Accumulator);
            text.Append(" }");
        }

        /// <summary>Appends every attempted candidate and its separated grid evidence in candidate order.</summary>
        private static void AppendCandidateLadder(StringBuilder text, AdaptiveSelection selection)
        {
            text.Append("  \"candidateLadder\": [");
            AdaptiveCandidateEvidence[] candidates = selection.CandidateLadder;
            if (candidates.Length == 0) candidates = new[] { new AdaptiveCandidateEvidence(selection.Protocol, selection, selection, selection, "selected-in-memory-probe") };
            for (int index = 0; index < candidates.Length; index++)
            {
                AppendCandidate(text, candidates[index]);
                if (index + 1 != candidates.Length) text.Append(", ");
            }

            text.Append("],\n");
        }

        /// <summary>Appends calibration, Original, canonical-fit, and reason evidence for one candidate.</summary>
        private static void AppendCandidate(StringBuilder text, AdaptiveCandidateEvidence candidate)
        {
            text.Append("{ \"settings\": ");
            AppendSettings(text, candidate.Settings);
            text.Append(", \"stricterWitnessSettings\": ");
            AppendSettings(text, candidate.Settings.Witness());
            text.Append(", \"calibration\": ");
            AppendSelectionEvidence(text, candidate.Calibration, true);
            text.Append(", \"original\": ");
            AppendSelectionEvidence(text, candidate.Original, false);
            text.Append(", \"canonicalFitAndFurnace\": ");
            if (candidate.Canonical == null) text.Append("null"); else AppendSelectionEvidence(text, candidate.Canonical, false);
            text.Append(", \"reason\": ");
            AppendJsonString(text, candidate.Reason);
            text.Append(" }");
        }

        /// <summary>Appends both epsilon branches, splitting combined calibration evidence back by fixed grid order.</summary>
        private static void AppendSelectionEvidence(StringBuilder text, AdaptiveSelection selection, bool calibration)
        {
            text.Append("{ \"normal\": ");
            AppendBranchEvidence(text, selection.Normal, calibration);
            text.Append(", \"switch\": ");
            AppendBranchEvidence(text, selection.Switch, calibration);
            text.Append(", \"passes\": ").Append(selection.IsSelected ? "true" : "false");
            text.Append(", \"stressStable\": ").Append(selection.StressStable ? "true" : "false").Append(" }");
        }

        /// <summary>Appends per-grid primary, witness, cross, indicator, difference, count, stop, fit, and furnace evidence.</summary>
        private static void AppendBranchEvidence(StringBuilder text, AdaptiveBranch branch, bool calibration)
        {
            text.Append("{ \"original\": ");
            AppendGridEvidence(text, branch, Original);
            if (calibration)
            {
                text.Append(", \"stress\": ");
                AppendGridEvidence(text, branch, Stress);
            }

            text.Append(", \"fitAndFurnace\": ");
            AppendFit(text, branch.Fit);
            text.Append(" }");
        }

        /// <summary>Appends evidence in exact fixed-grid order, using the separately measured path outputs.</summary>
        private static void AppendGridEvidence(StringBuilder text, AdaptiveBranch branch, AdaptiveCoordinate[] grid)
        {
            text.Append("[");
            if (!ContainsGridEvidence(branch, grid))
            {
                for (int index = 0; index < branch.Values.Length; index++)
                {
                    AppendEvidence(text, branch.Values[index]);
                    if (index + 1 != branch.Values.Length) text.Append(", ");
                }

                text.Append("]");
                return;
            }

            for (int index = 0; index < grid.Length; index++)
            {
                AdaptiveEvidence evidence = FindEvidence(branch, grid[index]);
                AppendEvidence(text, evidence);
                if (index + 1 != grid.Length) text.Append(", ");
            }

            text.Append("]");
        }

        /// <summary>Gets whether a branch contains every exactly represented point from a fixed grid.</summary>
        private static bool ContainsGridEvidence(AdaptiveBranch branch, AdaptiveCoordinate[] grid)
        {
            foreach (AdaptiveCoordinate point in grid)
            {
                bool found = false;
                foreach (AdaptiveEvidence evidence in branch.Values)
                {
                    found |= BitConverter.DoubleToInt64Bits(evidence.Point.P) == BitConverter.DoubleToInt64Bits(point.P) && BitConverter.DoubleToInt64Bits(evidence.Point.V) == BitConverter.DoubleToInt64Bits(point.V);
                }

                if (!found) return false;
            }

            return true;
        }

        /// <summary>Finds the exactly represented point in a branch without coordinate normalization.</summary>
        private static AdaptiveEvidence FindEvidence(AdaptiveBranch branch, AdaptiveCoordinate point)
        {
            foreach (AdaptiveEvidence evidence in branch.Values)
            {
                if (BitConverter.DoubleToInt64Bits(evidence.Point.P) == BitConverter.DoubleToInt64Bits(point.P) && BitConverter.DoubleToInt64Bits(evidence.Point.V) == BitConverter.DoubleToInt64Bits(point.V)) return evidence;
            }

            throw new InvalidOperationException("The declared grid point is absent from candidate evidence: " + point.Text + ".");
        }

        /// <summary>Appends one complete primary, witness, and cross-path sample record.</summary>
        private static void AppendEvidence(StringBuilder text, AdaptiveEvidence evidence)
        {
            text.Append("{ \"point\": { \"p\": \"").Append(Bits(evidence.Point.P)).Append("\", \"ndotV\": \"").Append(Bits(evidence.Point.V)).Append("\" }, \"primary\": ");
            AppendResult(text, evidence.Primary);
            text.Append(", \"witness\": ");
            AppendResult(text, evidence.Witness);
            text.Append(", \"cross\": ");
            AppendResult(text, evidence.Cross);
            text.Append(", \"primaryWitnessDifference\": ").Append(D(Math.Abs(evidence.Primary.Value - evidence.Witness.Value)));
            text.Append(", \"crossDifference\": ").Append(D(evidence.CrossDifference));
            text.Append(", \"passes\": ").Append(evidence.Passes ? "true" : "false").Append(" }");
        }

        /// <summary>Appends one result's raw value, indicator, cap counts, and deterministic stop state.</summary>
        private static void AppendResult(StringBuilder text, AdaptiveResult result)
        {
            text.Append("{ \"raw\": ").Append(D(result.Value));
            text.Append(", \"indicator\": ").Append(D(result.Error));
            text.Append(", \"tolerance\": ").Append(D(result.Tolerance));
            text.Append(", \"evaluations\": ").Append(result.Evaluations);
            text.Append(", \"panels\": ").Append(result.Panels);
            text.Append(", \"depth\": ").Append(result.Depth);
            text.Append(", \"stopState\": ");
            AppendJsonString(text, result.Diagnostic ?? "accepted");
            text.Append(", \"accepted\": ").Append(result.IsAccepted ? "true" : "false").Append(" }");
        }

        /// <summary>Appends coefficient, gain, compact-validation, and furnace improvement evidence.</summary>
        private static void AppendFit(StringBuilder text, AdaptiveFit fit)
        {
            text.Append("{ \"coefficientDelta\": ").Append(F(fit.CoefficientDelta));
            text.Append(", \"gainDelta\": ").Append(F(fit.GainDelta));
            text.Append(", \"selectedP95\": ").Append(F(fit.P95));
            text.Append(", \"selectedMaximum\": ").Append(F(fit.Maximum));
            text.Append(", \"witnessP95\": ").Append(F(fit.WitnessP95));
            text.Append(", \"witnessMaximum\": ").Append(F(fit.WitnessMaximum));
            text.Append(", \"highRoughnessImproves\": ").Append(fit.Improves ? "true" : "false");
            text.Append(", \"stable\": ").Append(fit.Stable ? "true" : "false").Append(" }");
        }

        /// <summary>Appends one effective settings object with all fixed tolerance and cap values.</summary>
        private static void AppendSettings(StringBuilder text, AdaptiveSettings settings)
        {
            text.Append("{ \"name\": ");
            AppendJsonString(text, settings.Name);
            text.Append(", \"absolute\": ").Append(D(settings.Absolute));
            text.Append(", \"relative\": ").Append(D(settings.Relative));
            text.Append(", \"witnessAbsolute\": ").Append(D(settings.WitnessAbsolute));
            text.Append(", \"witnessRelative\": ").Append(D(settings.WitnessRelative));
            text.Append(", \"caps\": { \"maxDepth\": ").Append(settings.MaxDepth);
            text.Append(", \"maxPanels\": ").Append(settings.MaxPanels);
            text.Append(", \"maxEvaluations\": ").Append(settings.MaxEvaluations).Append(" } }");
        }

        /// <summary>Appends the selected result and explicit selection reason after every candidate entry.</summary>
        private static void AppendSelection(StringBuilder text, AdaptiveSelection selection)
        {
            text.Append("  \"selection\": { \"state\": \"selected\", \"reason\": \"all frozen candidate, witness, cross, fit, and furnace gates passed\"");
            text.Append(", \"normalPasses\": ").Append(selection.Normal.Passes ? "true" : "false");
            text.Append(", \"switchPasses\": ").Append(selection.Switch.Passes ? "true" : "false");
            text.Append(", \"stressStable\": ").Append(selection.StressStable ? "true" : "false").Append(" }\n");
        }

        /// <summary>Escapes a string using only strict JSON ASCII escape forms.</summary>
        private static void AppendJsonString(StringBuilder text, string value)
        {
            text.Append("\"");
            foreach (char character in value)
            {
                if (character == '\\') text.Append("\\\\");
                else if (character == '"') text.Append("\\\"");
                else if (character == '\n') text.Append("\\n");
                else if (character == '\r') text.Append("\\r");
                else if (character == '\t') text.Append("\\t");
                else if (character < 0x20 || character > 0x7e) text.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                else text.Append(character);
            }

            text.Append("\"");
        }
    }
}