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

// Tests the immutable, direct-only primary outcome census.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    /// <summary>Tests the immutable, direct-only primary outcome census.</summary>
    public sealed partial class PureBasePbrMultipleScatteringCompensationTests
    {
        private const string PrimaryOutcomeCensusEvidenceTestName = "PureBase.Tests.Daily.PureBasePbrMultipleScatteringCompensationTests.PrimaryOutcomeCensusRendersFullDeterministicEvidence";
        private const string PrimaryOutcomeCensusEvidenceFileName = "PureBase.Tests.Daily.PureBasePbrMultipleScatteringCompensationTests.PrimaryOutcomeCensusRendersFullDeterministicEvidence.envelope";

        /// <summary>Requires the source-ordered exact-bit union to retain all frozen-grid memberships and both branches.</summary>
        [Test]
        public void PrimaryOutcomeCensusUsesExactBitUnionAndBothBranches()
        {
            PrimaryOutcomeCensus census = AdaptiveProtocol.RunPrimaryOutcomeCensusForTest(); Assert.That(census.Coordinates.Count, Is.GreaterThan(0)); Assert.That(census.Rows.Count, Is.EqualTo(census.Coordinates.Count * 2));
            Assert.That(Count(census.Coordinates, input => input.IsTraining), Is.EqualTo(169)); Assert.That(Count(census.Coordinates, input => input.IsValidation), Is.EqualTo(49)); Assert.That(Count(census.Coordinates, input => input.IsOriginal), Is.EqualTo(16)); Assert.That(Count(census.Coordinates, input => input.IsStress), Is.EqualTo(25));
            for (int index = 0; index < census.Coordinates.Count; index++) AssertBranches(census.Rows, index, census.Coordinates[index]);
        }

        /// <summary>Requires the frozen calibration-a input to preserve its raw direct-primary observation semantics.</summary>
        [Test]
        public void PrimaryOutcomeCensusMatchesCalibrationADirectDiagnostic()
        {
            PrimaryDiagnosticRun expected = AdaptiveProtocol.RunCalibrationAPrimaryDiagnosticForTest(); PrimaryOutcomeRow actual = Find(census: AdaptiveProtocol.RunPrimaryOutcomeCensusForTest(), p: 0.089d, v: 0.0d, switchBranch: false);
            Assert.That(actual.State, Is.EqualTo(expected.State)); AssertResultEqual(expected.Result, actual.Run.Result); Assert.That(expected.Result.Diagnostic, Does.Contain("direct-primary-diagnostic")); Assert.That(actual.Run.Result.Diagnostic, Does.Contain("primary-outcome-census")); Assert.That(actual.Run.Trace.Used, Is.EqualTo(expected.Trace.Used)); Assert.That(actual.Run.Trace.Limit, Is.EqualTo(expected.Trace.Limit));
            PrimaryDiagnosticSnapshot expectedSnapshot = RequireOutcomeSnapshot(expected); PrimaryDiagnosticSnapshot actualSnapshot = RequireOutcomeSnapshot(actual.Run); Assert.That(actualSnapshot.CompletedKernelWork, Is.EqualTo(expectedSnapshot.CompletedKernelWork)); Assert.That(actualSnapshot.Result.Evaluations, Is.EqualTo(expectedSnapshot.Result.Evaluations)); Assert.That(actualSnapshot.TerminalSample.KernelStarted, Is.EqualTo(expectedSnapshot.TerminalSample.KernelStarted)); Assert.That(actualSnapshot.TerminalSample.Stop, Is.EqualTo(expectedSnapshot.TerminalSample.Stop)); AssertPreKernelRejection(actual.Run.Trace);
        }

        /// <summary>Requires every branch row to retain an independent finite limit, trace, terminal sample, and supported taxonomy.</summary>
        [Test]
        public void PrimaryOutcomeCensusRetainsIndependentEvidenceAndExhaustiveTaxonomy()
        {
            PrimaryOutcomeCensus census = AdaptiveProtocol.RunPrimaryOutcomeCensusForTest(); Assert.That(Enum.GetValues(typeof(PrimaryDiagnosticState)), Is.EquivalentTo(new[] { PrimaryDiagnosticState.Accepted, PrimaryDiagnosticState.BudgetExhausted, PrimaryDiagnosticState.EvaluationCap, PrimaryDiagnosticState.PanelCap, PrimaryDiagnosticState.DepthCap, PrimaryDiagnosticState.GlobalError, PrimaryDiagnosticState.Nonfinite, PrimaryDiagnosticState.OtherLimit, PrimaryDiagnosticState.Faulted }));
            foreach (PrimaryOutcomeRow row in census.Rows) { Assert.That(row.ExecutionLimit, Is.EqualTo(512)); Assert.That(row.Run.Trace.Limit, Is.EqualTo(512)); Assert.That(row.Run.Snapshot, Is.Not.Null); Assert.That(row.Run.Snapshot.TerminalSample.Axis, Is.Not.Null); Assert.That(row.Run.Snapshot.Partitions.Count, Is.GreaterThan(0)); Assert.That(Enum.IsDefined(typeof(PrimaryDiagnosticState), row.State), Is.True); if (row.State == PrimaryDiagnosticState.Faulted) { Assert.That(row.ExceptionType, Is.Not.Null); Assert.That(row.ExceptionMessage, Is.Not.Null); } }
            PrimaryDiagnosticRun nonfinite = AdaptiveProtocol.RunNonfinitePrimaryDiagnosticForTest(); Assert.That(nonfinite.State, Is.EqualTo(PrimaryDiagnosticState.Nonfinite)); Assert.That(nonfinite.Exception, Is.Null);
        }

        /// <summary>Requires repeated primary-only observations and their nonpersistent renderer to leave cache and artifact state unchanged.</summary>
        [Test]
        public void PrimaryOutcomeCensusIsDeterministicAndCacheArtifactIsolated()
        {
            string path = AdaptiveProtocol.CanonicalArtifactPath; bool existed = File.Exists(path); byte[] before = existed ? File.ReadAllBytes(path) : null; Lazy<AdaptiveSelection> cache = GetOutcomeSelectionCache(); bool cacheBefore = cache.IsValueCreated;
            PrimaryOutcomeCensus first = AdaptiveProtocol.RunPrimaryOutcomeCensusForTest(); PrimaryOutcomeCensus second = AdaptiveProtocol.RunPrimaryOutcomeCensusForTest(); string rendered = PrimaryOutcomeCensusRenderer.Render(first);
            Assert.That(PrimaryOutcomeCensusRenderer.Render(second), Is.EqualTo(rendered)); Assert.That(rendered, Does.StartWith("primary-outcome-census version=2 observation=primary-only nonpersistent=true")); Assert.That(rendered, Does.Not.Contain("fit")); Assert.That(rendered, Does.Not.Contain("policy")); Assert.That(cache.IsValueCreated, Is.EqualTo(cacheBefore)); Assert.That(File.Exists(path), Is.EqualTo(existed)); if (existed) Assert.That(File.ReadAllBytes(path), Is.EqualTo(before));
        }

        /// <summary>Writes the complete stable primary-only census evidence for ordinary Unity test output.</summary>
        [Test]
        public void PrimaryOutcomeCensusRendersFullDeterministicEvidence()
        {
            PreparePrimaryOutcomeCensusEvidenceDirectory();
            PrimaryOutcomeCensus census = AdaptiveProtocol.RunPrimaryOutcomeCensusForTest(); string rendered = PrimaryOutcomeCensusRenderer.Render(census);
            Assert.That(rendered, Does.StartWith("primary-outcome-census version=2 observation=primary-only nonpersistent=true selection-verdict=none shader-safety-certification=none")); Assert.That(rendered, Does.Contain("frozenCandidate=Candidates[0]")); Assert.That(rendered, Does.Contain("fixedPerRowReservationLimit=512")); Assert.That(rendered, Does.Contain("sourceCounts={training=169 validation=49 original=16 stress=25}")); Assert.That(rendered, Does.Contain("categoryCounts={"));
            foreach (PrimaryOutcomeRow row in census.Rows) Assert.That(rendered, Does.Contain("row={index=" + row.CoordinateIndex + " coordinate={"));
            foreach (PrimaryOutcomeRow row in census.Rows) if (row.State != PrimaryDiagnosticState.Accepted) AssertNonAcceptedEvidence(rendered, row);
            TestContext.Progress.WriteLine(rendered);
            string currentTestId = TestContext.CurrentContext.Test.ID; Assert.That(currentTestId, Is.Not.Empty);
            byte[] report = Utf8NoBom.GetBytes(rendered); byte[] envelope = CreatePrimaryOutcomeCensusEvidenceEnvelope(report, currentTestId); string hash = GetSha256(report);
            Assert.That(CreatePrimaryOutcomeCensusEvidenceEnvelope(report, currentTestId), Is.EqualTo(envelope)); Assert.That(GetSha256(report), Is.EqualTo(hash));
            PublishPrimaryOutcomeCensusEvidence(envelope, report, hash, currentTestId); AssertFinalPrimaryOutcomeCensusEvidenceIsReadable(report, hash, currentTestId);
            CleanupPrimaryOutcomeCensusEvidenceForTest(); AssertNoPrimaryOutcomeCensusEvidenceArtifacts();
            PublishPrimaryOutcomeCensusEvidence(envelope, report, hash, currentTestId); AssertFinalPrimaryOutcomeCensusEvidenceIsReadable(report, hash, currentTestId);
        }

        /// <summary>Removes the named test's transient evidence paths without enumerating other Temp content.</summary>
        [Test]
        public void PrimaryOutcomeCensusEvidenceCleanupRemovesPublishedPaths()
        {
            CleanupPrimaryOutcomeCensusEvidenceForTest(); AssertNoPrimaryOutcomeCensusEvidenceArtifacts();
        }

        /// <summary>Removes only this test's final and pending transient evidence files.</summary>
        internal static void CleanupPrimaryOutcomeCensusEvidenceForTest()
        {
            if (File.Exists(PrimaryOutcomeCensusEvidenceFinalPath)) File.Delete(PrimaryOutcomeCensusEvidenceFinalPath);
            if (File.Exists(PrimaryOutcomeCensusEvidencePendingPath)) File.Delete(PrimaryOutcomeCensusEvidencePendingPath);
        }

        /// <summary>Counts source memberships without changing the immutable input list.</summary>
        private static int Count(IReadOnlyList<PrimaryOutcomeCoordinate> values, Func<PrimaryOutcomeCoordinate, bool> predicate)
        {
            int count = 0; foreach (PrimaryOutcomeCoordinate value in values) if (predicate(value)) count++; return count;
        }

        /// <summary>Requires exactly one normal and one switch row with the input's exact membership evidence.</summary>
        private static void AssertBranches(IReadOnlyList<PrimaryOutcomeRow> rows, int index, PrimaryOutcomeCoordinate input)
        {
            int normal = 0; int switched = 0; foreach (PrimaryOutcomeRow row in rows) if (row.CoordinateIndex == index && SameBits(row.Input.Coordinate, input.Coordinate)) { if (row.SwitchBranch) switched++; else normal++; }
            Assert.That(normal, Is.EqualTo(1)); Assert.That(switched, Is.EqualTo(1));
        }

        /// <summary>Finds one branch row using the exact binary64 coordinate identity.</summary>
        private static PrimaryOutcomeRow Find(PrimaryOutcomeCensus census, double p, double v, bool switchBranch)
        {
            foreach (PrimaryOutcomeRow row in census.Rows) if (row.SwitchBranch == switchBranch && BitConverter.DoubleToInt64Bits(row.Input.Coordinate.P) == BitConverter.DoubleToInt64Bits(p) && BitConverter.DoubleToInt64Bits(row.Input.Coordinate.V) == BitConverter.DoubleToInt64Bits(v)) return row;
            Assert.Fail("The requested primary outcome row was not present."); return null;
        }

        /// <summary>Requires a completed capture snapshot before inspecting direct primary evidence.</summary>
        private static PrimaryDiagnosticSnapshot RequireOutcomeSnapshot(PrimaryDiagnosticRun run)
        {
            Assert.That(run.Exception, Is.Null); Assert.That(run.Snapshot, Is.Not.Null); return run.Snapshot;
        }

        /// <summary>Compares raw direct result fields that do not embed the intentionally distinct execution context.</summary>
        private static void AssertResultEqual(AdaptiveResult expected, AdaptiveResult actual)
        {
            AssertBits(expected.Value, actual.Value); AssertBits(expected.Error, actual.Error); AssertBits(expected.Tolerance, actual.Tolerance); Assert.That(actual.Evaluations, Is.EqualTo(expected.Evaluations)); Assert.That(actual.Panels, Is.EqualTo(expected.Panels)); Assert.That(actual.Depth, Is.EqualTo(expected.Depth));
        }

        /// <summary>Requires the captured reservation rejection to occur before scalar-kernel execution.</summary>
        private static void AssertPreKernelRejection(SelectionExecutionTrace trace)
        {
            Assert.That(trace.FirstRejection.HasValue, Is.True); Assert.That(trace.FirstRejection.Value.KernelStarted, Is.False); Assert.That(trace.Used, Is.EqualTo(trace.Limit));
        }

        /// <summary>Requires every non-accepted row to retain its terminal and reservation evidence in the report.</summary>
        private static void AssertNonAcceptedEvidence(string rendered, PrimaryOutcomeRow row)
        {
            string prefix = "row={index=" + row.CoordinateIndex + " coordinate={"; string branch = "} branch=" + (row.SwitchBranch ? "switch" : "normal"); int start = 0; string evidence = null;
            while ((start = rendered.IndexOf(prefix, start, StringComparison.Ordinal)) >= 0) { int end = rendered.IndexOf("\nrow={", start + prefix.Length, StringComparison.Ordinal); string candidate = end < 0 ? rendered.Substring(start) : rendered.Substring(start, end - start); if (candidate.Contains(branch)) { evidence = candidate; break; } start += prefix.Length; }
            Assert.That(evidence, Is.Not.Null); Assert.That(evidence, Does.Contain("state=" + row.State)); Assert.That(evidence, Does.Contain("reservations={used=" + row.Run.Trace.Used + " limit=" + row.Run.Trace.Limit)); Assert.That(evidence, Does.Contain("firstRejection=")); Assert.That(evidence, Does.Contain("terminal={axis=")); Assert.That(evidence, Does.Contain("partitions=[")); Assert.That(evidence, Does.Contain("work=[")); Assert.That(evidence, Does.Contain("splits=["));
        }

        /// <summary>Compares one coordinate pair by its exact binary64 bit patterns.</summary>
        private static bool SameBits(AdaptiveCoordinate left, AdaptiveCoordinate right) => BitConverter.DoubleToInt64Bits(left.P) == BitConverter.DoubleToInt64Bits(right.P) && BitConverter.DoubleToInt64Bits(left.V) == BitConverter.DoubleToInt64Bits(right.V);
        /// <summary>Compares two binary64 values without numeric-equivalence normalization.</summary>
        private static void AssertBits(double expected, double actual) => Assert.That(BitConverter.DoubleToInt64Bits(actual), Is.EqualTo(BitConverter.DoubleToInt64Bits(expected)));

        /// <summary>Gets the private canonical selection cache without forcing its lazy value.</summary>
        private static Lazy<AdaptiveSelection> GetOutcomeSelectionCache()
        {
            FieldInfo field = typeof(PureBasePbrMultipleScatteringFurnaceOracle).GetField("SelectionCache", BindingFlags.NonPublic | BindingFlags.Static); return (Lazy<AdaptiveSelection>)field.GetValue(null);
        }

        /// <summary>Gets the deterministic final evidence path below the Unity workspace Temp directory.</summary>
        private static string PrimaryOutcomeCensusEvidenceFinalPath => Path.Combine(PrimaryOutcomeCensusEvidenceDirectory, PrimaryOutcomeCensusEvidenceFileName);

        /// <summary>Gets the same-volume pending path used before atomically publishing the final evidence.</summary>
        private static string PrimaryOutcomeCensusEvidencePendingPath => PrimaryOutcomeCensusEvidenceFinalPath + ".pending";

        /// <summary>Gets the dedicated transient evidence directory without depending on a host-specific profile path.</summary>
        private static string PrimaryOutcomeCensusEvidenceDirectory => Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Temp", "PureBase.TestEvidence");

        /// <summary>Gets the fixed UTF-8 encoding used for the byte-identical report body.</summary>
        private static UTF8Encoding Utf8NoBom => new UTF8Encoding(false);

        /// <summary>Creates the dedicated directory and proves that only the named stale paths were removed.</summary>
        private static void PreparePrimaryOutcomeCensusEvidenceDirectory()
        {
            Directory.CreateDirectory(PrimaryOutcomeCensusEvidenceDirectory); CleanupPrimaryOutcomeCensusEvidenceForTest(); AssertNoPrimaryOutcomeCensusEvidenceArtifacts();
        }

        /// <summary>Builds the fixed-LF ASCII envelope around the renderer's unchanged UTF-8 report bytes.</summary>
        private static byte[] CreatePrimaryOutcomeCensusEvidenceEnvelope(byte[] report, string currentTestId)
        {
            string header = "format-version=1\nexpected-full-test-name=" + PrimaryOutcomeCensusEvidenceTestName + "\ncurrent-nunit-test-id=" + currentTestId + "\nreport-byte-length=" + report.Length.ToString(CultureInfo.InvariantCulture) + "\nreport-sha256=" + GetSha256(report) + "\n---\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header); byte[] envelope = new byte[headerBytes.Length + report.Length]; Buffer.BlockCopy(headerBytes, 0, envelope, 0, headerBytes.Length); Buffer.BlockCopy(report, 0, envelope, headerBytes.Length, report.Length); return envelope;
        }

        /// <summary>Writes, flushes, validates, and atomically publishes one complete transient evidence envelope.</summary>
        private static void PublishPrimaryOutcomeCensusEvidence(byte[] envelope, byte[] report, string hash, string currentTestId)
        {
            AssertNoPrimaryOutcomeCensusEvidenceArtifacts(); WriteAndFlushPrimaryOutcomeCensusEvidence(PrimaryOutcomeCensusEvidencePendingPath, envelope); AssertPrimaryOutcomeCensusEvidenceEnvelope(PrimaryOutcomeCensusEvidencePendingPath, report, hash, currentTestId);
            File.Move(PrimaryOutcomeCensusEvidencePendingPath, PrimaryOutcomeCensusEvidenceFinalPath); Assert.That(File.Exists(PrimaryOutcomeCensusEvidencePendingPath), Is.False); AssertPrimaryOutcomeCensusEvidenceEnvelope(PrimaryOutcomeCensusEvidenceFinalPath, report, hash, currentTestId);
        }

        /// <summary>Writes one pending envelope synchronously so a later reader cannot observe an unflushed body.</summary>
        private static void WriteAndFlushPrimaryOutcomeCensusEvidence(string path, byte[] envelope)
        {
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(envelope, 0, envelope.Length); stream.Flush(true); }
        }

        /// <summary>Validates every fixed header field, delimiter, hash, and body byte in a published envelope.</summary>
        private static void AssertPrimaryOutcomeCensusEvidenceEnvelope(string path, byte[] report, string hash, string currentTestId)
        {
            string header = "format-version=1\nexpected-full-test-name=" + PrimaryOutcomeCensusEvidenceTestName + "\ncurrent-nunit-test-id=" + currentTestId + "\nreport-byte-length=" + report.Length.ToString(CultureInfo.InvariantCulture) + "\nreport-sha256=" + hash + "\n---\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(header); byte[] envelope = File.ReadAllBytes(path); Assert.That(envelope.Length, Is.EqualTo(headerBytes.Length + report.Length)); Assert.That(Encoding.ASCII.GetString(envelope, 0, headerBytes.Length), Is.EqualTo(header));
            byte[] body = new byte[report.Length]; Buffer.BlockCopy(envelope, headerBytes.Length, body, 0, body.Length); Assert.That(body, Is.EqualTo(report)); Assert.That(GetSha256(body), Is.EqualTo(hash));
        }

        /// <summary>Reopens the final path to prove that an external reader can recover the validated report body.</summary>
        private static void AssertFinalPrimaryOutcomeCensusEvidenceIsReadable(byte[] report, string hash, string currentTestId)
        {
            AssertPrimaryOutcomeCensusEvidenceEnvelope(PrimaryOutcomeCensusEvidenceFinalPath, report, hash, currentTestId); using (FileStream stream = new FileStream(PrimaryOutcomeCensusEvidenceFinalPath, FileMode.Open, FileAccess.Read, FileShare.Read)) Assert.That(stream.Length, Is.GreaterThan(report.Length));
        }

        /// <summary>Requires both exact transient paths to be absent without inspecting unrelated Temp files.</summary>
        private static void AssertNoPrimaryOutcomeCensusEvidenceArtifacts()
        {
            Assert.That(File.Exists(PrimaryOutcomeCensusEvidenceFinalPath), Is.False); Assert.That(File.Exists(PrimaryOutcomeCensusEvidencePendingPath), Is.False);
        }

        /// <summary>Computes the uppercase SHA-256 representation used by the fixed envelope header.</summary>
        private static string GetSha256(byte[] content)
        {
            using (SHA256 algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(content)).Replace("-", string.Empty);
        }
    }
}
