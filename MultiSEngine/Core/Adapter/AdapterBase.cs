using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.Modules.DataStruct;
using TrProtocol;

namespace MultiSEngine.Core.Adapter
{
    public abstract class AdapterBase
    {
        public AdapterBase(ClientData client, Socket connection)
        {
            Client = client;
            Connection = connection;
        }
        public virtual PacketSerializer Serializer { get; set; } = new(false);
        public ClientData Client { get; set; }
        public Socket Connection { get; set; }
        public virtual Packet GetOriginData(byte[] buffer, int start, int length)
        {
            try
            {
                using (var reader = new BinaryReader(new MemoryStream(buffer, start, length)))
                    return Serializer.Deserialize(reader);
            }
            catch (Exception ex)
            {
                Logs.Error($"An error occurred while serializing the packet{Environment.NewLine}{ex}");
                return null;
            }
        }
        /// <summary>
        /// 返回是否要继续传递给给定的socket
        /// </summary>
        /// <param name="client"></param>
        /// <param name="buffer"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public abstract bool GetData(Packet packet);
        public abstract void SendData(Packet packet);
        public virtual AdapterBase Start()
        {
            Task.Run(RecieveLoop);
            return this;
        }
        internal void RecieveLoop()
        {
            byte[] buffer = new byte[131070];
            while (true)
            {
                try
                {
                    CheckBuffer(Connection?.Receive(buffer) ?? -1, buffer);
                    Array.Clear(buffer, 0, buffer.Length);
                }
                catch (Exception ex)
                {
                    Logs.Error($"Socket connection abnormally terminated.\r\n{ex}");
                    break;
                }
            }
        }
        internal void CheckBuffer(int size, byte[] buffer)
        {
            try
            {
                if (size <= 0)
                    return;
                if (size > BitConverter.ToUInt16(buffer, 0))
                {
                    var position = 0;
                    while (position < size)
                    {
                        var tempLength = BitConverter.ToUInt16(buffer, position);
                        if (tempLength <= 0)
                            return;
                        var packet = GetOriginData(buffer, position, tempLength);
                        if (GetData(packet))
                            SendData(packet);
                        position += tempLength;
                    }
                }
                else
                {
                    var packet = GetOriginData(buffer, 0, size);
                    if (GetData(packet))
                        SendData(packet);
                }
            }
            catch (Exception ex) { Logs.Error($"An error occurred while processing buffer data{Environment.NewLine}{ex}"); }
        }
    }
}
