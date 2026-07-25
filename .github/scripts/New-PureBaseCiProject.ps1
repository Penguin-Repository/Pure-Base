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

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRootFullPath = [System.IO.Path]::GetFullPath($ProjectRoot)
$packageRoot = Join-Path $projectRootFullPath 'Packages/jp.penguin.purebase'
$shaderCoreRoot = Join-Path $projectRootFullPath 'Packages/jp.lilxyzw.shadercore'

foreach ($requiredPath in @($packageRoot, $shaderCoreRoot)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Container)) {
        throw "Required package checkout is missing: '$requiredPath'."
    }
}

$packageJson = Get-Content -LiteralPath (Join-Path $packageRoot 'package.json') -Raw | ConvertFrom-Json
if ([string]$packageJson.name -ne 'jp.penguin.purebase') {
    throw "Unexpected Pure-Base package identity '$($packageJson.name)'."
}

$shaderCoreJson = Get-Content -LiteralPath (Join-Path $shaderCoreRoot 'package.json') -Raw | ConvertFrom-Json
if ([string]$shaderCoreJson.name -ne 'jp.lilxyzw.shadercore' -or [string]$shaderCoreJson.version -ne '0.1.9') {
    throw "The CI workspace requires jp.lilxyzw.shadercore exactly 0.1.9."
}

$assetsRoot = Join-Path $projectRootFullPath 'Assets'
$projectSettingsRoot = Join-Path $projectRootFullPath 'ProjectSettings'
$packagesRoot = Join-Path $projectRootFullPath 'Packages'
foreach ($directory in @($assetsRoot, $projectSettingsRoot, $packagesRoot)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$projectVersionSource = Join-Path $packageRoot 'Tests/Release/ConsumerProject/ProjectSettings/ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $projectVersionSource -PathType Leaf)) {
    throw "Pinned Unity ProjectVersion source is missing: '$projectVersionSource'."
}
Copy-Item -LiteralPath $projectVersionSource -Destination (Join-Path $projectSettingsRoot 'ProjectVersion.txt') -Force

$manifest = [ordered]@{
    dependencies = [ordered]@{
        'com.unity.test-framework' = '1.1.33'
    }
}
$manifestText = ($manifest | ConvertTo-Json -Depth 4) + "`n"
[System.IO.File]::WriteAllText(
    (Join-Path $packagesRoot 'manifest.json'),
    $manifestText,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Output "Prepared Pure-Base CI Unity project: $projectRootFullPath"
Write-Output "Pure-Base package version: $($packageJson.version)"
Write-Output "Shader-Core package version: $($shaderCoreJson.version)"
