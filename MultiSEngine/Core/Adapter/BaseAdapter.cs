using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public BaseAdapter(ClientData client, Net.NetSession clientConnection, Net.NetClient serverConnection = null)
        {
            Client = client;
            ClientConnection = clientConnection;
            ServerConnection = serverConnection;
            RegisteHandlers();
            Task.Run(CheckBufferLoop);
        }
        #region 变量
        public int ErrorCount { get; protected set; } = 0;
        public bool IsDisposed { get; private set; } = false;
        public ClientData Client { get; init; }
        protected List<BaseHandler> _handlers { get; set; } = new();
        private ConcurrentQueue<(byte[] data, BufferCheckRefStructMutator check, BufferCallbackRefStructMutator callback)> _dataQueue = new();

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
            _dataQueue.Clear();
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
        private readonly byte[] _fixedCheckedBuf = new byte[1024 * 1024];
        private readonly byte[] _fixedUncheckedBuf = new byte[10 * 1024 * 1024];
        private int _uncheckedLength = 0;
        private int _uncheckedOffset = 0;

        public delegate bool BufferCheckRefStructMutator(ref Span<byte> s);
        public delegate void BufferCallbackRefStructMutator(ref Span<byte> s);

        public void CheckBuffer(byte[] buf, BufferCheckRefStructMutator check, BufferCallbackRefStructMutator callback)
        {
            if (buf.Length == 0)
            {
                Logs.Warn($"Receive a packet of length 0");
                return;
            }
            _dataQueue.Enqueue((buf, check, callback));
        }
        private void CheckBufferLoop()
        {
            while (!IsDisposed)
            {
                if (_dataQueue.TryDequeue(out var result))
                {
                    var buf = result.data.AsSpan();
                    try
                    {
                        if (BitConverter.ToInt16(buf) == buf.Length)
                        {
                            try
                            {
                                if (!result.check(ref buf))
                                    result.callback(ref buf);
                            }
                            catch(Exception ex)
                            {
                                result.callback(ref buf);
                                Logs.Warn($"An error occurred while checking buffer. Packet: {(MessageID)buf[2]} [Header Length: {BitConverter.ToInt16(buf)}, Absolute Length: {buf.Length}]{Environment.NewLine}{ex}");
                            }
                        }
                        else
                        {
                            result.data.CopyTo(_fixedUncheckedBuf, _uncheckedLength);
                            _uncheckedLength += buf.Length;
                            var uncheckedBuf = _fixedUncheckedBuf.AsSpan()[_uncheckedOffset.._uncheckedLength];
                            var checkedBuf = _fixedCheckedBuf.AsSpan();
                            var tempPos = 0;
                            var checkedPos = 0;
                            var tempLen = 0;

                            bool isFullPacket = true;
                            try
                            {
                                while (tempPos < uncheckedBuf.Length - 1)
                                {
                                    tempLen = BitConverter.ToInt16(uncheckedBuf.Slice(tempPos, 2));
                                    if (tempLen + tempPos > uncheckedBuf.Length)
                                    {
                                        isFullPacket = false;
                                        break;//包长度不够, 等待下个包
                                    }
                                    var checkData = uncheckedBuf.Slice(tempPos, tempLen);
                                    if (!result.check(ref checkData))
                                    {
                                        checkData.CopyTo(checkedBuf.Slice(checkedPos, tempLen));
                                        checkedPos += tempLen;
                                    }
                                    tempPos += tempLen;
                                }
                                var data = checkedBuf[..checkedPos];
                                if (checkedPos > 0)
                                {
                                    result.callback(ref data);
                                }
                                if (isFullPacket)
                                {
                                    uncheckedBuf.Clear();
                                    checkedBuf.Clear();
                                    _uncheckedLength = 0;
                                    _uncheckedOffset = 0;
                                }
                                else
                                {
                                    _uncheckedOffset = _uncheckedOffset += tempPos;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(string.Join(' ', uncheckedBuf[(tempPos + _uncheckedOffset)..].ToArray().Select(b => b.ToString())));
                                Logs.Warn($"An error occurred while checking Multiple Packet buffer. Buffer length: {_uncheckedLength}, Offset: {tempPos}, Packet: {(MessageID)buf[tempPos + 2]} [Header Length: {BitConverter.ToInt16(buf[tempPos..])}, Absolute Length: {buf.Length}]{Environment.NewLine}{ex}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.callback(ref buf);
                        var pos = BitConverter.ToInt16(_fixedUncheckedBuf);
                        Console.WriteLine(string.Join(' ', buf[pos..].ToArray().Select(b => b.ToString())));
                        Console.WriteLine(BitConverter.ToInt16(buf[pos..]));
                        Logs.Warn($"An error occurred while checking buffer. Buffer length: {_uncheckedLength}, Packet: {(MessageID)buf[pos + 2]} [Header Length: {BitConverter.ToInt16(buf[pos..])}, Absolute Length: {buf.Length}]{Environment.NewLine}{ex}");
                    }
                }
                else
                    Task.Delay(1).Wait();
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
