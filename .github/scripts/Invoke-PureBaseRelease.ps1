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
    [Parameter(Mandatory)][string]$PackageRoot,
    [Parameter(Mandatory)][string]$UnityEditorPath,
    [Parameter(Mandatory)][string]$ValidationArtifactDirectory,
    [Parameter(Mandatory)][string]$ReleaseArtifactDirectory,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$Branch,
    [Parameter(Mandatory)][string]$ConfirmedVersion,
    [Parameter(Mandatory)][string]$VpmRepository,
    [Parameter(Mandatory)][string]$AppSlug,
    [switch]$Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$releaseToken = [string]$env:PUREBASE_RELEASE_TOKEN
$dispatchToken = [string]$env:PUREBASE_DISPATCH_TOKEN
$apiRoot = if ($env:GITHUB_API_URL) { $env:GITHUB_API_URL.TrimEnd('/') } else { 'https://api.github.com' }
$apiVersion = '2022-11-28'
$packageName = 'jp.penguin.purebase'

if (-not $releaseToken -or -not $dispatchToken) { throw 'Release and dispatch tokens are required.' }
if ($Repository -notmatch '^[^/]+/[^/]+$' -or $VpmRepository -notmatch '^[^/]+/[^/]+$') {
    throw 'Repository values must use owner/name form.'
}
$PackageRoot = [IO.Path]::GetFullPath($PackageRoot)
$ValidationArtifactDirectory = [IO.Path]::GetFullPath($ValidationArtifactDirectory)
$ReleaseArtifactDirectory = [IO.Path]::GetFullPath($ReleaseArtifactDirectory)
New-Item -ItemType Directory -Path $ValidationArtifactDirectory,$ReleaseArtifactDirectory -Force | Out-Null
$statePath = Join-Path $ReleaseArtifactDirectory 'release-state.json'

function Write-State([string]$Phase, [hashtable]$Data = @{}) {
    $state = [ordered]@{ schemaVersion = 1; phase = $Phase; version = $ConfirmedVersion; timestampUtc = [DateTime]::UtcNow.ToString('o') }
    foreach ($entry in $Data.GetEnumerator()) { $state[$entry.Key] = $entry.Value }
    [IO.File]::WriteAllText($statePath, ($state | ConvertTo-Json -Depth 8) + "`n", [Text.UTF8Encoding]::new($false))
}

function Invoke-Git([string[]]$Arguments, [switch]$AllowFailure) {
    $output = @(& git -C $PackageRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join "`n")"
    }
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output -join "`n").Trim() }
}

function Invoke-Api([string]$Method, [string]$Uri, [string]$Token, $Body = $null, [string]$File = '') {
    $headers = @{ Accept = 'application/vnd.github+json'; Authorization = "Bearer $Token"; 'X-GitHub-Api-Version' = $apiVersion; 'User-Agent' = 'Pure-Base-Actions' }
    $parameters = @{ Method = $Method; Uri = $Uri; Headers = $headers }
    if ($File) { $parameters.InFile = $File; $parameters.ContentType = 'application/zip' }
    elseif ($null -ne $Body) { $parameters.Body = $Body | ConvertTo-Json -Depth 12 -Compress; $parameters.ContentType = 'application/json' }
    try { Invoke-RestMethod @parameters }
    catch {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        $wrapped = [InvalidOperationException]::new("GitHub API $Method $Uri failed (HTTP $code): $($_.Exception.Message)", $_.Exception)
        $wrapped.Data['StatusCode'] = $code
        throw $wrapped
    }
}

function Test-Version([string]$Value) {
    if ($Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw "Only stable unprefixed semantic versions are supported: '$Value'." }
    [version]$Value
}

function Get-Release([string]$Version) {
    try { return Invoke-Api GET "$apiRoot/repos/$Repository/releases/tags/$([Uri]::EscapeDataString($Version))" $releaseToken }
    catch { if ($_.Exception.Data['StatusCode'] -ne 404) { throw } }
    $all = @(Invoke-Api GET "$apiRoot/repos/$Repository/releases?per_page=100" $releaseToken)
    @($all | Where-Object tag_name -eq $Version | Select-Object -First 1)[0]
}

