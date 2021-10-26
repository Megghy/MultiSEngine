using System;
using System.Net.Sockets;
using TrProtocol;
using TrProtocol.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.CustomData;
using MultiSEngine.Modules.DataStruct;
using System.Threading.Tasks;
using TrProtocol.Models;

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
        internal Action<VisualPlayerAdapter, ClientData> Callback;
        public bool RunningAsNormal { get; set; } = false;
        public void ChangeProcessState(bool asNormal)
        {
            RunningAsNormal = true;
        }
        [Obsolete("调用 TryConnect", true)]
        public override AdapterBase Start()
        {
            //不能直接开始
            return this;
        }
        /// <summary>
        /// 如果成功连接的话则调用所给的函数
        /// </summary>
        /// <param name="successCallback"></param>
        public void TryConnect(ServerInfo server, Action<VisualPlayerAdapter, ClientData> successCallback)
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
        public override bool GetPacket(ref Packet packet)
        {
#if DEBUG
            Console.WriteLine($"[Recieve SERVER] {packet}");
#endif
            if (RunningAsNormal)
                return base.GetPacket(ref packet);
            switch (packet)
            {
                case Kick kick:
                    Client.SendErrorMessage(Localization.Instance["Prompt_Disconnect", Client.Server.Name, kick.Reason.GetText()]);
                    Stop(true);
                    break;
                case LoadPlayer slot:
                    base.GetPacket(ref packet);
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
                    Player.UpdateData(playerInfo);
                    return true;
                case WorldData worldData:
                    Player.UpdateData(worldData);
                    if (Callback != null)
                    {
                        Client.TP(Client.SpawnX, Client.SpawnY - 3);
                        TestConnecting = false;
                        Client.Server = TempServer;
                        Callback.Invoke(this, Client);
                        Callback = null;
                    }
                    InternalSendPacket(new RequestTileData() { Position = Utils.Point(Client.SpawnX, Client.SpawnY) });//请求物块数据
                    InternalSendPacket(new SpawnPlayer() { Position = Utils.ShortPoint(Client.SpawnX, Client.SpawnY) });//请求物块数据
                    break;
                case SyncEquipment invItem:
                    Player.UpdateData(invItem);
                    break;
                case RequestPassword:
                    Client.State = ClientData.ClientState.RequestPassword;
                    Client.SendInfoMessage(Localization.Instance["Prompt_NeedPassword", Client.Server.Name, Localization.Get("Help_Password")]);
                    return false;
                case StatusText:
                    return RunningAsNormal;
                case StartPlaying:
                    ChangeProcessState(true); //转换处理模式为普通
                    break;
                default:
                    return base.GetPacket(ref packet);
            }
            return !TestConnecting;
        }
    }
}
