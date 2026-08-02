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



# Promotes only the archive produced by a successful exact-SHA validation run.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$ValidatedEventSha,
    [Parameter(Mandatory)][string]$ReleaseArtifactDirectory,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$Branch,
    [Parameter(Mandatory)][string]$ConfirmedVersion,
    [Parameter(Mandatory)][string]$VpmRepository,
    [Parameter(Mandatory)][string]$AppSlug,
    [Parameter()][string]$ValidatedPackageDirectory = '',
    [Parameter()][scriptblock]$BeforeMutation,
    [Parameter()][switch]$Resume,
    [Parameter()][switch]$PreflightOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PureBase.Automation.psm1') -Force

$releaseToken = [string]$env:PUREBASE_RELEASE_TOKEN
$dispatchToken = [string]$env:PUREBASE_DISPATCH_TOKEN
$apiRoot = if ($env:GITHUB_API_URL) { $env:GITHUB_API_URL.TrimEnd('/') } else { 'https://api.github.com' }
$gitServerUrl = if ($env:GITHUB_SERVER_URL) { [string]$env:GITHUB_SERVER_URL } else { 'https://github.com' }
$apiVersion = '2026-03-10'
$packageName = 'jp.penguin.purebase'
$PackageRoot = [IO.Path]::GetFullPath($PackageRoot)
$ReleaseArtifactDirectory = [IO.Path]::GetFullPath($ReleaseArtifactDirectory)
New-Item -ItemType Directory -Path $ReleaseArtifactDirectory -Force | Out-Null
$statePath = Join-Path $ReleaseArtifactDirectory 'release-state.json'

if (-not $releaseToken -or -not $dispatchToken) { throw 'Release and dispatch tokens are required.' }
if ($Repository -notmatch '^[^/]+/[^/]+$' -or $VpmRepository -notmatch '^[^/]+/[^/]+$') { throw 'Repository values must use owner/name form.' }
if ($ValidatedEventSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'ValidatedEventSha must be a full Git SHA.' }
$controllerSha = $ValidatedEventSha

function Write-State([string]$Phase, [hashtable]$Data = @{}) {
    $state = [ordered]@{ schemaVersion = 1; phase = $Phase; version = $ConfirmedVersion; timestampUtc = [DateTime]::UtcNow.ToString('o') }
    foreach ($entry in $Data.GetEnumerator()) { $state[$entry.Key] = $entry.Value }
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
}

function Invoke-Git([string[]]$Arguments, [switch]$AllowFailure, [switch]$Authenticate) {
    $authenticationToken = if ($Authenticate) { $releaseToken } else { '' }
    Invoke-PureBaseGit -PackageRoot $PackageRoot -Arguments $Arguments -AuthenticationToken $authenticationToken -GitServerUrl $gitServerUrl -AllowFailure:$AllowFailure
}

function Invoke-Api([string]$Method, [string]$Uri, [string]$Token, $Body = $null, [string]$File = '') {
    $headers = @{ Accept = 'application/vnd.github+json'; Authorization = "Bearer $Token"; 'X-GitHub-Api-Version' = $apiVersion; 'User-Agent' = 'Pure-Base-Actions' }
    $parameters = @{ Method = $Method; Uri = $Uri; Headers = $headers }
    if ($File) { $parameters.InFile = $File; $parameters.ContentType = 'application/zip' }
    elseif ($null -ne $Body) { $parameters.Body = $Body | ConvertTo-Json -Depth 12 -Compress; $parameters.ContentType = 'application/json' }
    try { Invoke-RestMethod @parameters }
    catch {
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        $code = if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) { [int]$responseProperty.Value.StatusCode } else { 0 }
        $wrapped = [InvalidOperationException]::new("GitHub API $Method $Uri failed (HTTP $code): $($_.Exception.Message)", $_.Exception)
        $wrapped.Data['StatusCode'] = $code
        throw $wrapped
    }
}

