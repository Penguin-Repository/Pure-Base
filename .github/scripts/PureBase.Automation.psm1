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

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-PureBaseStableVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value)

    if ($Value -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
        throw "Only stable unprefixed semantic versions are supported: '$Value'."
    }
    return [version]$Value
}

function Resolve-PureBaseReleaseMode {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$CurrentVersion,
        [Parameter(Mandatory)][string]$TargetVersion,
        [Parameter()][switch]$Resume,
        [Parameter()][AllowEmptyString()][string]$ExistingTagSha = '',
        [Parameter()][AllowNull()]$ExistingRelease = $null
    )

    $current = ConvertTo-PureBaseStableVersion -Value $CurrentVersion
    $target = ConvertTo-PureBaseStableVersion -Value $TargetVersion
    $releaseState = if ($null -eq $ExistingRelease) {
        'none'
    }
    elseif ([bool]$ExistingRelease.draft) {
        'draft'
    }
    else {
        'published'
    }

    if ($Resume) {
        if ($target -ne $current) {
            throw 'Resume is valid only when update_trigger.json and package.json versions are equal.'
        }
        return [pscustomobject][ordered]@{
            Mode = 'resume'
            CurrentVersion = $CurrentVersion
            TargetVersion = $TargetVersion
            TagState = if ([string]::IsNullOrEmpty($ExistingTagSha)) { 'missing' } else { 'present' }
            ReleaseState = $releaseState
        }
    }

    if ($target -le $current) {
        throw "update_trigger.json '$TargetVersion' must be newer than package.json '$CurrentVersion'."
    }
    if (-not [string]::IsNullOrEmpty($ExistingTagSha) -or $null -ne $ExistingRelease) {
        throw "Tag or release '$TargetVersion' already exists."
    }

    return [pscustomobject][ordered]@{
        Mode = 'fresh'
        CurrentVersion = $CurrentVersion
        TargetVersion = $TargetVersion
        TagState = 'missing'
        ReleaseState = 'none'
    }
}

function Resolve-PureBaseResumeTagAction {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$HeadSha,
        [Parameter()][AllowEmptyString()][string]$ExistingTagSha = ''
    )

    if ([string]::IsNullOrWhiteSpace($HeadSha)) {
        throw 'Resume requires a non-empty HEAD commit SHA.'
    }
    if ([string]::IsNullOrEmpty($ExistingTagSha)) {
        return 'create'
    }
    if (-not [string]::Equals($ExistingTagSha, $HeadSha, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The existing release tag points to a different commit.'
    }
    return 'reuse'
}

