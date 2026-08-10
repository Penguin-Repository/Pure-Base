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

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityEditorPath,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$UnityArguments
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-NativeArgument {
    param([AllowEmptyString()][string]$Value)

    if ([string]::IsNullOrEmpty($Value)) {
        return '""'
    }

    if ($Value -notmatch '[\s"]') {
        return $Value
    }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashCount = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $backslashCount++
            continue
        }

        if ($character -eq '"') {
            [void]$builder.Append('\', ($backslashCount * 2) + 1)
            [void]$builder.Append('"')
            $backslashCount = 0
            continue
        }

        [void]$builder.Append('\', $backslashCount)
        $backslashCount = 0
        [void]$builder.Append($character)
    }

    [void]$builder.Append('\', $backslashCount * 2)
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Stop-ProcessTree {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    $taskKillPath = Join-Path $env:SystemRoot 'System32/taskkill.exe'
    if (Test-Path -LiteralPath $taskKillPath -PathType Leaf) {
        & $taskKillPath /PID $ProcessId /T /F 2>&1 | Out-Host
        return
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

$editorPath = [System.IO.Path]::GetFullPath($UnityEditorPath)
if (-not (Test-Path -LiteralPath $editorPath -PathType Leaf)) {
    Write-Error "Unity Editor executable was not found at '$editorPath'."
    exit 1
}

$logPath = $null
for ($index = 0; $index -lt $UnityArguments.Count - 1; $index++) {
    if ($UnityArguments[$index] -ieq '-logFile') {
        $logPath = $UnityArguments[$index + 1].Trim('"')
        break
    }
}

if ([string]::IsNullOrWhiteSpace($logPath)) {
    $logPath = Join-Path $env:RUNNER_TEMP "PureBase-Unity-$PID.log"
}

$logPath = [System.IO.Path]::GetFullPath($logPath)
$logDirectory = Split-Path -Parent $logPath
if (-not [string]::IsNullOrEmpty($logDirectory)) {
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
}

$diagnosticPath = [System.IO.Path]::ChangeExtension($logPath, 'Watchdog.txt')
# Test execution receives one hour. Configure/import receives the same 30-minute
# ceiling as the workflow step, so the watchdog catches hangs without preempting a
# legitimately slow first import.
$timeoutSeconds = if ($UnityArguments -contains '-runTests') { 3600 } else { 1800 }
$argumentText = ($UnityArguments | ForEach-Object { ConvertTo-NativeArgument -Value $_ }) -join ' '
$fatalPattern = '(?im)(Scripts have compiler errors\.|Aborting batchmode due to failure:|\berror CS\d{4}:)'

@(
    "UnityEditorPath=$editorPath",
    "UnityLogPath=$logPath",
    "TimeoutSeconds=$timeoutSeconds",
    "Arguments=$argumentText",
    "StartedUtc=$([DateTime]::UtcNow.ToString('O'))"
) | Set-Content -LiteralPath $diagnosticPath

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $editorPath
$startInfo.Arguments = $argumentText
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo

try {
    if (-not $process.Start()) {
        throw 'Unity process could not be started.'
    }

    "ProcessId=$($process.Id)" | Add-Content -LiteralPath $diagnosticPath
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    $failureReason = $null

    while (-not $process.WaitForExit(2000)) {
        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            $tail = (Get-Content -LiteralPath $logPath -Tail 200) -join [Environment]::NewLine
            if ($tail -match $fatalPattern) {
                $failureReason = 'fatal compiler/import error detected in Unity log'
                break
            }
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            $failureReason = "watchdog timeout after $timeoutSeconds seconds"
            break
        }
    }

    if ($null -ne $failureReason) {
        "WatchdogReason=$failureReason" | Add-Content -LiteralPath $diagnosticPath
        Stop-ProcessTree -ProcessId $process.Id
        [void]$process.WaitForExit(10000)

        if (Test-Path -LiteralPath $logPath -PathType Leaf) {
            Get-Content -LiteralPath $logPath -Tail 500 | Out-Host
        }
        Get-Content -LiteralPath $diagnosticPath | Out-Host
        Write-Error "Unity was terminated by the Pure-Base watchdog: $failureReason."
        exit 1
    }

    $process.WaitForExit()
    "ExitCode=$($process.ExitCode)" | Add-Content -LiteralPath $diagnosticPath
    "CompletedUtc=$([DateTime]::UtcNow.ToString('O'))" | Add-Content -LiteralPath $diagnosticPath
    exit $process.ExitCode
}
catch {
    "Exception=$($_.Exception.Message)" | Add-Content -LiteralPath $diagnosticPath
    if ($process.Id -gt 0 -and -not $process.HasExited) {
        Stop-ProcessTree -ProcessId $process.Id
    }
    Write-Error $_
    exit 1
}
finally {
    $process.Dispose()
}

