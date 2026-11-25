using System.Net;
using System.Net.Sockets;
using Microsoft.Xna.Framework;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Security;
using MultiSEngine.Core.Sessions;
using MultiSEngine.Core.Transfers;
using MultiSEngine.Protocol.CustomPackets;
using MultiSEngine.Protocol.Framing;
using MultiSEngine.Protocol.Terraria;
using MultiSEngine.Runtime.Commands;
using MultiSEngine.Runtime.Extensions;
using MultiSEngine.Runtime.Infrastructure;
using MultiSEngine.Runtime.Probing;
using MultiSEngine.Runtime.Sessions;

namespace MultiSEngine.Runtime.Hosting;

public sealed record MultiSEngineHostOptions
{
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;
    public ProxyConfig? InitialConfig { get; init; }
    public bool EnableConsoleLogging { get; init; } = true;
    public bool EnableFileLogging { get; init; } = true;
    public bool EnableTcpListener { get; init; } = true;
    public bool EnablePlugins { get; init; } = true;
    public TimeProvider? TimeProvider { get; init; }
}

/// <summary>
/// Fully self-contained, instantiable MultiSEngine host with zero global mutable static state.
/// Supports strict reverse-order graceful shutdown and parallel isolated test instances.
/// </summary>
public sealed class MultiSEngineHost : IAsyncDisposable
{
    private sealed class HostServicesAdapter(MultiSEngineHost host) : ISessionHostServices
    {
        public IConfigStore ConfigStore => host.ConfigStore;
        public ILocalizationStore Localization => host.Localization;
        public IMotdStore Motd => host.Motd;
        public ISessionRegistry Registry => host.Sessions;
        public HookBus Hooks => host.Hooks;
        public MiddlewarePipeline Middlewares => host.Middlewares;
        public CustomPacketRegistry CustomPackets => host.CustomPackets;
        public CustomPacketCodec CustomPacketCodec => host.CustomPacketCodec;
        public CommandRouter Commands => host.Commands;
        public IProxyLogger Logger => host.Logger;
        public ValueTask BroadcastAsync(string text, Color color, string? serverFilter = null, bool withPrefix = true, CancellationToken cancellationToken = default)
            => host.BroadcastAsync(text, color, serverFilter, withPrefix, cancellationToken);
    }

    private readonly MultiSEngineHostOptions _options;
    private readonly AsyncProxyLogger _asyncLogger;
    private readonly HostServicesAdapter _services;
    private readonly List<IDisposable> _builtInRegistrations = [];
    private readonly CancellationTokenSource _shutdownCts = new();

    private TcpListener? _listener;
    private Task? _acceptLoopTask;
    private int _started;
    private int _stopped;

    public MultiSEngineHost(MultiSEngineHostOptions? options = null)
    {
        _options = options ?? new MultiSEngineHostOptions();
        var workDir = _options.WorkingDirectory;

        _asyncLogger = new AsyncProxyLogger(
            _options.EnableFileLogging ? Path.Combine(workDir, "Logs") : null,
            enableConsole: _options.EnableConsoleLogging);
        Logger = _asyncLogger;

        ConfigStore = new ConfigStore(Path.Combine(workDir, "Config.json"), Logger, _options.InitialConfig);
        Localization = new LocalizationStore(_options.InitialConfig is null ? Path.Combine(workDir, "Localization.json") : null, Logger);
        Motd = new MotdStore(_options.InitialConfig is null ? Path.Combine(workDir, "MOTD.txt") : null, Logger);

        Sessions = new SessionRegistry();
        Hooks = new HookBus(Logger);
        Middlewares = new MiddlewarePipeline(Logger);
        CustomPackets = new CustomPacketRegistry();
        CustomPacketCodec = new CustomPacketCodec(_options.TimeProvider);
        Commands = new CommandRouter(Logger, Localization);
        Probes = new ServerProbeService(ConfigStore, Logger);

        Plugins = new PluginHost(
            Path.Combine(workDir, "Plugins"),
            Logger,
            ConfigStore,
            Localization,
            Sessions,
            Probes,
            Hooks,
            Middlewares,
            Commands,
            CustomPackets,
            BroadcastAsync);

        _services = new HostServicesAdapter(this);
        RegisterBuiltInExtensions();
    }

    public IProxyLogger Logger { get; }
    public IConfigStore ConfigStore { get; }
    public ILocalizationStore Localization { get; }
    public IMotdStore Motd { get; }
    public ISessionRegistry Sessions { get; }
    public HookBus Hooks { get; }
    public MiddlewarePipeline Middlewares { get; }
    public CustomPacketRegistry CustomPackets { get; }
    public CustomPacketCodec CustomPacketCodec { get; }
    public CommandRouter Commands { get; }
    public IServerProbeService Probes { get; }
    public PluginHost Plugins { get; }

    public IPEndPoint? BoundEndPoint => _listener?.LocalEndpoint as IPEndPoint;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            throw new InvalidOperationException("MultiSEngineHost has already been started.");

        // Touch LobbyProtocol once during startup so static spawn section is ready before first player joins.
        _ = LobbyProtocol.SpawnSquarePacket;

