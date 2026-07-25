# Copyright 2026 Penguin
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#     http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

Describe 'Pure-Base CI Unity project generation' {
    BeforeAll {
        function Assert-CiProjectHarness {
            param(
                [Parameter(Mandatory = $true)][bool]$Condition,
                [Parameter(Mandatory = $true)][string]$Message
            )

            if (-not $Condition) {
                throw $Message
            }
        }

        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $projectBuilder = Join-Path $repositoryRoot '.github/scripts/New-PureBaseCiProject.ps1'
    }

    BeforeEach {
        $projectRoot = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $pureBaseRoot = Join-Path $projectRoot 'Packages/jp.penguin.purebase'
        $shaderCoreRoot = Join-Path $projectRoot 'Packages/jp.lilxyzw.shadercore'
        $consumerSettings = Join-Path $pureBaseRoot 'Tests/Release/ConsumerProject/ProjectSettings'
        New-Item -ItemType Directory -Path $pureBaseRoot,$shaderCoreRoot,$consumerSettings -Force | Out-Null
        [IO.File]::WriteAllText(
            (Join-Path $pureBaseRoot 'package.json'),
            '{"name":"jp.penguin.purebase","version":"0.1.0"}',
            [Text.UTF8Encoding]::new($false)
        )
        [IO.File]::WriteAllText(
            (Join-Path $shaderCoreRoot 'package.json'),
            '{"name":"jp.lilxyzw.shadercore","version":"0.1.9"}',
            [Text.UTF8Encoding]::new($false)
        )
        [IO.File]::WriteAllText(
            (Join-Path $consumerSettings 'ProjectVersion.txt'),
            "m_EditorVersion: 2022.3.22f1`nm_EditorVersionWithRevision: 2022.3.22f1 (887be4894c44)`n",
            [Text.UTF8Encoding]::new($false)
        )
        $qualitySettingsFixture = @'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!47 &1
QualitySettings:
  m_ObjectHideFlags: 0
  serializedVersion: 5
  m_CurrentQuality: 2
  m_QualitySettings:
  - serializedVersion: 3
    name: VRC High
    pixelLightCount: 8
    shadows: 2
    shadowResolution: 3
    shadowProjection: 1
    shadowCascades: 4
    shadowDistance: 150
    shadowNearPlaneOffset: 2
    antiAliasing: 4
'@
        [IO.File]::WriteAllText(
            (Join-Path $consumerSettings 'QualitySettings.asset'),
            $qualitySettingsFixture + "`n",
            [Text.UTF8Encoding]::new($false)
        )
    }

    It 'pins Unity, test framework, owner scene, and reviewed quality settings' {
        & $projectBuilder -ProjectRoot $projectRoot

        $projectVersion = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Raw
        Assert-CiProjectHarness -Condition ($projectVersion -match 'm_EditorVersion: 2022\.3\.22f1') -Message 'Generated CI ProjectVersion.txt does not pin Unity 2022.3.22f1.'
        Assert-CiProjectHarness -Condition ($projectVersion -match '887be4894c44') -Message 'Generated CI ProjectVersion.txt does not pin the required Unity revision.'

        $manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
        Assert-CiProjectHarness -Condition ([string]$manifest.dependencies.'com.unity.test-framework' -eq '1.1.33') -Message 'Generated CI manifest does not pin the Unity Test Framework.'
        Assert-CiProjectHarness -Condition (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'Assets/Editor/PureBaseCiBootstrap.cs'))) -Message 'Generated CI project must not create transient bootstrap code.'

        $ownerScenePath = Join-Path $projectRoot 'Assets/Pure-Base.unity'
        Assert-CiProjectHarness -Condition (Test-Path -LiteralPath $ownerScenePath -PathType Leaf) -Message 'Generated CI project must include the persisted owner scene required by Daily restoration tests.'
        $ownerScene = Get-Content -LiteralPath $ownerScenePath -Raw
        Assert-CiProjectHarness -Condition ($ownerScene -match 'SceneRoots:') -Message 'Generated owner scene is not a serialized Unity scene.'
        Assert-CiProjectHarness -Condition ($ownerScene -match 'm_Roots: \[\]') -Message 'Generated owner scene must remain empty.'

        $qualitySettingsPath = Join-Path $projectRoot 'ProjectSettings/QualitySettings.asset'
        Assert-CiProjectHarness -Condition (Test-Path -LiteralPath $qualitySettingsPath -PathType Leaf) -Message 'Generated CI project is missing the reviewed QualitySettings asset.'
        $qualitySettings = Get-Content -LiteralPath $qualitySettingsPath -Raw
        Assert-CiProjectHarness -Condition ($qualitySettings -match 'm_CurrentQuality: 2') -Message 'Generated CI project must select the reviewed VRC High quality level.'
        Assert-CiProjectHarness -Condition ($qualitySettings -match 'name: VRC High') -Message 'Generated CI project is missing the VRC High profile.'
        Assert-CiProjectHarness -Condition ($qualitySettings -match 'shadowResolution: 3') -Message 'Generated CI project must preserve the reviewed shadow resolution.'
        Assert-CiProjectHarness -Condition ($qualitySettings -match 'shadowCascades: 4') -Message 'Generated CI project must preserve four shadow cascades.'
        Assert-CiProjectHarness -Condition ($qualitySettings -match 'shadowDistance: 150') -Message 'Generated CI project must preserve the reviewed shadow distance.'
        Assert-CiProjectHarness -Condition ($qualitySettings -match 'antiAliasing: 4') -Message 'Generated CI project must preserve 4x MSAA.'
    }

    It 'rejects an unexpected Shader-Core version' {
        [IO.File]::WriteAllText(
            (Join-Path $shaderCoreRoot 'package.json'),
            '{"name":"jp.lilxyzw.shadercore","version":"0.1.8"}',
            [Text.UTF8Encoding]::new($false)
        )

        $failure = $null
        try { & $projectBuilder -ProjectRoot $projectRoot }
        catch { $failure = $_ }
        Assert-CiProjectHarness -Condition ($null -ne $failure -and $failure.Exception.Message -like '*exactly 0.1.9*') -Message 'The CI project builder accepted an unexpected Shader-Core version.'
    }
}
