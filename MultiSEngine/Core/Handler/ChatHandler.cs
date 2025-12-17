using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;

namespace MultiSEngine.Core.Handler
{
    public class ChatHandler(BaseAdapter parent) : BaseHandler(parent)
    {
        public override async ValueTask<bool> RecieveClientDataAsync(HandlerPacketContext context)
        {
            if (context.Packet is NetTextModuleC2S chat && !Hooks.OnChat(Client, chat, out _))
            {
                var text = chat;
                Logs.LogAndSave($"{Client.Name} <{Client.CurrentServer?.Name}>: {text.Text}", "[Chat]");
                var (handled, continueSend) = await Command.HandleCommand(Client, text.Text).ConfigureAwait(false);
                if (text.Command == "Say" && (handled && !continueSend))
                    return true;
                else if (Client.State == ClientState.NewConnection)
                {
                    await Client.SendInfoMessageAsync($"{Localization.Instance["Command_NotEntered"]}\r\n" +
                        $"{Localization.Instance["Help_Tp"]}\r\n" +
                        $"{Localization.Instance["Help_Back"]}\r\n" +
                        $"{Localization.Instance["Help_List"]}\r\n" +
                        $"{Localization.Instance["Help_Command"]}"
                    ).ConfigureAwait(false);
                    return true;
                }
                else
                {
                    if (Config.Instance.EnableChatForward && !text.Text.StartsWith('/'))
                    {
                        var msg = Config.Instance.ChatFormat
                            .Replace("{servername}", Client.CurrentServer?.Name ?? "Not Join")
                            .Replace("username", Client.Name)
                            .Replace("{message}", text.Text);
                        foreach (var c in Data.Clients.Where(c => c.CurrentServer != Client.CurrentServer))
                            await c.SendMessageAsync(msg).ConfigureAwait(false);
                    }
                    if (Client.CurrentServer is null)
                        await SendToClientDirectAsync(new NetTextModuleS2C()
                        {
                            Text = NetworkText.FromLiteral(text.Text),
                            PlayerSlot = Client.Index,
                            Color = Color.White,
                        }).ConfigureAwait(false);
                }
            }
            return false;
        }
    }
}
