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

# Pure Base 技術資料

言語: [English](technical-information.md)

この文書には、一般の利用者向けの [README](../README.ja.md) から分離した実装、互換性、公開、検証に関する情報をまとめています。

Pure Base は Shader-Core を動かすための最小構成の土台です。多機能なマテリアル環境を目指すものではありません。

## 対応環境と互換性

- Unity `2022.3` が必要です。動作検証には Unity `2022.3.22f1` を使用しています。
- Built-in Render Pipeline のみ対応しています。URP には対応していません。
- `jp.lilxyzw.shadercore` `0.1.9` が必要です。
- 将来の Shader-Core `0.1.x` を自動では許可しません。Shader-Core は `0.x` 間の互換性を保証しておらず、読み込み処理、プロジェクト設定、関数の形が変わる可能性があります。
- 検証では D3D11 を使用します。
- Opaque、Cutout、Transparent の描画モードに対応しています。初期状態は Cutout です。URP には対応していません。

## シェーダー名

| シェーダー名 | 特徴 |
| --- | --- |
| `PureBase/Unlit` | ライティングの影響を受けない基本色表示 |
| `PureBase/Toon` | 直接光とSHから安定した方向を作り、明暗2帯のSHとライトマップを使うトゥーン表示 |
| `PureBase/PBR` | Unity 標準に近い物理ベース表示 |
| `PureBase/Hybrid` | トゥーンの拡散反射と物理ベースの反射表現を組み合わせた表示 |

各シェーダーは追加モジュールなしでも単独で使用できます。

公開されている描画処理や項目名の正確な仕様は、[Pure Base シェーダー契約](pure-base-shader-contract.md)に記載しています。

## 描画処理と公開項目

すべてのシェーダーのソースには、次の4つの描画処理が残ります。実際の描画状態は描画モードで決まり、Transparent では `ShadowCaster` と `Meta` が無効になります。

- `ForwardBase`
- `ForwardAdd`
- `ShadowCaster`
- `Meta`

### 描画モード ABI

`_RenderingMode` は `SC_uint` を基にした ShaderLab の `Integer` です。値は `Opaque=0`、`Cutout=1`（初期値）、`Transparent=2` です。

| モード | 実際の描画状態 |
| --- | --- |
| Opaque | `RenderType=Opaque`、キュー `2000`、ブレンド `One Zero`、`ZWrite 1`。切り抜きとブレンドを行わず、ライティングの寄与を有効にします。 |
| Cutout | 保存されているキューの上書きを `-1` に戻し、`RenderType=TransparentCutout` と `AlphaTest` キュー `2450` に解決します。モードキーワードを使わず、被覆を切り抜き、ライティングの寄与を有効にします。 |
| Transparent | `RenderType=Transparent`、キュー `3000`、ベースのブレンド `SrcAlpha OneMinusSrcAlpha`、追加ライトのブレンド `SrcAlpha One`、`ZWrite 0`。`ShadowCaster` と `Meta` は無効になります。 |

キーワードを使わない状態が Cutout です。Opaque と Transparent ではローカルな描画モードキーワードだけを使用します。`postpixel` が最後に出力するアルファは、`ForwardBase` と `ForwardAdd` のソースアルファを決めます。

エディターから明示的に適用する操作は `PureBaseMaterialRenderingMode.Apply(Material)` です。選択中のマテリアルには `Assets/PureBase/Resync Rendering Mode` を使えます。Inspector を開いたり更新したりするだけでは、旧形式のマテリアルを移行したり変更済みにしたりしません。実行時の切り替えは保証しません。モード変更または Resync を明示的に行うと標準キューをリセットして派生状態を同期します。ユーザーが設定したカスタムキューは、次にモードを明示的に編集または Resync するまで維持されます。

共通して公開する項目は次のとおりです。

`_RenderingMode`（`SC_uint` を基にした ShaderLab の `Integer`、`Opaque=0`、`Cutout=1`（初期値）、`Transparent=2`）, `_BaseTexture`, `_BaseColor`, `_SharedMask`, `_SharedGradients`, `_Cutoff`, `_Cull`

