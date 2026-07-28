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

// Persists the canonical baseline through independently audited Unity storage operations.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PureBase.Tests.Daily;
using UnityEditor;
using UnityEngine;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Defines raw canonical baseline storage operations without transaction auditing.</summary>
    internal interface ICanonicalBaselineStorageBackend
    {
        /// <summary>Determines whether the canonical baseline directory is already valid.</summary>
        /// <param name="assetPath">The canonical baseline directory AssetDatabase path.</param>
        /// <returns>Whether the directory is valid.</returns>
        bool IsDirectoryValid(string assetPath);

        /// <summary>Creates the canonical baseline directory below its parent AssetDatabase path.</summary>
        /// <param name="parentAssetPath">The canonical baseline directory parent AssetDatabase path.</param>
        /// <param name="directoryName">The canonical baseline directory name.</param>
        void CreateDirectory(string parentAssetPath, string directoryName);

        /// <summary>Writes serialized canonical baseline JSON to its known project-relative path.</summary>
        /// <param name="path">The canonical baseline file path.</param>
        /// <param name="contents">The already serialized baseline JSON.</param>
        void WriteAllText(string path, string contents);

        /// <summary>Synchronously imports the canonical baseline after its JSON write.</summary>
        /// <param name="path">The canonical baseline AssetDatabase path.</param>
        void ImportAsset(string path);
    }

    /// <summary>Defines an exclusive conditional raw-byte replacement for the canonical baseline.</summary>
    internal interface IConditionalRawCanonicalBaselineStorageBackend
    {
        /// <summary>Replaces canonical bytes only when their durable source still exactly matches the expected snapshot.</summary>
        /// <param name="path">The canonical baseline file path.</param>
        /// <param name="expectedContents">The exact bytes used for candidate validation.</param>
        /// <param name="replacementContents">The fully validated reviewed canonical bytes.</param>
        /// <returns>Whether the replacement was performed.</returns>
        bool TryWriteAllBytesIfCurrent(
            string path,
            byte[] expectedContents,
            byte[] replacementContents
        );
    }

    /// <summary>Implements raw canonical baseline storage through Unity editor APIs.</summary>
    internal sealed class UnityCanonicalBaselineStorageBackend
        : ICanonicalBaselineStorageBackend,
            IConditionalRawCanonicalBaselineStorageBackend
    {
        /// <inheritdoc />
        public bool IsDirectoryValid(string assetPath) => AssetDatabase.IsValidFolder(assetPath);

        /// <inheritdoc />
        public void CreateDirectory(string parentAssetPath, string directoryName) =>
            AssetDatabase.CreateFolder(parentAssetPath, directoryName);

        /// <inheritdoc />
        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

        /// <inheritdoc />
        public bool TryWriteAllBytesIfCurrent(
            string path,
            byte[] expectedContents,
            byte[] replacementContents
        )
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (expectedContents == null)
                throw new ArgumentNullException(nameof(expectedContents));
            if (replacementContents == null)
                throw new ArgumentNullException(nameof(replacementContents));

            try
            {
                using (
                    var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.None
                    )
                )
                {
                    stream.Lock(0, long.MaxValue);
                    try
                    {
                        if (!CurrentBytesMatch(stream, expectedContents))
                            return false;

                        stream.Position = 0;
                        stream.SetLength(0);
                        stream.Write(replacementContents, 0, replacementContents.Length);
                        stream.Flush(true);
                        return true;
                    }
                    finally
                    {
                        stream.Unlock(0, long.MaxValue);
                    }
                }
            }
            catch (NotSupportedException exception)
            {
                throw new InvalidOperationException(
                    "Lossless reviewed baseline persistence requires an operating-system file lock.",
                    exception
                );
            }
        }

        /// <summary>Compares one exclusively locked source stream with its expected snapshot without allocating a full duplicate.</summary>
        /// <param name="stream">The locked canonical baseline stream.</param>
        /// <param name="expectedContents">The exact source snapshot bytes.</param>
        /// <returns>Whether the locked source matches the expected snapshot.</returns>
        private static bool CurrentBytesMatch(FileStream stream, byte[] expectedContents)
        {
            if (stream.Length != expectedContents.Length)
                return false;

            stream.Position = 0;
            var buffer = new byte[Math.Min(expectedContents.Length, 4096)];
            int offset = 0;
            while (offset < expectedContents.Length)
            {
                int count = Math.Min(buffer.Length, expectedContents.Length - offset);
                int read = stream.Read(buffer, 0, count);
                if (read == 0)
                    return false;

                for (int index = 0; index < read; index++)
                    if (buffer[index] != expectedContents[offset + index])
                        return false;
                offset += read;
            }

            return true;
        }

        /// <inheritdoc />
        public void ImportAsset(string path) =>
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    }

    /// <summary>Retains one parsed canonical baseline together with its exact UTF-8 source bytes.</summary>
    internal sealed class CanonicalBaselineSnapshot
    {
        /// <summary>Initializes one immutable canonical baseline snapshot.</summary>
        /// <param name="baseline">The parsed canonical baseline.</param>
        /// <param name="rawBytes">The exact canonical baseline bytes.</param>
        public CanonicalBaselineSnapshot(SceneRegressionBaseline baseline, byte[] rawBytes)
        {
            Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
            RawBytes = rawBytes ?? throw new ArgumentNullException(nameof(rawBytes));
        }

        /// <summary>Gets the parsed canonical baseline.</summary>
        public SceneRegressionBaseline Baseline { get; }

        /// <summary>Gets the exact canonical baseline UTF-8 bytes.</summary>
        public byte[] RawBytes { get; }
    }

    /// <summary>Writes canonical baseline storage operations through the shared fail-closed transaction audit.</summary>
    internal static class PureBaseRegressionBaselineStorage
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>Loads the canonical baseline once while retaining its exact bytes for a lossless reviewed apply.</summary>
        /// <returns>The parsed canonical baseline and its exact UTF-8 bytes.</returns>
        internal static CanonicalBaselineSnapshot ReadCanonicalBaselineSnapshot()
        {
            byte[] rawBytes = File.ReadAllBytes(
                PureBaseValidationSceneRegressionTests.BaselinePath
            );
            string baselineJson = DecodeCanonicalBaseline(rawBytes);
            try
            {
                SceneRegressionBaseline baseline = JsonUtility.FromJson<SceneRegressionBaseline>(
                    baselineJson
                );
                if (baseline == null)
                    throw new InvalidOperationException("The canonical baseline JSON is empty.");

                return new CanonicalBaselineSnapshot(baseline, rawBytes);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "The canonical baseline JSON is malformed.",
                    exception
                );
            }
        }

        /// <summary>Writes one canonical baseline with an audit after every mutable storage operation.</summary>
        /// <param name="baseline">The caller-validated canonical baseline to serialize.</param>
        /// <param name="writeBoundary">The transaction audit applied after each mutable storage operation.</param>
        /// <param name="backend">The raw Unity storage backend without audit capabilities.</param>
        internal static void WriteCanonicalBaseline(
            SceneRegressionBaseline baseline,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary,
            ICanonicalBaselineStorageBackend backend
        )
        {
            if (baseline == null)
                throw new ArgumentNullException(nameof(baseline));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));

            string baselinePath = PureBaseValidationSceneRegressionTests.BaselinePath;
            string baselineDirectory = Path.GetDirectoryName(baselinePath);
            if (string.IsNullOrEmpty(baselineDirectory))
                throw new InvalidOperationException(
                    "The canonical baseline path has no parent directory."
                );

            string baselineJson = JsonUtility.ToJson(baseline, true);
            if (!backend.IsDirectoryValid(baselineDirectory))
            {
                string parent = Path.GetDirectoryName(baselineDirectory);
                string folderName = Path.GetFileName(baselineDirectory);
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
                    throw new InvalidOperationException(
                        "The canonical baseline directory is invalid."
                    );

                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    writeBoundary,
                    () => backend.CreateDirectory(parent.Replace('\\', '/'), folderName)
                );
            }

            PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                writeBoundary,
                () => backend.WriteAllText(baselinePath, baselineJson)
            );
            PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                writeBoundary,
                () => backend.ImportAsset(baselinePath)
            );
        }

        /// <summary>Writes an approved reviewed baseline by replacing only validated PBR and Hybrid Meta numeric tokens.</summary>
        /// <param name="canonicalBaseline">The parsed baseline represented by <paramref name="rawCanonicalBytes"/>.</param>
        /// <param name="rawCanonicalBytes">The exact current canonical baseline UTF-8 bytes.</param>
        /// <param name="approvedBaseline">The reviewed baseline with approved exact PBR and Hybrid values.</param>
        /// <param name="writeBoundary">The transaction audit applied after each persistence operation.</param>
        /// <param name="backend">The raw storage backend used for the lossless write and import.</param>
        internal static void WriteReviewedCanonicalBaselineLosslessly(
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawCanonicalBytes,
            SceneRegressionBaseline approvedBaseline,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary,
            ICanonicalBaselineStorageBackend backend
        )
        {
            if (canonicalBaseline == null)
                throw new ArgumentNullException(nameof(canonicalBaseline));
            if (rawCanonicalBytes == null)
                throw new ArgumentNullException(nameof(rawCanonicalBytes));
            if (approvedBaseline == null)
                throw new ArgumentNullException(nameof(approvedBaseline));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));
            if (!(backend is IConditionalRawCanonicalBaselineStorageBackend))
            {
                throw new InvalidOperationException(
                    "Lossless reviewed baseline persistence requires a conditional raw-byte storage backend."
                );
            }

            byte[] rewrittenBytes = CreateLosslessReviewedBaselineBytes(
                canonicalBaseline,
                rawCanonicalBytes,
                approvedBaseline
            );
            WriteReviewedCanonicalBaselineBytesIfCurrent(
                rawCanonicalBytes,
                rewrittenBytes,
                writeBoundary,
                backend
            );
        }

        /// <summary>Writes already validated reviewed canonical baseline bytes through separately audited write and import operations.</summary>
        /// <param name="expectedCanonicalBaselineBytes">The exact canonical bytes used to validate the reviewed replacement.</param>
        /// <param name="reviewedCanonicalBaselineBytes">The fully validated reviewed canonical baseline bytes.</param>
        /// <param name="writeBoundary">The transaction audit applied after each persistence operation.</param>
        /// <param name="backend">The raw storage backend used for the lossless write and import.</param>
        internal static void WriteReviewedCanonicalBaselineBytesIfCurrent(
            byte[] expectedCanonicalBaselineBytes,
            byte[] reviewedCanonicalBaselineBytes,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary,
            ICanonicalBaselineStorageBackend backend
        )
        {
            if (expectedCanonicalBaselineBytes == null)
                throw new ArgumentNullException(nameof(expectedCanonicalBaselineBytes));
            if (reviewedCanonicalBaselineBytes == null)
                throw new ArgumentNullException(nameof(reviewedCanonicalBaselineBytes));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));
            if (backend == null)
                throw new ArgumentNullException(nameof(backend));
            if (!(backend is IConditionalRawCanonicalBaselineStorageBackend conditionalRawBackend))
            {
                throw new InvalidOperationException(
                    "Lossless reviewed baseline persistence requires a conditional raw-byte storage backend."
                );
            }

            string baselinePath = PureBaseValidationSceneRegressionTests.BaselinePath;
            PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                writeBoundary,
                () =>
                {
                    if (
                        !conditionalRawBackend.TryWriteAllBytesIfCurrent(
                            baselinePath,
                            expectedCanonicalBaselineBytes,
                            reviewedCanonicalBaselineBytes
                        )
                    )
                    {
                        throw new InvalidOperationException(
                            "The canonical baseline changed after reviewed candidate validation."
                        );
                    }
                }
            );
            PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                writeBoundary,
                () => backend.ImportAsset(baselinePath)
            );
        }

        /// <summary>Builds a byte-exact reviewed baseline replacement after validating only writable Meta numeric ranges.</summary>
        /// <param name="canonicalBaseline">The parsed baseline represented by <paramref name="rawCanonicalBytes"/>.</param>
        /// <param name="rawCanonicalBytes">The exact current canonical baseline UTF-8 bytes.</param>
        /// <param name="approvedBaseline">The reviewed baseline with approved exact PBR and Hybrid values.</param>
        /// <returns>Canonical bytes with exactly four numeric literal ranges replaced.</returns>
        internal static byte[] CreateLosslessReviewedBaselineBytes(
            SceneRegressionBaseline canonicalBaseline,
            byte[] rawCanonicalBytes,
            SceneRegressionBaseline approvedBaseline
        )
        {
            if (canonicalBaseline == null)
                throw new ArgumentNullException(nameof(canonicalBaseline));
            if (rawCanonicalBytes == null)
                throw new ArgumentNullException(nameof(rawCanonicalBytes));
            if (approvedBaseline == null)
                throw new ArgumentNullException(nameof(approvedBaseline));

            return new ReviewedMetaBaselineJsonRewriter(
                canonicalBaseline,
                rawCanonicalBytes,
                approvedBaseline
            ).Rewrite();
        }

        /// <summary>Builds the approved Unlit and Toon Meta range migration after validating the exact predecessor bytes.</summary>
        /// <param name="rawCanonicalBytes">The exact pre-migration canonical baseline UTF-8 bytes.</param>
        /// <returns>Canonical bytes with only the four approved Unlit and Toon range literals replaced.</returns>
        internal static byte[] CreateApprovedUnlitToonRangeMigrationBytes(byte[] rawCanonicalBytes)
        {
            if (rawCanonicalBytes == null)
                throw new ArgumentNullException(nameof(rawCanonicalBytes));

            return new ReviewedMetaBaselineJsonRewriter(
                rawCanonicalBytes
            ).RewriteApprovedUnlitToonRanges();
        }

        /// <summary>Decodes canonical UTF-8 bytes without accepting a byte-order mark or invalid replacement characters.</summary>
        /// <param name="rawBytes">The canonical baseline bytes.</param>
        /// <returns>The canonical JSON text.</returns>
        private static string DecodeCanonicalBaseline(byte[] rawBytes)
        {
            if (rawBytes == null || rawBytes.Length == 0)
                throw new InvalidOperationException("The canonical baseline is empty.");
            if (
                rawBytes.Length >= 3
                && rawBytes[0] == 0xEF
                && rawBytes[1] == 0xBB
                && rawBytes[2] == 0xBF
            )
            {
                throw new InvalidOperationException(
                    "The canonical baseline must not contain a UTF-8 byte-order mark."
                );
            }

            try
            {
                return StrictUtf8.GetString(rawBytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidOperationException(
                    "The canonical baseline is not valid UTF-8.",
                    exception
                );
            }
        }

        /// <summary>Parses canonical JSON and replaces only approved PBR and Hybrid Meta numeric token spans.</summary>
        private sealed class ReviewedMetaBaselineJsonRewriter
        {
            private static readonly string[] ExpectedMaterialNames =
            {
                "PureBaseValidationUnlit",
                "PureBaseValidationToon",
                "PureBaseValidationPbr",
                "PureBaseValidationHybrid",
            };

            private static readonly string[] ExpectedShaderNames =
            {
                "PureBase/Unlit",
                "PureBase/Toon",
                "PureBase/PBR",
                "PureBase/Hybrid",
            };

            private static readonly FixedMetaRangeMigration[] ApprovedUnlitToonMigrations =
            {
                new FixedMetaRangeMigration(
                    "0.04757445678114891",
                    "0.04757445678114891",
                    "0.04757252708077431",
                    "0.04757445678114891"
                ),
                new FixedMetaRangeMigration(
                    "0.08925552666187286",
                    "0.08925552666187286",
                    "0.08925478160381317",
                    "0.08925552666187286"
                ),
                null,
                null,
            };

            private readonly SceneRegressionBaseline canonicalBaseline;
            private readonly SceneRegressionBaseline approvedBaseline;
            private readonly string json;
            private readonly List<MetaRangeTokens> metaRanges = new List<MetaRangeTokens>();
            private int position;

            /// <summary>Initializes a parser over one immutable canonical JSON snapshot.</summary>
            public ReviewedMetaBaselineJsonRewriter(
                SceneRegressionBaseline canonicalBaseline,
                byte[] rawCanonicalBytes,
                SceneRegressionBaseline approvedBaseline
            )
            {
                this.canonicalBaseline = canonicalBaseline;
                this.approvedBaseline = approvedBaseline;
                json = DecodeCanonicalBaseline(rawCanonicalBytes);
            }

            /// <summary>Initializes the fixed Unlit and Toon range migration over one immutable canonical JSON snapshot.</summary>
            /// <param name="rawCanonicalBytes">The exact pre-migration canonical baseline UTF-8 bytes.</param>
            public ReviewedMetaBaselineJsonRewriter(byte[] rawCanonicalBytes)
            {
                json = DecodeCanonicalBaseline(rawCanonicalBytes);
            }

            /// <summary>Validates the full JSON document and returns a replacement with only approved numeric tokens changed.</summary>
            public byte[] Rewrite()
            {
                ParseRootObject();
                SkipWhitespace();
                if (position != json.Length)
                    Fail("The canonical baseline contains trailing JSON content.");
                if (metaRanges.Count != ExpectedMaterialNames.Length)
                    Fail("The canonical baseline Meta entries are incomplete.");

                var replacements = new List<NumberReplacement>(4);
                for (int index = 0; index < metaRanges.Count; index++)
                {
                    MetaRangeTokens range = metaRanges[index];
                    ValidateMetaIdentity(index, range);
                    if (index < 2)
                        continue;

                    ValidateCanonicalRange(index, range);
                    FloatRange approvedRange = approvedBaseline.metaAlbedo[index].meanLuminance;
                    if (
                        approvedRange == null
                        || !SameFloatBits(approvedRange.minimum, approvedRange.maximum)
                    )
                    {
                        Fail("The reviewed baseline target range must be exact.");
                    }

                    string approvedLiteral = SerializeFloatLiteral(approvedRange.minimum);
                    replacements.Add(new NumberReplacement(range.Minimum, approvedLiteral));
                    replacements.Add(new NumberReplacement(range.Maximum, approvedLiteral));
                }

                return StrictUtf8.GetBytes(ReplaceNumbers(replacements));
            }

            /// <summary>Validates and applies the explicitly approved Unlit and Toon range migration.</summary>
            /// <returns>Canonical bytes with only the approved Unlit and Toon numeric tokens replaced.</returns>
            public byte[] RewriteApprovedUnlitToonRanges()
            {
                ParseRootObject();
                SkipWhitespace();
                if (position != json.Length)
                    Fail("The canonical baseline contains trailing JSON content.");
                if (metaRanges.Count != ExpectedMaterialNames.Length)
                    Fail("The canonical baseline Meta entries are incomplete.");

                var replacements = new List<NumberReplacement>(4);
                for (int index = 0; index < metaRanges.Count; index++)
                {
                    MetaRangeTokens range = metaRanges[index];
                    ValidateMetaIdentity(index, range);
                    FixedMetaRangeMigration migration = ApprovedUnlitToonMigrations[index];
                    if (migration == null)
                        continue;

                    ValidateFixedMigrationSource(range, migration);
                    replacements.Add(new NumberReplacement(range.Minimum, migration.TargetMinimum));
                    replacements.Add(new NumberReplacement(range.Maximum, migration.TargetMaximum));
                }

                return StrictUtf8.GetBytes(ReplaceNumbers(replacements));
            }

            /// <summary>Parses the root object and discovers exactly one Meta array.</summary>
            private void ParseRootObject()
            {
                SkipWhitespace();
                Expect('{');
                bool foundMetaAlbedo = false;
                SkipWhitespace();
                if (TryConsume('}'))
                    Fail("The canonical baseline Meta entries are missing.");

                while (true)
                {
                    string propertyName = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    if (string.Equals(propertyName, "metaAlbedo", StringComparison.Ordinal))
                    {
                        if (foundMetaAlbedo)
                            Fail("The canonical baseline contains duplicate Meta arrays.");
                        foundMetaAlbedo = true;
                        ParseMetaAlbedoArray();
                    }
                    else
                    {
                        ParseValue(0);
                    }

                    SkipWhitespace();
                    if (TryConsume('}'))
                        break;
                    Expect(',');
                    SkipWhitespace();
                }

                if (!foundMetaAlbedo)
                    Fail("The canonical baseline Meta entries are missing.");
            }

            /// <summary>Parses the ordered canonical Meta array without normalizing its source text.</summary>
            private void ParseMetaAlbedoArray()
            {
                SkipWhitespace();
                Expect('[');
                SkipWhitespace();
                if (TryConsume(']'))
                    return;

                while (true)
                {
                    metaRanges.Add(ParseMetaEntry());
                    SkipWhitespace();
                    if (TryConsume(']'))
                        break;
                    Expect(',');
                    SkipWhitespace();
                }
            }

            /// <summary>Parses one Meta entry and locates its required numeric range token spans.</summary>
            private MetaRangeTokens ParseMetaEntry()
            {
                SkipWhitespace();
                Expect('{');
                string materialName = null;
                string shaderName = null;
                NumberTokens minimum = default;
                NumberTokens maximum = default;
                bool foundMaterialName = false;
                bool foundShaderName = false;
                bool foundMeanLuminance = false;
                SkipWhitespace();
                if (TryConsume('}'))
                    Fail("A canonical Meta entry is empty.");

                while (true)
                {
                    string propertyName = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    if (string.Equals(propertyName, "materialName", StringComparison.Ordinal))
                    {
                        if (foundMaterialName)
                            Fail("A canonical Meta entry contains duplicate material identities.");
                        materialName = ParseStringValue();
                        foundMaterialName = true;
                    }
                    else if (string.Equals(propertyName, "shaderName", StringComparison.Ordinal))
                    {
                        if (foundShaderName)
                            Fail("A canonical Meta entry contains duplicate shader identities.");
                        shaderName = ParseStringValue();
                        foundShaderName = true;
                    }
                    else if (string.Equals(propertyName, "meanLuminance", StringComparison.Ordinal))
                    {
                        if (foundMeanLuminance)
                            Fail("A canonical Meta entry contains duplicate luminance ranges.");
                        ParseMeanLuminanceRange(out minimum, out maximum);
                        foundMeanLuminance = true;
                    }
                    else
                    {
                        ParseValue(1);
                    }

                    SkipWhitespace();
                    if (TryConsume('}'))
                        break;
                    Expect(',');
                    SkipWhitespace();
                }

                if (!foundMaterialName || !foundShaderName || !foundMeanLuminance)
                    Fail("A canonical Meta entry is incomplete.");
                return new MetaRangeTokens(materialName, shaderName, minimum, maximum);
            }

            /// <summary>Parses one Meta luminance range and retains the original minimum and maximum numeric token spans.</summary>
            private void ParseMeanLuminanceRange(out NumberTokens minimum, out NumberTokens maximum)
            {
                minimum = default;
                maximum = default;
                SkipWhitespace();
                Expect('{');
                bool foundMinimum = false;
                bool foundMaximum = false;
                SkipWhitespace();
                if (TryConsume('}'))
                    Fail("A canonical Meta luminance range is empty.");

                while (true)
                {
                    string propertyName = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    if (string.Equals(propertyName, "minimum", StringComparison.Ordinal))
                    {
                        if (foundMinimum)
                            Fail(
                                "A canonical Meta luminance range contains duplicate minimum values."
                            );
                        minimum = ParseNumberTokens();
                        foundMinimum = true;
                    }
                    else if (string.Equals(propertyName, "maximum", StringComparison.Ordinal))
                    {
                        if (foundMaximum)
                            Fail(
                                "A canonical Meta luminance range contains duplicate maximum values."
                            );
                        maximum = ParseNumberTokens();
                        foundMaximum = true;
                    }
                    else
                    {
                        ParseValue(2);
                    }

                    SkipWhitespace();
                    if (TryConsume('}'))
                        break;
                    Expect(',');
                    SkipWhitespace();
                }

                if (!foundMinimum || !foundMaximum)
                    Fail("A canonical Meta luminance range is incomplete.");
            }

            /// <summary>Parses one JSON value solely to validate the full source document outside target spans.</summary>
            private void ParseValue(int depth)
            {
                if (depth > 64)
                    Fail("The canonical baseline JSON exceeds the supported nesting depth.");
                SkipWhitespace();
                if (position >= json.Length)
                    Fail("The canonical baseline JSON ends unexpectedly.");

                char token = json[position];
                if (token == '{')
                {
                    position++;
                    SkipWhitespace();
                    if (TryConsume('}'))
                        return;
                    while (true)
                    {
                        ParseString();
                        SkipWhitespace();
                        Expect(':');
                        ParseValue(depth + 1);
                        SkipWhitespace();
                        if (TryConsume('}'))
                            return;
                        Expect(',');
                        SkipWhitespace();
                    }
                }

                if (token == '[')
                {
                    position++;
                    SkipWhitespace();
                    if (TryConsume(']'))
                        return;
                    while (true)
                    {
                        ParseValue(depth + 1);
                        SkipWhitespace();
                        if (TryConsume(']'))
                            return;
                        Expect(',');
                        SkipWhitespace();
                    }
                }

                if (token == '"')
                {
                    ParseString();
                    return;
                }

                if (token == 't')
                {
                    ExpectLiteral("true");
                    return;
                }

                if (token == 'f')
                {
                    ExpectLiteral("false");
                    return;
                }

                if (token == 'n')
                {
                    ExpectLiteral("null");
                    return;
                }

                ParseNumberTokens();
            }

            /// <summary>Parses a JSON string and returns its decoded value for structural identity validation.</summary>
            private string ParseStringValue()
            {
                SkipWhitespace();
                return ParseString();
            }

            /// <summary>Parses a JSON string, including standard escape sequences.</summary>
            private string ParseString()
            {
                SkipWhitespace();
                Expect('"');
                var result = new StringBuilder();
                while (position < json.Length)
                {
                    char character = json[position++];
                    if (character == '"')
                        return result.ToString();
                    if (character < 0x20)
                        Fail("The canonical baseline contains an unescaped control character.");
                    if (character != '\\')
                    {
                        result.Append(character);
                        continue;
                    }

                    if (position >= json.Length)
                        Fail("The canonical baseline contains an incomplete string escape.");
                    char escape = json[position++];
                    if (escape == '"' || escape == '\\' || escape == '/')
                        result.Append(escape);
                    else if (escape == 'b')
                        result.Append('\b');
                    else if (escape == 'f')
                        result.Append('\f');
                    else if (escape == 'n')
                        result.Append('\n');
                    else if (escape == 'r')
                        result.Append('\r');
                    else if (escape == 't')
                        result.Append('\t');
                    else if (escape == 'u')
                    {
                        if (position + 4 > json.Length)
                            Fail("The canonical baseline contains an incomplete Unicode escape.");
                        string hexadecimal = json.Substring(position, 4);
                        if (
                            !ushort.TryParse(
                                hexadecimal,
                                NumberStyles.AllowHexSpecifier,
                                CultureInfo.InvariantCulture,
                                out ushort codeUnit
                            )
                        )
                        {
                            Fail("The canonical baseline contains an invalid Unicode escape.");
                        }

                        result.Append((char)codeUnit);
                        position += 4;
                    }
                    else
                    {
                        Fail("The canonical baseline contains an invalid string escape.");
                    }
                }

                Fail("The canonical baseline contains an unterminated string.");
                return null;
            }

            /// <summary>Parses one JSON number and retains its original source span.</summary>
            private NumberTokens ParseNumberTokens()
            {
                SkipWhitespace();
                int start = position;
                if (TryConsume('-'))
                {
                    if (position >= json.Length)
                        Fail("The canonical baseline contains an incomplete number.");
                }

                if (TryConsume('0')) { }
                else
                {
                    if (position >= json.Length || json[position] < '1' || json[position] > '9')
                        Fail("The canonical baseline contains an invalid number.");
                    position++;
                    while (position < json.Length && json[position] >= '0' && json[position] <= '9')
                        position++;
                }

                if (TryConsume('.'))
                {
                    if (position >= json.Length || json[position] < '0' || json[position] > '9')
                        Fail("The canonical baseline contains an invalid fractional number.");
                    while (position < json.Length && json[position] >= '0' && json[position] <= '9')
                        position++;
                }

                if (position < json.Length && (json[position] == 'e' || json[position] == 'E'))
                {
                    position++;
                    if (position < json.Length && (json[position] == '+' || json[position] == '-'))
                        position++;
                    if (position >= json.Length || json[position] < '0' || json[position] > '9')
                        Fail("The canonical baseline contains an invalid exponent.");
                    while (position < json.Length && json[position] >= '0' && json[position] <= '9')
                        position++;
                }

                return new NumberTokens(start, position - start);
            }

            /// <summary>Validates one ordered Meta identity before any replacement can be constructed.</summary>
            private static void ValidateMetaIdentity(int index, MetaRangeTokens range)
            {
                if (
                    !string.Equals(
                        range.MaterialName,
                        ExpectedMaterialNames[index],
                        StringComparison.Ordinal
                    )
                    || !string.Equals(
                        range.ShaderName,
                        ExpectedShaderNames[index],
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidOperationException(
                        "The canonical baseline Meta identities are invalid."
                    );
                }
            }

            /// <summary>Validates that raw target literal values still represent the current parsed canonical target ranges.</summary>
            private void ValidateCanonicalRange(int index, MetaRangeTokens range)
            {
                if (
                    canonicalBaseline.metaAlbedo == null
                    || canonicalBaseline.metaAlbedo.Length != ExpectedMaterialNames.Length
                    || canonicalBaseline.metaAlbedo[index] == null
                    || canonicalBaseline.metaAlbedo[index].meanLuminance == null
                    || approvedBaseline.metaAlbedo == null
                    || approvedBaseline.metaAlbedo.Length != ExpectedMaterialNames.Length
                    || approvedBaseline.metaAlbedo[index] == null
                )
                {
                    Fail("The reviewed or canonical baseline Meta entries are incomplete.");
                }

                FloatRange canonicalRange = canonicalBaseline.metaAlbedo[index].meanLuminance;
                if (
                    !TryParseFloat(range.Minimum, out float minimum)
                    || !TryParseFloat(range.Maximum, out float maximum)
                    || !SameFloatBits(minimum, canonicalRange.minimum)
                    || !SameFloatBits(maximum, canonicalRange.maximum)
                )
                {
                    Fail(
                        "The canonical baseline raw target ranges no longer match their parsed values."
                    );
                }
            }

            /// <summary>Validates that one approved migration still receives its exact human-reviewed predecessor literals.</summary>
            /// <param name="range">The parsed target range tokens.</param>
            /// <param name="migration">The approved fixed source and target literals.</param>
            private void ValidateFixedMigrationSource(
                MetaRangeTokens range,
                FixedMetaRangeMigration migration
            )
            {
                if (
                    !string.Equals(
                        json.Substring(range.Minimum.Start, range.Minimum.Length),
                        migration.SourceMinimum,
                        StringComparison.Ordinal
                    )
                    || !string.Equals(
                        json.Substring(range.Maximum.Start, range.Maximum.Length),
                        migration.SourceMaximum,
                        StringComparison.Ordinal
                    )
                )
                {
                    Fail(
                        "The canonical baseline no longer contains the approved Unlit or Toon migration source literals."
                    );
                }
            }

            /// <summary>Formats an approved float using the same Unity JSON numeric literal policy as reviewed artifacts.</summary>
            private static string SerializeFloatLiteral(float value)
            {
                string json = JsonUtility.ToJson(new FloatLiteral { value = value });
                const string Prefix = "{\"value\":";
                if (
                    !json.StartsWith(Prefix, StringComparison.Ordinal)
                    || !json.EndsWith("}", StringComparison.Ordinal)
                )
                {
                    throw new InvalidOperationException(
                        "Unity did not serialize an approved float literal as JSON."
                    );
                }

                return json.Substring(Prefix.Length, json.Length - Prefix.Length - 1);
            }

            /// <summary>Rebuilds source text by replacing the four validated number spans in source order.</summary>
            private string ReplaceNumbers(List<NumberReplacement> replacements)
            {
                replacements.Sort((left, right) => left.Tokens.Start.CompareTo(right.Tokens.Start));
                var result = new StringBuilder(json.Length);
                int copiedThrough = 0;
                foreach (NumberReplacement replacement in replacements)
                {
                    if (replacement.Tokens.Start < copiedThrough)
                        Fail("The reviewed baseline replacement ranges overlap.");
                    result.Append(json, copiedThrough, replacement.Tokens.Start - copiedThrough);
                    result.Append(replacement.Literal);
                    copiedThrough = replacement.Tokens.Start + replacement.Tokens.Length;
                }

                result.Append(json, copiedThrough, json.Length - copiedThrough);
                return result.ToString();
            }

            /// <summary>Parses one raw target number as an invariant single-precision value.</summary>
            private bool TryParseFloat(NumberTokens tokens, out float value) =>
                float.TryParse(
                    json.Substring(tokens.Start, tokens.Length),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value
                )
                && !float.IsNaN(value)
                && !float.IsInfinity(value);

            /// <summary>Compares floating-point values by IEEE-754 bits rather than rounded numeric equality.</summary>
            private static bool SameFloatBits(float left, float right) =>
                BitConverter.ToInt32(BitConverter.GetBytes(left), 0)
                == BitConverter.ToInt32(BitConverter.GetBytes(right), 0);

            /// <summary>Consumes one required literal.</summary>
            private void ExpectLiteral(string literal)
            {
                if (
                    position + literal.Length > json.Length
                    || !string.Equals(
                        json.Substring(position, literal.Length),
                        literal,
                        StringComparison.Ordinal
                    )
                )
                {
                    Fail("The canonical baseline contains an invalid JSON literal.");
                }

                position += literal.Length;
            }

            /// <summary>Consumes one required punctuation character after optional JSON whitespace.</summary>
            private void Expect(char expected)
            {
                SkipWhitespace();
                if (position >= json.Length || json[position] != expected)
                    Fail($"The canonical baseline JSON expected '{expected}'.");
                position++;
            }

            /// <summary>Consumes one punctuation character when it is present at the current position.</summary>
            private bool TryConsume(char expected)
            {
                if (position >= json.Length || json[position] != expected)
                    return false;
                position++;
                return true;
            }

            /// <summary>Skips only JSON-defined whitespace.</summary>
            private void SkipWhitespace()
            {
                while (position < json.Length)
                {
                    char character = json[position];
                    if (
                        character != ' '
                        && character != '\t'
                        && character != '\r'
                        && character != '\n'
                    )
                        return;
                    position++;
                }
            }

            /// <summary>Throws one normalized malformed canonical baseline error.</summary>
            private static void Fail(string message) =>
                throw new InvalidOperationException(message);

            /// <summary>Stores one source numeric token span.</summary>
            private readonly struct NumberTokens
            {
                /// <summary>Initializes one numeric token span.</summary>
                public NumberTokens(int start, int length)
                {
                    Start = start;
                    Length = length;
                }

                /// <summary>Gets the source-text start index.</summary>
                public int Start { get; }

                /// <summary>Gets the source-text length.</summary>
                public int Length { get; }
            }

            /// <summary>Stores the required identity and source number tokens for one Meta range.</summary>
            private readonly struct MetaRangeTokens
            {
                /// <summary>Initializes one Meta range source descriptor.</summary>
                public MetaRangeTokens(
                    string materialName,
                    string shaderName,
                    NumberTokens minimum,
                    NumberTokens maximum
                )
                {
                    MaterialName = materialName;
                    ShaderName = shaderName;
                    Minimum = minimum;
                    Maximum = maximum;
                }

                /// <summary>Gets the material identity.</summary>
                public string MaterialName { get; }

                /// <summary>Gets the shader identity.</summary>
                public string ShaderName { get; }

                /// <summary>Gets the minimum numeric token.</summary>
                public NumberTokens Minimum { get; }

                /// <summary>Gets the maximum numeric token.</summary>
                public NumberTokens Maximum { get; }
            }

            /// <summary>Stores one approved literal replacement for a validated source span.</summary>
            private readonly struct NumberReplacement
            {
                /// <summary>Initializes one number replacement.</summary>
                public NumberReplacement(NumberTokens tokens, string literal)
                {
                    Tokens = tokens;
                    Literal = literal;
                }

                /// <summary>Gets the source numeric token span.</summary>
                public NumberTokens Tokens { get; }

                /// <summary>Gets the approved JSON numeric literal.</summary>
                public string Literal { get; }
            }

            /// <summary>Defines the exact predecessor and replacement literals for one approved Meta range migration.</summary>
            private sealed class FixedMetaRangeMigration
            {
                /// <summary>Initializes one fixed source-verified Meta range migration.</summary>
                /// <param name="sourceMinimum">The exact approved predecessor minimum literal.</param>
                /// <param name="sourceMaximum">The exact approved predecessor maximum literal.</param>
                /// <param name="targetMinimum">The exact approved replacement minimum literal.</param>
                /// <param name="targetMaximum">The exact approved replacement maximum literal.</param>
                public FixedMetaRangeMigration(
                    string sourceMinimum,
                    string sourceMaximum,
                    string targetMinimum,
                    string targetMaximum
                )
                {
                    SourceMinimum = sourceMinimum;
                    SourceMaximum = sourceMaximum;
                    TargetMinimum = targetMinimum;
                    TargetMaximum = targetMaximum;
                }

                /// <summary>Gets the exact predecessor minimum literal.</summary>
                public string SourceMinimum { get; }

                /// <summary>Gets the exact predecessor maximum literal.</summary>
                public string SourceMaximum { get; }

                /// <summary>Gets the exact replacement minimum literal.</summary>
                public string TargetMinimum { get; }

                /// <summary>Gets the exact replacement maximum literal.</summary>
                public string TargetMaximum { get; }
            }

            /// <summary>Defines the single-field Unity JSON serialization shape used to format approved float literals.</summary>
            [Serializable]
            private sealed class FloatLiteral
            {
                /// <summary>Stores the float rendered as a JSON literal.</summary>
                public float value;
            }
        }
    }
}
