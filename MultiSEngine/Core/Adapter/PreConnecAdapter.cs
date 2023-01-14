using System.Threading.Tasks;
using System.Threading;
using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;
using System;
using TrProtocol.Packets;

namespace MultiSEngine.Core.Adapter
{
    public class PreConnecAdapter : BaseAdapter
    {
        public PreConnecAdapter(ClientData client, Net.NetSession clientConnection, ServerInfo targetServer) : base(client, clientConnection, null)
        {
            TargetServer = targetServer;
        }

        public ServerInfo TargetServer { get; init; }
        private PreConnectHandler _preConnectHandler;

        public async Task TryConnect(CancellationToken cancel = default)
        {
            if (_preConnectHandler?.IsConnecting == true)
                return;
            _preConnectHandler = new PreConnectHandler(this, TargetServer);
            _preConnectHandler.Initialize();
            RegisteHander(_preConnectHandler);
            cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
            await Task.Run(() =>
            {
                if (Utils.TryParseAddress(TargetServer.IP, out var ip))
                {
                    ServerConnection = new(ip, TargetServer.Port, this);
                    ServerConnection.ConnectAsync();
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
