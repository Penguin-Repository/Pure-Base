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

# Exercises normal fail-closed inputs and archive-only atomic identity generation with an independent version.
Describe 'Shader-Core validation input boundaries' {
    BeforeAll {
        . (Join-Path $PSScriptRoot '../Tooling/PureBaseValidationInputs.ps1')
        . (Join-Path $PSScriptRoot '../Tooling/PureBaseReleaseInputs.ps1')
        . (Join-Path $PSScriptRoot '../Tooling/PureBaseValidationFixtures.ps1')
        $sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    }

    BeforeEach {
        $root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        $packageRoot = Join-Path $root 'Packages/jp.penguin.purebase'
        $tree = Join-Path $root 'Packages/jp.lilxyzw.shadercore'
        New-Item -ItemType Directory -Path $tree, (Join-Path $packageRoot 'Tests/Release') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $packageRoot 'package.json'), '{"name":"jp.penguin.purebase","version":"0.2.0","vpmDependencies":{"jp.lilxyzw.shadercore":"7.8.9"}}')
        [IO.File]::WriteAllText((Join-Path $tree 'package.json'), '{"name":"jp.lilxyzw.shadercore","version":"7.8.9"}')
        [IO.File]::WriteAllText((Join-Path $tree 'archive-only.txt'), 'archive bytes')
        Initialize-PureBaseValidationFixture -PackageRoot $packageRoot -Version '7.8.9'
        foreach ($name in @('Build-PureBaseRelease.ps1', 'release-content.json')) {
            Copy-Item (Join-Path $sourceRoot "Tests/Release/$name") (Join-Path $packageRoot "Tests/Release/$name")
        }
        $builder = Join-Path $packageRoot 'Tests/Release/Build-PureBaseRelease.ps1'
        $manifest = Join-Path $packageRoot 'Tests/Release/shader-core.sha256.json'
        $archive = Join-Path $root 'shader-core.zip'
        New-PureBaseFixtureArchive -PackageRoot $packageRoot -ShaderCoreRoot $tree -ArchivePath $archive
        & git -C $packageRoot init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Fixture git init failed.' }
    }

    It 'generates from the supplied archive with <State> identity, not from the unrelated local tree' -ForEach @(
        @{ State = 'missing' }, @{ State = 'old' }
    ) {
        if ($State -eq 'missing') { Remove-Item $manifest }
        else {
            $old = Get-Content $manifest -Raw | ConvertFrom-Json
            $old.packageVersion = '0.1.8'
            $old | ConvertTo-Json -Depth 4 | Set-Content $manifest
        }
        [IO.File]::WriteAllText((Join-Path $tree 'archive-only.txt'), 'unrelated local bytes')
        [IO.File]::WriteAllText((Join-Path $tree 'local-only.txt'), 'not in archive')

        & $builder -WriteShaderCoreManifest -ShaderCoreArchivePath $archive

        $inputs = Get-PureBaseValidationInputs -PackageRoot $packageRoot
        $inputs.ShaderCoreVersion | Should -BeExactly '7.8.9'
        $entries = $inputs.ShaderCoreIdentity.entries
        @($entries).Count | Should -Be 2
        $entries.path | Should -Not -Contain 'local-only.txt'
        $expectedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('archive bytes'))).ToLowerInvariant()
        ($entries | Where-Object path -eq 'archive-only.txt').sha256 | Should -BeExactly $expectedHash
    }

    It 'rejects normal <Mutation> before network or staging and preserves the target' -ForEach @(
        @{ Mutation = 'missing identity' }, @{ Mutation = 'old identity' },
        @{ Mutation = 'lock mismatch' }, @{ Mutation = 'package mismatch' }, @{ Mutation = 'unsafe entry' }
    ) {
        switch ($Mutation) {
            'missing identity' { Remove-Item $manifest }
            'old identity' { (Get-Content $manifest -Raw).Replace('7.8.9', '0.1.8') | Set-Content $manifest }
            'lock mismatch' {
                $path = Join-Path $packageRoot 'Tests/Release/ci-toolchain.json'
                (Get-Content $path -Raw).Replace('7.8.9', '0.1.8') | Set-Content $path
            }
            'package mismatch' {
                $path = Join-Path $packageRoot 'package.json'
                (Get-Content $path -Raw).Replace('7.8.9', '0.1.8') | Set-Content $path
            }
            'unsafe entry' { (Get-Content $manifest -Raw).Replace('fixture.txt', '../escape.txt') | Set-Content $manifest }
        }
        $project = Join-Path $root 'install-project'
        $target = Join-Path $project 'Packages/jp.lilxyzw.shadercore'
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        Set-Content (Join-Path $target 'keep.txt') 'preserve'
        Mock Invoke-WebRequest { throw 'Network must not be reached.' }

        { Get-PureBaseValidationInputs -PackageRoot $packageRoot } | Should -Throw
        { & $builder -AuditOnly } | Should -Throw
        { & (Join-Path $packageRoot '.github/scripts/Install-VerifiedShaderCoreRelease.ps1') -ProjectRoot $project } | Should -Throw

        Should -Invoke Invoke-WebRequest -Times 0 -Exactly
        Get-Content (Join-Path $target 'keep.txt') | Should -BeExactly 'preserve'
        @(Get-ChildItem (Join-Path $project 'Packages') -Force).Count | Should -Be 1
    }

    It 'allows only the named release dependency at the package and lock version' {
        $contract = Get-Content (Join-Path $packageRoot 'Tests/Release/release-content.json') -Raw | ConvertFrom-Json
        { Assert-PackageMetadata -PackageRoot $packageRoot -Contract $contract } | Should -Not -Throw
        $path = Join-Path $packageRoot 'package.json'
        $package = Get-Content $path -Raw | ConvertFrom-Json -AsHashtable
        $package.vpmDependencies['unexpected.dependency'] = '1.0.0'
        $package | ConvertTo-Json -Depth 4 | Set-Content $path

        { Assert-PackageMetadata -PackageRoot $packageRoot -Contract $contract } | Should -Throw '*only the allowed VPM dependency*'
    }

    It 'preserves the old manifest when generation rejects <Failure>' -ForEach @(
        @{ Failure = 'archive SHA' }, @{ Failure = 'unsafe ZIP' }, @{ Failure = 'metadata' }
    ) {
        $before = [IO.File]::ReadAllBytes($manifest)
        if ($Failure -eq 'archive SHA') {
            [IO.File]::AppendAllText($archive, 'tampered')
        }
        else {
            if ($Failure -eq 'metadata') {
                [IO.File]::WriteAllText((Join-Path $tree 'package.json'), '{"name":"jp.lilxyzw.shadercore","version":"0.1.8"}')
            }
            New-PureBaseFixtureArchive -PackageRoot $packageRoot -ShaderCoreRoot $tree -ArchivePath $archive
            if ($Failure -eq 'unsafe ZIP') {
                $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Update)
                try { [void]$zip.CreateEntry('../escape.txt') }
                finally { $zip.Dispose() }
                $path = Join-Path $packageRoot 'Tests/Release/ci-toolchain.json'
                $pin = Get-Content $path -Raw | ConvertFrom-Json
                $pin.shaderCore.archiveSha256 = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
                $pin | ConvertTo-Json -Depth 4 | Set-Content $path
            }
        }

        { & $builder -WriteShaderCoreManifest -ShaderCoreArchivePath $archive } | Should -Throw

        [IO.File]::ReadAllBytes($manifest) | Should -Be $before
        @(Get-ChildItem (Split-Path $manifest) -Filter '*.tmp').Count | Should -Be 0
    }
}