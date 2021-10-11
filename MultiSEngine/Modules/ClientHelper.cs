using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
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
            Logs.Info($"Switching {client.Name} to the server: {server.Name}");
            client.State = ClientData.ClientState.ReadyToSwitch;
            if (Utils.TryParseAddress(server.IP, out var ip))
            {
                client.GameServerConnection?.Close();
                try
                {
                    client.GameServerConnection = new();
                    client.GameServerConnection.Connect(server.IP, server.Port);
                    client.Server = server;
                    client.State = ClientData.ClientState.Switching;

                    client.RunningAdapter.Add(new ServerAdapter(client, client.GameServerConnection.Client).Start());

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
        public static void SendDataToClient(this ClientData client, byte[] buffer, int? index = null, int? length = null)
        {
            try
            {
                client.ClientConnection?.Send(buffer, index ?? 0, length ?? buffer.Length, SocketFlags.None);
            }
            catch(Exception ex)
            {
                if(client.State != ClientData.ClientState.Disconnect)
                {
                    Logs.Info($"Disconnected from {client.Player.Name ?? client.Address}{Environment.NewLine}{ex}");
                    client.Dispose();
                }
            }
        }
        public static void SendDataToGameServer(this ClientData client, byte[] buffer, int? index = null, int? length = null)
        {
            try
            {
                client.GameServerConnection?.Client?.Send(buffer ?? new byte[3], index ?? 0, length ?? buffer?.Length ?? 3, SocketFlags.None);
            }
            catch
            {
                Logs.Info($"Failed to send data to server: {client.Server?.IP}:{client.Server?.Port}");
                client.Back();
            }
        }
        public static void SendDataToClient(this ClientData client, Packet data) => client.SendDataToClient(data?.Serilize());
        public static void SendDataToGameServer(this ClientData client, Packet data) => client.SendDataToGameServer(data?.Serilize());
        public static void Disconnect(this ClientData client, string reason = "unknown")
        {
            client.SendDataToClient(Net.Instance.ServerSerializer.Serialize(new Kick() { Reason = new(reason, NetworkText.Mode.Literal) }));
            client.Dispose();
        }

        public static void SendMessage(this ClientData client, string text, Color color)
        {
            using (var writer = new BinaryWriter(new MemoryStream()))
            {
                client.SendDataToClient(new TrProtocol.Packets.Modules.NetTextModuleS2C()
                {
                    Text = new NetworkText(text, NetworkText.Mode.Literal),
                    Color = color
                }.Serilize());
            }
        }
        public static void SendInfoMessage(this ClientData client, string text) => SendMessage(client, text, new());
        public static void SendSuccessMessage(this ClientData client, string text)
        {

        }
        public static void SendErrorMessage(this ClientData client, string text)
        {

        }
    }
}
