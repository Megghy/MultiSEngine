using BenchmarkDotNet.Running;

namespace MultiSEngine.Benchmarks;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Contains("--load-report", StringComparer.OrdinalIgnoreCase))
        {
            await ProxyLoadReport.RunAsync().ConfigureAwait(false);
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
