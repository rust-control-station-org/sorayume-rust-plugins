using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("sorayume-CheckJump", "mazurk4", "1.1.0")]
    [Description("Moves a player vertically for cheat checks (default: 20m).")]
    class CheckJump : RustPlugin
    {
        private const string permUse = "checkjump.use";

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "You do not have permission to use this command.",
                ["Usage"] = "Usage: /jmp <SteamID/name> [height (default: 20)]",
                ["PlayerNotFound"] = "Player '{0}' was not found.",
                ["JumpSuccess"] = "[CheckJump] Moved {0} {1}m upwards."
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoPermission"] = "このコマンドを実行する権限がありません。",
                ["Usage"] = "使用法: /jmp <SteamID/名前> [高さ(省略時20)]",
                ["PlayerNotFound"] = "プレイヤー '{0}' が見つかりません。",
                ["JumpSuccess"] = "[CheckJump] {0} を {1}m 上空へ飛ばしました。"
            }, this, "ja");
        }

        private string Message(string key, BasePlayer player, params object[] args)
        {
            string message = lang.GetMessage(key, this, player.UserIDString);
            return args.Length == 0 ? message : string.Format(message, args);
        }

        void Init()
        {
            permission.RegisterPermission(permUse, this);
        }

        [ChatCommand("jmp")]
        void JumpCommand(BasePlayer player, string command, string[] args)
        {
            // 権限チェック
            if (!permission.UserHasPermission(player.UserIDString, permUse) && !player.IsAdmin)
            {
                SendReply(player, Message("NoPermission", player));
                return;
            }

            // 引数チェック（最低限 SteamID または 名前が必要）
            if (args.Length < 1)
            {
                SendReply(player, Message("Usage", player));
                return;
            }

            // 対象プレイヤーの特定
            BasePlayer target = BasePlayer.Find(args[0]);
            if (target == null)
            {
                SendReply(player, Message("PlayerNotFound", player, args[0]));
                return;
            }

            // 高さの判定ロジック
            float distance = 20f; // デフォルト値を20に設定
            if (args.Length >= 2)
            {
                // 第2引数がある場合はその値を使用、数値でなければ20のまま
                float.TryParse(args[1], out distance);
            }

            // 位置の更新
            Vector3 newPos = target.transform.position;
            newPos.y += distance;

            // テレポート実行
            target.Teleport(newPos);

            // 実行者に通知
            SendReply(player, Message("JumpSuccess", player, target.displayName, distance));

            // ログ記録
            Puts($"Admin {player.displayName} jumped {target.displayName} by {distance}m.");
        }
    }
}


