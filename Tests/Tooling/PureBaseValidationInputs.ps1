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

# Reads fixed validation inputs without network, staging, or filesystem writes.
function Get-PureBaseInputPath {
    param([string]$PackageRoot, [string]$RelativePath)

    $root = [IO.Path]::GetFullPath($PackageRoot)
    $path = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (-not $path.StartsWith($root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Validation input escapes the package root: '$RelativePath'."
    }
    $current = $path
    while (-not [string]::IsNullOrEmpty($current)) {
        if (Test-Path -LiteralPath $current) {
            if (((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Validation input path contains a reparse point: '$current'."
            }
        }
        $current = Split-Path -Parent $current
    }
    return $path
}

function Read-PureBaseInputJson {
    param([string]$PackageRoot, [string]$RelativePath)

    $path = Get-PureBaseInputPath -PackageRoot $PackageRoot -RelativePath $RelativePath
    return Get-Content -LiteralPath $path -Raw -ErrorAction Stop | ConvertFrom-Json -AsHashtable -ErrorAction Stop
}

function Get-PureBaseShaderCorePin {
    param([Parameter(Mandatory)][string]$PackageRoot)

    $package = Read-PureBaseInputJson $PackageRoot 'package.json'
    $toolchain = Read-PureBaseInputJson $PackageRoot 'Tests/Release/ci-toolchain.json'
    $version = [string]$package.vpmDependencies['jp.lilxyzw.shadercore']
    if ($package.name -cne 'jp.penguin.purebase' -or $version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\z') {
        throw 'package.json must require jp.lilxyzw.shadercore at an exact version.'
    }
    $expectedUrl = "https://github.com/lilxyzw/Shader-Core/releases/download/$version/jp.lilxyzw.shadercore-$version.zip"
    if ($toolchain.schemaVersion -ne 1 -or $toolchain.shaderCore.archiveUrl -cne $expectedUrl -or
        $toolchain.shaderCore.archiveSha256 -cnotmatch '\A[0-9a-f]{64}\z') {
        throw 'Shader-Core package/toolchain pin mismatch or invalid schema.'
    }
    if ($toolchain.unityCli.version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?\z' -or
        $toolchain.unityCli.sha256 -cnotmatch '\A[0-9a-f]{64}\z') {
        throw 'Invalid Unity CLI pin.'
    }
    return [pscustomobject]@{
        ShaderCoreVersion = $version
        ShaderCoreUri = [string]$toolchain.shaderCore.archiveUrl
        ShaderCoreSha256 = [string]$toolchain.shaderCore.archiveSha256
        UnityCliVersion = [string]$toolchain.unityCli.version
        UnityCliSha256 = [string]$toolchain.unityCli.sha256
    }
}

function Get-PureBaseUnityInput {
    param([Parameter(Mandatory)][string]$PackageRoot)

    $path = Get-PureBaseInputPath $PackageRoot 'Tests/Release/ConsumerProject/ProjectSettings/ProjectVersion.txt'
    $text = Get-Content -LiteralPath $path -Raw -ErrorAction Stop
    $version = [regex]::Matches($text, '(?m)^m_EditorVersion: ([0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+)\r?$')
    $revision = [regex]::Matches($text, '(?m)^m_EditorVersionWithRevision: ([0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+) \(([0-9a-f]{12})\)\r?$')
    if ($version.Count -ne 1 -or $revision.Count -ne 1 -or $version[0].Groups[1].Value -cne $revision[0].Groups[1].Value) {
        throw 'Invalid or inconsistent Unity ProjectVersion input.'
    }
    return [pscustomobject]@{ Version = $version[0].Groups[1].Value; Revision = $revision[0].Groups[2].Value }
}

function Assert-PureBaseIdentityEntries {
    param($Identity)

    $previous = $null
    $lines = [Text.StringBuilder]::new()
    if (@($Identity.entries).Count -eq 0) { throw 'Shader-Core identity entries are missing.' }
    foreach ($entry in $Identity.entries) {
        $path = [string]$entry.path
        if ([string]::IsNullOrWhiteSpace($path) -or $path -match '[\\:\x00-\x1f]|^/|//|(^|/)\.{1,2}(/|$)|/$' -or
            $entry.sha256 -cnotmatch '\A[0-9a-f]{64}\z') {
            throw 'Unsafe Shader-Core identity entry.'
        }
        if ($null -ne $previous -and [string]::CompareOrdinal($previous, $path) -ge 0) {
            throw 'Shader-Core identity entries must be unique and ordinally sorted.'
        }
        [void]$lines.Append("$path`t$($entry.sha256)`n")
        $previous = $path
    }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($lines.ToString()))).ToLowerInvariant()
    if ($hash -cne $Identity.identitySha256) { throw 'Shader-Core identity aggregate mismatch.' }
}

function Get-PureBaseValidationInputs {
    param([Parameter(Mandatory)][string]$PackageRoot)

    $pin = Get-PureBaseShaderCorePin -PackageRoot $PackageRoot
    $identity = Read-PureBaseInputJson $PackageRoot 'Tests/Release/shader-core.sha256.json'
    if ($identity.schemaVersion -ne 1 -or $identity.packageName -cne 'jp.lilxyzw.shadercore' -or
        $identity.packageVersion -cne $pin.ShaderCoreVersion -or
        [string]::IsNullOrWhiteSpace($identity.scope) -or [string]::IsNullOrWhiteSpace($identity.algorithm)) {
        throw 'Shader-Core package/toolchain/identity mismatch or invalid schema.'
    }
    Assert-PureBaseIdentityEntries -Identity $identity
    $unity = Get-PureBaseUnityInput -PackageRoot $PackageRoot
    $pin | Add-Member -NotePropertyName UnityVersion -NotePropertyValue $unity.Version
    $pin | Add-Member -NotePropertyName UnityRevision -NotePropertyValue $unity.Revision
    $pin | Add-Member -NotePropertyName ShaderCoreIdentity -NotePropertyValue $identity
    return $pin
}

function Get-PureBaseBootstrapPackageProfile {
    param([Parameter(Mandatory)][string]$PackageRoot)

    $packageProfile = Read-PureBaseInputJson $PackageRoot 'Tests/Release/first-bootstrap-package-profile.json'
    $unity = Get-PureBaseUnityInput -PackageRoot $PackageRoot
    if ($packageProfile.unityVersion -cne $unity.Version -or $packageProfile.unityRevision -cne $unity.Revision -or
        $packageProfile.manifestDependencies -isnot [Collections.IDictionary] -or $packageProfile.lockDependencies -isnot [Collections.IDictionary]) {
        throw 'Bootstrap package profile does not match the Unity input.'
    }
    return $packageProfile
}