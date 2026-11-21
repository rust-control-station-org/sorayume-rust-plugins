# sorayume-wabi

Rust のソロサーバーで、補填キャンペーンのアイテム・対象者・期限を管理し、プレイヤーが `/wabi` で受け取るプラグイン。受領履歴を保存し、同じキャンペーンの重複受領を防ぐ。

- 作者: `mazurk4`。バージョン: `1.1.0`。
- 元ソース: 作者本人の [mazurk4/rust-game-wabi](https://github.com/mazurk4/rust-game-wabi)、`Wabi` 1.0.1（表示名 `Wibi`、作者表記 `Mazurk4_`）。取得時 HEAD: `3e57d1b69a8818ef7a7151c668595cefb079f8eb`。
- ライセンス: 元の MIT License を維持（本文は末尾に掲載）。
- 管理ディレクトリ・表示名は依頼に合わせて小文字の `sorayume-wabi`。クラス・ファイル名・Oxide 内部名は `Wabi` のまま。
- 対応: Oxide/Rust。追加プラグイン不要。Newtonsoft.Json は Oxide/Rust 同梱版を使う。Carbon は未検証。
- 確認対象: Oxide.Rust 2.0.7894 と、2026-11-09 に取得した Rust 公開版 DLL（depot 258552、manifest `7175333784427202048`）。実機で確認した範囲は下記「検証」を参照。

## 挙動

- 管理者がキャンペーンを作り、アイテム・対象・期限を設定して受付を開始する。受付中は編集できない。操作例は [クイックスタート](クイックスタート.md)。
- 対象は全員または SteamID64 の個別指定。受け取りに権限は不要だが、キャンペーンの対象者であることを確認する。
- 相対期限は `wabi.start` から数える。固定期限はサーバーのローカル時刻で指定する。
- 付与前に `pending` を保存し、成功時は `claimed`、付与失敗時は `failed` とする。`pending`・`failed` は自動再試行しない。途中まで付与されている可能性があるため、管理者が確認する。
- 受け取りはメインインベントリ、ベルトの順に試し、空きがなければ設定に従って足元へドロップする。数量はアイテムのスタック上限で分割する。1回の受領は合計1000スタックまでとし、超過するキャンペーンは開始・受領を拒否する。
- 受付停止後も履歴は残る。`wabi.reset` は履歴だけを消す。既に渡したアイテムは回収せず、再受領すると二重付与になり得る。
- `OnNewSave(string filename)` で新しい Season ID に更新し、全キャンペーンを停止する。過去のキャンペーン・受領履歴は削除しない。通常の再読み込みでは履歴を引き継ぐ。
- タイマー・外部通信なし。期限判定はコマンド実行時に行う。

## コマンドと権限

| 入口 | コマンド | 内容 |
| --- | --- | --- |
| チャット | `/wabi` | 現在のキャンペーンの補填を受け取る。 |
| コンソール | `wabi.help` | 管理コマンドの一覧。 |
| コンソール | `wabi.new [id]` | キャンペーンを作成し、現在のキャンペーンにする。省略時は日時から ID を生成。指定 ID は英数字・`_`・`-` の1〜64文字。 |
| コンソール | `wabi.item <shortname> <amount>` | アイテムを追加・数量を上書き。数量は1以上の整数。 |
| コンソール | `wabi.remove <shortname>` | アイテムを設定から削除。 |
| コンソール | `wabi.target all` | 全員を対象にする。 |
| コンソール | `wabi.target add <steamid64>` / `remove <steamid64>` | 個別対象を追加・削除し、個別対象モードにする。 |
| コンソール | `wabi.target clear` | 個別対象を空にする。この直後は誰も受領できない。 |
| コンソール | `wabi.expire default` / `<3h\|30m>` / `<yyyy-MM-dd HH:mm>` | 既定・相対・固定の期限を設定。相対期限は最大5,256,000分。 |
| コンソール | `wabi.start` / `wabi.stop` | 受付の開始・停止。 |
| コンソール | `wabi.list` | 現在のキャンペーンと受領件数を表示。期限切れなら停止を保存する。 |
| コンソール | `wabi.check <steamid64> [campaignId]` | 現在または指定キャンペーンの受領記録を表示。 |
| コンソール | `wabi.reset <steamid64> [campaignId]` | 受領記録を削除。再付与の判断は管理者が行う。 |

サーバーコンソール・RCON は管理コマンドを実行できる。プレイヤーの F1 コンソールは管理者（`IsAdmin`）または `wabi.admin` が必要。接続情報があるのにプレイヤーが取得できない呼び出しは拒否する。

```text
oxide.grant user <SteamID> wabi.admin
```

返信は lang API で英語・日本語を登録し、実行者の言語（`/lang en`・`/lang ja`）で取得する。サーバーコンソール・RCON はサーバー言語を使う。言語ファイルは `oxide/lang/en/Wabi.json`・`oxide/lang/ja/Wabi.json`。

## 設定・保存データ

設定は `oxide/config/Wabi.json`。初回のみ既定値で生成する。設定のキーと既定値は次の表を参照。

| キー | 既定値 | 内容 |
| --- | --- | --- |
| `Default expiry hours` | `3.0` | 新規キャンペーンと `expire default` の受付時間。有限・0より大きく87600以下。分単位に丸め、最低1分。 |
| `Drop items at player's feet when inventory is full` | `true` | メインとベルトに入らないアイテムを足元にドロップする。`false` は受領失敗として記録する。 |

保存先は `oxide/data/Wabi.json`。Season ID、キャンペーン設定、SteamID・表示名・受領状態・時刻・付与内容・エラーを保存する。設定・データのキーや保存先は旧版を維持する。

壊れた JSON、必須フィールドの欠損、不正な設定・データでは、ファイルを上書きせずに操作を無効化してログへ出す。修復後に `oxide.reload Wabi` を実行する。保存失敗でも操作を無効化し、付与前の `pending` が保存できなければアイテムを渡さない。ディスク障害後は保存ファイルと実際の付与状況を確認してから再開する。

## 導入・更新・撤去

1. 旧版を含め `oxide/plugins/Wabi.cs`・設定・データ・言語ファイルをバックアップする。受領履歴を消さない。
2. 更新時は `oxide.unload Wabi` を実行し、`Wabi.cs` を `oxide/plugins/Wabi.cs` に配置する。自動ロードされない場合は `oxide.load Wabi`。
3. `wabi.list` で既存キャンペーンを確認する。初回は `wabi.new` から設定し、`wabi.start` で受付を開始する。

撤去は `oxide.unload Wabi` の後に本体を移動する。設定・履歴は残し、再導入時に引き継ぐ。旧版に戻す場合は本体・設定・データをバックアップから戻す。データだけを古い状態に戻すと再受領が可能になるため、既に渡したアイテムとの照合が必要。

## 取り込み時の変更

- 表示名と作者表記を統一し、1.1.0 に更新。コマンド・権限・ファイル名は維持。
- 日英併記・日本語固定の返信を lang API に移行。コンソール返信は `Arg.ReplyWith` に統一し、RCON からも取得できるようにした。
- プレイヤー不在の接続からの管理操作を拒否。
- 設定・データ破損時の自動初期化を廃止。受領履歴などの必須フィールド欠損も拒否し、保存失敗時は操作を止める。
- 非有限の期限、危険なキャンペーン ID、過大なスタック数を拒否。ドロップ失敗を検出。
- 権限・入力・保存データに関するテストを追加。

## 検証

2026-11-21、Rust buildid 26479619 / Oxide.Rust 2.0.7894 を対象に、本プラグインのコンパイル・ロード・日英メッセージの登録・アンロード・再ロードの確認が成功（管理者報告）。`wabi.list` の応答までを確認対象とし、実際のキャンペーン作成・受領・クラッシュ時の挙動は未確認。代替 API を使った通常テストでは、権限、対象・期限、付与前保存、二重付与防止、設定・データ破損時の保護を確認。

参照: [Oxide/uMod Documentation](https://umod.org/documentation)。

## ライセンス

```text
MIT License

Copyright (c) 2026 Mazurk4_

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
