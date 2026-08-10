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

// Provides shared fixture lifecycle, shader inspection, reflection, and rendering-state assertion support.

// Defines the read-only material, normalizer, legacy-compatibility, and persistence contracts for rendering modes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    public sealed partial class PureBaseRenderingModeContractTests
    {
        /// <summary>Saves and synchronously reimports one test-owned asset without persisting unrelated dirty Editor assets.</summary>
        /// <param name="asset">The exact fixture or temporary asset owned by this test.</param>
        /// <param name="assetPath">The expected project-relative path for <paramref name="asset"/>.</param>
        private static void SaveOnlyOwnedAssetAndReimport(
            UnityEngine.Object asset,
            string assetPath
        )
        {
            Assert.That(
                asset,
                Is.Not.Null,
                $"Test-owned asset '{assetPath}' must exist before persistence."
            );
            Assert.That(
                AssetDatabase.GetAssetPath(asset),
                Is.EqualTo(assetPath),
                "Persistence must target only the supplied test-owned asset path."
            );
            AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Returns one imported and compilable public product shader.</summary>
        /// <param name="shaderName">The stable public shader name.</param>
        /// <returns>The imported product shader.</returns>
        private static Shader RequireProductShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, $"Product shader '{shaderName}' was not imported.");
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                $"Product shader '{shaderName}' has compiler errors."
            );
            Assert.That(
                shader.isSupported,
                Is.True,
                $"Product shader '{shaderName}' is unsupported."
            );
            return shader;
        }

        /// <summary>Creates and registers one transient material for deterministic test cleanup.</summary>
        /// <param name="shader">The shader assigned to the new material.</param>
        /// <returns>The tracked transient material.</returns>
        private Material CreateMaterial(Shader shader)
        {
            var material = new Material(shader);
            transientMaterials.Add(material);
            return material;
        }

        /// <summary>Releases transient material resources after each test, including partial-failure paths.</summary>
        [TearDown]
        public void DestroyTransientMaterials()
        {
            foreach (Material material in transientMaterials)
            {
                if (material != null)
                    UnityEngine.Object.DestroyImmediate(material);
            }

            transientMaterials.Clear();
            foreach (Texture texture in transientTextures)
            {
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
            }

            transientTextures.Clear();
        }

        /// <summary>Returns the non-hidden property names in shader declaration order.</summary>
        /// <param name="shader">The shader whose visible property ABI is inspected.</param>
        /// <returns>The ordered visible property names.</returns>
        private static string[] GetVisiblePropertyNames(Shader shader)
        {
            var result = new List<string>();
            for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
            {
                if ((shader.GetPropertyFlags(index) & ShaderPropertyFlags.HideInInspector) == 0)
                    result.Add(shader.GetPropertyName(index));
            }

            return result.ToArray();
        }

        /// <summary>Returns the source-level pass names in declaration order.</summary>
        /// <param name="shader">The shader to inspect.</param>
        /// <returns>The ordered pass names.</returns>
        private static string[] GetPassNames(Shader shader)
        {
            var names = new List<string>();
            foreach (
                Match match in Regex.Matches(
                    LoadGeneratedSource(shader.name),
                    "\\bName\\s+\\\"([^\\\"]+)\\\""
                )
            )
                names.Add(match.Groups[1].Value);
            return names.ToArray();
        }

        /// <summary>Loads the generated source subasset for one imported product shader without requesting a reimport.</summary>
        /// <param name="shaderName">The imported public shader name.</param>
        /// <returns>The non-empty generated source text.</returns>
        private static string LoadGeneratedSource(string shaderName)
        {
            string path = null;
            foreach (
                string guid in AssetDatabase.FindAssets(
                    "t:Shader",
                    new[] { "Packages/jp.penguin.purebase/Shaders" }
                )
            )
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(candidate);
                if (
                    shader != null
                    && string.Equals(shader.name, shaderName, StringComparison.Ordinal)
                )
                {
                    path = candidate;
                    break;
                }
            }

            Assert.That(
                path,
                Is.Not.Empty,
                $"Could not locate the Shader-Core source asset for '{shaderName}'."
            );
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var source = asset as TextAsset;
                if (
                    source != null
                    && string.Equals(source.name, "Shader Source", StringComparison.Ordinal)
                )
                    return source.text;
            }

            Assert.Fail(
                $"Shader-Core source asset '{path}' for '{shaderName}' has no generated Shader Source subasset."
            );
            return null;
        }

        /// <summary>Finds one loaded type by its assembly-qualified full name.</summary>
        /// <param name="fullName">The exact full type name to find.</param>
        /// <returns>The loaded type, or <see langword="null"/> when no loaded assembly defines it.</returns>
        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }

        /// <summary>Asserts every hidden rendering state property for one expected mode.</summary>
        /// <param name="material">The inspected material.</param>
        /// <param name="mode">The expected rendering-mode state.</param>
        private static void AssertHiddenState(Material material, ModeContract mode)
        {
            Assert.That(material.HasProperty("_SrcBlend"), Is.True);
            Assert.That(material.HasProperty("_DstBlend"), Is.True);
            Assert.That(material.HasProperty("_ZWrite"), Is.True);
            Assert.That(material.HasProperty("_AddSrcBlend"), Is.True);
            Assert.That(material.HasProperty("_AddDstBlend"), Is.True);
            Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo(mode.srcBlend));
            Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo(mode.dstBlend));
            Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(mode.zWrite));
            Assert.That(material.GetFloat("_AddSrcBlend"), Is.EqualTo(mode.addSrcBlend));
            Assert.That(material.GetFloat("_AddDstBlend"), Is.EqualTo(mode.addDstBlend));
        }

        /// <summary>Asserts the exact enabled subset of the two rendering-mode local keywords.</summary>
        /// <param name="material">The inspected material.</param>
        /// <param name="expected">The expected enabled keyword names.</param>
        private static void AssertRenderingKeywords(Material material, string[] expected)
        {
            var actual = new List<string>();
            foreach (string keyword in RenderingModeKeywords)
            {
                if (material.IsKeywordEnabled(keyword))
                    actual.Add(keyword);
            }

            CollectionAssert.AreEquivalent(expected, actual);
        }

        /// <summary>Asserts every serializable state-table column for one material.</summary>
        /// <param name="material">The inspected material.</param>
        /// <param name="mode">The expected state-table row.</param>
        private static void AssertModeState(Material material, ModeContract mode)
        {
            Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(mode.value));
            AssertRenderTypeState(material, mode);
            Assert.That(GetRawRenderQueue(material), Is.EqualTo(mode.rawQueue));
            Assert.That(material.renderQueue, Is.EqualTo(mode.resolvedQueue));
            AssertHiddenState(material, mode);
            AssertRenderingKeywords(material, mode.enabledKeywords);
            Assert.That(
                material.GetShaderPassEnabled("ShadowCaster"),
                Is.EqualTo(mode.enableContributionPasses)
            );
            Assert.That(
                material.GetShaderPassEnabled("Meta"),
                Is.EqualTo(mode.enableContributionPasses)
            );
        }

        /// <summary>Asserts all noncanonical fields that the legacy fixture must preserve unchanged.</summary>
        /// <param name="state">The captured legacy material state.</param>
        private static void AssertLegacyState(MaterialState state)
        {
            Assert.That(state.rawQueue, Is.EqualTo(2467));
            Assert.That(state.hasRenderTypeOverride, Is.True);
            Assert.That(state.renderTypeOverride, Is.EqualTo("LegacyCutout"));
            CollectionAssert.AreEquivalent(new[] { "PUREBASE_LEGACY_UNRELATED" }, state.keywords);
            Assert.That(state.shadowCasterEnabled, Is.True);
            Assert.That(state.metaEnabled, Is.False);
            Assert.That(state.dirty, Is.False);
        }

        /// <summary>Reads Unity's serialized raw queue without conflating it with the shader-resolved queue.</summary>
        /// <param name="material">The material whose serialized queue is inspected.</param>
        /// <returns>The raw <c>m_CustomRenderQueue</c> value.</returns>
        private static int GetRawRenderQueue(Material material)
        {
            var serializedMaterial = new SerializedObject(material);
            SerializedProperty queue = serializedMaterial.FindProperty("m_CustomRenderQueue");
            Assert.That(
                queue,
                Is.Not.Null,
                "Material serialization has no m_CustomRenderQueue property."
            );
            return queue.intValue;
        }

        /// <summary>Asserts the serialized RenderType override separately from Unity's resolved shader tag.</summary>
        /// <param name="material">The material whose RenderType state is inspected.</param>
        /// <param name="mode">The expected rendering-mode state.</param>
        private static void AssertRenderTypeState(Material material, ModeContract mode)
        {
            bool hasOverride = TryGetSerializedRenderTypeOverride(
                material,
                out string renderTypeOverride
            );
            Assert.That(
                hasOverride,
                Is.EqualTo(mode.hasRenderTypeOverride),
                mode.name + " RenderType override presence."
            );
            if (hasOverride)
                Assert.That(
                    renderTypeOverride,
                    Is.EqualTo(mode.renderTypeOverride),
                    mode.name + " RenderType override."
                );
            Assert.That(
                material.GetTag("RenderType", false),
                Is.EqualTo(mode.resolvedRenderType),
                mode.name + " resolved RenderType tag."
            );
        }

        /// <summary>Reads the raw RenderType override from Unity's serialized material tag map.</summary>
        /// <param name="material">The material whose serialized tag map is inspected.</param>
        /// <param name="renderTypeOverride">Receives the override value when it exists.</param>
        /// <returns>Whether the material serializes an explicit RenderType override.</returns>
        private static bool TryGetSerializedRenderTypeOverride(
            Material material,
            out string renderTypeOverride
        )
        {
            string serializedMaterial = EditorJsonUtility.ToJson(material);
            Match tagMap = Regex.Match(
                serializedMaterial,
                @"""stringTagMap""\s*:\s*\{(?<entries>[^}]*)\}"
            );
            Assert.That(
                tagMap.Success,
                Is.True,
                "Material serialization has no stringTagMap object."
            );
            Match renderType = Regex.Match(
                tagMap.Groups["entries"].Value,
                @"""RenderType""\s*:\s*""(?<value>[^""]*)"""
            );
            renderTypeOverride = renderType.Success ? renderType.Groups["value"].Value : null;
            return renderType.Success;
        }

        /// <summary>Asserts the local rendering-mode feature ABI in each required generated shader pass.</summary>
        /// <param name="source">The generated shader source.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        private static void AssertRenderingModeKeywordDeclarations(string source, string shaderName)
        {
            var declaredKeywords = new HashSet<string>(StringComparer.Ordinal);
            foreach (
                Match declaration in Regex.Matches(
                    source,
                    @"^\s*#pragma\s+shader_feature_local\s+([^\r\n]+)",
                    RegexOptions.Multiline
                )
            )
            {
                foreach (
                    Match keyword in Regex.Matches(
                        declaration.Groups[1].Value,
                        @"\bPUREBASE_RENDERING_[A-Z0-9_]+\b"
                    )
                )
                    declaredKeywords.Add(keyword.Value);
            }

            CollectionAssert.AreEquivalent(
                RenderingModeKeywords,
                declaredKeywords,
                $"Product shader '{shaderName}' must declare exactly the Opaque and Transparent rendering-mode local keywords."
            );
            foreach (string passName in PassNames)
            {
                Assert.That(
                    Regex.IsMatch(
                        source,
                        "HLSLINCLUDE[\\s\\S]*?#pragma\\s+shader_feature_local\\s+(?:_\\s+)?PUREBASE_RENDERING_OPAQUE\\s+PUREBASE_RENDERING_TRANSPARENT[\\s\\S]*?ENDHLSL[\\s\\S]*?Name\\s+\\\""
                            + Regex.Escape(passName)
                            + "\\\""
                    ),
                    Is.True,
                    $"Product shader '{shaderName}' pass '{passName}' must inherit the rendering-mode local shader feature from the shared HLSLINCLUDE block."
                );
            }
        }

        /// <summary>Stores the public shader identity and visible property ABI for one product.</summary>
        private sealed class ProductContract
        {
            /// <summary>Initializes one immutable product contract.</summary>
            /// <param name="shaderName">The stable public shader name.</param>
            /// <param name="visiblePropertyNames">The ordered visible property ABI.</param>
            public ProductContract(
                string shaderName,
                string propertySourcePath,
                string[] visiblePropertyNames
            )
            {
                this.shaderName = shaderName;
                this.propertySourcePath = propertySourcePath;
                this.visiblePropertyNames = visiblePropertyNames;
            }

            /// <summary>Stores the stable public shader name.</summary>
            public readonly string shaderName;

            /// <summary>Stores the property source used to generate the product ShaderLab declaration.</summary>
            public readonly string propertySourcePath;

            /// <summary>Stores the ordered visible property ABI.</summary>
            public readonly string[] visiblePropertyNames;
        }

        /// <summary>Stores one complete, immutable rendering-mode state-table row.</summary>
        private sealed class ModeContract
        {
            /// <summary>Initializes one immutable state-table row.</summary>
            public ModeContract(
                int value,
                string name,
                BlendState blend,
                RenderTypeState renderType,
                QueueState queue,
                string[] enabledKeywords,
                bool enableContributionPasses
            )
            {
                this.value = value;
                this.name = name;
                srcBlend = blend.srcBlend;
                dstBlend = blend.dstBlend;
                zWrite = blend.zWrite;
                addSrcBlend = blend.addSrcBlend;
                addDstBlend = blend.addDstBlend;
                renderTypeOverride = renderType.renderTypeOverride;
                hasRenderTypeOverride = renderType.hasRenderTypeOverride;
                resolvedRenderType = renderType.resolvedRenderType;
                rawQueue = queue.rawQueue;
                resolvedQueue = queue.resolvedQueue;
                this.enabledKeywords = enabledKeywords;
                this.enableContributionPasses = enableContributionPasses;
            }

            /// <summary>Stores the serialized mode value.</summary>
            public readonly int value;

            /// <summary>Stores the diagnostic mode name.</summary>
            public readonly string name;

            /// <summary>Stores the ForwardBase source blend value.</summary>
            public readonly int srcBlend;

            /// <summary>Stores the ForwardBase destination blend value.</summary>
            public readonly int dstBlend;

            /// <summary>Stores the ForwardBase depth-write value.</summary>
            public readonly int zWrite;

            /// <summary>Stores the ForwardAdd source blend value.</summary>
            public readonly int addSrcBlend;

            /// <summary>Stores the ForwardAdd destination blend value.</summary>
            public readonly int addDstBlend;

            /// <summary>Stores the material RenderType override.</summary>
            public readonly string renderTypeOverride;

            /// <summary>Stores whether the material serializes an explicit RenderType override.</summary>
            public readonly bool hasRenderTypeOverride;

            /// <summary>Stores the shader-resolved RenderType tag.</summary>
            public readonly string resolvedRenderType;

            /// <summary>Stores the raw material render queue.</summary>
            public readonly int rawQueue;

            /// <summary>Stores the resolved render queue.</summary>
            public readonly int resolvedQueue;

            /// <summary>Stores the exact enabled local keywords.</summary>
            public readonly string[] enabledKeywords;

            /// <summary>Stores whether ShadowCaster and Meta are enabled.</summary>
            public readonly bool enableContributionPasses;
        }

        /// <summary>Stores the blend state columns for one rendering-mode state-table row.</summary>
        private sealed class BlendState
        {
            /// <summary>Initializes one immutable blend-state value group.</summary>
            public BlendState(
                int srcBlend,
                int dstBlend,
                int zWrite,
                int addSrcBlend,
                int addDstBlend
            )
            {
                this.srcBlend = srcBlend;
                this.dstBlend = dstBlend;
                this.zWrite = zWrite;
                this.addSrcBlend = addSrcBlend;
                this.addDstBlend = addDstBlend;
            }

            /// <summary>Stores the ForwardBase source blend value.</summary>
            public readonly int srcBlend;

            /// <summary>Stores the ForwardBase destination blend value.</summary>
            public readonly int dstBlend;

            /// <summary>Stores the ForwardBase depth-write value.</summary>
            public readonly int zWrite;

            /// <summary>Stores the ForwardAdd source blend value.</summary>
            public readonly int addSrcBlend;

            /// <summary>Stores the ForwardAdd destination blend value.</summary>
            public readonly int addDstBlend;
        }

        /// <summary>Stores the RenderType state columns for one rendering-mode state-table row.</summary>
        private sealed class RenderTypeState
        {
            /// <summary>Initializes one immutable RenderType-state value group.</summary>
            public RenderTypeState(
                string renderTypeOverride,
                bool hasRenderTypeOverride,
                string resolvedRenderType
            )
            {
                this.renderTypeOverride = renderTypeOverride;
                this.hasRenderTypeOverride = hasRenderTypeOverride;
                this.resolvedRenderType = resolvedRenderType;
            }

            /// <summary>Stores the material RenderType override.</summary>
            public readonly string renderTypeOverride;

            /// <summary>Stores whether the material serializes an explicit RenderType override.</summary>
            public readonly bool hasRenderTypeOverride;

            /// <summary>Stores the shader-resolved RenderType tag.</summary>
            public readonly string resolvedRenderType;
        }

        /// <summary>Stores the queue state columns for one rendering-mode state-table row.</summary>
        private sealed class QueueState
        {
            /// <summary>Initializes one immutable queue-state value group.</summary>
            public QueueState(int rawQueue, int resolvedQueue)
            {
                this.rawQueue = rawQueue;
                this.resolvedQueue = resolvedQueue;
            }

            /// <summary>Stores the raw material render queue.</summary>
            public readonly int rawQueue;

            /// <summary>Stores the shader-resolved render queue.</summary>
            public readonly int resolvedQueue;
        }
    }
}
