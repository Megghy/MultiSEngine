using System.Text;
using System.Threading.Channels;
using MultiSEngine.Core.Logging;

namespace MultiSEngine.Runtime.Infrastructure;

public sealed class AsyncProxyLogger : IProxyLogger, IAsyncDisposable
{
    private readonly record struct LogEntry(DateTimeOffset Timestamp, ProxyLogLevel Level, string Category, string Message, Exception? Exception);

    private readonly string? _logDirectory;
    private readonly bool _enableConsole;
    private readonly Channel<LogEntry>? _fileChannel;
    private readonly Lock _consoleLock = new();
    private readonly Task? _writerTask;
    private int _disposed;

    public AsyncProxyLogger(string? logDirectory, bool enableConsole = true, int channelCapacity = 4096)
    {
        _logDirectory = logDirectory;
        _enableConsole = enableConsole;

        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
            _fileChannel = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(channelCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false,
            });
            _writerTask = Task.Run(WriteLoopAsync);
        }
    }

    public void Log(ProxyLogLevel level, string category, string message, Exception? exception = null)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, category, message, exception);

        if (_enableConsole)
            WriteConsole(entry);

        if (_fileChannel is not null && Volatile.Read(ref _disposed) == 0)
            _fileChannel.Writer.TryWrite(entry);
    }

    private void WriteConsole(in LogEntry entry)
    {
        var (tag, color) = entry.Level switch
        {
            ProxyLogLevel.Debug => ("Debug", ConsoleColor.DarkGray),
            ProxyLogLevel.Text => ("Log", ConsoleColor.Gray),
            ProxyLogLevel.Info => ("Info", ConsoleColor.DarkCyan),
            ProxyLogLevel.Success => ("Success", ConsoleColor.Green),
            ProxyLogLevel.Warn => ("Warn", ConsoleColor.Yellow),
            ProxyLogLevel.Error => ("Error", ConsoleColor.Red),
            _ => ("Log", ConsoleColor.Gray),
        };

        var line = FormatLine(entry, tag);
        lock (_consoleLock)
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    var prev = Console.ForegroundColor;
                    Console.ForegroundColor = color;
                    Console.WriteLine(line);
                    Console.ForegroundColor = prev;
                }
                else
                {
                    Console.WriteLine(line);
                }
            }
            catch
            {
                // Ignore console errors on detached hosts.
            }
        }
    }

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        string? activeDate = null;

        try
        {
            await foreach (var entry in _fileChannel!.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var dateKey = entry.Timestamp.ToString("yyyy-MM-dd");
                var formatted = FormatLine(entry, entry.Level.ToString());

                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (writer is null || !string.Equals(activeDate, dateKey, StringComparison.Ordinal))
                        {
                            if (writer is not null)
                                await writer.DisposeAsync().ConfigureAwait(false);

                            Directory.CreateDirectory(_logDirectory!);
                            var filePath = Path.Combine(_logDirectory!, $"{dateKey}.log");
                            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
                            writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false };
                            activeDate = dateKey;
                        }

                        await writer.WriteLineAsync(formatted).ConfigureAwait(false);
                        if (_fileChannel.Reader.Count == 0)
                            await writer.FlushAsync().ConfigureAwait(false);
                        break;
                    }
                    catch
                    {
                        if (writer is not null)
                        {
                            try { await writer.DisposeAsync().ConfigureAwait(false); } catch { }
                            writer = null;
                            activeDate = null;
                        }

                        if (attempt < 2)
                            await Task.Delay(50 * (attempt + 1)).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            if (writer is not null)
            {
                try
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }
    }

    private static string FormatLine(in LogEntry entry, string levelTag)
    {
        var prefix = $"{entry.Timestamp:HH:mm:ss} [{levelTag}] [{entry.Category}] {entry.Message}";
        return entry.Exception is null ? prefix : $"{prefix}{Environment.NewLine}{entry.Exception}";
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _fileChannel?.Writer.TryComplete();
        if (_writerTask is not null)
        {
            try
            {
                await _writerTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
