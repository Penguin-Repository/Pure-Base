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

// Synchronizes the derived rendering state for supported Pure-Base materials.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using jp.lilxyzw.shadercore;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PureBase.Editor
{
    /// <summary>Identifies the supported Pure-Base material rendering modes.</summary>
    public enum PureBaseRenderingMode
    {
        /// <summary>Uses opaque blending and opaque contribution passes.</summary>
        Opaque = 0,

        /// <summary>Uses alpha-tested rendering with the shader-default queue.</summary>
        Cutout = 1,

        /// <summary>Uses alpha blending without depth writes or contribution passes.</summary>
        Transparent = 2,
    }

    /// <summary>Explicitly synchronizes derived rendering state for supported Pure-Base materials.</summary>
    public static class PureBaseMaterialRenderingMode
    {
        /// <summary>Identifies the rendering-mode selector property.</summary>
        private const string RenderingModePropertyName = "_RenderingMode";

        /// <summary>Identifies the source blend-factor property.</summary>
        private const string SourceBlendPropertyName = "_SrcBlend";

        /// <summary>Identifies the destination blend-factor property.</summary>
        private const string DestinationBlendPropertyName = "_DstBlend";

        /// <summary>Identifies the depth-write property.</summary>
        private const string DepthWritePropertyName = "_ZWrite";

        /// <summary>Identifies the additive source blend-factor property.</summary>
        private const string AdditiveSourceBlendPropertyName = "_AddSrcBlend";

        /// <summary>Identifies the additive destination blend-factor property.</summary>
        private const string AdditiveDestinationBlendPropertyName = "_AddDstBlend";

        /// <summary>Identifies the RenderType tag.</summary>
        private const string RenderTypeTagName = "RenderType";

        /// <summary>Identifies the Opaque local keyword.</summary>
        private const string OpaqueKeyword = "PUREBASE_RENDERING_OPAQUE";

        /// <summary>Identifies the Transparent local keyword.</summary>
        private const string TransparentKeyword = "PUREBASE_RENDERING_TRANSPARENT";

        /// <summary>Identifies the ShadowCaster shader pass.</summary>
        private const string ShadowCasterPassName = "ShadowCaster";

        /// <summary>Identifies the Meta shader pass.</summary>
        private const string MetaPassName = "Meta";

        /// <summary>Identifies the selected-material resynchronization command.</summary>
        private const string ResyncMenuItemName = "Assets/PureBase/Resync Rendering Mode";

        /// <summary>Identifies the Undo operation for selected-material resynchronization.</summary>
        private const string ResyncUndoName = "Resync PureBase Rendering Mode";

        /// <summary>Lists the only stable public shader names owned by Pure-Base.</summary>
        private static readonly HashSet<string> PureBaseShaderNames = new HashSet<string>(
            StringComparer.Ordinal
        )
        {
            "PureBase/Unlit",
            "PureBase/Toon",
            "PureBase/PBR",
            "PureBase/Hybrid",
        };

        /// <summary>Lists the hidden state properties that are synchronized with the selected mode.</summary>
        private static readonly string[] RequiredStatePropertyNames =
        {
            SourceBlendPropertyName,
            DestinationBlendPropertyName,
            DepthWritePropertyName,
            AdditiveSourceBlendPropertyName,
            AdditiveDestinationBlendPropertyName,
        };

        /// <summary>Defines every derived state value for one rendering mode.</summary>
        private static readonly ModeState[] ModeStates =
        {
            ModeState.CreateOpaque(),
            ModeState.CreateCutout(),
            ModeState.CreateTransparent(),
        };

        /// <summary>Applies the derived state for the material's current rendering-mode value.</summary>
        /// <param name="material">The supported Pure-Base material to synchronize.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="material"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the material does not expose the supported Pure-Base rendering-mode contract.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the rendering-mode value is not an integral supported value.</exception>
        public static void Apply(Material material)
        {
            Validate(material);
            ApplyValidatedMaterials(new[] { material });
        }

        /// <summary>Validates and atomically applies derived rendering state to an already-filtered material selection.</summary>
        /// <param name="materials">The supported Pure-Base materials to synchronize.</param>
        internal static void ApplyAll(IReadOnlyList<Material> materials)
        {
            if (materials == null)
                throw new ArgumentNullException(nameof(materials));

            ValidateAll(materials);
            ApplyValidatedMaterials(materials);
        }

        /// <summary>Determines whether one material uses a stable Pure-Base shader.</summary>
        /// <param name="material">The material to inspect.</param>
        /// <returns><see langword="true"/> when the material uses one of the four supported shader names.</returns>
        internal static bool IsPureBaseMaterial(Material material)
        {
            return material != null
                && material.shader != null
                && PureBaseShaderNames.Contains(material.shader.name);
        }

        /// <summary>Validates one material without modifying its serialized state.</summary>
        /// <param name="material">The material to validate.</param>
        internal static void Validate(Material material)
        {
            if (material == null)
                throw new ArgumentNullException(nameof(material));

            if (!IsPureBaseMaterial(material))
                throw CreateValidationException(
                    material,
                    "its shader is not a supported Pure-Base shader"
                );

            Shader shader = material.shader;

            if (!material.HasProperty(RenderingModePropertyName))
                throw CreateValidationException(
                    material,
                    "it does not expose the Pure-Base rendering-mode property"
                );

            int renderingModePropertyIndex = shader.FindPropertyIndex(RenderingModePropertyName);
            if (
                renderingModePropertyIndex < 0
                || shader.GetPropertyType(renderingModePropertyIndex) != ShaderPropertyType.Int
            )
                throw CreateValidationException(
                    material,
                    "it does not expose the Pure-Base integer rendering-mode property"
                );

            for (int index = 0; index < RequiredStatePropertyNames.Length; index++)
            {
                if (!material.HasProperty(RequiredStatePropertyNames[index]))
                    throw CreateValidationException(
                        material,
                        "it does not expose the complete Pure-Base rendering-mode state contract"
                    );
            }

            GetModeIndex(material);
        }

        /// <summary>Creates a validation exception that identifies the rejected material and its contract failure.</summary>
        /// <param name="material">The non-null material that failed validation.</param>
        /// <param name="reason">The specific rendering-mode contract rejection reason.</param>
        /// <returns>An exception that preserves the established validation exception type.</returns>
        private static InvalidOperationException CreateValidationException(
            Material material,
            string reason
        )
        {
            return new InvalidOperationException(
                "Material '" + material.name + "' was rejected because " + reason + "."
            );
        }

        /// <summary>Invokes selected-material resynchronization from Unity's Assets menu.</summary>
        [MenuItem(ResyncMenuItemName)]
        private static void ResyncSelectedMaterials()
        {
            Material[] materials = GetSelectedPureBaseMaterials();
            try
            {
                ValidateAll(materials);

                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(ResyncUndoName);
                Undo.RecordObjects(materials, ResyncUndoName);
                ApplyValidatedMaterials(materials);
                Undo.CollapseUndoOperations(undoGroup);
                SCUpdateEvent.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>Determines whether selected-material resynchronization is currently available.</summary>
        /// <returns><see langword="true"/> when at least one selected material satisfies the complete contract.</returns>
        [MenuItem(ResyncMenuItemName, true)]
        private static bool ValidateResyncSelectedMaterials()
        {
            Material[] materials = GetSelectedPureBaseMaterials();
            if (materials.Length == 0)
                return false;

            try
            {
                ValidateAll(materials);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        /// <summary>Returns selected assets whose stable shader names belong to Pure-Base.</summary>
        /// <returns>The filtered selection, without any non-Pure-Base materials.</returns>
        private static Material[] GetSelectedPureBaseMaterials()
        {
            Material[] selectedMaterials = Selection.GetFiltered<Material>(SelectionMode.Assets);
            var pureBaseMaterials = new List<Material>(selectedMaterials.Length);
            for (int index = 0; index < selectedMaterials.Length; index++)
            {
                Material material = selectedMaterials[index];
                if (IsPureBaseMaterial(material))
                    pureBaseMaterials.Add(material);
            }

            return pureBaseMaterials.ToArray();
        }

        /// <summary>Validates every material before an operation can mutate any selected target.</summary>
        /// <param name="materials">The materials to validate.</param>
        private static void ValidateAll(IReadOnlyList<Material> materials)
        {
            for (int index = 0; index < materials.Count; index++)
                Validate(materials[index]);
        }

        /// <summary>Captures, applies, and restores a fully prevalidated material set as one atomic operation.</summary>
        /// <param name="materials">The prevalidated materials to synchronize.</param>
        private static void ApplyValidatedMaterials(IReadOnlyList<Material> materials)
        {
            var snapshots = new MaterialStateSnapshot[materials.Count];
            for (int index = 0; index < materials.Count; index++)
                snapshots[index] = MaterialStateSnapshot.Capture(materials[index]);

            try
            {
                for (int index = 0; index < materials.Count; index++)
                {
                    Material material = materials[index];
                    ApplyState(material, ModeStates[GetModeIndex(material)]);
                    EditorUtility.SetDirty(material);
                }
            }
            catch (Exception applyException)
            {
                Exception rollbackException = null;
                for (int index = snapshots.Length - 1; index >= 0; index--)
                {
                    try
                    {
                        snapshots[index].Restore(materials[index]);
                    }
                    catch (Exception exception)
                    {
                        if (rollbackException == null)
                            rollbackException = exception;
                    }
                }

                if (rollbackException != null)
                {
                    throw new AggregateException(
                        "Rendering-mode normalization failed and rollback encountered errors.",
                        new[] { applyException, rollbackException }
                    );
                }

                throw;
            }
        }

        /// <summary>Applies the derived fields that are owned by the rendering-mode state table.</summary>
        /// <param name="material">The material to synchronize.</param>
        /// <param name="state">The state selected by the material's rendering-mode value.</param>
        private static void ApplyState(Material material, ModeState state)
        {
            material.SetFloat(SourceBlendPropertyName, state.SourceBlend);
            material.SetFloat(DestinationBlendPropertyName, state.DestinationBlend);
            material.SetFloat(DepthWritePropertyName, state.DepthWrite);
            material.SetFloat(AdditiveSourceBlendPropertyName, state.AdditiveSourceBlend);
            material.SetFloat(AdditiveDestinationBlendPropertyName, state.AdditiveDestinationBlend);
            material.SetOverrideTag(RenderTypeTagName, state.RenderType);
            material.renderQueue = state.RawRenderQueue;
            SetKeyword(material, OpaqueKeyword, state.EnableOpaqueKeyword);
            SetKeyword(material, TransparentKeyword, state.EnableTransparentKeyword);
            material.SetShaderPassEnabled(ShadowCasterPassName, state.EnableContributionPasses);
            material.SetShaderPassEnabled(MetaPassName, state.EnableContributionPasses);
        }

        /// <summary>Sets one local keyword without affecting any other keyword.</summary>
        /// <param name="material">The material whose keyword state changes.</param>
        /// <param name="keyword">The exact keyword to change.</param>
        /// <param name="enabled">Whether the keyword must be enabled.</param>
        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
                material.EnableKeyword(keyword);
            else
                material.DisableKeyword(keyword);
        }

        /// <summary>Returns the validated rendering-mode array index for a material.</summary>
        /// <param name="material">The material whose mode is read.</param>
        /// <returns>The zero-based state-table index.</returns>
        private static int GetModeIndex(Material material)
        {
            int value = material.GetInteger(RenderingModePropertyName);
            if (value < 0 || value > 2)
                throw new ArgumentOutOfRangeException(
                    RenderingModePropertyName,
                    value,
                    "Material '"
                        + material.name
                        + "' has a rendering-mode value outside the supported range: 0, 1, or 2."
                );

            return value;
        }

        /// <summary>Defines all derived rendering values for one supported mode.</summary>
        private readonly struct ModeState
        {
            /// <summary>Creates the derived state for opaque rendering.</summary>
            /// <returns>The immutable opaque rendering state.</returns>
            public static ModeState CreateOpaque()
            {
                return new ModeState(
                    (int)BlendMode.One,
                    (int)BlendMode.Zero,
                    1,
                    (int)BlendMode.One,
                    (int)BlendMode.One,
                    "Opaque",
                    2000,
                    new ModeStateFlags(true, false, true)
                );
            }

            /// <summary>Creates the derived state for cutout rendering.</summary>
            /// <returns>The immutable cutout rendering state.</returns>
            public static ModeState CreateCutout()
            {
                return new ModeState(
                    (int)BlendMode.One,
                    (int)BlendMode.Zero,
                    1,
                    (int)BlendMode.One,
                    (int)BlendMode.One,
                    string.Empty,
                    -1,
                    new ModeStateFlags(false, false, true)
                );
            }

            /// <summary>Creates the derived state for transparent rendering.</summary>
            /// <returns>The immutable transparent rendering state.</returns>
            public static ModeState CreateTransparent()
            {
                return new ModeState(
                    (int)BlendMode.SrcAlpha,
                    (int)BlendMode.OneMinusSrcAlpha,
                    0,
                    (int)BlendMode.SrcAlpha,
                    (int)BlendMode.One,
                    "Transparent",
                    3000,
                    new ModeStateFlags(false, true, false)
                );
            }

            /// <summary>Initializes the immutable derived rendering state.</summary>
            /// <param name="sourceBlend">The base-pass source blend factor.</param>
            /// <param name="destinationBlend">The base-pass destination blend factor.</param>
            /// <param name="depthWrite">The depth-write state.</param>
            /// <param name="additiveSourceBlend">The additive-pass source blend factor.</param>
            /// <param name="additiveDestinationBlend">The additive-pass destination blend factor.</param>
            /// <param name="renderType">The RenderType override tag.</param>
            /// <param name="rawRenderQueue">The raw material queue override.</param>
            /// <param name="flags">The keyword and contribution-pass state.</param>
            private ModeState(
                int sourceBlend,
                int destinationBlend,
                int depthWrite,
                int additiveSourceBlend,
                int additiveDestinationBlend,
                string renderType,
                int rawRenderQueue,
                ModeStateFlags flags
            )
            {
                SourceBlend = sourceBlend;
                DestinationBlend = destinationBlend;
                DepthWrite = depthWrite;
                AdditiveSourceBlend = additiveSourceBlend;
                AdditiveDestinationBlend = additiveDestinationBlend;
                RenderType = renderType;
                RawRenderQueue = rawRenderQueue;
                EnableOpaqueKeyword = flags.EnableOpaqueKeyword;
                EnableTransparentKeyword = flags.EnableTransparentKeyword;
                EnableContributionPasses = flags.EnableContributionPasses;
            }

            /// <summary>Gets the base-pass source blend factor.</summary>
            public int SourceBlend { get; }

            /// <summary>Gets the base-pass destination blend factor.</summary>
            public int DestinationBlend { get; }

            /// <summary>Gets the depth-write state.</summary>
            public int DepthWrite { get; }

            /// <summary>Gets the additive-pass source blend factor.</summary>
            public int AdditiveSourceBlend { get; }

            /// <summary>Gets the additive-pass destination blend factor.</summary>
            public int AdditiveDestinationBlend { get; }

            /// <summary>Gets the RenderType override tag.</summary>
            public string RenderType { get; }

            /// <summary>Gets the raw material queue override.</summary>
            public int RawRenderQueue { get; }

            /// <summary>Gets whether the Opaque keyword is enabled.</summary>
            public bool EnableOpaqueKeyword { get; }

            /// <summary>Gets whether the Transparent keyword is enabled.</summary>
            public bool EnableTransparentKeyword { get; }

            /// <summary>Gets whether ShadowCaster and Meta are enabled.</summary>
            public bool EnableContributionPasses { get; }
        }

        /// <summary>Groups the boolean rendering-mode flags for immutable state construction.</summary>
        private readonly struct ModeStateFlags
        {
            /// <summary>Initializes the immutable rendering-mode flags.</summary>
            /// <param name="enableOpaqueKeyword">Whether the Opaque keyword is enabled.</param>
            /// <param name="enableTransparentKeyword">Whether the Transparent keyword is enabled.</param>
            /// <param name="enableContributionPasses">Whether ShadowCaster and Meta are enabled.</param>
            public ModeStateFlags(
                bool enableOpaqueKeyword,
                bool enableTransparentKeyword,
                bool enableContributionPasses
            )
            {
                EnableOpaqueKeyword = enableOpaqueKeyword;
                EnableTransparentKeyword = enableTransparentKeyword;
                EnableContributionPasses = enableContributionPasses;
            }

            /// <summary>Gets whether the Opaque keyword is enabled.</summary>
            public bool EnableOpaqueKeyword { get; }

            /// <summary>Gets whether the Transparent keyword is enabled.</summary>
            public bool EnableTransparentKeyword { get; }

            /// <summary>Gets whether ShadowCaster and Meta are enabled.</summary>
            public bool EnableContributionPasses { get; }
        }

        /// <summary>Captures every field that the normalizer may modify for rollback.</summary>
        private readonly struct MaterialStateSnapshot
        {
            /// <summary>Initializes the immutable rollback snapshot.</summary>
            /// <param name="sourceBlend">The prior base-pass source blend factor.</param>
            /// <param name="destinationBlend">The prior base-pass destination blend factor.</param>
            /// <param name="depthWrite">The prior depth-write state.</param>
            /// <param name="additiveSourceBlend">The prior additive-pass source blend factor.</param>
            /// <param name="additiveDestinationBlend">The prior additive-pass destination blend factor.</param>
            /// <param name="metadata">The raw tag, queue, pass, keyword, and dirty-state metadata.</param>
            private MaterialStateSnapshot(
                float sourceBlend,
                float destinationBlend,
                float depthWrite,
                float additiveSourceBlend,
                float additiveDestinationBlend,
                MaterialStateSnapshotMetadata metadata
            )
            {
                SourceBlend = sourceBlend;
                DestinationBlend = destinationBlend;
                DepthWrite = depthWrite;
                AdditiveSourceBlend = additiveSourceBlend;
                AdditiveDestinationBlend = additiveDestinationBlend;
                HasRenderTypeOverride = metadata.HasRenderTypeOverride;
                RenderTypeOverride = metadata.RenderTypeOverride;
                RawRenderQueue = metadata.RawRenderQueue;
                OpaqueKeywordEnabled = metadata.OpaqueKeywordEnabled;
                TransparentKeywordEnabled = metadata.TransparentKeywordEnabled;
                ShadowCasterEnabled = metadata.ShadowCasterEnabled;
                MetaEnabled = metadata.MetaEnabled;
                WasDirty = metadata.WasDirty;
            }

            /// <summary>Gets the prior base-pass source blend factor.</summary>
            private float SourceBlend { get; }

            /// <summary>Gets the prior base-pass destination blend factor.</summary>
            private float DestinationBlend { get; }

            /// <summary>Gets the prior depth-write state.</summary>
            private float DepthWrite { get; }

            /// <summary>Gets the prior additive-pass source blend factor.</summary>
            private float AdditiveSourceBlend { get; }

            /// <summary>Gets the prior additive-pass destination blend factor.</summary>
            private float AdditiveDestinationBlend { get; }

            /// <summary>Gets whether a prior RenderType override existed in the raw tag map.</summary>
            private bool HasRenderTypeOverride { get; }

            /// <summary>Gets the prior raw RenderType override value.</summary>
            private string RenderTypeOverride { get; }

            /// <summary>Gets the prior raw material queue override.</summary>
            private int RawRenderQueue { get; }

            /// <summary>Gets whether the Opaque keyword was enabled.</summary>
            private bool OpaqueKeywordEnabled { get; }

            /// <summary>Gets whether the Transparent keyword was enabled.</summary>
            private bool TransparentKeywordEnabled { get; }

            /// <summary>Gets whether ShadowCaster was enabled.</summary>
            private bool ShadowCasterEnabled { get; }

            /// <summary>Gets whether Meta was enabled.</summary>
            private bool MetaEnabled { get; }

            /// <summary>Gets whether the material was dirty before normalization.</summary>
            private bool WasDirty { get; }

            /// <summary>Captures the normalizer-owned state from one material.</summary>
            /// <param name="material">The material to capture.</param>
            /// <returns>A rollback snapshot for <paramref name="material"/>.</returns>
            public static MaterialStateSnapshot Capture(Material material)
            {
                bool hasRenderTypeOverride = TryGetRawRenderTypeOverride(
                    material,
                    out string renderTypeOverride
                );
                return new MaterialStateSnapshot(
                    material.GetFloat(SourceBlendPropertyName),
                    material.GetFloat(DestinationBlendPropertyName),
                    material.GetFloat(DepthWritePropertyName),
                    material.GetFloat(AdditiveSourceBlendPropertyName),
                    material.GetFloat(AdditiveDestinationBlendPropertyName),
                    new MaterialStateSnapshotMetadata(
                        hasRenderTypeOverride,
                        renderTypeOverride,
                        GetRawRenderQueue(material),
                        material.IsKeywordEnabled(OpaqueKeyword),
                        material.IsKeywordEnabled(TransparentKeyword),
                        material.GetShaderPassEnabled(ShadowCasterPassName),
                        material.GetShaderPassEnabled(MetaPassName),
                        EditorUtility.IsDirty(material)
                    )
                );
            }

            /// <summary>Restores the normalizer-owned state to one material.</summary>
            /// <param name="material">The material to restore.</param>
            public void Restore(Material material)
            {
                material.SetFloat(SourceBlendPropertyName, SourceBlend);
                material.SetFloat(DestinationBlendPropertyName, DestinationBlend);
                material.SetFloat(DepthWritePropertyName, DepthWrite);
                material.SetFloat(AdditiveSourceBlendPropertyName, AdditiveSourceBlend);
                material.SetFloat(AdditiveDestinationBlendPropertyName, AdditiveDestinationBlend);
                material.SetOverrideTag(
                    RenderTypeTagName,
                    HasRenderTypeOverride ? RenderTypeOverride : string.Empty
                );
                material.renderQueue = RawRenderQueue;
                SetKeyword(material, OpaqueKeyword, OpaqueKeywordEnabled);
                SetKeyword(material, TransparentKeyword, TransparentKeywordEnabled);
                material.SetShaderPassEnabled(ShadowCasterPassName, ShadowCasterEnabled);
                material.SetShaderPassEnabled(MetaPassName, MetaEnabled);
                if (!WasDirty)
                    EditorUtility.ClearDirty(material);
            }
        }

        /// <summary>Groups the remaining rollback values for immutable snapshot construction.</summary>
        private readonly struct MaterialStateSnapshotMetadata
        {
            /// <summary>Initializes the immutable rollback metadata.</summary>
            /// <param name="hasRenderTypeOverride">Whether a prior RenderType override existed in the raw tag map.</param>
            /// <param name="renderTypeOverride">The prior raw RenderType override value.</param>
            /// <param name="rawRenderQueue">The prior raw material queue override.</param>
            /// <param name="opaqueKeywordEnabled">Whether the Opaque keyword was enabled.</param>
            /// <param name="transparentKeywordEnabled">Whether the Transparent keyword was enabled.</param>
            /// <param name="shadowCasterEnabled">Whether ShadowCaster was enabled.</param>
            /// <param name="metaEnabled">Whether Meta was enabled.</param>
            /// <param name="wasDirty">Whether the material was dirty before normalization.</param>
            public MaterialStateSnapshotMetadata(
                bool hasRenderTypeOverride,
                string renderTypeOverride,
                int rawRenderQueue,
                bool opaqueKeywordEnabled,
                bool transparentKeywordEnabled,
                bool shadowCasterEnabled,
                bool metaEnabled,
                bool wasDirty
            )
            {
                HasRenderTypeOverride = hasRenderTypeOverride;
                RenderTypeOverride = renderTypeOverride;
                RawRenderQueue = rawRenderQueue;
                OpaqueKeywordEnabled = opaqueKeywordEnabled;
                TransparentKeywordEnabled = transparentKeywordEnabled;
                ShadowCasterEnabled = shadowCasterEnabled;
                MetaEnabled = metaEnabled;
                WasDirty = wasDirty;
            }

            /// <summary>Gets whether a prior RenderType override existed in the raw tag map.</summary>
            public bool HasRenderTypeOverride { get; }

            /// <summary>Gets the prior raw RenderType override value.</summary>
            public string RenderTypeOverride { get; }

            /// <summary>Gets the prior raw material queue override.</summary>
            public int RawRenderQueue { get; }

            /// <summary>Gets whether the Opaque keyword was enabled.</summary>
            public bool OpaqueKeywordEnabled { get; }

            /// <summary>Gets whether the Transparent keyword was enabled.</summary>
            public bool TransparentKeywordEnabled { get; }

            /// <summary>Gets whether ShadowCaster was enabled.</summary>
            public bool ShadowCasterEnabled { get; }

            /// <summary>Gets whether Meta was enabled.</summary>
            public bool MetaEnabled { get; }

            /// <summary>Gets whether the material was dirty before normalization.</summary>
            public bool WasDirty { get; }
        }

        /// <summary>Reads the raw RenderType override presence and value without resolving shader fallback tags.</summary>
        /// <param name="material">The material whose serialized tag map is read.</param>
        /// <param name="renderTypeOverride">Receives the raw override value when one exists.</param>
        /// <returns><see langword="true"/> when the material serializes an explicit RenderType override.</returns>
        private static bool TryGetRawRenderTypeOverride(
            Material material,
            out string renderTypeOverride
        )
        {
            string serializedMaterial = EditorJsonUtility.ToJson(material);
            Match tagMap = Regex.Match(
                serializedMaterial,
                @"""stringTagMap""\s*:\s*\{(?<entries>[^}]*)\}"
            );
            if (!tagMap.Success)
                throw new InvalidOperationException(
                    "The material does not expose a serialized raw RenderType tag map."
                );

            Match renderType = Regex.Match(
                tagMap.Groups["entries"].Value,
                @"""RenderType""\s*:\s*""(?<value>[^""]*)"""
            );
            renderTypeOverride = renderType.Success ? renderType.Groups["value"].Value : null;
            return renderType.Success;
        }

        /// <summary>Reads the serialized raw render queue without resolving the shader-default queue.</summary>
        /// <param name="material">The material whose raw queue is read.</param>
        /// <returns>The serialized raw render queue.</returns>
        private static int GetRawRenderQueue(Material material)
        {
            using (var serializedMaterial = new SerializedObject(material))
            {
                SerializedProperty rawRenderQueue = serializedMaterial.FindProperty(
                    "m_CustomRenderQueue"
                );
                if (rawRenderQueue == null)
                    throw new InvalidOperationException(
                        "The material does not expose a serialized raw render queue."
                    );

                return rawRenderQueue.intValue;
            }
        }
    }
}
