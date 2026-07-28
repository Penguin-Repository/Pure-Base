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

# Validates versioned PureBase legacy, daily, and release parity evidence before legacy-harness deletion.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LegacyArtifactRoot,
    [Parameter(Mandatory = $true)][string]$DailyArtifactRoot,
    [Parameter(Mandatory = $true)][string]$ReleaseArtifactRoot,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter()][string]$OutputDirectory,
    [Parameter()][string]$ReportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Validate-PureBaseParity.Oracle.ps1')

function Get-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [System.IO.Path]::GetFullPath($Path)
}

function Test-PathEqualOrDescendant {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Candidate
    )

    $normalizedRoot = (Get-FullPath -Path $Root).TrimEnd('\', '/')
    $normalizedCandidate = (Get-FullPath -Path $Candidate).TrimEnd('\', '/')
    return [string]::Equals($normalizedRoot, $normalizedCandidate, [System.StringComparison]::OrdinalIgnoreCase) -or
        $normalizedCandidate.StartsWith($normalizedRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-JsonArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure -Failures $Failures -Code $Code -Message "Required artifact is missing: '$Path'."
        return $null
    }

    try {
        $artifact = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if ($null -eq $artifact -or $artifact -isnot [pscustomobject]) { throw 'JSON artifact must be an object.' }
        return $artifact
    }
    catch {
        Add-Failure -Failures $Failures -Code $Code -Message "Required artifact is invalid JSON: '$Path'."
        return $null
    }
}

function Get-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-Failure -Failures $Failures -Code $Code -Message "Required artifact is missing: '$Path'."
        return $null
    }
    return Get-Item -LiteralPath $Path -Force
}

function Get-StringProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        Add-Failure -Failures $Failures -Code $Code -Message "Artifact is missing non-empty string property '$Name'."
        return $null
    }
    return [string]$property.Value
}

function Get-StringPropertyAlias {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string[]]$Names,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $present = @($Names | Where-Object { $null -ne $Object.PSObject.Properties[$_] })
    if ($present.Count -ne 1) {
        Add-Failure -Failures $Failures -Code $Code -Message "Artifact must contain exactly one graphics API alias: '$($Names -join "' or '")'."
        return $null
    }
    return Get-StringProperty -Object $Object -Name $present[0] -Failures $Failures -Code $Code
}

function Get-BooleanProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) {
        Add-Failure -Failures $Failures -Code $Code -Message "Artifact is missing Boolean property '$Name'."
        return $null
    }
    return [bool]$property.Value
}

function Test-JsonNumber {
    param([Parameter(Mandatory = $true)]$Value)

    if ($null -eq $Value) { return $false }
    return [System.Type]::GetTypeCode($Value.GetType()) -in @([System.TypeCode]::SByte, [System.TypeCode]::Byte, [System.TypeCode]::Int16, [System.TypeCode]::UInt16, [System.TypeCode]::Int32, [System.TypeCode]::UInt32, [System.TypeCode]::Int64, [System.TypeCode]::UInt64, [System.TypeCode]::Single, [System.TypeCode]::Double, [System.TypeCode]::Decimal)
}

function Test-StringArrayProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter()][int]$ExpectedCount = -1,
        [Parameter()][AllowEmptyCollection()][string[]]$RequiredValues = @()
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value -or $property.Value -is [string] -or -not ($property.Value -is [System.Collections.IEnumerable])) {
        Add-Failure -Failures $Failures -Code $Code -Message "Artifact is missing array property '$Name'."
        return $false
    }
    $items = @($property.Value)
    if ($ExpectedCount -ge 0 -and $items.Count -ne $ExpectedCount) {
        Add-Failure -Failures $Failures -Code $Code -Message "Artifact array '$Name' has an unexpected entry count."
        return $false
    }
    foreach ($item in $items) {
        if ($item -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$item)) {
            Add-Failure -Failures $Failures -Code $Code -Message "Artifact array '$Name' contains a null, non-string, or empty entry."
            return $false
        }
    }
    foreach ($value in $RequiredValues) {
        if ((@($items | Where-Object { [string]$_ -eq $value })).Count -ne 1) {
            Add-Failure -Failures $Failures -Code $Code -Message "Artifact array '$Name' is missing or duplicates required entry '$value'."
            return $false
        }
    }
    return $true
}

