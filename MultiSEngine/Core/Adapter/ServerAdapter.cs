using System;
using System.Linq;
using System.Net.Sockets;
using Delphinus;
using Delphinus.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core.Adapter
{

    public class ServerAdapter : AdapterBase
    {
        public ServerAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        public override void OnRecieveLoopError(Exception ex)
        {
            if (!ShouldStop)
            {
                Stop(true);
                Logs.Warn($"Cannot continue to maintain connection between {Client.Name} and server {Client.Server.Name}{Environment.NewLine}{ex}");
                Client.Back();
            }
        }
        public override bool GetPacket(ref Packet packet)
        {
            switch (packet)
            {
                case KickPacket kick:
                    Client.State = ClientData.ClientState.Disconnect;
                    Client.TimeOutTimer.Stop();
                    Stop(true);
                    var reason = kick.Reason.GetText();
                    Logs.Info($"Player {Client.Player.Name} is removed from server {Client.Server.Name}, for the following reason:{reason}");
                    Client.SendErrorMessage(string.Format(Localization.Get("Prompt_Disconnect"), Client.Server.Name, kick.Reason));
                    Client.Back();
                    return false;
                case LoadPlayerPacket slot:
                    if (Client.Player.Index != slot.PlayerSlot)
                        Logs.Text($"Update the index of [{Client.Name}]: {Client.Player.Index} => {slot.PlayerSlot}.");
                    Client.Player.Index = slot.PlayerSlot;
                    return true;
                case WorldDataPacket worldData:
                    worldData.WorldName = string.IsNullOrEmpty(Config.Instance.ServerName) ? worldData.WorldName : Config.Instance.ServerName; //设置了服务器名称的话则替换
                    Client.Player.UpdateData(worldData);
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
                    if (Hooks.OnPostSwitch(Client, Client.Server, out _))
                        return true;
                    Client.State = ClientData.ClientState.InGame;
                    Logs.Success($"[{Client.Name}] successfully joined the server: {Client.Server.Name}");
                    return true;
                case Delphinus.NetModules.NetTextModule modules:
                    modules.fromClient = true;
                    Client.SendDataToClient(modules, false);
                    return false;
                default:
                    return true;
            }
        }
        public override void SendPacket(Packet packet)
        {
            if (!ShouldStop)
                Client.SendDataToClient(Serializer.Serialize(packet));
        }
        public void ResetAlmostEverything()
        {
            Logs.Text($"Resetting client data of [{Client.Name}]");
            var playerActive = new PlayerActivePacket()
            {
                Active = false
            };
            Data.Clients.Where(c => c.Server == Client.Server && c != Client)
                .ForEach(c => c.SendDataToClient(new PlayerActivePacket()
                {
                    Active = false,
                    PlayerSlot = c.Player.Index
                }));
        }
    }
}
