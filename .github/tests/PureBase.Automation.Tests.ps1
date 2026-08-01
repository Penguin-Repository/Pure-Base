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
    It 'keeps the release script as an artifact-only consumer' {
        $releaseScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') -Raw
        $releaseScript | Should -Match 'Resolve-PureBaseReleaseMode'
        $releaseScript | Should -Match 'Resolve-PureBaseResumeTagAction'
        $releaseScript | Should -Match 'Select-PureBaseReleaseValidationRun'
        $releaseScript | Should -Match 'Resolve-PureBaseValidationArtifact'
        $releaseScript | Should -Match 'Assert-PureBaseValidationManifest'
        $releaseScript | Should -Match 'New-PureBaseDispatchPayload'
        $releaseScript | Should -Match "ReleaseState -eq 'published'"
        $releaseScript | Should -Not -Match 'Invoke-Validation'
        $releaseScript | Should -Not -Match 'UnityEditorPath|UNITY_LICENSE|ConsumerProject'
        $releaseScript | Should -Not -Match 'Set-PackageVersion|WriteAllText.*package\.json|Set-Content.*package\.json'
        $releaseScript | Should -Not -Match "Invoke-Git\s+@\('commit'|git\s+commit"
        $releaseScript | Should -Not -Match 'HEAD:|refs/heads/|--atomic'
        $releaseScript | Should -Not -Match 'update_trigger\.json'
        $releaseScript | Should -Not -Match 'Build-Zip|Compress-Archive|ZipFile\]::Create|CreateFromDirectory'
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

Describe 'Validated artifact fresh release orchestration' {
    BeforeAll {
        function Get-ValidatedArtifactReleaseState {
            param(
                $Release,
                [Parameter(Mandatory = $true)][string]$AssetName
            )

            $assets = if ($null -eq $Release) {
                @()
            }
            else {
                @($Release.assets | ForEach-Object {
                        [pscustomobject]@{ Name = [string]$_.name; Digest = [string]$_.digest }
                    })
            }
            [pscustomobject]@{
                Exists = ($null -ne $Release)
                TagName = if ($null -eq $Release) { '' } else { [string]$Release.tag_name }
                TargetCommitish = if ($null -eq $Release) { '' } else { [string]$Release.target_commitish }
                Draft = if ($null -eq $Release) { $null } else { [bool]$Release.draft }
                Immutable = if ($null -eq $Release) { $null } else { [bool]$Release.immutable }
                Body = if ($null -eq $Release) { '' } else { [string]$Release.body }
                Assets = $assets
                ExpectedAssetDigest = if ($null -eq $Release) { '' } else { [string](@($Release.assets | Where-Object name -eq $AssetName | Select-Object -First 1).digest) }
            }
        }

        function New-ValidatedArtifactReleaseFixture {
            param(
                [switch]$AdvanceBranchBeforeRelease,
                [ValidateSet('fresh', 'draft-resume', 'published-resume')][string]$ReleaseState = 'fresh',
                [ValidateSet('none', 'tag-push', 'draft-create', 'draft-body-repair', 'asset-upload', 'publish', 'vpm-dispatch')][string]$AdvanceBranchBeforeMutation = 'none',
                [ValidateSet('current', 'stale', 'legacy-missing-badge')][string]$DraftBody = 'current',
                [ValidateSet('absent', 'matching', 'mismatched', 'duplicate')][string]$DraftAssetState = 'absent',
                [ValidateSet('valid', 'malformed-manifest', 'manifest-hash-mismatch', 'missing-package-manifest', 'duplicate-package-manifest')][string]$ValidatedArtifactState = 'valid'
            )

            $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('PureBase-Validated-Artifact-Release-' + [guid]::NewGuid().ToString('N'))
            $packageRoot = Join-Path $temporaryRoot 'package'
            $remoteRoot = Join-Path $temporaryRoot 'remote.git'
            $validatedPackageDirectory = Join-Path $temporaryRoot 'validated-package'
            $releaseArtifacts = Join-Path $temporaryRoot 'release-artifacts'
            $validationArtifactArchive = Join-Path $temporaryRoot 'validation-artifact.zip'
            $pushLogPath = Join-Path $temporaryRoot 'push.log'
            $version = '0.2.0-beta.7'
            $assetName = "jp.penguin.purebase-$version.zip"
            $canonicalBadge = "[![Downloads](https://img.shields.io/github/downloads/test/Pure-Base/$version/$assetName?label=downloads)]"
            $generatedNotesBody = 'Generated release notes from GitHub'
            $currentReleaseBody = "$canonicalBadge`n$generatedNotesBody"
            try {
                New-Item -ItemType Directory -Path $packageRoot, $validatedPackageDirectory -Force | Out-Null
                & git init --bare --quiet $remoteRoot
                & git -C $packageRoot init --initial-branch master --quiet
                & git -C $packageRoot config user.name 'PureBase Test'
                & git -C $packageRoot config user.email 'purebase-test@example.invalid'
                [IO.File]::WriteAllText((Join-Path $packageRoot 'package.json'), "{`"name`":`"jp.penguin.purebase`",`"version`":`"$version`"}`n", [Text.UTF8Encoding]::new($false))
                & git -C $packageRoot add -- package.json
                & git -C $packageRoot commit --quiet -m fixture
                if ($ReleaseState -ne 'fresh') {
                    & git -C $packageRoot tag --annotate $version --message "Release $version"
                }
                & git -C $packageRoot remote add origin $remoteRoot
                & git -C $packageRoot push --quiet origin master --tags
                $eventSha = (& git -C $packageRoot rev-parse HEAD).Trim()
                $initialBranchSha = (& git -C $remoteRoot rev-parse refs/heads/master).Trim()

                $zipPath = Join-Path $validatedPackageDirectory $assetName
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
                try {
                    $packageEntryNames = switch ($ValidatedArtifactState) {
                        'missing-package-manifest' { @('payload.txt') }
                        'duplicate-package-manifest' { @('package.json', 'package.json') }
                        default { @('package.json') }
                    }
                    foreach ($entryName in $packageEntryNames) {
                        $entry = $archive.CreateEntry($entryName)
                        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
                        try { $writer.Write("{`"name`":`"jp.penguin.purebase`",`"version`":`"$version`"}") }
                        finally { $writer.Dispose() }
                    }
                }
                finally { $archive.Dispose() }
                $zipSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
                [IO.File]::WriteAllText($zipPath + '.sha256', $zipSha256 + "`n", [Text.UTF8Encoding]::new($false))
                $validationManifestPath = Join-Path $validatedPackageDirectory 'release-validation.json'
                [IO.File]::WriteAllText($validationManifestPath, (@{
                            schemaVersion = 1; repository = 'test/Pure-Base'; headSha = $eventSha; headBranch = 'master'
                            workflowRunId = 11; workflowRunAttempt = 2; version = $version; assetName = $assetName
                            sha256 = if ($ValidatedArtifactState -eq 'manifest-hash-mismatch') { '0' * 64 } else { $zipSha256 }
                        } | ConvertTo-Json -Compress) + "`n", [Text.UTF8Encoding]::new($false))
                if ($ValidatedArtifactState -eq 'malformed-manifest') {
                    [IO.File]::WriteAllText($validationManifestPath, '{invalid manifest' + "`n", [Text.UTF8Encoding]::new($false))
                }
                $validationArtifactStaging = Join-Path $temporaryRoot 'validation-artifact-staging'
                $validationArtifactPayload = Join-Path $validationArtifactStaging 'validated-package'
                New-Item -ItemType Directory -Path $validationArtifactPayload -Force | Out-Null
                Get-ChildItem -LiteralPath $validatedPackageDirectory -Force |
                Copy-Item -Destination $validationArtifactPayload -Recurse -Force
                [IO.Compression.ZipFile]::CreateFromDirectory($validationArtifactStaging, $validationArtifactArchive)

                $hookLogPath = $pushLogPath.Replace('\', '/')
                [IO.File]::WriteAllText((Join-Path $remoteRoot 'hooks/pre-receive'), "#!/bin/sh`nwhile read old new ref; do printf '%s\n' `"`$ref`" >> '$hookLogPath'; done`n", [Text.UTF8Encoding]::new($false))
                if (-not $IsWindows) { & chmod +x -- (Join-Path $remoteRoot 'hooks/pre-receive') }

                $advanceRemoteBranch = {
                    $advancerRoot = Join-Path $temporaryRoot 'advancer'
                    & git clone --quiet $remoteRoot $advancerRoot
                    & git -C $advancerRoot config user.name 'Advance Test'
                    & git -C $advancerRoot config user.email 'advance-test@example.invalid'
                    [IO.File]::WriteAllText((Join-Path $advancerRoot 'branch-advance.txt'), "advance`n", [Text.UTF8Encoding]::new($false))
                    & git -C $advancerRoot add -- branch-advance.txt
                    & git -C $advancerRoot commit --quiet -m advance
                    & git -C $advancerRoot push --quiet origin master
                    Remove-Item -LiteralPath $advancerRoot -Recurse -Force
                }.GetNewClosure()

                if ($AdvanceBranchBeforeRelease) {
                    & $advanceRemoteBranch
                }

                $previousReleaseToken = $env:PUREBASE_RELEASE_TOKEN
                $previousDispatchToken = $env:PUREBASE_DISPATCH_TOKEN
                $previousApiRoot = $env:GITHUB_API_URL
                $env:PUREBASE_RELEASE_TOKEN = 'test-release-token'
                $env:PUREBASE_DISPATCH_TOKEN = 'test-dispatch-token'
                $env:GITHUB_API_URL = 'https://api.example.invalid'
                $apiCalls = [Collections.Generic.List[object]]::new()
                $operationLog = [Collections.Generic.List[object]]::new()
                $mutationBoundaries = [Collections.Generic.List[string]]::new()
                $tagPushCountAtGate = [Collections.Generic.Dictionary[string, int]]::new()
                $dispatchPayloads = [Collections.Generic.List[object]]::new()
                $initialAssets = switch ($DraftAssetState) {
                    'matching' { @([pscustomobject]@{ id = 7; name = $assetName; digest = "sha256:$zipSha256"; browser_download_url = "https://downloads.example.invalid/$assetName" }) }
                    'mismatched' { @([pscustomobject]@{ id = 7; name = $assetName; digest = 'sha256:' + ('0' * 64); browser_download_url = "https://downloads.example.invalid/$assetName" }) }
                    'duplicate' { @([pscustomobject]@{ id = 7; name = $assetName; digest = "sha256:$zipSha256" }, [pscustomobject]@{ id = 8; name = $assetName; digest = "sha256:$zipSha256" }) }
                    default { @() }
                }
                $release = if ($ReleaseState -eq 'fresh') {
                    $null
                }
                else {
                    [pscustomobject]@{
                        id = 42; tag_name = $version; target_commitish = $eventSha; draft = ($ReleaseState -eq 'draft-resume')
                        prerelease = $true; immutable = ($ReleaseState -eq 'published-resume'); body = if ($DraftBody -eq 'stale') { 'stale release body' } elseif ($DraftBody -eq 'legacy-missing-badge') { 'legacy immutable release body' } else { $currentReleaseBody }
                        upload_url = 'https://uploads.example.invalid/releases/42/assets{?name,label}'; assets = $initialAssets
                        html_url = "https://github.example.invalid/test/Pure-Base/releases/tag/$version"
                    }
                }
                $initialReleaseState = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                $beforeMutation = {
                    param([string]$Boundary)
                    $mutationBoundaries.Add($Boundary) | Out-Null
                    $tagPushCountAtGate[$Boundary] = if (Test-Path -LiteralPath $pushLogPath) { @((Get-Content -LiteralPath $pushLogPath) | Where-Object { $_ -like 'refs/tags/*' }).Count } else { 0 }
                    $operationLog.Add([pscustomobject]@{ Kind = 'gate'; Boundary = $Boundary; ApiCall = $null }) | Out-Null
                    if ($Boundary -eq $AdvanceBranchBeforeMutation) {
                        & $advanceRemoteBranch
                    }
                }.GetNewClosure()
                Mock Invoke-RestMethod {
                    param($Method, $Uri, $Body, $InFile)
                    $apiCall = [pscustomobject]@{
                            Method = $Method; Uri = $Uri; Body = $Body; InFile = $InFile
                            ReleaseDraft = if ($null -eq $release) { $null } else { $release.draft }
                            ReleaseImmutable = if ($null -eq $release) { $null } else { $release.immutable }
                            AssetDigest = if ($null -eq $release) { $null } else { (@($release.assets | Where-Object name -eq $assetName | Select-Object -First 1).digest) }
                            StateBefore = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                            StateAfter = $null
                        }
                    $apiCalls.Add($apiCall) | Out-Null
                    $operationLog.Add([pscustomobject]@{ Kind = 'api'; Boundary = ''; ApiCall = $apiCall }) | Out-Null
                    if ($Uri -match '/immutable-releases$') {
                        $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                        return [pscustomobject]@{ enabled = $true }
                    }
                    if ($Uri -match '/actions/workflows/release-validation\.yml/runs') {
                        $operationLog.Add([pscustomobject]@{ Kind = 'validation-run'; Boundary = ''; ApiCall = $apiCall }) | Out-Null
                        $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                        return [pscustomobject]@{
                            workflow_runs = @([pscustomobject]@{
                                    id = 11; path = 'release-validation.yml'; head_sha = $eventSha; head_branch = 'master'
                                    event = 'workflow_dispatch'; run_number = 11; run_attempt = 2; status = 'completed'; conclusion = 'success'
                                })
                        }
                    }
                    if ($Uri -match '/actions/runs/11/artifacts') {
                        $operationLog.Add([pscustomobject]@{ Kind = 'validation-artifact'; Boundary = ''; ApiCall = $apiCall }) | Out-Null
                        $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                        return [pscustomobject]@{
                            artifacts = @([pscustomobject]@{
                                    id = 7; name = 'pure-base-release-validation-11-2'; expired = $false
                                    workflow_run = [pscustomobject]@{ id = 11; run_attempt = 2 }
                                })
                        }
                    }
                    if ($Uri -match '/actions/artifacts/7/zip') {
                        $operationLog.Add([pscustomobject]@{ Kind = 'validation-archive-request'; Boundary = ''; ApiCall = $apiCall }) | Out-Null
                        $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                        return [pscustomobject]@{ StatusCode = 302; Headers = @{ Location = 'https://objects.example.invalid/validation-artifact.zip' } }
                    }
                    if ($Uri -match '/releases/tags/') {
                        if ($null -ne $release) {
                            $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                            return $release
                        }
                        $exception = [InvalidOperationException]::new('Not Found')
                        $exception | Add-Member -NotePropertyName Response -NotePropertyValue ([pscustomobject]@{ StatusCode = 404 })
                        throw $exception
                    }
                    if ($Uri -match '/releases\?per_page=100$') {
                        $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                        return ,([object[]]@())
                    }
                    if ($Method -eq 'POST' -and $Uri -match '/repos/test/Pure-Base/releases$') {
                        $create = $Body | ConvertFrom-Json
                        $release = [pscustomobject]@{
                            id = 42; tag_name = $version; target_commitish = $eventSha; draft = $true; prerelease = $true; immutable = $false
                            body = "$([string]$create.body)`n$generatedNotesBody"; upload_url = 'https://uploads.example.invalid/releases/42/assets{?name,label}'; assets = @()
                            html_url = "https://github.example.invalid/test/Pure-Base/releases/tag/$version"
                        }
                        $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                        return $release
                    }
                    if ($Method -eq 'POST' -and $Uri -match '/assets\?name=') {
                        $release.assets = @($release.assets) + [pscustomobject]@{
                            id = 9; name = $assetName; digest = "sha256:$zipSha256"; browser_download_url = "https://downloads.example.invalid/$assetName"
                        }
                        $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                        return $release.assets[-1]
                    }
                    if ($Method -eq 'PATCH' -and $Uri -match '/repos/test/Pure-Base/releases/42$') {
                        $patch = $Body | ConvertFrom-Json
                        if ($null -ne $patch.PSObject.Properties['body']) { $release.body = [string]$patch.body }
                        if ($null -ne $patch.PSObject.Properties['draft'] -and -not [bool]$patch.draft) {
                            $release.draft = $false
                            $release.immutable = $true
                        }
                        $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                        return $release
                    }
                    if ($Method -eq 'POST' -and $Uri -match '/dispatches$') {
                        $dispatchPayloads.Add($Body) | Out-Null
                    }
                    $apiCall.StateAfter = Get-ValidatedArtifactReleaseState -Release $release -AssetName $assetName
                    return $null
                }
                Mock Invoke-WebRequest {
                    param($Method, $Uri, $Headers, $OutFile, $MaximumRedirection)
                    if ($Uri -match '/actions/artifacts/7/zip') {
                        $operationLog.Add([pscustomobject]@{ Kind = 'validation-archive-request'; Boundary = ''; ApiCall = [pscustomobject]@{ Uri = $Uri; OutFile = $OutFile } }) | Out-Null
                        return [pscustomobject]@{ StatusCode = 302; Headers = @{ Location = 'https://objects.example.invalid/validation-artifact.zip' } }
                    }
                    $operationLog.Add([pscustomobject]@{ Kind = 'validation-archive-download'; Boundary = ''; ApiCall = [pscustomobject]@{ Uri = $Uri; OutFile = $OutFile } }) | Out-Null
                    if ($Uri -ne 'https://objects.example.invalid/validation-artifact.zip') {
                        throw "Unexpected validation artifact download URI '$Uri'."
                    }
                    if ([string]::IsNullOrEmpty($OutFile)) {
                        throw 'Validation artifact download did not provide an output path.'
                    }
                    Copy-Item -LiteralPath $validationArtifactArchive -Destination $OutFile -Force
                    return [pscustomobject]@{ StatusCode = 200 }
                }

                $failure = $null
                try {
                    $releaseArguments = @{
                        PackageRoot = $packageRoot; ValidatedEventSha = $eventSha
                        ReleaseArtifactDirectory = $releaseArtifacts; Repository = 'test/Pure-Base'; Branch = 'master'; ConfirmedVersion = $version
                        VpmRepository = 'test/VPM-Repository'; AppSlug = 'purebase-test'; BeforeMutation = $beforeMutation
                    }
                    if ($ReleaseState -ne 'fresh') { $releaseArguments.Resume = $true }
                    & (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') @releaseArguments
                }
                catch { $failure = $_ }

                return [pscustomobject]@{
                    Failure = $failure; ApiCalls = $apiCalls.ToArray(); RemoteRoot = $remoteRoot; InitialBranchSha = $initialBranchSha
                    EventSha = $eventSha; Version = $version; PushLogPath = $pushLogPath; PreviousReleaseToken = $previousReleaseToken
                    PreviousDispatchToken = $previousDispatchToken; PreviousApiRoot = $previousApiRoot; TemporaryRoot = $temporaryRoot; ZipPath = $zipPath
                    ZipSha256 = $zipSha256; MutationBoundaries = $mutationBoundaries.ToArray(); Release = $release; AssetName = $assetName
                    ConfirmedVersion = $version; ValidationManifestPath = (Join-Path $validatedPackageDirectory 'release-validation.json'); PackageRoot = $packageRoot
                    OperationLog = $operationLog.ToArray(); TagPushCountAtGate = $tagPushCountAtGate; DispatchPayloads = $dispatchPayloads.ToArray()
                    CanonicalBadge = $canonicalBadge; CurrentReleaseBody = $currentReleaseBody; GeneratedNotesBody = $generatedNotesBody; InitialReleaseState = $initialReleaseState; ValidatedArtifactState = $ValidatedArtifactState
                    ValidationArtifactArchive = $validationArtifactArchive; ReleaseArtifactDirectory = $releaseArtifacts
                }
            }
            catch {
                Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
                throw
            }
        }

        function Remove-ValidatedArtifactReleaseFixture {
            param([Parameter(Mandatory = $true)]$Fixture)
            $env:PUREBASE_RELEASE_TOKEN = $Fixture.PreviousReleaseToken
            $env:PUREBASE_DISPATCH_TOKEN = $Fixture.PreviousDispatchToken
            $env:GITHUB_API_URL = $Fixture.PreviousApiRoot
            Remove-Item -LiteralPath $Fixture.TemporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
        }

        function New-StaleDraftBodyRepairFixture {
            return New-ValidatedArtifactReleaseFixture -ReleaseState 'draft-resume' -DraftBody 'stale' -DraftAssetState 'matching'
        }
    }

    It 'runs an artifact-only release invocation with package.json as the sole identity declaration and no update-trigger data' {
        $fixture = New-ValidatedArtifactReleaseFixture
        try {
            $eventPackage = (& git -C $fixture.RemoteRoot show "$($fixture.EventSha):package.json" | ConvertFrom-Json)
            $archive = [IO.Compression.ZipFile]::OpenRead($fixture.ZipPath)
            try {
                $entry = @($archive.Entries | Where-Object FullName -ceq 'package.json')
                $entry.Count | Should -Be 1
                $reader = [IO.StreamReader]::new($entry[0].Open(), [Text.UTF8Encoding]::new($false, $true))
                try { $archivePackage = $reader.ReadToEnd() | ConvertFrom-Json }
                finally { $reader.Dispose() }
            }
            finally { $archive.Dispose() }
            $manifest = Get-Content -LiteralPath $fixture.ValidationManifestPath -Raw | ConvertFrom-Json

            $eventPackage.version | Should -Be $fixture.Version
            $archivePackage.version | Should -Be $fixture.Version
            $manifest.version | Should -Be $fixture.Version
            $fixture.ConfirmedVersion | Should -Be $fixture.Version
            $fixture.AssetName | Should -Be "jp.penguin.purebase-$($fixture.Version).zip"
            Test-Path -LiteralPath (Join-Path $fixture.PackageRoot 'update_trigger.json') | Should -BeFalse
            @(& git -C $fixture.RemoteRoot ls-tree -r --name-only $fixture.EventSha) | Should -Be @('package.json')
            $fixture.Failure | Should -BeNullOrEmpty
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'selects and verifies the exact validation artifact before the first remote mutation' {
        $fixture = New-ValidatedArtifactReleaseFixture
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            $firstMutationIndex = [array]::FindIndex($fixture.OperationLog, [Predicate[object]]{
                    param($entry)
                    $entry.Kind -eq 'gate' -and $entry.Boundary -eq 'tag-push'
                })
            $firstMutationIndex | Should -BeGreaterThan -1

            foreach ($validationOperation in @('validation-run', 'validation-artifact', 'validation-archive-request', 'validation-archive-download')) {
                $operationIndex = [array]::FindIndex($fixture.OperationLog, [Predicate[object]]{
                        param($entry)
                        $entry.Kind -eq $validationOperation
                    }.GetNewClosure())
                $operationIndex | Should -BeGreaterThan -1
                $operationIndex | Should -BeLessThan $firstMutationIndex
            }

            $preflightIndex = [array]::FindIndex($fixture.OperationLog, [Predicate[object]]{
                    param($entry)
                    $entry.Kind -eq 'api' -and $entry.ApiCall.Uri -match '/immutable-releases$'
                })
            $preflightIndex | Should -BeGreaterThan -1
            $preflightIndex | Should -BeLessThan $firstMutationIndex
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'creates one annotated tag at the validated event SHA without changing the release branch' {
        $fixture = New-ValidatedArtifactReleaseFixture
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            (& git -C $fixture.RemoteRoot rev-parse refs/heads/master).Trim() | Should -Be $fixture.InitialBranchSha
            (& git -C $fixture.RemoteRoot cat-file -t "refs/tags/$($fixture.Version)").Trim() | Should -Be 'tag'
            (& git -C $fixture.RemoteRoot rev-parse "refs/tags/$($fixture.Version)^{commit}").Trim() | Should -Be $fixture.EventSha
            @(Get-Content -LiteralPath $fixture.PushLogPath) | Should -Be @("refs/tags/$($fixture.Version)")
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'creates the fresh draft through the release API before it can be published' {
        $fixture = New-ValidatedArtifactReleaseFixture
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            $draftCreates = @($fixture.ApiCalls | Where-Object { $_.Method -eq 'POST' -and $_.Uri -eq 'https://api.example.invalid/repos/test/Pure-Base/releases' })
            $draftCreates.Count | Should -Be 1
            $draftCreates[0].StateBefore.Exists | Should -BeFalse
            $draftCreates[0].StateAfter.Exists | Should -BeTrue
            $draftCreates[0].StateAfter.Draft | Should -BeTrue
            $draftCreates[0].StateAfter.Immutable | Should -BeFalse
            $draftCreates[0].StateAfter.TagName | Should -Be $fixture.Version
            $draftCreates[0].StateAfter.TargetCommitish | Should -Be $fixture.EventSha
            $createRequest = $draftCreates[0].Body | ConvertFrom-Json
            $createRequest.body | Should -Be $fixture.CanonicalBadge
            $createRequest.generate_release_notes | Should -BeTrue
            $createRequest.body.StartsWith($fixture.CanonicalBadge, [StringComparison]::Ordinal) | Should -BeTrue
            ([regex]::Matches($createRequest.body, '\[!\[Downloads\]\(')).Count | Should -Be 1
            $draftCreates[0].StateAfter.Body | Should -Be "$($fixture.CanonicalBadge)`n$($fixture.GeneratedNotesBody)"
            $draftCreates[0].StateAfter.Body | Should -Be $fixture.CurrentReleaseBody
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'rejects a branch advance before tag creation and never creates the remote tag' {
        $fixture = New-ValidatedArtifactReleaseFixture -AdvanceBranchBeforeRelease
        try {
            $fixture.Failure | Should -Not -BeNullOrEmpty
            $fixture.Failure.Exception.Message | Should -Match 'remote release branch.*advanced'
            (& git -C $fixture.RemoteRoot show-ref --verify --quiet "refs/tags/$($fixture.Version)")
            $LASTEXITCODE | Should -Not -Be 0
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'rejects a tag-push pre-mutation branch advance without creating a tag, release mutation, or dispatch' {
        $fixture = New-ValidatedArtifactReleaseFixture -AdvanceBranchBeforeMutation 'tag-push'
        try {
            $fixture.Failure | Should -Not -BeNullOrEmpty
            $fixture.Failure.Exception.Message | Should -Match 'remote release branch.*advanced'
            $fixture.MutationBoundaries | Should -Contain 'tag-push'
            $fixture.TagPushCountAtGate['tag-push'] | Should -Be 0
            (& git -C $fixture.RemoteRoot show-ref --verify --quiet "refs/tags/$($fixture.Version)")
            $LASTEXITCODE | Should -Not -Be 0
            (& git -C $fixture.RemoteRoot rev-parse refs/heads/master).Trim() | Should -Not -Be $fixture.InitialBranchSha
            @($fixture.ApiCalls | Where-Object { $_.Method -in @('POST', 'PATCH', 'DELETE') }).Count | Should -Be 0
            $fixture.DispatchPayloads.Count | Should -Be 0
            $releaseState = Get-ValidatedArtifactReleaseState -Release $fixture.Release -AssetName $fixture.AssetName
            $releaseState.Exists | Should -BeFalse
            $releaseState.Assets.Count | Should -Be 0
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'rejects a remote branch advance at the <Boundary> mutation gate without downstream mutations' -ForEach @(
        @{ Boundary = 'draft-create'; ReleaseState = 'fresh'; DraftBody = 'current'; DraftAssetState = 'absent'; Forbidden = @('asset-upload', 'publish', 'vpm-dispatch'); ReleaseExists = $false; ExpectedDraft = $null; ExpectedImmutable = $null; ExpectedBody = ''; ExpectedAssetCount = 0; ExpectedDigest = '' },
        @{ Boundary = 'draft-body-repair'; ReleaseState = 'draft-resume'; DraftBody = 'stale'; DraftAssetState = 'matching'; Forbidden = @('asset-upload', 'publish', 'vpm-dispatch'); ReleaseExists = $true; ExpectedDraft = $true; ExpectedImmutable = $false; ExpectedBody = 'stale release body'; ExpectedAssetCount = 1; ExpectedDigest = 'matching' },
        @{ Boundary = 'asset-upload'; ReleaseState = 'draft-resume'; DraftBody = 'current'; DraftAssetState = 'absent'; Forbidden = @('publish', 'vpm-dispatch'); ReleaseExists = $true; ExpectedDraft = $true; ExpectedImmutable = $false; ExpectedBody = 'canonical'; ExpectedAssetCount = 0; ExpectedDigest = '' },
        @{ Boundary = 'publish'; ReleaseState = 'draft-resume'; DraftBody = 'current'; DraftAssetState = 'matching'; Forbidden = @('vpm-dispatch'); ReleaseExists = $true; ExpectedDraft = $true; ExpectedImmutable = $false; ExpectedBody = 'canonical'; ExpectedAssetCount = 1; ExpectedDigest = 'matching' },
        @{ Boundary = 'vpm-dispatch'; ReleaseState = 'published-resume'; DraftBody = 'current'; DraftAssetState = 'matching'; Forbidden = @(); ReleaseExists = $true; ExpectedDraft = $false; ExpectedImmutable = $true; ExpectedBody = 'canonical'; ExpectedAssetCount = 1; ExpectedDigest = 'matching' }
    ) {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState $ReleaseState -DraftBody $DraftBody -DraftAssetState $DraftAssetState -AdvanceBranchBeforeMutation $Boundary
        try {
            $fixture.Failure | Should -Not -BeNullOrEmpty
            $fixture.Failure.Exception.Message | Should -Match 'remote release branch.*advanced'
            $fixture.MutationBoundaries | Should -Contain $Boundary
            $gateIndex = [array]::FindIndex($fixture.OperationLog, [Predicate[object]]{ param($entry) $entry.Kind -eq 'gate' -and $entry.Boundary -eq $Boundary })
            $gateIndex | Should -BeGreaterThan -1
            $postGateMutations = @($fixture.OperationLog | Select-Object -Skip ($gateIndex + 1) | Where-Object {
                    $_.Kind -eq 'api' -and $_.ApiCall.Method -in @('POST', 'PATCH', 'DELETE')
                })
            $postGateMutations.Count | Should -Be 0
            $finalTagPushCount = if (Test-Path -LiteralPath $fixture.PushLogPath) { @((Get-Content -LiteralPath $fixture.PushLogPath) | Where-Object { $_ -like 'refs/tags/*' }).Count } else { 0 }
            $finalTagPushCount | Should -Be $fixture.TagPushCountAtGate[$Boundary]
            (& git -C $fixture.RemoteRoot rev-parse "refs/tags/$($fixture.Version)^{commit}").Trim() | Should -Be $fixture.EventSha
            $releaseState = Get-ValidatedArtifactReleaseState -Release $fixture.Release -AssetName $fixture.AssetName
            $releaseState.Exists | Should -Be $ReleaseExists
            $releaseState.Draft | Should -Be $ExpectedDraft
            $releaseState.Immutable | Should -Be $ExpectedImmutable
            $releaseState.Body | Should -Be (if ($ExpectedBody -eq 'canonical') { $fixture.CurrentReleaseBody } else { $ExpectedBody })
            $releaseState.Assets.Count | Should -Be $ExpectedAssetCount
            if ($ReleaseExists) {
                $releaseState.TagName | Should -Be $fixture.Version
                $releaseState.TargetCommitish | Should -Be $fixture.EventSha
            }
            if ($ExpectedDigest -eq 'matching') {
                $releaseState.ExpectedAssetDigest | Should -Be "sha256:$($fixture.ZipSha256)"
            }
            foreach ($forbiddenBoundary in $Forbidden) {
                $fixture.MutationBoundaries | Should -Not -Contain $forbiddenBoundary
            }
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'repairs the draft body before publishing without recreating its release or asset' {
        $fixture = New-StaleDraftBodyRepairFixture
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            $repairIndex = [array]::IndexOf($fixture.MutationBoundaries, 'draft-body-repair')
            $publishIndex = [array]::IndexOf($fixture.MutationBoundaries, 'publish')
            $repairIndex | Should -BeGreaterThan -1
            $publishIndex | Should -BeGreaterThan $repairIndex
            $fixture.MutationBoundaries | Should -Not -Contain 'draft-create'
            $fixture.MutationBoundaries | Should -Not -Contain 'asset-upload'
            $bodyRepairPatches = @($fixture.ApiCalls | Where-Object {
                    if ($_.Method -ne 'PATCH' -or $_.Uri -notmatch '/repos/test/Pure-Base/releases/42$') { return $false }
                    $payload = $_.Body | ConvertFrom-Json
                    return $null -ne $payload.PSObject.Properties['body']
                })
            $bodyRepairPatches.Count | Should -Be 1
            $repairedBody = [string](($bodyRepairPatches[0].Body | ConvertFrom-Json).body)
            $repairedBody.StartsWith($fixture.CanonicalBadge, [StringComparison]::Ordinal) | Should -BeTrue
            ([regex]::Matches($repairedBody, '\[!\[Downloads\]\(')).Count | Should -Be 1
            $repairedBody | Should -Be "$($fixture.CanonicalBadge)`nstale release body"
            $fixture.Release.body | Should -Be $repairedBody
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'preserves the already canonical current draft body through asset and publish gates' {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState 'draft-resume' -DraftBody 'current' -DraftAssetState 'matching'
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            $fixture.Release.body | Should -Be $fixture.CurrentReleaseBody
            $fixture.Release.body.StartsWith($fixture.CanonicalBadge, [StringComparison]::Ordinal) | Should -BeTrue
            ([regex]::Matches($fixture.Release.body, '\[!\[Downloads\]\(')).Count | Should -Be 1
            @($fixture.ApiCalls | Where-Object {
                    $_.Method -eq 'PATCH' -and $_.Uri -match '/repos/test/Pure-Base/releases/42$' -and
                    $null -ne (($_.Body | ConvertFrom-Json).PSObject.Properties['body'])
                }).Count | Should -Be 0
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'rejects a <Condition> validated artifact during <ReleaseState> before tag, release, or dispatch mutation' -ForEach @(
        @{ Condition = 'malformed manifest'; ValidatedArtifactState = 'malformed-manifest'; ReleaseState = 'fresh' },
        @{ Condition = 'malformed manifest'; ValidatedArtifactState = 'malformed-manifest'; ReleaseState = 'draft-resume' },
        @{ Condition = 'malformed manifest'; ValidatedArtifactState = 'malformed-manifest'; ReleaseState = 'published-resume' },
        @{ Condition = 'manifest hash mismatch'; ValidatedArtifactState = 'manifest-hash-mismatch'; ReleaseState = 'fresh' },
        @{ Condition = 'manifest hash mismatch'; ValidatedArtifactState = 'manifest-hash-mismatch'; ReleaseState = 'draft-resume' },
        @{ Condition = 'manifest hash mismatch'; ValidatedArtifactState = 'manifest-hash-mismatch'; ReleaseState = 'published-resume' },
        @{ Condition = 'missing package payload'; ValidatedArtifactState = 'missing-package-manifest'; ReleaseState = 'fresh' },
        @{ Condition = 'missing package payload'; ValidatedArtifactState = 'missing-package-manifest'; ReleaseState = 'draft-resume' },
        @{ Condition = 'missing package payload'; ValidatedArtifactState = 'missing-package-manifest'; ReleaseState = 'published-resume' },
        @{ Condition = 'duplicate package payload'; ValidatedArtifactState = 'duplicate-package-manifest'; ReleaseState = 'fresh' },
        @{ Condition = 'duplicate package payload'; ValidatedArtifactState = 'duplicate-package-manifest'; ReleaseState = 'draft-resume' },
        @{ Condition = 'duplicate package payload'; ValidatedArtifactState = 'duplicate-package-manifest'; ReleaseState = 'published-resume' }
    ) {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState $ReleaseState -DraftAssetState 'matching' -ValidatedArtifactState $ValidatedArtifactState
        try {
            $fixture.Failure | Should -Not -BeNullOrEmpty
            $fixture.Failure.Exception.Message | Should -Match 'validation|manifest|artifact|archive'
            (& git -C $fixture.RemoteRoot rev-parse refs/heads/master).Trim() | Should -Be $fixture.InitialBranchSha
            $fixture.MutationBoundaries.Count | Should -Be 0
            @($fixture.ApiCalls | Where-Object { $_.Method -in @('POST', 'PATCH', 'DELETE') -and $_.Uri -match '/releases|/assets|/dispatches' }).Count | Should -Be 0
            $fixture.DispatchPayloads.Count | Should -Be 0
            $releaseState = Get-ValidatedArtifactReleaseState -Release $fixture.Release -AssetName $fixture.AssetName
            $releaseState.Exists | Should -Be $fixture.InitialReleaseState.Exists
            $releaseState.TagName | Should -Be $fixture.InitialReleaseState.TagName
            $releaseState.TargetCommitish | Should -Be $fixture.InitialReleaseState.TargetCommitish
            $releaseState.Draft | Should -Be $fixture.InitialReleaseState.Draft
            $releaseState.Immutable | Should -Be $fixture.InitialReleaseState.Immutable
            $releaseState.Body | Should -Be $fixture.InitialReleaseState.Body
            $releaseState.ExpectedAssetDigest | Should -Be $fixture.InitialReleaseState.ExpectedAssetDigest
            @($releaseState.Assets | ForEach-Object { "$($_.Name)|$($_.Digest)" }) | Should -Be @($fixture.InitialReleaseState.Assets | ForEach-Object { "$($_.Name)|$($_.Digest)" })
            if ($ReleaseState -eq 'fresh') {
                (& git -C $fixture.RemoteRoot show-ref --verify --quiet "refs/tags/$($fixture.Version)")
                $LASTEXITCODE | Should -Not -Be 0
            }
            else {
                (& git -C $fixture.RemoteRoot rev-parse "refs/tags/$($fixture.Version)^{commit}").Trim() | Should -Be $fixture.EventSha
            }
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'uploads the downloaded validated ZIP exactly once when a draft asset is missing' {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState 'draft-resume' -DraftAssetState 'absent'
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            $uploads = @($fixture.ApiCalls | Where-Object { $_.Method -eq 'POST' -and $_.Uri -match '/assets\?name=' })
            $uploads.Count | Should -Be 1
            $uploads[0].InFile | Should -Match ([regex]::Escape($fixture.ReleaseArtifactDirectory))
            $uploads[0].InFile | Should -Not -Be $fixture.ZipPath
            (Get-FileHash -LiteralPath $uploads[0].InFile -Algorithm SHA256).Hash.ToLowerInvariant() | Should -Be $fixture.ZipSha256
            $uploads[0].Uri | Should -Be "https://uploads.example.invalid/releases/42/assets?name=$($fixture.AssetName)"
            $uploads[0].StateBefore.ExpectedAssetDigest | Should -BeNullOrEmpty
            $uploads[0].StateAfter.ExpectedAssetDigest | Should -Be "sha256:$($fixture.ZipSha256)"
            $fixture.Release.assets.Count | Should -Be 1
            $fixture.Release.assets[0].name | Should -Be $fixture.AssetName
            $fixture.Release.assets[0].digest | Should -Be "sha256:$($fixture.ZipSha256)"
            $fixture.MutationBoundaries | Should -Contain 'asset-upload'
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'reuses exactly one matching draft asset without delete or replacement' {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState 'draft-resume' -DraftAssetState 'matching'
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            @($fixture.ApiCalls | Where-Object { $_.Method -eq 'DELETE' -or ($_.Method -eq 'POST' -and $_.Uri -match '/assets\?name=') }).Count | Should -Be 0
            $fixture.Release.assets.Count | Should -Be 1
            $fixture.Release.assets[0].name | Should -Be $fixture.AssetName
            $fixture.Release.assets[0].digest | Should -Be "sha256:$($fixture.ZipSha256)"
            $fixture.MutationBoundaries | Should -Not -Contain 'asset-upload'
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'rejects <AssetState> draft assets without deleting or replacing them' -ForEach @(
        @{ AssetState = 'mismatched'; ExpectedAssetCount = 1 },
        @{ AssetState = 'duplicate'; ExpectedAssetCount = 2 }
    ) {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState 'draft-resume' -DraftAssetState $AssetState
        try {
            $fixture.Failure | Should -Not -BeNullOrEmpty
            $fixture.Failure.Exception.Message | Should -Match 'release asset'
            @($fixture.ApiCalls | Where-Object { $_.Method -eq 'DELETE' -or ($_.Method -eq 'POST' -and $_.Uri -match '/assets\?name=') -or ($_.Method -eq 'PATCH' -and $_.Uri -match '/releases/42$') }).Count | Should -Be 0
            $fixture.Release.draft | Should -BeTrue
            $fixture.Release.immutable | Should -BeFalse
            @($fixture.Release.assets | Where-Object name -eq $fixture.AssetName).Count | Should -Be $ExpectedAssetCount
            $fixture.MutationBoundaries | Should -Not -Contain 'publish'
            $fixture.MutationBoundaries | Should -Not -Contain 'vpm-dispatch'
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'dispatches a published resume only after immutable published asset verification and performs no release mutation' {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState 'published-resume' -DraftAssetState 'matching'
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            @($fixture.ApiCalls | Where-Object {
                    $_.Method -eq 'PATCH' -or $_.Method -eq 'DELETE' -or ($_.Method -eq 'POST' -and ($_.Uri -match '/releases$' -or $_.Uri -match '/assets\?name='))
                }).Count | Should -Be 0
            $dispatchIndex = [array]::FindIndex($fixture.ApiCalls, [Predicate[object]]{ param($call) $call.Method -eq 'POST' -and $call.Uri -match '/dispatches$' })
            $verifiedPublishedIndex = [array]::FindLastIndex($fixture.ApiCalls, [Predicate[object]]{ param($call) $call.Method -eq 'GET' -and $call.Uri -match '/releases/tags/' -and $call.StateBefore.Exists -and -not $call.StateBefore.Draft -and $call.StateBefore.Immutable -and $call.StateBefore.TagName -eq $fixture.Version -and $call.StateBefore.TargetCommitish -eq $fixture.EventSha -and $call.StateBefore.ExpectedAssetDigest -eq "sha256:$($fixture.ZipSha256)" })
            $verifiedPublishedIndex | Should -BeGreaterThan -1
            $dispatchIndex | Should -BeGreaterThan $verifiedPublishedIndex
            $fixture.DispatchPayloads.Count | Should -Be 1
            $fixture.Release.draft | Should -BeFalse
            $fixture.Release.immutable | Should -BeTrue
            $fixture.Release.target_commitish | Should -Be $fixture.EventSha
            $fixture.Release.assets[0].digest | Should -Be "sha256:$($fixture.ZipSha256)"
            $fixture.MutationBoundaries | Should -Be @('vpm-dispatch')
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'dispatches a legacy immutable published resume with a missing badge body without changing the release' {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState 'published-resume' -DraftBody 'legacy-missing-badge' -DraftAssetState 'matching'
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            $fixture.InitialReleaseState.Body | Should -Be 'legacy immutable release body'
            $fixture.Release.body | Should -Be $fixture.InitialReleaseState.Body
            ([regex]::Matches($fixture.Release.body, '\[!\[Downloads\]\(')).Count | Should -Be 0
            @($fixture.ApiCalls | Where-Object {
                    $_.Method -in @('POST', 'PATCH', 'DELETE') -and
                    ($_.Uri -match '/releases$' -or $_.Uri -match '/releases/42$' -or $_.Uri -match '/assets\?name=')
                }).Count | Should -Be 0
            $dispatchIndex = [array]::FindIndex($fixture.ApiCalls, [Predicate[object]]{ param($call) $call.Method -eq 'POST' -and $call.Uri -match '/dispatches$' })
            $verifiedPublishedIndex = [array]::FindLastIndex($fixture.ApiCalls, [Predicate[object]]{ param($call) $call.Method -eq 'GET' -and $call.Uri -match '/releases/tags/' -and $call.StateBefore.Exists -and -not $call.StateBefore.Draft -and $call.StateBefore.Immutable -and $call.StateBefore.TagName -eq $fixture.Version -and $call.StateBefore.TargetCommitish -eq $fixture.EventSha -and $call.StateBefore.ExpectedAssetDigest -eq "sha256:$($fixture.ZipSha256)" })
            $verifiedPublishedIndex | Should -BeGreaterThan -1
            $dispatchIndex | Should -BeGreaterThan $verifiedPublishedIndex
            $fixture.DispatchPayloads.Count | Should -Be 1
            $fixture.Release.tag_name | Should -Be $fixture.Version
            $fixture.Release.target_commitish | Should -Be $fixture.EventSha
            $fixture.Release.assets[0].digest | Should -Be "sha256:$($fixture.ZipSha256)"
            $fixture.MutationBoundaries | Should -Be @('vpm-dispatch')
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'rejects a published resume whose immutable asset digest differs from the validation artifact' {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState 'published-resume' -DraftAssetState 'mismatched'
        try {
            $fixture.Failure | Should -Not -BeNullOrEmpty
            $fixture.Failure.Exception.Message | Should -Match 'validation artifact|audited ZIP|digest'
            @($fixture.ApiCalls | Where-Object { $_.Method -ne 'GET' -and $_.Uri -match '/releases|/dispatches$' }).Count | Should -Be 0
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'verifies the uploaded validated asset digest before publishing' {
        $fixture = New-ValidatedArtifactReleaseFixture -ReleaseState 'draft-resume' -DraftAssetState 'absent'
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            $uploadIndex = [array]::FindIndex($fixture.ApiCalls, [Predicate[object]]{ param($call) $call.Method -eq 'POST' -and $call.Uri -match '/assets\?name=' })
            $publishIndex = [array]::FindIndex($fixture.ApiCalls, [Predicate[object]]{ param($call) $call.Method -eq 'PATCH' -and $call.Uri -match '/releases/42$' -and $call.Body -match '"draft":false' })
            $verificationIndex = [array]::FindIndex($fixture.ApiCalls, $uploadIndex + 1, $publishIndex - $uploadIndex - 1, [Predicate[object]]{ param($call) $call.Method -eq 'GET' -and $call.Uri -match '/releases/tags/' -and $call.AssetDigest -eq "sha256:$($fixture.ZipSha256)" })
            $uploadIndex | Should -BeGreaterThan -1
            $verificationIndex | Should -BeGreaterThan $uploadIndex
            $publishIndex | Should -BeGreaterThan $verificationIndex
            $verificationCall = $fixture.ApiCalls[$verificationIndex]
            $verificationCall.StateBefore.Exists | Should -BeTrue
            $verificationCall.StateBefore.Draft | Should -BeTrue
            $verificationCall.StateBefore.Immutable | Should -BeFalse
            $verificationCall.StateBefore.TagName | Should -Be $fixture.Version
            $verificationCall.StateBefore.TargetCommitish | Should -Be $fixture.EventSha
            $verificationCall.StateBefore.ExpectedAssetDigest | Should -Be "sha256:$($fixture.ZipSha256)"
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
    }

    It 'confirms the final published immutable release before dispatching VPM' {
        $fixture = New-ValidatedArtifactReleaseFixture
        try {
            $fixture.Failure | Should -BeNullOrEmpty
            $publishIndex = [array]::FindIndex($fixture.ApiCalls, [Predicate[object]]{ param($call) $call.Method -eq 'PATCH' -and $call.Uri -match '/releases/42$' -and $call.Body -match '"draft":false' })
            $dispatchIndex = [array]::FindIndex($fixture.ApiCalls, [Predicate[object]]{ param($call) $call.Method -eq 'POST' -and $call.Uri -match '/dispatches$' })
            $confirmationIndex = [array]::FindIndex($fixture.ApiCalls, $publishIndex + 1, $dispatchIndex - $publishIndex - 1, [Predicate[object]]{ param($call) $call.Method -eq 'GET' -and $call.Uri -match '/releases/tags/' -and -not $call.ReleaseDraft -and $call.ReleaseImmutable -and $call.AssetDigest -eq "sha256:$($fixture.ZipSha256)" })
            $publishIndex | Should -BeGreaterThan -1
            $confirmationIndex | Should -BeGreaterThan $publishIndex
            $dispatchIndex | Should -BeGreaterThan $confirmationIndex
            $confirmationCall = $fixture.ApiCalls[$confirmationIndex]
            $confirmationCall.StateBefore.Exists | Should -BeTrue
            $confirmationCall.StateBefore.Draft | Should -BeFalse
            $confirmationCall.StateBefore.Immutable | Should -BeTrue
            $confirmationCall.StateBefore.TagName | Should -Be $fixture.Version
            $confirmationCall.StateBefore.TargetCommitish | Should -Be $fixture.EventSha
            $confirmationCall.StateBefore.ExpectedAssetDigest | Should -Be "sha256:$($fixture.ZipSha256)"
            $fixture.DispatchPayloads.Count | Should -Be 1
        }
        finally { Remove-ValidatedArtifactReleaseFixture -Fixture $fixture }
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

Describe 'Exact-SHA validated promotion contracts' {
    BeforeAll {
        $headSha = 'a' * 40
        $version = '0.2.0-beta.1'
        $assetName = "jp.penguin.purebase-$version.zip"
        $annotatedTag = [pscustomobject]@{ Name = $version; Annotated = $true; PeeledCommitSha = $headSha }
        $draftRelease = [pscustomobject]@{ tag_name = $version; target_commitish = $headSha; draft = $true; prerelease = $true }
        $publishedRelease = [pscustomobject]@{ tag_name = $version; target_commitish = $headSha; draft = $false; prerelease = $true; immutable = $true }

        function New-ExpectedValidationManifest {
            return [ordered]@{
                schemaVersion = 1; repository = 'PenguinDOOM/Pure-Base'; headSha = $headSha; headBranch = 'master'
                workflowRunId = 11; workflowRunAttempt = 2; version = $version; assetName = $assetName; sha256 = ('c' * 64)
            }
        }
    }

    It 'accepts a fresh state with no existing tag or release' -Tag 'release-mode' {
        $fresh = Resolve-PureBaseReleaseMode `
            -PackageVersion $version `
            -ConfirmedVersion $version `
            -HeadSha $headSha
        $fresh.Mode | Should -Be 'fresh'
    }

    It 'accepts an exact annotated tag and draft release resume state' -Tag 'release-mode' {
        $resume = Resolve-PureBaseReleaseMode `
            -PackageVersion $version `
            -ConfirmedVersion $version `
            -HeadSha $headSha `
            -Resume `
            -ExistingTag $annotatedTag `
            -ExistingRelease $draftRelease
        $resume.Mode | Should -Be 'resume'
    }

    It 'accepts an exact annotated tag and published immutable release resume state' -Tag 'release-mode' {
        $resume = Resolve-PureBaseReleaseMode `
            -PackageVersion $version `
            -ConfirmedVersion $version `
            -HeadSha $headSha `
            -Resume `
            -ExistingTag $annotatedTag `
            -ExistingRelease $publishedRelease
        $resume.Mode | Should -Be 'resume'
    }

    It 'rejects package confirmation disagreement' -Tag 'release-mode' {
        { Resolve-PureBaseReleaseMode -PackageVersion '0.2.0' -ConfirmedVersion $version -HeadSha $headSha } | Should -Throw '*strict release state*'
    }

    It 'rejects a fresh release with an existing tag' -Tag 'release-mode' {
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -ExistingTag $annotatedTag } | Should -Throw '*strict release state*'
    }

    It 'rejects a fresh release with an existing draft release' -Tag 'release-mode' {
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -ExistingRelease $draftRelease } | Should -Throw '*strict release state*'
    }

    It 'rejects a tag-only resume state' -Tag 'release-mode' {
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -Resume -ExistingTag $annotatedTag } | Should -Throw '*strict release state*'
    }

    It 'rejects a release-only resume state' -Tag 'release-mode' {
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -Resume -ExistingRelease $draftRelease } | Should -Throw '*strict release state*'
    }

    It 'rejects a lightweight tag during resume' -Tag 'release-mode' {
        $tag = [pscustomobject]@{ Name = $version; Annotated = $false; PeeledCommitSha = $headSha }
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -Resume -ExistingTag $tag -ExistingRelease $draftRelease } | Should -Throw '*strict release state*'
    }

    It 'rejects a resume tag whose name does not equal the confirmed version' -Tag 'release-mode' {
        $tag = [pscustomobject]@{ Name = '0.2.0-beta.2'; Annotated = $true; PeeledCommitSha = $headSha }
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -Resume -ExistingTag $tag -ExistingRelease $draftRelease } | Should -Throw '*strict release state*'
    }

    It 'rejects a resume tag at another commit' -Tag 'release-mode' {
        $tag = [pscustomobject]@{ Name = $version; Annotated = $true; PeeledCommitSha = ('b' * 40) }
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -Resume -ExistingTag $tag -ExistingRelease $draftRelease } | Should -Throw '*strict release state*'
    }

    It 'rejects a release whose tag name does not equal the confirmed version' -Tag 'release-mode' {
        $release = [pscustomobject]@{ tag_name = '0.2.0-beta.2'; target_commitish = $headSha; draft = $true; prerelease = $true }
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -Resume -ExistingTag $annotatedTag -ExistingRelease $release } | Should -Throw '*strict release state*'
    }

    It 'rejects a release target that does not equal the selected commit' -Tag 'release-mode' {
        $release = [pscustomobject]@{ tag_name = $version; target_commitish = ('b' * 40); draft = $true; prerelease = $true }
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -Resume -ExistingTag $annotatedTag -ExistingRelease $release } | Should -Throw '*strict release state*'
    }

    It 'rejects a release with the wrong prerelease state' -Tag 'release-mode' {
        $release = [pscustomobject]@{ tag_name = $version; target_commitish = $headSha; draft = $true; prerelease = $false }
        { Resolve-PureBaseReleaseMode -PackageVersion $version -ConfirmedVersion $version -HeadSha $headSha -Resume -ExistingTag $annotatedTag -ExistingRelease $release } | Should -Throw '*strict release state*'
    }

    It 'selects only the latest exact validation run after combining paginated results' {
        $runs = @(
            [pscustomobject]@{ path = 'release-validation.yml'; head_sha = $headSha; head_branch = 'master'; event = 'workflow_dispatch'; run_number = 10; run_attempt = 1; status = 'completed'; conclusion = 'success' },
            [pscustomobject]@{ path = 'release-validation.yml'; head_sha = $headSha; head_branch = 'master'; event = 'workflow_dispatch'; run_number = 11; run_attempt = 2; status = 'completed'; conclusion = 'success' },
            [pscustomobject]@{ path = 'daily.yml'; head_sha = $headSha; head_branch = 'master'; event = 'workflow_dispatch'; run_number = 99; run_attempt = 1; status = 'completed'; conclusion = 'success' }
        )

        $selected = Select-PureBaseReleaseValidationRun -Runs $runs -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml'
        $selected.run_number | Should -Be 11
        $selected.run_attempt | Should -Be 2
    }

    It 'rejects a selector candidate with the wrong commit SHA' {
        $run = [pscustomobject]@{ path = 'release-validation.yml'; head_sha = ('b' * 40); head_branch = 'master'; event = 'workflow_dispatch'; run_number = 1; run_attempt = 1; status = 'completed'; conclusion = 'success' }
        { Select-PureBaseReleaseValidationRun -Runs @($run) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
    }

    It 'rejects a selector candidate from the wrong branch' {
        $run = [pscustomobject]@{ path = 'release-validation.yml'; head_sha = $headSha; head_branch = 'release'; event = 'workflow_dispatch'; run_number = 1; run_attempt = 1; status = 'completed'; conclusion = 'success' }
        { Select-PureBaseReleaseValidationRun -Runs @($run) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
    }

    It 'rejects a selector candidate from the wrong event' {
        $run = [pscustomobject]@{ path = 'release-validation.yml'; head_sha = $headSha; head_branch = 'master'; event = 'push'; run_number = 1; run_attempt = 1; status = 'completed'; conclusion = 'success' }
        { Select-PureBaseReleaseValidationRun -Runs @($run) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
    }

    It 'rejects a selector candidate with null commit metadata' {
        $run = [pscustomobject]@{ path = 'release-validation.yml'; head_sha = $null; head_branch = 'master'; event = 'workflow_dispatch'; run_number = 1; run_attempt = 1; status = 'completed'; conclusion = 'success' }
        { Select-PureBaseReleaseValidationRun -Runs @($run) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
    }

    It 'accepts a unique unexpired artifact for the selected run attempt' {
        $artifact = [pscustomobject]@{ id = 7; name = "pure-base-release-validation-11-2"; expired = $false; workflow_run = [pscustomobject]@{ id = 11; run_attempt = 2 } }
        $resolved = Resolve-PureBaseValidationArtifact -Artifacts @($artifact) -ExpectedName $artifact.name -WorkflowRunId 11 -WorkflowRunAttempt 2
        $resolved.id | Should -Be 7
    }

    It 'accepts a schema 1 manifest matching the selected run and archive' {
        $manifest = New-ExpectedValidationManifest
        Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64)
    }

    It 'rejects a manifest with an unsupported schema version' {
        $manifest = New-ExpectedValidationManifest; $manifest.schemaVersion = 2
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*schemaVersion*'
    }

    It 'rejects a manifest from another repository' {
        $manifest = New-ExpectedValidationManifest; $manifest.repository = 'other/repository'
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*repository*'
    }

    It 'rejects a manifest with another head SHA' {
        $manifest = New-ExpectedValidationManifest; $manifest.headSha = ('b' * 40)
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*head SHA*'
    }

    It 'rejects a manifest from another head branch' {
        $manifest = New-ExpectedValidationManifest; $manifest.headBranch = 'release'
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*head branch*'
    }

    It 'rejects a manifest with another workflow run ID' {
        $manifest = New-ExpectedValidationManifest; $manifest.workflowRunId = 12
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*workflow run ID*'
    }

    It 'rejects a manifest with another workflow run attempt' {
        $manifest = New-ExpectedValidationManifest; $manifest.workflowRunAttempt = 1
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*workflow run attempt*'
    }

    It 'rejects a manifest with another package version' {
        $manifest = New-ExpectedValidationManifest; $manifest.version = '0.2.0-beta.2'
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*version*'
    }

    It 'rejects a manifest with another asset name' {
        $manifest = New-ExpectedValidationManifest; $manifest.assetName = 'other.zip'
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*assetName*'
    }

    It 'rejects a manifest with another archive SHA-256' {
        $manifest = New-ExpectedValidationManifest; $manifest.sha256 = ('d' * 64)
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*SHA-256*'
    }

    It 'rejects duplicate artifacts for the selected run attempt' {
        $artifacts = @([pscustomobject]@{ id = 1; name = 'expected'; expired = $false; workflow_run = [pscustomobject]@{ id = 11; run_attempt = 2 } }, [pscustomobject]@{ id = 2; name = 'expected'; expired = $false; workflow_run = [pscustomobject]@{ id = 11; run_attempt = 2 } })
        { Resolve-PureBaseValidationArtifact -Artifacts $artifacts -ExpectedName 'expected' -WorkflowRunId 11 -WorkflowRunAttempt 2 } | Should -Throw '*validation artifact*'
    }

    It 'rejects an expired artifact for the selected run attempt' {
        $artifact = [pscustomobject]@{ id = 1; name = 'expected'; expired = $true; workflow_run = [pscustomobject]@{ id = 11; run_attempt = 2 } }
        { Resolve-PureBaseValidationArtifact -Artifacts @($artifact) -ExpectedName 'expected' -WorkflowRunId 11 -WorkflowRunAttempt 2 } | Should -Throw '*validation artifact*'
    }

    It 'renders one leading URL-encoded asset-specific badge for draft release notes' {
        $body = New-PureBaseReleaseBody -Repository 'owner/repository name' -Version $version -AssetName 'asset name.zip' -GeneratedNotesBody 'Generated notes'
        $body | Should -Match '^\[!\[Downloads\]\(https://img\.shields\.io/github/downloads/owner/repository%20name/0\.2\.0-beta\.1/asset%20name\.zip\?label=downloads\)\]'
    }

    It 'preserves generated release notes after the download badge' {
        $body = New-PureBaseReleaseBody -Repository 'owner/repository name' -Version $version -AssetName 'asset name.zip' -GeneratedNotesBody 'Generated notes'
        $body | Should -Match 'Generated notes$'
    }

    It 'emits one leading URL-encoded badge and requests generated release notes' {
        $body = New-PureBaseReleaseBody -Repository 'owner/repository name' -Version $version -AssetName 'asset name.zip' -GeneratedNotesBody 'Generated notes'
        ([regex]::Matches($body, '\[!\[Downloads\]\(')).Count | Should -Be 1
        $body | Should -Match '^\[!\[Downloads\]\(https://img\.shields\.io/github/downloads/owner/repository%20name/0\.2\.0-beta\.1/asset%20name\.zip\?label=downloads\)\]'
        $releaseScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') -Raw
        $releaseScript | Should -Match 'generate_release_notes = \$true'
    }

}

Describe 'Exact-SHA validation failure matrix' {
    BeforeAll {
        $headSha = 'a' * 40
        $version = '0.2.0-beta.1'
        $assetName = "jp.penguin.purebase-$version.zip"

        function New-ExactValidationRun {
            return [pscustomobject]@{ path = 'release-validation.yml'; head_sha = $headSha; head_branch = 'master'; event = 'workflow_dispatch'; run_number = 11; run_attempt = 2; status = 'completed'; conclusion = 'success' }
        }

        function New-ExactValidationArtifact {
            return [pscustomobject]@{ id = 7; name = 'pure-base-release-validation-11-2'; expired = $false; workflow_run = [pscustomobject]@{ id = 11; run_attempt = 2 } }
        }

        function New-ExactValidationManifest {
            return [ordered]@{
                schemaVersion = 1; repository = 'PenguinDOOM/Pure-Base'; headSha = $headSha; headBranch = 'master'
                workflowRunId = 11; workflowRunAttempt = 2; version = $version; assetName = $assetName; sha256 = ('c' * 64)
            }
        }
    }

    It 'rejects the latest matching validation run when it is <State>' -ForEach @(
        @{ State = 'failure'; Status = 'completed'; Conclusion = 'failure' },
        @{ State = 'cancelled'; Status = 'completed'; Conclusion = 'cancelled' },
        @{ State = 'skipped'; Status = 'completed'; Conclusion = 'skipped' },
        @{ State = 'in-progress'; Status = 'in_progress'; Conclusion = $null },
        @{ State = 'queued'; Status = 'queued'; Conclusion = $null }
    ) {
        $run = New-ExactValidationRun
        $run.status = $Status
        $run.conclusion = $Conclusion
        { Select-PureBaseReleaseValidationRun -Runs @($run) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
    }

    It 'rejects a newer exact matching <State> run instead of selecting the older successful run' -ForEach @(
        @{ State = 'failure'; Status = 'completed'; Conclusion = 'failure' },
        @{ State = 'cancelled'; Status = 'completed'; Conclusion = 'cancelled' },
        @{ State = 'skipped'; Status = 'completed'; Conclusion = 'skipped' },
        @{ State = 'in-progress'; Status = 'in_progress'; Conclusion = $null },
        @{ State = 'queued'; Status = 'queued'; Conclusion = $null }
    ) {
        $olderSuccess = New-ExactValidationRun
        $olderSuccess.run_number = 10
        $olderSuccess.run_attempt = 1
        $newerRun = New-ExactValidationRun
        $newerRun.status = $Status
        $newerRun.conclusion = $Conclusion

        { Select-PureBaseReleaseValidationRun -Runs @($olderSuccess, $newerRun) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
    }

    It 'rejects matching attempt 2 in <State> instead of stale attempt 1 success across reversed pages' -ForEach @(
        @{ State = 'failure'; Status = 'completed'; Conclusion = 'failure' },
        @{ State = 'cancelled'; Status = 'completed'; Conclusion = 'cancelled' },
        @{ State = 'skipped'; Status = 'completed'; Conclusion = 'skipped' },
        @{ State = 'in-progress'; Status = 'in_progress'; Conclusion = $null },
        @{ State = 'queued'; Status = 'queued'; Conclusion = $null }
    ) {
        $attemptOne = New-ExactValidationRun
        $attemptOne.run_attempt = 1
        $attemptTwo = New-ExactValidationRun
        $attemptTwo.status = $Status
        $attemptTwo.conclusion = $Conclusion
        $pageOrders = @(
            [pscustomobject]@{ FirstPage = @($attemptOne); SecondPage = @($attemptTwo) },
            [pscustomobject]@{ FirstPage = @($attemptTwo); SecondPage = @($attemptOne) }
        )

        foreach ($pageOrder in $pageOrders) {
            $runs = @($pageOrder.FirstPage + $pageOrder.SecondPage)
            { Select-PureBaseReleaseValidationRun -Runs $runs -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
        }
    }

    It 'selects matching successful attempt 2 over attempt 1 across reversed pages' {
        $attemptOne = New-ExactValidationRun
        $attemptOne.run_attempt = 1
        $attemptTwo = New-ExactValidationRun
        $pageOrders = @(
            [pscustomobject]@{ FirstPage = @($attemptOne); SecondPage = @($attemptTwo) },
            [pscustomobject]@{ FirstPage = @($attemptTwo); SecondPage = @($attemptOne) }
        )

        foreach ($pageOrder in $pageOrders) {
            $runs = @($pageOrder.FirstPage + $pageOrder.SecondPage)
            $selected = Select-PureBaseReleaseValidationRun -Runs $runs -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml'
            $selected.run_number | Should -Be 11
            $selected.run_attempt | Should -Be 2
        }
    }

    It 'rejects the latest matching validation run with a wrong workflow path' {
        $run = New-ExactValidationRun
        $run.path = 'daily.yml'
        { Select-PureBaseReleaseValidationRun -Runs @($run) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
    }

    It 'rejects the latest matching validation run with <Field> metadata' -ForEach @(
        @{ Field = 'null branch'; Property = 'head_branch'; Value = $null },
        @{ Field = 'null event'; Property = 'event'; Value = $null },
        @{ Field = 'invalid run number'; Property = 'run_number'; Value = 0 },
        @{ Field = 'invalid run attempt'; Property = 'run_attempt'; Value = 0 }
    ) {
        $run = New-ExactValidationRun
        $run.($Property) = $Value
        { Select-PureBaseReleaseValidationRun -Runs @($run) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml' } | Should -Throw '*matching validation run*'
    }

    It 'selects the latest exact validation run after combining separate pagination results' {
        $firstPage = @(New-ExactValidationRun)
        $firstPage[0].run_number = 10
        $firstPage[0].run_attempt = 1
        $secondPage = @(New-ExactValidationRun)
        $selected = Select-PureBaseReleaseValidationRun -Runs @($firstPage + $secondPage) -HeadSha $headSha -Branch 'master' -WorkflowPath 'release-validation.yml'
        $selected.run_number | Should -Be 11
        $selected.run_attempt | Should -Be 2
    }

    It 'rejects a validation artifact with <Condition>' -ForEach @(
        @{ Condition = 'no matching artifact'; Mutate = { param($artifact) @() } },
        @{ Condition = 'a wrong artifact name'; Mutate = { param($artifact) $artifact.name = 'other'; @($artifact) } },
        @{ Condition = 'another workflow run'; Mutate = { param($artifact) $artifact.workflow_run.id = 12; @($artifact) } },
        @{ Condition = 'another workflow run attempt'; Mutate = { param($artifact) $artifact.workflow_run.run_attempt = 1; @($artifact) } },
        @{ Condition = 'an expired artifact'; Mutate = { param($artifact) $artifact.expired = $true; @($artifact) } },
        @{ Condition = 'duplicate matching artifacts'; Mutate = { param($artifact) @($artifact, (New-ExactValidationArtifact)) } }
    ) {
        $artifact = New-ExactValidationArtifact
        $artifacts = & $Mutate $artifact
        { Resolve-PureBaseValidationArtifact -Artifacts $artifacts -ExpectedName 'pure-base-release-validation-11-2' -WorkflowRunId 11 -WorkflowRunAttempt 2 } | Should -Throw '*validation artifact*'
    }

    It 'rejects validation payload files with exactly one <Condition>' -ForEach @(
        @{ Condition = 'ZIP missing'; Files = @('release-validation.json', 'jp.penguin.purebase-0.2.0-beta.1.zip.sha256') },
        @{ Condition = 'ZIP duplicated'; Files = @('release-validation.json', 'jp.penguin.purebase-0.2.0-beta.1.zip', 'jp.penguin.purebase-0.2.0-beta.1.zip', 'jp.penguin.purebase-0.2.0-beta.1.zip.sha256') },
        @{ Condition = 'sidecar missing'; Files = @('release-validation.json', 'jp.penguin.purebase-0.2.0-beta.1.zip') },
        @{ Condition = 'sidecar duplicated'; Files = @('release-validation.json', 'jp.penguin.purebase-0.2.0-beta.1.zip', 'jp.penguin.purebase-0.2.0-beta.1.zip.sha256', 'jp.penguin.purebase-0.2.0-beta.1.zip.sha256') },
        @{ Condition = 'manifest missing'; Files = @('jp.penguin.purebase-0.2.0-beta.1.zip', 'jp.penguin.purebase-0.2.0-beta.1.zip.sha256') },
        @{ Condition = 'manifest duplicated'; Files = @('release-validation.json', 'release-validation.json', 'jp.penguin.purebase-0.2.0-beta.1.zip', 'jp.penguin.purebase-0.2.0-beta.1.zip.sha256') }
    ) {
        { Resolve-PureBaseValidationPayloadFiles -Files $Files -AssetName $assetName } | Should -Throw '*validation payload*'
    }

    It 'rejects a validation manifest with <Condition>' -ForEach @(
        @{ Condition = 'a null repository'; Property = 'repository'; Value = $null },
        @{ Condition = 'a null head SHA'; Property = 'headSha'; Value = $null },
        @{ Condition = 'a null head branch'; Property = 'headBranch'; Value = $null },
        @{ Condition = 'a null workflow run ID'; Property = 'workflowRunId'; Value = $null },
        @{ Condition = 'a null workflow run attempt'; Property = 'workflowRunAttempt'; Value = $null },
        @{ Condition = 'a null version'; Property = 'version'; Value = $null },
        @{ Condition = 'a null asset name'; Property = 'assetName'; Value = $null },
        @{ Condition = 'a null archive hash'; Property = 'sha256'; Value = $null }
    ) {
        $manifest = New-ExactValidationManifest
        $manifest[$Property] = $Value
        { Assert-PureBaseValidationManifest -Manifest $manifest -Repository 'PenguinDOOM/Pure-Base' -HeadSha $headSha -HeadBranch 'master' -WorkflowRunId 11 -WorkflowRunAttempt 2 -Version $version -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw
    }

    It 'reuses one matching draft asset and uploads only when the draft asset is absent' -ForEach @(
        @{ AssetState = 'absent'; Assets = @(); ExpectedAction = 'upload' },
        @{ AssetState = 'matching'; Assets = @([pscustomobject]@{ name = $assetName; digest = 'sha256:' + ('c' * 64) }); ExpectedAction = 'reuse' }
    ) {
        $action = Resolve-PureBaseDraftAssetAction -Assets $Assets -AssetName $assetName -Sha256 ('c' * 64)
        $action | Should -Be $ExpectedAction
    }

    It 'rejects a draft asset with <Condition>' -ForEach @(
        @{ Condition = 'a mismatched digest'; Assets = @([pscustomobject]@{ name = $assetName; digest = 'sha256:' + ('d' * 64) }) },
        @{ Condition = 'duplicate matching names'; Assets = @([pscustomobject]@{ name = $assetName; digest = 'sha256:' + ('c' * 64) }, [pscustomobject]@{ name = $assetName; digest = 'sha256:' + ('c' * 64) }) }
    ) {
        { Resolve-PureBaseDraftAssetAction -Assets $Assets -AssetName $assetName -Sha256 ('c' * 64) } | Should -Throw '*release asset*'
    }

    It 'accepts a published resume asset only when its digest matches the validation artifact' {
        $release = [pscustomobject]@{ assets = @([pscustomobject]@{ name = $assetName; digest = 'sha256:' + ('c' * 64) }) }
        Assert-PureBasePublishedResumeArtifact -Release $release -AssetName $assetName -ValidationArtifactSha256 ('c' * 64)
    }

    It 'rejects a published resume asset whose digest differs from the validation artifact' {
        $release = [pscustomobject]@{ assets = @([pscustomobject]@{ name = $assetName; digest = 'sha256:' + ('d' * 64) }) }
        { Assert-PureBasePublishedResumeArtifact -Release $release -AssetName $assetName -ValidationArtifactSha256 ('c' * 64) } | Should -Throw '*validation artifact*'
    }
}

Describe 'Validated artifact archive and mutation gate contracts' {
    BeforeAll {
        function New-ValidatedArchiveFixture {
            param(
                [Parameter(Mandatory = $true)][string]$Root,
                [Parameter(Mandatory = $true)][string]$PackageVersion,
                [string]$PackageName = 'jp.penguin.purebase',
                [string[]]$PackageEntryNames = @('package.json')
            )

            New-Item -ItemType Directory -Path $Root -Force | Out-Null
            $zipPath = Join-Path $Root 'jp.penguin.purebase-0.2.0.zip'
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
            try {
                foreach ($entryName in $PackageEntryNames) {
                    $entry = $archive.CreateEntry($entryName)
                    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
                    try { $writer.Write("{`"name`":`"$PackageName`",`"version`":`"$PackageVersion`"}") }
                    finally { $writer.Dispose() }
                }
            }
            finally { $archive.Dispose() }

            $sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
            [IO.File]::WriteAllText($zipPath + '.sha256', $sha256 + "`n", [Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText((Join-Path $Root 'release-validation.json'), '{"schemaVersion":1,"version":"0.2.0"}' + "`n", [Text.UTF8Encoding]::new($false))
            return [pscustomobject]@{ ZipPath = $zipPath; Sha256 = $sha256 }
        }
    }

    It 'rejects a missing archive redirect location' {
        { Assert-PureBaseArtifactRedirectLocation -Location '' } | Should -Throw '*absolute HTTPS redirect*'
    }

    It 'rejects an invalid archive redirect location' {
        { Assert-PureBaseArtifactRedirectLocation -Location 'https://[invalid' } | Should -Throw '*absolute HTTPS redirect*'
    }

    It 'rejects a relative archive redirect location' {
        { Assert-PureBaseArtifactRedirectLocation -Location '/archive.zip' } | Should -Throw '*absolute HTTPS redirect*'
    }

    It 'rejects an archive redirect location with userinfo' {
        { Assert-PureBaseArtifactRedirectLocation -Location 'https://token@example.invalid/archive.zip' } | Should -Throw '*absolute HTTPS redirect*'
    }

    It 'rejects a non-HTTPS archive redirect location' {
        { Assert-PureBaseArtifactRedirectLocation -Location 'http://objects.example.invalid/archive.zip' } | Should -Throw '*absolute HTTPS redirect*'
    }

    It 'downloads an authenticated 302 HTTPS redirect without forwarding authorization' {
        $destinationPath = Join-Path $TestDrive 'archive.zip'
        $requests = [Collections.Generic.List[object]]::new()
        $requestInvoker = {
            param($Method, $Uri, $Headers, $OutFile, $MaximumRedirection)
            $requests.Add([pscustomobject]@{ Method = $Method; Uri = $Uri; Headers = $Headers; OutFile = $OutFile; MaximumRedirection = $MaximumRedirection }) | Out-Null
            if ($Uri -eq 'https://api.github.com/actions/artifacts/7/zip') {
                return [pscustomobject]@{ StatusCode = 302; Headers = @{ Location = 'https://objects.example.invalid/archive.zip' } }
            }
            [IO.File]::WriteAllBytes($OutFile, [byte[]](1, 2, 3))
            return [pscustomobject]@{ StatusCode = 200; Headers = @{} }
        }.GetNewClosure()

        Invoke-PureBaseArtifactArchiveDownload -ArchiveUri 'https://api.github.com/actions/artifacts/7/zip' -Token 'release-token' -DestinationPath $destinationPath -RequestInvoker $requestInvoker

        $requests.Count | Should -Be 2
        $requests[0].Headers.Authorization | Should -Be 'Bearer release-token'
        $requests[0].MaximumRedirection | Should -Be 0
        $requests[1].Uri | Should -Be 'https://objects.example.invalid/archive.zip'
        $requests[1].Headers.ContainsKey('Authorization') | Should -BeFalse
        $requests[1].MaximumRedirection | Should -Be 0
        Test-Path -LiteralPath $destinationPath | Should -BeTrue
    }

    It 'rejects a 410 archive response without following another request' {
        $requests = [Collections.Generic.List[object]]::new()
        $requestInvoker = { param($Method, $Uri, $Headers, $OutFile, $MaximumRedirection) $requests.Add($Uri) | Out-Null; [pscustomobject]@{ StatusCode = 410; Headers = @{} } }.GetNewClosure()

        { Invoke-PureBaseArtifactArchiveDownload -ArchiveUri 'https://api.github.com/actions/artifacts/7/zip' -Token 'release-token' -DestinationPath (Join-Path $TestDrive 'gone.zip') -RequestInvoker $requestInvoker } | Should -Throw '*410*'
        $requests.Count | Should -Be 1
    }

    It 'rejects an archive redirect chain that exceeds the configured limit' {
        $requests = [Collections.Generic.List[object]]::new()
        $requestInvoker = {
            param($Method, $Uri, $Headers, $OutFile, $MaximumRedirection)
            $requests.Add([pscustomobject]@{ Uri = $Uri; Headers = $Headers; MaximumRedirection = $MaximumRedirection }) | Out-Null
            return [pscustomobject]@{ StatusCode = 302; Headers = @{ Location = "https://objects.example.invalid/archive-$($requests.Count).zip" } }
        }.GetNewClosure()

        { Invoke-PureBaseArtifactArchiveDownload -ArchiveUri 'https://api.github.com/actions/artifacts/7/zip' -Token 'release-token' -DestinationPath (Join-Path $TestDrive 'redirect-limit.zip') -RequestInvoker $requestInvoker -MaximumRedirects 2 } | Should -Throw '*redirect limit*'
        $requests.Count | Should -Be 3
        $requests | ForEach-Object { $_.Headers.ContainsKey('Authorization') | Should -Be ($_ -eq $requests[0]) }
    }

    It 'accepts a validated archive with one root package manifest and a lowercase sidecar' {
        $fixture = New-ValidatedArchiveFixture -Root (Join-Path $TestDrive 'valid') -PackageVersion '0.2.0'
        Assert-PureBaseValidatedArchive -ValidatedPackageDirectory (Split-Path -Parent $fixture.ZipPath) -AssetName 'jp.penguin.purebase-0.2.0.zip' -ExpectedSha256 $fixture.Sha256 -Version '0.2.0'
    }

    It 'rejects a ZIP with <Condition>' -ForEach @(
        @{ Condition = 'no package manifest'; PackageEntryNames = @() },
        @{ Condition = 'duplicate root package manifests'; PackageEntryNames = @('package.json', 'package.json') },
        @{ Condition = 'a nested package manifest instead of a root manifest'; PackageEntryNames = @('nested/package.json') },
        @{ Condition = 'a wrong root package name'; PackageEntryNames = @('package.json'); PackageName = 'jp.penguin.other' }
    ) {
        $parameters = @{ Root = Join-Path $TestDrive ($Condition -replace '[^A-Za-z0-9]+', '-'); PackageVersion = '0.2.0'; PackageEntryNames = $PackageEntryNames }
        if ($PSBoundParameters.ContainsKey('PackageName')) { $parameters.PackageName = $PackageName }
        $fixture = New-ValidatedArchiveFixture @parameters
        { Assert-PureBaseValidatedArchive -ValidatedPackageDirectory (Split-Path -Parent $fixture.ZipPath) -AssetName 'jp.penguin.purebase-0.2.0.zip' -ExpectedSha256 $fixture.Sha256 -Version '0.2.0' } | Should -Throw '*validated archive*'
    }

    It 'rejects an uppercase ZIP SHA-256 sidecar while all archive metadata is valid' {
        $fixture = New-ValidatedArchiveFixture -Root (Join-Path $TestDrive 'uppercase-sidecar') -PackageVersion '0.2.0'
        [IO.File]::WriteAllText($fixture.ZipPath + '.sha256', $fixture.Sha256.ToUpperInvariant() + "`n", [Text.UTF8Encoding]::new($false))
        { Assert-PureBaseValidatedArchive -ValidatedPackageDirectory (Split-Path -Parent $fixture.ZipPath) -AssetName 'jp.penguin.purebase-0.2.0.zip' -ExpectedSha256 $fixture.Sha256 -Version '0.2.0' } | Should -Throw '*validated archive*'
    }

    It 'rejects a malformed ZIP SHA-256 sidecar while all archive metadata is valid' {
        $fixture = New-ValidatedArchiveFixture -Root (Join-Path $TestDrive 'malformed-sidecar') -PackageVersion '0.2.0'
        [IO.File]::WriteAllText($fixture.ZipPath + '.sha256', 'not-a-sha256' + "`n", [Text.UTF8Encoding]::new($false))
        { Assert-PureBaseValidatedArchive -ValidatedPackageDirectory (Split-Path -Parent $fixture.ZipPath) -AssetName 'jp.penguin.purebase-0.2.0.zip' -ExpectedSha256 $fixture.Sha256 -Version '0.2.0' } | Should -Throw '*validated archive*'
    }

    It 'rejects a ZIP whose payload hash differs from the expected manifest hash' {
        $fixture = New-ValidatedArchiveFixture -Root (Join-Path $TestDrive 'payload-hash') -PackageVersion '0.2.0'
        { Assert-PureBaseValidatedArchive -ValidatedPackageDirectory (Split-Path -Parent $fixture.ZipPath) -AssetName 'jp.penguin.purebase-0.2.0.zip' -ExpectedSha256 ('a' * 64) -Version '0.2.0' } | Should -Throw '*validated archive*'
    }

    It 'rejects a ZIP whose package identity has another version' {
        $fixture = New-ValidatedArchiveFixture -Root (Join-Path $TestDrive 'package-version') -PackageVersion '0.2.1'
        { Assert-PureBaseValidatedArchive -ValidatedPackageDirectory (Split-Path -Parent $fixture.ZipPath) -AssetName 'jp.penguin.purebase-0.2.0.zip' -ExpectedSha256 $fixture.Sha256 -Version '0.2.0' } | Should -Throw '*validated archive*'
    }

}
