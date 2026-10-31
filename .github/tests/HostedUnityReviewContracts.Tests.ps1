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

# Checks shared hosted workflow and watchdog contracts.
Describe 'Hosted Unity review contracts' {
    BeforeAll {
        . (Join-Path $PSScriptRoot 'HostedUnityReviewContracts.Helpers.ps1')
    }

    It 'extracts composite action steps and quoted YAML outside run blocks' {
        $action = [string]::Join("`n", @(
                'runs:',
                '  using: composite',
                '  steps:',
                '',
                '    - name: Install Unity CLI (Windows)',
                '      run: |',
                '        echo install',
                '',
                '    - name: Determine Unity install roots',
                '      id: install-roots',
                '      run: |',
                '        echo paths',
                '',
                '    - name: Shell selector text must not select this step',
                '      run: |',
                '        name: Install Unity CLI (Windows)',
                '        id: install-roots',
                '',
                '    - id: cache',
                '      uses: "actions/cache/restore@55cc8345863c7cc4c66a329aec7e433d2d1c52a9"',
                '      run: |',
                '        restore-keys: shell text',
                '        continue-on-error: shell text',
                '      "restore-keys": invalid',
                "      'continue-on-error': true"
            ))

        $installRootsSteps = @(Get-CompositeActionStepBlocks -Action $action -Selector id -Value 'install-roots')
        $installRootsSteps.Count | Should -Be 1
        $installRootsSteps[0] | Should -Match '(?m)^    - name: Determine Unity install roots$'
        $installRootsSteps[0] | Should -Match '(?m)^      id: install-roots$'

        $windowsInstallerSteps = @(Get-CompositeActionStepBlocks -Action $action -Selector name -Value 'Install Unity CLI (Windows)')
        $windowsInstallerSteps.Count | Should -Be 1
        $windowsInstallerSteps[0] | Should -Match '(?m)^    - name: Install Unity CLI \(Windows\)$'

        $references = @(Get-ActionReferences -Source $action)
        $references.Count | Should -Be 1
        $references[0] | Should -Be 'actions/cache/restore@55cc8345863c7cc4c66a329aec7e433d2d1c52a9'
        (Get-ActionYamlKeyOccurrences -Action $action -Key 'restore-keys').Count | Should -Be 1
        (Get-ActionYamlKeyOccurrences -Action $action -Key 'continue-on-error').Count | Should -Be 1
    }

    It 'accepts ordered lines in a valid literal run block' {
        $step = [string]::Join("`n", @(
                '    - id: install-roots',
                '      shell: bash',
                '      run: |',
                '        if [ "$RUNNER_OS" = "Windows" ]; then',
                '          export PATH="$(cygpath "$LOCALAPPDATA")/Unity/bin:$PATH"',
                '        else',
                '          export PATH="$HOME/.unity/bin:$PATH"',
                '        fi',
                '        unity install-path </dev/null > /tmp/install_roots.txt'
            ))

        {
            Assert-LiteralRunLinesInOrder -Step $step -ExpectedLines @(
                'if [ "$RUNNER_OS" = "Windows" ]; then',
                'export PATH="$(cygpath "$LOCALAPPDATA")/Unity/bin:$PATH"',
                'else',
                'export PATH="$HOME/.unity/bin:$PATH"',
                'fi',
                'unity install-path </dev/null > /tmp/install_roots.txt'
            )
        } | Should -Not -Throw
    }

    It 'rejects direct inputs after an internal literal run blank line' {
        $steps = [string]::Join("`n", @(
                '    - name: Install pinned CLI',
                '      shell: pwsh',
                '      run: |',
                '        Write-Output "safe"',
                '',
                '        $cliVersion = "${{ inputs.cli-version }}"',
                '    - name: Later step',
                '      run: |',
                '        Write-Output "not part of the first run"'
            ))

        $literalRun = Get-LiteralRunBlock -Step $steps
        $literalRun | Should -Match '\$\{\{\s*inputs\.cli-version\s*\}\}'
        $literalRun | Should -Not -Match 'not part of the first run'
        { $literalRun | Should -Not -Match '\$\{\{\s*inputs\.' } | Should -Throw
    }

    It 'uses repository-unique PR concurrency for Daily' {
        $dailyWorkflow.Contains('group: daily-${{ github.event.pull_request.number || github.ref }}') | Should -BeTrue
        $dailyWorkflow.Contains('group: daily-${{ github.event.pull_request.head.ref || github.ref_name }}') | Should -BeFalse
    }

    It 'keeps actionlint isolated and preserves the Pester job' {
        $actionlintJob = Get-NamedJobBlock -Workflow $automationTestsWorkflow -Name 'actionlint'
        $pesterJob = Get-NamedJobBlock -Workflow $automationTestsWorkflow -Name 'pester'
        $checkout = Get-NamedStepBlock -Job $actionlintJob -Name 'Checkout repository'
        $actionlintStep = Get-NamedStepBlock -Job $actionlintJob -Name 'Run actionlint'

        $actionlintJob | Should -Not -BeNullOrEmpty
        $actionlintJob | Should -Match '(?m)^    runs-on: ubuntu-latest$'
        $actionlintJob | Should -Not -Match '(?m)^    needs:'
        $checkout | Should -Match '(?m)^        uses: actions/checkout@'
        $checkout | Should -Match '(?m)^          persist-credentials: false$'
        @(Get-ActionReferences -Source $actionlintJob).Count | Should -Be 1
        $actionlintStep | Should -Match '(?m)^        run: go run github\.com/rhysd/actionlint/cmd/actionlint@v1\.7\.12 -color$'
        $pesterJob | Should -Not -BeNullOrEmpty
        $pesterJob | Should -Match '(?m)^    name: Pester 5\.9\.0$'
        $pesterJob | Should -Match '(?m)^      - name: Run automation tests$'
    }

    It 'uses fixed inputs and one verified executable for setup' {
        $lookupActionPath | Should -Exist
        $inputs = @(Get-CompositeActionStepBlocks -Action $lookupAction -Selector id -Value 'inputs')
        $editor = @(Get-CompositeActionStepBlocks -Action $lookupAction -Selector id -Value 'editor')
        $inputs.Count | Should -Be 1
        $editor.Count | Should -Be 1
        $inputs[0] | Should -Not -Match '(?m)^      if:'
        $inputs[0] | Should -Match 'Tests/Tooling/PureBaseValidationInputs\.ps1'
        $inputs[0] | Should -Match 'Get-PureBaseValidationInputs -PackageRoot \$root'
        ([regex]::Matches($lookupAction, '(?m)^\s*Install-PinnedUnityCli -Version')).Count | Should -Be 1
        $inputs[0] | Should -Match 'Install-PinnedUnityCli -Version \$inputData\.UnityCliVersion -ExpectedSha256 \$inputData\.UnityCliSha256'
        $inputs[0] | Should -Match '\$cli = Join-Path \$env:LOCALAPPDATA ''Unity/bin/unity\.exe'''
        $inputs[0] | Should -Match '& \$cli install-path'
        $inputs[0] | Should -Match 'if \(\$LASTEXITCODE -ne 0\) \{ throw'
        $inputs[0] | Should -Match 'Get-UnityEditorCachePaths -Roots \$roots -Version \$inputData\.UnityVersion'
        $inputs[0] | Should -Match 'Unity CLI must not be included in the Editor cache'
        $inputs[0] | Should -Match '"cli=\$cli" >> \$env:GITHUB_OUTPUT'
        $inputs[0] | Should -Match '"version=\$\(\$inputData\.UnityVersion\)" >> \$env:GITHUB_OUTPUT'
        $inputs[0] | Should -Match '"revision=\$\(\$inputData\.UnityRevision\)" >> \$env:GITHUB_OUTPUT'
        $editor[0] | Should -Match 'CLI: \$\{\{ steps\.inputs\.outputs\.cli \}\}'
        $editor[0] | Should -Match 'CACHE_HIT: \$\{\{ steps\.cache\.outputs\.cache-hit \}\}'
        $editor[0] | Should -Match 'Resolve-InstalledUnityEditor -CliPath \$env:CLI .* -CacheHit \$env:CACHE_HIT'
        $editor[0] | Should -Not -Match '(?m)^      if:'
        $lookupAction | Should -Not -Match '(?i)install\.ps1|\blatest\b|\blts\b|yamachu/'
        (Get-ActionYamlKeyOccurrences -Action $lookupAction -Key 'continue-on-error').Count | Should -Be 0
        $lookupAction | Should -Match '(?m)^  editor-path:\n(?:.*\n)*?    value: \$\{\{ steps\.editor\.outputs\.path \}\}$'
        $lookupAction | Should -Match '(?m)^  cache-hit:\n(?:.*\n)*?    value: \$\{\{ steps\.editor\.outputs\.cache-hit \}\}$'
    }

    It 'restores and saves only the exact Editor cache after validation' {
        $cache = @(Get-CompositeActionStepBlocks -Action $lookupAction -Selector id -Value 'cache')
        $cache.Count | Should -Be 1
        $references = @(Get-ActionReferences -Source $lookupAction)
        $references.Count | Should -Be 2
        $references[0] | Should -Match '^actions/cache/restore@[0-9a-f]{40}$'
        $references[1] | Should -Match '^actions/cache/save@[0-9a-f]{40}$'
        $key = 'key: pure-base-unity-editor-v1-${{ runner.os }}-${{ runner.arch }}-${{ steps.inputs.outputs.version }}-${{ steps.inputs.outputs.revision }}'
        ([regex]::Matches($lookupAction, [regex]::Escape($key))).Count | Should -Be 2
        ([regex]::Matches($lookupAction, '(?m)^        path: \$\{\{ steps\.inputs\.outputs\.paths \}\}$')).Count | Should -Be 2
        ([regex]::Matches($lookupAction, '(?m)^        enableCrossOsArchive: false$')).Count | Should -Be 2
        (Get-ActionYamlKeyOccurrences -Action $lookupAction -Key 'restore-keys').Count | Should -Be 0
        (Get-ActionYamlKeyOccurrences -Action $lookupAction -Key 'lookup-only').Count | Should -Be 0
        $lookupAction | Should -Match "(?m)^      if: steps\.cache\.outputs\.cache-hit != 'true'$"
        $lookupAction | Should -Not -Match '(?i)always\(\)|failure\(\)'
        $lookupAction.IndexOf('id: inputs') | Should -BeLessThan $lookupAction.IndexOf('id: cache')
        $lookupAction.IndexOf('id: cache') | Should -BeLessThan $lookupAction.IndexOf('id: editor')
        $lookupAction.IndexOf('id: editor') | Should -BeLessThan $lookupAction.IndexOf('uses: actions/cache/save@')
    }

    It 'defines a fail-closed pinned Windows X64 CLI bootstrap' {
        $bootstrapScriptPath | Should -Exist

        if ($null -eq $bootstrapScript) {
            return
        }

        $bootstrapScript | Should -Match '(?m)^function Install-PinnedUnityCli\b'
        $bootstrapScript | Should -Match ([regex]::Escape('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$'))
        $bootstrapScript | Should -Match ([regex]::Escape('^[0-9a-f]{64}$'))
        $bootstrapScript | Should -Match 'RunnerOS.*Windows'
        $bootstrapScript | Should -Match 'RunnerArchitecture.*X64'
        $bootstrapScript | Should -Match 'https://public-cdn\.cloud\.unity3d\.com/hub/prod/cli/\$Version/unity-windows-x64\.exe'
        $bootstrapScript | Should -Match 'Invoke-WebRequest.*-ErrorAction Stop'
        $bootstrapScript | Should -Match 'Get-FileHash.*SHA256'
        $bootstrapScript | Should -Match 'Remove-Item'
        $bootstrapScript | Should -Match 'Join-Path \$LocalApplicationDataRoot [\'']Unity[\'']'
        $bootstrapScript | Should -Match 'unity\.exe'

        $networkIndex = $bootstrapScript.IndexOf('Invoke-WebRequest')
        $hashIndex = $bootstrapScript.IndexOf('Get-FileHash')
        $moveIndex = $bootstrapScript.IndexOf('Move-Item')
        $windowsGuardIndex = $bootstrapScript.IndexOf('Windows')
        $architectureGuardIndex = $bootstrapScript.IndexOf('X64')
        $windowsGuardIndex | Should -BeGreaterThan -1
        $architectureGuardIndex | Should -BeGreaterThan -1
        $networkIndex | Should -BeGreaterThan $windowsGuardIndex
        $networkIndex | Should -BeGreaterThan $architectureGuardIndex
        $hashIndex | Should -BeGreaterThan $networkIndex
        $moveIndex | Should -BeGreaterThan $hashIndex
    }

    It 'keeps exactly one unconditional pinned setup call in each Unity execution job' -ForEach @(
        @{ WorkflowName = 'Daily'; Job = 'unity-daily'; CheckoutRoot = 'PureBaseCi'; Ref = 'needs.authorize.outputs.checkout_ref' },
        @{ WorkflowName = 'Release validation'; Job = 'validate'; CheckoutRoot = 'PureBaseSource'; Ref = 'github.sha' }
    ) {
        $Workflow = switch ($WorkflowName) {
            'Daily' { $dailyWorkflow }
            'Release validation' { $releaseWorkflow }
            'Release publishing' { $releasePublishingWorkflow }
        }
        $executionJob = Get-NamedJobBlock -Workflow $Workflow -Name $Job
        $setupStep = Get-NamedStepBlock -Job $executionJob -Name 'Restore Unity Editor'

        $executionJob | Should -Not -BeNullOrEmpty
        $Workflow | Should -Not -Match '(?m)^  unity-editor-cache:'
        $Workflow | Should -Not -Match 'lookup-unity-editor-cache|yamachu/unity-cli-actions'
        $checkout = Get-NamedStepBlock -Job $executionJob -Name 'Checkout Pure-Base'
        $checkout | Should -Match 'uses: actions/checkout@[0-9a-f]{40}'
        $checkout | Should -Match ([regex]::Escape('ref: ${{ ' + $Ref + ' }}'))
        $checkout | Should -Match 'persist-credentials: false'
        ([regex]::Matches($executionJob, 'uses: \./[^\r\n]*/setup-unity-editor')).Count | Should -Be 1
        $setupStep | Should -Not -Match '(?m)^        if:'
        $setupStep | Should -Match ([regex]::Escape("uses: ./$CheckoutRoot/Packages/jp.penguin.purebase/.github/actions/setup-unity-editor"))
        $executionJob.IndexOf($checkout) | Should -BeLessThan $executionJob.IndexOf($setupStep)
    }

    It 'preserves top-level read-only workflow permissions' {
        foreach ($workflow in @($dailyWorkflow, $releaseWorkflow)) {
            $workflow | Should -Match '(?m)^permissions:\n  contents: read$'
        }
    }

    It 'permits only SHA-pinned external actions and the planned local helper references' {
        $sources = @($dailyWorkflow, $releaseWorkflow, $releasePublishingWorkflow)
        if ($null -ne $lookupAction) {
            $sources += $lookupAction
        }

        $references = @($sources | ForEach-Object { Get-ActionReferences -Source $_ })
        $local = @('./PureBaseCi/Packages/jp.penguin.purebase/.github/actions/setup-unity-editor', './PureBaseSource/Packages/jp.penguin.purebase/.github/actions/setup-unity-editor')
        foreach ($path in $local) { @($references | Where-Object { $_ -eq $path }).Count | Should -Be 1 }
        foreach ($reference in $references | Where-Object { $_ -notin $local }) {
            $reference | Should -Match '^[^@]+@[0-9a-f]{40}$'
        }
    }

    It 'preserves authorization, license retry, and editor-path boundaries' {
        $dailyWorkflow | Should -Match 'Resolve-PureBaseDailySource'
        $dailyWorkflow | Should -Match 'ref: \$\{\{ needs\.authorize\.outputs\.checkout_ref \}\}'
        $dailyWorkflow | Should -Not -Match 'pull_request_target'
        $dailyJob = Get-NamedJobBlock -Workflow $dailyWorkflow -Name 'unity-daily'
        $dailyJob | Should -Match "(?m)^    if: needs\.authorize\.outputs\.allowed == 'true'$"
        $dailyJob | Should -Match '(?m)^      - authorize$'

        foreach ($workflow in @($dailyWorkflow, $releaseWorkflow)) {
            $executionJobName = if ($workflow -eq $dailyWorkflow) { 'unity-daily' } else { 'validate' }
            $executionJob = Get-NamedJobBlock -Workflow $workflow -Name $executionJobName
            $activationStep = Get-NamedStepBlock -Job $executionJob -Name 'Activate Unity Personal license'
            $waitStep = Get-NamedStepBlock -Job $executionJob -Name 'Wait before retrying Unity license activation'
            $retryStep = Get-NamedStepBlock -Job $executionJob -Name 'Retry Unity Personal license activation'

            $activationStep | Should -Match '(?m)^        id: unity_license$'
            $activationStep | Should -Match '(?m)^        continue-on-error: true$'
            $activationStep | Should -Match '(?m)^          license: personal$'
            $activationStep | Should -Match '(?m)^          username: \$\{\{ secrets\.UNITY_EMAIL \}\}$'
            $activationStep | Should -Match '(?m)^          password: \$\{\{ secrets\.UNITY_PASSWORD \}\}$'
            ([regex]::Matches($activationStep, '(?m)^          license: personal$')).Count | Should -Be 1
            ([regex]::Matches($activationStep, '(?m)^          username: \$\{\{ secrets\.UNITY_EMAIL \}\}$')).Count | Should -Be 1
            ([regex]::Matches($activationStep, '(?m)^          password: \$\{\{ secrets\.UNITY_PASSWORD \}\}$')).Count | Should -Be 1
            $waitStep | Should -Match "(?m)^        if: steps\.unity_license\.outcome == 'failure'$"
            ([regex]::Matches($executionJob, '(?m)^      - name: Retry Unity Personal license activation$')).Count | Should -Be 1
            $retryStep | Should -Match "(?m)^        if: steps\.unity_license\.outcome == 'failure'$"
            $retryStep | Should -Match '(?m)^          license: personal$'
            $retryStep | Should -Match '(?m)^          username: \$\{\{ secrets\.UNITY_EMAIL \}\}$'
            $retryStep | Should -Match '(?m)^          password: \$\{\{ secrets\.UNITY_PASSWORD \}\}$'
            ([regex]::Matches($retryStep, '(?m)^          license: personal$')).Count | Should -Be 1
            ([regex]::Matches($retryStep, '(?m)^          username: \$\{\{ secrets\.UNITY_EMAIL \}\}$')).Count | Should -Be 1
            ([regex]::Matches($retryStep, '(?m)^          password: \$\{\{ secrets\.UNITY_PASSWORD \}\}$')).Count | Should -Be 1
        }

        $dailyEditorPathStep = Get-NamedStepBlock `
            -Job (Get-NamedJobBlock -Workflow $dailyWorkflow -Name 'unity-daily') `
            -Name 'Export Unity editor path'
        $dailyEditorPathStep | Should -Match "-EditorPath '\$\{\{ steps\.unity\.outputs\.editor-path \}\}'"
    }

    It 'aligns the configure watchdog with the workflow timeout' {
        $watchdogScript.Contains("`$timeoutSeconds = if (`$UnityArguments -contains '-runTests') { 3600 } else { 1800 }") | Should -BeTrue
        $watchdogScript.Contains('same 30-minute') | Should -BeTrue
    }

    It 'preserves the original watchdog start failure without an unstarted process error' {
        $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('PureBase-Watchdog-Test-' + [guid]::NewGuid().ToString('N'))
        $fixturePath = Join-Path $temporaryRoot 'not-an-executable.txt'
        $logPath = Join-Path ([IO.Path]::GetTempPath()) ('PureBase-Watchdog-Log-' + [guid]::NewGuid().ToString('N') + '.log')
        $diagnosticPath = [IO.Path]::ChangeExtension($logPath, 'Watchdog.txt')
        $expectedProcess = [Diagnostics.Process]::new()
        $expectedStartException = $null
        $unstartedProcess = [Diagnostics.Process]::new()
        $unstartedProcessException = $null

        try {
            New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
            Set-Content -LiteralPath $fixturePath -Value 'not an executable'

            $expectedProcess.StartInfo = [Diagnostics.ProcessStartInfo]::new()
            $expectedProcess.StartInfo.FileName = $fixturePath
            $expectedProcess.StartInfo.Arguments = "-logFile $logPath"
            $expectedProcess.StartInfo.UseShellExecute = $false
            $expectedProcess.StartInfo.CreateNoWindow = $true
            $expectedProcess.StartInfo.WorkingDirectory = [Environment]::CurrentDirectory
            try {
                [void]$expectedProcess.Start()
                throw 'The invalid Unity Editor fixture unexpectedly started.'
            }
            catch {
                $expectedStartException = $_.Exception
            }

            $expectedStartException.GetType().FullName | Should -Be 'System.Management.Automation.MethodInvocationException'
            $expectedStartException.InnerException.GetType().FullName | Should -Be 'System.ComponentModel.Win32Exception'
            $expectedStartMessage = $expectedStartException.Message

            try {
                $null = [Diagnostics.Process].GetProperty('Id').GetValue($unstartedProcess)
                throw 'The unstarted process unexpectedly has an ID.'
            }
            catch {
                $unstartedProcessException = $_.Exception
            }

            $unstartedProcessBaseException = $unstartedProcessException.GetBaseException()
            $unstartedProcessBaseException.GetType().FullName | Should -Be 'System.InvalidOperationException'
            $unstartedProcessMessage = $unstartedProcessBaseException.Message
            $unstartedProcessMessage | Should -Not -BeNullOrEmpty

            $childOutput = & pwsh -NoLogo -NoProfile -NonInteractive `
                -File (Join-Path $repositoryRoot '.github/scripts/UnityWatchdogProxy.ps1') `
                -UnityEditorPath $fixturePath `
                -logFile $logPath 2>&1 | Out-String
            $exitCode = $LASTEXITCODE

            $exitCode | Should -Be 1
            Test-Path -LiteralPath $diagnosticPath -PathType Leaf | Should -BeTrue
            $diagnostic = Get-Content -LiteralPath $diagnosticPath -Raw
            $diagnosticMatch = [regex]::Match($diagnostic, '(?m)^Exception=(?<message>[^\r\n]*)\r?$')
            $diagnosticMatch.Success | Should -BeTrue
            $diagnosticMatch.Groups['message'].Value | Should -Be $expectedStartMessage

            $normalizedChildOutput = ($childOutput -replace '(?m)^\s*\|\s*', '') -replace '\s+', ''
            $normalizedExpectedStartMessage = $expectedStartMessage -replace '\s+', ''
            $normalizedUnstartedProcessMessage = $unstartedProcessMessage -replace '\s+', ''
            $normalizedChildOutput | Should -Match ([regex]::Escape($normalizedExpectedStartMessage))
            $normalizedChildOutput | Should -Not -Match ([regex]::Escape($normalizedUnstartedProcessMessage))
        }
        finally {
            $expectedProcess.Dispose()
            $unstartedProcess.Dispose()
            Remove-Item -LiteralPath $temporaryRoot, $logPath, $diagnosticPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'keeps documentation and diagnostics free of stale reviewed values' {
        $ciDocumentation.Contains('GitHub-hosted `windows-2022` runners') | Should -BeTrue
        $ciDocumentation.Contains('GitHub-hosted `windows-latest` runners') | Should -BeFalse
        $shadowDiagnostics.Contains('341-352') | Should -BeFalse
        $shadowDiagnostics.Contains('metaUnlitRange') | Should -BeTrue
        $shadowDiagnostics.Contains('metaToonRange') | Should -BeTrue
        $shadowDiagnostics.Contains('metaPbrRange') | Should -BeTrue
        $shadowDiagnostics.Contains('metaHybridRange') | Should -BeTrue
        $shadowDiagnostics.Contains('shadowRange') | Should -BeTrue
    }
}

