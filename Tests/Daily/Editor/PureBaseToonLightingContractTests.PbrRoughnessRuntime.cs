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

// Owns direct and reflection fixture setup for PBR perceptual-roughness GPU observations.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Provides roughness-specific extensions to the isolated BIRP capture scope.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Owns direct and reflection capture additions for the PBR roughness floor contracts.</summary>
        private partial class ToonLightingCaptureRuntimeScope
        {
            /// <summary>Renders one full 64x64 direct PBR visibility frame with measured incidence coordinates.</summary>
            public PbrVisibilityObservation RenderPbrVisibilityReference(string shaderName, string passName, float metallic, float roughness, Vector3 normal, string incidence, Vector3? lightDirectionOverride = null)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Vector3 normalizedNormal = normal.normalized;
                Vector3 lightDirection = lightDirectionOverride ?? Vector3.Reflect(Vector3.forward, normalizedNormal).normalized;
                Material material = CreatePbrRoughnessMaterial(shaderName, passName, roughness, metallic);
                LightCaptureRequest request = passName == "ForwardAdd"
                    ? CreateLightCaptureRequest(normalizedNormal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 1.0f), ShCoefficients.Zero, LightType.Point, 4.0f)
                    : CreateDirectionalLightCaptureRequest(normalizedNormal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f), ShCoefficients.Zero);
                PbrVisibilityFrame frame = passName == "ForwardAdd"
                    ? RenderPbrVisibilityLightDifference(material, request)
                    : RenderPbrVisibilityDirectionalLightDifference(material, request);
                var input = new PbrVisibilityRenderInput(shaderName, passName, metallic, roughness, normal, incidence);
                return new PbrVisibilityObservation(input, frame, request, camera, meshFilter.transform);
            }

            /// <summary>Renders a low-radiance metallic direct observation through an explicit forward pass.</summary>
            public Color RenderPbrRoughnessDirect(string shaderName, string passName, float roughness, Vector3 normal)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                Material material = CreatePbrRoughnessMaterial(shaderName, passName, roughness, 1.0f);
                Vector3 lightDirection = Vector3.Reflect(Vector3.forward, normal.normalized).normalized;
                if (passName == "ForwardAdd")
                    return RenderLightDifference(material, CreateLightCaptureRequest(normal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 1.0f), ShCoefficients.Zero, LightType.Point, 4.0f));
                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(normal, new Vector4(0.015f, 0.012f, 0.009f, 1.0f), new Vector4(lightDirection.x, lightDirection.y, lightDirection.z, 0.0f), ShCoefficients.Zero));
            }

            /// <summary>Renders direct-light-free metallic reflection from fixture-owned mip-distinct cubemap data.</summary>
            public Color RenderPbrRoughnessReflection(string shaderName, float roughness)
            {
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
                ConfigurePbrRoughnessReflection();
                Material material = CreatePbrRoughnessMaterial(shaderName, "ForwardBase", roughness, 1.0f);
                return RenderWithLights(material, CreateDirectionalLightCaptureRequest(Vector3.back, Vector4.zero, new Vector4(0.0f, 0.0f, -1.0f, 0.0f), ShCoefficients.Zero));
            }

            /// <summary>Creates a high-albedo metallic PBR-family material without a direct-diffuse contribution.</summary>
            private Material CreatePbrRoughnessMaterial(string shaderName, string passName, float roughness, float metallic)
            {
                Material material = CreateProductMaterial(shaderName, passName, metallic);
                material.SetTexture("_BaseTexture", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Roughness", roughness);
                material.SetInteger("_UseUnityStandardDiffuseBrightness", 0);
                return material;
            }

            /// <summary>Renders one explicit light configuration and copies all 64x64 linear float samples before cleanup.</summary>
            private PbrVisibilityFrame RenderPbrVisibilityWithLights(Material material, LightCaptureRequest request)
            {
                var lightObjects = new System.Collections.Generic.List<GameObject>();
                try
                {
                    InjectShGlobals(request.coefficients);
                    ApplyShProperties(request.coefficients);
                    meshFilter.sharedMesh = CreateNormalControlledQuad(request.normal);
                    renderer.sharedMaterial = material;
                    renderer.enabled = true;
                    CreateLights(lightObjects, request);
                    camera.Render();
                    Assert.That(camera.actualRenderingPath, Is.EqualTo(RenderingPath.Forward), "Visibility capture requires the BIRP Forward camera path.");
                    return CreatePbrVisibilityFrame(lightObjects);
                }
                finally
                {
                    renderer.enabled = false;
                    renderer.SetPropertyBlock(null);
                    DestroyGameObjects(lightObjects);
                }
            }

            /// <summary>Renders one and two equivalent Point lights, returning the full isolated second-light frame.</summary>
            private PbrVisibilityFrame RenderPbrVisibilityLightDifference(Material material, LightCaptureRequest request)
            {
                request.lightCount = 1;
                PbrVisibilityFrame oneLight = RenderPbrVisibilityWithLights(material, request);
                request.lightCount = 2;
                PbrVisibilityFrame twoLights = RenderPbrVisibilityWithLights(material, request);
                return CreatePbrVisibilityDifference(oneLight, twoLights);
            }

            /// <summary>Isolates the Directional Light contribution from unchanged BIRP indirect lighting.</summary>
            private PbrVisibilityFrame RenderPbrVisibilityDirectionalLightDifference(Material material, LightCaptureRequest request)
            {
                LightCaptureRequest noLight = CreateDirectionalLightCaptureRequest(request.normal, Vector4.zero, request.lightPosition, request.coefficients);
                PbrVisibilityFrame withoutLight = RenderPbrVisibilityWithLights(material, noLight);
                PbrVisibilityFrame withLight = RenderPbrVisibilityWithLights(material, request);
                return CreatePbrVisibilityDifference(withoutLight, withLight);
            }

            /// <summary>Returns the second frame's measured geometry with its isolated RGB contribution.</summary>
            private static PbrVisibilityFrame CreatePbrVisibilityDifference(PbrVisibilityFrame first, PbrVisibilityFrame second)
            {
                var pixels = new Color[first.Pixels.Length];
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = second.Pixels[index] - first.Pixels[index];
                return new PbrVisibilityFrame(pixels, second.Normal, second.LightDirection, second.ViewDirection, second.LightTransformPosition, second.LightTransformRotation);
            }

            /// <summary>Captures the render-used geometry from the configured mesh, camera, and first Unity Light.</summary>
            private PbrVisibilityFrame CreatePbrVisibilityFrame(List<GameObject> lightObjects)
            {
                Vector3 normal = meshFilter.transform.TransformDirection(meshFilter.sharedMesh.normals[0]).normalized;
                Vector3 viewDirection = -camera.transform.forward;
                if (lightObjects.Count == 0)
                    return new PbrVisibilityFrame(ReadPixels(), normal, Vector3.zero, viewDirection, Vector3.zero, Quaternion.identity);
                Light light = lightObjects[0].GetComponent<Light>();
                Vector3 lightDirection = light.type == LightType.Directional
                    ? -light.transform.forward
                    : (light.transform.position - meshFilter.transform.position).normalized;
                return new PbrVisibilityFrame(ReadPixels(), normal, lightDirection, viewDirection, light.transform.position, light.transform.rotation);
            }

            /// <summary>Installs a transient custom reflection cubemap with distinct finite colors in every mip level.</summary>
            private void ConfigurePbrRoughnessReflection()
            {
                var cubemap = new Cubemap(8, TextureFormat.RGBAFloat, true) { hideFlags = HideFlags.HideAndDontSave };
                pbrBrightnessResources.Add(cubemap);
                for (int mip = 0; mip < cubemap.mipmapCount; mip++)
                {
                    Color color = new Color(0.12f + (mip * 0.21f), 0.08f + (mip * 0.13f), 0.04f + (mip * 0.07f), 1.0f);
                    int size = Mathf.Max(1, cubemap.width >> mip);
                    Color[] pixels = CreatePbrRoughnessMipPixels(size, color);
                    foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
                        cubemap.SetPixels(pixels, face, mip);
                }

                cubemap.Apply(false, true);
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cubemap;
                RenderSettings.reflectionIntensity = 1.0f;
            }

            /// <summary>Creates the uniformly colored pixels assigned to one owned cubemap mip level.</summary>
            private static Color[] CreatePbrRoughnessMipPixels(int size, Color color)
            {
                var pixels = new Color[size * size];
                for (int index = 0; index < pixels.Length; index++)
                    pixels[index] = color;
                return pixels;
            }
        }

        /// <summary>Stores a complete PBR visibility frame and its measured fixture inputs.</summary>
        private readonly struct PbrVisibilityObservation
        {
            /// <summary>Initializes one frame observation.</summary>
            public PbrVisibilityObservation(PbrVisibilityRenderInput input, PbrVisibilityFrame frame, LightCaptureRequest request, Camera camera, Transform meshTransform)
            {
                ShaderName = input.ShaderName;
                PassName = input.PassName;
                Metallic = input.Metallic;
                Roughness = input.Roughness;
                Incidence = input.Incidence;
                Pixels = frame.Pixels;
                Normal = frame.Normal;
                LightDirection = frame.LightDirection;
                ViewDirection = frame.ViewDirection;
                MeasuredNdotL = Vector3.Dot(Normal, LightDirection);
                MeasuredNdotV = Vector3.Dot(Normal, ViewDirection);
                LightPlusView = LightDirection + ViewDirection;
                HalfVector = LightPlusView.sqrMagnitude > 0.0f ? LightPlusView.normalized : Vector3.zero;
                LightType = request.lightType;
                LightColor = request.lightColor;
                LightRange = request.range;
                LightTransformPosition = frame.LightTransformPosition;
                LightTransformRotation = frame.LightTransformRotation;
                CameraPosition = camera.transform.position;
                CameraRotation = camera.transform.rotation;
                CameraOrthographicSize = camera.orthographicSize;
                CameraNearClipPlane = camera.nearClipPlane;
                CameraFarClipPlane = camera.farClipPlane;
                CameraRenderingPath = camera.actualRenderingPath.ToString();
                MeshPosition = meshTransform.position;
                MeshRotation = meshTransform.rotation;
                MeshScale = meshTransform.lossyScale;
            }

            /// <summary>Gets the full linear ARGBFloat frame.</summary>
            public Color[] Pixels { get; }

            /// <summary>Gets the center frame sample.</summary>
            public Color Center => Pixels[31 + (31 * 64)];

            /// <summary>Gets the measured light cosine.</summary>
            public float MeasuredNdotL { get; }

            /// <summary>Gets the measured view cosine.</summary>
            public float MeasuredNdotV { get; }

            /// <summary>Gets the measured uniform mesh normal.</summary>
            public Vector3 Normal { get; }

            /// <summary>Gets the measured normalized light vector.</summary>
            public Vector3 LightDirection { get; }

            /// <summary>Gets the measured normalized camera view vector.</summary>
            public Vector3 ViewDirection { get; }

            /// <summary>Gets the measured unnormalized light-plus-view vector.</summary>
            public Vector3 LightPlusView { get; }

            /// <summary>Gets the measured half vector, or zero when the light-plus-view vector degenerates.</summary>
            public Vector3 HalfVector { get; }

            /// <summary>Gets the real Unity light type used for this observation.</summary>
            public LightType LightType { get; }

            /// <summary>Gets the linear light color requested for this observation.</summary>
            public Vector4 LightColor { get; }

            /// <summary>Gets the configured Point-light range.</summary>
            public float LightRange { get; }

            /// <summary>Gets the generated light transform position.</summary>
            public Vector3 LightTransformPosition { get; }

            /// <summary>Gets the generated light transform rotation.</summary>
            public Quaternion LightTransformRotation { get; }

            /// <summary>Gets the capture camera transform position.</summary>
            public Vector3 CameraPosition { get; }

            /// <summary>Gets the capture camera transform rotation.</summary>
            public Quaternion CameraRotation { get; }

            /// <summary>Gets the capture camera orthographic size.</summary>
            public float CameraOrthographicSize { get; }

            /// <summary>Gets the capture camera near clip plane.</summary>
            public float CameraNearClipPlane { get; }

            /// <summary>Gets the capture camera far clip plane.</summary>
            public float CameraFarClipPlane { get; }

            /// <summary>Gets the actual camera rendering path used by the render.</summary>
            public string CameraRenderingPath { get; }

            /// <summary>Gets the mesh transform position at render time.</summary>
            public Vector3 MeshPosition { get; }

            /// <summary>Gets the mesh transform rotation at render time.</summary>
            public Quaternion MeshRotation { get; }

            /// <summary>Gets the mesh transform lossy scale at render time.</summary>
            public Vector3 MeshScale { get; }

            /// <summary>Gets the human-readable observation label.</summary>
            public string Label => ShaderName + " " + PassName + " m=" + Metallic.ToString(CultureInfo.InvariantCulture) + " r=" + Roughness.ToString(CultureInfo.InvariantCulture) + " " + Incidence;

            /// <summary>Gets the deterministic diagnostic filename.</summary>
            public string FileName => ShaderName.Replace("/", "-") + "-" + PassName + "-m" + Metallic.ToString("0", CultureInfo.InvariantCulture) + "-r" + Roughness.ToString("0.###", CultureInfo.InvariantCulture) + "-" + Incidence + ".png";

            /// <summary>Gets whether every RGB frame sample is finite.</summary>
            public bool FrameFinite
            {
                get
                {
                    foreach (Color pixel in Pixels)
                        if (!float.IsFinite(pixel.r) || !float.IsFinite(pixel.g) || !float.IsFinite(pixel.b)) return false;
                    return true;
                }
            }

            /// <summary>Gets whether the center RGB sample is finite.</summary>
            public bool CenterFinite => float.IsFinite(Center.r) && float.IsFinite(Center.g) && float.IsFinite(Center.b);

            /// <summary>Gets the source shader name.</summary>
            public string ShaderName { get; }

            /// <summary>Gets the rendered forward-pass name.</summary>
            public string PassName { get; }

            /// <summary>Gets the material metallic value.</summary>
            public float Metallic { get; }

            /// <summary>Gets the material perceptual roughness.</summary>
            public float Roughness { get; }

            /// <summary>Gets the measured incidence label.</summary>
            public string Incidence { get; }
        }

        /// <summary>Stores one PBR visibility frame with render-used fixture geometry.</summary>
        private readonly struct PbrVisibilityFrame
        {
            /// <summary>Initializes one rendered frame and its measured scene inputs.</summary>
            public PbrVisibilityFrame(Color[] pixels, Vector3 normal, Vector3 lightDirection, Vector3 viewDirection, Vector3 lightTransformPosition, Quaternion lightTransformRotation)
            {
                Pixels = pixels;
                Normal = normal;
                LightDirection = lightDirection;
                ViewDirection = viewDirection;
                LightTransformPosition = lightTransformPosition;
                LightTransformRotation = lightTransformRotation;
            }

            /// <summary>Gets the linear frame readback.</summary>
            public Color[] Pixels { get; }

            /// <summary>Gets the world-space receiver normal.</summary>
            public Vector3 Normal { get; }

            /// <summary>Gets the Unity Light direction toward the receiver.</summary>
            public Vector3 LightDirection { get; }

            /// <summary>Gets the world-space direction from receiver to camera.</summary>
            public Vector3 ViewDirection { get; }

            /// <summary>Gets the constructed Unity Light transform position.</summary>
            public Vector3 LightTransformPosition { get; }

            /// <summary>Gets the constructed Unity Light transform rotation.</summary>
            public Quaternion LightTransformRotation { get; }
        }

        /// <summary>Stores the identity of a validated immutable legacy capture selected for fast evidence.</summary>
        private sealed class VisibilityCaptureReference
        {
            /// <summary>Initializes the legacy capture identity used by a fast evidence bundle.</summary>
            public VisibilityCaptureReference(string captureId, string directory)
            {
                CaptureId = captureId;
                Directory = directory;
            }

            /// <summary>Gets the selected legacy capture identifier.</summary>
            public string CaptureId { get; }

            /// <summary>Gets the selected legacy capture directory.</summary>
            public string Directory { get; }
        }

        /// <summary>Deserializes the legacy capture identity fields required for immutable fast-reference validation.</summary>
        [Serializable]
        private sealed class VisibilityCaptureManifest
        {
            /// <summary>Gets or sets the capture identifier recorded by the legacy evidence exporter.</summary>
            public string captureId;

            /// <summary>Gets or sets the formula state recorded by the legacy evidence exporter.</summary>
            public string formula;

            /// <summary>Gets or sets the immutable input fingerprint recorded by the legacy evidence exporter.</summary>
            public string inputsSha256;
        }

        /// <summary>Writes complete diagnostic PNGs, frame statistics, and formula characterization records.</summary>
        private static void WriteVisibilityObservations(string directory, List<PbrVisibilityObservation> observations, string formula)
        {
            Directory.CreateDirectory(directory);
            var entries = new List<string>();
            foreach (PbrVisibilityObservation observation in observations)
            {
                string fileName = observation.FileName;
                string path = Path.Combine(directory, fileName);
                byte[] png = EncodeDiagnosticPng(observation.Pixels);
                File.WriteAllBytes(path, png);
                string pngHash = Sha256(png);
                Assert.That(Sha256(File.ReadAllBytes(path)), Is.EqualTo(pngHash), "Each diagnostic PNG must be written and rehashed before export completes.");
                entries.Add("    { \"name\": " + JsonString(fileName) + ", \"center\": " + JsonColor(observation.Center) + ", \"centerFinite\": " + (observation.CenterFinite ? "true" : "false") + ", \"frame\": " + BuildFrameSummary(observation.Pixels) + ", \"frameFinite\": " + (observation.FrameFinite ? "true" : "false") + ", \"ndotL\": " + JsonFloat(observation.MeasuredNdotL) + ", \"ndotV\": " + JsonFloat(observation.MeasuredNdotV) + ", \"lightPlusView\": " + JsonVector3(observation.LightPlusView) + ", \"halfVectorDefined\": " + (observation.LightPlusView.sqrMagnitude > 0.0f ? "true" : "false") + ", \"sha256\": " + JsonString(pngHash) + " }");
            }

            string observationsManifest = "{\n  \"formula\": " + JsonString(formula) + ",\n  \"inputsSha256\": " + JsonString(Sha256(File.ReadAllBytes(Path.Combine(directory, "inputs.json")))) + ",\n  \"observations\": [\n" + string.Join(",\n", entries) + "\n  ]\n}\n";
            string observationsPath = Path.Combine(directory, "observations.json");
            File.WriteAllText(observationsPath, observationsManifest, new UTF8Encoding(false));
            Assert.That(File.ReadAllText(observationsPath), Is.EqualTo(observationsManifest), "observations.json must be written without transformation.");
            string characterization = PureBasePbrVisibilityApproximationTests.BuildVisibilityCharacterizationArtifact();
            string characterizationPath = Path.Combine(directory, "characterization.json");
            File.WriteAllText(characterizationPath, characterization, new UTF8Encoding(false));
            Assert.That(File.ReadAllText(characterizationPath), Is.EqualTo(characterization), "characterization.json must be written without transformation.");
        }

        /// <summary>Summarizes every HDR RGB sample without replacing the diagnostic frame.</summary>
        private static string BuildFrameSummary(Color[] pixels)
        {
            Color minimum = new Color(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            Color maximum = new Color(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            Color total = Color.clear;
            foreach (Color pixel in pixels)
            {
                minimum = new Color(Mathf.Min(minimum.r, pixel.r), Mathf.Min(minimum.g, pixel.g), Mathf.Min(minimum.b, pixel.b), Mathf.Min(minimum.a, pixel.a));
                maximum = new Color(Mathf.Max(maximum.r, pixel.r), Mathf.Max(maximum.g, pixel.g), Mathf.Max(maximum.b, pixel.b), Mathf.Max(maximum.a, pixel.a));
                total += pixel;
            }

            return "{ \"pixelCount\": " + pixels.Length.ToString(CultureInfo.InvariantCulture) + ", \"minimum\": " + JsonColor(minimum) + ", \"maximum\": " + JsonColor(maximum) + ", \"mean\": " + JsonColor(total / pixels.Length) + " }";
        }

        /// <summary>Writes the focused NUnit success record after all capture assertions and hash writes complete.</summary>
        private static void WriteNUnitResult(string directory, string captureId, string fingerprint)
        {
            string hashPath = Path.Combine(directory, "hashes.json");
            string result = "{\n  \"framework\": \"NUnit\",\n  \"testId\": " + JsonString(TestContext.CurrentContext.Test.ID) + ",\n  \"testName\": " + JsonString(TestContext.CurrentContext.Test.FullName) + ",\n  \"result\": \"Passed\",\n  \"resultScope\": \"all GPU observations and capture artifacts completed before test return\",\n  \"captureId\": " + JsonString(captureId) + ",\n  \"inputsSha256\": " + JsonString(fingerprint) + ",\n  \"hashManifestSha256\": " + JsonString(Sha256(File.ReadAllBytes(hashPath))) + "\n}\n";
            string resultPath = Path.Combine(directory, "nunit-result.json");
            File.WriteAllText(resultPath, result, new UTF8Encoding(false));
            Assert.That(File.ReadAllText(resultPath), Is.EqualTo(result), "nunit-result.json must be written without transformation.");
        }

        /// <summary>Copies the exact Unity Editor log after writing a capture-linked diagnostic entry.</summary>
        private static void WriteUnityLogSnapshot(string directory, string captureId, string fingerprint)
        {
            string logPath = Application.consoleLogPath;
            Assert.That(File.Exists(logPath), Is.True, "Unity Editor log must exist to audit the exact GPU capture.");
            string log = ReadSharedLog(logPath);
            Assert.That(log.IndexOf(captureId, StringComparison.Ordinal) >= 0 && log.IndexOf(fingerprint, StringComparison.Ordinal) >= 0, Is.True, "Unity Editor log must contain the capture-linked diagnostic entry.");
            File.WriteAllText(Path.Combine(directory, "unity-editor.log"), log, new UTF8Encoding(false));
        }

        /// <summary>Reads Unity's actively written editor log without requesting exclusive file access.</summary>
        private static string ReadSharedLog(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream))
                return reader.ReadToEnd();
        }

        /// <summary>Writes the immutable capture identity that links inputs, formula, and audit artifacts.</summary>
        private static void WriteCaptureManifest(string directory, string captureId, string fingerprint, int observationCount, string formula, VisibilityCaptureReference reference)
        {
            string referenceMetadata = reference == null ? string.Empty : "  \"reference\": { \"captureId\": " + JsonString(reference.CaptureId) + ", \"path\": " + JsonString(reference.Directory) + ", \"inputsSha256\": " + JsonString(fingerprint) + " },\n";
            string manifest = "{\n  \"schemaVersion\": 1,\n  \"captureId\": " + JsonString(captureId) + ",\n  \"formula\": " + JsonString(formula) + ",\n  \"inputsSha256\": " + JsonString(fingerprint) + ",\n" + referenceMetadata + "  \"observationCount\": " + observationCount.ToString(CultureInfo.InvariantCulture) + ",\n  \"nunitResult\": \"nunit-result.json\",\n  \"unityLog\": \"unity-editor.log\"\n}\n";
            string path = Path.Combine(directory, "capture.json");
            File.WriteAllText(path, manifest, new UTF8Encoding(false));
            Assert.That(File.ReadAllText(path), Is.EqualTo(manifest), "capture.json must be written without transformation.");
        }

        /// <summary>Writes and verifies SHA-256 records for every immutable capture artifact except its final NUnit receipt.</summary>
        private static void WriteVisibilityHashList(string directory, string fingerprint)
        {
            var entries = new List<string>();
            foreach (string file in new[] { "inputs.json", "observations.json", "characterization.json", "unity-editor.log", "capture.json" })
                entries.Add(BuildHashEntry(directory, file));
            foreach (string path in Directory.GetFiles(directory, "*.png"))
                entries.Add(BuildHashEntry(directory, Path.GetFileName(path)));
            entries.Sort(StringComparer.Ordinal);
            string hashManifest = "{\n  \"schemaVersion\": 1,\n  \"algorithm\": \"SHA-256\",\n  \"inputsSha256\": " + JsonString(fingerprint) + ",\n  \"files\": [\n" + string.Join(",\n", entries) + "\n  ]\n}\n";
            string hashPath = Path.Combine(directory, "hashes.json");
            File.WriteAllText(hashPath, hashManifest, new UTF8Encoding(false));
            Assert.That(File.ReadAllText(hashPath), Is.EqualTo(hashManifest), "hashes.json must be written without transformation.");
        }

        /// <summary>Builds one read-back SHA-256 record for an already-written capture artifact.</summary>
        private static string BuildHashEntry(string directory, string fileName)
        {
            string path = Path.Combine(directory, fileName);
            return "    { \"file\": " + JsonString(fileName) + ", \"sha256\": " + JsonString(Sha256(File.ReadAllBytes(path))) + ", \"validation\": \"written-and-rehashed\" }";
        }
    }
}
