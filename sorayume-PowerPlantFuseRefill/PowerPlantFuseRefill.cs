using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("sorayume-PowerPlantFuseRefill", "mazurk4", "1.0.0")]
    [Description("Refills heavy fuses at the Power Plant on a timer or by console/RCON command, and shows fuse condition and burnout estimates, for solo servers.")]
    public class PowerPlantFuseRefill : RustPlugin
    {
        private const string PermAdmin = "powerplantfuserefill.admin";
        private const string FuseShortname = "fuse.highgrade";
        private const int MaxTargetFuseCount = 64;
        private const double MinIntervalSeconds = 60;
        private const double MaxIntervalSeconds = 604800;

        // PowergridFuseBox keeps its fuse container private (checked on Rust buildid 26479619).
        // Reflection works whether or not the Oxide publicizer is enabled.
        private static readonly FieldInfo InventoryField = typeof(PowergridFuseBox).GetField(
            "inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private PluginConfig config;
        private Timer refillTimer;
        private bool serverReady;

        public class PluginConfig
        {
            [JsonProperty("Auto refill enabled")]
            public bool? AutoRefillEnabled;

            [JsonProperty("Refill interval seconds")]
            public double? RefillIntervalSeconds;

            [JsonProperty("Target fuse count")]
            public int? TargetFuseCount;

            [JsonProperty("Refill when plugin loads (auto refill only)")]
            public bool? RefillOnLoad;

            public static PluginConfig CreateDefault()
            {
                return new PluginConfig
                {
                    AutoRefillEnabled = true,
                    RefillIntervalSeconds = 10800,
                    TargetFuseCount = 10,
                    RefillOnLoad = true
                };
            }

            public string Validate()
            {
                if (AutoRefillEnabled == null) return "\"Auto refill enabled\" is missing";
                if (RefillIntervalSeconds == null) return "\"Refill interval seconds\" is missing";
                if (TargetFuseCount == null) return "\"Target fuse count\" is missing";
                if (RefillOnLoad == null) return "\"Refill when plugin loads (auto refill only)\" is missing";
                double interval = RefillIntervalSeconds.Value;
                if (double.IsNaN(interval) || double.IsInfinity(interval) || interval < MinIntervalSeconds || interval > MaxIntervalSeconds)
                    return "\"Refill interval seconds\" must be between 60 and 604800";
                if (TargetFuseCount.Value < 0 || TargetFuseCount.Value > MaxTargetFuseCount)
                    return "\"Target fuse count\" must be between 0 and 64";
                return null;
            }
        }

        private enum RefillStatus { Ok, GridDisabled, NoFuseBoxes, Unsupported }

        private class RefillResult
        {
            public RefillStatus Status;
            public int Sockets;
            public int Active;
            public int Goal;
            public int Inserted;
            public int Failed;
            public readonly List<FuseInfo> Fuses = new List<FuseInfo>();
        }

        private class FuseInfo
        {
            public int Box;
            public int Slot;
            public Item Item;
            public bool FullDecay;
            public double BurnoutSeconds = double.PositiveInfinity;
            public string Label => Box + "-" + Slot.ToString("00", CultureInfo.InvariantCulture);
        }

        // -------------------------------------------------------------------
        // 設定（欠損・範囲外・破損時はファイルを書き換えず、補充を停止する）
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
                PrintError("Refill is disabled until the config is fixed: " + error + ". The file was not modified.");
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
                ["ConfigInvalid"] = "The PowerPlantFuseRefill config is invalid. Fix it and reload the plugin.",
                ["NotReady"] = "The server has not finished starting. Try again later.",
                ["UsageFill"] = "Usage: powerplantfuse.fill [target fuse count 0-{0}]",
                ["UsageAuto"] = "Usage: powerplantfuse.auto [on|off]",
                ["On"] = "on",
                ["Off"] = "off",
                ["AutoState"] = "Automatic refill is {0} (every {1} h).",
                ["AutoEnabled"] = "Automatic refill enabled (every {0} h).",
                ["AutoDisabled"] = "Automatic refill disabled.",
                ["ConfigSaveFailed"] = "The setting was applied but could not be saved. It will revert after a reload.",
                ["Inserted"] = "Inserted {0} heavy fuse(s). Active fuses: {1}/{2} sockets (target {3}).",
                ["NothingToDo"] = "No refill needed. Active fuses: {0}/{1} sockets (target {2}).",
                ["Shortfall"] = "{0} fuse(s) could not be inserted. Check the server log.",
                ["GridDisabled"] = "The power grid is disabled on this server (powergrid.enabled).",
                ["NoFuseBoxes"] = "No Power Plant fuse boxes were found on this map.",
                ["Unsupported"] = "The Power Plant fuse boxes are not compatible with this plugin version. Check the server log.",
                ["Status"] = "Automatic refill: {0} (every {1} h). Target: {2}. Active fuses: {3}/{4} sockets. Power grid stage: {5}.",
                ["Powerline"] = "Powerline output: {0}/{1}.",
                ["NextStage"] = "Next stage {0} at {1} fuses ({2} more).",
                ["MaxStage"] = "Maximum stage reached.",
                ["NextBurnout"] = "Next burnout: {1} in {0} (estimate at {2} online player(s), without refill).",
                ["NoDecay"] = "Fuses do not decay (powergrid.fuselifespanseconds is 0).",
                ["Duration"] = "~{0}h {1:00}m",
                ["NoBurnout"] = "no decay",
                ["ListHeader"] = "Socket  Condition  Decay  Burnout in (estimate)",
                ["ListRow"] = "{0,-6}  {1,9}  {2,-5}  {3}",
                ["DecayFull"] = "full",
                ["DecaySlow"] = "slow",
                ["ListNone"] = "No heavy fuses are inserted.",
                ["ListFooter"] = "Empty sockets: {0}. Socket = box-slot. Estimates assume {1} online player(s) and no refill."
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "このコマンドを実行する権限がありません。",
                ["ConfigInvalid"] = "PowerPlantFuseRefill の設定が不正です。修正してからプラグインを再読み込みしてください。",
                ["NotReady"] = "サーバーの起動が完了していません。しばらくしてから再実行してください。",
                ["UsageFill"] = "使用法: powerplantfuse.fill [目標本数 0-{0}]",
                ["UsageAuto"] = "使用法: powerplantfuse.auto [on|off]",
                ["On"] = "有効",
                ["Off"] = "無効",
                ["AutoState"] = "自動補充は{0}です（{1} 時間ごと）。",
                ["AutoEnabled"] = "自動補充を有効にしました（{0} 時間ごと）。",
                ["AutoDisabled"] = "自動補充を無効にしました。",
                ["ConfigSaveFailed"] = "設定は反映しましたが保存できませんでした。再読み込みすると元に戻ります。",
                ["Inserted"] = "ヘビーヒューズを {0} 本補充しました。稼働中: {1}/{2} ソケット（目標 {3}）。",
                ["NothingToDo"] = "補充は不要です。稼働中: {0}/{1} ソケット（目標 {2}）。",
                ["Shortfall"] = "{0} 本を挿入できませんでした。サーバーログを確認してください。",
                ["GridDisabled"] = "このサーバーでは電力網が無効です（powergrid.enabled）。",
                ["NoFuseBoxes"] = "このマップにパワープラントのヒューズボックスが見つかりません。",
                ["Unsupported"] = "パワープラントのヒューズボックスがこのプラグインの版に対応していません。サーバーログを確認してください。",
                ["Status"] = "自動補充: {0}（{1} 時間ごと）。目標: {2}。稼働中: {3}/{4} ソケット。電力網ステージ: {5}。",
                ["Powerline"] = "送電線の出力: {0}/{1}。",
                ["NextStage"] = "次のステージ {0} は {1} 本で到達（あと {2} 本）。",
                ["MaxStage"] = "最大ステージです。",
                ["NextBurnout"] = "次に切れるヒューズ: {1}、{0}後（オンライン {2} 人・補充なしの推定）。",
                ["NoDecay"] = "ヒューズは劣化しません（powergrid.fuselifespanseconds が 0）。",
                ["Duration"] = "約{0}時間{1:00}分",
                ["NoBurnout"] = "劣化なし",
                ["ListHeader"] = "ソケット  耐久  劣化  切れるまで（推定）",
                ["ListRow"] = "{0,-6}  {1,9}  {2,-5}  {3}",
                ["DecayFull"] = "通常",
                ["DecaySlow"] = "低速",
                ["ListNone"] = "ヘビーヒューズは挿入されていません。",
                ["ListFooter"] = "空きソケット: {0}。ソケットは「ボックス-スロット」。推定はオンライン {1} 人・補充なしの前提。"
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
            if (!IsInventoryFieldSupported())
                PrintError("PowergridFuseBox.inventory was not found; refill is unavailable on this Rust version.");
            if (config == null || config.AutoRefillEnabled != true)
                return;
            StartTimer();
            if (config.RefillOnLoad == true)
                RunAutoRefill();
        }

        private void Unload()
        {
            StopTimer();
        }

        private void StartTimer()
        {
            StopTimer();
            refillTimer = timer.Every((float)config.RefillIntervalSeconds.Value, RunAutoRefill);
        }

        private void StopTimer()
        {
            if (refillTimer != null)
            {
                refillTimer.Destroy();
                refillTimer = null;
            }
        }

        private void RunAutoRefill()
        {
            if (config == null || config.AutoRefillEnabled != true)
                return;
            RefillResult result = Refill(config.TargetFuseCount.Value, true);
            LogResult("Auto refill", result);
        }

        // -------------------------------------------------------------------
        // 補充処理（全エンティティ走査は定期実行・管理コマンド時のみ）
        // -------------------------------------------------------------------
        private static bool IsInventoryFieldSupported()
        {
            return InventoryField != null && InventoryField.FieldType == typeof(ItemContainer);
        }

        private RefillResult Refill(int target, bool insert)
        {
            RefillResult result = new RefillResult();
            if (!Powergrid.enabled)
            {
                result.Status = RefillStatus.GridDisabled;
                return result;
            }
            ItemDefinition definition = ItemManager.FindItemDefinition(FuseShortname);
            if (definition == null || !IsInventoryFieldSupported())
            {
                result.Status = RefillStatus.Unsupported;
                return result;
            }

            List<PowergridFuseBox> boxes = new List<PowergridFuseBox>();
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                PowergridFuseBox box = entity as PowergridFuseBox;
                if (box != null && !box.IsDestroyed && box.net != null)
                    boxes.Add(box);
            }
            if (boxes.Count == 0)
            {
                result.Status = RefillStatus.NoFuseBoxes;
                return result;
            }
            boxes.Sort((a, b) => a.net.ID.Value.CompareTo(b.net.ID.Value));

            List<ItemContainer> containers = new List<ItemContainer>(boxes.Count);
            for (int boxIndex = 0; boxIndex < boxes.Count; boxIndex++)
            {
                PowergridFuseBox box = boxes[boxIndex];
                ItemContainer container = InventoryField.GetValue(box) as ItemContainer;
                if (container == null || !box.IsValidPassthroughItem(definition))
                {
                    result.Status = RefillStatus.Unsupported;
                    return result;
                }
                containers.Add(container);
                result.Sockets += container.capacity;
                foreach (Item item in container.itemList)
                {
                    if (item != null && item.info == definition && !item.isBroken)
                    {
                        result.Active++;
                        if (!insert)
                            result.Fuses.Add(new FuseInfo { Box = boxIndex + 1, Slot = item.position + 1, Item = item });
                    }
                }
            }
            result.Fuses.Sort((a, b) => a.Box != b.Box ? a.Box.CompareTo(b.Box) : a.Slot.CompareTo(b.Slot));

            result.Goal = Math.Min(target, result.Sockets);
            if (!insert)
                return result;

            int needed = result.Goal - result.Active;
            foreach (ItemContainer container in containers)
            {
                for (int slot = 0; slot < container.capacity && needed > 0; slot++)
                {
                    if (container.GetSlot(slot) != null)
                        continue;
                    Item fuse = ItemManager.Create(definition, 1);
                    if (fuse == null)
                    {
                        result.Failed++;
                        break;
                    }
                    if (!fuse.MoveToContainer(container, slot, false))
                    {
                        fuse.Remove();
                        result.Failed++;
                        continue;
                    }
                    result.Inserted++;
                    needed--;
                }
            }
            result.Active += result.Inserted;
            return result;
        }

        // -------------------------------------------------------------------
        // 劣化の推定（PowergridManager.ServerFuseDeteriorationTick を buildid 26479619 で確認）
        // 耐久の低い fuseFullDecayCount 本は fuseLifespanSeconds で info.condition.max を失い、残りは
        // 本ごとの低速倍率で劣化する。どちらもオンライン人数の倍率が掛かる。補充なしで切れる順に計算する。
        // -------------------------------------------------------------------
        private static void EstimateBurnouts(List<FuseInfo> fuses)
        {
            float lifespan = Powergrid.fuseLifespanSeconds;
            if (fuses.Count == 0 || lifespan <= 0f)
                return;
            List<Item> gameFull = new List<Item>();
            PowergridManager.Server_GatherFullDecayFuses(gameFull);
            double pop = PowergridManager.Server_GetPopDecayRateScale(BasePlayer.activePlayerList.Count);

            int count = fuses.Count;
            double[] condition = new double[count];
            double[] fullRate = new double[count];
            double[] slowRate = new double[count];
            bool[] full = new bool[count];
            bool[] alive = new bool[count];
            for (int i = 0; i < count; i++)
            {
                Item item = fuses[i].Item;
                double max = item.info.condition.max;
                double perSecond = max > 0 ? max / lifespan * pop : 0;
                condition[i] = item.condition;
                fullRate[i] = perSecond;
                slowRate[i] = perSecond * PowergridManager.Server_GetSlowDecayRateScale(item);
                full[i] = fuses[i].FullDecay = gameFull.Contains(item);
                alive[i] = true;
            }

            double elapsed = 0;
            for (int round = 0; round < count; round++)
            {
                int next = -1;
                double step = double.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    double rate = full[i] ? fullRate[i] : slowRate[i];
                    if (alive[i] && rate > 0 && condition[i] / rate < step)
                    {
                        step = condition[i] / rate;
                        next = i;
                    }
                }
                if (next < 0)
                    break;
                elapsed += step;
                for (int i = 0; i < count; i++)
                {
                    if (alive[i])
                        condition[i] = Math.Max(0, condition[i] - (full[i] ? fullRate[i] : slowRate[i]) * step);
                }
                alive[next] = false;
                fuses[next].BurnoutSeconds = elapsed;

                // The game re-selects the lowest-condition fuses for full decay on every tick.
                List<int> order = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    full[i] = false;
                    if (alive[i])
                        order.Add(i);
                }
                order.Sort((a, b) => condition[a] != condition[b] ? condition[a].CompareTo(condition[b]) : a.CompareTo(b));
                for (int k = 0; k < order.Count && k < Powergrid.fuseFullDecayCount; k++)
                    full[order[k]] = true;
            }
        }

        private string Duration(double seconds, BasePlayer player)
        {
            if (double.IsInfinity(seconds) || double.IsNaN(seconds))
                return Message("NoBurnout", player);
            long minutes = (long)Math.Round(Math.Min(seconds, 1e12) / 60);
            return Message("Duration", player, minutes / 60, minutes % 60);
        }

        private static int OnlinePlayers()
        {
            return BasePlayer.activePlayerList.Count;
        }

        private void LogResult(string source, RefillResult result)
        {
            switch (result.Status)
            {
                case RefillStatus.GridDisabled:
                    PrintWarning(source + ": skipped because powergrid.enabled is false.");
                    return;
                case RefillStatus.NoFuseBoxes:
                    PrintWarning(source + ": no Power Plant fuse boxes found on this map.");
                    return;
                case RefillStatus.Unsupported:
                    PrintError(source + ": Power Plant fuse boxes have an unsupported structure; nothing was inserted.");
                    return;
            }
            if (result.Inserted > 0)
                Puts($"{source}: inserted {result.Inserted} heavy fuse(s); active {result.Active}/{result.Sockets} (target {result.Goal}).");
            if (result.Active < result.Goal)
                PrintWarning($"{source}: {result.Goal - result.Active} fuse(s) could not be inserted ({result.Failed} failed attempt(s)).");
        }

        private string DescribeResult(RefillResult result, BasePlayer player)
        {
            switch (result.Status)
            {
                case RefillStatus.GridDisabled: return Message("GridDisabled", player);
                case RefillStatus.NoFuseBoxes: return Message("NoFuseBoxes", player);
                case RefillStatus.Unsupported: return Message("Unsupported", player);
            }
            string text = result.Inserted > 0
                ? Message("Inserted", player, result.Inserted, result.Active, result.Sockets, result.Goal)
                : Message("NothingToDo", player, result.Active, result.Sockets, result.Goal);
            if (result.Active < result.Goal)
                text += "\n" + Message("Shortfall", player, result.Goal - result.Active);
            return text;
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

        private string Hours(double seconds)
        {
            return (seconds / 3600).ToString("0.##", CultureInfo.InvariantCulture);
        }

        [ConsoleCommand("powerplantfuse.fill")]
        private void FillCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player;
            if (!CanUse(arg, out player))
                return;
            int target = config.TargetFuseCount.Value;
            if (arg.HasArgs(2) || (arg.HasArgs(1) && (!int.TryParse(arg.GetString(0), NumberStyles.None, CultureInfo.InvariantCulture, out target) || target > MaxTargetFuseCount)))
            {
                arg.ReplyWith(Message("UsageFill", player, MaxTargetFuseCount));
                return;
            }
            RefillResult result = Refill(target, true);
            LogResult(player == null ? "Console refill" : "Refill by " + player.UserIDString, result);
            arg.ReplyWith(DescribeResult(result, player));
        }

        [ConsoleCommand("powerplantfuse.auto")]
        private void AutoCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player;
            if (!CanUse(arg, out player))
                return;
            if (!arg.HasArgs(1))
            {
                string state = Message(config.AutoRefillEnabled == true ? "On" : "Off", player);
                arg.ReplyWith(Message("AutoState", player, state, Hours(config.RefillIntervalSeconds.Value)));
                return;
            }
            string value = arg.HasArgs(2) ? null : arg.GetString(0).ToLowerInvariant();
            if (value != "on" && value != "off")
            {
                arg.ReplyWith(Message("UsageAuto", player));
                return;
            }

            bool enable = value == "on";
            config.AutoRefillEnabled = enable;
            if (enable)
                StartTimer();
            else
                StopTimer();
            Puts($"Automatic refill {(enable ? "enabled" : "disabled")} by {(player == null ? "console" : player.UserIDString)}.");

            string reply = enable ? Message("AutoEnabled", player, Hours(config.RefillIntervalSeconds.Value)) : Message("AutoDisabled", player);
            try
            {
                SaveConfig();
            }
            catch (Exception ex)
            {
                PrintError("Could not save config: " + ex.Message);
                reply += "\n" + Message("ConfigSaveFailed", player);
            }
            arg.ReplyWith(reply);
        }

        [ConsoleCommand("powerplantfuse.status")]
        private void StatusCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player;
            if (!CanUse(arg, out player))
                return;
            RefillResult result = Refill(config.TargetFuseCount.Value, false);
            if (result.Status != RefillStatus.Ok)
            {
                arg.ReplyWith(DescribeResult(result, player));
                return;
            }
            int stage = PowergridManager.GetCurrentStage(true);
            string state = Message(config.AutoRefillEnabled == true ? "On" : "Off", player);
            string text = Message("Status", player, state, Hours(config.RefillIntervalSeconds.Value),
                config.TargetFuseCount.Value, result.Active, result.Sockets, stage);

            text += "\n" + Message("Powerline", player, PowergridManager.Server_GetCurrentPowerlineEnergy(), Powergrid.powerlineMaxPowerOutput);
            PowergridStageConfig stages = PowergridStageConfig.instance;
            PowergridStageConfig.StageData next;
            if (stages != null && stage >= 0 && stage < stages.GetNumberOfStages() && stages.TryGetStageDataForStage(stage + 1, out next) && next != null)
            {
                int counted = result.Active + Powergrid.simulatePowerPlantFuses;
                text += " " + Message("NextStage", player, stage + 1, next.requiredFuses, Math.Max(0, next.requiredFuses - counted));
            }
            else if (stages != null)
            {
                text += " " + Message("MaxStage", player);
            }

            if (Powergrid.fuseLifespanSeconds <= 0f)
            {
                text += "\n" + Message("NoDecay", player);
            }
            else if (result.Fuses.Count > 0)
            {
                EstimateBurnouts(result.Fuses);
                FuseInfo first = result.Fuses[0];
                foreach (FuseInfo fuse in result.Fuses)
                {
                    if (fuse.BurnoutSeconds < first.BurnoutSeconds)
                        first = fuse;
                }
                if (!double.IsInfinity(first.BurnoutSeconds))
                    text += "\n" + Message("NextBurnout", player, Duration(first.BurnoutSeconds, player), first.Label, OnlinePlayers());
            }
            arg.ReplyWith(text);
        }

        [ConsoleCommand("powerplantfuse.list")]
        private void ListCommand(ConsoleSystem.Arg arg)
        {
            BasePlayer player;
            if (!CanUse(arg, out player))
                return;
            RefillResult result = Refill(config.TargetFuseCount.Value, false);
            if (result.Status != RefillStatus.Ok)
            {
                arg.ReplyWith(DescribeResult(result, player));
                return;
            }
            if (result.Fuses.Count == 0)
            {
                arg.ReplyWith(Message("ListNone", player) + "\n" + Message("ListFooter", player, result.Sockets, OnlinePlayers()));
                return;
            }
            EstimateBurnouts(result.Fuses);
            List<string> lines = new List<string> { Message("ListHeader", player) };
            foreach (FuseInfo fuse in result.Fuses)
            {
                double max = fuse.Item.info.condition.max;
                string percent = (max > 0 ? fuse.Item.condition / max * 100 : 0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
                string decay = Powergrid.fuseLifespanSeconds <= 0f ? "-" : Message(fuse.FullDecay ? "DecayFull" : "DecaySlow", player);
                lines.Add(Message("ListRow", player, fuse.Label, percent, decay, Duration(fuse.BurnoutSeconds, player)));
            }
            lines.Add(Message("ListFooter", player, result.Sockets - result.Fuses.Count, OnlinePlayers()));
            arg.ReplyWith(string.Join("\n", lines));
        }
    }
}
