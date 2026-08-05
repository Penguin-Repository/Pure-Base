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

    It 'fully initializes shading data and derives Cutout coverage from base without clamping HDR RGB' {
        $initialColorIndex = $surface.IndexOf('sd.col = half4(0, 0, 0, 0);', [StringComparison]::Ordinal)
        $modelNormalIndex = $surface.IndexOf('SCModelInitializeTangentNormal(sd);', [StringComparison]::Ordinal)
        $baseIndex = $surface.IndexOf('__SC_PHASE_base__', [StringComparison]::Ordinal)
        $alphaSaturateIndex = $surface.IndexOf('sd.albedoAlpha.a = saturate(sd.albedoAlpha.a);', [StringComparison]::Ordinal)
        $finalColorIndex = $surface.IndexOf('sd.col = sd.albedoAlpha;', [StringComparison]::Ordinal)
        $coverageIndex = $surface.IndexOf('coverage = sd.albedoAlpha.a;', [StringComparison]::Ordinal)

        ($initialColorIndex -ge 0) | Should -BeTrue
        ($modelNormalIndex -gt $initialColorIndex) | Should -BeTrue
        ($baseIndex -gt $modelNormalIndex) | Should -BeTrue
        ($alphaSaturateIndex -gt $baseIndex) | Should -BeTrue
        ($finalColorIndex -gt $alphaSaturateIndex) | Should -BeTrue
        ($coverageIndex -gt $finalColorIndex) | Should -BeTrue
        $surface | Should -Not -Match 'sd\.albedoAlpha\s*=\s*saturate\s*\(\s*sd\.albedoAlpha\s*\)\s*;?'
    }

    It 'keeps postpixel as the final color mutation hook' {
        $postpixelIndex = $fragmentHost.IndexOf('__SC_PHASE_postpixel__', [StringComparison]::Ordinal)
        ($postpixelIndex -ge 0) | Should -BeTrue

        $returnIndex = $fragmentHost.IndexOf('return sd.col;', $postpixelIndex, [StringComparison]::Ordinal)
        ($returnIndex -gt $postpixelIndex) | Should -BeTrue

        $tail = $fragmentHost.Substring($postpixelIndex, $returnIndex - $postpixelIndex)
        $tail | Should -Not -Match 'sd\.col\s*='
        $tail | Should -Not -Match 'sd\.col\.[rgba]{1,4}\s*='
        $tail | Should -Not -Match 'UNITY_APPLY_FOG'
    }
}
