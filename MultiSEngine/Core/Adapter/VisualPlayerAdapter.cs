using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Delphinus;
using Delphinus.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    internal class VisualPlayerAdapter : ServerAdapter
    {
        public VisualPlayerAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        internal MSEPlayer Player => Client.Player;
        public bool RunningAsNormal = false;
        internal bool Connecting = false;
        internal Action<VisualPlayerAdapter, ClientData> Callback;
        /// <summary>
        /// 调用 tryconnect
        /// </summary>
        /// <returns></returns>
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
            if (Connecting)
                return;
            Connecting = true;
            InternalSendPacket(new ClientHelloPacket()
            {
                Version = $"Terraria{(server.VersionNum is { } and > 0 and < 65565 ? server.VersionNum : Client.Player.VersionNum)}"
            });  //发起连接请求
            Task.Run(RecieveLoop);
            Callback = successCallback;
        }
        public void SyncPlayer()
        {
            Logs.Text($"Syncing player: {Client.Name}");
            Client.SendDataToClient(new LoadPlayerPacket() { PlayerSlot = Player.Index });
            (Player.SSC ? Player.ServerData : Player.OriginData).Inventory.Where(i => i != null).ForEach(i => Client.SendDataToClient(i));
            Client.SendDataToClient(Player.ServerData.WorldData ?? Player.OriginData.WorldData);
            Client.SendDataToClient(Player.ServerData.Info ?? Player.OriginData.Info);
        }

        public override bool GetData(Packet packet)
        {
            if (RunningAsNormal)
                return base.GetData(packet);
            switch (packet)
            {
                case KickPacket kick:
                    Client.SendErrorMessage(kick.Reason.GetText());
                    Stop(true);
                    break;
                case LoadPlayerPacket slot: 
                    Player.Index = slot.PlayerSlot;
                    InternalSendPacket(Player.OriginData.Info);
                    InternalSendPacket(new ClientUUIDPacket() { UUID = Player.UUID });
                    InternalSendPacket(new RequestWorldInfoPacket() { });//请求世界信息
                    break;
                case SyncPlayerPacket playerInfo:
                    Player.UpdateData(playerInfo);
                    break;
                case WorldDataPacket worldData:
                    Player.UpdateData(worldData);
                    InternalSendPacket(new RequestTileDataPacket() { PosX = Client.SpawnX, PosY = Client.SpawnY });//请求物块数据
                    break;
                case TileSectionPacket:
                    if(Callback != null)
                    {
                        Client.TP(Client.SpawnX, Client.SpawnY - 3);
                        Connecting = false;
                        Callback.Invoke(this, Client); 
                        Callback = null;
                    }
                    return true;
                case SyncEquipmentPacket invItem:
                    Player.UpdateData(invItem);
                    break;
                case RequestPasswordPacket:
                    Console.WriteLine($"need pass");
                    Stop(true);
                    break;
                case StartPlayingPacket:
                    InternalSendPacket(new SpawnPlayerPacket()
                    {
                        PlayerSlot = Client.Player.Index,
                        PosX = (short)Client.SpawnX,
                        PosY = (short)Client.SpawnY,
                        Context = Terraria.PlayerSpawnContext.SpawningIntoWorld
                    });
                    RunningAsNormal = true; //转换处理模式为普通
                    break;
            }
            return !Connecting;
        }
        public override void SendData(Packet packet)
        {
            if (!Connecting)
                base.SendData(packet);
        }
    }
}
