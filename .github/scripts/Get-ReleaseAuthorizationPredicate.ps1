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
    [Parameter(Mandatory)][string]$ReleaseArtifactRoot,
    [Parameter(Mandatory)][string]$ActorLogin,
    [Parameter(Mandatory)][string]$ActorId,
    [Parameter(Mandatory)][string]$TriggeringActorLogin,
    [Parameter(Mandatory)][string]$EventName,
    [Parameter(Mandatory)][long]$RunId,
    [Parameter(Mandatory)][long]$RunNumber,
    [Parameter(Mandatory)][int]$RunAttempt,
    [Parameter(Mandatory)][string]$Repository,
    [Parameter(Mandatory)][string]$RepositoryId,
    [Parameter(Mandatory)][string]$RepositoryOwner,
    [Parameter(Mandatory)][string]$RepositoryOwnerId,
    [Parameter(Mandatory)][string]$DispatchRef,
    [Parameter(Mandatory)][string]$DispatchRefName,
    [Parameter(Mandatory)][string]$DispatchRefType,
    [Parameter(Mandatory)][string]$WorkflowName,
    [Parameter(Mandatory)][string]$WorkflowRef,
    [Parameter(Mandatory)][string]$WorkflowSha,
    [Parameter(Mandatory)][string]$ConfirmedVersion,
    [Parameter()][switch]$Resume
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$artifactRoot = [IO.Path]::GetFullPath($ReleaseArtifactRoot)
$statePath = Join-Path $artifactRoot 'release-state.json'
if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
    throw 'Release completion state is missing.'
}

try {
    $stateJson = Get-Content -LiteralPath $statePath -Raw -ErrorAction Stop
    $state = ConvertFrom-Json -InputObject $stateJson -ErrorAction Stop
}
catch {
    throw [IO.InvalidDataException]::new("release-state.json is invalid: $($_.Exception.Message)", $_.Exception)
}
if ($null -eq $state) {
    throw [IO.InvalidDataException]::new('release-state.json did not contain a JSON object.')
}
if ([string]$state.phase -cne 'completed') {
    throw "Release state is '$($state.phase)' instead of 'completed'."
}

foreach ($propertyName in @('commitSha', 'releaseUrl', 'vpmRepository', 'sha256')) {
    $property = $state.PSObject.Properties[$propertyName]
    if ($null -eq $property -or [string]::IsNullOrWhiteSpace([string]$property.Value)) {
        throw "Release completion state is missing '$propertyName'."
    }
}

$assetName = "jp.penguin.purebase-$ConfirmedVersion.zip"
$subjectPath = Join-Path $artifactRoot "validation-artifact/validated-package/$assetName"
if (-not (Test-Path -LiteralPath $subjectPath -PathType Leaf)) {
    throw "Validated release artifact '$subjectPath' is missing."
}

$actualSha256 = (Get-FileHash -LiteralPath $subjectPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedSha256 = ([string]$state.sha256).ToLowerInvariant()
if ($expectedSha256 -notmatch '^[0-9a-f]{64}$') {
    throw "Release completion state SHA-256 '$expectedSha256' is invalid."
}
if ($actualSha256 -cne $expectedSha256) {
    throw "Validated release artifact SHA-256 '$actualSha256' does not match release state '$expectedSha256'."
}

$predicate = [ordered]@{
    schemaVersion = 1
    authorization = [ordered]@{
        method = 'github-actions-workflow-dispatch'
        actor = [ordered]@{
            login = $ActorLogin
            id = $ActorId
        }
        triggeringActor = $TriggeringActorLogin
        eventName = $EventName
    }
    release = [ordered]@{
        repository = $Repository
        repositoryId = $RepositoryId
        repositoryOwner = $RepositoryOwner
        repositoryOwnerId = $RepositoryOwnerId
        version = $ConfirmedVersion
        commitSha = [string]$state.commitSha
        releaseUrl = [string]$state.releaseUrl
        vpmRepository = [string]$state.vpmRepository
        artifact = [ordered]@{
            name = $assetName
            sha256 = $actualSha256
        }
    }
    workflow = [ordered]@{
        name = $WorkflowName
        ref = $WorkflowRef
        sha = $WorkflowSha
        runId = $RunId
        runNumber = $RunNumber
        runAttempt = $RunAttempt
        environment = 'release'
    }
    request = [ordered]@{
        ref = $DispatchRef
        refName = $DispatchRefName
        refType = $DispatchRefType
        resume = [bool]$Resume
        preflightOnly = $false
    }
    recordedAtUtc = [DateTime]::UtcNow.ToString('o')
}

$predicate | ConvertTo-Json -Depth 8
