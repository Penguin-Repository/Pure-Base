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

// Defines material snapshot and delayed-invalidating collection support for atomicity contracts.

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
        /// <summary>Captures every material field whose mutation must be rejected by invalid normalizer inputs.</summary>
        private sealed class MaterialState
        {
            /// <summary>Captures an immutable snapshot from one material.</summary>
            /// <param name="material">The material to snapshot.</param>
            /// <param name="observedPropertyTypes">The optional set that records captured shader property types.</param>
            /// <returns>The captured state.</returns>
            public static MaterialState Capture(
                Material material,
                ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes = null
            )
            {
                MaterialState state = CreateBaseState(material);
                CaptureShaderPropertyState(state, material, observedPropertyTypes);
                CaptureHiddenStateAndPasses(state, material);
                return state;
            }

            /// <summary>Captures material-wide rendering state before visible shader properties are enumerated.</summary>
            private static MaterialState CreateBaseState(Material material)
            {
                return new MaterialState
                {
                    hasRenderTypeOverride = TryGetSerializedRenderTypeOverride(
                        material,
                        out string renderTypeOverride
                    ),
                    renderTypeOverride = renderTypeOverride,
                    resolvedRenderType = material.GetTag("RenderType", true),
                    rawQueue = GetRawRenderQueue(material),
                    resolvedQueue = material.renderQueue,
                    shadowCasterEnabled = material.GetShaderPassEnabled("ShadowCaster"),
                    metaEnabled = material.GetShaderPassEnabled("Meta"),
                    dirty = EditorUtility.IsDirty(material),
                    keywords = material.shaderKeywords,
                };
            }

            /// <summary>Captures every visible shader property in declaration order.</summary>
            private static void CaptureShaderPropertyState(
                MaterialState state,
                Material material,
                ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes
            )
            {
                Shader shader = material.shader;
                for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
                {
                    string propertyName = shader.GetPropertyName(index);
                    ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(
                        shader,
                        index
                    );
                    ObserveAtomicityPropertyType(observedPropertyTypes, propertyType);
                    switch (propertyType)
                    {
                        case ShaderUtil.ShaderPropertyType.Float:
                        case ShaderUtil.ShaderPropertyType.Range:
                            state.floats[propertyName] = material.GetFloat(propertyName);
                            break;
                        case ShaderUtil.ShaderPropertyType.Int:
                            state.integers[propertyName] = material.GetInteger(propertyName);
                            break;
                        case ShaderUtil.ShaderPropertyType.Color:
                            state.colors[propertyName] = material.GetColor(propertyName);
                            break;
                        case ShaderUtil.ShaderPropertyType.Vector:
                            state.vectors[propertyName] = material.GetVector(propertyName);
                            break;
                        case ShaderUtil.ShaderPropertyType.TexEnv:
                            state.textures[propertyName] = TexturePropertyState.Capture(
                                material,
                                propertyName
                            );
                            break;
                        default:
                            Assert.Fail(
                                $"Unsupported shader property type '{ShaderUtil.GetPropertyType(shader, index)}' for '{propertyName}'."
                            );
                            break;
                    }
                }
            }

            /// <summary>Captures the hidden normalizer state and pass enabled values.</summary>
            private static void CaptureHiddenStateAndPasses(MaterialState state, Material material)
            {
                foreach (string propertyName in HiddenStatePropertyNames)
                {
                    if (material.HasProperty(propertyName))
                        state.floats[propertyName] = material.GetFloat(propertyName);
                }

                foreach (string passName in PassNames)
                    state.passes[passName] = material.GetShaderPassEnabled(passName);
            }

            /// <summary>Asserts that a material still matches this immutable snapshot.</summary>
            /// <param name="material">The material to compare.</param>
            /// <param name="context">The diagnostic operation context.</param>
            /// <param name="observedPropertyTypes">The optional set that records asserted shader property types.</param>
            public void AssertEqual(
                Material material,
                string context,
                ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes = null
            )
            {
                ObserveAtomicityPropertyTypes(material, observedPropertyTypes);
                bool actualHasRenderTypeOverride = TryGetSerializedRenderTypeOverride(
                    material,
                    out string actualRenderTypeOverride
                );
                Assert.That(
                    actualHasRenderTypeOverride,
                    Is.EqualTo(hasRenderTypeOverride),
                    context + " RenderType override presence."
                );
                if (actualHasRenderTypeOverride)
                    Assert.That(
                        actualRenderTypeOverride,
                        Is.EqualTo(renderTypeOverride),
                        context + " RenderType override."
                    );
                Assert.That(
                    material.GetTag("RenderType", true),
                    Is.EqualTo(resolvedRenderType),
                    context + " resolved RenderType tag."
                );
                Assert.That(
                    GetRawRenderQueue(material),
                    Is.EqualTo(rawQueue),
                    context + " raw render queue."
                );
                Assert.That(
                    material.renderQueue,
                    Is.EqualTo(resolvedQueue),
                    context + " resolved render queue."
                );
                Assert.That(
                    material.GetShaderPassEnabled("ShadowCaster"),
                    Is.EqualTo(shadowCasterEnabled),
                    context + " ShadowCaster state."
                );
                Assert.That(
                    material.GetShaderPassEnabled("Meta"),
                    Is.EqualTo(metaEnabled),
                    context + " Meta state."
                );
                Assert.That(
                    EditorUtility.IsDirty(material),
                    Is.EqualTo(dirty),
                    context + " dirty state."
                );
                CollectionAssert.AreEquivalent(
                    keywords,
                    material.shaderKeywords,
                    context + " keyword set."
                );
                foreach (KeyValuePair<string, float> pair in floats)
                    Assert.That(
                        material.GetFloat(pair.Key),
                        Is.EqualTo(pair.Value),
                        context + " property " + pair.Key + "."
                    );
                foreach (KeyValuePair<string, int> pair in integers)
                {
                    int actual = material.GetInteger(pair.Key);
                    Assert.That(
                        actual,
                        Is.EqualTo(pair.Value),
                        context + " int property " + pair.Key + "."
                    );
                }
                foreach (KeyValuePair<string, Color> pair in colors)
                    Assert.That(
                        material.GetColor(pair.Key),
                        Is.EqualTo(pair.Value),
                        context + " color property " + pair.Key + "."
                    );
                foreach (KeyValuePair<string, Vector4> pair in vectors)
                    Assert.That(
                        material.GetVector(pair.Key),
                        Is.EqualTo(pair.Value),
                        context + " vector property " + pair.Key + "."
                    );
                foreach (KeyValuePair<string, TexturePropertyState> pair in textures)
                    pair.Value.AssertEqual(material, pair.Key, context);
                foreach (KeyValuePair<string, bool> pair in passes)
                    Assert.That(
                        material.GetShaderPassEnabled(pair.Key),
                        Is.EqualTo(pair.Value),
                        context + " pass " + pair.Key + "."
                    );
            }

            /// <summary>Asserts that this snapshot includes one visible or hidden shader property.</summary>
            /// <param name="propertyName">The shader property that must be captured.</param>
            public void AssertCapturesShaderProperty(string propertyName)
            {
                Assert.That(
                    floats.ContainsKey(propertyName)
                        || integers.ContainsKey(propertyName)
                        || colors.ContainsKey(propertyName)
                        || vectors.ContainsKey(propertyName)
                        || textures.ContainsKey(propertyName),
                    Is.True,
                    "The material snapshot must include shader property '" + propertyName + "'."
                );
            }

            /// <summary>Stores whether the snapshot captured an explicit RenderType override.</summary>
            public bool hasRenderTypeOverride;

            /// <summary>Stores the captured serialized RenderType override.</summary>
            public string renderTypeOverride;

            /// <summary>Stores the captured shader-resolved RenderType tag.</summary>
            public string resolvedRenderType;

            /// <summary>Stores the captured raw queue.</summary>
            public int rawQueue;

            /// <summary>Stores the captured shader-resolved render queue.</summary>
            public int resolvedQueue;

            /// <summary>Stores the captured ShadowCaster flag.</summary>
            public bool shadowCasterEnabled;

            /// <summary>Stores the captured Meta flag.</summary>
            public bool metaEnabled;

            /// <summary>Stores the captured dirty flag.</summary>
            public bool dirty;

            /// <summary>Stores the captured keyword set.</summary>
            public string[] keywords;

            /// <summary>Stores captured float and range property values.</summary>
            public readonly Dictionary<string, float> floats = new Dictionary<string, float>(
                StringComparer.Ordinal
            );

            /// <summary>Stores captured integer property values.</summary>
            public readonly Dictionary<string, int> integers = new Dictionary<string, int>(
                StringComparer.Ordinal
            );

            /// <summary>Stores captured color property values.</summary>
            public readonly Dictionary<string, Color> colors = new Dictionary<string, Color>(
                StringComparer.Ordinal
            );

            /// <summary>Stores captured vector property values.</summary>
            public readonly Dictionary<string, Vector4> vectors = new Dictionary<string, Vector4>(
                StringComparer.Ordinal
            );

            /// <summary>Stores captured texture property values and their UV transforms.</summary>
            public readonly Dictionary<string, TexturePropertyState> textures = new Dictionary<
                string,
                TexturePropertyState
            >(StringComparer.Ordinal);

            /// <summary>Stores captured enabled-state values for every rendering-mode-relevant pass.</summary>
            public readonly Dictionary<string, bool> passes = new Dictionary<string, bool>(
                StringComparer.Ordinal
            );
        }

        /// <summary>Returns valid materials during validation and snapshots, then makes one later target invalid during application.</summary>
        private sealed class LateInvalidatingMaterialList : IReadOnlyList<Material>
        {
            /// <summary>Initializes a deterministic material list that invalidates one target on its third indexed read.</summary>
            /// <param name="materials">The ordered batch materials.</param>
            /// <param name="invalidMaterialIndex">The later material index to invalidate.</param>
            /// <param name="invalidRenderingMode">The unsupported mode assigned immediately before its application.</param>
            public LateInvalidatingMaterialList(
                Material[] materials,
                int invalidMaterialIndex,
                int invalidRenderingMode
            )
            {
                this.materials = materials;
                this.invalidMaterialIndex = invalidMaterialIndex;
                this.invalidRenderingMode = invalidRenderingMode;
            }

            /// <summary>Gets the number of materials in the batch.</summary>
            public int Count => materials.Length;

            /// <summary>Returns the batch materials in their deterministic order.</summary>
            /// <returns>An enumerator for the batch materials.</returns>
            public IEnumerator<Material> GetEnumerator()
            {
                return ((IEnumerable<Material>)materials).GetEnumerator();
            }

            /// <summary>Returns the batch materials through the non-generic enumeration contract.</summary>
            /// <returns>An enumerator for the batch materials.</returns>
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return materials.GetEnumerator();
            }

            /// <summary>Gets a material and invalidates the designated later target immediately before application.</summary>
            /// <param name="index">The requested batch index.</param>
            /// <returns>The requested material.</returns>
            public Material this[int index]
            {
                get
                {
                    if (index == invalidMaterialIndex && ++invalidMaterialReadCount == 3)
                    {
                        ObservedPriorMutations =
                            materials[0].GetTag("RenderType", false) == "Opaque"
                            && materials[1].GetTag("RenderType", false) == "Transparent";
                        materials[index].SetInteger("_RenderingMode", invalidRenderingMode);
                    }

                    return materials[index];
                }
            }

            /// <summary>Gets whether the list observed normalized prior targets before it invalidated the later target.</summary>
            public bool ObservedPriorMutations { get; private set; }

            /// <summary>Stores the ordered batch materials.</summary>
            private readonly Material[] materials;

            /// <summary>Stores the later material index invalidated during application.</summary>
            private readonly int invalidMaterialIndex;

            /// <summary>Stores the unsupported rendering-mode value used to force application failure.</summary>
            private readonly int invalidRenderingMode;

            /// <summary>Counts accesses to the material that becomes invalid.</summary>
            private int invalidMaterialReadCount;
        }

        /// <summary>Stores one texture property and its material-local UV transform for atomicity assertions.</summary>
        private sealed class TexturePropertyState
        {
            /// <summary>Captures one texture property's complete material-local state.</summary>
            /// <param name="material">The source material.</param>
            /// <param name="propertyName">The texture property name.</param>
            /// <returns>The immutable texture-property snapshot.</returns>
            public static TexturePropertyState Capture(Material material, string propertyName)
            {
                return new TexturePropertyState
                {
                    texture = material.GetTexture(propertyName),
                    scale = material.GetTextureScale(propertyName),
                    offset = material.GetTextureOffset(propertyName),
                };
            }

            /// <summary>Asserts one material texture property still matches this snapshot.</summary>
            /// <param name="material">The material to inspect.</param>
            /// <param name="propertyName">The texture property name.</param>
            /// <param name="context">The diagnostic operation context.</param>
            public void AssertEqual(Material material, string propertyName, string context)
            {
                Assert.That(
                    material.GetTexture(propertyName),
                    Is.EqualTo(texture),
                    context + " texture property " + propertyName + "."
                );
                Assert.That(
                    material.GetTextureScale(propertyName),
                    Is.EqualTo(scale),
                    context + " texture scale " + propertyName + "."
                );
                Assert.That(
                    material.GetTextureOffset(propertyName),
                    Is.EqualTo(offset),
                    context + " texture offset " + propertyName + "."
                );
            }

            /// <summary>Stores the captured texture object.</summary>
            public Texture texture;

            /// <summary>Stores the captured texture UV scale.</summary>
            public Vector2 scale;

            /// <summary>Stores the captured texture UV offset.</summary>
            public Vector2 offset;
        }
    }
}
