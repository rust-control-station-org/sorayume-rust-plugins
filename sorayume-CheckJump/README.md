# sorayume-CheckJump

導入と最初の操作は [クイックスタート](クイックスタート.md) を参照。

対象プレイヤーを垂直移動させる管理用プラグイン。

- 作者: `mazurk4`。バージョン: `1.1.0`。
- 提供元: ユーザー登録ソース。変更前: `CheckJump`、作者 `ysaito`、`1.0.1`。配布元 URL・ライセンスは未確認。再配布許諾の範囲は未確認。
- 今回の変更: フォルダ名・表示名と作者の統一、日英の返信、説明文の既定高度を実装どおり20mに修正。
- 対応: Oxide/Rust。必須の追加プラグインなし。実機確認は Rust buildid 26479619 / Oxide Core 2.0.4156。Carbon は未検証。
- コマンド: チャットで `/jmp <SteamID/名前> [高さ]`。省略時は `20`m。
- 権限: `checkjump.use` を持つプレイヤー、または管理者。権限付与の例（サーバーコンソール）: `oxide.grant user <SteamID> checkjump.use`。

`CheckJump.cs` を `oxide/plugins/CheckJump.cs` に配置する。既存版の更新も同名で上書きし、必要なら `oxide.reload CheckJump` を実行する。\
返信は `oxide/lang/en/CheckJump.json` と `oxide/lang/ja/CheckJump.json` で編集できる。言語は実行者の設定に従い、`/lang ja` または `/lang en` で選択する。管理ログは既存どおり英語。\
独自の設定ファイル・永続データ・外部通信はない。ワイプ対応は不要。

更新前に `.cs` と言語ファイルをバックアップする。撤去は `oxide.unload CheckJump` の後に本体を移動する。復元時はバックアップを戻してロードする。既存の権限付与はそのまま使用できる。

## 検証

2026-11-21、Rust buildid 26479619 / Oxide.Rust 2.0.7894 を対象に、本プラグインのコンパイル・ロード・日英メッセージの登録・アンロード・再ロードの確認が成功（管理者報告）。代替 API の通常テストで主要分岐と日英返信を確認。プレイヤー接続を伴う操作は実機で未確認。 数値でない高さは既存処理で `0` となり、高さの範囲・非有限値の制限は未実装。

参照: [Oxide/uMod Documentation](https://umod.org/documentation)。