`PureBase/Toon` は、追加で `_NormalMap` と `_NormalScale` を公開します。

`PureBase/PBR` と `PureBase/Hybrid` は、法線マップ用の項目に加えて `_Metallic` と `_Roughness` を公開します。両者の公開項目定義は完全に同一です。粗さは `0.002` から `1` の範囲に制限されます。

### ステンシル ABI

4つの製品シェーダーは、Float互換の `SC_float` ABI を通じて、次のステンシル項目を公開します。列挙型に対応する項目も `SC_float` として宣言されます。

| 項目 | UI の仕様 | 初期値 |
| --- | --- | ---: |
| `_StencilRef` | 整数範囲 `0-255` | `0` |
| `_StencilReadMask` | 整数範囲 `0-255` | `255` |
| `_StencilWriteMask` | 整数範囲 `0-255` | `255` |
| `_StencilComp` | `UnityEngine.Rendering.CompareFunction` | `8`（`Always`） |
| `_StencilPass` | `UnityEngine.Rendering.StencilOp` | `0`（`Keep`） |
| `_StencilFail` | `UnityEngine.Rendering.StencilOp` | `0`（`Keep`） |
| `_StencilZFail` | `UnityEngine.Rendering.StencilOp` | `0`（`Keep`） |

`ForwardBase` は7項目すべてを適用します。`ForwardAdd` は `ForwardBase` 後のステンシル値を `_StencilRef`、`_StencilReadMask`、`_StencilComp` だけで比較します。`WriteMask` は `0`、`Pass`、`Fail`、`ZFail` は `Keep` に固定されるため、追加ライトがステンシルを書き換えることはありません。`ShadowCaster` と `Meta` ではカメラ用のステンシル方針を適用しません。

初期値の `Always` と `Keep` の組み合わせは、ステンシルに対する何もしない状態です。保存済みのステンシル項目を持たない旧形式のマテリアルも描画を継続し、ステンシルを書き換えません。`ForwardBase` で `Replace`、`Incr`、`Decr`、`Invert` などを指定すると、`ForwardAdd` が変更後の値を比較するため、追加ライトは独立して通過または拒否されることがあります。`ForwardBase` 前の値との比較を維持する仕様ではありません。

ステンシルの追加によって、ステンシル専用のキーワード、シェーダーバリアント、パス、パッケージ依存関係は増えません。描画モードの変更と Resync は描画モードの状態を同期しますが、ユーザーが設定した7つのステンシル項目は保持します。

## Shader-Core との連携

共通の処理差し込み位置は、次の順で実行されます。

`morph`, `postvertex`, `base`, `light`, `customlight`, `modifylight`, `shade`, `reflection`, `add`, `postpixel`

`Meta` は専用の描画処理であり、この共通順序は実行しません。

- `ForwardBase` は通常の表面とライティング結果を担当します。
- `ForwardAdd` は追加ライトの直接光だけを加算します。
- Cutout では、`ForwardBase`、`ForwardAdd`、有効な `ShadowCaster` が `base` 後のモジュール調整済み `sd.albedoAlpha.a` から被覆を決定します。Opaque は切り抜きを行わず、Transparent は深度を書き込まずにアルファブレンドし、`ShadowCaster` と `Meta` を無効にします。
- `postpixel` は色を変更できる最後の差し込み位置です。モジュールが変更した最後のアルファは、両フォワードパスでソースアルファとして使われます。製品パスのカラーマスクは固定されています。
- PBR と Hybrid は、Unity 標準の間接光と反射プローブを `ForwardBase` で計算します。`ForwardAdd` では間接光を重複して計算しません。

リムライト、MatCap、デカール、細部用テクスチャ、発光、ディゾルブ、距離によるフェード、視差表現、髪向け反射、クリアコート、グリッター、特定環境専用の連携などは、別の Shader-Core モジュールで追加する想定です。Pure Base 本体には含めません。