function Get-Release([string]$Version) {
    try { return Invoke-Api GET "$apiRoot/repos/$Repository/releases/tags/$([Uri]::EscapeDataString($Version))" $releaseToken }
    catch { if ($_.Exception.Data['StatusCode'] -ne 404) { throw } }
    $releases = @(Invoke-Api GET "$apiRoot/repos/$Repository/releases?per_page=100" $releaseToken)
    if ($releases.Count -eq 1 -and $releases[0] -is [object[]]) { $releases = $releases[0] }
    $releaseCandidates = @($releases | Where-Object { $null -ne $_ -and [string]$_.tag_name -ceq $Version } | Select-Object -First 1)
    if ($releaseCandidates.Count -eq 0) { return $null }
    return $releaseCandidates[0]
}

function Get-ReleaseById([long]$ReleaseId, [int]$MaximumAttempts = 4) {
    if ($ReleaseId -le 0) { throw 'Release ID must be positive.' }
    if ($MaximumAttempts -le 0) { throw 'MaximumAttempts must be positive.' }
    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try { return Invoke-Api -Method GET -Uri "$apiRoot/repos/$Repository/releases/$ReleaseId" -Token $releaseToken }
        catch {
            if ($_.Exception.Data['StatusCode'] -ne 404) { throw }
            if ($attempt -eq $MaximumAttempts) { return $null }
            Start-Sleep -Milliseconds (100 * $attempt)
        }
    }
    return $null
}

function Get-ReleaseTag([string]$Version) {
    $tagType = Invoke-Git @('cat-file', '-t', "refs/tags/$Version") -AllowFailure
    if ($tagType.ExitCode -ne 0) { return $null }
    if ($tagType.Output -cne 'tag') { return [pscustomobject]@{ Name = $Version; Annotated = $false; PeeledCommitSha = '' } }
    $peeled = Invoke-Git @('rev-parse', "refs/tags/$Version^{commit}") -AllowFailure
    if ($peeled.ExitCode -ne 0) { throw "Annotated tag '$Version' cannot be peeled to a commit." }
    return [pscustomobject]@{ Name = $Version; Annotated = $true; PeeledCommitSha = $peeled.Output }
}

function Get-WorkflowRuns([Parameter(Mandatory)][string]$HeadSha) {
    $allRuns = [Collections.Generic.List[object]]::new()
    for ($page = 1; ; $page++) {
        $query = "head_sha=$([Uri]::EscapeDataString($HeadSha))&branch=$([Uri]::EscapeDataString($Branch))&event=workflow_dispatch&per_page=100&page=$page"
        $response = Invoke-Api GET "$apiRoot/repos/$Repository/actions/workflows/release-validation.yml/runs?$query" $releaseToken
        $pageRuns = @($response.workflow_runs)
        foreach ($run in $pageRuns) { $allRuns.Add($run) }
        if ($pageRuns.Count -lt 100) { break }
    }
    return $allRuns.ToArray()
}

function Get-ValidationArtifacts([long]$RunId) {
    $allArtifacts = [Collections.Generic.List[object]]::new()
    for ($page = 1; ; $page++) {
        $response = Invoke-Api GET "$apiRoot/repos/$Repository/actions/runs/$RunId/artifacts?per_page=100&page=$page" $releaseToken
        $pageArtifacts = @($response.artifacts)
        foreach ($artifact in $pageArtifacts) { $allArtifacts.Add($artifact) }
        if ($pageArtifacts.Count -lt 100) { break }
    }
    return $allArtifacts.ToArray()
}

