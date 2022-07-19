using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
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
        public BaseAdapter(ClientData client, Socket connection)
        {
            Client = client;
            Connection = connection;
        }
        #region 变量
        public int ErrorCount { get; protected set; } = 0;
        protected bool ShouldStop { get; set; } = false;
        public int VersionNum => Client?.Player?.VersionNum ?? -1;
        public virtual PacketSerializer InternalClientSerializer => Net.ClientSerializer.TryGetValue(VersionNum, out var result) ? result : Net.DefaultClientSerializer;
        public virtual PacketSerializer InternalServerSerializer => Net.ServerSerializer.TryGetValue(VersionNum, out var result) ? result : Net.DefaultServerSerializer;
        public ClientData Client { get; protected set; }
        public Socket Connection { get; internal set; }
        protected BinaryReader NetReader { get; set; }
        public ConcurrentQueue<Packet> PacketPool { get; protected set; } = new();
        public abstract bool ListenningClient { get; }
        #endregion
        /// <summary>
        /// 返回是否要继续传递给给定的socket
        /// </summary>
        /// <param name="client"></param>
        /// <param name="buffer"></param>
        /// <param name="start"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        public abstract bool GetPacket(Packet packet);
        public abstract void SendPacket(Packet packet);
        public virtual BaseAdapter Start()
        {
            NetReader = new BinaryReader(new NetworkStream(Connection));
            Task.Run(RecieveLoop);
            Task.Run(ProcessPacketLoop);
            return this;
        }
        public virtual void Stop(bool disposeConnection = false)
        {
#if DEBUG
            Logs.Warn($"[{GetType()}] <{Connection?.RemoteEndPoint}> Stopped");
#endif
            ShouldStop = true;
            Client?.TimeOutTimer?.Stop();
            if (disposeConnection)
            {
                try { Connection?.Shutdown(SocketShutdown.Both); } catch { }
                NetReader?.Dispose();
                NetReader = null;
                Connection?.Dispose();
                Connection = null;
            }
        }
        protected void ProcessPacketLoop()
        {
            while (!ShouldStop)
            {
                if (PacketPool.TryDequeue(out var packet))
                {
                    try
                    {
                        if (packet is not null && !Hooks.OnGetPacket(Client, packet, ListenningClient, out _) && GetPacket(packet))
                            SendPacket(packet);
                    }
#if DEBUG
                    catch (IOException io)
                    {
                        Console.WriteLine(io);
                    }
#endif
                    catch (OutOfBoundsException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"An error occurred while processing packet {packet}.{Environment.NewLine}{ex}");
                    }
                }
                else
                    Thread.Sleep(1);
            }
            PacketPool.Clear();
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
        protected void RecieveLoop()
        {
            while (!ShouldStop && NetReader is { BaseStream: not null })
            {
                try
                {
                    PacketPool.Enqueue((ListenningClient ? InternalServerSerializer : Net.DefaultClientSerializer).Deserialize(NetReader));
                }
                catch (Exception ex)
                {
                    OnRecieveLoopError(ex);
                }

            }
        }
        public virtual void InternalSendPacket(Packet packet, bool asClient = false)
        {
#if DEBUG
            Console.WriteLine($"[Internal Send] {packet}");
#endif
            if (!ShouldStop)
                Connection?.Send((ListenningClient ? (asClient ? InternalClientSerializer : InternalServerSerializer) : (asClient ? Net.DefaultClientSerializer : Net.DefaultServerSerializer)).Serialize(packet));
        }
    }
}
