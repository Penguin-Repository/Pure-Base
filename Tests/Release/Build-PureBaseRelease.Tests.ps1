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
        Remove-Item -LiteralPath $shaderCoreRoot -Recurse -Force -ErrorAction SilentlyContinue
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

    It 'reports aggregate identities and the first changed entry' {
        Write-ShaderCoreIdentityManifest -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath
        [System.IO.File]::WriteAllText((Join-Path $shaderCoreRoot 'identity-probe.txt'), 'changed identity probe', [System.Text.UTF8Encoding]::new($false))

        $failure = $null
        try { Assert-ShaderCoreIdentity -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath }
        catch { $failure = $_ }

        Assert-ManifestHarness -Condition ($null -ne $failure) -Message 'Changed Shader-Core identity was not rejected.'
        Assert-ManifestHarness -Condition ($failure.Exception.Message -match 'Expected aggregate identity SHA-256: [a-f0-9]{64}\. Actual aggregate identity SHA-256: [a-f0-9]{64}\. Expected entry count: 2\. Actual entry count: 2\.') -Message 'Changed-entry diagnostic omitted aggregate identities or entry counts.'
        Assert-ManifestHarness -Condition ($failure.Exception.Message -match "First divergent entry at ordinal 0: expected path 'identity-probe\.txt' SHA-256 '[a-f0-9]{64}'; actual path 'identity-probe\.txt' SHA-256 '[a-f0-9]{64}'\.") -Message 'Changed-entry diagnostic omitted the first path and hash divergence.'
    }

    It 'reports an actual-only first entry when a file is added' {
        Write-ShaderCoreIdentityManifest -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath
        [System.IO.File]::WriteAllText((Join-Path $shaderCoreRoot 'added-entry.txt'), 'added entry', [System.Text.UTF8Encoding]::new($false))

        $failure = $null
        try { Assert-ShaderCoreIdentity -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath }
        catch { $failure = $_ }

        Assert-ManifestHarness -Condition ($null -ne $failure) -Message 'Added Shader-Core identity entry was not rejected.'
        Assert-ManifestHarness -Condition ($failure.Exception.Message -match 'Expected entry count: 2\. Actual entry count: 3\.') -Message 'Added-entry diagnostic omitted the differing entry counts.'
        Assert-ManifestHarness -Condition ($failure.Exception.Message -match "First divergent entry at expected ordinal 0 \(actual ordinal 0\): actual-only path 'added-entry\.txt' SHA-256 '[a-f0-9]{64}'\.") -Message 'Added-entry diagnostic omitted the first actual-only entry.'
    }

    It 'reports an expected-only first entry when a file is removed' {
        Write-ShaderCoreIdentityManifest -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath
        Remove-Item -LiteralPath (Join-Path $shaderCoreRoot 'identity-probe.txt') -Force

        $failure = $null
        try { Assert-ShaderCoreIdentity -ShaderCoreRoot $shaderCoreRoot -ManifestPath $manifestPath }
        catch { $failure = $_ }

        Assert-ManifestHarness -Condition ($null -ne $failure) -Message 'Removed Shader-Core identity entry was not rejected.'
        Assert-ManifestHarness -Condition ($failure.Exception.Message -match "First divergent entry at expected ordinal 0 \(actual ordinal 0\): expected-only path 'identity-probe\.txt' SHA-256 '[a-f0-9]{64}'\.") -Message 'Removed-entry diagnostic omitted the first expected-only entry.'
    }
}

Describe 'Release archive version and policy contracts' {
    It 'derives stable and prerelease ZIP names from package.json.version' {
        $builderSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Build-PureBaseRelease.ps1') -Raw

        $builderSource | Should -Match '\$packageJson\.version'
        $builderSource | Should -Not -Match 'jp\.penguin\.purebase-0\.1\.0\.zip'
    }

    It 'explicitly excludes vpm-yanks.json from dynamic release ZIP inputs' {
        $contractPath = Join-Path $PSScriptRoot 'release-content.json'
        $contract = Get-Content -LiteralPath $contractPath -Raw

        $contract | Should -Match '"vpm-yanks\.json"'
    }
}

