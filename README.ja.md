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

![GitHub Total Downloads](https://img.shields.io/github/downloads/Penguin-Repository/Pure-Base/total?label=GitHub%20Release%20downloads)
![Downloads latest](https://img.shields.io/github/downloads/Penguin-Repository/Pure-Base/latest/total)

[![Automation tests](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/automation-tests.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/automation-tests.yml)
[![CodeQL](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/codeql.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/codeql.yml)
[![Daily](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/daily.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/daily.yml)

[![Release validation](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/release-validation.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/release-validation.yml)
[![Release](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/release.yml/badge.svg)](https://github.com/Penguin-Repository/Pure-Base/actions/workflows/release.yml)

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

---

Enjoy your Unity!

<!--

🔥🔥🔥 ここから先は、ソースを開いた者だけが目撃できる封印領域だ！ 🔥🔥🔥

# Pure Base ― 小さい土台を笑うな！ 巨大な表現は、いつだって最初の一歩から始まる！！

言語: [English](README.md)

おい！ そこのマテリアル！

まだ自分の可能性を「機能が足りない」の一言で閉じ込めていないか！？

Pure Base は、Shader-Core で使える**4種類の基本シェーダー**をひとつにまとめた Unity 向けパッケージだ！

「最初から機能を全部盛れば強い」？

違う！ 違うんだ！！

山盛りの機能に埋もれて、自分が何を使っているのか分からなくなったら、表現のハンドルを誰が握るんだ！？

Pure Base が目指すのは、必要な機能を Shader-Core の追加モジュールで組み合わせていくための、**軽い！ 分かる！ 伸ばせる！** 土台だ！

小さいから弱いんじゃない。

小さいから見える！
小さいから学べる！
小さいから、お前の手でどこまでも育てられるんだ！！

> [!IMPORTANT]
> Pure Base は、Shader-Core、NonToon、lilToon とは別に作られている非公式プロジェクトです。
>
> 開発には生成AIを使用しています。

ここは勢いで飛び越える場所じゃない！

非公式は非公式！ 別プロジェクトは別プロジェクト！ 生成AIを使っているなら、使っていると書く！

熱さとは事実を曲げることじゃない。
**事実を真正面から受け止めたうえで、それでも前へ進むことだ！！**

## できること ― 4人のシェーダー戦士、ここに集結！！

Pure Base に含まれるのは、用途の異なる4つのシェーダーだ！

たった4つ？

「たった」じゃない！

この4つが、お前の表現を立ち上げる四本柱だ！！

| シェーダー | 向いている用途 |
| --- | --- |
| `PureBase/Unlit` | 周囲の明るさに振り回されるな！ シーンライティングの影響を受けない表示を貫きたいとき |
| `PureBase/Toon` | 光か！？ 影か！？ 明暗をくっきり分け、アニメ調の魂を画面へ叩き込みたいとき |
| `PureBase/PBR` | 金属感を磨け！ 粗さにも胸を張れ！ 標準的で説得力のある質感を作りたいとき |
| `PureBase/Hybrid` | トゥーンか反射か、どちらか選べだと！？ 両方だ！！ アニメ調の明暗と物理ベースの反射を一つへ束ねたいとき |

しかも全員、追加モジュールなしで単独出場できる！

補欠じゃない！
土台だから未完成でもない！

まず一つ選べ！ マテリアルへ設定しろ！ 画面へ出せ！

話はそれからだ！！

## 対応環境 ― 情熱の前に、足元を確認しろ！！

- Unity 2022.3
- Built-in Render Pipeline
- Shader-Core 0.1.9

見たか、この三本柱を！

**Unity 2022.3！ Built-in Render Pipeline！ Shader-Core 0.1.9！**

ここが Pure Base の勝負する舞台だ！

URPは？

対応していない！！

半透明マテリアルは？

対応していない！！

透明部分はどうする！？

**切り抜き方式だ！！**

「気合いで半透明になりませんか」？

ならない！ 気合いは仕様を上書きしない！！

だから確認するんだ。対応環境を知ることは逃げじゃない。最短距離で成功へ向かうための助走だ！！

## 導入方法 ― 迷っている時間に、リポジトリは追加できる！！

Pure Base は、VRChat Creator Companion、ALCOM、またはVPMに対応した管理ソフトから導入できる！

難しい顔をするな！

やることは二つだ！

**配布元を追加する！ プロジェクトへ入れる！**

以上！！

### 1. 配布元を追加する ― URLは逃げない！ お前も逃げるな！！

次のボタンを開き、Penguin VPM Repository を追加してください。

[Penguin VPM Repository を追加する](vcc://vpm/addRepo?url=https://raw.githubusercontent.com/Penguin-Repository/VPM-Repository/refs/heads/master/vpm.json)

ボタンが動かない！？

いいぞ！ 問題が見えたなら、もう半分は解決している！！

管理ソフトの「リポジトリを追加」画面へ、次のURLを貼り付けろ！

```text
https://raw.githubusercontent.com/Penguin-Repository/VPM-Repository/refs/heads/master/vpm.json
```

貼ったか！？

まだ終わりじゃない！ Shader-Core の配布元を追加していないなら、次のURLも追加だ！！

```text
https://lilxyzw.github.io/vpm-repos/vpm.json
```

URLは長い！

だが道はまっすぐだ！！

コピー！ 貼り付け！ 追加！

一文字ずつ手打ちして己を試す必要はない！ 文明を使え！！

### 2. プロジェクトへ追加する ― 4ステップで火を入れろ！！

1. 使用するUnityプロジェクトを管理ソフトで開きます。
2. パッケージ一覧から `PureBase` を探します。
3. 追加する版を選び、プロジェクトへ導入します。
4. Shader-Core 0.1.9 が一緒に導入されることを確認します。

開く！
探す！
選ぶ！
確認する！

4ステップだ！！

「一覧にない……終わった……」？

終わってない！！ 始まってすらいない！！

Pure Base は現在、開発版だ。管理ソフトの設定によっては一覧に表示されない場合がある！

見えないなら、開発版やプレリリースを表示する設定を有効にしろ！

設定の扉を開け！
開発版の表示を許可しろ！

Pure Base は隠れているんじゃない。
**お前がまだ表示していないだけだ！！**

## 基本的な使い方 ― マテリアルは作らなければ始まらない！！

1. Unityで新しいマテリアルを作成します。
2. マテリアルのシェーダーから `PureBase` を選びます。
3. 用途に合わせて `Unlit`、`Toon`、`PBR`、`Hybrid` のいずれかを選びます。
4. 基本色やテクスチャなどを設定します。
5. 必要に応じて Shader-Core の追加モジュールを組み合わせます。

まずマテリアルを作れ！！

頭の中にある最高のマテリアルは、Unity上ではまだファイルサイズ0バイト以下だ！

作らなければ存在しない！
選ばなければ描画されない！
設定しなければ伝わらない！！

`PureBase` を選べ！

そして4つの中から決めろ！

- 周囲の明るさに影響されたくない？ `Unlit`！
- アニメ調で攻めたい？ `Toon`！
- 一般的な質感から始めたい？ `PBR`！
- トゥーンと物理反射を両立したい？ `Hybrid`！

まだ迷う！？

いい！ 迷うのは本気で選ぼうとしている証拠だ！！

だが、迷ったままマウスを置くな！

最初の一手は、アニメ調なら `Toon`！ 一般的な質感なら `PBR`！

選んでから考えろ！ 表示してから比べろ！

**画面に出た結果だけが、次の判断材料になる！！**

## 注意点 ― 制限を知った者だけが、自由に拡張できる！！

- Pure Base 本体は、できるだけ小さく保つ方針です。
- リムライト、MatCap、発光、ディゾルブなどの追加表現は、別の Shader-Core モジュールで補う想定です。
- 正式版ではない版では、仕様や使い方が変更される可能性があります。
- 不具合を報告する際は、使用したUnity、Pure Base、Shader-Coreの版を記載してください。

なぜ小さく保つ！？

足りないからじゃない！

**役割を分けるためだ！！**

リムライトが欲しい？
MatCapが欲しい？
発光したい？
ディゾルブで消えたい！？

いい！ その表現欲、最高だ！！

だが全部を本体へ押し込むんじゃない。必要な Shader-Core モジュールで補うんだ！

一つの巨大な塊にするな。
必要な力を、必要な場所へ組み合わせろ！！

そして忘れるな。これは正式版ではない版を含む！ 仕様や使い方が変わる可能性がある！

変化を恐れるな。

だが、変化を知らずに昨日の手順へしがみつくな！！

不具合を報告するときは、ただ「動きません！」で終わらせるな！

- Unity の版！
- Pure Base の版！
- Shader-Core の版！

この三つを書け！！

バージョン情報は飾りじゃない。
問題へ向かうための座標だ！！

## 詳しい資料 ― READMEの先にも道は続いている！！

一般的な利用なら、このREADMEだけで導入と基本操作を始められる！

つまり、今すぐ始められる！！

だが、お前の好奇心はそこで止まるのか！？

実装仕様を知りたい！
公開手順を追いたい！
検証方法まで把握したい！

その気持ち、閉じ込めるな！！

開発者向け情報は、[技術資料](Docs/technical-information.ja.md)にまとめてある！

READMEが入口なら、技術資料は地下深くまで続く探検ルートだ！

読むか読まないかは自由だ。

だが、知りたいと思ったその瞬間、リンクはもう目の前にある！！

## ライセンスと支援について ― 金銭ではなく、行動で前へ進め！！

Pure Base は Apache License 2.0 で公開されています。詳しくは [LICENSE](LICENSE) を確認してください。

ライセンスは勢いで読み飛ばすためにあるんじゃない！

使うなら確認しろ！
配るなら確認しろ！
直すなら確認しろ！！

そして、Pure Base と Penguin は金銭的な支援を受け付けていない！

「じゃあ何もできないのか」？

違う！！

- 不具合を見つけたら報告できる！
- 改善案を思いついたら伝えられる！
- コードを直せるなら修正を送れる！

お金だけが支援じゃない。

気づくことも支援だ！
伝えることも支援だ！
直すことも支援だ！！

使え！
試せ！
壊れたら調べろ！
分かったら伝えろ！
直せるなら直せ！！

小さな一報が、次の改善を動かす。
一本のプルリクエストが、誰かの詰まりを消す。
一つの修正が、まだ会ったこともない誰かの制作時間を救う！！

Pure Base は土台だ。

土台は目立たないかもしれない。

だが忘れるな！！

**高く伸びる表現ほど、足元の土台が支えている！！**

さあ、マテリアルを作れ。
シェーダーを選べ。
色を置け。
テクスチャを載せろ。
必要なモジュールを組み合わせろ。

昨日より一つ、画面へ出せ！！

お前の表現は、まだ完成していない。

だから面白いんだ！！！

🔥🔥🔥 封印README・完 🔥🔥🔥

-->
