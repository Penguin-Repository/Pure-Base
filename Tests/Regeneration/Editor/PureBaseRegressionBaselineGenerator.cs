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

// Explicitly bakes the canonical BIRP fixture and writes its reviewed baseline only after environment validation.

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
    /// <summary>Explicitly regenerates canonical scene regression assets after fail-closed environment validation.</summary>
    public static class PureBaseRegressionBaselineGenerator
    {
        /// <summary>Identifies the sole directory metadata file required beside the canonical baseline JSON.</summary>
        private const string CanonicalBaselineDirectoryMetaPath =
            "Packages/jp.penguin.purebase/Tests/Baselines.meta";

        /// <summary>Identifies the Unity metadata sidecar required beside the canonical baseline JSON.</summary>
        private const string CanonicalBaselineSidecarMetaPath =
            "Packages/jp.penguin.purebase/Tests/Baselines/birp-d3d11-2022.3.22f1.json.meta";

        /// <summary>Identifies the versioned external observation-candidate JSON contract.</summary>
        internal const int ObservationCandidateSchemaVersion = 1;

        /// <summary>Identifies the required batch command argument for a read-only candidate output path.</summary>
        internal const string ObservationCandidatePathArgument =
            "-pureBaseObservationCandidatePath";

        /// <summary>Identifies the required batch command argument for a reviewed candidate input path.</summary>
        internal const string ReviewedCandidatePathArgument = "-pureBaseReviewedCandidatePath";

        /// <summary>Identifies the dynamic-lightmap limitation preserved in every reviewed candidate.</summary>
        internal const string DynamicLightmapLimitation = "NOT_DETERMINISTIC_IN_BATCH_EDITMODE";

        /// <summary>Lists every canonical path that explicit regeneration is allowed to write.</summary>
        public static readonly string[] WritableCanonicalTargets =
        {
            PureBaseValidationSceneRegressionTests.ScenePath,
            "Packages/jp.penguin.purebase/Tests/Fixtures/Lighting",
            "Packages/jp.penguin.purebase/Tests/Fixtures/Materials",
            "Packages/jp.penguin.purebase/Tests/Fixtures/Scenes",
            CanonicalBaselineDirectoryMetaPath,
            PureBaseValidationSceneRegressionTests.BaselinePath,
            CanonicalBaselineSidecarMetaPath,
        };

        /// <summary>Runs the explicit canonical fixture bake and writes an exact baseline for subsequent human range review.</summary>
        [MenuItem("PureBase/Tests/Regenerate Scene Baseline")]
        public static void Regenerate()
        {
            var writeBoundary = new UnityWriteBoundary();
            Regenerate(
                new UnityEnvironment(),
                new UnityRegenerationOperations(writeBoundary),
                writeBoundary
            );
        }

        /// <summary>Provides an explicit no-argument entry point for Unity batch-mode execution.</summary>
        public static void RegenerateForBatchMode()
        {
            Debug.Log("Pure-Base baseline regeneration batch entry started.");

            if (IsNoWriteBatchProbeRequested())
            {
                RunNoWriteBatchProbe();
                return;
            }

            try
            {
                Regenerate();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Pure-Base baseline regeneration batch entry failed: {exception.Message}"
                );
                throw;
            }
        }

        /// <summary>Captures one non-mutating external candidate for independent review before baseline replacement.</summary>
        public static void CaptureObservationForBatchMode()
        {
            try
            {
                string candidatePath = GetRequiredExternalCandidatePath(
                    Environment.GetCommandLineArgs(),
                    ObservationCandidatePathArgument
                );
                CaptureObservationCandidate(
                    new UnityEnvironment(),
                    candidatePath,
                    new UnityObservationCaptureOperations()
                );
                Debug.Log(
                    $"Pure-Base read-only observation candidate written to '{candidatePath}'."
                );
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Pure-Base read-only observation batch entry failed: {exception.Message}"
                );
                throw;
            }
        }

        /// <summary>Applies one independently reviewed external candidate without baking, recapturing, or widening ranges.</summary>
        public static void ApplyReviewedBaselineForBatchMode()
        {
            try
            {
                string candidatePath = GetRequiredExternalCandidatePath(
                    Environment.GetCommandLineArgs(),
                    ReviewedCandidatePathArgument
                );
                ObservationCandidate candidate = ReadObservationCandidate(candidatePath);
                var writeBoundary = new UnityWriteBoundary();
                ApplyReviewedCandidate(
                    new UnityEnvironment(),
                    candidate,
                    new UnityReviewedCandidateWriter(),
                    writeBoundary
                );
                Debug.Log(
                    $"Pure-Base reviewed baseline candidate from '{candidatePath}' was applied."
                );
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Pure-Base reviewed baseline apply batch entry failed: {exception.Message}"
                );
                throw;
            }
        }

        /// <summary>Creates a source-bound reviewed Meta baseline artifact from one raw observation and the current canonical baseline.</summary>
        public static void CreateReviewedMetaBaselineForBatchMode()
        {
            try
            {
                PureBaseReviewedBaselineCandidate.CreateFromCommandLine(
                    new FileArtifactReader(),
                    Environment.GetCommandLineArgs(),
                    new UnityEnvironment(),
                    PureBaseValidationSceneRegressionTests.LoadBaseline(),
                    new FileArtifactWriter()
                );
                Debug.Log(
                    "Pure-Base reviewed Meta baseline candidate was written."
                );
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Pure-Base reviewed Meta baseline create batch entry failed: {exception.Message}"
                );
                throw;
            }
        }

        /// <summary>Applies one source-bound reviewed Meta baseline after validating both external artifacts before transaction start.</summary>
        public static void ApplyReviewedMetaBaselineForBatchMode()
        {
            try
            {
                string observationCandidatePath = GetRequiredExternalCandidatePath(
                    Environment.GetCommandLineArgs(),
                    PureBaseReviewedBaselineCandidate.ObservationCandidatePathArgument
                );
                string reviewedMetaBaselinePath = GetRequiredExternalCandidatePath(
                    Environment.GetCommandLineArgs(),
                    PureBaseReviewedBaselineCandidate.ReviewedMetaBaselinePathArgument
                );
                var writeBoundary = new UnityWriteBoundary();
                PureBaseReviewedBaselineCandidate.ApplyFromArtifacts(
                    new FileArtifactReader(),
                    observationCandidatePath,
                    reviewedMetaBaselinePath,
                    PureBaseValidationSceneRegressionTests.LoadBaseline(),
                    new UnityReviewedCandidateWriter(),
                    writeBoundary
                );
                Debug.Log(
                    $"Pure-Base reviewed Meta baseline candidate from '{reviewedMetaBaselinePath}' was applied."
                );
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    $"Pure-Base reviewed Meta baseline apply batch entry failed: {exception.Message}"
                );
                throw;
            }
        }

        /// <summary>Captures one candidate through read-only test seams and writes it only to the already-validated external path.</summary>
        /// <param name="environment">The current editor environment.</param>
        /// <param name="candidatePath">The validated external candidate output path.</param>
        /// <param name="operations">The read-only canonical scene capture operation.</param>
        internal static void CaptureObservationCandidate(
            IEnvironment environment,
            string candidatePath,
            IObservationCaptureOperations operations
        )
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));
            candidatePath = ValidateExternalCandidatePath(candidatePath);
            ValidateEnvironment(environment);
            SceneRegressionObservation observation = operations.CaptureObservation();
            ObservationCandidate candidate = CreateObservationCandidate(environment, observation);
            File.WriteAllText(candidatePath, SerializeObservationCandidate(candidate));
        }

        /// <summary>Applies only the exact reviewed baseline carried by a validated candidate through the current transaction audit.</summary>
        /// <param name="environment">The current editor environment.</param>
        /// <param name="candidate">The externally reviewed candidate.</param>
        /// <param name="writer">The sole canonical baseline persistence operation.</param>
        /// <param name="writeBoundary">The transaction audit that preserves unrelated durable state.</param>
        internal static void ApplyReviewedCandidate(
            IEnvironment environment,
            ObservationCandidate candidate,
            IReviewedCandidateWriter writer,
            IWriteBoundary writeBoundary
        )
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));

            ValidateEnvironment(environment);
            ValidateObservationCandidate(candidate, environment);
            writeBoundary.BeginTransaction();
            try
            {
                writer.WriteExactBaseline(candidate.exactBaseline, writeBoundary);
                writeBoundary.VerifyNoUnrelatedChanges();
            }
            finally
            {
                writeBoundary.VerifyNoUnrelatedChanges();
            }
        }

        /// <summary>Creates the external candidate schema from one read-only observation and its exact baseline representation.</summary>
        /// <param name="environment">The environment that produced the observation.</param>
        /// <param name="observation">The read-only Daily observation.</param>
        /// <returns>The validated candidate artifact.</returns>
        internal static ObservationCandidate CreateObservationCandidate(
            IEnvironment environment,
            SceneRegressionObservation observation
        )
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            ValidateEnvironment(environment);
            var candidate = new ObservationCandidate
            {
                schemaVersion = ObservationCandidateSchemaVersion,
                unityVersion = environment.UnityVersion,
                graphicsDevice = environment.GraphicsDeviceType.ToString(),
                colorSpace = environment.ColorSpace.ToString(),
                renderPipeline = "BuiltIn",
                observation = observation,
                exactBaseline = PureBaseValidationSceneRegressionTests.CreateExactBaseline(
                    observation
                ),
            };
            ValidateObservationCandidate(candidate, environment);
            return candidate;
        }

        /// <summary>Serializes one validated external candidate without importing it into Unity.</summary>
        /// <param name="candidate">The candidate to serialize.</param>
        /// <returns>The versioned JSON representation.</returns>
        internal static string SerializeObservationCandidate(ObservationCandidate candidate)
        {
            if (candidate == null)
                throw new ArgumentNullException(nameof(candidate));
            return JsonUtility.ToJson(candidate, true);
        }

        /// <summary>Reads and schema-validates an external candidate without importing or changing any Unity asset.</summary>
        /// <param name="candidatePath">The external candidate JSON path.</param>
        /// <returns>The deserialized candidate.</returns>
        internal static ObservationCandidate ReadObservationCandidate(string candidatePath)
        {
            candidatePath = ValidateExternalCandidatePath(candidatePath);
            if (!File.Exists(candidatePath))
                throw new InvalidOperationException(
                    $"The reviewed observation candidate '{candidatePath}' is missing."
                );
            return DeserializeObservationCandidate(File.ReadAllText(candidatePath));
        }

        /// <summary>Deserializes one external candidate and rejects missing or incompatible schema data.</summary>
        /// <param name="json">The candidate JSON content.</param>
        /// <returns>The parsed candidate.</returns>
        internal static ObservationCandidate DeserializeObservationCandidate(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("The reviewed observation candidate is empty.");
            ObservationCandidate candidate = JsonUtility.FromJson<ObservationCandidate>(json);
            if (candidate == null || candidate.schemaVersion != ObservationCandidateSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"The reviewed observation candidate must use schema version {ObservationCandidateSchemaVersion}."
                );
            }

            return candidate;
        }

        /// <summary>Validates one candidate against the active environment and exact baseline representation before any write can begin.</summary>
        /// <param name="candidate">The candidate to validate.</param>
        /// <param name="environment">The active environment.</param>
        internal static void ValidateObservationCandidate(
            ObservationCandidate candidate,
            IEnvironment environment
        )
        {
            if (candidate == null)
                throw new InvalidOperationException(
                    "The reviewed observation candidate is missing."
                );
            if (candidate.schemaVersion != ObservationCandidateSchemaVersion)
                throw new InvalidOperationException(
                    "The reviewed observation candidate schema version is unsupported."
                );
            if (
                !string.Equals(
                    candidate.unityVersion,
                    environment.UnityVersion,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    candidate.graphicsDevice,
                    environment.GraphicsDeviceType.ToString(),
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    candidate.colorSpace,
                    environment.ColorSpace.ToString(),
                    StringComparison.Ordinal
                )
                || !string.Equals(candidate.renderPipeline, "BuiltIn", StringComparison.Ordinal)
            )
            {
                throw new InvalidOperationException(
                    "The reviewed observation candidate environment does not match the active BIRP D3D11 Linear editor."
                );
            }

            ValidateCandidateDiagnostics(candidate.observation);
            try
            {
                PureBaseValidationSceneRegressionTests.ValidateBaselineObservability(
                    candidate.exactBaseline,
                    "Reviewed observation candidate baseline"
                );
            }
            catch (AssertionException exception)
            {
                throw new InvalidOperationException(
                    "The reviewed observation candidate baseline is not observable.",
                    exception
                );
            }
            EnsureExactBaselineMatchesObservation(candidate.exactBaseline, candidate.observation);
        }

        /// <summary>Rejects candidates that omit canonical read-only evidence or the dynamic-lightmap limitation.</summary>
        /// <param name="observation">The candidate observation to inspect.</param>
        private static void ValidateCandidateDiagnostics(SceneRegressionObservation observation)
        {
            if (observation == null)
                throw new InvalidOperationException(
                    "The reviewed observation candidate has no observation."
                );
            if (
                observation.staticLightmapCount != 2
                || observation.staticRendererAssignmentCount != 20
                || observation.sceneFinitePixelCount <= 0
                || observation.sceneVisiblePixelCount <= 0
                || observation.shadowCoveragePixelCount <= 0
                || observation.warmedVariantCount != 56
                || !IsFiniteUnitInterval(observation.sceneVisibleCoverage)
                || !IsFiniteUnitInterval(observation.sceneVisibleCentroidX)
                || !IsFiniteUnitInterval(observation.sceneVisibleCentroidY)
                || !IsFiniteUnitInterval(observation.shadowCoverage)
                || !IsFiniteUnitInterval(observation.shadowCentroidX)
                || !IsFiniteUnitInterval(observation.shadowCentroidY)
                || !IsFiniteNonNegative(observation.shadowMaxAbsoluteRgbDelta)
                || !string.Equals(
                    observation.dynamicLightmapStatus,
                    DynamicLightmapLimitation,
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidOperationException(
                    "The reviewed observation candidate is missing required read-only scene, shadow, variant, or dynamic-lightmap evidence."
                );
            }
        }

        /// <summary>Confirms the candidate carries exact values rather than a newly calculated or widened baseline.</summary>
        /// <param name="baseline">The exact baseline to write.</param>
        /// <param name="observation">The reviewed read-only observation.</param>
        private static void EnsureExactBaselineMatchesObservation(
            SceneRegressionBaseline baseline,
            SceneRegressionObservation observation
        )
        {
            if (
                baseline.schemaVersion
                    != PureBaseValidationSceneRegressionTests.BaselineSchemaVersion
                || !string.Equals(
                    baseline.unityVersion,
                    PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    baseline.graphicsDevice,
                    GraphicsDeviceType.Direct3D11.ToString(),
                    StringComparison.Ordinal
                )
                || !string.Equals(
                    baseline.colorSpace,
                    ColorSpace.Linear.ToString(),
                    StringComparison.Ordinal
                )
                || !string.Equals(baseline.renderPipeline, "BuiltIn", StringComparison.Ordinal)
                || baseline.renderSize != PureBaseValidationSceneRegressionTests.RenderSize
                || baseline.staticLightmapCount != observation.staticLightmapCount
                || baseline.staticRendererAssignmentCount
                    != observation.staticRendererAssignmentCount
                || baseline.sceneVisiblePixelCount == null
                || baseline.sceneVisiblePixelCount.minimum != observation.sceneVisiblePixelCount
                || baseline.sceneVisiblePixelCount.maximum != observation.sceneVisiblePixelCount
                // A freshly regenerated baseline must remain exact until a reviewer
                // explicitly approves a renderer-specific tolerance range.
                || baseline.shadowChangedPixelCount == null
                || baseline.shadowChangedPixelCount.minimum
                    != observation.shadowChangedPixelCount
                || baseline.shadowChangedPixelCount.maximum
                    != observation.shadowChangedPixelCount
                || baseline.warmedVariantCount != observation.warmedVariantCount
                || !string.Equals(
                    baseline.dynamicLightmapStatus,
                    observation.dynamicLightmapStatus,
                    StringComparison.Ordinal
                )
                || baseline.metaAlbedo == null
                || observation.metaAlbedo == null
                || baseline.metaAlbedo.Length != observation.metaAlbedo.Length
            )
            {
                throw new InvalidOperationException(
                    "The reviewed observation candidate baseline does not exactly match its observation."
                );
            }

            for (int index = 0; index < observation.metaAlbedo.Length; index++)
            {
                MetaAlbedoObservation observedMeta = observation.metaAlbedo[index];
                MetaAlbedoBaseline baselineMeta = baseline.metaAlbedo[index];
                if (
                    observedMeta == null
                    || baselineMeta == null
                    || baselineMeta.meanLuminance == null
                    || !string.Equals(
                        baselineMeta.materialName,
                        observedMeta.materialName,
                        StringComparison.Ordinal
                    )
                    || !string.Equals(
                        baselineMeta.shaderName,
                        observedMeta.shaderName,
                        StringComparison.Ordinal
                    )
                    || baselineMeta.meanLuminance.minimum != observedMeta.meanLuminance
                    || baselineMeta.meanLuminance.maximum != observedMeta.meanLuminance
                )
                {
                    throw new InvalidOperationException(
                        "The reviewed observation candidate Meta baseline does not exactly match its observation."
                    );
                }
            }
        }

        /// <summary>Reads one required command-line value and rejects missing, duplicate, or empty values.</summary>
        /// <param name="arguments">The complete batch command-line argument sequence.</param>
        /// <param name="argumentName">The required candidate path argument name.</param>
        /// <returns>The validated external candidate path.</returns>
        internal static string GetRequiredExternalCandidatePath(
            string[] arguments,
            string argumentName
        )
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            string candidatePath = null;
            int index = 0;
            while (index < arguments.Length)
            {
                if (!string.Equals(arguments[index], argumentName, StringComparison.Ordinal))
                {
                    index++;
                    continue;
                }
                if (
                    candidatePath != null
                    || index + 1 >= arguments.Length
                    || string.IsNullOrWhiteSpace(arguments[index + 1])
                )
                {
                    throw new InvalidOperationException(
                        $"Batch entry requires one non-empty '{argumentName}' path argument."
                    );
                }

                candidatePath = arguments[index + 1];
                index += 2;
            }

            if (candidatePath == null)
                throw new InvalidOperationException(
                    $"Batch entry requires '{argumentName} <absolute-external-path>'."
                );
            return ValidateExternalCandidatePath(candidatePath);
        }

        /// <summary>Rejects paths that are relative or inside the package or Unity Assets import scope before scene mutation.</summary>
        /// <param name="candidatePath">The candidate path supplied by a batch caller.</param>
        /// <returns>The normalized absolute external path.</returns>
        internal static string ValidateExternalCandidatePath(string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || !Path.IsPathRooted(candidatePath))
            {
                throw new InvalidOperationException(
                    "Observation candidate paths must be absolute."
                );
            }

            string fullPath = Path.GetFullPath(candidatePath);
            DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
            if (projectRoot == null)
                throw new InvalidOperationException(
                    "The Unity project root is unavailable for candidate path validation."
                );
            string packagePath = Path.Combine(
                projectRoot.FullName,
                "Packages",
                "jp.penguin.purebase"
            );
            if (IsPathWithin(fullPath, packagePath) || IsPathWithin(fullPath, Application.dataPath))
            {
                throw new InvalidOperationException(
                    "Observation candidate paths must be external to the package and Assets import scope."
                );
            }

            return fullPath;
        }

        /// <summary>Determines whether a normalized path is the specified root or one of its descendants.</summary>
        /// <param name="path">The normalized path to inspect.</param>
        /// <param name="root">The normalized root path.</param>
        /// <returns><see langword="true"/> when the path belongs to the root.</returns>
        private static bool IsPathWithin(string path, string root)
        {
            path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            root = root.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            string normalizedRoot =
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            return string.Equals(
                    path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    normalizedRoot.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase
                ) || path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Determines whether a value is finite and lies in the inclusive unit interval.</summary>
        private static bool IsFiniteUnitInterval(float value)
        {
            return !float.IsNaN(value)
                && !float.IsInfinity(value)
                && value >= 0.0f
                && value <= 1.0f;
        }

        /// <summary>Determines whether a value is finite and strictly positive.</summary>
        private static bool IsFinitePositive(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.0f;
        }

        /// <summary>Determines whether a value is finite and nonnegative.</summary>
        private static bool IsFiniteNonNegative(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0.0f;
        }

        /// <summary>Determines whether the batch command requests the non-writing guarded-entry probe.</summary>
        /// <returns><see langword="true"/> when the command line includes the probe argument.</returns>
        private static bool IsNoWriteBatchProbeRequested()
        {
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (string.Equals(argument, "-pureBaseRegenerationProbe", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>Exercises the normal environment and dirty-target audit without allowing fixture or baseline writes.</summary>
        private static void RunNoWriteBatchProbe()
        {
            var operations = new BatchProbeOperations();
            Regenerate(new UnityEnvironment(), operations, new UnityWriteBoundary());

            if (
                operations.GenerateFixtureCallCount != 1
                || operations.BakeAndWriteBaselineCallCount != 1
            )
            {
                throw new InvalidOperationException(
                    "Baseline regeneration batch probe did not reach each guarded non-writing operation exactly once."
                );
            }

            Debug.Log(
                "Pure-Base baseline regeneration batch probe completed after dirty-target audits without writes."
            );
        }

        /// <summary>Runs regeneration through testable environment and write seams.</summary>
        /// <param name="environment">The current editor environment.</param>
        /// <param name="operations">The operations permitted after environment validation.</param>
        internal static void Regenerate(
            IEnvironment environment,
            IRegenerationOperations operations
        )
        {
            Regenerate(environment, operations, new UnityWriteBoundary());
        }

        /// <summary>Runs regeneration through testable environment, write-boundary, and write-operation seams.</summary>
        /// <param name="environment">The current editor environment.</param>
        /// <param name="operations">The operations permitted after environment validation.</param>
        /// <param name="writeBoundary">The transaction audit that preserves unrelated durable state.</param>
        internal static void Regenerate(
            IEnvironment environment,
            IRegenerationOperations operations,
            IWriteBoundary writeBoundary
        )
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));

            ValidateEnvironment(environment);
            writeBoundary.BeginTransaction();
            try
            {
                operations.GenerateFixture();
                writeBoundary.VerifyNoUnrelatedChanges();
                operations.BakeAndWriteBaseline();
                writeBoundary.VerifyNoUnrelatedChanges();
            }
            finally
            {
                writeBoundary.VerifyNoUnrelatedChanges();
            }
        }

        /// <summary>Runs fixture generation only after the same environment and persistence checks used by full regeneration.</summary>
        /// <param name="environment">The current editor environment.</param>
        /// <param name="operations">The fixture operation permitted after validation.</param>
        /// <param name="writeBoundary">The transaction audit that preserves unrelated durable state.</param>
        internal static void GenerateFixture(
            IEnvironment environment,
            IFixtureGenerationOperations operations,
            IWriteBoundary writeBoundary
        )
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));

            ValidateEnvironment(environment);
            writeBoundary.BeginTransaction();
            try
            {
                operations.GenerateFixture();
                writeBoundary.VerifyNoUnrelatedChanges();
            }
            finally
            {
                writeBoundary.VerifyNoUnrelatedChanges();
            }
        }

        /// <summary>Rejects an unrelated dirty scene before a write-capable operation can start.</summary>
        internal static void EnsureNoDirtyNonCanonicalScenes()
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (scene.isLoaded && scene.isDirty && !IsCanonicalTarget(scene.path))
                {
                    throw new InvalidOperationException(
                        $"Baseline regeneration refuses to save unrelated dirty scene '{scene.path}'."
                    );
                }
            }
        }

        /// <summary>Determines whether an asset path is a non-canonical durable workspace target.</summary>
        /// <param name="assetPath">The AssetDatabase path of the dirty object.</param>
        /// <returns><see langword="true"/> when the target must be covered by the transaction audit.</returns>
        internal static bool IsNonCanonicalDurableWorkspaceAssetPath(string assetPath)
        {
            return IsDurableWorkspaceAssetPath(assetPath) && !IsCanonicalTarget(assetPath);
        }

        /// <summary>Determines whether a path belongs in the non-canonical durable filesystem inventory.</summary>
        /// <param name="assetPath">The project or package path to inspect.</param>
        /// <returns><see langword="true"/> when the path is durable, non-canonical, and not Git administration data.</returns>
        internal static bool IsNonCanonicalDurableInventoryAssetPath(string assetPath)
        {
            return IsNonCanonicalDurableWorkspaceAssetPath(assetPath)
                && !IsGitAdministrativePath(assetPath);
        }

        /// <summary>Determines whether an AssetDatabase path resolves to a durable project or embedded/local package source.</summary>
        /// <param name="assetPath">The AssetDatabase path to inspect.</param>
        /// <returns><see langword="true"/> when the path is a workspace asset that Unity can persist.</returns>
        internal static bool IsDurableWorkspaceAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            assetPath = NormalizeAssetPath(assetPath);
            if (assetPath.StartsWith("Assets/", StringComparison.Ordinal))
                return Directory.Exists(Application.dataPath);
            if (!assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                return false;

            PackageInfo packageInfo = PackageInfo.FindForAssetPath(assetPath);
            return packageInfo != null
                && assetPath.StartsWith(
                    "Packages/" + packageInfo.name + "/",
                    StringComparison.Ordinal
                )
                && IsDurablePackageSource(packageInfo.source, packageInfo.resolvedPath);
        }

        /// <summary>Determines whether a Package Manager source resolves to a durable local workspace directory.</summary>
        /// <param name="source">The Package Manager source kind.</param>
        /// <param name="resolvedPath">The package's physical root.</param>
        /// <returns><see langword="true"/> for embedded or local sources outside Unity caches.</returns>
        internal static bool IsDurablePackageSource(PackageSource source, string resolvedPath)
        {
            return (source == PackageSource.Embedded || source == PackageSource.Local)
                && !string.IsNullOrEmpty(resolvedPath)
                && Directory.Exists(resolvedPath)
                && !IsPackageCachePath(resolvedPath);
        }

        /// <summary>Determines whether a resolved package path belongs to a Unity package cache.</summary>
        /// <param name="resolvedPath">The physical package root.</param>
        /// <returns><see langword="true"/> when the root is a cache rather than a workspace source.</returns>
        private static bool IsPackageCachePath(string resolvedPath)
        {
            string normalizedPath = resolvedPath.Replace('\\', '/');
            return normalizedPath.IndexOf(
                    "/Library/PackageCache/",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0
                || normalizedPath.IndexOf(
                    "/Library/PackageManager/",
                    StringComparison.OrdinalIgnoreCase
                ) >= 0;
        }

        /// <summary>Determines whether a project-relative path addresses nested Git administrative data.</summary>
        /// <param name="assetPath">The project or package path to inspect.</param>
        /// <returns><see langword="true"/> when a path segment is exactly <c>.git</c>.</returns>
        private static bool IsGitAdministrativePath(string assetPath)
        {
            foreach (string segment in NormalizeAssetPath(assetPath).Split('/'))
            {
                if (string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Converts a project-relative path to Unity-style separators for deterministic comparison.</summary>
        /// <param name="assetPath">The path to normalize.</param>
        /// <returns>The path with forward-slash separators.</returns>
        private static string NormalizeAssetPath(string assetPath)
        {
            return assetPath.Replace('\\', '/');
        }

        /// <summary>Determines whether a persistent path belongs to the explicit regeneration allowlist.</summary>
        /// <param name="assetPath">The AssetDatabase path to inspect.</param>
        /// <returns><see langword="true"/> when the path is a canonical target.</returns>
        private static bool IsCanonicalTarget(string assetPath)
        {
            assetPath = NormalizeAssetPath(assetPath);
            if (IsExactCanonicalBaselineOutputPath(assetPath))
                return true;
            foreach (string target in WritableCanonicalTargets)
            {
                if (IsExactCanonicalBaselineOutputPath(target))
                    continue;
                if (
                    string.Equals(assetPath, target, StringComparison.Ordinal)
                    || assetPath.StartsWith(target + "/", StringComparison.Ordinal)
                )
                    return true;
            }

            return false;
        }

        /// <summary>Determines whether a path is an exact canonical baseline output rather than a fixture directory target.</summary>
        /// <param name="assetPath">The AssetDatabase path to inspect.</param>
        /// <returns><see langword="true"/> only for the canonical JSON baseline or its exact metadata files.</returns>
        private static bool IsExactCanonicalBaselineOutputPath(string assetPath)
        {
            return string.Equals(
                    assetPath,
                    PureBaseValidationSceneRegressionTests.BaselinePath,
                    StringComparison.Ordinal
                ) || IsCanonicalBaselineMetadataPath(assetPath);
        }

        /// <summary>Determines whether a path is one of the required exact metadata files for the canonical baseline output.</summary>
        /// <param name="assetPath">The AssetDatabase path to inspect.</param>
        /// <returns><see langword="true"/> only for the canonical baseline directory or JSON sidecar metadata path.</returns>
        private static bool IsCanonicalBaselineMetadataPath(string assetPath)
        {
            return string.Equals(
                    assetPath,
                    CanonicalBaselineDirectoryMetaPath,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    assetPath,
                    CanonicalBaselineSidecarMetaPath,
                    StringComparison.Ordinal
                );
        }

        /// <summary>Fails before any write when the current editor cannot produce the reviewed BIRP baseline.</summary>
        /// <param name="environment">The environment to validate.</param>
        internal static void ValidateEnvironment(IEnvironment environment)
        {
            if (
                !string.Equals(
                    environment.UnityVersion,
                    PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidOperationException(
                    $"Baseline regeneration requires Unity {PureBaseValidationSceneRegressionTests.ExpectedUnityVersion}, not '{environment.UnityVersion}'."
                );
            }

            if (!environment.IsBuiltInRenderPipeline)
            {
                throw new InvalidOperationException(
                    "Baseline regeneration requires the Built-in Render Pipeline."
                );
            }

            if (environment.GraphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                throw new InvalidOperationException(
                    $"Baseline regeneration requires D3D11, not '{environment.GraphicsDeviceType}'."
                );
            }

            if (environment.ColorSpace != ColorSpace.Linear)
            {
                throw new InvalidOperationException(
                    $"Baseline regeneration requires Linear color space, not '{environment.ColorSpace}'."
                );
            }
        }

        /// <summary>Defines the read-only environment values that gate all regeneration writes.</summary>
        internal interface IEnvironment
        {
            /// <summary>Gets the active Unity version.</summary>
            string UnityVersion { get; }

            /// <summary>Gets whether the Built-in Render Pipeline is active.</summary>
            bool IsBuiltInRenderPipeline { get; }

            /// <summary>Gets the active graphics device.</summary>
            GraphicsDeviceType GraphicsDeviceType { get; }

            /// <summary>Gets the project color space.</summary>
            ColorSpace ColorSpace { get; }
        }

        /// <summary>Defines fixture-generation operations that are unreachable when environment validation fails.</summary>
        internal interface IFixtureGenerationOperations
        {
            /// <summary>Generates the canonical scene, lighting, and material fixture.</summary>
            void GenerateFixture();
        }

        /// <summary>Defines the fixture generation and baseline-writing operations permitted after validation.</summary>
        internal interface IRegenerationOperations : IFixtureGenerationOperations
        {
            /// <summary>Bakes the canonical scene and writes its exact baseline DTO.</summary>
            void BakeAndWriteBaseline();
        }

        /// <summary>Defines the canonical scene read operation permitted by the non-mutating candidate entry.</summary>
        internal interface IObservationCaptureOperations
        {
            /// <summary>Captures the complete Daily observation without baking or persisting Unity assets.</summary>
            /// <returns>The read-only canonical scene observation.</returns>
            SceneRegressionObservation CaptureObservation();
        }

        /// <summary>Defines the only baseline persistence operation permitted by reviewed-candidate apply.</summary>
        internal interface IReviewedCandidateWriter
        {
            /// <summary>Writes the already validated exact baseline through the supplied transaction boundary.</summary>
            /// <param name="baseline">The reviewed exact baseline supplied by the external candidate.</param>
            /// <param name="writeBoundary">The active transaction audit.</param>
            void WriteExactBaseline(SceneRegressionBaseline baseline, IWriteBoundary writeBoundary);
        }

        /// <summary>Defines the fail-closed persistence boundary for explicit regeneration.</summary>
        internal interface IWriteBoundary
        {
            /// <summary>Captures the pre-write durable inventory and dirty non-scene asset state.</summary>
            void BeginTransaction();

            /// <summary>Fails when a non-canonical durable target changed during the transaction.</summary>
            void VerifyNoUnrelatedChanges();
        }

        /// <summary>Defines the external JSON artifact used to collect nonzero observations before an explicit baseline apply.</summary>
        [Serializable]
        public sealed class ObservationCandidate
        {
            /// <summary>Stores the candidate schema version.</summary>
            public int schemaVersion;

            /// <summary>Stores the Unity version used for observation.</summary>
            public string unityVersion;

            /// <summary>Stores the graphics device used for observation.</summary>
            public string graphicsDevice;

            /// <summary>Stores the color space used for observation.</summary>
            public string colorSpace;

            /// <summary>Stores the render pipeline used for observation.</summary>
            public string renderPipeline;

            /// <summary>Stores all read-only observation evidence, including scene and shadow diagnostics.</summary>
            public SceneRegressionObservation observation;

            /// <summary>Stores the exact baseline representation supplied for explicit review and apply.</summary>
            public SceneRegressionBaseline exactBaseline;
        }

        /// <summary>Runs one canonical persistence operation and audits durable state before another write can begin.</summary>
        /// <param name="writeBoundary">The active transaction audit.</param>
        /// <param name="persistenceOperation">The single canonical persistence operation to execute.</param>
        internal static void PersistCanonicalOperation(
            IWriteBoundary writeBoundary,
            Action persistenceOperation
        )
        {
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));
            if (persistenceOperation == null)
                throw new ArgumentNullException(nameof(persistenceOperation));

            persistenceOperation();
            writeBoundary.VerifyNoUnrelatedChanges();
        }

        /// <summary>Writes only an already reviewed exact candidate baseline and its Unity metadata through targeted audited persistence.</summary>
        /// <param name="baseline">The exact baseline embedded in the reviewed external candidate.</param>
        /// <param name="writeBoundary">The transaction audit applied after each canonical persistence operation.</param>
        internal static void WriteReviewedCandidateBaseline(
            SceneRegressionBaseline baseline,
            IWriteBoundary writeBoundary
        )
        {
            if (baseline == null)
                throw new ArgumentNullException(nameof(baseline));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));
            PureBaseValidationSceneRegressionTests.ValidateBaselineObservability(
                baseline,
                "Reviewed candidate baseline before write"
            );
            PureBaseRegressionBaselineStorage.WriteCanonicalBaseline(
                baseline,
                writeBoundary,
                new UnityCanonicalBaselineStorageBackend()
            );
        }

        /// <summary>Supplies the durable filesystem and dirty-asset state inspected by a write transaction.</summary>
        internal interface ITransactionAuditState
        {
            /// <summary>Rejects a dirty non-canonical scene before it can be saved indirectly.</summary>
            void EnsureNoDirtyNonCanonicalScenes();

            /// <summary>Captures non-canonical durable file paths and their content hashes.</summary>
            /// <returns>The current durable inventory keyed by Unity-style asset path.</returns>
            Dictionary<string, string> CaptureNonCanonicalDurableInventory();

            /// <summary>Captures every dirty non-canonical durable non-scene asset.</summary>
            /// <returns>The current dirty assets and their identities.</returns>
            List<DirtyAssetState> CaptureDirtyNonCanonicalAssets();
        }

        /// <summary>Describes a dirty asset by durable path and current Unity object identity.</summary>
        internal sealed class DirtyAssetState
        {
            /// <summary>Initializes a dirty asset state.</summary>
            /// <param name="assetPath">The durable Unity asset path.</param>
            /// <param name="identity">The current Unity object identity.</param>
            public DirtyAssetState(string assetPath, string identity)
            {
                AssetPath = assetPath;
                Identity = identity;
            }

            /// <summary>Gets the durable Unity asset path.</summary>
            public string AssetPath { get; }

            /// <summary>Gets the Unity object identity associated with the path.</summary>
            public string Identity { get; }

            /// <summary>Gets a stable dictionary key for comparison across audit checkpoints.</summary>
            public string Key => AssetPath + "|" + Identity;
        }

        /// <summary>Compares one pre-write snapshot against later checkpoints without mutating unrelated state.</summary>
        internal sealed class TransactionWriteBoundary : IWriteBoundary
        {
            private readonly ITransactionAuditState state;
            private Dictionary<string, string> initialInventory;
            private Dictionary<string, DirtyAssetState> initialDirtyAssets;

            /// <summary>Initializes an audit boundary with the state it must preserve.</summary>
            /// <param name="state">The durable workspace state provider.</param>
            public TransactionWriteBoundary(ITransactionAuditState state)
            {
                this.state = state ?? throw new ArgumentNullException(nameof(state));
            }

            /// <inheritdoc />
            public void BeginTransaction()
            {
                state.EnsureNoDirtyNonCanonicalScenes();
                initialInventory = state.CaptureNonCanonicalDurableInventory();
                initialDirtyAssets = CreateDirtyAssetIndex(state.CaptureDirtyNonCanonicalAssets());
            }

            /// <inheritdoc />
            public void VerifyNoUnrelatedChanges()
            {
                if (initialInventory == null || initialDirtyAssets == null)
                {
                    throw new InvalidOperationException(
                        "Baseline regeneration write transaction was not initialized."
                    );
                }

                state.EnsureNoDirtyNonCanonicalScenes();
                EnsureMatchingInventory(
                    initialInventory,
                    state.CaptureNonCanonicalDurableInventory()
                );
                EnsureMatchingDirtyAssets(
                    initialDirtyAssets,
                    CreateDirtyAssetIndex(state.CaptureDirtyNonCanonicalAssets())
                );
            }

            /// <summary>Builds a stable index for dirty asset identity comparison.</summary>
            /// <param name="dirtyAssets">The assets reported at one checkpoint.</param>
            /// <returns>The assets keyed by path and identity.</returns>
            private static Dictionary<string, DirtyAssetState> CreateDirtyAssetIndex(
                List<DirtyAssetState> dirtyAssets
            )
            {
                var index = new Dictionary<string, DirtyAssetState>(StringComparer.Ordinal);
                foreach (DirtyAssetState dirtyAsset in dirtyAssets)
                {
                    if (
                        dirtyAsset == null
                        || string.IsNullOrEmpty(dirtyAsset.AssetPath)
                        || string.IsNullOrEmpty(dirtyAsset.Identity)
                    )
                    {
                        throw new InvalidOperationException(
                            "Baseline regeneration received an invalid dirty asset audit record."
                        );
                    }

                    if (!index.TryAdd(dirtyAsset.Key, dirtyAsset))
                    {
                        throw new InvalidOperationException(
                            $"Baseline regeneration received duplicate dirty asset identity '{dirtyAsset.AssetPath}'."
                        );
                    }
                }

                return index;
            }

            /// <summary>Fails when a non-canonical durable file was added, deleted, or changed.</summary>
            /// <param name="expectedInventory">The pre-write inventory.</param>
            /// <param name="actualInventory">The current inventory.</param>
            private static void EnsureMatchingInventory(
                Dictionary<string, string> expectedInventory,
                Dictionary<string, string> actualInventory
            )
            {
                if (expectedInventory.Count != actualInventory.Count)
                {
                    throw new InvalidOperationException(
                        "Baseline regeneration changed the non-canonical durable filesystem inventory."
                    );
                }

                foreach (KeyValuePair<string, string> entry in expectedInventory)
                {
                    if (
                        !actualInventory.TryGetValue(entry.Key, out string currentHash)
                        || !string.Equals(entry.Value, currentHash, StringComparison.Ordinal)
                    )
                    {
                        throw new InvalidOperationException(
                            $"Baseline regeneration changed non-canonical durable file '{entry.Key}'."
                        );
                    }
                }
            }

            /// <summary>Fails when a preexisting dirty asset was cleaned or replaced, or a new durable asset became dirty.</summary>
            /// <param name="expectedDirtyAssets">The dirty assets present before writes began.</param>
            /// <param name="actualDirtyAssets">The dirty assets present at a checkpoint.</param>
            private static void EnsureMatchingDirtyAssets(
                Dictionary<string, DirtyAssetState> expectedDirtyAssets,
                Dictionary<string, DirtyAssetState> actualDirtyAssets
            )
            {
                if (expectedDirtyAssets.Count != actualDirtyAssets.Count)
                {
                    throw new InvalidOperationException(
                        "Baseline regeneration changed the non-canonical dirty asset set."
                    );
                }

                foreach (KeyValuePair<string, DirtyAssetState> entry in expectedDirtyAssets)
                {
                    if (!actualDirtyAssets.ContainsKey(entry.Key))
                    {
                        throw new InvalidOperationException(
                            $"Baseline regeneration cleaned or replaced preexisting dirty asset '{entry.Value.AssetPath}'."
                        );
                    }
                }
            }
        }

        /// <summary>Records write-capable operations while the batch probe runs the real environment and dirty-target guards.</summary>
        private sealed class BatchProbeOperations : IRegenerationOperations
        {
            /// <summary>Gets the number of fixture-generation calls.</summary>
            public int GenerateFixtureCallCount { get; private set; }

            /// <summary>Gets the number of baseline-writing calls.</summary>
            public int BakeAndWriteBaselineCallCount { get; private set; }

            /// <inheritdoc />
            public void GenerateFixture()
            {
                GenerateFixtureCallCount++;
            }

            /// <inheritdoc />
            public void BakeAndWriteBaseline()
            {
                BakeAndWriteBaselineCallCount++;
            }
        }

        /// <summary>Reads the actual Unity Editor environment.</summary>
        internal sealed class UnityEnvironment : IEnvironment
        {
            /// <inheritdoc />
            public string UnityVersion => Application.unityVersion;

            /// <inheritdoc />
            public bool IsBuiltInRenderPipeline => GraphicsSettings.currentRenderPipeline == null;

            /// <inheritdoc />
            public GraphicsDeviceType GraphicsDeviceType => SystemInfo.graphicsDeviceType;

            /// <inheritdoc />
            public ColorSpace ColorSpace => PlayerSettings.colorSpace;
        }

        /// <summary>Applies the actual Unity durable-state transaction audit before regeneration can persist any changes.</summary>
        internal sealed class UnityWriteBoundary : IWriteBoundary
        {
            private readonly TransactionWriteBoundary transaction = new TransactionWriteBoundary(
                new UnityTransactionAuditState()
            );

            /// <inheritdoc />
            public void BeginTransaction()
            {
                transaction.BeginTransaction();
            }

            /// <inheritdoc />
            public void VerifyNoUnrelatedChanges()
            {
                transaction.VerifyNoUnrelatedChanges();
            }
        }

        /// <summary>Opens the canonical scene additively, captures Daily evidence, and restores the prior scene setup without persistence.</summary>
        private sealed class UnityObservationCaptureOperations : IObservationCaptureOperations
        {
            /// <inheritdoc />
            public SceneRegressionObservation CaptureObservation()
            {
                SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
                try
                {
                    Scene scene = EditorSceneManager.OpenScene(
                        PureBaseValidationSceneRegressionTests.ScenePath,
                        OpenSceneMode.Additive
                    );
                    if (!SceneManager.SetActiveScene(scene))
                        throw new InvalidOperationException(
                            "The canonical validation scene could not become active for read-only observation."
                        );
                    return PureBaseValidationSceneRegressionTests.CaptureObservation(scene);
                }
                finally
                {
                    if (previousSceneSetup != null && previousSceneSetup.Length > 0)
                    {
                        EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
                    }
                }
            }
        }

        /// <summary>Writes only the external candidate's already validated exact baseline.</summary>
        private sealed class UnityReviewedCandidateWriter : IReviewedCandidateWriter
        {
            /// <inheritdoc />
            public void WriteExactBaseline(
                SceneRegressionBaseline baseline,
                IWriteBoundary writeBoundary
            )
            {
                WriteReviewedCandidateBaseline(baseline, writeBoundary);
            }
        }

        /// <summary>Reads each external reviewed artifact from one immutable byte-array snapshot.</summary>
        private sealed class FileArtifactReader : PureBaseReviewedBaselineCandidate.IArtifactReader
        {
            /// <inheritdoc />
            public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
        }

        /// <summary>Writes each reviewed artifact only after its source and canonical state validate.</summary>
        private sealed class FileArtifactWriter : PureBaseReviewedBaselineCandidate.IArtifactWriter
        {
            /// <inheritdoc />
            public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);
        }

        /// <summary>Reads Unity's durable package roots, non-canonical files, and dirty asset identities.</summary>
        private sealed class UnityTransactionAuditState : ITransactionAuditState
        {
            /// <inheritdoc />
            public void EnsureNoDirtyNonCanonicalScenes()
            {
                PureBaseRegressionBaselineGenerator.EnsureNoDirtyNonCanonicalScenes();
            }

            /// <inheritdoc />
            public Dictionary<string, string> CaptureNonCanonicalDurableInventory()
            {
                var inventory = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (DurableRoot root in EnumerateDurableRoots())
                {
                    foreach (
                        string filePath in Directory.GetFiles(
                            root.PhysicalPath,
                            "*",
                            SearchOption.AllDirectories
                        )
                    )
                    {
                        string assetPath =
                            root.AssetPath
                            + "/"
                            + filePath
                                .Substring(root.PhysicalPath.Length)
                                .TrimStart(
                                    Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar
                                )
                                .Replace('\\', '/');
                        if (!IsNonCanonicalDurableInventoryAssetPath(assetPath))
                            continue;
                        inventory.Add(assetPath, ComputeFileHash(filePath));
                    }
                }

                return inventory;
            }

            /// <inheritdoc />
            public List<DirtyAssetState> CaptureDirtyNonCanonicalAssets()
            {
                var dirtyAssets = new List<DirtyAssetState>();
                var capturedInstanceIds = new HashSet<int>();
                foreach (
                    UnityEngine.Object asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                )
                {
                    if (asset == null || !EditorUtility.IsDirty(asset))
                        continue;
                    string assetPath = AssetDatabase.GetAssetPath(asset);
                    if (
                        !IsNonCanonicalDurableWorkspaceAssetPath(assetPath)
                        || !capturedInstanceIds.Add(asset.GetInstanceID())
                    )
                        continue;
                    dirtyAssets.Add(
                        new DirtyAssetState(assetPath, asset.GetInstanceID().ToString())
                    );
                }

                return dirtyAssets;
            }

            /// <summary>Enumerates only physical project and embedded/local package roots that Unity can persist.</summary>
            /// <returns>The durable roots paired with Unity-style asset paths.</returns>
            private static IEnumerable<DurableRoot> EnumerateDurableRoots()
            {
                if (Directory.Exists(Application.dataPath))
                {
                    yield return new DurableRoot("Assets", Application.dataPath);
                }

                foreach (PackageInfo packageInfo in PackageInfo.GetAllRegisteredPackages())
                {
                    if (
                        packageInfo.source != PackageSource.Embedded
                        && packageInfo.source != PackageSource.Local
                    )
                        continue;
                    if (
                        string.IsNullOrEmpty(packageInfo.resolvedPath)
                        || !Directory.Exists(packageInfo.resolvedPath)
                        || IsPackageCachePath(packageInfo.resolvedPath)
                    )
                        continue;
                    yield return new DurableRoot(
                        "Packages/" + packageInfo.name,
                        packageInfo.resolvedPath
                    );
                }
            }

            /// <summary>Calculates a stable content hash without importing, saving, or changing the file.</summary>
            /// <param name="filePath">The durable file to hash.</param>
            /// <returns>The SHA-256 content hash.</returns>
            private static string ComputeFileHash(string filePath)
            {
                using (var algorithm = SHA256.Create())
                using (FileStream stream = File.OpenRead(filePath))
                {
                    return BitConverter
                        .ToString(algorithm.ComputeHash(stream))
                        .Replace("-", string.Empty);
                }
            }
        }

        /// <summary>Pairs a Unity asset-root path with its resolved physical directory.</summary>
        private sealed class DurableRoot
        {
            /// <summary>Initializes a durable asset root.</summary>
            /// <param name="assetPath">The Unity-style root path.</param>
            /// <param name="physicalPath">The resolved physical root.</param>
            public DurableRoot(string assetPath, string physicalPath)
            {
                AssetPath = assetPath;
                PhysicalPath = physicalPath;
            }

            /// <summary>Gets the Unity-style root path.</summary>
            public string AssetPath { get; }

            /// <summary>Gets the resolved physical root.</summary>
            public string PhysicalPath { get; }
        }

        /// <summary>Performs the explicit fixture generation, synchronous bake, and baseline write.</summary>
        private sealed class UnityRegenerationOperations : IRegenerationOperations
        {
            private readonly IWriteBoundary writeBoundary;

            /// <summary>Initializes canonical write operations with their active transaction audit.</summary>
            /// <param name="writeBoundary">The audit used after each canonical persistence checkpoint.</param>
            public UnityRegenerationOperations(IWriteBoundary writeBoundary)
            {
                this.writeBoundary =
                    writeBoundary ?? throw new ArgumentNullException(nameof(writeBoundary));
            }

            /// <inheritdoc />
            public void GenerateFixture()
            {
                Debug.Log(
                    $"Pure-Base baseline regeneration may write only: {string.Join(", ", WritableCanonicalTargets)}"
                );
                PureBaseValidationLightingSettingsGenerator.GenerateAndValidateAfterGuards(
                    writeBoundary
                );
            }

            /// <inheritdoc />
            public void BakeAndWriteBaseline()
            {
                SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
                try
                {
                    Scene scene = EditorSceneManager.OpenScene(
                        PureBaseValidationSceneRegressionTests.ScenePath,
                        OpenSceneMode.Additive
                    );
                    SceneManager.SetActiveScene(scene);
                    if (!Lightmapping.Bake())
                    {
                        throw new InvalidOperationException(
                            "The canonical validation scene synchronous bake did not start."
                        );
                    }
                    writeBoundary.VerifyNoUnrelatedChanges();

                    SceneRegressionObservation observation =
                        PureBaseValidationSceneRegressionTests.CaptureObservation(scene);
                    SceneRegressionBaseline baseline =
                        PureBaseValidationSceneRegressionTests.CreateExactBaseline(observation);
                    WriteBaseline(baseline, writeBoundary);
                    PersistCanonicalOperation(
                        writeBoundary,
                        () => EditorSceneManager.SaveScene(scene)
                    );
                    PersistCanonicalOperation(
                        writeBoundary,
                        () =>
                            AssetDatabase.ImportAsset(
                                PureBaseValidationSceneRegressionTests.ScenePath,
                                ImportAssetOptions.ForceSynchronousImport
                                    | ImportAssetOptions.ForceUpdate
                            )
                    );
                }
                finally
                {
                    if (previousSceneSetup != null && previousSceneSetup.Length > 0)
                    {
                        EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
                    }
                }
            }

            /// <summary>Writes only the versioned canonical JSON baseline and never changes numeric ranges after review.</summary>
            /// <param name="baseline">The exact observation captured after the explicit bake.</param>
            private static void WriteBaseline(
                SceneRegressionBaseline baseline,
                IWriteBoundary writeBoundary
            )
            {
                PureBaseValidationSceneRegressionTests.ValidateBaselineObservability(
                    baseline,
                    "Regenerated baseline before write"
                );
                if (File.Exists(PureBaseValidationSceneRegressionTests.BaselinePath))
                {
                    SceneRegressionBaseline reviewedBaseline =
                        PureBaseValidationSceneRegressionTests.LoadBaseline();
                    EnsureReviewedRangesAreNotWidened(reviewedBaseline, baseline);
                    PreserveReviewedRanges(reviewedBaseline, baseline);
                }

                PureBaseRegressionBaselineStorage.WriteCanonicalBaseline(
                    baseline,
                    writeBoundary,
                    new UnityCanonicalBaselineStorageBackend()
                );
            }

            /// <summary>Copies reviewed ranges so explicit regeneration cannot silently narrow or widen numeric tolerances.</summary>
            /// <param name="reviewedBaseline">The existing reviewed baseline.</param>
            /// <param name="regeneratedBaseline">The newly observed baseline being written.</param>
            private static void PreserveReviewedRanges(
                SceneRegressionBaseline reviewedBaseline,
                SceneRegressionBaseline regeneratedBaseline
            )
            {
                regeneratedBaseline.sceneVisiblePixelCount =
                    reviewedBaseline.sceneVisiblePixelCount;
                for (int index = 0; index < reviewedBaseline.metaAlbedo.Length; index++)
                {
                    regeneratedBaseline.metaAlbedo[index].meanLuminance = reviewedBaseline
                        .metaAlbedo[index]
                        .meanLuminance;
                }
            }

            /// <summary>Rejects writing an observation when it would silently widen a reviewed numeric tolerance.</summary>
            /// <param name="reviewedBaseline">The existing reviewed baseline.</param>
            /// <param name="exactBaseline">The newly observed exact baseline.</param>
            private static void EnsureReviewedRangesAreNotWidened(
                SceneRegressionBaseline reviewedBaseline,
                SceneRegressionBaseline exactBaseline
            )
            {
                if (reviewedBaseline.metaAlbedo.Length != exactBaseline.metaAlbedo.Length)
                {
                    throw new InvalidOperationException(
                        "Baseline regeneration cannot change the reviewed Meta observation count."
                    );
                }

                for (int index = 0; index < reviewedBaseline.metaAlbedo.Length; index++)
                {
                    FloatRange reviewed = reviewedBaseline.metaAlbedo[index].meanLuminance;
                    FloatRange exact = exactBaseline.metaAlbedo[index].meanLuminance;
                    if (
                        reviewed == null
                        || exact == null
                        || exact.minimum < reviewed.minimum
                        || exact.maximum > reviewed.maximum
                    )
                    {
                        throw new InvalidOperationException(
                            "Baseline regeneration refuses to automatically widen a reviewed Meta luminance tolerance."
                        );
                    }
                }

                IntRange reviewedVisible = reviewedBaseline.sceneVisiblePixelCount;
                IntRange exactVisible = exactBaseline.sceneVisiblePixelCount;
                if (
                    reviewedVisible == null
                    || exactVisible == null
                    || exactVisible.minimum < reviewedVisible.minimum
                    || exactVisible.maximum > reviewedVisible.maximum
                )
                {
                    throw new InvalidOperationException(
                        "Baseline regeneration refuses to automatically widen the reviewed visible-pixel tolerance."
                    );
                }
            }
        }

        /// <summary>Verifies that configuration failures occur before a write-capable operation can run.</summary>
        public sealed class EnvironmentGuardTests
        {
            /// <summary>Ensures unsupported environments fail before either write seam.</summary>
            /// <param name="environment">The unsupported environment to test.</param>
            [TestCase("2022.3.0f1", true, GraphicsDeviceType.Direct3D11, ColorSpace.Linear)]
            [TestCase(
                PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                false,
                GraphicsDeviceType.Direct3D11,
                ColorSpace.Linear
            )]
            [TestCase(
                PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                true,
                GraphicsDeviceType.Direct3D12,
                ColorSpace.Linear
            )]
            [TestCase(
                PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                true,
                GraphicsDeviceType.Direct3D11,
                ColorSpace.Gamma
            )]
            public void UnsupportedEnvironmentFailsBeforeAnyWriteOperation(
                string unityVersion,
                bool isBuiltInRenderPipeline,
                GraphicsDeviceType graphicsDeviceType,
                ColorSpace colorSpace
            )
            {
                var operations = new RecordingOperations();

                Assert.Throws<InvalidOperationException>(() =>
                    Regenerate(
                        new TestEnvironment(
                            unityVersion,
                            isBuiltInRenderPipeline,
                            graphicsDeviceType,
                            colorSpace
                        ),
                        operations
                    )
                );

                Assert.That(operations.GenerateFixtureCallCount, Is.Zero);
                Assert.That(operations.BakeAndWriteBaselineCallCount, Is.Zero);
            }

            /// <summary>Supplies fixed environment values to fail-before-write tests.</summary>
            private sealed class TestEnvironment : IEnvironment
            {
                /// <summary>Initializes fixed environment values.</summary>
                public TestEnvironment(
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

            /// <summary>Records calls that must remain unreachable when environment validation fails.</summary>
            private sealed class RecordingOperations : IRegenerationOperations
            {
                /// <summary>Gets fixture-generation calls.</summary>
                public int GenerateFixtureCallCount { get; private set; }

                /// <summary>Gets baseline-writing calls.</summary>
                public int BakeAndWriteBaselineCallCount { get; private set; }

                /// <inheritdoc />
                public void GenerateFixture() => GenerateFixtureCallCount++;

                /// <inheritdoc />
                public void BakeAndWriteBaseline() => BakeAndWriteBaselineCallCount++;
            }
        }
    }

}
