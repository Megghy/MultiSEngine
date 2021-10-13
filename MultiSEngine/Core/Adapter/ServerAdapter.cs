using System;
using System.Net.Sockets;
using Delphinus;
using Delphinus.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using Terraria;
using Terraria.Localization;

namespace MultiSEngine.Core.Adapter
{

    public class ServerAdapter : AdapterBase
    {
        public ServerAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        public override void OnRecieveError(Exception ex)
        {
            base.OnRecieveError(ex);
            Stop(true);
            Client.Back();
        }
        public override bool GetData(Packet packet)
        {
            if (Program.DEBUG)
                Console.WriteLine($"[Recieve from SERVER] {packet}");
            switch (packet)
            {
                case KickPacket kick:
                    Client.State = ClientData.ClientState.Disconnect;
                    Client.TimeOutTimer.Stop();
                    Stop(true);
                    var reason = kick.Reason.GetText();
                    Logs.Info($"Player {Client.Player.Name} is removed from server {Client.Server.Name}, for the following reason:{reason}");
                    //Client.SendErrorMessage(string.Format(Localization.Get("Prompt_Disconnect"), Client.Server.Name, kick.Reason));
                    Client.SendErrorMessage($"Kicked from {Client.Server.Name}: {reason}{Environment.NewLine}Returning to the previous level server.");
                    Client.Back();
                    return false;
                case LoadPlayerPacket slot:
                    Client.Player.Index = slot.PlayerSlot;
                    Client.AddBuff(149, 180);
                    return true;
                case WorldDataPacket worldData:
                    worldData.WorldName = string.IsNullOrEmpty(Config.Instance.ServerName) ? worldData.WorldName : Config.Instance.ServerName; //设置了服务器名称的话则替换
                    Client.Player.UpdateData(worldData);
                    if (Client.State == ClientData.ClientState.Switching)
                    {
                        Client.State = ClientData.ClientState.FinishSendInventory;
                        Client.SendDataToGameServer(new RequestTileDataPacket() { PosX = Client.SpawnX, PosY = Client.SpawnY });
                        Client.SendDataToGameServer(new SpawnPlayerPacket()
                        {
                            PlayerSlot = Client.Player.Index,
                            PosX = (short)Client.SpawnX,
                            PosY = (short)Client.SpawnY,
                            Context = Terraria.PlayerSpawnContext.SpawningIntoWorld
                        }); 
                        Client.TP(Client.SpawnX, Client.SpawnY - 3);
                        Client.AddBuff(149, 120);
                        Logs.Text($"Spawn {Client.Name} into world <{Client.SpawnX}, {Client.SpawnY}>.");
                        ResetAlmostEverything();
                    }
                    return true;
                case SpawnPlayerPacket spawn:
                    Client.Player.SpawnX = spawn.PosX;
                    Client.Player.SpawnY = spawn.PosY;
                    return true;
                case RequestPasswordPacket:
                    Client.State = ClientData.ClientState.RequestPassword;
                    Client.SendErrorMessage(string.Format(Localization.Get("Prompt_NeedPassword"), Client.Server.Name, Localization.Get("Help_Password")));
                    return false;
                case FinishedConnectingToServerPacket:
                    Client.State = ClientData.ClientState.InGame;
                    Logs.Success($"Player {Client.Name} successfully joined the server: {Client.Server.Name}");
                    return true;
                default:
                    return true;
            }
        }
        public override void SendData(Packet packet)
        {
            if (Program.DEBUG)
                Console.WriteLine($"[Send to client] {packet}");
            if (!Client.SendDataToClient(packet))
                Client.Dispose();
        }
        public void ReplaceConnection(Socket connection, bool disposeOld = true)
        {
            if (disposeOld)
            {
                NetReader?.Dispose();
                Connection?.Dispose();
            }
            Connection = connection;
            NetReader = new(new NetworkStream(Connection));
        }
        public void ResetAlmostEverything()
        {
            Logs.Text($"Resetting client data of [{Client.Name}]");
            var emptyNPC = new SyncNPCPacket()
            {
                Life = 0,
                ReleaseOwner = new byte[16],
                AIs = new float[2]
            };
            for (int i = 0; i < 200; i++)
            {
                emptyNPC.NPCSlot = (short)i;
                Client.SendDataToClient(emptyNPC);
            }
            var emptyPlayerActive = new PlayerActivePacket()
            {
                Active = false
            };
            for (int i = 0; i < 255; i++)
            {
                if (i == Client.Player.Index)
                    continue;
                emptyPlayerActive.PlayerSlot = (byte)i;
                Client.SendDataToClient(emptyPlayerActive);
            }
            var emptyItem = new SyncItemPacket()
            {
                ItemType = 0,
                Owner = 255,
                Position = new(),
                Velocity = new(),
                Prefix = 0,
                Stack = 0
            };
            for (int i = 0; i < 255; i++)
            {
                emptyItem.ItemSlot = (byte)i;
                Client.SendDataToClient(emptyItem);
            }
        }
    }
}
