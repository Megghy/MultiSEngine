using System.Diagnostics;
using System.Text;

namespace MultiSEngine.IntegrationTests.Support;

internal sealed class TestAgentProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _clientConnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringBuilder _output = new();

    public TestAgentProcess(string workingDirectory, string configPath)
    {
        var dllPath = GetAgentDllPath();
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"Unable to locate TrProtocol.TestAgent build output: {dllPath}");
        }

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };
        _process.StartInfo.ArgumentList.Add(dllPath);
        _process.StartInfo.ArgumentList.Add("--config");
        _process.StartInfo.ArgumentList.Add(configPath);

        _process.OutputDataReceived += (_, args) => HandleOutput(args.Data);
        _process.ErrorDataReceived += (_, args) => HandleOutput(args.Data);
        _process.Exited += (_, _) =>
        {
            var message = $"TrProtocol.TestAgent exited unexpectedly. Output:{Environment.NewLine}{Output}";
            _ready.TrySetException(new InvalidOperationException(message));
            _clientConnected.TrySetException(new InvalidOperationException(message));
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public string Output
    {
        get
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }
    }

    public async Task WaitUntilReadyAsync(TimeSpan timeout)
        => await _ready.Task.WaitAsync(timeout);

    public async Task WaitForClientConnectionAsync(TimeSpan timeout)
        => await _clientConnected.Task.WaitAsync(timeout);

    private void HandleOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }

        if (line.Contains("[Proxy] Listening on port", StringComparison.Ordinal))
        {
            _ready.TrySetResult();
        }

        if (line.Contains("[Proxy] Client connected:", StringComparison.Ordinal))
        {
            _clientConnected.TrySetResult();
        }
    }

    private static string GetAgentDllPath()
    {
        var root = FindRepositoryRoot();
#if DEBUG
        const string configuration = "Debug";
        const string alternateConfiguration = "Release";
#else
        const string configuration = "Release";
        const string alternateConfiguration = "Debug";
#endif
        var binRoot = Path.Combine(
            root,
            "external",
            "TrProtocol",
            "src",
            "TrProtocol.TestAgent",
            "bin");

        var primaryPath = Path.Combine(binRoot, configuration, "net9.0", "TrProtocol.TestAgent.dll");
        if (File.Exists(primaryPath))
        {
            return primaryPath;
        }

        var alternatePath = Path.Combine(binRoot, alternateConfiguration, "net9.0", "TrProtocol.TestAgent.dll");
        if (File.Exists(alternatePath))
        {
            return alternatePath;
        }

        return primaryPath;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MultiSEngine.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "MultiSEngine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Unable to locate repository root from {AppContext.BaseDirectory}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        _process.Dispose();
    }
}
