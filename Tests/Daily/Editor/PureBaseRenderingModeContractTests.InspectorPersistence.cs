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

// Defines Inspector registration, selection workflow, and persistence contracts for rendering modes.

// Defines the read-only material, normalizer, legacy-compatibility, and persistence contracts for rendering modes.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;


namespace PureBase.Tests.Daily
{
    public sealed partial class PureBaseRenderingModeContractTests
    {
        /// <summary>Requires the registered Shader-Core drawer to preserve mixed values without mutating a clean normalized selection.</summary>
        [Test]
        public void InspectorDrawerIsRegisteredForMixedSelectionAndExposesOneAtomicUndoWorkflow()
        {
            AssertRenderingModeDrawerRegistration();
            var opaque = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            var transparent = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            AssertMixedSelectionDrawerReadsAreReadOnly(opaque, transparent);
        }

        /// <summary>Requires the rendering-mode drawer registration to remain discoverable through Shader-Core.</summary>
        private static void AssertRenderingModeDrawerRegistration()
        {
            Assert.That(
                FindLoadedType("PureBase.Editor.PureBaseRenderingModeElement"),
                Is.Not.Null,
                "The dedicated rendering-mode Inspector drawer must be loaded."
            );

            Type attributeActionsType = FindLoadedType("jp.lilxyzw.shadercore.AttributeActions");
            Assert.That(attributeActionsType, Is.Not.Null, "Shader-Core AttributeActions was not loaded.");
            MethodInfo containsKey = attributeActionsType.GetMethod(
                "ContainsKey",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null
            );
            Assert.That(containsKey, Is.Not.Null);
            Assert.That((bool)containsKey.Invoke(null, new object[] { "PureBaseRenderingMode" }), Is.True);
        }

        /// <summary>Asserts the complete read-only mixed-selection drawer workflow for two normalized material modes.</summary>
        private static void AssertMixedSelectionDrawerReadsAreReadOnly(Material opaque, Material transparent)
        {
            MethodInfo apply = RequireApplyMethod();
            MethodInfo refreshSelection = RequireDrawerSelectionRefreshMethod();
            MethodInfo getSelectionDisplayState = RequireDrawerSelectionDisplayStateMethod();
            opaque.SetInteger("_RenderingMode", 0);
            transparent.SetInteger("_RenderingMode", 2);
            NormalizeAndClearMixedSelectionTargets(apply, opaque, transparent);
            MaterialState opaqueBaseline = MaterialState.Capture(opaque);
            MaterialState transparentBaseline = MaterialState.Capture(transparent);
            MaterialProperty property = MaterialEditor.GetMaterialProperty(new UnityEngine.Object[] { opaque, transparent }, "_RenderingMode");
            Assert.That(property.hasMixedValue, Is.True, "The rendering-mode field must expose mixed state before user selection.");
            opaqueBaseline.AssertEqual(opaque, "Opaque target after mixed field binding");
            transparentBaseline.AssertEqual(transparent, "Transparent target after mixed field binding");
            object selectionDisplayState = InvokeDrawerSelectionDisplayState(getSelectionDisplayState, new[] { opaque, transparent });
            AssertSelectionDisplayState(selectionDisplayState, true, new[] { "Opaque", "Cutout", "Transparent" });
            opaqueBaseline.AssertEqual(opaque, "Opaque target after mixed drawer display-state read");
            transparentBaseline.AssertEqual(transparent, "Transparent target after mixed drawer display-state read");
            InvokeDrawerSelectionRefresh(refreshSelection, new[] { opaque, transparent });
            opaqueBaseline.AssertEqual(opaque, "Opaque target after read-only mixed refresh");
            transparentBaseline.AssertEqual(transparent, "Transparent target after read-only mixed refresh");
        }

