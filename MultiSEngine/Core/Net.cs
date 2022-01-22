using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using TrProtocol;

namespace MultiSEngine.Core
{
    public class Net
    {
        public static Socket SocketServer { get; internal set; }
        public static readonly Dictionary<int, PacketSerializer> ClientSerializer = new();
        public static readonly Dictionary<int, PacketSerializer> ServerSerializer = new();
        public static PacketSerializer DefaultClientSerializer => ClientSerializer[Config.Instance.ServerVersion > Data.Versions.Last() ? Modules.Data.Versions.Last() : Config.Instance.ServerVersion];
        public static PacketSerializer DefaultServerSerializer => ServerSerializer[Config.Instance.ServerVersion > Data.Versions.Last() ? Modules.Data.Versions.Last() : Config.Instance.ServerVersion];
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

                    Modules.Data.Clients.Add(client);
                }
                catch (Exception ex)
                {
                    Logs.Error(ex);
                }
            }
        }
        static bool isTesting = false;
        internal static void TestAll(bool showDetails = false) 
        {
            Task.Run(() =>
            {
                Logs.Info($"Ready to start testing all server connectivity");
                int successCount = 0;
                Config.Instance.Servers.ForEach(s =>
                {
                    if (TestConnect(s, showDetails))
                        successCount++;
                });
                Logs.Info($"Test completed. Number of available servers:{successCount}/{Config.Instance.Servers.Count}");
            });
        }
        internal static bool TestConnect(ServerInfo server, bool showDetails = false)
        {
            return Task.Run(() =>
            {
                if (isTesting)
                {
                    Logs.Warn($"Now testing other servers, please do so when it finished");
                    return false;
                }
                isTesting = true;
                try
                {
                    using var tempConnection = new TestAdapter(server, showDetails);
                    Logs.Info($"Start testing the connectivity of [{server.Name}]");
                    tempConnection.Start();
                    long waitTime = 0;
                    while (Config.Instance.SwitchTimeOut > waitTime)
                    {
                        if (tempConnection?.IsSuccess ?? false)
                        {
                            isTesting = false;
                            Logs.Success($"Server [{server.Name}] is in good condition :)");
                            return true;
                        }
                        else
                            waitTime += 50;
                        Task.Delay(50).Wait();
                    }
                    if (!tempConnection.IsSuccess.HasValue)
                        Logs.LogAndSave($"Test FAILED: Time out", $"[TEST] <{server.Name}>", ConsoleColor.Red, false);
                }
                catch (Exception ex)
                {
                    Logs.LogAndSave($"Test FAILED: Unable to connect to {server.IP}:{server.Port}{Environment.NewLine}{ex}", $"[TEST] <{server.Name}>", ConsoleColor.Red, false);
                }
                isTesting = false;
                return false;
            }).Result;
        }
    }

}
