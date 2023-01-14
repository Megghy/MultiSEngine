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
                _client.Adapter = new BaseAdapter(_client, this, null);

                Data.Clients.Add(_client);

#if DEBUG
                Console.WriteLine($"Session created.");
#endif
            }
            private ClientData _client { get; set; }
            protected override void OnConnecting()
            {
                Logs.Text($"{Socket.RemoteEndPoint} trying to connect...");
            }

            protected override void OnDisconnecting()
            {
                _client?.Disconnect();
            }

            protected override void OnReceived(byte[] buffer, long offset, long size)
            {
                if (_client != null && _client.Adapter != null)
                {
                    _client.Adapter.CheckBuffer(buffer.AsSpan().Slice((int)offset, (int)size).ToArray(), _client.Adapter.RecieveClientData, _client.Adapter.ClientBufCheckedCallback);
                }
            }

            protected override void OnError(SocketError error)
            {
                Console.WriteLine($"TCP session caught an error with code {error}");
            }
        }
        public class NetClient : TcpClient
        {
            public NetClient(IPAddress address, int port, BaseAdapter adapter) : base(address, port)
            {
                _adpter = adapter;
            }
            private BaseAdapter _adpter { get; init; }
            protected override void OnReceived(byte[] buffer, long offset, long size)
            {
                _adpter.CheckBuffer(buffer.AsSpan().Slice((int)offset, (int)size).ToArray(), _adpter.RecieveServerData, _adpter.ServerBufCheckedCallback);
            }
            protected override void OnError(SocketError error)
            {
                Console.WriteLine($"TCP session caught an error with code {error}");
                _adpter.Client?.Back();
            }
            protected override void OnDisconnecting()
            {
                _adpter.Stop();
            }
        }
        public static PacketSerializer DefaultClientSerializer = new(true);
        public static PacketSerializer DefaultServerSerializer = new(false);
        [AutoInit(postMsg: "Opened socket server successfully.")]
        public static void Init()
        {
            try
            {
                var server = new NetServer(Config.Instance.ListenIP is null or "0.0.0.0" or "localhost" ? IPAddress.Any : IPAddress.Parse(Config.Instance.ListenIP), Config.Instance.ListenPort);
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
                    tempConnection.Stop(true);
                }
                isTesting = false;
                return false;
            }).Result;
        }
    }

}
