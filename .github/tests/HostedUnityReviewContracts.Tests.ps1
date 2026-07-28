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
        $dailyWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/daily.yml') -Raw
        $releaseWorkflow = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/workflows/release-validation.yml') -Raw
        $resolverScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/Resolve-UnityEditorPath.ps1') -Raw
        $watchdogScript = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/scripts/UnityWatchdogProxy.ps1') -Raw
        $ciDocumentation = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github/CI.md') -Raw
        $shadowDiagnostics = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Tests/Daily/Editor/PureBaseShadowObservationDiagnosticsTests.cs') -Raw
    }

    It 'uses repository-unique PR concurrency for Daily' {
        $dailyWorkflow.Contains('group: daily-${{ github.event.pull_request.number || github.ref }}') | Should -BeTrue
        $dailyWorkflow.Contains('group: daily-${{ github.event.pull_request.head.ref || github.ref_name }}') | Should -BeFalse
    }

    It 'treats Unity editor cache misses as telemetry instead of fatal errors' {
        $dailyWorkflow.Contains('cache handoff missed; setup-unity-cli installed/restored the Editor in this job, so validation will continue') | Should -BeTrue
        $releaseWorkflow.Contains('cache handoff missed; setup-unity-cli installed/restored the Editor in this job, so validation will continue') | Should -BeTrue
        $dailyWorkflow.Contains('throw "Unity Editor cache was not available') | Should -BeFalse
        $releaseWorkflow.Contains('throw "Unity Editor cache was not available') | Should -BeFalse
    }

    It 'passes a real Unity.exe only to audited release validation' {
        $releaseWorkflow.Contains('-RealEditorPathOutputFile $realEditorPathFile') | Should -BeTrue
        $releaseWorkflow.Contains('REAL_UNITY_EDITOR_PATH=$realEditorPath') | Should -BeTrue
        $releaseWorkflow.Contains('-UnityEditorPath $env:REAL_UNITY_EDITOR_PATH') | Should -BeTrue
        $releaseWorkflow.Contains('& $env:UNITY_EDITOR_PATH `') | Should -BeTrue
        $resolverScript.Contains('RealEditorPathOutputFile') | Should -BeTrue
        $resolverScript.Contains('real Unity.exe because its audited runner intentionally rejects wrapper executables') | Should -BeTrue
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
