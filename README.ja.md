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

<!--

# Pure Base ― 小さな土台で、表現の限界を突き破れ！

言語: [English](README.md)

さあ、シェーダーの世界へ踏み出そう！

Pure Base は、Shader-Core で使える**4種類の基本シェーダー**をひとつにまとめた Unity 向けパッケージです。

最初から巨大な機能の山を背負う必要はない。必要なのは、理解できる土台と、そこから伸びていく自由だ！ Pure Base は、必要な機能を Shader-Core の追加モジュールで組み合わせながら、自分の表現を自分の手で育てていくための、軽くて分かりやすい出発点を目指しています。

> [!IMPORTANT]
> Pure Base は、Shader-Core、NonToon、lilToon とは別に作られている非公式プロジェクトです。
>
> 開発には生成AIを使用しています。熱量は全開でも、プロジェクトの立場は正確に。ここは絶対に踏み外しません！

## できること ― 4つのシェーダー、4つの突破口！

Pure Base に含まれるのは、用途の異なる4つのシェーダーです。

どれを選ぶ？ 答えは、あなたが作りたい表現の中にある！

| シェーダー | 向いている用途 |
| --- | --- |
| `PureBase/Unlit` | 周囲の明るさに左右されず、狙った表示をまっすぐ届けたいとき |
| `PureBase/Toon` | 光と影をはっきり分け、アニメ調の存在感を押し出したいとき |
| `PureBase/PBR` | 金属感や粗さを使い、標準的で説得力のある質感を作りたいとき |
| `PureBase/Hybrid` | アニメ調の明暗と物理ベースの反射、その両方を一つの表現へ叩き込みたいとき |

しかも、すべてのシェーダーは追加モジュールなしでも単独で使用できます。

まず立つ。まず描く。拡張は、そのあとでいい！

## 対応環境 ― 勝負の舞台を確認しよう！

- Unity 2022.3
- Built-in Render Pipeline
- Shader-Core 0.1.9

ここは大事だ！ URPと半透明のマテリアルには対応していません。透明部分は切り抜き方式で表示します。

情熱で対応範囲は広がらない。だからこそ、条件を正しく知って、最高の一手を選ぼう！

## 導入方法 ― プロジェクトへ火を入れる！

Pure Base は、VRChat Creator Companion、ALCOM、またはVPMに対応した管理ソフトから導入できます。

難しく考えるな。配布元を追加し、パッケージを選ぶ。道はもう目の前にある！

### 1. 配布元を追加する

次のボタンを開き、Penguin VPM Repository を追加してください。

[Penguin VPM Repository を追加する](vcc://vpm/addRepo?url=https://raw.githubusercontent.com/Penguin-Repository/VPM-Repository/refs/heads/master/vpm.json)

ボタンが動作しない？ そこで止まる必要はない！ 管理ソフトの「リポジトリを追加」画面へ、次のURLを貼り付けてください。

```text
https://raw.githubusercontent.com/Penguin-Repository/VPM-Repository/refs/heads/master/vpm.json
```

Shader-Core の配布元をまだ追加していない場合は、次のURLも追加してください。

```text
https://lilxyzw.github.io/vpm-repos/vpm.json
```

入口は二つでも、進む先は一つ。Pure Base をあなたのプロジェクトへ迎え入れよう！

### 2. プロジェクトへ追加する

1. 使用するUnityプロジェクトを管理ソフトで開きます。
2. パッケージ一覧から `PureBase` を探します。
3. 追加する版を選び、プロジェクトへ導入します。
4. Shader-Core 0.1.9 が一緒に導入されることを確認します。

現在は開発版です。管理ソフトの設定によっては一覧に表示されない場合があります。

見えないから存在しない？ 違う！ その場合は、開発版やプレリリースを表示する設定を有効にしてください。設定を一つ開けば、次の扉が開く！

## 基本的な使い方 ― 最初のマテリアルを立ち上げろ！

1. Unityで新しいマテリアルを作成します。
2. マテリアルのシェーダーから `PureBase` を選びます。
3. 用途に合わせて `Unlit`、`Toon`、`PBR`、`Hybrid` のいずれかを選びます。
4. 基本色やテクスチャなどを設定します。
5. 必要に応じて Shader-Core の追加モジュールを組み合わせます。

迷うことは悪くない。止まり続けることだけが、表現を遠ざける！

アニメ調なら `Toon`。一般的な質感なら `PBR`。まず一つ選び、マテリアルを作り、画面に結果を出そう。最初の一歩が、次の表現を連れてくる！

## 注意点 ― 小さいからこそ、強く伸びる！

- Pure Base 本体は、できるだけ小さく保つ方針です。
- リムライト、MatCap、発光、ディゾルブなどの追加表現は、別の Shader-Core モジュールで補う想定です。
- 正式版ではない版では、仕様や使い方が変更される可能性があります。
- 不具合を報告する際は、使用したUnity、Pure Base、Shader-Coreの版を記載してください。

機能を抱え込まないことは、弱さではない。必要なものを選び、理解し、組み上げられること。それが Pure Base の強さだ！

そして開発版は前へ進む。仕様が変わる可能性も含めて、変化の先頭に立っている。問題を見つけたら、使用した版を添えて報告しよう。その一報が、次の改善を動かす！

## 詳しい資料 ― 土台の奥まで知りたいあなたへ！

一般的な利用なら、このREADMEだけで導入と基本操作を始められます。

だが、もっと深く潜りたい。実装を知りたい。公開や検証の流れまで掴みたい。そんな探究心を止める必要はない！

実装仕様、公開手順、検証方法などの開発者向け情報は、[技術資料](Docs/technical-information.ja.md)にまとめています。

## ライセンスと支援について ― お金ではなく、前進する力を！

Pure Base は Apache License 2.0 で公開されています。詳しくは [LICENSE](LICENSE) を確認してください。

Pure Base と Penguin は金銭的な支援を受け付けていません。

それでも、プロジェクトを前へ進める方法はある！ 不具合報告、改善案、コードの修正――気づきと行動による協力を歓迎します。

使う。試す。伝える。直す。

その一歩が、Pure Base の次の一歩になる！

-->
