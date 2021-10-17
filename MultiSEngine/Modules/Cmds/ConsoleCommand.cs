using MultiSEngine.Modules.DataStruct;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MultiSEngine.Modules.Cmds
{
    internal class ConsoleCommand : Core.Command.CmdBase
    {
        public override string Name => "";
        public override bool ServerCommand => true;

        public override bool Execute(ClientData client, string cmdName, List<string> parma)
        {
            var internalCommand = Data.Commands.FirstOrDefault(c => c.Name == "mce");
            switch (cmdName)
            {
                case "list":
                    Logs.Info($"{Localization.Get("Command_AviliableServer")}{Environment.NewLine + "- "}{string.Join(Environment.NewLine + "- ", from server in Config.Instance.Servers let text = $"{server.Name} <{server.Online()}>" select text)}", false);
                    break;
                case "online":
                    Logs.Info($"{Data.Clients.Count} Player(s) Online:{Environment.NewLine}{string.Join(", ", from c in Data.Clients let text = $"{c.Name} <{c.Server?.Name ?? "FakeWorld"}>" select text)}");
                    break;
                case "stop":
                case "exit":
                    return false;
                case "help":
                default:
                    Logs.Info($"Avaliable console commands:{Environment.NewLine}- stop(exit){Environment.NewLine}- list{Environment.NewLine}- online");
                    break;
            }
            Data.Commands.FirstOrDefault(c => c.Name == "mce")?.Execute(client, cmdName, parma);
            return true;
        }
    }
}
