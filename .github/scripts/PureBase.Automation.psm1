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
    return "https://github.com/$Repository/releases/download/$Version/$($encodedAssetName)?"
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
        [Parameter()][bool]$PullRequestDraft = $false
    )

    if ($EventName -eq 'push') {
        if ([string]::IsNullOrWhiteSpace($PushSha)) {
            throw 'Push events require a commit SHA.'
        }
        return [pscustomobject][ordered]@{ Allowed = $true; CheckoutRef = $PushSha; Reason = 'push' }
    }
    if ($EventName -ne 'pull_request_target') {
        throw "Unsupported Daily event '$EventName'."
    }
    if (-not [string]::Equals($PullRequestHeadRepository, $Repository, [StringComparison]::Ordinal)) {
        return [pscustomobject][ordered]@{ Allowed = $false; CheckoutRef = ''; Reason = 'external pull request' }
    }
    if ($PullRequestDraft) {
        return [pscustomobject][ordered]@{ Allowed = $false; CheckoutRef = ''; Reason = 'draft pull request' }
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

Export-ModuleMember -Function @(
    'ConvertTo-PureBaseStableVersion',
    'Resolve-PureBaseReleaseMode',
    'Resolve-PureBaseResumeTagAction',
    'New-PureBasePackageUrl',
    'New-PureBaseDispatchPayload',
    'Resolve-PureBaseDailySource',
    'Assert-PureBaseImmutableReleasesEnabled'
)
