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

# Verifies validated-package export layout, provenance, and byte encoding contracts.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Describe 'Validated package exporter contracts' {
    BeforeAll {
        $exporterPath = Join-Path $PSScriptRoot '../scripts/Export-PureBaseValidationZip.ps1'
        $utf8NoBom = [Text.UTF8Encoding]::new($false)
    }

    BeforeEach {
        $packageRoot = Join-Path $TestDrive 'package'
        $validationRoot = Join-Path $TestDrive 'validation'
        $archiveRoot = Join-Path $validationRoot 'archive'
        Remove-Item -LiteralPath $validationRoot -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $packageRoot, $archiveRoot -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $packageRoot 'package.json'), '{"name":"jp.penguin.purebase","version":"0.2.0-beta.1"}', $utf8NoBom)

        $sourceZip = Join-Path $archiveRoot 'jp.penguin.purebase-0.2.0-beta.1.zip'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::Open($sourceZip, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $archive.CreateEntry('package.json')
            $writer = [IO.StreamWriter]::new($entry.Open(), $utf8NoBom)
            try { $writer.Write('{"name":"jp.penguin.purebase","version":"0.2.0-beta.1"}') }
            finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }

        $arguments = @{
            PackageRoot = $packageRoot; ValidationArtifactDirectory = $validationRoot; Repository = 'Penguin-Repository/Pure-Base'
            HeadSha = ('a' * 40); HeadBranch = 'master'; WorkflowRunId = 101; WorkflowRunAttempt = 2
        }
    }

    It 'creates a clean validated-package layout with one ZIP, lowercase sidecar, and schema 1 manifest' {
        & $exporterPath @arguments

        $exportRoot = Join-Path $validationRoot 'validated-package'
        $files = @(Get-ChildItem -LiteralPath $exportRoot -File | Sort-Object Name)
        $files.Name | Should -Be @('jp.penguin.purebase-0.2.0-beta.1.zip', 'jp.penguin.purebase-0.2.0-beta.1.zip.sha256', 'release-validation.json')
        $zipPath = Join-Path $exportRoot 'jp.penguin.purebase-0.2.0-beta.1.zip'
        $actualSha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        (Get-Content -LiteralPath ($zipPath + '.sha256') -Raw) | Should -Be ($actualSha256 + "`n")

        $manifest = Get-Content -LiteralPath (Join-Path $exportRoot 'release-validation.json') -Raw | ConvertFrom-Json
        $manifest.schemaVersion | Should -Be 1
        $manifest.repository | Should -Be 'Penguin-Repository/Pure-Base'
        $manifest.headSha | Should -Be ('a' * 40)
        $manifest.headBranch | Should -Be 'master'
        $manifest.workflowRunId | Should -Be 101
        $manifest.workflowRunAttempt | Should -Be 2
        $manifest.version | Should -Be '0.2.0-beta.1'
        $manifest.assetName | Should -Be 'jp.penguin.purebase-0.2.0-beta.1.zip'
        $manifest.sha256 | Should -Be $actualSha256
    }

    It 'writes the sidecar and manifest as UTF-8 without BOM with one terminal LF' {
        & $exporterPath @arguments

        foreach ($name in @('jp.penguin.purebase-0.2.0-beta.1.zip.sha256', 'release-validation.json')) {
            $bytes = [IO.File]::ReadAllBytes((Join-Path $validationRoot "validated-package/$name"))
            $bytes[0..2] | Should -Not -Be ([byte[]](0xEF, 0xBB, 0xBF))
            $bytes[-1] | Should -Be 0x0A
            $bytes[-2] | Should -Not -Be 0x0A
        }
    }

    It 'removes stale validated-package output before writing the verified artifact' {
        $staleDirectory = Join-Path $validationRoot 'validated-package'
        New-Item -ItemType Directory -Path $staleDirectory -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $staleDirectory 'stale.txt'), 'stale', $utf8NoBom)

        & $exporterPath @arguments

        Test-Path -LiteralPath (Join-Path $staleDirectory 'stale.txt') | Should -BeFalse
    }

    It 'rejects an invalid package version before creating export output' {
        [IO.File]::WriteAllText((Join-Path $packageRoot 'package.json'), '{"name":"jp.penguin.purebase","version":"v0.2.0-beta.1"}', $utf8NoBom)

        { & $exporterPath @arguments } | Should -Throw '*package.json version must be valid*'
        Test-Path -LiteralPath (Join-Path $validationRoot 'validated-package') | Should -BeFalse
    }

    It 'rejects a missing package version before creating export output' {
        [IO.File]::WriteAllText((Join-Path $packageRoot 'package.json'), '{"name":"jp.penguin.purebase"}', $utf8NoBom)

        { & $exporterPath @arguments } | Should -Throw '*package.json version must be valid*'
        Test-Path -LiteralPath (Join-Path $validationRoot 'validated-package') | Should -BeFalse
    }

    It 'rejects invalid provenance before creating export output' -ForEach @(
        @{ Name = 'repository'; Override = @{ Repository = 'owner-only' } },
        @{ Name = 'head SHA'; Override = @{ HeadSha = 'not-a-sha' } },
        @{ Name = 'branch'; Override = @{ HeadBranch = '' } },
        @{ Name = 'run ID'; Override = @{ WorkflowRunId = 0 } },
        @{ Name = 'run attempt'; Override = @{ WorkflowRunAttempt = 0 } }
    ) {
        $invalidArguments = @{} + $arguments
        foreach ($entry in $Override.GetEnumerator()) { $invalidArguments[$entry.Key] = $entry.Value }

        { & $exporterPath @invalidArguments } | Should -Throw "*$Name must be valid*"
        Test-Path -LiteralPath (Join-Path $validationRoot 'validated-package') | Should -BeFalse
    }
}
