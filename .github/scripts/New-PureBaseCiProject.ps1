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
  [string]$ProjectRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRootFullPath = [System.IO.Path]::GetFullPath($ProjectRoot)
$packageRoot = Join-Path $projectRootFullPath 'Packages/jp.penguin.purebase'
$shaderCoreRoot = Join-Path $projectRootFullPath 'Packages/jp.lilxyzw.shadercore'

foreach ($requiredPath in @($packageRoot, $shaderCoreRoot)) {
  if (-not (Test-Path -LiteralPath $requiredPath -PathType Container)) {
    throw "Required package checkout is missing: '$requiredPath'."
  }
}

$packageJson = Get-Content -LiteralPath (Join-Path $packageRoot 'package.json') -Raw | ConvertFrom-Json
if ([string]$packageJson.name -ne 'jp.penguin.purebase') {
  throw "Unexpected Pure-Base package identity '$($packageJson.name)'."
}

$shaderCoreJson = Get-Content -LiteralPath (Join-Path $shaderCoreRoot 'package.json') -Raw | ConvertFrom-Json
if ([string]$shaderCoreJson.name -ne 'jp.lilxyzw.shadercore' -or [string]$shaderCoreJson.version -ne '0.1.9') {
  throw "The CI workspace requires jp.lilxyzw.shadercore exactly 0.1.9."
}

