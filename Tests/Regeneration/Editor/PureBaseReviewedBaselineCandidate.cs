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

// Defines the reviewed-baseline artifact merge and validated canonical apply boundary.

using System;
using System.Security.Cryptography;
using System.Text;
using PureBase.Tests.Daily;
using UnityEngine;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Defines the versioned reviewed artifact that binds an approved baseline to raw observation bytes.</summary>
    [Serializable]
    public sealed class PureBaseReviewedBaselineCandidate
    {
        /// <summary>Identifies the only supported reviewed-baseline artifact schema.</summary>
        public const int SchemaVersion = 3;

        /// <summary>Identifies the required raw-observation path command-line argument.</summary>
        internal const string ObservationCandidatePathArgument =
            "-pureBaseObservationCandidatePath";

        /// <summary>Identifies the required reviewed Meta baseline path command-line argument.</summary>
        internal const string ReviewedMetaBaselinePathArgument =
            "-pureBaseReviewedMetaBaselinePath";

        /// <summary>Stores the reviewed artifact schema version.</summary>
        public int schemaVersion;

        /// <summary>Stores the lowercase SHA-256 digest of the exact raw observation artifact bytes.</summary>
        public string sourceObservationSha256;

        /// <summary>Stores the canonical PBR range that was current when review began.</summary>
        public FloatRange canonicalPbrRange;

        /// <summary>Stores the canonical Hybrid range that was current when review began.</summary>
        public FloatRange canonicalHybridRange;

        /// <summary>Stores the complete canonical baseline with only approved Meta observations replaced.</summary>
        public SceneRegressionBaseline approvedBaseline;

        /// <summary>Defines the byte reader used by reviewed-artifact apply without exposing file-system operations.</summary>
        internal interface IArtifactReader
        {
            /// <summary>Reads one external artifact into an immutable byte array.</summary>
            /// <param name="path">The validated external artifact path.</param>
            /// <returns>The exact artifact bytes.</returns>
            byte[] ReadAllBytes(string path);
        }

        /// <summary>Defines the reviewed-artifact output operation without exposing file-system operations.</summary>
        internal interface IArtifactWriter
        {
            /// <summary>Writes one serialized reviewed artifact to its validated external path.</summary>
            /// <param name="path">The validated reviewed artifact output path.</param>
            /// <param name="contents">The fully validated reviewed artifact JSON.</param>
            void WriteAllText(string path, string contents);
        }

        /// <summary>Defines the reviewed Meta writer that preserves raw canonical baseline bytes outside approved numeric ranges.</summary>
        internal interface ILosslessReviewedCandidateWriter
        {
            /// <summary>Writes one validated reviewed baseline with only its approved target numeric literals replaced.</summary>
            /// <param name="expectedCanonicalBaselineBytes">The exact canonical bytes used for candidate validation.</param>
            /// <param name="reviewedCanonicalBaselineBytes">The fully validated canonical bytes with exactly four approved literals replaced.</param>
            /// <param name="writeBoundary">The active audited write boundary.</param>
            void WriteLosslessReviewedBaseline(
                byte[] expectedCanonicalBaselineBytes,
                byte[] reviewedCanonicalBaselineBytes,
                PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
            );
        }

        /// <summary>Creates a reviewed artifact from a raw observation, the current canonical baseline, and exact source bytes.</summary>
        /// <param name="observationCandidate">The validated raw observation candidate.</param>
        /// <param name="canonicalBaseline">The current canonical baseline whose unrelated ranges must be preserved.</param>
        /// <param name="rawObservationBytes">The exact serialized raw observation artifact bytes.</param>
        /// <returns>The reviewed artifact bound to the raw observation bytes.</returns>
        public static PureBaseReviewedBaselineCandidate Create(
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate,
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawObservationBytes
        )
        {
            return Create(
                new PureBaseRegressionBaselineGenerator.UnityEnvironment(),
                observationCandidate,
                canonicalBaseline,
                rawObservationBytes
            );
        }

        /// <summary>Creates a reviewed artifact through an explicit environment-validation seam.</summary>
        /// <param name="environment">The active editor environment.</param>
        /// <param name="observationCandidate">The validated raw observation candidate.</param>
        /// <param name="canonicalBaseline">The current canonical baseline whose unrelated ranges must be preserved.</param>
        /// <param name="rawObservationBytes">The exact serialized raw observation artifact bytes.</param>
        /// <returns>The reviewed artifact bound to the raw observation bytes and canonical target ranges.</returns>
        internal static PureBaseReviewedBaselineCandidate Create(
            PureBaseRegressionBaselineGenerator.IEnvironment environment,
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate,
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawObservationBytes
        )
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (rawObservationBytes == null)
                throw new ArgumentNullException(nameof(rawObservationBytes));
            PureBaseRegressionBaselineGenerator.ObservationCandidate sourceObservation =
                DeserializeObservationCandidate(rawObservationBytes);
            if (!string.Equals(
                    PureBaseRegressionBaselineGenerator.SerializeObservationCandidate(
                        observationCandidate
                    ),
                    PureBaseRegressionBaselineGenerator.SerializeObservationCandidate(sourceObservation),
                    StringComparison.Ordinal
                ))
            {
                throw new InvalidOperationException(
                    "The raw observation bytes do not match the reviewed baseline source observation."
                );
            }

            ValidateObservationAndCanonical(environment, sourceObservation, canonicalBaseline);

            SceneRegressionBaseline approvedBaseline = CloneBaseline(canonicalBaseline);
            for (int index = 0; index < ExpectedMetaIdentities.Length; index++)
            {
                if (!ExpectedMetaIdentities[index].IsApprovedTarget)
                    continue;

                approvedBaseline.metaAlbedo[index].meanLuminance = FloatRange.Exact(
                    sourceObservation.observation.metaAlbedo[index].meanLuminance
                );
            }

            return new PureBaseReviewedBaselineCandidate
            {
                schemaVersion = SchemaVersion,
                sourceObservationSha256 = ComputeSha256(rawObservationBytes),
                canonicalPbrRange = CloneRange(canonicalBaseline.metaAlbedo[2].meanLuminance),
                canonicalHybridRange = CloneRange(canonicalBaseline.metaAlbedo[3].meanLuminance),
                approvedBaseline = approvedBaseline,
            };
        }

        /// <summary>Serializes a reviewed artifact without importing or persisting it.</summary>
        /// <param name="candidate">The reviewed artifact to serialize.</param>
        /// <returns>The reviewed artifact JSON.</returns>
        public static string Serialize(PureBaseReviewedBaselineCandidate candidate) =>
            candidate == null
                ? throw new ArgumentNullException(nameof(candidate))
                : JsonUtility.ToJson(candidate, true);

        /// <summary>Deserializes a reviewed artifact from the exact bytes that will be source-identity checked.</summary>
        /// <param name="reviewedCandidateBytes">The exact reviewed artifact bytes.</param>
        /// <returns>The deserialized reviewed artifact.</returns>
        public static PureBaseReviewedBaselineCandidate Deserialize(byte[] reviewedCandidateBytes) =>
            DeserializeReviewedCandidate(DecodeArtifactBytes(reviewedCandidateBytes, "reviewed baseline"));

        /// <summary>Computes the lowercase SHA-256 digest for exact artifact bytes.</summary>
        /// <param name="artifactBytes">The exact artifact bytes to hash.</param>
        /// <returns>The lowercase hexadecimal SHA-256 digest.</returns>
        public static string ComputeSha256(byte[] artifactBytes)
        {
            if (artifactBytes == null)
                throw new ArgumentNullException(nameof(artifactBytes));

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(artifactBytes);
                var digest = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    digest.Append(value.ToString("x2"));
                return digest.ToString();
            }
        }

        /// <summary>Validates source identity and canonical preservation before any write transaction can begin.</summary>
        /// <param name="candidate">The reviewed artifact to validate.</param>
        /// <param name="observationCandidate">The raw observation represented by the source bytes.</param>
        /// <param name="canonicalBaseline">The current canonical baseline.</param>
        /// <param name="rawObservationBytes">The exact raw observation bytes used to bind the artifact.</param>
        public static void Validate(
            PureBaseReviewedBaselineCandidate candidate,
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate,
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawObservationBytes
        )
        {
            if (candidate == null)
                throw new InvalidOperationException("The reviewed baseline candidate is missing.");
            if (candidate.schemaVersion != SchemaVersion)
                throw new InvalidOperationException("The reviewed baseline candidate schema version is unsupported.");
            if (!IsLowercaseSha256(candidate.sourceObservationSha256))
                throw new InvalidOperationException("The reviewed baseline candidate source hash is invalid.");
            if (rawObservationBytes == null)
                throw new ArgumentNullException(nameof(rawObservationBytes));
            if (!string.Equals(
                    candidate.sourceObservationSha256,
                    ComputeSha256(rawObservationBytes),
                    StringComparison.Ordinal
                ))
            {
                throw new InvalidOperationException(
                    "The reviewed baseline candidate source hash does not match the raw observation bytes."
                );
            }

            ValidateObservationAndCanonical(
                new PureBaseRegressionBaselineGenerator.UnityEnvironment(),
                observationCandidate,
                canonicalBaseline
            );
            ValidateBaseline(candidate.approvedBaseline, "The reviewed baseline candidate approved baseline");
            EnsureCanonicalTargetRangesMatch(
                candidate,
                canonicalBaseline
            );
            EnsureApprovedBaselinePreservesCanonical(
                candidate.approvedBaseline,
                canonicalBaseline,
                observationCandidate.observation
            );
        }

        /// <summary>Applies an already validated reviewed baseline through the existing audited write boundary.</summary>
        /// <param name="candidate">The reviewed artifact to apply.</param>
        /// <param name="observationCandidate">The raw observation represented by the source bytes.</param>
        /// <param name="canonicalBaseline">The current canonical baseline.</param>
        /// <param name="rawObservationBytes">The exact raw observation bytes used to bind the artifact.</param>
        /// <param name="writer">The sole reviewed baseline writer.</param>
        /// <param name="writeBoundary">The audited transaction boundary.</param>
        internal static void Apply(
            PureBaseReviewedBaselineCandidate candidate,
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate,
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawObservationBytes,
            PureBaseRegressionBaselineGenerator.IReviewedCandidateWriter writer,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));

            Validate(candidate, observationCandidate, canonicalBaseline, rawObservationBytes);
            writeBoundary.BeginTransaction();
            try
            {
                writer.WriteExactBaseline(candidate.approvedBaseline, writeBoundary);
                writeBoundary.VerifyNoUnrelatedChanges();
            }
            finally
            {
                writeBoundary.VerifyNoUnrelatedChanges();
            }
        }

        /// <summary>Applies a reviewed Meta baseline while preserving all non-target canonical source bytes.</summary>
        /// <param name="candidate">The reviewed artifact to apply.</param>
        /// <param name="observationCandidate">The raw observation represented by the source bytes.</param>
        /// <param name="canonicalBaseline">The parsed current canonical baseline.</param>
        /// <param name="rawCanonicalBaselineBytes">The exact current canonical baseline UTF-8 bytes.</param>
        /// <param name="rawObservationBytes">The exact raw observation bytes used to bind the artifact.</param>
        /// <param name="writer">The lossless reviewed Meta writer.</param>
        /// <param name="writeBoundary">The audited transaction boundary.</param>
        internal static void ApplyLosslessly(
            PureBaseReviewedBaselineCandidate candidate,
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate,
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawCanonicalBaselineBytes,
            byte[] rawObservationBytes,
            ILosslessReviewedCandidateWriter writer,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            if (rawCanonicalBaselineBytes == null)
                throw new ArgumentNullException(nameof(rawCanonicalBaselineBytes));
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));

            Validate(candidate, observationCandidate, canonicalBaseline, rawObservationBytes);
            byte[] reviewedCanonicalBaselineBytes =
                PureBaseRegressionBaselineStorage.CreateLosslessReviewedBaselineBytes(
                    canonicalBaseline,
                    rawCanonicalBaselineBytes,
                    candidate.approvedBaseline
                );
            writeBoundary.BeginTransaction();
            try
            {
                writer.WriteLosslessReviewedBaseline(
                    rawCanonicalBaselineBytes,
                    reviewedCanonicalBaselineBytes,
                    writeBoundary
                );
                writeBoundary.VerifyNoUnrelatedChanges();
            }
            finally
            {
                writeBoundary.VerifyNoUnrelatedChanges();
            }
        }

        /// <summary>Reads each source artifact once before validating and applying a reviewed baseline.</summary>
        /// <param name="reader">The external artifact reader.</param>
        /// <param name="observationCandidatePath">The raw observation artifact path.</param>
        /// <param name="reviewedCandidatePath">The reviewed artifact path.</param>
        /// <param name="canonicalBaseline">The current canonical baseline.</param>
        /// <param name="writer">The sole reviewed baseline writer.</param>
        /// <param name="writeBoundary">The audited transaction boundary.</param>
        internal static void ApplyFromArtifacts(
            IArtifactReader reader,
            string observationCandidatePath,
            string reviewedCandidatePath,
            SceneRegressionBaseline canonicalBaseline,
            PureBaseRegressionBaselineGenerator.IReviewedCandidateWriter writer,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            byte[] observationBytes = reader.ReadAllBytes(observationCandidatePath);
            byte[] reviewedBytes = reader.ReadAllBytes(reviewedCandidatePath);
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate =
                DeserializeObservationCandidate(observationBytes);
            PureBaseReviewedBaselineCandidate reviewedCandidate = Deserialize(reviewedBytes);
            Apply(
                reviewedCandidate,
                observationCandidate,
                canonicalBaseline,
                observationBytes,
                writer,
                writeBoundary
            );
        }

        /// <summary>Reads each reviewed artifact once before applying it through a byte-preserving canonical writer.</summary>
        /// <param name="reader">The external artifact reader.</param>
        /// <param name="observationCandidatePath">The raw observation artifact path.</param>
        /// <param name="reviewedCandidatePath">The reviewed artifact path.</param>
        /// <param name="canonicalBaseline">The parsed current canonical baseline.</param>
        /// <param name="rawCanonicalBaselineBytes">The exact current canonical baseline UTF-8 bytes.</param>
        /// <param name="writer">The lossless reviewed Meta writer.</param>
        /// <param name="writeBoundary">The audited transaction boundary.</param>
        internal static void ApplyFromArtifactsLosslessly(
            IArtifactReader reader,
            string observationCandidatePath,
            string reviewedCandidatePath,
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawCanonicalBaselineBytes,
            ILosslessReviewedCandidateWriter writer,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            byte[] observationBytes = reader.ReadAllBytes(observationCandidatePath);
            byte[] reviewedBytes = reader.ReadAllBytes(reviewedCandidatePath);
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate =
                DeserializeObservationCandidate(observationBytes);
            PureBaseReviewedBaselineCandidate reviewedCandidate = Deserialize(reviewedBytes);
            ApplyLosslessly(
                reviewedCandidate,
                observationCandidate,
                canonicalBaseline,
                rawCanonicalBaselineBytes,
                observationBytes,
                writer,
                writeBoundary
            );
        }

        /// <summary>Reads one raw observation once, validates it, and writes one reviewed artifact only after all checks pass.</summary>
        /// <param name="reader">The external artifact reader.</param>
        /// <param name="observationCandidatePath">The raw observation artifact path.</param>
        /// <param name="reviewedCandidatePath">The reviewed artifact output path.</param>
        /// <param name="environment">The active editor environment.</param>
        /// <param name="canonicalBaseline">The current canonical baseline.</param>
        /// <param name="writer">The reviewed artifact output operation.</param>
        internal static void CreateFromArtifacts(
            IArtifactReader reader,
            string observationCandidatePath,
            string reviewedCandidatePath,
            PureBaseRegressionBaselineGenerator.IEnvironment environment,
            SceneRegressionBaseline canonicalBaseline,
            IArtifactWriter writer
        )
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            PureBaseRegressionBaselineGenerator.ValidateEnvironment(environment);
            byte[] observationBytes = reader.ReadAllBytes(observationCandidatePath);
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate =
                DeserializeObservationCandidate(observationBytes);
            PureBaseReviewedBaselineCandidate candidate = Create(
                environment,
                observationCandidate,
                canonicalBaseline,
                observationBytes
            );
            writer.WriteAllText(reviewedCandidatePath, Serialize(candidate));
        }

        /// <summary>Reads reviewed-artifact command-line arguments before creating the external reviewed artifact.</summary>
        /// <param name="reader">The external artifact reader.</param>
        /// <param name="arguments">The complete batch command-line argument sequence.</param>
        /// <param name="environment">The active editor environment.</param>
        /// <param name="canonicalBaseline">The current canonical baseline.</param>
        /// <param name="writer">The reviewed artifact output operation.</param>
        internal static void CreateFromCommandLine(
            IArtifactReader reader,
            string[] arguments,
            PureBaseRegressionBaselineGenerator.IEnvironment environment,
            SceneRegressionBaseline canonicalBaseline,
            IArtifactWriter writer
        )
        {
            string observationCandidatePath =
                PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(
                    arguments,
                    ObservationCandidatePathArgument
                );
            string reviewedCandidatePath =
                PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(
                    arguments,
                    ReviewedMetaBaselinePathArgument
                );
            CreateFromArtifacts(
                reader,
                observationCandidatePath,
                reviewedCandidatePath,
                environment,
                canonicalBaseline,
                writer
            );
        }

        /// <summary>Reads reviewed-artifact command-line arguments before validating and applying the external artifacts.</summary>
        /// <param name="reader">The external artifact reader.</param>
        /// <param name="arguments">The complete batch command-line argument sequence.</param>
        /// <param name="canonicalBaseline">The current canonical baseline.</param>
        /// <param name="writer">The sole reviewed baseline writer.</param>
        /// <param name="writeBoundary">The audited transaction boundary.</param>
        internal static void ApplyFromCommandLine(
            IArtifactReader reader,
            string[] arguments,
            SceneRegressionBaseline canonicalBaseline,
            PureBaseRegressionBaselineGenerator.IReviewedCandidateWriter writer,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            string observationCandidatePath =
                PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(
                    arguments,
                    ObservationCandidatePathArgument
                );
            string reviewedCandidatePath =
                PureBaseRegressionBaselineGenerator.GetRequiredExternalCandidatePath(
                    arguments,
                    ReviewedMetaBaselinePathArgument
                );
            ApplyFromArtifacts(
                reader,
                observationCandidatePath,
                reviewedCandidatePath,
                canonicalBaseline,
                writer,
                writeBoundary
            );
        }

        /// <summary>Defines one required Meta material and shader identity.</summary>
        private sealed class MetaIdentity
        {
            /// <summary>Initializes one required identity and whether it receives an observed value.</summary>
            /// <param name="materialName">The required material name.</param>
            /// <param name="shaderName">The required shader name.</param>
            /// <param name="isApprovedTarget">Whether the reviewed baseline replaces this observed value.</param>
            public MetaIdentity(string materialName, string shaderName, bool isApprovedTarget)
            {
                MaterialName = materialName;
                ShaderName = shaderName;
                IsApprovedTarget = isApprovedTarget;
            }

            /// <summary>Gets the required material name.</summary>
            public string MaterialName { get; }

            /// <summary>Gets the required shader name.</summary>
            public string ShaderName { get; }

            /// <summary>Gets whether the identity receives the observed exact range.</summary>
            public bool IsApprovedTarget { get; }
        }

        /// <summary>Defines the only accepted ordered Meta identities.</summary>
        private static readonly MetaIdentity[] ExpectedMetaIdentities =
        {
            new MetaIdentity("PureBaseValidationUnlit", "PureBase/Unlit", false),
            new MetaIdentity("PureBaseValidationToon", "PureBase/Toon", false),
            new MetaIdentity("PureBaseValidationPbr", "PureBase/PBR", true),
            new MetaIdentity("PureBaseValidationHybrid", "PureBase/Hybrid", true),
        };

        /// <summary>Decodes exact artifact bytes without rereading the external source.</summary>
        /// <param name="artifactBytes">The exact artifact bytes.</param>
        /// <param name="artifactLabel">The artifact type used in validation messages.</param>
        /// <returns>The decoded JSON text.</returns>
        private static string DecodeArtifactBytes(byte[] artifactBytes, string artifactLabel)
        {
            if (artifactBytes == null || artifactBytes.Length == 0)
                throw new InvalidOperationException($"The {artifactLabel} artifact is empty.");
            return Encoding.UTF8.GetString(artifactBytes);
        }

        /// <summary>Deserializes a reviewed artifact and rejects missing or incompatible schemas.</summary>
        /// <param name="json">The exact reviewed artifact JSON.</param>
        /// <returns>The parsed reviewed artifact.</returns>
        private static PureBaseReviewedBaselineCandidate DeserializeReviewedCandidate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("The reviewed baseline artifact is empty.");
            try
            {
                PureBaseReviewedBaselineCandidate candidate =
                    JsonUtility.FromJson<PureBaseReviewedBaselineCandidate>(json);
                if (candidate == null || candidate.schemaVersion != SchemaVersion)
                {
                    throw new InvalidOperationException(
                        $"The reviewed baseline artifact must use schema version {SchemaVersion}."
                    );
                }

                return candidate;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The reviewed baseline artifact JSON is malformed.",
                    exception
                );
            }
        }

        /// <summary>Deserializes a raw observation from the same bytes used for source hashing.</summary>
        /// <param name="observationBytes">The exact raw observation bytes.</param>
        /// <returns>The parsed observation candidate.</returns>
        private static PureBaseRegressionBaselineGenerator.ObservationCandidate DeserializeObservationCandidate(
            byte[] observationBytes
        )
        {
            try
            {
                return PureBaseRegressionBaselineGenerator.DeserializeObservationCandidate(
                    DecodeArtifactBytes(observationBytes, "raw observation")
                );
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("The raw observation artifact JSON is malformed.", exception);
            }
        }

        /// <summary>Validates the current editor, raw observation, canonical baseline, and expected identities.</summary>
        /// <param name="observationCandidate">The raw observation candidate.</param>
        /// <param name="canonicalBaseline">The current canonical baseline.</param>
        private static void ValidateObservationAndCanonical(
            PureBaseRegressionBaselineGenerator.IEnvironment environment,
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate,
            SceneRegressionBaseline canonicalBaseline
        )
        {
            PureBaseRegressionBaselineGenerator.ValidateEnvironment(environment);
            PureBaseRegressionBaselineGenerator.ValidateObservationCandidate(
                observationCandidate,
                environment
            );
            ValidateBaseline(canonicalBaseline, "The current canonical baseline");
            ValidateObservationIdentities(observationCandidate.observation.metaAlbedo);
            ValidateBaselineIdentities(canonicalBaseline.metaAlbedo);
            ValidateBaselineIdentities(observationCandidate.exactBaseline.metaAlbedo);
        }

        /// <summary>Ensures the current writable target ranges are exactly those reviewed at artifact creation.</summary>
        /// <param name="candidate">The reviewed artifact carrying the canonical target binding.</param>
        /// <param name="canonicalBaseline">The current canonical baseline.</param>
        private static void EnsureCanonicalTargetRangesMatch(
            PureBaseReviewedBaselineCandidate candidate,
            SceneRegressionBaseline canonicalBaseline
        )
        {
            if (!RangesEqual(candidate.canonicalPbrRange, canonicalBaseline.metaAlbedo[2].meanLuminance)
                || !RangesEqual(candidate.canonicalHybridRange, canonicalBaseline.metaAlbedo[3].meanLuminance))
            {
                throw new InvalidOperationException(
                    "The reviewed baseline candidate was created against different canonical PBR or Hybrid ranges."
                );
            }
        }

        /// <summary>Validates one complete baseline and normalizes assertion failures to reviewed-artifact failures.</summary>
        /// <param name="baseline">The baseline to validate.</param>
        /// <param name="baselineLabel">The validation label.</param>
        private static void ValidateBaseline(SceneRegressionBaseline baseline, string baselineLabel)
        {
            try
            {
                PureBaseValidationSceneRegressionTests.ValidateBaselineObservability(
                    baseline,
                    baselineLabel
                );
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException($"{baselineLabel} is invalid.", exception);
            }

            ValidateBaselineIdentities(baseline.metaAlbedo);
        }

        /// <summary>Rejects an observation whose Meta values are missing, reordered, duplicated, or unexpected.</summary>
        /// <param name="metaAlbedo">The ordered observed Meta values.</param>
        private static void ValidateObservationIdentities(MetaAlbedoObservation[] metaAlbedo)
        {
            if (metaAlbedo == null || metaAlbedo.Length != ExpectedMetaIdentities.Length)
                throw new InvalidOperationException("The raw observation Meta identities are incomplete.");

            for (int index = 0; index < ExpectedMetaIdentities.Length; index++)
            {
                MetaAlbedoObservation meta = metaAlbedo[index];
                MetaIdentity identity = ExpectedMetaIdentities[index];
                if (meta == null
                    || !string.Equals(meta.materialName, identity.MaterialName, StringComparison.Ordinal)
                    || !string.Equals(meta.shaderName, identity.ShaderName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The raw observation Meta identities are invalid.");
                }
            }
        }

        /// <summary>Rejects a baseline whose Meta ranges are missing, reordered, duplicated, or unexpected.</summary>
        /// <param name="metaAlbedo">The ordered baseline Meta ranges.</param>
        private static void ValidateBaselineIdentities(MetaAlbedoBaseline[] metaAlbedo)
        {
            if (metaAlbedo == null || metaAlbedo.Length != ExpectedMetaIdentities.Length)
                throw new InvalidOperationException("The baseline Meta identities are incomplete.");

            for (int index = 0; index < ExpectedMetaIdentities.Length; index++)
            {
                MetaAlbedoBaseline meta = metaAlbedo[index];
                MetaIdentity identity = ExpectedMetaIdentities[index];
                if (meta == null
                    || meta.meanLuminance == null
                    || !string.Equals(meta.materialName, identity.MaterialName, StringComparison.Ordinal)
                    || !string.Equals(meta.shaderName, identity.ShaderName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("The baseline Meta identities are invalid.");
                }
            }
        }

        /// <summary>Ensures every non-target field stays equal to canonical data and approved targets are exact observations.</summary>
        /// <param name="approved">The reviewed baseline.</param>
        /// <param name="canonical">The current canonical baseline.</param>
        /// <param name="observation">The raw observation evidence.</param>
        private static void EnsureApprovedBaselinePreservesCanonical(
            SceneRegressionBaseline approved,
            SceneRegressionBaseline canonical,
            SceneRegressionObservation observation
        )
        {
            if (approved.schemaVersion != canonical.schemaVersion
                || !string.Equals(approved.unityVersion, canonical.unityVersion, StringComparison.Ordinal)
                || !string.Equals(approved.graphicsDevice, canonical.graphicsDevice, StringComparison.Ordinal)
                || !string.Equals(approved.colorSpace, canonical.colorSpace, StringComparison.Ordinal)
                || !string.Equals(approved.renderPipeline, canonical.renderPipeline, StringComparison.Ordinal)
                || approved.renderSize != canonical.renderSize
                || approved.staticLightmapCount != canonical.staticLightmapCount
                || approved.staticRendererAssignmentCount != canonical.staticRendererAssignmentCount
                || !RangesEqual(approved.sceneVisiblePixelCount, canonical.sceneVisiblePixelCount)
                || !RangesEqual(approved.shadowChangedPixelCount, canonical.shadowChangedPixelCount)
                || approved.warmedVariantCount != canonical.warmedVariantCount
                || !string.Equals(approved.dynamicLightmapStatus, canonical.dynamicLightmapStatus, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The reviewed baseline candidate does not preserve the current canonical baseline."
                );
            }

            for (int index = 0; index < ExpectedMetaIdentities.Length; index++)
            {
                if (ExpectedMetaIdentities[index].IsApprovedTarget)
                {
                    if (!IsExactRange(
                            approved.metaAlbedo[index].meanLuminance,
                            observation.metaAlbedo[index].meanLuminance
                        ))
                    {
                        throw new InvalidOperationException(
                            "The reviewed baseline candidate does not match its approved Meta observation."
                        );
                    }
                }
                else if (!RangesEqual(
                    approved.metaAlbedo[index].meanLuminance,
                    canonical.metaAlbedo[index].meanLuminance
                ))
                {
                    throw new InvalidOperationException(
                        "The reviewed baseline candidate does not preserve canonical Meta ranges."
                    );
                }
            }
        }

        /// <summary>Creates a complete independent baseline copy.</summary>
        /// <param name="baseline">The baseline to clone.</param>
        /// <returns>The independent clone.</returns>
        private static SceneRegressionBaseline CloneBaseline(SceneRegressionBaseline baseline)
        {
            return new SceneRegressionBaseline
            {
                schemaVersion = baseline.schemaVersion,
                unityVersion = baseline.unityVersion,
                graphicsDevice = baseline.graphicsDevice,
                colorSpace = baseline.colorSpace,
                renderPipeline = baseline.renderPipeline,
                renderSize = baseline.renderSize,
                staticLightmapCount = baseline.staticLightmapCount,
                staticRendererAssignmentCount = baseline.staticRendererAssignmentCount,
                sceneVisiblePixelCount = CloneRange(baseline.sceneVisiblePixelCount),
                shadowChangedPixelCount = CloneRange(baseline.shadowChangedPixelCount),
                warmedVariantCount = baseline.warmedVariantCount,
                dynamicLightmapStatus = baseline.dynamicLightmapStatus,
                metaAlbedo = CloneMetaAlbedo(baseline.metaAlbedo),
            };
        }

        /// <summary>Creates an independent integer range copy.</summary>
        /// <param name="range">The range to clone.</param>
        /// <returns>The independent clone.</returns>
        private static IntRange CloneRange(IntRange range) =>
            new IntRange { minimum = range.minimum, maximum = range.maximum };

        /// <summary>Creates an independent floating-point range copy.</summary>
        /// <param name="range">The range to clone.</param>
        /// <returns>The independent clone.</returns>
        private static FloatRange CloneRange(FloatRange range) =>
            new FloatRange { minimum = range.minimum, maximum = range.maximum };

        /// <summary>Creates independent copies of every Meta baseline entry.</summary>
        /// <param name="metaAlbedo">The Meta entries to clone.</param>
        /// <returns>The independent Meta entries.</returns>
        private static MetaAlbedoBaseline[] CloneMetaAlbedo(MetaAlbedoBaseline[] metaAlbedo)
        {
            var copy = new MetaAlbedoBaseline[metaAlbedo.Length];
            for (int index = 0; index < metaAlbedo.Length; index++)
            {
                copy[index] = new MetaAlbedoBaseline
                {
                    materialName = metaAlbedo[index].materialName,
                    shaderName = metaAlbedo[index].shaderName,
                    meanLuminance = CloneRange(metaAlbedo[index].meanLuminance),
                };
            }

            return copy;
        }

        /// <summary>Compares two integer ranges exactly.</summary>
        /// <param name="left">The first range.</param>
        /// <param name="right">The second range.</param>
        /// <returns>Whether both bounds are equal.</returns>
        private static bool RangesEqual(IntRange left, IntRange right) =>
            left != null
            && right != null
            && left.minimum == right.minimum
            && left.maximum == right.maximum;

        /// <summary>Compares two floating-point ranges exactly.</summary>
        /// <param name="left">The first range.</param>
        /// <param name="right">The second range.</param>
        /// <returns>Whether both bounds are equal.</returns>
        private static bool RangesEqual(FloatRange left, FloatRange right) =>
            left != null
            && right != null
            && left.minimum == right.minimum
            && left.maximum == right.maximum;

        /// <summary>Checks whether a range is the exact observed value.</summary>
        /// <param name="range">The reviewed range.</param>
        /// <param name="value">The observed value.</param>
        /// <returns>Whether both bounds equal the observation.</returns>
        private static bool IsExactRange(FloatRange range, float value) =>
            range != null && range.minimum == value && range.maximum == value;

        /// <summary>Checks the required lowercase hexadecimal SHA-256 representation.</summary>
        /// <param name="digest">The candidate digest.</param>
        /// <returns>Whether the digest has exactly 64 lowercase hexadecimal characters.</returns>
        private static bool IsLowercaseSha256(string digest)
        {
            if (string.IsNullOrEmpty(digest) || digest.Length != 64)
                return false;

            for (int index = 0; index < digest.Length; index++)
            {
                char character = digest[index];
                if (!((character >= '0' && character <= '9')
                    || (character >= 'a' && character <= 'f')))
                    return false;
            }

            return true;
        }
    }
}