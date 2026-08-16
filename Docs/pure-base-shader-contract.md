<!--
Copyright 2026 Penguin

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
-->

# Pure-Base Shader Contract

This document defines the stable public contract of the Pure-Base shader package for users and Shader-Core module authors. Pure-Base is a minimal shader host, not a feature-complete material system.

## Runtime Boundary

- Target pipeline: Unity Built-in Render Pipeline only.
- Integration validation editor: Unity `2022.3.22f1`.
- Integration test graphics API: D3D11, forced by the harness.
- Shader-Core dependency: exactly `jp.lilxyzw.shadercore` `0.1.9`.
- Pure-Base does not automatically allow future `0.1.x` releases. Shader-Core upstream has not declared compatibility across `0.x` releases, and importer, ProjectSettings, and method-shape contracts are sensitive.
- Opaque, Cutout, and Transparent rendering modes are supported. URP is outside the supported contract.

## Stable Shader Paths

The package publishes exactly these shader paths:

| Shader path | Contracted model |
| --- | --- |
| `PureBase/Unlit` | Ignores host direct and indirect lighting in the final output, except for module effects. |
| `PureBase/Toon` | Uses binary direct diffuse with a stable direct-plus-SH direction, bright/dark SH bands, and Shader-Core lightmap input. |
| `PureBase/PBR` | Uses a continuous direct metallic BRDF with Unity Standard indirect GI and reflection probes. |
| `PureBase/Hybrid` | Replaces only the PBR direct diffuse response with Toon-style binary diffuse while sharing PBR specular and IBL. |

Each shader is independently usable without an optional module.

## Material and Pass Contract

Every product shader source retains exactly four passes:

| Pass | Ownership and restrictions |
| --- | --- |
| `ForwardBase` | Builds the normal surface and lighting result. PBR and Hybrid own Unity Standard indirect GI and reflection-probe evaluation here. |
| `ForwardAdd` | Additional direct-light contribution only, with black fog semantics. PBR and Hybrid must not duplicate indirect GI or reflection-probe lighting here. |
| `ShadowCaster` | When enabled for Cutout, applies coverage after the Shader-Core `base` phase, so module changes to `sd.albedoAlpha.a` affect casting. |
| `Meta` | Uses the host base-texture Cutout coverage for Meta/lightmap workflows when enabled. This dedicated pass does not execute the standard phase ABI. |

The effective tags, queue, blend state, depth writing, and pass enablement are selected by the rendering-mode ABI below. The `ForwardAdd` additive blend state in Opaque and Cutout is an additional direct-light pass, not transparent blending.

### Stencil pass policy

The camera Stencil policy is bounded to the forward passes:

- `ForwardBase` applies all seven public Stencil settings.
- `ForwardAdd` compares the Stencil value left by `ForwardBase` using only `_StencilRef`, `_StencilReadMask`, and `_StencilComp`. It fixes `WriteMask` to `0` and `Pass`, `Fail`, and `ZFail` to `Keep`, so additional lights cannot mutate Stencil.
- `ShadowCaster` and `Meta` do not apply the camera Stencil policy.

The defaults are `Always` with `Keep` for all outcomes (`0, 255, 255, 8, 0, 0, 0`). They are a no-op Stencil state: a legacy material without saved Stencil fields continues to draw and does not mutate Stencil. A `ForwardBase` operation such as `Replace`, `Incr`, `Decr`, or `Invert` can intentionally change the value observed by `ForwardAdd`; the additional-light pass then accepts or rejects independently based on that post-`ForwardBase` value. It does not preserve a comparison against the pre-`ForwardBase` value.

The Stencil ABI adds no Stencil-specific keyword, shader variant, pass, or package dependency. Rendering-mode changes and Resync synchronize rendering-mode state while preserving all user Stencil values.

## Rendering-mode ABI

`_RenderingMode` is a ShaderLab `Integer` backed by `SC_uint` with these values:

