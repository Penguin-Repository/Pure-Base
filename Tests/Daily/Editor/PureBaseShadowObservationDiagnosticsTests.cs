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

using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    /// <summary>Emits successful rendering observations so reviewed renderer tolerances remain auditable.</summary>
    public sealed class PureBaseShadowObservationDiagnosticsTests
    {
        /// <summary>Logs one stable record containing the complete current rendering observation and its reviewed ranges.</summary>
        [Test]
        public void LogReviewedShadowObservationEnvironment()
        {
            Scene originalActiveScene = SceneManager.GetActiveScene();
            Scene validationScene = SceneManager.GetSceneByPath(
                PureBaseValidationSceneRegressionTests.ScenePath
            );
            bool sceneWasLoaded = validationScene.isLoaded;

            try
            {
                PureBaseValidationSceneRegressionTests.ValidateRuntimeConfiguration();
                if (!sceneWasLoaded)
                {
                    validationScene = EditorSceneManager.OpenScene(
                        PureBaseValidationSceneRegressionTests.ScenePath,
                        OpenSceneMode.Additive
                    );
                }

                Assert.That(
                    SceneManager.SetActiveScene(validationScene),
                    Is.True,
                    "The canonical validation scene could not become active for diagnostics."
                );

                SceneRegressionBaseline baseline =
                    PureBaseValidationSceneRegressionTests.LoadBaseline();
                SceneRegressionObservation observation =
                    PureBaseValidationSceneRegressionTests.CaptureObservation(validationScene);
                int qualityLevel = QualitySettings.GetQualityLevel();
                string qualityName =
                    qualityLevel >= 0 && qualityLevel < QualitySettings.names.Length
                        ? QualitySettings.names[qualityLevel]
                        : "<invalid>";

                MetaAlbedoObservation[] meta = observation.metaAlbedo;

                // This diagnostic intentionally does not widen or rewrite the baseline.
                Debug.Log(
                    $"Pure-Base Daily observation: unityVersion='{Application.unityVersion}', renderPipeline='BuiltIn', "
                        + $"graphicsApi='{SystemInfo.graphicsDeviceType}', colorSpace='{PlayerSettings.colorSpace}', "
                        + $"graphicsDeviceName='{SystemInfo.graphicsDeviceName}', graphicsDeviceVersion='{SystemInfo.graphicsDeviceVersion}', "
                        + $"qualityLevel={qualityLevel}, qualityName='{qualityName}', "
                        + $"metaUnlit={meta[0].meanLuminance}, metaUnlitRange=[{baseline.metaAlbedo[0].meanLuminance.minimum}, {baseline.metaAlbedo[0].meanLuminance.maximum}], "
                        + $"metaToon={meta[1].meanLuminance}, metaToonRange=[{baseline.metaAlbedo[1].meanLuminance.minimum}, {baseline.metaAlbedo[1].meanLuminance.maximum}], "
                        + $"metaPbr={meta[2].meanLuminance}, metaPbrRange=[{baseline.metaAlbedo[2].meanLuminance.minimum}, {baseline.metaAlbedo[2].meanLuminance.maximum}], "
                        + $"metaHybrid={meta[3].meanLuminance}, metaHybridRange=[{baseline.metaAlbedo[3].meanLuminance.minimum}, {baseline.metaAlbedo[3].meanLuminance.maximum}], "
                        + $"shadowChangedPixels={observation.shadowChangedPixelCount}, shadowRange=[{baseline.shadowChangedPixelCount.minimum}, {baseline.shadowChangedPixelCount.maximum}]."
                );

                Assert.That(
                    observation.shadowChangedPixelCount,
                    Is.InRange(
                        baseline.shadowChangedPixelCount.minimum,
                        baseline.shadowChangedPixelCount.maximum
                    )
                );
            }
            finally
            {
                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                    SceneManager.SetActiveScene(originalActiveScene);
                if (!sceneWasLoaded && validationScene.IsValid() && validationScene.isLoaded)
                    EditorSceneManager.CloseScene(validationScene, true);
            }
        }
    }
}
