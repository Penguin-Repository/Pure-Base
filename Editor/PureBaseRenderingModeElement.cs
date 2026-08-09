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

// Provides the Pure-Base rendering-mode Inspector popup and its explicit material-edit boundary.

using System;
using System.Collections.Generic;
using jp.lilxyzw.shadercore;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SCMaterialProperty = jp.lilxyzw.shadercore.MaterialProperty;

namespace PureBase.Editor
{
    /// <summary>Renders and applies the supported Pure-Base material rendering modes.</summary>
    internal sealed class PureBaseRenderingModeElement : PopupField<int>, IMaterialPropertyElement
    {
        /// <summary>Identifies the rendering-mode selector property.</summary>
        private const string RenderingModePropertyName = "_RenderingMode";

        /// <summary>Identifies the single Undo operation created by one popup action.</summary>
        private const string UndoName = "Set PureBase Rendering Mode";

        /// <summary>Explains the derived state of one Transparent material selection.</summary>
        private const string TransparentDescription = "Transparent materials use alpha blending. ZWrite, ShadowCaster, and Meta are disabled.";

        /// <summary>Explains the derived state when a mixed selection includes Transparent materials.</summary>
        private const string MixedTransparentDescription = "One or more selected materials are Transparent. Those materials use alpha blending, and their ZWrite, ShadowCaster, and Meta are disabled.";

        /// <summary>Defines the mode values in their popup display order.</summary>
        private static readonly List<int> ModeValues = new List<int>
        {
            (int)PureBaseRenderingMode.Opaque,
            (int)PureBaseRenderingMode.Cutout,
            (int)PureBaseRenderingMode.Transparent,
        };

        /// <summary>Defines the stable English mode labels used by the selection model.</summary>
        private static readonly string[] ModeNames =
        {
            "Opaque",
            "Cutout",
            "Transparent",
        };

        /// <summary>Stores the material property currently represented by this field.</summary>
        public SCMaterialProperty Property { get; set; }

        /// <summary>Stores the Shader-Core localization module identity.</summary>
        public string ModuleID { get; set; }

        /// <summary>Stores the localized Inspector label.</summary>
        public string LocalizedLabel { get; set; }

        /// <summary>Gets the popup and help-box root inserted into Shader-Core's property container.</summary>
        private VisualElement Root { get; }

        /// <summary>Gets the help box shown for a single Transparent selection.</summary>
        private HelpBox TransparentHelpBox { get; }

        /// <summary>Gets the help box shown for a mixed selection containing Transparent materials.</summary>
        private HelpBox MixedTransparentHelpBox { get; }

        /// <summary>Stores localized labels for the popup choices.</summary>
        private List<string> localizedModeNames;

        /// <summary>Registers the rendering-mode drawer with Shader-Core when the Editor domain loads.</summary>
        [InitializeOnLoadMethod]
        private static void RegisterDrawer()
        {
            AttributeActions.AddDrawer("PureBaseRenderingMode", Draw);
        }

        /// <summary>Adds the rendering-mode popup to one Shader-Core property container.</summary>
        /// <param name="_">The unused Shader-Core material editor.</param>
        /// <param name="property">The rendering-mode material property.</param>
        /// <param name="arguments">Unused drawer arguments.</param>
        /// <param name="container">The property container that owns the drawer UI.</param>
        private static void Draw(SCMaterialEditor _, SCMaterialProperty property, string arguments, VisualElement container)
        {
            var element = new PureBaseRenderingModeElement(property);
            container.Add(element.Root);
        }

