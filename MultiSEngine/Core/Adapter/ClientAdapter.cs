using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    public class ClientAdapter : AdapterBase
    {
        public ClientAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        public override PacketSerializer Serializer { get; set; } = new(false);
        public override AdapterBase Start()
        {
            Task.Run(RecieveLoop);
            Task.Run(CheckAlive);
            return this;
        }
        public void CheckAlive()
        {
            while (Connection is { Connected: true })
            {
                try
                {
                    Connection.Send(new byte[3]);
                    Task.Delay(500).Wait();
                }
                catch
                {
                    Client.Dispose();
                    return;
                }
            }
            Client.Dispose();
        }
        public override bool GetData(Packet packet)
        {
            switch (packet)
            {
                case ClientHello hello:
                    if (Client.State is ClientData.ClientState.NewConnection) //首次连接时默认进入主服务器
                    {
                        if (Config.Instance.MainServer is { })
                        {
                            Client.ReadVersion(hello);
                            Client.Join(Config.Instance.MainServer);
                        }
                        else
                            Client.Disconnect("No default server is set for the current server.");
                    }
                    return false;
                case SyncPlayer playerInfo:
                    Client.Player.Name = playerInfo.Name;
                    Logs.Info($"Name of {Client.Address} is {Client.Player.Name}");
                    return true;
                case TrProtocol.Packets.Modules.NetTextModuleC2S modules:
                    if (modules.Command == "Say")
                    {
                        Command.HandleCommand(Client, modules.Text, out var c);
                        return c;
                    }
                    return true;
                default:
                    return true;
            }
        }
        public override void SendData(Packet packet)
        {
            Client.SendDataToGameServer(packet);
        }
    }
}
