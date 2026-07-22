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

# Builds an audited archive and validates it in one disposable external Unity consumer project.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityEditorPath,

    [Parameter()]
    [string]$ArtifactDirectory,

    [Parameter()]
    [switch]$KeepConsumer,

    [Parameter()]
    [switch]$CompareWarmAndColdStandardMorph,

    [Parameter()]
    [switch]$ModuleFreeOnly

    ,
    [Parameter()]
    [switch]$ToonBaseOnly,

    [Parameter()]
    [switch]$FogOnly,

    [Parameter()]
    [switch]$BakeOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RequiredUnityVersion = '2022.3.22f1'
$ConsumerAssembly = 'PureBase.Release.Consumer.Tests'
$ProductNames = @('PureBase/Unlit', 'PureBase/Toon', 'PureBase/PBR', 'PureBase/Hybrid')
$ProductPasses = @('ForwardBase', 'ForwardAdd', 'ShadowCaster', 'Meta')
$AllSentinels = @(
    'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_MORPH', 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_POSTVERTEX',
    'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_BASE', 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_LIGHT',
    'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_CUSTOMLIGHT', 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_MODIFYLIGHT',
    'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_SHADE', 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_REFLECTION',
    'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_ADD', 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_POSTPIXEL',
    'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_BASE', 'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_LIGHT',
    'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_MODIFYLIGHT', 'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_SHADE',
    'PUREBASE_UNLIT_FORWARD_ADD_FOG_SENTINEL', 'PUREBASE_MODULE_ORDER_ALPHA', 'PUREBASE_MODULE_ORDER_ZETA'
)

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathContainedBy {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ParentPath
    )

    $candidate = Get-NormalizedPath -Path $Path
    $parent = Get-NormalizedPath -Path $ParentPath
    return $candidate.Equals($parent, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith($parent + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-ReparsePoint {
    param([Parameter(Mandatory = $true)][System.IO.FileSystemInfo]$Item)

    return (($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0)
}

function Assert-PathHasNoReparsePoints {
    param([Parameter(Mandatory = $true)][string]$Path)

    $current = Get-NormalizedPath -Path $Path
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (Test-ReparsePoint -Item $item) {
                throw "Path contains a reparse point: '$current'."
            }
        }

        $parent = Split-Path -Parent $current
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            return
        }
        $current = $parent
    }
}

function Assert-RegularTree {
    param([Parameter(Mandatory = $true)][string]$Root)

    Assert-PathHasNoReparsePoints -Path $Root
    foreach ($item in Get-ChildItem -LiteralPath $Root -Recurse -Force) {
        if (Test-ReparsePoint -Item $item) {
            throw "Source tree contains a reparse point: '$($item.FullName)'."
        }
    }
}

function Get-PackageGitRoot {
    $candidate = Split-Path -Parent $PSScriptRoot
    $root = (& git -C $candidate rev-parse --show-toplevel).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw 'Cannot resolve the nested PureBase package Git root.'
    }

    $root = Get-NormalizedPath -Path $root
    if ((Split-Path -Leaf $root) -ne 'jp.penguin.purebase') {
        throw "Expected nested package Git root 'jp.penguin.purebase', received '$root'."
    }
    Assert-PathHasNoReparsePoints -Path $root
    return $root
}

function Assert-ExternalDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$WorkspaceRoot
    )

    $fullPath = Get-NormalizedPath -Path $Path
    if (Test-PathContainedBy -Path $fullPath -ParentPath $WorkspaceRoot) {
        throw "External output '$fullPath' must be outside workspace '$WorkspaceRoot'."
    }
    Assert-PathHasNoReparsePoints -Path $fullPath
    if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
        throw "External output '$fullPath' must be a directory."
    }
    return $fullPath
}

function Resolve-UnityEditor {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        throw 'UnityEditorPath must be an absolute path to Unity.exe; bare Unity.exe is not accepted.'
    }
    $editor = Get-NormalizedPath -Path $Path
    if ((Split-Path -Leaf $editor) -ine 'Unity.exe' -or -not (Test-Path -LiteralPath $editor -PathType Leaf)) {
        throw "UnityEditorPath must identify an existing Unity.exe: '$editor'."
    }

    $versionDirectory = [System.IO.DirectoryInfo](Split-Path -Parent $editor)
    while ($null -ne $versionDirectory) {
        if ($versionDirectory.Name -eq $RequiredUnityVersion) {
            return $editor
        }
        $versionDirectory = $versionDirectory.Parent
    }
    throw "Unity editor '$editor' cannot be proven to be Unity $RequiredUnityVersion."
}

function Assert-EditorClosed {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $lockPath = Join-Path $ProjectRoot 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $lockPath -PathType Leaf) {
        throw "Unity project '$ProjectRoot' appears open. Close the Unity Editor before running this validation."
    }

    $projectPattern = [regex]::Escape((Get-NormalizedPath -Path $ProjectRoot))
    $openProject = Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue | Where-Object {
        $null -ne $_.CommandLine -and $_.CommandLine -match $projectPattern
    } | Select-Object -First 1
    if ($null -ne $openProject) {
        throw "Unity process $($openProject.ProcessId) is using '$ProjectRoot'."
    }
}

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

function Get-ImmutableTreeEntries {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$RootName
    )

    $rootPath = Join-Path $ConsumerRoot $RootName
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
        throw "Immutable consumer root '$RootName' is missing."
    }
    Assert-RegularTree -Root $rootPath

    $entriesByPath = @{}
    foreach ($file in Get-ChildItem -LiteralPath $rootPath -File -Recurse -Force) {
        $relativePath = Get-NormalizedRelativePath -Path $file.FullName.Substring($rootPath.Length).TrimStart('\', '/')
        if ($RootName -eq 'Assets' -and $relativePath.StartsWith('Artifacts/', [System.StringComparison]::Ordinal)) {
            continue
        }
        if ($RootName -eq '_LocalPackages' -and ($relativePath.StartsWith('jp.lilxyzw.shadercore/.git/', [System.StringComparison]::Ordinal) -or $relativePath.StartsWith('jp.lilxyzw.shadercore/.serena/', [System.StringComparison]::Ordinal))) {
            continue
        }

        $manifestPath = $RootName + '/' + $relativePath
        if ($entriesByPath.ContainsKey($manifestPath)) {
            throw "Immutable consumer paths normalize to a duplicate: '$manifestPath'."
        }
        $entriesByPath.Add($manifestPath, [ordered]@{ path = $manifestPath; sha256 = Get-Sha256Hex -Path $file.FullName })
    }

    return @($entriesByPath.Values)
}

function Get-ConsumerImmutableManifest {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$ShaderCoreManifestPath
    )

    $rootNames = @('Assets', 'Packages', 'ProjectSettings', '_LocalPackages')
    $entriesByPath = @{}
    foreach ($rootName in $rootNames) {
        foreach ($entry in Get-ImmutableTreeEntries -ConsumerRoot $ConsumerRoot -RootName $rootName) {
            $entriesByPath.Add([string]$entry.path, $entry)
        }
    }

    $sortedPaths = Get-OrdinalSortedStrings -Values ([string[]]@($entriesByPath.Keys))
    $entries = @($sortedPaths | ForEach-Object { $entriesByPath[$_] })
    $manifestLines = (@($entries | ForEach-Object { "$($_.path)`t$($_.sha256)`n" }) -join '')
    $shaderCorePackagePath = Join-Path $ConsumerRoot '_LocalPackages/jp.lilxyzw.shadercore/package.json'
    $shaderCorePackage = Get-Content -LiteralPath $shaderCorePackagePath -Raw | ConvertFrom-Json
    $expectedShaderCoreManifest = Get-Content -LiteralPath $ShaderCoreManifestPath -Raw | ConvertFrom-Json
    $shaderCoreEntries = @($entries | Where-Object { $_.path.StartsWith('_LocalPackages/jp.lilxyzw.shadercore/', [System.StringComparison]::Ordinal) })
    $shaderCoreLines = (@($shaderCoreEntries | ForEach-Object { "$($_.path.Substring('_LocalPackages/jp.lilxyzw.shadercore/'.Length))`t$($_.sha256)`n" }) -join '')

    $shaderCoreTreeHash = Get-Sha256Hex -Bytes ([System.Text.Encoding]::UTF8.GetBytes($shaderCoreLines))

    return [ordered]@{
        schemaVersion = 1
        pathOrdering = 'System.StringComparer.Ordinal'
        immutableRoots = $rootNames
        excludedMutablePathPrefixes = @('Assets/Artifacts/', 'Library/')
        rootSha256 = Get-Sha256Hex -Bytes ([System.Text.Encoding]::UTF8.GetBytes($manifestLines))
        entries = $entries
        releaseZipSha256 = Get-Sha256Hex -Path $ZipPath
        shaderCore = [ordered]@{
            packageName = $shaderCorePackage.name
            packageVersion = $shaderCorePackage.version
            expectedIdentitySha256 = [string]$expectedShaderCoreManifest.identitySha256
            treeSha256 = $shaderCoreTreeHash
        }
    }
}

function Add-ConsumerStagingReceiptTreeEntries {
    param(
        [Parameter(Mandatory = $true)][hashtable]$EntriesByDestination,
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][string]$DestinationPrefix,
        [Parameter(Mandatory = $true)][string]$SourceKind
    )

    Assert-RegularTree -Root $SourceRoot
    foreach ($file in Get-ChildItem -LiteralPath $SourceRoot -File -Recurse -Force) {
        $relativePath = Get-NormalizedRelativePath -Path $file.FullName.Substring($SourceRoot.Length).TrimStart('\', '/')
        $destination = if ([string]::IsNullOrEmpty($DestinationPrefix)) { $relativePath } else { $DestinationPrefix + '/' + $relativePath }
        if ($EntriesByDestination.ContainsKey($destination)) {
            throw "Staging receipt source mappings overlap at '$destination'."
        }
        $EntriesByDestination.Add($destination, [ordered]@{
                destination = $destination
                sourceKind = $SourceKind
                source = $file.FullName
                sha256 = Get-Sha256Hex -Path $file.FullName
            })
    }
}

