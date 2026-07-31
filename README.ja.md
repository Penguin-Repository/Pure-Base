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

言語: [English](README.md)

Pure Base は Shader-Core 向けに、Built-in Render Pipeline 用の最小限の base shader を4つ提供します。各 shader は optional module なしで独立して使用できます。

> [!Important]
> Pure Base は Shader-Core、NonToon、lilToon から独立しており、かつそれらとは無関係な非公式プロジェクトです。
>
> Pure Base と Penguin は金銭的支援を受け付けていません。受け付けるのは issue、pull request、code、patch のみです。
>
> Pure Base の開発には Gen AI (LLM) が使用されています。

## 要件

- Built-in Render Pipeline のみです。URP はサポートされません。
- パッケージは正確な依存関係 `jp.lilxyzw.shadercore` `0.1.9` を要求します。
- Pure Base は将来の `0.1.x` release を自動的には許可しません。Shader-Core upstream は `0.x` release 間の互換性を宣言しておらず、importer、ProjectSettings、method-shape contract は変更に敏感です。
- integration harness は Unity `2022.3.22f1` に固定され、test execution には D3D11 を強制します。
- Material transparency と transparent blending はサポートされません。すべての product shader は固定の Cutout coverage を使用します。

one-time migration の開始状態は、target version に既存の tag または GitHub release がない fresh-release precondition の下で、`package.json` の package manifest が `jp.penguin.purebase` version `0.0.0`、`update_trigger.json` の選択 version が `0.1.0-beta.1`、`vpm-yanks.json` の `versions` policy が空です。release version の選択元は `update_trigger.json` です。manual の Release workflow はその exact SemVer を検証し、`package.json` に書き込み、同じ version を tag にして package を publish します。stable version と prerelease version の両方をサポートします。package-owned validation の source of truth は `Packages/jp.penguin.purebase/Tests` です。

release ZIP には `Tests/**` と test-only の `*.scmodule` files は含まれません。追跡対象の `.scmodule` files は test fixtures であり、package-owned の `Tests/**` fixture boundary 内でのみ許可されます。

## Release と VPM の公開状態

Release workflow は stable-only ではありません。`0.1.0-beta.1` のような version は GitHub prerelease として publish され、`0.1.0` は stable として publish されます。VPM client での prerelease の表示は client の実装に依存するため、すべての VCC client が同じように prerelease を非表示または表示するとは限りません。

`vpm-yanks.json` は VPM repository の desired-state policy です。version key が存在する version は **Yank**、key を削除した version は **Unyank** になります。reason value は public な運用情報であり、secret を渡すための channel ではありません。secret、credential、個人情報、その他の非公開情報を記載しないでください。

literal `master` branch の `vpm-yanks.json` 変更で synchronization workflow が起動します。dispatch が stale になった場合や receiver outage の復旧後は、`master` から manual に再実行できます。workflow は current commit の policy を dispatch 前に検証し、固定された `sync-vpm-yanks` event だけを送信します。任意の source path や branch は受け付けません。manual replay では current policy commit が source of truth になります。

initial Yank rollout には gate があります。VPM receiver の準備ができ、target `0.1.0-beta.1` release が VPM feed に登録されたことを確認するまで policy は空のままにしてください。両方を確認した後は、最初の prerelease である `0.1.0-beta.1` を end-to-end の Yank/Unyank test のために policy に追加できます。空の policy は no-op の desired state であり、target feed に release が存在する前に version を追加しないでください。feed と receiver の更新には eventual consistency があるため、stale または早すぎる dispatch は listing を変更せず fail closed します。反映後に `master` から current policy commit で retry してください。ALCOM の prerelease と package feed の挙動は実装依存であり、他の VCC client については保証されません。

## シェーダーパス

| シェーダーパス | モデル |
| --- | --- |
| `PureBase/Unlit` | lighting に依存しない base color output |
| `PureBase/Toon` | ambient と lightmap をサポートする binary direct diffuse |
| `PureBase/PBR` | Unity Standard の indirect GI と reflection probes を使用する continuous metallic BRDF |
| `PureBase/Hybrid` | PBR specular と IBL path を使用する Toon-style binary direct diffuse |

