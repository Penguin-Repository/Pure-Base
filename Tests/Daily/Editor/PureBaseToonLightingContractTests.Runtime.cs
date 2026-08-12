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
        /// <summary>Owns one isolated explicit-draw fixture and restores every Unity global it changes.</summary>
        private class ToonLightingCaptureRuntimeScope : IDisposable
        {
            /// <summary>Lists the injected global property names in draw-order ownership order.</summary>
            private static readonly string[] GlobalNames =
            {
                "_LightColor0",
                "_WorldSpaceLightPos0",
                "unity_SHAr",
                "unity_SHAg",
                "unity_SHAb",
                "unity_SHBr",
                "unity_SHBg",
                "unity_SHBb",
                "unity_SHC",
            };

            /// <summary>Stores the caller's POINT keyword state before this fixture can issue a point-light draw.</summary>
            private readonly bool pointKeywordEnabled;

            /// <summary>Stores the global vectors captured before the fixture writes any explicit lighting input.</summary>
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

            /// <summary>Stores the explicit-draw camera.</summary>
            private readonly Camera camera;

            /// <summary>Tracks completed render meshes for deterministic scope cleanup.</summary>
            private readonly List<Mesh> meshes = new List<Mesh>();

            /// <summary>Stores the linear float render target.</summary>
            private readonly RenderTexture target;

            /// <summary>Stores the float CPU readback texture.</summary>
            private readonly Texture2D readback;

            /// <summary>Stores the controlled linear normal texture used by every explicit product draw.</summary>
            private readonly Texture2D normalMap;

            /// <summary>Stores the command buffer that injects globals immediately before every draw.</summary>
            private readonly CommandBuffer commandBuffer;

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
                pointKeywordEnabled = Shader.IsKeywordEnabled(PointKeyword);
                foreach (string globalName in GlobalNames)
                {
                    globals.Add(globalName, Shader.GetGlobalVector(globalName));
                }

                scene = EditorSceneManager.NewPreviewScene();
                RenderSettings.fog = false;
                QualitySettings.pixelLightCount = Mathf.Max(2, pixelLightCount);

                GameObject cameraObject = EditorUtility.CreateGameObjectWithHideFlags(
                    "PureBase Toon Lighting Contract Camera",
                    HideFlags.HideAndDontSave,
                    typeof(Camera)
                );
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.GetComponent<Camera>();
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
                commandBuffer = new CommandBuffer { name = "PureBase Toon Lighting Contract Draw" };
                camera.enabled = false;
                camera.cullingMask = 0;
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

            /// <summary>Draws one product pass after installing the exact lighting globals for the current test case.</summary>
            /// <param name="shaderName">The required product shader name.</param>
            /// <param name="passName">The required explicit pass name.</param>
            /// <param name="normal">The uniform mesh world normal.</param>
            /// <param name="lightColor">The explicitly injected main or additional light color.</param>
            /// <param name="lightPosition">The explicitly injected directional or point light vector.</param>
            /// <param name="coefficients">The seven SH globals injected immediately before the draw.</param>
            /// <param name="pointLight">Whether the ForwardAdd draw selects the point-light shader variant.</param>
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
                bool pointKeywordWasEnabled = Shader.IsKeywordEnabled(PointKeyword);
                try
                {
                    int pass;
                    Material material = CreateProductMaterial(shaderName, passName, metallic, out pass);
                    Mesh renderMesh = CreateNormalControlledQuad(normal);
                    QueueDraw(
                        renderMesh,
                        material,
                        pass,
                        lightColor,
                        lightPosition,
                        coefficients,
                        pointLight,
                        pointKeywordWasEnabled
                    );
                    camera.Render();
                    return ReadCenterPixel();
                }
                finally
                {
                    RestorePointKeywordState(pointKeywordWasEnabled);
                }
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

            /// <summary>Creates and configures one transient product material for the explicit named pass.</summary>
            /// <param name="shaderName">The required product shader name.</param>
            /// <param name="passName">The required explicit pass name.</param>
            /// <param name="metallic">The material metallic value.</param>
            /// <param name="pass">Receives the required pass index.</param>
            /// <returns>The registered transient material.</returns>
            private Material CreateProductMaterial(
                string shaderName,
                string passName,
                float metallic,
                out int pass
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
                pass = material.FindPass(passName);
                Assert.That(pass, Is.GreaterThanOrEqualTo(0), shaderName + " requires " + passName + ".");
                return material;
            }

            /// <summary>Queues one draw after installing the explicit direct-light and SH globals.</summary>
            /// <param name="renderMesh">The completed render-scoped mesh.</param>
            /// <param name="material">The configured transient material.</param>
            /// <param name="pass">The required material pass.</param>
            /// <param name="lightColor">The explicit main or additional light color.</param>
            /// <param name="lightPosition">The explicit directional or point light vector.</param>
            /// <param name="coefficients">The seven SH globals for the draw.</param>
            /// <param name="pointLight">Whether to select the point-light shader variant.</param>
            /// <param name="pointKeywordWasEnabled">The caller-owned POINT state to queue after the draw.</param>
            private void QueueDraw(
                Mesh renderMesh,
                Material material,
                int pass,
                Vector4 lightColor,
                Vector4 lightPosition,
                ShCoefficients coefficients,
                bool pointLight,
                bool pointKeywordWasEnabled
            )
            {
                commandBuffer.Clear();
                InjectLightingGlobals(lightColor, lightPosition, coefficients);
                if (pointLight)
                {
                    commandBuffer.EnableShaderKeyword(PointKeyword);
                }

                commandBuffer.DrawMesh(renderMesh, Matrix4x4.identity, material, 0, pass);
                if (pointLight)
                {
                    SetCommandBufferPointKeywordState(pointKeywordWasEnabled);
                }
            }

            /// <summary>Injects all direct-light and spherical-harmonic globals immediately before the draw.</summary>
            /// <param name="lightColor">The explicit main or additional light color.</param>
            /// <param name="lightPosition">The explicit directional or point light vector.</param>
            /// <param name="coefficients">The seven SH globals for the draw.</param>
            private void InjectLightingGlobals(
                Vector4 lightColor,
                Vector4 lightPosition,
                ShCoefficients coefficients
            )
            {
                commandBuffer.SetGlobalVector("_LightColor0", lightColor);
                commandBuffer.SetGlobalVector("_WorldSpaceLightPos0", lightPosition);
                commandBuffer.SetGlobalVector("unity_SHAr", coefficients.ar);
                commandBuffer.SetGlobalVector("unity_SHAg", coefficients.ag);
                commandBuffer.SetGlobalVector("unity_SHAb", coefficients.ab);
                commandBuffer.SetGlobalVector("unity_SHBr", coefficients.br);
                commandBuffer.SetGlobalVector("unity_SHBg", coefficients.bg);
                commandBuffer.SetGlobalVector("unity_SHBb", coefficients.bb);
                commandBuffer.SetGlobalVector("unity_SHC", coefficients.c);
            }

            /// <summary>Releases command-buffer, material, texture, target, and render-mesh resources in allocation order.</summary>
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
                RestorePointKeywordState(pointKeywordEnabled);
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

            /// <summary>Queues restoration of the POINT keyword state after the explicit point-light draw.</summary>
            /// <param name="enabled">Whether POINT must remain enabled after the draw.</param>
            private void SetCommandBufferPointKeywordState(bool enabled)
            {
                if (enabled)
                {
                    commandBuffer.EnableShaderKeyword(PointKeyword);
                }
                else
                {
                    commandBuffer.DisableShaderKeyword(PointKeyword);
                }
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

            /// <summary>Creates the full-frame mesh used by explicit pass draws.</summary>
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
