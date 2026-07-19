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
        private const string LightingSettingsPath = LightingDirectory + "/PureBaseValidationLightingSettings.lighting";

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
        private static readonly string[] ProductShaderNames = { "PureBase/Unlit", "PureBase/Toon", "PureBase/PBR", "PureBase/Hybrid" };

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
            SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                EnsureFixtureDirectories();

                Scene validationScene = OpenOrCreateValidationScene();

                SceneManager.SetActiveScene(validationScene);

                LightingSettings lightingSettings = LoadOrCreateLightingSettings();
                ConfigureLightingSettings(lightingSettings);
                ConfigureAmbientSettings();
                ConfigureValidationFixture(validationScene);

                Lightmapping.SetLightingSettingsForScene(validationScene, lightingSettings);
                EditorUtility.SetDirty(lightingSettings);
                EditorSceneManager.MarkSceneDirty(validationScene);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(validationScene);
                AssetDatabase.ImportAsset(LightingSettingsPath, ImportAssetOptions.ForceUpdate);

                Validate(validationScene, lightingSettings);
                Debug.Log($"Pure-Base validation lighting settings generated: {LightingSettingsPath}");
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

        /// <summary>
        /// Verifies that the validation lighting asset and scene assignment are available after Unity imports them.
        /// </summary>
        [MenuItem("PureBase/Tests/Validate Validation Lighting Settings")]
        public static void ValidateGeneratedAssets()
        {
            SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                LightingSettings lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
                if (lightingSettings == null)
                {
                    throw new InvalidOperationException($"Missing Lighting Settings asset at '{LightingSettingsPath}'.");
                }

                Scene validationScene = SceneManager.GetSceneByPath(ScenePath);
                if (!validationScene.isLoaded)
                {
                    validationScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                }

                SceneManager.SetActiveScene(validationScene);
                Validate(validationScene, lightingSettings);
                Debug.Log($"Pure-Base validation lighting settings verified: {LightingSettingsPath}");
            }
            finally
            {
                RestoreSceneManagerSetupIfPresent(previousSceneSetup);
            }
        }

        /// <summary>
        /// Ensures Unity's AssetDatabase owns every directory required by the persisted validation fixture.
        /// </summary>
        private static void EnsureFixtureDirectories()
        {
            if (!AssetDatabase.IsValidFolder(FixtureRootDirectory))
            {
                throw new InvalidOperationException("The validation fixture root folder does not exist.");
            }

            EnsureAssetDirectory(LightingDirectory);
            EnsureAssetDirectory(MaterialsDirectory);
            EnsureAssetDirectory(ScenesDirectory);
        }

        /// <summary>
        /// Creates one missing fixture directory below the existing validation fixture root.
        /// </summary>
        /// <param name="directoryPath">The AssetDatabase path for the required directory.</param>
        private static void EnsureAssetDirectory(string directoryPath)
        {
            if (!AssetDatabase.IsValidFolder(directoryPath))
            {
                AssetDatabase.CreateFolder(FixtureRootDirectory, System.IO.Path.GetFileName(directoryPath));
            }
        }

        /// <summary>
        /// Opens the persisted validation scene or creates its initially empty persistent asset through the Editor API.
        /// </summary>
        /// <returns>The loaded validation scene.</returns>
        private static Scene OpenOrCreateValidationScene()
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

            validationScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(validationScene, ScenePath);
            return validationScene;
        }

        /// <summary>
        /// Loads the generated asset when present or creates it using Unity's serialized asset pipeline.
        /// </summary>
        /// <returns>The persistent validation lighting settings asset.</returns>
        private static LightingSettings LoadOrCreateLightingSettings()
        {
            LightingSettings lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (lightingSettings != null)
            {
                return lightingSettings;
            }

            lightingSettings = new LightingSettings();
            AssetDatabase.CreateAsset(lightingSettings, LightingSettingsPath);
            return lightingSettings;
        }

        /// <summary>
        /// Recreates the deterministic scene-owned geometry, camera, directional light, and persisted product materials.
        /// </summary>
        /// <param name="validationScene">The active persistent validation scene.</param>
        private static void ConfigureValidationFixture(Scene validationScene)
        {
            GameObject existingRoot = GameObject.Find(FixtureRootName);
            if (existingRoot != null && existingRoot.scene == validationScene)
            {
                UnityEngine.Object.DestroyImmediate(existingRoot);
            }

            Material[] productMaterials = CreateOrUpdateProductMaterials();
            GameObject fixtureRoot = new GameObject(FixtureRootName);
            SceneManager.MoveGameObjectToScene(fixtureRoot, validationScene);

            CreateGround(fixtureRoot.transform);
            for (int materialIndex = 0; materialIndex < productMaterials.Length; materialIndex++)
            {
                CreateProductCube(fixtureRoot.transform, productMaterials[materialIndex], materialIndex);
            }

            CreateSceneCamera(fixtureRoot.transform);
            CreateBakedDirectionalLight(fixtureRoot.transform);
        }

        /// <summary>
        /// Creates or updates each persistent material bound to the required public PureBase shader.
        /// </summary>
        /// <returns>The persisted materials in product shader order.</returns>
        private static Material[] CreateOrUpdateProductMaterials()
        {
            Material[] productMaterials = new Material[ProductShaderNames.Length];
            for (int materialIndex = 0; materialIndex < ProductShaderNames.Length; materialIndex++)
            {
                Shader shader = Shader.Find(ProductShaderNames[materialIndex]);
                if (shader == null)
                {
                    throw new InvalidOperationException($"The required product shader '{ProductShaderNames[materialIndex]}' is unavailable while generating the validation fixture.");
                }

                Material material = AssetDatabase.LoadAssetAtPath<Material>(ProductMaterialPaths[materialIndex]);
                if (material == null)
                {
                    material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(ProductMaterialPaths[materialIndex]) };
                    AssetDatabase.CreateAsset(material, ProductMaterialPaths[materialIndex]);
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
            Material groundMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.36f, 0.38f, 0.42f, 1.0f) };
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            SetStaticLightingFlags(ground);
        }

        /// <summary>
        /// Creates one static product-material cube for the fixed screenshot, Meta, and bake fixture.
        /// </summary>
        /// <param name="parent">The generated fixture root transform.</param>
        /// <param name="material">The persisted material bound to the product shader.</param>
        /// <param name="materialIndex">The zero-based product material position.</param>
        private static void CreateProductCube(Transform parent, Material material, int materialIndex)
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
            GameObjectUtility.SetStaticEditorFlags(gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
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
                throw new InvalidOperationException($"Lighting Settings asset has no Unity GUID: '{LightingSettingsPath}'.");
            }

            if (Lightmapping.GetLightingSettingsForScene(validationScene) != lightingSettings)
            {
                throw new InvalidOperationException("The validation scene does not reference the generated Lighting Settings asset.");
            }

            for (int materialIndex = 0; materialIndex < ProductShaderNames.Length; materialIndex++)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(ProductMaterialPaths[materialIndex]);
                if (material == null || material.shader == null || material.shader.name != ProductShaderNames[materialIndex])
                {
                    throw new InvalidOperationException($"The validation fixture does not persist the required '{ProductShaderNames[materialIndex]}' material at '{ProductMaterialPaths[materialIndex]}'.");
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
                    hasBakedDirectionalLight |= light.enabled && light.type == LightType.Directional && light.lightmapBakeType == LightmapBakeType.Baked;
                }

                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    hasStaticRenderer |= renderer.enabled && renderer.gameObject.isStatic && renderer.sharedMaterial != null;
                }
            }

            if (!hasCamera || !hasBakedDirectionalLight || !hasStaticRenderer)
            {
                throw new InvalidOperationException("The validation scene is missing its enabled camera, baked directional light, or static renderers.");
            }

            if (lightingSettings.lightmapper != LightingSettings.Lightmapper.ProgressiveCPU ||
                !lightingSettings.bakedGI ||
                lightingSettings.realtimeGI ||
                lightingSettings.autoGenerate)
            {
                throw new InvalidOperationException("The validation lighting asset does not have the required Progressive BIRP baseline.");
            }

            if (RenderSettings.ambientMode != AmbientMode.Flat ||
                RenderSettings.ambientLight != new Color(0.212f, 0.227f, 0.259f, 1.0f) ||
                !Mathf.Approximately(RenderSettings.ambientIntensity, 1.0f))
            {
                throw new InvalidOperationException("The validation scene does not have the required fixed flat ambient source.");
            }
        }
    }
}