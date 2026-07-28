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

// Runs a runner-staged synchronous validation-scene bake and exports contained evidence.

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Release.Consumer.Tests
{
    /// <summary>Validates one real BIRP bake in the runner-staged consumer validation scene.</summary>
    public sealed class PureBaseConsumerBakeEvidenceTests
    {
        /// <summary>Defines the square evidence image dimension.</summary>
        private const int RenderSize = 256;

        /// <summary>Identifies the manifest-excluded Unity asset root used only while a bake is running.</summary>
        private const string DisposableArtifactRootDirectory = "Assets/Artifacts";

        /// <summary>Identifies the disposable directory that owns copied-scene bake output.</summary>
        private const string DisposableBakeDirectory =
            DisposableArtifactRootDirectory + "/ProgressiveCpuBake";

        /// <summary>Identifies the copied validation-scene asset used only for the synchronous bake.</summary>
        private const string DisposableBakeScenePath =
            DisposableBakeDirectory + "/PureBaseValidationBake.unity";

        /// <summary>Defines the four public products that must produce Meta evidence.</summary>
        private static readonly string[] RequiredProductShaderNames =
        {
            "PureBase/Unlit",
            "PureBase/Toon",
            "PureBase/PBR",
            "PureBase/Hybrid",
        };

        /// <summary>Synchronously bakes the configured scene and records lightmap and image evidence.</summary>
        [Test]
        public void ConfiguredValidationSceneBakesAndExportsEvidence()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            Assert.That(
                SystemInfo.graphicsDeviceType,
                Is.EqualTo(GraphicsDeviceType.Direct3D11),
                $"Consumer run '{contract.runLabel}' requires Direct3D11 for bake evidence."
            );
            Assert.That(
                GraphicsSettings.currentRenderPipeline,
                Is.Null,
                $"Consumer run '{contract.runLabel}' requires the Built-in Render Pipeline for bake evidence."
            );
            Assert.That(
                contract.bake,
                Is.Not.Null,
                $"Consumer run '{contract.runLabel}' must provide bake for bake evidence."
            );
            ValidateBakeContract(contract);
            AssertModuleFreeGeneratedSources(contract);

            ConsumerBakeArtifact artifact = new ConsumerBakeArtifact
            {
                runLabel = contract.runLabel,
                scenePath = contract.bake.scenePath,
                unityVersion = Application.unityVersion,
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString(),
            };
            SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            DisposableBakeScope disposableBakeScope = new DisposableBakeScope();
            try
            {
                LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                    contract.bake.lightingSettingsPath
                );
                Scene scene = CreateDisposableBakeScene(
                    contract.bake.scenePath,
                    settings,
                    disposableBakeScope
                );
                ValidateLightingBaseline(contract, scene, artifact);
                artifact.bakeStarted = Lightmapping.Bake();
                Assert.That(
                    artifact.bakeStarted,
                    Is.True,
                    $"Consumer run '{contract.runLabel}' did not start a synchronous bake for '{contract.bake.scenePath}'."
                );
                Assert.That(
                    EditorSceneManager.SaveScene(scene),
                    Is.True,
                    $"Consumer run '{contract.runLabel}' could not persist disposable bake data at '{scene.path}'."
                );
                ValidateLightmaps(contract, scene, artifact);
                CaptureReadback(contract, scene, artifact);
                CaptureMetaAlbedoReadbacks(contract, scene, artifact);
                CaptureShadowSilhouette(contract, scene, artifact);
                WarmRepresentativeVariants(contract, artifact);
            }
            finally
            {
                try
                {
                    RestoreSceneBaselineOrCloseFixtureScenes(
                        previousSceneSetup,
                        disposableBakeScope
                    );
                }
                finally
                {
                    try
                    {
                        CleanupDisposableBakeAssets(disposableBakeScope);
                    }
                    finally
                    {
                        File.WriteAllText(
                            Path.Combine(
                                ConsumerValidationSupport.GetArtifactDirectory(),
                                "bake-evidence.json"
                            ),
                            JsonUtility.ToJson(artifact, true)
                        );
                    }
                }
            }
        }

        /// <summary>Copies the fixed scene into the excluded artifact namespace and detaches its fixture-owned lighting data.</summary>
        /// <param name="fixtureScenePath">The validated fixed scene path to copy without loading it.</param>
        /// <param name="settings">The validated fixture Lighting Settings asset to reuse read-only.</param>
        /// <param name="scope">The scope that records created assets for cleanup, including partial initialization.</param>
        /// <returns>The disposable scene that owns all scene-local bake output.</returns>
        private static Scene CreateDisposableBakeScene(
            string fixtureScenePath,
            LightingSettings settings,
            DisposableBakeScope scope
        )
        {
            Assert.That(
                fixtureScenePath,
                Is.Not.EqualTo(DisposableBakeScenePath),
                "The fixture scene must not already be in the disposable bake namespace."
            );
            Assert.That(
                settings,
                Is.Not.Null,
                "The validated fixture Lighting Settings asset must remain available for the disposable bake."
            );

            scope.artifactRootExisted = AssetDatabase.IsValidFolder(
                DisposableArtifactRootDirectory
            );
            scope.bakeDirectoryExisted = AssetDatabase.IsValidFolder(DisposableBakeDirectory);
            if (!scope.artifactRootExisted)
            {
                Assert.That(
                    AssetDatabase.CreateFolder("Assets", "Artifacts"),
                    Is.Not.Empty,
                    "Could not create the manifest-excluded Unity artifact root."
                );
            }

            if (!scope.bakeDirectoryExisted)
            {
                Assert.That(
                    AssetDatabase.CreateFolder(
                        DisposableArtifactRootDirectory,
                        "ProgressiveCpuBake"
                    ),
                    Is.Not.Empty,
                    "Could not create the disposable Unity bake directory."
                );
            }

            scope.scenePath = AssetDatabase.GenerateUniqueAssetPath(DisposableBakeScenePath);
            Assert.That(
                AssetDatabase.CopyAsset(fixtureScenePath, scope.scenePath),
                Is.True,
                $"Could not copy fixed fixture scene '{fixtureScenePath}' into '{scope.scenePath}'."
            );
            AssetDatabase.ImportAsset(
                scope.scenePath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
            );

            scope.scene = EditorSceneManager.OpenScene(scope.scenePath, OpenSceneMode.Additive);
            Assert.That(
                SceneManager.SetActiveScene(scope.scene),
                Is.True,
                $"Could not activate disposable bake scene '{scope.scenePath}'."
            );
            Lightmapping.lightingDataAsset = null;
            Assert.That(
                Lightmapping.lightingDataAsset,
                Is.Null,
                $"Disposable bake scene '{scope.scenePath}' still references fixture-owned lighting data."
            );
            Lightmapping.SetLightingSettingsForScene(scope.scene, settings);
            Assert.That(
                Lightmapping.GetLightingSettingsForScene(scope.scene),
                Is.SameAs(settings),
                $"Disposable bake scene '{scope.scenePath}' did not retain the read-only fixture Lighting Settings asset."
            );
            EditorSceneManager.MarkSceneDirty(scope.scene);
            Assert.That(
                EditorSceneManager.SaveScene(scope.scene),
                Is.True,
                $"Could not persist detached lighting ownership for disposable bake scene '{scope.scenePath}'."
            );
            return scope.scene;
        }

        /// <summary>Restores a valid prior scene setup or closes the disposable scene opened by this fixture.</summary>
        /// <param name="previousSceneSetup">The scene setup captured before the fixture opened any scenes.</param>
        /// <param name="scope">The scope that owns the disposable bake scene.</param>
        private static void RestoreSceneBaselineOrCloseFixtureScenes(
            SceneSetup[] previousSceneSetup,
            DisposableBakeScope scope
        )
        {
            if (IsRestorableSceneSetup(previousSceneSetup))
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
                return;
            }

            CloseOwnedScene(scope == null ? default : scope.scene, "disposable bake");
        }

        /// <summary>Determines whether a captured scene setup satisfies Unity's restore requirements.</summary>
        /// <param name="sceneSetup">The captured scene setup to validate.</param>
        /// <returns><see langword="true"/> when the setup has loaded scenes and exactly one loaded active scene.</returns>
        private static bool IsRestorableSceneSetup(SceneSetup[] sceneSetup)
        {
            if (sceneSetup == null)
            {
                return false;
            }

            int loadedSceneCount = 0;
            int activeSceneCount = 0;
            foreach (SceneSetup scene in sceneSetup)
            {
                if (scene.isLoaded)
                {
                    loadedSceneCount++;
                }

                if (scene.isActive)
                {
                    if (!scene.isLoaded)
                    {
                        return false;
                    }

                    activeSceneCount++;
                }
            }

            return loadedSceneCount > 0 && activeSceneCount == 1;
        }

        /// <summary>Closes a loaded scene owned by this fixture without affecting any existing scene.</summary>
        /// <param name="scene">The fixture-owned scene to close.</param>
        /// <param name="sceneDescription">The description used in any failure message.</param>
        private static void CloseOwnedScene(Scene scene, string sceneDescription)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            Assert.That(
                EditorSceneManager.CloseScene(scene, true),
                Is.True,
                $"Could not close {sceneDescription} scene '{scene.path}' during cleanup."
            );
        }

        /// <summary>Deletes transient Unity bake assets after bake evidence images have been captured.</summary>
        /// <param name="scope">The disposable bake scope, or <see langword="null"/> when setup did not complete.</param>
        private static void CleanupDisposableBakeAssets(DisposableBakeScope scope)
        {
            if (scope == null)
            {
                return;
            }

            if (!scope.bakeDirectoryExisted && AssetDatabase.IsValidFolder(DisposableBakeDirectory))
            {
                Assert.That(
                    AssetDatabase.DeleteAsset(DisposableBakeDirectory),
                    Is.True,
                    $"Could not remove disposable bake assets from '{DisposableBakeDirectory}'."
                );
            }

            if (
                !scope.artifactRootExisted
                && AssetDatabase.IsValidFolder(DisposableArtifactRootDirectory)
            )
            {
                Assert.That(
                    AssetDatabase.DeleteAsset(DisposableArtifactRootDirectory),
                    Is.True,
                    $"Could not remove the transient manifest-excluded Unity artifact root '{DisposableArtifactRootDirectory}'."
                );
            }
        }

        /// <summary>Imports each module-free product and proves every release sentinel is absent from generated source.</summary>
        /// <param name="contract">The runner-provided module-free bake contract.</param>
        private static void AssertModuleFreeGeneratedSources(ConsumerValidationContract contract)
        {
            Assert.That(
                contract.hasSelectedModule,
                Is.False,
                $"Consumer run '{contract.runLabel}' bake must not select an external module."
            );
            foreach (ConsumerProductContract product in contract.products)
            {
                ConsumerValidationSupport.ImportProductShader(product, contract.runLabel);
                string source = ConsumerValidationSupport.LoadGeneratedSource(
                    product,
                    contract.runLabel
                );
                ConsumerValidationSupport.ExportGeneratedSource(
                    contract.runLabel,
                    product.shaderName,
                    source
                );
                PureBaseConsumerModuleFreeImportTests.AssertInactiveSentinels(contract, source);
            }
        }

        /// <summary>Checks the runner-provided bake configuration before the scene is opened.</summary>
        /// <param name="contract">The current consumer contract.</param>
        private static void ValidateBakeContract(ConsumerValidationContract contract)
        {
            Assert.That(
                contract.bake.scenePath,
                Is.Not.Empty,
                $"Consumer run '{contract.runLabel}' bake must provide scenePath."
            );
            Assert.That(
                contract.bake.cameraName,
                Is.Not.Empty,
                $"Consumer run '{contract.runLabel}' bake must provide cameraName."
            );
            Assert.That(
                contract.bake.requiredStaticRendererNames,
                Is.Not.Null.And.Not.Empty,
                $"Consumer run '{contract.runLabel}' bake must provide requiredStaticRendererNames."
            );
            Assert.That(
                contract.bake.minimumLightmapCount,
                Is.GreaterThan(0),
                $"Consumer run '{contract.runLabel}' bake must require at least one lightmap."
            );
            Assert.That(
                contract.bake.minimumVisiblePixelCount,
                Is.GreaterThan(0),
                $"Consumer run '{contract.runLabel}' bake must require visible pixels."
            );
            Assert.That(
                contract.bake.lightingSettingsPath,
                Is.Not.Empty,
                $"Consumer run '{contract.runLabel}' bake must provide lightingSettingsPath."
            );
            Assert.That(
                contract.bake.lightingSettingsGuid,
                Is.Not.Empty,
                $"Consumer run '{contract.runLabel}' bake must provide lightingSettingsGuid."
            );
            Assert.That(
                contract.bake.lightmapper,
                Is.EqualTo(LightingSettings.Lightmapper.ProgressiveCPU.ToString()),
                $"Consumer run '{contract.runLabel}' bake must require the Progressive CPU lightmapper."
            );
            Assert.That(
                contract.bake.bakedGi,
                Is.True,
                $"Consumer run '{contract.runLabel}' bake must require baked GI."
            );
            Assert.That(
                contract.bake.realtimeGi,
                Is.False,
                $"Consumer run '{contract.runLabel}' bake must disable realtime GI."
            );
            Assert.That(
                contract.bake.autoGenerate,
                Is.False,
                $"Consumer run '{contract.runLabel}' bake must require an on-demand bake."
            );
            Assert.That(
                contract.bake.metaReadbacks,
                Is.Not.Null.And.Length.EqualTo(RequiredProductShaderNames.Length),
                $"Consumer run '{contract.runLabel}' bake must provide four product Meta readbacks."
            );
            Assert.That(
                contract.bake.shadowEvidence,
                Is.Not.Null,
                $"Consumer run '{contract.runLabel}' bake must provide shadowEvidence."
            );
            Assert.That(
                contract.bake.variantWarmups,
                Is.Not.Null,
                $"Consumer run '{contract.runLabel}' bake must provide variantWarmups."
            );
            Assert.That(
                contract.bake.expectedVariantWarmupCount,
                Is.EqualTo(56),
                $"Consumer run '{contract.runLabel}' bake must require exactly 56 representative BIRP variant warmups."
            );
            Assert.That(
                contract.bake.variantWarmups.Length,
                Is.EqualTo(contract.bake.expectedVariantWarmupCount),
                $"Consumer run '{contract.runLabel}' bake must provide exactly {contract.bake.expectedVariantWarmupCount} variant warmup requests."
            );
        }

        /// <summary>Verifies the runner-staged Progressive CPU lighting configuration before the synchronous bake.</summary>
        /// <param name="contract">The current consumer contract.</param>
        /// <param name="scene">The opened bake scene.</param>
        /// <param name="artifact">The evidence record to update.</param>
        private static void ValidateLightingBaseline(
            ConsumerValidationContract contract,
            Scene scene,
            ConsumerBakeArtifact artifact
        )
        {
            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(
                contract.bake.lightingSettingsPath
            );
            Assert.That(
                settings,
                Is.Not.Null,
                $"Consumer run '{contract.runLabel}' did not import Lighting Settings '{contract.bake.lightingSettingsPath}'."
            );
            Assert.That(
                AssetDatabase.AssetPathToGUID(contract.bake.lightingSettingsPath),
                Is.EqualTo(contract.bake.lightingSettingsGuid),
                $"Consumer run '{contract.runLabel}' changed Lighting Settings GUID '{contract.bake.lightingSettingsPath}'."
            );
            Assert.That(
                Lightmapping.GetLightingSettingsForScene(scene),
                Is.SameAs(settings),
                $"Consumer run '{contract.runLabel}' bake scene did not reference its staged Lighting Settings asset."
            );
            Assert.That(
                settings.lightmapper,
                Is.EqualTo(LightingSettings.Lightmapper.ProgressiveCPU),
                $"Consumer run '{contract.runLabel}' bake scene does not use Progressive CPU."
            );
            Assert.That(
                settings.bakedGI,
                Is.True,
                $"Consumer run '{contract.runLabel}' bake scene does not enable baked GI."
            );
            Assert.That(
                settings.realtimeGI,
                Is.False,
                $"Consumer run '{contract.runLabel}' bake scene must disable realtime GI."
            );
            Assert.That(
                settings.autoGenerate,
                Is.False,
                $"Consumer run '{contract.runLabel}' bake scene must use an explicit on-demand bake."
            );
            artifact.lightingSettingsPath = contract.bake.lightingSettingsPath;
            artifact.lightingSettingsGuid = AssetDatabase.AssetPathToGUID(
                contract.bake.lightingSettingsPath
            );
            artifact.lightmapper = settings.lightmapper.ToString();
            artifact.bakedGi = settings.bakedGI;
            artifact.realtimeGi = settings.realtimeGI;
            artifact.autoGenerate = settings.autoGenerate;
        }

        /// <summary>Verifies baked lightmaps and required static renderer assignments.</summary>
        /// <param name="contract">The current consumer contract.</param>
        /// <param name="scene">The baked validation scene.</param>
        /// <param name="artifact">The evidence record to update.</param>
        private static void ValidateLightmaps(
            ConsumerValidationContract contract,
            Scene scene,
            ConsumerBakeArtifact artifact
        )
        {
            LightmapData[] lightmaps = LightmapSettings.lightmaps;
            artifact.lightmapCount = lightmaps == null ? 0 : lightmaps.Length;
            Assert.That(
                artifact.lightmapCount,
                Is.GreaterThanOrEqualTo(contract.bake.minimumLightmapCount),
                $"Consumer run '{contract.runLabel}' bake produced {artifact.lightmapCount} lightmaps, but expected at least {contract.bake.minimumLightmapCount}."
            );

            Dictionary<string, MeshRenderer> renderers = GetStaticMeshRenderers(scene);
            foreach (string rendererName in contract.bake.requiredStaticRendererNames)
            {
                Assert.That(
                    rendererName,
                    Is.Not.Empty,
                    $"Consumer run '{contract.runLabel}' bake configured an empty static renderer name."
                );
                Assert.That(
                    renderers.ContainsKey(rendererName),
                    Is.True,
                    $"Consumer run '{contract.runLabel}' bake did not find required static renderer '{rendererName}'."
                );
                MeshRenderer renderer = renderers[rendererName];
                artifact.staticRenderers.Add(
                    new ConsumerStaticRendererArtifact
                    {
                        name = renderer.name,
                        lightmapIndex = renderer.lightmapIndex,
                    }
                );
                Assert.That(
                    renderer.lightmapIndex,
                    Is.GreaterThanOrEqualTo(0),
                    $"Consumer run '{contract.runLabel}' baked static renderer '{renderer.name}' has no lightmap assignment."
                );
            }
        }

        /// <summary>Collects all enabled static mesh renderers by name.</summary>
        /// <param name="scene">The loaded validation scene.</param>
        /// <returns>The static renderers indexed by unique name.</returns>
        private static Dictionary<string, MeshRenderer> GetStaticMeshRenderers(Scene scene)
        {
            Dictionary<string, MeshRenderer> renderers = new Dictionary<string, MeshRenderer>(
                StringComparer.Ordinal
            );
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (
                        renderer.gameObject.isStatic
                        && renderer.enabled
                        && renderer.sharedMaterial != null
                    )
                    {
                        Assert.That(
                            renderers.ContainsKey(renderer.name),
                            Is.False,
                            $"Consumer bake scene contains duplicate static renderer name '{renderer.name}'."
                        );
                        renderers.Add(renderer.name, renderer);
                    }
                }
            }

            return renderers;
        }

        /// <summary>Renders the baked scene and exports a finite HDR readback image.</summary>
        /// <param name="contract">The current consumer contract.</param>
        /// <param name="scene">The baked validation scene.</param>
        /// <param name="artifact">The evidence record to update.</param>
        private static void CaptureReadback(
            ConsumerValidationContract contract,
            Scene scene,
            ConsumerBakeArtifact artifact
        )
        {
            Camera camera = FindCamera(scene, contract.bake.cameraName);
            RenderTexture previousTarget = camera.targetTexture;
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
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0.0f, 0.0f, RenderSize, RenderSize), 0, 0, false);
                    readback.Apply(false, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                Color[] pixels = readback.GetPixels();
                artifact.finitePixelCount = CountFinitePixels(pixels);
                artifact.visiblePixelCount = CountVisiblePixels(pixels, camera.backgroundColor);
                Assert.That(
                    artifact.finitePixelCount,
                    Is.EqualTo(pixels.Length),
                    $"Consumer run '{contract.runLabel}' bake evidence contains non-finite HDR pixels."
                );
                Assert.That(
                    artifact.visiblePixelCount,
                    Is.GreaterThanOrEqualTo(contract.bake.minimumVisiblePixelCount),
                    $"Consumer run '{contract.runLabel}' bake evidence contains only {artifact.visiblePixelCount} visible pixels."
                );
                SavePng(pixels, "bake-evidence.png");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                UnityEngine.Object.DestroyImmediate(readback);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        /// <summary>Renders each runner-selected product material through its compiled Meta pass and records its mean luminance.</summary>
        /// <param name="contract">The current consumer contract.</param>
        /// <param name="scene">The baked validation scene.</param>
        /// <param name="artifact">The evidence record to update.</param>
        private static void CaptureMetaAlbedoReadbacks(
            ConsumerValidationContract contract,
            Scene scene,
            ConsumerBakeArtifact artifact
        )
        {
            Dictionary<string, Material> materials = GetProductMaterials(scene);
            HashSet<string> shaderNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConsumerMetaReadbackContract readbackContract in contract.bake.metaReadbacks)
            {
                Assert.That(
                    readbackContract,
                    Is.Not.Null,
                    $"Consumer run '{contract.runLabel}' bake has a null Meta readback contract."
                );
                Assert.That(
                    readbackContract.materialName,
                    Is.Not.Empty,
                    $"Consumer run '{contract.runLabel}' bake Meta readback must provide materialName."
                );
                Assert.That(
                    readbackContract.shaderName,
                    Is.Not.Empty,
                    $"Consumer run '{contract.runLabel}' bake Meta readback '{readbackContract.materialName}' must provide shaderName."
                );
                ConsumerValidationSupport.ValidateRange(
                    readbackContract.meanLuminance,
                    $"bake Meta readback '{readbackContract.materialName}'.meanLuminance"
                );
                Assert.That(
                    materials.ContainsKey(readbackContract.materialName),
                    Is.True,
                    $"Consumer run '{contract.runLabel}' bake did not find Meta material '{readbackContract.materialName}'."
                );
                Material material = materials[readbackContract.materialName];
                Assert.That(
                    material.shader.name,
                    Is.EqualTo(readbackContract.shaderName),
                    $"Consumer run '{contract.runLabel}' bake Meta material '{material.name}' has an unexpected shader."
                );
                Assert.That(
                    shaderNames.Add(material.shader.name),
                    Is.True,
                    $"Consumer run '{contract.runLabel}' bake configured multiple Meta readbacks for product '{material.shader.name}'."
                );
                float meanLuminance = RenderMetaAlbedo(material, contract.runLabel);
                artifact.metaReadbacks.Add(
                    new ConsumerMetaReadbackArtifact
                    {
                        material = material.name,
                        shader = material.shader.name,
                        meanLuminance = meanLuminance,
                    }
                );
                Assert.That(
                    meanLuminance,
                    Is.InRange(
                        readbackContract.meanLuminance.minimum,
                        readbackContract.meanLuminance.maximum
                    ),
                    $"Consumer run '{contract.runLabel}' bake Meta material '{material.name}' observed mean luminance {meanLuminance}, but expected [{readbackContract.meanLuminance.minimum}, {readbackContract.meanLuminance.maximum}]."
                );
            }

            CollectionAssert.AreEquivalent(
                RequiredProductShaderNames,
                shaderNames,
                $"Consumer run '{contract.runLabel}' bake Meta readbacks did not cover the four public products."
            );
        }

        /// <summary>Collects uniquely named public-product materials from the baked scene.</summary>
        /// <param name="scene">The baked validation scene.</param>
        /// <returns>Scene materials indexed by stable material name.</returns>
        private static Dictionary<string, Material> GetProductMaterials(Scene scene)
        {
            Dictionary<string, Material> materials = new Dictionary<string, Material>(
                StringComparer.Ordinal
            );
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (
                            material != null
                            && material.shader != null
                            && material.shader.name.StartsWith(
                                "PureBase/",
                                StringComparison.Ordinal
                            )
                        )
                        {
                            if (materials.TryGetValue(material.name, out Material existingMaterial))
                            {
                                Assert.That(
                                    existingMaterial,
                                    Is.SameAs(material),
                                    $"Consumer bake scene contains different public-product materials named '{material.name}'."
                                );
                            }

                            materials[material.name] = material;
                        }
                    }
                }
            }

            return materials;
        }

        /// <summary>Draws one actual compiled Meta pass and returns a finite mean albedo luminance.</summary>
        /// <param name="sourceMaterial">The baked scene material whose Meta pass must be rendered.</param>
        /// <param name="runLabel">The current consumer run label.</param>
        /// <returns>The finite mean Meta albedo luminance.</returns>
        private static float RenderMetaAlbedo(Material sourceMaterial, string runLabel)
        {
            Material material = new Material(sourceMaterial);
            Mesh mesh = CreateScreenMesh();
            GameObject cameraObject = new GameObject("PureBase Consumer Bake Meta Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
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
            Vector4 originalVertexControl = Shader.GetGlobalVector("unity_MetaVertexControl");
            Vector4 originalFragmentControl = Shader.GetGlobalVector("unity_MetaFragmentControl");
            Vector4 originalLightmapSt = Shader.GetGlobalVector("unity_LightmapST");
            float originalOutputBoost = Shader.GetGlobalFloat("unity_OneOverOutputBoost");
            float originalMaximumOutput = Shader.GetGlobalFloat("unity_MaxOutputValue");
            try
            {
                int pass = material.FindPass("Meta");
                Assert.That(
                    pass,
                    Is.GreaterThanOrEqualTo(0),
                    $"Consumer run '{runLabel}' material '{sourceMaterial.name}' does not expose a Meta pass."
                );
                target.Create();
                camera.enabled = false;
                camera.cullingMask = 0;
                camera.transform.position = new Vector3(0.0f, 0.0f, -5.0f);
                camera.orthographic = true;
                camera.orthographicSize = 1.0f;
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
                CommandBuffer commandBuffer = new CommandBuffer
                {
                    name = "PureBase Consumer Bake Meta",
                };
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
                    $"Consumer run '{runLabel}' material '{sourceMaterial.name}' produced non-finite Meta samples."
                );
                float luminance = 0.0f;
                foreach (Color pixel in pixels)
                {
                    luminance += (pixel.r * 0.2126f) + (pixel.g * 0.7152f) + (pixel.b * 0.0722f);
                }

                return luminance / pixels.Length;
            }
            finally
            {
                Shader.SetGlobalVector("unity_MetaVertexControl", originalVertexControl);
                Shader.SetGlobalVector("unity_MetaFragmentControl", originalFragmentControl);
                Shader.SetGlobalVector("unity_LightmapST", originalLightmapSt);
                Shader.SetGlobalFloat("unity_OneOverOutputBoost", originalOutputBoost);
                Shader.SetGlobalFloat("unity_MaxOutputValue", originalMaximumOutput);
                UnityEngine.Object.DestroyImmediate(readback);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        /// <summary>Renders a runner-selected actual product ShadowCaster with and without shadows and records the silhouette delta.</summary>
        /// <param name="contract">The current consumer contract.</param>
        /// <param name="scene">The baked validation scene.</param>
        /// <param name="artifact">The evidence record to update.</param>
        private static void CaptureShadowSilhouette(
            ConsumerValidationContract contract,
            Scene scene,
            ConsumerBakeArtifact artifact
        )
        {
            ConsumerShadowEvidenceContract shadowContract = contract.bake.shadowEvidence;
            Assert.That(
                shadowContract.materialName,
                Is.Not.Empty,
                $"Consumer run '{contract.runLabel}' bake shadowEvidence must provide materialName."
            );
            Assert.That(
                shadowContract.shaderName,
                Is.Not.Empty,
                $"Consumer run '{contract.runLabel}' bake shadowEvidence must provide shaderName."
            );
            Assert.That(
                shadowContract.minimumChangedPixelCount,
                Is.GreaterThan(0),
                $"Consumer run '{contract.runLabel}' bake shadowEvidence must require changed pixels."
            );
            Assert.That(
                shadowContract.screenshotFileName,
                Is.Not.Empty,
                $"Consumer run '{contract.runLabel}' bake shadowEvidence must provide screenshotFileName."
            );
            Dictionary<string, Material> materials = GetProductMaterials(scene);
            Assert.That(
                materials.ContainsKey(shadowContract.materialName),
                Is.True,
                $"Consumer run '{contract.runLabel}' bake did not find shadow material '{shadowContract.materialName}'."
            );
            Material sourceMaterial = materials[shadowContract.materialName];
            Assert.That(
                sourceMaterial.shader.name,
                Is.EqualTo(shadowContract.shaderName),
                $"Consumer run '{contract.runLabel}' bake shadow material '{sourceMaterial.name}' has an unexpected shader."
            );

            const int FixtureLayer = 31;
            Material casterMaterial = new Material(sourceMaterial);
            Material receiverMaterial = new Material(Shader.Find("Standard"));
            GameObject cameraObject = new GameObject("PureBase Consumer Bake Shadow Camera");
            GameObject lightObject = new GameObject("PureBase Consumer Bake Shadow Light");
            GameObject receiver = GameObject.CreatePrimitive(PrimitiveType.Plane);
            GameObject caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Camera camera = cameraObject.AddComponent<Camera>();
            Light directionalLight = lightObject.AddComponent<Light>();
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
                Assert.That(
                    receiverMaterial.shader,
                    Is.Not.Null,
                    $"Consumer run '{contract.runLabel}' cannot load the Built-in Standard receiver material for ShadowCaster evidence."
                );
                cameraObject.layer = FixtureLayer;
                lightObject.layer = FixtureLayer;
                receiver.layer = FixtureLayer;
                caster.layer = FixtureLayer;
                camera.enabled = false;
                camera.cullingMask = 1 << FixtureLayer;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1.0f);
                camera.transform.position = new Vector3(0.0f, 3.0f, -7.0f);
                camera.transform.LookAt(new Vector3(0.0f, 0.5f, 0.0f));
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 30.0f;
                camera.fieldOfView = 45.0f;
                directionalLight.type = LightType.Directional;
                directionalLight.color = Color.white;
                directionalLight.intensity = 1.5f;
                directionalLight.shadows = LightShadows.Hard;
                lightObject.transform.rotation = Quaternion.Euler(55.0f, -35.0f, 0.0f);
                receiver.transform.localScale = Vector3.one * 0.8f;
                receiver.GetComponent<MeshRenderer>().sharedMaterial = receiverMaterial;
                caster.transform.position = new Vector3(0.0f, 1.0f, 0.0f);
                MeshRenderer casterRenderer = caster.GetComponent<MeshRenderer>();
                casterRenderer.sharedMaterial = casterMaterial;
                casterRenderer.shadowCastingMode = ShadowCastingMode.On;
                casterRenderer.receiveShadows = false;
                target.Create();
                camera.targetTexture = target;
                directionalLight.shadows = LightShadows.None;
                camera.Render();
                Color[] withoutShadows = ReadPixels(target, readback);
                directionalLight.shadows = LightShadows.Hard;
                camera.Render();
                Color[] withShadows = ReadPixels(target, readback);
                artifact.shadowMaterial = sourceMaterial.name;
                artifact.shadowShader = sourceMaterial.shader.name;
                artifact.shadowScreenshot = shadowContract.screenshotFileName;
                artifact.shadowChangedPixelCount = CountChangedPixels(withoutShadows, withShadows);
                SavePng(withShadows, artifact.shadowScreenshot);
                Assert.That(
                    artifact.shadowChangedPixelCount,
                    Is.GreaterThanOrEqualTo(shadowContract.minimumChangedPixelCount),
                    $"Consumer run '{contract.runLabel}' bake ShadowCaster evidence changed {artifact.shadowChangedPixelCount} pixels, but expected at least {shadowContract.minimumChangedPixelCount}."
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(readback);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(caster);
                UnityEngine.Object.DestroyImmediate(receiver);
                UnityEngine.Object.DestroyImmediate(lightObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(receiverMaterial);
                UnityEngine.Object.DestroyImmediate(casterMaterial);
            }
        }

        /// <summary>Imports and warms every explicit representative BIRP shader/pass/keyword request.</summary>
        /// <param name="contract">The current consumer contract.</param>
        /// <param name="artifact">The evidence record to update.</param>
        private static void WarmRepresentativeVariants(
            ConsumerValidationContract contract,
            ConsumerBakeArtifact artifact
        )
        {
            HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> shaderNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConsumerBirpVariantWarmupContract request in contract.bake.variantWarmups)
            {
                Assert.That(
                    request,
                    Is.Not.Null,
                    $"Consumer run '{contract.runLabel}' bake has a null variant warmup request."
                );
                Assert.That(
                    request.label,
                    Is.Not.Empty,
                    $"Consumer run '{contract.runLabel}' bake has a variant warmup request without label."
                );
                Assert.That(
                    labels.Add(request.label),
                    Is.True,
                    $"Consumer run '{contract.runLabel}' bake repeats variant warmup label '{request.label}'."
                );
                Assert.That(
                    request.shaderName,
                    Is.Not.Empty,
                    $"Consumer run '{contract.runLabel}' variant '{request.label}' must provide shaderName."
                );
                Assert.That(
                    request.shaderAssetPath,
                    Is.Not.Empty,
                    $"Consumer run '{contract.runLabel}' variant '{request.label}' must provide shaderAssetPath."
                );
                Assert.That(
                    request.passType,
                    Is.Not.Empty,
                    $"Consumer run '{contract.runLabel}' variant '{request.label}' must provide passType."
                );
                Assert.That(
                    request.keywords,
                    Is.Not.Null,
                    $"Consumer run '{contract.runLabel}' variant '{request.label}' must provide keywords, including an empty array when none are selected."
                );
                PassType passType;
                Assert.That(
                    Enum.TryParse(request.passType, false, out passType),
                    Is.True,
                    $"Consumer run '{contract.runLabel}' variant '{request.label}' has unknown PassType '{request.passType}'."
                );
                Shader shader = ConsumerValidationSupport.ImportProductShader(
                    new ConsumerProductContract
                    {
                        shaderName = request.shaderName,
                        shaderAssetPath = request.shaderAssetPath,
                    },
                    contract.runLabel
                );
                shaderNames.Add(shader.name);
                ConsumerVariantWarmupArtifact outcome = new ConsumerVariantWarmupArtifact
                {
                    label = request.label,
                    shader = shader.name,
                    passType = passType.ToString(),
                    keywords = request.keywords,
                };
                ShaderVariantCollection collection = new ShaderVariantCollection();
                try
                {
                    outcome.added = collection.Add(
                        new ShaderVariantCollection.ShaderVariant(
                            shader,
                            passType,
                            request.keywords
                        )
                    );
                    Assert.That(
                        outcome.added,
                        Is.True,
                        $"Consumer run '{contract.runLabel}' could not add representative variant '{request.label}' for '{shader.name}'."
                    );
                    collection.WarmUp();
                    outcome.warmed = collection.isWarmedUp;
                    outcome.variantCount = collection.variantCount;
                    Assert.That(
                        outcome.warmed,
                        Is.True,
                        $"Consumer run '{contract.runLabel}' did not warm representative variant '{request.label}' for '{shader.name}'."
                    );
                    Assert.That(
                        outcome.variantCount,
                        Is.EqualTo(1),
                        $"Consumer run '{contract.runLabel}' representative variant '{request.label}' did not remain a single explicit request for '{shader.name}'."
                    );
                }
                finally
                {
                    artifact.variantWarmups.Add(outcome);
                    UnityEngine.Object.DestroyImmediate(collection);
                }
            }

            Assert.That(
                artifact.variantWarmups.Count,
                Is.EqualTo(contract.bake.expectedVariantWarmupCount),
                $"Consumer run '{contract.runLabel}' did not record every representative BIRP warmup."
            );
            CollectionAssert.AreEquivalent(
                RequiredProductShaderNames,
                shaderNames,
                $"Consumer run '{contract.runLabel}' representative BIRP warmups did not cover every public product."
            );
        }

        /// <summary>Creates a full-frame mesh with matching primary and lightmap UVs for compiled Meta draws.</summary>
        /// <returns>The initialized full-frame mesh.</returns>
        private static Mesh CreateScreenMesh()
        {
            Mesh mesh = new Mesh { name = "PureBase Consumer Bake Meta Screen Mesh" };
            mesh.vertices = new[]
            {
                new Vector3(-1.0f, -1.0f, 0.0f),
                new Vector3(1.0f, -1.0f, 0.0f),
                new Vector3(1.0f, 1.0f, 0.0f),
                new Vector3(-1.0f, 1.0f, 0.0f),
            };
            Vector2[] uvs =
            {
                new Vector2(0.0f, 0.0f),
                new Vector2(1.0f, 0.0f),
                new Vector2(1.0f, 1.0f),
                new Vector2(0.0f, 1.0f),
            };
            mesh.uv = uvs;
            mesh.uv2 = uvs;
            mesh.uv3 = uvs;
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Reads a render target into CPU-visible HDR pixels.</summary>
        /// <param name="target">The rendered target.</param>
        /// <param name="readback">The reusable CPU texture.</param>
        /// <returns>The copied HDR pixels.</returns>
        private static Color[] ReadPixels(RenderTexture target, Texture2D readback)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0.0f, 0.0f, RenderSize, RenderSize), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        /// <summary>Counts pixels changed by enabling the directional-shadow path.</summary>
        /// <param name="withoutShadows">The no-shadow readback.</param>
        /// <param name="withShadows">The shadow-enabled readback.</param>
        /// <returns>The number of materially changed pixels.</returns>
        private static int CountChangedPixels(Color[] withoutShadows, Color[] withShadows)
        {
            int count = 0;
            for (int index = 0; index < withoutShadows.Length; index++)
            {
                Color delta = withoutShadows[index] - withShadows[index];
                float minimumComponent = Mathf.Min(delta.r, Mathf.Min(delta.g, delta.b));
                if (delta.maxColorComponent > 0.002f || -minimumComponent > 0.002f)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Finds the configured camera in the baked scene.</summary>
        /// <param name="scene">The baked validation scene.</param>
        /// <param name="cameraName">The required camera name.</param>
        /// <returns>The configured camera.</returns>
        private static Camera FindCamera(Scene scene, string cameraName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                {
                    if (string.Equals(camera.name, cameraName, StringComparison.Ordinal))
                    {
                        return camera;
                    }
                }
            }

            Assert.Fail(
                $"Consumer bake scene '{scene.path}' did not contain configured camera '{cameraName}'."
            );
            return null;
        }

        /// <summary>Counts finite HDR pixels.</summary>
        /// <param name="pixels">The HDR pixels to inspect.</param>
        /// <returns>The finite pixel count.</returns>
        private static int CountFinitePixels(Color[] pixels)
        {
            int count = 0;
            foreach (Color pixel in pixels)
            {
                if (
                    IsFinite(pixel.r)
                    && IsFinite(pixel.g)
                    && IsFinite(pixel.b)
                    && IsFinite(pixel.a)
                )
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Counts pixels that differ materially from the camera clear color.</summary>
        /// <param name="pixels">The HDR pixels to inspect.</param>
        /// <param name="background">The camera clear color.</param>
        /// <returns>The visible pixel count.</returns>
        private static int CountVisiblePixels(Color[] pixels, Color background)
        {
            int count = 0;
            foreach (Color pixel in pixels)
            {
                float distance =
                    Mathf.Abs(pixel.r - background.r)
                    + Mathf.Abs(pixel.g - background.g)
                    + Mathf.Abs(pixel.b - background.b);
                if (distance > 0.01f)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Writes an LDR PNG projection of HDR evidence beneath the runner-provided artifact directory.</summary>
        /// <param name="pixels">The pixels to encode.</param>
        /// <param name="fileName">The artifact filename.</param>
        private static void SavePng(Color[] pixels, string fileName)
        {
            Texture2D image = new Texture2D(
                RenderSize,
                RenderSize,
                TextureFormat.RGBA32,
                false,
                true
            );
            try
            {
                image.SetPixels(pixels);
                image.Apply(false, false);
                File.WriteAllBytes(
                    Path.Combine(ConsumerValidationSupport.GetArtifactDirectory(), fileName),
                    image.EncodeToPNG()
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        /// <summary>Determines whether a scalar is finite.</summary>
        /// <param name="value">The scalar to inspect.</param>
        /// <returns><see langword="true"/> when the value is finite.</returns>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>Tracks disposable Unity assets so cleanup can also handle partial setup failures.</summary>
        private sealed class DisposableBakeScope
        {
            /// <summary>Stores the loaded disposable bake scene.</summary>
            public Scene scene;

            /// <summary>Stores the copied disposable scene asset path.</summary>
            public string scenePath;

            /// <summary>Stores whether the shared mutable asset root existed before this test.</summary>
            public bool artifactRootExisted;

            /// <summary>Stores whether the bake-specific artifact directory existed before this test.</summary>
            public bool bakeDirectoryExisted;
        }

        /// <summary>Stores machine-readable bake evidence.</summary>
        [Serializable]
        private sealed class ConsumerBakeArtifact
        {
            /// <summary>Stores the current consumer run label.</summary>
            public string runLabel;

            /// <summary>Stores the baked scene path.</summary>
            public string scenePath;

            /// <summary>Stores the Unity version.</summary>
            public string unityVersion;

            /// <summary>Stores the graphics device.</summary>
            public string graphicsDevice;

            /// <summary>Stores the staged Lighting Settings asset path.</summary>
            public string lightingSettingsPath;

            /// <summary>Stores the observed staged Lighting Settings GUID.</summary>
            public string lightingSettingsGuid;

            /// <summary>Stores the configured lightmapper enum name.</summary>
            public string lightmapper;

            /// <summary>Stores whether baked global illumination was enabled.</summary>
            public bool bakedGi;

            /// <summary>Stores whether realtime global illumination was enabled.</summary>
            public bool realtimeGi;

            /// <summary>Stores whether automatic lightmap generation was enabled.</summary>
            public bool autoGenerate;

            /// <summary>Stores whether the synchronous bake started.</summary>
            public bool bakeStarted;

            /// <summary>Stores the produced lightmap count.</summary>
            public int lightmapCount;

            /// <summary>Stores static renderer lightmap assignments.</summary>
            public List<ConsumerStaticRendererArtifact> staticRenderers =
                new List<ConsumerStaticRendererArtifact>();

            /// <summary>Stores the finite HDR pixel count.</summary>
            public int finitePixelCount;

            /// <summary>Stores the visible HDR pixel count.</summary>
            public int visiblePixelCount;

            /// <summary>Stores actual compiled product Meta readbacks.</summary>
            public List<ConsumerMetaReadbackArtifact> metaReadbacks =
                new List<ConsumerMetaReadbackArtifact>();

            /// <summary>Stores the material cloned for ShadowCaster evidence.</summary>
            public string shadowMaterial;

            /// <summary>Stores the product shader used for ShadowCaster evidence.</summary>
            public string shadowShader;

            /// <summary>Stores the shadow PNG evidence filename.</summary>
            public string shadowScreenshot;

            /// <summary>Stores the number of pixels changed by actual directional shadows.</summary>
            public int shadowChangedPixelCount;

            /// <summary>Stores every explicit BIRP variant warmup result.</summary>
            public List<ConsumerVariantWarmupArtifact> variantWarmups =
                new List<ConsumerVariantWarmupArtifact>();
        }

        /// <summary>Stores one baked static renderer assignment.</summary>
        [Serializable]
        private sealed class ConsumerStaticRendererArtifact
        {
            /// <summary>Stores the renderer name.</summary>
            public string name;

            /// <summary>Stores the assigned lightmap index.</summary>
            public int lightmapIndex;
        }

        /// <summary>Stores one actual compiled product Meta readback result.</summary>
        [Serializable]
        private sealed class ConsumerMetaReadbackArtifact
        {
            /// <summary>Stores the material name.</summary>
            public string material;

            /// <summary>Stores the product shader name.</summary>
            public string shader;

            /// <summary>Stores the observed mean Meta albedo luminance.</summary>
            public float meanLuminance;
        }

        /// <summary>Stores one explicit BIRP variant warmup result.</summary>
        [Serializable]
        private sealed class ConsumerVariantWarmupArtifact
        {
            /// <summary>Stores the runner-provided request label.</summary>
            public string label;

            /// <summary>Stores the warmed public shader name.</summary>
            public string shader;

            /// <summary>Stores the warmed BIRP pass enum name.</summary>
            public string passType;

            /// <summary>Stores the exact warmed shader keywords.</summary>
            public string[] keywords;

            /// <summary>Stores whether Unity accepted the explicit request.</summary>
            public bool added;

            /// <summary>Stores whether Unity reported the collection warmed.</summary>
            public bool warmed;

            /// <summary>Stores the explicit collection variant count.</summary>
            public int variantCount;
        }
    }
}
