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
    $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    Import-Module (Join-Path $repositoryRoot '.github/scripts/PureBase.Automation.psm1') -Force
}

Describe 'Stable release versions' {
    It 'accepts stable unprefixed semantic versions' {
        (ConvertTo-PureBaseStableVersion -Value '1.20.300').ToString() | Should -Be '1.20.300'
    }

    It 'rejects prefixes, prerelease suffixes, metadata, and leading zeroes' -ForEach @(
        @{ Value = 'v1.2.3' },
        @{ Value = '1.2.3-rc.1' },
        @{ Value = '1.2.3+build.1' },
        @{ Value = '01.2.3' }
    ) {
        { ConvertTo-PureBaseStableVersion -Value $Value } | Should -Throw '*stable unprefixed semantic versions*'
    }
}

Describe 'Release mode resolution' {
    It 'allows a fresh release only when the target is newer' {
        $plan = Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0' -TargetVersion '0.2.0'
        $plan.Mode | Should -Be 'fresh'
        $plan.TagState | Should -Be 'missing'
        $plan.ReleaseState | Should -Be 'none'
    }

    It 'rejects equal and older fresh versions' -ForEach @(
        @{ Target = '0.1.0' },
        @{ Target = '0.0.9' }
    ) {
        { Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0' -TargetVersion $Target } | Should -Throw '*must be newer*'
    }

    It 'rejects a fresh release when the tag already exists' {
        { Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0' -TargetVersion '0.2.0' -ExistingTagSha 'abc123' } |
        Should -Throw '*already exists*'
    }

    It 'rejects a fresh release when a draft already exists' {
        $draft = [pscustomobject]@{ draft = $true }
        { Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0' -TargetVersion '0.2.0' -ExistingRelease $draft } |
        Should -Throw '*already exists*'
    }

    It 'allows resume only when package and trigger versions are equal' {
        $draft = [pscustomobject]@{ draft = $true }
        $plan = Resolve-PureBaseReleaseMode -CurrentVersion '0.2.0' -TargetVersion '0.2.0' -Resume -ExistingTagSha 'abc123' -ExistingRelease $draft
        $plan.Mode | Should -Be 'resume'
        $plan.TagState | Should -Be 'present'
        $plan.ReleaseState | Should -Be 'draft'
    }

    It 'recognizes an already published release during resume' {
        $published = [pscustomobject]@{ draft = $false }
        $plan = Resolve-PureBaseReleaseMode -CurrentVersion '0.2.0' -TargetVersion '0.2.0' -Resume -ExistingRelease $published
        $plan.ReleaseState | Should -Be 'published'
    }

    It 'rejects resume while the target is still newer' {
        { Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0' -TargetVersion '0.2.0' -Resume } |
        Should -Throw '*versions are equal*'
    }
}

Describe 'Resume tag handling' {
    It 'creates a missing tag' {
        Resolve-PureBaseResumeTagAction -HeadSha 'abcdef' | Should -Be 'create'
    }

    It 'reuses a tag pointing to HEAD' {
        Resolve-PureBaseResumeTagAction -HeadSha 'ABCDEF' -ExistingTagSha 'abcdef' | Should -Be 'reuse'
    }

    It 'rejects a tag pointing to another commit' {
        { Resolve-PureBaseResumeTagAction -HeadSha 'abcdef' -ExistingTagSha '123456' } |
        Should -Throw '*different commit*'
    }
}

Describe 'Git process output isolation' {
    It 'keeps stderr warnings out of the logic-critical Output value' {
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('PureBase-Git-Test-' + [guid]::NewGuid().ToString('N'))
        try {
            New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
            & git -C $temporaryRoot init --quiet
            if ($LASTEXITCODE -ne 0) { throw 'git init failed for test fixture.' }

            $result = Invoke-PureBaseGit `
                -PackageRoot $temporaryRoot `
                -Arguments @('-c', 'alias.emit=!sh -c ''echo expected; echo warning >&2''', 'emit')

            $result.ExitCode | Should -Be 0
            $result.Output | Should -Be 'expected'
            $result.Error | Should -Be 'warning'
        }
        finally {
            if (Test-Path -LiteralPath $temporaryRoot) {
                Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
            }
        }
    }
}

Describe 'VPM dispatch payload' {
    It 'creates the exact immutable release asset URL without an empty query string' {
        New-PureBasePackageUrl `
            -Repository 'PenguinDOOM/Pure-Base' `
            -Version '0.2.0' `
            -AssetName 'jp.penguin.purebase-0.2.0.zip' |
        Should -Be 'https://github.com/PenguinDOOM/Pure-Base/releases/download/0.2.0/jp.penguin.purebase-0.2.0.zip'
    }

    It 'includes the URL and SHA-256 in repository_dispatch data' {
        $payload = New-PureBaseDispatchPayload `
            -PackageName 'jp.penguin.purebase' `
            -Repository 'PenguinDOOM/Pure-Base' `
            -Version '0.2.0' `
            -CommitSha 'abcdef' `
            -AssetName 'jp.penguin.purebase-0.2.0.zip' `
            -Sha256 '0123456789abcdef' `
            -ReleaseUrl 'https://github.com/PenguinDOOM/Pure-Base/releases/tag/0.2.0'

        $payload.event_type | Should -Be 'update-vpm'
        $payload.client_payload.packageurl | Should -Be 'https://github.com/PenguinDOOM/Pure-Base/releases/download/0.2.0/jp.penguin.purebase-0.2.0.zip'
        $payload.client_payload.sha256 | Should -Be '0123456789abcdef'
    }
}

Describe 'Daily source authorization' {
    It 'accepts pushes' {
        $result = Resolve-PureBaseDailySource -EventName push -Repository 'PenguinDOOM/Pure-Base' -PushSha 'abc123'
        $result.Allowed | Should -BeTrue
        $result.CheckoutRef | Should -Be 'abc123'
    }

    It 'accepts a non-draft branch from the same repository' {
        $result = Resolve-PureBaseDailySource `
            -EventName pull_request `
            -Repository 'PenguinDOOM/Pure-Base' `
            -PullRequestHeadRepository 'PenguinDOOM/Pure-Base' `
            -PullRequestHeadSha 'abc123' `
            -PullRequestAuthor 'octocat'
        $result.Allowed | Should -BeTrue
        $result.CheckoutRef | Should -Be 'abc123'
    }

    It 'accepts repository identity with different owner or repository casing' {
        $result = Resolve-PureBaseDailySource `
            -EventName pull_request `
            -Repository 'PenguinDOOM/Pure-Base' `
            -PullRequestHeadRepository 'penguindoom/pure-base' `
            -PullRequestHeadSha 'abc123' `
            -PullRequestAuthor 'octocat'
        $result.Allowed | Should -BeTrue
    }

    It 'rejects an external fork before runner allocation' {
        $result = Resolve-PureBaseDailySource `
            -EventName pull_request `
            -Repository 'PenguinDOOM/Pure-Base' `
            -PullRequestHeadRepository 'someone/Pure-Base' `
            -PullRequestHeadSha 'abc123' `
            -PullRequestAuthor 'octocat'
        $result.Allowed | Should -BeFalse
        $result.CheckoutRef | Should -BeNullOrEmpty
        $result.Reason | Should -Be 'external pull request'
    }

    It 'rejects draft pull requests' {
        $result = Resolve-PureBaseDailySource `
            -EventName pull_request `
            -Repository 'PenguinDOOM/Pure-Base' `
            -PullRequestHeadRepository 'PenguinDOOM/Pure-Base' `
            -PullRequestHeadSha 'abc123' `
            -PullRequestAuthor 'octocat' `
            -PullRequestDraft $true
        $result.Allowed | Should -BeFalse
        $result.Reason | Should -Be 'draft pull request'
    }

    It 'rejects Dependabot pull requests' {
        $result = Resolve-PureBaseDailySource `
            -EventName pull_request `
            -Repository 'PenguinDOOM/Pure-Base' `
            -PullRequestHeadRepository 'PenguinDOOM/Pure-Base' `
            -PullRequestHeadSha 'abc123' `
            -PullRequestAuthor 'dependabot[bot]'
        $result.Allowed | Should -BeFalse
        $result.CheckoutRef | Should -BeNullOrEmpty
        $result.Reason | Should -Be 'dependabot pull request'
    }

    It 'rejects the legacy pull request target event' {
        {
            Resolve-PureBaseDailySource `
                -EventName pull_request_target `
                -Repository 'PenguinDOOM/Pure-Base' `
                -PullRequestHeadRepository 'PenguinDOOM/Pure-Base' `
                -PullRequestHeadSha 'abc123'
        } | Should -Throw "*Unsupported Daily event 'pull_request_target'*"
    }

    It 'rejects unknown events' {
        {
            Resolve-PureBaseDailySource `
                -EventName workflow_dispatch `
                -Repository 'PenguinDOOM/Pure-Base'
        } | Should -Throw "*Unsupported Daily event 'workflow_dispatch'*"
    }

    It 'rejects pushes without a commit SHA' {
        {
            Resolve-PureBaseDailySource `
                -EventName push `
                -Repository 'PenguinDOOM/Pure-Base'
        } | Should -Throw '*Push events require a commit SHA*'
    }

    It 'rejects pull requests without a head SHA' {
        {
            Resolve-PureBaseDailySource `
                -EventName pull_request `
                -Repository 'PenguinDOOM/Pure-Base' `
                -PullRequestHeadRepository 'PenguinDOOM/Pure-Base' `
                -PullRequestAuthor 'octocat'
        } | Should -Throw '*Trusted pull requests require a head commit SHA*'
    }
}

Describe 'Immutable Releases preflight' {
    It 'calls the repository immutable-releases endpoint and accepts enabled state' {
        $calls = [Collections.Generic.List[object]]::new()
        $invoker = {
            param($Method, $Uri, $Token)
            $calls.Add([pscustomobject]@{ Method = $Method; Uri = $Uri; Token = $Token }) | Out-Null
            return [pscustomobject]@{ enabled = $true; enforced_by_owner = $false }
        }.GetNewClosure()

        $result = Assert-PureBaseImmutableReleasesEnabled `
            -ApiRoot 'https://api.github.com' `
            -Repository 'PenguinDOOM/Pure-Base' `
            -Token 'token' `
            -ApiInvoker $invoker

        $result.enabled | Should -BeTrue
        $calls.Count | Should -Be 1
        $calls[0].Method | Should -Be 'GET'
        $calls[0].Uri | Should -Be 'https://api.github.com/repos/PenguinDOOM/Pure-Base/immutable-releases'
    }

    It 'fails before release validation when immutable releases are disabled' {
        $invoker = {
            param($Method, $Uri, $Token)
            $exception = [InvalidOperationException]::new('Not Found')
            $exception.Data['StatusCode'] = 404
            throw $exception
        }

        { Assert-PureBaseImmutableReleasesEnabled `
                -ApiRoot 'https://api.github.com' `
                -Repository 'PenguinDOOM/Pure-Base' `
                -Token 'token' `
                -ApiInvoker $invoker } |
        Should -Throw '*must be enabled*'
    }

    It 'rejects an unexpected disabled response' {
        $invoker = { param($Method, $Uri, $Token) [pscustomobject]@{ enabled = $false } }
        { Assert-PureBaseImmutableReleasesEnabled `
                -ApiRoot 'https://api.github.com' `
                -Repository 'PenguinDOOM/Pure-Base' `
                -Token 'token' `
                -ApiInvoker $invoker } |
        Should -Throw '*did not confirm*'
    }
}

Describe 'Published immutable release reuse' {
    BeforeAll {
        $assetName = 'jp.penguin.purebase-0.2.0.zip'
        $digest = 'a' * 64
        $publishedRelease = [pscustomobject]@{
            draft     = $false
            immutable = $true
            assets    = @(
                [pscustomobject]@{
                    name                 = $assetName
                    state                = 'uploaded'
                    digest               = "sha256:$digest"
                    browser_download_url = "https://github.com/PenguinDOOM/Pure-Base/releases/download/0.2.0/$assetName"
                }
            )
        }
    }

    It 'reuses the GitHub asset digest instead of requiring a rebuilt ZIP' {
        $artifact = Resolve-PureBasePublishedArtifact -Release $publishedRelease -AssetName $assetName
        $artifact.Source | Should -Be 'published-release'
        $artifact.Path | Should -BeNullOrEmpty
        $artifact.Sha256 | Should -Be ('a' * 64)
        $artifact.DownloadUrl | Should -Match '/releases/download/0\.2\.0/'
    }

    It 'rejects a non-immutable published release' {
        $release = $publishedRelease.PSObject.Copy()
        $release.immutable = $false
        { Resolve-PureBasePublishedArtifact -Release $release -AssetName $assetName } |
        Should -Throw '*immutable*'
    }

    It 'rejects a published asset without a valid digest' {
        $release = [pscustomobject]@{
            draft     = $false
            immutable = $true
            assets    = @([pscustomobject]@{ name = $assetName; state = 'uploaded'; digest = ''; browser_download_url = 'https://example.invalid/file.zip' })
        }
        { Resolve-PureBasePublishedArtifact -Release $release -AssetName $assetName } |
        Should -Throw '*valid SHA-256 digest*'
    }

    It 'validates an expected digest when one is supplied' {
        { Resolve-PureBasePublishedArtifact -Release $publishedRelease -AssetName $assetName -ExpectedSha256 ('b' * 64) } |
        Should -Throw '*expected SHA-256*'
    }
}

Describe 'Production workflow integration' {
    It 'runs the immutable preflight before full release validation' {
        $releaseScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') -Raw
        $preflightIndex = $releaseScript.LastIndexOf('Assert-PureBaseImmutableReleasesEnabled')
        $validationIndex = $releaseScript.LastIndexOf('Invoke-Validation')
        $preflightIndex | Should -BeGreaterThan -1
        $validationIndex | Should -BeGreaterThan $preflightIndex
        $releaseScript | Should -Match 'Resolve-PureBaseReleaseMode'
        $releaseScript | Should -Match 'Resolve-PureBaseResumeTagAction'
        $releaseScript | Should -Match 'Resolve-PureBasePublishedArtifact'
        $releaseScript | Should -Match 'New-PureBaseDispatchPayload'
        $releaseScript | Should -Match "ReleaseState -eq 'published'"
    }

    It 'uses the tested authorization function before allocating the Unity runner' {
        $dailyWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw
        $dailyWorkflow | Should -Match 'Resolve-PureBaseDailySource'
        $dailyWorkflow | Should -Match "needs: authorize\s+if: needs\.authorize\.outputs\.allowed == 'true'\s+runs-on:"
        $dailyWorkflow | Should -Match 'ref: \$\{\{ needs\.authorize\.outputs\.checkout_ref \}\}'
    }

    It 'uses the pull request trigger with exactly the supported activity types' {
        $dailyWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw
        $triggerMatch = [regex]::Match($dailyWorkflow, '(?ms)^  pull_request:\r?\n(?<body>.*?)(?=^permissions:)')
        $triggerMatch.Success | Should -BeTrue

        $activityTypes = @(
            [regex]::Matches($triggerMatch.Groups['body'].Value, '(?m)^\s+-\s+([^\s]+)\s*$') |
            ForEach-Object { $_.Groups[1].Value }
        )
        $activityTypes.Count | Should -Be 4
        ($activityTypes -join ',') | Should -Be 'opened,synchronize,reopened,ready_for_review'
    }

    It 'does not use the privileged pull request target event' {
        $dailyWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw
        $dailyWorkflow | Should -Not -Match 'pull_request_target'
    }

    It 'does not opt into unsafe pull request checkout' {
        $dailyWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw
        $dailyWorkflow | Should -Not -Match 'allow-unsafe-pr-checkout'
    }

    It 'rejects unauthorized pull requests in the rejection job' {
        $dailyWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw
        $dailyWorkflow | Should -Match "(?ms)reject-untrusted-pull-request:.*?needs: authorize\s+if: github\.event_name == 'pull_request' && needs\.authorize\.outputs\.allowed != 'true'"
    }

    It 'passes the pull request author from the event into the resolver' {
        $dailyWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw
        $dailyWorkflow | Should -Match 'PR_AUTHOR: \$\{\{ github\.event\.pull_request\.user\.login \}\}'
        $dailyWorkflow | Should -Match '(?ms)Resolve-PureBaseDailySource.*-PullRequestAuthor\s+\$env:PR_AUTHOR'
    }
}
