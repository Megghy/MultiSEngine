using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static MultiSEngine.Core.Command;

namespace MultiSEngine.Modules
{
    internal class Data
    {
        public const int TRVersion = 242;
        public static readonly List<DataStruct.ClientData> Clients = new();
        public static readonly List<CmdBase> Commands = new();
        internal static byte[] StaticSpawnSquareData { get; set; }
        private static string _motd = string.Empty;
        public static string Motd => _motd
            .Replace("{online}", Clients.Count.ToString())
            .Replace("{name}", Config.Instance.ServerName)
            .Replace("{players}", string.Join(", ", Clients.Select(c => c.Name)))
            .Replace("{servers}", string.Join(", ", Config.Instance.Servers.Select(s => s.Name)));
        public static string MotdPath => Path.Combine(Environment.CurrentDirectory, "MOTD.txt");
        public static void Init()
        {
            StaticSpawnSquareData = Utils.GetTileSection(4150, 1150, 100, 100);
            if (!File.Exists(MotdPath))
                File.WriteAllText(MotdPath, Properties.Resources.DefaultMotd);
            _motd = File.ReadAllText(MotdPath);
        }
    }
}
