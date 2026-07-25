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
