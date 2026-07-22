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

// Validates runner-selected consumer imports, generated source, and BIRP runtime samples.

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
    /// <summary>Validates module-free public product imports and their generated-source contracts.</summary>
    public sealed class PureBaseConsumerModuleFreeImportTests
    {
        /// <summary>Defines the stable public product shader names required for a module-free consumer import.</summary>
        private static readonly string[] RequiredProductShaderNames = { "PureBase/Unlit", "PureBase/Toon", "PureBase/PBR", "PureBase/Hybrid" };

        /// <summary>Imports all runner-configured module-free products and checks their public and generated contracts.</summary>
        [Test]
        public void ModuleFreeProductsCompileWithConfiguredPassPropertyAndSourceContracts()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            Assert.That(contract.runKind, Is.EqualTo("module-free"), $"Consumer run '{contract.runLabel}' must use runKind 'module-free' for this test.");
            Assert.That(contract.hasSelectedModule, Is.False, $"Module-free consumer run '{contract.runLabel}' must not select an external module.");
            Assert.That(contract.selectedModule == null || IsEmptySelectedModule(contract.selectedModule), Is.True, $"Module-free consumer run '{contract.runLabel}' must not provide external module data.");
            AssertRequiredProductSet(contract);

            foreach (ConsumerProductContract product in contract.products)
            {
                Shader shader = ConsumerValidationSupport.ImportProductShader(product, contract.runLabel);
                CollectionAssert.AreEqual(product.expectedPassNames, ConsumerValidationSupport.GetPassNames(shader), $"Module-free consumer run '{contract.runLabel}' changed pass layout for '{product.shaderName}'.");
                CollectionAssert.AreEquivalent(product.expectedVisiblePropertyNames, ConsumerValidationSupport.GetVisiblePropertyNames(shader), $"Module-free consumer run '{contract.runLabel}' changed visible properties for '{product.shaderName}'.");
                string source = ConsumerValidationSupport.LoadGeneratedSource(product, contract.runLabel);
                ConsumerValidationSupport.ExportGeneratedSource(contract.runLabel, product.shaderName, source);
                AssertGlobalFragments(contract, product, source);
                AssertPassContracts(contract, product, source, false);
                AssertInactiveSentinels(contract, source);
            }
        }

        /// <summary>Checks that a JsonUtility-restored module payload contains no selected-module data.</summary>
        /// <param name="module">The optional module payload from the runner contract.</param>
        /// <returns><see langword="true"/> when the payload is absent or empty.</returns>
        private static bool IsEmptySelectedModule(ConsumerModuleContract module)
        {
            return string.IsNullOrEmpty(module.phase)
                && string.IsNullOrEmpty(module.moduleUniqueId)
                && string.IsNullOrEmpty(module.propertyName)
                && string.IsNullOrEmpty(module.sentinel);
        }

        /// <summary>Checks that a consumer contract covers exactly the four public product shaders.</summary>
        /// <param name="contract">The runner-provided module-free contract.</param>
        internal static void AssertRequiredProductSet(ConsumerValidationContract contract)
        {
            Assert.That(contract.products.Length, Is.EqualTo(RequiredProductShaderNames.Length), $"Module-free consumer run '{contract.runLabel}' must configure exactly four public products.");
            HashSet<string> actualNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (ConsumerProductContract product in contract.products)
            {
                Assert.That(actualNames.Add(product.shaderName), Is.True, $"Module-free consumer run '{contract.runLabel}' configured product '{product.shaderName}' more than once.");
            }

            CollectionAssert.AreEquivalent(RequiredProductShaderNames, actualNames, $"Module-free consumer run '{contract.runLabel}' did not configure the complete public product set.");
        }

        /// <summary>Checks non-pass-specific required and forbidden source fragments.</summary>
        /// <param name="contract">The runner-provided contract.</param>
        /// <param name="product">The inspected product.</param>
        /// <param name="source">The complete generated source.</param>
        internal static void AssertGlobalFragments(ConsumerValidationContract contract, ConsumerProductContract product, string source)
        {
            foreach (string fragment in EmptyWhenNull(product.requiredSourceFragments))
            {
                Assert.That(fragment, Is.Not.Empty, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' has an empty required source fragment.");
                StringAssert.Contains(fragment, source, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' did not retain required source fragment '{fragment}'.");
            }

            foreach (string fragment in EmptyWhenNull(product.forbiddenSourceFragments))
            {
                Assert.That(fragment, Is.Not.Empty, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' has an empty forbidden source fragment.");
                Assert.That(source.IndexOf(fragment, StringComparison.Ordinal), Is.LessThan(0), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' retained forbidden source fragment '{fragment}'.");
            }
        }

        /// <summary>Checks bounded pass source fragments and selected-module sentinel counts.</summary>
        /// <param name="contract">The runner-provided contract.</param>
        /// <param name="product">The inspected product.</param>
        /// <param name="source">The complete generated source.</param>
        /// <param name="selectedModuleRequired">Whether every pass contract must declare a selected sentinel count.</param>
        internal static void AssertPassContracts(ConsumerValidationContract contract, ConsumerProductContract product, string source, bool selectedModuleRequired)
        {
            Assert.That(product.expectedPassNames, Is.Not.Null.And.Not.Empty, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' must provide expectedPassNames.");
            Assert.That(product.passContracts, Is.Not.Null.And.Not.Empty, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' must provide pass contracts.");
            Assert.That(product.passContracts.Length, Is.EqualTo(product.expectedPassNames.Length), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' must provide exactly one ordered pass contract for every expected pass.");

            HashSet<string> expectedPassNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (string expectedPassName in product.expectedPassNames)
            {
                Assert.That(expectedPassName, Is.Not.Empty, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' has an empty expected pass name.");
                Assert.That(expectedPassNames.Add(expectedPassName), Is.True, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' declares expected pass '{expectedPassName}' more than once.");
            }

            int expectedSelectedSentinelCount = 0;
            for (int passIndex = 0; passIndex < product.passContracts.Length; passIndex++)
            {
                ConsumerPassContract passContract = product.passContracts[passIndex];
                Assert.That(passContract, Is.Not.Null, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' has a null pass contract.");
                Assert.That(passContract.passName, Is.Not.Empty, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' has a pass contract without passName.");
                Assert.That(passContract.passName, Is.EqualTo(product.expectedPassNames[passIndex]), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass contract {passIndex} must target expected pass '{product.expectedPassNames[passIndex]}'.");
                if (passIndex + 1 < product.expectedPassNames.Length)
                {
                    Assert.That(passContract.nextPassName, Is.EqualTo(product.expectedPassNames[passIndex + 1]), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passContract.passName}' must end at its immediate following pass.");
                }
                else
                {
                    Assert.That(string.IsNullOrEmpty(passContract.nextPassName), Is.True, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' final pass '{passContract.passName}' must use an empty nextPassName.");
                }

                string passSource = ConsumerValidationSupport.GetPassSource(source, passContract.passName, passContract.nextPassName, contract.runLabel, product.shaderName);
                foreach (string fragment in EmptyWhenNull(passContract.requiredFragments))
                {
                    Assert.That(fragment, Is.Not.Empty, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passContract.passName}' has an empty required fragment.");
                    StringAssert.Contains(fragment, passSource, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passContract.passName}' did not retain '{fragment}'.");
                }

                foreach (string fragment in EmptyWhenNull(passContract.forbiddenFragments))
                {
                    Assert.That(fragment, Is.Not.Empty, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passContract.passName}' has an empty forbidden fragment.");
                    Assert.That(passSource.IndexOf(fragment, StringComparison.Ordinal), Is.LessThan(0), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passContract.passName}' retained forbidden fragment '{fragment}'.");
                }

                if (selectedModuleRequired)
                {
                    Assert.That(passContract.selectedSentinelCount, Is.GreaterThanOrEqualTo(0), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passContract.passName}' must provide a selected sentinel count.");
                    Assert.That(ConsumerValidationSupport.CountOccurrences(passSource, contract.selectedModule.sentinel), Is.EqualTo(passContract.selectedSentinelCount), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passContract.passName}' has an unexpected selected sentinel count.");
                    expectedSelectedSentinelCount += passContract.selectedSentinelCount;
                }
            }

            if (selectedModuleRequired)
            {
                Assert.That(ConsumerValidationSupport.CountOccurrences(source, contract.selectedModule.sentinel), Is.EqualTo(expectedSelectedSentinelCount), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' retained the selected sentinel outside its configured pass boundaries.");
            }
        }

        /// <summary>Proves every runner-declared inactive sentinel is absent from generated source.</summary>
        /// <param name="contract">The runner-provided contract.</param>
        /// <param name="source">The generated source to inspect.</param>
        internal static void AssertInactiveSentinels(ConsumerValidationContract contract, string source)
        {
            Assert.That(contract.inactiveSentinels, Is.Not.Null, $"Consumer run '{contract.runLabel}' must provide inactiveSentinels.");
            HashSet<string> uniqueSentinels = new HashSet<string>(StringComparer.Ordinal);
            foreach (string sentinel in contract.inactiveSentinels)
            {
                Assert.That(sentinel, Is.Not.Empty, $"Consumer run '{contract.runLabel}' configured an empty inactive sentinel.");
                Assert.That(uniqueSentinels.Add(sentinel), Is.True, $"Consumer run '{contract.runLabel}' configured inactive sentinel '{sentinel}' more than once.");
                Assert.That(ConsumerValidationSupport.CountOccurrences(source, sentinel), Is.Zero, $"Consumer run '{contract.runLabel}' retained inactive sentinel '{sentinel}' in generated source.");
            }
        }

        /// <summary>Returns an empty sequence for an omitted JSON array.</summary>
        /// <param name="values">The optional JSON array.</param>
        /// <returns>The supplied values or an empty array.</returns>
        internal static string[] EmptyWhenNull(string[] values)
        {
            return values ?? Array.Empty<string>();
        }
    }

    /// <summary>Records standard-morph generated-source pass counts without requiring one expected count shape.</summary>
    public sealed class PureBaseConsumerStandardMorphObservationTests
    {
        /// <summary>Identifies the versioned standard-morph observation artifact.</summary>
        private const string ObservationArtifactFileName = "standard-morph-observation.json";

        /// <summary>Records standard-morph pass counts and generated-source evidence for all public products.</summary>
        [Test]
        public void StandardMorphProductsRecordPassCountObservations()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            ValidateStandardMorphContract(contract);

            List<ConsumerStandardMorphProductObservationArtifact> products = new List<ConsumerStandardMorphProductObservationArtifact>();
            ConsumerStandardMorphObservationArtifact artifact = new ConsumerStandardMorphObservationArtifact
            {
                schemaName = "purebase-standard-morph-observation",
                schemaVersion = 1,
                runLabel = contract.runLabel,
                runKind = contract.runKind,
                selectedModulePhase = contract.selectedModule.phase,
                selectedModuleUniqueId = contract.selectedModule.moduleUniqueId,
                selectedModuleSentinel = contract.selectedModule.sentinel
            };

            try
            {
                foreach (ConsumerProductContract product in contract.products)
                {
                    Shader shader = ConsumerValidationSupport.ImportProductShader(product, contract.runLabel);
                    CollectionAssert.AreEqual(product.expectedPassNames, ConsumerValidationSupport.GetPassNames(shader), $"Consumer run '{contract.runLabel}' changed pass layout for '{product.shaderName}'.");
                    AssertSelectedProductProperties(contract, product, shader);

                    string source = ConsumerValidationSupport.LoadGeneratedSource(product, contract.runLabel);
                    ConsumerValidationSupport.ExportGeneratedSource(contract.runLabel, product.shaderName, source);
                    PureBaseConsumerModuleFreeImportTests.AssertGlobalFragments(contract, product, source);
                    PureBaseConsumerModuleFreeImportTests.AssertPassContracts(contract, product, source, false);

                    ConsumerStandardMorphProductObservationArtifact productArtifact = new ConsumerStandardMorphProductObservationArtifact
                    {
                        shaderName = product.shaderName,
                        compiled = true,
                        supported = true,
                        generatedSourceArtifactFileName = ConsumerValidationSupport.GetGeneratedSourceArtifactFileName(contract.runLabel, product.shaderName),
                        passCounts = ObservePassCounts(contract, product, source),
                        inactiveSentinels = ObserveInactiveSentinels(contract, source)
                    };
                    products.Add(productArtifact);
                }
            }
            finally
            {
                artifact.products = products.ToArray();
                File.WriteAllText(Path.Combine(ConsumerValidationSupport.GetArtifactDirectory(), ObservationArtifactFileName), JsonUtility.ToJson(artifact, true));
            }
        }

        /// <summary>Checks that the selected contract is the all-product standard-morph observation contract.</summary>
        /// <param name="contract">The runner-provided consumer contract.</param>
        private static void ValidateStandardMorphContract(ConsumerValidationContract contract)
        {
            Assert.That(contract.runKind, Is.EqualTo("product-phase"), $"Consumer run '{contract.runLabel}' must use runKind 'product-phase' for standard-morph observation.");
            Assert.That(contract.hasSelectedModule, Is.True, $"Consumer run '{contract.runLabel}' must select standard morph for observation.");
            Assert.That(contract.selectedModule, Is.Not.Null, $"Consumer run '{contract.runLabel}' must provide selectedModule for standard-morph observation.");
            Assert.That(contract.selectedModule.phase, Is.EqualTo("morph"), $"Consumer run '{contract.runLabel}' must select the morph phase for observation.");
            Assert.That(contract.selectedModule.moduleUniqueId, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide selectedModule.moduleUniqueId for standard-morph observation.");
            Assert.That(contract.selectedModule.sentinel, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide selectedModule.sentinel for standard-morph observation.");
            CollectionAssert.DoesNotContain(contract.inactiveSentinels, contract.selectedModule.sentinel, $"Consumer run '{contract.runLabel}' cannot declare its selected morph sentinel inactive.");
            PureBaseConsumerModuleFreeImportTests.AssertRequiredProductSet(contract);
        }

        /// <summary>Checks the public property ABI for one selected standard-morph product.</summary>
        /// <param name="contract">The runner-provided consumer contract.</param>
        /// <param name="product">The product whose public properties are inspected.</param>
        /// <param name="shader">The imported public shader.</param>
        private static void AssertSelectedProductProperties(ConsumerValidationContract contract, ConsumerProductContract product, Shader shader)
        {
            string[] visiblePropertyNames = ConsumerValidationSupport.GetVisiblePropertyNames(shader);
            if (string.IsNullOrEmpty(contract.selectedModule.propertyName))
            {
                CollectionAssert.AreEquivalent(product.expectedVisiblePropertyNames, visiblePropertyNames, $"Consumer run '{contract.runLabel}' propertyless standard-morph selection changed visible properties for '{product.shaderName}'.");
                return;
            }

            Assert.That(Array.IndexOf(visiblePropertyNames, contract.selectedModule.propertyName), Is.GreaterThanOrEqualTo(0), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' did not expose selected module property '{contract.selectedModule.propertyName}'.");
        }

        /// <summary>Records selected morph sentinel counts in every configured pass without asserting an expected count.</summary>
        /// <param name="contract">The runner-provided consumer contract.</param>
        /// <param name="product">The product whose generated passes are inspected.</param>
        /// <param name="source">The complete generated source.</param>
        /// <returns>The observed ordered pass counts.</returns>
        private static ConsumerStandardMorphPassObservationArtifact[] ObservePassCounts(ConsumerValidationContract contract, ConsumerProductContract product, string source)
        {
            ConsumerStandardMorphPassObservationArtifact[] passCounts = new ConsumerStandardMorphPassObservationArtifact[product.passContracts.Length];
            for (int index = 0; index < product.passContracts.Length; index++)
            {
                ConsumerPassContract passContract = product.passContracts[index];
                Assert.That(passContract.selectedSentinelCount, Is.GreaterThanOrEqualTo(0), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passContract.passName}' must provide a non-negative selected sentinel count.");
                string passSource = ConsumerValidationSupport.GetPassSource(source, passContract.passName, passContract.nextPassName, contract.runLabel, product.shaderName);
                passCounts[index] = new ConsumerStandardMorphPassObservationArtifact
                {
                    passName = passContract.passName,
                    selectedSentinelCount = ConsumerValidationSupport.CountOccurrences(passSource, contract.selectedModule.sentinel)
                };
            }

            return passCounts;
        }

        /// <summary>Records and enforces zero occurrences for every configured inactive sentinel.</summary>
        /// <param name="contract">The runner-provided consumer contract.</param>
        /// <param name="source">The complete generated source.</param>
        /// <returns>The observed inactive sentinel counts.</returns>
        private static ConsumerInactiveSentinelObservationArtifact[] ObserveInactiveSentinels(ConsumerValidationContract contract, string source)
        {
            Assert.That(contract.inactiveSentinels, Is.Not.Null, $"Consumer run '{contract.runLabel}' must provide inactiveSentinels.");
            ConsumerInactiveSentinelObservationArtifact[] observations = new ConsumerInactiveSentinelObservationArtifact[contract.inactiveSentinels.Length];
            HashSet<string> uniqueSentinels = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < contract.inactiveSentinels.Length; index++)
            {
                string sentinel = contract.inactiveSentinels[index];
                Assert.That(sentinel, Is.Not.Empty, $"Consumer run '{contract.runLabel}' configured an empty inactive sentinel.");
                Assert.That(uniqueSentinels.Add(sentinel), Is.True, $"Consumer run '{contract.runLabel}' configured inactive sentinel '{sentinel}' more than once.");
                int occurrenceCount = ConsumerValidationSupport.CountOccurrences(source, sentinel);
                observations[index] = new ConsumerInactiveSentinelObservationArtifact { sentinel = sentinel, occurrenceCount = occurrenceCount };
                Assert.That(occurrenceCount, Is.Zero, $"Consumer run '{contract.runLabel}' retained inactive sentinel '{sentinel}' in generated source.");
            }

            return observations;
        }
    }

    /// <summary>Stores one versioned standard-morph observation artifact for runner classification.</summary>
    [Serializable]
    internal sealed class ConsumerStandardMorphObservationArtifact
    {
        /// <summary>Stores the stable machine-readable schema name.</summary>
        public string schemaName;

        /// <summary>Stores the machine-readable schema version.</summary>
        public int schemaVersion;

        /// <summary>Stores the runner-provided observation label.</summary>
        public string runLabel;

        /// <summary>Stores the runner-provided validation lane.</summary>
        public string runKind;

        /// <summary>Stores the selected Shader-Core phase.</summary>
        public string selectedModulePhase;

        /// <summary>Stores the selected Shader-Core module identity.</summary>
        public string selectedModuleUniqueId;

        /// <summary>Stores the selected generated-source sentinel.</summary>
        public string selectedModuleSentinel;

        /// <summary>Stores observations in the contract's public product order.</summary>
        public ConsumerStandardMorphProductObservationArtifact[] products;
    }

    /// <summary>Stores one public product's standard-morph import and source observations.</summary>
    [Serializable]
    internal sealed class ConsumerStandardMorphProductObservationArtifact
    {
        /// <summary>Stores the public shader name.</summary>
        public string shaderName;

        /// <summary>Stores whether the imported product had no compiler errors.</summary>
        public bool compiled;

        /// <summary>Stores whether the imported product was supported.</summary>
        public bool supported;

        /// <summary>Stores the deterministic generated-source evidence filename under the artifact directory.</summary>
        public string generatedSourceArtifactFileName;

        /// <summary>Stores observed selected-sentinel counts in declared pass order.</summary>
        public ConsumerStandardMorphPassObservationArtifact[] passCounts;

        /// <summary>Stores observed inactive-sentinel counts.</summary>
        public ConsumerInactiveSentinelObservationArtifact[] inactiveSentinels;
    }

    /// <summary>Stores one generated pass's selected morph sentinel count.</summary>
    [Serializable]
    internal sealed class ConsumerStandardMorphPassObservationArtifact
    {
        /// <summary>Stores the generated pass name.</summary>
        public string passName;

        /// <summary>Stores the observed selected morph sentinel count.</summary>
        public int selectedSentinelCount;
    }

    /// <summary>Stores one inactive sentinel's generated-source occurrence count.</summary>
    [Serializable]
    internal sealed class ConsumerInactiveSentinelObservationArtifact
    {
        /// <summary>Stores the inactive sentinel.</summary>
        public string sentinel;

        /// <summary>Stores the observed generated-source occurrence count.</summary>
        public int occurrenceCount;
    }

    /// <summary>Materializes Unity-owned scene template settings through a disposable EditMode scene lifecycle.</summary>
    public sealed class PureBaseConsumerSceneTemplateBootstrapTests
    {
        /// <summary>Identifies the manifest-excluded Unity asset root used only by this bootstrap test.</summary>
        private const string DisposableArtifactRootDirectory = "Assets/Artifacts";

        /// <summary>Identifies the disposable directory that owns bootstrap scene assets.</summary>
        private const string DisposableSceneDirectory = DisposableArtifactRootDirectory + "/SceneTemplateBootstrap";

        /// <summary>Identifies the Unity-owned settings file expected after a normal scene lifecycle.</summary>
        private const string SceneTemplateSettingsProjectRelativePath = "ProjectSettings/SceneTemplateSettings.json";

        /// <summary>Creates, saves, reopens, and saves a disposable scene so Unity materializes its scene template settings.</summary>
        [Test]
        public void DisposableSceneLifecycleMaterializesSceneTemplateSettings()
        {
            SceneSetup[] previousSceneSetup = EditorSceneManager.GetSceneManagerSetup();
            bool restorePreviousSceneSetup = IsRestorableSceneSetup(previousSceneSetup);
            bool artifactRootExisted = AssetDatabase.IsValidFolder(DisposableArtifactRootDirectory);
            bool sceneDirectoryExisted = AssetDatabase.IsValidFolder(DisposableSceneDirectory);
            Scene scene = default;
            string scenePath = null;
            string ownerScenePath = null;
            try
            {
                CreateDisposableSceneDirectory(artifactRootExisted, sceneDirectoryExisted);
                if (!restorePreviousSceneSetup)
                {
                    ownerScenePath = ReplaceTransientStartupSceneWithSavedOwnedScene();
                }

                scenePath = AssetDatabase.GenerateUniqueAssetPath(DisposableSceneDirectory + "/SceneTemplateBootstrap.unity");
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True, $"Could not save disposable scene-template bootstrap scene '{scenePath}'.");
                Assert.That(EditorSceneManager.CloseScene(scene, true), Is.True, $"Could not close disposable scene-template bootstrap scene '{scenePath}'.");

                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                Assert.That(SceneManager.SetActiveScene(scene), Is.True, $"Could not activate disposable scene-template bootstrap scene '{scenePath}'.");
                EditorSceneManager.MarkSceneDirty(scene);
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True, $"Could not resave disposable scene-template bootstrap scene '{scenePath}'.");
                Assert.That(File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, SceneTemplateSettingsProjectRelativePath)), Is.True, "Unity did not materialize ProjectSettings/SceneTemplateSettings.json after the disposable scene lifecycle.");
            }
            finally
            {
                try
                {
                    RestoreSceneSetupOrCloseOwnedScene(previousSceneSetup, restorePreviousSceneSetup, scene);
                }
                finally
                {
                    CleanupDisposableSceneAssets(artifactRootExisted, sceneDirectoryExisted, ownerScenePath, scenePath);
                }
            }
        }

        /// <summary>Replaces only a clean transient startup scene with a saved scene owned by this test.</summary>
        /// <returns>The path of the saved owner scene that cleanup must remove.</returns>
        private static string ReplaceTransientStartupSceneWithSavedOwnedScene()
        {
            Assert.That(SceneManager.sceneCount, Is.LessThanOrEqualTo(1), "Cannot replace multiple loaded scenes while preparing the disposable scene-template bootstrap scene.");
            if (SceneManager.sceneCount == 1)
            {
                Scene startupScene = SceneManager.GetSceneAt(0);
                Assert.That(startupScene.path, Is.Empty, "Cannot replace a saved scene while preparing the disposable scene-template bootstrap scene.");
                Assert.That(startupScene.isDirty, Is.False, "Cannot replace a modified unsaved scene while preparing the disposable scene-template bootstrap scene.");
            }

            Scene ownedScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Assert.That(ownedScene.IsValid() && ownedScene.isLoaded, Is.True, "Could not create the default scene owned by the disposable scene-template bootstrap test.");
            string ownedScenePath = AssetDatabase.GenerateUniqueAssetPath(DisposableSceneDirectory + "/SceneTemplateBootstrapOwner.unity");
            Assert.That(EditorSceneManager.SaveScene(ownedScene, ownedScenePath), Is.True, $"Could not save the scene owned by the disposable scene-template bootstrap test at '{ownedScenePath}'.");
            return ownedScenePath;
        }

        /// <summary>Creates only the disposable asset directories required by this test.</summary>
        /// <param name="artifactRootExisted">Whether the shared artifact root existed before the test.</param>
        /// <param name="sceneDirectoryExisted">Whether the bootstrap-scene directory existed before the test.</param>
        private static void CreateDisposableSceneDirectory(bool artifactRootExisted, bool sceneDirectoryExisted)
        {
            if (!artifactRootExisted)
            {
                Assert.That(AssetDatabase.CreateFolder("Assets", "Artifacts"), Is.Not.Empty, "Could not create the manifest-excluded Unity artifact root.");
            }

            if (!sceneDirectoryExisted)
            {
                Assert.That(AssetDatabase.CreateFolder(DisposableArtifactRootDirectory, "SceneTemplateBootstrap"), Is.Not.Empty, "Could not create the disposable scene-template bootstrap directory.");
            }
        }

        /// <summary>Restores a valid captured scene setup or closes the scene owned by this test.</summary>
        /// <param name="previousSceneSetup">The scene setup captured before the test created any scene.</param>
        /// <param name="restorePreviousSceneSetup">Whether the captured setup is safe for Unity to restore.</param>
        /// <param name="scene">The disposable scene owned by this test.</param>
        private static void RestoreSceneSetupOrCloseOwnedScene(SceneSetup[] previousSceneSetup, bool restorePreviousSceneSetup, Scene scene)
        {
            if (restorePreviousSceneSetup)
            {
                EditorSceneManager.RestoreSceneManagerSetup(previousSceneSetup);
                return;
            }

            Scene restoredScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Assert.That(restoredScene.IsValid() && restoredScene.isLoaded, Is.True, "Could not restore the transient startup scene after the disposable scene-template bootstrap test.");
        }

        /// <summary>Determines whether a captured scene setup has exactly one loaded active scene to restore.</summary>
        /// <param name="sceneSetup">The captured scene setup to validate.</param>
        /// <returns><see langword="true"/> when Unity can restore the scene setup.</returns>
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
                    if (string.IsNullOrEmpty(scene.path))
                    {
                        return false;
                    }

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

        /// <summary>Deletes only transient assets created by this test.</summary>
        /// <param name="artifactRootExisted">Whether the shared artifact root existed before the test.</param>
        /// <param name="sceneDirectoryExisted">Whether the bootstrap-scene directory existed before the test.</param>
        /// <param name="ownerScenePath">The optional owner scene created for a transient startup scene.</param>
        /// <param name="scenePath">The disposable scene created for scene template materialization.</param>
        private static void CleanupDisposableSceneAssets(bool artifactRootExisted, bool sceneDirectoryExisted, string ownerScenePath, string scenePath)
        {
            DeleteOwnedSceneAsset(scenePath);
            DeleteOwnedSceneAsset(ownerScenePath);

            if (!sceneDirectoryExisted && AssetDatabase.IsValidFolder(DisposableSceneDirectory))
            {
                Assert.That(AssetDatabase.DeleteAsset(DisposableSceneDirectory), Is.True, "Could not remove disposable scene-template bootstrap assets.");
            }

            if (!artifactRootExisted && AssetDatabase.IsValidFolder(DisposableArtifactRootDirectory))
            {
                Assert.That(AssetDatabase.DeleteAsset(DisposableArtifactRootDirectory), Is.True, "Could not remove the transient manifest-excluded Unity artifact root.");
            }
        }

        /// <summary>Deletes a saved scene only when it was created by this test.</summary>
        /// <param name="scenePath">The generated path that this test reserved for a scene.</param>
        private static void DeleteOwnedSceneAsset(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath) || AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                return;
            }

            Assert.That(AssetDatabase.DeleteAsset(scenePath), Is.True, $"Could not remove disposable scene-template bootstrap scene '{scenePath}'.");
        }
    }

    /// <summary>Validates JSON transport and static source checks for complete ordered pass contracts.</summary>
    public sealed class PureBaseConsumerPassContractTests
    {
        /// <summary>Proves that JsonUtility preserves legacy morph pass applicability and complete source accounting.</summary>
        [Test]
        public void JsonUtilityRoundTripPreservesCompleteOrderedMorphPassContracts()
        {
            const string sentinel = "PUREBASE_TEST_PHASE_SENTINEL_MORPH";
            ConsumerValidationContract contract = new ConsumerValidationContract
            {
                runLabel = "static-morph-pass-contract",
                selectedModule = new ConsumerModuleContract
                {
                    sentinel = sentinel,
                },
                products = new[]
                {
                    new ConsumerProductContract
                    {
                        shaderName = "PureBase/Tests/Static/Morph",
                        expectedPassNames = new[] { "ForwardBase", "ForwardAdd", "ShadowCaster", "Meta" },
                        passContracts = new[]
                        {
                            new ConsumerPassContract { passName = "ForwardBase", nextPassName = "ForwardAdd", selectedSentinelCount = 1 },
                            new ConsumerPassContract { passName = "ForwardAdd", nextPassName = "ShadowCaster", selectedSentinelCount = 1 },
                            new ConsumerPassContract { passName = "ShadowCaster", nextPassName = "Meta", selectedSentinelCount = 1 },
                            new ConsumerPassContract { passName = "Meta", nextPassName = string.Empty, selectedSentinelCount = 0 },
                        },
                    },
                },
            };
            string source = "Name \"ForwardBase\"\n" + sentinel + "\nName \"ForwardAdd\"\n" + sentinel + "\nName \"ShadowCaster\"\n" + sentinel + "\nName \"Meta\"\n";

            ConsumerValidationContract restoredContract = JsonUtility.FromJson<ConsumerValidationContract>(JsonUtility.ToJson(contract));

            Assert.That(restoredContract.products, Is.Not.Null.And.Length.EqualTo(1));
            CollectionAssert.AreEqual(contract.products[0].expectedPassNames, restoredContract.products[0].expectedPassNames);
            Assert.That(restoredContract.products[0].passContracts, Is.Not.Null.And.Length.EqualTo(4));
            PureBaseConsumerModuleFreeImportTests.AssertPassContracts(restoredContract, restoredContract.products[0], source, true);
        }
    }

    /// <summary>Validates one externally selected product phase module without persisting selection state.</summary>
    public sealed class PureBaseConsumerProductPhaseTests
    {
        /// <summary>Imports all selected products and checks module identity, pass placement, and inactive sentinels.</summary>
        [Test]
        public void SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            Assert.That(contract.runKind, Is.EqualTo("product-phase"), $"Consumer run '{contract.runLabel}' must use product-phase runKind for this test.");
            Assert.That(contract.selectedModule, Is.Not.Null, $"Consumer run '{contract.runLabel}' must provide selectedModule.");
            Assert.That(contract.selectedModule.phase, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide selectedModule.phase.");
            Assert.That(contract.selectedModule.moduleUniqueId, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide selectedModule.moduleUniqueId.");
            Assert.That(contract.selectedModule.sentinel, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide selectedModule.sentinel.");
            CollectionAssert.DoesNotContain(contract.inactiveSentinels, contract.selectedModule.sentinel, $"Consumer run '{contract.runLabel}' cannot declare its selected sentinel inactive.");

            foreach (ConsumerProductContract product in contract.products)
            {
                Shader shader = ConsumerValidationSupport.ImportProductShader(product, contract.runLabel);
                string[] visiblePropertyNames = ConsumerValidationSupport.GetVisiblePropertyNames(shader);
                if (string.IsNullOrEmpty(contract.selectedModule.propertyName))
                {
                    CollectionAssert.AreEquivalent(product.expectedVisiblePropertyNames, visiblePropertyNames, $"Consumer run '{contract.runLabel}' propertyless module changed visible properties for '{product.shaderName}'.");
                }
                else
                {
                    Assert.That(Array.IndexOf(visiblePropertyNames, contract.selectedModule.propertyName), Is.GreaterThanOrEqualTo(0), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' did not expose selected module property '{contract.selectedModule.propertyName}'.");
                }

                string source = ConsumerValidationSupport.LoadGeneratedSource(product, contract.runLabel);
                ConsumerValidationSupport.ExportGeneratedSource(contract.runLabel, product.shaderName, source);
                if (!string.IsNullOrEmpty(contract.selectedModule.propertyName))
                {
                    StringAssert.Contains("[SCModule(" + contract.selectedModule.moduleUniqueId + ")]", source, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' did not retain selected module identity '{contract.selectedModule.moduleUniqueId}'.");
                    StringAssert.Contains(contract.selectedModule.propertyName, source, $"Consumer run '{contract.runLabel}' product '{product.shaderName}' did not retain selected module property '{contract.selectedModule.propertyName}'.");
                }

                PureBaseConsumerModuleFreeImportTests.AssertGlobalFragments(contract, product, source);
                PureBaseConsumerModuleFreeImportTests.AssertPassContracts(contract, product, source, true);
                PureBaseConsumerModuleFreeImportTests.AssertInactiveSentinels(contract, source);
            }
        }
    }

    /// <summary>Validates that two selected external modules remain in their configured generated-source order.</summary>
    public sealed class PureBaseConsumerModuleOrderTests
    {
        /// <summary>Checks ordered pair placement across every configured product without persisting module selection.</summary>
        [Test]
        public void ConfiguredModuleOrderAppearsOnlyInExpectedProductPasses()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            Assert.That(contract.runKind, Is.EqualTo("module-order"), $"Consumer run '{contract.runLabel}' must use module-order runKind for this test.");
            ValidateModuleOrderContract(contract);

            foreach (ConsumerProductContract product in contract.products)
            {
                ConsumerValidationSupport.ImportProductShader(product, contract.runLabel);
                string source = ConsumerValidationSupport.LoadGeneratedSource(product, contract.runLabel);
                ConsumerValidationSupport.ExportGeneratedSource(contract.runLabel, product.shaderName, source);
                AssertOrderedPasses(contract, product, source, contract.moduleOrder.presentPassNames, true);
                AssertOrderedPasses(contract, product, source, contract.moduleOrder.absentPassNames, false);
                PureBaseConsumerModuleFreeImportTests.AssertInactiveSentinels(contract, source);
            }
        }

        /// <summary>Checks the runner-provided two-module order configuration.</summary>
        /// <param name="contract">The current consumer contract.</param>
        private static void ValidateModuleOrderContract(ConsumerValidationContract contract)
        {
            ConsumerModuleOrderContract moduleOrder = contract.moduleOrder;
            Assert.That(moduleOrder, Is.Not.Null, $"Consumer run '{contract.runLabel}' must provide moduleOrder.");
            Assert.That(moduleOrder.firstModuleName, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide moduleOrder.firstModuleName.");
            Assert.That(moduleOrder.secondModuleName, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide moduleOrder.secondModuleName.");
            Assert.That(moduleOrder.firstSentinel, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide moduleOrder.firstSentinel.");
            Assert.That(moduleOrder.secondSentinel, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide moduleOrder.secondSentinel.");
            Assert.That(moduleOrder.firstSentinel, Is.Not.EqualTo(moduleOrder.secondSentinel), $"Consumer run '{contract.runLabel}' must provide distinct module-order sentinels.");
            Assert.That(moduleOrder.presentPassNames, Is.Not.Null.And.Not.Empty, $"Consumer run '{contract.runLabel}' must provide moduleOrder.presentPassNames.");
            Assert.That(moduleOrder.absentPassNames, Is.Not.Null.And.Not.Empty, $"Consumer run '{contract.runLabel}' must provide moduleOrder.absentPassNames.");
        }

        /// <summary>Checks the ordered sentinel pair in a configured group of product passes.</summary>
        /// <param name="contract">The current consumer contract.</param>
        /// <param name="product">The inspected product.</param>
        /// <param name="source">The complete generated source.</param>
        /// <param name="passNames">The configured pass names.</param>
        /// <param name="expectedPresent">Whether the ordered pair must be present.</param>
        private static void AssertOrderedPasses(ConsumerValidationContract contract, ConsumerProductContract product, string source, string[] passNames, bool expectedPresent)
        {
            HashSet<string> uniquePassNames = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < passNames.Length; index++)
            {
                string passName = passNames[index];
                Assert.That(passName, Is.Not.Empty, $"Consumer run '{contract.runLabel}' has an empty module-order pass name.");
                Assert.That(uniquePassNames.Add(passName), Is.True, $"Consumer run '{contract.runLabel}' configured module-order pass '{passName}' more than once.");
                string nextPassName = index + 1 < passNames.Length ? passNames[index + 1] : FindNextProductPass(product, passName);
                string passSource = ConsumerValidationSupport.GetPassSource(source, passName, nextPassName, contract.runLabel, product.shaderName);
                int firstIndex = passSource.IndexOf(contract.moduleOrder.firstSentinel, StringComparison.Ordinal);
                int secondIndex = passSource.IndexOf(contract.moduleOrder.secondSentinel, StringComparison.Ordinal);
                if (expectedPresent)
                {
                    Assert.That(ConsumerValidationSupport.CountOccurrences(passSource, contract.moduleOrder.firstSentinel), Is.EqualTo(1), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passName}' must contain first module sentinel exactly once.");
                    Assert.That(ConsumerValidationSupport.CountOccurrences(passSource, contract.moduleOrder.secondSentinel), Is.EqualTo(1), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passName}' must contain second module sentinel exactly once.");
                    Assert.That(secondIndex, Is.GreaterThan(firstIndex), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passName}' did not preserve module order '{contract.moduleOrder.firstModuleName}' before '{contract.moduleOrder.secondModuleName}'.");
                }
                else
                {
                    Assert.That(firstIndex, Is.LessThan(0), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passName}' retained first module-order sentinel.");
                    Assert.That(secondIndex, Is.LessThan(0), $"Consumer run '{contract.runLabel}' product '{product.shaderName}' pass '{passName}' retained second module-order sentinel.");
                }
            }
        }

        /// <summary>Finds the pass marker that bounds a requested product pass.</summary>
        /// <param name="product">The product pass contract.</param>
        /// <param name="passName">The current pass name.</param>
        /// <returns>The following pass name, or an empty string for the final pass.</returns>
        private static string FindNextProductPass(ConsumerProductContract product, string passName)
        {
            int passIndex = Array.IndexOf(product.expectedPassNames, passName);
            Assert.That(passIndex, Is.GreaterThanOrEqualTo(0), $"Product '{product.shaderName}' module-order pass '{passName}' is not declared in expectedPassNames.");
            return passIndex + 1 < product.expectedPassNames.Length ? product.expectedPassNames[passIndex + 1] : string.Empty;
        }
    }

    /// <summary>Renders runner-selected BIRP material states and records actual pixel evidence.</summary>
    public sealed class PureBaseConsumerRuntimeTests
    {
        /// <summary>Defines the readback target dimension.</summary>
        private const int RenderSize = 128;

        /// <summary>Renders every configured sample and checks its center-pixel ranges.</summary>
        [Test]
        public void ConfiguredRuntimeSamplesProduceExpectedBirpReadbacks()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            Assert.That(SystemInfo.graphicsDeviceType, Is.EqualTo(GraphicsDeviceType.Direct3D11), $"Consumer run '{contract.runLabel}' requires Direct3D11 for BIRP runtime evidence.");
            Assert.That(GraphicsSettings.currentRenderPipeline, Is.Null, $"Consumer run '{contract.runLabel}' requires the Built-in Render Pipeline for runtime evidence.");
            Assert.That(contract.runtimeSamples, Is.Not.Null.And.Not.Empty, $"Consumer run '{contract.runLabel}' must provide runtimeSamples.");

            List<ConsumerRuntimeArtifact> artifacts = new List<ConsumerRuntimeArtifact>();
            try
            {
                foreach (ConsumerRuntimeSampleContract sample in contract.runtimeSamples)
                {
                    Color color = RenderSample(contract, sample);
                    ConsumerRuntimeArtifact artifact = new ConsumerRuntimeArtifact { label = sample.label, shader = sample.shaderName, red = color.r, green = color.g, blue = color.b, alpha = color.a };
                    artifacts.Add(artifact);
                    AssertInRange(color.r, sample.red, contract.runLabel, sample.label, "red");
                    AssertInRange(color.g, sample.green, contract.runLabel, sample.label, "green");
                    AssertInRange(color.b, sample.blue, contract.runLabel, sample.label, "blue");
                    AssertInRange(color.a, sample.alpha, contract.runLabel, sample.label, "alpha");
                    if (contract.hasSelectedModule)
                    {
                        AssertSelectedModuleRuntimeDelta(contract, sample, color, artifact);
                    }
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(ConsumerValidationSupport.GetArtifactDirectory(), "runtime-readbacks.json"), JsonUtility.ToJson(new ConsumerRuntimeArtifactCollection { samples = artifacts.ToArray() }, true));
            }
        }

        /// <summary>Imports and renders one configured product sample.</summary>
        /// <param name="contract">The current consumer contract.</param>
        /// <param name="sample">The runtime sample to render.</param>
        /// <returns>The HDR center pixel.</returns>
        private static Color RenderSample(ConsumerValidationContract contract, ConsumerRuntimeSampleContract sample)
        {
            Assert.That(sample, Is.Not.Null, $"Consumer run '{contract.runLabel}' has a null runtime sample.");
            Assert.That(sample.label, Is.Not.Empty, $"Consumer run '{contract.runLabel}' has a runtime sample without label.");
            ConsumerValidationSupport.ValidateRange(sample.red, $"runtime sample '{sample.label}'.red");
            ConsumerValidationSupport.ValidateRange(sample.green, $"runtime sample '{sample.label}'.green");
            ConsumerValidationSupport.ValidateRange(sample.blue, $"runtime sample '{sample.label}'.blue");
            ConsumerValidationSupport.ValidateRange(sample.alpha, $"runtime sample '{sample.label}'.alpha");
            ConsumerProductContract product = new ConsumerProductContract { shaderName = sample.shaderName, shaderAssetPath = sample.shaderAssetPath };
            Shader shader = ConsumerValidationSupport.ImportProductShader(product, contract.runLabel);
            string source = ConsumerValidationSupport.LoadGeneratedSource(product, contract.runLabel);
            ConsumerValidationSupport.ExportGeneratedSource(contract.runLabel, product.shaderName, source);
            PureBaseConsumerModuleFreeImportTests.AssertInactiveSentinels(contract, source);
            using (ConsumerRenderFixture fixture = new ConsumerRenderFixture(shader, sample))
            {
                return fixture.Render();
            }
        }

        /// <summary>Checks the selected runtime sample against its required module-free comparison and records the observed delta.</summary>
        /// <param name="contract">The runner-provided validation contract.</param>
        /// <param name="sample">The selected runtime sample.</param>
        /// <param name="selectedColor">The observed selected-module center pixel.</param>
        /// <param name="artifact">The runtime evidence entry to update.</param>
        private static void AssertSelectedModuleRuntimeDelta(ConsumerValidationContract contract, ConsumerRuntimeSampleContract sample, Color selectedColor, ConsumerRuntimeArtifact artifact)
        {
            ConsumerRuntimeDeltaContract deltaContract = contract.runtimeDelta;
            Assert.That(deltaContract, Is.Not.Null, $"Consumer run '{contract.runLabel}' must provide runtimeDelta for selected runtime evidence.");
            Assert.That(deltaContract.sampleLabel, Is.EqualTo(sample.label), $"Consumer run '{contract.runLabel}' runtimeDelta must target runtime sample '{sample.label}'.");
            Assert.That(deltaContract.moduleFreeReference, Is.Not.Null, $"Consumer run '{contract.runLabel}' runtimeDelta must provide moduleFreeReference.");
            Assert.That(deltaContract.selectedMinusModuleFree, Is.Not.Null, $"Consumer run '{contract.runLabel}' runtimeDelta must provide selectedMinusModuleFree.");
            ValidateColorRanges(deltaContract.selectedMinusModuleFree, $"Consumer run '{contract.runLabel}' runtimeDelta.selectedMinusModuleFree");
            Assert.That(RequiresVisibleEffect(deltaContract.selectedMinusModuleFree), Is.True, $"Consumer run '{contract.runLabel}' runtimeDelta must require a nonzero selected-module effect in at least one channel.");

            Color moduleFreeReference = new Color(deltaContract.moduleFreeReference.red, deltaContract.moduleFreeReference.green, deltaContract.moduleFreeReference.blue, deltaContract.moduleFreeReference.alpha);
            Color selectedMinusModuleFree = selectedColor - moduleFreeReference;
            artifact.moduleFreeReference = ConsumerRuntimeColorArtifact.FromColor(moduleFreeReference);
            artifact.selectedMinusModuleFree = ConsumerRuntimeColorArtifact.FromColor(selectedMinusModuleFree);
            AssertInRange(selectedMinusModuleFree.r, deltaContract.selectedMinusModuleFree.red, contract.runLabel, sample.label, "selected-minus-module-free red");
            AssertInRange(selectedMinusModuleFree.g, deltaContract.selectedMinusModuleFree.green, contract.runLabel, sample.label, "selected-minus-module-free green");
            AssertInRange(selectedMinusModuleFree.b, deltaContract.selectedMinusModuleFree.blue, contract.runLabel, sample.label, "selected-minus-module-free blue");
            AssertInRange(selectedMinusModuleFree.a, deltaContract.selectedMinusModuleFree.alpha, contract.runLabel, sample.label, "selected-minus-module-free alpha");
        }

        /// <summary>Checks that every RGBA channel range is valid.</summary>
        /// <param name="ranges">The configured channel ranges.</param>
        /// <param name="description">The diagnostic description for the contract section.</param>
        private static void ValidateColorRanges(ConsumerColorRangeContract ranges, string description)
        {
            ConsumerValidationSupport.ValidateRange(ranges.red, description + ".red");
            ConsumerValidationSupport.ValidateRange(ranges.green, description + ".green");
            ConsumerValidationSupport.ValidateRange(ranges.blue, description + ".blue");
            ConsumerValidationSupport.ValidateRange(ranges.alpha, description + ".alpha");
        }

        /// <summary>Determines whether a configured channel range requires a visible selected-module effect.</summary>
        /// <param name="ranges">The selected-minus-module-free channel ranges.</param>
        /// <returns><see langword="true"/> when at least one channel excludes zero.</returns>
        private static bool RequiresVisibleEffect(ConsumerColorRangeContract ranges)
        {
            return ExcludesZero(ranges.red)
                || ExcludesZero(ranges.green)
                || ExcludesZero(ranges.blue)
                || ExcludesZero(ranges.alpha);
        }

        /// <summary>Determines whether an inclusive range excludes zero.</summary>
        /// <param name="range">The range to inspect.</param>
        /// <returns><see langword="true"/> when zero is outside the range.</returns>
        private static bool ExcludesZero(ConsumerFloatRange range)
        {
            return range.minimum > 0.0f || range.maximum < 0.0f;
        }

        /// <summary>Checks one observed channel against its configured inclusive range.</summary>
        /// <param name="actual">The observed channel value.</param>
        /// <param name="expected">The expected inclusive range.</param>
        /// <param name="runLabel">The current consumer run label.</param>
        /// <param name="sampleLabel">The runtime sample label.</param>
        /// <param name="channel">The color channel name.</param>
        private static void AssertInRange(float actual, ConsumerFloatRange expected, string runLabel, string sampleLabel, string channel)
        {
            Assert.That(actual, Is.InRange(expected.minimum, expected.maximum), $"Consumer run '{runLabel}' runtime sample '{sampleLabel}' observed {channel}={actual}, but expected [{expected.minimum}, {expected.maximum}].");
        }

        /// <summary>Stores actual runtime evidence for one sample.</summary>
        [Serializable]
        private sealed class ConsumerRuntimeArtifact
        {
            /// <summary>Stores the sample label.</summary>
            public string label;

            /// <summary>Stores the rendered shader name.</summary>
            public string shader;

            /// <summary>Stores the observed red channel.</summary>
            public float red;

            /// <summary>Stores the observed green channel.</summary>
            public float green;

            /// <summary>Stores the observed blue channel.</summary>
            public float blue;

            /// <summary>Stores the observed alpha channel.</summary>
            public float alpha;

            /// <summary>Stores the configured module-free center-pixel reference when a module is selected.</summary>
            public ConsumerRuntimeColorArtifact moduleFreeReference;

            /// <summary>Stores the observed selected-minus-module-free center-pixel delta when a module is selected.</summary>
            public ConsumerRuntimeColorArtifact selectedMinusModuleFree;
        }

        /// <summary>Stores one JSON-serializable RGBA runtime observation.</summary>
        [Serializable]
        private sealed class ConsumerRuntimeColorArtifact
        {
            /// <summary>Stores the red component.</summary>
            public float red;

            /// <summary>Stores the green component.</summary>
            public float green;

            /// <summary>Stores the blue component.</summary>
            public float blue;

            /// <summary>Stores the alpha component.</summary>
            public float alpha;

            /// <summary>Creates a serializable runtime observation from one color.</summary>
            /// <param name="color">The color to store.</param>
            /// <returns>The serialized runtime color.</returns>
            public static ConsumerRuntimeColorArtifact FromColor(Color color)
            {
                return new ConsumerRuntimeColorArtifact { red = color.r, green = color.g, blue = color.b, alpha = color.a };
            }
        }

        /// <summary>Stores all actual runtime evidence for one consumer invocation.</summary>
        [Serializable]
        private sealed class ConsumerRuntimeArtifactCollection
        {
            /// <summary>Stores the per-sample runtime observations.</summary>
            public ConsumerRuntimeArtifact[] samples;
        }

        /// <summary>Owns the temporary scene and readback resources for one deterministic BIRP product render.</summary>
        private sealed class ConsumerRenderFixture : IDisposable
        {
            /// <summary>Stores the ambient mode active before the fixture.</summary>
            private readonly AmbientMode originalAmbientMode;

            /// <summary>Stores the ambient light active before the fixture.</summary>
            private readonly Color originalAmbientLight;

            /// <summary>Stores the temporary material.</summary>
            private readonly Material material;

            /// <summary>Stores the temporary camera object.</summary>
            private readonly GameObject cameraObject;

            /// <summary>Stores the temporary mesh object.</summary>
            private readonly GameObject meshObject;

            /// <summary>Stores the temporary directional light object.</summary>
            private readonly GameObject directionalLightObject;

            /// <summary>Stores the optional temporary point light object.</summary>
            private readonly GameObject pointLightObject;

            /// <summary>Stores the HDR target.</summary>
            private readonly RenderTexture target;

            /// <summary>Stores the CPU-readable HDR texture.</summary>
            private readonly Texture2D readback;

            /// <summary>Stores the rendering camera.</summary>
            private readonly Camera camera;

            /// <summary>Creates the temporary BIRP product render fixture.</summary>
            /// <param name="shader">The imported product shader.</param>
            /// <param name="sample">The configured material and lighting state.</param>
            public ConsumerRenderFixture(Shader shader, ConsumerRuntimeSampleContract sample)
            {
                originalAmbientMode = RenderSettings.ambientMode;
                originalAmbientLight = RenderSettings.ambientLight;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = Color.black;

                material = new Material(shader);
                foreach (ConsumerFloatAssignment assignment in sample.floatAssignments ?? Array.Empty<ConsumerFloatAssignment>())
                {
                    Assert.That(assignment, Is.Not.Null, $"Runtime sample '{sample.label}' has a null float assignment.");
                    Assert.That(assignment.propertyName, Is.Not.Empty, $"Runtime sample '{sample.label}' has a float assignment without propertyName.");
                    Assert.That(material.HasProperty(assignment.propertyName), Is.True, $"Runtime sample '{sample.label}' product '{shader.name}' does not expose '{assignment.propertyName}'.");
                    material.SetFloat(assignment.propertyName, assignment.value);
                }

                cameraObject = new GameObject("PureBase Consumer Runtime Camera");
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.allowHDR = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                cameraObject.transform.position = new Vector3(0.0f, 0.0f, -3.0f);
                cameraObject.transform.rotation = Quaternion.identity;

                meshObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                meshObject.name = "PureBase Consumer Runtime Product";
                meshObject.GetComponent<Renderer>().sharedMaterial = material;

                directionalLightObject = new GameObject("PureBase Consumer Runtime Directional Light");
                Light directionalLight = directionalLightObject.AddComponent<Light>();
                directionalLight.type = LightType.Directional;
                directionalLight.intensity = 1.0f;
                directionalLightObject.transform.rotation = Quaternion.Euler(35.0f, -25.0f, 0.0f);

                pointLightObject = null;
                if (sample.includePointLight)
                {
                    pointLightObject = new GameObject("PureBase Consumer Runtime Point Light");
                    Light pointLight = pointLightObject.AddComponent<Light>();
                    pointLight.type = LightType.Point;
                    pointLight.range = 5.0f;
                    pointLight.intensity = 2.0f;
                    pointLightObject.transform.position = new Vector3(0.3f, 0.4f, -1.0f);
                }

                target = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBHalf) { useMipMap = false, autoGenerateMips = false };
                target.Create();
                camera.targetTexture = target;
                readback = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAHalf, false, true);
            }

            /// <summary>Renders the configured material state and returns the HDR center pixel.</summary>
            /// <returns>The observed center pixel.</returns>
            public Color Render()
            {
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0.0f, 0.0f, RenderSize, RenderSize), 0, 0, false);
                    readback.Apply(false, false);
                    return readback.GetPixel(RenderSize / 2, RenderSize / 2);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            /// <summary>Releases every temporary Unity object and restores global lighting state.</summary>
            public void Dispose()
            {
                RenderSettings.ambientMode = originalAmbientMode;
                RenderSettings.ambientLight = originalAmbientLight;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(readback);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(meshObject);
                UnityEngine.Object.DestroyImmediate(directionalLightObject);
                UnityEngine.Object.DestroyImmediate(pointLightObject);
            }
        }
    }
}