| Value | Mode | Contract |
| ---: | --- | --- |
| `0` | Opaque | Uses `RenderType=Opaque`, queue `2000`, blend `One Zero`, and `ZWrite 1`. Opaque rendering is uncut and unblended; lighting contributions remain enabled. |
| `1` | Cutout (default) | Clears the material queue override to `-1`, resolving `RenderType=TransparentCutout` and the `AlphaTest` queue at `2450`. It uses no mode keyword, clips coverage, and keeps lighting contributions enabled. |
| `2` | Transparent | Uses `RenderType=Transparent`, queue `3000`, base blend `SrcAlpha OneMinusSrcAlpha`, additional-light blend `SrcAlpha One`, and `ZWrite 0`. `ShadowCaster` and `Meta` are disabled. |

Cutout is the keyword-free state. Opaque and Transparent use only local rendering-mode keywords. All source shaders retain their four pass declarations even when Transparent disables `ShadowCaster` and `Meta`.

Coverage behavior is part of the public contract: Opaque is uncut and unblended, Cutout clips coverage, and Transparent alpha-blends without writing depth. The final alpha produced by `postpixel` controls the `ForwardBase` and `ForwardAdd` source alpha.

The explicit editor action is `PureBaseMaterialRenderingMode.Apply(Material)`. The selected-material menu is `Assets/PureBase/Resync Rendering Mode`. Opening or refreshing the Inspector does not migrate or dirty a legacy material. Runtime switching is not guaranteed. An explicit mode change or Resync resets the standard queue and synchronizes derived state; a user custom queue remains until the next explicit mode edit or Resync.

## Public Property ABI

All four shaders expose exactly these common visible properties, in declaration order:

| Property | Applies to |
| --- | --- |
| `_BaseTexture` | All shaders |
| `_BaseColor` | All shaders |
| `_SharedMask` | All shaders |
| `_SharedGradients` | All shaders |
| `_RenderingMode` | All shaders |
| `_Cutoff` | All shaders |
| `_Cull` | All shaders |
| `_StencilRef` | All shaders |
| `_StencilReadMask` | All shaders |
| `_StencilWriteMask` | All shaders |
| `_StencilComp` | All shaders |
| `_StencilPass` | All shaders |
| `_StencilFail` | All shaders |
| `_StencilZFail` | All shaders |

The model-specific properties are:

| Shader path | Additional properties |
| --- | --- |
| `PureBase/Unlit` | None |
| `PureBase/Toon` | `_NormalMap`, `_NormalScale` |
| `PureBase/PBR` | `_NormalMap`, `_NormalScale`, `_Metallic`, `_Roughness`, `_UseUnityStandardDiffuseBrightness` |
| `PureBase/Hybrid` | `_NormalMap`, `_NormalScale`, `_Metallic`, `_Roughness`, `_UseUnityStandardDiffuseBrightness` |

PBR and Hybrid use byte-identical property declarations. `_Roughness` clamps from `0.002` to `1`.

### Direct diffuse brightness ABI and semantics

`_UseUnityStandardDiffuseBrightness` is exposed only by `PureBase/PBR` and `PureBase/Hybrid`. It is a ShaderLab `Integer` backed by `SC_uint`, declared with `[SCToggle]`, and supports values `0` and `1` with a default of `0`. The declarations are appended after `_Roughness` and remain byte-identical between PBR and Hybrid.

The default/off value `0` preserves the former direct diffuse coefficient `1/pi`. The enabled value `1` selects coefficient `1`, making the direct diffuse contribution approximately `pi` times the former contribution before later blending, tone, and saturation. This does not mean that total output is generally `pi` times brighter, because the other lighting terms are not multiplied by this setting.

The setting affects PBR's continuous direct diffuse and Hybrid's binary direct diffuse through both `ForwardBase` and `ForwardAdd`. It does not affect direct GGX specular, Unity Standard indirect GI, reflection probes, lightmaps, or `Meta`. `ForwardAdd` remains an additional direct-light pass only.