$assetsRoot = Join-Path $projectRootFullPath 'Assets'
$projectSettingsRoot = Join-Path $projectRootFullPath 'ProjectSettings'
$packagesRoot = Join-Path $projectRootFullPath 'Packages'
foreach ($directory in @($assetsRoot, $projectSettingsRoot, $packagesRoot)) {
  New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

$consumerProjectSettingsRoot = Join-Path $packageRoot 'Tests/Release/ConsumerProject/ProjectSettings'
$projectVersionSource = Join-Path $consumerProjectSettingsRoot 'ProjectVersion.txt'
if (-not (Test-Path -LiteralPath $projectVersionSource -PathType Leaf)) {
  throw "Pinned Unity ProjectVersion source is missing: '$projectVersionSource'."
}
Copy-Item -LiteralPath $projectVersionSource -Destination (Join-Path $projectSettingsRoot 'ProjectVersion.txt') -Force

# This is a reviewed ProjectSettings snapshot from a local project after VRChat SDK
# setup established the VRC-named quality profiles. CI intentionally does not install
# com.vrchat.base, com.vrchat.avatars, or com.vrchat.worlds, so copying this file only
# reproduces the captured Unity QualitySettings values; it does not reproduce VRChat
# SDK editor initialization, scripting defines, import hooks, or build behavior.
# Recompare and refresh this fixture whenever the VRChat SDK or its Project Setup changes.
$qualitySettingsSource = Join-Path $consumerProjectSettingsRoot 'QualitySettings.asset'
if (-not (Test-Path -LiteralPath $qualitySettingsSource -PathType Leaf)) {
  throw "Reviewed VRChat-project QualitySettings snapshot is missing: '$qualitySettingsSource'."
}
Copy-Item -LiteralPath $qualitySettingsSource -Destination (Join-Path $projectSettingsRoot 'QualitySettings.asset') -Force

$manifest = [ordered]@{
  dependencies = [ordered]@{
    'com.unity.test-framework' = '1.1.33'
  }
}
$manifestText = ($manifest | ConvertTo-Json -Depth 4) + "`n"
[System.IO.File]::WriteAllText(
  (Join-Path $packagesRoot 'manifest.json'),
  $manifestText,
  [System.Text.UTF8Encoding]::new($false)
)

$ownerSceneText = @'
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
  m_OcclusionBakeSettings:
    smallestOccluder: 5
    smallestHole: 0.25
    backfaceThreshold: 100
  m_SceneGUID: 00000000000000000000000000000000
  m_OcclusionCullingData: {fileID: 0}
--- !u!104 &2
RenderSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 9
  m_Fog: 0
  m_FogColor: {r: 0.5, g: 0.5, b: 0.5, a: 1}
  m_FogMode: 3
  m_FogDensity: 0.01
  m_LinearFogStart: 0
  m_LinearFogEnd: 300
  m_AmbientSkyColor: {r: 0.212, g: 0.227, b: 0.259, a: 1}
  m_AmbientEquatorColor: {r: 0.114, g: 0.125, b: 0.133, a: 1}
  m_AmbientGroundColor: {r: 0.047, g: 0.043, b: 0.035, a: 1}
  m_AmbientIntensity: 1
  m_AmbientMode: 3
  m_SubtractiveShadowColor: {r: 0.42, g: 0.478, b: 0.627, a: 1}
  m_SkyboxMaterial: {fileID: 0}
  m_HaloStrength: 0.5
  m_FlareStrength: 1
  m_FlareFadeSpeed: 3
  m_HaloTexture: {fileID: 0}
  m_SpotCookie: {fileID: 0}
  m_DefaultReflectionMode: 0
  m_DefaultReflectionResolution: 128
  m_ReflectionBounces: 1
  m_ReflectionIntensity: 1
  m_CustomReflection: {fileID: 0}
  m_Sun: {fileID: 0}
  m_IndirectSpecularColor: {r: 0, g: 0, b: 0, a: 1}
  m_UseRadianceAmbientProbe: 0
--- !u!157 &3
LightmapSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 12
  m_GIWorkflowMode: 1
  m_GISettings:
    serializedVersion: 2
    m_BounceScale: 1
    m_IndirectOutputScale: 1
    m_AlbedoBoost: 1
    m_EnvironmentLightingMode: 0
    m_EnableBakedLightmaps: 1
    m_EnableRealtimeLightmaps: 0
  m_LightmapEditorSettings:
    serializedVersion: 12
    m_Resolution: 2
    m_BakeResolution: 40
    m_AtlasSize: 1024
    m_AO: 0
    m_AOMaxDistance: 1
    m_CompAOExponent: 1
    m_CompAOExponentDirect: 0
    m_ExtractAmbientOcclusion: 0
    m_Padding: 2
    m_LightmapParameters: {fileID: 0}
    m_LightmapsBakeMode: 1
    m_TextureCompression: 1
    m_FinalGather: 0
    m_FinalGatherFiltering: 1
    m_FinalGatherRayCount: 256
    m_ReflectionCompression: 2
    m_MixedBakeMode: 2
    m_BakeBackend: 1
    m_PVRSampling: 1
    m_PVRDirectSampleCount: 32
    m_PVRSampleCount: 512
    m_PVRBounces: 2
    m_PVREnvironmentSampleCount: 256
    m_PVREnvironmentReferencePointCount: 2048
    m_PVRFilteringMode: 1
    m_PVRDenoiserTypeDirect: 1
    m_PVRDenoiserTypeIndirect: 1
    m_PVRDenoiserTypeAO: 1
    m_PVRFilterTypeDirect: 0
    m_PVRFilterTypeIndirect: 0
    m_PVRFilterTypeAO: 0
    m_PVREnvironmentMIS: 1
    m_PVRCulling: 1
    m_PVRFilteringGaussRadiusDirect: 1
    m_PVRFilteringGaussRadiusIndirect: 5
    m_PVRFilteringGaussRadiusAO: 2
    m_PVRFilteringAtrousPositionSigmaDirect: 0.5
    m_PVRFilteringAtrousPositionSigmaIndirect: 2
    m_PVRFilteringAtrousPositionSigmaAO: 1
    m_ExportTrainingData: 0
    m_TrainingDataDestination: TrainingData
    m_LightProbeSampleCountMultiplier: 4
  m_LightingDataAsset: {fileID: 0}
  m_LightingSettings: {fileID: 0}
--- !u!196 &4
NavMeshSettings:
  serializedVersion: 2
  m_ObjectHideFlags: 0
  m_BuildSettings:
    serializedVersion: 3
    agentTypeID: 0
    agentRadius: 0.5
    agentHeight: 2
    agentSlope: 45
    agentClimb: 0.4
    ledgeDropHeight: 0
    maxJumpAcrossDistance: 0
    minRegionArea: 2
    manualCellSize: 0
    cellSize: 0.16666667
    manualTileSize: 0
    tileSize: 256
    buildHeightMesh: 0
    maxJobWorkers: 0
    preserveTilesOutsideBounds: 0
    debug:
      m_Flags: 0
  m_NavMeshData: {fileID: 0}
--- !u!1660057539 &9223372036854775807
SceneRoots:
  m_ObjectHideFlags: 0
  m_Roots: []
'@
[System.IO.File]::WriteAllText(
  (Join-Path $assetsRoot 'Pure-Base.unity'),
  $ownerSceneText.Replace("`r`n", "`n") + "`n",
  [System.Text.UTF8Encoding]::new($false)
)

Write-Output "Prepared Pure-Base CI Unity project: $projectRootFullPath"
Write-Output "Pure-Base package version: $($packageJson.version)"
Write-Output "Shader-Core package version: $($shaderCoreJson.version)"
Write-Output "VRChat-project QualitySettings snapshot: $qualitySettingsSource"
Write-Output "VRChat SDK packages installed in generated CI project: none"

# Normalize only an accepted native-command status when the caller has one.
if ((Test-Path Variable:LASTEXITCODE) -and $LASTEXITCODE -lt 8) { $global:LASTEXITCODE = 0 }
