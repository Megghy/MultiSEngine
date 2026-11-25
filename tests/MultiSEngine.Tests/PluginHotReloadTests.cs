using System.Runtime.CompilerServices;
using MultiSEngine.Core.Configuration;
using MultiSEngine.Runtime.Hosting;

namespace MultiSEngine.Tests;

public sealed class PluginHotReloadTests
{
    [Fact]
    public async Task PluginHost_LoadsInvokesAndTrulyUnloadsCollectibleAssemblyLoadContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mse-plugin-test-{Guid.NewGuid():N}");
        var pluginsDir = Path.Combine(tempDir, "Plugins");
        Directory.CreateDirectory(pluginsDir);

        try
        {
            var sourceDll = FindTestPluginDll();
            File.Copy(sourceDll, Path.Combine(pluginsDir, Path.GetFileName(sourceDll)), overwrite: true);

            await using var host = new MultiSEngineHost(new MultiSEngineHostOptions
            {
                WorkingDirectory = tempDir,
                InitialConfig = new ProxyConfig(),
                EnableConsoleLogging = false,
                EnableFileLogging = false,
                EnableTcpListener = false,
                EnablePlugins = true,
            });

            await host.StartAsync();

            var weakRefs = await LoadVerifyAndUnloadAsync(host);

            Assert.Single(weakRefs);
            for (var attempt = 0; attempt < 20 && weakRefs[0].IsAlive; attempt++)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                await Task.Delay(25);
            }

            Assert.False(weakRefs[0].IsAlive, "Collectible PluginLoadContext was leaked and not garbage-collected after unload.");

            // After unload, plugin command must no longer be registered.
            var afterUnload = await host.ExecuteConsoleCommandAsync("pluginping");
            Assert.False(afterUnload.Handled);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<IReadOnlyList<WeakReference>> LoadVerifyAndUnloadAsync(MultiSEngineHost host)
    {
        var loaded = host.Plugins.GetLoadedPlugins();
        Assert.Single(loaded);
        Assert.Equal("sample.reloadable", loaded[0].Metadata.Id);

        // Plugin-registered console command must work and must not be swallowed or cleared.
        var result = await host.ExecuteConsoleCommandAsync("pluginping");
        Assert.True(result.Handled);

        // Reload must cleanly unload old ALC and load fresh instance.
        var firstUnloadRefs = await host.Plugins.ReloadAllAsync();
        Assert.Single(firstUnloadRefs);
        Assert.Single(host.Plugins.GetLoadedPlugins());

        var afterReload = await host.ExecuteConsoleCommandAsync("pluginping");
        Assert.True(afterReload.Handled);

        return await host.Plugins.UnloadAllAsync();
    }

    private static string FindTestPluginDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MultiSEngine.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);

#if DEBUG
        const string primaryConfig = "Debug";
        const string fallbackConfig = "Release";
#else
        const string primaryConfig = "Release";
        const string fallbackConfig = "Debug";
#endif

        var primary = Path.Combine(dir!.FullName, "tests", "MultiSEngine.TestPlugin", "bin", primaryConfig, "net10.0", "MultiSEngine.TestPlugin.dll");
        if (File.Exists(primary))
            return primary;

        var fallback = Path.Combine(dir.FullName, "tests", "MultiSEngine.TestPlugin", "bin", fallbackConfig, "net10.0", "MultiSEngine.TestPlugin.dll");
        Assert.True(File.Exists(fallback), $"Could not locate MultiSEngine.TestPlugin.dll at '{primary}' or '{fallback}'.");
        return fallback;
    }
}
