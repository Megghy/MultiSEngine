﻿using System;
using System.Net.Sockets;
using Delphinus;
using Delphinus.Packets;
using MultiSEngine.Modules;
using MultiSEngine.Modules.CustomDataPacket;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    internal class VisualPlayerAdapter : ServerAdapter, IStatusChangeable
    {
        public VisualPlayerAdapter(ClientData client, Socket connection) : base(client, connection)
        {
        }
        internal MSEPlayer Player => Client.Player;
        internal bool TestConnecting = false;
        internal Action<VisualPlayerAdapter, ClientData> Callback;
        public bool RunningAsNormal { get; set; } = false;
        public void ChangeProcessState(bool asNormal)
        {
            RunningAsNormal = true;
        }
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
            if (TestConnecting)
                return;
            base.Start();
            Callback = successCallback;
            TestConnecting = true;
            InternalSendPacket(new ClientHelloPacket()
            {
                Version = $"Terraria{(server.VersionNum is { } and > 0 and < 65535 ? server.VersionNum : Client.Player.VersionNum)}"
            });  //发起连接请求   
            InternalSendPacket(new SyncIPPacket()
            {
                IP = Client.IP
            });  //尝试同步玩家IP
        }
        public override bool GetPacket(ref Packet packet)
        {
#if DEBUG
            Console.WriteLine($"[Recieve from SERVER] {packet}");
#endif
            if (RunningAsNormal)
                return base.GetPacket(ref packet);
            switch (packet)
            {
                case KickPacket kick:
                    Client.SendErrorMessage(Localization.Get("Prompt_Disconnect", new[] { Client.Server.Name, kick.Reason.GetText() }));
                    Stop(true);
                    break;
                case LoadPlayerPacket slot:
                    base.GetPacket(ref packet);
                    Client.AddBuff(149, 120);
                    InternalSendPacket(Player.OriginData.Info);
                    InternalSendPacket(new ClientUUIDPacket() { UUID = Player.UUID });
                    InternalSendPacket(new RequestWorldInfoPacket() { });//请求世界信息
                    break;
                case SyncPlayerPacket playerInfo:
                    Player.UpdateData(playerInfo);
                    return true;
                case WorldDataPacket worldData:
                    Player.UpdateData(worldData);
                    if (Callback != null)
                    {
                        Client.TP(Client.SpawnX, Client.SpawnY - 3);
                        TestConnecting = false;
                        Callback.Invoke(this, Client);
                        Callback = null;
                    }
                    InternalSendPacket(new RequestTileDataPacket() { PosX = Client.SpawnX, PosY = Client.SpawnY });//请求物块数据
                    InternalSendPacket(new SpawnPlayerPacket() { PosX = (short)Client.SpawnX, PosY = (short)Client.SpawnY });//请求物块数据
                    break;
                case SyncEquipmentPacket invItem:
                    Player.UpdateData(invItem);
                    break;
                case RequestPasswordPacket:
                    Console.WriteLine($"need pass");
                    Stop(true);
                    return false;
                case StatusTextPacket:
                    return RunningAsNormal;
                case StartPlayingPacket:
                    ChangeProcessState(true); //转换处理模式为普通
                    break;
                default:
                    return base.GetPacket(ref packet);
            }
            return !TestConnecting;
        }
    }
}
