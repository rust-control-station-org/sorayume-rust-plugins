using System.Collections.Generic;
using ConVar;

namespace Oxide.Plugins
{
    [Info("sorayume-AutoReply", "mazurk4", "1.3.1")]
    [Description("Responds to specific chat triggers (!vip, !discord, !kit, !rules) using Oxide lang files.")]
    public class AutoReply : RustPlugin
    {
        // -------------------------------------------------------------------
        // 言語ファイルの初期化
        // -------------------------------------------------------------------
        protected override void LoadDefaultMessages()
        {
            // English
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Reply.VIP"] =
                    "<color=#55aaff>[VIP]</color> VIP includes queue skip and a special chat tag.",

                ["Reply.Discord"] =
                    "<color=#7289da>[Discord]</color> Join our official Discord: https://discord.gg/yourserver",

                ["Reply.Kit"] =
                    "<color=#ffaa00>[Kit]</color> Kit claims (!kit) are not supported on this server.",

                ["Reply.Rules"] =
                    "<color=#ff5555>[Rules]</color> Please check the server rules on our official Discord."
            }, this, "en");


            // Japanese
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Reply.VIP"] =
                    "<color=#55aaff>[VIP]</color> VIPにはキュースキップとチャットの特別表示が含まれます。",

                ["Reply.Discord"] =
                    "<color=#7289da>[Discord]</color> 公式Discordはこちら: https://discord.gg/yourserver",

                ["Reply.Kit"] =
                    "<color=#ffaa00>[Kit]</color> 当サーバーにはキット機能（!kit）はありません。",

                ["Reply.Rules"] =
                    "<color=#ff5555>[Rules]</color> サーバールールは公式Discordをご確認ください。"
            }, this, "ja");
        }


        // -------------------------------------------------------------------
        // チャット検知
        // -------------------------------------------------------------------
        private object OnPlayerChat(
            BasePlayer player,
            string message,
            Chat.ChatChannel channel)
        {
            if (player == null || string.IsNullOrWhiteSpace(message))
                return null;

            // 前後の空白を削除し、大文字小文字を無視
            string trigger = message.Trim().ToLowerInvariant();

            string langKey = null;

            switch (trigger)
            {
                case "!vip":
                    langKey = "Reply.VIP";
                    break;

                case "!discord":
                    langKey = "Reply.Discord";
                    break;

                case "!kit":
                    langKey = "Reply.Kit";
                    break;

                case "!rules":
                    langKey = "Reply.Rules";
                    break;
            }


            // 登録されていないメッセージなら通常チャットとして処理
            if (langKey == null)
                return null;


            // ----------------------------------------------------------------
            // プレイヤー本人の言語設定に応じたメッセージを取得
            // ----------------------------------------------------------------
            string translatedMessage = lang.GetMessage(
                langKey,
                this,
                player.UserIDString
            );


            // ----------------------------------------------------------------
            // コマンドを入力した本人だけに返信
            // ----------------------------------------------------------------
            SendReply(player, translatedMessage);


            // ----------------------------------------------------------------
            // !vip 等の入力自体を通常チャットに表示させない
            // ----------------------------------------------------------------
            return false;
        }
    }
}


