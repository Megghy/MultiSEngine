using Delphinus;
using Delphinus.Packets;
using Microsoft.Xna.Framework;
using MultiSEngine.Core.Adapter;
using MultiSEngine.Modules.DataStruct;
using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;

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
        public static void Join(this ClientData client, ServerInfo server)
        {
            if (client.Server?.Name == server?.Name || (client.State > ClientData.ClientState.ReadyToSwitch && client.State < ClientData.ClientState.InGame))
            {
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
                    client.TempConnection.Connect(ip, server.Port); //新建与服务器的连接

                    if (client.CAdapter is FakeWorldAdapter fwa)
                        fwa.ChangeProcessState(true);  //切换至正常的客户端处理

                    var tempAdapter = new VisualPlayerAdapter(client, client.TempConnection);
                    tempAdapter.TryConnect(server, (tempAdapter, client) =>
                    {
                        //Logs.Info($"Visual player: {client.Name} connect success.");
                        client.State = ClientData.ClientState.InGame;
                        client.SAdapter?.Stop(true);
                        client.SAdapter = tempAdapter;
                        client.TempConnection = null;
                        client.TimeOutTimer.Stop();
                        client.Sync();
                        client.Server = server;
                    });
                }
                catch (Exception ex)
                {
                    client.State = ClientData.ClientState.ReadyToSwitch;
                    Logs.Error($"Unable to connect to server {server.IP}:{server.Port}{Environment.NewLine}{ex}");
                    //client.SendErrorMessage(string.Format(Localization.Get("Prompt_CannotConnect"), server.Name));
                    client.SendErrorMessage(string.Format(Localization.Get("Prompt_CannotConnect"), server.Name));
                }
            }
            else
                client.SendErrorMessage(Localization.Get("Prompt_UnknownAddress"));
        }
        /// <summary>
        /// 返回到默认的初始服务器
        /// </summary>
        /// <param name="client"></param>
        public static void Back(this ClientData client) => client.Join(Config.Instance.DefaultServerInternal);
        public static void Sync(this ClientData client)
        {
            Logs.Text($"Syncing player: [{client.Name}]");
            client.Syncing = true;

            client.AddBuff(149, 120);
            client.SAdapter?.ResetAlmostEverything();
            client.SendDataToClient(new LoadPlayerPacket() { PlayerSlot = client.Player.Index, ServerWantsToRunCheckBytesInClientLoopThread = true });
            client.SendDataToClient(client.Player.ServerData.WorldData);
            if (!client.Player.SSC && Config.Instance.RestoreDataWhenJoinNonSSC) //非ssc的话还原玩家最开始的背包
            {
                client.SendDataToClient(client.Player.OriginData.Info);
                client.Player.OriginData.Inventory.Where(i => i != null).ForEach(i => client.SendDataToClient(i));
            }

            client.Syncing = false;
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
                Console.WriteLine($"[Send to CLIENT] <{BitConverter.ToInt16(buffer, start)} byte> {(length is null ? "" : $"<Length: {length}>")} {buffer.GetMessageID()}");
#endif
                if (buffer is { Length: < 3 } && buffer[start + 2] is < 1 or > 140)
                {
#if DEBUG
                    Console.WriteLine($"[Send to CLIENT] <Invaild data> <{BitConverter.ToInt16(buffer, start)} byte> {(length is null ? "" : $"<Length: {length}>")} {buffer.GetMessageID()}");
#endif
                    return true;
                }
                using (var arg = new SocketAsyncEventArgs())
                {
                    arg.SetBuffer(buffer ?? new byte[3] { 3, 0, 0 }, start, length ?? buffer.Length);
                    client.CAdapter?.Connection?.SendAsync(arg);
                    //client.CAdapter?.Connection?.Send(buffer ?? new byte[3] { 3, 0, 0 }, start, length ?? buffer.Length, SocketFlags.None);
                }
                return true;
            }
            catch (Exception ex)
            {
                Logs.Warn($"Failed to send data to {client.Name}{Environment.NewLine}{ex}");
                return false;
            }
        }
        public static void SendDataToGameServer(this ClientData client, byte[] buffer, int start = 0, int? length = null)
        {
            if (client.SAdapter is not { Connection: not null })
                return;
            try
            {
#if DEBUG
                Console.WriteLine($"[Send to SERVER] <{BitConverter.ToInt16(buffer)} byte> {buffer.GetMessageID()}");
#endif
                using (var arg = new SocketAsyncEventArgs())
                {
                    arg.SetBuffer(buffer ?? new byte[3] { 3, 0, 0 }, start, length ?? buffer.Length);
                    client.SAdapter?.Connection?.SendAsync(arg);
                }
            }
            catch
            {
                Logs.Info($"Failed to send data to server: {client.Name}");
            }
        }
        public static bool SendDataToClient(this ClientData client, Packet packet, bool serializerAsClient = false)
        {
            if (packet is null)
                throw new ArgumentNullException(nameof(packet));
            if (packet is WorldDataPacket world && (client.Player.TileX > world.MaxTileX || client.Player.TileY > world.MaxTileY))
                client.TP(client.SpawnY, client.SpawnY); //防止玩家超出地图游戏崩溃
            return client.SendDataToClient(packet.Serialize(serializerAsClient), 0);
        }
        public static void SendDataToGameServer(this ClientData client, Packet packet, bool serializerAsClient = false)
        {
            if (packet is null)
                throw new ArgumentNullException(nameof(packet));
            client.SendDataToGameServer(packet.Serialize(serializerAsClient), 0);
        }
        public static void Disconnect(this ClientData client, string reason = null)
        {

            Logs.Text($"[{client.Name}] disconnected. {reason}");
            if(client.State == ClientData.ClientState.NewConnection)
                Data.Clients.Where(c => c.Server is null && c != client).ForEach(c => c.SendMessage($"{client.Name} has leave."));
            if (client.CAdapter?.Connection is { Connected: true } && !client.Disposed)
                client.SendDataToClient(new KickPacket() { Reason = new(reason ?? "Unknown", Terraria.Localization.NetworkText.Mode.Literal) });
            client.Dispose();
        }

        public static void SendMessage(this ClientData client, string text, Color color, bool withPrefix = true)
        {
            if (client is null)
                Console.WriteLine(text);
            else
                using (var writer = new BinaryWriter(new MemoryStream()))
                {
                    client.SendDataToClient(new Delphinus.NetModules.NetTextModule()
                    {
                        fromClient = false,
                        Command = "Say",
                        NetworkText = new($"{(withPrefix ? $"<[c/B1DAE4:{Data.MessagePrefix}]> " : "")}{text}", Terraria.Localization.NetworkText.Mode.Literal),
                        Text = $"{(withPrefix ? $"<[c/B1DAE4:{Data.MessagePrefix}]> " : "")}{text}",
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
        public static void Broadcast(this ClientData client, string message, bool ruleOutSelf = true) => Data.Clients.Where(c => !ruleOutSelf || (c != client && c.Server != client?.Server)).ForEach(c => c.SendMessage(message));
        public static void ReadVersion(this ClientData client, ClientHelloPacket hello)
        {
            client.Player.VersionNum = hello.Version.StartsWith("Terraria") && int.TryParse(hello.Version[8..], out var v)
                            ? v
                            : Config.Instance.DefaultServerInternal.VersionNum;
            Logs.Info($"Version num of {client.Name} is {client.Player.VersionNum}.");
        }
        public static void TP(this ClientData client, int tileX, int tileY)
        {
            client.SendDataToClient(new TeleportPacket() { PlayerSlot = client.Player.Index, Position = new(tileX * 16, tileY * 16) });
        }
        public static void AddBuff(this ClientData client, int buffID, int time = 60)
        {
            client?.SendDataToClient(new AddPlayerBuffPacket() { BuffTime = time, BuffType = (ushort)buffID, OtherPlayerSlot = client.Player.Index });
        }
        #endregion
        #region 其他

        #endregion
    }
}
