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
- Transparent blending is not supported. Every product shader uses fixed Cutout coverage.

## Stable shader paths

| Shader path | Lighting model |
| --- | --- |
| `PureBase/Unlit` | Base color output without host lighting |
| `PureBase/Toon` | Binary direct diffuse with ambient and lightmap support |
| `PureBase/PBR` | Continuous metallic BRDF with Unity Standard indirect GI and reflection probes |
| `PureBase/Hybrid` | Toon-style binary direct diffuse with the PBR specular and IBL path |

Each shader remains independently usable without an optional Shader-Core module.

The complete, stable pass and property contract is defined in [Pure Base shader contract](pure-base-shader-contract.md).

## Render passes and public properties

Every shader uses `RenderType=TransparentCutout`, the `AlphaTest` queue, and exactly four passes:

- `ForwardBase`
- `ForwardAdd`
- `ShadowCaster`
- `Meta`

All four shaders expose these common properties:

`_BaseTexture`, `_BaseColor`, `_SharedMask`, `_SharedGradients`, `_Cutoff`, `_Cull`

`PureBase/Toon` additionally exposes `_NormalMap` and `_NormalScale`.

`PureBase/PBR` and `PureBase/Hybrid` expose the same normal-map properties plus `_Metallic` and `_Roughness`. Their public property declarations are byte-identical. Roughness is clamped from `0.002` to `1`.

## Shader-Core integration

The shared standard phase ABI is executed in this order:

`morph`, `postvertex`, `base`, `light`, `customlight`, `modifylight`, `shade`, `reflection`, `add`, `postpixel`

`Meta` is a dedicated pass and does not execute the standard phase sequence.

- `ForwardBase` owns the normal surface and lighting result.
- `ForwardAdd` contributes additional direct light only and uses black fog semantics.
- `ShadowCaster` and `Meta` preserve the fixed Cutout coverage contract.
- PBR and Hybrid evaluate Unity Standard indirect GI and reflection probes in `ForwardBase`. Their `ForwardAdd` passes do not duplicate indirect lighting.

Optional visual features belong in separate Shader-Core modules. Pure Base does not include rim lighting, MatCap, decals, detail textures, emission, dissolve, distance fade, parallax, hair or anisotropic specular, clear coat, glitter, or platform-specific integrations.

## Release preparation and publication

`package.json` is the sole release identity and version declaration.

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
