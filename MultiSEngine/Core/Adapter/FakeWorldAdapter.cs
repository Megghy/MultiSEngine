using System;
using System.Linq;
using System.Net.Sockets;
using System.Timers;
using Delphinus;
using Delphinus.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    internal class FakeWorldAdapter : ClientAdapter, IStatusChangeable
    {
        public FakeWorldAdapter(Socket connection) : this(null, connection)
        {
        }
        public FakeWorldAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        public const int Width = 8400;
        public const int Height = 2400;
        public bool RunningAsNormal { get; set; } = false;
        public bool IsEnterWorld = false;
        public void ChangeProcessState(bool asNormal)
        {
            if (asNormal)
                IsEnterWorld = false;
            RunningAsNormal = true;
        }
        public override bool GetPacket(Packet packet)
        {
#if DEBUG
            Console.WriteLine($"[Recieve CLIENT] {packet}");
#endif
            if (RunningAsNormal)
                return base.GetPacket(packet);
            switch (packet)
            {
                case ClientHelloPacket hello:
                    Client.ReadVersion(hello);
                    InternalSendPacket(new LoadPlayerPacket() { PlayerSlot = 0, ServerWantsToRunCheckBytesInClientLoopThread = true });
                    return false;
                case RequestWorldInfoPacket:
                    var bb = new Terraria.BitsByte();
                    bb[6] = true;
                    //Client.Player.OriginData.WorldData = new() { EventInfo1 = bb, SpawnX = 4200, SpawnY = 1200 };
                    Client.Player.OriginData.WorldData = new() { EventInfo1 = bb, SpawnX = Width / 2, SpawnY = Height / 2 };
                    Client.SendDataToClient(new WorldDataPacket()
                    {
                        SpawnX = (short)Client.Player.WorldSpawnX,
                        SpawnY = (short)Client.Player.WorldSpawnY,
                        MaxTileX = Width,
                        MaxTileY = Height,
                        GameMode = 0,
                        WorldName = Config.Instance.ServerName,
                        WorldUniqueID = new byte[16]
                    });
                    return false;
                case RequestTileDataPacket:
                    Client.SendDataToClient(Data.StaticSpawnSquareData);
                    Client.SendDataToClient(new StartPlayingPacket());
                    return false;
                case SpawnPlayerPacket spawn:
                    if (spawn.Context == Terraria.PlayerSpawnContext.SpawningIntoWorld)
                    {
                        IsEnterWorld = true;
                        Client.Player.SpawnX = spawn.PosX;
                        Client.Player.SpawnY = spawn.PosY;
                        Client.SendDataToClient(new FinishedConnectingToServerPacket());
                        Client.SendMessage(Data.Motd, false);
                        Data.Clients.Where(c => c.Server is null && c != Client).ForEach(c => c.SendMessage($"{Client.Name} has join."));
                        if (Config.Instance.SwitchToDefaultServerOnJoin)
                        {
                            if (Config.Instance.DefaultServerInternal is { })
                            {
                                Client.SendInfoMessage(string.Format(Localization.Get("Command_Switch"), Config.Instance.DefaultServerInternal));
                                Client.Join(Config.Instance.DefaultServerInternal);
                            }
                            else
                                Client.SendInfoMessage("No default server is set for the current server.");
                        }
                        else
                            Logs.Text($"[{Client.Name}] is temporarily transported in FakeWorld");
                    }
                    return false;
                default:
                    return base.GetPacket(packet);
            }
        }
    }
}
