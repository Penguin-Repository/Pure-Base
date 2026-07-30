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
$RequiredUnityRevision = '887be4894c44'
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

function Get-ConsumerUnityProcess {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $projectPattern = [regex]::Escape((Get-NormalizedPath -Path $ProjectRoot))
    try {
        $unityProcesses = @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction Stop)
    }
    catch {
        return [pscustomobject][ordered]@{
            status  = 'indeterminate'
            process = $null
            reason  = "Could not query Unity processes: $($_.Exception.Message)"
        }
    }

    $unreadableProcesses = New-Object System.Collections.Generic.List[object]
    foreach ($unityProcess in $unityProcesses) {
        $commandLine = if ($null -eq $unityProcess.CommandLine) { '' } else { [string]$unityProcess.CommandLine }
        if ([string]::IsNullOrWhiteSpace($commandLine)) {
            [void]$unreadableProcesses.Add($unityProcess)
            continue
        }
        if ($commandLine -match $projectPattern) {
            return [pscustomobject][ordered]@{
                status  = 'active'
                process = $unityProcess
                reason  = "Unity process $($unityProcess.ProcessId) is using '$ProjectRoot'."
            }
        }
    }

    if ($unreadableProcesses.Count -ne 0) {
        $processIds = @($unreadableProcesses | ForEach-Object { [string]$_.ProcessId }) -join ', '
        return [pscustomobject][ordered]@{
            status  = 'indeterminate'
            process = $null
            reason  = "Cannot inspect CommandLine for Unity process(es) $processIds; refusing to rule out '$ProjectRoot'."
        }
    }

    return [pscustomobject][ordered]@{
        status  = 'none'
        process = $null
        reason  = "No Unity process is using '$ProjectRoot'."
    }
}

function Assert-EditorClosed {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $lockPath = Join-Path $ProjectRoot 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $lockPath -PathType Leaf) {
        throw "Unity project '$ProjectRoot' appears open. Close the Unity Editor before running this validation."
    }

    $processDiscovery = Get-ConsumerUnityProcess -ProjectRoot $ProjectRoot
    if ($processDiscovery.status -eq 'active') {
        throw $processDiscovery.reason
    }
    if ($processDiscovery.status -ne 'none') {
        throw "Cannot verify whether a Unity process is using '$ProjectRoot'; refusing to continue. $($processDiscovery.reason)"
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
        schemaVersion               = 1
        pathOrdering                = 'System.StringComparer.Ordinal'
        immutableRoots              = $rootNames
        excludedMutablePathPrefixes = @('Assets/Artifacts/', 'Library/')
        rootSha256                  = Get-Sha256Hex -Bytes ([System.Text.Encoding]::UTF8.GetBytes($manifestLines))
        entries                     = $entries
        releaseZipSha256            = Get-Sha256Hex -Path $ZipPath
        shaderCore                  = [ordered]@{
            packageName            = $shaderCorePackage.name
            packageVersion         = $shaderCorePackage.version
            expectedIdentitySha256 = [string]$expectedShaderCoreManifest.identitySha256
            treeSha256             = $shaderCoreTreeHash
        }
    }
}

function Add-ConsumerStagingReceiptTreeEntries {
    param(
        [Parameter(Mandatory = $true)][hashtable]$EntriesByDestination,
        [Parameter(Mandatory = $true)][string]$SourceRoot,
        [Parameter(Mandatory = $true)][AllowEmptyString()][ValidateScript({ $null -ne $_ -and $_ -is [string] })][object]$DestinationPrefix,
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
                sourceKind  = $SourceKind
                source      = $file.FullName
                sha256      = Get-Sha256Hex -Path $file.FullName
            })
    }
}

