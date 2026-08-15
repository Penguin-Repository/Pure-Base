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

# Compares canonical Shader-Core host rows with the release bootstrap contracts.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Describe 'Release Shader-Core bootstrap cardinality contracts' {
    BeforeAll {
        $script:packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $script:manifestPath = Join-Path $script:packageRoot 'Tests/Config/shader-core-test-hosts.json'
        $script:consumerSourcePath = Join-Path $script:packageRoot 'Tests/Release/ConsumerProject/Assets/Editor/PureBaseConsumerReleaseTests.cs'
        $script:runnerPath = Join-Path $script:packageRoot 'Tests/Release/Run-PureBaseReleaseValidation.ps1'

        $script:canonicalManifest = Get-Content -LiteralPath $script:manifestPath -Raw | ConvertFrom-Json
        $script:canonicalHosts = @($script:canonicalManifest.hosts)
        $script:canonicalMapping = [ordered]@{}
        foreach ($manifestHost in $script:canonicalHosts) {
            $singleModuleProperty = $manifestHost.PSObject.Properties['moduleUniqueId']
            $multipleModulesProperty = $manifestHost.PSObject.Properties['moduleUniqueIds']
            $modules = if ($null -ne $singleModuleProperty) {
                @([string]$singleModuleProperty.Value)
            }
            elseif ($null -ne $multipleModulesProperty) {
                @($multipleModulesProperty.Value | ForEach-Object { [string]$_ })
            }
            else {
                @()
            }
            $script:canonicalMapping[[string]$manifestHost.shaderName] = $modules
        }
        foreach ($productShaderName in @('PureBase/Unlit', 'PureBase/Toon', 'PureBase/Hybrid', 'PureBase/PBR')) {
            $script:canonicalMapping[$productShaderName] = @()
        }

        $runnerSource = Get-Content -LiteralPath $script:runnerPath -Raw
        $entryPointIndex = $runnerSource.IndexOf("`n`$packageRoot = Get-PackageGitRoot")
        $libraryStartIndex = $runnerSource.IndexOf('Set-StrictMode -Version Latest')
        if ($entryPointIndex -lt 0 -or $libraryStartIndex -lt 0 -or $entryPointIndex -le $libraryStartIndex) {
            throw 'The release runner entry point could not be isolated from its library functions.'
        }
        $script:libraryPath = Join-Path ([IO.Path]::GetTempPath()) ('PureBaseReleaseShaderCoreContracts-' + [guid]::NewGuid().ToString('N') + '.ps1')
        [IO.File]::WriteAllText(
            $script:libraryPath,
            $runnerSource.Substring($libraryStartIndex, $entryPointIndex - $libraryStartIndex),
            (New-Object Text.UTF8Encoding($false))
        )
        . $script:libraryPath

        $script:stagedRoot = Join-Path ([IO.Path]::GetTempPath()) ('PureBaseReleaseShaderCoreFixture-' + [guid]::NewGuid().ToString('N'))
        $stagedManifestPath = Join-Path $script:stagedRoot (Get-CanonicalShaderCoreConfigDestination).Replace('/', '\')
        New-Item -ItemType Directory -Path (Split-Path -Parent $stagedManifestPath) -Force | Out-Null
        Copy-Item -LiteralPath $script:manifestPath -Destination $stagedManifestPath

        function Get-MappingContractViolations {
            param(
                [Parameter(Mandatory = $true)]$Actual,
                [Parameter(Mandatory = $true)]$Expected
            )

            $violations = [Collections.Generic.List[string]]::new()
            if ($Actual.Count -ne $Expected.Count) {
                $violations.Add("row count expected $($Expected.Count), found $($Actual.Count)")
            }

            $actualNames = @($Actual.Keys | Sort-Object)
            $expectedNames = @($Expected.Keys | Sort-Object)
            if (($actualNames -join "`n") -ne ($expectedNames -join "`n")) {
                $violations.Add('shader-name set does not match the canonical manifest')
            }

            foreach ($shaderName in $Expected.Keys) {
                if (-not $Actual.Contains($shaderName)) {
                    continue
                }
                $actualModules = @($Actual[$shaderName]) -join "`n"
                $expectedModules = @($Expected[$shaderName]) -join "`n"
                if ($actualModules -ne $expectedModules) {
                    $violations.Add("module ID array differs for '$shaderName'")
                }
            }
            return @($violations)
        }
    }

    AfterAll {
        if ((Test-Path -LiteralPath 'variable:script:libraryPath') -and (Test-Path -LiteralPath $script:libraryPath)) {
            Remove-Item -LiteralPath $script:libraryPath -Force -ErrorAction SilentlyContinue
        }
        if ((Test-Path -LiteralPath 'variable:script:stagedRoot') -and (Test-Path -LiteralPath $script:stagedRoot)) {
            Remove-Item -LiteralPath $script:stagedRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'parses the canonical 13-host manifest and the single production host-count assignment' {
        $script:canonicalManifest.schemaVersion | Should -Be 1
        $script:canonicalHosts.Count | Should -Be 13
        $script:canonicalMapping.Count | Should -Be 17
        (@($script:canonicalMapping['PureBase/Tests/ShaderCore/ToonShadow']) -join "`n") | Should -BeExactly 'jp.penguin.purebase.tests.shadercore.toonshadow'
        (@($script:canonicalMapping['PureBase/Tests/ShaderCore/ToonOpenLitGamma']) -join "`n") | Should -BeExactly 'jp.penguin.purebase.tests.shadercore.toonopenlitgamma'

        $consumerSource = Get-Content -LiteralPath $script:consumerSourcePath -Raw
        $assignments = [regex]::Matches($consumerSource, '(?m)^\s*private\s+const\s+int\s+ExpectedHostCount\s*=\s*(?<value>\d+)\s*;\s*$')
        $assignments.Count | Should -Be 1
        [int]$assignments[0].Groups['value'].Value | Should -Be $script:canonicalHosts.Count
    }

    It 'matches the first-bootstrap production profile to canonical rows' {
        $firstProfile = Get-FirstBootstrapShaderCoreSettingsProfile
        @(Get-MappingContractViolations -Actual $firstProfile -Expected $script:canonicalMapping) | Should -BeNullOrEmpty
    }

    It 'matches the staged canonical fixture through the production profile loader' {
        $canonicalProfile = Get-CanonicalShaderCoreSettingsProfile -ConsumerRoot $script:stagedRoot
        @(Get-MappingContractViolations -Actual $canonicalProfile -Expected $script:canonicalMapping) | Should -BeNullOrEmpty
    }
}