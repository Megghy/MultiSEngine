using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using Delphinus;
using MultiSEngine.Modules.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    public abstract class AdapterBase
    {
        public AdapterBase(ClientData client, Socket connection)
        {
            Client = client;
            Connection = connection;
            NetReader = new BinaryReader(new NetworkStream(Connection));
        }
        public bool ShouldStop { get; set; } = false;
        public virtual PacketSerializer Serializer { get; set; } = new(true);
        public ClientData Client { get; set; }
        public Socket Connection { get; set; }
        public int ErrorCount = 0;
        public BinaryReader NetReader { get; set; }
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
        public virtual void Stop(bool disposeConnection = false)
        {
            ShouldStop = true;
            if (disposeConnection)
            {
                Connection?.Shutdown(SocketShutdown.Both);
                NetReader?.Dispose();
                NetReader = null;
                Connection?.Dispose();
            }
        }
        public virtual void ChangeConnection(Socket connection)
        {
            Connection?.Shutdown(SocketShutdown.Both);
            NetReader?.Dispose();
            Connection = null;
            NetReader = null;
            Connection = connection;
            NetReader = new(new NetworkStream(Connection));
        }
        public virtual void OnRecieveError(Exception ex)
        {
            ErrorCount++;
            switch (ex)
            {
                case EndOfStreamException:
                case IOException:
                    break;
                case Exception:
                    Logs.Error($"Socket connection abnormally terminated.\r\n{ex}");
                    break;
                default:
                    break;
            }
        }
        public virtual void InternalSendPacket(Packet packet)
        {
            if (!ShouldStop)
                Connection?.Send(packet.Serilize());
        }
        internal void RecieveLoop()
        {
            while (NetReader is { BaseStream: not null } && !ShouldStop)
            {
                try
                {
                    Packet packet;
                    packet = Serializer.Deserialize(NetReader);
                    try
                    {
                        if (GetData(packet))
                            SendData(packet);
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"An error occurred while processing packet {packet}.{Environment.NewLine}{ex}");
                    }
                }
                catch (EndOfStreamException eos) { OnRecieveError(eos); }
                catch (IOException io) { OnRecieveError(io); }
                catch (Exception ex) { OnRecieveError(ex); }
            }

        }
    }
}
