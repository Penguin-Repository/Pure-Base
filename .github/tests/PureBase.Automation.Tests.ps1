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

Describe 'Release versions' {
    It 'accepts an unprefixed SemVer version without normalizing its text' {
        (ConvertTo-PureBaseSemVer -Value '1.20.300').original | Should -Be '1.20.300'
    }

    It 'rejects prefixes, prerelease suffixes, metadata, and leading zeroes' -ForEach @(
        @{ Value = 'v1.2.3' },
        @{ Value = '1.2.3+build.1' },
        @{ Value = '01.2.3' }
    ) {
        { ConvertTo-PureBaseSemVer -Value $Value } | Should -Throw
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
        $draft = [pscustomobject]@{ draft = $true; prerelease = $false }
        { Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0' -TargetVersion '0.2.0' -ExistingRelease $draft } |
        Should -Throw '*already exists*'
    }

    It 'allows resume only when package and trigger versions are equal' {
        $draft = [pscustomobject]@{ draft = $true; prerelease = $false }
        $plan = Resolve-PureBaseReleaseMode -CurrentVersion '0.2.0' -TargetVersion '0.2.0' -Resume -ExistingTagSha 'abc123' -ExistingRelease $draft
        $plan.Mode | Should -Be 'resume'
        $plan.TagState | Should -Be 'present'
        $plan.ReleaseState | Should -Be 'draft'
    }

    It 'recognizes an already published release during resume' {
        $published = [pscustomobject]@{ draft = $false; prerelease = $false }
        $plan = Resolve-PureBaseReleaseMode -CurrentVersion '0.2.0' -TargetVersion '0.2.0' -Resume -ExistingRelease $published
        $plan.ReleaseState | Should -Be 'published'
    }

    It 'rejects resume while the target is still newer' {
        { Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0' -TargetVersion '0.2.0' -Resume } |
        Should -Throw '*versions are equal*'
    }
}

Describe 'Resume tag handling' {
    It 'rejects a missing tag during resume' {
        { Resolve-PureBaseResumeTagAction -HeadSha 'abcdef' } | Should -Throw '*must exist*'
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
            -PolicyCommitSha 'abcdef' `
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

Describe 'Prerelease SemVer grammar and precedence' {
    $sharedAcceptanceVectors = @(
        @{ Version = '0.1.0-alpha.1' },
        @{ Version = '0.1.0-beta.1' },
        @{ Version = '0.1.0-beta.2' },
        @{ Version = '0.1.0-rc.1' },
        @{ Version = '0.1.0' }
    )
    $sharedRejectionVectors = @(
        'v0.1.0',
        '01.0.0',
        '0.1',
        '0.1.0-',
        '0.1.0-beta..1',
        '0.1.0-01',
        '0.1.0+build.1',
        '0.1.0-beta.1+build.1'
    )
    $sharedOrderingVectors = @(
        @{ Lower = '0.1.0-alpha'; Higher = '0.1.0-alpha.1' },
        @{ Lower = '0.1.0-alpha.1'; Higher = '0.1.0-alpha.beta' },
        @{ Lower = '0.1.0-alpha.beta'; Higher = '0.1.0-beta' },
        @{ Lower = '0.1.0-beta'; Higher = '0.1.0-beta.2' },
        @{ Lower = '0.1.0-beta.2'; Higher = '0.1.0-beta.11' },
        @{ Lower = '0.1.0-beta.11'; Higher = '0.1.0-rc.1' },
        @{ Lower = '0.1.0-rc.1'; Higher = '0.1.0' }
    )
    $sharedUnboundedVectors = @(
        @{ Lower = '18446744073709551615.999.999'; Higher = '18446744073709551616.0.0' },
        @{ Lower = '0.1.0-beta.18446744073709551615'; Higher = '0.1.0-beta.18446744073709551616' }
    )

    It 'accepts shared SemVer vector <Version> without normalizing its text' -ForEach $sharedAcceptanceVectors {
        $parsed = ConvertTo-PureBaseSemVer -Value $Version
        $parsed.original | Should -Be $Version
    }

    It 'rejects shared SemVer vector <_>' -ForEach $sharedRejectionVectors {
        { ConvertTo-PureBaseSemVer -Value $Value } | Should -Throw
    }

    It 'orders shared SemVer vector <Lower> before <Higher>' -ForEach $sharedOrderingVectors {
        (Compare-PureBaseSemVer -Left $Lower -Right $Higher) | Should -BeLessThan 0
    }

    It 'orders unbounded shared SemVer vector <Lower> before <Higher>' -ForEach $sharedUnboundedVectors {
        (Compare-PureBaseSemVer -Left $Lower -Right $Higher) | Should -BeLessThan 0
    }
}

Describe 'Prerelease release transitions and publication safety' {
    It 'allows a fresh release from <Current> to <Target>' -ForEach @(
        @{ Current = '0.0.0'; Target = '0.1.0-beta.1' },
        @{ Current = '0.1.0-beta.1'; Target = '0.1.0-beta.2' },
        @{ Current = '0.1.0-beta.2'; Target = '0.1.0-rc.1' },
        @{ Current = '0.1.0-rc.1'; Target = '0.1.0' }
    ) {
        (Resolve-PureBaseReleaseMode -CurrentVersion $Current -TargetVersion $Target).Mode | Should -Be 'fresh'
    }

    It 'rejects a fresh downgrade from 0.1.0 to 0.1.0-beta.2' {
        { Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0' -TargetVersion '0.1.0-beta.2' } |
        Should -Throw
    }

    It 'rejects a resume when the package and trigger SemVer text differs' {
        { Resolve-PureBaseReleaseMode -CurrentVersion '0.1.0-beta.1' -TargetVersion '0.1.0-beta.2' -Resume } |
        Should -Throw '*versions are equal*'
    }

    It 'rejects an existing release whose prerelease kind differs from the target' {
        $release = [pscustomobject]@{ draft = $true; prerelease = $false }
        {
            Resolve-PureBaseReleaseMode `
                -CurrentVersion '0.1.0-beta.1' `
                -TargetVersion '0.1.0-beta.1' `
                -Resume `
                -ExistingTagSha 'abcdef' `
                -ExistingRelease $release
        } | Should -Throw '*prerelease*'
    }

    It 'uses an atomic branch and tag push so an injected rejection cannot publish either ref' {
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('PureBase-AtomicPush-' + [guid]::NewGuid().ToString('N'))
        try {
            $remoteRoot = Join-Path $temporaryRoot 'remote.git'
            $localRoot = Join-Path $temporaryRoot 'local'
            New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
            & git init --bare --quiet $remoteRoot
            if ($LASTEXITCODE -ne 0) { throw 'git init --bare failed for atomic push fixture.' }
            & git -C $localRoot init --quiet
            & git -C $localRoot config user.name 'PureBase Test'
            & git -C $localRoot config user.email 'purebase-test@example.invalid'
            [IO.File]::WriteAllText((Join-Path $localRoot 'release.txt'), 'release', [Text.UTF8Encoding]::new($false))
            & git -C $localRoot add -- release.txt
            & git -C $localRoot commit --quiet -m release
            & git -C $localRoot tag --annotate 0.1.0 --message release
            & git -C $localRoot remote add origin $remoteRoot
            $hookPath = Join-Path $remoteRoot 'hooks/pre-receive'
            [IO.File]::WriteAllText($hookPath, "#!/bin/sh`nwhile read old new ref; do`n  if [ `"`$ref`" = `"refs/tags/0.1.0`" ]; then exit 1; fi`ndone`nexit 0`n", [Text.UTF8Encoding]::new($false))
            if (-not $IsWindows) {
                & chmod +x -- $hookPath
                if ($LASTEXITCODE -ne 0) { throw 'chmod failed for atomic push rejection hook.' }
            }

            foreach ($reference in @('refs/heads/master', 'refs/tags/0.1.0')) {
                (& git -C $remoteRoot show-ref --verify --quiet $reference) | Out-Null
                $LASTEXITCODE | Should -Not -Be 0
            }

            & git -C $localRoot push --atomic origin HEAD:master refs/tags/0.1.0 2>$null
            $LASTEXITCODE | Should -Not -Be 0
            foreach ($reference in @('refs/heads/master', 'refs/tags/0.1.0')) {
                (& git -C $remoteRoot show-ref --verify --quiet $reference) | Out-Null
                $LASTEXITCODE | Should -Not -Be 0
            }

            $releaseScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') -Raw
            $releaseScript | Should -Match ([regex]::Escape("Invoke-Git @('push', '--atomic', 'origin'"))
            ($releaseScript -match [regex]::Escape('Invoke-Git @(''push'', ''origin'', "HEAD:$Branch")')) | Should -BeFalse
        }
        finally {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'updates the target manifest before validation and leaves all remote side effects after it' {
        $releaseScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') -Raw
        $targetManifestIndex = $releaseScript.IndexOf('Set-PackageVersion $targetText')
        $validationIndex = $releaseScript.LastIndexOf('Invoke-Validation')
        $commitIndex = $releaseScript.LastIndexOf('Commit-And-Tag $targetText')
        $releaseIndex = $releaseScript.LastIndexOf('Publish-Release $targetText')
        $dispatchIndex = $releaseScript.LastIndexOf('Invoke-Api POST "$apiRoot/repos/$VpmRepository/dispatches"')

        $targetManifestIndex | Should -BeGreaterThan -1
        $validationIndex | Should -BeGreaterThan $targetManifestIndex
        $commitIndex | Should -BeGreaterThan $validationIndex
        $releaseIndex | Should -BeGreaterThan $validationIndex
        $dispatchIndex | Should -BeGreaterThan $validationIndex
    }

    It 'requires the resume tag to point at the release HEAD instead of accepting an advanced HEAD' {
        { Resolve-PureBaseResumeTagAction -HeadSha 'advanced-head' -ExistingTagSha 'release-head' } |
        Should -Throw '*different commit*'
    }
}

Describe 'Release orchestration validation failure' {
    It 'continues to validation when a missing release tag has an empty release list and stops before every remote mutation' {
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('PureBase-Release-Orchestration-' + [guid]::NewGuid().ToString('N'))
        $previousReleaseToken = $env:PUREBASE_RELEASE_TOKEN
        $previousDispatchToken = $env:PUREBASE_DISPATCH_TOKEN
        $previousApiRoot = $env:GITHUB_API_URL
        try {
            $packageRoot = Join-Path $temporaryRoot 'package'
            $remoteRoot = Join-Path $temporaryRoot 'remote.git'
            $pushLogPath = Join-Path $temporaryRoot 'push.log'
            $validationArtifacts = Join-Path $temporaryRoot 'validation-artifacts'
            $releaseArtifacts = Join-Path $temporaryRoot 'release-artifacts'
            $unityEditorPath = Join-Path $temporaryRoot 'Unity.exe'
            $targetVersion = '0.2.0-beta.7'
            $assetName = "jp.penguin.purebase-$targetVersion.zip"
            New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
            New-Item -ItemType File -Path $unityEditorPath -Force | Out-Null
            & git init --bare --quiet $remoteRoot
            if ($LASTEXITCODE -ne 0) { throw 'git init --bare failed for orchestration fixture.' }
            $hookPath = Join-Path $remoteRoot 'hooks/pre-receive'
            $hookLogPath = $pushLogPath.Replace('\', '/')
            [IO.File]::WriteAllText($hookPath, "#!/bin/sh`nprintf 'push\n' >> '$hookLogPath'`n", [Text.UTF8Encoding]::new($false))
            if (-not $IsWindows) {
                & chmod +x -- $hookPath
                if ($LASTEXITCODE -ne 0) { throw 'chmod failed for resume push detection hook.' }
            }
            & git -C $packageRoot init --initial-branch master --quiet
            if ($LASTEXITCODE -ne 0) { throw 'git init failed for orchestration fixture.' }
            & git -C $packageRoot config user.name 'PureBase Test'
            & git -C $packageRoot config user.email 'purebase-test@example.invalid'
            [IO.File]::WriteAllText((Join-Path $packageRoot 'package.json'), '{"name":"jp.penguin.purebase","version":"0.2.0-beta.6"}' + "`n", [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText((Join-Path $packageRoot 'update_trigger.json'), "{`"version`":`"$targetVersion`"}`n", [Text.UTF8Encoding]::new($false))
            & git -C $packageRoot add -- package.json update_trigger.json
            & git -C $packageRoot commit --quiet -m fixture
            if ($LASTEXITCODE -ne 0) { throw 'git commit failed for orchestration fixture.' }
            & git -C $packageRoot remote add origin $remoteRoot
            if ($LASTEXITCODE -ne 0) { throw 'git remote add failed for orchestration fixture.' }

            $apiCalls = [Collections.Generic.List[object]]::new()
            $validationObservations = [Collections.Generic.List[object]]::new()
            $env:PUREBASE_RELEASE_TOKEN = 'test-release-token'
            $env:PUREBASE_DISPATCH_TOKEN = 'test-dispatch-token'
            $env:GITHUB_API_URL = 'https://api.example.invalid'
            $validationInvoker = {
                param($ObservedPackageRoot, $ObservedUnityEditorPath, $ObservedValidationArtifactDirectory, $ObservedAssetName)
                $observedVersion = [string]((Get-Content -LiteralPath (Join-Path $ObservedPackageRoot 'package.json') -Raw | ConvertFrom-Json).version)
                $validationObservations.Add([pscustomobject]@{
                    PackageVersion = $observedVersion
                    AssetName = $ObservedAssetName
                    UnityEditorPath = $ObservedUnityEditorPath
                    ArtifactDirectory = $ObservedValidationArtifactDirectory
                }) | Out-Null
                throw 'Injected validation failure.'
            }.GetNewClosure()

            Mock Invoke-RestMethod {
                param($Method, $Uri)
                $apiCalls.Add([pscustomobject]@{ Method = $Method; Uri = $Uri }) | Out-Null
                if ($Uri -match '/immutable-releases$') { return [pscustomobject]@{ enabled = $true } }
                if ($Uri -match '/releases/tags/') {
                    $exception = [InvalidOperationException]::new('Not Found')
                    $exception | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = 404 })
                    throw $exception
                }
                if ($Uri -match '/releases\?per_page=100$') { return ,([object[]]@()) }
                return $null
            }

            {
                & (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') `
                    -PackageRoot $packageRoot `
                    -UnityEditorPath $unityEditorPath `
                    -ValidationArtifactDirectory $validationArtifacts `
                    -ReleaseArtifactDirectory $releaseArtifacts `
                    -Repository 'test/Pure-Base' `
                    -Branch 'master' `
                    -ConfirmedVersion $targetVersion `
                    -VpmRepository 'test/VPM-Repository' `
                    -AppSlug 'purebase-test' `
                    -ValidationInvoker $validationInvoker
            } | Should -Throw '*Injected validation failure*'

            $validationObservations.Count | Should -Be 1
            $validationObservations[0].PackageVersion | Should -Be $targetVersion
            $validationObservations[0].AssetName | Should -Be $assetName
            $validationObservations[0].UnityEditorPath | Should -Be $unityEditorPath
            $validationObservations[0].ArtifactDirectory | Should -Be $validationArtifacts
            @($apiCalls | Where-Object { $_.Uri -match '/releases/tags/' }).Count | Should -Be 1
            @($apiCalls | Where-Object { $_.Uri -match '/releases\?per_page=100$' }).Count | Should -Be 1
            @($apiCalls | Where-Object { $_.Method -in @('POST', 'PATCH', 'DELETE') }).Count | Should -Be 0
            @($apiCalls | Where-Object { $_.Uri -match '/dispatches$' }).Count | Should -Be 0
            $pushCount = if (Test-Path -LiteralPath $pushLogPath) { @(Get-Content -LiteralPath $pushLogPath).Count } else { 0 }
            $pushCount | Should -Be 0
            (& git -C $remoteRoot show-ref) | Should -BeNullOrEmpty
            $LASTEXITCODE | Should -Not -Be 0
        }
        finally {
            $env:PUREBASE_RELEASE_TOKEN = $previousReleaseToken
            $env:PUREBASE_DISPATCH_TOKEN = $previousDispatchToken
            $env:GITHUB_API_URL = $previousApiRoot
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Release orchestration resume publication' {
    It 'rebuilds and publishes one prerelease asset after reusing an annotated tag without pushing' {
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('PureBase-Resume-Publication-' + [guid]::NewGuid().ToString('N'))
        $previousReleaseToken = $env:PUREBASE_RELEASE_TOKEN
        $previousDispatchToken = $env:PUREBASE_DISPATCH_TOKEN
        $previousApiRoot = $env:GITHUB_API_URL
        try {
            $packageRoot = Join-Path $temporaryRoot 'package'
            $remoteRoot = Join-Path $temporaryRoot 'remote.git'
            $pushLogPath = Join-Path $temporaryRoot 'push.log'
            $validationArtifacts = Join-Path $temporaryRoot 'validation-artifacts'
            $releaseArtifacts = Join-Path $temporaryRoot 'release-artifacts'
            $unityEditorPath = Join-Path $temporaryRoot 'Unity.exe'
            $targetVersion = '0.2.0-beta.7'
            $assetName = "jp.penguin.purebase-$targetVersion.zip"
            New-Item -ItemType Directory -Path (Join-Path $packageRoot 'Tests/Release') -Force | Out-Null
            New-Item -ItemType File -Path $unityEditorPath -Force | Out-Null
            & git init --bare --quiet $remoteRoot
            if ($LASTEXITCODE -ne 0) { throw 'git init --bare failed for resume fixture.' }
            $hookPath = Join-Path $remoteRoot 'hooks/pre-receive'
            $hookLogPath = $pushLogPath.Replace('\', '/')
            [IO.File]::WriteAllText($hookPath, "#!/bin/sh`nprintf 'push\n' >> '$hookLogPath'`n", [Text.UTF8Encoding]::new($false))
            if (-not $IsWindows) {
                & chmod +x -- $hookPath
                if ($LASTEXITCODE -ne 0) { throw 'chmod +x failed for orchestration fixture hook.' }
            }
            & git -C $packageRoot init --initial-branch master --quiet
            if ($LASTEXITCODE -ne 0) { throw 'git init failed for resume fixture.' }
            & git -C $packageRoot config user.name 'PureBase Test'
            & git -C $packageRoot config user.email 'purebase-test@example.invalid'
            [IO.File]::WriteAllText((Join-Path $packageRoot 'package.json'), "{`"name`":`"jp.penguin.purebase`",`"version`":`"$targetVersion`"}`n", [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText((Join-Path $packageRoot 'update_trigger.json'), "{`"version`":`"$targetVersion`"}`n", [Text.UTF8Encoding]::new($false))
            $builderPath = Join-Path $packageRoot 'Tests/Release/Build-PureBaseRelease.ps1'
            $builder = @'
param([Parameter(Mandatory)][string]$OutputDirectory)

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $OutputDirectory 'jp.penguin.purebase-0.2.0-beta.7.zip'
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    $entry = $archive.CreateEntry('package.json')
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write('{"name":"jp.penguin.purebase","version":"0.2.0-beta.7"}') }
    finally { $writer.Dispose() }
}
finally { $archive.Dispose() }

Write-Output "Release ZIP: $zipPath"
Write-Output "SHA-256: $((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant())"
Write-Output 'Audited entries: 1'
'@
            [IO.File]::WriteAllText($builderPath, $builder, [Text.UTF8Encoding]::new($false))
            & git -C $packageRoot add -- package.json update_trigger.json Tests/Release/Build-PureBaseRelease.ps1
            & git -C $packageRoot commit --quiet -m fixture
            if ($LASTEXITCODE -ne 0) { throw 'git commit failed for resume fixture.' }
            & git -C $packageRoot tag --annotate $targetVersion --message release
            if ($LASTEXITCODE -ne 0) { throw 'git tag failed for resume fixture.' }
            & git -C $packageRoot remote add origin $remoteRoot
            if ($LASTEXITCODE -ne 0) { throw 'git remote add failed for resume fixture.' }
            $headSha = (& git -C $packageRoot rev-parse HEAD).Trim()

            $apiCalls = [Collections.Generic.List[object]]::new()
            $validationObservations = [Collections.Generic.List[object]]::new()
            $releaseCreateBodies = [Collections.Generic.List[string]]::new()
            $releaseState = @{ Created = $false; UploadedSha = '' }
            $env:PUREBASE_RELEASE_TOKEN = 'test-release-token'
            $env:PUREBASE_DISPATCH_TOKEN = 'test-dispatch-token'
            $env:GITHUB_API_URL = 'https://api.example.invalid'
            $validationInvoker = {
                param($ObservedPackageRoot, $ObservedUnityEditorPath, $ObservedValidationArtifactDirectory, $ObservedAssetName)
                $validationObservations.Add([pscustomobject]@{
                        PackageVersion = [string]((Get-Content -LiteralPath (Join-Path $ObservedPackageRoot 'package.json') -Raw | ConvertFrom-Json).version)
                        AssetName = $ObservedAssetName
                    }) | Out-Null
            }.GetNewClosure()

            Mock Invoke-RestMethod {
                param($Method, $Uri, $Body, $InFile)
                $apiCalls.Add([pscustomobject]@{ Method = $Method; Uri = $Uri; Body = $Body; InFile = $InFile }) | Out-Null
                if ($Uri -match '/immutable-releases$') { return [pscustomobject]@{ enabled = $true } }
                if ($Uri -match '/releases/tags/') {
                    if ($releaseState.Created) {
                        return [pscustomobject]@{
                            id = 42; draft = $false; prerelease = $true; immutable = $true
                            html_url = "https://github.com/test/Pure-Base/releases/tag/$targetVersion"
                            assets = @([pscustomobject]@{ name = $assetName; digest = "sha256:$($releaseState.UploadedSha)" })
                        }
                    }
                    $exception = [InvalidOperationException]::new('Not Found')
                    $exception | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = 404 })
                    throw $exception
                }
                if ($Uri -match '/releases\?per_page=100$') { return ,([object[]]@()) }
                if ($Method -eq 'POST' -and $Uri -match '/repos/test/Pure-Base/releases$') {
                    $releaseCreateBodies.Add([string]$Body) | Out-Null
                    $releaseState.Created = $true
                    return [pscustomobject]@{ id = 42; draft = $true; prerelease = $true; upload_url = 'https://uploads.example.invalid/releases/42/assets{?name,label}'; assets = @() }
                }
                if ($Method -eq 'POST' -and $Uri -match '^https://uploads\.example\.invalid/') {
                    $releaseState.UploadedSha = (Get-FileHash -LiteralPath $InFile -Algorithm SHA256).Hash.ToLowerInvariant()
                    return $null
                }
                return $null
            }

            & (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') `
                -PackageRoot $packageRoot `
                -UnityEditorPath $unityEditorPath `
                -ValidationArtifactDirectory $validationArtifacts `
                -ReleaseArtifactDirectory $releaseArtifacts `
                -Repository 'test/Pure-Base' `
                -Branch 'master' `
                -ConfirmedVersion $targetVersion `
                -VpmRepository 'test/VPM-Repository' `
                -AppSlug 'purebase-test' `
                -ValidationInvoker $validationInvoker `
                -Resume

            $validationObservations.Count | Should -Be 1
            $validationObservations[0].PackageVersion | Should -Be $targetVersion
            $validationObservations[0].AssetName | Should -Be $assetName
            $releaseCreateBodies.Count | Should -Be 1
            $releaseCreate = $releaseCreateBodies[0] | ConvertFrom-Json
            $releaseCreate.tag_name | Should -Be $targetVersion
            $releaseCreate.target_commitish | Should -Be $headSha
            $releaseCreate.draft | Should -BeTrue
            $releaseCreate.prerelease | Should -BeTrue
            @($apiCalls | Where-Object { $_.Method -eq 'POST' -and $_.Uri -match '^https://uploads\.example\.invalid/' }).Count | Should -Be 1
            $upload = @($apiCalls | Where-Object { $_.Method -eq 'POST' -and $_.Uri -match '^https://uploads\.example\.invalid/' })[0]
            (Split-Path -Leaf $upload.InFile) | Should -Be $assetName
            $releaseState.UploadedSha | Should -Match '^[0-9a-f]{64}$'
            @($apiCalls | Where-Object { $_.Method -eq 'PATCH' -and $_.Uri -match '/releases/42$' }).Count | Should -Be 1
            @($apiCalls | Where-Object { $_.Method -eq 'POST' -and $_.Uri -match '/dispatches$' }).Count | Should -Be 1
            $state = Get-Content -LiteralPath (Join-Path $releaseArtifacts 'release-state.json') -Raw | ConvertFrom-Json
            $state.phase | Should -Be 'completed'
            $state.assetSource | Should -Be 'rebuilt'
            $state.sha256 | Should -Be $releaseState.UploadedSha
            $pushCount = if (Test-Path -LiteralPath $pushLogPath) { @(Get-Content -LiteralPath $pushLogPath).Count } else { 0 }
            $pushCount | Should -Be 0
        }
        finally {
            $env:PUREBASE_RELEASE_TOKEN = $previousReleaseToken
            $env:PUREBASE_DISPATCH_TOKEN = $previousDispatchToken
            $env:GITHUB_API_URL = $previousApiRoot
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Prerelease release naming and dispatch' {
    BeforeAll {
        $version = '0.1.0-beta.1'
        $assetName = 'jp.penguin.purebase-0.1.0-beta.1.zip'
        $releaseUrl = 'https://github.com/PenguinDOOM/Pure-Base/releases/tag/0.1.0-beta.1'
    }

    It 'preserves exact prerelease text in the package URL' {
        New-PureBasePackageUrl -Repository 'PenguinDOOM/Pure-Base' -Version $version -AssetName $assetName |
        Should -Be "https://github.com/PenguinDOOM/Pure-Base/releases/download/$version/$assetName"
    }

    It 'preserves exact prerelease text in the tag, name, ZIP, release URL, and dispatch payload' {
        $payload = New-PureBaseDispatchPayload `
            -PackageName 'jp.penguin.purebase' `
            -Repository 'PenguinDOOM/Pure-Base' `
            -Version $version `
            -CommitSha ('a' * 40) `
            -PolicyCommitSha ('b' * 40) `
            -AssetName $assetName `
            -Sha256 ('c' * 64) `
            -ReleaseUrl $releaseUrl

        $payload.client_payload.version | Should -Be $version
        $payload.client_payload.tag | Should -Be $version
        $payload.client_payload.assetName | Should -Be $assetName
        $payload.client_payload.packageurl | Should -Be "https://github.com/PenguinDOOM/Pure-Base/releases/download/$version/$assetName"
        $payload.client_payload.releaseUrl | Should -Be $releaseUrl
        $payload.client_payload.policyCommitSha | Should -Be ('b' * 40)
    }
}

Describe 'VPM yank policy dispatch preflight' {
    It 'requires a concrete yank dispatch API that validates policy before repository dispatch' {
        Get-Command -Name Invoke-PureBaseYankDispatch -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
    }

    It 'requires a concrete yank policy reader API' {
        Get-Command -Name Read-PureBaseVpmYankPolicy -ErrorAction SilentlyContinue | Should -Not -BeNullOrEmpty
    }

    It 'rejects policy with <Name> before invoking repository dispatch' -ForEach @(
        @{ Name = 'UTF-8 BOM'; Bytes = [byte[]](0xEF, 0xBB, 0xBF, 0x7B, 0x7D) },
        @{ Name = 'invalid UTF-8'; Bytes = [byte[]](0x7B, 0xFF, 0x7D) },
        @{ Name = 'missing required schemaVersion key'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"package":"jp.penguin.purebase","versions":{}}') },
        @{ Name = 'missing required package key'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"versions":{}}') },
        @{ Name = 'missing required versions key'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase"}') },
        @{ Name = 'top-level duplicate key'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"schemaVersion":1,"package":"jp.penguin.purebase","versions":{}}') },
        @{ Name = 'nested duplicate key'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{"0.1.0":"first","0.1.0":"second"}}') },
        @{ Name = 'root trailing comma'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{},}') },
        @{ Name = 'versions trailing comma'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{"0.1.0":"reason",}}') },
        @{ Name = 'trailing data'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{}} trailing') },
        @{ Name = 'unknown schema key'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{},"unknown":true}') },
        @{ Name = 'Boolean schemaVersion'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":true,"package":"jp.penguin.purebase","versions":{}}') },
        @{ Name = 'string schemaVersion'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":"1","package":"jp.penguin.purebase","versions":{}}') },
        @{ Name = 'NaN schemaVersion'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":NaN,"package":"jp.penguin.purebase","versions":{}}') },
        @{ Name = 'Infinity schemaVersion'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":Infinity,"package":"jp.penguin.purebase","versions":{}}') },
        @{ Name = 'unsupported schemaVersion'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":2,"package":"jp.penguin.purebase","versions":{}}') },
        @{ Name = 'wrong package value'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.other","versions":{}}') },
        @{ Name = 'non-object versions value'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":[]}') },
        @{ Name = 'invalid SemVer'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{"v0.1.0":"reason"}}') },
        @{ Name = 'non-string reason'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{"0.1.0":true}}') },
        @{ Name = 'blank reason'; Bytes = [Text.Encoding]::UTF8.GetBytes('{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{"0.1.0":"  "}}') }
    ) {
        if ($null -eq (Get-Command -Name Invoke-PureBaseYankDispatch -ErrorAction SilentlyContinue)) {
            Set-ItResult -Skipped -Because 'Invoke-PureBaseYankDispatch is not implemented.'
            return
        }

        $policyPath = Join-Path $TestDrive 'vpm-yanks.json'
        [IO.File]::WriteAllBytes($policyPath, $Bytes)
        $calls = [Collections.Generic.List[object]]::new()
        $apiInvoker = { param($Method, $Uri, $Token, $Body) $calls.Add($Uri) | Out-Null }.GetNewClosure()

        { Invoke-PureBaseYankDispatch -PolicyPath $policyPath -PolicyCommitSha ('a' * 40) -ApiInvoker $apiInvoker } |
        Should -Throw
        $calls.Count | Should -Be 0
    }

    It 'accepts a policy at the explicit 64 KiB boundary' {
        if ($null -eq (Get-Command -Name Read-PureBaseVpmYankPolicy -ErrorAction SilentlyContinue)) {
            Set-ItResult -Skipped -Because 'Read-PureBaseVpmYankPolicy is not implemented.'
            return
        }

        $prefix = '{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{"0.1.0":"'
        $suffix = '"}}'
        $reasonLength = 65536 - [Text.Encoding]::UTF8.GetByteCount($prefix + $suffix)
        $boundaryPath = Join-Path $TestDrive 'boundary-policy.json'
        [IO.File]::WriteAllText($boundaryPath, $prefix + ('x' * $reasonLength) + $suffix, [Text.UTF8Encoding]::new($false))

        { Read-PureBaseVpmYankPolicy -Path $boundaryPath } | Should -Not -Throw
    }

    It 'rejects a policy at the explicit 64 KiB plus one byte boundary' {
        if ($null -eq (Get-Command -Name Read-PureBaseVpmYankPolicy -ErrorAction SilentlyContinue)) {
            Set-ItResult -Skipped -Because 'Read-PureBaseVpmYankPolicy is not implemented.'
            return
        }

        $prefix = '{"schemaVersion":1,"package":"jp.penguin.purebase","versions":{"0.1.0":"'
        $suffix = '"}}'
        $reasonLength = 65536 - [Text.Encoding]::UTF8.GetByteCount($prefix + $suffix)
        $oversizedPath = Join-Path $TestDrive 'oversized-policy.json'
        [IO.File]::WriteAllText($oversizedPath, $prefix + ('x' * ($reasonLength + 1)) + $suffix, [Text.UTF8Encoding]::new($false))

        { Read-PureBaseVpmYankPolicy -Path $oversizedPath } | Should -Throw '*64 KiB*'
    }
}

Describe 'VPM yank sender workflow contracts' {
    BeforeAll {
        $senderWorkflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/sync-vpm-yanks.yml') -Raw) -replace "`r`n", "`n"
        $payloadStart = $senderWorkflow.IndexOf('$payload =')
        $jsonStart = $senderWorkflow.IndexOf('$body =', $payloadStart)
        $payloadSection = $senderWorkflow.Substring($payloadStart, $jsonStart - $payloadStart)
    }

    It 'triggers only on master policy pushes and manual dispatch' {
        $senderWorkflow | Should -Match '(?m)^  push:$'
        $senderWorkflow | Should -Match '(?ms)^  push:\n    branches:\n      - master\n    paths:\n      - vpm-yanks\.json\n'
        $senderWorkflow | Should -Match '(?m)^  workflow_dispatch:$'
        $senderWorkflow | Should -Not -Match '(?ms)^  workflow_dispatch:\n\s+inputs:'
    }

    It 'rechecks master and validates the current full commit SHA' {
        $senderWorkflow | Should -Match '(?m)^\s+SELECTED_BRANCH: \$\{\{ github\.ref_name \}\}$'
        $senderWorkflow | Should -Match "\$env:SELECTED_BRANCH -ne 'master'"
        $senderWorkflow | Should -Match '\$env:POLICY_COMMIT_SHA -notmatch'
        $senderWorkflow | Should -Match '\^\[0-9a-fA-F\]\{40\}\$'
        $senderWorkflow | Should -Match '(?m)^\s+POLICY_COMMIT_SHA: \$\{\{ github\.sha \}\}$'
    }

    It 'uses the release environment and least workflow permissions' {
        $senderWorkflow | Should -Match '(?m)^  contents: read$'
        $senderWorkflow | Should -Match '(?m)^\s+environment: release$'
        $senderWorkflow | Should -Not -Match '(?m)^\s+contents: write$'
        $senderWorkflow | Should -Not -Match '(?m)^\s+administration:'
        $senderWorkflow | Should -Match 'client-id: \$\{\{ secrets\.APP_CLIENT_ID \}\}'
        $senderWorkflow | Should -Match 'private-key: \$\{\{ secrets\.APP_PRIVATE_KEY \}\}'
        $senderWorkflow | Should -Match 'owner: \$\{\{ steps\.validate-config\.outputs\.vpm_owner \}\}'
        $senderWorkflow | Should -Match 'repositories: \$\{\{ steps\.validate-config\.outputs\.vpm_repository \}\}'
        $senderWorkflow | Should -Match 'permission-contents: write'
    }

    It 'validates the strict policy before dispatch and skips dispatch after validation failure' {
        $validationIndex = $senderWorkflow.IndexOf('Invoke-PureBaseYankDispatch')
        $dispatchIndex = $senderWorkflow.IndexOf('Invoke-RestMethod')
        $validationIndex | Should -BeGreaterThan -1
        $dispatchIndex | Should -BeGreaterThan $validationIndex
        $senderWorkflow | Should -Match "if: steps\.validate-policy\.outcome == 'success'"
        $policyPathContract = [regex]::Escape("-PolicyPath (Join-Path `$env:GITHUB_WORKSPACE 'vpm-yanks.json')")
        $senderWorkflow | Should -Match $policyPathContract
    }

    It 'sends only the fixed receiver event and payload fields' {
        $payloadSection | Should -Match "event_type = 'sync-vpm-yanks'"
        $payloadSection | Should -Match "packageName = 'jp\.penguin\.purebase'"
        $payloadSection | Should -Match "sourceRepository = 'PenguinDOOM/Pure-Base'"
        $payloadSection | Should -Match 'policyCommitSha = \$env:POLICY_COMMIT_SHA'
        $payloadSection | Should -Not -Match '(?i)sourcePath|path|branch|reason|versions'
        $senderWorkflow | Should -Match 'https://api\.github\.com/repos/\$env:VPM_REPOSITORY/dispatches'
    }

    It 'does not expose policy reasons or mutate repository refs' {
        $senderWorkflow | Should -Not -Match '(?i)\breason\b'
        $senderWorkflow | Should -Not -Match '(?im)git\s+push'
        $senderWorkflow | Should -Match 'persist-credentials: false'
        $senderWorkflow | Should -Match 'GITHUB_STEP_SUMMARY'
        $senderWorkflow | Should -Match 'Policy commit SHA:'
        $senderWorkflow | Should -Match 'Yank entry count:'
        $senderWorkflow | Should -Match 'Target repository:'
    }
}
