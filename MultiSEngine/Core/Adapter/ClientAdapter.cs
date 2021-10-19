using TrProtocol;
using TrProtocol.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using System;
using System.Linq;
using System.Net.Sockets;

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
            if (Client.State != ClientData.ClientState.Disconnect)
                Client.Disconnect();
        }
        public override bool GetPacket(ref Packet packet)
        {
            switch (packet)
            {
                case ClientHello hello: //使用fakeworld时不会使用这个
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
                case SyncPlayer playerInfo:
                    Client.Player.UpdateData(playerInfo);
                    return true;
                case SyncEquipment:
                case PlayerHealth:
                case PlayerMana:
                case PlayerBuffs:
                case PlayerControls:
                    Client.Player.UpdateData(packet);
                    return !Client.Syncing;
                case ClientUUID uuid:
                    Client.Player.UUID = uuid.UUID;
                    return true;
                case SyncNPCName npcName:
                    Client.SendDataToGameServer(npcName, true);
                    return false; //特殊包
                case TrProtocol.Packets.Modules.NetTextModuleC2S modules:
                    if (Hooks.OnChat(Client, modules, out _))
                        return false;
                    return false;
                default:
                    return true;
            }
        }
        public override void SendPacket(Packet packet)
        {
            //bool shouldSerializeLikeClient = packet.GetType().GetProperties().Any(p => p.GetCustomAttributes(true)?.Any(a => a.GetType() == typeof(S2COnlyAttribute)) ?? false);
            if (!ShouldStop)
                Client.SendDataToGameServer(packet);
        }
    }
}

