using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MultiSEngine.Core.Adapter;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;
using NetCoreServer;
using TrProtocol;
using TcpClient = NetCoreServer.TcpClient;

namespace MultiSEngine.Core
{
    public class Net
    {
        public class NetServer : TcpServer
        {
            public NetServer(IPAddress address, int port) : base(address, port) { }

            protected override TcpSession CreateSession() { return new NetSession(this); }

            protected override void OnError(SocketError error)
            {
                Console.WriteLine($"Chat TCP server caught an error with code {error}");
            }
        }
        public class NetSession : TcpSession
        {
            public NetSession(TcpServer server) : base(server)
            {
                _client = new ClientData();
                _client.CAdapter = new FakeWorldAdapter(_client, this);

                Data.Clients.Add(_client);
            }
            private ClientData _client { get; set; }
            protected override void OnConnected()
            {
                Logs.Text($"{Socket.RemoteEndPoint} trying to connect...");
            }

            protected override void OnDisconnecting()
            {
                _client?.Disconnect();
            }

            protected override void OnReceived(byte[] buffer, long offset, long size)
            {
                if (_client != null && _client.CAdapter != null)
                    _client.CAdapter.CheckBuffer(buffer.AsSpan().Slice((int)offset, (int)size), _client.CAdapter.GetData, _client.CAdapter.BufCheckedCallback);
            }

            protected override void OnError(SocketError error)
            {
                Console.WriteLine($"TCP session caught an error with code {error}");
            }
        }
        public class NetClient : TcpClient
        {
            public NetClient(IPAddress address, int port, ServerAdapter adapter) : base(address, port)
            {
                _adapter = adapter;
            }
            private ServerAdapter _adapter { get; init; }
            protected override void OnReceived(byte[] buffer, long offset, long size)
            {
                _adapter.CheckBuffer(buffer.AsSpan().Slice((int)offset, (int)size), _adapter.GetData, _adapter.BufCheckedCallback);
            }
            protected override void OnError(SocketError error)
            {
                Console.WriteLine($"TCP session caught an error with code {error}");
            }
            protected override void OnDisconnecting()
            {
                _adapter.Stop(true);
            }
        }
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
                var server = new NetServer(Config.Instance.ListenIP is null or "0.0.0.0" or "localhost" ? IPAddress.Any : IPAddress.Parse(Config.Instance.ListenIP), Config.Instance.ListenPort)
                {
                    OptionReceiveBufferSize = 131070,
                    OptionSendBufferSize = 131070
                };
                server.Start();
            }
            catch (Exception ex)
            {
                Logs.Error(ex);
                Console.ReadLine();
                Environment.Exit(0);
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
                    Task.Run(() =>
                    {
                        tempConnection.StartTest();
                    });
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
