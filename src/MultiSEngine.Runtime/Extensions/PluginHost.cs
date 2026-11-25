using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.Xna.Framework;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Sessions;
using MultiSEngine.Core.Transfers;
using MultiSEngine.Runtime.Commands;

namespace MultiSEngine.Runtime.Extensions;

internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string name, string pluginMainAssemblyPath)
        : base(name, isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginMainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Always share host-provided core & protocol assemblies from the default context so interface types match.
        var name = assemblyName.Name;
        if (name is "MultiSEngine.Core" or "TrProtocol" or "MultiSEngine.Protocol")
            return null;

        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (resolvedPath is not null)
            return LoadFromAssemblyPath(resolvedPath);

        return null;
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : nint.Zero;
    }
}

public sealed record LoadedPluginInfo(PluginMetadata Metadata, string AssemblyPath);

public sealed class PluginHost : IAsyncDisposable
{
    private sealed class LoadedPluginScope(
        PluginLoadContext loadContext,
        IMsePlugin instance,
        string assemblyPath,
        ScopedPluginContext context)
    {
        public PluginLoadContext LoadContext { get; } = loadContext;
        public IMsePlugin Instance { get; } = instance;
        public string AssemblyPath { get; } = assemblyPath;
        public ScopedPluginContext Context { get; } = context;
    }

    private sealed class ScopedPluginContext : IPluginContext, IDisposable
    {
        private readonly HookBus _hooks;
        private readonly MiddlewarePipeline _middlewares;
        private readonly CommandRouter _commands;
        private readonly CustomPacketRegistry _customPackets;
        private readonly Func<string, Color, string?, bool, CancellationToken, ValueTask> _broadcast;
        private readonly CancellationTokenSource _unloadCts = new();
        private readonly Lock _regLock = new();
        private readonly List<IDisposable> _registrations = [];
        private bool _disposed;

        public ScopedPluginContext(
            IProxyLogger logger,
            IConfigStore config,
            ILocalizationStore localization,
            ISessionRegistry sessions,
            IServerProbeService probes,
            HookBus hooks,
            MiddlewarePipeline middlewares,
            CommandRouter commands,
            CustomPacketRegistry customPackets,
            Func<string, Color, string?, bool, CancellationToken, ValueTask> broadcast)
        {
            Logger = logger;
            Config = config;
            Localization = localization;
            Sessions = sessions;
            Probes = probes;
            _hooks = hooks;
            _middlewares = middlewares;
            _commands = commands;
            _customPackets = customPackets;
            _broadcast = broadcast;
        }

        public IProxyLogger Logger { get; }
        public IConfigStore Config { get; }
        public ILocalizationStore Localization { get; }
        public ISessionRegistry Sessions { get; }
        public IServerProbeService Probes { get; }
        public CancellationToken UnloadToken => _unloadCts.Token;

        public IDisposable RegisterPacketMiddleware(IPacketMiddleware middleware) => Track(_middlewares.Register(middleware));
        public IDisposable RegisterCommand(ICommand command) => Track(_commands.Register(command));
        public IDisposable RegisterCustomPacket(ICustomPacketDefinition definition) => Track(_customPackets.Register(definition));

        public IDisposable OnPlayerJoining(Func<PlayerJoiningEvent, CancellationToken, ValueTask> handler) => Track(_hooks.SubscribeJoining(handler));
        public IDisposable OnPlayerJoined(Func<PlayerJoinedEvent, CancellationToken, ValueTask> handler) => Track(_hooks.SubscribeJoined(handler));
        public IDisposable OnPlayerLeft(Func<PlayerLeftEvent, CancellationToken, ValueTask> handler) => Track(_hooks.SubscribeLeft(handler));
        public IDisposable OnPreSwitch(Func<PreSwitchEvent, CancellationToken, ValueTask> handler) => Track(_hooks.SubscribePreSwitch(handler));
        public IDisposable OnPostSwitch(Func<PostSwitchEvent, CancellationToken, ValueTask> handler) => Track(_hooks.SubscribePostSwitch(handler));
        public IDisposable OnPlayerChat(Func<PlayerChatEvent, CancellationToken, ValueTask> handler) => Track(_hooks.SubscribeChat(handler));
        public IDisposable OnCustomPacketReceived(Func<CustomPacketReceivedEvent, CancellationToken, ValueTask> handler) => Track(_hooks.SubscribeCustomPacket(handler));