function Test-ValidationSceneEvidence {
    param(
        [Parameter(Mandatory = $true)]$Scene,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $valid = $true
    $staticLightmaps = Get-ArrayPropertyItems -Object $Scene -Name 'staticLightmaps' -Failures $Failures -Code $Code
    if ($null -ne $staticLightmaps) {
        foreach ($entry in $staticLightmaps) {
            if ($null -eq $entry -or $entry -isnot [pscustomobject] -or
                $null -eq (Get-StringProperty -Object $entry -Name 'renderer' -Failures $Failures -Code $Code) -or
                $null -eq (Get-IntegerProperty -Object $entry -Name 'lightmapIndex' -Failures $Failures -Code $Code) -or
                (@(@('scaleOffsetX', 'scaleOffsetY', 'scaleOffsetZ', 'scaleOffsetW') | Where-Object { $null -eq $entry.PSObject.Properties[$_] -or -not (Test-JsonNumber -Value $entry.PSObject.Properties[$_].Value) })).Count -ne 0) {
                Add-Failure -Failures $Failures -Code $Code -Message 'Static-lightmap evidence contains a null, incomplete, or malformed entry.'
                $valid = $false
                break
            }
        }
    }
    else { $valid = $false }

    $metaAlbedo = Get-ArrayPropertyItems -Object $Scene -Name 'metaAlbedo' -Failures $Failures -Code $Code
    if ($null -ne $metaAlbedo) {
        foreach ($entry in $metaAlbedo) {
            if ($null -eq $entry -or $entry -isnot [pscustomobject] -or
                $null -eq (Get-StringProperty -Object $entry -Name 'material' -Failures $Failures -Code $Code) -or
                $null -eq (Get-StringProperty -Object $entry -Name 'shader' -Failures $Failures -Code $Code) -or
                $entry.shader -notmatch '^PureBase/(Unlit|Toon|PBR|Hybrid)$' -or
                $null -eq $entry.PSObject.Properties['meanLuminance'] -or -not (Test-JsonNumber -Value $entry.PSObject.Properties['meanLuminance'].Value)) {
                Add-Failure -Failures $Failures -Code $Code -Message 'Meta-albedo evidence contains a null, incomplete, or malformed entry.'
                $valid = $false
                break
            }
        }
    }
    else { $valid = $false }

    $variants = Get-ArrayPropertyItems -Object $Scene -Name 'variants' -Failures $Failures -Code $Code
    if ($null -ne $variants) {
        foreach ($entry in $variants) {
            if ($null -eq $entry -or $entry -isnot [pscustomobject] -or
                $null -eq (Get-StringProperty -Object $entry -Name 'shader' -Failures $Failures -Code $Code) -or
                $null -eq (Get-StringProperty -Object $entry -Name 'pass' -Failures $Failures -Code $Code) -or
                $null -eq (Get-StringProperty -Object $entry -Name 'label' -Failures $Failures -Code $Code) -or
                -not (Test-StringArrayProperty -Object $entry -Name 'keywords' -Failures $Failures -Code $Code) -or
                $true -ne (Get-BooleanProperty -Object $entry -Name 'added' -Failures $Failures -Code $Code) -or
                $true -ne (Get-BooleanProperty -Object $entry -Name 'warmed' -Failures $Failures -Code $Code) -or
                $null -eq (Get-IntegerProperty -Object $entry -Name 'variantCount' -Failures $Failures -Code $Code) -or
                [int]$entry.variantCount -ne 1) {
                Add-Failure -Failures $Failures -Code $Code -Message 'Variant evidence contains a null, incomplete, or malformed warmed entry.'
                $valid = $false
                break
            }
        }
    }
    else { $valid = $false }

    return $valid
}

function Test-NUnitArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter()][string]$ExpectedFullName,
        [Parameter()][switch]$RequireSingleTest
    )

    $file = Get-RequiredFile -Path $Path -Failures $Failures -Code $Code
    if ($null -eq $file) { return $null }
    try {
        $document = New-Object System.Xml.XmlDocument
        $document.Load($Path)
        $root = $document.DocumentElement
        if ($null -eq $root) { throw 'NUnit XML has no document element.' }
        $total = [int]$root.GetAttribute('total')
        $passed = [int]$root.GetAttribute('passed')
        $failed = [int]$root.GetAttribute('failed')
        $skipped = if ([string]::IsNullOrEmpty($root.GetAttribute('skipped'))) { 0 } else { [int]$root.GetAttribute('skipped') }
        $inconclusive = if ([string]::IsNullOrEmpty($root.GetAttribute('inconclusive'))) { 0 } else { [int]$root.GetAttribute('inconclusive') }
        if ($total -le 0 -or $passed -ne $total -or $failed -ne 0 -or $skipped -ne 0 -or $inconclusive -ne 0) {
            Add-Failure -Failures $Failures -Code $Code -Message "NUnit artifact is not fully passing: '$Path'."
        }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedFullName)) {
            $nodes = @($document.SelectNodes("//test-case[@fullname='$ExpectedFullName']"))
            if ($nodes.Count -ne 1 -or $nodes[0].GetAttribute('result') -ne 'Passed') {
                Add-Failure -Failures $Failures -Code $Code -Message "NUnit artifact does not contain exactly one passing expected test '$ExpectedFullName': '$Path'."
            }
        }
        if ($RequireSingleTest -and $total -ne 1) {
            Add-Failure -Failures $Failures -Code $Code -Message "Legacy invocation artifact must contain one test: '$Path'."
        }
        return [ordered]@{ total = $total; passed = $passed; failed = $failed; skipped = $skipped; inconclusive = $inconclusive }
    }
    catch {
        Add-Failure -Failures $Failures -Code $Code -Message "NUnit artifact cannot be parsed: '$Path'."
        return $null
    }
}

function Test-Hash {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value -match '^[0-9a-fA-F]{64}$'
}

