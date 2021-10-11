using System;
using System.Net.Sockets;
using System.Threading.Tasks;
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
                    Client.SendDataToClient(new byte[3]);
                    Task.Delay(1000).Wait();
                }
                catch
                {
                    Client.Dispose();
                    return;
                }
            }
        }
        public override bool GetData(Packet packet)
        {
            try
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
                        Client.Player.SpawnX = BitConverter.ToInt16(worldData.Data, 13);
                        Client.Player.SpawnY = BitConverter.ToInt16(worldData.Data, 15);
                        if (Client.State < ClientData.ClientState.InGame)
                        {
                            Client.SendDataToGameServer(new RequestTileData() { Position = new() { X = -1, Y = -1 } });
                            Client.SendDataToGameServer(new SpawnPlayer()
                            {
                                PlayerSlot = Client.Player.Index,
                                Position = Utils.Point(Client.Server.SpawnX, Client.Server.SpawnY),
                                Context = PlayerSpawnContext.SpawningIntoWorld
                            });
                            if (Client.Server.SpawnX == -1 || Client.Server.SpawnY == -1)
                                Client.SendDataToClient(new Teleport()
                                {
                                    PlayerSlot = Client.Player.Index,
                                    Position = new(Client.Player.SpawnX, Client.Player.SpawnY),
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
                        }
                        return true;
                    case RequestPassword requestPassword:
                        Client.State = ClientData.ClientState.RequestPassword;
                        Client.SendErrorMessage(string.Format(Localization.Get("Prompt_NeedPassword"), Client.Server.Name, Localization.Get("Help_Password")));
                        return false;
                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Deserilize game server packet error: {ex}");
                return false;
            }
        }

        public override void SendData(Packet packet)
        {
            Client.SendDataToClient(packet);
        }
    }
}
