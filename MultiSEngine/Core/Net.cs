using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Delphinus;
using MultiSEngine.Core.Adapter;
using MultiSEngine.Modules;

namespace MultiSEngine.Core
{
    public class Net
    {
        public static Net Instance { get; internal set; } = new();
        public Socket SocketServer { get; internal set; }
        public readonly PacketSerializer ClientSerializer = new(true);
        public readonly PacketSerializer ServerSerializer = new(false);
        public Net Init(string ip = "127.0.0.1", int port = 7778)
        {
            try
            {
                SocketServer?.Dispose();
                SocketServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPAddress address = IPAddress.Parse(ip);
                IPEndPoint point = new(address, port);
                SocketServer.Bind(point);
                SocketServer.Listen(50);

                Task.Run(WatchConnecting);
            }
            catch (Exception ex)
            {
                Logs.Error(ex);
                Console.ReadLine();
            }
            return this;
        }
        public void WatchConnecting()
        {
            while (true)
            {
                try
                {
                    Socket connection = SocketServer.Accept();

                    Logs.Text($"{connection.RemoteEndPoint} trying to connect...");
                    var ca = new FakeWorldAdapter(new(), connection);
                    ca.Start();

                    Data.Clients.Add(ca.Client);
                }
                catch (Exception ex)
                {
                    Logs.Error(ex);
                    continue;
                }
            }
        }
    }

}