        public ValueTask BroadcastAsync(string text, Color color, string? serverFilter = null, bool withPrefix = true, CancellationToken cancellationToken = default)
            => _broadcast(text, color, serverFilter, withPrefix, cancellationToken);

        public void CancelAndRevokeAll()
        {
            try { _unloadCts.Cancel(); } catch (ObjectDisposedException) { }

            IDisposable[] snapshot;
            lock (_regLock)
            {
                snapshot = [.. _registrations];
                _registrations.Clear();
            }

            foreach (var reg in snapshot)
            {
                try { reg.Dispose(); } catch { }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            CancelAndRevokeAll();
            _unloadCts.Dispose();
        }

        private IDisposable Track(IDisposable registration)
        {
            lock (_regLock)
            {
                if (_disposed)
                {
                    registration.Dispose();
                    return registration;
                }

                _registrations.Add(registration);
            }

            return registration;
        }
    }

    private readonly string _pluginsDirectory;
    private readonly IProxyLogger _logger;
    private readonly IConfigStore _config;
    private readonly ILocalizationStore _localization;
    private readonly ISessionRegistry _sessions;
    private readonly IServerProbeService _probes;
    private readonly HookBus _hooks;
    private readonly MiddlewarePipeline _middlewares;
    private readonly CommandRouter _commands;
    private readonly CustomPacketRegistry _customPackets;
    private readonly Func<string, Color, string?, bool, CancellationToken, ValueTask> _broadcast;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly List<LoadedPluginScope> _loaded = [];

    public PluginHost(
        string pluginsDirectory,
        IProxyLogger logger,
        IConfigStore config,
        ILocalizationStore localization,
        ISessionRegistry sessions,
        IServerProbeService probes,
        HookBus hooks,
        MiddlewarePipeline middlewares,
        CommandRouter commands,
        CustomPacketRegistry customPackets,
        Func<string, Color, string?, bool, CancellationToken, ValueTask> broadcast)
    {
        _pluginsDirectory = pluginsDirectory;
        _logger = logger;
        _config = config;
        _localization = localization;
        _sessions = sessions;
        _probes = probes;
        _hooks = hooks;
        _middlewares = middlewares;
        _commands = commands;
        _customPackets = customPackets;
        _broadcast = broadcast;
    }

    public ImmutableArray<LoadedPluginInfo> GetLoadedPlugins()
    {
        _lifecycleLock.Wait();
        try
        {
            return [.. _loaded.Select(s => new LoadedPluginInfo(s.Instance.Metadata, s.AssemblyPath))];
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task LoadAllAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await LoadAllLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<IReadOnlyList<WeakReference>> UnloadAllAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await UnloadAllLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task<IReadOnlyList<WeakReference>> ReloadAllAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var oldContexts = await UnloadAllLockedAsync().ConfigureAwait(false);
            await LoadAllLockedAsync(cancellationToken).ConfigureAwait(false);
            return oldContexts;
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task LoadAllLockedAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_pluginsDirectory))
            return;

        Directory.CreateDirectory(_pluginsDirectory);
        var dllFiles = Directory.GetFiles(_pluginsDirectory, "*.dll", SearchOption.TopDirectoryOnly);

