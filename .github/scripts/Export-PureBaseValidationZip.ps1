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
[void](ConvertTo-PureBaseSemVer -Value $version)

$sourceZips = @(
    Get-ChildItem -LiteralPath $ValidationArtifactDirectory -Filter 'jp.penguin.purebase-*.zip' -File -Recurse |
    Where-Object { $_.DirectoryName -match '[\\/]archive$' }
)
if ($sourceZips.Count -ne 1) {
    throw "Release validation must produce exactly one audited package ZIP below '$ValidationArtifactDirectory'."
}
$sourceZip = $sourceZips[0]
if ($sourceZip.Name -cne "jp.penguin.purebase-$version.zip") { throw "Audited package ZIP '$($sourceZip.Name)' does not match package.json version '$version'." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($sourceZip.FullName)
try {
    $manifestEntries = @($archive.Entries | Where-Object FullName -ceq 'package.json')
    if ($manifestEntries.Count -ne 1) { throw 'Audited package ZIP must contain exactly one package.json.' }
    $reader = [IO.StreamReader]::new($manifestEntries[0].Open(), [Text.UTF8Encoding]::new($false, $true))
    try { $zipVersion = [string](($reader.ReadToEnd() | ConvertFrom-Json).version) }
    finally { $reader.Dispose() }
    if ($zipVersion -cne $version) { throw "Audited package ZIP manifest version '$zipVersion' does not match '$version'." }
}
finally { $archive.Dispose() }

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
