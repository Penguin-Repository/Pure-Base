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

# Pure-Base

Pure-Base provides four minimal Built-in Render Pipeline base shaders for Shader-Core. Each shader is independently usable without an optional module.

## Requirements

- Built-in Render Pipeline only. URP is not supported.
- The package declares the exact dependency `jp.lilxyzw.shadercore` `0.1.5`.
- The integration harness is fixed to Unity `2022.3.22f1` and forces D3D11 for test execution.
- Material transparency and transparent blending are not supported. All product shaders use fixed Cutout coverage.

The package metadata identifies this package as `jp.penguin.purebase` version `0.1.0`. The package-owned validation source of truth is `Packages/jp.penguin.purebase/Tests`.

The release ZIP excludes `Tests/**` and test-only `*.scmodule` files. Tracked `.scmodule` files are test fixtures and are allowed only within the package-owned `Tests/**` fixture boundary.

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

The complete pass and property contract is documented in [Pure-Base shader contract](../../Docs/pure-base-shader-contract.md).

## Shader-Core Integration

The shared standard phase ABI is, in order:

`morph`, `postvertex`, `base`, `light`, `customlight`, `modifylight`, `shade`, `reflection`, `add`, `postpixel`

`Meta` is a dedicated pass and does not execute the standard phase sequence.

- `ForwardBase` owns the normal surface and lighting result.
- `ForwardAdd` contributes additional direct light only and uses black fog semantics.
- `ShadowCaster` and `Meta` preserve the fixed Cutout coverage contract.
- PBR and Hybrid `ForwardBase` own Unity Standard indirect GI and reflection-probe evaluation. Their `ForwardAdd` passes do not duplicate indirect lighting.

Optional visual features belong in separate Shader-Core modules. Pure-Base does not include rim lighting, MatCap, decals, detail textures, emission, dissolve, distance fade, parallax, hair or anisotropic specular, clear coat, glitter, or platform-specific integrations.

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
