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

Describe 'Hosted Unity review contracts' {
    BeforeAll {
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $dailyWorkflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw) -replace "`r`n", "`n"
        $releaseWorkflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release-validation.yml') -Raw) -replace "`r`n", "`n"
        $releasePublishingWorkflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release.yml') -Raw) -replace "`r`n", "`n"
        $shaderCoreInstallerPath = Join-Path $repositoryRoot '.github/scripts/Install-VerifiedShaderCoreRelease.ps1'
        $shaderCoreInstaller = if (Test-Path -LiteralPath $shaderCoreInstallerPath -PathType Leaf) {
            (Get-Content -LiteralPath $shaderCoreInstallerPath -Raw) -replace "`r`n", "`n"
        }
        else {
            $null
        }
        $lookupActionPath = Join-Path $repositoryRoot '.github/actions/lookup-unity-editor-cache/action.yml'
        $bootstrapScriptPath = Join-Path $repositoryRoot '.github/actions/lookup-unity-editor-cache/Install-PinnedUnityCli.ps1'
        $lookupAction = if (Test-Path -LiteralPath $lookupActionPath -PathType Leaf) {
            (Get-Content -LiteralPath $lookupActionPath -Raw) -replace "`r`n", "`n"
        }
        else {
            $null
        }
        $bootstrapScript = if (Test-Path -LiteralPath $bootstrapScriptPath -PathType Leaf) {
            (Get-Content -LiteralPath $bootstrapScriptPath -Raw) -replace "`r`n", "`n"
        }
        else {
            $null
        }
        $resolverScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/Resolve-UnityEditorPath.ps1') -Raw
        $watchdogScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/UnityWatchdogProxy.ps1') -Raw
        $ciDocumentation = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/CI.md') -Raw
        $shadowDiagnostics = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Tests/Daily/Editor/PureBaseShadowObservationDiagnosticsTests.cs') -Raw

        function Get-NamedJobBlock {
            param(
                [string]$Workflow,
                [string]$Name
            )

            $match = [regex]::Match(
                $Workflow,
                "(?ms)^  $([regex]::Escape($Name)):\n.*?(?=^  [A-Za-z0-9_-]+:\n|\z)"
            )
            return $match.Value
        }

        function Get-NamedStepBlock {
            param(
                [string]$Job,
                [string]$Name
            )

            $match = [regex]::Match(
                $Job,
                "(?ms)^      - name: $([regex]::Escape($Name))\n.*?(?=^      - name:|\z)"
            )
            return $match.Value
        }

        function Get-CompositeActionStepBlocks {
            param(
                [string]$Action,
                [ValidateSet('name', 'id')]
                [string]$Selector,
                [string]$Value
            )

            $actionLines = [string[]]($Action -split "\n")
            $runsIndex = [Array]::FindIndex(
                $actionLines,
                [Predicate[string]] { param($line) $line -match '^(?:runs|"runs"|''runs'')\s*:\s*$' }
            )
            if ($runsIndex -lt 0) {
                return @()
            }

            $stepsIndex = -1
            for ($index = $runsIndex + 1; $index -lt $actionLines.Count; $index++) {
                $line = $actionLines[$index]
                if ($line -match '^  (?:steps|"steps"|''steps'')\s*:\s*$') {
                    $stepsIndex = $index
                    break
                }

                if ($line -match '^\S') {
                    break
                }
            }

            if ($stepsIndex -lt 0) {
                return @()
            }

            $steps = [Collections.Generic.List[string]]::new()
            for ($index = $stepsIndex + 1; $index -lt $actionLines.Count; $index++) {
                $line = $actionLines[$index]
                if ($line -match '^ {0,2}\S') {
                    break
                }

                $steps.Add($line)
            }

            $selectorName = [regex]::Escape($Selector)
            $selectorValue = [regex]::Escape($Value)
            $selectorPattern = "(?m)^(?:    - | {6})(?:$selectorName|`"$selectorName`"|'$selectorName')[ \t]*:[ \t]*(?:$selectorValue|`"$selectorValue`"|'$selectorValue')[ \t]*(?:#.*)?$"
            return @(
                [regex]::Matches(($steps -join "`n"), '(?ms)^    - .*?(?=^    - |\z)') |
                Where-Object {
                    $stepMappingHeader = [regex]::Split(
                        $_.Value,
                        '(?m)^ {6}(?:run|"run"|''run'')[ \t]*:[ \t]*\|[-+]?[ \t]*(?:#.*)?$',
                        2
                    )[0]
                    $stepMappingHeader -match $selectorPattern
                } |
                ForEach-Object Value
            )
        }

        function Assert-LinesInOrder {
            param(
                [string]$Block,
                [string[]]$ExpectedLines
            )

            $actualLines = [string[]]($Block -split "\n")
            $lastIndex = -1
            foreach ($expectedLine in $ExpectedLines) {
                $currentIndex = [Array]::IndexOf($actualLines, $expectedLine, $lastIndex + 1)
                $currentIndex | Should -BeGreaterThan $lastIndex
                $lastIndex = $currentIndex
            }
        }

        function Assert-LiteralRunLinesInOrder {
            param(
                [string]$Step,
                [string[]]$ExpectedLines
            )

            Assert-LinesInOrder -Block (Get-LiteralRunBlock -Step $Step) -ExpectedLines $ExpectedLines
        }

        function Get-LiteralRunBlock {
            param([string]$Step)

            $runMatch = [regex]::Match(
                $Step,
                '(?m)^ {6}(?:run|"run"|''run'')[ \t]*:[ \t]*\|[-+]?[ \t]*(?:#.*)?\r?\n(?<script>(?:(?:^ {7,}[^\r\n]*|^[ \t]*)\r?\n)*(?:^ {7,}[^\r\n]*)?)(?=^ {4}- |\z)'
            )
            if (-not $runMatch.Success) {
                throw 'The composite action step does not contain a literal run block.'
            }

            return (($runMatch.Groups['script'].Value -split "\r?\n" | ForEach-Object { $_.Trim() }) -join "`n").Trim()
        }

        function Get-StepMappingHeader {
            param([string]$Step)

            return [regex]::Split(
                $Step,
                '(?m)^ {6}(?:run|"run"|''run'')[ \t]*:[ \t]*\|[-+]?[ \t]*(?:#.*)?$',
                2
            )[0]
        }

        function Get-ActionYamlKeyOccurrences {
            param(
                [string]$Action,
                [string]$Key
            )

            $literalBlockIndent = $null
            $escapedKey = [regex]::Escape($Key)
            $keyPattern = "^(?<indent> *)(?:$escapedKey|`"$escapedKey`"|'$escapedKey')\s*:"
            $literalBlockPattern = '^(?<indent> *)(?:run|"run"|''run'')\s*:\s*[>|][-+]?\s*(?:#.*)?$'
            $occurrences = [Collections.Generic.List[string]]::new()

            foreach ($line in $Action -split "\n") {
                if ($line -notmatch '^(?<indent> *)\S') {
                    continue
                }

                $indent = $Matches['indent'].Length
                if ($null -ne $literalBlockIndent -and $indent -le $literalBlockIndent) {
                    $literalBlockIndent = $null
                }

                if ($null -eq $literalBlockIndent -and $line -match $keyPattern) {
                    $occurrences.Add($line)
                }

                if ($null -eq $literalBlockIndent -and $line -match $literalBlockPattern) {
                    $literalBlockIndent = $Matches['indent'].Length
                }
            }

            return @($occurrences)
        }

        function Get-ActionReferences {
            param([string]$Source)

            $literalBlockIndent = $null
            $references = [Collections.Generic.List[string]]::new()
            $usesPattern = '^(?<indent> *)(?:uses|"uses"|''uses'')\s*:\s*(?:(?<quote>["''])(?<reference>[^"'']+)\k<quote>|(?<reference>\S+))\s*(?:#.*)?$'
            $literalBlockPattern = '^(?<indent> *)(?:run|"run"|''run'')\s*:\s*[>|][-+]?\s*(?:#.*)?$'

            foreach ($line in $Source -split "\n") {
                if ($line -notmatch '^(?<indent> *)\S') {
                    continue
                }

                $indent = $Matches['indent'].Length
                if ($null -ne $literalBlockIndent -and $indent -le $literalBlockIndent) {
                    $literalBlockIndent = $null
                }

                if ($null -eq $literalBlockIndent -and $line -match $usesPattern) {
                    $references.Add($Matches['reference'])
                }

                if ($null -eq $literalBlockIndent -and $line -match $literalBlockPattern) {
                    $literalBlockIndent = $Matches['indent'].Length
                }
            }

            return @($references)
        }
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
        $lookupStep | Should -Match '(?m)^          unity-version: "2022\.3\.22f1"$'
        $lookupStep | Should -Match '(?m)^          cli-version: "1\.0\.0-beta\.3"$'
        $lookupStep | Should -Match '(?m)^          cli-sha256: "ff9ef81ade1063041d25e2c549cc7ed14e96d446f4204400bf101b389f7b8502"$'
        $lookupStep | Should -Not -Match '(?m)^          cli-channel:'
        $fallbackStep | Should -Match "(?m)^        if: steps\.unity-cache-lookup\.outputs\.cache-hit != 'true'$"
        $fallbackStep | Should -Match '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@e0f32f7e273329bbe99af5bf5809bf1056935556$'
        $fallbackStep | Should -Match '(?m)^          unity-version: "2022\.3\.22f1"$'
        $fallbackStep | Should -Match '(?m)^          cli-version: "1\.0\.0-beta\.3"$'
        $fallbackStep | Should -Match '(?m)^          cli-channel: beta$'
        $fallbackStep | Should -Match '(?m)^          cache: "true"$'
        ([regex]::Matches($preparationJob, '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@')).Count | Should -Be 1
        $dailyWorkflow.Contains('throw "Unity Editor cache was not available') | Should -BeFalse
        $releaseWorkflow.Contains('throw "Unity Editor cache was not available') | Should -BeFalse
    }

    It 'keeps exactly one unconditional pinned setup call in each Unity execution job' -ForEach @(
        @{ WorkflowName = 'Daily'; Job = 'unity-daily' },
        @{ WorkflowName = 'Release validation'; Job = 'validate' },
        @{ WorkflowName = 'Release publishing'; Job = 'release' }
    ) {
        $Workflow = switch ($WorkflowName) {
            'Daily' { $dailyWorkflow }
            'Release validation' { $releaseWorkflow }
            'Release publishing' { $releasePublishingWorkflow }
        }
        $executionJob = Get-NamedJobBlock -Workflow $Workflow -Name $Job
        $setupStep = Get-NamedStepBlock -Job $executionJob -Name 'Restore Unity 2022.3.22f1'

        $executionJob | Should -Not -BeNullOrEmpty
        ([regex]::Matches($executionJob, '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@')).Count | Should -Be 1
        $setupStep | Should -Not -Match '(?m)^        if:'
        $setupStep | Should -Match '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@e0f32f7e273329bbe99af5bf5809bf1056935556$'
        $setupStep | Should -Match '(?m)^          unity-version: "2022\.3\.22f1"$'
        $setupStep | Should -Match '(?m)^          cli-version: "1\.0\.0-beta\.3"$'
        $setupStep | Should -Match '(?m)^          cli-channel: beta$'
        $setupStep | Should -Match '(?m)^          cache: "true"$'
    }

    It 'preserves top-level read-only workflow permissions' {
        foreach ($workflow in @($dailyWorkflow, $releaseWorkflow, $releasePublishingWorkflow)) {
            $workflow | Should -Match '(?m)^permissions:\n  contents: read$'
        }
    }

    It 'permits only SHA-pinned external actions and the planned local helper reference' {
        $sources = @($dailyWorkflow, $releaseWorkflow, $releasePublishingWorkflow)
        if ($null -ne $lookupAction) {
            $sources += $lookupAction
        }

        $references = @($sources | ForEach-Object { Get-ActionReferences -Source $_ })
        ($references | Where-Object { $_ -eq './.github/actions/lookup-unity-editor-cache' }).Count | Should -Be 3
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

    It 'passes a real Unity.exe only to audited release validation' {
        $releaseWorkflow.Contains('-RealEditorPathOutputFile $realEditorPathFile') | Should -BeTrue
        $releaseWorkflow.Contains('REAL_UNITY_EDITOR_PATH=$realEditorPath') | Should -BeTrue
        $releaseWorkflow.Contains('-UnityEditorPath $env:REAL_UNITY_EDITOR_PATH') | Should -BeTrue
        $releaseWorkflow.Contains('& $env:UNITY_EDITOR_PATH `') | Should -BeTrue
        $resolverScript.Contains('RealEditorPathOutputFile') | Should -BeTrue
        $resolverScript.Contains('real Unity.exe because its audited runner intentionally rejects wrapper executables') | Should -BeTrue
    }

    It 'uses hosted Unity cache, licensing, and resolved editor paths for release publishing' {
        $preparationJob = Get-NamedJobBlock -Workflow $releasePublishingWorkflow -Name 'unity-editor-cache'
        $releaseJob = Get-NamedJobBlock -Workflow $releasePublishingWorkflow -Name 'release'
        $checkoutStep = Get-NamedStepBlock -Job $preparationJob -Name 'Checkout cache lookup helper'
        $lookupStep = Get-NamedStepBlock -Job $preparationJob -Name 'Look up Unity Editor cache'
        $fallbackStep = Get-NamedStepBlock -Job $preparationJob -Name 'Restore or create Unity Editor cache'
        $activationStep = Get-NamedStepBlock -Job $releaseJob -Name 'Activate Unity Personal license'
        $waitStep = Get-NamedStepBlock -Job $releaseJob -Name 'Wait before retrying Unity license activation'
        $retryStep = Get-NamedStepBlock -Job $releaseJob -Name 'Retry Unity Personal license activation'
        $pathStep = Get-NamedStepBlock -Job $releaseJob -Name 'Export Unity editor paths'

        $preparationJob | Should -Match '(?m)^    runs-on: windows-2022$'
    $checkoutStep | Should -Match '(?m)^        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1(?: #.*)?$'
    $checkoutStep | Should -Match '(?m)^          ref: \$\{\{ github\.sha \}\}$'
    $checkoutStep | Should -Match '(?m)^          persist-credentials: false$'
        $lookupStep | Should -Match '(?m)^        uses: \./\.github/actions/lookup-unity-editor-cache$'
        $lookupStep | Should -Match '(?m)^          unity-version: "2022\.3\.22f1"$'
        $lookupStep | Should -Match '(?m)^          cli-version: "1\.0\.0-beta\.3"$'
        $lookupStep | Should -Match '(?m)^          cli-sha256: "ff9ef81ade1063041d25e2c549cc7ed14e96d446f4204400bf101b389f7b8502"$'
    $lookupStep | Should -Not -Match '(?m)^          cli-channel:'
    $fallbackStep | Should -Match '(?m)^        id: unity$'
        $fallbackStep | Should -Match "(?m)^        if: steps\.unity-cache-lookup\.outputs\.cache-hit != 'true'$"
        $fallbackStep | Should -Match '(?m)^        uses: yamachu/unity-cli-actions/setup-unity-cli@e0f32f7e273329bbe99af5bf5809bf1056935556$'
    $fallbackStep | Should -Match '(?m)^          unity-version: "2022\.3\.22f1"$'
    $fallbackStep | Should -Match '(?m)^          cli-version: "1\.0\.0-beta\.3"$'
        $fallbackStep | Should -Match '(?m)^          cli-channel: beta$'
        $fallbackStep | Should -Match '(?m)^          cache: "true"$'

        $releaseJob | Should -Match '(?m)^    needs: unity-editor-cache$'
        $releaseJob | Should -Match '(?m)^    environment: release$'
        $releaseJob | Should -Match '(?m)^    runs-on: windows-2022$'
        $releaseJob | Should -Match '(?m)^      group: pure-base-unity-personal-license$'
        $releaseJob | Should -Match '(?m)^      cancel-in-progress: false$'
        $releaseJob | Should -Not -Match 'self-hosted'
        $releaseJob | Should -Not -Match [regex]::Escape('C:\Program Files\Unity\Hub\Editor\2022.3.22f1\Editor\Unity.exe')

        $activationStep | Should -Match '(?m)^        continue-on-error: true$'
        $activationStep | Should -Match '(?m)^        uses: buildalon/activate-unity-license@e0d245d0787b7b9931b56ccbde3b508f6b70f1af(?: #.*)?$'
        $activationStep | Should -Match '(?m)^          license: personal$'
        $activationStep | Should -Match '(?m)^          username: \$\{\{ secrets\.UNITY_EMAIL \}\}$'
        $activationStep | Should -Match '(?m)^          password: \$\{\{ secrets\.UNITY_PASSWORD \}\}$'
        $activationStep | Should -Match '(?m)^          license-version: "6\.x"$'
        $waitStep | Should -Match "(?m)^        if: steps\.unity_license\.outcome == 'failure'$"
        $waitStep | Should -Match '(?m)^        run: Start-Sleep -Seconds 45$'
        $retryStep | Should -Match "(?m)^        if: steps\.unity_license\.outcome == 'failure'$"
        $retryStep | Should -Match '(?m)^        uses: buildalon/activate-unity-license@e0d245d0787b7b9931b56ccbde3b508f6b70f1af(?: #.*)?$'
        $retryStep | Should -Match '(?m)^          license: personal$'
        $retryStep | Should -Match '(?m)^          username: \$\{\{ secrets\.UNITY_EMAIL \}\}$'
        $retryStep | Should -Match '(?m)^          password: \$\{\{ secrets\.UNITY_PASSWORD \}\}$'
        $retryStep | Should -Match '(?m)^          license-version: "6\.x"$'
        $pathStep | Should -Match '-RealEditorPathOutputFile \$realEditorPathFile'
        $pathStep | Should -Match 'UNITY_EDITOR_PATH=\$watchdogEditorPath'
        $pathStep | Should -Match 'REAL_UNITY_EDITOR_PATH=\$realEditorPath'
        $releaseJob | Should -Match '-UnityEditorPath \$env:REAL_UNITY_EDITOR_PATH'
    }

    It 'runs release validation before Unity project configuration' {
        $validationJob = Get-NamedJobBlock -Workflow $releaseWorkflow -Name 'validate'

        $validationJob | Should -Not -BeNullOrEmpty
        Assert-LinesInOrder -Block $validationJob -ExpectedLines @(
            '      - name: Prepare Unity project shell',
            '      - name: Run release validation',
            '      - name: Configure Unity project',
            '      - name: Export versioned validation ZIP'
        )
    }

    It 'stages the same verified Shader-Core release asset in every hosted consumer' {
        $expectedUrl = 'https://github.com/lilxyzw/Shader-Core/releases/download/0.1.9/jp.lilxyzw.shadercore-0.1.9.zip'
        $expectedSha256 = 'fe303273fd653a44d2dc1b746cec587c07fcec3e2777409549b71a2ed742f5ed'
        $consumers = @(
            [pscustomobject]@{ Workflow = $dailyWorkflow; Job = 'unity-daily' },
            [pscustomobject]@{ Workflow = $releaseWorkflow; Job = 'validate' },
            [pscustomobject]@{ Workflow = $releasePublishingWorkflow; Job = 'release' }
        )

        $shaderCoreInstaller | Should -Not -BeNullOrEmpty
        $shaderCoreInstaller | Should -Match ([regex]::Escape($expectedUrl))
        $shaderCoreInstaller | Should -Match ([regex]::Escape($expectedSha256))

        foreach ($consumer in $consumers) {
            $job = Get-NamedJobBlock -Workflow $consumer.Workflow -Name $consumer.Job
            $step = Get-NamedStepBlock -Job $job -Name 'Install verified Shader-Core 0.1.9 release'

            $step | Should -Not -BeNullOrEmpty
            $step | Should -Match 'Install-VerifiedShaderCoreRelease\.ps1'
            $step | Should -Match ([regex]::Escape($expectedUrl))
            $step | Should -Match ([regex]::Escape($expectedSha256))
            $step | Should -Not -Match 'actions/checkout|repository:\s*lilxyzw/Shader-Core'
        }
    }

    It 'uses pwsh-compatible retried runtime downloads' {
        $resolverScript.Contains("`$ProgressPreference = 'SilentlyContinue'") | Should -BeTrue
        $resolverScript.Contains('function Invoke-DownloadWithRetry') | Should -BeTrue
        $resolverScript.Contains('MaximumAttempts = 3') | Should -BeTrue
        $resolverScript.Contains('UseBasicParsing') | Should -BeFalse
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

        try {
            New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null
            Set-Content -LiteralPath $fixturePath -Value 'not an executable'

            $childOutput = & pwsh -NoLogo -NoProfile -NonInteractive `
                -File (Join-Path $repositoryRoot '.github/scripts/UnityWatchdogProxy.ps1') `
                -UnityEditorPath $fixturePath `
                -logFile $logPath 2>&1 | Out-String
            $exitCode = $LASTEXITCODE

            $exitCode | Should -Be 1
            Test-Path -LiteralPath $diagnosticPath -PathType Leaf | Should -BeTrue
            $diagnostic = Get-Content -LiteralPath $diagnosticPath -Raw
            $diagnostic | Should -Match '(?m)^Exception=Exception calling "Start"'
            $childOutput | Should -Not -Match '(?i)(No process associated with this object|Process has not been started|The Process object must have an associated process|process.*not.*started)'
        }
        finally {
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
