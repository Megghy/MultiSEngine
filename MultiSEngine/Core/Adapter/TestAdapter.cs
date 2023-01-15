using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    internal class TestAdapter : PreConnecAdapter
    {
        public TestAdapter(ServerInfo server, bool showDetails) : base(null, null, server)
        {
            ShowDetails = showDetails;
        }
        public bool ShowDetails { get; init; }
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
            RegisteHander(handler);
            Log($"Start connecting to [{TargetServer.Name}]<{TargetServer.IP}:{TargetServer.Port}>");
            var cancel = new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token;
            await Task.Run(async () =>
            {
                if (Utils.TryParseAddress(TargetServer.IP, out var ip))
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(ip, TargetServer.Port);
                    SetServerConnection(new(client));
                }
                else
                {
                    throw new Exception($"Invalid server address: {TargetServer.IP}");
                }
            }, cancel).ContinueWith(task =>
            {
                while (!ServerConnection.IsConnected)
                {
                    cancel.ThrowIfCancellationRequested();
                    Thread.Sleep(1);
                }
                State = 1;
                Log($"Sending [ConnectRequest] packet");
                SendToServerDirect(new ClientHello()
                {
                    Version = $"Terraria{(TargetServer.VersionNum is { } and > 0 and < 65535 ? TargetServer.VersionNum : Config.Instance.ServerVersion)}"
                });  //发起连接请求 
            }, cancel);
        }
    }
}
