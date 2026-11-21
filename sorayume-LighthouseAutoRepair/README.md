# sorayume-LighthouseAutoRepair

導入と最初の操作は [クイックスタート](クイックスタート.md) を参照。

ワイプから指定した時間が過ぎても灯台が修理されていなければ、自動で修理する。\
2026-11-05 のアップデート（Printed and Polished）以降、ディープシーは灯台を修理するまで開かない。ソロサーバーでのこの負担を減らすためのプラグイン。

- 作者: `mazurk4`。バージョン: `1.0.0`。
- 提供元: 自作。外部由来のコードなし。ライセンスは未指定。
- 対応: Oxide/Rust。必須の追加プラグインなし。Carbon は未検証。
- 対象: Rust buildid 26479619（2026-11-09 確認）、Oxide.Rust 2.0.7894。

## 挙動

- ワイプの時刻は、ゲームがセーブに記録している作成時刻（`SaveRestore.SaveCreatedTime`）を使う。サーバーを再起動しても基準は変わらず、プラグイン側の永続データはない。
- 期限（既定はワイプの24時間後）までに誰も修理していなければ、期限の時点で全灯台を修理済みにする。確認は1分ごと。
- 期限を過ぎてからサーバーを起動した場合（期限の時刻に停止していた等）は、起動の直後に修理する。
- 途中まで修理してある灯台も、期限が来たら完了させる。素材は消費しない。
- プレイヤーが期限前に修理していれば何もしない。1か所でも修理済みなら、ゲームと同じく修理済みとして扱う。
- 修理後のディープシーはゲーム本体の処理どおりに動く。プレイヤーが修理を終えた場合と同じ処理を呼ぶため、ゲームの設定 `deepsea.lighthouseOpenDelay`（既定 0 秒）が過ぎると開き、その後は通常の周期になる。
- 自動修理したときは、オンラインの各プレイヤーに、その人の言語でチャット通知を送る（設定で無効にできる）。サーバーログにも記録する。
- 灯台のないマップ、ディープシーが無効のサーバー（`deepsea.enabled false`）では何もしない。どちらもゲーム側で灯台を待たない。
- 修理の処理が効かなかった場合（他のプラグインやゲームの変更など）は、エラーをログに出し、プラグインを再読み込みするまで自動修理を止める。毎分の再試行でログを埋めないため。
- 灯台の修理エフェクト（音・光の演出）は、自動修理では出さない。

### ゲーム側の挙動

Rust buildid 26479619 で確認した、灯台とディープシーに関わるゲーム本体の挙動。

- サーバー上の灯台は、修理の進み具合を共有する。1か所を修理すると、すべての灯台が修理済みになる。
- 修理が完了すると、ディープシーは `deepsea.lighthouseOpenDelay` の後に開く。本プラグインは、プレイヤーが修理を完了したときと同じ処理を同じ順で呼ぶ。
- ディープシーは灯台が修理されるまで開かない。ただし、灯台が1つもないマップ、一度開いたことがある場合、`deepsea.openOnServerWipe` が真の場合は待たない。
- `deepsea.enabled` が偽のとき、ゲームは灯台を最初から修理済みにする。
- ゲーム本体の設定 `deepsea.openOnServerWipe true` でも、ワイプ直後からディープシーを開ける（灯台の修理は待たない）。期限まではプレイヤーの修理を待ちたい場合に本プラグインを使う。本プラグインはこの設定を変更しない。

## コマンドと権限

コンソールコマンドのみ。チャットコマンドはない。

| コマンド | 内容 |
| --- | --- |
| `lighthouserepair.status` | 灯台の状態（修理済みか、灯台の数）、ワイプの時刻、自動修理の設定と期限（未修理なら残り時間）、ディープシーの状態を表示する。何も変更しない。 |
| `lighthouserepair.repair` | 期限を待たずにすぐ修理する。修理済み・灯台なし・ディープシー無効の場合は何もしない。自動修理が無効でも使える。 |

