using System.Reflection;
using System.Runtime.InteropServices;
using MultiSEngine.Runtime.Hosting;

namespace MultiSEngine;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        PrintBanner();

        using var shutdownCts = new CancellationTokenSource();
        using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
        {
            ctx.Cancel = true;
            shutdownCts.Cancel();
        });
        using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;
            shutdownCts.Cancel();
        });

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
        };

        await using var host = new MultiSEngineHost(new MultiSEngineHostOptions
        {
            WorkingDirectory = Environment.CurrentDirectory,
            EnableConsoleLogging = true,
            EnableFileLogging = true,
            EnableTcpListener = true,
            EnablePlugins = true,
        });

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            host.Logger.Error(
                e.ExceptionObject is Exception ex ? $"Unhandled AppDomain exception: {ex.Message}" : $"Unhandled error: {e.ExceptionObject}",
                e.ExceptionObject as Exception,
                "Fatal");
        };

        try
        {
            await host.StartAsync(shutdownCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            host.Logger.Error("MultiSEngine failed to start.", ex, "Host");
            return 1;
        }

        using var titleCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownCts.Token);
        var titleTask = RunConsoleTitleUpdaterAsync(host, titleCts.Token);

        try
        {
            await RunConsoleLoopAsync(host, shutdownCts).ConfigureAwait(false);
        }
        finally
        {
            await titleCts.CancelAsync().ConfigureAwait(false);
            try { await titleTask.ConfigureAwait(false); } catch { }
            await host.StopAsync().ConfigureAwait(false);
        }

        Console.WriteLine("Bye!");
        return 0;
    }

    private static async Task RunConsoleLoopAsync(MultiSEngineHost host, CancellationTokenSource shutdownCts)
    {
        while (!shutdownCts.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await Console.In.ReadLineAsync(shutdownCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Standard input reached EOF (non-interactive daemon / systemd / container): park cleanly until signal.
            if (line is null)
            {
                host.Logger.Info("Standard input is closed (headless mode). Waiting for SIGINT/SIGTERM.", "Host");
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, shutdownCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                break;
            }

            var result = await host.ExecuteConsoleCommandAsync(line, shutdownCts.Token).ConfigureAwait(false);
            if (result.RequestShutdown)
            {
                await shutdownCts.CancelAsync().ConfigureAwait(false);
                break;
            }
        }
    }

    private static async Task RunConsoleTitleUpdaterAsync(MultiSEngineHost host, CancellationToken cancellationToken)
    {
        if (Console.IsOutputRedirected || !OperatingSystem.IsWindows())
            return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                var cfg = host.ConfigStore.Current;
                var online = host.Sessions.Count;
                try
                {
                    Console.Title = $"MultiSEngine v2 | {cfg.ServerName} | Online: {online} | Port: {host.BoundEndPoint?.Port ?? cfg.ListenPort}";
                }
                catch
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void PrintBanner()
    {
        Console.WriteLine("""

        __  ___      ____  _ _____ ______            _
       /  |/  /_  __/ / /_(_) ___// ____/___  ____ _(_)___  ___
      / /|_/ / / / / / __/ /\__ \/ __/ / __ \/ __ `/ / __ \/ _ \
     / /  / / /_/ / / /_/ /___/ / /___/ / / / /_/ / / / / /  __/
    /_/  /_/\__,_/_/\__/_//____/_____/_/ /_/\__, /_/_/ /_/\___/
                                           /____/
""");
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Console.WriteLine($"-> MultiSEngine v{version} (.NET 10)");
        Console.WriteLine();
    }
}
