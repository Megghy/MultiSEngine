using System;
using System.IO;
using System.Net.Sockets;
using MultiSEngine.Core;
using MultiSEngine.Core.Adapter;
using MultiSEngine.Modules.DataStruct;
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
        public static void Join(this ClientData client, ServerInfo server)
        {
            if (client.Server == server)
                return;
            Logs.Info($"Switching {client.Name} to the server: {server.Name}");
            client.State = ClientData.ClientState.ReadyToSwitch;
            if (Utils.TryParseAddress(server.IP, out var ip))
            {
                
                client.GameServerConnection?.Close();
                try
                {
                    client.State = ClientData.ClientState.Switching;
                    var connection = new TcpClient();
                    connection.Connect(server.IP, server.Port); //新建与服务器的连接

                    client.SAdapter?.Stop(true);
                    client.GameServerConnection = connection;  
                    client.SAdapter = new ServerAdapter(client, client.GameServerConnection.Client);
                    client.SAdapter.Start();        //断开并释放旧连接, 替换为新连接
                    client.Server = server;

                    client.SendDataToGameServer(new ClientHello()
                    {
                        Version = $"Terraria{(server.VersionNum is { } and > 0 and < 65565 ? server.VersionNum : client.Player.VersionNum)}"
                    });  //发起连接请求
                }
                catch (Exception ex)
                {
                    client.State = ClientData.ClientState.Disconnect;
                    Logs.Error($"Unable to connect to server {server.IP}:{server.Port}{Environment.NewLine}{ex}");
                    if (server == Config.Instance.MainServer)
                        client.SendErrorMessage("The main server is not responding, please try to join the following other servers:");
                    else
                    {
                        client.SendErrorMessage(string.Format(Localization.Get("Prompt_CannotConnect"), server.Name));
                        client.Back();
                    }
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
                client.ClientConnection?.Send(buffer ?? new byte[3], index ?? 0, length ?? buffer?.Length ?? 3, SocketFlags.None);
                return true;
            }
            catch (Exception ex)
            {
                return false;
                //Logs.Warn($"Failed to send data to {client.Player.Name ?? client.Address}{Environment.NewLine}{ex}");
            }
        }
        public static void SendDataToGameServer(this ClientData client, byte[] buffer, int? index = null, int? length = null)
        {
            try
            {
                buffer ??= new byte[3];
                index ??= 0;
                length ??= buffer?.Length ?? 3;
                client.GameServerConnection?.Client?.Send(buffer, (int)index, (int)length, SocketFlags.None);
            }
            catch
            {
                Logs.Info($"Failed to send data to server: {client.Name}");
                client.Back();
            }
        }
        public static bool SendDataToClient(this ClientData client, Packet data) => client.SendDataToClient(Net.Instance.ServerSerializer?.Serialize(data));
        public static void SendDataToGameServer(this ClientData client, Packet data) => client.SendDataToGameServer(Net.Instance.ClientSerializer?.Serialize(data));
        public static void Disconnect(this ClientData client, string reason = "unknown")
        {
            client.SendDataToClient(Net.Instance.ServerSerializer.Serialize(new Kick() { Reason = new(reason, NetworkText.Mode.Literal) }));
            client.Dispose();
        }

        public static void SendMessage(this ClientData client, string text, Color color, bool withPrefix = true)
        {
            using (var writer = new BinaryWriter(new MemoryStream()))
            {
                client.SendDataToClient(new TrProtocol.Packets.Modules.NetTextModuleS2C()
                {
                    Text = new NetworkText($"{(withPrefix ? $"<[c/B1DAE4:{Data.MessagePrefix}]> " : "")}{text}", NetworkText.Mode.Literal),
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
        public static void ReadVersion(this ClientData client, ClientHello hello)
        {
            client.Player.VersionNum = hello.Version.StartsWith("Terraria") && int.TryParse(hello.Version[8..], out var v)
                            ? v
                            : Config.Instance.MainServer.VersionNum;
            Logs.Info($"Version num of player {client.Name} is {client.Player.VersionNum}.");
        }
        #endregion
    }
}