        /// <summary>Explicitly normalizes each mixed-selection target and restores the clean read-only baseline.</summary>
        private static void NormalizeAndClearMixedSelectionTargets(MethodInfo apply, Material opaque, Material transparent)
        {
            EditorUtility.ClearDirty(opaque);
            EditorUtility.ClearDirty(transparent);
            Assert.That(EditorUtility.IsDirty(opaque), Is.False, "The Opaque resync target must be clean before explicit normalization.");
            Assert.That(EditorUtility.IsDirty(transparent), Is.False, "The Transparent resync target must be clean before explicit normalization.");
            InvokeApply(apply, opaque);
            Assert.That(EditorUtility.IsDirty(opaque), Is.True, "Explicit normalization must move the clean Opaque resync target to dirty.");
            EditorUtility.ClearDirty(transparent);
            Assert.That(EditorUtility.IsDirty(transparent), Is.False, "The Transparent resync target must be clean immediately before its own explicit normalization.");
            InvokeApply(apply, transparent);
            Assert.That(EditorUtility.IsDirty(transparent), Is.True, "Explicit normalization must move the clean Transparent resync target to dirty.");
            EditorUtility.ClearDirty(opaque);
            EditorUtility.ClearDirty(transparent);
            Assert.That(EditorUtility.IsDirty(opaque), Is.False, "The Opaque mixed-selection baseline must be clean.");
            Assert.That(EditorUtility.IsDirty(transparent), Is.False, "The Transparent mixed-selection baseline must be clean.");
        }

        /// <summary>Requires the Cutoff drawer to register and report read-only visibility from supported Cutout selections only.</summary>
        [Test]
        public void CutoffDrawerIsRegisteredAndVisibilityModelIsReadOnly()
        {
            Type attributeActionsType = FindLoadedType("jp.lilxyzw.shadercore.AttributeActions");
            Assert.That(attributeActionsType, Is.Not.Null, "Shader-Core AttributeActions was not loaded.");
            MethodInfo containsKey = attributeActionsType.GetMethod(
                "ContainsKey",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null
            );
            Assert.That(containsKey, Is.Not.Null);
            Assert.That((bool)containsKey.Invoke(null, new object[] { "PureBaseCutoff" }), Is.True);

            Type cutoffElementType = FindLoadedType("PureBase.Editor.PureBaseCutoffElement");
            Assert.That(cutoffElementType, Is.Not.Null, "The dedicated Cutoff Inspector drawer must be loaded.");
            MethodInfo getSelectionDisplayState = cutoffElementType.GetMethod(
                "GetSelectionDisplayState",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(UnityEngine.Object[]) },
                null
            );
            Assert.That(getSelectionDisplayState, Is.Not.Null, "The Cutoff drawer must expose its read-only selection display model.");
            PropertyInfo isVisible = getSelectionDisplayState.ReturnType.GetProperty("IsVisible", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(isVisible, Is.Not.Null, "The Cutoff selection display model must expose visibility.");

            var opaque = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            var transparent = CreateMaterial(RequireProductShader("PureBase/Toon"));
            var cutout = CreateMaterial(RequireProductShader("PureBase/PBR"));
            var unsupported = CreateMaterial(RequireUnsupportedRenderingModeShader());
            opaque.SetInteger("_RenderingMode", Modes[0].value);
            transparent.SetInteger("_RenderingMode", Modes[2].value);
            cutout.SetInteger("_RenderingMode", Modes[1].value);
            MaterialState opaqueBaseline = MaterialState.Capture(opaque);
            MaterialState transparentBaseline = MaterialState.Capture(transparent);
            MaterialState cutoutBaseline = MaterialState.Capture(cutout);
            MaterialState unsupportedBaseline = MaterialState.Capture(unsupported);

            Func<UnityEngine.Object[], bool> getVisibility = targets =>
                (bool)isVisible.GetValue(getSelectionDisplayState.Invoke(null, new object[] { targets }));
            Assert.That(getVisibility(new UnityEngine.Object[] { opaque, transparent }), Is.False, "All Opaque and Transparent supported targets must hide Cutoff.");
            Assert.That(getVisibility(new UnityEngine.Object[] { opaque, transparent, unsupported }), Is.False, "Unsupported targets must not make Cutoff visible.");
            Assert.That(getVisibility(new UnityEngine.Object[] { opaque, transparent, cutout, unsupported }), Is.True, "Any supported Cutout target must make Cutoff visible.");
            opaqueBaseline.AssertEqual(opaque, "Opaque target after Cutoff display-state read");
            transparentBaseline.AssertEqual(transparent, "Transparent target after Cutoff display-state read");
            cutoutBaseline.AssertEqual(cutout, "Cutout target after Cutoff display-state read");
            unsupportedBaseline.AssertEqual(unsupported, "Unsupported target after Cutoff display-state read");
        }

