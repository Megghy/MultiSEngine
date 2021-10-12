using System;
using System.IO;
using System.Net.Sockets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    internal class FakeWorldAdapter : ClientAdapter
    {
        public FakeWorldAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }

        public override bool GetData(Packet packet)
        {
            switch (packet)
            {
                case ClientHello hello:
                    Client.ReadVersion(hello);
                    Client.SendDataToClient(new LoadPlayer() { PlayerSlot = 0 });
                    return false;
                case RequestWorldInfo:
                    Client.Player.WorldSpawnX = 8400 / 2;
                    Client.Player.WorldSpawnY = 2400 / 2;
                    Client.SendDataToClient(new WorldData()
                    {
                        SpawnX = (short)Client.Player.WorldSpawnX,
                        SpawnY = (short)Client.Player.WorldSpawnY,
                        MaxTileX = 8400,
                        MaxTileY = 2400,
                        GameMode = 0,
                        WorldName = Config.Instance.ServerName
                    });
                    return false;
                case RequestTileData:
                    Client.SendDataToClient(Data.StaticSpawnSquareData);
                    Client.SendDataToClient(new StartPlaying());
                    return false;
                case SpawnPlayer spawn:
                    if (spawn.Context == PlayerSpawnContext.SpawningIntoWorld)
                    {
                        Client.Player.SpawnX = spawn.Position.X;
                        Client.Player.SpawnY = spawn.Position.Y;
                        Client.SendDataToClient(new FinishedConnectingToServer());
                        Client.SendDataToClient(new SpawnPlayer()
                        {
                            Position = Utils.Point(Client.Player.WorldSpawnX, Client.Player.WorldSpawnY),
                            Context = PlayerSpawnContext.RecallFromItem,
                            PlayerSlot = 0,
                            Timer = 0
                        });
                        Logs.Text($"Player {Client.Name} is temporarily transported in FakeWorld");
                    }
                    return false;
                case TrProtocol.Packets.Modules.NetTextModuleC2S modules:
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
            //压根没连服务器往哪传
        }
    }
}
