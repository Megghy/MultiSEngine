using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
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
        public static Socket SocketServer { get; internal set; }
        public static readonly PacketSerializer ClientSerializer = new(true);
        public static readonly PacketSerializer ServerSerializer = new(false);
        [AutoInit(postMsg: "Opened socket server successfully.")]
        public static void Init()
        {
            try
            {
                SocketServer?.Dispose();
                SocketServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                IPAddress address = Config.Instance.ListenIP is null or "0.0.0.0" or "localhost" ? IPAddress.Any : IPAddress.Parse(Config.Instance.ListenIP);
                IPEndPoint point = new(address, Config.Instance.ListenPort);
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
        }
        public static void WatchConnecting()
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
