# sorayume-rust-plugins

Rust ゲームのソロサーバー向け Oxide/uMod プラグイン集です。作者は `mazurk4`、実装言語は C# です。

各プラグインは単独で導入でき、英語・日本語のメッセージに対応しています。基本の対応対象は Oxide/Rust です。Carbon での互換性は未検証です。

## プラグイン一覧

最初の導入と操作は各「クイックスタート」、全コマンド・権限・設定・制限は各「詳細」を参照してください。

| プラグイン | 用途 | バージョン | 使い方 |
| --- | --- | --- | --- |
| AutoReply | `!vip`・`!discord`・`!kit`・`!rules` に本人だけへ自動返信 | 1.3.1 | [クイックスタート](sorayume-AutoReply/クイックスタート.md) / [詳細](sorayume-AutoReply/README.md) |
| CheckJump | 管理者向けの垂直テレポート | 1.1.0 | [クイックスタート](sorayume-CheckJump/クイックスタート.md) / [詳細](sorayume-CheckJump/README.md) |
| SoloServerRules | Code Lock の他者によるコード入力と、寝袋・ベッドの他者への割り当てを制限 | 1.2.0 | [クイックスタート](sorayume-SoloServerRules/クイックスタート.md) / [詳細](sorayume-SoloServerRules/README.md) |
| PowerPlantFuseRefill | パワープラントのヘビーヒューズ補充と状態・推定寿命の表示 | 1.0.0 | [クイックスタート](sorayume-PowerPlantFuseRefill/クイックスタート.md) / [詳細](sorayume-PowerPlantFuseRefill/README.md) |
| LighthouseAutoRepair | ワイプから指定時間後に未修理の灯台を自動修理 | 1.0.0 | [クイックスタート](sorayume-LighthouseAutoRepair/クイックスタート.md) / [詳細](sorayume-LighthouseAutoRepair/README.md) |
| MonumentReset | モニュメントのパズル・箱・NPC を手動リセット | 1.1.0 | [クイックスタート](sorayume-MonumentReset/クイックスタート.md) / [詳細](sorayume-MonumentReset/README.md) |
| Wabi | 対象・期限付きの補填配布と受領履歴の管理 | 1.1.0 | [クイックスタート](sorayume-wabi/クイックスタート.md) / [詳細](sorayume-wabi/README.md) |

## 導入

1. 導入したいプラグインのクイックスタートを読み、必要な権限と初回ロード時の挙動を確認します。
2. 対象ディレクトリの `.cs` をダウンロードします。個別のファイルページで **Raw** を開き、ファイル名を変えずに保存してください。一式を取得する場合は **Code → Download ZIP** を利用できます。
3. 既存版を更新する場合は、本体・設定・言語ファイル・保存データをバックアップします。
4. `.cs` をサーバーの `oxide/plugins/` に配置します。通常は自動でロードされます。設定・言語ファイルがあるプラグインは、初回ロード時に生成します。
5. 各プラグインの説明に従って設定と権限を整え、動作を確認します。

例: `sorayume-wabi/Wabi.cs` の配置先は `oxide/plugins/Wabi.cs` です。`sorayume-` は配布ディレクトリと表示名に付く名前で、`.cs` の名前には付けません。

PowerPlantFuseRefill は既定でロード直後に補充します。LighthouseAutoRepair は期限を過ぎていればロード直後に修理します。状態だけを先に確認したい場合は、各クイックスタートにある自動処理を無効にする設定を配置してから導入してください。

導入仕様: [uMod Plugin installation](https://umod.org/documentation/plugins/installation)。

## 言語・更新・撤去

ゲーム内チャットで `/lang ja` または `/lang en` を実行すると、対応する言語で案内を受け取れます。権限の名前と、サーバーコンソール・チャット・F1 コンソールのどこから実行できるかは各 README を参照してください。

更新時は同じファイル名を使用します。必要に応じてサーバーコンソールから `oxide.reload <内部名>` を実行します。Wabi の更新では、先に `oxide.unload Wabi` を実行し、受領履歴を保持してください。

撤去は `oxide.unload <内部名>` の後に本体を配置先から移動します。補充したアイテムや修理・リセット後のワールドの状態は元に戻りません。保存データの扱いは各 README を確認してください。

## 対応版・検証範囲

2026-11-21 時点の確認対象は Rust buildid 26479619 / Oxide.Rust 2.0.7894 です。ゲーム更新後の互換性と、実機で確認済みの操作・未確認の操作は各 README に記載しています。

## 出典・ライセンス

出典とライセンスはプラグインごとに異なります。各 README を確認してください。Wabi は元の MIT License と著作権表示を README 内に掲載しています。その他のライセンスが未指定・未確認のプラグインに、Wabi の MIT License が適用されるものではありません。
