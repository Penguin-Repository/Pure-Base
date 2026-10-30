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

# Checks release producer provenance, evidence paths, and publishing boundaries.
Describe 'Hosted Release review contracts' {
    BeforeAll {
        . (Join-Path $PSScriptRoot 'HostedUnityReviewContracts.Helpers.ps1')
    }

    It 'passes a real Unity.exe only to audited release validation' -Tag ReleaseValidationProducer {
        $releaseWorkflow.Contains('-RealEditorPathOutputFile $realEditorPathFile') | Should -BeTrue
        $releaseWorkflow.Contains('REAL_UNITY_EDITOR_PATH=$realEditorPath') | Should -BeTrue
        $releaseWorkflow.Contains('-UnityEditorPath $env:REAL_UNITY_EDITOR_PATH') | Should -BeTrue
        $releaseWorkflow.Contains('& $env:UNITY_EDITOR_PATH `') | Should -BeTrue
        $resolverScript.Contains('RealEditorPathOutputFile') | Should -BeTrue
        $resolverScript.Contains('real Unity.exe because its audited runner intentionally rejects wrapper executables') | Should -BeTrue
    }

    It 'provisions prerequisites before Unity and only uploads the safe report' {
        $job = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $paths = Get-NamedStepBlock -Job $job -Name 'Export Unity editor paths'
        $provision = Get-NamedStepBlock -Job $job -Name 'Provision Unity runtime prerequisites'
        $upload = Get-NamedStepBlock -Job $job -Name 'Upload release validation evidence'
        $export = Get-NamedStepBlock -Job $job -Name 'Export versioned validation ZIP'

        Assert-LinesInOrder -Block $job -ExpectedLines @(
            '      - name: Capture initial repository state',
            '      - name: Provision Unity runtime prerequisites',
            '      - name: Install verified Shader-Core 0.3.0 release',
            '      - name: Restore Unity 2022.3.28f1',
            '      - name: Activate Unity Personal license',
            '      - name: Export Unity editor paths'
        )
        $provision | Should -Match '(?m)^        timeout-minutes: 15$'
        $provision | Should -Match 'Install-ReleaseUnityRuntimePrerequisites\.ps1'
        $provision | Should -Match '-WorkflowRunId ''\$\{\{ github\.run_id \}\}'''
        $provision | Should -Match '-WorkflowRunAttempt ''\$\{\{ github\.run_attempt \}\}'''
        $provision | Should -Match '-WorkflowJob ''\$\{\{ github\.job \}\}'''
        $provision | Should -Match '-IntendedSha ''\$\{\{ github\.sha \}\}'''
        $provision | Should -Match '-CheckoutSha \$checkoutSha'
        $provision | Should -Match 'repository-state\.json'
        $paths | Should -Match '(?m)^            -RequireExistingRuntime$'
        $paths | Should -Not -Match 'RuntimeEvidence|WorkflowRunId|WorkflowRunAttempt|IntendedSha|CheckoutSha'
        $dailyWorkflow | Should -Not -Match 'RuntimeEvidence'
        $upload | Should -Match '(?m)^        if: always\(\)$'
        $upload | Should -Match '(?m)^          retention-days: 14$'
        $upload | Should -Not -Match 'Runtime-Prerequisites-Private|\.exe|installer\.bytes'
        $export | Should -Match '(?m)^        if: success\(\)$'
        $job | Should -Not -Match '(?m)^            -Scope\b'
    }

    It 'runs release validation before Unity project configuration' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'

        $validationJob | Should -Not -BeNullOrEmpty
        Assert-LinesInOrder -Block $validationJob -ExpectedLines @(
            '      - name: Prepare Unity project shell',
            '      - name: Run release validation',
            '      - name: Configure Unity project',
            '      - name: Export versioned validation ZIP'
        )
    }

    It 'captures detached-safe local branch snapshots for Release validation' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $initialSnapshotStep = Get-NamedStepBlock -Job $validationJob -Name 'Capture initial repository state'
        $finalSnapshotStep = Get-NamedStepBlock -Job $validationJob -Name 'Assert repository state unchanged'
        $safeExpression = '(& git branch --show-current | Out-String).Trim()'
        $unsafeExpression = '(& git branch --show-current).Trim()'

        $initialSnapshotStep | Should -Not -BeNullOrEmpty
        $finalSnapshotStep | Should -Not -BeNullOrEmpty
        $initialSnapshotStep.Contains($safeExpression) | Should -BeTrue
        $finalSnapshotStep.Contains($safeExpression) | Should -BeTrue
        $releaseWorkflow | Should -Not -Match ([regex]::Escape($unsafeExpression))
        ([regex]::Matches($releaseWorkflow, [regex]::Escape($safeExpression))).Count | Should -Be 2
    }

    It 'stages the same verified Shader-Core release asset in every Unity validation consumer' {
        $expectedUrl = 'https://github.com/lilxyzw/Shader-Core/releases/download/0.3.0/jp.lilxyzw.shadercore-0.3.0.zip'
        $expectedSha256 = '5fb149379441031e172128933a23a8e5acb066c76e9d942aa91ed13ad93c70b5'
        $consumers = @(
            [pscustomobject]@{ Workflow = $dailyWorkflow; Job = 'unity-daily' },
            [pscustomobject]@{ Workflow = $releaseWorkflow; Job = 'validate' }
        )

        $shaderCoreInstaller | Should -Not -BeNullOrEmpty
        $shaderCoreInstaller | Should -Match ([regex]::Escape($expectedUrl))
        $shaderCoreInstaller | Should -Match ([regex]::Escape($expectedSha256))

        foreach ($consumer in $consumers) {
            $job = Get-NamedJobBlock -Workflow $consumer.Workflow -Name $consumer.Job
            $step = Get-NamedStepBlock -Job $job -Name 'Install verified Shader-Core 0.3.0 release'

            $step | Should -Not -BeNullOrEmpty
            $step | Should -Match 'Install-VerifiedShaderCoreRelease\.ps1'
            $step | Should -Match ([regex]::Escape($expectedUrl))
            $step | Should -Match ([regex]::Escape($expectedSha256))
            $step | Should -Not -Match 'actions/checkout|repository:\s*lilxyzw/Shader-Core'
        }
    }

    It 'defines Release validation producer permissions, exact SHA checkouts, and credential boundaries' -Tag ReleaseValidationProducer {
        $releaseWorkflow | Should -Match '(?m)^permissions:\n  contents: read$'

        foreach ($jobName in @('unity-editor-cache', 'validate')) {
            $job = Get-NamedJobBlock -Workflow $releaseWorkflow -Name $jobName
            $checkoutName = if ($jobName -eq 'unity-editor-cache') { 'Checkout cache lookup helper' } else { 'Checkout Pure-Base' }
            $checkout = Get-NamedStepBlock -Job $job -Name $checkoutName

            $job | Should -Not -BeNullOrEmpty
            $checkout | Should -Match '(?m)^        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1(?: #.*)?$'
            $checkout | Should -Match '(?m)^          ref: \$\{\{ github\.sha \}\}$'
            $checkout | Should -Match '(?m)^          persist-credentials: false$'
        }
    }

    It 'passes validation provenance and package metadata to the producer exporter' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $exportStep = Get-NamedStepBlock -Job $validationJob -Name 'Export versioned validation ZIP'

        $exportStep | Should -Not -BeNullOrEmpty
        Assert-LinesInOrder -Block $exportStep -ExpectedLines @(
            '            -PackageRoot $env:PACKAGE_ROOT `',
            '            -ValidationArtifactDirectory $env:ARTIFACT_ROOT `',
            '            -Repository ''${{ github.repository }}'' `',
            '            -HeadSha ''${{ github.sha }}'' `',
            '            -HeadBranch ''${{ github.ref_name }}'' `',
            '            -WorkflowRunId ''${{ github.run_id }}'' `',
            '            -WorkflowRunAttempt ''${{ github.run_attempt }}'''
        )
    }

    It 'proves producer ref invariants, forbids Git mutation, and uploads validated-package evidence' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $uploadStep = Get-NamedStepBlock -Job $validationJob -Name 'Upload release validation evidence'

        Assert-LinesInOrder -Block $validationJob -ExpectedLines @(
            '      - name: Capture initial repository state',
            '      - name: Run release validation',
            '      - name: Configure Unity project',
            '      - name: Export versioned validation ZIP',
            '      - name: Assert repository state unchanged',
            '      - name: Upload release validation evidence'
        )

        $releaseWorkflow | Should -Not -Match '(?im)^\s*git\s+(commit|push)\b'
        $releaseWorkflow | Should -Match '(?m)git\s+rev-parse\s+HEAD'
        $releaseWorkflow | Should -Match '(?m)git\s+status\s+--porcelain'
        $releaseWorkflow | Should -Match '(?m)git\s+for-each-ref'

        $uploadStep | Should -Match '(?m)^        uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9(?: #.*)?$'
        $uploadStep | Should -Match '(?m)^          path: \|$'
        $uploadStep | Should -Match '(?m)^          if-no-files-found: error$'
        $uploadStep | Should -Match '(?m)^          compression-level: 0$'
    }

    It 'uploads direct evidence children without parent-directory traversal' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $uploadStep = Get-NamedStepBlock -Job $validationJob -Name 'Upload release validation evidence'
        $pathMatch = [regex]::Match(
            $uploadStep,
            '(?ms)^          path: \|\n(?<paths>(?:^            [^\r\n]*(?:\r?\n|$))+?)(?=^          [A-Za-z0-9_-]+:|\z)'
        )

        $pathMatch.Success | Should -BeTrue
        $pathLines = @(
            $pathMatch.Groups['paths'].Value -split "\r?\n" |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                ForEach-Object { $_.Trim() }
        )
        $artifactRoot = '${{ runner.temp }}\PureBase-Release-Validation-${{ github.run_id }}-${{ github.run_attempt }}'
        $expectedPathLines = @(
            "$artifactRoot\validated-package"
            "$artifactRoot\repository-state.json"
            "$artifactRoot\ReleaseConsumer-*\bootstrap\**"
            "$artifactRoot\ReleaseConsumer-*\runs\**"
            "$artifactRoot\ReleaseConsumer-*\archive\*.zip"
            "$artifactRoot\ReleaseConsumer-*\archive\*.sha256"
            "$artifactRoot\ReleaseConsumer-*\shader-core-0.3.0.sha256.json"
            "$artifactRoot\ReleaseConsumer-*\run-summary.json"
            "$artifactRoot\ReleaseConsumer-*\cleanup-summary.json"
            "$artifactRoot\ReleaseConsumer-*\cleanup-failure.json"
            "$artifactRoot\*.log"
            "$artifactRoot\runtime-prerequisites\report.json"
        )
        $pathLines | Should -Be $expectedPathLines
        ($pathLines -join "`n") | Should -Not -Match '\.\.'
    }

    It 'defines Release consumer permissions and separates Pure-Base and VPM token scopes' {
        $releasePublishingWorkflow | Should -Match '(?m)^permissions:\n  actions: read\n  contents: read$'

        $releaseJob = Get-NamedJobBlock -Workflow $releasePublishingWorkflow -Name 'release'
        $releaseJob | Should -Not -BeNullOrEmpty
        $releasePublishingWorkflow | Should -Not -Match '(?m)^  unity-editor-cache:$'

        $pureBaseTokenStep = Get-NamedStepBlock -Job $releaseJob -Name 'Create release GitHub App token'
        $vpmTokenStep = Get-NamedStepBlock -Job $releaseJob -Name 'Create VPM dispatch GitHub App token'

        $pureBaseTokenStep | Should -Match '(?m)^          permission-contents: write$'
        $pureBaseTokenStep | Should -Match '(?m)^          permission-actions: read$'
        $pureBaseTokenStep | Should -Match '(?m)^          permission-workflows: write$'
        $pureBaseTokenStep | Should -Match '(?m)^          permission-administration: read$'
        $vpmTokenStep | Should -Match '(?m)^          permission-contents: write$'
        $vpmTokenStep | Should -Match '(?m)^          owner: \$\{\{ steps\.release-config\.outputs\.vpm_owner \}\}$'
        $vpmTokenStep | Should -Match '(?m)^          repositories: \$\{\{ steps\.release-config\.outputs\.vpm_repository \}\}$'
        $vpmTokenStep | Should -Not -Match '(?m)^          permission-(actions|workflows|administration):'
    }

    It 'documents artifact-only version confirmation and exact-SHA resume requirements' {
        $releasePublishingWorkflow | Should -Match '(?m)^        description: Confirm exact version from the checked-out package\.json$'
        $releasePublishingWorkflow | Should -Match '(?m)^        description: Resume only for an exact annotated tag and matching draft or published Release at the same SHA$'
        $releasePublishingWorkflow | Should -Not -Match '(?i)update_trigger\.json|resume after|package[ -]version[ -]commit'
    }

    It 'checks out the exact dispatch SHA without persisting the promotion credential' {
        $releaseJob = Get-NamedJobBlock -Workflow $releasePublishingWorkflow -Name 'release'
        $checkouts = @([regex]::Matches($releaseJob, '(?ms)^      - name: [^\r\n]*Checkout[^\r\n]*\n.*?(?=^      - name:|\z)') | ForEach-Object Value)

        $checkouts.Count | Should -Be 1
        $checkouts[0] | Should -Match '(?m)^        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1(?: #.*)?$'
        $checkouts[0] | Should -Match '(?m)^          ref: \$\{\{ github\.sha \}\}$'
        $checkouts[0] | Should -Match '(?m)^          fetch-depth: 0$'
        $checkouts[0] | Should -Match '(?m)^          fetch-tags: true$'
        $checkouts[0] | Should -Match '(?m)^          token: \$\{\{ steps\.release-app-token\.outputs\.token \}\}$'
        $checkouts[0] | Should -Match '(?m)^          persist-credentials: false$'
    }

    It 'supports an optional no-mutation preflight and repeated release-branch tip checks' {
        $releasePublishingWorkflow | Should -Match '(?m)^      preflight_only:$'
        $releasePublishingWorkflow | Should -Match '(?m)^        default: false$'
        $releasePublishingWorkflow | Should -Match '(?m)^          PREFLIGHT_ONLY: \$\{\{ inputs\.preflight_only \}\}$'

        $releaseJob = Get-NamedJobBlock -Workflow $releasePublishingWorkflow -Name 'release'
        $releaseInvocation = Get-NamedStepBlock -Job $releaseJob -Name 'Promote validated artifact release'
        $releaseInvocation | Should -Match '(?m)^          \$preflightOnly = \$env:PREFLIGHT_ONLY -eq ''true''$'
        $releaseInvocation | Should -Match '(?m)^            -ValidatedEventSha ''\$\{\{ github\.sha \}\}'' `$'
        $releaseInvocation | Should -Match '(?m)^            -ReleaseArtifactDirectory \$env:RELEASE_ARTIFACT_ROOT `$'
        $releaseInvocation | Should -Match '(?m)^            -Repository ''\$\{\{ github\.repository \}\}'' `$'
        $releaseInvocation | Should -Match '(?m)^            -Branch \$env:RELEASE_BRANCH `$'
        $releaseInvocation | Should -Match '(?m)^            -ConfirmedVersion \$env:CONFIRMED_VERSION `$'
        $releaseInvocation | Should -Match '(?m)^            -Resume:\$resume `$'
        $releaseInvocation | Should -Match '(?m)^            -PreflightOnly:\$preflightOnly$'

        $releaseAutomationScript | Should -Match '(?m)^function Assert-RemoteReleaseBranchHead\b'
        $releaseAutomationScript | Should -Match '(?m)\[switch\]\$PreflightOnly'
        $releaseAutomationScript | Should -Match '(?m)if \(\$PreflightOnly\)'
        $releaseAutomationScript | Should -Match '(?ms)^function Invoke-MutationGate.*?^    Assert-RemoteReleaseBranchHead$'
        $releaseAutomationScript | Should -Match '(?ms)^Write-State ''release-mode-resolved''.*?^Assert-RemoteReleaseBranchHead$.*?^if \(\$PreflightOnly\)'
        ([regex]::Matches($releaseAutomationScript, '(?m)Assert-RemoteReleaseBranchHead')).Count | Should -Be 3
    }

    It 'removes Unity, version-writer, and branch-push responsibilities from the consumer' {
        $releasePublishingWorkflow | Should -Not -Match '(?i)unity|shader-core|Run-PureBaseReleaseValidation|New-PureBaseCiProject|Export-PureBaseValidationZip|Set-PackageVersion|Build-Zip|Build-PureBaseRelease|Compress-Archive|ValidationArtifactDirectory|VALIDATION_ARTIFACT_ROOT|UnityEditorPath|REAL_UNITY_EDITOR_PATH|UNITY_EDITOR_PATH'
        $releaseAutomationScript | Should -Not -Match '(?m)\[Parameter\(Mandatory\)\]\[string\]\$UnityEditorPath'
        $releaseAutomationScript | Should -Not -Match '(?m)\b(Set-PackageVersion|Build-Zip|Commit-And-Tag|Run-PureBaseReleaseValidation)\b'
        $releaseAutomationScript | Should -Not -Match '(?im)^\s*git\s+push\s+.*(?:refs/heads|HEAD:)'
    }

    It 'keeps promotion mutations in the fixed validation-to-dispatch order' {
        $mainScript = $releaseAutomationScript.Substring($releaseAutomationScript.IndexOf("Write-State 'preflight'"))
        $anchors = @(
            'Select-PureBaseReleaseValidationRun',
            'Get-ValidatedArtifact',
            'Assert-RemoteReleaseBranchHead',
            'refs/tags/',
            'Resolve-PureBaseDraftAssetAction',
            'Assert-PureBasePublishedResumeArtifact',
            'New-PureBaseDispatchPayload'
        )

        $lastIndex = -1
        foreach ($anchor in $anchors) {
            $currentIndex = $mainScript.IndexOf($anchor)
            $currentIndex | Should -BeGreaterThan $lastIndex
            $lastIndex = $currentIndex
        }
    }
}