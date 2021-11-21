using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using System;
using System.Linq;
using System.Net.Sockets;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.Packets;

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
        public void BackToThere()
        {
            Logs.Info($"[{Client.Name}] now in FakeWorld");
            Client.State = ClientData.ClientState.ReadyToSwitch;
            Client.Player.ServerData = new();
            Client.Server = null;
            Client.SAdapter?.Stop(true);
            Client.Sync();
            Client.TP(4200, 1200);
            Client.SendDataToClient(new TileSection()
            {
                Data = new()
                {
                    ChestCount = 0,
                    Chests = Array.Empty<ChestData>(),
                    SignCount = 0,
                    Signs = Array.Empty<SignData>(),
                    TileEntities = Array.Empty<TileEntity>(),
                    TileEntityCount = 0,
                    Height = 180,
                    Width = 180,
                    StartX = 4200 - 90,
                    StartY = 1200 - 90,
                    Tiles = new ComplexTileData[1]
                    {
                        new ComplexTileData()
                        {
                            TileType = 541,
                            Count = 180 * 180
                        }
                    }
                }
            });
            var playerActive = new PlayerActive()
            {
                PlayerSlot = 1,
                Active = false
            };
            for (int i = 1; i < 255; i++)
            {
                playerActive.PlayerSlot = (byte)i;
                Client.SendDataToClient(playerActive);
            }  //隐藏其他所有玩家
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
                case ClientHello hello:
                    if (!Hooks.OnPlayerJoin(Client, Client.IP, Client.Port, hello.Version, out var joinEvent))
                    {
                        Client.ReadVersion(joinEvent.Version);
                        if (Client.Player.VersionNum != Data.TRVersion)
                            Client.Disconnect(Localization.Instance["Prompt_NoAvailableServer", joinEvent.Version]);
                        else
                            InternalSendPacket(new LoadPlayer() { PlayerSlot = 0, ServerWantsToRunCheckBytesInClientLoopThread = true });
                    }
                    return false;
                case RequestWorldInfo:
                    var bb = new BitsByte();
                    bb[6] = true;
                    //Client.Player.OriginData.WorldData = new() { EventInfo1 = bb, SpawnX = 4200, SpawnY = 1200 };
                    Client.Player.OriginData.WorldData = new WorldData()
                    {
                        EventInfo1 = bb,
                        SpawnX = Width / 2,
                        SpawnY = Height / 2,
                        MaxTileX = Width,
                        MaxTileY = Height,
                        GameMode = 0,
                        WorldName = Config.Instance.ServerName,
                        WorldUniqueID = Guid.NewGuid()
                    };
                    Client.SendDataToClient(Client.Player.OriginData.WorldData);
                    return false;
                case RequestTileData:
                    Client.SendDataToClient(Data.StaticSpawnSquareData);
                    Client.SendDataToClient(new StartPlaying());
                    return false;
                case SpawnPlayer spawn:
                    if (spawn.Context == PlayerSpawnContext.SpawningIntoWorld)
                    {
                        IsEnterWorld = true;
                        Client.Player.SpawnX = spawn.Position.X;
                        Client.Player.SpawnY = spawn.Position.Y;
                        Client.SendDataToClient(new FinishedConnectingToServer());
                        Client.SendMessage(Data.Motd, false);
                        Data.Clients.Where(c => c.Server is null && c != Client).ForEach(c => c.SendMessage($"{Client.Name} has join."));
                        if (Config.Instance.SwitchToDefaultServerOnJoin)
                        {
                            if (Config.Instance.DefaultServerInternal is { })
                            {
                                Client.SendInfoMessage(Localization.Instance["Command_Switch", new[] { Config.Instance.DefaultServerInternal.Name }]);
                                Client.Join(Config.Instance.DefaultServerInternal);
                            }
                            else
                                Client.SendInfoMessage(Localization.Instance["Prompt_DefaultServerNotFound", new[] { Config.Instance.DefaultServer }]);
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
