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

// Verifies deterministic state convergence without mutating the project singleton.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Verifies the isolated Shader-Core test-state convergence contract.</summary>
    public sealed class ShaderCoreTestStateInitializerTests
    {
        /// <summary>Ensures missing, stale, and duplicate target rows converge without changing unrelated rows.</summary>
        [Test]
        public void ConvergeRowsReplacesTargetsPreservesUnrelatedRowsAndBecomesANoOp()
        {
            var actualRows = new[]
            {
                new ShaderCoreTestStateInitializer.ShaderSettingRow(
                    "Unrelated/First",
                    new[] { "unrelated.one" }
                ),
                new ShaderCoreTestStateInitializer.ShaderSettingRow(
                    "PureBase/Tests/Host",
                    new[] { "stale.module" }
                ),
                new ShaderCoreTestStateInitializer.ShaderSettingRow(
                    "PureBase/Tests/Host",
                    new[] { "duplicate.module" }
                ),
                new ShaderCoreTestStateInitializer.ShaderSettingRow(
                    "Unrelated/Last",
                    new[] { "unrelated.two", "unrelated.three" }
                ),
            };
            var expectedRows = new Dictionary<string, string[]>
            {
                ["PureBase/Tests/Host"] = new[] { "expected.module" },
                ["PureBase/Unlit"] = System.Array.Empty<string>(),
            };

            var convergedRows = ShaderCoreTestStateInitializer.ConvergeRows(
                actualRows,
                expectedRows,
                out var changed
            );

            Assert.That(changed, Is.True);
            Assert.That(
                convergedRows.Select(row => row.ShaderName),
                Is.EqualTo(
                    new[]
                    {
                        "Unrelated/First",
                        "PureBase/Tests/Host",
                        "Unrelated/Last",
                        "PureBase/Unlit",
                    }
                )
            );
            Assert.That(convergedRows[0].Modules, Is.EqualTo(new[] { "unrelated.one" }));
            Assert.That(convergedRows[1].Modules, Is.EqualTo(new[] { "expected.module" }));
            Assert.That(
                convergedRows[2].Modules,
                Is.EqualTo(new[] { "unrelated.two", "unrelated.three" })
            );
            Assert.That(convergedRows[3].Modules, Is.Empty);

            var secondRunRows = ShaderCoreTestStateInitializer.ConvergeRows(
                convergedRows,
                expectedRows,
                out var secondRunChanged
            );
            Assert.That(secondRunChanged, Is.False);
            Assert.That(secondRunRows, Is.EqualTo(convergedRows));
        }

        /// <summary>Ensures an invalid Save contract fails before any serialized row mutation, apply, or host reimport.</summary>
        [Test]
        public void InitializeFailsClosedBeforeStateApplicationWhenSaveContractIsInvalid()
        {
            var originalRows = new[]
            {
                new ShaderCoreTestStateInitializer.ShaderSettingRow(
                    "Unrelated/Preserved",
                    new[] { "unrelated.module" }
                ),
                new ShaderCoreTestStateInitializer.ShaderSettingRow(
                    "PureBase/Tests/Host",
                    new[] { "stale.module" }
                ),
            };
            var expectedRows = new Dictionary<string, string[]>
            {
                ["PureBase/Tests/Host"] = new[] { "expected.module" },
            };
            var stateApplication = new RecordingStateApplication(originalRows);
            var stateApplicationFactory = new RecordingStateApplicationFactory(stateApplication);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ShaderCoreTestStateInitializer.Initialize(
                    expectedRows,
                    new InvalidSaveMethodReflectionContractResolver(),
                    stateApplicationFactory
                )
            );

            Assert.That(exception.Message, Does.Contain("Save"));
            Assert.That(stateApplicationFactory.CreateCallCount, Is.Zero);
            Assert.That(stateApplication.ReadRowsCallCount, Is.Zero);
            Assert.That(stateApplication.WriteRowsCallCount, Is.Zero);
            Assert.That(stateApplication.ApplyCallCount, Is.Zero);
            Assert.That(stateApplication.SaveCallCount, Is.Zero);
            Assert.That(stateApplication.ReimportCallCount, Is.Zero);
            Assert.That(stateApplication.Rows, Is.EqualTo(originalRows));
        }

        /// <summary>Ensures the manifest maps every fixed test host and preserves empty product selections.</summary>
        [Test]
        public void ManifestMapsFixedHostsAndProductShadersToExpectedSelections()
        {
            var expectedRows = ShaderCoreTestStateInitializer.LoadExpectedRows();

            Assert.That(expectedRows.Count, Is.EqualTo(17));
            Assert.That(
                expectedRows["PureBase/Tests/ShaderCore/ModuleOrder"],
                Is.EqualTo(
                    new[]
                    {
                        "jp.penguin.purebase.tests.shadercore.moduleorder.zeta",
                        "jp.penguin.purebase.tests.shadercore.moduleorder.alpha",
                    }
                )
            );
            Assert.That(
                expectedRows["PureBase/Tests/ShaderCore/ToonShadow"],
                Is.EqualTo(new[] { "jp.penguin.purebase.tests.shadercore.toonshadow" })
            );
            Assert.That(
                expectedRows["PureBase/Tests/ShaderCore/ToonOpenLitGamma"],
                Is.EqualTo(new[] { "jp.penguin.purebase.tests.shadercore.toonopenlitgamma" })
            );
            Assert.That(expectedRows["PureBase/Unlit"], Is.Empty);
            Assert.That(expectedRows["PureBase/Toon"], Is.Empty);
            Assert.That(expectedRows["PureBase/Hybrid"], Is.Empty);
            Assert.That(expectedRows["PureBase/PBR"], Is.Empty);
        }

        /// <summary>Ensures the current Shader-Core singleton reaches a stable no-op state after initialization.</summary>
        [Test]
        public void InitializeIsANoOpForTheCurrentProjectSettings()
        {
            ShaderCoreTestStateInitializer.Initialize();
            var secondRun = ShaderCoreTestStateInitializer.Initialize();

            Assert.That(secondRun.Changed, Is.False);
            Assert.That(secondRun.ReimportedAssets, Is.Empty);
        }

        /// <summary>Validates generated source sentinel counts after explicit state initialization and host import.</summary>
        [Test, Explicit("Requires compilable persistent host HLSL sources.")]
        public void InitializedHostsContainManifestSentinelsInExpectedPasses()
        {
            ShaderCoreTestStateInitializer.Initialize();
            var manifest = JsonUtility.FromJson<GeneratedSourceManifest>(
                File.ReadAllText(ShaderCoreTestStateInitializer.GetManifestPath())
            );

            Assert.That(manifest, Is.Not.Null);
            foreach (GeneratedSourceHost host in manifest.hosts)
            {
                string assetPath = FindHostAssetPath(host.shaderName);
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
                );
                var source = AssetDatabase
                    .LoadAllAssetsAtPath(assetPath)
                    .OfType<TextAsset>()
                    .SingleOrDefault(asset => asset.name == "Shader Source")
                    ?.text;
                Assert.That(
                    source,
                    Is.Not.Null.And.Not.Empty,
                    $"Host '{host.shaderName}' did not produce generated Shader Source."
                );

                foreach (string sentinel in host.expectedSentinels)
                {
                    AssertPassSentinelCount(
                        source,
                        "ForwardBase",
                        sentinel,
                        host.expectedPassSentinelCounts.ForwardBase,
                        host.shaderName
                    );
                    AssertPassSentinelCount(
                        source,
                        "ForwardAdd",
                        sentinel,
                        host.expectedPassSentinelCounts.ForwardAdd,
                        host.shaderName
                    );
                    AssertPassSentinelCount(
                        source,
                        "ShadowCaster",
                        sentinel,
                        host.expectedPassSentinelCounts.ShadowCaster,
                        host.shaderName
                    );
                    AssertPassSentinelCount(
                        source,
                        "Meta",
                        sentinel,
                        host.expectedPassSentinelCounts.Meta,
                        host.shaderName
                    );
                }

                if (host.shaderName == "PureBase/Tests/ShaderCore/ModuleOrder")
                {
                    AssertModuleOrderSentinelOrder(
                        source,
                        "ForwardBase",
                        host.expectedPassSentinelCounts.ForwardBase,
                        host.shaderName
                    );
                    AssertModuleOrderSentinelOrder(
                        source,
                        "ForwardAdd",
                        host.expectedPassSentinelCounts.ForwardAdd,
                        host.shaderName
                    );
                    AssertModuleOrderSentinelOrder(
                        source,
                        "ShadowCaster",
                        host.expectedPassSentinelCounts.ShadowCaster,
                        host.shaderName
                    );
                    AssertModuleOrderSentinelOrder(
                        source,
                        "Meta",
                        host.expectedPassSentinelCounts.Meta,
                        host.shaderName
                    );
                }

                foreach (string inactiveSentinel in host.inactiveSentinels ?? Array.Empty<string>())
                {
                    Assert.That(
                        CountOccurrences(source, inactiveSentinel),
                        Is.Zero,
                        $"Host '{host.shaderName}' emitted inactive sentinel '{inactiveSentinel}'."
                    );
                }
            }
        }

        /// <summary>Finds a persistent host asset by its imported Shader-Core shader name.</summary>
        private static string FindHostAssetPath(string shaderName)
        {
            foreach (
                string guid in AssetDatabase.FindAssets(
                    "PureBaseTest",
                    new[] { "Packages/jp.penguin.purebase/Tests/Fixtures/Hosts" }
                )
            )
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith(".scshader", StringComparison.OrdinalIgnoreCase))
                    continue;

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (shader != null && shader.name == shaderName)
                    return assetPath;
            }

            Assert.Fail($"Persistent host shader '{shaderName}' was not imported.");
            return null;
        }

        /// <summary>Asserts one sentinel's count within a named generated ShaderLab pass.</summary>
        private static void AssertPassSentinelCount(
            string source,
            string passName,
            string sentinel,
            int expectedCount,
            string shaderName
        )
        {
            string passSource = GetPassSource(source, passName, shaderName);
            Assert.That(
                CountOccurrences(passSource, sentinel),
                Is.EqualTo(expectedCount),
                $"Host '{shaderName}' pass '{passName}' emitted unexpected count for '{sentinel}'."
            );
        }

        /// <summary>Asserts that the generated module-order sentinels retain their configured order in one relevant pass.</summary>
        private static void AssertModuleOrderSentinelOrder(
            string source,
            string passName,
            int expectedCount,
            string shaderName
        )
        {
            if (expectedCount == 0)
                return;

            string passSource = GetPassSource(source, passName, shaderName);
            int zetaIndex = passSource.IndexOf(
                "PUREBASE_TEST_MODULE_ORDER_ZETA",
                StringComparison.Ordinal
            );
            int alphaIndex = passSource.IndexOf(
                "PUREBASE_TEST_MODULE_ORDER_ALPHA",
                StringComparison.Ordinal
            );
            Assert.That(
                zetaIndex,
                Is.GreaterThanOrEqualTo(0),
                $"Host '{shaderName}' pass '{passName}' did not emit the Zeta module-order sentinel."
            );
            Assert.That(
                alphaIndex,
                Is.GreaterThanOrEqualTo(0),
                $"Host '{shaderName}' pass '{passName}' did not emit the Alpha module-order sentinel."
            );
            Assert.That(
                zetaIndex,
                Is.LessThan(alphaIndex),
                $"Host '{shaderName}' pass '{passName}' emitted module-order sentinels out of order: Zeta index {zetaIndex} must be before Alpha index {alphaIndex}."
            );
        }

        /// <summary>Gets one named generated ShaderLab pass from the generated Shader Source.</summary>
        private static string GetPassSource(string source, string passName, string shaderName)
        {
            int passStart = source.IndexOf($"Name \"{passName}\"", StringComparison.Ordinal);
            Assert.That(
                passStart,
                Is.GreaterThanOrEqualTo(0),
                $"Host '{shaderName}' has no generated {passName} pass."
            );
            int nextPass = source.IndexOf(
                "\n        Pass",
                passStart + 1,
                StringComparison.Ordinal
            );
            return source.Substring(
                passStart,
                nextPass < 0 ? source.Length - passStart : nextPass - passStart
            );
        }

        /// <summary>Counts non-overlapping ordinal occurrences in a string.</summary>
        private static int CountOccurrences(string source, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }

        /// <summary>Simulates the reflection seam rejecting a missing or mismatched non-public parameterless Save method.</summary>
        private sealed class InvalidSaveMethodReflectionContractResolver
            : ShaderCoreTestStateInitializer.IReflectionContractResolver
        {
            /// <inheritdoc />
            public ShaderCoreTestStateInitializer.ProjectSettingsReflectionContract Resolve()
            {
                throw new InvalidOperationException(
                    "Shader-Core ProjectSettings.Save() did not match the required non-public parameterless method contract."
                );
            }
        }

        /// <summary>Records whether contract resolution permits creation of a serialized state application.</summary>
        private sealed class RecordingStateApplicationFactory
            : ShaderCoreTestStateInitializer.IStateApplicationFactory
        {
            private readonly ShaderCoreTestStateInitializer.IStateApplication stateApplication;

            /// <summary>Initializes a recording state application factory.</summary>
            /// <param name="stateApplication">The recording state application returned when creation is permitted.</param>
            public RecordingStateApplicationFactory(
                ShaderCoreTestStateInitializer.IStateApplication stateApplication
            )
            {
                this.stateApplication =
                    stateApplication ?? throw new ArgumentNullException(nameof(stateApplication));
            }

            /// <summary>Gets the number of requested serialized state applications.</summary>
            public int CreateCallCount { get; private set; }

            /// <inheritdoc />
            public ShaderCoreTestStateInitializer.IStateApplication Create(
                ShaderCoreTestStateInitializer.ProjectSettingsReflectionContract reflectionContract
            )
            {
                CreateCallCount++;
                return stateApplication;
            }
        }

        /// <summary>Records state-application operations while retaining independent row state for fail-closed assertions.</summary>
        private sealed class RecordingStateApplication
            : ShaderCoreTestStateInitializer.IStateApplication
        {
            private readonly List<ShaderCoreTestStateInitializer.ShaderSettingRow> rows;

            /// <summary>Initializes the recording state application with independent source rows.</summary>
            /// <param name="rows">The source rows whose mutation must be observed.</param>
            public RecordingStateApplication(
                IEnumerable<ShaderCoreTestStateInitializer.ShaderSettingRow> rows
            )
            {
                this.rows = new List<ShaderCoreTestStateInitializer.ShaderSettingRow>(
                    rows ?? throw new ArgumentNullException(nameof(rows))
                );
            }

            /// <summary>Gets the rows held by this state application.</summary>
            public IReadOnlyList<ShaderCoreTestStateInitializer.ShaderSettingRow> Rows => rows;

            /// <summary>Gets the number of row reads.</summary>
            public int ReadRowsCallCount { get; private set; }

            /// <summary>Gets the number of row writes.</summary>
            public int WriteRowsCallCount { get; private set; }

            /// <summary>Gets the number of serialized applies.</summary>
            public int ApplyCallCount { get; private set; }

            /// <summary>Gets the number of Save invocations.</summary>
            public int SaveCallCount { get; private set; }

            /// <summary>Gets the number of host reimports.</summary>
            public int ReimportCallCount { get; private set; }

            /// <inheritdoc />
            public IReadOnlyList<ShaderCoreTestStateInitializer.ShaderSettingRow> ReadRows()
            {
                ReadRowsCallCount++;
                return rows;
            }

            /// <inheritdoc />
            public void WriteRows(
                IReadOnlyList<ShaderCoreTestStateInitializer.ShaderSettingRow> newRows
            )
            {
                WriteRowsCallCount++;
                rows.Clear();
                rows.AddRange(newRows);
            }

            /// <inheritdoc />
            public bool Apply()
            {
                ApplyCallCount++;
                return true;
            }

            /// <inheritdoc />
            public void Save()
            {
                SaveCallCount++;
            }

            /// <inheritdoc />
            public IReadOnlyList<string> ReimportConfiguredHostAssets(
                IEnumerable<string> shaderNames
            )
            {
                ReimportCallCount++;
                return Array.Empty<string>();
            }
        }

        /// <summary>Represents the source-validation subset of the persistent host manifest.</summary>
        [Serializable]
        private sealed class GeneratedSourceManifest
        {
            /// <summary>Stores the fixed source-validation hosts.</summary>
            public GeneratedSourceHost[] hosts;
        }

        /// <summary>Represents generated-source expectations for one host.</summary>
        [Serializable]
        private sealed class GeneratedSourceHost
        {
            /// <summary>Stores the imported shader name.</summary>
            public string shaderName;

            /// <summary>Stores selected sentinels.</summary>
            public string[] expectedSentinels;

            /// <summary>Stores unselected sentinels.</summary>
            public string[] inactiveSentinels;

            /// <summary>Stores expected per-pass selected-sentinel counts.</summary>
            public PassSentinelCounts expectedPassSentinelCounts;
        }

        /// <summary>Represents expected selected-sentinel counts for generated passes.</summary>
        [Serializable]
        private sealed class PassSentinelCounts
        {
            /// <summary>Stores the ForwardBase count.</summary>
            public int ForwardBase;

            /// <summary>Stores the ForwardAdd count.</summary>
            public int ForwardAdd;

            /// <summary>Stores the ShadowCaster count.</summary>
            public int ShadowCaster;

            /// <summary>Stores the Meta count.</summary>
            public int Meta;
        }
    }
}
