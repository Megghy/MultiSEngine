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
                    Logs.Info($"{Localization.Instance["Command_AviliableServer"]}{Environment.NewLine + "- "}{string.Join(Environment.NewLine + "- ", from server in Config.Instance.Servers let text = $"{server.Name} <{server.Online()}>" select text)}", false);
                    break;
                case "online":
                    Logs.Info($"{Data.Clients.Count} Player(s) Online:{Environment.NewLine}{string.Join(", ", from c in Data.Clients let text = $"{c.Name} <{c.Server?.Name ?? "FakeWorld"}>" select text)}", false);
                    break;
                case "stop":
                case "exit":
                    return false;
                case "kick":
                    if (parma.Any())
                    {
                        if (Data.Clients.FirstOrDefault(c => c.Name.StartsWith(parma[0]) || c.Name.Contains(parma[0])) is { } c)
                            c.Disconnect($"Kicked by server: {(parma.Count > 1 ? parma[1] : "Unknown")}");
                        else
                            Logs.Error($"Specified player: [{parma[0]}] not found.");
                    }
                    else
                        Logs.Error(Localization.Instance["Prompt_InvalidFormat"]);
                    break;
                case "reload":
                    Reload();
                    break;
                case "help":
                default:
                    Logs.Info($"Avaliable console commands:{Environment.NewLine}" +
                        $"- stop(exit){Environment.NewLine}" +
                        $"- kick <Player name> (reason)" +
                        $"- list{Environment.NewLine}" +
                        $"- online{Environment.NewLine}" +
                        $"- reload{Environment.NewLine}", false);
                    break;
            }
            Data.Commands.FirstOrDefault(c => c.Name == "mce")?.Execute(client, cmdName, parma);
            return true;
        }
        public static void Reload()
        {
            Config._instance = null;
            Localization._instance = null;
            Logs.Success("Successfully reloaded.");
        }
    }
}
