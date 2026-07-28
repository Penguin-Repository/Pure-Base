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

// Verifies external observation candidate validation and explicit apply seams through Unity-discoverable EditMode tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using PureBase.Tests.Daily;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Verifies external observation candidate validation and explicit apply seams through Unity-discoverable EditMode tests.</summary>
    public sealed class PureBaseRegressionObservationCandidateTests
    {
        /// <summary>Ensures one external candidate path is normalized while unrelated arguments are ignored.</summary>
        [Test]
        public void CandidatePathReturnsNormalizedExternalPathAmongUnrelatedArguments()
        {
            string suppliedPath = Path.Combine(Path.GetTempPath(), "purebase-observation-candidate", "..", "candidate.json");
            string expectedPath = Path.GetFullPath(suppliedPath);
            string actualPath = PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(new[] { "-batchmode", "-logFile", "editor.log", PureBaseRegressionBaselineGenerator.ObservationCandidatePathArgument, suppliedPath, "-quit" }, PureBaseRegressionBaselineGenerator.ObservationCandidatePathArgument);
            Assert.That(actualPath, Is.EqualTo(expectedPath));
        }

        /// <summary>Ensures duplicate, empty, whitespace, and missing candidate path values are rejected.</summary>
        [Test]
        public void CandidatePathRejectsDuplicateEmptyWhitespaceAndMissingValues()
        {
            string argumentName = PureBaseRegressionBaselineGenerator.ObservationCandidatePathArgument;
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(new[] { argumentName, Path.GetTempPath(), argumentName, Path.GetTempPath() }, argumentName));
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(new[] { argumentName, string.Empty }, argumentName));
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(new[] { argumentName, "   " }, argumentName));
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(new[] { "-batchmode", argumentName }, argumentName));
        }

        /// <summary>Ensures the batch entries reject missing and relative path arguments before scene mutation.</summary>
        [Test]
        public void CandidatePathsRejectMissingAndRelativeArguments()
        {
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(Array.Empty<string>(), PureBaseRegressionBaselineGenerator.ObservationCandidatePathArgument));
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(new[] { PureBaseRegressionBaselineGenerator.ObservationCandidatePathArgument, "relative.json" }, PureBaseRegressionBaselineGenerator.ObservationCandidatePathArgument));
        }

        /// <summary>Ensures the batch entries reject paths inside the embedded package before scene mutation.</summary>
        [Test]
        public void CandidatePathsRejectPackageScope()
        {
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            Assert.That(projectRoot, Is.Not.Null);
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.ValidateExternalCandidatePath(Path.Combine(projectRoot.FullName, "Packages", "jp.penguin.purebase", "candidate.json")));
        }

        /// <summary>Ensures the batch entries reject paths inside the Unity Assets import scope before scene mutation.</summary>
        [Test]
        public void CandidatePathsRejectAssetsImportScope() => Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.ValidateExternalCandidatePath(Path.Combine(Application.dataPath, "candidate.json")));

        /// <summary>Ensures missing and incompatible candidate JSON is rejected before a candidate can be applied.</summary>
        [Test]
        public void CandidateSerializationRejectsMissingAndInvalidSchemas()
        {
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.DeserializeObservationCandidate(null));
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.DeserializeObservationCandidate("{}"));
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.DeserializeObservationCandidate("{\"schemaVersion\":99}"));
        }

        /// <summary>Ensures environment mismatches fail before the apply transaction begins.</summary>
        [Test]
        public void CandidateEnvironmentMismatchFailsBeforeWriteBoundary()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate candidate = CreateValidCandidate();
            candidate.graphicsDevice = GraphicsDeviceType.Direct3D12.ToString();
            var writer = new RecordingCandidateWriter();
            var boundary = new RecordingWriteBoundary();
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.ApplyReviewedCandidate(CreateValidEnvironment(), candidate, writer, boundary));
            Assert.That(writer.WriteCallCount, Is.Zero);
            Assert.That(boundary.BeginCallCount, Is.Zero);
        }

        /// <summary>Ensures the candidate header cannot conceal incompatible embedded baseline environment metadata.</summary>
        [Test]
        public void CandidateEmbeddedBaselineEnvironmentMismatchFailsBeforeWriteBoundary()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate candidate = CreateValidCandidate();
            candidate.exactBaseline.renderPipeline = "Custom";
            AssertApplyFailsBeforeWrite(candidate);
        }

        /// <summary>Ensures candidates cannot omit or zero an observable Meta signal.</summary>
        [Test]
        public void CandidateMissingOrZeroMetaFailsBeforeWriteBoundary()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate missingMeta = CreateValidCandidate();
            missingMeta.observation.metaAlbedo = null;
            AssertApplyFailsBeforeWrite(missingMeta);
            PureBaseRegressionBaselineGenerator.ObservationCandidate zeroMeta = CreateValidCandidate();
            zeroMeta.observation.metaAlbedo[0].meanLuminance = 0.0f;
            zeroMeta.exactBaseline.metaAlbedo[0].meanLuminance = FloatRange.Exact(0.0f);
            AssertApplyFailsBeforeWrite(zeroMeta);
        }

        /// <summary>Ensures candidates cannot zero the directional shadow signal.</summary>
        [Test]
        public void CandidateZeroShadowFailsBeforeWriteBoundary()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate candidate = CreateValidCandidate();
            candidate.observation.shadowChangedPixelCount = 0;
            candidate.exactBaseline.shadowChangedPixelCount = IntRange.Exact(0);
            AssertApplyFailsBeforeWrite(candidate);
        }

        /// <summary>Ensures zero HDR shadow color delta reaches apply while invalid HDR deltas fail before write.</summary>
        [Test]
        public void CandidateZeroHdrShadowDeltaAppliesAndInvalidDeltasFailBeforeWriteBoundary()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate zeroDeltaCandidate = CreateValidCandidate();
            zeroDeltaCandidate.observation.shadowMaxAbsoluteRgbDelta = 0.0f;
            var validWriter = new RecordingCandidateWriter();
            var validBoundary = new RecordingWriteBoundary();
            PureBaseRegressionBaselineGenerator.ApplyReviewedCandidate(CreateValidEnvironment(), zeroDeltaCandidate, validWriter, validBoundary);
            Assert.That(validWriter.WriteCallCount, Is.EqualTo(1));
            Assert.That(validWriter.Baseline, Is.SameAs(zeroDeltaCandidate.exactBaseline));
            Assert.That(validBoundary.BeginCallCount, Is.EqualTo(1));
            Assert.That(validBoundary.VerifyCallCount, Is.EqualTo(2));
            foreach (float invalidDelta in new[] { -0.01f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                PureBaseRegressionBaselineGenerator.ObservationCandidate invalidCandidate = CreateValidCandidate();
                invalidCandidate.observation.shadowMaxAbsoluteRgbDelta = invalidDelta;
                AssertApplyFailsBeforeWrite(invalidCandidate);
            }
        }

        /// <summary>Ensures every canonical static-scene count is required before the apply transaction can begin.</summary>
        /// <param name="staticLightmapCount">The static lightmap count to test.</param>
        /// <param name="staticRendererAssignmentCount">The static renderer assignment count to test.</param>
        /// <param name="warmedVariantCount">The warmed variant count to test.</param>
        [TestCase(0, 20, 56)]
        [TestCase(2, 0, 56)]
        [TestCase(2, 20, 1)]
        public void CandidateNoncanonicalStaticEvidenceFailsBeforeWriteBoundary(int staticLightmapCount, int staticRendererAssignmentCount, int warmedVariantCount)
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate candidate = CreateValidCandidate();
            candidate.observation.staticLightmapCount = staticLightmapCount;
            candidate.exactBaseline.staticLightmapCount = staticLightmapCount;
            candidate.observation.staticRendererAssignmentCount = staticRendererAssignmentCount;
            candidate.exactBaseline.staticRendererAssignmentCount = staticRendererAssignmentCount;
            candidate.observation.warmedVariantCount = warmedVariantCount;
            candidate.exactBaseline.warmedVariantCount = warmedVariantCount;
            AssertApplyFailsBeforeWrite(candidate);
        }

        /// <summary>Ensures every coverage and centroid component remains in the inclusive unit interval before apply can begin.</summary>
        /// <param name="componentIndex">The coverage or centroid component index to test.</param>
        /// <param name="invalidValue">The finite out-of-range component value to test.</param>
        [TestCase(0, -0.01f)]
        [TestCase(1, 2.0f)]
        [TestCase(2, -0.01f)]
        [TestCase(3, 2.0f)]
        [TestCase(4, -0.01f)]
        [TestCase(5, 2.0f)]
        public void CandidateOutOfRangeCoverageOrCentroidFailsBeforeWriteBoundary(int componentIndex, float invalidValue)
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate candidate = CreateValidCandidate();
            switch (componentIndex)
            {
                case 0: candidate.observation.sceneVisibleCoverage = invalidValue; break;
                case 1: candidate.observation.sceneVisibleCentroidX = invalidValue; break;
                case 2: candidate.observation.sceneVisibleCentroidY = invalidValue; break;
                case 3: candidate.observation.shadowCoverage = invalidValue; break;
                case 4: candidate.observation.shadowCentroidX = invalidValue; break;
                case 5: candidate.observation.shadowCentroidY = invalidValue; break;
                default: throw new ArgumentOutOfRangeException(nameof(componentIndex));
            }
            AssertApplyFailsBeforeWrite(candidate);
        }

        /// <summary>Ensures the read-only seam writes only an external candidate and preserves dynamic-lightmap limitation evidence.</summary>
        [Test]
        public void ReadOnlyCaptureWritesExternalCandidateWithDynamicLightmapLimitation()
        {
            string candidatePath = Path.Combine(Path.GetTempPath(), "PureBase-Observation-" + Guid.NewGuid().ToString("N") + ".json");
            var operations = new RecordingObservationCapture(CreateValidObservation());
            try
            {
                PureBaseRegressionBaselineGenerator.CaptureObservationCandidate(CreateValidEnvironment(), candidatePath, operations);
                PureBaseRegressionBaselineGenerator.ObservationCandidate candidate = PureBaseRegressionBaselineGenerator.ReadObservationCandidate(candidatePath);
                Assert.That(operations.CaptureCallCount, Is.EqualTo(1));
                Assert.That(candidate.observation.dynamicLightmapStatus, Is.EqualTo(PureBaseRegressionBaselineGenerator.DynamicLightmapLimitation));
                Assert.That(candidate.exactBaseline.dynamicLightmapStatus, Is.EqualTo(PureBaseRegressionBaselineGenerator.DynamicLightmapLimitation));
                Assert.That(candidate.exactBaseline.metaAlbedo, Has.Length.EqualTo(4));
                Assert.That(candidate.observation.shadowChangedPixelCount, Is.GreaterThan(32));
            }
            finally { if (File.Exists(candidatePath)) File.Delete(candidatePath); }
        }

        /// <summary>Ensures apply passes the candidate's exact baseline through the write boundary without recapturing or substituting values.</summary>
        [Test]
        public void ApplyUsesOnlyCandidateExactBaselineThroughWriteBoundary()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate candidate = CreateValidCandidate();
            var writer = new RecordingCandidateWriter();
            var boundary = new RecordingWriteBoundary();
            PureBaseRegressionBaselineGenerator.ApplyReviewedCandidate(CreateValidEnvironment(), candidate, writer, boundary);
            Assert.That(writer.WriteCallCount, Is.EqualTo(1));
            Assert.That(writer.Baseline, Is.SameAs(candidate.exactBaseline));
            Assert.That(boundary.BeginCallCount, Is.EqualTo(1));
            Assert.That(boundary.VerifyCallCount, Is.EqualTo(2));
        }

        /// <summary>Verifies that one invalid candidate cannot reach a write operation or begin a write transaction.</summary>
        /// <param name="candidate">The invalid candidate.</param>
        private static void AssertApplyFailsBeforeWrite(PureBaseRegressionBaselineGenerator.ObservationCandidate candidate)
        {
            var writer = new RecordingCandidateWriter();
            var boundary = new RecordingWriteBoundary();
            Assert.Throws<InvalidOperationException>(() => PureBaseRegressionBaselineGenerator.ApplyReviewedCandidate(CreateValidEnvironment(), candidate, writer, boundary));
            Assert.That(writer.WriteCallCount, Is.Zero);
            Assert.That(boundary.BeginCallCount, Is.Zero);
        }

        /// <summary>Creates the fixed compatible environment used by candidate seam tests.</summary>
        private static PureBaseRegressionBaselineGenerator.IEnvironment CreateValidEnvironment() => new CandidateEnvironment(PureBaseValidationSceneRegressionTests.ExpectedUnityVersion, true, GraphicsDeviceType.Direct3D11, ColorSpace.Linear);

        /// <summary>Creates a complete observable candidate through the production candidate serialization seam.</summary>
        private static PureBaseRegressionBaselineGenerator.ObservationCandidate CreateValidCandidate() => PureBaseRegressionBaselineGenerator.CreateObservationCandidate(CreateValidEnvironment(), CreateValidObservation());

        /// <summary>Creates one finite nonzero read-only observation with the complete candidate diagnostic surface.</summary>
        private static SceneRegressionObservation CreateValidObservation()
        {
            return new SceneRegressionObservation
            {
                staticLightmapCount = 2, staticRendererAssignmentCount = 20, sceneFinitePixelCount = 4096, sceneVisiblePixelCount = 1024, sceneVisibleCoverage = 0.25f, sceneVisibleCentroidX = 0.5f, sceneVisibleCentroidY = 0.5f, shadowChangedPixelCount = 33, shadowCoveragePixelCount = 64, shadowCoverage = 0.015625f, shadowCentroidX = 0.5f, shadowCentroidY = 0.5f, shadowMaxAbsoluteRgbDelta = 0.2f, warmedVariantCount = 56, dynamicLightmapStatus = PureBaseRegressionBaselineGenerator.DynamicLightmapLimitation,
                metaAlbedo = new[] { new MetaAlbedoObservation { materialName = "PureBaseValidationUnlit", shaderName = "PureBase/Unlit", meanLuminance = 0.01f }, new MetaAlbedoObservation { materialName = "PureBaseValidationToon", shaderName = "PureBase/Toon", meanLuminance = 0.02f }, new MetaAlbedoObservation { materialName = "PureBaseValidationPbr", shaderName = "PureBase/PBR", meanLuminance = 0.03f }, new MetaAlbedoObservation { materialName = "PureBaseValidationHybrid", shaderName = "PureBase/Hybrid", meanLuminance = 0.04f } },
            };
        }

        /// <summary>Supplies fixed environment values without reading or changing the Unity editor.</summary>
        private sealed class CandidateEnvironment : PureBaseRegressionBaselineGenerator.IEnvironment
        {
            /// <summary>Initializes fixed environment values.</summary>
            public CandidateEnvironment(string unityVersion, bool isBuiltInRenderPipeline, GraphicsDeviceType graphicsDeviceType, ColorSpace colorSpace) { UnityVersion = unityVersion; IsBuiltInRenderPipeline = isBuiltInRenderPipeline; GraphicsDeviceType = graphicsDeviceType; ColorSpace = colorSpace; }
            /// <inheritdoc />
            public string UnityVersion { get; }
            /// <inheritdoc />
            public bool IsBuiltInRenderPipeline { get; }
            /// <inheritdoc />
            public GraphicsDeviceType GraphicsDeviceType { get; }
            /// <inheritdoc />
            public ColorSpace ColorSpace { get; }
        }

        /// <summary>Records read-only observation capture without creating Unity assets.</summary>
        private sealed class RecordingObservationCapture : PureBaseRegressionBaselineGenerator.IObservationCaptureOperations
        {
            /// <summary>Stores the fixed observation returned by the capture seam.</summary>
            private readonly SceneRegressionObservation observation;
            /// <summary>Initializes a fixed observation result.</summary>
            /// <param name="observation">The observation returned by the read-only seam.</param>
            public RecordingObservationCapture(SceneRegressionObservation observation) { this.observation = observation; }
            /// <summary>Gets the number of read-only capture calls.</summary>
            public int CaptureCallCount { get; private set; }
            /// <inheritdoc />
            public SceneRegressionObservation CaptureObservation() { CaptureCallCount++; return observation; }
        }

        /// <summary>Records candidate baseline writes without touching canonical files.</summary>
        private sealed class RecordingCandidateWriter : PureBaseRegressionBaselineGenerator.IReviewedCandidateWriter
        {
            /// <summary>Gets the number of candidate baseline writes.</summary>
            public int WriteCallCount { get; private set; }
            /// <summary>Gets the exact candidate baseline supplied to the writer.</summary>
            public SceneRegressionBaseline Baseline { get; private set; }
            /// <inheritdoc />
            public void WriteExactBaseline(SceneRegressionBaseline baseline, PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary) { WriteCallCount++; Baseline = baseline; }
        }

        /// <summary>Records transaction calls without capturing or mutating durable workspace state.</summary>
        private sealed class RecordingWriteBoundary : PureBaseRegressionBaselineGenerator.IWriteBoundary
        {
            /// <summary>Gets the number of transaction starts.</summary>
            public int BeginCallCount { get; private set; }
            /// <summary>Gets the number of transaction checks.</summary>
            public int VerifyCallCount { get; private set; }
            /// <inheritdoc />
            public void BeginTransaction() => BeginCallCount++;
            /// <inheritdoc />
            public void VerifyNoUnrelatedChanges() => VerifyCallCount++;
        }
    }
}