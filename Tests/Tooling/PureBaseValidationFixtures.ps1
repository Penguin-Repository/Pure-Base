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

# Supplies explicit synthetic validation inputs; fixture pins never follow the production package version.
function Initialize-PureBaseValidationFixture {
    param([string]$PackageRoot, [string]$Version = '0.3.0')

    $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    foreach ($relativePath in @(
        'Tests/Tooling/PureBaseValidationInputs.ps1',
        'Tests/Tooling/PureBaseReleaseInputs.ps1',
        '.github/scripts/Install-VerifiedShaderCoreRelease.ps1'
    )) {
        $target = Join-Path $PackageRoot $relativePath
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $sourceRoot $relativePath) -Destination $target -Force
    }
    $projectVersion = Join-Path $PackageRoot 'Tests/Release/ConsumerProject/ProjectSettings/ProjectVersion.txt'
    New-Item -ItemType Directory -Path (Split-Path -Parent $projectVersion) -Force | Out-Null
    [IO.File]::WriteAllText($projectVersion, "m_EditorVersion: 2022.3.28f1`nm_EditorVersionWithRevision: 2022.3.28f1 (6bae5ce6b222)`n")
    $toolchain = @{
        schemaVersion = 1
        shaderCore = @{
            archiveUrl = "https://github.com/lilxyzw/Shader-Core/releases/download/$Version/jp.lilxyzw.shadercore-$Version.zip"
            archiveSha256 = '0' * 64
        }
        unityCli = @{ version = '1.0.0-beta.3'; sha256 = 'f' * 64 }
    }
    $toolchain | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PackageRoot 'Tests/Release/ci-toolchain.json') -Encoding utf8
    $entryHash = 'a' * 64
    $aggregate = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("fixture.txt`t$entryHash`n"))).ToLowerInvariant()
    @{
        schemaVersion = 1; packageName = 'jp.lilxyzw.shadercore'; packageVersion = $Version
        scope = 'Synthetic fixture'; algorithm = 'SHA-256 fixture'; identitySha256 = $aggregate
        entries = @(@{ path = 'fixture.txt'; sha256 = $entryHash })
    } | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PackageRoot 'Tests/Release/shader-core.sha256.json') -Encoding utf8
}

function New-PureBaseFixtureArchive {
    param([string]$PackageRoot, [string]$ShaderCoreRoot, [string]$ArchivePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $ArchivePath) { Remove-Item -LiteralPath $ArchivePath -Force }
    [IO.Compression.ZipFile]::CreateFromDirectory($ShaderCoreRoot, $ArchivePath)
    $pinPath = Join-Path $PackageRoot 'Tests/Release/ci-toolchain.json'
    $toolchain = Get-Content -LiteralPath $pinPath -Raw | ConvertFrom-Json
    $toolchain.shaderCore.archiveSha256 = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $toolchain | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $pinPath -Encoding utf8
}