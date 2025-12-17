using System.Text.Json;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.DataStruct.CustomData;
using MultiSEngine.Modules;

namespace MultiSEngine.Core.Handler
{
    public class PreConnectHandler(BaseAdapter parent, ServerInfo server) : BaseHandler(parent)
    {
        public short SpawnX { get; private set; } = -1;
        public short SpawnY { get; private set; } = -1;
        public WorldData World { get; private set; }

        public ServerInfo TargetServer { get; init; } = server;
        public bool IsConnecting { get; private set; } = true;

        private byte index = 0;
        private readonly TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private List<ReadOnlyMemory<byte>> recievedPackets = [];

        public override void Dispose()
        {
            base.Dispose();
            recievedPackets.Clear();
        }

        public Task<bool> ConnectionTask => _tcs.Task;
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
            _tcs.TrySetResult(true); //设置连接成功
            Parent.DeregisterHandler(this); //转换处理模式为普通
            IsConnecting = false;
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

            recievedPackets.Add(context.Data.ToArray());

            switch (msgType)
            {
                case MessageID.Kick:
                    var kick = context.Packet as Kick ?? throw new Exception("[PreConnectHandler] Kick packet not found");
                    await Parent.DisposeAsync(true).ConfigureAwait(false);
                    await Client.SendErrorMessageAsync(Localization.Instance["Prompt_Disconnect", TargetServer.Name, kick.Reason.GetText()]).ConfigureAwait(false);
                    Logs.Info($"[{Client.Name}] kicked by [{TargetServer.Name}]: {kick.Reason.GetText()}");
                    Client.State = ClientState.Disconnect;
                    IsConnecting = false;
                    _tcs.TrySetResult(false);
                    Parent.DeregisterHandler(this);
                    break;
                case MessageID.LoadPlayer:
                    var slot = context.Packet as LoadPlayer ?? throw new Exception("[PreConnectHandler] LoadPlayer packet not found");

                    if (Client.Player.Index != slot.PlayerSlot) // 玩家索引需要更新
                        return true; // 预连接阶段拦截，不直接下发给客户端

                    index = slot.PlayerSlot;
                    Logs.Info($"[{Client.Name}] remote index: {slot.PlayerSlot}");
                    await Client.AddBuffAsync(149, 60).ConfigureAwait(false);

                    var tempInfo = JsonSerializer.Deserialize<SyncPlayer>(JsonSerializer.Serialize(Client.Player.OriginCharacter.Info));
                    tempInfo.PlayerSlot = index;
                    await SendToServerDirectAsync(tempInfo).ConfigureAwait(false);

                    await SendToServerDirectAsync(new ClientUUID
                    {
                        UUID = Client.Player.UUID
                    }).ConfigureAwait(false);
                    await SendToServerDirectAsync(new SyncIP()
                    {
                        PlayerName = Client.Name,
                        IP = Client.IP
                    }).ConfigureAwait(false);  //尝试同步玩家IP

                    await SendToServerDirectAsync(new RequestWorldInfo()).ConfigureAwait(false);//请求世界信息
                    break;
                case MessageID.WorldData:
                    var worldData = context.Packet as WorldData ?? throw new Exception("[PreConnectHandler] WorldData packet not found");
#if DEBUG
                    await Client.SendInfoMessageAsync($"SSC: {worldData.EventInfo1[6]}").ConfigureAwait(false);
#endif
                    World ??= worldData;
                    var worldMaxX = worldData.MaxTileX;
                    var worldMaxY = worldData.MaxTileY;

                    var spawnX = TargetServer.SpawnX > -1 && TargetServer.SpawnX <= worldMaxX ? TargetServer.SpawnX : worldData.SpawnX;
                    var spawnY = TargetServer.SpawnY > -1 && TargetServer.SpawnY <= worldMaxY ? TargetServer.SpawnY : worldData.SpawnY;

                    if ((spawnX != TargetServer.SpawnX && TargetServer.SpawnX > -1) || (spawnY != TargetServer.SpawnY && TargetServer.SpawnY > -1))
                    {
                        Logs.Warn($"[{Client.Name}] <Server: {TargetServer.Name}> spawn point invalid ({TargetServer.SpawnX}, {TargetServer.SpawnY} | World size: {worldMaxX}, {worldMaxY}), adjusted to ({spawnX}, {spawnY})");
                    }

                    SpawnX = spawnX;
                    SpawnY = spawnY;

                    await SendToServerDirectAsync(new RequestTileData
                    {
                        Position = new(spawnX, spawnY)
                    }).ConfigureAwait(false);//请求物块数据

                    await SendToServerDirectAsync(new SpawnPlayer
                    {
                        PlayerSlot = index,
                        Position = new ShortPosition(SpawnX, SpawnY),
                        Timer = Client.Player.Timer,
                        DeathsPVE = Client.Player.DeathsPVE,
                        DeathsPVP = Client.Player.DeathsPVP,
                        Context = PlayerSpawnContext.SpawningIntoWorld
                    }).ConfigureAwait(false);

                    await SendToClientDirectAsync(context.Data);
                    break;
                case MessageID.RequestPassword:
                    var requestPassword = context.Packet as RequestPassword ?? throw new Exception("[PreConnectHandler] RequestPassword packet not found");
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
                    var startPlaying = context.Packet as StartPlaying ?? throw new Exception("[PreConnectHandler] StartPlaying packet not found");
                    break;
                case MessageID.FinishedConnectingToServer:
#if DEBUG
                    Console.WriteLine($"[PreConnectHandler] connect successed, sending data to client");
#endif
                    // 状态更新由 Join 统一处理
                    Client.SetIndex(index); //设置玩家索引

                    await Parent.Client.Teleport(SpawnX, SpawnY - 3);
                    await SendToClientDirectAsync(World);

                    await Parent.SendToClientBatchAsync(recievedPackets).ConfigureAwait(false); // 将缓存的数据包批量发送给客户端, 等待客户端同步完成(客户端发送 RequestWorldInfo)

                    await FinishedConnectingAsync().ConfigureAwait(false);

                    break;
            }
            return true;
        }
    }
}
