using MultiSEngine.Application.Transfers;

namespace MultiSEngine.Application.Clients
{
    /// <summary>
    /// 服务器切换
    /// </summary>
    public static partial class ClientManager
    {
        /// <summary>
        /// 加入到指定的服务器
        /// </summary>
        /// <param name="client"></param>
        /// <param name="server"></param>
        public static async Task Join(this ClientData client, ServerInfo server, CancellationToken cancel = default)
            => await TransferCoordinator.JoinAsync(client, server, cancel).ConfigureAwait(false);

        public static async ValueTask BackAsync(this ClientData client, CancellationToken cancellationToken = default)
            => await TransferCoordinator.BackAsync(client, cancellationToken).ConfigureAwait(false);

        public static void Back(this ClientData client)
            => _ = client?.BackAsync();

        public static async ValueTask SyncAsync(this ClientData client, ServerInfo targetServer, CancellationToken cancellationToken = default)
            => await PlayerSyncService.SyncClientAsync(client, cancellationToken).ConfigureAwait(false);
        public static void Disconnect(this ClientData client, string reason = null)
            => _ = client.DisconnectAsync(reason);

        public static async ValueTask DisconnectAsync(this ClientData client, string reason = null)
            => await TransferCoordinator.DisconnectAsync(client, reason).ConfigureAwait(false);
    }
}


