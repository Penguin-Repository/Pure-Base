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

# Pure Base

Language: [日本語](README.ja.md)

Pure Base provides four minimal Built-in Render Pipeline base shaders for Shader-Core. Each shader is independently usable without an optional module.

> [!Important]
> Pure Base is an unofficial project that is independent of and unrelated to Shader-Core, NonToon, and lilToon.
>
> Pure Base and Penguin do not accept any financial support. Only issues, pull requests, code, and patches are accepted.
>
> Gen AI (LLM) is used in the development of Pure Base.

## Requirements

- Built-in Render Pipeline only. URP is not supported.
- The package requires the exact dependency `jp.lilxyzw.shadercore` `0.1.9`.
- Pure Base does not automatically allow future `0.1.x` releases. Shader-Core upstream has not declared compatibility across `0.x` releases, and importer, ProjectSettings, and method-shape contracts are sensitive.
- The integration harness is fixed to Unity `2022.3.22f1` and forces D3D11 for test execution.
- Material transparency and transparent blending are not supported. All product shaders use fixed Cutout coverage.

## Release preparation and publication

`package.json` is the sole release identity and version declaration. The `version` input to the
manual `Release` workflow confirms the exact version in the checked-out package; it does not write
or commit a version. Prepare and commit the package changes, run the hosted `Release validation`
workflow for that exact commit SHA, then run `Release` from the same branch and SHA. Validation
produces a deterministic Store-mode ZIP, a lowercase SHA-256 sidecar, and a schema-1
`release-validation.json` provenance manifest in one artifact. `Release` selects the latest
matching validation run and attempt, verifies the unexpired artifact and its digest, and publishes
the downloaded ZIP without rebuilding it.

An expired or unsuccessful latest matching validation run requires a new validation run for the
same SHA. Release does not use an older successful run or rebuild the package. A fresh release
requires no existing tag or GitHub Release. Resume requires an exact annotated tag plus a matching
draft or published Release at the same SHA; a tag-only failure needs operator investigation. Draft
resume is limited to badge repair and missing-asset upload or exact-digest reuse. Published resume
requires the release branch to remain at the same SHA and does not change the immutable Release
body or assets, including a legacy or missing badge. The optional `preflight_only=true` input performs these hosted checks without
creating a tag, Release, asset, or VPM dispatch, and should be run before the first production
publication. The Release path does not run Unity, rebuild the ZIP, write or commit `package.json`,
or push the release branch. The package-owned validation source of truth is
`Packages/jp.penguin.purebase/Tests`.

The release ZIP excludes `Tests/**` and test-only `*.scmodule` files. Tracked `.scmodule` files are test fixtures and are allowed only within the package-owned `Tests/**` fixture boundary.

## Release and VPM availability

The Release workflow is not stable-only. A prerelease is published as a GitHub prerelease, while a stable version is published as stable. Prerelease visibility in a VPM client depends on that client's behavior; this package does not promise that every VCC client hides or displays prereleases in the same way.

`vpm-yanks.json` is a desired-state policy for the VPM repository. A version key means **Yank** that version, and removing the key means **Unyank** it. The reason value is public operational documentation, not a secret channel. Never put secrets, credentials, personal data, or other private information in it.

Changes to `vpm-yanks.json` on the literal `master` branch trigger the synchronization workflow. Operators can also run it manually from `master` when a dispatch is stale or a receiver outage has been recovered. The workflow validates the policy at the current commit before sending a fixed `sync-vpm-yanks` event; it does not accept arbitrary source paths or branches. Manual replay uses the current policy commit as the source of truth.

The initial Yank rollout is gated: keep the policy empty until the VPM receiver is ready and the
confirmed release version is registered in the VPM feed. Once both are confirmed, the policy may add
that released version for the end-to-end Yank/Unyank test as a separate approved policy update. An
empty policy is a no-op desired state, and no version may be added before its release exists in the
target feed. Feed and receiver updates are eventually consistent; a stale or premature dispatch
fails closed without changing the listing, so retry from `master` with the current policy commit
after propagation. The VPM receiver, VPM repository, and existing `update-vpm` payload contract
remain unchanged and are outside the release pipeline. ALCOM prerelease and package-feed behavior
is implementation-specific and is not guaranteed for other VCC clients.

