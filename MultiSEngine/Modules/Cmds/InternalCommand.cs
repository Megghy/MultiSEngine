using System;
using System.Collections.Generic;
using System.Linq;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Modules.Cmds
{
    internal class InternalCommand : Core.Command.CmdBase
    {
        public override string Name => "mse";

        public override void Execute(ClientData client, string cmdName, List<string> cmd)
        {
            if (cmd.Any())
            {
                switch (cmd.First().ToLower())
                {
                    case "tp":
                    case "to":
                    case "t":
                        if (cmd.Count < 2)
                            client.SendInfoMessage($"{Localization.Get("Prompt_InvalidFormat")}{Environment.NewLine}{Localization.Get("Help_Tp")}");
                        else
                            SwitchServer(client, cmd[1]);
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
                        client.SendMessage($"{Localization.Get("Command_AviliableServer")}{string.Join(", ", Config.Instance.Servers.Where(s => s.Visible).Select(s => s.Name))}");
                        break;
                    default:
                        SendHelpText();
                        break;
                }
            }
            else
                SendHelpText();
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
            if (Utils.GetServerInfoByName(serverName).FirstOrDefault() is { } server)
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
