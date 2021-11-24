using MultiSEngine.DataStruct;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static MultiSEngine.Core.Command;

namespace MultiSEngine.Modules
{
    public class Data
    {
        public static readonly List<ClientData> Clients = new();
        public static readonly List<CmdBase> Commands = new();
        internal static byte[] StaticSpawnSquareData { get; set; }
        private static string _motd = string.Empty;
        public static string Motd => _motd
            .Replace("{online}", Clients.Count.ToString())
            .Replace("{name}", Config.Instance.ServerName)
            .Replace("{players}", string.Join(", ", Clients.Select(c => c.Name)))
            .Replace("{servers}", string.Join(", ", Config.Instance.Servers.Select(s => s.Name)));
        public static string MotdPath => Path.Combine(Environment.CurrentDirectory, "MOTD.txt");
        public static string Convert(int version)
        {
            string protocol = $"Terraria{version}";
            return protocol switch
            {
                "Terraria230" => "v1.4.0.5",
                "Terraria233" => "v1.4.1.1",
                "Terraria234" => "v1.4.1.2",
                "Terraria235" => "v1.4.2",
                "Terraria236" => "v1.4.2.1",
                "Terraria237" => "v1.4.2.2",
                "Terraria238" => "v1.4.2.3",
                "Terraria242" => "v1.4.3",
                "Terraria243" => "v1.4.3.1",
                _ => "Unknown",
            };
        }
        [AutoInit]
        public static void Init()
        {
            StaticSpawnSquareData = Utils.GetTileSection(4150, 1150, 100, 100);
            if (!File.Exists(MotdPath))
                File.WriteAllText(MotdPath, Properties.Resources.DefaultMotd);
            _motd = File.ReadAllText(MotdPath);
        }
    }
}
