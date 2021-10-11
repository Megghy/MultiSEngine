using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Modules.Cmds
{
    internal class InternalCommand : Core.Command.CmdBase
    {
        public override string Name => "mce";

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
                }
            }
            
        }
        private static void SwitchServer(ClientData client, string serverName)
        {
            if (client.State >= ClientData.ClientState.ReadyToSwitch)
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
