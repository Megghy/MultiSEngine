using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    public class VisualPlayerAdapter : ServerAdapter, IStatusChangeable
    {
        public VisualPlayerAdapter(ClientData client, ServerInfo server) : base(client, server)
        {
        }

        internal PlayerInfo Player => Client.Player;
        internal bool TestConnecting = false;
        public bool RunningAsNormal { get; set; } = false;
        public void ChangeProcessState(bool asNormal)
        {
            RunningAsNormal = true;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="server">目标服务器</param>
        /// <param name="cancel"></param>
        /// <returns></returns>
        public async Task TryConnect(ServerInfo server, CancellationToken cancel = default)
        {
            if (TestConnecting)
                return;
            cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
            await Connect(cancel)
                .ContinueWith(task =>
                {
                    TestConnecting = true;
                    InternalSendPacket(new ClientHello()
                    {
                        Version = $"Terraria{(server.VersionNum is { } and > 0 and < 65535 ? server.VersionNum : Client.Player.VersionNum)}"
                    });  //发起连接请求   
                    while (!RunningAsNormal)
                    {
                        cancel.ThrowIfCancellationRequested();
                        Thread.Sleep(1);
                    }
                }, cancel);
        }
        public override bool GetData(ref Span<byte> buf)
        {
            var msgType = (MessageID)buf[2];
#if DEBUG
            Console.WriteLine($"[Recieve SERVER] {msgType}");
#endif
            if (msgType is MessageID.Kick
                or MessageID.LoadPlayer
                or MessageID.SyncPlayer
                or MessageID.WorldData
                or MessageID.SyncEquipment
                or MessageID.RequestPassword
                or MessageID.StatusText
                or MessageID.StartPlaying
                )
            {
                using var reader = new BinaryReader(new MemoryStream(buf.ToArray()));
                var packet = Net.DefaultServerSerializer.Deserialize(reader);
                if (RunningAsNormal)
                    return base.GetData(ref buf);
                switch (packet)
                {
                    case Kick kick:
                        Stop(true);
                        Client.SendErrorMessage(Localization.Instance["Prompt_Disconnect", (Client.Server ?? TargetServer)?.Name, kick.Reason.GetText()]);
                        Logs.Info($"[{Client.Name}] kicked by [{TargetServer.Name}]: {kick.Reason.GetText()}");
                        Client.State = ClientData.ClientState.Disconnect;
                        return true;
                    case LoadPlayer slot:
                        base.GetData(ref buf);
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
                        return false;
                    case SyncPlayer playerInfo:
                        Player.UpdateData(playerInfo, false);
                        return false;
                    case WorldData worldData:
#if DEBUG
                        Client.SendInfoMessage($"SSC: {worldData.EventInfo1[6]}");
#endif
                        Player.UpdateData(worldData, false);
                        InternalSendPacket(new RequestTileData() { Position = new(Client.SpawnX, Client.SpawnY) });//请求物块数据
                        InternalSendPacket(new SpawnPlayer() { Position = new(Client.SpawnX, Client.SpawnY) });//请求物块数据
                        return false;
                    case SyncEquipment invItem:
                        Player.UpdateData(invItem, false);
                        return false;
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
                        return true;
                    case StatusText:
                        return RunningAsNormal;
                    case StartPlaying:
                        Client.SendDataToClient(new SpawnPlayer() { PlayerSlot = Client.Index, Context = TrProtocol.Models.PlayerSpawnContext.RecallFromItem, Position = new(Client.SpawnX, (short)(Client.SpawnY - 3)), Timer = 0 });
                        ChangeProcessState(true); //转换处理模式为普通
                        return false;
                }
            }
            return false;
        }
    }
}
