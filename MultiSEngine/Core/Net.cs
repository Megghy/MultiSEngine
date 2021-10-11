using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.Modules;
using MultiSEngine.Modules.DataStruct;
using TrProtocol;
using TrProtocol.Packets;

namespace MultiSEngine.Core
{
    public partial class Net
    {
        public static Net Instance { get; internal set; } = new();
        public Socket SocketServer { get; internal set; }
        internal PacketSerializer Serializer { get; set; } = new PacketSerializer(true);
        public Net Init(string ip = "127.0.0.1", int port = 7778)
        {
            try
            {
                SocketServer?.Dispose();
                SocketServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPAddress address = IPAddress.Parse(ip);
                IPEndPoint point = new(address, port);
                SocketServer.Bind(point);
                SocketServer.Listen(50);

                Task.Run(WatchConnecting);
            }
            catch (Exception ex)
            {
                Logs.Error(ex);
                Console.ReadLine();
            }
            return this;
        }
        public void WatchConnecting()
        {
            Socket connection = null;

            //持续不断监听客户端发来的请求   
            while (true)
            {
                try
                {
                    connection = SocketServer.Accept();
                }
                catch (Exception ex)
                {
                    Logs.Error(ex);
                    break;
                }
                var client = new ClientInfo(connection);
                Data.Clients.Add(client);
                //显示与客户端连接情况
                Logs.Info($"{connection.RemoteEndPoint} trying to connect...");
                Task.Run(() => RecieveLoop(client));
                Task.Run(() => CheckAlive(client));
            }
        }
    }
    /// <summary>
    /// 接收数据的处理模块
    /// </summary>
    public partial class Net
    {
        public void CheckAlive(ClientInfo client)
        {
            while (client.ClientConnection is { Connected: true })
            {
                try
                {
                    client.SendDataToClient(new byte[1]);
                    Task.Delay(1000).Wait();
                }
                catch
                {
                    client.Dispose();
                }
            }
        }
        public void RecieveLoop(ClientInfo client)
        {
            byte[] buffer = new byte[131070];
            while (client.ClientConnection is { Connected: true })
            {
                try
                {
                    CheckBuffer(client, client.ClientConnection.Receive(buffer), buffer);
                }
                catch (Exception ex)
                {
                    client.Dispose();
                    Logs.Error($"Client connection abnormally terminated.{Environment.NewLine}{ex}");
                    return;
                }
            }
            Logs.Text($"{client.Player.Name ?? $"{client.IP}:{client.Port}"} disconnect.");
        }
        private void CheckBuffer(ClientInfo client, int size, byte[] buffer)
        {
            try
            {
                if (size <= 0)
                    return;
                var length = BitConverter.ToUInt16(buffer, 0);
                Console.WriteLine($"recieve from client{length} [{buffer[2]}]");
                if (size > length)
                {
                    var position = 0;
                    while (position < size)
                    {
                        var tempLength = BitConverter.ToUInt16(buffer, position);
                        if (tempLength == 0)
                            break;
                        if (!ProcessClientPacket(client, buffer, position, tempLength))
                            Array.Clear(buffer, position, tempLength);
                        position += tempLength;
                    }
                }
                else if (!ProcessClientPacket(client, buffer, 0, size))
                    return;
                client.SendDataToGameServer(buffer);
            }
            catch { }
        }
        /// <summary>
        /// 返回从玩家接收到的数据是否发送给服务器
        /// </summary>
        /// <param name="tempBuffer"></param>
        /// <param name="startIndex"></param>
        /// <param name="length"></param>
        private bool ProcessClientPacket(ClientInfo client, byte[] tempBuffer, int startIndex, int length)
        {
            try
            {
                if (tempBuffer[startIndex + 2] == 1)
                    using (var reader = new BinaryReader(new MemoryStream(tempBuffer, startIndex, length)))
                    {
                        var hello = Serializer.Deserialize(reader) as ClientHello;
                        if (client.State is ClientInfo.ClientState.NewConnection) //首次连接时默认进入主服务器
                        {
                            if (Config.Instance.MainServer is { })
                            {
                                client.Player.VersionNum = hello.Version.StartsWith("Terraria") && int.TryParse(hello.Version[8..], out var v)
                                ? v
                                : Config.Instance.MainServer.VersionNum;
                                Logs.Info($"Version num of player {client.IP}:{client.Port} is {client.Player.VersionNum}.");
                            }
                            else
                                client.Disconnect("No default server is set for the current server.");
                        }
                        return false;
                    }
                else
                    return true;
            }
            catch (Exception ex)
            {
                Logs.Error($"Deserilize client packet error: {ex}");
                return false;
            }
        }
    }
}
