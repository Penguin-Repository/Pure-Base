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

Describe 'Shader-Core phase compatibility' {
    BeforeAll {
        $repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
        $surfacePath = Join-Path $repositoryRoot 'Shaders/Common/surface.hlsl'
        $fragmentHostPath = Join-Path $repositoryRoot 'Shaders/Common/birp_host.hlsl'
        $surface = Get-Content -LiteralPath $surfacePath -Raw
        $fragmentHost = Get-Content -LiteralPath $fragmentHostPath -Raw
    }

    It 'derives Cutout coverage from the base-phase result' {
        $baseIndex = $surface.IndexOf('__SC_PHASE_base__', [StringComparison]::Ordinal)
        $saturateIndex = $surface.IndexOf('sd.albedoAlpha = saturate(sd.albedoAlpha);', [StringComparison]::Ordinal)
        $coverageIndex = $surface.IndexOf('coverage = sd.albedoAlpha.a;', [StringComparison]::Ordinal)

        $baseIndex | Should -BeGreaterThanOrEqual 0
        $saturateIndex | Should -BeGreaterThan $baseIndex
        $coverageIndex | Should -BeGreaterThan $saturateIndex
    }

    It 'keeps postpixel as the final color mutation hook' {
        $postpixelIndex = $fragmentHost.IndexOf('__SC_PHASE_postpixel__', [StringComparison]::Ordinal)
        $returnIndex = $fragmentHost.IndexOf('return sd.col;', $postpixelIndex, [StringComparison]::Ordinal)

        $postpixelIndex | Should -BeGreaterThanOrEqual 0
        $returnIndex | Should -BeGreaterThan $postpixelIndex

        $tail = $fragmentHost.Substring($postpixelIndex, $returnIndex - $postpixelIndex)
        $tail | Should -Not -Match 'sd\.col\s*='
        $tail | Should -Not -Match 'sd\.col\.[rgba]{1,4}\s*='
        $tail | Should -Not -Match 'UNITY_APPLY_FOG'
    }
}
