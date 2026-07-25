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

using System;
using UnityEditor;
using UnityEngine;

namespace PureBase.Tests.Regeneration
{
    /// <summary>Initializes the generated CI project and fixed Shader-Core test state in one batch-mode invocation.</summary>
    public static class PureBaseCiInitializer
    {
        /// <summary>Provides the public no-argument entry point used by Unity batch mode.</summary>
        public static void InitializeForBatchMode()
        {
            if (Application.unityVersion != "2022.3.22f1")
            {
                throw new InvalidOperationException(
                    $"Pure-Base CI requires Unity 2022.3.22f1, received {Application.unityVersion}."
                );
            }

            ShaderCoreTestStateInitializer.Initialize();
            EditorSettings.serializationMode = SerializationMode.ForceText;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
        }
    }
}
