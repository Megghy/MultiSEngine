using MultiSEngine.DataStruct;
using System;
using System.Linq;

namespace MultiSEngine.Modules.Cmds
{
    internal class InternalCommand : Core.Command.CmdBase
    {
        public override string Name => "mse";
        public override bool Execute(ClientData client, string cmdName, string[] parma)
        {
            if (client is null)
                client.SendErrorMessage("Unable to execute this command.");
            else if (parma.Any())
            {
                switch (parma.First().ToLower())
                {
                    case "tp":
                    case "to":
                    case "t":
                        if (parma.Length < 2)
                            client.SendInfoMessage($"{Localization.Get("Prompt_InvalidFormat")}{Environment.NewLine}{Localization.Get("Help_Tp")}");
                        else
                            SwitchServer(client, parma[1]);
                        break;
                    case "back":
                    case "b":
                        if (client.State == ClientData.ClientState.NewConnection)
                            client.SendInfoMessage($"{Localization.Get("Command_NotJoined")}");
                        else if (client.Server == Config.Instance.DefaultServerInternal)
                            client.SendErrorMessage(string.Format(Localization.Get("Command_AlreadyIn"), client.Server.Name));
                        else
                            client.Back();
                        break;
                    case "list":
                    case "l":
                        client.SendSuccessMessage($"{Localization.Get("Command_AviliableServer")}{Environment.NewLine + "- "}{string.Join(Environment.NewLine + "- ", (from server in Config.Instance.Servers let text = $"{server.Name} {(string.IsNullOrEmpty(server.ShortName) ? "" : $"[{server.ShortName}]")} <{server.Online().Length}>" select text))}");
                        break;
                    case "password":
                    case "pass":
                    case "p":
                        if (parma.Length > 1)
                        {
                            if (client.State == ClientData.ClientState.RequestPassword)
                                client.TempAdapter.InternalSendPacket(new TrProtocol.Packets.SendPassword()
                                {
                                    Password = parma[1]
                                });
                            else
                                client.SendErrorMessage(Localization.Get("Command_NotJoined"));
                        }
                        else
                            client.SendInfoMessage($"{Localization.Get("Prompt_InvalidFormat")}{Environment.NewLine}{Localization.Get("Help_Password")}");
                        break;
#if DEBUG
                    case "let":
                        if (parma.Length < 3)
                            Console.Write("error /mse let name server");
                        else
                            Data.Clients.FirstOrDefault(c => c.Name.ToLower().StartsWith(parma[1].ToLower()))?.Join(Utils.GetSingleServerInfoByName(parma[2]));
                        break;
#endif
                    default:
                        SendHelpText();
                        break;
                }
            }
            else
                SendHelpText();
            return false;
            void SendHelpText()
            {
                client.SendInfoMessage($"{Localization.Get("Prompt_InvalidFormat")}\r\n" +
                    $"{Localization.Get("Help_Tp")}\r\n" +
                    $"{Localization.Get("Help_Back")}\r\n" +
                    $"{Localization.Get("Help_List")}\r\n" +
                    $"{Localization.Get("Help_Command")}"
                    );
            }
        }
        private static void SwitchServer(ClientData client, string serverName)
        {
            if (client.State > ClientData.ClientState.ReadyToSwitch && client.State < ClientData.ClientState.InGame)
            {
                client.SendErrorMessage(Localization.Get("Command_IsSwitching"));
                return;
            }
            if (Utils.GetServersInfoByName(serverName).FirstOrDefault() is { } server)
            {
                if (client.Server == server)
                    client.SendErrorMessage(string.Format(Localization.Get("Command_AlreadyIn"), server.Name));
                else
                {
                    client.SendInfoMessage(string.Format(Localization.Get("Command_Switch"), server.Name));
                    client.Join(server);
                }
            }
            else
                client.SendErrorMessage(string.Format(Localization.Get("Command_ServerNotFound"), serverName));
        }
    }
}
