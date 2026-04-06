using System.Diagnostics;

namespace MultiSEngine.IntegrationTests.Support;

internal sealed class TShockServerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly StringWriter _capturedOutput = new();

    public TShockServerProcess(string repositoryPath, string runtimePath, int port)
    {
        var dllPath = GetLauncherDllPath(repositoryPath);
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"Unable to locate TShock launcher build output: {dllPath}");
        }

        Directory.CreateDirectory(runtimePath);
        Directory.CreateDirectory(Path.Combine(runtimePath, "worlds"));

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = runtimePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        _process.StartInfo.ArgumentList.Add(dllPath);
        _process.StartInfo.ArgumentList.Add("-port");
        _process.StartInfo.ArgumentList.Add(port.ToString());
        _process.StartInfo.ArgumentList.Add("-maxplayers");
        _process.StartInfo.ArgumentList.Add("8");
        _process.StartInfo.ArgumentList.Add("-world");
        _process.StartInfo.ArgumentList.Add(Path.Combine(runtimePath, "worlds", "codex-live.wld"));
        _process.StartInfo.ArgumentList.Add("-autocreate");
        _process.StartInfo.ArgumentList.Add("1");
        _process.StartInfo.ArgumentList.Add("-worldname");
        _process.StartInfo.ArgumentList.Add("CodexLiveTest");
        _process.StartInfo.ArgumentList.Add("-noupnp");

        _process.OutputDataReceived += (_, args) => HandleOutput(args.Data);
        _process.ErrorDataReceived += (_, args) => HandleOutput(args.Data);
        _process.Exited += (_, _) =>
        {
            if (_ready.Task.IsCompleted)
            {
                return;
            }

            _ready.TrySetException(new InvalidOperationException(
                $"TShock exited before startup completed.{Environment.NewLine}{Output}"));
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public string Output => _capturedOutput.ToString();

    public async Task WaitUntilReadyAsync(TimeSpan timeout)
        => await _ready.Task.WaitAsync(timeout);

    private void HandleOutput(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (_capturedOutput)
        {
            _capturedOutput.WriteLine(line);
        }

        if (line.Contains("正在侦听端口", StringComparison.Ordinal)
            || line.Contains("Listening on port", StringComparison.OrdinalIgnoreCase)
            || line.Contains("服务器已启动", StringComparison.Ordinal)
            || line.Contains("Server started", StringComparison.OrdinalIgnoreCase))
        {
            _ready.TrySetResult();
        }
    }

    private static string GetLauncherDllPath(string repositoryPath)
    {
        var binRoot = Path.Combine(repositoryPath, "TShockLauncher", "bin");
        var candidates = new[]
        {
            Path.Combine(binRoot, "Debug", "net9.0", "TShock.Server.dll"),
            Path.Combine(binRoot, "Release", "net9.0", "TShock.Server.dll"),
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
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
        _capturedOutput.Dispose();
    }
}
