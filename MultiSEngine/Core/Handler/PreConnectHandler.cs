using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;
using MultiSEngine.Modules;

namespace MultiSEngine.Core.Handler
{
    public class PreConnectHandler(BaseAdapter parent, ServerInfo server) : BaseHandler(parent)
    {
        public ServerInfo TargetServer { get; init; } = server;
        public bool IsConnecting { get; private set; } = true;
        byte index = 0;
        public override bool RecieveClientData(MessageID msgType, Span<byte> data)
        {
            return true;
        }
        public override bool RecieveServerData(MessageID msgType, Span<byte> data)
        {
            switch (msgType)
            {
                case MessageID.Kick:
                    var kick = data.AsPacket<Kick>();
                    Parent.Dispose(true);
                    Client.SendErrorMessage(Localization.Instance["Prompt_Disconnect", TargetServer.Name, kick.Reason.GetText()]);
                    Logs.Info($"[{Client.Name}] kicked by [{TargetServer.Name}]: {kick.Reason.GetText()}");
                    Client.State = ClientState.Disconnect;
                    return true;
                case MessageID.LoadPlayer:
                    var slot = data.AsPacket<LoadPlayer>();
                    index = slot.PlayerSlot;
                    Client.AddBuff(149, 120);
                    SendToServerDirect(Client.Player.OriginCharacter.Info);
                    SendToServerDirect(new ClientUUID(Client.Player.UUID));
                    SendToServerDirect(new RequestWorldInfo());//请求世界信息
                    SendToServerDirect(new SyncIP()
                    {
                        PlayerName = Client.Name,
                        IP = Client.IP
                    });  //尝试同步玩家IP
                    return false;
                case MessageID.WorldData:
                    var worldData = data.AsPacket<WorldData>();
#if DEBUG
                    Client.SendInfoMessage($"SSC: {worldData.EventInfo1[6]}");
#endif
                    //Client.Player.UpdateData(worldData, false);
                    SendToServerDirect(new RequestTileData(new(Client.SpawnX, Client.SpawnY)));//请求物块数据
                    SendToServerDirect(new SpawnPlayer(index, new(Client.SpawnX, Client.SpawnY), Client.Player.Timer, Client.Player.DeathsPVE, Client.Player.DeathsPVP, Terraria.PlayerSpawnContext.SpawningIntoWorld));//请求物块数据
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
                    Client.SendDataToClient(new SpawnPlayer(Client.Index, new(Client.SpawnX, (short)(Client.SpawnY - 3)), Client.Player.Timer, Client.Player.DeathsPVE, Client.Player.DeathsPVP, Terraria.PlayerSpawnContext.SpawningIntoWorld));
                    return false;
                case MessageID.FinishedConnectingToServer:
                    IsConnecting = false;
                    Client.State = ClientState.InGame;
                    Parent.DeregisteHander(this); //转换处理模式为普通
                    if (Hooks.OnPostSwitch(Client, TargetServer, out _))
                        return false;
                    Client.SendSuccessMessage(Localization.Instance["Prompt_ConnectSuccess", TargetServer.Name]);
                    Logs.Success($"[{Client.Name}] successfully joined the server: {TargetServer.Name}");
                    return false;
            }
            return false;
        }
    }
}
