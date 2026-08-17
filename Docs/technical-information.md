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

# Pure Base technical information

Language: [日本語](technical-information.ja.md)

This document collects the implementation, compatibility, release, and validation information that is intentionally kept out of the user-facing [README](../README.md).

Pure Base is a minimal Shader-Core host. It is not intended to become a feature-complete material system.

## Supported environment and compatibility boundary

- Unity `2022.3` is required. Integration validation is fixed to Unity `2022.3.22f1`.
- Only the Built-in Render Pipeline is supported. URP is not supported.
- The package requires exactly `jp.lilxyzw.shadercore` `0.1.9`.
- Future `0.1.x` Shader-Core releases are not accepted automatically. Shader-Core does not declare compatibility across `0.x` releases, and importer, project-setting, and method-shape contracts may change.
- The integration harness forces D3D11 during test execution.
- Opaque, Cutout, and Transparent rendering modes are supported. Cutout is the default mode; URP is not supported.

## Stable shader paths

| Shader path | Lighting model |
| --- | --- |
| `PureBase/Unlit` | Base color output without host lighting |
| `PureBase/Toon` | Binary direct diffuse with a stable direct-plus-SH direction, bright/dark SH bands, and Shader-Core lightmap support |
| `PureBase/PBR` | Continuous metallic BRDF with Unity Standard indirect GI and reflection probes |
| `PureBase/Hybrid` | Toon-style binary direct diffuse with the PBR specular and IBL path |

Each shader remains independently usable without an optional Shader-Core module.

The complete, stable pass and property contract is defined in [Pure Base shader contract](pure-base-shader-contract.md).

## Render passes and public properties

Every shader source retains exactly four passes. The rendering mode selects the effective render state; Transparent disables the `ShadowCaster` and `Meta` passes without removing their source declarations:

- `ForwardBase`
- `ForwardAdd`
- `ShadowCaster`
- `Meta`

### Rendering mode ABI

`_RenderingMode` is a ShaderLab `Integer` backed by `SC_uint`. The values are `Opaque=0`, `Cutout=1` (default), and `Transparent=2`.

| Mode | Effective state |
| --- | --- |
| Opaque | `RenderType=Opaque`, queue `2000`, blend `One Zero`, `ZWrite 1`; uncut and unblended with lighting contributions enabled. |
| Cutout | Clears the serialized queue override to `-1`, resolves `RenderType=TransparentCutout` and `AlphaTest` queue `2450`; keyword-free, clips coverage, and keeps lighting contributions enabled. |
| Transparent | `RenderType=Transparent`, queue `3000`, base blend `SrcAlpha OneMinusSrcAlpha`, additional-light blend `SrcAlpha One`, `ZWrite 0`; `ShadowCaster` and `Meta` are disabled. |

Only local Opaque and Transparent keywords are used; Cutout is keyword-free. The final alpha from `postpixel` controls the `ForwardBase` and `ForwardAdd` source alpha.

The explicit editor action is `PureBaseMaterialRenderingMode.Apply(Material)`. For selected materials, use `Assets/PureBase/Resync Rendering Mode`. Opening or refreshing the Inspector does not migrate or dirty a legacy material. Runtime switching is not guaranteed. An explicit mode change or Resync resets the standard queue and synchronizes derived state; a user custom queue remains until the next explicit mode edit or Resync.

All four shaders expose these common properties:

`_RenderingMode` (`Integer` backed by `SC_uint`; `Opaque=0`, `Cutout=1` (default), `Transparent=2`), `_BaseTexture`, `_BaseColor`, `_SharedMask`, `_SharedGradients`, `_Cutoff`, `_Cull`

`PureBase/Toon` additionally exposes `_NormalMap` and `_NormalScale`.

`PureBase/PBR` and `PureBase/Hybrid` expose the same normal-map properties plus `_Metallic`, `_Roughness`, and `_UseUnityStandardDiffuseBrightness`. Their public property declarations, including this metadata, are byte-identical. `_Roughness` is a ShaderLab `Float` backed by `SC_float`, with default `0.5`, public perceptual range `[0.089, 1]`, and exact `[SCRange(0.089,1)]` metadata. It remains ordered between `_Metallic` and `_UseUnityStandardDiffuseBrightness`.

### PBR and Hybrid perceptual roughness floor

