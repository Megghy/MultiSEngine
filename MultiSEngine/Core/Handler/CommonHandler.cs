using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;

namespace MultiSEngine.Core.Handler
{
    public class CommonHandler(BaseAdapter parent) : BaseHandler(parent)
    {
        public override async ValueTask<bool> RecieveServerDataAsync(HandlerPacketContext context)
        {
            var msgType = context.MessageId;
            switch (msgType)
            {
                case MessageID.Kick:
                    var kick = context.Packet as Kick ?? throw new Exception("[CommonHandler] Kick packet not found");
                    Client.State = ClientState.Disconnect;
                    var reason = kick.Reason.GetText();
                    Logs.Info($"Player {Client.Player.Name} is removed from server {Client.CurrentServer.Name}, for the following reason:{reason}");
                    await Client.SendErrorMessageAsync(string.Format(Localization.Instance["Prompt_Disconnect", Client.CurrentServer.Name, kick.Reason.GetText()])).ConfigureAwait(false);
                    await Client.BackAsync();
                    return true;
            }
            return false;
        }
    }
}
