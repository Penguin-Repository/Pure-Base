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
        private const int ExpectedQualityLevel = 2;
        private const string ExpectedQualityName = "VRC High";

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

                ValidateQualitySettingsSnapshot();
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

        /// <summary>
        /// Ensures CI loaded the reviewed QualitySettings snapshot captured from a local
        /// VRChat SDK project instead of Unity's fresh-project defaults. The generated CI
        /// project does not install or initialize the VRChat SDK, so this validation proves
        /// only the captured Unity quality values, not full VRChat SDK environment parity.
        /// </summary>
        private static void ValidateQualitySettingsSnapshot()
        {
            int qualityLevel = QualitySettings.GetQualityLevel();
            string[] qualityNames = QualitySettings.names;
            string qualityName =
                qualityLevel >= 0 && qualityLevel < qualityNames.Length
                    ? qualityNames[qualityLevel]
                    : "<out-of-range>";

            if (qualityLevel != ExpectedQualityLevel || qualityName != ExpectedQualityName)
            {
                throw new InvalidOperationException(
                    $"Pure-Base CI requires VRChat-project QualitySettings snapshot level {ExpectedQualityLevel} '{ExpectedQualityName}', received {qualityLevel} '{qualityName}'."
                );
            }

            if (
                QualitySettings.pixelLightCount != 8
                || QualitySettings.shadows != ShadowQuality.All
                || QualitySettings.shadowResolution != ShadowResolution.VeryHigh
                || QualitySettings.shadowProjection != ShadowProjection.StableFit
                || QualitySettings.shadowCascades != 4
                || !Mathf.Approximately(QualitySettings.shadowDistance, 150.0f)
                || !Mathf.Approximately(QualitySettings.shadowNearPlaneOffset, 2.0f)
                || QualitySettings.antiAliasing != 4
            )
            {
                throw new InvalidOperationException(
                    "Pure-Base CI did not load the reviewed VRChat-project VRC High shadow and MSAA snapshot."
                );
            }

            Debug.Log(
                "Pure-Base CI VRChat-project QualitySettings snapshot: "
                    + "vrchatSdkInstalled=false "
                    + $"level={qualityLevel} name={qualityName} "
                    + $"pixelLights={QualitySettings.pixelLightCount} "
                    + $"shadows={QualitySettings.shadows} "
                    + $"shadowResolution={QualitySettings.shadowResolution} "
                    + $"shadowProjection={QualitySettings.shadowProjection} "
                    + $"shadowCascades={QualitySettings.shadowCascades} "
                    + $"shadowDistance={QualitySettings.shadowDistance} "
                    + $"shadowNearPlaneOffset={QualitySettings.shadowNearPlaneOffset} "
                    + $"antiAliasing={QualitySettings.antiAliasing}"
            );
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
