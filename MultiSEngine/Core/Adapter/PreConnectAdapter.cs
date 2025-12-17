using System.Net.Sockets;
using MultiSEngine.Core.Handler;
using MultiSEngine.DataStruct;

namespace MultiSEngine.Core.Adapter
{
    public class PreConnectAdapter(ClientData client, TcpContainer clientConnection, ServerInfo targetServer) : BaseAdapter(client, clientConnection, null)
    {
        public ServerInfo TargetServer { get; init; } = targetServer;
        public PreConnectHandler ConnectHandler { get; private set; }

        public virtual async Task TryConnect(CancellationToken cancel = default)
        {
            // fully async connect and handshake; TcpContainer owns the TcpClient lifecycle
            if (ConnectHandler?.IsConnecting == true)
                return;
            ConnectHandler = new PreConnectHandler(this, TargetServer);
            ConnectHandler.Initialize();
            RegisterHandler(ConnectHandler);
            cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
            var ip = await Utils.ResolveAddressAsync(TargetServer.IP, cancel).ConfigureAwait(false);
            if (ip is not null)
            {
                var client = new TcpClient();
                try
                {
                    await client.ConnectAsync(ip, TargetServer.Port, cancel).ConfigureAwait(false);
                    await SetServerConnectionAsync(new(client)).ConfigureAwait(false);
                    Start();

                    await SendToServerDirectAsync(new ClientHello
                    {
                        Version = $"Terraria{(TargetServer.VersionNum is { } and > 0 and < 65535 ? TargetServer.VersionNum : Client?.Player.VersionNum ?? Config.Instance.ServerVersion)}"
                    }, cancel).ConfigureAwait(false);  //发起连接请求   
                    var success = await ConnectHandler.ConnectionTask.WaitAsync(cancel).ConfigureAwait(false);
                    if (!success)
                        throw new InvalidOperationException($"PreConnect failed to {TargetServer.Name}");
                }
                catch
                {
                    throw;
                }
                // do not dispose client here; it is owned by TcpContainer
            }
            else
            {
                throw new InvalidOperationException($"Invalid server address: {TargetServer.IP}");
            }
        }
    }
}
