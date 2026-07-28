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

// Verifies the durable write-boundary transaction through Unity-discoverable EditMode tests.

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
    /// <summary>Verifies the durable write-boundary transaction through Unity-discoverable EditMode tests.</summary>
    public sealed class PureBaseRegressionBaselineGeneratorWriteBoundaryTransactionTests
    {
        /// <summary>Ensures missing canonical directories retain independent audited create, write, and import operations.</summary>
        [Test]
        public void CanonicalStorageAuditsEachOperationWhenDirectoryIsMissing()
        {
            var events = new List<string>();
            var backend = new RecordingCanonicalBaselineStorageBackend(false, events);
            var writeBoundary = new RecordingCanonicalBaselineStorageWriteBoundary(events);
            var baseline = new SceneRegressionBaseline();
            PureBaseRegressionBaselineStorage.WriteCanonicalBaseline(
                baseline,
                writeBoundary,
                backend
            );
            Assert.That(
                events,
                Is.EqualTo(new[] { "create", "audit", "write", "audit", "import", "audit" })
            );
            Assert.That(backend.WrittenJson, Is.EqualTo(JsonUtility.ToJson(baseline, true)));
        }

        /// <summary>Ensures existing canonical directories skip creation while retaining independent audited write and import operations.</summary>
        [Test]
        public void CanonicalStorageAuditsWriteAndImportWhenDirectoryAlreadyExists()
        {
            var events = new List<string>();
            var backend = new RecordingCanonicalBaselineStorageBackend(true, events);
            var writeBoundary = new RecordingCanonicalBaselineStorageWriteBoundary(events);
            PureBaseRegressionBaselineStorage.WriteCanonicalBaseline(
                new SceneRegressionBaseline(),
                writeBoundary,
                backend
            );
            Assert.That(events, Is.EqualTo(new[] { "write", "audit", "import", "audit" }));
        }

        /// <summary>Ensures a failed write audit prevents the following canonical import operation.</summary>
        [Test]
        public void CanonicalStorageFailsClosedBeforeImportWhenWriteAuditFails()
        {
            var events = new List<string>();
            var backend = new RecordingCanonicalBaselineStorageBackend(true, events);
            var writeBoundary = new RecordingCanonicalBaselineStorageWriteBoundary(events, 1);
            Assert.Throws<InvalidOperationException>(() =>
                PureBaseRegressionBaselineStorage.WriteCanonicalBaseline(
                    new SceneRegressionBaseline(),
                    writeBoundary,
                    backend
                )
            );
            Assert.That(events, Is.EqualTo(new[] { "write", "audit" }));
        }

        /// <summary>Ensures project and embedded/local package sources are durable while caches and internal resources are not.</summary>
        [Test]
        public void DurablePathClassificationUsesPackageSourceAndPhysicalResolution()
        {
            string packageRoot = Path.GetFullPath("Packages/jp.penguin.purebase");
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsDurableWorkspaceAssetPath(
                    "Assets/Unrelated/Dirty.asset"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsDurableWorkspaceAssetPath(
                    "Packages/jp.penguin.purebase/package.json"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsDurablePackageSource(
                    PackageSource.Embedded,
                    packageRoot
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsDurablePackageSource(
                    PackageSource.Local,
                    packageRoot
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsDurablePackageSource(
                    PackageSource.Registry,
                    packageRoot
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsDurablePackageSource(
                    PackageSource.Git,
                    packageRoot
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsDurablePackageSource(
                    PackageSource.Embedded,
                    Path.Combine("Library", "PackageCache", "com.example.cache")
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsDurableWorkspaceAssetPath(
                    "Library/unity default resources"
                ),
                Is.False
            );
        }

        /// <summary>Ensures nested Git administration paths are excluded without excluding neighboring package sources.</summary>
        [Test]
        public void DurableInventoryExcludesPackageGitAdministrationWithWindowsPathNormalization()
        {
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    @"Packages\jp.penguin.purebase\.git\index"
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    "Packages/jp.penguin.purebase/.git"
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    "Packages/jp.penguin.purebase/.gitignore"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    "Packages/jp.penguin.purebase/Tests/Unrelated.shader.meta"
                ),
                Is.True
            );
        }

        /// <summary>Ensures only the required parent and JSON sidecar metadata for the canonical baseline output are excluded from both audits.</summary>
        [Test]
        public void CanonicalBaselineMetadataIsExcludedOnlyForTheApprovedBaselineOutput()
        {
            const string canonicalDirectoryMetaPath =
                "Packages/jp.penguin.purebase/Tests/Baselines.meta";
            const string canonicalSidecarMetaPath =
                "Packages/jp.penguin.purebase/Tests/Baselines/birp-d3d11-2022.3.22f1.json.meta";
            Assert.That(
                PureBaseValidationSceneRegressionTests.BaselinePath,
                Is.EqualTo(
                    "Packages/jp.penguin.purebase/Tests/Baselines/birp-d3d11-2022.3.22f1.json"
                )
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.WritableCanonicalTargets,
                Does.Contain(canonicalDirectoryMetaPath)
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.WritableCanonicalTargets,
                Does.Contain(canonicalSidecarMetaPath)
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    PureBaseValidationSceneRegressionTests.BaselinePath
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableWorkspaceAssetPath(
                    PureBaseValidationSceneRegressionTests.BaselinePath
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    canonicalDirectoryMetaPath
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableWorkspaceAssetPath(
                    canonicalDirectoryMetaPath
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    canonicalSidecarMetaPath
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableWorkspaceAssetPath(
                    canonicalSidecarMetaPath
                ),
                Is.False
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    "Packages/jp.penguin.purebase/Tests/Unexpected.meta"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    "Packages/jp.penguin.purebase/Tests/Baselines/alternate.json"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    "Packages/jp.penguin.purebase/Tests/Baselines/alternate.json.meta"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    canonicalSidecarMetaPath + ".meta"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    PureBaseValidationSceneRegressionTests.BaselinePath + "/unexpected.asset"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableWorkspaceAssetPath(
                    PureBaseValidationSceneRegressionTests.BaselinePath + "/unexpected.asset"
                ),
                Is.True
            );
            Assert.That(
                PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                    "Packages/jp.penguin.purebase/Tests/Baselines/unexpected.asset"
                ),
                Is.True
            );
        }

        /// <summary>Ensures a Git index mutation is omitted from the transaction inventory.</summary>
        [Test]
        public void PackageGitIndexChangeIsIgnoredByTransactionBoundary()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var boundary = new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state);
            boundary.BeginTransaction();
            state.Inventory["Packages/jp.penguin.purebase/.git/index"] = "changed";
            Assert.DoesNotThrow(() => boundary.VerifyNoUnrelatedChanges());
        }

        /// <summary>Ensures sibling package source and metadata mutations remain fail-closed.</summary>
        /// <param name="assetPath">The non-Git package file to mutate.</param>
        [TestCase("Packages/jp.penguin.purebase/Tests/Unrelated.shader")]
        [TestCase("Packages/jp.penguin.purebase/Tests/Unrelated.shader.meta")]
        [TestCase("Packages/jp.penguin.purebase/package.json")]
        [TestCase("Packages/jp.penguin.purebase/package.json.meta")]
        public void PackageSourceOrMetaChangeFailsClosedWhenGitAdministrationIsExcluded(
            string assetPath
        ) => AssertInventoryMutationFails(state => state.Inventory[assetPath] = "changed");

        /// <summary>Ensures sibling metadata and noncanonical baseline child or sidecar deltas remain audited.</summary>
        /// <param name="assetPath">The noncanonical durable path to mutate.</param>
        [TestCase("Packages/jp.penguin.purebase/Tests/Unexpected.meta")]
        [TestCase("Packages/jp.penguin.purebase/Tests/Baselines/alternate.json")]
        [TestCase("Packages/jp.penguin.purebase/Tests/Baselines/alternate.json.meta")]
        [TestCase(
            "Packages/jp.penguin.purebase/Tests/Baselines/birp-d3d11-2022.3.22f1.json.meta.meta"
        )]
        [TestCase("Packages/jp.penguin.purebase/Tests/Baselines/unexpected.asset")]
        public void SiblingOrNoncanonicalBaselineDeltaFailsClosedInInventoryAndDirtyAudits(
            string assetPath
        )
        {
            AssertInventoryMutationFails(state => state.Inventory[assetPath] = "changed");
            var state = CreateStateWithPreexistingDirtyAsset();
            var boundary = new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state);
            boundary.BeginTransaction();
            state.DirtyAssets.Add(
                new PureBaseRegressionBaselineGenerator.DirtyAssetState(assetPath, "new-instance")
            );
            Assert.Throws<InvalidOperationException>(() => boundary.VerifyNoUnrelatedChanges());
        }

        /// <summary>Ensures an unchanged startup-dirty durable asset is preserved while both operation seams remain reachable.</summary>
        [Test]
        public void PreexistingDirtyNonSceneAssetIsAcceptedWhenInventoryAndIdentityArePreserved()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var operations = new RecordingOperations();
            Assert.DoesNotThrow(() =>
                PureBaseRegressionBaselineGenerator.Regenerate(
                    CreateValidEnvironment(),
                    operations,
                    new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state)
                )
            );
            Assert.That(operations.GenerateFixtureCallCount, Is.EqualTo(1));
            Assert.That(operations.BakeAndWriteBaselineCallCount, Is.EqualTo(1));
        }

        /// <summary>Ensures creating the canonical baseline directory metadata passes every normal regeneration audit.</summary>
        [Test]
        public void CanonicalBaselineDirectoryMetaCreationIsAcceptedThroughNormalTransactionAudits()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var operations = new CanonicalMetaMutatingOperations(state, false);
            Assert.DoesNotThrow(() =>
                PureBaseRegressionBaselineGenerator.Regenerate(
                    CreateValidEnvironment(),
                    operations,
                    new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state)
                )
            );
            Assert.That(operations.GenerateFixtureCallCount, Is.EqualTo(1));
            Assert.That(operations.BakeAndWriteBaselineCallCount, Is.EqualTo(1));
        }

        /// <summary>Ensures creating the exact canonical baseline JSON sidecar passes every normal regeneration audit.</summary>
        [Test]
        public void CanonicalBaselineSidecarMetaCreationIsAcceptedThroughNormalTransactionAudits()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var operations = new CanonicalMetaMutatingOperations(
                state,
                CanonicalMetaMutatingOperations.CanonicalSidecarMetaPath,
                false
            );
            Assert.DoesNotThrow(() =>
                PureBaseRegressionBaselineGenerator.Regenerate(
                    CreateValidEnvironment(),
                    operations,
                    new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state)
                )
            );
            Assert.That(operations.GenerateFixtureCallCount, Is.EqualTo(1));
            Assert.That(operations.BakeAndWriteBaselineCallCount, Is.EqualTo(1));
        }

        /// <summary>Ensures non-canonical durable inventory additions fail closed.</summary>
        [Test]
        public void DurableFileAdditionFailsClosed() =>
            AssertInventoryMutationFails(state =>
                state.Inventory.Add("Assets/Unrelated/Added.asset", "new")
            );

        /// <summary>Ensures non-canonical durable inventory deletions fail closed.</summary>
        [Test]
        public void DurableFileDeletionFailsClosed() =>
            AssertInventoryMutationFails(state =>
                state.Inventory.Remove("Assets/Unrelated/Existing.asset")
            );

        /// <summary>Ensures non-canonical durable content changes fail closed.</summary>
        [Test]
        public void DurableFileContentChangeFailsClosed() =>
            AssertInventoryMutationFails(state =>
                state.Inventory["Assets/Unrelated/Existing.asset"] = "changed"
            );

        /// <summary>Ensures newly dirty non-canonical durable assets fail closed.</summary>
        [Test]
        public void NewlyDirtyDurableAssetFailsClosed()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var boundary = new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state);
            boundary.BeginTransaction();
            state.DirtyAssets.Add(
                new PureBaseRegressionBaselineGenerator.DirtyAssetState(
                    "Assets/Unrelated/NewDirty.asset",
                    "instance-2"
                )
            );
            Assert.Throws<InvalidOperationException>(() => boundary.VerifyNoUnrelatedChanges());
        }

        /// <summary>Ensures the finally audit detects a non-canonical durable change after an operation throws.</summary>
        [Test]
        public void ExceptionFlowFailsClosedWhenAnOperationChangesDurableState()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var operations = new ThrowingMutatingOperations(state);
            Assert.Throws<InvalidOperationException>(() =>
                PureBaseRegressionBaselineGenerator.Regenerate(
                    CreateValidEnvironment(),
                    operations,
                    new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state)
                )
            );
            Assert.That(operations.BakeAndWriteBaselineCallCount, Is.Zero);
        }

        /// <summary>Ensures an exception after canonical baseline metadata creation reaches the finally audit without a false rejection.</summary>
        [Test]
        public void CanonicalBaselineDirectoryMetaCreationIsAcceptedByFinallyAuditAfterOperationException()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var operations = new CanonicalMetaMutatingOperations(state, true);
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PureBaseRegressionBaselineGenerator.Regenerate(
                    CreateValidEnvironment(),
                    operations,
                    new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state)
                )
            );
            Assert.That(exception.Message, Is.EqualTo("Operation failure."));
            Assert.That(operations.BakeAndWriteBaselineCallCount, Is.Zero);
        }

        /// <summary>Ensures the exact canonical baseline JSON sidecar is accepted by the finally audit after an operation exception.</summary>
        [Test]
        public void CanonicalBaselineSidecarMetaCreationIsAcceptedByFinallyAuditAfterOperationException()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var operations = new CanonicalMetaMutatingOperations(
                state,
                CanonicalMetaMutatingOperations.CanonicalSidecarMetaPath,
                true
            );
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                PureBaseRegressionBaselineGenerator.Regenerate(
                    CreateValidEnvironment(),
                    operations,
                    new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state)
                )
            );
            Assert.That(exception.Message, Is.EqualTo("Operation failure."));
            Assert.That(operations.BakeAndWriteBaselineCallCount, Is.Zero);
        }

        /// <summary>Ensures a fixture asset-creation checkpoint blocks the following asset save after an unrelated durable delta.</summary>
        [Test]
        public void FixtureCreationCheckpointPreventsFollowingSaveWhenDurableStateChanges()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var boundary = new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state);
            var persistenceOperations = new List<string>();
            boundary.BeginTransaction();
            Assert.Throws<InvalidOperationException>(() =>
            {
                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    boundary,
                    () =>
                    {
                        persistenceOperations.Add("CreateAsset");
                        state.Inventory["Assets/Unrelated/Existing.asset"] = "changed";
                    }
                );
                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    boundary,
                    () => persistenceOperations.Add("SaveAssetIfDirty")
                );
            });
            Assert.That(persistenceOperations, Is.EqualTo(new[] { "CreateAsset" }));
        }

        /// <summary>Ensures a baseline scene-save checkpoint blocks its following targeted import after an unrelated durable delta.</summary>
        [Test]
        public void BaselineSaveCheckpointPreventsFollowingImportWhenDurableStateChanges()
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var boundary = new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state);
            var persistenceOperations = new List<string>();
            boundary.BeginTransaction();
            Assert.Throws<InvalidOperationException>(() =>
            {
                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    boundary,
                    () =>
                    {
                        persistenceOperations.Add("SaveScene");
                        state.Inventory["Assets/Unrelated/Existing.asset"] = "changed";
                    }
                );
                PureBaseRegressionBaselineGenerator.PersistCanonicalOperation(
                    boundary,
                    () => persistenceOperations.Add("ImportAsset")
                );
            });
            Assert.That(persistenceOperations, Is.EqualTo(new[] { "SaveScene" }));
        }

        /// <summary>Applies one inventory mutation after a transaction snapshot and verifies it is rejected.</summary>
        /// <param name="mutation">The controlled non-canonical filesystem mutation.</param>
        private static void AssertInventoryMutationFails(Action<MutableAuditState> mutation)
        {
            var state = CreateStateWithPreexistingDirtyAsset();
            var boundary = new PureBaseRegressionBaselineGenerator.TransactionWriteBoundary(state);
            boundary.BeginTransaction();
            mutation(state);
            Assert.Throws<InvalidOperationException>(() => boundary.VerifyNoUnrelatedChanges());
        }

        /// <summary>Creates a valid fixed environment for transaction orchestration tests.</summary>
        private static PureBaseRegressionBaselineGenerator.IEnvironment CreateValidEnvironment() =>
            new TestEnvironment(
                PureBaseValidationSceneRegressionTests.ExpectedUnityVersion,
                true,
                GraphicsDeviceType.Direct3D11,
                ColorSpace.Linear
            );

        /// <summary>Creates an inventory containing one unchanged preexisting dirty package asset.</summary>
        private static MutableAuditState CreateStateWithPreexistingDirtyAsset()
        {
            var inventory = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Assets/Unrelated/Existing.asset", "original" },
                { "Packages/jp.penguin.purebase/.git/index", "original-git-index" },
                { "Packages/jp.penguin.purebase/Tests/Unrelated.shader", "shader" },
                { "Packages/jp.penguin.purebase/Tests/Unrelated.shader.meta", "meta" },
            };
            var dirtyAssets = new List<PureBaseRegressionBaselineGenerator.DirtyAssetState>
            {
                new PureBaseRegressionBaselineGenerator.DirtyAssetState(
                    "Packages/jp.penguin.purebase/Tests/Unrelated.shader",
                    "instance-1"
                ),
            };
            return new MutableAuditState(inventory, dirtyAssets);
        }

        /// <summary>Supplies fixed environment values to transaction tests.</summary>
        private sealed class TestEnvironment : PureBaseRegressionBaselineGenerator.IEnvironment
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

        /// <summary>Provides a mutable in-memory model of durable state without changing Unity assets.</summary>
        private sealed class MutableAuditState
            : PureBaseRegressionBaselineGenerator.ITransactionAuditState
        {
            /// <summary>Initializes a mutable audit-state model.</summary>
            public MutableAuditState(
                Dictionary<string, string> inventory,
                List<PureBaseRegressionBaselineGenerator.DirtyAssetState> dirtyAssets
            )
            {
                Inventory = inventory;
                DirtyAssets = dirtyAssets;
            }

            /// <summary>Gets the mutable durable filesystem inventory.</summary>
            public Dictionary<string, string> Inventory { get; }

            /// <summary>Gets the mutable dirty non-scene assets.</summary>
            public List<PureBaseRegressionBaselineGenerator.DirtyAssetState> DirtyAssets { get; }

            /// <inheritdoc />
            public void EnsureNoDirtyNonCanonicalScenes() { }

            /// <inheritdoc />
            public Dictionary<string, string> CaptureNonCanonicalDurableInventory()
            {
                var inventory = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, string> entry in Inventory)
                    if (
                        PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableInventoryAssetPath(
                            entry.Key
                        )
                    )
                        inventory.Add(entry.Key, entry.Value);
                return inventory;
            }

            /// <inheritdoc />
            public List<PureBaseRegressionBaselineGenerator.DirtyAssetState> CaptureDirtyNonCanonicalAssets()
            {
                var dirtyAssets = new List<PureBaseRegressionBaselineGenerator.DirtyAssetState>();
                foreach (
                    PureBaseRegressionBaselineGenerator.DirtyAssetState dirtyAsset in DirtyAssets
                )
                    if (
                        PureBaseRegressionBaselineGenerator.IsNonCanonicalDurableWorkspaceAssetPath(
                            dirtyAsset.AssetPath
                        )
                    )
                        dirtyAssets.Add(dirtyAsset);
                return dirtyAssets;
            }
        }

        /// <summary>Records normal regeneration operation seams without mutating the state model.</summary>
        private sealed class RecordingOperations
            : PureBaseRegressionBaselineGenerator.IRegenerationOperations
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

        /// <summary>Changes non-canonical durable state and then throws to exercise the finally audit.</summary>
        private sealed class ThrowingMutatingOperations
            : PureBaseRegressionBaselineGenerator.IRegenerationOperations
        {
            /// <summary>Stores the mutable durable-state model changed before the controlled failure.</summary>
            private readonly MutableAuditState state;

            /// <summary>Initializes an exception-path operation with its state model.</summary>
            public ThrowingMutatingOperations(MutableAuditState state)
            {
                this.state = state;
            }

            /// <summary>Gets baseline-writing calls.</summary>
            public int BakeAndWriteBaselineCallCount { get; private set; }

            /// <inheritdoc />
            public void GenerateFixture()
            {
                state.Inventory["Assets/Unrelated/Existing.asset"] = "changed";
                throw new InvalidOperationException("Operation failure.");
            }

            /// <inheritdoc />
            public void BakeAndWriteBaseline() => BakeAndWriteBaselineCallCount++;
        }

        /// <summary>Creates only the metadata required by the canonical baseline directory, optionally after simulating an operation failure.</summary>
        private sealed class CanonicalMetaMutatingOperations
            : PureBaseRegressionBaselineGenerator.IRegenerationOperations
        {
            /// <summary>Identifies the exact sidecar Unity generates beside the canonical baseline JSON.</summary>
            public const string CanonicalSidecarMetaPath =
                "Packages/jp.penguin.purebase/Tests/Baselines/birp-d3d11-2022.3.22f1.json.meta";

            /// <summary>Stores the mutable transaction state updated by simulated metadata creation.</summary>
            private readonly MutableAuditState state;

            /// <summary>Stores the exact canonical metadata path allowed by the transaction audit.</summary>
            private readonly string canonicalMetaPath;

            /// <summary>Indicates whether fixture generation throws after creating canonical metadata.</summary>
            private readonly bool throwAfterMutation;

            /// <summary>Initializes canonical metadata mutation behavior for normal and exception-flow audits.</summary>
            /// <param name="state">The mutable transaction state to update.</param>
            /// <param name="throwAfterMutation">Whether fixture generation throws after the canonical metadata appears.</param>
            public CanonicalMetaMutatingOperations(MutableAuditState state, bool throwAfterMutation)
                : this(
                    state,
                    "Packages/jp.penguin.purebase/Tests/Baselines.meta",
                    throwAfterMutation
                ) { }

            /// <summary>Initializes canonical metadata mutation behavior for one exact allowed metadata path.</summary>
            /// <param name="state">The mutable transaction state to update.</param>
            /// <param name="canonicalMetaPath">The exact allowed canonical metadata path to simulate.</param>
            /// <param name="throwAfterMutation">Whether fixture generation throws after the canonical metadata appears.</param>
            public CanonicalMetaMutatingOperations(
                MutableAuditState state,
                string canonicalMetaPath,
                bool throwAfterMutation
            )
            {
                this.state = state;
                this.canonicalMetaPath = canonicalMetaPath;
                this.throwAfterMutation = throwAfterMutation;
            }

            /// <summary>Gets the number of fixture-generation calls.</summary>
            public int GenerateFixtureCallCount { get; private set; }

            /// <summary>Gets the number of baseline-writing calls.</summary>
            public int BakeAndWriteBaselineCallCount { get; private set; }

            /// <inheritdoc />
            public void GenerateFixture()
            {
                GenerateFixtureCallCount++;
                state.Inventory[canonicalMetaPath] = "created";
                state.DirtyAssets.Add(
                    new PureBaseRegressionBaselineGenerator.DirtyAssetState(
                        canonicalMetaPath,
                        "canonical-meta-instance"
                    )
                );
                if (throwAfterMutation)
                    throw new InvalidOperationException("Operation failure.");
            }

            /// <inheritdoc />
            public void BakeAndWriteBaseline() => BakeAndWriteBaselineCallCount++;
        }

        /// <summary>Records raw canonical storage operations without transaction auditing.</summary>
        private sealed class RecordingCanonicalBaselineStorageBackend
            : ICanonicalBaselineStorageBackend
        {
            /// <summary>Indicates whether the canonical parent directory already exists.</summary>
            private readonly bool isDirectoryValid;

            /// <summary>Records the ordered storage operations.</summary>
            private readonly List<string> events;

            /// <summary>Initializes the recording storage backend.</summary>
            /// <param name="isDirectoryValid">Whether the canonical parent directory already exists.</param>
            /// <param name="events">The ordered operation log.</param>
            public RecordingCanonicalBaselineStorageBackend(
                bool isDirectoryValid,
                List<string> events
            )
            {
                this.isDirectoryValid = isDirectoryValid;
                this.events = events;
            }

            /// <summary>Gets the JSON written through this backend.</summary>
            public string WrittenJson { get; private set; }

            /// <inheritdoc />
            public bool IsDirectoryValid(string assetPath) => isDirectoryValid;

            /// <inheritdoc />
            public void CreateDirectory(string parentAssetPath, string directoryName) =>
                events.Add("create");

            /// <inheritdoc />
            public void WriteAllText(string path, string contents)
            {
                WrittenJson = contents;
                events.Add("write");
            }

            /// <inheritdoc />
            public void ImportAsset(string path) => events.Add("import");
        }

        /// <summary>Records audit checkpoints and can reject a selected checkpoint.</summary>
        private sealed class RecordingCanonicalBaselineStorageWriteBoundary
            : PureBaseRegressionBaselineGenerator.IWriteBoundary
        {
            /// <summary>Records the ordered audit operations.</summary>
            private readonly List<string> events;

            /// <summary>Identifies the audit invocation that throws, or zero when all audits pass.</summary>
            private readonly int failingAuditCall;

            /// <summary>Counts completed audit invocations.</summary>
            private int auditCallCount;

            /// <summary>Initializes the recording write boundary.</summary>
            /// <param name="events">The ordered operation log.</param>
            /// <param name="failingAuditCall">The audit invocation that throws, or zero when all audits pass.</param>
            public RecordingCanonicalBaselineStorageWriteBoundary(
                List<string> events,
                int failingAuditCall = 0
            )
            {
                this.events = events;
                this.failingAuditCall = failingAuditCall;
            }

            /// <inheritdoc />
            public void BeginTransaction() { }

            /// <inheritdoc />
            public void VerifyNoUnrelatedChanges()
            {
                auditCallCount++;
                events.Add("audit");
                if (auditCallCount == failingAuditCall)
                    throw new InvalidOperationException("Controlled audit failure.");
            }
        }
    }
}
