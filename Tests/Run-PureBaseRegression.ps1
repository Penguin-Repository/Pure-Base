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

# Runs the read-only daily Pure-Base regression lane or explicitly initializes fixed Shader-Core test hosts.

[CmdletBinding()]
param(
    [ValidateSet('Daily', 'Initialize', 'Smoke')]
    [string]$Mode = 'Daily',
    [string]$UnityEditorPath,
    [string]$ArtifactDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DailyTestAssembly = 'PureBase.Tests.Daily'
$StrictDailyBaselineTestCase = 'PureBase.Tests.Daily.PureBaseValidationSceneRegressionTests.CanonicalSceneMatchesCommittedBirpBaseline'
$InitializerExecutionMethod = 'PureBase.Tests.Regeneration.ShaderCoreTestStateInitializer.InitializeForBatchMode'
$ProjectSettingsRelativePath = 'ProjectSettings/jp.lilxyzw.shadercore.asset'

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathContainedBy {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ParentPath
    )

    $normalizedPath = Get-NormalizedPath -Path $Path
    $normalizedParentPath = Get-NormalizedPath -Path $ParentPath
    return $normalizedPath.Equals($normalizedParentPath, [System.StringComparison]::OrdinalIgnoreCase) -or
    $normalizedPath.StartsWith($normalizedParentPath + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-PackageGitRoot {
    $packagePath = Split-Path -Parent $PSScriptRoot
    $gitCommand = Get-Command git -ErrorAction SilentlyContinue
    if ($null -eq $gitCommand) {
        throw 'Git is required to snapshot the tracked Pure-Base package tree.'
    }

    $gitRoot = (& $gitCommand.Source -C $packagePath rev-parse --show-toplevel 2>&1)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($gitRoot -join '').Trim())) {
        throw "The runner must be located beneath a Git-tracked package. Could not resolve a Git root from '$packagePath'."
    }

    return Get-NormalizedPath -Path (($gitRoot -join "`n").Trim())
}

function Get-ProjectRoot {
    param([Parameter(Mandatory = $true)][string]$PackageGitRoot)

    $directory = [System.IO.DirectoryInfo](Get-NormalizedPath -Path $PackageGitRoot)
    while ($null -ne $directory) {
        if (Test-Path -LiteralPath (Join-Path $directory.FullName 'ProjectSettings') -PathType Container) {
            return $directory.FullName
        }

        $directory = $directory.Parent
    }

    throw "Could not find a Unity project root containing ProjectSettings above package Git root '$PackageGitRoot'."
}

function Get-RequiredUnityVersion {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $projectVersionPath = Join-Path $ProjectRoot 'ProjectSettings/ProjectVersion.txt'
    if (-not (Test-Path -LiteralPath $projectVersionPath -PathType Leaf)) {
        throw "Unity project version file '$projectVersionPath' was not found."
    }

    $versionLine = Get-Content -LiteralPath $projectVersionPath | Where-Object { $_ -like 'm_EditorVersion:*' } | Select-Object -First 1
    if ($null -eq $versionLine) {
        throw "Unity project version file '$projectVersionPath' does not define m_EditorVersion."
    }

    return $versionLine.Split(':', 2)[1].Trim()
}

function Assert-ProjectAvailableForBatchMode {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $lockFilePath = Join-Path $ProjectRoot 'Temp/UnityLockfile'
    if (Test-Path -LiteralPath $lockFilePath -PathType Leaf) {
        throw "Unity project '$ProjectRoot' appears to be open. Close the Unity Editor before running this batchmode runner."
    }
}

