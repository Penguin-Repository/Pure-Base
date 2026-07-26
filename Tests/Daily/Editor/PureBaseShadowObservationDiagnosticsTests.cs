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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PureBase.Tests.Daily
{
    /// <summary>Emits successful shadow observations so reviewed renderer tolerances remain auditable.</summary>
    public sealed class PureBaseShadowObservationDiagnosticsTests
    {
        /// <summary>Logs the current shadow count, reviewed range, renderer, and active quality profile.</summary>
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

                // This diagnostic intentionally does not widen or rewrite the baseline. It
                // records the current value and the committed range after local and
                // GitHub-hosted projects apply the same reviewed quality configuration.
                Debug.Log(
                    $"Pure-Base Daily shadow observation: changedPixels={observation.shadowChangedPixelCount}, "
                        + $"reviewedRange=[{baseline.shadowChangedPixelCount.minimum}, {baseline.shadowChangedPixelCount.maximum}], "
                        + $"graphicsDevice='{SystemInfo.graphicsDeviceName}', graphicsDeviceType={SystemInfo.graphicsDeviceType}, "
                        + $"qualityLevel={qualityLevel} ('{qualityName}')."
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
