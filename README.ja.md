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

Pure Base は、Shader-Core で使える4種類の基本シェーダーをまとめた Unity 向けパッケージです。

複雑な機能を最初から大量に備えるのではなく、必要な機能を Shader-Core の追加モジュールで組み合わせて使うための、軽くて分かりやすい土台を目指しています。

> [!IMPORTANT]
> Pure Base は、Shader-Core、NonToon、lilToon とは別に作られている非公式プロジェクトです。
>
> 開発には生成AIを使用しています。

## できること

Pure Base には、用途の異なる4つのシェーダーが含まれています。

| シェーダー | 向いている用途 |
| --- | --- |
| `PureBase/Unlit` | 周囲の明るさに影響されない表示 |
| `PureBase/Toon` | 明暗をはっきり分けたアニメ調の表示 |
| `PureBase/PBR` | 金属感や粗さを使った標準的な質感表現 |
| `PureBase/Hybrid` | アニメ調の明暗と物理ベースの反射を組み合わせた表示 |

すべてのシェーダーは、追加モジュールなしでも単独で使用できます。

## 対応環境

- Unity 2022.3
- Built-in Render Pipeline
- Shader-Core 0.1.9

URPと半透明のマテリアルには対応していません。透明部分は切り抜き方式で表示します。

## 導入方法

VRChat Creator Companion、ALCOM、またはVPMに対応した管理ソフトから導入できます。

### 1. 配布元を追加する

次のボタンを開き、Penguin VPM Repository を追加してください。

[Penguin VPM Repository を追加する](vcc://vpm/addRepo?url=https://raw.githubusercontent.com/Penguin-Repository/VPM-Repository/refs/heads/master/vpm.json)

ボタンが動作しない場合は、管理ソフトの「リポジトリを追加」画面へ次のURLを貼り付けてください。

```text
https://raw.githubusercontent.com/Penguin-Repository/VPM-Repository/refs/heads/master/vpm.json
```

Shader-Core の配布元をまだ追加していない場合は、次のURLも追加してください。

```text
https://lilxyzw.github.io/vpm-repos/vpm.json
```

### 2. プロジェクトへ追加する

1. 使用するUnityプロジェクトを管理ソフトで開きます。
2. パッケージ一覧から `PureBase` を探します。
3. 追加する版を選び、プロジェクトへ導入します。
4. Shader-Core 0.1.9 が一緒に導入されることを確認します。

現在は開発版のため、管理ソフトの設定によっては一覧に表示されない場合があります。その場合は、開発版やプレリリースを表示する設定を有効にしてください。

## 基本的な使い方

1. Unityで新しいマテリアルを作成します。
2. マテリアルのシェーダーから `PureBase` を選びます。
3. 用途に合わせて `Unlit`、`Toon`、`PBR`、`Hybrid` のいずれかを選びます。
4. 基本色やテクスチャなどを設定します。
5. 必要に応じて Shader-Core の追加モジュールを組み合わせます。

最初に迷った場合は、アニメ調なら `Toon`、一般的な質感なら `PBR` が分かりやすい選択です。

## 注意点

- Pure Base 本体は、できるだけ小さく保つ方針です。
- リムライト、MatCap、発光、ディゾルブなどの追加表現は、別の Shader-Core モジュールで補う想定です。
- 正式版ではない版では、仕様や使い方が変更される可能性があります。
- 不具合を報告する際は、使用したUnity、Pure Base、Shader-Coreの版を記載してください。

## 詳しい資料

一般的な利用では、このREADMEだけで導入と基本操作を始められます。

実装仕様、公開手順、検証方法などの開発者向け情報は、[技術資料](Docs/technical-information.ja.md)にまとめています。

## ライセンスと支援について

Pure Base は Apache License 2.0 で公開されています。詳しくは [LICENSE](LICENSE) を確認してください。

Pure Base と Penguin は金銭的な支援を受け付けていません。不具合報告、改善案、コードの修正などによる協力を歓迎します。