function Test-ReleaseZip {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$ReleaseContentContract,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures
    )

    if ($null -eq (Get-RequiredFile -Path $Path -Failures $Failures -Code 'release-zip-missing')) { return }
    $requiredEntriesProperty = $ReleaseContentContract.PSObject.Properties['requiredEntries']
    if ($null -eq $requiredEntriesProperty -or $null -eq $requiredEntriesProperty.Value -or $requiredEntriesProperty.Value -is [string] -or -not ($requiredEntriesProperty.Value -is [System.Collections.IEnumerable])) {
        Add-Failure -Failures $Failures -Code 'release-zip-contract' -Message 'Release content contract must provide requiredEntries as a nonempty array.'
        return
    }
    $requiredEntries = @($requiredEntriesProperty.Value)
    $requiredEntrySet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($requiredEntry in $requiredEntries) {
        if ($requiredEntry -isnot [string] -or [string]::IsNullOrWhiteSpace([string]$requiredEntry) -or -not $requiredEntrySet.Add([string]$requiredEntry)) {
            Add-Failure -Failures $Failures -Code 'release-zip-contract' -Message 'Release content contract has an empty, non-string, or duplicate required entry.'
            return
        }
    }
    if ($requiredEntrySet.Count -eq 0) {
        Add-Failure -Failures $Failures -Code 'release-zip-contract' -Message 'Release content contract must require at least one release entry.'
        return
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
        try {
            $archiveEntrySet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
            foreach ($entry in $archive.Entries) {
                $entryName = $entry.FullName.Replace('\', '/')
                [void]$archiveEntrySet.Add($entryName)
                if ($entryName -match '(^|/)Tests(/|$)' -or $entryName -match '\.scmodule$') {
                    Add-Failure -Failures $Failures -Code 'release-zip-content' -Message "Release ZIP contains prohibited entry '$entryName'."
                }
            }
            foreach ($requiredEntry in $requiredEntrySet) {
                if (-not $archiveEntrySet.Contains($requiredEntry)) {
                    Add-Failure -Failures $Failures -Code 'release-zip-required-entry' -Message "Release ZIP omits required contract entry '$requiredEntry'."
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    catch {
        Add-Failure -Failures $Failures -Code 'release-zip-invalid' -Message "Release ZIP cannot be opened: '$Path'."
    }
}

function Test-StagingReceipt {
    param(
        [Parameter()]$Receipt,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures
    )

    if ($null -eq $Receipt) { return }
    if ($Receipt.schemaName -ne 'purebase-consumer-staging-receipt' -or [int]$Receipt.schemaVersion -ne 1 -or $Receipt.pathOrdering -ne 'System.StringComparer.Ordinal') {
        Add-Failure -Failures $Failures -Code 'staging-receipt-schema' -Message 'Staging receipt has an unsupported schema.'
        return
    }
    $destinations = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    $entries = @($Receipt.entries)
    $previousDestination = $null
    if ($entries.Count -eq 0) {
        Add-Failure -Failures $Failures -Code 'staging-receipt-empty' -Message 'Staging receipt has no entries.'
    }
    foreach ($entry in $entries) {
        $destination = if ($null -eq $entry) { '' } else { [string]$entry.destination }
        if ($null -eq $entry -or [string]::IsNullOrWhiteSpace($destination) -or [string]::IsNullOrWhiteSpace([string]$entry.sourceKind) -or [string]::IsNullOrWhiteSpace([string]$entry.source) -or -not (Test-Hash -Value ([string]$entry.sha256)) -or -not $destinations.Add($destination) -or ($null -ne $previousDestination -and [string]::CompareOrdinal($previousDestination, $destination) -ge 0)) {
            Add-Failure -Failures $Failures -Code 'staging-receipt-integrity' -Message 'Staging receipt contains an empty, duplicate, unordered, unhashed, or source-incomplete entry.'
        }
        $previousDestination = $destination
    }
}

function Test-OrdinalPathEntries {
    param(
        [Parameter(Mandatory = $true)]$Entries,
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $paths = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    $previousPath = $null
    foreach ($entry in @($Entries)) {
        $path = if ($null -eq $entry -or $null -eq $entry.PSObject.Properties['path']) { '' } else { [string]$entry.path }
        if ([string]::IsNullOrWhiteSpace($path) -or -not $paths.Add($path) -or ($null -ne $previousPath -and [string]::CompareOrdinal($previousPath, $path) -ge 0)) {
            Add-Failure -Failures $Failures -Code $Code -Message "$Kind entries must have unique, ordinally sorted paths."
            return
        }
        $previousPath = $path
    }
}

function Test-ImmutableDelta {
    param(
        [Parameter()]$Delta,
        [Parameter(Mandatory = $true)]$Contract,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures
    )

    if ($null -eq $Delta) { return }
    if ($Delta.schemaName -ne $Contract.immutableDeltaSchemaName -or [int]$Delta.schemaVersion -ne [int]$Contract.schemaVersion -or $Delta.classification -ne $Contract.classification -or $Delta.pathOrdering -ne $Contract.pathOrdering -or -not (Test-Hash -Value ([string]$Delta.preBootstrapRootSha256)) -or -not (Test-Hash -Value ([string]$Delta.postBootstrapRootSha256))) {
        Add-Failure -Failures $Failures -Code 'immutable-delta-schema' -Message 'Immutable bootstrap delta has an unsupported schema, classification, ordering, or root hash.'
        return
    }
    foreach ($name in @('added', 'changed', 'removed')) {
        if ($null -eq $Delta.PSObject.Properties[$name]) {
            Add-Failure -Failures $Failures -Code 'immutable-delta-shape' -Message "Immutable bootstrap delta is missing '$name'."
            return
        }
    }
    if (@($Delta.added).Count -ne [int]$Contract.added -or @($Delta.changed).Count -ne [int]$Contract.changed -or @($Delta.removed).Count -ne [int]$Contract.removed) {
        Add-Failure -Failures $Failures -Code 'immutable-delta-count' -Message 'Immutable bootstrap delta does not match the pinned first-transition counts.'
    }
    Test-OrdinalPathEntries -Entries $Delta.added -Kind 'Immutable added' -Failures $Failures -Code 'immutable-delta-order'
    Test-OrdinalPathEntries -Entries $Delta.changed -Kind 'Immutable changed' -Failures $Failures -Code 'immutable-delta-order'
    Test-OrdinalPathEntries -Entries $Delta.removed -Kind 'Immutable removed' -Failures $Failures -Code 'immutable-delta-order'
    foreach ($entry in @($Delta.added)) {
        if ($null -eq $entry -or -not (Test-Hash -Value ([string]$entry.sha256))) { Add-Failure -Failures $Failures -Code 'immutable-delta-shape' -Message 'Immutable added entry has an invalid hash.' }
    }
    foreach ($entry in @($Delta.changed)) {
        if ($null -eq $entry -or -not (Test-Hash -Value ([string]$entry.preBootstrapSha256)) -or -not (Test-Hash -Value ([string]$entry.postBootstrapSha256))) { Add-Failure -Failures $Failures -Code 'immutable-delta-shape' -Message 'Immutable changed entry has invalid hashes.' }
    }
}

function Test-ReleaseEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)]$ReleaseContentContract,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures
    )

    $layout = $Manifest.releaseArtifactLayout
    $contract = $layout.initialBootstrapTransition
    Test-ReleaseZip -Path (Join-Path $Root 'archive/jp.penguin.purebase-0.1.0.zip') -ReleaseContentContract $ReleaseContentContract -Failures $Failures
    $runSummary = Get-JsonArtifact -Path (Join-Path $Root 'run-summary.json') -Failures $Failures -Code 'release-summary'
    $cleanup = Get-JsonArtifact -Path (Join-Path $Root 'cleanup-summary.json') -Failures $Failures -Code 'release-cleanup'
    if ($null -ne $cleanup) {
        $cleanupFailed = Get-BooleanProperty -Object $cleanup -Name 'failed' -Failures $Failures -Code 'release-cleanup-status'
        $consumerDirectoryRemovalFailed = Get-BooleanProperty -Object $cleanup -Name 'consumerDirectoryRemovalFailed' -Failures $Failures -Code 'release-cleanup-status'
        if ($false -ne $cleanupFailed -or $false -ne $consumerDirectoryRemovalFailed) {
            Add-Failure -Failures $Failures -Code 'release-cleanup-status' -Message 'Release cleanup must explicitly report Boolean false for failed and consumerDirectoryRemovalFailed.'
        }
    }

    $null = Test-NUnitArtifact -Path (Join-Path $Root 'bootstrap/NUnit.xml') -Failures $Failures -Code 'release-bootstrap-nunit'
    $expectedLabels = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($expectedLabel in @($layout.fullMatrixLabels)) {
        if ($null -ne $expectedLabel) {
            $null = $expectedLabels.Add([string]$expectedLabel)
        }
    }
    $actualLabels = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    if ($null -eq $runSummary -or $runSummary.validationScope -ne 'full-release-validation-matrix' -or @($runSummary.outcomes).Count -eq 0) {
        Add-Failure -Failures $Failures -Code 'release-summary-outcomes' -Message 'Release run summary is not a nonempty full validation matrix.'
    }
    elseif ($null -ne $runSummary) {
        foreach ($outcome in @($runSummary.outcomes)) {
            $label = if ($null -eq $outcome -or $null -eq $outcome.PSObject.Properties['label']) { '' } else { [string]$outcome.label }
            if ([string]::IsNullOrWhiteSpace($label) -or $label -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or -not $actualLabels.Add($label)) {
                Add-Failure -Failures $Failures -Code 'release-summary-label' -Message 'Release run summary has an empty, duplicate, or unsafe matrix label.'
                continue
            }
            if (-not $expectedLabels.Contains($label)) {
                Add-Failure -Failures $Failures -Code 'release-summary-unrecognized-label' -Message "Release run summary has an unrecognized matrix label '$label'."
            }
            $directoryLabel = [string]$layout.fullMatrixRunDirectories.PSObject.Properties[$label].Value
            if ($null -ne $outcome.PSObject.Properties['runDirectoryLabel'] -and [string]$outcome.runDirectoryLabel -ne $directoryLabel) {
                Add-Failure -Failures $Failures -Code 'release-summary-directory-label' -Message "Release run summary label '$label' does not declare the manifest-pinned directory '$directoryLabel'."
                continue
            }
            $null = Test-NUnitArtifact -Path (Join-Path $Root ('runs/' + $directoryLabel + '/NUnit.xml')) -Failures $Failures -Code 'release-run-nunit'
        }
        foreach ($expectedLabel in $expectedLabels) {
            if (-not $actualLabels.Contains($expectedLabel)) {
                Add-Failure -Failures $Failures -Code 'release-summary-missing-label' -Message "Release run summary is missing expected matrix label '$expectedLabel'."
            }
        }
    }

    $receipt = Get-JsonArtifact -Path (Join-Path $Root 'bootstrap/staging-receipt.json') -Failures $Failures -Code 'staging-receipt'
    Test-StagingReceipt -Receipt $receipt -Failures $Failures
    $delta = Get-JsonArtifact -Path (Join-Path $Root 'bootstrap/immutable-input-manifest-bootstrap-delta.json') -Failures $Failures -Code 'immutable-delta'
    Test-ImmutableDelta -Delta $delta -Contract $contract -Failures $Failures
    $semantic = Get-JsonArtifact -Path (Join-Path $Root 'bootstrap/semantic-transition-report.json') -Failures $Failures -Code 'semantic-transition'
    if ($null -ne $semantic -and ($semantic.schemaName -ne $contract.semanticTransitionSchemaName -or [int]$semantic.schemaVersion -ne [int]$contract.schemaVersion -or $semantic.verdict -ne 'accepted' -or [int]$semantic.summary.accepted -ne [int]$contract.semanticAccepted -or [int]$semantic.summary.rejected -ne [int]$contract.semanticRejected -or [int]$semantic.summary.unclassified -ne [int]$contract.semanticUnclassified)) {
        Add-Failure -Failures $Failures -Code 'semantic-rejected' -Message 'Semantic transition schema, verdict, or classification counts are invalid.'
    }
    $fixedPoint = Get-JsonArtifact -Path (Join-Path $Root 'bootstrap/second-bootstrap/fixed-point-report.json') -Failures $Failures -Code 'fixed-point'
    if ($null -ne $fixedPoint -and ($fixedPoint.schemaName -ne $contract.fixedPointSchemaName -or [int]$fixedPoint.schemaVersion -ne [int]$contract.schemaVersion -or $fixedPoint.rootsEqual -ne $true -or @($fixedPoint.added).Count -ne 0 -or @($fixedPoint.changed).Count -ne 0 -or @($fixedPoint.removed).Count -ne 0)) {
        Add-Failure -Failures $Failures -Code 'fixed-point-nonzero' -Message 'Second bootstrap did not reach a zero-delta immutable fixed point.'
    }
}

function Test-Manifest {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures
    )

    if ([int]$Manifest.schemaVersion -ne 3) { Add-Failure -Failures $Failures -Code 'manifest-schema' -Message 'Only parity manifest schemaVersion 3 is supported.' }
    if ($null -eq $Manifest.PSObject.Properties['migration'] -or $Manifest.migration -isnot [pscustomobject] -or $Manifest.migration.status -ne 'release-fixture-migration-complete') {
        Add-Failure -Failures $Failures -Code 'manifest-migration-status' -Message "Parity manifest migration status must be 'release-fixture-migration-complete'."
    }
    $aggregate = $Manifest.expectedAggregate
    if ($null -eq $aggregate -or [int]$aggregate.invocationCount -ne 62 -or [int]$aggregate.passed -ne 62 -or [int]$aggregate.failed -ne 0 -or [int]$aggregate.skipped -ne 0 -or [int]$aggregate.inconclusive -ne 0) {
        Add-Failure -Failures $Failures -Code 'manifest-aggregate' -Message 'Legacy aggregate must be exactly 62/62/0/0/0.'
    }
    $fixed = $Manifest.fixedValidationSceneOracle
    if ($null -eq $fixed -or [int]$fixed.staticLightmapCount -ne 2 -or [int]$fixed.staticRendererAssignmentCount -ne 20 -or [int]$fixed.metaReadbackCount -ne 18 -or [int]$fixed.shadowDeltaPixelCount -ne 967 -or [int]$fixed.warmedRepresentativeVariantCount -ne 56 -or $fixed.dynamicLightmapStatus -ne 'NOT_DETERMINISTIC_IN_BATCH_EDITMODE') {
        Add-Failure -Failures $Failures -Code 'manifest-fixed-oracle' -Message 'Fixed validation-scene oracle is not pinned to the captured values.'
    }
    $limitations = @($Manifest.knownLimitations | Where-Object { $_.id -eq 'dynamic-lightmaps-batch-editmode' -and $_.status -eq 'NOT_DETERMINISTIC_IN_BATCH_EDITMODE' -and $_.parityRequirement -eq 'excluded-from-verified-runtime-parity' })
    if ($limitations.Count -ne 1) { Add-Failure -Failures $Failures -Code 'manifest-known-limitation' -Message 'Dynamic-lightmaps limitation is missing or changed.' }
    $releaseMatrixLabels = @()
    if ($null -ne $Manifest.releaseArtifactLayout -and $null -ne $Manifest.releaseArtifactLayout.PSObject.Properties['fullMatrixLabels']) {
        $releaseMatrixLabels = @($Manifest.releaseArtifactLayout.fullMatrixLabels)
    }
    $canonicalReleaseMatrixLabels = @(
        'module-free-clean-import', 'standard-morph', 'standard-postvertex', 'standard-base', 'standard-light', 'standard-customlight', 'standard-modifylight', 'standard-shade', 'standard-reflection', 'standard-add', 'standard-postpixel',
        'toon-base-phase', 'toon-base-runtime', 'toon-light-phase', 'toon-light-runtime', 'toon-modifylight-phase', 'toon-modifylight-runtime', 'toon-shade-phase', 'toon-shade-runtime',
        'unlit-forward-add-fog', 'module-order', 'progressive-cpu-bake'
    )
    $releaseMatrixLabelSet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    if ($releaseMatrixLabels.Count -ne 22) {
        Add-Failure -Failures $Failures -Code 'manifest-release-matrix-labels' -Message 'Manifest must pin exactly 22 full release matrix labels.'
    }
    foreach ($releaseMatrixLabel in $releaseMatrixLabels) {
        $label = [string]$releaseMatrixLabel
        if ([string]::IsNullOrWhiteSpace($label) -or $label -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or -not $releaseMatrixLabelSet.Add($label)) {
            Add-Failure -Failures $Failures -Code 'manifest-release-matrix-labels' -Message 'Manifest has an empty, duplicate, or unsafe full release matrix label.'
        }
    }
    foreach ($canonicalReleaseMatrixLabel in $canonicalReleaseMatrixLabels) {
        if (-not $releaseMatrixLabelSet.Contains($canonicalReleaseMatrixLabel)) {
            Add-Failure -Failures $Failures -Code 'manifest-release-matrix-labels' -Message "Manifest is missing canonical full release matrix label '$canonicalReleaseMatrixLabel'."
        }
    }
    $runDirectories = if ($null -eq $Manifest.releaseArtifactLayout) { $null } else { $Manifest.releaseArtifactLayout.fullMatrixRunDirectories }
    $runDirectorySet = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    if ($null -eq $runDirectories -or @($runDirectories.PSObject.Properties).Count -ne $releaseMatrixLabelSet.Count) {
        Add-Failure -Failures $Failures -Code 'manifest-release-run-directories' -Message 'Manifest must pin exactly one run directory for every full release matrix label.'
    }
    else {
        foreach ($releaseMatrixLabel in $releaseMatrixLabelSet) {
            $property = $runDirectories.PSObject.Properties[$releaseMatrixLabel]
            $directory = if ($null -eq $property) { '' } else { [string]$property.Value }
            if ([string]::IsNullOrWhiteSpace($directory) -or $directory -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]*$' -or -not $runDirectorySet.Add($directory)) {
                Add-Failure -Failures $Failures -Code 'manifest-release-run-directories' -Message "Manifest has an empty, duplicate, or unsafe run directory for '$releaseMatrixLabel'."
            }
        }
    }

    $invocations = @($Manifest.nunitInvocations)
    $mappings = @($Manifest.parityMappings)
    if ($invocations.Count -ne 62 -or $mappings.Count -ne 62) { Add-Failure -Failures $Failures -Code 'manifest-row-count' -Message 'Manifest must contain exactly 62 legacy invocations and 62 parity mappings.' }
    $ids = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    $invocationsById = @{}
    foreach ($invocation in $invocations) {
        if ($null -eq $invocation -or [string]::IsNullOrWhiteSpace([string]$invocation.id) -or [string]::IsNullOrWhiteSpace([string]$invocation.nunitFullName) -or $invocation.expectedResult -ne 'Passed' -or -not $ids.Add([string]$invocation.id)) {
            Add-Failure -Failures $Failures -Code 'manifest-legacy-row' -Message 'Manifest has a duplicate, incomplete, or non-passing legacy invocation.'
            continue
        }
        $invocationsById[[string]$invocation.id] = $invocation
    }
    $mappedIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    $requiredArtifacts = @('daily-nunit', 'release-run-summary', 'release-staging-receipt', 'release-immutable-delta', 'release-semantic-transition', 'release-fixed-point', 'release-zip')
    $requiredEvidence = @('Daily.NUnit.xml', 'run-summary.json', 'staging-receipt.json', 'immutable-input-manifest-bootstrap-delta.json', 'semantic-transition-report.json', 'fixed-point-report.json', 'jp.penguin.purebase-0.1.0.zip')
    foreach ($mapping in $mappings) {
        $mappedId = if ($null -eq $mapping) { '' } else { [string]$mapping.legacyId }
        $mappingValid = $null -ne $mapping -and $invocationsById.ContainsKey($mappedId) -and $mappedIds.Add($mappedId) -and
            -not [string]::IsNullOrWhiteSpace([string]$mapping.newOwner) -and -not [string]::IsNullOrWhiteSpace([string]$mapping.newLane) -and
            $mapping.oracle -eq 'nunit-and-release-bootstrap' -and $mapping.status -eq 'required' -and
            (@('covered', 'intentionally-replaced-with-equivalent-oracle') -contains [string]$mapping.classification) -and
            @($mapping.artifacts).Count -ge $requiredArtifacts.Count -and @($mapping.evidence).Count -ge $requiredEvidence.Count
        if ($mappingValid) {
            foreach ($artifact in $requiredArtifacts) { if (-not (@($mapping.artifacts) -contains $artifact)) { $mappingValid = $false } }
            foreach ($evidence in $requiredEvidence) { if (-not (@($mapping.evidence) -contains $evidence)) { $mappingValid = $false } }
        }
        if (-not $mappingValid) { Add-Failure -Failures $Failures -Code 'manifest-mapping' -Message "Parity mapping is missing, duplicate, weak, or unapproved for legacy ID '$mappedId'." }
    }
    if ($mappedIds.Count -ne $ids.Count) { Add-Failure -Failures $Failures -Code 'manifest-unmapped' -Message 'Every legacy invocation must have exactly one parity mapping.' }
    return $invocations
}

