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

# Installs a checksum-verified Shader-Core release archive into a CI project.
[CmdletBinding()]
param(
    [string]$ProjectRoot,

    [ValidatePattern('^https://')]
    [string]$Uri = 'https://github.com/lilxyzw/Shader-Core/releases/download/0.1.9/jp.lilxyzw.shadercore-0.1.9.zip',

    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$ExpectedSha256 = 'fe303273fd653a44d2dc1b746cec587c07fcec3e2777409549b71a2ed742f5ed',

    [string]$TemporaryRoot = [IO.Path]::GetTempPath()
)

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Description
    )

    if (Test-Path -LiteralPath $Path) {
        $attributes = (Get-Item -LiteralPath $Path -Force -ErrorAction Stop).Attributes
        if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "$Description must not be a reparse point: '$Path'."
        }
    }
}

function Get-SafeArchiveRelativePath {
    param(
        [Parameter(Mandatory)]
        [IO.Compression.ZipArchiveEntry]$Entry
    )

    $entryPath = $Entry.FullName
    if ([string]::IsNullOrWhiteSpace($entryPath)) {
        throw 'The release archive contains an empty entry path.'
    }

    if ($entryPath.Contains('\') -or
        $entryPath -match '^(?:/|[A-Za-z]:)' -or
        $entryPath -match '(^|/)\.\.?($|/)' -or
        $entryPath -match '[:\x00]') {
        throw "The release archive contains an unsafe entry path: '$entryPath'."
    }

    $externalAttributes = [uint32]$Entry.ExternalAttributes
    $windowsFileAttributes = $externalAttributes -band [uint32][IO.FileAttributes]::ReparsePoint
    if ($windowsFileAttributes -ne 0) {
        throw "The release archive contains a reparse-point entry: '$entryPath'."
    }

    $unixFileType = (($externalAttributes -shr 16) -band 0xF000)
    if ($unixFileType -eq 0xA000) {
        throw "The release archive contains a symbolic-link entry: '$entryPath'."
    }

    if ($entryPath.EndsWith('/')) {
        return $null
    }

    return $entryPath
}

function Assert-ExtractedTreeIsSafe {
    param(
        [Parameter(Mandatory)]
        [string]$RootPath
    )

    foreach ($item in Get-ChildItem -LiteralPath $RootPath -Force -Recurse -ErrorAction Stop) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "The extracted release archive contains a reparse point: '$($item.FullName)'."
        }
    }
}

function Assert-ShaderCorePackageMetadata {
    param(
        [Parameter(Mandatory)]
        [string]$PackageRoot
    )

    $packageJsonPath = Join-Path $PackageRoot 'package.json'
    if (-not (Test-Path -LiteralPath $packageJsonPath -PathType Leaf)) {
        throw "The extracted release archive does not contain '$packageJsonPath'."
    }

    try {
        $metadata = Get-Content -LiteralPath $packageJsonPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "The extracted Shader-Core package metadata is invalid: $($_.Exception.Message)"
    }

    if ($metadata.name -ne 'jp.lilxyzw.shadercore' -or $metadata.version -ne '0.1.9') {
        throw "The extracted package metadata must identify jp.lilxyzw.shadercore 0.1.9; found '$($metadata.name)' '$($metadata.version)'."
    }
}

