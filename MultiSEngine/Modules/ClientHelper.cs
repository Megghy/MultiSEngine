using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Intrinsics.X86;
using TrProtocol;
using TrProtocol.Models;
using TrProtocol.Packets;

namespace MultiSEngine.Modules
{
    /// <summary>
    /// 服务器切换
    /// </summary>
    public static partial class ClientHelper
    {
        /// <summary>
        /// 加入到指定的服务器
        /// </summary>
        /// <param name="client"></param>
        /// <param name="server"></param>
        public static async void Join(this ClientData client, ServerInfo server)
        {
            if (Core.Hooks.OnPreSwitch(client, server, out _))
                return;
            if (client.Server?.Name == server?.Name || (client.State > ClientData.ClientState.ReadyToSwitch && client.State < ClientData.ClientState.InGame))
            {
                if (client.Server == server)
                    client.SendErrorMessage(string.Format(Localization.Get("Command_AlreadyIn"), server.Name));
                Logs.Warn($"Unallowed transmission requests for [{client.Name}]");
                return;
            }
            Logs.Info($"Switching [{client.Name}] to the server: [{server.Name}]");
            client.State = ClientData.ClientState.ReadyToSwitch;
            if (Utils.TryParseAddress(server.IP, out var ip))
            {
                try
                {
                    client.State = ClientData.ClientState.Switching;
                    client.TimeOutTimer.Start();

                    client.TempConnection = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    await client.TempConnection.ConnectAsync(ip, server.Port); //新建与服务器的连接

                    client.CAdapter.ChangeProcessState(true);  //切换至正常的客户端处理

                    var tempAdapter = new VisualPlayerAdapter(client, client.TempConnection);
                    client.TempAdapter = tempAdapter;
                    tempAdapter.TryConnect(server, (client) =>
                    {
                        client.State = ClientData.ClientState.InGame;
                        client.SAdapter?.Stop(true);
                        client.SAdapter = client.TempAdapter;
                        client.TempConnection = null; 
                        client.TempAdapter = null;
                        client.TimeOutTimer.Stop();
                        client.Sync(server);
                    });
                }
                catch (Exception ex)
                {
                    client.State = ClientData.ClientState.ReadyToSwitch;
                    Logs.Error($"Unable to connect to server {server.IP}:{server.Port}{Environment.NewLine}{ex}");
                    client.SendErrorMessage(Localization.Instance["Prompt_CannotConnect", server.Name]);
                }
            }
            else
                client.SendErrorMessage(Localization.Instance["Prompt_UnknownAddress"]);
        }
        /// <summary>
        /// 返回到默认的初始服务器
        /// </summary>
        /// <param name="client"></param>
        public static void Back(this ClientData client)
        {
            if (client.Server == Config.Instance.DefaultServerInternal)
            {
                client.SendErrorMessage(Localization.Instance["Prompt_NoAvailableServer"]);
                Logs.Info($"No default server avilable, send [{client.Name}] to FakeWorld.");
                (client.CAdapter as FakeWorldAdapter)?.BackToThere();
            }
            else if (client.Server is null)
                client.SendErrorMessage(Localization.Instance["Prompt_CannotConnect", client.TempAdapter?.TempServer?.Name]);
            else
                client.Join(Config.Instance.DefaultServerInternal);
        }
        public static void Sync(this ClientData client, ServerInfo targetServer)
        {
            Logs.Text($"Syncing player: [{client.Name}]");
            client.Syncing = true;

            var data = client.Player.ServerData?.WorldData ?? client.Player.OriginData.WorldData;
            if (!client.Player.SSC && Config.Instance.RestoreDataWhenJoinNonSSC) //非ssc的话还原玩家最开始的背包
            {
                var bb = data.EventInfo1;
                bb[6] = true;
                data.EventInfo1 = bb;
                client.SendDataToClient(data); //没有ssc的话没法改背包
                client.SendDataToClient(client.Player.OriginData.Info);
                client.SendDataToClient(new PlayerHealth() { PlayerSlot = client.Player.Index, StatLife = client.Player.OriginData.Health, StatLifeMax = client.Player.OriginData.HealthMax });
                client.SendDataToClient(new PlayerMana() { PlayerSlot = client.Player.Index, StatMana = client.Player.OriginData.Mana, StatManaMax = client.Player.OriginData.ManaMax });
                client.Player.OriginData.Inventory.Where(i => i != null).ForEach(i => client.SendDataToClient(i));
                bb[6] = false;//改回去
                data.EventInfo1 = bb;
                client.SendDataToClient(data);
            }
            else
                client.SendDataToClient(data);

            client.TP(client.SpawnX, client.SpawnY - 3);
            client.SAdapter?.ResetAlmostEverything();
            client.SendDataToClient(new LoadPlayer() { PlayerSlot = client.Player.Index, ServerWantsToRunCheckBytesInClientLoopThread = true });

            client.Syncing = false;
        }
        public static void Disconnect(this ClientData client, string reason = null)
        {
            Logs.Text($"[{client.Name}] disconnected. {reason}");
            Core.Hooks.OnPlayerLeave(client, out _);
            Data.Clients.Where(c => c.Server is null && c != client).ForEach(c => c.SendMessage($"{client.Name} has leave."));
            if (client.CAdapter?.Connection is { Connected: true })
                client.SendDataToClient(new Kick() { Reason = new(reason ?? "Unknown", NetworkText.Mode.Literal) });
            if (!Data.Clients.Remove(client))
                Logs.Warn($"Abnormal disposed of client data.");
            client.Dispose();
        }
    }
    public static partial class ClientHelper
    {
        #region 消息函数
        public static bool SendDataToClient(this ClientData client, byte[] buffer, int start = 0, int? length = null)
        {
            try
            {
#if DEBUG
                Console.WriteLine($"[Send to CLIENT] <{BitConverter.ToInt16(buffer, start)} byte> {(length is null ? "" : $"<Length: {length}>")} {(MessageID)buffer[start + 2]}");
#endif
                if (buffer is { Length: < 3 } && buffer[start + 2] is < 1 or > 140)
                {
#if DEBUG
                    Console.WriteLine($"[Send to CLIENT] <Invaild data> <{BitConverter.ToInt16(buffer, start)} byte> {(length is null ? "" : $"<Length: {length}>")} {(MessageID)buffer[start + 2]}");
#endif
                    return true;
                }
                using var arg = new SocketAsyncEventArgs();
                arg.SetBuffer(buffer ?? new byte[3] { 3, 0, 0 }, start, length ?? buffer?.Length ?? 3);
                client.CAdapter?.Connection?.SendAsync(arg);
                return true;
            }
            catch (Exception ex)
            {
                Logs.Warn($"Failed to send data to {client.Name}{Environment.NewLine}{ex}");
                return false;
            }
        }
        public static void SendDataToServer(this ClientData client, byte[] buffer, int start = 0, int? length = null)
        {
            if (client.SAdapter is not { Connection: not null })
                return;
            try
            {
#if DEBUG
                Console.WriteLine($"[Send to SERVER] <{BitConverter.ToInt16(buffer)} byte> {(MessageID)buffer[start + 2]}");
#endif
                using var arg = new SocketAsyncEventArgs();
                arg.SetBuffer(buffer ?? new byte[3] { 3, 0, 0 }, start, length ?? buffer?.Length ?? 3);
                client.SAdapter?.Connection?.SendAsync(arg);
            }
            catch
            {
                Logs.Info($"Failed to send data to server: {client.Name}");
            }
        }
        public static bool SendDataToClient(this ClientData client, Packet packet, bool asClient = false)
        {
            if (packet is null)
                throw new ArgumentNullException(nameof(packet));
            if (Core.Hooks.OnSendPacket(client, packet, true, out _))
                return true;
            if (packet is WorldData world && (client.Player.TileX >= world.MaxTileX || client.Player.TileY >= world.MaxTileY))
                client.TP(client.SpawnY, client.SpawnY); //防止玩家超出地图游戏崩溃
            return client.SendDataToClient((asClient ? client.CAdapter.InternalClientSerializer : client.CAdapter.InternalServerSerializer).Serialize(packet));
        }
        public static void SendDataToServer(this ClientData client, Packet packet, bool asClient = false)
        {
            if (packet is null)
                throw new ArgumentNullException(nameof(packet));
            if (Core.Hooks.OnSendPacket(client, packet, false, out _))
                return;
            client.SendDataToServer((asClient ? Core.Net.DefaultClientSerializer : Core.Net.DefaultServerSerializer).Serialize(packet)); //发送给服务端则不需要区分版本
        }
        public static void SendMessage(this ClientData client, string text, Color color, bool withPrefix = true)
        {
            if (client is null)
                Console.WriteLine(text);
            else
                using (var writer = new BinaryWriter(new MemoryStream()))
                {
                    client.SendDataToClient(new TrProtocol.Packets.Modules.NetTextModuleS2C()
                    {
                        Text = new($"{(withPrefix ? $"{Localization.Instance["Prefix"]}" : "")}{text}", NetworkText.Mode.Literal),
                        Color = color,
                        PlayerSlot = 255
                    });
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
            Logs.Info($"Version of {client.Name} is {Data.Convert(client.Player.VersionNum)}.");
        }
        public static bool HandleCommand(this ClientData client, string cmd)
        {
            return Core.Command.HandleCommand(client, cmd, out _);
        }
        public static void TP(this ClientData client, int tileX, int tileY)
        {
            client.SendDataToClient(new Teleport() { PlayerSlot = client.Player.Index, Position = new(tileX * 16, tileY * 16) });
        }
        public static void AddBuff(this ClientData client, int buffID, int time = 60)
        {
            client?.SendDataToClient(new AddPlayerBuff() { BuffTime = time, BuffType = (ushort)buffID, OtherPlayerSlot = client.Player.Index });
        }
        public static void CreatePartical(this ClientData client, ParticleOrchestraType type, ParticleOrchestraSettings setting = null)
        {
            client.SendDataToClient(new TrProtocol.Packets.Modules.NetParticlesModule()
            {
                ParticleType = type,
                Setting = setting ?? new()
                {
                    MovementVector = new(0, 0),
                    PositionInWorld = new(client.Player.X, client.Player.Y),
                    PackedShaderIndex = 0,
                    IndexOfPlayerWhoInvokedThis = client.Player.Index
                }
            });
        }
        public static void CreatePartical(this ClientData client, ParticleOrchestraType type, Vector2 position, Vector2 movement = default)
        {
            client.CreatePartical(type, new()
            {
                MovementVector = movement,
                PositionInWorld = position,
                PackedShaderIndex = 0,
                IndexOfPlayerWhoInvokedThis = client.Player.Index
            });
        }
        #endregion
        #region 其他
        public static ClientData GetClientByName(string name) => Data.Clients.FirstOrDefault(c => c.Name == name);
        #endregion
    }
}
