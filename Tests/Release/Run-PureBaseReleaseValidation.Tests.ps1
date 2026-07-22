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

# Exercises runner-only evidence and immutable-manifest failure handling without Unity.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runnerPath = Join-Path $PSScriptRoot 'Run-PureBaseReleaseValidation.ps1'
$runnerSource = Get-Content -LiteralPath $runnerPath -Raw
$entryPointIndex = $runnerSource.IndexOf("`n`$packageRoot = Get-PackageGitRoot")
 $libraryStartIndex = $runnerSource.IndexOf('Set-StrictMode -Version Latest')
if ($entryPointIndex -lt 0 -or $libraryStartIndex -lt 0) {
    throw 'The runner entry point could not be isolated for the runner-only harness.'
}
$libraryPath = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseReleaseRunner-' + [guid]::NewGuid().ToString('N') + '.ps1')
[System.IO.File]::WriteAllText($libraryPath, $runnerSource.Substring($libraryStartIndex, $entryPointIndex - $libraryStartIndex), (New-Object System.Text.UTF8Encoding($false)))
. $libraryPath

function Assert-Harness {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function New-HarnessManifest {
    param(
        [Parameter(Mandatory = $true)][string]$RootHash,
        [Parameter(Mandatory = $true)][string]$Transition = 'approved'
    )

    $entries = @()
    if ($Transition -ne 'removal') {
        $entries += [ordered]@{
            path = 'ProjectSettings/jp.lilxyzw.shadercore.asset'
            sha256 = if ($Transition -eq 'existing-entry-content-mutation') { 'mutated-settings' } else { 'settings' }
        }
    }
    if ($Transition -ne 'pre-bootstrap') {
        $entries += [ordered]@{ path = 'ProjectSettings/SceneTemplateSettings.json'; sha256 = 'scene-template-settings' }
    }
    if ($Transition -eq 'unexpected-new-immutable-entry') {
        $entries += [ordered]@{
            path = 'ProjectSettings/UnexpectedBootstrapSettings.json'
            sha256 = 'unexpected-bootstrap-settings'
        }
    }

    return [ordered]@{
        schemaVersion = 1
        pathOrdering = 'System.StringComparer.Ordinal'
        immutableRoots = @('Assets', 'Packages', 'ProjectSettings', '_LocalPackages')
        excludedMutablePathPrefixes = @('Assets/Artifacts/', 'Library/')
        rootSha256 = $RootHash
        entries = $entries
        releaseZipSha256 = 'release-zip'
        shaderCore = [ordered]@{
            packageName = 'jp.lilxyzw.shadercore'
            packageVersion = '0.1.5'
            expectedIdentitySha256 = 'shader-core'
            treeSha256 = 'shader-core'
        }
    }
}

function New-HarnessStagingReceipt {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $entries = @()
    foreach ($file in Get-ChildItem -LiteralPath $ConsumerRoot -File -Recurse -Force | Sort-Object -Property FullName) {
        $destination = Get-NormalizedRelativePath -Path $file.FullName.Substring($ConsumerRoot.Length).TrimStart('\', '/')
        $entries += [ordered]@{
            destination = $destination
            sourceKind = 'consumer-scaffold'
            source = $file.FullName
            sha256 = Get-Sha256Hex -Path $file.FullName
        }
    }
    return [ordered]@{
        schemaName = 'purebase-consumer-staging-receipt'
        schemaVersion = 1
        pathOrdering = 'System.StringComparer.Ordinal'
        entries = $entries
    }
}

$script:HarnessManifestHashes = @()
$script:HarnessManifestIndex = 0
$script:HarnessBootstrapTransition = 'approved'
$script:HarnessBaselineMismatch = $false
$script:HarnessEditorGuardFailure = $false
$script:HarnessResetCalls = 0
$script:coldLibraryResetCount = 0

function Assert-EditorClosed {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    if ($script:HarnessEditorGuardFailure) {
        throw "Synthetic editor guard failure for '$ProjectRoot'."
    }
}

function Write-ConsumerContract {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot, [Parameter(Mandatory = $true)]$Contract)
}

function Reset-ConsumerLibrary {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot)

    $script:HarnessResetCalls++
    return [ordered]@{ libraryPath = (Join-Path $ConsumerRoot 'Library'); priorLibraryPresent = $true; libraryPresentAfterReset = $false }
}

function Get-ConsumerImmutableManifest {
    param([Parameter(Mandatory = $true)][string]$ConsumerRoot, [Parameter(Mandatory = $true)][string]$ZipPath, [Parameter(Mandatory = $true)][string]$ShaderCoreManifestPath)

    $callIndex = $script:HarnessManifestIndex
    $hashIndex = if ($callIndex -eq 0) { 0 } else { [Math]::Min($callIndex - 1, $script:HarnessManifestHashes.Count - 1) }
    $transition = if ($callIndex -eq 0) { 'pre-bootstrap' } elseif ($callIndex -eq 1) { $script:HarnessBootstrapTransition } else { 'approved' }
    $manifest = New-HarnessManifest -RootHash $script:HarnessManifestHashes[$hashIndex] -Transition $transition
    $script:HarnessManifestIndex++
    if ($script:HarnessBaselineMismatch -and $callIndex -gt 0) {
        $manifest.shaderCore.treeSha256 = 'unexpected-shader-core'
    }
    return $manifest
}

$fakeUnityPath = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseReleaseFakeUnity-' + [guid]::NewGuid().ToString('N') + '.cmd')
$fakeUnitySource = @'
@echo off
set RESULTS=
set LOG=
set TEST_FILTER=
:arguments
if "%~1"=="" goto run
if /I "%~1"=="-testResults" set RESULTS=%~2& shift& shift& goto arguments
if /I "%~1"=="-logFile" set LOG=%~2& shift& shift& goto arguments
if /I "%~1"=="-testFilter" set TEST_FILTER=%~2& shift& shift& goto arguments
if /I "%~1"=="-runTests" set IS_TEST=1& shift& goto arguments
shift
goto arguments
:run
if /I "%TEST_FILTER%"=="PureBase.Release.Consumer.Tests.PureBaseConsumerSceneTemplateBootstrapTests.DisposableSceneLifecycleMaterializesSceneTemplateSettings" goto bootstrap
> "%LOG%" echo synthetic Unity log
if /I "%PUREBASE_HARNESS_NUNIT_RESULT%"=="Failed" > "%RESULTS%" echo ^<test-run result="Failed" total="1" passed="0" failed="1" skipped="0" inconclusive="0"^>^<test-suite^>^<test-case fullname="Synthetic.Failure" result="Failed"^>^<failure^>^<message^>synthetic NUnit failure detail^</message^>^</failure^>^</test-case^>^</test-suite^>^</test-run^>
if /I "%PUREBASE_HARNESS_NUNIT_RESULT%"=="Failed" goto exit
if not "%RESULTS%"=="" > "%RESULTS%" echo ^<test-run result="%PUREBASE_HARNESS_NUNIT_RESULT%" total="1" passed="%PUREBASE_HARNESS_NUNIT_PASSED%" failed="%PUREBASE_HARNESS_NUNIT_FAILED%" skipped="0" inconclusive="0" /^>
:exit
if "%IS_TEST%"=="1" exit /b %PUREBASE_HARNESS_UNITY_EXIT%
exit /b %PUREBASE_HARNESS_BOOTSTRAP_EXIT%
:bootstrap
> "%LOG%" echo synthetic Unity bootstrap log
if /I "%PUREBASE_HARNESS_BOOTSTRAP_NUNIT_RESULT%"=="Failed" > "%RESULTS%" echo ^<test-run result="Failed" total="1" passed="0" failed="1" skipped="0" inconclusive="0"^>^<test-suite^>^<test-case fullname="Synthetic.BootstrapFailure" result="Failed"^>^<failure^>^<message^>synthetic bootstrap NUnit failure detail^</message^>^</failure^>^</test-case^>^</test-suite^>^</test-run^>
if /I "%PUREBASE_HARNESS_BOOTSTRAP_NUNIT_RESULT%"=="Failed" goto exit
if not "%RESULTS%"=="" > "%RESULTS%" echo ^<test-run result="Passed" total="1" passed="1" failed="0" skipped="0" inconclusive="0" /^>
exit /b %PUREBASE_HARNESS_BOOTSTRAP_EXIT%
'@
[System.IO.File]::WriteAllText($fakeUnityPath, $fakeUnitySource, (New-Object System.Text.ASCIIEncoding))

