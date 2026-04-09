
namespace MultiSEngine.Protocol.Handlers
{
    public class ChatHandler(BaseAdapter parent) : BaseHandler(parent)
    {
        private static readonly MessageID[] ClientSubscriptions = [MessageID.NetModules];

        public override IReadOnlyList<MessageID>? ClientMessageSubscriptions => ClientSubscriptions;

        public override async ValueTask<bool> RecieveClientDataAsync(HandlerPacketContext context)
        {
            if (context.Packet is NetTextModule { TextC2S: { } chat } && !Hooks.OnChat(Client, chat, out _))
            {
                Logs.LogAndSave($"{Client.Name} <{Client.CurrentServer?.Name}>: {chat.Text}", "[Chat]");
                var (handled, continueSend) = await CommandDispatcher.HandleCommand(Client, chat.Text).ConfigureAwait(false);
                if (chat.Command == "Say" && (handled && !continueSend))
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
                    if (Config.Instance.EnableChatForward && !(chat.Text?.StartsWith('/') ?? false))
                    {
                        var msg = Config.Instance.ChatFormat
                            .Replace("{servername}", Client.CurrentServer?.Name ?? "Not Join")
                            .Replace("username", Client.Name)
                            .Replace("{message}", chat.Text);
                        foreach (var c in RuntimeState.ClientRegistry.Where(c => c.CurrentServer != Client.CurrentServer))
                            await c.SendMessageAsync(msg).ConfigureAwait(false);
                    }
                    if (Client.CurrentServer is null)
                        await SendToClientDirectAsync(new NetTextModule
                        {
                            TextS2C = new TextS2C
                            {
                                Text = Utils.LiteralText(chat.Text),
                                PlayerSlot = Client.Index,
                                Color = Utils.Rgb(255, 255, 255),
                            }
                        }).ConfigureAwait(false);
                }
            }
            return false;
        }
    }
}


