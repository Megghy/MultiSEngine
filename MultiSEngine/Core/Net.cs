using System.Net;
using System.Net.Sockets;
using MultiSEngine.Core.Adapter;
using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;
using MultiSEngine.Modules;

namespace MultiSEngine.Core
{
    public class Net
    {
        public static TcpListener Server { get; private set; }
        private static Task _watchTask;
        [AutoInit(postMsg: "Opened socket server successfully.")]
        public static void Init()
        {
            try
            {
                IPAddress address = Config.Instance.ListenIP is null or "0.0.0.0" or "localhost" ? IPAddress.Any : IPAddress.Parse(Config.Instance.ListenIP);
                Server = new(new IPEndPoint(address, Config.Instance.ListenPort));
                Server.Start();
                // 启动异步 Accept 循环并持有任务引用
                _watchTask = WatchConnectionAsync();
            }
            catch (Exception ex)
            {
                Logs.Error(ex);
                Console.ReadLine();
                Environment.Exit(0);
            }
        }
        public static async Task WatchConnectionAsync()
        {
            while (true)
            {
                try
                {
                    var tcp = await Server.AcceptTcpClientAsync().ConfigureAwait(false);
                    var client = new ClientData();
                    client.Adapter = new(client, new(tcp));
                    client.Adapter.RegisterHandler(new AcceptConnectionHandler(client.Adapter));
                    client.Adapter.ExceptionRaised += async ex =>
                    {
                        if (client.State == ClientState.InGame && client.Adapter?.ServerConnection is { } serverConnection)
                        {
                            await serverConnection.DisposeAsync(true);
                        }
                        else
                        {
                            await client.DisconnectAsync();
                        }
                    };
                    client.Adapter.Start();

                    Logs.Text($"{client.Adapter.ClientConnection.RemoteEndPoint} trying to connect...");
                }
                catch (Exception ex)
                {
                    Logs.Error(ex);
                }
            }
        }
        static bool isTesting = false;
        internal static async Task TestAllAsync(bool showDetails = false)
        {
            Logs.Info($"Ready to start testing all server connectivity");
            var tasks = Config.Instance.Servers.Select(s => TestConnectAsync(s, showDetails)).ToArray();
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            var successCount = results.Count(r => r);
            Logs.Info($"Test completed. Available servers:{successCount}/{Config.Instance.Servers.Count}");
        }
        internal static async Task<bool> TestConnectAsync(ServerInfo server, bool showDetails = false)
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
                await tempConnection.StartTest().ConfigureAwait(false);
                long waitTime = 0;
                while (Config.Instance.SwitchTimeOut > waitTime)
                {
                    if (tempConnection.IsSuccess.HasValue)
                    {
                        if (tempConnection.IsSuccess == true)
                        {
                            Logs.Success($"Server [{server.Name}] is in good condition :)");
                            return true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    waitTime += 50;
                    await Task.Delay(50).ConfigureAwait(false);
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
                await tempConnection.DisposeAsync(true).ConfigureAwait(false);
                isTesting = false;
            }
            return false;
        }
    }

}