        /// <summary>Requires the drawer's one-action multi-target boundary to validate, normalize, undo, redo, and refresh without incidental mutation.</summary>
        [Test]
        public void InspectorMultiTargetActionIsAtomicAndUndoRedoRefreshesAreReadOnly()
        {
            MethodInfo apply = RequireApplyMethod();
            MethodInfo applySelection = RequireDrawerSelectionApplyMethod();
            MethodInfo refreshSelection = RequireDrawerSelectionRefreshMethod();
            var first = CreateMaterial(RequireProductShader("PureBase/Unlit"));
            var second = CreateMaterial(RequireProductShader("PureBase/Toon"));
            var unsupported = CreateMaterial(RequireUnsupportedRenderingModeShader());
            int initialUndoGroup = Undo.GetCurrentGroup();
            try
            {
                first.SetInteger("_RenderingMode", 0);
                second.SetInteger("_RenderingMode", 1);
                InvokeApply(apply, first);
                InvokeApply(apply, second);
                MaterialState firstBefore = MaterialState.Capture(first);
                MaterialState secondBefore = MaterialState.Capture(second);
                MaterialState unsupportedBefore = MaterialState.Capture(unsupported);
                AssertRejectedSelectionPreservesEveryTarget(applySelection, first, second, unsupported, firstBefore, secondBefore, unsupportedBefore);

                InvokeDrawerSelectionApply(applySelection, new[] { first, second }, 2);
                int editUndoGroup = Undo.GetCurrentGroup();
                Assert.That(
                    editUndoGroup,
                    Is.EqualTo(initialUndoGroup + 1),
                    "One multi-target mode selection must create exactly one Undo group."
                );
                AssertModeState(first, Modes[2]);
                AssertModeState(second, Modes[2]);
                AssertUndoRedoRefreshesAreReadOnly(refreshSelection, first, second, firstBefore, secondBefore);
            }
            finally
            {
                Undo.RevertAllDownToGroup(initialUndoGroup);
            }
        }

        /// <summary>Asserts that a rejected mixed selection leaves all targets and the Undo stack unchanged.</summary>
        private static void AssertRejectedSelectionPreservesEveryTarget(MethodInfo applySelection, Material first, Material second, Material unsupported, MaterialState firstBefore, MaterialState secondBefore, MaterialState unsupportedBefore)
        {
            int undoBeforeRejectedSelection = Undo.GetCurrentGroup();
            Assert.Throws<InvalidOperationException>(
                () => InvokeDrawerSelectionApply(applySelection, new[] { first, second, unsupported }, 2),
                "The drawer must validate every selected material before mutating any valid target."
            );
            firstBefore.AssertEqual(first, "valid target after rejected mixed selection");
            secondBefore.AssertEqual(second, "second valid target after rejected mixed selection");
            unsupportedBefore.AssertEqual(unsupported, "unsupported target after rejected mixed selection");
            Assert.That(
                Undo.GetCurrentGroup(),
                Is.EqualTo(undoBeforeRejectedSelection),
                "A rejected multi-target selection must not create an Undo group before validation succeeds."
            );
        }

        /// <summary>Asserts that Undo, Redo, and their subsequent drawer refreshes preserve established material state.</summary>
        private static void AssertUndoRedoRefreshesAreReadOnly(MethodInfo refreshSelection, Material first, Material second, MaterialState firstBefore, MaterialState secondBefore)
        {
            Undo.PerformUndo();
            firstBefore.AssertEqual(first, "first target after Undo");
            secondBefore.AssertEqual(second, "second target after Undo");
            InvokeDrawerSelectionRefresh(refreshSelection, new[] { first, second });
            firstBefore.AssertEqual(first, "first target after read-only Undo refresh");
            secondBefore.AssertEqual(second, "second target after read-only Undo refresh");
            Undo.PerformRedo();
            AssertModeState(first, Modes[2]);
            AssertModeState(second, Modes[2]);
            MaterialState firstRedo = MaterialState.Capture(first);
            MaterialState secondRedo = MaterialState.Capture(second);
            InvokeDrawerSelectionRefresh(refreshSelection, new[] { first, second });
            firstRedo.AssertEqual(first, "first target after read-only Redo refresh");
            secondRedo.AssertEqual(second, "second target after read-only Redo refresh");
        }

