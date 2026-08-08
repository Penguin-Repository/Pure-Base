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

// Validates the shipped rendering-mode ABI, material state table, and postpixel alpha release probe.

using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PureBase.Editor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Release.Consumer.Tests
{
    /// <summary>Defines cold-consumer rendering-mode and postpixel-alpha contracts before the package implementation exists.</summary>
    public sealed class PureBaseConsumerRenderingModeTests
    {
        /// <summary>Identifies the only release module selected by the postpixel alpha consumer invocation.</summary>
        private const string PostPixelAlphaProbeId = "jp.penguin.purebase.release.renderingmode.postpixel-alpha";

        /// <summary>Lists every local keyword owned by the rendering-mode contract.</summary>
        private static readonly string[] RenderingModeKeywords =
        {
            "PUREBASE_RENDERING_OPAQUE",
            "PUREBASE_RENDERING_TRANSPARENT",
        };

        /// <summary>Lists every hidden state property synchronized by the public normalizer.</summary>
        private static readonly string[] HiddenStatePropertyNames =
        {
            "_SrcBlend",
            "_DstBlend",
            "_ZWrite",
            "_AddSrcBlend",
            "_AddDstBlend",
        };

        /// <summary>Lists the four declared source passes retained regardless of material contribution state.</summary>
        private static readonly string[] SourcePassNames =
        {
            "ForwardBase",
            "ForwardAdd",
            "ShadowCaster",
            "Meta",
        };

        /// <summary>Matches the source declaration for the public integer rendering-mode selector.</summary>
        private const string RenderingModePropertySourcePattern =
            @"SC_uint\s*\(\s*_RenderingMode\s*,\s*1(?:\.0+)?\s*,\s*\[\s*PureBaseRenderingMode\s*\]\s*,\s*""[^""\r\n]*""\s*,\s*""[^""\r\n]*""\s*\)";

        /// <summary>Matches the generated ForwardBase fragment function declaration.</summary>
        private const string FragmentFunctionDeclarationPattern =
            @"(?m)^[ \t]*(?:half|float|fixed)[1-4]?\s+frag\s*\(";

        /// <summary>Requires the dedicated cold-import invocation to select the alpha probe for Transparent Toon observations.</summary>
        [Test]
        public void PostPixelAlphaConsumerInvocationSelectsTheTransparentToonProbeContract()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            Assert.That(contract.runKind, Is.EqualTo("product-phase"));
            Assert.That(contract.hasSelectedModule, Is.True);
            Assert.That(contract.selectedModule, Is.Not.Null);
            Assert.That(contract.selectedModule.phase, Is.EqualTo("postpixel"));
            Assert.That(contract.selectedModule.moduleUniqueId, Is.EqualTo(PostPixelAlphaProbeId));
            Assert.That(contract.products, Is.Not.Null.And.Length.EqualTo(1));
            Assert.That(contract.products[0].shaderName, Is.EqualTo("PureBase/Toon"));
            Shader shader = ConsumerValidationSupport.ImportProductShader(
                contract.products[0],
                contract.runLabel
            );
            CollectionAssert.AreEqual(SourcePassNames, ConsumerValidationSupport.GetPassNames(shader));
            string generatedSource = ConsumerValidationSupport.LoadGeneratedSource(contract.products[0], contract.runLabel);
            PureBaseConsumerModuleFreeImportTests.AssertGlobalFragments(
                contract,
                contract.products[0],
                generatedSource
            );
            PureBaseConsumerModuleFreeImportTests.AssertPassContracts(
                contract,
                contract.products[0],
                generatedSource,
                false
            );
            string forwardBaseSource = ConsumerValidationSupport.GetPassSource(
                generatedSource,
                "ForwardBase",
                "ForwardAdd",
                contract.runLabel,
                contract.products[0].shaderName
            );
            string fragmentBody = GetFragmentBody(
                forwardBaseSource,
                contract.runLabel,
                contract.products[0].shaderName
            );
            Match modeAlphaOperation = Regex.Match(
                fragmentBody,
                @"\bPureBaseApplyRenderingModeOutputAlpha\s*\("
            );
            Match alphaProbeMatch = Regex.Match(
                fragmentBody,
                @"\bsd\.col\.a\s*=\s*half\s*\(\s*0\.25\s*\)\s*;"
            );
            Assert.That(
                alphaProbeMatch.Success,
                Is.True,
                "The ForwardBase fragment must contain the transparent toon alpha probe contract."
            );
            int alphaProbe = alphaProbeMatch.Index;
            Match returnStatement = Regex.Match(
                fragmentBody.Substring(alphaProbe),
                @"\breturn\b"
            );
            Assert.That(modeAlphaOperation.Success, Is.True);
            Assert.That(alphaProbe, Is.GreaterThan(modeAlphaOperation.Index));
            Assert.That(returnStatement.Success, Is.True);
            Assert.That(
                alphaProbe + returnStatement.Index,
                Is.GreaterThan(alphaProbe),
                "The postpixel alpha probe must execute before the fragment return."
            );
        }

        /// <summary>Returns the body of the generated ForwardBase fragment function without imported helper declarations.</summary>
        /// <param name="passSource">The generated ForwardBase pass source.</param>
        /// <param name="runLabel">The current consumer invocation label.</param>
        /// <param name="shaderName">The public shader name used in diagnostics.</param>
        /// <returns>The text between the fragment function's outer braces.</returns>
        private static string GetFragmentBody(string passSource, string runLabel, string shaderName)
        {
            Match declaration = Regex.Match(passSource, FragmentFunctionDeclarationPattern);
            Assert.That(
                declaration.Success,
                Is.True,
                $"Consumer run '{runLabel}' product '{shaderName}' did not contain a generated ForwardBase frag function."
            );
            int openingBrace = passSource.IndexOf(
                '{',
                declaration.Index + declaration.Length
            );
            Assert.That(
                openingBrace,
                Is.GreaterThanOrEqualTo(0),
                $"Consumer run '{runLabel}' product '{shaderName}' generated frag function has no opening brace."
            );

            int braceDepth = 1;
            for (int index = openingBrace + 1; index < passSource.Length; index++)
            {
                if (passSource[index] == '{')
                {
                    braceDepth++;
                }
                else if (passSource[index] == '}' && --braceDepth == 0)
                {
                    return passSource.Substring(openingBrace + 1, index - openingBrace - 1);
                }
            }

            Assert.Fail(
                $"Consumer run '{runLabel}' product '{shaderName}' generated frag function has no closing brace."
            );
            return string.Empty;
        }

        /// <summary>Requires every public shader to implement the complete rendering-mode ABI and state table through the shipped Editor assembly.</summary>
        [Test]
        public void ColdImportedPublicNormalizerMatchesTheFourByThreeStateTable()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            Assert.That(contract.runKind, Is.EqualTo("module-free"));
            Assert.That(contract.hasSelectedModule, Is.False);
            PureBaseConsumerModuleFreeImportTests.AssertRequiredProductSet(contract);

            foreach (ConsumerProductContract product in contract.products)
            {
                Shader shader = ConsumerValidationSupport.ImportProductShader(product, contract.runLabel);
                AssertRenderingModeAbi(product, shader, contract.runLabel);
                var material = new Material(shader);
                try
                {
                    AssertCutoutDefaults(material, product.shaderName);
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        material.SetInteger("_RenderingMode", mode);
                        PureBaseMaterialRenderingMode.Apply(material);
                        AssertModeState(material, product.shaderName, mode);
                    }

                    AssertInvalidModeIsAtomic(material, product.shaderName, -1);
                    AssertInvalidModeIsAtomic(material, product.shaderName, 3);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>Checks the visible integer selector, hidden state fields, local keywords, declared passes, and source declaration for one product.</summary>
        /// <param name="product">The runner-provided product contract.</param>
        /// <param name="shader">The imported public shader.</param>
        /// <param name="runLabel">The current consumer invocation label.</param>
        private static void AssertRenderingModeAbi(
            ConsumerProductContract product,
            Shader shader,
            string runLabel
        )
        {
            CollectionAssert.AreEqual(SourcePassNames, ConsumerValidationSupport.GetPassNames(shader));
            CollectionAssert.Contains(
                ConsumerValidationSupport.GetVisiblePropertyNames(shader),
                "_RenderingMode"
            );
            int modeIndex = shader.FindPropertyIndex("_RenderingMode");
            Assert.That(modeIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(shader.GetPropertyType(modeIndex), Is.EqualTo(ShaderPropertyType.Int));
            CollectionAssert.Contains(shader.GetPropertyAttributes(modeIndex), "PureBaseRenderingMode");
            foreach (string propertyName in HiddenStatePropertyNames)
            {
                Assert.That(shader.FindPropertyIndex(propertyName), Is.GreaterThanOrEqualTo(0));
                CollectionAssert.DoesNotContain(
                    ConsumerValidationSupport.GetVisiblePropertyNames(shader),
                    propertyName
                );
            }

            string generatedSource = ConsumerValidationSupport.LoadGeneratedSource(product, runLabel);
            StringAssert.Contains(
                "#pragma shader_feature_local _ PUREBASE_RENDERING_OPAQUE PUREBASE_RENDERING_TRANSPARENT",
                generatedSource
            );
            Assert.That(
                generatedSource.IndexOf("PUREBASE_RENDERING_CUTOUT", StringComparison.Ordinal),
                Is.LessThan(0),
                product.shaderName + " must keep Cutout keyword-free."
            );
            string propertySourcePath = Path.ChangeExtension(product.shaderAssetPath, null)
                + "_properties.hlsl";
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string propertySource = File.ReadAllText(
                Path.Combine(projectRoot, propertySourcePath.Replace('/', Path.DirectorySeparatorChar))
            );
            Assert.That(
                Regex.IsMatch(propertySource, RenderingModePropertySourcePattern),
                Is.True,
                product.shaderName + " must declare _RenderingMode through SC_uint with default 1."
            );
        }

        /// <summary>Checks the static Cutout-compatible default state before an explicit normalization mutates the material.</summary>
        /// <param name="material">The new transient material.</param>
        /// <param name="shaderName">The material's public shader name.</param>
        private static void AssertCutoutDefaults(Material material, string shaderName)
        {
            Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(1));
            AssertModeState(material, shaderName, 1);
        }

        /// <summary>Checks all derived state fields for one supported material mode without conflating source pass presence with material pass enablement.</summary>
        /// <param name="material">The normalized transient material.</param>
        /// <param name="shaderName">The material's public shader name.</param>
        /// <param name="mode">The public rendering-mode value.</param>
        private static void AssertModeState(Material material, string shaderName, int mode)
        {
            Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(mode), shaderName + " rendering mode.");
            int sourceBlend;
            int destinationBlend;
            int depthWrite;
            int additiveSourceBlend;
            int additiveDestinationBlend;
            string renderType;
            int renderQueue;
            bool opaqueKeyword;
            bool transparentKeyword;
            bool contributionPasses;
            switch (mode)
            {
                case 0:
                    sourceBlend = (int)BlendMode.One;
                    destinationBlend = (int)BlendMode.Zero;
                    depthWrite = 1;
                    additiveSourceBlend = (int)BlendMode.One;
                    additiveDestinationBlend = (int)BlendMode.One;
                    renderType = "Opaque";
                    renderQueue = 2000;
                    opaqueKeyword = true;
                    transparentKeyword = false;
                    contributionPasses = true;
                    break;
                case 1:
                    sourceBlend = (int)BlendMode.One;
                    destinationBlend = (int)BlendMode.Zero;
                    depthWrite = 1;
                    additiveSourceBlend = (int)BlendMode.One;
                    additiveDestinationBlend = (int)BlendMode.One;
                    renderType = "TransparentCutout";
                    renderQueue = (int)RenderQueue.AlphaTest;
                    opaqueKeyword = false;
                    transparentKeyword = false;
                    contributionPasses = true;
                    break;
                case 2:
                    sourceBlend = (int)BlendMode.SrcAlpha;
                    destinationBlend = (int)BlendMode.OneMinusSrcAlpha;
                    depthWrite = 0;
                    additiveSourceBlend = (int)BlendMode.SrcAlpha;
                    additiveDestinationBlend = (int)BlendMode.One;
                    renderType = "Transparent";
                    renderQueue = 3000;
                    opaqueKeyword = false;
                    transparentKeyword = true;
                    contributionPasses = false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }

            Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)sourceBlend));
            Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)destinationBlend));
            Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo((float)depthWrite));
            Assert.That(material.GetFloat("_AddSrcBlend"), Is.EqualTo((float)additiveSourceBlend));
            Assert.That(material.GetFloat("_AddDstBlend"), Is.EqualTo((float)additiveDestinationBlend));
            Assert.That(material.GetTag("RenderType", false), Is.EqualTo(renderType));
            Assert.That(material.renderQueue, Is.EqualTo(renderQueue));
            Assert.That(material.IsKeywordEnabled(RenderingModeKeywords[0]), Is.EqualTo(opaqueKeyword));
            Assert.That(material.IsKeywordEnabled(RenderingModeKeywords[1]), Is.EqualTo(transparentKeyword));
            Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.EqualTo(contributionPasses));
            Assert.That(material.GetShaderPassEnabled("Meta"), Is.EqualTo(contributionPasses));
        }

        /// <summary>Requires invalid public mode values to leave all derived state from the prior valid mode unchanged.</summary>
        /// <param name="material">The reusable transient material.</param>
        /// <param name="shaderName">The material's public shader name.</param>
        /// <param name="invalidMode">The unsupported public mode value.</param>
        private static void AssertInvalidModeIsAtomic(Material material, string shaderName, int invalidMode)
        {
            material.SetInteger("_RenderingMode", 0);
            PureBaseMaterialRenderingMode.Apply(material);
            material.SetInteger("_RenderingMode", invalidMode);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => PureBaseMaterialRenderingMode.Apply(material)
            );
            Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(invalidMode));
            Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.One));
            Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.Zero));
            Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(1.0f));
            Assert.That(material.GetFloat("_AddSrcBlend"), Is.EqualTo((float)BlendMode.One));
            Assert.That(material.GetFloat("_AddDstBlend"), Is.EqualTo((float)BlendMode.One));
            Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Opaque"));
            Assert.That(material.renderQueue, Is.EqualTo(2000));
            Assert.That(material.IsKeywordEnabled(RenderingModeKeywords[0]), Is.True);
            Assert.That(material.IsKeywordEnabled(RenderingModeKeywords[1]), Is.False);
            Assert.That(material.GetShaderPassEnabled("ShadowCaster"), Is.True);
            Assert.That(material.GetShaderPassEnabled("Meta"), Is.True, shaderName + " invalid mode must not disable Meta.");
        }
    }
}
