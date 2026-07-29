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

# Tests the isolated fail-closed Unity CLI bootstrap contract.
$script:repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$script:bootstrapScriptPath = Join-Path $script:repositoryRoot '.github/actions/lookup-unity-editor-cache/Install-PinnedUnityCli.ps1'
$script:bootstrapAvailable = Test-Path -LiteralPath $script:bootstrapScriptPath -PathType Leaf
$script:expectedSha256 = 'ff9ef81ade1063041d25e2c549cc7ed14e96d446f4204400bf101b389f7b8502'

Describe 'Install-PinnedUnityCli' {
    BeforeAll {
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $bootstrapScriptPath = Join-Path $repositoryRoot '.github/actions/lookup-unity-editor-cache/Install-PinnedUnityCli.ps1'
        if (Test-Path -LiteralPath $bootstrapScriptPath -PathType Leaf) {
            . $bootstrapScriptPath
        }

        function Set-DestinationBytes {
            param([byte[]]$Bytes)

            New-Item -ItemType Directory -Path (Split-Path -Parent $script:destinationPath) -Force | Out-Null
            [IO.File]::WriteAllBytes($script:destinationPath, $Bytes)
        }

        function Assert-FailurePreservesDestination {
            param([byte[]]$ExpectedBytes)

            [Convert]::ToHexString([IO.File]::ReadAllBytes($script:destinationPath)) |
                Should -Be ([Convert]::ToHexString($ExpectedBytes))
            @(Get-ChildItem -LiteralPath $script:temporaryRoot -Recurse -File -ErrorAction SilentlyContinue).Count | Should -Be 0
        }

        function Invoke-Bootstrap {
            param(
                [string]$Version = '1.0.0-beta.3',
                [string]$Hash = 'ff9ef81ade1063041d25e2c549cc7ed14e96d446f4204400bf101b389f7b8502',
                [string]$RunnerOS = 'Windows',
                [string]$RunnerArchitecture = 'X64'
            )

            Install-PinnedUnityCli `
                -Version $Version `
                -ExpectedSha256 $Hash `
                -RunnerOS $RunnerOS `
                -RunnerArchitecture $RunnerArchitecture `
                -TemporaryRoot $script:temporaryRoot `
                -LocalApplicationDataRoot $script:localApplicationDataRoot
        }
    }

    BeforeEach {
        $script:temporaryRoot = Join-Path $TestDrive 'temporary'
        $script:localApplicationDataRoot = Join-Path $TestDrive 'local-app-data'
        $script:destinationPath = Join-Path $script:localApplicationDataRoot 'Unity/bin/unity.exe'
        $script:downloadBytes = [byte[]](1, 2, 3, 4)
        $script:actualSha256 = $expectedSha256
        New-Item -ItemType Directory -Path $script:temporaryRoot -Force | Out-Null
    }

    It 'provides the repository-owned bootstrap script' {
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        (Join-Path $repositoryRoot '.github/actions/lookup-unity-editor-cache/Install-PinnedUnityCli.ps1') | Should -Exist
    }

    It 'installs only verified bytes after download and hashing' -Skip:(-not $script:bootstrapAvailable) {
        $operations = [Collections.Generic.List[string]]::new()
        Mock Invoke-WebRequest {
            param($Uri, $OutFile)
            $operations.Add('download')
            [IO.File]::WriteAllBytes($OutFile, $script:downloadBytes)
        }
        Mock Get-FileHash {
            $operations.Add('hash')
            [pscustomobject]@{ Hash = $script:actualSha256 }
        }

        Invoke-Bootstrap

        [Convert]::ToHexString([IO.File]::ReadAllBytes($script:destinationPath)) |
            Should -Be ([Convert]::ToHexString($script:downloadBytes))
        ($operations -join ',') | Should -Be 'download,hash'
        @(Get-ChildItem -LiteralPath $script:temporaryRoot -Recurse -File -ErrorAction SilentlyContinue).Count | Should -Be 0
    }

    It 'rejects Linux before any network operation' -Skip:(-not $script:bootstrapAvailable) {
        Set-DestinationBytes -Bytes ([byte[]](9, 9))
        Mock Invoke-WebRequest {}

        { Invoke-Bootstrap -RunnerOS 'Linux' } | Should -Throw

        Assert-MockCalled Invoke-WebRequest -Times 0 -Exactly
        Assert-FailurePreservesDestination -ExpectedBytes ([byte[]](9, 9))
    }

    It 'rejects Windows ARM64 before any network operation' -Skip:(-not $script:bootstrapAvailable) {
        Set-DestinationBytes -Bytes ([byte[]](9, 9))
        Mock Invoke-WebRequest {}

        { Invoke-Bootstrap -RunnerArchitecture 'ARM64' } | Should -Throw

        Assert-MockCalled Invoke-WebRequest -Times 0 -Exactly
        Assert-FailurePreservesDestination -ExpectedBytes ([byte[]](9, 9))
    }

    It 'rejects empty and malicious versions before any network operation' -ForEach @('', '1.0.0; Remove-Item C:\\') -Skip:(-not $script:bootstrapAvailable) {
        param($Version)
        Set-DestinationBytes -Bytes ([byte[]](9, 9))
        Mock Invoke-WebRequest {}

        { Invoke-Bootstrap -Version $Version } | Should -Throw

        Assert-MockCalled Invoke-WebRequest -Times 0 -Exactly
        Assert-FailurePreservesDestination -ExpectedBytes ([byte[]](9, 9))
    }

    It 'rejects uppercase and boundary-invalid hashes before any network operation' -ForEach @(
        ('A' * 64),
        ('a' * 63),
        ('a' * 65),
        (('a' * 63) + 'g')
    ) -Skip:(-not $script:bootstrapAvailable) {
        param($Hash)
        Set-DestinationBytes -Bytes ([byte[]](9, 9))
        Mock Invoke-WebRequest {}

        { Invoke-Bootstrap -Hash $Hash } | Should -Throw

        Assert-MockCalled Invoke-WebRequest -Times 0 -Exactly
        Assert-FailurePreservesDestination -ExpectedBytes ([byte[]](9, 9))
    }

    It 'cleans failed downloads without replacing the destination' -Skip:(-not $script:bootstrapAvailable) {
        Set-DestinationBytes -Bytes ([byte[]](9, 9))
        Mock Invoke-WebRequest {
            param($Uri, $OutFile)
            [IO.File]::WriteAllBytes($OutFile, $script:downloadBytes)
            throw 'download failure'
        }
        Mock Move-Item {}

        { Invoke-Bootstrap } | Should -Throw

        Assert-MockCalled Move-Item -Times 0 -Exactly
        Assert-FailurePreservesDestination -ExpectedBytes ([byte[]](9, 9))
    }

    It 'rejects checksum mismatches before destination placement' -Skip:(-not $script:bootstrapAvailable) {
        Set-DestinationBytes -Bytes ([byte[]](9, 9))
        Mock Invoke-WebRequest {
            param($Uri, $OutFile)
            [IO.File]::WriteAllBytes($OutFile, $script:downloadBytes)
        }
        Mock Get-FileHash { [pscustomobject]@{ Hash = ('0' * 64) } }
        Mock Move-Item {}

        { Invoke-Bootstrap } | Should -Throw

        Assert-MockCalled Move-Item -Times 0 -Exactly
        Assert-FailurePreservesDestination -ExpectedBytes ([byte[]](9, 9))
    }

    It 'cleans verified temporary bytes when destination placement fails' -Skip:(-not $script:bootstrapAvailable) {
        Set-DestinationBytes -Bytes ([byte[]](9, 9))
        Mock Invoke-WebRequest {
            param($Uri, $OutFile)
            [IO.File]::WriteAllBytes($OutFile, $script:downloadBytes)
        }
        Mock Get-FileHash { [pscustomobject]@{ Hash = $script:actualSha256 } }
        Mock Move-Item { throw 'move failure' }

        { Invoke-Bootstrap } | Should -Throw

        Assert-MockCalled Move-Item -Times 1 -Exactly
        Assert-FailurePreservesDestination -ExpectedBytes ([byte[]](9, 9))
    }
}