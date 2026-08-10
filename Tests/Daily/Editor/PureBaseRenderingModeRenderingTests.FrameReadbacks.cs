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

// Defines numeric alpha and depth observations that render and read transient frames.

using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    public sealed partial class PureBaseRenderingModeRenderingTests
    {
        /// <summary>Defines finite, threshold, and numeric alpha metrics for the future BIRP mode rendering observations.</summary>
        [Test]
        public void NumericObservationMetricsRejectOpaqueAlphaLeakCutoutLeakAndTransparentDepthOrAddAlphaErrors()
        {
            Shader shader = RequireProductShader("PureBase/Unlit");
            var opaque = CreateConfiguredMaterial(shader, 0, new Color(0.8f, 0.2f, 0.1f, 0.1f));
            var cutoutBelow = CreateConfiguredMaterial(
                shader,
                1,
                new Color(0.8f, 0.2f, 0.1f, 0.25f)
            );
            var transparent = CreateConfiguredMaterial(
                shader,
                2,
                new Color(0.8f, 0.2f, 0.1f, 0.25f)
            );
            {
                RequireRenderingModeProperty(opaque);
                Color opaquePixel = RenderCenterPixel(opaque, Color.clear);
                Color cutoutPixel = RenderCenterPixel(cutoutBelow, Color.clear);
                Color transparentPixel = RenderCenterPixel(transparent, Color.clear);
                AssertFinite(opaquePixel, "Opaque readback");
                AssertFinite(cutoutPixel, "Cutout readback");
                AssertFinite(transparentPixel, "Transparent readback");
                Assert.That(
                    opaquePixel.a,
                    Is.GreaterThan(0.95f),
                    "Opaque output must ignore base alpha."
                );
                Assert.That(
                    cutoutPixel.a,
                    Is.LessThan(0.02f),
                    "Cutout coverage below _Cutoff must not contribute."
                );
                Assert.That(
                    transparentPixel.a,
                    Is.EqualTo(0.0625f).Within(0.005f),
                    "Transparent source alpha 0.25 over clear destination alpha 0 must use standard alpha blending: 0.25 * 0.25."
                );
            }
        }

        /// <summary>Requires Transparent material sorting to produce the expected finite back-to-front two-layer readback without depth writes.</summary>
        [Test]
        public void TransparentDepthOrderingUsesBackToFrontCompositionWithoutDepthWrite()
        {
            Shader shader = RequireProductShader("PureBase/Unlit");
            var red = CreateConfiguredMaterial(shader, 2, new Color(1.0f, 0.0f, 0.0f, 0.25f));
            var blue = CreateConfiguredMaterial(shader, 2, new Color(0.0f, 0.0f, 1.0f, 0.25f));
            {
                Color redInFront = RenderLayeredCenterPixel(red, blue);
                Color blueInFront = RenderLayeredCenterPixel(blue, red);
                AssertFinite(redInFront, "Red-front transparent depth readback");
                AssertFinite(blueInFront, "Blue-front transparent depth readback");
                Assert.That(redInFront.r, Is.EqualTo(0.25f).Within(0.05f));
                Assert.That(redInFront.b, Is.EqualTo(0.1875f).Within(0.05f));
                Assert.That(blueInFront.b, Is.EqualTo(0.25f).Within(0.05f));
                Assert.That(blueInFront.r, Is.EqualTo(0.1875f).Within(0.05f));
                Assert.That(redInFront.r, Is.GreaterThan(redInFront.b + 0.02f));
                Assert.That(blueInFront.b, Is.GreaterThan(blueInFront.r + 0.02f));
            }
        }

        /// <summary>Requires Transparent ForwardBase to leave depth unchanged so an explicitly later opaque marker behind it remains visible.</summary>
        [Test]
        public void TransparentDepthWriteDoesNotOccludeAnExplicitlyLaterOpaqueMarker()
        {
            Shader transparentShader = RequireProductShader("PureBase/Unlit");
            Shader markerShader = Shader.Find("Unlit/Color");
            Assert.That(
                markerShader,
                Is.Not.Null,
                "The Built-in Unlit/Color shader is unavailable for the Transparent depth-write probe."
            );
            var transparent = CreateConfiguredMaterial(
                transparentShader,
                2,
                new Color(1.0f, 0.0f, 0.0f, 0.25f)
            );
            var marker = CreateMaterial(markerShader);
            {
                marker.SetColor("_Color", Color.green);
                Color observed = RenderTransparentThenOpaqueDepthProbe(transparent, marker);
                AssertFinite(observed, "Transparent explicit-depth probe readback");
                Assert.That(
                    observed.g,
                    Is.GreaterThan(0.85f),
                    "Transparent ZWrite Off must allow the explicitly later opaque marker behind the transparent surface to pass depth."
                );
                Assert.That(
                    observed.r,
                    Is.LessThan(0.08f),
                    "The later opaque marker must replace the transparent probe color when Transparent does not write depth."
                );
            }
        }

        /// <summary>Renders an isolated directional-light fixture with and without shadows and returns the measured receiver silhouette.</summary>
        /// <param name="material">The configured material assigned to the shadow caster.</param>
        /// <returns>The controlled actual ShadowCaster readback.</returns>
        private static ShadowReadback RenderShadowReadback(Material material)
        {
            var fixture = new ShadowReadbackFixture();
            try
            {
                fixture.Initialize(material);
                return fixture.Render();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        /// <summary>Owns the temporary preview-scene resources for one ShadowCaster readback.</summary>
        private sealed class ShadowReadbackFixture : System.IDisposable
        {
            private const int FixtureLayer = 31;
            private Scene scene;
            private GameObject cameraObject;
            private GameObject lightObject;
            private GameObject receiver;
            private GameObject caster;
            private Material receiverMaterial;
            private RenderTexture renderTexture;
            private Texture2D texture;

            /// <summary>Initializes an allocation-free ShadowCaster readback fixture.</summary>
            public ShadowReadbackFixture() { }

            /// <summary>Allocates and configures the ShadowCaster readback fixture.</summary>
            /// <param name="material">The material assigned to the caster.</param>
            public void Initialize(Material material)
            {
                scene = EditorSceneManager.NewPreviewScene();
                CreateResources();
                MoveObjectsToFixtureScene();
                ConfigureCamera();
                ConfigureLight();
                ConfigureReceiver();
                ConfigureCaster(material);
                renderTexture.Create();
            }

            /// <summary>Captures the receiver with shadows disabled and enabled.</summary>
            /// <returns>The measured ShadowCaster silhouette.</returns>
            public ShadowReadback Render()
            {
                Camera camera = cameraObject.GetComponent<Camera>();
                Light light = lightObject.GetComponent<Light>();
                light.shadows = LightShadows.None;
                camera.Render();
                Color[] withoutShadows = ReadPixels(renderTexture, texture);
                light.shadows = LightShadows.Hard;
                camera.Render();
                Color[] withShadows = ReadPixels(renderTexture, texture);
                return AnalyzeShadowReadback(withoutShadows, withShadows);
            }

            /// <summary>Releases every preview-scene resource in its original ownership order.</summary>
            public void Dispose()
            {
                Camera camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
                if (camera != null)
                    camera.targetTexture = null;
                if (texture != null)
                    UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture != null)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                if (receiverMaterial != null)
                    UnityEngine.Object.DestroyImmediate(receiverMaterial);
                if (caster != null)
                    UnityEngine.Object.DestroyImmediate(caster);
                if (receiver != null)
                    UnityEngine.Object.DestroyImmediate(receiver);
                if (lightObject != null)
                    UnityEngine.Object.DestroyImmediate(lightObject);
                if (cameraObject != null)
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.ClosePreviewScene(scene);
            }

            /// <summary>Allocates every temporary Unity resource used by the fixture.</summary>
            private void CreateResources()
            {
                cameraObject = new GameObject("PureBaseRenderingModeShadowCamera");
                lightObject = new GameObject("PureBaseRenderingModeShadowLight");
                receiver = GameObject.CreatePrimitive(PrimitiveType.Plane);
                caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Shader receiverShader = Shader.Find("Standard");
                Assert.That(
                    receiverShader,
                    Is.Not.Null,
                    "The Built-in Standard shader is unavailable for ShadowCaster readback."
                );
                receiverMaterial = new Material(receiverShader);
                renderTexture = new RenderTexture(
                    RenderSize,
                    RenderSize,
                    24,
                    RenderTextureFormat.ARGBFloat
                );
                texture = new Texture2D(
                    RenderSize,
                    RenderSize,
                    TextureFormat.RGBAFloat,
                    false,
                    true
                );
            }

            /// <summary>Moves every fixture object into the isolated preview scene and layer.</summary>
            private void MoveObjectsToFixtureScene()
            {
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                SceneManager.MoveGameObjectToScene(lightObject, scene);
                SceneManager.MoveGameObjectToScene(receiver, scene);
                SceneManager.MoveGameObjectToScene(caster, scene);
                cameraObject.layer = FixtureLayer;
                lightObject.layer = FixtureLayer;
                receiver.layer = FixtureLayer;
                caster.layer = FixtureLayer;
            }

            /// <summary>Configures the isolated ShadowCaster camera.</summary>
            private void ConfigureCamera()
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << FixtureLayer;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1.0f);
                camera.transform.position = new Vector3(0.0f, 3.0f, -7.0f);
                camera.transform.LookAt(new Vector3(0.0f, 0.5f, 0.0f));
                camera.fieldOfView = 45.0f;
                camera.targetTexture = renderTexture;
            }

            /// <summary>Configures the directional light used by the ShadowCaster fixture.</summary>
            private void ConfigureLight()
            {
                Light light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.5f;
                light.cullingMask = 1 << FixtureLayer;
                light.shadows = LightShadows.Hard;
                lightObject.transform.rotation = Quaternion.Euler(55.0f, -35.0f, 0.0f);
            }

            /// <summary>Configures the receiver plane for directional-shadow measurements.</summary>
            private void ConfigureReceiver()
            {
                receiver.transform.localScale = Vector3.one * 0.8f;
                receiver.GetComponent<MeshRenderer>().sharedMaterial = receiverMaterial;
            }

            /// <summary>Configures the measured caster with its effective ShadowCaster state.</summary>
            /// <param name="material">The source material.</param>
            private void ConfigureCaster(Material material)
            {
                caster.transform.position = new Vector3(0.0f, 1.0f, 0.0f);
                MeshRenderer casterRenderer = caster.GetComponent<MeshRenderer>();
                casterRenderer.sharedMaterial = material;
                casterRenderer.shadowCastingMode = material.GetShaderPassEnabled("ShadowCaster")
                    ? ShadowCastingMode.ShadowsOnly
                    : ShadowCastingMode.Off;
            }
        }

        /// <summary>Renders the actual Meta pass into a linear target and returns its center pixel without changing persistent assets.</summary>
        /// <param name="material">The configured source material.</param>
        /// <returns>The linear Meta center readback.</returns>
        private static Color RenderMetaCenterPixel(Material material)
        {
            MetaGlobalState globalState = MetaGlobalState.Capture();
            GameObject cameraObject = null;
            GameObject quadObject = null;
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            CommandBuffer commandBuffer = null;
            try
            {
                return RenderMetaReadback(
                    material,
                    out cameraObject,
                    out quadObject,
                    out renderTexture,
                    out texture,
                    out commandBuffer
                );
            }
            finally
            {
                globalState.Restore();
                ReleaseMetaReadbackResources(
                    cameraObject,
                    quadObject,
                    renderTexture,
                    texture,
                    commandBuffer
                );
            }
        }

        /// <summary>Creates and executes the actual Meta-pass command-buffer readback.</summary>
        private static Color RenderMetaReadback(
            Material material,
            out GameObject cameraObject,
            out GameObject quadObject,
            out RenderTexture renderTexture,
            out Texture2D texture,
            out CommandBuffer commandBuffer
        )
        {
            cameraObject = new GameObject("PureBaseRenderingModeMetaCamera");
            quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            renderTexture = new RenderTexture(
                RenderSize,
                RenderSize,
                24,
                RenderTextureFormat.ARGBFloat
            );
            texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBAFloat, false, true);
            commandBuffer = new CommandBuffer { name = "PureBase Rendering Mode Meta Readback" };
            int pass = material.FindPass("Meta");
            Assert.That(
                pass,
                Is.GreaterThanOrEqualTo(0),
                "The material must expose an actual Meta pass."
            );
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.cullingMask = 0;
            camera.orthographic = true;
            camera.orthographicSize = 1.0f;
            camera.transform.position = new Vector3(0.0f, 0.0f, -5.0f);
            camera.targetTexture = renderTexture;
            renderTexture.Create();
            ApplyMetaGlobals();
            commandBuffer.SetRenderTarget(renderTexture);
            commandBuffer.ClearRenderTarget(true, true, Color.clear);
            if (material.GetShaderPassEnabled("Meta"))
                commandBuffer.DrawMesh(
                    quadObject.GetComponent<MeshFilter>().sharedMesh,
                    Matrix4x4.identity,
                    material,
                    0,
                    pass
                );
            camera.AddCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
            camera.Render();
            return ReadCenterPixel(renderTexture, texture);
        }

        /// <summary>Sets the Meta pass globals required for the controlled albedo readback.</summary>
        private static void ApplyMetaGlobals()
        {
            Shader.SetGlobalVector("unity_MetaVertexControl", new Vector4(1.0f, 0.0f, 0.0f, 0.0f));
            Shader.SetGlobalVector(
                "unity_MetaFragmentControl",
                new Vector4(1.0f, 0.0f, 0.0f, 0.0f)
            );
            Shader.SetGlobalVector("unity_LightmapST", new Vector4(1.0f, 1.0f, 0.0f, 0.0f));
            Shader.SetGlobalFloat("unity_OneOverOutputBoost", 1.0f);
            Shader.SetGlobalFloat("unity_MaxOutputValue", 1.0f);
        }

        /// <summary>Releases the Meta command buffer and transient rendering resources.</summary>
        private static void ReleaseMetaReadbackResources(
            GameObject cameraObject,
            GameObject quadObject,
            RenderTexture renderTexture,
            Texture2D texture,
            CommandBuffer commandBuffer
        )
        {
            Camera camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
            if (camera != null && commandBuffer != null)
                camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, commandBuffer);
            if (commandBuffer != null)
                commandBuffer.Release();
            ReleaseQuadReadbackResources(cameraObject, quadObject, camera, renderTexture, texture);
        }

        /// <summary>Captures the global state modified by one Meta-pass readback.</summary>
        private sealed class MetaGlobalState
        {
            private readonly Vector4 vertexControl;
            private readonly Vector4 fragmentControl;
            private readonly Vector4 lightmapSt;
            private readonly float outputBoost;
            private readonly float maxOutput;

            public MetaGlobalState(
                Vector4 vertexControl,
                Vector4 fragmentControl,
                Vector4 lightmapSt,
                float outputBoost,
                float maxOutput
            )
            {
                this.vertexControl = vertexControl;
                this.fragmentControl = fragmentControl;
                this.lightmapSt = lightmapSt;
                this.outputBoost = outputBoost;
                this.maxOutput = maxOutput;
            }

            /// <summary>Captures the current Meta globals before the temporary readback mutates them.</summary>
            /// <returns>The state to restore.</returns>
            public static MetaGlobalState Capture()
            {
                return new MetaGlobalState(
                    Shader.GetGlobalVector("unity_MetaVertexControl"),
                    Shader.GetGlobalVector("unity_MetaFragmentControl"),
                    Shader.GetGlobalVector("unity_LightmapST"),
                    Shader.GetGlobalFloat("unity_OneOverOutputBoost"),
                    Shader.GetGlobalFloat("unity_MaxOutputValue")
                );
            }

            /// <summary>Restores the Meta globals in their original mutation order.</summary>
            public void Restore()
            {
                Shader.SetGlobalVector("unity_MetaVertexControl", vertexControl);
                Shader.SetGlobalVector("unity_MetaFragmentControl", fragmentControl);
                Shader.SetGlobalVector("unity_LightmapST", lightmapSt);
                Shader.SetGlobalFloat("unity_OneOverOutputBoost", outputBoost);
                Shader.SetGlobalFloat("unity_MaxOutputValue", maxOutput);
            }
        }

        /// <summary>Reads the center pixel of an already-rendered target without changing the active render target after completion.</summary>
        /// <param name="renderTexture">The source render target.</param>
        /// <param name="texture">The transient readback texture.</param>
        /// <returns>The center pixel.</returns>
        private static Color ReadCenterPixel(RenderTexture renderTexture, Texture2D texture)
        {
            ReadPixels(renderTexture, texture);
            return texture.GetPixel(RenderSize / 2, RenderSize / 2);
        }

        /// <summary>Reads every pixel from one target while restoring the previous active render target.</summary>
        /// <param name="renderTexture">The source render target.</param>
        /// <param name="texture">The transient readback texture.</param>
        /// <returns>The copied target pixels.</returns>
        private static Color[] ReadPixels(RenderTexture renderTexture, Texture2D texture)
        {
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, RenderSize, RenderSize), 0, 0);
                texture.Apply(false, false);
                return texture.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        /// <summary>Measures the maximum RGB delta and changed-pixel count caused by directional shadows.</summary>
        /// <param name="withoutShadows">The unshadowed readback pixels.</param>
        /// <param name="withShadows">The shadowed readback pixels.</param>
        /// <returns>The measured directional-shadow silhouette.</returns>
        private static ShadowReadback AnalyzeShadowReadback(
            Color[] withoutShadows,
            Color[] withShadows
        )
        {
            Assert.That(
                withShadows.Length,
                Is.EqualTo(withoutShadows.Length),
                "Directional-shadow readbacks must have matching dimensions."
            );
            var changedPixelCount = 0;
            var maxAbsoluteRgbDelta = 0.0f;
            for (int index = 0; index < withoutShadows.Length; index++)
            {
                Color delta = withoutShadows[index] - withShadows[index];
                float maximumAbsoluteDelta = Mathf.Max(
                    Mathf.Abs(delta.r),
                    Mathf.Max(Mathf.Abs(delta.g), Mathf.Abs(delta.b))
                );
                if (maximumAbsoluteDelta > ShadowPixelNoiseThreshold)
                    changedPixelCount++;
                if (maximumAbsoluteDelta > maxAbsoluteRgbDelta)
                    maxAbsoluteRgbDelta = maximumAbsoluteDelta;
            }

            return new ShadowReadback(maxAbsoluteRgbDelta, changedPixelCount);
        }
    }
}
