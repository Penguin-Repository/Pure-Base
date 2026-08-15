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

// Records a transient lilToon 2.3.4 BIRP bright/dark classification observation for the OpenLit runtime inputs.

using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Records a transient lilToon 2.3.4 BIRP bright/dark classification observation for the OpenLit runtime inputs.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Identifies the installed lilToon BIRP shader observed by this supplemental test.</summary>
        private const string LilToonShaderName = "lilToon";

        /// <summary>Identifies the installed lilToon package manifest relative to the Unity project root.</summary>
        private const string LilToonPackageManifestRelativePath = "Packages/jp.lilxyzw.liltoon/package.json";

        /// <summary>Identifies the VRC Light Volumes enable global that must remain disabled for this observation.</summary>
        private const string UdonLightVolumeEnabledGlobalName = "_UdonLightVolumeEnabled";

        /// <summary>Records top, side, and bottom lilToon output classifications without treating final lilToon RGB as a parity result.</summary>
        [Test]
        public void LilToon234BirpObservationRecordsTopSideBottomClassificationAndTransitionOrientation()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.EqualTo(GraphicsDeviceType.Direct3D11));
            Assert.That(QualitySettings.activeColorSpace, Is.EqualTo(ColorSpace.Linear));
            AssertInstalledLilToon234();

            ShCoefficients coefficients = CreateOpenLitCoefficients();
            Vector3 lightDirection = EvaluateOpenLitDominantDirection(
                Vector3.zero,
                coefficients.ar,
                coefficients.ag,
                coefficients.ab
            );

            using (var capture = new ToonLightingCaptureScope())
            {
                Color top = capture.RenderLilToonOpenLitObservation(lightDirection, lightDirection, coefficients);
                Color side = capture.RenderLilToonOpenLitObservation(Vector3.right, lightDirection, coefficients);
                Color bottom = capture.RenderLilToonOpenLitObservation(-lightDirection, lightDirection, coefficients);

                AssertFinite(top, "lilToon 2.3.4 top BIRP observation");
                AssertFinite(side, "lilToon 2.3.4 side BIRP observation");
                AssertFinite(bottom, "lilToon 2.3.4 bottom BIRP observation");
                RecordLilToonObservation(lightDirection, coefficients, top, side, bottom);
            }
        }

        /// <summary>Requires the read-only comparison target to be the installed lilToon 2.3.4 package.</summary>
        private static void AssertInstalledLilToon234()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string manifestPath = Path.Combine(projectRoot, LilToonPackageManifestRelativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.That(File.Exists(manifestPath), Is.True, "The installed lilToon package manifest is unavailable for the BIRP observation.");
            StringAssert.Contains("\"version\": \"2.3.4\"", File.ReadAllText(manifestPath));
        }

        /// <summary>Writes the reproducible classification observation and its known non-parity RGB boundaries to the NUnit result.</summary>
        /// <param name="lightDirection">The normalized OpenLit direction used for all three samples.</param>
        /// <param name="coefficients">The seven Unity SH vectors shared with the formal OpenLit runtime test.</param>
        /// <param name="top">The top-normal lilToon BIRP readback.</param>
        /// <param name="side">The side-normal lilToon BIRP readback.</param>
        /// <param name="bottom">The bottom-normal lilToon BIRP readback.</param>
        private static void RecordLilToonObservation(
            Vector3 lightDirection,
            ShCoefficients coefficients,
            Color top,
            Color side,
            Color bottom
        )
        {
            float topLuminance = EvaluateObservationLuminance(top);
            float sideLuminance = EvaluateObservationLuminance(side);
            float bottomLuminance = EvaluateObservationLuminance(bottom);
            float midpoint = (topLuminance + bottomLuminance) * 0.5f;

            TestContext.WriteLine(
                "lilToon 2.3.4 BIRP OpenLit observation | "
                + "directColor=(0,0,0), direction=" + lightDirection
                + ", SHAr=" + coefficients.ar + ", SHAg=" + coefficients.ag + ", SHAb=" + coefficients.ab
                + ", SHBr=" + coefficients.br + ", SHBg=" + coefficients.bg + ", SHBb=" + coefficients.bb + ", SHC=" + coefficients.c
                + ", top=" + DescribeObservation(top, topLuminance, midpoint)
                + ", side=" + DescribeObservation(side, sideLuminance, midpoint)
                + ", bottom=" + DescribeObservation(bottom, bottomLuminance, midpoint)
                + ", orientation=" + DescribeTransitionOrientation(topLuminance, sideLuminance, bottomLuminance)
                + "; RGB is descriptive only: lilToon CorrectLights and shade threshold/color stages are outside Pure Base parity."
            );
        }

        /// <summary>Calculates the Linear luminance used only to label a supplemental lilToon observation.</summary>
        /// <param name="color">The finite linear BIRP readback.</param>
        /// <returns>The observation-only luminance.</returns>
        private static float EvaluateObservationLuminance(Color color)
        {
            return color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;
        }

        /// <summary>Formats one color readback with its observed bright, dark, or transition classification.</summary>
        /// <param name="color">The observed linear BIRP color.</param>
        /// <param name="luminance">The observation-only luminance.</param>
        /// <param name="midpoint">The midpoint between top and bottom luminance.</param>
        /// <returns>A stable NUnit output fragment.</returns>
        private static string DescribeObservation(Color color, float luminance, float midpoint)
        {
            const float classificationTolerance = 0.001f;
            string classification = luminance > midpoint + classificationTolerance
                ? "bright"
                : luminance < midpoint - classificationTolerance
                    ? "dark"
                    : "transition";
            return "rgb=" + color + ", luminance=" + luminance + ", classification=" + classification;
        }

        /// <summary>Describes the observed top-to-bottom transition without asserting a lilToon final-color expectation.</summary>
        /// <param name="top">The top-normal luminance.</param>
        /// <param name="side">The side-normal luminance.</param>
        /// <param name="bottom">The bottom-normal luminance.</param>
        /// <returns>The observed monotonic orientation or its explicit non-monotonic state.</returns>
        private static string DescribeTransitionOrientation(float top, float side, float bottom)
        {
            const float transitionTolerance = 0.001f;
            if (top >= side - transitionTolerance
                && side >= bottom - transitionTolerance
                && top > bottom + transitionTolerance)
            {
                return "top-to-bottom darkening";
            }

            if (top <= side + transitionTolerance
                && side <= bottom + transitionTolerance
                && top < bottom - transitionTolerance)
            {
                return "top-to-bottom brightening";
            }

            return Mathf.Abs(top - bottom) <= transitionTolerance ? "flat" : "non-monotonic";
        }

        /// <summary>Owns the lilToon-specific extension of the existing transient BIRP capture scope.</summary>
        private partial class ToonLightingCaptureRuntimeScope
        {
            /// <summary>Renders one lilToon ForwardBase observation with all available nonessential correction controls neutralized.</summary>
            /// <param name="normal">The uniform world-space normal for the observed surface.</param>
            /// <param name="lightDirection">The normalized OpenLit direction retained across every sample.</param>
            /// <param name="coefficients">The seven injected Unity SH vectors.</param>
            /// <returns>The center linear readback used only for observation output.</returns>
            public Color RenderLilToonOpenLitObservation(Vector3 normal, Vector3 lightDirection, ShCoefficients coefficients)
            {
                float originalLightVolumeEnabled = Shader.GetGlobalFloat(UdonLightVolumeEnabledGlobalName);
                Material material = CreateLilToonObservationMaterial();
                try
                {
                    ConfigureLilToonObservationMaterial(material);
                    Shader.SetGlobalFloat(UdonLightVolumeEnabledGlobalName, 0.0f);
                    return RenderWithLights(
                        material,
                        CreateDirectionalLightCaptureRequest(
                            normal,
                            Vector4.zero,
                            new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f),
                            coefficients
                        )
                    );
                }
                finally
                {
                    Shader.SetGlobalFloat(UdonLightVolumeEnabledGlobalName, originalLightVolumeEnabled);
                }
            }

            /// <summary>Creates a registered transient lilToon material without imposing a Pure Base named-pass contract.</summary>
            /// <returns>The capture-owned lilToon material for BIRP Forward rendering.</returns>
            private Material CreateLilToonObservationMaterial()
            {
                Shader shader = Shader.Find(LilToonShaderName);
                Assert.That(shader, Is.Not.Null, "Installed lilToon 2.3.4 BIRP shader is unavailable.");
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, "Installed lilToon 2.3.4 BIRP shader has compiler errors.");

                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                materials.Add(material);
                ConfigureMaterial(material, 0.0f);
                return material;
            }

            /// <summary>Neutralizes material-local lilToon corrections while retaining its shade stage for classification observation.</summary>
            /// <param name="material">The capture-owned transient lilToon material.</param>
            private static void ConfigureLilToonObservationMaterial(Material material)
            {
                material.SetTexture("_MainTex", Texture2D.whiteTexture);
                material.SetColor("_Color", Color.white);
                material.SetFloat("_LightMinLimit", 0.0f);
                material.SetFloat("_LightMaxLimit", 10.0f);
                material.SetFloat("_MonochromeLighting", 0.0f);
                material.SetFloat("_AsUnlit", 0.0f);
                material.SetFloat("_lilDirectionalLightStrength", 1.0f);
                material.SetFloat("_UseShadow", 1.0f);
                material.SetVector("_LightDirectionOverride", new Vector4(0.001f, 0.002f, 0.001f, 0.0f));
                material.DisableKeyword("LTCGI");
                material.DisableKeyword("LIL_LTCGI");
            }
        }
    }
}
