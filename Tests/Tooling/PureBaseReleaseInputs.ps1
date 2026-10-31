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

# Enforces release dependency policy and generates identities from verified archives only.
function Get-PureBaseIdentityFailureMessage {
    param($Expected, $Actual, [string]$FirstDivergence)

    return (@(
            'Local Shader-Core identity does not match shader-core.sha256.json.'
            "Expected aggregate identity SHA-256: $($Expected.identitySha256)."
            "Actual aggregate identity SHA-256: $($Actual.identitySha256)."
            "Expected entry count: $(@($Expected.entries).Count)."
            "Actual entry count: $(@($Actual.entries).Count)."
            $FirstDivergence
            'Regenerate only after intentionally reviewing the dependency source.'
        ) -join ' ')
}

function Assert-PackageMetadata {
    param([string]$PackageRoot, $Contract)

    $package = Read-PureBaseInputJson $PackageRoot 'package.json'
    $pin = Get-PureBaseShaderCorePin -PackageRoot $PackageRoot
    if ($package.name -cne $Contract.packageName) { throw 'package.json name does not match release contract.' }
    $requiredNames = @($Contract.requiredVpmDependencyNames)
    if ($requiredNames.Count -ne 1 -or $requiredNames[0] -cne 'jp.lilxyzw.shadercore') {
        throw 'Release policy must allow only jp.lilxyzw.shadercore.'
    }
    $actualNames = @($package.vpmDependencies.Keys)
    if ($actualNames.Count -ne 1 -or $actualNames[0] -cne $requiredNames[0]) {
        throw 'package.json must contain only the allowed VPM dependency.'
    }
    foreach ($section in @('dependencies', 'vpmDependencies')) {
        if (-not $package.Contains($section)) { continue }
        foreach ($name in $package[$section].Keys) {
            if ($name -notin $requiredNames -or $name -in $Contract.forbiddenDependencyNames) {
                throw "package.json contains forbidden dependency '$name'."
            }
            if ($package[$section][$name] -cne $pin.ShaderCoreVersion) { throw 'Dependency package/lock version mismatch.' }
        }
    }
}

function Write-ShaderCoreIdentityManifest {
    param([string]$ShaderCoreRoot, [string]$ManifestPath, [string]$ExpectedVersion)

    $identity = Get-VerifiedShaderCorePackageIdentity -ShaderCoreRoot $ShaderCoreRoot -ExpectedVersion $ExpectedVersion
    $manifest = Get-RecursiveIdentityManifest -ShaderCoreRoot $ShaderCoreRoot -ShaderCorePackageIdentity $identity
    $temporaryPath = $ManifestPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $temporaryPath -Encoding utf8 -ErrorAction Stop
        [IO.File]::Move($temporaryPath, $ManifestPath, $true)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
    }
}

function Write-PureBaseShaderCoreArchiveManifest {
    param([string]$PackageRoot, [string]$ArchivePath)

    if ([string]::IsNullOrWhiteSpace($ArchivePath)) { throw '-WriteShaderCoreManifest requires -ShaderCoreArchivePath.' }
    $pin = Get-PureBaseShaderCorePin -PackageRoot $PackageRoot
    $manifestPath = Get-PureBaseInputPath $PackageRoot 'Tests/Release/shader-core.sha256.json'
    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('PureBaseIdentity-' + [guid]::NewGuid().ToString('N'))
    try {
        New-Item -ItemType Directory -Path $temporaryRoot -ErrorAction Stop | Out-Null
        & (Join-Path $PackageRoot '.github/scripts/Install-VerifiedShaderCoreRelease.ps1') -ProjectRoot $temporaryRoot `
            -Uri $pin.ShaderCoreUri -ExpectedSha256 $pin.ShaderCoreSha256 -ExpectedVersion $pin.ShaderCoreVersion `
            -ArchivePath $ArchivePath -TemporaryRoot $temporaryRoot
        $tree = Join-Path $temporaryRoot 'Packages/jp.lilxyzw.shadercore'
        Write-ShaderCoreIdentityManifest -ShaderCoreRoot $tree -ManifestPath $manifestPath -ExpectedVersion $pin.ShaderCoreVersion
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
    }
}