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
    [string]$EditorPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-MicrosoftRuntime {
    param(
        [Parameter(Mandatory = $true)]
        [string]$DisplayName,
        [Parameter(Mandatory = $true)]
        [string[]]$RequiredFiles,
        [Parameter(Mandatory = $true)]
        [string]$InstallerFileName,
        [Parameter(Mandatory = $true)]
        [string]$InstallerUri,
        [Parameter(Mandatory = $true)]
        [string[]]$InstallerArguments
    )

    $missingRuntimeFiles = @($RequiredFiles | Where-Object {
        -not (Test-Path -LiteralPath $_ -PathType Leaf)
    })
    if ($missingRuntimeFiles.Count -eq 0) {
        Write-Host "$DisplayName is already available."
        return
    }

    $installerPath = Join-Path $env:RUNNER_TEMP $InstallerFileName
    Write-Host "Installing $DisplayName because these files are missing: $($missingRuntimeFiles -join ', ')"
    Invoke-WebRequest -UseBasicParsing -Uri $InstallerUri -OutFile $installerPath

    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notlike '*Microsoft Corporation*') {
        throw "Downloaded $DisplayName installer did not have a valid Microsoft signature. Status: $($signature.Status)."
    }

    $installerProcess = Start-Process `
        -FilePath $installerPath `
        -ArgumentList $InstallerArguments `
        -Wait `
        -PassThru `
        -NoNewWindow
    if ($installerProcess.ExitCode -notin @(0, 1638, 3010)) {
        throw "$DisplayName installer failed with exit code $($installerProcess.ExitCode)."
    }

    $missingAfterInstall = @($RequiredFiles | Where-Object {
        -not (Test-Path -LiteralPath $_ -PathType Leaf)
    })
    if ($missingAfterInstall.Count -gt 0) {
        throw "$DisplayName installation completed but required files are still missing: $($missingAfterInstall -join ', ')."
    }

    Write-Host "$DisplayName installed successfully (exit code $($installerProcess.ExitCode))."
}

$runtimeDirectory = Join-Path $env:WINDIR 'System32'
Assert-MicrosoftRuntime `
    -DisplayName 'Microsoft Visual C++ 2010 SP1 x64 runtime' `
    -RequiredFiles @(
        (Join-Path $runtimeDirectory 'msvcr100.dll'),
        (Join-Path $runtimeDirectory 'msvcp100.dll')
    ) `
    -InstallerFileName 'vcredist-2010-sp1-x64.exe' `
    -InstallerUri 'https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x64.exe' `
    -InstallerArguments @('/quiet', '/norestart')

Assert-MicrosoftRuntime `
    -DisplayName 'Microsoft Visual C++ 2013 x64 runtime' `
    -RequiredFiles @(
        (Join-Path $runtimeDirectory 'msvcr120.dll'),
        (Join-Path $runtimeDirectory 'msvcp120.dll')
    ) `
    -InstallerFileName 'vcredist-2013-x64.exe' `
    -InstallerUri 'https://download.microsoft.com/download/0/5/6/056DCDA9-D667-4E27-8001-8A0C6971D6B1/vcredist_x64.exe' `
    -InstallerArguments @('/install', '/quiet', '/norestart')

$normalizedPath = $EditorPath.Trim()
if ([string]::IsNullOrWhiteSpace($normalizedPath)) {
    throw 'Unity Editor path output was empty.'
}

if ($normalizedPath -match '^/([A-Za-z])(?:/(.*))?$') {
    $drive = $Matches[1].ToUpperInvariant()
    $tail = if ($Matches.Count -gt 2 -and $null -ne $Matches[2]) { $Matches[2] } else { '' }
    $normalizedPath = '{0}:\{1}' -f $drive, $tail.Replace('/', '\')
}

$normalizedPath = [System.IO.Path]::GetFullPath($normalizedPath)
if (-not (Test-Path -LiteralPath $normalizedPath -PathType Leaf)) {
    throw "Unity Editor executable was not found at '$normalizedPath' (raw output: '$EditorPath')."
}

if ([System.IO.Path]::GetFileName($normalizedPath) -ine 'Unity.exe') {
    throw "Resolved Unity Editor path does not point to Unity.exe: '$normalizedPath'."
}

$proxyScriptPath = Join-Path $PSScriptRoot 'UnityWatchdogProxy.ps1'
if (-not (Test-Path -LiteralPath $proxyScriptPath -PathType Leaf)) {
    throw "Unity watchdog proxy script was not found at '$proxyScriptPath'."
}

$pwshPath = (Get-Command pwsh -ErrorAction Stop).Source
$proxyRoot = Join-Path $env:RUNNER_TEMP 'PureBaseUnityProxy/2022.3.22f1/Editor'
New-Item -ItemType Directory -Path $proxyRoot -Force | Out-Null
$proxyCommandPath = Join-Path $proxyRoot 'Unity.cmd'
$proxyCommand = @"
@echo off
"$pwshPath" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$proxyScriptPath" -UnityEditorPath "$normalizedPath" %*
exit /b %ERRORLEVEL%
"@
[System.IO.File]::WriteAllText(
    $proxyCommandPath,
    $proxyCommand.Replace("`n", "`r`n"),
    [System.Text.ASCIIEncoding]::new()
)

Write-Host "Unity watchdog proxy: $proxyCommandPath"
Write-Host "Unity watchdog target: $normalizedPath"
Write-Output $proxyCommandPath