function Invoke-HarnessCase {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter()][hashtable]$Selections = @{},
        [Parameter()][switch]$SkipColdLibraryReset,
        [Parameter(Mandatory = $true)][string[]]$ManifestHashes,
        [Parameter()][switch]$BaselineMismatch,
        [Parameter()][switch]$EditorGuardFailure,
        [Parameter()][int]$BootstrapExitCode = 0,
        [Parameter()][string]$BootstrapNUnitResult = 'Passed',
        [Parameter()][int]$UnityExitCode = 0,
        [Parameter()][string]$NUnitResult = 'Passed',
        [Parameter()][int]$NUnitPassed = 1,
        [Parameter()][int]$NUnitFailed = 0,
        [Parameter()][string]$TestFilter = 'Harness',
        [Parameter()][switch]$AllowObservationEvidence,
        [Parameter()][ValidateSet('approved', 'existing-entry-content-mutation', 'unexpected-new-immutable-entry', 'removal')][string]$BootstrapTransition = 'approved',
        [Parameter()][ValidateSet('approved', 'missing', 'extra', 'hash-mismatch')][string]$StagingReceiptTransition = 'approved'
    )

    $caseRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseReleaseHarness-' + [guid]::NewGuid().ToString('N'))
    $consumerRoot = Join-Path $caseRoot 'ConsumerProject'
    New-Item -ItemType Directory -Path $consumerRoot -Force | Out-Null
        $settingsDirectory = Join-Path $consumerRoot 'ProjectSettings'
        New-Item -ItemType Directory -Path $settingsDirectory -Force | Out-Null
        $settingsPath = Join-Path $settingsDirectory 'jp.lilxyzw.shadercore.asset'
        $settingsSource = @'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &1
MonoBehaviour:
    shaderSettings:
    - shadername: PureBase/Unlit
        modules:
        -
    - shadername: PureBase/Toon
        modules:
        -
    - shadername: PureBase/PBR
        modules:
        -
    - shadername: PureBase/Hybrid
        modules:
        -
    - shadername: PureBase/Tests/FixtureRegistration
        modules:
        - jp.penguin.purebase.tests.fixture.registration
'@
        $settingsSource = $settingsSource -replace '(?m)^    (?=shaderSettings:|- shadername:)', '  '
        $settingsSource = $settingsSource -replace '(?m)^        (?=modules:|- )', '    '
        [System.IO.File]::WriteAllText($settingsPath, $settingsSource, (New-Object System.Text.UTF8Encoding($false)))
    $zipPath = Join-Path $caseRoot 'release.zip'
    $shaderCoreManifestPath = Join-Path $caseRoot 'shader-core.json'
    [System.IO.File]::WriteAllText($zipPath, 'zip')
    [System.IO.File]::WriteAllText($shaderCoreManifestPath, '{}')
    $stagingReceipt = New-HarnessStagingReceipt -ConsumerRoot $consumerRoot
    switch ($StagingReceiptTransition) {
        'missing' { Remove-Item -LiteralPath $settingsPath -Force }
        'extra' { [System.IO.File]::WriteAllText((Join-Path $consumerRoot 'extra.txt'), 'extra', (New-Object System.Text.UTF8Encoding($false))) }
        'hash-mismatch' { [System.IO.File]::WriteAllText($settingsPath, 'mismatched settings', (New-Object System.Text.UTF8Encoding($false))) }
    }
    $script:HarnessManifestHashes = $ManifestHashes
    $script:HarnessManifestIndex = 0
    $script:HarnessBootstrapTransition = $BootstrapTransition
    $script:HarnessBaselineMismatch = [bool]$BaselineMismatch
    $script:HarnessEditorGuardFailure = [bool]$EditorGuardFailure
    $script:HarnessResetCalls = 0
    $env:PUREBASE_HARNESS_BOOTSTRAP_EXIT = [string]$BootstrapExitCode
    $env:PUREBASE_HARNESS_BOOTSTRAP_NUNIT_RESULT = $BootstrapNUnitResult
    $env:PUREBASE_HARNESS_UNITY_EXIT = [string]$UnityExitCode
    $env:PUREBASE_HARNESS_NUNIT_RESULT = $NUnitResult
    $env:PUREBASE_HARNESS_NUNIT_PASSED = [string]$NUnitPassed
    $env:PUREBASE_HARNESS_NUNIT_FAILED = [string]$NUnitFailed
    $contract = [ordered]@{ runLabel = $Label; runKind = 'harness'; products = @() }
    $failure = $null
    try {
        $bootstrapManifest = Invoke-ConsumerBootstrapImport -UnityEditor $fakeUnityPath -ConsumerRoot $consumerRoot -RunRoot $caseRoot -ZipPath $zipPath -ShaderCoreManifestPath $shaderCoreManifestPath -StagingReceipt $stagingReceipt
        $summary = Invoke-ConsumerTest -UnityEditor $fakeUnityPath -ConsumerRoot $consumerRoot -RunRoot $caseRoot -ZipPath $zipPath -ShaderCoreManifestPath $shaderCoreManifestPath -Contract $contract -TestFilter $TestFilter -Selections $Selections -SkipColdLibraryReset:$SkipColdLibraryReset -AllowObservationEvidence:$AllowObservationEvidence
    }
    catch {
        $failure = $_
        $summary = $null
    }
    return [ordered]@{ root = $caseRoot; bootstrapDirectory = (Join-Path $caseRoot 'bootstrap'); runDirectory = (Join-Path $caseRoot ('runs/' + $Label)); settingsPath = $settingsPath; failure = $failure; summary = $summary; resetCalls = $script:HarnessResetCalls }
}

function New-HarnessStandardMorphContract {
    param([Parameter(Mandatory = $true)][string]$RunLabel)

    $module = [ordered]@{
        label = 'standard-morph'
        phase = 'morph'
        uniqueId = 'jp.penguin.purebase.integration.products.morph'
        propertyName = ''
        sentinel = 'PUREBASE_ALL_PRODUCT_PHASE_SENTINEL_MORPH'
    }
    $contract = New-PhaseContract -Module $module -SelectedProducts $ProductNames
    $contract.runLabel = $RunLabel
    return $contract
}