The term `Unity Standard-compatible` refers only to effective direct-diffuse normalization and a controlled normal-incidence comparison. It does not claim Unity Standard's Disney diffuse angular or roughness response, or full Standard BRDF or material equivalence.

The forbidden release-boundary property names `_Emission`, `_Rim`, `_MatCap`, and `_ClearCoat` are not part of this ABI.

### Stencil ABI

All four product shaders expose the following public Stencil properties. The declarations use the Float-compatible `SC_float` ABI, including the enum-backed values.

| Property | Public UI contract | Default |
| --- | --- | ---: |
| `_StencilRef` | `SC_float`; integer UI range `0-255` | `0` |
| `_StencilReadMask` | `SC_float`; integer UI range `0-255` | `255` |
| `_StencilWriteMask` | `SC_float`; integer UI range `0-255` | `255` |
| `_StencilComp` | `SC_float`; `UnityEngine.Rendering.CompareFunction` | `8` (`Always`) |
| `_StencilPass` | `SC_float`; `UnityEngine.Rendering.StencilOp` | `0` (`Keep`) |
| `_StencilFail` | `SC_float`; `UnityEngine.Rendering.StencilOp` | `0` (`Keep`) |
| `_StencilZFail` | `SC_float`; `UnityEngine.Rendering.StencilOp` | `0` (`Keep`) |

The three mask/reference properties use the `0-255` UI range. The comparison and operation properties use Unity's `CompareFunction` and `StencilOp` enums respectively.

## Shader-Core Phase ABI

The standard insertion points are shared by the product hosts in this order:

`morph` -> `postvertex` -> `base` -> `light` -> `customlight` -> `modifylight` -> `shade` -> `reflection` -> `add` -> `postpixel`

External modules may target these standard phases. The `base` phase runs before Cutout coverage is finalized. The host saturates only `sd.albedoAlpha.a` before the alpha test; `sd.albedoAlpha.rgb` remains unclamped so HDR base color and module color adjustments are preserved. The host finalizes output alpha and applies fog before `postpixel`; no host color mutation occurs after `postpixel` before returning the fragment result. The final alpha from `postpixel` is the source alpha for both `ForwardBase` and `ForwardAdd`.

`Meta` is not a standard-phase execution path. Pass ownership remains fixed: `ForwardBase` builds the normal surface and lighting result, `ForwardAdd` is additional direct light only, `ShadowCaster` honors base-phase Cutout changes when enabled, and `Meta` retains host-owned Cutout coverage when enabled.

## Model Semantics

### Unlit

Unlit returns the base surface without host direct, baked, ambient, or environmental lighting in its final output. Shader-Core module effects remain applicable. Its `ForwardAdd` result is black, preserving the additional-light pass contract without adding host lighting.

### Toon

Toon evaluates a binary direct diffuse response from the surface normal and light direction. Its `ForwardBase` direction combines the Shader-Core direct aggregate with the first-order SH direction, and its ambient result selects a fixed bright or dark SH band from that direction. Shader-Core continues to provide the lightmap input; when Shader-Core supplies the lightmap aggregate, Toon does not synthesize an additional baked-light contribution. `ForwardAdd` contributes direct light only.

### OpenLit-derived Toon direction and SH bands

The module-free Toon host uses a bounded Pure Base adaptation of selected OpenLit 1.0.2 BIRP concepts. It retains Shader-Core's post-`light` aggregation boundary rather than copying OpenLit's `ComputeLights` or replacing Shader-Core light enumeration.