function Test-LegacyEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][object[]]$Invocations,
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures
    )

    $aggregate = [ordered]@{ total = 0; passed = 0; failed = 0; skipped = 0; inconclusive = 0 }
    foreach ($invocation in $Invocations) {
        $id = [string]$invocation.id
        $result = Test-NUnitArtifact -Path (Join-Path $Root ($id + '/' + $id + '.NUnit.xml')) -Failures $Failures -Code 'legacy-nunit' -ExpectedFullName ([string]$invocation.nunitFullName) -RequireSingleTest
        if ($null -ne $result) {
            foreach ($name in @('total', 'passed', 'failed', 'skipped', 'inconclusive')) { $aggregate[$name] += $result[$name] }
        }
    }
    $expected = $Manifest.expectedAggregate
    if ($aggregate.total -ne [int]$expected.invocationCount -or $aggregate.passed -ne [int]$expected.passed -or $aggregate.failed -ne [int]$expected.failed -or $aggregate.skipped -ne [int]$expected.skipped -or $aggregate.inconclusive -ne [int]$expected.inconclusive) {
        Add-Failure -Failures $Failures -Code 'legacy-aggregate' -Message 'Legacy NUnit aggregate does not match the pinned 62/62/0/0/0 evidence.'
    }
    $scene = Get-JsonArtifact -Path (Join-Path $Root 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json') -Failures $Failures -Code 'legacy-validation-scene'
    if ($null -ne $scene) {
        if (-not (Test-ValidationSceneEvidence -Scene $scene -Failures $Failures -Code 'legacy-validation-scene')) { $scene = $null }
    }
    if ($null -ne $scene) {
        $fixed = $Manifest.fixedValidationSceneOracle
        foreach ($name in @('staticLightmapCount', 'staticRendererAssignmentCount', 'metaReadbackCount', 'shadowDeltaPixelCount', 'warmedRepresentativeVariantCount')) {
            $actual = Get-ValidationSceneOracleValue -Scene $scene -Name $name -Failures $Failures -Code 'legacy-validation-scene'
            if ($null -ne $actual -and $actual -ne [int]$fixed.$name) { Add-Failure -Failures $Failures -Code 'legacy-fixed-evidence' -Message "Legacy fixed evidence '$name' does not match the pinned value." }
        }
    }
    $feasibilityPath = Join-Path $Root 'validation-scene-birp-feasibility.json'
    $feasibility = Get-JsonArtifact -Path $feasibilityPath -Failures $Failures -Code 'legacy-dynamic-lightmaps'
    if ($null -ne $feasibility) {
        $dynamicValid = $null -ne (Get-StringProperty -Object $feasibility -Name 'check' -Failures $Failures -Code 'legacy-dynamic-lightmaps') -and
            (Get-StringProperty -Object $feasibility -Name 'status' -Failures $Failures -Code 'legacy-dynamic-lightmaps') -eq 'PASSED' -and
            $null -ne (Get-StringProperty -Object $feasibility -Name 'unityVersion' -Failures $Failures -Code 'legacy-dynamic-lightmaps') -and
            $null -ne (Get-StringPropertyAlias -Object $feasibility -Names @('graphicsApi', 'graphicsDevice') -Failures $Failures -Code 'legacy-dynamic-lightmaps') -and
            (Get-StringProperty -Object $feasibility -Name 'testName' -Failures $Failures -Code 'legacy-dynamic-lightmaps') -eq 'PureBase.Integration.Tests.PureBaseValidationSceneTests.FixedValidationSceneBakesAndRequestsRepresentativeBirpVariants' -and
            $null -ne (Get-StringProperty -Object $feasibility -Name 'artifactPath' -Failures $Failures -Code 'legacy-dynamic-lightmaps') -and
            (Get-StringProperty -Object $feasibility -Name 'dynamicLightmapStatus' -Failures $Failures -Code 'legacy-dynamic-lightmaps') -eq 'NOT_DETERMINISTIC_IN_BATCH_EDITMODE'
        if (-not $dynamicValid) { Add-Failure -Failures $Failures -Code 'legacy-dynamic-lightmaps' -Message 'Dynamic-lightmaps evidence does not match the required JSON schema and known limitation.' }
    }
    $probe = Get-JsonArtifact -Path (Join-Path $Root 'birp-probe-feasibility.json') -Failures $Failures -Code 'legacy-birp-probe'
    if ($null -ne $probe) {
        $probeValid = $null -ne (Get-StringProperty -Object $probe -Name 'check' -Failures $Failures -Code 'legacy-birp-probe') -and
            (Get-StringProperty -Object $probe -Name 'status' -Failures $Failures -Code 'legacy-birp-probe') -eq 'PASSED' -and
            $null -ne (Get-StringProperty -Object $probe -Name 'unityVersion' -Failures $Failures -Code 'legacy-birp-probe') -and
            $null -ne (Get-StringPropertyAlias -Object $probe -Names @('graphicsApi', 'graphicsDevice') -Failures $Failures -Code 'legacy-birp-probe') -and
            (Test-StringArrayProperty -Object $probe -Name 'testNames' -Failures $Failures -Code 'legacy-birp-probe' -ExpectedCount 2 -RequiredValues @('PureBase.Integration.Tests.BirpGiProbeReadbackTests.BlackProbePathProducesFiniteHdrReadbackWithMeshCoverage', 'PureBase.Integration.Tests.BirpGiProbeReadbackTests.BoxProjectedReflectionProbePathProducesFiniteHdrReadbackWithMeshCoverage')) -and
            (Test-StringArrayProperty -Object $probe -Name 'artifactPaths' -Failures $Failures -Code 'legacy-birp-probe' -ExpectedCount 2)
        if (-not $probeValid) { Add-Failure -Failures $Failures -Code 'legacy-birp-probe' -Message 'BIRP probe evidence does not match the required JSON schema and passing verdict.' }
    }
    $audit = Get-JsonArtifact -Path (Join-Path $Root 'release-boundary-audit.json') -Failures $Failures -Code 'legacy-release-boundary'
    if ($null -ne $audit) {
        $packageContainsPureBaseTestAssets = Get-BooleanProperty -Object $audit -Name 'packageContainsPureBaseTestAssets' -Failures $Failures -Code 'legacy-release-boundary-test-assets'
        $packageContainsPureBaseTestAssetsValid = $false -eq $packageContainsPureBaseTestAssets
        if (-not $packageContainsPureBaseTestAssetsValid) {
            Add-Failure -Failures $Failures -Code 'legacy-release-boundary-test-assets' -Message "Release-boundary audit property 'packageContainsPureBaseTestAssets' must be Boolean false."
        }
        $auditValid = $packageContainsPureBaseTestAssetsValid -and
            $null -ne (Get-StringProperty -Object $audit -Name 'check' -Failures $Failures -Code 'legacy-release-boundary') -and
            (Get-StringProperty -Object $audit -Name 'status' -Failures $Failures -Code 'legacy-release-boundary') -eq 'PASSED' -and
            $null -ne (Get-StringProperty -Object $audit -Name 'packagePath' -Failures $Failures -Code 'legacy-release-boundary') -and
            (Test-StringArrayProperty -Object $audit -Name 'trackedScmodulePaths' -Failures $Failures -Code 'legacy-release-boundary') -and
            (Test-StringArrayProperty -Object $audit -Name 'approvedTrackedScmodulePaths' -Failures $Failures -Code 'legacy-release-boundary') -and
            (Test-StringArrayProperty -Object $audit -Name 'unapprovedTrackedScmodulePaths' -Failures $Failures -Code 'legacy-release-boundary' -ExpectedCount 0) -and
            (Test-StringArrayProperty -Object $audit -Name 'missingTrackedScmodulePaths' -Failures $Failures -Code 'legacy-release-boundary' -ExpectedCount 0) -and
            $true -eq (Get-BooleanProperty -Object $audit -Name 'trackedScmodulePathsExactlyApproved' -Failures $Failures -Code 'legacy-release-boundary') -and
            $null -ne (Get-StringProperty -Object $audit -Name 'shaderCoreDependency' -Failures $Failures -Code 'legacy-release-boundary') -and
            $false -eq (Get-BooleanProperty -Object $audit -Name 'urpDependencyPresent' -Failures $Failures -Code 'legacy-release-boundary') -and
            $true -eq (Get-BooleanProperty -Object $audit -Name 'pbrHybridPropertiesByteIdentical' -Failures $Failures -Code 'legacy-release-boundary') -and
            $true -eq (Get-BooleanProperty -Object $audit -Name 'roughnessAbi' -Failures $Failures -Code 'legacy-release-boundary')
        foreach ($name in @('requiredProperties', 'forbiddenProperties')) {
            $entries = @(Get-ArrayPropertyItems -Object $audit -Name $name -Failures $Failures -Code 'legacy-release-boundary')
            $expectedPresent = $name -eq 'requiredProperties'
            if ($null -eq $entries -or $entries.Count -eq 0 -or (@($entries | Where-Object { $null -eq $_ -or $_ -isnot [pscustomobject] -or $null -eq (Get-StringProperty -Object $_ -Name 'name' -Failures $Failures -Code 'legacy-release-boundary') -or (Get-BooleanProperty -Object $_ -Name 'present' -Failures $Failures -Code 'legacy-release-boundary') -ne $expectedPresent })).Count -ne 0) { $auditValid = $false }
        }
        if (-not $auditValid) { Add-Failure -Failures $Failures -Code 'legacy-release-boundary' -Message 'Release-boundary audit does not match the required JSON schema and passing verdict.' }
    }
}