すべての shader は `RenderType=TransparentCutout` を使用し、`AlphaTest` queue を持ち、正確に次の4つの pass を公開します：`ForwardBase`、`ForwardAdd`、`ShadowCaster`、`Meta`。

## 公開プロパティ

4つすべての shader は、次の共通 property を公開します：

`_BaseTexture`, `_BaseColor`, `_SharedMask`, `_SharedGradients`, `_Cutoff`, `_Cull`

`PureBase/Toon` はさらに `_NormalMap` と `_NormalScale` を公開します。`PureBase/PBR` と `PureBase/Hybrid` は同じ追加 property に加えて、`_Metallic` と `_Roughness` を公開します。PBR と Hybrid の property declarations は byte-identical です。Roughness は `0.002` から `1` までに clamp されます。

完全な pass と property の contract は [Pure Base shader contract](Docs/pure-base-shader-contract.md) に記載されています。

## Shader-Core 連携

共有される標準 phase ABI は次の順序です：

`morph`, `postvertex`, `base`, `light`, `customlight`, `modifylight`, `shade`, `reflection`, `add`, `postpixel`

`Meta` は専用 pass であり、標準 phase sequence を実行しません。

- `ForwardBase` は通常の surface と lighting result を担います。
- `ForwardAdd` は additional direct light のみを追加し、black fog semantics を使用します。
- `ShadowCaster` と `Meta` は固定の Cutout coverage contract を維持します。
- PBR と Hybrid の `ForwardBase` は Unity Standard の indirect GI と reflection-probe evaluation を担います。それらの `ForwardAdd` pass は indirect lighting を重複させません。

Optional visual features は separate Shader-Core modules に属します。Pure Base には rim lighting、MatCap、decals、detail textures、emission、dissolve、distance fade、parallax、hair または anisotropic specular、clear coat、glitter、platform-specific integrations は含まれません。

## 検証レーン

package-owned persistent validation lanes は `Tests/` の下で定義されています。

- `Tests/Run-PureBaseRegression.ps1 -Mode Daily` は read-only の Daily lane です。`PureBase.Tests.Daily` EditMode assembly のみを実行し、実行前後に project settings と追跡対象 package tree を保護します。
- `Tests/Run-PureBaseRegression.ps1 -Mode Initialize` は、固定された Shader-Core test hosts 用の別個の write-capable setup lane です。Daily の一部ではありません。
- Fixture baking と canonical baseline regeneration は、Daily とは別の明示的な write-capable operations です。Daily は `Tests/Baselines/birp-d3d11-2022.3.22f1.json` を読み取り、作成または置換することはありません。
- `Tests/Release/Run-PureBaseReleaseValidation.ps1` は、使い捨ての外部 consumer directory 1つで release ZIP を build と validate します。その cold resets は consumer の `Library` だけを削除します。runner は残りの immutable consumer inputs を検証し、最後に `-KeepConsumer` が使用されていない限り consumer directory を削除します。

## 検証

final disposable-project matrix は `62/62` を Unity `2022.3.22f1` と D3D11 の下で pass しました。これは module-free imports、all ten standard external phase probes、PBR と Hybrid の finite/reflection/`ForwardAdd`/specular behavior、Unlit と Toon の regressions、Meta と shadow checks を含む固定 validation-scene bake、そして 56 Built-in Render Pipeline variants を対象とします。

validation result は dynamic lightmap を `NOT_DETERMINISTIC_IN_BATCH_EDITMODE` と記録しています。これは batch EditMode harness が deterministic な dynamic-lightmap binding path を提供しないことを意味します。verified runtime dynamic-lightmap rendering として読み取ってはなりません。

Release-boundary checks も pass しました：

- release ZIP は `Tests/**` と test-only の `*.scmodule` files を除外します。
- 追跡対象の `.scmodule` files は `Tests/**` fixture boundary 内に限定されています。
- package 内に `Assets/PureBase.Tests` はありません。
- URP dependency はありません。
- PBR と Hybrid の public property ABI は byte-identical です。
- `_Emission`、`_Rim`、`_MatCap`、`_ClearCoat` は存在しません。

package-owned test lanes とその write boundaries は [`Tests/README.md`](Tests/README.md) に記載されています。