function Resolve-UnityEditor {
    param(
        [Parameter(Mandatory = $true)][string]$RequiredVersion,
        [AllowEmptyString()][string]$ConfiguredPath
    )

    $candidatePath = $ConfiguredPath
    if ([string]::IsNullOrWhiteSpace($candidatePath)) {
        $candidatePath = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) "Unity\Hub\Editor\$RequiredVersion\Editor\Unity.exe"
    }

    if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
        throw "Unity Editor '$RequiredVersion' was not found at '$candidatePath'. Specify -UnityEditorPath with the matching Unity.exe."
    }

    $unityPath = Get-NormalizedPath -Path $candidatePath
    $unityDirectory = [System.IO.DirectoryInfo](Split-Path -Parent $unityPath)
    while ($null -ne $unityDirectory) {
        if ($unityDirectory.Name -eq $RequiredVersion) {
            return $unityPath
        }

        $unityDirectory = $unityDirectory.Parent
    }

    throw "Unity editor path '$unityPath' cannot be proven compatible with required version '$RequiredVersion'. Use an editor path below a '$RequiredVersion' directory."
}

function Resolve-ArtifactDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$PackageGitRoot,
        [AllowEmptyString()][string]$ConfiguredPath
    )

    $artifactRoot = $ConfiguredPath
    if ([string]::IsNullOrWhiteSpace($artifactRoot)) {
        $artifactRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'PureBase.Regression.Artifacts'
    }

    $artifactRoot = Get-NormalizedPath -Path $artifactRoot
    if ((Test-PathContainedBy -Path $artifactRoot -ParentPath $PackageGitRoot) -or (Test-PathContainedBy -Path $PackageGitRoot -ParentPath $artifactRoot)) {
        throw "Artifact directory '$artifactRoot' must be outside the package Git root '$PackageGitRoot'."
    }

    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    return $artifactRoot
}

function Get-TrackedPackageFiles {
    param([Parameter(Mandatory = $true)][string]$PackageGitRoot)

    $trackedFiles = @(& git -C $PackageGitRoot -c core.quotepath=false ls-files --cached 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not enumerate tracked files under package Git root '$PackageGitRoot'."
    }

    if ($trackedFiles.Count -eq 0) {
        throw "Package Git root '$PackageGitRoot' has no tracked files to snapshot."
    }

    return @($trackedFiles | ForEach-Object { $_.ToString() } | Sort-Object)
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Tracked file '$Path' is missing from the working tree."
    }

    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            return ([System.BitConverter]::ToString($hasher.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $hasher.Dispose()
    }
}

