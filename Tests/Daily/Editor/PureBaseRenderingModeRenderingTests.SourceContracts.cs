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

// Defines source-order contracts for the rendering-mode BIRP integration.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PureBase.Tests.Daily
{
    public sealed partial class PureBaseRenderingModeRenderingTests
    {
        /// <summary>Identifies the common BIRP fragment host whose ordering is part of the generated-source ABI.</summary>
        private const string BirpHostPath =
            "Packages/jp.penguin.purebase/Shaders/Common/birp_host.hlsl";

        /// <summary>Identifies the Toon model source whose direct and environment callback ownership is diagnosed.</summary>
        private const string ToonModelPath =
            "Packages/jp.penguin.purebase/Shaders/Models/toon.hlsl";

        /// <summary>Identifies the fixed Toon shadow host whose importer-valid source layout is part of the test contract.</summary>
        private const string ToonShadowFixturePath =
            "Packages/jp.penguin.purebase/Tests/Fixtures/Hosts/ToonShadow/PureBaseTestToonShadow.scshader";

        /// <summary>Identifies the Toon-only helper that owns dominant-direction and two-band SH evaluation.</summary>
        private const string ToonLightingHelperPath =
            "Packages/jp.penguin.purebase/Shaders/Common/toon_lighting.hlsl";

        /// <summary>Identifies the future Pure Base-owned helper for non-shadow BIRP attenuation.</summary>
        private const string BirpLightAttenuationHelperPath =
            "Packages/jp.penguin.purebase/Shaders/Common/birp_light_attenuation.hlsl";

        /// <summary>Identifies the Unlit model that must retain its existing light and shadow ownership.</summary>
        private const string UnlitModelPath =
            "Packages/jp.penguin.purebase/Shaders/Models/unlit.hlsl";

        /// <summary>Identifies the PBR model that must not consume Toon lighting direction or environment bands.</summary>
        private const string PbrModelPath =
            "Packages/jp.penguin.purebase/Shaders/Models/pbr.hlsl";

        /// <summary>Identifies the Hybrid model wrapper that must retain PBR lighting ownership.</summary>
        private const string HybridModelPath =
            "Packages/jp.penguin.purebase/Shaders/Models/hybrid.hlsl";

        /// <summary>Identifies the shared PBR BRDF source that retains Hybrid's inline binary diffuse factor.</summary>
        private const string PbrBrdfPath =
            "Packages/jp.penguin.purebase/Shaders/Common/pbr_brdf.hlsl";

        /// <summary>Identifies the Shader-Core BIRP light acquisition boundary that retains lightmap ownership.</summary>
        private const string ShaderCoreBirpLightingPath =
            "Packages/jp.lilxyzw.shadercore/ShaderLibrary/birp_lighting.hlsl";

        /// <summary>Identifies the shared rendering-mode helper that owns mode clip and output-alpha semantics.</summary>
        private const string RenderingModeHelperPath =
            "Packages/jp.penguin.purebase/Shaders/Common/rendering_mode.hlsl";

        /// <summary>Identifies the shared operation that publishes the mode-specific output alpha.</summary>
        private const string RenderingModeOutputAlphaOperation =
            "PureBaseApplyRenderingModeOutputAlpha";

        /// <summary>Identifies the rendering-mode keyword whose output alpha preserves coverage.</summary>
        private const string TransparentRenderingModeKeyword = "PUREBASE_RENDERING_TRANSPARENT";

        /// <summary>Identifies the sole allowed generated rendering-mode variant declaration.</summary>
        private const string ExpectedRenderingModeVariantDeclaration =
            "#pragma shader_feature_local _ PUREBASE_RENDERING_OPAQUE PUREBASE_RENDERING_TRANSPARENT";

        /// <summary>Identifies the built-in Scene View variant retained by generated product sources.</summary>
        private const string ExpectedEditorVisualizationVariantDeclaration =
            "#pragma shader_feature EDITOR_VISUALIZATION";

        /// <summary>Lists the product shaders whose generated BIRP source shares the Stencil pass policy.</summary>
        private static readonly string[] ProductShaderNames =
        {
            "PureBase/Unlit",
            "PureBase/Toon",
            "PureBase/PBR",
            "PureBase/Hybrid",
        };

        /// <summary>Lists the exact pass ABI retained by every product generated source.</summary>
        private static readonly string[] ExpectedPassNames =
        {
            "ForwardBase",
            "ForwardAdd",
            "ShadowCaster",
            "Meta",
        };

        /// <summary>Identifies the release-only postpixel alpha probe source.</summary>
        private const string PostPixelProbePath =
            "Packages/jp.penguin.purebase/Tests/Release/Modules/Standard/PostPixel/phase_postpixel.hlsl";

        /// <summary>Requires the shared mode-alpha helper to run after add and before fog, postpixel, and return.</summary>
        [Test]
        public void BirpHostPreservesModeAlphaFogPostPixelAndForwardAddSourceOrder()
        {
            string host = File.ReadAllText(BirpHostPath);
            string renderingModeHelper = File.ReadAllText(RenderingModeHelperPath);
            int addPhase = RequireIndex(host, "__SC_PHASE_add__");
            Match modeOutputAlphaCall = Regex.Match(
                host,
                @"\b" + Regex.Escape(RenderingModeOutputAlphaOperation) + @"\s*\("
            );
            Assert.That(
                modeOutputAlphaCall.Success,
                Is.True,
                "The BIRP host must call the shared rendering-mode output-alpha operation."
            );
            int modeOutputAlpha = modeOutputAlphaCall.Index;
            int fog = RequireIndex(host, "UNITY_APPLY_FOG");
            int postPixel = RequireIndex(host, "__SC_PHASE_postpixel__");
            int returnStatement = RequireIndex(host, "return sd.col;");
            StringAssert.Contains(
                "#include \"Packages/jp.penguin.purebase/Shaders/Common/rendering_mode.hlsl\"",
                host
            );
            Assert.That(
                modeOutputAlpha,
                Is.GreaterThan(addPhase),
                "The shared mode-alpha helper must run after the add phase."
            );
            Assert.That(
                modeOutputAlpha,
                Is.LessThan(fog),
                "The shared mode-alpha helper must run before fog."
            );
            Assert.That(fog, Is.LessThan(postPixel), "Fog must occur before postpixel.");
            Assert.That(
                postPixel,
                Is.LessThan(returnStatement),
                "Postpixel must remain the final color mutation point before return."
            );
            StringAssert.Contains(RenderingModeOutputAlphaOperation, renderingModeHelper);
            StringAssert.Contains(TransparentRenderingModeKeyword, renderingModeHelper);
            StringAssert.Contains("coverage", renderingModeHelper);
            StringAssert.Contains(".a", renderingModeHelper);
            Assert.That(
                Regex.IsMatch(renderingModeHelper, @"\b1(?:\.0+)?\b"),
                Is.True,
                "The shared helper must distinguish Transparent coverage alpha from Opaque and Cutout alpha one."
            );
            string generatedProductSource = LoadProductSource("PureBase/Toon");
            Assert.That(
                Regex.IsMatch(
                    generatedProductSource,
                    @"\b" + Regex.Escape(RenderingModeOutputAlphaOperation) + @"\s*\("
                ),
                Is.True,
                "The generated product source must retain the shared rendering-mode output-alpha operation."
            );
            StringAssert.Contains("Blend [_AddSrcBlend] [_AddDstBlend]", generatedProductSource);
            StringAssert.Contains("ColorMask RGB", generatedProductSource);
            StringAssert.Contains("sd.col.a = half(0.25)", File.ReadAllText(PostPixelProbePath));
        }

        /// <summary>Requires Toon-owned binary direct and two-band environment lighting with Shader-Core lightmap and ForwardAdd isolation.</summary>
        [Test]
        public void ToonLightingOwnershipKeepsBinaryDirectTwoBandShaderCoreLightmapsAndForwardAddIsolation()
        {
            string toon = File.ReadAllText(ToonModelPath);
            string helper = File.ReadAllText(ToonLightingHelperPath);
            string host = File.ReadAllText(BirpHostPath);
            string shaderCoreLighting = File.ReadAllText(ShaderCoreBirpLightingPath);
            string pbr = File.ReadAllText(PbrModelPath);
            string pbrBrdf = File.ReadAllText(PbrBrdfPath);
            string hybrid = File.ReadAllText(HybridModelPath);

            AssertToonHelperAndModelContracts(toon, helper);
            AssertBirpHostForwardAddAndLightmapContracts(host, shaderCoreLighting);
            AssertPbrAndHybridLightingOwnership(pbr, pbrBrdf, hybrid);
            AssertLightingPhaseOrder(host);
            OpenLitSourceContractAssertions.AssertOpenLitFallbackPrecedesNormalization(helper);
        }

        /// <summary>Requires the Toon direction fallback to affect nondegenerate aggregates before normalization.</summary>
        [Test]
        public void ToonOpenLitFallbackIsAddedBeforeDirectionNormalization()
        {
            OpenLitSourceContractAssertions.AssertOpenLitFallbackPrecedesNormalization(File.ReadAllText(ToonLightingHelperPath));
        }

        /// <summary>Requires Toon to separate non-shadow attenuation from Unity effective visibility before Shader-Core light phases.</summary>
        [Test]
        public void ToonShadowSeparationRequiresSplitInputsModelPreparationAndUnchangedNonToonOwnership()
        {
            Assert.That(
                File.Exists(BirpLightAttenuationHelperPath),
                Is.True,
                "Pure Base must own a dedicated non-shadow BIRP attenuation helper."
            );

            ToonShadowSourceSet sources = ToonShadowSourceSet.Load();
            AssertNonShadowAttenuationHelperContract(sources.helper);
            AssertRequiredToonAndNonToonSourceTokens(sources);
            AssertModelPreparationOwnership(sources);
            AssertToonShadowPhaseOrder(sources.host);
            AssertDirectionalVisibilityOwnership(sources.host, sources.toon);
            AssertToonShadowFixtureSourceContracts();
        }

        /// <summary>Stores the source texts participating in the Toon directional-shadow ownership contract.</summary>
        private sealed class ToonShadowSourceSet
        {
            /// <summary>Gets the dedicated non-shadow attenuation helper source.</summary>
            public string helper { get; private set; }

            /// <summary>Gets the Pure Base BIRP host source.</summary>
            public string host { get; private set; }

            /// <summary>Gets the Toon model source.</summary>
            public string toon { get; private set; }

            /// <summary>Gets the PBR model source.</summary>
            public string pbr { get; private set; }

            /// <summary>Gets the Unlit model source.</summary>
            public string unlit { get; private set; }

            /// <summary>Gets the Shader-Core BIRP lighting source.</summary>
            public string shaderCoreLighting { get; private set; }

            /// <summary>Loads all source texts participating in the shadow ownership contract.</summary>
            /// <returns>The loaded source-text set.</returns>
            public static ToonShadowSourceSet Load()
            {
                return new ToonShadowSourceSet
                {
                    helper = File.ReadAllText(BirpLightAttenuationHelperPath),
                    host = File.ReadAllText(BirpHostPath),
                    toon = File.ReadAllText(ToonModelPath),
                    pbr = File.ReadAllText(PbrModelPath),
                    unlit = File.ReadAllText(UnlitModelPath),
                    shaderCoreLighting = File.ReadAllText(ShaderCoreBirpLightingPath),
                };
            }
        }

        /// <summary>Asserts that the dedicated attenuation helper preserves Unity branches without reconstructing visibility.</summary>
        /// <param name="helper">The non-shadow attenuation helper source.</param>
        private static void AssertNonShadowAttenuationHelperContract(string helper)
        {
            foreach (string lightKind in new[] { "DIRECTIONAL", "POINT", "SPOT", "POINT_COOKIE", "DIRECTIONAL_COOKIE" })
            {
                StringAssert.Contains(lightKind, helper, "The non-shadow helper must preserve Unity's " + lightKind + " branch.");
            }

            StringAssert.Contains("PureBaseEvaluateNonShadowLightAttenuation", helper);
            Assert.That(
                Regex.IsMatch(helper, @"/\s*(?:[A-Za-z0-9_]*[Ss]hadow|[Ss]hadow[A-Za-z0-9_]*)"),
                Is.False,
                "The non-shadow helper must not reconstruct attenuation by dividing through visibility."
            );
        }

        /// <summary>Asserts the tokens that separate Toon visibility ownership from shared and non-Toon lighting sources.</summary>
        /// <param name="sources">The loaded source texts to inspect.</param>
        private static void AssertRequiredToonAndNonToonSourceTokens(ToonShadowSourceSet sources)
        {
            StringAssert.Contains("UNITY_SHADOW_ATTENUATION", sources.host);
            StringAssert.Contains("mainLightNonShadowAttenuation", sources.host);
            StringAssert.Contains("mainLightShadowVisibility", sources.host);
            StringAssert.Contains("SCModelPrepareMainLight", sources.host);
            StringAssert.Contains("SCModelPrepareMainLight", sources.toon);
            StringAssert.Contains("sd.shadow", sources.toon);
            StringAssert.Contains("mainLightAttenuation", sources.pbr);
            StringAssert.Contains("SCModelPrepareMainLight", sources.unlit);
            StringAssert.Contains("LIGHTMAP_SHADOW_MIXING", sources.shaderCoreLighting);
            StringAssert.DoesNotContain("LIGHTMAP_SHADOW_MIXING", sources.host);
        }

        /// <summary>Asserts PBR attenuation preservation and Unlit's explicit no-op main-light callback.</summary>
        /// <param name="sources">The loaded source texts to inspect.</param>
        private static void AssertModelPreparationOwnership(ToonShadowSourceSet sources)
        {
            Assert.That(
                Regex.IsMatch(
                    sources.toon,
                    @"void\s+SCModelPrepareMainLight\s*\([^)]*\)\s*\{\s*light\.color\s*=\s*mainLightColor\s*\*\s*mainLightNonShadowAttenuation\s*;\s*sd\.shadow\s*=\s*mainLightShadowVisibility\s*;\s*\}",
                    RegexOptions.Singleline
                ),
                Is.True,
                "Toon must publish non-shadow direct color and independent effective visibility before Shader-Core light phases."
            );
            Assert.That(
                Regex.IsMatch(
                    sources.pbr,
                    @"void\s+SCModelInitializeGiInput\s*\(\s*out\s+UnityGIInput\s+input\s*,\s*SCCustomData\s+customData\s*,\s*SCVertexData\s+vertex\s*\)\s*\{[^}]*\binput\.atten\s*=\s*customData\.mainLightAttenuation\s*;",
                    RegexOptions.Singleline
                ),
                Is.True,
                "PBR and Hybrid must preserve UnityGIInput.atten ownership through the shared PBR model."
            );
            Assert.That(
                Regex.IsMatch(
                    sources.unlit,
                    @"void\s+SCModelPrepareMainLight\s*\([^)]*\)\s*\{\s*\}",
                    RegexOptions.Singleline
                ),
                Is.True,
                "Unlit must retain an explicit no-op main-light preparation callback."
            );
            }

            /// <summary>Asserts that BIRP captures shadow visibility and orders Shader-Core light phases before aggregation.</summary>
            /// <param name="host">The Pure Base BIRP host source.</param>
            private static void AssertToonShadowPhaseOrder(string host)
            {
            int surfaceInitialization = RequireIndex(host, "SCShadingData sd");
            int shadowCapture = RequireIndex(host, "UNITY_SHADOW_ATTENUATION");
            int allLights = RequireIndex(host, "SCCalculateAllLights");
            int lightPhase = RequireIndex(host, "__SC_PHASE_light__");
            int aggregateDirection = RequireIndex(host, "lightSum.direction +=");
            int aggregateColor = RequireIndex(host, "lightSum.color +=");
            int modifyLightPhase = RequireIndex(host, "__SC_PHASE_modifylight__");
            int surfaceColor = RequireIndex(host, "SCModelBaseSurfaceColor");
            int shadePhase = RequireIndex(host, "__SC_PHASE_shade__");
            Assert.That(surfaceInitialization, Is.LessThan(shadowCapture));
            Assert.That(shadowCapture, Is.LessThan(allLights));
            Assert.That(lightPhase, Is.LessThan(aggregateDirection));
            Assert.That(aggregateDirection, Is.LessThan(aggregateColor));
            Assert.That(allLights, Is.LessThan(modifyLightPhase));
            Assert.That(modifyLightPhase, Is.LessThan(shadePhase));
            Assert.That(surfaceColor, Is.LessThan(shadePhase));
            Assert.That(
                Regex.IsMatch(
                    host,
                    @"void\s+SCCalculateLight\s*\([^)]*\)\s*\{[^}]*SCModelSelectMainLightDirection\s*\([^;]*\)\s*;[^}]*SCModelPrepareMainLight\s*\([^;]*\)\s*;[^}]*__SC_PHASE_light__[^}]*lightSum\.direction\s*\+=",
                    RegexOptions.Singleline
                ),
                Is.True,
                "SCCalculateLight must select the main-light direction, prepare model data, expose the light phase, and only then aggregate the light."
            );
        }

        /// <summary>Asserts that Toon consumes directional visibility once in direct radiance without shadowing aggregate direction.</summary>
        /// <param name="host">The Pure Base BIRP host source.</param>
        /// <param name="toon">The Toon model source.</param>
        private static void AssertDirectionalVisibilityOwnership(string host, string toon)
        {
            Assert.That(
                Regex.IsMatch(host, @"lightSum\.(?:color|direction)\s*\*?=\s*[^;]*sd\.shadow"),
                Is.False
            );
            Assert.That(
                Regex.IsMatch(
                    host,
                    @"lightSum\.(?:color|direction)[^;]*mainLightShadowVisibility"
                ),
                Is.False,
                "Directional visibility must be published through sd.shadow, not folded into the direct color or aggregate direction."
            );
            Assert.That(
                Regex.IsMatch(
                    host,
                    @"lightSum\.direction\s*\+=\s*[^;]*(?:sd\.shadow|mainLightShadowVisibility)"
                ),
                Is.False,
                "Directional visibility must not enter Toon aggregate light direction."
            );
            Assert.That(
                Regex.IsMatch(
                    toon,
                    @"sd\.shadow\s*=\s*mainLightShadowVisibility\s*;"
                ),
                Is.True,
                "The Toon callback must publish the captured directional visibility as the phase-local sd.shadow value."
            );
            AssertToonDirectFactorConsumesVisibilityOnce(toon);
        }

        /// <summary>Asserts that Toon direct-factor evaluation consumes the published visibility exactly once.</summary>
        /// <param name="toon">The Toon model source.</param>
        private static void AssertToonDirectFactorConsumesVisibilityOnce(string toon)
        {
            Match directFactor = Regex.Match(
                toon,
                @"half\s+SCModelEvaluateDirectFactor\s*\(\s*SCShadingData\s+shadingData\s*,\s*SCLightData\s+light\s*\)\s*\{(?<body>[^}]*)\}",
                RegexOptions.Singleline
            );
            Assert.That(directFactor.Success, Is.True, "The Toon model must define its direct-factor callback.");
            MatchCollection shadowUses = Regex.Matches(toon, @"\bshadingData\.shadow\b");
            Assert.That(
                shadowUses.Count,
                Is.EqualTo(1),
                "The Toon model source must consume effective visibility through shadingData.shadow exactly once."
            );
            string body = directFactor.Groups["body"].Value;
            Assert.That(
                Regex.IsMatch(
                    body,
                    @"\breturn\s+PureBaseToonEvaluateDirectFactor\s*\(\s*shadingData\.N\s*,\s*light\.direction\s*\)\s*\*\s*shadingData\.shadow\s*;"
                ),
                Is.True,
                "Toon must apply the quantized direct response to effective visibility in its direct-factor callback."
            );
            Assert.That(
                shadowUses[0].Index,
                Is.GreaterThanOrEqualTo(directFactor.Groups["body"].Index)
                    .And.LessThan(directFactor.Groups["body"].Index + body.Length),
                "The Toon model's sole shadingData.shadow consumption must remain within SCModelEvaluateDirectFactor."
            );
        }

        /// <summary>Asserts the fixed Toon host keeps its read-only ForwardAdd stencil state and properties-header license placement.</summary>
        private static void AssertToonShadowFixtureSourceContracts()
        {
            string fixture = File.ReadAllText(ToonShadowFixturePath);
            Match forwardAddStencil = Regex.Match(
                fixture,
                @"Name\s+""ForwardAdd""[\s\S]*?Stencil\s*\{(?<body>[\s\S]*?)\}",
                RegexOptions.Singleline
            );
            Assert.That(forwardAddStencil.Success, Is.True, "The fixed Toon host must retain a ForwardAdd Stencil block.");
            string stencilBody = forwardAddStencil.Groups["body"].Value;
            MatchCollection writeMaskDirectives = Regex.Matches(
                stencilBody,
                @"\bWriteMask\s+(?<value>[^\s}]+)"
            );
            Assert.That(
                writeMaskDirectives.Count,
                Is.EqualTo(1),
                "The fixed Toon host ForwardAdd Stencil block must contain exactly one read-only WriteMask 0 directive."
            );
            Assert.That(
                writeMaskDirectives[0].Groups["value"].Value,
                Is.EqualTo("0"),
                "The fixed Toon host ForwardAdd Stencil block must use 0 as its only WriteMask value."
            );
            StringAssert.DoesNotContain(
                "_StencilWriteMask",
                stencilBody,
                "The fixed Toon host ForwardAdd Stencil block must not restore the dynamic write mask."
            );
            Assert.That(
                Regex.IsMatch(
                    fixture,
                    @"Properties\s*\{\s*/\*.*?Copyright 2026 Penguin.*?Licensed under the Apache License, Version 2\.0.*?WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND.*?\*/\s*__SC_SHADERLAB_properties__",
                    RegexOptions.Singleline
                ),
                Is.True,
                "The fixed Toon host must retain its Apache notice in the importer-valid location before properties expansion."
            );
        }

        /// <summary>Asserts that Toon alone owns its binary direct response and two-band environment interpretation.</summary>
        /// <param name="toon">The Toon model source.</param>
        /// <param name="helper">The Toon lighting helper source.</param>
        private static void AssertToonHelperAndModelContracts(string toon, string helper)
        {
            StringAssert.Contains(
                "return step(0, dot(surfaceNormal, lightDirection));",
                helper,
                "The Toon helper must own the stable binary direct-light response."
            );
            Assert.That(
                Regex.IsMatch(
                    toon,
                    @"\breturn\s+PureBaseToonEvaluateDirectFactor\s*\(\s*shadingData\.N\s*,\s*light\.direction\s*\)\s*\*\s*shadingData\.shadow\s*;"
                ),
                Is.True,
                "The Toon model must delegate direct-light evaluation to its binary helper."
            );
            StringAssert.Contains(
                "float3 PureBaseToonComputeLightDirection",
                helper,
                "The Toon helper must own the stable direct and SH aggregate direction."
            );
            StringAssert.Contains(
                "float3 PureBaseToonEvaluateTwoBandSh",
                helper,
                "The Toon helper must own fixed bright and dark environment band interpretation."
            );
            OpenLitSourceContractAssertions.AssertOpenLitToonEquationContracts(helper);
            StringAssert.Contains(
                "#include \"Packages/jp.penguin.purebase/Shaders/Common/toon_lighting.hlsl\"",
                toon,
                "The Toon model must include its lighting helper as the sole Toon-specific lighting source."
            );
            StringAssert.Contains("SCModelEvaluateAmbient", toon);
            StringAssert.Contains("SCModelSelectEnvironmentLighting", toon);
        }

        /// <summary>Asserts the BIRP host controls ForwardAdd selection and Shader-Core retains lightmap ownership.</summary>
        /// <param name="host">The common BIRP fragment host source.</param>
        /// <param name="shaderCoreLighting">The Shader-Core BIRP lighting source.</param>
        private static void AssertBirpHostForwardAddAndLightmapContracts(string host, string shaderCoreLighting)
        {
            StringAssert.Contains("SCCalculateAllLights", host, "The BIRP host must own the aggregate light flow.");
            StringAssert.Contains("env = SCModelSelectEnvironmentLighting(env);", host);
            StringAssert.Contains("sd.lightColor = lightSum.color + env;", host);
            StringAssert.Contains("sd.lightColor = lightSum.color;", host);
            OpenLitSourceContractAssertions.AssertOpenLitHostGateContracts(host);
            Assert.That(
                RequireIndex(host, "#if defined(UNITY_PASS_FORWARDADD)"),
                Is.LessThan(RequireIndex(host, "env = SCModelSelectEnvironmentLighting(env);")),
                "ForwardAdd must select only the direct aggregate before the Base environment calculation branch."
            );
            StringAssert.Contains("LIGHTMAP_ON", shaderCoreLighting);
            StringAssert.Contains("unity_Lightmap", shaderCoreLighting);
            StringAssert.Contains("__SC_PHASE_customlight__", shaderCoreLighting);
        }

        /// <summary>Asserts PBR and Hybrid retain their independent PBR lighting and binary-diffuse ownership.</summary>
        /// <param name="pbr">The PBR model source.</param>
        /// <param name="pbrBrdf">The shared PBR BRDF source.</param>
        /// <param name="hybrid">The Hybrid model source.</param>
        private static void AssertPbrAndHybridLightingOwnership(string pbr, string pbrBrdf, string hybrid)
        {
            StringAssert.DoesNotContain(
                "toon_lighting.hlsl",
                pbr,
                "PBR must retain its own environment and aggregate-direction ownership."
            );
            StringAssert.Contains(
                "half diffuseNdotL = binaryDiffuse ? step(0.0, signedNdotL) : NdotL;",
                pbrBrdf,
                "Hybrid must retain the existing inline PBR binary diffuse equation."
            );
            StringAssert.DoesNotContain(
                "toon_lighting.hlsl",
                hybrid,
                "Hybrid must retain PBR lighting ownership without consuming Toon SH direction."
            );
            }

            /// <summary>Asserts the required aggregate-light, environment, and fragment-phase execution order.</summary>
            /// <param name="host">The common BIRP fragment host source.</param>
            private static void AssertLightingPhaseOrder(string host)
            {
            int allLights = RequireIndex(host, "SCCalculateAllLights");
            int environmentSelection = RequireIndex(host, "env = SCModelSelectEnvironmentLighting(env);");
            int modifyLight = RequireIndex(host, "__SC_PHASE_modifylight__");
            int shade = RequireIndex(host, "__SC_PHASE_shade__");
            int reflection = RequireIndex(host, "__SC_PHASE_reflection__");
            int add = RequireIndex(host, "__SC_PHASE_add__");
            int postPixel = RequireIndex(host, "__SC_PHASE_postpixel__");
            Assert.That(allLights, Is.LessThan(environmentSelection));
            Assert.That(environmentSelection, Is.LessThan(modifyLight));
            Assert.That(modifyLight, Is.LessThan(shade));
            Assert.That(shade, Is.LessThan(reflection));
            Assert.That(reflection, Is.LessThan(add));
            Assert.That(add, Is.LessThan(postPixel));
        }

        /// <summary>Requires pass-bounded Stencil policy while preserving the existing pass and rendering-mode keyword ABI.</summary>
        [Test]
        public void ProductGeneratedSourcesExposeStencilPassContractsWithoutNewVariantsOrPasses()
        {
            foreach (string shaderName in ProductShaderNames)
            {
                string source = LoadProductSource(shaderName);
                AssertExpectedPassNames(source, shaderName);
                AssertRenderingModeKeywordContracts(source, shaderName);
                AssertNoStencilKeywordsOrPasses(source, shaderName);

                string forwardBasePrefix = ExtractRenderStatePrefix(
                    ExtractNamedPass(source, "ForwardBase"),
                    shaderName,
                    "ForwardBase"
                );
                string forwardAddPrefix = ExtractRenderStatePrefix(
                    ExtractNamedPass(source, "ForwardAdd"),
                    shaderName,
                    "ForwardAdd"
                );
                string shadowCasterPrefix = ExtractRenderStatePrefix(
                    ExtractNamedPass(source, "ShadowCaster"),
                    shaderName,
                    "ShadowCaster"
                );
                string metaPrefix = ExtractRenderStatePrefix(
                    ExtractNamedPass(source, "Meta"),
                    shaderName,
                    "Meta"
                );

                AssertForwardBaseStencilBlock(forwardBasePrefix, shaderName);
                AssertForwardAddStencilBlock(forwardAddPrefix, shaderName);
                AssertNoStencilRenderState(shadowCasterPrefix, shaderName, "ShadowCaster");
                AssertNoStencilRenderState(metaPrefix, shaderName, "Meta");
            }
        }

        /// <summary>Asserts that generated source has exactly the established four named passes.</summary>
        /// <param name="source">The generated shader source.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        private static void AssertExpectedPassNames(string source, string shaderName)
        {
            var passNames = new List<string>();
            foreach (Match match in Regex.Matches(source, @"\bName\s+""(?<name>[^""]+)"""))
            {
                passNames.Add(match.Groups["name"].Value);
            }

            CollectionAssert.AreEqual(
                ExpectedPassNames,
                passNames,
                "Product shader '"
                    + shaderName
                    + "' must retain exactly the established pass order."
            );
        }

        /// <summary>Asserts the exact rendering-mode variant declaration remains the complete allowed variant set.</summary>
        /// <param name="source">The generated shader source.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        private static void AssertRenderingModeKeywordContracts(string source, string shaderName)
        {
            var variantDeclarations = new List<string>();
            foreach (
                Match declaration in Regex.Matches(
                    source,
                    @"^\s*#pragma\s+(?<directive>shader_feature(?:_local)?|multi_compile(?:_local)?)\s+(?<keywords>[^\r\n]+?)\s*$",
                    RegexOptions.Multiline
                )
            )
            {
                variantDeclarations.Add(
                    Regex.Replace(
                        "#pragma "
                            + declaration.Groups["directive"].Value
                            + " "
                            + declaration.Groups["keywords"].Value,
                        @"\s+",
                        " "
                    )
                );
            }

            CollectionAssert.AreEqual(
                new[]
                {
                    ExpectedRenderingModeVariantDeclaration,
                    ExpectedEditorVisualizationVariantDeclaration,
                },
                variantDeclarations,
                "Product shader '"
                    + shaderName
                    + "' must retain exactly the established rendering-mode variant declaration without additional variants."
            );
        }

        /// <summary>Rejects Stencil-specific keyword declarations and named passes without inspecting valid HLSL declarations.</summary>
        /// <param name="source">The generated shader source.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        private static void AssertNoStencilKeywordsOrPasses(string source, string shaderName)
        {
            Assert.That(
                Regex.IsMatch(
                    source,
                    @"^\s*#pragma\s+[^\r\n]*(?:stencil|_stencil)[^\r\n]*$",
                    RegexOptions.Multiline | RegexOptions.IgnoreCase
                ),
                Is.False,
                "Product shader '"
                    + shaderName
                    + "' must not declare a Stencil keyword, shader_feature, or multi_compile variant."
            );
        }

        /// <summary>Extracts one named Pass from its Name marker through the immediate next named Pass.</summary>
        /// <param name="source">The generated shader source.</param>
        /// <param name="passName">The required Pass name.</param>
        /// <returns>The source section belonging only to the requested Pass.</returns>
        private static string ExtractNamedPass(string source, string passName)
        {
            MatchCollection names = Regex.Matches(source, @"\bName\s+""(?<name>[^""]+)""");
            for (int index = 0; index < names.Count; index++)
            {
                Match name = names[index];
                if (!string.Equals(name.Groups["name"].Value, passName, StringComparison.Ordinal))
                {
                    continue;
                }

                int end = index + 1 < names.Count ? names[index + 1].Index : source.Length;
                return source.Substring(name.Index, end - name.Index);
            }

            Assert.Fail("Generated source did not contain Pass '" + passName + "'.");
            return null;
        }

        /// <summary>Limits render-state assertions to the ShaderLab prefix before the Pass HLSLPROGRAM.</summary>
        /// <param name="passSource">The source section for one named Pass.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        /// <param name="passName">The Pass name used in diagnostics.</param>
        /// <returns>The ShaderLab render-state prefix.</returns>
        private static string ExtractRenderStatePrefix(
            string passSource,
            string shaderName,
            string passName
        )
        {
            int hlslProgram = passSource.IndexOf("HLSLPROGRAM", StringComparison.OrdinalIgnoreCase);
            Assert.That(
                hlslProgram,
                Is.GreaterThanOrEqualTo(0),
                "Product shader '"
                    + shaderName
                    + "' Pass '"
                    + passName
                    + "' must contain HLSLPROGRAM."
            );
            return passSource.Substring(0, hlslProgram);
        }

        /// <summary>Asserts that ForwardBase uses the complete seven-property Stencil block.</summary>
        /// <param name="prefix">The ForwardBase ShaderLab render-state prefix.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        private static void AssertForwardBaseStencilBlock(string prefix, string shaderName)
        {
            string body = RequireStencilBody(prefix, shaderName, "ForwardBase");
            const string refDirective = @"\bRef\s*\[\s*_StencilRef\s*\]";
            const string readMaskDirective = @"\bReadMask\s*\[\s*_StencilReadMask\s*\]";
            const string writeMaskDirective = @"\bWriteMask\s*\[\s*_StencilWriteMask\s*\]";
            const string compDirective = @"\bComp\s*\[\s*_StencilComp\s*\]";
            const string passDirective = @"\bPass\s*\[\s*_StencilPass\s*\]";
            const string failDirective = @"\bFail\s*\[\s*_StencilFail\s*\]";
            const string zFailDirective = @"\bZFail\s*\[\s*_StencilZFail\s*\]";

            AssertStencilDirectiveExactlyOnce(
                body,
                refDirective,
                shaderName,
                "ForwardBase",
                "Ref [_StencilRef]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                readMaskDirective,
                shaderName,
                "ForwardBase",
                "ReadMask [_StencilReadMask]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                writeMaskDirective,
                shaderName,
                "ForwardBase",
                "WriteMask [_StencilWriteMask]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                compDirective,
                shaderName,
                "ForwardBase",
                "Comp [_StencilComp]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                passDirective,
                shaderName,
                "ForwardBase",
                "Pass [_StencilPass]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                failDirective,
                shaderName,
                "ForwardBase",
                "Fail [_StencilFail]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                zFailDirective,
                shaderName,
                "ForwardBase",
                "ZFail [_StencilZFail]"
            );

            string unrecognizedState = Regex.Replace(
                body,
                refDirective
                    + "|"
                    + readMaskDirective
                    + "|"
                    + writeMaskDirective
                    + "|"
                    + compDirective
                    + "|"
                    + passDirective
                    + "|"
                    + failDirective
                    + "|"
                    + zFailDirective,
                string.Empty
            );
            Assert.That(
                string.IsNullOrWhiteSpace(unrecognizedState),
                Is.True,
                "Product shader '"
                    + shaderName
                    + "' ForwardBase must contain only the fixed shared Stencil state directives."
            );
        }

        /// <summary>Asserts that ForwardAdd compares the shared Stencil value without writing or repeating operations.</summary>
        /// <param name="prefix">The ForwardAdd ShaderLab render-state prefix.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        private static void AssertForwardAddStencilBlock(string prefix, string shaderName)
        {
            string body = RequireStencilBody(prefix, shaderName, "ForwardAdd");
            const string refDirective = @"\bRef\s*\[\s*_StencilRef\s*\]";
            const string readMaskDirective = @"\bReadMask\s*\[\s*_StencilReadMask\s*\]";
            const string compDirective = @"\bComp\s*\[\s*_StencilComp\s*\]";
            const string writeMaskDirective = @"\bWriteMask\s+0(?:\.0+)?\b";
            const string passDirective = @"\bPass\s+Keep\b";
            const string failDirective = @"\bFail\s+Keep\b";
            const string zFailDirective = @"\bZFail\s+Keep\b";

            AssertStencilDirectiveExactlyOnce(
                body,
                refDirective,
                shaderName,
                "ForwardAdd",
                "Ref [_StencilRef]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                readMaskDirective,
                shaderName,
                "ForwardAdd",
                "ReadMask [_StencilReadMask]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                compDirective,
                shaderName,
                "ForwardAdd",
                "Comp [_StencilComp]"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                writeMaskDirective,
                shaderName,
                "ForwardAdd",
                "WriteMask 0"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                passDirective,
                shaderName,
                "ForwardAdd",
                "Pass Keep"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                failDirective,
                shaderName,
                "ForwardAdd",
                "Fail Keep"
            );
            AssertStencilDirectiveExactlyOnce(
                body,
                zFailDirective,
                shaderName,
                "ForwardAdd",
                "ZFail Keep"
            );

            string unrecognizedState = Regex.Replace(
                body,
                refDirective
                    + "|"
                    + readMaskDirective
                    + "|"
                    + compDirective
                    + "|"
                    + writeMaskDirective
                    + "|"
                    + passDirective
                    + "|"
                    + failDirective
                    + "|"
                    + zFailDirective,
                string.Empty
            );
            Assert.That(
                string.IsNullOrWhiteSpace(unrecognizedState),
                Is.True,
                "Product shader '"
                    + shaderName
                    + "' ForwardAdd must contain only the fixed compare-only Stencil state directives."
            );
        }

        /// <summary>Requires a single ShaderLab Stencil body in a screen-rendering Pass prefix.</summary>
        /// <param name="prefix">The ShaderLab render-state prefix.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        /// <param name="passName">The Pass name used in diagnostics.</param>
        /// <returns>The contents of the Stencil block.</returns>
        private static string RequireStencilBody(string prefix, string shaderName, string passName)
        {
            MatchCollection blocks = Regex.Matches(
                prefix,
                @"\bStencil\s*\{(?<body>[^{}]*)\}",
                RegexOptions.Singleline
            );
            Assert.That(
                blocks.Count,
                Is.EqualTo(1),
                "Product shader '"
                    + shaderName
                    + "' Pass '"
                    + passName
                    + "' must contain exactly one bounded Stencil block."
            );
            return blocks[0].Groups["body"].Value;
        }

        /// <summary>Asserts one fixed Stencil directive and rejects duplicate or alternate state.</summary>
        /// <param name="body">The bounded Stencil block body.</param>
        /// <param name="pattern">The exact directive pattern.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        /// <param name="passName">The Pass name used in diagnostics.</param>
        /// <param name="description">The human-readable directive description.</param>
        private static void AssertStencilDirectiveExactlyOnce(
            string body,
            string pattern,
            string shaderName,
            string passName,
            string description
        )
        {
            Assert.That(
                Regex.Matches(body, pattern).Count,
                Is.EqualTo(1),
                "Product shader '"
                    + shaderName
                    + "' "
                    + passName
                    + " must contain exactly one "
                    + description
                    + " directive."
            );
        }

        /// <summary>Rejects Stencil blocks and property directives from ShadowCaster and Meta render-state prefixes.</summary>
        /// <param name="prefix">The ShaderLab render-state prefix.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        /// <param name="passName">The Pass name used in diagnostics.</param>
        private static void AssertNoStencilRenderState(
            string prefix,
            string shaderName,
            string passName
        )
        {
            Assert.That(
                Regex.IsMatch(prefix, @"\bStencil\b|_Stencil", RegexOptions.IgnoreCase),
                Is.False,
                "Product shader '"
                    + shaderName
                    + "' Pass '"
                    + passName
                    + "' must not apply Stencil before HLSLPROGRAM."
            );
        }

        /// <summary>Loads one generated product source subasset without modifying its import state.</summary>
        /// <param name="shaderName">The product shader name.</param>
        /// <returns>The generated source text.</returns>
        private static string LoadProductSource(string shaderName)
        {
            foreach (
                string guid in AssetDatabase.FindAssets(
                    "t:Shader",
                    new[] { "Packages/jp.penguin.purebase/Shaders" }
                )
            )
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (
                    shader == null
                    || !string.Equals(shader.name, shaderName, StringComparison.Ordinal)
                )
                    continue;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    var source = asset as TextAsset;
                    if (source != null && source.name == "Shader Source")
                        return source.text;
                }
            }

            Assert.Fail(
                "Generated source for product shader '" + shaderName + "' was unavailable."
            );
            return null;
        }

        /// <summary>Returns one required marker index with a diagnostic that keeps source-order failures local.</summary>
        /// <param name="source">The source text to inspect.</param>
        /// <param name="marker">The required marker.</param>
        /// <returns>The marker index.</returns>
        private static int RequireIndex(string source, string marker)
        {
            int index = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.That(
                index,
                Is.GreaterThanOrEqualTo(0),
                "Required source marker '" + marker + "' was absent."
            );
            return index;
        }
    }
}