Describe 'Deterministic release archive contracts' {
    BeforeAll {
        $builderPath = Join-Path $PSScriptRoot 'Build-PureBaseRelease.ps1'

        function New-FixedReleaseArchiveFixture {
            param([Parameter(Mandatory = $true)][string]$Root)

            $packageRoot = Join-Path $Root 'Packages/jp.penguin.purebase'
            $shaderCoreRoot = Join-Path $Root 'Packages/jp.lilxyzw.shadercore'
            $scriptRoot = Join-Path $packageRoot 'Tests/Release'
            New-Item -ItemType Directory -Path (Join-Path $packageRoot 'Editor'), (Join-Path $packageRoot 'Shaders'), $scriptRoot, $shaderCoreRoot -Force | Out-Null

            $utf8NoBom = [Text.UTF8Encoding]::new($false)
            $files = [ordered]@{
                'LICENSE' = "license fixture`n"
                'NOTICE' = "notice fixture`n"
                'README.md' = "# Fixture`n"
                'Editor/.gitkeep' = ''
                'Shaders/PureBaseHybrid.scshader' = "Shader fixture Hybrid`n"
                'Shaders/PureBasePBR.scshader' = "Shader fixture PBR`n"
                'Shaders/PureBaseToon.scshader' = "Shader fixture Toon`n"
                'Shaders/PureBaseUnlit.scshader' = "Shader fixture Unlit`n"
                'package.json' = "{`"name`":`"jp.penguin.purebase`",`"version`":`"0.2.0`",`"vpmDependencies`":{`"jp.lilxyzw.shadercore`":`"0.1.9`"}}`n"
            }
            foreach ($entry in $files.GetEnumerator()) {
                $path = Join-Path $packageRoot $entry.Key
                New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
                [IO.File]::WriteAllText($path, $entry.Value, $utf8NoBom)
            }

            Copy-Item -LiteralPath $builderPath -Destination (Join-Path $scriptRoot 'Build-PureBaseRelease.ps1') -Force
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release-content.json') -Destination (Join-Path $scriptRoot 'release-content.json') -Force
            [IO.File]::WriteAllText((Join-Path $shaderCoreRoot 'package.json'), '{"name":"jp.lilxyzw.shadercore","version":"0.1.9"}' + "`n", $utf8NoBom)
            [IO.File]::WriteAllText((Join-Path $shaderCoreRoot 'identity-probe.txt'), "fixture identity`n", $utf8NoBom)

            & git -C $packageRoot init --initial-branch master --quiet
            if ($LASTEXITCODE -ne 0) { throw 'git init failed for fixed release archive fixture.' }
            & git -C $packageRoot config user.name 'PureBase Test'
            & git -C $packageRoot config user.email 'purebase-test@example.invalid'
            & git -C $packageRoot add -- .
            & git -C $packageRoot commit --quiet -m fixture
            if ($LASTEXITCODE -ne 0) { throw 'git commit failed for fixed release archive fixture.' }

            $fixtureBuilderPath = Join-Path $scriptRoot 'Build-PureBaseRelease.ps1'
            & pwsh -NoProfile -File $fixtureBuilderPath -WriteShaderCoreManifest
            if ($LASTEXITCODE -ne 0) { throw 'shader-core fixture manifest generation failed.' }
            return [pscustomobject]@{ PackageRoot = $packageRoot; BuilderPath = $fixtureBuilderPath }
        }
    }

    It 'uses an explicit Store-mode ZIP writer with stable entry order, timestamp, and attributes' {
        $builderSource = Get-Content -LiteralPath $builderPath -Raw

        ($builderSource -match 'ZipArchiveMode\]::Create') | Should -BeTrue
        ($builderSource -match 'CompressionLevel\]::NoCompression') | Should -BeTrue
        ($builderSource -match 'LastWriteTime') | Should -BeTrue
        ($builderSource -match 'ExternalAttributes') | Should -BeTrue
        ($builderSource -notmatch 'CreateFromDirectory') | Should -BeTrue
    }

    It 'produces byte-identical ZIPs with fixed entry metadata from separate PowerShell processes' {
        $fixture = New-FixedReleaseArchiveFixture -Root (Join-Path $TestDrive 'fixed-fixture')
        $firstOutput = Join-Path $TestDrive 'first'
        $secondOutput = Join-Path $TestDrive 'second'
        New-Item -ItemType Directory -Path $firstOutput, $secondOutput -Force | Out-Null

        & pwsh -NoProfile -File $fixture.BuilderPath -OutputDirectory $firstOutput
        $firstExitCode = $LASTEXITCODE
        & pwsh -NoProfile -File $fixture.BuilderPath -OutputDirectory $secondOutput
        $secondExitCode = $LASTEXITCODE
        $firstExitCode | Should -Be 0
        $secondExitCode | Should -Be 0

        $firstZip = Get-ChildItem -LiteralPath $firstOutput -Filter 'jp.penguin.purebase-*.zip' -File | Select-Object -First 1
        $secondZip = Get-ChildItem -LiteralPath $secondOutput -Filter 'jp.penguin.purebase-*.zip' -File | Select-Object -First 1
        (Get-FileHash -LiteralPath $firstZip.FullName -Algorithm SHA256).Hash | Should -Be (Get-FileHash -LiteralPath $secondZip.FullName -Algorithm SHA256).Hash

        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $firstArchive = [IO.Compression.ZipFile]::OpenRead($firstZip.FullName)
        $secondArchive = [IO.Compression.ZipFile]::OpenRead($secondZip.FullName)
        try {
            $firstEntries = @($firstArchive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
            $secondEntries = @($secondArchive.Entries | Where-Object { -not $_.FullName.EndsWith('/') })
            $firstEntries.FullName | Should -Be $secondEntries.FullName
            foreach ($entry in $firstEntries) {
                $entry.CompressedLength | Should -Be $entry.Length
                $entry.LastWriteTime.UtcDateTime | Should -Be ([datetime]'2026-01-01T00:00:00Z')
                $entry.ExternalAttributes | Should -Not -Be 0
            }
        }
        finally {
            $firstArchive.Dispose()
            $secondArchive.Dispose()
        }
    }

    It 'matches the fixed UTF-8 fixture SHA-256 baseline without using the repository package root' {
        $fixture = New-FixedReleaseArchiveFixture -Root (Join-Path $TestDrive 'baseline-fixture')
        $outputDirectory = Join-Path $TestDrive 'baseline-output'
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

        & pwsh -NoProfile -File $fixture.BuilderPath -OutputDirectory $outputDirectory
        $LASTEXITCODE | Should -Be 0

        $archive = Get-ChildItem -LiteralPath $outputDirectory -Filter 'jp.penguin.purebase-0.2.0.zip' -File | Select-Object -First 1
        $archive | Should -Not -BeNullOrEmpty
        (Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant() |
        Should -Be '7f5d3c541f7e3d39a39ac6f7fb65b70c96081dea5e08a9cdc3ef805487aac232'
    }
}