using MultiSEngine.Application.Extensions;
using MultiSEngine.Commands;
using MultiSEngine.Models;
using MultiSEngine.Protocol.CustomData;

namespace MultiSEngine.Tests;

public sealed class ExtensionRegistryTests
{
    [Fact]
    public void CommandRegistry_LoadFromAssemblies_DiscoversCommandTypes()
    {
        var registry = new CommandRegistry();

        registry.LoadFromAssemblies([typeof(ExtensionRegistryTests).Assembly]);

        Assert.Contains(registry.Snapshot(), command => command.GetType() == typeof(RegistryCommand));
    }

    [Fact]
    public void CustomPacketRegistry_LoadFromAssemblies_DiscoversPacketTypes()
    {
        var registry = new CustomPacketRegistry();

        registry.LoadFromAssemblies([typeof(SyncIP).Assembly]);

        Assert.True(registry.TryGetValue("MultiSEngine.SyncIP", out var packetType));
        Assert.Equal(typeof(SyncIP), packetType);
    }

    private sealed class RegistryCommand : CommandDispatcher.CmdBase
    {
        public override string Name => "registry";

        public override ValueTask<bool> Execute(ClientData client, string cmdName, string[] parma)
            => ValueTask.FromResult(true);
    }

}
