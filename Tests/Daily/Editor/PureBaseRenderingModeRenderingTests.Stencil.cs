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

// Defines explicit D24S8 runtime observations for product shader Stencil pass behavior.

using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    public sealed partial class PureBaseRenderingModeRenderingTests
    {
        /// <summary>Requires default Always and Keep Stencil state to render independently of the cleared Stencil value.</summary>
        [Test]
        public void D24S8StencilDefaultsRenderIndependentlyOfClearStencilAcrossProductShaders()
        {
            foreach (string shaderName in ProductShaderNames)
            {
                var fixture = new D24S8StencilFixture();
                try
                {
                    fixture.Initialize();
                    Shader shader = RequireProductShader(shaderName);
                    Color zeroClear = fixture.RenderSingle(
                        shader,
                        new Color(0.8f, 0.6f, 0.4f, 1.0f),
                        0,
                        new StencilState(
                            37,
                            255,
                            255,
                            CompareFunction.Always,
                            StencilOp.Keep,
                            StencilOp.Keep,
                            StencilOp.Keep
                        ),
                        0
                    );
                    Color nonzeroClear = fixture.RenderSingle(
                        shader,
                        new Color(0.8f, 0.6f, 0.4f, 1.0f),
                        203,
                        new StencilState(
                            37,
                            255,
                            255,
                            CompareFunction.Always,
                            StencilOp.Keep,
                            StencilOp.Keep,
                            StencilOp.Keep
                        ),
                        0
                    );

                    AssertFinite(
                        zeroClear,
                        shaderName + " default-Always clear-0 " + fixture.FormatDescription
                    );
                    AssertFinite(
                        nonzeroClear,
                        shaderName + " default-Always clear-203 " + fixture.FormatDescription
                    );
                    Assert.That(
                        RgbMagnitude(zeroClear),
                        Is.GreaterThan(0.05f),
                        shaderName
                            + " default Always+Keep must draw over clear stencil 0. "
                            + fixture.FormatDescription
                            + " Pixel="
                            + zeroClear
                    );
                    Assert.That(
                        RgbMagnitude(nonzeroClear),
                        Is.GreaterThan(0.05f),
                        shaderName
                            + " default Always+Keep must draw over clear stencil 203. "
                            + fixture.FormatDescription
                            + " Pixel="
                            + nonzeroClear
                    );
                    Assert.That(
                        RgbMagnitude(zeroClear - nonzeroClear),
                        Is.LessThan(0.02f),
                        shaderName
                            + " default Always+Keep must not depend on the cleared stencil value. "
                            + fixture.FormatDescription
                            + " Clear0="
                            + zeroClear
                            + " Clear203="
                            + nonzeroClear
                    );
                }
                finally
                {
                    fixture.Dispose();
                }
            }
        }

        /// <summary>Requires ForwardBase Replace to make matching Equal readers pass and mismatched readers reject across product shaders.</summary>
        [Test]
        public void D24S8StencilForwardBaseReplaceControlsEqualAndMismatchedReadersAcrossProductShaders()
        {
            var writerState = new StencilState(
                60,
                255,
                255,
                CompareFunction.Always,
                StencilOp.Replace,
                StencilOp.Keep,
                StencilOp.Keep
            );
            var matchingReaderState = new StencilState(
                60,
                255,
                0,
                CompareFunction.Equal,
                StencilOp.Keep,
                StencilOp.Keep,
                StencilOp.Keep
            );
            var mismatchedReaderState = new StencilState(
                61,
                255,
                0,
                CompareFunction.Equal,
                StencilOp.Keep,
                StencilOp.Keep,
                StencilOp.Keep
            );
            foreach (string shaderName in ProductShaderNames)
            {
                var fixture = new D24S8StencilFixture();
                try
                {
                    fixture.Initialize();
                    Shader shader = RequireProductShader(shaderName);
                    Color matchingWriter;
                    Color matching = fixture.RenderWriterThenReader(
                        shader,
                        0,
                        Color.black,
                        writerState,
                        new Color(0.7f, 0.8f, 0.9f, 1.0f),
                        matchingReaderState,
                        0,
                        out matchingWriter
                    );
                    Color mismatchedWriter;
                    Color mismatched = fixture.RenderWriterThenReader(
                        shader,
                        0,
                        Color.black,
                        writerState,
                        new Color(0.7f, 0.8f, 0.9f, 1.0f),
                        mismatchedReaderState,
                        0,
                        out mismatchedWriter
                    );

                    AssertFinite(
                        matching,
                        shaderName + " Replace/Equal matching reader " + fixture.FormatDescription
                    );
                    AssertFinite(
                        mismatched,
                        shaderName + " Replace/Equal mismatched reader " + fixture.FormatDescription
                    );
                    Assert.That(
                        RgbMagnitude(matching),
                        Is.GreaterThan(0.05f),
                        shaderName
                            + " Replace writer followed by Equal reader with ref 60 must render. "
                            + fixture.FormatDescription
                            + " Pixel="
                            + matching
                    );
                    Assert.That(
                        RgbMagnitude(mismatched - mismatchedWriter),
                        Is.LessThan(RgbMagnitude(matching - matchingWriter) * 0.2f),
                        shaderName
                            + " Replace writer followed by Equal reader with ref 61 must reject without adding reader color beyond the black-writer baseline. "
                            + fixture.FormatDescription
                            + " Matching="
                            + matching
                            + " MatchingWriter="
                            + matchingWriter
                            + " Mismatched="
                            + mismatched
                            + " MismatchedWriter="
                            + mismatchedWriter
                    );
                }
                finally
                {
                    fixture.Dispose();
                }
            }
        }

        /// <summary>Requires partial Stencil WriteMask and ReadMask behavior to preserve and compare only the selected low bits.</summary>
        [Test]
        public void D24S8StencilHonorsPartialReadAndWriteMasksAcrossProductShaders()
        {
            var writerState = new StencilState(
                0x12,
                255,
                0x0f,
                CompareFunction.Always,
                StencilOp.Replace,
                StencilOp.Keep,
                StencilOp.Keep
            );
            var matchingReaderState = new StencilState(
                0xf2,
                0x0f,
                0,
                CompareFunction.Equal,
                StencilOp.Keep,
                StencilOp.Keep,
                StencilOp.Keep
            );
            var mismatchedReaderState = new StencilState(
                0xf1,
                0x0f,
                0,
                CompareFunction.Equal,
                StencilOp.Keep,
                StencilOp.Keep,
                StencilOp.Keep
            );
            foreach (string shaderName in ProductShaderNames)
            {
                var fixture = new D24S8StencilFixture();
                try
                {
                    fixture.Initialize();
                    Shader shader = RequireProductShader(shaderName);
                    Color matchingWriter;
                    Color matching = fixture.RenderWriterThenReader(
                        shader,
                        0xa0,
                        Color.black,
                        writerState,
                        new Color(0.9f, 0.7f, 0.5f, 1.0f),
                        matchingReaderState,
                        0,
                        out matchingWriter
                    );
                    Color mismatchedWriter;
                    Color mismatched = fixture.RenderWriterThenReader(
                        shader,
                        0xa0,
                        Color.black,
                        writerState,
                        new Color(0.9f, 0.7f, 0.5f, 1.0f),
                        mismatchedReaderState,
                        0,
                        out mismatchedWriter
                    );

                    AssertFinite(
                        matching,
                        shaderName + " partial-mask matching reader " + fixture.FormatDescription
                    );
                    AssertFinite(
                        mismatched,
                        shaderName + " partial-mask mismatched reader " + fixture.FormatDescription
                    );
                    Assert.That(
                        RgbMagnitude(matching),
                        Is.GreaterThan(0.05f),
                        shaderName
                            + " WriteMask 0x0f must write low bits that ReadMask 0x0f can match. "
                            + fixture.FormatDescription
                            + " Pixel="
                            + matching
                    );
                    Assert.That(
                        RgbMagnitude(mismatched - mismatchedWriter),
                        Is.LessThan(RgbMagnitude(matching - matchingWriter) * 0.2f),
                        shaderName
                            + " ReadMask 0x0f must reject a mismatched low-bit reference without adding reader color beyond the black-writer baseline. "
                            + fixture.FormatDescription
                            + " Matching="
                            + matching
                            + " MatchingWriter="
                            + matchingWriter
                            + " Mismatched="
                            + mismatched
                            + " MismatchedWriter="
                            + mismatchedWriter
                    );
                }
                finally
                {
                    fixture.Dispose();
                }
            }
        }

        /// <summary>Requires Toon ForwardAdd to retain Equal+Keep contribution and reject add contribution after NotEqual+Replace mutates ForwardBase stencil.</summary>
        [Test]
        public void D24S8StencilToonForwardAddRecomparesPostBaseStencilWithoutWriting()
        {
            Shader toon = RequireProductShader("PureBase/Toon");
            var equalKeep = new StencilState(
                0,
                255,
                255,
                CompareFunction.Equal,
                StencilOp.Keep,
                StencilOp.Keep,
                StencilOp.Keep
            );
            var notEqualReplace = new StencilState(
                1,
                255,
                255,
                CompareFunction.NotEqual,
                StencilOp.Replace,
                StencilOp.Keep,
                StencilOp.Keep
            );
            var fixture = new ToonForwardAddScope();
            try
            {
                fixture.Initialize();
                var alwaysKeep = new StencilState(
                    0,
                    255,
                    255,
                    CompareFunction.Always,
                    StencilOp.Keep,
                    StencilOp.Keep,
                    StencilOp.Keep
                );
                Color alwaysKeepOneLight = fixture.RenderToonComposite(toon, 0, alwaysKeep, 1);
                AssertFinite(
                    alwaysKeepOneLight,
                    "Toon Always+Keep one-light " + fixture.FormatDescription
                );
                Assert.That(
                    RgbMagnitude(alwaysKeepOneLight),
                    Is.GreaterThan(0.05f),
                    "Toon Always+Keep transparent one-light control must render before testing ForwardAdd Stencil recompare. "
                        + fixture.FormatDescription
                        + " Pixel="
                        + alwaysKeepOneLight
                );
                Color equalKeepOneLight = fixture.RenderToonComposite(toon, 0, equalKeep, 1);
                Color equalKeepTwoLights = fixture.RenderToonComposite(toon, 0, equalKeep, 2);
                float retainedAddDelta = RgbMagnitude(equalKeepTwoLights - equalKeepOneLight);
                AssertFinite(
                    equalKeepOneLight,
                    "Toon Equal+Keep one-light " + fixture.FormatDescription
                );
                AssertFinite(
                    equalKeepTwoLights,
                    "Toon Equal+Keep two-light " + fixture.FormatDescription
                );
                Assert.That(
                    retainedAddDelta,
                    Is.GreaterThan(0.01f),
                    "Toon two-light control must expose a measurable ForwardAdd contribution before testing Stencil recompare. "
                        + fixture.FormatDescription
                        + " OneLight="
                        + equalKeepOneLight
                        + " TwoLights="
                        + equalKeepTwoLights
                        + " Delta="
                        + retainedAddDelta
                );
                Color notEqualReplaceOneLight = fixture.RenderToonComposite(
                    toon,
                    0,
                    notEqualReplace,
                    1
                );
                Color notEqualReplaceTwoLights = fixture.RenderToonComposite(
                    toon,
                    0,
                    notEqualReplace,
                    2
                );
                float rejectedAddDelta = RgbMagnitude(
                    notEqualReplaceTwoLights - notEqualReplaceOneLight
                );

                AssertFinite(
                    notEqualReplaceOneLight,
                    "Toon NotEqual+Replace one-light " + fixture.FormatDescription
                );
                AssertFinite(
                    notEqualReplaceTwoLights,
                    "Toon NotEqual+Replace two-light " + fixture.FormatDescription
                );
                Assert.That(
                    rejectedAddDelta,
                    Is.LessThan(retainedAddDelta * 0.2f),
                    "Toon NotEqual+Replace must make ForwardAdd recompare the post-ForwardBase value and reject its add contribution. "
                        + fixture.FormatDescription
                        + " OneLight="
                        + notEqualReplaceOneLight
                        + " TwoLights="
                        + notEqualReplaceTwoLights
                        + " RetainedDelta="
                        + retainedAddDelta
                        + " RejectedDelta="
                        + rejectedAddDelta
                );
                TestContext.Progress.WriteLine(
                    "Toon D24S8 controls: AlwaysKeepOneLight="
                        + alwaysKeepOneLight
                        + " EqualKeepOneLight="
                        + equalKeepOneLight
                        + " EqualKeepTwoLights="
                        + equalKeepTwoLights
                        + " NotEqualReplaceOneLight="
                        + notEqualReplaceOneLight
                        + " NotEqualReplaceTwoLights="
                        + notEqualReplaceTwoLights
                        + " RetainedAddDelta="
                        + retainedAddDelta
                        + " RejectedAddDelta="
                        + rejectedAddDelta
                );
            }
            finally
            {
                fixture.Dispose();
            }
        }

        /// <summary>Stores all user-controlled Stencil state values for one material draw.</summary>
        private sealed class StencilState
        {
            /// <summary>Initializes one complete material Stencil state.</summary>
            /// <param name="referenceValue">The Stencil reference value.</param>
            /// <param name="readMask">The Stencil read mask.</param>
            /// <param name="writeMask">The Stencil write mask.</param>
            /// <param name="comparison">The Stencil comparison function.</param>
            /// <param name="passOperation">The operation when Stencil and depth tests pass.</param>
            /// <param name="failOperation">The operation when the Stencil test fails.</param>
            /// <param name="depthFailOperation">The operation when the depth test fails.</param>
            public StencilState(
                byte referenceValue,
                byte readMask,
                byte writeMask,
                CompareFunction comparison,
                StencilOp passOperation,
                StencilOp failOperation,
                StencilOp depthFailOperation
            )
            {
                this.referenceValue = referenceValue;
                this.readMask = readMask;
                this.writeMask = writeMask;
                this.comparison = comparison;
                this.passOperation = passOperation;
                this.failOperation = failOperation;
                this.depthFailOperation = depthFailOperation;
            }

            /// <summary>Stores the reference value used by Stencil comparison and writes.</summary>
            public readonly byte referenceValue;

            /// <summary>Stores the mask applied before Stencil comparison.</summary>
            public readonly byte readMask;

            /// <summary>Stores the mask applied to a Stencil write operation.</summary>
            public readonly byte writeMask;

            /// <summary>Stores the Stencil comparison function.</summary>
            public readonly CompareFunction comparison;

            /// <summary>Stores the Stencil operation when tests pass.</summary>
            public readonly StencilOp passOperation;

            /// <summary>Stores the Stencil operation when the Stencil test fails.</summary>
            public readonly StencilOp failOperation;

            /// <summary>Stores the Stencil operation when the depth test fails.</summary>
            public readonly StencilOp depthFailOperation;
        }
    }
}
