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

BeforeAll {
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $projectBuilder = Join-Path $repositoryRoot '.github/scripts/New-PureBaseCiProject.ps1'
}

Describe 'Pure-Base CI Unity project generation' {
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
            '{"name":"jp.lilxyzw.shadercore","version":"0.1.5"}',
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
        $projectVersion | Should -Match 'm_EditorVersion: 2022\.3\.22f1'
        $projectVersion | Should -Match '887be4894c44'

        $manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
        [string]$manifest.dependencies.'com.unity.test-framework' | Should -Be '1.1.33'

        $bootstrap = Get-Content -LiteralPath (Join-Path $projectRoot 'Assets/Editor/PureBaseCiBootstrap.cs') -Raw
        $bootstrap | Should -Match 'Application\.unityVersion != "2022\.3\.22f1"'
        $bootstrap | Should -Match 'PlayerSettings\.colorSpace = ColorSpace\.Linear;'
        $bootstrap | Should -Match 'EditorSettings\.serializationMode = SerializationMode\.ForceText;'
    }

    It 'rejects an unexpected Shader-Core version' {
        [IO.File]::WriteAllText(
            (Join-Path $shaderCoreRoot 'package.json'),
            '{"name":"jp.lilxyzw.shadercore","version":"0.1.6"}',
            [Text.UTF8Encoding]::new($false)
        )

        { & $projectBuilder -ProjectRoot $projectRoot } | Should -Throw '*exactly 0.1.5*'
    }
}
