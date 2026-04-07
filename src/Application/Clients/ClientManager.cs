namespace MultiSEngine.Application.Clients
{
    /// <summary>
    /// 服务器切换
    /// </summary>
    public static partial class ClientManager
    {
        /// <summary>
        /// 加入到指定的服务器
        /// </summary>
        /// <param name="client"></param>
        /// <param name="server"></param>
        public static async Task Join(this ClientData client, ServerInfo server, CancellationToken cancel = default)
        {
            if (Hooks.OnPreSwitch(client, server, out _))
                return;

            if (client.CurrentServer?.Name == server?.Name || (client.State > ClientState.ReadyToSwitch && client.State < ClientState.InGame))
            {
                if (client.CurrentServer == server)
                    await client.SendErrorMessageAsync(string.Format(Localization.Get("Command_AlreadyIn"), server.Name)).ConfigureAwait(false);
                Logs.Warn($"Unallowed transmission requests for [{client.Name}]");
                return;
            }

            Logs.Info($"Switching [{client.Name}] to the server: [{server?.Name ?? "DefaultWorld"}]");
            client.State = ClientState.ReadyToSwitch;

            try
            {
                try
                {
                    client.State = ClientState.Switching;
                    client.Adapter?.PauseRouting(true, true);

                    // 创建临时适配器，订阅客户端连接；并暂时关闭主适配器的上行转发，避免与临时适配器并发上行
                    client.TempAdapter = new(client, client.Adapter.ClientConnection, server);

                    cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
                    await client.TempAdapter.TryConnect(cancel).ConfigureAwait(false);

                    // 先停止临时适配器的订阅，再迁移连接，避免同一 ServerConnection 双读
                    await client.TempAdapter.DisposeAsync(false).ConfigureAwait(false);

                    // 切换完成，接管新服务器连接并清理临时适配器
                    client.LastServer = client.CurrentServer;
                    client.CurrentServer = server;
                    client.State = ClientState.InGame;
                    await client.Adapter.SetServerConnectionAsync(client.TempAdapter.ServerConnection, true).ConfigureAwait(false);
                    client.Player.ServerCharacter.WorldData = client.TempAdapter.ConnectHandler.World ?? throw new Exception("[ClientManager] World data missing after pre-connect");
                    client.Player.SpawnX = client.TempAdapter.ConnectHandler.SpawnX;
                    client.Player.SpawnY = client.TempAdapter.ConnectHandler.SpawnY;

                    client.TempAdapter = null;
                    await client.SyncAsync(server, cancel).ConfigureAwait(false);
                    client.Adapter?.PauseRouting(false, false);
                }
                catch (InvalidOperationException ex)
                {
                    await HandleSwitchFailureAsync(client, ex, Localization.Instance["Prompt_UnknownAddress"]).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await HandleSwitchFailureAsync(client, ex, Localization.Instance["Prompt_CannotConnect", server.Name]).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"An error occurred while switching servers: {ex}");
            }
        }

        private static async ValueTask HandleSwitchFailureAsync(ClientData client, Exception ex, string message)
        {
            client.State = ClientState.ReadyToSwitch;
            client.Adapter?.PauseRouting(false, false);
            if (client.TempAdapter?.ServerConnection is { })
                await client.TempAdapter.ServerConnection.DisposeAsync(true).ConfigureAwait(false);
            if (client.TempAdapter is { })
                await client.TempAdapter.DisposeAsync().ConfigureAwait(false);
            client.TempAdapter = null;
            Logs.Error($"Unable to connect to server: {client?.Name ?? "<unknown>"}{Environment.NewLine}{ex}");
            await client.SendErrorMessageAsync(message).ConfigureAwait(false);
        }
        public static async ValueTask BackAsync(this ClientData client, CancellationToken cancellationToken = default)
        {
            if (client is null)
                return;

            var noAvailableWorld = (client.LastServer ?? Config.Instance.DefaultServerInternal) is null;
            if (client.CurrentServer == Config.Instance.DefaultServerInternal || noAvailableWorld)
            {
                await client.SendErrorMessageAsync(Localization.Instance["Prompt_NoAvailableServer"]).ConfigureAwait(false);
                Logs.Info($"No default server avilable, send [{client.Name}] to FakeWorld.");
                Logs.Info($"[{client.Name}] now in FakeWorld");
                client.State = ClientState.ReadyToSwitch;
                client.Player.ServerCharacter = new();
                client.CurrentServer = null;
                // 使用异步释放连接，避免阻塞
                if (client.Adapter?.ServerConnection is { } serverConnection)
                    await serverConnection.DisposeAsync(true).ConfigureAwait(false);
                await client.SyncAsync(null, cancellationToken).ConfigureAwait(false);
                if (client.Adapter is { } adapter)
                {
                    await adapter.SendToClientDirectAsync(RuntimeState.SpawnSquarePacket, cancellationToken).ConfigureAwait(false);
                    await client.Teleport(4200, 1200).ConfigureAwait(false);
                    await adapter.SendToClientDirectAsync(RuntimeState.DeactivateAllPlayerPacket, cancellationToken).ConfigureAwait(false); //隐藏所有玩家
                }
            }
            else if (client.CurrentServer is null)
                await client.SendErrorMessageAsync(Localization.Instance["Prompt_CannotConnect", client.TempAdapter?.TargetServer?.Name]).ConfigureAwait(false);
            else
                await client.Join(client.LastServer ?? Config.Instance.DefaultServerInternal, cancellationToken).ConfigureAwait(false);
        }

        public static void Back(this ClientData client)
            => _ = client?.BackAsync();

        public static async ValueTask SyncAsync(this ClientData client, ServerInfo targetServer, CancellationToken cancellationToken = default)
        {
            _ = targetServer;
            Logs.Text($"Syncing player: [{client.Name}]");
            client.Syncing = true;

            var rentals = new List<Utils.PacketMemoryRental>();

            void EnqueuePacket(INetPacket packet)
            {
                var rental = packet.AsPacketRental();
                rentals.Add(rental);
            }

            var data = client.Player.ServerCharacter?.WorldData ?? client.Player.OriginCharacter.WorldData ?? throw new Exception("[ClientManager] World data not available for sync");
            if (!client.Player.SSC && Config.Instance.RestoreDataWhenJoinNonSSC) //非ssc的话还原玩家最开始的背包
            {
                var bb = data.EventInfo1;
                bb[6] = true;
                data.EventInfo1 = bb;
                EnqueuePacket(data); //没有ssc的话没法改背包
                EnqueuePacket(client.Player.OriginCharacter.Info ?? throw new Exception("[ClientManager] Origin player info not available for sync"));
                EnqueuePacket(new PlayerHealth
                {
                    PlayerSlot = client.Player.Index,
                    StatLife = client.Player.OriginCharacter.Health,
                    StatLifeMax = client.Player.OriginCharacter.HealthMax,
                });
                EnqueuePacket(new PlayerMana
                {
                    PlayerSlot = client.Player.Index,
                    StatMana = client.Player.OriginCharacter.Mana,
                    StatManaMax = client.Player.OriginCharacter.ManaMax,
                });
                EnqueuePacket(client.Player.OriginCharacter.CreateSyncLoadoutPacket(client.Player.Index));
                client.Player.OriginCharacter
                    .EnumerateSyncEquipment(client.Player.Index)
                    .ForEach(packet => EnqueuePacket(packet));
                bb[6] = false;//改回去
                data.EventInfo1 = bb;
                EnqueuePacket(data);
            }
            else
            {
                EnqueuePacket(data);
            }

            try
            {
                await DispatchBatchToClientAsync(client, rentals, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                client.Syncing = false;
            }
        }
        public static void Disconnect(this ClientData client, string reason = null)
            => _ = client.DisconnectAsync(reason);

        public static async ValueTask DisconnectAsync(this ClientData client, string reason = null)
        {
            if (client.Disposed)
                return;
            Logs.Text($"[{client.Name}] disconnected. {reason}");
            Hooks.OnPlayerLeave(client, out _);
            foreach (var c in RuntimeState.Clients.Where(c => c.CurrentServer is null && c != client))
                await c.SendMessageAsync($"{client.Name} has leave.", Utils.Rgb(255, 255, 255), true).ConfigureAwait(false);
            if (client.Adapter is { } adapter)
            {
                await adapter
                    .SendToClientDirectAsync(new Kick
                    {
                        Reason = Utils.LiteralText(reason ?? "You have been kicked. Reason: unknown")
                    })
                    .ConfigureAwait(false);
            }
            client.Dispose();
        }
    }
}


