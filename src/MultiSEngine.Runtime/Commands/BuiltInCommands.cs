using System.Text;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Security;
using MultiSEngine.Core.Sessions;
using MultiSEngine.Core.Transfers;
using MultiSEngine.Protocol.CustomPackets;
using MultiSEngine.Protocol.Terraria;
using MultiSEngine.Runtime.Extensions;

namespace MultiSEngine.Runtime.Commands;

internal sealed class ExecuteCommandCustomPacketHandler(
    ISessionRegistry sessions,
    CommandRouter commands,
    IProxyLogger logger) : ICustomPacketDefinition
{
    public string PacketType => BuiltInCustomPacketTypes.ExecuteCommand;
    public bool AllowFromClient => false;
    public bool AllowFromServer => true;

    public async ValueTask HandleAsync(CustomPacketContext context, CancellationToken cancellationToken)
    {
        var (targetPlayerName, commandText) = BuiltInCustomPacketTypes.DecodeExecuteCommandPayload(context.Payload);
        if (string.IsNullOrWhiteSpace(commandText))
            return;

        ISessionHandle? targetSession;
        var hasClusterPerm = context.SourceServer?.AllowClusterCommands == true;

        if (string.IsNullOrWhiteSpace(targetPlayerName) ||
            string.Equals(targetPlayerName, context.Session.PlayerName, StringComparison.OrdinalIgnoreCase))
        {
            targetSession = context.Session;
        }
        else if (hasClusterPerm)
        {
            targetSession = sessions.FindByName(targetPlayerName);
        }
        else
        {
            logger.Warn(
                $"Blocked cross-player ExecuteCommand from [{context.SourceServer?.Name ?? "unknown"}] targeting '{targetPlayerName}' (AllowClusterCommands is false).",
                "Security");
            return;
        }

        if (targetSession is null)
            return;

        await commands.DispatchAsync(
            CommandSourceKind.BackendServer,
            commandText,
            playerSession: targetSession,
            backendServer: context.SourceServer,
            hasClusterPermission: hasClusterPerm,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}

public sealed class MsePlayerCommand(
    IConfigStore configStore,
    ILocalizationStore localization) : ICommand
{
    public string Name => "mse";
    public IReadOnlyList<string> Aliases => [];
    public CommandSourceKind AllowedSources => CommandSourceKind.Player | CommandSourceKind.BackendServer;
    public string Usage => "/mse <tp|back|list|password> [args]";
    public string Description => "Player cross-server transfer and utility command.";

    public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var session = context.PlayerSession;
        if (session is null)
            return CommandExecutionResult.Intercepted();

        var state = session.CurrentState;
        if (context.Arguments.Count == 0)
        {
            await SendHelpAsync(session, cancellationToken).ConfigureAwait(false);
            return CommandExecutionResult.Intercepted();
        }

        var sub = context.Arguments[0].ToLowerInvariant();
        switch (sub)
        {
            case "p" or "pass" or "password":
            {
                if (state.Phase != SessionPhase.TransferAwaitingPassword)
                {
                    await session.SendChatAsync(localization.Get("Command_NotJoined"), TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, cancellationToken).ConfigureAwait(false);
                    return CommandExecutionResult.Intercepted();
                }

                if (context.Arguments.Count < 2)
                {
                    await session.SendChatAsync(localization.Get("Help_Password"), TerrariaPacketFactory.Rgb(220, 220, 130), withPrefix: true, cancellationToken).ConfigureAwait(false);
                    return CommandExecutionResult.Intercepted();
                }

                var password = string.Join(' ', context.Arguments.Skip(1));
                await session.SubmitPasswordAsync(password, cancellationToken).ConfigureAwait(false);
                return CommandExecutionResult.Intercepted();
            }

            case "l" or "list":
            {
                var visibleServers = configStore.Current.Servers.Where(s => s.Visible).Select(s => s.Name);
                var listText = $"{localization.Get("Command_AvailableServer")}{string.Join(", ", visibleServers)}";
                await session.SendChatAsync(listText, TerrariaPacketFactory.Rgb(177, 218, 228), withPrefix: true, cancellationToken).ConfigureAwait(false);
                return CommandExecutionResult.Intercepted();
            }

            case "b" or "back":
            {
                if (state.Phase is SessionPhase.TransferConnecting or SessionPhase.TransferAwaitingPassword or SessionPhase.TransferSyncing or SessionPhase.ReturningToLobby)
                {
                    await session.SendChatAsync(localization.Get("Command_IsSwitching"), TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, cancellationToken).ConfigureAwait(false);
                    return CommandExecutionResult.Intercepted();
                }

                if (state.Phase != SessionPhase.InServer)
                {
                    await session.SendChatAsync(localization.Get("Command_NotJoined"), TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, cancellationToken).ConfigureAwait(false);
                    return CommandExecutionResult.Intercepted();
                }

                session.TryRequestReturnBack("Player requested /mse back");
                return CommandExecutionResult.Intercepted();
            }

            case "t" or "tp":
            {
                if (state.Phase is SessionPhase.TransferConnecting or SessionPhase.TransferAwaitingPassword or SessionPhase.TransferSyncing or SessionPhase.ReturningToLobby)
                {
                    await session.SendChatAsync(localization.Get("Command_IsSwitching"), TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, cancellationToken).ConfigureAwait(false);
                    return CommandExecutionResult.Intercepted();
                }

                if (context.Arguments.Count < 2)
                {
                    await session.SendChatAsync(localization.Get("Help_Tp"), TerrariaPacketFactory.Rgb(220, 220, 130), withPrefix: true, cancellationToken).ConfigureAwait(false);
                    return CommandExecutionResult.Intercepted();
                }

                var query = context.Arguments[1];
                var target = configStore.Current.FindServer(query);
                if (target is null)
                {
                    await session.SendChatAsync(localization.Format("Command_ServerNotFound", query), TerrariaPacketFactory.Rgb(255, 80, 80), withPrefix: true, cancellationToken).ConfigureAwait(false);
                    return CommandExecutionResult.Intercepted();
                }

                await session.SendChatAsync(localization.Format("Command_Switch", target.Name), TerrariaPacketFactory.Rgb(220, 220, 130), withPrefix: true, cancellationToken).ConfigureAwait(false);
                session.TryRequestTransfer(target);
                return CommandExecutionResult.Intercepted();
            }

            default:
                await SendHelpAsync(session, cancellationToken).ConfigureAwait(false);
                return CommandExecutionResult.Intercepted();
        }
    }

    private ValueTask SendHelpAsync(ISessionHandle session, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine(localization.Get("Help_Tp"));
        sb.AppendLine(localization.Get("Help_Back"));
        sb.AppendLine(localization.Get("Help_List"));
        sb.Append(localization.Get("Help_Password"));
        return session.SendChatAsync(sb.ToString(), TerrariaPacketFactory.Rgb(220, 220, 130), withPrefix: false, ct);
    }
}

public sealed class ConsoleListCommand(IConfigStore configStore, ISessionRegistry sessions, IProxyLogger logger) : ICommand
{
    public string Name => "list";
    public IReadOnlyList<string> Aliases => ["ls"];
    public CommandSourceKind AllowedSources => CommandSourceKind.Console;
    public string Usage => "list";
    public string Description => "List online players grouped by server.";

    public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var snapshots = sessions.GetSnapshots();
        var sb = new StringBuilder();
        sb.AppendLine($"Online ({snapshots.Length}):");

        var lobbyPlayers = snapshots.Where(s => s.CurrentServerName is null).Select(s => s.PlayerName).ToArray();
        sb.AppendLine($"  [Lobby] ({lobbyPlayers.Length}): {string.Join(", ", lobbyPlayers)}");

        foreach (var srv in configStore.Current.Servers)
        {
            var players = snapshots
                .Where(s => string.Equals(s.CurrentServerName, srv.Name, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.PlayerName)
                .ToArray();
            sb.AppendLine($"  [{srv.Name}] ({players.Length}): {string.Join(", ", players)}");
        }

        logger.Info(sb.ToString().TrimEnd(), "Console");
        return ValueTask.FromResult(CommandExecutionResult.Intercepted());
    }
}

public sealed class ConsoleKickCommand(ISessionRegistry sessions, IProxyLogger logger) : ICommand
{
    public string Name => "kick";
    public CommandSourceKind AllowedSources => CommandSourceKind.Console;
    public string Usage => "kick <player> [reason]";
    public string Description => "Disconnect an online player.";

    public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Count < 1)
        {
            logger.Warn($"Usage: {Usage}", "Console");
            return CommandExecutionResult.Intercepted();
        }

        var target = sessions.FindByName(context.Arguments[0]);
        if (target is null)
        {
            logger.Warn($"Player '{context.Arguments[0]}' not found.", "Console");
            return CommandExecutionResult.Intercepted();
        }

        var reason = context.Arguments.Count > 1 ? string.Join(' ', context.Arguments.Skip(1)) : "Kicked by console.";
        await target.DisconnectAsync(reason, cancellationToken).ConfigureAwait(false);
        logger.Info($"Kicked [{target.PlayerName}]: {reason}", "Console");
        return CommandExecutionResult.Intercepted();
    }
}

