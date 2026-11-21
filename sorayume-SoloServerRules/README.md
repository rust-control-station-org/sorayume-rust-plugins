# sorayume-SoloServerRules

導入と最初の操作は [クイックスタート](クイックスタート.md) を参照。

Code Lock の所有者以外によるコード入力と、寝袋・ベッドの他プレイヤーへの割り当てを拒否する。所有者自身のコード入力、自分への割り当ては許可する。

- 作者: `mazurk4`。バージョン: `1.2.0`。
- 提供元: ユーザー登録ソース。変更前: `Solo Server Rules`、作者 `Mazurk4_`、`1.1.0`。配布元 URL・ライセンスは未確認。再配布許諾の範囲は未確認。
- 今回の変更: フォルダ名・表示名と作者の統一、拒否メッセージの日英対応。制限の判定は維持。
- 対応: Oxide/Rust。必須の追加プラグインなし。実機確認は Rust buildid 26479619 / Oxide Core 2.0.4156。Carbon は未検証。
- コマンド・権限: 独自の登録なし。管理者を含め、全プレイヤーに同じ制限を適用する。

`SoloServerRules.cs` を `oxide/plugins/SoloServerRules.cs` に配置する。既存版の更新も同名で上書きし、必要なら `oxide.reload SoloServerRules` を実行する。\
返信は `oxide/lang/en/SoloServerRules.json` と `oxide/lang/ja/SoloServerRules.json` で編集できる。操作したプレイヤーの言語設定に従い、`/lang ja` または `/lang en` で選択する。\
独自の設定ファイル・永続データ・外部通信はない。既存のロック認証情報・寝袋割り当てを遡って削除する処理はない。

更新前に `.cs` と言語ファイルをバックアップする。撤去は `oxide.unload SoloServerRules` の後に本体を移動する。復元時はバックアップを戻してロードする。

## 検証

2026-11-21、Rust buildid 26479619 / Oxide.Rust 2.0.7894 を対象に、本プラグインのコンパイル・ロード・日英メッセージの登録・アンロード・再ロードの確認が成功（管理者報告）。代替 API の通常テストで主要分岐と日英返信を確認。プレイヤー接続を伴う操作は実機で未確認。

参照: [Oxide/uMod Documentation](https://umod.org/documentation)。