## Toon ライティングの境界とマテリアル互換性

追加モジュールなしの Toon ホストは、直接光を2値化する拡散反射、直接光の集計と SH から作る安定したシーン光方向、明るい帯と暗い帯に分けた SH、Shader-Core が渡すライトマップ入力、既存の `ForwardBase` と `ForwardAdd` の分離を担当します。設定できる影の帯・色・マスク、方向の上書き、ライティングの上限と下限、モノクロ・unlit 化の制御、その他の任意の演出的な制御は Shader-Core モジュールの担当です。

SRP またはプラットフォーム固有の連携、APV/LPPV、ライトボリューム、LTCGI、lilToon の無関係な効果は Pure Base ホストの対象外です。Toon は既存の Shader-Core ライトマップ集計を使い、焼き込み光をもう一度加算しません。`ForwardAdd` はこれまでどおり追加の直接光だけを加算します。

Hybrid は PBR の経路の中にある既存の2値化直接拡散反射の式をそのまま維持します。Toon の SH 帯は Hybrid のライティング方向へ渡しません。Hybrid は引き続き Unity Standard の間接 GI、反射プローブ、PBR の直接光方向、直接 GGX 鏡面反射を使用します。

この固定されたホスト動作によって、公開項目、キーワード、パス、バリアント、依存関係は増えません。公開プロパティ ABI は変わらないため、既存のマテリアルを移行する必要はなく、自動的にこの動作を受け取ります。

## 公開の準備と実行

`package.json` が、公開名と版番号を決める唯一の情報源です。

現在のパッケージ版は `0.2.0-beta.2` です。

手動の `Release` ワークフローへ渡す `version` は、すでにパッケージへ記載されている版番号と一致するかを確認するためだけに使われます。版番号の書き換えやコミットは行いません。

公開は次の順で行います。

1. パッケージの変更を準備してコミットします。
2. そのコミットに対して GitHub Actions の `Release validation` を実行します。
3. 同じブランチとコミットから `Release` を実行します。

検証では、同じ内容を再現できる配布用ZIP、SHA-256の照合用ファイル、`release-validation.json` を1つの成果物として作成します。`Release` は、条件に一致する最新の検証結果を選び、有効期限とハッシュ値を確認してから、そのZIPを作り直さずに公開します。

一致する最新の検証が失効または失敗している場合は、同じコミットで検証を再実行してください。古い成功結果を使ったり、公開時にパッケージを再構築したりはしません。

新規公開では、同じタグや GitHub Release が存在していてはいけません。途中から再開する場合は、同じコミットを指す注釈付きタグと、それに対応する下書きまたは公開済みの GitHub Release が必要です。タグだけが残っている場合は、自動修復せず人が確認します。

下書きからの再開で行えるのは、バッジの修復、足りない配布物の追加、または完全に同じハッシュ値を持つ配布物の再利用だけです。公開済みからの再開では、公開元ブランチが同じコミットを指している必要があります。公開本文や配布物は変更しません。

`preflight_only=true` を指定すると、タグ、GitHub Release、配布物、VPM通知を作らずに事前確認だけを行えます。初回の本番公開前に実行してください。

`Release` は Unity を起動せず、ZIPを作り直さず、`package.json` を書き換えず、コミットやブランチの送信も行いません。

パッケージとして導入された後の検証元は `Packages/jp.penguin.purebase/Tests` です。このリポジトリでは、同じ内容が [`Tests`](../Tests) にあります。

配布用ZIPには `Tests/**` と検証専用の `*.scmodule` を含めません。追跡対象の `.scmodule` は検証用の素材であり、`Tests/**` の中にだけ置けます。

## GitHub Release と VPM での公開状態

公開処理は、正式版と開発版の両方に対応しています。開発版は GitHub のプレリリース、正式版は通常のリリースとして公開されます。

VPMクライアントで開発版が表示されるかどうかは、そのクライアントの実装によります。すべてのVCC互換クライアントで同じ表示になることは保証しません。

