using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Handler
{
    public class CommonHandler : BaseHandler
    {
        public CommonHandler(BaseAdapter parent) : base(parent)
        {
        }
        public override bool RecieveServerData(MessageID msgType, byte[] data)
        {
            switch (msgType)
            {
                case MessageID.Kick:
                    var kick = data.AsPacket<Kick>();
                    Client.State = ClientState.Disconnect;
                    var reason = kick.Reason.GetText();
                    Logs.Info($"Player {Client.Player.Name} is removed from server {Client.CurrentServer.Name}, for the following reason:{reason}");
                    Client.SendErrorMessage(string.Format(Localization.Instance["Prompt_Disconnect", Client.CurrentServer.Name, kick.Reason.GetText()]));
                    Client.Back();
                    return true;
            }
            return false;
        }
    }
}
