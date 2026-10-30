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

# Exercises the actual release provision script using isolated non-executable fixtures.
Describe 'Release Unity runtime prerequisites' {
    BeforeAll {
        $script:producer = Join-Path $PSScriptRoot '../scripts/Install-ReleaseUnityRuntimePrerequisites.ps1'
        $script:realTestPath = Get-Command Test-Path -CommandType Cmdlet
        function Invoke-ProvisionFixture {
            $fixture.error = $null
            try {
                $fixture.output = @(& $producer -WorkflowRunId '1234' -WorkflowRunAttempt '1' -WorkflowJob 'validate' `
                    -IntendedSha ('a' * 40) -CheckoutSha ('b' * 40) 6> $fixture.hostLog 2> $fixture.errorLog)
            }
            catch { $fixture.error = $_ }
            if ([IO.File]::Exists($fixture.report)) {
                $fixture.public = [IO.File]::ReadAllText($fixture.report)
                if ($fixture.public.TrimStart().StartsWith('{')) { $fixture.state = $fixture.public | ConvertFrom-Json }
            }
            $fixture.console = @($fixture.hostLog, $fixture.errorLog) | ForEach-Object {
                if ([IO.File]::Exists($_)) { [IO.File]::ReadAllText($_) }
            }
        }
        function Add-SyntheticPair {
            param([string]$Suffix)
            foreach ($prefix in @('msvcr', 'msvcp')) {
                [IO.File]::WriteAllText((Join-Path $env:WINDIR "System32/$prefix$Suffix.dll"), 'fixture')
            }
        }
    }
    BeforeEach {
        $savedEnvironment = @{}
        foreach ($name in @('OS', 'WINDIR', 'RUNNER_TEMP', 'PROCESSOR_ARCHITECTURE')) {
            $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        }
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $env:OS = 'Windows_NT'
        $env:PROCESSOR_ARCHITECTURE = 'AMD64'
        $env:WINDIR = Join-Path $root 'windows'
        $env:RUNNER_TEMP = Join-Path $root 'temp'
        [IO.Directory]::CreateDirectory((Join-Path $env:WINDIR 'System32')) | Out-Null
        [IO.Directory]::CreateDirectory($env:RUNNER_TEMP) | Out-Null
        $script:fixture = @{
            choco = (Join-Path $root 'choco.exe'); hostLog = (Join-Path $root 'host.txt'); errorLog = (Join-Path $root 'error.txt')
            report = (Join-Path $env:RUNNER_TEMP 'PureBase-Release-Validation-1234-1/runtime-prerequisites/report.json')
            calls = [Collections.Generic.List[object]]::new(); error = $null; public = ''; console = ''; state = $null
            version = '2.7.4'; versionExit = 0; exits = @(0, 0); createDll = $true; startFailure = $false
            absent = $false; finalMissing = $false; dllProbes = 0
        }
        [IO.File]::WriteAllText($fixture.choco, 'non-executable fixture')
        Mock Get-Command -ParameterFilter { $Name -eq 'choco.exe' } {
            if ($fixture.absent) { throw 'not found' }
            [pscustomobject]@{ Source = $fixture.choco }
        }
        Mock Start-Process {
            $fixture.calls.Add([pscustomobject]@{ args = @($ArgumentList); wait = [bool]$Wait; pass = [bool]$PassThru })
            if ($fixture.startFailure -and $ArgumentList[0] -eq 'install') { throw 'PRIVATE_SENTINEL' }
            [IO.File]::WriteAllText($RedirectStandardOutput, $(if ($ArgumentList[0] -eq '--version') { $fixture.version } else { 'PRIVATE_SENTINEL' }))
            [IO.File]::WriteAllText($RedirectStandardError, 'PRIVATE_SENTINEL')
            if ($ArgumentList[0] -eq '--version') { return [pscustomobject]@{ ExitCode = $fixture.versionExit } }
            $index = @($fixture.calls | Where-Object { $_.args[0] -eq 'install' }).Count - 1
            if ($fixture.createDll) { Add-SyntheticPair $(if ($ArgumentList[1] -eq 'vcredist2010') { '100' } else { '120' }) }
            return [pscustomobject]@{ ExitCode = $fixture.exits[$index] }
        }
        Mock Test-Path -ParameterFilter { $LiteralPath -match 'msvc[pr](100|120)\.dll$' } {
            $fixture.dllProbes++
            if ($fixture.finalMissing -and $fixture.dllProbes -gt 8) { return $false }
            & $realTestPath -LiteralPath $LiteralPath -PathType $PathType
        }
    }
    AfterEach {
        foreach ($name in @('OS', 'WINDIR', 'RUNNER_TEMP', 'PROCESSOR_ARCHITECTURE')) {
            if ($null -eq $savedEnvironment[$name]) { Remove-Item -LiteralPath "Env:$name" -ErrorAction SilentlyContinue }
            else { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
        }
    }
    It 'requests both fixed packages in order and observes only accepted synchronous results' {
        $fixture.exits = @(0, 3010)
        Invoke-ProvisionFixture
        $fixture.error | Should -BeNullOrEmpty
        $fixture.calls.Count | Should -Be 3
        $fixture.calls[0].args | Should -Be @('--version')
        $fixture.calls[1].args | Should -Be @('install', 'vcredist2010', '--version=10.0.40219.32503', '--source=https://community.chocolatey.org/api/v2/', '-y', '--no-progress', '--requirechecksum', '--use-package-exit-codes')
        $fixture.calls[2].args | Should -Be @('install', 'vcredist2013', '--version=12.0.40660.20180427', '--source=https://community.chocolatey.org/api/v2/', '-y', '--no-progress', '--requirechecksum', '--use-package-exit-codes')
        @($fixture.calls | Where-Object { -not $_.wait -or -not $_.pass }).Count | Should -Be 0
        $fixture.state.packages[1].rebootRequired | Should -BeTrue
        $fixture.state.finalFiles | Should -Be @($true, $true, $true, $true)
        ($fixture.public + ($fixture.console -join '')) | Should -Not -Match 'PRIVATE_SENTINEL'
    }
    It 'skips complete pairs and requests only the missing pair' -ForEach @(
        @{ Both = $true; Installs = 0 }, @{ Both = $false; Installs = 1 }
    ) {
        Add-SyntheticPair '100'
        if ($Both) { Add-SyntheticPair '120' }
        Invoke-ProvisionFixture
        $fixture.error | Should -BeNullOrEmpty
        $fixture.state.packages[0].status | Should -Be 'ExistingRuntime'
        $fixture.state.packages[0].installRequested | Should -BeFalse
        $fixture.calls.Count | Should -Be ($Installs + 1)
        if (-not $Both) { $fixture.calls[1].args[1] | Should -Be 'vcredist2013' }
    }
    It 'stops before installation for missing or incompatible Chocolatey' -ForEach @(
        @{ Mode = 'absent' }, @{ Mode = 'version' }, @{ Mode = 'exit' }
    ) {
        if ($Mode -eq 'absent') { $fixture.absent = $true }
        if ($Mode -eq 'version') { $fixture.version = '1.9.0' }
        if ($Mode -eq 'exit') { $fixture.versionExit = 1 }
        Invoke-ProvisionFixture
        $fixture.error | Should -Not -BeNullOrEmpty
        @($fixture.calls | Where-Object { $_.args[0] -eq 'install' }).Count | Should -Be 0
    }
    It 'stops on rejected native code, start exception or missing postcheck' -ForEach @(
        @{ Mode = 'exit'; Primary = 'ExitRejected' }, @{ Mode = 'start'; Primary = 'ProcessStartFailed' },
        @{ Mode = 'missing'; Primary = 'PostcheckMissing' }
    ) {
        if ($Mode -eq 'exit') { $fixture.exits = @(1638) }
        if ($Mode -eq 'start') { $fixture.startFailure = $true }
        if ($Mode -eq 'missing') { $fixture.createDll = $false }
        Invoke-ProvisionFixture
        $fixture.error | Should -Not -BeNullOrEmpty
        $fixture.state.primary | Should -Be $Primary
        $fixture.state.packages[1].status | Should -Be 'NotReached'
        ($fixture.public + ($fixture.console -join '') + $fixture.error.ToString()) | Should -Not -Match 'PRIVATE_SENTINEL'
    }
    It 'fails if the final recheck loses a previously observed DLL' {
        $fixture.finalMissing = $true
        Invoke-ProvisionFixture
        $fixture.state.primary | Should -Be 'FinalFilesMissing'
        $fixture.error | Should -Not -BeNullOrEmpty
    }
    It 'preserves native rejection when the final report save fails' {
        $fixture.exits = @(1641)
        Mock Set-Content -ParameterFilter { $LiteralPath -like '*report.json' -and $Value -match 'ExitRejected' } { throw 'PRIVATE_SENTINEL' }
        Invoke-ProvisionFixture
        $fixture.error.ToString() | Should -Be 'RuntimePrerequisiteFailed: ExitRejected; secondary=ReportSaveFailed'
        $fixture.error.ToString() | Should -Not -Match 'PRIVATE_SENTINEL'
    }
    It 'does not overwrite an existing report or request installation' {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fixture.report)) | Out-Null
        [IO.File]::WriteAllText($fixture.report, 'preserve')
        Invoke-ProvisionFixture
        $fixture.error | Should -Not -BeNullOrEmpty
        [IO.File]::ReadAllText($fixture.report) | Should -Be 'preserve'
        $fixture.calls.Count | Should -Be 0
    }
    It 'fails closed when the mandatory initial report cannot be saved' {
        Mock Set-Content -ParameterFilter { $LiteralPath -like '*report.json' } { throw 'PRIVATE_SENTINEL' }
        Invoke-ProvisionFixture
        $fixture.error.ToString() | Should -Be 'RuntimePrerequisiteFailed: EvidenceInitializationFailed; secondary=ReportSaveFailed'
        $fixture.calls.Count | Should -Be 0
    }
    It 'fails when final report saving fails after successful DLL checks' {
        Mock Set-Content -ParameterFilter { $LiteralPath -like '*report.json' -and $Value -match '"finalFiles": \[\s*true' } {
            throw 'PRIVATE_SENTINEL'
        }
        Invoke-ProvisionFixture
        $fixture.error.ToString() | Should -Be 'RuntimePrerequisiteFailed: None; secondary=ReportSaveFailed'
        $fixture.error.ToString() | Should -Not -Match 'PRIVATE_SENTINEL'
    }
}
