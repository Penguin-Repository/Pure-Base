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

    [Parameter(Mandatory = $true)]
    [string[]]$Arguments,

    [Parameter(Mandatory = $true)]
    [string]$UnityLogPath,

    [Parameter(Mandatory = $true)]
    [string]$DiagnosticPath,

    [ValidateRange(30, 7200)]
    [int]$TimeoutSeconds = 900,

    [ValidateRange(1, 30)]
    [int]$PollSeconds = 2,

    [string]$RunDescription = 'Unity batchmode'
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

function Get-UnityLogTail {
    param([int]$LineCount = 300)

    if (-not (Test-Path -LiteralPath $UnityLogPath -PathType Leaf)) {
        return [string]::Empty
    }

    return ((Get-Content -LiteralPath $UnityLogPath -Tail $LineCount) -join [Environment]::NewLine)
}

function Stop-UnityProcessTree {
    param([Parameter(Mandatory = $true)][int]$ProcessId)

    $taskKillPath = Join-Path $env:SystemRoot 'System32/taskkill.exe'
    if (Test-Path -LiteralPath $taskKillPath -PathType Leaf) {
        $taskKillOutput = & $taskKillPath /PID $ProcessId /T /F 2>&1
        "TaskkillOutput=$($taskKillOutput -join ' ')" | Add-Content -LiteralPath $DiagnosticPath
        return
    }

    Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
}

$resolvedEditorPath = [System.IO.Path]::GetFullPath($UnityEditorPath)
if (-not (Test-Path -LiteralPath $resolvedEditorPath -PathType Leaf)) {
    throw "Unity Editor executable was not found at '$resolvedEditorPath'."
}

foreach ($outputPath in @($UnityLogPath, $DiagnosticPath)) {
    $parentDirectory = Split-Path -Parent $outputPath
    if (-not [string]::IsNullOrEmpty($parentDirectory)) {
        New-Item -ItemType Directory -Path $parentDirectory -Force | Out-Null
    }
}

$argumentText = ($Arguments | ForEach-Object { ConvertTo-NativeArgument -Value $_ }) -join ' '
@(
    "RunDescription=$RunDescription",
    "UnityEditorPath=$resolvedEditorPath",
    "UnityLogPath=$UnityLogPath",
    "TimeoutSeconds=$TimeoutSeconds",
    "PollSeconds=$PollSeconds",
    "Arguments=$argumentText",
    "StartedUtc=$([DateTime]::UtcNow.ToString('O'))"
) | Set-Content -LiteralPath $DiagnosticPath

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $resolvedEditorPath
$startInfo.Arguments = $argumentText
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$fatalLogPattern = '(?im)(Scripts have compiler errors\.|Aborting batchmode due to failure:|\berror CS\d{4}:)'

try {
    if (-not $process.Start()) {
        throw "Unity $RunDescription process could not be started."
    }

    "ProcessId=$($process.Id)" | Add-Content -LiteralPath $DiagnosticPath
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $fatalLogDetected = $false
    $timedOut = $false

    while (-not $process.WaitForExit($PollSeconds * 1000)) {
        $logTail = Get-UnityLogTail -LineCount 200
        if (-not [string]::IsNullOrEmpty($logTail) -and $logTail -match $fatalLogPattern) {
            $fatalLogDetected = $true
            "WatchdogReason=Fatal Unity log pattern detected." | Add-Content -LiteralPath $DiagnosticPath
            break
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            $timedOut = $true
            "WatchdogReason=Timeout after $TimeoutSeconds seconds." | Add-Content -LiteralPath $DiagnosticPath
            break
        }
    }

    if ($fatalLogDetected -or $timedOut) {
        Stop-UnityProcessTree -ProcessId $process.Id
        [void]$process.WaitForExit(10000)

        $failureTail = Get-UnityLogTail -LineCount 500
        if (-not [string]::IsNullOrEmpty($failureTail)) {
            $failureTail | Out-Host
        }
        Get-Content -LiteralPath $DiagnosticPath | Out-Host

        if ($fatalLogDetected) {
            throw "Unity $RunDescription was terminated after a fatal compiler/import error was detected."
        }

        throw "Unity $RunDescription exceeded the $TimeoutSeconds-second watchdog timeout."
    }

    $process.WaitForExit()
    $exitCode = $process.ExitCode
    "ExitCode=$exitCode" | Add-Content -LiteralPath $DiagnosticPath
    "CompletedUtc=$([DateTime]::UtcNow.ToString('O'))" | Add-Content -LiteralPath $DiagnosticPath

    $finalTail = Get-UnityLogTail -LineCount 500
    if (-not [string]::IsNullOrEmpty($finalTail)) {
        $finalTail | Out-Host
    }
    else {
        "Unity did not create '$UnityLogPath'." | Add-Content -LiteralPath $DiagnosticPath
    }
    Get-Content -LiteralPath $DiagnosticPath | Out-Host

    if ($exitCode -ne 0) {
        throw "Unity $RunDescription failed with exit code $exitCode. Diagnostics: '$DiagnosticPath'."
    }
}
catch {
    "Exception=$($_.Exception.Message)" | Add-Content -LiteralPath $DiagnosticPath
    if (-not $process.HasExited) {
        Stop-UnityProcessTree -ProcessId $process.Id
    }
    throw
}
finally {
    $process.Dispose()
}