        foreach (var dllPath in dllFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TryLoadPluginAssemblyAsync(dllPath, cancellationToken).ConfigureAwait(false);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task TryLoadPluginAssemblyAsync(string dllPath, CancellationToken cancellationToken)
    {
        PluginLoadContext? alc = null;
        try
        {
            var fullPath = Path.GetFullPath(dllPath);
            alc = new PluginLoadContext($"Plugin:{Path.GetFileNameWithoutExtension(fullPath)}:{Guid.NewGuid():N}", fullPath);

            // Read into memory stream so the file on disk is never locked during hot-reload on Windows.
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            using var peStream = new MemoryStream(bytes, writable: false);
            var pdbPath = Path.ChangeExtension(fullPath, ".pdb");
            Assembly assembly;
            if (File.Exists(pdbPath))
            {
                var pdbBytes = await File.ReadAllBytesAsync(pdbPath, cancellationToken).ConfigureAwait(false);
                using var pdbStream = new MemoryStream(pdbBytes, writable: false);
                assembly = alc.LoadFromStream(peStream, pdbStream);
            }
            else
            {
                assembly = alc.LoadFromStream(peStream);
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                types = [.. rtle.Types.Where(t => t is not null)!];
            }

            var pluginTypes = types
                .Where(t => t is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false } && typeof(IMsePlugin).IsAssignableFrom(t))
                .ToArray();

            if (pluginTypes.Length == 0)
            {
                alc.Unload();
                return;
            }

            var loadedAny = false;
            foreach (var pluginType in pluginTypes)
            {
                try
                {
                    if (Activator.CreateInstance(pluginType) is not IMsePlugin plugin)
                        continue;

                    var context = new ScopedPluginContext(
                        _logger, _config, _localization, _sessions, _probes,
                        _hooks, _middlewares, _commands, _customPackets, _broadcast);

                    try
                    {
                        await plugin.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
                        _loaded.Add(new LoadedPluginScope(alc, plugin, fullPath, context));
                        loadedAny = true;
                        _logger.Info($"Loaded plugin [{plugin.Metadata.Name}] v{plugin.Metadata.Version} ({plugin.Metadata.Id}).", "Plugins");
                    }
                    catch (Exception initEx)
                    {
                        context.Dispose();
                        try { await plugin.DisposeAsync().ConfigureAwait(false); } catch { }
                        _logger.Error($"Plugin '{pluginType.FullName}' failed during InitializeAsync.", initEx, "Plugins");
                    }
                }
                catch (Exception ctorEx)
                {
                    _logger.Error($"Failed to instantiate plugin type '{pluginType.FullName}' from '{fullPath}'.", ctorEx, "Plugins");
                }
            }

            if (!loadedAny)
                alc.Unload();
        }
        catch (BadImageFormatException)
        {
            // Ignore native/non-.NET DLLs placed in Plugins directory.
            alc?.Unload();
        }
        catch (Exception ex)
        {
            alc?.Unload();
            _logger.Error($"Failed to load plugin assembly '{dllPath}'.", ex, "Plugins");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<IReadOnlyList<WeakReference>> UnloadAllLockedAsync()
    {
        var scopes = _loaded.ToArray();
        _loaded.Clear();

        var contexts = new HashSet<PluginLoadContext>();
        foreach (var scope in scopes)
        {
            // Step 1: revoke every host reference BEFORE calling plugin DisposeAsync & ALC Unload.
            scope.Context.CancelAndRevokeAll();

            // Step 2: dispose plugin with full error isolation.
            try
            {
                await scope.Instance.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"Plugin '{scope.Instance.Metadata.Id}' threw during DisposeAsync.", ex, "Plugins");
            }
            finally
            {
                scope.Context.Dispose();
            }

            contexts.Add(scope.LoadContext);
        }

        // Step 3: unload every collectible ALC and return weak references for collectibility verification.
        var weakRefs = new List<WeakReference>(contexts.Count);
        foreach (var alc in contexts)
        {
            weakRefs.Add(new WeakReference(alc, trackResurrection: true));
            alc.Unload();
        }

        return weakRefs;
    }

    public async ValueTask DisposeAsync()
    {
        await UnloadAllAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
    }
}
