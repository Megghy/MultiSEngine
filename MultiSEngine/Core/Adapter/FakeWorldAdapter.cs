using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using Delphinus;
using Delphinus.Packets;

namespace MultiSEngine.Core.Adapter
{
    internal class FakeWorldAdapter : ClientAdapter
    {
        public FakeWorldAdapter(Socket connection) : base(null, connection)
        {
        }
        public FakeWorldAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        public bool RunningAsNormal = false;
        public override bool GetData(Packet packet)
        {
            if(RunningAsNormal)
                return base.GetData(packet);
            switch (packet)
            {
                case ClientHelloPacket hello:
                    Client.ReadVersion(hello);
                    InternalSendPacket(new LoadPlayerPacket() { PlayerSlot = 0 });
                    return false;
                case RequestWorldInfoPacket:
                    var bb = new Terraria.BitsByte();
                    bb[6] = true;
                    Client.Player.OriginData.WorldData = new() { EventInfo1 = bb, SpawnX = 4200, SpawnY = 1200 };
                    Client.SendDataToClient(new WorldDataPacket()
                    {
                        SpawnX = (short)Client.Player.WorldSpawnX,
                        SpawnY = (short)Client.Player.WorldSpawnY,
                        MaxTileX = 8400,
                        MaxTileY = 2400,
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
                case Delphinus.NetModules.NetTextModule modules:
                    if (modules.Command == "Say")
                    {
                        if (!Command.HandleCommand(Client, modules.Text, out var c) && modules.Text.StartsWith("/"))
                        {
                            Client.SendInfoMessage($"unkonwn command");
                        }
                    }
                    return false;
                default:
                    return base.GetData(packet);
            }
        }
        public override void SendData(Packet packet)
        {
            if (RunningAsNormal)
                base.SendData(packet);
        }
        public void ChangeStatusToNormal()
        {
            RunningAsNormal = true;
        }
    }
}
