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

// Converges the fixed Shader-Core test host selections through the supported serialized state shape.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Initializes only the fixed Shader-Core test-host module selections.</summary>
    public static class ShaderCoreTestStateInitializer
    {
        private const string ShaderCoreAssemblyName = "jp.lilxyzw.shadercore";
        private const string ProjectSettingsTypeName = "jp.lilxyzw.shadercore.ProjectSettings";
        private const string ShaderSettingsFieldName = "shaderSettings";
        private const string ShaderNameFieldName = "shadername";
        private const string ModulesFieldName = "modules";

        private static readonly string[] ProductShaderNames =
        {
            "PureBase/Unlit",
            "PureBase/Toon",
            "PureBase/Hybrid",
            "PureBase/PBR"
        };

        /// <summary>Provides a public no-argument entry point for Unity batch-mode <c>-executeMethod</c> invocation.</summary>
        public static void InitializeForBatchMode()
        {
            Initialize();
        }

        /// <summary>Converges the configured test-host and product module-selection rows.</summary>
        /// <returns>The state mutation result.</returns>
        public static StateInitializationResult Initialize()
        {
            return Initialize(
                LoadExpectedRows(),
                new ShaderCoreReflectionContractResolver(),
                new SerializedStateApplicationFactory());
        }

        /// <summary>Converges expected rows after resolving every required reflection contract before state application.</summary>
        /// <param name="expectedRows">The required test-host and product rows.</param>
        /// <param name="reflectionContractResolver">Resolves the validated Shader-Core reflection contract.</param>
        /// <param name="stateApplicationFactory">Creates the serialized state application only after contract resolution.</param>
        /// <returns>The state mutation result.</returns>
        internal static StateInitializationResult Initialize(
            IReadOnlyDictionary<string, string[]> expectedRows,
            IReflectionContractResolver reflectionContractResolver,
            IStateApplicationFactory stateApplicationFactory)
        {
            if (expectedRows == null) throw new ArgumentNullException(nameof(expectedRows));
            if (reflectionContractResolver == null) throw new ArgumentNullException(nameof(reflectionContractResolver));
            if (stateApplicationFactory == null) throw new ArgumentNullException(nameof(stateApplicationFactory));

            var reflectionContract = reflectionContractResolver.Resolve();
            var stateApplication = stateApplicationFactory.Create(reflectionContract);
            var actualRows = stateApplication.ReadRows();
            var convergedRows = ConvergeRows(actualRows, expectedRows, out var changed);

            if (!changed)
            {
                return new StateInitializationResult(false, Array.Empty<string>());
            }

            stateApplication.WriteRows(convergedRows);
            if (!stateApplication.Apply())
            {
                throw new InvalidOperationException("Shader-Core ProjectSettings did not accept the validated state update.");
            }

            stateApplication.Save();
            var reimportedAssets = stateApplication.ReimportConfiguredHostAssets(expectedRows.Keys);
            return new StateInitializationResult(true, reimportedAssets);
        }

        /// <summary>Loads the expected test-host and product selections from the versioned package manifest.</summary>
        /// <returns>A mapping from shader name to its expected ordered module IDs.</returns>
        internal static IReadOnlyDictionary<string, string[]> LoadExpectedRows()
        {
            var manifestJson = File.ReadAllText(GetManifestPath());
            var manifest = JsonUtility.FromJson<HostManifest>(manifestJson);
            if (manifest == null || manifest.schemaVersion != 1 || manifest.hosts == null)
            {
                throw new InvalidOperationException("The Shader-Core test-host manifest must be schema version 1 with a hosts array.");
            }

            var expectedRows = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (HostManifestEntry host in manifest.hosts)
            {
                if (host == null || string.IsNullOrEmpty(host.shaderName))
                {
                    throw new InvalidOperationException("The Shader-Core test-host manifest contains a host without shaderName.");
                }

                var modules = GetExpectedModules(host);
                if (modules.Length == 0 || modules.Any(string.IsNullOrEmpty) || !expectedRows.TryAdd(host.shaderName, modules))
                {
                    throw new InvalidOperationException($"The Shader-Core test-host manifest contains an invalid or duplicate host '{host.shaderName}'.");
                }
            }

            foreach (string productShaderName in ProductShaderNames)
            {
                if (!expectedRows.TryAdd(productShaderName, Array.Empty<string>()))
                {
                    throw new InvalidOperationException($"The Shader-Core test-host manifest must not redefine product shader '{productShaderName}'.");
                }
            }

            return expectedRows;
        }

        /// <summary>Converges target rows while retaining every unrelated row's order and module contents.</summary>
        /// <param name="actualRows">The current serialized rows.</param>
        /// <param name="expectedRows">The required test-host and product rows.</param>
        /// <param name="changed">Receives whether the serialized row sequence differs.</param>
        /// <returns>The deterministic converged row sequence.</returns>
        internal static IReadOnlyList<ShaderSettingRow> ConvergeRows(
            IReadOnlyList<ShaderSettingRow> actualRows,
            IReadOnlyDictionary<string, string[]> expectedRows,
            out bool changed)
        {
            if (actualRows == null) throw new ArgumentNullException(nameof(actualRows));
            if (expectedRows == null) throw new ArgumentNullException(nameof(expectedRows));

            var result = new List<ShaderSettingRow>(actualRows.Count + expectedRows.Count);
            var encounteredTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (ShaderSettingRow actualRow in actualRows)
            {
                if (!expectedRows.TryGetValue(actualRow.ShaderName, out string[] expectedModules))
                {
                    result.Add(actualRow);
                    continue;
                }

                if (encounteredTargets.Add(actualRow.ShaderName))
                {
                    result.Add(new ShaderSettingRow(actualRow.ShaderName, expectedModules));
                }
            }

            foreach (KeyValuePair<string, string[]> expectedRow in expectedRows)
            {
                if (encounteredTargets.Add(expectedRow.Key))
                {
                    result.Add(new ShaderSettingRow(expectedRow.Key, expectedRow.Value));
                }
            }

            changed = !RowsEqual(actualRows, result);
            return result;
        }

        /// <summary>Returns the package manifest path from Unity's project root.</summary>
        internal static string GetManifestPath()
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "jp.penguin.purebase", "Tests", "Config", "shader-core-test-hosts.json");
        }

        /// <summary>Gets every validated Shader-Core ProjectSettings reflection contract or fails before state application.</summary>
        private static ProjectSettingsReflectionContract GetValidatedProjectSettingsContract()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(candidate => candidate.GetName().Name == ShaderCoreAssemblyName);
            var settingsType = assembly?.GetType(ProjectSettingsTypeName, false);
            if (settingsType == null)
            {
                throw new InvalidOperationException($"Shader-Core 0.1.5 type '{ProjectSettingsTypeName}' was not loaded.");
            }

            ValidateProjectSettingsShape(settingsType);
            var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(settingsType);
            var instanceProperty = singletonType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static);
            var settings = instanceProperty?.GetValue(null) as UnityEngine.Object;
            if (settings == null)
            {
                throw new InvalidOperationException("Shader-Core ProjectSettings singleton was unavailable after shape validation.");
            }

            return new ProjectSettingsReflectionContract(settings, GetValidatedSaveMethod(settings.GetType()));
        }

        /// <summary>Validates the exact Shader-Core 0.1.5 reflection field shape required before state writes.</summary>
        private static void ValidateProjectSettingsShape(Type settingsType)
        {
            var settingsField = settingsType.GetField(ShaderSettingsFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (settingsField == null || !settingsField.FieldType.IsGenericType || settingsField.FieldType.GetGenericTypeDefinition() != typeof(List<>))
            {
                throw new InvalidOperationException("Shader-Core ProjectSettings.shaderSettings did not match the expected List<T> field shape.");
            }

            var rowType = settingsField.FieldType.GetGenericArguments()[0];
            var shaderNameField = rowType.GetField(ShaderNameFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var modulesField = rowType.GetField(ModulesFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (shaderNameField?.FieldType != typeof(string) || modulesField == null || !modulesField.FieldType.IsGenericType || modulesField.FieldType.GetGenericTypeDefinition() != typeof(List<>) || modulesField.FieldType.GetGenericArguments()[0] != typeof(string))
            {
                throw new InvalidOperationException("Shader-Core ShaderSettings row fields did not match the expected shadername/string and modules/List<string> shape.");
            }
        }

        /// <summary>Gets the required non-public parameterless Save method before serialized state can be changed.</summary>
        /// <param name="settingsType">The validated Shader-Core ProjectSettings runtime type.</param>
        /// <returns>The validated Save method.</returns>
        private static MethodInfo GetValidatedSaveMethod(Type settingsType)
        {
            var saveMethod = settingsType.GetMethod("Save", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (saveMethod == null)
            {
                throw new InvalidOperationException("Shader-Core ProjectSettings.Save() did not match the required non-public parameterless method contract.");
            }

            return saveMethod;
        }

        /// <summary>Gets and validates the serialized representation before any property is changed.</summary>
        private static SerializedProperty GetValidatedSettingsProperty(SerializedObject serializedSettings)
        {
            var settingsProperty = serializedSettings.FindProperty(ShaderSettingsFieldName);
            if (settingsProperty == null || !settingsProperty.isArray || settingsProperty.propertyType != SerializedPropertyType.Generic)
            {
                throw new InvalidOperationException("Shader-Core serialized shaderSettings did not match the expected array shape.");
            }

            if (settingsProperty.arraySize > 0)
            {
                var row = settingsProperty.GetArrayElementAtIndex(0);
                var shaderName = row.FindPropertyRelative(ShaderNameFieldName);
                var modules = row.FindPropertyRelative(ModulesFieldName);
                if (shaderName == null || shaderName.propertyType != SerializedPropertyType.String || modules == null || !modules.isArray)
                {
                    throw new InvalidOperationException("Shader-Core serialized ShaderSettings row did not match the expected field shape.");
                }
            }

            return settingsProperty;
        }

        /// <summary>Reads all existing rows without changing the serialized object.</summary>
        private static List<ShaderSettingRow> ReadRows(SerializedProperty settingsProperty)
        {
            var rows = new List<ShaderSettingRow>(settingsProperty.arraySize);
            for (var index = 0; index < settingsProperty.arraySize; index++)
            {
                var row = settingsProperty.GetArrayElementAtIndex(index);
                var shaderName = row.FindPropertyRelative(ShaderNameFieldName);
                var modules = row.FindPropertyRelative(ModulesFieldName);
                var moduleIds = new string[modules.arraySize];
                for (var moduleIndex = 0; moduleIndex < modules.arraySize; moduleIndex++)
                {
                    var module = modules.GetArrayElementAtIndex(moduleIndex);
                    if (module.propertyType != SerializedPropertyType.String)
                    {
                        throw new InvalidOperationException("Shader-Core serialized modules contained a non-string entry.");
                    }

                    moduleIds[moduleIndex] = module.stringValue;
                }

                rows.Add(new ShaderSettingRow(shaderName.stringValue, moduleIds));
            }

            return rows;
        }

        /// <summary>Writes the already-validated converged rows into the serialized settings object.</summary>
        private static void WriteRows(SerializedProperty settingsProperty, IReadOnlyList<ShaderSettingRow> rows)
        {
            settingsProperty.arraySize = rows.Count;
            for (var index = 0; index < rows.Count; index++)
            {
                var row = settingsProperty.GetArrayElementAtIndex(index);
                row.FindPropertyRelative(ShaderNameFieldName).stringValue = rows[index].ShaderName;
                var modules = row.FindPropertyRelative(ModulesFieldName);
                modules.arraySize = rows[index].Modules.Count;
                for (var moduleIndex = 0; moduleIndex < rows[index].Modules.Count; moduleIndex++)
                {
                    modules.GetArrayElementAtIndex(moduleIndex).stringValue = rows[index].Modules[moduleIndex];
                }
            }
        }

        /// <summary>Reimports only package test-host source assets after an actual selection-state change.</summary>
        private static IReadOnlyList<string> ReimportConfiguredHostAssets(IEnumerable<string> shaderNames)
        {
            var remainingNames = new HashSet<string>(shaderNames.Where(name => !ProductShaderNames.Contains(name)), StringComparer.Ordinal);
            var reimportedAssets = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("PureBaseTest", new[] { "Packages/jp.penguin.purebase/Tests/Fixtures/Hosts" }))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith(".scshader", StringComparison.OrdinalIgnoreCase)) continue;

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (shader != null && remainingNames.Remove(shader.name))
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    reimportedAssets.Add(assetPath);
                }
            }

            return reimportedAssets;
        }

        /// <summary>Resolves every required Shader-Core reflection contract before serialized state application.</summary>
        internal interface IReflectionContractResolver
        {
            /// <summary>Gets the validated reflection contract.</summary>
            /// <returns>The validated reflection contract.</returns>
            ProjectSettingsReflectionContract Resolve();
        }

        /// <summary>Creates a serialized state application after the reflection contract has been validated.</summary>
        internal interface IStateApplicationFactory
        {
            /// <summary>Creates an application bound to the validated ProjectSettings contract.</summary>
            /// <param name="reflectionContract">The validated reflection contract.</param>
            /// <returns>The state application.</returns>
            IStateApplication Create(ProjectSettingsReflectionContract reflectionContract);
        }

        /// <summary>Applies converged rows through the validated ProjectSettings serialized state.</summary>
        internal interface IStateApplication
        {
            /// <summary>Reads the current serialized rows.</summary>
            /// <returns>The current rows.</returns>
            IReadOnlyList<ShaderSettingRow> ReadRows();

            /// <summary>Writes converged rows without applying them.</summary>
            /// <param name="rows">The converged rows.</param>
            void WriteRows(IReadOnlyList<ShaderSettingRow> rows);

            /// <summary>Applies the pending serialized state.</summary>
            /// <returns>Whether state was applied.</returns>
            bool Apply();

            /// <summary>Persists the applied ProjectSettings state.</summary>
            void Save();

            /// <summary>Reimports configured host assets after a state mutation.</summary>
            /// <param name="shaderNames">The configured host and product shader names.</param>
            /// <returns>The reimported host asset paths.</returns>
            IReadOnlyList<string> ReimportConfiguredHostAssets(IEnumerable<string> shaderNames);
        }

        /// <summary>Captures the validated ProjectSettings singleton and its persistence method.</summary>
        internal sealed class ProjectSettingsReflectionContract
        {
            /// <summary>Initializes a validated ProjectSettings reflection contract.</summary>
            /// <param name="settings">The Shader-Core ProjectSettings singleton.</param>
            /// <param name="saveMethod">The required non-public parameterless Save method.</param>
            public ProjectSettingsReflectionContract(UnityEngine.Object settings, MethodInfo saveMethod)
            {
                Settings = settings ?? throw new ArgumentNullException(nameof(settings));
                SaveMethod = saveMethod ?? throw new ArgumentNullException(nameof(saveMethod));
            }

            /// <summary>Gets the Shader-Core ProjectSettings singleton.</summary>
            public UnityEngine.Object Settings { get; }

            /// <summary>Gets the required non-public parameterless Save method.</summary>
            public MethodInfo SaveMethod { get; }
        }

        /// <summary>Resolves the Shader-Core reflection contract from the loaded package assembly.</summary>
        private sealed class ShaderCoreReflectionContractResolver : IReflectionContractResolver
        {
            /// <inheritdoc />
            public ProjectSettingsReflectionContract Resolve()
            {
                return GetValidatedProjectSettingsContract();
            }
        }

        /// <summary>Creates the Unity serialized-state adapter after reflection validation succeeds.</summary>
        private sealed class SerializedStateApplicationFactory : IStateApplicationFactory
        {
            /// <inheritdoc />
            public IStateApplication Create(ProjectSettingsReflectionContract reflectionContract)
            {
                return new SerializedStateApplication(reflectionContract);
            }
        }

        /// <summary>Applies converged rows through Unity's serialized ProjectSettings representation.</summary>
        private sealed class SerializedStateApplication : IStateApplication
        {
            private readonly ProjectSettingsReflectionContract reflectionContract;
            private readonly SerializedObject serializedSettings;
            private readonly SerializedProperty settingsProperty;

            /// <summary>Initializes the state adapter after its reflection contract is already valid.</summary>
            /// <param name="reflectionContract">The validated ProjectSettings reflection contract.</param>
            public SerializedStateApplication(ProjectSettingsReflectionContract reflectionContract)
            {
                this.reflectionContract = reflectionContract ?? throw new ArgumentNullException(nameof(reflectionContract));
                serializedSettings = new SerializedObject(reflectionContract.Settings);
                settingsProperty = GetValidatedSettingsProperty(serializedSettings);
            }

            /// <inheritdoc />
            public IReadOnlyList<ShaderSettingRow> ReadRows()
            {
                return ShaderCoreTestStateInitializer.ReadRows(settingsProperty);
            }

            /// <inheritdoc />
            public void WriteRows(IReadOnlyList<ShaderSettingRow> rows)
            {
                ShaderCoreTestStateInitializer.WriteRows(settingsProperty, rows);
            }

            /// <inheritdoc />
            public bool Apply()
            {
                return serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }

            /// <inheritdoc />
            public void Save()
            {
                reflectionContract.SaveMethod.Invoke(reflectionContract.Settings, null);
            }

            /// <inheritdoc />
            public IReadOnlyList<string> ReimportConfiguredHostAssets(IEnumerable<string> shaderNames)
            {
                return ShaderCoreTestStateInitializer.ReimportConfiguredHostAssets(shaderNames);
            }
        }

        /// <summary>Gets the normalized module selection for a manifest host.</summary>
        private static string[] GetExpectedModules(HostManifestEntry host)
        {
            if (!string.IsNullOrEmpty(host.moduleUniqueId))
            {
                if (host.moduleUniqueIds != null && host.moduleUniqueIds.Length > 0)
                {
                    throw new InvalidOperationException($"Host '{host.shaderName}' cannot define both moduleUniqueId and moduleUniqueIds.");
                }

                return new[] { host.moduleUniqueId };
            }

            return host.moduleUniqueIds ?? Array.Empty<string>();
        }

        /// <summary>Compares row sequences including their module order.</summary>
        private static bool RowsEqual(IReadOnlyList<ShaderSettingRow> left, IReadOnlyList<ShaderSettingRow> right)
        {
            return left.Count == right.Count && !left.Where((row, index) => !row.Equals(right[index])).Any();
        }

        /// <summary>Represents one serialized Shader-Core selection row without retaining SerializedProperty references.</summary>
        internal sealed class ShaderSettingRow : IEquatable<ShaderSettingRow>
        {
            /// <summary>Initializes a serialized selection row.</summary>
            public ShaderSettingRow(string shaderName, IEnumerable<string> modules)
            {
                ShaderName = shaderName ?? throw new ArgumentNullException(nameof(shaderName));
                Modules = (modules ?? throw new ArgumentNullException(nameof(modules))).ToArray();
            }

            /// <summary>Gets the configured shader name.</summary>
            public string ShaderName { get; }

            /// <summary>Gets the configured module IDs in selection order.</summary>
            public IReadOnlyList<string> Modules { get; }

            /// <summary>Compares the shader name and exact module order.</summary>
            public bool Equals(ShaderSettingRow other)
            {
                return other != null && ShaderName == other.ShaderName && Modules.SequenceEqual(other.Modules);
            }

            /// <summary>Compares this row with another object.</summary>
            public override bool Equals(object obj)
            {
                return Equals(obj as ShaderSettingRow);
            }

            /// <summary>Gets a hash code for this row.</summary>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = ShaderName.GetHashCode();
                    foreach (string module in Modules) hashCode = (hashCode * 397) ^ (module?.GetHashCode() ?? 0);
                    return hashCode;
                }
            }
        }

        /// <summary>Reports whether initialization changed serialized state and which host sources were reimported.</summary>
        public readonly struct StateInitializationResult
        {
            /// <summary>Initializes an initialization result.</summary>
            public StateInitializationResult(bool changed, IReadOnlyList<string> reimportedAssets)
            {
                Changed = changed;
                ReimportedAssets = reimportedAssets ?? throw new ArgumentNullException(nameof(reimportedAssets));
            }

            /// <summary>Gets whether ProjectSettings rows changed.</summary>
            public bool Changed { get; }

            /// <summary>Gets test-host source assets reimported after state changed.</summary>
            public IReadOnlyList<string> ReimportedAssets { get; }
        }

        /// <summary>Represents the versioned Shader-Core host manifest.</summary>
        [Serializable]
        private sealed class HostManifest
        {
            /// <summary>Stores the manifest schema version.</summary>
            public int schemaVersion;

            /// <summary>Stores the fixed host entries.</summary>
            public HostManifestEntry[] hosts;
        }

        /// <summary>Represents one fixed Shader-Core host selection.</summary>
        [Serializable]
        private sealed class HostManifestEntry
        {
            /// <summary>Stores the Shader-Core shader name.</summary>
            public string shaderName;

            /// <summary>Stores one selected module ID.</summary>
            public string moduleUniqueId;

            /// <summary>Stores an ordered multi-module selection.</summary>
            public string[] moduleUniqueIds;
        }
    }
}