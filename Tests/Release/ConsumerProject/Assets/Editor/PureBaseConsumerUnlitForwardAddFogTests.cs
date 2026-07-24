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

// Renders the runner-selected Unlit ForwardAdd fog signal with controlled BIRP fog state.

using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Release.Consumer.Tests
{
    /// <summary>Validates that the selected Unlit ForwardAdd signal attenuates toward black under controlled BIRP fog.</summary>
    public sealed class PureBaseConsumerUnlitForwardAddFogTests
    {
        /// <summary>Defines the square HDR readback dimension.</summary>
        private const int RenderSize = 96;

        /// <summary>Identifies Unity's BIRP linear fog keyword.</summary>
        private const string FogLinearKeyword = "FOG_LINEAR";

        /// <summary>Identifies Unity's BIRP exponential fog keyword.</summary>
        private const string FogExponentialKeyword = "FOG_EXP";

        /// <summary>Identifies Unity's BIRP exponential-squared fog keyword.</summary>
        private const string FogExponentialSquaredKeyword = "FOG_EXP2";

        /// <summary>Identifies Unity's BIRP fog color global.</summary>
        private const string FogColorGlobalName = "unity_FogColor";

        /// <summary>Identifies Unity's BIRP fog parameter global.</summary>
        private const string FogParametersGlobalName = "unity_FogParams";

        /// <summary>Renders the selected ForwardAdd signal with fog disabled and enabled, then records its attenuation toward black.</summary>
        [Test]
        public void SelectedForwardAddSignalAttenuatesTowardBlackWithControlledFog()
        {
            ConsumerValidationContract contract = ConsumerValidationSupport.LoadContract();
            Assert.That(SystemInfo.graphicsDeviceType, Is.EqualTo(GraphicsDeviceType.Direct3D11), $"Consumer run '{contract.runLabel}' requires Direct3D11 for Unlit ForwardAdd fog evidence.");
            Assert.That(GraphicsSettings.currentRenderPipeline, Is.Null, $"Consumer run '{contract.runLabel}' requires the Built-in Render Pipeline for Unlit ForwardAdd fog evidence.");
            Assert.That(contract.unlitForwardAddFog, Is.Not.Null, $"Consumer run '{contract.runLabel}' must provide unlitForwardAddFog.");
            ValidateContract(contract);

            ConsumerUnlitForwardAddFogContract fogContract = contract.unlitForwardAddFog;
            Shader shader = ConsumerValidationSupport.ImportProductShader(fogContract.product, contract.runLabel);
            string generatedSource = ConsumerValidationSupport.LoadGeneratedSource(fogContract.product, contract.runLabel);
            ConsumerValidationSupport.ExportGeneratedSource(contract.runLabel, fogContract.product.shaderName, generatedSource);
            StringAssert.Contains("[SCModule(" + fogContract.moduleUniqueId + ")]", generatedSource, $"Consumer run '{contract.runLabel}' did not import selected ForwardAdd fog module '{fogContract.moduleUniqueId}'.");
            StringAssert.Contains(fogContract.sentinel, generatedSource, $"Consumer run '{contract.runLabel}' did not retain selected ForwardAdd fog sentinel '{fogContract.sentinel}'.");
            PureBaseConsumerModuleFreeImportTests.AssertInactiveSentinels(contract, generatedSource);

            ConsumerForwardAddFogArtifact artifact = new ConsumerForwardAddFogArtifact
            {
                runLabel = contract.runLabel,
                shader = shader.name,
                moduleUniqueId = fogContract.moduleUniqueId,
                sentinel = fogContract.sentinel,
                fogMode = fogContract.fog.mode,
                fogDensity = fogContract.fog.density,
                fogRed = fogContract.fog.color.red,
                fogGreen = fogContract.fog.color.green,
                fogBlue = fogContract.fog.color.blue,
                fogAlpha = fogContract.fog.color.alpha,
                cameraFieldOfView = fogContract.cameraFieldOfView,
            };
            try
            {
                using (ConsumerForwardAddFogFixture fixture = new ConsumerForwardAddFogFixture(shader, fogContract))
                {
                    Color withoutFog = fixture.Render(false);
                    artifact.fogDisabled = ConsumerColorArtifact.FromColor(withoutFog);
                    artifact.fogDisabledRgbMagnitude = RgbMagnitude(withoutFog);
                    AssertInRange(artifact.fogDisabledRgbMagnitude, fogContract.fogDisabledSignalMagnitude, contract.runLabel, "fog-disabled signal RGB magnitude");

                    Color withFog = fixture.Render(true);
                    artifact.fogEnabled = ConsumerColorArtifact.FromColor(withFog);
                    artifact.fogEnabledRgbMagnitude = RgbMagnitude(withFog);
                    artifact.retainedSignalFraction = artifact.fogEnabledRgbMagnitude / artifact.fogDisabledRgbMagnitude;
                    artifact.renderSettingsFogEnabled = RenderSettings.fog;
                    artifact.globalExponentialFogKeywordEnabled = Shader.IsKeywordEnabled(FogExponentialKeyword);
                    artifact.materialExponentialFogKeywordEnabled = fixture.IsMaterialKeywordEnabled(FogExponentialKeyword);
                    artifact.fogParametersY = Shader.GetGlobalVector(FogParametersGlobalName).y;
                    AssertInRange(artifact.retainedSignalFraction, fogContract.retainedSignalFraction, contract.runLabel, "fog-enabled retained signal fraction");
                    AssertInRange(withFog.r, fogContract.blackFogRed, contract.runLabel, "fog-enabled black red");
                    AssertInRange(withFog.g, fogContract.blackFogGreen, contract.runLabel, "fog-enabled black green");
                    AssertInRange(withFog.b, fogContract.blackFogBlue, contract.runLabel, "fog-enabled black blue");
                    AssertInRange(withFog.a, fogContract.blackFogAlpha, contract.runLabel, "fog-enabled black alpha");
                }
            }
            finally
            {
                File.WriteAllText(Path.Combine(ConsumerValidationSupport.GetArtifactDirectory(), "unlit-forward-add-fog-readbacks.json"), JsonUtility.ToJson(artifact, true));
            }
        }

        /// <summary>Validates every runner-provided input before importing or rendering the selected product.</summary>
        /// <param name="contract">The current consumer contract.</param>
        private static void ValidateContract(ConsumerValidationContract contract)
        {
            ConsumerUnlitForwardAddFogContract fogContract = contract.unlitForwardAddFog;
            Assert.That(fogContract.product, Is.Not.Null, $"Consumer run '{contract.runLabel}' unlitForwardAddFog must provide product.");
            Assert.That(fogContract.product.shaderName, Is.EqualTo("PureBase/Unlit"), $"Consumer run '{contract.runLabel}' unlitForwardAddFog must render PureBase/Unlit.");
            Assert.That(fogContract.product.shaderAssetPath, Is.Not.Empty, $"Consumer run '{contract.runLabel}' unlitForwardAddFog product must provide shaderAssetPath.");
            Assert.That(fogContract.moduleUniqueId, Is.Not.Empty, $"Consumer run '{contract.runLabel}' unlitForwardAddFog must provide moduleUniqueId.");
            Assert.That(fogContract.sentinel, Is.Not.Empty, $"Consumer run '{contract.runLabel}' unlitForwardAddFog must provide sentinel.");
            Assert.That(fogContract.floatAssignments, Is.Not.Null, $"Consumer run '{contract.runLabel}' unlitForwardAddFog must provide floatAssignments, including an empty array when none are needed.");
            Assert.That(fogContract.fog, Is.Not.Null, $"Consumer run '{contract.runLabel}' unlitForwardAddFog must provide fog.");
            Assert.That(fogContract.fog.mode, Is.EqualTo(FogMode.Exponential.ToString()), $"Consumer run '{contract.runLabel}' unlitForwardAddFog must use Exponential fog.");
            Assert.That(fogContract.fog.color, Is.Not.Null, $"Consumer run '{contract.runLabel}' unlitForwardAddFog must provide fog.color.");
            Assert.That(fogContract.fog.density, Is.GreaterThan(0.0f), $"Consumer run '{contract.runLabel}' unlitForwardAddFog must provide a positive fog density.");
            Assert.That(fogContract.cameraFieldOfView, Is.InRange(1.0f, 179.0f), $"Consumer run '{contract.runLabel}' unlitForwardAddFog must provide a perspective camera field of view between 1 and 179 degrees.");
            ConsumerValidationSupport.ValidateRange(fogContract.fogDisabledSignalMagnitude, "unlitForwardAddFog.fogDisabledSignalMagnitude");
            ConsumerValidationSupport.ValidateRange(fogContract.retainedSignalFraction, "unlitForwardAddFog.retainedSignalFraction");
            ConsumerValidationSupport.ValidateRange(fogContract.blackFogRed, "unlitForwardAddFog.blackFogRed");
            ConsumerValidationSupport.ValidateRange(fogContract.blackFogGreen, "unlitForwardAddFog.blackFogGreen");
            ConsumerValidationSupport.ValidateRange(fogContract.blackFogBlue, "unlitForwardAddFog.blackFogBlue");
            ConsumerValidationSupport.ValidateRange(fogContract.blackFogAlpha, "unlitForwardAddFog.blackFogAlpha");
            Assert.That(fogContract.fogDisabledSignalMagnitude.minimum, Is.GreaterThan(0.0f), $"Consumer run '{contract.runLabel}' unlitForwardAddFog must require a nonzero fog-disabled signal.");
        }

        /// <summary>Asserts an observed scalar against a runner-provided inclusive range.</summary>
        /// <param name="actual">The observed scalar.</param>
        /// <param name="expected">The expected inclusive range.</param>
        /// <param name="runLabel">The current consumer run label.</param>
        /// <param name="description">The observed quantity description.</param>
        private static void AssertInRange(float actual, ConsumerFloatRange expected, string runLabel, string description)
        {
            Assert.That(actual, Is.InRange(expected.minimum, expected.maximum), $"Consumer run '{runLabel}' observed {description}={actual}, but expected [{expected.minimum}, {expected.maximum}].");
        }

        /// <summary>Returns the Euclidean RGB magnitude of one HDR color.</summary>
        /// <param name="color">The HDR color to inspect.</param>
        /// <returns>The RGB magnitude.</returns>
        private static float RgbMagnitude(Color color)
        {
            return Mathf.Sqrt((color.r * color.r) + (color.g * color.g) + (color.b * color.b));
        }

        /// <summary>Owns an actual imported Unlit ForwardAdd material and restores global fog state after direct pass rendering.</summary>
        private sealed class ConsumerForwardAddFogFixture : IDisposable
        {
            /// <summary>Stores the fog-enabled value active before fixture setup.</summary>
            private readonly bool originalFog;

            /// <summary>Stores the fog mode active before fixture setup.</summary>
            private readonly FogMode originalFogMode;

            /// <summary>Stores the fog color active before fixture setup.</summary>
            private readonly Color originalFogColor;

            /// <summary>Stores the fog density active before fixture setup.</summary>
            private readonly float originalFogDensity;

            /// <summary>Stores the BIRP fog color global active before fixture setup.</summary>
            private readonly Color originalFogColorGlobal;

            /// <summary>Stores the BIRP fog parameter global active before fixture setup.</summary>
            private readonly Vector4 originalFogParametersGlobal;

            /// <summary>Stores the linear fog keyword state active before fixture setup.</summary>
            private readonly bool originalFogLinearKeyword;

            /// <summary>Stores the exponential fog keyword state active before fixture setup.</summary>
            private readonly bool originalFogExponentialKeyword;

            /// <summary>Stores the exponential-squared fog keyword state active before fixture setup.</summary>
            private readonly bool originalFogExponentialSquaredKeyword;

            /// <summary>Stores the runner-provided fog configuration.</summary>
            private readonly ConsumerUnlitForwardAddFogContract fogContract;

            /// <summary>Stores the temporary actual product material.</summary>
            private readonly Material material;

            /// <summary>Stores the full-frame mesh used for direct ForwardAdd draws.</summary>
            private readonly Mesh mesh;

            /// <summary>Stores the temporary direct-draw camera GameObject.</summary>
            private readonly GameObject cameraObject;

            /// <summary>Stores the direct-draw camera.</summary>
            private readonly Camera camera;

            /// <summary>Stores the temporary HDR target.</summary>
            private readonly RenderTexture target;

            /// <summary>Stores the temporary CPU-readable HDR texture.</summary>
            private readonly Texture2D readback;

            /// <summary>Stores the actual ForwardAdd pass index.</summary>
            private readonly int forwardAddPass;

            /// <summary>Initializes actual Unlit resources and captures the complete global fog state.</summary>
            /// <param name="shader">The imported selected Unlit shader.</param>
            /// <param name="fogContract">The runner-provided fog configuration.</param>
            public ConsumerForwardAddFogFixture(Shader shader, ConsumerUnlitForwardAddFogContract fogContract)
            {
                originalFog = RenderSettings.fog;
                originalFogMode = RenderSettings.fogMode;
                originalFogColor = RenderSettings.fogColor;
                originalFogDensity = RenderSettings.fogDensity;
                originalFogColorGlobal = Shader.GetGlobalColor(FogColorGlobalName);
                originalFogParametersGlobal = Shader.GetGlobalVector(FogParametersGlobalName);
                originalFogLinearKeyword = Shader.IsKeywordEnabled(FogLinearKeyword);
                originalFogExponentialKeyword = Shader.IsKeywordEnabled(FogExponentialKeyword);
                originalFogExponentialSquaredKeyword = Shader.IsKeywordEnabled(FogExponentialSquaredKeyword);
                this.fogContract = fogContract;
                material = new Material(shader) { name = "PureBase Consumer Unlit ForwardAdd Fog Material" };
                foreach (ConsumerFloatAssignment assignment in fogContract.floatAssignments)
                {
                    Assert.That(assignment, Is.Not.Null, "Consumer Unlit ForwardAdd fog contract has a null float assignment.");
                    Assert.That(assignment.propertyName, Is.Not.Empty, "Consumer Unlit ForwardAdd fog contract has a float assignment without propertyName.");
                    Assert.That(material.HasProperty(assignment.propertyName), Is.True, $"Consumer Unlit ForwardAdd fog product '{shader.name}' does not expose '{assignment.propertyName}'.");
                    material.SetFloat(assignment.propertyName, assignment.value);
                }
                material.SetFloat("_Cull", 0.0f);

                forwardAddPass = material.FindPass("ForwardAdd");
                Assert.That(forwardAddPass, Is.GreaterThanOrEqualTo(0), $"Consumer Unlit ForwardAdd fog product '{shader.name}' does not expose ForwardAdd.");
                mesh = CreateScreenMesh();
                cameraObject = new GameObject("PureBase Consumer Unlit ForwardAdd Fog Camera");
                camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 0;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.transform.position = new Vector3(0.0f, 0.0f, -5.0f);
                camera.orthographic = false;
                camera.fieldOfView = fogContract.cameraFieldOfView;
                target = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                target.Create();
                camera.targetTexture = target;
                readback = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
            }

            /// <summary>Renders only the actual ForwardAdd pass with the requested controlled fog state.</summary>
            /// <param name="fogEnabled">Whether exponential fog must be enabled for the draw.</param>
            /// <returns>The HDR center pixel from the direct pass draw.</returns>
            public Color Render(bool fogEnabled)
            {
                SetFogState(fogEnabled);
                CommandBuffer commandBuffer = new CommandBuffer { name = fogEnabled ? "PureBase Consumer ForwardAdd Fog Enabled" : "PureBase Consumer ForwardAdd Fog Disabled" };
                try
                {
                    commandBuffer.SetRenderTarget(target);
                    commandBuffer.ClearRenderTarget(true, true, Color.black);
                    commandBuffer.DrawMesh(mesh, Matrix4x4.identity, material, 0, forwardAddPass);
                    camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                    camera.Render();
                    return ReadbackCenter();
                }
                finally
                {
                    camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
                    commandBuffer.Release();
                }
            }

            /// <summary>Returns whether the fixture material selected one fog keyword.</summary>
            /// <param name="keyword">The keyword to query.</param>
            /// <returns>Whether the material selected the keyword.</returns>
            public bool IsMaterialKeywordEnabled(string keyword)
            {
                return material.IsKeywordEnabled(keyword);
            }

            /// <summary>Restores every global fog setting and releases all fixture-owned resources.</summary>
            public void Dispose()
            {
                RenderSettings.fog = originalFog;
                RenderSettings.fogMode = originalFogMode;
                RenderSettings.fogColor = originalFogColor;
                RenderSettings.fogDensity = originalFogDensity;
                Shader.SetGlobalColor(FogColorGlobalName, originalFogColorGlobal);
                Shader.SetGlobalVector(FogParametersGlobalName, originalFogParametersGlobal);
                SetFogKeywords(originalFogLinearKeyword, originalFogExponentialKeyword, originalFogExponentialSquaredKeyword);
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(readback);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(material);
            }

            /// <summary>Applies the requested BIRP fog state to render settings, globals, and the actual material variant.</summary>
            /// <param name="fogEnabled">Whether exponential fog must be enabled.</param>
            private void SetFogState(bool fogEnabled)
            {
                RenderSettings.fog = fogEnabled;
                if (fogEnabled)
                {
                    Color color = new Color(fogContract.fog.color.red, fogContract.fog.color.green, fogContract.fog.color.blue, fogContract.fog.color.alpha);
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogColor = color;
                    RenderSettings.fogDensity = fogContract.fog.density;
                    Shader.SetGlobalColor(FogColorGlobalName, color);
                    Shader.SetGlobalVector(FogParametersGlobalName, new Vector4(0.0f, fogContract.fog.density * 1.4426951f, 0.0f, 0.0f));
                    SetFogKeywords(false, true, false);
                }
                else
                {
                    SetFogKeywords(false, false, false);
                }

                SetMaterialFogKeywords(false, fogEnabled, false);
            }

            /// <summary>Sets global BIRP fog keywords to the requested state.</summary>
            /// <param name="linear">Whether linear fog is enabled.</param>
            /// <param name="exponential">Whether exponential fog is enabled.</param>
            /// <param name="exponentialSquared">Whether exponential-squared fog is enabled.</param>
            private static void SetFogKeywords(bool linear, bool exponential, bool exponentialSquared)
            {
                SetKeyword(FogLinearKeyword, linear);
                SetKeyword(FogExponentialKeyword, exponential);
                SetKeyword(FogExponentialSquaredKeyword, exponentialSquared);
            }

            /// <summary>Sets material BIRP fog keywords to the requested state.</summary>
            /// <param name="linear">Whether linear fog is enabled.</param>
            /// <param name="exponential">Whether exponential fog is enabled.</param>
            /// <param name="exponentialSquared">Whether exponential-squared fog is enabled.</param>
            private void SetMaterialFogKeywords(bool linear, bool exponential, bool exponentialSquared)
            {
                SetMaterialKeyword(FogLinearKeyword, linear);
                SetMaterialKeyword(FogExponentialKeyword, exponential);
                SetMaterialKeyword(FogExponentialSquaredKeyword, exponentialSquared);
            }

            /// <summary>Sets one global shader keyword.</summary>
            /// <param name="keyword">The keyword to set.</param>
            /// <param name="enabled">Whether the keyword must be enabled.</param>
            private static void SetKeyword(string keyword, bool enabled)
            {
                if (enabled)
                {
                    Shader.EnableKeyword(keyword);
                }
                else
                {
                    Shader.DisableKeyword(keyword);
                }
            }

            /// <summary>Sets one fixture-material shader keyword.</summary>
            /// <param name="keyword">The keyword to set.</param>
            /// <param name="enabled">Whether the keyword must be enabled.</param>
            private void SetMaterialKeyword(string keyword, bool enabled)
            {
                if (enabled)
                {
                    material.EnableKeyword(keyword);
                }
                else
                {
                    material.DisableKeyword(keyword);
                }
            }

            /// <summary>Reads the HDR target center pixel after a direct pass draw.</summary>
            /// <returns>The finite center pixel.</returns>
            private Color ReadbackCenter()
            {
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0.0f, 0.0f, RenderSize, RenderSize), 0, 0, false);
                    readback.Apply(false, false);
                    Color center = readback.GetPixel(RenderSize / 2, RenderSize / 2);
                    Assert.That(float.IsNaN(center.r) || float.IsInfinity(center.r) || float.IsNaN(center.g) || float.IsInfinity(center.g) || float.IsNaN(center.b) || float.IsInfinity(center.b) || float.IsNaN(center.a) || float.IsInfinity(center.a), Is.False, "Consumer Unlit ForwardAdd fog readback produced a non-finite center pixel.");
                    return center;
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            /// <summary>Creates a full-frame mesh for direct actual-pass draws.</summary>
            /// <returns>The initialized mesh.</returns>
            private static Mesh CreateScreenMesh()
            {
                Mesh mesh = new Mesh { name = "PureBase Consumer Unlit ForwardAdd Fog Mesh" };
                mesh.vertices = new[] { new Vector3(-1.0f, -1.0f, 0.0f), new Vector3(1.0f, -1.0f, 0.0f), new Vector3(1.0f, 1.0f, 0.0f), new Vector3(-1.0f, 1.0f, 0.0f) };
                Vector2[] uvs = { new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.0f), new Vector2(1.0f, 1.0f), new Vector2(0.0f, 1.0f) };
                mesh.uv = uvs;
                mesh.uv2 = uvs;
                mesh.uv3 = uvs;
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>Stores actual selected ForwardAdd fog evidence for one consumer run.</summary>
        [Serializable]
        private sealed class ConsumerForwardAddFogArtifact
        {
            /// <summary>Stores the current consumer run label.</summary>
            public string runLabel;

            /// <summary>Stores the rendered public shader name.</summary>
            public string shader;

            /// <summary>Stores the selected module identity.</summary>
            public string moduleUniqueId;

            /// <summary>Stores the selected generated-source sentinel.</summary>
            public string sentinel;

            /// <summary>Stores the configured fog mode.</summary>
            public string fogMode;

            /// <summary>Stores the configured fog density.</summary>
            public float fogDensity;

            /// <summary>Stores the configured fog red component.</summary>
            public float fogRed;

            /// <summary>Stores the configured fog green component.</summary>
            public float fogGreen;

            /// <summary>Stores the configured fog blue component.</summary>
            public float fogBlue;

            /// <summary>Stores the configured fog alpha component.</summary>
            public float fogAlpha;

            /// <summary>Stores the configured perspective field of view.</summary>
            public float cameraFieldOfView;

            /// <summary>Stores the fog-disabled center readback.</summary>
            public ConsumerColorArtifact fogDisabled;

            /// <summary>Stores the fog-enabled center readback.</summary>
            public ConsumerColorArtifact fogEnabled;

            /// <summary>Stores the fog-disabled RGB magnitude.</summary>
            public float fogDisabledRgbMagnitude;

            /// <summary>Stores the fog-enabled RGB magnitude.</summary>
            public float fogEnabledRgbMagnitude;

            /// <summary>Stores the fog-enabled to fog-disabled RGB magnitude ratio.</summary>
            public float retainedSignalFraction;

            /// <summary>Stores whether RenderSettings fog remained enabled for the fog-on draw.</summary>
            public bool renderSettingsFogEnabled;

            /// <summary>Stores whether the global exponential fog keyword remained enabled for the fog-on draw.</summary>
            public bool globalExponentialFogKeywordEnabled;

            /// <summary>Stores whether the actual material exponential fog keyword remained enabled for the fog-on draw.</summary>
            public bool materialExponentialFogKeywordEnabled;

            /// <summary>Stores the observed BIRP fog parameter y component for the fog-on draw.</summary>
            public float fogParametersY;
        }

        /// <summary>Stores one JSON-serializable HDR color observation.</summary>
        [Serializable]
        private sealed class ConsumerColorArtifact
        {
            /// <summary>Stores the red component.</summary>
            public float red;

            /// <summary>Stores the green component.</summary>
            public float green;

            /// <summary>Stores the blue component.</summary>
            public float blue;

            /// <summary>Stores the alpha component.</summary>
            public float alpha;

            /// <summary>Creates a serializable color artifact from one HDR color.</summary>
            /// <param name="color">The HDR color to store.</param>
            /// <returns>The serialized color artifact.</returns>
            public static ConsumerColorArtifact FromColor(Color color)
            {
                return new ConsumerColorArtifact { red = color.r, green = color.g, blue = color.b, alpha = color.a };
            }
        }
    }
}