- 使える入口: サーバーコンソールと RCON（接続を持たない実行）は常に許可。F1 コンソールは、管理者（`IsAdmin`）か `lighthouseautorepair.admin` の権限を持つプレイヤーに限る。
- 権限の付与例: `oxide.grant user <SteamID> lighthouseautorepair.admin`。
- 引数は取らない。引数を付けた場合は何もせずに使用法を返す。
- 返信は実行者の言語（`/lang ja` または `/lang en`）、サーバーコンソールと RCON ではサーバーの言語で表示する。言語ファイルは `oxide/lang/en/LighthouseAutoRepair.json` と `oxide/lang/ja/LighthouseAutoRepair.json`。サーバーログは英語。
- 時刻はサーバーのタイムゾーンで表示する。
- ディープシーの生成中は、状態が「閉じています。次に開くまで約0時間00分」と表示される場合がある。生成が完了すると開いた状態になる。

表示例（`lighthouserepair.status`、日本語。値は例）:

```text
灯台: 未修理（このマップに 1 か所）。
ワイプ（セーブ作成）: 2026-11-06 04:00 +09:00。
自動修理: 有効。ワイプから 24 時間後（2026-11-07 04:00 +09:00, あと約23時間00分）。
ディープシー: 灯台の修理待ち。
```

## 設定

`oxide/config/LighthouseAutoRepair.json`。初回ロード時に既定値で作られる。設定のキーと既定値は次の表を参照。

| キー | 既定値 | 範囲 | 内容 |
| --- | --- | --- | --- |
| `Auto repair enabled` | `true` | `true`/`false` | 自動修理を行うか。 |
| `Hours after wipe before auto repair` | `24` | 0〜720 | ワイプから自動修理までの時間（時間単位、小数可）。0 ならサーバーの起動直後に修理する。 |
| `Announce auto repair in chat` | `true` | `true`/`false` | 自動修理したときにチャットで知らせるか。 |

- 変更後は `oxide.reload LighthouseAutoRepair` で反映する。
- キーの欠損、範囲外、NaN・無限大、読み込めないファイルの場合は、ファイルを書き換えずにエラーをログに出し、自動修理とコマンドを止める。
- 永続データはない。修理の状態はゲームのワールドのセーブに残り、ワイプで未修理に戻る。
- 外部通信はない。

## 導入・更新・撤去

1. 既存の `oxide/plugins/LighthouseAutoRepair.cs`、`oxide/config/LighthouseAutoRepair.json`、言語ファイルがあればバックアップする。
2. `LighthouseAutoRepair.cs` を `oxide/plugins/LighthouseAutoRepair.cs` に配置する（自動でロードされる）。更新も同名で上書きし、必要なら `oxide.reload LighthouseAutoRepair` を実行する。
3. 必要なら設定を編集して再読み込みし、`lighthouserepair.status` で期限と状態を確認する。

- 導入した時点で期限を過ぎていて未修理なら、ロードの直後に修理される。期限前に様子を見たい場合は、先に設定の `Auto repair enabled` を `false` にしてから配置する。
- 撤去は `oxide.unload LighthouseAutoRepair` の後に本体を移動する。修理済みの灯台はそのまま残る（元に戻す処理はない）。
- 復元時はバックアップを戻してロードする。

## 検証

2026-11-21、Rust buildid 26479619 / Oxide.Rust 2.0.7894 を対象に、本プラグインのコンパイル・ロード・日英メッセージの登録・アンロード・再ロードの確認が成功（管理者報告）。2026-11-09 に実機で手動修理・ディープシー開放・期限による自動修理・再ロードを確認。チャット通知、F1 コンソールと RCON での実行、期限時刻に停止していた場合の起動直後の修理は実機で未確認。

参照:

- [RUST November 2026 Update Preview（Corrosion Hour、2026-11-04、コミットからのプレビュー）](https://www.corrosionhour.com/rust-november-2026-update-preview/)
- [Rust November 2026 Update: patch notes preview（Rustfront）](https://rustfront.com/news/upcoming-2026-11)
- [PRINTED AND POLISHED · Rust update for 5 November 2026（SteamDB）](https://steamdb.info/patchnotes/26434844/)（本文は取得できず、検索結果の抜粋のみ確認）
- [Lighthouse（RustHelp）](https://rusthelp.com/monument/lighthouse)
- [Oxide Configuration](https://umod.org/documentation/api/configuration)、[Permissions](https://umod.org/documentation/api/permissions)、[Localization](https://umod.org/documentation/api/localization)、[Timers](https://umod.org/documentation/api/timers)

資料の差異: 修理に必要な素材の種類と量は、確認した資料のどれにも書かれていなかった。本プラグインは素材を使わないため、実装には影響しない。
