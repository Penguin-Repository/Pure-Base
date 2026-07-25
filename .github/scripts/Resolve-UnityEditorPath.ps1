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

function Assert-VisualCpp2013Runtime {
    $runtimeDirectory = Join-Path $env:WINDIR 'System32'
    $requiredRuntimeFiles = @(
        (Join-Path $runtimeDirectory 'msvcr120.dll'),
        (Join-Path $runtimeDirectory 'msvcp120.dll')
    )

    $missingRuntimeFiles = @($requiredRuntimeFiles | Where-Object {
        -not (Test-Path -LiteralPath $_ -PathType Leaf)
    })
    if ($missingRuntimeFiles.Count -eq 0) {
        Write-Host 'Microsoft Visual C++ 2013 x64 runtime is already available.'
        return
    }

    $installerPath = Join-Path $env:RUNNER_TEMP 'vcredist-2013-x64.exe'
    $installerUri = 'https://download.microsoft.com/download/0/5/6/056DCDA9-D667-4E27-8001-8A0C6971D6B1/vcredist_x64.exe'
    Write-Host "Installing Microsoft Visual C++ 2013 x64 runtime because these files are missing: $($missingRuntimeFiles -join ', ')"
    Invoke-WebRequest -UseBasicParsing -Uri $installerUri -OutFile $installerPath

    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signature.SignerCertificate.Subject -notlike '*Microsoft Corporation*') {
        throw "Downloaded Visual C++ 2013 installer did not have a valid Microsoft signature. Status: $($signature.Status)."
    }

    $installerProcess = Start-Process `
        -FilePath $installerPath `
        -ArgumentList @('/install', '/quiet', '/norestart') `
        -Wait `
        -PassThru `
        -NoNewWindow
    if ($installerProcess.ExitCode -notin @(0, 1638, 3010)) {
        throw "Visual C++ 2013 x64 runtime installer failed with exit code $($installerProcess.ExitCode)."
    }

    $missingAfterInstall = @($requiredRuntimeFiles | Where-Object {
        -not (Test-Path -LiteralPath $_ -PathType Leaf)
    })
    if ($missingAfterInstall.Count -gt 0) {
        throw "Visual C++ 2013 x64 runtime installation completed but required files are still missing: $($missingAfterInstall -join ', ')."
    }

    Write-Host "Microsoft Visual C++ 2013 x64 runtime installed successfully (exit code $($installerProcess.ExitCode))."
}

Assert-VisualCpp2013Runtime

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

Write-Output $normalizedPath