function Get-ConsumerStagingReceipt {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$ScaffoldRoot,
        [Parameter(Mandatory = $true)][string]$ShaderCoreRoot,
        [Parameter(Mandatory = $true)][string]$ModulesRoot,
        [Parameter(Mandatory = $true)][string]$FixturesRoot
    )

    $entriesByDestination = @{}
    Add-ConsumerStagingReceiptTreeEntries -EntriesByDestination $entriesByDestination -SourceRoot $ScaffoldRoot -DestinationPrefix '' -SourceKind 'consumer-scaffold'
    Add-ConsumerStagingReceiptTreeEntries -EntriesByDestination $entriesByDestination -SourceRoot $ShaderCoreRoot -DestinationPrefix '_LocalPackages/jp.lilxyzw.shadercore' -SourceKind 'shader-core-tree'
    Add-ConsumerStagingReceiptTreeEntries -EntriesByDestination $entriesByDestination -SourceRoot $ModulesRoot -DestinationPrefix 'Assets/ReleaseModules' -SourceKind 'release-modules'
    Add-ConsumerStagingReceiptTreeEntries -EntriesByDestination $entriesByDestination -SourceRoot $FixturesRoot -DestinationPrefix 'Assets/ReleaseConsumer/Fixtures' -SourceKind 'release-fixtures'

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName.EndsWith('/')) {
                continue
            }
            $entryPath = Get-NormalizedRelativePath -Path $entry.FullName
            $destination = '_LocalPackages/jp.penguin.purebase/' + $entryPath
            if ($entriesByDestination.ContainsKey($destination)) {
                throw "Staging receipt source mappings overlap at '$destination'."
            }
            $stream = $entry.Open()
            try {
                $sha256 = [System.Security.Cryptography.SHA256]::Create()
                try {
                    $hash = ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
                }
                finally {
                    $sha256.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }
            $entriesByDestination.Add($destination, [ordered]@{
                    destination = $destination
                    sourceKind = 'release-zip-entry'
                    source = $entry.FullName
                    sha256 = $hash
                })
        }
    }
    finally {
        $archive.Dispose()
    }

    $destinations = Get-OrdinalSortedStrings -Values ([string[]]@($entriesByDestination.Keys))
    return [ordered]@{
        schemaName = 'purebase-consumer-staging-receipt'
        schemaVersion = 1
        pathOrdering = 'System.StringComparer.Ordinal'
        entries = @($destinations | ForEach-Object { $entriesByDestination[$_] })
    }
}

function Assert-ConsumerStagingReceipt {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)]$Receipt
    )

    if ($Receipt.schemaName -ne 'purebase-consumer-staging-receipt' -or [int]$Receipt.schemaVersion -ne 1 -or $Receipt.pathOrdering -ne 'System.StringComparer.Ordinal') {
        throw 'Consumer staging receipt has an unsupported schema.'
    }

    $entriesByDestination = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::Ordinal)
    foreach ($entry in @($Receipt.entries)) {
        $destination = Get-NormalizedRelativePath -Path ([string]$entry.destination)
        if ($entriesByDestination.ContainsKey($destination)) {
            throw "Consumer staging receipt contains duplicate destination '$destination'."
        }
        $entriesByDestination.Add($destination, $entry)
        $destinationPath = Join-Path $ConsumerRoot $destination.Replace('/', '\')
        if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
            throw "Staged consumer destination is missing: '$destination'."
        }
        $actualHash = Get-Sha256Hex -Path $destinationPath
        if (-not [string]::Equals([string]$entry.sha256, $actualHash, [System.StringComparison]::Ordinal)) {
            throw "Staged consumer destination content mismatches its source receipt: '$destination'."
        }
    }

    Assert-RegularTree -Root $ConsumerRoot
    foreach ($file in Get-ChildItem -LiteralPath $ConsumerRoot -File -Recurse -Force) {
        $destination = Get-NormalizedRelativePath -Path $file.FullName.Substring($ConsumerRoot.Length).TrimStart('\', '/')
        if (-not $entriesByDestination.ContainsKey($destination)) {
            throw "Staged consumer destination is extra: '$destination'."
        }
    }
}

function Get-ConsumerImmutableManifestDeltaReport {
    param(
        [Parameter(Mandatory = $true)]$PreBootstrap,
        [Parameter(Mandatory = $true)]$PostBootstrap
    )

    $preEntries = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
    foreach ($entry in @($PreBootstrap.entries)) {
        $path = Get-NormalizedRelativePath -Path ([string]$entry.path)
        if ($preEntries.ContainsKey($path)) { throw "Pre-bootstrap immutable manifest contains duplicate path '$path'." }
        $preEntries.Add($path, [string]$entry.sha256)
    }
    $postEntries = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([System.StringComparer]::Ordinal)
    foreach ($entry in @($PostBootstrap.entries)) {
        $path = Get-NormalizedRelativePath -Path ([string]$entry.path)
        if ($postEntries.ContainsKey($path)) { throw "Post-bootstrap immutable manifest contains duplicate path '$path'." }
        $postEntries.Add($path, [string]$entry.sha256)
    }

    $added = New-Object System.Collections.Generic.List[object]
    $removed = New-Object System.Collections.Generic.List[object]
    $changed = New-Object System.Collections.Generic.List[object]
    foreach ($path in Get-OrdinalSortedStrings -Values ([string[]]@($postEntries.Keys))) {
        if (-not $preEntries.ContainsKey($path)) {
            [void]$added.Add([ordered]@{ path = $path; sha256 = $postEntries[$path] })
        }
        elseif (-not [string]::Equals($preEntries[$path], $postEntries[$path], [System.StringComparison]::Ordinal)) {
            [void]$changed.Add([ordered]@{ path = $path; preBootstrapSha256 = $preEntries[$path]; postBootstrapSha256 = $postEntries[$path] })
        }
    }
    foreach ($path in Get-OrdinalSortedStrings -Values ([string[]]@($preEntries.Keys))) {
        if (-not $postEntries.ContainsKey($path)) {
            [void]$removed.Add([ordered]@{ path = $path; sha256 = $preEntries[$path] })
        }
    }

    return [ordered]@{
        schemaName = 'purebase-immutable-manifest-bootstrap-delta'
        schemaVersion = 1
        classification = 'observed'
        pathOrdering = 'System.StringComparer.Ordinal'
        preBootstrapRootSha256 = [string]$PreBootstrap.rootSha256
        postBootstrapRootSha256 = [string]$PostBootstrap.rootSha256
        added = $added.ToArray()
        removed = $removed.ToArray()
        changed = $changed.ToArray()
    }
}

function Write-ConsumerImmutableManifestBootstrapDelta {
    param(
        [Parameter(Mandatory = $true)]$PreBootstrap,
        [Parameter(Mandatory = $true)]$PostBootstrap,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $deltaReport = Get-ConsumerImmutableManifestDeltaReport -PreBootstrap $PreBootstrap -PostBootstrap $PostBootstrap
    Write-ConsumerJsonArtifact -Path $Path -Value $deltaReport
}

function Assert-ConsumerImmutableManifestBaseline {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$RunLabel
    )

    if ($Manifest.shaderCore.packageName -ne 'jp.lilxyzw.shadercore' -or $Manifest.shaderCore.packageVersion -ne '0.1.5') {
        throw "Consumer run '$RunLabel' did not stage Shader-Core jp.lilxyzw.shadercore version 0.1.5."
    }
    if ($Manifest.shaderCore.expectedIdentitySha256 -ne $Manifest.shaderCore.treeSha256) {
        throw "Consumer run '$RunLabel' staged Shader-Core does not match shader-core-0.1.5.sha256.json."
    }
}

function Assert-ConsumerImmutableManifest {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$RunLabel
    )

    if ($Expected.pathOrdering -ne 'System.StringComparer.Ordinal' -or $Actual.pathOrdering -ne 'System.StringComparer.Ordinal') {
        throw "Consumer run '$RunLabel' did not use explicit ordinal immutable-path ordering."
    }
    if ($Expected.rootSha256 -ne $Actual.rootSha256) {
        throw "Consumer run '$RunLabel' changed immutable staged source, settings, or package inputs."
    }
    if ($Expected.releaseZipSha256 -ne $Actual.releaseZipSha256) {
        throw "Consumer run '$RunLabel' changed the approved release ZIP."
    }
    if ($Expected.shaderCore.packageName -ne $Actual.shaderCore.packageName -or $Expected.shaderCore.packageVersion -ne $Actual.shaderCore.packageVersion -or $Expected.shaderCore.expectedIdentitySha256 -ne $Actual.shaderCore.expectedIdentitySha256 -or $Expected.shaderCore.treeSha256 -ne $Actual.shaderCore.treeSha256) {
        throw "Consumer run '$RunLabel' changed the staged Shader-Core identity."
    }
}

function Write-ConsumerJsonArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value,
        [Parameter()][int]$Depth = 8
    )

    $Value | ConvertTo-Json -Depth $Depth | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-ConsumerFailureEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$RunDirectory,
        [Parameter(Mandatory = $true)][string]$RunLabel,
        [Parameter(Mandatory = $true)][System.Management.Automation.ErrorRecord]$Failure,
        [Parameter(Mandatory = $true)][string[]]$EvidencePaths
    )

    Write-ConsumerJsonArtifact -Path (Join-Path $RunDirectory 'failure.json') -Value ([ordered]@{
            message = $Failure.Exception.Message
            runLabel = $RunLabel
        })
    $failureDirectory = Join-Path $RunDirectory 'failure-evidence'
    New-Item -ItemType Directory -Path $failureDirectory -Force | Out-Null
    foreach ($evidencePath in $EvidencePaths) {
        if (Test-Path -LiteralPath $evidencePath -PathType Leaf) {
            Copy-Item -LiteralPath $evidencePath -Destination (Join-Path $failureDirectory (Split-Path -Leaf $evidencePath)) -Force
        }
    }
}

function Reset-ConsumerLibrary {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    Assert-EditorClosed -ProjectRoot $ConsumerRoot
    $libraryPath = Join-Path $ConsumerRoot 'Library'
    $priorLibraryPresent = Test-Path -LiteralPath $libraryPath
    if ($priorLibraryPresent) {
        Assert-PathHasNoReparsePoints -Path $libraryPath
        Remove-Item -LiteralPath $libraryPath -Recurse -Force
    }

    return [ordered]@{ libraryPath = $libraryPath; priorLibraryPresent = [bool]$priorLibraryPresent; libraryPresentAfterReset = [bool](Test-Path -LiteralPath $libraryPath) }
}

