using MultiSEngine.DataStruct;
using System;
using System.Linq;

namespace MultiSEngine.Modules.Cmds
{
    internal class ConsoleCommand : Core.Command.CmdBase
    {
        public override string Name => "";
        public override bool ServerCommand => true;

        public override bool Execute(ClientData client, string cmdName, string[] parma)
        {
            var internalCommand = Data.Commands.FirstOrDefault(c => c.Name == "mse");
            switch (cmdName)
            {
                case "list":
                    Logs.Info($"{Localization.Instance["Command_AviliableServer"]}{Environment.NewLine + "- "}{string.Join(Environment.NewLine + "- ", from server in Config.Instance.Servers let text = $"{server.Name} <{server.Online().Length}>" select text)}", false);
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
                            c.Disconnect(Localization.Instance["Command_Kick", Config.Instance.ServerName, parma.Length > 1 ? parma[1] : "Unknown"]);
                        else
                            Logs.Error($"Specified player: [{parma[0]}] not found.");
                    }
                    else
                        Logs.Error(Localization.Instance["Prompt_InvalidFormat"]);
                    break;
                case "reload":
                    Config._instance = null;
                    Localization._instance = null;
                    Logs.Success("Successfully reloaded.");
                    break;
                case "reloadplugin":
                case "rp":
                    Core.PluginSystem.Reload();
                    break;
                case "broadcast":
                case "bc":
                    if (parma.Length > 1)
                    {
                        if (Utils.GetServerInfoByName(parma[1]).FirstOrDefault() is { } server)
                            Data.Clients.Where(c => c.Server == server).ForEach(c => c.SendMessage($"[Broadcast] {parma[0]}", false));
                        else
                            Logs.Error(string.Format(Localization.Get("Command_ServerNotFound"), parma[1]));
                    }
                    else
                        ClientHelper.Broadcast(null, parma.FirstOrDefault());
                    break;
                case "help":
                default:
                    Logs.Info($"Avaliable console commands:{Environment.NewLine}" +
                        $"- stop(exit){Environment.NewLine}" +
                        $"- kick <Player name> (reason){Environment.NewLine}" +
                        $"- broadcase(bc) <message> (target server){Environment.NewLine}" +
                        $"- list{Environment.NewLine}" +
                        $"- online{Environment.NewLine}" +
                        $"- reload{Environment.NewLine}" +
                        $"- reloadplugin(rp){Environment.NewLine}", false);
                    break;
            }
            Data.Commands.FirstOrDefault(c => c.Name == "mce")?.Execute(client, cmdName, parma);
            return true;
        }
    }
}
