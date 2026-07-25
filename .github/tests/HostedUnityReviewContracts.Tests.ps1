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
        $dailyWorkflow | Should -Match [regex]::Escape('group: daily-${{ github.event.pull_request.number || github.ref }}')
        $dailyWorkflow | Should -Not -Match [regex]::Escape('group: daily-${{ github.event.pull_request.head.ref || github.ref_name }}')
    }

    It 'treats Unity editor cache misses as telemetry instead of fatal errors' {
        $dailyWorkflow | Should -Match 'cache handoff missed; setup-unity-cli installed/restored the Editor in this job, so validation will continue'
        $releaseWorkflow | Should -Match 'cache handoff missed; setup-unity-cli installed/restored the Editor in this job, so validation will continue'
        $dailyWorkflow | Should -Not -Match 'throw "Unity Editor cache was not available'
        $releaseWorkflow | Should -Not -Match 'throw "Unity Editor cache was not available'
    }

    It 'passes a real Unity.exe only to audited release validation' {
        $releaseWorkflow | Should -Match '-RealEditorPathOutputFile \$realEditorPathFile'
        $releaseWorkflow | Should -Match 'REAL_UNITY_EDITOR_PATH=\$realEditorPath'
        $releaseWorkflow | Should -Match '-UnityEditorPath \$env:REAL_UNITY_EDITOR_PATH'
        $releaseWorkflow | Should -Match '& \$env:UNITY_EDITOR_PATH\s+`\s*-batchmode'
        $resolverScript | Should -Match 'RealEditorPathOutputFile'
        $resolverScript | Should -Match 'real Unity\.exe because its audited runner intentionally rejects wrapper executables'
    }

    It 'uses pwsh-compatible retried runtime downloads' {
        $resolverScript | Should -Match "\$ProgressPreference = 'SilentlyContinue'"
        $resolverScript | Should -Match 'function Invoke-DownloadWithRetry'
        $resolverScript | Should -Match 'MaximumAttempts = 3'
        $resolverScript | Should -Not -Match 'UseBasicParsing'
    }

    It 'aligns the configure watchdog with the workflow timeout' {
        $watchdogScript | Should -Match "if \(\$UnityArguments -contains '-runTests'\) \{ 3600 \} else \{ 1800 \}"
        $watchdogScript | Should -Match 'same 30-minute'
    }

    It 'keeps documentation and diagnostics free of stale reviewed values' {
        $ciDocumentation | Should -Match 'GitHub-hosted `windows-2022` runners'
        $ciDocumentation | Should -Not -Match 'GitHub-hosted `windows-latest` runners'
        $shadowDiagnostics | Should -Not -Match '341-352'
        $shadowDiagnostics | Should -Match 'committed range'
    }
}
