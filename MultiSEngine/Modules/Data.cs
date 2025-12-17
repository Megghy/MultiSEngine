using System.Buffers;
using MultiSEngine.DataStruct;
using static MultiSEngine.Core.Command;

namespace MultiSEngine.Modules
{
    public static class Data
    {
        public static List<ClientData> Clients { get; } = [];
        public static List<CmdBase> Commands { get; } = [];
        internal static Utils.PacketMemoryRental? StaticSpawnSquareData { get; private set; }
        internal static Utils.PacketMemoryRental? StaticDeactiveAllPlayer { get; private set; }
        internal static ReadOnlyMemory<byte> SpawnSquarePacket => StaticSpawnSquareData?.Memory ?? ReadOnlyMemory<byte>.Empty;
        internal static ReadOnlyMemory<byte> DeactivateAllPlayerPacket => StaticDeactiveAllPlayer?.Memory ?? ReadOnlyMemory<byte>.Empty;
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
            StaticSpawnSquareData?.Dispose();
            StaticDeactiveAllPlayer?.Dispose();

            StaticSpawnSquareData = Utils.GetTileSection(4150, 1150, 100, 100);
            var playerActive = new PlayerActive
            {
                PlayerSlot = 1,
                Active = false
            };
            using var firstRental = playerActive.AsPacketRental();
            var packetLength = firstRental.Memory.Length;
            var totalPlayers = 254;
            var owner = MemoryPool<byte>.Shared.Rent(packetLength * totalPlayers);
            var destination = owner.Memory.Span;
            firstRental.Memory.Span.CopyTo(destination);
            var offset = packetLength;
            for (int i = 2; i <= totalPlayers + 1; i++)
            {
                playerActive.PlayerSlot = (byte)i;
                using var rental = playerActive.AsPacketRental();
                rental.Memory.Span.CopyTo(destination[offset..]);
                offset += rental.Memory.Length;
            }  //隐藏其他所有玩家
            StaticDeactiveAllPlayer = new Utils.PacketMemoryRental(owner, offset);
            if (!File.Exists(MotdPath))
                File.WriteAllText(MotdPath, Properties.Resources.DefaultMotd);
            _motd = File.ReadAllText(MotdPath);
        }
    }
}