function Get-ConsumerStagingReceipt {
    param(
        [Parameter(Mandatory = $true)][string]$ZipPath,
        [Parameter(Mandatory = $true)][string]$ScaffoldRoot,
        [Parameter(Mandatory = $true)][string]$ShaderCoreRoot,
        [Parameter(Mandatory = $true)][string]$ModulesRoot,
        [Parameter(Mandatory = $true)][string]$FixturesRoot,
        [Parameter(Mandatory = $true)][string]$CanonicalShaderCoreConfigPath
    )

    $entriesByDestination = @{}
    Add-ConsumerStagingReceiptTreeEntries -EntriesByDestination $entriesByDestination -SourceRoot $ScaffoldRoot -DestinationPrefix '' -SourceKind 'consumer-scaffold'
    Add-ConsumerStagingReceiptTreeEntries -EntriesByDestination $entriesByDestination -SourceRoot $ShaderCoreRoot -DestinationPrefix '_LocalPackages/jp.lilxyzw.shadercore' -SourceKind 'shader-core-tree'
    Add-ConsumerStagingReceiptTreeEntries -EntriesByDestination $entriesByDestination -SourceRoot $ModulesRoot -DestinationPrefix 'Assets/ReleaseModules' -SourceKind 'release-modules'
    Add-ConsumerStagingReceiptTreeEntries -EntriesByDestination $entriesByDestination -SourceRoot $FixturesRoot -DestinationPrefix 'Assets/ReleaseConsumer/Fixtures' -SourceKind 'release-fixtures'
    $canonicalConfigDestination = Get-CanonicalShaderCoreConfigDestination
    if (-not (Test-Path -LiteralPath $CanonicalShaderCoreConfigPath -PathType Leaf)) {
        throw "Canonical Shader-Core config source is missing: '$CanonicalShaderCoreConfigPath'."
    }
    if ($entriesByDestination.ContainsKey($canonicalConfigDestination)) {
        throw "Staging receipt source mappings overlap at '$canonicalConfigDestination'."
    }
    $entriesByDestination.Add($canonicalConfigDestination, [ordered]@{
            destination = $canonicalConfigDestination
            sourceKind  = 'workspace-canonical-shader-core-config'
            source      = $CanonicalShaderCoreConfigPath
            sha256      = Get-Sha256Hex -Path $CanonicalShaderCoreConfigPath
        })

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
                    sourceKind  = 'release-zip-entry'
                    source      = $entry.FullName
                    sha256      = $hash
                })
        }
    }
    finally {
        $archive.Dispose()
    }

    $destinations = Get-OrdinalSortedStrings -Values ([string[]]@($entriesByDestination.Keys))
    return [ordered]@{
        schemaName    = 'purebase-consumer-staging-receipt'
        schemaVersion = 1
        pathOrdering  = 'System.StringComparer.Ordinal'
        entries       = @($destinations | ForEach-Object { $entriesByDestination[$_] })
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
        schemaName              = 'purebase-immutable-manifest-bootstrap-delta'
        schemaVersion           = 1
        classification          = 'observed'
        pathOrdering            = 'System.StringComparer.Ordinal'
        preBootstrapRootSha256  = [string]$PreBootstrap.rootSha256
        postBootstrapRootSha256 = [string]$PostBootstrap.rootSha256
        added                   = $added.ToArray()
        removed                 = $removed.ToArray()
        changed                 = $changed.ToArray()
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

function Get-ExpectedFirstBootstrapAddedPaths {
    return @(
        'Assets/ReleaseConsumer/Fixtures.meta', 'Assets/ReleaseModules.meta', 'Assets/Resources.meta', 'Assets/Resources/BillingMode.json', 'Assets/Resources/BillingMode.json.meta', 'Packages/packages-lock.json',
        'ProjectSettings/AudioManager.asset', 'ProjectSettings/ClusterInputManager.asset', 'ProjectSettings/DynamicsManager.asset', 'ProjectSettings/EditorBuildSettings.asset', 'ProjectSettings/EditorSettings.asset', 'ProjectSettings/GraphicsSettings.asset', 'ProjectSettings/InputManager.asset', 'ProjectSettings/MemorySettings.asset', 'ProjectSettings/NavMeshAreas.asset', 'ProjectSettings/Physics2DSettings.asset', 'ProjectSettings/PresetManager.asset', 'ProjectSettings/ProjectSettings.asset', 'ProjectSettings/SceneTemplateSettings.json', 'ProjectSettings/TagManager.asset', 'ProjectSettings/TimeManager.asset', 'ProjectSettings/UnityConnectSettings.asset', 'ProjectSettings/VFXManager.asset', 'ProjectSettings/VersionControlSettings.asset', 'ProjectSettings/jp.lilxyzw.shadercore.asset',
        '_LocalPackages/jp.penguin.purebase/Editor.meta', '_LocalPackages/jp.penguin.purebase/LICENSE.meta', '_LocalPackages/jp.penguin.purebase/NOTICE.meta', '_LocalPackages/jp.penguin.purebase/README.md.meta', '_LocalPackages/jp.penguin.purebase/Shaders.meta', '_LocalPackages/jp.penguin.purebase/package.json.meta'
    )
}

function Get-ExpectedFirstBootstrapChangedPaths {
    return @(
        'Packages/manifest.json', 'ProjectSettings/ProjectVersion.txt'
    )
}

function Get-FirstBootstrapGeneratedMetaProfile {
    return [ordered]@{
        'Assets/ReleaseConsumer/Fixtures.meta'                 = [ordered]@{ relatedPath = 'Assets/ReleaseConsumer/Fixtures'; itemType = 'Directory' }
        'Assets/ReleaseModules.meta'                           = [ordered]@{ relatedPath = 'Assets/ReleaseModules'; itemType = 'Directory' }
        'Assets/Resources.meta'                                = [ordered]@{ relatedPath = 'Assets/Resources'; itemType = 'Directory' }
        'Assets/Resources/BillingMode.json.meta'               = [ordered]@{ relatedPath = 'Assets/Resources/BillingMode.json'; itemType = 'Leaf' }
        '_LocalPackages/jp.penguin.purebase/Editor.meta'       = [ordered]@{ relatedPath = '_LocalPackages/jp.penguin.purebase/Editor'; itemType = 'Directory' }
        '_LocalPackages/jp.penguin.purebase/LICENSE.meta'      = [ordered]@{ relatedPath = '_LocalPackages/jp.penguin.purebase/LICENSE'; itemType = 'Leaf' }
        '_LocalPackages/jp.penguin.purebase/NOTICE.meta'       = [ordered]@{ relatedPath = '_LocalPackages/jp.penguin.purebase/NOTICE'; itemType = 'Leaf' }
        '_LocalPackages/jp.penguin.purebase/README.md.meta'    = [ordered]@{ relatedPath = '_LocalPackages/jp.penguin.purebase/README.md'; itemType = 'Leaf' }
        '_LocalPackages/jp.penguin.purebase/Shaders.meta'      = [ordered]@{ relatedPath = '_LocalPackages/jp.penguin.purebase/Shaders'; itemType = 'Directory' }
        '_LocalPackages/jp.penguin.purebase/package.json.meta' = [ordered]@{ relatedPath = '_LocalPackages/jp.penguin.purebase/package.json'; itemType = 'Leaf' }
    }
}

function Get-FirstBootstrapPackageProfile {
    $manifestDependencies = [ordered]@{}
    foreach ($entry in @'
com.unity.2d.sprite|1.0.0
com.unity.2d.tilemap|1.0.0
com.unity.ads|4.4.2
com.unity.ai.navigation|1.1.5
com.unity.analytics|3.8.1
com.unity.collab-proxy|2.3.1
com.unity.ide.rider|3.0.28
com.unity.ide.visualstudio|2.0.22
com.unity.ide.vscode|1.2.5
com.unity.purchasing|4.9.3
com.unity.test-framework|1.1.33
com.unity.textmeshpro|3.0.6
com.unity.timeline|1.7.6
com.unity.ugui|1.0.0
com.unity.xr.legacyinputhelpers|2.1.10
jp.lilxyzw.shadercore|file:../_LocalPackages/jp.lilxyzw.shadercore
jp.penguin.purebase|file:../_LocalPackages/jp.penguin.purebase
com.unity.modules.ai|1.0.0
com.unity.modules.androidjni|1.0.0
com.unity.modules.animation|1.0.0
com.unity.modules.assetbundle|1.0.0
com.unity.modules.audio|1.0.0
com.unity.modules.cloth|1.0.0
com.unity.modules.director|1.0.0
com.unity.modules.imageconversion|1.0.0
com.unity.modules.imgui|1.0.0
com.unity.modules.jsonserialize|1.0.0
com.unity.modules.particlesystem|1.0.0
com.unity.modules.physics|1.0.0
com.unity.modules.physics2d|1.0.0
com.unity.modules.screencapture|1.0.0
com.unity.modules.terrain|1.0.0
com.unity.modules.terrainphysics|1.0.0
com.unity.modules.tilemap|1.0.0
com.unity.modules.ui|1.0.0
com.unity.modules.uielements|1.0.0
com.unity.modules.umbra|1.0.0
com.unity.modules.unityanalytics|1.0.0
com.unity.modules.unitywebrequest|1.0.0
com.unity.modules.unitywebrequestassetbundle|1.0.0
com.unity.modules.unitywebrequestaudio|1.0.0
com.unity.modules.unitywebrequesttexture|1.0.0
com.unity.modules.unitywebrequestwww|1.0.0
com.unity.modules.vehicles|1.0.0
com.unity.modules.video|1.0.0
com.unity.modules.vr|1.0.0
com.unity.modules.wind|1.0.0
com.unity.modules.xr|1.0.0
'@ -split "`r?`n" | Where-Object { $_ -ne '' }) {
        $parts = $entry.Split('|', 2)
        $manifestDependencies.Add($parts[0], $parts[1])
    }

    $lockDependencies = [ordered]@{}
    foreach ($entry in @'
com.unity.2d.sprite|1.0.0|0|builtin|
com.unity.2d.tilemap|1.0.0|0|builtin|com.unity.modules.tilemap=1.0.0,com.unity.modules.uielements=1.0.0
com.unity.ads|4.4.2|0|registry|com.unity.ugui=1.0.0
com.unity.ai.navigation|1.1.5|0|registry|com.unity.modules.ai=1.0.0
com.unity.analytics|3.8.1|0|registry|com.unity.services.analytics=1.0.4,com.unity.ugui=1.0.0
com.unity.collab-proxy|2.3.1|0|registry|
com.unity.ext.nunit|1.0.6|1|registry|
com.unity.ide.rider|3.0.28|0|registry|com.unity.ext.nunit=1.0.6
com.unity.ide.visualstudio|2.0.22|0|registry|com.unity.test-framework=1.1.9
com.unity.ide.vscode|1.2.5|0|registry|
com.unity.modules.ai|1.0.0|0|builtin|
com.unity.modules.androidjni|1.0.0|0|builtin|
com.unity.modules.animation|1.0.0|0|builtin|
com.unity.modules.assetbundle|1.0.0|0|builtin|
com.unity.modules.audio|1.0.0|0|builtin|
com.unity.modules.cloth|1.0.0|0|builtin|com.unity.modules.physics=1.0.0
com.unity.modules.director|1.0.0|0|builtin|com.unity.modules.animation=1.0.0,com.unity.modules.audio=1.0.0
com.unity.modules.imageconversion|1.0.0|0|builtin|
com.unity.modules.imgui|1.0.0|0|builtin|
com.unity.modules.jsonserialize|1.0.0|0|builtin|
com.unity.modules.particlesystem|1.0.0|0|builtin|
com.unity.modules.physics|1.0.0|0|builtin|
com.unity.modules.physics2d|1.0.0|0|builtin|
com.unity.modules.screencapture|1.0.0|0|builtin|com.unity.modules.imageconversion=1.0.0
com.unity.modules.subsystems|1.0.0|1|builtin|com.unity.modules.jsonserialize=1.0.0
com.unity.modules.terrain|1.0.0|0|builtin|
com.unity.modules.terrainphysics|1.0.0|0|builtin|com.unity.modules.physics=1.0.0,com.unity.modules.terrain=1.0.0
com.unity.modules.tilemap|1.0.0|0|builtin|com.unity.modules.physics2d=1.0.0
com.unity.modules.ui|1.0.0|0|builtin|
com.unity.modules.uielements|1.0.0|0|builtin|com.unity.modules.imgui=1.0.0,com.unity.modules.jsonserialize=1.0.0,com.unity.modules.ui=1.0.0
com.unity.modules.umbra|1.0.0|0|builtin|
com.unity.modules.unityanalytics|1.0.0|0|builtin|com.unity.modules.jsonserialize=1.0.0,com.unity.modules.unitywebrequest=1.0.0
com.unity.modules.unitywebrequest|1.0.0|0|builtin|
com.unity.modules.unitywebrequestassetbundle|1.0.0|0|builtin|com.unity.modules.assetbundle=1.0.0,com.unity.modules.unitywebrequest=1.0.0
com.unity.modules.unitywebrequestaudio|1.0.0|0|builtin|com.unity.modules.audio=1.0.0,com.unity.modules.unitywebrequest=1.0.0
com.unity.modules.unitywebrequesttexture|1.0.0|0|builtin|com.unity.modules.imageconversion=1.0.0,com.unity.modules.unitywebrequest=1.0.0
com.unity.modules.unitywebrequestwww|1.0.0|0|builtin|com.unity.modules.assetbundle=1.0.0,com.unity.modules.audio=1.0.0,com.unity.modules.imageconversion=1.0.0,com.unity.modules.unitywebrequest=1.0.0,com.unity.modules.unitywebrequestassetbundle=1.0.0,com.unity.modules.unitywebrequestaudio=1.0.0
com.unity.modules.vehicles|1.0.0|0|builtin|com.unity.modules.physics=1.0.0
com.unity.modules.video|1.0.0|0|builtin|com.unity.modules.audio=1.0.0,com.unity.modules.ui=1.0.0,com.unity.modules.unitywebrequest=1.0.0
com.unity.modules.vr|1.0.0|0|builtin|com.unity.modules.jsonserialize=1.0.0,com.unity.modules.physics=1.0.0,com.unity.modules.xr=1.0.0
com.unity.modules.wind|1.0.0|0|builtin|
com.unity.modules.xr|1.0.0|0|builtin|com.unity.modules.jsonserialize=1.0.0,com.unity.modules.physics=1.0.0,com.unity.modules.subsystems=1.0.0
com.unity.nuget.newtonsoft-json|3.2.1|1|registry|
com.unity.purchasing|4.9.3|0|registry|com.unity.modules.androidjni=1.0.0,com.unity.modules.jsonserialize=1.0.0,com.unity.modules.unityanalytics=1.0.0,com.unity.modules.unitywebrequest=1.0.0,com.unity.services.core=1.8.1,com.unity.ugui=1.0.0
com.unity.services.analytics|5.0.0|1|registry|com.unity.modules.jsonserialize=1.0.0,com.unity.services.core=1.10.1,com.unity.ugui=1.0.0
com.unity.services.core|1.12.4|1|registry|com.unity.modules.androidjni=1.0.0,com.unity.modules.unitywebrequest=1.0.0,com.unity.nuget.newtonsoft-json=3.2.1
com.unity.test-framework|1.1.33|0|registry|com.unity.ext.nunit=1.0.6,com.unity.modules.imgui=1.0.0,com.unity.modules.jsonserialize=1.0.0
com.unity.textmeshpro|3.0.6|0|registry|com.unity.ugui=1.0.0
com.unity.timeline|1.7.6|0|registry|com.unity.modules.animation=1.0.0,com.unity.modules.audio=1.0.0,com.unity.modules.director=1.0.0,com.unity.modules.particlesystem=1.0.0
com.unity.ugui|1.0.0|0|builtin|com.unity.modules.imgui=1.0.0,com.unity.modules.ui=1.0.0
com.unity.xr.legacyinputhelpers|2.1.10|0|registry|com.unity.modules.vr=1.0.0,com.unity.modules.xr=1.0.0
jp.lilxyzw.shadercore|file:../_LocalPackages/jp.lilxyzw.shadercore|0|local|com.unity.nuget.newtonsoft-json=3.0.0
jp.penguin.purebase|file:../_LocalPackages/jp.penguin.purebase|0|local|
'@ -split "`r?`n" | Where-Object { $_ -ne '' }) {
        $parts = $entry.Split('|', 5)
        $dependencies = [ordered]@{}
        if (-not [string]::IsNullOrEmpty($parts[4])) {
            foreach ($edge in $parts[4].Split(',')) {
                $edgeParts = $edge.Split('=', 2)
                $dependencies.Add($edgeParts[0], $edgeParts[1])
            }
        }
        $lockDependencies.Add($parts[0], [ordered]@{ version = $parts[1]; depth = [int]$parts[2]; source = $parts[3]; dependencies = $dependencies })
    }

    return [ordered]@{
        unityVersion         = $RequiredUnityVersion
        unityRevision        = $RequiredUnityRevision
        manifestDependencies = $manifestDependencies
        lockDependencies     = $lockDependencies
    }
}

function Get-FirstBootstrapProjectSettingsProfile {
    return [ordered]@{
        'ProjectSettings/AudioManager.asset'           = [ordered]@{ root = 'AudioManager'; requiredLines = @('  serializedVersion: 2', '  m_Volume: 1', '  m_SampleRate: 0') }
        'ProjectSettings/ClusterInputManager.asset'    = [ordered]@{ root = 'ClusterInputManager'; requiredLines = @('  m_Inputs: []') }
        'ProjectSettings/DynamicsManager.asset'        = [ordered]@{ root = 'PhysicsManager'; requiredLines = @('  serializedVersion: 14', '  m_Gravity: {x: 0, y: -9.81, z: 0}', '  m_DefaultSolverIterations: 6') }
        'ProjectSettings/EditorBuildSettings.asset'    = [ordered]@{ root = 'EditorBuildSettings'; requiredLines = @('  serializedVersion: 2', '  m_Scenes: []') }
        'ProjectSettings/EditorSettings.asset'         = [ordered]@{ root = 'EditorSettings'; requiredLines = @('  serializedVersion: 12', '  m_SerializationMode: 2', '  m_LineEndingsForNewScripts: 2') }
        'ProjectSettings/GraphicsSettings.asset'       = [ordered]@{ root = 'GraphicsSettings'; requiredLines = @('  serializedVersion: 15', '  m_AlwaysIncludedShaders:', '  - {fileID: 7, guid: 0000000000000000f000000000000000, type: 0}', '  m_DefaultRenderingPath: 1') }
        'ProjectSettings/InputManager.asset'           = [ordered]@{ root = 'InputManager'; requiredLines = @('  serializedVersion: 2', '  m_Axes:', '  - serializedVersion: 3', '    m_Name: Horizontal') }
        'ProjectSettings/MemorySettings.asset'         = [ordered]@{ root = 'MemorySettings'; requiredLines = @('  m_PlatformMemorySettings: {}', '  m_EditorMemorySettings:', '    m_MainAllocatorBlockSize: -1') }
        'ProjectSettings/NavMeshAreas.asset'           = [ordered]@{ root = 'NavMeshProjectSettings'; requiredLines = @('  serializedVersion: 2', '  areas:', '  - name: Walkable', '    cost: 1') }
        'ProjectSettings/Physics2DSettings.asset'      = [ordered]@{ root = 'Physics2DSettings'; requiredLines = @('  serializedVersion: 6', '  m_Gravity: {x: 0, y: -9.81}', '  m_VelocityIterations: 8') }
        'ProjectSettings/PresetManager.asset'          = [ordered]@{ root = 'PresetManager'; requiredLines = @('  serializedVersion: 2', '  m_DefaultPresets: {}') }
        'ProjectSettings/ProjectSettings.asset'        = [ordered]@{ root = 'PlayerSettings'; requiredLines = @('  serializedVersion: 26', '  companyName: DefaultCompany', '  productName: ConsumerProject', '  defaultScreenWidth: 1920', '  defaultScreenHeight: 1080', '  m_ActiveColorSpace: 0', '  bundleVersion: 1.0') }
        'ProjectSettings/TagManager.asset'             = [ordered]@{ root = 'TagManager'; requiredLines = @('  serializedVersion: 2', '  tags: []', '  layers:', '  - Default') }
        'ProjectSettings/TimeManager.asset'            = [ordered]@{ root = 'TimeManager'; requiredLines = @('  Fixed Timestep: 0.02', '  Maximum Allowed Timestep: 0.33333334', '  m_TimeScale: 1') }
        'ProjectSettings/UnityConnectSettings.asset'   = [ordered]@{ root = 'UnityConnectSettings'; requiredLines = @('  serializedVersion: 1', '  m_Enabled: 0', '  UnityAnalyticsSettings:', '    m_InitializeOnStartup: 1') }
        'ProjectSettings/VFXManager.asset'             = [ordered]@{ root = 'VFXManager'; requiredLines = @('  m_FixedTimeStep: 0.016666668', '  m_MaxDeltaTime: 0.05') }
        'ProjectSettings/VersionControlSettings.asset' = [ordered]@{ root = 'VersionControlSettings'; requiredLines = @('  m_Mode: Visible Meta Files', '  m_CollabEditorSettings:', '    inProgressEnabled: 1') }
    }
}

function Get-FirstBootstrapShaderCoreSettingsProfile {
    return [ordered]@{
        'PureBase/Hybrid'                             = @()
        'PureBase/Tests/ShaderCore/Phase/PostPixel'   = @('jp.penguin.purebase.tests.shadercore.phase.postpixel')
        'PureBase/Tests/ShaderCore/Phase/Add'         = @('jp.penguin.purebase.tests.shadercore.phase.add')
        'PureBase/Tests/ShaderCore/Phase/CustomLight' = @('jp.penguin.purebase.tests.shadercore.phase.customlight')
        'PureBase/Unlit'                              = @()
        'PureBase/Tests/ShaderCore/Phase/Light'       = @('jp.penguin.purebase.tests.shadercore.phase.light')
        'PureBase/PBR'                                = @()
        'PureBase/Tests/ShaderCore/Phase/Base'        = @('jp.penguin.purebase.tests.shadercore.phase.base')
        'PureBase/Tests/ShaderCore/Phase/ModifyLight' = @('jp.penguin.purebase.tests.shadercore.phase.modifylight')
        'PureBase/Tests/ShaderCore/Phase/Morph'       = @('jp.penguin.purebase.tests.shadercore.phase.morph')
        'PureBase/Tests/ShaderCore/Phase/PostVertex'  = @('jp.penguin.purebase.tests.shadercore.phase.postvertex')
        'PureBase/Tests/ShaderCore/Phase/Reflection'  = @('jp.penguin.purebase.tests.shadercore.phase.reflection')
        'PureBase/Tests/ShaderCore/ModuleOrder'       = @('jp.penguin.purebase.tests.shadercore.moduleorder.zeta', 'jp.penguin.purebase.tests.shadercore.moduleorder.alpha')
        'PureBase/Tests/ShaderCore/Phase/Shade'       = @('jp.penguin.purebase.tests.shadercore.phase.shade')
        'PureBase/Toon'                               = @()
    }
}

function Get-CanonicalShaderCoreSettingsProfile {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $manifestPath = Join-Path $ConsumerRoot (Get-CanonicalShaderCoreConfigDestination).Replace('/', '\')
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Canonical Shader-Core test-host manifest is missing: '$manifestPath'."
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($null -eq $manifest -or [int]$manifest.schemaVersion -ne 1 -or $null -eq $manifest.hosts) {
        throw 'Canonical Shader-Core test-host manifest must be schema version 1 with hosts.'
    }

    $mapping = [ordered]@{}
    foreach ($manifestHost in @($manifest.hosts)) {
        $shaderName = [string]$manifestHost.shaderName
        $singleModuleProperty = $manifestHost.PSObject.Properties['moduleUniqueId']
        $multipleModulesProperty = $manifestHost.PSObject.Properties['moduleUniqueIds']
        $singleModule = if ($null -eq $singleModuleProperty) { '' } else { [string]$singleModuleProperty.Value }
        $multipleModules = @(
            if ($null -ne $multipleModulesProperty) {
                @($multipleModulesProperty.Value) | ForEach-Object { [string]$_ }
            }
        )
        $modules = @(
            if (-not [string]::IsNullOrEmpty($singleModule)) {
                if ($multipleModules.Count -gt 0) { throw "Canonical Shader-Core host '$shaderName' defines both moduleUniqueId and moduleUniqueIds." }
                $singleModule
            }
            else {
                $multipleModules
            }
        )
        if ([string]::IsNullOrEmpty($shaderName) -or $modules.Count -eq 0 -or @($modules | Where-Object { [string]::IsNullOrEmpty($_) }).Count -ne 0 -or $mapping.Contains($shaderName)) {
            throw "Canonical Shader-Core test-host manifest contains an invalid or duplicate host '$shaderName'."
        }
        $mapping[$shaderName] = $modules
    }
    foreach ($productShaderName in @('PureBase/Unlit', 'PureBase/Toon', 'PureBase/Hybrid', 'PureBase/PBR')) {
        if ($mapping.Contains($productShaderName)) {
            throw "Canonical Shader-Core test-host manifest must not redefine product shader '$productShaderName'."
        }
        $mapping[$productShaderName] = @()
    }
    if ($mapping.Count -ne 15) {
        throw "Canonical Shader-Core test-host manifest must define exactly 15 fixed host and product rows; found $($mapping.Count)."
    }
    return $mapping
}

function Get-CanonicalShaderCoreConfigDestination {
    return 'Assets/ReleaseConsumer/Fixtures/ShaderCore/shader-core-test-hosts.json'
}

function Get-CanonicalShaderCoreConfigGeneratedMetaProfile {
    $configDestination = Get-CanonicalShaderCoreConfigDestination
    $directoryDestination = $configDestination.Substring(0, $configDestination.LastIndexOf('/'))
    return [ordered]@{
        ($directoryDestination + '.meta') = [ordered]@{ relatedPath = $directoryDestination; itemType = 'Directory' }
        ($configDestination + '.meta')    = [ordered]@{ relatedPath = $configDestination; itemType = 'Leaf' }
    }
}

function Assert-FirstBootstrapProjectSettingsAsset {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $profile = Get-FirstBootstrapProjectSettingsProfile
    if (-not $profile.Contains($RelativePath)) {
        throw "No Unity 2022.3.22f1 project-settings projection is defined for '$RelativePath'."
    }
    $expected = $profile[$RelativePath]
    $facts = Assert-FirstBootstrapUnityYaml -ConsumerRoot $ConsumerRoot -RelativePath $RelativePath -ExpectedRootName $expected.root
    $text = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath $RelativePath
    foreach ($requiredLine in @($expected.requiredLines)) {
        if ($text -notmatch ('(?m)^' + [regex]::Escape($requiredLine) + '\r?$')) {
            throw "First-bootstrap ProjectSettings projection changed '$requiredLine' in '$RelativePath'."
        }
    }
    $facts['projection'] = [ordered]@{ root = $expected.root; requiredLines = @($expected.requiredLines) }
    return $facts
}

function Get-ConsumerFileText {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $path = Join-Path $ConsumerRoot ($RelativePath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Expected first-bootstrap file is missing: '$RelativePath'."
    }
    return Get-Content -LiteralPath $path -Raw
}

function Assert-FirstBootstrapUnityYaml {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter()][string]$ExpectedRootName
    )

    $text = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath $RelativePath
    if ($text -notmatch '(?m)^%YAML 1\.1\r?$' -or $text -notmatch '(?m)^%TAG !u! tag:unity3d\.com,2011:\r?$' -or $text -notmatch '(?m)^--- !u![0-9]+ &-?[0-9]+\r?$') {
        throw "First-bootstrap Unity YAML is invalid: '$RelativePath'."
    }
    if (-not [string]::IsNullOrEmpty($ExpectedRootName) -and $text -notmatch ('(?m)^' + [regex]::Escape($ExpectedRootName) + ':\r?$')) {
        throw "First-bootstrap Unity YAML root is not '$ExpectedRootName': '$RelativePath'."
    }
    return [ordered]@{ yaml = 'valid'; expectedRoot = $ExpectedRootName }
}

