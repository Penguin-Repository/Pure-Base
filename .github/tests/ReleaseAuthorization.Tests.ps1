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

Describe 'Release authorization workflow contract' {
    BeforeAll {
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $workflow = (
            Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release.yml') -Raw
        ) -replace "`r`n", "`n"
        $predicateScriptPath = Join-Path $repositoryRoot '.github/scripts/Get-ReleaseAuthorizationPredicate.ps1'
        $predicateScript = (Get-Content -LiteralPath $predicateScriptPath -Raw) -replace "`r`n", "`n"

        function Invoke-PredicateScript {
            param(
                [Parameter(Mandatory)][string]$ArtifactRoot,
                [Parameter()][switch]$Resume
            )

            & $predicateScriptPath `
                -ReleaseArtifactRoot $ArtifactRoot `
                -ActorLogin 'PenguinDOOM' `
                -ActorId '50603637' `
                -TriggeringActorLogin 'PenguinDOOM' `
                -EventName 'workflow_dispatch' `
                -RunId 123456 `
                -RunNumber 42 `
                -RunAttempt 1 `
                -Repository 'Penguin-Repository/Pure-Base' `
                -RepositoryId '1298547445' `
                -RepositoryOwner 'Penguin-Repository' `
                -RepositoryOwnerId '311554875' `
                -DispatchRef 'refs/heads/master' `
                -DispatchRefName 'master' `
                -DispatchRefType 'branch' `
                -WorkflowName 'Release' `
                -WorkflowRef 'Penguin-Repository/Pure-Base/.github/workflows/release.yml@refs/heads/master' `
                -WorkflowSha ('a' * 40) `
                -ConfirmedVersion '0.1.0' `
                -Resume:$Resume
        }
    }

    It 'authorizes only the stable Penguin GitHub account ID before deployment' {
        $workflow | Should -Match '(?m)^  AUTHORIZED_RELEASE_ACTOR_ID: "50603637"$'
        $workflow | Should -Match '(?ms)^  authorize:\n.*?^    permissions: \{\}$'
        $workflow | Should -Match '(?ms)^  authorize:\n.*?ACTOR_ID: \$\{\{ github\.actor_id \}\}'
        $workflow | Should -Match '\$env:ACTOR_ID -cne \$env:AUTHORIZED_RELEASE_ACTOR_ID'
        $workflow | Should -Match '\$env:RUN_ATTEMPT -cne ''1'''
        $workflow | Should -Match '\$env:ACTOR_LOGIN -cne \$env:TRIGGERING_ACTOR_LOGIN'
        $workflow | Should -Match '(?ms)^  release:\n.*?^    needs: authorize$'
        $workflow | Should -Match '(?ms)^  release:\n.*?^    environment: release$'
    }

    It 'revalidates the initiator as the first release job step' {
        $releaseJob = [regex]::Match($workflow, '(?ms)^  release:\n.*?(?=^  [A-Za-z0-9_-]+:\n|\z)').Value
        $releaseJob | Should -Match '(?ms)^    steps:\n      - name: Revalidate release initiator\n.*?RUN_ATTEMPT: \$\{\{ github\.run_attempt \}\}'
        $releaseJob | Should -Match '\$env:RUN_ATTEMPT -cne ''1'''
        $releaseJob.IndexOf('- name: Revalidate release initiator') | Should -BeLessThan $releaseJob.IndexOf('- name: Initialize artifact roots')
    }

    It 'grants attestation permissions only to the release job' {
        $workflow | Should -Match '(?m)^permissions:\n  actions: read\n  contents: read$'
        $workflow | Should -Match '(?ms)^  release:\n.*?^    permissions:\n      actions: read\n      attestations: write\n      contents: read\n      id-token: write$'
        $workflow | Should -Not -Match '(?m)^  contents: write$'
    }

    It 'extracts predicate construction into a repository-owned script' {
        $predicateScriptPath | Should -Exist
        $workflow | Should -Match ([regex]::Escape('& "$env:PACKAGE_ROOT/.github/scripts/Get-ReleaseAuthorizationPredicate.ps1"'))
        $workflow | Should -Not -Match '\$predicate = \[ordered\]@\{'
        $predicateScript | Should -Match '(?m)^\[CmdletBinding\(\)\]$'
        $predicateScript | Should -Match '(?m)^\s*\[Parameter\(Mandatory\)\]\[string\]\$ReleaseArtifactRoot,$'
        $predicateScript | Should -Match 'Get-FileHash -LiteralPath \$subjectPath -Algorithm SHA256'
        $predicateScript | Should -Match 'commitSha = \[string\]\$state\.commitSha'
        $predicateScript | Should -Match 'sha = \$WorkflowSha'
        $predicateScript | Should -Match 'runAttempt = \$RunAttempt'
    }

    It 'rejects malformed release state with an explicit parse error' {
        $artifactRoot = Join-Path $TestDrive 'invalid-state'
        New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
        [IO.File]::WriteAllText(
            (Join-Path $artifactRoot 'release-state.json'),
            '{not-json',
            [Text.UTF8Encoding]::new($false)
        )

        { Invoke-PredicateScript -ArtifactRoot $artifactRoot } |
            Should -Throw -ExpectedMessage 'release-state.json is invalid:*'
    }

    It 'binds a completed release artifact to the initiating workflow context' {
        $artifactRoot = Join-Path $TestDrive 'completed-state'
        $payloadRoot = Join-Path $artifactRoot 'validation-artifact/validated-package'
        New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
        $assetPath = Join-Path $payloadRoot 'jp.penguin.purebase-0.1.0.zip'
        [IO.File]::WriteAllBytes($assetPath, [byte[]](1, 2, 3, 4))
        $sha256 = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $state = [ordered]@{
            phase = 'completed'
            commitSha = 'b' * 40
            releaseUrl = 'https://github.com/Penguin-Repository/Pure-Base/releases/tag/0.1.0'
            vpmRepository = 'Penguin-Repository/Pure-Base-Repository'
            sha256 = $sha256
        }
        [IO.File]::WriteAllText(
            (Join-Path $artifactRoot 'release-state.json'),
            ($state | ConvertTo-Json) + "`n",
            [Text.UTF8Encoding]::new($false)
        )

        $predicate = Invoke-PredicateScript -ArtifactRoot $artifactRoot -Resume | ConvertFrom-Json
        $predicate.schemaVersion | Should -Be 1
        $predicate.authorization.actor.id | Should -Be '50603637'
        $predicate.release.commitSha | Should -Be ('b' * 40)
        $predicate.release.artifact.sha256 | Should -Be $sha256
        $predicate.workflow.sha | Should -Be ('a' * 40)
        $predicate.workflow.runAttempt | Should -Be 1
        $predicate.workflow.environment | Should -Be 'release'
        $predicate.request.resume | Should -BeTrue
        $predicate.request.preflightOnly | Should -BeFalse
    }

    It 'uses a pinned custom attestation and preserves its evidence bundle' {
        $workflow | Should -Match 'uses: actions/attest@508db95dd578ae2727ebd6217d5ba78e4fbda05d # v4'
        $workflow | Should -Match 'predicate-type: https://github\.com/Penguin-Repository/Pure-Base/attestations/release-authorization/v1'
        $workflow | Should -Match 'predicate-path: \$\{\{ steps\.release-authorization\.outputs\.predicate_path \}\}'
        $workflow | Should -Match 'release-authorization\.attestation\.json'
        ([regex]::Matches($workflow, '(?m)^        if: inputs\.preflight_only == false$')).Count | Should -Be 3
    }
}
