using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using System;
using System.Net.Sockets;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{

    public class ServerAdapter : BaseAdapter
    {
        public ServerAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        public override bool ListenningClient => false;
        public override void OnRecieveLoopError(Exception ex)
        {
            if (!ShouldStop)
            {
                Stop(true);
                Logs.Warn($"Cannot continue to maintain connection between {Client.Name} and server {Client.Server?.Name}{Environment.NewLine}{ex}");
                Client.SendErrorMessage(Localization.Instance["Prompt_UnknownError"]);
                Client.Back();
            }
        }
        public override bool GetPacket(Packet packet)
        {
            switch (packet)
            {
                #region 原生数据包
                case Kick kick:
                    Client.State = ClientData.ClientState.Disconnect;
                    Stop(true);
                    Client.TimeOutTimer.Stop();
                    var reason = kick.Reason.GetText();
                    Logs.Info($"Player {Client.Player.Name} is removed from server {Client.Server.Name}, for the following reason:{reason}");
                    Client.SendErrorMessage(string.Format(Localization.Instance["Prompt_Disconnect", Client.Server.Name, kick.Reason.GetText()]));
                    Client.Back();
                    return false;
                case LoadPlayer slot:
                    if (Client.Player.Index != slot.PlayerSlot)
                        Logs.Text($"Update the index of [{Client.Name}]: {Client.Player.Index} => {slot.PlayerSlot}.");
                    Client.Player.Index = slot.PlayerSlot;
                    return true;
                case WorldData worldData:
                    Client.Player.UpdateData(worldData, false);
                    return true;
                case SpawnPlayer spawn:
                    Client.Player.SpawnX = spawn.Position.X;
                    Client.Player.SpawnY = spawn.Position.Y;
                    return true;
                case RequestPassword:
                    if (Client.State == ClientData.ClientState.InGame)
                        return false;
                    Client.State = ClientData.ClientState.RequestPassword;
                    Client.SendErrorMessage(string.Format(Localization.Instance["Prompt_NeedPassword", Client.Server.Name, Localization.Instance["Help_Password"]]));
                    return false;
                case FinishedConnectingToServer:
                    if (Hooks.OnPostSwitch(Client, Client.Server, out _))
                        return true;
                    Client.State = ClientData.ClientState.InGame;
                    Client.SendSuccessMessage(Localization.Instance["Prompt_ConnectSuccess", Client.Server.Name]);
                    Logs.Success($"[{Client.Name}] successfully joined the server: {Client.Server.Name}");
                    return true;
                case TrProtocol.Packets.Modules.NetTextModuleS2C modules:
                    Client.SendDataToClient(modules, false);
                    return false;
                #endregion
                #region 自定义数据包
                case CustomPacketStuff.CustomDataPacket custom:
                    if (custom is not null)
                        custom.Data.RecievedData(Client);
                    return false;
                #endregion
                default:
                    return true;
            }
        }
        public override void SendPacket(Packet packet)
        {
            if (!ShouldStop)
                Client.SendDataToClient(packet, false);
        }
        public void ResetAlmostEverything()
        {
            //Logs.Text($"Resetting client data of [{Client.Name}]");
            //暂时没有要写的
            var emptyNPC = new SyncNPC()
            {
                HP = 0,
                NPCType = 0,
                Extra = new byte[16]
            };
            for (int i = 0; i < 200; i++)
            {
                emptyNPC.NPCSlot = (short)i;
                Client.SendDataToClient(emptyNPC);
            }
        }
    }
}
