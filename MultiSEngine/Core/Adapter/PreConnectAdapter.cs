using System.Net.Sockets;
using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    public class PreConnectAdapter(ClientData client, TcpContainer clientConnection, ServerInfo targetServer) : BaseAdapter(client, clientConnection, null)
    {
        public ServerInfo TargetServer { get; init; } = targetServer;
        private PreConnectHandler _preConnectHandler;

        public virtual Task TryConnect(CancellationToken cancel = default)
        {
            if (_preConnectHandler?.IsConnecting == true)
                return Task.CompletedTask;
            _preConnectHandler = new PreConnectHandler(this, TargetServer);
            _preConnectHandler.Initialize();
            RegisteHander(_preConnectHandler);
            cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
            if (Utils.TryParseAddress(TargetServer.IP, out var ip))
            {
                var client = new TcpClient();
                try
                {
                    client.ConnectAsync(ip, TargetServer.Port, cancel).AsTask().Wait(cancel);
                    SetServerConnection(new(client));

                    SendToServerDirect(new ClientHello($"Terraria{(TargetServer.VersionNum is { } and > 0 and < 65535 ? TargetServer.VersionNum : Client?.Player.VersionNum ?? Config.Instance.ServerVersion)}"));  //发起连接请求   
                    while (_preConnectHandler.IsConnecting)
                    {
                        cancel.ThrowIfCancellationRequested();
                        Thread.Sleep(1);
                    }
                }
                catch
                {
                    throw;
                }
                finally
                {
                    client.Dispose();
                }
            }
            else
            {
                throw new Exception($"Invalid server address: {TargetServer.IP}");
            }
            return Task.CompletedTask;
        }
    }
}
