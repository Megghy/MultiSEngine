using System.Net;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Logging;
using MultiSEngine.Core.Security;
using MultiSEngine.Runtime.Commands;
using MultiSEngine.Runtime.Extensions;
using MultiSEngine.Runtime.Infrastructure;

namespace MultiSEngine.Tests;

internal sealed class NullProxyLogger : IProxyLogger
{
    public List<string> Messages { get; } = [];

    public void Log(ProxyLogLevel level, string category, string message, Exception? exception = null)
    {
        lock (Messages)
            Messages.Add($"[{level}] {message}");
    }
}

public sealed class ConfigCommandAndHooksTests
{
    [Fact]
    public void ConfigStore_NormalizesDuplicatesEmptyNamesAndPrioritizesExactServerLookup()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mse-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var logger = new NullProxyLogger();
            var path = Path.Combine(dir, "Config.json");
            var store = new ConfigStore(path, logger, new ProxyConfig
            {
                Servers =
                [
                    new ServerDescriptor { Name = "s10", Host = "127.0.0.1", Port = 7771 },
                    new ServerDescriptor { Name = "s1", ShortName = "one", Host = "127.0.0.1", Port = 7772 },
                    new ServerDescriptor { Name = "s1", Host = "127.0.0.1", Port = 7773 }, // duplicate
                    new ServerDescriptor { Name = "   ", Host = "127.0.0.1", Port = 7774 }, // empty
                ],
            });

            Assert.Equal(2, store.Current.Servers.Length);
            // Exact match "s1" must win over earlier prefix match "s10".
            Assert.Equal("s1", store.Current.FindServer("s1")?.Name);
            Assert.Equal("s1", store.Current.FindServer("one")?.Name);
            Assert.Equal("s10", store.Current.FindServer("s10")?.Name);

            store.Save(store.Current);
            Assert.True(File.Exists(path));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ParseListenAddress_LocalhostBindsLoopbackNotAny()
    {
        Assert.Equal(IPAddress.Loopback, NetworkAddressResolver.ParseListenAddress("localhost"));
        Assert.Equal(IPAddress.Any, NetworkAddressResolver.ParseListenAddress("0.0.0.0"));
        Assert.Equal(IPAddress.Parse("127.0.0.2"), NetworkAddressResolver.ParseListenAddress("127.0.0.2"));
    }

    [Fact]
    public void CommandTokenizer_HandlesQuotesEscapesAndCrLfCleanly()
    {
        Assert.True(CommandTokenizer.TryParse("/mse password \"hello \\\"world\\\"\" \r\n", requireLeadingSlash: true, out var cmd, out var args));
        Assert.Equal("mse", cmd);
        Assert.Equal(["password", "hello \"world\""], args);

        Assert.False(CommandTokenizer.TryParse(null, requireLeadingSlash: false, out _, out _));
        Assert.False(CommandTokenizer.TryParse("   ", requireLeadingSlash: false, out _, out _));
    }

    [Fact]
    public async Task CommandRouter_FailedPlayerCommandNeverLeaksToBackendAndPluginConsoleCommandNotHijacked()
    {
        var logger = new NullProxyLogger();
        var loc = new LocalizationStore(null, logger);
        var router = new CommandRouter(logger, loc);

        router.Register(new ThrowingPlayerCommand());
        var pluginExecuted = false;
        router.Register(new CustomConsoleCommand(() => pluginExecuted = true));

        // Failed player command MUST remain Intercepted (ForwardToServer == false) so sensitive args never leak.
        var failResult = await router.DispatchAsync(CommandSourceKind.Player, "/secretcmd my-secret-password");
        Assert.True(failResult.Handled);
        Assert.False(failResult.ForwardToServer);

        // Plugin console command must be invoked directly without being swallowed by any catch-all console handler.
        var consoleResult = await router.DispatchAsync(CommandSourceKind.Console, "myplugin arg1");
        Assert.True(consoleResult.Handled);
        Assert.True(pluginExecuted);
    }

    [Fact]
    public async Task HookBus_IsolatesSubscriberExceptionsSoSubsequentHandlersStillRun()
    {
        var logger = new NullProxyLogger();
        var hooks = new HookBus(logger);

        var secondRan = false;
        hooks.SubscribeChat((_, _) => throw new InvalidOperationException("Boom from plugin 1"));
        hooks.SubscribeChat((ev, _) =>
        {
            secondRan = true;
            ev.Message = "sanitized";
            return ValueTask.CompletedTask;
        });

        var chatEvent = new PlayerChatEvent(null!, "Say", "original");
        await hooks.DispatchChatAsync(chatEvent, CancellationToken.None);

        Assert.True(secondRan);
        Assert.Equal("sanitized", chatEvent.Message);
    }

    private sealed class ThrowingPlayerCommand : ICommand
    {
        public string Name => "secretcmd";
        public CommandSourceKind AllowedSources => CommandSourceKind.Player;
        public string Usage => "/secretcmd";
        public string Description => "Always throws.";
        public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated failure");
    }

    private sealed class CustomConsoleCommand(Action onExecute) : ICommand
    {
        public string Name => "myplugin";
        public CommandSourceKind AllowedSources => CommandSourceKind.Console;
        public string Usage => "myplugin";
        public string Description => "Custom console command.";
        public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            onExecute();
            return ValueTask.FromResult(CommandExecutionResult.Intercepted());
        }
    }
}