`_Roughness` stores perceptual roughness `p`, not academic roughness `p^2`. PBR and Hybrid use one shared runtime clamp, `clamp(p, 0.089, 1)`, before creating their shared BRDF data. The clamped value feeds every roughness-sensitive path:

- Direct GGX evaluates `roughnessSquared = p^2` and then reaches `roughnessFourth = p^4` in the direct evaluator for both `ForwardBase` and `ForwardAdd`.
- Unity Standard GI and reflection-probe setup derive `Smoothness = 1 - p` from the same clamped value before `LightingStandard_GI`; the resulting indirect contribution uses the same shared BRDF data.
- PBR and Hybrid `Meta` fragments create the same shared BRDF data, so Meta/lightmapping uses the same floor through its squared-roughness rule.

The floor is `0.089` because the direct evaluator's fourth-power term must remain above the IEEE-754 binary16 minimum positive normal. Specifically, $0.089^4 = 0.000062742241 > 2^{-14} = 0.00006103515625$. Unity URP uses related FP16 protections in its [`BRDF.hlsl`](https://github.com/Unity-Technologies/Graphics/blob/e6595ee2d83c8b02dab6e58abba0ff285c0c80ed/Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl): its BRDF initialization protects squared roughness with `HALF_MIN_SQRT` and the square of that value with `HALF_MIN`. Unity Core's [`CommonMaterial.hlsl`](https://github.com/Unity-Technologies/Graphics/blob/cdc941e1378729b1ca1fafb175151ac3d781ebb0/Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl) also documents that zero or excessively small analytical-light roughness is invalid or can alias. These are related numerical protections, not a claim that URP is supported or that Pure Base implements the URP BRDF.

Built-in Standard's internal `0.002` clamp is a different parameterization. It clamps academic roughness after perceptual roughness has been squared, whereas Pure Base's public `_Roughness` is perceptual `p`. The installed Unity `2022.3.22f1` Built-in Standard source therefore does not define Pure Base's public floor; the same numeral represents a different quantity.

No material migration command or bulk serialized rewrite is added. A stored value below `0.089` remains stored as-is until a user explicitly edits or otherwise rewrites the material, but it evaluates as `0.089` at runtime in direct lighting, Unity Standard GI/reflection, and Meta/lightmapping. Stored values at or above `0.089` retain their public ordering and roughness meaning, and the public default remains `0.5`.

This change does not implement the Issue #13 visibility approximation, Issue #14 multiple-scattering compensation, specular anti-aliasing, or full Unity Standard BRDF parity. It does not change the ownership, placement, or behavior of `_UseUnityStandardDiffuseBrightness`.

### PBR and Hybrid direct-diffuse brightness

`_UseUnityStandardDiffuseBrightness` is a ShaderLab `Integer` backed by `SC_uint`, uses `[SCToggle]`, supports values `0` and `1`, and defaults to `0`. It is exposed only by PBR and Hybrid and is appended after `_Roughness` in both byte-identical property declarations.

With the default/off value `0`, existing and new materials use the former direct diffuse coefficient `1/pi`. With value `1`, the coefficient is `1`, so the direct diffuse contribution is approximately `pi` times the former contribution before later blending, tone, and saturation. Total output is not generally `pi` times brighter because direct GGX specular and indirect terms are unchanged.

The setting affects PBR's continuous direct diffuse and Hybrid's binary direct diffuse through both `ForwardBase` and `ForwardAdd`. It does not affect direct GGX specular, Unity Standard indirect GI, reflection probes, lightmaps, or `Meta`; `ForwardAdd` remains additional direct light only.

`Unity Standard-compatible` describes only effective direct-diffuse normalization and the controlled normal-incidence comparison. It does not describe Unity Standard's Disney diffuse angular or roughness response, or full Standard BRDF or material equivalence.

### Stencil ABI

All four product shaders expose these public Stencil properties through the Float-compatible `SC_float` ABI:

| Property | UI contract | Default |
| --- | --- | ---: |
| `_StencilRef` | Integer range `0-255` | `0` |
| `_StencilReadMask` | Integer range `0-255` | `255` |
| `_StencilWriteMask` | Integer range `0-255` | `255` |
| `_StencilComp` | `UnityEngine.Rendering.CompareFunction` | `8` (`Always`) |
| `_StencilPass` | `UnityEngine.Rendering.StencilOp` | `0` (`Keep`) |
| `_StencilFail` | `UnityEngine.Rendering.StencilOp` | `0` (`Keep`) |
| `_StencilZFail` | `UnityEngine.Rendering.StencilOp` | `0` (`Keep`) |

`ForwardBase` applies all seven settings. `ForwardAdd` compares the post-`ForwardBase` Stencil value using `_StencilRef`, `_StencilReadMask`, and `_StencilComp` only; it fixes `WriteMask` to `0` and `Pass`, `Fail`, and `ZFail` to `Keep`, so additional lights do not mutate Stencil. `ShadowCaster` and `Meta` do not apply the camera Stencil policy.

The default `Always` plus `Keep` state is a no-op: a legacy material without saved Stencil fields continues to draw and does not mutate Stencil. If `ForwardBase` uses `Replace`, `Incr`, `Decr`, `Invert`, or another mutating operation, `ForwardAdd` may compare the changed value and therefore accept or reject independently. The policy intentionally does not preserve a comparison against the pre-`ForwardBase` value.

Stencil adds no Stencil-specific keyword, variant, pass, or package dependency. Rendering-mode changes and Resync preserve user values for all seven Stencil properties.

## Shader-Core integration

The shared standard phase ABI is executed in this order:

`morph`, `postvertex`, `base`, `light`, `customlight`, `modifylight`, `shade`, `reflection`, `add`, `postpixel`

`Meta` is a dedicated pass and does not execute the standard phase sequence.

- `ForwardBase` owns the normal surface and lighting result.
- `ForwardAdd` contributes additional direct light only and uses black fog semantics.
- In Cutout, `ForwardBase`, `ForwardAdd`, and enabled `ShadowCaster` derive coverage from the module-adjusted `sd.albedoAlpha.a` after `base`. Opaque is uncut, while Transparent alpha-blends without depth writing and disables `ShadowCaster` and `Meta`.
- `postpixel` is the final color mutation point. Modules may change the returned alpha there, and that final alpha is used as the source alpha by both forward passes. The product color-mask states remain fixed.
- PBR and Hybrid evaluate Unity Standard indirect GI and reflection probes in `ForwardBase`. Their `ForwardAdd` passes do not duplicate indirect lighting.

Optional visual features belong in separate Shader-Core modules. Pure Base does not include rim lighting, MatCap, decals, detail textures, emission, dissolve, distance fade, parallax, hair or anisotropic specular, clear coat, glitter, or platform-specific integrations.

## Toon lighting boundary and material compatibility

The module-free Toon host owns a binary direct-diffuse response, a stable scene-light direction formed from the direct aggregate and SH, bright/dark SH bands, Shader-Core lightmap input, and the existing `ForwardBase`/`ForwardAdd` separation. Shader-Core modules own configurable shadow bands, colors, and masks; direction overrides; lighting limits; monochrome and as-unlit controls; and other optional artistic controls.

SRP or platform integrations, APV/LPPV, light volumes, LTCGI, and unrelated lilToon effects are outside the Pure Base host scope. Toon uses the existing Shader-Core lightmap aggregate and does not add a second baked-light contribution. `ForwardAdd` remains additional direct light only.

Hybrid retains its unchanged binary direct-diffuse equation inside the PBR path. Toon SH banding never supplies Hybrid's lighting direction. Hybrid continues to use Unity Standard indirect GI, reflection probes, its PBR direct-light direction, and direct GGX specular.

This fixed host behavior adds no public property, keyword, pass, variant, or dependency. The public property ABI is unchanged, so existing materials need no migration and receive the behavior automatically.

### OpenLit-derived Toon behavior and provenance

Pure Base's module-free Toon lighting is a bounded adaptation of selected OpenLit 1.0.2 BIRP concepts inspected through lilToon 2.3.4. It keeps Shader-Core's post-`light` direct-light aggregation and does not copy OpenLit's `ComputeLights` or replace Shader-Core light enumeration.

- Toon weights the post-`light` direct aggregate with color-space-specific OpenLit luminance: `(0.22, 0.707, 0.071)` for Gamma and `(0.0396819152, 0.458021790, 0.00609653955)` for Linear. The aggregate therefore continues to include module-authored `light` changes.
- Its direction combines that aggregate, positive-Y first-order SH, and the fixed fallback `(0.001, 0.002, 0.001)` before normalization. The fallback is always part of the sum; exact-zero or nonfinite totals fall back to that vector, while finite near-cancellation residuals remain normalizable.
- In normal BIRP, OpenLit `GetV` is represented by the identity direction used by the host. There is no camera/position dependency, Light Volumes behavior, direction override, or other expanded OpenLit scope.
- The bright SH band uses unscaled `V` for the L0/L2 base and L1. The dark band reuses that L0/L2 base and uses L1 along the normalized SH RGB direction. A zero or nonfinite SH direction contributes zero dark L1 to keep the result finite.
- Gamma applies Unity's Linear-to-sRGB conversion to both assembled bands; Linear does not convert them. Binary band selection uses the sign of the surface-normal and scene-direction dot product.

`ForwardAdd` is direct-only: it uses normalized direct aggregate direction above the `0.000001` squared-length threshold and zero otherwise, without SH, fallback, or environment contribution. Lightmap decoding and Mixed/Subtractive handling remain Shader-Core-owned, and Toon-generated SH is suppressed for `LIGHTMAP_ON` or disabled Unity SH sampling. `sd.shadow` changes direct Toon radiance once, never the aggregate direction or SH. PBR, Hybrid, and Unlit are unchanged.

### Toon direct-light visibility contract

For `PureBase/Toon`, `light.color` in the `light` phase is the scene/direct light color multiplied by non-shadow distance, spot, and cookie attenuation. Unity effective per-light visibility is exposed independently through `sd.shadow` before `light`, `modifylight`, and `shade`. The same split is used for the supported directional, point, spot, point-cookie, and directional-cookie light branches.

`sd.shadow` represents Unity effective visibility, including realtime shadows, baked occlusion and Shadowmask mixing, and shadow-distance fade where enabled. It is not a raw realtime-only sample. Existing Toon light modules that assumed `light.color` was already pre-shadowed must read `sd.shadow` instead.

After the `light` phase, the Toon host consumes `sd.shadow` exactly once while accumulating host-managed direct radiance into `lightSum.color`. It does not apply visibility to aggregate light direction, SH, lightmap, or environment lighting, and it does not consume the value again after that direct-radiance evaluation. This Toon-only ownership adds no second `sd.shadow` consumption to PBR, Hybrid, or Unlit. A module may observe `sd.shadow` for classification or use it for an independent module-owned effect, but it must not multiply host-managed Toon direct radiance or color by `sd.shadow` again. `customlight` owns the visibility of its own lights after main-light aggregation; the host does not add an implicit visibility factor for that phase.

For `LIGHTMAP_ON && LIGHTMAP_SHADOW_MIXING && !SHADOWS_SHADOWMASK && SHADOWS_SCREEN`, Shader-Core suppresses the main-light callback, leaves `sd.shadow` at its initialized value `1`, and applies Subtractive shadowing exactly once. This Shader-Core Mixed/Subtractive handling does not add a second Toon visibility consumption. Lightmap and SH environment lighting remain separate from direct visibility. `ShadowCaster` remains casting-only and unchanged; this receiving-side contract does not change the material ABI, render modes, or pass declarations.

## Release preparation and publication

`package.json` is the sole release identity and version declaration.

The current package release is `0.2.0-beta.2`.

The `version` input of the manual `Release` workflow verifies the exact version already present in the checked-out package. It does not write or commit a version.

The intended publication sequence is:

1. Prepare and commit the package changes.
2. Run the hosted `Release validation` workflow for that exact commit SHA.
3. Run `Release` from the same branch and commit SHA.

Validation produces one artifact containing a deterministic Store-mode ZIP, a lowercase SHA-256 sidecar, and a schema-1 `release-validation.json` provenance manifest. `Release` selects the latest matching validation run and attempt, verifies that the artifact has not expired, verifies its digest, and publishes the downloaded ZIP without rebuilding it.

If the latest matching validation run is expired or unsuccessful, run validation again for the same SHA. Release does not use an older successful run and does not rebuild the package.

A fresh release requires that neither the matching tag nor GitHub Release already exists. Resume requires an exact annotated tag and a matching draft or published Release at the same SHA. A tag-only failure requires operator investigation.

Draft resume is limited to badge repair and either uploading a missing asset or reusing an asset with the exact expected digest. Published resume requires the release branch to remain at the same SHA and does not change the immutable Release body or assets, including a legacy or missing badge.

The optional `preflight_only=true` input performs the hosted checks without creating a tag, GitHub Release, asset, or VPM dispatch. It should be run before the first production publication.

The Release path does not run Unity, rebuild the ZIP, write or commit `package.json`, or push the release branch.

The package-owned validation source of truth is `Packages/jp.penguin.purebase/Tests` after installation. In this repository, the same content is stored under [`Tests`](../Tests).

The release ZIP excludes `Tests/**` and test-only `*.scmodule` files. Tracked `.scmodule` files are test fixtures and are allowed only within the package-owned `Tests/**` fixture boundary.

## Release and VPM availability

The Release workflow supports both prerelease and stable versions. A prerelease is published as a GitHub prerelease; a stable version is published as stable.

Whether a VPM client displays prereleases depends on that client. Pure Base does not promise that every VCC-compatible client presents prereleases in the same way.

`vpm-yanks.json` is the desired-state policy for the VPM repository. A version key means that the version is yanked; removing the key unyanks it. The reason is public operational documentation and must never contain secrets, credentials, personal data, or other private information.

Changes to `vpm-yanks.json` on the literal `master` branch trigger the synchronization workflow. Operators may also run it manually from `master` after a stale dispatch or receiver outage. The workflow validates the policy at the current commit and sends only the fixed `sync-vpm-yanks` event. It does not accept arbitrary source paths or branches.

The initial yank rollout is gated. Keep the policy empty until the VPM receiver is ready and the confirmed release version is present in the target feed. Only then may the released version be added for a separately approved end-to-end yank and unyank test.

An empty policy is a no-op desired state. A version must not be added before its release exists in the target feed. Feed and receiver changes are eventually consistent, so a stale or premature dispatch fails closed without changing the listing. Retry from `master` with the current policy commit after propagation.

The VPM receiver, VPM repository, and existing `update-vpm` payload contract remain outside the release pipeline. ALCOM prerelease and package-feed behavior is implementation-specific and is not guaranteed for other VCC-compatible clients.

## Validation lanes

The persistent validation lanes are defined under [`Tests`](../Tests).

- `Tests/Run-PureBaseRegression.ps1 -Mode Daily` is the read-only Daily lane. It runs only the `PureBase.Tests.Daily` EditMode assembly and protects project settings and the tracked package tree before and after execution.
- `Tests/Run-PureBaseRegression.ps1 -Mode Initialize` is a separate write-capable setup lane for fixed Shader-Core test hosts. It is not part of Daily.
- Fixture baking and canonical baseline regeneration are explicit write-capable operations separate from Daily. Daily reads `Tests/Baselines/birp-d3d11-2022.3.22f1.json` and never creates or replaces it.
- `Tests/Release/Run-PureBaseReleaseValidation.ps1` builds and validates the release ZIP in one disposable external consumer directory. Its cold reset removes only that consumer's `Library`. The runner verifies the remaining immutable consumer inputs and removes the consumer directory at the end unless `-KeepConsumer` is used.

## Validation evidence

The final disposable-project matrix passed `62/62` under Unity `2022.3.22f1` with D3D11.

It covers module-free imports, all ten standard external phase probes, PBR and Hybrid finite, reflection, `ForwardAdd`, and specular behavior, Unlit and Toon regressions, a fixed validation-scene bake with Meta and shadow checks, and 56 Built-in Render Pipeline variants.

The validation result records dynamic lightmap as `NOT_DETERMINISTIC_IN_BATCH_EDITMODE`. This means the batch EditMode harness does not provide a deterministic dynamic-lightmap binding path. It must not be interpreted as verified runtime dynamic-lightmap rendering.

Release-boundary checks also verified that:

- the release ZIP excludes `Tests/**` and test-only `*.scmodule` files;
- tracked `.scmodule` files are confined to the `Tests/**` fixture boundary;
- the package does not contain `Assets/PureBase.Tests`;
- there is no URP dependency;
- PBR and Hybrid have byte-identical public property declarations; and
- `_Emission`, `_Rim`, `_MatCap`, and `_ClearCoat` are absent.

The executable test procedure and write boundaries are documented in [`Tests/README.md`](../Tests/README.md). CI ownership is documented in [`.github/CI.md`](../.github/CI.md).
