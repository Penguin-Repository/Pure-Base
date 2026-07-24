<#
 Copyright 2026 Penguin

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.
#>

# Builds and audits a PureBase release ZIP exclusively from Git-tracked package files.
[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputDirectory,

    [Parameter()]
    [switch]$WriteShaderCoreManifest,

    [Parameter()]
    [switch]$AuditOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256Hex {
    param(
        [Parameter(ParameterSetName = 'Path', Mandatory = $true)][string]$Path,
        [Parameter(ParameterSetName = 'Bytes', Mandatory = $true)][byte[]]$Bytes
    )

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        if ($PSCmdlet.ParameterSetName -eq 'Path') {
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
            try {
                $hashBytes = $sha256.ComputeHash($stream)
            }
            finally {
                $stream.Dispose()
            }
        }
        else {
            $hashBytes = $sha256.ComputeHash($Bytes)
        }

        return ([System.BitConverter]::ToString($hashBytes)).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-OrdinalSortedStrings {
    param([Parameter(Mandatory = $true)][string[]]$Values)

    $sortedValues = [string[]]@($Values)
    [System.Array]::Sort($sortedValues, [System.StringComparer]::Ordinal)
    return $sortedValues
}

function Get-NormalizedRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '[\x00]' -or $Path -match '^[\\/]' -or $Path -match '^[A-Za-z]:' -or $Path -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "Unsafe relative path: '$Path'."
    }

    $normalizedPath = $Path.Replace('\', '/')
    if ($normalizedPath -match '//' -or $normalizedPath -match '(^|/)\.(?:/|$)' -or $normalizedPath.EndsWith('/')) {
        throw "Non-canonical relative path: '$Path'."
    }

    return $normalizedPath
}

function Test-PathPattern {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Pattern
    )

    $escapedPattern = [regex]::Escape($Pattern).Replace('\*\*', '.*').Replace('\*', '[^/]*')
    return $Path -match ('^' + $escapedPattern + '$')
}

function Test-ReparsePoint {
    param([Parameter(Mandatory = $true)][System.IO.FileSystemInfo]$Item)

    return (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Test-PathEqualOrDescendant {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Candidate
    )

    $normalizedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $normalizedCandidate = [System.IO.Path]::GetFullPath($Candidate).TrimEnd('\', '/')
    return [string]::Equals($normalizedCandidate, $normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or $normalizedCandidate.StartsWith($normalizedRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-ExternalOutputDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$PackageRoot,
        [Parameter(Mandatory = $true)][string]$OutputDirectory
    )

    $outputDirectoryFullPath = [System.IO.Path]::GetFullPath($OutputDirectory)
    if (Test-PathEqualOrDescendant -Root $PackageRoot -Candidate $outputDirectoryFullPath) {
        throw 'Release artifacts must be outside the package Git root.'
    }

    $currentPath = $outputDirectoryFullPath
    while ($true) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (Test-ReparsePoint -Item $item) {
                throw "Release artifact path contains a reparse point: '$currentPath'."
            }
            if ([string]::Equals($currentPath, $outputDirectoryFullPath, [System.StringComparison]::OrdinalIgnoreCase) -and -not $item.PSIsContainer) {
                throw "Release artifact path must be a directory: '$outputDirectoryFullPath'."
            }
        }

        $parentPath = Split-Path -Parent $currentPath
        if ([string]::IsNullOrWhiteSpace($parentPath) -or [string]::Equals($parentPath, $currentPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $currentPath = $parentPath
    }

    return $outputDirectoryFullPath
}

function Assert-NoReparsePointPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $currentPath = $Root
    foreach ($segment in $RelativePath.Replace('/', '\').Split('\')) {
        if ([string]::IsNullOrWhiteSpace($segment)) {
            continue
        }
        $currentPath = Join-Path $currentPath $segment
        if (Test-ReparsePoint -Item (Get-Item -LiteralPath $currentPath -Force)) {
            throw "Path contains a reparse point: '$RelativePath'."
        }
    }
}

function Get-ItemInsideRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $normalizedRelativePath = Get-NormalizedRelativePath -Path $RelativePath
    $candidatePath = [System.IO.Path]::GetFullPath((Join-Path $Root $normalizedRelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)))
    $rootPrefix = $Root.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidatePath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes the package root: '$RelativePath'."
    }

    $item = Get-Item -LiteralPath $candidatePath -Force
    Assert-NoReparsePointPath -Root $Root -RelativePath $normalizedRelativePath
    if ($item.PSIsContainer -or (Test-ReparsePoint -Item $item)) {
        throw "Release input must be a regular non-reparse-point file: '$RelativePath'."
    }

    return $item
}

