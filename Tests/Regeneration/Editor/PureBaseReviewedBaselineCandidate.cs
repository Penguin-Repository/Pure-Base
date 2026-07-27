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

// Defines the reviewed-baseline artifact surface without enabling baseline mutation.

using System;
using PureBase.Tests.Daily;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Defines the versioned reviewed artifact that binds an approved baseline to raw observation bytes.</summary>
    [Serializable]
    public sealed class PureBaseReviewedBaselineCandidate
    {
        /// <summary>Identifies the only supported reviewed-baseline artifact schema.</summary>
        public const int SchemaVersion = 2;

        /// <summary>Defines the deterministic exception message used while reviewed-baseline operations are unavailable.</summary>
        public const string UnimplementedOperationMessage =
            "Reviewed baseline candidate operations are intentionally unimplemented.";

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

        /// <summary>Creates a reviewed artifact from a raw observation, the current canonical baseline, and exact source bytes.</summary>
        /// <param name="observationCandidate">The validated raw observation candidate.</param>
        /// <param name="canonicalBaseline">The current canonical baseline whose unrelated ranges must be preserved.</param>
        /// <param name="rawObservationBytes">The exact serialized raw observation artifact bytes.</param>
        /// <returns>The reviewed artifact bound to the raw observation bytes.</returns>
        public static PureBaseReviewedBaselineCandidate Create(
            PureBaseRegressionBaselineGenerator.ObservationCandidate observationCandidate,
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawObservationBytes
        ) => throw CreateUnimplementedException();

        /// <summary>Serializes a reviewed artifact without importing or persisting it.</summary>
        /// <param name="candidate">The reviewed artifact to serialize.</param>
        /// <returns>The reviewed artifact JSON.</returns>
        public static string Serialize(PureBaseReviewedBaselineCandidate candidate) =>
            throw CreateUnimplementedException();

        /// <summary>Deserializes a reviewed artifact from the exact bytes that will be source-identity checked.</summary>
        /// <param name="reviewedCandidateBytes">The exact reviewed artifact bytes.</param>
        /// <returns>The deserialized reviewed artifact.</returns>
        public static PureBaseReviewedBaselineCandidate Deserialize(byte[] reviewedCandidateBytes) =>
            throw CreateUnimplementedException();

        /// <summary>Computes the lowercase SHA-256 digest for exact artifact bytes.</summary>
        /// <param name="artifactBytes">The exact artifact bytes to hash.</param>
        /// <returns>The lowercase hexadecimal SHA-256 digest.</returns>
        public static string ComputeSha256(byte[] artifactBytes) => throw CreateUnimplementedException();

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
        ) => throw CreateUnimplementedException();

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
        ) => throw CreateUnimplementedException();

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
        ) => throw CreateUnimplementedException();

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
        ) => throw CreateUnimplementedException();

        /// <summary>Creates the deterministic exception used by every unavailable reviewed-artifact operation.</summary>
        /// <returns>The deterministic unavailable-operation exception.</returns>
        private static NotSupportedException CreateUnimplementedException() =>
            new NotSupportedException(UnimplementedOperationMessage);
    }
}