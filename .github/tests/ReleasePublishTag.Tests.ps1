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

Describe 'Release publication tag preservation' {
    BeforeAll {
        $scriptPath = Join-Path $PSScriptRoot '../scripts/Invoke-PureBaseRelease.ps1'
        $script = Get-Content -LiteralPath $scriptPath -Raw
    }

    It 'publishes the draft with the confirmed tag and exact target commit' {
        $script | Should -Match 'tag_name\s*=\s*\$ConfirmedVersion'
        $script | Should -Match 'target_commitish\s*=\s*\$releaseTargetSha'
    }

    It 'keeps the release ID for post-publish verification' {
        $script | Should -Match '\$releaseId\s*=\s*\[long\]\$release\.id'
        $script | Should -Match 'Get-ReleaseById\s+-ReleaseId\s+\$releaseId'
    }

    It 'rejects an unexpected tag before VPM dispatch' {
        $identityCheck = $script.IndexOf("Published release tag")
        $dispatch = $script.IndexOf("Invoke-MutationGate 'vpm-dispatch'")
        $identityCheck | Should -BeGreaterThan -1
        $dispatch | Should -BeGreaterThan $identityCheck
    }
}
