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

function ConvertTo-PureBaseReleasePublicationBody {
    [CmdletBinding()]
    [OutputType([System.Collections.Specialized.OrderedDictionary])]
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$TargetCommitSha,
        [Parameter(Mandatory)][bool]$Prerelease
    )

    if ([string]::IsNullOrWhiteSpace($Version)) { throw 'Version is required.' }
    if ($TargetCommitSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'TargetCommitSha must be a full Git SHA.' }

    return [ordered]@{
        tag_name         = $Version
        target_commitish = $TargetCommitSha
        draft            = $false
        prerelease       = $Prerelease
    }
}

function Assert-PureBasePublishedReleaseIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Release,
        [Parameter(Mandatory)][long]$ExpectedReleaseId,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$TargetCommitSha
    )

    if ($ExpectedReleaseId -le 0) { throw 'ExpectedReleaseId must be positive.' }
    if ([string]::IsNullOrWhiteSpace($Version)) { throw 'Version is required.' }
    if ($TargetCommitSha -notmatch '^[0-9a-fA-F]{40}$') { throw 'TargetCommitSha must be a full Git SHA.' }
    if ($null -eq $Release -or $null -eq $Release.PSObject.Properties['id'] -or [long]$Release.id -ne $ExpectedReleaseId) {
        throw "Published release '$Version' returned an invalid release identity."
    }
    if ($null -eq $Release.PSObject.Properties['tag_name'] -or -not [string]::Equals([string]$Release.tag_name, $Version, [StringComparison]::Ordinal)) {
        throw "Published release tag '$([string]$Release.tag_name)' does not match confirmed version '$Version'."
    }
    if ($null -eq $Release.PSObject.Properties['target_commitish'] -or -not [string]::Equals([string]$Release.target_commitish, $TargetCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Published release target '$([string]$Release.target_commitish)' does not match release target '$TargetCommitSha'."
    }

    return $Release
}

function Invoke-PureBaseReleaseLookupWithRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][scriptblock]$Lookup,
        [Parameter()][int]$MaximumAttempts = 5,
        [Parameter()][int]$InitialDelayMilliseconds = 250,
        [Parameter()][scriptblock]$Delay = { param([int]$Milliseconds) Start-Sleep -Milliseconds $Milliseconds }
    )

    if ($MaximumAttempts -le 0) { throw 'MaximumAttempts must be positive.' }
    if ($InitialDelayMilliseconds -le 0) { throw 'InitialDelayMilliseconds must be positive.' }

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        $result = & $Lookup
        if ($null -ne $result) { return $result }
        if ($attempt -eq $MaximumAttempts) { return $null }

        $delayMilliseconds = [int]($InitialDelayMilliseconds * [Math]::Pow(2, $attempt - 1))
        & $Delay $delayMilliseconds
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-PureBaseReleasePublicationBody',
    'Assert-PureBasePublishedReleaseIdentity',
    'Invoke-PureBaseReleaseLookupWithRetry'
)
