
namespace MultiSEngine.Protocol.Handlers
{
    public class CommonHandler(BaseAdapter parent) : BaseHandler(parent)
    {
        private static readonly MessageID[] ServerSubscriptions = [MessageID.Kick];

        public override IReadOnlyList<MessageID>? ServerMessageSubscriptions => ServerSubscriptions;

        public override async ValueTask<bool> RecieveServerDataAsync(HandlerPacketContext context)
        {
            var msgType = context.MessageId;
            switch (msgType)
            {
                case MessageID.Kick:
                    if (context.Packet is not Kick kick)
                        throw new Exception("[CommonHandler] Kick packet not found");
                    var currentServer = Client.CurrentServer ?? throw new InvalidOperationException("[CommonHandler] Kick received without current server.");
                    var reason = kick.Reason.GetText();
                    Logs.Info($"Player {Client.Player.Name} is removed from server {currentServer.Name}, for the following reason:{reason}");
                    await Client.SendErrorMessageAsync(string.Format(Localization.Instance["Prompt_Disconnect", currentServer.Name, reason])).ConfigureAwait(false);
                    await Client.BackAsync();
                    return true;
            }
            return false;
        }
    }
}


