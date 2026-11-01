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
    }

    It 'provisions prerequisites before Unity and only uploads the safe report' {
        $job = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $paths = Get-UniqueWorkflowStep -Job $job -Pattern 'Resolve-UnityEditorPath\.ps1'
        $provision = Get-UniqueWorkflowStep -Job $job -Pattern 'Install-ReleaseUnityRuntimePrerequisites\.ps1'
        $upload = Get-UniqueWorkflowStep -Job $job -Pattern '(?m)^        uses: actions/upload-artifact@'
        $export = Get-UniqueWorkflowStep -Job $job -Pattern 'Export-PureBaseValidationZip\.ps1'
        $initial = Get-UniqueWorkflowStep -Job $job -Pattern '\$initialHead\s*='
        $shader = Get-UniqueWorkflowStep -Job $job -Pattern '(?m)^        id: shader-core$'
        $editor = Get-UniqueWorkflowStep -Job $job -Pattern '(?m)^        id: unity$'
        $job.IndexOf($initial) | Should -BeLessThan $job.IndexOf($provision)
        $job.IndexOf($provision) | Should -BeLessThan $job.IndexOf($shader)
        $job.IndexOf($shader) | Should -BeLessThan $job.IndexOf($editor)
        $job.IndexOf($editor) | Should -BeLessThan $job.IndexOf($paths)
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
        $prepare = Get-UniqueWorkflowStep -Job $validationJob -Pattern 'New-PureBaseCiProject\.ps1'
        $validation = Get-UniqueWorkflowStep -Job $validationJob -Pattern 'Run-PureBaseReleaseValidation\.ps1'
        $configuration = Get-UniqueWorkflowStep -Job $validationJob -Pattern 'PureBaseCiProjectInitializer\.InitializeForBatchMode'
        $export = Get-UniqueWorkflowStep -Job $validationJob -Pattern 'Export-PureBaseValidationZip\.ps1'
        $validationJob.IndexOf($prepare) | Should -BeLessThan $validationJob.IndexOf($validation)
        $validationJob.IndexOf($validation) | Should -BeLessThan $validationJob.IndexOf($configuration)
        $validationJob.IndexOf($configuration) | Should -BeLessThan $validationJob.IndexOf($export)
    }

    It 'captures detached-safe local branch snapshots for Release validation' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $initialSnapshotStep = Get-UniqueWorkflowStep -Job $validationJob -Pattern '\$initialHead\s*='
        $finalSnapshotStep = Get-UniqueWorkflowStep -Job $validationJob -Pattern '\$finalHead\s*='

        $initialSnapshotStep | Should -Not -BeNullOrEmpty
        $finalSnapshotStep | Should -Not -BeNullOrEmpty
        $validationJob.IndexOf($initialSnapshotStep) | Should -BeLessThan $validationJob.IndexOf($finalSnapshotStep)
    }

    It 'stages the same verified Shader-Core release asset in every Unity validation consumer' {
        $consumers = @(
            [pscustomobject]@{ Workflow = $dailyWorkflow; Job = 'unity-daily' },
            [pscustomobject]@{ Workflow = $releaseWorkflow; Job = 'validate' }
        )

        $shaderCoreInstaller | Should -Not -BeNullOrEmpty
        $shaderCoreInstaller | Should -Match 'Get-PureBaseValidationInputs'
        $shaderCoreInstaller | Should -Match '\$inputs\.ShaderCoreUri'
        $shaderCoreInstaller | Should -Match '\$inputs\.ShaderCoreSha256'
        $shaderCoreInstaller | Should -Match '\$inputs\.ShaderCoreVersion'

        foreach ($consumer in $consumers) {
            $job = Get-NamedJobBlock -Workflow $consumer.Workflow -Name $consumer.Job
            $step = Get-UniqueWorkflowStep -Job $job -Pattern '(?m)^        id: shader-core$'
            $inputs = Get-UniqueWorkflowStep -Job $job -Pattern '(?m)^        id: validation-inputs$'
            $checkout = Get-UniqueWorkflowStep -Job $job -Pattern '(?m)^        uses: actions/checkout@'

            $step | Should -Not -BeNullOrEmpty
            $step | Should -Match 'Install-VerifiedShaderCoreRelease\.ps1'
            $step | Should -Match '(?m)^        id: shader-core$'
            $step | Should -Not -Match '-Uri|-ExpectedSha256|-ExpectedVersion'
            $inputs | Should -Match 'PureBaseValidationInputs\.ps1'
            $inputs | Should -Match 'Get-PureBaseValidationInputs -PackageRoot \$env:PACKAGE_ROOT'
            $job.IndexOf($checkout) | Should -BeLessThan $job.IndexOf($inputs)
            $job.IndexOf($inputs) | Should -BeLessThan $job.IndexOf($step)
            $step | Should -Not -Match 'actions/checkout|repository:\s*lilxyzw/Shader-Core'
        }
    }

    It 'defines Release validation producer permissions, exact SHA checkouts, and credential boundaries' -Tag ReleaseValidationProducer {
        $releaseWorkflow | Should -Match '(?m)^permissions:\n  contents: read$'

        foreach ($jobName in @('validate')) {
            $job = Get-NamedJobBlock -Workflow $releaseWorkflow -Name $jobName
            $checkout = Get-UniqueWorkflowStep -Job $job -Pattern '(?m)^        uses: actions/checkout@'

            $job | Should -Not -BeNullOrEmpty
            $checkout | Should -Match '(?m)^        uses: actions/checkout@[0-9a-f]{40}(?: #.*)?$'
            $checkout | Should -Match '(?m)^          ref: \$\{\{ github\.sha \}\}$'
            $checkout | Should -Match '(?m)^          persist-credentials: false$'
        }
    }

    It 'passes validation provenance and package metadata to the producer exporter' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $exportStep = Get-UniqueWorkflowStep -Job $validationJob -Pattern 'Export-PureBaseValidationZip\.ps1'

        $exportStep | Should -Not -BeNullOrEmpty
        $requiredInputs = @(
            '            -PackageRoot $env:PACKAGE_ROOT `',
            '            -ValidationArtifactDirectory $env:ARTIFACT_ROOT `',
            '            -Repository ''${{ github.repository }}'' `',
            '            -HeadSha ''${{ github.sha }}'' `',
            '            -HeadBranch ''${{ github.ref_name }}'' `',
            '            -WorkflowRunId ''${{ github.run_id }}'' `',
            '            -WorkflowRunAttempt ''${{ github.run_attempt }}'''
        )
        foreach ($inputLine in $requiredInputs) {
            $exportStep | Should -Match ([regex]::Escape($inputLine.TrimEnd(' ', '`')) + '(?:\s|$)')
        }
    }

    It 'proves producer ref invariants, forbids Git mutation, and uploads validated-package evidence' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $uploadStep = Get-UniqueWorkflowStep -Job $validationJob -Pattern '(?m)^        uses: actions/upload-artifact@'
        $initial = Get-UniqueWorkflowStep -Job $validationJob -Pattern '\$initialHead\s*='
        $validation = Get-UniqueWorkflowStep -Job $validationJob -Pattern 'Run-PureBaseReleaseValidation\.ps1'
        $export = Get-UniqueWorkflowStep -Job $validationJob -Pattern 'Export-PureBaseValidationZip\.ps1'
        $final = Get-UniqueWorkflowStep -Job $validationJob -Pattern '\$finalHead\s*='
        $validationJob.IndexOf($initial) | Should -BeLessThan $validationJob.IndexOf($validation)
        $validationJob.IndexOf($export) | Should -BeLessThan $validationJob.IndexOf($final)
        $validationJob.IndexOf($final) | Should -BeLessThan $validationJob.IndexOf($uploadStep)

        $releaseWorkflow | Should -Not -Match '(?im)^\s*git\s+(commit|push)\b'
        $releaseWorkflow | Should -Match '(?m)git\s+rev-parse\s+HEAD'
        $releaseWorkflow | Should -Match '(?m)git\s+status\s+--porcelain'
        $releaseWorkflow | Should -Match '(?m)git\s+for-each-ref'

        $uploadStep | Should -Match '(?m)^        uses: actions/upload-artifact@[0-9a-f]{40}(?: #.*)?$'
        $uploadStep | Should -Match '(?m)^          path: \|$'
        $uploadStep | Should -Match '(?m)^          if-no-files-found: error$'
        $uploadStep | Should -Match '(?m)^          compression-level: 0$'
    }

    It 'uploads direct evidence children without parent-directory traversal' -Tag ReleaseValidationProducer {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'
        $uploadStep = Get-UniqueWorkflowStep -Job $validationJob -Pattern '(?m)^        uses: actions/upload-artifact@'
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
            "$artifactRoot\ReleaseConsumer-*\shader-core.sha256.json"
            "$artifactRoot\ReleaseConsumer-*\run-summary.json"
            "$artifactRoot\ReleaseConsumer-*\cleanup-summary.json"
            "$artifactRoot\ReleaseConsumer-*\cleanup-failure.json"
            "$artifactRoot\*.log"
            "$artifactRoot\runtime-prerequisites\report.json"
        )
        @($pathLines | Sort-Object) | Should -Be @($expectedPathLines | Sort-Object)
        ($pathLines -join "`n") | Should -Not -Match '\.\.'
    }

    It 'defines Release consumer permissions and separates Pure-Base and VPM token scopes' {
        $releasePublishingWorkflow | Should -Match '(?m)^permissions:\n  actions: read\n  contents: read$'

        $releaseJob = Get-NamedJobBlock -Workflow $releasePublishingWorkflow -Name 'release'
        $releaseJob | Should -Not -BeNullOrEmpty
        $releasePublishingWorkflow | Should -Not -Match '(?m)^  unity-editor-cache:$'

        $pureBaseTokenStep = Get-UniqueWorkflowStep -Job $releaseJob -Pattern '(?m)^        id: release-app-token$'
        $vpmTokenStep = Get-UniqueWorkflowStep -Job $releaseJob -Pattern '(?m)^        id: dispatch-app-token$'

        $pureBaseTokenStep | Should -Match '(?m)^          permission-contents: write$'
        $pureBaseTokenStep | Should -Match '(?m)^          permission-actions: read$'
        $pureBaseTokenStep | Should -Match '(?m)^          permission-workflows: write$'
        $pureBaseTokenStep | Should -Match '(?m)^          permission-administration: read$'
        $vpmTokenStep | Should -Match '(?m)^          permission-contents: write$'
        $vpmTokenStep | Should -Match '(?m)^          owner: \$\{\{ steps\.release-config\.outputs\.vpm_owner \}\}$'
        $vpmTokenStep | Should -Match '(?m)^          repositories: \$\{\{ steps\.release-config\.outputs\.vpm_repository \}\}$'
        $vpmTokenStep | Should -Not -Match '(?m)^          permission-(actions|workflows|administration):'
    }

    It 'avoids legacy version-writing and resume paths' {
        $releasePublishingWorkflow | Should -Not -Match '(?i)update_trigger\.json|resume after|package[ -]version[ -]commit'
    }

    It 'checks out the exact dispatch SHA without persisting the promotion credential' {
        $releaseJob = Get-NamedJobBlock -Workflow $releasePublishingWorkflow -Name 'release'
        $checkouts = @(Get-UniqueWorkflowStep -Job $releaseJob -Pattern '(?m)^        uses: actions/checkout@')

        $checkouts.Count | Should -Be 1
        $checkouts[0] | Should -Match '(?m)^        uses: actions/checkout@[0-9a-f]{40}(?: #.*)?$'
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
        $releaseInvocation = Get-UniqueWorkflowStep -Job $releaseJob -Pattern 'Invoke-PureBaseRelease\.ps1'
        $releaseInvocation | Should -Match '(?m)^          \$preflightOnly = \$env:PREFLIGHT_ONLY -eq ''true''$'
        $releaseInvocation | Should -Match '-PackageRoot \$env:PACKAGE_ROOT(?:\s|$)'
        $releaseInvocation | Should -Match '-ValidatedEventSha ''\$\{\{ github\.sha \}\}''(?:\s|$)'
        $releaseInvocation | Should -Match '-ReleaseArtifactDirectory \$env:RELEASE_ARTIFACT_ROOT(?:\s|$)'
        $releaseInvocation | Should -Match '-Repository ''\$\{\{ github\.repository \}\}''(?:\s|$)'
        $releaseInvocation | Should -Match '-Branch \$env:RELEASE_BRANCH(?:\s|$)'
        $releaseInvocation | Should -Match '-ConfirmedVersion \$env:CONFIRMED_VERSION(?:\s|$)'
        $releaseInvocation | Should -Match '-VpmRepository \$env:VPM_REPOSITORY(?:\s|$)'
        $releaseInvocation | Should -Match '-AppSlug \$env:APP_SLUG(?:\s|$)'
        $releaseInvocation | Should -Match '-Resume:\$resume(?:\s|$)'
        $releaseInvocation | Should -Match '-PreflightOnly:\$preflightOnly(?:\s|$)'

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