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

// Owns the isolated Toon lighting runtime capture fixture and Unity-state restoration.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Identifies the saved owner scene used by Daily regular additive shadow captures.</summary>
        private const string ShadowCaptureOwnerScenePath = "Assets/Pure-Base.unity";

        /// <summary>Warms representative ForwardAdd variants for every Unity light kind and shadow keyword form.</summary>
        /// <returns>The number of individually warmed nonpersistent variants.</returns>
        private static int WarmAllLightKindVariants()
        {
            var requests = new[]
            {
                new LightVariantRequest("ForwardBase Baseline", PassType.ForwardBase, Array.Empty<string>()),
                new LightVariantRequest("ForwardBase Opaque", PassType.ForwardBase, new[] { "PUREBASE_RENDERING_OPAQUE" }),
                new LightVariantRequest("ForwardBase Transparent", PassType.ForwardBase, new[] { "PUREBASE_RENDERING_TRANSPARENT" }),
                new LightVariantRequest("ForwardBase Screen Shadow", PassType.ForwardBase, new[] { "SHADOWS_SCREEN" }),
                new LightVariantRequest("Directional ForwardAdd", PassType.ForwardAdd, new[] { "DIRECTIONAL" }),
                new LightVariantRequest("Directional Cookie ForwardAdd", PassType.ForwardAdd, new[] { "DIRECTIONAL_COOKIE" }),
                new LightVariantRequest("Point ForwardAdd", PassType.ForwardAdd, new[] { "POINT" }),
                new LightVariantRequest("Point Cookie ForwardAdd", PassType.ForwardAdd, new[] { "POINT_COOKIE" }),
                new LightVariantRequest("Spot ForwardAdd", PassType.ForwardAdd, new[] { "SPOT" }),
                new LightVariantRequest("Directional Depth Shadow ForwardAdd", PassType.ForwardAdd, new[] { "DIRECTIONAL", "SHADOWS_DEPTH" }),
                new LightVariantRequest("Directional Cookie Depth Shadow ForwardAdd", PassType.ForwardAdd, new[] { "DIRECTIONAL_COOKIE", "SHADOWS_DEPTH" }),
                new LightVariantRequest("Point Cube Shadow ForwardAdd", PassType.ForwardAdd, new[] { "POINT", "SHADOWS_CUBE" }),
                new LightVariantRequest("Point Cookie Cube Shadow ForwardAdd", PassType.ForwardAdd, new[] { "POINT_COOKIE", "SHADOWS_CUBE" }),
                new LightVariantRequest("Spot Depth Shadow ForwardAdd", PassType.ForwardAdd, new[] { "SPOT", "SHADOWS_DEPTH" }),
            };
            var warmedCount = 0;
            foreach (string shaderName in new[] { "PureBase/Unlit", "PureBase/Toon", "PureBase/PBR", "PureBase/Hybrid" })
            {
                Shader shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, "Product shader '" + shaderName + "' is unavailable.");
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, "Product shader '" + shaderName + "' has compiler errors.");
                foreach (LightVariantRequest request in requests)
                {
                    var variants = new ShaderVariantCollection();
                    try
                    {
                        Assert.That(
                            variants.Add(new ShaderVariantCollection.ShaderVariant(shader, request.passType, request.keywords)),
                            Is.True,
                            "The " + request.label + " variant could not be added for '" + shaderName + "'."
                        );
                        variants.WarmUp();
                        Assert.That(variants.variantCount, Is.EqualTo(1));
                        warmedCount++;
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(variants);
                    }
                }
            }

            return warmedCount;
        }

        /// <summary>Stores one representative transient product-pass variant request.</summary>
        private sealed class LightVariantRequest
        {
            /// <summary>Initializes one variant request.</summary>
            /// <param name="label">The diagnostic light-kind label.</param>
            /// <param name="passType">The product pass that must compile the variant.</param>
            /// <param name="keywords">The exact enabled Unity variant keywords.</param>
            public LightVariantRequest(string label, PassType passType, string[] keywords)
            {
                this.label = label;
                this.passType = passType;
                this.keywords = keywords;
            }

            /// <summary>Gets the diagnostic light-kind label.</summary>
            public string label { get; }

            /// <summary>Gets the product pass that must compile the variant.</summary>
            public PassType passType { get; }

            /// <summary>Gets the exact enabled Unity variant keywords.</summary>
            public string[] keywords { get; }
        }

        /// <summary>Groups the inputs for one isolated Unity light readback.</summary>
        private sealed class LightCaptureRequest
        {
            /// <summary>Initializes one light capture request with an optional caller-owned transient cookie.</summary>
            /// <param name="cookie">The caller-owned cookie to apply to transient Unity lights, if any.</param>
            public LightCaptureRequest(Texture cookie = null)
            {
                this.cookie = cookie;
            }

            /// <summary>Gets or sets the uniform mesh world normal.</summary>
            public Vector3 normal { get; set; }

            /// <summary>Gets or sets the real main or additional light color.</summary>
            public Vector4 lightColor { get; set; }

            /// <summary>Gets or sets the real directional or local-light position.</summary>
            public Vector4 lightPosition { get; set; }

            /// <summary>Gets or sets the spherical-harmonic globals for the render.</summary>
            public ShCoefficients coefficients { get; set; }

            /// <summary>Gets or sets the real Unity light type.</summary>
            public LightType lightType { get; set; }

            /// <summary>Gets or sets the number of ForcePixel lights to create.</summary>
            public int lightCount { get; set; }

            /// <summary>Gets or sets the Point or Spot light range.</summary>
            public float range { get; set; } = 4.0f;

            /// <summary>Gets or sets the Spot outer angle.</summary>
            public float spotAngle { get; set; } = 30.0f;

            /// <summary>Gets the optional caller-owned transient cookie.</summary>
            public Texture cookie { get; }
        }

        /// <summary>Owns one isolated regular-render fixture and restores every Unity global it changes.</summary>
        private class ToonLightingCaptureRuntimeScope : IDisposable
        {
            /// <summary>Stores the dedicated layer used by the preview-scene renderer and lights.</summary>
            private const int FixtureLayer = 31;

            /// <summary>Owns transient objects and readback resources for one directional shadow receiver capture.</summary>
            private sealed class ShadowReceiverCapture
            {
                /// <summary>Stores the transient diagnostic camera object.</summary>
                public GameObject cameraObject;

                /// <summary>Stores the transient receiver object.</summary>
                public GameObject receiver;

                /// <summary>Stores the transient shadow caster object.</summary>
                public GameObject caster;

                /// <summary>Stores the transient directional-light object.</summary>
                public GameObject lightObject;

                /// <summary>Stores the transient linear receiver render target.</summary>
                public RenderTexture target;

                /// <summary>Stores the transient CPU receiver readback texture.</summary>
                public Texture2D readback;

                /// <summary>Stores the configured diagnostic camera.</summary>
                public Camera camera;
            }

            /// <summary>Lists the spherical-harmonic globals injected immediately before each render.</summary>
            private static readonly string[] GlobalNames =
            {
                "unity_SHAr",
                "unity_SHAg",
                "unity_SHAb",
                "unity_SHBr",
                "unity_SHBg",
                "unity_SHBb",
                "unity_SHC",
            };

            /// <summary>Stores the global vectors captured before the fixture writes spherical-harmonic input.</summary>
            private readonly Dictionary<string, Vector4> globals = new Dictionary<string, Vector4>();

            /// <summary>Stores caller-owned temporary material instances.</summary>
            private readonly List<Material> materials = new List<Material>();

            /// <summary>Stores the active render target before CPU readback changes it.</summary>
            private readonly RenderTexture activeRenderTexture;

            /// <summary>Stores the active scene before the Preview Scene becomes the capture context.</summary>
            private readonly Scene activeScene;

            /// <summary>Stores the original loaded scene count for restoration diagnostics.</summary>
            private readonly int sceneCount;

            /// <summary>Stores the original pixel-light budget.</summary>
            private readonly int pixelLightCount;

            /// <summary>Stores the caller's directional shadow quality.</summary>
            private readonly ShadowQuality shadowQuality;

            /// <summary>Stores the caller's directional shadow draw distance.</summary>
            private readonly float shadowDistance;

            /// <summary>Stores the original fog setting for the formerly active scene.</summary>
            private readonly bool fogEnabled;

            /// <summary>Stores the disposable Preview Scene that owns all generated GameObjects.</summary>
            private readonly Scene scene;

            /// <summary>Stores the isolated Forward-rendering camera.</summary>
            private Camera camera;

            /// <summary>Stores the renderer used for every regular product-material render.</summary>
            private MeshRenderer renderer;

            /// <summary>Stores the mesh filter used for controlled-normal render meshes.</summary>
            private MeshFilter meshFilter;

            /// <summary>Tracks completed renderer meshes for deterministic scope cleanup.</summary>
            private readonly List<Mesh> meshes = new List<Mesh>();

            /// <summary>Tracks camera and renderer GameObjects for deterministic preview-scene cleanup.</summary>
            private readonly List<GameObject> gameObjects = new List<GameObject>();

            /// <summary>Stores the linear float render target.</summary>
            private RenderTexture target;

            /// <summary>Stores the float CPU readback texture.</summary>
            private Texture2D readback;

            /// <summary>Stores the controlled linear normal texture used by every product-material render.</summary>
            private Texture2D normalMap;

            /// <summary>Stores the command buffer that injects only SH globals immediately before every render.</summary>
            private CommandBuffer commandBuffer;

            /// <summary>Stores the renderer-local SH override that wins over Unity's per-object probe setup.</summary>
            private MaterialPropertyBlock shProperties;

            /// <summary>Tracks whether this scope has already released its resources.</summary>
            private bool disposed;

            /// <summary>Creates an isolated linear render fixture with no fog or reflection probes.</summary>
            public ToonLightingCaptureRuntimeScope()
            {
                activeRenderTexture = RenderTexture.active;
                activeScene = SceneManager.GetActiveScene();
                sceneCount = SceneManager.sceneCount;
                pixelLightCount = QualitySettings.pixelLightCount;
                shadowQuality = QualitySettings.shadows;
                shadowDistance = QualitySettings.shadowDistance;
                fogEnabled = RenderSettings.fog;
                foreach (string globalName in GlobalNames)
                {
                    globals.Add(globalName, Shader.GetGlobalVector(globalName));
                }

                try
                {
                    scene = EditorSceneManager.NewPreviewScene();
                    RenderSettings.fog = false;
                    QualitySettings.pixelLightCount = Mathf.Max(2, pixelLightCount);
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowDistance = Mathf.Max(32.0f, shadowDistance);

                    InitializeRenderResources();
                }
                catch
                {
                    ReleaseRenderResources();
                    ClosePreviewScene();
                    RestoreCallerState();
                    throw;
                }
            }

            /// <summary>Creates the hidden preview-scene objects, transient render resources, and camera configuration.</summary>
            private void InitializeRenderResources()
            {
                    GameObject cameraObject = CreateHiddenObject("PureBase Toon Lighting Contract Camera");
                    camera = cameraObject.AddComponent<Camera>();
                    GameObject quadObject = CreateHiddenObject("PureBase Toon Lighting Contract Renderer");
                    meshFilter = quadObject.AddComponent<MeshFilter>();
                    renderer = quadObject.AddComponent<MeshRenderer>();
                    renderer.enabled = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    target = new RenderTexture(
                        64,
                        64,
                        24,
                        RenderTextureFormat.ARGBFloat,
                        RenderTextureReadWrite.Linear
                    ) { hideFlags = HideFlags.HideAndDontSave };
                    target.Create();
                    readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    normalMap = CreateNeutralNormalTexture();
                    commandBuffer = new CommandBuffer { name = "PureBase Toon Lighting Contract SH" };
                    shProperties = new MaterialPropertyBlock();
                    camera.enabled = false;
                    camera.cullingMask = 1 << FixtureLayer;
                    camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
                    camera.orthographic = true;
                    camera.orthographicSize = 1.0f;
                    camera.nearClipPlane = 0.1f;
                    camera.farClipPlane = 10.0f;
                    camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.37f);
                    camera.renderingPath = RenderingPath.Forward;
                    camera.targetTexture = target;
                    camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);
            }

            /// <summary>Renders one product material through BIRP after installing the exact SH globals for the current test case.</summary>
            /// <param name="shaderName">The required product shader name.</param>
            /// <param name="passName">The required product pass name.</param>
            /// <param name="normal">The uniform mesh world normal.</param>
            /// <param name="lightColor">The real main or additional light color.</param>
            /// <param name="lightPosition">The real directional or point light vector.</param>
            /// <param name="coefficients">The seven SH globals injected immediately before the render.</param>
            /// <param name="pointLight">Whether to isolate a Point ForwardAdd contribution with one- versus two-light rendering.</param>
            /// <param name="metallic">The material metallic value.</param>
            /// <returns>The center linear float readback color.</returns>
            public Color Render(
                string shaderName,
                string passName,
                Vector3 normal,
                Vector4 lightColor,
                Vector4 lightPosition,
                ShCoefficients coefficients,
                bool pointLight = false,
                float metallic = 0.0f
            )
            {
                Material material = CreateProductMaterial(shaderName, passName, metallic);
                if (pointLight)
                {
                    return RenderLightDifference(
                        material,
                        CreateLightCaptureRequest(
                            normal,
                            lightColor,
                            lightPosition,
                            coefficients,
                            LightType.Point
                        )
                    );
                }

                return RenderWithLights(
                    material,
                    CreateDirectionalLightCaptureRequest(
                        normal,
                        lightColor,
                        lightPosition,
                        coefficients
                    )
                );
            }

            /// <summary>Renders one direct or additional light with an optional transient Unity cookie.</summary>
            /// <param name="shaderName">The required product shader name.</param>
            /// <param name="passName">The product pass that receives the light.</param>
            /// <param name="request">The coherent light and cookie input for the capture.</param>
            /// <returns>The center linear readback.</returns>
            public Color RenderLightWithCookie(string shaderName, string passName, LightCaptureRequest request)
            {
                Material material = CreateProductMaterial(shaderName, passName, 0.0f);
                return RenderWithLights(
                    material,
                    request
                );
            }

            /// <summary>Renders a shadowed horizontal receiver and returns a whole-region RGB observation.</summary>
            /// <param name="shaderName">The imported product or fixed host shader name.</param>
            /// <param name="shadows">The requested real Unity directional shadow mode.</param>
            /// <returns>The visible receiver region's mean RGB and sample count.</returns>
            public ShadowReceiverObservation RenderDirectionalShadowReceiver(string shaderName, LightShadows shadows)
            {
                Material material = CreateProductMaterial(shaderName, "ForwardBase", 0.0f);
                Scene receiverScene = GetShadowReceiverScene(out bool receiverSceneWasLoaded);
                var capture = new ShadowReceiverCapture();
                try
                {
                    ConfigureShadowReceiverCapture(capture, receiverScene, material, shadows);
                    return ReadShadowReceiverObservation(capture);
                }
                finally
                {
                    DestroyShadowReceiverCapture(capture);
                    RestoreShadowReceiverScene(receiverScene, receiverSceneWasLoaded);
                }
            }

            /// <summary>Gets the existing shadow-owner scene or opens it additively for one capture.</summary>
            /// <param name="receiverSceneWasLoaded">Receives whether the owner scene was already loaded.</param>
            /// <returns>The shadow-owner scene.</returns>
            private static Scene GetShadowReceiverScene(out bool receiverSceneWasLoaded)
            {
                Scene receiverScene = SceneManager.GetSceneByPath(ShadowCaptureOwnerScenePath);
                receiverSceneWasLoaded = receiverScene.isLoaded;
                if (!receiverSceneWasLoaded)
                {
                    receiverScene = EditorSceneManager.OpenScene(
                        ShadowCaptureOwnerScenePath,
                        OpenSceneMode.Additive
                    );
                    EditorSceneManager.SetSceneCullingMask(
                        receiverScene,
                        EditorSceneManager.CalculateAvailableSceneCullingMask()
                    );
                }

                return receiverScene;
            }

            /// <summary>Allocates and configures the complete directional shadow receiver capture.</summary>
            /// <param name="capture">Owns every allocated capture resource from its first allocation.</param>
            /// <param name="receiverScene">The scene that owns all generated capture objects.</param>
            /// <param name="material">The registered product material applied to the receiver.</param>
            /// <param name="shadows">The requested Unity directional shadow mode.</param>
            private void ConfigureShadowReceiverCapture(
                ShadowReceiverCapture capture,
                Scene receiverScene,
                Material material,
                LightShadows shadows
            )
            {
                CreateShadowReceiverResources(capture, receiverScene);
                ConfigureShadowReceiverCamera(capture, receiverScene);
                ConfigureShadowReceiverGeometry(capture, material);
                ConfigureShadowReceiverLight(capture, shadows);
            }

            /// <summary>Allocates and immediately registers all transient objects and readback resources for a receiver capture.</summary>
            /// <param name="capture">The capture that owns every allocated resource.</param>
            /// <param name="receiverScene">The scene that owns all generated capture objects.</param>
            private static void CreateShadowReceiverResources(
                ShadowReceiverCapture capture,
                Scene receiverScene
            )
            {
                capture.cameraObject = CreateShadowSceneObject(
                    receiverScene,
                    "PureBase Toon Shadow Diagnostic Camera"
                );
                capture.receiver = CreateShadowSceneObject(
                    receiverScene,
                    "PureBase Toon Shadow Diagnostic Receiver"
                );
                capture.caster = CreateShadowSceneObject(
                    receiverScene,
                    "PureBase Toon Shadow Diagnostic Caster"
                );
                capture.lightObject = CreateShadowSceneObject(
                    receiverScene,
                    "PureBase Toon Shadow Diagnostic Light"
                );
                capture.target = new RenderTexture(
                    64,
                    64,
                    24,
                    RenderTextureFormat.ARGBFloat,
                    RenderTextureReadWrite.Linear
                ) { hideFlags = HideFlags.HideAndDontSave };
                capture.target.Create();
                capture.readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }

            /// <summary>Configures the receiver camera after its target and readback resources are registered.</summary>
            /// <param name="capture">The capture whose diagnostic camera is configured.</param>
            /// <param name="receiverScene">The scene isolated by the camera culling mask.</param>
            private static void ConfigureShadowReceiverCamera(
                ShadowReceiverCapture capture,
                Scene receiverScene
            )
            {
                capture.camera = capture.cameraObject.AddComponent<Camera>();
                capture.camera.enabled = false;
                capture.camera.cullingMask = 1 << FixtureLayer;
                capture.camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(
                    receiverScene
                );
                capture.camera.clearFlags = CameraClearFlags.SolidColor;
                capture.camera.backgroundColor = new Color(0.0f, 0.0f, 0.0f, 0.0f);
                capture.camera.fieldOfView = 42.0f;
                capture.camera.nearClipPlane = 0.1f;
                capture.camera.farClipPlane = 20.0f;
                capture.camera.transform.position = new Vector3(0.0f, 3.6f, -5.0f);
                capture.camera.transform.LookAt(Vector3.zero);
                capture.camera.targetTexture = capture.target;
            }

            /// <summary>Configures the receiver and caster geometry for one directional shadow capture.</summary>
            /// <param name="capture">The capture whose receiver and caster are configured.</param>
            /// <param name="material">The registered product material applied to the receiver.</param>
            private void ConfigureShadowReceiverGeometry(
                ShadowReceiverCapture capture,
                Material material
            )
            {
                MeshRenderer receiverRenderer = capture.receiver.AddComponent<MeshRenderer>();
                capture.receiver.AddComponent<MeshFilter>().sharedMesh = CreateShadowReceiverMesh();
                receiverRenderer.sharedMaterial = material;
                receiverRenderer.receiveShadows = true;
                capture.caster.transform.position = new Vector3(0.0f, 1.0f, 0.0f);
                capture.caster.transform.localScale = new Vector3(1.1f, 1.8f, 1.1f);
                MeshRenderer casterRenderer = capture.caster.AddComponent<MeshRenderer>();
                capture.caster.AddComponent<MeshFilter>().sharedMesh = CreateShadowCasterMesh();
                casterRenderer.sharedMaterial = CreateStandardShadowCasterMaterial();
                casterRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                casterRenderer.receiveShadows = false;
            }

            /// <summary>Configures the directional shadow-casting light for one receiver capture.</summary>
            /// <param name="capture">The capture whose directional light is configured.</param>
            /// <param name="shadows">The requested Unity directional shadow mode.</param>
            private static void ConfigureShadowReceiverLight(
                ShadowReceiverCapture capture,
                LightShadows shadows
            )
            {
                Light light = capture.lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.renderMode = LightRenderMode.ForcePixel;
                light.color = Color.white;
                light.intensity = 1.0f;
                light.cullingMask = 1 << FixtureLayer;
                light.shadows = shadows;
                capture.lightObject.transform.rotation = Quaternion.Euler(55.0f, -35.0f, 0.0f);
            }

            /// <summary>Renders a configured receiver capture and returns its visible-region observation.</summary>
            /// <param name="capture">The configured receiver capture.</param>
            /// <returns>The visible receiver-region measurement.</returns>
            private ShadowReceiverObservation ReadShadowReceiverObservation(ShadowReceiverCapture capture)
            {
                capture.camera.Render();
                return MeasureReceiverRegion(ReadPixels(capture.target, capture.readback));
            }

            /// <summary>Releases one receiver capture in the established render-target and object destruction order.</summary>
            /// <param name="capture">The capture whose owned transient resources are released.</param>
            private static void DestroyShadowReceiverCapture(ShadowReceiverCapture capture)
            {
                if (capture.readback != null)
                {
                    UnityEngine.Object.DestroyImmediate(capture.readback);
                }

                if (capture.target != null)
                {
                    capture.target.Release();
                    UnityEngine.Object.DestroyImmediate(capture.target);
                }

                DestroyShadowSceneObject(capture.lightObject);
                DestroyShadowSceneObject(capture.caster);
                DestroyShadowSceneObject(capture.receiver);
                DestroyShadowSceneObject(capture.cameraObject);
            }

            /// <summary>Restores the owner-scene load and active-scene state after a shadow receiver capture.</summary>
            /// <param name="receiverScene">The capture owner scene.</param>
            /// <param name="receiverSceneWasLoaded">Whether the owner scene preceded the capture.</param>
            private void RestoreShadowReceiverScene(Scene receiverScene, bool receiverSceneWasLoaded)
            {
                if (!receiverSceneWasLoaded && receiverScene.IsValid() && receiverScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(receiverScene, true);
                }

                if (activeScene.IsValid() && activeScene.isLoaded)
                {
                    SceneManager.SetActiveScene(activeScene);
                }
            }

            /// <summary>Renders an isolated Point or Spot ForwardAdd contribution without changing the caller's existing capture configuration.</summary>
            /// <param name="shaderName">The required product shader name.</param>
            /// <param name="normal">The uniform mesh world normal.</param>
            /// <param name="lightColor">The additional light color.</param>
            /// <param name="lightPosition">The additional light position.</param>
            /// <param name="lightType">The supported additional-light type.</param>
            /// <param name="range">The transient light range.</param>
            /// <param name="spotAngle">The transient Spot outer angle.</param>
            /// <returns>The isolated second additional-light contribution.</returns>
            public Color RenderAdditionalLight(
                string shaderName,
                Vector3 normal,
                Vector4 lightColor,
                Vector4 lightPosition,
                LightType lightType,
                float range,
                float spotAngle
            )
            {
                return RenderAdditionalLight(
                    shaderName,
                    normal,
                    lightColor,
                    lightPosition,
                    lightType,
                    range,
                    spotAngle,
                    ShCoefficients.Zero
                );
            }

            /// <summary>Renders an isolated Point or Spot ForwardAdd contribution with caller-controlled SH globals.</summary>
            /// <param name="shaderName">The required product shader name.</param>
            /// <param name="normal">The uniform mesh world normal.</param>
            /// <param name="lightColor">The additional light color.</param>
            /// <param name="lightPosition">The additional light position.</param>
            /// <param name="lightType">The supported additional-light type.</param>
            /// <param name="range">The transient light range.</param>
            /// <param name="spotAngle">The transient Spot outer angle.</param>
            /// <param name="coefficients">The SH globals installed only for this readback.</param>
            /// <returns>The isolated second additional-light contribution.</returns>
            public Color RenderAdditionalLight(
                string shaderName,
                Vector3 normal,
                Vector4 lightColor,
                Vector4 lightPosition,
                LightType lightType,
                float range,
                float spotAngle,
                ShCoefficients coefficients
            )
            {
                Assert.That(
                    lightType == LightType.Point || lightType == LightType.Spot,
                    Is.True,
                    "The additional-light capture supports only Point and Spot lights."
                );
                Material material = CreateProductMaterial(shaderName, "ForwardAdd", 0.0f);
                return RenderLightDifference(
                    material,
                    CreateLightCaptureRequest(
                        normal,
                        lightColor,
                        lightPosition,
                        coefficients,
                        lightType,
                        range,
                        spotAngle
                    )
                );
            }

            /// <summary>Creates one Point, Spot, or cookie-capable light capture request with no lights enabled yet.</summary>
            /// <param name="normal">The uniform mesh world normal.</param>
            /// <param name="lightColor">The light color.</param>
            /// <param name="lightPosition">The directional vector or local-light position.</param>
            /// <param name="coefficients">The spherical-harmonic globals for the render.</param>
            /// <param name="lightType">The Unity light type.</param>
            /// <param name="range">The Point or Spot light range.</param>
            /// <param name="spotAngle">The Spot outer angle.</param>
            /// <returns>The coherent light capture request.</returns>
            private static LightCaptureRequest CreateLightCaptureRequest(
                Vector3 normal,
                Vector4 lightColor,
                Vector4 lightPosition,
                ShCoefficients coefficients,
                LightType lightType,
                float range = 4.0f,
                float spotAngle = 30.0f
            )
            {
                return new LightCaptureRequest
                {
                    normal = normal,
                    lightColor = lightColor,
                    lightPosition = lightPosition,
                    coefficients = coefficients,
                    lightType = lightType,
                    range = range,
                    spotAngle = spotAngle,
                };
            }

            /// <summary>Creates one Directional light capture request with the established zero-light color control.</summary>
            /// <param name="normal">The uniform mesh world normal.</param>
            /// <param name="lightColor">The directional light color.</param>
            /// <param name="lightPosition">The directional light vector.</param>
            /// <param name="coefficients">The spherical-harmonic globals for the render.</param>
            /// <returns>The configured directional light capture request.</returns>
            private static LightCaptureRequest CreateDirectionalLightCaptureRequest(
                Vector3 normal,
                Vector4 lightColor,
                Vector4 lightPosition,
                ShCoefficients coefficients
            )
            {
                LightCaptureRequest request = CreateLightCaptureRequest(
                    normal,
                    lightColor,
                    lightPosition,
                    coefficients,
                    LightType.Directional
                );
                request.lightCount = lightColor == Vector4.zero ? 0 : 1;
                return request;
            }

            /// <summary>Renders one and two equivalent lights, returning only the isolated second-light contribution.</summary>
            /// <param name="material">The configured transient material.</param>
            /// <param name="request">The light capture request reused for one- and two-light rendering.</param>
            /// <returns>The isolated second-light contribution.</returns>
            private Color RenderLightDifference(Material material, LightCaptureRequest request)
            {
                request.lightCount = 1;
                Color oneLight = RenderWithLights(material, request);
                request.lightCount = 2;
                Color twoLights = RenderWithLights(material, request);
                return new Color(
                    twoLights.r - oneLight.r,
                    twoLights.g - oneLight.g,
                    twoLights.b - oneLight.b,
                    twoLights.a
                );
            }

            /// <summary>Releases generated objects and restores global, render-target, quality, fog, and active-scene state.</summary>
            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                try
                {
                    ReleaseRenderResources();
                    ClosePreviewScene();
                }
                finally
                {
                    RestoreCallerState();
                    AssertRestoredSceneCount();
                }
            }

            /// <summary>Creates and configures one transient product material for the required named pass.</summary>
            /// <param name="shaderName">The required product shader name.</param>
            /// <param name="passName">The required explicit pass name.</param>
            /// <param name="metallic">The material metallic value.</param>
            /// <returns>The registered transient material.</returns>
            private Material CreateProductMaterial(
                string shaderName,
                string passName,
                float metallic
            )
            {
                Shader shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, "Product shader '" + shaderName + "' is unavailable.");
                Assert.That(
                    ShaderUtil.ShaderHasError(shader),
                    Is.False,
                    "Product shader '" + shaderName + "' has compiler errors."
                );
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                materials.Add(material);
                ConfigureMaterial(material, metallic);
                int pass = material.FindPass(passName);
                Assert.That(pass, Is.GreaterThanOrEqualTo(0), shaderName + " requires " + passName + ".");
                return material;
            }

            /// <summary>Creates a fixture-owned Standard material used only to cast a controlled directional shadow.</summary>
            /// <returns>The registered nonpersistent shadow-caster material.</returns>
            private Material CreateStandardShadowCasterMaterial()
            {
                Shader shader = Shader.Find("Standard");
                Assert.That(shader, Is.Not.Null, "The Built-in Standard shader is unavailable for the shadow receiver readback.");
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                materials.Add(material);
                return material;
            }

            /// <summary>Renders a controlled mesh with the requested real Unity light setup.</summary>
            /// <param name="material">The configured transient material.</param>
            /// <param name="request">The coherent light and spherical-harmonic input for one render.</param>
            /// <returns>The center linear float readback color.</returns>
            private Color RenderWithLights(
                Material material,
                LightCaptureRequest request
            )
            {
                var lightObjects = new List<GameObject>();
                try
                {
                    InjectShGlobals(request.coefficients);
                    ApplyShProperties(request.coefficients);
                    meshFilter.sharedMesh = CreateNormalControlledQuad(request.normal);
                    renderer.sharedMaterial = material;
                    renderer.enabled = true;
                    CreateLights(lightObjects, request);
                    camera.Render();
                    Assert.That(
                        camera.actualRenderingPath,
                        Is.EqualTo(RenderingPath.Forward),
                        "The Toon lighting capture scope must actually use the BIRP Forward camera path."
                    );
                    return ReadCenterPixel();
                }
                finally
                {
                    renderer.enabled = false;
                    renderer.SetPropertyBlock(null);
                    DestroyGameObjects(lightObjects);
                }
            }

            /// <summary>Injects only the test-owned spherical-harmonic globals immediately before the render.</summary>
            /// <param name="coefficients">The seven SH globals for the render.</param>
            private void InjectShGlobals(ShCoefficients coefficients)
            {
                commandBuffer.Clear();
                commandBuffer.SetGlobalVector("unity_SHAr", coefficients.ar);
                commandBuffer.SetGlobalVector("unity_SHAg", coefficients.ag);
                commandBuffer.SetGlobalVector("unity_SHAb", coefficients.ab);
                commandBuffer.SetGlobalVector("unity_SHBr", coefficients.br);
                commandBuffer.SetGlobalVector("unity_SHBg", coefficients.bg);
                commandBuffer.SetGlobalVector("unity_SHBb", coefficients.bb);
                commandBuffer.SetGlobalVector("unity_SHC", coefficients.c);
            }

            /// <summary>Applies the test-owned SH vectors after Unity prepares renderer-local probe data.</summary>
            /// <param name="coefficients">The seven SH vectors for the render.</param>
            private void ApplyShProperties(ShCoefficients coefficients)
            {
                shProperties.Clear();
                shProperties.SetVector("unity_SHAr", coefficients.ar);
                shProperties.SetVector("unity_SHAg", coefficients.ag);
                shProperties.SetVector("unity_SHAb", coefficients.ab);
                shProperties.SetVector("unity_SHBr", coefficients.br);
                shProperties.SetVector("unity_SHBg", coefficients.bg);
                shProperties.SetVector("unity_SHBb", coefficients.bb);
                shProperties.SetVector("unity_SHC", coefficients.c);
                renderer.SetPropertyBlock(shProperties);
            }

            /// <summary>Creates real ForcePixel lights on the isolated preview-scene layer.</summary>
            /// <param name="lightObjects">Receives the caller-owned light GameObjects immediately after allocation.</param>
            /// <param name="request">The coherent light input for every generated light.</param>
            private void CreateLights(
                List<GameObject> lightObjects,
                LightCaptureRequest request
            )
            {
                for (int index = 0; index < request.lightCount; index++)
                {
                    GameObject lightObject = CreateHiddenObject(
                        "PureBase Toon Lighting Contract Light " + index,
                        lightObjects
                    );
                    Light light = lightObject.AddComponent<Light>();
                    light.renderMode = LightRenderMode.ForcePixel;
                    light.color = new Color(request.lightColor.x, request.lightColor.y, request.lightColor.z, 1.0f).gamma;
                    light.intensity = 1.0f;
                    light.cullingMask = 1 << FixtureLayer;
                    light.type = request.lightType;
                    light.cookie = request.cookie;
                    if (request.lightType == LightType.Directional)
                    {
                        Vector3 direction = new Vector3(
                            request.lightPosition.x,
                            request.lightPosition.y,
                            request.lightPosition.z
                        ).normalized;
                        Assert.That(
                            direction,
                            Is.Not.EqualTo(Vector3.zero),
                            "Directional lighting requires a nonzero direction."
                        );
                        lightObject.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
                    }
                    else
                    {
                        light.range = request.range;
                        light.spotAngle = request.spotAngle;
                        lightObject.transform.position = new Vector3(
                            request.lightPosition.x,
                            request.lightPosition.y,
                            request.lightPosition.z
                        );
                        if (request.lightType == LightType.Spot)
                        {
                            lightObject.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                        }
                    }
                }
            }

            /// <summary>Creates one hidden preview-scene GameObject and registers it immediately for cleanup.</summary>
            /// <param name="name">The diagnostic object name.</param>
            /// <returns>The caller-owned hidden preview-scene GameObject.</returns>
            private GameObject CreateHiddenObject(string name)
            {
                return CreateHiddenObject(name, gameObjects);
            }

            /// <summary>Creates one hidden preview-scene GameObject in the supplied cleanup collection.</summary>
            /// <param name="name">The diagnostic object name.</param>
            /// <param name="objects">The cleanup collection that receives the object immediately after allocation.</param>
            /// <returns>The caller-owned hidden preview-scene GameObject.</returns>
            private GameObject CreateHiddenObject(string name, List<GameObject> objects)
            {
                var gameObject = new GameObject(name);
                objects.Add(gameObject);
                gameObject.hideFlags = HideFlags.HideAndDontSave;
                gameObject.layer = FixtureLayer;
                SceneManager.MoveGameObjectToScene(gameObject, scene);
                return gameObject;
            }

            /// <summary>Creates one hidden receiver-scene object on the capture layer.</summary>
            /// <param name="receiverScene">The isolated regular additive scene.</param>
            /// <param name="name">The diagnostic object name.</param>
            /// <returns>The caller-owned temporary GameObject.</returns>
            private static GameObject CreateShadowSceneObject(Scene receiverScene, string name)
            {
                var gameObject = new GameObject(name)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = FixtureLayer,
                };
                SceneManager.MoveGameObjectToScene(gameObject, receiverScene);
                return gameObject;
            }

            /// <summary>Destroys one temporary regular-scene object when it was allocated.</summary>
            /// <param name="gameObject">The caller-owned temporary object.</param>
            private static void DestroyShadowSceneObject(GameObject gameObject)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            /// <summary>Releases command-buffer, temporary objects, material, texture, target, and render-mesh resources.</summary>
            private void ReleaseRenderResources()
            {
                if (camera != null && commandBuffer != null)
                {
                    camera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, commandBuffer);
                }

                if (commandBuffer != null)
                {
                    commandBuffer.Release();
                }

                DestroyGameObjects(gameObjects);

                foreach (Material material in materials)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }

                if (readback != null)
                {
                    UnityEngine.Object.DestroyImmediate(readback);
                }

                if (normalMap != null)
                {
                    UnityEngine.Object.DestroyImmediate(normalMap);
                }

                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }

                foreach (Mesh mesh in meshes)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }

            /// <summary>Destroys the supplied temporary GameObjects in reverse creation order.</summary>
            /// <param name="objects">The caller-owned GameObjects to destroy.</param>
            private static void DestroyGameObjects(List<GameObject> objects)
            {
                for (int index = objects.Count - 1; index >= 0; index--)
                {
                    UnityEngine.Object.DestroyImmediate(objects[index]);
                }

                objects.Clear();
            }

            /// <summary>Closes the generated Preview Scene after all generated resources have been released.</summary>
            private void ClosePreviewScene()
            {
                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }

            /// <summary>Restores every caller-owned global, render target, quality, and scene setting.</summary>
            private void RestoreCallerState()
            {
                foreach (KeyValuePair<string, Vector4> global in globals)
                {
                    Shader.SetGlobalVector(global.Key, global.Value);
                }

                RenderTexture.active = activeRenderTexture;
                QualitySettings.pixelLightCount = pixelLightCount;
                QualitySettings.shadows = shadowQuality;
                QualitySettings.shadowDistance = shadowDistance;
                if (activeScene.IsValid() && activeScene.isLoaded)
                {
                    SceneManager.SetActiveScene(activeScene);
                    RenderSettings.fog = fogEnabled;
                }
            }

            /// <summary>Asserts that the generated Preview Scene did not leak after cleanup.</summary>
            private void AssertRestoredSceneCount()
            {
                Assert.That(
                    SceneManager.sceneCount,
                    Is.EqualTo(sceneCount),
                    "The Toon lighting capture scope must restore the original loaded scene count."
                );
            }

            /// <summary>Configures a white opaque product material with no texture-specific lighting variation.</summary>
            /// <param name="material">The transient product material.</param>
            /// <param name="metallic">The metallic value for PBR and Hybrid observations.</param>
            private void ConfigureMaterial(Material material, float metallic)
            {
                material.SetTexture("_BaseTexture", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", Color.white);
                material.SetTexture("_NormalMap", normalMap);
                material.SetFloat("_NormalScale", 1.0f);
                if (material.HasProperty("_Metallic"))
                {
                    material.SetFloat("_Metallic", metallic);
                    material.SetFloat("_Roughness", 0.25f);
                }
            }

            /// <summary>Creates a linear normal-map texel that unpacks to the tangent-space forward vector in both Shader-Core branches.</summary>
            /// <returns>The fixture-owned nonpersistent normal texture.</returns>
            private static Texture2D CreateNeutralNormalTexture()
            {
                var texture = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixel(0, 0, new Color(0.5f, 0.5f, 1.0f, 1.0f));
                texture.Apply(false, true);
                return texture;
            }

            /// <summary>Creates the full-frame mesh used by regular renderer draws.</summary>
            /// <param name="normal">The required uniform world-space normal.</param>
            /// <returns>The caller-owned transient mesh.</returns>
            private Mesh CreateNormalControlledQuad(Vector3 normal)
            {
                var result = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                meshes.Add(result);
                Vector3 normalized = normal.normalized;
                result.vertices = new[]
                {
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(-1.0f, 1.0f, 0.0f),
                    new Vector3(1.0f, 1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                };
                result.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                result.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                result.RecalculateBounds();
                result.normals = new[] { normalized, normalized, normalized, normalized };
                result.tangents = new[]
                {
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                };
                return result;
            }

            /// <summary>Creates the horizontal receiver mesh used for the directional shadow region readback.</summary>
            /// <returns>The caller-owned transient receiver mesh.</returns>
            private Mesh CreateShadowReceiverMesh()
            {
                var result = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                meshes.Add(result);
                result.vertices = new[]
                {
                    new Vector3(-2.75f, 0.0f, -2.75f),
                    new Vector3(-2.75f, 0.0f, 2.75f),
                    new Vector3(2.75f, 0.0f, 2.75f),
                    new Vector3(2.75f, 0.0f, -2.75f),
                };
                result.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                result.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                result.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                result.tangents = new[]
                {
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                    new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                };
                result.RecalculateBounds();
                return result;
            }

            /// <summary>Creates the hidden cube mesh used only to cast one directional diagnostic shadow.</summary>
            /// <returns>The caller-owned transient caster mesh.</returns>
            private Mesh CreateShadowCasterMesh()
            {
                var result = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                meshes.Add(result);
                result.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
                    new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
                    new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f),
                    new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
                };
                result.triangles = new[]
                {
                    0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6,
                    0, 1, 5, 0, 5, 4, 2, 6, 7, 2, 7, 3,
                    0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5,
                };
                result.RecalculateBounds();
                return result;
            }

            /// <summary>Reads the center pixel while restoring the caller's active render target.</summary>
            /// <returns>The center linear color.</returns>
            private Color ReadCenterPixel()
            {
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(31.0f, 31.0f, 1.0f, 1.0f), 0, 0);
                    readback.Apply(false, false);
                    return readback.GetPixel(0, 0);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            /// <summary>Reads the full transient target while restoring the caller's active render target.</summary>
            /// <returns>The linear HDR receiver pixels.</returns>
            private Color[] ReadPixels()
            {
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0.0f, 0.0f, 64.0f, 64.0f), 0, 0);
                    readback.Apply(false, false);
                    return readback.GetPixels();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            /// <summary>Reads an arbitrary transient shadow target while restoring the active render target.</summary>
            /// <param name="source">The completed shadow render target.</param>
            /// <param name="destination">The transient CPU readback texture.</param>
            /// <returns>The copied linear HDR pixels.</returns>
            private static Color[] ReadPixels(RenderTexture source, Texture2D destination)
            {
                RenderTexture previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = source;
                    destination.ReadPixels(new Rect(0.0f, 0.0f, 64.0f, 64.0f), 0, 0);
                    destination.Apply(false, false);
                    return destination.GetPixels();
                }
                finally
                {
                    RenderTexture.active = previous;
                }
            }

            /// <summary>Computes a region mean from all finite opaque receiver samples rather than one fragile pixel.</summary>
            /// <param name="pixels">The complete receiver readback.</param>
            /// <returns>The observed region statistics.</returns>
            private static ShadowReceiverObservation MeasureReceiverRegion(Color[] pixels)
            {
                var sum = Color.black;
                var count = 0;
                foreach (Color pixel in pixels)
                {
                    if (pixel.a < 0.99f)
                    {
                        continue;
                    }

                    sum += pixel;
                    count++;
                }

                return new ShadowReceiverObservation(count, count == 0 ? Color.black : sum / count);
            }
        }

        /// <summary>Stores a finite mean RGB measurement for one shadow receiver region.</summary>
        [SuppressMessage("SonarAnalyzer.CSharp", "S3898", Justification = "Field assertions are the only intended contract for this private test carrier; it has no equality or hash-based use.")]
        private readonly struct ShadowReceiverObservation
        {
            /// <summary>Initializes one region observation.</summary>
            /// <param name="sampleCount">The number of opaque receiver samples.</param>
            /// <param name="meanColor">The receiver's mean linear color.</param>
            public ShadowReceiverObservation(int sampleCount, Color meanColor)
            {
                this.sampleCount = sampleCount;
                this.meanColor = meanColor;
            }

            /// <summary>Gets the count of receiver samples contributing to the mean.</summary>
            public int sampleCount { get; }

            /// <summary>Gets the region's mean linear color.</summary>
            public Color meanColor { get; }
        }

        /// <summary>Temporarily selects and imports only the fixed Toon shadow host without persisting Shader-Core settings.</summary>
        private sealed class ToonShadowHostSelectionScope : IDisposable
        {
            private const string ShaderCoreAssemblyName = "jp.lilxyzw.shadercore";
            private const string ProjectSettingsTypeName = "jp.lilxyzw.shadercore.ProjectSettings";
            private const string ShaderSettingsFieldName = "shaderSettings";
            private const string ShaderNameFieldName = "shadername";
            private const string ModulesFieldName = "modules";
            private const string MultiModulesFieldName = "multiModules";
            private const string MultiModuleNameFieldName = "name";
            private const string MultiModuleCountFieldName = "count";
            private const string ToonShadowShaderName = "PureBase/Tests/ShaderCore/ToonShadow";
            private const string ToonShadowModuleId = "jp.penguin.purebase.tests.shadercore.toonshadow";
            private const string ToonShadowHostAssetPath = "Packages/jp.penguin.purebase/Tests/Fixtures/Hosts/ToonShadow/PureBaseTestToonShadow.scshader";
            private const string ProjectSettingsRelativePath = "ProjectSettings/jp.lilxyzw.shadercore.asset";

            private readonly UnityEngine.Object settings;
            private readonly string shaderName;
            private readonly string moduleId;
            private readonly string hostAssetPath;
            private readonly ToonShadowSettingsRow originalRow;
            private readonly string projectSettingsHash;
            private bool temporarySelectionApplied;
            private bool disposed;

            /// <summary>Captures the original fixed-host row, applies a temporary selection, and synchronously imports only its host.</summary>
            public ToonShadowHostSelectionScope()
                : this(ToonShadowShaderName, ToonShadowModuleId, ToonShadowHostAssetPath)
            {
            }

            /// <summary>Captures one fixed-host row, applies its temporary selection, and synchronously imports only that host.</summary>
            /// <param name="shaderName">The unique fixed-host shader name whose selection row is temporary.</param>
            /// <param name="moduleId">The sole diagnostic module selected for the fixed host.</param>
            /// <param name="hostAssetPath">The Shader-Core host asset imported after selection.</param>
            public ToonShadowHostSelectionScope(string shaderName, string moduleId, string hostAssetPath)
            {
                this.shaderName = shaderName;
                this.moduleId = moduleId;
                this.hostAssetPath = hostAssetPath;
                settings = GetProjectSettings();
                projectSettingsHash = GetFileSha256(GetProjectSettingsPath());
                try
                {
                    using (var serializedSettings = new SerializedObject(settings))
                    {
                        SerializedProperty settingsProperty = GetShaderSettingsProperty(serializedSettings);
                        originalRow = ReadToonShadowRow(settingsProperty, shaderName);
                        WriteTemporaryToonShadowRow(settingsProperty, shaderName, moduleId);
                        serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                        temporarySelectionApplied = true;
                    }

                    AssetDatabase.ImportAsset(
                        hostAssetPath,
                        ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate
                    );
                }
                catch
                {
                    if (temporarySelectionApplied)
                    {
                        RestoreAndAssertUnchanged();
                    }

                    throw;
                }
            }

            /// <summary>Restores only the captured ToonShadow row without reimporting or saving Shader-Core settings.</summary>
            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                if (temporarySelectionApplied)
                {
                    RestoreAndAssertUnchanged();
                }
            }

            /// <summary>Restores the temporary row, then checks its semantic state and the persisted ProjectSettings bytes.</summary>
            private void RestoreAndAssertUnchanged()
            {
                using (var serializedSettings = new SerializedObject(settings))
                {
                    SerializedProperty settingsProperty = GetShaderSettingsProperty(serializedSettings);
                    RestoreToonShadowRow(settingsProperty, shaderName);
                    serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                }

                temporarySelectionApplied = false;
                Assert.That(
                    GetFileSha256(GetProjectSettingsPath()),
                    Is.EqualTo(projectSettingsHash),
                    "The temporary fixed-host selection must not persist Shader-Core ProjectSettings."
                );
                using (var serializedSettings = new SerializedObject(settings))
                {
                    ToonShadowSettingsRow restoredRow = ReadToonShadowRow(
                        GetShaderSettingsProperty(serializedSettings),
                        shaderName
                    );
                    Assert.That(
                        restoredRow.Equals(originalRow),
                        Is.True,
                        "The temporary fixed-host selection must restore only its original serialized row."
                    );
                }
            }

            /// <summary>Gets the loaded Shader-Core ProjectSettings singleton without invoking its persistence API.</summary>
            private static UnityEngine.Object GetProjectSettings()
            {
                Assembly shaderCoreAssembly = null;
                foreach (Assembly candidate in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (candidate.GetName().Name == ShaderCoreAssemblyName)
                    {
                        shaderCoreAssembly = candidate;
                        break;
                    }
                }

                Type settingsType = shaderCoreAssembly?.GetType(ProjectSettingsTypeName, false);
                Assert.That(
                    settingsType,
                    Is.Not.Null,
                    "Shader-Core ProjectSettings was not loaded."
                );
                Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(settingsType);
                PropertyInfo instanceProperty = singletonType.GetProperty(
                    "instance",
                    BindingFlags.Public | BindingFlags.Static
                );
                UnityEngine.Object resolvedSettings = instanceProperty?.GetValue(null) as UnityEngine.Object;
                Assert.That(
                    resolvedSettings,
                    Is.Not.Null,
                    "Shader-Core ProjectSettings singleton was unavailable."
                );
                return resolvedSettings;
            }

            /// <summary>Gets the validated serialized Shader-Core selection array.</summary>
            private static SerializedProperty GetShaderSettingsProperty(SerializedObject serializedSettings)
            {
                SerializedProperty settingsProperty = serializedSettings.FindProperty(
                    ShaderSettingsFieldName
                );
                Assert.That(
                    settingsProperty,
                    Is.Not.Null.And.Property("isArray").True,
                    "Shader-Core ProjectSettings did not expose the expected shaderSettings array."
                );
                return settingsProperty;
            }

            /// <summary>Reads only the original ToonShadow row, rejecting duplicate target rows before mutation.</summary>
            private static ToonShadowSettingsRow ReadToonShadowRow(
                SerializedProperty settingsProperty,
                string shaderName
            )
            {
                int rowIndex = FindToonShadowRowIndex(settingsProperty, shaderName);
                if (rowIndex < 0)
                {
                    return ToonShadowSettingsRow.Missing;
                }

                SerializedProperty row = settingsProperty.GetArrayElementAtIndex(rowIndex);
                return new ToonShadowSettingsRow(
                    true,
                    ReadStringArray(row.FindPropertyRelative(ModulesFieldName)),
                    ReadMultiModules(row.FindPropertyRelative(MultiModulesFieldName))
                );
            }

            /// <summary>Upserts only the target row with its required one-module selection.</summary>
            private static void WriteTemporaryToonShadowRow(
                SerializedProperty settingsProperty,
                string shaderName,
                string moduleId
            )
            {
                int rowIndex = FindToonShadowRowIndex(settingsProperty, shaderName);
                if (rowIndex < 0)
                {
                    rowIndex = settingsProperty.arraySize;
                    settingsProperty.InsertArrayElementAtIndex(rowIndex);
                }

                SerializedProperty row = settingsProperty.GetArrayElementAtIndex(rowIndex);
                row.FindPropertyRelative(ShaderNameFieldName).stringValue = shaderName;
                WriteStringArray(row.FindPropertyRelative(ModulesFieldName), new[] { moduleId });
                WriteMultiModules(row.FindPropertyRelative(MultiModulesFieldName), Array.Empty<MultiModuleSetting>());
            }

            /// <summary>Restores only the target row to its captured presence and exact module collections.</summary>
            private void RestoreToonShadowRow(SerializedProperty settingsProperty, string shaderName)
            {
                int rowIndex = FindToonShadowRowIndex(settingsProperty, shaderName);
                if (!originalRow.present)
                {
                    Assert.That(
                        rowIndex,
                        Is.GreaterThanOrEqualTo(0),
                        "The temporary fixed-host row disappeared before it could be removed."
                    );
                    settingsProperty.DeleteArrayElementAtIndex(rowIndex);
                    return;
                }

                Assert.That(
                    rowIndex,
                    Is.GreaterThanOrEqualTo(0),
                    "The original fixed-host row disappeared before it could be restored."
                );
                SerializedProperty row = settingsProperty.GetArrayElementAtIndex(rowIndex);
                row.FindPropertyRelative(ShaderNameFieldName).stringValue = shaderName;
                WriteStringArray(row.FindPropertyRelative(ModulesFieldName), originalRow.modules);
                WriteMultiModules(row.FindPropertyRelative(MultiModulesFieldName), originalRow.multiModules);
            }

            /// <summary>Finds the sole ToonShadow row without reading or changing unrelated module-selection rows.</summary>
            private static int FindToonShadowRowIndex(
                SerializedProperty settingsProperty,
                string shaderName
            )
            {
                var foundIndex = -1;
                for (var index = 0; index < settingsProperty.arraySize; index++)
                {
                    SerializedProperty shaderNameProperty = settingsProperty
                        .GetArrayElementAtIndex(index)
                        .FindPropertyRelative(ShaderNameFieldName);
                    if (shaderNameProperty == null || shaderNameProperty.stringValue != shaderName)
                    {
                        continue;
                    }

                    Assert.That(
                        foundIndex,
                        Is.EqualTo(-1),
                        "Shader-Core ProjectSettings contains duplicate fixed-host rows."
                    );
                    foundIndex = index;
                }

                return foundIndex;
            }

            /// <summary>Copies an ordered serialized string list without retaining SerializedProperty instances.</summary>
            private static string[] ReadStringArray(SerializedProperty property)
            {
                Assert.That(property, Is.Not.Null.And.Property("isArray").True);
                var values = new string[property.arraySize];
                for (var index = 0; index < property.arraySize; index++)
                {
                    values[index] = property.GetArrayElementAtIndex(index).stringValue;
                }

                return values;
            }

            /// <summary>Writes an ordered serialized string list.</summary>
            private static void WriteStringArray(SerializedProperty property, string[] values)
            {
                Assert.That(property, Is.Not.Null.And.Property("isArray").True);
                property.arraySize = values.Length;
                for (var index = 0; index < values.Length; index++)
                {
                    property.GetArrayElementAtIndex(index).stringValue = values[index];
                }
            }

            /// <summary>Copies the ToonShadow multi-module selection exactly.</summary>
            private static MultiModuleSetting[] ReadMultiModules(SerializedProperty property)
            {
                Assert.That(property, Is.Not.Null.And.Property("isArray").True);
                var values = new MultiModuleSetting[property.arraySize];
                for (var index = 0; index < property.arraySize; index++)
                {
                    SerializedProperty value = property.GetArrayElementAtIndex(index);
                    values[index] = new MultiModuleSetting(
                        value.FindPropertyRelative(MultiModuleNameFieldName).stringValue,
                        value.FindPropertyRelative(MultiModuleCountFieldName).intValue
                    );
                }

                return values;
            }

            /// <summary>Writes the ToonShadow multi-module selection exactly.</summary>
            private static void WriteMultiModules(
                SerializedProperty property,
                MultiModuleSetting[] values
            )
            {
                Assert.That(property, Is.Not.Null.And.Property("isArray").True);
                property.arraySize = values.Length;
                for (var index = 0; index < values.Length; index++)
                {
                    SerializedProperty value = property.GetArrayElementAtIndex(index);
                    value.FindPropertyRelative(MultiModuleNameFieldName).stringValue = values[index].name;
                    value.FindPropertyRelative(MultiModuleCountFieldName).intValue = values[index].count;
                }
            }

            /// <summary>Gets the persistent Shader-Core ProjectSettings path for byte-level non-persistence checks.</summary>
            private static string GetProjectSettingsPath()
            {
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string path = Path.Combine(
                    projectRoot,
                    ProjectSettingsRelativePath.Replace('/', Path.DirectorySeparatorChar)
                );
                Assert.That(path, Does.Exist, "Shader-Core ProjectSettings asset was not found.");
                return path;
            }

            /// <summary>Returns one lowercase SHA-256 digest for persisted-state equality checks.</summary>
            private static string GetFileSha256(string path)
            {
                using (SHA256 hasher = SHA256.Create())
                using (FileStream stream = File.OpenRead(path))
                {
                    return BitConverter.ToString(hasher.ComputeHash(stream))
                        .Replace("-", string.Empty)
                        .ToLowerInvariant();
                }
            }

            /// <summary>Stores one multi-module name and count without retaining SerializedProperty state.</summary>
            private readonly struct MultiModuleSetting : IEquatable<MultiModuleSetting>
            {
                /// <summary>Initializes one multi-module selection.</summary>
                public MultiModuleSetting(string name, int count)
                {
                    this.name = name;
                    this.count = count;
                }

                /// <summary>Gets the module identifier.</summary>
                public string name { get; }

                /// <summary>Gets the selected property count.</summary>
                public int count { get; }

                /// <summary>Compares one multi-module selection exactly.</summary>
                public bool Equals(MultiModuleSetting other)
                {
                    return name == other.name && count == other.count;
                }

                /// <inheritdoc />
                public override bool Equals(object obj)
                {
                    return obj is MultiModuleSetting other && Equals(other);
                }

                /// <inheritdoc />
                public override int GetHashCode()
                {
                    unchecked
                    {
                        return ((name == null ? 0 : name.GetHashCode()) * 31) + count;
                    }
                }
            }

            /// <summary>Stores only the captured ToonShadow row presence and exact module collections.</summary>
            private readonly struct ToonShadowSettingsRow : IEquatable<ToonShadowSettingsRow>
            {
                /// <summary>Initializes one captured ToonShadow selection row.</summary>
                public ToonShadowSettingsRow(
                    bool present,
                    string[] modules,
                    MultiModuleSetting[] multiModules
                )
                {
                    this.present = present;
                    this.modules = modules;
                    this.multiModules = multiModules;
                }

                /// <summary>Gets a missing ToonShadow row capture.</summary>
                public static ToonShadowSettingsRow Missing => new ToonShadowSettingsRow(
                    false,
                    Array.Empty<string>(),
                    Array.Empty<MultiModuleSetting>()
                );

                /// <summary>Gets whether the original ToonShadow row was present.</summary>
                public bool present { get; }

                /// <summary>Gets the original ordered module selection.</summary>
                public string[] modules { get; }

                /// <summary>Gets the original ordered multi-module selection.</summary>
                public MultiModuleSetting[] multiModules { get; }

                /// <summary>Compares one captured ToonShadow row semantically and in selection order.</summary>
                public bool Equals(ToonShadowSettingsRow other)
                {
                    if (present != other.present)
                    {
                        return false;
                    }

                    if (!ReferenceEquals(modules, other.modules))
                    {
                        if (modules == null || other.modules == null || modules.Length != other.modules.Length)
                        {
                            return false;
                        }

                        for (var index = 0; index < modules.Length; index++)
                        {
                            if (modules[index] != other.modules[index])
                            {
                                return false;
                            }
                        }
                    }

                    if (ReferenceEquals(multiModules, other.multiModules))
                    {
                        return true;
                    }

                    if (multiModules == null || other.multiModules == null || multiModules.Length != other.multiModules.Length)
                    {
                        return false;
                    }

                    for (var index = 0; index < multiModules.Length; index++)
                    {
                        if (!multiModules[index].Equals(other.multiModules[index]))
                        {
                            return false;
                        }
                    }

                    return true;
                }

                /// <inheritdoc />
                public override bool Equals(object obj)
                {
                    return obj is ToonShadowSettingsRow other && Equals(other);
                }

                /// <inheritdoc />
                public override int GetHashCode()
                {
                    unchecked
                    {
                        var hash = present ? 1 : 0;
                        hash = (hash * 31) + (modules == null ? 0 : 1);
                        if (modules != null)
                        {
                            hash = (hash * 31) + modules.Length;
                            foreach (string module in modules)
                            {
                                hash = (hash * 31) + (module == null ? 0 : module.GetHashCode());
                            }
                        }

                        hash = (hash * 31) + (multiModules == null ? 0 : 1);
                        if (multiModules != null)
                        {
                            hash = (hash * 31) + multiModules.Length;
                            foreach (MultiModuleSetting multiModule in multiModules)
                            {
                                hash = (hash * 31) + multiModule.GetHashCode();
                            }
                        }

                        return hash;
                    }
                }
            }
        }
    }
}