## Shader Paths

| Shader path | Model |
| --- | --- |
| `PureBase/Unlit` | Lighting-independent base color output |
| `PureBase/Toon` | Binary direct diffuse with ambient and lightmap support |
| `PureBase/PBR` | Continuous metallic BRDF with Unity Standard indirect GI and reflection probes |
| `PureBase/Hybrid` | Toon-style binary direct diffuse with the PBR specular and IBL path |

Every shader uses `RenderType=TransparentCutout` with the `AlphaTest` queue and exposes exactly these four passes: `ForwardBase`, `ForwardAdd`, `ShadowCaster`, and `Meta`.

## Public Properties

All four shaders expose the following common properties:

`_BaseTexture`, `_BaseColor`, `_SharedMask`, `_SharedGradients`, `_Cutoff`, `_Cull`

`PureBase/Toon` additionally exposes `_NormalMap` and `_NormalScale`. `PureBase/PBR` and `PureBase/Hybrid` expose the same additional properties, plus `_Metallic` and `_Roughness`. The PBR and Hybrid property declarations are byte-identical. Roughness is clamped from `0.002` to `1`.

The complete pass and property contract is documented in [Pure Base shader contract](Docs/pure-base-shader-contract.md).

## Shader-Core Integration

The shared standard phase ABI is, in order:

`morph`, `postvertex`, `base`, `light`, `customlight`, `modifylight`, `shade`, `reflection`, `add`, `postpixel`

`Meta` is a dedicated pass and does not execute the standard phase sequence.

- `ForwardBase` owns the normal surface and lighting result.
- `ForwardAdd` contributes additional direct light only and uses black fog semantics.
- `ShadowCaster` and `Meta` preserve the fixed Cutout coverage contract.
- PBR and Hybrid `ForwardBase` own Unity Standard indirect GI and reflection-probe evaluation. Their `ForwardAdd` passes do not duplicate indirect lighting.

Optional visual features belong in separate Shader-Core modules. Pure Base does not include rim lighting, MatCap, decals, detail textures, emission, dissolve, distance fade, parallax, hair or anisotropic specular, clear coat, glitter, or platform-specific integrations.

## Validation Lanes

The package-owned persistent validation lanes are defined under `Tests/`.

- `Tests/Run-PureBaseRegression.ps1 -Mode Daily` is the read-only Daily lane. It runs only the `PureBase.Tests.Daily` EditMode assembly and protects the project settings and tracked package tree before and after execution.
- `Tests/Run-PureBaseRegression.ps1 -Mode Initialize` is a separate write-capable setup lane for fixed Shader-Core test hosts. It is not part of Daily.
- Fixture baking and canonical baseline regeneration are explicit write-capable operations separate from Daily. Daily reads `Tests/Baselines/birp-d3d11-2022.3.22f1.json` and never creates or replaces it.
- `Tests/Release/Run-PureBaseReleaseValidation.ps1` builds and validates the release ZIP in one disposable external consumer directory. Its cold resets remove only that consumer's `Library`; the runner verifies the remaining immutable consumer inputs and removes the consumer directory at the end unless `-KeepConsumer` is used.

## Validation

The final disposable-project matrix passed `62/62` under Unity `2022.3.22f1` with D3D11. It covers module-free imports, all ten standard external phase probes, PBR and Hybrid finite/reflection/`ForwardAdd`/specular behavior, Unlit and Toon regressions, a fixed validation-scene bake with Meta and shadow checks, and 56 Built-in Render Pipeline variants.

The validation result records dynamic lightmap as `NOT_DETERMINISTIC_IN_BATCH_EDITMODE`. This means the batch EditMode harness does not provide a deterministic dynamic-lightmap binding path; it must not be read as verified runtime dynamic-lightmap rendering.

Release-boundary checks also passed:

- release ZIP excludes `Tests/**` and test-only `*.scmodule` files;
- tracked `.scmodule` files are confined to the `Tests/**` fixture boundary;
- no `Assets/PureBase.Tests` inside the package;
- no URP dependency;
- PBR and Hybrid public property ABI byte-identical;
- `_Emission`, `_Rim`, `_MatCap`, and `_ClearCoat` absent.

The package-owned test lanes and their write boundaries are described in [`Tests/README.md`](Tests/README.md).
