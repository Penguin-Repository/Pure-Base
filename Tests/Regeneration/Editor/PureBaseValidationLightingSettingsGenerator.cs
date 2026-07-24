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

// Regenerates and validates the persisted deterministic BIRP scene fixture and lighting configuration on explicit request.

using System;
using NUnit.Framework;
using PureBase.Tests.Daily;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Regeneration
{
    /// <summary>
    /// Creates the validation scene's Lighting Settings asset through Unity Editor APIs and assigns it to the scene.
    /// </summary>
    public static class PureBaseValidationLightingSettingsGenerator
    {
        /// <summary>Stores optional test-only dependencies used by the public fixture-generation entry points.</summary>
        private static GenerationDependencies testGenerationDependencies;

        /// <summary>
        /// Stores the AssetDatabase root directory for persisted validation fixture assets.
        /// </summary>
        private const string FixtureRootDirectory = "Packages/jp.penguin.purebase/Tests/Fixtures";

        /// <summary>
        /// Stores the AssetDatabase directory for the validation lighting asset.
        /// </summary>
        private const string LightingDirectory = FixtureRootDirectory + "/Lighting";

        /// <summary>
        /// Stores the AssetDatabase directory for the validation materials.
        /// </summary>
        private const string MaterialsDirectory = FixtureRootDirectory + "/Materials";

        /// <summary>
        /// Stores the AssetDatabase directory for the validation scene.
        /// </summary>
        private const string ScenesDirectory = FixtureRootDirectory + "/Scenes";

        /// <summary>
        /// Stores the AssetDatabase path for the validation lighting settings asset.
        /// </summary>
        private const string LightingSettingsPath =
            LightingDirectory + "/PureBaseValidationLightingSettings.lighting";

        /// <summary>
        /// Stores the AssetDatabase path for the validation scene.
        /// </summary>
        private const string ScenePath = ScenesDirectory + "/PureBaseValidation.unity";

        /// <summary>
        /// Stores the root GameObject name reserved for generated validation fixture content.
        /// </summary>
        private const string FixtureRootName = "PureBase Validation Fixture";

        /// <summary>
        /// Stores the fixed product shader names used by the persisted validation materials.
        /// </summary>
        private static readonly string[] ProductShaderNames =
        {
            "PureBase/Unlit",
            "PureBase/Toon",
            "PureBase/PBR",
            "PureBase/Hybrid",
        };

        /// <summary>
        /// Stores the persisted validation material paths that correspond to <see cref="ProductShaderNames"/>.
        /// </summary>
        private static readonly string[] ProductMaterialPaths =
        {
            MaterialsDirectory + "/PureBaseValidationUnlit.mat",
            MaterialsDirectory + "/PureBaseValidationToon.mat",
            MaterialsDirectory + "/PureBaseValidationPbr.mat",
            MaterialsDirectory + "/PureBaseValidationHybrid.mat",
        };

        /// <summary>
        /// Stores the non-transparent colors used to distinguish the persisted product materials in screenshot evidence.
        /// </summary>
        private static readonly Color[] ProductColors =
        {
            new Color(0.86f, 0.25f, 0.22f, 1.0f),
            new Color(0.24f, 0.72f, 0.32f, 1.0f),
            new Color(0.20f, 0.45f, 0.90f, 1.0f),
            new Color(0.92f, 0.68f, 0.18f, 1.0f),
        };

        /// <summary>
        /// Creates or updates the fixed BIRP lighting settings asset, assigns it to the validation scene, and verifies both assets.
        /// </summary>
        [MenuItem("PureBase/Tests/Regenerate Validation Fixture")]
        public static void GenerateAndValidate()
        {
            GenerationDependencies dependencies = testGenerationDependencies;
            if (dependencies == null)
            {
                var writeBoundary = new PureBaseRegressionBaselineGenerator.UnityWriteBoundary();
                dependencies = new GenerationDependencies(
                    new PureBaseRegressionBaselineGenerator.UnityEnvironment(),
                    new UnityFixtureGenerationOperations(writeBoundary),
                    writeBoundary
                );
            }

            PureBaseRegressionBaselineGenerator.GenerateFixture(
                dependencies.environment,
                dependencies.operations,
                dependencies.writeBoundary
            );
        }

        /// <summary>Runs the fixture write operations after the public entry point's guards have succeeded.</summary>
        /// <param name="writeBoundary">The transaction audit used after every canonical persistence checkpoint.</param>
        internal static void GenerateAndValidateAfterGuards(
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));
            SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                EnsureFixtureDirectories(writeBoundary);

                Scene validationScene = OpenOrCreateValidationScene(writeBoundary);

                SceneManager.SetActiveScene(validationScene);

                LightingSettings lightingSettings = LoadOrCreateLightingSettings(writeBoundary);
                ConfigureLightingSettings(lightingSettings);
                ConfigureAmbientSettings();
                Material[] productMaterials = ConfigureValidationFixture(
                    validationScene,
                    writeBoundary
                );

                Lightmapping.SetLightingSettingsForScene(validationScene, lightingSettings);
                EditorUtility.SetDirty(lightingSettings);
                EditorSceneManager.MarkSceneDirty(validationScene);
                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    writeBoundary,
                    () => AssetDatabase.SaveAssetIfDirty(lightingSettings)
                );
                foreach (Material productMaterial in productMaterials)
                {
                    PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                        writeBoundary,
                        () => AssetDatabase.SaveAssetIfDirty(productMaterial)
                    );
                }

                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    writeBoundary,
                    () => EditorSceneManager.SaveScene(validationScene)
                );
                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    writeBoundary,
                    () =>
                        AssetDatabase.ImportAsset(
                            LightingSettingsPath,
                            ImportAssetOptions.ForceSynchronousImport
                                | ImportAssetOptions.ForceUpdate
                        )
                );

                Validate(validationScene, lightingSettings);
                Debug.Log(
                    $"Pure-Base validation lighting settings generated: {LightingSettingsPath}"
                );
            }
            finally
            {
                RestoreSceneManagerSetupIfPresent(previousSceneSetup);
            }
        }

        /// <summary>
        /// Provides an explicit no-argument entry point for Unity batch-mode <c>-executeMethod</c> invocation.
        /// </summary>
        public static void GenerateAndValidateForBatchMode()
        {
            GenerateAndValidate();
        }

        /// <summary>Temporarily replaces the public entry point dependencies for fail-before-write tests.</summary>
        /// <param name="environment">The environment presented to the public entry point.</param>
        /// <param name="operations">The write operation that must remain unreachable on rejection.</param>
        /// <param name="writeBoundary">The transaction audit presented to the public entry point.</param>
        /// <returns>A scope that restores the production dependencies.</returns>
        internal static IDisposable OverrideGenerationDependenciesForTests(
            PureBaseRegressionBaselineGenerator.IEnvironment environment,
            PureBaseRegressionBaselineGenerator.IFixtureGenerationOperations operations,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            if (environment == null)
                throw new ArgumentNullException(nameof(environment));
            if (operations == null)
                throw new ArgumentNullException(nameof(operations));
            if (writeBoundary == null)
                throw new ArgumentNullException(nameof(writeBoundary));

            GenerationDependencies previousDependencies = testGenerationDependencies;
            testGenerationDependencies = new GenerationDependencies(
                environment,
                operations,
                writeBoundary
            );
            return new GenerationDependencyScope(previousDependencies);
        }

        /// <summary>
        /// Verifies that the validation lighting asset and scene assignment are available after Unity imports them.
        /// </summary>
        [MenuItem("PureBase/Tests/Validate Validation Lighting Settings")]
        public static void ValidateGeneratedAssets()
        {
            SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                LightingSettings lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                    LightingSettingsPath
                );
                if (lightingSettings == null)
                {
                    throw new InvalidOperationException(
                        $"Missing Lighting Settings asset at '{LightingSettingsPath}'."
                    );
                }

                Scene validationScene = SceneManager.GetSceneByPath(ScenePath);
                if (!validationScene.isLoaded)
                {
                    validationScene = EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Additive
                    );
                }

                SceneManager.SetActiveScene(validationScene);
                Validate(validationScene, lightingSettings);
                Debug.Log(
                    $"Pure-Base validation lighting settings verified: {LightingSettingsPath}"
                );
            }
            finally
            {
                RestoreSceneManagerSetupIfPresent(previousSceneSetup);
            }
        }

        /// <summary>
        /// Ensures Unity's AssetDatabase owns every directory required by the persisted validation fixture.
        /// </summary>
        private static void EnsureFixtureDirectories(
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            if (!AssetDatabase.IsValidFolder(FixtureRootDirectory))
            {
                throw new InvalidOperationException(
                    "The validation fixture root folder does not exist."
                );
            }

            EnsureAssetDirectory(LightingDirectory, writeBoundary);
            EnsureAssetDirectory(MaterialsDirectory, writeBoundary);
            EnsureAssetDirectory(ScenesDirectory, writeBoundary);
        }

        /// <summary>
        /// Creates one missing fixture directory below the existing validation fixture root.
        /// </summary>
        /// <param name="directoryPath">The AssetDatabase path for the required directory.</param>
        private static void EnsureAssetDirectory(
            string directoryPath,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            if (!AssetDatabase.IsValidFolder(directoryPath))
            {
                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    writeBoundary,
                    () =>
                        AssetDatabase.CreateFolder(
                            FixtureRootDirectory,
                            System.IO.Path.GetFileName(directoryPath)
                        )
                );
            }
        }

        /// <summary>
        /// Opens the persisted validation scene or creates its initially empty persistent asset through the Editor API.
        /// </summary>
        /// <returns>The loaded validation scene.</returns>
        private static Scene OpenOrCreateValidationScene(
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            Scene validationScene = SceneManager.GetSceneByPath(ScenePath);
            if (validationScene.isLoaded)
            {
                return validationScene;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            validationScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive
            );
            PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                writeBoundary,
                () => EditorSceneManager.SaveScene(validationScene, ScenePath)
            );
            return validationScene;
        }

        /// <summary>
        /// Loads the generated asset when present or creates it using Unity's serialized asset pipeline.
        /// </summary>
        /// <returns>The persistent validation lighting settings asset.</returns>
        private static LightingSettings LoadOrCreateLightingSettings(
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            LightingSettings lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                LightingSettingsPath
            );
            if (lightingSettings != null)
            {
                return lightingSettings;
            }

            lightingSettings = new LightingSettings();
            PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                writeBoundary,
                () => AssetDatabase.CreateAsset(lightingSettings, LightingSettingsPath)
            );
            return lightingSettings;
        }

        /// <summary>
        /// Recreates the deterministic scene-owned geometry, camera, directional light, and persisted product materials.
        /// </summary>
        /// <param name="validationScene">The active persistent validation scene.</param>
        private static Material[] ConfigureValidationFixture(
            Scene validationScene,
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            GameObject existingRoot = GameObject.Find(FixtureRootName);
            if (existingRoot != null && existingRoot.scene == validationScene)
            {
                UnityEngine.Object.DestroyImmediate(existingRoot);
            }

            Material[] productMaterials = CreateOrUpdateProductMaterials(writeBoundary);
            GameObject fixtureRoot = new GameObject(FixtureRootName);
            SceneManager.MoveGameObjectToScene(fixtureRoot, validationScene);

            CreateGround(fixtureRoot.transform);
            for (int materialIndex = 0; materialIndex < productMaterials.Length; materialIndex++)
            {
                CreateProductCube(
                    fixtureRoot.transform,
                    productMaterials[materialIndex],
                    materialIndex
                );
            }

            CreateSceneCamera(fixtureRoot.transform);
            CreateBakedDirectionalLight(fixtureRoot.transform);
            return productMaterials;
        }

        /// <summary>
        /// Creates or updates each persistent material bound to the required public PureBase shader.
        /// </summary>
        /// <returns>The persisted materials in product shader order.</returns>
        private static Material[] CreateOrUpdateProductMaterials(
            PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
        )
        {
            Material[] productMaterials = new Material[ProductShaderNames.Length];
            for (int materialIndex = 0; materialIndex < ProductShaderNames.Length; materialIndex++)
            {
                Shader shader = Shader.Find(ProductShaderNames[materialIndex]);
                if (shader == null)
                {
                    throw new InvalidOperationException(
                        $"The required product shader '{ProductShaderNames[materialIndex]}' is unavailable while generating the validation fixture."
                    );
                }

                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    ProductMaterialPaths[materialIndex]
                );
                if (material == null)
                {
                    material = new Material(shader)
                    {
                        name = System.IO.Path.GetFileNameWithoutExtension(
                            ProductMaterialPaths[materialIndex]
                        ),
                    };
                    PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                        writeBoundary,
                        () =>
                            AssetDatabase.CreateAsset(material, ProductMaterialPaths[materialIndex])
                    );
                }
                else
                {
                    material.shader = shader;
                }

                material.SetColor("_BaseColor", ProductColors[materialIndex]);
                material.SetFloat("_Cutoff", 0.5f);
                EditorUtility.SetDirty(material);
                productMaterials[materialIndex] = material;
            }

            return productMaterials;
        }

        /// <summary>
        /// Creates the static Standard-material ground receiver for the fixed product geometry.
        /// </summary>
        /// <param name="parent">The generated fixture root transform.</param>
        private static void CreateGround(Transform parent)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "PureBase Validation Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(1.2f, 1.0f, 1.2f);
            Material groundMaterial = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.36f, 0.38f, 0.42f, 1.0f),
            };
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            SetStaticLightingFlags(ground);
        }

        /// <summary>
        /// Creates one static product-material cube for the fixed screenshot, Meta, and bake fixture.
        /// </summary>
        /// <param name="parent">The generated fixture root transform.</param>
        /// <param name="material">The persisted material bound to the product shader.</param>
        /// <param name="materialIndex">The zero-based product material position.</param>
        private static void CreateProductCube(
            Transform parent,
            Material material,
            int materialIndex
        )
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"PureBase Validation {material.shader.name.Substring("PureBase/".Length)}";
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = new Vector3((materialIndex - 1.5f) * 2.2f, 0.75f, 0.0f);
            cube.transform.localScale = new Vector3(1.2f, 1.5f, 1.2f);
            MeshRenderer renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            SetStaticLightingFlags(cube);
        }

        /// <summary>
        /// Marks a generated fixture object as contributing static geometry to the on-demand bake.
        /// </summary>
        /// <param name="gameObject">The generated object that must receive a static lightmap assignment.</param>
        private static void SetStaticLightingFlags(GameObject gameObject)
        {
            GameObjectUtility.SetStaticEditorFlags(
                gameObject,
                StaticEditorFlags.ContributeGI
                    | StaticEditorFlags.BatchingStatic
                    | StaticEditorFlags.OccluderStatic
                    | StaticEditorFlags.OccludeeStatic
            );
        }

        /// <summary>
        /// Creates the enabled camera that renders the fixed validation scene evidence.
        /// </summary>
        /// <param name="parent">The generated fixture root transform.</param>
        private static void CreateSceneCamera(Transform parent)
        {
            GameObject cameraObject = new GameObject("PureBase Validation Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0.0f, 4.8f, -12.0f);
            cameraObject.transform.LookAt(new Vector3(0.0f, 0.8f, 0.0f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.045f, 0.070f, 1.0f);
            camera.fieldOfView = 42.0f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100.0f;
        }

        /// <summary>
        /// Creates the baked directional source that lights and casts shadows from the static fixture geometry.
        /// </summary>
        /// <param name="parent">The generated fixture root transform.</param>
        private static void CreateBakedDirectionalLight(Transform parent)
        {
            GameObject lightObject = new GameObject("PureBase Validation Baked Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(52.0f, -32.0f, 0.0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = new Color(1.0f, 0.95f, 0.86f, 1.0f);
            light.intensity = 1.4f;
            light.shadows = LightShadows.Hard;
        }

        /// <summary>
        /// Configures the conservative Progressive CPU BIRP bake baseline used by the validation fixture.
        /// </summary>
        /// <param name="lightingSettings">The persistent lighting settings asset to configure.</param>
        private static void ConfigureLightingSettings(LightingSettings lightingSettings)
        {
            lightingSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            lightingSettings.bakedGI = true;
            lightingSettings.realtimeGI = false;
            lightingSettings.autoGenerate = false;
            lightingSettings.lightmapResolution = 40.0f;
            lightingSettings.indirectResolution = 2.0f;
            lightingSettings.lightmapPadding = 2;
            lightingSettings.lightmapMaxSize = 1024;
            lightingSettings.lightmapCompression = LightmapCompression.NormalQuality;
            lightingSettings.directSampleCount = 32;
            lightingSettings.indirectSampleCount = 512;
            lightingSettings.environmentSampleCount = 256;
            lightingSettings.environmentImportanceSampling = true;
            lightingSettings.minBounces = 1;
            lightingSettings.maxBounces = 2;
            lightingSettings.lightProbeSampleCountMultiplier = 4;
        }

        /// <summary>
        /// Applies a fixed flat ambient source to the active validation scene.
        /// </summary>
        private static void ConfigureAmbientSettings()
        {
            Color ambientColor = new Color(0.212f, 0.227f, 0.259f, 1.0f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;
            RenderSettings.ambientIntensity = 1.0f;
        }

        /// <summary>
        /// Restores previously opened scenes only when Unity supplied a non-empty setup.
        /// </summary>
        /// <param name="sceneSetup">The scene setup captured before explicit fixture work began.</param>
        private static void RestoreSceneManagerSetupIfPresent(SceneSetup[] sceneSetup)
        {
            if (sceneSetup != null && sceneSetup.Length > 0)
            {
                EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
            }
        }

        /// <summary>
        /// Confirms the persistent asset, the scene assignment, and the deterministic BIRP lighting baseline.
        /// </summary>
        /// <param name="validationScene">The opened validation scene.</param>
        /// <param name="lightingSettings">The persistent lighting settings asset assigned to the scene.</param>
        private static void Validate(Scene validationScene, LightingSettings lightingSettings)
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(LightingSettingsPath)))
            {
                throw new InvalidOperationException(
                    $"Lighting Settings asset has no Unity GUID: '{LightingSettingsPath}'."
                );
            }

            if (Lightmapping.GetLightingSettingsForScene(validationScene) != lightingSettings)
            {
                throw new InvalidOperationException(
                    "The validation scene does not reference the generated Lighting Settings asset."
                );
            }

            for (int materialIndex = 0; materialIndex < ProductShaderNames.Length; materialIndex++)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    ProductMaterialPaths[materialIndex]
                );
                if (
                    material == null
                    || material.shader == null
                    || material.shader.name != ProductShaderNames[materialIndex]
                )
                {
                    throw new InvalidOperationException(
                        $"The validation fixture does not persist the required '{ProductShaderNames[materialIndex]}' material at '{ProductMaterialPaths[materialIndex]}'."
                    );
                }
            }

            bool hasCamera = false;
            bool hasBakedDirectionalLight = false;
            bool hasStaticRenderer = false;
            foreach (GameObject root in validationScene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    hasCamera |= camera.enabled;
                }

                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    hasBakedDirectionalLight |=
                        light.enabled
                        && light.type == LightType.Directional
                        && light.lightmapBakeType == LightmapBakeType.Baked;
                }

                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    hasStaticRenderer |=
                        renderer.enabled
                        && renderer.gameObject.isStatic
                        && renderer.sharedMaterial != null;
                }
            }

            if (!hasCamera || !hasBakedDirectionalLight || !hasStaticRenderer)
            {
                throw new InvalidOperationException(
                    "The validation scene is missing its enabled camera, baked directional light, or static renderers."
                );
            }

            if (
                lightingSettings.lightmapper != LightingSettings.Lightmapper.ProgressiveCPU
                || !lightingSettings.bakedGI
                || lightingSettings.realtimeGI
                || lightingSettings.autoGenerate
            )
            {
                throw new InvalidOperationException(
                    "The validation lighting asset does not have the required Progressive BIRP baseline."
                );
            }

            if (
                RenderSettings.ambientMode != AmbientMode.Flat
                || RenderSettings.ambientLight != new Color(0.212f, 0.227f, 0.259f, 1.0f)
                || !Mathf.Approximately(RenderSettings.ambientIntensity, 1.0f)
            )
            {
                throw new InvalidOperationException(
                    "The validation scene does not have the required fixed flat ambient source."
                );
            }
        }

        /// <summary>Invokes the fixture implementation only after the shared guarded orchestration authorizes it.</summary>
        private sealed class UnityFixtureGenerationOperations
            : PureBaseRegressionBaselineGenerator.IFixtureGenerationOperations
        {
            private readonly PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary;

            /// <summary>Initializes fixture writes with their active transaction audit.</summary>
            /// <param name="writeBoundary">The audit used after canonical persistence checkpoints.</param>
            public UnityFixtureGenerationOperations(
                PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
            )
            {
                this.writeBoundary =
                    writeBoundary ?? throw new ArgumentNullException(nameof(writeBoundary));
            }

            /// <inheritdoc />
            public void GenerateFixture()
            {
                GenerateAndValidateAfterGuards(writeBoundary);
            }
        }

        /// <summary>Supplies the guarded public entry point with one environment, write operation, and dirty audit.</summary>
        private sealed class GenerationDependencies
        {
            /// <summary>Initializes one coherent guarded-generation dependency set.</summary>
            /// <param name="environment">The environment to validate.</param>
            /// <param name="operations">The fixture write operation.</param>
            /// <param name="writeBoundary">The dirty-target audit.</param>
            public GenerationDependencies(
                PureBaseRegressionBaselineGenerator.IEnvironment environment,
                PureBaseRegressionBaselineGenerator.IFixtureGenerationOperations operations,
                PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary
            )
            {
                this.environment = environment;
                this.operations = operations;
                this.writeBoundary = writeBoundary;
            }

            /// <summary>Gets the environment to validate before writing.</summary>
            public PureBaseRegressionBaselineGenerator.IEnvironment environment { get; }

            /// <summary>Gets the fixture write operation.</summary>
            public PureBaseRegressionBaselineGenerator.IFixtureGenerationOperations operations { get; }

            /// <summary>Gets the dirty-target audit.</summary>
            public PureBaseRegressionBaselineGenerator.IWriteBoundary writeBoundary { get; }
        }

        /// <summary>Restores the production public-entry dependencies after a focused test.</summary>
        private sealed class GenerationDependencyScope : IDisposable
        {
            private readonly GenerationDependencies previousDependencies;
            private bool disposed;

            /// <summary>Initializes a restoration scope.</summary>
            /// <param name="previousDependencies">The dependencies that preceded a test override.</param>
            public GenerationDependencyScope(GenerationDependencies previousDependencies)
            {
                this.previousDependencies = previousDependencies;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (disposed)
                    return;
                testGenerationDependencies = previousDependencies;
                disposed = true;
            }
        }

        /// <summary>Verifies that the public fixture-generation entry points reject invalid environments before writes.</summary>
        public sealed class PublicEntryPointGuardTests
        {
            /// <summary>Ensures the menu entry point rejects a Unity version mismatch before fixture generation.</summary>
            [Test]
            public void MenuEntryPointRejectsUnityVersionMismatchBeforeWrite()
            {
                AssertMenuEntryPointFailsBeforeWrite(
                    new TestEnvironment(
                        "2022.3.0f1",
                        true,
                        GraphicsDeviceType.Direct3D11,
                        ColorSpace.Linear
                    )
                );
            }

            /// <summary>Ensures the menu entry point rejects a non-BIRP pipeline before fixture generation.</summary>
            [Test]
            public void MenuEntryPointRejectsNonBirpBeforeWrite()
            {
                AssertMenuEntryPointFailsBeforeWrite(
                    new TestEnvironment(
                        PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                        false,
                        GraphicsDeviceType.Direct3D11,
                        ColorSpace.Linear
                    )
                );
            }

            /// <summary>Ensures the menu entry point rejects a non-D3D11 graphics API before fixture generation.</summary>
            [Test]
            public void MenuEntryPointRejectsNonD3D11BeforeWrite()
            {
                AssertMenuEntryPointFailsBeforeWrite(
                    new TestEnvironment(
                        PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                        true,
                        GraphicsDeviceType.Direct3D12,
                        ColorSpace.Linear
                    )
                );
            }

            /// <summary>Ensures the menu entry point rejects a non-linear project before fixture generation.</summary>
            [Test]
            public void MenuEntryPointRejectsNonLinearColorSpaceBeforeWrite()
            {
                AssertMenuEntryPointFailsBeforeWrite(
                    new TestEnvironment(
                        PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                        true,
                        GraphicsDeviceType.Direct3D11,
                        ColorSpace.Gamma
                    )
                );
            }

            /// <summary>Ensures the batch entry point rejects a dirty non-canonical scene before fixture generation.</summary>
            [Test]
            public void BatchModeEntryPointRejectsDirtyNonCanonicalSceneBeforeWrite()
            {
                var operations = new RecordingOperations();
                var writeBoundary = new RejectingWriteBoundary();
                using (
                    OverrideGenerationDependenciesForTests(
                        new TestEnvironment(
                            PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                            true,
                            GraphicsDeviceType.Direct3D11,
                            ColorSpace.Linear
                        ),
                        operations,
                        writeBoundary
                    )
                )
                {
                    Assert.Throws<InvalidOperationException>(() =>
                        GenerateAndValidateForBatchMode()
                    );
                }

                Assert.That(writeBoundary.CallCount, Is.EqualTo(1));
                Assert.That(operations.CallCount, Is.Zero);
            }

            /// <summary>Ensures the menu entry point rejects a dirty non-canonical scene before fixture generation.</summary>
            [Test]
            public void MenuEntryPointRejectsDirtyNonCanonicalSceneBeforeWrite()
            {
                var operations = new RecordingOperations();
                var writeBoundary = new RejectingWriteBoundary();
                using (
                    OverrideGenerationDependenciesForTests(
                        new TestEnvironment(
                            PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                            true,
                            GraphicsDeviceType.Direct3D11,
                            ColorSpace.Linear
                        ),
                        operations,
                        writeBoundary
                    )
                )
                {
                    Assert.Throws<InvalidOperationException>(() => GenerateAndValidate());
                }

                Assert.That(writeBoundary.CallCount, Is.EqualTo(1));
                Assert.That(operations.CallCount, Is.Zero);
            }

            /// <summary>Invokes the actual menu entry point with an invalid environment and verifies its write operation is unreachable.</summary>
            /// <param name="environment">The invalid environment to present.</param>
            private static void AssertMenuEntryPointFailsBeforeWrite(
                PureBaseRegressionBaselineGenerator.IEnvironment environment
            )
            {
                var operations = new RecordingOperations();
                var writeBoundary = new RecordingWriteBoundary();
                using (
                    OverrideGenerationDependenciesForTests(environment, operations, writeBoundary)
                )
                {
                    Assert.Throws<InvalidOperationException>(() => GenerateAndValidate());
                }

                Assert.That(writeBoundary.CallCount, Is.Zero);
                Assert.That(operations.CallCount, Is.Zero);
            }

            /// <summary>Supplies fixed environment values to public-entry fail-before-write tests.</summary>
            private sealed class TestEnvironment : PureBaseRegressionBaselineGenerator.IEnvironment
            {
                /// <summary>Initializes fixed environment values.</summary>
                /// <param name="unityVersion">The Unity version.</param>
                /// <param name="isBuiltInRenderPipeline">Whether BIRP is active.</param>
                /// <param name="graphicsDeviceType">The graphics device type.</param>
                /// <param name="colorSpace">The project color space.</param>
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

            /// <summary>Records whether public-entry fixture writes became reachable.</summary>
            private sealed class RecordingOperations
                : PureBaseRegressionBaselineGenerator.IFixtureGenerationOperations
            {
                /// <summary>Gets the number of fixture write attempts.</summary>
                public int CallCount { get; private set; }

                /// <inheritdoc />
                public void GenerateFixture()
                {
                    CallCount++;
                }
            }

            /// <summary>Records transaction audit calls.</summary>
            private sealed class RecordingWriteBoundary
                : PureBaseRegressionBaselineGenerator.IWriteBoundary
            {
                /// <summary>Gets the number of transaction starts.</summary>
                public int CallCount { get; private set; }

                /// <inheritdoc />
                public void BeginTransaction()
                {
                    CallCount++;
                }

                /// <inheritdoc />
                public void VerifyNoUnrelatedChanges() { }
            }

            /// <summary>Models a pre-write transaction rejection without creating a serialized asset.</summary>
            private sealed class RejectingWriteBoundary
                : PureBaseRegressionBaselineGenerator.IWriteBoundary
            {
                /// <summary>Gets the number of transaction starts.</summary>
                public int CallCount { get; private set; }

                /// <inheritdoc />
                public void BeginTransaction()
                {
                    CallCount++;
                    throw new InvalidOperationException("A non-canonical dirty scene is present.");
                }

                /// <inheritdoc />
                public void VerifyNoUnrelatedChanges() { }
            }
        }
    }
}
