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

// Validates imported fixed Shader-Core hosts and product source contracts without changing module-selection state.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Validates imported fixed Shader-Core hosts and product source contracts without changing editor state.</summary>
    public sealed class ShaderCoreTestHostManifestTests
    {
        /// <summary>Identifies the generated Shader-Core source subasset.</summary>
        private const string GeneratedSourceName = "Shader Source";

        /// <summary>Identifies the runtime gate exposed by every phase host.</summary>
        private const string RuntimeGatePropertyName = "_PhaseHostRuntimeGate";

        /// <summary>Identifies the test-only source marker prefix excluded from product shaders.</summary>
        private const string TestSentinelPrefix = "PUREBASE_TEST_";

        /// <summary>Defines the calibrated HDR target dimension used for gate delta observation.</summary>
        private const int RenderSize = 160;

        /// <summary>Defines the private layer used by transient runtime objects.</summary>
        private const int RuntimeLayer = 30;

        /// <summary>Defines the minimum alpha needed to classify a rendered host pixel.</summary>
        private const float VisibleAlphaThreshold = 0.0025f;

        /// <summary>Defines the background color used by each isolated runtime observation.</summary>
        private static readonly Color RuntimeBackgroundColor = new Color(
            0.009f,
            0.013f,
            0.021f,
            0.0f
        );

        /// <summary>Lists package product shaders that must stay free of test-host sentinels.</summary>
        private static readonly string[] ProductShaderNames =
        {
            "PureBase/Unlit",
            "PureBase/Toon",
            "PureBase/Hybrid",
            "PureBase/PBR",
        };

        /// <summary>Ensures every fixed host has one non-empty shader name and module selection.</summary>
        [Test]
        public void ManifestContainsElevenUniqueFixedHostSelections()
        {
            var manifest = JsonUtility.FromJson<HostManifest>(File.ReadAllText(GetManifestPath()));

            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.schemaVersion, Is.EqualTo(1));
            Assert.That(manifest.hosts, Is.Not.Null);
            Assert.That(manifest.hosts.Length, Is.EqualTo(11));

            var shaderNames = new System.Collections.Generic.HashSet<string>(
                StringComparer.Ordinal
            );
            foreach (HostManifestEntry host in manifest.hosts)
            {
                Assert.That(host.shaderName, Is.Not.Empty);
                Assert.That(
                    shaderNames.Add(host.shaderName),
                    Is.True,
                    $"Duplicate fixed host shader '{host.shaderName}'."
                );

                var moduleCount = string.IsNullOrEmpty(host.moduleUniqueId)
                    ? host.moduleUniqueIds?.Length ?? 0
                    : 1;
                Assert.That(
                    moduleCount,
                    Is.GreaterThan(0),
                    $"Host '{host.shaderName}' has no fixed module selection."
                );
                Assert.That(
                    host.expectedSentinels,
                    Is.Not.Null.And.Not.Empty,
                    $"Host '{host.shaderName}' has no expected sentinels."
                );
                Assert.That(
                    host.expectedPassSentinelCounts,
                    Is.Not.Null,
                    $"Host '{host.shaderName}' has no expected pass counts."
                );
            }

            Assert.That(
                manifest.hosts.Count(HasConfiguredRuntimeDelta),
                Is.EqualTo(10),
                "Each phase host must declare one valid runtime delta and the module-order host must not."
            );
            Assert.That(
                manifest.hosts.Count(HasConfiguredModuleOrder),
                Is.EqualTo(1),
                "Only the module-order host must declare one valid module-order contract."
            );
        }

        /// <summary>Checks every imported host's compiler status and generated source sentinel contract without importing or modifying assets.</summary>
        [Test]
        public void ImportedHostsMatchGeneratedSourceContracts()
        {
            HostManifest manifest = LoadManifest();
            foreach (HostManifestEntry host in manifest.hosts)
            {
                string assetPath = FindHostAssetPath(host.shaderName);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                AssertImportedShaderIsUsable(host.shaderName, shader);

                string source = LoadGeneratedShaderSource(assetPath, host.shaderName);
                AssertExpectedSentinelCounts(host, source);
                AssertInactiveSentinelsAreAbsent(host, source);

                if (HasConfiguredModuleOrder(host))
                {
                    AssertModuleOrder(host, source);
                }
            }
        }

        /// <summary>Measures each phase host with its gate disabled and enabled using only transient render resources.</summary>
        [Test]
        public void PhaseHostsExposeConfiguredRuntimeDeltas()
        {
            Assert.That(
                GraphicsSettings.currentRenderPipeline,
                Is.Null,
                "Fixed host runtime deltas require the Built-in Render Pipeline."
            );

            HostManifest manifest = LoadManifest();
            foreach (HostManifestEntry host in manifest.hosts.Where(HasConfiguredRuntimeDelta))
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                    FindHostAssetPath(host.shaderName)
                );
                AssertImportedShaderIsUsable(host.shaderName, shader);
                using (
                    RuntimeRenderResources resources = new RuntimeRenderResources(
                        shader,
                        host.shaderName
                    )
                )
                {
                    RuntimeObservation disabled = resources.Render(
                        0.0f,
                        out Color[] disabledPixels
                    );
                    RuntimeObservation enabled = resources.Render(1.0f, out Color[] enabledPixels);
                    AssertRuntimeDelta(
                        host,
                        disabled,
                        enabled,
                        CountChangedPixels(disabledPixels, enabledPixels)
                    );
                }
            }
        }

        /// <summary>Ensures product generated sources did not absorb test host sentinels.</summary>
        [Test]
        public void ProductGeneratedSourcesContainNoTestSentinels()
        {
            foreach (string productShaderName in ProductShaderNames)
            {
                string assetPath = FindShaderCoreAssetPath(
                    productShaderName,
                    "Packages/jp.penguin.purebase/Shaders"
                );
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                AssertImportedShaderIsUsable(productShaderName, shader);
                string source = LoadGeneratedShaderSource(assetPath, productShaderName);
                Assert.That(
                    source.IndexOf(TestSentinelPrefix, StringComparison.Ordinal),
                    Is.EqualTo(-1),
                    $"Product shader '{productShaderName}' imported a test sentinel."
                );
            }
        }

        /// <summary>Ensures each product shader declares one Shader-Core material editor at the outer Shader scope.</summary>
        [Test]
        public void ProductShadersDeclareShaderCoreMaterialEditorAtShaderScope()
        {
            const string expectedDirective = "CustomEditor \"SCMaterialEditor\"";
            const string lineStartDeclarationPattern = @"^[ \t]*CustomEditor\b[^\r\n]*";
            const string finalShaderScopePattern =
                @"^[ \t]*}[ \t]*\r?\n(?:[ \t]*\r?\n)?    CustomEditor ""SCMaterialEditor""[ \t]*\r?\n[ \t]*}\s*\z";

            foreach (string productShaderName in ProductShaderNames)
            {
                string assetPath = FindShaderCoreAssetPath(
                    productShaderName,
                    "Packages/jp.penguin.purebase/Shaders"
                );
                string source = File.ReadAllText(assetPath);

                Assert.That(
                    CountOccurrences(source, expectedDirective),
                    Is.EqualTo(1),
                    $"Product shader '{productShaderName}' at '{assetPath}' must contain exactly one '{expectedDirective}' declaration."
                );

                Assert.That(
                    Regex
                        .Matches(source, lineStartDeclarationPattern, RegexOptions.Multiline)
                        .Count,
                    Is.EqualTo(1),
                    $"Product shader '{productShaderName}' at '{assetPath}' must contain exactly one line-start CustomEditor declaration."
                );

                Assert.That(
                    Regex.IsMatch(source, finalShaderScopePattern, RegexOptions.Multiline),
                    Is.True,
                    $"Product shader '{productShaderName}' at '{assetPath}' must end with the last SubShader close, '{expectedDirective}', and the outer Shader close."
                );
            }
        }

        /// <summary>Loads and validates the fixed host manifest.</summary>
        private static HostManifest LoadManifest()
        {
            HostManifest manifest = JsonUtility.FromJson<HostManifest>(
                File.ReadAllText(GetManifestPath())
            );
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.schemaVersion, Is.EqualTo(1));
            Assert.That(manifest.hosts, Is.Not.Null.And.Length.EqualTo(11));
            return manifest;
        }

        /// <summary>Returns whether a host declares a complete runtime delta rather than JsonUtility's empty optional DTO.</summary>
        private static bool HasConfiguredRuntimeDelta(HostManifestEntry host)
        {
            RuntimeDelta runtimeDelta = host.runtimeDelta;
            return runtimeDelta != null
                && IsSupportedRuntimeMetric(runtimeDelta.metric)
                && IsSupportedRuntimeDirection(runtimeDelta.direction)
                && runtimeDelta.minimumAbsoluteDelta > 0.0f;
        }

        /// <summary>Returns whether a host declares a complete module-order contract rather than JsonUtility's empty optional DTO.</summary>
        private static bool HasConfiguredModuleOrder(HostManifestEntry host)
        {
            ModuleOrder moduleOrder = host.moduleOrder;
            return moduleOrder != null
                && !string.IsNullOrEmpty(moduleOrder.firstSentinel)
                && !string.IsNullOrEmpty(moduleOrder.secondSentinel);
        }

        /// <summary>Finds a Shader-Core asset by imported shader name in one read-only asset search root.</summary>
        private static string FindShaderCoreAssetPath(string shaderName, string searchRoot)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { searchRoot }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith(".scshader", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (
                    shader != null
                    && string.Equals(shader.name, shaderName, StringComparison.Ordinal)
                )
                {
                    return assetPath;
                }
            }

            Assert.Fail(
                $"Imported Shader-Core shader '{shaderName}' was not found below '{searchRoot}'. Run the dedicated Initialize lane before Daily."
            );
            return null;
        }

        /// <summary>Finds one fixed test host asset without importing or modifying it.</summary>
        private static string FindHostAssetPath(string shaderName)
        {
            return FindShaderCoreAssetPath(
                shaderName,
                "Packages/jp.penguin.purebase/Tests/Fixtures/Hosts"
            );
        }

        /// <summary>Asserts that an already imported Shader-Core shader compiled and is supported.</summary>
        private static void AssertImportedShaderIsUsable(string shaderName, Shader shader)
        {
            Assert.That(
                shader,
                Is.Not.Null,
                $"Imported Shader-Core shader '{shaderName}' was unavailable."
            );
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                $"Imported Shader-Core shader '{shaderName}' has compiler errors."
            );
            Assert.That(
                shader.isSupported,
                Is.True,
                $"Imported Shader-Core shader '{shaderName}' is not supported."
            );
        }

        /// <summary>Loads exactly one generated Shader Source subasset without forcing an import.</summary>
        private static string LoadGeneratedShaderSource(string assetPath, string shaderName)
        {
            TextAsset[] sourceAssets = AssetDatabase
                .LoadAllAssetsAtPath(assetPath)
                .OfType<TextAsset>()
                .Where(asset =>
                    string.Equals(asset.name, GeneratedSourceName, StringComparison.Ordinal)
                )
                .ToArray();
            Assert.That(
                sourceAssets,
                Has.Length.EqualTo(1),
                $"Imported Shader-Core shader '{shaderName}' must contain exactly one generated Shader Source subasset."
            );
            Assert.That(
                sourceAssets[0].text,
                Is.Not.Empty,
                $"Imported Shader-Core shader '{shaderName}' generated an empty Shader Source subasset."
            );
            return sourceAssets[0].text;
        }

        /// <summary>Checks selected sentinel counts in every named generated ShaderLab pass.</summary>
        private static void AssertExpectedSentinelCounts(HostManifestEntry host, string source)
        {
            foreach (string sentinel in host.expectedSentinels)
            {
                AssertPassSentinelCount(
                    host,
                    source,
                    "ForwardBase",
                    sentinel,
                    host.expectedPassSentinelCounts.ForwardBase
                );
                AssertPassSentinelCount(
                    host,
                    source,
                    "ForwardAdd",
                    sentinel,
                    host.expectedPassSentinelCounts.ForwardAdd
                );
                AssertPassSentinelCount(
                    host,
                    source,
                    "ShadowCaster",
                    sentinel,
                    host.expectedPassSentinelCounts.ShadowCaster
                );
                AssertPassSentinelCount(
                    host,
                    source,
                    "Meta",
                    sentinel,
                    host.expectedPassSentinelCounts.Meta
                );
            }
        }

        /// <summary>Checks that one sentinel count in a named generated ShaderLab pass matches the manifest.</summary>
        private static void AssertPassSentinelCount(
            HostManifestEntry host,
            string source,
            string passName,
            string sentinel,
            int expectedCount
        )
        {
            string passSource = GetPassSource(source, passName, host.shaderName);
            Assert.That(
                CountOccurrences(passSource, sentinel),
                Is.EqualTo(expectedCount),
                $"Host '{host.shaderName}' pass '{passName}' emitted an unexpected count for '{sentinel}'."
            );
        }

        /// <summary>Checks that every inactive sentinel is absent from the entire generated source.</summary>
        private static void AssertInactiveSentinelsAreAbsent(HostManifestEntry host, string source)
        {
            foreach (string sentinel in host.inactiveSentinels ?? Array.Empty<string>())
            {
                Assert.That(
                    CountOccurrences(source, sentinel),
                    Is.Zero,
                    $"Host '{host.shaderName}' emitted inactive sentinel '{sentinel}'."
                );
            }
        }

        /// <summary>Checks configured module-order sentinels in every pass where the manifest expects their emission.</summary>
        private static void AssertModuleOrder(HostManifestEntry host, string source)
        {
            Assert.That(host.moduleOrder, Is.Not.Null);
            AssertModuleOrderInPass(
                host,
                source,
                "ForwardBase",
                host.expectedPassSentinelCounts.ForwardBase
            );
            AssertModuleOrderInPass(
                host,
                source,
                "ForwardAdd",
                host.expectedPassSentinelCounts.ForwardAdd
            );
            AssertModuleOrderInPass(
                host,
                source,
                "ShadowCaster",
                host.expectedPassSentinelCounts.ShadowCaster
            );
            AssertModuleOrderInPass(host, source, "Meta", host.expectedPassSentinelCounts.Meta);
        }

        /// <summary>Checks module-order sentinels are present in source order in one emitted generated pass.</summary>
        private static void AssertModuleOrderInPass(
            HostManifestEntry host,
            string source,
            string passName,
            int expectedCount
        )
        {
            if (expectedCount == 0)
            {
                return;
            }

            string passSource = GetPassSource(source, passName, host.shaderName);
            int firstIndex = passSource.IndexOf(
                host.moduleOrder.firstSentinel,
                StringComparison.Ordinal
            );
            int secondIndex = passSource.IndexOf(
                host.moduleOrder.secondSentinel,
                StringComparison.Ordinal
            );
            Assert.That(
                firstIndex,
                Is.GreaterThanOrEqualTo(0),
                $"Host '{host.shaderName}' pass '{passName}' did not emit first module-order sentinel '{host.moduleOrder.firstSentinel}'."
            );
            Assert.That(
                secondIndex,
                Is.GreaterThan(firstIndex),
                $"Host '{host.shaderName}' pass '{passName}' did not preserve module order '{host.moduleOrder.firstSentinel}' before '{host.moduleOrder.secondSentinel}'."
            );
        }

        /// <summary>Asserts the configured gate-on versus gate-off metric direction and magnitude.</summary>
        private static void AssertRuntimeDelta(
            HostManifestEntry host,
            RuntimeObservation disabled,
            RuntimeObservation enabled,
            int changedPixelCount
        )
        {
            float delta =
                GetRuntimeMetric(enabled, host.runtimeDelta.metric)
                - GetRuntimeMetric(disabled, host.runtimeDelta.metric);
            float minimumAbsoluteDelta = host.runtimeDelta.minimumAbsoluteDelta;
            string evidence =
                $"disabled={GetRuntimeMetric(disabled, host.runtimeDelta.metric)}, enabled={GetRuntimeMetric(enabled, host.runtimeDelta.metric)}, disabledCoverage={disabled.MeshCoverage}, enabledCoverage={enabled.MeshCoverage}, changedPixelCount={changedPixelCount}";
            Assert.That(
                minimumAbsoluteDelta,
                Is.GreaterThan(0.0f),
                $"Host '{host.shaderName}' must declare a positive runtime delta magnitude."
            );
            AssertCoverageDoesNotFillRenderTarget(
                host,
                "disabled",
                disabled.MeshCoverage,
                evidence
            );
            AssertCoverageDoesNotFillRenderTarget(host, "enabled", enabled.MeshCoverage, evidence);

            if (string.Equals(host.runtimeDelta.direction, "increase", StringComparison.Ordinal))
            {
                Assert.That(
                    delta,
                    Is.GreaterThanOrEqualTo(minimumAbsoluteDelta),
                    $"Host '{host.shaderName}' gate did not increase '{host.runtimeDelta.metric}' by {minimumAbsoluteDelta}. {evidence}."
                );
                return;
            }

            if (string.Equals(host.runtimeDelta.direction, "decrease", StringComparison.Ordinal))
            {
                Assert.That(
                    delta,
                    Is.LessThanOrEqualTo(-minimumAbsoluteDelta),
                    $"Host '{host.shaderName}' gate did not decrease '{host.runtimeDelta.metric}' by {minimumAbsoluteDelta}. {evidence}."
                );
                return;
            }

            Assert.Fail(
                $"Host '{host.shaderName}' has unsupported runtime delta direction '{host.runtimeDelta.direction}'."
            );
        }

        /// <summary>Rejects a full-frame alpha occupancy result because the isolated sphere cannot cover the complete target.</summary>
        private static void AssertCoverageDoesNotFillRenderTarget(
            HostManifestEntry host,
            string gateState,
            int coverage,
            string evidence
        )
        {
            Assert.That(
                coverage,
                Is.LessThan(RenderSize * RenderSize),
                $"Host '{host.shaderName}' {gateState} observation covered the complete render target. {evidence}."
            );
        }

        /// <summary>Counts readback pixels whose RGBA values differ between two gate states.</summary>
        private static int CountChangedPixels(Color[] disabledPixels, Color[] enabledPixels)
        {
            Assert.That(enabledPixels, Has.Length.EqualTo(disabledPixels.Length));

            var changedPixelCount = 0;
            for (var index = 0; index < disabledPixels.Length; index++)
            {
                Color disabled = disabledPixels[index];
                Color enabled = enabledPixels[index];
                if (
                    disabled.r != enabled.r
                    || disabled.g != enabled.g
                    || disabled.b != enabled.b
                    || disabled.a != enabled.a
                )
                {
                    changedPixelCount++;
                }
            }

            return changedPixelCount;
        }

        /// <summary>Returns one named measurement from a transient runtime observation.</summary>
        private static float GetRuntimeMetric(RuntimeObservation observation, string metric)
        {
            switch (metric)
            {
                case "meshCentroidX":
                    return observation.MeshCentroidX;
                case "meshCentroidY":
                    return observation.MeshCentroidY;
                case "meshCoverage":
                    return observation.MeshCoverage;
                case "meanRed":
                    return observation.MeanColor.r;
                case "meanGreen":
                    return observation.MeanColor.g;
                case "meanBlue":
                    return observation.MeanColor.b;
                default:
                    Assert.Fail($"Unsupported fixed host runtime metric '{metric}'.");
                    return 0.0f;
            }
        }

        /// <summary>Returns whether a runtime delta metric is supported by the read-only render harness.</summary>
        private static bool IsSupportedRuntimeMetric(string metric)
        {
            switch (metric)
            {
                case "meshCentroidX":
                case "meshCentroidY":
                case "meshCoverage":
                case "meanRed":
                case "meanGreen":
                case "meanBlue":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Returns whether a runtime delta direction is supported by the render contract.</summary>
        private static bool IsSupportedRuntimeDirection(string direction)
        {
            return string.Equals(direction, "increase", StringComparison.Ordinal)
                || string.Equals(direction, "decrease", StringComparison.Ordinal);
        }

        /// <summary>Gets one named generated ShaderLab pass from a generated Shader Source.</summary>
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

        /// <summary>Counts non-overlapping ordinal occurrences in a source string.</summary>
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

        /// <summary>Returns the package manifest path from Unity's project root.</summary>
        private static string GetManifestPath()
        {
            return Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Packages",
                "jp.penguin.purebase",
                "Tests",
                "Config",
                "shader-core-test-hosts.json"
            );
        }

        /// <summary>Represents the read-only top-level host manifest.</summary>
        [Serializable]
        private sealed class HostManifest
        {
            /// <summary>Gets the manifest format version.</summary>
            public int schemaVersion;

            /// <summary>Gets the fixed host definitions.</summary>
            public HostManifestEntry[] hosts;
        }

        /// <summary>Represents one fixed host's selected modules.</summary>
        [Serializable]
        private sealed class HostManifestEntry
        {
            /// <summary>Gets the Shader-Core shader name.</summary>
            public string shaderName;

            /// <summary>Gets a single selected module ID.</summary>
            public string moduleUniqueId;

            /// <summary>Gets an ordered multi-module selection.</summary>
            public string[] moduleUniqueIds;

            /// <summary>Gets sentinels expected to be emitted by the imported generated source.</summary>
            public string[] expectedSentinels;

            /// <summary>Gets sentinels that must not be emitted by the imported generated source.</summary>
            public string[] inactiveSentinels;

            /// <summary>Gets expected selected sentinel counts for every generated pass.</summary>
            public PassSentinelCounts expectedPassSentinelCounts;

            /// <summary>Gets the configured phase-specific runtime gate observation.</summary>
            public RuntimeDelta runtimeDelta;

            /// <summary>Gets configured generated-source order expectations for the two-module host.</summary>
            public ModuleOrder moduleOrder;
        }

        /// <summary>Stores selected sentinel counts for every generated ShaderLab pass.</summary>
        [Serializable]
        private sealed class PassSentinelCounts
        {
            /// <summary>Gets the expected ForwardBase sentinel count.</summary>
            public int ForwardBase;

            /// <summary>Gets the expected ForwardAdd sentinel count.</summary>
            public int ForwardAdd;

            /// <summary>Gets the expected ShadowCaster sentinel count.</summary>
            public int ShadowCaster;

            /// <summary>Gets the expected Meta sentinel count.</summary>
            public int Meta;
        }

        /// <summary>Stores a phase host's gate-on versus gate-off runtime measurement contract.</summary>
        [Serializable]
        private sealed class RuntimeDelta
        {
            /// <summary>Gets the measured runtime observation field.</summary>
            public string metric;

            /// <summary>Gets the expected signed direction of the measurement delta.</summary>
            public string direction;

            /// <summary>Gets the minimum absolute magnitude required for the observation delta.</summary>
            public float minimumAbsoluteDelta;
        }

        /// <summary>Stores generated source ordering expectations for the selected same-phase modules.</summary>
        [Serializable]
        private sealed class ModuleOrder
        {
            /// <summary>Gets the first expected generated source sentinel.</summary>
            public string firstSentinel;

            /// <summary>Gets the second expected generated source sentinel.</summary>
            public string secondSentinel;
        }

        /// <summary>Stores one gate-state measurement from the transient host renderer.</summary>
        private readonly struct RuntimeObservation
        {
            /// <summary>Initializes one host render observation.</summary>
            /// <param name="meshCoverage">The number of visible host pixels.</param>
            /// <param name="meshCentroidX">The normalized horizontal host centroid.</param>
            /// <param name="meshCentroidY">The normalized vertical host centroid.</param>
            /// <param name="meanColor">The average HDR host color.</param>
            public RuntimeObservation(
                int meshCoverage,
                float meshCentroidX,
                float meshCentroidY,
                Color meanColor
            )
            {
                MeshCoverage = meshCoverage;
                MeshCentroidX = meshCentroidX;
                MeshCentroidY = meshCentroidY;
                MeanColor = meanColor;
            }

            /// <summary>Gets the number of visible host pixels.</summary>
            public int MeshCoverage { get; }

            /// <summary>Gets the normalized horizontal host centroid.</summary>
            public float MeshCentroidX { get; }

            /// <summary>Gets the normalized vertical host centroid.</summary>
            public float MeshCentroidY { get; }

            /// <summary>Gets the average HDR color of visible host pixels.</summary>
            public Color MeanColor { get; }
        }

        /// <summary>Owns temporary render objects used to observe one imported host's runtime gate.</summary>
        private sealed class RuntimeRenderResources : IDisposable
        {
            private readonly Material material;
            private readonly GameObject cameraObject;
            private readonly GameObject hostObject;
            private readonly GameObject lightObject;
            private readonly Camera camera;
            private readonly RenderTexture renderTarget;
            private readonly Texture2D readbackTexture;

            /// <summary>Initializes isolated non-persistent BIRP render objects for one imported host.</summary>
            /// <param name="shader">The already imported host shader.</param>
            /// <param name="shaderName">The name used in assertion messages.</param>
            public RuntimeRenderResources(Shader shader, string shaderName)
            {
                material = new Material(shader);
                Assert.That(
                    material.HasProperty(RuntimeGatePropertyName),
                    Is.True,
                    $"Host '{shaderName}' does not expose {RuntimeGatePropertyName}."
                );

                cameraObject = new GameObject("PureBase Daily Host Camera")
                {
                    layer = RuntimeLayer,
                };
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.allowHDR = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = RuntimeBackgroundColor;
                camera.cullingMask = 1 << RuntimeLayer;
                camera.fieldOfView = 38.0f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 32.0f;
                cameraObject.transform.position = new Vector3(0.0f, 0.0f, -5.5f);
                cameraObject.transform.LookAt(Vector3.zero);

                hostObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                hostObject.name = "PureBase Daily Host";
                hostObject.layer = RuntimeLayer;
                hostObject.transform.localScale = Vector3.one * 1.65f;
                hostObject.GetComponent<Renderer>().sharedMaterial = material;

                lightObject = new GameObject("PureBase Daily Host Light") { layer = RuntimeLayer };
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.shadows = LightShadows.None;
                light.cullingMask = 1 << RuntimeLayer;
                lightObject.transform.rotation = Quaternion.Euler(50.0f, -35.0f, 0.0f);

                renderTarget = new RenderTexture(
                    RenderSize,
                    RenderSize,
                    24,
                    RenderTextureFormat.ARGBHalf
                )
                {
                    useMipMap = false,
                    autoGenerateMips = false,
                };
                renderTarget.Create();
                camera.targetTexture = renderTarget;
                readbackTexture = new Texture2D(
                    RenderSize,
                    RenderSize,
                    TextureFormat.RGBAHalf,
                    mipChain: false,
                    linear: true
                );
            }

            /// <summary>Renders one runtime gate state into an in-memory observation and readback.</summary>
            /// <param name="gateValue">The non-persistent gate value assigned to the temporary material.</param>
            /// <param name="pixels">Receives the in-memory HDR readback for direct gate-state comparison.</param>
            /// <returns>The observed host coverage, centroid, and average HDR color.</returns>
            public RuntimeObservation Render(float gateValue, out Color[] pixels)
            {
                material.SetFloat(RuntimeGatePropertyName, gateValue);
                camera.Render();
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = renderTarget;
                    readbackTexture.ReadPixels(
                        new Rect(0.0f, 0.0f, RenderSize, RenderSize),
                        0,
                        0,
                        recalculateMipMaps: false
                    );
                    readbackTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }

                pixels = readbackTexture.GetPixels();
                return MeasurePixels(pixels);
            }

            /// <summary>Releases every non-persistent object allocated for the runtime observation.</summary>
            public void Dispose()
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(readbackTexture);
                UnityEngine.Object.DestroyImmediate(renderTarget);
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(hostObject);
                UnityEngine.Object.DestroyImmediate(lightObject);
            }

            /// <summary>Measures visible host pixels from their alpha occupancy.</summary>
            /// <param name="pixels">The HDR render target pixels.</param>
            /// <returns>The compact gate-state observation.</returns>
            private static RuntimeObservation MeasurePixels(Color[] pixels)
            {
                var coverage = 0;
                var sumX = 0.0f;
                var sumY = 0.0f;
                var sumColor = Color.black;
                for (var index = 0; index < pixels.Length; index++)
                {
                    Color pixel = pixels[index];
                    if (pixel.a <= VisibleAlphaThreshold)
                    {
                        continue;
                    }

                    coverage++;
                    sumX += index % RenderSize;
                    sumY += index / RenderSize;
                    sumColor += pixel;
                }

                return coverage == 0
                    ? new RuntimeObservation(0, 0.0f, 0.0f, Color.black)
                    : new RuntimeObservation(
                        coverage,
                        sumX / coverage / RenderSize,
                        sumY / coverage / RenderSize,
                        sumColor / coverage
                    );
            }
        }
    }
}
