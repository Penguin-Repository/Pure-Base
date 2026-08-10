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
    [string]$ValidationArtifactDirectory,

    [Parameter(Mandatory = $true)]
    [string]$Repository,

    [Parameter(Mandatory = $true)]
    [string]$HeadSha,

    [Parameter(Mandatory = $true)]
    [AllowEmptyString()]
    [string]$HeadBranch,

    [Parameter(Mandatory = $true)]
    [long]$WorkflowRunId,

    [Parameter(Mandatory = $true)]
    [int]$WorkflowRunAttempt
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PureBase.Automation.psm1') -Force

if ($Repository -notmatch '^[^/\s]+/[^/\s]+$') { throw 'repository must be valid owner/name form.' }
if ($HeadSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'head SHA must be valid 40-character hexadecimal.' }
if ([string]::IsNullOrWhiteSpace($HeadBranch) -or $HeadBranch -match '[\x00-\x1F\x7F\r\n]') { throw 'branch must be valid.' }
if ($WorkflowRunId -le 0) { throw 'run ID must be valid and greater than zero.' }
if ($WorkflowRunAttempt -le 0) { throw 'run attempt must be valid and greater than zero.' }

if ([string]::IsNullOrWhiteSpace($PackageRoot)) { throw 'PackageRoot must be valid.' }
if ([string]::IsNullOrWhiteSpace($ValidationArtifactDirectory)) { throw 'ValidationArtifactDirectory must be valid.' }
$packageRootFullPath = [IO.Path]::GetFullPath($PackageRoot)
$validationArtifactRoot = [IO.Path]::GetFullPath($ValidationArtifactDirectory)
if (-not (Test-Path -LiteralPath $packageRootFullPath -PathType Container)) { throw "PackageRoot must be valid: '$PackageRoot'." }
if (-not (Test-Path -LiteralPath $validationArtifactRoot -PathType Container)) { throw "ValidationArtifactDirectory must be valid: '$ValidationArtifactDirectory'." }

$packageJsonPath = Join-Path $packageRootFullPath 'package.json'
if (-not (Test-Path -LiteralPath $packageJsonPath -PathType Leaf)) { throw "package.json is missing from PackageRoot '$PackageRoot'." }
try {
    $package = Get-Content -LiteralPath $packageJsonPath -Raw | ConvertFrom-Json
}
catch {
    throw "package.json must contain valid JSON: $($_.Exception.Message)"
}
$packageNameProperty = if ($null -ne $package) { $package.PSObject.Properties['name'] } else { $null }
$packageName = if ($null -ne $packageNameProperty) { [string]$packageNameProperty.Value } else { '' }
if ($packageName -cne 'jp.penguin.purebase') { throw 'package.json name must be valid jp.penguin.purebase.' }
$versionProperty = if ($null -ne $package) { $package.PSObject.Properties['version'] } else { $null }
$version = if ($null -ne $versionProperty) { [string]$versionProperty.Value } else { '' }
try { [void](ConvertTo-PureBaseSemVer -Value $version) }
catch { throw "package.json version must be valid strict SemVer: '$version'." }

$sourceZips = @(
    Get-ChildItem -LiteralPath $validationArtifactRoot -Filter 'jp.penguin.purebase-*.zip' -File -Recurse |
        Where-Object { $_.Directory.Name -ceq 'archive' }
)
if ($sourceZips.Count -ne 1) {
    throw "Release validation must produce exactly one audited package ZIP below '$validationArtifactRoot/archive'."
}
$sourceZip = $sourceZips[0]
if ($sourceZip.Name -cne "jp.penguin.purebase-$version.zip") { throw "Audited package ZIP '$($sourceZip.Name)' does not match package.json version '$version'." }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = $null
try {
    $archive = [IO.Compression.ZipFile]::OpenRead($sourceZip.FullName)
    $manifestEntries = @($archive.Entries | Where-Object FullName -CEQ 'package.json')
    if ($manifestEntries.Count -ne 1) { throw 'Audited package ZIP must contain exactly one package.json.' }
    $reader = [IO.StreamReader]::new($manifestEntries[0].Open(), [Text.UTF8Encoding]::new($false, $true))
    try {
        $zipPackage = $reader.ReadToEnd() | ConvertFrom-Json
        $zipName = [string]$zipPackage.name
        $zipVersion = [string]$zipPackage.version
    }
    finally { $reader.Dispose() }
    if ($zipName -cne 'jp.penguin.purebase') { throw "Audited package ZIP package.json name '$zipName' does not match 'jp.penguin.purebase'." }
    if ($zipVersion -cne $version) { throw "Audited package ZIP manifest version '$zipVersion' does not match '$version'." }
}
catch { throw "Audited package ZIP is invalid: $($_.Exception.Message)" }
finally { if ($null -ne $archive) { $archive.Dispose() } }

$exportDirectory = Join-Path $validationArtifactRoot 'validated-package'
if (Test-Path -LiteralPath $exportDirectory) { Remove-Item -LiteralPath $exportDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $exportDirectory -Force | Out-Null
$destinationZip = Join-Path $exportDirectory "jp.penguin.purebase-$version.zip"
Copy-Item -LiteralPath $sourceZip.FullName -Destination $destinationZip -Force
$sha256 = (Get-FileHash -LiteralPath $destinationZip -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText(
    $destinationZip + '.sha256',
    $sha256 + "`n",
    [System.Text.ASCIIEncoding]::new()
)

$manifest = [ordered]@{
    schemaVersion      = 1
    repository         = $Repository
    headSha            = $HeadSha
    headBranch         = $HeadBranch
    workflowRunId      = $WorkflowRunId
    workflowRunAttempt = $WorkflowRunAttempt
    version            = $version
    assetName          = [IO.Path]::GetFileName($destinationZip)
    sha256             = $sha256
}
$manifestPath = Join-Path $exportDirectory 'release-validation.json'
$temporaryManifestPath = "$manifestPath.$([guid]::NewGuid().ToString('N')).tmp"
try {
    [IO.File]::WriteAllText(
        $temporaryManifestPath,
        (($manifest | ConvertTo-Json -Compress) + "`n"),
        [Text.UTF8Encoding]::new($false)
    )
    Move-Item -LiteralPath $temporaryManifestPath -Destination $manifestPath -Force -ErrorAction Stop
}
finally {
    if (Test-Path -LiteralPath $temporaryManifestPath) { Remove-Item -LiteralPath $temporaryManifestPath -Force -ErrorAction SilentlyContinue }
}

Write-Output "Validated package ZIP: $destinationZip"
Write-Output "SHA-256: $sha256"

