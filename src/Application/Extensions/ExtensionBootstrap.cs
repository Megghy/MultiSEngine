using System.Reflection;
using MultiSEngine.Plugins;

namespace MultiSEngine.Application.Extensions;

public static class ExtensionBootstrap
{
    [AutoInit("Loading plugins and extensions.", order: 50)]
    internal static void Init()
    {
        PluginManager.Load();
        RebuildRegistries();
    }

    public static void Reload()
    {
        HookRegistry.Reset();
        PluginManager.Reload();
        RebuildRegistries();
    }

    private static void RebuildRegistries()
    {
        var assemblies = GetExtensionAssemblies();
        RuntimeState.Commands.LoadFromAssemblies(assemblies);
        RuntimeState.CustomPackets.LoadFromAssemblies(assemblies);
        Logs.Info($"Registered {RuntimeState.Commands.Count} command(s) and {RuntimeState.CustomPackets.Count} custom packet(s).");
    }

    private static Assembly[] GetExtensionAssemblies()
        => [Assembly.GetExecutingAssembly(), .. PluginManager.LoadedAssemblies];
}
