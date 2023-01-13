using System;
using System.IO;
using System.Net.Sockets;
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
    public abstract class BaseAdapter
    {
        public BaseAdapter(ClientData client)
        {
            Client = client;
        }
        #region 变量
        public int ErrorCount { get; protected set; } = 0;
        protected bool ShouldStop { get; set; } = false;
        public int VersionNum => Client?.Player?.VersionNum ?? -1;
        public virtual PacketSerializer InternalClientSerializer => Net.ClientSerializer.TryGetValue(VersionNum, out var result) ? result : Net.DefaultClientSerializer;
        public virtual PacketSerializer InternalServerSerializer => Net.ServerSerializer.TryGetValue(VersionNum, out var result) ? result : Net.DefaultServerSerializer;
        public ClientData Client { get; protected set; }
        #endregion
        public abstract bool ListenningClient { get; }
        public abstract bool GetData(ref Span<byte> data);
        public abstract void SendData(ref Span<byte> data);
        public virtual void Stop(bool disposeConnection = false)
        {
            if (ShouldStop)
                return;
            ShouldStop = true;
            System.Net.EndPoint ep = default;
            if (disposeConnection)
            {
                if (this is ClientAdapter client)
                {
                    ep = client._clientConnection.Socket.RemoteEndPoint;
                    client._clientConnection?.Disconnect();
                }
                else if (this is ServerAdapter server)
                {
                    ep = server._serverConnection?.Endpoint;
                    server._serverConnection?.Disconnect();
                }
            }

#if DEBUG
            Logs.Warn($"[{GetType()}] <{ep}> Stopped");
#endif
        }
        protected virtual void OnRecieveLoopError(Exception ex)
        {
#if DEBUG
            Console.WriteLine($"[Recieve Loop Error] {ex}");
#endif
            ErrorCount++;
            switch (ex)
            {
                case EndOfStreamException:
                case IOException:
                case OutOfBoundsException:
                    //
                    break;
                default:
                    Logs.Warn($"{(ListenningClient ? "Client" : "Server")} recieve loop abnormally terminated. [{ErrorCount}]\r\n{ex}");
                    break;
            }
            if (ErrorCount > 10)
                Client.Back();
        }
        internal void BufCheckedCallback(ref Span<byte> buf)
        {
            try
            {
                if (buf.Length > 0)
                    SendData(ref buf);
            }
#if DEBUG
            catch (IOException io)
            {
                Console.WriteLine(io);
            }
#endif
            catch (Exception ex)
            {
                Logs.Error($"An error occurred while processing packet {BitConverter.ToUInt16(buf[..2])}.{Environment.NewLine}{ex}");
            }
        }
        private readonly byte[] _fixdCheckedBuf = new byte[131070];

        public delegate bool BufferCheckRefStructMutator(ref Span<byte> s);
        public delegate void BufferCallbackRefStructMutator(ref Span<byte> s);

        public bool CheckBuffer(Span<byte> buf, BufferCheckRefStructMutator check, BufferCallbackRefStructMutator callback)
        {
            if (buf.IsEmpty)
            {
                Logs.Warn($"Receive a packet of length 0");
                return true;
            }
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
            return default;
        }
        public virtual void InternalSendPacket(Packet packet, bool asClient = false)
        {
#if DEBUG
            Console.WriteLine($"[Internal Send] {packet}");
#endif
            if (packet == null)
                return;
            if (!ShouldStop)
            {
                var data = (ListenningClient ? (asClient ? InternalClientSerializer : InternalServerSerializer) : (asClient ? Net.DefaultClientSerializer : Net.DefaultServerSerializer)).Serialize(packet);
                if (this is ClientAdapter client)
                {
                    client._clientConnection?.SendAsync(data);
                }
                else if (this is ServerAdapter server)
                {
                    server._serverConnection?.SendAsync(data);
                }
            }
        }
        public virtual void InternalSendPacket(byte[] data, bool asClient = false)
        {
#if DEBUG
            Console.WriteLine($"[Internal Send] {(MessageID)data[2]}");
#endif
            if (!ShouldStop)
            {
                if (this is ClientAdapter client)
                {
                    client._clientConnection?.SendAsync(data);
                }
                else if (this is ServerAdapter server)
                {
                    server._serverConnection?.SendAsync(data);
                }
            }
        }
    }
}