function Invoke-ConsumerBootstrapImport {
    param(
        [Parameter(Mandatory = $true)][string]$UnityEditor,
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$ShaderCoreManifestPath,
        [Parameter(Mandatory = $true)]$StagingReceipt
    )

    $bootstrapDirectory = Join-Path $RunRoot 'bootstrap'
    New-Item -ItemType Directory -Path $bootstrapDirectory -Force | Out-Null
    $commandPath = Join-Path $bootstrapDirectory 'unity-command.json'
    $unityLogPath = Join-Path $bootstrapDirectory 'Unity.log'
    $processLogPath = Join-Path $bootstrapDirectory 'Process.log'
    $resultsPath = Join-Path $bootstrapDirectory 'NUnit.xml'
    $nunitSummaryPath = Join-Path $bootstrapDirectory 'nunit-summary.json'
    $receiptPath = Join-Path $bootstrapDirectory 'staging-receipt.json'
    $preBootstrapManifestPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-pre-bootstrap.json'
    $manifestPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-quiescent.json'
    $deltaReportPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-bootstrap-delta.json'
    $afterResetManifestPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-after-library-reset.json'
    $resetPath = Join-Path $bootstrapDirectory 'library-reset.json'
    $failure = $null
    $preBootstrapManifest = $null
    $manifest = $null

    try {
        Assert-EditorClosed -ProjectRoot $ConsumerRoot
        Write-ConsumerJsonArtifact -Path $receiptPath -Value $StagingReceipt
        Assert-ConsumerStagingReceipt -ConsumerRoot $ConsumerRoot -Receipt $StagingReceipt
        $preBootstrapManifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $preBootstrapManifestPath -Value $preBootstrapManifest
        Assert-ConsumerImmutableManifestBaseline -Manifest $preBootstrapManifest -RunLabel 'bootstrap-pre'
        $arguments = @('-batchmode', '-force-d3d11', '-projectPath', $ConsumerRoot, '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', $ConsumerAssembly, '-testFilter', 'PureBase.Release.Consumer.Tests.PureBaseConsumerSceneTemplateBootstrapTests.DisposableSceneLifecycleMaterializesSceneTemplateSettings', '-testResults', $resultsPath, '-logFile', $unityLogPath)
        Write-ConsumerJsonArtifact -Path $commandPath -Value ([ordered]@{ executable = $UnityEditor; arguments = $arguments }) -Depth 4
        [System.IO.File]::WriteAllText($processLogPath, '', (New-Object System.Text.UTF8Encoding($false)))
        & $UnityEditor @arguments 2>&1 | Tee-Object -FilePath $processLogPath -Append | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Unity bootstrap import exited with $LASTEXITCODE. See '$processLogPath'." }
        $nunitSummary = Test-NUnitEvidence -ResultsPath $resultsPath -RunLabel 'bootstrap'
        Write-ConsumerJsonArtifact -Path $nunitSummaryPath -Value $nunitSummary

        $manifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $manifestPath -Value $manifest
        Write-ConsumerImmutableManifestBootstrapDelta -PreBootstrap $preBootstrapManifest -PostBootstrap $manifest -Path $deltaReportPath
        Assert-ConsumerImmutableManifestBaseline -Manifest $manifest -RunLabel 'bootstrap'

        $resetResult = Reset-ConsumerLibrary -ConsumerRoot $ConsumerRoot
        Write-ConsumerJsonArtifact -Path $resetPath -Value ([ordered]@{
                priorLibraryPresent = $resetResult.priorLibraryPresent
                libraryPresentAfterReset = $resetResult.libraryPresentAfterReset
            })
        if ($resetResult.libraryPresentAfterReset) {
            throw "Unity bootstrap import did not remove disposable Library: '$($resetResult.libraryPath)'."
        }

        $afterResetManifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $afterResetManifestPath -Value $afterResetManifest
        Assert-ConsumerImmutableManifest -Expected $manifest -Actual $afterResetManifest -RunLabel 'bootstrap-library-reset'
    }
    catch {
        $failure = $_
    }

    if ($null -ne $failure) {
        if ($null -eq $manifest) {
            try {
                $manifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
                Write-ConsumerJsonArtifact -Path $manifestPath -Value $manifest
                if ($null -ne $preBootstrapManifest) {
                    Write-ConsumerImmutableManifestBootstrapDelta -PreBootstrap $preBootstrapManifest -PostBootstrap $manifest -Path $deltaReportPath
                }
            }
            catch {
            }
        }
        try {
            Write-ConsumerFailureEvidence -RunDirectory $bootstrapDirectory -RunLabel 'bootstrap' -Failure $failure -EvidencePaths @($receiptPath, $preBootstrapManifestPath, $manifestPath, $deltaReportPath, $commandPath, $unityLogPath, $processLogPath, $resultsPath, $nunitSummaryPath, $afterResetManifestPath, $resetPath)
        }
        catch {
            throw "Consumer bootstrap import failed and its failure evidence could not be persisted. Original failure: $($failure.Exception.Message). Evidence failure: $($_.Exception.Message)"
        }
        throw $failure
    }

    return $manifest
}

function Copy-RegularTree {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Assert-RegularTree -Root $Source
    if (Test-Path -LiteralPath $Destination) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $Destination) -Force | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Recurse -Force
}

function Expand-ValidatedZip {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $destinationRoot = Get-NormalizedPath -Path $Destination
    New-Item -ItemType Directory -Path $destinationRoot -Force | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            $relative = $entry.FullName.Replace('/', '\\')
            if ($relative -match '(^|\\)\.\.?(\\|$)' -or [System.IO.Path]::IsPathRooted($relative) -or $relative -match '^[A-Za-z]:') {
                throw "Unsafe ZIP entry '$($entry.FullName)'."
            }
            if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
                throw "ZIP entry '$($entry.FullName)' is a symbolic link."
            }

            $target = [System.IO.Path]::GetFullPath((Join-Path $destinationRoot $relative))
            if (-not (Test-PathContainedBy -Path $target -ParentPath $destinationRoot)) {
                throw "ZIP entry '$($entry.FullName)' escapes its destination."
            }
            if ($entry.FullName.EndsWith('/')) {
                New-Item -ItemType Directory -Path $target -Force | Out-Null
                continue
            }

            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
    finally {
        $archive.Dispose()
    }
}

function New-ShaderCoreProductSelectionBlock {
    param(
        [Parameter(Mandatory = $true)][string]$ShaderName,
        [Parameter()][string[]]$Modules = @()
    )

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("  - shadername: $ShaderName")
    [void]$lines.Add('    modules:')
    if ($Modules.Count -eq 0) {
        [void]$lines.Add('    - ')
    }
    else {
        foreach ($module in $Modules) {
            [void]$lines.Add("    - $module")
        }
    }
    return ($lines -join "`n") + "`n"
}

function Write-ShaderCoreBaseline {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter()][hashtable]$Selections = @{}
    )

    $settingsPath = Join-Path $ConsumerRoot 'ProjectSettings/jp.lilxyzw.shadercore.asset'
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        throw "Shader-Core settings were not created by consumer bootstrap: '$settingsPath'."
    }

    $settingsText = Get-Content -LiteralPath $settingsPath -Raw
    if (-not [regex]::IsMatch($settingsText, '(?m)^  shaderSettings:\r?\n')) {
        throw "Shader-Core settings do not contain a shaderSettings collection: '$settingsPath'."
    }

    foreach ($product in $ProductNames) {
        $selectionBlock = New-ShaderCoreProductSelectionBlock -ShaderName $product -Modules @($Selections[$product])
        $productPattern = '(?ms)^  - shadername: ' + [regex]::Escape($product) + '\r?\n.*?(?=^  - shadername:|\z)'
        if ([regex]::IsMatch($settingsText, $productPattern)) {
            $replacement = [System.Text.RegularExpressions.MatchEvaluator] {
                param($match)
                return $selectionBlock
            }
            $settingsText = [regex]::Replace($settingsText, $productPattern, $replacement, 1)
        }
        else {
            if (-not $settingsText.EndsWith("`n")) {
                $settingsText += "`n"
            }
            $settingsText += $selectionBlock
        }
    }

    [System.IO.File]::WriteAllText($settingsPath, $settingsText, (New-Object System.Text.UTF8Encoding($false)))
}

function New-ProductContract {
    param(
        [Parameter(Mandatory = $true)][string]$ShaderName,
        [Parameter()][string]$Sentinel = '',
        [Parameter()][System.Collections.IDictionary]$PassSentinelCounts = $null
    )

    $passContracts = @()
    if ($null -ne $PassSentinelCounts) {
        if ($PassSentinelCounts.Count -ne $ProductPasses.Count) {
            throw "Product pass sentinel count contract for '$ShaderName' must enumerate every expected pass."
        }
        foreach ($passName in $ProductPasses) {
            if (-not $PassSentinelCounts.Contains($passName)) {
                throw "Product pass sentinel count contract for '$ShaderName' omits '$passName'."
            }
        }
    }
    foreach ($passIndex in 0..($ProductPasses.Count - 1)) {
        $passName = $ProductPasses[$passIndex]
        $nextPassName = if ($passIndex + 1 -lt $ProductPasses.Count) { $ProductPasses[$passIndex + 1] } else { '' }
        $selectedSentinelCount = if ($null -eq $PassSentinelCounts) { 0 } else { [int]$PassSentinelCounts[$passName] }
        if ($selectedSentinelCount -lt 0) {
            throw "Product pass sentinel count for '$ShaderName' pass '$passName' cannot be negative."
        }
        if ($selectedSentinelCount -gt 0 -and [string]::IsNullOrEmpty($Sentinel)) {
            throw "Product pass sentinel count for '$ShaderName' pass '$passName' requires a sentinel."
        }
        $passContracts += [ordered]@{
            passName = $passName
            nextPassName = $nextPassName
            requiredFragments = if ($selectedSentinelCount -gt 0) { @($Sentinel) } else { @() }
            forbiddenFragments = @()
            selectedSentinelCount = $selectedSentinelCount
        }
    }
    $expectedVisiblePropertyNames = switch ($ShaderName) {
        'PureBase/Unlit' { @('_BaseTexture', '_BaseColor', '_SharedMask', '_SharedGradients', '_Cutoff', '_Cull') }
        'PureBase/Toon' { @('_BaseTexture', '_BaseColor', '_SharedMask', '_SharedGradients', '_Cutoff', '_Cull', '_NormalMap', '_NormalScale') }
        'PureBase/PBR' { @('_BaseTexture', '_BaseColor', '_SharedMask', '_SharedGradients', '_Cutoff', '_Cull', '_NormalMap', '_NormalScale', '_Metallic', '_Roughness') }
        'PureBase/Hybrid' { @('_BaseTexture', '_BaseColor', '_SharedMask', '_SharedGradients', '_Cutoff', '_Cull', '_NormalMap', '_NormalScale', '_Metallic', '_Roughness') }
        default { throw "Unsupported PureBase product '$ShaderName'." }
    }
    return [ordered]@{
        shaderName = $ShaderName
        shaderAssetPath = Get-ProductShaderAssetPath -ShaderName $ShaderName
        expectedPassNames = $ProductPasses
        expectedVisiblePropertyNames = $expectedVisiblePropertyNames
        requiredSourceFragments = @()
        forbiddenSourceFragments = @()
        passContracts = $passContracts
    }
}

function Get-PhasePassSentinelCounts {
    param([Parameter(Mandatory = $true)][string]$Phase)

    $phaseIdentifier = $Phase.ToLowerInvariant()
    switch ($phaseIdentifier) {
        'morph' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 1; Meta = 0 } }
        'postvertex' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 1; Meta = 0 } }
        'base' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 1; Meta = 0 } }
        'light' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 0; Meta = 0 } }
        'customlight' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 0; Meta = 0 } }
        'modifylight' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 0; Meta = 0 } }
        'shade' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 0; Meta = 0 } }
        'reflection' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 0; Meta = 0 } }
        'add' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 0; Meta = 0 } }
        'postpixel' { return [ordered]@{ ForwardBase = 1; ForwardAdd = 1; ShadowCaster = 0; Meta = 0 } }
        default { throw "Unsupported product phase '$Phase'." }
    }
}

function Get-ShaderCoreNamespacedPropertyName {
    param(
        [Parameter(Mandatory = $true)][string]$ModuleUniqueId,
        [Parameter(Mandatory = $true)][string]$RawPropertyName
    )

    if ([string]::IsNullOrWhiteSpace($ModuleUniqueId) -or $ModuleUniqueId -notmatch '^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*$') {
        throw "Shader-Core module unique ID is malformed: '$ModuleUniqueId'."
    }
    if ([string]::IsNullOrWhiteSpace($RawPropertyName) -or $RawPropertyName -notmatch '^_[A-Za-z][A-Za-z0-9_]*$') {
        throw "Shader-Core raw property name is malformed: '$RawPropertyName'."
    }

    return '_' + $ModuleUniqueId.Replace('.', '_') + $RawPropertyName
}

