using MultiSEngine.DataStruct;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MultiSEngine.Modules.Cmds
{
    internal class ConsoleCommand : Core.Command.CmdBase
    {
        public override string Name => "";
        public override bool ServerCommand => true;
        public override bool Execute(ClientData client, string cmdName, string[] parma)
        {
            if (string.IsNullOrEmpty(cmdName))
                return true;
            var internalCommand = Data.Commands.FirstOrDefault(c => c.Name == "mse");
            switch (cmdName)
            {
                case "l":
                case "list":
                    Logs.Info($"{Localization.Instance["Command_AviliableServer"]}{Environment.NewLine + "- "}{string.Join(Environment.NewLine + "- ", from server in Config.Instance.Servers let text = $"{server.Name} <{server.Online().Length}>" select text)}", false);
                    break;
                case "ol":
                case "online":
                case "playing":
                    Logs.Info($"{Data.Clients.Count} Player(s) Online:{Environment.NewLine}{string.Join(", ", from c in Data.Clients let text = $"{c.Name} <{c.Server?.Name ?? "FakeWorld"}>" select text)}", false);
                    break;
                case "stop":
                case "exit":
                    return false;
                case "k":
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
                case "rl":
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
                        if (Utils.GetServersInfoByName(parma[1]).FirstOrDefault() is { } server)
                            Data.Clients.Where(c => c.Server == server).ForEach(c => c.SendMessage($"[Broadcast] {parma[0]}", false));
                        else
                            Logs.Error(string.Format(Localization.Get("Command_ServerNotFound"), parma[1]));
                    }
                    else
                        ClientHelper.Broadcast(null, $"[Broadcast] {parma.FirstOrDefault()}");
                    Logs.Info($"Broadcast: {(parma.Length > 1 ? parma[1] : parma[0])}");
                    break;
                case "t":
                case "test":
                    if (parma.Any())
                    {
                        bool showDetail = parma.Length > 1 && parma[1].ToLower() == "-detail";
                        if(parma[0].ToLower() == "all")
                            Core.Net.TestAll(showDetail);
                        else if (Utils.GetSingleServerInfoByName(parma[0]) is { } testServer)
                            Task.Run(() => Core.Net.TestConnect(testServer, showDetail));
                        else
                            Logs.Error($"The server named [{parma[0]}] was not found");
                    }
                    else
                        Logs.Error(Localization.Instance["Prompt_InvalidFormat"]);
                    break;
                case "help":
                default:
                    Logs.Info($"Avaliable console commands:{Environment.NewLine}" +
                        $"- stop(exit) -- Exit MultiSEngine.{Environment.NewLine}" +
                        $"- kick(k) <Player name> (reason) -- Kick out the specified player.{Environment.NewLine}" +
                        $"- broadcase(bc) (target server) <message>  -- Send a message to all players.{Environment.NewLine}" +
                        $"- list(l) -- List all servers in the config.{Environment.NewLine}" +
                        $"- online(ol) -- List all online players.{Environment.NewLine}" +
                        $"- test(t) <Server name>/<all> (-detail) -- Test if the server can connect. If you add the detail parameter at the end, it will show the details of the connection{Environment.NewLine}" +
                        $"- reload(rl) -- Overloading most of the config file content.{Environment.NewLine}" +
                        $"- reloadplugin(rp) -- Reload Plugin.{Environment.NewLine}", false);
                    break;
            }
            Data.Commands.FirstOrDefault(c => c.Name == "mce")?.Execute(client, cmdName, parma);
            return true;
        }
    }
}
