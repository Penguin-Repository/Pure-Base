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

# Verifies release manifest generation accepts only the pinned local Shader-Core package identity.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Describe 'Shader-Core identity manifest generation' {
    BeforeAll {
        $builderPath = Join-Path $PSScriptRoot 'Build-PureBaseRelease.ps1'
        $builderSource = Get-Content -LiteralPath $builderPath -Raw
        $libraryStartIndex = $builderSource.IndexOf('Set-StrictMode -Version Latest')
        $entryPointIndex = $builderSource.IndexOf("`n" + '$scriptRoot = Split-Path -Parent $PSCommandPath')
        if ($libraryStartIndex -lt 0 -or $entryPointIndex -lt 0) {
            throw 'The release builder library could not be isolated for the manifest harness.'
        }

        $libraryPath = Join-Path ([System.IO.Path]::GetTempPath()) ('PureBaseReleaseBuilder-' + [guid]::NewGuid().ToString('N') + '.ps1')
        [System.IO.File]::WriteAllText($libraryPath, $builderSource.Substring($libraryStartIndex, $entryPointIndex - $libraryStartIndex), [System.Text.UTF8Encoding]::new($false))
        . $libraryPath

        function Assert-ManifestHarness {
            param(
                [Parameter(Mandatory = $true)][bool]$Condition,
                [Parameter(Mandatory = $true)][string]$Message
            )

            if (-not $Condition) {
                throw $Message
            }
        }
    }

    BeforeEach {
        $shaderCoreRoot = Join-Path $TestDrive 'jp.lilxyzw.shadercore'
        New-Item -ItemType Directory -Path $shaderCoreRoot -Force | Out-Null
        [System.IO.File]::WriteAllText((Join-Path $shaderCoreRoot 'package.json'), '{"name":"jp.lilxyzw.shadercore","version":"0.1.9"}', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText((Join-Path $shaderCoreRoot 'identity-probe.txt'), 'identity probe', [System.Text.UTF8Encoding]::new($false))
        $manifestPath = Join-Path $TestDrive 'shader-core-0.1.9.sha256.json'
    }

    It 'writes a manifest with the verified 0.1.9 package metadata' {
        Write-ShaderCoreIdentityManifest -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath

        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        Assert-ManifestHarness -Condition ([string]$manifest.packageName -eq 'jp.lilxyzw.shadercore') -Message 'Generated manifest packageName is not the verified Shader-Core package ID.'
        Assert-ManifestHarness -Condition ([string]$manifest.packageVersion -eq '0.1.9') -Message 'Generated manifest packageVersion is not the verified Shader-Core version.'
        Assert-ManifestHarness -Condition ([string]$manifest.identitySha256 -match '^[a-f0-9]{64}$') -Message 'Generated manifest identitySha256 is not a lowercase SHA-256 hash.'
    }

    It 'does not overwrite a manifest when the local package ID is mismatched' {
        [System.IO.File]::WriteAllText((Join-Path $shaderCoreRoot 'package.json'), '{"name":"unexpected.shadercore","version":"0.1.9"}', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($manifestPath, 'preserve-id-mismatch', [System.Text.UTF8Encoding]::new($false))

        $failure = $null
        try { Write-ShaderCoreIdentityManifest -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath }
        catch { $failure = $_ }
        Assert-ManifestHarness -Condition ($null -ne $failure -and $failure.Exception.Message -like '*jp.lilxyzw.shadercore version 0.1.9*') -Message 'Mismatched Shader-Core package ID was not rejected.'
        Assert-ManifestHarness -Condition ((Get-Content -LiteralPath $manifestPath -Raw) -eq 'preserve-id-mismatch') -Message 'Mismatched Shader-Core package ID overwrote the manifest.'
    }

    It 'does not overwrite a manifest when the local package version is mismatched' {
        [System.IO.File]::WriteAllText((Join-Path $shaderCoreRoot 'package.json'), '{"name":"jp.lilxyzw.shadercore","version":"0.1.8"}', [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($manifestPath, 'preserve-version-mismatch', [System.Text.UTF8Encoding]::new($false))

        $failure = $null
        try { Write-ShaderCoreIdentityManifest -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath }
        catch { $failure = $_ }
        Assert-ManifestHarness -Condition ($null -ne $failure -and $failure.Exception.Message -like '*jp.lilxyzw.shadercore version 0.1.9*') -Message 'Mismatched Shader-Core package version was not rejected.'
        Assert-ManifestHarness -Condition ((Get-Content -LiteralPath $manifestPath -Raw) -eq 'preserve-version-mismatch') -Message 'Mismatched Shader-Core package version overwrote the manifest.'
    }
}