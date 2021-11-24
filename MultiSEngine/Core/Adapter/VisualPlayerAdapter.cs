using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;
using MultiSEngine.Modules;
using System;
using System.Net.Sockets;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    internal class VisualPlayerAdapter : ServerAdapter, IStatusChangeable
    {
        public VisualPlayerAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        internal MSEPlayer Player => Client.Player;
        internal bool TestConnecting = false;
        internal ServerInfo TempServer;
        internal Action<ClientData> Callback;
        public bool RunningAsNormal { get; set; } = false;
        public void ChangeProcessState(bool asNormal)
        {
            RunningAsNormal = true;
        }
        [Obsolete("调用 TryConnect", true)]
        public override BaseAdapter Start()
        {
            //不能直接开始
            return this;
        }
        /// <summary>
        /// 如果成功连接的话则调用所给的函数
        /// </summary>
        /// <param name="successCallback"></param>
        public void TryConnect(ServerInfo server, Action<ClientData> successCallback)
        {
            if (TestConnecting)
                return;
            base.Start();
            TempServer = server;
            Callback = successCallback;
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
                    Client.SendErrorMessage(Localization.Instance["Prompt_Disconnect", (Client.Server ?? TempServer)?.Name, kick.Reason.GetText()]);
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
                    if (Callback != null)
                    {
                        TestConnecting = false;
                        Client.Server = TempServer;
                        Callback.Invoke(Client);
                        Callback = null;
                    }
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
                        Client.SendInfoMessage(Localization.Instance["Prompt_WrongPassword", TempServer.Name, Localization.Get("Help_Password")]);
                    }
                    else
                    {
                        Client.State = ClientData.ClientState.RequestPassword;
                        Client.SendInfoMessage(Localization.Instance["Prompt_NeedPassword", TempServer.Name, Localization.Get("Help_Password")]);
                    }
                    Client.TimeOutTimer.Stop();
                    Client.TimeOutTimer.Start();
                    return false;
                case StatusText:
                    return RunningAsNormal;
                case StartPlaying:
                    Client.SendDataToClient(new SpawnPlayer() { PlayerSlot = Client.Index, Context = TrProtocol.Models.PlayerSpawnContext.RecallFromItem, Position = new(Client.SpawnX, (short)(Client.SpawnY - 3)), Timer = 0 });
                    ChangeProcessState(true); //转换处理模式为普通
                    break;
                default:
                    return base.GetPacket(packet);
            }
            return !TestConnecting;
        }
    }
}