        /// <summary>Initializes a PopupField-based rendering-mode drawer without normalizing material state.</summary>
        /// <param name="property">The rendering-mode material property represented by this element.</param>
        public PureBaseRenderingModeElement(SCMaterialProperty property)
        {
            localizedModeNames = CreateLocalizedModeNames();
            choices = ModeValues;
            formatListItemCallback = GetModeLabel;
            formatSelectedValueCallback = GetModeLabel;

            TransparentHelpBox = new HelpBox(SCL10n.L(TransparentDescription), HelpBoxMessageType.Info);
            MixedTransparentHelpBox = new HelpBox(SCL10n.L(MixedTransparentDescription), HelpBoxMessageType.Info);
            Root = new VisualElement();
            Root.Add(this);
            Root.Add(TransparentHelpBox);
            Root.Add(MixedTransparentHelpBox);

            ((IMaterialPropertyElement)this).InitializeVisualElement(this, UpdateUI, property);
            SCStyles.ApplyPopupStyle(this);
            style.flexGrow = 0;

            RegisterCallback<ChangeEvent<int>>(eventData =>
            {
                Material[] materials = GetPureBaseMaterials(Property.targets);
                ApplySelection(materials, eventData.newValue);
                UpdateUI();
            });
            RegisterCallback<SCLocalizeEvent>(_ => UpdateLocalizedText());
        }

        /// <summary>Applies one selected mode to every validated material as a single Undo operation.</summary>
        /// <param name="materials">The selected Pure-Base materials to update.</param>
        /// <param name="mode">The requested supported rendering-mode value.</param>
        internal static void ApplySelection(Material[] materials, int mode)
        {
            if (materials == null)
                throw new ArgumentNullException(nameof(materials));
            if (mode < (int)PureBaseRenderingMode.Opaque || mode > (int)PureBaseRenderingMode.Transparent)
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "The rendering mode must be Opaque, Cutout, or Transparent.");

            for (int index = 0; index < materials.Length; index++)
                PureBaseMaterialRenderingMode.Validate(materials[index]);

            if (materials.Length == 0)
                return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            Undo.RecordObjects(materials, UndoName);
            for (int index = 0; index < materials.Length; index++)
                materials[index].SetInteger(RenderingModePropertyName, mode);

            PureBaseMaterialRenderingMode.ApplyAll(materials);
            Undo.CollapseUndoOperations(undoGroup);
            SCUpdateEvent.Invoke();
        }

        /// <summary>Reads the supplied selection without applying or normalizing its material state.</summary>
        /// <param name="materials">The selected material targets.</param>
        internal static void RefreshSelection(Material[] materials)
        {
            GetSelectionDisplayState(materials);
        }

        /// <summary>Gets the read-only popup state for the supplied material selection.</summary>
        /// <param name="materials">The selected material targets.</param>
        /// <returns>The selected value, mixed state, and stable popup labels.</returns>
        internal static SelectionDisplayState GetSelectionDisplayState(Material[] materials)
        {
            if (materials == null)
                throw new ArgumentNullException(nameof(materials));

            int selectedValue = (int)PureBaseRenderingMode.Cutout;
            bool hasMixedValue = false;
            bool containsTransparent = false;
            if (materials.Length > 0)
            {
                selectedValue = GetDisplayModeValue(materials[0]);
                containsTransparent = selectedValue == (int)PureBaseRenderingMode.Transparent;
                for (int index = 1; index < materials.Length; index++)
                {
                    int value = GetDisplayModeValue(materials[index]);
                    hasMixedValue |= value != selectedValue;
                    containsTransparent |= value == (int)PureBaseRenderingMode.Transparent;
                }
            }

            return new SelectionDisplayState(selectedValue, hasMixedValue, containsTransparent, ModeNames);
        }

        /// <summary>Determines whether one material uses a stable Pure-Base shader.</summary>
        /// <param name="material">The material to inspect.</param>
        /// <returns><see langword="true"/> when the material uses one of the four supported shader names.</returns>
        internal static bool IsPureBaseMaterial(Material material)
        {
            return PureBaseMaterialRenderingMode.IsPureBaseMaterial(material);
        }

