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
    /// <summary>Defines Editor-side rendering-mode contracts before the product normalizer is implemented.</summary>
    public sealed class PureBaseRenderingModeContractTests
    {
        /// <summary>Tracks transient materials so each test releases every native Unity object it created.</summary>
        private readonly List<Material> transientMaterials = new List<Material>();

        /// <summary>Tracks transient texture sentinels used to make invalid-input atomicity snapshots discriminating.</summary>
        private readonly List<Texture> transientTextures = new List<Texture>();

        /// <summary>Identifies the package-local root used only by persistence tests.</summary>
        private const string TemporaryAssetRoot = "Assets/PureBaseRenderingModeTests";

        /// <summary>Identifies the pre-rendering-mode material fixture that must remain byte-identical.</summary>
        private const string LegacyFixturePath =
            "Packages/jp.penguin.purebase/Tests/Fixtures/Materials/PureBaseLegacyCutout.mat";

        /// <summary>Matches the required Shader-Core property declaration without relying on reflection metadata.</summary>
        private const string RenderingModePropertySourcePattern =
            @"SC_uint\s*\(\s*_RenderingMode\s*,\s*1(?:\.0+)?\s*,\s*\[\s*PureBaseRenderingMode\s*\]\s*,\s*""[^""\r\n]*""\s*,\s*""[^""\r\n]*""\s*\)";

        /// <summary>Lists the public product shaders and their complete visible property ABI.</summary>
        private static readonly ProductContract[] Products =
        {
            new ProductContract(
                "PureBase/Unlit",
                "Packages/jp.penguin.purebase/Shaders/PureBaseUnlit_properties.hlsl",
                new[] { "_BaseTexture", "_BaseColor", "_SharedMask", "_SharedGradients", "_RenderingMode", "_Cutoff", "_Cull" }
            ),
            new ProductContract(
                "PureBase/Toon",
                "Packages/jp.penguin.purebase/Shaders/PureBaseToon_properties.hlsl",
                new[]
                {
                    "_BaseTexture", "_BaseColor", "_SharedMask", "_SharedGradients", "_RenderingMode", "_Cutoff", "_Cull", "_NormalMap", "_NormalScale",
                }
            ),
            new ProductContract(
                "PureBase/PBR",
                "Packages/jp.penguin.purebase/Shaders/PureBasePBR_properties.hlsl",
                new[]
                {
                    "_BaseTexture", "_BaseColor", "_SharedMask", "_SharedGradients", "_RenderingMode", "_Cutoff", "_Cull", "_NormalMap", "_NormalScale", "_Metallic", "_Roughness",
                }
            ),
            new ProductContract(
                "PureBase/Hybrid",
                "Packages/jp.penguin.purebase/Shaders/PureBaseHybrid_properties.hlsl",
                new[]
                {
                    "_BaseTexture", "_BaseColor", "_SharedMask", "_SharedGradients", "_RenderingMode", "_Cutoff", "_Cull", "_NormalMap", "_NormalScale", "_Metallic", "_Roughness",
                }
            ),
        };

        /// <summary>Lists the hidden material-state properties synchronized by the normalizer.</summary>
        private static readonly string[] HiddenStatePropertyNames =
        {
            "_SrcBlend",
            "_DstBlend",
            "_ZWrite",
            "_AddSrcBlend",
            "_AddDstBlend",
        };

        /// <summary>Lists the only local keywords the rendering-mode feature may declare.</summary>
        private static readonly string[] RenderingModeKeywords =
        {
            "PUREBASE_RENDERING_OPAQUE",
            "PUREBASE_RENDERING_TRANSPARENT",
        };

        /// <summary>Lists the source-level pass ABI retained by every product material.</summary>
        private static readonly string[] PassNames =
        {
            "ForwardBase",
            "ForwardAdd",
            "ShadowCaster",
            "Meta",
        };

        /// <summary>Lists every ShaderUtil property type whose invalid-input atomicity path must execute.</summary>
        private static readonly ShaderUtil.ShaderPropertyType[] RequiredAtomicityPropertyTypes =
        {
            ShaderUtil.ShaderPropertyType.Float,
            ShaderUtil.ShaderPropertyType.Range,
            ShaderUtil.ShaderPropertyType.Int,
            ShaderUtil.ShaderPropertyType.Color,
            ShaderUtil.ShaderPropertyType.Vector,
            ShaderUtil.ShaderPropertyType.TexEnv,
        };

        /// <summary>Defines the complete state expected for one explicit material rendering mode.</summary>
        private static readonly ModeContract[] Modes =
        {
            new ModeContract(
                0,
                "Opaque",
                (int)BlendMode.One,
                (int)BlendMode.Zero,
                1,
                (int)BlendMode.One,
                (int)BlendMode.One,
                "Opaque",
                true,
                "Opaque",
                2000,
                2000,
                new[] { "PUREBASE_RENDERING_OPAQUE" },
                true
            ),
            new ModeContract(
                1,
                "Cutout",
                (int)BlendMode.One,
                (int)BlendMode.Zero,
                1,
                (int)BlendMode.One,
                (int)BlendMode.One,
                string.Empty,
                false,
                "TransparentCutout",
                -1,
                (int)RenderQueue.AlphaTest,
                Array.Empty<string>(),
                true
            ),
            new ModeContract(
                2,
                "Transparent",
                (int)BlendMode.SrcAlpha,
                (int)BlendMode.OneMinusSrcAlpha,
                0,
                (int)BlendMode.SrcAlpha,
                (int)BlendMode.One,
                "Transparent",
                true,
                "Transparent",
                3000,
                3000,
                new[] { "PUREBASE_RENDERING_TRANSPARENT" },
                false
            ),
        };

        /// <summary>Requires the complete shader ABI, static Cutout defaults, pass ABI, and local-keyword declaration.</summary>
        [Test]
        public void ProductShadersExposeRenderingModeAndCutoutCompatibleStaticDefaults()
        {
            foreach (ProductContract product in Products)
            {
                Shader shader = RequireProductShader(product.shaderName);
                CollectionAssert.AreEqual(
                    product.visiblePropertyNames,
                    GetVisiblePropertyNames(shader),
                    $"Product shader '{product.shaderName}' changed its public property ABI."
                );

                int modeIndex = shader.FindPropertyIndex("_RenderingMode");
                Assert.That(modeIndex, Is.GreaterThanOrEqualTo(0), $"Product shader '{product.shaderName}' must expose _RenderingMode.");
                Assert.That(
                    shader.GetPropertyType(modeIndex),
                    Is.EqualTo(ShaderPropertyType.Int),
                    $"Product shader '{product.shaderName}' must expose _RenderingMode as an Integer property."
                );
                CollectionAssert.Contains(
                    shader.GetPropertyAttributes(modeIndex),
                    "PureBaseRenderingMode",
                    $"Product shader '{product.shaderName}' must use the Pure-Base rendering-mode drawer."
                );
                Assert.That(
                    Regex.IsMatch(File.ReadAllText(product.propertySourcePath), RenderingModePropertySourcePattern),
                    Is.True,
                    $"Product property source '{product.propertySourcePath}' must declare _RenderingMode as SC_uint with default 1 and the PureBaseRenderingMode drawer."
                );

                var material = CreateMaterial(shader);
                {
                    Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(1));
                    AssertHiddenState(material, Modes[1]);
                    Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
                    Assert.That(material.GetTag("RenderType", false), Is.EqualTo("TransparentCutout"));
                    Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.True);
                    Assert.That(material.GetShaderPassEnabled("Meta"), Is.True);
                    AssertRenderingKeywords(material, Array.Empty<string>());
                }

                CollectionAssert.AreEqual(PassNames, GetPassNames(shader));
                string source = LoadGeneratedSource(product.shaderName);
                AssertRenderingModeKeywordDeclarations(source, product.shaderName);
            }
        }

        /// <summary>Requires a new unsaved material to behave as Cutout without creating persistence dirtiness.</summary>
        [Test]
        public void NewMaterialWithoutSavedModeRemainsReadOnlyCutoutUntilExplicitNormalization()
        {
            Shader shader = RequireProductShader("PureBase/Unlit");
            var material = CreateMaterial(shader);
            {
                Assert.That(shader.FindPropertyIndex("_RenderingMode"), Is.GreaterThanOrEqualTo(0));
                EditorUtility.ClearDirty(material);
                Assert.That(EditorUtility.IsDirty(material), Is.False, "The Inspector-bind test must establish a clean baseline.");
                MaterialState baseline = MaterialState.Capture(material);
                MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material });
                baseline.AssertEqual(material, "Inspector bind");
                Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(1));
                AssertHiddenState(material, Modes[1]);
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
                Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.True);
                Assert.That(material.GetShaderPassEnabled("Meta"), Is.True);
                AssertRenderingKeywords(material, Array.Empty<string>());
            }
        }

        /// <summary>Ensures a 0.1.x serialized material keeps all noncanonical overrides after an Inspector bind and save-reload.</summary>
        [Test]
        public void LegacyCutoutFixtureRemainsByteAndStateIdenticalAcrossReadOnlyBindAndSaveReload()
        {
            byte[] beforeBytes = File.ReadAllBytes(LegacyFixturePath);
            string beforeText = File.ReadAllText(LegacyFixturePath);
            Assert.That(beforeText.IndexOf("_RenderingMode", StringComparison.Ordinal), Is.LessThan(0));

            AssetDatabase.ImportAsset(LegacyFixturePath, ImportAssetOptions.ForceSynchronousImport);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(LegacyFixturePath);
            Assert.That(material, Is.Not.Null, "The legacy fixture did not import as a material.");
            Assert.That(material.shader.FindPropertyIndex("_RenderingMode"), Is.GreaterThanOrEqualTo(0));
            MaterialState before = MaterialState.Capture(material);
            AssertLegacyState(before);
            MaterialEditor.GetMaterialProperties(new UnityEngine.Object[] { material });
            Assert.That(EditorUtility.IsDirty(material), Is.False, "Binding a legacy material must not normalize it.");

            SaveOnlyOwnedAssetAndReimport(material, LegacyFixturePath);
            material = AssetDatabase.LoadAssetAtPath<Material>(LegacyFixturePath);
            Assert.That(material, Is.Not.Null);
            AssertLegacyState(MaterialState.Capture(material));
            CollectionAssert.AreEqual(beforeBytes, File.ReadAllBytes(LegacyFixturePath));
        }

        /// <summary>Requires the public normalizer API and checks every product against the complete explicit state table.</summary>
        [Test]
        public void ExplicitModeNormalizationMatchesTheCompleteFourByThreeStateTable()
        {
            MethodInfo apply = RequireApplyMethod();
            foreach (ProductContract product in Products)
            {
                var material = CreateMaterial(RequireProductShader(product.shaderName));
                {
                    foreach (ModeContract mode in Modes)
                    {
                        material.SetInteger("_RenderingMode", mode.value);
                        InvokeApply(apply, material);
                        Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(mode.value), $"{product.shaderName} {mode.name} mode value.");
                        AssertRenderTypeState(material, mode);
                        Assert.That(GetRawRenderQueue(material), Is.EqualTo(mode.rawQueue));
                        Assert.That(material.renderQueue, Is.EqualTo(mode.resolvedQueue));
                        AssertHiddenState(material, mode);
                        AssertRenderingKeywords(material, mode.enabledKeywords);
                        Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.EqualTo(mode.enableContributionPasses));
                        Assert.That(material.GetShaderPassEnabled("Meta"), Is.EqualTo(mode.enableContributionPasses));
                    }
                }
            }
        }

        /// <summary>Requires the public enum and method shape through reflection so missing production code remains a test failure.</summary>
        [Test]
        public void PublicRenderingModeApiIsDiscoverableWithoutATestAssemblyDependency()
        {
            Type enumType = FindLoadedType("PureBase.Editor.PureBaseRenderingMode");
            Assert.That(enumType, Is.Not.Null, "PureBaseRenderingMode must be discoverable from the loaded Editor assemblies.");
            Assert.That(enumType.IsPublic, Is.True, "PureBaseRenderingMode must be public.");
            Assert.That(enumType.IsEnum, Is.True, "PureBaseRenderingMode must be an enum.");
            CollectionAssert.AreEqual(
                new[] { "Opaque", "Cutout", "Transparent" },
                Enum.GetNames(enumType),
                "PureBaseRenderingMode must expose exactly the three stable public names without aliases."
            );
            Array enumValues = Enum.GetValues(enumType);
            var numericValues = new int[enumValues.Length];
            for (int index = 0; index < enumValues.Length; index++)
                numericValues[index] = Convert.ToInt32(enumValues.GetValue(index));
            CollectionAssert.AreEqual(
                new[] { 0, 1, 2 },
                numericValues,
                "PureBaseRenderingMode must expose exactly the stable 0, 1, and 2 ABI values without aliases."
            );
            Type normalizerType = FindLoadedType("PureBase.Editor.PureBaseMaterialRenderingMode");
            Assert.That(normalizerType, Is.Not.Null, "PureBaseMaterialRenderingMode must be discoverable from the loaded Editor assemblies.");
            Assert.That(normalizerType.IsPublic, Is.True, "PureBaseMaterialRenderingMode must be public.");
            Assert.That(RequireApplyMethod(), Is.Not.Null);
        }

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
            {
                SeedAtomicityState(unsupportedOwnership, seededPropertyTypes);
                Assert.That(
                    unsupportedOwnership.HasProperty("_RenderingMode"),
                    Is.True,
                    "The unsupported ownership input must expose _RenderingMode without being owned by Pure-Base."
                );
                MaterialState before = MaterialState.Capture(unsupportedOwnership, capturedPropertyTypes);
                Assert.Throws<InvalidOperationException>(() => InvokeApply(apply, unsupportedOwnership));
                before.AssertEqual(unsupportedOwnership, "non-Pure-Base shader with _RenderingMode", assertedPropertyTypes);
            }

            var unsupportedMissingProperty = CreateMaterial(RequireUnsupportedShaderWithoutRenderingMode());
            {
                SeedAtomicityState(unsupportedMissingProperty, seededPropertyTypes);
                Assert.That(
                    unsupportedMissingProperty.HasProperty("_RenderingMode"),
                    Is.False,
                    "The missing-property input must not expose _RenderingMode."
                );
                MaterialState before = MaterialState.Capture(unsupportedMissingProperty, capturedPropertyTypes);
                Assert.Throws<InvalidOperationException>(() => InvokeApply(apply, unsupportedMissingProperty));
                before.AssertEqual(unsupportedMissingProperty, "non-Pure-Base shader without _RenderingMode", assertedPropertyTypes);
            }

            var first = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            var second = CreateMaterial(RequireProductShader("PureBase/Toon"));
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
                    ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => InvokeApply(apply, first));
                    AssertInvalidRenderingModeException(exception, first, invalidMode, "single-target invalid mode");
                    firstBefore.AssertEqual(first, $"invalid mode {invalidMode}", assertedPropertyTypes);
                    secondBefore.AssertEqual(second, $"unrelated target after invalid mode {invalidMode}", assertedPropertyTypes);

                    exception = Assert.Throws<ArgumentOutOfRangeException>(() => InvokeApplyAll(applyAll, new[] { first, second }));
                    AssertInvalidRenderingModeException(exception, first, invalidMode, "batch invalid mode");
                    firstBefore.AssertEqual(first, $"batch invalid mode {invalidMode}", assertedPropertyTypes);
                    secondBefore.AssertEqual(second, $"unrelated target after batch invalid mode {invalidMode}", assertedPropertyTypes);
                }
            }

            foreach (Material coverageMaterial in CreateAtomicityCoverageMaterials(seededPropertyTypes, capturedPropertyTypes, assertedPropertyTypes))
            {
                SeedAtomicityState(coverageMaterial, seededPropertyTypes);
                MaterialState before = MaterialState.Capture(coverageMaterial, capturedPropertyTypes);
                Assert.Throws<InvalidOperationException>(() => InvokeApply(apply, coverageMaterial));
                before.AssertEqual(coverageMaterial, "non-Pure-Base property-type coverage target", assertedPropertyTypes);
            }

            AssertCompleteAtomicityPropertyTypeCoverage(seededPropertyTypes, "seed");
            AssertCompleteAtomicityPropertyTypeCoverage(capturedPropertyTypes, "capture");
            AssertCompleteAtomicityPropertyTypeCoverage(assertedPropertyTypes, "assertion");
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
            second.SetOverrideTag("RenderType", "LegacyTransparent");
            foreach (int invalidMode in new[] { -1, 3 })
            {
                failing.SetInteger("_RenderingMode", 1);
                EditorUtility.ClearDirty(first);
                EditorUtility.ClearDirty(second);
                EditorUtility.ClearDirty(failing);
                MaterialState firstBefore = MaterialState.Capture(first);
                MaterialState secondBefore = MaterialState.Capture(second);
                var materials = new LateInvalidatingMaterialList(new[] { first, second, failing }, 2, invalidMode);

                ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => InvokeApplyAll(applyAll, materials));
                AssertInvalidRenderingModeException(exception, failing, invalidMode, "late batch invalid mode");
                Assert.That(materials.ObservedPriorMutations, Is.True, "The late invalidation must occur after prior materials are normalized.");
                firstBefore.AssertEqual(first, "first material after late batch rollback");
                secondBefore.AssertEqual(second, "second material after late batch rollback");
                Assert.That(AssetDatabase.GetAssetPath(first), Is.Empty, "The rollback fixture must remain transient.");
                Assert.That(AssetDatabase.GetAssetPath(second), Is.Empty, "The rollback fixture must remain transient.");
                Assert.That(AssetDatabase.GetAssetPath(failing), Is.Empty, "The failure fixture must remain transient.");
            }
        }

        /// <summary>Requires the registered Shader-Core drawer to preserve mixed values without mutating a clean normalized selection.</summary>
        [Test]
        public void InspectorDrawerIsRegisteredForMixedSelectionAndExposesOneAtomicUndoWorkflow()
        {
            Assert.That(
                FindLoadedType("PureBase.Editor.PureBaseRenderingModeElement"),
                Is.Not.Null,
                "The dedicated rendering-mode Inspector drawer must be loaded."
            );

            Type attributeActionsType = FindLoadedType("jp.lilxyzw.shadercore.AttributeActions");
            Assert.That(attributeActionsType, Is.Not.Null, "Shader-Core AttributeActions was not loaded.");
            MethodInfo containsKey = attributeActionsType.GetMethod(
                "ContainsKey",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null
            );
            Assert.That(containsKey, Is.Not.Null);
            Assert.That((bool)containsKey.Invoke(null, new object[] { "PureBaseRenderingMode" }), Is.True);

            var opaque = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            var transparent = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            {
                MethodInfo apply = RequireApplyMethod();
                MethodInfo refreshSelection = RequireDrawerSelectionRefreshMethod();
                MethodInfo getSelectionDisplayState = RequireDrawerSelectionDisplayStateMethod();
                opaque.SetInteger("_RenderingMode", 0);
                transparent.SetInteger("_RenderingMode", 2);
                EditorUtility.ClearDirty(opaque);
                EditorUtility.ClearDirty(transparent);
                Assert.That(EditorUtility.IsDirty(opaque), Is.False, "The Opaque resync target must be clean before explicit normalization.");
                Assert.That(EditorUtility.IsDirty(transparent), Is.False, "The Transparent resync target must be clean before explicit normalization.");
                InvokeApply(apply, opaque);
                Assert.That(EditorUtility.IsDirty(opaque), Is.True, "Explicit normalization must move the clean Opaque resync target to dirty.");
                EditorUtility.ClearDirty(transparent);
                Assert.That(EditorUtility.IsDirty(transparent), Is.False, "The Transparent resync target must be clean immediately before its own explicit normalization.");
                InvokeApply(apply, transparent);
                Assert.That(EditorUtility.IsDirty(transparent), Is.True, "Explicit normalization must move the clean Transparent resync target to dirty.");
                EditorUtility.ClearDirty(opaque);
                EditorUtility.ClearDirty(transparent);
                Assert.That(EditorUtility.IsDirty(opaque), Is.False, "The Opaque mixed-selection baseline must be clean.");
                Assert.That(EditorUtility.IsDirty(transparent), Is.False, "The Transparent mixed-selection baseline must be clean.");
                MaterialState opaqueBaseline = MaterialState.Capture(opaque);
                MaterialState transparentBaseline = MaterialState.Capture(transparent);
                MaterialProperty property = MaterialEditor.GetMaterialProperty(
                    new UnityEngine.Object[] { opaque, transparent },
                    "_RenderingMode"
                );
                Assert.That(property.hasMixedValue, Is.True, "The rendering-mode field must expose mixed state before user selection.");
                opaqueBaseline.AssertEqual(opaque, "Opaque target after mixed field binding");
                transparentBaseline.AssertEqual(transparent, "Transparent target after mixed field binding");
                object selectionDisplayState = InvokeDrawerSelectionDisplayState(getSelectionDisplayState, new[] { opaque, transparent });
                AssertSelectionDisplayState(selectionDisplayState, true, new[] { "Opaque", "Cutout", "Transparent" });
                opaqueBaseline.AssertEqual(opaque, "Opaque target after mixed drawer display-state read");
                transparentBaseline.AssertEqual(transparent, "Transparent target after mixed drawer display-state read");
                InvokeDrawerSelectionRefresh(refreshSelection, new[] { opaque, transparent });
                opaqueBaseline.AssertEqual(opaque, "Opaque target after read-only mixed refresh");
                transparentBaseline.AssertEqual(transparent, "Transparent target after read-only mixed refresh");
            }
        }

        /// <summary>Requires the drawer's one-action multi-target boundary to validate, normalize, undo, redo, and refresh without incidental mutation.</summary>
        [Test]
        public void InspectorMultiTargetActionIsAtomicAndUndoRedoRefreshesAreReadOnly()
        {
            MethodInfo apply = RequireApplyMethod();
            MethodInfo applySelection = RequireDrawerSelectionApplyMethod();
            MethodInfo refreshSelection = RequireDrawerSelectionRefreshMethod();
            var first = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            var second = CreateMaterial(RequireProductShader("PureBase/Toon"));
            var unsupported = CreateMaterial(RequireUnsupportedRenderingModeShader());
            int initialUndoGroup = Undo.GetCurrentGroup();
            try
            {
                first.SetInteger("_RenderingMode", 0);
                second.SetInteger("_RenderingMode", 1);
                InvokeApply(apply, first);
                InvokeApply(apply, second);
                MaterialState firstBefore = MaterialState.Capture(first);
                MaterialState secondBefore = MaterialState.Capture(second);
                MaterialState unsupportedBefore = MaterialState.Capture(unsupported);
                int undoBeforeRejectedSelection = Undo.GetCurrentGroup();

                Assert.Throws<InvalidOperationException>(
                    () => InvokeDrawerSelectionApply(applySelection, new[] { first, second, unsupported }, 2),
                    "The drawer must validate every selected material before mutating any valid target."
                );
                firstBefore.AssertEqual(first, "valid target after rejected mixed selection");
                secondBefore.AssertEqual(second, "second valid target after rejected mixed selection");
                unsupportedBefore.AssertEqual(unsupported, "unsupported target after rejected mixed selection");
                Assert.That(
                    Undo.GetCurrentGroup(),
                    Is.EqualTo(undoBeforeRejectedSelection),
                    "A rejected multi-target selection must not create an Undo group before validation succeeds."
                );

                InvokeDrawerSelectionApply(applySelection, new[] { first, second }, 2);
                int editUndoGroup = Undo.GetCurrentGroup();
                Assert.That(
                    editUndoGroup,
                    Is.EqualTo(initialUndoGroup + 1),
                    "One multi-target mode selection must create exactly one Undo group."
                );
                AssertModeState(first, Modes[2]);
                AssertModeState(second, Modes[2]);

                Undo.PerformUndo();
                firstBefore.AssertEqual(first, "first target after Undo");
                secondBefore.AssertEqual(second, "second target after Undo");
                InvokeDrawerSelectionRefresh(refreshSelection, new[] { first, second });
                firstBefore.AssertEqual(first, "first target after read-only Undo refresh");
                secondBefore.AssertEqual(second, "second target after read-only Undo refresh");

                Undo.PerformRedo();
                AssertModeState(first, Modes[2]);
                AssertModeState(second, Modes[2]);
                MaterialState firstRedo = MaterialState.Capture(first);
                MaterialState secondRedo = MaterialState.Capture(second);
                InvokeDrawerSelectionRefresh(refreshSelection, new[] { first, second });
                firstRedo.AssertEqual(first, "first target after read-only Redo refresh");
                secondRedo.AssertEqual(second, "second target after read-only Redo refresh");
            }
            finally
            {
                Undo.RevertAllDownToGroup(initialUndoGroup);
            }
        }

        /// <summary>Requires explicit normalization to survive material and prefab save-reload while deleting every temporary asset.</summary>
        [Test]
        public void ExplicitNormalizationPersistsThroughMaterialAndPrefabSaveReloadAndCleansUp()
        {
            string materialPath = TemporaryAssetRoot + "/mode.mat";
            string prefabPath = TemporaryAssetRoot + "/mode.prefab";
            var retainedPaths = new List<string>();
            try
            {
                Assert.That(AssetDatabase.IsValidFolder(TemporaryAssetRoot), Is.False, "Temporary asset root already exists.");
                AssetDatabase.CreateFolder("Assets", "PureBaseRenderingModeTests");
                var material = CreateMaterial(RequireProductShader("PureBase/Toon"));
                AssetDatabase.CreateAsset(material, materialPath);
                material.SetInteger("_RenderingMode", 2);
                InvokeApply(RequireApplyMethod(), material);
                Assert.That(EditorUtility.IsDirty(material), Is.True, "Explicit normalization must dirty the temporary material before the path-scoped save.");
                SaveOnlyOwnedAssetAndReimport(material, materialPath);
                material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                Assert.That(material, Is.Not.Null);
                var instance = GameObject.CreatePrimitive(PrimitiveType.Quad);
                try
                {
                    instance.GetComponent<Renderer>().sharedMaterial = material;
                    PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                }

                GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(savedPrefab, Is.Not.Null);
                SaveOnlyOwnedAssetAndReimport(savedPrefab, prefabPath);
                AssetDatabase.ImportAsset(materialPath, ImportAssetOptions.ForceSynchronousImport);
                Material reloaded = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                Assert.That(reloaded, Is.Not.Null);
                AssertModeState(reloaded, Modes[2]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null);
                Assert.That(prefab.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(reloaded));
            }
            finally
            {
                if (!AssetDatabase.DeleteAsset(TemporaryAssetRoot))
                    retainedPaths.Add(TemporaryAssetRoot);
                if (AssetDatabase.IsValidFolder(TemporaryAssetRoot))
                    retainedPaths.Add(TemporaryAssetRoot);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(materialPath) != null)
                    retainedPaths.Add(materialPath);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath) != null)
                    retainedPaths.Add(prefabPath);
                Assert.That(retainedPaths, Is.Empty, $"Rendering-mode persistence test retained temporary assets: {string.Join(", ", retainedPaths)}.");
            }
        }

        /// <summary>Saves and synchronously reimports one test-owned asset without persisting unrelated dirty Editor assets.</summary>
        /// <param name="asset">The exact fixture or temporary asset owned by this test.</param>
        /// <param name="assetPath">The expected project-relative path for <paramref name="asset"/>.</param>
        private static void SaveOnlyOwnedAssetAndReimport(UnityEngine.Object asset, string assetPath)
        {
            Assert.That(asset, Is.Not.Null, $"Test-owned asset '{assetPath}' must exist before persistence.");
            Assert.That(AssetDatabase.GetAssetPath(asset), Is.EqualTo(assetPath), "Persistence must target only the supplied test-owned asset path.");
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
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, $"Product shader '{shaderName}' has compiler errors.");
            Assert.That(shader.isSupported, Is.True, $"Product shader '{shaderName}' is unsupported.");
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

        /// <summary>Assigns distinguishable values to every shader property before atomicity snapshots without modifying persistent assets.</summary>
        /// <param name="material">The transient material that must remain unchanged after rejection.</param>
        /// <param name="observedPropertyTypes">The optional set that records seeded shader property types.</param>
        private void SeedAtomicityState(Material material, ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes = null)
        {
            Shader shader = material.shader;
            for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
            {
                string propertyName = shader.GetPropertyName(index);
                ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, index);
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
                        material.SetColor(propertyName, new Color(0.13f + (index * 0.01f), 0.27f, 0.41f, 0.59f));
                        break;
                    case ShaderUtil.ShaderPropertyType.Vector:
                        material.SetVector(propertyName, new Vector4(0.11f, 0.23f, 0.37f, 0.53f + (index * 0.01f)));
                        break;
                    case ShaderUtil.ShaderPropertyType.TexEnv:
                        material.SetTexture(propertyName, CreateTextureSentinel(shader, index));
                        material.SetTextureScale(propertyName, new Vector2(0.71f, 0.83f));
                        material.SetTextureOffset(propertyName, new Vector2(0.17f, 0.29f));
                        break;
                    default:
                        Assert.Fail($"Unsupported shader property type '{ShaderUtil.GetPropertyType(shader, index)}' for '{propertyName}'.");
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
                    Assert.Fail($"Shader property '{shader.GetPropertyName(propertyIndex)}' has unsupported texture dimension '{dimension}'.");
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
            ISet<ShaderUtil.ShaderPropertyType> assertedPropertyTypes)
        {
            foreach (ShaderUtil.ShaderPropertyType propertyType in RequiredAtomicityPropertyTypes)
            {
                if (seededPropertyTypes.Contains(propertyType)
                    && capturedPropertyTypes.Contains(propertyType)
                    && assertedPropertyTypes.Contains(propertyType))
                    continue;
                yield return CreateMaterial(RequireSupportedNonProductShaderWithPropertyType(propertyType));
            }
        }

        /// <summary>Returns a deterministic supported non-Pure-Base shader that exposes one required property type.</summary>
        /// <param name="propertyType">The property type required by atomicity coverage.</param>
        /// <returns>An imported, supported non-Pure-Base shader.</returns>
        private static Shader RequireSupportedNonProductShaderWithPropertyType(ShaderUtil.ShaderPropertyType propertyType)
        {
            string[] guids = AssetDatabase.FindAssets("t:Shader");
            Array.Sort(guids, StringComparer.Ordinal);
            foreach (string guid in guids)
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                if (shader == null || shader.name.StartsWith("PureBase/", StringComparison.Ordinal))
                    continue;
                if (ShaderUtil.ShaderHasError(shader) || !shader.isSupported)
                    continue;
                for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
                {
                    if (ShaderUtil.GetPropertyType(shader, index) == propertyType)
                        return shader;
                }
            }

            Assert.Fail($"No supported non-Pure-Base shader exposing '{propertyType}' was imported for atomicity coverage.");
            return null;
        }

        /// <summary>Records one property type observed by an atomicity execution path.</summary>
        /// <param name="observedPropertyTypes">The optional path-local observed type set.</param>
        /// <param name="propertyType">The property type encountered by the path.</param>
        private static void ObserveAtomicityPropertyType(ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes, ShaderUtil.ShaderPropertyType propertyType)
        {
            if (observedPropertyTypes != null)
                observedPropertyTypes.Add(propertyType);
        }

        /// <summary>Requires one atomicity execution path to exercise every supported property type.</summary>
        /// <param name="observedPropertyTypes">The types observed by the execution path.</param>
        /// <param name="pathName">The diagnostic name of the execution path.</param>
        private static void AssertCompleteAtomicityPropertyTypeCoverage(ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes, string pathName)
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
        private static void ObserveAtomicityPropertyTypes(Material material, ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes)
        {
            Shader shader = material.shader;
            for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
                ObserveAtomicityPropertyType(observedPropertyTypes, ShaderUtil.GetPropertyType(shader, index));
        }

        /// <summary>Returns one supported non-Pure-Base shader that has no rendering-mode property.</summary>
        /// <returns>A supported shader that is not owned by Pure-Base.</returns>
        private static Shader RequireUnsupportedShaderWithoutRenderingMode()
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            Assert.That(shader, Is.Not.Null, "No built-in unsupported shader was available.");
            Assert.That(shader.FindPropertyIndex("_RenderingMode"), Is.LessThan(0), "The missing-property shader must not expose _RenderingMode.");
            return shader;
        }

        /// <summary>Returns one supported non-Pure-Base shader that independently exposes the common rendering-mode property.</summary>
        /// <returns>A non-Pure-Base shader with <c>_RenderingMode</c>.</returns>
        private static Shader RequireUnsupportedRenderingModeShader()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Packages/jp.lilxyzw.nontoon" }))
            {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                if (shader == null || shader.name.StartsWith("PureBase/", StringComparison.Ordinal))
                    continue;
                if (ShaderUtil.ShaderHasError(shader) || shader.FindPropertyIndex("_RenderingMode") < 0)
                    continue;
                return shader;
            }

            Assert.Fail("No supported non-Pure-Base shader exposing _RenderingMode was imported for unsupported-ownership validation.");
            return null;
        }

        /// <summary>Returns the product shader's ordered visible property names.</summary>
        /// <param name="shader">The shader to inspect.</param>
        /// <returns>The visible property names in declaration order.</returns>
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
            foreach (Match match in Regex.Matches(LoadGeneratedSource(shader.name), "\\bName\\s+\\\"([^\\\"]+)\\\""))
                names.Add(match.Groups[1].Value);
            return names.ToArray();
        }

        /// <summary>Loads the generated source subasset for one imported product shader without requesting a reimport.</summary>
        /// <param name="shaderName">The imported public shader name.</param>
        /// <returns>The non-empty generated source text.</returns>
        private static string LoadGeneratedSource(string shaderName)
        {
            string path = null;
            foreach (string guid in AssetDatabase.FindAssets("t:Shader", new[] { "Packages/jp.penguin.purebase/Shaders" }))
            {
                string candidate = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(candidate);
                if (shader != null && string.Equals(shader.name, shaderName, StringComparison.Ordinal))
                {
                    path = candidate;
                    break;
                }
            }

            Assert.That(path, Is.Not.Empty, $"Could not locate the Shader-Core source asset for '{shaderName}'.");
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                var source = asset as TextAsset;
                if (source != null && string.Equals(source.name, "Shader Source", StringComparison.Ordinal))
                    return source.text;
            }

            Assert.Fail($"Shader-Core source asset '{path}' for '{shaderName}' has no generated Shader Source subasset.");
            return null;
        }

        /// <summary>Returns the required public normalizer method without statically referencing its not-yet-created assembly.</summary>
        /// <returns>The public static <c>Apply(Material)</c> method.</returns>
        private static MethodInfo RequireApplyMethod()
        {
            Type type = FindLoadedType("PureBase.Editor.PureBaseMaterialRenderingMode");
            Assert.That(type, Is.Not.Null, "PureBaseMaterialRenderingMode must be loaded from PureBase.Editor.");
            Assert.That(type.IsPublic, Is.True, "PureBaseMaterialRenderingMode must be public.");
            MethodInfo method = type.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
            Assert.That(method, Is.Not.Null, "PureBaseMaterialRenderingMode must expose public static Apply(Material).");
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)), "PureBaseMaterialRenderingMode.Apply(Material) must return void.");
            return method;
        }

        /// <summary>Returns the internal validated batch boundary used to verify rollback after an apply-time failure.</summary>
        /// <returns>The static <c>ApplyAll(IReadOnlyList&lt;Material&gt;)</c> method.</returns>
        private static MethodInfo RequireApplyAllMethod()
        {
            Type type = FindLoadedType("PureBase.Editor.PureBaseMaterialRenderingMode");
            Assert.That(type, Is.Not.Null, "PureBaseMaterialRenderingMode must be loaded from PureBase.Editor.");
            MethodInfo method = type.GetMethod(
                "ApplyAll",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(IReadOnlyList<Material>) },
                null
            );
            Assert.That(method, Is.Not.Null, "PureBaseMaterialRenderingMode must retain the validated batch boundary.");
            return method;
        }

        /// <summary>Returns the drawer operation that applies one selected mode to every validated target in one user action.</summary>
        /// <returns>The static <c>ApplySelection(Material[], int)</c> drawer operation.</returns>
        private static MethodInfo RequireDrawerSelectionApplyMethod()
        {
            return RequireDrawerMethod("ApplySelection", new[] { typeof(Material[]), typeof(int) });
        }

        /// <summary>Returns the drawer operation that refreshes the current selection without applying or normalizing material state.</summary>
        /// <returns>The static <c>RefreshSelection(Material[])</c> drawer operation.</returns>
        private static MethodInfo RequireDrawerSelectionRefreshMethod()
        {
            return RequireDrawerMethod("RefreshSelection", new[] { typeof(Material[]) });
        }

        /// <summary>Returns the drawer's read-only selection model boundary used to render mixed state and exact popup choices.</summary>
        /// <returns>The static <c>GetSelectionDisplayState(Material[])</c> drawer operation.</returns>
        private static MethodInfo RequireDrawerSelectionDisplayStateMethod()
        {
            MethodInfo method = RequireDrawerMethod("GetSelectionDisplayState", new[] { typeof(Material[]) });
            Assert.That(method.ReturnType, Is.Not.EqualTo(typeof(void)), "The drawer selection display-state boundary must return a readable UI model.");
            return method;
        }

        /// <summary>Returns one required static drawer operation without adding a compile-time dependency on its future assembly.</summary>
        /// <param name="methodName">The required operation name.</param>
        /// <param name="parameterTypes">The exact operation parameter types.</param>
        /// <returns>The required static drawer operation.</returns>
        private static MethodInfo RequireDrawerMethod(string methodName, Type[] parameterTypes)
        {
            Type type = FindLoadedType("PureBase.Editor.PureBaseRenderingModeElement");
            Assert.That(type, Is.Not.Null, "The dedicated rendering-mode Inspector drawer must be loaded.");
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                parameterTypes,
                null
            );
            Assert.That(
                method,
                Is.Not.Null,
                "PureBaseRenderingModeElement must expose the testable " + methodName + " selection boundary."
            );
            return method;
        }

        /// <summary>Invokes the public normalizer while preserving its original exception type for NUnit assertions.</summary>
        /// <param name="method">The reflected normalizer method.</param>
        /// <param name="material">The material passed to the normalizer.</param>
        private static void InvokeApply(MethodInfo method, Material material)
        {
            InvokeReflectedMethod(method, new object[] { material });
        }

        /// <summary>Invokes the validated batch boundary while preserving its original exception type.</summary>
        /// <param name="method">The reflected batch normalizer method.</param>
        /// <param name="materials">The material list passed to the batch normalizer.</param>
        private static void InvokeApplyAll(MethodInfo method, IReadOnlyList<Material> materials)
        {
            InvokeReflectedMethod(method, new object[] { materials });
        }

        /// <summary>Asserts that one rejected rendering-mode value preserves its established exception contract.</summary>
        /// <param name="exception">The exception thrown for the rejected value.</param>
        /// <param name="material">The rejected material identified by the exception.</param>
        /// <param name="value">The rejected rendering-mode value.</param>
        /// <param name="context">The operation context used in assertion diagnostics.</param>
        private static void AssertInvalidRenderingModeException(ArgumentOutOfRangeException exception, Material material, int value, string context)
        {
            Assert.That(exception, Is.Not.Null, context + " must throw an ArgumentOutOfRangeException.");
            Assert.That(exception.ParamName, Is.EqualTo("_RenderingMode"), context + " exception parameter.");
            Assert.That(exception.ActualValue, Is.EqualTo(value), context + " exception value.");
            StringAssert.Contains(material.name, exception.Message, context + " exception material identity.");
            StringAssert.Contains("0, 1, or 2", exception.Message, context + " exception supported values.");
        }

        /// <summary>Invokes the drawer's one-action multi-target operation while preserving its original exception type.</summary>
        /// <param name="method">The reflected drawer operation.</param>
        /// <param name="materials">The selected material targets.</param>
        /// <param name="mode">The requested serialized rendering-mode value.</param>
        private static void InvokeDrawerSelectionApply(MethodInfo method, Material[] materials, int mode)
        {
            InvokeReflectedMethod(method, new object[] { materials, mode });
        }

        /// <summary>Invokes the drawer's read-only selection refresh while preserving its original exception type.</summary>
        /// <param name="method">The reflected drawer refresh operation.</param>
        /// <param name="materials">The selected material targets.</param>
        private static void InvokeDrawerSelectionRefresh(MethodInfo method, Material[] materials)
        {
            InvokeReflectedMethod(method, new object[] { materials });
        }

        /// <summary>Reads the drawer-owned display model without invoking a user action or normalizing material state.</summary>
        /// <param name="method">The reflected drawer display-state operation.</param>
        /// <param name="materials">The selected material targets.</param>
        /// <returns>The read-only drawer display model.</returns>
        private static object InvokeDrawerSelectionDisplayState(MethodInfo method, Material[] materials)
        {
            return InvokeReflectedMethod(method, new object[] { materials });
        }

        /// <summary>Invokes a reflected operation while preserving its original exception type for NUnit assertions.</summary>
        /// <param name="method">The reflected operation.</param>
        /// <param name="arguments">The operation arguments.</param>
        private static object InvokeReflectedMethod(MethodInfo method, object[] arguments)
        {
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        /// <summary>Asserts the read-only drawer model for one current material selection.</summary>
        /// <param name="displayState">The reflection-returned drawer selection model.</param>
        /// <param name="expectedMixed">Whether the selection must be displayed as mixed.</param>
        /// <param name="expectedChoices">The complete ordered mode labels presented by the popup.</param>
        private static void AssertSelectionDisplayState(object displayState, bool expectedMixed, string[] expectedChoices)
        {
            Assert.That(displayState, Is.Not.Null, "The drawer must return a real selection display model.");
            Assert.That(ReadDisplayStateMember(displayState, "HasMixedValue"), Is.EqualTo(expectedMixed), "The drawer display model mixed indicator.");
            object choices = ReadDisplayStateMember(displayState, "Choices");
            var labels = choices as IEnumerable<string>;
            Assert.That(labels, Is.Not.Null, "The drawer display model Choices member must be a readable string sequence.");
            CollectionAssert.AreEqual(expectedChoices, labels, "The drawer popup must expose exactly the three supported rendering-mode choices.");
        }

        /// <summary>Reads one field or property from a drawer-owned selection display model without depending on its accessibility.</summary>
        /// <param name="displayState">The reflection-returned selection display model.</param>
        /// <param name="memberName">The required field or property name.</param>
        /// <returns>The member value.</returns>
        private static object ReadDisplayStateMember(object displayState, string memberName)
        {
            Type type = displayState.GetType();
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = type.GetProperty(memberName, Flags);
            if (property != null)
                return property.GetValue(displayState, null);
            FieldInfo field = type.GetField(memberName, Flags);
            Assert.That(field, Is.Not.Null, "The drawer display model must expose " + memberName + " as a readable field or property.");
            return field.GetValue(displayState);
        }

        /// <summary>Finds a type from all currently loaded assemblies without introducing a compile-time assembly dependency.</summary>
        /// <param name="fullName">The required fully-qualified type name.</param>
        /// <returns>The loaded type, or <see langword="null"/>.</returns>
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
            Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.EqualTo(mode.enableContributionPasses));
            Assert.That(material.GetShaderPassEnabled("Meta"), Is.EqualTo(mode.enableContributionPasses));
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
            Assert.That(queue, Is.Not.Null, "Material serialization has no m_CustomRenderQueue property.");
            return queue.intValue;
        }

        /// <summary>Asserts the serialized RenderType override separately from Unity's resolved shader tag.</summary>
        /// <param name="material">The material whose RenderType state is inspected.</param>
        /// <param name="mode">The expected rendering-mode state.</param>
        private static void AssertRenderTypeState(Material material, ModeContract mode)
        {
            bool hasOverride = TryGetSerializedRenderTypeOverride(material, out string renderTypeOverride);
            Assert.That(hasOverride, Is.EqualTo(mode.hasRenderTypeOverride), mode.name + " RenderType override presence.");
            if (hasOverride)
                Assert.That(renderTypeOverride, Is.EqualTo(mode.renderTypeOverride), mode.name + " RenderType override.");
            Assert.That(material.GetTag("RenderType", false), Is.EqualTo(mode.resolvedRenderType), mode.name + " resolved RenderType tag.");
        }

        /// <summary>Reads the raw RenderType override from Unity's serialized material tag map.</summary>
        /// <param name="material">The material whose serialized tag map is inspected.</param>
        /// <param name="renderTypeOverride">Receives the override value when it exists.</param>
        /// <returns>Whether the material serializes an explicit RenderType override.</returns>
        private static bool TryGetSerializedRenderTypeOverride(Material material, out string renderTypeOverride)
        {
            string serializedMaterial = EditorJsonUtility.ToJson(material);
            Match tagMap = Regex.Match(serializedMaterial, @"""stringTagMap""\s*:\s*\{(?<entries>[^}]*)\}");
            Assert.That(tagMap.Success, Is.True, "Material serialization has no stringTagMap object.");
            Match renderType = Regex.Match(tagMap.Groups["entries"].Value, @"""RenderType""\s*:\s*""(?<value>[^""]*)""");
            renderTypeOverride = renderType.Success ? renderType.Groups["value"].Value : null;
            return renderType.Success;
        }

        /// <summary>Asserts the local rendering-mode feature ABI in each required generated shader pass.</summary>
        /// <param name="source">The generated shader source.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        private static void AssertRenderingModeKeywordDeclarations(string source, string shaderName)
        {
            var declaredKeywords = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match declaration in Regex.Matches(source, @"^\s*#pragma\s+shader_feature_local\s+([^\r\n]+)", RegexOptions.Multiline))
            {
                foreach (Match keyword in Regex.Matches(declaration.Groups[1].Value, @"\bPUREBASE_RENDERING_[A-Z0-9_]+\b"))
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
                        "HLSLINCLUDE[\\s\\S]*?#pragma\\s+shader_feature_local\\s+(?:_\\s+)?PUREBASE_RENDERING_OPAQUE\\s+PUREBASE_RENDERING_TRANSPARENT[\\s\\S]*?ENDHLSL[\\s\\S]*?Name\\s+\\\"" + Regex.Escape(passName) + "\\\""
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
            public ProductContract(string shaderName, string propertySourcePath, string[] visiblePropertyNames)
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
            public ModeContract(int value, string name, int srcBlend, int dstBlend, int zWrite, int addSrcBlend, int addDstBlend, string renderTypeOverride, bool hasRenderTypeOverride, string resolvedRenderType, int rawQueue, int resolvedQueue, string[] enabledKeywords, bool enableContributionPasses)
            {
                this.value = value;
                this.name = name;
                this.srcBlend = srcBlend;
                this.dstBlend = dstBlend;
                this.zWrite = zWrite;
                this.addSrcBlend = addSrcBlend;
                this.addDstBlend = addDstBlend;
                this.renderTypeOverride = renderTypeOverride;
                this.hasRenderTypeOverride = hasRenderTypeOverride;
                this.resolvedRenderType = resolvedRenderType;
                this.rawQueue = rawQueue;
                this.resolvedQueue = resolvedQueue;
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

        /// <summary>Captures every material field whose mutation must be rejected by invalid normalizer inputs.</summary>
        private sealed class MaterialState
        {
            /// <summary>Captures an immutable snapshot from one material.</summary>
            /// <param name="material">The material to snapshot.</param>
            /// <param name="observedPropertyTypes">The optional set that records captured shader property types.</param>
            /// <returns>The captured state.</returns>
            public static MaterialState Capture(Material material, ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes = null)
            {
                var state = new MaterialState
                {
                    hasRenderTypeOverride = TryGetSerializedRenderTypeOverride(material, out string renderTypeOverride),
                    renderTypeOverride = renderTypeOverride,
                    resolvedRenderType = material.GetTag("RenderType", true),
                    rawQueue = GetRawRenderQueue(material),
                    resolvedQueue = material.renderQueue,
                    shadowCasterEnabled = material.GetShaderPassEnabled("ShadowCaster"),
                    metaEnabled = material.GetShaderPassEnabled("Meta"),
                    dirty = EditorUtility.IsDirty(material),
                    keywords = material.shaderKeywords,
                };
                Shader shader = material.shader;
                for (int index = 0; index < ShaderUtil.GetPropertyCount(shader); index++)
                {
                    string propertyName = shader.GetPropertyName(index);
                    ShaderUtil.ShaderPropertyType propertyType = ShaderUtil.GetPropertyType(shader, index);
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
                            state.textures[propertyName] = TexturePropertyState.Capture(material, propertyName);
                            break;
                        default:
                            Assert.Fail($"Unsupported shader property type '{ShaderUtil.GetPropertyType(shader, index)}' for '{propertyName}'.");
                            break;
                    }
                }
                foreach (string propertyName in HiddenStatePropertyNames)
                {
                    if (material.HasProperty(propertyName))
                        state.floats[propertyName] = material.GetFloat(propertyName);
                }

                foreach (string passName in PassNames)
                    state.passes[passName] = material.GetShaderPassEnabled(passName);
                return state;
            }

            /// <summary>Asserts that a material still matches this immutable snapshot.</summary>
            /// <param name="material">The material to compare.</param>
            /// <param name="context">The diagnostic operation context.</param>
            /// <param name="observedPropertyTypes">The optional set that records asserted shader property types.</param>
            public void AssertEqual(Material material, string context, ISet<ShaderUtil.ShaderPropertyType> observedPropertyTypes = null)
            {
                ObserveAtomicityPropertyTypes(material, observedPropertyTypes);
                bool actualHasRenderTypeOverride = TryGetSerializedRenderTypeOverride(material, out string actualRenderTypeOverride);
                Assert.That(actualHasRenderTypeOverride, Is.EqualTo(hasRenderTypeOverride), context + " RenderType override presence.");
                if (actualHasRenderTypeOverride)
                    Assert.That(actualRenderTypeOverride, Is.EqualTo(renderTypeOverride), context + " RenderType override.");
                Assert.That(material.GetTag("RenderType", true), Is.EqualTo(resolvedRenderType), context + " resolved RenderType tag.");
                Assert.That(GetRawRenderQueue(material), Is.EqualTo(rawQueue), context + " raw render queue.");
                Assert.That(material.renderQueue, Is.EqualTo(resolvedQueue), context + " resolved render queue.");
                Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.EqualTo(shadowCasterEnabled), context + " ShadowCaster state.");
                Assert.That(material.GetShaderPassEnabled("Meta"), Is.EqualTo(metaEnabled), context + " Meta state.");
                Assert.That(EditorUtility.IsDirty(material), Is.EqualTo(dirty), context + " dirty state.");
                CollectionAssert.AreEquivalent(keywords, material.shaderKeywords, context + " keyword set.");
                foreach (KeyValuePair<string, float> pair in floats)
                    Assert.That(material.GetFloat(pair.Key), Is.EqualTo(pair.Value), context + " property " + pair.Key + ".");
                foreach (KeyValuePair<string, int> pair in integers)
                {
                    int actual = material.GetInteger(pair.Key);
                    Assert.That(actual, Is.EqualTo(pair.Value), context + " int property " + pair.Key + ".");
                }
                foreach (KeyValuePair<string, Color> pair in colors)
                    Assert.That(material.GetColor(pair.Key), Is.EqualTo(pair.Value), context + " color property " + pair.Key + ".");
                foreach (KeyValuePair<string, Vector4> pair in vectors)
                    Assert.That(material.GetVector(pair.Key), Is.EqualTo(pair.Value), context + " vector property " + pair.Key + ".");
                foreach (KeyValuePair<string, TexturePropertyState> pair in textures)
                    pair.Value.AssertEqual(material, pair.Key, context);
                foreach (KeyValuePair<string, bool> pair in passes)
                    Assert.That(material.GetShaderPassEnabled(pair.Key), Is.EqualTo(pair.Value), context + " pass " + pair.Key + ".");
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
            public readonly Dictionary<string, float> floats = new Dictionary<string, float>(StringComparer.Ordinal);

            /// <summary>Stores captured integer property values.</summary>
            public readonly Dictionary<string, int> integers = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>Stores captured color property values.</summary>
            public readonly Dictionary<string, Color> colors = new Dictionary<string, Color>(StringComparer.Ordinal);

            /// <summary>Stores captured vector property values.</summary>
            public readonly Dictionary<string, Vector4> vectors = new Dictionary<string, Vector4>(StringComparer.Ordinal);

            /// <summary>Stores captured texture property values and their UV transforms.</summary>
            public readonly Dictionary<string, TexturePropertyState> textures = new Dictionary<string, TexturePropertyState>(StringComparer.Ordinal);

            /// <summary>Stores captured enabled-state values for every rendering-mode-relevant pass.</summary>
            public readonly Dictionary<string, bool> passes = new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        /// <summary>Returns valid materials during validation and snapshots, then makes one later target invalid during application.</summary>
        private sealed class LateInvalidatingMaterialList : IReadOnlyList<Material>
        {
            /// <summary>Initializes a deterministic material list that invalidates one target on its third indexed read.</summary>
            /// <param name="materials">The ordered batch materials.</param>
            /// <param name="invalidMaterialIndex">The later material index to invalidate.</param>
            /// <param name="invalidRenderingMode">The unsupported mode assigned immediately before its application.</param>
            public LateInvalidatingMaterialList(Material[] materials, int invalidMaterialIndex, int invalidRenderingMode)
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
                        ObservedPriorMutations = materials[0].GetTag("RenderType", false) == "Opaque"
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
                Assert.That(material.GetTexture(propertyName), Is.EqualTo(texture), context + " texture property " + propertyName + ".");
                Assert.That(material.GetTextureScale(propertyName), Is.EqualTo(scale), context + " texture scale " + propertyName + ".");
                Assert.That(material.GetTextureOffset(propertyName), Is.EqualTo(offset), context + " texture offset " + propertyName + ".");
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