function Get-ProductShaderAssetPath {
    param([Parameter(Mandatory = $true)][string]$ShaderName)

    $fileName = switch ($ShaderName) {
        'PureBase/Unlit' { 'PureBaseUnlit.scshader' }
        'PureBase/Toon' { 'PureBaseToon.scshader' }
        'PureBase/PBR' { 'PureBasePBR.scshader' }
        'PureBase/Hybrid' { 'PureBaseHybrid.scshader' }
        default { throw "Unsupported PureBase product '$ShaderName'." }
    }
    return 'Packages/jp.penguin.purebase/Shaders/' + $fileName
}

function New-ModuleFreeContract {
    return [ordered]@{
        runLabel = 'module-free-clean-import'
        runKind = 'module-free'
        hasSelectedModule = $false
        products = @($ProductNames | ForEach-Object { New-ProductContract -ShaderName $_ })
        selectedModule = $null
        moduleOrder = $null
        inactiveSentinels = $AllSentinels
        runtimeSamples = @()
        bake = $null
        unlitForwardAddFog = $null
    }
}

function New-PhaseContract {
    param(
        [Parameter(Mandatory = $true)]$Module,
        [Parameter(Mandatory = $true)][string[]]$SelectedProducts,
        [Parameter()][System.Collections.IDictionary]$PassSentinelCounts = $null
    )

    if ($null -eq $PassSentinelCounts) {
        $PassSentinelCounts = Get-PhasePassSentinelCounts -Phase $Module.phase
    }
    return [ordered]@{
        runLabel = $Module.label
        runKind = 'product-phase'
        hasSelectedModule = $true
        products = @($SelectedProducts | ForEach-Object { New-ProductContract -ShaderName $_ -Sentinel $Module.sentinel -PassSentinelCounts $PassSentinelCounts })
        selectedModule = [ordered]@{ phase = $Module.phase; moduleUniqueId = $Module.uniqueId; propertyName = $Module.propertyName; sentinel = $Module.sentinel }
        moduleOrder = $null
        inactiveSentinels = @($AllSentinels | Where-Object { $_ -ne $Module.sentinel })
        runtimeSamples = @()
        bake = $null
        unlitForwardAddFog = $null
    }
}

function New-ToonRuntimeContract {
    param([Parameter(Mandatory = $true)]$Module)

    $contract = New-PhaseContract -Module $Module -SelectedProducts @('PureBase/Toon')
    $contract.runLabel = $Module.label + '-runtime'
    $range = { param([double]$Minimum, [double]$Maximum) [ordered]@{ minimum = $Minimum; maximum = $Maximum } }
    $moduleFreeReference = [ordered]@{ red = 2.853515625; green = 2.8125; blue = 2.69921875; alpha = 1.0 }
    $runtimeRanges = switch ($Module.phase) {
        'base' { [ordered]@{ red = & $range 3.55 3.58; green = & $range 2.75 2.9; blue = & $range 2.65 2.75; alpha = & $range 0.99 1.01 } }
        'light' { [ordered]@{ red = & $range 2.8 2.9; green = & $range 2.92 3.9; blue = & $range 2.65 2.75; alpha = & $range 0.99 1.01 } }
        'modifylight' { [ordered]@{ red = & $range 2.8 2.9; green = & $range 2.75 2.9; blue = & $range 2.8 3.8; alpha = & $range 0.99 1.01 } }
        'shade' { [ordered]@{ red = & $range 2.95 3.9; green = & $range 2.8 3.4; blue = & $range 2.65 3.1; alpha = & $range 0.99 1.01 } }
        default { throw "Unsupported Toon runtime phase '$($Module.phase)'." }
    }
    $selectedMinusModuleFree = switch ($Module.phase) {
        'base' { [ordered]@{ red = & $range 0.70 0.73; green = & $range -0.1 0.1; blue = & $range -0.1 0.1; alpha = & $range -0.01 0.01 } }
        'light' { [ordered]@{ red = & $range -0.1 0.1; green = & $range 0.1 1.0; blue = & $range -0.1 0.1; alpha = & $range -0.01 0.01 } }
        'modifylight' { [ordered]@{ red = & $range -0.1 0.1; green = & $range -0.1 0.1; blue = & $range 0.1 1.0; alpha = & $range -0.01 0.01 } }
        'shade' { [ordered]@{ red = & $range 0.1 1.0; green = & $range -0.1 0.5; blue = & $range -0.1 0.3; alpha = & $range -0.01 0.01 } }
        default { throw "Unsupported Toon runtime phase '$($Module.phase)'." }
    }
    $contract.runtimeSamples = @([ordered]@{
            label = $Module.label
            shaderName = 'PureBase/Toon'
            shaderAssetPath = Get-ProductShaderAssetPath -ShaderName 'PureBase/Toon'
            floatAssignments = @()
            includePointLight = $true
            red = $runtimeRanges.red
            green = $runtimeRanges.green
            blue = $runtimeRanges.blue
            alpha = $runtimeRanges.alpha
        })
    $contract.runtimeDelta = [ordered]@{
        sampleLabel = $Module.label
        moduleFreeReference = $moduleFreeReference
        selectedMinusModuleFree = $selectedMinusModuleFree
    }
    return $contract
}

function New-ModuleOrderContract {
    return [ordered]@{
        runLabel = 'module-order'
        runKind = 'module-order'
        hasSelectedModule = $false
        products = @($ProductNames | ForEach-Object { New-ProductContract -ShaderName $_ })
        selectedModule = $null
        moduleOrder = [ordered]@{
            firstModuleName = 'Zeta'; secondModuleName = 'Alpha'
            firstSentinel = 'PUREBASE_MODULE_ORDER_ZETA'; secondSentinel = 'PUREBASE_MODULE_ORDER_ALPHA'
            presentPassNames = @('ForwardBase', 'ForwardAdd', 'ShadowCaster'); absentPassNames = @('Meta')
        }
        inactiveSentinels = @($AllSentinels | Where-Object { $_ -notin @('PUREBASE_MODULE_ORDER_ALPHA', 'PUREBASE_MODULE_ORDER_ZETA') })
        runtimeSamples = @(); bake = $null; unlitForwardAddFog = $null
    }
}

function New-FogContract {
    $moduleUniqueId = 'jp.penguin.purebase.integration.unlit.forwardaddfog'
    $rawPropertyName = '_ForwardAddFogSignalProperty'
    $product = New-ProductContract -ShaderName 'PureBase/Unlit'
    return [ordered]@{
        runLabel = 'unlit-forward-add-fog'
        runKind = 'unlit-forward-add-fog'
        hasSelectedModule = $false
        products = @($product)
        selectedModule = $null; moduleOrder = $null
        inactiveSentinels = @($AllSentinels | Where-Object { $_ -ne 'PUREBASE_UNLIT_FORWARD_ADD_FOG_SENTINEL' })
        runtimeSamples = @(); bake = $null
        unlitForwardAddFog = [ordered]@{
            product = $product
            moduleUniqueId = $moduleUniqueId
            sentinel = 'PUREBASE_UNLIT_FORWARD_ADD_FOG_SENTINEL'
            floatAssignments = @([ordered]@{ propertyName = Get-ShaderCoreNamespacedPropertyName -ModuleUniqueId $moduleUniqueId -RawPropertyName $rawPropertyName; value = 1.0 })
            fog = [ordered]@{ mode = 'Exponential'; color = [ordered]@{ red = 0.0; green = 0.0; blue = 0.0; alpha = 1.0 }; density = 3.0 }
            cameraFieldOfView = 60.0
            fogDisabledSignalMagnitude = [ordered]@{ minimum = 0.0001; maximum = 1000.0 }
            retainedSignalFraction = [ordered]@{ minimum = 0.0; maximum = 1.0 }
            blackFogRed = [ordered]@{ minimum = 0.0; maximum = 0.1 }
            blackFogGreen = [ordered]@{ minimum = 0.0; maximum = 0.1 }
            blackFogBlue = [ordered]@{ minimum = 0.0; maximum = 0.1 }
            blackFogAlpha = [ordered]@{ minimum = 0.0; maximum = 1.0 }
        }
    }
}

function Get-FixtureContract {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $fixtureRoot = Join-Path $ConsumerRoot 'Assets/ReleaseConsumer/Fixtures'
    $scenePath = 'Assets/ReleaseConsumer/Fixtures/Scenes/PureBaseValidation.unity'
    $lightingPath = 'Assets/ReleaseConsumer/Fixtures/Lighting/PureBaseValidationLightingSettings.lighting'
    $lightingMeta = Get-Content -LiteralPath (Join-Path $fixtureRoot 'Lighting/PureBaseValidationLightingSettings.lighting.meta') -Raw
    $lightingGuid = ([regex]::Match($lightingMeta, '(?m)^guid:\s*([0-9a-f]+)\s*$')).Groups[1].Value
    if ([string]::IsNullOrEmpty($lightingGuid)) { throw 'Staged Lighting Settings meta file does not provide a GUID.' }
    $sceneText = Get-Content -LiteralPath (Join-Path $fixtureRoot 'Scenes/PureBaseValidation.unity') -Raw
    $names = @([regex]::Matches($sceneText, '(?m)^  m_Name:\s*(.+?)\s*$') | ForEach-Object { $_.Groups[1].Value.Trim() } | Where-Object { $_ -ne '' })
    $cameraName = @($names | Where-Object { $_ -match 'Camera' } | Select-Object -First 1)[0]
    $rendererNames = @($names | Where-Object { $_ -match 'Unlit|Toon|PBR|Hybrid' } | Select-Object -Unique)
    if ([string]::IsNullOrEmpty($cameraName) -or $rendererNames.Count -eq 0) {
        throw 'Staged validation scene does not expose the expected camera and product renderer names.'
    }
    $metaReadbacks = @()
    foreach ($product in $ProductNames) {
        $shortName = $product.Split('/')[-1]
        $materialPath = Get-ChildItem -LiteralPath (Join-Path $fixtureRoot 'Materials') -Filter '*.mat' | Where-Object { $_.BaseName -match [regex]::Escape($shortName) } | Select-Object -First 1
        if ($null -eq $materialPath) { throw "Staged fixture has no material for '$product'." }
        $materialText = Get-Content -LiteralPath $materialPath.FullName -Raw
        $materialName = ([regex]::Match($materialText, '(?m)^  m_Name:\s*(.+?)\s*$')).Groups[1].Value.Trim()
        if ([string]::IsNullOrEmpty($materialName)) { throw "Fixture material '$($materialPath.Name)' has no m_Name." }
        $metaReadbacks += [ordered]@{ materialName = $materialName; shaderName = $product; meanLuminance = [ordered]@{ minimum = 0.00001; maximum = 1000.0 } }
    }
    return [ordered]@{ scenePath = $scenePath; cameraName = $cameraName; requiredStaticRendererNames = @($rendererNames | Select-Object -First 4); minimumLightmapCount = 1; minimumVisiblePixelCount = 1; lightingSettingsPath = $lightingPath; lightingSettingsGuid = $lightingGuid; lightmapper = 'ProgressiveCPU'; bakedGi = $true; realtimeGi = $false; autoGenerate = $false; metaReadbacks = $metaReadbacks; shadowEvidence = [ordered]@{ materialName = $metaReadbacks[0].materialName; shaderName = $metaReadbacks[0].shaderName; minimumChangedPixelCount = 1; screenshotFileName = 'shadow-evidence.png' }; variantWarmups = @(New-VariantWarmups); expectedVariantWarmupCount = 56 }
}

