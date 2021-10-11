using System;
using System.Linq;
using System.Net.Sockets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    internal class ClientAdapter : AdapterBase
    {
        public ClientAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }

        public override bool GetData(Packet packet)
        {
            try
            {
                switch (packet)
                {
                    case ClientHello connect:
                        if (Client.State is ClientData.ClientState.NewConnection) //首次连接时默认进入主服务器
                        {
                            if (Config.Instance.MainServer is { })
                            {
                                Client.Player.VersionNum = connect.Version.StartsWith("Terraria") && int.TryParse(connect.Version[8..], out var v)
                                ? v
                                : Config.Instance.MainServer.VersionNum;
                                Logs.Info($"Version num of player {Client.Name} is {Client.Player.VersionNum}.");
                                Client.Join(Config.Instance.MainServer);
                            }
                            else
                                Client.Disconnect("No default server is set for the current server.");
                        }
                        return false;
                    case SyncPlayer playerInfo:
                        Client.Player.Name = playerInfo.Name;
                        return true;
                    case TrProtocol.Packets.Modules.NetTextModuleC2S modules:
                        return Command.HandleCommand(Client, modules.Text, out var c) && c;
                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Deserilize client packet error: {ex}");
                return false;
            }
        }

        public override void SendData(Packet packet)
        {
            Client.SendDataToGameServer(packet);
        }
    }
}
