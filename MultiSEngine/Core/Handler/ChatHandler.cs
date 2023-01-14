using System;
using System.Linq;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.Packets.Modules;

namespace MultiSEngine.Core.Handler
{
    public class ChatHandler : BaseHandler
    {
        public ChatHandler(BaseAdapter parent) : base(parent)
        {
        }

        public override bool RecieveClientData(MessageID msgType, ref Span<byte> data)
        {
            if (msgType is MessageID.NetModules && data.AsPacket() is NetTextModuleC2S chat && !Hooks.OnChat(Client, chat, out _))
            {
                Logs.LogAndSave($"{Client.Name} <{Client.CurrentServer?.Name}>: {chat.Text}", "[Chat]");
                if (chat.Command == "Say" && (Command.HandleCommand(Client, chat.Text, out var c) && !c))
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
                    if (Config.Instance.EnableChatForward && !chat.Text.StartsWith("/"))
                    {
                        Data.Clients.Where(c => c.CurrentServer != Client.CurrentServer)
                            .ForEach(c => c.SendMessage(Config.Instance.ChatFormat.Replace("{servername}", Client.CurrentServer?.Name ?? "Not Join")
                            .Replace("username", Client.Name)
                            .Replace("{message}", chat.Text)));
                    }
                    if (Client.CurrentServer is null)
                        Client.SendDataToClient(new NetTextModuleS2C()
                        {
                            Text = NetworkText.FromLiteral(chat.Text),
                            PlayerSlot = Client.Index,
                            Color = Color.White,
                        });
                    Client.SendDataToServer(chat, true);
                    
                }
                return true;
            }
            return false;
        }
    }
}