function New-VariantWarmups {
    $requests = @()
    foreach ($product in $ProductNames) {
        $assetPath = Get-ProductShaderAssetPath -ShaderName $product
        foreach ($passType in @('ForwardBase', 'ForwardBase', 'ForwardBase', 'ForwardBase', 'ForwardAdd', 'ForwardAdd', 'ForwardAdd', 'ForwardAdd', 'ShadowCaster', 'ShadowCaster', 'ShadowCaster', 'Meta', 'Meta', 'Meta')) {
            $requests += [ordered]@{ label = "$product-$passType-$($requests.Count)"; shaderName = $product; shaderAssetPath = $assetPath; passType = $passType; keywords = @() }
        }
    }
    return $requests
}

function New-BakeContract {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    return [ordered]@{ runLabel = 'progressive-cpu-bake'; runKind = 'bake'; hasSelectedModule = $false; products = @($ProductNames | ForEach-Object { New-ProductContract -ShaderName $_ }); selectedModule = $null; moduleOrder = $null; inactiveSentinels = $AllSentinels; runtimeSamples = @(); bake = Get-FixtureContract -ConsumerRoot $ConsumerRoot; unlitForwardAddFog = $null }
}

function Select-ValidationMatrix {
    param(
        [Parameter(Mandatory = $true)][object[]]$Matrix,
        [Parameter()][switch]$FogOnly,
        [Parameter()][switch]$BakeOnly
    )

    if ($BakeOnly) {
        $selectedMatrix = @($Matrix | Where-Object { $_.label -eq 'progressive-cpu-bake' })
        return ,$selectedMatrix
    }
    if ($FogOnly) {
        $selectedMatrix = @($Matrix | Where-Object { $_.label -eq 'unlit-forward-add-fog' })
        return ,$selectedMatrix
    }
    $selectedMatrix = @($Matrix)
    return ,$selectedMatrix
}

function Write-ConsumerContract {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)]$Contract
    )

    $contractPath = Join-Path $ConsumerRoot 'Assets/ReleaseConsumer/PureBaseConsumerValidationContract.json'
    $Contract | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $contractPath -Encoding UTF8
}

function Test-NUnitEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$ResultsPath,
        [Parameter(Mandatory = $true)][string]$RunLabel
    )

    if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) { throw "Unity run '$RunLabel' produced no NUnit XML." }
    [xml]$results = Get-Content -LiteralPath $ResultsPath -Raw
    $run = $results.SelectSingleNode('/test-run')
    if ($null -eq $run) { throw "Unity run '$RunLabel' produced invalid NUnit XML." }
    $total = [int]$run.GetAttribute('total')
    $failed = [int]$run.GetAttribute('failed')
    $skipped = [int]$run.GetAttribute('skipped')
    $inconclusive = [int]$run.GetAttribute('inconclusive')
    if ($run.GetAttribute('result') -ne 'Passed' -or $total -le 0 -or $failed -ne 0 -or $skipped -ne 0 -or $inconclusive -ne 0) {
        throw "Unity run '$RunLabel' did not pass cleanly: total=$total failed=$failed skipped=$skipped inconclusive=$inconclusive."
    }
    return [ordered]@{ total = $total; passed = [int]$run.GetAttribute('passed'); failed = $failed; skipped = $skipped; inconclusive = $inconclusive }
}

function Get-NUnitFailureDetail {
    param([Parameter(Mandatory = $true)][string]$ResultsPath)

    if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
        return ''
    }
    try {
        [xml]$results = Get-Content -LiteralPath $ResultsPath -Raw
        $failure = $results.SelectSingleNode('//test-case[@result="Failed"]/failure')
        if ($null -eq $failure) {
            return ''
        }
        $message = $failure.SelectSingleNode('message')
        if ($null -eq $message -or [string]::IsNullOrWhiteSpace($message.InnerText)) {
            return ''
        }
        return $message.InnerText.Trim()
    }
    catch {
        return ''
    }
}

function Get-ExpectedGeneratedSourceArtifactFileName {
    param(
        [Parameter(Mandatory = $true)][string]$RunLabel,
        [Parameter(Mandatory = $true)][string]$ShaderName
    )

    $sanitize = {
        param([string]$Value)

        $builder = New-Object System.Text.StringBuilder
        foreach ($character in $Value.ToCharArray()) {
            if ([char]::IsLetterOrDigit($character)) {
                [void]$builder.Append($character)
            }
            else {
                [void]$builder.Append('-')
            }
        }
        if ($builder.Length -eq 0) { return 'consumer' }
        return $builder.ToString()
    }
    return (& $sanitize $RunLabel) + '-' + (& $sanitize $ShaderName) + '-generated-source.txt'
}

function Get-StandardMorphObservationClassification {
    param([Parameter(Mandatory = $true)][int[]]$PassCounts)

    if ($PassCounts.Count -ne $ProductPasses.Count) {
        return 'invalid'
    }
    if (($PassCounts[0] -eq 1) -and ($PassCounts[1] -eq 1) -and ($PassCounts[2] -eq 1) -and ($PassCounts[3] -eq 0)) {
        return 'canonical'
    }
    if (($PassCounts[0] -eq 2) -and ($PassCounts[1] -eq 2) -and ($PassCounts[2] -eq 2) -and ($PassCounts[3] -eq 0)) {
        return 'known-duplicate'
    }
    return 'invalid'
}

function Assert-ExactJsonPropertyNames {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string[]]$ExpectedNames,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $actualNames = @($Value.PSObject.Properties | ForEach-Object { $_.Name })
    if ($actualNames.Count -ne $ExpectedNames.Count) {
        throw "$Description has an unexpected property set."
    }
    foreach ($expectedName in $ExpectedNames) {
        if ($actualNames -notcontains $expectedName) {
            throw "$Description is missing required property '$expectedName'."
        }
    }
}

function ConvertTo-NonNegativeObservationCount {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if (($Value -isnot [int]) -and ($Value -isnot [long])) {
        throw "$Description must be an integer."
    }
    if ($Value -lt 0 -or $Value -gt [int]::MaxValue) {
        throw "$Description must be a non-negative 32-bit integer."
    }
    return [int]$Value
}

function Read-StandardMorphObservationEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$RunDirectory,
        [Parameter(Mandatory = $true)]$Contract
    )

    $evidenceDirectory = Join-Path $RunDirectory 'consumer-evidence'
    $observationPath = Join-Path $evidenceDirectory 'standard-morph-observation.json'
    if (-not (Test-Path -LiteralPath $observationPath -PathType Leaf)) {
        throw "Standard-morph observation evidence is missing: '$observationPath'."
    }
    try {
        $observation = Get-Content -LiteralPath $observationPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Standard-morph observation evidence is invalid JSON: '$observationPath'."
    }
    Assert-ExactJsonPropertyNames -Value $observation -ExpectedNames @('schemaName', 'schemaVersion', 'runLabel', 'runKind', 'selectedModulePhase', 'selectedModuleUniqueId', 'selectedModuleSentinel', 'products') -Description 'Standard-morph observation evidence'
    if ($observation.schemaName -ne 'purebase-standard-morph-observation' -or [int]$observation.schemaVersion -ne 1) {
        throw "Standard-morph observation evidence has an unsupported schema: '$observationPath'."
    }
    if ($observation.runLabel -ne $Contract.runLabel -or $observation.runKind -ne 'product-phase') {
        throw "Standard-morph observation evidence does not identify run '$($Contract.runLabel)'."
    }
    if ($null -eq $observation.products -or @($observation.products).Count -ne $ProductNames.Count) {
        throw "Standard-morph observation evidence must contain exactly $($ProductNames.Count) products."
    }

    $products = New-Object System.Collections.Generic.List[object]
    $observedNames = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::Ordinal)
    foreach ($productObservation in @($observation.products)) {
        if ($null -eq $productObservation -or [string]::IsNullOrWhiteSpace([string]$productObservation.shaderName)) {
            throw 'Standard-morph observation evidence contains a product without shaderName.'
        }
        Assert-ExactJsonPropertyNames -Value $productObservation -ExpectedNames @('shaderName', 'compiled', 'supported', 'generatedSourceArtifactFileName', 'passCounts', 'inactiveSentinels') -Description 'Standard-morph observation product'
        $shaderName = [string]$productObservation.shaderName
        if (-not $observedNames.Add($shaderName) -or $ProductNames -notcontains $shaderName) {
            throw "Standard-morph observation evidence contains an unknown or duplicate product '$shaderName'."
        }
        if ($productObservation.compiled -isnot [bool] -or $productObservation.supported -isnot [bool] -or -not $productObservation.compiled -or -not $productObservation.supported) {
            throw "Standard-morph observation product '$shaderName' must report compiled=true and supported=true."
        }
        $expectedGeneratedSourceFileName = Get-ExpectedGeneratedSourceArtifactFileName -RunLabel $Contract.runLabel -ShaderName $shaderName
        if ([string]$productObservation.generatedSourceArtifactFileName -ne $expectedGeneratedSourceFileName) {
            throw "Standard-morph observation product '$shaderName' has an unexpected generated-source filename."
        }
        $generatedSourcePath = Join-Path $evidenceDirectory $expectedGeneratedSourceFileName
        if (-not (Test-Path -LiteralPath $generatedSourcePath -PathType Leaf)) {
            throw "Standard-morph observation product '$shaderName' is missing generated-source evidence '$expectedGeneratedSourceFileName'."
        }
        if ($null -eq $productObservation.passCounts -or @($productObservation.passCounts).Count -ne $ProductPasses.Count) {
            throw "Standard-morph observation product '$shaderName' must contain exactly $($ProductPasses.Count) ordered pass counts."
        }
        $passCounts = New-Object System.Collections.Generic.List[int]
        for ($passIndex = 0; $passIndex -lt $ProductPasses.Count; $passIndex++) {
            $passObservation = $productObservation.passCounts[$passIndex]
            if ($null -eq $passObservation) {
                throw "Standard-morph observation product '$shaderName' contains a null pass observation."
            }
            Assert-ExactJsonPropertyNames -Value $passObservation -ExpectedNames @('passName', 'selectedSentinelCount') -Description "Standard-morph observation product '$shaderName' pass"
            if ($null -eq $passObservation -or [string]$passObservation.passName -ne $ProductPasses[$passIndex]) {
                throw "Standard-morph observation product '$shaderName' has an invalid pass order."
            }
            [void]$passCounts.Add((ConvertTo-NonNegativeObservationCount -Value $passObservation.selectedSentinelCount -Description "Standard-morph observation product '$shaderName' selected sentinel count"))
        }
        if ($null -eq $productObservation.inactiveSentinels -or @($productObservation.inactiveSentinels).Count -ne @($Contract.inactiveSentinels).Count) {
            throw "Standard-morph observation product '$shaderName' does not contain the required inactive-sentinel observations."
        }
        $inactiveSentinels = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::Ordinal)
        for ($inactiveIndex = 0; $inactiveIndex -lt @($Contract.inactiveSentinels).Count; $inactiveIndex++) {
            $inactiveObservation = $productObservation.inactiveSentinels[$inactiveIndex]
            if ($null -eq $inactiveObservation) {
                throw "Standard-morph observation product '$shaderName' contains a null inactive-sentinel observation."
            }
            Assert-ExactJsonPropertyNames -Value $inactiveObservation -ExpectedNames @('sentinel', 'occurrenceCount') -Description "Standard-morph observation product '$shaderName' inactive sentinel"
            if ([string]$inactiveObservation.sentinel -ne [string]$Contract.inactiveSentinels[$inactiveIndex] -or -not $inactiveSentinels.Add([string]$inactiveObservation.sentinel) -or (ConvertTo-NonNegativeObservationCount -Value $inactiveObservation.occurrenceCount -Description "Standard-morph observation product '$shaderName' inactive sentinel count") -ne 0) {
                throw "Standard-morph observation product '$shaderName' has invalid inactive-sentinel evidence."
            }
        }
        [void]$products.Add([ordered]@{
                productName = $shaderName
                generatedSourceFileName = $expectedGeneratedSourceFileName
                passCounts = $passCounts.ToArray()
                warmClassification = Get-StandardMorphObservationClassification -PassCounts $passCounts.ToArray()
            })
    }
    if ($observedNames.Count -ne $ProductNames.Count) {
        throw 'Standard-morph observation evidence does not contain the expected product set.'
    }
    return $products.ToArray()
}

