using System;
using System.IO;
using System.Net.Sockets;
using Delphinus;
using Delphinus.Packets;
using Microsoft.Xna.Framework;
using MultiSEngine.Core;
using MultiSEngine.Core.Adapter;
using MultiSEngine.Modules.DataStruct;
using Terraria.Localization;

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
            if (client.Server == server || (client.State > ClientData.ClientState.ReadyToSwitch && client.State < ClientData.ClientState.InGame))
            {
                Logs.Warn($"Unallowed transmission requests for {client.Name}");
                return;
            }
            Logs.Info($"Switching {client.Name} to the server: {server.Name}");
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
                        fwa.ChangeStatusToNormal();  //切换至正常的客户端处理

                    var sa = new VisualPlayerAdapter(client, client.TempConnection);
                    sa.TryConnect(server, (adapter, client) =>
                    {
                        //Logs.Info($"Visual player: {client.Name} connect success.");
                        client.SendDataToClient(client.Player.OriginData.Info);
                        client.State = ClientData.ClientState.InGame;
                        client.SAdapter?.Stop(true);
                        client.SAdapter = adapter;
                        client.Server = server;
                        client.TempConnection = null;
                        client.TimeOutTimer.Stop();
                        (client.SAdapter as VisualPlayerAdapter)?.SyncPlayer();
                    });
                }
                catch (Exception ex)
                {
                    client.State = ClientData.ClientState.ReadyToSwitch;
                    Logs.Error($"Unable to connect to server {server.IP}:{server.Port}{Environment.NewLine}{ex}");
                    //client.SendErrorMessage(string.Format(Localization.Get("Prompt_CannotConnect"), server.Name));
                    client.SendErrorMessage("Prompt_CannotConnect");
                }
            }
            else
                client.SendErrorMessage(Localization.Get("Prompt_UnknownAddress"));
        }
        /// <summary>
        /// 返回到默认的初始服务器
        /// </summary>
        /// <param name="client"></param>
        public static void Back(this ClientData client) => client.Join(Config.Instance.MainServer);
    }
    public static partial class ClientHelper
    {
        #region 消息函数
        public static bool SendDataToClient(this ClientData client, byte[] buffer, int? index = null, int? length = null)
        {
            try
            {
                client.CAdapter?.Connection?.Send(buffer ?? new byte[3], index ?? 0, length ?? buffer?.Length ?? 3, SocketFlags.None);
                return true;
            }
            catch (Exception ex)
            {
                Logs.Warn($"Failed to send data to {client.Name}{Environment.NewLine}{ex}");
                return false;
            }
        }
        public static void SendDataToGameServer(this ClientData client, byte[] buffer, int? index = null, int? length = null)
        {
            try
            {
                buffer ??= new byte[3];
                index ??= 0;
                length ??= buffer?.Length ?? 3;
                client.SAdapter?.Connection?.Send(buffer, (int)index, (int)length, SocketFlags.None);
            }
            catch
            {
                Logs.Info($"Failed to send data to server: {client.Name}");
            }
        }
        public static bool SendDataToClient(this ClientData client, Packet data) => client.SendDataToClient(Net.Instance.ServerSerializer?.Serialize(data ?? new EmojiPacket()));
        public static void SendDataToGameServer(this ClientData client, Packet data) => client.SendDataToGameServer(Net.Instance.ClientSerializer?.Serialize(data));
        public static void Disconnect(this ClientData client, string reason = "unknown")
        {
            client.SendDataToClient(Net.Instance.ServerSerializer.Serialize(new KickPacket() { Reason = new(reason, NetworkText.Mode.Literal) }));
            client.Dispose();
        }

        public static void SendMessage(this ClientData client, string text, Color color, bool withPrefix = true)
        {
            using (var writer = new BinaryWriter(new MemoryStream()))
            {
                client.SendDataToClient(new Delphinus.NetModules.NetTextModule()
                {
                    fromClient = false,
                    Command = "Say",
                    NetworkText = new($"{(withPrefix ? $"<[c/B1DAE4:{Data.MessagePrefix}]> " : "")}{text}", NetworkText.Mode.Literal),
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
        public static void ReadVersion(this ClientData client, ClientHelloPacket hello)
        {
            client.Player.VersionNum = hello.Version.StartsWith("Terraria") && int.TryParse(hello.Version[8..], out var v)
                            ? v
                            : Config.Instance.MainServer.VersionNum;
            Logs.Info($"Version num of player {client.Name} is {client.Player.VersionNum}.");
        }
        public static void TP(this ClientData client, int tileX, int tileY)
        {
            client.SendDataToClient(new TeleportPacket() { PlayerSlot = client.Player.Index, Position = new(tileX * 16, tileY * 16) });
        }
        public static void AddBuff(this ClientData client, int buffID, int time = 60)
        {
            client.SendDataToClient(new AddPlayerBuffPacket() { BuffTime = time, BuffType = (ushort)buffID, OtherPlayerSlot = client.Player.Index });
        }
        #endregion
        #region 其他

        #endregion
    }
}
