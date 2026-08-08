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

// Displays the existing Shader-Core Cutoff range control only for Cutout material selections.

using System;
using jp.lilxyzw.shadercore;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using SCMaterialProperty = jp.lilxyzw.shadercore.MaterialProperty;

namespace PureBase.Editor
{
    /// <summary>Wraps the Shader-Core Cutoff range drawer with read-only rendering-mode visibility.</summary>
    internal static class PureBaseCutoffElement
    {
        /// <summary>Identifies the rendering-mode selector property.</summary>
        private const string RenderingModePropertyName = "_RenderingMode";

        /// <summary>Identifies the existing Cutoff range drawer and its stable bounds.</summary>
        private const string CutoffRangeAttribute = "SCRange(-0.001,1.001)";

        /// <summary>Registers the Cutoff drawer with Shader-Core when the Editor domain loads.</summary>
        [InitializeOnLoadMethod]
        private static void RegisterDrawer()
        {
            AttributeActions.AddDrawer("PureBaseCutoff", Draw);
        }

        /// <summary>Adds the existing Shader-Core range drawer with mode-controlled visibility.</summary>
        /// <param name="editor">The active Shader-Core material editor.</param>
        /// <param name="property">The Cutoff material property.</param>
        /// <param name="arguments">Unused drawer arguments.</param>
        /// <param name="container">The property container that owns the drawer UI.</param>
        private static void Draw(SCMaterialEditor editor, SCMaterialProperty property, string arguments, VisualElement container)
        {
            var rangeContainer = new VisualElement();
            container.Add(rangeContainer);
            editor.ShaderProperty(rangeContainer, property, new[] { CutoffRangeAttribute });

            UpdateVisibility(rangeContainer, property.targets);
            rangeContainer.RegisterCallback<SCUpdateEvent>(_ => UpdateVisibility(rangeContainer, property.targets));
        }

        /// <summary>Updates visibility from current selected material values without modifying them.</summary>
        /// <param name="container">The element that owns the existing range drawer.</param>
        /// <param name="targets">The shared targets of the represented Cutoff property.</param>
        private static void UpdateVisibility(VisualElement container, UnityEngine.Object[] targets)
        {
            SelectionDisplayState displayState = GetSelectionDisplayState(targets);
            container.style.display = displayState.IsVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Gets the read-only Cutoff drawer state for the supplied material selection.</summary>
        /// <param name="targets">The targets associated with the Cutoff material property.</param>
        /// <returns>The visibility state derived from supported Cutout targets.</returns>
        internal static SelectionDisplayState GetSelectionDisplayState(UnityEngine.Object[] targets)
        {
            if (targets == null)
                throw new ArgumentNullException(nameof(targets));

            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] is Material material
                    && PureBaseRenderingModeElement.IsPureBaseMaterial(material)
                    && material.HasProperty(RenderingModePropertyName)
                    && material.GetInteger(RenderingModePropertyName) == (int)PureBaseRenderingMode.Cutout)
                {
                    return new SelectionDisplayState(true);
                }
            }

            return new SelectionDisplayState(false);
        }

        /// <summary>Represents the read-only visibility of the Cutoff drawer for one material selection.</summary>
        internal readonly struct SelectionDisplayState
        {
            /// <summary>Initializes a Cutoff drawer selection display state.</summary>
            /// <param name="isVisible">Whether at least one supported selected material is Cutout.</param>
            public SelectionDisplayState(bool isVisible)
            {
                IsVisible = isVisible;
            }

            /// <summary>Gets whether the Cutoff drawer is visible for the current selection.</summary>
            public bool IsVisible { get; }
        }
    }
}
