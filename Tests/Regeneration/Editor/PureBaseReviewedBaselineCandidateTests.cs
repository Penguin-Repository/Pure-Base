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

// Defines reviewed-baseline merge, validation, and pre-write rejection contracts.

using System;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using PureBase.Tests.Daily;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Defines reviewed-baseline artifact merge and validation contracts.</summary>
    public sealed class PureBaseReviewedBaselineCandidateTests
    {
        /// <summary>Requires the merge to preserve every canonical field except exact PBR and Hybrid observations.</summary>
        [Test]
        public void CreatePreservesCanonicalFieldsAndOnlyReplacesApprovedMetaRanges()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate observation =
                CreateObservation();
            SceneRegressionBaseline canonical = CreateCanonicalBaseline();
            PureBaseReviewedBaselineCandidate candidate = CreateUnderTest(
                observation,
                canonical,
                CreateObservationBytes()
            );

            Assert.That(
                candidate.schemaVersion,
                Is.EqualTo(PureBaseReviewedBaselineCandidate.SchemaVersion)
            );
            Assert.That(candidate.approvedBaseline, Is.Not.SameAs(canonical));
            AssertRange(candidate.canonicalPbrRange, canonical.metaAlbedo[2].meanLuminance);
            AssertRange(candidate.canonicalHybridRange, canonical.metaAlbedo[3].meanLuminance);
            AssertCanonicalPreservation(canonical, candidate.approvedBaseline, observation);
        }

        /// <summary>Requires the reviewed baseline to retain independent inner range objects after canonical mutation.</summary>
        [Test]
        public void CreateDeepCopiesCanonicalInnerRangeObjects()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate observation =
                CreateObservation();
            SceneRegressionBaseline canonical = CreateCanonicalBaseline();
            PureBaseReviewedBaselineCandidate candidate = CreateUnderTest(
                observation,
                canonical,
                CreateObservationBytes()
            );
            FloatRange originalRange = canonical.metaAlbedo[0].meanLuminance;

            originalRange.minimum = 0.9f;
            originalRange.maximum = 1.0f;

            Assert.That(
                candidate.approvedBaseline.metaAlbedo[0].meanLuminance,
                Is.Not.SameAs(originalRange)
            );
            Assert.That(
                candidate.approvedBaseline.metaAlbedo[0].meanLuminance.minimum,
                Is.EqualTo(0.01f)
            );
            Assert.That(
                candidate.approvedBaseline.metaAlbedo[0].meanLuminance.maximum,
                Is.EqualTo(0.010001f)
            );
        }

        /// <summary>Requires reviewed candidates to bind the exact observation bytes with SHA-256.</summary>
        [Test]
        public void CreateBindsTheExactRawObservationSha256()
        {
            byte[] bytes = CreateObservationBytes();
            PureBaseReviewedBaselineCandidate candidate = CreateUnderTest(
                CreateObservation(),
                CreateCanonicalBaseline(),
                bytes
            );

            Assert.That(candidate.sourceObservationSha256, Is.EqualTo(ComputeFixtureSha256(bytes)));
        }

        /// <summary>Requires the builder to reject bytes that deserialize to different source observation evidence.</summary>
        [Test]
        public void CreateRejectsRawObservationBytesThatDoNotMatchItsSourceObservation()
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate differentObservation =
                CreateObservation();
            differentObservation.observation.metaAlbedo[2].meanLuminance = 0.9f;
            differentObservation.exactBaseline.metaAlbedo[2].meanLuminance = FloatRange.Exact(0.9f);

            Assert.Throws<InvalidOperationException>(() =>
                PureBaseReviewedBaselineCandidate.Create(
                    CreateObservation(),
                    CreateCanonicalBaseline(),
                    SerializeObservation(differentObservation)
                )
            );
        }

        /// <summary>Requires the public SHA-256 API to use a known lowercase hexadecimal vector.</summary>
        [Test]
        public void ComputeSha256UsesKnownLowercaseHexadecimalVector()
        {
            Assert.That(
                PureBaseReviewedBaselineCandidate.ComputeSha256(KnownVectorBytes),
                Is.EqualTo("67d6291b301412df4a6ca7808aeafac9d0587b06ece3c68e72bab0157cd9d52c")
            );
        }

        /// <summary>Requires each invalid observation-path argument form to reject before any write.</summary>
        /// <param name="condition">The single observation path condition to invalidate.</param>
        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("empty")]
        [TestCase("trailing")]
        public void ApplyFromCommandLineRejectsObservationPathFailuresBeforeWrites(string condition)
        {
            AssertCommandLineRejection(CreateInvalidArguments(condition, true));
        }

        /// <summary>Requires each invalid reviewed-path argument form to reject before any write.</summary>
        /// <param name="condition">The single reviewed path condition to invalidate.</param>
        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("empty")]
        [TestCase("trailing")]
        public void ApplyFromCommandLineRejectsReviewedPathFailuresBeforeWrites(string condition)
        {
            AssertCommandLineRejection(CreateInvalidArguments(condition, false));
        }

        /// <summary>Requires missing, unsupported, and malformed observation schemas to reject before any write.</summary>
        /// <param name="condition">The single observation schema condition to invalidate.</param>
        [TestCase("missing")]
        [TestCase("legacy")]
        [TestCase("unsupported")]
        [TestCase("malformed")]
        public void ApplyFromArtifactsRejectsObservationSchemaFailuresBeforeWrites(string condition)
        {
            byte[] observationBytes;
            if (condition == "malformed")
            {
                observationBytes = Encoding.UTF8.GetBytes("{");
            }
            else
            {
                PureBaseRegressionBaselineGenerator.ObservationCandidate observation =
                    CreateObservation();
                observation.schemaVersion =
                    condition == "missing"
                        ? 0
                        : PureBaseRegressionBaselineGenerator.ObservationCandidateSchemaVersion + 1;
                observationBytes = SerializeObservation(observation);
            }

            AssertArtifactRejection(
                observationBytes,
                CreateReviewedBytes(),
                CreateCanonicalBaseline()
            );
        }

        /// <summary>Requires missing, unsupported, and malformed reviewed schemas to reject before any write.</summary>
        /// <param name="condition">The single reviewed schema condition to invalidate.</param>
        [TestCase("missing")]
        [TestCase("legacy")]
        [TestCase("unsupported")]
        [TestCase("malformed")]
        public void ApplyFromArtifactsRejectsReviewedSchemaFailuresBeforeWrites(string condition)
        {
            byte[] reviewedBytes;
            if (condition == "malformed")
            {
                reviewedBytes = Encoding.UTF8.GetBytes("{");
            }
            else
            {
                PureBaseReviewedBaselineCandidate candidate = CreateReviewedCandidate();
                candidate.schemaVersion =
                    condition == "missing" ? 0
                    : condition == "legacy" ? PureBaseReviewedBaselineCandidate.SchemaVersion - 1
                    : PureBaseReviewedBaselineCandidate.SchemaVersion + 1;
                reviewedBytes = SerializeReviewed(candidate);
            }

            AssertArtifactRejection(
                CreateObservationBytes(),
                reviewedBytes,
                CreateCanonicalBaseline()
            );
        }

        /// <summary>Requires reviewed artifacts supplied at the observation path to reject before any write.</summary>
        [Test]
        public void ApplyFromArtifactsRejectsReviewedArtifactAtObservationPathBeforeWrites()
        {
            AssertArtifactRejection(
                CreateReviewedBytes(),
                CreateReviewedBytes(),
                CreateCanonicalBaseline()
            );
        }

        /// <summary>Requires observation artifacts supplied at the reviewed path to reject before any write.</summary>
        [Test]
        public void ApplyFromArtifactsRejectsObservationArtifactAtReviewedPathBeforeWrites()
        {
            AssertArtifactRejection(
                CreateObservationBytes(),
                CreateObservationBytes(),
                CreateCanonicalBaseline()
            );
        }

        /// <summary>Requires the legacy observation artifact boundary to reject a reviewed artifact by schema alone.</summary>
        [Test]
        public void LegacyApplyArtifactDeserializerRejectsReviewedArtifactSchemaMismatch()
        {
            string reviewedArtifactJson = Encoding.UTF8.GetString(CreateReviewedBytes());

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PureBaseRegressionBaselineGenerator.DeserializeObservationCandidate(
                    reviewedArtifactJson
                )
            );

            Assert.That(
                exception.Message,
                Is.EqualTo(
                    $"The reviewed observation candidate must use schema version {PureBaseRegressionBaselineGenerator.ObservationCandidateSchemaVersion}."
                )
            );
        }

        /// <summary>Requires non-lowercase source hashes to reject before any write.</summary>
        [Test]
        public void ApplyRejectsInvalidSourceHashFormatBeforeWrites()
        {
            PureBaseReviewedBaselineCandidate candidate = CreateReviewedCandidate();
            candidate.sourceObservationSha256 =
                candidate.sourceObservationSha256.ToUpperInvariant();

            AssertApplyRejection(candidate, CreateObservation(), CreateCanonicalBaseline());
        }

        /// <summary>Requires source hashes shorter than a SHA-256 digest to reject before any write.</summary>
        [Test]
        public void ApplyRejectsSourceHashWithWrongLengthBeforeWrites()
        {
            PureBaseReviewedBaselineCandidate candidate = CreateReviewedCandidate();
            candidate.sourceObservationSha256 = candidate.sourceObservationSha256.Substring(1);

            AssertApplyRejection(candidate, CreateObservation(), CreateCanonicalBaseline());
        }

        /// <summary>Requires source hashes containing non-hexadecimal characters to reject before any write.</summary>
        [Test]
        public void ApplyRejectsSourceHashWithNonHexCharacterBeforeWrites()
        {
            PureBaseReviewedBaselineCandidate candidate = CreateReviewedCandidate();
            candidate.sourceObservationSha256 =
                "g" + candidate.sourceObservationSha256.Substring(1);

            AssertApplyRejection(candidate, CreateObservation(), CreateCanonicalBaseline());
        }

        /// <summary>Requires an otherwise valid source hash that binds different bytes to reject before any write.</summary>
        [Test]
        public void ApplyRejectsSourceHashIdentityMismatchBeforeWrites()
        {
            byte[] changedBytes = Encoding.UTF8.GetBytes(
                "\n" + Encoding.UTF8.GetString(CreateObservationBytes())
            );

            AssertApplyRejection(
                CreateReviewedCandidate(),
                CreateObservation(),
                CreateCanonicalBaseline(),
                changedBytes
            );
        }

        /// <summary>Requires every missing, duplicate, reordered, unexpected, and mismatched Meta identity to reject.</summary>
        /// <param name="condition">The single Meta identity condition to invalidate.</param>
        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("reordered")]
        [TestCase("unexpected")]
        [TestCase("mismatched")]
        public void ApplyRejectsMetaIdentityFailuresBeforeWrites(string condition)
        {
            PureBaseReviewedBaselineCandidate candidate = CreateReviewedCandidate();
            MetaAlbedoBaseline[] meta = candidate.approvedBaseline.metaAlbedo;
            if (condition == "missing")
                candidate.approvedBaseline.metaAlbedo = new[] { meta[0], meta[1], meta[2] };
            else if (condition == "duplicate")
                meta[3] = CreateMeta(meta[2].materialName, meta[2].shaderName, 0.071f, 0.071f);
            else if (condition == "reordered")
                candidate.approvedBaseline.metaAlbedo = new[]
                {
                    meta[0],
                    meta[1],
                    meta[3],
                    meta[2],
                };
            else if (condition == "unexpected")
                meta[2].shaderName = "PureBase/Unexpected";
            else
                meta[2].materialName = "PureBaseValidationHybrid";

            AssertApplyRejection(candidate, CreateObservation(), CreateCanonicalBaseline());
        }

        /// <summary>Requires each approved PBR or Hybrid range to equal its observed target exactly.</summary>
        /// <param name="index">The sole approved target index to invalidate.</param>
        [TestCase(2)]
        [TestCase(3)]
        public void ApplyRejectsApprovedTargetValueMismatchBeforeWrites(int index)
        {
            PureBaseReviewedBaselineCandidate candidate = CreateReviewedCandidate();
            candidate.approvedBaseline.metaAlbedo[index].meanLuminance = FloatRange.Exact(0.9f);

            AssertApplyRejection(candidate, CreateObservation(), CreateCanonicalBaseline());
        }

        /// <summary>Requires every unrelated scalar, range, Unlit range, and Toon range to remain canonical.</summary>
        /// <param name="condition">The sole canonical condition to invalidate.</param>
        [TestCase("schema")]
        [TestCase("unity")]
        [TestCase("graphics")]
        [TestCase("color")]
        [TestCase("pipeline")]
        [TestCase("render-size")]
        [TestCase("lightmaps")]
        [TestCase("assignments")]
        [TestCase("visible-minimum")]
        [TestCase("visible-maximum")]
        [TestCase("shadow-minimum")]
        [TestCase("shadow-maximum")]
        [TestCase("variants")]
        [TestCase("dynamic-lightmap")]
        [TestCase("unlit")]
        [TestCase("toon")]
        public void ApplyRejectsCanonicalPreservationFailuresBeforeWrites(string condition)
        {
            PureBaseReviewedBaselineCandidate candidate = CreateReviewedCandidate();
            SceneRegressionBaseline approved = candidate.approvedBaseline;
            if (condition == "schema")
                approved.schemaVersion++;
            else if (condition == "unity")
                approved.unityVersion = "different";
            else if (condition == "graphics")
                approved.graphicsDevice = "different";
            else if (condition == "color")
                approved.colorSpace = "different";
            else if (condition == "pipeline")
                approved.renderPipeline = "different";
            else if (condition == "render-size")
                approved.renderSize++;
            else if (condition == "lightmaps")
                approved.staticLightmapCount++;
            else if (condition == "assignments")
                approved.staticRendererAssignmentCount++;
            else if (condition == "visible-minimum")
                approved.sceneVisiblePixelCount.minimum--;
            else if (condition == "visible-maximum")
                approved.sceneVisiblePixelCount.maximum++;
            else if (condition == "shadow-minimum")
                approved.shadowChangedPixelCount.minimum--;
            else if (condition == "shadow-maximum")
                approved.shadowChangedPixelCount.maximum++;
            else if (condition == "variants")
                approved.warmedVariantCount++;
            else if (condition == "dynamic-lightmap")
                approved.dynamicLightmapStatus = "different";
            else if (condition == "unlit")
                approved.metaAlbedo[0].meanLuminance.minimum += 0.1f;
            else
                approved.metaAlbedo[1].meanLuminance.maximum += 0.1f;

            AssertApplyRejection(candidate, CreateObservation(), CreateCanonicalBaseline());
        }

        /// <summary>Requires a reviewed candidate to reject after the canonical baseline has changed.</summary>
        [Test]
        public void ApplyRejectsStaleCanonicalBaselineBeforeWrites()
        {
            SceneRegressionBaseline staleCanonical = CreateCanonicalBaseline();
            staleCanonical.shadowChangedPixelCount.maximum++;

            AssertApplyRejection(CreateReviewedCandidate(), CreateObservation(), staleCanonical);
        }

        /// <summary>Requires either reviewed writable target range to remain unchanged from artifact creation.</summary>
        /// <param name="index">The sole canonical PBR or Hybrid target index to change.</param>
        [TestCase(2)]
        [TestCase(3)]
        public void ApplyRejectsChangedCanonicalTargetRangeBeforeWrites(int index)
        {
            SceneRegressionBaseline changedCanonical = CreateCanonicalBaseline();
            changedCanonical.metaAlbedo[index].meanLuminance.maximum += 0.01f;

            AssertApplyRejection(CreateReviewedCandidate(), CreateObservation(), changedCanonical);
        }

        /// <summary>Requires lossless reviewed apply preparation to preserve every non-target literal and terminal newline.</summary>
        [Test]
        public void CreateLosslessReviewedBaselineBytesPreservesNonTargetTextAndOnlyChangesApprovedRanges()
        {
            SceneRegressionBaseline canonical = CreateCanonicalBaseline();
            SceneRegressionBaseline approved = CreateReviewedCandidate().approvedBaseline;
            string rawText = CreateRawCanonicalBaselineText();

            string rewrittenText = Encoding.UTF8.GetString(
                PureBaseRegressionBaselineStorage.CreateLosslessReviewedBaselineBytes(
                    canonical,
                    Encoding.UTF8.GetBytes(rawText),
                    approved
                )
            );

            string pbrMinimum = ExtractMetaRangeLiteral(
                rewrittenText,
                "PureBaseValidationPbr",
                "minimum"
            );
            string pbrMaximum = ExtractMetaRangeLiteral(
                rewrittenText,
                "PureBaseValidationPbr",
                "maximum"
            );
            string hybridMinimum = ExtractMetaRangeLiteral(
                rewrittenText,
                "PureBaseValidationHybrid",
                "minimum"
            );
            string hybridMaximum = ExtractMetaRangeLiteral(
                rewrittenText,
                "PureBaseValidationHybrid",
                "maximum"
            );
            string expectedText = rawText
                .Replace("5.0e-2", pbrMinimum)
                .Replace("6.0e-2", pbrMaximum)
                .Replace("7.0e-2", hybridMinimum)
                .Replace("8.0e-2", hybridMaximum);

            Assert.That(rewrittenText, Is.EqualTo(expectedText));
            Assert.That(rewrittenText, Does.Contain("\"minimum\": 3.0e-2"));
            Assert.That(rewrittenText, Does.Contain("\"maximum\": 3.00005e-2"));
            Assert.That(rewrittenText.EndsWith("\n", StringComparison.Ordinal), Is.True);
            Assert.That(
                ParseInvariantSingle(pbrMinimum),
                Is.EqualTo(approved.metaAlbedo[2].meanLuminance.minimum)
            );
            Assert.That(
                ParseInvariantSingle(pbrMaximum),
                Is.EqualTo(approved.metaAlbedo[2].meanLuminance.maximum)
            );
            Assert.That(
                ParseInvariantSingle(hybridMinimum),
                Is.EqualTo(approved.metaAlbedo[3].meanLuminance.minimum)
            );
            Assert.That(
                ParseInvariantSingle(hybridMaximum),
                Is.EqualTo(approved.metaAlbedo[3].meanLuminance.maximum)
            );
        }

        /// <summary>Requires the approved Unlit and Toon migration to replace only its four validated Meta token spans.</summary>
        [Test]
        public void CreateApprovedUnlitToonRangeMigrationBytesPreservesAllOtherCanonicalBytes()
        {
            string rawText = CreateApprovedUnlitToonMigrationSourceText();
            string rewrittenText = Encoding.UTF8.GetString(
                PureBaseRegressionBaselineStorage.CreateApprovedUnlitToonRangeMigrationBytes(
                    Encoding.UTF8.GetBytes(rawText)
                )
            );
            string expectedText = rawText
                .Replace("\"minimum\": 0.04757445678114891", "\"minimum\": 0.04757252708077431")
                .Replace("\"minimum\": 0.08925552666187286", "\"minimum\": 0.08925478160381317");

            Assert.That(rewrittenText, Is.EqualTo(expectedText));
            Assert.That(
                ExtractMetaRangeLiteral(rewrittenText, "PureBaseValidationUnlit", "minimum"),
                Is.EqualTo("0.04757252708077431")
            );
            Assert.That(
                ExtractMetaRangeLiteral(rewrittenText, "PureBaseValidationUnlit", "maximum"),
                Is.EqualTo("0.04757445678114891")
            );
            Assert.That(
                ExtractMetaRangeLiteral(rewrittenText, "PureBaseValidationToon", "minimum"),
                Is.EqualTo("0.08925478160381317")
            );
            Assert.That(
                ExtractMetaRangeLiteral(rewrittenText, "PureBaseValidationToon", "maximum"),
                Is.EqualTo("0.08925552666187286")
            );
            Assert.That(
                ExtractMetaRangeLiteral(rewrittenText, "PureBaseValidationPbr", "minimum"),
                Is.EqualTo("0.0477057620882988")
            );
            Assert.That(
                ExtractMetaRangeLiteral(rewrittenText, "PureBaseValidationHybrid", "maximum"),
                Is.EqualTo("0.12078462541103363")
            );
            Assert.That(rewrittenText, Does.Contain("\"minimum\": 341, \"maximum\": 352"));
            Assert.That(rewrittenText.EndsWith("\n", StringComparison.Ordinal), Is.True);
        }

        /// <summary>Requires stale migration source literals and every Meta identity failure to reject before producing bytes.</summary>
        /// <param name="condition">The one source or identity invariant to invalidate.</param>
        [TestCase("stale")]
        [TestCase("missing")]
        [TestCase("duplicate")]
        [TestCase("reordered")]
        [TestCase("unexpected")]
        public void CreateApprovedUnlitToonRangeMigrationBytesRejectsInvalidSourceOrIdentity(
            string condition
        )
        {
            Assert.Throws<InvalidOperationException>(() =>
                PureBaseRegressionBaselineStorage.CreateApprovedUnlitToonRangeMigrationBytes(
                    Encoding.UTF8.GetBytes(
                        CreateInvalidApprovedUnlitToonMigrationSourceText(condition)
                    )
                )
            );
        }

        /// <summary>Requires malformed canonical target ranges to reject before the lossless apply transaction begins.</summary>
        [Test]
        public void ApplyLosslesslyRejectsAmbiguousCanonicalTargetRangeBeforeTransaction()
        {
            string ambiguousRawText = CreateRawCanonicalBaselineText()
                .Replace("\"minimum\": 5.0e-2,", "\"minimum\": 5.0e-2, \"minimum\": 5.0e-2,");
            var writer = new RecordingLosslessWriter();
            var boundary = new RecordingWriteBoundary();

            Assert.Throws<InvalidOperationException>(() =>
                PureBaseReviewedBaselineCandidate.ApplyLosslessly(
                    CreateReviewedCandidate(),
                    CreateObservation(),
                    CreateCanonicalBaseline(),
                    Encoding.UTF8.GetBytes(ambiguousRawText),
                    CreateObservationBytes(),
                    writer,
                    boundary
                )
            );

            Assert.That(writer.WriteCalls, Is.Zero);
            Assert.That(boundary.BeginCalls, Is.Zero);
            Assert.That(boundary.VerifyCalls, Is.Zero);
        }

        /// <summary>Requires lossless reviewed apply to write prepared bytes only after existing source and canonical guards validate.</summary>
        [Test]
        public void ApplyLosslesslyWritesPreparedBytePreservingCanonicalBaseline()
        {
            SceneRegressionBaseline canonical = CreateCanonicalBaseline();
            PureBaseReviewedBaselineCandidate candidate = CreateReviewedCandidate();
            byte[] rawCanonicalBytes = Encoding.UTF8.GetBytes(CreateRawCanonicalBaselineText());
            var writer = new RecordingLosslessWriter();
            var boundary = new RecordingWriteBoundary();

            PureBaseReviewedBaselineCandidate.ApplyLosslessly(
                candidate,
                CreateObservation(),
                canonical,
                rawCanonicalBytes,
                CreateObservationBytes(),
                writer,
                boundary
            );

            Assert.That(writer.WriteCalls, Is.EqualTo(1));
            Assert.That(boundary.BeginCalls, Is.EqualTo(1));
            Assert.That(boundary.VerifyCalls, Is.EqualTo(2));
            Assert.That(
                writer.WrittenBytes,
                Is.EqualTo(
                    PureBaseRegressionBaselineStorage.CreateLosslessReviewedBaselineBytes(
                        canonical,
                        rawCanonicalBytes,
                        candidate.approvedBaseline
                    )
                )
            );
            Assert.That(writer.ExpectedBytes, Is.EqualTo(rawCanonicalBytes));
        }

        /// <summary>Requires a canonical mutation after snapshot capture to reject at the final source comparison without a write or import.</summary>
        [Test]
        public void ApplyLosslesslyRejectsCanonicalMutationAtFinalSourceComparisonWithoutStorageEffects()
        {
            byte[] snapshotBytes = Encoding.UTF8.GetBytes(CreateRawCanonicalBaselineText());
            byte[] changedCanonicalBytes = Encoding.UTF8.GetBytes(
                CreateRawCanonicalBaselineText().Replace("1.0e-2", "1.1e-2")
            );
            var writer = new FinalSourceComparisonWriter(changedCanonicalBytes);
            var boundary = new RecordingWriteBoundary();

            Assert.Throws<InvalidOperationException>(() =>
                PureBaseReviewedBaselineCandidate.ApplyLosslessly(
                    CreateReviewedCandidate(),
                    CreateObservation(),
                    CreateCanonicalBaseline(),
                    snapshotBytes,
                    CreateObservationBytes(),
                    writer,
                    boundary
                )
            );

            Assert.That(writer.FinalSourceComparisons, Is.EqualTo(1));
            Assert.That(writer.BaselineWriteCalls, Is.Zero);
            Assert.That(writer.ImportCalls, Is.Zero);
            Assert.That(boundary.BeginCalls, Is.EqualTo(1));
            Assert.That(boundary.VerifyCalls, Is.EqualTo(1));
        }

        /// <summary>Requires each artifact path to be read exactly once even if a second read would change content.</summary>
        [Test]
        public void ApplyFromArtifactsReadsEachArtifactPathExactlyOnce()
        {
            var reader = CreateReader(CreateObservationBytes(), CreateReviewedBytes());
            var writer = new RecordingWriter();
            var boundary = new RecordingWriteBoundary();
            PureBaseReviewedBaselineCandidate.ApplyFromArtifacts(
                reader,
                ObservationPath,
                ReviewedPath,
                CreateCanonicalBaseline(),
                writer,
                boundary
            );

            Assert.That(reader.ObservationReads, Is.EqualTo(1));
            Assert.That(reader.ReviewedReads, Is.EqualTo(1));
        }

        /// <summary>Requires reviewed-artifact creation to read a valid raw observation exactly once before one output.</summary>
        [Test]
        public void CreateFromArtifactsReadsObservationExactlyOnceBeforeWritingReviewedArtifact()
        {
            var reader = CreateReader(CreateObservationBytes(), CreateReviewedBytes());
            var writer = new RecordingArtifactWriter();

            PureBaseReviewedBaselineCandidate.CreateFromArtifacts(
                reader,
                ObservationPath,
                ReviewedPath,
                CreateValidEnvironment(),
                CreateCanonicalBaseline(),
                writer
            );

            Assert.That(reader.ObservationReads, Is.EqualTo(1));
            Assert.That(reader.ReviewedReads, Is.Zero);
            Assert.That(writer.WriteCalls, Is.EqualTo(1));
            Assert.That(writer.LastPath, Is.EqualTo(ReviewedPath));
        }

        /// <summary>Requires each invalid create command line to reject before artifact reads or output.</summary>
        /// <param name="condition">The single command-line condition to invalidate.</param>
        /// <param name="observationPath">Whether to invalidate the observation path.</param>
        [TestCase("missing", true)]
        [TestCase("duplicate", true)]
        [TestCase("empty", true)]
        [TestCase("trailing", true)]
        [TestCase("missing", false)]
        [TestCase("duplicate", false)]
        [TestCase("empty", false)]
        [TestCase("trailing", false)]
        public void CreateFromCommandLineRejectsArgumentFailuresBeforeReadsOrOutput(
            string condition,
            bool observationPath
        )
        {
            var reader = CreateReader(CreateObservationBytes(), CreateReviewedBytes());
            var writer = new RecordingArtifactWriter();

            AssertCreateRejectedBeforeOutput(
                () =>
                    PureBaseReviewedBaselineCandidate.CreateFromCommandLine(
                        reader,
                        CreateInvalidArguments(condition, observationPath),
                        CreateValidEnvironment(),
                        CreateCanonicalBaseline(),
                        writer
                    ),
                reader,
                writer,
                0
            );
        }

        /// <summary>Requires raw schema and cross-use failures to reject after one reached read and before output.</summary>
        /// <param name="condition">The single raw artifact condition to invalidate.</param>
        [TestCase("schema")]
        [TestCase("cross-use")]
        public void CreateFromArtifactsRejectsSchemaAndCrossUseBeforeOutput(string condition)
        {
            byte[] observationBytes =
                condition == "schema"
                    ? Encoding.UTF8.GetBytes("{\"schemaVersion\":0}")
                    : CreateReviewedBytes();
            var reader = CreateReader(observationBytes, CreateReviewedBytes());
            var writer = new RecordingArtifactWriter();

            AssertCreateRejectedBeforeOutput(
                () =>
                    PureBaseReviewedBaselineCandidate.CreateFromArtifacts(
                        reader,
                        ObservationPath,
                        ReviewedPath,
                        CreateValidEnvironment(),
                        CreateCanonicalBaseline(),
                        writer
                    ),
                reader,
                writer,
                1
            );
        }

        /// <summary>Requires an incompatible create environment to reject before input reads or output.</summary>
        [Test]
        public void CreateFromArtifactsRejectsEnvironmentFailureBeforeReadsOrOutput()
        {
            var reader = CreateReader(CreateObservationBytes(), CreateReviewedBytes());
            var writer = new RecordingArtifactWriter();

            AssertCreateRejectedBeforeOutput(
                () =>
                    PureBaseReviewedBaselineCandidate.CreateFromArtifacts(
                        reader,
                        ObservationPath,
                        ReviewedPath,
                        new CandidateEnvironment(
                            "different",
                            true,
                            GraphicsDeviceType.Direct3D11,
                            ColorSpace.Linear
                        ),
                        CreateCanonicalBaseline(),
                        writer
                    ),
                reader,
                writer,
                0
            );
        }

        /// <summary>Requires invalid observation diagnostics and identities to reject after one reached read and before output.</summary>
        /// <param name="condition">The single raw observation condition to invalidate.</param>
        [TestCase("observation")]
        [TestCase("identity")]
        public void CreateFromArtifactsRejectsObservationAndIdentityFailuresBeforeOutput(
            string condition
        )
        {
            PureBaseRegressionBaselineGenerator.ObservationCandidate observation =
                CreateObservation();
            if (condition == "observation")
                observation.observation.dynamicLightmapStatus = "different";
            else
                observation.observation.metaAlbedo[2].shaderName = "PureBase/Unexpected";
            var reader = CreateReader(SerializeObservation(observation), CreateReviewedBytes());
            var writer = new RecordingArtifactWriter();

            AssertCreateRejectedBeforeOutput(
                () =>
                    PureBaseReviewedBaselineCandidate.CreateFromArtifacts(
                        reader,
                        ObservationPath,
                        ReviewedPath,
                        CreateValidEnvironment(),
                        CreateCanonicalBaseline(),
                        writer
                    ),
                reader,
                writer,
                1
            );
        }

        /// <summary>Creates a reviewed candidate through the merge boundary.</summary>
        /// <param name="observation">The complete valid observation fixture.</param>
        /// <param name="canonical">The complete valid canonical baseline fixture.</param>
        /// <param name="bytes">The exact serialized observation bytes.</param>
        /// <returns>The created reviewed candidate.</returns>
        private static PureBaseReviewedBaselineCandidate CreateUnderTest(
            PureBaseRegressionBaselineGenerator.ObservationCandidate observation,
            SceneRegressionBaseline canonical,
            byte[] bytes
        )
        {
            return PureBaseReviewedBaselineCandidate.Create(observation, canonical, bytes);
        }

        /// <summary>Asserts a command-line input rejects before transaction, writer, and storage access.</summary>
        /// <param name="arguments">The invalid command-line argument sequence.</param>
        private static void AssertCommandLineRejection(string[] arguments)
        {
            var writer = new RecordingWriter();
            var boundary = new RecordingWriteBoundary();
            RecordingArtifactReader reader = CreateReader(
                CreateObservationBytes(),
                CreateReviewedBytes()
            );
            AssertRejectedBeforeWrite(
                () =>
                    PureBaseReviewedBaselineCandidate.ApplyFromCommandLine(
                        reader,
                        arguments,
                        CreateCanonicalBaseline(),
                        writer,
                        boundary
                    ),
                writer,
                boundary,
                reader,
                0,
                0
            );
        }

        /// <summary>Asserts artifact bytes reject before transaction, writer, and storage access.</summary>
        /// <param name="observationBytes">The observation artifact bytes.</param>
        /// <param name="reviewedBytes">The reviewed artifact bytes.</param>
        /// <param name="canonical">The current canonical baseline.</param>
        private static void AssertArtifactRejection(
            byte[] observationBytes,
            byte[] reviewedBytes,
            SceneRegressionBaseline canonical
        )
        {
            var writer = new RecordingWriter();
            var boundary = new RecordingWriteBoundary();
            RecordingArtifactReader reader = CreateReader(observationBytes, reviewedBytes);
            AssertRejectedBeforeWrite(
                () =>
                    PureBaseReviewedBaselineCandidate.ApplyFromArtifacts(
                        reader,
                        ObservationPath,
                        ReviewedPath,
                        canonical,
                        writer,
                        boundary
                    ),
                writer,
                boundary,
                reader,
                1,
                1
            );
        }

        /// <summary>Asserts a DTO input rejects before transaction, writer, and storage access.</summary>
        /// <param name="candidate">The reviewed candidate to reject.</param>
        /// <param name="observation">The raw observation to validate.</param>
        /// <param name="canonical">The current canonical baseline.</param>
        /// <param name="bytes">The exact observation bytes, or <see langword="null"/> for valid fixture bytes.</param>
        private static void AssertApplyRejection(
            PureBaseReviewedBaselineCandidate candidate,
            PureBaseRegressionBaselineGenerator.ObservationCandidate observation,
            SceneRegressionBaseline canonical,
            byte[] bytes = null
        )
        {
            var writer = new RecordingWriter();
            var boundary = new RecordingWriteBoundary();
            AssertRejectedBeforeWrite(
                () =>
                    PureBaseReviewedBaselineCandidate.Apply(
                        candidate,
                        observation,
                        canonical,
                        bytes ?? CreateObservationBytes(),
                        writer,
                        boundary
                    ),
                writer,
                boundary
            );
        }

        /// <summary>Asserts create input rejection reaches no output operation after the expected raw read count.</summary>
        /// <param name="operation">The create operation expected to reject.</param>
        /// <param name="reader">The artifact reader whose observation reads are counted.</param>
        /// <param name="writer">The reviewed artifact writer that must remain unused.</param>
        /// <param name="expectedObservationReads">The expected observation-path reads.</param>
        private static void AssertCreateRejectedBeforeOutput(
            Action operation,
            RecordingArtifactReader reader,
            RecordingArtifactWriter writer,
            int expectedObservationReads
        )
        {
            Assert.Throws<InvalidOperationException>(() => operation());
            Assert.That(reader.ObservationReads, Is.EqualTo(expectedObservationReads));
            Assert.That(reader.ReviewedReads, Is.Zero);
            Assert.That(writer.WriteCalls, Is.Zero);
        }

        /// <summary>Asserts an invalid input does not reach any mutable boundary.</summary>
        /// <param name="operation">The operation expected to reject.</param>
        /// <param name="writer">The writer that must remain unused.</param>
        /// <param name="boundary">The transaction boundary that must remain unused.</param>
        /// <param name="reader">The artifact reader whose reached paths are counted.</param>
        /// <param name="expectedObservationReads">The expected observation-path reads after input rejection.</param>
        /// <param name="expectedReviewedReads">The expected reviewed-path reads after input rejection.</param>
        private static void AssertRejectedBeforeWrite(
            Action operation,
            RecordingWriter writer,
            RecordingWriteBoundary boundary,
            RecordingArtifactReader reader = null,
            int expectedObservationReads = 0,
            int expectedReviewedReads = 0
        )
        {
            try
            {
                operation();
                Assert.Fail("Invalid reviewed-baseline input reached a write boundary.");
            }
            catch (InvalidOperationException) { }

            AssertNoWrites(writer, boundary);
            AssertArtifactReads(reader, expectedObservationReads, expectedReviewedReads);
        }

        /// <summary>Asserts no transaction, writer, or simulated storage backend call occurred.</summary>
        /// <param name="writer">The writer that must remain unused.</param>
        /// <param name="boundary">The transaction boundary that must remain unused.</param>
        private static void AssertNoWrites(RecordingWriter writer, RecordingWriteBoundary boundary)
        {
            Assert.That(writer.WriteCalls, Is.Zero);
            Assert.That(writer.StorageBackendCalls, Is.Zero);
            Assert.That(boundary.BeginCalls, Is.Zero);
            Assert.That(boundary.VerifyCalls, Is.Zero);
        }

        /// <summary>Asserts each artifact path was read only when the operation reached that input boundary.</summary>
        /// <param name="reader">The reader to inspect, or <see langword="null"/> when no artifact input exists.</param>
        /// <param name="expectedObservationReads">The expected observation-path read count.</param>
        /// <param name="expectedReviewedReads">The expected reviewed-path read count.</param>
        private static void AssertArtifactReads(
            RecordingArtifactReader reader,
            int expectedObservationReads,
            int expectedReviewedReads
        )
        {
            if (reader == null)
                return;

            Assert.That(reader.ObservationReads, Is.EqualTo(expectedObservationReads));
            Assert.That(reader.ReviewedReads, Is.EqualTo(expectedReviewedReads));
        }

        /// <summary>Asserts all canonical fields and ranges remain unchanged except exact PBR and Hybrid ranges.</summary>
        /// <param name="canonical">The original canonical baseline.</param>
        /// <param name="approved">The approved merged baseline.</param>
        /// <param name="observation">The source observation.</param>
        private static void AssertCanonicalPreservation(
            SceneRegressionBaseline canonical,
            SceneRegressionBaseline approved,
            PureBaseRegressionBaselineGenerator.ObservationCandidate observation
        )
        {
            Assert.That(approved.schemaVersion, Is.EqualTo(canonical.schemaVersion));
            Assert.That(approved.unityVersion, Is.EqualTo(canonical.unityVersion));
            Assert.That(approved.graphicsDevice, Is.EqualTo(canonical.graphicsDevice));
            Assert.That(approved.colorSpace, Is.EqualTo(canonical.colorSpace));
            Assert.That(approved.renderPipeline, Is.EqualTo(canonical.renderPipeline));
            Assert.That(approved.renderSize, Is.EqualTo(canonical.renderSize));
            Assert.That(approved.staticLightmapCount, Is.EqualTo(canonical.staticLightmapCount));
            Assert.That(
                approved.staticRendererAssignmentCount,
                Is.EqualTo(canonical.staticRendererAssignmentCount)
            );
            AssertRange(approved.sceneVisiblePixelCount, canonical.sceneVisiblePixelCount);
            AssertRange(approved.shadowChangedPixelCount, canonical.shadowChangedPixelCount);
            Assert.That(approved.warmedVariantCount, Is.EqualTo(canonical.warmedVariantCount));
            Assert.That(
                approved.dynamicLightmapStatus,
                Is.EqualTo(canonical.dynamicLightmapStatus)
            );
            Assert.That(approved.metaAlbedo, Has.Length.EqualTo(canonical.metaAlbedo.Length));

            for (int index = 0; index < canonical.metaAlbedo.Length; index++)
            {
                MetaAlbedoBaseline actual = approved.metaAlbedo[index];
                MetaAlbedoBaseline expected = canonical.metaAlbedo[index];
                Assert.That(actual.materialName, Is.EqualTo(expected.materialName));
                Assert.That(actual.shaderName, Is.EqualTo(expected.shaderName));
                if (index < 2)
                    AssertRange(actual.meanLuminance, expected.meanLuminance);
                else
                    Assert.That(
                        actual.meanLuminance.minimum,
                        Is.EqualTo(observation.observation.metaAlbedo[index].meanLuminance)
                    );
                if (index >= 2)
                    Assert.That(
                        actual.meanLuminance.maximum,
                        Is.EqualTo(observation.observation.metaAlbedo[index].meanLuminance)
                    );
            }
        }

        /// <summary>Asserts two integer ranges are identical.</summary>
        /// <param name="actual">The actual range.</param>
        /// <param name="expected">The expected range.</param>
        private static void AssertRange(IntRange actual, IntRange expected)
        {
            Assert.That(actual.minimum, Is.EqualTo(expected.minimum));
            Assert.That(actual.maximum, Is.EqualTo(expected.maximum));
        }

        /// <summary>Asserts two floating-point ranges are identical.</summary>
        /// <param name="actual">The actual range.</param>
        /// <param name="expected">The expected range.</param>
        private static void AssertRange(FloatRange actual, FloatRange expected)
        {
            Assert.That(actual.minimum, Is.EqualTo(expected.minimum));
            Assert.That(actual.maximum, Is.EqualTo(expected.maximum));
        }

        /// <summary>Creates a complete valid raw observation and exact baseline fixture.</summary>
        /// <returns>The valid observation candidate.</returns>
        private static PureBaseRegressionBaselineGenerator.ObservationCandidate CreateObservation()
        {
            SceneRegressionObservation observation = new SceneRegressionObservation
            {
                staticLightmapCount = 2,
                staticRendererAssignmentCount = 20,
                sceneFinitePixelCount = 25600,
                sceneVisiblePixelCount = 11,
                sceneVisibleCoverage = 0.5f,
                sceneVisibleCentroidX = 0.5f,
                sceneVisibleCentroidY = 0.5f,
                shadowChangedPixelCount = 347,
                shadowCoveragePixelCount = 12800,
                shadowCoverage = 0.5f,
                shadowCentroidX = 0.5f,
                shadowCentroidY = 0.5f,
                shadowMaxAbsoluteRgbDelta = 0.25f,
                warmedVariantCount = 56,
                dynamicLightmapStatus =
                    PureBaseRegressionBaselineGenerator.DynamicLightmapLimitation,
                metaAlbedo = new[]
                {
                    CreateObservedMeta("PureBaseValidationUnlit", "PureBase/Unlit", 0.011f),
                    CreateObservedMeta("PureBaseValidationToon", "PureBase/Toon", 0.031f),
                    CreateObservedMeta("PureBaseValidationPbr", "PureBase/PBR", 0.051f),
                    CreateObservedMeta("PureBaseValidationHybrid", "PureBase/Hybrid", 0.071f),
                },
            };
            return new PureBaseRegressionBaselineGenerator.ObservationCandidate
            {
                schemaVersion =
                    PureBaseRegressionBaselineGenerator.ObservationCandidateSchemaVersion,
                unityVersion = PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                graphicsDevice = GraphicsDeviceType.Direct3D11.ToString(),
                colorSpace = ColorSpace.Linear.ToString(),
                renderPipeline = "BuiltIn",
                observation = observation,
                exactBaseline = CreateExactBaseline(observation),
            };
        }

        /// <summary>Creates the complete exact baseline representation of one valid observation.</summary>
        /// <param name="observation">The observation represented exactly.</param>
        /// <returns>The complete exact baseline.</returns>
        private static SceneRegressionBaseline CreateExactBaseline(
            SceneRegressionObservation observation
        )
        {
            return new SceneRegressionBaseline
            {
                schemaVersion = PureBaseValidationSceneRegressionTests.BaselineSchemaVersion,
                unityVersion = PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                graphicsDevice = GraphicsDeviceType.Direct3D11.ToString(),
                colorSpace = ColorSpace.Linear.ToString(),
                renderPipeline = "BuiltIn",
                renderSize = PureBaseValidationSceneRegressionTests.RenderSize,
                staticLightmapCount = observation.staticLightmapCount,
                staticRendererAssignmentCount = observation.staticRendererAssignmentCount,
                sceneVisiblePixelCount = IntRange.Exact(observation.sceneVisiblePixelCount),
                shadowChangedPixelCount = IntRange.Exact(observation.shadowChangedPixelCount),
                warmedVariantCount = observation.warmedVariantCount,
                dynamicLightmapStatus = observation.dynamicLightmapStatus,
                metaAlbedo = new[]
                {
                    CreateMeta("PureBaseValidationUnlit", "PureBase/Unlit", 0.011f, 0.011f),
                    CreateMeta("PureBaseValidationToon", "PureBase/Toon", 0.031f, 0.031f),
                    CreateMeta("PureBaseValidationPbr", "PureBase/PBR", 0.051f, 0.051f),
                    CreateMeta("PureBaseValidationHybrid", "PureBase/Hybrid", 0.071f, 0.071f),
                },
            };
        }

        /// <summary>Creates a canonical baseline whose PBR and Hybrid ranges differ from observations.</summary>
        /// <returns>The complete current canonical baseline.</returns>
        private static SceneRegressionBaseline CreateCanonicalBaseline()
        {
            return new SceneRegressionBaseline
            {
                schemaVersion = PureBaseValidationSceneRegressionTests.BaselineSchemaVersion,
                unityVersion = PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                graphicsDevice = GraphicsDeviceType.Direct3D11.ToString(),
                colorSpace = ColorSpace.Linear.ToString(),
                renderPipeline = "BuiltIn",
                renderSize = PureBaseValidationSceneRegressionTests.RenderSize,
                staticLightmapCount = 2,
                staticRendererAssignmentCount = 20,
                sceneVisiblePixelCount = new IntRange { minimum = 10, maximum = 12 },
                shadowChangedPixelCount = new IntRange { minimum = 341, maximum = 352 },
                warmedVariantCount = 56,
                dynamicLightmapStatus =
                    PureBaseRegressionBaselineGenerator.DynamicLightmapLimitation,
                metaAlbedo = new[]
                {
                    CreateMeta("PureBaseValidationUnlit", "PureBase/Unlit", 0.01f, 0.010001f),
                    CreateMeta("PureBaseValidationToon", "PureBase/Toon", 0.03f, 0.0300005f),
                    CreateMeta("PureBaseValidationPbr", "PureBase/PBR", 0.05f, 0.05f),
                    CreateMeta("PureBaseValidationHybrid", "PureBase/Hybrid", 0.07f, 0.07f),
                },
            };
        }

        /// <summary>Creates canonical raw JSON with non-canonical but equivalent float literal spellings and a terminal newline.</summary>
        /// <returns>The raw canonical JSON text used by lossless replacement contracts.</returns>
        private static string CreateRawCanonicalBaselineText() =>
            "{\n"
            + "  \"metaAlbedo\": [\n"
            + "    {\"materialName\": \"PureBaseValidationUnlit\", \"shaderName\": \"PureBase/Unlit\", \"meanLuminance\": {\"minimum\": 1.0e-2, \"maximum\": 1.0001e-2}},\n"
            + "    {\"materialName\": \"PureBaseValidationToon\", \"shaderName\": \"PureBase/Toon\", \"meanLuminance\": {\"minimum\": 3.0e-2, \"maximum\": 3.00005e-2}},\n"
            + "    {\"materialName\": \"PureBaseValidationPbr\", \"shaderName\": \"PureBase/PBR\", \"meanLuminance\": {\"minimum\": 5.0e-2, \"maximum\": 5.0e-2}},\n"
            + "    {\"materialName\": \"PureBaseValidationHybrid\", \"shaderName\": \"PureBase/Hybrid\", \"meanLuminance\": {\"minimum\": 7.0e-2, \"maximum\": 7.0e-2}}\n"
            + "  ]\n"
            + "}\n";

        /// <summary>Creates the exact predecessor canonical JSON text for the approved Unlit and Toon range migration.</summary>
        /// <returns>The lossless migration source with a terminal newline.</returns>
        private static string CreateApprovedUnlitToonMigrationSourceText() =>
            "{\n"
            + "  \"shadowChangedPixelCount\": {\"minimum\": 341, \"maximum\": 352},\n"
            + "  \"metaAlbedo\": [\n"
            + "    {\"materialName\": \"PureBaseValidationUnlit\", \"shaderName\": \"PureBase/Unlit\", \"meanLuminance\": {\"minimum\": 0.04757445678114891, \"maximum\": 0.04757445678114891}},\n"
            + "    {\"materialName\": \"PureBaseValidationToon\", \"shaderName\": \"PureBase/Toon\", \"meanLuminance\": {\"minimum\": 0.08925552666187286, \"maximum\": 0.08925552666187286}},\n"
            + "    {\"materialName\": \"PureBaseValidationPbr\", \"shaderName\": \"PureBase/PBR\", \"meanLuminance\": {\"minimum\": 0.0477057620882988, \"maximum\": 0.0477057620882988}},\n"
            + "    {\"materialName\": \"PureBaseValidationHybrid\", \"shaderName\": \"PureBase/Hybrid\", \"meanLuminance\": {\"minimum\": 0.12078462541103363, \"maximum\": 0.12078462541103363}}\n"
            + "  ]\n"
            + "}\n";

        /// <summary>Creates one source fixture with a single stale literal or Meta identity invariant.</summary>
        /// <param name="condition">The invariant to invalidate.</param>
        /// <returns>The invalid migration source text.</returns>
        private static string CreateInvalidApprovedUnlitToonMigrationSourceText(string condition)
        {
            string source = CreateApprovedUnlitToonMigrationSourceText();
            if (condition == "stale")
            {
                return source.Replace("0.04757445678114891", "0.04757445678114890");
            }

            if (condition == "missing")
            {
                return source.Replace(
                    "    {\"materialName\": \"PureBaseValidationUnlit\", \"shaderName\": \"PureBase/Unlit\", \"meanLuminance\": {\"minimum\": 0.04757445678114891, \"maximum\": 0.04757445678114891}},\n",
                    string.Empty
                );
            }

            if (condition == "duplicate")
            {
                return source.Replace("PureBaseValidationUnlit", "PureBaseValidationToon");
            }

            if (condition == "reordered")
            {
                const string UnlitLine =
                    "    {\"materialName\": \"PureBaseValidationUnlit\", \"shaderName\": \"PureBase/Unlit\", \"meanLuminance\": {\"minimum\": 0.04757445678114891, \"maximum\": 0.04757445678114891}},\n";
                const string ToonLine =
                    "    {\"materialName\": \"PureBaseValidationToon\", \"shaderName\": \"PureBase/Toon\", \"meanLuminance\": {\"minimum\": 0.08925552666187286, \"maximum\": 0.08925552666187286}},\n";
                return source.Replace(UnlitLine + ToonLine, ToonLine + UnlitLine);
            }

            return source.Replace("PureBase/Toon", "PureBase/Unexpected");
        }

        /// <summary>Extracts one rewritten numeric literal from the specified ordered Meta entry.</summary>
        /// <param name="json">The rewritten canonical JSON.</param>
        /// <param name="materialName">The Meta material identity.</param>
        /// <param name="propertyName">The requested range property name.</param>
        /// <returns>The exact rewritten numeric literal.</returns>
        private static string ExtractMetaRangeLiteral(
            string json,
            string materialName,
            string propertyName
        )
        {
            int materialIndex = json.IndexOf(
                $"\"materialName\": \"{materialName}\"",
                StringComparison.Ordinal
            );
            Assert.That(materialIndex, Is.GreaterThanOrEqualTo(0));
            int propertyIndex = json.IndexOf(
                $"\"{propertyName}\": ",
                materialIndex,
                StringComparison.Ordinal
            );
            Assert.That(propertyIndex, Is.GreaterThan(materialIndex));
            int literalStart = propertyIndex + propertyName.Length + 4;
            int literalEnd = json.IndexOfAny(new[] { ',', '}' }, literalStart);
            Assert.That(literalEnd, Is.GreaterThan(literalStart));
            return json.Substring(literalStart, literalEnd - literalStart);
        }

        /// <summary>Parses one JSON numeric literal as an invariant single-precision value for reviewed-target assertions.</summary>
        /// <param name="literal">The JSON numeric literal.</param>
        /// <returns>The parsed float value.</returns>
        private static float ParseInvariantSingle(string literal) =>
            float.Parse(
                literal,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture
            );

        /// <summary>Creates a complete valid reviewed candidate fixture.</summary>
        /// <returns>The reviewed candidate with only exact PBR and Hybrid replacements.</returns>
        private static PureBaseReviewedBaselineCandidate CreateReviewedCandidate()
        {
            SceneRegressionBaseline approved = CreateCanonicalBaseline();
            approved.metaAlbedo[2].meanLuminance = FloatRange.Exact(0.051f);
            approved.metaAlbedo[3].meanLuminance = FloatRange.Exact(0.071f);
            return new PureBaseReviewedBaselineCandidate
            {
                schemaVersion = PureBaseReviewedBaselineCandidate.SchemaVersion,
                sourceObservationSha256 = ComputeFixtureSha256(CreateObservationBytes()),
                canonicalPbrRange = new FloatRange { minimum = 0.05f, maximum = 0.05f },
                canonicalHybridRange = new FloatRange { minimum = 0.07f, maximum = 0.07f },
                approvedBaseline = approved,
            };
        }

        /// <summary>Creates the fixed compatible environment used by reviewed-artifact creation tests.</summary>
        /// <returns>The compatible editor environment.</returns>
        private static PureBaseRegressionBaselineGenerator.IEnvironment CreateValidEnvironment()
        {
            return new CandidateEnvironment(
                PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                true,
                GraphicsDeviceType.Direct3D11,
                ColorSpace.Linear
            );
        }

        /// <summary>Creates a Meta observation entry.</summary>
        /// <param name="materialName">The material identity.</param>
        /// <param name="shaderName">The shader identity.</param>
        /// <param name="meanLuminance">The observed luminance.</param>
        /// <returns>The complete observation entry.</returns>
        private static MetaAlbedoObservation CreateObservedMeta(
            string materialName,
            string shaderName,
            float meanLuminance
        )
        {
            return new MetaAlbedoObservation
            {
                materialName = materialName,
                shaderName = shaderName,
                meanLuminance = meanLuminance,
            };
        }

        /// <summary>Creates a Meta baseline entry.</summary>
        /// <param name="materialName">The material identity.</param>
        /// <param name="shaderName">The shader identity.</param>
        /// <param name="minimum">The range minimum.</param>
        /// <param name="maximum">The range maximum.</param>
        /// <returns>The complete baseline entry.</returns>
        private static MetaAlbedoBaseline CreateMeta(
            string materialName,
            string shaderName,
            float minimum,
            float maximum
        )
        {
            return new MetaAlbedoBaseline
            {
                materialName = materialName,
                shaderName = shaderName,
                meanLuminance = new FloatRange { minimum = minimum, maximum = maximum },
            };
        }

        /// <summary>Serializes the valid observation fixture into exact UTF-8 artifact bytes.</summary>
        /// <returns>The valid observation artifact bytes.</returns>
        private static byte[] CreateObservationBytes() => SerializeObservation(CreateObservation());

        /// <summary>Serializes the valid reviewed fixture into exact UTF-8 artifact bytes.</summary>
        /// <returns>The valid reviewed artifact bytes.</returns>
        private static byte[] CreateReviewedBytes() => SerializeReviewed(CreateReviewedCandidate());

        /// <summary>Serializes an observation fixture without file I/O.</summary>
        /// <param name="observation">The observation fixture to serialize.</param>
        /// <returns>The UTF-8 artifact bytes.</returns>
        private static byte[] SerializeObservation(
            PureBaseRegressionBaselineGenerator.ObservationCandidate observation
        ) =>
            Encoding.UTF8.GetBytes(
                PureBaseRegressionBaselineGenerator.SerializeObservationCandidate(observation)
            );

        /// <summary>Serializes a reviewed fixture without file I/O.</summary>
        /// <param name="candidate">The reviewed fixture to serialize.</param>
        /// <returns>The UTF-8 artifact bytes.</returns>
        private static byte[] SerializeReviewed(PureBaseReviewedBaselineCandidate candidate) =>
            Encoding.UTF8.GetBytes(JsonUtility.ToJson(candidate, true));

        /// <summary>Computes an independent SHA-256 fixture value without exercising the DTO method under test.</summary>
        /// <param name="bytes">The exact bytes to hash.</param>
        /// <returns>The lowercase hexadecimal hash.</returns>
        private static string ComputeFixtureSha256(byte[] bytes)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(bytes);
                var result = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    result.Append(value.ToString("x2"));
                return result.ToString();
            }
        }

        /// <summary>Creates one invalid command line by changing exactly one required path condition.</summary>
        /// <param name="condition">The path condition to invalidate.</param>
        /// <param name="observationPath">Whether to invalidate the observation rather than reviewed path.</param>
        /// <returns>The invalid argument sequence.</returns>
        private static string[] CreateInvalidArguments(string condition, bool observationPath)
        {
            string argument = observationPath
                ? PureBaseReviewedBaselineCandidate.ObservationCandidatePathArgument
                : PureBaseReviewedBaselineCandidate.ReviewedMetaBaselinePathArgument;
            string path = observationPath ? ObservationPath : ReviewedPath;
            string otherArgument = observationPath
                ? PureBaseReviewedBaselineCandidate.ReviewedMetaBaselinePathArgument
                : PureBaseReviewedBaselineCandidate.ObservationCandidatePathArgument;
            string otherPath = observationPath ? ReviewedPath : ObservationPath;
            if (condition == "missing")
                return new[] { otherArgument, otherPath };
            if (condition == "duplicate")
                return new[] { argument, path, argument, path + ".copy", otherArgument, otherPath };
            if (condition == "empty")
                return new[] { argument, string.Empty, otherArgument, otherPath };
            return new[] { argument, path, otherArgument };
        }

        /// <summary>Creates a reader whose second result for either path differs from its first result.</summary>
        /// <param name="observationBytes">The first observation bytes.</param>
        /// <param name="reviewedBytes">The first reviewed bytes.</param>
        /// <returns>The deterministic read recorder.</returns>
        private static RecordingArtifactReader CreateReader(
            byte[] observationBytes,
            byte[] reviewedBytes
        )
        {
            return new RecordingArtifactReader(
                observationBytes,
                reviewedBytes,
                Encoding.UTF8.GetBytes("changed observation bytes"),
                Encoding.UTF8.GetBytes("changed reviewed bytes")
            );
        }

        /// <summary>Identifies the external observation artifact path used by contract tests.</summary>
        private const string ObservationPath = "C:/PureBaseCandidates/observation.json";

        /// <summary>Identifies the external reviewed artifact path used by contract tests.</summary>
        private const string ReviewedPath = "C:/PureBaseCandidates/reviewed.json";

        /// <summary>Defines the known SHA-256 vector input for <c>{"schema":1}</c>.</summary>
        private static readonly byte[] KnownVectorBytes =
        {
            0x7B,
            0x22,
            0x73,
            0x63,
            0x68,
            0x65,
            0x6D,
            0x61,
            0x22,
            0x3A,
            0x31,
            0x7D,
        };

        /// <summary>Records per-path reads and returns different content only for hypothetical second reads.</summary>
        private sealed class RecordingArtifactReader
            : PureBaseReviewedBaselineCandidate.IArtifactReader
        {
            /// <summary>Stores the first observation-path response.</summary>
            private readonly byte[] observationBytes;

            /// <summary>Stores the first reviewed-path response.</summary>
            private readonly byte[] reviewedBytes;

            /// <summary>Stores the changed second observation-path response.</summary>
            private readonly byte[] secondObservationBytes;

            /// <summary>Stores the changed second reviewed-path response.</summary>
            private readonly byte[] secondReviewedBytes;

            /// <summary>Initializes first and changed-second read responses.</summary>
            /// <param name="observationBytes">The first observation response.</param>
            /// <param name="reviewedBytes">The first reviewed response.</param>
            /// <param name="secondObservationBytes">The changed second observation response.</param>
            /// <param name="secondReviewedBytes">The changed second reviewed response.</param>
            public RecordingArtifactReader(
                byte[] observationBytes,
                byte[] reviewedBytes,
                byte[] secondObservationBytes,
                byte[] secondReviewedBytes
            )
            {
                this.observationBytes = observationBytes;
                this.reviewedBytes = reviewedBytes;
                this.secondObservationBytes = secondObservationBytes;
                this.secondReviewedBytes = secondReviewedBytes;
            }

            /// <summary>Gets the number of observation-path reads.</summary>
            public int ObservationReads { get; private set; }

            /// <summary>Gets the number of reviewed-path reads.</summary>
            public int ReviewedReads { get; private set; }

            /// <inheritdoc />
            public byte[] ReadAllBytes(string path)
            {
                if (path == ObservationPath)
                {
                    ObservationReads++;
                    return ObservationReads == 1 ? observationBytes : secondObservationBytes;
                }

                if (path == ReviewedPath)
                {
                    ReviewedReads++;
                    return ReviewedReads == 1 ? reviewedBytes : secondReviewedBytes;
                }

                throw new InvalidOperationException($"Unexpected artifact path '{path}'.");
            }
        }

        /// <summary>Records reviewed-artifact output without writing to the filesystem.</summary>
        private sealed class RecordingArtifactWriter
            : PureBaseReviewedBaselineCandidate.IArtifactWriter
        {
            /// <summary>Gets the number of output calls.</summary>
            public int WriteCalls { get; private set; }

            /// <summary>Gets the most recent output path.</summary>
            public string LastPath { get; private set; }

            /// <inheritdoc />
            public void WriteAllText(string path, string contents)
            {
                WriteCalls++;
                LastPath = path;
            }
        }

        /// <summary>Supplies fixed environment values without reading or changing the Unity editor.</summary>
        private sealed class CandidateEnvironment : PureBaseRegressionBaselineGenerator.IEnvironment
        {
            /// <summary>Initializes fixed environment values.</summary>
            public CandidateEnvironment(
                string unityVersion,
                bool isBuiltInRenderPipeline,
                GraphicsDeviceType graphicsDeviceType,
                ColorSpace colorSpace
            )
            {
                UnityVersion = unityVersion;
                IsBuiltInRenderPipeline = isBuiltInRenderPipeline;
                GraphicsDeviceType = graphicsDeviceType;
                ColorSpace = colorSpace;
            }

            /// <inheritdoc />
            public string UnityVersion { get; }

            /// <inheritdoc />
            public bool IsBuiltInRenderPipeline { get; }

            /// <inheritdoc />
            public GraphicsDeviceType GraphicsDeviceType { get; }

            /// <inheritdoc />
            public ColorSpace ColorSpace { get; }
        }

        /// <summary>Records reviewed-writer and simulated storage-backend calls without persistence.</summary>
        private sealed class RecordingWriter
            : PureBaseRegressionBaselineGenerator.IReviewedCandidateWriter
        {
            /// <summary>Gets the number of writer calls.</summary>
            public int WriteCalls { get; private set; }

            /// <summary>Gets the number of simulated storage backend calls caused by a writer call.</summary>
            public int StorageBackendCalls { get; private set; }

            /// <inheritdoc />
            public void WriteExactBaseline(
                SceneRegressionBaseline baseline,
                PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
            )
            {
                WriteCalls++;
                StorageBackendCalls++;
            }
        }

        /// <summary>Records prepared lossless bytes without touching durable storage.</summary>
        private sealed class RecordingLosslessWriter
            : PureBaseReviewedBaselineCandidate.ILosslessReviewedCandidateWriter
        {
            /// <summary>Gets the number of lossless writer calls.</summary>
            public int WriteCalls { get; private set; }

            /// <summary>Gets the last prepared raw canonical bytes.</summary>
            public byte[] WrittenBytes { get; private set; }

            /// <summary>Gets the exact canonical bytes supplied to the final source comparison.</summary>
            public byte[] ExpectedBytes { get; private set; }

            /// <inheritdoc />
            public void WriteLosslessReviewedBaseline(
                byte[] expectedCanonicalBaselineBytes,
                byte[] reviewedCanonicalBaselineBytes,
                PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
            )
            {
                WriteCalls++;
                ExpectedBytes = expectedCanonicalBaselineBytes;
                WrittenBytes = reviewedCanonicalBaselineBytes;
            }
        }

        /// <summary>Simulates the conditional storage operation that rejects a canonical source changed after snapshot capture.</summary>
        private sealed class FinalSourceComparisonWriter
            : PureBaseReviewedBaselineCandidate.ILosslessReviewedCandidateWriter
        {
            /// <summary>Stores the conditional canonical persistence backend exercised by this writer.</summary>
            private readonly ChangedCanonicalStorageBackend backend;

            /// <summary>Initializes a final source comparison with controlled current canonical bytes.</summary>
            /// <param name="currentCanonicalBytes">The bytes present after snapshot capture.</param>
            public FinalSourceComparisonWriter(byte[] currentCanonicalBytes)
            {
                backend = new ChangedCanonicalStorageBackend(currentCanonicalBytes);
            }

            /// <summary>Gets the number of final source comparisons.</summary>
            public int FinalSourceComparisons => backend.FinalSourceComparisons;

            /// <summary>Gets the number of simulated canonical writes.</summary>
            public int BaselineWriteCalls => backend.BaselineWriteCalls;

            /// <summary>Gets the number of simulated canonical imports.</summary>
            public int ImportCalls => backend.ImportCalls;

            /// <inheritdoc />
            public void WriteLosslessReviewedBaseline(
                byte[] expectedCanonicalBaselineBytes,
                byte[] reviewedCanonicalBaselineBytes,
                PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
            )
            {
                PureBaseRegressionBaselineStorage.WriteReviewedCanonicalBaselineBytesIfCurrent(
                    expectedCanonicalBaselineBytes,
                    reviewedCanonicalBaselineBytes,
                    writeBoundary,
                    backend
                );
            }
        }

        /// <summary>Models canonical storage whose durable bytes change between snapshot capture and conditional persistence.</summary>
        private sealed class ChangedCanonicalStorageBackend
            : ICanonicalBaselineStorageBackend,
                IConditionalRawCanonicalBaselineStorageBackend
        {
            /// <summary>Stores the durable canonical bytes observed at the final write boundary.</summary>
            private readonly byte[] currentCanonicalBytes;

            /// <summary>Initializes the controlled canonical source state.</summary>
            /// <param name="currentCanonicalBytes">The bytes present after snapshot capture.</param>
            public ChangedCanonicalStorageBackend(byte[] currentCanonicalBytes)
            {
                this.currentCanonicalBytes = currentCanonicalBytes;
            }

            /// <summary>Gets the number of final conditional source comparisons.</summary>
            public int FinalSourceComparisons { get; private set; }

            /// <summary>Gets the number of simulated conditional canonical writes.</summary>
            public int BaselineWriteCalls { get; private set; }

            /// <summary>Gets the number of canonical imports.</summary>
            public int ImportCalls { get; private set; }

            /// <inheritdoc />
            public bool IsDirectoryValid(string assetPath) => true;

            /// <inheritdoc />
            public void CreateDirectory(string parentAssetPath, string directoryName) =>
                throw new InvalidOperationException(
                    "The final source comparison must not create directories."
                );

            /// <inheritdoc />
            public void WriteAllText(string path, string contents) =>
                throw new InvalidOperationException(
                    "The final source comparison must not serialize canonical JSON."
                );

            /// <inheritdoc />
            public bool TryWriteAllBytesIfCurrent(
                string path,
                byte[] expectedContents,
                byte[] replacementContents
            )
            {
                FinalSourceComparisons++;
                if (!BytesMatch(expectedContents, currentCanonicalBytes))
                    return false;

                BaselineWriteCalls++;
                return true;
            }

            /// <inheritdoc />
            public void ImportAsset(string path) => ImportCalls++;

            /// <summary>Compares two nullable byte arrays without treating references as equivalent source state.</summary>
            /// <param name="left">The expected source bytes.</param>
            /// <param name="right">The current source bytes.</param>
            /// <returns>Whether both arrays contain the same bytes.</returns>
            private static bool BytesMatch(byte[] left, byte[] right)
            {
                if (left == null || right == null || left.Length != right.Length)
                    return false;

                for (int index = 0; index < left.Length; index++)
                    if (left[index] != right[index])
                        return false;
                return true;
            }
        }

        /// <summary>Records transaction operations without inspecting or mutating workspace state.</summary>
        private sealed class RecordingWriteBoundary
            : PureBaseRegressionBaselineGenerator.IWriteBoundary
        {
            /// <summary>Gets the number of transaction starts.</summary>
            public int BeginCalls { get; private set; }

            /// <summary>Gets the number of transaction verification calls.</summary>
            public int VerifyCalls { get; private set; }

            /// <inheritdoc />
            public void BeginTransaction() => BeginCalls++;

            /// <inheritdoc />
            public void VerifyNoUnrelatedChanges() => VerifyCalls++;
        }
    }
}
