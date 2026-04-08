using System.Net.Sockets;

using MultiSEngine.Application.Transfers;

namespace MultiSEngine.Protocol.Adapters
{
    public class PreConnectAdapter(ClientData client, TcpContainer clientConnection, ServerInfo targetServer) : BaseAdapter(client, clientConnection, null)
    {
        public ServerInfo TargetServer { get; init; } = targetServer;
        public PreConnectSession Session { get; private set; }
        public PreConnectHandler ConnectHandler { get; private set; }

        public virtual async Task TryConnect(CancellationToken cancel = default)
        {
            // fully async connect and handshake; TcpContainer owns the TcpClient lifecycle
            if (ConnectHandler?.IsConnecting == true)
                return;
            Session = new(TargetServer);
            ConnectHandler = new PreConnectHandler(this, Session);
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
                    if (!string.IsNullOrWhiteSpace(Client?.Player.UUID))
                    {
                        await SendToServerDirectAsync(new ClientUUID
                        {
                            UUID = Client.Player.UUID
                        }, cancel).ConfigureAwait(false);
                    }
                    var success = await Session.CompletionTask.WaitAsync(cancel).ConfigureAwait(false);
                    if (!success)
                        throw new InvalidOperationException(Session.FailureReason ?? $"PreConnect failed to {TargetServer.Name}");
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