        /// <summary>Requires explicit normalization to survive material and prefab save-reload while deleting every temporary asset.</summary>
        [Test]
        public void ExplicitNormalizationPersistsThroughMaterialAndPrefabSaveReloadAndCleansUp()
        {
            string materialPath = TemporaryAssetRoot + "/mode.mat";
            string prefabPath = TemporaryAssetRoot + "/mode.prefab";
            var retainedPaths = new List<string>();
            try
            {
                Assert.That(AssetDatabase.IsValidFolder(TemporaryAssetRoot), Is.False, "Temporary asset root already exists.");
                AssetDatabase.CreateFolder("Assets", "PureBaseRenderingModeTests");
                Material material = CreateAndPersistTransparentMaterial(materialPath);
                SaveMaterialAsPrefab(material, prefabPath);

                GameObject savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(savedPrefab, Is.Not.Null);
                SaveOnlyOwnedAssetAndReimport(savedPrefab, prefabPath);
                AssetDatabase.ImportAsset(materialPath, ImportAssetOptions.ForceSynchronousImport);
                Material reloaded = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                Assert.That(reloaded, Is.Not.Null);
                AssertModeState(reloaded, Modes[2]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.That(prefab, Is.Not.Null);
                Assert.That(prefab.GetComponent<Renderer>().sharedMaterial, Is.EqualTo(reloaded));
            }
            finally
            {
                if (!AssetDatabase.DeleteAsset(TemporaryAssetRoot))
                    retainedPaths.Add(TemporaryAssetRoot);
                if (AssetDatabase.IsValidFolder(TemporaryAssetRoot))
                    retainedPaths.Add(TemporaryAssetRoot);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(materialPath) != null)
                    retainedPaths.Add(materialPath);
                if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(prefabPath) != null)
                    retainedPaths.Add(prefabPath);
                Assert.That(retainedPaths, Is.Empty, $"Rendering-mode persistence test retained temporary assets: {string.Join(", ", retainedPaths)}.");
            }
        }

        /// <summary>Creates, normalizes, saves, and reloads the transient material used by the persistence contract.</summary>
        private Material CreateAndPersistTransparentMaterial(string materialPath)
        {
            var material = CreateMaterial(RequireProductShader("PureBase/Toon"));
            AssetDatabase.CreateAsset(material, materialPath);
            material.SetInteger("_RenderingMode", 2);
            InvokeApply(RequireApplyMethod(), material);
            Assert.That(EditorUtility.IsDirty(material), Is.True, "Explicit normalization must dirty the temporary material before the path-scoped save.");
            SaveOnlyOwnedAssetAndReimport(material, materialPath);
            material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            Assert.That(material, Is.Not.Null);
            return material;
        }

