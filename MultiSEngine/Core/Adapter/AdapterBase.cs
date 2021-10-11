using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.Modules.DataStruct;
using TrProtocol;
using TrProtocol.Models;

namespace MultiSEngine.Core.Adapter
{
    public abstract class AdapterBase
    {
        public AdapterBase(ClientData client, Socket connection)
        {
            Client = client;
            Connection = connection;
        }
        public virtual PacketSerializer Serilizer { get; set; } = new(true);
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
        internal void RecieveLoop()
        {
            using (var reader = new BinaryReader(new NetworkStream(Connection)))
                while (true)
                {
                    try
                    {
                        var packet = Serilizer.Deserialize(reader);
                        if (GetData(packet))
                            SendData(packet);
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Socket connection abnormally terminated.\r\n{ex}");
                        break;
                    }
                }
        }
    }
}
