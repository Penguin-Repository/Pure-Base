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

    It 'defines the repository-owned lookup helper cache identity' {
        $lookupActionPath | Should -Exist

        if ($null -eq $lookupAction) {
            return
        }

        $lookupAction | Should -Match '(?m)^name: Lookup Unity Editor cache$'
        $lookupAction | Should -Match '(?m)^inputs:\r?\n(?:.*\r?\n)*?  unity-version:'
        $lookupAction | Should -Match '(?m)^  cli-version:'
        $lookupAction | Should -Match '(?m)^  cli-sha256:'
        $lookupAction | Should -Not -Match '(?m)^  cli-channel:'
        $cliVersionInput = [regex]::Match($lookupAction, '(?ms)^  cli-version:\n.*?(?=^  [A-Za-z0-9_-]+:|\z)').Value
        $cliSha256Input = [regex]::Match($lookupAction, '(?ms)^  cli-sha256:\n.*?(?=^  [A-Za-z0-9_-]+:|\z)').Value
        $cliVersionInput | Should -Match '(?m)^    required: true$'
        $cliSha256Input | Should -Match '(?m)^    required: true$'

        $windowsInstallerSteps = @(Get-CompositeActionStepBlocks -Action $lookupAction -Selector name -Value 'Install Unity CLI (Windows)')
        $installRootsSteps = @(Get-CompositeActionStepBlocks -Action $lookupAction -Selector id -Value 'install-roots')
        $cacheSteps = @(Get-CompositeActionStepBlocks -Action $lookupAction -Selector id -Value 'cache')

        $windowsInstallerSteps.Count | Should -Be 1
        $installRootsSteps.Count | Should -Be 1
        $cacheSteps.Count | Should -Be 1

        $windowsInstallerStep = $windowsInstallerSteps[0]
        $windowsInstallerStep | Should -Not -Match '(?m)^      if:'
        $windowsInstallerStep | Should -Match '(?m)^      shell: pwsh$'
        $windowsInstallerStep | Should -Match '(?m)^      run: \|$'
        $windowsInstallerHeader = Get-StepMappingHeader -Step $windowsInstallerStep
        $windowsInstallerHeader | Should -Match '(?m)^      env:$'
        $windowsInstallerHeader | Should -Match '(?m)^        UNITY_CLI_VERSION: \$\{\{ inputs\.cli-version \}\}$'
        $windowsInstallerHeader | Should -Match '(?m)^        UNITY_CLI_SHA256: \$\{\{ inputs\.cli-sha256 \}\}$'
        $windowsInstallerHeader | Should -Match '(?m)^        UNITY_CLI_ACTION_PATH: \$\{\{ github\.action_path \}\}$'
        $windowsInstallerRun = Get-LiteralRunBlock -Step $windowsInstallerStep
        $windowsInstallerRun | Should -Not -Match '\$\{\{\s*inputs\.'
        $windowsInstallerRun | Should -Not -Match 'install\.ps1'
        $windowsInstallerRun | Should -Not -Match 'latest'
        Assert-LiteralRunLinesInOrder -Step $windowsInstallerStep -ExpectedLines @(
            '. "$env:UNITY_CLI_ACTION_PATH/Install-PinnedUnityCli.ps1"',
            'Install-PinnedUnityCli `',
            '-Version $env:UNITY_CLI_VERSION `',
            '-ExpectedSha256 $env:UNITY_CLI_SHA256'
        )

        $installRootsStep = $installRootsSteps[0]
        $installRootsStep | Should -Match '(?m)^      shell: bash$'
        Assert-LiteralRunLinesInOrder -Step $installRootsStep -ExpectedLines @(
            'export PATH="$(cygpath "$LOCALAPPDATA")/Unity/bin:$PATH"',
            'unity install-path </dev/null > /tmp/install_roots.txt',
            '{',
            'echo "paths<<UNITY_INSTALL_ROOTS_EOF"',
            'cat /tmp/install_roots.txt',
            'echo "UNITY_INSTALL_ROOTS_EOF"',
            '} >> "$GITHUB_OUTPUT"'
        )
        $installRootsStep | Should -Not -Match '\$HOME/\.unity/bin'
        $installRootsStep | Should -Not -Match 'sed -n ''l'''

        $cacheActionReferences = @(
            Get-ActionReferences -Source $lookupAction |
                Where-Object { $_ -match '^actions/cache(?:/restore)?@' }
        )
        $cacheActionReferences.Count | Should -Be 1
        $cacheActionReferences[0] | Should -Be 'actions/cache/restore@55cc8345863c7cc4c66a329aec7e433d2d1c52a9'

        $cacheStep = $cacheSteps[0]
        $cacheStepReferences = @(Get-ActionReferences -Source $cacheStep)
        $cacheStepReferences.Count | Should -Be 1
        $cacheStepReferences[0] | Should -Be 'actions/cache/restore@55cc8345863c7cc4c66a329aec7e433d2d1c52a9'
        $cachePaths = @([regex]::Matches($cacheStep, '(?m)^        path: (?<value>.+)$'))
        $cachePaths.Count | Should -Be 1
        $cachePaths[0].Groups['value'].Value | Should -Be '${{ steps.install-roots.outputs.paths }}'
        $cacheStep | Should -Match '(?m)^        key: unity-editor-\$\{\{ runner\.os \}\}-\$\{\{ runner\.arch \}\}-\$\{\{ inputs\.unity-version \}\}$'
        $cacheStep | Should -Match '(?m)^        lookup-only: true$'
        $cacheStep | Should -Match '(?m)^        enableCrossOsArchive: false$'

        $outputsMatch = [regex]::Match($lookupAction, '(?ms)^outputs:\n(?<outputs>(?:^ {2,}.*(?:\n|\z))*)')
        $cacheHitOutputs = @([regex]::Matches($outputsMatch.Groups['outputs'].Value, '(?ms)^  cache-hit:\n.*?(?=^  [A-Za-z0-9_-]+:\n|\z)'))
        $cacheHitOutputs.Count | Should -Be 1
        $cacheHitOutputs[0].Value | Should -Match '(?m)^    value: \$\{\{ steps\.cache\.outputs\.cache-hit \}\}$'
        (Get-ActionYamlKeyOccurrences -Action $lookupAction -Key 'restore-keys').Count | Should -Be 0
        (Get-ActionYamlKeyOccurrences -Action $lookupAction -Key 'continue-on-error').Count | Should -Be 0
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

    It 'uses lookup-only only in the local helper' {
        $dailyWorkflow | Should -Not -Match '(?m)^\s*lookup-only:'
        $releaseWorkflow | Should -Not -Match '(?m)^\s*lookup-only:'

        if ($null -ne $lookupAction) {
            ([regex]::Matches($lookupAction, '(?m)^\s*lookup-only: true$')).Count | Should -Be 1
        }
    }

    It 'uses the local helper and a conditional upstream fallback in each cache preparation job' -ForEach @(
        @{
            WorkflowName = 'Daily'
            CheckoutRef  = 'ref: \$\{\{ needs\.authorize\.outputs\.checkout_ref \}\}'
        },
        @{
            WorkflowName = 'Release validation'
            CheckoutRef  = 'ref: \$\{\{ github\.sha \}\}'
        }
    ) {
        $Workflow = if ($WorkflowName -eq 'Daily') { $dailyWorkflow } else { $releaseWorkflow }
        $preparationJob = Get-NamedJobBlock -Workflow $Workflow -Name 'unity-editor-cache'
        $checkoutStep = Get-NamedStepBlock -Job $preparationJob -Name 'Checkout cache lookup helper'
        $lookupStep = Get-NamedStepBlock -Job $preparationJob -Name 'Look up Unity Editor cache'
        $fallbackStep = Get-NamedStepBlock -Job $preparationJob -Name 'Restore or create Unity Editor cache'

        $preparationJob | Should -Not -BeNullOrEmpty
        $checkoutStep | Should -Match '(?m)^        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1'
        $checkoutStep | Should -Match "(?m)^          $CheckoutRef$"
        $checkoutStep | Should -Match '(?m)^          persist-credentials: false$'
        $lookupStep | Should -Match '(?m)^        id: unity-cache-lookup$'
        $lookupStep | Should -Match '(?m)^        uses: \./\.github/actions/lookup-unity-editor-cache$'
        $lookupStep | Should -Match '(?m)^          unity-version: "2022\.3\.28f1"$'
        $lookupStep | Should -Match '(?m)^          cli-version: "1\.0\.0-beta\.3"$'
        $lookupStep | Should -Match '(?m)^          cli-sha256: "ff9ef81ade1063041d25e2c549cc7ed14e96d446f4204400bf101b389f7b8502"$'
        $lookupStep | Should -Not -Match '(?m)^          cli-channel:'
        $fallbackStep | Should -Match "(?m)^        if: steps\.unity-cache-lookup\.outputs\.cache-hit != 'true'$"
        $fallbackStep | Should -Match '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@e0f32f7e273329bbe99af5bf5809bf1056935556$'
        $fallbackStep | Should -Match '(?m)^          unity-version: "2022\.3\.28f1"$'
        $fallbackStep | Should -Match '(?m)^          cli-version: "1\.0\.0-beta\.3"$'
        $fallbackStep | Should -Match '(?m)^          cli-channel: beta$'
        $fallbackStep | Should -Match '(?m)^          cache: "true"$'
        ([regex]::Matches($preparationJob, '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@')).Count | Should -Be 1
        $dailyWorkflow.Contains('throw "Unity Editor cache was not available') | Should -BeFalse
        $releaseWorkflow.Contains('throw "Unity Editor cache was not available') | Should -BeFalse
    }

    It 'keeps exactly one unconditional pinned setup call in each Unity execution job' -ForEach @(
        @{ WorkflowName = 'Daily'; Job = 'unity-daily' },
        @{ WorkflowName = 'Release validation'; Job = 'validate' }
    ) {
        $Workflow = switch ($WorkflowName) {
            'Daily' { $dailyWorkflow }
            'Release validation' { $releaseWorkflow }
            'Release publishing' { $releasePublishingWorkflow }
        }
        $executionJob = Get-NamedJobBlock -Workflow $Workflow -Name $Job
        $setupStep = Get-NamedStepBlock -Job $executionJob -Name 'Restore Unity 2022.3.28f1'

        $executionJob | Should -Not -BeNullOrEmpty
        ([regex]::Matches($executionJob, '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@')).Count | Should -Be 1
        $setupStep | Should -Not -Match '(?m)^        if:'
        $setupStep | Should -Match '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@e0f32f7e273329bbe99af5bf5809bf1056935556$'
        $setupStep | Should -Match '(?m)^          unity-version: "2022\.3\.28f1"$'
        $setupStep | Should -Match '(?m)^          cli-version: "1\.0\.0-beta\.3"$'
        $setupStep | Should -Match '(?m)^          cli-channel: beta$'
        $setupStep | Should -Match '(?m)^          cache: "true"$'
    }

    It 'preserves top-level read-only workflow permissions' {
        foreach ($workflow in @($dailyWorkflow, $releaseWorkflow)) {
            $workflow | Should -Match '(?m)^permissions:\n  contents: read$'
        }
    }

    It 'permits only SHA-pinned external actions and the planned local helper reference' {
        $sources = @($dailyWorkflow, $releaseWorkflow, $releasePublishingWorkflow)
        if ($null -ne $lookupAction) {
            $sources += $lookupAction
        }

        $references = @($sources | ForEach-Object { Get-ActionReferences -Source $_ })
        ($references | Where-Object { $_ -eq './.github/actions/lookup-unity-editor-cache' }).Count | Should -Be 2
        foreach ($reference in $references | Where-Object { $_ -ne './.github/actions/lookup-unity-editor-cache' }) {
            $reference | Should -Match '^[^@]+@[0-9a-f]{40}$'
        }
    }

    It 'preserves authorization, license retry, and editor-path boundaries' {
        $dailyWorkflow | Should -Match 'Resolve-PureBaseDailySource'
        $dailyWorkflow | Should -Match 'ref: \$\{\{ needs\.authorize\.outputs\.checkout_ref \}\}'
        $dailyWorkflow | Should -Not -Match 'pull_request_target'

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

