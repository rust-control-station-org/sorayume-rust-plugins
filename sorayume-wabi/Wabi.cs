// Based on Wabi 1.0.1. Copyright (c) 2026 Mazurk4_. MIT License; see LICENSE.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("sorayume-wabi", "mazurk4", "1.1.0")]
    [Description("Compensation claim plugin with campaign IDs, target control, expiry and persistent claim history.")]
    public class Wabi : RustPlugin
    {
        private const string PermAdmin = "wabi.admin";
        private const string TargetAll = "all";
        private const string TargetSelected = "selected";

        private readonly HashSet<ulong> processingPlayers = new HashSet<ulong>();

        private PluginConfig config;
        private StoredData data;
        private bool dataHealthy;

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Unavailable"] = "No compensation is currently available.",
                ["PreviousSeason"] = "This compensation belongs to a previous season.",
                ["Expired"] = "This compensation has expired.",
                ["NotEligible"] = "You are not eligible for this compensation.",
                ["AlreadyClaimed"] = "You have already claimed this compensation.",
                ["Incomplete"] = "A previous claim did not complete normally. Please contact an administrator.",
                ["Processing"] = "Your compensation is already being processed.",
                ["InvalidItems"] = "Compensation configuration error. Please contact an administrator.",
                ["ReceivedDropped"] = "Compensation received. Some items were dropped at your feet because your inventory was full.\n{0}",
                ["Received"] = "Compensation received.\n{0}",
                ["ClaimFailed"] = "An error occurred while claiming. Automatic retry is disabled to prevent duplicate delivery. Please contact an administrator.",
                ["Help"] = "Wabi commands:\nwabi.new [id]\nwabi.item <shortname> <amount>\nwabi.remove <shortname>\nwabi.target all\nwabi.target add <steamid64>\nwabi.target remove <steamid64>\nwabi.target clear\nwabi.expire default\nwabi.expire <3h|30m|180m>\nwabi.expire <yyyy-MM-dd HH:mm>  (server local time)\nwabi.start\nwabi.stop\nwabi.list\nwabi.check <steamid64> [campaignId]\nwabi.reset <steamid64> [campaignId]",
                ["MustStop"] = "The current campaign is active. Run wabi.stop first.",
                ["DuplicateId"] = "That campaign ID already exists: {0}",
                ["Created"] = "Created campaign: {0}\nSeasonId: {1}\nDefault expiry: {2} minutes (from wabi.start)",
                ["UsageItem"] = "Usage: wabi.item <shortname> <amount>",
                ["InvalidAmount"] = "amount must be a positive integer.",
                ["UnknownItem"] = "Unknown item shortname: {0}",
                ["ItemSet"] = "Set: {0} x{1}",
                ["UsageRemove"] = "Usage: wabi.remove <shortname>",
                ["ItemRemoved"] = "{0}{1}",
                ["UsageTarget"] = "Usage: wabi.target all | add <steamid64> | remove <steamid64> | clear",
                ["TargetAll"] = "Target set to all players.",
                ["TargetClear"] = "Target list cleared. Nobody is eligible until added.",
                ["InvalidSteamId"] = "Specify a SteamID64.",
                ["TargetAdded"] = "Added target: {0}",
                ["TargetRemoved"] = "Removed target: {0}",
                ["UsageExpire"] = "Usage: wabi.expire default | 3h | 30m | yyyy-MM-dd HH:mm",
                ["ExpiryDefault"] = "Expiry reset to the default {0} minutes from wabi.start.",
                ["ExpiryDuration"] = "Expiry set to {0} minutes from wabi.start.",
                ["InvalidExpiry"] = "Invalid expiry. Use 3h, 30m or yyyy-MM-dd HH:mm.",
                ["PastExpiry"] = "The expiry must be in the future.",
                ["ExpiryFixed"] = "Fixed expiry set: {0}",
                ["NoCampaign"] = "No campaign. Run wabi.new first.",
                ["AlreadyActive"] = "This campaign is already active.",
                ["CannotStart"] = "Cannot start: {0}",
                ["NoTargets"] = "Cannot start: the selected target list is empty.",
                ["WrongSeason"] = "Cannot start: season mismatch. Create a new campaign with wabi.new.",
                ["FixedExpired"] = "The fixed expiry has passed. Set it again with wabi.expire.",
                ["Started"] = "Campaign started.\nID: {0}\nExpires: {1}\nTarget: {2}",
                ["NoCurrent"] = "There is no current campaign.",
                ["Stopped"] = "Campaign stopped: {0}",
                ["NoCampaignList"] = "There is no current campaign. Create one with wabi.new.",
                ["CampaignList"] = "Wabi campaign\nID: {0}\nSeasonId: {1}\nStatus: {2}\nCreated: {3}\nStarted: {4}\nExpires: {5}\nExpiry mode: {6}\nTarget: {7}\nItems: {8}\nClaims: claimed={9} pending={10} failed={11}",
                ["UsageCheck"] = "Usage: wabi.check <steamid64> [campaignId]",
                ["CampaignMissing"] = "Campaign ID not found.",
                ["NotClaimed"] = "Campaign {0}: {1} has not claimed.",
                ["ClaimDetails"] = "Campaign: {0}\nSteamID: {1}\nPlayer: {2}\nStatus: {3}\nStarted: {4}\nCompleted: {5}\nDelivered: {6}\nError: {7}",
                ["UsageReset"] = "Usage: wabi.reset <steamid64> [campaignId]",
                ["NoClaim"] = "There is no claim record for that SteamID.",
                ["ResetClaim"] = "Claim record reset: {0} / {1}\nItems already delivered are not removed. Another /wabi can grant duplicates. Administrator use only.",
                ["NoPermission"] = "Permission denied. Required permission: {0}",
                ["MustStopEditing"] = "An active campaign cannot be edited. Run wabi.stop first.",
                ["Removed"] = "Removed: ",
                ["InvalidId"] = "ID must contain 1-64 letters, digits, underscores or hyphens.",
                ["TooManyStacks"] = "A claim may contain at most 1000 item stacks.",
                ["NotConfigured"] = "Not configured: ",
                ["UnavailableState"] = "Wabi configuration or data is invalid. Repair the files and reload Wabi.",
                ["NoItems"] = "No compensation items are configured.",
                ["InvalidReward"] = "Invalid item or amount: {0}"
            }, this, "en");
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Unavailable"] = "現在受け取れる補填はありません。",
                ["PreviousSeason"] = "この補填は現在のシーズンでは受け取れません。",
                ["Expired"] = "この補填の受け取り期間は終了しました。",
                ["NotEligible"] = "あなたはこの補填の対象ではありません。",
                ["AlreadyClaimed"] = "この補填は既に受け取っています。",
                ["Incomplete"] = "前回の受領処理が正常完了していません。管理者へお問い合わせください。",
                ["Processing"] = "現在受領処理中です。",
                ["InvalidItems"] = "補填設定に問題があります。管理者へお問い合わせください。",
                ["ReceivedDropped"] = "補填を受け取りました。一部アイテムは足元にドロップされました。\n{0}",
                ["Received"] = "補填を受け取りました。\n{0}",
                ["ClaimFailed"] = "補填の受領処理中にエラーが発生しました。二重付与防止のため自動再試行は行いません。管理者へお問い合わせください。",
                ["Help"] = "Wabi commands:\nwabi.new [id]\nwabi.item <shortname> <amount>\nwabi.remove <shortname>\nwabi.target all\nwabi.target add <steamid64>\nwabi.target remove <steamid64>\nwabi.target clear\nwabi.expire default\nwabi.expire <3h|30m|180m>\nwabi.expire <yyyy-MM-dd HH:mm>  (server local time)\nwabi.start\nwabi.stop\nwabi.list\nwabi.check <steamid64> [campaignId]\nwabi.reset <steamid64> [campaignId]",
                ["MustStop"] = "現在の補填が受付中です。先に wabi.stop を実行してください。",
                ["DuplicateId"] = "その補填IDは既に存在します: {0}",
                ["Created"] = "補填を作成しました: {0}\nSeasonId: {1}\nDefault expiry: {2} minutes (starts from wabi.start)",
                ["UsageItem"] = "Usage: wabi.item <shortname> <amount>",
                ["InvalidAmount"] = "amount は1以上の整数を指定してください。",
                ["UnknownItem"] = "存在しないitem shortnameです: {0}",
                ["ItemSet"] = "設定しました: {0} x{1}",
                ["UsageRemove"] = "Usage: wabi.remove <shortname>",
                ["ItemRemoved"] = "{0}{1}",
                ["UsageTarget"] = "Usage: wabi.target all | add <steamid64> | remove <steamid64> | clear",
                ["TargetAll"] = "対象を全プレイヤーに設定しました。",
                ["TargetClear"] = "個別対象リストを空にしました。現在は誰も受領できません。",
                ["InvalidSteamId"] = "SteamID64を指定してください。",
                ["TargetAdded"] = "対象に追加しました: {0}",
                ["TargetRemoved"] = "対象から削除しました: {0}",
                ["UsageExpire"] = "Usage: wabi.expire default | 3h | 30m | yyyy-MM-dd HH:mm",
                ["ExpiryDefault"] = "有効期限をデフォルトの {0} 分に戻しました。wabi.start からカウントします。",
                ["ExpiryDuration"] = "有効期間を {0} 分に設定しました。wabi.start からカウントします。",
                ["InvalidExpiry"] = "期限を解析できません。例: wabi.expire 3h / wabi.expire 2026-08-16 23:59",
                ["PastExpiry"] = "過去の日時は指定できません。",
                ["ExpiryFixed"] = "固定期限を設定しました: {0}",
                ["NoCampaign"] = "補填がありません。wabi.new を実行してください。",
                ["AlreadyActive"] = "この補填は既に受付中です。",
                ["CannotStart"] = "開始できません: {0}",
                ["NoTargets"] = "開始できません: 個別対象モードですが対象SteamIDが0件です。",
                ["WrongSeason"] = "開始できません: この補填は現在のSeasonIdと一致しません。新しい補填を wabi.new で作成してください。",
                ["FixedExpired"] = "固定期限が既に過ぎています。wabi.expire で設定し直してください。",
                ["Started"] = "補填受付を開始しました。\nID: {0}\nExpires: {1}\nTarget: {2}",
                ["NoCurrent"] = "現在の補填がありません。",
                ["Stopped"] = "補填受付を停止しました: {0}",
                ["NoCampaignList"] = "現在の補填がありません。wabi.new で作成してください。",
                ["CampaignList"] = "Wabi campaign\nID: {0}\nSeasonId: {1}\nStatus: {2}\nCreated: {3}\nStarted: {4}\nExpires: {5}\nExpiry mode: {6}\nTarget: {7}\nItems: {8}\nClaims: claimed={9} pending={10} failed={11}",
                ["UsageCheck"] = "Usage: wabi.check <steamid64> [campaignId]",
                ["CampaignMissing"] = "補填IDが見つかりません。",
                ["NotClaimed"] = "Campaign {0}: {1} は未受領です。",
                ["ClaimDetails"] = "Campaign: {0}\nSteamID: {1}\nPlayer: {2}\nStatus: {3}\nStarted: {4}\nCompleted: {5}\nDelivered: {6}\nError: {7}",
                ["UsageReset"] = "Usage: wabi.reset <steamid64> [campaignId]",
                ["NoClaim"] = "そのSteamIDには受領記録がありません。",
                ["ResetClaim"] = "受領記録をリセットしました: {0} / {1}\n注意: 既にアイテムが渡っていた場合、再度 /wabi で二重付与されます。管理者判断でのみ使用してください。",
                ["NoPermission"] = "権限がありません。Required permission: {0}",
                ["MustStopEditing"] = "受付中の補填は編集できません。先に wabi.stop を実行してください。",
                ["Removed"] = "削除しました: ",
                ["InvalidId"] = "ID は英数字・_・- の1〜64文字で指定してください。",
                ["TooManyStacks"] = "1回の受領は最大1000スタックまでです。",
                ["NotConfigured"] = "設定されていません: ",
                ["UnavailableState"] = "Wabi の設定または保存データが不正です。修正してから再読み込みしてください。",
                ["NoItems"] = "補填アイテムが設定されていません。",
                ["InvalidReward"] = "アイテムまたは数量が不正です: {0}"
            }, this, "ja");
        }

        private string Text(string key, BasePlayer player = null, params object[] args)
        {
            return string.Format(lang.GetMessage(key, this, player?.UserIDString), args);
        }

        #region Configuration

        private class PluginConfig
        {
            [JsonProperty("Default expiry hours", Required = Required.Always)]
            public double DefaultExpiryHours = 3.0;

            [JsonProperty("Drop items at player's feet when inventory is full", Required = Required.Always)]
            public bool DropWhenInventoryFull = true;
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null || double.IsNaN(config.DefaultExpiryHours) || double.IsInfinity(config.DefaultExpiryHours)
                    || config.DefaultExpiryHours <= 0 || config.DefaultExpiryHours * 60 > MaxDurationMinutes)
                    throw new Exception("Default expiry hours must be finite, positive and at most 87600");
            }
            catch (Exception ex)
            {
                config = null;
                PrintError("Failed to read config; file preserved, commands disabled: " + ex.Message);
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Data

        private class StoredData
        {
            [JsonProperty("CurrentSeasonId", Required = Required.AllowNull)]
            public string CurrentSeasonId = string.Empty;

            [JsonProperty("CurrentCampaignId", Required = Required.AllowNull)]
            public string CurrentCampaignId = string.Empty;

            [JsonProperty("Campaigns", Required = Required.AllowNull)]
            public Dictionary<string, CompensationCampaign> Campaigns = new Dictionary<string, CompensationCampaign>();
        }

        private class CompensationCampaign
        {
            [JsonProperty("Id", Required = Required.AllowNull)]
            public string Id;

            [JsonProperty("SeasonId", Required = Required.AllowNull)]
            public string SeasonId;

            [JsonProperty("CreatedAtUnix", Required = Required.AllowNull)]
            public long CreatedAtUnix;

            [JsonProperty("StartedAtUnix", Required = Required.AllowNull)]
            public long StartedAtUnix;

            [JsonProperty("ExpiresAtUnix", Required = Required.AllowNull)]
            public long ExpiresAtUnix;

            [JsonProperty("ExpiryDurationMinutes", Required = Required.AllowNull)]
            public int ExpiryDurationMinutes;

            [JsonProperty("UsesFixedExpiry", Required = Required.AllowNull)]
            public bool UsesFixedExpiry;

            [JsonProperty("Active", Required = Required.AllowNull)]
            public bool Active;

            [JsonProperty("TargetMode", Required = Required.AllowNull)]
            public string TargetMode = TargetAll;

            [JsonProperty("TargetSteamIds", Required = Required.AllowNull)]
            public HashSet<string> TargetSteamIds = new HashSet<string>();

            [JsonProperty("Items", Required = Required.AllowNull)]
            public List<CompensationItem> Items = new List<CompensationItem>();

            [JsonProperty("Claims", Required = Required.AllowNull)]
            public Dictionary<string, ClaimRecord> Claims = new Dictionary<string, ClaimRecord>();
        }

        private class CompensationItem
        {
            [JsonProperty("Shortname", Required = Required.AllowNull)]
            public string Shortname;

            [JsonProperty("Amount", Required = Required.AllowNull)]
            public int Amount;
        }

        private class ClaimRecord
        {
            [JsonProperty("SteamId", Required = Required.AllowNull)]
            public string SteamId;

            [JsonProperty("PlayerName", Required = Required.AllowNull)]
            public string PlayerName;

            [JsonProperty("Status", Required = Required.AllowNull)]
            public string Status;

            [JsonProperty("StartedAtUnix", Required = Required.AllowNull)]
            public long StartedAtUnix;

            [JsonProperty("CompletedAtUnix", Required = Required.AllowNull)]
            public long CompletedAtUnix;

            [JsonProperty("Delivered", Required = Required.AllowNull)]
            public List<string> Delivered = new List<string>();

            [JsonProperty("Error", Required = Required.AllowNull)]
            public string Error;
        }

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
        }

        private void OnServerInitialized()
        {
            if (!dataHealthy || config == null) return;
            if (string.IsNullOrEmpty(data.CurrentSeasonId))
            {
                data.CurrentSeasonId = "installed-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                SaveData();
            }
        }

        private void OnServerSave()
        {
            SaveData();
        }

        private void Unload()
        {
            SaveData();
        }

        // Called by Oxide when Rust creates a new save (normally a map wipe).
        private void OnNewSave(string filename)
        {
            if (!dataHealthy || config == null) return;

            data.CurrentSeasonId = string.IsNullOrEmpty(filename)
                ? "wipe-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                : filename;

            foreach (CompensationCampaign campaign in data.Campaigns.Values)
                campaign.Active = false;

            SaveData();
            Puts("[Wabi] New save detected. All compensation campaigns were stopped. SeasonId=" + data.CurrentSeasonId);
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ExistsDatafile(Name)
                    ? Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) : new StoredData();
                ValidateData();
                dataHealthy = true;
            }
            catch (Exception ex)
            {
                PrintError("Failed to read data; file preserved, commands disabled: " + ex.Message);
                data = null;
                dataHealthy = false;
            }
        }

        private void ValidateData()
        {
            if (data == null || data.CurrentSeasonId == null || data.CurrentCampaignId == null || data.Campaigns == null
                || (data.CurrentCampaignId.Length > 0 && !data.Campaigns.ContainsKey(data.CurrentCampaignId)))
                throw new Exception("Invalid campaign data");
            foreach (var pair in data.Campaigns)
            {
                CompensationCampaign c = pair.Value;
                if (c == null || c.Id != pair.Key || c.SeasonId == null || c.Items == null || c.Claims == null
                    || c.TargetSteamIds == null || (c.TargetMode != TargetAll && c.TargetMode != TargetSelected)
                    || c.Items.Any(x => x == null || string.IsNullOrEmpty(x.Shortname) || x.Amount <= 0)
                    || c.ExpiryDurationMinutes <= 0 || c.ExpiryDurationMinutes > MaxDurationMinutes
                    || !ValidUnix(c.CreatedAtUnix) || !ValidUnix(c.StartedAtUnix) || !ValidUnix(c.ExpiresAtUnix))
                    throw new Exception("Invalid campaign entry");
                foreach (var record in c.Claims)
                {
                    ClaimRecord r = record.Value;
                    if (r == null || r.SteamId != record.Key || r.Delivered == null || r.PlayerName == null
                        || (r.Status != "pending" && r.Status != "claimed" && r.Status != "failed")
                        || !ValidUnix(r.StartedAtUnix) || !ValidUnix(r.CompletedAtUnix))
                        throw new Exception("Invalid claim record");
                }
            }
        }

        private static bool ValidUnix(long value) => value >= 0 && value <= 253402300799L;

        private void SaveData()
        {
            if (!dataHealthy || data == null || config == null) return;
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(Name, data);
            }
            catch
            {
                dataHealthy = false;
                PrintError("Wabi data could not be saved; commands disabled until reload. No automatic claim retry.");
                throw;
            }
        }

        #endregion

        #region Player Command

        [ChatCommand("wabi")]
        private void CmdWabi(BasePlayer player, string command, string[] args)
        {
            if (player == null)
                return;
            if (!dataHealthy || config == null)
            {
                ReplyPlayer(player, Text("UnavailableState", player));
                return;
            }

            CompensationCampaign campaign = GetCurrentCampaign();
            if (campaign == null)
            {
                ReplyPlayer(player, Text("Unavailable", player));
                return;
            }

            if (!campaign.Active)
            {
                ReplyPlayer(player, Text("Unavailable", player));
                return;
            }

            if (campaign.SeasonId != data.CurrentSeasonId)
            {
                campaign.Active = false;
                SaveData();
                ReplyPlayer(player, Text("PreviousSeason", player));
                return;
            }

            long now = UnixNow();
            if (campaign.ExpiresAtUnix > 0 && now >= campaign.ExpiresAtUnix)
            {
                campaign.Active = false;
                SaveData();
                ReplyPlayer(player, Text("Expired", player));
                return;
            }

            string steamId = player.UserIDString;
            if (!IsTargetPlayer(campaign, steamId))
            {
                ReplyPlayer(player, Text("NotEligible", player));
                return;
            }

            ClaimRecord existing;
            if (campaign.Claims.TryGetValue(steamId, out existing))
            {
                if (existing.Status == "claimed")
                {
                    ReplyPlayer(player, Text("AlreadyClaimed", player));
                    return;
                }

                ReplyPlayer(player, Text("Incomplete", player));
                return;
            }

            if (!processingPlayers.Add(player.userID))
            {
                ReplyPlayer(player, Text("Processing", player));
                return;
            }

            try
            {
                string validationError;
                if (!ValidateCampaignItems(campaign, out validationError, player))
                {
                    PrintError("[Wabi] Campaign " + campaign.Id + " validation failed: " + validationError);
                    ReplyPlayer(player, Text("InvalidItems", player));
                    return;
                }

                ClaimRecord claim = new ClaimRecord
                {
                    SteamId = steamId,
                    PlayerName = SanitizeDisplayName(player.displayName),
                    Status = "pending",
                    StartedAtUnix = now,
                    CompletedAtUnix = 0,
                    Error = string.Empty
                };

                // Persist pending BEFORE giving items. This intentionally prioritizes preventing
                // duplicate claims if the server/plugin crashes during delivery.
                campaign.Claims[steamId] = claim;
                SaveData();

                bool droppedAny = false;

                try
                {
                    foreach (CompensationItem reward in campaign.Items)
                    {
                        ItemDefinition definition = ItemManager.FindItemDefinition(reward.Shortname);
                        if (definition == null)
                            throw new Exception("Unknown item shortname: " + reward.Shortname);

                        int remaining = reward.Amount;
                        int maxStack = definition.stackable > 0 ? definition.stackable : 1;

                        while (remaining > 0)
                        {
                            int stackAmount = Math.Min(remaining, maxStack);
                            Item item = ItemManager.CreateByName(reward.Shortname, stackAmount);
                            if (item == null)
                                throw new Exception("ItemManager.CreateByName returned null for " + reward.Shortname);

                            bool dropped;
                            if (!GiveOrDrop(player, item, out dropped))
                            {
                                item.Remove();
                                throw new Exception("Failed to give or drop " + reward.Shortname + " x" + stackAmount);
                            }

                            if (dropped)
                                droppedAny = true;

                            remaining -= stackAmount;
                        }

                        claim.Delivered.Add(reward.Shortname + " x" + reward.Amount);
                    }

                    claim.Status = "claimed";
                    claim.CompletedAtUnix = UnixNow();
                    SaveData();

                    string itemText = string.Join(", ", campaign.Items.Select(x => x.Shortname + " x" + x.Amount).ToArray());
                    Puts("[Wabi] CLAIMED campaign=" + campaign.Id + " player=" + claim.PlayerName + " (" + steamId + ") items=" + itemText);

                    if (droppedAny)
                    {
                        ReplyPlayer(player, Text("ReceivedDropped", player, itemText));
                    }
                    else
                    {
                        ReplyPlayer(player, Text("Received", player, itemText));
                    }
                }
                catch (Exception ex)
                {
                    claim.Status = "failed";
                    claim.Error = ex.Message;
                    claim.CompletedAtUnix = UnixNow();
                    SaveData();

                    PrintError("[Wabi] FAILED campaign=" + campaign.Id + " player=" + claim.PlayerName + " (" + steamId + "): " + ex);
                    ReplyPlayer(player, Text("ClaimFailed", player));
                }
            }
            finally
            {
                processingPlayers.Remove(player.userID);
            }
        }

        private bool GiveOrDrop(BasePlayer player, Item item, out bool dropped)
        {
            dropped = false;

            if (player == null || item == null)
                return false;

            if (item.MoveToContainer(player.inventory.containerMain))
                return true;

            if (item.MoveToContainer(player.inventory.containerBelt))
                return true;

            if (!config.DropWhenInventoryFull)
                return false;

            if (item.Drop(player.transform.position + Vector3.up * 0.5f, Vector3.zero) == null)
                return false;
            dropped = true;
            return true;
        }

        #endregion

        #region Admin Commands

        [ConsoleCommand("wabi.help")]
        private void CmdWabiHelp(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            ReplyAdmin(arg, Text("Help", arg.Player()));
        }

        [ConsoleCommand("wabi.new")]
        private void CmdWabiNew(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            CompensationCampaign current = GetCurrentCampaign();
            if (current != null && current.Active && !IsExpired(current))
            {
                ReplyAdmin(arg, Text("MustStop", arg.Player()));
                return;
            }

            string requestedId = (arg.Args != null && arg.Args.Length > 0) ? ((string)arg.Args[0]).Trim() : string.Empty;
            string id = string.IsNullOrEmpty(requestedId) ? GenerateCampaignId() : requestedId;

            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,64}$"))
            {
                ReplyAdmin(arg, Text("InvalidId", arg.Player()));
                return;
            }

            if (data.Campaigns.ContainsKey(id))
            {
                ReplyAdmin(arg, Text("DuplicateId", arg.Player(), id));
                return;
            }

            CompensationCampaign campaign = new CompensationCampaign
            {
                Id = id,
                SeasonId = data.CurrentSeasonId,
                CreatedAtUnix = UnixNow(),
                ExpiryDurationMinutes = DefaultExpiryMinutes(),
                UsesFixedExpiry = false,
                Active = false,
                TargetMode = TargetAll
            };

            data.Campaigns[id] = campaign;
            data.CurrentCampaignId = id;
            SaveData();

            Puts("[Wabi] ACTION wabi.new by " + DescribeActor(arg) + " campaign=" + id);
            ReplyAdmin(arg, Text("Created", arg.Player(), id, campaign.SeasonId, campaign.ExpiryDurationMinutes));
        }

        [ConsoleCommand("wabi.item")]
        private void CmdWabiItem(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            CompensationCampaign campaign;
            if (!RequireEditableCampaign(arg, out campaign)) return;

            if (arg.Args == null || arg.Args.Length < 2)
            {
                ReplyAdmin(arg, Text("UsageItem", arg.Player()));
                return;
            }

            string shortname = GetArgString(arg, 0).ToLowerInvariant();
            int amount;
            if (!int.TryParse(GetArgString(arg, 1), out amount) || amount <= 0)
            {
                ReplyAdmin(arg, Text("InvalidAmount", arg.Player()));
                return;
            }

            ItemDefinition definition = ItemManager.FindItemDefinition(shortname);
            if (definition == null)
            {
                ReplyAdmin(arg, Text("UnknownItem", arg.Player(), shortname));
                return;
            }

            CompensationItem existing = campaign.Items.FirstOrDefault(x => x.Shortname == shortname);
            if (existing == null)
            {
                campaign.Items.Add(new CompensationItem { Shortname = shortname, Amount = amount });
            }
            else
            {
                existing.Amount = amount;
            }

            SaveData();
            Puts("[Wabi] ACTION wabi.item by " + DescribeActor(arg) + " campaign=" + campaign.Id + " item=" + shortname + " x" + amount);
            ReplyAdmin(arg, Text("ItemSet", arg.Player(), shortname, amount));
        }

        [ConsoleCommand("wabi.remove")]
        private void CmdWabiRemove(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            CompensationCampaign campaign;
            if (!RequireEditableCampaign(arg, out campaign)) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                ReplyAdmin(arg, Text("UsageRemove", arg.Player()));
                return;
            }

            string shortname = GetArgString(arg, 0).ToLowerInvariant();
            int removed = campaign.Items.RemoveAll(x => x.Shortname == shortname);
            SaveData();
            if (removed > 0)
                Puts("[Wabi] ACTION wabi.remove by " + DescribeActor(arg) + " campaign=" + campaign.Id + " item=" + shortname);
            ReplyAdmin(arg, Text("ItemRemoved", arg.Player(), (removed > 0 ? Text("Removed", arg.Player()) : Text("NotConfigured", arg.Player())), shortname));
        }

        [ConsoleCommand("wabi.target")]
        private void CmdWabiTarget(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            CompensationCampaign campaign;
            if (!RequireEditableCampaign(arg, out campaign)) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                ReplyAdmin(arg, Text("UsageTarget", arg.Player()));
                return;
            }

            string action = GetArgString(arg, 0).ToLowerInvariant();

            if (action == "all")
            {
                campaign.TargetMode = TargetAll;
                campaign.TargetSteamIds.Clear();
                SaveData();
                Puts("[Wabi] ACTION wabi.target all by " + DescribeActor(arg) + " campaign=" + campaign.Id);
                ReplyAdmin(arg, Text("TargetAll", arg.Player()));
                return;
            }

            if (action == "clear")
            {
                campaign.TargetMode = TargetSelected;
                campaign.TargetSteamIds.Clear();
                SaveData();
                Puts("[Wabi] ACTION wabi.target clear by " + DescribeActor(arg) + " campaign=" + campaign.Id);
                ReplyAdmin(arg, Text("TargetClear", arg.Player()));
                return;
            }

            if ((action == "add" || action == "remove") && arg.Args.Length >= 2)
            {
                string steamId = GetArgString(arg, 1);
                ulong parsed;
                if (!ulong.TryParse(steamId, out parsed) || steamId.Length < 16)
                {
                    ReplyAdmin(arg, Text("InvalidSteamId", arg.Player()));
                    return;
                }

                campaign.TargetMode = TargetSelected;

                if (action == "add")
                {
                    campaign.TargetSteamIds.Add(steamId);
                    ReplyAdmin(arg, Text("TargetAdded", arg.Player(), steamId));
                }
                else
                {
                    campaign.TargetSteamIds.Remove(steamId);
                    ReplyAdmin(arg, Text("TargetRemoved", arg.Player(), steamId));
                }

                SaveData();
                Puts("[Wabi] ACTION wabi.target " + action + " by " + DescribeActor(arg) + " campaign=" + campaign.Id + " steamid=" + steamId);
                return;
            }

            ReplyAdmin(arg, Text("UsageTarget", arg.Player()));
        }

        [ConsoleCommand("wabi.expire")]
        private void CmdWabiExpire(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            CompensationCampaign campaign;
            if (!RequireEditableCampaign(arg, out campaign)) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                ReplyAdmin(arg, Text("UsageExpire", arg.Player()));
                return;
            }

            string input = JoinArgStrings(arg, 0);

            if (input.Equals("default", StringComparison.OrdinalIgnoreCase))
            {
                campaign.UsesFixedExpiry = false;
                campaign.ExpiryDurationMinutes = DefaultExpiryMinutes();
                campaign.ExpiresAtUnix = 0;
                SaveData();
                Puts("[Wabi] ACTION wabi.expire default by " + DescribeActor(arg) + " campaign=" + campaign.Id);
                ReplyAdmin(arg, Text("ExpiryDefault", arg.Player(), campaign.ExpiryDurationMinutes));
                return;
            }

            int durationMinutes;
            if (TryParseDurationMinutes(input, out durationMinutes))
            {
                campaign.UsesFixedExpiry = false;
                campaign.ExpiryDurationMinutes = durationMinutes;
                campaign.ExpiresAtUnix = 0;
                SaveData();
                Puts("[Wabi] ACTION wabi.expire " + durationMinutes + "m by " + DescribeActor(arg) + " campaign=" + campaign.Id);
                ReplyAdmin(arg, Text("ExpiryDuration", arg.Player(), durationMinutes));
                return;
            }

            DateTime localTime;
            string[] formats = { "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss" };
            if (!DateTime.TryParseExact(input, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out localTime))
            {
                ReplyAdmin(arg, Text("InvalidExpiry", arg.Player()));
                return;
            }

            localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Local);
            long expiryUnix = ToUnix(localTime.ToUniversalTime());
            if (expiryUnix <= UnixNow())
            {
                ReplyAdmin(arg, Text("PastExpiry", arg.Player()));
                return;
            }

            campaign.UsesFixedExpiry = true;
            campaign.ExpiresAtUnix = expiryUnix;
            SaveData();
            Puts("[Wabi] ACTION wabi.expire fixed by " + DescribeActor(arg) + " campaign=" + campaign.Id + " expires=" + FormatUnix(expiryUnix));
            ReplyAdmin(arg, Text("ExpiryFixed", arg.Player(), FormatUnix(expiryUnix)));
        }

        [ConsoleCommand("wabi.start")]
        private void CmdWabiStart(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            CompensationCampaign campaign = GetCurrentCampaign();
            if (campaign == null)
            {
                ReplyAdmin(arg, Text("NoCampaign", arg.Player()));
                return;
            }

            if (campaign.Active && !IsExpired(campaign))
            {
                ReplyAdmin(arg, Text("AlreadyActive", arg.Player()));
                return;
            }

            string validationError;
            if (!ValidateCampaignItems(campaign, out validationError, arg.Player()))
            {
                ReplyAdmin(arg, Text("CannotStart", arg.Player(), validationError));
                return;
            }

            if (campaign.TargetMode == TargetSelected && campaign.TargetSteamIds.Count == 0)
            {
                ReplyAdmin(arg, Text("NoTargets", arg.Player()));
                return;
            }

            if (campaign.SeasonId != data.CurrentSeasonId)
            {
                ReplyAdmin(arg, Text("WrongSeason", arg.Player()));
                return;
            }

            long now = UnixNow();
            if (campaign.UsesFixedExpiry)
            {
                if (campaign.ExpiresAtUnix <= now)
                {
                    ReplyAdmin(arg, Text("FixedExpired", arg.Player()));
                    return;
                }
            }
            else
            {
                if (campaign.ExpiryDurationMinutes <= 0)
                    campaign.ExpiryDurationMinutes = DefaultExpiryMinutes();

                campaign.ExpiresAtUnix = now + campaign.ExpiryDurationMinutes * 60L;
            }

            campaign.StartedAtUnix = now;
            campaign.Active = true;
            SaveData();

            Puts("[Wabi] ACTION wabi.start by " + DescribeActor(arg) + " campaign=" + campaign.Id + " expires=" + FormatUnix(campaign.ExpiresAtUnix));
            ReplyAdmin(arg, Text("Started", arg.Player(), campaign.Id, FormatUnix(campaign.ExpiresAtUnix), DescribeTarget(campaign)));
        }

        [ConsoleCommand("wabi.stop")]
        private void CmdWabiStop(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            CompensationCampaign campaign = GetCurrentCampaign();
            if (campaign == null)
            {
                ReplyAdmin(arg, Text("NoCurrent", arg.Player()));
                return;
            }

            campaign.Active = false;
            SaveData();
            Puts("[Wabi] ACTION wabi.stop by " + DescribeActor(arg) + " campaign=" + campaign.Id);
            ReplyAdmin(arg, Text("Stopped", arg.Player(), campaign.Id));
        }

        [ConsoleCommand("wabi.list")]
        private void CmdWabiList(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            CompensationCampaign campaign = GetCurrentCampaign();
            if (campaign == null)
            {
                ReplyAdmin(arg, Text("NoCampaignList", arg.Player()));
                return;
            }

            if (campaign.Active && IsExpired(campaign))
            {
                campaign.Active = false;
                SaveData();
            }

            string items = campaign.Items.Count == 0
                ? "(none)"
                : string.Join(", ", campaign.Items.Select(x => x.Shortname + " x" + x.Amount).ToArray());

            int claimed = campaign.Claims.Values.Count(x => x.Status == "claimed");
            int pending = campaign.Claims.Values.Count(x => x.Status == "pending");
            int failed = campaign.Claims.Values.Count(x => x.Status == "failed");

            ReplyAdmin(arg, Text("CampaignList", arg.Player(), campaign.Id, campaign.SeasonId, (campaign.Active ? "ACTIVE" : "STOPPED"), FormatUnix(campaign.CreatedAtUnix), (campaign.StartedAtUnix > 0 ? FormatUnix(campaign.StartedAtUnix) : "-"), (campaign.ExpiresAtUnix > 0 ? FormatUnix(campaign.ExpiresAtUnix) : "not started"), (campaign.UsesFixedExpiry ? "fixed" : campaign.ExpiryDurationMinutes + " minutes from start"), DescribeTarget(campaign), items, claimed, pending, failed));
        }

        [ConsoleCommand("wabi.check")]
        private void CmdWabiCheck(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                ReplyAdmin(arg, Text("UsageCheck", arg.Player()));
                return;
            }

            string steamId = GetArgString(arg, 0);
            CompensationCampaign campaign = GetCampaignFromOptionalArg(arg, 1);
            if (campaign == null)
            {
                ReplyAdmin(arg, Text("CampaignMissing", arg.Player()));
                return;
            }

            ClaimRecord claim;
            if (!campaign.Claims.TryGetValue(steamId, out claim))
            {
                ReplyAdmin(arg, Text("NotClaimed", arg.Player(), campaign.Id, steamId));
                return;
            }

            ReplyAdmin(arg, Text("ClaimDetails", arg.Player(), campaign.Id, claim.SteamId, claim.PlayerName, claim.Status, FormatUnix(claim.StartedAtUnix), (claim.CompletedAtUnix > 0 ? FormatUnix(claim.CompletedAtUnix) : "-"), (claim.Delivered.Count > 0 ? string.Join(", ", claim.Delivered.ToArray()) : "-"), (string.IsNullOrEmpty(claim.Error) ? "-" : claim.Error)));
        }

        [ConsoleCommand("wabi.reset")]
        private void CmdWabiReset(ConsoleSystem.Arg arg)
        {
            if (!RequireAdmin(arg)) return;

            if (arg.Args == null || arg.Args.Length < 1)
            {
                ReplyAdmin(arg, Text("UsageReset", arg.Player()));
                return;
            }

            string steamId = GetArgString(arg, 0);
            CompensationCampaign campaign = GetCampaignFromOptionalArg(arg, 1);
            if (campaign == null)
            {
                ReplyAdmin(arg, Text("CampaignMissing", arg.Player()));
                return;
            }

            ClaimRecord oldClaim;
            if (!campaign.Claims.TryGetValue(steamId, out oldClaim))
            {
                ReplyAdmin(arg, Text("NoClaim", arg.Player()));
                return;
            }

            campaign.Claims.Remove(steamId);
            SaveData();

            PrintWarning("[Wabi] CLAIM RESET by " + DescribeActor(arg) + " campaign=" + campaign.Id + " steamid=" + steamId + " previousStatus=" + oldClaim.Status);
            ReplyAdmin(arg, Text("ResetClaim", arg.Player(), steamId, campaign.Id));
        }

        #endregion

        #region Helpers

        private static readonly Regex RichTextTagRegex = new Regex("<.*?>", RegexOptions.Compiled);

        // Player display names are attacker-controlled and can contain rich text tags or control
        // characters; strip them before persisting/echoing the name back into logs or admin replies.
        private string SanitizeDisplayName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "(unknown)";

            string noTags = RichTextTagRegex.Replace(name, string.Empty);

            StringBuilder builder = new StringBuilder(noTags.Length);
            foreach (char c in noTags)
            {
                if (!char.IsControl(c))
                    builder.Append(c);
            }

            string cleaned = builder.ToString().Trim();
            return string.IsNullOrEmpty(cleaned) ? "(unknown)" : cleaned;
        }

        private string DescribeActor(ConsoleSystem.Arg arg)
        {
            BasePlayer player = arg == null ? null : arg.Player();
            if (player == null)
                return "server console/RCON";

            return SanitizeDisplayName(player.displayName) + " (" + player.UserIDString + ")";
        }

        private bool RequireAdmin(ConsoleSystem.Arg arg)
        {
            if (arg == null)
                return false;

            BasePlayer player = arg.Player();
            if (arg.Connection != null && (player == null || (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PermAdmin))))
            {
                ReplyAdmin(arg, Text("NoPermission", player, PermAdmin));
                return false;
            }
            if (!dataHealthy || config == null)
            {
                ReplyAdmin(arg, Text("UnavailableState", player));
                return false;
            }
            return true;
        }

        private bool RequireEditableCampaign(ConsoleSystem.Arg arg, out CompensationCampaign campaign)
        {
            campaign = GetCurrentCampaign();
            if (campaign == null)
            {
                ReplyAdmin(arg, Text("NoCampaign", arg.Player()));
                return false;
            }

            if (campaign.Active && IsExpired(campaign))
            {
                campaign.Active = false;
                SaveData();
            }

            if (campaign.Active)
            {
                ReplyAdmin(arg, Text("MustStopEditing", arg.Player()));
                return false;
            }

            return true;
        }

        private CompensationCampaign GetCurrentCampaign()
        {
            if (data == null || string.IsNullOrEmpty(data.CurrentCampaignId))
                return null;

            CompensationCampaign campaign;
            return data.Campaigns.TryGetValue(data.CurrentCampaignId, out campaign) ? campaign : null;
        }

        private CompensationCampaign GetCampaignFromOptionalArg(ConsoleSystem.Arg arg, int index)
        {
            string campaignId = GetArgString(arg, index);
            if (!string.IsNullOrEmpty(campaignId))
            {
                CompensationCampaign selected;
                return data.Campaigns.TryGetValue(campaignId, out selected) ? selected : null;
            }

            return GetCurrentCampaign();
        }

        private string GetArgString(ConsoleSystem.Arg arg, int index)
        {
            if (arg == null || arg.Args == null || index < 0 || arg.Args.Length <= index)
                return string.Empty;

            return ((string)arg.Args[index]).Trim();
        }

        private string JoinArgStrings(ConsoleSystem.Arg arg, int startIndex)
        {
            if (arg == null || arg.Args == null || startIndex < 0 || arg.Args.Length <= startIndex)
                return string.Empty;

            List<string> parts = new List<string>();
            for (int i = startIndex; i < arg.Args.Length; i++)
                parts.Add((string)arg.Args[i]);

            return string.Join(" ", parts.ToArray()).Trim();
        }

        private bool IsTargetPlayer(CompensationCampaign campaign, string steamId)
        {
            return campaign.TargetMode == TargetAll || campaign.TargetSteamIds.Contains(steamId);
        }

        private bool IsExpired(CompensationCampaign campaign)
        {
            return campaign.ExpiresAtUnix > 0 && UnixNow() >= campaign.ExpiresAtUnix;
        }

        private bool ValidateCampaignItems(CompensationCampaign campaign, out string error, BasePlayer requester = null)
        {
            if (campaign.Items == null || campaign.Items.Count == 0)
            {
                error = Text("NoItems", requester);
                return false;
            }

            long stacks = 0;
            foreach (CompensationItem reward in campaign.Items)
            {
                if (reward.Amount <= 0)
                {
                    error = Text("InvalidReward", requester, reward.Shortname);
                    return false;
                }

                if (ItemManager.FindItemDefinition(reward.Shortname) == null)
                {
                    error = Text("InvalidReward", requester, reward.Shortname);
                    return false;
                }
                int stackSize = Math.Max(1, ItemManager.FindItemDefinition(reward.Shortname).stackable);
                stacks += ((long)reward.Amount + stackSize - 1) / stackSize;
                if (stacks > 1000)
                {
                    error = Text("TooManyStacks", requester);
                    return false;
                }
            }

            error = null;
            return true;
        }

        private string DescribeTarget(CompensationCampaign campaign)
        {
            if (campaign.TargetMode == TargetAll)
                return "all players";

            return "selected players (" + campaign.TargetSteamIds.Count + ")";
        }

        private string GenerateCampaignId()
        {
            string baseId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string id = baseId;
            int suffix = 2;

            while (data.Campaigns.ContainsKey(id))
            {
                id = baseId + "-" + suffix;
                suffix++;
            }

            return id;
        }

        private int DefaultExpiryMinutes()
        {
            return Math.Max(1, (int)Math.Round(config.DefaultExpiryHours * 60.0));
        }

        // Upper bound (~10 years in minutes) so an extreme input can't overflow the int minutes
        // field when cast below.
        private const double MaxDurationMinutes = 5256000.0;

        private bool TryParseDurationMinutes(string input, out int minutes)
        {
            minutes = 0;
            if (string.IsNullOrEmpty(input) || input.Length < 2)
                return false;

            string lower = input.Trim().ToLowerInvariant();
            char unit = lower[lower.Length - 1];
            string numberPart = lower.Substring(0, lower.Length - 1);
            double value;

            if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value <= 0)
                return false;

            double computedMinutes;
            if (unit == 'h')
                computedMinutes = value * 60.0;
            else if (unit == 'm')
                computedMinutes = value;
            else
                return false;

            if (double.IsNaN(computedMinutes) || double.IsInfinity(computedMinutes) || computedMinutes <= 0 || computedMinutes > MaxDurationMinutes)
                return false;

            minutes = (int)Math.Round(computedMinutes);
            return minutes > 0;
        }

        private long UnixNow()
        {
            return ToUnix(DateTime.UtcNow);
        }

        private long ToUnix(DateTime utc)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(utc.ToUniversalTime() - epoch).TotalSeconds;
        }

        private DateTime FromUnix(long unix)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddSeconds(unix);
        }

        private string FormatUnix(long unix)
        {
            if (unix <= 0)
                return "-";

            return FromUnix(unix).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private void ReplyPlayer(BasePlayer player, string message)
        {
            SendReply(player, "<color=#ffd479>[Wabi]</color> " + message);
        }

        private void ReplyAdmin(ConsoleSystem.Arg arg, string message)
        {
            arg.ReplyWith(message);
        }

        #endregion
    }
}