        if (_options.EnablePlugins)
            await Plugins.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        if (_options.EnableTcpListener)
        {
            var cfg = ConfigStore.Current;
            var ip = NetworkAddressResolver.ParseListenAddress(cfg.ListenIP);
            _listener = new TcpListener(ip, cfg.ListenPort);
            _listener.Start();
            _acceptLoopTask = AcceptConnectionsAsync(_listener, _shutdownCts.Token);
            Logger.Success($"MultiSEngine listening on {BoundEndPoint}.", "Host");
        }
    }

    /// <summary>
    /// Attaches an already-created FramedConnection (e.g. an in-memory DuplexPipe for deterministic tests or benchmarks).
    /// </summary>
    public PlayerSessionActor AttachConnection(FramedConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var actor = new PlayerSessionActor(connection, _services);
        actor.Start();
        return actor;
    }

    public ValueTask<CommandExecutionResult> ExecuteConsoleCommandAsync(string? line, CancellationToken cancellationToken = default)
        => Commands.DispatchAsync(CommandSourceKind.Console, line, cancellationToken: cancellationToken);

    /// <summary>
    /// Non-blocking fan-out broadcast: serializes the chat packet once and sends concurrently so one slow client never stalls others.
    /// </summary>
    public async ValueTask BroadcastAsync(
        string text,
        Color color,
        string? serverFilter = null,
        bool withPrefix = true,
        CancellationToken cancellationToken = default)
    {
        var fullText = withPrefix ? $"{Localization.Get("Prefix")}{text}" : text;
        var frame = PacketFrameCodec.SerializeToArray(TerrariaPacketFactory.CreateServerChat(fullText, color), fromServer: true);
        var sessions = Sessions.GetSessions();
        if (sessions.IsEmpty)
            return;

        List<Task>? pending = null;
        foreach (var session in sessions)
        {
            if (session.CurrentState.Phase is SessionPhase.Handshaking or SessionPhase.Disconnecting or SessionPhase.Disconnected)
                continue;

            if (serverFilter is not null)
            {
                var current = session.CurrentServer?.Name;
                if (!string.Equals(current, serverFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var sendTask = session.SendRawToClientAsync(frame, cancellationToken);
            if (!sendTask.IsCompletedSuccessfully)
            {
                pending ??= [];
                pending.Add(sendTask.AsTask());
            }
        }

        if (pending is not null)
            await Task.WhenAll(pending).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        Logger.Info("Stopping MultiSEngine host...", "Host");

        // 1. Stop accepting new TCP connections first.
        try { await _shutdownCts.CancelAsync().ConfigureAwait(false); } catch (ObjectDisposedException) { }
        try { _listener?.Stop(); } catch { }
        if (_acceptLoopTask is { } acceptTask)
        {
            try { await acceptTask.ConfigureAwait(false); } catch { }
        }

        // 2. Gracefully disconnect all online sessions in parallel.
        var activeSessions = Sessions.GetSessions();
        if (!activeSessions.IsEmpty)
        {
            var disconnectTasks = activeSessions
                .Select(s => s.DisconnectAsync("Server closed.", cancellationToken).AsTask())
                .ToArray();
            try
            {
                await Task.WhenAll(disconnectTasks).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        // 3. Unload plugins and dispose built-in registrations.
        await Plugins.DisposeAsync().ConfigureAwait(false);
        foreach (var reg in _builtInRegistrations)
            reg.Dispose();
        _builtInRegistrations.Clear();

        Logger.Info("MultiSEngine host stopped.", "Host");

        // 4. Flush and close logger last so shutdown logs are persisted cleanly.
        await _asyncLogger.DisposeAsync().ConfigureAwait(false);
        _shutdownCts.Dispose();
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient? tcp = null;
            try
            {
                tcp = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var cfg = ConfigStore.Current;
                if (Sessions.Count >= cfg.Limits.MaxConnections)
                {
                    Logger.Warn($"Rejected connection from {tcp.Client.RemoteEndPoint}: MaxConnections ({cfg.Limits.MaxConnections}) reached.", "Listener");
                    tcp.Close();
                    continue;
                }

                var framed = FramedConnection.FromTcpClient(tcp, new FramingOptions
                {
                    PauseWriterThreshold = cfg.Limits.SendPipePauseThresholdBytes,
                    ResumeWriterThreshold = cfg.Limits.SendPipeResumeThresholdBytes,
                    SendFlushTimeout = TimeSpan.FromMilliseconds(cfg.Limits.SendFlushTimeoutMs),
                    DisableTcpDelay = cfg.DisableTcpDelay,
                });
                tcp = null;

                Logger.Text($"{framed.RemoteEndPoint} connected.", "Listener");
                AttachConnection(framed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                tcp?.Dispose();
                Logger.Error("Error accepting incoming TCP connection.", ex, "Listener");
            }
        }
    }

    private void RegisterBuiltInExtensions()
    {
        _builtInRegistrations.Add(CustomPackets.Register(new ExecuteCommandCustomPacketHandler(Sessions, Commands, Logger)));
        _builtInRegistrations.Add(CustomPackets.Register(new SwitchServerCustomPacketHandler(ConfigStore, Logger)));
        _builtInRegistrations.Add(Commands.Register(new MsePlayerCommand(ConfigStore, Localization)));
        _builtInRegistrations.Add(Commands.Register(new ConsoleListCommand(ConfigStore, Sessions, Logger)));
        _builtInRegistrations.Add(Commands.Register(new ConsoleKickCommand(Sessions, Logger)));
        _builtInRegistrations.Add(Commands.Register(new ConsoleBroadcastCommand(BroadcastAsync, Logger)));
        _builtInRegistrations.Add(Commands.Register(new ConsoleReloadCommand(ConfigStore, Localization, Motd, Logger)));
        _builtInRegistrations.Add(Commands.Register(new ConsoleReloadPluginsCommand(Plugins, Logger)));
        _builtInRegistrations.Add(Commands.Register(new ConsoleTestCommand(ConfigStore, Probes, Logger)));
        _builtInRegistrations.Add(Commands.Register(new ConsoleHelpCommand(Commands, Logger)));
        _builtInRegistrations.Add(Commands.Register(new ConsoleStopCommand(Logger)));
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