function Resolve-ReportPath {
    param(
        [Parameter(Mandatory = $true)][string]$PackageRoot,
        [Parameter()][string]$OutputDirectory,
        [Parameter()][string]$ReportPath
    )

    if (-not [string]::IsNullOrWhiteSpace($OutputDirectory) -and -not [string]::IsNullOrWhiteSpace($ReportPath)) { throw 'Specify either OutputDirectory or ReportPath, not both.' }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory) -and [string]::IsNullOrWhiteSpace($ReportPath)) { throw 'A caller-provided external OutputDirectory or ReportPath is required.' }
    $candidate = if ([string]::IsNullOrWhiteSpace($ReportPath)) { Join-Path $OutputDirectory 'pure-base-parity-report.json' } else { $ReportPath }
    $fullPath = Get-FullPath -Path $candidate
    if (Test-PathEqualOrDescendant -Root $PackageRoot -Candidate $fullPath) { throw 'Parity reports must be written outside the package root.' }
    return $fullPath
}

$scriptRoot = Split-Path -Parent $PSCommandPath
$packageRoot = Get-FullPath -Path (Join-Path $scriptRoot '../..')
$reportPath = Resolve-ReportPath -PackageRoot $packageRoot -OutputDirectory $OutputDirectory -ReportPath $ReportPath
[void](New-Item -ItemType Directory -Path (Split-Path -Parent $reportPath) -Force)
$failures = New-Object 'System.Collections.Generic.List[object]'

