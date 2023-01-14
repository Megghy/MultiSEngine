using System;
using System.Collections.Generic;
using System.IO;
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
        public BaseAdapter(ClientData client, Net.NetSession clientConnection, Net.NetClient serverConnection = null)
        {
            Client = client;
            ClientConnection = clientConnection;
            ServerConnection = serverConnection;
            RegisteHandlers();
        }
        #region 变量
        public int ErrorCount { get; protected set; } = 0;
        public bool IsDisposed { get; private set; } = false;
        public ClientData Client { get; init; }
        protected List<BaseHandler> _handlers { get; set; } = new();

        internal Net.NetSession ClientConnection { get; init; }
        internal Net.NetClient ServerConnection { get; set; }
        #endregion
        protected virtual void RegisteHandlers()
        {
            if (Client?.State <= ClientState.NewConnection)
                RegisteHander<ConnectionRequestHandler>();
            RegisteHander<CommonHandler>();
            RegisteHander<CustomPacketHandler>();
            RegisteHander<PlayerInfoHandler>();
            RegisteHander<ChatHandler>();
        }
        public virtual void Stop(bool disposeConnection = false)
        {
            if (IsDisposed)
                return;
            IsDisposed = true;
            foreach (var handler in _handlers)
            {
                handler.Dispose();
            }
            _handlers.Clear();
            if (disposeConnection)
            {
                ClientConnection?.Disconnect();
                ServerConnection?.Disconnect();
            }

#if DEBUG
            Logs.Warn($"[{GetType()}] Stopped");
#endif
        }
        internal void ServerBufCheckedCallback(ref Span<byte> buf)
        {
            if (Client?.Adapter.ClientConnection is null)
                SendToClientDirect(ref buf);
            else
                Client.SendDataToClient(ref buf);
        }
        internal void ClientBufCheckedCallback(ref Span<byte> buf)
        {
            if (Client?.Adapter.ServerConnection is null)
                SendToServerDirect(ref buf);
            else
                Client.SendDataToServer(ref buf);
        }

        public bool RecieveClientData(ref Span<byte> buf)
        {
#if DEBUG
            Console.WriteLine($"[Recieve CLIENT] {(MessageID)buf[2]}");
#endif
            try
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i].RecieveClientData((MessageID)buf[2], ref buf))
                        return true;
                }
                return false;
            }
#if DEBUG
            catch (IOException io)
            {
                Console.WriteLine(io);
            }
#endif
            catch (Exception ex)
            {
                Logs.Error($"An error occurred while processing packet {(MessageID)buf[2]}.{Environment.NewLine}{ex}");
            }
            return false;
        }
        public bool RecieveServerData(ref Span<byte> buf)
        {
#if DEBUG
            Console.WriteLine($"[Recieve SERVER] {(MessageID)buf[2]}");
#endif
            try
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i].RecieveServerData((MessageID)buf[2], ref buf))
                        return true;
                }
                return false;
            }
#if DEBUG
            catch (IOException io)
            {
                Console.WriteLine(io);
            }
#endif
            catch (Exception ex)
            {
                Logs.Error($"An error occurred while processing packet {(MessageID)buf[2]}.{Environment.NewLine}{ex}");
            }
            return false;
        }
        private readonly byte[] _fixdCheckedBuf = new byte[131070];

        public delegate bool BufferCheckRefStructMutator(ref Span<byte> s);
        public delegate void BufferCallbackRefStructMutator(ref Span<byte> s);

        public void CheckBuffer(ref Span<byte> buf, BufferCheckRefStructMutator check, BufferCallbackRefStructMutator callback)
        {
            if (buf.IsEmpty)
            {
                Logs.Warn($"Receive a packet of length 0");
                return;
            }
            try
            {
                var length = BitConverter.ToUInt16(buf);
                if (buf.Length > length)
                {
                    var checkedPos = 0;
                    Span<byte> _checkedBuf = new(_fixdCheckedBuf);
                    var tempPos = 0;
                    while (tempPos < buf.Length)
                    {
                        var tempLen = BitConverter.ToUInt16(buf.Slice(tempPos, 2));
                        if (tempLen == 0)
                            break;
                        var checkData = buf.Slice(tempPos, tempLen);
                        if (!checkData.IsEmpty && !check(ref checkData))
                        {
                            buf.Slice(tempPos, tempLen).CopyTo(_checkedBuf.Slice(checkedPos, tempLen));
                            checkedPos += tempLen;
                        }
                        tempPos += tempLen;
                    }
                    var data = _checkedBuf[..checkedPos];
                    if (checkedPos > 0)
                    {
                        callback(ref data);
                    }
                    _checkedBuf.Clear();
                }
                else
                {
                    if (!check(ref buf))
                        callback(ref buf);
                }
            }
            catch(Exception ex)
            {
                callback(ref buf);
                Logs.Warn($"An error occurred while checking buffer. Length: {buf.Length}.{Environment.NewLine}{ex}");
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

        public bool SendToClientDirect(ref Span<byte> buf)
        {
#if DEBUG
            Console.WriteLine($"[Internal Send TO CLIENT] {(MessageID)buf[2]}");
#endif
            try
            {
                return ClientConnection?.SendAsync(buf) ?? false;
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to send data: {(MessageID)buf[2]}{Environment.NewLine}{ex}");
                return false;
            }
        }
        public bool SendToServerDirect(ref Span<byte> buf)
        {
#if DEBUG
            Console.WriteLine($"[Internal Send TO SERVER] {(MessageID)buf[2]}");
#endif
            try
            {
                return ServerConnection?.SendAsync(buf) ?? false;
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to send data: {(MessageID)buf[2]}{Environment.NewLine}{ex}");
                return false;
            }
        }
        public bool SendToClientDirect(Packet packet)
        {
            var buf = packet.AsBytes().AsSpan();
            return SendToClientDirect(ref buf);
        }
        public bool SendToServerDirect(Packet packet)
        {
            var buf = packet.AsBytes().AsSpan();
            return SendToServerDirect(ref buf);
        }
    }
}
