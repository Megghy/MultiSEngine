using MultiSEngine.Application.Clients;
using MultiSEngine.Application.Sessions;

namespace MultiSEngine.Application.Transfers;

public static class TransferCoordinator
{
    public static async Task JoinAsync(ClientData client, ServerInfo server, CancellationToken cancel = default)
    {
        if (Hooks.OnPreSwitch(client, server, out _))
            return;

        var canStartTransfer = client.Session.State is SessionState.IdleInFakeWorld or SessionState.InGameTarget;
        if (client.CurrentServer?.Name == server?.Name || !canStartTransfer)
        {
            if (client.CurrentServer == server)
                await client.SendErrorMessageAsync(string.Format(Localization.Get("Command_AlreadyIn"), server.Name)).ConfigureAwait(false);
            Logs.Warn($"Unallowed transmission requests for [{client.Name}]");
            return;
        }

        Logs.Info($"Switching [{client.Name}] to the server: [{server.Name}]");
        SessionLifecycleService.BeginTransfer(client);
        client.State = ClientState.ReadyToSwitch;

        try
        {
            try
            {
                client.State = ClientState.Switching;
                client.Adapter?.PauseRouting(true, true);

                client.TempAdapter = new(client, client.Adapter.ClientConnection, server);
                SessionLifecycleService.MarkTargetConnecting(client);

                cancel = cancel == default ? new CancellationTokenSource(Config.Instance.SwitchTimeOut).Token : cancel;
                await client.TempAdapter.TryConnect(cancel).ConfigureAwait(false);
                await client.TempAdapter.DisposeAsync(false).ConfigureAwait(false);

                SessionLifecycleService.MarkTargetAttaching(client);
                await client.Adapter.SetServerConnectionAsync(client.TempAdapter.ServerConnection, true).ConfigureAwait(false);
                client.LastServer = client.CurrentServer;
                client.CurrentServer = server;
                PlayerStateStore.ApplyTargetSession(client.Player, client.TempAdapter.Session);

                SessionLifecycleService.MarkClientSyncing(client);
                client.TempAdapter = null;
                await PlayerSyncService.SyncClientAsync(client, cancel).ConfigureAwait(false);
                SessionLifecycleService.MarkTargetEntered(client);
                client.State = ClientState.InGame;
                client.Adapter?.PauseRouting(false, false);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("Invalid server address:", StringComparison.Ordinal))
            {
                await HandleSwitchFailureAsync(client, ex, Localization.Instance["Prompt_UnknownAddress"]).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await HandleSwitchFailureAsync(client, ex, Localization.Instance["Prompt_CannotConnect", server.Name]).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"An error occurred while switching servers: {ex}");
        }
    }

    public static async ValueTask BackAsync(ClientData client, CancellationToken cancellationToken = default)
    {
        if (client is null)
            return;

        var noAvailableWorld = (client.LastServer ?? Config.Instance.DefaultServerInternal) is null;
        if (client.CurrentServer == Config.Instance.DefaultServerInternal || noAvailableWorld)
        {
            var returningFromTarget = client.CurrentServer is not null;
            if (returningFromTarget)
            {
                SessionLifecycleService.BeginReturnToFakeWorld(client);
                client.State = ClientState.Switching;
            }

            await client.SendErrorMessageAsync(Localization.Instance["Prompt_NoAvailableServer"]).ConfigureAwait(false);
            Logs.Info($"No default server avilable, send [{client.Name}] to FakeWorld.");
            Logs.Info($"[{client.Name}] now in FakeWorld");

            PlayerStateStore.ResetTargetCharacter(client.Player);
            client.CurrentServer = null;
            if (client.Adapter?.ServerConnection is { } serverConnection)
                await serverConnection.DisposeAsync(true).ConfigureAwait(false);

            await PlayerSyncService.SyncClientAsync(client, cancellationToken).ConfigureAwait(false);
            await TeleportService.EnterFakeWorldAsync(client, cancellationToken).ConfigureAwait(false);

            client.State = ClientState.ReadyToSwitch;
            if (returningFromTarget)
                SessionLifecycleService.CompleteReturnToFakeWorld(client);

            return;
        }

        if (client.CurrentServer is null)
        {
            await client.SendErrorMessageAsync(Localization.Instance["Prompt_CannotConnect", client.TempAdapter?.TargetServer?.Name]).ConfigureAwait(false);
            return;
        }

        await JoinAsync(client, client.LastServer ?? Config.Instance.DefaultServerInternal, cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask DisconnectAsync(ClientData client, string? reason = null)
    {
        if (client.Disposed)
            return;

        SessionLifecycleService.BeginDisconnect(client);
        try
        {
            Logs.Text($"[{client.Name}] disconnected. {reason}");
            Hooks.OnPlayerLeave(client, out _);
            foreach (var otherClient in RuntimeState.ClientRegistry.Where(c => c.CurrentServer is null && c != client))
                await otherClient.SendMessageAsync($"{client.Name} has leave.", Utils.Rgb(255, 255, 255), true).ConfigureAwait(false);

            if (client.Adapter is { } adapter)
            {
                await adapter
                    .SendToClientDirectAsync(new Kick
                    {
                        Reason = Utils.LiteralText(reason ?? "You have been kicked. Reason: unknown")
                    })
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            client.Dispose();
        }
    }

    private static async ValueTask HandleSwitchFailureAsync(ClientData client, Exception ex, string message)
    {
        var fatalFailure = client.Session.State is SessionState.AttachingTarget or SessionState.SyncingClient;
        var rollbackToStable = client.Session.State is SessionState.PreparingTransfer or SessionState.ConnectingTarget;
        if (rollbackToStable)
        {
            SessionLifecycleService.RollbackToStableState(client);
            client.State = client.Session.State == SessionState.InGameTarget
                ? ClientState.InGame
                : ClientState.ReadyToSwitch;
        }
        else if (client.Session.State == SessionState.InGameTarget)
        {
            client.State = ClientState.InGame;
        }

        client.Adapter?.PauseRouting(false, false);
        if (client.TempAdapter?.ServerConnection is { } serverConnection)
            await serverConnection.DisposeAsync(true).ConfigureAwait(false);
        if (client.TempAdapter is { } tempAdapter)
            await tempAdapter.DisposeAsync(false).ConfigureAwait(false);
        client.TempAdapter = null;

        Logs.Error($"Unable to connect to server: {client?.Name ?? "<unknown>"}{Environment.NewLine}{ex}");
        if (fatalFailure)
        {
            await DisconnectAsync(client, message).ConfigureAwait(false);
            return;
        }

        await client.SendErrorMessageAsync(message).ConfigureAwait(false);
    }
}
