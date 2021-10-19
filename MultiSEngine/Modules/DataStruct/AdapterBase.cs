using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;
using TrProtocol;

namespace MultiSEngine.Modules.DataStruct
{
    public interface IStatusChangeable
    {
        public bool RunningAsNormal { get; set; }
        public void ChangeProcessState(bool asNormal);
    }
    public abstract class AdapterBase
    {
        public AdapterBase(ClientData client, Socket connection)
        {
            Client = client;
            Connection = connection;
            PacketPool = new();
            NetReader = new BinaryReader(new NetworkStream(Connection));
        }
        #region 变量
        public int ErrorCount = 0;
        protected bool ShouldStop { get; set; } = false;
        public virtual PacketSerializer Serializer { get; set; } = new(true);
        public ClientData Client { get; protected set; }
        public Socket Connection { get; set; }
        public Queue PacketPool { get; set; }
        protected BinaryReader NetReader { get; set; }
        #endregion
        /// <summary>
        /// 返回是否要继续传递给给定的socket
        /// </summary>
        /// <param name="client"></param>
        /// <param name="buffer"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public abstract bool GetPacket(ref Packet packet);
        public abstract void SendPacket(Packet packet);
        public virtual AdapterBase Start()
        {
            //Connection.BeginReceive(Buffer, 0, Buffer.Length, SocketFlags.None, new AsyncCallback(ReceiveMessage), null);
            Task.Run(RecieveLoop);
            Task.Run(ProcessPacketLoop);
            return this;
        }
        public virtual void Stop(bool disposeConnection = false)
        {
            if (ShouldStop)
                return;
#if DEBUG
            Logs.Warn($"[{GetType()}] <{Connection.RemoteEndPoint}> Stopped");
#endif
            ShouldStop = true;
            if (disposeConnection)
            {
                try { Connection?.Shutdown(SocketShutdown.Both); } catch { }
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
        protected void ProcessPacketLoop()
        {
            while (!ShouldStop)
            {
                while (PacketPool.Count < 1)
                    Task.Delay(1).Wait();
                var packet = PacketPool.Dequeue() as Packet;
                try
                {
                    if (!Core.Hooks.OnGetPacket(Client, packet, out _)
                        && GetPacket(ref packet)
                        && !Core.Hooks.OnSendPacket(Client, packet, out _))
                        SendPacket(packet);
                }
                catch (Exception ex)
                {
                    Logs.Error($"An error occurred while processing packet {packet}.{Environment.NewLine}{ex}");
                }
            }
            PacketPool.Clear();
        }
        public virtual void OnRecieveLoopError(Exception ex)
        {
#if DEBUG
            Console.WriteLine($"[Recieve Loop Error] {ex}");
#endif
            ErrorCount++;
            switch (ex)
            {
                case EndOfStreamException:
                case IOException:
                    break;
                case Exception:
                    Logs.Warn($"Socket connection abnormally terminated.\r\n{ex}");
                    break;
                default:
                    break;
            }
        }
        protected void RecieveLoop()
        {
            try
            {
                while (NetReader is { BaseStream: not null } && !ShouldStop)
                {
                    PacketPool.Enqueue(Serializer.Deserialize(NetReader));
                }
            }
            catch (Exception ex)
            {
                OnRecieveLoopError(ex);
            }
            if (!ShouldStop)
                Stop(true);
        }
        public virtual void InternalSendPacket(Packet packet)
        {
#if DEBUG
            Console.WriteLine($"[Internal Send] {packet}");
#endif
            if (!ShouldStop)
                Connection?.Send(packet.Serialize());
        }
    }
}