function Get-TagSha([string]$Version) {
    $result = Invoke-Git @('rev-parse', "refs/tags/$Version^{commit}") -AllowFailure
    if ($result.ExitCode -eq 0) { return $result.Output }
    return ''
}

function Invoke-Validation {
    Write-State 'validation'
    & (Join-Path $PackageRoot 'Tests/Release/Run-PureBaseReleaseValidation.ps1') -UnityEditorPath $UnityEditorPath -ArtifactDirectory $ValidationArtifactDirectory
    if ($LASTEXITCODE -ne 0) { throw "Release validation failed with exit code $LASTEXITCODE." }
}

function Set-PackageVersion([string]$Version) {
    $path = Join-Path $PackageRoot 'package.json'
    $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    $json.version = $Version
    [IO.File]::WriteAllText($path, ($json | ConvertTo-Json -Depth 20) + "`n", [Text.UTF8Encoding]::new($false))
}

function Commit-And-Tag([string]$Version) {
    Set-PackageVersion $Version
    Invoke-Git @('config','user.name',"$AppSlug[bot]") | Out-Null
    Invoke-Git @('config','user.email',"$AppSlug[bot]@users.noreply.github.com") | Out-Null
    Invoke-Git @('add','--','package.json') | Out-Null
    Invoke-Git @('commit','-m',"Release $Version",'-m','Automatically updated by GitHub Actions after release validation.') | Out-Null
    $sha = (Invoke-Git @('rev-parse','HEAD')).Output
    Invoke-Git @('push','origin',"HEAD:$Branch") | Out-Null
    Invoke-Git @('tag','--annotate',$Version,'--message',"Release $Version",$sha) | Out-Null
    Invoke-Git @('push','origin',"refs/tags/$Version") | Out-Null
    $sha
}

function Ensure-ResumeTag([string]$Version) {
    $sha = (Invoke-Git @('rev-parse','HEAD')).Output
    $headPackage = Get-Content -LiteralPath (Join-Path $PackageRoot 'package.json') -Raw | ConvertFrom-Json
    if ([string]$headPackage.version -ne $Version) { throw 'Resume requires HEAD package.json to contain the selected version.' }
    $tagSha = Get-TagSha $Version
    if ($tagSha -and $tagSha -ne $sha) { throw "Tag '$Version' points to a different commit." }
    if (-not $tagSha) {
        Invoke-Git @('tag','--annotate',$Version,'--message',"Release $Version",$sha) | Out-Null
        Invoke-Git @('push','origin',"refs/tags/$Version") | Out-Null
    }
    $sha
}

