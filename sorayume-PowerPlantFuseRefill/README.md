# sorayume-PowerPlantFuseRefill

導入と最初の操作は [クイックスタート](クイックスタート.md) を参照。

パワープラントのヒューズボックスに、ヘビーヒューズ（Heavy Fuse、`fuse.highgrade`）を無償で補充する。\
ソロサーバーでは入手が難しいヒューズを補い、電力網のステージを維持するためのプラグイン。

- 作者: `mazurk4`。バージョン: `1.0.0`。
- 提供元: 自作。外部由来のコードなし。ライセンスは未指定。
- 対応: Oxide/Rust。必須の追加プラグインなし。Carbon は未検証。
- 対象: Rust buildid 26479619（2026-11-05 の更新後）、Oxide Core 2.0.4156、Oxide.Rust 2.0.7894、Oxide.CSharp 2.0.4149。

## 挙動

- 補充するのは空いているソケットだけ。稼働中のヒューズを、壊れたものを除いて数え、目標本数に満たない分を挿入する。挿入済みのヒューズを交換・修理することはない。
- 目標本数の上限は、マップ上のヒューズボックスの総ソケット数。ボックスはエンティティ ID の小さい順に、各ボックスのスロットは先頭から埋める。
- 自動補充は一定間隔で行う（既定は3時間）。設定が有効なら、プラグインのロード時（サーバー起動・ワイプ直後・リロード）にも1回補充する。
- コマンドでいつでも補充でき、自動補充のオンオフも切り替えられる。外部のスケジューラから RCON で `powerplantfuse.fill` を送れば、任意の時刻に補充できる。
- ヒューズの劣化と、壊れたヒューズがボックスの前に落ちる挙動はゲーム本体のまま変えない。壊れたヒューズは、そのアイテムの消滅時間（1時間）で消える。
- 挿入元のプレイヤーがいない状態で挿入する。そのため実績（`POWERPLANT_HEAVY_FUSE`）とクランスコアは付かない。
- 他のプラグインが挿入を拒否した場合、作ったヒューズは削除し、不足本数を返信とログに出す。
- `powerplantfuse.status` と `powerplantfuse.list` で、送電線の出力、次のステージまでの本数、ヒューズごとの耐久と切れるまでの推定時間を確認できる。確認だけのコマンドで、ヒューズの挿入や状態の変更はしない。

### 切れるまでの時間の推定

ゲーム本体と同じ計算（buildid 26479619 で確認）で、補充しない場合にどの順で切れるかを計算する。

- 毎秒の減り幅は「最大耐久 ÷ `powergrid.fuselifespanseconds` × 速度倍率 × 人数倍率」。
- 速度倍率は、ゲームが全速の対象に選んだヒューズで1、それ以外はヒューズごとに固定の低速倍率（既定 8〜12%）。
- 全速の対象は、耐久の低い順に `powergrid.fusefulldecaycount` 本。ゲームは毎秒選び直すため、推定でも1本切れるたびに選び直す。
- 人数倍率はゲームの `Server_GetPopDecayRateScale` に、いまのオンライン人数を渡して得る。
- 推定の前提は、オンライン人数がこのまま変わらず、補充もしないこと。人数の変化、補充、他のプラグインやゲーム設定の変更でずれる。表示は分単位の四捨五入。

### ゲーム側の挙動

Rust buildid 26479619 で確認した、ヒューズボックスに関わるゲーム本体の挙動。

- ヒューズボックスに入るのはヘビーヒューズだけで、1スロット1本。プレイヤーは取り外せず、交換できるのは状態がより良いヒューズとだけ。
- ゲームの更新でヒューズの格納先が見つからない版では、何も挿入せず、エラーをログに出す。
- ステージはヒューズの本数で決まる。閾値は 1・4・10・18 本（2026-11-09 に実機で確認）で、送電線の出力は総ソケットに近づくほど上がる（`powergrid.powerlinebasepoweroutput` 5 から `powergrid.powerlinemaxpoweroutput` 50）。
- 劣化は1秒ごとに計算される。状態の悪い `powergrid.fusefulldecaycount`（3）本は全速で、残りは 8〜12% の速度で劣化する。接続人数が `powergrid.fusedecaylowpop`（10）以下なら、さらに `powergrid.fusedecaylowpopscale`（0.25）倍になる。
  - 既定値でのソロ運用の目安: 全速の1本は 9600 秒 ÷ 0.25 ≒ 10時間40分で壊れる。既定の3時間間隔で十分に追いつく。
