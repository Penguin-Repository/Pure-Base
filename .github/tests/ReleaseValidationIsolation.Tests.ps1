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

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Describe 'Release validation Unity import isolation' {
    BeforeAll {
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $workflow = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release-validation.yml') -Raw) -replace "`r`n", "`n"
        $projectSetup = (Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/New-PureBaseCiProject.ps1') -Raw) -replace "`r`n", "`n"
    }

    It 'keeps the audited checkout in the release runner compatible package layout' {
        $workflow | Should -Match '(?m)^\s*SOURCE_PROJECT_ROOT:\s*\$\{\{ github\.workspace \}\}\\PureBaseSource\s*(?:#.*)?$'
        $workflow | Should -Match '(?m)^\s*PACKAGE_ROOT:\s*\$\{\{ github\.workspace \}\}\\PureBaseSource\\Packages\\jp\.penguin\.purebase\s*(?:#.*)?$'
        $workflow | Should -Match '(?m)^\s*path:\s*PureBaseSource\\Packages\\jp\.penguin\.purebase\s*(?:#.*)?$'
    }

    It 'keeps disposable Unity package destinations separate from the audited workspace' {
        $workflow | Should -Match '(?m)^\s*CI_PROJECT_ROOT:\s*\$\{\{ github\.workspace \}\}\\PureBaseCi\s*(?:#.*)?$'
        $workflow | Should -Match '(?m)^\s*UNITY_PACKAGE_ROOT:\s*\$\{\{ github\.workspace \}\}\\PureBaseCi\\Packages\\jp\.penguin\.purebase\s*(?:#.*)?$'
        $workflow | Should -Match '(?m)^\s*UNITY_SHADER_CORE_ROOT:\s*\$\{\{ github\.workspace \}\}\\PureBaseCi\\Packages\\jp\.lilxyzw\.shadercore\s*(?:#.*)?$'
    }

    It 'stages disposable package copies without Pure Base Git metadata' {
        $workflow | Should -Match ([regex]::Escape('$null = & robocopy $env:PACKAGE_ROOT $env:UNITY_PACKAGE_ROOT /MIR /XD .git'))
        $workflow | Should -Match ([regex]::Escape('$null = & robocopy $sourceShaderCoreRoot $env:UNITY_SHADER_CORE_ROOT /MIR'))
        $workflow | Should -Match ([regex]::Escape('& "$env:UNITY_PACKAGE_ROOT/.github/scripts/New-PureBaseCiProject.ps1"'))
    }

    It 'normalizes only defined accepted native copy exit codes as the final operation' {
        $projectSetup.TrimEnd() | Should -Match '(?s)if \(\(Test-Path Variable:LASTEXITCODE\) -and \$LASTEXITCODE -lt 8\) \{ \$global:LASTEXITCODE = 0 \}\z'
    }

    It 'builds and audits the release from the unchanged source checkout' {
        $workflow | Should -Match ([regex]::Escape('-ProjectRoot $env:SOURCE_PROJECT_ROOT'))
        $workflow | Should -Match ([regex]::Escape('& "$env:PACKAGE_ROOT/Tests/Release/Run-PureBaseReleaseValidation.ps1"'))
        $workflow | Should -Match ([regex]::Escape('-PackageRoot $env:PACKAGE_ROOT'))
        ([regex]::Matches($workflow, [regex]::Escape('git status --porcelain --untracked-files=all'))).Count | Should -Be 2
        $workflow | Should -Match ([regex]::Escape("Write-Host 'Repository working tree changes:'"))
    }
}
