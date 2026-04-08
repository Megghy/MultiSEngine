using MultiSEngine.Application.Transfers;

namespace MultiSEngine.Protocol.Handlers
{
    public class PreConnectHandler(BaseAdapter parent, PreConnectSession session) : BaseHandler(parent)
    {
        public PreConnectSession Session { get; } = session;
        public ServerInfo TargetServer => Session.TargetServer;
        public bool IsConnecting => Session.IsConnecting;

        public override void Dispose()
        {
            base.Dispose();
            Session.Dispose();
        }
        public override ValueTask<bool> RecieveClientDataAsync(HandlerPacketContext context)
        {
            var msgType = context.MessageId;
            if (msgType != MessageID.ClientHello && msgType != MessageID.Unused15 && msgType > (MessageID)12 && msgType != (MessageID)93 && msgType != (MessageID)16 && msgType != (MessageID)42 && msgType != (MessageID)50 && msgType != (MessageID)38 && msgType != (MessageID)68)
            {
                return ValueTask.FromResult(true);
            }
            return ValueTask.FromResult(false);
        }
        private async ValueTask FinishedConnectingAsync()
        {
            Session.MarkSucceeded();
            Parent.DeregisterHandler(this); //转换处理模式为普通
            if (Hooks.OnPostSwitch(Client, TargetServer, out _))
            {
                return;
            }
            await Client.SendSuccessMessageAsync(Localization.Instance["Prompt_ConnectSuccess", TargetServer.Name]).ConfigureAwait(false);
            Logs.Success($"[{Client.Name}] successfully joined the server: {TargetServer.Name}");
        }
        public override async ValueTask<bool> RecieveServerDataAsync(HandlerPacketContext context)
        {
            var msgType = context.MessageId;

            Session.BufferPacket(context.Data);

            switch (msgType)
            {
                case MessageID.Kick:
                    if (context.Packet is not Kick kick)
                        throw new Exception("[PreConnectHandler] Kick packet not found");
                    Logs.Info($"[{Client.Name}] kicked by [{TargetServer.Name}]: {kick.Reason.GetText()}");
                    Session.MarkFailed($"PreConnect failed to {TargetServer.Name}: {kick.Reason.GetText()}");
                    Parent.DeregisterHandler(this);
                    break;
                case MessageID.LoadPlayer:
                    if (context.Packet is not LoadPlayer slot)
                        throw new Exception("[PreConnectHandler] LoadPlayer packet not found");
                    Session.SetRemotePlayerIndex(slot.PlayerSlot);
                    Logs.Info($"[{Client.Name}] remote index: {slot.PlayerSlot}");
                    if (Config.Instance.UseCrowdControlled)
                    {
                        await Client.AddBuffAsync(149, 60).ConfigureAwait(false);
                    }

                    var tempInfo = PlayerStateStore.CreateRemoteSyncPlayer(Client.Player, slot.PlayerSlot);
                    await SendToServerDirectAsync(tempInfo).ConfigureAwait(false);
                    await SendToServerDirectAsync(new SyncIP()
                    {
                        PlayerName = Client.Name,
                        IP = Client.IP
                    }).ConfigureAwait(false);  //尝试同步玩家IP

                    await SendToServerDirectAsync(new RequestWorldInfo()).ConfigureAwait(false);//请求世界信息
                    break;
                case MessageID.WorldData:
                    if (context.Packet is not WorldData worldData)
                        throw new Exception("[PreConnectHandler] WorldData packet not found");
#if DEBUG
                    await Client.SendInfoMessageAsync($"SSC: {worldData.EventInfo1[6]}").ConfigureAwait(false);
#endif
                    var worldMaxX = worldData.MaxTileX;
                    var worldMaxY = worldData.MaxTileY;

                    var spawnX = TargetServer.SpawnX > -1 && TargetServer.SpawnX <= worldMaxX ? TargetServer.SpawnX : worldData.SpawnX;
                    var spawnY = TargetServer.SpawnY > -1 && TargetServer.SpawnY <= worldMaxY ? TargetServer.SpawnY : worldData.SpawnY;

                    if ((spawnX != TargetServer.SpawnX && TargetServer.SpawnX > -1) || (spawnY != TargetServer.SpawnY && TargetServer.SpawnY > -1))
                    {
                        Logs.Warn($"[{Client.Name}] <Server: {TargetServer.Name}> spawn point invalid ({TargetServer.SpawnX}, {TargetServer.SpawnY} | World size: {worldMaxX}, {worldMaxY}), adjusted to ({spawnX}, {spawnY})");
                    }

                    Session.UpdateWorldData(worldData, spawnX, spawnY);

                    await SendToServerDirectAsync(new RequestTileData
                    {
                        Position = new Point(spawnX, spawnY)
                    }).ConfigureAwait(false);//请求物块数据

                    await SendToServerDirectAsync(new SpawnPlayer
                    {
                        PlayerSlot = Session.RemotePlayerIndex,
                        Position = new Terraria.DataStructures.Point16(Session.SpawnX, Session.SpawnY),
                        Timer = Client.Player.Timer,
                        DeathsPVE = Client.Player.DeathsPVE,
                        DeathsPVP = Client.Player.DeathsPVP,
                        Context = PlayerSpawnContext.SpawningIntoWorld
                    }).ConfigureAwait(false);

                    await SendToClientDirectAsync(context.Data);
                    break;
                case MessageID.RequestPassword:
                    if (context.Packet is not RequestPassword requestPassword)
                        throw new Exception("[PreConnectHandler] RequestPassword packet not found");
                    if (Client.State == ClientState.InGame)
                        return false;
                    if (Client.State == ClientState.RequestPassword)
                    {
                        await Client.SendInfoMessageAsync(Localization.Instance["Prompt_WrongPassword", TargetServer.Name, Localization.Get("Help_Password")]).ConfigureAwait(false);
                    }
                    else
                    {
                        Client.State = ClientState.RequestPassword;
                        await Client.SendInfoMessageAsync(Localization.Instance["Prompt_NeedPassword", TargetServer.Name, Localization.Get("Help_Password")]).ConfigureAwait(false);
                    }
                    break;
                case MessageID.StatusText:
                    break;
                case MessageID.StartPlaying:
                    if (context.Packet is not StartPlaying)
                        throw new Exception("[PreConnectHandler] StartPlaying packet not found");
                    break;
                case MessageID.FinishedConnectingToServer:
#if DEBUG
                    Console.WriteLine($"[PreConnectHandler] connect successed, sending data to client");
#endif
                    // 状态更新由 Join 统一处理
                    Client.SetIndex(Session.RemotePlayerIndex);
                    await TeleportService.CompleteTargetEntryAsync(Parent, Parent.Client, Session).ConfigureAwait(false);

                    await FinishedConnectingAsync().ConfigureAwait(false);

                    break;
            }
            return true;
        }
    }
}