function Get-GeneratedSourcePassCounts {
    param(
        [Parameter(Mandatory = $true)][string]$GeneratedSource,
        [Parameter(Mandatory = $true)][string]$Sentinel,
        [Parameter(Mandatory = $true)][string]$ProductName,
        [Parameter(Mandatory = $true)][string]$RunLabel
    )

    $counts = New-Object System.Collections.Generic.List[int]
    for ($passIndex = 0; $passIndex -lt $ProductPasses.Count; $passIndex++) {
        $startMarker = 'Name "' + $ProductPasses[$passIndex] + '"'
        $start = $GeneratedSource.IndexOf($startMarker, [System.StringComparison]::Ordinal)
        if ($start -lt 0) {
            throw "Consumer run '$RunLabel' product '$ProductName' generated source is missing pass '$($ProductPasses[$passIndex])'."
        }
        $end = $GeneratedSource.Length
        if ($passIndex + 1 -lt $ProductPasses.Count) {
            $endMarker = 'Name "' + $ProductPasses[$passIndex + 1] + '"'
            $end = $GeneratedSource.IndexOf($endMarker, $start + $startMarker.Length, [System.StringComparison]::Ordinal)
            if ($end -le $start) {
                throw "Consumer run '$RunLabel' product '$ProductName' generated source has invalid pass order."
            }
        }
        $passSource = $GeneratedSource.Substring($start, $end - $start)
        $count = 0
        $offset = 0
        while (($offset = $passSource.IndexOf($Sentinel, $offset, [System.StringComparison]::Ordinal)) -ge 0) {
            $count++
            $offset += $Sentinel.Length
        }
        [void]$counts.Add($count)
    }
    return $counts.ToArray()
}

function Read-StandardMorphColdEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$RunDirectory,
        [Parameter(Mandatory = $true)]$Contract
    )

    $evidenceDirectory = Join-Path $RunDirectory 'consumer-evidence'
    $products = New-Object System.Collections.Generic.List[object]
    foreach ($productName in $ProductNames) {
        $generatedSourceFileName = Get-ExpectedGeneratedSourceArtifactFileName -RunLabel $Contract.runLabel -ShaderName $productName
        $generatedSourcePath = Join-Path $evidenceDirectory $generatedSourceFileName
        if (-not (Test-Path -LiteralPath $generatedSourcePath -PathType Leaf)) {
            throw "Standard-morph cold evidence is missing generated source '$generatedSourceFileName'."
        }
        $passCounts = Get-GeneratedSourcePassCounts -GeneratedSource (Get-Content -LiteralPath $generatedSourcePath -Raw) -Sentinel $Contract.selectedModule.sentinel -ProductName $productName -RunLabel $Contract.runLabel
        [void]$products.Add([ordered]@{
                productName = $productName
                generatedSourceFileName = $generatedSourceFileName
                passCounts = $passCounts
                coldClassification = Get-StandardMorphObservationClassification -PassCounts $passCounts
            })
    }
    return $products.ToArray()
}

function Assert-ModuleFreeComparisonEvidence {
    param([Parameter(Mandatory = $true)][string]$RunRoot)

    $runLabel = 'module-free-clean-import'
    $evidenceDirectory = Join-Path $RunRoot ('runs/' + $runLabel + '/consumer-evidence')
    foreach ($productName in $ProductNames) {
        $generatedSourceFileName = Get-ExpectedGeneratedSourceArtifactFileName -RunLabel $runLabel -ShaderName $productName
        $generatedSourcePath = Join-Path $evidenceDirectory $generatedSourceFileName
        if (-not (Test-Path -LiteralPath $generatedSourcePath -PathType Leaf)) {
            throw "Module-free comparison evidence is missing generated source '$generatedSourceFileName'."
        }
        $source = Get-Content -LiteralPath $generatedSourcePath -Raw
        foreach ($sentinel in $AllSentinels) {
            if ($source.IndexOf($sentinel, [System.StringComparison]::Ordinal) -ge 0) {
                throw "Module-free comparison evidence product '$productName' retained sentinel '$sentinel'."
            }
        }
    }
}

function Invoke-StandardMorphComparisonVerdict {
    param(
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [Parameter(Mandatory = $true)]$WarmContract,
        [Parameter(Mandatory = $true)]$ColdContract
    )

    $verdictPath = Join-Path $RunRoot 'standard-morph-comparison-verdict.json'
    $warmRunDirectory = Join-Path $RunRoot ('runs/' + $WarmContract.runLabel)
    $coldRunDirectory = Join-Path $RunRoot ('runs/' + $ColdContract.runLabel)
    $verdict = [ordered]@{
        schemaName = 'purebase-standard-morph-comparison-verdict'
        schemaVersion = 1
        comparisonName = 'warm-cold-standard-morph'
        moduleFreeRunPath = 'runs/module-free-clean-import'
        warmRunPath = 'runs/' + $WarmContract.runLabel
        coldRunPath = 'runs/' + $ColdContract.runLabel
        status = 'failed'
        products = @()
    }
    try {
        Assert-ModuleFreeComparisonEvidence -RunRoot $RunRoot
        $warmProducts = Read-StandardMorphObservationEvidence -RunDirectory $warmRunDirectory -Contract $WarmContract
        $coldProducts = Read-StandardMorphColdEvidence -RunDirectory $coldRunDirectory -Contract $ColdContract
        $coldByName = @{}
        foreach ($coldProduct in $coldProducts) {
            $coldByName.Add($coldProduct.productName, $coldProduct)
        }
        $products = New-Object System.Collections.Generic.List[object]
        foreach ($warmProduct in $warmProducts) {
            $coldProduct = $coldByName[$warmProduct.productName]
            if ($null -eq $coldProduct) {
                throw "Standard-morph cold evidence omitted product '$($warmProduct.productName)'."
            }
            $coldClassification = $coldProduct.coldClassification
            [void]$products.Add([ordered]@{
                    productName = $warmProduct.productName
                    warmClassification = $warmProduct.warmClassification
                    coldClassification = $coldClassification
                    coldCanonical = [bool]($coldClassification -eq 'canonical')
                })
        }
        $verdict.products = $products.ToArray()
        if (@($verdict.products | Where-Object { $_.warmClassification -eq 'invalid' }).Count -ne 0) {
            throw 'Standard-morph warm evidence contains an invalid product classification.'
        }
        if (@($verdict.products | Where-Object { -not $_.coldCanonical }).Count -ne 0) {
            throw 'Standard-morph cold evidence is not canonical for every product.'
        }
        $verdict.status = 'passed'
        Write-ConsumerJsonArtifact -Path $verdictPath -Value $verdict
        return $verdict
    }
    catch {
        $verdict.failure = $_.Exception.Message
        try {
            Write-ConsumerJsonArtifact -Path $verdictPath -Value $verdict
            Write-ConsumerJsonArtifact -Path (Join-Path $RunRoot 'standard-morph-comparison-failure.json') -Value ([ordered]@{ message = $_.Exception.Message; verdictPath = (Split-Path -Leaf $verdictPath) })
        }
        catch {
            throw "Standard-morph comparison failed and its external failure evidence could not be persisted. Original failure: $($verdict.failure). Evidence failure: $($_.Exception.Message)"
        }
        throw
    }
}

