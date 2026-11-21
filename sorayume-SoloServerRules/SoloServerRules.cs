using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("sorayume-SoloServerRules", "mazurk4", "1.2.0")]
    [Description("Prevents code sharing and sleeping bag/bed assignment on solo servers.")]
    public class SoloServerRules : RustPlugin
    {
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CodeSharingDenied"] = "<color=#ff5555>[Solo]</color> You cannot use another player's passcode on this solo server.",
                ["BedAssignmentDenied"] = "<color=#ff5555>[Solo]</color> You cannot assign sleeping bags or beds to other players on this solo server."
            }, this, "en");

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["CodeSharingDenied"] = "<color=#ff5555>[Solo]</color> ソロサーバーのため、他のプレイヤーが設定したパスコードを使用することはできません。",
                ["BedAssignmentDenied"] = "<color=#ff5555>[Solo]</color> ソロサーバーのため、他のプレイヤーに寝袋やベッドを割り当てることはできません。"
            }, this, "ja");
        }

        // -------------------------------------------------------------------
        // 他プレイヤーによる Code Lock のコード入力を禁止
        // -------------------------------------------------------------------
        private object OnCodeEntered(
            CodeLock codeLock,
            BasePlayer player,
            string code)
        {
            if (player == null || codeLock == null)
                return null;

            // Code Lock の所有者本人は許可
            if (codeLock.OwnerID == player.userID)
                return null;

            SendReply(
                player,
                lang.GetMessage("CodeSharingDenied", this, player.UserIDString)
            );

            // コード入力処理をキャンセル
            return false;
        }


        // -------------------------------------------------------------------
        // 寝袋・ベッドを他プレイヤーへ割り当てる操作を禁止
        // -------------------------------------------------------------------
        private object CanAssignBed(
            BasePlayer player,
            SleepingBag bag,
            ulong targetPlayerId)
        {
            if (player == null || bag == null)
                return null;

            // 念のため自分自身への割り当ては許可
            if (player.userID == targetPlayerId)
                return null;

            SendReply(
                player,
                lang.GetMessage("BedAssignmentDenied", this, player.UserIDString)
            );

            // Assign をキャンセル
            return false;
        }
    }
}

