using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("sorayume-MonumentReset", "mazurk4", "1.1.0")]
    [Description("Resets monument puzzles, loot, NPCs and locked crates on demand by chat or console command, after an optional countdown pop-up warning, for solo servers.")]
    public class MonumentReset : RustPlugin
    {
        private const string PermAdmin = "monumentreset.admin";
        private const int MaxCountdownSeconds = 120;
        private const float RefillDelaySeconds = 1f;
        private const int ListLinesPerMessage = 12;
        private const int MaxKindArguments = 8;
        private static readonly int[] AnnounceAt = { 60, 30, 10, 5, 3, 2, 1 };

        // TerrainPath.Monuments と SpawnHandler.SpawnGroups は internal（Rust buildid 26479619 で確認）。
        // Oxide の publicizer の有無にかかわらず動くようにリフレクションで読む。
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo MonumentsField = typeof(TerrainPath).GetField("Monuments", AnyInstance);
        private static readonly FieldInfo SpawnGroupsField = typeof(SpawnHandler).GetField("SpawnGroups", AnyInstance);
        private static readonly FieldInfo OneTimeSpawnerField = typeof(IndividualSpawner).GetField("oneTimeSpawner", AnyInstance);

        private PluginConfig config;
        private bool serverReady;
        private readonly Dictionary<string, PendingReset> pending = new Dictionary<string, PendingReset>(StringComparer.Ordinal);

        public class PluginConfig
        {
            [JsonProperty("Show warning")]
            public bool? ShowWarning;

            [JsonProperty("Countdown seconds")]
            public int? CountdownSeconds;

            [JsonProperty("Warn players within monument bounds only")]
            public bool? WarnInsideOnly;

            public static PluginConfig CreateDefault()
            {
                return new PluginConfig { ShowWarning = true, CountdownSeconds = 10, WarnInsideOnly = true };
            }

            public string Validate()
            {
                if (ShowWarning == null) return "\"Show warning\" is missing";
                if (CountdownSeconds == null) return "\"Countdown seconds\" is missing";
                if (WarnInsideOnly == null) return "\"Warn players within monument bounds only\" is missing";
                if (CountdownSeconds.Value < 0 || CountdownSeconds.Value > MaxCountdownSeconds)
                    return "\"Countdown seconds\" must be between 0 and " + MaxCountdownSeconds;
                return null;
            }
        }

        [Flags]
        private enum ResetKind { None = 0, Puzzle = 1, Loot = 2, Npc = 4, Crate = 8, All = Puzzle | Loot | Npc | Crate }

        private class SpawnerEntry
        {
            public ISpawnGroup Group;
            public ResetKind Kind;
            public bool Refill;
        }

        private class MonumentEntry
        {
            public string Id;
            public string Name;
            public string Grid;
            public MonumentInfo Monument;
            // カードリーダーの色（accessLevel 1=緑, 2=青, 3=赤）をビットで持つ。一覧のときだけ集計する。
            public int CardLevels;
            public readonly List<PuzzleReset> Puzzles = new List<PuzzleReset>();
            public readonly List<SpawnerEntry> Spawners = new List<SpawnerEntry>();

            public int Count(ResetKind kind)
            {
                if (kind == ResetKind.Puzzle)
                    return Puzzles.Count;
                int count = 0;
                foreach (SpawnerEntry spawner in Spawners)
                {
                    if (spawner.Kind == kind)
                        count++;
                }
                return count;
            }

            public bool Has(ResetKind kinds)
            {
                foreach (ResetKind kind in new[] { ResetKind.Puzzle, ResetKind.Loot, ResetKind.Npc, ResetKind.Crate })
                {
                    if ((kinds & kind) != 0 && Count(kind) > 0)
                        return true;
                }
                return false;
            }
        }

        private class PendingReset
        {
            public string Id;
            public string Name;
            public ResetKind Kinds;
            public int Remaining;
            public Timer Timer;
            public bool Refilling;
            public BasePlayer Requester;
            public string Source;
            public List<ISpawnGroup> ToRefill;
            public ResetResult Result;
        }

        private class ResetResult
        {
            public int Puzzles;
            public int SkippedSleepers;
            public int Loot;
            public int Npc;
            public int Crate;
            public int Respawned;
            public int Failed;
        }

        // -------------------------------------------------------------------
        // 設定（欠損・範囲外・破損時はファイルを書き換えず、コマンドを無効化する）
        // -------------------------------------------------------------------
        protected override void LoadDefaultConfig()
        {
            config = PluginConfig.CreateDefault();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            PluginConfig loaded = null;
            string error;
            try
            {
                loaded = Config.ReadObject<PluginConfig>();
                error = loaded == null ? "config is empty" : loaded.Validate();
            }
            catch (Exception ex)
            {
                error = "config could not be read (" + ex.Message + ")";
            }
            config = error == null ? loaded : null;
            if (error != null)
                PrintError("Commands are disabled until the config is fixed: " + error + ". The file was not modified.");
        }

        protected override void SaveConfig()
        {
            if (config != null)
                Config.WriteObject(config, true);
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission to use this command.",
                ["ConfigInvalid"] = "The MonumentReset config is invalid. Fix it and reload the plugin.",
                ["NotReady"] = "The server has not finished starting. Try again later.",
                ["Unsupported"] = "Monument data is not available on this Rust version. Check the server log.",
                ["ChatUsage"] = "Usage: /mreset list [all] | /mreset <id> [puzzle|loot|npc|crate|all ...] | /mreset cancel [id] | /mreset help",
                ["UsageList"] = "Usage: monumentreset.list [all]",
                ["UsageRun"] = "Usage: monumentreset.run <id> [puzzle|loot|npc|crate|all ...]",
                ["UsageCancel"] = "Usage: monumentreset.cancel [id]",
                ["HelpChat"] = "MonumentReset commands:\n" +
                    "/mreset list - monuments with keycard readers\n" +
                    "/mreset list all - every resettable monument\n" +
                    "/mreset <id> [puzzle|loot|npc|crate|all ...] - reset after a countdown (default: all)\n" +
                    "/mreset cancel [id] - cancel a scheduled reset (no id: all)\n" +
                    "/mreset help - show this help",
                ["HelpConsole"] = "MonumentReset commands:\n" +
                    "monumentreset.list - monuments with keycard readers\n" +
                    "monumentreset.list all - every resettable monument\n" +
                    "monumentreset.run <id> [puzzle|loot|npc|crate|all ...] - reset after a countdown (default: all)\n" +
                    "monumentreset.cancel [id] - cancel a scheduled reset (no id: all)\n" +
                    "monumentreset.help - show this help",
                ["UnknownKind"] = "Unknown reset type: {0}. Use puzzle, loot, npc, crate or all.",
                ["UnknownMonument"] = "Monument '{0}' was not found. Use list to see the IDs.",
                ["CardListHeader"] = "Keycard monuments: {0}. Columns: ID | name | grid | puzzle/loot/NPC/crate | card readers. Use 'list all' for every monument.",
                ["FullListHeader"] = "All monuments: {0}. Columns: ID | name | grid | puzzle/loot/NPC/crate | card readers",
                ["MonumentRow"] = "{0} | {1} | {2} | {3}/{4}/{5}/{6} | {7}",
                ["CardGreen"] = "green",
                ["CardBlue"] = "blue",
                ["CardRed"] = "red",
                ["CardNone"] = "-",
                ["CardListNone"] = "No resettable monuments with keycard readers were found. Use 'list all' for every monument.",
                ["ListNone"] = "No resettable monuments were found on this map.",
                ["NothingToReset"] = "{0} has nothing to reset for: {1}.",
                ["AlreadyPending"] = "{0} is already scheduled or resetting.",
                ["Scheduled"] = "{0} will reset in {1} s ({2}). To stop it, cancel {3}.",
                ["ToastWarning"] = "{0} resets in {1} s. Loot, NPCs and puzzles will be replaced.",
                ["ToastResetting"] = "{0} is resetting now.",
                ["Resetting"] = "{0} is resetting now.",
                ["CancelledOne"] = "Cancelled the reset of {0}.",
                ["CancelledAll"] = "Cancelled {0} scheduled reset(s).",
                ["NothingToCancel"] = "No reset is waiting to be cancelled.",
                ["NotCancellable"] = "{0} is already resetting and cannot be cancelled.",
                ["MonumentGone"] = "The reset of {0} was skipped because the monument is no longer available.",
                ["Done"] = "{0} was reset ({1}). Puzzles: {2}. Spawners cleared: loot {3}, NPC {4}, crate {5}. Respawned: {6}.",
                ["SkippedSleepers"] = "{0} puzzle(s) were skipped because a sleeping player is nearby (the game would kill them).",
                ["Failures"] = "{0} step(s) failed. Check the server log."
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "このコマンドを実行する権限がありません。",
                ["ConfigInvalid"] = "MonumentReset の設定が不正です。修正してからプラグインを再読み込みしてください。",
                ["NotReady"] = "サーバーの起動が完了していません。しばらくしてから再実行してください。",
                ["Unsupported"] = "この Rust の版ではモニュメントの情報を取得できません。サーバーログを確認してください。",
                ["ChatUsage"] = "使用法: /mreset list [all] | /mreset <ID> [puzzle|loot|npc|crate|all ...] | /mreset cancel [ID] | /mreset help",
                ["UsageList"] = "使用法: monumentreset.list [all]",
                ["UsageRun"] = "使用法: monumentreset.run <ID> [puzzle|loot|npc|crate|all ...]",
                ["UsageCancel"] = "使用法: monumentreset.cancel [ID]",
                ["HelpChat"] = "MonumentReset のコマンド:\n" +
                    "/mreset list - カードリーダーのあるモニュメントの一覧\n" +
                    "/mreset list all - リセットできる全モニュメントの一覧\n" +
                    "/mreset <ID> [puzzle|loot|npc|crate|all ...] - 秒読みのあとにリセット（省略時はすべて）\n" +
                    "/mreset cancel [ID] - 秒読み中のリセットを取り消す（ID 省略ですべて）\n" +
                    "/mreset help - このヘルプを表示",
                ["HelpConsole"] = "MonumentReset のコマンド:\n" +
                    "monumentreset.list - カードリーダーのあるモニュメントの一覧\n" +
                    "monumentreset.list all - リセットできる全モニュメントの一覧\n" +
                    "monumentreset.run <ID> [puzzle|loot|npc|crate|all ...] - 秒読みのあとにリセット（省略時はすべて）\n" +
                    "monumentreset.cancel [ID] - 秒読み中のリセットを取り消す（ID 省略ですべて）\n" +
                    "monumentreset.help - このヘルプを表示",
                ["UnknownKind"] = "不明なリセット種別です: {0}。puzzle、loot、npc、crate、all のいずれかを指定してください。",
                ["UnknownMonument"] = "モニュメント '{0}' が見つかりません。list で ID を確認してください。",
                ["CardListHeader"] = "カードのモニュメント: {0} 件。列: ID | 名前 | グリッド | パズル/ルート/NPC/特殊箱 | カードリーダー。全件は list all。",
                ["FullListHeader"] = "全モニュメント: {0} 件。列: ID | 名前 | グリッド | パズル/ルート/NPC/特殊箱 | カードリーダー",
                ["MonumentRow"] = "{0} | {1} | {2} | {3}/{4}/{5}/{6} | {7}",
                ["CardGreen"] = "緑",
                ["CardBlue"] = "青",
                ["CardRed"] = "赤",
                ["CardNone"] = "-",
                ["CardListNone"] = "カードリーダーのあるリセット可能なモニュメントはありません。全件は list all で表示します。",
                ["ListNone"] = "このマップにリセットできるモニュメントはありません。",
                ["NothingToReset"] = "{0} にはリセットする対象がありません（{1}）。",
                ["AlreadyPending"] = "{0} はすでにリセットの予約中、またはリセット中です。",
                ["Scheduled"] = "{0} を {1} 秒後にリセットします（{2}）。取り消すには cancel {3}。",
                ["ToastWarning"] = "{0} は {1} 秒後にリセットされます。ルート・NPC・パズルが入れ替わります。",
                ["ToastResetting"] = "{0} をリセットします。",
                ["Resetting"] = "{0} をリセットします。",
                ["CancelledOne"] = "{0} のリセットを取り消しました。",
                ["CancelledAll"] = "予約中のリセットを {0} 件取り消しました。",
                ["NothingToCancel"] = "取り消せるリセットはありません。",
                ["NotCancellable"] = "{0} はリセット中のため取り消せません。",
                ["MonumentGone"] = "{0} が見つからなくなったため、リセットを中止しました。",
                ["Done"] = "{0} をリセットしました（{1}）。パズル: {2}。消去したスポナー: ルート {3}、NPC {4}、特殊箱 {5}。再出現: {6}。",
                ["SkippedSleepers"] = "近くに寝ているプレイヤーがいるため、パズル {0} 件をスキップしました（ゲームの処理で死亡するため）。",
                ["Failures"] = "{0} 件の処理に失敗しました。サーバーログを確認してください。"
            }, this, "ja");
        }

        private string Message(string key, BasePlayer player, params object[] args)
        {
            string message = lang.GetMessage(key, this, player?.UserIDString);
            return args.Length == 0 ? message : string.Format(message, args);
        }

        // -------------------------------------------------------------------
        // ライフサイクル
        // -------------------------------------------------------------------
        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
        }

        private void OnServerInitialized()
        {
            serverReady = true;
            if (MonumentsField == null || SpawnGroupsField == null)
                PrintError("TerrainPath.Monuments or SpawnHandler.SpawnGroups was not found; resets are unavailable on this Rust version.");
            if (OneTimeSpawnerField == null)
                PrintWarning("IndividualSpawner.oneTimeSpawner was not found; single-entity spawners will not be reset.");
        }

        private void Unload()
        {
            foreach (PendingReset entry in new List<PendingReset>(pending.Values))
            {
                entry.Timer?.Destroy();
                // 消去済みで再出現待ちのものは、空のまま残さないようにその場で補充する。
                if (entry.Refilling)
                    FinishRefill(entry);
            }
            pending.Clear();
        }

        // -------------------------------------------------------------------
        // モニュメントと対象の一覧（コマンド実行時のみ作る。高頻度フックでは使わない）
        // -------------------------------------------------------------------
        private List<MonumentEntry> BuildCatalog(bool withCardReaders = false)
        {
            if (TerrainMeta.Path == null || MonumentsField == null || SpawnGroupsField == null)
                return null;
            IEnumerable monumentList = MonumentsField.GetValue(TerrainMeta.Path) as IEnumerable;
            if (monumentList == null)
                return null;

            List<MonumentInfo> monuments = new List<MonumentInfo>();
            foreach (object value in monumentList)
            {
                MonumentInfo monument = value as MonumentInfo;
                if (monument != null)
                    monuments.Add(monument);
            }
            monuments.Sort((a, b) =>
            {
                Vector3 pa = a.transform.position, pb = b.transform.position;
                return pa.x != pb.x ? pa.x.CompareTo(pb.x) : pa.z.CompareTo(pb.z);
            });

            List<MonumentEntry> entries = new List<MonumentEntry>(monuments.Count);
            Dictionary<MonumentInfo, MonumentEntry> byMonument = new Dictionary<MonumentInfo, MonumentEntry>();
            Dictionary<string, int> baseCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> baseIds = new List<string>(monuments.Count);
            foreach (MonumentInfo monument in monuments)
            {
                string baseId = BaseId(monument);
                baseIds.Add(baseId);
                baseCounts[baseId] = baseCounts.TryGetValue(baseId, out int n) ? n + 1 : 1;
            }
            Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < monuments.Count; i++)
            {
                string baseId = baseIds[i];
                string id = baseId;
                if (baseCounts[baseId] > 1)
                {
                    int index = seen.TryGetValue(baseId, out int s) ? s + 1 : 1;
                    seen[baseId] = index;
                    id = baseId + "-" + index;
                }
                MonumentEntry entry = new MonumentEntry
                {
                    Id = id,
                    Monument = monuments[i],
                    Name = DisplayName(monuments[i], baseId),
                    Grid = MapHelper.PositionToString(monuments[i].transform.position)
                };
                entries.Add(entry);
                byMonument[monuments[i]] = entry;
            }

            HashSet<SpawnGroup> puzzleGroups = new HashSet<SpawnGroup>();
            foreach (PuzzleReset reset in PuzzleReset.AllResets)
            {
                if (reset == null)
                    continue;
                MonumentEntry owner = FindOwner(reset.GetComponentInParent<MonumentInfo>(), reset.transform.position, entries, byMonument);
                if (owner == null)
                    continue;
                owner.Puzzles.Add(reset);
                List<SpawnGroup> groups = reset.GetSpawnGroups();
                if (groups != null)
                {
                    foreach (SpawnGroup group in groups)
                    {
                        if (group != null)
                            puzzleGroups.Add(group);
                    }
                }
            }

            if (withCardReaders)
                AssignCardReaders(entries, byMonument);

            IEnumerable spawnGroups = SingletonComponent<SpawnHandler>.Instance == null
                ? null
                : SpawnGroupsField.GetValue(SingletonComponent<SpawnHandler>.Instance) as IEnumerable;
            if (spawnGroups == null)
                return entries;
            foreach (object value in spawnGroups)
            {
                SpawnerEntry spawner;
                Vector3 position;
                MonumentInfo parent;
                if (value is SpawnGroup group)
                {
                    if (group == null || puzzleGroups.Contains(group))
                        continue;
                    spawner = new SpawnerEntry { Group = group, Kind = ClassifyGroup(group), Refill = group.WantsInitialSpawn() };
                    position = group.transform.position;
                    parent = group.Monument != null ? group.Monument : group.GetComponentInParent<MonumentInfo>();
                }
                else if (value is IndividualSpawner individual)
                {
                    // 一度だけ出すスポナーは Fill で戻らないので触らない。判定できない版では対象外にする。
                    if (individual == null || OneTimeSpawnerField == null || !(OneTimeSpawnerField.GetValue(individual) is bool oneTime) || oneTime)
                        continue;
                    spawner = new SpawnerEntry { Group = individual, Kind = Classify(SafeEntity(individual.entityPrefab)), Refill = true };
                    position = individual.transform.position;
                    parent = individual.GetComponentInParent<MonumentInfo>();
                }
                else
                {
                    continue;
                }
                if (spawner.Kind == ResetKind.None)
                    continue;
                // 地下トンネル・海底ラボは対象外（仕様）。
                if (EnvironmentManager.Check(position, EnvironmentType.TrainTunnels | EnvironmentType.UnderwaterLab))
                    continue;
                MonumentEntry owner = FindOwner(parent, position, entries, byMonument);
                if (owner != null)
                    owner.Spawners.Add(spawner);
            }
            return entries;
        }

        // 一覧の実行時だけ全エンティティを1回走査する（高頻度の処理からは呼ばない）。
        private static void AssignCardReaders(List<MonumentEntry> entries, Dictionary<MonumentInfo, MonumentEntry> byMonument)
        {
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                CardReader reader = entity as CardReader;
                if (reader == null || reader.IsDestroyed || reader.accessLevel < 1 || reader.accessLevel > 3)
                    continue;
                Vector3 position = reader.transform.position;
                // スポナーと同じく、地下トンネル・海底ラボは対象外。
                if (EnvironmentManager.Check(position, EnvironmentType.TrainTunnels | EnvironmentType.UnderwaterLab))
                    continue;
                MonumentEntry owner = FindOwner(reader.GetComponentInParent<MonumentInfo>(), position, entries, byMonument);
                if (owner != null)
                    owner.CardLevels |= 1 << reader.accessLevel;
            }
        }

        private string CardsText(int levels, BasePlayer player)
        {
            List<string> names = new List<string>(3);
            if ((levels & (1 << 1)) != 0) names.Add(Message("CardGreen", player));
            if ((levels & (1 << 2)) != 0) names.Add(Message("CardBlue", player));
            if ((levels & (1 << 3)) != 0) names.Add(Message("CardRed", player));
            return names.Count == 0 ? Message("CardNone", player) : string.Join(", ", names);
        }

        private static MonumentEntry FindOwner(MonumentInfo parent, Vector3 position, List<MonumentEntry> entries, Dictionary<MonumentInfo, MonumentEntry> byMonument)
        {
            if (parent != null && byMonument.TryGetValue(parent, out MonumentEntry direct))
                return direct;
            // 親のない物は範囲に入るモニュメントのうち、最も小さいものに属させる。
            MonumentEntry best = null;
            float bestVolume = float.MaxValue;
            foreach (MonumentEntry entry in entries)
            {
                if (!entry.Monument.IsInBounds(position))
                    continue;
                Vector3 size = entry.Monument.Bounds.size;
                float volume = Math.Abs(size.x * size.y * size.z);
                if (volume < bestVolume)
                {
                    bestVolume = volume;
                    best = entry;
                }
            }
            return best;
        }

        private static string BaseId(MonumentInfo monument)
        {
            string raw = monument.name;
            if (string.IsNullOrEmpty(raw) && monument.displayPhrase != null)
                raw = monument.displayPhrase.token;
            if (string.IsNullOrEmpty(raw))
                raw = "monument";
            int slash = raw.LastIndexOf('/');
            if (slash >= 0)
                raw = raw.Substring(slash + 1);
            if (raw.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(0, raw.Length - ".prefab".Length);
            StringBuilder builder = new StringBuilder(raw.Length);
            foreach (char c in raw.ToLowerInvariant())
                builder.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' ? c : '_');
            return builder.Length == 0 ? "monument" : builder.ToString();
        }

        private static string DisplayName(MonumentInfo monument, string fallback)
        {
            if (monument.displayPhrase != null && !string.IsNullOrEmpty(monument.displayPhrase.english))
                return monument.displayPhrase.english;
            return fallback;
        }

        private static BaseEntity SafeEntity(GameObjectRef prefab)
        {
            try
            {
                return prefab?.GetEntity();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static ResetKind Classify(BaseEntity entity)
        {
            if (entity == null) return ResetKind.None;
            if (entity is HackableLockedCrate) return ResetKind.Crate;
            if (entity.IsNpc) return ResetKind.Npc;
            if (entity is LootContainer) return ResetKind.Loot;
            return ResetKind.None;
        }

        // 1 つのグループに複数種が混在する場合は NPC > 特殊箱 > ルートの順に分類する。
        private static ResetKind ClassifyGroup(SpawnGroup group)
        {
            ResetKind found = ResetKind.None;
            if (group.prefabs == null)
                return found;
            foreach (SpawnGroup.SpawnEntry entry in group.prefabs)
                found |= Classify(SafeEntity(entry?.prefab));
            if ((found & ResetKind.Npc) != 0) return ResetKind.Npc;
            if ((found & ResetKind.Crate) != 0) return ResetKind.Crate;
            return found;
        }

        private static MonumentEntry Find(List<MonumentEntry> entries, string id)
        {
            foreach (MonumentEntry entry in entries)
            {
                if (string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }
            return null;
        }

        // -------------------------------------------------------------------
        // 入力の解釈
        // -------------------------------------------------------------------
        private static bool TryParseKinds(string[] args, int start, out ResetKind kinds, out string invalid)
        {
            kinds = ResetKind.None;
            invalid = null;
            if (args.Length - start > MaxKindArguments)
            {
                invalid = args[start + MaxKindArguments];
                return false;
            }
            for (int i = start; i < args.Length; i++)
            {
                switch ((args[i] ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "puzzle": kinds |= ResetKind.Puzzle; break;
                    case "loot": kinds |= ResetKind.Loot; break;
                    case "npc": kinds |= ResetKind.Npc; break;
                    case "crate": kinds |= ResetKind.Crate; break;
                    case "all": kinds |= ResetKind.All; break;
                    default:
                        invalid = args[i];
                        return false;
                }
            }
            if (kinds == ResetKind.None)
                kinds = ResetKind.All;
            return true;
        }

        private static string KindsText(ResetKind kinds)
        {
            List<string> names = new List<string>();
            if ((kinds & ResetKind.Puzzle) != 0) names.Add("puzzle");
            if ((kinds & ResetKind.Loot) != 0) names.Add("loot");
            if ((kinds & ResetKind.Npc) != 0) names.Add("npc");
            if ((kinds & ResetKind.Crate) != 0) names.Add("crate");
            return string.Join(", ", names);
        }

        // 入力を返信に含めるときは、チャットのリッチテキストとして解釈されないよう山括弧を除き、長さを制限する。
        private static string Truncate(string value, int max)
        {
            if (value == null) return string.Empty;
            value = value.Replace("<", string.Empty).Replace(">", string.Empty);
            return value.Length <= max ? value : value.Substring(0, max);
        }

        // -------------------------------------------------------------------
        // 処理本体（チャット・コンソール共通）
        // -------------------------------------------------------------------
        private void HandleList(BasePlayer player, Action<string> reply, bool all)
        {
            List<MonumentEntry> catalog = BuildCatalog(true);
            if (catalog == null)
            {
                reply(Message("Unsupported", player));
                return;
            }
            List<string> rows = new List<string>();
            foreach (MonumentEntry entry in catalog)
            {
                if (!entry.Has(ResetKind.All) || (!all && entry.CardLevels == 0))
                    continue;
                rows.Add(Message("MonumentRow", player, entry.Id, entry.Name, entry.Grid,
                    entry.Count(ResetKind.Puzzle), entry.Count(ResetKind.Loot), entry.Count(ResetKind.Npc), entry.Count(ResetKind.Crate),
                    CardsText(entry.CardLevels, player)));
            }
            if (rows.Count == 0)
            {
                reply(Message(all ? "ListNone" : "CardListNone", player));
                return;
            }
            StringBuilder chunk = new StringBuilder(Message(all ? "FullListHeader" : "CardListHeader", player, rows.Count));
            int lines = 1;
            foreach (string row in rows)
            {
                if (lines >= ListLinesPerMessage)
                {
                    reply(chunk.ToString());
                    chunk.Clear();
                    lines = 0;
                }
                if (chunk.Length > 0)
                    chunk.Append('\n');
                chunk.Append(row);
                lines++;
            }
            reply(chunk.ToString());
        }

        private void HandleRun(BasePlayer player, Action<string> reply, string id, ResetKind kinds, string source)
        {
            List<MonumentEntry> catalog = BuildCatalog();
            if (catalog == null)
            {
                reply(Message("Unsupported", player));
                return;
            }
            MonumentEntry entry = Find(catalog, id);
            if (entry == null)
            {
                reply(Message("UnknownMonument", player, Truncate(id, 64)));
                return;
            }
            if (pending.ContainsKey(entry.Id))
            {
                reply(Message("AlreadyPending", player, entry.Id));
                return;
            }
            if (!entry.Has(kinds))
            {
                reply(Message("NothingToReset", player, entry.Id, KindsText(kinds)));
                return;
            }

            PendingReset request = new PendingReset
            {
                Id = entry.Id,
                Name = entry.Name,
                Kinds = kinds,
                Remaining = config.CountdownSeconds.Value,
                Requester = player,
                Source = source
            };
            pending[entry.Id] = request;
            Puts($"{source} scheduled a reset of {entry.Id} ({KindsText(kinds)}) in {request.Remaining} s.");
            if (request.Remaining <= 0)
            {
                reply(Message("Resetting", player, entry.Name));
                Execute(request);
                return;
            }
            reply(Message("Scheduled", player, entry.Id, request.Remaining, KindsText(kinds), entry.Id));
            Warn(entry, request.Remaining, "ToastWarning");
            request.Timer = timer.Every(1f, () => Tick(request));
        }

        private void Tick(PendingReset request)
        {
            if (!pending.TryGetValue(request.Id, out PendingReset current) || current != request)
            {
                request.Timer?.Destroy();
                return;
            }
            request.Remaining--;
            if (request.Remaining <= 0)
            {
                request.Timer?.Destroy();
                request.Timer = null;
                Execute(request);
                return;
            }
            if (Array.IndexOf(AnnounceAt, request.Remaining) >= 0)
            {
                List<MonumentEntry> catalog = BuildCatalog();
                MonumentEntry entry = catalog == null ? null : Find(catalog, request.Id);
                if (entry != null)
                    Warn(entry, request.Remaining, "ToastWarning");
            }
        }

        private void HandleCancel(BasePlayer player, Action<string> reply, string id)
        {
            if (id != null)
            {
                PendingReset match = null;
                foreach (PendingReset entry in pending.Values)
                {
                    if (string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                        match = entry;
                }
                if (match == null)
                {
                    reply(Message("NothingToCancel", player));
                    return;
                }
                if (match.Refilling)
                {
                    reply(Message("NotCancellable", player, match.Id));
                    return;
                }
                match.Timer?.Destroy();
                pending.Remove(match.Id);
                Puts($"Reset of {match.Id} was cancelled.");
                reply(Message("CancelledOne", player, match.Id));
                return;
            }
            int cancelled = 0;
            foreach (PendingReset entry in new List<PendingReset>(pending.Values))
            {
                if (entry.Refilling)
                    continue;
                entry.Timer?.Destroy();
                pending.Remove(entry.Id);
                cancelled++;
            }
            if (cancelled == 0)
            {
                reply(Message("NothingToCancel", player));
                return;
            }
            Puts($"{cancelled} scheduled reset(s) were cancelled.");
            reply(Message("CancelledAll", player, cancelled));
        }

        // サーバー再起動の通知と同じ Server_Event のポップアップ（トースト）で知らせる。
        // トークンを空にすると、クライアントは翻訳を引かずに渡した文をそのまま表示する（Translate.Phrase.translated）。
        private void Warn(MonumentEntry entry, int seconds, string key)
        {
            if (config == null || config.ShowWarning != true)
                return;
            foreach (BasePlayer target in new List<BasePlayer>(BasePlayer.activePlayerList))
            {
                if (target == null || !target.IsConnected)
                    continue;
                if (config.WarnInsideOnly == true && !entry.Monument.IsInBounds(target.transform.position))
                    continue;
                target.ShowToast(GameTip.Styles.Server_Event, new Translate.Phrase(string.Empty, Message(key, target, entry.Name, seconds)), false);
            }
        }

        private void Execute(PendingReset request)
        {
            // 秒読みの間に参照が変わる可能性があるため、実行時に一覧を作り直す。
            List<MonumentEntry> catalog = BuildCatalog();
            MonumentEntry entry = catalog == null ? null : Find(catalog, request.Id);
            if (entry == null)
            {
                pending.Remove(request.Id);
                PrintWarning($"Reset of {request.Id} was skipped: the monument is no longer available.");
                Notify(request, Message("MonumentGone", request.Requester, request.Id));
                return;
            }
            Warn(entry, 0, "ToastResetting");

            ResetResult result = new ResetResult();
            if ((request.Kinds & ResetKind.Puzzle) != 0)
            {
                foreach (PuzzleReset reset in entry.Puzzles)
                {
                    if (HasSleeperNear(reset))
                    {
                        result.SkippedSleepers++;
                        continue;
                    }
                    try
                    {
                        // skipAlerts: true でバニラのトースト・オイルリグ通知を抑止し、プラグインの lang で通知する。
                        reset.DoReset(true);
                        reset.ResetTimer();
                        result.Puzzles++;
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        PrintError($"PuzzleReset at {request.Id} failed: {ex.Message}");
                    }
                }
            }

            List<ISpawnGroup> toRefill = new List<ISpawnGroup>();
            foreach (SpawnerEntry spawner in entry.Spawners)
            {
                if ((request.Kinds & spawner.Kind) == 0)
                    continue;
                try
                {
                    spawner.Group.Clear();
                    if (spawner.Refill)
                        toRefill.Add(spawner.Group);
                    if (spawner.Kind == ResetKind.Loot) result.Loot++;
                    else if (spawner.Kind == ResetKind.Npc) result.Npc++;
                    else if (spawner.Kind == ResetKind.Crate) result.Crate++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    PrintError($"Clearing a spawner at {request.Id} failed: {ex.Message}");
                }
            }

            // 消去したエンティティの破棄はフレーム末に行われるため、バニラ（PuzzleReset.DoReset）と同じく 1 秒後に補充する。
            request.Refilling = true;
            request.ToRefill = toRefill;
            request.Result = result;
            request.Timer = timer.Once(RefillDelaySeconds, () => FinishRefill(request));
        }

        private void FinishRefill(PendingReset request)
        {
            if (!request.Refilling)
                return;
            request.Refilling = false;
            request.Timer = null;
            if (pending.TryGetValue(request.Id, out PendingReset current) && current == request)
                pending.Remove(request.Id);

            ResetResult result = request.Result ?? new ResetResult();
            foreach (ISpawnGroup group in request.ToRefill ?? new List<ISpawnGroup>())
            {
                try
                {
                    group.Fill();
                    result.Respawned += group.currentPopulation;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    PrintError($"Refilling a spawner at {request.Id} failed: {ex.Message}");
                }
            }

            Puts($"Reset of {request.Id} ({KindsText(request.Kinds)}) by {request.Source}: puzzles {result.Puzzles}, " +
                 $"skipped (sleeper) {result.SkippedSleepers}, cleared loot {result.Loot} / npc {result.Npc} / crate {result.Crate}, " +
                 $"respawned {result.Respawned}, failed {result.Failed}.");
            BasePlayer player = request.Requester;
            string text = Message("Done", player, request.Id, KindsText(request.Kinds), result.Puzzles, result.Loot, result.Npc, result.Crate, result.Respawned);
            if (result.SkippedSleepers > 0)
                text += "\n" + Message("SkippedSleepers", player, result.SkippedSleepers);
            if (result.Failed > 0)
                text += "\n" + Message("Failures", player, result.Failed);
            Notify(request, text);
        }

        // PuzzleReset.CleanupSleepers は範囲内の寝ているプレイヤーを即死させるため、その場合は実行しない。
        private static bool HasSleeperNear(PuzzleReset reset)
        {
            Transform origin = reset.playerDetectionOrigin;
            if (origin == null)
                return false;
            foreach (BasePlayer sleeper in BasePlayer.sleepingPlayerList)
            {
                if (sleeper != null && sleeper.IsSleeping() && Vector3.Distance(sleeper.transform.position, origin.position) <= reset.playerDetectionRadius)
                    return true;
            }
            return false;
        }

        private void Notify(PendingReset request, string text)
        {
            BasePlayer player = request.Requester;
            if (player != null && player.IsConnected)
                player.ChatMessage(text);
        }

        // -------------------------------------------------------------------
        // 入口（チャット・サーバーコンソール・RCON・F1 コンソール）
        // -------------------------------------------------------------------
        private bool CheckReady(BasePlayer player, Action<string> reply)
        {
            if (config == null)
            {
                reply(Message("ConfigInvalid", player));
                return false;
            }
            if (!serverReady)
            {
                reply(Message("NotReady", player));
                return false;
            }
            return true;
        }

        private bool HasAccess(BasePlayer player)
        {
            return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin));
        }

        [ChatCommand("mreset")]
        private void ResetChatCommand(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;
            Action<string> reply = message => SendReply(player, message);
            if (!HasAccess(player))
            {
                reply(Message("NoPermission", player));
                return;
            }
            args = args ?? Array.Empty<string>();
            // ヘルプは設定不正・起動前でも表示する（状態を読まず、何も変更しない）。
            if (args.Length == 1 && string.Equals((args[0] ?? string.Empty).Trim(), "help", StringComparison.OrdinalIgnoreCase))
            {
                reply(Message("HelpChat", player));
                return;
            }
            if (!CheckReady(player, reply))
                return;
            if (args.Length == 0)
            {
                reply(Message("ChatUsage", player));
                return;
            }
            string first = (args[0] ?? string.Empty).Trim();
            if (string.Equals(first, "list", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length > 2 || (args.Length == 2 && !IsAll(args[1])))
                    reply(Message("ChatUsage", player));
                else
                    HandleList(player, reply, args.Length == 2);
                return;
            }
            if (string.Equals(first, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length > 2)
                    reply(Message("ChatUsage", player));
                else
                    HandleCancel(player, reply, args.Length == 2 ? args[1] : null);
                return;
            }
            if (!TryParseKinds(args, 1, out ResetKind kinds, out string invalid))
            {
                reply(Message("UnknownKind", player, Truncate(invalid, 32)));
                return;
            }
            HandleRun(player, reply, first, kinds, "Player " + player.UserIDString);
        }

        private bool CanUseConsole(ConsoleSystem.Arg arg, out BasePlayer player, bool requireReady = true)
        {
            player = null;
            if (arg.Connection != null)
            {
                player = arg.Player();
                if (!HasAccess(player))
                {
                    arg.ReplyWith(Message("NoPermission", player));
                    return false;
                }
            }
            BasePlayer requester = player;
            return !requireReady || CheckReady(requester, message => arg.ReplyWith(message));
        }

        private static bool IsAll(string value)
        {
            return string.Equals((value ?? string.Empty).Trim(), "all", StringComparison.OrdinalIgnoreCase);
        }

        // ConsoleSystem.Arg.Args は版によって型が違う（StringView[] など）ため、GetString で読む。
        // 上限を超える引数は TryParseKinds で不正として扱えるよう、上限 + 1 個まで取り出す。
        private static string[] ArgsOf(ConsoleSystem.Arg arg)
        {
            const int limit = MaxKindArguments + 2;
            int count = 0;
            while (count < limit && arg.HasArgs(count + 1))
                count++;
            string[] args = new string[count];
            for (int i = 0; i < count; i++)
                args[i] = arg.GetString(i, string.Empty);
            return args;
        }

        [ConsoleCommand("monumentreset.list")]
        private void ListConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg, out BasePlayer player))
                return;
            string[] args = ArgsOf(arg);
            if (args.Length > 1 || (args.Length == 1 && !IsAll(args[0])))
            {
                arg.ReplyWith(Message("UsageList", player));
                return;
            }
            List<string> lines = new List<string>();
            HandleList(player, lines.Add, args.Length == 1);
            arg.ReplyWith(string.Join("\n", lines));
        }

        [ConsoleCommand("monumentreset.help")]
        private void HelpConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg, out BasePlayer player, requireReady: false))
                return;
            arg.ReplyWith(Message("HelpConsole", player));
        }

        [ConsoleCommand("monumentreset.run")]
        private void RunConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg, out BasePlayer player))
                return;
            string[] args = ArgsOf(arg);
            if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
            {
                arg.ReplyWith(Message("UsageRun", player));
                return;
            }
            if (!TryParseKinds(args, 1, out ResetKind kinds, out string invalid))
            {
                arg.ReplyWith(Message("UnknownKind", player, Truncate(invalid, 32)));
                return;
            }
            List<string> lines = new List<string>();
            HandleRun(player, lines.Add, args[0].Trim(), kinds, player == null ? "Console" : "Player " + player.UserIDString);
            if (lines.Count > 0)
                arg.ReplyWith(string.Join("\n", lines));
        }

        [ConsoleCommand("monumentreset.cancel")]
        private void CancelConsoleCommand(ConsoleSystem.Arg arg)
        {
            if (!CanUseConsole(arg, out BasePlayer player))
                return;
            string[] args = ArgsOf(arg);
            if (args.Length > 1)
            {
                arg.ReplyWith(Message("UsageCancel", player));
                return;
            }
            List<string> lines = new List<string>();
            HandleCancel(player, lines.Add, args.Length == 1 ? args[0] : null);
            arg.ReplyWith(string.Join("\n", lines));
        }
    }
}
