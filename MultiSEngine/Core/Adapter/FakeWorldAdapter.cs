using System;
using System.Linq;
using System.Net.Sockets;
using System.Timers;
using TrProtocol;
using TrProtocol.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using TrProtocol.Models;

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
        public override bool GetPacket(ref Packet packet)
        {
#if DEBUG
            Console.WriteLine($"[Recieve CLIENT] {packet}");
#endif
            if (RunningAsNormal)
                return base.GetPacket(ref packet);
            switch (packet)
            {
                case ClientHello hello:
                    if(!Hooks.OnPlayerJoin(Client, Client.IP, Client.Port, hello.Version, out var joinEvent))
                    {
                        Client.ReadVersion(joinEvent.Version);
                        InternalSendPacket(new LoadPlayer() { PlayerSlot = 0, ServerWantsToRunCheckBytesInClientLoopThread = true });
                    }
                    return false;
                case RequestWorldInfo:
                    var bb = new BitsByte();
                    bb[6] = true;
                    //Client.Player.OriginData.WorldData = new() { EventInfo1 = bb, SpawnX = 4200, SpawnY = 1200 };
                    Client.Player.OriginData.WorldData = new() { EventInfo1 = bb, SpawnX = Width / 2, SpawnY = Height / 2 };
                    Client.SendDataToClient(new WorldData()
                    {
                        SpawnX = (short)Client.Player.WorldSpawnX,
                        SpawnY = (short)Client.Player.WorldSpawnY,
                        MaxTileX = Width,
                        MaxTileY = Height,
                        GameMode = 0,
                        WorldName = Config.Instance.ServerName,
                        WorldUniqueID = Guid.NewGuid()
                    });
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
                    return base.GetPacket(ref packet);
            }
        }
    }
}