function Invoke-ConsumerTest {
    param(
        [Parameter(Mandatory = $true)][string]$UnityEditor,
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$ShaderCoreManifestPath,
        [Parameter(Mandatory = $true)]$Contract,
        [Parameter(Mandatory = $true)][string]$TestFilter,
        [Parameter()][hashtable]$Selections = @{},
        [Parameter()][switch]$SkipColdLibraryReset,
        [Parameter()][switch]$AllowObservationEvidence
    )

    $runDirectory = Join-Path $RunRoot ('runs/' + $Contract.runLabel)
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $requiresColdLibraryReset = $Selections.Count -gt 0 -and -not $SkipColdLibraryReset
    $resultsPath = Join-Path $runDirectory 'NUnit.xml'
    $unityLogPath = Join-Path $runDirectory 'Unity.log'
    $processLogPath = Join-Path $runDirectory 'Process.log'
    $internalArtifactPath = 'Artifacts/' + $Contract.runLabel
    $beforeManifestPath = Join-Path $runDirectory 'immutable-input-manifest-before.json'
    $afterResetManifestPath = Join-Path $runDirectory 'immutable-input-manifest-after-reset.json'
    $afterManifestPath = Join-Path $runDirectory 'immutable-input-manifest-after.json'
    $contractPath = Join-Path $runDirectory 'selected-module-contract.json'
    $resetPath = Join-Path $runDirectory 'library-reset.json'
    $commandPath = Join-Path $runDirectory 'unity-command.json'
    $nunitSummaryPath = Join-Path $runDirectory 'nunit-summary.json'
    $resetEvidence = [ordered]@{
        required = [bool]$requiresColdLibraryReset
        attempted = $false
        completed = $false
        resetCount = [int]$script:coldLibraryResetCount
        priorLibraryPresent = $null
        libraryPresentAfterReset = $null
    }
    Write-ConsumerJsonArtifact -Path $contractPath -Value ([ordered]@{ contract = $Contract; selections = $Selections; skipColdLibraryReset = [bool]$SkipColdLibraryReset }) -Depth 12
    Write-ConsumerJsonArtifact -Path $resetPath -Value $resetEvidence

    $immutableManifestBefore = $null
    $failure = $null
    $summary = $null
    try {
        if ($AllowObservationEvidence -and $TestFilter -ne 'PureBase.Release.Consumer.Tests.PureBaseConsumerStandardMorphObservationTests.StandardMorphProductsRecordPassCountObservations') {
            throw 'Observation evidence bypass is restricted to the standard-morph observation test.'
        }
        Assert-EditorClosed -ProjectRoot $ConsumerRoot
        Write-ShaderCoreBaseline -ConsumerRoot $ConsumerRoot -Selections $Selections
        Write-ConsumerContract -ConsumerRoot $ConsumerRoot -Contract $Contract
        $immutableManifestBefore = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $beforeManifestPath -Value $immutableManifestBefore
        Assert-ConsumerImmutableManifestBaseline -Manifest $immutableManifestBefore -RunLabel $Contract.runLabel
        if ($requiresColdLibraryReset) {
            $resetEvidence.attempted = $true
            Write-ConsumerJsonArtifact -Path $resetPath -Value $resetEvidence
            $resetResult = Reset-ConsumerLibrary -ConsumerRoot $ConsumerRoot
            $script:coldLibraryResetCount++
            $resetEvidence.completed = $true
            $resetEvidence.resetCount = [int]$script:coldLibraryResetCount
            $resetEvidence.priorLibraryPresent = $resetResult.priorLibraryPresent
            $resetEvidence.libraryPresentAfterReset = $resetResult.libraryPresentAfterReset
            Write-ConsumerJsonArtifact -Path $resetPath -Value $resetEvidence
            $immutableManifestAfterReset = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
            Write-ConsumerJsonArtifact -Path $afterResetManifestPath -Value $immutableManifestAfterReset
            Assert-ConsumerImmutableManifest -Expected $immutableManifestBefore -Actual $immutableManifestAfterReset -RunLabel $Contract.runLabel
        }

        $env:PUREBASE_CONSUMER_ARTIFACTS_DIRECTORY = $internalArtifactPath
        $arguments = @('-batchmode', '-force-d3d11', '-projectPath', $ConsumerRoot, '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', $ConsumerAssembly, '-testFilter', $TestFilter, '-testResults', $resultsPath, '-logFile', $unityLogPath)
        Write-ConsumerJsonArtifact -Path $commandPath -Value ([ordered]@{ executable = $UnityEditor; arguments = $arguments }) -Depth 4
        [System.IO.File]::WriteAllText($processLogPath, '', (New-Object System.Text.UTF8Encoding($false)))
        & $UnityEditor @arguments 2>&1 | Tee-Object -FilePath $processLogPath -Append | Out-Host
        if ($LASTEXITCODE -ne 0) {
            $nunitFailureDetail = Get-NUnitFailureDetail -ResultsPath $resultsPath
            $nunitFailureSuffix = if ([string]::IsNullOrEmpty($nunitFailureDetail)) { '' } else { " NUnit failure: $nunitFailureDetail" }
            throw "Unity run '$($Contract.runLabel)' exited with $LASTEXITCODE.$nunitFailureSuffix See '$processLogPath'."
        }
        $summary = Test-NUnitEvidence -ResultsPath $resultsPath -RunLabel $Contract.runLabel
        Write-ConsumerJsonArtifact -Path $nunitSummaryPath -Value $summary
    }
    catch {
        $failure = $_
    }

    try {
        $immutableManifestAfter = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $afterManifestPath -Value $immutableManifestAfter
        if ($null -ne $immutableManifestBefore) {
            Assert-ConsumerImmutableManifest -Expected $immutableManifestBefore -Actual $immutableManifestAfter -RunLabel $Contract.runLabel
        }
    }
    catch {
        if ($null -eq $failure) {
            $failure = $_
        }
    }

    try {
        $internalArtifacts = Join-Path $ConsumerRoot $internalArtifactPath
        if (Test-Path -LiteralPath $internalArtifacts) {
            Copy-RegularTree -Source $internalArtifacts -Destination (Join-Path $runDirectory 'consumer-evidence')
        }
    }
    catch {
        if ($null -eq $failure) {
            $failure = $_
        }
    }

    if ($null -ne $failure) {
        try {
            Write-ConsumerFailureEvidence -RunDirectory $runDirectory -RunLabel $Contract.runLabel -Failure $failure -EvidencePaths @($contractPath, $resetPath, $beforeManifestPath, $afterResetManifestPath, $afterManifestPath, $commandPath, $nunitSummaryPath, $resultsPath, $unityLogPath, $processLogPath)
        }
        catch {
            throw "Consumer run '$($Contract.runLabel)' failed and its failure evidence could not be persisted. Original failure: $($failure.Exception.Message). Evidence failure: $($_.Exception.Message)"
        }
        throw $failure
    }

    return $summary
}

function Write-ReleaseRunSummary {
    param(
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [Parameter(Mandatory = $true)][int]$ConsumerCreated,
        [Parameter(Mandatory = $true)][int]$ConsumerRemoved,
        [Parameter(Mandatory = $true)][string]$ValidationScope,
        [Parameter(Mandatory = $true)][bool]$ComparisonMode,
        [Parameter(Mandatory = $true)][bool]$ModuleFreeOnly,
        [Parameter(Mandatory = $true)][object[]]$Outcomes,
        [Parameter()]$ComparisonVerdict
    )

    [ordered]@{ consumerDirectoryCreationCount = $ConsumerCreated; consumerDirectoryRemovalCount = $ConsumerRemoved; coldLibraryResetCount = $script:coldLibraryResetCount; validationScope = $ValidationScope; comparisonMode = $ComparisonMode; moduleFreeOnly = $ModuleFreeOnly; outcomes = $Outcomes; comparisonVerdict = $ComparisonVerdict } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $RunRoot 'run-summary.json') -Encoding UTF8
}

function Write-ReleaseCleanupSummary {
    param(
        [Parameter(Mandatory = $true)][string]$RunRoot,
        [Parameter(Mandatory = $true)][int]$ConsumerCreated,
        [Parameter(Mandatory = $true)][int]$ConsumerRemoved,
        [Parameter(Mandatory = $true)][bool]$KeepConsumer,
        [Parameter(Mandatory = $true)][bool]$Failed
    )

    [ordered]@{ consumerDirectoryCreationCount = $ConsumerCreated; consumerDirectoryRemovalCount = $ConsumerRemoved; coldLibraryResetCount = $script:coldLibraryResetCount; keepConsumer = $KeepConsumer; failed = $Failed } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunRoot 'cleanup-summary.json') -Encoding UTF8
}

$packageRoot = Get-PackageGitRoot
if ($ModuleFreeOnly -and $CompareWarmAndColdStandardMorph) {
    throw '-ModuleFreeOnly cannot be combined with -CompareWarmAndColdStandardMorph because the latter requires the three-row standard-morph warm/cold comparison.'
}
if ($ToonBaseOnly -and $CompareWarmAndColdStandardMorph) {
    throw '-ToonBaseOnly cannot be combined with -CompareWarmAndColdStandardMorph because the latter requires the three-row standard-morph warm/cold comparison.'
}
if ($ToonBaseOnly -and $ModuleFreeOnly) {
    throw '-ToonBaseOnly cannot be combined with -ModuleFreeOnly because it requires the Toon base product-phase row.'
}
if ($FogOnly -and $ModuleFreeOnly) {
    throw '-FogOnly cannot be combined with -ModuleFreeOnly because it requires the Unlit ForwardAdd fog row.'
}
if ($FogOnly -and $ToonBaseOnly) {
    throw '-FogOnly cannot be combined with -ToonBaseOnly because it requires the Unlit ForwardAdd fog row.'
}
if ($FogOnly -and $CompareWarmAndColdStandardMorph) {
    throw '-FogOnly cannot be combined with -CompareWarmAndColdStandardMorph because it requires the Unlit ForwardAdd fog row.'
}
if ($BakeOnly -and $ModuleFreeOnly) {
    throw '-BakeOnly cannot be combined with -ModuleFreeOnly because it requires the progressive-cpu-bake row.'
}
if ($BakeOnly -and $ToonBaseOnly) {
    throw '-BakeOnly cannot be combined with -ToonBaseOnly because it requires the progressive-cpu-bake row.'
}
if ($BakeOnly -and $FogOnly) {
    throw '-BakeOnly cannot be combined with -FogOnly because it requires the progressive-cpu-bake row.'
}
if ($BakeOnly -and $CompareWarmAndColdStandardMorph) {
    throw '-BakeOnly cannot be combined with -CompareWarmAndColdStandardMorph because it requires the progressive-cpu-bake row.'
}

