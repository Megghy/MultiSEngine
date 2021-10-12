using System.Net.Sockets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{

    public class ServerAdapter : AdapterBase
    {
        public ServerAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }

        public override bool GetData(Packet packet)
        {
            switch (packet)
            {
                case Kick kick:
                    Client.State = ClientData.ClientState.Disconnect;
                    Logs.Info($"Player {Client.Player.Name} is removed from server {Client.Server.Name}, for the following reason:{kick.Reason}");
                    Client.SendErrorMessage(string.Format(Localization.Get("Prompt_Disconnect"), Client.Server.Name, kick.Reason));
                    Client.Back();
                    return false;
                case LoadPlayer slot:
                    Client.Player.Index = slot.PlayerSlot;
                    return true;
                case WorldData worldData:
                    Client.Player.WorldSpawnX = worldData.SpawnX;
                    Client.Player.WorldSpawnY = worldData.SpawnY;
                    worldData.WorldName = string.IsNullOrEmpty(Config.Instance.ServerName) ? worldData.WorldName : Config.Instance.ServerName; //设置了服务器名称的话则替换
                    if (Client.State < ClientData.ClientState.InGame)
                    {
                        Client.SendDataToGameServer(new RequestTileData() { Position = new() { X = -1, Y = -1 } });
                        Client.SendDataToGameServer(new SpawnPlayer()
                        {
                            PlayerSlot = Client.Player.Index,
                            Position = Utils.Point(Client.Server.SpawnX, Client.Server.SpawnY),
                            Context = PlayerSpawnContext.SpawningIntoWorld
                        });
                    }
                    return true;
                case SpawnPlayer spawn:
                    if (spawn.Context == PlayerSpawnContext.SpawningIntoWorld)
                    {
                        Client.Player.SpawnX = spawn.Position.X;
                        Client.Player.SpawnY = spawn.Position.Y;
                        if (Client.Server.SpawnX != -1 && Client.Server.SpawnY != -1)
                            spawn.Position = new() { X = (short)Client.Server.SpawnX, Y = (short)Client.Server.SpawnY }; //如果设置了指定出生位置则修改
                    }
                    return true;
                case RequestPassword requestPassword:
                    Client.State = ClientData.ClientState.RequestPassword;
                    Client.SendErrorMessage(string.Format(Localization.Get("Prompt_NeedPassword"), Client.Server.Name, Localization.Get("Help_Password")));
                    return false;
                case FinishedConnectingToServer:
                    if (Client.Server.SpawnX == -1 || Client.Server.SpawnY == -1)
                        Client.SendDataToClient(new Teleport()
                        {
                            PlayerSlot = Client.Player.Index,
                            Position = new(Client.Player.WorldSpawnX, Client.Player.WorldSpawnY),
                            Style = 1
                        });
                    else
                        Client.SendDataToClient(new Teleport()
                        {
                            PlayerSlot = Client.Player.Index,
                            Position = new(Client.Server.SpawnX, Client.Server.SpawnY),
                            Style = 1
                        });
                    Client.State = ClientData.ClientState.InGame;
                    Logs.Success($"Player {Client.Name} successfully joined the server: {Client.Server.Name}");
                    return true;
                default:
                    return true;
            }
        }

        public override void SendData(Packet packet)
        {
            if (!Client.SendDataToClient(packet))
                Client.Back();
        }
    }
}
