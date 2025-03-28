using EnchCoreApi.TrProtocol.NetPackets.Modules;
using Microsoft.Xna.Framework;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using Terraria.Localization;

namespace MultiSEngine.Core.Handler
{
    public class ChatHandler(BaseAdapter parent) : BaseHandler(parent)
    {
        public override bool RecieveClientData(MessageID msgType, Span<byte> data)
        {
            if (msgType is MessageID.NetModules && data.AsPacket<NetTextModule>(true) is { TextC2S: not null } chat && !Hooks.OnChat(Client, chat.TextC2S, out _))
            {
                var text = chat.TextC2S;
                Logs.LogAndSave($"{Client.Name} <{Client.CurrentServer?.Name}>: {text.Text}", "[Chat]");
                if (text.Command == "Say" && (Command.HandleCommand(Client, text.Text, out var c) && !c))
                    return true;
                else if (Client.State == ClientState.NewConnection)
                {
                    Client.SendInfoMessage($"{Localization.Instance["Command_NotEntered"]}\r\n" +
                        $"{Localization.Instance["Help_Tp"]}\r\n" +
                        $"{Localization.Instance["Help_Back"]}\r\n" +
                        $"{Localization.Instance["Help_List"]}\r\n" +
                        $"{Localization.Instance["Help_Command"]}"
                    );
                    return true;
                }
                else
                {
                    if (Config.Instance.EnableChatForward && !text.Text.StartsWith('/'))
                    {
                        Data.Clients.Where(c => c.CurrentServer != Client.CurrentServer)
                            .ForEach(c => c.SendMessage(Config.Instance.ChatFormat.Replace("{servername}", Client.CurrentServer?.Name ?? "Not Join")
                            .Replace("username", Client.Name)
                            .Replace("{message}", text.Text)));
                    }
                    if (Client.CurrentServer is null)
                        Client.SendDataToClient(new NetTextModule(null, new TextS2C()
                        {
                            Text = NetworkTextModel.FromLiteral(text.Text),
                            PlayerSlot = Client.Index,
                            Color = Color.White,
                        }, true));
                }
            }
            return false;
        }
    }
}