$workspaceRoot = Get-NormalizedPath -Path (Split-Path -Parent (Split-Path -Parent $packageRoot))
$scriptRoot = $PSScriptRoot
$unityEditor = Resolve-UnityEditor -Path $UnityEditorPath
Assert-EditorClosed -ProjectRoot $workspaceRoot
$artifactBase = if ([string]::IsNullOrWhiteSpace($ArtifactDirectory)) { Join-Path ([System.IO.Path]::GetTempPath()) 'PureBase.Release.Validation' } else { $ArtifactDirectory }
$artifactBase = Assert-ExternalDirectory -Path $artifactBase -WorkspaceRoot $workspaceRoot
New-Item -ItemType Directory -Path $artifactBase -Force | Out-Null
$runRoot = Join-Path $artifactBase ('ReleaseConsumer-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $PID)
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

$consumerCreated = 0
$consumerRemoved = 0
$script:coldLibraryResetCount = 0
$consumerRoot = Join-Path $runRoot 'ConsumerProject'
$failed = $true
try {
    $archiveDirectory = Join-Path $runRoot 'archive'
    & (Join-Path $scriptRoot 'Build-PureBaseRelease.ps1') -OutputDirectory $archiveDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Approved release archive builder failed.' }
    $zipPath = Join-Path $archiveDirectory 'jp.penguin.purebase-0.1.0.zip'
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw 'Approved release archive builder did not produce the expected ZIP.' }
    $shaderCoreManifestPath = Join-Path $runRoot 'shader-core-0.1.5.sha256.json'
    Copy-Item -LiteralPath (Join-Path $scriptRoot 'shader-core-0.1.5.sha256.json') -Destination $shaderCoreManifestPath -Force

    $scaffoldRoot = Join-Path $scriptRoot 'ConsumerProject'
    Copy-RegularTree -Source $scaffoldRoot -Destination $consumerRoot
    $consumerCreated++
    $localPackages = Join-Path $consumerRoot '_LocalPackages'
    Expand-ValidatedZip -ZipPath $zipPath -Destination (Join-Path $localPackages 'jp.penguin.purebase')
    $shaderCoreRoot = Join-Path $workspaceRoot 'Packages/jp.lilxyzw.shadercore'
    Assert-RegularTree -Root $shaderCoreRoot
    # The approved builder verified this exact local Shader-Core tree before this copy.
    Copy-RegularTree -Source $shaderCoreRoot -Destination (Join-Path $localPackages 'jp.lilxyzw.shadercore')
    Copy-RegularTree -Source (Join-Path $scriptRoot 'Modules') -Destination (Join-Path $consumerRoot 'Assets/ReleaseModules')
    Copy-RegularTree -Source (Join-Path $packageRoot 'Tests/Fixtures') -Destination (Join-Path $consumerRoot 'Assets/ReleaseConsumer/Fixtures')
    $stagingReceipt = Get-ConsumerStagingReceipt -ZipPath $zipPath -ScaffoldRoot $scaffoldRoot -ShaderCoreRoot $shaderCoreRoot -ModulesRoot (Join-Path $scriptRoot 'Modules') -FixturesRoot (Join-Path $packageRoot 'Tests/Fixtures')
    $bootstrapManifest = Invoke-ConsumerBootstrapImport -UnityEditor $unityEditor -ConsumerRoot $consumerRoot -RunRoot $runRoot -ZipPath $zipPath -ShaderCoreManifestPath $shaderCoreManifestPath -StagingReceipt $stagingReceipt

    $comparisonWarmContract = $null
    $comparisonColdContract = $null
    $comparisonVerdict = $null
    $matrix = New-Object System.Collections.Generic.List[object]
    $matrix.Add([ordered]@{ label = 'module-free-clean-import'; contract = New-ModuleFreeContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerModuleFreeImportTests.ModuleFreeProductsCompileWithConfiguredPassPropertyAndSourceContracts'; selections = @{}; skipColdLibraryReset = $false })
    if (-not $ModuleFreeOnly) {
        $standardPhases = @('morph', 'postvertex', 'base', 'light', 'customlight', 'modifylight', 'shade', 'reflection', 'add', 'postpixel')
        if ($ToonBaseOnly) {
            $moduleUniqueId = 'jp.penguin.purebase.integration.toon.phase.base'
            $rawPropertyName = '_ProductPhaseValue'
            $module = [ordered]@{ label = 'toon-base'; phase = 'base'; uniqueId = $moduleUniqueId; propertyName = Get-ShaderCoreNamespacedPropertyName -ModuleUniqueId $moduleUniqueId -RawPropertyName $rawPropertyName; sentinel = 'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_BASE' }
            $matrix.Add([ordered]@{ label = $module.label + '-phase'; contract = New-PhaseContract -Module $module -SelectedProducts @('PureBase/Toon'); filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerProductPhaseTests.SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage'; selections = @{ 'PureBase/Toon' = @($module.uniqueId) }; skipColdLibraryReset = $false })
            $matrix.Add([ordered]@{ label = $module.label + '-runtime'; contract = New-ToonRuntimeContract -Module $module; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerRuntimeTests.ConfiguredRuntimeSamplesProduceExpectedBirpReadbacks'; selections = @{ 'PureBase/Toon' = @($module.uniqueId) }; skipColdLibraryReset = $false })
        }
        elseif ($CompareWarmAndColdStandardMorph) {
            $module = [ordered]@{ label = 'standard-morph'; phase = 'morph'; uniqueId = 'jp.penguin.purebase.integration.products.morph'; propertyName = ''; sentinel = 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_MORPH' }
            $selections = @{ 'PureBase/Unlit' = @($module.uniqueId); 'PureBase/Toon' = @($module.uniqueId); 'PureBase/PBR' = @($module.uniqueId); 'PureBase/Hybrid' = @($module.uniqueId) }
            $warmContract = New-PhaseContract -Module $module -SelectedProducts $ProductNames
            $warmContract.runLabel = 'standard-morph-warm-library-duplicate-evidence'
            $comparisonWarmContract = $warmContract
            $matrix.Add([ordered]@{ label = $warmContract.runLabel; contract = $warmContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerStandardMorphObservationTests.StandardMorphProductsRecordPassCountObservations'; selections = $selections; skipColdLibraryReset = $true; allowObservationEvidence = $true })
            $coldContract = New-PhaseContract -Module $module -SelectedProducts $ProductNames
            $coldContract.runLabel = 'standard-morph-cold-library-legacy-counts'
            $comparisonColdContract = $coldContract
            $matrix.Add([ordered]@{ label = $coldContract.runLabel; contract = $coldContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerProductPhaseTests.SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage'; selections = $selections; skipColdLibraryReset = $false; allowObservationEvidence = $false })
        }
        else {
            foreach ($phase in $standardPhases) {
                $module = [ordered]@{ label = 'standard-' + $phase; phase = $phase; uniqueId = 'jp.penguin.purebase.integration.products.' + $phase; propertyName = ''; sentinel = 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_' + $phase.ToUpperInvariant() }
                $matrix.Add([ordered]@{ label = $module.label; contract = New-PhaseContract -Module $module -SelectedProducts $ProductNames; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerProductPhaseTests.SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage'; selections = @{ 'PureBase/Unlit' = @($module.uniqueId); 'PureBase/Toon' = @($module.uniqueId); 'PureBase/PBR' = @($module.uniqueId); 'PureBase/Hybrid' = @($module.uniqueId) }; skipColdLibraryReset = $false })
            }
        }
        if (-not $ToonBaseOnly -and -not $CompareWarmAndColdStandardMorph) {
            foreach ($phase in @('base', 'light', 'modifylight', 'shade')) {
                $moduleUniqueId = 'jp.penguin.purebase.integration.toon.phase.' + $phase
                $rawPropertyName = '_ProductPhaseValue'
                $module = [ordered]@{ label = 'toon-' + $phase; phase = $phase; uniqueId = $moduleUniqueId; propertyName = Get-ShaderCoreNamespacedPropertyName -ModuleUniqueId $moduleUniqueId -RawPropertyName $rawPropertyName; sentinel = 'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_' + $phase.ToUpperInvariant() }
                $phaseContract = New-PhaseContract -Module $module -SelectedProducts @('PureBase/Toon')
                $runtimeContract = New-ToonRuntimeContract -Module $module
                $matrix.Add([ordered]@{ label = $module.label + '-phase'; contract = $phaseContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerProductPhaseTests.SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage'; selections = @{ 'PureBase/Toon' = @($module.uniqueId) }; skipColdLibraryReset = $false })
                $matrix.Add([ordered]@{ label = $module.label + '-runtime'; contract = $runtimeContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerRuntimeTests.ConfiguredRuntimeSamplesProduceExpectedBirpReadbacks'; selections = @{ 'PureBase/Toon' = @($module.uniqueId) }; skipColdLibraryReset = $false })
            }
            $matrix.Add([ordered]@{ label = 'unlit-forward-add-fog'; contract = New-FogContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerUnlitForwardAddFogTests.SelectedForwardAddSignalAttenuatesTowardBlackWithControlledFog'; selections = @{ 'PureBase/Unlit' = @('jp.penguin.purebase.integration.unlit.forwardaddfog') }; skipColdLibraryReset = $false })
            $matrix.Add([ordered]@{ label = 'module-order'; contract = New-ModuleOrderContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerModuleOrderTests.ConfiguredModuleOrderAppearsOnlyInExpectedProductPasses'; selections = @{ 'PureBase/Unlit' = @('jp.penguin.purebase.integration.module-order.alpha', 'jp.penguin.purebase.integration.module-order.zeta'); 'PureBase/Toon' = @('jp.penguin.purebase.integration.module-order.alpha', 'jp.penguin.purebase.integration.module-order.zeta'); 'PureBase/PBR' = @('jp.penguin.purebase.integration.module-order.alpha', 'jp.penguin.purebase.integration.module-order.zeta'); 'PureBase/Hybrid' = @('jp.penguin.purebase.integration.module-order.alpha', 'jp.penguin.purebase.integration.module-order.zeta') }; skipColdLibraryReset = $false })
            $matrix.Add([ordered]@{ label = 'progressive-cpu-bake'; contract = New-BakeContract -ConsumerRoot $consumerRoot; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerBakeEvidenceTests.ConfiguredValidationSceneBakesAndExportsEvidence'; selections = @{}; skipColdLibraryReset = $false })
        }
    }

    $matrix = Select-ValidationMatrix -Matrix $matrix -FogOnly:$FogOnly -BakeOnly:$BakeOnly
    if ($BakeOnly -and ($matrix.Count -ne 1 -or $matrix[0].label -ne 'progressive-cpu-bake')) {
        throw 'Bake-only validation must select exactly the progressive-cpu-bake matrix row.'
    }

    $outcomes = @()
    foreach ($entry in $matrix) {
        $allowObservationEvidence = $false
        if ($entry.Contains('allowObservationEvidence')) {
            $allowObservationEvidence = [bool]$entry.allowObservationEvidence
        }
        $outcomes += [ordered]@{ label = $entry.label; nunit = Invoke-ConsumerTest -UnityEditor $unityEditor -ConsumerRoot $consumerRoot -RunRoot $runRoot -ZipPath $zipPath -ShaderCoreManifestPath $shaderCoreManifestPath -Contract $entry.contract -TestFilter $entry.filter -Selections $entry.selections -SkipColdLibraryReset:$entry.skipColdLibraryReset -AllowObservationEvidence:$allowObservationEvidence }
    }
    if ($CompareWarmAndColdStandardMorph) {
        if ($matrix.Count -ne 3 -or $null -eq $comparisonWarmContract -or $null -eq $comparisonColdContract) {
            throw 'Standard-morph comparison must execute exactly module-free, warm, and cold rows.'
        }
        $comparisonVerdict = Invoke-StandardMorphComparisonVerdict -RunRoot $runRoot -WarmContract $comparisonWarmContract -ColdContract $comparisonColdContract
    }
    $validationScope = if ($ModuleFreeOnly) { 'module-free-diagnostic-only' } elseif ($ToonBaseOnly) { 'toon-base-diagnostic-only' } elseif ($FogOnly) { 'unlit-forward-add-fog-diagnostic-only' } elseif ($BakeOnly) { 'progressive-cpu-bake-diagnostic-only' } elseif ($CompareWarmAndColdStandardMorph) { 'warm-cold-standard-morph-comparison' } else { 'full-release-validation-matrix' }
    Write-ReleaseRunSummary -RunRoot $runRoot -ConsumerCreated $consumerCreated -ConsumerRemoved $consumerRemoved -ValidationScope $validationScope -ComparisonMode ([bool]$CompareWarmAndColdStandardMorph) -ModuleFreeOnly ([bool]$ModuleFreeOnly) -Outcomes $outcomes -ComparisonVerdict $comparisonVerdict
    $failed = $false
}
finally {
    if (-not $KeepConsumer -and (Test-Path -LiteralPath $consumerRoot)) {
        Assert-EditorClosed -ProjectRoot $consumerRoot
        Remove-Item -LiteralPath $consumerRoot -Recurse -Force
        $consumerRemoved++
    }
    Write-ReleaseCleanupSummary -RunRoot $runRoot -ConsumerCreated $consumerCreated -ConsumerRemoved $consumerRemoved -KeepConsumer ([bool]$KeepConsumer) -Failed $failed
}

if ($consumerCreated -ne 1 -or ((-not $KeepConsumer) -and $consumerRemoved -ne 1)) {
    throw "Consumer lifecycle contract failed: created=$consumerCreated removed=$consumerRemoved."
}

Write-Host "Pure-Base release consumer validation passed. Artifacts: '$runRoot'."
