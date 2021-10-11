using System;
using System.Net;
using System.Net.Sockets;

namespace MultiSEngine.Modules.DataStruct
{
    public class ClientInfo
    {
        public enum ClientState
        {
            Disconnect,
            NewConnection,
            ReadyToSwitch,
            Switching,
            RequestingPassword,
            FinishSendInventory,
            InGame,
        }
        public ClientInfo(Socket connection)
        {
            if (connection is null)
                throw new ArgumentNullException(nameof(connection));
            ClientConnection = connection;
            IP = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString();
            Port = (connection.RemoteEndPoint as IPEndPoint)?.Port ?? -1;
        }
        public ClientState State { get; set; } = ClientState.NewConnection;
        public string IP { get; set; }
        public int Port { get; set; }
        /// <summary>
        /// 玩家连接与此服务器之间的连接
        /// </summary>
        public Socket ClientConnection { get; set; }
        /// <summary>
        /// 服务器与游戏服务器之间的连接
        /// </summary>
        public TcpClient GameServerConnection { get; set; }
        public ServerInfo Server { get; set; }
        public MSEPlayer Player { get; set; } = new();
        public void RecieveLoop()
        {
            byte[] buffer = new byte[1024 * 1024];
            while (true)
            {
                try
                {
                    int length = ClientConnection.Receive(buffer);
                }
                catch (Exception ex)
                {
                    Logs.Error($"连接已断开\r\n{ex}");
                    break;
                }
            }
        }
        public void Dispose()
        {
            Data.Clients.Remove(this);
            try
            {
                ClientConnection?.Shutdown(SocketShutdown.Both);
                GameServerConnection?.Close();
            }
            catch { }
            ClientConnection = null;
            try
            {
                if (GameServerConnection is { Connected: true })
                    GameServerConnection?.Client.Shutdown(SocketShutdown.Both);
                GameServerConnection?.Client?.Close();
                GameServerConnection?.Close();
            }
            catch { }
            GameServerConnection = null;
            State = ClientState.Disconnect;
        }
    }
}
