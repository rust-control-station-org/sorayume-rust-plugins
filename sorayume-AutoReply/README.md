# sorayume-AutoReply

導入と最初の操作は [クイックスタート](クイックスタート.md) を参照。

チャットの `!vip`、`!discord`、`!kit`、`!rules` に、入力者本人だけへ返信する。前後の空白と大文字小文字を無視し、一致した入力の通常チャット表示を抑止する。

- 作者: `mazurk4`。バージョン: `1.3.1`。
- 提供元: ユーザー登録ソース。変更前: `Auto Reply`、作者 `Mazurk4_`、`1.3.0`。配布元 URL・ライセンスは未確認。再配布許諾の範囲は未確認。
- 今回の変更: フォルダ名・表示名と作者の統一。既存の日英メッセージと処理を維持。
- 対応: Oxide/Rust。必須の追加プラグインなし。実機確認は Rust buildid 26479619 / Oxide Core 2.0.4156。Carbon は未検証。
- 権限: 追加権限なし。プレイヤー本人の Oxide 言語設定に応じた英語・日本語で返信する。

`AutoReply.cs` を `oxide/plugins/AutoReply.cs` に配置する。既存版の更新も同名で上書きし、必要なら `oxide.reload AutoReply` を実行する。\
翻訳・返信内容は `oxide/lang/en/AutoReply.json` と `oxide/lang/ja/AutoReply.json` で編集する。Discord URL は既存の仮 URL のままなので、実際の案内先に合わせて設定する。\
独自の設定ファイル・プレイヤーデータ・外部通信はない。ワイプで初期化する独自データもない。\
プレイヤーは `/lang ja` または `/lang en` で言語を選択する。

更新前に `.cs` と言語ファイルをバックアップする。撤去は `oxide.unload AutoReply` の後に本体をサーバーのプラグイン配置先から移動する。復元時はバックアップを戻してロードする。

## 検証

2026-11-21、Rust buildid 26479619 / Oxide.Rust 2.0.7894 を対象に、本プラグインのコンパイル・ロード・日英メッセージの登録・アンロード・再ロードの確認が成功（管理者報告）。代替 API の通常テストで主要分岐と日英返信を確認。プレイヤー接続を伴う操作は実機で未確認。

参照: [Oxide/uMod Documentation](https://umod.org/documentation)。
