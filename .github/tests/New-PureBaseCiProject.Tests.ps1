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

function Assert-CiProjectHarness {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

Describe 'Pure-Base CI Unity project generation' {
    BeforeAll {
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
    }

    It 'pins Unity, Linear color space, text serialization, and test framework version' {
        & $projectBuilder -ProjectRoot $projectRoot

        $projectVersion = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Raw
        Assert-CiProjectHarness -Condition ($projectVersion -match 'm_EditorVersion: 2022\.3\.22f1') -Message 'Generated CI ProjectVersion.txt does not pin Unity 2022.3.22f1.'
        Assert-CiProjectHarness -Condition ($projectVersion -match '887be4894c44') -Message 'Generated CI ProjectVersion.txt does not pin the required Unity revision.'

        $manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
        Assert-CiProjectHarness -Condition ([string]$manifest.dependencies.'com.unity.test-framework' -eq '1.1.33') -Message 'Generated CI manifest does not pin the Unity Test Framework.'

        $bootstrap = Get-Content -LiteralPath (Join-Path $projectRoot 'Assets/Editor/PureBaseCiBootstrap.cs') -Raw
        Assert-CiProjectHarness -Condition ($bootstrap -match 'Application\.unityVersion != "2022\.3\.22f1"') -Message 'Generated CI bootstrap does not pin the Unity version.'
        Assert-CiProjectHarness -Condition ($bootstrap -match 'PlayerSettings\.colorSpace = ColorSpace\.Linear;') -Message 'Generated CI bootstrap does not require Linear color space.'
        Assert-CiProjectHarness -Condition ($bootstrap -match 'EditorSettings\.serializationMode = SerializationMode\.ForceText;') -Message 'Generated CI bootstrap does not require text serialization.'
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
