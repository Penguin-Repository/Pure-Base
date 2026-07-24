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

// Provides contract loading, generated-source access, and artifact-path containment for consumer tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PureBase.Release.Consumer.Tests
{
    /// <summary>Describes one complete, runner-provided consumer validation invocation.</summary>
    [Serializable]
    public sealed class ConsumerValidationContract
    {
        /// <summary>Stores the stable label for the current consumer invocation.</summary>
        public string runLabel;

        /// <summary>Stores the validation lane selected by the runner.</summary>
        public string runKind;

        /// <summary>Stores the package products to import and inspect.</summary>
        public ConsumerProductContract[] products;

        /// <summary>Stores whether an external module is selected for this invocation.</summary>
        public bool hasSelectedModule;

        /// <summary>Stores the externally selected module payload when a module is selected.</summary>
        public ConsumerModuleContract selectedModule;

        /// <summary>Stores the same-phase module order contract when two external modules are selected.</summary>
        public ConsumerModuleOrderContract moduleOrder;

        /// <summary>Stores every unselected module sentinel that must be absent from generated source.</summary>
        public string[] inactiveSentinels;

        /// <summary>Stores actual BIRP runtime samples required by the current invocation.</summary>
        public ConsumerRuntimeSampleContract[] runtimeSamples;

        /// <summary>Stores the module-free comparison required for a selected runtime sample.</summary>
        public ConsumerRuntimeDeltaContract runtimeDelta;

        /// <summary>Stores the validation-scene bake contract when the invocation must bake.</summary>
        public ConsumerBakeContract bake;

        /// <summary>Stores the selected Unlit ForwardAdd fog runtime contract when the invocation must render it.</summary>
        public ConsumerUnlitForwardAddFogContract unlitForwardAddFog;
    }

    /// <summary>Describes one imported public product shader and its expected generated shape.</summary>
    [Serializable]
    public sealed class ConsumerProductContract
    {
        /// <summary>Stores the public shader name resolved through <see cref="Shader.Find(string)"/>.</summary>
        public string shaderName;

        /// <summary>Stores the AssetDatabase-relative Shader-Core asset path.</summary>
        public string shaderAssetPath;

        /// <summary>Stores the exact public pass names in declaration order.</summary>
        public string[] expectedPassNames;

        /// <summary>Stores the exact visible generated property names.</summary>
        public string[] expectedVisiblePropertyNames;

        /// <summary>Stores required generated-source fragments that are not pass-specific.</summary>
        public string[] requiredSourceFragments;

        /// <summary>Stores forbidden generated-source fragments that are not pass-specific.</summary>
        public string[] forbiddenSourceFragments;

        /// <summary>Stores complete ordered pass-specific generated-source expectations.</summary>
        public ConsumerPassContract[] passContracts;
    }

    /// <summary>Describes source requirements within one ordered ShaderLab pass.</summary>
    [Serializable]
    public sealed class ConsumerPassContract
    {
        /// <summary>Stores the expected pass name at this contract position.</summary>
        public string passName;

        /// <summary>Stores the following pass name, or an empty string for the final pass.</summary>
        public string nextPassName;

        /// <summary>Stores fragments that must appear in this pass.</summary>
        public string[] requiredFragments;

        /// <summary>Stores fragments that must be absent from this pass.</summary>
        public string[] forbiddenFragments;

        /// <summary>Stores the exact selected-module sentinel count expected in this pass.</summary>
        public int selectedSentinelCount;
    }

    /// <summary>Describes the one external Shader-Core module selected for an invocation.</summary>
    [Serializable]
    public sealed class ConsumerModuleContract
    {
        /// <summary>Stores the selected Shader-Core phase identifier.</summary>
        public string phase;

        /// <summary>Stores the module identity emitted into generated source.</summary>
        public string moduleUniqueId;

        /// <summary>Stores the module-owned generated property name.</summary>
        public string propertyName;

        /// <summary>Stores the non-comment source marker emitted by the module.</summary>
        public string sentinel;
    }

    /// <summary>Describes the two external modules whose generated-source order must be preserved.</summary>
    [Serializable]
    public sealed class ConsumerModuleOrderContract
    {
        /// <summary>Stores the first module's display name for diagnostics.</summary>
        public string firstModuleName;

        /// <summary>Stores the second module's display name for diagnostics.</summary>
        public string secondModuleName;

        /// <summary>Stores the sentinel that must appear first.</summary>
        public string firstSentinel;

        /// <summary>Stores the sentinel that must appear after the first sentinel.</summary>
        public string secondSentinel;

        /// <summary>Stores pass names that must contain the ordered sentinel pair.</summary>
        public string[] presentPassNames;

        /// <summary>Stores pass names that must contain neither sentinel.</summary>
        public string[] absentPassNames;
    }

    /// <summary>Describes one float material assignment applied before a runtime readback.</summary>
    [Serializable]
    public sealed class ConsumerFloatAssignment
    {
        /// <summary>Stores the material property name.</summary>
        public string propertyName;

        /// <summary>Stores the float value applied to the property.</summary>
        public float value;
    }

    /// <summary>Describes one inclusive floating-point range.</summary>
    [Serializable]
    public sealed class ConsumerFloatRange
    {
        /// <summary>Stores the inclusive minimum.</summary>
        public float minimum;

        /// <summary>Stores the inclusive maximum.</summary>
        public float maximum;
    }

    /// <summary>Describes a deterministic product render and its expected center-pixel output.</summary>
    [Serializable]
    public sealed class ConsumerRuntimeSampleContract
    {
        /// <summary>Stores the human-readable sample label.</summary>
        public string label;

        /// <summary>Stores the public shader name.</summary>
        public string shaderName;

        /// <summary>Stores the Shader-Core asset path to import before rendering.</summary>
        public string shaderAssetPath;

        /// <summary>Stores material float assignments required before the sample is rendered.</summary>
        public ConsumerFloatAssignment[] floatAssignments;

        /// <summary>Stores whether a point light is included to exercise ForwardAdd.</summary>
        public bool includePointLight;

        /// <summary>Stores the expected red channel range.</summary>
        public ConsumerFloatRange red;

        /// <summary>Stores the expected green channel range.</summary>
        public ConsumerFloatRange green;

        /// <summary>Stores the expected blue channel range.</summary>
        public ConsumerFloatRange blue;

        /// <summary>Stores the expected alpha channel range.</summary>
        public ConsumerFloatRange alpha;
    }

    /// <summary>Describes the required selected-module runtime effect relative to a module-free reference.</summary>
    [Serializable]
    public sealed class ConsumerRuntimeDeltaContract
    {
        /// <summary>Stores the runtime sample label covered by this comparison.</summary>
        public string sampleLabel;

        /// <summary>Stores the expected module-free center-pixel reference.</summary>
        public ConsumerColorContract moduleFreeReference;

        /// <summary>Stores the inclusive selected-minus-module-free channel ranges.</summary>
        public ConsumerColorRangeContract selectedMinusModuleFree;
    }

    /// <summary>Describes inclusive channel ranges for one RGBA color observation.</summary>
    [Serializable]
    public sealed class ConsumerColorRangeContract
    {
        /// <summary>Stores the inclusive red-channel range.</summary>
        public ConsumerFloatRange red;

        /// <summary>Stores the inclusive green-channel range.</summary>
        public ConsumerFloatRange green;

        /// <summary>Stores the inclusive blue-channel range.</summary>
        public ConsumerFloatRange blue;

        /// <summary>Stores the inclusive alpha-channel range.</summary>
        public ConsumerFloatRange alpha;
    }

    /// <summary>Describes the runner-staged validation scene that must be synchronously baked.</summary>
    [Serializable]
    public sealed class ConsumerBakeContract
    {
        /// <summary>Stores the AssetDatabase-relative validation scene path.</summary>
        public string scenePath;

        /// <summary>Stores the camera name used for bake evidence readback.</summary>
        public string cameraName;

        /// <summary>Stores static renderer names that must receive lightmap assignments.</summary>
        public string[] requiredStaticRendererNames;

        /// <summary>Stores the minimum number of resulting lightmaps.</summary>
        public int minimumLightmapCount;

        /// <summary>Stores the minimum visible pixel count in the evidence image.</summary>
        public int minimumVisiblePixelCount;

        /// <summary>Stores the AssetDatabase-relative Lighting Settings asset expected by the baked scene.</summary>
        public string lightingSettingsPath;

        /// <summary>Stores the stable GUID expected for the Lighting Settings asset.</summary>
        public string lightingSettingsGuid;

        /// <summary>Stores the required Lighting Settings lightmapper enum name.</summary>
        public string lightmapper;

        /// <summary>Stores whether baked global illumination must be enabled.</summary>
        public bool bakedGi;

        /// <summary>Stores whether realtime global illumination must be enabled.</summary>
        public bool realtimeGi;

        /// <summary>Stores whether Unity automatic lightmap generation must be enabled.</summary>
        public bool autoGenerate;

        /// <summary>Stores the four actual product Meta readbacks required after the bake.</summary>
        public ConsumerMetaReadbackContract[] metaReadbacks;

        /// <summary>Stores the actual ShadowCaster silhouette evidence requirements.</summary>
        public ConsumerShadowEvidenceContract shadowEvidence;

        /// <summary>Stores every explicit BIRP shader/pass/keyword warmup request.</summary>
        public ConsumerBirpVariantWarmupContract[] variantWarmups;

        /// <summary>Stores the exact expected number of explicit BIRP warmup requests.</summary>
        public int expectedVariantWarmupCount;
    }

    /// <summary>Describes one actual product Meta-pass luminance readback required after a bake.</summary>
    [Serializable]
    public sealed class ConsumerMetaReadbackContract
    {
        /// <summary>Stores the scene material name that must be drawn through its Meta pass.</summary>
        public string materialName;

        /// <summary>Stores the public product shader name expected on the material.</summary>
        public string shaderName;

        /// <summary>Stores the inclusive expected range for the observed mean Meta albedo luminance.</summary>
        public ConsumerFloatRange meanLuminance;
    }

    /// <summary>Describes the actual ShadowCaster silhouette evidence required after a bake.</summary>
    [Serializable]
    public sealed class ConsumerShadowEvidenceContract
    {
        /// <summary>Stores the scene material name cloned for the shadow caster.</summary>
        public string materialName;

        /// <summary>Stores the public product shader name expected on the shadow caster material.</summary>
        public string shaderName;

        /// <summary>Stores the minimum changed-pixel count between no-shadow and shadow renders.</summary>
        public int minimumChangedPixelCount;

        /// <summary>Stores the PNG filename used for the shadow evidence artifact.</summary>
        public string screenshotFileName;
    }

    /// <summary>Describes one explicit BIRP shader/pass/keyword warmup request.</summary>
    [Serializable]
    public sealed class ConsumerBirpVariantWarmupContract
    {
        /// <summary>Stores the stable diagnostic label for the request.</summary>
        public string label;

        /// <summary>Stores the public shader name to warm.</summary>
        public string shaderName;

        /// <summary>Stores the Shader-Core asset path imported before warmup.</summary>
        public string shaderAssetPath;

        /// <summary>Stores the <see cref="UnityEngine.Rendering.PassType"/> enum name for the requested BIRP pass.</summary>
        public string passType;

        /// <summary>Stores the exact shader keywords requested for this pass.</summary>
        public string[] keywords;
    }

    /// <summary>Describes a controlled BIRP fog state.</summary>
    [Serializable]
    public sealed class ConsumerFogStateContract
    {
        /// <summary>Stores the required <see cref="FogMode"/> enum name.</summary>
        public string mode;

        /// <summary>Stores the fog color applied through render settings and the BIRP global binding.</summary>
        public ConsumerColorContract color;

        /// <summary>Stores the BIRP fog density applied while fog is enabled.</summary>
        public float density;
    }

    /// <summary>Describes an RGBA color supplied by the runner.</summary>
    [Serializable]
    public sealed class ConsumerColorContract
    {
        /// <summary>Stores the red component.</summary>
        public float red;

        /// <summary>Stores the green component.</summary>
        public float green;

        /// <summary>Stores the blue component.</summary>
        public float blue;

        /// <summary>Stores the alpha component.</summary>
        public float alpha;
    }

    /// <summary>Describes the selected Unlit ForwardAdd fog runtime oracle.</summary>
    [Serializable]
    public sealed class ConsumerUnlitForwardAddFogContract
    {
        /// <summary>Stores the selected product imported and rendered for the oracle.</summary>
        public ConsumerProductContract product;

        /// <summary>Stores the selected module identity that emits the ForwardAdd signal.</summary>
        public string moduleUniqueId;

        /// <summary>Stores the generated source sentinel required from the selected module.</summary>
        public string sentinel;

        /// <summary>Stores material assignments applied before the direct ForwardAdd draws.</summary>
        public ConsumerFloatAssignment[] floatAssignments;

        /// <summary>Stores the controlled fog state used for the fog-enabled draw.</summary>
        public ConsumerFogStateContract fog;

        /// <summary>Stores the perspective camera field of view used to produce fog distance.</summary>
        public float cameraFieldOfView;

        /// <summary>Stores the inclusive expected range for the fog-disabled RGB magnitude.</summary>
        public ConsumerFloatRange fogDisabledSignalMagnitude;

        /// <summary>Stores the inclusive expected range for the fog-enabled to fog-disabled magnitude ratio.</summary>
        public ConsumerFloatRange retainedSignalFraction;

        /// <summary>Stores the fog-enabled red-channel black range.</summary>
        public ConsumerFloatRange blackFogRed;

        /// <summary>Stores the fog-enabled green-channel black range.</summary>
        public ConsumerFloatRange blackFogGreen;

        /// <summary>Stores the fog-enabled blue-channel black range.</summary>
        public ConsumerFloatRange blackFogBlue;

        /// <summary>Stores the fog-enabled alpha-channel expected range.</summary>
        public ConsumerFloatRange blackFogAlpha;
    }

    /// <summary>Loads runner-provided configuration and shared consumer validation helpers.</summary>
    public static class ConsumerValidationSupport
    {
        /// <summary>Identifies the contract asset that the runner stages for each Unity process.</summary>
        public const string ContractAssetPath = "Assets/ReleaseConsumer/PureBaseConsumerValidationContract.json";

        /// <summary>Identifies the runner-provided project-relative artifact directory variable.</summary>
        public const string ArtifactDirectoryEnvironmentVariable = "PUREBASE_CONSUMER_ARTIFACTS_DIRECTORY";

        /// <summary>Identifies the only project-relative root that may contain generated evidence.</summary>
        private const string ArtifactRootDirectory = "Artifacts";

        /// <summary>Identifies the generated Shader-Core source subasset.</summary>
        private const string GeneratedSourceName = "Shader Source";

        /// <summary>Loads and validates the current process's runner-provided contract without modifying it.</summary>
        /// <returns>The deserialized consumer validation contract.</returns>
        public static ConsumerValidationContract LoadContract()
        {
            AssetDatabase.ImportAsset(ContractAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            TextAsset contractAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(ContractAssetPath);
            Assert.That(contractAsset, Is.Not.Null, $"The runner must stage a consumer validation contract at '{ContractAssetPath}' before tests run.");
            Assert.That(contractAsset.text, Is.Not.Empty, $"The runner-staged consumer validation contract at '{ContractAssetPath}' is empty.");

            ConsumerValidationContract contract;
            try
            {
                contract = JsonUtility.FromJson<ConsumerValidationContract>(contractAsset.text);
            }
            catch (Exception exception)
            {
                Assert.Fail($"The runner-staged consumer validation contract at '{ContractAssetPath}' is invalid JSON. Original exception: {exception}");
                return null;
            }

            Assert.That(contract, Is.Not.Null, $"The runner-staged consumer validation contract at '{ContractAssetPath}' could not be deserialized.");
            Assert.That(contract.runLabel, Is.Not.Empty, "The runner-staged consumer validation contract must provide runLabel.");
            Assert.That(contract.runKind, Is.Not.Empty, $"Consumer run '{contract.runLabel}' must provide runKind.");
            Assert.That(contract.products, Is.Not.Null.And.Not.Empty, $"Consumer run '{contract.runLabel}' must provide product contracts.");
            return contract;
        }

        /// <summary>Returns the contained artifact directory selected by the runner for the current process.</summary>
        /// <returns>An absolute artifact directory that remains beneath the consumer project's Artifacts root.</returns>
        public static string GetArtifactDirectory()
        {
            string configuredDirectory = Environment.GetEnvironmentVariable(ArtifactDirectoryEnvironmentVariable);
            Assert.That(configuredDirectory, Is.Not.Null.And.Not.Empty, $"The runner must set {ArtifactDirectoryEnvironmentVariable} to a project-relative directory under '{ArtifactRootDirectory}'.");
            Assert.That(Path.IsPathRooted(configuredDirectory), Is.False, $"{ArtifactDirectoryEnvironmentVariable} must be project-relative under '{ArtifactRootDirectory}'.");

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string artifactRoot = Path.GetFullPath(Path.Combine(projectRoot, ArtifactRootDirectory));
            string artifactDirectory = Path.GetFullPath(Path.Combine(projectRoot, configuredDirectory));
            string artifactRootWithSeparator = artifactRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? artifactRoot
                : artifactRoot + Path.DirectorySeparatorChar;
            Assert.That(
                string.Equals(artifactDirectory, artifactRoot, StringComparison.OrdinalIgnoreCase)
                    || artifactDirectory.StartsWith(artifactRootWithSeparator, StringComparison.OrdinalIgnoreCase),
                Is.True,
                $"{ArtifactDirectoryEnvironmentVariable} must resolve under '{ArtifactRootDirectory}'.");

            Directory.CreateDirectory(artifactDirectory);
            return artifactDirectory;
        }

        /// <summary>Imports one product source asset and returns the public shader it registers.</summary>
        /// <param name="product">The runner-provided product contract.</param>
        /// <param name="runLabel">The current consumer run label.</param>
        /// <returns>The imported usable public shader.</returns>
        public static Shader ImportProductShader(ConsumerProductContract product, string runLabel)
        {
            Assert.That(product, Is.Not.Null, $"Consumer run '{runLabel}' has a null product contract.");
            Assert.That(product.shaderName, Is.Not.Empty, $"Consumer run '{runLabel}' has a product without shaderName.");
            Assert.That(product.shaderAssetPath, Is.Not.Empty, $"Consumer run '{runLabel}' product '{product.shaderName}' has no shaderAssetPath.");
            AssetDatabase.ImportAsset(product.shaderAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Shader shader = Shader.Find(product.shaderName);
            Assert.That(shader, Is.Not.Null, $"Consumer run '{runLabel}' did not register Shader.Find(\"{product.shaderName}\") after importing '{product.shaderAssetPath}'.");
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False, $"Consumer run '{runLabel}' imported '{product.shaderName}' with compiler errors.");
            Assert.That(shader.isSupported, Is.True, $"Consumer run '{runLabel}' imported unsupported shader '{product.shaderName}'.");
            return shader;
        }

        /// <summary>Loads the unique generated source subasset for one imported product source asset.</summary>
        /// <param name="product">The product whose generated source is required.</param>
        /// <param name="runLabel">The current consumer run label.</param>
        /// <returns>The non-empty generated ShaderLab source.</returns>
        public static string LoadGeneratedSource(ConsumerProductContract product, string runLabel)
        {
            TextAsset generatedSource = null;
            int generatedSourceCount = 0;
            foreach (UnityEngine.Object importedAsset in AssetDatabase.LoadAllAssetsAtPath(product.shaderAssetPath))
            {
                TextAsset candidate = importedAsset as TextAsset;
                if (candidate != null && string.Equals(candidate.name, GeneratedSourceName, StringComparison.Ordinal))
                {
                    generatedSource = candidate;
                    generatedSourceCount++;
                }
            }

            Assert.That(generatedSourceCount, Is.EqualTo(1), $"Consumer run '{runLabel}' product '{product.shaderName}' expected exactly one '{GeneratedSourceName}' subasset, but found {generatedSourceCount}.");
            Assert.That(generatedSource, Is.Not.Null.And.Property("text").Not.Empty, $"Consumer run '{runLabel}' product '{product.shaderName}' emitted no usable '{GeneratedSourceName}' subasset.");
            return generatedSource.text;
        }

        /// <summary>Writes one generated source artifact beneath the runner-provided artifact directory.</summary>
        /// <param name="runLabel">The current consumer run label.</param>
        /// <param name="shaderName">The product shader name.</param>
        /// <param name="source">The exact generated source text.</param>
        public static void ExportGeneratedSource(string runLabel, string shaderName, string source)
        {
            string fileName = GetGeneratedSourceArtifactFileName(runLabel, shaderName);
            File.WriteAllText(Path.Combine(GetArtifactDirectory(), fileName), source, new UTF8Encoding(false));
        }

        /// <summary>Returns the deterministic generated-source artifact filename for one consumer product.</summary>
        /// <param name="runLabel">The current consumer run label.</param>
        /// <param name="shaderName">The product shader name.</param>
        /// <returns>The filename written beneath the runner-provided artifact directory.</returns>
        public static string GetGeneratedSourceArtifactFileName(string runLabel, string shaderName)
        {
            return SanitizeFileName(runLabel) + "-" + SanitizeFileName(shaderName) + "-generated-source.txt";
        }

        /// <summary>Returns declared pass names in their generated order.</summary>
        /// <param name="shader">The imported product shader.</param>
        /// <returns>The pass names from the first generated subshader.</returns>
        public static string[] GetPassNames(Shader shader)
        {
            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            string[] passNames = new string[subshader.PassCount];
            for (int index = 0; index < passNames.Length; index++)
            {
                passNames[index] = subshader.GetPass(index).Name;
            }

            return passNames;
        }

        /// <summary>Returns generated public property names in their declared order.</summary>
        /// <param name="shader">The imported product shader.</param>
        /// <returns>Every public property name.</returns>
        public static string[] GetVisiblePropertyNames(Shader shader)
        {
            List<string> propertyNames = new List<string>();
            for (int index = 0; index < shader.GetPropertyCount(); index++)
            {
                if (!ShaderUtil.IsShaderPropertyHidden(shader, index))
                {
                    propertyNames.Add(shader.GetPropertyName(index));
                }
            }

            return propertyNames.ToArray();
        }

        /// <summary>Returns one bounded generated pass source section.</summary>
        /// <param name="source">The complete generated ShaderLab source.</param>
        /// <param name="passName">The requested pass name.</param>
        /// <param name="nextPassName">The following pass name, or an empty string for the final pass.</param>
        /// <param name="runLabel">The current consumer run label.</param>
        /// <param name="shaderName">The product shader name used in diagnostics.</param>
        /// <returns>The requested pass section.</returns>
        public static string GetPassSource(string source, string passName, string nextPassName, string runLabel, string shaderName)
        {
            string startMarker = "Name \"" + passName + "\"";
            int start = source.IndexOf(startMarker, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), $"Consumer run '{runLabel}' product '{shaderName}' generated source did not contain pass marker '{startMarker}'.");
            int end = source.Length;
            if (!string.IsNullOrEmpty(nextPassName))
            {
                string endMarker = "Name \"" + nextPassName + "\"";
                end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
                Assert.That(end, Is.GreaterThan(start), $"Consumer run '{runLabel}' product '{shaderName}' generated source did not contain following pass marker '{endMarker}'.");
            }

            return source.Substring(start, end - start);
        }

        /// <summary>Counts non-overlapping ordinal occurrences of a source fragment.</summary>
        /// <param name="source">The source to inspect.</param>
        /// <param name="fragment">The required fragment.</param>
        /// <returns>The number of occurrences.</returns>
        public static int CountOccurrences(string source, string fragment)
        {
            int count = 0;
            int start = 0;
            while ((start = source.IndexOf(fragment, start, StringComparison.Ordinal)) >= 0)
            {
                count++;
                start += fragment.Length;
            }

            return count;
        }

        /// <summary>Asserts one inclusive range is valid.</summary>
        /// <param name="range">The range to validate.</param>
        /// <param name="description">The field description used in diagnostics.</param>
        public static void ValidateRange(ConsumerFloatRange range, string description)
        {
            Assert.That(range, Is.Not.Null, $"Consumer runtime contract must provide {description}.");
            Assert.That(range.minimum, Is.LessThanOrEqualTo(range.maximum), $"Consumer runtime contract {description} has minimum {range.minimum} greater than maximum {range.maximum}.");
        }

        /// <summary>Converts an arbitrary label into a portable artifact filename segment.</summary>
        /// <param name="value">The label to sanitize.</param>
        /// <returns>A non-empty filename-safe segment.</returns>
        private static string SanitizeFileName(string value)
        {
            StringBuilder result = new StringBuilder();
            foreach (char character in value)
            {
                result.Append(char.IsLetterOrDigit(character) ? character : '-');
            }

            return result.Length == 0 ? "consumer" : result.ToString();
        }
    }
}