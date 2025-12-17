using System.Collections.Concurrent;
using MultiSEngine.DataStruct;

namespace MultiSEngine
{
    public class Logs
    {
        public static string LogPath => Path.Combine(Environment.CurrentDirectory, "Logs");
        public static string LogName => Path.Combine(LogPath, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
        public const ConsoleColor DefaultColor = ConsoleColor.Gray;
        public static void Text(object text, bool save = true)
        {
            LogAndSave(text, "[Log]", DefaultColor, save);
        }
        public static void Info(object text, bool save = true)
        {
            LogAndSave(text, "[Info]", ConsoleColor.Yellow, save);
        }
        public static void Error(object text, bool save = true)
        {
            LogAndSave(text, "[Error]", ConsoleColor.Red, save);
        }
        public static void Warn(object text, bool save = true)
        {
            LogAndSave(text, "[Warn]", ConsoleColor.DarkYellow, save);
        }
        public static void Success(object text, bool save = true)
        {
            LogAndSave(text, "[Success]", ConsoleColor.Green, save);
        }
        internal static void Init()
        {
            if (!Directory.Exists(LogPath))
                Directory.CreateDirectory(LogPath);
        }
        private static readonly System.Threading.Channels.Channel<string> _channel = System.Threading.Channels.Channel.CreateUnbounded<string>(new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        private static Task _logLoopTask;
        [AutoInit]
        private static void SaveLogTask()
        {
            _logLoopTask = SaveLogLoopAsync();
        }
        private static async Task SaveLogLoopAsync()
        {
            string currentDate = null;
            FileStream fs = null;
            StreamWriter writer = null;
            try
            {
                while (true)
                {
                    var line = await _channel.Reader.ReadAsync().ConfigureAwait(false);
                    var today = DateTime.Now.ToString("yyyy-MM-dd");
                    if (!string.Equals(currentDate, today, StringComparison.Ordinal))
                    {
                        writer?.Flush();
                        writer?.Dispose();
                        fs?.Dispose();
                        if (!Directory.Exists(LogPath))
                            Directory.CreateDirectory(LogPath);
                        fs = new FileStream(LogName, FileMode.Append, FileAccess.Write, FileShare.Read, 8192, FileOptions.Asynchronous);
                        writer = new StreamWriter(fs);
                        currentDate = today;
                    }

                    writer.WriteLine(line);
                    int drained = 0;
                    while (drained < 1024 && _channel.Reader.TryRead(out var extra))
                    {
                        writer.WriteLine(extra);
                        drained++;
                    }
                    await writer.FlushAsync().ConfigureAwait(false);
                }
            }
            catch
            {
            }
            finally
            {
                try { writer?.Flush(); } catch { }
                writer?.Dispose();
                fs?.Dispose();
            }
        }
        public static void LogAndSave(object message, string prefix = "[Log]", ConsoleColor color = DefaultColor, bool save = true)
        {
            Console.ForegroundColor = color;
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {prefix} {message}");
            Console.ForegroundColor = DefaultColor;
            if (save)
                _channel.Writer.TryWrite($"{DateTime.Now:HH:mm:ss} - {prefix} {message}");
        }
    }
}
