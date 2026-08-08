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

// Defines product shader, public API, and explicit state-table contracts for rendering modes.

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
        private readonly List<Material> transientMaterials = new List<Material>();

        /// <summary>Tracks transient texture sentinels used to make invalid-input atomicity snapshots discriminating.</summary>
        private readonly List<Texture> transientTextures = new List<Texture>();

        /// <summary>Identifies the package-local root used only by persistence tests.</summary>
        private const string TemporaryAssetRoot = "Assets/PureBaseRenderingModeTests";

        /// <summary>Identifies the pre-rendering-mode material fixture that must remain byte-identical.</summary>
        private const string LegacyFixturePath =
            "Packages/jp.penguin.purebase/Tests/Fixtures/Materials/PureBaseLegacyCutout.mat";

        /// <summary>Identifies the deterministic non-Pure-Base shader fixture used for unsupported-ownership and atomicity coverage.</summary>
        private const string UnsupportedRenderingModeFixturePath =
            "Packages/jp.penguin.purebase/Tests/Fixtures/RenderingMode/PureBaseUnsupportedRenderingMode.shader";

        /// <summary>Matches the required Shader-Core property declaration without relying on reflection metadata.</summary>
        private const string RenderingModePropertySourcePattern =
            @"SC_uint\s*\(\s*_RenderingMode\s*,\s*1(?:\.0+)?\s*,\s*\[\s*PureBaseRenderingMode\s*\]\s*,\s*""[^""\r\n]*""\s*,\s*""[^""\r\n]*""\s*\)";

        /// <summary>Matches the required Cutoff declaration with its Pure-Base drawer and stable range bounds.</summary>
        private const string CutoffPropertySourcePattern =
            @"SC_float\s*\(\s*_Cutoff\s*,\s*0\.5(?:0+)?\s*,\s*\[\s*PureBaseCutoff\s*\]\s*\[\s*SCRange\s*\(\s*-0\.001\s*,\s*1\.001\s*\)\s*\]\s*,\s*""Cutoff""\s*,\s*""""\s*\)";

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
                new BlendState((int)BlendMode.One, (int)BlendMode.Zero, 1, (int)BlendMode.One, (int)BlendMode.One),
                new RenderTypeState("Opaque", true, "Opaque"),
                new QueueState(2000, 2000),
                new[] { "PUREBASE_RENDERING_OPAQUE" },
                true
            ),
            new ModeContract(
                1,
                "Cutout",
                new BlendState((int)BlendMode.One, (int)BlendMode.Zero, 1, (int)BlendMode.One, (int)BlendMode.One),
                new RenderTypeState(string.Empty, false, "TransparentCutout"),
                new QueueState(-1, (int)RenderQueue.AlphaTest),
                Array.Empty<string>(),
                true
            ),
            new ModeContract(
                2,
                "Transparent",
                new BlendState((int)BlendMode.SrcAlpha, (int)BlendMode.OneMinusSrcAlpha, 0, (int)BlendMode.SrcAlpha, (int)BlendMode.One),
                new RenderTypeState("Transparent", true, "Transparent"),
                new QueueState(3000, 3000),
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
                AssertProductShaderAbi(product, shader);
                AssertProductShaderStaticDefaults(product, shader);
            }
        }

        /// <summary>Asserts the visible-property ABI and rendering-mode property declarations for one product shader.</summary>
        /// <param name="product">The expected product shader contract.</param>
        /// <param name="shader">The imported product shader.</param>
        private static void AssertProductShaderAbi(ProductContract product, Shader shader)
        {
            CollectionAssert.AreEqual(product.visiblePropertyNames, GetVisiblePropertyNames(shader), $"Product shader '{product.shaderName}' changed its public property ABI.");
            int modeIndex = shader.FindPropertyIndex("_RenderingMode");
            Assert.That(modeIndex, Is.GreaterThanOrEqualTo(0), $"Product shader '{product.shaderName}' must expose _RenderingMode.");
            Assert.That(shader.GetPropertyType(modeIndex), Is.EqualTo(ShaderPropertyType.Int), $"Product shader '{product.shaderName}' must expose _RenderingMode as an Integer property.");
            CollectionAssert.Contains(shader.GetPropertyAttributes(modeIndex), "PureBaseRenderingMode", $"Product shader '{product.shaderName}' must use the Pure-Base rendering-mode drawer.");
            Assert.That(Regex.IsMatch(File.ReadAllText(product.propertySourcePath), RenderingModePropertySourcePattern), Is.True, $"Product property source '{product.propertySourcePath}' must declare _RenderingMode as SC_uint with default 1 and the PureBaseRenderingMode drawer.");
            int cutoffIndex = shader.FindPropertyIndex("_Cutoff");
            Assert.That(cutoffIndex, Is.GreaterThanOrEqualTo(0), $"Product shader '{product.shaderName}' must expose _Cutoff.");
            CollectionAssert.Contains(shader.GetPropertyAttributes(cutoffIndex), "PureBaseCutoff", $"Product shader '{product.shaderName}' must use the Pure-Base Cutoff drawer.");
            Assert.That(Regex.IsMatch(File.ReadAllText(product.propertySourcePath), CutoffPropertySourcePattern), Is.True, $"Product property source '{product.propertySourcePath}' must declare _Cutoff with the PureBaseCutoff drawer and SCRange(-0.001,1.001).");
        }

        /// <summary>Asserts the static Cutout defaults, pass ABI, and keyword declarations for one product shader.</summary>
        /// <param name="product">The expected product shader contract.</param>
        /// <param name="shader">The imported product shader.</param>
        private void AssertProductShaderStaticDefaults(ProductContract product, Shader shader)
        {
            var material = CreateMaterial(shader);
            Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(1));
            AssertHiddenState(material, Modes[1]);
            Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
            Assert.That(material.GetTag("RenderType", false), Is.EqualTo("TransparentCutout"));
            Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.True);
            Assert.That(material.GetShaderPassEnabled("Meta"), Is.True);
            AssertRenderingKeywords(material, Array.Empty<string>());
            CollectionAssert.AreEqual(PassNames, GetPassNames(shader));
            AssertRenderingModeKeywordDeclarations(LoadGeneratedSource(product.shaderName), product.shaderName);
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

    }
}
