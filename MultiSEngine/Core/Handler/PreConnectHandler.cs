using System;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;
using MultiSEngine.Modules;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Handler
{
    public class PreConnectHandler : BaseHandler
    {
        public PreConnectHandler(BaseAdapter parent, ServerInfo server) : base(parent)
        {
            TargetServer = server;
        }
        public ServerInfo TargetServer { get; init; }
        public bool IsConnecting { get; private set; } = true;
        public override bool RecieveClientData(MessageID msgType, ref Span<byte> data)
        {
            return true;
        }
        public override bool RecieveServerData(MessageID msgType, ref Span<byte> data)
        {
            switch (msgType)
            {
                case MessageID.Kick:
                    var kick = data.AsPacket<Kick>();
                    Parent.Stop(true);
                    Client.SendErrorMessage(Localization.Instance["Prompt_Disconnect", TargetServer.Name, kick.Reason.GetText()]);
                    Logs.Info($"[{Client.Name}] kicked by [{TargetServer.Name}]: {kick.Reason.GetText()}");
                    Client.State = ClientState.Disconnect;
                    return true;
                case MessageID.LoadPlayer:
                    var slot = data.AsPacket<LoadPlayer>();
                    Client.AddBuff(149, 120);
                    SendToServerDirect(Client.Player.OriginCharacter.Info);
                    SendToServerDirect(new ClientUUID() { UUID = Client.Player.UUID });
                    SendToServerDirect(new RequestWorldInfo() { });//请求世界信息
                    SendToServerDirect(new CustomPacketStuff.CustomDataPacket()
                    {
                        Data = new SyncIP()
                        {
                            PlayerName = Client.Name,
                            IP = Client.IP
                        }
                    });  //尝试同步玩家IP
                    return false;
                case MessageID.WorldData:
                    var worldData = data.AsPacket<WorldData>();
#if DEBUG
                    Client.SendInfoMessage($"SSC: {worldData.EventInfo1[6]}");
#endif
                    //Client.Player.UpdateData(worldData, false);
                    SendToServerDirect(new RequestTileData() { Position = new(Client.SpawnX, Client.SpawnY) });//请求物块数据
                    SendToServerDirect(new SpawnPlayer() { Position = new(Client.SpawnX, Client.SpawnY) });//请求物块数据
                    return false;
                case MessageID.RequestPassword:
                    if (Client.State == ClientState.InGame)
                        return false;
                    if (Client.State == ClientState.RequestPassword)
                    {
                        Client.SendInfoMessage(Localization.Instance["Prompt_WrongPassword", TargetServer.Name, Localization.Get("Help_Password")]);
                    }
                    else
                    {
                        Client.State = ClientState.RequestPassword;
                        Client.SendInfoMessage(Localization.Instance["Prompt_NeedPassword", TargetServer.Name, Localization.Get("Help_Password")]);
                    }
                    return true;
                case MessageID.StatusText:
                    return true;
                case MessageID.StartPlaying:
                    Client.SendDataToClient(new SpawnPlayer() { PlayerSlot = Client.Index, Context = TrProtocol.Models.PlayerSpawnContext.RecallFromItem, Position = new(Client.SpawnX, (short)(Client.SpawnY - 3)), Timer = 0 });
                    return false;
                case MessageID.FinishedConnectingToServer:
                    IsConnecting = false;
                    if (Hooks.OnPostSwitch(Client, TargetServer, out _))
                        return false;
                    Parent.DeregisteHander(this); //转换处理模式为普通
                    Client.State = ClientState.InGame;
                    Client.SendSuccessMessage(Localization.Instance["Prompt_ConnectSuccess", TargetServer.Name]);
                    Client.SendDataToClient(new SpawnPlayer()
                    {
                        PlayerSlot = Client.Index,
                        Context = TrProtocol.Models.PlayerSpawnContext.SpawningIntoWorld,
                        Timer = 0,
                        Position = new(Client.SpawnX, Client.SpawnY),
                        DeathsPVE = 0,
                        DeathsPVP = 0,
                    });
                    Logs.Success($"[{Client.Name}] successfully joined the server: {TargetServer.Name}");
                    return false;
            }
            return false;
        }
    }
}
