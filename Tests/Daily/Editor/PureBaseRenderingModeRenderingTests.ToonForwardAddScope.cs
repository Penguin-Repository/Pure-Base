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

// Defines the isolated Toon ForwardAdd rendering scope used by Stencil tests.

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
    public sealed partial class PureBaseRenderingModeRenderingTests
    {
        /// <summary>Owns the active-scene-only Toon ForwardAdd observation while restoring all shared editor state.</summary>
        private sealed class ToonForwardAddScope : IDisposable
        {
            /// <summary>Stores every loaded scene and its initial dirty state.</summary>
            private sealed class SceneState
            {
                /// <summary>Initializes one captured loaded-scene state.</summary>
                /// <param name="scene">The loaded scene.</param>
                /// <param name="isDirty">Whether the scene was dirty before the scope began.</param>
                public SceneState(Scene scene, bool isDirty)
                {
                    this.scene = scene;
                    this.isDirty = isDirty;
                }

                /// <summary>Stores the observed loaded scene.</summary>
                public readonly Scene scene;

                /// <summary>Stores the scene dirty state captured before temporary object creation.</summary>
                public readonly bool isDirty;
            }

            /// <summary>Stores temporary directional lights in creation order.</summary>
            private readonly List<GameObject> lightObjects = new List<GameObject>();

            /// <summary>Stores temporary product materials in creation order.</summary>
            private readonly List<Material> materials = new List<Material>();

            /// <summary>Stores the loaded-scene states captured before temporary object creation.</summary>
            private readonly List<SceneState> sceneStates = new List<SceneState>();

            /// <summary>Stores the active scene before this scope begins.</summary>
            private Scene activeScene;

            /// <summary>Stores the loaded scene count before this scope begins.</summary>
            private int sceneCount;

            /// <summary>Stores the pixel-light budget before the scope raises it for ForwardAdd.</summary>
            private int pixelLightCount;

            /// <summary>Stores the active render target before readback.</summary>
            private RenderTexture activeRenderTexture;

            /// <summary>Stores the dynamically selected layer excluded from all existing scene objects.</summary>
            private int fixtureLayer;

            /// <summary>Stores the imported D24S8 asset without changing its serialization or hide flags.</summary>
            private RenderTexture renderTexture;

            /// <summary>Tracks whether this scope created the D24S8 asset GPU resource.</summary>
            private bool createdRenderTextureResource;

            /// <summary>Stores the temporary command buffer used for explicit D24S8 clears.</summary>
            private CommandBuffer commandBuffer;

            /// <summary>Stores the temporary active-scene camera.</summary>
            private GameObject cameraObject;

            /// <summary>Stores the temporary active-scene quad.</summary>
            private GameObject quadObject;

            /// <summary>Stores the temporary CPU readback texture.</summary>
            private Texture2D texture;

            /// <summary>Tracks whether cleanup already restored the captured state.</summary>
            private bool disposed;

            /// <summary>Gets device, attachment, and camera diagnostics for Toon ForwardAdd assertions.</summary>
            public string FormatDescription
            {
                get
                {
                    Camera camera = cameraObject == null ? null : cameraObject.GetComponent<Camera>();
                    return D24S8StencilFixture.DescribeTarget(renderTexture) +
                        " RequestedRenderingPath=" + (camera == null ? "<unallocated>" : camera.renderingPath.ToString()) +
                        " ActualRenderingPath=" + (camera == null ? "<unallocated>" : camera.actualRenderingPath.ToString()) +
                        " PixelLightCount=" + QualitySettings.pixelLightCount +
                        " FixtureLayer=" + fixtureLayer;
                }
            }

            /// <summary>Captures shared state and allocates only HideAndDontSave active-scene render objects.</summary>
            public void Initialize()
            {
                CaptureSharedState();
                fixtureLayer = FindUnusedLayer();
                renderTexture = D24S8StencilFixture.LoadAndCreateD24S8RenderTextureAssetForToonScope(out createdRenderTextureResource);
                commandBuffer = new CommandBuffer { name = "PureBase Toon ForwardAdd D24S8 Clear" };
                cameraObject = CreateHiddenObject("PureBaseToonForwardAddCamera");
                texture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
                quadObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quadObject.hideFlags = HideFlags.HideAndDontSave;
                quadObject.layer = fixtureLayer;
                quadObject.GetComponent<Renderer>().enabled = false;

                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 0.5f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 10.0f;
                camera.renderingPath = RenderingPath.Forward;
                camera.cullingMask = 1 << fixtureLayer;
                camera.clearFlags = CameraClearFlags.Nothing;
                camera.transform.position = new Vector3(0.0f, 0.0f, -2.0f);
                camera.targetTexture = renderTexture;
                Assert.That(camera.renderingPath, Is.EqualTo(RenderingPath.Forward), "The Toon D24S8 scope must request the BIRP Forward camera path.");
            }

            /// <summary>Renders one transparent Toon material with the requested isolated directional-light count.</summary>
            /// <param name="shader">The Toon product shader.</param>
            /// <param name="clearStencil">The initial Stencil byte.</param>
            /// <param name="stencilState">The explicit Stencil configuration.</param>
            /// <param name="lightCount">The number of ForcePixel directional lights.</param>
            /// <returns>The rendered center pixel.</returns>
            public Color RenderToonComposite(Shader shader, byte clearStencil, StencilState stencilState, int lightCount)
            {
                SetLightCount(lightCount);
                Material material = CreateToonMaterial(shader, stencilState);
                ClearTarget(clearStencil);
                Renderer renderer = quadObject.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.enabled = true;
                Camera camera = cameraObject.GetComponent<Camera>();
                try
                {
                    camera.Render();
                    Assert.That(camera.actualRenderingPath, Is.EqualTo(RenderingPath.Forward), "The Toon D24S8 scope must actually use the BIRP Forward camera path. " + FormatDescription);
                    return ReadCenterPixel(renderTexture, texture);
                }
                finally
                {
                    renderer.enabled = false;
                }
            }

            /// <summary>Releases all temporary objects and restores the captured global and scene state.</summary>
            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                try
                {
                    DestroyTemporaryObjects();
                    AssertTemporaryObjectsDestroyed();
                }
                finally
                {
                    RestoreEditorState();
                }
            }

            /// <summary>Destroys temporary resources in the same order used by the scope cleanup.</summary>
            private void DestroyTemporaryObjects()
            {
                DestroyMaterials();
                DestroyLights();
                DestroyQuad();
                DestroyTexture();
                DetachCameraTarget();
                ReleaseRenderTexture();
                DestroyCamera();
                ReleaseCommandBuffer();
            }

            /// <summary>Destroys temporary materials in reverse creation order.</summary>
            private void DestroyMaterials()
            {
                for (int index = materials.Count - 1; index >= 0; index--)
                {
                    UnityEngine.Object.DestroyImmediate(materials[index]);
                }

                materials.Clear();
            }

            /// <summary>Destroys the temporary Toon quad.</summary>
            private void DestroyQuad()
            {
                if (quadObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(quadObject);
                }
            }

            /// <summary>Destroys the temporary CPU readback texture.</summary>
            private void DestroyTexture()
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }

            /// <summary>Detaches the temporary camera from the shared render target before release.</summary>
            private void DetachCameraTarget()
            {
                Camera camera = cameraObject == null ? null : cameraObject.GetComponent<Camera>();
                if (camera != null)
                {
                    camera.targetTexture = null;
                }
            }

            /// <summary>Releases the D24S8 GPU resource only when this scope created it.</summary>
            private void ReleaseRenderTexture()
            {
                if (createdRenderTextureResource && renderTexture != null)
                {
                    renderTexture.Release();
                }
            }

            /// <summary>Destroys the temporary active-scene camera.</summary>
            private void DestroyCamera()
            {
                if (cameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
            }

            /// <summary>Releases the temporary command buffer.</summary>
            private void ReleaseCommandBuffer()
            {
                if (commandBuffer != null)
                {
                    commandBuffer.Release();
                }
            }

            /// <summary>Verifies that every temporary Unity object was destroyed.</summary>
            private void AssertTemporaryObjectsDestroyed()
            {
                Assert.That(quadObject == null, Is.True, "The Toon D24S8 scope must destroy its temporary quad.");
                Assert.That(cameraObject == null, Is.True, "The Toon D24S8 scope must destroy its temporary camera.");
                foreach (GameObject lightObject in lightObjects)
                {
                    Assert.That(lightObject == null, Is.True, "The Toon D24S8 scope must destroy every temporary directional light.");
                }
            }

            /// <summary>Restores the captured render target, quality, active scene, and scene dirty state.</summary>
            private void RestoreEditorState()
            {
                RenderTexture.active = activeRenderTexture;
                QualitySettings.pixelLightCount = pixelLightCount;
                RestoreSceneState();
            }

            /// <summary>Captures every global state this scope may observe or change.</summary>
            private void CaptureSharedState()
            {
                activeScene = SceneManager.GetActiveScene();
                sceneCount = SceneManager.sceneCount;
                pixelLightCount = QualitySettings.pixelLightCount;
                activeRenderTexture = RenderTexture.active;
                for (int index = 0; index < sceneCount; index++)
                {
                    Scene scene = SceneManager.GetSceneAt(index);
                    if (scene.IsValid() && scene.isLoaded)
                    {
                        sceneStates.Add(new SceneState(scene, scene.isDirty));
                    }
                }
            }

            /// <summary>Restores the active scene and verifies every loaded-scene dirty state remained unchanged.</summary>
            private void RestoreSceneState()
            {
                Assert.That(SceneManager.sceneCount, Is.EqualTo(sceneCount), "The Toon D24S8 scope must not add or remove loaded scenes.");
                if (activeScene.IsValid() && activeScene.isLoaded)
                {
                    if (SceneManager.GetActiveScene() != activeScene)
                    {
                        Assert.That(SceneManager.SetActiveScene(activeScene), Is.True, "The Toon D24S8 scope must restore the original active scene.");
                    }
                    Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(activeScene), "The Toon D24S8 scope must preserve the original active scene.");
                }

                foreach (SceneState state in sceneStates)
                {
                    Assert.That(state.scene.isLoaded, Is.True, "The Toon D24S8 scope must keep every initially loaded scene loaded.");
                    if (state.isDirty)
                    {
                        Assert.That(EditorSceneManager.MarkSceneDirty(state.scene), Is.True, "The Toon D24S8 scope must restore initially dirty scene state.");
                    }
                    Assert.That(state.scene.isDirty, Is.EqualTo(state.isDirty), "The Toon D24S8 scope must preserve clean and dirty scene states.");
                }
            }

            /// <summary>Selects a layer not used by any current scene object so existing scene rendering stays excluded.</summary>
            /// <returns>An unused user layer.</returns>
            private static int FindUnusedLayer()
            {
                for (int layer = 31; layer >= 8; layer--)
                {
                    if (!IsLayerInUse(layer))
                    {
                        return layer;
                    }
                }

                Assert.Fail("The Toon D24S8 scope requires one unused user layer to exclude existing scene rendering.");
                return 0;
            }

            /// <summary>Returns whether any loaded-scene object uses the candidate layer.</summary>
            /// <param name="layer">The candidate layer.</param>
            /// <returns>True when an existing scene object uses the layer.</returns>
            private static bool IsLayerInUse(int layer)
            {
                for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
                {
                    Scene scene = SceneManager.GetSceneAt(sceneIndex);
                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        continue;
                    }
                    foreach (GameObject rootObject in scene.GetRootGameObjects())
                    {
                        if (UsesLayer(rootObject.transform, layer))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            /// <summary>Returns whether a transform or descendant uses the candidate layer.</summary>
            /// <param name="transform">The transform to inspect.</param>
            /// <param name="layer">The candidate layer.</param>
            /// <returns>True when the transform hierarchy uses the layer.</returns>
            private static bool UsesLayer(Transform transform, int layer)
            {
                if (transform.gameObject.layer == layer)
                {
                    return true;
                }
                for (int index = 0; index < transform.childCount; index++)
                {
                    if (UsesLayer(transform.GetChild(index), layer))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>Creates a hidden temporary active-scene GameObject on the dedicated layer.</summary>
            /// <param name="name">The diagnostic object name.</param>
            /// <returns>The caller-owned temporary GameObject.</returns>
            private GameObject CreateHiddenObject(string name)
            {
                var gameObject = new GameObject(name);
                gameObject.hideFlags = HideFlags.HideAndDontSave;
                gameObject.layer = fixtureLayer;
                return gameObject;
            }

            /// <summary>Creates the requested ForcePixel directional lights with deterministic front-facing rotations.</summary>
            /// <param name="lightCount">The required light count.</param>
            private void SetLightCount(int lightCount)
            {
                Assert.That(lightCount, Is.GreaterThanOrEqualTo(1), "The Toon D24S8 scope requires at least one directional light.");
                DestroyLights();
                QualitySettings.pixelLightCount = Math.Max(2, pixelLightCount);
                int cullingMask = 1 << fixtureLayer;
                for (int index = 0; index < lightCount; index++)
                {
                    GameObject lightObject = CreateHiddenObject("PureBaseToonForwardAddLight" + index);
                    lightObjects.Add(lightObject);
                    Light light = lightObject.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.renderMode = LightRenderMode.ForcePixel;
                    light.color = Color.white;
                    light.intensity = 1.0f;
                    light.cullingMask = cullingMask;
                    lightObject.transform.rotation = Quaternion.Euler(30.0f, index == 0 ? -30.0f : 30.0f, 0.0f);
                }

                Assert.That(QualitySettings.pixelLightCount, Is.GreaterThanOrEqualTo(2), "The Toon D24S8 scope must allow at least two pixel lights.");
            }

            /// <summary>Destroys all temporary directional lights in reverse creation order.</summary>
            private void DestroyLights()
            {
                for (int index = lightObjects.Count - 1; index >= 0; index--)
                {
                    UnityEngine.Object.DestroyImmediate(lightObjects[index]);
                }
            }

            /// <summary>Creates one hidden Toon material with deterministic normal input and explicit Stencil state.</summary>
            /// <param name="shader">The Toon product shader.</param>
            /// <param name="stencilState">The required Stencil configuration.</param>
            /// <returns>The caller-owned configured material.</returns>
            private Material CreateToonMaterial(Shader shader, StencilState stencilState)
            {
                Assert.That(shader, Is.Not.Null, "The Toon shader is required for the D24S8 ForwardAdd scope.");
                var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                materials.Add(material);
                Assert.That(material.HasProperty("_BaseColor"), Is.True, "Toon must expose _BaseColor for the D24S8 ForwardAdd scope.");
                Assert.That(material.HasProperty("_Cutoff"), Is.True, "Toon must expose _Cutoff for the D24S8 ForwardAdd scope.");
                Assert.That(material.HasProperty("_NormalMap"), Is.True, "Toon must expose _NormalMap for the D24S8 ForwardAdd scope.");
                material.SetTexture("_BaseTexture", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", new Color(0.8f, 0.6f, 0.4f, 0.5f));
                material.SetFloat("_Cutoff", 0.5f);
                material.SetTexture("_NormalMap", Texture2D.normalTexture);
                material.SetFloat("_NormalScale", 1.0f);
                ConfigureMode(material, 2);
                ConfigureStencil(material, stencilState);
                return material;
            }

            /// <summary>Applies the complete public Stencil ABI to one Toon material.</summary>
            /// <param name="material">The material receiving Stencil values.</param>
            /// <param name="stencilState">The requested Stencil configuration.</param>
            private void ConfigureStencil(Material material, StencilState stencilState)
            {
                string[] properties = { "_StencilRef", "_StencilReadMask", "_StencilWriteMask", "_StencilComp", "_StencilPass", "_StencilFail", "_StencilZFail" };
                foreach (string property in properties)
                {
                    Assert.That(material.HasProperty(property), Is.True, "Toon is missing Stencil ABI property '" + property + "' for the D24S8 ForwardAdd scope. " + FormatDescription);
                }
                material.SetFloat("_StencilRef", stencilState.referenceValue);
                material.SetFloat("_StencilReadMask", stencilState.readMask);
                material.SetFloat("_StencilWriteMask", stencilState.writeMask);
                material.SetFloat("_StencilComp", (float)stencilState.comparison);
                material.SetFloat("_StencilPass", (float)stencilState.passOperation);
                material.SetFloat("_StencilFail", (float)stencilState.failOperation);
                material.SetFloat("_StencilZFail", (float)stencilState.depthFailOperation);
            }

            /// <summary>Clears color, depth, and the exact Stencil byte through the temporary command buffer.</summary>
            /// <param name="clearStencil">The Stencil value to install before rendering.</param>
            private void ClearTarget(byte clearStencil)
            {
                commandBuffer.Clear();
                commandBuffer.SetRenderTarget(renderTexture);
                commandBuffer.ClearRenderTarget(RTClearFlags.All, new Color(0.0f, 0.0f, 0.0f, 0.6f), 1.0f, clearStencil);
                Graphics.ExecuteCommandBuffer(commandBuffer);
            }
        }
    }
}
