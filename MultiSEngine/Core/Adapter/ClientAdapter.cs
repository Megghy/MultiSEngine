using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Delphinus;
using Delphinus.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    public class ClientAdapter : AdapterBase
    {
        public ClientAdapter() : this(null, null)
        {
        }
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
        public override void OnRecieveError(Exception ex)
        {
            base.OnRecieveError(ex);
            Client.Dispose();
        }
        public void CheckAlive()
        {
            while (Connection is { Connected: true } && !ShouldStop)
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
            if (!ShouldStop)
                Client.Dispose();
        }
        public override bool GetData(Packet packet)
        {
            if (Program.DEBUG)
                Console.WriteLine($"[Recieve from CLIENT] {packet}");
            switch (packet)
            {
                case ClientHelloPacket hello:
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
                case SyncPlayerPacket playerInfo:
                    Client.Player.UpdateData(playerInfo);
                    return true;
                case SyncEquipmentPacket:
                case PlayerHealthPacket:
                case PlayerManaPacket:
                case PlayerBuffsPacket:
                    Client.Player.UpdateData(packet);
                    return !Client.Syncing;
                case ClientUUIDPacket uuid:
                    Client.Player.UUID = uuid.UUID;
                    return true;
                case Delphinus.NetModules.NetTextModule modules:
                    if (modules.Command == "Say")
                    {
                        modules.fromClient = true;
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
            if (Program.DEBUG)
                Console.WriteLine($"[Sent to SERVER] {packet}");
            if (!Client.SAdapter?.ShouldStop ?? false)
                Client.SendDataToGameServer(packet);
        }
    }
}
