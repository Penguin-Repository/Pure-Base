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

# Downloads and atomically installs the pinned Unity CLI for Windows X64 cache lookup.
function Install-PinnedUnityCli {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Version,

        [Parameter(Mandatory)]
        [string]$ExpectedSha256,

        [string]$RunnerOS = $env:RUNNER_OS,

        [string]$RunnerArchitecture = $env:RUNNER_ARCH,

        [string]$TemporaryRoot = $env:RUNNER_TEMP,

        [string]$LocalApplicationDataRoot = $env:LOCALAPPDATA
    )

    if ($RunnerOS -ne 'Windows') {
        throw "Install-PinnedUnityCli supports only Windows runners; received '$RunnerOS'."
    }

    if ($RunnerArchitecture -ne 'X64') {
        throw "Install-PinnedUnityCli supports only X64 runners; received '$RunnerArchitecture'."
    }

    if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:\.[0-9A-Za-z]+)*)?$') {
        throw "Unity CLI version '$Version' is invalid."
    }

    if ($ExpectedSha256 -notmatch '^[0-9a-f]{64}$') {
        throw 'The Unity CLI SHA-256 must be 64 lowercase hexadecimal characters.'
    }

    if ([string]::IsNullOrWhiteSpace($TemporaryRoot)) {
        throw 'The runner temporary directory is required.'
    }

    if ([string]::IsNullOrWhiteSpace($LocalApplicationDataRoot)) {
        throw 'The local application-data directory is required.'
    }

    $temporaryPath = Join-Path $TemporaryRoot ("unity-windows-x64-{0}.exe" -f [guid]::NewGuid().ToString('N'))
    $downloadUri = "https://public-cdn.cloud.unity3d.com/hub/prod/cli/$Version/unity-windows-x64.exe"

    try {
        Invoke-WebRequest -Uri $downloadUri -OutFile $temporaryPath -ErrorAction Stop
        $actualSha256 = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256 -ErrorAction Stop).Hash.ToLowerInvariant()
        if ($actualSha256 -ne $ExpectedSha256) {
            throw "Unity CLI SHA-256 mismatch. Expected '$ExpectedSha256', received '$actualSha256'."
        }

        $unityDirectory = Join-Path $LocalApplicationDataRoot 'Unity'
        $destinationDirectory = Join-Path $unityDirectory 'bin'
        $destinationPath = Join-Path $destinationDirectory 'unity.exe'
        New-Item -ItemType Directory -Path $destinationDirectory -Force -ErrorAction Stop | Out-Null
        Move-Item -LiteralPath $temporaryPath -Destination $destinationPath -Force -ErrorAction Stop
    }
    finally {
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
    }
}
