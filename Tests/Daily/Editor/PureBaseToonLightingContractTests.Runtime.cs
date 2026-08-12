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
        /// <summary>Owns one isolated regular-render fixture and restores every Unity global it changes.</summary>
        private class ToonLightingCaptureRuntimeScope : IDisposable
        {
            /// <summary>Stores the dedicated layer used by the preview-scene renderer and lights.</summary>
            private const int FixtureLayer = 31;

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

            /// <summary>Stores the original fog setting for the formerly active scene.</summary>
            private readonly bool fogEnabled;

            /// <summary>Stores the disposable Preview Scene that owns all generated GameObjects.</summary>
            private readonly Scene scene;

            /// <summary>Stores the isolated Forward-rendering camera.</summary>
            private readonly Camera camera;

            /// <summary>Stores the renderer used for every regular product-material render.</summary>
            private readonly MeshRenderer renderer;

            /// <summary>Stores the mesh filter used for controlled-normal render meshes.</summary>
            private readonly MeshFilter meshFilter;

            /// <summary>Tracks completed renderer meshes for deterministic scope cleanup.</summary>
            private readonly List<Mesh> meshes = new List<Mesh>();

            /// <summary>Tracks camera and renderer GameObjects for deterministic preview-scene cleanup.</summary>
            private readonly List<GameObject> gameObjects = new List<GameObject>();

            /// <summary>Stores the linear float render target.</summary>
            private readonly RenderTexture target;

            /// <summary>Stores the float CPU readback texture.</summary>
            private readonly Texture2D readback;

            /// <summary>Stores the controlled linear normal texture used by every product-material render.</summary>
            private readonly Texture2D normalMap;

            /// <summary>Stores the command buffer that injects only SH globals immediately before every render.</summary>
            private readonly CommandBuffer commandBuffer;

            /// <summary>Stores the renderer-local SH override that wins over Unity's per-object probe setup.</summary>
            private readonly MaterialPropertyBlock shProperties;

            /// <summary>Tracks whether this scope has already released its resources.</summary>
            private bool disposed;

            /// <summary>Creates an isolated linear render fixture with no fog or reflection probes.</summary>
            public ToonLightingCaptureRuntimeScope()
            {
                activeRenderTexture = RenderTexture.active;
                activeScene = SceneManager.GetActiveScene();
                sceneCount = SceneManager.sceneCount;
                pixelLightCount = QualitySettings.pixelLightCount;
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
                catch
                {
                    ReleaseRenderResources();
                    ClosePreviewScene();
                    RestoreCallerState();
                    throw;
                }
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
                    Color oneLight = RenderWithLights(
                        material,
                        normal,
                        lightColor,
                        lightPosition,
                        coefficients,
                        true,
                        1
                    );
                    Color twoLights = RenderWithLights(
                        material,
                        normal,
                        lightColor,
                        lightPosition,
                        coefficients,
                        true,
                        2
                    );
                    return new Color(
                        twoLights.r - oneLight.r,
                        twoLights.g - oneLight.g,
                        twoLights.b - oneLight.b,
                        twoLights.a
                    );
                }

                return RenderWithLights(
                    material,
                    normal,
                    lightColor,
                    lightPosition,
                    coefficients,
                    false,
                    lightColor == Vector4.zero ? 0 : 1
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

            /// <summary>Renders a controlled mesh with the requested real Unity light setup.</summary>
            /// <param name="material">The configured transient material.</param>
            /// <param name="normal">The uniform mesh world normal.</param>
            /// <param name="lightColor">The real main or additional light color.</param>
            /// <param name="lightPosition">The real directional or point light vector.</param>
            /// <param name="coefficients">The seven SH globals for the render.</param>
            /// <param name="pointLight">Whether the setup uses Point lights.</param>
            /// <param name="lightCount">The number of ForcePixel lights to create.</param>
            /// <returns>The center linear float readback color.</returns>
            private Color RenderWithLights(
                Material material,
                Vector3 normal,
                Vector4 lightColor,
                Vector4 lightPosition,
                ShCoefficients coefficients,
                bool pointLight,
                int lightCount
            )
            {
                var lightObjects = new List<GameObject>();
                try
                {
                    InjectShGlobals(coefficients);
                    ApplyShProperties(coefficients);
                    meshFilter.sharedMesh = CreateNormalControlledQuad(normal);
                    renderer.sharedMaterial = material;
                    renderer.enabled = true;
                    CreateLights(lightObjects, lightColor, lightPosition, pointLight, lightCount);
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
            /// <param name="lightColor">The real main or additional light color.</param>
            /// <param name="lightPosition">The directional or point light vector.</param>
            /// <param name="pointLight">Whether the setup uses Point lights.</param>
            /// <param name="lightCount">The number of ForcePixel lights to create.</param>
            private void CreateLights(
                List<GameObject> lightObjects,
                Vector4 lightColor,
                Vector4 lightPosition,
                bool pointLight,
                int lightCount
            )
            {
                for (int index = 0; index < lightCount; index++)
                {
                    GameObject lightObject = CreateHiddenObject(
                        "PureBase Toon Lighting Contract Light " + index,
                        lightObjects
                    );
                    Light light = lightObject.AddComponent<Light>();
                    light.renderMode = LightRenderMode.ForcePixel;
                    light.color = new Color(lightColor.x, lightColor.y, lightColor.z, 1.0f).gamma;
                    light.intensity = 1.0f;
                    light.cullingMask = 1 << FixtureLayer;
                    if (pointLight)
                    {
                        light.type = LightType.Point;
                        light.range = 4.0f;
                        lightObject.transform.position = new Vector3(
                            lightPosition.x,
                            lightPosition.y,
                            lightPosition.z
                        );
                    }
                    else
                    {
                        light.type = LightType.Directional;
                        Vector3 direction = new Vector3(
                            lightPosition.x,
                            lightPosition.y,
                            lightPosition.z
                        ).normalized;
                        Assert.That(
                            direction,
                            Is.Not.EqualTo(Vector3.zero),
                            "Directional lighting requires a nonzero direction."
                        );
                        lightObject.transform.rotation = Quaternion.LookRotation(-direction, Vector3.up);
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
        }
    }
}
