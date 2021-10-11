using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.Core;
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
        public static void Join(this ClientInfo client, ServerInfo server)
        {
            Logs.Info($"Switching {client.Name} to the server: {server.Name}");
            client.State = ClientInfo.ClientState.ReadyToSwitch;
            if (Utils.TryParseAddress(server.IP, out var ip))
            {
                client.GameServerConnection?.Close();
                try
                {
                    client.GameServerConnection = new();
                    client.GameServerConnection.Connect(server.IP, server.Port);
                    client.Server = server;
                    client.State = ClientInfo.ClientState.Switching;

                    Task.Run(() => RecieveLoop(client));

                    client.SendDataToGameServer(new ClientHello()
                    {
                        Version = $"Terraria{(server.VersionNum is { } and > 0 and < 65565 ? server.VersionNum : client.Player.VersionNum)}"
                    });  //发起连接请求
                }
                catch (Exception ex)
                {
                    client.State = ClientInfo.ClientState.Disconnect;
                    Logs.Error($"Unable to connect to server {server.IP}:{server.Port}{Environment.NewLine}{ex}");
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
        public static void Back(this ClientInfo client) => client.Join(Config.Instance.MainServer);
        private static void RecieveLoop(ClientInfo client)
        {
            byte[] buffer = new byte[131070];
            while (true)
            {
                try
                {
                    CheckBuffer(client, client.GameServerConnection?.Client?.Receive(buffer) ?? -1, buffer);
                    Array.Clear(buffer, 0, buffer.Length);
                }
                catch (Exception ex)
                {
                    client.Dispose();
                    Logs.Error($"Game server connection abnormally terminated.\r\n{ex}");
                    break;
                }
            }
        }
        private static void CheckBuffer(ClientInfo client, int size, byte[] buffer)
        {
            try
            {
                if (size <= 0)
                    return;
                var length = BitConverter.ToUInt16(buffer, 0);
                if (size > length)
                {
                    var position = 0;
                    while (position < size)
                    {
                        var tempLength = BitConverter.ToUInt16(buffer, position);
                        if (tempLength <= 0)
                            break;
                        if (DeserilizeGameServerPacket(client, buffer, position, tempLength))
                            client.SendDataToClient(buffer, position, tempLength);
                        position += tempLength;
                    }
                }
                else if (DeserilizeGameServerPacket(client, buffer, 0, size))
                    client.SendDataToClient(buffer, 0, size);
            }
            catch { }
        }
        /// <summary>
        /// 返回从服务器接收到的数据是否发送给玩家
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        private static bool DeserilizeGameServerPacket(ClientInfo client, byte[] buffer, int startIndex, int length)
        {
            try
            {
                if (buffer[startIndex + 2] is 2 or 3 or 7 or 37)
                    using (var reader = new BinaryReader(new MemoryStream(buffer, startIndex, length)))
                    {
                        var packet = Net.Instance.Serializer.Deserialize(reader);
                        switch (packet)
                        {
                            case Kick kick:
                                client.State = ClientInfo.ClientState.Disconnect;
                                Logs.Info($"Player {client.Player.Name} is removed from server {client.Server.Name}, for the following reason:{kick.Reason}");
                                client.SendErrorMessage(string.Format(Localization.Get("Prompt_Disconnect"), client.Server.Name, kick.Reason));
                                client.Back();
                                return false;
                            case LoadPlayer slot:
                                client.Player.Index = slot.PlayerSlot;
                                return true;
                            case WorldData worldData:
                                client.Player.SpawnX = BitConverter.ToInt16(buffer, startIndex + 13);
                                client.Player.SpawnY = BitConverter.ToInt16(buffer, startIndex + 15);
                                if (client.State < ClientInfo.ClientState.InGame)
                                {
                                    client.SendDataToGameServer(new RequestTileData() { Position = new() { X = -1, Y = -1 } });
                                    client.SendDataToGameServer(new SpawnPlayer()
                                    {
                                        PlayerSlot = client.Player.Index,
                                        Position = Utils.Point(client.Server.SpawnX, client.Server.SpawnY),
                                        Context = PlayerSpawnContext.SpawningIntoWorld
                                    });
                                    if (client.Server.SpawnX == -1 || client.Server.SpawnY == -1)
                                        client.SendDataToClient(new Teleport()
                                        {
                                            PlayerSlot = client.Player.Index,
                                            Position = new(client.Player.SpawnX, client.Player.SpawnY),
                                            Style = 1
                                        });
                                    else
                                        client.SendDataToClient(new Teleport()
                                        {
                                            PlayerSlot = client.Player.Index,
                                            Position = new(client.Server.SpawnX, client.Server.SpawnY),
                                            Style = 1
                                        });
                                    client.State = ClientInfo.ClientState.InGame;
                                    Logs.Success($"Player {client.Name} successfully joined the server: {client.Server.Name}");
                                }
                                return true;
                            case RequestPassword requestPassword:
                                client.State = ClientInfo.ClientState.RequestingPassword;
                                client.SendErrorMessage(string.Format(Localization.Get("Prompt_NeedPassword"), client.Server.Name, Localization.Get("Help_Password")));
                                return false;
                            default:
                                return true;
                        }
                    }
                else
                    return true;
            }
            catch (Exception ex)
            {
                Logs.Error($"Deserilize game server packet error: {ex}");
                return false;
            }
        }
    }
    public static partial class ClientHelper
    {
        public static void SendDataToClient(this ClientInfo client, byte[] buffer, int? index = null, int? length = null)
        {
            try
            {
                client.ClientConnection?.Send(buffer, index ?? 0, length ?? buffer.Length, SocketFlags.None);
            }
            catch(Exception ex)
            {
                if(client.State != ClientInfo.ClientState.Disconnect)
                {
                    Logs.Info($"Disconnected from {client.Player.Name ?? client.Address}{Environment.NewLine}{ex}");
                    client.Dispose();
                }
            }
        }
        public static void SendDataToGameServer(this ClientInfo client, byte[] buffer, int? index = null, int? length = null)
        {
            try
            {
                client.GameServerConnection?.Client?.Send(buffer, index ?? 0, length ?? buffer.Length, SocketFlags.None);
            }
            catch
            {
                Logs.Info($"Failed to send data to server: {client.Server?.IP}:{client.Server?.Port}");
                client.Back();
            }
        }
        public static void SendDataToClient<T>(this ClientInfo client, T data) where T : Packet => client.SendDataToClient(data?.Serilize());
        public static void SendDataToGameServer<T>(this ClientInfo client, T data) where T : Packet => client.SendDataToGameServer(data?.Serilize());
        public static void Disconnect(this ClientInfo client, string reason = "unknown")
        {
            client.SendDataToClient(Net.Instance.Serializer.Serialize(new Kick() { Reason = new(reason, NetworkText.Mode.Literal) }));
            client.Dispose();
        }

        public static void SendMessage(this ClientInfo client, string text, Color color)
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
        public static void SendInfoMessage(this ClientInfo client, string text) => SendMessage(client, text, new());
        public static void SendSuccessMessage(this ClientInfo client, string text)
        {

        }
        public static void SendErrorMessage(this ClientInfo client, string text)
        {

        }
    }
}
