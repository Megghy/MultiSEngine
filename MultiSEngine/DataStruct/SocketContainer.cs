using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TrProtocol;

namespace MultiSEngine.DataStruct
{
    public class TcpContainer
    {
        public TcpContainer(TcpClient connection)
        {
            Connection = connection;
            _reader = new(connection.GetStream());
            _writer = new(connection.GetStream());
            Task.Run(RecieveLoop);
        }
        private readonly ConcurrentQueue<byte[]> _packetsBuf = new();
        public event Action<Exception> OnException;
        public TcpClient Connection { get; init; }
        public EndPoint RemoteEndPoint
            => Connection?.Client?.RemoteEndPoint;
        public bool IsDisposed { get; private set; }
        private BinaryReader _reader;
        private BinaryWriter _writer;

        public bool IsConnected
        {
            get
            {
                try
                {
                    if (Connection != null && Connection.Client != null && Connection.Client.Connected)
                    {
                        if (Connection.Client.Poll(0, SelectMode.SelectRead))
                        {
                            byte[] buff = new byte[1];
                            if (Connection.Client.Receive(buff, SocketFlags.Peek) == 0)
                            {
                                return false;
                            }
                            else
                            {
                                return true;
                            }
                        }

                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }
            }
        }

        public bool Send(ref Span<byte> buf)
        {
            if (IsDisposed)
                return false;
            try
            {
                _writer.Write(buf);
                return true;
            }
            catch (Exception ex)
            {
                Logs.Error($"Failed to send data: {(MessageID)buf[2]}{Environment.NewLine}{ex}");
                return false;
            }
        }
        public bool Send(byte[] data)
        {
            var buf = data.AsSpan();
            return Send(ref buf);
        }
        public byte[] Get()
        {
            if (IsDisposed)
                return null;
            while (!IsDisposed)
            {
                if (_packetsBuf.TryDequeue(out var buf))
                    return buf;
                else
                    Task.Delay(1).Wait();
            }
            return null;
        }

        private void RecieveLoop()
        {
            while (!IsDisposed)
            {
                try
                {
                    var len = _reader.ReadUInt16();
                    var buf = new byte[len];
                    Buffer.BlockCopy(_reader.ReadBytes(len - 2), 0, buf, 2, len - 2);
                    MemoryMarshal.Write(buf.AsSpan(0, 2), ref len);
                    _packetsBuf.Enqueue(buf);
                }
                catch (Exception ex)
                {
                    if (!IsDisposed)
                        OnException(ex);
                }
            }
        }
        public void Dispose(bool closeConnection)
        {
#if DEBUG
            Logs.Warn($"{RemoteEndPoint} TcpContainer disposed.");
#endif
            if (IsDisposed)
                return;
            IsDisposed = true;
            if (closeConnection)
                Connection.Close();
            while (_packetsBuf.TryDequeue(out _)) ;
        }
    }
}