- 補充の代わりに、ゲーム本体の設定でも似た状態は作れる。`powergrid.fuselifespanseconds 0`（劣化なし）や `powergrid.simulatepowerplantfuses`（仮想のヒューズ数）。本プラグインはこれらを変更しない。

## コマンドと権限

コンソールコマンドのみ。チャットコマンドはない。

| コマンド | 内容 |
| --- | --- |
| `powerplantfuse.fill [目標本数 0-64]` | すぐに補充する。省略時は設定の目標本数。指定した本数は今回だけ使う。 |
| `powerplantfuse.auto [on\|off]` | 自動補充を切り替える。結果は設定ファイルに保存する。引数なしなら現在の状態を表示する。 |
| `powerplantfuse.status` | 自動補充の状態、間隔、目標、稼働中の本数とソケット数、電力網ステージを表示する。続けて送電線の出力（現在値/最大）、次のステージの必要本数と残り本数（最大なら「最大ステージ」）、次に切れるヒューズと推定時間を表示する。ヒューズは挿入しない。 |
| `powerplantfuse.list` | 挿入済みのヘビーヒューズを1本ずつ表示する。ソケット（ボックス番号-スロット番号）、耐久（最大耐久に対する%）、劣化（全速なら「通常」、低速なら「低速」）、切れるまでの推定時間を出し、最後に空きソケット数と推定の前提を表示する。ヒューズは挿入しない。 |

- 使える入口: サーバーコンソールと RCON（接続を持たない実行）は常に許可。F1 コンソールは、管理者（`IsAdmin`）か `powerplantfuserefill.admin` の権限を持つプレイヤーに限る。
- 権限の付与例: `oxide.grant user <SteamID> powerplantfuserefill.admin`。
- 権限・入口は全コマンドで共通。確認だけの `status` と `list` も、補充と同じく管理者限定にしている。
- 引数は、0〜64 の整数（符号・空白・指数は不可）か `on`/`off` だけを受け付ける。`status` と `list` は引数を使わない。引数が多すぎる場合や不正な値は、何も変更せずに使用法を返す。
- 返信は実行者の言語（`/lang ja` または `/lang en`）、サーバーコンソールと RCON ではサーバーの言語で表示する。言語ファイルは `oxide/lang/en/PowerPlantFuseRefill.json` と `oxide/lang/ja/PowerPlantFuseRefill.json`。サーバーログは英語。

表示例（`powerplantfuse.list`、英語。値はテストでの計算例）:

```text
Socket  Condition  Decay  Burnout in (estimate)
1-01        30.0%  full   ~3h 12m
1-02        50.0%  full   ~5h 20m
1-03        80.0%  full   ~8h 32m
1-04       100.0%  slow   ~15h 34m
1-05       100.0%  slow   ~13h 29m
Empty sockets: 15. Socket = box-slot. Estimates assume 1 online player(s) and no refill.
```

- ボックス番号は、ヒューズボックスをエンティティ ID の小さい順に並べた番号（補充の順序と同じ）。ゲーム内の見た目の位置とは対応しない場合がある。
- `powergrid.fuselifespanseconds` が 0 なら、`status` は劣化しないことを表示し、`list` の劣化と時間は「-」「劣化なし」になる。

RCON での定期実行の例（外部スケジューラから送る）:

```text
powerplantfuse.fill
powerplantfuse.fill 18
```

## 設定

`oxide/config/PowerPlantFuseRefill.json`。ファイルがなければ初回ロード時に既定値で作成する。設定のキーと既定値は次の表を参照。

