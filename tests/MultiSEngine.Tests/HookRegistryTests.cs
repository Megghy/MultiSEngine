using System.Reflection;
using MultiSEngine.Application.Extensions;
using MultiSEngine.Events;

namespace MultiSEngine.Tests;

public sealed class HookRegistryTests
{
    [Fact]
    public void Reset_ClearsRegisteredHooks()
    {
        static void Handler(PlayerJoinEventArgs _) { }

        Hooks.PlayerJoin += Handler;

        HookRegistry.Reset();

        var field = typeof(Hooks).GetField("PlayerJoin", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.Null(field?.GetValue(null));
    }
}
