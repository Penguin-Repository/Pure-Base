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

# Resolves the pinned Editor without starting it or accepting ambiguous roots.
function Get-UnityEditorCachePaths {
    param([string[]]$Roots, [string]$Version)

    if ($Version -cnotmatch '\A[0-9]+\.[0-9]+\.[0-9]+[abfp][0-9]+\z' -or $Roots.Count -eq 0) {
        throw 'Invalid Unity version or empty install roots.'
    }
    $paths = @()
    foreach ($root in $Roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or $root -match '[\r\n*?\[\]]' -or
            -not [IO.Path]::IsPathFullyQualified($root)) { throw "Invalid install root: '$root'." }
        $path = Join-Path ([IO.Path]::GetFullPath($root)) $Version
        $current = $path
        while ($current) {
            if ((Test-Path -LiteralPath $current) -and
                ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Install path contains a reparse point: '$current'."
            }
            $current = Split-Path -Parent $current
        }
        if ($paths -contains $path) { throw "Duplicate install root: '$root'." }
        $paths += $path
    }
    return $paths
}

# Invokes the verified executable and preserves its native exit status.
function Invoke-PinnedUnityInstall {
    param([string]$CliPath, [string]$Version)

    '' | & $CliPath install $Version -y --accept-eula --non-interactive | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Unity install failed with exit code $LASTEXITCODE." }
}

# Installs on an exact cache miss and returns exactly one matching executable.
function Resolve-InstalledUnityEditor {
    param([string]$CliPath, [string[]]$Roots, [string]$Version, [string]$Revision, [string]$CacheHit)

    if ($Revision -cnotmatch '\A[0-9a-f]{12}\z' -or $CacheHit -cnotin @('true', 'false', '')) {
        throw 'Invalid Unity revision or cache status.'
    }
    $paths = @(Get-UnityEditorCachePaths -Roots $Roots -Version $Version)
    if ($CacheHit -cne 'true') {
        Invoke-PinnedUnityInstall -CliPath $CliPath -Version $Version
    }
    $paths = @(Get-UnityEditorCachePaths -Roots $Roots -Version $Version)
    $candidates = @()
    foreach ($path in $paths) {
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { continue }
        $editor = Join-Path $path 'Editor/Unity.exe'
        if (Test-Path -LiteralPath $editor -PathType Leaf) {
            if ((Get-Item -LiteralPath (Split-Path -Parent $editor) -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Editor directory is a reparse point: '$editor'."
            }
            $item = Get-Item -LiteralPath $editor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
                $item.VersionInfo.ProductVersion -cne "${Version}_${Revision}") {
                throw "Invalid Unity Editor identity: '$editor'."
            }
            $candidates += $item.FullName
        }
    }
    if ($candidates.Count -ne 1) { throw "Expected exactly one Unity Editor; found $($candidates.Count)." }
    return $candidates[0]
}