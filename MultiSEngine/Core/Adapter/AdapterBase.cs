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
        public bool ShouldStop { get; set; } = false;
        public virtual PacketSerializer Serializer { get; set; } = new(true);
        public ClientData Client { get; set; }
        public Socket Connection { get; set; }
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
        public virtual void Stop(bool closeConnection = false)
        {
            ShouldStop = true;
            if (closeConnection)
            {
                Connection?.Dispose();
                Connection = null;
            }
        }
        internal void RecieveLoop()
        {
            using (var reader = new BinaryReader(new NetworkStream(Connection)))
                try
                {
                    while (Connection is { Connected: true })
                    {
                        var packet = Serializer.Deserialize(reader);
                        if (GetData(packet))
                            SendData(packet);
                    }
                }
                catch (EndOfStreamException) { }
                catch (IOException) { }
                catch (Exception ex)
                {
                    Logs.Error($"Socket connection abnormally terminated.\r\n{ex}");
                }

        }
    }
}
