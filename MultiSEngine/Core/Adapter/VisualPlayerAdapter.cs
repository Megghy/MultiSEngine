using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;
using MultiSEngine.Modules;
using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    public class VisualPlayerAdapter : ServerAdapter, IStatusChangeable
    {
        public VisualPlayerAdapter(ClientData client, ServerInfo server) : base(client, new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp))
        {
            TargetServer = server;
        }

        internal PlayerInfo Player => Client.Player;
        internal bool TestConnecting = false;
        internal ServerInfo TargetServer;
        internal Action<ClientData> SuccessCallback;
        public bool RunningAsNormal { get; set; } = false;
        public void ChangeProcessState(bool asNormal)
        {
            RunningAsNormal = true;
        }
        [Obsolete("调用 TryConnect", true)]
#pragma warning disable CS0809
        public override BaseAdapter Start()
#pragma warning restore CS0809
        {
            //不能直接开始
            return this;
        }
        /// <summary>
        /// 如果成功连接的话则调用所给的函数
        /// </summary>
        /// <param name="successCallback"></param>
        public async Task TryConnect(ServerInfo server, Action<ClientData> successCallback)
        {
            if (TestConnecting)
                return;
            await Connection.ConnectAsync(server.IP, server.Port);
            base.Start();
            TargetServer = server;
            SuccessCallback = successCallback;
            TestConnecting = true;
            InternalSendPacket(new ClientHello()
            {
                Version = $"Terraria{(server.VersionNum is { } and > 0 and < 65535 ? server.VersionNum : Client.Player.VersionNum)}"
            });  //发起连接请求   
        }
        public override bool GetPacket(Packet packet)
        {
#if DEBUG
            Console.WriteLine($"[Recieve SERVER] {packet}");
#endif
            if (RunningAsNormal)
                return base.GetPacket(packet);
            switch (packet)
            {
                case Kick kick:
                    Stop(true);
                    Client.SendErrorMessage(Localization.Instance["Prompt_Disconnect", (Client.Server ?? TargetServer)?.Name, kick.Reason.GetText()]);
                    Client.State = ClientData.ClientState.Disconnect;
                    break;
                case LoadPlayer slot:
                    base.GetPacket(packet);
                    Client.AddBuff(149, 120);
                    InternalSendPacket(Player.OriginData.Info);
                    InternalSendPacket(new ClientUUID() { UUID = Player.UUID });
                    InternalSendPacket(new RequestWorldInfo() { });//请求世界信息
                    InternalSendPacket(new CustomPacketStuff.CustomDataPacket()
                    {
                        Data = new SyncIP()
                        {
                            PlayerName = Client.Name,
                            IP = Client.IP
                        }
                    });  //尝试同步玩家IP
                    break;
                case SyncPlayer playerInfo:
                    Player.UpdateData(playerInfo, false);
                    return true;
                case WorldData worldData:
#if DEBUG
                    Client.SendInfoMessage($"SSC: {worldData.EventInfo1[6]}");
#endif
                    Player.UpdateData(worldData, false);
                    InternalSendPacket(new RequestTileData() { Position = new(Client.SpawnX, Client.SpawnY) });//请求物块数据
                    InternalSendPacket(new SpawnPlayer() { Position = new(Client.SpawnX, Client.SpawnY) });//请求物块数据
                    break;
                case SyncEquipment invItem:
                    Player.UpdateData(invItem, false);
                    break;
                case RequestPassword:
                    if (Client.State == ClientData.ClientState.InGame)
                        return false;
                    if (Client.State == ClientData.ClientState.RequestPassword)
                    {
                        Client.SendInfoMessage(Localization.Instance["Prompt_WrongPassword", TargetServer.Name, Localization.Get("Help_Password")]);
                    }
                    else
                    {
                        Client.State = ClientData.ClientState.RequestPassword;
                        Client.SendInfoMessage(Localization.Instance["Prompt_NeedPassword", TargetServer.Name, Localization.Get("Help_Password")]);
                    }
                    Client.TimeOutTimer.Stop();
                    Client.TimeOutTimer.Start();
                    return false;
                case StatusText:
                    return RunningAsNormal;
                case StartPlaying:
                    Client.SendDataToClient(new SpawnPlayer() { PlayerSlot = Client.Index, Context = TrProtocol.Models.PlayerSpawnContext.RecallFromItem, Position = new(Client.SpawnX, (short)(Client.SpawnY - 3)), Timer = 0 });
                    ChangeProcessState(true); //转换处理模式为普通

                    if (SuccessCallback != null)
                    {
                        TestConnecting = false;
                        SuccessCallback?.Invoke(Client);
                        SuccessCallback = null;
                        Client.Server = TargetServer;
                    }
                    break;
                default:
                    return base.GetPacket(packet);
            }
            return !TestConnecting;
        }
    }
}