public sealed class ConsoleBroadcastCommand(
    Func<string, Microsoft.Xna.Framework.Color, string?, bool, CancellationToken, ValueTask> broadcast,
    IProxyLogger logger) : ICommand
{
    public string Name => "broadcast";
    public IReadOnlyList<string> Aliases => ["bc"];
    public CommandSourceKind AllowedSources => CommandSourceKind.Console;
    public string Usage => "broadcast [-s <server>] <message>";
    public string Description => "Broadcast a message to all players or a specific server.";

    public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (context.Arguments.Count == 0)
        {
            logger.Warn($"Usage: {Usage}", "Console");
            return CommandExecutionResult.Intercepted();
        }

        string? serverFilter = null;
        string message;
        if (context.Arguments.Count >= 3 && string.Equals(context.Arguments[0], "-s", StringComparison.OrdinalIgnoreCase))
        {
            serverFilter = context.Arguments[1];
            message = string.Join(' ', context.Arguments.Skip(2));
        }
        else
        {
            message = string.Join(' ', context.Arguments);
        }

        await broadcast(message, TerrariaPacketFactory.Rgb(255, 220, 120), serverFilter, true, cancellationToken).ConfigureAwait(false);
        logger.Info($"Broadcast{(serverFilter is null ? "" : $" [{serverFilter}]")}: {message}", "Console");
        return CommandExecutionResult.Intercepted();
    }
}

