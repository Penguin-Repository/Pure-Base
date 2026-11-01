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

# Provisions the two required runtime pairs and writes only finite public evidence.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkflowRunId,
    [Parameter(Mandatory)][string]$WorkflowRunAttempt,
    [Parameter(Mandatory)][string]$WorkflowJob,
    [Parameter(Mandatory)][string]$IntendedSha,
    [Parameter(Mandatory)][string]$CheckoutSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$source = 'https://community.chocolatey.org/api/v2/'

function Assert-SafePath {
    param([string]$Path, [string]$Root)

    $full = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'OutsideRoot' }
    for ($node = $full; $node; $node = [IO.Path]::GetDirectoryName($node)) {
        if (Test-Path -LiteralPath $node) {
            if ((Get-Item -LiteralPath $node -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'ReparsePath' }
        }
    }
}

function Save-Report {
    Assert-SafePath -Path $reportPath -Root $env:RUNNER_TEMP
    $report.utc = [DateTime]::UtcNow.ToString('o')
    $json = ConvertTo-Json -InputObject $report -Depth 8
    Set-Content -LiteralPath $reportPath -Value $json -Encoding utf8NoBOM -ErrorAction Stop
}

function Get-PairPresence {
    param([string[]]$Names)

    return @($Names | ForEach-Object {
        [bool](Test-Path -LiteralPath (Join-Path $systemDirectory $_) -PathType Leaf)
    })
}

function Invoke-Chocolatey {
    param([string[]]$Arguments, [string]$Label)

    $stdout = Join-Path $privateRoot "$Label.stdout"
    $stderr = Join-Path $privateRoot "$Label.stderr"
    Assert-SafePath -Path $stdout -Root $privateRoot
    Assert-SafePath -Path $stderr -Root $privateRoot
    if ((Test-Path -LiteralPath $stdout) -or (Test-Path -LiteralPath $stderr)) { throw 'LogCollision' }
    return Start-Process -FilePath $chocoPath -ArgumentList $Arguments -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -ErrorAction Stop
}

function Invoke-RuntimePackage {
    param($Package)

    $Package.pre = @(Get-PairPresence $Package.files)
    if ($Package.pre -notcontains $false) {
        $Package.status = 'ExistingRuntime'
        $Package.post = @($Package.pre)
        return
    }
    $Package.status = 'InstallRequested'
    $Package.installRequested = $true
    $report.primary = 'ProcessStartFailed'
    $arguments = @('install', $Package.name, "--version=$($Package.version)", "--source=$source", '-y',
        '--no-progress', '--requirechecksum', '--use-package-exit-codes')
    $process = Invoke-Chocolatey -Arguments $arguments -Label $Package.name
    $Package.exitCode = [int]$process.ExitCode
    $Package.rebootRequired = $Package.exitCode -eq 3010
    $report.primary = 'ExitRejected'
    if ($Package.exitCode -notin @(0, 3010)) { throw 'ExitRejected' }
    $Package.post = @(Get-PairPresence $Package.files)
    $report.primary = 'PostcheckMissing'
    if ($Package.post -contains $false) { throw 'PostcheckMissing' }
    $report.primary = 'None'
    Save-Report
}

if ($WorkflowRunId -notmatch '^[0-9]+$' -or $WorkflowRunAttempt -notmatch '^[1-9][0-9]*$' -or
    $WorkflowJob -notmatch '^[A-Za-z0-9_-]+$' -or $IntendedSha -cnotmatch '^[0-9a-f]{40}$' -or
    $CheckoutSha -cnotmatch '^[0-9a-f]{40}$') { throw 'RuntimePrerequisiteFailed: IdentityInvalid; secondary=None' }
if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP) -or [string]::IsNullOrWhiteSpace($env:WINDIR)) {
    throw 'RuntimePrerequisiteFailed: EnvironmentInvalid; secondary=ReportSaveFailed'
}