        /// <summary>Saves one transient material reference in a temporary prefab while releasing the source instance.</summary>
        private static void SaveMaterialAsPrefab(Material material, string prefabPath)
        {
            var instance = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                instance.GetComponent<Renderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Returns the required public normalizer method without statically referencing its not-yet-created assembly.</summary>
        /// <returns>The public static <c>Apply(Material)</c> method.</returns>
        private static MethodInfo RequireApplyMethod()
        {
            Type type = FindLoadedType("PureBase.Editor.PureBaseMaterialRenderingMode");
            Assert.That(type, Is.Not.Null, "PureBaseMaterialRenderingMode must be loaded from PureBase.Editor.");
            Assert.That(type.IsPublic, Is.True, "PureBaseMaterialRenderingMode must be public.");
            MethodInfo method = type.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
            Assert.That(method, Is.Not.Null, "PureBaseMaterialRenderingMode must expose public static Apply(Material).");
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)), "PureBaseMaterialRenderingMode.Apply(Material) must return void.");
            return method;
        }

        /// <summary>Returns the internal validated batch boundary used to verify rollback after an apply-time failure.</summary>
        /// <returns>The static <c>ApplyAll(IReadOnlyList&lt;Material&gt;)</c> method.</returns>
        private static MethodInfo RequireApplyAllMethod()
        {
            Type type = FindLoadedType("PureBase.Editor.PureBaseMaterialRenderingMode");
            Assert.That(type, Is.Not.Null, "PureBaseMaterialRenderingMode must be loaded from PureBase.Editor.");
            MethodInfo method = type.GetMethod(
                "ApplyAll",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[] { typeof(IReadOnlyList<Material>) },
                null
            );
            Assert.That(method, Is.Not.Null, "PureBaseMaterialRenderingMode must retain the validated batch boundary.");
            return method;
        }

        /// <summary>Returns the drawer operation that applies one selected mode to every validated target in one user action.</summary>
        /// <returns>The static <c>ApplySelection(Material[], int)</c> drawer operation.</returns>
        private static MethodInfo RequireDrawerSelectionApplyMethod()
        {
            return RequireDrawerMethod("ApplySelection", new[] { typeof(Material[]), typeof(int) });
        }

        /// <summary>Returns the drawer operation that refreshes the current selection without applying or normalizing material state.</summary>
        /// <returns>The static <c>RefreshSelection(Material[])</c> drawer operation.</returns>
        private static MethodInfo RequireDrawerSelectionRefreshMethod()
        {
            return RequireDrawerMethod("RefreshSelection", new[] { typeof(Material[]) });
        }

        /// <summary>Returns the drawer's read-only selection model boundary used to render mixed state and exact popup choices.</summary>
        /// <returns>The static <c>GetSelectionDisplayState(Material[])</c> drawer operation.</returns>
        private static MethodInfo RequireDrawerSelectionDisplayStateMethod()
        {
            MethodInfo method = RequireDrawerMethod("GetSelectionDisplayState", new[] { typeof(Material[]) });
            Assert.That(method.ReturnType, Is.Not.EqualTo(typeof(void)), "The drawer selection display-state boundary must return a readable UI model.");
            return method;
        }

        /// <summary>Returns one required static drawer operation without adding a compile-time dependency on its future assembly.</summary>
        /// <param name="methodName">The required operation name.</param>
        /// <param name="parameterTypes">The exact operation parameter types.</param>
        /// <returns>The required static drawer operation.</returns>
        private static MethodInfo RequireDrawerMethod(string methodName, Type[] parameterTypes)
        {
            Type type = FindLoadedType("PureBase.Editor.PureBaseRenderingModeElement");
            Assert.That(type, Is.Not.Null, "The dedicated rendering-mode Inspector drawer must be loaded.");
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                parameterTypes,
                null
            );
            Assert.That(
                method,
                Is.Not.Null,
                "PureBaseRenderingModeElement must expose the testable " + methodName + " selection boundary."
            );
            return method;
        }

        /// <summary>Invokes the public normalizer while preserving its original exception type for NUnit assertions.</summary>
        /// <param name="method">The reflected normalizer method.</param>
        /// <param name="material">The material passed to the normalizer.</param>
        private static void InvokeApply(MethodInfo method, Material material)
        {
            InvokeReflectedMethod(method, new object[] { material });
        }

        /// <summary>Invokes the validated batch boundary while preserving its original exception type.</summary>
        /// <param name="method">The reflected batch normalizer method.</param>
        /// <param name="materials">The material list passed to the batch normalizer.</param>
        private static void InvokeApplyAll(MethodInfo method, IReadOnlyList<Material> materials)
        {
            InvokeReflectedMethod(method, new object[] { materials });
        }

        /// <summary>Asserts that one rejected rendering-mode value preserves its established exception contract.</summary>
        /// <param name="exception">The exception thrown for the rejected value.</param>
        /// <param name="material">The rejected material identified by the exception.</param>
        /// <param name="value">The rejected rendering-mode value.</param>
        /// <param name="context">The operation context used in assertion diagnostics.</param>
        private static void AssertInvalidRenderingModeException(ArgumentOutOfRangeException exception, Material material, int value, string context)
        {
            Assert.That(exception, Is.Not.Null, context + " must throw an ArgumentOutOfRangeException.");
            Assert.That(exception.ParamName, Is.EqualTo("_RenderingMode"), context + " exception parameter.");
            Assert.That(exception.ActualValue, Is.EqualTo(value), context + " exception value.");
            StringAssert.Contains(material.name, exception.Message, context + " exception material identity.");
            StringAssert.Contains("0, 1, or 2", exception.Message, context + " exception supported values.");
        }

        /// <summary>Invokes the drawer's one-action multi-target operation while preserving its original exception type.</summary>
        /// <param name="method">The reflected drawer operation.</param>
        /// <param name="materials">The selected material targets.</param>
        /// <param name="mode">The requested serialized rendering-mode value.</param>
        private static void InvokeDrawerSelectionApply(MethodInfo method, Material[] materials, int mode)
        {
            InvokeReflectedMethod(method, new object[] { materials, mode });
        }

        /// <summary>Invokes the drawer's read-only selection refresh while preserving its original exception type.</summary>
        /// <param name="method">The reflected drawer refresh operation.</param>
        /// <param name="materials">The selected material targets.</param>
        private static void InvokeDrawerSelectionRefresh(MethodInfo method, Material[] materials)
        {
            InvokeReflectedMethod(method, new object[] { materials });
        }

        /// <summary>Reads the drawer-owned display model without invoking a user action or normalizing material state.</summary>
        /// <param name="method">The reflected drawer display-state operation.</param>
        /// <param name="materials">The selected material targets.</param>
        /// <returns>The read-only drawer display model.</returns>
        private static object InvokeDrawerSelectionDisplayState(MethodInfo method, Material[] materials)
        {
            return InvokeReflectedMethod(method, new object[] { materials });
        }

        /// <summary>Invokes a reflected operation while preserving its original exception type for NUnit assertions.</summary>
        /// <param name="method">The reflected operation.</param>
        /// <param name="arguments">The operation arguments.</param>
        private static object InvokeReflectedMethod(MethodInfo method, object[] arguments)
        {
            try
            {
                return method.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        /// <summary>Asserts the read-only drawer model for one current material selection.</summary>
        /// <param name="displayState">The reflection-returned drawer selection model.</param>
        /// <param name="expectedMixed">Whether the selection must be displayed as mixed.</param>
        /// <param name="expectedChoices">The complete ordered mode labels presented by the popup.</param>
        private static void AssertSelectionDisplayState(object displayState, bool expectedMixed, string[] expectedChoices)
        {
            Assert.That(displayState, Is.Not.Null, "The drawer must return a real selection display model.");
            Assert.That(ReadDisplayStateMember(displayState, "HasMixedValue"), Is.EqualTo(expectedMixed), "The drawer display model mixed indicator.");
            object choices = ReadDisplayStateMember(displayState, "Choices");
            var labels = choices as IEnumerable<string>;
            Assert.That(labels, Is.Not.Null, "The drawer display model Choices member must be a readable string sequence.");
            CollectionAssert.AreEqual(expectedChoices, labels, "The drawer popup must expose exactly the three supported rendering-mode choices.");
        }

        /// <summary>Reads one field or property from a drawer-owned selection display model without depending on its accessibility.</summary>
        /// <param name="displayState">The reflection-returned selection display model.</param>
        /// <param name="memberName">The required field or property name.</param>
        /// <returns>The member value.</returns>
        private static object ReadDisplayStateMember(object displayState, string memberName)
        {
            Type type = displayState.GetType();
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = type.GetProperty(memberName, Flags);
            if (property != null)
                return property.GetValue(displayState, null);
            FieldInfo field = type.GetField(memberName, Flags);
            Assert.That(field, Is.Not.Null, "The drawer display model must expose " + memberName + " as a readable field or property.");
            return field.GetValue(displayState);
        }

        /// <summary>Finds a type from all currently loaded assemblies without introducing a compile-time assembly dependency.</summary>
        /// <param name="fullName">The required fully-qualified type name.</param>
        /// <returns>The loaded type, or <see langword="null"/>.</returns>
    }
}