public sealed class ConsoleReloadCommand(
    IConfigStore configStore,
    ILocalizationStore localization,
    IMotdStore motd,
    IProxyLogger logger) : ICommand
{
    public string Name => "reload";
    public CommandSourceKind AllowedSources => CommandSourceKind.Console;
    public string Usage => "reload";
    public string Description => "Reload Config.json, Localization.json, and MOTD.txt.";

    public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        configStore.Reload();
        localization.Reload();
        motd.Reload();
        logger.Success("Reloaded configuration, localization, and MOTD.", "Console");
        return ValueTask.FromResult(CommandExecutionResult.Intercepted());
    }
}

public sealed class ConsoleReloadPluginsCommand(PluginHost pluginHost, IProxyLogger logger) : ICommand
{
    public string Name => "reloadplugin";
    public IReadOnlyList<string> Aliases => ["rp"];
    public CommandSourceKind AllowedSources => CommandSourceKind.Console;
    public string Usage => "reloadplugin";
    public string Description => "Unload and reload all plugins in isolated collectible AssemblyLoadContexts.";

    public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await pluginHost.ReloadAllAsync(cancellationToken).ConfigureAwait(false);
        var loaded = pluginHost.GetLoadedPlugins();
        logger.Success($"Plugins reloaded ({loaded.Length} active).", "Console");
        return CommandExecutionResult.Intercepted();
    }
}

public sealed class ConsoleTestCommand(IConfigStore configStore, IServerProbeService probes, IProxyLogger logger) : ICommand
{
    public string Name => "test";
    public CommandSourceKind AllowedSources => CommandSourceKind.Console;
    public string Usage => "test [server]";
    public string Description => "Concurrently probe connectivity to all or one configured backend server.";

    public async ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(configStore.Current.SwitchTimeoutMs);
        IReadOnlyList<ProbeResult> results;

        if (context.Arguments.Count >= 1)
        {
            var server = configStore.Current.FindServer(context.Arguments[0]);
            if (server is null)
            {
                logger.Warn($"Server '{context.Arguments[0]}' not found.", "Probe");
                return CommandExecutionResult.Intercepted();
            }

            results = [await probes.ProbeAsync(server, timeout, cancellationToken).ConfigureAwait(false)];
        }
        else
        {
            results = await probes.ProbeAllAsync(configStore.Current.Servers, timeout, cancellationToken).ConfigureAwait(false);
        }

        var okCount = 0;
        foreach (var r in results)
        {
            if (r.IsAvailable)
            {
                okCount++;
                logger.Success($"[{r.Server.Name}] OK ({r.Latency.TotalMilliseconds:F0}ms)", "Probe");
            }
            else
            {
                logger.Warn($"[{r.Server.Name}] FAILED ({r.Latency.TotalMilliseconds:F0}ms): {r.FailureReason}", "Probe");
            }
        }

        logger.Info($"Connectivity probe complete: {okCount}/{results.Count} available.", "Probe");
        return CommandExecutionResult.Intercepted();
    }
}

public sealed class ConsoleHelpCommand(CommandRouter router, IProxyLogger logger) : ICommand
{
    public string Name => "help";
    public IReadOnlyList<string> Aliases => ["?"];
    public CommandSourceKind AllowedSources => CommandSourceKind.Console;
    public string Usage => "help";
    public string Description => "List all available console commands.";

    public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available console commands:");
        foreach (var cmd in router.GetRegisteredCommands().Where(c => (c.AllowedSources & CommandSourceKind.Console) != 0).OrderBy(c => c.Name))
        {
            sb.AppendLine($"  {cmd.Usage,-34} - {cmd.Description}");
        }
        logger.Info(sb.ToString().TrimEnd(), "Console");
        return ValueTask.FromResult(CommandExecutionResult.Intercepted());
    }
}

public sealed class ConsoleStopCommand(IProxyLogger logger) : ICommand
{
    public string Name => "stop";
    public IReadOnlyList<string> Aliases => ["exit", "quit"];
    public CommandSourceKind AllowedSources => CommandSourceKind.Console;
    public string Usage => "stop";
    public string Description => "Gracefully shut down MultiSEngine.";

    public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        logger.Info("Shutdown requested via console.", "Host");
        return ValueTask.FromResult(CommandExecutionResult.Intercepted(requestShutdown: true));
    }
}
