using System.Net.Sockets;
using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    internal class TestAdapter(ServerInfo server, bool showDetails) : PreConnectAdapter(null, null, server)
    {
        public bool ShowDetails { get; init; } = showDetails;
        public int State { get; internal set; } = 0;
        public bool? IsSuccess { get; internal set; }
        internal void Log(string msg, bool isDetail = true, ConsoleColor color = ConsoleColor.Blue)
        {
            if (isDetail && !ShowDetails)
                return;
            Logs.LogAndSave(msg, $"[TEST] <{TargetServer.Name}> {(IsSuccess.HasValue ? ((bool)IsSuccess) ? "SUCCESS" : "FAILED" : "TESTING")}: {State} -", color, false);
        }
        public async Task StartTest()
        {
            if (State != 0)
                return;
            var handler = new TestHandler(this);
            handler.Initialize();
            RegisterHandler(handler);
            Log($"Start connecting to [{TargetServer.Name}]<{TargetServer.IP}:{TargetServer.Port}>");
            var cancel = new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token;
            // 直接异步连接并接管 TcpClient 生命周期
            if (Utils.TryParseAddress(TargetServer.IP, out var ip))
            {
                var client = new TcpClient();
                await client.ConnectAsync(ip, TargetServer.Port, cancel).ConfigureAwait(false);
                await SetServerConnectionAsync(new(client)).ConfigureAwait(false);
                Start();
            }
            else
            {
                throw new Exception($"Invalid server address: {TargetServer.IP}");
            }

            while (!ServerConnection.IsConnected)
            {
                cancel.ThrowIfCancellationRequested();
                await Task.Delay(1, cancel).ConfigureAwait(false);
            }
            State = 1;
            Log($"Sending [ConnectRequest] packet");
            await SendToServerDirectAsync(new ClientHello
            {
                Version = $"Terraria{(TargetServer.VersionNum is { } and > 0 and < 65535 ? TargetServer.VersionNum : Config.Instance.ServerVersion)}"
            }).ConfigureAwait(false);  //发起连接请求 
        }
    }
}
