using MultiSEngine.DataStruct;

namespace MultiSEngine.Modules.Cmds
{
    internal class InternalCommand : Core.Command.CmdBase
    {
        public override string Name => "mse";
        public override async ValueTask<bool> Execute(ClientData client, string cmdName, string[] parma)
        {
            if (client is null)
            {
                Logs.Error("Unable to execute this command.");
                return false;
            }
            else if (parma.Length != 0)
            {
                switch (parma.First().ToLower())
                {
                    case "tp":
                    case "to":
                    case "t":
                        if (parma.Length < 2)
                            await client.SendInfoMessageAsync($"{Localization.Get("Prompt_InvalidFormat")}{Environment.NewLine}{Localization.Get("Help_Tp")}").ConfigureAwait(false);
                        else
                            await SwitchServer(client, parma[1]).ConfigureAwait(false);
                        break;
                    case "back":
                    case "b":
                        if (client.State == ClientState.NewConnection)
                            await client.SendInfoMessageAsync($"{Localization.Get("Command_NotJoined")}").ConfigureAwait(false);
                        else if (client.CurrentServer == Config.Instance.DefaultServerInternal)
                            await client.SendErrorMessageAsync(string.Format(Localization.Get("Command_AlreadyIn"), client.CurrentServer.Name)).ConfigureAwait(false);
                        else
                            await client.BackAsync().ConfigureAwait(false);
                        break;
                    case "list":
                    case "l":
                        await client.SendSuccessMessageAsync($"{Localization.Get("Command_AviliableServer")}{Environment.NewLine + "- "}{string.Join(Environment.NewLine + "- ", (from server in Config.Instance.Servers let text = $"{server.Name} {(string.IsNullOrEmpty(server.ShortName) ? "" : $"[{server.ShortName}]")} <{server.Online().Length}>" select text))}").ConfigureAwait(false);
                        break;
                    case "password":
                    case "pass":
                    case "p":
                        if (parma.Length > 1)
                        {
                            if (client.State == ClientState.RequestPassword)
                            {
                                if (client.TempAdapter is { } adapter)
                                {
                                    await adapter.SendToServerDirectAsync(new SendPassword
                                    {
                                        Password = parma[1]
                                    });
                                }
                            }
                            else
                                await client.SendErrorMessageAsync(Localization.Get("Command_NotJoined")).ConfigureAwait(false);
                        }
                        else
                            await client.SendInfoMessageAsync($"{Localization.Get("Prompt_InvalidFormat")}{Environment.NewLine}{Localization.Get("Help_Password")}").ConfigureAwait(false);
                        break;
#if DEBUG
                    case "let":
                        if (parma.Length < 3)
                            Console.Write("error /mse let name server");
                        else
                            if (Data.Clients.FirstOrDefault(c => c.Name.ToLower().StartsWith(parma[1].ToLower())) is { } targetClient)
                            {
                                await targetClient.Join(Utils.GetSingleServerInfoByName(parma[2])).ConfigureAwait(false);
                            }
                        break;
#endif
                    default:
                        await SendHelpTextAsync().ConfigureAwait(false);
                        break;
                }
            }
            else
                await SendHelpTextAsync().ConfigureAwait(false);
            return false;

            async ValueTask SendHelpTextAsync()
            {
                await client.SendInfoMessageAsync($"{Localization.Get("Prompt_InvalidFormat")}\r\n" +
                    $"{Localization.Get("Help_Tp")}\r\n" +
                    $"{Localization.Get("Help_Back")}\r\n" +
                    $"{Localization.Get("Help_List")}\r\n" +
                    $"{Localization.Get("Help_Command")}" 
                    ).ConfigureAwait(false);
            }
        }
        private async static Task SwitchServer(ClientData client, string serverName, CancellationToken cancel = default)
        {
            if (client.State > ClientState.ReadyToSwitch && client.State < ClientState.InGame)
            {
                await client.SendErrorMessageAsync(Localization.Get("Command_IsSwitching")).ConfigureAwait(false);
                return;
            }
            if (Utils.GetServersInfoByName(serverName).FirstOrDefault() is { } server)
            {
                if (client.CurrentServer == server)
                    await client.SendErrorMessageAsync(string.Format(Localization.Get("Command_AlreadyIn"), server.Name)).ConfigureAwait(false);
                else
                {
                    await client.SendInfoMessageAsync(string.Format(Localization.Get("Command_Switch"), server.Name)).ConfigureAwait(false);
                    await client.Join(server, cancel).ConfigureAwait(false);
                }
            }
            else
                await client.SendErrorMessageAsync(string.Format(Localization.Get("Command_ServerNotFound"), serverName)).ConfigureAwait(false);
        }
    }
}
