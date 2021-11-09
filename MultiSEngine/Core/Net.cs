using MultiSEngine.Core.Adapter;
using MultiSEngine.Modules;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using TrProtocol;

namespace MultiSEngine.Core
{
    public class Net
    {
        public static Net Instance { get; internal set; } = new();
        public Socket SocketServer { get; internal set; }
        public readonly PacketSerializer ClientSerializer = new(true);
        public readonly PacketSerializer ServerSerializer = new(false);
        public Net Init(string ip = null, int port = 7778)
        {
            try
            {
                SocketServer?.Dispose();
                SocketServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPAddress address = ip is null or "0.0.0.0" or "localhost" ? IPAddress.Any : IPAddress.Parse(ip);
                IPEndPoint point = new(address, port);
                SocketServer.Bind(point);
                SocketServer.Listen(50);

                Task.Run(WatchConnecting);
            }
            catch (Exception ex)
            {
                Logs.Error(ex);
                Console.ReadLine();
                Environment.Exit(0);
            }
            return this;
        }
        public void WatchConnecting()
        {
            while (true)
            {
                try
                {
                    var ca = new FakeWorldAdapter(new(), SocketServer.Accept());
                    ca.Start();

                    Logs.Text($"{ca.Connection.RemoteEndPoint} trying to connect...");

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
