using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using MultiSEngine.Core.Adapter;

namespace MultiSEngine.Modules.DataStruct
{
    public class ClientData
    {
        public enum ClientState
        {
            Disconnect,
            NewConnection,
            ReadyToSwitch,
            Switching,
            RequestPassword,
            FinishSendInventory,
            InGame,
        }
        public ClientData(Socket connection)
        {
            if (connection is null)
                throw new ArgumentNullException(nameof(connection));
            ClientConnection = connection;
            IP = (connection.RemoteEndPoint as IPEndPoint)?.Address.ToString();
            Port = (connection.RemoteEndPoint as IPEndPoint)?.Port ?? -1;
        }
        public ServerAdapter SAdapter { get; set; }
        public ClientAdapter CAdapter { get; set; }
        public ClientState State { get; set; } = ClientState.NewConnection;
        public string IP { get; set; }
        public int Port { get; set; }
        public string Address => $"{IP}:{Port}";
        /// <summary>
        /// 玩家连接与此服务器之间的连接
        /// </summary>
        public Socket ClientConnection { get; set; }
        /// <summary>
        /// 服务器与游戏服务器之间的连接
        /// </summary>
        public TcpClient GameServerConnection { get; set; }
        public ServerInfo Server { get; set; }
        public string Name => Player.Name ?? Address;
        public MSEPlayer Player { get; set; } = new();

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
