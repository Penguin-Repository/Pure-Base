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

# Loads text fixtures and YAML selectors into each calling test's run scope.
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$dailyWorkflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw) -replace "`r`n", "`n"
$automationTestsWorkflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/automation-tests.yml') -Raw) -replace "`r`n", "`n"
$releaseWorkflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release-validation.yml') -Raw) -replace "`r`n", "`n"
$releasePublishingWorkflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release.yml') -Raw) -replace "`r`n", "`n"
$releaseAutomationScript = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/Invoke-PureBaseRelease.ps1') -Raw) -replace "`r`n", "`n"
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

function Get-CompositeRunsSteps {
    param([string]$Action)

    $actionLines = [string[]]($Action -split "\n")
    $runsIndex = [Array]::FindIndex(
        $actionLines,
        [Predicate[string]] { param($line) $line -match '^(?:runs|"runs"|''runs'')\s*:\s*$' }
    )
    if ($runsIndex -lt 0) { return @() }
    $stepsIndex = -1
    for ($index = $runsIndex + 1; $index -lt $actionLines.Count; $index++) {
        $line = $actionLines[$index]
        if ($line -match '^  (?:steps|"steps"|''steps'')\s*:\s*$') {
            $stepsIndex = $index
            break
        }
        if ($line -match '^\S') { break }
    }
    if ($stepsIndex -lt 0) { return @() }
    $steps = [Collections.Generic.List[string]]::new()
    for ($index = $stepsIndex + 1; $index -lt $actionLines.Count; $index++) {
        $line = $actionLines[$index]
        if ($line -match '^ {0,2}\S') { break }
        $steps.Add($line)
    }
    return $steps
}

function Get-CompositeActionStepBlocks {
    param(
        [string]$Action,
        [ValidateSet('name', 'id')]
        [string]$Selector,
        [string]$Value
    )

    $steps = @(Get-CompositeRunsSteps -Action $Action)
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