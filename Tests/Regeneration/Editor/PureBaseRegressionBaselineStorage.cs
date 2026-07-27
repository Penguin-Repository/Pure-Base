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
using System.IO;
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

    /// <summary>Implements raw canonical baseline storage through Unity editor APIs.</summary>
    internal sealed class UnityCanonicalBaselineStorageBackend : ICanonicalBaselineStorageBackend
    {
        /// <inheritdoc />
        public bool IsDirectoryValid(string assetPath) => AssetDatabase.IsValidFolder(assetPath);

        /// <inheritdoc />
        public void CreateDirectory(string parentAssetPath, string directoryName) =>
            AssetDatabase.CreateFolder(parentAssetPath, directoryName);

        /// <inheritdoc />
        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

        /// <inheritdoc />
        public void ImportAsset(string path) =>
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
    }

    /// <summary>Writes canonical baseline storage operations through the shared fail-closed transaction audit.</summary>
    internal static class PureBaseRegressionBaselineStorage
    {
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
                throw new InvalidOperationException("The canonical baseline path has no parent directory.");

            string baselineJson = JsonUtility.ToJson(baseline, true);
            if (!backend.IsDirectoryValid(baselineDirectory))
            {
                string parent = Path.GetDirectoryName(baselineDirectory);
                string folderName = Path.GetFileName(baselineDirectory);
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
                    throw new InvalidOperationException("The canonical baseline directory is invalid.");

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
    }
}