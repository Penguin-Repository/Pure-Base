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

# Tests verified Shader-Core release archive installation using local ZIP fixtures.
$script:installerPath = Join-Path $PSScriptRoot 'Install-VerifiedShaderCoreRelease.ps1'
$script:installerAvailable = Test-Path -LiteralPath $script:installerPath -PathType Leaf

Describe 'Install-VerifiedShaderCoreRelease' {
    BeforeAll {
        function New-ReleaseArchive {
            param(
                [string]$Path,
                [string]$PackageName = 'jp.lilxyzw.shadercore',
                [string]$PackageVersion = '0.1.9',
                [string[]]$AdditionalEntries = @(),
                [hashtable]$AdditionalEntryExternalAttributes = @{}
            )

            Add-Type -AssemblyName System.IO.Compression
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            if (Test-Path -LiteralPath $Path) {
                Remove-Item -LiteralPath $Path -Force
            }
            $archive = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
            try {
                $packageEntry = $archive.CreateEntry('package.json')
                $writer = [IO.StreamWriter]::new($packageEntry.Open(), [Text.UTF8Encoding]::new($false))
                try {
                    $writer.Write("{`"name`":`"$PackageName`",`"version`":`"$PackageVersion`"}")
                }
                finally {
                    $writer.Dispose()
                }

                $shaderEntry = $archive.CreateEntry('Shaders/Core.hlsl')
                $shaderWriter = [IO.StreamWriter]::new($shaderEntry.Open(), [Text.UTF8Encoding]::new($false))
                try {
                    $shaderWriter.Write("float4 ShaderCoreFixture() { return 1; }`r`n")
                }
                finally {
                    $shaderWriter.Dispose()
                }

                foreach ($entryName in $AdditionalEntries) {
                    $entry = $archive.CreateEntry($entryName)
                    $entryWriter = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
                    try {
                        $entryWriter.Write('fixture')
                    }
                    finally {
                        $entryWriter.Dispose()
                    }
                    if ($AdditionalEntryExternalAttributes.ContainsKey($entryName)) {
                        $entry.ExternalAttributes = [int]$AdditionalEntryExternalAttributes[$entryName]
                    }
                }
            }
            finally {
                $archive.Dispose()
            }
        }

        function Get-ArchiveSha256 {
            param([string]$Path)

            return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        }

        function Set-ExistingTarget {
            New-Item -ItemType Directory -Path $script:targetPath -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $script:targetPath 'existing.txt'), 'preserve', [Text.UTF8Encoding]::new($false))
        }

        function Assert-ExistingTargetPreserved {
            (Join-Path $script:targetPath 'existing.txt') | Should -Exist
            Get-Content -LiteralPath (Join-Path $script:targetPath 'existing.txt') -Raw | Should -Be 'preserve'
            (Join-Path $script:targetPath 'package.json') | Should -Not -Exist
        }

        function Assert-RecoverableBackupPreserved {
            $packagesPath = Join-Path $script:projectRoot 'Packages'
            $backups = @(Get-ChildItem -LiteralPath $packagesPath -Directory -Force | Where-Object {
                    $_.Name -like '.jp.lilxyzw.shadercore.backup.*'
                })

            $backups.Count | Should -Be 1
            (Join-Path $backups[0].FullName 'existing.txt') | Should -Exist
            Get-Content -LiteralPath (Join-Path $backups[0].FullName 'existing.txt') -Raw | Should -Be 'preserve'
        }

        function Invoke-Installer {
            param(
                [string]$ArchivePath,
                [string]$ExpectedSha256
            )

            Mock Invoke-WebRequest {
                param($Uri, $OutFile)
                Copy-Item -LiteralPath $env:PUREBASE_TEST_SOURCE_ARCHIVE -Destination $OutFile -Force
            }

            & (Join-Path $PSScriptRoot 'Install-VerifiedShaderCoreRelease.ps1') `
                -ProjectRoot $script:projectRoot `
                -Uri 'https://example.test/jp.lilxyzw.shadercore-0.1.9.zip' `
                -ExpectedSha256 $ExpectedSha256 `
                -TemporaryRoot $script:temporaryRoot
        }
    }

    BeforeEach {
        $script:projectRoot = Join-Path $TestDrive 'PureBaseCi'
        $script:temporaryRoot = Join-Path $TestDrive 'temporary'
        $script:targetPath = Join-Path $script:projectRoot 'Packages/jp.lilxyzw.shadercore'
        $script:sourceArchivePath = Join-Path $TestDrive 'fixture.zip'
        $env:PUREBASE_TEST_SOURCE_ARCHIVE = $script:sourceArchivePath
        if (Test-Path -LiteralPath $script:projectRoot) {
            Remove-Item -LiteralPath $script:projectRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Path $script:projectRoot -Force | Out-Null
    }

    AfterEach {
        Remove-Item Env:PUREBASE_TEST_SOURCE_ARCHIVE -ErrorAction SilentlyContinue
    }

    It 'provides the repository-owned installer script' {
        (Join-Path $PSScriptRoot 'Install-VerifiedShaderCoreRelease.ps1') | Should -Exist
    }

    It 'installs a locally constructed verified archive' -Skip:(-not $script:installerAvailable) {
        New-ReleaseArchive -Path $script:sourceArchivePath

        Invoke-Installer -ArchivePath $script:sourceArchivePath -ExpectedSha256 (Get-ArchiveSha256 -Path $script:sourceArchivePath)

        (Join-Path $script:targetPath 'package.json') | Should -Exist
        (Join-Path $script:targetPath 'Shaders/Core.hlsl') | Should -Exist
        Assert-MockCalled Invoke-WebRequest -Times 1 -Exactly
    }

    It 'rejects a hash mismatch without replacing the target' -Skip:(-not $script:installerAvailable) {
        New-ReleaseArchive -Path $script:sourceArchivePath
        Set-ExistingTarget

        { Invoke-Installer -ArchivePath $script:sourceArchivePath -ExpectedSha256 ('0' * 64) } | Should -Throw

        Assert-ExistingTargetPreserved
    }

    It 'rejects mismatched package metadata without replacing the target' -Skip:(-not $script:installerAvailable) {
        New-ReleaseArchive -Path $script:sourceArchivePath -PackageVersion '9.9.9'
        Set-ExistingTarget

        { Invoke-Installer -ArchivePath $script:sourceArchivePath -ExpectedSha256 (Get-ArchiveSha256 -Path $script:sourceArchivePath) } | Should -Throw

        Assert-ExistingTargetPreserved
    }

    It 'rejects traversal archive entries without replacing the target' -Skip:(-not $script:installerAvailable) {
        New-ReleaseArchive -Path $script:sourceArchivePath -AdditionalEntries '../escape.txt'
        Set-ExistingTarget

        { Invoke-Installer -ArchivePath $script:sourceArchivePath -ExpectedSha256 (Get-ArchiveSha256 -Path $script:sourceArchivePath) } | Should -Throw

        Assert-ExistingTargetPreserved
        (Join-Path $script:projectRoot 'escape.txt') | Should -Not -Exist
    }

    It 'rejects reparse-point archive attributes without replacing the target' -Skip:(-not $script:installerAvailable) {
        New-ReleaseArchive -Path $script:sourceArchivePath `
            -AdditionalEntries 'Links/' `
            -AdditionalEntryExternalAttributes @{ 'Links/' = [int][IO.FileAttributes]::ReparsePoint }
        Set-ExistingTarget

        { Invoke-Installer -ArchivePath $script:sourceArchivePath -ExpectedSha256 (Get-ArchiveSha256 -Path $script:sourceArchivePath) } | Should -Throw '*reparse-point entry*'

        Assert-ExistingTargetPreserved
    }

    It 'restores the existing target when candidate commit fails after backup' -Skip:(-not $script:installerAvailable) {
        New-ReleaseArchive -Path $script:sourceArchivePath
        Set-ExistingTarget
        Mock Move-Item {
            param($LiteralPath, $Destination)

            if ($LiteralPath -like '*.jp.lilxyzw.shadercore.staging.*') {
                throw 'Injected candidate commit failure.'
            }
            [IO.Directory]::Move($LiteralPath, $Destination)
        }

        { Invoke-Installer -ArchivePath $script:sourceArchivePath -ExpectedSha256 (Get-ArchiveSha256 -Path $script:sourceArchivePath) } | Should -Throw '*Injected candidate commit failure*'

        Assert-ExistingTargetPreserved
    }

    It 'retains the recoverable backup when candidate recovery fails' -Skip:(-not $script:installerAvailable) {
        New-ReleaseArchive -Path $script:sourceArchivePath
        Set-ExistingTarget
        Mock Move-Item {
            param($LiteralPath, $Destination)

            if ($LiteralPath -like '*.jp.lilxyzw.shadercore.staging.*' -or $LiteralPath -like '*.jp.lilxyzw.shadercore.backup.*') {
                throw 'Injected recovery failure.'
            }
            [IO.Directory]::Move($LiteralPath, $Destination)
        }

        { Invoke-Installer -ArchivePath $script:sourceArchivePath -ExpectedSha256 (Get-ArchiveSha256 -Path $script:sourceArchivePath) } | Should -Throw '*previous target remains recoverable*'

        $script:targetPath | Should -Not -Exist
        Assert-RecoverableBackupPreserved
    }
}

