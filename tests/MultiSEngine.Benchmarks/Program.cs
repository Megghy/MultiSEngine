using BenchmarkDotNet.Running;
using MultiSEngine.Benchmarks;

if (await ScenarioLoadReporter.TryRunAsync(args).ConfigureAwait(false))
    return;

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(args);