function Get-TrackedFiles {
    param([Parameter(Mandatory = $true)][string]$PackageRoot)

    $gitOutput = & git -C $PackageRoot ls-files -z
    if ($LASTEXITCODE -ne 0) {
        throw 'git ls-files failed for the package repository.'
    }

    $trackedFiles = @(Get-OrdinalSortedStrings -Values @($gitOutput -split "`0" | Where-Object { $_ -ne '' } | ForEach-Object { Get-NormalizedRelativePath -Path $_ }))
    $duplicatePaths = @($trackedFiles | Group-Object | Where-Object { $_.Count -gt 1 })
    if ($duplicatePaths.Count -ne 0) {
        throw ('Git tracked paths normalize to duplicates: ' + (($duplicatePaths | ForEach-Object { $_.Name }) -join ', '))
    }

    return $trackedFiles
}

function Test-ContractPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Contract
    )

    foreach ($pattern in $Contract.excludedPathPatterns) {
        if (Test-PathPattern -Path $Path -Pattern $pattern) {
            return $false
        }
    }

    if ($Contract.allowedExactPaths -contains $Path) {
        return $true
    }

    foreach ($prefix in $Contract.allowedPathPrefixes) {
        if ($Path.StartsWith([string]$prefix, [System.StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

function Assert-PackageMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$PackageRoot,
        [Parameter(Mandatory = $true)]$Contract
    )

    $packageJson = Get-Content -LiteralPath (Join-Path $PackageRoot 'package.json') -Raw | ConvertFrom-Json
    if ($packageJson.name -ne $Contract.packageName) {
        throw "package.json name '$($packageJson.name)' does not match release contract '$($Contract.packageName)'."
    }

    foreach ($dependency in $Contract.requiredVpmDependencies.PSObject.Properties) {
        $actualDependency = $packageJson.vpmDependencies.PSObject.Properties[$dependency.Name]
        if ($null -eq $actualDependency -or [string]$actualDependency.Value -ne [string]$dependency.Value) {
            throw "package.json must require $($dependency.Name) exactly $($dependency.Value)."
        }
    }

    foreach ($dependencySectionName in @('dependencies', 'vpmDependencies')) {
        $dependencySection = $packageJson.PSObject.Properties[$dependencySectionName]
        if ($null -ne $dependencySection) {
            foreach ($dependency in $dependencySection.Value.PSObject.Properties) {
                if ($Contract.forbiddenDependencyNames -contains $dependency.Name) {
                    throw "package.json contains forbidden URP-related dependency '$($dependency.Name)'."
                }
            }
        }
    }
}

function Assert-ForbiddenShaderProperties {
    param(
        [Parameter(Mandatory = $true)][string]$PackageRoot,
        [Parameter(Mandatory = $true)][string[]]$ReleaseFiles,
        [Parameter(Mandatory = $true)]$Contract
    )

    $shaderFiles = $ReleaseFiles | Where-Object { $_ -match '^Shaders/.*\.(?:scshader|hlsl)$' }
    foreach ($relativePath in $shaderFiles) {
        $content = Get-Content -LiteralPath (Join-Path $PackageRoot $relativePath) -Raw
        foreach ($propertyName in $Contract.forbiddenShaderPropertyNames) {
            if ($content -match ([regex]::Escape([string]$propertyName))) {
                throw "Forbidden optional shader property '$propertyName' found in '$relativePath'."
            }
        }
    }
}

function Get-VerifiedShaderCorePackageIdentity {
    param([Parameter(Mandatory = $true)][string]$ShaderCoreRoot)

    $shaderCorePackage = Get-Content -LiteralPath (Join-Path $ShaderCoreRoot 'package.json') -Raw | ConvertFrom-Json
    $packageName = [string]$shaderCorePackage.name
    $packageVersion = [string]$shaderCorePackage.version
    if ($packageName -ne 'jp.lilxyzw.shadercore' -or $packageVersion -ne '0.1.9') {
        throw 'Local Shader-Core must be jp.lilxyzw.shadercore version 0.1.9.'
    }

    return [pscustomobject][ordered]@{
        packageName = $packageName
        packageVersion = $packageVersion
    }
}

function Get-RecursiveIdentityManifest {
    param(
        [Parameter(Mandatory = $true)][string]$ShaderCoreRoot,
        [Parameter(Mandatory = $true)]$ShaderCorePackageIdentity
    )

    $manifestEntries = New-Object System.Collections.Generic.List[object]
    foreach ($directory in Get-ChildItem -LiteralPath $ShaderCoreRoot -Directory -Recurse -Force) {
        if (Test-ReparsePoint -Item $directory) {
            throw "Shader-Core identity directory is a reparse point: '$($directory.FullName)'."
        }
    }
    $files = Get-ChildItem -LiteralPath $ShaderCoreRoot -File -Recurse -Force
    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($ShaderCoreRoot.Length).TrimStart('\', '/')
        if ($relativePath -match '^(?:\.git|\.serena)(?:/|\\|$)') {
            continue
        }

        $normalizedRelativePath = Get-NormalizedRelativePath -Path $relativePath
        if (Test-ReparsePoint -Item $file) {
            throw "Shader-Core identity input is a reparse point: '$normalizedRelativePath'."
        }

        [void]$manifestEntries.Add([pscustomobject][ordered]@{
                path = $normalizedRelativePath
            sha256 = Get-Sha256Hex -Path $file.FullName
            })
    }

    $duplicatePaths = @($manifestEntries | Group-Object path | Where-Object { $_.Count -gt 1 })
    if ($duplicatePaths.Count -ne 0) {
        throw 'Shader-Core identity paths normalize to duplicates.'
    }

    $entriesByPath = @{}
    foreach ($entry in $manifestEntries) {
        $entriesByPath.Add([string]$entry.path, $entry)
    }
    $sortedManifestEntries = New-Object System.Collections.Generic.List[object]
    $sortedManifestPaths = New-Object string[] $manifestEntries.Count
    for ($index = 0; $index -lt $manifestEntries.Count; $index++) {
        $sortedManifestPaths[$index] = [string]$manifestEntries[$index].path
    }
    [System.Array]::Sort($sortedManifestPaths, [System.StringComparer]::Ordinal)
    foreach ($path in $sortedManifestPaths) {
        [void]$sortedManifestEntries.Add($entriesByPath[$path])
    }

    $identityLines = (@($sortedManifestEntries | ForEach-Object { "$($_.path)`t$($_.sha256)`n" }) -join '')
    $identityBytes = [System.Text.Encoding]::UTF8.GetBytes($identityLines)
    $identityHash = Get-Sha256Hex -Bytes $identityBytes

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        packageName = [string]$ShaderCorePackageIdentity.packageName
        packageVersion = [string]$ShaderCorePackageIdentity.packageVersion
        scope = 'All regular, non-reparse-point files below the local Packages/jp.lilxyzw.shadercore directory, excluding .git and .serena directories. Entry paths are package-relative UTF-8 paths normalized to forward slashes and sorted ordinally.'
        algorithm = "For each entry, SHA-256 is computed over raw file bytes. The identity SHA-256 is computed over UTF-8 lines '<entry-path>\t<lowercase-file-sha256>\n' in ordinal path order."
        identitySha256 = $identityHash
        entries = $sortedManifestEntries.ToArray()
    }
}

function Write-ShaderCoreIdentityManifest {
    param(
        [Parameter(Mandatory = $true)][string]$ShaderCoreRoot,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $shaderCorePackageIdentity = Get-VerifiedShaderCorePackageIdentity -ShaderCoreRoot $ShaderCoreRoot
    $manifest = Get-RecursiveIdentityManifest -ShaderCoreRoot $ShaderCoreRoot -ShaderCorePackageIdentity $shaderCorePackageIdentity
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ManifestPath -Encoding UTF8
}

function Assert-ShaderCoreIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$ShaderCoreRoot,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $shaderCorePackageIdentity = Get-VerifiedShaderCorePackageIdentity -ShaderCoreRoot $ShaderCoreRoot
    $expectedManifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($expectedManifest.packageName -ne $shaderCorePackageIdentity.packageName -or $expectedManifest.packageVersion -ne $shaderCorePackageIdentity.packageVersion) {
        throw 'Shader-Core identity manifest package metadata does not match the verified local package.'
    }

    $actualManifest = Get-RecursiveIdentityManifest -ShaderCoreRoot $ShaderCoreRoot -ShaderCorePackageIdentity $shaderCorePackageIdentity
    if ($expectedManifest.identitySha256 -ne $actualManifest.identitySha256 -or @($expectedManifest.entries).Count -ne @($actualManifest.entries).Count) {
        throw 'Local Shader-Core identity does not match shader-core-0.1.9.sha256.json. Regenerate only after intentionally reviewing the dependency source.'
    }

    for ($index = 0; $index -lt @($actualManifest.entries).Count; $index++) {
        if ($expectedManifest.entries[$index].path -ne $actualManifest.entries[$index].path -or $expectedManifest.entries[$index].sha256 -ne $actualManifest.entries[$index].sha256) {
            throw "Local Shader-Core identity differs at manifest entry $index."
        }
    }
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$packageRoot = (& git -C $scriptRoot rev-parse --show-toplevel).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($packageRoot)) {
    throw 'Cannot resolve the nested PureBase package Git root.'
}
$packageRoot = [System.IO.Path]::GetFullPath($packageRoot)
if ((Split-Path -Leaf $packageRoot) -ne 'jp.penguin.purebase') {
    throw "Expected the nested package Git root, received '$packageRoot'."
}
if (Test-ReparsePoint -Item (Get-Item -LiteralPath $packageRoot -Force)) {
    throw 'The package Git root must not be a reparse point.'
}

$workspaceRoot = Split-Path -Parent (Split-Path -Parent $packageRoot)
$shaderCoreRoot = Join-Path (Join-Path $workspaceRoot 'Packages') 'jp.lilxyzw.shadercore'
$contractPath = Join-Path $scriptRoot 'release-content.json'
$manifestPath = Join-Path $scriptRoot 'shader-core-0.1.9.sha256.json'
$contract = Get-Content -LiteralPath $contractPath -Raw | ConvertFrom-Json
if ($contract.schemaVersion -ne 1) {
    throw "Unsupported release contract schema version '$($contract.schemaVersion)'."
}

if ($WriteShaderCoreManifest) {
    Write-ShaderCoreIdentityManifest -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath
    Write-Output "Wrote Shader-Core identity manifest: $manifestPath"
    return
}

Assert-PackageMetadata -PackageRoot $packageRoot -Contract $contract
$trackedFiles = Get-TrackedFiles -PackageRoot $packageRoot
$releaseFiles = New-Object System.Collections.Generic.List[string]
foreach ($relativePath in $trackedFiles) {
    if (Test-ContractPath -Path $relativePath -Contract $contract) {
        [void](Get-ItemInsideRoot -Root $packageRoot -RelativePath $relativePath)
        $releaseFiles.Add($relativePath)
    }
}

foreach ($requiredEntry in $contract.requiredEntries) {
    $normalizedRequiredEntry = Get-NormalizedRelativePath -Path $requiredEntry
    if (-not $releaseFiles.Contains($normalizedRequiredEntry)) {
        throw "Required release entry is absent from the tracked source set: '$normalizedRequiredEntry'."
    }
}

Assert-ForbiddenShaderProperties -PackageRoot $packageRoot -ReleaseFiles $releaseFiles.ToArray() -Contract $contract
Assert-ShaderCoreIdentity -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath

if ($AuditOnly) {
    Write-Output "Release source audit passed: $($releaseFiles.Count) tracked files."
    return
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'PureBaseRelease'
}
$outputDirectoryFullPath = Assert-ExternalOutputDirectory -PackageRoot $packageRoot -OutputDirectory $OutputDirectory
if (Test-Path -LiteralPath $outputDirectoryFullPath) {
}
else {
    [void](New-Item -ItemType Directory -Path $outputDirectoryFullPath -Force)
}

$stageDirectory = Join-Path $outputDirectoryFullPath ('purebase-release-stage-' + [guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $outputDirectoryFullPath 'jp.penguin.purebase-0.1.0.zip'
$hashPath = $zipPath + '.sha256'
try {
    [void](New-Item -ItemType Directory -Path $stageDirectory -Force)
    foreach ($relativePath in $releaseFiles) {
        $sourceItem = Get-ItemInsideRoot -Root $packageRoot -RelativePath $relativePath
        $stagePath = Join-Path $stageDirectory $relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $stagePath) -Force)
        Copy-Item -LiteralPath $sourceItem.FullName -Destination $stagePath -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stageDirectory, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $zipEntries = New-Object System.Collections.Generic.List[string]
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith('/')) {
                continue
            }
            $entryPath = Get-NormalizedRelativePath -Path $entry.FullName
            if (-not (Test-ContractPath -Path $entryPath -Contract $contract)) {
                throw "ZIP contains excluded or unapproved entry '$entryPath'."
            }
            if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                throw "ZIP entry '$entryPath' is a symbolic link."
            }
            $zipEntries.Add($entryPath)
        }

        $duplicates = @($zipEntries | Group-Object | Where-Object { $_.Count -gt 1 })
        if ($duplicates.Count -ne 0) {
            throw ('ZIP contains duplicate normalized entries: ' + (($duplicates | ForEach-Object { $_.Name }) -join ', '))
        }
        $sortedReleaseFiles = @(Get-OrdinalSortedStrings -Values $releaseFiles.ToArray())
        $sortedZipEntries = @(Get-OrdinalSortedStrings -Values $zipEntries.ToArray())
        $zipEntriesMatchReleaseFiles = $sortedReleaseFiles.Count -eq $sortedZipEntries.Count
        if ($zipEntriesMatchReleaseFiles) {
            for ($index = 0; $index -lt $sortedReleaseFiles.Count; $index++) {
                if (-not [string]::Equals($sortedReleaseFiles[$index], $sortedZipEntries[$index], [System.StringComparison]::Ordinal)) {
                    $zipEntriesMatchReleaseFiles = $false
                    break
                }
            }
        }
        if (-not $zipEntriesMatchReleaseFiles) {
            throw 'ZIP entries do not exactly match the audited tracked release source set.'
        }
        foreach ($requiredEntry in $contract.requiredEntries) {
            if (-not $zipEntries.Contains([string]$requiredEntry)) {
                throw "ZIP omits required release entry '$requiredEntry'."
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    $zipHash = Get-Sha256Hex -Path $zipPath
    Set-Content -LiteralPath $hashPath -Value $zipHash -Encoding ASCII
    Write-Output "Release ZIP: $zipPath"
    Write-Output "SHA-256: $zipHash"
    Write-Output "Audited entries: $($releaseFiles.Count)"
}
finally {
    if (Test-Path -LiteralPath $stageDirectory) {
        Remove-Item -LiteralPath $stageDirectory -Recurse -Force
    }
}