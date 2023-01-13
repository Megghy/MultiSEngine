using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{

    public class ServerAdapter : BaseAdapter
    {
        public ServerAdapter(ClientData client, ServerInfo server) : base(client)
        {
            TargetServer = server;
        }
        public ServerInfo TargetServer { get; init; }
        internal Net.NetClient _serverConnection { get; set; }
        public override bool ListenningClient => false;
        protected override void OnRecieveLoopError(Exception ex)
        {
            if (ex is SocketException && !ShouldStop)
            {
                Stop(true);
                Logs.Warn($"Cannot continue to maintain connection between {Client.Name} and server {Client.Server?.Name}{Environment.NewLine}{ex}");
                Client.SendErrorMessage(Localization.Instance["Prompt_UnknownError"]);
                Client.Back();
            }
            else
            {
                base.OnRecieveLoopError(ex);
            }
        }
        public async Task Connect(CancellationToken cancel = default)
        {
            if (_serverConnection?.IsConnecting == true)
                return;
            cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
            await Task.Run(() =>
            {
                if(Utils.TryParseAddress(TargetServer.IP, out var ip))
                {
                    _serverConnection = new(ip, TargetServer.Port, this)
                    {
                        OptionNoDelay = true
                    };
                    _serverConnection.ConnectAsync();
                }
                else
                {
                    throw new Exception($"Invalid server address: {TargetServer.IP}");
                }
            }, cancel);
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

        public override bool GetData(ref Span<byte> buf)
        {
            var msgType = (MessageID)buf[2];
            if (msgType is MessageID.Kick
                or MessageID.LoadPlayer
                or MessageID.WorldData
                or MessageID.SpawnPlayer
                or MessageID.RequestPassword
                or MessageID.FinishedConnectingToServer
                or MessageID.NetModules
                or MessageID.Unused15
                )
            {
                using var reader = new BinaryReader(new MemoryStream(buf.ToArray()));
                var packet = Net.DefaultServerSerializer.Deserialize(reader);
                switch (packet)
                {
                    #region 原生数据包
                    case Kick kick:
                        Client.State = ClientData.ClientState.Disconnect;
                        Stop(true);
                        var reason = kick.Reason.GetText();
                        Logs.Info($"Player {Client.Player.Name} is removed from server {Client.Server.Name}, for the following reason:{reason}");
                        Client.SendErrorMessage(string.Format(Localization.Instance["Prompt_Disconnect", Client.Server.Name, kick.Reason.GetText()]));
                        Client.Back();
                        return true;
                    case LoadPlayer slot:
                        if (Client.Player.Index != slot.PlayerSlot)
                            Logs.Text($"Update the index of [{Client.Name}]: {Client.Player.Index} => {slot.PlayerSlot}.");
                        Client.Player.Index = slot.PlayerSlot;
                        return false;
                    case WorldData worldData:
                        Client.Player.UpdateData(worldData, false);
                        return false;
                    case SpawnPlayer spawn:
                        Client.Player.SpawnX = spawn.Position.X;
                        Client.Player.SpawnY = spawn.Position.Y;
                        return false;
                    case RequestPassword:
                        if (Client.State == ClientData.ClientState.InGame)
                            return false;
                        Client.State = ClientData.ClientState.RequestPassword;
                        Client.SendErrorMessage(string.Format(Localization.Instance["Prompt_NeedPassword", Client.Server.Name, Localization.Instance["Help_Password"]]));
                        return true;
                    case FinishedConnectingToServer:
                        if (Hooks.OnPostSwitch(Client, Client.Server, out _))
                            return false;
                        Client.State = ClientData.ClientState.InGame;
                        Client.SendSuccessMessage(Localization.Instance["Prompt_ConnectSuccess", Client.Server.Name]);
                        Logs.Success($"[{Client.Name}] successfully joined the server: {Client.Server.Name}");
                        return false;
                    case TrProtocol.Packets.Modules.NetTextModuleS2C modules:
                        Client.SendDataToClient(modules, false);
                        return true;
                    #endregion
                    #region 自定义数据包
                    case CustomPacketStuff.CustomDataPacket custom:
                        custom?.Data.RecievedData(Client);
                        return true;
                        #endregion
                }
            }
            return false;
        }

        public override void SendData(ref Span<byte> buf)
        {
            if (!ShouldStop)
                Client.SendDataToClient(ref buf);
        }
    }
}