function Build-Zip([string]$Version) {
    $builder = Join-Path $ReleaseArtifactDirectory 'builder-output'
    if (Test-Path $builder) { Remove-Item $builder -Recurse -Force }
    New-Item -ItemType Directory -Path $builder -Force | Out-Null
    & (Join-Path $PackageRoot 'Tests/Release/Build-PureBaseRelease.ps1') -OutputDirectory $builder
    if ($LASTEXITCODE -ne 0) { throw "Release ZIP builder failed with exit code $LASTEXITCODE." }
    $zips = @(Get-ChildItem $builder -Filter "$packageName-*.zip" -File)
    if ($zips.Count -ne 1) { throw "Expected exactly one audited ZIP, found $($zips.Count)." }
    $name = "$packageName-$Version.zip"
    $path = Join-Path $ReleaseArtifactDirectory $name
    Copy-Item $zips[0].FullName $path -Force
    $sha = (Get-FileHash $path -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($path + '.sha256', $sha + "`n", [Text.ASCIIEncoding]::new())
    [pscustomobject]@{ Name = $name; Path = $path; Sha256 = $sha }
}

function Assert-Asset($Release, $Artifact) {
    $asset = @($Release.assets | Where-Object name -eq $Artifact.Name | Select-Object -First 1)
    if ($asset.Count -ne 1) { throw "Release asset '$($Artifact.Name)' is missing." }
    if ($asset[0].digest -and $asset[0].digest -ne "sha256:$($Artifact.Sha256)") { throw 'Existing release asset digest does not match the rebuilt ZIP.' }
}

function Publish-Release([string]$Version, [string]$CommitSha, $Artifact, [bool]$IsResume) {
    $release = Get-Release $Version
    if ($release -and -not $release.draft) {
        if (-not $IsResume) { throw "Release '$Version' already exists." }
        Assert-Asset $release $Artifact
        return $release
    }
    if (-not $release) {
        $release = Invoke-Api POST "$apiRoot/repos/$Repository/releases" $releaseToken ([ordered]@{ tag_name=$Version; target_commitish=$CommitSha; name=$Version; draft=$true; prerelease=$false; generate_release_notes=$true })
    }
    foreach ($asset in @($release.assets | Where-Object name -eq $Artifact.Name)) {
        Invoke-Api DELETE "$apiRoot/repos/$Repository/releases/assets/$($asset.id)" $releaseToken | Out-Null
    }
    $upload = ([string]$release.upload_url) -replace '\{\?name,label\}$',''
    Invoke-Api POST "$upload?name=$([Uri]::EscapeDataString($Artifact.Name))" $releaseToken $null $Artifact.Path | Out-Null
    Invoke-Api PATCH "$apiRoot/repos/$Repository/releases/$($release.id)" $releaseToken @{ draft=$false } | Out-Null
    $published = Get-Release $Version
    if (-not $published -or $published.draft) { throw "Release '$Version' was not published." }
    if ($null -ne $published.PSObject.Properties['immutable'] -and -not [bool]$published.immutable) { throw "Release '$Version' was published but is not immutable." }
    Assert-Asset $published $Artifact
    $published
}

Write-State 'preflight'
if (-not (Test-Path $UnityEditorPath -PathType Leaf)) { throw "Unity is missing at '$UnityEditorPath'." }
if ((Invoke-Git @('branch','--show-current')).Output -ne $Branch) { throw "The checked-out branch is not '$Branch'." }
if ((Invoke-Git @('status','--porcelain=v1')).Output) { throw 'Release checkout must be clean.' }
Invoke-Api GET "$apiRoot/repos/$VpmRepository" $dispatchToken | Out-Null

$trigger = Get-Content -LiteralPath (Join-Path $PackageRoot 'update_trigger.json') -Raw | ConvertFrom-Json
$package = Get-Content -LiteralPath (Join-Path $PackageRoot 'package.json') -Raw | ConvertFrom-Json
$targetText = [string]$trigger.version
$currentText = [string]$package.version
$target = Test-Version $targetText
$current = Test-Version $currentText
if ($ConfirmedVersion -ne $targetText) { throw "Confirmation '$ConfirmedVersion' does not match update_trigger.json '$targetText'." }
$isResume = $Resume -and $target -eq $current
if (-not $isResume -and $target -le $current) { throw "update_trigger.json '$targetText' must be newer than package.json '$currentText'." }
if ($Resume -and -not $isResume) { throw 'Resume is valid only when update_trigger.json and package.json versions are equal.' }
if (-not $isResume -and ((Get-TagSha $targetText) -or (Get-Release $targetText))) { throw "Tag or release '$targetText' already exists." }

Invoke-Validation
$commitSha = if ($isResume) { Ensure-ResumeTag $targetText } else { Commit-And-Tag $targetText }
Write-State 'version-committed-and-tagged' @{ commitSha=$commitSha; resume=$isResume }
$artifact = Build-Zip $targetText
Write-State 'final-archive-built' @{ commitSha=$commitSha; assetName=$artifact.Name; sha256=$artifact.Sha256 }
$release = Publish-Release $targetText $commitSha $artifact $isResume
Write-State 'immutable-release-published' @{ commitSha=$commitSha; releaseUrl=[string]$release.html_url; sha256=$artifact.Sha256 }

$packageUrl = "https://github.com/$Repository/releases/download/$targetText/$($artifact.Name)?"
Invoke-Api POST "$apiRoot/repos/$VpmRepository/dispatches" $dispatchToken ([ordered]@{
    event_type = 'update-vpm'
    client_payload = [ordered]@{ packageName=$packageName; version=$targetText; tag=$targetText; commitSha=$commitSha; packageurl=$packageUrl; sha256=$artifact.Sha256; releaseUrl=[string]$release.html_url; sourceRepository=$Repository }
}) | Out-Null
Write-State 'completed' @{ commitSha=$commitSha; releaseUrl=[string]$release.html_url; vpmRepository=$VpmRepository; sha256=$artifact.Sha256 }
Write-Output "Release completed: $($release.html_url)"