function Get-Sha256ForText {
    param([Parameter(Mandatory = $true)][string]$Text)

    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([System.BitConverter]::ToString($hasher.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Get-ProtectedStateSnapshot {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$PackageGitRoot
    )

    $projectSettingsPath = Join-Path $ProjectRoot $ProjectSettingsRelativePath
    $trackedFiles = Get-TrackedPackageFiles -PackageGitRoot $PackageGitRoot
    $treeEntries = foreach ($relativePath in $trackedFiles) {
        $fullPath = Join-Path $PackageGitRoot $relativePath
        "$relativePath`0$(Get-FileSha256 -Path $fullPath)"
    }

    return [pscustomobject]@{
        ProjectSettingsHash    = Get-FileSha256 -Path $projectSettingsPath
        PackageTrackedTreeHash = Get-Sha256ForText -Text ($treeEntries -join "`n")
        TrackedFileCount       = $trackedFiles.Count
    }
}

function Assert-ProtectedStateUnchanged {
    param(
        [Parameter(Mandatory = $true)]$Before,
        [Parameter(Mandatory = $true)]$After
    )

    $changes = @()
    if ($Before.ProjectSettingsHash -ne $After.ProjectSettingsHash) {
        $changes += $ProjectSettingsRelativePath
    }

    if ($Before.PackageTrackedTreeHash -ne $After.PackageTrackedTreeHash -or $Before.TrackedFileCount -ne $After.TrackedFileCount) {
        $changes += 'package Git tracked tree'
    }

    if ($changes.Count -gt 0) {
        throw "Daily mode changed protected state: $($changes -join ', ')."
    }
}

function New-DailyUnityArguments {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$ResultsPath,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    return @(
        '-batchmode', '-force-d3d11',
        '-projectPath', $ProjectRoot,
        '-runTests', '-testPlatform', 'EditMode',
        '-assemblyNames', $DailyTestAssembly,
        '-testResults', $ResultsPath,
        '-logFile', $LogPath
    )
}

function New-InitializeUnityArguments {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$LogPath
    )

    return @(
        '-batchmode', '-force-d3d11',
        '-projectPath', $ProjectRoot,
        '-executeMethod', $InitializerExecutionMethod,
        '-quit',
        '-logFile', $LogPath
    )
}

function Test-NUnitResult {
    param([Parameter(Mandatory = $true)][string]$ResultsPath)

    if (-not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
        return $false
    }

    [xml]$results = Get-Content -LiteralPath $ResultsPath -Raw
    $testRun = $results.SelectSingleNode('/test-run')
    if ($null -eq $testRun) {
        return $false
    }

    $total = 0
    [void][int]::TryParse($testRun.GetAttribute('total'), [ref]$total)
    $assemblySuites = @($results.SelectNodes("//test-suite[@type='Assembly']"))
    $assemblyNames = @($assemblySuites | ForEach-Object { $_.GetAttribute('name') })
    $expectedAssemblyNames = @($DailyTestAssembly, "$DailyTestAssembly.dll")
    if ($assemblyNames.Count -ne 1 -or $assemblyNames[0] -notin $expectedAssemblyNames) {
        throw "Daily NUnit evidence must contain only '$DailyTestAssembly'. Found: $($assemblyNames -join ', ')."
    }

    $strictTestCases = @($results.SelectNodes("//test-case[@fullname='$StrictDailyBaselineTestCase']"))
    if ($strictTestCases.Count -ne 1) {
        throw "Daily NUnit evidence must contain exactly one strict baseline testcase '$StrictDailyBaselineTestCase'. Found: $($strictTestCases.Count)."
    }

    $strictTestResult = $strictTestCases[0].GetAttribute('result')
    if ($strictTestResult -ne 'Passed') {
        throw "Daily NUnit evidence must report strict baseline testcase '$StrictDailyBaselineTestCase' as Passed. Found result: '$strictTestResult'."
    }

    Write-Host "Daily NUnit summary: assembly=$($assemblyNames[0]) total=$total passed=$($testRun.GetAttribute('passed')) failed=$($testRun.GetAttribute('failed')) skipped=$($testRun.GetAttribute('skipped')) inconclusive=$($testRun.GetAttribute('inconclusive'))"
    return $testRun.GetAttribute('result') -eq 'Passed' -and $total -gt 0
}

function Invoke-UnityProcess {
    param(
        [Parameter(Mandatory = $true)][string]$UnityEditor,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$ProcessLogPath,
        [Parameter(Mandatory = $true)][string]$RunDescription
    )

    [System.IO.File]::WriteAllText($ProcessLogPath, [string]::Empty)
    & $UnityEditor @Arguments 2>&1 | Tee-Object -FilePath $ProcessLogPath -Append | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Unity $RunDescription failed with exit code $LASTEXITCODE. Process log: '$ProcessLogPath'."
    }
}

