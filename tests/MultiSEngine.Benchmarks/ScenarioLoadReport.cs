using System.Diagnostics;

namespace MultiSEngine.Benchmarks;

internal static class ScenarioLoadReporter
{
    private static readonly LoadScenario[] Scenarios =
    [
        new("16 players / 1 server", 16, 1),
        new("16 players / 4 servers", 16, 4),
        new("64 players / 1 server", 64, 1),
        new("64 players / 4 servers", 64, 4),
    ];

    public static async Task<bool> TryRunAsync(string[] args)
    {
        if (!args.Contains("--load-report", StringComparer.OrdinalIgnoreCase))
            return false;

        var duration = ResolveDuration(args);
        var scenarios = ResolveScenarios(args);
        Console.WriteLine("# MultiSEngine Load Report");
        Console.WriteLine();
        Console.WriteLine($"Duration per scenario: {duration.TotalSeconds:0} s");
        Console.WriteLine($"Logical cores: {Environment.ProcessorCount}");
        Console.WriteLine();
        Console.WriteLine("| Scenario | Player sync/s | Terraria packets/s | Avg CPU | Peak working set | Peak managed heap |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");

        foreach (var scenario in scenarios)
        {
            var result = await RunScenarioAsync(scenario, duration).ConfigureAwait(false);
            Console.WriteLine(
                $"| {scenario.Name} | {FormatRate(result.PlayerSyncsPerSecond)} | {FormatRate(result.PacketsPerSecond)} | {FormatCpu(result.AverageCpuCores, result.AverageCpuPercent)} | {FormatMiB(result.PeakWorkingSetBytes)} | {FormatMiB(result.PeakManagedHeapBytes)} |");
        }

        return true;
    }

    private static async Task<LoadScenarioResult> RunScenarioAsync(LoadScenario scenario, TimeSpan duration)
    {
        await using var cluster = await PlayerSyncClusterHarness.CreateAsync(scenario.Players, scenario.Servers).ConfigureAwait(false);

        for (var i = 0; i < 2; i++)
            await cluster.SyncAllAsync().ConfigureAwait(false);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var sampler = new ResourceSampler();
        using var cts = new CancellationTokenSource();
        var samplingTask = sampler.SampleAsync(cts.Token);

        var cpuStart = ReadCpuTime();
        var stopwatch = Stopwatch.StartNew();
        long rounds = 0;
        while (stopwatch.Elapsed < duration)
        {
            await cluster.SyncAllAsync().ConfigureAwait(false);
            rounds++;
        }

        stopwatch.Stop();
        var cpuEnd = ReadCpuTime();
        cts.Cancel();
        await samplingTask.ConfigureAwait(false);

        var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        // player-sync 是“单个玩家的一整套同步”，packet/s 则按这套同步里实际发送的底层包数折算。
        var packetsPerSecond = rounds * cluster.ExpectedPacketsPerRound / elapsedSeconds;
        var cpuSeconds = (cpuEnd - cpuStart).TotalSeconds;
        var averageCpuCores = cpuSeconds / elapsedSeconds;
        var averageCpuPercent = averageCpuCores / Environment.ProcessorCount * 100d;

        return new LoadScenarioResult(
            rounds * scenario.Players / elapsedSeconds,
            packetsPerSecond,
            averageCpuCores,
            averageCpuPercent,
            sampler.PeakWorkingSetBytes,
            sampler.PeakManagedHeapBytes);
    }

    private static TimeSpan ResolveDuration(string[] args)
    {
        const double defaultSeconds = 10;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--duration-seconds", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!double.TryParse(args[i + 1], out var seconds) || seconds <= 0)
                break;

            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(defaultSeconds);
    }

    private static IReadOnlyList<LoadScenario> ResolveScenarios(string[] args)
    {
        var players = ResolveIntArgument(args, "--players");
        var servers = ResolveIntArgument(args, "--servers");
        if (players is null && servers is null)
            return Scenarios;

        if (players is null || servers is null)
            throw new ArgumentException("Both --players and --servers must be provided together.");

        for (var i = 0; i < Scenarios.Length; i++)
        {
            if (Scenarios[i].Players == players.Value && Scenarios[i].Servers == servers.Value)
                return [Scenarios[i]];
        }

        throw new ArgumentException($"Unsupported scenario: players={players}, servers={servers}.");
    }

    private static int? ResolveIntArgument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!int.TryParse(args[i + 1], out var value) || value <= 0)
                throw new ArgumentException($"Invalid value for {name}: {args[i + 1]}");

            return value;
        }

        return null;
    }

    private static TimeSpan ReadCpuTime()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.TotalProcessorTime;
    }

    private static string FormatRate(double value)
        => $"{Math.Round(value):N0}";

    private static string FormatCpu(double cores, double percent)
        => $"{cores:F2} cores ({percent:F1}%)";

    private static string FormatMiB(long bytes)
        => $"{bytes / 1024d / 1024d:F1} MiB";

    private readonly record struct LoadScenario(string Name, int Players, int Servers);

    private readonly record struct LoadScenarioResult(
        double PlayerSyncsPerSecond,
        double PacketsPerSecond,
        double AverageCpuCores,
        double AverageCpuPercent,
        long PeakWorkingSetBytes,
        long PeakManagedHeapBytes);

    private sealed class ResourceSampler
    {
        private const int SampleIntervalMs = 200;

        public long PeakWorkingSetBytes { get; private set; }

        public long PeakManagedHeapBytes { get; private set; }

        public async Task SampleAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Sample();

                try
                {
                    await Task.Delay(SampleIntervalMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            Sample();
        }

        private void Sample()
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            PeakWorkingSetBytes = Math.Max(PeakWorkingSetBytes, process.WorkingSet64);
            PeakManagedHeapBytes = Math.Max(PeakManagedHeapBytes, GC.GetGCMemoryInfo().HeapSizeBytes);
        }
    }
}
