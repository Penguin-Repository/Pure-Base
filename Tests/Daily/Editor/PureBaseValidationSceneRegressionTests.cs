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

// Validates the committed BIRP validation fixture against a read-only numeric baseline.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    /// <summary>Validates the committed BIRP validation scene without baking or saving persistent assets.</summary>
    public sealed class PureBaseValidationSceneRegressionTests
    {
        /// <summary>Identifies the canonical validation scene.</summary>
        public const string ScenePath =
            "Packages/jp.penguin.purebase/Tests/Fixtures/Scenes/PureBaseValidation.unity";

        /// <summary>Identifies the canonical Lighting Settings asset.</summary>
        public const string LightingSettingsPath =
            "Packages/jp.penguin.purebase/Tests/Fixtures/Lighting/PureBaseValidationLightingSettings.lighting";

        /// <summary>Identifies the reviewed BIRP numeric baseline.</summary>
        public const string BaselinePath =
            "Packages/jp.penguin.purebase/Tests/Baselines/birp-d3d11-2022.3.22f1.json";

        /// <summary>Defines the supported baseline schema.</summary>
        public const int BaselineSchemaVersion = 2;

        /// <summary>Defines the expected baseline Unity version.</summary>
        public const string ExpectedUnityVersion = "2022.3.22f1";

        /// <summary>Defines the fixed readback dimension.</summary>
        public const int RenderSize = 160;

        /// <summary>Defines the lowest accepted mean luminance for each Meta material observation.</summary>
        public const float MinimumMetaLuminance = 0.001f;

        /// <summary>Defines the lowest accepted changed-pixel count for the directional shadow observation.</summary>
        public const int MinimumShadowChangedPixelCount = 32;

        /// <summary>Identifies the persisted scene used when an isolated restoration test needs a non-canonical owner.</summary>
        private const string TestOwnerScenePath = "Assets/Pure-Base.unity";

        /// <summary>Lists the product shaders expected in the canonical scene.</summary>
        private static readonly string[] ProductShaderNames =
        {
            "PureBase/Unlit",
            "PureBase/Toon",
            "PureBase/PBR",
            "PureBase/Hybrid",
        };

        /// <summary>Stores the one allocation point that focused cleanup tests force to fail.</summary>
        private static CaptureAllocationFault injectedCaptureAllocationFault;

        /// <summary>Reports whether the latest transient capture disposed every tracked native resource.</summary>
        private static bool lastCaptureResourcesReleased;

        /// <summary>Ensures a missing baseline fails before Daily opens or changes the canonical scene.</summary>
        [Test]
        public void MissingBaselineFailsBeforeSceneMutation()
        {
            Scene validationScene = SceneManager.GetSceneByPath(ScenePath);
            bool wasLoaded = validationScene.isLoaded;
            bool wasDirty = validationScene.isDirty;

            Assert.Throws<AssertionException>(() =>
                LoadBaseline("Library/PureBaseTests/missing-scene-regression-baseline.json")
            );

            Assert.That(validationScene.isLoaded, Is.EqualTo(wasLoaded));
            Assert.That(validationScene.isDirty, Is.EqualTo(wasDirty));
        }

        /// <summary>Ensures the transient Meta mesh retains every legacy UV channel and produces finite nonzero results for all product materials.</summary>
        [Test]
        public void MetaCaptureUsesLegacyUvChannelsAndProducesNonzeroLuminance()
        {
            Mesh mesh = CreateScreenMesh();
            EditorStateSnapshot state = EditorStateSnapshot.Capture();
            Scene validationScene = default;
            bool sceneWasLoaded = false;
            bool sceneWasDirty = false;
            try
            {
                Assert.That(mesh.uv, Has.Length.EqualTo(mesh.vertexCount));
                Assert.That(mesh.uv2, Has.Length.EqualTo(mesh.vertexCount));
                Assert.That(mesh.uv3, Has.Length.EqualTo(mesh.vertexCount));
                CollectionAssert.AreEqual(mesh.uv, mesh.uv2);
                CollectionAssert.AreEqual(mesh.uv, mesh.uv3);

                validationScene = SceneManager.GetSceneByPath(ScenePath);
                sceneWasLoaded = validationScene.isLoaded;
                if (!sceneWasLoaded)
                    validationScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive
                    );
                sceneWasDirty = validationScene.isDirty;

                MetaAlbedoObservation[] observations = CaptureMetaAlbedo(
                    GetProductMaterials(validationScene)
                );
                Assert.That(observations, Has.Length.EqualTo(ProductShaderNames.Length));
                for (int index = 0; index < observations.Length; index++)
                {
                    Assert.That(
                        IsFinite(observations[index].meanLuminance),
                        Is.True,
                        $"Meta luminance for '{observations[index].materialName}' is non-finite."
                    );
                    Assert.That(
                        observations[index].meanLuminance,
                        Is.GreaterThan(MinimumMetaLuminance),
                        $"Meta luminance for '{observations[index].materialName}' is not observable."
                    );
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                state.Restore(validationScene, sceneWasLoaded, sceneWasDirty);
            }
        }

        /// <summary>Ensures the regular additive-scene shadow capture is selected after an identical fixture confirms preview rendering has no silhouette.</summary>
        [Test]
        public void ShadowCaptureUsesRegularAdditiveSceneWhenPreviewSceneHasNoSilhouette()
        {
            EditorStateSnapshot state = EditorStateSnapshot.Capture();
            Scene validationScene = default;
            bool sceneWasLoaded = false;
            bool sceneWasDirty = false;
            try
            {
                validationScene = SceneManager.GetSceneByPath(ScenePath);
                sceneWasLoaded = validationScene.isLoaded;
                if (!sceneWasLoaded)
                    validationScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive
                    );
                sceneWasDirty = validationScene.isDirty;

                ShadowCaptureComparison comparison = CaptureShadowComparison(
                    GetProductMaterials(validationScene)[0]
                );
                Assert.That(
                    comparison.preview.changedPixelCount,
                    Is.LessThanOrEqualTo(MinimumShadowChangedPixelCount),
                    comparison.Describe()
                );
                Assert.That(
                    comparison.additive.coveragePixelCount,
                    Is.GreaterThan(0),
                    comparison.Describe()
                );
                Assert.That(
                    comparison.additive.maxAbsoluteRgbDelta,
                    Is.GreaterThan(0.002f),
                    comparison.Describe()
                );
                Assert.That(
                    comparison.additive.changedPixelCount,
                    Is.GreaterThan(MinimumShadowChangedPixelCount),
                    comparison.Describe()
                );
                Assert.That(
                    CaptureShadowSilhouette(validationScene).changedPixelCount,
                    Is.EqualTo(comparison.additive.changedPixelCount),
                    comparison.Describe()
                );
            }
            finally
            {
                state.Restore(validationScene, sceneWasLoaded, sceneWasDirty);
            }
        }

        /// <summary>Ensures a layer-31 shadow caster in another loaded scene cannot affect the regular additive capture.</summary>
        [Test]
        public void RegularAdditiveShadowCaptureExcludesOtherLoadedScenes()
        {
            EditorStateSnapshot state = EditorStateSnapshot.Capture();
            Scene validationScene = default;
            bool sceneWasLoaded = false;
            bool sceneWasDirty = false;
            try
            {
                validationScene = SceneManager.GetSceneByPath(ScenePath);
                sceneWasLoaded = validationScene.isLoaded;
                if (!sceneWasLoaded)
                    validationScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive
                    );
                sceneWasDirty = validationScene.isDirty;

                Material sourceMaterial = GetProductMaterials(validationScene)[0];
                ShadowCaptureObservation expected = CaptureShadowSilhouette(
                    sourceMaterial,
                    TemporarySceneOwnership.RegularAdditive
                );
                GameObject foreignCaster = EditorUtility.CreateGameObjectWithHideFlags(
                    "PureBase Daily Foreign Shadow Caster",
                    HideFlags.HideAndDontSave,
                    typeof(MeshFilter),
                    typeof(MeshRenderer)
                );
                try
                {
                    SceneManager.MoveGameObjectToScene(foreignCaster, validationScene);
                    foreignCaster.layer = 31;
                    foreignCaster.transform.position = new Vector3(0.0f, 1.5f, 0.0f);
                    foreignCaster.GetComponent<MeshFilter>().sharedMesh =
                        Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                    foreignCaster.GetComponent<MeshRenderer>().sharedMaterial = sourceMaterial;
                    foreignCaster.GetComponent<MeshRenderer>().shadowCastingMode =
                        ShadowCastingMode.On;

                    ShadowCaptureObservation actual = CaptureShadowSilhouette(
                        sourceMaterial,
                        TemporarySceneOwnership.RegularAdditive
                    );
                    Assert.That(
                        actual.changedPixelCount,
                        Is.EqualTo(expected.changedPixelCount),
                        actual.Describe()
                    );
                    Assert.That(
                        actual.coveragePixelCount,
                        Is.EqualTo(expected.coveragePixelCount),
                        actual.Describe()
                    );
                    Assert.That(
                        actual.maxAbsoluteRgbDelta,
                        Is.EqualTo(expected.maxAbsoluteRgbDelta).Within(0.0001f),
                        actual.Describe()
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(foreignCaster);
                }
            }
            finally
            {
                state.Restore(validationScene, sceneWasLoaded, sceneWasDirty);
            }
        }

        /// <summary>Ensures Meta and Shadow capture release every acquired native resource after each intermediate allocation failure.</summary>
        [Test]
        public void CapturePartialInitializationReleasesResourcesAndRestoresState()
        {
            EditorStateSnapshot state = EditorStateSnapshot.Capture();
            Scene validationScene = default;
            bool sceneWasLoaded = false;
            bool sceneWasDirty = false;
            Vector4 originalVertexControl = Shader.GetGlobalVector("unity_MetaVertexControl");
            Vector4 originalFragmentControl = Shader.GetGlobalVector("unity_MetaFragmentControl");
            Vector4 originalLightmapSt = Shader.GetGlobalVector("unity_LightmapST");
            float originalOutputBoost = Shader.GetGlobalFloat("unity_OneOverOutputBoost");
            float originalMaximumOutput = Shader.GetGlobalFloat("unity_MaxOutputValue");
            try
            {
                validationScene = SceneManager.GetSceneByPath(ScenePath);
                sceneWasLoaded = validationScene.isLoaded;
                if (!sceneWasLoaded)
                    validationScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive
                    );
                sceneWasDirty = validationScene.isDirty;
                Material sourceMaterial = GetProductMaterials(validationScene)[0];

                foreach (
                    CaptureAllocationFault fault in new[]
                    {
                        CaptureAllocationFault.MetaMaterial,
                        CaptureAllocationFault.MetaMesh,
                        CaptureAllocationFault.MetaCamera,
                        CaptureAllocationFault.MetaTarget,
                        CaptureAllocationFault.MetaReadback,
                    }
                )
                {
                    AssertCaptureAllocationFailure(fault, () => RenderMetaAlbedo(sourceMaterial));
                }

                foreach (
                    CaptureAllocationFault fault in new[]
                    {
                        CaptureAllocationFault.ShadowCasterMaterial,
                        CaptureAllocationFault.ShadowReceiverMaterial,
                        CaptureAllocationFault.ShadowCamera,
                        CaptureAllocationFault.ShadowLight,
                        CaptureAllocationFault.ShadowReceiver,
                        CaptureAllocationFault.ShadowCaster,
                        CaptureAllocationFault.ShadowTarget,
                        CaptureAllocationFault.ShadowReadback,
                    }
                )
                {
                    AssertCaptureAllocationFailure(
                        fault,
                        () =>
                            CaptureShadowSilhouette(
                                sourceMaterial,
                                TemporarySceneOwnership.RegularAdditive
                            )
                    );
                }

                Assert.That(
                    Shader.GetGlobalVector("unity_MetaVertexControl"),
                    Is.EqualTo(originalVertexControl)
                );
                Assert.That(
                    Shader.GetGlobalVector("unity_MetaFragmentControl"),
                    Is.EqualTo(originalFragmentControl)
                );
                Assert.That(
                    Shader.GetGlobalVector("unity_LightmapST"),
                    Is.EqualTo(originalLightmapSt)
                );
                Assert.That(
                    Shader.GetGlobalFloat("unity_OneOverOutputBoost"),
                    Is.EqualTo(originalOutputBoost)
                );
                Assert.That(
                    Shader.GetGlobalFloat("unity_MaxOutputValue"),
                    Is.EqualTo(originalMaximumOutput)
                );
            }
            finally
            {
                injectedCaptureAllocationFault = CaptureAllocationFault.None;
                state.Restore(validationScene, sceneWasLoaded, sceneWasDirty);
            }
        }

        /// <summary>Ensures a baseline without all four Meta observations cannot be accepted.</summary>
        [Test]
        public void BaselineRejectsMissingMetaObservations()
        {
            var baseline = new SceneRegressionBaseline
            {
                metaAlbedo = null,
                shadowChangedPixelCount = IntRange.Exact(
                    MinimumShadowChangedPixelCount + 1
                ),
            };

            Assert.Throws<AssertionException>(() =>
                ValidateBaselineObservability(baseline, "test baseline")
            );
        }

        /// <summary>Ensures a baseline with a zero Meta range cannot be accepted.</summary>
        [Test]
        public void BaselineRejectsZeroMetaLuminance()
        {
            SceneRegressionBaseline baseline = CreateObservableBaseline();
            baseline.metaAlbedo[0].meanLuminance = FloatRange.Exact(0.0f);

            Assert.Throws<AssertionException>(() =>
                ValidateBaselineObservability(baseline, "test baseline")
            );
        }

        /// <summary>Ensures a baseline with no changed shadow pixels cannot be accepted.</summary>
        [Test]
        public void BaselineRejectsZeroShadowSilhouette()
        {
            SceneRegressionBaseline baseline = CreateObservableBaseline();
            baseline.shadowChangedPixelCount = IntRange.Exact(0);

            Assert.Throws<AssertionException>(() =>
                ValidateBaselineObservability(baseline, "test baseline")
            );
        }

        /// <summary>Ensures successful temporary capture setup does not change the active scene dirty state.</summary>
        [Test]
        public void TemporaryCaptureScenePreservesDirtyStateOnSuccess()
        {
            AssertTemporaryCaptureScenePreservesDirtyState(false);
        }

        /// <summary>Ensures exceptional temporary capture cleanup does not change the active scene dirty state.</summary>
        [Test]
        public void TemporaryCaptureScenePreservesDirtyStateAfterException()
        {
            AssertTemporaryCaptureScenePreservesDirtyState(true);
        }

        /// <summary>Ensures a preloaded canonical scene retains its owner-specific settings after normal snapshot restoration.</summary>
        [Test]
        public void PreloadedCanonicalSceneRestoresOwnerSettingsOnSuccess()
        {
            AssertCanonicalSceneSnapshotRestoration(true, false);
        }

        /// <summary>Ensures a preloaded canonical scene retains its owner-specific settings after exceptional snapshot restoration.</summary>
        [Test]
        public void PreloadedCanonicalSceneRestoresOwnerSettingsAfterException()
        {
            AssertCanonicalSceneSnapshotRestoration(true, true);
        }

        /// <summary>Ensures a temporarily loaded canonical scene restores the original unloaded setup on normal completion.</summary>
        [Test]
        public void UnloadedCanonicalSceneRestoresOriginalSetupOnSuccess()
        {
            AssertCanonicalSceneSnapshotRestoration(false, false);
        }

        /// <summary>Ensures a temporarily loaded canonical scene restores the original unloaded setup after an exception.</summary>
        [Test]
        public void UnloadedCanonicalSceneRestoresOriginalSetupAfterException()
        {
            AssertCanonicalSceneSnapshotRestoration(false, true);
        }

        /// <summary>Validates the committed scene and reviewed baseline while restoring all editor state.</summary>
        [Test]
        public void CanonicalSceneMatchesCommittedBirpBaseline()
        {
            SceneRegressionBaseline baseline = LoadBaseline();
            EditorStateSnapshot state = EditorStateSnapshot.Capture();
            Scene validationScene = default;
            bool sceneWasLoaded = false;
            bool sceneWasDirty = false;

            try
            {
                ValidateRuntimeConfiguration();
                validationScene = SceneManager.GetSceneByPath(ScenePath);
                sceneWasLoaded = validationScene.isLoaded;
                if (!sceneWasLoaded)
                {
                    validationScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive
                    );
                }

                sceneWasDirty = validationScene.isDirty;
                Assert.That(
                    SceneManager.SetActiveScene(validationScene),
                    Is.True,
                    "The canonical validation scene could not become active."
                );
                SceneRegressionObservation observation = CaptureObservation(validationScene);
                AssertObservationMatchesBaseline(observation, baseline);
            }
            finally
            {
                state.Restore(validationScene, sceneWasLoaded, sceneWasDirty);
            }
        }

        /// <summary>Loads the reviewed baseline without creating or updating it.</summary>
        /// <returns>The validated baseline DTO.</returns>
        public static SceneRegressionBaseline LoadBaseline()
        {
            return LoadBaseline(BaselinePath);
        }

        /// <summary>Loads and validates a reviewed baseline at an explicit path without creating or updating it.</summary>
        /// <param name="baselinePath">The baseline path to read.</param>
        /// <returns>The validated baseline DTO.</returns>
        internal static SceneRegressionBaseline LoadBaseline(string baselinePath)
        {
            if (!File.Exists(baselinePath))
            {
                throw new AssertionException(
                    $"Missing committed scene regression baseline at '{baselinePath}'. Run the explicit PureBase/Tests/Regenerate Scene Baseline command in the approved environment; Daily never creates baselines."
                );
            }

            SceneRegressionBaseline baseline = JsonUtility.FromJson<SceneRegressionBaseline>(
                File.ReadAllText(baselinePath)
            );
            if (baseline == null || baseline.schemaVersion != BaselineSchemaVersion)
            {
                throw new AssertionException(
                    $"Baseline '{baselinePath}' must use schema version {BaselineSchemaVersion}."
                );
            }

            ValidateBaselineObservability(baseline, $"Baseline '{baselinePath}'");
            return baseline;
        }

        /// <summary>Rejects a baseline that cannot prove Meta or directional shadow rendering remains observable.</summary>
        /// <param name="baseline">The baseline to validate.</param>
        /// <param name="baselineLabel">The diagnostic baseline label.</param>
        public static void ValidateBaselineObservability(
            SceneRegressionBaseline baseline,
            string baselineLabel
        )
        {
            if (baseline == null)
                throw new AssertionException($"{baselineLabel} is missing.");
            if (
                baseline.metaAlbedo == null
                || baseline.metaAlbedo.Length != ProductShaderNames.Length
            )
            {
                throw new AssertionException(
                    $"{baselineLabel} must contain four reviewed Meta albedo observations."
                );
            }

            for (int index = 0; index < baseline.metaAlbedo.Length; index++)
            {
                MetaAlbedoBaseline meta = baseline.metaAlbedo[index];
                if (
                    meta == null
                    || meta.meanLuminance == null
                    || !IsFinite(meta.meanLuminance.minimum)
                    || !IsFinite(meta.meanLuminance.maximum)
                    || meta.meanLuminance.minimum <= MinimumMetaLuminance
                    || meta.meanLuminance.maximum < meta.meanLuminance.minimum
                )
                {
                    throw new AssertionException(
                        $"{baselineLabel} has an unobservable Meta luminance range at index {index}."
                    );
                }
            }

            if (
                baseline.shadowChangedPixelCount == null
                || baseline.shadowChangedPixelCount.minimum <= MinimumShadowChangedPixelCount
                || baseline.shadowChangedPixelCount.maximum
                    < baseline.shadowChangedPixelCount.minimum
            )
            {
                throw new AssertionException(
                    $"{baselineLabel} must contain a reviewed directional-shadow range above {MinimumShadowChangedPixelCount} changed pixels."
                );
            }
        }

        /// <summary>Validates the environment that the reviewed BIRP baseline describes.</summary>
        public static void ValidateRuntimeConfiguration()
        {
            Assert.That(
                Application.unityVersion,
                Is.EqualTo(ExpectedUnityVersion),
                $"The scene regression baseline requires Unity {ExpectedUnityVersion}."
            );
            Assert.That(
                GraphicsSettings.currentRenderPipeline,
                Is.Null,
                "The scene regression baseline requires the Built-in Render Pipeline."
            );
            Assert.That(
                SystemInfo.graphicsDeviceType,
                Is.EqualTo(GraphicsDeviceType.Direct3D11),
                "The scene regression baseline requires D3D11."
            );
            Assert.That(
                PlayerSettings.colorSpace,
                Is.EqualTo(ColorSpace.Linear),
                "The scene regression baseline requires Linear color space."
            );
        }

        /// <summary>Captures all numeric observations required by the read-only baseline contract.</summary>
        /// <param name="scene">The active canonical validation scene.</param>
        /// <returns>The captured observation.</returns>
        public static SceneRegressionObservation CaptureObservation(Scene scene)
        {
            ValidateFixture(scene);
            var observation = new SceneRegressionObservation();
            List<MeshRenderer> staticRenderers = GetStaticRenderers(scene);
            observation.staticLightmapCount =
                LightmapSettings.lightmaps == null ? 0 : LightmapSettings.lightmaps.Length;
            observation.staticRendererAssignmentCount = staticRenderers.Count;
            foreach (MeshRenderer renderer in staticRenderers)
            {
                Assert.That(
                    renderer.lightmapIndex,
                    Is.GreaterThanOrEqualTo(0),
                    $"Static renderer '{renderer.name}' is not assigned to a committed lightmap."
                );
            }

            CaptureSceneReadback(scene, observation);
            observation.metaAlbedo = CaptureMetaAlbedo(GetProductMaterials(scene));
            ShadowCaptureObservation shadow = CaptureShadowSilhouette(scene);
            observation.shadowChangedPixelCount = shadow.changedPixelCount;
            observation.shadowCoveragePixelCount = shadow.coveragePixelCount;
            observation.shadowCoverage = shadow.coverage;
            observation.shadowCentroidX = shadow.centroidX;
            observation.shadowCentroidY = shadow.centroidY;
            observation.shadowMaxAbsoluteRgbDelta = shadow.maxAbsoluteRgbDelta;
            observation.warmedVariantCount = WarmRepresentativeVariants();
            observation.dynamicLightmapStatus = "NOT_DETERMINISTIC_IN_BATCH_EDITMODE";
            return observation;
        }

        /// <summary>Compares each captured observable against its reviewed fixed value or range.</summary>
        /// <param name="observation">The current read-only observation.</param>
        /// <param name="baseline">The committed reviewed baseline.</param>
        public static void AssertObservationMatchesBaseline(
            SceneRegressionObservation observation,
            SceneRegressionBaseline baseline
        )
        {
            ValidateBaselineObservability(baseline, "Reviewed baseline");
            ValidateObservationObservability(observation, "Current observation");
            Assert.That(baseline.unityVersion, Is.EqualTo(ExpectedUnityVersion));
            Assert.That(
                baseline.graphicsDevice,
                Is.EqualTo(GraphicsDeviceType.Direct3D11.ToString())
            );
            Assert.That(baseline.colorSpace, Is.EqualTo(ColorSpace.Linear.ToString()));
            Assert.That(baseline.renderPipeline, Is.EqualTo("BuiltIn"));
            Assert.That(baseline.renderSize, Is.EqualTo(RenderSize));
            Assert.That(observation.staticLightmapCount, Is.EqualTo(baseline.staticLightmapCount));
            Assert.That(
                observation.staticRendererAssignmentCount,
                Is.EqualTo(baseline.staticRendererAssignmentCount)
            );
            // Hard-shadow rasterization differs slightly between physical GPUs and the
            // Microsoft Basic Render Driver used by GitHub-hosted runners. Keep the
            // reviewed bounds narrow so real silhouette regressions still fail.
            AssertRange(
                observation.shadowChangedPixelCount,
                baseline.shadowChangedPixelCount,
                "directional-shadow changed pixel count"
            );
            Assert.That(observation.warmedVariantCount, Is.EqualTo(baseline.warmedVariantCount));
            Assert.That(
                observation.dynamicLightmapStatus,
                Is.EqualTo("NOT_DETERMINISTIC_IN_BATCH_EDITMODE")
            );
            Assert.That(
                observation.sceneFinitePixelCount,
                Is.EqualTo(RenderSize * RenderSize),
                "The scene readback contains non-finite HDR values."
            );
            AssertRange(
                observation.sceneVisiblePixelCount,
                baseline.sceneVisiblePixelCount,
                "scene visible pixel count"
            );
            Assert.That(observation.metaAlbedo, Has.Length.EqualTo(baseline.metaAlbedo.Length));
            for (int index = 0; index < baseline.metaAlbedo.Length; index++)
            {
                MetaAlbedoBaseline expected = baseline.metaAlbedo[index];
                MetaAlbedoObservation actual = observation.metaAlbedo[index];
                Assert.That(actual.materialName, Is.EqualTo(expected.materialName));
                Assert.That(actual.shaderName, Is.EqualTo(expected.shaderName));
                AssertRange(
                    actual.meanLuminance,
                    expected.meanLuminance,
                    $"Meta luminance for '{actual.materialName}'"
                );
            }
        }

        /// <summary>Creates a baseline-shaped DTO from an observation using exact values pending human tolerance review.</summary>
        /// <param name="observation">The observation captured after an explicit regeneration bake.</param>
        /// <returns>A baseline DTO with zero-width numeric ranges.</returns>
        public static SceneRegressionBaseline CreateExactBaseline(
            SceneRegressionObservation observation
        )
        {
            ValidateObservationObservability(observation, "Regenerated observation");
            var baseline = new SceneRegressionBaseline
            {
                schemaVersion = BaselineSchemaVersion,
                unityVersion = ExpectedUnityVersion,
                graphicsDevice = GraphicsDeviceType.Direct3D11.ToString(),
                colorSpace = ColorSpace.Linear.ToString(),
                renderPipeline = "BuiltIn",
                renderSize = RenderSize,
                staticLightmapCount = observation.staticLightmapCount,
                staticRendererAssignmentCount = observation.staticRendererAssignmentCount,
                sceneVisiblePixelCount = IntRange.Exact(observation.sceneVisiblePixelCount),
                // Regeneration starts from an exact observation. Reviewers may widen
                // this range only after validating another supported D3D11 renderer.
                shadowChangedPixelCount = IntRange.Exact(
                    observation.shadowChangedPixelCount
                ),
                warmedVariantCount = observation.warmedVariantCount,
                dynamicLightmapStatus = "NOT_DETERMINISTIC_IN_BATCH_EDITMODE",
                metaAlbedo = new MetaAlbedoBaseline[observation.metaAlbedo.Length],
            };

            for (int index = 0; index < observation.metaAlbedo.Length; index++)
            {
                MetaAlbedoObservation meta = observation.metaAlbedo[index];
                baseline.metaAlbedo[index] = new MetaAlbedoBaseline
                {
                    materialName = meta.materialName,
                    shaderName = meta.shaderName,
                    meanLuminance = FloatRange.Exact(meta.meanLuminance),
                };
            }

            ValidateBaselineObservability(baseline, "Regenerated baseline");
            return baseline;
        }

        /// <summary>Rejects an observation that cannot serve as a rendering regression oracle.</summary>
        /// <param name="observation">The captured rendering observation.</param>
        /// <param name="observationLabel">The diagnostic observation label.</param>
        private static void ValidateObservationObservability(
            SceneRegressionObservation observation,
            string observationLabel
        )
        {
            if (
                observation == null
                || observation.metaAlbedo == null
                || observation.metaAlbedo.Length != ProductShaderNames.Length
            )
            {
                throw new AssertionException(
                    $"{observationLabel} must contain four Meta albedo observations."
                );
            }

            for (int index = 0; index < observation.metaAlbedo.Length; index++)
            {
                MetaAlbedoObservation meta = observation.metaAlbedo[index];
                if (
                    meta == null
                    || !IsFinite(meta.meanLuminance)
                    || meta.meanLuminance <= MinimumMetaLuminance
                )
                {
                    throw new AssertionException(
                        $"{observationLabel} has an unobservable Meta luminance at index {index}."
                    );
                }
            }

            if (observation.shadowChangedPixelCount <= MinimumShadowChangedPixelCount)
            {
                throw new AssertionException(
                    $"{observationLabel} must contain more than {MinimumShadowChangedPixelCount} changed directional-shadow pixels."
                );
            }
        }

        /// <summary>Validates the persistent scene, materials, and lighting settings without modifying them.</summary>
        /// <param name="scene">The canonical validation scene.</param>
        private static void ValidateFixture(Scene scene)
        {
            Assert.That(scene.IsValid(), Is.True, "The canonical validation scene is unavailable.");
            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                LightingSettingsPath
            );
            Assert.That(
                settings,
                Is.Not.Null,
                "The canonical Lighting Settings asset is unavailable."
            );
            Assert.That(Lightmapping.GetLightingSettingsForScene(scene), Is.SameAs(settings));
            Assert.That(
                settings.lightmapper,
                Is.EqualTo(LightingSettings.Lightmapper.ProgressiveCPU)
            );
            Assert.That(settings.bakedGI, Is.True);
            Assert.That(settings.realtimeGI, Is.False);
            Assert.That(settings.autoGenerate, Is.False);
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
            Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(1.0f).Within(0.0001f));
        }

        /// <summary>Gets every enabled static renderer with a persistent material assignment.</summary>
        /// <param name="scene">The canonical validation scene.</param>
        /// <returns>The static renderers in scene traversal order.</returns>
        private static List<MeshRenderer> GetStaticRenderers(Scene scene)
        {
            var renderers = new List<MeshRenderer>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (
                        renderer.enabled
                        && renderer.gameObject.isStatic
                        && renderer.sharedMaterial != null
                    )
                    {
                        renderers.Add(renderer);
                    }
                }
            }

            Assert.That(
                renderers,
                Is.Not.Empty,
                "The canonical validation scene has no static renderers."
            );
            return renderers;
        }

        /// <summary>Renders the scene through a temporary camera without changing the persisted camera target.</summary>
        /// <param name="scene">The canonical validation scene.</param>
        /// <param name="observation">The observation to populate.</param>
        private static void CaptureSceneReadback(
            Scene scene,
            SceneRegressionObservation observation
        )
        {
            Camera sourceCamera = FindSceneCamera(scene);
            using (var temporaryScene = new TemporaryCaptureScene())
            {
                GameObject cameraObject = temporaryScene.CreateGameObject(
                    "PureBase Daily Scene Readback Camera",
                    typeof(Camera)
                );
                Camera camera = cameraObject.GetComponent<Camera>();
                RenderTexture target = new RenderTexture(
                    RenderSize,
                    RenderSize,
                    24,
                    RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear
                );
                Texture2D readback = new Texture2D(
                    RenderSize,
                    RenderSize,
                    TextureFormat.RGBAFloat,
                    false,
                    true
                );
                try
                {
                    camera.CopyFrom(sourceCamera);
                    camera.enabled = false;
                    target.Create();
                    camera.targetTexture = target;
                    camera.Render();
                    Color[] pixels = ReadPixels(target, readback);
                    observation.sceneFinitePixelCount = CountFinitePixels(pixels);
                    observation.sceneVisiblePixelCount = CountVisiblePixels(pixels);
                    observation.sceneVisibleCoverage =
                        (float)observation.sceneVisiblePixelCount / pixels.Length;
                    CalculateVisibleCentroid(
                        pixels,
                        out observation.sceneVisibleCentroidX,
                        out observation.sceneVisibleCentroidY
                    );
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(readback);
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
            }
        }

        /// <summary>Captures a transient Meta albedo luminance observation for each product material.</summary>
        /// <param name="materials">The committed product materials.</param>
        /// <returns>The Meta observations in product shader order.</returns>
        private static MetaAlbedoObservation[] CaptureMetaAlbedo(IReadOnlyList<Material> materials)
        {
            var observations = new MetaAlbedoObservation[materials.Count];
            for (int index = 0; index < materials.Count; index++)
            {
                Material material = materials[index];
                observations[index] = new MetaAlbedoObservation
                {
                    materialName = material.name,
                    shaderName = material.shader.name,
                    meanLuminance = RenderMetaAlbedo(material),
                };
            }

            ValidateMetaAlbedoObservations(observations, "Captured Meta observation");
            return observations;
        }

        /// <summary>Rejects non-finite or black Meta capture results before they reach an observation DTO.</summary>
        /// <param name="observations">The transient Meta observations to inspect.</param>
        /// <param name="observationLabel">The diagnostic observation label.</param>
        private static void ValidateMetaAlbedoObservations(
            IReadOnlyList<MetaAlbedoObservation> observations,
            string observationLabel
        )
        {
            for (int index = 0; index < observations.Count; index++)
            {
                MetaAlbedoObservation observation = observations[index];
                Assert.That(observation, Is.Not.Null, $"{observationLabel} {index} is missing.");
                Assert.That(
                    IsFinite(observation.meanLuminance),
                    Is.True,
                    $"{observationLabel} for '{observation.materialName}' is non-finite."
                );
                Assert.That(
                    observation.meanLuminance,
                    Is.GreaterThan(MinimumMetaLuminance),
                    $"{observationLabel} for '{observation.materialName}' is not observable."
                );
            }
        }

        /// <summary>Renders the actual Meta pass into transient memory and calculates its mean luminance.</summary>
        /// <param name="sourceMaterial">The persistent material whose Meta pass is observed.</param>
        /// <returns>The mean RGB luminance.</returns>
        private static float RenderMetaAlbedo(Material sourceMaterial)
        {
            using (var resources = new CaptureResourceScope())
            using (var temporaryScene = new TemporaryCaptureScene())
            {
                Material material = null;
                Mesh mesh = null;
                GameObject cameraObject = null;
                RenderTexture target = null;
                Texture2D readback = null;
                Vector4 originalVertexControl = default;
                Vector4 originalFragmentControl = default;
                Vector4 originalLightmapSt = default;
                float originalOutputBoost = default;
                float originalMaximumOutput = default;
                bool metaGlobalsCaptured = false;
                try
                {
                    material = resources.Track(
                        new Material(sourceMaterial),
                        CaptureAllocationFault.MetaMaterial
                    );
                    mesh = resources.Track(CreateScreenMesh(), CaptureAllocationFault.MetaMesh);
                    cameraObject = resources.Track(
                        temporaryScene.CreateGameObject(
                            "PureBase Daily Meta Camera",
                            typeof(Camera)
                        ),
                        CaptureAllocationFault.MetaCamera
                    );
                    target = resources.Track(
                        new RenderTexture(
                            RenderSize,
                            RenderSize,
                            24,
                            RenderTextureFormat.ARGBHalf,
                            RenderTextureReadWrite.Linear
                        ),
                        CaptureAllocationFault.MetaTarget
                    );
                    readback = resources.Track(
                        new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true),
                        CaptureAllocationFault.MetaReadback
                    );
                    Camera camera = cameraObject.GetComponent<Camera>();
                    originalVertexControl = Shader.GetGlobalVector("unity_MetaVertexControl");
                    originalFragmentControl = Shader.GetGlobalVector("unity_MetaFragmentControl");
                    originalLightmapSt = Shader.GetGlobalVector("unity_LightmapST");
                    originalOutputBoost = Shader.GetGlobalFloat("unity_OneOverOutputBoost");
                    originalMaximumOutput = Shader.GetGlobalFloat("unity_MaxOutputValue");
                    metaGlobalsCaptured = true;
                    int pass = material.FindPass("Meta");
                    Assert.That(
                        pass,
                        Is.GreaterThanOrEqualTo(0),
                        $"Material '{sourceMaterial.name}' does not expose a Meta pass."
                    );
                    target.Create();
                    camera.enabled = false;
                    camera.cullingMask = 0;
                    camera.transform.position = new Vector3(0.0f, 0.0f, -5.0f);
                    camera.transform.rotation = Quaternion.identity;
                    camera.orthographic = true;
                    camera.orthographicSize = 1.0f;
                    camera.nearClipPlane = 0.1f;
                    camera.farClipPlane = 20.0f;
                    camera.targetTexture = target;
                    Shader.SetGlobalVector(
                        "unity_MetaVertexControl",
                        new Vector4(1.0f, 0.0f, 0.0f, 0.0f)
                    );
                    Shader.SetGlobalVector(
                        "unity_MetaFragmentControl",
                        new Vector4(1.0f, 0.0f, 0.0f, 0.0f)
                    );
                    Shader.SetGlobalVector("unity_LightmapST", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
                    Shader.SetGlobalFloat("unity_OneOverOutputBoost", 1.0f);
                    Shader.SetGlobalFloat("unity_MaxOutputValue", 1.0f);
                    var commandBuffer = new CommandBuffer { name = "PureBase Daily Meta Readback" };
                    try
                    {
                        commandBuffer.SetRenderTarget(target);
                        commandBuffer.ClearRenderTarget(true, true, Color.black);
                        commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
                        camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                        camera.Render();
                    }
                    finally
                    {
                        camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                        commandBuffer.Release();
                    }

                    Color[] pixels = ReadPixels(target, readback);
                    Assert.That(
                        CountFinitePixels(pixels),
                        Is.EqualTo(pixels.Length),
                        $"Material '{sourceMaterial.name}' produced non-finite Meta samples."
                    );
                    float luminance = 0.0f;
                    foreach (Color pixel in pixels)
                    {
                        luminance +=
                            (pixel.r * 0.2126f) + (pixel.g * 0.7152f) + (pixel.b * 0.0722f);
                    }

                    return luminance / pixels.Length;
                }
                finally
                {
                    if (metaGlobalsCaptured)
                    {
                        Shader.SetGlobalVector("unity_MetaVertexControl", originalVertexControl);
                        Shader.SetGlobalVector(
                            "unity_MetaFragmentControl",
                            originalFragmentControl
                        );
                        Shader.SetGlobalVector("unity_LightmapST", originalLightmapSt);
                        Shader.SetGlobalFloat("unity_OneOverOutputBoost", originalOutputBoost);
                        Shader.SetGlobalFloat("unity_MaxOutputValue", originalMaximumOutput);
                    }
                }
            }
        }

        /// <summary>Measures the transient actual ShadowCaster silhouette without changing fixture objects.</summary>
        /// <param name="scene">The canonical validation scene.</param>
        /// <returns>The complete directional shadow capture metrics.</returns>
        private static ShadowCaptureObservation CaptureShadowSilhouette(Scene scene)
        {
            ShadowCaptureObservation observation = CaptureShadowSilhouette(
                GetProductMaterials(scene)[0],
                TemporarySceneOwnership.RegularAdditive
            );
            Assert.That(
                observation.changedPixelCount,
                Is.GreaterThan(MinimumShadowChangedPixelCount),
                observation.Describe()
            );
            return observation;
        }

        /// <summary>Captures preview-scene and regular additive-scene shadow results for the same transient fixture.</summary>
        /// <param name="sourceMaterial">The persistent product material used by both captures.</param>
        /// <returns>The comparable capture results.</returns>
        private static ShadowCaptureComparison CaptureShadowComparison(Material sourceMaterial)
        {
            return new ShadowCaptureComparison(
                CaptureShadowSilhouette(sourceMaterial, TemporarySceneOwnership.Preview),
                CaptureShadowSilhouette(sourceMaterial, TemporarySceneOwnership.RegularAdditive)
            );
        }

        /// <summary>Captures a transient directional shadow fixture in one specified disposable scene kind.</summary>
        /// <param name="sourceMaterial">The persistent product material whose ShadowCaster pass is observed.</param>
        /// <param name="ownership">The disposable scene kind that owns the fixture.</param>
        /// <returns>The rendered shadow coverage and delta measurements.</returns>
        private static ShadowCaptureObservation CaptureShadowSilhouette(
            Material sourceMaterial,
            TemporarySceneOwnership ownership
        )
        {
            using (var resources = new CaptureResourceScope())
            using (var temporaryScene = new TemporaryCaptureScene(ownership))
            {
                Material casterMaterial = null;
                Material receiverMaterial = null;
                GameObject cameraObject = null;
                GameObject lightObject = null;
                GameObject receiver = null;
                GameObject caster = null;
                RenderTexture target = null;
                Texture2D readback = null;
                try
                {
                    casterMaterial = resources.Track(
                        new Material(sourceMaterial),
                        CaptureAllocationFault.ShadowCasterMaterial
                    );
                    receiverMaterial = resources.Track(
                        new Material(Shader.Find("Standard")),
                        CaptureAllocationFault.ShadowReceiverMaterial
                    );
                    cameraObject = resources.Track(
                        temporaryScene.CreateGameObject(
                            "PureBase Daily Shadow Camera",
                            typeof(Camera)
                        ),
                        CaptureAllocationFault.ShadowCamera
                    );
                    lightObject = resources.Track(
                        temporaryScene.CreateGameObject(
                            "PureBase Daily Shadow Light",
                            typeof(Light)
                        ),
                        CaptureAllocationFault.ShadowLight
                    );
                    receiver = resources.Track(
                        temporaryScene.CreatePrimitive(
                            "PureBase Daily Shadow Receiver",
                            PrimitiveType.Plane
                        ),
                        CaptureAllocationFault.ShadowReceiver
                    );
                    caster = resources.Track(
                        temporaryScene.CreatePrimitive(
                            "PureBase Daily Shadow Caster",
                            PrimitiveType.Cube
                        ),
                        CaptureAllocationFault.ShadowCaster
                    );
                    target = resources.Track(
                        new RenderTexture(
                            RenderSize,
                            RenderSize,
                            24,
                            RenderTextureFormat.ARGBHalf,
                            RenderTextureReadWrite.Linear
                        ),
                        CaptureAllocationFault.ShadowTarget
                    );
                    readback = resources.Track(
                        new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true),
                        CaptureAllocationFault.ShadowReadback
                    );
                    Camera camera = cameraObject.GetComponent<Camera>();
                    Light directionalLight = lightObject.GetComponent<Light>();
                    const int fixtureLayer = 31;
                    Assert.That(
                        receiverMaterial.shader,
                        Is.Not.Null,
                        "The Built-in Standard shader is unavailable for the shadow readback."
                    );
                    cameraObject.layer = fixtureLayer;
                    lightObject.layer = fixtureLayer;
                    receiver.layer = fixtureLayer;
                    caster.layer = fixtureLayer;
                    camera.enabled = false;
                    camera.cullingMask = 1 << fixtureLayer;
                    if (ownership == TemporarySceneOwnership.RegularAdditive)
                    {
                        camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(
                            temporaryScene.Scene
                        );
                    }
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1.0f);
                    camera.transform.position = new Vector3(0.0f, 3.0f, -7.0f);
                    camera.transform.LookAt(new Vector3(0.0f, 0.5f, 0.0f));
                    camera.fieldOfView = 45.0f;
                    directionalLight.type = LightType.Directional;
                    directionalLight.intensity = 1.5f;
                    directionalLight.shadows = LightShadows.Hard;
                    lightObject.transform.rotation = Quaternion.Euler(55.0f, -35.0f, 0.0f);
                    receiver.transform.localScale = Vector3.one * 0.8f;
                    receiver.GetComponent<MeshRenderer>().sharedMaterial = receiverMaterial;
                    caster.transform.position = new Vector3(0.0f, 1.0f, 0.0f);
                    caster.GetComponent<MeshRenderer>().sharedMaterial = casterMaterial;
                    caster.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.On;
                    target.Create();
                    camera.targetTexture = target;
                    directionalLight.shadows = LightShadows.None;
                    camera.Render();
                    Color[] withoutShadows = ReadPixels(target, readback);
                    directionalLight.shadows = LightShadows.Hard;
                    camera.Render();
                    Color[] withShadows = ReadPixels(target, readback);
                    return AnalyzeShadowObservation(
                        withoutShadows,
                        withShadows,
                        camera.backgroundColor
                    );
                }
                finally { }
            }
        }

        /// <summary>Warms the fourteen fixed BIRP variants for each product shader.</summary>
        /// <returns>The number of warmed representative variants.</returns>
        private static int WarmRepresentativeVariants()
        {
            var warmedCount = 0;
            foreach (string shaderName in ProductShaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, $"Product shader '{shaderName}' is unavailable.");
                Assert.That(
                    ShaderUtil.ShaderHasError(shader),
                    Is.False,
                    $"Product shader '{shaderName}' has compiler errors."
                );
                foreach (VariantRequest request in GetVariantRequests())
                {
                    var collection = new ShaderVariantCollection();
                    try
                    {
                        Assert.That(
                            collection.Add(
                                new ShaderVariantCollection.ShaderVariant(
                                    shader,
                                    request.pass,
                                    request.keywords
                                )
                            ),
                            Is.True,
                            $"Variant '{request.label}' could not be added for '{shaderName}'."
                        );
                        collection.WarmUp();
                        Assert.That(collection.variantCount, Is.EqualTo(1));
                        warmedCount++;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(collection);
                    }
                }
            }

            return warmedCount;
        }

        /// <summary>Finds the enabled canonical scene camera.</summary>
        /// <param name="scene">The scene to search.</param>
        /// <returns>The enabled camera.</returns>
        private static Camera FindSceneCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (camera.enabled)
                        return camera;
                }
            }

            throw new AssertionException("The canonical validation scene has no enabled camera.");
        }

        /// <summary>Gets the four committed product materials in fixed shader order.</summary>
        /// <param name="scene">The scene that owns the material assignments.</param>
        /// <returns>The product materials in shader order.</returns>
        private static List<Material> GetProductMaterials(Scene scene)
        {
            var byShader = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material material = renderer.sharedMaterial;
                    if (material != null && material.shader != null)
                    {
                        byShader[material.shader.name] = material;
                    }
                }
            }

            var materials = new List<Material>(ProductShaderNames.Length);
            foreach (string shaderName in ProductShaderNames)
            {
                Assert.That(
                    byShader.TryGetValue(shaderName, out Material material),
                    Is.True,
                    $"The canonical scene is missing material '{shaderName}'."
                );
                materials.Add(material);
            }

            return materials;
        }

        /// <summary>Builds the fixed screen quad used for transient Meta rendering.</summary>
        /// <returns>A transient mesh.</returns>
        private static Mesh CreateScreenMesh()
        {
            var legacyUv = new List<Vector4>
            {
                Vector2.zero,
                Vector2.up,
                Vector2.one,
                Vector2.right,
            };
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-1.0f, -1.0f),
                    new Vector3(-1.0f, 1.0f),
                    new Vector3(1.0f, 1.0f),
                    new Vector3(1.0f, -1.0f),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            mesh.SetUVs(0, legacyUv);
            mesh.SetUVs(1, legacyUv);
            mesh.SetUVs(2, legacyUv);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Reads a linear HDR render target into managed pixels.</summary>
        /// <param name="target">The source render target.</param>
        /// <param name="readback">The reusable texture used for readback.</param>
        /// <returns>The copied pixels.</returns>
        private static Color[] ReadPixels(RenderTexture target, Texture2D readback)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0);
                readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        /// <summary>Counts finite RGB samples.</summary>
        /// <param name="pixels">The samples to inspect.</param>
        /// <returns>The number of finite samples.</returns>
        private static int CountFinitePixels(Color[] pixels)
        {
            var count = 0;
            foreach (Color pixel in pixels)
            {
                if (
                    !float.IsNaN(pixel.r)
                    && !float.IsInfinity(pixel.r)
                    && !float.IsNaN(pixel.g)
                    && !float.IsInfinity(pixel.g)
                    && !float.IsNaN(pixel.b)
                    && !float.IsInfinity(pixel.b)
                )
                    count++;
            }

            return count;
        }

        /// <summary>Counts visible samples.</summary>
        /// <param name="pixels">The samples to inspect.</param>
        /// <returns>The number of visible samples.</returns>
        private static int CountVisiblePixels(Color[] pixels)
        {
            var count = 0;
            foreach (Color pixel in pixels)
            {
                if (pixel.maxColorComponent > 0.01f)
                    count++;
            }

            return count;
        }

        /// <summary>Calculates the normalized centroid of visible scene samples.</summary>
        /// <param name="pixels">The scene readback samples.</param>
        /// <param name="centroidX">Receives the horizontal normalized centroid.</param>
        /// <param name="centroidY">Receives the vertical normalized centroid.</param>
        private static void CalculateVisibleCentroid(
            Color[] pixels,
            out float centroidX,
            out float centroidY
        )
        {
            var visibleCount = 0;
            var totalX = 0.0f;
            var totalY = 0.0f;
            for (int index = 0; index < pixels.Length; index++)
            {
                if (pixels[index].maxColorComponent <= 0.01f)
                    continue;
                visibleCount++;
                totalX += index % RenderSize;
                totalY += index / RenderSize;
            }

            centroidX = visibleCount == 0 ? 0.0f : totalX / visibleCount / (RenderSize - 1);
            centroidY = visibleCount == 0 ? 0.0f : totalY / visibleCount / (RenderSize - 1);
        }

        /// <summary>Counts samples that change when the directional shadow is enabled.</summary>
        /// <param name="withoutShadows">The unshadowed samples.</param>
        /// <param name="withShadows">The shadowed samples.</param>
        /// <returns>The changed sample count.</returns>
        private static ShadowCaptureObservation AnalyzeShadowObservation(
            Color[] withoutShadows,
            Color[] withShadows,
            Color backgroundColor
        )
        {
            var changedPixelCount = 0;
            var coveragePixelCount = 0;
            var maxAbsoluteRgbDelta = 0.0f;
            var totalChangedX = 0.0f;
            var totalChangedY = 0.0f;
            for (int index = 0; index < withoutShadows.Length; index++)
            {
                Color delta = withoutShadows[index] - withShadows[index];
                float maximumAbsoluteDelta = Mathf.Max(
                    Mathf.Abs(delta.r),
                    Mathf.Max(Mathf.Abs(delta.g), Mathf.Abs(delta.b))
                );
                if (maximumAbsoluteDelta > 0.002f)
                {
                    changedPixelCount++;
                    totalChangedX += index % RenderSize;
                    totalChangedY += index / RenderSize;
                }
                if (maximumAbsoluteDelta > maxAbsoluteRgbDelta)
                    maxAbsoluteRgbDelta = maximumAbsoluteDelta;

                Color coverageDelta = withShadows[index] - backgroundColor;
                if (
                    Mathf.Max(
                        Mathf.Abs(coverageDelta.r),
                        Mathf.Max(Mathf.Abs(coverageDelta.g), Mathf.Abs(coverageDelta.b))
                    ) > 0.002f
                )
                    coveragePixelCount++;
            }

            float coverage = (float)coveragePixelCount / withoutShadows.Length;
            float centroidX =
                changedPixelCount == 0
                    ? 0.0f
                    : totalChangedX / changedPixelCount / (RenderSize - 1);
            float centroidY =
                changedPixelCount == 0
                    ? 0.0f
                    : totalChangedY / changedPixelCount / (RenderSize - 1);
            return new ShadowCaptureObservation(
                coveragePixelCount,
                coverage,
                centroidX,
                centroidY,
                maxAbsoluteRgbDelta,
                changedPixelCount
            );
        }

        /// <summary>Forces one allocation failure and verifies that the capture left no tracked resource or temporary scene behind.</summary>
        /// <param name="fault">The allocation point that must throw.</param>
        /// <param name="capture">The capture operation expected to fail.</param>
        private static void AssertCaptureAllocationFailure(
            CaptureAllocationFault fault,
            TestDelegate capture
        )
        {
            int originalSceneCount = SceneManager.sceneCount;
            lastCaptureResourcesReleased = false;
            injectedCaptureAllocationFault = fault;
            try
            {
                Assert.Throws<InvalidOperationException>(
                    capture,
                    $"Capture allocation fault '{fault}' did not throw."
                );
                Assert.That(
                    lastCaptureResourcesReleased,
                    Is.True,
                    $"Capture allocation fault '{fault}' leaked a native resource."
                );
                Assert.That(
                    SceneManager.sceneCount,
                    Is.EqualTo(originalSceneCount),
                    $"Capture allocation fault '{fault}' leaked a temporary scene."
                );
            }
            finally
            {
                injectedCaptureAllocationFault = CaptureAllocationFault.None;
            }
        }

        /// <summary>Throws immediately after a selected resource has been registered for cleanup.</summary>
        /// <param name="fault">The allocation point that completed.</param>
        private static void ThrowIfCaptureAllocationFaultInjected(CaptureAllocationFault fault)
        {
            if (injectedCaptureAllocationFault == fault)
            {
                throw new InvalidOperationException(
                    $"Simulated capture allocation failure at '{fault}'."
                );
            }
        }

        /// <summary>Determines whether a floating-point observation is finite.</summary>
        /// <param name="value">The value to inspect.</param>
        /// <returns><see langword="true"/> when the value is neither NaN nor infinite.</returns>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>Creates an in-memory baseline that satisfies only the rendering-observability invariant.</summary>
        /// <returns>An observable baseline for focused fail-closed tests.</returns>
        private static SceneRegressionBaseline CreateObservableBaseline()
        {
            var metaAlbedo = new MetaAlbedoBaseline[ProductShaderNames.Length];
            for (int index = 0; index < metaAlbedo.Length; index++)
            {
                metaAlbedo[index] = new MetaAlbedoBaseline
                {
                    meanLuminance = FloatRange.Exact(MinimumMetaLuminance + 0.001f),
                };
            }

            return new SceneRegressionBaseline
            {
                metaAlbedo = metaAlbedo,
                shadowChangedPixelCount = IntRange.Exact(
                    MinimumShadowChangedPixelCount + 1
                ),
            };
        }

        /// <summary>Exercises temporary capture isolation against a disposable active scene.</summary>
        /// <param name="throwInsideScope">Whether to simulate a capture exception after temporary object creation.</param>
        private static void AssertTemporaryCaptureScenePreservesDirtyState(bool throwInsideScope)
        {
            Scene sourceScene = SceneManager.GetActiveScene();
            bool sourceWasDirty = sourceScene.isDirty;
            Assert.That(sourceScene.IsValid(), Is.True);
            foreach (
                TemporarySceneOwnership ownership in new[]
                {
                    TemporarySceneOwnership.Preview,
                    TemporarySceneOwnership.RegularAdditive,
                }
            )
            {
                if (throwInsideScope)
                {
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        using (var temporaryScene = new TemporaryCaptureScene(ownership))
                        {
                            CreateTemporaryCaptureObjects(temporaryScene);
                            throw new InvalidOperationException(
                                "Simulated temporary capture failure."
                            );
                        }
                    });
                }
                else
                {
                    using (var temporaryScene = new TemporaryCaptureScene(ownership))
                    {
                        CreateTemporaryCaptureObjects(temporaryScene);
                    }
                }

                Assert.That(
                    SceneManager.GetActiveScene(),
                    Is.EqualTo(sourceScene),
                    $"Temporary {ownership} capture changed the active scene."
                );
                Assert.That(
                    sourceScene.isDirty,
                    Is.EqualTo(sourceWasDirty),
                    $"Temporary {ownership} capture changed the active scene dirty state."
                );
            }
        }

        /// <summary>Creates every temporary object shape used by Daily capture and verifies preview-scene ownership.</summary>
        /// <param name="temporaryScene">The disposable preview scene that must own every created object.</param>
        private static void CreateTemporaryCaptureObjects(TemporaryCaptureScene temporaryScene)
        {
            GameObject camera = temporaryScene.CreateGameObject(
                "PureBase Daily Temporary Capture Camera",
                typeof(Camera)
            );
            GameObject light = temporaryScene.CreateGameObject(
                "PureBase Daily Temporary Capture Light",
                typeof(Light)
            );
            GameObject plane = temporaryScene.CreatePrimitive(
                "PureBase Daily Temporary Capture Plane",
                PrimitiveType.Plane
            );
            GameObject cube = temporaryScene.CreatePrimitive(
                "PureBase Daily Temporary Capture Cube",
                PrimitiveType.Cube
            );
            Assert.That(camera.scene, Is.EqualTo(temporaryScene.Scene));
            Assert.That(light.scene, Is.EqualTo(temporaryScene.Scene));
            Assert.That(plane.scene, Is.EqualTo(temporaryScene.Scene));
            Assert.That(cube.scene, Is.EqualTo(temporaryScene.Scene));
        }

        /// <summary>Exercises the snapshot restore path with the canonical scene initially loaded or unloaded.</summary>
        /// <param name="canonicalPreloaded">Whether the canonical scene is loaded before capturing the snapshot.</param>
        /// <param name="throwInsideScope">Whether to simulate an observation failure before restoration.</param>
        private static void AssertCanonicalSceneSnapshotRestoration(
            bool canonicalPreloaded,
            bool throwInsideScope
        )
        {
            SceneSetup[] originalSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            Scene validationScene = SceneManager.GetSceneByPath(ScenePath);
            try
            {
                Scene ownerScene = GetOrOpenPersistedOwnerScene();
                string ownerScenePath = ownerScene.path;
                Assert.That(
                    SceneManager.SetActiveScene(ownerScene),
                    Is.True,
                    "The persisted owner scene could not become active before the canonical scene state is prepared."
                );
                if (canonicalPreloaded && !validationScene.isLoaded)
                {
                    validationScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive
                    );
                }
                else if (!canonicalPreloaded && validationScene.isLoaded)
                {
                    Assert.That(
                        validationScene.isDirty,
                        Is.False,
                        "The unloaded-scene restoration test cannot discard a dirty canonical fixture."
                    );
                    Assert.That(
                        EditorSceneManager.CloseScene(validationScene, true),
                        Is.True,
                        "The canonical validation scene could not be closed before snapshot capture."
                    );
                    validationScene = SceneManager.GetSceneByPath(ScenePath);
                    Assert.That(
                        validationScene.isLoaded,
                        Is.False,
                        "The canonical validation scene remained loaded before the unloaded-scene snapshot was captured."
                    );
                }

                if (!canonicalPreloaded)
                    Assert.That(
                        validationScene.isLoaded,
                        Is.False,
                        "The canonical validation scene must be unloaded before the unloaded-scene snapshot is captured."
                    );

                bool ownerWasDirty = ownerScene.isDirty;
                bool validationWasDirty = validationScene.isLoaded && validationScene.isDirty;
                AmbientMode validationAmbientMode = default;
                Color validationAmbientLight = default;
                float validationAmbientIntensity = default;
                LightmapData[] validationLightmaps = null;
                if (canonicalPreloaded)
                {
                    Assert.That(
                        SceneManager.SetActiveScene(validationScene),
                        Is.True,
                        "The preloaded canonical scene could not become active before snapshot capture."
                    );
                    validationAmbientMode = RenderSettings.ambientMode;
                    validationAmbientLight = RenderSettings.ambientLight;
                    validationAmbientIntensity = RenderSettings.ambientIntensity;
                    validationLightmaps = LightmapSettings.lightmaps;
                    Assert.That(
                        SceneManager.SetActiveScene(ownerScene),
                        Is.True,
                        "The persisted owner scene could not be restored as active before snapshot capture."
                    );
                }

                EditorStateSnapshot snapshot = EditorStateSnapshot.Capture();
                if (!validationScene.isLoaded)
                {
                    validationScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive
                    );
                }

                if (throwInsideScope)
                {
                    Assert.Throws<InvalidOperationException>(() =>
                    {
                        try
                        {
                            Assert.That(
                                SceneManager.SetActiveScene(validationScene),
                                Is.True,
                                "The canonical scene could not become active before exceptional restoration."
                            );
                            MutateSceneOwnedLightingSettings();
                            throw new InvalidOperationException(
                                "Simulated canonical scene observation failure."
                            );
                        }
                        finally
                        {
                            snapshot.Restore(
                                validationScene,
                                canonicalPreloaded,
                                validationWasDirty
                            );
                        }
                    });
                }
                else
                {
                    Assert.That(
                        SceneManager.SetActiveScene(validationScene),
                        Is.True,
                        "The canonical scene could not become active before normal restoration."
                    );
                    MutateSceneOwnedLightingSettings();
                    snapshot.Restore(validationScene, canonicalPreloaded, validationWasDirty);
                }

                Scene restoredOwnerScene = SceneManager.GetSceneByPath(ownerScenePath);
                Assert.That(
                    SceneManager.GetActiveScene().path,
                    Is.EqualTo(restoredOwnerScene.path)
                );
                Assert.That(restoredOwnerScene.isDirty, Is.EqualTo(ownerWasDirty));
                validationScene = SceneManager.GetSceneByPath(ScenePath);
                Assert.That(validationScene.isLoaded, Is.EqualTo(canonicalPreloaded));
                if (canonicalPreloaded)
                {
                    Assert.That(validationScene.isDirty, Is.EqualTo(validationWasDirty));
                    Assert.That(
                        SceneManager.SetActiveScene(validationScene),
                        Is.True,
                        "The restored preloaded canonical scene could not become active for verification."
                    );
                    Assert.That(RenderSettings.ambientMode, Is.EqualTo(validationAmbientMode));
                    Assert.That(RenderSettings.ambientLight, Is.EqualTo(validationAmbientLight));
                    Assert.That(
                        RenderSettings.ambientIntensity,
                        Is.EqualTo(validationAmbientIntensity)
                    );
                    Assert.That(
                        LightmapsMatch(LightmapSettings.lightmaps, validationLightmaps),
                        Is.True
                    );
                }

                UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (originalSceneSetup != null && originalSceneSetup.Length > 0)
                {
                    EditorSceneManager.RestoreSceneManagerSetup(originalSceneSetup);
                }
            }
        }

        /// <summary>Gets a loaded saved scene that can own active-scene settings during an isolated restoration test.</summary>
        /// <returns>A loaded non-canonical scene.</returns>
        private static Scene GetOrOpenPersistedOwnerScene()
        {
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (
                    scene.isLoaded
                    && !string.IsNullOrEmpty(scene.path)
                    && !string.Equals(scene.path, ScenePath, StringComparison.Ordinal)
                )
                    return scene;
            }

            return EditorSceneManager.OpenScene(TestOwnerScenePath, OpenSceneMode.Additive);
        }

        /// <summary>Changes the active scene's lighting state so snapshot restoration must reapply its captured values.</summary>
        private static void MutateSceneOwnedLightingSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientLight = Color.magenta;
            RenderSettings.ambientIntensity = 0.25f;
            LightmapSettings.lightmaps = Array.Empty<LightmapData>();
        }

        /// <summary>Compares lightmap arrays without assigning identical scene-owned state.</summary>
        /// <param name="left">The current scene lightmaps.</param>
        /// <param name="right">The expected scene lightmaps.</param>
        /// <returns><see langword="true"/> when every lightmap reference is unchanged.</returns>
        private static bool LightmapsMatch(LightmapData[] left, LightmapData[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int index = 0; index < left.Length; index++)
            {
                LightmapData leftData = left[index];
                LightmapData rightData = right[index];
                if (ReferenceEquals(leftData, rightData))
                    continue;
                if (
                    leftData == null
                    || rightData == null
                    || leftData.lightmapColor != rightData.lightmapColor
                    || leftData.lightmapDir != rightData.lightmapDir
                    || leftData.shadowMask != rightData.shadowMask
                )
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Asserts that an integer observation is inside the reviewed inclusive range.</summary>
        /// <param name="actual">The observed value.</param>
        /// <param name="range">The reviewed range.</param>
        /// <param name="label">The failing observable label.</param>
        private static void AssertRange(int actual, IntRange range, string label)
        {
            Assert.That(range, Is.Not.Null, $"Baseline has no range for {label}.");
            Assert.That(
                actual,
                Is.InRange(range.minimum, range.maximum),
                $"{label} was {actual}, outside [{range.minimum}, {range.maximum}]."
            );
        }

        /// <summary>Asserts that a floating-point observation is inside the reviewed inclusive range.</summary>
        /// <param name="actual">The observed value.</param>
        /// <param name="range">The reviewed range.</param>
        /// <param name="label">The failing observable label.</param>
        private static void AssertRange(float actual, FloatRange range, string label)
        {
            Assert.That(range, Is.Not.Null, $"Baseline has no range for {label}.");
            Assert.That(
                actual,
                Is.InRange(range.minimum, range.maximum),
                $"{label} was {actual}, outside [{range.minimum}, {range.maximum}]."
            );
        }

        /// <summary>Returns the fixed representative BIRP variant requests.</summary>
        /// <returns>The fourteen variant requests.</returns>
        private static VariantRequest[] GetVariantRequests()
        {
            return new[]
            {
                new VariantRequest(
                    "ForwardBase default",
                    PassType.ForwardBase,
                    Array.Empty<string>()
                ),
                new VariantRequest("ForwardBase fog", PassType.ForwardBase, new[] { "FOG_LINEAR" }),
                new VariantRequest(
                    "ForwardBase instancing",
                    PassType.ForwardBase,
                    new[] { "INSTANCING_ON" }
                ),
                new VariantRequest(
                    "ForwardBase lightmap",
                    PassType.ForwardBase,
                    new[] { "LIGHTMAP_ON" }
                ),
                new VariantRequest(
                    "ForwardBase directional-lightmap",
                    PassType.ForwardBase,
                    new[] { "LIGHTMAP_ON", "DIRLIGHTMAP_COMBINED" }
                ),
                new VariantRequest(
                    "ForwardBase dynamic-lightmap",
                    PassType.ForwardBase,
                    new[] { "DYNAMICLIGHTMAP_ON" }
                ),
                new VariantRequest(
                    "ForwardBase shadowmask",
                    PassType.ForwardBase,
                    new[] { "LIGHTMAP_ON", "SHADOWS_SHADOWMASK" }
                ),
                new VariantRequest(
                    "ForwardAdd directional",
                    PassType.ForwardAdd,
                    new[] { "DIRECTIONAL" }
                ),
                new VariantRequest("ForwardAdd point", PassType.ForwardAdd, new[] { "POINT" }),
                new VariantRequest("ForwardAdd spot", PassType.ForwardAdd, new[] { "SPOT" }),
                new VariantRequest(
                    "ForwardAdd full shadows",
                    PassType.ForwardAdd,
                    new[] { "SPOT", "SHADOWS_DEPTH" }
                ),
                new VariantRequest(
                    "ShadowCaster instancing",
                    PassType.ShadowCaster,
                    new[] { "INSTANCING_ON" }
                ),
                new VariantRequest(
                    "ShadowCaster cutout",
                    PassType.ShadowCaster,
                    Array.Empty<string>()
                ),
                new VariantRequest("Meta bake", PassType.Meta, Array.Empty<string>()),
            };
        }

        /// <summary>Stores the persistent and global editor state that Daily temporarily changes.</summary>
        private sealed class EditorStateSnapshot
        {
            private readonly SceneSetup[] sceneSetup;
            private readonly Scene activeScene;
            private readonly string activeScenePath;
            private readonly SceneLightingState[] sceneLightingStates;
            private readonly SceneDirtyState[] sceneDirtyStates;

            private EditorStateSnapshot(
                SceneSetup[] sceneSetup,
                Scene activeScene,
                string activeScenePath,
                SceneLightingState[] sceneLightingStates,
                SceneDirtyState[] sceneDirtyStates
            )
            {
                this.sceneSetup = sceneSetup;
                this.activeScene = activeScene;
                this.activeScenePath = activeScenePath;
                this.sceneLightingStates = sceneLightingStates;
                this.sceneDirtyStates = sceneDirtyStates;
            }

            /// <summary>Captures the global state before a Daily scene observation.</summary>
            /// <returns>The captured state.</returns>
            public static EditorStateSnapshot Capture()
            {
                var dirtyStates = new List<SceneDirtyState>();
                var lightingStates = new List<SceneLightingState>();
                Scene activeScene = SceneManager.GetActiveScene();
                for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                {
                    Scene scene = SceneManager.GetSceneAt(sceneIndex);
                    if (!scene.isLoaded)
                        continue;
                    dirtyStates.Add(new SceneDirtyState(scene, scene.isDirty));
                    if (!SceneManager.SetActiveScene(scene))
                        continue;
                    lightingStates.Add(SceneLightingState.Capture(scene));
                }

                if (activeScene.IsValid() && activeScene.isLoaded)
                    SceneManager.SetActiveScene(activeScene);
                return new EditorStateSnapshot(
                    EditorSceneManager.GetSceneManagerSetup(),
                    activeScene,
                    activeScene.path,
                    lightingStates.ToArray(),
                    dirtyStates.ToArray()
                );
            }

            /// <summary>Restores all captured state and verifies that the fixture's original dirty state remains unchanged.</summary>
            /// <param name="validationScene">The scene observed by Daily.</param>
            /// <param name="sceneWasLoaded">Whether the scene was already loaded before the test.</param>
            /// <param name="sceneWasDirty">The fixture dirty state observed before the test.</param>
            public void Restore(Scene validationScene, bool sceneWasLoaded, bool sceneWasDirty)
            {
                string validationScenePath = validationScene.path;
                RestoreSceneSetup(validationScenePath, sceneWasLoaded);
                RestoreSceneLightingSettings();
                Scene restoredActiveScene = ResolveScene(activeScene, activeScenePath);
                if (restoredActiveScene.IsValid() && restoredActiveScene.isLoaded)
                    SceneManager.SetActiveScene(restoredActiveScene);
                RestoreDirtyStates();
                Scene restoredValidationScene = SceneManager.GetSceneByPath(validationScenePath);
                if (
                    sceneWasLoaded
                    && restoredValidationScene.IsValid()
                    && restoredValidationScene.isLoaded
                )
                    Assert.That(
                        restoredValidationScene.isDirty,
                        Is.EqualTo(sceneWasDirty),
                        "Daily changed the canonical fixture dirty state."
                    );
            }

            /// <summary>Restores saved scene layouts or preserves untitled user scenes while unloading a Daily-opened canonical scene.</summary>
            /// <param name="validationScenePath">The canonical scene path Daily observed.</param>
            /// <param name="sceneWasLoaded">Whether the canonical scene was loaded when the snapshot was captured.</param>
            private void RestoreSceneSetup(string validationScenePath, bool sceneWasLoaded)
            {
                if (sceneSetup == null || sceneSetup.Length == 0)
                    return;
                if (!ContainsUntitledScene())
                {
                    EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                    CloseTemporarilyLoadedValidationScene(validationScenePath, sceneWasLoaded);
                    return;
                }

                CloseTemporarilyLoadedValidationScene(validationScenePath, sceneWasLoaded);
            }

            /// <summary>Closes a canonical scene that Daily loaded after capture so the original scene layout remains intact.</summary>
            /// <param name="validationScenePath">The canonical scene path Daily observed.</param>
            /// <param name="sceneWasLoaded">Whether the canonical scene was loaded when the snapshot was captured.</param>
            private void CloseTemporarilyLoadedValidationScene(
                string validationScenePath,
                bool sceneWasLoaded
            )
            {
                if (sceneWasLoaded || string.IsNullOrEmpty(validationScenePath))
                    return;
                Scene restoredValidationScene = SceneManager.GetSceneByPath(validationScenePath);
                if (!restoredValidationScene.IsValid() || !restoredValidationScene.isLoaded)
                    return;
                Scene restoredActiveScene = ResolveScene(activeScene, activeScenePath);
                if (restoredActiveScene.IsValid() && restoredActiveScene.isLoaded)
                    SceneManager.SetActiveScene(restoredActiveScene);
                EditorSceneManager.CloseScene(restoredValidationScene, true);
            }

            /// <summary>Determines whether Unity cannot safely restore this scene setup because it contains an untitled scene.</summary>
            /// <returns><see langword="true"/> when manual restoration is required.</returns>
            private bool ContainsUntitledScene()
            {
                foreach (SceneSetup setup in sceneSetup)
                {
                    if (string.IsNullOrEmpty(setup.path))
                        return true;
                }

                return false;
            }

            /// <summary>Restores every captured scene's lighting settings while that scene owns the active-scene context.</summary>
            private void RestoreSceneLightingSettings()
            {
                foreach (SceneLightingState lightingState in sceneLightingStates)
                {
                    Scene scene = lightingState.GetRestoredScene();
                    if (!scene.IsValid() || !scene.isLoaded)
                        continue;
                    if (!SceneManager.SetActiveScene(scene))
                        continue;
                    lightingState.Restore();
                }
            }

            /// <summary>Preserves dirty scenes and rejects any clean scene that Daily would have dirtied.</summary>
            private void RestoreDirtyStates()
            {
                foreach (SceneDirtyState dirtyState in sceneDirtyStates)
                {
                    Scene scene = dirtyState.GetRestoredScene();
                    if (!scene.IsValid() || !scene.isLoaded)
                        continue;
                    if (dirtyState.wasDirty && !scene.isDirty)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                    }
                    else if (!dirtyState.wasDirty)
                    {
                        Assert.That(
                            scene.isDirty,
                            Is.False,
                            $"Daily changed the dirty state of scene '{scene.path}'."
                        );
                    }
                }
            }

            /// <summary>Finds a saved scene's current instance after Unity restores a scene manager setup.</summary>
            /// <param name="scene">The scene captured before restoration.</param>
            /// <param name="scenePath">The captured persistent path, if any.</param>
            /// <returns>The current scene instance.</returns>
            private static Scene ResolveScene(Scene scene, string scenePath)
            {
                return string.IsNullOrEmpty(scenePath)
                    ? scene
                    : SceneManager.GetSceneByPath(scenePath);
            }

            /// <summary>Stores one loaded scene's dirty state before Daily observation.</summary>
            private sealed class SceneDirtyState
            {
                /// <summary>Initializes one scene dirty-state record.</summary>
                /// <param name="scene">The loaded scene.</param>
                /// <param name="wasDirty">Whether the scene was dirty before observation.</param>
                public SceneDirtyState(Scene scene, bool wasDirty)
                {
                    this.scene = scene;
                    scenePath = scene.path;
                    this.wasDirty = wasDirty;
                }

                /// <summary>Gets the loaded scene.</summary>
                public Scene scene { get; }

                private string scenePath { get; }

                /// <summary>Gets whether the scene was dirty before observation.</summary>
                public bool wasDirty { get; }

                /// <summary>Gets the current scene instance after a possible scene setup restoration.</summary>
                /// <returns>The restored scene instance.</returns>
                public Scene GetRestoredScene()
                {
                    return ResolveScene(scene, scenePath);
                }
            }

            /// <summary>Stores one loaded scene's active-context RenderSettings and LightmapSettings values.</summary>
            private sealed class SceneLightingState
            {
                private SceneLightingState(
                    Scene scene,
                    AmbientMode ambientMode,
                    Color ambientLight,
                    float ambientIntensity,
                    LightmapData[] lightmaps
                )
                {
                    this.scene = scene;
                    scenePath = scene.path;
                    this.ambientMode = ambientMode;
                    this.ambientLight = ambientLight;
                    this.ambientIntensity = ambientIntensity;
                    this.lightmaps = lightmaps;
                }

                /// <summary>Gets the scene that owns these active-context settings.</summary>
                public Scene scene { get; }

                private string scenePath { get; }

                private AmbientMode ambientMode { get; }

                private Color ambientLight { get; }

                private float ambientIntensity { get; }

                private LightmapData[] lightmaps { get; }

                /// <summary>Captures the settings exposed while <paramref name="scene"/> is active.</summary>
                /// <param name="scene">The active scene that owns the settings.</param>
                /// <returns>The captured scene-owned settings.</returns>
                public static SceneLightingState Capture(Scene scene)
                {
                    return new SceneLightingState(
                        scene,
                        RenderSettings.ambientMode,
                        RenderSettings.ambientLight,
                        RenderSettings.ambientIntensity,
                        LightmapSettings.lightmaps
                    );
                }

                /// <summary>Gets the current scene instance after a possible scene setup restoration.</summary>
                /// <returns>The restored scene instance.</returns>
                public Scene GetRestoredScene()
                {
                    return ResolveScene(scene, scenePath);
                }

                /// <summary>Restores the captured settings while the owning scene is active.</summary>
                public void Restore()
                {
                    if (RenderSettings.ambientMode != ambientMode)
                        RenderSettings.ambientMode = ambientMode;
                    if (RenderSettings.ambientLight != ambientLight)
                        RenderSettings.ambientLight = ambientLight;
                    if (!Mathf.Approximately(RenderSettings.ambientIntensity, ambientIntensity))
                        RenderSettings.ambientIntensity = ambientIntensity;
                    if (
                        !PureBaseValidationSceneRegressionTests.LightmapsMatch(
                            LightmapSettings.lightmaps,
                            lightmaps
                        )
                    )
                        LightmapSettings.lightmaps = lightmaps;
                }
            }
        }

        /// <summary>Owns a disposable preview scene for temporary capture objects so canonical scenes remain untouched.</summary>
        private sealed class TemporaryCaptureScene : IDisposable
        {
            private readonly Scene scene;
            private readonly Scene activeScene;
            private readonly TemporarySceneOwnership ownership;

            /// <summary>Creates a preview scene for one temporary capture without changing the active scene.</summary>
            public TemporaryCaptureScene()
                : this(TemporarySceneOwnership.Preview) { }

            /// <summary>Creates an isolated preview or regular additive scene for one temporary capture.</summary>
            /// <param name="ownership">The disposable scene kind that owns temporary objects.</param>
            public TemporaryCaptureScene(TemporarySceneOwnership ownership)
            {
                activeScene = SceneManager.GetActiveScene();
                this.ownership = ownership;
                if (ownership == TemporarySceneOwnership.Preview)
                {
                    scene = EditorSceneManager.NewPreviewScene();
                    return;
                }

                scene = EditorSceneManager.OpenScene(TestOwnerScenePath, OpenSceneMode.Additive);
                EditorSceneManager.SetSceneCullingMask(
                    scene,
                    EditorSceneManager.CalculateAvailableSceneCullingMask()
                );
            }

            /// <summary>Gets the preview scene that owns the temporary capture objects.</summary>
            public Scene Scene => scene;

            /// <summary>Creates a hidden, non-saving temporary GameObject in the preview scene.</summary>
            /// <param name="name">The temporary object name.</param>
            /// <param name="components">The component types to add.</param>
            /// <returns>The preview-scene GameObject.</returns>
            public GameObject CreateGameObject(string name, params Type[] components)
            {
                GameObject gameObject = EditorUtility.CreateGameObjectWithHideFlags(
                    name,
                    HideFlags.HideAndDontSave,
                    components
                );
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                return gameObject;
            }

            /// <summary>Creates a primitive-renderer equivalent without adding a persistent object to the active scene.</summary>
            /// <param name="name">The temporary primitive name.</param>
            /// <param name="primitiveType">The built-in mesh shape to assign.</param>
            /// <returns>The preview-scene primitive object.</returns>
            public GameObject CreatePrimitive(string name, PrimitiveType primitiveType)
            {
                string meshName =
                    primitiveType == PrimitiveType.Plane ? "New-Plane.fbx" : "Cube.fbx";
                Mesh mesh = Resources.GetBuiltinResource<Mesh>(meshName);
                if (mesh == null)
                    throw new InvalidOperationException(
                        $"The built-in '{primitiveType}' mesh is unavailable for Daily capture."
                    );

                GameObject gameObject = CreateGameObject(
                    name,
                    typeof(MeshFilter),
                    typeof(MeshRenderer)
                );
                gameObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                return gameObject;
            }

            /// <summary>Destroys all temporary preview-scene objects without changing the active scene.</summary>
            public void Dispose()
            {
                if (!scene.IsValid() || !scene.isLoaded)
                    return;
                if (ownership == TemporarySceneOwnership.Preview)
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                    return;
                }

                if (activeScene.IsValid() && activeScene.isLoaded)
                    SceneManager.SetActiveScene(activeScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Defines the disposable scene type that owns transient render-capture objects.</summary>
        private enum TemporarySceneOwnership
        {
            /// <summary>Uses Unity's preview-scene rendering context.</summary>
            Preview,

            /// <summary>Uses a regular empty additive scene, matching the legacy shadow harness context.</summary>
            RegularAdditive,
        }

        /// <summary>Identifies a transient resource acquisition that focused tests can interrupt.</summary>
        private enum CaptureAllocationFault
        {
            /// <summary>Does not inject a failure.</summary>
            None,

            /// <summary>Interrupts Meta material allocation.</summary>
            MetaMaterial,

            /// <summary>Interrupts Meta mesh allocation.</summary>
            MetaMesh,

            /// <summary>Interrupts Meta camera allocation.</summary>
            MetaCamera,

            /// <summary>Interrupts Meta render-target allocation.</summary>
            MetaTarget,

            /// <summary>Interrupts Meta readback allocation.</summary>
            MetaReadback,

            /// <summary>Interrupts Shadow caster-material allocation.</summary>
            ShadowCasterMaterial,

            /// <summary>Interrupts Shadow receiver-material allocation.</summary>
            ShadowReceiverMaterial,

            /// <summary>Interrupts Shadow camera allocation.</summary>
            ShadowCamera,

            /// <summary>Interrupts Shadow light allocation.</summary>
            ShadowLight,

            /// <summary>Interrupts Shadow receiver allocation.</summary>
            ShadowReceiver,

            /// <summary>Interrupts Shadow caster allocation.</summary>
            ShadowCaster,

            /// <summary>Interrupts Shadow render-target allocation.</summary>
            ShadowTarget,

            /// <summary>Interrupts Shadow readback allocation.</summary>
            ShadowReadback,
        }

        /// <summary>Tracks transient Unity objects from their first allocation and destroys them in reverse acquisition order.</summary>
        private sealed class CaptureResourceScope : IDisposable
        {
            private readonly List<UnityEngine.Object> resources = new List<UnityEngine.Object>();

            /// <summary>Registers one transient resource before optional fault injection.</summary>
            /// <typeparam name="T">The Unity object type.</typeparam>
            /// <param name="resource">The newly allocated resource.</param>
            /// <param name="fault">The allocation point associated with the resource.</param>
            /// <returns>The tracked resource.</returns>
            public T Track<T>(T resource, CaptureAllocationFault fault)
                where T : UnityEngine.Object
            {
                resources.Add(resource);
                ThrowIfCaptureAllocationFaultInjected(fault);
                return resource;
            }

            /// <summary>Releases GPU render targets and destroys all tracked Unity objects.</summary>
            public void Dispose()
            {
                bool released = true;
                for (int index = resources.Count - 1; index >= 0; index--)
                {
                    UnityEngine.Object resource = resources[index];
                    if (resource is RenderTexture target)
                        target.Release();
                    UnityEngine.Object.DestroyImmediate(resource);
                    released &= !resource;
                }

                lastCaptureResourcesReleased = released;
            }
        }

        /// <summary>Stores measurable directional-shadow results from one disposable scene context.</summary>
        private sealed class ShadowCaptureObservation
        {
            /// <summary>Initializes one shadow capture measurement.</summary>
            /// <param name="coveragePixelCount">The number of pixels covered by transient geometry.</param>
            /// <param name="maxAbsoluteRgbDelta">The greatest RGB delta between unshadowed and shadowed rendering.</param>
            /// <param name="changedPixelCount">The number of pixels changed by directional shadowing.</param>
            public ShadowCaptureObservation(
                int coveragePixelCount,
                float coverage,
                float centroidX,
                float centroidY,
                float maxAbsoluteRgbDelta,
                int changedPixelCount
            )
            {
                this.coveragePixelCount = coveragePixelCount;
                this.coverage = coverage;
                this.centroidX = centroidX;
                this.centroidY = centroidY;
                this.maxAbsoluteRgbDelta = maxAbsoluteRgbDelta;
                this.changedPixelCount = changedPixelCount;
            }

            /// <summary>Gets the number of pixels covered by transient geometry.</summary>
            public int coveragePixelCount { get; }

            /// <summary>Gets the normalized receiver coverage.</summary>
            public float coverage { get; }

            /// <summary>Gets the horizontal normalized centroid of changed pixels.</summary>
            public float centroidX { get; }

            /// <summary>Gets the vertical normalized centroid of changed pixels.</summary>
            public float centroidY { get; }

            /// <summary>Gets the largest observed absolute RGB delta.</summary>
            public float maxAbsoluteRgbDelta { get; }

            /// <summary>Gets the number of changed shadow pixels.</summary>
            public int changedPixelCount { get; }

            /// <summary>Formats the capture measurements for assertion diagnostics.</summary>
            /// <returns>The diagnostic measurement text.</returns>
            public string Describe() =>
                $"coverage={coveragePixelCount}, maxAbsoluteRgbDelta={maxAbsoluteRgbDelta}, changedPixels={changedPixelCount}";
        }

        /// <summary>Stores preview-scene and regular additive-scene results for one identical shadow fixture.</summary>
        private sealed class ShadowCaptureComparison
        {
            /// <summary>Initializes the two-scene shadow capture comparison.</summary>
            /// <param name="preview">The preview-scene result.</param>
            /// <param name="additive">The regular additive-scene result.</param>
            public ShadowCaptureComparison(
                ShadowCaptureObservation preview,
                ShadowCaptureObservation additive
            )
            {
                this.preview = preview;
                this.additive = additive;
            }

            /// <summary>Gets the preview-scene result.</summary>
            public ShadowCaptureObservation preview { get; }

            /// <summary>Gets the regular additive-scene result.</summary>
            public ShadowCaptureObservation additive { get; }

            /// <summary>Formats both scene results for deterministic A/B assertion diagnostics.</summary>
            /// <returns>The diagnostic comparison text.</returns>
            public string Describe() =>
                $"preview({preview.Describe()}), additive({additive.Describe()})";
        }

        /// <summary>Defines one fixed BIRP shader variant request.</summary>
        private sealed class VariantRequest
        {
            /// <summary>Initializes one fixed variant request.</summary>
            /// <param name="label">The diagnostic variant label.</param>
            /// <param name="pass">The requested shader pass.</param>
            /// <param name="keywords">The enabled keywords.</param>
            public VariantRequest(string label, PassType pass, string[] keywords)
            {
                this.label = label;
                this.pass = pass;
                this.keywords = keywords;
            }

            /// <summary>Gets the diagnostic label.</summary>
            public string label { get; }

            /// <summary>Gets the requested pass.</summary>
            public PassType pass { get; }

            /// <summary>Gets the enabled keywords.</summary>
            public string[] keywords { get; }
        }
    }

    /// <summary>Defines the versioned JSON contract for the BIRP scene regression baseline.</summary>
    [Serializable]
    public sealed class SceneRegressionBaseline
    {
        /// <summary>Stores the JSON schema version.</summary>
        public int schemaVersion;

        /// <summary>Stores the Unity version used to approve this baseline.</summary>
        public string unityVersion;

        /// <summary>Stores the graphics API used to approve this baseline.</summary>
        public string graphicsDevice;

        /// <summary>Stores the project color space used to approve this baseline.</summary>
        public string colorSpace;

        /// <summary>Stores the rendering pipeline used to approve this baseline.</summary>
        public string renderPipeline;

        /// <summary>Stores the square readback dimension.</summary>
        public int renderSize;

        /// <summary>Stores the committed static lightmap count.</summary>
        public int staticLightmapCount;

        /// <summary>Stores the committed static renderer assignment count.</summary>
        public int staticRendererAssignmentCount;

        /// <summary>Stores the reviewed visible scene-pixel range.</summary>
        public IntRange sceneVisiblePixelCount;

        /// <summary>
        /// Stores the reviewed shadow silhouette range. The narrow range accounts for
        /// deterministic rasterization differences between physical D3D11 GPUs and
        /// GitHub-hosted runners using Microsoft Basic Render Driver.
        /// </summary>
        public IntRange shadowChangedPixelCount;

        /// <summary>Stores the committed representative warmed variant count.</summary>
        public int warmedVariantCount;

        /// <summary>Stores the non-deterministic dynamic-lightmap status.</summary>
        public string dynamicLightmapStatus;

        /// <summary>Stores the reviewed Meta albedo observations.</summary>
        public MetaAlbedoBaseline[] metaAlbedo;
    }

    /// <summary>Stores one captured scene-regression observation.</summary>
    [Serializable]
    public sealed class SceneRegressionObservation
    {
        /// <summary>Stores the static lightmap count.</summary>
        public int staticLightmapCount;

        /// <summary>Stores the number of static renderer assignments.</summary>
        public int staticRendererAssignmentCount;

        /// <summary>Stores the finite scene-pixel count.</summary>
        public int sceneFinitePixelCount;

        /// <summary>Stores the visible scene-pixel count.</summary>
        public int sceneVisiblePixelCount;

        /// <summary>Stores the normalized visible scene-pixel coverage.</summary>
        public float sceneVisibleCoverage;

        /// <summary>Stores the horizontal normalized centroid of visible scene pixels.</summary>
        public float sceneVisibleCentroidX;

        /// <summary>Stores the vertical normalized centroid of visible scene pixels.</summary>
        public float sceneVisibleCentroidY;

        /// <summary>Stores the observed Meta values.</summary>
        public MetaAlbedoObservation[] metaAlbedo;

        /// <summary>Stores the shadow silhouette count.</summary>
        public int shadowChangedPixelCount;

        /// <summary>Stores the shadow receiver coverage pixel count.</summary>
        public int shadowCoveragePixelCount;

        /// <summary>Stores the normalized shadow receiver coverage.</summary>
        public float shadowCoverage;

        /// <summary>Stores the horizontal normalized centroid of changed shadow pixels.</summary>
        public float shadowCentroidX;

        /// <summary>Stores the vertical normalized centroid of changed shadow pixels.</summary>
        public float shadowCentroidY;

        /// <summary>Stores the maximum absolute RGB difference caused by directional shadows.</summary>
        public float shadowMaxAbsoluteRgbDelta;

        /// <summary>Stores the warmed variant count.</summary>
        public int warmedVariantCount;

        /// <summary>Stores the dynamic-lightmap limitation status.</summary>
        public string dynamicLightmapStatus;
    }

    /// <summary>Defines an inclusive reviewed integer range.</summary>
    [Serializable]
    public sealed class IntRange
    {
        /// <summary>Stores the inclusive minimum.</summary>
        public int minimum;

        /// <summary>Stores the inclusive maximum.</summary>
        public int maximum;

        /// <summary>Creates a zero-width reviewed range.</summary>
        /// <param name="value">The reviewed value.</param>
        /// <returns>The exact range.</returns>
        public static IntRange Exact(int value) =>
            new IntRange { minimum = value, maximum = value };
    }

    /// <summary>Defines an inclusive reviewed floating-point range.</summary>
    [Serializable]
    public sealed class FloatRange
    {
        /// <summary>Stores the inclusive minimum.</summary>
        public float minimum;

        /// <summary>Stores the inclusive maximum.</summary>
        public float maximum;

        /// <summary>Creates a zero-width reviewed range.</summary>
        /// <param name="value">The reviewed value.</param>
        /// <returns>The exact range.</returns>
        public static FloatRange Exact(float value) =>
            new FloatRange { minimum = value, maximum = value };
    }

    /// <summary>Stores one reviewed Meta albedo range.</summary>
    [Serializable]
    public sealed class MetaAlbedoBaseline
    {
        /// <summary>Stores the material name.</summary>
        public string materialName;

        /// <summary>Stores the product shader name.</summary>
        public string shaderName;

        /// <summary>Stores the reviewed mean luminance range.</summary>
        public FloatRange meanLuminance;
    }

    /// <summary>Stores one observed Meta albedo value.</summary>
    [Serializable]
    public sealed class MetaAlbedoObservation
    {
        /// <summary>Stores the material name.</summary>
        public string materialName;

        /// <summary>Stores the product shader name.</summary>
        public string shaderName;

        /// <summary>Stores the observed mean luminance.</summary>
        public float meanLuminance;
    }
}
