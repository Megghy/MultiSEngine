using System;
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
            FreezeTimer = new()
            {
                Interval = 1000,
                AutoReset = true
            };
            FreezeTimer.Elapsed += (_, _) => Client.AddBuff(149, 100);
        }
        private Timer FreezeTimer;
        public const int Width = 8400;
        public const int Height = 2400;
        public bool RunningAsNormal { get; set; } = false;
        public void ChangeProcessState(bool asNormal)
        {
            RunningAsNormal = true;
            FreezeTimer.Stop();
        }
        public override AdapterBase Start()
        {
            FreezeTimer.Start();
            Client.AddBuff(149, 100);
            return base.Start();
        }
        public override void Stop(bool disposeConnection = false)
        {
            FreezeTimer.Stop();
            base.Stop(disposeConnection);
        }
        public override bool GetPacket(Packet packet)
        {
#if DEBUG
            Console.WriteLine($"[Recieve from CLIENT] {packet}");
#endif
            if (RunningAsNormal)
                return base.GetPacket(packet);
            switch (packet)
            {
                case ClientHelloPacket hello:
                    Client.ReadVersion(hello);
                    InternalSendPacket(new LoadPlayerPacket() { PlayerSlot = 0 });
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
                        Client.Player.SpawnX = spawn.PosX;
                        Client.Player.SpawnY = spawn.PosY;
                        Client.SendDataToClient(new FinishedConnectingToServerPacket());
                        Client.SendDataToClient(new SpawnPlayerPacket()
                        {
                            PosX = (short)Client.SpawnX,
                            PosY = (short)Client.SpawnY,
                            Context = Terraria.PlayerSpawnContext.RecallFromItem,
                            PlayerSlot = 0,
                            Timer = 0
                        });
                        Logs.Text($"Player {Client.Name} is temporarily transported in FakeWorld");
                    }
                    return false;
                default:
                    return base.GetPacket(packet);
            }
        }
        public override void SendOriginData(byte[] buffer, int start = 0, int? length = null)
        {
            base.SendOriginData(buffer, start, length);
        }
    }
}
