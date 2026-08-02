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

BeforeAll {
    Import-Module (Join-Path $PSScriptRoot '../scripts/PureBase.ReleasePublication.psm1') -Force
}

Describe 'Release publication tag preservation' {
    It 'builds the publish PATCH body with the confirmed tag and exact target commit' {
        $targetSha = 'a' * 40

        $body = New-PureBaseReleasePublicationBody `
            -Version '0.1.0-beta.4' `
            -TargetCommitSha $targetSha `
            -Prerelease $true

        $body.Keys | Should -Be @('tag_name', 'target_commitish', 'draft', 'prerelease')
        $body.tag_name | Should -Be '0.1.0-beta.4'
        $body.target_commitish | Should -Be $targetSha
        $body.draft | Should -BeFalse
        $body.prerelease | Should -BeTrue
    }

    It 'accepts a published release whose SHA differs only by case' {
        $release = [pscustomobject]@{
            id = 42
            tag_name = '0.1.0-beta.4'
            target_commitish = 'A' * 40
        }

        {
            Assert-PureBasePublishedReleaseIdentity `
                -Release $release `
                -ExpectedReleaseId 42 `
                -Version '0.1.0-beta.4' `
                -TargetCommitSha ('a' * 40)
        } | Should -Not -Throw
    }

    It 'rejects an unexpected release tag or release ID' {
        $release = [pscustomobject]@{
            id = 43
            tag_name = 'untagged-f42a63fceb89b817fe6d'
            target_commitish = 'a' * 40
        }

        {
            Assert-PureBasePublishedReleaseIdentity `
                -Release $release `
                -ExpectedReleaseId 42 `
                -Version '0.1.0-beta.4' `
                -TargetCommitSha ('a' * 40)
        } | Should -Throw '*invalid release identity*'
    }

    It 'retries release lookup five times with exponential backoff' {
        $attemptState = [pscustomobject]@{ Count = 0 }
        $delays = [Collections.Generic.List[int]]::new()
        $lookup = {
            $attemptState.Count++
            if ($attemptState.Count -eq 5) { return [pscustomobject]@{ id = 42 } }
            return $null
        }.GetNewClosure()
        $delay = {
            param([int]$Milliseconds)
            $delays.Add($Milliseconds) | Out-Null
        }.GetNewClosure()

        $result = Invoke-PureBaseReleaseLookupWithRetry `
            -Lookup $lookup `
            -Delay $delay

        $result.id | Should -Be 42
        $attemptState.Count | Should -Be 5
        $delays.ToArray() | Should -Be @(250, 500, 1000, 2000)
    }
}
