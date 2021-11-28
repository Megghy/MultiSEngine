using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using TrProtocol;
using System.Linq;

namespace MultiSEngine.Core
{
    public class Net
    {
        public static Socket SocketServer { get; internal set; }
        public static readonly Dictionary<int, PacketSerializer> ClientSerializer = new();
        public static readonly Dictionary<int, PacketSerializer> ServerSerializer = new();
        public static PacketSerializer DefaultClientSerializer => ClientSerializer[Config.Instance.ServerVersion > Data.Versions.Last() ? Data.Versions.Last() : Config.Instance.ServerVersion];
        public static PacketSerializer DefaultServerSerializer => ServerSerializer[Config.Instance.ServerVersion > Data.Versions.Last() ? Data.Versions.Last() : Config.Instance.ServerVersion];
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
                    var client = new ClientData();
                    client.CAdapter = new FakeWorldAdapter(client, SocketServer.Accept());
                    client.CAdapter.Start();

                    Logs.Text($"{client.CAdapter.Connection.RemoteEndPoint} trying to connect...");

                    Data.Clients.Add(client);
                }
                catch (Exception ex)
                {
                    Logs.Error(ex);
                }
            }
        }
    }

}