function Assert-SmokeContract {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$PackageGitRoot
    )

    $dailyArguments = New-DailyUnityArguments -ProjectRoot $ProjectRoot -ResultsPath 'nunit.xml' -LogPath 'unity.log'
    $assemblyNamesIndex = [Array]::IndexOf($dailyArguments, '-assemblyNames')
    if ($assemblyNamesIndex -lt 0 -or $dailyArguments[$assemblyNamesIndex + 1] -ne $DailyTestAssembly -or $dailyArguments -contains '-testFilter' -or $dailyArguments -contains '-executeMethod') {
        throw 'Daily command composition must select only PureBase.Tests.Daily with -assemblyNames and must not execute an initializer.'
    }

    $initializeArguments = New-InitializeUnityArguments -ProjectRoot $ProjectRoot -LogPath 'initialize.log'
    $executeMethodIndex = [Array]::IndexOf($initializeArguments, '-executeMethod')
    if ($executeMethodIndex -lt 0 -or $initializeArguments[$executeMethodIndex + 1] -ne $InitializerExecutionMethod -or $initializeArguments -contains '-runTests' -or $initializeArguments -contains '-assemblyNames') {
        throw 'Initialize command composition must execute only the initializer and must not run daily tests.'
    }

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($PSCommandPath, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        throw "Runner parse failed: $($parseErrors[0].Message)"
    }

    $initializeBranches = @($ast.EndBlock.Statements | Where-Object {
            if ($_ -isnot [System.Management.Automation.Language.IfStatementAst] -or $_.Clauses.Count -ne 1) {
                return $false
            }

            $conditionPipeline = $_.Clauses[0].Item1
            if ($conditionPipeline.PipelineElements.Count -ne 1 -or $conditionPipeline.PipelineElements[0] -isnot [System.Management.Automation.Language.CommandExpressionAst]) {
                return $false
            }

            $condition = $conditionPipeline.PipelineElements[0].Expression
            return $condition -is [System.Management.Automation.Language.BinaryExpressionAst] -and
            $condition.Operator -eq [System.Management.Automation.Language.TokenKind]::Ieq -and
            $condition.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $condition.Left.VariablePath.UserPath -eq 'Mode' -and
            $condition.Right -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
            $condition.Right.Value -eq 'Initialize'
        })
    if ($initializeBranches.Count -ne 1) {
        throw 'Runner must contain exactly one Initialize mode branch.'
    }

    $initializeBranch = $initializeBranches[0]
    $initializeBlock = $initializeBranch.Clauses[0].Item2
    $unityInvocations = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Invoke-UnityProcess'
            }, $true))
    $initializeInvocations = @($unityInvocations | Where-Object {
            $_.Extent.StartOffset -ge $initializeBlock.Extent.StartOffset -and $_.Extent.EndOffset -le $initializeBlock.Extent.EndOffset
        })
    $dailyInvocations = @($unityInvocations | Where-Object { $_ -notin $initializeInvocations })
    if ($unityInvocations.Count -ne 2 -or $initializeInvocations.Count -ne 1 -or $dailyInvocations.Count -ne 1) {
        throw 'Runner must contain exactly one dedicated Initialize Unity invocation and one Daily Unity invocation.'
    }

    $initializeArgumentBuilders = @($initializeInvocations[0].FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'New-InitializeUnityArguments'
            }, $true))
    $dailyArgumentBuilders = @($dailyInvocations[0].FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'New-DailyUnityArguments'
            }, $true))
    $initializeReturns = @($initializeBlock.FindAll({ param($node) $node -is [System.Management.Automation.Language.ReturnStatementAst] }, $true))
    if ($initializeArgumentBuilders.Count -ne 1 -or $dailyArgumentBuilders.Count -ne 1 -or $initializeReturns.Count -ne 1 -or $initializeReturns[0].Extent.StartOffset -le $initializeInvocations[0].Extent.EndOffset -or $dailyInvocations[0].Extent.StartOffset -le $initializeReturns[0].Extent.EndOffset) {
        throw 'Initialize must run its dedicated Unity process once and return before the single Daily Unity process path.'
    }

    $artifactRoot = Resolve-ArtifactDirectory -PackageGitRoot $PackageGitRoot -ConfiguredPath ''
    if (Test-PathContainedBy -Path $artifactRoot -ParentPath $PackageGitRoot) {
        throw "Default artifact directory '$artifactRoot' must be outside package Git root '$PackageGitRoot'."
    }

    $containedArtifactRootRejected = $false
    try {
        [void](Resolve-ArtifactDirectory -PackageGitRoot $PackageGitRoot -ConfiguredPath (Join-Path $PackageGitRoot '.smoke-artifacts'))
    }
    catch {
        $containedArtifactRootRejected = $true
    }
    if (-not $containedArtifactRootRejected) {
        throw 'Artifact directory resolution must reject a path contained by the package Git root.'
    }

    $invalidNUnitResultPath = Join-Path $artifactRoot ("Smoke.InvalidAssembly-$PID-$([guid]::NewGuid().ToString('N')).NUnit.xml")
    try {
        [System.IO.File]::WriteAllText($invalidNUnitResultPath, @"
<test-run total="1" passed="1" failed="0" skipped="0" inconclusive="0" result="Passed">
  <test-suite type="Assembly" name="$DailyTestAssembly" />
  <test-suite type="Assembly" name="Unexpected.Test.Assembly" />
</test-run>
"@)

        $invalidNUnitResultRejected = $false
        try {
            [void](Test-NUnitResult -ResultsPath $invalidNUnitResultPath)
        }
        catch {
            $invalidNUnitResultRejected = $true
        }
        if (-not $invalidNUnitResultRejected) {
            throw 'NUnit result validation must reject evidence containing an unexpected assembly suite.'
        }

        $strictNUnitEvidence = @(
            [pscustomobject]@{ Name = 'Passed'; TestCases = "<test-case fullname=`"$StrictDailyBaselineTestCase`" result=`"Passed`" />"; Accepted = $true }
            [pscustomobject]@{ Name = 'Missing'; TestCases = ''; Accepted = $false }
            [pscustomobject]@{ Name = 'Duplicated'; TestCases = "<test-case fullname=`"$StrictDailyBaselineTestCase`" result=`"Passed`" /><test-case fullname=`"$StrictDailyBaselineTestCase`" result=`"Passed`" />"; Accepted = $false }
            [pscustomobject]@{ Name = 'Skipped'; TestCases = "<test-case fullname=`"$StrictDailyBaselineTestCase`" result=`"Skipped`" />"; Accepted = $false }
            [pscustomobject]@{ Name = 'Inconclusive'; TestCases = "<test-case fullname=`"$StrictDailyBaselineTestCase`" result=`"Inconclusive`" />"; Accepted = $false }
            [pscustomobject]@{ Name = 'Failed'; TestCases = "<test-case fullname=`"$StrictDailyBaselineTestCase`" result=`"Failed`" />"; Accepted = $false }
        )
        foreach ($strictNUnitEvidenceCase in $strictNUnitEvidence) {
            $strictNUnitResultPath = Join-Path $artifactRoot ("Smoke.Strict$($strictNUnitEvidenceCase.Name)-$PID-$([guid]::NewGuid().ToString('N')).NUnit.xml")
            try {
                [System.IO.File]::WriteAllText($strictNUnitResultPath, @"
<test-run total="1" passed="1" failed="0" skipped="0" inconclusive="0" result="Passed">
  <test-suite type="Assembly" name="$DailyTestAssembly">
    $($strictNUnitEvidenceCase.TestCases)
  </test-suite>
</test-run>
"@)

                $strictNUnitResultAccepted = $false
                try {
                    $strictNUnitResultAccepted = Test-NUnitResult -ResultsPath $strictNUnitResultPath
                }
                catch {
                    $strictNUnitResultAccepted = $false
                }

                if ($strictNUnitResultAccepted -ne $strictNUnitEvidenceCase.Accepted) {
                    throw "NUnit result validation acceptance mismatch for strict testcase evidence '$($strictNUnitEvidenceCase.Name)'."
                }
            }
            finally {
                if (Test-Path -LiteralPath $strictNUnitResultPath -PathType Leaf) {
                    [System.IO.File]::Delete($strictNUnitResultPath)
                }
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $invalidNUnitResultPath -PathType Leaf) {
            [System.IO.File]::Delete($invalidNUnitResultPath)
        }
    }

    $mutatingCommands = $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true) |
        ForEach-Object { $_.GetCommandName() } |
        Where-Object { $_ -in @('Copy-Item', 'Move-Item', 'Remove-Item') }
    if (@($mutatingCommands).Count -gt 0) {
        throw "Daily runner must not copy, move, or delete project content. Found: $($mutatingCommands -join ', ')."
    }

    $before = Get-ProtectedStateSnapshot -ProjectRoot $ProjectRoot -PackageGitRoot $PackageGitRoot
    $after = Get-ProtectedStateSnapshot -ProjectRoot $ProjectRoot -PackageGitRoot $PackageGitRoot
    Assert-ProtectedStateUnchanged -Before $before -After $after
    Write-Host "Smoke passed: assemblyNames=$DailyTestAssembly trackedFiles=$($after.TrackedFileCount) projectSettingsHash=$($after.ProjectSettingsHash) packageTrackedTreeHash=$($after.PackageTrackedTreeHash)"
}

$packageGitRoot = Get-PackageGitRoot
$projectRoot = Get-ProjectRoot -PackageGitRoot $packageGitRoot

if ($Mode -eq 'Smoke') {
    Assert-SmokeContract -ProjectRoot $projectRoot -PackageGitRoot $packageGitRoot
    exit 0
}

$artifactRoot = Resolve-ArtifactDirectory -PackageGitRoot $packageGitRoot -ConfiguredPath $ArtifactDirectory
$runDirectory = Join-Path $artifactRoot ("$Mode-" + (Get-Date -Format 'yyyyMMdd-HHmmss') + "-$PID")
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
Assert-ProjectAvailableForBatchMode -ProjectRoot $projectRoot
$unityEditor = Resolve-UnityEditor -RequiredVersion (Get-RequiredUnityVersion -ProjectRoot $projectRoot) -ConfiguredPath $UnityEditorPath

if ($Mode -eq 'Initialize') {
    $initializeLogPath = Join-Path $runDirectory 'Initialize.Unity.log'
    $initializeProcessLogPath = Join-Path $runDirectory 'Initialize.Process.log'
    Invoke-UnityProcess -UnityEditor $unityEditor -Arguments (New-InitializeUnityArguments -ProjectRoot $projectRoot -LogPath $initializeLogPath) -ProcessLogPath $initializeProcessLogPath -RunDescription 'initializer'
    foreach ($artifactPath in @($initializeLogPath, $initializeProcessLogPath)) {
        if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
            throw "Initializer did not produce required artifact '$artifactPath'."
        }
    }

    Write-Host "Pure-Base Initialize run passed. Unity logs: '$runDirectory'."
    return
}

$before = if ($Mode -eq 'Daily') { Get-ProtectedStateSnapshot -ProjectRoot $projectRoot -PackageGitRoot $packageGitRoot } else { $null }
$resultsPath = Join-Path $runDirectory 'Daily.NUnit.xml'
$logPath = Join-Path $runDirectory 'Daily.Unity.log'
$processLogPath = Join-Path $runDirectory 'Daily.Process.log'
$dailyFailure = $null
try {
    Invoke-UnityProcess -UnityEditor $unityEditor -Arguments (New-DailyUnityArguments -ProjectRoot $projectRoot -ResultsPath $resultsPath -LogPath $logPath) -ProcessLogPath $processLogPath -RunDescription "daily test assembly '$DailyTestAssembly'"
    if (-not (Test-NUnitResult -ResultsPath $resultsPath)) {
        throw "Daily test assembly '$DailyTestAssembly' did not produce a passing NUnit result. Artifacts: '$runDirectory'."
    }
}
catch {
    $dailyFailure = $_
}
finally {
    if ($Mode -eq 'Daily') {
        $after = Get-ProtectedStateSnapshot -ProjectRoot $projectRoot -PackageGitRoot $packageGitRoot
        Assert-ProtectedStateUnchanged -Before $before -After $after
        Write-Host "Daily protected state: beforeProjectSettingsHash=$($before.ProjectSettingsHash) afterProjectSettingsHash=$($after.ProjectSettingsHash) beforePackageTrackedTreeHash=$($before.PackageTrackedTreeHash) afterPackageTrackedTreeHash=$($after.PackageTrackedTreeHash)"
    }
}

if ($null -ne $dailyFailure) {
    throw $dailyFailure
}

Write-Host "Pure-Base $Mode run passed. NUnit XML and Unity logs: '$runDirectory'."