- The post-`light` direct aggregate weights each `light.color` with the OpenLit color-space luminance coefficients: `(0.22, 0.707, 0.071)` under `UNITY_COLORSPACE_GAMMA`, or `(0.0396819152, 0.458021790, 0.00609653955)` in Linear. Module changes made through the established `light` phase therefore remain part of the Toon aggregate.
- The scene direction adds the positive-Y first-order SH direction to that direct aggregate. The direction vector is `directAggregate + ((shAr.rgb + shAg.rgb + shAb.rgb) / 3)` with the Y component made positive, plus the fixed fallback `(0.001, 0.002, 0.001)`. The fallback is added before normalization, including for a nonzero aggregate. Exact-zero or nonfinite direction vectors use the fallback; finite near-cancellation residuals are normalized normally.
- In the supported normal BIRP scope, OpenLit `GetV` is the identity: the SH evaluator uses the selected scene direction without a camera or world-position dependency. Light Volumes, direction override, and other expanded OpenLit scope are not implemented.
- The bright band evaluates the unscaled identity-BIRP `V` using the L0/L2 base plus the L1 term along `V`. The dark band reuses the same L0/L2 base and evaluates L1 along the normalized SH RGB direction `normalize(shAr.rgb + shAg.rgb + shAb.rgb)`. An exact-zero or nonfinite SH direction contributes zero dark-band L1 so the result remains finite.
- Both bands are assembled before color-space conversion. Gamma converts both assembled bands with Unity's Linear-to-sRGB conversion; Linear leaves both assembled bands unconverted. The selected band remains the binary result of `step(0, dot(surfaceNormal, lightDirection))`.

These equations are Pure Base's narrow reimplementation of inspected OpenLit 1.0.2 concepts as used by lilToon 2.3.4. They do not copy upstream function bodies, add an OpenLit dependency, or imply an official lilToon/OpenLit association.

The ownership boundaries remain explicit: `ForwardAdd` publishes and uses only normalized direct aggregate direction when its squared length is greater than `0.000001`, otherwise zero; it adds no SH direction, fallback, or environment band. `LIGHTMAP_ON` and disabled Unity SH sampling preserve the direct direction but suppress Toon-generated SH bands, while Shader-Core owns lightmap decoding and Mixed/Subtractive handling. `sd.shadow` affects host-managed direct Toon radiance exactly once and never direction or SH evaluation. PBR, Hybrid, and Unlit retain their existing lighting paths and do not inherit Toon's OpenLit-derived helper.

### Toon direct-light visibility contract

For `PureBase/Toon`, the per-light `light.color` exposed to the `light` phase is the scene/direct light color multiplied by non-shadow distance, spot, and cookie attenuation. Unity effective visibility is published separately as `sd.shadow` before the `light` phase, so the same value is available to the `modifylight` and `shade` phases. This contract applies across the supported Unity light-kind branches, including directional, point, spot, point-cookie, and directional-cookie inputs.

`sd.shadow` is Unity effective per-light visibility: it includes realtime shadowing, baked occlusion and Shadowmask mixing, and shadow-distance fade wherever Unity enables those behaviors. It is not a raw realtime-only shadow sample. Existing Toon light modules that assumed Shader-Core had already multiplied shadow visibility into `light.color` must migrate to `sd.shadow`.

After the `light` phase, the Toon host consumes `sd.shadow` exactly once while accumulating host-managed direct radiance into `lightSum.color`. It does not apply visibility to aggregate light direction, SH, lightmap, or environment lighting, and it does not consume the value again after that direct-radiance evaluation. This Toon-only ownership adds no second `sd.shadow` consumption to PBR, Hybrid, or Unlit. A module may observe `sd.shadow` for classification or use it for an independent module-owned effect, but it must not multiply host-managed Toon direct radiance or color by `sd.shadow` again. `customlight` remains responsible for the visibility of lights it authors or changes after main-light aggregation; the Toon host does not infer or add that visibility for it.

In the exact `LIGHTMAP_ON && LIGHTMAP_SHADOW_MIXING && !SHADOWS_SHADOWMASK && SHADOWS_SCREEN` case, Shader-Core suppresses the main-light callback, `sd.shadow` remains at its initialized value `1`, and Shader-Core owns Subtractive application exactly once. This Shader-Core Mixed/Subtractive handling does not add a second Toon visibility consumption. Lightmap and SH environment lighting remain separate from direct-light visibility. `ShadowCaster` controls casting only and is unchanged by this receiving-side split; there is no material ABI, render-mode, or pass change.