        /// <summary>Updates the popup and help-box state from the current material values without writing them.</summary>
        public void UpdateUI()
        {
            Material[] materials = GetPureBaseMaterials(Property.targets);
            SelectionDisplayState displayState = GetSelectionDisplayState(materials);
            showMixedValue = displayState.HasMixedValue;
            SetValueWithoutNotify(displayState.SelectedValue);
            textElement.text = GetModeLabel(displayState.SelectedValue);
            TransparentHelpBox.style.display = !displayState.HasMixedValue && displayState.SelectedValue == (int)PureBaseRenderingMode.Transparent
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            MixedTransparentHelpBox.style.display = displayState.HasMixedValue && displayState.ContainsTransparent
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        /// <summary>Creates localized labels for the fixed rendering-mode names.</summary>
        /// <returns>Localized labels in popup display order.</returns>
        private static List<string> CreateLocalizedModeNames()
        {
            var names = new List<string>(ModeNames.Length);
            for (int index = 0; index < ModeNames.Length; index++)
                names.Add(SCL10n.L(ModeNames[index]));

            return names;
        }

        /// <summary>Filters an arbitrary shared property target set to stable Pure-Base materials.</summary>
        /// <param name="targets">The targets associated with one material property.</param>
        /// <returns>Only targets supported by the Pure-Base rendering-mode contract.</returns>
        private static Material[] GetPureBaseMaterials(UnityEngine.Object[] targets)
        {
            var materials = new List<Material>(targets.Length);
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] is Material material && IsPureBaseMaterial(material))
                    materials.Add(material);
            }

            return materials.ToArray();
        }

        /// <summary>Returns a supported popup value without mutating malformed stored data.</summary>
        /// <param name="material">The material whose current selector value is read.</param>
        /// <returns>The stored mode when supported; otherwise the non-mutating Cutout fallback.</returns>
        private static int GetDisplayModeValue(Material material)
        {
            int mode = material.HasProperty(RenderingModePropertyName)
                ? material.GetInteger(RenderingModePropertyName)
                : (int)PureBaseRenderingMode.Cutout;
            return mode >= (int)PureBaseRenderingMode.Opaque && mode <= (int)PureBaseRenderingMode.Transparent
                ? mode
                : (int)PureBaseRenderingMode.Cutout;
        }

        /// <summary>Gets the localized display label for one popup value.</summary>
        /// <param name="value">The rendering-mode value to label.</param>
        /// <returns>The localized label, or an empty string for an unsupported value.</returns>
        private string GetModeLabel(int value)
        {
            int index = ModeValues.IndexOf(value);
            return index >= 0 ? localizedModeNames[index] : string.Empty;
        }

        /// <summary>Refreshes localized labels and help text without changing any material.</summary>
        private void UpdateLocalizedText()
        {
            SCL10n.Load(ModuleID);
            localizedModeNames = CreateLocalizedModeNames();
            TransparentHelpBox.text = SCL10n.L(TransparentDescription);
            MixedTransparentHelpBox.text = SCL10n.L(MixedTransparentDescription);
            UpdateUI();
        }

        /// <summary>Represents the read-only Inspector state of one material selection.</summary>
        internal readonly struct SelectionDisplayState
        {
            /// <summary>Initializes a rendering-mode selection display state.</summary>
            /// <param name="selectedValue">The non-mixed popup value.</param>
            /// <param name="hasMixedValue">Whether selected materials have different modes.</param>
            /// <param name="containsTransparent">Whether any selected material is Transparent.</param>
            /// <param name="choices">The stable labels displayed by the popup.</param>
            public SelectionDisplayState(int selectedValue, bool hasMixedValue, bool containsTransparent, IReadOnlyList<string> choices)
            {
                SelectedValue = selectedValue;
                HasMixedValue = hasMixedValue;
                ContainsTransparent = containsTransparent;
                Choices = choices;
            }

            /// <summary>Gets the selected value used when the field is not mixed.</summary>
            public int SelectedValue { get; }

            /// <summary>Gets whether the selection contains multiple rendering-mode values.</summary>
            public bool HasMixedValue { get; }

            /// <summary>Gets whether any selected material uses Transparent mode.</summary>
            public bool ContainsTransparent { get; }

            /// <summary>Gets the exact ordered popup labels.</summary>
            public IReadOnlyList<string> Choices { get; }
        }
    }
}
