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

// Verifies the PBR roughness floor through one disposable Progressive CPU lightmap bake.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Verifies stored PBR and Hybrid roughness values through one isolated real lightmap bake.</summary>
    public sealed class PureBaseRoughnessLightmapBakeTests
    {
        /// <summary>Identifies the temporary asset root that this test creates and removes as one transaction.</summary>
        private const string TemporaryRoot = "Assets/Artifacts/PureBaseRoughnessBake";

        /// <summary>Identifies the validated read-only Progressive CPU settings used by the disposable scene.</summary>
        private const string LightingSettingsPath = "Packages/jp.penguin.purebase/Tests/Fixtures/Lighting/PureBaseValidationLightingSettings.lighting";

        /// <summary>Identifies the isolated layer used by source surfaces that must not reach the readback camera.</summary>
        private const int SourceLayer = 30;

        /// <summary>Identifies the isolated layer used by baked-lightmap-only receiver surfaces.</summary>
        private const int ReceiverLayer = 31;

        /// <summary>Allows calibrated spatial and atlas variation when comparing equivalent baked floor observations.</summary>
        private const float BakeFloorEquivalenceTolerance = 0.005f;

        /// <summary>Requires one finite real bake and only equates below-floor and exact-floor observations.</summary>
        [Test]
        public void PbrAndHybridRoughnessFloorMatchesAfterOneProgressiveCpuBake()
        {
            SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
            LightingState lightingState = new LightingState();
            Scene owner = default;
            Scene scene = default;
            try
            {
                ResetTemporaryRoot();
                owner = CreateBakeOwnerScene();
                scene = CreateBakeScene();
                List<BakedCell> cells = CreateBakedCells(scene);
                Assert.That(Lightmapping.Bake(), Is.True, "The disposable Progressive CPU bake did not start.");
                var observations = new Dictionary<string, Color>();
                Camera camera = CreateReadbackCamera(scene);
                try
                {
                    foreach (BakedCell cell in cells)
                        observations.Add(cell.key, ReadBakedReceiver(camera, cell.renderer));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(camera.gameObject);
                }

                AssertBakedCells(observations);
            }
            finally
            {
                RestoreBakeState(setup, lightingState, owner, scene);
            }
        }

        /// <summary>Deletes stale disposable bake artifacts and creates the owned asset root.</summary>
        private static void ResetTemporaryRoot()
        {
            AssetDatabase.DeleteAsset(TemporaryRoot);
            if (!AssetDatabase.IsValidFolder("Assets/Artifacts"))
                AssetDatabase.CreateFolder("Assets", "Artifacts");
            AssetDatabase.CreateFolder("Assets/Artifacts", "PureBaseRoughnessBake");
        }

        /// <summary>Creates and saves the persistent owner required before opening the additive disposable bake scene.</summary>
        private static Scene CreateBakeOwnerScene()
        {
            Scene owner = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(owner, TemporaryRoot + "/Owner.unity"), Is.True);
            return owner;
        }

        /// <summary>Creates, saves, and configures the additive disposable scene with read-only Progressive CPU settings.</summary>
        private static Scene CreateBakeScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);
            LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            Assert.That(settings, Is.Not.Null, "The validated Progressive CPU Lighting Settings asset is unavailable.");
            Assert.That(settings.lightmapper, Is.EqualTo(LightingSettings.Lightmapper.ProgressiveCPU));
            Lightmapping.SetLightingSettingsForScene(scene, settings);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.reflectionIntensity = 0.0f;
            RenderSettings.fog = false;
            CreateBakedLight(scene);
            Assert.That(EditorSceneManager.SaveScene(scene, TemporaryRoot + "/RoughnessBake.unity"), Is.True);
            return scene;
        }

        /// <summary>Creates the single baked directional source for every isolated PBR-family receiver cell.</summary>
        private static void CreateBakedLight(Scene scene)
        {
            var lightObject = new GameObject("PureBase Roughness Bake Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = new Color(1.0f, 0.92f, 0.78f, 1.0f);
            light.intensity = 8.0f;
            light.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
        }

        /// <summary>Creates six spatially isolated PBR and Hybrid source-and-receiver cells at the requested stored roughness values.</summary>
        private static List<BakedCell> CreateBakedCells(Scene scene)
        {
            var cells = new List<BakedCell>();
            string[] shaders = { "PureBase/PBR", "PureBase/Hybrid" };
            float[] roughnesses = { 0.0f, 0.089f, 0.25f };
            for (int shaderIndex = 0; shaderIndex < shaders.Length; shaderIndex++)
            {
                for (int roughnessIndex = 0; roughnessIndex < roughnesses.Length; roughnessIndex++)
                    cells.Add(CreateBakedCell(scene, shaders[shaderIndex], roughnesses[roughnessIndex], shaderIndex, roughnessIndex));
            }

            return cells;
        }

        /// <summary>Creates a lit product source and an indirectly lit matched receiver that can be rendered from its baked lightmap.</summary>
        private static BakedCell CreateBakedCell(Scene scene, string shaderName, float roughness, int shaderIndex, int roughnessIndex)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, "Missing product shader '" + shaderName + "'.");
            var sourceMaterial = new Material(shader);
            sourceMaterial.SetTexture("_BaseTexture", Texture2D.whiteTexture);
            sourceMaterial.SetColor("_BaseColor", Color.white);
            sourceMaterial.SetFloat("_Metallic", 0.9f);
            sourceMaterial.SetFloat("_Roughness", roughness);
            string key = shaderName + " " + roughness.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
            string materialPrefix = TemporaryRoot + "/" + shader.name.Replace("/", "-") + "-" + roughnessIndex;
            AssetDatabase.CreateAsset(sourceMaterial, materialPrefix + "-source.mat");
            var receiverMaterial = new Material(Shader.Find("Standard"));
            receiverMaterial.color = Color.white;
            AssetDatabase.CreateAsset(receiverMaterial, materialPrefix + "-receiver.mat");
            Vector3 sourcePosition = new Vector3((roughnessIndex - 1) * 10.0f, 0.0f, shaderIndex * 12.0f);
            CreateBakedSource(scene, sourcePosition, sourceMaterial, key);
            MeshRenderer receiver = CreateBakedReceiver(scene, sourcePosition, receiverMaterial, key);
            return new BakedCell(key, receiver);
        }

        /// <summary>Creates the horizontal product source whose Meta albedo feeds a nearby receiver through bounced baked light.</summary>
        private static void CreateBakedSource(Scene scene, Vector3 position, Material material, string key)
        {
            GameObject source = GameObject.CreatePrimitive(PrimitiveType.Plane);
            source.name = "PureBase Roughness Source " + key;
            source.transform.position = position;
            source.transform.localScale = Vector3.one * 0.35f;
            source.layer = SourceLayer;
            SceneManager.MoveGameObjectToScene(source, scene);
            MeshRenderer renderer = source.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveGI = ReceiveGI.Lightmaps;
            GameObjectUtility.SetStaticEditorFlags(source, StaticEditorFlags.ContributeGI);
        }

        /// <summary>Creates a vertical Standard receiver that has no direct contribution from the downward baked light.</summary>
        private static MeshRenderer CreateBakedReceiver(Scene scene, Vector3 sourcePosition, Material material, string key)
        {
            GameObject receiver = GameObject.CreatePrimitive(PrimitiveType.Plane);
            receiver.name = "PureBase Roughness Receiver " + key;
            receiver.transform.position = sourcePosition + new Vector3(0.0f, 1.5f, 1.5f);
            receiver.transform.rotation = Quaternion.Euler(-90.0f, 0.0f, 0.0f);
            receiver.transform.localScale = Vector3.one * 0.35f;
            receiver.layer = ReceiverLayer;
            SceneManager.MoveGameObjectToScene(receiver, scene);
            MeshRenderer renderer = receiver.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.receiveGI = ReceiveGI.Lightmaps;
            GameObjectUtility.SetStaticEditorFlags(receiver, StaticEditorFlags.ContributeGI);
            return renderer;
        }

        /// <summary>Creates a manually rendered camera that can see only the baked receiver layer.</summary>
        private static Camera CreateReadbackCamera(Scene scene)
        {
            var cameraObject = new GameObject("PureBase Roughness Bake Readback Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 2.0f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 10.0f;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 1 << ReceiverLayer;
            camera.useOcclusionCulling = false;
            return camera;
        }

        /// <summary>Renders the centre of a receiver in baked-lightmap-only mode through a transient HDR readback texture.</summary>
        private static Color ReadBakedReceiver(Camera camera, MeshRenderer renderer)
        {
            Assert.That(renderer.lightmapIndex, Is.GreaterThanOrEqualTo(0), renderer.name + " has no baked lightmap index.");
            RenderTexture active = RenderTexture.active;
            var target = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            try
            {
                Vector3 normal = renderer.transform.up;
                camera.transform.position = renderer.bounds.center + (normal * 2.0f);
                camera.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                readback.Apply(false, false);
                return readback.GetPixel(32, 32);
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        /// <summary>Requires finite nonnegative nonblack data and below-floor equivalence for both baked product receivers.</summary>
        private static void AssertBakedCells(IReadOnlyDictionary<string, Color> observations)
        {
            WriteBakedEvidence(observations, "PureBase/PBR");
            WriteBakedEvidence(observations, "PureBase/Hybrid");
            var failures = new List<string>();
            foreach (KeyValuePair<string, Color> observation in observations)
                AddBakedObservationFailures(failures, observation.Value, observation.Key);
            AddBakedFloorEquivalenceFailure(failures, observations, "PureBase/PBR");
            AddBakedFloorEquivalenceFailure(failures, observations, "PureBase/Hybrid");
            AddBakedRoughnessDiscriminationFailure(failures, observations, "PureBase/PBR");
            AddBakedRoughnessDiscriminationFailure(failures, observations, "PureBase/Hybrid");
            Assert.That(failures.Count, Is.EqualTo(0), string.Join(Environment.NewLine, failures));
        }

        /// <summary>Requires the two stored values that must share the new runtime floor to match after baking.</summary>
        private static void AddBakedFloorEquivalenceFailure(List<string> failures, IReadOnlyDictionary<string, Color> observations, string shaderName)
        {
            Color below = observations[shaderName + " 0.000"];
            Color exact = observations[shaderName + " 0.089"];
            if (MaximumDifference(below, exact) > BakeFloorEquivalenceTolerance)
                failures.Add(DescribeBakedDeltas(observations, shaderName) + ". " + shaderName + " baked below-floor output must equal exact-floor output.");
        }

        /// <summary>Requires the above-floor cell to prove that the sampled baked receiver is roughness-sensitive.</summary>
        private static void AddBakedRoughnessDiscriminationFailure(List<string> failures, IReadOnlyDictionary<string, Color> observations, string shaderName)
        {
            Color exact = observations[shaderName + " 0.089"];
            Color above = observations[shaderName + " 0.250"];
            if (MaximumDifference(exact, above) <= 0.0005f)
                failures.Add(DescribeBakedDeltas(observations, shaderName) + ". " + shaderName + " baked 0.25 output must differ from exact-floor output.");
        }

        /// <summary>Describes both required roughness deltas with full single-precision fidelity for assertion failures.</summary>
        private static string DescribeBakedDeltas(IReadOnlyDictionary<string, Color> observations, string shaderName)
        {
            float floorDifference = MaximumDifference(observations[shaderName + " 0.000"], observations[shaderName + " 0.089"]);
            float discriminationDifference = MaximumDifference(observations[shaderName + " 0.089"], observations[shaderName + " 0.250"]);
            return shaderName + " baked deltas: stored 0.000/exact 0.089 = " + floorDifference.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "; exact 0.089/above 0.250 = " + discriminationDifference.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Adds failures for one sampled baked observation that lacks valid nonblack HDR evidence.</summary>
        private static void AddBakedObservationFailures(List<string> failures, Color color, string label)
        {
            if (!float.IsFinite(color.r) || !float.IsFinite(color.g) || !float.IsFinite(color.b))
                failures.Add(label + " baked lightmap is non-finite.");
            if (color.r < 0.0f || color.g < 0.0f || color.b < 0.0f)
                failures.Add(label + " baked lightmap is negative.");
            if (color.maxColorComponent <= 0.001f)
                failures.Add(label + " baked lightmap is black.");
        }

        /// <summary>Writes the stored source roughness observations and both required deltas to the focused test output.</summary>
        private static void WriteBakedEvidence(IReadOnlyDictionary<string, Color> observations, string shaderName)
        {
            WriteBakedColor(observations, shaderName, "0.000");
            WriteBakedColor(observations, shaderName, "0.089");
            WriteBakedColor(observations, shaderName, "0.250");
            WriteBakedDifference(observations, shaderName, "0.000", "0.089");
            WriteBakedDifference(observations, shaderName, "0.089", "0.250");
        }

        /// <summary>Writes one baked receiver RGB observation with an invariant decimal representation.</summary>
        private static void WriteBakedColor(IReadOnlyDictionary<string, Color> observations, string shaderName, string roughness)
        {
            Color color = observations[shaderName + " " + roughness];
            TestContext.WriteLine(shaderName + " receiver " + roughness + " RGB = (" + color.r.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) + ", " + color.g.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) + ", " + color.b.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        /// <summary>Writes one maximum RGB delta between two stored source roughness observations.</summary>
        private static void WriteBakedDifference(IReadOnlyDictionary<string, Color> observations, string shaderName, string firstRoughness, string secondRoughness)
        {
            float difference = MaximumDifference(observations[shaderName + " " + firstRoughness], observations[shaderName + " " + secondRoughness]);
            TestContext.WriteLine(shaderName + " receiver delta " + firstRoughness + "/" + secondRoughness + " = " + difference.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Calculates the maximum absolute RGB difference used by the bake-specific equivalence tolerance.</summary>
        private static float MaximumDifference(Color first, Color second)
        {
            return Mathf.Max(Mathf.Abs(first.r - second.r), Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b));
        }

        /// <summary>Restores all scene, lightmap, lighting, renderer, and temporary asset state in failure-safe cleanup order.</summary>
        private static void RestoreBakeState(SceneSetup[] setup, LightingState lightingState, Scene owner, Scene scene)
        {
            try
            {
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
            finally
            {
                try
                {
                    lightingState.Restore();
                }
                finally
                {
                    try
                    {
                        EditorSceneManager.RestoreSceneManagerSetup(setup);
                        CloseOwnerAfterRestoringSetup(owner);
                    }
                    finally
                    {
                        try
                        {
                            AssetDatabase.DeleteAsset(TemporaryRoot);
                        }
                        finally
                        {
                            AssetDatabase.Refresh();
                        }
                    }
                }
            }
        }

        /// <summary>Closes a residual owner only after original setup restoration guarantees another loaded scene.</summary>
        private static void CloseOwnerAfterRestoringSetup(Scene owner)
        {
            if (owner.IsValid() && owner.isLoaded && SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(owner, true);
        }

        /// <summary>Pairs one receiver renderer with its stable product and stored-roughness key.</summary>
        private sealed class BakedCell
        {
            /// <summary>Initializes one baked receiver cell.</summary>
            public BakedCell(string key, MeshRenderer renderer)
            {
                this.key = key;
                this.renderer = renderer;
            }

            /// <summary>Gets the stable observation key for the receiver.</summary>
            public string key { get; }

            /// <summary>Gets the receiver that receives and references the baked lightmap.</summary>
            public MeshRenderer renderer { get; }
        }

        /// <summary>Captures every global lighting value changed by the disposable bake before creating its owner scene.</summary>
        private sealed class LightingState
        {
            /// <summary>Initializes the captured global lightmap and rendering state.</summary>
            public LightingState()
            {
                lightmaps = LightmapSettings.lightmaps;
                lightmapsMode = LightmapSettings.lightmapsMode;
                lightingData = Lightmapping.lightingDataAsset;
                ambientMode = RenderSettings.ambientMode;
                ambientLight = RenderSettings.ambientLight;
                reflectionIntensity = RenderSettings.reflectionIntensity;
                fog = RenderSettings.fog;
            }

            /// <summary>Restores every global lightmap and rendering value modified by the disposable bake.</summary>
            public void Restore()
            {
                LightmapSettings.lightmaps = lightmaps;
                LightmapSettings.lightmapsMode = lightmapsMode;
                Lightmapping.lightingDataAsset = lightingData;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.fog = fog;
            }

            /// <summary>Stores the original lightmap textures.</summary>
            private readonly LightmapData[] lightmaps;

            /// <summary>Stores the original lightmap sampling mode.</summary>
            private readonly LightmapsMode lightmapsMode;

            /// <summary>Stores the original baked lighting data asset.</summary>
            private readonly LightingDataAsset lightingData;

            /// <summary>Stores the original ambient lighting mode.</summary>
            private readonly AmbientMode ambientMode;

            /// <summary>Stores the original ambient lighting color.</summary>
            private readonly Color ambientLight;

            /// <summary>Stores the original reflection contribution.</summary>
            private readonly float reflectionIntensity;

            /// <summary>Stores the original fog enabled state.</summary>
            private readonly bool fog;
        }
    }
}
