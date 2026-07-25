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
    /// <summary>Configures the generated CI project from a normal Editor assembly.</summary>
    [InitializeOnLoad]
    public static class PureBaseCiProjectInitializer
    {
        private const string ExitPendingKey = "PureBase.CiProjectInitializer.ExitPending";

        static PureBaseCiProjectInitializer()
        {
            if (Application.isBatchMode && SessionState.GetBool(ExitPendingKey, false))
                SubscribeForStableExit();
        }

        /// <summary>Provides the public no-argument entry point used by Unity batch mode.</summary>
        public static void InitializeForBatchMode()
        {
            SessionState.SetBool(ExitPendingKey, true);

            try
            {
                if (Application.unityVersion != "2022.3.22f1")
                {
                    throw new InvalidOperationException(
                        $"Pure-Base CI requires Unity 2022.3.22f1, received {Application.unityVersion}."
                    );
                }

                EditorSettings.serializationMode = SerializationMode.ForceText;
                if (PlayerSettings.colorSpace != ColorSpace.Linear)
                    PlayerSettings.colorSpace = ColorSpace.Linear;

                AssetDatabase.SaveAssets();
                SubscribeForStableExit();
            }
            catch (Exception exception)
            {
                SessionState.EraseBool(ExitPendingKey);
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }

        private static void SubscribeForStableExit()
        {
            EditorApplication.update -= ExitWhenStable;
            EditorApplication.update += ExitWhenStable;
        }

        private static void ExitWhenStable()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= ExitWhenStable;

            try
            {
                AssetDatabase.SaveAssets();
                SessionState.EraseBool(ExitPendingKey);
                Debug.Log("Pure-Base CI project configuration completed after imports stabilized.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                SessionState.EraseBool(ExitPendingKey);
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
