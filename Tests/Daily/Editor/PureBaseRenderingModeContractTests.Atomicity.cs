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

// Defines invalid-input and rollback atomicity contracts for rendering-mode normalization.

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
        /// <summary>Requires invalid public-API inputs to throw specified exceptions without changing serialized material state.</summary>
        [Test]
        public void InvalidNormalizerInputsAreAtomicForSingleAndMultipleTargets()
        {
            MethodInfo apply = RequireApplyMethod();
            MethodInfo applyAll = RequireApplyAllMethod();
            Assert.Throws<ArgumentNullException>(() => InvokeApply(apply, null));
            var seededPropertyTypes = new HashSet<ShaderUtil.ShaderPropertyType>();
            var capturedPropertyTypes = new HashSet<ShaderUtil.ShaderPropertyType>();
            var assertedPropertyTypes = new HashSet<ShaderUtil.ShaderPropertyType>();

            var unsupportedOwnership = CreateMaterial(RequireUnsupportedRenderingModeShader());
            AssertUnsupportedInputIsAtomic(
                apply,
                unsupportedOwnership,
                true,
                "The unsupported ownership input must expose _RenderingMode without being owned by Pure-Base.",
                "non-Pure-Base shader with _RenderingMode",
                seededPropertyTypes,
                capturedPropertyTypes,
                assertedPropertyTypes
            );

            var unsupportedMissingProperty = CreateMaterial(
                RequireUnsupportedShaderWithoutRenderingMode()
            );
            AssertUnsupportedInputIsAtomic(
                apply,
                unsupportedMissingProperty,
                false,
                "The missing-property input must not expose _RenderingMode.",
                "non-Pure-Base shader without _RenderingMode",
                seededPropertyTypes,
                capturedPropertyTypes,
                assertedPropertyTypes
            );

            var first = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            var second = CreateMaterial(RequireProductShader("PureBase/Toon"));
            AssertInvalidModesAreAtomic(
                apply,
                applyAll,
                first,
                second,
                seededPropertyTypes,
                capturedPropertyTypes,
                assertedPropertyTypes
            );

            foreach (
                Material coverageMaterial in CreateAtomicityCoverageMaterials(
                    seededPropertyTypes,
                    capturedPropertyTypes,
                    assertedPropertyTypes
                )
            )
            {
                SeedAtomicityState(coverageMaterial, seededPropertyTypes);
                MaterialState before = MaterialState.Capture(
                    coverageMaterial,
                    capturedPropertyTypes
                );
                Assert.Throws<InvalidOperationException>(() =>
                    InvokeApply(apply, coverageMaterial)
                );
                before.AssertEqual(
                    coverageMaterial,
                    "non-Pure-Base property-type coverage target",
                    assertedPropertyTypes
                );
            }

            AssertCompleteAtomicityPropertyTypeCoverage(seededPropertyTypes, "seed");
            AssertCompleteAtomicityPropertyTypeCoverage(capturedPropertyTypes, "capture");
            AssertCompleteAtomicityPropertyTypeCoverage(assertedPropertyTypes, "assertion");
        }

        /// <summary>Asserts that one unsupported input is rejected without changing its serialized material state.</summary>
        /// <param name="apply">The reflected single-material normalizer method.</param>
        /// <param name="material">The unsupported material to inspect.</param>
        /// <param name="hasRenderingMode">Whether the material is expected to expose <c>_RenderingMode</c>.</param>
        /// <param name="propertyMessage">The assertion message for the rendering-mode property check.</param>
        /// <param name="context">The material-state assertion context.</param>
        /// <param name="seededPropertyTypes">The set that records seeded shader property types.</param>
        /// <param name="capturedPropertyTypes">The set that records captured shader property types.</param>
        /// <param name="assertedPropertyTypes">The set that records asserted shader property types.</param>
        private void AssertUnsupportedInputIsAtomic(
            MethodInfo apply,
            Material material,
            bool hasRenderingMode,
            string propertyMessage,
            string context,
            ISet<ShaderUtil.ShaderPropertyType> seededPropertyTypes,
            ISet<ShaderUtil.ShaderPropertyType> capturedPropertyTypes,
            ISet<ShaderUtil.ShaderPropertyType> assertedPropertyTypes
        )
        {
            SeedAtomicityState(material, seededPropertyTypes);
            Assert.That(
                material.HasProperty("_RenderingMode"),
                Is.EqualTo(hasRenderingMode),
                propertyMessage
            );
            MaterialState before = MaterialState.Capture(material, capturedPropertyTypes);
            Assert.Throws<InvalidOperationException>(() => InvokeApply(apply, material));
            before.AssertEqual(material, context, assertedPropertyTypes);
        }

        /// <summary>Asserts that invalid single and batch rendering-mode values leave every target unchanged.</summary>
        /// <param name="apply">The reflected single-material normalizer method.</param>
        /// <param name="applyAll">The reflected batch normalizer method.</param>
        /// <param name="first">The target whose rendering mode is invalidated.</param>
        /// <param name="second">The unaffected target used to verify batch atomicity.</param>
        /// <param name="seededPropertyTypes">The set that records seeded shader property types.</param>
        /// <param name="capturedPropertyTypes">The set that records captured shader property types.</param>
        /// <param name="assertedPropertyTypes">The set that records asserted shader property types.</param>
        private void AssertInvalidModesAreAtomic(
            MethodInfo apply,
            MethodInfo applyAll,
            Material first,
            Material second,
            ISet<ShaderUtil.ShaderPropertyType> seededPropertyTypes,
            ISet<ShaderUtil.ShaderPropertyType> capturedPropertyTypes,
            ISet<ShaderUtil.ShaderPropertyType> assertedPropertyTypes
        )
        {
            SeedAtomicityState(first, seededPropertyTypes);
            SeedAtomicityState(second, seededPropertyTypes);
            EditorUtility.ClearDirty(second);
            foreach (int invalidMode in new[] { -1, 3 })
            {
                first.SetInteger("_RenderingMode", invalidMode);
                EditorUtility.ClearDirty(first);
                MaterialState firstBefore = MaterialState.Capture(first, capturedPropertyTypes);
                MaterialState secondBefore = MaterialState.Capture(second, capturedPropertyTypes);
                firstBefore.AssertCapturesShaderProperty("_PureBaseShaderLabSentinel");
                ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                    () =>
                        InvokeApply(apply, first)
                );
                AssertInvalidRenderingModeException(
                    exception,
                    first,
                    invalidMode,
                    "single-target invalid mode"
                );
                firstBefore.AssertEqual(
                    first,
                    $"invalid mode {invalidMode}",
                    assertedPropertyTypes
                );
                secondBefore.AssertEqual(
                    second,
                    $"unrelated target after invalid mode {invalidMode}",
                    assertedPropertyTypes
                );
                exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                    InvokeApplyAll(applyAll, new[] { first, second })
                );
                AssertInvalidRenderingModeException(
                    exception,
                    first,
                    invalidMode,
                    "batch invalid mode"
                );
                firstBefore.AssertEqual(
                    first,
                    $"batch invalid mode {invalidMode}",
                    assertedPropertyTypes
                );
                secondBefore.AssertEqual(
                    second,
                    $"unrelated target after batch invalid mode {invalidMode}",
                    assertedPropertyTypes
                );
            }
        }

        /// <summary>Requires a late batch failure to restore every already-mutated material exactly, including raw RenderType override presence.</summary>
        [Test]
        public void AtomicBatchRollbackRestoresRawRenderTypeOverridesAfterLateFailure()
        {
            MethodInfo applyAll = RequireApplyAllMethod();
            var first = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            var second = CreateMaterial(RequireProductShader("PureBase/Toon"));
            var failing = CreateMaterial(RequireProductShader("PureBase/PBR"));
            SeedAtomicityState(first);
            SeedAtomicityState(second);
            SeedAtomicityState(failing);
            first.SetInteger("_RenderingMode", 0);
            second.SetInteger("_RenderingMode", 2);
            failing.SetInteger("_RenderingMode", 1);
            first.SetOverrideTag("RenderType", string.Empty);
            second.SetOverrideTag("RenderType", "TransparentCutout");
            foreach (int invalidMode in new[] { -1, 3 })
            {
                failing.SetInteger("_RenderingMode", 1);
                EditorUtility.ClearDirty(first);
                EditorUtility.ClearDirty(second);
                EditorUtility.ClearDirty(failing);
                AssertDistinctFallbackRenderTypeOverrideStates(
                    first,
                    second,
                    "before late batch rollback"
                );
                MaterialState firstBefore = MaterialState.Capture(first);
                MaterialState secondBefore = MaterialState.Capture(second);
                var materials = new LateInvalidatingMaterialList(
                    new[] { first, second, failing },
                    2,
                    invalidMode
                );

                ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
                    () =>
                        InvokeApplyAll(applyAll, materials)
                );
                AssertInvalidRenderingModeException(
                    exception,
                    failing,
                    invalidMode,
                    "late batch invalid mode"
                );
                Assert.That(
                    materials.ObservedPriorMutations,
                    Is.True,
                    "The late invalidation must occur after prior materials are normalized."
                );
                firstBefore.AssertEqual(first, "first material after late batch rollback");
                secondBefore.AssertEqual(second, "second material after late batch rollback");
                AssertDistinctFallbackRenderTypeOverrideStates(
                    first,
                    second,
                    "after late batch rollback"
                );
                Assert.That(
                    AssetDatabase.GetAssetPath(first),
                    Is.Empty,
                    "The rollback fixture must remain transient."
                );
                Assert.That(
                    AssetDatabase.GetAssetPath(second),
                    Is.Empty,
                    "The rollback fixture must remain transient."
                );
                Assert.That(
                    AssetDatabase.GetAssetPath(failing),
                    Is.Empty,
                    "The failure fixture must remain transient."
                );
            }
        }

        /// <summary>Asserts that absent and fallback-valued RenderType overrides remain distinct serialized states.</summary>
        /// <param name="withoutOverride">The material whose raw RenderType override is absent.</param>
        /// <param name="withFallbackOverride">The material whose raw override equals the shader fallback.</param>
        /// <param name="context">The operation boundary described by the assertions.</param>
        private static void AssertDistinctFallbackRenderTypeOverrideStates(
            Material withoutOverride,
            Material withFallbackOverride,
            string context
        )
        {
            bool hasAbsentOverride = TryGetSerializedRenderTypeOverride(
                withoutOverride,
                out string absentOverride
            );
            bool hasFallbackOverride = TryGetSerializedRenderTypeOverride(
                withFallbackOverride,
                out string fallbackOverride
            );
            Assert.That(
                hasAbsentOverride,
                Is.False,
                $"The {context} absent override fixture must not serialize RenderType."
            );
            Assert.That(
                absentOverride,
                Is.Null,
                $"The {context} absent override fixture must not expose a RenderType value."
            );
            Assert.That(
                hasFallbackOverride,
                Is.True,
                $"The {context} fallback override fixture must serialize RenderType."
            );
            Assert.That(
                fallbackOverride,
                Is.EqualTo("TransparentCutout"),
                $"The {context} fallback override fixture must preserve its raw RenderType value."
            );
            Assert.That(
                withoutOverride.GetTag("RenderType", false),
                Is.EqualTo("TransparentCutout"),
                $"The {context} absent override fixture must resolve the PureBase SubShader RenderType fallback."
            );
            Assert.That(
                withFallbackOverride.GetTag("RenderType", false),
                Is.EqualTo("TransparentCutout"),
                $"The {context} fallback override fixture must resolve the same RenderType value."
            );
        }

        /// <summary>Assigns distinguishable values to every shader property before atomicity snapshots without modifying persistent assets.</summary>
        /// <param name="material">The transient material that must remain unchanged after rejection.</param>
        /// <param name="observedPropertyTypes">The optional set that records seeded shader property types.</param>
        private void SeedAtomicityState(
            Material material,
            ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes = null
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
                        material.SetFloat(propertyName, 0.137f + (index * 0.019f));
                        break;
                    case ShaderUtil.ShaderPropertyType.Int:
                        material.SetInteger(propertyName, 17 + index);
                        break;
                    case ShaderUtil.ShaderPropertyType.Color:
                        material.SetColor(
                            propertyName,
                            new Color(0.13f + (index * 0.01f), 0.27f, 0.41f, 0.59f)
                        );
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        material.SetVector(
                            propertyName,
                            new Vector4(0.11f, 0.23f, 0.37f, 0.53f + (index * 0.01f))
                        );
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        material.SetTexture(propertyName, CreateTextureSentinel(shader, index));
                        material.SetTextureScale(propertyName, new Vector2(0.71f, 0.83f));
                        material.SetTextureOffset(propertyName, new Vector2(0.17f, 0.29f));
                        break;
                    default:
                        Assert.Fail(
                            $"Unsupported shader property type '{ShaderUtil.GetPropertyType(shader, index)}' for '{propertyName}'."
                        );
                        break;
                }
            }
        }

        /// <summary>Creates and tracks a transient texture matching one shader property's declared texture dimension.</summary>
        /// <param name="shader">The shader declaring the texture property.</param>
        /// <param name="propertyIndex">The declared shader-property index.</param>
        /// <returns>A compatible transient texture sentinel.</returns>
        private Texture CreateTextureSentinel(Shader shader, int propertyIndex)
        {
            TextureDimension dimension = shader.GetPropertyTextureDimension(propertyIndex);
            Texture texture;
            switch (dimension)
            {
                case TextureDimension.Tex2D:
                    var texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                    texture2D.SetPixel(0, 0, new Color(0.17f, 0.43f, 0.71f, 1.0f));
                    texture2D.Apply(false, false);
                    texture = texture2D;
                    break;
                case TextureDimension.Tex2DArray:
                    texture = new Texture2DArray(2, 2, 1, TextureFormat.RGBA32, false, true);
                    break;
                case TextureDimension.Tex3D:
                    texture = new Texture3D(2, 2, 2, TextureFormat.RGBA32, false);
                    break;
                case TextureDimension.Cube:
                    texture = new Cubemap(2, TextureFormat.RGBA32, false);
                    break;
                case TextureDimension.CubeArray:
                    texture = new CubemapArray(2, 1, TextureFormat.RGBA32, false);
                    break;
                default:
                    Assert.Fail(
                        $"Shader property '{shader.GetPropertyName(propertyIndex)}' has unsupported texture dimension '{dimension}'."
                    );
                    return null;
            }

            transientTextures.Add(texture);
            return texture;
        }

        /// <summary>Creates transient non-Pure-Base materials that fill any property-type coverage gap in all atomicity paths.</summary>
        /// <param name="seededPropertyTypes">The property types observed while seeding existing atomicity targets.</param>
        /// <param name="capturedPropertyTypes">The property types observed while capturing existing atomicity targets.</param>
        /// <param name="assertedPropertyTypes">The property types observed while asserting existing atomicity targets.</param>
        /// <returns>One tracked material for every property type not already covered by all paths.</returns>
        private IEnumerable<Material> CreateAtomicityCoverageMaterials(
            ISet<ShaderUtil.ShaderPropertyType> seededPropertyTypes,
            ISet<ShaderUtil.ShaderPropertyType> capturedPropertyTypes,
            ISet<ShaderUtil.ShaderPropertyType> assertedPropertyTypes
        )
        {
            foreach (ShaderUtil.ShaderPropertyType propertyType in RequiredAtomicityPropertyTypes)
            {
                if (
                    seededPropertyTypes.Contains(propertyType)
                    && capturedPropertyTypes.Contains(propertyType)
                    && assertedPropertyTypes.Contains(propertyType)
                )
                    continue;
                yield return CreateMaterial(
                    RequireSupportedNonProductShaderWithPropertyType(propertyType)
                );
            }
        }

        /// <summary>Returns a deterministic supported non-Pure-Base shader that exposes one required property type.</summary>
        /// <param name="propertyType">The property type required by atomicity coverage.</param>
        /// <returns>An imported, supported non-Pure-Base shader.</returns>
        private static Shader RequireSupportedNonProductShaderWithPropertyType(
            ShaderUtil.ShaderPropertyType propertyType
        )
        {
            Shader shader = RequireUnsupportedRenderingModeShader();
            for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
            {
                if (ShaderUtil.GetPropertyType(shader, index) == propertyType)
                    return shader;
            }

            Assert.Fail(
                $"The deterministic non-Pure-Base fixture shader did not expose '{propertyType}' for atomicity coverage."
            );
            return null;
        }

        /// <summary>Records one property type observed by an atomicity execution path.</summary>
        /// <param name="observedPropertyTypes">The optional path-local observed type set.</param>
        /// <param name="propertyType">The property type encountered by the path.</param>
        private static void ObserveAtomicityPropertyType(
            ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes,
            ShaderUtil.ShaderPropertyType propertyType
        )
        {
            if (observedPropertyTypes != null)
                observedPropertyTypes.Add(propertyType);
        }

        /// <summary>Requires one atomicity execution path to exercise every supported property type.</summary>
        /// <param name="observedPropertyTypes">The types observed by the execution path.</param>
        /// <param name="pathName">The diagnostic name of the execution path.</param>
        private static void AssertCompleteAtomicityPropertyTypeCoverage(
            ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes,
            string pathName
        )
        {
            CollectionAssert.AreEquivalent(
                RequiredAtomicityPropertyTypes,
                observedPropertyTypes,
                $"The atomicity {pathName} path must exercise every supported shader property type."
            );
        }

        /// <summary>Records every property type visible to one atomicity assertion path.</summary>
        /// <param name="material">The material whose shader properties are being asserted.</param>
        /// <param name="observedPropertyTypes">The optional path-local observed type set.</param>
        private static void ObserveAtomicityPropertyTypes(
            Material material,
            ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes
        )
        {
            Shader shader = material.shader;
            for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
                ObserveAtomicityPropertyType(
                    observedPropertyTypes,
                    ShaderUtil.GetPropertyType(shader, index)
                );
        }

        /// <summary>Returns one supported non-Pure-Base shader that has no rendering-mode property.</summary>
        /// <returns>A supported shader that is not owned by Pure-Base.</returns>
        private static Shader RequireUnsupportedShaderWithoutRenderingMode()
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            Assert.That(shader, Is.Not.Null, "No built-in unsupported shader was available.");
            Assert.That(
                shader.FindPropertyIndex("_RenderingMode"),
                Is.LessThan(0),
                "The missing-property shader must not expose _RenderingMode."
            );
            return shader;
        }

        /// <summary>Returns one supported non-Pure-Base shader that independently exposes the common rendering-mode property.</summary>
        /// <returns>A non-Pure-Base shader with <c>_RenderingMode</c>.</returns>
        private static Shader RequireUnsupportedRenderingModeShader()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(
                UnsupportedRenderingModeFixturePath
            );
            Assert.That(
                shader,
                Is.Not.Null,
                $"The unsupported-ownership fixture shader was not imported at '{UnsupportedRenderingModeFixturePath}'."
            );
            Assert.That(
                shader.name,
                Is.EqualTo("PureBaseTests/Unsupported Rendering Mode"),
                "The unsupported-ownership fixture shader name changed."
            );
            Assert.That(
                shader.name,
                Does.Not.StartWith("PureBase/"),
                "The unsupported-ownership fixture shader must not be owned by Pure-Base."
            );
            Assert.That(
                ShaderUtil.ShaderHasError(shader),
                Is.False,
                "The unsupported-ownership fixture shader has import errors."
            );
            Assert.That(
                shader.isSupported,
                Is.True,
                "The unsupported-ownership fixture shader is unsupported."
            );
            Assert.That(
                shader.FindPropertyIndex("_RenderingMode"),
                Is.GreaterThanOrEqualTo(0),
                "The unsupported-ownership fixture shader must expose _RenderingMode."
            );
            return shader;
        }

        /// <summary>Returns the product shader's ordered visible property names.</summary>
        /// <param name="shader">The shader to inspect.</param>
        /// <returns>The visible property names in declaration order.</returns>
    }
}