function New-HarnessGeneratedSource {
    param(
        [Parameter(Mandatory = $true)][int[]]$PassCounts,
        [Parameter()][string]$Sentinel = ''
    )

    $source = New-Object System.Text.StringBuilder
    for ($index = 0; $index -lt $ProductPasses.Count; $index++) {
        [void]$source.Append('Name "' + $ProductPasses[$index] + '"' + "`n")
        for ($count = 0; $count -lt $PassCounts[$index]; $count++) {
            [void]$source.Append($Sentinel + "`n")
        }
    }
    return $source.ToString()
}

function Write-HarnessComparisonEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)]$WarmContract,
        [Parameter(Mandatory = $true)]$ColdContract,
        [Parameter(Mandatory = $true)][hashtable]$WarmCounts,
        [Parameter(Mandatory = $true)][hashtable]$ColdCounts
    )

    $moduleFreeDirectory = Join-Path $Root 'runs/module-free-clean-import/consumer-evidence'
    $warmDirectory = Join-Path $Root ('runs/' + $WarmContract.runLabel + '/consumer-evidence')
    $coldDirectory = Join-Path $Root ('runs/' + $ColdContract.runLabel + '/consumer-evidence')
    foreach ($directory in @($moduleFreeDirectory, $warmDirectory, $coldDirectory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $products = @()
    foreach ($productName in $ProductNames) {
        $moduleFreeFileName = Get-ExpectedGeneratedSourceArtifactFileName -RunLabel 'module-free-clean-import' -ShaderName $productName
        [System.IO.File]::WriteAllText((Join-Path $moduleFreeDirectory $moduleFreeFileName), (New-HarnessGeneratedSource -PassCounts @(0, 0, 0, 0)), (New-Object System.Text.UTF8Encoding($false)))

        $warmFileName = Get-ExpectedGeneratedSourceArtifactFileName -RunLabel $WarmContract.runLabel -ShaderName $productName
        $warmPassCounts = [int[]]$WarmCounts[$productName]
        [System.IO.File]::WriteAllText((Join-Path $warmDirectory $warmFileName), (New-HarnessGeneratedSource -PassCounts $warmPassCounts -Sentinel $WarmContract.selectedModule.sentinel), (New-Object System.Text.UTF8Encoding($false)))
        $products += [ordered]@{
            shaderName = $productName
            compiled = $true
            supported = $true
            generatedSourceArtifactFileName = $warmFileName
            passCounts = @(
                [ordered]@{ passName = 'ForwardBase'; selectedSentinelCount = $warmPassCounts[0] },
                [ordered]@{ passName = 'ForwardAdd'; selectedSentinelCount = $warmPassCounts[1] },
                [ordered]@{ passName = 'ShadowCaster'; selectedSentinelCount = $warmPassCounts[2] },
                [ordered]@{ passName = 'Meta'; selectedSentinelCount = $warmPassCounts[3] }
            )
            inactiveSentinels = @($WarmContract.inactiveSentinels | ForEach-Object { [ordered]@{ sentinel = $_; occurrenceCount = 0 } })
        }

        $coldFileName = Get-ExpectedGeneratedSourceArtifactFileName -RunLabel $ColdContract.runLabel -ShaderName $productName
        [System.IO.File]::WriteAllText((Join-Path $coldDirectory $coldFileName), (New-HarnessGeneratedSource -PassCounts ([int[]]$ColdCounts[$productName]) -Sentinel $ColdContract.selectedModule.sentinel), (New-Object System.Text.UTF8Encoding($false)))
    }
    [ordered]@{
        schemaName = 'purebase-standard-morph-observation'
        schemaVersion = 1
        runLabel = $WarmContract.runLabel
        runKind = 'product-phase'
        selectedModulePhase = 'morph'
        selectedModuleUniqueId = $WarmContract.selectedModule.moduleUniqueId
        selectedModuleSentinel = $WarmContract.selectedModule.sentinel
        products = $products
    } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $warmDirectory 'standard-morph-observation.json') -Encoding UTF8
}

function Invoke-HarnessCompletionPath {
    param(
        [Parameter(Mandatory = $true)][string]$ValidationScope,
        [Parameter()][switch]$ModuleFreeOnly,
        [Parameter()][switch]$ForceRunSummaryFailure
    )

    $runRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseReleaseCompletion-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
    $consumerCreated = 1
    $consumerRemoved = 1
    $failed = $true
    $comparisonVerdict = $null
    $failure = $null
    $outcomes = @([ordered]@{ label = 'synthetic-success'; nunit = [ordered]@{ result = 'Passed' } })
    try {
        if ($ForceRunSummaryFailure) {
            New-Item -ItemType Directory -Path (Join-Path $runRoot 'run-summary.json') -Force | Out-Null
        }
        Write-ReleaseRunSummary -RunRoot $runRoot -ConsumerCreated $consumerCreated -ConsumerRemoved $consumerRemoved -ValidationScope $ValidationScope -ComparisonMode $false -ModuleFreeOnly ([bool]$ModuleFreeOnly) -Outcomes $outcomes -ComparisonVerdict $comparisonVerdict
        $failed = $false
    }
    catch {
        $failure = $_
    }
    finally {
        Write-ReleaseCleanupSummary -RunRoot $runRoot -ConsumerCreated $consumerCreated -ConsumerRemoved $consumerRemoved -KeepConsumer $false -Failed $failed
    }

    $summaryPath = Join-Path $runRoot 'run-summary.json'
    $summary = if (Test-Path -LiteralPath $summaryPath -PathType Leaf) { Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json } else { $null }
    $cleanup = Get-Content -LiteralPath (Join-Path $runRoot 'cleanup-summary.json') -Raw | ConvertFrom-Json
    return [ordered]@{ root = $runRoot; failure = $failure; summary = $summary; cleanup = $cleanup }
}