function Invoke-PureBaseGit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter()][switch]$AllowFailure
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.WorkingDirectory = [IO.Path]::GetFullPath($PackageRoot)
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw 'Failed to start Git.'
        }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
        $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        $details = @($stderr, $stdout) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        throw "git $($Arguments -join ' ') failed with exit code ${exitCode}:`n$($details -join "`n")"
    }

    return [pscustomobject][ordered]@{
        ExitCode = $exitCode
        Output = $stdout
        Error = $stderr
    }
}

function New-PureBasePackageUrl {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$AssetName
    )

    if ($Repository -notmatch '^[^/]+/[^/]+$') {
        throw 'Repository must use owner/name form.'
    }
    [void](ConvertTo-PureBaseStableVersion -Value $Version)
    if ([string]::IsNullOrWhiteSpace($AssetName) -or $AssetName -match '[/\\]') {
        throw 'AssetName must be one file name without path separators.'
    }

    $encodedAssetName = [Uri]::EscapeDataString($AssetName)
    return "https://github.com/$Repository/releases/download/$Version/$encodedAssetName"
}

function New-PureBaseDispatchPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PackageName,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$CommitSha,
        [Parameter(Mandatory)][string]$AssetName,
        [Parameter(Mandatory)][string]$Sha256,
        [Parameter(Mandatory)][string]$ReleaseUrl
    )

    return [ordered]@{
        event_type = 'update-vpm'
        client_payload = [ordered]@{
            packageName = $PackageName
            version = $Version
            tag = $Version
            commitSha = $CommitSha
            packageurl = New-PureBasePackageUrl -Repository $Repository -Version $Version -AssetName $AssetName
            sha256 = $Sha256
            releaseUrl = $ReleaseUrl
            sourceRepository = $Repository
        }
    }
}

function Resolve-PureBaseDailySource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$EventName,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter()][AllowEmptyString()][string]$PushSha = '',
        [Parameter()][AllowEmptyString()][string]$PullRequestHeadRepository = '',
        [Parameter()][AllowEmptyString()][string]$PullRequestHeadSha = '',
        [Parameter()][AllowEmptyString()][string]$PullRequestAuthor = '',
        [Parameter()][bool]$PullRequestDraft = $false
    )

    if ($EventName -eq 'push') {
        if ([string]::IsNullOrWhiteSpace($PushSha)) {
            throw 'Push events require a commit SHA.'
        }
        return [pscustomobject][ordered]@{ Allowed = $true; CheckoutRef = $PushSha; Reason = 'push' }
    }
    if ($EventName -ne 'pull_request') {
        throw "Unsupported Daily event '$EventName'."
    }
    if (-not [string]::Equals($PullRequestHeadRepository, $Repository, [StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject][ordered]@{ Allowed = $false; CheckoutRef = ''; Reason = 'external pull request' }
    }
    if ($PullRequestDraft) {
        return [pscustomobject][ordered]@{ Allowed = $false; CheckoutRef = ''; Reason = 'draft pull request' }
    }
    if ([string]::Equals($PullRequestAuthor, 'dependabot[bot]', [StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject][ordered]@{ Allowed = $false; CheckoutRef = ''; Reason = 'dependabot pull request' }
    }
    if ([string]::IsNullOrWhiteSpace($PullRequestHeadSha)) {
        throw 'Trusted pull requests require a head commit SHA.'
    }
    return [pscustomobject][ordered]@{ Allowed = $true; CheckoutRef = $PullRequestHeadSha; Reason = 'same-repository pull request' }
}

function Assert-PureBaseImmutableReleasesEnabled {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ApiRoot,
        [Parameter(Mandatory)][string]$Repository,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][scriptblock]$ApiInvoker
    )

    $uri = "$($ApiRoot.TrimEnd('/'))/repos/$Repository/immutable-releases"
    try {
        $response = & $ApiInvoker 'GET' $uri $Token
    }
    catch {
        if ($_.Exception.Data['StatusCode'] -eq 404) {
            throw "Immutable Releases must be enabled for '$Repository' before release validation can begin."
        }
        throw
    }

    if ($null -eq $response -or $null -eq $response.PSObject.Properties['enabled'] -or -not [bool]$response.enabled) {
        throw "GitHub did not confirm that Immutable Releases are enabled for '$Repository'."
    }
    return $response
}

function Resolve-PureBasePublishedArtifact {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)][string]$AssetName,
        [Parameter()][AllowEmptyString()][string]$ExpectedSha256 = ''
    )

    if ([bool]$Release.draft) {
        throw 'A draft release cannot be reused as a published immutable release.'
    }
    if ($null -eq $Release.PSObject.Properties['immutable'] -or -not [bool]$Release.immutable) {
        throw 'GitHub did not report the published release as immutable.'
    }

    $assets = @($Release.assets | Where-Object name -eq $AssetName)
    if ($assets.Count -ne 1) {
        throw "Published release must contain exactly one asset named '$AssetName'."
    }
    $asset = $assets[0]
    if ([string]$asset.state -ne 'uploaded') {
        throw "Published release asset '$AssetName' is not in the uploaded state."
    }

    $digest = [string]$asset.digest
    if ($digest -notmatch '^sha256:([0-9a-fA-F]{64})$') {
        throw "Published release asset '$AssetName' has no valid SHA-256 digest."
    }
    $sha256 = $Matches[1].ToLowerInvariant()
    if (-not [string]::IsNullOrEmpty($ExpectedSha256) -and
        -not [string]::Equals($sha256, $ExpectedSha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Published release asset '$AssetName' does not match the expected SHA-256."
    }

    $downloadUrl = [string]$asset.browser_download_url
    if ([string]::IsNullOrWhiteSpace($downloadUrl)) {
        throw "Published release asset '$AssetName' has no browser download URL."
    }

    return [pscustomobject][ordered]@{
        Name = $AssetName
        Path = ''
        Sha256 = $sha256
        DownloadUrl = $downloadUrl
        Source = 'published-release'
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-PureBaseStableVersion',
    'Resolve-PureBaseReleaseMode',
    'Resolve-PureBaseResumeTagAction',
    'Invoke-PureBaseGit',
    'New-PureBasePackageUrl',
    'New-PureBaseDispatchPayload',
    'Resolve-PureBaseDailySource',
    'Assert-PureBaseImmutableReleasesEnabled',
    'Resolve-PureBasePublishedArtifact'
)
