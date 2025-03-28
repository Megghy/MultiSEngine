using MultiSEngine.DataStruct;
using static MultiSEngine.Core.Command;

namespace MultiSEngine.Modules
{
    public static class Data
    {
        public static List<ClientData> Clients { get; } = [];
        public static List<CmdBase> Commands { get; } = [];
        internal static byte[] StaticSpawnSquareData { get; set; }
        internal static byte[] StaticDeactiveAllPlayer { get; set; }
        private static string _motd = string.Empty;
        public static string Motd => _motd
            .Replace("{online}", Clients.Count.ToString())
            .Replace("{name}", Config.Instance.ServerName)
            .Replace("{players}", string.Join(", ", Clients.Select(c => c.Name)))
            .Replace("{servers}", string.Join(", ", Config.Instance.Servers.Where(s => s.Visible).Select(s => s.Name)));
        public static string MotdPath => Path.Combine(Environment.CurrentDirectory, "MOTD.txt");
        public static string Convert(int version)
        {
            return version switch
            {
                269 => "v1.4.4",
                270 => "v1.4.4.1",
                271 => "v1.4.4.2",
                272 => "v1.4.4.3",
                273 => "v1.4.4.4",
                274 => "v1.4.4.5",
                275 => "v1.4.4.6",
                276 => "v1.4.4.7",
                277 => "v1.4.4.8",
                278 => "v1.4.4.8.1",
                279 => "v1.4.4.9",
                _ => "Unknown",
            };
        }
        public static readonly int[] Versions = { 230, 233, 234, 235, 236, 237, 238, 242, 243, 244, 245, 246, 247, 248 };
        [AutoInit(order: 0)]
        public static void Init()
        {
            StaticSpawnSquareData = Utils.GetTileSection(4150, 1150, 100, 100).ToArray();
            var deactivePlayers = new List<byte>();
            var playerActive = new PlayerActive(1, false);
            for (int i = 1; i < 255; i++)
            {
                playerActive.PlayerSlot = (byte)i;
                deactivePlayers.AddRange(playerActive.AsBytes());
            }  //隐藏其他所有玩家
            StaticDeactiveAllPlayer = [.. deactivePlayers];
            if (!File.Exists(MotdPath))
                File.WriteAllText(MotdPath, Properties.Resources.DefaultMotd);
            _motd = File.ReadAllText(MotdPath);
        }
    }
}
