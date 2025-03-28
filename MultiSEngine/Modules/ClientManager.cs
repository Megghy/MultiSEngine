using EnchCoreApi.TrProtocol.NetPackets.Modules;
using Microsoft.Xna.Framework;
using MultiSEngine.Core;
using MultiSEngine.DataStruct;
using Terraria.GameContent.Drawing;
using Terraria.Localization;

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
                    client.SendErrorMessage(string.Format(Localization.Get("Command_AlreadyIn"), server.Name));
                Logs.Warn($"Unallowed transmission requests for [{client.Name}]");
            }
            else
            {
                Logs.Info($"Switching [{client.Name}] to the server: [{server.Name}]");
                client.State = ClientState.ReadyToSwitch;
                if (Utils.TryParseAddress(server.IP, out var ip))
                {
                    try
                    {
                        client.State = ClientState.Switching;

                        client.TempAdapter = new(client, client.Adapter.ClientConnection, server);//新建与服务器的连接

                        cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
                        await client.TempAdapter.TryConnect(cancel)
                            .ContinueWith(task =>
                            {
                                client.LastServer = client.CurrentServer;
                                client.CurrentServer = server;
                                client.State = ClientState.InGame;
                                client.Adapter.SetServerConnection(client.TempAdapter.ServerConnection, true);
                                client.TempAdapter.Dispose();
                                client.TempAdapter = null;
                                client.Sync(server);
                            }, cancel);
                    }
                    catch (Exception ex)
                    {
                        client.State = ClientState.ReadyToSwitch;
                        client.TempAdapter?.ServerConnection?.Dispose(true);
                        client.TempAdapter?.Dispose();
                        client.TempAdapter = null;
                        Logs.Error($"Unable to connect to server {server.IP}:{server.Port}{Environment.NewLine}{ex}");
                        client.SendErrorMessage(Localization.Instance["Prompt_CannotConnect", server.Name]);
                    }
                }
                else
                    client.SendErrorMessage(Localization.Instance["Prompt_UnknownAddress"]);
            }
        }
        public static void Back(this ClientData client)
        {
            if (client.CurrentServer == Config.Instance.DefaultServerInternal)
            {
                client.SendErrorMessage(Localization.Instance["Prompt_NoAvailableServer"]);
                Logs.Info($"No default server avilable, send [{client.Name}] to FakeWorld.");
                Logs.Info($"[{client.Name}] now in FakeWorld");
                client.State = ClientState.ReadyToSwitch;
                client.Player.ServerCharacter = new();
                client.CurrentServer = null;
                client.Adapter.ServerConnection?.Dispose(true);
                client.Sync(null);
                client.TP(4200, 1200);
                client.SendDataToClient(Data.StaticSpawnSquareData);
                client.SendDataToClient(Data.StaticDeactiveAllPlayer); //隐藏所有玩家
            }
            else if (client.CurrentServer is null)
                client.SendErrorMessage(Localization.Instance["Prompt_CannotConnect", client.TempAdapter?.TargetServer?.Name]);
            else
                Task.Run(() => client.Join(client.LastServer ?? Config.Instance.DefaultServerInternal));
        }
        public static void Sync(this ClientData client, ServerInfo targetServer)
        {
            Logs.Text($"Syncing player: [{client.Name}]");
            client.Syncing = true;

            var data = client.Player.ServerCharacter?.WorldData ?? client.Player.OriginCharacter.WorldData;
            if (!client.Player.SSC && Config.Instance.RestoreDataWhenJoinNonSSC) //非ssc的话还原玩家最开始的背包
            {
                var bb = data.EventInfo1;
                bb[6] = true;
                data.EventInfo1 = bb;
                client.SendDataToClient(data); //没有ssc的话没法改背包
                client.SendDataToClient(client.Player.OriginCharacter.Info);
                client.SendDataToClient(new PlayerHealth(client.Player.Index, client.Player.OriginCharacter.Health, client.Player.OriginCharacter.HealthMax));
                client.SendDataToClient(new PlayerMana(client.Player.Index, client.Player.OriginCharacter.Mana, client.Player.OriginCharacter.ManaMax));
                client.Player.OriginCharacter.Inventory.Where(i => i != null).ForEach(i => client.SendDataToClient(i));
                bb[6] = false;//改回去
                data.EventInfo1 = bb;
                client.SendDataToClient(data);
            }
            else
                client.SendDataToClient(data);

            client.TP(client.SpawnX, client.SpawnY - 3);
            //client.Adapter?.ResetAlmostEverything();
            client.SendDataToClient(new LoadPlayer(client.Player.Index, true));

            client.Syncing = false;
        }
        public static void Disconnect(this ClientData client, string reason = null)
        {
            if (client.Disposed)
                return;
            Logs.Text($"[{client.Name}] disconnected. {reason}");
            Hooks.OnPlayerLeave(client, out _);
            Data.Clients.Where(c => c.CurrentServer is null && c != client)
                .ForEach(c => c.SendMessage($"{client.Name} has leave."));
            client.SendDataToClient(new Kick(new(reason ?? "You have been kicked. Reason: unknown", NetworkTextModel.Mode.Literal)));
            client.Dispose();
        }
    }
    public static partial class ClientManager
    {
        #region 消息函数
        public static bool SendDataToClient(this ClientData client, ref Span<byte> buf)
        {
            try
            {
#if DEBUG
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[Send to CLIENT] <{BitConverter.ToInt16(buf)} byte>, Length: {buf.Length} - {(MessageID)buf[2]}");
                Console.ResetColor();
#endif
                if (buf is { Length: < 3 })
                {
#if DEBUG
                    Console.WriteLine($"[Send to CLIENT] <Invaild data> <{BitConverter.ToInt16(buf)} byte> Length: {buf.Length}");
#endif
                    return true;
                }
                return client?.Adapter?.ClientConnection?.Send(buf) ?? false;
            }
            catch (Exception ex)
            {
                Logs.Warn($"Failed to send data to {client.Name}{Environment.NewLine}{ex}");
                return false;
            }
        }
        public static bool SendDataToClient(this ClientData client, byte[] buffer, int start = 0, int? length = null)
        {
            var data = buffer.AsSpan().Slice(start, length ?? buffer.Length);
            return client.SendDataToClient(ref data);
        }
        public static bool SendDataToServer(this ClientData client, byte[] buffer, int start = 0, int? length = null)
        {
            var data = buffer.AsSpan().Slice(start, length ?? buffer.Length);
            return client.SendDataToServer(ref data);
        }
        public static bool SendDataToServer(this ClientData client, ref Span<byte> buf)
        {
            if (client.Adapter is not { ServerConnection: not null })
                return false;
            try
            {
#if DEBUG
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"[Send to SERVER] <{BitConverter.ToInt16(buf)} byte>, Length: {buf.Length} - {(MessageID)buf[2]}");
                Console.ResetColor();
#endif
                if (buf is { Length: < 3 })
                {
#if DEBUG
                    Console.WriteLine($"[Send to SERVER] <Invaild data> <{BitConverter.ToInt16(buf)} byte>, Length: {buf.Length} - {(MessageID)buf[2]}");
#endif
                    return false;
                }
                //using var arg = new SocketAsyncEventArgs();
                //arg.SetBuffer(buffer ?? new byte[3] { 3, 0, 0 }, start, length ?? buffer?.Length ?? 3);
                return client.Adapter?.ServerConnection?.Send(buf) ?? false;
            }
            catch
            {
                Logs.Info($"Failed to send data to server: {client.Name}");
                return false;
            }
        }
        public static bool SendDataToClient(this ClientData client, NetPacket packet, bool asClient = false)
        {
            ArgumentNullException.ThrowIfNull(packet);
            ArgumentNullException.ThrowIfNull(client);
            /*if (Core.Hooks.OnSendPacket(client, packet, true, out _))
                return true;*/
            if (client.Disposed)
                return false;
            if (packet is WorldData world && (client.Player.TileX >= world.MaxTileX || client.Player.TileY >= world.MaxTileY))
                client.TP(client.SpawnY, client.SpawnY); //防止玩家超出地图游戏崩溃
            var data = packet.AsBytes();
            return client.SendDataToClient(ref data);
        }
        public static void SendDataToServer(this ClientData client, NetPacket packet, bool asClient = false)
        {
            if (packet is null)
                throw new ArgumentNullException(nameof(packet));
            if (client is null)
                throw new ArgumentNullException(nameof(client));
            /*if (Core.Hooks.OnSendPacket(client, packet, false, out _))
                return;*/
            if (client.Disposed)
                return;
            var data = packet.AsBytes();
            client.SendDataToServer(ref data); //发送给服务端则不需要区分版本
        }
        public static void SendMessage(this ClientData client, string text, Color color, bool withPrefix = true)
        {
            if (client is null)
                Console.WriteLine(text);
            else
                using (var writer = new BinaryWriter(new MemoryStream()))
                {
                    client.SendDataToClient(new NetTextModule(null, new TextS2C()
                    {
                        Text = new($"{(withPrefix ? $"{Localization.Instance["Prefix"]}" : "")}{text}", NetworkTextModel.Mode.Literal),
                        Color = color,
                        PlayerSlot = 255
                    }, true));
                }
        }
        public static void SendMessage(this ClientData client, string text, bool withPrefix = true) => SendMessage(client, text, new(255, 255, 255), withPrefix);
        public static void SendInfoMessage(this ClientData client, string text, bool withPrefix = true) => SendMessage(client, text, new(220, 220, 130), withPrefix);
        public static void SendSuccessMessage(this ClientData client, string text, bool withPrefix = true) => SendMessage(client, text, new(165, 230, 155), withPrefix);
        public static void SendErrorMessage(this ClientData client, string text, bool withPrefix = true) => SendMessage(client, text, new(220, 135, 135), withPrefix);
        #endregion

        #region 一些小工具
        public static void Broadcast(this ClientData client, string message, bool ignoreSelf = true) => Data.Clients.Where(c => !ignoreSelf || c != client).ForEach(c => c.SendMessage(message, false));
        public static void ReadVersion(this ClientData client, ClientHello hello) => client.ReadVersion(hello.Version);
        public static void ReadVersion(this ClientData client, string version)
        {

            client.Player.VersionNum = version.StartsWith("Terraria") && int.TryParse(version[8..], out var v)
                            ? v
                            : Config.Instance.DefaultServerInternal.VersionNum;
            Logs.Info($"Version of {client.Name} is {Data.Convert(client.Player.VersionNum)}<{client.Player.VersionNum}>.");
        }
        public static bool HandleCommand(this ClientData client, string cmd)
        {
            return Core.Command.HandleCommand(client, cmd, out _);
        }
        public static void TP(this ClientData client, int tileX, int tileY)
        {
            client.SendDataToClient(new Teleport(new(), client.Player.Index, new(tileX * 16, tileY * 16), 0, 0));
        }
        public static void AddBuff(this ClientData client, ushort buffID, int time = 60)
        {
            client?.SendDataToClient(new AddPlayerBuff(client.Player.Index, buffID, time));
        }
        public static void CreatePartical(this ClientData client, ParticleOrchestraType type, ParticleOrchestraSettings? setting = null)
        {
            client.SendDataToClient(new NetParticlesModule(type, setting ?? new()
            {
                MovementVector = new(0, 0),
                PositionInWorld = new(client.Player.X, client.Player.Y),
                UniqueInfoPiece = 0,
                IndexOfPlayerWhoInvokedThis = client.Player.Index
            }));
        }
        public static void CreatePartical(this ClientData client, ParticleOrchestraType type, Vector2 position, Vector2 movement = default)
        {
            client.CreatePartical(type, new()
            {
                MovementVector = movement,
                PositionInWorld = position,
                UniqueInfoPiece = 0,
                IndexOfPlayerWhoInvokedThis = client.Player.Index
            });
        }
        #endregion

        #region 其他
        public static ClientData GetClientByName(string name) => Data.Clients.FirstOrDefault(c => c.Name == name);
        #endregion
    }
}
