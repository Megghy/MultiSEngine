using MultiSEngine.Core.Extensions;
using MultiSEngine.Core.Security;

namespace MultiSEngine.TestPlugin;

public sealed class SampleReloadablePlugin : IMsePlugin
{
    public PluginMetadata Metadata { get; } = new(
        Id: "sample.reloadable",
        Name: "Sample Reloadable Plugin",
        Version: new Version(1, 0, 0),
        Author: "Test",
        Description: "Verifies collectible ALC unloading and managed registration cleanup.");

    public ValueTask InitializeAsync(IPluginContext context, CancellationToken cancellationToken)
    {
        context.RegisterCommand(new PingPluginCommand(context));
        context.OnPlayerChat((ev, _) =>
        {
            if (ev.Message == "ping-hook")
                ev.Message = "pong-hook";
            return ValueTask.CompletedTask;
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class PingPluginCommand(IPluginContext context) : ICommand
    {
        public string Name => "pluginping";
        public CommandSourceKind AllowedSources => CommandSourceKind.Console;
        public string Usage => "pluginping";
        public string Description => "Test command registered by SampleReloadablePlugin.";

        public ValueTask<CommandExecutionResult> ExecuteAsync(CommandContext cmd, CancellationToken cancellationToken)
        {
            context.Logger.Info("pluginping-ok", "TestPlugin");
            return ValueTask.FromResult(CommandExecutionResult.Intercepted());
        }
    }
}
