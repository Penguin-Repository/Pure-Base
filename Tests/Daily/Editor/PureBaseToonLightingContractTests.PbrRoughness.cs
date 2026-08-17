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

// Defines direct and reflection GPU contracts for the PBR perceptual-roughness floor.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Tests.Daily
{
    /// <summary>Defines GPU contracts for the PBR and Hybrid perceptual-roughness floor.</summary>
    public sealed partial class PureBaseToonLightingContractTests
    {
        /// <summary>Identifies the public lower bound shared by PBR and Hybrid perceptual roughness.</summary>
        private const float PbrRoughnessFloor = 0.089f;

        /// <summary>Lists the PBR-family products that must share roughness behavior.</summary>
        private static readonly string[] PbrRoughnessShaderNames = { "PureBase/PBR", "PureBase/Hybrid" };

        /// <summary>Defines the opt-in environment variable that enables package-external legacy reference export.</summary>
        private const string VisibilityEvidenceExportEnvironmentVariable = "PUREBASE_ISSUE13_VISIBILITY_EVIDENCE_EXPORT";

        /// <summary>Defines the optional absolute environment variable selecting the evidence root.</summary>
        private const string VisibilityEvidenceRootEnvironmentVariable = "PUREBASE_ISSUE13_VISIBILITY_EVIDENCE_ROOT";

        /// <summary>Defines the minimum nonblack center signal for low-radiance visibility captures.</summary>
        private const float VisibilitySignalMinimum = 0.0001f;

        /// <summary>Requires both direct forward paths to map below-floor metallic roughness to the public floor.</summary>
        [Test]
        public void PbrAndHybridDirectRoughnessFloorIsFiniteEquivalentAndDiscriminating()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                foreach (string shaderName in PbrRoughnessShaderNames)
                {
                    AssertDirectRoughnessCase(capture, shaderName, "ForwardBase", Vector3.back, "normal incidence", true);
                    AssertDirectRoughnessCase(capture, shaderName, "ForwardBase", new Vector3(0.98f, 0.0f, -0.2f), "grazing incidence", false);
                    AssertDirectRoughnessCase(capture, shaderName, "ForwardAdd", Vector3.back, "normal incidence", true);
                    AssertDirectRoughnessCase(capture, shaderName, "ForwardAdd", new Vector3(0.98f, 0.0f, -0.2f), "grazing incidence", false);
                }
            }
        }

        /// <summary>Requires direct-light-free reflection to select the shared floor and a distinct higher roughness response.</summary>
        [Test]
        public void PbrAndHybridReflectionRoughnessFloorIsFiniteEquivalentAndDiscriminating()
        {
            using (var capture = new ToonLightingCaptureScope())
            {
                foreach (string shaderName in PbrRoughnessShaderNames)
                {
                    Color below = capture.RenderPbrRoughnessReflection(shaderName, 0.0f);
                    Color floor = capture.RenderPbrRoughnessReflection(shaderName, PbrRoughnessFloor);
                    Color above = capture.RenderPbrRoughnessReflection(shaderName, 0.25f);
                    AssertPbrRoughnessObservation(below, shaderName + " reflection below floor", true);
                    AssertPbrRoughnessObservation(floor, shaderName + " reflection floor", true);
                    AssertPbrRoughnessObservation(above, shaderName + " reflection above floor", true);
                    AssertColorWithin(floor, below, 0.01f, shaderName + " reflection below-floor equivalence");
                    Assert.That(MaximumPbrRoughnessDifference(floor, above), Is.GreaterThan(0.01f), shaderName + " reflection must distinguish 0.25 roughness.");
                }
            }
        }

        /// <summary>Ensures reflection capture restores caller-owned reflection globals after disposal.</summary>
        [Test]
        public void PbrRoughnessReflectionCaptureRestoresCallerState()
        {
            DefaultReflectionMode mode = RenderSettings.defaultReflectionMode;
            Texture texture = RenderSettings.customReflectionTexture;
            float intensity = RenderSettings.reflectionIntensity;
            using (var capture = new ToonLightingCaptureScope())
                AssertPbrRoughnessObservation(capture.RenderPbrRoughnessReflection("PureBase/PBR", PbrRoughnessFloor), "reflection restoration control", true);
            Assert.That(RenderSettings.defaultReflectionMode, Is.EqualTo(mode));
            Assert.That(RenderSettings.customReflectionTexture, Is.EqualTo(texture));
            Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(intensity));
        }

        /// <summary>Requires representative PBR and Hybrid direct visibility observations to remain finite across both forward passes and degeneracies.</summary>
        [Test]
        public void PbrAndHybridVisibilityReferenceCasesAreFiniteAcrossPassesAnglesMetallicitiesAndDegeneracies()
        {
            var evidence = new List<PbrVisibilityObservation>();
            using (var capture = new ToonLightingCaptureScope())
            {
                foreach (string shaderName in PbrRoughnessShaderNames)
                {
                    foreach (string passName in new[] { "ForwardBase", "ForwardAdd" })
                    {
                        foreach (float metallic in new[] { 0.0f, 1.0f })
                        {
                            foreach (float roughness in new[] { PbrRoughnessFloor, 0.25f, 0.5f, 1.0f })
                            {
                                AddVisibilityObservation(capture, evidence, shaderName, passName, metallic, roughness, Vector3.back, "normal", true);
                                AddVisibilityObservation(capture, evidence, shaderName, passName, metallic, roughness, new Vector3(0.98f, 0.0f, -0.2f), "grazing", false);
                            }
                        }
                    }
                }

                AddDegeneracyObservations(capture, evidence);
            }

            ExportLegacyVisibilityEvidenceIfEnabled(evidence);
        }

        /// <summary>Asserts one selected direct incidence and pass case with an optional nonblack control.</summary>
        private static void AssertDirectRoughnessCase(ToonLightingCaptureScope capture, string shaderName, string passName, Vector3 normal, string incidence, bool requireNonBlack)
        {
            Color below = capture.RenderPbrRoughnessDirect(shaderName, passName, 0.0f, normal);
            Color floor = capture.RenderPbrRoughnessDirect(shaderName, passName, PbrRoughnessFloor, normal);
            Color above = capture.RenderPbrRoughnessDirect(shaderName, passName, 0.25f, normal);
            string label = shaderName + " " + passName + " " + incidence;
            AssertPbrRoughnessObservation(below, label + " below floor", false);
            AssertPbrRoughnessObservation(floor, label + " floor", requireNonBlack);
            AssertPbrRoughnessObservation(above, label + " above floor", requireNonBlack);
            AssertColorWithin(floor, below, 0.01f, label + " below-floor equivalence");
            Assert.That(MaximumPbrRoughnessDifference(floor, above), Is.GreaterThan(0.005f), label + " must distinguish 0.25 roughness.");
        }

        /// <summary>Renders and validates one complete 64x64 representative direct-visibility observation.</summary>
        private static void AddVisibilityObservation(ToonLightingCaptureScope capture, List<PbrVisibilityObservation> evidence, string shaderName, string passName, float metallic, float roughness, Vector3 normal, string incidence, bool requireNonBlack)
        {
            PbrVisibilityObservation observation = capture.RenderPbrVisibilityReference(shaderName, passName, metallic, roughness, normal, incidence);
            AssertPbrVisibilityObservation(observation, requireNonBlack);
            evidence.Add(observation);
        }

        /// <summary>Adds the measured zero-dot and zero-half-vector contracts with a neighboring nonblack control.</summary>
        private static void AddDegeneracyObservations(ToonLightingCaptureScope capture, List<PbrVisibilityObservation> evidence)
        {
            AddVisibilityObservation(capture, evidence, "PureBase/PBR", "ForwardBase", 1.0f, 0.25f, Vector3.back, "normal-control", true);
            PbrVisibilityObservation zeroLight = capture.RenderPbrVisibilityReference("PureBase/PBR", "ForwardBase", 1.0f, 0.25f, Vector3.back, "ndotl-zero", Vector3.right);
            PbrVisibilityObservation zeroView = capture.RenderPbrVisibilityReference("PureBase/PBR", "ForwardBase", 1.0f, 0.25f, Vector3.right, "ndotv-zero", Vector3.right);
            PbrVisibilityObservation bothZero = capture.RenderPbrVisibilityReference("PureBase/PBR", "ForwardBase", 1.0f, 0.25f, Vector3.right, "both-zero", Vector3.forward);
            PbrVisibilityObservation opposite = capture.RenderPbrVisibilityReference("PureBase/PBR", "ForwardBase", 1.0f, 0.25f, Vector3.back, "light-plus-view-zero", Vector3.forward);
            foreach (PbrVisibilityObservation observation in new[] { zeroLight, zeroView, bothZero, opposite })
            {
                AssertPbrVisibilityObservation(observation, false);
                evidence.Add(observation);
            }

            Assert.That(zeroLight.Center.maxColorComponent, Is.EqualTo(0.0f).Within(0.000001f), "Measured NdotL zero must yield zero direct metallic specular.");
            Assert.That(opposite.Center.maxColorComponent, Is.EqualTo(0.0f).Within(0.000001f), "Measured L + V zero control must retain zero direct metallic specular when NdotL is zero.");
        }

        /// <summary>Requires a complete rendered frame to be finite, nonnegative, and optionally nonblack at its center.</summary>
        private static void AssertPbrVisibilityObservation(PbrVisibilityObservation observation, bool requireNonBlack)
        {
            foreach (Color sample in observation.Pixels)
            {
                Assert.That(float.IsFinite(sample.r) && float.IsFinite(sample.g) && float.IsFinite(sample.b), Is.True, observation.Label + " contains a non-finite 64x64 sample.");
                Assert.That(sample.r >= 0.0f && sample.g >= 0.0f && sample.b >= 0.0f, Is.True, observation.Label + " contains a negative 64x64 sample.");
            }

            Assert.That(float.IsFinite(observation.MeasuredNdotL) && float.IsFinite(observation.MeasuredNdotV), Is.True, observation.Label + " has non-finite measured incidence.");
            if (requireNonBlack)
                Assert.That(observation.Center.maxColorComponent, Is.GreaterThan(VisibilitySignalMinimum), observation.Label + " is black or nondiscriminating.");
        }

        /// <summary>Exports manifest-bound legacy frames only when an explicit environment variable enables the operation.</summary>
        private static void ExportLegacyVisibilityEvidenceIfEnabled(List<PbrVisibilityObservation> observations)
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(VisibilityEvidenceExportEnvironmentVariable), "legacy", StringComparison.Ordinal))
                return;
            string root = Environment.GetEnvironmentVariable(VisibilityEvidenceRootEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(root))
                root = Path.Combine(Path.GetTempPath(), "PureBase-Issue13-Visibility");
            else
                Assert.That(Path.IsPathRooted(root), Is.True, "The configured visibility evidence destination must be absolute.");
            root = Path.GetFullPath(root);
            AssertExternalEvidenceRoot(root);
            string inputs = BuildVisibilityInputsManifest(observations);
            string fingerprint = Sha256(Encoding.UTF8.GetBytes(inputs));
            string captureId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture) + "-" + fingerprint.Substring(0, 16);
            string directory = Path.Combine(root, "captures", "legacy-exact-" + captureId);
            Assert.That(Directory.Exists(directory), Is.False, "Each legacy evidence export requires a new capture directory so prior audit artifacts remain intact.");
            Directory.CreateDirectory(directory);
            WriteVisibilityInputs(Path.Combine(directory, "inputs.json"), inputs);
            WriteVisibilityObservations(directory, observations);
            Debug.Log("PureBase Issue13 visibility capture " + captureId + " inputSha256=" + fingerprint);
            WriteUnityLogSnapshot(directory, captureId, fingerprint);
            WriteCaptureManifest(directory, captureId, fingerprint, observations.Count);
            WriteVisibilityHashList(directory, fingerprint);
            WriteNUnitResult(directory, captureId, fingerprint);
            TestContext.Progress.WriteLine("PureBase Issue13 visibility evidence capture=" + captureId + ", inputSha256=" + fingerprint + ", directory=" + directory);
        }

        /// <summary>Rejects evidence locations that could add generated output to the package or Unity Assets tree.</summary>
        private static void AssertExternalEvidenceRoot(string root)
        {
            Assert.That(Path.IsPathRooted(root), Is.True, "Visibility evidence requires an absolute external destination.");
            string package = Path.GetFullPath("Packages/jp.penguin.purebase");
            string assets = Path.GetFullPath(Application.dataPath);
            Assert.That(root.StartsWith(package, StringComparison.OrdinalIgnoreCase), Is.False, "Visibility evidence must not be written inside the package.");
            Assert.That(root.StartsWith(assets, StringComparison.OrdinalIgnoreCase), Is.False, "Visibility evidence must not be written inside Unity Assets.");
        }

        /// <summary>Builds the immutable render-input manifest shared by legacy and future fast observations.</summary>
        private static string BuildVisibilityInputsManifest(List<PbrVisibilityObservation> observations)
        {
            var manifest = new StringBuilder();
            manifest.Append("{\n  \"schemaVersion\": 3,\n  \"environment\": {\n");
            manifest.Append("    \"unityVersion\": ").Append(JsonString(Application.unityVersion)).Append(",\n");
            manifest.Append("    \"colorSpace\": ").Append(JsonString(QualitySettings.activeColorSpace.ToString())).Append(",\n");
            manifest.Append("    \"graphicsDevice\": { \"type\": ").Append(JsonString(SystemInfo.graphicsDeviceType.ToString())).Append(", \"name\": ").Append(JsonString(SystemInfo.graphicsDeviceName)).Append(", \"vendor\": ").Append(JsonString(SystemInfo.graphicsDeviceVendor)).Append(", \"deviceId\": ").Append(SystemInfo.graphicsDeviceID.ToString(CultureInfo.InvariantCulture)).Append(", \"api\": ").Append(JsonString(SystemInfo.graphicsDeviceVersion)).Append(" },\n");
            manifest.Append("    \"lighting\": { \"fog\": false, \"sphericalHarmonics\": \"zero\", \"reflection\": \"off\", \"lightProbes\": \"off\", \"reflectionProbes\": \"off\" },\n");
            manifest.Append("    \"diagnosticPng\": { \"mapping\": \"linear-to-sRGB\", \"exposure\": 1, \"clamp\": [0, 1] }\n  },\n");
            manifest.Append("  \"renderTarget\": { \"width\": 64, \"height\": 64, \"format\": \"ARGBFloat\", \"readbackFormat\": \"RGBAFloat\", \"readWrite\": \"Linear\" },\n  \"cases\": [\n");
            for (int index = 0; index < observations.Count; index++)
            {
                if (index > 0)
                    manifest.Append(",\n");
                AppendVisibilityInputCase(manifest, observations[index], index);
            }

            manifest.Append("\n  ]\n}\n");
            return manifest.ToString();
        }

        /// <summary>Writes one capture-owned immutable render-input identity.</summary>
        private static void WriteVisibilityInputs(string inputPath, string inputs)
        {
            File.WriteAllText(inputPath, inputs, new UTF8Encoding(false));
            Assert.That(File.ReadAllText(inputPath), Is.EqualTo(inputs), "Capture-owned inputs.json must be written without transformation.");
        }

        /// <summary>Appends one complete render-used input case to the immutable manifest.</summary>
        private static void AppendVisibilityInputCase(StringBuilder manifest, PbrVisibilityObservation observation, int index)
        {
            bool isPointLight = observation.LightType == LightType.Point;
            manifest.Append("    { \"id\": ").Append(index.ToString(CultureInfo.InvariantCulture)).Append(", \"name\": ").Append(JsonString(observation.FileName)).Append(", \"shader\": ").Append(JsonString(observation.ShaderName)).Append(", \"pass\": ").Append(JsonString(observation.PassName)).Append(",\n");
            manifest.Append("      \"material\": { \"baseTexture\": \"white\", \"baseColor\": [1, 1, 1, 1], \"metallic\": ").Append(JsonFloat(observation.Metallic)).Append(", \"roughness\": ").Append(JsonFloat(observation.Roughness)).Append(", \"useUnityStandardDiffuseBrightness\": 0 },\n");
            manifest.Append("      \"camera\": { \"transform\": { \"position\": ").Append(JsonVector3(observation.CameraPosition)).Append(", \"rotation\": ").Append(JsonQuaternion(observation.CameraRotation)).Append(" }, \"orthographic\": true, \"orthographicSize\": ").Append(JsonFloat(observation.CameraOrthographicSize)).Append(", \"near\": ").Append(JsonFloat(observation.CameraNearClipPlane)).Append(", \"far\": ").Append(JsonFloat(observation.CameraFarClipPlane)).Append(", \"clearFlags\": \"SolidColor\", \"background\": [0, 0, 0, 0.37], \"renderingPath\": ").Append(JsonString(observation.CameraRenderingPath)).Append(", \"culling\": \"capture-scene\" },\n");
            manifest.Append("      \"mesh\": { \"primitive\": \"full-frame-quad\", \"transform\": { \"position\": ").Append(JsonVector3(observation.MeshPosition)).Append(", \"rotation\": ").Append(JsonQuaternion(observation.MeshRotation)).Append(", \"scale\": ").Append(JsonVector3(observation.MeshScale)).Append(" }, \"uniformNormal\": ").Append(JsonVector3(observation.Normal)).Append(" },\n");
            manifest.Append("      \"light\": { \"type\": ").Append(JsonString(observation.LightType.ToString())).Append(", \"color\": ").Append(JsonVector4(observation.LightColor)).Append(", \"intensity\": 1, \"transform\": { \"position\": ").Append(JsonVector3(observation.LightTransformPosition)).Append(", \"rotation\": ").Append(JsonQuaternion(observation.LightTransformRotation)).Append(" }, \"range\": ").Append(isPointLight ? JsonFloat(observation.LightRange) : "null").Append(", \"renderLightCounts\": ").Append(isPointLight ? "[1, 2]" : "[1]").Append(" },\n");
            manifest.Append("      \"measuredGeometry\": { \"normal\": ").Append(JsonVector3(observation.Normal)).Append(", \"light\": ").Append(JsonVector3(observation.LightDirection)).Append(", \"view\": ").Append(JsonVector3(observation.ViewDirection)).Append(", \"ndotL\": ").Append(JsonFloat(observation.MeasuredNdotL)).Append(", \"ndotV\": ").Append(JsonFloat(observation.MeasuredNdotV)).Append(", \"lightPlusView\": ").Append(JsonVector3(observation.LightPlusView)).Append(", \"halfVector\": ").Append(JsonVector3(observation.HalfVector)).Append(", \"halfVectorDefined\": ").Append(observation.LightPlusView.sqrMagnitude > 0.0f ? "true" : "false").Append(" }\n    }");
        }

        /// <summary>Writes complete diagnostic PNGs, frame statistics, and formula characterization records.</summary>
        private static void WriteVisibilityObservations(string directory, List<PbrVisibilityObservation> observations)
        {
            Directory.CreateDirectory(directory);
            var entries = new List<string>();
            var hashEntries = new List<string>();
            foreach (PbrVisibilityObservation observation in observations)
            {
                string fileName = observation.FileName;
                string path = Path.Combine(directory, fileName);
                byte[] png = EncodeDiagnosticPng(observation.Pixels);
                File.WriteAllBytes(path, png);
                string pngHash = Sha256(png);
                Assert.That(Sha256(File.ReadAllBytes(path)), Is.EqualTo(pngHash), "Each diagnostic PNG must be written and rehashed before export completes.");
                entries.Add("    { \"name\": " + JsonString(fileName) + ", \"center\": " + JsonColor(observation.Center) + ", \"centerFinite\": " + (observation.CenterFinite ? "true" : "false") + ", \"frame\": " + BuildFrameSummary(observation.Pixels) + ", \"frameFinite\": " + (observation.FrameFinite ? "true" : "false") + ", \"ndotL\": " + JsonFloat(observation.MeasuredNdotL) + ", \"ndotV\": " + JsonFloat(observation.MeasuredNdotV) + ", \"lightPlusView\": " + JsonVector3(observation.LightPlusView) + ", \"halfVectorDefined\": " + (observation.LightPlusView.sqrMagnitude > 0.0f ? "true" : "false") + ", \"sha256\": " + JsonString(pngHash) + " }");
                hashEntries.Add("    { \"file\": " + JsonString(fileName) + ", \"sha256\": " + JsonString(pngHash) + ", \"validation\": \"written-and-rehashed\" }");
            }

            string observationsManifest = "{\n  \"formula\": \"legacy-exact\",\n  \"inputsSha256\": " + JsonString(Sha256(File.ReadAllBytes(Path.Combine(directory, "inputs.json")))) + ",\n  \"observations\": [\n" + string.Join(",\n", entries) + "\n  ]\n}\n";
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
        private static void WriteCaptureManifest(string directory, string captureId, string fingerprint, int observationCount)
        {
            string manifest = "{\n  \"schemaVersion\": 1,\n  \"captureId\": " + JsonString(captureId) + ",\n  \"formula\": \"legacy-exact\",\n  \"inputsSha256\": " + JsonString(fingerprint) + ",\n  \"observationCount\": " + observationCount.ToString(CultureInfo.InvariantCulture) + ",\n  \"nunitResult\": \"nunit-result.json\",\n  \"unityLog\": \"unity-editor.log\"\n}\n";
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

        /// <summary>Formats a JSON string without locale or control-character ambiguity.</summary>
        private static string JsonString(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        }

        /// <summary>Formats a finite floating-point value using invariant round-trip representation.</summary>
        private static string JsonFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Formats one vector for structured evidence output.</summary>
        private static string JsonVector3(Vector3 value)
        {
            return "[" + JsonFloat(value.x) + ", " + JsonFloat(value.y) + ", " + JsonFloat(value.z) + "]";
        }

        /// <summary>Formats one four-component vector for structured evidence output.</summary>
        private static string JsonVector4(Vector4 value)
        {
            return "[" + JsonFloat(value.x) + ", " + JsonFloat(value.y) + ", " + JsonFloat(value.z) + ", " + JsonFloat(value.w) + "]";
        }

        /// <summary>Formats one quaternion for structured evidence output.</summary>
        private static string JsonQuaternion(Quaternion value)
        {
            return "[" + JsonFloat(value.x) + ", " + JsonFloat(value.y) + ", " + JsonFloat(value.z) + ", " + JsonFloat(value.w) + "]";
        }

        /// <summary>Formats one HDR color for structured evidence output.</summary>
        private static string JsonColor(Color value)
        {
            return "[" + JsonFloat(value.r) + ", " + JsonFloat(value.g) + ", " + JsonFloat(value.b) + ", " + JsonFloat(value.a) + "]";
        }

        /// <summary>Encodes a full 64x64 linear frame with the documented fixed linear-to-sRGB exposure mapping.</summary>
        private static byte[] EncodeDiagnosticPng(Color[] linearPixels)
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var mapped = new Color[linearPixels.Length];
                for (int index = 0; index < linearPixels.Length; index++)
                    mapped[index] = new Color(Mathf.LinearToGammaSpace(Mathf.Clamp01(linearPixels[index].r)), Mathf.LinearToGammaSpace(Mathf.Clamp01(linearPixels[index].g)), Mathf.LinearToGammaSpace(Mathf.Clamp01(linearPixels[index].b)), 1.0f);
                texture.SetPixels(mapped);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>Returns the lower-case SHA-256 hash for one immutable diagnostic PNG.</summary>
        private static string Sha256(byte[] content)
        {
            using (SHA256 algorithm = SHA256.Create())
            {
                var builder = new StringBuilder();
                foreach (byte value in algorithm.ComputeHash(content))
                    builder.Append(value.ToString("x2"));
                return builder.ToString();
            }
        }

        /// <summary>Requires a finite, nonnegative HDR observation and an optional nonblack control.</summary>
        private static void AssertPbrRoughnessObservation(Color color, string label, bool requireNonBlack)
        {
            Assert.That(float.IsFinite(color.r) && float.IsFinite(color.g) && float.IsFinite(color.b), Is.True, label + " is non-finite.");
            Assert.That(color.r >= 0.0f && color.g >= 0.0f && color.b >= 0.0f, Is.True, label + " is negative.");
            if (requireNonBlack)
                Assert.That(color.maxColorComponent, Is.GreaterThan(0.001f), label + " is black or nondiscriminating.");
        }

        /// <summary>Calculates the largest absolute RGB difference between two roughness observations.</summary>
        private static float MaximumPbrRoughnessDifference(Color first, Color second)
        {
            return Mathf.Max(Mathf.Abs(first.r - second.r), Mathf.Abs(first.g - second.g), Mathf.Abs(first.b - second.b));
        }
    }
}
