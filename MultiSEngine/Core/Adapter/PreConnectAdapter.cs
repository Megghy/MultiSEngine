using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    public class PreConnectAdapter : BaseAdapter
    {
        public PreConnectAdapter(ClientData client, TcpContainer clientConnection, ServerInfo targetServer) : base(client, clientConnection, null)
        {
            TargetServer = targetServer;
        }

        public ServerInfo TargetServer { get; init; }
        private PreConnectHandler _preConnectHandler;

        public virtual async Task TryConnect(CancellationToken cancel = default)
        {
            if (_preConnectHandler?.IsConnecting == true)
                return;
            _preConnectHandler = new PreConnectHandler(this, TargetServer);
            _preConnectHandler.Initialize();
            RegisteHander(_preConnectHandler);
            cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
            await Task.Run(async () =>
            {
                if (Utils.TryParseAddress(TargetServer.IP, out var ip))
                {
                    var client = new TcpClient();
                    await client.ConnectAsync(ip, TargetServer.Port, cancel);
                    SetServerConnection(new(client));
                }
                else
                {
                    throw new Exception($"Invalid server address: {TargetServer.IP}");
                }
            }, cancel).ContinueWith(task =>
            {
                SendToServerDirect(new ClientHello()
                {
                    Version = $"Terraria{(TargetServer.VersionNum is { } and > 0 and < 65535 ? TargetServer.VersionNum : Client?.Player.VersionNum ?? Config.Instance.ServerVersion)}"
                });  //发起连接请求   
                while (_preConnectHandler.IsConnecting)
                {
                    cancel.ThrowIfCancellationRequested();
                    Thread.Sleep(1);
                }
            }, cancel);
        }
    }
}