try {
    $conflictArtifactDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseReleaseConflict-' + [guid]::NewGuid().ToString('N'))
    $conflictFailure = $null
    try {
        & $runnerPath -UnityEditorPath 'not-a-unity-editor.exe' -ArtifactDirectory $conflictArtifactDirectory -ModuleFreeOnly -CompareWarmAndColdStandardMorph
    }
    catch {
        $conflictFailure = $_
    }
    Assert-Harness -Condition ($null -ne $conflictFailure) -Message 'Incompatible runner switches unexpectedly passed.'
    Assert-Harness -Condition ($conflictFailure.Exception.Message -eq '-ModuleFreeOnly cannot be combined with -CompareWarmAndColdStandardMorph because the latter requires the three-row standard-morph warm/cold comparison.') -Message 'Incompatible runner switches did not report the deterministic conflict error before Unity validation.'
    Assert-Harness -Condition (-not (Test-Path -LiteralPath $conflictArtifactDirectory)) -Message 'Incompatible runner switches created an artifact directory before failing.'

    $toonBaseConflictArtifactDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseReleaseToonBaseConflict-' + [guid]::NewGuid().ToString('N'))
    $toonBaseConflictFailure = $null
    try {
        & $runnerPath -UnityEditorPath 'not-a-unity-editor.exe' -ArtifactDirectory $toonBaseConflictArtifactDirectory -ToonBaseOnly -ModuleFreeOnly
    }
    catch {
        $toonBaseConflictFailure = $_
    }
    Assert-Harness -Condition ($null -ne $toonBaseConflictFailure) -Message 'Incompatible Toon-base runner switches unexpectedly passed.'
    Assert-Harness -Condition ($toonBaseConflictFailure.Exception.Message -eq '-ToonBaseOnly cannot be combined with -ModuleFreeOnly because it requires the Toon base product-phase row.') -Message 'Incompatible Toon-base runner switches did not report the deterministic conflict error before Unity validation.'
    Assert-Harness -Condition (-not (Test-Path -LiteralPath $toonBaseConflictArtifactDirectory)) -Message 'Incompatible Toon-base runner switches created an artifact directory before failing.'

    $selectionMatrix = @(
        [ordered]@{ label = 'module-free-clean-import' },
        [ordered]@{ label = 'unlit-forward-add-fog' },
        [ordered]@{ label = 'progressive-cpu-bake' }
    )
    $fogOnlyMatrix = Select-ValidationMatrix -Matrix $selectionMatrix -FogOnly
    Assert-Harness -Condition ($fogOnlyMatrix.Count -eq 1 -and $fogOnlyMatrix[0].label -eq 'unlit-forward-add-fog') -Message 'Fog-only matrix selection no longer returns exactly the unlit-forward-add-fog row.'
    $bakeOnlyMatrix = Select-ValidationMatrix -Matrix $selectionMatrix -BakeOnly
    Assert-Harness -Condition ($bakeOnlyMatrix.Count -eq 1 -and $bakeOnlyMatrix[0].label -eq 'progressive-cpu-bake') -Message 'Bake-only matrix selection did not return exactly the progressive-cpu-bake row.'

    foreach ($conflict in @(
        [ordered]@{ label = 'module-free'; parameters = @{ ModuleFreeOnly = $true }; message = '-BakeOnly cannot be combined with -ModuleFreeOnly because it requires the progressive-cpu-bake row.' },
        [ordered]@{ label = 'toon-base'; parameters = @{ ToonBaseOnly = $true }; message = '-BakeOnly cannot be combined with -ToonBaseOnly because it requires the progressive-cpu-bake row.' },
        [ordered]@{ label = 'fog'; parameters = @{ FogOnly = $true }; message = '-BakeOnly cannot be combined with -FogOnly because it requires the progressive-cpu-bake row.' },
        [ordered]@{ label = 'warm-cold-comparison'; parameters = @{ CompareWarmAndColdStandardMorph = $true }; message = '-BakeOnly cannot be combined with -CompareWarmAndColdStandardMorph because it requires the progressive-cpu-bake row.' }
    )) {
        $conflictArtifactDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseReleaseBakeConflict-' + $conflict.label + '-' + [guid]::NewGuid().ToString('N'))
        $runnerParameters = @{
            UnityEditorPath = 'not-a-unity-editor.exe'
            ArtifactDirectory = $conflictArtifactDirectory
            BakeOnly = $true
        }
        foreach ($parameterName in $conflict.parameters.Keys) {
            $runnerParameters[$parameterName] = $conflict.parameters[$parameterName]
        }
        $conflictFailure = $null
        try {
            & $runnerPath @runnerParameters
        }
        catch {
            $conflictFailure = $_
        }
        Assert-Harness -Condition ($null -ne $conflictFailure) -Message "Incompatible Bake-only '$($conflict.label)' runner switches unexpectedly passed."
        Assert-Harness -Condition ($conflictFailure.Exception.Message -eq $conflict.message) -Message "Incompatible Bake-only '$($conflict.label)' runner switches did not report the deterministic conflict error before Unity validation."
        Assert-Harness -Condition (-not (Test-Path -LiteralPath $conflictArtifactDirectory)) -Message "Incompatible Bake-only '$($conflict.label)' runner switches created an artifact directory before failing."
    }

    $toonPropertyMappings = @(
        [ordered]@{ phase = 'base'; uniqueId = 'jp.penguin.purebase.integration.toon.phase.base'; expectedPropertyName = '_jp_penguin_purebase_integration_toon_phase_base_ProductPhaseValue' },
        [ordered]@{ phase = 'light'; uniqueId = 'jp.penguin.purebase.integration.toon.phase.light'; expectedPropertyName = '_jp_penguin_purebase_integration_toon_phase_light_ProductPhaseValue' },
        [ordered]@{ phase = 'modifylight'; uniqueId = 'jp.penguin.purebase.integration.toon.phase.modifylight'; expectedPropertyName = '_jp_penguin_purebase_integration_toon_phase_modifylight_ProductPhaseValue' },
        [ordered]@{ phase = 'shade'; uniqueId = 'jp.penguin.purebase.integration.toon.phase.shade'; expectedPropertyName = '_jp_penguin_purebase_integration_toon_phase_shade_ProductPhaseValue' }
    )
    foreach ($mapping in $toonPropertyMappings) {
        $propertyName = Get-ShaderCoreNamespacedPropertyName -ModuleUniqueId $mapping.uniqueId -RawPropertyName '_ProductPhaseValue'
        Assert-Harness -Condition ($propertyName -eq $mapping.expectedPropertyName) -Message "Toon '$($mapping.phase)' property ABI mapping changed."
        Assert-Harness -Condition ($propertyName -ne '_ProductPhaseValue') -Message "Toon '$($mapping.phase)' contract regressed to the raw property name."
        $module = [ordered]@{ label = 'harness-toon-' + $mapping.phase; phase = $mapping.phase; uniqueId = $mapping.uniqueId; propertyName = $propertyName; sentinel = 'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_' + $mapping.phase.ToUpperInvariant() }
        $contract = New-PhaseContract -Module $module -SelectedProducts @('PureBase/Toon')
        Assert-Harness -Condition ($contract.selectedModule.propertyName -eq $mapping.expectedPropertyName) -Message "Toon '$($mapping.phase)' phase contract did not retain the visible property ABI."
    }
    $fogContract = New-FogContract
    $fogAssignmentPropertyName = $fogContract.unlitForwardAddFog.floatAssignments[0].propertyName
    Assert-Harness -Condition ($fogAssignmentPropertyName -eq '_jp_penguin_purebase_integration_unlit_forwardaddfog_ForwardAddFogSignalProperty') -Message 'Fog contract did not map its float assignment to the expected namespaced property ABI.'
    Assert-Harness -Condition ($fogAssignmentPropertyName -ne '_ForwardAddFogSignalProperty') -Message 'Fog contract regressed to the raw property name.'
    $toonBaseModule = [ordered]@{ label = 'toon-base'; phase = 'base'; uniqueId = 'jp.penguin.purebase.integration.toon.phase.base'; propertyName = '_jp_penguin_purebase_integration_toon_phase_base_ProductPhaseValue'; sentinel = 'PUREBASE_TOON_PRODUCT_PHASE_SENTINEL_BASE' }
    $toonBaseRuntimeContract = New-ToonRuntimeContract -Module $toonBaseModule
    $toonBaseRuntimeSample = $toonBaseRuntimeContract.runtimeSamples[0]
    $toonBaseRuntimeDelta = $toonBaseRuntimeContract.runtimeDelta.selectedMinusModuleFree
    Assert-Harness -Condition ($toonBaseRuntimeSample.red.minimum -eq 3.55 -and $toonBaseRuntimeSample.red.maximum -eq 3.58) -Message 'Toon base runtime absolute red range must remain the evidence-backed 3.55-3.58 interval.'
    Assert-Harness -Condition ($toonBaseRuntimeDelta.red.minimum -eq 0.70 -and $toonBaseRuntimeDelta.red.maximum -eq 0.73) -Message 'Toon base selected-minus-module-free red range must remain the evidence-backed 0.70-0.73 interval.'
    Assert-Harness -Condition ($toonBaseRuntimeSample.red.minimum -le 3.56640625 -and $toonBaseRuntimeSample.red.maximum -ge 3.56640625) -Message 'Toon base runtime absolute red range excludes the recorded BIRP readback.'
    Assert-Harness -Condition ($toonBaseRuntimeDelta.red.minimum -le 0.712890625 -and $toonBaseRuntimeDelta.red.maximum -ge 0.712890625) -Message 'Toon base selected-minus-module-free red range excludes the recorded BIRP delta.'
    Assert-Harness -Condition ($toonBaseRuntimeSample.red.maximum -lt 4.2 -and $toonBaseRuntimeDelta.red.maximum -lt 1.3) -Message 'Toon base runtime contract regressed to the direct-add red expectation.'
    foreach ($invalidAbiInput in @(
        [ordered]@{ uniqueId = ''; propertyName = '_ProductPhaseValue' },
        [ordered]@{ uniqueId = 'jp..penguin'; propertyName = '_ProductPhaseValue' },
        [ordered]@{ uniqueId = 'jp.penguin'; propertyName = 'ProductPhaseValue' }
    )) {
        $invalidAbiFailure = $null
        try {
            Get-ShaderCoreNamespacedPropertyName -ModuleUniqueId $invalidAbiInput.uniqueId -RawPropertyName $invalidAbiInput.propertyName | Out-Null
        }
        catch {
            $invalidAbiFailure = $_
        }
        Assert-Harness -Condition ($null -ne $invalidAbiFailure) -Message 'Malformed Shader-Core property ABI input unexpectedly passed.'
    }

    foreach ($completionCase in @(
        [ordered]@{ validationScope = 'full-release-validation-matrix'; moduleFreeOnly = $false },
        [ordered]@{ validationScope = 'module-free-diagnostic-only'; moduleFreeOnly = $true },
        [ordered]@{ validationScope = 'progressive-cpu-bake-diagnostic-only'; moduleFreeOnly = $false }
    )) {
        $completion = Invoke-HarnessCompletionPath -ValidationScope $completionCase.validationScope -ModuleFreeOnly:$completionCase.moduleFreeOnly
        Assert-Harness -Condition ($null -eq $completion.failure) -Message "Non-comparison completion '$($completionCase.validationScope)' unexpectedly failed under StrictMode."
        Assert-Harness -Condition ($null -ne $completion.summary) -Message "Non-comparison completion '$($completionCase.validationScope)' did not persist run-summary.json."
        Assert-Harness -Condition ($completion.summary.validationScope -eq $completionCase.validationScope) -Message "Non-comparison completion '$($completionCase.validationScope)' changed validationScope."
        Assert-Harness -Condition ($completion.summary.consumerDirectoryCreationCount -eq 1 -and $completion.summary.consumerDirectoryRemovalCount -eq 1) -Message "Non-comparison completion '$($completionCase.validationScope)' changed consumer lifecycle counts."
        Assert-Harness -Condition (-not $completion.summary.comparisonMode -and ([bool]$completion.summary.moduleFreeOnly -eq [bool]$completionCase.moduleFreeOnly)) -Message "Non-comparison completion '$($completionCase.validationScope)' changed comparison flags."
        Assert-Harness -Condition ($null -eq $completion.summary.comparisonVerdict) -Message "Non-comparison completion '$($completionCase.validationScope)' claimed a comparison verdict."
        Assert-Harness -Condition ($completion.cleanup.consumerDirectoryCreationCount -eq 1 -and $completion.cleanup.consumerDirectoryRemovalCount -eq 1) -Message "Non-comparison cleanup '$($completionCase.validationScope)' changed consumer lifecycle counts."
        Assert-Harness -Condition (-not $completion.cleanup.failed) -Message "Non-comparison completion '$($completionCase.validationScope)' wrote an incorrect cleanup failure state."
    }

    $runSummaryFailure = Invoke-HarnessCompletionPath -ValidationScope 'full-release-validation-matrix' -ForceRunSummaryFailure
    Assert-Harness -Condition ($null -ne $runSummaryFailure.failure) -Message 'Synthetic run-summary failure unexpectedly passed.'
    Assert-Harness -Condition ($runSummaryFailure.cleanup.failed) -Message 'Run-summary failure wrote cleanup-summary.json with failed=false.'

    foreach ($successfulLabel in @('module-free-clean-import', 'progressive-cpu-bake')) {
        $case = Invoke-HarnessCase -Label $successfulLabel -ManifestHashes @('bootstrap', 'bootstrap', 'row', 'row')
        Assert-Harness -Condition ($null -eq $case.failure) -Message "Successful row '$successfulLabel' unexpectedly failed."
        Assert-Harness -Condition ($case.resetCalls -eq 1) -Message "Successful row '$successfulLabel' did not reset only the bootstrap Library."
        $resetEvidence = Get-Content -LiteralPath (Join-Path $case.runDirectory 'library-reset.json') -Raw | ConvertFrom-Json
        Assert-Harness -Condition (-not $resetEvidence.required -and -not $resetEvidence.attempted -and -not $resetEvidence.completed) -Message "Successful row '$successfulLabel' unexpectedly attempted a selected-module cold Library reset."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'staging-receipt.json') -PathType Leaf) -Message "Successful row '$successfulLabel' did not persist its staging receipt."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-pre-bootstrap.json') -PathType Leaf) -Message "Successful row '$successfulLabel' did not collect the pre-bootstrap immutable manifest."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-quiescent.json') -PathType Leaf) -Message "Successful row '$successfulLabel' did not collect the canonical post-bootstrap manifest."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-bootstrap-delta.json') -PathType Leaf) -Message "Successful row '$successfulLabel' did not persist its bootstrap delta report."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-after-library-reset.json') -PathType Leaf) -Message "Successful row '$successfulLabel' did not compare immutable inputs after its bootstrap Library reset."
        $preBootstrapManifest = Get-Content -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-pre-bootstrap.json') -Raw | ConvertFrom-Json
        $bootstrapManifest = Get-Content -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-quiescent.json') -Raw | ConvertFrom-Json
        $afterResetManifest = Get-Content -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-after-library-reset.json') -Raw | ConvertFrom-Json
        $rowManifest = Get-Content -LiteralPath (Join-Path $case.runDirectory 'immutable-input-manifest-before.json') -Raw | ConvertFrom-Json
        Assert-Harness -Condition ($bootstrapManifest.rootSha256 -eq 'bootstrap' -and $rowManifest.rootSha256 -eq 'row') -Message "Successful row '$successfulLabel' did not collect its row baseline after bootstrap."
        $bootstrapCommand = Get-Content -LiteralPath (Join-Path $case.bootstrapDirectory 'unity-command.json') -Raw | ConvertFrom-Json
        Assert-Harness -Condition ($bootstrapCommand.arguments -contains 'PureBase.Release.Consumer.Tests.PureBaseConsumerSceneTemplateBootstrapTests.DisposableSceneLifecycleMaterializesSceneTemplateSettings') -Message "Successful row '$successfulLabel' did not select the scene-template bootstrap test."
        $bootstrapSceneTemplateEntry = @($bootstrapManifest.entries | Where-Object { $_.path -eq 'ProjectSettings/SceneTemplateSettings.json' })
        $preBootstrapSceneTemplateEntry = @($preBootstrapManifest.entries | Where-Object { $_.path -eq 'ProjectSettings/SceneTemplateSettings.json' })
        $afterResetSceneTemplateEntry = @($afterResetManifest.entries | Where-Object { $_.path -eq 'ProjectSettings/SceneTemplateSettings.json' })
        $bootstrapDelta = Get-Content -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-bootstrap-delta.json') -Raw | ConvertFrom-Json
        Assert-Harness -Condition ($preBootstrapSceneTemplateEntry.Count -eq 0 -and $bootstrapSceneTemplateEntry.Count -eq 1 -and $afterResetSceneTemplateEntry.Count -eq 1 -and $bootstrapSceneTemplateEntry[0].sha256 -eq 'scene-template-settings' -and $afterResetSceneTemplateEntry[0].sha256 -eq $bootstrapSceneTemplateEntry[0].sha256) -Message "Successful row '$successfulLabel' did not preserve the materialized SceneTemplateSettings entry across the Library reset."
        Assert-Harness -Condition ($bootstrapDelta.classification -eq 'observed' -and @($bootstrapDelta.added).Count -eq 1 -and $bootstrapDelta.added[0].path -eq 'ProjectSettings/SceneTemplateSettings.json' -and @($bootstrapDelta.removed).Count -eq 0 -and @($bootstrapDelta.changed).Count -eq 0) -Message "Successful row '$successfulLabel' did not report the observed bootstrap delta."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.runDirectory 'immutable-input-manifest-before.json') -PathType Leaf) -Message "Successful row '$successfulLabel' did not persist its before manifest."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.runDirectory 'immutable-input-manifest-after.json') -PathType Leaf) -Message "Successful row '$successfulLabel' did not persist its after manifest."
    }

    $deltaPreBootstrap = [ordered]@{
        rootSha256 = 'pre-root'
        entries = @(
            [ordered]@{ path = 'Assets/Changed.asset'; sha256 = 'before-change' },
            [ordered]@{ path = 'Assets/Removed.asset'; sha256 = 'removed-hash' }
        )
    }
    $deltaPostBootstrap = [ordered]@{
        rootSha256 = 'post-root'
        entries = @(
            [ordered]@{ path = 'Assets/Added.asset'; sha256 = 'added-hash' },
            [ordered]@{ path = 'Assets/Changed.asset'; sha256 = 'after-change' }
        )
    }
    $deltaReport = Get-ConsumerImmutableManifestDeltaReport -PreBootstrap $deltaPreBootstrap -PostBootstrap $deltaPostBootstrap
    $deltaReportRepeat = Get-ConsumerImmutableManifestDeltaReport -PreBootstrap $deltaPreBootstrap -PostBootstrap $deltaPostBootstrap
    $deltaReportArtifact = $deltaReport | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    Assert-ExactJsonPropertyNames -Value $deltaReportArtifact -ExpectedNames @('schemaName', 'schemaVersion', 'classification', 'pathOrdering', 'preBootstrapRootSha256', 'postBootstrapRootSha256', 'added', 'removed', 'changed') -Description 'Bootstrap delta report'
    Assert-Harness -Condition ($deltaReport.schemaName -eq 'purebase-immutable-manifest-bootstrap-delta' -and $deltaReport.schemaVersion -eq 1 -and $deltaReport.classification -eq 'observed' -and $deltaReport.pathOrdering -eq 'System.StringComparer.Ordinal' -and $deltaReport.preBootstrapRootSha256 -eq 'pre-root' -and $deltaReport.postBootstrapRootSha256 -eq 'post-root') -Message 'Bootstrap delta report schema changed.'
    Assert-Harness -Condition (@($deltaReport.added).Count -eq 1 -and $deltaReport.added[0].path -eq 'Assets/Added.asset' -and $deltaReport.added[0].sha256 -eq 'added-hash') -Message 'Bootstrap delta report omitted the added path hash.'
    Assert-Harness -Condition (@($deltaReport.removed).Count -eq 1 -and $deltaReport.removed[0].path -eq 'Assets/Removed.asset' -and $deltaReport.removed[0].sha256 -eq 'removed-hash') -Message 'Bootstrap delta report omitted the removed path hash.'
    Assert-Harness -Condition (@($deltaReport.changed).Count -eq 1 -and $deltaReport.changed[0].path -eq 'Assets/Changed.asset' -and $deltaReport.changed[0].preBootstrapSha256 -eq 'before-change' -and $deltaReport.changed[0].postBootstrapSha256 -eq 'after-change') -Message 'Bootstrap delta report omitted the changed path hashes.'
    Assert-Harness -Condition (($deltaReport | ConvertTo-Json -Depth 8 -Compress) -eq ($deltaReportRepeat | ConvertTo-Json -Depth 8 -Compress)) -Message 'Bootstrap delta report ordering is not deterministic.'

    foreach ($receiptCase in @(
        [ordered]@{ label = 'missing'; expectedMessage = 'Staged consumer destination is missing' },
        [ordered]@{ label = 'extra'; expectedMessage = 'Staged consumer destination is extra' },
        [ordered]@{ label = 'hash-mismatch'; expectedMessage = 'Staged consumer destination content mismatches' }
    )) {
        $case = Invoke-HarnessCase -Label ('staging-receipt-' + $receiptCase.label) -ManifestHashes @('bootstrap') -StagingReceiptTransition $receiptCase.label
        Assert-Harness -Condition ($null -ne $case.failure -and $case.failure.Exception.Message -match $receiptCase.expectedMessage) -Message "Staging receipt '$($receiptCase.label)' unexpectedly passed or reported the wrong failure."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'staging-receipt.json') -PathType Leaf) -Message "Staging receipt '$($receiptCase.label)' did not persist its receipt."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'failure-evidence/staging-receipt.json') -PathType Leaf) -Message "Staging receipt '$($receiptCase.label)' did not retain receipt failure evidence."
        Assert-Harness -Condition (-not (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'unity-command.json') -PathType Leaf)) -Message "Staging receipt '$($receiptCase.label)' launched Unity before rejecting the staged consumer."
    }

    $fixturePreservation = Invoke-HarnessCase -Label 'fixture-preservation' -Selections @{ 'PureBase/Unlit' = @('jp.penguin.purebase.integration.module') } -ManifestHashes @('bootstrap', 'bootstrap', 'row', 'row', 'row')
    Assert-Harness -Condition ($null -eq $fixturePreservation.failure) -Message 'Fixture registration preservation case unexpectedly failed.'
    $settingsText = Get-Content -LiteralPath $fixturePreservation.settingsPath -Raw
    Assert-Harness -Condition ($settingsText -match '(?ms)^  - shadername: PureBase/Tests/FixtureRegistration\r?\n    modules:\r?\n    - jp\.penguin\.purebase\.tests\.fixture\.registration\r?$') -Message 'Shader-Core fixture registration was not preserved.'
    Assert-Harness -Condition ($settingsText -match '(?ms)^  - shadername: PureBase/Unlit\r?\n    modules:\r?\n    - jp\.penguin\.purebase\.integration\.module\r?$') -Message 'Shader-Core product module selection was not applied.'

    $unityFailure = Invoke-HarnessCase -Label 'unity-exit-failure' -ManifestHashes @('bootstrap', 'bootstrap', 'row', 'row') -UnityExitCode 17 -NUnitResult 'Failed' -NUnitPassed 0 -NUnitFailed 1
    $nunitFailure = Invoke-HarnessCase -Label 'nunit-failure' -ManifestHashes @('bootstrap', 'bootstrap', 'row', 'row') -NUnitResult 'Failed' -NUnitPassed 0 -NUnitFailed 1
    foreach ($case in @($unityFailure, $nunitFailure)) {
        Assert-Harness -Condition ($null -ne $case.failure) -Message "Failure row '$($case.runDirectory)' unexpectedly passed."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.runDirectory 'immutable-input-manifest-after.json') -PathType Leaf) -Message "Failure row '$($case.runDirectory)' did not persist its after manifest."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.runDirectory 'failure.json') -PathType Leaf) -Message "Failure row '$($case.runDirectory)' did not persist failure.json."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.runDirectory 'failure-evidence/immutable-input-manifest-after.json') -PathType Leaf) -Message "Failure row '$($case.runDirectory)' did not preserve the after manifest as recoverable evidence."
    }
    Assert-Harness -Condition ($unityFailure.failure.Exception.Message -match 'NUnit failure: synthetic NUnit failure detail') -Message 'Nonzero Unity exit did not surface available NUnit failure detail.'

    $editorGuardFailure = Invoke-HarnessCase -Label 'editor-guard-failure' -ManifestHashes @('bootstrap') -EditorGuardFailure
    foreach ($case in @($editorGuardFailure)) {
        Assert-Harness -Condition ($null -ne $case.failure) -Message "Preflight failure row '$($case.runDirectory)' unexpectedly passed."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'failure.json') -PathType Leaf) -Message "Preflight failure row '$($case.bootstrapDirectory)' did not persist bootstrap failure.json."
    }

    $bootstrapProcessFailure = Invoke-HarnessCase -Label 'bootstrap-process-failure' -ManifestHashes @('bootstrap') -BootstrapExitCode 19
    $bootstrapNUnitFailure = Invoke-HarnessCase -Label 'bootstrap-nunit-failure' -ManifestHashes @('bootstrap') -BootstrapNUnitResult 'Failed'
    $bootstrapBaselineFailure = Invoke-HarnessCase -Label 'bootstrap-baseline-failure' -ManifestHashes @('bootstrap', 'bootstrap') -BaselineMismatch
    foreach ($case in @($bootstrapProcessFailure, $bootstrapNUnitFailure, $bootstrapBaselineFailure)) {
        Assert-Harness -Condition ($null -ne $case.failure) -Message "Bootstrap failure '$($case.bootstrapDirectory)' unexpectedly passed."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-quiescent.json') -PathType Leaf) -Message "Bootstrap failure '$($case.bootstrapDirectory)' did not persist its post-bootstrap manifest."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-bootstrap-delta.json') -PathType Leaf) -Message "Bootstrap failure '$($case.bootstrapDirectory)' did not persist its observed delta."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $case.bootstrapDirectory 'failure-evidence/immutable-input-manifest-bootstrap-delta.json') -PathType Leaf) -Message "Bootstrap failure '$($case.bootstrapDirectory)' did not preserve its actual delta artifact as failure evidence."
        $bootstrapDelta = Get-Content -LiteralPath (Join-Path $case.bootstrapDirectory 'immutable-input-manifest-bootstrap-delta.json') -Raw | ConvertFrom-Json
        $bootstrapFailureDelta = Get-Content -LiteralPath (Join-Path $case.bootstrapDirectory 'failure-evidence/immutable-input-manifest-bootstrap-delta.json') -Raw | ConvertFrom-Json
        Assert-ExactJsonPropertyNames -Value $bootstrapDelta -ExpectedNames @('schemaName', 'schemaVersion', 'classification', 'pathOrdering', 'preBootstrapRootSha256', 'postBootstrapRootSha256', 'added', 'removed', 'changed') -Description 'Bootstrap failure delta report'
        Assert-Harness -Condition ($bootstrapDelta.schemaName -eq 'purebase-immutable-manifest-bootstrap-delta' -and $bootstrapDelta.schemaVersion -eq 1 -and $bootstrapDelta.classification -eq 'observed' -and $bootstrapDelta.pathOrdering -eq 'System.StringComparer.Ordinal' -and @($bootstrapDelta.added).Count -eq 1 -and $bootstrapDelta.added[0].path -eq 'ProjectSettings/SceneTemplateSettings.json' -and @($bootstrapDelta.removed).Count -eq 0 -and @($bootstrapDelta.changed).Count -eq 0) -Message "Bootstrap failure '$($case.bootstrapDirectory)' changed its deterministic observed delta schema or content."
        Assert-Harness -Condition (($bootstrapDelta | ConvertTo-Json -Depth 8 -Compress) -eq ($bootstrapFailureDelta | ConvertTo-Json -Depth 8 -Compress)) -Message "Bootstrap failure '$($case.bootstrapDirectory)' did not preserve the actual delta artifact in failure evidence."
        Assert-Harness -Condition (-not (Test-Path -LiteralPath $case.runDirectory)) -Message "Bootstrap failure '$($case.bootstrapDirectory)' did not fail closed before a matrix row."
    }
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $bootstrapProcessFailure.bootstrapDirectory 'failure-evidence/Process.log') -PathType Leaf) -Message 'Bootstrap process failure did not preserve its process evidence.'
    Assert-Harness -Condition ($bootstrapNUnitFailure.failure.Exception.Message -match 'did not pass cleanly') -Message 'Bootstrap NUnit failure did not report the NUnit verdict.'
    Assert-Harness -Condition ($bootstrapBaselineFailure.failure.Exception.Message -match 'does not match shader-core-0.1.5.sha256.json') -Message 'Bootstrap identity failure did not report the baseline mismatch.'

    $resetMismatch = Invoke-HarnessCase -Label 'reset-mismatch' -Selections @{ 'PureBase/Unlit' = @('module') } -ManifestHashes @('bootstrap', 'bootstrap', 'row', 'drift', 'drift')
    Assert-Harness -Condition ($null -ne $resetMismatch.failure) -Message 'Cold reset manifest drift unexpectedly passed.'
    Assert-Harness -Condition ($resetMismatch.resetCalls -eq 2) -Message 'Cold reset drift did not execute both bootstrap and selected-module Library resets.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $resetMismatch.runDirectory 'immutable-input-manifest-after-reset.json') -PathType Leaf) -Message 'Cold reset drift did not persist its post-reset manifest.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $resetMismatch.runDirectory 'failure-evidence/immutable-input-manifest-after.json') -PathType Leaf) -Message 'Cold reset drift did not preserve final immutable evidence.'

    $finalDrift = Invoke-HarnessCase -Label 'final-drift' -ManifestHashes @('bootstrap', 'bootstrap', 'row', 'drift')
    Assert-Harness -Condition ($null -ne $finalDrift.failure) -Message 'Immutable drift unexpectedly passed.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $finalDrift.runDirectory 'failure.json') -PathType Leaf) -Message 'Immutable drift did not fail closed with failure.json.'
    $finalDriftBeforeManifest = Get-Content -LiteralPath (Join-Path $finalDrift.runDirectory 'immutable-input-manifest-before.json') -Raw | ConvertFrom-Json
    $finalDriftAfterManifest = Get-Content -LiteralPath (Join-Path $finalDrift.runDirectory 'immutable-input-manifest-after.json') -Raw | ConvertFrom-Json
    $finalDriftBeforeSceneTemplateEntry = @($finalDriftBeforeManifest.entries | Where-Object { $_.path -eq 'ProjectSettings/SceneTemplateSettings.json' })
    $finalDriftAfterSceneTemplateEntry = @($finalDriftAfterManifest.entries | Where-Object { $_.path -eq 'ProjectSettings/SceneTemplateSettings.json' })
    Assert-Harness -Condition ($finalDriftBeforeManifest.rootSha256 -eq 'row' -and $finalDriftAfterManifest.rootSha256 -eq 'drift' -and $finalDriftBeforeSceneTemplateEntry.Count -eq 1 -and $finalDriftAfterSceneTemplateEntry.Count -eq 1 -and $finalDriftAfterSceneTemplateEntry[0].sha256 -eq $finalDriftBeforeSceneTemplateEntry[0].sha256) -Message 'Immutable drift did not preserve the materialized SceneTemplateSettings entry while rejecting unrelated immutable input drift.'

    $canonicalCounts = @{}
    $duplicateCounts = @{}
    $invalidCounts = @{}
    foreach ($productName in $ProductNames) {
        $canonicalCounts[$productName] = @(1, 1, 1, 0)
        $duplicateCounts[$productName] = @(2, 2, 2, 0)
        $invalidCounts[$productName] = @(1, 3, 1, 0)
    }
    $warmContract = New-HarnessStandardMorphContract -RunLabel 'standard-morph-warm-library-duplicate-evidence'
    $coldContract = New-HarnessStandardMorphContract -RunLabel 'standard-morph-cold-library-legacy-counts'

    $canonicalRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseComparisonCanonical-' + [guid]::NewGuid().ToString('N'))
    Write-HarnessComparisonEvidence -Root $canonicalRoot -WarmContract $warmContract -ColdContract $coldContract -WarmCounts $canonicalCounts -ColdCounts $canonicalCounts
    $canonicalVerdict = Invoke-StandardMorphComparisonVerdict -RunRoot $canonicalRoot -WarmContract $warmContract -ColdContract $coldContract
    Assert-Harness -Condition ($canonicalVerdict.status -eq 'passed') -Message 'Canonical warm/cold comparison did not pass.'
    Assert-Harness -Condition (@($canonicalVerdict.products | Where-Object { $_.warmClassification -ne 'canonical' -or -not $_.coldCanonical }).Count -eq 0) -Message 'Canonical warm/cold comparison did not classify every product as canonical.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $canonicalRoot 'standard-morph-comparison-verdict.json') -PathType Leaf) -Message 'Canonical comparison did not persist its verdict.'

    $duplicateRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseComparisonDuplicate-' + [guid]::NewGuid().ToString('N'))
    Write-HarnessComparisonEvidence -Root $duplicateRoot -WarmContract $warmContract -ColdContract $coldContract -WarmCounts $duplicateCounts -ColdCounts $canonicalCounts
    $duplicateVerdict = Invoke-StandardMorphComparisonVerdict -RunRoot $duplicateRoot -WarmContract $warmContract -ColdContract $coldContract
    Assert-Harness -Condition ($duplicateVerdict.status -eq 'passed') -Message 'Known-duplicate warm evidence with canonical cold evidence did not pass.'
    Assert-Harness -Condition (@($duplicateVerdict.products | Where-Object { $_.warmClassification -ne 'known-duplicate' -or -not $_.coldCanonical }).Count -eq 0) -Message 'Known-duplicate warm evidence did not receive the expected classification.'

    $invalidRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseComparisonInvalid-' + [guid]::NewGuid().ToString('N'))
    Write-HarnessComparisonEvidence -Root $invalidRoot -WarmContract $warmContract -ColdContract $coldContract -WarmCounts $invalidCounts -ColdCounts $canonicalCounts
    $invalidFailure = $null
    try {
        Invoke-StandardMorphComparisonVerdict -RunRoot $invalidRoot -WarmContract $warmContract -ColdContract $coldContract | Out-Null
    }
    catch {
        $invalidFailure = $_
    }
    Assert-Harness -Condition ($null -ne $invalidFailure) -Message 'Invalid warm observation unexpectedly passed.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $invalidRoot 'standard-morph-comparison-verdict.json') -PathType Leaf) -Message 'Invalid warm observation did not persist a verdict.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $invalidRoot 'standard-morph-comparison-failure.json') -PathType Leaf) -Message 'Invalid warm observation did not persist an external failure artifact.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $invalidRoot ('runs/' + $warmContract.runLabel + '/consumer-evidence/standard-morph-observation.json')) -PathType Leaf) -Message 'Invalid warm observation did not retain row evidence.'

    foreach ($observationFailureKind in @('malformed', 'missing')) {
        $observationFailureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseComparisonObservation-' + $observationFailureKind + '-' + [guid]::NewGuid().ToString('N'))
        Write-HarnessComparisonEvidence -Root $observationFailureRoot -WarmContract $warmContract -ColdContract $coldContract -WarmCounts $canonicalCounts -ColdCounts $canonicalCounts
        $observationPath = Join-Path $observationFailureRoot ('runs/' + $warmContract.runLabel + '/consumer-evidence/standard-morph-observation.json')
        if ($observationFailureKind -eq 'malformed') {
            [System.IO.File]::WriteAllText($observationPath, '{not-json', (New-Object System.Text.UTF8Encoding($false)))
        }
        else {
            Remove-Item -LiteralPath $observationPath -Force
        }
        $observationFailure = $null
        try {
            Invoke-StandardMorphComparisonVerdict -RunRoot $observationFailureRoot -WarmContract $warmContract -ColdContract $coldContract | Out-Null
        }
        catch {
            $observationFailure = $_
        }
        Assert-Harness -Condition ($null -ne $observationFailure) -Message "${observationFailureKind} standard-morph observation unexpectedly passed."
        Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $observationFailureRoot 'standard-morph-comparison-failure.json') -PathType Leaf) -Message "${observationFailureKind} standard-morph observation did not persist a comparison failure artifact."
    }

    $warmProcessFailure = Invoke-HarnessCase -Label 'standard-morph-warm-library-duplicate-evidence' -ManifestHashes @('bootstrap', 'bootstrap', 'row', 'row') -UnityExitCode 23 -TestFilter 'PureBase.Release.Consumer.Tests.PureBaseConsumerStandardMorphObservationTests.StandardMorphProductsRecordPassCountObservations' -AllowObservationEvidence
    Assert-Harness -Condition ($null -ne $warmProcessFailure.failure) -Message 'Warm observation accepted a nonzero Unity process exit.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $warmProcessFailure.runDirectory 'failure.json') -PathType Leaf) -Message 'Warm observation process failure did not persist failure.json.'
    Assert-Harness -Condition (Test-Path -LiteralPath (Join-Path $warmProcessFailure.runDirectory 'failure-evidence/Process.log') -PathType Leaf) -Message 'Warm observation process failure did not preserve Process.log.'
    Write-Host 'Runner-only immutable manifest harness passed.'
}
finally {
    Remove-Item -LiteralPath $libraryPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $fakeUnityPath -Force -ErrorAction SilentlyContinue
}