### PBR

PBR evaluates a continuous metallic BRDF for direct lighting. Its `ForwardBase` also evaluates Unity Standard indirect GI and reflection probes. Its `ForwardAdd` evaluates only the additional direct BRDF contribution.

### Hybrid

Hybrid preserves the mathematically identical binary direct-diffuse equation in its existing PBR path, but does not use Toon SH banding. Toon SH never feeds Hybrid's lighting direction. Hybrid retains Unity Standard indirect GI, reflection probes, the PBR direct-light direction, and direct GGX specular, together with the PBR `ForwardBase` and `ForwardAdd` ownership rules.

## Toon Lighting Classification and Compatibility

The following boundary is fixed for the module-free Toon host:

| Classification | Lighting concepts |
| --- | --- |
| Host-essential | Binary diffuse, stable direct-plus-SH scene direction, bright/dark SH bands, Shader-Core lightmap input, and `ForwardBase`/`ForwardAdd` separation. |
| Shader-Core module-owned | Configurable shadow bands, colors, and masks; direction overrides; lighting limits; monochrome and as-unlit controls; and other optional artistic controls. |
| Out of scope | SRP or platform integrations, APV/LPPV, light volumes, LTCGI, and unrelated lilToon effects. |

The fixed host behavior adds no public property, keyword, pass, variant, or dependency. The public property ABI is unchanged, so existing materials require no migration and receive the fixed Toon host behavior automatically.

## Module Boundary

The package-owned validation source of truth is `Packages/jp.penguin.purebase/Tests`. The release ZIP excludes `Tests/**` and test-only `*.scmodule` files. Tracked `.scmodule` files are allowed only within the package-owned `Tests/**` fixture boundary, and package-local `Assets/PureBase.Tests` assets are not part of the release package. Rim, MatCap, decals, detail, emission, dissolve, distance fade, parallax, hair, anisotropic specular, clear coat, glitter, and platform-specific integrations are separate-module concerns and are not additions to this package.

## Validation Lanes

- `Packages/jp.penguin.purebase/Tests/Run-PureBaseRegression.ps1 -Mode Daily` is the read-only Daily lane. It runs only `PureBase.Tests.Daily` and protects the project settings and tracked package tree before and after execution.
- `Packages/jp.penguin.purebase/Tests/Run-PureBaseRegression.ps1 -Mode Initialize` is a separate write-capable setup lane for fixed Shader-Core test hosts. It is not part of Daily.
- Fixture baking and canonical baseline regeneration are explicit write-capable operations separate from Daily. Daily reads `Tests/Baselines/birp-d3d11-2022.3.22f1.json` and does not create or replace it.
- `Packages/jp.penguin.purebase/Tests/Release/Run-PureBaseReleaseValidation.ps1` builds and validates the release ZIP in one disposable external consumer directory. Cold resets remove only that consumer's `Library`; immutable consumer inputs are checked around the reset. The consumer directory is removed at the end unless `-KeepConsumer` is used.

## Verification Evidence

The final disposable-project matrix passed `62/62` under Unity `2022.3.22f1` with D3D11. The matrix covers module-free imports, all ten standard external phase probes, PBR and Hybrid finite/reflection/`ForwardAdd`/specular behavior, Unlit and Toon regressions, a fixed validation-scene bake with Meta and shadow checks, and 56 Built-in Render Pipeline variants.

The validation artifact records dynamic lightmap as `NOT_DETERMINISTIC_IN_BATCH_EDITMODE`. The result documents the deterministic limitation of batch EditMode; it does not verify runtime dynamic-lightmap rendering.

The executable procedure and artifact layout are documented in [`Tests/README.md`](../Tests/README.md). Package-level usage and release scope are summarized in [the package README](../README.md).
