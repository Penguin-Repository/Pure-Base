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

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Describe 'Unity documentation metadata' {
    BeforeAll {
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $docsRoot = Join-Path $repositoryRoot 'Docs'

        function Get-DocumentationMetadataFiles {
            $metadataFiles = [Collections.Generic.List[IO.FileInfo]]::new()
            $docsMetadataPath = $docsRoot + '.meta'
            if (Test-Path -LiteralPath $docsMetadataPath -PathType Leaf) {
                $metadataFiles.Add((Get-Item -LiteralPath $docsMetadataPath))
            }
            foreach ($metadataFile in Get-ChildItem -LiteralPath $docsRoot -Filter '*.meta' -File -Recurse) {
                $metadataFiles.Add($metadataFile)
            }
            return $metadataFiles.ToArray()
        }

        function Get-UnityMetadataGuid {
            param([Parameter(Mandatory = $true)][string]$Path)

            $guidMatches = @(
                Select-String -LiteralPath $Path -Pattern '^guid: ([0-9a-fA-F]{32})$'
            )
            if ($guidMatches.Count -ne 1) {
                return ''
            }
            return $guidMatches[0].Matches[0].Groups[1].Value.ToLowerInvariant()
        }
    }

    It 'tracks a meta file for every documentation asset and directory' {
        $documentationAssets = @(
            Get-Item -LiteralPath $docsRoot
            Get-ChildItem -LiteralPath $docsRoot -Recurse -Force
        )
        $missingMetadata = @(
            $documentationAssets |
                Where-Object { -not $_.Name.EndsWith('.meta', [StringComparison]::Ordinal) } |
                Where-Object { -not (Test-Path -LiteralPath ($_.FullName + '.meta') -PathType Leaf) } |
                ForEach-Object { $_.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/') }
        )

        $missingMetadata | Should -BeNullOrEmpty
    }

    It 'uses unique 32-character hexadecimal GUIDs for documentation metadata' {
        $guidEntries = @(
            Get-DocumentationMetadataFiles |
                ForEach-Object {
                    [pscustomobject]@{
                        Path = $_.FullName.Substring($repositoryRoot.Length + 1).Replace('\', '/')
                        Guid = Get-UnityMetadataGuid -Path $_.FullName
                    }
                }
        )

        @($guidEntries | Where-Object { [string]::IsNullOrEmpty($_.Guid) }).Path | Should -BeNullOrEmpty
        @($guidEntries | Group-Object Guid | Where-Object Count -GT 1).Name | Should -BeNullOrEmpty
    }
}