function Assert-FirstBootstrapMeta {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $text = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath $RelativePath
    $guidMatch = [regex]::Match($text, '(?m)^guid:\s*([0-9a-f]{32})\s*$')
    if ($text -notmatch '(?m)^fileFormatVersion:\s*2\s*$' -or -not $guidMatch.Success -or $text -notmatch '(?m)^[A-Za-z]+Importer:\s*$') {
        throw "First-bootstrap Unity meta file is invalid: '$RelativePath'."
    }
    $guid = $guidMatch.Groups[1].Value

    $assetPath = $RelativePath.Substring(0, $RelativePath.Length - '.meta'.Length)
    $assetAbsolutePath = Join-Path $ConsumerRoot ($assetPath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $assetAbsolutePath)) {
        throw "First-bootstrap Unity meta file is orphaned: '$RelativePath'."
    }
    return [ordered]@{ yaml = 'valid'; guid = $guid; relatedPath = $assetPath }
}

function Get-FirstBootstrapMetaValidationFailures {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)]$ReceiptDestinations,
        [Parameter(Mandatory = $true)]$ExpectedAdded,
        [Parameter(Mandatory = $true)]$Delta,
        [Parameter(Mandatory = $true)]$ReceiptGeneratedMetaProfile
    )

    $addedMetaPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($entry in @($Delta.added | Where-Object { ([string]$_.path).EndsWith('.meta', [System.StringComparison]::Ordinal) })) {
        [void]$addedMetaPaths.Add([string]$entry.path)
    }
    $failures = @{}
    $guidPaths = @{}
    $generatedMetaProfile = Get-FirstBootstrapGeneratedMetaProfile
    $allMetaPaths = @(
        Get-ChildItem -LiteralPath $ConsumerRoot -File -Recurse -Force |
        ForEach-Object { Get-NormalizedRelativePath -Path $_.FullName.Substring($ConsumerRoot.Length).TrimStart('\', '/') } |
        Where-Object { $_.EndsWith('.meta', [System.StringComparison]::Ordinal) -and ($_.StartsWith('Assets/', [System.StringComparison]::Ordinal) -or $_.StartsWith('Packages/', [System.StringComparison]::Ordinal) -or $_.StartsWith('ProjectSettings/', [System.StringComparison]::Ordinal) -or $_.StartsWith('_LocalPackages/', [System.StringComparison]::Ordinal)) }
    )
    $allMetaPaths = Get-OrdinalSortedStrings -Values $allMetaPaths
    foreach ($path in $allMetaPaths) {
        try {
            $facts = Assert-FirstBootstrapMeta -ConsumerRoot $ConsumerRoot -RelativePath $path
            $isReceiptMeta = $ReceiptDestinations.Contains($path)
            $isAddedMeta = $addedMetaPaths.Contains($path)
            $isReceiptGeneratedMeta = $ReceiptGeneratedMetaProfile.Contains($path)
            if (-not $isReceiptMeta -and -not $isAddedMeta -and -not $isReceiptGeneratedMeta) {
                throw "Immutable Unity meta is neither receipt-owned nor an observed bootstrap addition: '$path'."
            }
            if ($isAddedMeta) {
                if (-not $ExpectedAdded.Contains($path)) {
                    throw "First-bootstrap Unity meta is not in the exact allowed transition set: '$path'."
                }
                if (-not $generatedMetaProfile.Contains($path)) {
                    throw "First-bootstrap Unity meta has no exact generated relationship profile: '$path'."
                }
            }
            if ($isAddedMeta -or $isReceiptGeneratedMeta) {
                $expectedRelationship = if ($isReceiptGeneratedMeta) { $ReceiptGeneratedMetaProfile[$path] } else { $generatedMetaProfile[$path] }
                if ($facts.relatedPath -ne $expectedRelationship.relatedPath) {
                    throw "First-bootstrap Unity meta has an unexpected generated relationship: '$path'."
                }
                $relatedItem = Get-Item -LiteralPath (Join-Path $ConsumerRoot ($facts.relatedPath.Replace('/', '\'))) -Force
                if (($expectedRelationship.itemType -eq 'Directory' -and -not $relatedItem.PSIsContainer) -or ($expectedRelationship.itemType -eq 'Leaf' -and $relatedItem.PSIsContainer)) {
                    throw "First-bootstrap Unity meta has an unexpected generated item type: '$path'."
                }
            }
            $guid = [string]$facts.guid
            if ($guidPaths.ContainsKey($guid)) {
                $failures[$path] = "First-bootstrap Unity meta GUID '$guid' collides with '$($guidPaths[$guid])'."
                $failures[$guidPaths[$guid]] = "First-bootstrap Unity meta GUID '$guid' collides with '$path'."
            }
            else {
                $guidPaths[$guid] = $path
            }
        }
        catch {
            $failures[$path] = $_.Exception.Message
        }
    }
    return $failures
}

function Assert-FirstBootstrapManifest {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $manifest = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath 'Packages/manifest.json' | ConvertFrom-Json
    $profile = Get-FirstBootstrapPackageProfile
    $expectedDependencies = $profile.manifestDependencies
    if ($null -eq $manifest.dependencies) {
        throw 'First-bootstrap manifest has no dependency map.'
    }
    if (@($manifest.dependencies.PSObject.Properties).Count -ne $expectedDependencies.Count) {
        throw 'First-bootstrap manifest dependency names do not match the Unity 2022.3.22f1 profile.'
    }
    $expectedNames = @(Get-OrdinalSortedStrings -Values ([string[]]$expectedDependencies.Keys))
    foreach ($dependencyName in $expectedDependencies.Keys) {
        if ($null -eq $manifest.dependencies.PSObject.Properties[$dependencyName]) {
            throw "First-bootstrap manifest omits required dependency '$dependencyName'."
        }
        $value = [string]$manifest.dependencies.PSObject.Properties[$dependencyName].Value
        if ($value -ne $expectedDependencies[$dependencyName]) {
            throw "First-bootstrap manifest changed direct dependency '$dependencyName'."
        }
        if ($value.StartsWith('file:', [System.StringComparison]::Ordinal)) {
            $localPath = Get-NormalizedPath -Path (Join-Path (Join-Path $ConsumerRoot 'Packages') $value.Substring('file:'.Length))
            if (-not (Test-PathContainedBy -Path $localPath -ParentPath (Join-Path $ConsumerRoot '_LocalPackages'))) {
                throw "First-bootstrap manifest local dependency escapes _LocalPackages: '$dependencyName'."
            }
        }
    }
    foreach ($dependency in $manifest.dependencies.PSObject.Properties) {
        if (-not $expectedDependencies.Contains([string]$dependency.Name)) {
            throw "First-bootstrap manifest added an unprofiled dependency '$($dependency.Name)'."
        }
    }
    return [ordered]@{ profile = 'Unity-2022.3.22f1-887be4894c44'; dependencyNames = $expectedNames; localDependencies = @('jp.lilxyzw.shadercore', 'jp.penguin.purebase') }
}

function Assert-FirstBootstrapProjectVersion {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $text = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath 'ProjectSettings/ProjectVersion.txt'
    $versionMatch = [regex]::Match($text, '(?m)^m_EditorVersion:\s*(\S+)\s*$')
    $revisionMatch = [regex]::Match($text, '(?m)^m_EditorVersionWithRevision:\s*(\S+)\s+\(([0-9a-f]+)\)\s*$')
    if (-not $versionMatch.Success -or -not $revisionMatch.Success -or $versionMatch.Groups[1].Value -ne $RequiredUnityVersion -or $revisionMatch.Groups[1].Value -ne $RequiredUnityVersion -or $revisionMatch.Groups[2].Value -ne $RequiredUnityRevision) {
        throw "First-bootstrap ProjectVersion does not pin Unity $RequiredUnityVersion ($RequiredUnityRevision)."
    }
    return [ordered]@{ unityVersion = $RequiredUnityVersion; unityRevision = $RequiredUnityRevision }
}

function Assert-FirstBootstrapPackagesLock {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $lock = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath 'Packages/packages-lock.json' | ConvertFrom-Json
    if ($null -eq $lock.dependencies) {
        throw 'First-bootstrap packages-lock.json has no dependency graph.'
    }
    $profile = Get-FirstBootstrapPackageProfile
    $expectedLock = $profile.lockDependencies
    if (@($lock.dependencies.PSObject.Properties).Count -ne $expectedLock.Count) {
        throw 'First-bootstrap packages-lock.json entry names do not match the Unity 2022.3.22f1 profile.'
    }
    foreach ($dependencyName in $expectedLock.Keys) {
        $actualProperty = $lock.dependencies.PSObject.Properties[$dependencyName]
        if ($null -eq $actualProperty) {
            throw "First-bootstrap packages-lock.json omits profiled dependency '$dependencyName'."
        }
        $actual = $actualProperty.Value
        $expected = $expectedLock[$dependencyName]
        if ([string]$actual.version -ne [string]$expected.version -or [int]$actual.depth -ne [int]$expected.depth -or [string]$actual.source -ne [string]$expected.source) {
            throw "First-bootstrap packages-lock.json changed version, depth, or source for '$dependencyName'."
        }
        $actualEdges = if ($null -eq $actual.dependencies) { @() } else { @($actual.dependencies.PSObject.Properties) }
        if (@($actualEdges).Count -ne @($expected.dependencies.Keys).Count) {
            throw "First-bootstrap packages-lock.json changed dependency edges for '$dependencyName'."
        }
        foreach ($edgeName in $expected.dependencies.Keys) {
            if ($null -eq $actual.dependencies.PSObject.Properties[$edgeName] -or [string]$actual.dependencies.PSObject.Properties[$edgeName].Value -ne [string]$expected.dependencies[$edgeName]) {
                throw "First-bootstrap packages-lock.json changed dependency edge '$dependencyName' -> '$edgeName'."
            }
        }
    }
    foreach ($dependency in $lock.dependencies.PSObject.Properties) {
        if (-not $expectedLock.Contains([string]$dependency.Name)) {
            throw "First-bootstrap packages-lock.json added an unprofiled dependency '$($dependency.Name)'."
        }
    }
    foreach ($dependencyName in $profile.manifestDependencies.Keys) {
        $entry = $lock.dependencies.PSObject.Properties[$dependencyName].Value
        if ([int]$entry.depth -ne 0) {
            throw "First-bootstrap packages-lock.json manifest dependency '$dependencyName' is not a depth-zero lock root."
        }
    }
    foreach ($localDependencyName in @('jp.lilxyzw.shadercore', 'jp.penguin.purebase')) {
        $entry = $lock.dependencies.PSObject.Properties[$localDependencyName].Value
        if (-not ([string]$entry.version).StartsWith('file:', [System.StringComparison]::Ordinal)) {
            throw "First-bootstrap packages-lock.json local package '$localDependencyName' has no local URI."
        }
        $localPath = Get-NormalizedPath -Path (Join-Path (Join-Path $ConsumerRoot 'Packages') ([string]$entry.version).Substring('file:'.Length))
        if (-not (Test-PathContainedBy -Path $localPath -ParentPath (Join-Path $ConsumerRoot '_LocalPackages'))) {
            throw "First-bootstrap packages-lock.json local package escapes _LocalPackages: '$localDependencyName'."
        }
    }
    return [ordered]@{ profile = 'Unity-2022.3.22f1-887be4894c44'; lockEntryCount = $expectedLock.Count; manifestLockRoots = @(Get-OrdinalSortedStrings -Values ([string[]]$profile.manifestDependencies.Keys)) }
}

function Assert-FirstBootstrapBillingMode {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $billing = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath 'Assets/Resources/BillingMode.json' | ConvertFrom-Json
    if ($null -eq $billing -or @($billing.PSObject.Properties).Count -ne 1 -or $null -eq $billing.PSObject.Properties['androidStore'] -or [string]$billing.androidStore -ne 'GooglePlay') {
        throw 'First-bootstrap BillingMode.json does not match the Unity 2022.3.22f1 Android-store profile.'
    }
    return [ordered]@{ androidStore = 'GooglePlay' }
}

function Assert-FirstBootstrapShaderCoreSettings {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $facts = Assert-FirstBootstrapUnityYaml -ConsumerRoot $ConsumerRoot -RelativePath 'ProjectSettings/jp.lilxyzw.shadercore.asset' -ExpectedRootName 'MonoBehaviour'
    $text = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath 'ProjectSettings/jp.lilxyzw.shadercore.asset'
    $expectedMapping = Get-CanonicalShaderCoreSettingsProfile -ConsumerRoot $ConsumerRoot
    $actualMappings = @([regex]::Matches($text, '(?ms)^  - shadername:\s*(\S+)\r?\n    modules:[ \t]*(\[\]|(?:\r?\n    -\s*\S+)+)'))
    if ($text -notmatch '(?m)^  shaderSettings:\r?$' -or $actualMappings.Count -ne $expectedMapping.Count) {
        throw 'First-bootstrap Shader-Core settings do not match the expected shaderSettings profile.'
    }
    $actualMapping = @{}
    foreach ($actual in $actualMappings) {
        $shaderName = $actual.Groups[1].Value
        if ($actualMapping.ContainsKey($shaderName)) {
            throw "First-bootstrap Shader-Core settings contain duplicate shader mapping '$shaderName'."
        }
        $actualModules = @([regex]::Matches($actual.Groups[2].Value, '(?m)^    -\s*(\S+)\r?$') | ForEach-Object { $_.Groups[1].Value })
        $actualMapping[$shaderName] = $actualModules
    }
    foreach ($shaderName in $actualMapping.Keys) {
        if (-not $expectedMapping.Contains($shaderName)) {
            throw "First-bootstrap Shader-Core settings contain unexpected shader mapping '$shaderName'."
        }
    }
    foreach ($shaderName in $expectedMapping.Keys) {
        if (-not $actualMapping.ContainsKey($shaderName)) {
            throw "First-bootstrap Shader-Core settings omit required shader mapping '$shaderName'."
        }
        $actualModules = @($actualMapping[$shaderName])
        $expectedModules = @($expectedMapping[$shaderName])
        if ($actualModules.Count -ne $expectedModules.Count) {
            throw "First-bootstrap Shader-Core settings changed module count for '$shaderName'."
        }
        for ($moduleIndex = 0; $moduleIndex -lt $expectedModules.Count; $moduleIndex++) {
            if ($actualModules[$moduleIndex] -ne $expectedModules[$moduleIndex]) {
                throw "First-bootstrap Shader-Core settings changed module mapping for '$shaderName' at index $moduleIndex."
            }
        }
    }
    $facts['mapping'] = [ordered]@{ shaderNames = @($expectedMapping.Keys); modules = $expectedMapping; rowCount = $expectedMapping.Count }
    return $facts
}

function Get-ConsumerFirstBootstrapTransitionReport {
    param(
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)]$StagingReceipt,
        [Parameter(Mandatory = $true)]$PreBootstrap,
        [Parameter(Mandatory = $true)]$PostBootstrap
    )

    $delta = Get-ConsumerImmutableManifestDeltaReport -PreBootstrap $PreBootstrap -PostBootstrap $PostBootstrap
    $receiptDestinations = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($receiptEntry in @($StagingReceipt.entries)) { [void]$receiptDestinations.Add([string]$receiptEntry.destination) }
    $canonicalConfigDestination = Get-CanonicalShaderCoreConfigDestination
    $canonicalConfigEntries = @($StagingReceipt.entries | Where-Object { $_.destination -eq $canonicalConfigDestination })
    if ($canonicalConfigEntries.Count -ne 1 -or $canonicalConfigEntries[0].sourceKind -ne 'workspace-canonical-shader-core-config') {
        throw "Canonical Shader-Core config receipt is missing or invalid for '$canonicalConfigDestination'."
    }
    $receiptGeneratedMetaProfile = Get-CanonicalShaderCoreConfigGeneratedMetaProfile
    $semanticDelta = [ordered]@{
        preBootstrapRootSha256  = $delta.preBootstrapRootSha256
        postBootstrapRootSha256 = $delta.postBootstrapRootSha256
        added                   = @($delta.added | Where-Object { -not $receiptGeneratedMetaProfile.Contains([string]$_.path) })
        removed                 = @($delta.removed)
        changed                 = @($delta.changed)
    }
    $expectedAdded = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($path in Get-ExpectedFirstBootstrapAddedPaths) { [void]$expectedAdded.Add($path) }
    $expectedChanged = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($path in Get-ExpectedFirstBootstrapChangedPaths) { [void]$expectedChanged.Add($path) }
    $entries = New-Object System.Collections.Generic.List[object]
    $metaValidationFailures = Get-FirstBootstrapMetaValidationFailures -ConsumerRoot $ConsumerRoot -ReceiptDestinations $receiptDestinations -ExpectedAdded $expectedAdded -Delta $semanticDelta -ReceiptGeneratedMetaProfile $receiptGeneratedMetaProfile

    foreach ($operation in @('added', 'changed', 'removed')) {
        foreach ($deltaEntry in @($semanticDelta.$operation)) {
            $path = [string]$deltaEntry.path
            $ownership = if ($receiptDestinations.Contains($path)) { 'receipt-owned' } elseif ($operation -eq 'added' -and $path -eq 'Packages/packages-lock.json') { 'package-manager-owned' } else { 'unity-generated' }
            $ruleId = if ($operation -eq 'changed' -and $path -eq 'Packages/manifest.json') { 'PB-BT-001-manifest' } elseif ($operation -eq 'changed' -and $path -eq 'ProjectSettings/ProjectVersion.txt') { 'PB-BT-002-project-version' } elseif ($operation -eq 'added' -and $path -eq 'Packages/packages-lock.json') { 'PB-BT-003-packages-lock' } elseif ($operation -eq 'added' -and $path -eq 'Assets/Resources/BillingMode.json') { 'PB-BT-004-billing-mode' } elseif ($operation -eq 'added' -and $path -eq 'ProjectSettings/jp.lilxyzw.shadercore.asset') { 'PB-BT-005-shader-core-settings' } elseif ($operation -eq 'added' -and $path.EndsWith('.meta', [System.StringComparison]::Ordinal)) { 'PB-BT-006-unity-meta' } elseif ($operation -eq 'added' -and $path -eq 'ProjectSettings/ProjectSettings.asset') { 'PB-BT-007-player-settings-projection' } elseif ($operation -eq 'added' -and $path.StartsWith('ProjectSettings/', [System.StringComparison]::Ordinal)) { 'PB-BT-008-generated-project-settings' } else { 'PB-BT-000-unclassified' }
            $hashes = if ($operation -eq 'changed') { [ordered]@{ preBootstrapSha256 = [string]$deltaEntry.preBootstrapSha256; postBootstrapSha256 = [string]$deltaEntry.postBootstrapSha256 } } else { [ordered]@{ sha256 = [string]$deltaEntry.sha256 } }
            $entry = [ordered]@{ path = $path; operation = $operation; ownership = $ownership; ruleId = $ruleId; hashValues = $hashes; semanticChecks = @(); facts = [ordered]@{}; verdict = 'unclassified' }
            $entries.Add($entry)
        }
    }

    foreach ($entry in $entries) {
        try {
            if ($entry.operation -eq 'removed') { throw "Removed immutable path is not approved: '$($entry.path)'." }
            if ($entry.operation -eq 'changed' -and -not $expectedChanged.Contains($entry.path)) { throw "Changed immutable path is not approved: '$($entry.path)'." }
            if ($entry.operation -eq 'added' -and -not $expectedAdded.Contains($entry.path)) { throw "Added immutable path is not approved: '$($entry.path)'." }
            if ($entry.operation -eq 'changed' -and $receiptDestinations.Contains($entry.path) -and -not $expectedChanged.Contains($entry.path)) { throw "Receipt-owned path changed without an explicit rule: '$($entry.path)'." }
            $facts = $null
            if ($entry.path -eq 'Packages/manifest.json') { $facts = Assert-FirstBootstrapManifest -ConsumerRoot $ConsumerRoot }
            elseif ($entry.path -eq 'ProjectSettings/ProjectVersion.txt') { $facts = Assert-FirstBootstrapProjectVersion -ConsumerRoot $ConsumerRoot }
            elseif ($entry.path -eq 'Packages/packages-lock.json') { $facts = Assert-FirstBootstrapPackagesLock -ConsumerRoot $ConsumerRoot }
            elseif ($entry.path -eq 'Assets/Resources/BillingMode.json') { $facts = Assert-FirstBootstrapBillingMode -ConsumerRoot $ConsumerRoot }
            elseif ($entry.path -eq 'ProjectSettings/jp.lilxyzw.shadercore.asset') { $facts = Assert-FirstBootstrapShaderCoreSettings -ConsumerRoot $ConsumerRoot }
            elseif ($entry.path.EndsWith('.meta', [System.StringComparison]::Ordinal)) {
                $facts = Assert-FirstBootstrapMeta -ConsumerRoot $ConsumerRoot -RelativePath $entry.path
                if ($metaValidationFailures.ContainsKey($entry.path)) { throw $metaValidationFailures[$entry.path] }
            }
            elseif ($entry.path -eq 'ProjectSettings/SceneTemplateSettings.json') {
                $sceneTemplateSettings = Get-ConsumerFileText -ConsumerRoot $ConsumerRoot -RelativePath $entry.path | ConvertFrom-Json
                $expectedNames = @('templatePinStates', 'dependencyTypeInfos', 'defaultDependencyTypeInfo', 'newSceneOverride')
                $actualNames = @($sceneTemplateSettings.PSObject.Properties | ForEach-Object { $_.Name })
                $defaultDependencyTypeInfo = $sceneTemplateSettings.defaultDependencyTypeInfo
                $hasExpectedDependencyTypes = @($sceneTemplateSettings.dependencyTypeInfos).Count -eq 22
                $hasOnlyDefaultDependencyTypes = @($sceneTemplateSettings.dependencyTypeInfos | Where-Object { $_.userAdded -or [string]::IsNullOrEmpty([string]$_.type) -or ([int]$_.defaultInstantiationMode -ne 0 -and [int]$_.defaultInstantiationMode -ne 1) }).Count -eq 0
                $hasExpectedDefaultDependencyType = $null -ne $defaultDependencyTypeInfo -and -not $defaultDependencyTypeInfo.userAdded -and [string]$defaultDependencyTypeInfo.type -eq '<default_scene_template_dependencies>' -and [int]$defaultDependencyTypeInfo.defaultInstantiationMode -eq 1
                if ($null -eq $sceneTemplateSettings -or $actualNames.Count -ne $expectedNames.Count -or [string]::Join('|', $actualNames) -ne [string]::Join('|', $expectedNames) -or @($sceneTemplateSettings.templatePinStates).Count -ne 0 -or -not $hasExpectedDependencyTypes -or -not $hasOnlyDefaultDependencyTypes -or -not $hasExpectedDefaultDependencyType -or [int]$sceneTemplateSettings.newSceneOverride -ne 0) { throw 'First-bootstrap SceneTemplateSettings does not match the Unity 2022.3.22f1 dependency-type projection.' }
                $facts = [ordered]@{ propertyNames = $expectedNames; templatePinCount = 0; dependencyTypeInfoCount = @($sceneTemplateSettings.dependencyTypeInfos).Count; userAddedDependencyTypeInfoCount = 0 }
            }
            elseif ((Get-FirstBootstrapProjectSettingsProfile).Contains($entry.path)) { $facts = Assert-FirstBootstrapProjectSettingsAsset -ConsumerRoot $ConsumerRoot -RelativePath $entry.path }
            elseif ($entry.path.StartsWith('ProjectSettings/', [System.StringComparison]::Ordinal)) { throw "No Unity 2022.3.22f1 ProjectSettings projection classified '$($entry.path)'." }
            else { throw "No semantic rule classified '$($entry.path)'." }

            $entry.semanticChecks = @('path-operation-approved', 'ownership-approved', 'observed-stable-hash-or-semantic-projection', 'content-schema-approved')
            $entry.facts = $facts
            $entry.verdict = 'accepted'
        }
        catch {
            $entry.semanticChecks = @('failed: ' + $_.Exception.Message)
            $entry.verdict = if ($entry.ruleId -eq 'PB-BT-000-unclassified') { 'unclassified' } else { 'rejected' }
        }
    }

    $accepted = @($entries | Where-Object { $_.verdict -eq 'accepted' }).Count
    $rejected = @($entries | Where-Object { $_.verdict -eq 'rejected' }).Count
    $unclassified = @($entries | Where-Object { $_.verdict -eq 'unclassified' }).Count
    $expectedAccepted = $expectedAdded.Count + $expectedChanged.Count
    $verdict = if ($accepted -eq $expectedAccepted -and $rejected -eq 0 -and $unclassified -eq 0 -and $metaValidationFailures.Count -eq 0 -and @($semanticDelta.added).Count -eq $expectedAdded.Count -and @($semanticDelta.changed).Count -eq $expectedChanged.Count -and @($semanticDelta.removed).Count -eq 0) { 'accepted' } else { 'rejected' }
    return [ordered]@{
        schemaName    = 'purebase-first-bootstrap-semantic-transition'
        schemaVersion = 1
        profile       = [ordered]@{
            unityVersion  = $RequiredUnityVersion
            unityRevision = $RequiredUnityRevision
            packageGraph  = [ordered]@{ manifestDependencyCount = (Get-FirstBootstrapPackageProfile).manifestDependencies.Count; lockDependencyCount = (Get-FirstBootstrapPackageProfile).lockDependencies.Count }
            shaderCore    = [ordered]@{ packageName = [string]$PostBootstrap.shaderCore.packageName; packageVersion = [string]$PostBootstrap.shaderCore.packageVersion; identitySha256 = [string]$PostBootstrap.shaderCore.treeSha256; expectedIdentitySha256 = [string]$PostBootstrap.shaderCore.expectedIdentitySha256 }
        }
        deltaRoots    = [ordered]@{ preBootstrapSha256 = [string]$delta.preBootstrapRootSha256; postBootstrapSha256 = [string]$delta.postBootstrapRootSha256 }
        entries       = $entries.ToArray()
        summary       = [ordered]@{ accepted = $accepted; rejected = $rejected; unclassified = $unclassified; metaValidationFailures = $metaValidationFailures.Count; expectedAdded = $expectedAdded.Count; expectedChanged = $expectedChanged.Count; observedAdded = @($semanticDelta.added).Count; observedChanged = @($semanticDelta.changed).Count; observedRemoved = @($semanticDelta.removed).Count }
        verdict       = $verdict
    }
}

function Assert-ConsumerFirstBootstrapTransitionReport {
    param([Parameter(Mandatory = $true)]$Report)

    if ($Report.profile.unityVersion -ne $RequiredUnityVersion -or $Report.profile.unityRevision -ne $RequiredUnityRevision -or $Report.profile.shaderCore.packageName -ne 'jp.lilxyzw.shadercore' -or $Report.profile.shaderCore.packageVersion -ne '0.1.9' -or $Report.profile.shaderCore.identitySha256 -ne $Report.profile.shaderCore.expectedIdentitySha256) {
        throw 'First-bootstrap semantic transition profile does not match the pinned Unity and Shader-Core identities.'
    }
    $expectedAccepted = [int]$Report.summary.expectedAdded + [int]$Report.summary.expectedChanged
    if ($Report.verdict -ne 'accepted' -or [int]$Report.summary.accepted -ne $expectedAccepted -or [int]$Report.summary.observedAdded -ne [int]$Report.summary.expectedAdded -or [int]$Report.summary.observedChanged -ne [int]$Report.summary.expectedChanged -or [int]$Report.summary.observedRemoved -ne 0 -or [int]$Report.summary.rejected -ne 0 -or [int]$Report.summary.unclassified -ne 0) {
        throw "First-bootstrap semantic transition rejected or did not classify every immutable change: accepted=$($Report.summary.accepted) rejected=$($Report.summary.rejected) unclassified=$($Report.summary.unclassified)."
    }
}

function Assert-ConsumerSecondBootstrapFixedPoint {
    param(
        [Parameter(Mandatory = $true)]$FirstBootstrap,
        [Parameter(Mandatory = $true)]$AfterLibraryReset,
        [Parameter(Mandatory = $true)]$SecondBootstrap,
        [Parameter(Mandatory = $true)]$Delta
    )

    if ($FirstBootstrap.rootSha256 -ne $AfterLibraryReset.rootSha256 -or $FirstBootstrap.rootSha256 -ne $SecondBootstrap.rootSha256 -or @($Delta.added).Count -ne 0 -or @($Delta.changed).Count -ne 0 -or @($Delta.removed).Count -ne 0) {
        throw 'Second bootstrap did not reach a byte-exact immutable fixed point.'
    }
}

function Assert-ConsumerImmutableManifestBaseline {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$RunLabel
    )

    if ($Manifest.shaderCore.packageName -ne 'jp.lilxyzw.shadercore' -or $Manifest.shaderCore.packageVersion -ne '0.1.9') {
        throw "Consumer run '$RunLabel' did not stage Shader-Core jp.lilxyzw.shadercore version 0.1.9."
    }
    if ($Manifest.shaderCore.expectedIdentitySha256 -ne $Manifest.shaderCore.treeSha256) {
        throw "Consumer run '$RunLabel' staged Shader-Core does not match shader-core-0.1.9.sha256.json."
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
            message  = $Failure.Exception.Message
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

function Invoke-ConsumerShaderCoreStateInitialization {
    param(
        [Parameter(Mandatory = $true)][string]$UnityEditor,
        [Parameter(Mandatory = $true)][string]$ConsumerRoot,
        [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
        [Parameter(Mandatory = $true)][string]$Phase
    )

    $method = 'PureBase.Release.Consumer.Tests.PureBaseConsumerShaderCoreInitializer.InitializeForBatchMode'
    $commandPath = Join-Path $ArtifactDirectory 'shader-core-state-initialization-command.json'
    $unityLogPath = Join-Path $ArtifactDirectory 'shader-core-state-initialization-Unity.log'
    $processLogPath = Join-Path $ArtifactDirectory 'shader-core-state-initialization-Process.log'
    $reportPath = Join-Path $ArtifactDirectory 'shader-core-state-initialization-report.json'
    $arguments = @('-batchmode', '-force-d3d11', '-projectPath', $ConsumerRoot, '-executeMethod', $method, '-quit', '-logFile', $unityLogPath)
    Write-ConsumerJsonArtifact -Path $commandPath -Value ([ordered]@{ executable = $UnityEditor; arguments = $arguments; phase = $Phase; canonicalConfigDestination = Get-CanonicalShaderCoreConfigDestination; initializer = $method }) -Depth 4
    [System.IO.File]::WriteAllText($processLogPath, '', (New-Object System.Text.UTF8Encoding($false)))
    & $UnityEditor @arguments 2>&1 | Tee-Object -FilePath $processLogPath -Append | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Unity Shader-Core state initialization '$Phase' exited with $LASTEXITCODE. See '$processLogPath'."
    }
    $facts = Assert-FirstBootstrapShaderCoreSettings -ConsumerRoot $ConsumerRoot
    Write-ConsumerJsonArtifact -Path $reportPath -Value ([ordered]@{
            schemaName                 = 'purebase-shader-core-bootstrap-initialization'
            schemaVersion              = 1
            phase                      = $Phase
            canonicalConfigDestination = Get-CanonicalShaderCoreConfigDestination
            initializer                = $method
            rowCount                   = [int]$facts.mapping.rowCount
            mapping                    = $facts.mapping
        }) -Depth 12
    return $facts
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
    $initializationCommandPath = Join-Path $bootstrapDirectory 'shader-core-state-initialization-command.json'
    $initializationUnityLogPath = Join-Path $bootstrapDirectory 'shader-core-state-initialization-Unity.log'
    $initializationProcessLogPath = Join-Path $bootstrapDirectory 'shader-core-state-initialization-Process.log'
    $initializationReportPath = Join-Path $bootstrapDirectory 'shader-core-state-initialization-report.json'
    $initializationConfigReceiptPath = Join-Path $bootstrapDirectory 'shader-core-state-initialization-config.json'
    $initializationConfigPath = Join-Path $bootstrapDirectory 'shader-core-test-hosts.json'
    $receiptPath = Join-Path $bootstrapDirectory 'staging-receipt.json'
    $preBootstrapManifestPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-pre-bootstrap.json'
    $sceneBootstrapManifestPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-after-scene-bootstrap.json'
    $manifestPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-quiescent.json'
    $deltaReportPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-bootstrap-delta.json'
    $semanticTransitionPath = Join-Path $bootstrapDirectory 'semantic-transition-report.json'
    $afterResetManifestPath = Join-Path $bootstrapDirectory 'immutable-input-manifest-after-library-reset.json'
    $resetPath = Join-Path $bootstrapDirectory 'library-reset.json'
    $secondBootstrapDirectory = Join-Path $bootstrapDirectory 'second-bootstrap'
    $secondCommandPath = Join-Path $secondBootstrapDirectory 'unity-command.json'
    $secondUnityLogPath = Join-Path $secondBootstrapDirectory 'Unity.log'
    $secondProcessLogPath = Join-Path $secondBootstrapDirectory 'Process.log'
    $secondResultsPath = Join-Path $secondBootstrapDirectory 'NUnit.xml'
    $secondNunitSummaryPath = Join-Path $secondBootstrapDirectory 'nunit-summary.json'
    $secondInitializationCommandPath = Join-Path $secondBootstrapDirectory 'shader-core-state-initialization-command.json'
    $secondInitializationUnityLogPath = Join-Path $secondBootstrapDirectory 'shader-core-state-initialization-Unity.log'
    $secondInitializationProcessLogPath = Join-Path $secondBootstrapDirectory 'shader-core-state-initialization-Process.log'
    $secondInitializationReportPath = Join-Path $secondBootstrapDirectory 'shader-core-state-initialization-report.json'
    $secondManifestPath = Join-Path $secondBootstrapDirectory 'immutable-input-manifest-quiescent.json'
    $secondDeltaPath = Join-Path $secondBootstrapDirectory 'immutable-input-manifest-fixed-point-delta.json'
    $fixedPointReportPath = Join-Path $secondBootstrapDirectory 'fixed-point-report.json'
    $bootstrapTestFilter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerSceneTemplateBootstrapTests.DisposableSceneLifecycleMaterializesSceneTemplateSettings'
    $failure = $null
    $preBootstrapManifest = $null
    $manifest = $null

    try {
        Assert-EditorClosed -ProjectRoot $ConsumerRoot
        Write-ConsumerJsonArtifact -Path $receiptPath -Value $StagingReceipt
        $canonicalConfigDestination = Get-CanonicalShaderCoreConfigDestination
        $canonicalConfigEntries = @($StagingReceipt.entries | Where-Object { $_.destination -eq $canonicalConfigDestination })
        Write-ConsumerJsonArtifact -Path $initializationConfigReceiptPath -Value ([ordered]@{
                destination        = $canonicalConfigDestination
                expectedSourceKind = 'workspace-canonical-shader-core-config'
                receiptEntryCount  = $canonicalConfigEntries.Count
                receiptEntry       = if ($canonicalConfigEntries.Count -eq 1) { $canonicalConfigEntries[0] } else { $null }
            }) -Depth 6
        if ($canonicalConfigEntries.Count -ne 1 -or $canonicalConfigEntries[0].sourceKind -ne 'workspace-canonical-shader-core-config') {
            throw "Canonical Shader-Core config receipt is missing or invalid for '$canonicalConfigDestination'."
        }
        Assert-ConsumerStagingReceipt -ConsumerRoot $ConsumerRoot -Receipt $StagingReceipt
        Copy-Item -LiteralPath (Join-Path $ConsumerRoot $canonicalConfigDestination.Replace('/', '\')) -Destination $initializationConfigPath -Force
        $preBootstrapManifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $preBootstrapManifestPath -Value $preBootstrapManifest
        $arguments = @('-batchmode', '-force-d3d11', '-projectPath', $ConsumerRoot, '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', $ConsumerAssembly, '-testFilter', $bootstrapTestFilter, '-testResults', $resultsPath, '-logFile', $unityLogPath)
        Write-ConsumerJsonArtifact -Path $commandPath -Value ([ordered]@{ executable = $UnityEditor; arguments = $arguments }) -Depth 4
        [System.IO.File]::WriteAllText($processLogPath, '', (New-Object System.Text.UTF8Encoding($false)))
        & $UnityEditor @arguments 2>&1 | Tee-Object -FilePath $processLogPath -Append | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Unity bootstrap import exited with $LASTEXITCODE. See '$processLogPath'." }
        $nunitSummary = Test-NUnitEvidence -ResultsPath $resultsPath -RunLabel 'bootstrap'
        Write-ConsumerJsonArtifact -Path $nunitSummaryPath -Value $nunitSummary
        $sceneBootstrapManifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $sceneBootstrapManifestPath -Value $sceneBootstrapManifest
        Invoke-ConsumerShaderCoreStateInitialization -UnityEditor $UnityEditor -ConsumerRoot $ConsumerRoot -ArtifactDirectory $bootstrapDirectory -Phase 'first-bootstrap' | Out-Null

        $manifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $manifestPath -Value $manifest
        Write-ConsumerImmutableManifestBootstrapDelta -PreBootstrap $preBootstrapManifest -PostBootstrap $manifest -Path $deltaReportPath
        $semanticTransition = Get-ConsumerFirstBootstrapTransitionReport -ConsumerRoot $ConsumerRoot -StagingReceipt $StagingReceipt -PreBootstrap $preBootstrapManifest -PostBootstrap $manifest
        Write-ConsumerJsonArtifact -Path $semanticTransitionPath -Value $semanticTransition -Depth 12
        Assert-ConsumerFirstBootstrapTransitionReport -Report $semanticTransition
        Assert-ConsumerImmutableManifestBaseline -Manifest $preBootstrapManifest -RunLabel 'bootstrap-pre'
        Assert-ConsumerImmutableManifestBaseline -Manifest $manifest -RunLabel 'bootstrap'

        $resetResult = Reset-ConsumerLibrary -ConsumerRoot $ConsumerRoot
        Write-ConsumerJsonArtifact -Path $resetPath -Value ([ordered]@{
                priorLibraryPresent      = $resetResult.priorLibraryPresent
                libraryPresentAfterReset = $resetResult.libraryPresentAfterReset
            })
        if ($resetResult.libraryPresentAfterReset) {
            throw "Unity bootstrap import did not remove disposable Library: '$($resetResult.libraryPath)'."
        }

        $afterResetManifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $afterResetManifestPath -Value $afterResetManifest
        Assert-ConsumerImmutableManifest -Expected $manifest -Actual $afterResetManifest -RunLabel 'bootstrap-library-reset'

        New-Item -ItemType Directory -Path $secondBootstrapDirectory -Force | Out-Null
        $secondArguments = @('-batchmode', '-force-d3d11', '-projectPath', $ConsumerRoot, '-runTests', '-testPlatform', 'EditMode', '-assemblyNames', $ConsumerAssembly, '-testFilter', $bootstrapTestFilter, '-testResults', $secondResultsPath, '-logFile', $secondUnityLogPath)
        Write-ConsumerJsonArtifact -Path $secondCommandPath -Value ([ordered]@{ executable = $UnityEditor; arguments = $secondArguments }) -Depth 4
        [System.IO.File]::WriteAllText($secondProcessLogPath, '', (New-Object System.Text.UTF8Encoding($false)))
        & $UnityEditor @secondArguments 2>&1 | Tee-Object -FilePath $secondProcessLogPath -Append | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Unity second bootstrap import exited with $LASTEXITCODE. See '$secondProcessLogPath'." }
        $secondNunitSummary = Test-NUnitEvidence -ResultsPath $secondResultsPath -RunLabel 'bootstrap-second'
        Write-ConsumerJsonArtifact -Path $secondNunitSummaryPath -Value $secondNunitSummary
        Invoke-ConsumerShaderCoreStateInitialization -UnityEditor $UnityEditor -ConsumerRoot $ConsumerRoot -ArtifactDirectory $secondBootstrapDirectory -Phase 'second-bootstrap' | Out-Null
        $secondManifest = Get-ConsumerImmutableManifest -ConsumerRoot $ConsumerRoot -ZipPath $ZipPath -ShaderCoreManifestPath $ShaderCoreManifestPath
        Write-ConsumerJsonArtifact -Path $secondManifestPath -Value $secondManifest
        $secondDelta = Get-ConsumerImmutableManifestDeltaReport -PreBootstrap $manifest -PostBootstrap $secondManifest
        Write-ConsumerJsonArtifact -Path $secondDeltaPath -Value $secondDelta
        $fixedPointReport = [ordered]@{
            schemaName                    = 'purebase-second-bootstrap-fixed-point'
            schemaVersion                 = 1
            firstCanonicalRootSha256      = [string]$manifest.rootSha256
            preSecondBootstrapRootSha256  = [string]$afterResetManifest.rootSha256
            postSecondBootstrapRootSha256 = [string]$secondManifest.rootSha256
            rootsEqual                    = [bool]($manifest.rootSha256 -eq $afterResetManifest.rootSha256 -and $manifest.rootSha256 -eq $secondManifest.rootSha256)
            added                         = @($secondDelta.added)
            changed                       = @($secondDelta.changed)
            removed                       = @($secondDelta.removed)
            nunit                         = $secondNunitSummary
        }
        Write-ConsumerJsonArtifact -Path $fixedPointReportPath -Value $fixedPointReport -Depth 12
        Assert-ConsumerSecondBootstrapFixedPoint -FirstBootstrap $manifest -AfterLibraryReset $afterResetManifest -SecondBootstrap $secondManifest -Delta $secondDelta
        Assert-ConsumerImmutableManifest -Expected $manifest -Actual $secondManifest -RunLabel 'bootstrap-second-fixed-point'
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
            Write-ConsumerFailureEvidence -RunDirectory $bootstrapDirectory -RunLabel 'bootstrap' -Failure $failure -EvidencePaths @($receiptPath, $initializationConfigReceiptPath, $initializationConfigPath, $preBootstrapManifestPath, $sceneBootstrapManifestPath, $manifestPath, $deltaReportPath, $semanticTransitionPath, $commandPath, $unityLogPath, $processLogPath, $resultsPath, $nunitSummaryPath, $initializationCommandPath, $initializationUnityLogPath, $initializationProcessLogPath, $initializationReportPath, $afterResetManifestPath, $resetPath, $secondCommandPath, $secondUnityLogPath, $secondProcessLogPath, $secondResultsPath, $secondNunitSummaryPath, $secondInitializationCommandPath, $secondInitializationUnityLogPath, $secondInitializationProcessLogPath, $secondInitializationReportPath, $secondManifestPath, $secondDeltaPath, $fixedPointReportPath)
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
            passName              = $passName
            nextPassName          = $nextPassName
            requiredFragments     = if ($selectedSentinelCount -gt 0) { @($Sentinel) } else { @() }
            forbiddenFragments    = @()
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
        shaderName                   = $ShaderName
        shaderAssetPath              = Get-ProductShaderAssetPath -ShaderName $ShaderName
        expectedPassNames            = $ProductPasses
        expectedVisiblePropertyNames = $expectedVisiblePropertyNames
        requiredSourceFragments      = @()
        forbiddenSourceFragments     = @()
        passContracts                = $passContracts
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
        runLabel           = 'module-free-clean-import'
        runKind            = 'module-free'
        hasSelectedModule  = $false
        products           = @($ProductNames | ForEach-Object { New-ProductContract -ShaderName $_ })
        selectedModule     = $null
        moduleOrder        = $null
        inactiveSentinels  = $AllSentinels
        runtimeSamples     = @()
        bake               = $null
        unlitForwardAddFog = $null
    }
}

function New-ModuleFreeToonRuntimeObservationContract {
    $contract = New-ModuleFreeContract
    $contract.runLabel = 'module-free-toon-runtime-observation'
    $contract.runKind = 'module-free-toon-runtime-observation'
    $range = { param([double]$Minimum, [double]$Maximum) [ordered]@{ minimum = $Minimum; maximum = $Maximum } }
    $contract.runtimeSamples = @([ordered]@{
            label             = 'module-free-toon-center-pixel'
            shaderName        = 'PureBase/Toon'
            shaderAssetPath   = Get-ProductShaderAssetPath -ShaderName 'PureBase/Toon'
            floatAssignments  = @()
            includePointLight = $true
            red               = & $range 0.0 1000.0
            green             = & $range 0.0 1000.0
            blue              = & $range 0.0 1000.0
            alpha             = & $range 0.99 1.01
        })
    return $contract
}

function New-InitialValidationMatrix {
    param([Parameter()][switch]$ModuleFreeOnly)

    $matrix = New-Object System.Collections.Generic.List[object]
    $matrix.Add([ordered]@{ label = 'module-free-clean-import'; contract = New-ModuleFreeContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerModuleFreeImportTests.ModuleFreeProductsCompileWithConfiguredPassPropertyAndSourceContracts'; selections = @{}; skipColdLibraryReset = $false })
    if (-not $ModuleFreeOnly) {
        $matrix.Add([ordered]@{ label = 'module-free-toon-runtime-observation'; contract = New-ModuleFreeToonRuntimeObservationContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerRuntimeTests.ConfiguredRuntimeSamplesProduceExpectedBirpReadbacks'; selections = @{}; requiresColdLibraryReset = $true; skipColdLibraryReset = $false })
    }
    return , $matrix
}

function Add-StandardMorphComparisonMatrixRows {
    param([Parameter(Mandatory = $true)][System.Collections.Generic.List[object]]$Matrix)

    $module = [ordered]@{ label = 'standard-morph'; phase = 'morph'; uniqueId = 'jp.penguin.purebase.release.fixture.products.morph'; propertyName = ''; sentinel = 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_MORPH' }
    $selections = @{ 'PureBase/Unlit' = @($module.uniqueId); 'PureBase/Toon' = @($module.uniqueId); 'PureBase/PBR' = @($module.uniqueId); 'PureBase/Hybrid' = @($module.uniqueId) }
    $warmContract = New-PhaseContract -Module $module -SelectedProducts $ProductNames
    $warmContract.runLabel = 'standard-morph-warm-library-duplicate-evidence'
    $Matrix.Add([ordered]@{ label = $warmContract.runLabel; contract = $warmContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerStandardMorphObservationTests.StandardMorphProductsRecordPassCountObservations'; selections = $selections; skipColdLibraryReset = $true; allowObservationEvidence = $true })
    $coldContract = New-PhaseContract -Module $module -SelectedProducts $ProductNames
    $coldContract.runLabel = 'standard-morph-cold-library-legacy-counts'
    $Matrix.Add([ordered]@{ label = $coldContract.runLabel; contract = $coldContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerProductPhaseTests.SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage'; selections = $selections; skipColdLibraryReset = $false; allowObservationEvidence = $false })
    return [ordered]@{ warmContract = $warmContract; coldContract = $coldContract }
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
        runLabel           = $Module.label
        runKind            = 'product-phase'
        hasSelectedModule  = $true
        products           = @($SelectedProducts | ForEach-Object { New-ProductContract -ShaderName $_ -Sentinel $Module.sentinel -PassSentinelCounts $PassSentinelCounts })
        selectedModule     = [ordered]@{ phase = $Module.phase; moduleUniqueId = $Module.uniqueId; propertyName = $Module.propertyName; sentinel = $Module.sentinel }
        moduleOrder        = $null
        inactiveSentinels  = @($AllSentinels | Where-Object { $_ -ne $Module.sentinel })
        runtimeSamples     = @()
        bake               = $null
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
            label             = $Module.label
            shaderName        = 'PureBase/Toon'
            shaderAssetPath   = Get-ProductShaderAssetPath -ShaderName 'PureBase/Toon'
            floatAssignments  = @()
            includePointLight = $true
            red               = $runtimeRanges.red
            green             = $runtimeRanges.green
            blue              = $runtimeRanges.blue
            alpha             = $runtimeRanges.alpha
        })
    $contract.runtimeDelta = [ordered]@{
        sampleLabel             = $Module.label
        moduleFreeReference     = $moduleFreeReference
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
    $moduleUniqueId = 'jp.penguin.purebase.release.fixture.unlit.forwardaddfog'
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
            product                    = $product
            moduleUniqueId             = $moduleUniqueId
            sentinel                   = 'PUREBASE_UNLIT_FORWARD_ADD_FOG_SENTINEL'
            floatAssignments           = @([ordered]@{ propertyName = Get-ShaderCoreNamespacedPropertyName -ModuleUniqueId $moduleUniqueId -RawPropertyName $rawPropertyName; value = 1.0 })
            fog                        = [ordered]@{ mode = 'Exponential'; color = [ordered]@{ red = 0.0; green = 0.0; blue = 0.0; alpha = 1.0 }; density = 3.0 }
            cameraFieldOfView          = 60.0
            fogDisabledSignalMagnitude = [ordered]@{ minimum = 0.0001; maximum = 1000.0 }
            retainedSignalFraction     = [ordered]@{ minimum = 0.0; maximum = 1.0 }
            blackFogRed                = [ordered]@{ minimum = 0.0; maximum = 0.1 }
            blackFogGreen              = [ordered]@{ minimum = 0.0; maximum = 0.1 }
            blackFogBlue               = [ordered]@{ minimum = 0.0; maximum = 0.1 }
            blackFogAlpha              = [ordered]@{ minimum = 0.0; maximum = 1.0 }
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
        return , $selectedMatrix
    }
    if ($FogOnly) {
        $selectedMatrix = @($Matrix | Where-Object { $_.label -eq 'unlit-forward-add-fog' })
        return , $selectedMatrix
    }
    $selectedMatrix = @($Matrix)
    return , $selectedMatrix
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
                productName             = $shaderName
                generatedSourceFileName = $expectedGeneratedSourceFileName
                passCounts              = $passCounts.ToArray()
                warmClassification      = Get-StandardMorphObservationClassification -PassCounts $passCounts.ToArray()
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
                productName             = $productName
                generatedSourceFileName = $generatedSourceFileName
                passCounts              = $passCounts
                coldClassification      = Get-StandardMorphObservationClassification -PassCounts $passCounts
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
        schemaName        = 'purebase-standard-morph-comparison-verdict'
        schemaVersion     = 1
        comparisonName    = 'warm-cold-standard-morph'
        moduleFreeRunPath = 'runs/module-free-clean-import'
        warmRunPath       = 'runs/' + $WarmContract.runLabel
        coldRunPath       = 'runs/' + $ColdContract.runLabel
        status            = 'failed'
        products          = @()
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
                    productName        = $warmProduct.productName
                    warmClassification = $warmProduct.warmClassification
                    coldClassification = $coldClassification
                    coldCanonical      = [bool]($coldClassification -eq 'canonical')
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
        [Parameter()][switch]$RequireColdLibraryReset,
        [Parameter()][switch]$SkipColdLibraryReset,
        [Parameter()][switch]$AllowObservationEvidence
    )

    $runDirectory = Join-Path $RunRoot ('runs/' + $Contract.runLabel)
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $requiresColdLibraryReset = -not $SkipColdLibraryReset -and ($Selections.Count -gt 0 -or $RequireColdLibraryReset)
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
        required                 = [bool]$requiresColdLibraryReset
        attempted                = $false
        completed                = $false
        resetCount               = [int]$script:coldLibraryResetCount
        priorLibraryPresent      = $null
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
        [Parameter(Mandatory = $true)][bool]$Failed,
        [Parameter()][AllowEmptyString()][string]$CleanupFailure = '',
        [Parameter()][AllowEmptyString()][string]$CleanupStatus = 'not-attempted',
        [Parameter()][AllowEmptyString()][string]$CleanupReason = ''
    )

    [ordered]@{ consumerDirectoryCreationCount = $ConsumerCreated; consumerDirectoryRemovalCount = $ConsumerRemoved; consumerDirectoryRemovalFailed = -not [string]::IsNullOrEmpty($CleanupFailure); consumerDirectoryPresentAfterCleanup = Test-Path -LiteralPath (Join-Path $RunRoot 'ConsumerProject'); coldLibraryResetCount = $script:coldLibraryResetCount; keepConsumer = $KeepConsumer; failed = $Failed; cleanupStatus = $CleanupStatus; cleanupReason = $CleanupReason; cleanupFailure = $CleanupFailure } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunRoot 'cleanup-summary.json') -Encoding UTF8
}

function Remove-ConsumerProject {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $processDiscovery = Get-ConsumerUnityProcess -ProjectRoot $ConsumerRoot
    if ($processDiscovery.status -ne 'none') {
        $message = if ($processDiscovery.status -eq 'active') {
            "Unity process $($processDiscovery.process.ProcessId) is using '$ConsumerRoot'; refusing consumer cleanup."
        }
        elseif ($processDiscovery.status -eq 'indeterminate') {
            "Cannot verify whether a Unity process is using '$ConsumerRoot'; refusing consumer cleanup. $($processDiscovery.reason)"
        }
        else {
            "Unity process discovery returned unsupported status '$($processDiscovery.status)' for '$ConsumerRoot'; refusing consumer cleanup. $($processDiscovery.reason)"
        }
        $exception = [System.InvalidOperationException]::new($message)
        $exception.Data['cleanupStatus'] = [string]$processDiscovery.status
        $exception.Data['cleanupReason'] = [string]$processDiscovery.reason
        throw $exception
    }
    Remove-Item -LiteralPath $ConsumerRoot -Recurse -Force
    return [ordered]@{ cleanupStatus = 'removed'; cleanupReason = $processDiscovery.reason }
}

$packageRoot = Get-PackageGitRoot
if ($ModuleFreeOnly -and $CompareWarmAndColdStandardMorph) {
    throw '-ModuleFreeOnly cannot be combined with -CompareWarmAndColdStandardMorph because the latter requires the four-row standard-morph comparison: module-free import, module-free Toon runtime observation, warm, and cold.'
}
if ($ToonBaseOnly -and $CompareWarmAndColdStandardMorph) {
    throw '-ToonBaseOnly cannot be combined with -CompareWarmAndColdStandardMorph because the latter requires the four-row standard-morph comparison: module-free import, module-free Toon runtime observation, warm, and cold.'
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
$executionFailure = $null
try {
    $archiveDirectory = Join-Path $runRoot 'archive'
    & (Join-Path $scriptRoot 'Build-PureBaseRelease.ps1') -OutputDirectory $archiveDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Approved release archive builder failed.' }
    $zipPath = Join-Path $archiveDirectory 'jp.penguin.purebase-0.1.0.zip'
    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) { throw 'Approved release archive builder did not produce the expected ZIP.' }
    $shaderCoreManifestPath = Join-Path $runRoot 'shader-core-0.1.9.sha256.json'
    Copy-Item -LiteralPath (Join-Path $scriptRoot 'shader-core-0.1.9.sha256.json') -Destination $shaderCoreManifestPath -Force

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
    $canonicalShaderCoreConfigPath = Join-Path $packageRoot 'Tests/Config/shader-core-test-hosts.json'
    $canonicalShaderCoreConfigDestination = Join-Path $consumerRoot (Get-CanonicalShaderCoreConfigDestination).Replace('/', '\')
    New-Item -ItemType Directory -Path (Split-Path -Parent $canonicalShaderCoreConfigDestination) -Force | Out-Null
    Copy-Item -LiteralPath $canonicalShaderCoreConfigPath -Destination $canonicalShaderCoreConfigDestination -Force
    $stagingReceipt = Get-ConsumerStagingReceipt -ZipPath $zipPath -ScaffoldRoot $scaffoldRoot -ShaderCoreRoot $shaderCoreRoot -ModulesRoot (Join-Path $scriptRoot 'Modules') -FixturesRoot (Join-Path $packageRoot 'Tests/Fixtures') -CanonicalShaderCoreConfigPath $canonicalShaderCoreConfigPath
    $bootstrapManifest = Invoke-ConsumerBootstrapImport -UnityEditor $unityEditor -ConsumerRoot $consumerRoot -RunRoot $runRoot -ZipPath $zipPath -ShaderCoreManifestPath $shaderCoreManifestPath -StagingReceipt $stagingReceipt

    $comparisonWarmContract = $null
    $comparisonColdContract = $null
    $comparisonVerdict = $null
    $matrix = New-InitialValidationMatrix -ModuleFreeOnly:$ModuleFreeOnly
    if (-not $ModuleFreeOnly) {
        $standardPhases = @('morph', 'postvertex', 'base', 'light', 'customlight', 'modifylight', 'shade', 'reflection', 'add', 'postpixel')
        if ($ToonBaseOnly) {
            $moduleUniqueId = 'jp.penguin.purebase.release.fixture.toon.phase.base'
            $rawPropertyName = '_ProductPhaseValue'
            $module = [ordered]@{ label = 'toon-base'; phase = 'base'; uniqueId = $moduleUniqueId; propertyName = Get-ShaderCoreNamespacedPropertyName -ModuleUniqueId $moduleUniqueId -RawPropertyName $rawPropertyName; sentinel = 'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_BASE' }
            $matrix.Add([ordered]@{ label = $module.label + '-phase'; contract = New-PhaseContract -Module $module -SelectedProducts @('PureBase/Toon'); filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerProductPhaseTests.SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage'; selections = @{ 'PureBase/Toon' = @($module.uniqueId) }; skipColdLibraryReset = $false })
            $matrix.Add([ordered]@{ label = $module.label + '-runtime'; contract = New-ToonRuntimeContract -Module $module; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerRuntimeTests.ConfiguredRuntimeSamplesProduceExpectedBirpReadbacks'; selections = @{ 'PureBase/Toon' = @($module.uniqueId) }; skipColdLibraryReset = $false })
        }
        elseif ($CompareWarmAndColdStandardMorph) {
            $comparisonContracts = Add-StandardMorphComparisonMatrixRows -Matrix $matrix
            $comparisonWarmContract = $comparisonContracts.warmContract
            $comparisonColdContract = $comparisonContracts.coldContract
        }
        else {
            foreach ($phase in $standardPhases) {
                $module = [ordered]@{ label = 'standard-' + $phase; phase = $phase; uniqueId = 'jp.penguin.purebase.release.fixture.products.' + $phase; propertyName = ''; sentinel = 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_' + $phase.ToUpperInvariant() }
                $matrix.Add([ordered]@{ label = $module.label; contract = New-PhaseContract -Module $module -SelectedProducts $ProductNames; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerProductPhaseTests.SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage'; selections = @{ 'PureBase/Unlit' = @($module.uniqueId); 'PureBase/Toon' = @($module.uniqueId); 'PureBase/PBR' = @($module.uniqueId); 'PureBase/Hybrid' = @($module.uniqueId) }; skipColdLibraryReset = $false })
            }
        }
        if (-not $ToonBaseOnly -and -not $CompareWarmAndColdStandardMorph) {
            foreach ($phase in @('base', 'light', 'modifylight', 'shade')) {
                $moduleUniqueId = 'jp.penguin.purebase.release.fixture.toon.phase.' + $phase
                $rawPropertyName = '_ProductPhaseValue'
                $module = [ordered]@{ label = 'toon-' + $phase; phase = $phase; uniqueId = $moduleUniqueId; propertyName = Get-ShaderCoreNamespacedPropertyName -ModuleUniqueId $moduleUniqueId -RawPropertyName $rawPropertyName; sentinel = 'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_' + $phase.ToUpperInvariant() }
                $phaseContract = New-PhaseContract -Module $module -SelectedProducts @('PureBase/Toon')
                $runtimeContract = New-ToonRuntimeContract -Module $module
                $matrix.Add([ordered]@{ label = $module.label + '-phase'; contract = $phaseContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerProductPhaseTests.SelectedExternalModuleCompilesInConfiguredProductsWithNoInactiveSentinelLeakage'; selections = @{ 'PureBase/Toon' = @($module.uniqueId) }; skipColdLibraryReset = $false })
                $matrix.Add([ordered]@{ label = $module.label + '-runtime'; contract = $runtimeContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerRuntimeTests.ConfiguredRuntimeSamplesProduceExpectedBirpReadbacks'; selections = @{ 'PureBase/Toon' = @($module.uniqueId) }; skipColdLibraryReset = $false })
            }
            $matrix.Add([ordered]@{ label = 'unlit-forward-add-fog'; contract = New-FogContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerUnlitForwardAddFogTests.SelectedForwardAddSignalAttenuatesTowardBlackWithControlledFog'; selections = @{ 'PureBase/Unlit' = @('jp.penguin.purebase.release.fixture.unlit.forwardaddfog') }; skipColdLibraryReset = $false })
            $matrix.Add([ordered]@{ label = 'module-order'; contract = New-ModuleOrderContract; filter = 'PureBase.Release.Consumer.Tests.PureBaseConsumerModuleOrderTests.ConfiguredModuleOrderAppearsOnlyInExpectedProductPasses'; selections = @{ 'PureBase/Unlit' = @('jp.penguin.purebase.release.fixture.module-order.alpha', 'jp.penguin.purebase.release.fixture.module-order.zeta'); 'PureBase/Toon' = @('jp.penguin.purebase.release.fixture.module-order.alpha', 'jp.penguin.purebase.release.fixture.module-order.zeta'); 'PureBase/PBR' = @('jp.penguin.purebase.release.fixture.module-order.alpha', 'jp.penguin.purebase.release.fixture.module-order.zeta'); 'PureBase/Hybrid' = @('jp.penguin.purebase.release.fixture.module-order.alpha', 'jp.penguin.purebase.release.fixture.module-order.zeta') }; skipColdLibraryReset = $false })
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
        $requireColdLibraryReset = $false
        if ($entry.Contains('requiresColdLibraryReset')) {
            $requireColdLibraryReset = [bool]$entry.requiresColdLibraryReset
        }
        $outcomes += [ordered]@{ label = $entry.label; runDirectoryLabel = $entry.contract.runLabel; nunit = Invoke-ConsumerTest -UnityEditor $unityEditor -ConsumerRoot $consumerRoot -RunRoot $runRoot -ZipPath $zipPath -ShaderCoreManifestPath $shaderCoreManifestPath -Contract $entry.contract -TestFilter $entry.filter -Selections $entry.selections -RequireColdLibraryReset:$requireColdLibraryReset -SkipColdLibraryReset:$entry.skipColdLibraryReset -AllowObservationEvidence:$allowObservationEvidence }
    }
    if ($CompareWarmAndColdStandardMorph) {
        $expectedComparisonLabels = @('module-free-clean-import', 'module-free-toon-runtime-observation', 'standard-morph-warm-library-duplicate-evidence', 'standard-morph-cold-library-legacy-counts')
        $actualComparisonLabels = @($matrix | ForEach-Object { [string]$_.label })
        if ($matrix.Count -ne 4 -or $null -eq $comparisonWarmContract -or $null -eq $comparisonColdContract -or [string]::Join('|', $actualComparisonLabels) -ne [string]::Join('|', $expectedComparisonLabels)) {
            throw 'Standard-morph comparison must execute exactly module-free import, module-free Toon runtime observation, warm, and cold rows.'
        }
        $comparisonVerdict = Invoke-StandardMorphComparisonVerdict -RunRoot $runRoot -WarmContract $comparisonWarmContract -ColdContract $comparisonColdContract
    }
    $validationScope = if ($ModuleFreeOnly) { 'module-free-diagnostic-only' } elseif ($ToonBaseOnly) { 'toon-base-diagnostic-only' } elseif ($FogOnly) { 'unlit-forward-add-fog-diagnostic-only' } elseif ($BakeOnly) { 'progressive-cpu-bake-diagnostic-only' } elseif ($CompareWarmAndColdStandardMorph) { 'warm-cold-standard-morph-comparison' } else { 'full-release-validation-matrix' }
    Write-ReleaseRunSummary -RunRoot $runRoot -ConsumerCreated $consumerCreated -ConsumerRemoved $consumerRemoved -ValidationScope $validationScope -ComparisonMode ([bool]$CompareWarmAndColdStandardMorph) -ModuleFreeOnly ([bool]$ModuleFreeOnly) -Outcomes $outcomes -ComparisonVerdict $comparisonVerdict
    $failed = $false
}
catch {
    $executionFailure = $_
    throw
}
finally {
    $cleanupFailure = $null
    $cleanupStatus = if ($KeepConsumer) { 'not-requested' } else { 'not-required' }
    $cleanupReason = if ($KeepConsumer) { 'Consumer retention was requested.' } else { 'Consumer project was not present for cleanup.' }
    if (-not $KeepConsumer -and (Test-Path -LiteralPath $consumerRoot)) {
        try {
            $cleanupResult = Remove-ConsumerProject -ConsumerRoot $consumerRoot
            $consumerRemoved++
            $cleanupStatus = $cleanupResult.cleanupStatus
            $cleanupReason = $cleanupResult.cleanupReason
        }
        catch {
            $cleanupFailure = $_
            $cleanupStatus = if ($_.Exception.Data.Contains('cleanupStatus')) { [string]$_.Exception.Data['cleanupStatus'] } else { 'failed' }
            $cleanupReason = if ($_.Exception.Data.Contains('cleanupReason')) { [string]$_.Exception.Data['cleanupReason'] } else { $_.Exception.Message }
            Write-ConsumerJsonArtifact -Path (Join-Path $runRoot 'cleanup-failure.json') -Value ([ordered]@{
                    cleanupStatus    = $cleanupStatus
                    cleanupReason    = $cleanupReason
                    cleanupFailure   = $_.Exception.Message
                    executionFailure = if ($null -ne $executionFailure) { $executionFailure.Exception.Message } else { '' }
                })
        }
    }
    $cleanupFailureMessage = if ($null -ne $cleanupFailure) { $cleanupFailure.Exception.Message } else { '' }
    Write-ReleaseCleanupSummary -RunRoot $runRoot -ConsumerCreated $consumerCreated -ConsumerRemoved $consumerRemoved -KeepConsumer ([bool]$KeepConsumer) -Failed ($failed -or ($null -ne $cleanupFailure)) -CleanupFailure $cleanupFailureMessage -CleanupStatus $cleanupStatus -CleanupReason $cleanupReason
    if ($null -ne $cleanupFailure) {
        if ($failed) {
            throw "Release validation execution failed: $($executionFailure.Exception.Message). Consumer cleanup failed: $cleanupFailureMessage. See '$runRoot\cleanup-failure.json'."
        }
        throw $cleanupFailure
    }
}

if ($consumerCreated -ne 1 -or ((-not $KeepConsumer) -and $consumerRemoved -ne 1)) {
    throw "Consumer lifecycle contract failed: created=$consumerCreated removed=$consumerRemoved."
}

Write-Host "Pure-Base release consumer validation passed. Artifacts: '$runRoot'."
