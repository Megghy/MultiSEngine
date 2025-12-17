using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MultiSEngine.Core;
using MultiSEngine.DataStruct;

namespace MultiSEngine.Modules
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
                    client.Player.ServerCharacter.WorldData = client.TempAdapter.ConnectHandler.World;
                    client.Player.SpawnX = client.TempAdapter.ConnectHandler.SpawnX;
                    client.Player.SpawnY = client.TempAdapter.ConnectHandler.SpawnY;
                    client.Adapter?.PauseRouting(false, false);

                    client.TempAdapter = null;
                    await client.SyncAsync(server, cancel).ConfigureAwait(false);
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
                await client.Teleport(4200, 1200).ConfigureAwait(false);
                if (client.Adapter is { } adapter)
                {
                    await adapter.SendToClientDirectAsync(Data.SpawnSquarePacket, cancellationToken).ConfigureAwait(false);
                    await adapter.SendToClientDirectAsync(Data.DeactivateAllPlayerPacket, cancellationToken).ConfigureAwait(false); //隐藏所有玩家
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

            void EnqueuePacket(Packet packet)
            {
                var rental = packet.AsPacketRental();
                rentals.Add(rental);
            }

            var data = client.Player.ServerCharacter?.WorldData ?? client.Player.OriginCharacter.WorldData;
            if (!client.Player.SSC && Config.Instance.RestoreDataWhenJoinNonSSC) //非ssc的话还原玩家最开始的背包
            {
                var bb = data.EventInfo1;
                bb[6] = true;
                data.EventInfo1 = bb;
                EnqueuePacket(data); //没有ssc的话没法改背包
                EnqueuePacket(client.Player.OriginCharacter.Info);
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
                client.Player.OriginCharacter.Inventory.Where(i => i != null).ForEach(EnqueuePacket);
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
            foreach (var c in Data.Clients.Where(c => c.CurrentServer is null && c != client))
                await c.SendMessageAsync($"{client.Name} has leave.", new Color(255, 255, 255), true).ConfigureAwait(false);
            if (client.Adapter is { } adapter)
            {
                await adapter
                    .SendToClientDirectAsync(new Kick
                    {
                        Reason = NetworkText.FromLiteral(reason ?? "You have been kicked. Reason: unknown")
                    })
                    .ConfigureAwait(false);
            }
            client.Dispose();
        }
    }
    public static partial class ClientManager
    {
        #region 消息函数
        public static async ValueTask<bool> SendDataToClientAsync(this ClientData client, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (client is null || client.Disposed)
                return false;
            if (client.Adapter?.ClientConnection is null)
                return false;
            if (buffer.Length < 3)
                return true;

            try
            {
#if DEBUG
                var span = buffer.Span;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[Send to CLIENT] <{BitConverter.ToInt16(span)} byte>, Length: {span.Length} - {(MessageID)span[2]}");
                Console.ResetColor();
#endif
                return await client.Adapter.SendToClientDirectAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.Warn($"Failed to send data to {client.Name}{Environment.NewLine}{ex}");
                return false;
            }
        }
        public static async ValueTask<bool> SendDataToServerAsync(this ClientData client, ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (client is null || client.Disposed)
                return false;
            if (client.Adapter?.ServerConnection is null)
                return false;
            if (buffer.Length < 3)
                return true;

            try
            {
#if DEBUG
                var span = buffer.Span;
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"[Send to SERVER] <{BitConverter.ToInt16(span)} byte>, Length: {span.Length} - {(MessageID)span[2]}");
                Console.ResetColor();
#endif
                return await client.Adapter.SendToServerDirectAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.Info($"Failed to send data to server: {client.Name}{Environment.NewLine}{ex}");
                return false;
            }
        }

        // Removed synchronous SendDataToClient/Server wrappers to avoid blocking. Prefer async APIs above.
        public static async ValueTask SendMessageAsync(this ClientData client, string text, Color color, bool withPrefix = true)
        {
            if (client is null)
            {
                Console.WriteLine(text);
                return;
            }
            if (client.Adapter is { } adapter)
            {
                var message = withPrefix ? $"{Localization.Instance["Prefix"]}{text}" : text;
                await adapter
                    .SendToClientDirectAsync(new NetTextModuleS2C
                    {
                        PlayerSlot = 255,
                        Text = NetworkText.FromLiteral(message),
                        Color = color,
                    })
                    .ConfigureAwait(false);
            }
        }

        public static ValueTask SendMessageAsync(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessageAsync(text, new Color(255, 255, 255), withPrefix);

        public static ValueTask SendInfoMessageAsync(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessageAsync(text, new Color(220, 220, 130), withPrefix);

        public static ValueTask SendSuccessMessageAsync(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessageAsync(text, new Color(165, 230, 155), withPrefix);

        public static ValueTask SendErrorMessageAsync(this ClientData client, string text, bool withPrefix = true)
            => client.SendMessageAsync(text, new Color(220, 135, 135), withPrefix);
        public static void SendMessage(this ClientData client, string text, Color color, bool withPrefix = true)
            => _ = client.SendMessageAsync(text, color, withPrefix);
        public static void SendMessage(this ClientData client, string text, bool withPrefix = true) => client.SendMessage(text, new Color(255, 255, 255), withPrefix);
        public static void SendInfoMessage(this ClientData client, string text, bool withPrefix = true) => client.SendMessage(text, new Color(220, 220, 130), withPrefix);
        public static void SendSuccessMessage(this ClientData client, string text, bool withPrefix = true) => client.SendMessage(text, new Color(165, 230, 155), withPrefix);
        public static void SendErrorMessage(this ClientData client, string text, bool withPrefix = true) => client.SendMessage(text, new Color(220, 135, 135), withPrefix);
        #endregion

        #region 一些小工具
        public static async ValueTask BroadcastAsync(this ClientData client, string message, bool ignoreSelf = true)
        {
            foreach (var c in Data.Clients.Where(c => !ignoreSelf || c != client))
                await c.SendMessageAsync(message, new Color(255, 255, 255), false).ConfigureAwait(false);
        }
        public static void Broadcast(this ClientData client, string message, bool ignoreSelf = true) => _ = client.BroadcastAsync(message, ignoreSelf);
        public static void ReadVersion(this ClientData client, ClientHello hello) => client.ReadVersion(hello.Version);
        public static void ReadVersion(this ClientData client, string version)
        {

            client.Player.VersionNum = version.StartsWith("Terraria") && int.TryParse(version[8..], out var v)
                            ? v
                            : Config.Instance.DefaultServerInternal.VersionNum;
            Logs.Info($"Version of {client.Name} is {Data.Convert(client.Player.VersionNum)}<{client.Player.VersionNum}>.");
        }
        public static ValueTask<(bool handled, bool continueSend)> HandleCommand(this ClientData client, string cmd)
        {
            return Core.Command.HandleCommand(client, cmd);
        }
        public static async ValueTask Teleport(this ClientData client, int tileX, int tileY)
        {
            await client.Adapter.SendToClientDirectAsync(new Teleport
            {
                Bit1 = new BitsByte(),
                PlayerSlot = client.Player.Index,
                Position = new Vector2(tileX * 16, tileY * 16),
                Style = 0,
                ExtraInfo = 0,
            });
        }
        public static async ValueTask AddBuffAsync(this ClientData client, ushort buffID, int time = 60)
        {
            if (client?.Adapter is { } adapter)
            {
                await adapter.SendToClientDirectAsync(new AddPlayerBuff
                {
                    OtherPlayerSlot = client.Player.Index,
                    BuffType = buffID,
                    BuffTime = time,
                }).ConfigureAwait(false);
            }
        }
        public static void AddBuff(this ClientData client, ushort buffID, int time = 60)
            => _ = client.AddBuffAsync(buffID, time);
        public static async ValueTask CreateParticalAsync(this ClientData client, ParticleOrchestraType type, ParticleOrchestraSettings? setting = null)
        {
            if (client?.Adapter is not { } adapter)
                return;

            await adapter.SendToClientDirectAsync(new NetParticlesModule
            {
                ParticleType = type,
                Setting = setting ?? new ParticleOrchestraSettings
                {
                    MovementVector = new Vector2(0, 0),
                    PositionInWorld = new Vector2(client.Player.X, client.Player.Y),
                    PackedShaderIndex = 0,
                    IndexOfPlayerWhoInvokedThis = client.Player.Index,
                }
            }).ConfigureAwait(false);
        }
        public static void CreatePartical(this ClientData client, ParticleOrchestraType type, ParticleOrchestraSettings? setting = null)
            => _ = client.CreateParticalAsync(type, setting);
        public static void CreatePartical(this ClientData client, ParticleOrchestraType type, Vector2 position, Vector2 movement = default)
        {
            client.CreatePartical(type, new ParticleOrchestraSettings
            {
                MovementVector = movement,
                PositionInWorld = position,
                PackedShaderIndex = 0,
                IndexOfPlayerWhoInvokedThis = client.Player.Index,
            });
        }
        #endregion

        #region 其他
        public static ClientData GetClientByName(string name) => Data.Clients.FirstOrDefault(c => c.Name == name);
        #endregion

        private static async ValueTask DispatchBatchToClientAsync(ClientData client, List<Utils.PacketMemoryRental> rentals, CancellationToken cancellationToken)
        {
            if (rentals.Count == 0)
            {
                foreach (var rental in rentals)
                    rental.Dispose();
                return;
            }

            var adapter = client.Adapter;
            if (adapter is null)
            {
                foreach (var rental in rentals)
                    rental.Dispose();
                return;
            }

            var bufferArray = new ReadOnlyMemory<byte>[rentals.Count];
            var rentalArray = new Utils.PacketMemoryRental[rentals.Count];
            for (int i = 0; i < rentals.Count; i++)
            {
                rentalArray[i] = rentals[i];
                bufferArray[i] = rentals[i].Memory;
            }

            rentals.Clear();

            try
            {
                await adapter.SendToClientBatchAsync(bufferArray, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logs.Warn($"Failed to send batch data to {client.Name}{Environment.NewLine}{ex}");
            }
            finally
            {
                foreach (var rental in rentalArray)
                    rental.Dispose();
            }
        }
    }
}
