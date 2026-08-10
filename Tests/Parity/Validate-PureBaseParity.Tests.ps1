<#
 Copyright 2026 Penguin

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.
#>

# Exercises fail-closed parity validation using synthetic artifacts without launching Unity.
[CmdletBinding()]
param()

Describe 'Validate-PureBaseParity synthetic artifact contracts' {
    BeforeAll {
        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'
        . (Join-Path $PSScriptRoot 'Validate-PureBaseParity.Oracle.ps1')

        function Assert-Harness {
            param([Parameter(Mandatory = $true)][bool]$Condition, [Parameter(Mandatory = $true)][string]$Message)
            if (-not $Condition) { throw $Message }
        }

        function Write-Json {
            param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)]$Value)
            [void](New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force)
            $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding UTF8
        }

        function Write-NUnit {
            param([Parameter(Mandatory = $true)][string]$Path, [Parameter()][string]$FullName = 'PureBase.Tests.Daily.Synthetic', [Parameter()][int]$Failed = 0)
            [void](New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force)
            $result = if ($Failed -eq 0) { 'Passed' } else { 'Failed' }
            $passed = if ($Failed -eq 0) { 1 } else { 0 }
            $xml = "<test-run total=`"1`" passed=`"$passed`" failed=`"$Failed`" skipped=`"0`" inconclusive=`"0`"><test-suite><test-case fullname=`"$([System.Security.SecurityElement]::Escape($FullName))`" result=`"$result`" /></test-suite></test-run>"
            [System.IO.File]::WriteAllText($Path, $xml, (New-Object System.Text.UTF8Encoding($false)))
        }

        function Add-ZipEntry {
            param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$EntryName)
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Update)
            try {
                $entry = $archive.CreateEntry($EntryName)
                $writer = New-Object System.IO.StreamWriter($entry.Open())
                try { $writer.Write('forbidden') } finally { $writer.Dispose() }
            }
            finally { $archive.Dispose() }
        }

        function Remove-ZipEntry {
            param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$EntryName)
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Update)
            try {
                $entry = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $EntryName }) | Select-Object -First 1
                if ($null -eq $entry) { throw "Synthetic ZIP entry '$EntryName' does not exist." }
                $entry.Delete()
            }
            finally { $archive.Dispose() }
        }

        function New-Hash {
            param([Parameter(Mandatory = $true)][string]$Character)
            return $Character * 64
        }

        function New-PathEntries {
            param([Parameter(Mandatory = $true)][int]$Count, [Parameter(Mandatory = $true)][string]$Prefix, [Parameter(Mandatory = $true)][string]$HashCharacter)
            return @(
                1..$Count | ForEach-Object {
                    [ordered]@{ path = ('{0}/{1:D2}.asset' -f $Prefix, $_); sha256 = New-Hash -Character $HashCharacter }
                }
            )
        }

        function New-ChangedPathEntries {
            return @(
                [ordered]@{ path = 'ProjectSettings/Changed01.asset'; preBootstrapSha256 = New-Hash -Character 'd'; postBootstrapSha256 = New-Hash -Character 'e' },
                [ordered]@{ path = 'ProjectSettings/Changed02.asset'; preBootstrapSha256 = New-Hash -Character 'f'; postBootstrapSha256 = New-Hash -Character '0' }
            )
        }

        function New-SyntheticArtifacts {
            param(
                [Parameter(Mandatory = $true)]$Manifest,
                [Parameter(Mandatory = $true)]$ReleaseContentContract,
                [Parameter()][string]$PackageVersion = '0.1.0'
            )

            $root = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseParity-' + [guid]::NewGuid().ToString('N'))
            $legacy = Join-Path $root 'legacy'
            $daily = Join-Path $root 'daily'
            $release = Join-Path $root 'release'
            [void](New-Item -ItemType Directory -Path $legacy, $daily, $release -Force)
            foreach ($invocation in @($Manifest.nunitInvocations)) {
                $id = [string]$invocation.id
                Write-NUnit -Path (Join-Path $legacy ($id + '/' + $id + '.NUnit.xml')) -FullName ([string]$invocation.nunitFullName)
            }
            Write-Json -Path (Join-Path $legacy 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json') -Value ([ordered]@{
                    staticLightmapCount     = 2
                    staticLightmaps         = @(1..20 | ForEach-Object { [ordered]@{ renderer = "Renderer$_"; lightmapIndex = 0; scaleOffsetX = 0.1; scaleOffsetY = 0.1; scaleOffsetZ = 0.0; scaleOffsetW = 0.0 } })
                    metaAlbedo              = @(1..18 | ForEach-Object { [ordered]@{ material = "Material$_"; shader = 'PureBase/Unlit'; meanLuminance = 0.25 } })
                    shadowChangedPixelCount = 967
                    variants                = @(1..56 | ForEach-Object { [ordered]@{ shader = 'PureBase/Unlit'; pass = 'ForwardBase'; label = "Variant$_"; keywords = @(); added = $true; warmed = $true; variantCount = 1 } })
                })
            Write-Json -Path (Join-Path $legacy 'validation-scene-birp-feasibility.json') -Value ([ordered]@{
                    check                 = 'Fixed BIRP validation scene synchronous bake, static lightmap, Meta readback, shadow silhouette, and representative variant warmup'
                    status                = 'PASSED'
                    unityVersion          = '2022.3.22f1'
                    graphicsApi           = 'D3D11'
                    testName              = 'PureBase.Integration.Tests.PureBaseValidationSceneTests.FixedValidationSceneBakesAndRequestsRepresentativeBirpVariants'
                    artifactPath          = 'C:/Temp/PureBaseParity/validation-scene-birp'
                    dynamicLightmapStatus = 'NOT_DETERMINISTIC_IN_BATCH_EDITMODE'
                })
            Write-Json -Path (Join-Path $legacy 'birp-probe-feasibility.json') -Value ([ordered]@{
                    check         = 'BIRP probe finite black and box-projected reflection-probe readback'
                    status        = 'PASSED'
                    unityVersion  = '2022.3.22f1'
                    graphicsApi   = 'D3D11'
                    testNames     = @('PureBase.Integration.Tests.BirpGiProbeReadbackTests.BlackProbePathProducesFiniteHdrReadbackWithMeshCoverage', 'PureBase.Integration.Tests.BirpGiProbeReadbackTests.BoxProjectedReflectionProbePathProducesFiniteHdrReadbackWithMeshCoverage')
                    artifactPaths = @('C:/Temp/PureBaseParity/birp-black', 'C:/Temp/PureBaseParity/birp-box')
                })
            Write-Json -Path (Join-Path $legacy 'release-boundary-audit.json') -Value ([ordered]@{
                    check                               = 'PureBase release boundary and metallic property ABI'
                    status                              = 'PASSED'
                    packagePath                         = 'C:/Temp/PureBaseParity/package'
                    trackedScmodulePaths                = @('Tests/Fixtures/Hosts/Phase/base/test.scmodule')
                    approvedTrackedScmodulePaths        = @('Tests/Fixtures/Hosts/Phase/base/test.scmodule')
                    unapprovedTrackedScmodulePaths      = @()
                    missingTrackedScmodulePaths         = @()
                    trackedScmodulePathsExactlyApproved = $true
                    shaderCoreDependency                = '0.1.9'
                    urpDependencyPresent                = $false
                    packageContainsPureBaseTestAssets   = $false
                    pbrHybridPropertiesByteIdentical    = $true
                    requiredProperties                  = @([ordered]@{ name = '_Metallic'; present = $true })
                    forbiddenProperties                 = @([ordered]@{ name = '_Emission'; present = $false })
                    roughnessAbi                        = $true
                })

            Write-NUnit -Path (Join-Path $daily 'Daily.NUnit.xml')
            [System.IO.File]::WriteAllText((Join-Path $daily 'Daily.Unity.log'), '', (New-Object System.Text.UTF8Encoding($false)))
            [System.IO.File]::WriteAllText((Join-Path $daily 'Daily.Process.log'), '', (New-Object System.Text.UTF8Encoding($false)))

            $archiveDirectory = Join-Path $release 'archive'
            $stagingDirectory = Join-Path $root 'zip-staging'
            [void](New-Item -ItemType Directory -Path $archiveDirectory -Force)
            foreach ($requiredEntry in @($ReleaseContentContract.requiredEntries)) {
                $stagePath = Join-Path $stagingDirectory ([string]$requiredEntry).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
                [void](New-Item -ItemType Directory -Path (Split-Path -Parent $stagePath) -Force)
                $content = if ($requiredEntry -eq 'package.json') { '{"name":"jp.penguin.purebase","version":"' + $PackageVersion + '"}' } else { [string]$requiredEntry }
                [System.IO.File]::WriteAllText($stagePath, $content, (New-Object System.Text.UTF8Encoding($false)))
            }
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $releaseZipPath = Join-Path $archiveDirectory ('jp.penguin.purebase-' + $PackageVersion + '.zip')
            [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDirectory, $releaseZipPath)
            $releaseLabels = @($Manifest.releaseArtifactLayout.fullMatrixLabels)
            $releaseRunDirectories = $Manifest.releaseArtifactLayout.fullMatrixRunDirectories
            Write-Json -Path (Join-Path $release 'run-summary.json') -Value ([ordered]@{ validationScope = 'full-release-validation-matrix'; outcomes = @($releaseLabels | ForEach-Object { [ordered]@{ label = [string]$_ } }) })
            Write-Json -Path (Join-Path $release 'cleanup-summary.json') -Value ([ordered]@{ failed = $false; consumerDirectoryRemovalFailed = $false })
            $bootstrap = Join-Path $release 'bootstrap'
            Write-NUnit -Path (Join-Path $bootstrap 'NUnit.xml')
            foreach ($releaseLabel in $releaseLabels) {
                $runDirectoryLabel = [string]$releaseRunDirectories.PSObject.Properties[[string]$releaseLabel].Value
                Write-NUnit -Path (Join-Path $release ('runs/' + $runDirectoryLabel + '/NUnit.xml'))
            }
            Write-Json -Path (Join-Path $bootstrap 'staging-receipt.json') -Value ([ordered]@{
                    schemaName = 'purebase-consumer-staging-receipt'; schemaVersion = 1; pathOrdering = 'System.StringComparer.Ordinal'; entries = @([ordered]@{ destination = 'Assets/Synthetic.txt'; sourceKind = 'synthetic'; source = 'Synthetic.txt'; sha256 = New-Hash -Character 'a' })
                })
            Write-Json -Path (Join-Path $bootstrap 'immutable-input-manifest-bootstrap-delta.json') -Value ([ordered]@{
                    schemaName = 'purebase-immutable-manifest-bootstrap-delta'; schemaVersion = 1; classification = 'observed'; pathOrdering = 'System.StringComparer.Ordinal'; preBootstrapRootSha256 = New-Hash -Character 'b'; postBootstrapRootSha256 = New-Hash -Character 'c'; added = New-PathEntries -Count 34 -Prefix 'ProjectSettings/Added' -HashCharacter 'a'; changed = New-ChangedPathEntries; removed = @()
                })
            Write-Json -Path (Join-Path $bootstrap 'semantic-transition-report.json') -Value ([ordered]@{ schemaName = 'purebase-first-bootstrap-semantic-transition'; schemaVersion = 1; verdict = 'accepted'; summary = [ordered]@{ accepted = 34; rejected = 0; unclassified = 0 } })
            Write-Json -Path (Join-Path $bootstrap 'second-bootstrap/fixed-point-report.json') -Value ([ordered]@{ schemaName = 'purebase-second-bootstrap-fixed-point'; schemaVersion = 1; rootsEqual = $true; added = @(); changed = @(); removed = @() })
            return [pscustomobject]@{ root = $root; legacy = $legacy; daily = $daily; release = $release; releaseZipPath = $releaseZipPath }
        }

        function Invoke-ValidatorCase {
            param(
                [Parameter(Mandatory = $true)][string]$Name,
                [Parameter(Mandatory = $true)]$Manifest,
                [Parameter(Mandatory = $true)][string]$ValidatorPath,
                [Parameter(Mandatory = $true)][string]$ManifestPath,
                [Parameter(Mandatory = $true)][string]$PackageRoot,
                [Parameter(Mandatory = $true)][bool]$ExpectedEligible,
                [Parameter()][string]$ExpectedFailureCode,
                [Parameter()][scriptblock]$Mutate,
                [Parameter()][switch]$ReportUnderPackageRoot
            )

            $artifacts = New-SyntheticArtifacts -Manifest $Manifest -ReleaseContentContract $script:releaseContentContract
            try {
                $effectiveManifestPath = $ManifestPath
                if ($null -ne $Mutate) {
                    $mutatedManifestPath = & $Mutate $artifacts
                    if ($mutatedManifestPath -is [string] -and -not [string]::IsNullOrWhiteSpace($mutatedManifestPath)) { $effectiveManifestPath = $mutatedManifestPath }
                }
                $reportPath = if ($ReportUnderPackageRoot) { Join-Path $PackageRoot ('parity-report-' + $Name + '-' + [guid]::NewGuid().ToString('N') + '.json') } else { Join-Path $artifacts.root ('report-' + $Name + '.json') }
                $previousErrorActionPreference = $ErrorActionPreference
                $ErrorActionPreference = 'Continue'
                $hostExecutableName = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' }
                $hostExecutable = Join-Path $PSHOME $hostExecutableName
                & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $ValidatorPath -LegacyArtifactRoot $artifacts.legacy -DailyArtifactRoot $artifacts.daily -ReleaseArtifactRoot $artifacts.release -ManifestPath $effectiveManifestPath -ReportPath $reportPath 2>$null | Out-Null
                $ErrorActionPreference = $previousErrorActionPreference
                $eligible = $LASTEXITCODE -eq 0
                $diagnostic = ''
                if ($eligible -ne $ExpectedEligible -and -not $ReportUnderPackageRoot -and (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
                    $diagnostic = ' Report: ' + (Get-Content -LiteralPath $reportPath -Raw)
                }
                Assert-Harness -Condition ($eligible -eq $ExpectedEligible) -Message "Case '$Name' returned eligible=$eligible, expected $ExpectedEligible.$diagnostic"
                if (-not $ReportUnderPackageRoot) {
                    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
                    Assert-Harness -Condition ([bool]$report.deletionEligible -eq $ExpectedEligible) -Message "Case '$Name' report eligibility differs from expected value."
                    if (-not $ExpectedEligible) { Assert-Harness -Condition ([int]$report.failureCount -gt 0 -and @($report.failures).Count -gt 0) -Message "Case '$Name' did not persist a nonzero failure report." }
                    if (-not [string]::IsNullOrWhiteSpace($ExpectedFailureCode)) { Assert-Harness -Condition (@($report.failures | Where-Object { $_.code -eq $ExpectedFailureCode }).Count -gt 0) -Message "Case '$Name' did not persist failure code '$ExpectedFailureCode'." }
                }
                else {
                    Assert-Harness -Condition (-not (Test-Path -LiteralPath $reportPath)) -Message "Case '$Name' wrote a report under the package root."
                }
            }
            finally {
                Remove-Item -LiteralPath $artifacts.root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        $script:scriptRoot = Split-Path -Parent $PSCommandPath
        $script:validatorPath = Join-Path $script:scriptRoot 'Validate-PureBaseParity.ps1'
        $script:manifestPath = Join-Path $script:scriptRoot 'pure-base-validation-parity.json'
        $script:packageRoot = [System.IO.Path]::GetFullPath((Join-Path $script:scriptRoot '../..'))
        $script:manifest = Get-Content -LiteralPath $script:manifestPath -Raw | ConvertFrom-Json
        $releaseContentContractPath = Join-Path $script:scriptRoot '../Release/release-content.json'
        $script:releaseContentContract = Get-Content -LiteralPath $releaseContentContractPath -Raw | ConvertFrom-Json

        function Invoke-ValidationSceneOracleCase {
            param(
                [Parameter(Mandatory = $true)][string]$Name,
                [Parameter(Mandatory = $true)][string]$MissingProperty,
                [Parameter(Mandatory = $true)][string]$ValidatorPath
            )

            $variant = [pscustomobject][ordered]@{
                shader       = 'PureBase/Unlit'
                pass         = 'ForwardBase'
                label        = 'DirectOracleVariant'
                keywords     = @()
                added        = $true
                warmed       = $true
                variantCount = 1
            }
            $variant.PSObject.Properties.Remove($MissingProperty)
            $scene = [pscustomobject][ordered]@{ variants = @($variant) }
            $failures = New-Object 'System.Collections.Generic.List[object]'
            $actual = Get-ValidationSceneOracleValue -Scene $scene -Name 'warmedRepresentativeVariantCount' -Failures $failures -Code 'direct-oracle'

            Assert-Harness -Condition ($null -eq $actual) -Message "Case '$Name' did not reject the malformed oracle fixture."
            Assert-Harness -Condition ($failures.Count -eq 1) -Message "Case '$Name' recorded an unexpected number of oracle failures."
            Assert-Harness -Condition ([string]$failures[0].code -eq 'direct-oracle') -Message "Case '$Name' recorded an unexpected oracle failure code."
            Assert-Harness -Condition ([string]$failures[0].message -eq 'Variant evidence is missing a warmed representative variant.') -Message "Case '$Name' recorded an unexpected oracle failure message."
        }

    }

    It 'preserves all synthetic parity acceptance and rejection contracts' {
        $manifest = $script:manifest
        $validatorPath = $script:validatorPath
        $manifestPath = $script:manifestPath
        $packageRoot = $script:packageRoot

        Invoke-ValidatorCase -Name 'happy-path' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $true
        Invoke-ValidatorCase -Name 'missing-package-test-assets-boolean' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ExpectedFailureCode 'legacy-release-boundary-test-assets' -Mutate {
            param($artifacts)
            $auditPath = Join-Path $artifacts.legacy 'release-boundary-audit.json'
            $audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
            $audit.PSObject.Properties.Remove('packageContainsPureBaseTestAssets')
            Write-Json -Path $auditPath -Value $audit
        }
        Invoke-ValidatorCase -Name 'package-test-assets-present' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ExpectedFailureCode 'legacy-release-boundary-test-assets' -Mutate {
            param($artifacts)
            $auditPath = Join-Path $artifacts.legacy 'release-boundary-audit.json'
            $audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
            $audit.packageContainsPureBaseTestAssets = $true
            Write-Json -Path $auditPath -Value $audit
        }
        Invoke-ValidatorCase -Name 'incomplete-legacy-row' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $copy = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $copy.nunitInvocations[0].nunitFullName = ''
            $changedManifest = Join-Path $artifacts.root 'incomplete-legacy-row.json'
            Write-Json -Path $changedManifest -Value $copy
            $changedManifest
        }
        Invoke-ValidatorCase -Name 'duplicate-legacy-row' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $copy = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $copy.nunitInvocations[1].id = $copy.nunitInvocations[0].id
            $changedManifest = Join-Path $artifacts.root 'duplicate-legacy-row.json'
            Write-Json -Path $changedManifest -Value $copy
            $changedManifest
        }
        Invoke-ValidatorCase -Name 'duplicate-mapping' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $copy = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $copy.parityMappings[1].legacyId = $copy.parityMappings[0].legacyId
            $changedManifest = Join-Path $artifacts.root 'duplicate-mapping.json'
            Write-Json -Path $changedManifest -Value $copy
            $changedManifest
        }
        Invoke-ValidatorCase -Name 'failing-legacy-nunit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $first = $manifest.nunitInvocations[0]
            Write-NUnit -Path (Join-Path $artifacts.legacy ($first.id + '/' + $first.id + '.NUnit.xml')) -FullName $first.nunitFullName -Failed 1
        }
        Invoke-ValidatorCase -Name 'missing-legacy-nunit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $first = $manifest.nunitInvocations[0]
            Remove-Item -LiteralPath (Join-Path $artifacts.legacy ($first.id + '/' + $first.id + '.NUnit.xml')) -Force
        }
        Invoke-ValidatorCase -Name 'failing-daily-nunit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Write-NUnit -Path (Join-Path $artifacts.daily 'Daily.NUnit.xml') -Failed 1
        }
        Invoke-ValidatorCase -Name 'missing-daily-nunit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Remove-Item -LiteralPath (Join-Path $artifacts.daily 'Daily.NUnit.xml') -Force
        }
        Invoke-ValidatorCase -Name 'failing-bootstrap-nunit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Write-NUnit -Path (Join-Path $artifacts.release 'bootstrap/NUnit.xml') -Failed 1
        }
        Invoke-ValidatorCase -Name 'missing-bootstrap-nunit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Remove-Item -LiteralPath (Join-Path $artifacts.release 'bootstrap/NUnit.xml') -Force
        }
        Invoke-ValidatorCase -Name 'failing-release-matrix-nunit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Write-NUnit -Path (Join-Path $artifacts.release ('runs/' + [string]$manifest.releaseArtifactLayout.fullMatrixLabels[1] + '/NUnit.xml')) -Failed 1
        }
        Invoke-ValidatorCase -Name 'missing-release-matrix-nunit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Remove-Item -LiteralPath (Join-Path $artifacts.release ('runs/' + [string]$manifest.releaseArtifactLayout.fullMatrixLabels[1] + '/NUnit.xml')) -Force
        }
        Invoke-ValidatorCase -Name 'missing-expected-release-matrix-label' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $summaryPath = Join-Path $artifacts.release 'run-summary.json'
            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            $missingLabel = [string]$manifest.releaseArtifactLayout.fullMatrixLabels[0]
            $summary.outcomes = @($summary.outcomes | Where-Object { [string]$_.label -ne $missingLabel })
            Write-Json -Path $summaryPath -Value $summary
        }
        Invoke-ValidatorCase -Name 'unexpected-extra-release-matrix-label' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $summaryPath = Join-Path $artifacts.release 'run-summary.json'
            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            $summary.outcomes += [pscustomobject][ordered]@{ label = 'unexpected-release-matrix-label' }
            Write-NUnit -Path (Join-Path $artifacts.release 'runs/unexpected-release-matrix-label/NUnit.xml')
            Write-Json -Path $summaryPath -Value $summary
        }
        Invoke-ValidatorCase -Name 'declared-release-run-directory-label' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $true -Mutate {
            param($artifacts)
            $summaryPath = Join-Path $artifacts.release 'run-summary.json'
            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            foreach ($outcome in @($summary.outcomes)) {
                $outcome | Add-Member -NotePropertyName runDirectoryLabel -NotePropertyValue ([string]$manifest.releaseArtifactLayout.fullMatrixRunDirectories.PSObject.Properties[[string]$outcome.label].Value)
            }
            Write-Json -Path $summaryPath -Value $summary
        }
        Invoke-ValidatorCase -Name 'mismatched-release-run-directory-label' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $summaryPath = Join-Path $artifacts.release 'run-summary.json'
            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            $summary.outcomes[0] | Add-Member -NotePropertyName runDirectoryLabel -NotePropertyValue 'wrong-directory'
            Write-Json -Path $summaryPath -Value $summary
        }
        Invoke-ValidatorCase -Name 'unwarmed-variant-evidence' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $scenePath = Join-Path $artifacts.legacy 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json'
            $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
            $scene.variants[0].warmed = $false
            Write-Json -Path $scenePath -Value $scene
        }
        Invoke-ValidatorCase -Name 'missing-warmed-variant-evidence' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $scenePath = Join-Path $artifacts.legacy 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json'
            $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
            $scene.variants[0].PSObject.Properties.Remove('warmed')
            Write-Json -Path $scenePath -Value $scene
        }
        Invoke-ValidatorCase -Name 'missing-variant-count-evidence' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $scenePath = Join-Path $artifacts.legacy 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json'
            $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
            $scene.variants[0].PSObject.Properties.Remove('variantCount')
            Write-Json -Path $scenePath -Value $scene
        }
        Invoke-ValidationSceneOracleCase -Name 'direct-missing-warmed-property' -MissingProperty 'warmed' -ValidatorPath $validatorPath
        Invoke-ValidationSceneOracleCase -Name 'direct-missing-variant-count-property' -MissingProperty 'variantCount' -ValidatorPath $validatorPath
        Invoke-ValidatorCase -Name 'fixed-evidence-mismatch' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $scenePath = Join-Path $artifacts.legacy 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json'
            $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
            $scene.staticLightmapCount = 1
            Write-Json -Path $scenePath -Value $scene
        }
        Invoke-ValidatorCase -Name 'dynamic-limitation-mismatch' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Write-Json -Path (Join-Path $artifacts.legacy 'validation-scene-birp-feasibility.json') -Value ([ordered]@{ status = 'SUPPORTED' })
        }
        Invoke-ValidatorCase -Name 'malformed-legacy-validation-scene' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            [System.IO.File]::WriteAllText((Join-Path $artifacts.legacy 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json'), '{', (New-Object System.Text.UTF8Encoding($false)))
        }
        Invoke-ValidatorCase -Name 'null-static-lightmap-entry' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $scenePath = Join-Path $artifacts.legacy 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json'
            $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
            $scene.staticLightmaps[0] = $null
            Write-Json -Path $scenePath -Value $scene
        }
        Invoke-ValidatorCase -Name 'incomplete-meta-albedo-entry' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $scenePath = Join-Path $artifacts.legacy 'validation-scene-birp/Artifacts/purebase-validation-scene-birp.json'
            $scene = Get-Content -LiteralPath $scenePath -Raw | ConvertFrom-Json
            $scene.metaAlbedo[0].PSObject.Properties.Remove('shader')
            Write-Json -Path $scenePath -Value $scene
        }
        Invoke-ValidatorCase -Name 'malformed-dynamic-lightmaps-evidence' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            [System.IO.File]::WriteAllText((Join-Path $artifacts.legacy 'validation-scene-birp-feasibility.json'), '{', (New-Object System.Text.UTF8Encoding($false)))
        }
        Invoke-ValidatorCase -Name 'incomplete-birp-probe-evidence' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Write-Json -Path (Join-Path $artifacts.legacy 'birp-probe-feasibility.json') -Value ([ordered]@{ status = 'PASSED' })
        }
        Invoke-ValidatorCase -Name 'incomplete-release-boundary-audit' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $auditPath = Join-Path $artifacts.legacy 'release-boundary-audit.json'
            $audit = Get-Content -LiteralPath $auditPath -Raw | ConvertFrom-Json
            $audit.PSObject.Properties.Remove('requiredProperties')
            Write-Json -Path $auditPath -Value $audit
        }
        Invoke-ValidatorCase -Name 'zip-tests-forbidden' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Add-ZipEntry -Path $artifacts.releaseZipPath -EntryName 'Tests/forbidden.txt'
        }
        Invoke-ValidatorCase -Name 'zip-scmodule-forbidden' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Add-ZipEntry -Path $artifacts.releaseZipPath -EntryName 'Modules/forbidden.scmodule'
        }
        Invoke-ValidatorCase -Name 'zip-required-release-entry-missing' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ExpectedFailureCode 'release-zip-required-entry' -Mutate {
            param($artifacts)
            Remove-ZipEntry -Path $artifacts.releaseZipPath -EntryName 'Shaders/PureBaseToon.scshader'
        }
        Invoke-ValidatorCase -Name 'zip-yank-policy-forbidden' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ExpectedFailureCode 'release-zip-content' -Mutate {
            param($artifacts)
            Add-ZipEntry -Path $artifacts.releaseZipPath -EntryName 'vpm-yanks.json'
        }
        Invoke-ValidatorCase -Name 'cleanup-status-missing' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ExpectedFailureCode 'release-cleanup-status' -Mutate {
            param($artifacts)
            $cleanupPath = Join-Path $artifacts.release 'cleanup-summary.json'
            $cleanup = Get-Content -LiteralPath $cleanupPath -Raw | ConvertFrom-Json
            $cleanup.PSObject.Properties.Remove('failed')
            Write-Json -Path $cleanupPath -Value $cleanup
        }
        Invoke-ValidatorCase -Name 'cleanup-status-nonboolean' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ExpectedFailureCode 'release-cleanup-status' -Mutate {
            param($artifacts)
            $cleanupPath = Join-Path $artifacts.release 'cleanup-summary.json'
            $cleanup = Get-Content -LiteralPath $cleanupPath -Raw | ConvertFrom-Json
            $cleanup.failed = 'false'
            Write-Json -Path $cleanupPath -Value $cleanup
        }
        Invoke-ValidatorCase -Name 'cleanup-status-true' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ExpectedFailureCode 'release-cleanup-status' -Mutate {
            param($artifacts)
            $cleanupPath = Join-Path $artifacts.release 'cleanup-summary.json'
            $cleanup = Get-Content -LiteralPath $cleanupPath -Raw | ConvertFrom-Json
            $cleanup.consumerDirectoryRemovalFailed = $true
            Write-Json -Path $cleanupPath -Value $cleanup
        }
        Invoke-ValidatorCase -Name 'migration-status-drift' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ExpectedFailureCode 'manifest-migration-status' -Mutate {
            param($artifacts)
            $copy = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $copy.migration.status = 'unexpected-status'
            $changedManifest = Join-Path $artifacts.root 'migration-status-drift.json'
            Write-Json -Path $changedManifest -Value $copy
            $changedManifest
        }
        Invoke-ValidatorCase -Name 'receipt-integrity-failure' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $receiptPath = Join-Path $artifacts.release 'bootstrap/staging-receipt.json'
            $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
            $receipt.entries += $receipt.entries[0]
            Write-Json -Path $receiptPath -Value $receipt
        }
        Invoke-ValidatorCase -Name 'malformed-immutable-delta' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            [System.IO.File]::WriteAllText((Join-Path $artifacts.release 'bootstrap/immutable-input-manifest-bootstrap-delta.json'), '{', (New-Object System.Text.UTF8Encoding($false)))
        }
        Invoke-ValidatorCase -Name 'rejected-immutable-delta' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $deltaPath = Join-Path $artifacts.release 'bootstrap/immutable-input-manifest-bootstrap-delta.json'
            $delta = Get-Content -LiteralPath $deltaPath -Raw | ConvertFrom-Json
            $delta.classification = 'rejected'
            Write-Json -Path $deltaPath -Value $delta
        }
        Invoke-ValidatorCase -Name 'invalid-immutable-delta-schema' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $deltaPath = Join-Path $artifacts.release 'bootstrap/immutable-input-manifest-bootstrap-delta.json'
            $delta = Get-Content -LiteralPath $deltaPath -Raw | ConvertFrom-Json
            $delta.schemaVersion = 999
            Write-Json -Path $deltaPath -Value $delta
        }
        Invoke-ValidatorCase -Name 'noncanonical-immutable-delta' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $deltaPath = Join-Path $artifacts.release 'bootstrap/immutable-input-manifest-bootstrap-delta.json'
            $delta = Get-Content -LiteralPath $deltaPath -Raw | ConvertFrom-Json
            $delta.added = @($delta.added | Sort-Object path -Descending)
            Write-Json -Path $deltaPath -Value $delta
        }
        Invoke-ValidatorCase -Name 'unexpected-immutable-delta' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            $deltaPath = Join-Path $artifacts.release 'bootstrap/immutable-input-manifest-bootstrap-delta.json'
            $delta = Get-Content -LiteralPath $deltaPath -Raw | ConvertFrom-Json
            $delta.added += [pscustomobject]@{ path = 'ProjectSettings/Unexpected.asset'; sha256 = New-Hash -Character '1' }
            Write-Json -Path $deltaPath -Value $delta
        }
        Invoke-ValidatorCase -Name 'nonzero-fixed-point' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Write-Json -Path (Join-Path $artifacts.release 'bootstrap/second-bootstrap/fixed-point-report.json') -Value ([ordered]@{ schemaName = 'purebase-second-bootstrap-fixed-point'; schemaVersion = 1; rootsEqual = $true; added = @('unexpected'); changed = @(); removed = @() })
        }
        Invoke-ValidatorCase -Name 'semantic-rejection' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Write-Json -Path (Join-Path $artifacts.release 'bootstrap/semantic-transition-report.json') -Value ([ordered]@{ schemaName = 'purebase-first-bootstrap-semantic-transition'; schemaVersion = 1; verdict = 'rejected'; summary = [ordered]@{ accepted = 33; rejected = 1; unclassified = 0 } })
        }
        Invoke-ValidatorCase -Name 'semantic-unclassified' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -Mutate {
            param($artifacts)
            Write-Json -Path (Join-Path $artifacts.release 'bootstrap/semantic-transition-report.json') -Value ([ordered]@{ schemaName = 'purebase-first-bootstrap-semantic-transition'; schemaVersion = 1; verdict = 'accepted'; summary = [ordered]@{ accepted = 33; rejected = 0; unclassified = 1 } })
        }
        Invoke-ValidatorCase -Name 'package-root-report' -Manifest $manifest -ValidatorPath $validatorPath -ManifestPath $manifestPath -PackageRoot $packageRoot -ExpectedEligible $false -ReportUnderPackageRoot
    }

    It 'finds a prerelease archive whose filename exactly matches its package manifest version' {
        $artifacts = New-SyntheticArtifacts `
            -Manifest $script:manifest `
            -ReleaseContentContract $script:releaseContentContract `
            -PackageVersion '0.1.0-beta.1'
        try {
            [IO.Path]::GetFileName($artifacts.releaseZipPath) | Should -Be 'jp.penguin.purebase-0.1.0-beta.1.zip'
            $hostExecutableName = if ($PSVersionTable.PSEdition -eq 'Core') { 'pwsh.exe' } else { 'powershell.exe' }
            $hostExecutable = Join-Path $PSHOME $hostExecutableName
            $reportPath = Join-Path $artifacts.root 'prerelease-report.json'
            & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $script:validatorPath -LegacyArtifactRoot $artifacts.legacy -DailyArtifactRoot $artifacts.daily -ReleaseArtifactRoot $artifacts.release -ManifestPath $script:manifestPath -ReportPath $reportPath 2>$null | Out-Null

            $LASTEXITCODE | Should -Be 0
        }
        finally {
            Remove-Item -LiteralPath $artifacts.root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'rejects a parity mapping that pins a stable release ZIP filename' {
        Invoke-ValidatorCase -Name 'stable-release-zip-evidence' -Manifest $script:manifest -ValidatorPath $script:validatorPath -ManifestPath $script:manifestPath -PackageRoot $script:packageRoot -ExpectedEligible $false -ExpectedFailureCode 'manifest-mapping' -Mutate {
            param($artifacts)
            $copy = Get-Content -LiteralPath $script:manifestPath -Raw | ConvertFrom-Json
            foreach ($mapping in @($copy.parityMappings)) {
                $mapping.evidence = @($mapping.evidence | ForEach-Object {
                        if ($_ -eq 'package-versioned-release-zip') { 'jp.penguin.purebase-0.1.0.zip' } else { $_ }
                    })
            }
            $changedManifest = Join-Path $artifacts.root 'stable-release-zip-evidence.json'
            Write-Json -Path $changedManifest -Value $copy
            return $changedManifest
        }
    }
}