`vpm-yanks.json` は、VPMリポジトリで非推奨扱いにする版を管理する方針ファイルです。版番号の項目がある版は非推奨扱いになり、項目を削除すると解除されます。理由は公開情報として扱われるため、秘密情報、認証情報、個人情報、その他の非公開情報を書かないでください。

`master` ブランチ上で `vpm-yanks.json` が変更されると、同期処理が実行されます。通知が古くなった場合や受信側の障害から復旧した場合は、`master` から手動で再実行できます。処理は現在のコミットにある方針を確認し、固定された `sync-vpm-yanks` 通知だけを送ります。任意のファイル位置やブランチは指定できません。

最初の非推奨化テストには制限があります。VPM受信側の準備が完了し、対象の公開版が一覧へ登録されたことを確認するまで、方針ファイルは空のままにしてください。その後、別途承認された変更として、公開済みの版を追加し、非推奨化と解除の一連の動作を確認できます。

空の方針は何も変更しません。対象の版が配布一覧に存在する前に追加してはいけません。配布一覧と受信側の反映には時間差があるため、古い通知や早すぎる通知は一覧を変更せず失敗します。反映後に、`master` の現在のコミットから再実行してください。

VPM受信側、VPMリポジトリ、既存の `update-vpm` 通知仕様は、公開処理の対象外です。ALCOMでの開発版や配布一覧の表示方法はALCOM固有であり、他のVCC互換クライアントと同じ動作は保証しません。

## 検証の種類

継続的に使う検証は [`Tests`](../Tests) にあります。

- `Tests/Run-PureBaseRegression.ps1 -Mode Daily` は、書き込みを行わない日常検証です。`PureBase.Tests.Daily` の EditMode 検証だけを実行し、前後でプロジェクト設定と追跡対象のパッケージ内容が変わっていないことを確認します。
- `Tests/Run-PureBaseRegression.ps1 -Mode Initialize` は、固定された Shader-Core 検証環境を準備するための、書き込みを伴う別処理です。日常検証には含まれません。
- 検証用素材の焼き込みと基準値の再作成は、日常検証とは別の明示的な書き込み処理です。日常検証は `Tests/Baselines/birp-d3d11-2022.3.22f1.json` を読むだけで、作成や置換は行いません。
- `Tests/Release/Run-PureBaseReleaseValidation.ps1` は、使い捨ての外部利用環境で配布用ZIPの作成と検証を行います。初期化時に削除するのは、その利用環境の `Library` だけです。残りの固定入力を確認し、`-KeepConsumer` が指定されていなければ最後に利用環境を削除します。

## 検証結果

最終的な使い捨てプロジェクトでの検証は、Unity `2022.3.22f1` と D3D11 の環境で `62/62` 成功しています。

検証対象には、追加モジュールなしでの読み込み、10種類すべての標準差し込み位置、PBR と Hybrid の値の有限性、反射、`ForwardAdd`、鏡面反射、Unlit と Toon の再発防止確認、Meta と影を含む固定検証シーンの焼き込み、Built-in Render Pipeline の56種類の組み合わせが含まれます。

動的ライトマップについては `NOT_DETERMINISTIC_IN_BATCH_EDITMODE` と記録されています。これは、EditMode の一括検証では動的ライトマップを毎回同じ条件で結び付けられないことを示しています。実行時の動的ライトマップ描画を検証済みという意味ではありません。

公開境界では、次の内容も確認しています。

- 配布用ZIPに `Tests/**` と検証専用の `*.scmodule` が含まれないこと
- 追跡対象の `.scmodule` が `Tests/**` の中だけにあること
- パッケージ内に `Assets/PureBase.Tests` がないこと
- URPへの依存がないこと
- PBR と Hybrid の公開項目定義が完全に同一であること
- `_Emission`、`_Rim`、`_MatCap`、`_ClearCoat` が存在しないこと

検証の実行方法と書き込み範囲は [`Tests/README.md`](../Tests/README.md) に、CIの担当範囲は [`.github/CI.md`](../.github/CI.md) に記載しています。
