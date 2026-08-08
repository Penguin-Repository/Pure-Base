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

// Seeds the cold-import consumer expectation for the rendering-mode postpixel alpha probe without referencing unimplemented Editor types.

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PureBase.Release.Consumer.Tests
{
    /// <summary>Defines cold-consumer rendering-mode and postpixel-alpha contracts before the package implementation exists.</summary>
    public sealed class PureBaseConsumerRenderingModeTests
    {
        /// <summary>Identifies the only release module selected by the postpixel alpha consumer invocation.</summary>
        private const string PostPixelAlphaProbeId = "jp.penguin.purebase.release.renderingmode.postpixel-alpha";

        /// <summary>Lists every cold-imported public product expected to support explicit material normalization.</summary>
        private static readonly string[] ProductShaderNames =
        {
            "PureBase/Unlit",
            "PureBase/Toon",
            "PureBase/PBR",
            "PureBase/Hybrid",
        };

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
            string generatedSource = ConsumerValidationSupport.LoadGeneratedSource(contract.products[0], contract.runLabel);
            StringAssert.Contains("sd.col.a = half(0.25)", generatedSource);
        }

        /// <summary>Requires the installed public normalizer through reflection so this consumer assembly remains compile-safe before it is shipped.</summary>
        [Test]
        public void ColdImportedPackageExposesThePublicRenderingModeNormalizer()
        {
            Type type = FindLoadedType("PureBase.Editor.PureBaseMaterialRenderingMode");
            Assert.That(type, Is.Not.Null, "The cold-imported package must load PureBaseMaterialRenderingMode.");
            MethodInfo apply = type.GetMethod(
                "Apply",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Material) },
                null
            );
            Assert.That(apply, Is.Not.Null, "The cold-imported package must expose public Apply(Material).");
        }

        /// <summary>Requires cold-imported public shaders to normalize each declared mode only through the reflected package API.</summary>
        [Test]
        public void ColdImportedPublicNormalizerAcceptsEveryProductAndDeclaredMode()
        {
            Type type = FindLoadedType("PureBase.Editor.PureBaseMaterialRenderingMode");
            Assert.That(type, Is.Not.Null, "The cold-imported package must load PureBaseMaterialRenderingMode.");
            MethodInfo apply = type.GetMethod(
                "Apply",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Material) },
                null
            );
            Assert.That(apply, Is.Not.Null, "The cold-imported package must expose public Apply(Material).");

            foreach (string shaderName in ProductShaderNames)
            {
                Shader shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, "The cold-imported package did not expose " + shaderName + ".");
                var material = new Material(shader);
                try
                {
                    Assert.That(material.HasProperty("_RenderingMode"), Is.True, shaderName + " must expose _RenderingMode.");
                    foreach (int mode in new[] { 0, 1, 2 })
                    {
                        material.SetInteger("_RenderingMode", mode);
                        apply.Invoke(null, new object[] { material });
                        Assert.That(material.GetInteger("_RenderingMode"), Is.EqualTo(mode), shaderName + " normalized mode value.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }

        /// <summary>Finds a loaded type without a consumer-assembly dependency on the future PureBase.Editor assembly definition.</summary>
        /// <param name="fullName">The fully-qualified type name.</param>
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
    }
}