try {
    foreach ($root in @($LegacyArtifactRoot, $DailyArtifactRoot, $ReleaseArtifactRoot)) {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { Add-Failure -Failures $failures -Code 'artifact-root' -Message "Artifact root is missing: '$root'." }
    }
    $manifest = Get-JsonArtifact -Path $ManifestPath -Failures $failures -Code 'manifest-json'
    $releaseContentContract = Get-JsonArtifact -Path (Join-Path $scriptRoot '../Release/release-content.json') -Failures $failures -Code 'release-zip-contract'
    if ($null -ne $manifest) {
        $invocations = Test-Manifest -Manifest $manifest -Failures $failures
        if (Test-Path -LiteralPath $LegacyArtifactRoot -PathType Container) { Test-LegacyEvidence -Root $LegacyArtifactRoot -Invocations $invocations -Manifest $manifest -Failures $failures }
        if (Test-Path -LiteralPath $DailyArtifactRoot -PathType Container) {
            $null = Test-NUnitArtifact -Path (Join-Path $DailyArtifactRoot 'Daily.NUnit.xml') -Failures $failures -Code 'daily-nunit'
            foreach ($path in @('Daily.Unity.log', 'Daily.Process.log')) { $null = Get-RequiredFile -Path (Join-Path $DailyArtifactRoot $path) -Failures $failures -Code 'daily-required-artifact' }
        }
        if ((Test-Path -LiteralPath $ReleaseArtifactRoot -PathType Container) -and $null -ne $releaseContentContract) { Test-ReleaseEvidence -Root $ReleaseArtifactRoot -Manifest $manifest -ReleaseContentContract $releaseContentContract -Failures $failures }
    }
}
catch {
    Add-Failure -Failures $failures -Code 'validator-exception' -Message $_.Exception.Message
}

$report = [ordered]@{
    schemaVersion = 1
    validator = 'Validate-PureBaseParity.ps1'
    deletionEligible = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures.ToArray()
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
if ($failures.Count -ne 0) {
    Write-Error "PureBase parity validation failed. Report: '$reportPath'."
    exit 1
}
Write-Output "PureBase parity validation passed. Report: '$reportPath'."