| キー | 既定値 | 範囲 | 意味 |
| --- | --- | --- | --- |
| `Auto refill enabled` | `true` | `true`/`false` | 定期的な自動補充を行う。`powerplantfuse.auto` で書き換わる。 |
| `Refill interval seconds` | `10800`（3時間） | 60〜604800 | 自動補充の間隔（秒）。 |
| `Target fuse count` | `10`（ステージ3相当） | 0〜64 | 補充後の稼働本数の目標。総ソケット数を超える分は無視する。 |
| `Refill when plugin loads (auto refill only)` | `true` | `true`/`false` | 自動補充が有効なとき、ロード時にも1回補充する。 |

- 目標本数の既定値は、指定がなかったため中間のステージ3（10本）にした。最大ステージは 18、全ソケットを埋めて最大出力にするなら 20（実機で確認した総ソケット数）。
- キーの欠損、範囲外、非有限値、壊れた JSON があった場合は、補充とコマンドを止め、エラーをログに出す。ファイルは上書きしない。修正してから `oxide.reload PowerPlantFuseRefill` を実行する。
- 永続データはない。挿入したヒューズは通常のゲーム内アイテムとしてワールドのセーブに残り、ワイプで消える。ワイプ後はロード時の補充で、自動的に目標本数まで戻る。
- 外部通信はない。
- ゲーム本体の `gameplay_analytics` が有効だと、挿入元プレイヤーがいないためゲーム側で例外ログが出る可能性がある。挿入自体はゲーム側の例外処理内で完了する。無効時の動作を確認しており、有効時は実機未確認。

## 導入・更新・撤去

1. 既存の `oxide/plugins/PowerPlantFuseRefill.cs`、`oxide/config/PowerPlantFuseRefill.json`、言語ファイルをバックアップする。
2. `PowerPlantFuseRefill.cs` を `oxide/plugins/PowerPlantFuseRefill.cs` に配置する（自動でロードされる）。更新も同名で上書きし、必要なら `oxide.reload PowerPlantFuseRefill` を実行する。
3. 必要なら設定を編集して再読み込みし、`powerplantfuse.status` で状態を確認する。

撤去は `oxide.unload PowerPlantFuseRefill` の後に本体を移動する。挿入済みのヒューズはゲーム内に残り、通常どおり劣化する。\
復元時はバックアップを戻してロードする。

## 検証

2026-11-21、Rust buildid 26479619 / Oxide.Rust 2.0.7894 を対象に、本プラグインのコンパイル・ロード・日英メッセージの登録・アンロード・再ロードの確認が成功（管理者報告）。2026-11-09 に実機で挿入・不足分のみの補充・ステージ変化・再ロード・状態表示・劣化時間の推定を確認。自動補充タイマー、セーブ・再起動後の保持、プレイヤー接続時の実行は実機で未確認。

参照:

- [Power Trip（Facepunch、2026-08-06）](https://rust.facepunch.com/news/power-trip)
- [Heavy Fuse（RustHelp、2026-11-05 データ確認）](https://rusthelp.com/items/heavy-fuse)
- [Player-Maintained Monument System（Corrosion Hour、2026-08-03 更新、プレビュー時点の情報）](https://www.corrosionhour.com/rust-player-maintained-monument-system/)
- [Oxide Configuration](https://umod.org/documentation/api/configuration)、[Permissions](https://umod.org/documentation/api/permissions)、[Localization](https://umod.org/documentation/api/localization)、[Timers](https://umod.org/documentation/api/timers)

資料の差異: Corrosion Hour（プレビュー時点）は、1階のパネルを15スロット、ステージを 1-3/4-9/10-17/18+ 本としている。RustHelp はソケットを計20、ステージの閾値を 1/4/10/18 本としている。\
総ソケット数は、実機の `powerplantfuse.status` で20と確認した（2026-11-08）。ステージの閾値は、実機で 1/4/10/18 本と確認した（2026-11-09）。
