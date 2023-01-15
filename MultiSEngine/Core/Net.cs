using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using TrProtocol;

namespace MultiSEngine.Core
{
    public class Net
    {
        public static TcpListener Server { get; private set; }
        public static readonly PacketSerializer DefaultClientSerializer = new(true);
        public static readonly PacketSerializer DefaultServerSerializer = new(false);
        [AutoInit(postMsg: "Opened socket server successfully.")]
        public static void Init()
        {
            try
            {
                IPAddress address = Config.Instance.ListenIP is null or "0.0.0.0" or "localhost" ? IPAddress.Any : IPAddress.Parse(Config.Instance.ListenIP);
                Server = new(new IPEndPoint(address, Config.Instance.ListenPort));
                Server.Start();
                Task.Run(WatchConnection);
            }
            catch (Exception ex)
            {
                Logs.Error(ex);
                Console.ReadLine();
                Environment.Exit(0);
            }
        }
        public static void WatchConnection()
        {
            while (true)
            {
                try
                {
                    var client = new ClientData();
                    client.Adapter = new(client, Server.AcceptTcpClient());

                    Logs.Text($"{client.Adapter.ClientConnection.RemoteEndPoint} trying to connect...");

                    Data.Clients.Add(client);
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
                Logs.Info($"Test completed. Available servers:{successCount}/{Config.Instance.Servers.Count}");
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
                var tempConnection = new TestAdapter(server, showDetails);
                try
                {
                    Logs.Info($"Start testing the connectivity of [{server.Name}]");
                    Task.Run(() =>
                    {
                        tempConnection.StartTest();
                    });
                    long waitTime = 0;
                    while (Config.Instance.SwitchTimeOut > waitTime)
                    {
                        if (tempConnection.IsSuccess.HasValue)
                        {
                            if (tempConnection.IsSuccess == true)
                            {
                                isTesting = false;
                                Logs.Success($"Server [{server.Name}] is in good condition :)");
                                return true;
                            }
                            else
                            {
                                isTesting = false;
                                return false;
                            }
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
                finally
                {
                    tempConnection.Dispose(true);
                }
                isTesting = false;
                return false;
            }).Result;
        }
    }

}
