using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("sorayume-LighthouseAutoRepair", "mazurk4", "1.0.0")]
    [Description("Automatically repairs the lighthouses (which open the Deep Sea) if nobody has repaired them within a configurable time after the wipe, for solo servers.")]
    public class LighthouseAutoRepair : RustPlugin
    {
        private const string PermAdmin = "lighthouseautorepair.admin";
        private const double MaxHoursAfterWipe = 720;
        private const float CheckIntervalSeconds = 60f;
        // SaveRestore.SaveCreatedTime is default(DateTime) until a save is created or loaded.
        private static readonly DateTime EarliestValidWipe = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Replaceable clock for the test doubles.
        private static Func<DateTime> utcNow = () => DateTime.UtcNow;

        private PluginConfig config;
        private Timer checkTimer;
        private bool serverReady;

        public class PluginConfig
        {
            [JsonProperty("Auto repair enabled")]
            public bool? AutoRepairEnabled;

            [JsonProperty("Hours after wipe before auto repair")]
            public double? HoursAfterWipe;

            [JsonProperty("Announce auto repair in chat")]
            public bool? AnnounceInChat;

            public static PluginConfig CreateDefault()
            {
                return new PluginConfig
                {
                    AutoRepairEnabled = true,
                    HoursAfterWipe = 24,
                    AnnounceInChat = true
                };
            }

            public string Validate()
            {
                if (AutoRepairEnabled == null) return "\"Auto repair enabled\" is missing";
                if (HoursAfterWipe == null) return "\"Hours after wipe before auto repair\" is missing";
                if (AnnounceInChat == null) return "\"Announce auto repair in chat\" is missing";
                double hours = HoursAfterWipe.Value;
                if (double.IsNaN(hours) || double.IsInfinity(hours) || hours < 0 || hours > MaxHoursAfterWipe)
                    return "\"Hours after wipe before auto repair\" must be between 0 and 720";
                return null;
            }
        }

        private enum LighthouseState { NotRepaired, Repaired, NoLighthouses, DeepSeaDisabled }

        private enum RepairResult { Repaired, AlreadyRepaired, NoLighthouses, DeepSeaDisabled, Failed }

        // -------------------------------------------------------------------
        // 設定（欠損・範囲外・破損時はファイルを書き換えず、自動修理を停止する）
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
                PrintError("Auto repair is disabled until the config is fixed: " + error + ". The file was not modified.");
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
                ["ConfigInvalid"] = "The LighthouseAutoRepair config is invalid. Fix it and reload the plugin.",
                ["NotReady"] = "The server has not finished starting. Try again later.",
                ["UsageStatus"] = "Usage: lighthouserepair.status",
                ["UsageRepair"] = "Usage: lighthouserepair.repair",
                ["Broadcast"] = "The lighthouse was not repaired in time, so it has been repaired automatically. The Deep Sea will open on its normal schedule.",
                ["Repaired"] = "The lighthouse has been repaired. The Deep Sea will open on its normal schedule.",
                ["AlreadyRepaired"] = "The lighthouse is already repaired. Nothing was changed.",
                ["NoLighthouses"] = "This map has no repairable lighthouse. The Deep Sea does not wait for a lighthouse.",
                ["DeepSeaDisabled"] = "The Deep Sea is disabled on this server (deepsea.enabled). Lighthouses are not used.",
                ["Failed"] = "The lighthouse could not be repaired. Check the server log.",
                ["StateRepaired"] = "Lighthouse: repaired.",
                ["StateNotRepaired"] = "Lighthouse: not repaired ({0} lighthouse(s) on this map).",
                ["Wipe"] = "Wipe (save created): {0}.",
                ["WipeUnknown"] = "Wipe time: unknown. Automatic repair will not run.",
                ["AutoOn"] = "Automatic repair: on, {0} h after wipe ({1}).",
                ["AutoOff"] = "Automatic repair: off ({0} h after wipe).",
                ["Remaining"] = "in ~{0}h {1:00}m",
                ["Due"] = "due now",
                ["DeepSeaWaiting"] = "Deep Sea: waiting for the lighthouse.",
                ["DeepSeaOpen"] = "Deep Sea: open.",
                ["DeepSeaClosed"] = "Deep Sea: closed, next opening in ~{0}h {1:00}m.",
                ["DeepSeaMissing"] = "Deep Sea: not created on this server."
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "このコマンドを実行する権限がありません。",
                ["ConfigInvalid"] = "LighthouseAutoRepair の設定が不正です。修正してからプラグインを再読み込みしてください。",
                ["NotReady"] = "サーバーの起動が完了していません。しばらくしてから再実行してください。",
                ["UsageStatus"] = "使用法: lighthouserepair.status",
                ["UsageRepair"] = "使用法: lighthouserepair.repair",
                ["Broadcast"] = "期限までに灯台が修理されなかったため、自動で修理しました。ディープシーは通常どおりの周期で開きます。",
                ["Repaired"] = "灯台を修理しました。ディープシーは通常どおりの周期で開きます。",
                ["AlreadyRepaired"] = "灯台は修理済みです。変更はありません。",
                ["NoLighthouses"] = "このマップには修理できる灯台がありません。ディープシーは灯台を待たずに開きます。",
                ["DeepSeaDisabled"] = "このサーバーではディープシーが無効です（deepsea.enabled）。灯台は使われません。",
                ["Failed"] = "灯台を修理できませんでした。サーバーログを確認してください。",
                ["StateRepaired"] = "灯台: 修理済み。",
                ["StateNotRepaired"] = "灯台: 未修理（このマップに {0} か所）。",
                ["Wipe"] = "ワイプ（セーブ作成）: {0}。",
                ["WipeUnknown"] = "ワイプ時刻: 不明。自動修理は行いません。",
                ["AutoOn"] = "自動修理: 有効。ワイプから {0} 時間後（{1}）。",
                ["AutoOff"] = "自動修理: 無効（ワイプから {0} 時間後）。",
                ["Remaining"] = "あと約{0}時間{1:00}分",
                ["Due"] = "期限を過ぎています",
                ["DeepSeaWaiting"] = "ディープシー: 灯台の修理待ち。",
                ["DeepSeaOpen"] = "ディープシー: 開いています。",
                ["DeepSeaClosed"] = "ディープシー: 閉じています。次に開くまで約{0}時間{1:00}分。",
                ["DeepSeaMissing"] = "ディープシー: このサーバーには作成されていません。"
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
            if (config == null)
                return;
            if (config.AutoRepairEnabled == true && WipeTimeUtc() == null)
                PrintWarning("The wipe time (SaveRestore.SaveCreatedTime) is unknown; automatic repair will not run.");
            // The check reads a handful of lighthouses once a minute, so it stays cheap.
            checkTimer = timer.Every(CheckIntervalSeconds, CheckDeadline);
            CheckDeadline();
        }

        private void Unload()
        {
            if (checkTimer != null)
            {
                checkTimer.Destroy();
                checkTimer = null;
            }
        }

        private void CheckDeadline()
        {
            if (config == null || config.AutoRepairEnabled != true || GetState() != LighthouseState.NotRepaired)
                return;
            DateTime? deadline = DeadlineUtc();
            if (deadline == null || utcNow() < deadline.Value)
                return;

            RepairResult result = Repair();
            if (result == RepairResult.Repaired)
            {
                Puts($"Lighthouses were not repaired within {Hours(config.HoursAfterWipe.Value)} h after the wipe; repaired automatically.");
                if (config.AnnounceInChat == true)
                {
                    foreach (BasePlayer player in BasePlayer.activePlayerList)
                    {
                        if (player != null)
                            player.ChatMessage(Message("Broadcast", player));
                    }
                }
            }
            else if (result == RepairResult.Failed)
            {
                PrintError("Automatic repair failed: the lighthouses are still not repaired. Automatic repair is paused until the plugin is reloaded.");
                Unload();
            }
        }

        // -------------------------------------------------------------------
        // 灯台の状態と修理（RepairableLighthouse・DeepSeaManager を 2026-11-09 の実 DLL で確認）
        // プレイヤーが修理を終えたときの RepairableLighthouse.OnConstructionComplete と同じく、
        // 全灯台を修理済みにしてから DeepSeaManager.OnLighthouseRepaired を呼ぶ。
        // -------------------------------------------------------------------
        private static LighthouseState GetState()
        {
            if (!ConVar.DeepSea.enabled)
                return LighthouseState.DeepSeaDisabled;
            if (RepairableLighthouse.server_RepairableLighthouses.Count == 0)
                return LighthouseState.NoLighthouses;
            return RepairableLighthouse.IsAnyRepaired() ? LighthouseState.Repaired : LighthouseState.NotRepaired;
        }

        private static RepairResult Repair()
        {
            switch (GetState())
            {
                case LighthouseState.DeepSeaDisabled: return RepairResult.DeepSeaDisabled;
                case LighthouseState.NoLighthouses: return RepairResult.NoLighthouses;
                case LighthouseState.Repaired: return RepairResult.AlreadyRepaired;
            }
            RepairableLighthouse.RepairAll();
            if (!RepairableLighthouse.IsAnyRepaired())
                return RepairResult.Failed;
            DeepSeaManager manager = PointEntity<DeepSeaManager>.ServerInstance;
            if (manager != null)
                manager.OnLighthouseRepaired();
            return RepairResult.Repaired;
        }

        private static DateTime? WipeTimeUtc()
        {
            DateTime created = SaveRestore.SaveCreatedTime;
            if (created.Kind == DateTimeKind.Local)
                created = created.ToUniversalTime();
            else if (created.Kind == DateTimeKind.Unspecified)
                created = DateTime.SpecifyKind(created, DateTimeKind.Utc);
            return created < EarliestValidWipe ? (DateTime?)null : created;
        }

        private DateTime? DeadlineUtc()
        {
            DateTime? wipe = WipeTimeUtc();
            return wipe == null ? (DateTime?)null : wipe.Value.AddHours(config.HoursAfterWipe.Value);
        }

        // -------------------------------------------------------------------
        // 表示
        // -------------------------------------------------------------------
        private static string Hours(double hours)
        {
            return hours.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Timestamp(DateTime utc)
        {
            return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture);
        }

        private string Duration(string key, double seconds, BasePlayer player)
        {
            long minutes = (long)Math.Ceiling(Math.Max(0, seconds) / 60);
            return Message(key, player, minutes / 60, minutes % 60);
        }

        private string DescribeState(LighthouseState state, BasePlayer player)
        {
            switch (state)
            {
                case LighthouseState.DeepSeaDisabled: return Message("DeepSeaDisabled", player);
                case LighthouseState.NoLighthouses: return Message("NoLighthouses", player);
                case LighthouseState.Repaired: return Message("StateRepaired", player);
                default: return Message("StateNotRepaired", player, RepairableLighthouse.server_RepairableLighthouses.Count);
            }
        }

        private string DescribeDeepSea(BasePlayer player)
        {
            DeepSeaManager manager = PointEntity<DeepSeaManager>.ServerInstance;
            if (manager == null)
                return Message("DeepSeaMissing", player);
            if (manager.IsOpen())
                return Message("DeepSeaOpen", player);
            if (manager.IsWaitingForLighthouse())
                return Message("DeepSeaWaiting", player);
            return Duration("DeepSeaClosed", manager.TimeToNextOpening, player);
        }

        // -------------------------------------------------------------------
        // コンソールコマンド（サーバーコンソール・RCON・権限付き F1 コンソール）
        // -------------------------------------------------------------------
        private bool CanUse(ConsoleSystem.Arg arg, out BasePlayer player)
        {
            player = null;
            if (arg.Connection != null)
            {
                player = arg.Player();
                if (player == null || !(player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin)))
                {
                    arg.ReplyWith(Message("NoPermission", player));
                    return false;
                }
            }
            if (config == null)
            {
                arg.ReplyWith(Message("ConfigInvalid", player));
                return false;
            }
            if (!serverReady)
            {
                arg.ReplyWith(Message("NotReady", player));
                return false;
            }
            return true;
        }

        [ConsoleCommand("lighthouserepair.status")]
        private void StatusCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player;
            if (!CanUse(arg, out player))
                return;
            if (arg.HasArgs(1))
            {
                arg.ReplyWith(Message("UsageStatus", player));
                return;
            }
            LighthouseState state = GetState();
            List<string> lines = new List<string> { DescribeState(state, player) };
            if (state == LighthouseState.Repaired || state == LighthouseState.NotRepaired)
            {
                DateTime? wipe = WipeTimeUtc();
                string hours = Hours(config.HoursAfterWipe.Value);
                if (wipe == null)
                {
                    lines.Add(Message("WipeUnknown", player));
                }
                else
                {
                    lines.Add(Message("Wipe", player, Timestamp(wipe.Value)));
                    if (config.AutoRepairEnabled != true)
                    {
                        lines.Add(Message("AutoOff", player, hours));
                    }
                    else
                    {
                        DateTime deadline = DeadlineUtc().Value;
                        double remaining = (deadline - utcNow()).TotalSeconds;
                        string when = Timestamp(deadline);
                        if (state == LighthouseState.NotRepaired)
                            when += ", " + (remaining > 0 ? Duration("Remaining", remaining, player) : Message("Due", player));
                        lines.Add(Message("AutoOn", player, hours, when));
                    }
                }
                lines.Add(DescribeDeepSea(player));
            }
            arg.ReplyWith(string.Join("\n", lines));
        }

        [ConsoleCommand("lighthouserepair.repair")]
        private void RepairCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player;
            if (!CanUse(arg, out player))
                return;
            if (arg.HasArgs(1))
            {
                arg.ReplyWith(Message("UsageRepair", player));
                return;
            }
            RepairResult result = Repair();
            switch (result)
            {
                case RepairResult.Repaired:
                    Puts("Lighthouses repaired by " + (player == null ? "console" : player.UserIDString) + ".");
                    arg.ReplyWith(Message("Repaired", player));
                    return;
                case RepairResult.AlreadyRepaired:
                    arg.ReplyWith(Message("AlreadyRepaired", player));
                    return;
                case RepairResult.NoLighthouses:
                    arg.ReplyWith(Message("NoLighthouses", player));
                    return;
                case RepairResult.DeepSeaDisabled:
                    arg.ReplyWith(Message("DeepSeaDisabled", player));
                    return;
                default:
                    PrintError("Manual repair failed: the lighthouses are still not repaired.");
                    arg.ReplyWith(Message("Failed", player));
                    return;
            }
        }
    }
}
