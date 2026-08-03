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
    }

    It 'keeps the audited checkout outside the generated Unity project' {
        $workflow | Should -Match '(?m)^  PACKAGE_ROOT: \$\{\{ github\.workspace \}\}\\PureBaseSource$'
        $workflow | Should -Match '(?m)^  UNITY_PACKAGE_ROOT: \$\{\{ github\.workspace \}\}\\PureBaseCi\\Packages\\jp\.penguin\.purebase$'
        $workflow | Should -Match '(?m)^          path: PureBaseSource$'
    }

    It 'stages a disposable package copy without Git metadata' {
        $workflow | Should -Match ([regex]::Escape('$null = & robocopy $env:PACKAGE_ROOT $env:UNITY_PACKAGE_ROOT /MIR /XD .git'))
        $workflow | Should -Match ([regex]::Escape('& "$env:UNITY_PACKAGE_ROOT/.github/scripts/New-PureBaseCiProject.ps1"'))
    }

    It 'builds and audits the release from the unchanged source checkout' {
        $workflow | Should -Match ([regex]::Escape('& "$env:PACKAGE_ROOT/Tests/Release/Run-PureBaseReleaseValidation.ps1"'))
        $workflow | Should -Match ([regex]::Escape('-PackageRoot $env:PACKAGE_ROOT'))
        $workflow | Should -Match ([regex]::Escape('git status --porcelain --untracked-files=all'))
        $workflow | Should -Match ([regex]::Escape("Write-Host 'Repository working tree changes:'"))
    }
}
