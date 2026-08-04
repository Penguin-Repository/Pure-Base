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

    It 'grants attestation permissions only to the release job' {
        $workflow | Should -Match '(?m)^permissions:\n  actions: read\n  contents: read$'
        $workflow | Should -Match '(?ms)^  release:\n.*?^    permissions:\n      actions: read\n      attestations: write\n      contents: read\n      id-token: write$'
        $workflow | Should -Not -Match '(?m)^  contents: write$'
    }

    It 'binds the completed release artifact to the initiating workflow context' {
        $workflow | Should -Match '\[string\]\$state\.phase -cne ''completed'''
        $workflow | Should -Match 'Get-FileHash -LiteralPath \$subjectPath -Algorithm SHA256'
        $workflow | Should -Match 'id = \$env:ACTOR_ID'
        $workflow | Should -Match 'commitSha = \[string\]\$state\.commitSha'
        $workflow | Should -Match 'sha = \$env:WORKFLOW_SHA'
        $workflow | Should -Match 'runAttempt = \[int\]\$env:RUN_ATTEMPT'
        $workflow | Should -Match "environment = 'release'"
    }

    It 'uses a pinned custom attestation and preserves its evidence bundle' {
        $workflow | Should -Match 'uses: actions/attest@508db95dd578ae2727ebd6217d5ba78e4fbda05d # v4'
        $workflow | Should -Match 'predicate-type: https://github\.com/Penguin-Repository/Pure-Base/attestations/release-authorization/v1'
        $workflow | Should -Match 'predicate-path: \$\{\{ steps\.release-authorization\.outputs\.predicate_path \}\}'
        $workflow | Should -Match 'release-authorization\.attestation\.json'
        ([regex]::Matches($workflow, '(?m)^        if: inputs\.preflight_only == false$')).Count | Should -Be 3
    }
}
