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
        public ClientAdapter(ClientData client, Socket connection) : base(client, connection)
        {
            client.CAdapter = this;
        }
        public override PacketSerializer Serializer { get; set; } = new(false);
        public override void OnRecieveLoopError(Exception ex)
        {
            base.OnRecieveLoopError(ex);
            Client.Disconnect();
        }
        public override bool GetPacket(Packet packet)
        {
            switch (packet)
            {
                case ClientHelloPacket hello: //使用fakeworld时不会使用这个
                    if (Client.State is ClientData.ClientState.NewConnection) //首次连接时默认进入主服务器
                    {
                        if (Config.Instance.DefaultServerInternal is { })
                        {
                            Client.ReadVersion(hello);
                            Client.Join(Config.Instance.DefaultServerInternal);
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
                case PlayerControlsPacket:
                    Client.Player.UpdateData(packet);
                    return !Client.Syncing;
                case ClientUUIDPacket uuid:
                    Client.Player.UUID = uuid.UUID;
                    return true;
                case Delphinus.NetModules.NetTextModule modules:
                    if (modules.Command == "Say" && (Command.HandleCommand(Client, modules.Text, out var c) && !c))
                        return false;
                    else if (Client.State == ClientData.ClientState.NewConnection)
                    {
                        Client.SendInfoMessage($"{Localization.Get("Command_NotEntered")}\r\n" +
                            $"{Localization.Get("Help_Tp")}\r\n" +
                            $"{Localization.Get("Help_Back")}\r\n" +
                            $"{Localization.Get("Help_List")}\r\n" +
                            $"{Localization.Get("Help_Command")}"
                        );
                    }
                    else
                    {
                        Client.SendDataToGameServer(modules, true);
                    }
                    return false;
                default:
                    return true;
            }
        }
        public override void SendPacket(Packet packet)
        {
            if (!ShouldStop)
                Client.SendDataToGameServer(Serializer.Serialize(packet));
        }
    }
}

