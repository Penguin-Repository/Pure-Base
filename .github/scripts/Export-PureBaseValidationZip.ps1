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
    [string]$PackageRoot,

    [Parameter(Mandatory = $true)]
    [string]$ValidationArtifactDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PureBase.Automation.psm1') -Force

$package = Get-Content -LiteralPath (Join-Path $PackageRoot 'package.json') -Raw | ConvertFrom-Json
$version = [string]$package.version
[void](ConvertTo-PureBaseStableVersion -Value $version)

$sourceZip = @(
    Get-ChildItem -LiteralPath $ValidationArtifactDirectory -Filter 'jp.penguin.purebase-*.zip' -File -Recurse |
        Where-Object { $_.DirectoryName -match '[\\/]archive$' } |
        Sort-Object LastWriteTimeUtc -Descending
) | Select-Object -First 1
if ($null -eq $sourceZip) {
    throw "Release validation did not produce an audited package ZIP below '$ValidationArtifactDirectory'."
}

$exportDirectory = Join-Path $ValidationArtifactDirectory 'validated-package'
New-Item -ItemType Directory -Path $exportDirectory -Force | Out-Null
$destinationZip = Join-Path $exportDirectory "jp.penguin.purebase-$version.zip"
Copy-Item -LiteralPath $sourceZip.FullName -Destination $destinationZip -Force
$sha256 = (Get-FileHash -LiteralPath $destinationZip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(
    $destinationZip + '.sha256',
    $sha256 + "`n",
    [System.Text.ASCIIEncoding]::new()
)

Write-Output "Validated package ZIP: $destinationZip"
Write-Output "SHA-256: $sha256"