function Install-VerifiedShaderCoreRelease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$ProjectRoot,

        [ValidatePattern('^https://')]
        [string]$Uri = 'https://github.com/lilxyzw/Shader-Core/releases/download/0.1.9/jp.lilxyzw.shadercore-0.1.9.zip',

        [ValidatePattern('^[0-9a-f]{64}$')]
        [string]$ExpectedSha256 = 'fe303273fd653a44d2dc1b746cec587c07fcec3e2777409549b71a2ed742f5ed',

        [ValidateNotNullOrEmpty()]
        [string]$TemporaryRoot = [IO.Path]::GetTempPath()
    )

    $projectRootPath = [IO.Path]::GetFullPath($ProjectRoot)
    if (-not (Test-Path -LiteralPath $projectRootPath -PathType Container)) {
        throw "The CI project root does not exist: '$projectRootPath'."
    }
    Assert-NotReparsePoint -Path $projectRootPath -Description 'The CI project root'

    $packagesPath = [IO.Path]::GetFullPath((Join-Path $projectRootPath 'Packages'))
    $targetPath = [IO.Path]::GetFullPath((Join-Path $packagesPath 'jp.lilxyzw.shadercore'))
    $targetPrefix = $packagesPath + [IO.Path]::DirectorySeparatorChar
    if (-not $targetPath.StartsWith($targetPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The Shader-Core target must remain inside the CI project Packages directory: '$targetPath'."
    }

    New-Item -ItemType Directory -Path $packagesPath -Force -ErrorAction Stop | Out-Null
    Assert-NotReparsePoint -Path $packagesPath -Description 'The CI project Packages directory'
    Assert-NotReparsePoint -Path $targetPath -Description 'The existing Shader-Core target'

    $temporaryRootPath = [IO.Path]::GetFullPath($TemporaryRoot)
    New-Item -ItemType Directory -Path $temporaryRootPath -Force -ErrorAction Stop | Out-Null
    Assert-NotReparsePoint -Path $temporaryRootPath -Description 'The temporary download directory'

    $operationId = [Guid]::NewGuid().ToString('N')
    $downloadDirectory = Join-Path $temporaryRootPath "ShaderCoreRelease.$operationId"
    $archivePath = Join-Path $downloadDirectory 'jp.lilxyzw.shadercore-0.1.9.zip'
    $candidatePath = Join-Path $packagesPath ".jp.lilxyzw.shadercore.staging.$operationId"
    $backupPath = Join-Path $packagesPath ".jp.lilxyzw.shadercore.backup.$operationId"
    $targetMoved = $false
    $candidateCommitted = $false
    $restoreVerified = $false

    try {
        New-Item -ItemType Directory -Path $downloadDirectory -ErrorAction Stop | Out-Null
        Invoke-WebRequest -Uri $Uri -OutFile $archivePath -ErrorAction Stop

        $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        if ($actualSha256 -ne $ExpectedSha256) {
            throw "The downloaded Shader-Core release archive SHA-256 '$actualSha256' does not match the required SHA-256 '$ExpectedSha256'."
        }

        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        New-Item -ItemType Directory -Path $candidatePath -ErrorAction Stop | Out-Null
        $candidatePrefix = $candidatePath + [IO.Path]::DirectorySeparatorChar
        $seenEntryPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            foreach ($entry in $archive.Entries) {
                $entryPath = Get-SafeArchiveRelativePath -Entry $entry
                if ($null -eq $entryPath) {
                    continue
                }

                if (-not $seenEntryPaths.Add($entryPath)) {
                    throw "The release archive contains duplicate entry path '$entryPath'."
                }

                $destinationPath = [IO.Path]::GetFullPath((Join-Path $candidatePath $entryPath))
                if (-not $destinationPath.StartsWith($candidatePrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    throw "The release archive entry escapes the staging directory: '$entryPath'."
                }

                $destinationDirectory = Split-Path -Parent $destinationPath
                New-Item -ItemType Directory -Path $destinationDirectory -Force -ErrorAction Stop | Out-Null
                $input = $entry.Open()
                try {
                    $output = [IO.File]::Open($destinationPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
                    try {
                        $input.CopyTo($output)
                    }
                    finally {
                        $output.Dispose()
                    }
                }
                finally {
                    $input.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }

        Assert-ExtractedTreeIsSafe -RootPath $candidatePath
        Assert-ShaderCorePackageMetadata -PackageRoot $candidatePath

        if (Test-Path -LiteralPath $targetPath) {
            Move-Item -LiteralPath $targetPath -Destination $backupPath -ErrorAction Stop
            $targetMoved = $true
        }

        Move-Item -LiteralPath $candidatePath -Destination $targetPath -ErrorAction Stop
        $candidateCommitted = $true
    }
    catch {
        $installationFailure = $_
        if ($targetMoved -and -not $candidateCommitted) {
            try {
                if (Test-Path -LiteralPath $targetPath) {
                    throw "The target path remains occupied and cannot be replaced during recovery: '$targetPath'."
                }
                if (-not (Test-Path -LiteralPath $backupPath)) {
                    throw "The previous Shader-Core target backup is missing: '$backupPath'."
                }

                Move-Item -LiteralPath $backupPath -Destination $targetPath -ErrorAction Stop
                if (-not (Test-Path -LiteralPath $targetPath) -or (Test-Path -LiteralPath $backupPath)) {
                    throw "The previous Shader-Core target was not restored from '$backupPath'."
                }
                $restoreVerified = $true
            }
            catch {
                throw "The Shader-Core installation failed after backing up the previous target. The previous target remains recoverable at '$backupPath'. Original failure: $($installationFailure.Exception.Message) Restore failure: $($_.Exception.Message)"
            }
        }
        throw $installationFailure
    }
    finally {
        if (Test-Path -LiteralPath $candidatePath) {
            Remove-Item -LiteralPath $candidatePath -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ((Test-Path -LiteralPath $backupPath) -and ($candidateCommitted -or $restoreVerified)) {
            Remove-Item -LiteralPath $backupPath -Recurse -Force -ErrorAction SilentlyContinue
        }
        if (Test-Path -LiteralPath $downloadDirectory) {
            Remove-Item -LiteralPath $downloadDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    Install-VerifiedShaderCoreRelease @PSBoundParameters
}
