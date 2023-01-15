using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;

namespace MultiSEngine.Core.Adapter
{
    public interface IStatusChangeable
    {
        public bool RunningAsNormal { get; set; }
        public void ChangeProcessState(bool asNormal);
    }
    public class BaseAdapter
    {
        public BaseAdapter(ClientData client, TcpClient clientConnection, TcpClient serverConnection = null)
        {
            Init(client, new(clientConnection), serverConnection is null ? null : new(serverConnection));
        }
        public BaseAdapter(ClientData client, TcpContainer clientConnection, TcpContainer serverConnection = null)
        {
            Init(client, clientConnection, serverConnection);
        }
        private void Init(ClientData client, TcpContainer clientConnection, TcpContainer serverConnection = null)
        {
            Client = client;
            if (clientConnection is not null)
            {
                ClientConnection = clientConnection;
                ClientConnection.OnException += OnClientException;
            }
            if (serverConnection is not null)
            {
                SetServerConnection(serverConnection);
            }
            RegisteHandlers();
            Task.Run(RecieveServerLoop);
            Task.Run(RecieveClientLoop);
        }
        #region 变量
        public int ErrorCount { get; protected set; } = 0;
        public bool IsDisposed { get; private set; } = false;
        public ClientData Client { get; private set; }
        protected List<BaseHandler> _handlers { get; set; } = new();

        internal TcpContainer ClientConnection { get; private set; }
        internal TcpContainer ServerConnection { get; private set; }

        #endregion
        protected virtual void RegisteHandlers()
        {
            if (Client?.State <= ClientState.NewConnection)
                RegisteHander<AcceptConnectionHandler>();
            RegisteHander<CommonHandler>();
            RegisteHander<CustomPacketHandler>();
            RegisteHander<PlayerInfoHandler>();
            RegisteHander<ChatHandler>();
        }
        public void SetServerConnection(TcpContainer serverConnection, bool disposeOld = false)
        {
            if (disposeOld)
                ServerConnection?.Dispose(true);
            ServerConnection = serverConnection;
            ServerConnection.OnException += OnServerException;
        }
        public virtual void Dispose(bool disposeConnection = false)
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            foreach (var handler in _handlers)
            {
                handler.Dispose();
            }
            _handlers.Clear();
            if (ClientConnection is not null)
                ClientConnection.OnException -= OnClientException;
            if (ServerConnection is not null)
                ServerConnection.OnException -= OnServerException;
            if (disposeConnection)
            {
                ClientConnection?.Dispose(true);
                ServerConnection?.Dispose(true);
            }
#if DEBUG
            Logs.Warn($"[{GetType()}] Stopped");
#endif
        }
        private void RecieveServerLoop()
        {
            while (!IsDisposed)
            {
                if (ServerConnection is { IsDisposed: false })
                {
                    var buf = ServerConnection.Get();
                    if (buf is null)
                        continue;
#if DEBUG
                    Console.WriteLine($"[Recieve SERVER] {(MessageID)buf[2]}");
#endif
                    try
                    {
                        for (int i = _handlers.Count - 1; i >= 0; i--)
                        {
                            if (_handlers[i].RecieveServerData((MessageID)buf[2], buf))
                                goto Ignore;
                        }
                        if (Client?.Adapter.ClientConnection is null)
                            SendToClientDirect(buf);
                        else
                            Client.SendDataToClient(buf);
                        Ignore:;
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"An error occurred while processing SERVER packet {(MessageID)buf[2]}.{Environment.NewLine}{ex}");
                    }
                }
                else
                    Task.Delay(1).Wait();
            }
        }
        private void RecieveClientLoop()
        {
            while (!IsDisposed)
            {
                if (ClientConnection is { IsDisposed: false })
                {
                    var buf = ClientConnection.Get();
                    if (buf is null)
                        continue;
#if DEBUG
                    Console.WriteLine($"[Recieve CLIENT] {(MessageID)buf[2]}");
#endif
                    try
                    {
                        for (int i = _handlers.Count - 1; i >= 0; i--)
                        {
                            if (_handlers[i].RecieveClientData((MessageID)buf[2], buf))
                                goto Ignore;
                        }
                        if (Client?.Adapter.ServerConnection is null)
                            SendToServerDirect(buf);
                        else
                            Client.SendDataToServer(buf);
                        Ignore:;
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"An error occurred while processing CLIENT packet {(MessageID)buf[2]}.{Environment.NewLine}{ex}");
                    }
                }
                else
                    Task.Delay(1).Wait();
            }
        }

        private int _errorCount = 0;
        public virtual void OnClientException(Exception ex)
        {
            Client?.Disconnect();
        }
        public virtual void OnServerException(Exception ex)
        {
            Logs.Error($"Server connection error: {ex}");
            if (Client.State == ClientState.InGame)
            {
                ServerConnection?.Dispose(true);
                Client.Back();
            }
        }


        public void RegisteHander(BaseHandler handler)
        {
            handler.Initialize();
            _handlers.Add(handler);
        }
        public void RegisteHander<T>() where T : BaseHandler
        {
            var handler = Activator.CreateInstance(typeof(T), new object[] { this }) as BaseHandler;
            RegisteHander(handler);
        }
        public bool DeregisteHander<T>(T handler) where T : BaseHandler
        {
            handler?.Dispose();
            return _handlers.Remove(handler);
        }

        #region Packet Send

        public bool SendToClientDirect(byte[] data)
        {
            var buf = data.AsSpan();
            return SendToClientDirect(ref buf);
        }
        public bool SendToServerDirect(byte[] data)
        {
            var buf = data.AsSpan();
            return SendToServerDirect(ref buf);
        }
        public bool SendToClientDirect(ref Span<byte> buf)
        {
#if DEBUG
            Console.WriteLine($"[Internal Send TO CLIENT] {(MessageID)buf[2]}");
#endif
            return ClientConnection?.Send(ref buf) ?? false;
        }
        public bool SendToServerDirect(ref Span<byte> buf)
        {
#if DEBUG
            Console.WriteLine($"[Internal Send TO SERVER] {(MessageID)buf[2]}");
#endif
            return ServerConnection?.Send(ref buf) ?? false;
        }
        public bool SendToClientDirect(Packet packet)
        {
            return SendToClientDirect(packet.AsBytes());
        }
        public bool SendToServerDirect(Packet packet)
        {
            return SendToServerDirect(packet.AsBytes());
        }
        #endregion
    }
}