$publicRoot = Join-Path $env:RUNNER_TEMP "PureBase-Release-Validation-$WorkflowRunId-$WorkflowRunAttempt/runtime-prerequisites"
$privateRoot = Join-Path $env:RUNNER_TEMP "PureBase-Chocolatey-Private-$WorkflowRunId-$WorkflowRunAttempt"
$reportPath = Join-Path $publicRoot 'report.json'
$systemDirectory = Join-Path $env:WINDIR 'System32'
$report = [ordered]@{
    schemaVersion = 2; provider = 'Chocolatey'; run = $WorkflowRunId; attempt = $WorkflowRunAttempt
    job = $WorkflowJob; intendedSha = $IntendedSha; checkoutSha = $CheckoutSha; cliVersion = $null
    source = $source; primary = 'NotReached'; secondary = 'None'; utc = $null
    finalFiles = @($false, $false, $false, $false)
    packages = @(
        [ordered]@{ name = 'vcredist2010'; version = '10.0.40219.32503'; files = @('msvcr100.dll', 'msvcp100.dll'); pre = @(); post = @(); status = 'NotReached'; installRequested = $false; exitCode = $null; rebootRequired = $false }
        [ordered]@{ name = 'vcredist2013'; version = '12.0.40660.20180427'; files = @('msvcr120.dll', 'msvcp120.dll'); pre = @(); post = @(); status = 'NotReached'; installRequested = $false; exitCode = $null; rebootRequired = $false }
    )
}

try {
    foreach ($path in @($publicRoot, $privateRoot)) {
        Assert-SafePath -Path $path -Root $env:RUNNER_TEMP
        if (Test-Path -LiteralPath $path) { throw 'Collision' }
    }
    foreach ($path in @($publicRoot, $privateRoot)) {
        New-Item -ItemType Directory -Path $path -ErrorAction Stop | Out-Null
        Assert-SafePath -Path $path -Root $env:RUNNER_TEMP
    }
    Save-Report
}
catch { throw 'RuntimePrerequisiteFailed: EvidenceInitializationFailed; secondary=ReportSaveFailed' }

try {
    $report.primary = 'PlatformUnsupported'
    if ($env:OS -ne 'Windows_NT' -or [IntPtr]::Size -ne 8 -or
        $env:PROCESSOR_ARCHITECTURE -notin @('AMD64', 'ARM64')) { throw 'PlatformUnsupported' }
    $report.primary = 'ChocolateyUnavailable'
    $command = Get-Command choco.exe -CommandType Application -ErrorAction Stop
    $chocoPath = $command.Source
    if (-not (Test-Path -LiteralPath $chocoPath -PathType Leaf)) { throw 'ChocolateyUnavailable' }
    $report.primary = 'ChocolateyVersionFailed'
    $versionProcess = Invoke-Chocolatey -Arguments @('--version') -Label 'version'
    if ([int]$versionProcess.ExitCode -ne 0) { throw 'ChocolateyVersionFailed' }
    $versionPath = Join-Path $privateRoot 'version.stdout'
    if ((Get-Item -LiteralPath $versionPath).Length -gt 128) { throw 'ChocolateyVersionFailed' }
    $version = [IO.File]::ReadAllText($versionPath).Trim()
    if ($version -cnotmatch '^2\.[0-9]+\.[0-9]+(?:\.[0-9]+)?$') { throw 'ChocolateyVersionFailed' }
    $report.cliVersion = $version
    $report.primary = 'None'
    Save-Report
    foreach ($package in $report.packages) { Invoke-RuntimePackage $package }
    $report.finalFiles = @($report.packages | ForEach-Object { Get-PairPresence $_.files })
    $report.primary = 'FinalFilesMissing'
    if ($report.finalFiles -contains $false) { throw 'FinalFilesMissing' }
    $report.primary = 'None'
}
catch {
    if ($report.primary -eq 'None') { $report.primary = 'ReportSaveFailed' }
}
try { Save-Report }
catch { $report.secondary = 'ReportSaveFailed' }
if ($report.primary -ne 'None' -or $report.secondary -ne 'None') {
    throw [InvalidOperationException]::new("RuntimePrerequisiteFailed: $($report.primary); secondary=$($report.secondary)")
}