function Assert-RemoteReleaseBranchHead {
    Invoke-Git @('fetch', '--no-tags', 'origin', "refs/heads/${Branch}:refs/remotes/origin/$Branch") -Authenticate | Out-Null
    $remoteSha = (Invoke-Git @('rev-parse', "refs/remotes/origin/$Branch")).Output
    if (-not [string]::Equals($remoteSha, $controllerSha, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The remote release branch '$Branch' advanced from the controller SHA."
    }
}

function Invoke-MutationGate([string]$Boundary) {
    if ($null -ne $BeforeMutation) { & $BeforeMutation $Boundary }
    Assert-RemoteReleaseBranchHead
}

function Expand-ValidatedArtifact([string]$ArchivePath) {
    $destination = Join-Path $ReleaseArtifactDirectory 'validation-artifact'
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $root = [IO.Path]::GetFullPath($destination)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $entryName = [string]$entry.FullName
            if ([string]::IsNullOrWhiteSpace($entryName) -or $entryName.StartsWith('/') -or $entryName.StartsWith('\\') -or $entryName -match '(^|[\\/])\.\.([\\/]|$)') { throw 'Validation artifact archive contains an unsafe entry path.' }
            $relative = $entryName -replace '/', [IO.Path]::DirectorySeparatorChar
            $target = [IO.Path]::GetFullPath((Join-Path $root $relative))
            if (-not $target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Validation artifact archive entry escapes the extraction root.' }
            if (-not $seen.Add($target)) { throw 'Validation artifact archive contains a duplicate normalized path.' }
            if ($entryName.EndsWith('/')) { New-Item -ItemType Directory -Path $target -Force | Out-Null; continue }
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            $archiveEntryStream = $entry.Open()
            $output = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
            try { $archiveEntryStream.CopyTo($output) }
            finally { $output.Dispose(); $archiveEntryStream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    $payload = Join-Path $destination 'validated-package'
    if (-not (Test-Path -LiteralPath $payload -PathType Container)) { throw 'Validation artifact archive does not contain validated-package.' }
    return $payload
}

function Get-ValidatedArtifact([long]$RunId, [int]$RunAttempt, [string]$AssetName, [Parameter(Mandatory)][string]$HeadSha) {
    $artifactName = "pure-base-release-validation-$RunId-$RunAttempt"
    $validationArtifact = Resolve-PureBaseValidationArtifact -Artifacts (Get-ValidationArtifacts -RunId $RunId) -ExpectedName $artifactName -WorkflowRunId $RunId -WorkflowRunAttempt $RunAttempt
    if ($ValidatedPackageDirectory) {
        $payloadDirectory = [IO.Path]::GetFullPath($ValidatedPackageDirectory)
    }
    else {
        $archivePath = Join-Path $ReleaseArtifactDirectory 'validation-artifact-download.zip'
        $requestInvoker = {
            param($Method, $Uri, $Headers, $OutFile, $MaximumRedirection)
            if ($OutFile) { Invoke-WebRequest -Method $Method -Uri $Uri -Headers $Headers -OutFile $OutFile -PassThru -MaximumRedirection $MaximumRedirection -ErrorAction Stop }
            else { Invoke-WebRequest -Method $Method -Uri $Uri -Headers $Headers -MaximumRedirection $MaximumRedirection -ErrorAction Stop }
        }
        Invoke-PureBaseArtifactArchiveDownload -ArchiveUri "$apiRoot/repos/$Repository/actions/artifacts/$($validationArtifact.id)/zip" -Token $releaseToken -DestinationPath $archivePath -RequestInvoker $requestInvoker | Out-Null
        $payloadDirectory = Expand-ValidatedArtifact -ArchivePath $archivePath
    }
    Assert-PureBaseValidationPayloadLayout -ValidatedPackageDirectory $payloadDirectory -AssetName $AssetName
    $manifestPath = Join-Path $payloadDirectory 'release-validation.json'
    try { $manifest = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "Validation artifact manifest is invalid: $($_.Exception.Message)" }
    Assert-PureBaseValidationManifest -Manifest $manifest -Repository $Repository -HeadSha $HeadSha -HeadBranch $Branch -WorkflowRunId $RunId -WorkflowRunAttempt $RunAttempt -Version $ConfirmedVersion -AssetName $AssetName -Sha256 ([string]$manifest.sha256) | Out-Null
    return Assert-PureBaseValidatedArchive -ValidatedPackageDirectory $payloadDirectory -AssetName $AssetName -ExpectedSha256 ([string]$manifest.sha256) -Version $ConfirmedVersion -PackageName $packageName
}

function Get-CanonicalDraftBody([string]$Body, [string]$Badge) {
    $badgePattern = '(?m)^\[!\[Downloads\]\(https://img\.shields\.io/github/downloads/[^\r\n]+\?label=downloads\)\]\r?\n?'
    $withoutBadges = [regex]::Replace($Body, $badgePattern, '').TrimStart("`r", "`n")
    if ([string]::IsNullOrEmpty($withoutBadges)) { return $Badge }
    return "$Badge`n$withoutBadges"
}

Write-State 'preflight'
if ((Invoke-Git @('branch', '--show-current')).Output) { throw 'The release checkout must be detached at ValidatedEventSha.' }
if ((Invoke-Git @('rev-parse', 'HEAD')).Output -cne $controllerSha) { throw "The checked-out HEAD does not equal controller SHA '$controllerSha' (the original ValidatedEventSha input)." }
if ((Invoke-Git @('status', '--porcelain=v1')).Output) { throw 'Release checkout must be clean.' }

$existingTag = Get-ReleaseTag $ConfirmedVersion
$releaseTargetSha = $controllerSha
if ($Resume) {
    if ($null -eq $existingTag -or -not [bool]$existingTag.Annotated -or [string]$existingTag.PeeledCommitSha -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Resume requires existing annotated tag '$ConfirmedVersion' to resolve to a commit."
    }
    $releaseTargetSha = [string]$existingTag.PeeledCommitSha
}

$packageJson = if ([string]::Equals($releaseTargetSha, $controllerSha, [StringComparison]::OrdinalIgnoreCase)) {
    Get-Content -LiteralPath (Join-Path $PackageRoot 'package.json') -Raw
}
else {
    (Invoke-Git @('show', "${releaseTargetSha}:package.json")).Output
}
$package = $packageJson | ConvertFrom-Json
if ([string]$package.name -cne $packageName -or [string]$package.version -cne $ConfirmedVersion) { throw 'package.json does not match the confirmed release identity.' }
Assert-PureBaseImmutableReleasesEnabled -ApiRoot $apiRoot -Repository $Repository -Token $releaseToken -ApiInvoker { param($Method, $Uri, $Token) Invoke-Api $Method $Uri $Token } | Out-Null
Invoke-Api GET "$apiRoot/repos/$VpmRepository" $dispatchToken | Out-Null

$assetName = "$packageName-$ConfirmedVersion.zip"
$validationRuns = @(Get-WorkflowRuns -HeadSha $releaseTargetSha)
if ($validationRuns.Count -eq 0) {
    throw "No release validation workflow runs were found for release target SHA '$releaseTargetSha' on branch '$Branch'."
}
$run = Select-PureBaseReleaseValidationRun -Runs $validationRuns -HeadSha $releaseTargetSha -Branch $Branch -WorkflowPath '.github/workflows/release-validation.yml'
if ($null -eq $run.PSObject.Properties['id'] -or [long]$run.id -le 0) { throw 'Selected validation run has no valid ID.' }
$artifact = Get-ValidatedArtifact -RunId ([long]$run.id) -RunAttempt ([int]$run.run_attempt) -AssetName $assetName -HeadSha $releaseTargetSha
$existingRelease = Get-Release $ConfirmedVersion
$releaseMode = Resolve-PureBaseReleaseMode -PackageVersion ([string]$package.version) -ConfirmedVersion $ConfirmedVersion -HeadSha $releaseTargetSha -Resume:$Resume -ExistingTag $existingTag -ExistingRelease $existingRelease
if ($Resume) {
    [void](Resolve-PureBaseResumeTagAction -HeadSha $releaseTargetSha -ExistingTagSha ([string]$existingTag.PeeledCommitSha))
}
Write-State 'release-mode-resolved' @{ mode = $releaseMode.Mode; tagState = $releaseMode.TagState; releaseState = $releaseMode.ReleaseState; commitSha = $releaseTargetSha; validationRunId = [long]$run.id; validationRunAttempt = [int]$run.run_attempt; assetName = $artifact.Name; sha256 = $artifact.Sha256 }
Assert-RemoteReleaseBranchHead
if ($PreflightOnly) { Write-State 'preflight-completed' @{ commitSha = $releaseTargetSha; validationRunId = [long]$run.id; validationRunAttempt = [int]$run.run_attempt; assetName = $artifact.Name; sha256 = $artifact.Sha256; mode = $releaseMode.Mode }; Write-Output 'Release preflight completed.'; return }

if ($releaseMode.Mode -eq 'fresh') {
    Invoke-Git @('config', 'user.name', "$AppSlug[bot]") | Out-Null
    Invoke-Git @('config', 'user.email', "$AppSlug[bot]@users.noreply.github.com") | Out-Null
    Invoke-MutationGate 'tag-push'
    Invoke-Git @('tag', '--annotate', $ConfirmedVersion, '--message', "Release $ConfirmedVersion", $releaseTargetSha) | Out-Null
    Invoke-Git @('push', 'origin', "refs/tags/$ConfirmedVersion") -Authenticate | Out-Null
}

$badge = New-PureBaseReleaseBody -Repository $Repository -Version $ConfirmedVersion -AssetName $assetName
$release = $existingRelease
if ($null -eq $release) {
    Invoke-MutationGate 'draft-create'
    $createdRelease = Invoke-Api -Method POST -Uri "$apiRoot/repos/$Repository/releases" -Token $releaseToken -Body ([ordered]@{ tag_name = $ConfirmedVersion; target_commitish = $releaseTargetSha; name = $ConfirmedVersion; body = $badge; draft = $true; prerelease = [bool]$releaseMode.PrereleaseKind; generate_release_notes = $true })
    if ($null -eq $createdRelease -or $null -eq $createdRelease.PSObject.Properties['id'] -or [long]$createdRelease.id -le 0) {
        throw "Created draft release '$ConfirmedVersion' returned no valid release ID."
    }
    $release = Get-ReleaseById -ReleaseId ([long]$createdRelease.id)
    if ($null -eq $release) { throw "Created draft release '$ConfirmedVersion' could not be re-read by ID before asset processing." }
}
elseif ($releaseMode.ReleaseState -eq 'published') {
    Write-State 'published-release-resume' @{ commitSha = $releaseTargetSha; assetName = $artifact.Name; sha256 = $artifact.Sha256 }
}
elseif ([bool]$release.draft) {
    $body = Get-CanonicalDraftBody -Body ([string]$release.body) -Badge $badge
    if ($body -cne [string]$release.body) {
        Invoke-MutationGate 'draft-body-repair'
        $release = Invoke-Api PATCH "$apiRoot/repos/$Repository/releases/$($release.id)" $releaseToken ([ordered]@{ body = $body })
    }
}

if ([bool]$release.draft) {
    $releaseId = [long]$release.id
    $assetAction = Resolve-PureBaseDraftAssetAction -Assets @($release.assets) -AssetName $artifact.Name -Sha256 $artifact.Sha256
    if ($assetAction -eq 'upload') {
        Invoke-MutationGate 'asset-upload'
        $uploadUri = (([string]$release.upload_url) -replace '\{\?name,label\}$', '') + '?name=' + [Uri]::EscapeDataString($artifact.Name)
        Invoke-Api POST $uploadUri $releaseToken $null $artifact.Path | Out-Null
    }
    $release = Get-ReleaseById -ReleaseId $releaseId
    if ($null -eq $release) { throw "Draft release '$ConfirmedVersion' could not be re-read by ID after asset processing." }
    [void](Resolve-PureBaseDraftAssetAction -Assets @($release.assets) -AssetName $artifact.Name -Sha256 $artifact.Sha256)
    Invoke-MutationGate 'publish'
    Invoke-Api PATCH "$apiRoot/repos/$Repository/releases/$($release.id)" $releaseToken ([ordered]@{ draft = $false; prerelease = [bool]$releaseMode.PrereleaseKind }) | Out-Null
}

$release = Get-Release $ConfirmedVersion
[void](Resolve-PureBaseReleaseMode -PackageVersion ([string]$package.version) -ConfirmedVersion $ConfirmedVersion -HeadSha $releaseTargetSha -Resume -ExistingTag (Get-ReleaseTag $ConfirmedVersion) -ExistingRelease $release)
Assert-PureBasePublishedResumeArtifact -Release $release -AssetName $artifact.Name -ValidationArtifactSha256 $artifact.Sha256 | Out-Null
Invoke-MutationGate 'vpm-dispatch'
$dispatchPayload = New-PureBaseDispatchPayload -PackageName $packageName -Repository $Repository -Version $ConfirmedVersion -CommitSha $releaseTargetSha -PolicyCommitSha $controllerSha -AssetName $artifact.Name -Sha256 $artifact.Sha256 -ReleaseUrl ([string]$release.html_url)
Invoke-Api POST "$apiRoot/repos/$VpmRepository/dispatches" $dispatchToken $dispatchPayload | Out-Null
Write-State 'completed' @{ commitSha = $releaseTargetSha; validationRunId = [long]$run.id; validationRunAttempt = [int]$run.run_attempt; releaseUrl = [string]$release.html_url; vpmRepository = $VpmRepository; sha256 = $artifact.Sha256; mode = $releaseMode.Mode }
Write-Output "Release completed: $($release.html_url)"